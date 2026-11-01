using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Platform.Windows.Input;

public sealed class WindowsInputEmitter : IInputEmitter
{
    private readonly IWindowsInputApi _api;
    private readonly IClickTargetGuard _targetGuard;
    private ClickButton? _pending;

    public WindowsInputEmitter() : this(new WindowsInputApi(),
        new WindowsClickTargetGuard(new WindowTargetApi(), (uint)Environment.ProcessId))
    {
    }

    internal WindowsInputEmitter(IWindowsInputApi api, IClickTargetGuard targetGuard)
    {
        _api = api;
        _targetGuard = targetGuard;
    }

    public InputSendResult SendClick(ClickButton button)
    {
        if (_pending is not null)
            throw new InvalidOperationException("A mouse button is pending release. Stop and try again.");

        var flags = GetFlags(button);
        if (!_targetGuard.CanSendClick())
            return InputSendResult.Suppressed;

        var result = _api.Send([CreateInput(flags.Down), CreateInput(flags.Up)]);
        if (result.InsertedEvents == 1)
            _pending = button;

        return result;
    }

    public void ReleasePendingButtons()
    {
        if (_pending is not { } button)
            return;

        var result = _api.Send([CreateInput(GetFlags(button).Up)]);
        if (!result.IsSuccess)
            throw new InputEmissionException(result);

        _pending = null;
    }

    private static NativeInput CreateInput(uint flags) => new()
    {
        Type = 0,
        Data = new InputUnion { Mouse = new MouseInput { Flags = flags } }
    };

    private static (uint Down, uint Up) GetFlags(ClickButton button) => button switch
    {
        ClickButton.Left => (0x0002, 0x0004),
        ClickButton.Right => (0x0008, 0x0010),
        ClickButton.Middle => (0x0020, 0x0040),
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };
}
