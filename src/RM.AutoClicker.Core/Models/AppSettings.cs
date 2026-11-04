namespace RM.AutoClicker.Core.Models;

public sealed record AppSettings(int SchemaVersion = 2, ClickPlan? Plan = null, HotkeyGesture? Hotkey = null, int ThemeIndex = 0,
    bool HasInitialDelay = false, string Language = "auto", bool MinimizeToTray = false)
{
    public HotkeyGesture EffectiveHotkey => Hotkey ?? HotkeyGesture.Default;

    public void Validate()
    {
        if (SchemaVersion != 2 || ThemeIndex is < 0 or > 2 || Language is not ("auto" or "pt-BR" or "en-US" or "es"))
            throw new ArgumentException("Invalid preferences or incompatible schema version.");
        Plan?.Validate();
        EffectiveHotkey.Validate();
    }
}

public sealed record SettingsLoadResult(AppSettings Settings, UserMessageCode? Warning = null, bool CanSave = true);
