using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.ViewModels;
using RM.AutoClicker.Tests;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class MinimalPresentationTests
{
    [Fact]
    public void ReadyShortcutsDoNotHideAnIntegrationWarning()
    {
        var hotkeys = new ClassifiedHotkeys();
        var model = CreateModel(new Controller());
        model.Attach(hotkeys);
        Assert.False(model.HasHotkeyStatusWarning);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        hotkeys.HasStatusWarning = true;
        hotkeys.Status = "An integration warning while the old shortcut remains ready.";
        model.Refresh();

        Assert.True(hotkeys.HotkeysReady);
        Assert.True(model.HasHotkeyStatusWarning);
        Assert.Equal(hotkeys.Status, model.HotkeyStatus);
        Assert.Contains(nameof(MainViewModel.HasHotkeyStatusWarning), notifications);

        hotkeys.HasStatusWarning = false;
        model.Refresh();
        Assert.False(model.HasHotkeyStatusWarning);
    }

    [Fact]
    public void UnclassifiedProvidersKeepTheirStatusVisible()
    {
        var model = CreateModel(new Controller());
        model.Attach(new UnclassifiedHotkeys());

        Assert.True(model.HasHotkeyStatusWarning);
        Assert.Equal("Unclassified provider status", model.HotkeyStatus);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es")]
    public async Task RestartNoticeAddsTheSessionWarningOnlyWhileActive(string language)
    {
        var controller = new Controller();
        var model = CreateModel(controller, language);
        model.LanguageIndex = language == "es" ? 2 : 3;
        await model.LanguageSaveTask;
        Assert.True(model.LanguageChangePending);
        var idleMessage = model.LanguageRestartMessage;
        Assert.False(string.IsNullOrWhiteSpace(idleMessage));
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        string? activeMessage = null;

        foreach (var state in new[] { SessionState.Starting, SessionState.Running, SessionState.Stopping })
        {
            controller.Snapshot = controller.Snapshot with { State = state };
            model.Refresh();

            Assert.StartsWith(idleMessage, model.LanguageRestartMessage);
            Assert.NotEqual(idleMessage, model.LanguageRestartMessage);
            activeMessage ??= model.LanguageRestartMessage;
            Assert.Equal(activeMessage, model.LanguageRestartMessage);
        }

        controller.Snapshot = controller.Snapshot with { State = SessionState.Idle };
        model.Refresh();
        Assert.Equal(idleMessage, model.LanguageRestartMessage);
        Assert.True(model.LanguageChangePending);
        Assert.Contains(nameof(MainViewModel.LanguageRestartMessage), notifications);
    }

    [Fact]
    public void SimpleKeyWarningIsConfinedToTheShortcutDraft()
    {
        var model = CreateModel(new Controller());
        model.Attach(new ClassifiedHotkeys());
        Assert.Empty(model.HotkeyEditHint);
        model.BeginHotkeyCaptureCommand.Execute(null);
        var candidate = new HotkeyGesture(0x41);

        model.CaptureHotkey(candidate);

        Assert.True(model.HasHotkeyDraft);
        Assert.Empty(model.HotkeyCaptureMessage);
        Assert.Equal(TestStrings.Portuguese.Get("SimpleKeyWarning"), model.HotkeyEditHint);
        Assert.False(model.StartCommand.CanExecute(null));

        model.CancelHotkeyCaptureCommand.Execute(null);
        Assert.Empty(model.HotkeyCaptureMessage);
        Assert.Empty(model.HotkeyEditHint);
    }

    [Fact]
    public async Task FailedSaveStatusSurvivesOtherMessagesAndInvalidEditsUntilRetrySucceeds()
    {
        var settings = new Settings("pt-BR") { FailSaving = true };
        var strings = TestStrings.Portuguese;
        var model = new MainViewModel(strings, new Controller(), settings, false, writeDiagnostic: (_, _) => { });
        model.ClicksPerSecond = 14;
        await model.SaveAsync();
        Assert.Equal(strings.Get("UnsavedFailure"), model.SaveFeedback);

        model.Message = string.Empty;
        model.ClicksPerSecond = double.NaN;
        model.Refresh();

        Assert.True(model.HasValidationError);
        Assert.False(model.HasMessage);
        Assert.Equal(strings.Get("UnsavedFailure"), model.SaveFeedback);

        settings.FailSaving = false;
        model.ClicksPerSecond = 14;
        await model.SaveAsync();
        Assert.Equal(strings.Get("Saved"), model.SaveFeedback);
    }
    private static MainViewModel CreateModel(Controller controller, string language = "pt-BR") =>
        new(TestStrings.Create(language), controller, new Settings(language), false, writeDiagnostic: (_, _) => { });

    private sealed class Settings(string language) : ISettingsStore
    {
        private AppSettings _value = new(Language: language);
        public bool FailSaving { get; set; }
        public SettingsLoadResult Load() => new(_value);
        public Task SaveAsync(AppSettings settings)
        {
            if (FailSaving) throw new IOException("Simulated save failure.");
            _value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Controller : IClickSessionController
    {
        public ExecutionSnapshot Snapshot { get; set; } = new(Guid.Empty, SessionState.Idle, 0,
            TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);
        public Task Completion => Task.CompletedTask;
        public bool Start(ClickPlan plan) => throw new InvalidOperationException("Presentation tests must never start clicking.");
        public Task StopAsync() => Task.CompletedTask;
        public bool ResetFault() => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ClassifiedHotkeys : IHotkeyService
    {
        public bool HotkeysReady => true;
        public bool EmergencyReady => true;
        public bool HasStatusWarning { get; set; }
        public string Status { get; set; } = "Healthy provider status";
        public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error) { error = null; return true; }
    }

    private sealed class UnclassifiedHotkeys : IHotkeyService
    {
        public bool HotkeysReady => true;
        public bool EmergencyReady => true;
        public string Status => "Unclassified provider status";
        public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error) { error = null; return true; }
    }
}
