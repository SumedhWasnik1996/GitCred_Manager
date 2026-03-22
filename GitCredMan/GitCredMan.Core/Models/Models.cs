using System.Text.Json.Serialization;

namespace GitCredMan.Core.Models;

// ────────────────────────────────────────────────────────────
//  Account
// ────────────────────────────────────────────────────────────

/// <summary>
/// A stored Git identity. Tokens are NEVER stored here —
/// they live exclusively in Windows Credential Manager.
/// </summary>
public sealed record class Account
{
    public string   Id          { get; init; } = Guid.NewGuid().ToString("D");
    public string   Name        { get; set; }  = string.Empty;   // "Work GitHub"
    public string   Username    { get; set; }  = string.Empty;
    public string   Email       { get; set; }  = string.Empty;
    public string   Host        { get; set; }  = "github.com";
    public bool     IsDefault   { get; set; }
    public DateTime CreatedAt   { get; init; } = DateTime.UtcNow;

    /// <summary>Not persisted — checked live from Credential Manager.</summary>
    [JsonIgnore]
    public bool HasStoredToken { get; set; }

    [JsonIgnore]
    public string AvatarInitial =>
        Name.Length > 0 ? Name[0].ToString().ToUpperInvariant() : "?";

    [JsonIgnore]
    public string DisplaySummary =>
        string.IsNullOrWhiteSpace(Email)
            ? $"{Username} · {Host}"
            : $"{Email} · {Host}";

}

// ────────────────────────────────────────────────────────────
//  Repository
// ────────────────────────────────────────────────────────────

public sealed class Repository
{
    public string   Path         { get; set; }  = string.Empty;
    public string   RemoteUrl    { get; set; }  = string.Empty;
    public bool     HasRemote    { get; set; }
    public string?  AccountId    { get; set; }   // null → global default
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;

    [JsonIgnore]
    public string DirectoryName =>
        System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));

    [JsonIgnore]
    public string ShortPath
    {
        get
        {
            var parts = Path.TrimEnd('\\', '/').Split(new[] { '\\', '/' },
                StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"…\\{parts[^2]}\\{parts[^1]}"
                : Path;
        }
    }

    [JsonIgnore]
    public string HostLabel
    {
        get
        {
            if (string.IsNullOrEmpty(RemoteUrl)) return string.Empty;
            try { return new Uri(RemoteUrl).Host; }
            catch { return string.Empty; }
        }
    }
}

// ────────────────────────────────────────────────────────────
//  AppSettings  (persisted, never contains secrets)
// ────────────────────────────────────────────────────────────

public sealed class AppSettings
{
    public List<Account>    Accounts          { get; set; } = [];
    public List<Repository> Repositories      { get; set; } = [];
    public string?          DefaultAccountId  { get; set; }
    public int              ScanDepth         { get; set; } = 8;
    public List<string>     ExcludedPaths     { get; set; } = [];
    public AppTheme         Theme             { get; set; } = AppTheme.Dark;
    public bool             MinimizeToTray    { get; set; } = true;
    public bool             StartMinimized    { get; set; } = false;
    public string           Version           { get; set; } = "1.0.0";
}

// ────────────────────────────────────────────────────────────
//  Enums
// ────────────────────────────────────────────────────────────

public enum AppTheme
{
    Dark,    // GitHub Desktop-style dark
    Light,   // Windows 11 Fluent light
}

// ────────────────────────────────────────────────────────────
//  Result types
// ────────────────────────────────────────────────────────────

public record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok()               => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}

public record ScanProgress(string CurrentPath, int FoundCount, bool IsComplete = false);

// ────────────────────────────────────────────────────────────
//  DiscoveredIdentity  — a git identity found in local configs
// ────────────────────────────────────────────────────────────

/// <summary>
/// A git identity (name + email + host) discovered by reading
/// .git/config files across all known repositories, plus the
/// global ~/.gitconfig.
/// </summary>
public sealed class DiscoveredIdentity
{
    public string  Username    { get; set; } = string.Empty;
    public string  Email       { get; set; } = string.Empty;
    public string  Host        { get; set; } = string.Empty;
    public string  Source      { get; set; } = string.Empty; // "Global" or repo path
    public bool    IsGlobal    { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrEmpty(Username) ? Username
        : !string.IsNullOrEmpty(Email)  ? Email
        : "(unnamed)";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary =>
        string.IsNullOrEmpty(Email)
            ? $"{Username} · {Host}"
            : $"{Email} · {Host}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceLabel =>
        IsGlobal ? "Global git config" : $"Repo: {System.IO.Path.GetFileName(Source.TrimEnd('\\', '/'))}";

    /// <summary>Unique key for deduplication.</summary>
    public string Key => $"{Username}|{Email}|{Host}".ToLowerInvariant();

    /// <summary>True if this identity already exists in the account list.</summary>
    public bool AlreadyImported { get; set; }
}
