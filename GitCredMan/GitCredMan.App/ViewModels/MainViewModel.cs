using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using GitCredMan.Core.Services;

namespace GitCredMan.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Services ──────────────────────────────────────────────
    private readonly AccountService _accountSvc;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IRepositoryScanner _scanner;
    private readonly IGitConfigService _gitCfg;

    // ── Persisted state ───────────────────────────────────────
    public AppSettings Settings { get; private set; }

    // ── Observable collections ────────────────────────────────
    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<Repository> Repositories { get; } = [];
    public ObservableCollection<Repository> FilteredRepositories { get; } = [];

    // ── Nav / UI state ────────────────────────────────────────
    [ObservableProperty] private int _activePage = 0;
    [ObservableProperty] private bool _navExpanded = false;

    // ── Account state ─────────────────────────────────────────
    [ObservableProperty] private Account? _selectedAccount;
    [ObservableProperty] private Account? _defaultAccount;

    // ── Repo state ────────────────────────────────────────────
    [ObservableProperty] private Repository? _selectedRepository;

    // ── Scan state ────────────────────────────────────────────
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private int _scanFound = 0;
    [ObservableProperty] private double _spinnerAngle = 0;

    // ── Status ────────────────────────────────────────────────
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private AppTheme _currentTheme = AppTheme.Dark;

    // ── System git identity ───────────────────────────────────
    [ObservableProperty] private string _systemGitIdentity = "Reading…";

    // ── Repo filter ───────────────────────────────────────────
    private string _repoFilter = string.Empty;
    public string RepoFilter
    {
        get => _repoFilter;
        set { if (SetProperty(ref _repoFilter, value)) RebuildFilter(); }
    }
    public int FilteredCount => FilteredRepositories.Count;

    private CancellationTokenSource? _scanCts;

    // ── Constructor ───────────────────────────────────────────
    public MainViewModel(
        AccountService accountSvc,
        ISettingsRepository settingsRepo,
        IRepositoryScanner scanner,
        IGitConfigService gitCfg)
    {
        _accountSvc = accountSvc;
        _settingsRepo = settingsRepo;
        _scanner = scanner;
        _gitCfg = gitCfg;

        Settings = _settingsRepo.Load();
        _accountSvc.RefreshHasToken(Settings);
        CurrentTheme = Settings.Theme;

        Repositories.CollectionChanged += (_, _) => RebuildFilter();
        FilteredRepositories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredCount));

        Reload();
        _ = LoadSystemGitIdentityAsync();
    }

    // ── Load / Reload ─────────────────────────────────────────

    private void Reload()
    {
        Accounts.Clear();
        foreach (var a in Settings.Accounts) Accounts.Add(a);

        Repositories.Clear();
        foreach (var r in Settings.Repositories) Repositories.Add(r);

        RefreshDefault();
        UpdateStatus();
    }

    private async Task LoadSystemGitIdentityAsync()
    {
        var identity = await GitConfigService.ReadSystemIdentityAsync();
        SystemGitIdentity = identity.Display;
    }

    private void RebuildFilter()
    {
        var f = _repoFilter.Trim();
        FilteredRepositories.Clear();
        foreach (var repo in Repositories)
        {
            if (string.IsNullOrEmpty(f) ||
                repo.Path.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                repo.DirectoryName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                repo.RemoteUrl.Contains(f, StringComparison.OrdinalIgnoreCase))
                FilteredRepositories.Add(repo);
        }
    }

    private void RefreshDefault()
    {
        DefaultAccount =
            Accounts.FirstOrDefault(a => a.Id == Settings.DefaultAccountId)
            ?? Accounts.FirstOrDefault(a => a.IsDefault)
            ?? Accounts.FirstOrDefault();
    }

    private void UpdateStatus() =>
        StatusText = $"Ready  ·  {Accounts.Count} account(s)  ·  {Repositories.Count} repo(s)";

    // ════════════════════════════════════════════════════════
    //  ACCOUNT COMMANDS
    // ════════════════════════════════════════════════════════

    /// <summary>Add a new account authenticated via PAT.</summary>
    public OperationResult AddAccount(Account account, string token)
    {
        var result = _accountSvc.Add(Settings, account, token);
        if (result.Success)
        {
            Accounts.Add(account);
            RefreshDefault();
            UpdateStatus();
            StatusText = $"Account '{account.Name}' added.";
        }
        return result;
    }

    /// <summary>Add a new account authenticated via OAuth device flow.</summary>
    public OperationResult AddOAuthAccount(Account account, string accessToken, string? refreshToken,
        string? username = null, string? email = null)
    {
        var result = _accountSvc.AddOAuth(Settings, account, accessToken, refreshToken, username, email);
        if (result.Success)
        {
            Accounts.Add(account);
            RefreshDefault();
            UpdateStatus();
            StatusText = $"Account '{account.Name}' added via OAuth.";
        }
        return result;
    }

    public OperationResult UpdateAccount(Account account, string? newToken)
    {
        var result = _accountSvc.Update(Settings, account, newToken);
        if (result.Success)
        {
            var idx = IndexOf(Accounts, a => a.Id == account.Id);
            if (idx >= 0) { Accounts.RemoveAt(idx); Accounts.Insert(idx, account); }
            RefreshDefault();
            StatusText = $"Account '{account.Name}' updated.";
        }
        return result;
    }

    /// <summary>Update an existing OAuth account with fresh tokens.</summary>
    public OperationResult UpdateOAuthAccount(Account account, string accessToken, string? refreshToken)
    {
        // Use the standard update path to save metadata, then overwrite the tokens
        var result = _accountSvc.Update(Settings, account, accessToken);
        if (result.Success)
        {
            // If a refresh token was returned, store it too
            if (!string.IsNullOrEmpty(refreshToken) && account.RefreshTokenKey is not null)
            {
                // Access the store directly via AccountService helper
                _accountSvc.SaveRefreshToken(account, refreshToken);
            }
            var idx = IndexOf(Accounts, a => a.Id == account.Id);
            if (idx >= 0) { Accounts.RemoveAt(idx); Accounts.Insert(idx, account); }
            RefreshDefault();
            StatusText = $"Account '{account.Name}' updated via OAuth.";
        }
        return result;
    }

    [RelayCommand]
    public void DeleteAccount(Account account)
    {
        var result = _accountSvc.Delete(Settings, account.Id);
        if (!result.Success) return;
        Accounts.Remove(account);
        if (SelectedAccount?.Id == account.Id) SelectedAccount = null;
        RefreshRepositories();
        RefreshDefault();
        UpdateStatus();
        StatusText = $"Account '{account.Name}' deleted.";
    }

    [RelayCommand]
    public void SetDefaultAccount(Account account)
    {
        _accountSvc.SetDefault(Settings, account.Id);
        foreach (var a in Accounts) a.IsDefault = a.Id == account.Id;
        RefreshDefault();
        RefreshAccounts();
        RefreshRepositories();
        StatusText = $"'{account.Name}' set as global default.";
    }

    /// <summary>
    /// Explicitly assigns <paramref name="account"/> to every repository that
    /// currently has no specific account set (AccountId is null/empty).
    /// Called when the user confirms the post-default-change prompt.
    /// </summary>
    [RelayCommand]
    public void AssignDefaultToUnassigned(Account account)
    {
        int count = 0;
        foreach (var repo in Repositories.Where(r => string.IsNullOrEmpty(r.AccountId)))
        {
            repo.AccountId = account.Id;
            count++;
        }
        Settings.Repositories = [.. Repositories];
        _settingsRepo.Save(Settings);
        RefreshRepositories();
        StatusText = $"Assigned '{account.Name}' to {count} repositor{(count == 1 ? "y" : "ies")}.";
    }

    // ════════════════════════════════════════════════════════
    //  REPOSITORY COMMANDS
    // ════════════════════════════════════════════════════════

    public void AssignAccount(Repository repo, string? accountId)
    {
        repo.AccountId = accountId;
        Settings.Repositories = [.. Repositories];
        _settingsRepo.Save(Settings);
        RefreshRepositories();
        var acc = _accountSvc.Resolve(Settings, repo);
        StatusText = $"Assigned '{repo.DirectoryName}' → {acc?.Name ?? "global default"}.";
    }

    [RelayCommand]
    public async Task ApplyToRepoAsync(Repository repo)
    {
        var account = _accountSvc.Resolve(Settings, repo);
        if (account is null) { StatusText = "No account to apply."; return; }

        // Use async token retrieval so OAuth refresh works without blocking
        var token = await _accountSvc.GetTokenAsync(account);
        if (token is null) { StatusText = $"No token stored for '{account.Name}'."; return; }

        StatusText = $"Applying '{account.Name}' to {repo.DirectoryName}…";
        var result = await _gitCfg.ApplyAsync(repo, account, token);
        StatusText = result.Success
            ? $"✓  Applied '{account.Name}' to {repo.DirectoryName}."
            : $"✗  {result.Error}";
    }

    [RelayCommand]
    public async Task ApplyAllAsync()
    {
        int ok = 0, fail = 0;
        StatusText = "Applying accounts to all repositories…";
        foreach (var repo in Repositories)
        {
            var account = _accountSvc.Resolve(Settings, repo);
            if (account is null) { fail++; continue; }

            var token = await _accountSvc.GetTokenAsync(account);
            if (token is null) { fail++; continue; }

            var result = await _gitCfg.ApplyAsync(repo, account, token);
            if (result.Success) ok++; else fail++;
        }
        Settings.Repositories = [.. Repositories];
        _settingsRepo.Save(Settings);
        StatusText = $"Apply all  ·  {ok} succeeded  ·  {fail} skipped/failed.";
    }

    [RelayCommand]
    public void RemoveRepository(Repository repo)
    {
        Repositories.Remove(repo);
        Settings.Repositories = [.. Repositories];
        _settingsRepo.Save(Settings);
        UpdateStatus();
    }

    [RelayCommand]
    public void OpenRepoFolder(Repository repo)
    {
        try { System.Diagnostics.Process.Start("explorer.exe", $"\"{repo.Path}\""); }
        catch { }
    }

    // ════════════════════════════════════════════════════════
    //  SCAN
    // ════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning) { _scanCts?.Cancel(); return; }

        IsScanning = true;
        ScanFound = 0;
        ScanStatus = "Starting scan…";
        _scanCts = new CancellationTokenSource();

        var spinTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        spinTimer.Tick += (_, _) => SpinnerAngle = (SpinnerAngle + 6) % 360;
        spinTimer.Start();

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanStatus = TruncatePath(p.CurrentPath, 72);
            ScanFound = p.FoundCount;
        });

        try
        {
            var found = await _scanner.ScanAsync(progress, _scanCts.Token);
            int added = 0;
            foreach (var repo in found)
            {
                if (!Repositories.Any(r => r.Path.Equals(repo.Path, StringComparison.OrdinalIgnoreCase)))
                { Repositories.Add(repo); added++; }
            }
            Settings.Repositories = [.. Repositories];
            _settingsRepo.Save(Settings);
            ScanStatus = $"Scan complete  ·  {added} new  ·  {Repositories.Count} total";
            StatusText = ScanStatus;
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled.";
            StatusText = ScanStatus;
        }
        finally
        {
            spinTimer.Stop();
            SpinnerAngle = 0;
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    // ════════════════════════════════════════════════════════
    //  THEME
    // ════════════════════════════════════════════════════════

    [RelayCommand]
    public void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Settings.Theme = CurrentTheme;
        _settingsRepo.Save(Settings);
        App.ApplyTheme(CurrentTheme);
    }

    // ════════════════════════════════════════════════════════
    //  NAV
    // ════════════════════════════════════════════════════════

    [RelayCommand]
    public void NavigateTo(int page) => ActivePage = page;

    // ════════════════════════════════════════════════════════
    //  PUBLIC HELPERS
    // ════════════════════════════════════════════════════════

    public Account? ResolveAccount(Repository repo) =>
        _accountSvc.Resolve(Settings, repo);

    public string ResolveAccountName(Repository repo)
    {
        var acc = _accountSvc.Resolve(Settings, repo);
        if (acc is null) return "(none)";
        bool specific = !string.IsNullOrEmpty(repo.AccountId);
        var label = acc.AuthMethod == AuthMethod.OAuth ? $"{acc.Name} 🔐" : acc.Name;
        return specific ? label : $"{label}  (default)";
    }

    // ════════════════════════════════════════════════════════
    //  DETECT EXISTING GIT ACCOUNTS
    // ════════════════════════════════════════════════════════

    public ObservableCollection<DiscoveredIdentity> DiscoveredIdentities { get; } = [];

    [ObservableProperty] private bool _isDetecting = false;
    [ObservableProperty] private string _detectStatus = string.Empty;
    [ObservableProperty] private bool _showDetectPanel = false;

    [RelayCommand]
    public async Task DetectExistingAccountsAsync()
    {
        IsDetecting = true;
        DetectStatus = "Scanning git configs…";
        ShowDetectPanel = true;
        DiscoveredIdentities.Clear();

        var progress = new Progress<string>(msg => DetectStatus = msg);

        try
        {
            var found = await GitIdentityScanner.ScanAsync(
                Repositories, Accounts, progress);

            foreach (var identity in found)
                DiscoveredIdentities.Add(identity);

            DetectStatus = found.Count == 0
                ? "No additional git identities found."
                : $"Found {found.Count} identit{(found.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            DetectStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;
        }
    }

    public Account BuildAccountFromDiscovered(DiscoveredIdentity identity) => new()
    {
        Name = string.IsNullOrEmpty(identity.Username) ? identity.Email : identity.Username,
        Username = identity.Username,
        Email = identity.Email,
        Host = identity.Host,
    };

    public void MarkDiscoveredAsImported(string key)
    {
        var match = DiscoveredIdentities.FirstOrDefault(d => d.Key == key);
        if (match is not null) match.AlreadyImported = true;
        var items = DiscoveredIdentities.ToList();
        DiscoveredIdentities.Clear();
        foreach (var i in items) DiscoveredIdentities.Add(i);
    }

    public void SaveNow()
    {
        Settings.Accounts = [.. Accounts];
        Settings.Repositories = [.. Repositories];
        _settingsRepo.Save(Settings);
    }

    // ════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ════════════════════════════════════════════════════════

    private void RefreshAccounts()
    {
        var items = Accounts.ToList();
        Accounts.Clear();
        foreach (var a in items) Accounts.Add(a);
    }

    private void RefreshRepositories()
    {
        var items = Repositories.ToList();
        Repositories.Clear();
        foreach (var r in items) Repositories.Add(r);
    }

    private static int IndexOf<T>(ObservableCollection<T> col, Func<T, bool> pred)
    {
        for (int i = 0; i < col.Count; i++) if (pred(col[i])) return i;
        return -1;
    }

    private static string TruncatePath(string p, int max) =>
        p.Length <= max ? p : $"…{p[^(max - 1)..]}";
}