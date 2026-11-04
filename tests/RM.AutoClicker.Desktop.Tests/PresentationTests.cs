using System.Xml.Linq;
using System.Globalization;
using RM.AutoClicker.Tests;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.ViewModels;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class PresentationTests
{
    private readonly RecordingDiagnostics _diagnostics = new();

    [Fact]
    public void UsesDismissibleControlsForEveryCompactNumericEditor()
    {
        var markup = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var inputs = markup.Descendants().Where(element => (string?)element.Attribute("SpinButtonPlacementMode") == "Compact").ToArray();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Equal(new[] { "ClickLimitInput", "DelayInput", "DurationInput", "SpeedInput", "VariationInput" },
            inputs.Select(input => (string?)input.Attribute(xaml + "Name")).Order().ToArray());
        Assert.All(inputs, input =>
        {
            Assert.Equal("AutoDismissNumberBox", input.Name.LocalName);
            Assert.Equal("using:RM.AutoClicker.Desktop.Controls", input.Name.NamespaceName);
        });
    }

    [Fact]
    public void CleanInitialSettingsDoNotProduceNeutralMessages()
    {
        var viewModel = Create();
        Assert.Empty(viewModel.SaveFeedback);
        Assert.Empty(viewModel.RateText);
        Assert.False(viewModel.HasSessionDetails);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.999, false)]
    [InlineData(1, true)]
    public void RateAppearsOnlyWhenItCanBeCalculated(double elapsedSeconds, bool visible)
    {
        var controller = new Controller();
        controller.Snapshot = controller.Snapshot with { Elapsed = TimeSpan.FromSeconds(elapsedSeconds) };
        var viewModel = Create(controller: controller);
        Assert.Equal(visible, !string.IsNullOrEmpty(viewModel.RateText));
        Assert.Equal(visible, viewModel.HasSessionDetails);
        if (visible) Assert.Contains("Taxa média enviada", viewModel.RateText);
    }

    [Fact]
    public async Task SaveConfirmationExpiresAfterThreeSeconds()
    {
        var clock = new Clock();
        var viewModel = Create(timeProvider: clock);
        await viewModel.SaveAsync();
        Assert.Equal("Salvo", viewModel.SaveFeedback);
        clock.Advance(TimeSpan.FromMilliseconds(2999));
        viewModel.Refresh();
        Assert.Equal("Salvo", viewModel.SaveFeedback);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        viewModel.Refresh();
        Assert.Empty(viewModel.SaveFeedback);
    }

    [Fact]
    public async Task SaveFailuresAndUnsavedChangesRemainUntilResolved()
    {
        var clock = new Clock();
        var settings = new Settings { FailSaving = true };
        var viewModel = Create(settings, timeProvider: clock);
        viewModel.ClicksPerSecond = 14;
        await viewModel.SaveAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        viewModel.Refresh();
        Assert.Equal("Não salvo", viewModel.SaveFeedback);
        Assert.True(viewModel.HasMessage);
        settings.FailSaving = false;
        await viewModel.SaveAsync();
        Assert.False(viewModel.HasMessage);
        viewModel.ClicksPerSecond = 15;
        clock.Advance(TimeSpan.FromMinutes(1));
        viewModel.Refresh();
        Assert.Equal("Alterações não salvas", viewModel.SaveFeedback);
    }

    [Fact]
    public async Task EditingDoesNotRestoreAnEarlierSaveConfirmation()
    {
        var viewModel = Create();
        await viewModel.SaveAsync();
        viewModel.ClicksPerSecond = 11;
        viewModel.ClicksPerSecond = 10;
        Assert.Empty(viewModel.SaveFeedback);
    }

    [Fact]
    public void OperationHintReflectsTheActualEmergencyRegistration()
    {
        var viewModel = new MainViewModel(TestStrings.Portuguese, new Controller(), new Settings(), false, writeDiagnostic: _diagnostics.Write);
        Assert.Contains("Emergência indisponível", viewModel.HotkeyOperationText);
        var hotkeys = new Hotkeys();
        viewModel.Attach(hotkeys);
        Assert.Contains("Emergência: Ctrl+Shift+F10", viewModel.HotkeyOperationText);
        Assert.DoesNotContain("inicia/para", viewModel.HotkeyOperationText);
        hotkeys.EmergencyReady = false;
        viewModel.Refresh();
        Assert.Contains("Emergência indisponível", viewModel.HotkeyOperationText);
        Assert.DoesNotContain("Ctrl+Shift+F10", viewModel.HotkeyOperationText);
    }

    [Fact]
    public void StartEmphasisFollowsAvailabilityInsteadOfOnlySessionState()
    {
        var viewModel = Create();
        Assert.True(viewModel.CanEmphasizeStart);
        viewModel.ClicksPerSecond = double.NaN;
        Assert.False(viewModel.CanEmphasizeStart);
        Assert.True(viewModel.HasStartBlocker);
        Assert.False(viewModel.ShowStartBlocker);
        viewModel.ClicksPerSecond = 10;
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        Assert.False(viewModel.CanEmphasizeStart);
        Assert.True(viewModel.ShowStartBlocker);
        viewModel.CancelHotkeyCapture();
        Assert.True(viewModel.CanEmphasizeStart);
    }

    [Fact]
    public void StartEmphasisIsDisabledWithoutIntegrationAndDuringShutdown()
    {
        var viewModel = new MainViewModel(TestStrings.Portuguese, new Controller(), new Settings(), false, writeDiagnostic: _diagnostics.Write);
        Assert.False(viewModel.CanEmphasizeStart);
        viewModel.Attach(new Hotkeys());
        Assert.True(viewModel.CanEmphasizeStart);
        viewModel.BeginShutdown();
        Assert.False(viewModel.CanEmphasizeStart);
    }

    [Fact]
    public void ReadyStatusRequiresValidSettingsAndIntegration()
    {
        var viewModel = new MainViewModel(TestStrings.Portuguese, new Controller(), new Settings(), false, writeDiagnostic: _diagnostics.Write);
        Assert.Equal("Parado", viewModel.StatusText);
        Assert.NotEmpty(viewModel.StartBlocker);
        viewModel.Attach(new Hotkeys());
        Assert.Equal("Pronto", viewModel.StatusText);
        Assert.Empty(viewModel.StartBlocker);
        viewModel.ClicksPerSecond = double.NaN;
        Assert.Equal("Parado", viewModel.StatusText);
        Assert.NotEmpty(viewModel.SpeedError);
    }

    [Fact]
    public async Task PendingBannerComparesTheFormWithTheActivePlanNotWithSavedPreferences()
    {
        var viewModel = Create();
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert.False(viewModel.ShowPendingChanges);
        viewModel.ThemeIndex = 2;
        Assert.False(viewModel.ShowPendingChanges);
        viewModel.ClicksPerSecond = 15;
        Assert.True(viewModel.ShowPendingChanges);
        Assert.Contains("15", viewModel.PendingChangesText);
        Assert.Contains("10", viewModel.ActivePlanText);
        await viewModel.SaveAsync();
        Assert.True(viewModel.ShowPendingChanges);
        viewModel.ClicksPerSecond = 10;
        Assert.False(viewModel.ShowPendingChanges);
    }

    [Fact]
    public async Task InvalidDraftCannotHideOrDisableStoppingTheCurrentSession()
    {
        var viewModel = Create();
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.ClicksPerSecond = double.NaN;
        Assert.False(viewModel.ShowPendingChanges);
        Assert.True(viewModel.IsSessionActive);
        Assert.False(viewModel.CanEmphasizeStart);
        Assert.True(viewModel.HasValidationError);
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.Contains("10", viewModel.ActivePlanText);
        await viewModel.StopAsync();
        Assert.Contains("Última sessão", viewModel.ActivePlanText);
    }

    [Theory]
    [InlineData("speed", nameof(MainViewModel.SpeedError), "SpeedError")]
    [InlineData("mouse", nameof(MainViewModel.MouseError), "MouseError")]
    [InlineData("delay", nameof(MainViewModel.DelayError), "DelayError")]
    [InlineData("clicks", nameof(MainViewModel.ClickLimitError), "ClickLimitError")]
    [InlineData("duration", nameof(MainViewModel.DurationLimitError), "DurationError")]
    [InlineData("variation", nameof(MainViewModel.VariationError), "VariationError")]
    public void ValidationHasFieldSpecificMessagesWithoutInternalParameterNames(string field, string property, string resource)
    {
        var viewModel = Create();
        switch (field)
        {
            case "speed": viewModel.ClicksPerSecond = double.NaN; break;
            case "mouse": viewModel.ButtonIndex = -1; break;
            case "delay": viewModel.HasInitialDelay = true; viewModel.InitialDelaySeconds = 0; break;
            case "clicks": viewModel.HasClickLimit = true; viewModel.MaximumClicks = 1.5; break;
            case "duration": viewModel.HasDurationLimit = true; viewModel.MaximumDurationSeconds = double.NaN; break;
            case "variation": viewModel.VariationPercent = double.PositiveInfinity; break;
        }
        Assert.True(viewModel.HasValidationError);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        var expected = TestStrings.Portuguese.Get(resource, 0.1);
        Assert.Equal(expected, viewModel.ValidationMessage);
        var errors = new Dictionary<string, string>
        {
            [nameof(MainViewModel.SpeedError)] = viewModel.SpeedError,
            [nameof(MainViewModel.MouseError)] = viewModel.MouseError,
            [nameof(MainViewModel.DelayError)] = viewModel.DelayError,
            [nameof(MainViewModel.ClickLimitError)] = viewModel.ClickLimitError,
            [nameof(MainViewModel.DurationLimitError)] = viewModel.DurationLimitError,
            [nameof(MainViewModel.VariationError)] = viewModel.VariationError
        };
        Assert.Equal(expected, errors[property]);
        Assert.All(errors.Where(error => error.Key != property), error => Assert.Empty(error.Value));
        Assert.DoesNotContain("Parameter", viewModel.ValidationMessage);
        Assert.Equal(field is not ("speed" or "mouse"), viewModel.HasAdvancedErrors);

        viewModel.ClicksPerSecond = 10;
        viewModel.ButtonIndex = 0;
        viewModel.InitialDelaySeconds = 3;
        viewModel.MaximumClicks = 100;
        viewModel.MaximumDurationSeconds = 30;
        viewModel.VariationPercent = 0;
        Assert.Empty(viewModel.ValidationMessage);
        Assert.False(viewModel.HasValidationError);
        Assert.False(viewModel.HasAdvancedErrors);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public void AdvancedSummaryOnlyIncludesEnabledAndValidOptions()
    {
        var viewModel = Create();
        Assert.Equal("Sem limites · Sem atraso", viewModel.AdvancedSummary);
        viewModel.HasInitialDelay = true;
        viewModel.HasClickLimit = true;
        viewModel.HasDurationLimit = true;
        viewModel.VariationPercent = 20;
        Assert.Contains("Atraso 3", viewModel.AdvancedSummary);
        Assert.Contains("100 cliques", viewModel.AdvancedSummary);
        Assert.Contains("30 s", viewModel.AdvancedSummary);
        Assert.Contains("20%", viewModel.AdvancedSummary);
        viewModel.InitialDelaySeconds = double.NaN;
        Assert.Equal("Revisar opções", viewModel.AdvancedSummary);
        viewModel.HasInitialDelay = false;
        Assert.DoesNotContain("Atraso", viewModel.AdvancedSummary);
    }

    [Fact]
    public void CaptureKeepsTheAppliedKeyAccessibleAndExplainsWhyStartingIsBlocked()
    {
        var viewModel = Create();
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        Assert.Contains("Gravando", viewModel.HotkeyAccessibleName);
        Assert.Contains("cancele", viewModel.StartBlocker);
        viewModel.CaptureHotkey(new(0x41, HotkeyModifiers.Control));
        Assert.Equal("F8", viewModel.AppliedHotkeyText);
        Assert.Equal("Ctrl+A", viewModel.HotkeyCaptureText);
        Assert.Contains("F8", viewModel.HotkeyAccessibleName);
        viewModel.CancelHotkeyCapture();
        Assert.Equal("Pronto", viewModel.StatusText);
        Assert.Empty(viewModel.HotkeyCaptureMessage);
    }

    [Fact]
    public async Task SaveFeedbackTracksChangesAndClearsOnlyAResolvedSaveWarning()
    {
        var settings = new Settings { FailSaving = true };
        var viewModel = Create(settings);
        viewModel.ClicksPerSecond = 14;
        Assert.Contains("não salvas", viewModel.SaveFeedback);
        await viewModel.SaveAsync();
        Assert.True(viewModel.HasMessage);
        Assert.False(viewModel.IsMessageError);
        settings.FailSaving = false;
        await viewModel.SaveAsync();
        Assert.False(viewModel.HasMessage);
        Assert.Equal("Salvo", viewModel.SaveFeedback);
        viewModel.ReportIntegrationFailure();
        await viewModel.SaveAsync();
        Assert.True(viewModel.IsMessageError);
        Assert.True(viewModel.HasMessage);
    }

    [Fact]
    public async Task ProtectedSettingsNeverClaimThatChangesWereSaved()
    {
        var settings = new Settings { CanSave = false };
        var viewModel = Create(settings);
        viewModel.ClicksPerSecond = 14;
        await viewModel.SaveAsync();
        Assert.Contains("protegidas", viewModel.SaveFeedback);
        Assert.Equal(0, settings.Saves);
    }

    [Fact]
    public void UnchangedStatusIsNotNotifiedOnEveryRefresh()
    {
        var viewModel = Create();
        var notifications = 0;
        viewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(MainViewModel.StatusText)) notifications++; };
        viewModel.Refresh();
        viewModel.Refresh();
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task OperationFailureRemainsVisibleAfterRefreshing()
    {
        var viewModel = Create(controller: new Controller { FailStarting = true });
        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.Refresh();
        Assert.True(viewModel.IsMessageError);
        Assert.Contains("Não foi possível iniciar", viewModel.Message);
        Assert.Empty(viewModel.ActivePlanText);
        var diagnostic = Assert.Single(_diagnostics.Entries);
        Assert.Equal("StartFailed", diagnostic.Event);
        Assert.IsType<InvalidOperationException>(diagnostic.Error);
    }

    [Theory]
    [InlineData("pt-BR", 10, "5,0")]
    [InlineData("en-US", 10, "5.0")]
    [InlineData("pt-BR", 9, "4,5")]
    public async Task SuppressionAndRateDescribeTheSessionWithoutPretendingThatTimeIsPaused(string culture, long clicks, string rate)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var controller = new Controller();
            var viewModel = Create(controller: controller);
            await viewModel.StartCommand.ExecuteAsync(null);
            controller.Snapshot = controller.Snapshot with { Elapsed = TimeSpan.FromSeconds(2), SentClicks = clicks, IsInputSuppressed = true };
            viewModel.Refresh();
            Assert.True(viewModel.IsInputSuppressed);
            Assert.Contains("segurança", viewModel.StatusText);
            Assert.Equal($"Taxa média enviada: {rate} cliques/s", viewModel.RateText);
            Assert.Equal("00:00:02", viewModel.ElapsedText);
            Assert.True(viewModel.HasSessionDetails);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private MainViewModel Create(Settings? settings = null, Controller? controller = null, TimeProvider? timeProvider = null)
    {
        var viewModel = new MainViewModel(TestStrings.Portuguese, controller ?? new Controller(), settings ?? new Settings(), false, timeProvider, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(new Hotkeys());
        return viewModel;
    }

    private sealed class Settings : ISettingsStore
    {
        public bool CanSave { get; init; } = true;
        public bool FailSaving { get; set; }
        public int Saves { get; private set; }
        public SettingsLoadResult Load() => new(new(), CanSave: CanSave);
        public Task SaveAsync(AppSettings settings)
        {
            if (FailSaving) throw new IOException("Falha simulada.");
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class Controller : IClickSessionController
    {
        public ExecutionSnapshot Snapshot { get; set; } = new(Guid.NewGuid(), SessionState.Idle, 0, TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);
        public bool FailStarting { get; init; }
        public Task Completion => Task.CompletedTask;
        public bool Start(ClickPlan plan)
        {
            if (FailStarting) throw new InvalidOperationException("Falha simulada.");
            Snapshot = Snapshot with { State = SessionState.Running };
            return true;
        }
        public Task StopAsync() { Snapshot = Snapshot with { State = SessionState.Idle }; return Task.CompletedTask; }
        public bool ResetFault() => true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Hotkeys : IHotkeyService
    {
        public bool HotkeysReady => true;
        public bool EmergencyReady { get; set; } = true;
        public string Status => "Atalho disponível";
        public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error) { error = null; return true; }
    }

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
