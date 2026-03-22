using FluentAssertions;
using GitCredMan.Core.Services;
using Xunit;

namespace GitCredMan.Tests.Core;

public class RepositoryScannerTests
{
    // ══════════════════════════════════════════════════════════
    //  ReadRemoteUrl  (internal static — tested via reflection)
    // ══════════════════════════════════════════════════════════

    private static string CallReadRemoteUrl(string repoPath)
    {
        var method = typeof(RepositoryScannerService)
            .GetMethod("ReadRemoteUrl",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [repoPath])!;
    }

    [Fact]
    public void ReadRemoteUrl_ValidConfig_ReturnsUrl()
    {
        var dir = CreateTempGitRepo("""
            [core]
                repositoryformatversion = 0
            [remote "origin"]
                url = https://github.com/user/repo.git
                fetch = +refs/heads/*:refs/remotes/origin/*
            """);

        var url = CallReadRemoteUrl(dir);

        url.Should().Be("https://github.com/user/repo.git");
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ReadRemoteUrl_SshRemote_ReturnsUrl()
    {
        var dir = CreateTempGitRepo("""
            [remote "origin"]
                url = git@github.com:user/repo.git
            """);

        var url = CallReadRemoteUrl(dir);

        url.Should().Be("git@github.com:user/repo.git");
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ReadRemoteUrl_NoRemoteSection_ReturnsEmpty()
    {
        var dir = CreateTempGitRepo("""
            [core]
                repositoryformatversion = 0
            """);

        var url = CallReadRemoteUrl(dir);

        url.Should().BeEmpty();
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ReadRemoteUrl_MissingConfig_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        var url = CallReadRemoteUrl(dir);

        url.Should().BeEmpty();
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ReadRemoteUrl_MultipleRemotes_ReturnsFirstOne()
    {
        var dir = CreateTempGitRepo("""
            [remote "origin"]
                url = https://github.com/user/main.git
            [remote "upstream"]
                url = https://github.com/org/main.git
            """);

        var url = CallReadRemoteUrl(dir);

        url.Should().Be("https://github.com/user/main.git");
        Directory.Delete(dir, recursive: true);
    }

    // ══════════════════════════════════════════════════════════
    //  Repository model helpers
    // ══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(@"C:\Users\alice\code\my-project",    "my-project")]
    [InlineData(@"C:\code\some-lib\",                 "some-lib")]
    [InlineData(@"/home/alice/repos/api",              "api")]
    public void Repository_DirectoryName_IsLastSegment(string path, string expected)
    {
        var repo = new GitCredMan.Core.Models.Repository { Path = path };
        repo.DirectoryName.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://github.com/user/repo.git",  "github.com")]
    [InlineData("git@github.com:user/repo.git",      "")]       // SSH — no parseable host
    [InlineData("",                                  "")]
    public void Repository_HostLabel_ParsedFromRemoteUrl(string url, string expected)
    {
        var repo = new GitCredMan.Core.Models.Repository { RemoteUrl = url };
        repo.HostLabel.Should().Be(expected);
    }

    // ══════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════

    private static string CreateTempGitRepo(string configContent)
    {
        var root   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var gitDir = Path.Combine(root, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), configContent);
        return root;
    }
}
