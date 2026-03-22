using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitCredMan.Core.Services;

/// <summary>
/// Recursively scans all fixed drives for .git directories.
/// Reports progress via IProgress and supports cancellation.
/// </summary>
public sealed class RepositoryScannerService : IRepositoryScanner
{
    private readonly ILogger<RepositoryScannerService> _log;
    private readonly int _maxDepth;

    // Directories never worth descending into
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows system dirs
        "Windows", "Program Files", "Program Files (x86)", "ProgramData",
        "Recovery", "System Volume Information", "$Recycle.Bin",
        "WinSxS", "SoftwareDistribution",
        // Package caches — huge and never contain repos
        "node_modules", ".npm", ".cargo", ".gradle", "Pods",
        "__pycache__", ".venv", "venv", "env", ".env",
        "vendor", "bower_components",
        // Don't recurse INTO .git itself
        ".git",
        // AppData sub-dirs that are never repos
        "Local", "Roaming", "LocalLow",
    };

    public RepositoryScannerService(ILogger<RepositoryScannerService> log, int maxDepth = 8)
    {
        _log      = log;
        _maxDepth = maxDepth;
    }

    public async Task<IReadOnlyList<Repository>> ScanAsync(
        IProgress<ScanProgress>? progress        = null,
        CancellationToken        cancellationToken = default)
    {
        var roots  = GetScanRoots();
        var result = new List<Repository>();
        int found  = 0;

        _log.LogInformation("Starting scan across {count} roots.", roots.Count);

        await Task.Run(() =>
        {
            foreach (var root in roots)
            {
                if (cancellationToken.IsCancellationRequested) break;
                _log.LogDebug("Scanning root: {root}", root);
                ScanDirectory(root, _maxDepth, result, ref found, progress, cancellationToken);
            }
            progress?.Report(new ScanProgress(string.Empty, found, IsComplete: true));
        }, cancellationToken);

        _log.LogInformation("Scan complete. Found {count} repositories.", found);
        return result;
    }

    // ── Build scan roots ──────────────────────────────────────
    // Scan ALL fixed drives. User profile is already under one of them.
    // We don't skip drives based on home dir — that caused repos outside
    // the user profile to be missed entirely.

    private static List<string> GetScanRoots()
    {
        var roots = new List<string>();

        // All fixed drives — full scan
        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            roots.Add(drive.RootDirectory.FullName);
        }

        // If no fixed drives found (unlikely), fall back to user profile
        if (roots.Count == 0)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
                roots.Add(home);
        }

        return roots;
    }

    // ── Recursive walk ────────────────────────────────────────

    private static void ScanDirectory(
        string path, int depth,
        List<Repository> found,
        ref int count,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        if (depth < 0 || ct.IsCancellationRequested) return;

        try
        {
            progress?.Report(new ScanProgress(path, count));

            // Is this a Git repo?
            if (Directory.Exists(Path.Combine(path, ".git")))
            {
                var repo = BuildRepository(path);
                lock (found) found.Add(repo);
                count++;
                progress?.Report(new ScanProgress(path, count));
                return; // Don't recurse into nested repos
            }

            foreach (var sub in Directory.EnumerateDirectories(path))
            {
                if (ct.IsCancellationRequested) break;

                var name  = Path.GetFileName(sub);
                var attrs = File.GetAttributes(sub);

                if (SkipNames.Contains(name))                                    continue;
                if ((attrs & FileAttributes.System)    != 0)                     continue;
                if ((attrs & FileAttributes.ReparsePoint) != 0)                  continue; // symlinks
                // Allow hidden dirs (repos like .config/... can be hidden on some setups)
                // but skip obvious system hidden dirs
                if ((attrs & FileAttributes.Hidden) != 0 && name.StartsWith('$')) continue;

                ScanDirectory(sub, depth - 1, found, ref count, progress, ct);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException)  { }
        catch (IOException)                 { }
    }

    // ── Build Repository from path ────────────────────────────

    private static Repository BuildRepository(string path)
    {
        var remoteUrl = ReadRemoteUrl(path);
        return new Repository
        {
            Path      = path,
            RemoteUrl = remoteUrl,
            HasRemote = !string.IsNullOrEmpty(remoteUrl),
        };
    }

    // ── Parse remote URL from .git/config ────────────────────

    internal static string ReadRemoteUrl(string repoPath)
    {
        try
        {
            var configPath = Path.Combine(repoPath, ".git", "config");
            if (!File.Exists(configPath)) return string.Empty;

            bool inRemote = false;
            foreach (var raw in File.ReadLines(configPath))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                    inRemote = line.StartsWith("[remote", StringComparison.OrdinalIgnoreCase);
                else if (inRemote)
                {
                    var eq = line.IndexOf('=');
                    if (eq > 0 && line[..eq].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
                        return line[(eq + 1)..].Trim();
                }
            }
        }
        catch { }
        return string.Empty;
    }
}
