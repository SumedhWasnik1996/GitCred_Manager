using FluentAssertions;
using GitCredMan.Core.Models;
using Xunit;

namespace GitCredMan.Tests.Core;

public class ModelTests
{
    // ══════════════════════════════════════════════════════════
    //  Account
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Account_NewInstance_HasUniqueId()
    {
        var a = new Account();
        var b = new Account();
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Account_NewInstance_IsNotDefault()
    {
        var a = new Account();
        a.IsDefault.Should().BeFalse();
    }

    [Theory]
    [InlineData("Alice",   "A")]
    [InlineData("bob",     "B")]
    [InlineData("",        "?")]
    public void Account_AvatarInitial_IsUppercaseFirstChar(string name, string expected)
    {
        var a = new Account { Name = name };
        a.AvatarInitial.Should().Be(expected);
    }

    [Fact]
    public void Account_DisplaySummary_WithEmail()
    {
        var a = new Account { Username = "alice", Email = "alice@example.com", Host = "github.com" };
        a.DisplaySummary.Should().Contain("alice@example.com");
        a.DisplaySummary.Should().Contain("github.com");
    }

    [Fact]
    public void Account_DisplaySummary_WithoutEmail_ShowsUsername()
    {
        var a = new Account { Username = "alice", Email = "", Host = "github.com" };
        a.DisplaySummary.Should().Contain("alice");
        a.DisplaySummary.Should().NotContain("@example");
    }

    [Fact]
    public void Account_With_CopiesValues()
    {
        var original = new Account { Name = "Original", Username = "user" };
        var copy     = original with { Name = "Modified" };

        // "with" creates a new instance with the changed property
        copy.Id.Should().Be(original.Id);
        copy.Name.Should().Be("Modified");
        original.Name.Should().Be("Original"); // original untouched
    }

    // ══════════════════════════════════════════════════════════
    //  OperationResult
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void OperationResult_Ok_IsSuccess()
    {
        var r = OperationResult.Ok();
        r.Success.Should().BeTrue();
        r.Error.Should().BeNull();
    }

    [Fact]
    public void OperationResult_Fail_HasError()
    {
        var r = OperationResult.Fail("Something went wrong");
        r.Success.Should().BeFalse();
        r.Error.Should().Be("Something went wrong");
    }

    // ══════════════════════════════════════════════════════════
    //  AppSettings
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void AppSettings_NewInstance_HasEmptyCollections()
    {
        var s = new AppSettings();
        s.Accounts.Should().BeEmpty();
        s.Repositories.Should().BeEmpty();
        s.ExcludedPaths.Should().BeEmpty();
    }

    [Fact]
    public void AppSettings_DefaultTheme_IsDark()
    {
        var s = new AppSettings();
        s.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void AppSettings_DefaultScanDepth_IsEight()
    {
        var s = new AppSettings();
        s.ScanDepth.Should().Be(8);
    }
}
