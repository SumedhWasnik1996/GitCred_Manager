using FluentAssertions;
using GitCredMan.Core.Models;
using GitCredMan.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace GitCredMan.Tests.Core;

public class GitConfigServiceTests
{
    private static GitConfigService CreateSut() =>
        new(NullLogger<GitConfigService>.Instance);

    private static string? CallBuildUrl(string url, string username, string token)
    {
        var method = typeof(GitConfigService)
            .GetMethod("BuildAuthenticatedUrl",
                BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)method.Invoke(null, [url, username, token]);
    }

    // ══════════════════════════════════════════════════════════
    //  BuildAuthenticatedUrl
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void BuildUrl_Https_EmbedsCredentials()
    {
        var result = CallBuildUrl(
            "https://github.com/user/repo.git",
            "alice", "ghp_mytoken");

        result.Should().NotBeNull();
        result.Should().Contain("alice");
        result.Should().Contain("ghp_mytoken");
        result.Should().StartWith("https://");
        result.Should().Contain("github.com");
    }

    [Fact]
    public void BuildUrl_Ssh_ReturnsNull()
    {
        // SSH remotes should not be modified
        var result = CallBuildUrl(
            "git@github.com:user/repo.git",
            "alice", "token");

        result.Should().BeNull();
    }

    [Fact]
    public void BuildUrl_SpecialCharsInToken_AreUriEncoded()
    {
        var result = CallBuildUrl(
            "https://github.com/user/repo.git",
            "alice", "tok@en#wi!th$special");

        result.Should().NotBeNull();
        // Special chars must be encoded; raw '@' in password would break the URL
        result.Should().NotContain(":tok@en#");
    }

    [Fact]
    public void BuildUrl_ExistingCredentialsInUrl_AreReplaced()
    {
        var result = CallBuildUrl(
            "https://olduser:oldtoken@github.com/user/repo.git",
            "newuser", "newtoken");

        result.Should().NotBeNull();
        result.Should().Contain("newuser");
        result.Should().Contain("newtoken");
        result.Should().NotContain("olduser");
        result.Should().NotContain("oldtoken");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com/repo")]
    public void BuildUrl_NonHttps_ReturnsNull(string url)
    {
        var result = CallBuildUrl(url, "user", "token");
        result.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════
    //  ApplyAsync — filesystem boundary test
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyAsync_NonExistentPath_ReturnsFailure()
    {
        var svc  = CreateSut();
        var repo = new Repository { Path = @"Z:\nonexistent\path\abc123" };
        var acc  = new Account { Name = "Test", Username = "user", Email = "u@e.com" };

        var result = await svc.ApplyAsync(repo, acc, "token");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
