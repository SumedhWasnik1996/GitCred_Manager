using FluentAssertions;
using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using GitCredMan.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace GitCredMan.Tests.Core;

public class AccountServiceTests
{
    // ── Helpers ───────────────────────────────────────────────

    private static (AccountService svc, ICredentialStore store, ISettingsRepository repo)
        CreateSut()
    {
        var store    = Substitute.For<ICredentialStore>();
        var settings = Substitute.For<ISettingsRepository>();
        store.Save(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var svc = new AccountService(store, settings, NullLogger<AccountService>.Instance);
        return (svc, store, settings);
    }

    private static AppSettings EmptySettings() => new();

    private static Account MakeAccount(string name = "Test", string id = "") => new()
    {
        Id       = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("D") : id,
        Name     = name,
        Username = "testuser",
        Email    = "test@example.com",
        Host     = "github.com",
    };

    // ══════════════════════════════════════════════════════════
    //  ADD
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Add_ValidAccount_ReturnsSuccess()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var account     = MakeAccount();

        var result = svc.Add(s, account, "ghp_token123");

        result.Success.Should().BeTrue();
        s.Accounts.Should().ContainSingle(a => a.Id == account.Id);
    }

    [Fact]
    public void Add_FirstAccount_AutomicallyBecomesDefault()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var account     = MakeAccount();

        svc.Add(s, account, "ghp_token");

        s.DefaultAccountId.Should().Be(account.Id);
        account.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Add_SecondAccount_DoesNotReplaceDefault()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var first       = MakeAccount("First");
        var second      = MakeAccount("Second");

        svc.Add(s, first,  "token1");
        svc.Add(s, second, "token2");

        s.DefaultAccountId.Should().Be(first.Id);
        s.Accounts.Should().HaveCount(2);
    }

    [Fact]
    public void Add_MissingName_ReturnsFailure()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var account     = MakeAccount() with { Name = "" };

        var result = svc.Add(s, account, "token");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        s.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void Add_MissingToken_ReturnsFailure()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();

        var result = svc.Add(s, MakeAccount(), token: null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Token");
    }

    [Fact]
    public void Add_SavesTokenToCredentialStore()
    {
        var (svc, store, _) = CreateSut();
        var s               = EmptySettings();
        var account         = MakeAccount();
        const string token  = "ghp_supersecret";

        svc.Add(s, account, token);

        store.Received(1).Save(account.Id, token);
    }

    // ══════════════════════════════════════════════════════════
    //  UPDATE
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Update_ExistingAccount_PersistsMutation()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var account     = MakeAccount();
        svc.Add(s, account, "token");

        var updated = account with { Name = "Updated Name", Email = "new@email.com" };
        var result  = svc.Update(s, updated, newToken: null);

        result.Success.Should().BeTrue();
        s.Accounts.Single().Name.Should().Be("Updated Name");
        s.Accounts.Single().Email.Should().Be("new@email.com");
    }

    [Fact]
    public void Update_WithNewToken_SavesNewTokenToStore()
    {
        var (svc, store, _) = CreateSut();
        var s               = EmptySettings();
        var account         = MakeAccount();
        svc.Add(s, account, "old_token");

        svc.Update(s, account, "new_token");

        store.Received(1).Save(account.Id, "new_token");
    }

    [Fact]
    public void Update_WithNullToken_DoesNotTouchCredentialStore()
    {
        var (svc, store, _) = CreateSut();
        var s               = EmptySettings();
        var account         = MakeAccount();
        svc.Add(s, account, "token");
        store.ClearReceivedCalls();

        svc.Update(s, account, newToken: null);

        store.DidNotReceive().Save(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void Update_NonExistentAccount_ReturnsFailure()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();

        var result = svc.Update(s, MakeAccount("Ghost"), null);

        result.Success.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════
    //  DELETE
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Delete_RemovesAccountFromList()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var account     = MakeAccount();
        svc.Add(s, account, "token");

        svc.Delete(s, account.Id);

        s.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void Delete_DeletesTokenFromCredentialStore()
    {
        var (svc, store, _) = CreateSut();
        var s               = EmptySettings();
        var account         = MakeAccount();
        svc.Add(s, account, "token");

        svc.Delete(s, account.Id);

        store.Received(1).Delete(account.Id);
    }

    [Fact]
    public void Delete_DefaultAccount_PromotesNextAccount()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var first       = MakeAccount("First");
        var second      = MakeAccount("Second");
        svc.Add(s, first,  "t1");
        svc.Add(s, second, "t2");

        svc.Delete(s, first.Id);

        s.DefaultAccountId.Should().Be(second.Id);
        second.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Delete_DetachesAssignedRepositories()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var account     = MakeAccount();
        svc.Add(s, account, "token");
        var repo = new Repository { Path = @"C:\code\myrepo", AccountId = account.Id };
        s.Repositories.Add(repo);

        svc.Delete(s, account.Id);

        repo.AccountId.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════
    //  SET DEFAULT
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void SetDefault_UpdatesIsDefaultOnAllAccounts()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var a           = MakeAccount("A");
        var b           = MakeAccount("B");
        svc.Add(s, a, "t1");
        svc.Add(s, b, "t2");

        svc.SetDefault(s, b.Id);

        a.IsDefault.Should().BeFalse();
        b.IsDefault.Should().BeTrue();
        s.DefaultAccountId.Should().Be(b.Id);
    }

    // ══════════════════════════════════════════════════════════
    //  RESOLVE
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_RepoWithSpecificAccount_ReturnsThatAccount()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var a           = MakeAccount("A");
        var b           = MakeAccount("B");
        svc.Add(s, a, "t1");
        svc.Add(s, b, "t2");
        svc.SetDefault(s, a.Id);

        var repo = new Repository { Path = @"C:\code\r", AccountId = b.Id };

        svc.Resolve(s, repo)!.Id.Should().Be(b.Id);
    }

    [Fact]
    public void Resolve_RepoWithNoAccount_ReturnsDefault()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var def         = MakeAccount("Default");
        svc.Add(s, def, "token");
        var repo = new Repository { Path = @"C:\code\r", AccountId = null };

        svc.Resolve(s, repo)!.Id.Should().Be(def.Id);
    }

    [Fact]
    public void Resolve_RepoWithDeletedAccountId_FallsBackToDefault()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var def         = MakeAccount("Default");
        svc.Add(s, def, "token");
        var repo = new Repository { Path = @"C:\code\r", AccountId = "nonexistent-id" };

        svc.Resolve(s, repo)!.Id.Should().Be(def.Id);
    }

    [Fact]
    public void Resolve_NoAccounts_ReturnsNull()
    {
        var (svc, _, _) = CreateSut();
        var s           = EmptySettings();
        var repo        = new Repository { Path = @"C:\code\r" };

        svc.Resolve(s, repo).Should().BeNull();
    }
}
