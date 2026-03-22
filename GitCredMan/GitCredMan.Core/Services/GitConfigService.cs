using System.Diagnostics;
using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitCredMan.Core.Services;

/// <summary>
/// Applies an Account's identity to a repository via git-config.
/// Also reads the current system git identity (global user.name / user.email).
/// </summary>
public sealed class GitConfigService : IGitConfigService
{
    private readonly ILogger<GitConfigService> _log;

    public GitConfigService(ILogger<GitConfigService> log) => _log = log;

    // ── Apply account to repo ─────────────────────────────────

    public async Task<OperationResult> ApplyAsync(Repository repo, Account account, string token)
    {
        if (!Directory.Exists(repo.Path))
            return OperationResult.Fail($"Repository path not found: {repo.Path}");

        bool ok = true;

        ok &= await RunGit(repo.Path, $"config user.name \"{Esc(account.Username)}\"");
        ok &= await RunGit(repo.Path, $"config user.email \"{Esc(account.Email)}\"");

        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repo.RemoteUrl))
        {
            var authedUrl = BuildAuthenticatedUrl(repo.RemoteUrl, account.Username, token);
            if (authedUrl is not null)
                ok &= await RunGit(repo.Path, $"remote set-url origin \"{Esc(authedUrl)}\"");
        }

        _log.LogInformation(
            ok ? "Applied '{name}' to {repo}." : "Partial failure applying '{name}' to {repo}.",
            account.Name, repo.DirectoryName);

        return ok ? OperationResult.Ok() : OperationResult.Fail("One or more git config commands failed.");
    }

    public async Task<string> ReadRemoteUrlAsync(string repoPath) =>
        await Task.Run(() => RepositoryScannerService.ReadRemoteUrl(repoPath));

    // ── Read system/global git identity ──────────────────────

    /// <summary>
    /// Reads the global git identity configured on this machine
    /// via "git config --global user.name / user.email".
    /// Returns null fields if not configured.
    /// </summary>
    public static async Task<SystemGitIdentity> ReadSystemIdentityAsync()
    {
        var name  = await ReadGlobalConfig("user.name");
        var email = await ReadGlobalConfig("user.email");
        return new SystemGitIdentity(name, email);
    }

    private static async Task<string?> ReadGlobalConfig(string key)
    {
        var psi = new ProcessStartInfo("git", $"config --global {key}")
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

    // ── Authenticated URL builder ─────────────────────────────

    private static string? BuildAuthenticatedUrl(string url, string username, string token)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var ub = new UriBuilder(url)
            {
                UserName = Uri.EscapeDataString(username),
                Password = Uri.EscapeDataString(token),
            };
            return ub.Uri.AbsoluteUri;
        }
        catch { return null; }
    }

    // ── Run git ───────────────────────────────────────────────

    private async Task<bool> RunGit(string repoPath, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory       = repoPath,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        try
        {
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
                _log.LogWarning("git {args} exited {code}: {err}", args, proc.ExitCode, stderr.Trim());
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to run git {args}", args);
            return false;
        }
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>The current global git identity read from the system.</summary>
public record SystemGitIdentity(string? Name, string? Email)
{
    public bool IsConfigured => !string.IsNullOrEmpty(Name) || !string.IsNullOrEmpty(Email);
    public string Display =>
        IsConfigured
            ? $"{Name ?? "(no name)"}  ·  {Email ?? "(no email)"}"
            : "No global git identity configured";
}
