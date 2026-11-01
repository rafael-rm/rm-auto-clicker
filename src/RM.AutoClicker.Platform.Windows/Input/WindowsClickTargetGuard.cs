using System.Runtime.InteropServices;

namespace RM.AutoClicker.Platform.Windows.Input;

internal interface IClickTargetGuard
{
    bool CanSendClick();
}

internal interface IWindowTargetApi
{
    nint GetWindowUnderCursor();
    nint GetRootOwner(nint window);
    uint GetProcessId(nint window);
    bool TryGetMouseCapture(out nint window);
}

internal sealed class WindowsClickTargetGuard(IWindowTargetApi windows, uint ownProcessId) : IClickTargetGuard
{
    public bool CanSendClick()
    {
        if (!windows.TryGetMouseCapture(out var capture))
            return false;
        if (capture != 0 && !IsExternalWindow(capture))
            return false;

        return IsExternalWindow(windows.GetWindowUnderCursor());
    }

    private bool IsExternalWindow(nint window)
    {
        if (window == 0)
            return false;

        var processId = windows.GetProcessId(window);
        if (processId == 0 || processId == ownProcessId)
            return false;

        var owner = windows.GetRootOwner(window);
        if (owner == 0)
            return false;
        if (owner == window)
            return true;

        var ownerProcessId = windows.GetProcessId(owner);
        return ownerProcessId != 0 && ownerProcessId != ownProcessId;
    }
}

internal sealed class WindowTargetApi : IWindowTargetApi
{
    public nint GetWindowUnderCursor() => GetCursorPos(out var point) ? WindowFromPoint(point) : 0;

    public nint GetRootOwner(nint window) => GetAncestor(window, 3);

    public uint GetProcessId(nint window) => GetWindowThreadProcessId(window, out var processId) != 0 ? processId : 0;

    public bool TryGetMouseCapture(out nint window)
    {
        var information = new NativeGuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeGuiThreadInfo>() };
        var success = GetGUIThreadInfo(0, ref information);
        window = information.CaptureWindow != 0 ? information.CaptureWindow : information.MoveSizeWindow;
        return success;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref NativeGuiThreadInfo information);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeGuiThreadInfo
{
    public uint Size;
    public uint Flags;
    public nint ActiveWindow;
    public nint FocusWindow;
    public nint CaptureWindow;
    public nint MenuOwnerWindow;
    public nint MoveSizeWindow;
    public nint CaretWindow;
    public int CaretLeft;
    public int CaretTop;
    public int CaretRight;
    public int CaretBottom;
}
