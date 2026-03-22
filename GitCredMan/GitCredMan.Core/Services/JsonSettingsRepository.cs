using System.Text.Json;
using GitCredMan.Core.Interfaces;
using GitCredMan.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitCredMan.Core.Services;

// Not sealed — allows test subclass to override the data directory
public class JsonSettingsRepository : ISettingsRepository
{
    private readonly ILogger<JsonSettingsRepository> _log;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented              = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition     = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string DataFilePath { get; }

    public JsonSettingsRepository(ILogger<JsonSettingsRepository> log)
        : this(log, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GitCredMan"))
    { }

    /// <summary>Test-only constructor — pass a custom directory.</summary>
    protected internal JsonSettingsRepository(ILogger<JsonSettingsRepository> log, string directory)
    {
        _log = log;
        Directory.CreateDirectory(directory);
        DataFilePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(DataFilePath))
            {
                _log.LogInformation("No settings file found at {path}; using defaults.", DataFilePath);
                return new AppSettings();
            }
            var json     = File.ReadAllText(DataFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _opts);
            _log.LogInformation("Loaded settings: {accounts} accounts, {repos} repos.",
                settings?.Accounts.Count ?? 0, settings?.Repositories.Count ?? 0);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load settings; returning defaults.");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            settings.Version = "1.0.0";
            var json = JsonSerializer.Serialize(settings, _opts);
            File.WriteAllText(DataFilePath, json);
            _log.LogDebug("Settings saved to {path}.", DataFilePath);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save settings.");
        }
    }
}
