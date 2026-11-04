using System.ComponentModel;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Platform.Windows.Hotkeys;

public sealed class WindowIntegration : IHotkeyService, IDisposable
{
    private const uint HotkeyMessage = 0x0312;
    private const uint TrayMessage = 0x8001;
    private const uint NoRepeat = 0x4000;
    private const int EmergencyId = 100;
    private readonly nint _window;
    private readonly IWindowIntegrationApi _api;
    private readonly IAppStrings _strings;
    private readonly WindowSubclassProcedure _procedure;
    private readonly uint _taskbarCreated;
    private int _toggleId = 1;
    private bool _subclassRegistered;
    private bool _toggleRegistered;
    private bool _emergencyRegistered;
    private bool _sessionRegistered;
    private bool _trayAvailable;
    private bool _hidden;
    private bool _windowMinimized;
    private bool _callbackFaulted;
    private bool _hotkeyStatusWarning;
    private HotkeyGesture _toggleHotkey;
    private int _sessionError;
    private string _hotkeyStatus = string.Empty;
    private string _sessionStatus;
    private bool _disposed;

    public WindowIntegration(nint window, IAppStrings strings, HotkeyGesture? hotkey = null)
        : this(window, new WindowsWindowIntegrationApi(), strings, hotkey)
    {
    }

    internal WindowIntegration(nint window, IWindowIntegrationApi api, IAppStrings strings, HotkeyGesture? hotkey = null)
    {
        hotkey ??= HotkeyGesture.Default;
        hotkey.Validate();

        _window = window;
        _api = api;
        _strings = strings;
        _sessionStatus = strings.Get("Stopped");
        _toggleHotkey = hotkey;
        _procedure = HandleMessage;
        if (!_api.Attach(window, _procedure))
            throw new Win32Exception(_api.LastError, "Could not attach the window message handler.");

        _subclassRegistered = true;
        try
        {
            _emergencyRegistered = _api.RegisterHotkey(window, EmergencyId, NoRepeat | 0x0002 | 0x0004, 0x79);
            _taskbarCreated = _api.RegisterTaskbarMessage();
            _sessionRegistered = _api.RegisterSessionNotifications(window);
            if (!_sessionRegistered)
                _sessionError = _api.LastError;
            TryChangeHotkey(hotkey, out _);
            _trayAvailable = _api.AddTrayIcon(window, TrayTip);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event Action? ToggleRequested;
    public event Action? StopRequested;
    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? Minimized;

    public bool HotkeysReady => !_disposed && _toggleRegistered && _emergencyRegistered && _sessionRegistered && !_callbackFaulted;
    public bool EmergencyReady => !_disposed && _emergencyRegistered;
    public bool HasStatusWarning => _disposed || _callbackFaulted || !_sessionRegistered || !_trayAvailable || !_emergencyRegistered || _hotkeyStatusWarning;
    public string Status => _disposed ? _strings.Get("IntegrationClosed")
        : _callbackFaulted ? _strings.Get("IntegrationFaulted")
        : !_sessionRegistered ? _strings.Get("SessionProtectionUnavailable", _sessionError)
        : !_trayAvailable ? _strings.Get("TrayStatusUnavailable", _hotkeyStatus)
        : _hotkeyStatus;

    private string TrayTip => _strings.Get("TrayTip", _sessionStatus, GetDisplayName(_toggleHotkey));

    public void UpdateSessionStatus(string status)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sessionStatus == status)
            return;
        _sessionStatus = status;
        if (_trayAvailable)
        {
            _trayAvailable = _api.UpdateTrayIcon(TrayTip);
            RestoreIfTrayUnavailable();
        }
    }

    public string GetDisplayName(HotkeyGesture hotkey) => WindowsHotkeyFormatter.Format(hotkey);

    public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(hotkey);
        hotkey.Validate();

        if (_toggleRegistered && _toggleHotkey == hotkey)
        {
            error = _emergencyRegistered ? null : _strings.Get("EmergencyConflict");
            _hotkeyStatus = error ?? _strings.Get("HotkeyAvailable", GetDisplayName(hotkey));
            _hotkeyStatusWarning = error is not null;
            return true;
        }

        var candidateId = _toggleId == 1 ? 2 : 1;
        if (!_api.RegisterHotkey(_window, candidateId, NoRepeat | (uint)hotkey.Modifiers, hotkey.VirtualKey))
        {
            error = _strings.Get("HotkeyConflict", GetDisplayName(hotkey), _api.LastError);
            _hotkeyStatus = error;
            _hotkeyStatusWarning = true;
            return false;
        }

        if (_toggleRegistered && !_api.UnregisterHotkey(_window, _toggleId))
        {
            _api.UnregisterHotkey(_window, candidateId);
            error = _strings.Get("HotkeyReleaseFailed");
            _hotkeyStatus = error;
            _hotkeyStatusWarning = true;
            return false;
        }

        _toggleId = candidateId;
        _toggleHotkey = hotkey;
        _toggleRegistered = true;
        error = _emergencyRegistered ? null : _strings.Get("EmergencyConflict");
        _hotkeyStatus = error ?? _strings.Get("HotkeyAvailable", GetDisplayName(hotkey));
        _hotkeyStatusWarning = error is not null;
        if (_trayAvailable)
        {
            _trayAvailable = _api.UpdateTrayIcon(TrayTip);
            RestoreIfTrayUnavailable();
        }
        return true;
    }

    public bool Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_trayAvailable)
            _trayAvailable = _api.AddTrayIcon(_window, TrayTip);
        if (!_trayAvailable)
        {
            Show();
            return false;
        }

        _api.SetVisible(_window, false);
        _hidden = true;
        return true;
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _api.SetVisible(_window, true);
        _hidden = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_toggleRegistered)
            _api.UnregisterHotkey(_window, _toggleId);
        if (_emergencyRegistered)
            _api.UnregisterHotkey(_window, EmergencyId);
        if (_sessionRegistered)
            _api.UnregisterSessionNotifications(_window);
        if (_trayAvailable)
            _api.DeleteTrayIcon();
        if (_subclassRegistered)
            _api.Detach(_window, _procedure);
        GC.KeepAlive(_procedure);
    }

    private void RestoreIfTrayUnavailable()
    {
        if (!_trayAvailable && _hidden)
            Show();
    }

    private nint HandleMessage(nint window, uint message, nuint parameter, nint detail, nuint subclassId, nuint referenceData)
    {
        try
        {
            if (!_disposed)
            {
                if (message == 0x0005)
                {
                    var minimized = parameter == 1;
                    if (minimized && !_windowMinimized)
                    {
                        _windowMinimized = true;
                        Minimized?.Invoke();
                    }
                    else if (!minimized)
                        _windowMinimized = false;
                }
                else if (message == HotkeyMessage)
                {
                    if (parameter == EmergencyId && _emergencyRegistered)
                        StopRequested?.Invoke();
                    else if (parameter == (nuint)_toggleId && _toggleRegistered)
                        ToggleRequested?.Invoke();
                }
                else if (message == TrayMessage)
                {
                    var mouseMessage = (uint)((long)detail & 0xFFFF);
                    if (mouseMessage == 0x0203)
                        ShowRequested?.Invoke();
                    else if (mouseMessage is 0x0205 or 0x007B)
                        ShowTrayMenu();
                }
                else if (message == 0x0218 && parameter == 4)
                    StopRequested?.Invoke();
                else if (message == 0x02B1 && _sessionRegistered && parameter is 2 or 4 or 6 or 7)
                    StopRequested?.Invoke();
                else if (_taskbarCreated != 0 && message == _taskbarCreated)
                {
                    _trayAvailable = _api.AddTrayIcon(_window, TrayTip);
                    RestoreIfTrayUnavailable();
                }
            }
        }
        catch (Exception)
        {
            _callbackFaulted = true;
            try
            {
                StopRequested?.Invoke();
            }
            catch (Exception)
            {
            }
        }

        return _api.ForwardMessage(window, message, parameter, detail);
    }

    private void ShowTrayMenu()
    {
        var selected = _api.ShowTrayMenu(_window, _strings.Get("TrayOpen"), _strings.Get("TrayStop"), _strings.Get("TrayExit"));
        if (selected == 1)
            ShowRequested?.Invoke();
        else if (selected == 2)
            StopRequested?.Invoke();
        else if (selected == 3)
            ExitRequested?.Invoke();
    }
}
