using GitCredMan.Core.Models;

namespace GitCredMan.Core.Interfaces;

/// <summary>Read/write tokens in the OS secure store.</summary>
public interface ICredentialStore
{
    bool   Save(string accountId, string token);
    string? Load(string accountId);
    bool   Delete(string accountId);
    bool   Exists(string accountId);
}

/// <summary>Persist and load application settings (no secrets).</summary>
public interface ISettingsRepository
{
    AppSettings Load();
    void Save(AppSettings settings);
    string DataFilePath { get; }
}

/// <summary>Scan the file system for Git repositories.</summary>
public interface IRepositoryScanner
{
    Task<IReadOnlyList<Repository>> ScanAsync(
        IProgress<ScanProgress>? progress        = null,
        CancellationToken        cancellationToken = default);
}

/// <summary>Apply an Account's identity to a Repository via git-config.</summary>
public interface IGitConfigService
{
    Task<OperationResult> ApplyAsync(Repository repo, Account account, string token);
    Task<string> ReadRemoteUrlAsync(string repoPath);
}
