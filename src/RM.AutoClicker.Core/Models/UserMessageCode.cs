namespace RM.AutoClicker.Core.Models;

public enum UserMessageCode
{
    SettingsIncompatible,
    SettingsInvalid,
    SettingsUnreadable,
    HotkeyWindowsReserved,
    HotkeyInvalidModifiers,
    HotkeyMissingKey,
    HotkeyDebuggerReserved,
    HotkeyEmergencyReserved,
    HotkeySystemReserved,
    SessionUnexpected,
    SessionInput,
    SessionRelease
}

public sealed class AppValidationException(UserMessageCode code) : ArgumentException(code.ToString())
{
    public UserMessageCode Code { get; } = code;
}
