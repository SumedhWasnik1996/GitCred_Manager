using System.Diagnostics;
using GitCredMan.Core.Models;

namespace GitCredMan.Core.Services;

/// <summary>
/// Scans all known repositories (and global git config) for existing
/// git identities (user.name / user.email) and returns them deduplicated.
/// This lets users discover and import pre-existing credentials they've
/// already configured on this machine.
/// </summary>
public static class GitIdentityScanner
{
    /// <summary>
    /// Scan all repositories plus the global git config.
    /// Returns deduplicated identities, each marked if already in accounts.
    /// </summary>
    public static async Task<List<DiscoveredIdentity>> ScanAsync(
        IEnumerable<Repository> repositories,
        IEnumerable<Account>    existingAccounts,
        IProgress<string>?      progress = null)
    {
        var seen      = new Dictionary<string, DiscoveredIdentity>(StringComparer.OrdinalIgnoreCase);
        var accountKeys = existingAccounts
            .Select(a => $"{a.Username}|{a.Email}|{a.Host}".ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── 1. Global git config ──────────────────────────────
        progress?.Report("Reading global git config…");
        var globalIdentity = await ReadGlobalIdentityAsync();
        if (globalIdentity is not null)
        {
            globalIdentity.AlreadyImported = accountKeys.Contains(globalIdentity.Key);
            seen[globalIdentity.Key] = globalIdentity;
        }

        // ── 2. Each repo's local .git/config ──────────────────
        foreach (var repo in repositories)
        {
            if (!Directory.Exists(repo.Path)) continue;
            progress?.Report($"Reading {Path.GetFileName(repo.Path.TrimEnd('\\', '/'))}…");

            var identity = ReadLocalIdentity(repo);
            if (identity is null) continue;

            // Skip if identical to global (no local override)
            if (globalIdentity is not null &&
                identity.Username == globalIdentity.Username &&
                identity.Email    == globalIdentity.Email    &&
                identity.Host     == globalIdentity.Host) continue;

            if (!seen.ContainsKey(identity.Key))
            {
                identity.AlreadyImported = accountKeys.Contains(identity.Key);
                seen[identity.Key] = identity;
            }
        }

        // Sort: global first, then by username
        return seen.Values
            .OrderByDescending(i => i.IsGlobal)
            .ThenBy(i => i.Username)
            .ThenBy(i => i.Email)
            .ToList();
    }

    // ── Read global git identity ──────────────────────────────

    private static async Task<DiscoveredIdentity?> ReadGlobalIdentityAsync()
    {
        var name  = await RunGitGlobal("config --global user.name");
        var email = await RunGitGlobal("config --global user.email");

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
            return null;

        return new DiscoveredIdentity
        {
            Username  = name  ?? string.Empty,
            Email     = email ?? string.Empty,
            Host      = "github.com",   // default — user can adjust on import
            Source    = "Global",
            IsGlobal  = true,
        };
    }

    // ── Read per-repo identity from .git/config ───────────────

    private static DiscoveredIdentity? ReadLocalIdentity(Repository repo)
    {
        try
        {
            var configPath = Path.Combine(repo.Path, ".git", "config");
            if (!File.Exists(configPath)) return null;

            string? name    = null;
            string? email   = null;
            string? remoteUrl = null;
            bool inUser     = false;
            bool inRemote   = false;

            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inUser   = line.StartsWith("[user",   StringComparison.OrdinalIgnoreCase);
                    inRemote = line.StartsWith("[remote", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim().ToLowerInvariant();
                var val = line[(eq + 1)..].Trim();

                if (inUser)
                {
                    if (key == "name")  name  = val;
                    if (key == "email") email = val;
                }
                else if (inRemote && key == "url" && remoteUrl is null)
                {
                    remoteUrl = val;
                }
            }

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
                return null;

            // Extract host from remote URL
            var host = ExtractHost(remoteUrl) ?? ExtractHost(repo.RemoteUrl) ?? "github.com";

            return new DiscoveredIdentity
            {
                Username = name  ?? string.Empty,
                Email    = email ?? string.Empty,
                Host     = host,
                Source   = repo.Path,
                IsGlobal = false,
            };
        }
        catch { return null; }
    }

    // ── Extract host from a git remote URL ───────────────────

    private static string? ExtractHost(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // HTTPS: https://github.com/user/repo.git
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(url).Host; }
            catch { }
        }

        // SSH: git@github.com:user/repo.git
        if (url.StartsWith("git@"))
        {
            var at    = url.IndexOf('@');
            var colon = url.IndexOf(':');
            if (at >= 0 && colon > at)
                return url[(at + 1)..colon];
        }

        return null;
    }

    // ── Run git command, return trimmed stdout ────────────────

    private static async Task<string?> RunGitGlobal(string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }
}
