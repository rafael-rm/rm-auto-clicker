using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Platform.Windows.Persistence;

namespace RM.AutoClicker.Platform.Windows.Tests;

public sealed class MinimizeToTraySettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RMAutoClicker.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ExistingSettingsKeepNormalMinimization(int schemaVersion)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"),
            "{\"schemaVersion\":" + schemaVersion + ",\"plan\":{\"clicksPerSecond\":12}}");

        var loaded = new SettingsStore(_directory).Load();

        Assert.True(loaded.CanSave);
        Assert.Null(loaded.Warning);
        Assert.False(loaded.Settings.MinimizeToTray);
        Assert.Equal(12, loaded.Settings.Plan!.ClicksPerSecond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTripsTheBehaviorWithTheOtherPreferences(bool minimizeToTray)
    {
        var settings = new AppSettings(Plan: new(ClicksPerSecond: 12.5), ThemeIndex: 2,
            Language: "es", Hotkey: new(0x41, HotkeyModifiers.Control), MinimizeToTray: minimizeToTray);
        var store = new SettingsStore(_directory);

        await store.SaveAsync(settings);

        Assert.Equal(settings, store.Load().Settings);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public void InvalidBehaviorSettingsArePreservedWithoutOverwrite(string value)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        var content = "{\"schemaVersion\":2,\"minimizeToTray\":" + value + "}";
        File.WriteAllText(path, content);

        var loaded = new SettingsStore(_directory).Load();

        Assert.False(loaded.CanSave);
        Assert.Equal(UserMessageCode.SettingsInvalid, loaded.Warning);
        Assert.Equal(content, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
