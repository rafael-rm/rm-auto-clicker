using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.Localization;
using RM.AutoClicker.Desktop.ViewModels;
using RM.AutoClicker.Tests;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class LocalizationTests
{
    private readonly RecordingDiagnostics _diagnostics = new();

    [Theory]
    [InlineData("auto", "pt-PT", "pt-BR")]
    [InlineData("auto", "es-MX", "es")]
    [InlineData("auto", "en-GB", "en-US")]
    [InlineData("auto", "fr-FR", "en-US")]
    [InlineData("auto", "", "en-US")]
    [InlineData("es", "pt-BR", "es")]
    [InlineData("pt-BR", "en-US", "pt-BR")]
    [InlineData("en-US", "es-AR", "en-US")]
    public void ResolvesAutomaticAndExplicitLanguages(string preference, string system, string expected)
        => Assert.Equal(expected, AppStrings.ResolveLanguage(preference, system));

    [Fact]
    public void CatalogsHaveMatchingKeysAndFormatPlaceholders()
    {
        var catalogs = new[] { "pt-BR", "en-US", "es" }.Select(language => XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw"))
            .Root!.Elements("data").ToDictionary(element => (string)element.Attribute("name")!, element => element.Element("value")!.Value)).ToArray();
        foreach (var catalog in catalogs)
        {
            Assert.Equal(catalogs[0].Keys.Order(), catalog.Keys.Order());
            foreach (var (key, value) in catalog)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), key);
                Assert.Equal(CompositeFormat.Parse(catalogs[0][key]).MinimumArgumentCount, CompositeFormat.Parse(value).MinimumArgumentCount);
                Assert.Equal(Regex.Matches(catalogs[0][key], @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order(),
                    Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order());
            }
            foreach (var code in Enum.GetNames<UserMessageCode>())
                Assert.True(catalog.ContainsKey(code), code);
        }
    }

    [Fact]
    public void FallsBackToEnglishAndRejectsMissingResources()
    {
        var strings = new AppStrings("es", (language, key) => language == "en-US" && key == "Ready" ? "Ready" : null);
        Assert.Equal("Ready", strings.Get("Ready"));
        Assert.Throws<InvalidOperationException>(() => strings.Get("Unknown"));
    }

    [Fact]
    public void MarkupUsesResourceIdentifiersForAllUserFacingText()
    {
        var markup = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Strings", "en-US", "Resources.resw"))
            .Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToArray();
        foreach (var element in markup.Descendants())
        {
            if (element.Attribute(xaml + "Uid") is { } uid)
                Assert.Contains(resources, key => key.StartsWith(uid.Value + ".", StringComparison.Ordinal));
            foreach (var attribute in element.Attributes().Where(attribute => attribute.Name.LocalName is "Text" or "Content" or "Header"
                or "Message" or "PlaceholderText" or "AutomationProperties.Name" or "AutomationProperties.HelpText" or "ToolTipService.ToolTip"))
                Assert.True(attribute.Value.StartsWith('{') || attribute.Value == "RM Auto Clicker", attribute.ToString());
        }
    }

    [Fact]
    public void RestartNoticeAndActionStayInsidePreferences()
    {
        var markup = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var preferences = markup.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "PreferencesButton");
        var notice = preferences.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "LanguageRestartNotice");
        Assert.Equal("InfoBar", notice.Name.LocalName);
        Assert.Contains("LanguageChangePending", (string?)notice.Attribute("IsOpen"));
        Assert.Contains("LanguageRestartMessage", (string?)notice.Attribute("Message"));
        var action = notice.Descendants().Single(element => (string?)element.Attribute(xaml + "Uid") == "RestartApp");
        Assert.Contains("RestartCommand", (string?)action.Attribute("Command"));
    }

    [Theory]
    [InlineData("pt-BR", "De 1 a 25", "Reiniciar agora")]
    [InlineData("en-US", "From 1 to 25", "Restart now")]
    [InlineData("es", "De 1 a 25", "Reiniciar ahora")]
    public void LocalizesRestartActionAndKeepsOnlyTheSpeedRange(string language, string range, string restart)
    {
        var strings = TestStrings.Create(language);
        Assert.Equal(range, strings.Get("SpeedHint.Text"));
        Assert.Equal(restart, strings.Get("RestartApp.Content"));
        Assert.False(string.IsNullOrWhiteSpace(strings.Get("LanguagePending")));
        Assert.False(string.IsNullOrWhiteSpace(strings.Get("LanguagePendingActive")));
    }

    [Theory]
    [InlineData("pt-BR", "Pronto", "Iniciando em 3 s…")]
    [InlineData("en-US", "Ready", "Starting in 3 s…")]
    [InlineData("es", "Listo", "Iniciando en 3 s…")]
    public void LocalizesDynamicStatesValidationAndReservedHotkeys(string language, string ready, string starting)
    {
        var controller = new Controller();
        var strings = TestStrings.Create(language);
        var model = new MainViewModel(strings, controller, new Settings(), false, writeDiagnostic: _diagnostics.Write);
        model.Attach(new Hotkeys());
        Assert.Equal(ready, model.StatusText);
        controller.Snapshot = controller.Snapshot with { State = SessionState.Starting, StartDelayRemaining = TimeSpan.FromSeconds(3) };
        model.Refresh();
        Assert.Equal(starting, model.StatusText);
        controller.Snapshot = controller.Snapshot with { State = SessionState.Idle };
        model.Refresh();
        model.ClicksPerSecond = double.NaN;
        Assert.Equal(strings.Get("SpeedError"), model.SpeedError);
        model.ClicksPerSecond = 12;
        model.BeginHotkeyCaptureCommand.Execute(null);
        model.CaptureHotkey(HotkeyGesture.Emergency);
        Assert.Equal(strings.Get("HotkeyEmergencyReserved"), model.HotkeyCaptureMessage);
        controller.Snapshot = controller.Snapshot with { State = SessionState.Faulted,
            Failure = new("English diagnostic only", new(2, 1, 5), UserMessageCode.SessionInput) };
        model.Refresh();
        Assert.Equal(strings.Get("SessionInput", 1, 2, 5), model.Message);
        Assert.DoesNotContain("English diagnostic", model.Message);
    }

    [Theory]
    [InlineData("pt-BR", "en-US", "12.5")]
    [InlineData("en-US", "pt-BR", "12,5")]
    [InlineData("es", "en-US", "12.5")]
    public void KeepsRegionalNumberFormattingIndependentFromDisplayLanguage(string language, string region, string formatted)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(region);
            Assert.Contains(formatted, TestStrings.Create(language).Get("Plan", 12.5, "Mouse", "Limits"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public async Task SavesOnlyTheLanguageWhenOtherFieldsAreInvalidAndAppliesItAfterReopening()
    {
        var settings = new Settings();
        var model = new MainViewModel(TestStrings.Portuguese, new Controller(), settings, false, writeDiagnostic: _diagnostics.Write);
        model.ClicksPerSecond = double.NaN;
        model.LanguageIndex = 3;
        await model.LanguageSaveTask;
        Assert.Equal("es", settings.Value.Language);
        Assert.Equal(12, settings.Value.Plan!.ClicksPerSecond);
        Assert.True(model.LanguageChangePending);
        Assert.Equal("pt-BR", TestStrings.Portuguese.Language);
        var reopened = new MainViewModel(TestStrings.Create(settings.Value.Language), new Controller(), settings, false, writeDiagnostic: _diagnostics.Write);
        reopened.Attach(new Hotkeys());
        Assert.False(reopened.LanguageChangePending);
        Assert.Equal("Listo", reopened.StatusText);
        Assert.Equal(3, reopened.LanguageIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveWaitsForTheLanguageSaveEvenWhenThePlanIsInvalid(bool failSaving)
    {
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new Settings { SaveCompletion = saved.Task, FailSaving = failSaving };
        var controller = new Controller();
        var model = new MainViewModel(TestStrings.Portuguese, controller, settings, false, writeDiagnostic: _diagnostics.Write);
        model.ClicksPerSecond = double.NaN;
        model.LanguageIndex = 3;

        var saving = model.SaveAsync();
        try
        {
            Assert.False(model.LanguageSaveTask.IsCompleted);
            Assert.False(saving.IsCompleted);
            Assert.Equal("auto", settings.Value.Language);
        }
        finally
        {
            saved.TrySetResult();
            await Task.WhenAll(saving, model.LanguageSaveTask).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(failSaving ? "auto" : "es", settings.Value.Language);
        Assert.Equal(12, settings.Value.Plan!.ClicksPerSecond);
        if (failSaving)
            Assert.Equal(TestStrings.Portuguese.Get("SaveFailed"), model.Message);
    }

    [Fact]
    public async Task DoesNotWriteProtectedPreferencesAndReportsSaveFailuresInTheCurrentLanguage()
    {
        var protectedSettings = new Settings { CanSave = false };
        var protectedModel = new MainViewModel(TestStrings.Create("en-US"), new Controller(), protectedSettings, false, writeDiagnostic: _diagnostics.Write);
        Assert.False(protectedModel.CanChangeLanguage);
        protectedModel.LanguageIndex = 3;
        await protectedModel.LanguageSaveTask;
        Assert.Equal("auto", protectedSettings.Value.Language);
        Assert.False(protectedModel.LanguageChangePending);
        Assert.False(protectedModel.RestartCommand.CanExecute(null));
        var settings = new Settings { FailSaving = true };
        var model = new MainViewModel(TestStrings.Create("es"), new Controller(), settings, false, writeDiagnostic: _diagnostics.Write);
        model.LanguageIndex = 2;
        await model.LanguageSaveTask;
        Assert.Equal(TestStrings.Create("es").Get("SaveFailed"), model.Message);
        Assert.Equal("auto", settings.Value.Language);
        Assert.False(model.LanguageChangePending);
        Assert.False(model.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task OffersRestartOnlyAfterSavingAndHidesItWhenReturningToTheOriginalPreference()
    {
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new Settings { SaveCompletion = saved.Task };
        var model = new MainViewModel(TestStrings.Portuguese, new Controller(), settings, false, writeDiagnostic: _diagnostics.Write);
        Assert.False(model.LanguageChangePending);
        model.LanguageIndex = 2;
        Assert.False(model.LanguageChangePending);
        Assert.False(model.RestartCommand.CanExecute(null));
        saved.SetResult();
        await model.LanguageSaveTask;
        Assert.True(model.LanguageChangePending);
        Assert.True(model.RestartCommand.CanExecute(null));
        model.LanguageIndex = 0;
        await model.LanguageSaveTask;
        Assert.False(model.LanguageChangePending);
        Assert.False(model.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestartSavesPreferencesStopsTheSessionAndBlocksDuplicateRequests()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new Controller { StopCompletion = stopped.Task };
        var settings = new Settings();
        var model = new MainViewModel(TestStrings.Portuguese, controller, settings, false, writeDiagnostic: _diagnostics.Write);
        model.Attach(new Hotkeys());
        var restarts = 0;
        model.RestartRequested += () =>
        {
            Assert.Equal(1, controller.StopCalls);
            Assert.Equal("en-US", settings.Value.Language);
            Assert.Equal(13.5, settings.Value.Plan!.ClicksPerSecond);
            restarts++;
        };
        await model.RestartCommand.ExecuteAsync(null);
        Assert.Equal(0, restarts);
        model.LanguageIndex = 2;
        await model.LanguageSaveTask;
        Assert.Equal(0, restarts);
        model.ClicksPerSecond = 13.5;
        var restart = model.RestartCommand.ExecuteAsync(null);
        Assert.False(model.CanChangeLanguage);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.CanEditHotkey);
        Assert.False(model.RestartCommand.CanExecute(null));
        await model.RestartCommand.ExecuteAsync(null);
        stopped.SetResult();
        await restart;
        Assert.Equal(1, restarts);
        Assert.True(model.CanChangeLanguage);
        Assert.Empty(model.RestartError);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es")]
    public async Task FailedRestartReportsInsidePreferencesAndAllowsRetry(string language)
    {
        var strings = TestStrings.Create(language);
        var settings = new Settings();
        var model = new MainViewModel(strings, new Controller(), settings, false, writeDiagnostic: _diagnostics.Write);
        model.LanguageIndex = 3;
        await model.LanguageSaveTask;
        var requests = 0;
        model.RestartRequested += () =>
        {
            if (++requests == 1)
                throw new InvalidOperationException("Simulated native restart failure.");
        };
        await model.RestartCommand.ExecuteAsync(null);
        Assert.Equal(strings.Get("RestartFailed"), model.RestartError);
        Assert.Equal("es", settings.Value.Language);
        Assert.True(model.LanguageChangePending);
        Assert.True(model.RestartCommand.CanExecute(null));
        await model.RestartCommand.ExecuteAsync(null);
        Assert.Equal(2, requests);
        Assert.Empty(model.RestartError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DoesNotRestartWhenStoppingOrSavingFails(bool failStopping)
    {
        var settings = new Settings();
        var controller = new Controller();
        var model = new MainViewModel(TestStrings.Portuguese, controller, settings, false, writeDiagnostic: _diagnostics.Write);
        model.LanguageIndex = 2;
        await model.LanguageSaveTask;
        settings.FailSaving = !failStopping;
        controller.StopCompletion = failStopping ? Task.FromException(new IOException("Simulated stop failure.")) : Task.CompletedTask;
        var restarted = false;
        model.RestartRequested += () => restarted = true;
        await model.RestartCommand.ExecuteAsync(null);
        Assert.False(restarted);
        Assert.Equal(TestStrings.Portuguese.Get("RestartFailed"), model.RestartError);
        Assert.True(model.CanChangeLanguage);
    }

    [Fact]
    public async Task ClosingDuringRestartPreventsRelaunch()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new Controller { StopCompletion = stopped.Task };
        var model = new MainViewModel(TestStrings.Portuguese, controller, new Settings(), false, writeDiagnostic: _diagnostics.Write);
        model.LanguageIndex = 2;
        await model.LanguageSaveTask;
        var restarted = false;
        model.RestartRequested += () => restarted = true;
        var restart = model.RestartCommand.ExecuteAsync(null);
        model.BeginShutdown();
        stopped.SetResult();
        await restart;
        Assert.False(restarted);
        Assert.False(model.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestartKeepsTheLastValidPlanWhenAnEditorContainsAnInvalidDraft()
    {
        var settings = new Settings();
        var model = new MainViewModel(TestStrings.Portuguese, new Controller(), settings, false, writeDiagnostic: _diagnostics.Write);
        model.ClicksPerSecond = double.NaN;
        model.LanguageIndex = 2;
        await model.LanguageSaveTask;
        var restarted = false;
        model.RestartRequested += () => restarted = true;
        await model.RestartCommand.ExecuteAsync(null);
        Assert.True(restarted);
        Assert.Equal("en-US", settings.Value.Language);
        Assert.Equal(12, settings.Value.Plan!.ClicksPerSecond);
    }

    [Fact]
    public async Task DoesNotRestartIfPendingInputCannotBeReleased()
    {
        var controller = new Controller
        {
            Snapshot = new(Guid.Empty, SessionState.Faulted, 0, TimeSpan.Zero, TimeSpan.Zero, CompletionReason.Failed,
                new("Simulated release failure.", Code: UserMessageCode.SessionRelease))
        };
        var model = new MainViewModel(TestStrings.Portuguese, controller, new Settings(), false, writeDiagnostic: _diagnostics.Write);
        model.LanguageIndex = 2;
        await model.LanguageSaveTask;
        var restarted = false;
        model.RestartRequested += () => restarted = true;
        await model.RestartCommand.ExecuteAsync(null);
        Assert.False(restarted);
        Assert.Equal(TestStrings.Portuguese.Get("RestartFailed"), model.RestartError);
    }

    private sealed class Settings : ISettingsStore
    {
        public AppSettings Value { get; private set; } = new(Plan: new(ClicksPerSecond: 12));
        public bool CanSave { get; init; } = true;
        public bool FailSaving { get; set; }
        public Task SaveCompletion { get; init; } = Task.CompletedTask;
        public SettingsLoadResult Load() => new(Value, CanSave: CanSave);
        public async Task SaveAsync(AppSettings settings)
        {
            await SaveCompletion;
            if (FailSaving) throw new IOException("Simulated save failure.");
            Value = settings;
        }
    }

    private sealed class Controller : IClickSessionController
    {
        public ExecutionSnapshot Snapshot { get; set; } = new(Guid.Empty, SessionState.Idle, 0, TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);
        public Task Completion => Task.CompletedTask;
        public Task StopCompletion { get; set; } = Task.CompletedTask;
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool Start(ClickPlan plan) => throw new InvalidOperationException("This test must not start a session.");
        public Task StopAsync() { StopCalls++; return StopCompletion; }
        public bool ResetFault() => false;
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class Hotkeys : IHotkeyService
    {
        public bool HotkeysReady => true;
        public bool EmergencyReady => true;
        public string Status => "Ready";
        public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error) { error = null; return true; }
    }
}
