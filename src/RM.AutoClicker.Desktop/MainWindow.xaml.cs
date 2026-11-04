using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Execution;
using RM.AutoClicker.Desktop.Localization;
using System.Globalization;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.ViewModels;
using RM.AutoClicker.Platform.Windows.Hotkeys;
using RM.AutoClicker.Platform.Windows.Input;
using RM.AutoClicker.Platform.Windows.Persistence;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace RM.AutoClicker.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly IAppStrings _strings;
    private readonly ClickSessionController _controller;
    private readonly WindowIntegration? _integration;
    private readonly DispatcherQueueTimer _timer;
    private Microsoft.UI.System.ThemeSettings? _themeSettings;
    private readonly bool _smokeTest;
    private readonly SmokeInputEmitter? _smokeEmitter;
    private bool _closing;
    private bool _closeConfirmed;
    private uint? _capturedHotkeyKey;
    private bool _hotkeyActivationPending;
    private readonly List<object> _responsiveChecks = [];
    private object? _advancedRevealCheck;
    private object? _windowSizingCheck;
    private object? _numericPopupDismissalCheck;
    private bool _advancedRevealPending;
    private int _advancedRevealVersion;

    public MainWindow(IAppStrings strings, ISettingsStore settings, SettingsLoadResult loadedSettings,
        bool smokeTest = false, bool testIntegration = false)
    {
        _strings = strings;
        _smokeTest = smokeTest;
        _smokeEmitter = smokeTest ? new SmokeInputEmitter() : null;
        _controller = new ClickSessionController(_smokeEmitter is { } smokeEmitter ? smokeEmitter : new WindowsInputEmitter());
        ViewModel = new MainViewModel(strings, _controller, settings, smokeTest, loadedSettings: loadedSettings);
        ViewModel.RestartRequested += () =>
        {
            var arguments = _smokeTest ? "--smoke-test --smoke-restart --smoke-restarted" : string.Empty;
            var reason = Microsoft.Windows.AppLifecycle.AppInstance.Restart(arguments);
            throw new InvalidOperationException($"Application restart failed: {reason}.");
        };
        InitializeComponent();
        ConfigureCustomTitleBar();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);
        Root.Language = strings.Language;
        var numberCulture = CultureInfo.CurrentCulture.Name;
        if (string.IsNullOrEmpty(numberCulture))
            numberCulture = "en-US";
        var numberRegion = new RegionInfo(numberCulture).TwoLetterISORegionName;
        foreach (var input in new[] { SpeedInput, DelayInput, ClickLimitInput, DurationInput, VariationInput })
            input.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter([numberCulture], numberRegion) { FractionDigits = 0 };
        ConfigureWindowBounds();
        var scale = GetWindowScale();
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min((int)(760 * scale), workArea.Width), Math.Min((int)(650 * scale), workArea.Height)));
        Root.Loaded += OnWindowLoaded;
        Root.SizeChanged += OnLayoutSizeChanged;
        AppTitleBar.SizeChanged += OnLayoutSizeChanged;
        SessionHeader.SizeChanged += OnLayoutSizeChanged;
        OperationFooter.SizeChanged += OnLayoutSizeChanged;
        AppWindow.Changed += OnWindowChanged;
        Root.RequestedTheme = (ElementTheme)ViewModel.ThemeIndex;
        ViewModel.ThemeChanged += theme => Root.RequestedTheme = (ElementTheme)theme;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.HasHotkeyDraft) && !ViewModel.HasHotkeyDraft && !_closing)
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => HotkeyCaptureButton.Focus(FocusState.Programmatic));
        };

        if (!smokeTest || testIntegration)
        {
            try
            {
                _integration = new(WindowNativeHandle(), strings, ViewModel.Hotkey);
                _integration.Minimized += OnWindowMinimized;
                _integration.ToggleRequested += () => ViewModel.Toggle(() =>
                {
                    if (Root.XamlRoot is { } root)
                        NumericInputCommitter.CommitFocusedEditor(root);
                });
                _integration.StopRequested += () => _ = ViewModel.StopAsync();
                _integration.ShowRequested += () => _integration.Show();
                _integration.ExitRequested += () => _ = CloseAsync();
                ViewModel.Attach(_integration);
            }
            catch (Exception exception)
            {
                ViewModel.ReportIntegrationFailure();
                AppDiagnostics.Write("WindowsIntegrationFailed", exception);
            }
        }
        else
        {
            ViewModel.Attach(new SmokeHotkeys());
        }

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) =>
        {
            ViewModel.Refresh();
            _integration?.UpdateSessionStatus(ViewModel.StatusText);
        };
        _timer.Start();
        AppWindow.Closing += OnClosing;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && ViewModel.IsCapturingHotkey)
                ViewModel.CancelHotkeyCapture();
        };
        if (smokeTest && !Environment.GetCommandLineArgs().Contains("--smoke-preview", StringComparer.Ordinal))
            Root.Loaded += OnSmokeLoaded;
    }

    public MainViewModel ViewModel { get; }

    public Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextVisibility(string value) => ToVisibility(!string.IsNullOrEmpty(value));
    public string CaptureButtonText(string value, bool editing) => editing ? value : _strings.Get("ChangeHotkey");
    public InfoBarSeverity NoticeSeverity(bool isError) => isError ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
    public string NoticeTitle(bool isError) => isError ? _strings.Get("OperationUnavailable") : _strings.Get("Attention");
    public Style? PrimaryButtonStyle(bool emphasize) => emphasize ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        SetTitleBar(TitleBarDragRegion);
        _themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(AppWindow.Id);
        UpdateWindowBorder();
        _themeSettings.Changed += OnThemeSettingsChanged;
    }

    private void OnThemeSettingsChanged(Microsoft.UI.System.ThemeSettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closing)
                UpdateWindowBorder();
        });

    private void UpdateWindowBorder()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        // Hide only DWM's painted outline; keep the native frame for resizing.
        // High contrast retains the system border so the window stays distinguishable.
        const uint borderColorAttribute = 34;
        uint borderColor = _themeSettings?.HighContrast == true ? 0xFFFFFFFF : 0xFFFFFFFE;
        var result = DwmSetWindowAttribute(WindowNativeHandle(), borderColorAttribute, in borderColor, sizeof(uint));
        if (result < 0)
            AppDiagnostics.Write("WindowBorderCustomizationFailed", Marshal.GetExceptionForHR(result));
    }

    private void ConfigureWindowBounds()
    {
        if (_closing || AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        var scale = GetWindowScale();
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var maximumWidth = Math.Max(1, Math.Min((int)(900 * scale), workArea.Width));
        var maximumHeight = Math.Max(1, Math.Min((int)(900 * scale), workArea.Height));
        var minimumWidth = Math.Min((int)(400 * scale), maximumWidth);
        var minimumHeight = Math.Min((int)(600 * scale), maximumHeight);
        presenter.IsMaximizable = false;
        presenter.IsResizable = true;

        if (presenter.PreferredMaximumWidth == maximumWidth && presenter.PreferredMaximumHeight == maximumHeight
            && presenter.PreferredMinimumWidth == minimumWidth && presenter.PreferredMinimumHeight == minimumHeight)
            return;

        // Clear old minima before applying smaller limits on another display.
        presenter.PreferredMinimumWidth = null;
        presenter.PreferredMinimumHeight = null;
        presenter.PreferredMaximumWidth = maximumWidth;
        presenter.PreferredMaximumHeight = maximumHeight;
        presenter.PreferredMinimumWidth = minimumWidth;
        presenter.PreferredMinimumHeight = minimumHeight;

        if (presenter.State != OverlappedPresenterState.Minimized)
        {
            var size = AppWindow.Size;
            var width = Math.Clamp(size.Width, minimumWidth, maximumWidth);
            var height = Math.Clamp(size.Height, minimumHeight, maximumHeight);
            if (width != size.Width || height != size.Height)
                AppWindow.Resize(new SizeInt32(width, height));
        }
    }

    private double GetWindowScale() => Math.Max(96, GetDpiForWindow(WindowNativeHandle())) / 96.0;

    private void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        Root.Loaded -= OnWindowLoaded;
        Root.XamlRoot.Changed += (_, _) => ConfigureWindowBounds();
        ConfigureWindowBounds();
        UpdateSettingsViewport();
        UpdateHotkeyEditorLayout();
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidPresenterChange)
            ConfigureWindowBounds();
    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateSettingsViewport();
        UpdateHotkeyEditorLayout();
    }

    private void UpdateHotkeyEditorLayout()
    {
        if (Root.ActualWidth <= 0 || HotkeyEditorGrid.ActualWidth <= 0)
            return;

        var fontScale = Math.Max(1, CurrentHotkeyValue.FontSize / 14.0);
        var inline = Root.ActualWidth >= 520 * fontScale;
        Grid.SetRow(CurrentHotkeyValue, 0);
        Grid.SetColumn(CurrentHotkeyValue, 0);
        Grid.SetColumnSpan(CurrentHotkeyValue, inline ? 1 : 2);
        Grid.SetRow(HotkeyCaptureButton, inline ? 0 : 1);
        Grid.SetColumn(HotkeyCaptureButton, inline ? 1 : 0);
        HotkeyCaptureButton.HorizontalAlignment = inline ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    private void UpdateSettingsViewport()
    {
        if (_closing || Root.ActualHeight <= 0)
            return;

        var available = Math.Max(0, Root.ActualHeight - AppTitleBar.ActualHeight - Layout.Padding.Top - Layout.Padding.Bottom
            - SessionHeader.ActualHeight - OperationFooter.ActualHeight - OperationFooter.Margin.Top - 2 * Layout.RowSpacing);
        if (Math.Abs(SettingsScroll.MaxHeight - available) > 0.5)
            SettingsScroll.MaxHeight = available;
    }

    private void OnAdvancedExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        _advancedRevealPending = true;
        _ = RevealAdvancedOptionsAsync(++_advancedRevealVersion);
    }

    private async Task RevealAdvancedOptionsAsync(int version)
    {
        try
        {
            var deadline = Environment.TickCount64 + 2000;
            long? stableSince = null;
            var previousY = double.NaN;
            var previousOffset = double.NaN;
            var previousExtent = double.NaN;
            while (Environment.TickCount64 < deadline)
            {
                await Task.Delay(40);
                if (_closing || version != _advancedRevealVersion || !AdvancedOptions.IsExpanded)
                    return;

                Root.UpdateLayout();
                if (InitialDelayToggle.ActualHeight <= 0 || SettingsScroll.ActualHeight <= 0)
                    continue;

                var position = InitialDelayToggle.TransformToVisual(SettingsScroll).TransformPoint(default);
                var offset = SettingsScroll.VerticalOffset;
                var extent = SettingsScroll.ExtentHeight;
                var visible = IsInsideViewport(InitialDelayToggle, SettingsScroll);
                // Expansion and scroll anchoring can update across several frames.
                if (visible && Math.Abs(position.Y - previousY) < 0.5
                    && Math.Abs(offset - previousOffset) < 0.5 && Math.Abs(extent - previousExtent) < 0.5)
                {
                    stableSince ??= Environment.TickCount64;
                    if (Environment.TickCount64 - stableSince.Value >= 200)
                        return;
                }
                else
                {
                    stableSince = null;
                }

                if (!visible)
                {
                    var target = offset + position.Y + InitialDelayToggle.ActualHeight / 2
                        - SettingsScroll.ActualHeight / 2;
                    SettingsScroll.ChangeView(null, Math.Clamp(target, 0, SettingsScroll.ScrollableHeight), null, true);
                }
                previousY = position.Y;
                previousOffset = offset;
                previousExtent = extent;
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("AdvancedOptionsRevealFailed", exception);
        }
        finally
        {
            if (version == _advancedRevealVersion)
                _advancedRevealPending = false;
        }
    }

    private void OnReviewValidation(object sender, RoutedEventArgs args)
    {
        Control target = !string.IsNullOrEmpty(ViewModel.SpeedError) ? SpeedInput
            : !string.IsNullOrEmpty(ViewModel.MouseError) ? MouseInput
            : !string.IsNullOrEmpty(ViewModel.DelayError) ? DelayInput
            : !string.IsNullOrEmpty(ViewModel.ClickLimitError) ? ClickLimitInput
            : !string.IsNullOrEmpty(ViewModel.DurationLimitError) ? DurationInput : VariationInput;
        if (ViewModel.HasAdvancedErrors && target != SpeedInput && target != MouseInput)
            AdvancedOptions.IsExpanded = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            target.StartBringIntoView();
            target.Focus(FocusState.Programmatic);
        });
    }

    private nint WindowNativeHandle() => WinRT.Interop.WindowNative.GetWindowHandle(this);

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(nint window, uint attribute, in uint value, uint size);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SendMessage(nint window, uint message, nuint parameter, nint detail);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(nint window);

    private static HotkeyModifiers ReadHotkeyModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsKeyDown(VirtualKey.Control)) modifiers |= HotkeyModifiers.Control;
        if (IsKeyDown(VirtualKey.Menu)) modifiers |= HotkeyModifiers.Alt;
        if (IsKeyDown(VirtualKey.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows)) modifiers |= HotkeyModifiers.Windows;
        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_closing || args.OriginalSource is not DependencyObject source)
            return;
        foreach (var input in new[] { SpeedInput, DelayInput, ClickLimitInput, DurationInput, VariationInput })
            if (!input.ContainsInteractionSource(source))
                input.DismissSpinPopup();
    }

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (ViewModel.HasHotkeyDraft && args.OriginalKey == VirtualKey.Escape && ReadHotkeyModifiers() == HotkeyModifiers.None)
        {
            args.Handled = true;
            _capturedHotkeyKey = (uint)VirtualKey.Escape;
            _hotkeyActivationPending = false;
            ViewModel.CancelHotkeyCapture();
            HotkeyCaptureButton.Focus(FocusState.Programmatic);
        }
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!ViewModel.IsCapturingHotkey)
        {
            if (args.OriginalKey == VirtualKey.Enter)
            {
                _capturedHotkeyKey = (uint)args.OriginalKey;
                _hotkeyActivationPending = true;
            }
            return;
        }
        args.Handled = true;
        if (args.KeyStatus.WasKeyDown)
            return;
        CaptureHotkeyKey((uint)args.OriginalKey, ReadHotkeyModifiers());
    }

    private void OnHotkeyPreviewKeyUp(object sender, KeyRoutedEventArgs args)
    {
        var key = (uint)args.OriginalKey;
        if (_capturedHotkeyKey == key)
        {
            args.Handled = !_hotkeyActivationPending || ViewModel.IsCapturingHotkey;
            _capturedHotkeyKey = null;
            _hotkeyActivationPending = false;
        }
        else if (ViewModel.IsCapturingHotkey && !HotkeyGesture.IsModifierKey(key))
        {
            args.Handled = true;
            CaptureHotkeyKey(key, ReadHotkeyModifiers());
        }
    }

    private void CaptureHotkeyKey(uint key, HotkeyModifiers modifiers)
    {
        if (key == (uint)VirtualKey.Escape && modifiers == HotkeyModifiers.None)
            ViewModel.CancelHotkeyCapture();
        else
            ViewModel.CaptureHotkey(new(key, modifiers));
        if (!HotkeyGesture.IsModifierKey(key))
        {
            _capturedHotkeyKey = key;
            _hotkeyActivationPending = false;
        }
    }

    private void OnHotkeyCaptureLostFocus(object sender, RoutedEventArgs args)
    {
        if (ViewModel.IsCapturingHotkey)
            ViewModel.CancelHotkeyCapture();
    }

    private void OnWindowMinimized()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = MinimizeWindowToTrayAsync());
    }

    private async Task MinimizeWindowToTrayAsync()
    {
        if (_closing || !ViewModel.MinimizeToTray)
            return;

        try
        {
            while (true)
            {
                var preferenceSave = ViewModel.MinimizeToTraySaveTask;
                await preferenceSave;
                if (ReferenceEquals(preferenceSave, ViewModel.MinimizeToTraySaveTask))
                    break;
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("MinimizeToTrayPreferenceWaitFailed", exception);
            return;
        }

        if (_closing || !ViewModel.MinimizeToTray
            || AppWindow.Presenter is not OverlappedPresenter presenter
            || presenter.State != OverlappedPresenterState.Minimized)
            return;

        PreferencesButton.Flyout?.Hide();
        try
        {
            var hidden = _integration?.Hide() == true;
            ViewModel.ReportTrayResult(hidden);
            if (!hidden && presenter.State == OverlappedPresenterState.Minimized)
                presenter.Restore();
        }
        catch (Exception exception)
        {
            ViewModel.ReportTrayResult(false);
            AppDiagnostics.Write("MinimizeToTrayFailed", exception);
            if (presenter.State == OverlappedPresenterState.Minimized)
                presenter.Restore();
        }
        ViewModel.Refresh();
    }

    private void OnTitleBarMinimize(object sender, RoutedEventArgs args)
    {
        if (!_closing && AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Minimize();
    }

    private void OnTitleBarClose(object sender, RoutedEventArgs args) => _ = CloseAsync();

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
            return;

        args.Cancel = true;
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (_closing)
            return;

        _closing = true;
        if (_themeSettings is not null)
            _themeSettings.Changed -= OnThemeSettingsChanged;
        PreferencesButton.Focus(FocusState.Programmatic);
        ViewModel.BeginShutdown();
        try
        {
            await ViewModel.StopAsync();
            await ViewModel.SaveAsync();
            try
            {
                await _controller.DisposeAsync();
            }
            catch (Exception exception)
            {
                AppDiagnostics.Write("FinalInputCleanupFailed", exception);
            }
        }
        finally
        {
            _timer.Stop();
            _integration?.Dispose();
            _closeConfirmed = true;
            if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Close))
                Close();
        }
    }

    private async void OnSmokeLoaded(object sender, RoutedEventArgs args)
    {
        if (!_smokeTest)
            return;

        Root.Loaded -= OnSmokeLoaded;
        try
        {
            var reportPath = Environment.GetEnvironmentVariable("RM_AUTO_CLICKER_SMOKE_REPORT");
            if (string.IsNullOrWhiteSpace(reportPath))
                throw new InvalidOperationException("Set RM_AUTO_CLICKER_SMOKE_REPORT to save the smoke report.");

            await Task.Delay(150);
            if (ViewModel.HasValidationError || !_controller.Completion.IsCompleted)
                throw new InvalidOperationException("Smoke safety verification requires a valid, idle form.");
            var startEnabled = ViewModel.StartCommand.CanExecute(null);
            var startButtonEnabled = StartButton.IsEnabled;
            ViewModel.Toggle();
            await _controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            var smokeSafetyVerified = !startEnabled && !startButtonEnabled && _controller.Snapshot.SessionId == Guid.Empty
                && _smokeEmitter is { Attempts: 0 };
            var restartTest = Environment.GetCommandLineArgs().Contains("--smoke-restart", StringComparer.Ordinal);
            var restarted = Environment.GetCommandLineArgs().Contains("--smoke-restarted", StringComparer.Ordinal);
            if (restartTest && !restarted)
            {
                var targetLanguage = Environment.GetEnvironmentVariable("RM_AUTO_CLICKER_SMOKE_LANGUAGE");
                if (targetLanguage is null or "auto")
                    targetLanguage = "en-US";
                ViewModel.LanguageIndex = AppStrings.Preferences.ToList().IndexOf(targetLanguage);
                await ViewModel.LanguageSaveTask;
                PreferencesButton.Flyout.ShowAt(PreferencesButton);
                await Task.Delay(150);
                Root.UpdateLayout();
                if (!LanguageRestartNotice.IsOpen || !RestartAppButton.IsEnabled || RestartAppButton.ActualHeight <= 0
                    || LanguageRestartNotice.Message != ViewModel.LanguageRestartMessage
                    || (string)RestartAppButton.Content != _strings.Get("RestartApp.Content"))
                    throw new InvalidOperationException("The language restart notice or action is not available inside Preferences.");
                await File.WriteAllTextAsync(reportPath + ".restart-request.json", JsonSerializer.Serialize(new
                {
                    ProcessId = Environment.ProcessId,
                    Language = targetLanguage,
                    NoticeVerified = true
                }));
                await ViewModel.RestartCommand.ExecuteAsync(null);
                throw new InvalidOperationException("The native restart returned without relaunching the application: " + ViewModel.RestartError);
            }
            FrameworkElement initialLastContent = AdvancedOptions;
            var initialContentBottom = initialLastContent.TransformToVisual(Root).TransformPoint(default).Y + initialLastContent.ActualHeight;
            var initialUnusedSpace = Math.Max(0, OperationFooter.TransformToVisual(Root).TransformPoint(default).Y - initialContentBottom);
            var initialActionsVisible = AreSecondaryActionsVisible();
            var numericInputEditingVerified = VerifyNumericInputEditing();
            var uncommittedNumericInputVerified = VerifyUncommittedNumericInput();
            var numericPopupDismissalVerified = await VerifyNumericPopupDismissalAsync();
            var windowSizingVerified = await VerifyWindowSizingAsync();
            var advancedRevealVerified = await VerifyAdvancedRevealAsync();
            var responsiveLayoutVerified = await VerifyResponsiveLayoutAsync();
            var nativeModules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(module => module.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)
                    || module.ModuleName.Equals("Microsoft.UI.Xaml.dll", StringComparison.OrdinalIgnoreCase))
                .Select(module => new { module.ModuleName, module.FileName }).ToArray();
            var report = new
            {
                Success = WindowNativeHandle() != 0 && Root.ActualWidth > 0,
                ProcessId = Environment.ProcessId,
                Restarted = restarted,
                RestartRequired = ViewModel.LanguageChangePending,
                InitialCompositionVerified = initialUnusedSpace <= 16,
                WindowSizingVerified = windowSizingVerified,
                WindowSizingCheck = _windowSizingCheck,
                InitialUnusedSpace = initialUnusedSpace,
                InitialActionsVisible = initialActionsVisible,
                AdvancedRevealVerified = advancedRevealVerified,
                AdvancedRevealCheck = _advancedRevealCheck,
                WindowTitleBarVerified = AreTitleBarActionsAccessible(),
                Language = _strings.Language,
                LocalizedStartLabel = StartButton.Content,
                ProductName = ProductNameText.Text,
                WindowTitle = Title,
                ApplicationIconPresent = File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico")),
                WindowIconPresent = SendMessage(WindowNativeHandle(), 0x007F, 0, 0) != 0
                    && SendMessage(WindowNativeHandle(), 0x007F, 1, 0) != 0,
                LanguageResources = new[] { "pt-BR", "en-US", "es" }.Select(language => new
                {
                    Language = language,
                    Ready = MrtAppStrings.Create(language).Get("Ready"),
                    Start = MrtAppStrings.Create(language).Get("Start.Content")
                }).ToArray(),
                Runtime = Environment.Version.ToString(),
                RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(),
                CoreLibrary = typeof(object).Assembly.Location,
                BaseDirectory = AppContext.BaseDirectory,
                NativeModules = nativeModules,
                ClicksSent = _controller.Snapshot.SentClicks,
                AutoClickingEnabled = startEnabled,
                StartButtonEnabled = startButtonEnabled,
                SmokeSafetyVerified = smokeSafetyVerified,
                InputAttempts = _smokeEmitter!.Attempts,
                HotkeysReady = _integration?.HotkeysReady,
                HotkeyStatus = _integration?.Status,
                AdvancedOptionsCollapsed = !AdvancedOptions.IsExpanded,
                CountdownDisabledByDefault = !ViewModel.HasInitialDelay && InitialDelayToggle.IsChecked == false,
                MinimizeToTrayDisabledByDefault = !ViewModel.MinimizeToTray,
                MetricsBelowStatus = SessionMetrics.TransformToVisual(Root).TransformPoint(default).Y
                    >= SessionStatus.TransformToVisual(Root).TransformPoint(default).Y + SessionStatus.ActualHeight,
                PersistentOperationLayout = Grid.GetRow(SettingsScroll) == 1 && Grid.GetRow(OperationFooter) == 2
                    && Grid.GetRow(SessionHeader) == 0 && OperationFooter.Parent == SettingsScroll.Parent,
                HotkeyActionsHiddenByDefault = HotkeyDraftActions.Visibility == Visibility.Collapsed,
                ResponsiveLayoutVerified = responsiveLayoutVerified,
                ResponsiveChecks = _responsiveChecks,
                NumericInputEditingVerified = numericInputEditingVerified,
                UncommittedNumericInputVerified = uncommittedNumericInputVerified,
                NumericPopupDismissalVerified = numericPopupDismissalVerified,
                NumericPopupDismissalCheck = _numericPopupDismissalCheck
            };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("SmokeTestFailed", exception);
        }
        finally
        {
            await CloseAsync();
        }
    }

    private async Task<bool> VerifyWindowSizingAsync()
    {
        var originalSize = AppWindow.Size;
        try
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter
                || presenter.PreferredMaximumWidth is not { } maximumWidth
                || presenter.PreferredMaximumHeight is not { } maximumHeight)
                return false;

            AdvancedOptions.IsExpanded = false;
            AppWindow.Resize(new SizeInt32(maximumWidth + 200, maximumHeight + 200));
            await Task.Delay(180);
            Root.UpdateLayout();
            var boundedSize = AppWindow.Size;
            FrameworkElement lastContent = AdvancedOptions;
            var contentBottom = lastContent.TransformToVisual(Root).TransformPoint(default).Y + lastContent.ActualHeight;
            var collapsedFooter = OperationFooter.TransformToVisual(Root).TransformPoint(default).Y;
            var collapsedGap = collapsedFooter - contentBottom;
            var collapsedActionsVisible = AreSecondaryActionsVisible();

            AdvancedOptions.IsExpanded = true;
            await Task.Delay(350);
            Root.UpdateLayout();
            var expandedActionsVisible = AreSecondaryActionsVisible()
                && OperationFooter.TransformToVisual(Root).TransformPoint(default).Y
                    + OperationFooter.ActualHeight <= Root.ActualHeight + 1;

            AdvancedOptions.IsExpanded = false;
            await Task.Delay(350);
            Root.UpdateLayout();
            var restoredFooter = OperationFooter.TransformToVisual(Root).TransformPoint(default).Y;
            _windowSizingCheck = new { MaximumWidth = maximumWidth, MaximumHeight = maximumHeight,
                RequestedWidth = maximumWidth + 200, RequestedHeight = maximumHeight + 200,
                ActualWidth = boundedSize.Width, ActualHeight = boundedSize.Height,
                IsMaximizable = presenter.IsMaximizable, IsResizable = presenter.IsResizable,
                CollapsedGap = collapsedGap, CollapsedActionsVisible = collapsedActionsVisible,
                ExpandedActionsVisible = expandedActionsVisible, RestoredFooterDelta = Math.Abs(restoredFooter - collapsedFooter) };

            return !presenter.IsMaximizable && presenter.IsResizable
                && boundedSize.Width <= maximumWidth && boundedSize.Height <= maximumHeight
                && collapsedGap is >= 0 and <= 16 && Math.Abs(restoredFooter - collapsedFooter) <= 1
                && collapsedActionsVisible && expandedActionsVisible && AppWindow.Size.Equals(boundedSize);
        }
        finally
        {
            AdvancedOptions.IsExpanded = false;
            SettingsScroll.ChangeView(null, 0, null, true);
            AppWindow.Resize(originalSize);
            await Task.Delay(150);
            Root.UpdateLayout();
        }
    }

    private async Task<bool> VerifyResponsiveLayoutAsync()
    {
        var originalSize = AppWindow.Size;
        var scale = Root.XamlRoot.RasterizationScale;
        var originalSpeed = ViewModel.ClicksPerSecond;
        var originalMessage = ViewModel.Message;
        var fonts = new List<(DependencyObject Element, double Size)>();
        try
        {
            ViewModel.Message = _strings.Get("SmokeLongNotice");
            ViewModel.ClicksPerSecond = double.NaN;
            AdvancedOptions.IsExpanded = true;
            Root.UpdateLayout();
            CollectFonts(Root, fonts);
            foreach (var fontScale in new[] { 1.0, 1.5, 2.0 })
            {
                ApplyFonts(fonts, fontScale);
                foreach (var height in new[] { 600, 660, 740 })
                foreach (var width in new[] { 400, 640, 800 })
                foreach (var hasErrors in new[] { false, true })
                {
                    ViewModel.ClicksPerSecond = hasErrors ? double.NaN : originalSpeed;
                    ViewModel.Message = hasErrors ? _strings.Get("SmokeLongNotice") : string.Empty;
                    AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
                    await Task.Delay(120);
                    Root.UpdateLayout();
                    var headerPosition = SessionHeader.TransformToVisual(Root).TransformPoint(default).Y;
                    var footerPosition = OperationFooter.TransformToVisual(Root).TransformPoint(default).Y;
                    var footerBottom = footerPosition + OperationFooter.ActualHeight;
                    var actionsFit = IsInsideRootHorizontally(StartButton) && IsInsideRootHorizontally(StopButton)
                        && IsInsideRootHorizontally(PreferencesButton) && AreSecondaryActionsVisible();
                    var titleBarActionsFit = AreTitleBarActionsAccessible();
                    var hotkeyControlsFit = IsInsideRootHorizontally(CurrentHotkeyValue)
                        && IsInsideRootHorizontally(HotkeyCaptureButton);
                    var hotkeyDraftFits = true;
                    if (!hasErrors)
                    {
                        try
                        {
                            ViewModel.BeginHotkeyCaptureCommand.Execute(null);
                            ViewModel.CaptureHotkey(new HotkeyGesture(0x6B,
                                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));
                            Root.UpdateLayout();
                            hotkeyDraftFits = ViewModel.HasHotkeyDraft
                                && IsInsideRootHorizontally(CurrentHotkeyValue)
                                && IsInsideRootHorizontally(HotkeyCaptureButton)
                                && IsInsideRootHorizontally(HotkeyDraftActions);
                        }
                        finally
                        {
                            ViewModel.CancelHotkeyCapture();
                            Root.UpdateLayout();
                        }
                    }
                    _responsiveChecks.Add(new { Width = width, Height = height, FontScale = fontScale, HasErrors = hasErrors,
                        RootWidth = Root.ActualWidth, RootHeight = Root.ActualHeight, MouseColumn = Grid.GetColumn(MouseInput),
                        HeaderHeight = SessionHeader.ActualHeight, ProductNameHeight = ProductNameText.ActualHeight,
                        SpeedWidth = SpeedInput.ActualWidth, SettingsHeight = SettingsScroll.ActualHeight, FooterBottom = footerBottom,
                        SecondaryActionsVisible = AreSecondaryActionsVisible(), ActionsFit = actionsFit,
                        WindowTitleBarActionsFit = titleBarActionsFit, HotkeyControlsFit = hotkeyControlsFit, HotkeyDraftFits = hotkeyDraftFits });
                    if (Grid.GetColumn(MouseInput) != (Root.ActualWidth >= 640 ? 1 : 0) || SpeedInput.ActualWidth < 100
                        || SettingsScroll.ActualHeight < 96 || footerBottom > Root.ActualHeight + 1 || !actionsFit
                        || !titleBarActionsFit || !hotkeyControlsFit || !hotkeyDraftFits
                        || SecondaryActions.Visibility != Visibility.Visible)
                        return false;
                    SettingsScroll.ChangeView(null, SettingsScroll.ScrollableHeight, null, true);
                    await Task.Delay(40);
                    if (SessionHeader.TransformToVisual(Root).TransformPoint(default).Y != headerPosition
                        || OperationFooter.TransformToVisual(Root).TransformPoint(default).Y != footerPosition)
                        return false;
                    SettingsScroll.ChangeView(null, 0, null, true);
                }
            }
            return true;
        }
        finally
        {
            ApplyFonts(fonts, 1);
            ViewModel.ClicksPerSecond = originalSpeed;
            ViewModel.Message = originalMessage;
            AdvancedOptions.IsExpanded = false;
            AppWindow.Resize(originalSize);
            SettingsScroll.ChangeView(null, 0, null, true);
        }
    }

    private bool IsInsideRootHorizontally(FrameworkElement element)
    {
        var position = element.TransformToVisual(Root).TransformPoint(default).X;
        return position >= 0 && position + element.ActualWidth <= Root.ActualWidth + 1;
    }

    private bool AreTitleBarActionsAccessible()
    {
        Root.UpdateLayout();
        var minimize = WindowMinimizeButton.TransformToVisual(AppTitleBar).TransformPoint(default);
        var close = WindowCloseButton.TransformToVisual(AppTitleBar).TransformPoint(default);
        var productName = TitleBarProductName.TransformToVisual(AppTitleBar).TransformPoint(default);
        return AppWindow.Presenter is OverlappedPresenter { HasBorder: true, HasTitleBar: false, IsResizable: true, IsMaximizable: false }
            && AppTitleBar.ActualHeight >= 32
            && !string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(WindowMinimizeButton))
            && !string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(WindowCloseButton))
            && WindowMinimizeButton.ActualHeight >= 32 && WindowCloseButton.ActualHeight >= 32
            && Math.Abs(minimize.Y - close.Y) <= 1
            && Math.Abs(minimize.X + WindowMinimizeButton.ActualWidth - close.X) <= 1
            && close.X + WindowCloseButton.ActualWidth <= AppTitleBar.ActualWidth + 1
            && productName.X + TitleBarProductName.ActualWidth <= minimize.X + 1;
    }

    private bool AreSecondaryActionsVisible() => SecondaryActions.Visibility == Visibility.Visible
        && IsInsideViewport(SavePreferencesButton, Root)
        && (SaveFeedbackText.Visibility != Visibility.Visible || IsInsideViewport(SaveFeedbackText, Root));

    private static bool IsInsideViewport(FrameworkElement element, FrameworkElement viewport)
    {
        var position = element.TransformToVisual(viewport).TransformPoint(default);
        return element.Visibility == Visibility.Visible && element.ActualWidth > 0 && element.ActualHeight > 0
            && position.X >= -1 && position.Y >= -1
            && position.X + element.ActualWidth <= viewport.ActualWidth + 1
            && position.Y + element.ActualHeight <= viewport.ActualHeight + 1;
    }

    private async Task<bool> VerifyAdvancedRevealAsync()
    {
        Root.UpdateLayout();
        var originalSize = AppWindow.Size;
        var headerPosition = SessionHeader.TransformToVisual(Root).TransformPoint(default).Y;
        try
        {
            PreferencesButton.Focus(FocusState.Programmatic);
            SettingsScroll.ChangeView(null, 0, null, true);
            AdvancedOptions.IsExpanded = true;
            var deadline = Environment.TickCount64 + 3000;
            long? visibleSince = null;
            while (Environment.TickCount64 < deadline)
            {
                await Task.Delay(40);
                Root.UpdateLayout();
                if (!_advancedRevealPending && IsInsideViewport(InitialDelayToggle, SettingsScroll))
                {
                    visibleSince ??= Environment.TickCount64;
                    if (Environment.TickCount64 - visibleSince.Value >= 150)
                        break;
                }
                else
                {
                    visibleSince = null;
                }
            }
            var toggleVisible = visibleSince is { } since && Environment.TickCount64 - since >= 150
                && IsInsideViewport(InitialDelayToggle, SettingsScroll);
            var sizeUnchanged = AppWindow.Size.Equals(originalSize);
            var headerStable = SessionHeader.TransformToVisual(Root).TransformPoint(default).Y == headerPosition;
            var footerGap = OperationFooter.TransformToVisual(Root).TransformPoint(default).Y
                - SettingsScroll.TransformToVisual(Root).TransformPoint(default).Y - SettingsScroll.ActualHeight;
            var footerFollowsContent = footerGap >= 0 && footerGap <= Layout.RowSpacing + OperationFooter.Margin.Top + 1;
            _advancedRevealCheck = new { ToggleVisible = toggleVisible, Offset = SettingsScroll.VerticalOffset,
                ToggleY = InitialDelayToggle.TransformToVisual(SettingsScroll).TransformPoint(default).Y,
                ToggleHeight = InitialDelayToggle.ActualHeight, SettingsHeight = SettingsScroll.ActualHeight,
                RevealPending = _advancedRevealPending, ScrollableHeight = SettingsScroll.ScrollableHeight,
                SizeUnchanged = sizeUnchanged, HeaderStable = headerStable, FooterFollowsContent = footerFollowsContent, FooterGap = footerGap };
            return toggleVisible && SettingsScroll.VerticalOffset > 0 && AreSecondaryActionsVisible()
                && sizeUnchanged && headerStable && footerFollowsContent;
        }
        finally
        {
            AdvancedOptions.IsExpanded = false;
            SettingsScroll.ChangeView(null, 0, null, true);
        }
    }

    private static void CollectFonts(DependencyObject element, List<(DependencyObject Element, double Size)> fonts)
    {
        if (element is Control control)
            fonts.Add((control, control.FontSize));
        else if (element is TextBlock text)
            fonts.Add((text, text.FontSize));
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(element); childIndex++)
            CollectFonts(VisualTreeHelper.GetChild(element, childIndex), fonts);
    }

    private static void ApplyFonts(List<(DependencyObject Element, double Size)> fonts, double scale)
    {
        foreach (var (element, size) in fonts)
        {
            if (element is Control control)
                control.FontSize = size * scale;
            else if (element is TextBlock text)
                text.FontSize = size * scale;
        }
    }

    private bool VerifyUncommittedNumericInput()
    {
        var originalSpeed = ViewModel.ClicksPerSecond;
        try
        {
            SpeedInput.Value = 23;
            if (!SpeedInput.Focus(FocusState.Programmatic)
                || FocusManager.GetFocusedElement(Root.XamlRoot) is not TextBox editor)
                return false;

            editor.Text = "1";
            NumericInputCommitter.CommitFocusedEditor(Root.XamlRoot);
            var typedValueCommitted = ViewModel.ClicksPerSecond == 1 && SpeedInput.Value == 1;
            editor.Text = string.Empty;
            NumericInputCommitter.CommitFocusedEditor(Root.XamlRoot);
            var emptyIsInvalid = double.IsNaN(ViewModel.ClicksPerSecond) && !string.IsNullOrEmpty(ViewModel.ValidationMessage);
            editor.Text = (12.5).ToString(CultureInfo.CurrentCulture);
            NumericInputCommitter.CommitFocusedEditor(Root.XamlRoot);
            var regionalDecimalCommitted = ViewModel.ClicksPerSecond == 12.5 && SpeedInput.Value == 12.5;
            editor.Text = "12";
            NumericInputCommitter.CommitFocusedEditor(Root.XamlRoot);
            return typedValueCommitted && emptyIsInvalid && regionalDecimalCommitted && ViewModel.ClicksPerSecond == 12;
        }
        finally
        {
            SpeedInput.Value = originalSpeed;
        }
    }

    private async Task<bool> VerifyNumericPopupDismissalAsync()
    {
        var originalSpeed = ViewModel.ClicksPerSecond;
        try
        {
            PreferencesButton.Focus(FocusState.Programmatic);
            await Task.Delay(50);
            SpeedInput.Value = 12;
            if (!SpeedInput.Focus(FocusState.Programmatic))
                return false;
            await Task.Delay(50);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot)
                .SingleOrDefault(popup => popup.Child is FrameworkElement { Name: "PopupContentRoot" });
            if (popup?.Child is null || FocusManager.GetFocusedElement(Root.XamlRoot) is not TextBox editor)
                return false;
            editor.Text = "18";
            var insideKeepsOpen = SpeedInput.ContainsInteractionSource(SpeedInput) && popup.IsOpen;
            var spinButtonsKeepOpen = SpeedInput.ContainsInteractionSource(popup.Child) && popup.IsOpen;
            SpeedInput.DismissSpinPopup();
            await Task.Delay(50);
            var outsideClosesAndCommits = !popup.IsOpen && SpeedInput.Value == 18 && ViewModel.ClicksPerSecond == 18;
            SpeedInput.OpenSpinPopup();
            await Task.Delay(50);
            var canReopen = popup.IsOpen;
            if (FocusManager.GetFocusedElement(Root.XamlRoot) is not TextBox reopenedEditor)
                return false;
            reopenedEditor.Text = string.Empty;
            SpeedInput.DismissSpinPopup();
            await Task.Delay(50);
            var emptyIsSafe = !popup.IsOpen && double.IsNaN(ViewModel.ClicksPerSecond)
                && SpeedSlider.Value == 18 && !string.IsNullOrEmpty(ViewModel.SpeedError);
            _numericPopupDismissalCheck = new { InsideKeepsOpen = insideKeepsOpen, SpinButtonsKeepOpen = spinButtonsKeepOpen,
                OutsideClosesAndCommits = outsideClosesAndCommits, CanReopen = canReopen, EmptyIsSafe = emptyIsSafe,
                FocusedElement = (FocusManager.GetFocusedElement(Root.XamlRoot) as FrameworkElement)?.Name,
                PopupOpen = popup.IsOpen, Speed = double.IsNaN(SpeedInput.Value) ? "NaN" : SpeedInput.Value.ToString(CultureInfo.InvariantCulture) };
            return insideKeepsOpen && spinButtonsKeepOpen && outsideClosesAndCommits && canReopen && emptyIsSafe;
        }
        finally
        {
            PreferencesButton.Focus(FocusState.Programmatic);
            SpeedInput.Value = originalSpeed;
        }
    }

    private bool VerifyNumericInputEditing()
    {
        var originalSpeed = ViewModel.ClicksPerSecond;
        try
        {
            SpeedInput.Value = 23;
            SpeedInput.Value = double.NaN;
            var emptyIsSafe = double.IsNaN(ViewModel.ClicksPerSecond) && SpeedSlider.Value == 23
                && !string.IsNullOrEmpty(ViewModel.ValidationMessage) && !ViewModel.SaveCommand.CanExecute(null);
            SpeedSlider.Value = 14;
            var sliderRestoresEditor = SpeedInput.Value == 14 && ViewModel.ClicksPerSecond == 14;
            SpeedInput.Value = 12;
            return emptyIsSafe && sliderRestoresEditor && SpeedSlider.Value == 12
                && string.IsNullOrEmpty(ViewModel.ValidationMessage);
        }
        finally
        {
            SpeedInput.Value = originalSpeed;
        }
    }
}
