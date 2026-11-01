using System.Runtime.InteropServices;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Platform.Windows.Input;

internal interface IWindowsInputApi
{
    InputSendResult Send(NativeInput[] inputs);
}

internal sealed class WindowsInputApi : IWindowsInputApi
{
    public InputSendResult Send(NativeInput[] inputs)
    {
        var inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        var error = inserted == inputs.Length ? 0 : Marshal.GetLastPInvokeError();
        return new(inputs.Length, (int)inserted, error);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int inputSize);
}
