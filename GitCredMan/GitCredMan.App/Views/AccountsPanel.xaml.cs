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
        var dlg = new AccountDialog(dlgVm) { Owner = Window.GetWindow(this) };

        if (dlg.ShowDialog() != true) return;

        var result = VM.AddOAuthAccount(
            dlgVm.BuildAccount(),
            dlgVm.OAuthAccessToken!,
            dlgVm.OAuthRefreshToken);

        if (!result.Success)
            ShowError(result.Error ?? "Unknown error.");

        dlg.ClearTokens();
    }

    // ── Edit ──────────────────────────────────────────────────

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedAccount is not { } account) return;

        var dlgVm = App.Services.GetRequiredService<AccountDialogViewModel>();
        dlgVm.LoadFrom(account);

        var dlg = new AccountDialog(dlgVm) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        OperationResult result;

        if (dlgVm.OAuthCompleted && !string.IsNullOrEmpty(dlgVm.OAuthAccessToken))
        {
            // User re-authenticated — replace tokens
            result = VM.UpdateOAuthAccount(
                dlgVm.BuildAccount(),
                dlgVm.OAuthAccessToken,
                dlgVm.OAuthRefreshToken);
        }
        else
        {
            // Label/host rename only — keep existing token
            result = VM.UpdateAccount(dlgVm.BuildAccount(), newToken: null);
        }

        if (!result.Success) ShowError(result.Error ?? "Unknown error.");
        dlg.ClearTokens();
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
        if (sender is not Button { Tag: Account account }) return;

        // Set the new default first
        VM.SetDefaultAccountCommand.Execute(account);

        // Ask whether to reassign all currently-unassigned repos to the new default
        int unassigned = VM.Repositories.Count(r => string.IsNullOrEmpty(r.AccountId));
        if (unassigned == 0) return;

        var answer = MessageBox.Show(
            $"'{account.Name}' is now the global default.\n\n" +
            $"Would you like to reassign all {unassigned} unassigned " +
            $"repositor{(unassigned == 1 ? "y" : "ies")} to this account?\n\n" +
            "Repositories with a specific account already set will not be changed.",
            "Update Repository Assignments?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
            VM.AssignDefaultToUnassignedCommand.Execute(account);
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

    private void ImportIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiscoveredIdentity identity }) return;
        if (identity.AlreadyImported) return;

        var dlgVm = App.Services.GetRequiredService<AccountDialogViewModel>();
        var draft = VM.BuildAccountFromDiscovered(identity);
        dlgVm.LoadFrom(draft);
        dlgVm.IsEditMode = false;
        dlgVm.OAuthCompleted = false;
        dlgVm.OAuthStatus = string.Empty;

        var dlg = new AccountDialog(dlgVm)
        {
            Owner = Window.GetWindow(this),
            Title = $"Import — {identity.DisplayName}"
        };

        if (dlg.ShowDialog() != true) { dlg.ClearTokens(); return; }

        var result = VM.AddOAuthAccount(
            dlgVm.BuildAccount(),
            dlgVm.OAuthAccessToken!,
            dlgVm.OAuthRefreshToken);

        dlg.ClearTokens();

        if (!result.Success) { ShowError(result.Error ?? "Unknown error."); return; }

        VM.MarkDiscoveredAsImported(identity.Key);
    }

    // ── Helper ────────────────────────────────────────────────

    private void ShowError(string msg) =>
        MessageBox.Show(msg, "Git Credential Manager",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}