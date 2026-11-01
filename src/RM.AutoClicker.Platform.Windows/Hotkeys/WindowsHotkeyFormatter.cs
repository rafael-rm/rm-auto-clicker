using System.Runtime.InteropServices;
using System.Text;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Platform.Windows.Hotkeys;

internal static class WindowsHotkeyFormatter
{
    public static string Format(HotkeyGesture hotkey)
    {
        var keyName = hotkey.KeyDisplayName;
        if (keyName.StartsWith("Tecla 0x", StringComparison.Ordinal))
        {
            var scan = MapVirtualKey(hotkey.VirtualKey, 4);
            var parameter = (int)((scan & 0xFF) << 16);
            if ((scan & 0xFF00) != 0)
                parameter |= 1 << 24;
            var name = new StringBuilder(128);
            if (scan != 0 && GetKeyNameText(parameter, name, name.Capacity) > 0)
                keyName = name.ToString();
        }
        return hotkey.Format(keyName);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKey(uint code, uint mapping);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameText(int parameter, StringBuilder text, int capacity);
}
