using FluentAssertions;
using GitCredMan.Core.Models;
using GitCredMan.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GitCredMan.Tests.Core;

/// <summary>
/// Tests JsonSettingsRepository using a temp directory instead of %APPDATA%,
/// by subclassing to override the data path.
/// </summary>
public class PersistenceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestableSettingsRepository _sut;

    public PersistenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GitCredManTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new TestableSettingsRepository(_tempDir, NullLogger<JsonSettingsRepository>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ══════════════════════════════════════════════════════════
    //  Round-trip
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void SaveAndLoad_RoundTrips_Accounts()
    {
        var settings = new AppSettings();
        settings.Accounts.Add(new Account
        {
            Name     = "Work",
            Username = "alice",
            Email    = "alice@work.com",
            Host     = "github.com",
        });

        _sut.Save(settings);
        var loaded = _sut.Load();

        loaded.Accounts.Should().HaveCount(1);
        loaded.Accounts[0].Name.Should().Be("Work");
        loaded.Accounts[0].Email.Should().Be("alice@work.com");
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_Repositories()
    {
        var settings = new AppSettings();
        settings.Repositories.Add(new Repository
        {
            Path      = @"C:\code\myrepo",
            RemoteUrl = "https://github.com/user/myrepo.git",
            HasRemote = true,
            AccountId = "some-id",
        });

        _sut.Save(settings);
        var loaded = _sut.Load();

        loaded.Repositories.Should().HaveCount(1);
        loaded.Repositories[0].Path.Should().Be(@"C:\code\myrepo");
        loaded.Repositories[0].AccountId.Should().Be("some-id");
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_Theme()
    {
        var settings = new AppSettings { Theme = AppTheme.Light };
        _sut.Save(settings);
        var loaded = _sut.Load();
        loaded.Theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void SaveAndLoad_TokensAreNeverPersisted()
    {
        // HasStoredToken is [JsonIgnore] — confirm it isn't in the file
        var settings = new AppSettings();
        var account  = new Account { Name = "Test", HasStoredToken = true };
        settings.Accounts.Add(account);
        _sut.Save(settings);

        var json = File.ReadAllText(_sut.DataFilePath);
        json.Should().NotContain("HasStoredToken");
    }

    // ══════════════════════════════════════════════════════════
    //  Missing / corrupt file
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var loaded = _sut.Load();

        loaded.Should().NotBeNull();
        loaded.Accounts.Should().BeEmpty();
        loaded.Repositories.Should().BeEmpty();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_sut.DataFilePath, "{ this is not valid JSON !!!");

        var loaded = _sut.Load();

        loaded.Should().NotBeNull();
        loaded.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void Load_EmptyJson_ReturnsDefaults()
    {
        File.WriteAllText(_sut.DataFilePath, "{}");

        var loaded = _sut.Load();

        loaded.Should().NotBeNull();
    }

    // ══════════════════════════════════════════════════════════
    //  Multiple saves
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Save_MultipleTimes_OverwritesPrevious()
    {
        var s1 = new AppSettings();
        s1.Accounts.Add(new Account { Name = "First" });
        _sut.Save(s1);

        var s2 = new AppSettings();
        s2.Accounts.Add(new Account { Name = "Second" });
        s2.Accounts.Add(new Account { Name = "Third" });
        _sut.Save(s2);

        var loaded = _sut.Load();
        loaded.Accounts.Should().HaveCount(2);
        loaded.Accounts.Should().NotContain(a => a.Name == "First");
    }

    // ══════════════════════════════════════════════════════════
    //  Testable subclass
    // ══════════════════════════════════════════════════════════

    private sealed class TestableSettingsRepository : JsonSettingsRepository
    {
        public TestableSettingsRepository(string dir,
            Microsoft.Extensions.Logging.ILogger<JsonSettingsRepository> log)
            : base(log, dir)
        { }
    }
}
