using System.Text.Json;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Platform.Windows.Persistence;

public sealed class SettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = true, IgnoreReadOnlyProperties = true };
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RM Corporation", "RM Auto Clicker");
        _path = Path.Combine(directory, "settings.json");
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
            return new(new());

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Invalid preferences.");
            var version = 1;
            var schemas = root.EnumerateObject().Where(property => string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (schemas.Length > 1 || (schemas.Length == 1
                && (schemas[0].Value.ValueKind != JsonValueKind.Number || !schemas[0].Value.TryGetInt32(out version))))
                throw new JsonException("Invalid schema version.");
            if (version is not (1 or 2))
                return new(new(), UserMessageCode.SettingsIncompatible, false);

            AppSettings settings;
            if (version == 1)
            {
                var legacy = root.Deserialize<LegacySettings>(_options) ?? throw new JsonException("Empty settings file.");
                if (legacy.HotkeyIndex is < 0 or > 5)
                    throw new JsonException("Invalid legacy shortcut.");
                settings = new(Plan: legacy.Plan, Hotkey: new((uint)(0x75 + legacy.HotkeyIndex)),
                    ThemeIndex: legacy.ThemeIndex, HasInitialDelay: legacy.HasInitialDelay);
            }
            else
            {
                settings = root.Deserialize<AppSettings>(_options) ?? throw new JsonException("Empty settings file.");
            }
            settings.Validate();

            return new(settings);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new(new(), UserMessageCode.SettingsInvalid, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(new(), UserMessageCode.SettingsUnreadable, false);
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        settings.Validate();
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(settings, _options)).ConfigureAwait(false);
                File.Move(temporaryPath, _path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private sealed record LegacySettings(ClickPlan? Plan = null, int HotkeyIndex = 2, int ThemeIndex = 0,
        bool HasInitialDelay = false);
}
