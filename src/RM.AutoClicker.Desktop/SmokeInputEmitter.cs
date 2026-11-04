using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Desktop;

internal sealed class SmokeInputEmitter : IInputEmitter
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public InputSendResult SendClick(ClickButton button)
    {
        Interlocked.Increment(ref _attempts);
        throw new InvalidOperationException("Input emission is forbidden during smoke testing.");
    }

    public void ReleasePendingButtons() { }
}

internal sealed class SmokeHotkeys : IHotkeyService
{
    public bool HotkeysReady => true;
    public bool EmergencyReady => true;
    public bool HasStatusWarning => false;
    public string Status => "Simulated hotkey registration for smoke testing.";

    public bool TryChangeHotkey(HotkeyGesture hotkey, out string? error)
    {
        error = null;
        return true;
    }
}
