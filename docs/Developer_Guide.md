# Git Credential Manager — Developer Guide

> **Version 1.0.0 · March 2026**
> Complete reference for understanding, building, and extending the codebase.

---

## Table of Contents

1. [What Is This Application?](#1-what-is-this-application)
2. [Technology Stack](#2-technology-stack)
3. [Solution Structure](#3-solution-structure)
4. [Core Concepts](#4-core-concepts)
   - [MVVM Pattern](#41-mvvm-pattern)
   - [Data Binding](#42-data-binding)
   - [Dependency Injection](#43-dependency-injection)
   - [Interfaces and Mocking](#44-interfaces-and-mocking)
5. [Data Models](#5-data-models)
6. [Services](#6-services)
   - [WindowsCredentialStore](#61-windowscredentialstore)
   - [JsonSettingsRepository](#62-jsonsettingsrepository)
   - [AccountService](#63-accountservice)
   - [RepositoryScannerService](#64-repositoryscannerservice)
   - [GitConfigService](#65-gitconfigservice)
   - [GitIdentityScanner](#66-gitidentityscanner)
7. [ViewModels](#7-viewmodels)
   - [MainViewModel](#71-mainviewmodel)
   - [AccountDialogViewModel](#72-accountdialogviewmodel)
8. [Views](#8-views)
9. [Theming System](#9-theming-system)
10. [Security Design](#10-security-design)
11. [Data Flow Walkthroughs](#11-data-flow-walkthroughs)
12. [Running and Writing Tests](#12-running-and-writing-tests)
13. [Building for Distribution](#13-building-for-distribution)
14. [Common Tasks Quick Reference](#14-common-tasks-quick-reference)
15. [Glossary](#15-glossary)

---

## 1. What Is This Application?

Git Credential Manager is a Windows desktop application that solves the problem of managing multiple Git identities on a single machine. Without it, git uses one global `user.name` / `user.email` and the same credentials for every repository. This app lets you:

- Store unlimited git identities (accounts), each with a Personal Access Token (PAT) encrypted in **Windows Credential Manager**
- Assign a different identity to each repository
- Apply `user.name`, `user.email`, and an authenticated HTTPS remote URL to any repo with one click
- Scan all drives to discover repositories automatically
- Detect existing git identities already configured on the machine and import them

---

## 2. Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Language | C# | 13 |
| Runtime | .NET | 10.0 |
| UI Framework | WPF (Windows Presentation Foundation) | .NET 10 |
| Architecture Pattern | MVVM | — |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.3.2 |
| Dependency Injection | Microsoft.Extensions.DependencyInjection | 10.0.0 |
| Logging | Microsoft.Extensions.Logging | 10.0.0 |
| Serialisation | System.Text.Json | (built-in to .NET 10) |
| Credential Storage | Windows Credential Manager via P/Invoke | — |
| Testing | xUnit + NSubstitute + FluentAssertions | 2.9.2 / 5.1.0 / 6.12.1 |

---

## 3. Solution Structure

The solution contains three projects, each with a clear, single responsibility.

```
GitCredMan/
├── GitCredMan.sln
│
├── GitCredMan.Core/               # Pure C# — no WPF, no UI references
│   ├── GitCredMan.Core.csproj
│   ├── Models/
│   │   └── Models.cs              # All data types
│   ├── Interfaces/
│   │   └── IServices.cs           # Service contracts
│   └── Services/
│       ├── AccountService.cs      # Account CRUD + token coordination
│       ├── WindowsCredentialStore.cs  # DPAPI token storage
│       ├── JsonSettingsRepository.cs  # settings.json persistence
│       ├── RepositoryScannerService.cs # Drive scan for .git dirs
│       ├── GitConfigService.cs    # git config commands + system identity
│       └── GitIdentityScanner.cs  # Detect existing identities
│
├── GitCredMan.App/                # WPF presentation layer
│   ├── GitCredMan.App.csproj
│   ├── App.xaml / App.xaml.cs     # Entry point, DI container, crash handling
│   ├── Converters/
│   │   └── Converters.cs          # XAML value converters
│   ├── Resources/
│   │   └── Icons/                 # app.ico, logo_*.png
│   ├── Themes/
│   │   ├── DarkTheme.xaml         # Dark colour palette
│   │   ├── LightTheme.xaml        # Light colour palette
│   │   └── SharedStyles.xaml      # All control templates
│   ├── ViewModels/
│   │   ├── MainViewModel.cs       # Central VM — accounts, repos, commands
│   │   ├── AccountDialogViewModel.cs
│   │   └── AssignAccountDialogViewModel.cs
│   └── Views/
│       ├── MainWindow.xaml/.cs    # Shell — nav rail, DWM title bar, tray
│       ├── AccountsPanel.xaml/.cs # Accounts page
│       ├── RepositoriesPanel.xaml/.cs # Repositories page
│       ├── AccountDialog.xaml/.cs # Add / Edit account modal
│       └── AssignAccountDialog.xaml/.cs # Assign account to repo modal
│
└── GitCredMan.Tests/              # xUnit test project
    ├── GitCredMan.Tests.csproj
    └── Core/
        ├── AccountServiceTests.cs     # 29 tests total
        ├── GitConfigServiceTests.cs
        ├── ModelTests.cs
        ├── PersistenceServiceTests.cs
        └── RepositoryScannerTests.cs
```

> **Rule:** `GitCredMan.Core` has zero knowledge of WPF. `GitCredMan.App` references `Core` but `Core` never references `App`. `GitCredMan.Tests` references only `Core`.

---

## 4. Core Concepts

### 4.1 MVVM Pattern

MVVM (Model–View–ViewModel) keeps three concerns separate:

| Layer | In this project | Knows about |
|---|---|---|
| **Model** | Everything in `GitCredMan.Core` — `Account`, `Repository`, `AccountService`, etc. | Data and business rules only |
| **View** | The `.xaml` files — `MainWindow.xaml`, `AccountsPanel.xaml`, etc. | Layout and appearance only |
| **ViewModel** | `MainViewModel`, `AccountDialogViewModel` | Bridges Model and View; exposes data as properties and actions as commands |

The View never calls business logic directly. The Model never imports WPF. The ViewModel contains no XAML.

### 4.2 Data Binding

XAML elements bind to ViewModel properties using the `{Binding}` syntax:

```xml
<!-- Displays StatusText and auto-updates when it changes -->
<TextBlock Text="{Binding StatusText}"/>

<!-- Button calls ScanCommand when clicked -->
<Button Command="{Binding ScanCommand}" Content="Scan"/>

<!-- Two-way: TextBox writes back to RepoFilter -->
<TextBox Text="{Binding RepoFilter, UpdateSourceTrigger=PropertyChanged}"/>
```

For a binding to update the UI automatically, the ViewModel property must raise `PropertyChanged`. The `[ObservableProperty]` attribute from CommunityToolkit.Mvvm generates this automatically:

```csharp
[ObservableProperty] private string _statusText = "Ready";
// Generates: public string StatusText { get; set; } with INotifyPropertyChanged
```

Commands are declared with `[RelayCommand]`:

```csharp
[RelayCommand]
public async Task ScanAsync() { /* ... */ }
// Generates: public IAsyncRelayCommand ScanCommand { get; }
```

### 4.3 Dependency Injection

The DI container is built in `App.xaml.cs → BuildServices()`. Every service is registered once and injected automatically via constructors:

```csharp
sc.AddSingleton<ICredentialStore,    WindowsCredentialStore>();
sc.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
sc.AddSingleton<IRepositoryScanner,  RepositoryScannerService>();
sc.AddSingleton<IGitConfigService,   GitConfigService>();
sc.AddSingleton<AccountService>();
sc.AddSingleton<MainViewModel>();
sc.AddTransient<AccountDialogViewModel>();  // New instance per dialog
sc.AddSingleton<MainWindow>();
```

`AddSingleton` means one instance shared for the lifetime of the app. `AddTransient` means a fresh instance each time it is resolved — used for dialogs so each dialog gets clean state.

### 4.4 Interfaces and Mocking

Every service has an interface in `IServices.cs`:

```csharp
public interface ICredentialStore
{
    bool    Save(string accountId, string token);
    string? Load(string accountId);
    bool    Delete(string accountId);
    bool    Exists(string accountId);
}
```

`AccountService` depends on `ICredentialStore`, not `WindowsCredentialStore`. In tests, NSubstitute creates a fake:

```csharp
var store = Substitute.For<ICredentialStore>();
store.Save(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
// Tests can now run without touching Windows Credential Manager
```

---

## 5. Data Models

All models are in `GitCredMan.Core/Models/Models.cs`.

### `Account` (record class)

```csharp
public sealed record class Account
{
    public string   Id         { get; init; }  // Guid, set once at creation
    public string   Name       { get; set; }   // "Work GitHub"
    public string   Username   { get; set; }   // "alice"
    public string   Email      { get; set; }   // "alice@example.com"
    public string   Host       { get; set; }   // "github.com"
    public bool     IsDefault  { get; set; }   // One account is the global default
    public DateTime CreatedAt  { get; init; }

    [JsonIgnore] public bool   HasStoredToken { get; set; }  // Checked live from Credential Manager
    [JsonIgnore] public string AvatarInitial  => ...         // First letter of Name
    [JsonIgnore] public string DisplaySummary => ...         // "email · host"
}
```

> `[JsonIgnore]` properties are never written to `settings.json`. The token itself is never a field on `Account` — it lives only in Windows Credential Manager.
>
> `Account` is a `record class` so the `with` expression works: `account with { Name = "Updated" }`.

### `Repository`

```csharp
public sealed class Repository
{
    public string   Path         { get; set; }  // Full path to the repo root
    public string   RemoteUrl    { get; set; }  // https://github.com/org/repo.git
    public bool     HasRemote    { get; set; }
    public string?  AccountId    { get; set; }  // null = use global default
    public DateTime DiscoveredAt { get; init; }

    [JsonIgnore] public string DirectoryName => ...  // Last path segment
    [JsonIgnore] public string ShortPath     => ...  // "…\parent\name"
    [JsonIgnore] public string HostLabel     => ...  // "github.com"
}
```

### `AppSettings`

The root object serialised to `%APPDATA%\GitCredMan\settings.json`:

```csharp
public sealed class AppSettings
{
    public List<Account>    Accounts         { get; set; }  // All accounts (no tokens)
    public List<Repository> Repositories     { get; set; }  // Known repos
    public string?          DefaultAccountId { get; set; }  // The global default
    public int              ScanDepth        { get; set; } = 8;
    public List<string>     ExcludedPaths    { get; set; }
    public AppTheme         Theme            { get; set; } = AppTheme.Dark;
    public bool             MinimizeToTray   { get; set; } = true;
    public bool             StartMinimized   { get; set; } = false;
    public string           Version          { get; set; } = "1.0.0";
}
```

### `OperationResult`

Returned by service methods instead of throwing exceptions:

```csharp
public record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok()               => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}
```

### `DiscoveredIdentity`

Produced by `GitIdentityScanner` when detecting existing git accounts:

```csharp
public sealed class DiscoveredIdentity
{
    public string Username     { get; set; }
    public string Email        { get; set; }
    public string Host         { get; set; }
    public string Source       { get; set; }   // "Global" or repo path
    public bool   IsGlobal     { get; set; }
    public bool   AlreadyImported { get; set; }
    public string Key => $"{Username}|{Email}|{Host}".ToLowerInvariant();
}
```

---

## 6. Services

### 6.1 WindowsCredentialStore

**File:** `GitCredMan.Core/Services/WindowsCredentialStore.cs`

Wraps the Windows Credential Manager API via P/Invoke. Tokens are encrypted at rest by DPAPI scoped to the current Windows user session.

**Credential key format:** `GitCredMan:{account-guid}`

**Win32 calls used:**

| Call | Purpose |
|---|---|
| `CredWriteW` | Encrypt and store a credential blob |
| `CredReadW` | Read and decrypt a credential blob |
| `CredDeleteW` | Permanently delete a credential |
| `CredFree` | Free the unmanaged memory returned by CredReadW |

**Memory safety:** The unmanaged blob pointer is zeroed with `Span<byte>.Clear()` before `Marshal.FreeHGlobal` is called. The managed byte array is cleared with `Array.Clear()` after decoding.

```csharp
// Persist setting used:
private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
// Tokens survive reboots but are scoped to the local machine.
// Change to CRED_PERSIST_SESSION (1) for session-only storage.
```

### 6.2 JsonSettingsRepository

**File:** `GitCredMan.Core/Services/JsonSettingsRepository.cs`

Persists `AppSettings` to `%APPDATA%\GitCredMan\settings.json` using `System.Text.Json`. The file is human-readable but **contains no tokens** — `HasStoredToken` is `[JsonIgnore]`.

The constructor has a `protected internal` overload that accepts a custom directory, used by test subclasses to isolate file I/O:

```csharp
// Production: uses %APPDATA%\GitCredMan\
public JsonSettingsRepository(ILogger<JsonSettingsRepository> log)

// Tests: uses a temp directory
protected internal JsonSettingsRepository(ILogger<JsonSettingsRepository> log, string directory)
```

### 6.3 AccountService

**File:** `GitCredMan.Core/Services/AccountService.cs`

The central domain service for account management. ViewModels call this — never raw storage directly.

| Method | What it does |
|---|---|
| `Add(settings, account, token)` | Validates, stores token via `ICredentialStore`, saves settings. Auto-defaults if first account. |
| `Update(settings, account, newToken)` | Updates metadata; optionally replaces token if `newToken` is provided. |
| `Delete(settings, accountId)` | Removes account, deletes token, detaches assigned repos, promotes new default. |
| `SetDefault(settings, accountId)` | Sets one account as global default, clears `IsDefault` on all others. |
| `Resolve(settings, repo)` | Returns the `Account` for a repo: specific assignment if set, else global default. |
| `GetToken(accountId)` | Retrieves the decrypted token from `ICredentialStore`. |
| `RefreshHasToken(settings)` | Calls `ICredentialStore.Exists()` for each account and sets `HasStoredToken`. |

### 6.4 RepositoryScannerService

**File:** `GitCredMan.Core/Services/RepositoryScannerService.cs`

Scans all fixed drives recursively for `.git` directories. Runs entirely on the thread pool via `Task.Run` to keep the UI responsive. Reports progress via `IProgress<ScanProgress>`.

**Scan roots:** All `DriveType.Fixed` drives (C:\, D:\, etc.) — all drives scanned unconditionally.

**Directories skipped** (never descended into):

```
Windows, Program Files, Program Files (x86), ProgramData,
Recovery, System Volume Information, $Recycle.Bin, WinSxS,
node_modules, .npm, .cargo, .gradle, Pods,
__pycache__, .venv, venv, env, .env,
vendor, bower_components, .git,
Local, Roaming, LocalLow
```

When a `.git` directory is found, recursion **stops** for that branch. Nested repos are not supported.

**Adding a directory to the skip list:**

```csharp
// In RepositoryScannerService.cs
private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
{
    // Add your directory name here:
    "my-large-folder",
    // ...
};
```

### 6.5 GitConfigService

**File:** `GitCredMan.Core/Services/GitConfigService.cs`

Applies an account's identity to a repository by spawning `git` processes.

**Commands run** (all `--local`, never `--global`):

```bash
git config user.name  "Alice"
git config user.email "alice@example.com"
git remote set-url origin https://alice:TOKEN@github.com/org/repo.git
```

> SSH remotes (`git@github.com:...`) are detected and left untouched. Only HTTPS remotes receive embedded credentials.

**Static helper:** `ReadSystemIdentityAsync()` reads the machine's global git identity shown in the status bar:

```csharp
var identity = await GitConfigService.ReadSystemIdentityAsync();
// identity.Name  → "Alice"
// identity.Email → "alice@example.com"
```

### 6.6 GitIdentityScanner

**File:** `GitCredMan.Core/Services/GitIdentityScanner.cs`

Scans for pre-existing git identities without spawning git processes (reads `.git/config` files directly).

```csharp
var found = await GitIdentityScanner.ScanAsync(
    repositories,    // known repos to scan
    existingAccounts, // marks already-imported ones
    progress         // IProgress<string> for status updates
);
// Returns List<DiscoveredIdentity> deduplicated by Key
```

Deduplication key: `"username|email|host"` (case-insensitive). The global identity is listed first, then per-repo identities that differ from it.

---

## 7. ViewModels

### 7.1 MainViewModel

**File:** `GitCredMan.App/ViewModels/MainViewModel.cs`

The central ViewModel. Holds all collections and exposes all commands that the UI needs.

**Observable collections:**

```csharp
public ObservableCollection<Account>    Accounts             { get; }
public ObservableCollection<Repository> Repositories         { get; }
public ObservableCollection<Repository> FilteredRepositories { get; }  // filtered by RepoFilter
public ObservableCollection<DiscoveredIdentity> DiscoveredIdentities { get; }
```

**Key observable properties:**

| Property | Type | Purpose |
|---|---|---|
| `ActivePage` | `int` | 0 = Accounts, 1 = Repositories |
| `CurrentTheme` | `AppTheme` | Dark or Light |
| `IsScanning` | `bool` | True while repo scan is running |
| `SpinnerAngle` | `double` | Driven by DispatcherTimer at ~60fps |
| `IsDetecting` | `bool` | True while identity scan is running |
| `StatusText` | `string` | Status bar message |
| `SystemGitIdentity` | `string` | "Alice · alice@example.com" |
| `RepoFilter` | `string` | Rebuilds `FilteredRepositories` on set |

**Commands:**

| Command | Async | What it does |
|---|---|---|
| `ScanCommand` | ✓ | Starts / cancels repository scan |
| `ApplyAllCommand` | ✓ | Applies correct account to every repo |
| `ApplyToRepoCommand` | ✓ | Applies to a single repo |
| `DeleteAccountCommand` | — | Deletes account + token + repo links |
| `SetDefaultAccountCommand` | — | Marks one account as global default |
| `RemoveRepositoryCommand` | — | Removes repo from list (files untouched) |
| `OpenRepoFolderCommand` | — | Opens repo in Windows Explorer |
| `ToggleThemeCommand` | — | Switches Dark ↔ Light |
| `DetectExistingAccountsCommand` | ✓ | Runs GitIdentityScanner |

**Spinner implementation:**

The spinner avoids WPF Storyboard name-scope issues by driving `SpinnerAngle` from a `DispatcherTimer` in the ViewModel. XAML simply binds to it:

```csharp
// In ScanAsync():
var spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
spinTimer.Tick += (_, _) => SpinnerAngle = (SpinnerAngle + 6) % 360;
spinTimer.Start();
// ... scan runs ...
spinTimer.Stop();
SpinnerAngle = 0;
```

```xml
<!-- In RepositoriesPanel.xaml — no TargetName needed -->
<Canvas.RenderTransform>
    <RotateTransform CenterX="7" CenterY="7" Angle="{Binding SpinnerAngle}"/>
</Canvas.RenderTransform>
```

### 7.2 AccountDialogViewModel

**File:** `GitCredMan.App/ViewModels/AccountDialogViewModel.cs`

Form state and validation for the Add / Edit Account dialog.

```csharp
public string  Name     { get; set; }
public string  Username { get; set; }
public string  Email    { get; set; }
public string  Host     { get; set; } = "github.com";
public bool    IsEditMode   { get; set; }
public bool    TokenChanged { get; set; }
public string? TokenHint    { get; set; }

public string? ValidationError { get; }  // null = valid
public Account BuildAccount()            // Creates Account from form state
public void   LoadFrom(Account a)        // Pre-fills form for edit mode
public void   NotifyTokenChanged(bool hasToken)  // Called from code-behind
```

The token itself is **never stored on the ViewModel** — it lives only in `AccountDialog.xaml.cs` as `_tokenBuffer`, a plain `string` that is zeroed in `ClearToken()`.

---

## 8. Views

### MainWindow

**File:** `GitCredMan.App/Views/MainWindow.xaml/.cs`

The application shell. Contains:

- **Vertical nav rail** (58px) — logo image, Accounts/Repos nav buttons with active indicator, Apply All and Theme toggle at the bottom
- **Content area** — `AccountsPanel` and `RepositoriesPanel` overlaid; visibility toggled by `ActivePage`
- **Status bar** (24px) — status text + system git identity
- **DWM title bar** — `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` called in `SourceInitialized` and on theme change

**Nav binding pattern** — inside a `ControlTemplate`, `RelativeSource AncestorType=Window` finds the `Window` object, not its `DataContext`. The fix is `ElementName`:

```xml
<!-- Window must have x:Name="RootWindow" -->
<DataTrigger Binding="{Binding DataContext.ActivePage, ElementName=RootWindow}" Value="0">
```

**Page animation** — triggered in `MainWindow.xaml.cs` when `ActivePage` changes:

```csharp
var fadeIn  = new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase() };
var slideIn = new ThicknessAnimation(new Thickness(0,14,0,0), new Thickness(0), duration);
target.BeginAnimation(OpacityProperty, fadeIn);
fe.BeginAnimation(MarginProperty, slideIn);
```

**System tray** — `WinFormsNotifyIcon` from `System.Windows.Forms`. Accessed via explicit `using` alias to avoid namespace clash with `System.Windows`:

```csharp
using WinFormsNotifyIcon = System.Windows.Forms.NotifyIcon;
```

### AccountsPanel

**File:** `GitCredMan.App/Views/AccountsPanel.xaml/.cs`

- Toolbar: Add, Edit, Delete, Detect buttons
- `ListBox` bound to `Accounts` with `AnimatedListItem` style (fade + slide in per item)
- Avatar hover-scale and star button pop-scale via `ControlTemplate` triggers (not `Style` triggers, which don't support `TargetName`)
- Detect panel expands below the list when `ShowDetectPanel` is true

### RepositoriesPanel

**File:** `GitCredMan.App/Views/RepositoriesPanel.xaml/.cs`

- Toolbar: Scan button (shows spinner when scanning), Apply All
- Scan progress banner (visible only while `IsScanning`)
- Full-width search bar with `Padding="36,8"` so the cursor starts after the icon
- `ListBox` of repo cards; account badge text set in `RepoList_LayoutUpdated` via visual tree walk
- `•••` button opens a `ContextMenu` with Open, Remove from list, Delete from disk

### AccountDialog

**File:** `GitCredMan.App/Views/AccountDialog.xaml/.cs`

Token security is handled entirely in code-behind — the token never touches the ViewModel:

```csharp
private string _tokenBuffer = string.Empty;

private void TokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
{
    _tokenBuffer = TokenPasswordBox.Password;
    _vm.NotifyTokenChanged(!string.IsNullOrEmpty(_tokenBuffer));
}

public void ClearToken()
{
    _tokenBuffer = string.Empty;
    TokenPasswordBox.Clear();
    TokenPlainBox.Text = string.Empty;
}

protected override void OnClosed(EventArgs e)
{
    ClearToken();  // Always zero on close
    base.OnClosed(e);
}
```

DWM dark/light title bar is applied in `SourceInitialized`:

```csharp
private void ApplyTitleBar()
{
    var hwnd = new WindowInteropHelper(this).Handle;
    bool isDark = Application.Current.TryFindResource("IsDark") is bool b && b;
    int dark = isDark ? 1 : 0;
    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
}
```

---

## 9. Theming System

### How it works

All control styles live in `SharedStyles.xaml` and reference **semantic brush keys** with `{DynamicResource}`:

```xml
<Setter Property="Background" Value="{DynamicResource PanelBg}"/>
<Setter Property="BorderBrush" Value="{DynamicResource SeparatorBrush}"/>
```

`DarkTheme.xaml` and `LightTheme.xaml` define those same keys with different colour values. Switching themes replaces `MergedDictionaries[0]` at runtime:

```csharp
// In App.ApplyTheme():
var merged = app.Resources.MergedDictionaries;
if (merged.Count > 0) merged.RemoveAt(0);
merged.Insert(0, new ResourceDictionary { Source = themeUri });
```

Because all bindings use `{DynamicResource}` (not `{StaticResource}`), WPF propagates colour changes immediately to every visible element.

### Semantic brush keys

| Key | Used for |
|---|---|
| `WindowBg` | Main window background |
| `PanelBg` | Nav rail, dialogs, headers |
| `CardBg` | List item / card backgrounds |
| `Surface2Brush` | Secondary card surface |
| `InputBg` / `InputBorder` / `InputFocusBorder` | TextBox / PasswordBox |
| `PrimaryText` / `SecondaryText` | Main and muted text |
| `AccentBrush` / `AccentHoverBrush` / `AccentPressBrush` | Interactive accent |
| `HoverBg` / `SelectedBg` | Hover and selection states |
| `SeparatorBrush` | Divider lines |
| `StatusBarBg` | Status bar background |
| `DangerBrush` / `DangerBgBrush` | Delete / error states |
| `SuccessBrush` / `SuccessBgBrush` | Success states |
| `IsDark` (`bool`) | Read by code-behind for DWM title bar |

### Adding a custom theme

1. Copy `DarkTheme.xaml` → `MyTheme.xaml`
2. Change the colour values
3. Add the new `AppTheme` enum value in `Models.cs`
4. Handle the new value in `App.ApplyTheme()`

---

## 10. Security Design

### Token storage

Tokens are stored using `CredWriteW` from `advapi32.dll`. Windows encrypts the blob with **DPAPI** bound to the current user's login session — only that user on that machine can decrypt it.

```
Credential key: "GitCredMan:{account-id-guid}"
Example:        "GitCredMan:3f2d1a09-bc74-4e12-9c01-d3b2f5e6a7c8"
```

`CRED_PERSIST_LOCAL_MACHINE` is used, meaning tokens survive reboots. Change to `CRED_PERSIST_SESSION` for session-only storage.

### What is never on disk

`settings.json` contains only account metadata. The `HasStoredToken` property is decorated with `[JsonIgnore]`:

```csharp
[JsonIgnore]
public bool HasStoredToken { get; set; }
```

The `PersistenceServiceTests` project includes an explicit test that asserts no token appears in the JSON output.

### Memory zeroing

| Location | Technique |
|---|---|
| Unmanaged heap (CredReadW blob) | `Span<byte>.Clear()` before `Marshal.FreeHGlobal` |
| Managed byte array | `Array.Clear()` after decoding |
| UI layer (PasswordBox) | `AccountDialog.ClearToken()` on Cancel and `OnClosed` |

> **Limitation:** C# `string` is immutable and interned. The token string itself cannot be reliably zeroed — this is a known limitation of managed languages.

### HTTPS remote URL warning

`GitConfigService` embeds the token in the HTTPS remote URL:

```
https://alice:ghp_TOKEN@github.com/org/repo.git
```

This means `git remote -v` will reveal the token to anyone with shell access to the repo directory. For higher security, use SSH key authentication — the app deliberately leaves SSH remotes unchanged.

---

## 11. Data Flow Walkthroughs

### Adding an account

```
User clicks + Add
    → AccountsPanel.xaml.cs AddBtn_Click
        → creates AccountDialogViewModel (from DI)
        → creates AccountDialog (modal)
            → user fills Name, Username, Email, Host, Token
            → token stored in AccountDialog._tokenBuffer only
        → DialogResult = true
    → VM.AddAccount(dlgVm.BuildAccount(), dlg.CollectedToken)
        → AccountService.Add(settings, account, token)
            → validates Name, Username, token
            → Settings.Accounts.Add(account)
            → _store.Save(account.Id, token)  [CredWriteW → DPAPI]
            → _settingsRepo.Save(settings)    [writes settings.json, no token]
        → Accounts.Add(account)               [ObservableCollection → UI updates]
    → dlg.ClearToken()                        [zeros _tokenBuffer]
```

### Applying an account to a repository

```
User clicks ⚡ on a repo card
    → RepositoriesPanel.xaml.cs ApplyBtn_Click
        → VM.ApplyToRepoCommand.ExecuteAsync(repo)
            → _accountSvc.Resolve(settings, repo)  → finds Account
            → _accountSvc.GetToken(account.Id)     → CredReadW → decrypted token
            → _gitCfg.ApplyAsync(repo, account, token)
                → Process.Start("git", "config user.name ...")
                → Process.Start("git", "config user.email ...")
                → Process.Start("git", "remote set-url origin https://user:token@...")
            → StatusText = "✓ Applied..."
```

### Scanning for repositories

```
User clicks Scan for Repos
    → ScanCommand.ExecuteAsync()
        → IsScanning = true
        → DispatcherTimer starts → SpinnerAngle increments 6° per tick
        → Task.Run(() => ScanDirectory(allDrives, depth: 8))
            → for each dir: check .git exists → add Repository
            → skip: Windows, node_modules, .git, AppData sub-dirs, etc.
            → IProgress<ScanProgress> → ScanStatus, ScanFound update on UI thread
        → new repos merged into Repositories collection
        → settings.json saved
        → DispatcherTimer stops → SpinnerAngle = 0
        → IsScanning = false
```

---

## 12. Running and Writing Tests

### Running

```powershell
dotnet test
# Expected: Passed 29, Failed 0
```

Or use the **Test Explorer** panel in Visual Studio (flask icon in the left sidebar).

### Test files

| File | What it tests |
|---|---|
| `AccountServiceTests.cs` | Add (valid/invalid), Update, Delete (token + repo detach + default promotion), SetDefault, Resolve (specific/default/deleted) |
| `GitConfigServiceTests.cs` | `BuildAuthenticatedUrl` (HTTPS embedding, SSH passthrough, special chars), non-existent path |
| `ModelTests.cs` | `Account` record (`with` expression, `AvatarInitial`, `DisplaySummary`), `OperationResult`, `AppSettings` defaults |
| `PersistenceServiceTests.cs` | Round-trip save/load, tokens never in JSON, corrupt file fallback, missing file fallback |
| `RepositoryScannerTests.cs` | `ReadRemoteUrl` (HTTPS, SSH, no remote, missing file), `DirectoryName`, `ShortPath` |

### Writing a new test

```csharp
public class MyServiceTests
{
    private static (MyService svc, ICredentialStore store) CreateSut()
    {
        var store   = Substitute.For<ICredentialStore>();
        var log     = Substitute.For<ILogger<MyService>>();
        var svc     = new MyService(store, log);
        return (svc, store);
    }

    [Fact]
    public void DoSomething_ValidInput_Succeeds()
    {
        var (svc, store) = CreateSut();
        store.Save(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var result = svc.DoSomething("input");

        result.Success.Should().BeTrue();
        store.Received(1).Save(Arg.Any<string>(), "input");
    }
}
```

---

## 13. Building for Distribution

### Framework-dependent (~15 MB, requires .NET 10 on target machine)

```powershell
dotnet publish GitCredMan.App -c Release -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -o .\publish
```

### Self-contained (~180 MB, no prerequisites)

```powershell
dotnet publish GitCredMan.App -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -o .\publish
```

Both produce `.\publish\GitCredMan.exe`. The self-contained build bundles the entire .NET 10 runtime.

---

## 14. Common Tasks Quick Reference

| I want to… | Where to look |
|---|---|
| Change the window size | `MainWindow.xaml` — `Width`, `Height`, `MinWidth`, `MinHeight` on the `Window` element |
| Add a new colour to the theme | `DarkTheme.xaml` and `LightTheme.xaml` — add the same key to both, reference with `{DynamicResource YourKey}` |
| Add a directory to the scan skip list | `RepositoryScannerService.cs` — `SkipNames` `HashSet` |
| Add a field to Account | `Models.cs` — add the property to `Account`. Use `[JsonIgnore]` if it should not be persisted |
| Add a new persisted setting | `Models.cs` — add the property to `AppSettings`. It serialises automatically |
| Add a new service | 1. Add interface to `IServices.cs`  2. Implement in `Services/`  3. Register in `App.xaml.cs BuildServices()`  4. Inject via constructor |
| Change how the token is stored | `WindowsCredentialStore.cs` — modify `Save()` / `Load()`. Change `CRED_PERSIST_LOCAL_MACHINE` for different persistence scope |
| Change the app icon | Replace `Resources/Icons/app.ico`. The `<ApplicationIcon>` element in `.csproj` points to it |
| Change the nav bar logo | Replace `Resources/Icons/logo_32.png`. `MainWindow.xaml` uses `<Image Source="/Resources/Icons/logo_32.png"/>` |
| Debug a startup crash | Check `Desktop/GitCredMan_crash.txt` — the full stack trace is written there by `App.xaml.cs` |
| Add a new nav page | 1. Create `Views/MyPage.xaml`  2. Add `<ColumnDefinition>` to nav rail  3. Add nav button with `DataContext.ActivePage` binding  4. Add page to content area with visibility trigger  5. Add `ActivePage` value to `MainViewModel` |

---

## 15. Glossary

| Term | Definition |
|---|---|
| **MVVM** | Model-View-ViewModel. An architectural pattern separating UI, data, and logic. |
| **WPF** | Windows Presentation Foundation. Microsoft's desktop UI framework for .NET on Windows. |
| **XAML** | Extensible Application Markup Language. XML-based syntax used to describe WPF user interfaces. |
| **Data Binding** | A WPF mechanism where UI elements automatically reflect and update ViewModel properties. |
| **ObservableProperty** | CommunityToolkit.Mvvm attribute that generates a property with `INotifyPropertyChanged`. |
| **RelayCommand** | CommunityToolkit.Mvvm attribute that generates an `ICommand` wired to a ViewModel method. |
| **DI / IoC** | Dependency Injection / Inversion of Control. Providing objects their dependencies externally. |
| **DPAPI** | Data Protection API. Windows OS service that encrypts data using the logged-in user's credentials as the key. |
| **P/Invoke** | Platform Invocation. C# mechanism for calling native Windows functions (e.g. `CredWriteW`). |
| **PAT** | Personal Access Token. A secret string that authenticates you to a git host instead of a password. |
| **DWM** | Desktop Window Manager. The Windows compositing engine. `DwmSetWindowAttribute` sets the dark/light title bar. |
| **`git config --local`** | Writes to `.git/config` inside a specific repo, overriding global settings for that repo only. |
| **ObservableCollection** | A .NET collection that raises events when items change. WPF list controls bind to these. |
| **DynamicResource** | WPF resource binding that updates at runtime (used for themes). Contrast with `StaticResource` which resolves once. |
| **NSubstitute** | .NET mocking library used in tests to create fake interface implementations. |
| **FluentAssertions** | .NET test assertion library: `result.Success.Should().BeTrue()`. |

---

*Git Credential Manager v1.0.0*