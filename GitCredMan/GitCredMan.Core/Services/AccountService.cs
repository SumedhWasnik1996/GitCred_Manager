using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitCredMan.Core.Services;

/// <summary>
/// Domain service: manages the Account collection and coordinates
/// with the credential store. This is the single source of truth
/// for account business logic — ViewModels call this, not raw storage.
/// </summary>
public sealed class AccountService
{
    private readonly ICredentialStore _store;
    private readonly ISettingsRepository _settings;
    private readonly OAuthService _oauth;
    private readonly ILogger<AccountService> _log;

    public AccountService(
        ICredentialStore store,
        ISettingsRepository settings,
        OAuthService oauth,
        ILogger<AccountService> log)
    {
        _store = store;
        _settings = settings;
        _oauth = oauth;
        _log = log;
    }

    // ── Add (PAT) ─────────────────────────────────────────────

    public OperationResult Add(AppSettings s, Account account, string? token)
    {
        if (s.Accounts.Count >= 64)
            return OperationResult.Fail("Maximum of 64 accounts reached.");

        if (string.IsNullOrWhiteSpace(account.Name))
            return OperationResult.Fail("Account name is required.");

        if (string.IsNullOrWhiteSpace(account.Username))
            return OperationResult.Fail("Username is required.");

        if (!TokenProvidedOrEditing(token, isNew: true))
            return OperationResult.Fail("A Personal Access Token is required for new accounts.");

        s.Accounts.Add(account);

        if (s.Accounts.Count == 1)
            SetDefault(s, account.Id);   // auto-default the first account

        if (!string.IsNullOrEmpty(token))
            _store.Save(account.CredentialKey, token);

        RefreshHasToken(s);
        _settings.Save(s);
        _log.LogInformation("Account '{name}' added (PAT).", account.Name);
        return OperationResult.Ok();
    }

    // ── Add (OAuth) ───────────────────────────────────────────

    /// <summary>
    /// Save a new account whose tokens were obtained via the OAuth device flow.
    /// Both the access token and (if present) the refresh token are persisted
    /// as separate entries in Windows Credential Manager.
    /// </summary>
    public OperationResult AddOAuth(AppSettings s, Account account,
        string accessToken, string? refreshToken,
        string? username = null, string? email = null)
    {
        if (s.Accounts.Count >= 64)
            return OperationResult.Fail("Maximum of 64 accounts reached.");

        if (string.IsNullOrWhiteSpace(account.Name))
            return OperationResult.Fail("Account label is required.");

        // Populate identity from OAuth user-info response if provided
        if (!string.IsNullOrEmpty(username)) account.Username = username;
        if (!string.IsNullOrEmpty(email)) account.Email = email;

        account.AuthMethod = AuthMethod.OAuth;
        s.Accounts.Add(account);

        if (s.Accounts.Count == 1)
            SetDefault(s, account.Id);

        _store.Save(account.CredentialKey, accessToken);

        if (!string.IsNullOrEmpty(refreshToken) && account.RefreshTokenKey is not null)
            _store.Save(account.RefreshTokenKey, refreshToken);

        RefreshHasToken(s);
        _settings.Save(s);
        _log.LogInformation("Account '{name}' added (OAuth).", account.Name);
        return OperationResult.Ok();
    }

    // ── Update ────────────────────────────────────────────────

    public OperationResult Update(AppSettings s, Account updated, string? newToken)
    {
        var existing = s.Accounts.FirstOrDefault(a => a.Id == updated.Id);
        if (existing is null)
            return OperationResult.Fail("Account not found.");

        existing.Name = updated.Name;
        existing.Username = updated.Username;
        existing.Email = updated.Email;
        existing.Host = updated.Host;

        if (!string.IsNullOrEmpty(newToken))
            _store.Save(existing.CredentialKey, newToken);

        RefreshHasToken(s);
        _settings.Save(s);
        _log.LogInformation("Account '{name}' updated.", existing.Name);
        return OperationResult.Ok();
    }

    // ── Delete ────────────────────────────────────────────────

    public OperationResult Delete(AppSettings s, string accountId)
    {
        var account = s.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return OperationResult.Fail("Account not found.");

        _store.Delete(account.CredentialKey);

        // Also remove the refresh token if this was an OAuth account
        if (account.RefreshTokenKey is not null)
            _store.Delete(account.RefreshTokenKey);

        // Detach repos from this account → they fall back to default
        foreach (var r in s.Repositories.Where(r => r.AccountId == accountId))
            r.AccountId = null;

        bool wasDefault = account.IsDefault;
        s.Accounts.Remove(account);

        if (wasDefault && s.Accounts.Count > 0)
            SetDefault(s, s.Accounts[0].Id);
        else if (s.Accounts.Count == 0)
            s.DefaultAccountId = null;

        _settings.Save(s);
        _log.LogInformation("Account '{name}' deleted.", account.Name);
        return OperationResult.Ok();
    }

    // ── Set default ───────────────────────────────────────────

    public void SetDefault(AppSettings s, string accountId)
    {
        foreach (var a in s.Accounts)
            a.IsDefault = a.Id == accountId;
        s.DefaultAccountId = accountId;
        _settings.Save(s);
    }

    // ── Resolve effective account for a repository ────────────

    public Account? Resolve(AppSettings s, Repository repo)
    {
        if (!string.IsNullOrEmpty(repo.AccountId))
        {
            var specific = s.Accounts.FirstOrDefault(a => a.Id == repo.AccountId);
            if (specific is not null) return specific;
        }
        return s.Accounts.FirstOrDefault(a => a.Id == s.DefaultAccountId)
            ?? s.Accounts.FirstOrDefault();
    }

    // ── Token helpers ─────────────────────────────────────────

    /// <summary>
    /// Load the stored access token for an account.
    /// For OAuth accounts, automatically attempts a token refresh if a
    /// refresh token is available and the access token appears missing/expired.
    /// Returns null if no valid token can be obtained.
    /// </summary>
    public string? GetToken(string accountId, AppSettings s)
    {
        var account = s.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return null;

        var token = _store.Load(account.CredentialKey);

        // For OAuth accounts, try refreshing if the access token is missing
        if (string.IsNullOrEmpty(token) && account.AuthMethod == AuthMethod.OAuth)
        {
            token = TryRefreshOAuthToken(account);
        }

        return token;
    }

    /// <summary>Legacy overload — used by callers that don't have AppSettings.</summary>
    public string? GetToken(string accountId) => _store.Load(accountId);

    private string? TryRefreshOAuthToken(Account account)
    {
        if (account.RefreshTokenKey is null) return null;

        var refreshToken = _store.Load(account.RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken)) return null;

        var provider = OAuthProvider.For(account.Host);
        if (provider is null)
        {
            _log.LogWarning("No OAuth provider registered for host '{host}'.", account.Host);
            return null;
        }

        _log.LogInformation("Access token missing for '{name}' — attempting OAuth refresh.", account.Name);

        try
        {
            // Run synchronously here because this is called from a sync path.
            // Callers that can go async should call TryRefreshOAuthTokenAsync instead.
            var result = _oauth.RefreshAccessTokenAsync(provider, refreshToken)
                .GetAwaiter().GetResult();

            if (!result.Success)
            {
                _log.LogWarning("OAuth refresh failed for '{name}': {err}", account.Name, result.Error);
                return null;
            }

            _store.Save(account.CredentialKey, result.AccessToken!);

            if (result.RefreshToken is not null)
                _store.Save(account.RefreshTokenKey, result.RefreshToken);

            _log.LogInformation("OAuth token refreshed for '{name}'.", account.Name);
            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Exception during OAuth token refresh for '{name}'.", account.Name);
            return null;
        }
    }

    /// <summary>
    /// Async version — prefer this when calling from an async context (e.g. ApplyAll).
    /// Returns the valid access token, refreshing it if needed.
    /// </summary>
    public async Task<string?> GetTokenAsync(Account account)
    {
        var token = _store.Load(account.CredentialKey);

        if (string.IsNullOrEmpty(token) && account.AuthMethod == AuthMethod.OAuth)
        {
            if (account.RefreshTokenKey is not null)
            {
                var refreshToken = _store.Load(account.RefreshTokenKey);
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    var provider = OAuthProvider.For(account.Host);
                    if (provider is not null)
                    {
                        var result = await _oauth.RefreshAccessTokenAsync(provider, refreshToken);
                        if (result.Success)
                        {
                            _store.Save(account.CredentialKey, result.AccessToken!);
                            if (result.RefreshToken is not null)
                                _store.Save(account.RefreshTokenKey, result.RefreshToken);
                            token = result.AccessToken;
                        }
                    }
                }
            }
        }

        return token;
    }

    public void RefreshHasToken(AppSettings s)
    {
        foreach (var a in s.Accounts)
            a.HasStoredToken = _store.Exists(a.CredentialKey);
    }

    /// <summary>
    /// Explicitly save a refresh token for an OAuth account.
    /// Called from the VM after an edit that produced new tokens.
    /// </summary>
    public void SaveRefreshToken(Account account, string refreshToken)
    {
        if (account.RefreshTokenKey is not null)
            _store.Save(account.RefreshTokenKey, refreshToken);
    }

    private static bool TokenProvidedOrEditing(string? token, bool isNew) =>
        !isNew || !string.IsNullOrEmpty(token);
}