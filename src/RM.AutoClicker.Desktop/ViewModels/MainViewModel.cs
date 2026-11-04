using System.Globalization;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.Localization;

namespace RM.AutoClicker.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IClickSessionController _controller;
    private readonly ISettingsStore _settings;
    private readonly IAppStrings _strings;
    private readonly Action<string, Exception?> _writeDiagnostic;
    private readonly string _initialLanguagePreference;
    private bool _loading = true;
    private readonly bool _canSave;
    private readonly bool _smokeTest;
    private readonly TimeProvider _timeProvider;
    private long? _saveConfirmationTimestamp;
    private bool _saveFailed;
    private IHotkeyService? _integration;
    private bool _shuttingDown;
    private bool _restarting;
    private AppSettings _savedSettings;
    private readonly SemaphoreSlim _preferencesGate = new(1, 1);
    private Task _minimizeToTraySaveTask = Task.CompletedTask;
    private double _sliderClicksPerSecond = 10;
    private ClickPlan? _activePlan;
    private string _noticeOrigin = string.Empty;
    private SessionFailure? _reportedFailure;

    public MainViewModel(IAppStrings strings, IClickSessionController controller, ISettingsStore settings, bool smokeTest,
        TimeProvider? timeProvider = null, SettingsLoadResult? loadedSettings = null,
        Action<string, Exception?>? writeDiagnostic = null)
    {
        _strings = strings;
        _writeDiagnostic = writeDiagnostic ?? AppDiagnostics.Write;
        _controller = controller;
        _settings = settings;
        _smokeTest = smokeTest;
        _timeProvider = timeProvider ?? TimeProvider.System;
        var loaded = loadedSettings ?? (smokeTest ? new SettingsLoadResult(new(), CanSave: false) : settings.Load());
        _savedSettings = loaded.Settings;
        _initialLanguagePreference = loaded.Settings.Language;
        LanguageIndex = AppStrings.Preferences.ToList().IndexOf(loaded.Settings.Language);
        _canSave = loaded.CanSave;
        var plan = loaded.Settings.Plan ?? new ClickPlan();
        ClicksPerSecond = plan.ClicksPerSecond;
        VariationPercent = plan.VariationPercent;
        ButtonIndex = (int)plan.Button;
        HasInitialDelay = loaded.Settings.HasInitialDelay;
        InitialDelaySeconds = plan.InitialDelay > TimeSpan.Zero ? plan.InitialDelay.TotalSeconds : 3;
        HasClickLimit = plan.MaximumClicks.HasValue;
        MaximumClicks = plan.MaximumClicks ?? 100;
        HasDurationLimit = plan.MaximumDuration.HasValue;
        MaximumDurationSeconds = plan.MaximumDuration?.TotalSeconds ?? 30;
        Hotkey = loaded.Settings.EffectiveHotkey;
        ThemeIndex = loaded.Settings.ThemeIndex;
        MinimizeToTray = loaded.Settings.MinimizeToTray;
        Message = loaded.Warning is { } warning ? _strings.Get(warning.ToString()) : string.Empty;
        HotkeyStatus = _strings.Get("PreparingHotkeys");
        _loading = false;
        RefreshPresentation();
        UpdateSaveFeedback();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntervalText), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial double ClicksPerSecond { get; set; } = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial double VariationPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial int ButtonIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool HasInitialDelay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial double InitialDelaySeconds { get; set; } = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool HasClickLimit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial double MaximumClicks { get; set; } = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool HasDurationLimit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial double MaximumDurationSeconds { get; set; } = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyCaptureText))]
    public partial HotkeyGesture Hotkey { get; private set; } = HotkeyGesture.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyCaptureText), nameof(HasHotkeyDraft))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(ApplyHotkeyCommand), nameof(CancelHotkeyCaptureCommand))]
    public partial bool IsCapturingHotkey { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyCaptureText), nameof(HasHotkeyDraft))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(ApplyHotkeyCommand), nameof(CancelHotkeyCaptureCommand))]
    public partial HotkeyGesture? PendingHotkey { get; private set; }

    [ObservableProperty]
    public partial string HotkeyCaptureMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotkeyStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SaveFeedback { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMessageError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CounterText), nameof(ElapsedText), nameof(RateText), nameof(CanEditHotkey))]
    public partial ExecutionSnapshot Snapshot { get; set; } = new(Guid.Empty, SessionState.Idle, 0, TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageChangePending))]
    public partial int LanguageIndex { get; set; }

    public bool CanChangeLanguage => _canSave && !_shuttingDown && !_restarting;
    public bool CanChangeMinimizeToTray => _canSave && !_shuttingDown && !_restarting;
    public Task MinimizeToTraySaveTask => _minimizeToTraySaveTask;
    private string SelectedLanguagePreference => LanguageIndex >= 0 && LanguageIndex < AppStrings.Preferences.Count
        ? AppStrings.Preferences[LanguageIndex] : _savedSettings.Language;
    public bool LanguageChangePending => _savedSettings.Language != _initialLanguagePreference;
    public Task LanguageSaveTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial string RestartError { get; set; } = string.Empty;
    public string[] ThemeLabels => [_strings.Get("ThemeSystem"), _strings.Get("ThemeLight"), _strings.Get("ThemeDark")];
    public string[] MouseLabels => [_strings.Get("MouseLeft"), _strings.Get("MouseRight"), _strings.Get("MouseMiddle")];
    public string[] LanguageLabels => [_strings.Get("LanguageAutomatic"), "Português (Brasil)", "English", "Español"];
    public string CurrentHotkeyText => _strings.Get("CurrentHotkey", AppliedHotkeyText);

    public event Action<int>? ThemeChanged;
    public event Action? RestartRequested;

    public double SliderClicksPerSecond
    {
        get => _sliderClicksPerSecond;
        set
        {
            if (double.IsFinite(value) && value is >= 1 and <= 25)
                ClicksPerSecond = value;
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool CanEditHotkey => !_shuttingDown && !_restarting && Snapshot.State is SessionState.Idle or SessionState.Faulted;
    public bool HasHotkeyDraft => IsCapturingHotkey || PendingHotkey is not null;
    public string HotkeyCaptureText => IsCapturingHotkey ? _strings.Get("CapturePrompt") : GetHotkeyName(PendingHotkey ?? Hotkey);
    public bool IsSessionActive => Snapshot.State is SessionState.Starting or SessionState.Running or SessionState.Stopping;
    public bool IsInputSuppressed => Snapshot.State == SessionState.Running && Snapshot.IsInputSuppressed;
    public bool ShowPendingChanges => IsSessionActive && _activePlan is not null && TryBuildPlan(out var plan, out _) && plan != _activePlan;
    public string PendingChangesText => ShowPendingChanges && TryBuildPlan(out var plan, out _) ? _strings.Get("NextPlan", DescribePlan(plan!)) : string.Empty;
    public string ActivePlanText => _activePlan is null ? string.Empty : _strings.Get(IsSessionActive ? "CurrentPlan" : "LastPlan", DescribePlan(_activePlan));
    public string AppliedHotkeyText => GetHotkeyName(Hotkey);
    public string HotkeyAccessibleName => IsCapturingHotkey ? _strings.Get("HotkeyRecording")
        : _strings.Get("HotkeyAccessible", AppliedHotkeyText);
    public string HotkeyOperationText => _integration?.EmergencyReady == true ? _strings.Get("EmergencyReady") : _strings.Get("EmergencyUnavailable");
    public string HotkeyEditHint => !CanEditHotkey ? _strings.Get("StopBeforeHotkey")
        : Hotkey.Modifiers == HotkeyModifiers.None ? _strings.Get("SimpleKeyWarning") : string.Empty;
    public bool HasHotkeyEditHint => !string.IsNullOrEmpty(HotkeyEditHint);
    public string AdvancedSummary => HasAdvancedErrors ? _strings.Get("ReviewOptions") : DescribeAdvancedPlan();
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationMessage);
    public bool HasAdvancedErrors => !string.IsNullOrEmpty(DelayError + ClickLimitError + DurationLimitError + VariationError);
    public string SpeedError => !double.IsFinite(ClicksPerSecond) || ClicksPerSecond is < 1 or > 25 ? _strings.Get("SpeedError") : string.Empty;
    public string MouseError => ButtonIndex is < 0 or > 2 ? _strings.Get("MouseError") : string.Empty;
    public string DelayError => HasInitialDelay && (!double.IsFinite(InitialDelaySeconds) || InitialDelaySeconds is < 0.1 or > 300) ? _strings.Get("DelayError", 0.1) : string.Empty;
    public string ClickLimitError => HasClickLimit && (!double.IsFinite(MaximumClicks) || MaximumClicks < 1 || MaximumClicks > 1_000_000_000 || MaximumClicks != Math.Truncate(MaximumClicks)) ? _strings.Get("ClickLimitError") : string.Empty;
    public string DurationLimitError => HasDurationLimit && (!double.IsFinite(MaximumDurationSeconds) || MaximumDurationSeconds is < 0.1 or > 86400) ? _strings.Get("DurationError", 0.1) : string.Empty;
    public string VariationError => !double.IsFinite(VariationPercent) || VariationPercent is < 0 or > 50 ? _strings.Get("VariationError") : string.Empty;
    public string StartBlocker => _shuttingDown ? _strings.Get("ShuttingDown")
        : _restarting ? _strings.Get("Restarting")
        : IsSessionActive ? string.Empty
        : HasHotkeyDraft ? _strings.Get("ConfirmHotkey")
        : HasValidationError ? _strings.Get("ReviewBeforeStart")
        : _integration?.HotkeysReady != true ? _strings.Get("CheckHotkeys")
        : !_controller.Completion.IsCompleted ? _strings.Get("FinishingPrevious")
        : string.Empty;
    public bool HasStartBlocker => !string.IsNullOrEmpty(StartBlocker);
    public bool ShowStartBlocker => HasStartBlocker && !HasValidationError;
    public bool CanEmphasizeStart => CanStart();
    public string IntervalText => double.IsFinite(ClicksPerSecond) && ClicksPerSecond > 0 ? _strings.Get("Interval", 1000 / ClicksPerSecond) : _strings.Get("InvalidInterval");
    public string CounterText => Snapshot.SentClicks.ToString("N0", CultureInfo.CurrentCulture);
    public string ElapsedText => $"{Math.Floor(Snapshot.Elapsed.TotalHours):00}:{Snapshot.Elapsed.Minutes:00}:{Snapshot.Elapsed.Seconds:00}";
    public string RateText => Snapshot.Elapsed.TotalSeconds >= 1 ? _strings.Get("Rate", Snapshot.SentClicks / Snapshot.Elapsed.TotalSeconds) : string.Empty;
    public bool HasSessionDetails => Snapshot.Elapsed.TotalSeconds >= 1 || _activePlan is not null || IsInputSuppressed;
    public string ValidationMessage => new[] { SpeedError, MouseError, DelayError, ClickLimitError, DurationLimitError, VariationError }.FirstOrDefault(error => !string.IsNullOrEmpty(error)) ?? string.Empty;

    private string BuildStatusText() => Snapshot.State switch
    {
        SessionState.Starting => _strings.Get("Starting", Math.Ceiling(Snapshot.StartDelayRemaining.TotalSeconds)),
        SessionState.Running => Snapshot.IsInputSuppressed ? _strings.Get("Suppressed") : _strings.Get("Running"),
        SessionState.Stopping => _strings.Get("Stopping"),
        SessionState.Faulted => _strings.Get("Faulted"),
        SessionState.Disposed => _strings.Get("Closed"),
        _ => Snapshot.CompletionReason switch
        {
            CompletionReason.ClickLimit => _strings.Get("CompletedClicks"),
            CompletionReason.DurationLimit => _strings.Get("CompletedTime"),
            CompletionReason.UserStopped => _strings.Get("Interrupted"),
            _ => CanStart() ? _strings.Get("Ready") : _strings.Get("Stopped")
        }
    };

    public void Attach(IHotkeyService integration)
    {
        _integration = integration;
        OnPropertyChanged(nameof(HotkeyCaptureText));
        Refresh();
    }

    public void Refresh()
    {
        Snapshot = _controller.Snapshot;
        if (Snapshot.Failure is { } failure)
        {
            if (!ReferenceEquals(_reportedFailure, failure))
            {
                _writeDiagnostic("SessionFailed", new InvalidOperationException(failure.Message));
                _reportedFailure = failure;
            }
            ReportNotice("session", failure.Code == UserMessageCode.SessionInput && failure.InputResult is { } input
                ? _strings.Get("SessionInput", input.InsertedEvents, input.RequestedEvents, input.ErrorCode)
                : _strings.Get(failure.Code.ToString()), true);
        }
        else
        {
            _reportedFailure = null;
            ClearNotice("session");
        }
        if (_integration is not null)
            HotkeyStatus = _integration.Status;

        RefreshPresentation();

        UpdateSaveFeedback();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        BeginHotkeyCaptureCommand.NotifyCanExecuteChanged();
        ApplyHotkeyCommand.NotifyCanExecuteChanged();
        CancelHotkeyCaptureCommand.NotifyCanExecuteChanged();
    }

    public void Toggle(Action? commitPendingInput = null)
    {
        if (CanStop())
        {
            StopCommand.Execute(null);
            return;
        }

        if (IsCapturingHotkey)
        {
            CaptureHotkey(Hotkey);
            return;
        }
        if (HasHotkeyDraft)
            return;

        commitPendingInput?.Invoke();
        if (CanStart())
            StartCommand.Execute(null);
    }

    public void BeginShutdown()
    {
        _shuttingDown = true;
        OnPropertyChanged(nameof(CanChangeLanguage));
        OnPropertyChanged(nameof(CanChangeMinimizeToTray));
        RestartCommand.NotifyCanExecuteChanged();
        CancelHotkeyCapture();
        OnPropertyChanged(nameof(CanEditHotkey));
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        try
        {
            if (!TryBuildPlan(out var plan, out var error))
            {
                ReportNotice("validation", error!, true);
                return;
            }

            if (_controller.Snapshot.State == SessionState.Faulted && !_controller.ResetFault())
            {
                Refresh();
                return;
            }

            ClearNotice("session");
            ClearNotice("validation");
            ClearNotice("operation");
            if (!_controller.Start(plan!))
                return;
            _activePlan = plan;
            Refresh();
            await SaveAsync();
        }
        catch (Exception exception)
        {
            ReportNotice("operation", _strings.Get("StartFailed"), true);
            _writeDiagnostic("StartFailed", exception);
        }
        finally
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    public async Task StopAsync()
    {
        try
        {
            await _controller.StopAsync();
            ClearNotice("operation");
        }
        catch (Exception exception)
        {
            ReportNotice("operation", _strings.Get("StopFailed"), true);
            _writeDiagnostic("StopFailed", exception);
        }
        finally
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        await Task.WhenAll(LanguageSaveTask, MinimizeToTraySaveTask);
        if (!_canSave || !TryBuildPlan(out var plan, out _))
            return;

        await _preferencesGate.WaitAsync();
        try
        {
            var settings = new AppSettings(Plan: plan, Hotkey: Hotkey, ThemeIndex: ThemeIndex,
                HasInitialDelay: HasInitialDelay, Language: SelectedLanguagePreference, MinimizeToTray: MinimizeToTray);
            await _settings.SaveAsync(settings);
            _savedSettings = settings;
            ConfirmPreferencesSaved();
            ClearNotice("preferences");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportNotice("preferences", _strings.Get("SaveFailed"));
            _saveFailed = true;
            UpdateSaveFeedback();
            _writeDiagnostic("SettingsSaveFailed", exception);
        }
        finally
        {
            _preferencesGate.Release();
        }
    }

    private bool CanStart() => !_shuttingDown && !_restarting && !_smokeTest && !HasHotkeyDraft && _integration?.HotkeysReady == true
        && Snapshot.State is SessionState.Idle or SessionState.Faulted
        && _controller.Completion.IsCompleted && TryBuildPlan(out _, out _);

    private bool CanStop() => Snapshot.State is SessionState.Starting or SessionState.Running;

    private bool CanSave() => _canSave && !_shuttingDown && !_restarting && TryBuildPlan(out _, out _);

    private bool CanRestart() => CanChangeLanguage && LanguageChangePending
        && SelectedLanguagePreference == _savedSettings.Language;

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        if (!CanRestart())
            return;

        _restarting = true;
        RestartError = string.Empty;
        CancelHotkeyCapture();
        RefreshRestartState();
        try
        {
            await LanguageSaveTask;
            await _controller.StopAsync();
            if (_controller.Snapshot.State == SessionState.Faulted && !_controller.ResetFault())
                throw new InvalidOperationException("Pending input could not be released before restarting.");
            await SaveAsync();
            if (_saveFailed)
                throw new IOException("Preferences could not be saved before restarting.");
            if (_shuttingDown)
                return;
            if (RestartRequested is not { } restart)
                throw new InvalidOperationException("No application restart handler is registered.");
            restart();
        }
        catch (Exception exception)
        {
            RestartError = _strings.Get("RestartFailed");
            _writeDiagnostic("ApplicationRestartFailed", exception);
        }
        finally
        {
            _restarting = false;
            RefreshRestartState();
        }
    }

    private void RefreshRestartState()
    {
        OnPropertyChanged(nameof(CanChangeLanguage));
        OnPropertyChanged(nameof(CanChangeMinimizeToTray));
        OnPropertyChanged(nameof(CanEditHotkey));
        RestartCommand.NotifyCanExecuteChanged();
        Refresh();
    }

    private bool TryBuildPlan(out ClickPlan? plan, out string? error)
    {
        if (HasValidationError)
        {
            plan = null;
            error = ValidationMessage;
            return false;
        }
        try
        {
            if (double.IsNaN(ClicksPerSecond))
                throw new ArgumentException(_strings.Get("SpeedError"));
            if (HasInitialDelay && (!double.IsFinite(InitialDelaySeconds) || InitialDelaySeconds is <= 0 or > 300))
                throw new ArgumentException(_strings.Get("DelayError", 0.1));
            if (HasClickLimit && (!double.IsFinite(MaximumClicks) || MaximumClicks < 1 || MaximumClicks > 1_000_000_000 || MaximumClicks != Math.Truncate(MaximumClicks)))
                throw new ArgumentException(_strings.Get("ClickLimitError"));
            if (HasDurationLimit && (!double.IsFinite(MaximumDurationSeconds) || MaximumDurationSeconds is < 0.1 or > 86400))
                throw new ArgumentException(_strings.Get("DurationError", 0.1));

            plan = new(ClicksPerSecond, VariationPercent, (ClickButton)ButtonIndex,
                HasInitialDelay ? TimeSpan.FromSeconds(InitialDelaySeconds) : TimeSpan.Zero, HasClickLimit ? (long)MaximumClicks : null,
                HasDurationLimit ? TimeSpan.FromSeconds(MaximumDurationSeconds) : null);
            plan.Validate();
            error = null;
            return true;
        }
        catch (ArgumentException)
        {
            plan = null;
            error = _strings.Get("InvalidSettings");
            return false;
        }
    }

    partial void OnClicksPerSecondChanged(double value)
    {
        if (double.IsFinite(value) && value is >= 1 and <= 25 && _sliderClicksPerSecond != value)
        {
            _sliderClicksPerSecond = value;
            OnPropertyChanged(nameof(SliderClicksPerSecond));
        }
    }

    partial void OnThemeIndexChanged(int value) => ThemeChanged?.Invoke(value);

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (!_loading)
            _minimizeToTraySaveTask = SaveMinimizeToTrayAsync();
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < AppStrings.Preferences.Count)
        {
            RestartError = string.Empty;
            LanguageSaveTask = SaveLanguageAsync();
            RestartCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SaveLanguageAsync()
    {
        if (!CanChangeLanguage)
            return;
        await _preferencesGate.WaitAsync();
        try
        {
            var settings = _savedSettings with { Language = SelectedLanguagePreference };
            await _settings.SaveAsync(settings);
            _savedSettings = settings;
            ClearNotice("preferences");
            ConfirmPreferencesSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportNotice("preferences", _strings.Get("SaveFailed"));
            _saveFailed = true;
            UpdateSaveFeedback();
            _writeDiagnostic("LanguageSaveFailed", exception);
        }
        finally
        {
            _preferencesGate.Release();
        }
    }

    private async Task SaveMinimizeToTrayAsync()
    {
        if (!CanChangeMinimizeToTray)
        {
            _loading = true;
            MinimizeToTray = _savedSettings.MinimizeToTray;
            _loading = false;
            UpdateSaveFeedback();
            return;
        }

        var requestedValue = MinimizeToTray;
        await _preferencesGate.WaitAsync();
        try
        {
            requestedValue = MinimizeToTray;
            if (requestedValue == _savedSettings.MinimizeToTray)
                return;

            var settings = _savedSettings with { MinimizeToTray = requestedValue };
            await _settings.SaveAsync(settings);
            _savedSettings = settings;
            ClearNotice("preferences");
            ConfirmPreferencesSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportNotice("preferences", _strings.Get("SaveFailed"));
            _saveFailed = true;
            if (requestedValue == MinimizeToTray)
            {
                _loading = true;
                MinimizeToTray = _savedSettings.MinimizeToTray;
                _loading = false;
            }
            UpdateSaveFeedback();
            _writeDiagnostic("MinimizeToTraySaveFailed", exception);
        }
        finally
        {
            _preferencesGate.Release();
        }
    }

    private string GetHotkeyName(HotkeyGesture hotkey) => _integration?.GetDisplayName(hotkey) ?? hotkey.ToString();

    [RelayCommand(CanExecute = nameof(CanBeginHotkeyCapture))]
    private void BeginHotkeyCapture()
    {
        Refresh();
        if (!CanBeginHotkeyCapture())
            return;

        PendingHotkey = null;
        IsCapturingHotkey = true;
        HotkeyCaptureMessage = _strings.Get("CaptureHint");
    }

    public void CaptureHotkey(HotkeyGesture candidate)
    {
        if (!IsCapturingHotkey || !CanEditHotkey)
            return;
        if (HotkeyGesture.IsModifierKey(candidate.VirtualKey))
            return;

        try
        {
            candidate.Validate();
            PendingHotkey = candidate;
            IsCapturingHotkey = false;
            HotkeyCaptureMessage = candidate.Modifiers == HotkeyModifiers.None
                ? _strings.Get("CapturedSimple", GetHotkeyName(candidate))
                : _strings.Get("Captured", GetHotkeyName(candidate));
        }
        catch (ArgumentException exception)
        {
            HotkeyCaptureMessage = _strings.Get(exception is AppValidationException validation ? validation.Code.ToString() : "HotkeyRegisterFailed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyHotkey))]
    private async Task ApplyHotkeyAsync()
    {
        Refresh();
        if (!CanApplyHotkey() || _integration is null)
            return;
        var candidate = PendingHotkey!;
        if (!_integration.TryChangeHotkey(candidate, out var error))
        {
            HotkeyCaptureMessage = error ?? _strings.Get("HotkeyRegisterFailed");
            Refresh();
            return;
        }

        Hotkey = candidate;
        CancelHotkeyCapture();
        HotkeyCaptureMessage = _strings.Get("HotkeyApplying", GetHotkeyName(candidate));
        Refresh();
        if (!_canSave)
        {
            ReportNotice("preferences", _strings.Get("HotkeyProtected"));
            HotkeyCaptureMessage = _strings.Get("HotkeyTemporary");
            return;
        }

        await _preferencesGate.WaitAsync();
        try
        {
            var settings = _savedSettings with { Hotkey = Hotkey };
            await _settings.SaveAsync(settings);
            _savedSettings = settings;
            HotkeyCaptureMessage = _strings.Get("HotkeySaved", GetHotkeyName(candidate));
            ClearNotice("preferences");
            ConfirmPreferencesSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportNotice("preferences", _strings.Get("HotkeySaveFailed"));
            HotkeyCaptureMessage = _strings.Get("HotkeyTemporary");
            _writeDiagnostic("HotkeySaveFailed", exception);
        }
        finally
        {
            _preferencesGate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelHotkeyCapture))]
    public void CancelHotkeyCapture()
    {
        IsCapturingHotkey = false;
        PendingHotkey = null;
        HotkeyCaptureMessage = string.Empty;
    }

    private bool CanBeginHotkeyCapture() => CanEditHotkey && _integration is not null && _controller.Completion.IsCompleted;
    private bool CanApplyHotkey() => CanBeginHotkeyCapture() && !IsCapturingHotkey && PendingHotkey is not null;
    private bool CanCancelHotkeyCapture() => HasHotkeyDraft;

    public void ReportIntegrationFailure() => ReportNotice("integration", _strings.Get("IntegrationFailed"), true);

    public void ReportTrayResult(bool hidden)
    {
        if (hidden)
            ClearNotice("tray");
        else
            ReportNotice("tray", _strings.Get("TrayUnavailable"));
    }

    private void ReportNotice(string origin, string message, bool isError = false)
    {
        _noticeOrigin = origin;
        IsMessageError = isError;
        Message = message;
    }

    private void ClearNotice(string origin)
    {
        if (_noticeOrigin != origin)
            return;
        _noticeOrigin = string.Empty;
        Message = string.Empty;
        IsMessageError = false;
    }

    private string DescribePlan(ClickPlan plan) => _strings.Get("Plan", plan.ClicksPerSecond, MouseLabels[(int)plan.Button], DescribeAdvanced(plan));

    private string DescribeAdvanced(ClickPlan plan)
    {
        var options = new List<string>();
        if (plan.InitialDelay > TimeSpan.Zero) options.Add(_strings.Get("PlanDelay", plan.InitialDelay.TotalSeconds));
        if (plan.MaximumClicks is { } clicks) options.Add(_strings.Get("PlanClicks", clicks));
        if (plan.MaximumDuration is { } duration) options.Add(_strings.Get("PlanDuration", duration.TotalSeconds));
        if (plan.VariationPercent > 0) options.Add(_strings.Get("PlanVariation", plan.VariationPercent));
        return options.Count == 0 ? _strings.Get("Unlimited") : string.Join(" · ", options);
    }

    private string DescribeAdvancedPlan() => TryBuildPlan(out var plan, out _) ? DescribeAdvanced(plan!) : _strings.Get("NextOptions");

    private void ConfirmPreferencesSaved()
    {
        _saveFailed = false;
        OnPropertyChanged(nameof(LanguageChangePending));
        RestartCommand.NotifyCanExecuteChanged();
        _saveConfirmationTimestamp = _timeProvider.GetTimestamp();
        UpdateSaveFeedback();
    }

    private void UpdateSaveFeedback()
    {
        SaveFeedback = !_canSave ? _strings.Get("PreferencesProtected")
            : !TryBuildPlan(out var plan, out _) ? _strings.Get("CorrectToSave")
            : _saveFailed ? _strings.Get("UnsavedFailure")
            : plan != (_savedSettings.Plan ?? new ClickPlan()) || Hotkey != _savedSettings.EffectiveHotkey
                || ThemeIndex != _savedSettings.ThemeIndex || HasInitialDelay != _savedSettings.HasInitialDelay
                || MinimizeToTray != _savedSettings.MinimizeToTray
                ? _strings.Get("Unsaved")
                : _saveConfirmationTimestamp is { } timestamp && _timeProvider.GetElapsedTime(timestamp) < TimeSpan.FromSeconds(3)
                    ? _strings.Get("Saved") : string.Empty;
    }

    private void RefreshPresentation()
    {
        StatusText = BuildStatusText();
        foreach (var property in new[] { nameof(IsSessionActive), nameof(IsInputSuppressed), nameof(ShowPendingChanges), nameof(PendingChangesText),
            nameof(ActivePlanText), nameof(HasSessionDetails), nameof(CurrentHotkeyText), nameof(AppliedHotkeyText), nameof(HotkeyAccessibleName), nameof(HotkeyOperationText), nameof(HotkeyEditHint),
            nameof(HasHotkeyEditHint), nameof(AdvancedSummary), nameof(HasValidationError), nameof(HasAdvancedErrors), nameof(SpeedError),
            nameof(MouseError), nameof(DelayError), nameof(ClickLimitError), nameof(DurationLimitError), nameof(VariationError), nameof(StartBlocker), nameof(HasStartBlocker), nameof(ShowStartBlocker), nameof(CanEmphasizeStart) })
            OnPropertyChanged(property);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(ClicksPerSecond) or nameof(ButtonIndex) or nameof(HasInitialDelay) or nameof(InitialDelaySeconds)
            or nameof(HasClickLimit) or nameof(MaximumClicks) or nameof(HasDurationLimit) or nameof(MaximumDurationSeconds) or nameof(VariationPercent)
            or nameof(ThemeIndex) or nameof(MinimizeToTray) or nameof(Hotkey) or nameof(IsCapturingHotkey) or nameof(PendingHotkey))
        {
            _saveConfirmationTimestamp = null;
            RefreshPresentation();
            UpdateSaveFeedback();
            ClearNotice("validation");
        }
    }
}
