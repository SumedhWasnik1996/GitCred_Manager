using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GitCredMan.App.ViewModels;
using GitCredMan.Core.Models;

using WpfUserControl = System.Windows.Controls.UserControl;

namespace GitCredMan.App.Views;

public partial class RepositoriesPanel : WpfUserControl
{
    private MainViewModel VM => (MainViewModel)DataContext;

    public RepositoriesPanel() => InitializeComponent();

    // ── Refresh account badge text after each layout pass ─────
    private void RepoList_LayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        for (int i = 0; i < vm.FilteredRepositories.Count; i++)
        {
            var container = RepoList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            var badge     = FindChild<TextBlock>(container, "AccountBadge");
            if (badge is not null)
                badge.Text = vm.ResolveAccountName(vm.FilteredRepositories[i]);
        }
    }

    // ── Assign ────────────────────────────────────────────────

    private void AssignBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Repository repo }) return;
        var current = VM.ResolveAccount(repo);
        var dlgVm   = new AssignAccountDialogViewModel(repo, VM.Accounts, current);
        var dlg     = new AssignAccountDialog(dlgVm) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            VM.AssignAccount(repo, dlgVm.GetChosenAccountId());
    }

    // ── Apply ─────────────────────────────────────────────────

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Repository repo })
            _ = VM.ApplyToRepoCommand.ExecuteAsync(repo);
    }

    // ── More dropdown: Open | Remove from list | Delete repo ──

    private void MoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Repository repo } btn) return;

        var menu = new ContextMenu { PlacementTarget = btn, Placement = PlacementMode.Bottom };

        // Open in Explorer
        var open = new MenuItem { Header = "📂  Open in Explorer" };
        open.Click += (_, _) => VM.OpenRepoFolderCommand.Execute(repo);
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        // Remove from list (keeps files)
        var remove = new MenuItem { Header = "✕  Remove from list" };
        remove.Click += (_, _) =>
        {
            var r = MessageBox.Show(
                $"Remove '{repo.DirectoryName}' from the list?\n\nFiles will not be deleted.",
                "Remove Repository", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
            if (r == MessageBoxResult.Yes)
                VM.RemoveRepositoryCommand.Execute(repo);
        };
        menu.Items.Add(remove);

        // Delete repo from disk
        var delete = new MenuItem
        {
            Header     = "🗑  Delete repository from disk",
            Foreground = System.Windows.Media.Brushes.IndianRed,
        };
        delete.Click += (_, _) => DeleteRepoDisk(repo);
        menu.Items.Add(delete);

        menu.IsOpen = true;
    }

    private void DeleteRepoDisk(Repository repo)
    {
        var r = MessageBox.Show(
            $"Permanently DELETE '{repo.DirectoryName}' from disk?\n\n" +
            $"Path: {repo.Path}\n\n" +
            "⚠  This cannot be undone. All files will be deleted.",
            "Delete Repository",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (r != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(repo.Path))
                Directory.Delete(repo.Path, recursive: true);

            VM.RemoveRepositoryCommand.Execute(repo);
            VM.StatusText = $"Deleted '{repo.DirectoryName}' from disk.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete '{repo.Path}':\n\n{ex.Message}",
                "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Visual tree helper ─────────────────────────────────────

    private static T? FindChild<T>(DependencyObject? parent, string name)
        where T : FrameworkElement
    {
        if (parent is null) return null;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var hit = FindChild<T>(child, name);
            if (hit is not null) return hit;
        }
        return null;
    }
}
