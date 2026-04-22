using CommunityToolkit.Mvvm.ComponentModel;
using GitCredMan.Core.Models;

namespace GitCredMan.App.ViewModels;

public partial class AccountDialogViewModel : ObservableObject
{
    // ── Form fields ───────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _host = "github.com";

    // ── Edit state ────────────────────────────────────────────

    [ObservableProperty] private bool _isEditMode = false;

    public string? OriginalId { get; private set; }

    // ── OAuth state ───────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private bool _isOAuthPending = false;

    [ObservableProperty] private string _oAuthStatus = string.Empty;
    [ObservableProperty] private string _oAuthUserCode = string.Empty;
    [ObservableProperty] private string _oAuthVerificationUri = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private bool _oAuthCompleted = false;

    /// <summary>Collected access token — read by the view after dialog closes.</summary>
    public string? OAuthAccessToken { get; set; }

    /// <summary>Collected refresh token — may be null if provider doesn't issue one.</summary>
    public string? OAuthRefreshToken { get; set; }

    // ── Validation ────────────────────────────────────────────

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Host) &&
        (OAuthCompleted || IsEditMode);

    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Account label is required.";
            if (string.IsNullOrWhiteSpace(Host)) return "Host is required.";
            if (!IsEditMode && !OAuthCompleted) return "Complete the browser sign-in before saving.";
            return null;
        }
    }

    // ── Called by the view ────────────────────────────────────

    /// <summary>Called by the dialog code-behind when OAuth completes successfully.</summary>
    public void NotifyOAuthCompleted(string accessToken, string? refreshToken,
        string? username, string? email)
    {
        OAuthAccessToken = accessToken;
        OAuthRefreshToken = refreshToken;
        OAuthCompleted = true;
        IsOAuthPending = false;
        OAuthStatus = "✓ Signed in successfully";

        // Auto-fill Name from username if the user left it blank
        if (string.IsNullOrWhiteSpace(Name) && !string.IsNullOrEmpty(username))
            Name = username;

        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationError));
    }

    // ── Load existing account for editing ─────────────────────

    public void LoadFrom(Account account)
    {
        OriginalId = account.Id;
        IsEditMode = true;
        Name = account.Name;
        Host = account.Host;
        // When editing an existing OAuth account, treat it as already signed in
        OAuthCompleted = true;
        OAuthStatus = account.HasStoredToken
            ? "✓ Token already stored — sign in again to replace it."
            : "No token stored — click Sign in to re-authenticate.";
    }

    // ── Build the Account model ───────────────────────────────

    public Account BuildAccount() => new()
    {
        Id = OriginalId ?? Guid.NewGuid().ToString("D"),
        Name = Name.Trim(),
        // Username and Email are populated from the OAuth result stored in the
        // credential store — we pass them through NotifyOAuthCompleted above
        // and the AccountService/OAuthService set them on the Account after save.
        Username = string.Empty,   // filled by AccountService.AddOAuth from token result
        Email = string.Empty,   // filled by AccountService.AddOAuth from token result
        Host = Host.Trim().ToLowerInvariant(),
        AuthMethod = AuthMethod.OAuth,
    };
}