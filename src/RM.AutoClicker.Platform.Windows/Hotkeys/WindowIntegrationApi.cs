using System.Runtime.InteropServices;

namespace RM.AutoClicker.Platform.Windows.Hotkeys;

internal delegate nint WindowSubclassProcedure(nint window, uint message, nuint parameter, nint detail, nuint subclassId, nuint referenceData);

internal interface IWindowIntegrationApi
{
    int LastError { get; }
    bool Attach(nint window, WindowSubclassProcedure procedure);
    void Detach(nint window, WindowSubclassProcedure procedure);
    nint ForwardMessage(nint window, uint message, nuint parameter, nint detail);
    bool RegisterHotkey(nint window, int id, uint modifiers, uint key);
    bool UnregisterHotkey(nint window, int id);
    uint RegisterTaskbarMessage();
    bool RegisterSessionNotifications(nint window);
    void UnregisterSessionNotifications(nint window);
    bool AddTrayIcon(nint window, string tip);
    bool UpdateTrayIcon(string tip);
    void DeleteTrayIcon();
    void SetVisible(nint window, bool visible);
    uint ShowTrayMenu(nint window, string openLabel, string stopLabel, string exitLabel);
}

internal sealed class WindowsWindowIntegrationApi : IWindowIntegrationApi
{
    private NotificationIconData _tray;

    public int LastError => Marshal.GetLastPInvokeError();
    public bool Attach(nint window, WindowSubclassProcedure procedure) => SetWindowSubclass(window, procedure, 1, 0);
    public void Detach(nint window, WindowSubclassProcedure procedure) => RemoveWindowSubclass(window, procedure, 1);
    public nint ForwardMessage(nint window, uint message, nuint parameter, nint detail) => DefSubclassProc(window, message, parameter, detail);
    public bool RegisterHotkey(nint window, int id, uint modifiers, uint key) => RegisterHotKey(window, id, modifiers, key);
    public bool UnregisterHotkey(nint window, int id) => UnregisterHotKey(window, id);
    public uint RegisterTaskbarMessage() => RegisterWindowMessage("TaskbarCreated");
    public bool RegisterSessionNotifications(nint window) => WTSRegisterSessionNotification(window, 0);
    public void UnregisterSessionNotifications(nint window) => WTSUnRegisterSessionNotification(window);

    public bool AddTrayIcon(nint window, string tip)
    {
        // Borrow the window-owned icon; the window keeps it alive until the tray is removed.
        var icon = SendMessage(window, 0x007F, 0, 0);
        if (icon == 0)
            icon = SendMessage(window, 0x007F, 1, 0);
        if (icon == 0)
            return false;

        _tray = new NotificationIconData
        {
            Size = (uint)Marshal.SizeOf<NotificationIconData>(),
            Window = window,
            Id = 1,
            Flags = 0x0001 | 0x0002 | 0x0004,
            CallbackMessage = 0x8001,
            Icon = icon,
            Tip = tip,
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        return Shell_NotifyIcon(0, ref _tray);
    }

    public bool UpdateTrayIcon(string tip)
    {
        _tray.Tip = tip;
        return Shell_NotifyIcon(1, ref _tray);
    }

    public void DeleteTrayIcon() => Shell_NotifyIcon(2, ref _tray);

    public void SetVisible(nint window, bool visible)
    {
        ShowWindow(window, visible ? 9 : 0);
        if (visible)
            SetForegroundWindow(window);
    }

    public uint ShowTrayMenu(nint window, string openLabel, string stopLabel, string exitLabel)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
            return 0;

        try
        {
            if (!AppendMenu(menu, 0, 1, openLabel) || !AppendMenu(menu, 0, 2, stopLabel)
                || !AppendMenu(menu, 0, 3, exitLabel) || !GetCursorPos(out var position))
                return 0;
            SetForegroundWindow(window);
            return TrackPopupMenu(menu, 0x0100 | 0x0002, position.PositionX, position.PositionY, 0, window, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotificationIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int PositionX;
        public int PositionY;
    }

    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, WindowSubclassProcedure procedure, nuint id, nuint referenceData);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, WindowSubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint parameter, nint detail);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nuint parameter, nint detail);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint command, ref NotificationIconData data);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string label);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int positionX, int positionY, int reserved, nint window, nint rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
}
