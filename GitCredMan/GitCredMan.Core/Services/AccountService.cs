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
    private readonly ICredentialStore  _store;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<AccountService> _log;

    public AccountService(
        ICredentialStore  store,
        ISettingsRepository settings,
        ILogger<AccountService> log)
    {
        _store    = store;
        _settings = settings;
        _log      = log;
    }

    // ── Add ───────────────────────────────────────────────────

    public OperationResult Add(AppSettings s, Account account, string? token)
    {
        if (s.Accounts.Count >= 64)
            return OperationResult.Fail("Maximum of 64 accounts reached.");

        if (string.IsNullOrWhiteSpace(account.Name))
            return OperationResult.Fail("Account name is required.");

        if (string.IsNullOrWhiteSpace(account.Username))
            return OperationResult.Fail("Username is required.");

        if (!token_provided_or_editing(token, isNew: true))
            return OperationResult.Fail("A Personal Access Token is required for new accounts.");

        s.Accounts.Add(account);

        if (s.Accounts.Count == 1)
            SetDefault(s, account.Id);   // auto-default the first account

        if (!string.IsNullOrEmpty(token))
            _store.Save(account.Id, token);

        RefreshHasToken(s);
        _settings.Save(s);
        _log.LogInformation("Account '{name}' added.", account.Name);
        return OperationResult.Ok();
    }

    // ── Update ────────────────────────────────────────────────

    public OperationResult Update(AppSettings s, Account updated, string? newToken)
    {
        var existing = s.Accounts.FirstOrDefault(a => a.Id == updated.Id);
        if (existing is null)
            return OperationResult.Fail("Account not found.");

        existing.Name     = updated.Name;
        existing.Username = updated.Username;
        existing.Email    = updated.Email;
        existing.Host     = updated.Host;

        if (!string.IsNullOrEmpty(newToken))
            _store.Save(existing.Id, newToken);

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

        // Require explicit deletion (no accidental logout path)
        _store.Delete(accountId);

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

    /// <summary>Load the stored token for an account (may return null).</summary>
    public string? GetToken(string accountId) => _store.Load(accountId);

    public void RefreshHasToken(AppSettings s)
    {
        foreach (var a in s.Accounts)
            a.HasStoredToken = _store.Exists(a.Id);
    }

    private static bool token_provided_or_editing(string? token, bool isNew) =>
        !isNew || !string.IsNullOrEmpty(token);
}
