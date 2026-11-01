using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Abstractions;

public interface IHotkeyService
{
    bool HotkeysReady { get; }

    bool EmergencyReady { get; }

    string Status { get; }

    bool TryChangeHotkey(HotkeyGesture hotkey, out string? error);

    string GetDisplayName(HotkeyGesture hotkey) => hotkey.ToString();
}
