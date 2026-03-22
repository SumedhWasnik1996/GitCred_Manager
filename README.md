<div align="center">

<img src="GitCredMan/GitCredMan.App/Resources/Icons/logo_64.png" width="80" height="80" alt="Git Credential Manager logo"/>

# Git Credential Manager

**Manage multiple Git identities across all your local repositories — on Windows.**

[![Release](https://img.shields.io/github/v/release/your-org/git-cred-man?style=flat-square&color=1F6FEB)](https://github.com/your-org/git-cred-man/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square)](https://github.com/your-org/git-cred-man/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-22C55E?style=flat-square)](LICENSE)

[**Download EXE →**](https://github.com/your-org/git-cred-man/releases/latest) · [Release Notes](RELEASE_NOTES_v1.0.0.md) · [Developer Guide](docs/Developer_Guide.md)

</div>

---

## What is this?

If you work with **multiple GitHub / GitLab / company accounts** on the same machine, git can only use one global identity at a time. Every repository gets the same `user.name`, `user.email`, and credentials — making it hard to separate work from personal projects.

**Git Credential Manager** solves this by letting you:

- Store multiple git identities, each with its own Personal Access Token
- Assign a different identity to each repository
- Apply `user.name`, `user.email`, and an authenticated remote URL to any repo in one click
- Detect existing git identities already configured on your machine and import them

Tokens are encrypted at rest using **Windows Credential Manager (DPAPI)** — never written to disk in plaintext.

---


## Features

### 🔑 Accounts
- Add, edit, and delete unlimited git identities
- Each account stores: label, username, email, host (e.g. `github.com`), and a PAT
- Mark one account as the **global default** — applied to all unassigned repositories
- Detect existing git identities from `.git/config` files across your machine
- One-click import with pre-filled Add Account dialog

### 📂 Repositories
- Scans **all fixed drives** recursively for `.git` directories
- Assign a specific account to any repository, or let it inherit the global default
- **Apply All** — write the correct `user.name`, `user.email`, and HTTPS remote URL to every repo at once
- Per-card actions: Assign, Apply ⚡, Open in Explorer, Remove from list, Delete from disk

### 🎨 UI
- Vertical nav rail with custom logo and page animations
- Dark theme (GitHub Desktop-inspired) and Light theme (Windows 11 Fluent)
- Runtime theme switching — no restart required
- DWM-matched title bar colour for all windows and dialogs
- System tray — minimises to tray, restores on double-click

---

## Download

Go to the [**Releases**](/releases/latest) page and download:

| File | Description |
|---|---|
| `GitCredMan.exe` | Self-contained — no prerequisites, runs on any Windows 10/11 x64 machine |
| Source code (zip / tar.gz) | Build it yourself — see [Building from source](#building-from-source) |

---

## System Requirements

| Component | Requirement |
|---|---|
| OS | Windows 10 v1903+ · Windows 11 |
| Architecture | x64 |
| .NET | 10.0 Desktop Runtime *(framework-dependent build only)* |
| Git | Any version on the system `PATH` |

---

## Building from Source

### Prerequisites

- [Visual Studio 2022+](https://visualstudio.microsoft.com/) with **.NET desktop development** workload, **or**
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) + any editor

### Clone and build

```powershell
git clone https://github.com/your-org/git-cred-man.git
cd git-cred-man
dotnet restore
dotnet build
dotnet run --project GitCredMan.App
```

### Run tests

```powershell
dotnet test
# Expected: 29 passed, 0 failed
```

### Publish

**Framework-dependent** *(~15 MB, requires .NET 10 on target machine)*:
```powershell
dotnet publish GitCredMan.App -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o .\publish
```

**Self-contained** *(~180 MB, no prerequisites)*:
```powershell
dotnet publish GitCredMan.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o .\publish
```

---

## Project Structure

```
GitCredMan/
├── GitCredMan.Core/          # Business logic, models, services (no WPF)
│   ├── Models/Models.cs      # Account, Repository, AppSettings, DiscoveredIdentity
│   ├── Interfaces/           # ICredentialStore, ISettingsRepository, etc.
│   └── Services/             # AccountService, WindowsCredentialStore,
│                             # RepositoryScannerService, GitConfigService,
│                             # GitIdentityScanner, JsonSettingsRepository
│
├── GitCredMan.App/           # WPF presentation layer
│   ├── Views/                # MainWindow, AccountsPanel, RepositoriesPanel,
│   │                         # AccountDialog, AssignAccountDialog
│   ├── ViewModels/           # MainViewModel, AccountDialogViewModel
│   ├── Themes/               # DarkTheme.xaml, LightTheme.xaml, SharedStyles.xaml
│   ├── Converters/           # Value converters for XAML bindings
│   └── Resources/Icons/      # app.ico, logo_*.png
│
└── GitCredMan.Tests/         # xUnit · NSubstitute · FluentAssertions
    └── Core/                 # 29 tests covering all Core services
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 13 / .NET 10 |
| UI Framework | WPF (Windows Presentation Foundation) |
| Architecture | MVVM — CommunityToolkit.Mvvm 8.3 |
| Security | Windows Credential Manager via P/Invoke (`advapi32.dll`) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Serialisation | `System.Text.Json` |
| Testing | xUnit · NSubstitute · FluentAssertions |

---

## Security

- Tokens are stored encrypted using **Windows DPAPI** via `CredWriteW` / `CredReadW`
- The `settings.json` file contains **no secrets** — only account metadata (name, email, host)
- Tokens are zeroed from unmanaged memory after use (`Span<byte>.Clear()`)
- SSH remotes are never modified — only HTTPS remotes receive embedded credentials

> **Note:** Embedding a token in the HTTPS remote URL means it is visible via `git remote -v`. For higher security, use SSH key authentication instead.

---

## License

[MIT](LICENSE) — see the LICENSE file for details.
