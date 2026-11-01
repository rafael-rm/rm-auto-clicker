using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Platform.Windows.Persistence;

namespace RM.AutoClicker.Platform.Windows.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RMAutoClicker.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingFileUsesDefaults()
    {
        var result = new SettingsStore(_directory).Load();
        Assert.True(result.CanSave);
        Assert.Equal(HotkeyGesture.Default, result.Settings.EffectiveHotkey);
        Assert.False(result.Settings.HasInitialDelay);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task RoundTripsAnImmutablePlanAndPreferences()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings(Plan: new(ClicksPerSecond: 12, VariationPercent: 20, Button: ClickButton.Right,
            InitialDelay: TimeSpan.FromSeconds(2), MaximumClicks: 100, MaximumDuration: TimeSpan.FromSeconds(20)),
            Hotkey: new(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt), ThemeIndex: 2, HasInitialDelay: true);

        await store.SaveAsync(settings);
        var loaded = store.Load();
        Assert.Equal(settings, loaded.Settings);
        Assert.True(loaded.CanSave);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.DoesNotContain("baseInterval", await File.ReadAllTextAsync(SettingsPath()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OlderSettingsDoNotEnableTheCountdownImplicitly()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath(), """{"schemaVersion":1,"plan":{"initialDelay":"00:00:03"}}""");

        var result = new SettingsStore(_directory).Load();
        Assert.True(result.CanSave);
        Assert.False(result.Settings.HasInitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Settings.Plan?.InitialDelay);
    }

    [Theory]
    [InlineData("{ invalid")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"hotkeyIndex\":-1}")]
    [InlineData("{\"plan\":{\"clicksPerSecond\":0}}")]
    public void InvalidOrFutureSettingsArePreservedAndNeverSilentlyOverwritten(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath(), content);
        var result = new SettingsStore(_directory).Load();

        Assert.False(result.CanSave);
        Assert.NotNull(result.Warning);
        Assert.Equal(content, File.ReadAllText(SettingsPath()));
    }

    [Fact]
    public async Task ConcurrentSavesLeaveValidJsonAndNoTemporaryFiles()
    {
        var store = new SettingsStore(_directory);
        var submitted = Enumerable.Range(1, 25).Select(rate => new AppSettings(Plan: new(ClicksPerSecond: rate),
            ThemeIndex: rate % 3, Language: rate % 2 == 0 ? "es" : "en-US")).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = submitted.Select(async settings =>
        {
            await start.Task;
            await store.SaveAsync(settings);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(10));

        var concurrentResult = store.Load();
        Assert.True(concurrentResult.CanSave);
        Assert.Null(concurrentResult.Warning);
        Assert.Contains(concurrentResult.Settings, submitted);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));

        await store.SaveAsync(new(Plan: new(ClicksPerSecond: 15)));
        Assert.Equal(15, store.Load().Settings.Plan?.ClicksPerSecond);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private string SettingsPath() => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task FailedReplacementPreservesTheFileAndCleansUpBeforeARetry()
    {
        var store = new SettingsStore(_directory);
        var original = new AppSettings(Plan: new(ClicksPerSecond: 12.5), Language: "es");
        var updated = original with { Plan = new(ClicksPerSecond: 20), ThemeIndex = 2 };
        await store.SaveAsync(original);
        var originalBytes = await File.ReadAllBytesAsync(SettingsPath());

        using (var locked = new FileStream(SettingsPath(), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => store.SaveAsync(updated));
            Assert.NotNull(failure);
            Assert.True(failure is IOException or UnauthorizedAccessException, failure.ToString());
            Assert.Contains(failure.HResult & 0xFFFF, new[] { 5, 32 });
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(SettingsPath()));
            Assert.Equal(original, store.Load().Settings);
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }

        await store.SaveAsync(updated);
        var loaded = store.Load();
        Assert.True(loaded.CanSave);
        Assert.Null(loaded.Warning);
        Assert.Equal(updated, loaded.Settings);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es")]
    public async Task RoundTripsLanguageWithoutChangingOtherPreferences(string language)
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings(Plan: new(ClicksPerSecond: 12.5), Language: language);
        await store.SaveAsync(settings);
        Assert.Equal(settings, store.Load().Settings);
    }

    [Fact]
    public void ExistingVersionTwoSettingsDefaultToAutomaticLanguage()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath(), """{"schemaVersion":2,"plan":{"clicksPerSecond":12.5}}""");
        var loaded = new SettingsStore(_directory).Load();
        Assert.True(loaded.CanSave);
        Assert.Equal("auto", loaded.Settings.Language);
        Assert.Equal(12.5, loaded.Settings.Plan!.ClicksPerSecond);
    }

    [Theory]
    [InlineData("\"fr-FR\"")]
    [InlineData("null")]
    [InlineData("\"\"")]
    public void PreservesUnsupportedLanguagePreferencesWithoutOverwritingTheFile(string language)
    {
        Directory.CreateDirectory(_directory);
        var content = "{\"schemaVersion\":2,\"language\":" + language + "}";
        File.WriteAllText(SettingsPath(), content);
        var loaded = new SettingsStore(_directory).Load();
        Assert.False(loaded.CanSave);
        Assert.Equal(UserMessageCode.SettingsInvalid, loaded.Warning);
        Assert.Equal(content, File.ReadAllText(SettingsPath()));
    }

    [Theory]
    [InlineData(0, 0x75)]
    [InlineData(1, 0x76)]
    [InlineData(2, 0x77)]
    [InlineData(3, 0x78)]
    [InlineData(4, 0x79)]
    [InlineData(5, 0x7A)]
    public async Task MigratesEveryLegacyHotkeyWithoutWritingUntilSave(int index, uint key)
    {
        Directory.CreateDirectory(_directory);
        var original = $$$"""{"schemaVersion":1,"hotkeyIndex":{{{index}}},"themeIndex":2,"hasInitialDelay":true,"plan":{"clicksPerSecond":12,"initialDelay":"00:00:03"}}""";
        File.WriteAllText(SettingsPath(), original);
        var store = new SettingsStore(_directory);

        var loaded = store.Load();

        Assert.True(loaded.CanSave);
        Assert.Equal(2, loaded.Settings.SchemaVersion);
        Assert.Equal(new HotkeyGesture(key), loaded.Settings.EffectiveHotkey);
        Assert.Equal(12, loaded.Settings.Plan?.ClicksPerSecond);
        Assert.True(loaded.Settings.HasInitialDelay);
        Assert.Equal(2, loaded.Settings.ThemeIndex);
        Assert.Equal(original, File.ReadAllText(SettingsPath()));

        await store.SaveAsync(loaded.Settings);
        var updated = File.ReadAllText(SettingsPath());
        Assert.Contains("\"schemaVersion\": 2", updated);
        Assert.DoesNotContain("hotkeyIndex", updated);
        Assert.DoesNotContain("effectiveHotkey", updated);
        Assert.Equal(loaded.Settings, store.Load().Settings);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":\"2\"}")]
    [InlineData("{\"schemaVersion\":1,\"SchemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":2,\"hotkey\":{\"virtualKey\":0}}")]
    [InlineData("{\"schemaVersion\":2,\"hotkey\":{\"virtualKey\":123}}")]
    [InlineData("{\"schemaVersion\":2,\"hotkey\":{\"virtualKey\":65,\"modifiers\":8}}")]
    [InlineData("{\"schemaVersion\":2,\"hotkey\":{\"virtualKey\":121,\"modifiers\":6}}")]
    public void PreservesInvalidVersionTwoPreferences(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath(), content);

        var loaded = new SettingsStore(_directory).Load();

        Assert.False(loaded.CanSave);
        Assert.NotNull(loaded.Warning);
        Assert.Equal(content, File.ReadAllText(SettingsPath()));
    }

    [Fact]
    public async Task RejectsReservedHotkeyBeforeCreatingASettingsFile()
    {
        var store = new SettingsStore(_directory);

        await Assert.ThrowsAsync<AppValidationException>(() => store.SaveAsync(new(Hotkey: HotkeyGesture.Emergency)));

        Assert.False(File.Exists(SettingsPath()));
    }

    [Fact]
    public void LoadsCaseInsensitiveVersionTwoPropertiesConsistently()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath(), """{"SchemaVersion":2,"Hotkey":{"VirtualKey":65,"Modifiers":3}}""");

        var loaded = new SettingsStore(_directory).Load();

        Assert.True(loaded.CanSave);
        Assert.Equal(new HotkeyGesture(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt), loaded.Settings.EffectiveHotkey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
