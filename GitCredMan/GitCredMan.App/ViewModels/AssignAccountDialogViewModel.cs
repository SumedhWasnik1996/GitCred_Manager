using CommunityToolkit.Mvvm.ComponentModel;
using GitCredMan.Core.Models;

namespace GitCredMan.App.ViewModels;

public partial class AssignAccountDialogViewModel : ObservableObject
{
    [ObservableProperty] private Repository? _repository;
    [ObservableProperty] private Account?    _selectedAccount;   // null = use default
    [ObservableProperty] private bool        _useDefault = true;

    public List<Account> Accounts { get; }

    public AssignAccountDialogViewModel(Repository repo, IEnumerable<Account> accounts, Account? current)
    {
        Repository = repo;
        Accounts   = [.. accounts];

        if (current is not null)
        {
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == current.Id);
            UseDefault      = SelectedAccount is null;
        }
        else
        {
            UseDefault = true;
        }
    }

    /// <summary>Returns null if "use default" is selected.</summary>
    public string? GetChosenAccountId() =>
        UseDefault ? null : SelectedAccount?.Id;

    partial void OnUseDefaultChanged(bool value)
    {
        if (value) SelectedAccount = null;
    }
}
