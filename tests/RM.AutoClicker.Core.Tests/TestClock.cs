using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace RM.AutoClicker.Core.Tests;

internal sealed class TestClock : TimeProvider
{
    private readonly FakeTimeProvider _inner = new();
    private readonly Channel<TimeSpan> _waits = Channel.CreateUnbounded<TimeSpan>();

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    public override long GetTimestamp() => _inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _inner.CreateTimer(callback, state, dueTime, period);
        _waits.Writer.TryWrite(dueTime);
        return timer;
    }

    public void Advance(TimeSpan amount) => _inner.Advance(amount);

    public Task<TimeSpan> NextWaitAsync() => _waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
}
