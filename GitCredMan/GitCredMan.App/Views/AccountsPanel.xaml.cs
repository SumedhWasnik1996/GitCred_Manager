using System.Windows;
using System.Windows.Controls;
using GitCredMan.App.ViewModels;
using GitCredMan.Core.Models;
using Microsoft.Extensions.DependencyInjection;

using WpfUserControl = System.Windows.Controls.UserControl;

namespace GitCredMan.App.Views;

public partial class AccountsPanel : WpfUserControl
{
    private MainViewModel VM => (MainViewModel)DataContext;

    public AccountsPanel() => InitializeComponent();

    // ── Add ───────────────────────────────────────────────────

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlgVm = App.Services.GetRequiredService<AccountDialogViewModel>();
        var dlg   = new AccountDialog(dlgVm) { Owner = Window.GetWindow(this) };

        if (dlg.ShowDialog() != true) return;

        var result = VM.AddAccount(dlgVm.BuildAccount(), dlg.CollectedToken);
        if (!result.Success)
            ShowError(result.Error ?? "Unknown error.");

        dlg.ClearToken();
    }

    // ── Edit ──────────────────────────────────────────────────

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedAccount is not { } account) return;

        var dlgVm = App.Services.GetRequiredService<AccountDialogViewModel>();
        dlgVm.LoadFrom(account);

        var dlg = new AccountDialog(dlgVm) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var newToken = string.IsNullOrEmpty(dlg.CollectedToken) ? null : dlg.CollectedToken;
        var result   = VM.UpdateAccount(dlgVm.BuildAccount(), newToken);

        if (!result.Success) ShowError(result.Error ?? "Unknown error.");
        dlg.ClearToken();
    }

    // ── Delete ────────────────────────────────────────────────

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedAccount is not { } account) return;

        var r = MessageBox.Show(
            $"Delete account '{account.Name}'?\n\n" +
            "• Its stored token will be removed from Windows Credential Manager.\n" +
            "• Repositories assigned to it will revert to the global default.\n\n" +
            "This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (r == MessageBoxResult.Yes)
            VM.DeleteAccountCommand.Execute(account);
    }

    // ── Set default star ──────────────────────────────────────

    private void StarBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Account account })
            VM.SetDefaultAccountCommand.Execute(account);
    }

    // ── Detect existing git accounts ──────────────────────────

    private void DetectBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = VM.DetectExistingAccountsCommand.ExecuteAsync(null);
    }

    private void CollapseDetect_Click(object sender, RoutedEventArgs e)
    {
        VM.ShowDetectPanel = false;
        VM.DiscoveredIdentities.Clear();
        VM.DetectStatus = string.Empty;
    }

    /// <summary>
    /// Import a discovered identity: pre-fill the Add Account dialog
    /// with the detected name/email/host, then prompt the user for a token.
    /// </summary>
    private void ImportIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiscoveredIdentity identity }) return;
        if (identity.AlreadyImported) return;

        // Pre-populate the dialog from the discovered identity
        var dlgVm = App.Services.GetRequiredService<AccountDialogViewModel>();
        var draft = VM.BuildAccountFromDiscovered(identity);
        dlgVm.LoadFrom(draft);         // puts it in edit mode with fields pre-filled
        dlgVm.IsEditMode = false;      // override: this is a new account, not an edit
        dlgVm.TokenHint  = "Enter a Personal Access Token for this identity.";
        dlgVm.TokenChanged = false;    // require token since it's new

        var dlg = new AccountDialog(dlgVm)
        {
            Owner = Window.GetWindow(this),
            Title = $"Import — {identity.DisplayName}"
        };

        if (dlg.ShowDialog() != true)
        {
            dlg.ClearToken();
            return;
        }

        var result = VM.AddAccount(dlgVm.BuildAccount(), dlg.CollectedToken);
        dlg.ClearToken();

        if (!result.Success)
        {
            ShowError(result.Error ?? "Unknown error.");
            return;
        }

        // Mark as imported in the discovered list
        VM.MarkDiscoveredAsImported(identity.Key);
    }

    // ── Helper ────────────────────────────────────────────────

    private void ShowError(string msg) =>
        MessageBox.Show(msg, "Git Credential Manager",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
