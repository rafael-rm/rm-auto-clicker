using RM.AutoClicker.Tests;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.ViewModels;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class MainViewModelTests
{
    private readonly RecordingDiagnostics _diagnostics = new();

    [Fact]
    public async Task EmptySpeedPreservesSliderAndPreventsStartingOrSaving()
    {
        var settings = new MemorySettings();
        var viewModel = CreateViewModel(settings);
        Assert.True(viewModel.StartCommand.CanExecute(null));

        viewModel.ClicksPerSecond = double.NaN;

        Assert.True(double.IsNaN(viewModel.ClicksPerSecond));
        Assert.Equal(23, viewModel.SliderClicksPerSecond);
        Assert.Equal("Intervalo inválido", viewModel.IntervalText);
        Assert.NotEmpty(viewModel.ValidationMessage);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        await viewModel.SaveAsync();
        Assert.Equal(0, settings.Saves);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidNumericInputNeverReachesSlider(double value)
    {
        var viewModel = CreateViewModel();
        var sliderNotifications = new List<double>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SliderClicksPerSecond))
                sliderNotifications.Add(viewModel.SliderClicksPerSecond);
        };

        viewModel.ClicksPerSecond = value;

        Assert.Equal(23, viewModel.SliderClicksPerSecond);
        Assert.Empty(sliderNotifications);
        Assert.False(viewModel.StartCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(101)]
    public void SliderRejectsInvalidAssignments(double value)
    {
        var viewModel = CreateViewModel();

        viewModel.SliderClicksPerSecond = value;

        Assert.Equal(23, viewModel.ClicksPerSecond);
        Assert.Equal(23, viewModel.SliderClicksPerSecond);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12.5)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(100)]
    public void ValidInputUpdatesSliderAndRecoversFromEmptyValue(double value)
    {
        var viewModel = CreateViewModel();
        viewModel.ClicksPerSecond = double.NaN;

        viewModel.ClicksPerSecond = value;

        Assert.Equal(value, viewModel.SliderClicksPerSecond);
        Assert.Empty(viewModel.ValidationMessage);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void MovingSliderRestoresAValidEmptyEditor()
    {
        var viewModel = CreateViewModel();
        viewModel.ClicksPerSecond = double.NaN;

        viewModel.SliderClicksPerSecond = 14;

        Assert.Equal(14, viewModel.ClicksPerSecond);
        Assert.Equal(14, viewModel.SliderClicksPerSecond);
        Assert.Empty(viewModel.ValidationMessage);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(23)]
    [InlineData(double.NaN)]
    public void ToggleCommitsPendingInputBeforeCheckingWhetherItCanStart(double previousSpeed)
    {
        var controller = new RecordingController();
        var viewModel = new MainViewModel(TestStrings.Portuguese, controller, new MemorySettings(), false, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(new ReadyHotkeys());
        viewModel.ClicksPerSecond = previousSpeed;

        viewModel.Toggle(() => viewModel.ClicksPerSecond = 1);

        Assert.Equal(1, controller.LastPlan?.ClicksPerSecond);
    }

    [Fact]
    public void ToggleDoesNotStartWhenCommittingAnEmptyEditor()
    {
        var controller = new RecordingController();
        var viewModel = new MainViewModel(TestStrings.Portuguese, controller, new MemorySettings(), false, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(new ReadyHotkeys());

        viewModel.Toggle(() => viewModel.ClicksPerSecond = double.NaN);

        Assert.Null(controller.LastPlan);
    }

    [Theory]
    [InlineData(SessionState.Starting)]
    [InlineData(SessionState.Running)]
    public void ToggleStopsWithoutCommittingOrValidatingPendingInput(SessionState state)
    {
        var controller = new RecordingController { State = state };
        var viewModel = new MainViewModel(TestStrings.Portuguese, controller, new MemorySettings(), false, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(new ReadyHotkeys());
        viewModel.ClicksPerSecond = double.NaN;

        viewModel.Toggle(() => throw new InvalidOperationException("A parada não pode confirmar o editor."));

        Assert.Equal(1, controller.Stops);
        Assert.Null(controller.LastPlan);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-0.5, 0)]
    [InlineData(0, 0)]
    [InlineData(12.5, 12.5)]
    [InlineData(50, 50)]
    [InlineData(50.5, 50)]
    [InlineData(80, 50)]
    public async Task VariationInputClampsToItsRangeAndSavesTheAdjustedValue(double input, double expected)
    {
        var settings = new MemorySettings();
        var model = CreateViewModel(settings);
        model.VariationPercent = 20;

        model.VariationPercent = input;

        Assert.Equal(expected, model.VariationPercent);
        Assert.Equal(expected, model.SliderVariationPercent);
        Assert.Empty(model.VariationError);
        Assert.True(model.StartCommand.CanExecute(null));
        await model.SaveAsync();
        Assert.Equal(expected, settings.LastSaved?.Plan?.VariationPercent);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(12.5, 12.5)]
    [InlineData(51, 50)]
    public void VariationSliderClampsAndSynchronizesTheNumericField(double input, double expected)
    {
        var model = CreateViewModel();
        model.VariationPercent = 20;

        model.SliderVariationPercent = input;

        Assert.Equal(expected, model.VariationPercent);
        Assert.Equal(expected, model.SliderVariationPercent);
    }

    [Fact]
    public void EmptyVariationPreservesTheSliderAndMovingItRestoresValidInput()
    {
        var model = CreateViewModel();
        model.VariationPercent = 20;

        model.VariationPercent = double.NaN;

        Assert.True(double.IsNaN(model.VariationPercent));
        Assert.Equal(20, model.SliderVariationPercent);
        Assert.NotEmpty(model.VariationError);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.SaveCommand.CanExecute(null));

        model.SliderVariationPercent = 35;

        Assert.Equal(35, model.VariationPercent);
        Assert.Empty(model.VariationError);
        Assert.True(model.StartCommand.CanExecute(null));
        Assert.True(model.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void VariationSliderIgnoresNonFiniteAssignments(double value)
    {
        var model = CreateViewModel();
        model.VariationPercent = 20;

        model.SliderVariationPercent = value;

        Assert.Equal(20, model.VariationPercent);
        Assert.Equal(20, model.SliderVariationPercent);
    }
    private sealed class RecordingController : IClickSessionController
    {
        public ClickPlan? LastPlan { get; private set; }
        public int Stops { get; private set; }
        public SessionState State { get; set; } = SessionState.Idle;
        public ExecutionSnapshot Snapshot => new(Guid.Empty, State, 0,
            TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);
        public Task Completion => Task.CompletedTask;
        public bool Start(ClickPlan plan) { LastPlan = plan; State = SessionState.Running; return true; }
        public Task StopAsync() { Stops++; State = SessionState.Idle; return Task.CompletedTask; }
        public bool ResetFault() => true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CapturesAndAppliesACustomGestureOnlyAfterConfirmation()
    {
        var settings = new MemorySettings();
        var hotkeys = new ReadyHotkeys();
        var viewModel = CreateViewModel(settings, hotkeys);
        var candidate = new HotkeyGesture(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt);

        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        Assert.True(viewModel.IsCapturingHotkey);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        viewModel.CaptureHotkey(candidate);
        Assert.Equal(HotkeyGesture.Default, viewModel.Hotkey);
        Assert.Equal(0, hotkeys.RegistrationAttempts);
        Assert.True(viewModel.ApplyHotkeyCommand.CanExecute(null));
        Assert.False(viewModel.StartCommand.CanExecute(null));

        await viewModel.ApplyHotkeyCommand.ExecuteAsync(null);

        Assert.Equal(candidate, viewModel.Hotkey);
        Assert.Equal(candidate, hotkeys.LastRequested);
        Assert.Equal(candidate, settings.LastSaved?.Hotkey);
        Assert.Equal("Ctrl+Alt+A", viewModel.HotkeyCaptureText);
        Assert.False(viewModel.HasHotkeyDraft);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public void ExistingGlobalHotkeyIsCapturedInsteadOfStartingDuringRecording()
    {
        var controller = new RecordingController();
        var viewModel = new MainViewModel(TestStrings.Portuguese, controller, new MemorySettings(), false, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(new ReadyHotkeys());
        viewModel.BeginHotkeyCaptureCommand.Execute(null);

        viewModel.Toggle();
        viewModel.Toggle();

        Assert.Equal(HotkeyGesture.Default, viewModel.PendingHotkey);
        Assert.Null(controller.LastPlan);
        Assert.False(viewModel.IsCapturingHotkey);
    }

    [Fact]
    public void CancellingDoesNotRegisterOrSaveTheDraft()
    {
        var settings = new MemorySettings();
        var hotkeys = new ReadyHotkeys();
        var viewModel = CreateViewModel(settings, hotkeys);
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        viewModel.CaptureHotkey(new(0x42));

        viewModel.CancelHotkeyCaptureCommand.Execute(null);

        Assert.False(viewModel.HasHotkeyDraft);
        Assert.Equal(HotkeyGesture.Default, viewModel.Hotkey);
        Assert.Equal(0, hotkeys.RegistrationAttempts);
        Assert.Equal(0, settings.Saves);
    }

    [Fact]
    public async Task RegistrationConflictKeepsTheOldGestureAndDoesNotPersistTheDraft()
    {
        var settings = new MemorySettings();
        var hotkeys = new ReadyHotkeys { RegisterSucceeds = false };
        var viewModel = CreateViewModel(settings, hotkeys);
        var candidate = new HotkeyGesture(0x51, HotkeyModifiers.Control | HotkeyModifiers.Alt);
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        viewModel.CaptureHotkey(candidate);

        await viewModel.ApplyHotkeyCommand.ExecuteAsync(null);

        Assert.Equal(HotkeyGesture.Default, viewModel.Hotkey);
        Assert.Equal(candidate, viewModel.PendingHotkey);
        Assert.Equal(0, settings.Saves);
        Assert.Contains("Conflito", viewModel.HotkeyCaptureMessage);
    }

    [Theory]
    [InlineData(0x7B, 0, "F12")]
    [InlineData(0x79, 6, "emergência")]
    [InlineData(0x41, 8, "Windows")]
    public void ReservedGesturesKeepRecordingAndShowValidation(uint key, uint modifiers, string expectedMessage)
    {
        var viewModel = CreateViewModel();
        viewModel.BeginHotkeyCaptureCommand.Execute(null);

        viewModel.CaptureHotkey(new(key, (HotkeyModifiers)modifiers));

        Assert.True(viewModel.IsCapturingHotkey);
        Assert.Null(viewModel.PendingHotkey);
        Assert.Contains(expectedMessage, viewModel.HotkeyCaptureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.ApplyHotkeyCommand.CanExecute(null));
    }

    [Fact]
    public void ModifierAloneDoesNotCompleteTheCapture()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginHotkeyCaptureCommand.Execute(null);

        viewModel.CaptureHotkey(new(0x11, HotkeyModifiers.Control));

        Assert.True(viewModel.IsCapturingHotkey);
        Assert.Null(viewModel.PendingHotkey);
    }

    [Theory]
    [InlineData(SessionState.Starting)]
    [InlineData(SessionState.Running)]
    [InlineData(SessionState.Stopping)]
    public void CannotRecordOrApplyDuringASession(SessionState state)
    {
        var controller = new RecordingController { State = state };
        var viewModel = new MainViewModel(TestStrings.Portuguese, controller, new MemorySettings(), false, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(new ReadyHotkeys());

        viewModel.BeginHotkeyCaptureCommand.Execute(null);

        Assert.False(viewModel.IsCapturingHotkey);
        Assert.False(viewModel.BeginHotkeyCaptureCommand.CanExecute(null));
        Assert.False(viewModel.ApplyHotkeyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyingAHotkeyPreservesTheSavedPlanWhenNumericDraftIsInvalid()
    {
        var settings = new MemorySettings();
        var viewModel = CreateViewModel(settings);
        viewModel.ClicksPerSecond = double.NaN;
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        viewModel.CaptureHotkey(new(0x42, HotkeyModifiers.Shift));

        await viewModel.ApplyHotkeyCommand.ExecuteAsync(null);

        Assert.Equal(new HotkeyGesture(0x42, HotkeyModifiers.Shift), settings.LastSaved?.Hotkey);
        Assert.Equal(23, settings.LastSaved?.Plan?.ClicksPerSecond);
        Assert.True(double.IsNaN(viewModel.ClicksPerSecond));
    }

    [Fact]
    public async Task SavingOtherSettingsNeverPersistsAnUnconfirmedHotkey()
    {
        var settings = new MemorySettings();
        var viewModel = CreateViewModel(settings);
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        viewModel.CaptureHotkey(new(0x42));

        await viewModel.SaveAsync();

        Assert.Equal(HotkeyGesture.Default, settings.LastSaved?.Hotkey);
    }

    [Fact]
    public async Task ProtectedSettingsCanChangeHotkeyWithoutOverwritingTheFile()
    {
        var settings = new MemorySettings { CanSave = false };
        var viewModel = CreateViewModel(settings);
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        viewModel.CaptureHotkey(new(0x42));

        await viewModel.ApplyHotkeyCommand.ExecuteAsync(null);

        Assert.Equal(new HotkeyGesture(0x42), viewModel.Hotkey);
        Assert.Equal(0, settings.Saves);
        Assert.Equal(TestStrings.Portuguese.Get("HotkeyProtected"), viewModel.Message);
    }

    [Fact]
    public async Task SaveFailureKeepsTheAppliedGestureAndReportsSessionOnlyPersistence()
    {
        var settings = new MemorySettings { FailSaving = true };
        var viewModel = CreateViewModel(settings);
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        viewModel.CaptureHotkey(new(0x42));

        await viewModel.ApplyHotkeyCommand.ExecuteAsync(null);

        Assert.Equal(new HotkeyGesture(0x42), viewModel.Hotkey);
        Assert.False(viewModel.HasHotkeyDraft);
        Assert.Equal(TestStrings.Portuguese.Get("HotkeySaveFailed"), viewModel.Message);
    }

    [Fact]
    public void ShutdownDiscardsTheDraftAndPreventsFurtherCapture()
    {
        var viewModel = CreateViewModel();
        viewModel.BeginHotkeyCaptureCommand.Execute(null);

        viewModel.BeginShutdown();

        Assert.False(viewModel.HasHotkeyDraft);
        Assert.False(viewModel.BeginHotkeyCaptureCommand.CanExecute(null));
        Assert.False(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyingHotkeyWaitsForAnEarlierPlanSaveWithoutRevertingTheSavedPlan()
    {
        var settings = new MemorySettings { BlockFirstSave = true };
        var viewModel = CreateViewModel(settings);
        viewModel.ClicksPerSecond = 14;
        var saving = viewModel.SaveAsync();
        await settings.FirstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.BeginHotkeyCaptureCommand.Execute(null);
        var candidate = new HotkeyGesture(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt);
        viewModel.CaptureHotkey(candidate);
        var applying = viewModel.ApplyHotkeyCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(1, settings.Saves);
            Assert.False(applying.IsCompleted);
        }
        finally
        {
            settings.ResumeFirstSave.TrySetResult();
        }

        await Task.WhenAll(saving, applying).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(14, settings.LastSaved?.Plan?.ClicksPerSecond);
        Assert.Equal(candidate, settings.LastSaved?.Hotkey);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SerializedLanguageHotkeyAndPlanSavesPreserveConfirmedPreferences(bool languageFirst, bool failFirst)
    {
        var settings = new MemorySettings { BlockFirstSave = true, FailFirstSave = failFirst };
        var model = CreateViewModel(settings);
        model.ClicksPerSecond = 14;
        Task firstSave;
        if (languageFirst)
        {
            model.LanguageIndex = 3;
            firstSave = model.LanguageSaveTask;
        }
        else firstSave = model.SaveAsync();

        Task applying = Task.CompletedTask;
        Task saving = Task.CompletedTask;
        var candidate = new HotkeyGesture(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt);
        try
        {
            await settings.FirstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!languageFirst) model.LanguageIndex = 3;
            model.BeginHotkeyCaptureCommand.Execute(null);
            model.CaptureHotkey(candidate);
            applying = model.ApplyHotkeyCommand.ExecuteAsync(null);
            if (languageFirst) saving = model.SaveAsync();

            Assert.Equal(1, settings.Saves);
            Assert.Null(settings.LastSaved);
            Assert.False(firstSave.IsCompleted);
            Assert.False(applying.IsCompleted);
            Assert.False(model.LanguageSaveTask.IsCompleted);
            if (languageFirst) Assert.False(saving.IsCompleted);
            Assert.NotEqual(TestStrings.Portuguese.Get("Saved"), model.SaveFeedback);
        }
        finally
        {
            settings.ResumeFirstSave.TrySetResult();
            await Task.WhenAll(firstSave, applying, saving, model.LanguageSaveTask).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal("es", settings.LastSaved?.Language);
        Assert.Equal(candidate, settings.LastSaved?.Hotkey);
        Assert.Equal(failFirst && !languageFirst ? 23 : 14, settings.LastSaved?.Plan?.ClicksPerSecond);
        if (failFirst)
            Assert.Contains(_diagnostics.Entries, entry => entry.Event == (languageFirst ? "LanguageSaveFailed" : "SettingsSaveFailed"));

        await model.SaveAsync();
        Assert.Equal("es", settings.LastSaved?.Language);
        Assert.Equal(candidate, settings.LastSaved?.Hotkey);
        Assert.Equal(14, settings.LastSaved?.Plan?.ClicksPerSecond);
        Assert.Equal(TestStrings.Portuguese.Get("Saved"), model.SaveFeedback);
        Assert.False(model.HasMessage);
    }

    private MainViewModel CreateViewModel(MemorySettings? settings = null, ReadyHotkeys? hotkeys = null)
    {
        var viewModel = new MainViewModel(TestStrings.Portuguese, new IdleController(), settings ?? new MemorySettings(), false, writeDiagnostic: _diagnostics.Write);
        viewModel.Attach(hotkeys ?? new ReadyHotkeys());
        return viewModel;
    }

    private sealed class MemorySettings : ISettingsStore
    {
        public int Saves { get; private set; }
        public AppSettings? LastSaved { get; private set; }
        public bool CanSave { get; init; } = true;
        public bool FailSaving { get; init; }
        public bool BlockFirstSave { get; init; }
        public bool FailFirstSave { get; init; }
        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SettingsLoadResult Load() => new(new(Plan: new(ClicksPerSecond: 23)), CanSave: CanSave);

        public async Task SaveAsync(AppSettings settings)
        {
            if (FailSaving)
                throw new IOException("Falha simulada.");
            Saves++;
            if (BlockFirstSave && Saves == 1)
            {
                FirstSaveEntered.TrySetResult();
                await ResumeFirstSave.Task;
                if (FailFirstSave) throw new IOException("Simulated first-save failure.");
            }
            LastSaved = settings;
        }
    }

    private sealed class IdleController : IClickSessionController
    {
        public ExecutionSnapshot Snapshot { get; } = new(Guid.Empty, SessionState.Idle, 0,
            TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);
        public Task Completion => Task.CompletedTask;
        public bool Start(ClickPlan plan) => throw new InvalidOperationException("Este teste não pode iniciar uma sessão.");
        public Task StopAsync() => Task.CompletedTask;
        public bool ResetFault() => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ReadyHotkeys : IHotkeyService
    {
        public bool RegisterSucceeds { get; init; } = true;
        public int RegistrationAttempts { get; private set; }
        public HotkeyGesture? LastRequested { get; private set; }
        public bool HotkeysReady => true;
        public bool EmergencyReady => true;
        public string Status => "Pronto";
        public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error)
        {
            RegistrationAttempts++;
            LastRequested = hotkey;
            error = RegisterSucceeds ? null : "Conflito de atalho simulado. O anterior foi preservado.";
            return RegisterSucceeds;
        }
    }
}
