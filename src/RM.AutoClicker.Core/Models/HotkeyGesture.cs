namespace RM.AutoClicker.Core.Models;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeyGesture(uint VirtualKey = 0x77, HotkeyModifiers Modifiers = HotkeyModifiers.None)
{
    public static HotkeyGesture Default { get; } = new();
    public static HotkeyGesture Emergency { get; } = new(0x79, HotkeyModifiers.Control | HotkeyModifiers.Shift);

    public static bool IsModifierKey(uint key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    public void Validate()
    {
        if ((Modifiers & HotkeyModifiers.Windows) != 0)
            throw new AppValidationException(UserMessageCode.HotkeyWindowsReserved);
        if ((Modifiers & ~(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)) != 0)
            throw new AppValidationException(UserMessageCode.HotkeyInvalidModifiers);
        if (VirtualKey is < 0x08 or > 0xFE or 0xE5 or 0xE7 || IsModifierKey(VirtualKey))
            throw new AppValidationException(UserMessageCode.HotkeyMissingKey);
        if (VirtualKey == 0x7B)
            throw new AppValidationException(UserMessageCode.HotkeyDebuggerReserved);
        if (this == Emergency)
            throw new AppValidationException(UserMessageCode.HotkeyEmergencyReserved);
        if (((Modifiers & HotkeyModifiers.Alt) != 0 && VirtualKey == 0x09)
            || ((Modifiers & (HotkeyModifiers.Alt | HotkeyModifiers.Control)) == HotkeyModifiers.Alt && VirtualKey is 0x1B or 0x73)
            || ((Modifiers & HotkeyModifiers.Control) != 0 && VirtualKey == 0x1B)
            || ((Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && VirtualKey == 0x2E))
            throw new AppValidationException(UserMessageCode.HotkeySystemReserved);
    }

    public string KeyDisplayName => VirtualKey switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{VirtualKey - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num {VirtualKey - 0x60}",
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x13 => "Pause", 0x14 => "Caps Lock",
        0x1B => "Esc", 0x20 => "Espaço", 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
        0x25 => "Esquerda", 0x26 => "Cima", 0x27 => "Direita", 0x28 => "Baixo",
        0x2C => "Print Screen", 0x2D => "Insert", 0x2E => "Delete", 0x5D => "Menu",
        0x6A => "Num *", 0x6B => "Num +", 0x6C => "Num Separador", 0x6D => "Num -", 0x6E => "Num Decimal", 0x6F => "Num /",
        0x90 => "Num Lock", 0x91 => "Scroll Lock",
        _ => $"Tecla 0x{VirtualKey:X2}"
    };

    public string Format(string keyName)
    {
        var parts = new List<string>();
        if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotkeyModifiers.Windows) != 0) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public override string ToString() => Format(KeyDisplayName);
}
