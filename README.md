# Git Credential Manager
### C# · .NET 10 · WPF

A native Windows desktop application for managing multiple Git identities across all your local repositories. Tokens stored encrypted in **Windows Credential Manager** (DPAPI). Switchable **Dark** (GitHub Desktop) and **Light** (Windows 11 Fluent) themes.

---

## Solution structure

```
GitCredMan.sln
├── GitCredMan.Core/          # Pure C# class library — no WPF dependency
│   ├── Models/Models.cs          Account, Repository, AppSettings, enums, OperationResult
│   ├── Interfaces/IServices.cs   ICredentialStore, ISettingsRepository, IRepositoryScanner, IGitConfigService
│   └── Services/
│       ├── WindowsCredentialStore.cs   P/Invoke advapi32 — DPAPI-backed token storage
│       ├── JsonSettingsRepository.cs   %APPDATA%\GitCredMan\settings.json (no secrets)
│       ├── RepositoryScannerService.cs Async recursive scanner, cancellable, progress-reporting
│       ├── GitConfigService.cs         Applies user.name / user.email / remote URL via git-config
│       └── AccountService.cs           Domain logic — CRUD, defaults, token coordination
│
├── GitCredMan.App/           # WPF presentation layer
│   ├── App.xaml / App.xaml.cs            DI container, single-instance mutex, theme bootstrap
│   ├── Themes/
│   │   ├── DarkTheme.xaml                GitHub Desktop-style dark palette
│   │   ├── LightTheme.xaml               Windows 11 Fluent light palette
│   │   └── SharedStyles.xaml             All control templates (theme-agnostic semantic keys)
│   ├── Converters/Converters.cs
│   ├── ViewModels/
│   │   ├── MainViewModel.cs              Accounts + Repos + Scan + Theme + Filter
│   │   ├── AccountDialogViewModel.cs     Add/Edit form state + validation
│   │   └── AssignAccountDialogViewModel.cs
│   └── Views/
│       ├── MainWindow.xaml/.cs           Chrome, DWM dark title bar, tray icon
│       ├── AccountsPanel.xaml/.cs        Account list with cards, badges, star button
│       ├── RepositoriesPanel.xaml/.cs    Repo list with filter, scan progress, per-row actions
│       ├── AccountDialog.xaml/.cs        Secure add/edit form with show/hide token
│       └── AssignAccountDialog.xaml/.cs  Per-repo account assignment
│
└── GitCredMan.Tests/         # xUnit · NSubstitute · FluentAssertions
    └── Core/
        ├── AccountServiceTests.cs        28 tests — CRUD, defaults, token store, resolve
        ├── RepositoryScannerTests.cs     Remote URL parsing, path helpers
        ├── GitConfigServiceTests.cs      URL builder, SSH passthrough, nonexistent path
        ├── ModelTests.cs                 Account, OperationResult, AppSettings invariants
        └── PersistenceServiceTests.cs    Round-trip JSON, corrupt file, token exclusion
```

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0+ |
| Visual Studio | 2022 17.8+ (or `dotnet` CLI) |
| Windows | 10 1903+ (for DWM dark title bar) |
| git | Any — must be on `PATH` |

---

## Build & run

```powershell
# Clone / extract, then:
cd GitCredMan
dotnet restore
dotnet build

# Run the app
dotnet run --project GitCredMan.App

# Run all tests
dotnet test

# Publish single-file exe
dotnet publish GitCredMan.App -c Release -r win-x64 --self-contained false -o ./publish
```

---

## Security design

### Token storage
Tokens are stored exclusively via **Windows Credential Manager** (`CredWriteW` / `CredReadW`).  
Key format: `GitCredMan:{account-id}`.  
The OS encrypts the blob with **DPAPI**, scoped to the current Windows user session.  
On deletion, `CredDeleteW` is called. Token buffers in unmanaged memory are zeroed with `Span<byte>.Clear()` before `Marshal.FreeHGlobal`. Managed token strings are nilled and `GC.Collect` is hinted after use.

### What is never stored
- The `settings.json` file contains **no tokens, passwords, or secrets**.  
- `Account.HasStoredToken` is `[JsonIgnore]` — confirmed by the persistence tests.  
- The `AccountDialog` code-behind zeros `_tokenBuffer` in `ClearToken()` and calls it from both `Cancel` and `OnClosed`.

### Preventing accidental logout
There is no "sign out" button. The only way to remove an account's credentials is the explicit **Delete** flow, which shows a confirmation dialog listing all consequences.

### git remote URL embedding
For HTTPS remotes, the authenticated URL `https://user:token@host/...` is written to the **local** `.git/config` via `git remote set-url origin`. This never modifies `~/.gitconfig`. SSH remotes are detected and left untouched.

---

## Theme switching

The theme toggle in the header bar swaps `DarkTheme.xaml` ↔ `LightTheme.xaml` at runtime by replacing the first entry in `Application.Resources.MergedDictionaries`. All control styles reference **semantic brush keys** (`WindowBg`, `PrimaryText`, `AccentBrush`, etc.) defined in both theme files, so every control re-renders immediately with no restart required. The DWM title bar dark/light mode is also updated via `DwmSetWindowAttribute`.

---

## Running the tests

```powershell
dotnet test --logger "console;verbosity=detailed"
```

The test suite covers all Core services without any WPF dependency. `PersistenceServiceTests` uses a temp directory override via an `internal` constructor on `JsonSettingsRepository`.

