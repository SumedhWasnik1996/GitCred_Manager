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
    private string _username = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _host = "github.com";

    // ── State ─────────────────────────────────────────────────
    [ObservableProperty] private bool   _isEditMode   = false;
    [ObservableProperty] private bool   _tokenChanged = false;
    [ObservableProperty] private string _tokenHint    = string.Empty;

    public string? OriginalId { get; private set; }

    // ── Validation ────────────────────────────────────────────
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name)     &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Host)     &&
        (IsEditMode || TokenChanged);

    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))     return "Account name is required.";
            if (string.IsNullOrWhiteSpace(Username)) return "Username is required.";
            if (string.IsNullOrWhiteSpace(Host))     return "Host is required.";
            if (!IsEditMode && !TokenChanged)        return "A Personal Access Token is required.";
            return null;
        }
    }

    /// <summary>
    /// Called by the View when the PasswordBox content changes.
    /// PasswordBox.Password cannot data-bind, so the code-behind calls this instead.
    /// </summary>
    public void NotifyTokenChanged(bool hasToken)
    {
        TokenChanged = hasToken;
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationError));
    }

    public void LoadFrom(Account account)
    {
        OriginalId   = account.Id;
        IsEditMode   = true;
        Name         = account.Name;
        Username     = account.Username;
        Email        = account.Email;
        Host         = account.Host;
        TokenHint    = account.HasStoredToken
            ? "Leave blank to keep the existing token."
            : "No token stored — enter one now.";
        TokenChanged = false;
    }

    public Account BuildAccount() => new()
    {
        Id       = OriginalId ?? Guid.NewGuid().ToString("D"),
        Name     = Name.Trim(),
        Username = Username.Trim(),
        Email    = Email.Trim(),
        Host     = Host.Trim().ToLowerInvariant(),
    };
}
