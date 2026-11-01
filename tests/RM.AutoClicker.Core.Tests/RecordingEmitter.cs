using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using System.Collections.Concurrent;

namespace RM.AutoClicker.Core.Tests;

internal sealed class RecordingEmitter : IInputEmitter
{
    private int _calls;
    private int _releases;

    public int Calls => Volatile.Read(ref _calls);
    public int Releases => Volatile.Read(ref _releases);
    public InputSendResult Result { get; set; } = new(2, 2);
    public Action? OnClick { get; set; }
    public Action? OnRelease { get; set; }
    public ConcurrentQueue<ClickButton> Buttons { get; } = new();
    public bool ThrowOnRelease { get; set; }

    public InputSendResult SendClick(ClickButton button)
    {
        Interlocked.Increment(ref _calls);
        Buttons.Enqueue(button);
        OnClick?.Invoke();
        return Result;
    }

    public void ReleasePendingButtons()
    {
        Interlocked.Increment(ref _releases);
        OnRelease?.Invoke();
        if (ThrowOnRelease)
            throw new InvalidOperationException("Falha de limpeza simulada.");
    }
}
