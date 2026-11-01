using System.Runtime.InteropServices;
using RM.AutoClicker.Platform.Windows.Input;

namespace RM.AutoClicker.Platform.Windows.Tests;

public sealed class WindowsClickTargetGuardTests
{
    [Theory]
    [InlineData(42, 42)]
    [InlineData(42, 99)]
    [InlineData(99, 42)]
    public void RejectsOwnWindowChildrenAndOwnedPopups(uint targetProcessId, uint ownerProcessId)
    {
        var windows = new WindowApi { TargetProcessId = targetProcessId, OwnerProcessId = ownerProcessId };
        Assert.False(new WindowsClickTargetGuard(windows, 42).CanSendClick());
    }

    [Theory]
    [InlineData(0, 2, 99, 99)]
    [InlineData(1, 2, 0, 99)]
    [InlineData(1, 0, 99, 99)]
    [InlineData(1, 2, 99, 0)]
    public void RejectsAnUnresolvedTarget(int target, int owner, uint targetProcessId, uint ownerProcessId)
    {
        var windows = new WindowApi
        {
            Target = target,
            Owner = owner,
            TargetProcessId = targetProcessId,
            OwnerProcessId = ownerProcessId
        };
        Assert.False(new WindowsClickTargetGuard(windows, 42).CanSendClick());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AllowsAnotherProcessEvenWithAnOwnedPopup(int owner)
    {
        var windows = new WindowApi { Owner = owner };
        Assert.True(new WindowsClickTargetGuard(windows, 42).CanSendClick());
    }

    [Theory]
    [InlineData(false, 0, 99, false)]
    [InlineData(true, 3, 42, false)]
    [InlineData(true, 3, 0, false)]
    [InlineData(true, 3, 99, true)]
    [InlineData(true, 0, 99, true)]
    public void AccountsForCaptureAndRejectsAnUnresolvedCapture(bool resolved, int capture, uint captureProcessId, bool allowed)
    {
        var windows = new WindowApi { CaptureResolved = resolved, Capture = capture, CaptureProcessId = captureProcessId };
        Assert.Equal(allowed, new WindowsClickTargetGuard(windows, 42).CanSendClick());
    }

    [Fact]
    public void RejectsCaptureOwnedByTheProtectedProcess()
    {
        var windows = new WindowApi { Capture = 3, CaptureOwner = 4, CaptureOwnerProcessId = 42 };
        Assert.False(new WindowsClickTargetGuard(windows, 42).CanSendClick());
    }

    [Fact]
    public void GuiThreadInformationHasTheNativeX64Layout()
    {
        Assert.Equal(72, Marshal.SizeOf<NativeGuiThreadInfo>());
        Assert.Equal(24, Marshal.OffsetOf<NativeGuiThreadInfo>(nameof(NativeGuiThreadInfo.CaptureWindow)).ToInt32());
    }

    private sealed class WindowApi : IWindowTargetApi
    {
        public nint Target { get; init; } = 1;
        public nint Owner { get; init; } = 2;
        public uint TargetProcessId { get; init; } = 99;
        public uint OwnerProcessId { get; init; } = 99;
        public bool CaptureResolved { get; init; } = true;
        public nint Capture { get; init; }
        public nint CaptureOwner { get; init; } = 3;
        public uint CaptureProcessId { get; init; } = 99;
        public uint CaptureOwnerProcessId { get; init; } = 99;

        public nint GetWindowUnderCursor() => Target;
        public nint GetRootOwner(nint window) => window == 3 ? CaptureOwner : Owner;
        public uint GetProcessId(nint window) => window == 4 ? CaptureOwnerProcessId : window == 3 ? CaptureProcessId : window == Target ? TargetProcessId : OwnerProcessId;
        public bool TryGetMouseCapture(out nint window) { window = Capture; return CaptureResolved; }
    }
}
