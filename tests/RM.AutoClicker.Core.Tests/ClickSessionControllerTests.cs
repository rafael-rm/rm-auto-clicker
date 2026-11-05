using RM.AutoClicker.Core.Execution;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Core.Abstractions;

namespace RM.AutoClicker.Core.Tests;

public sealed class ClickSessionControllerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task RejectsInvalidRate(double rate)
    {
        await using var controller = new ClickSessionController(new RecordingEmitter());
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Start(new(ClicksPerSecond: rate)));
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
    }

    [Fact]
    public async Task ConcurrentStartsCreateOnlyOneSession()
    {
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, new TestClock());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => controller.Start(new(InitialDelay: TimeSpan.FromMinutes(1))))));

        Assert.Single(attempts, accepted => accepted);
        await controller.StopAsync();
        Assert.Equal(0, emitter.Calls);
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
    }

    [Fact]
    public async Task StopsDuringInitialDelayWithoutClicking()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new(InitialDelay: TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(3), await clock.NextWaitAsync());

        await Task.WhenAll(controller.StopAsync(), controller.StopAsync());
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, emitter.Calls);
        Assert.Equal(CompletionReason.UserStopped, controller.Snapshot.CompletionReason);
        Assert.Equal(1, emitter.Releases);
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(50, 20)]
    [InlineData(100, 10)]
    public async Task SendsExactlyTheRequestedNumberOfClicks(double rate, int intervalMilliseconds)
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new(ClicksPerSecond: rate, MaximumClicks: 3));

        var firstWait = await clock.NextWaitAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(intervalMilliseconds), firstWait);
        clock.Advance(firstWait);
        clock.Advance(await clock.NextWaitAsync());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, emitter.Calls);
        Assert.Equal(3, controller.Snapshot.SentClicks);
        Assert.Equal(CompletionReason.ClickLimit, controller.Snapshot.CompletionReason);
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
    }

    [Fact]
    public async Task SuppressedClicksWaitWithoutCountingOrFaultingAndResumeNormally()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter { Result = InputSendResult.Suppressed };
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new(MaximumClicks: 1));

        var wait = await clock.NextWaitAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(100), wait);
        Assert.Equal(1, emitter.Calls);
        Assert.Equal(0, controller.Snapshot.SentClicks);
        Assert.Equal(SessionState.Running, controller.Snapshot.State);
        Assert.True(controller.Snapshot.IsInputSuppressed);

        emitter.Result = new(2, 2);
        clock.Advance(wait);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, emitter.Calls);
        Assert.Equal(1, controller.Snapshot.SentClicks);
        Assert.Equal(CompletionReason.ClickLimit, controller.Snapshot.CompletionReason);
        Assert.Null(controller.Snapshot.Failure);
        Assert.False(controller.Snapshot.IsInputSuppressed);
    }

    [Fact]
    public async Task DurationLimitStillExpiresWhileClicksAreSuppressed()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter { Result = InputSendResult.Suppressed };
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new(MaximumDuration: TimeSpan.FromMilliseconds(150)));

        clock.Advance(await clock.NextWaitAsync());
        clock.Advance(await clock.NextWaitAsync());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, emitter.Calls);
        Assert.Equal(0, controller.Snapshot.SentClicks);
        Assert.Equal(TimeSpan.FromMilliseconds(150), controller.Snapshot.Elapsed);
        Assert.Equal(CompletionReason.DurationLimit, controller.Snapshot.CompletionReason);
    }

    [Fact]
    public async Task StopStillWorksWhileClicksAreSuppressed()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter { Result = InputSendResult.Suppressed };
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new());
        await clock.NextWaitAsync();

        await controller.StopAsync();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, emitter.Calls);
        Assert.Equal(0, controller.Snapshot.SentClicks);
        Assert.Equal(CompletionReason.UserStopped, controller.Snapshot.CompletionReason);
        Assert.False(controller.Snapshot.IsInputSuppressed);
    }

    [Fact]
    public async Task DurationLimitInterruptsALongerClickInterval()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new(ClicksPerSecond: 1, MaximumDuration: TimeSpan.FromMilliseconds(50)));

        var wait = await clock.NextWaitAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(50), wait);
        clock.Advance(wait);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, emitter.Calls);
        Assert.Equal(CompletionReason.DurationLimit, controller.Snapshot.CompletionReason);
        Assert.Equal(TimeSpan.FromMilliseconds(50), controller.Snapshot.Elapsed);
    }

    [Fact]
    public async Task InitialDelayDoesNotConsumeTheDurationLimit()
    {
        var clock = new TestClock();
        await using var controller = new ClickSessionController(new RecordingEmitter(), clock);
        controller.Start(new(InitialDelay: TimeSpan.FromSeconds(3), MaximumDuration: TimeSpan.FromMilliseconds(50)));

        clock.Advance(await clock.NextWaitAsync());
        clock.Advance(await clock.NextWaitAsync());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromMilliseconds(50), controller.Snapshot.Elapsed);
        Assert.Equal(1, controller.Snapshot.SentClicks);
    }

    [Fact]
    public async Task NoMoreClicksAreSentAfterStopCompletes()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new());
        await clock.NextWaitAsync();

        await controller.StopAsync();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, emitter.Calls);
        Assert.True(controller.Start(new(MaximumClicks: 1)));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, controller.Snapshot.SentClicks);
    }

    [Fact]
    public async Task PartialInputFailsWithoutCountingSuccessAndCanRecover()
    {
        var emitter = new RecordingEmitter { Result = new(2, 1, 5) };
        await using var controller = new ClickSessionController(emitter);
        controller.Start(new());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SessionState.Faulted, controller.Snapshot.State);
        Assert.Equal(0, controller.Snapshot.SentClicks);
        Assert.Equal(5, controller.Snapshot.Failure?.InputResult?.ErrorCode);
        Assert.Equal(1, emitter.Releases);
        Assert.False(controller.Start(new()));
        Assert.True(controller.ResetFault());

        emitter.Result = new(2, 2);
        Assert.True(controller.Start(new(MaximumClicks: 1)));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
    }

    [Fact]
    public async Task UnexpectedEmitterExceptionIsObserved()
    {
        var emitter = new RecordingEmitter { OnClick = () => throw new InvalidOperationException("Falha simulada.") };
        await using var controller = new ClickSessionController(emitter);
        controller.Start(new());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SessionState.Faulted, controller.Snapshot.State);
        Assert.Equal("Falha simulada.", controller.Snapshot.Failure?.Message);
        Assert.Equal(1, emitter.Releases);
    }

    [Fact]
    public async Task CleanupFailureIsNotReportedAsSuccessfulCompletion()
    {
        var emitter = new RecordingEmitter { ThrowOnRelease = true };
        var controller = new ClickSessionController(emitter);
        controller.Start(new(MaximumClicks: 1));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SessionState.Faulted, controller.Snapshot.State);
        Assert.Equal(CompletionReason.Failed, controller.Snapshot.CompletionReason);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.DisposeAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.DisposeAsync());
        Assert.Equal(2, emitter.Releases);
        Assert.Equal(SessionState.Disposed, controller.Snapshot.State);
        Assert.Equal(UserMessageCode.SessionRelease, controller.Snapshot.Failure!.Code);
        Assert.Contains("shutdown", controller.Snapshot.Failure.Message);
    }

    [Fact]
    public async Task DisposeRetriesTransientCleanupFailureOnlyOnce()
    {
        var emitter = new RecordingEmitter { ThrowOnRelease = true };
        var controller = new ClickSessionController(emitter);
        controller.Start(new(MaximumClicks: 1));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        emitter.ThrowOnRelease = false;

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        Assert.Equal(2, emitter.Releases);
        Assert.Equal(SessionState.Disposed, controller.Snapshot.State);
    }

    [Fact]
    public async Task DisposeDoesNotRepeatSuccessfulWorkerCleanup()
    {
        var emitter = new RecordingEmitter();
        var controller = new ClickSessionController(emitter);
        controller.Start(new(MaximumClicks: 1));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        Assert.Equal(1, emitter.Releases);
    }

    [Fact]
    public async Task LateSchedulerDoesNotSendACatchUpBurst()
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, clock);
        controller.Start(new());
        await clock.NextWaitAsync();

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromMilliseconds(100), await clock.NextWaitAsync());
        Assert.Equal(2, emitter.Calls);
        await controller.StopAsync();
    }

    [Fact]
    public async Task CannotRestartWhileThePreviousSessionIsStillStopping()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var emitter = new RecordingEmitter { OnClick = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not unblock the emitter.");
        } };
        await using var controller = new ClickSessionController(emitter);
        controller.Start(new());

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var stopping = controller.StopAsync();
        try
        {
            Assert.Equal(SessionState.Stopping, controller.Snapshot.State);
            Assert.False(controller.Start(new()));
            Assert.False(stopping.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
        Assert.Null(controller.Snapshot.Failure);
    }

    [Theory]
    [InlineData(ClickButton.Left, 0, 60)]
    [InlineData(ClickButton.Right, 0.5, 80)]
    [InlineData(ClickButton.Middle, 1, 100)]
    public async Task ForwardsTheSelectedButtonAndUsesTheRequestedRateAndVariation(ClickButton button, double sample, double intervalMilliseconds)
    {
        var clock = new TestClock();
        var emitter = new RecordingEmitter();
        await using var controller = new ClickSessionController(emitter, clock, new FixedRandom(sample));
        Assert.True(controller.Start(new(ClicksPerSecond: 12.5, VariationPercent: 25, Button: button, MaximumClicks: 2)));

        var wait = await clock.NextWaitAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(intervalMilliseconds), wait);
        clock.Advance(wait);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { button, button }, emitter.Buttons.ToArray());
        Assert.Equal(2, controller.Snapshot.SentClicks);
        Assert.Equal(CompletionReason.ClickLimit, controller.Snapshot.CompletionReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStopAndDisposeWaitForEmissionAndCleanup(bool blockCleanup)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        void Block()
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The test did not unblock the emitter.");
        }
        var emitter = new RecordingEmitter
        {
            OnClick = blockCleanup ? null : Block,
            OnRelease = blockCleanup ? Block : null
        };
        var controller = new ClickSessionController(emitter);
        Assert.True(controller.Start(new(MaximumClicks: 1)));
        Task stopping = Task.CompletedTask;
        Task disposing = Task.CompletedTask;
        Task secondDisposal = Task.CompletedTask;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            stopping = controller.StopAsync();
            Assert.False(controller.Start(new()));
            disposing = controller.DisposeAsync().AsTask();
            secondDisposal = controller.DisposeAsync().AsTask();
            Assert.False(stopping.IsCompleted);
            Assert.False(disposing.IsCompleted);
            Assert.Same(disposing, secondDisposal);
            Assert.Throws<ObjectDisposedException>(() => controller.Start(new()));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(stopping, disposing, secondDisposal).WaitAsync(TimeSpan.FromSeconds(5));
            await controller.DisposeAsync();
        }

        Assert.Equal(1, emitter.Calls);
        Assert.Equal(1, emitter.Releases);
        Assert.Null(controller.Snapshot.Failure);
        Assert.Equal(SessionState.Disposed, controller.Snapshot.State);
    }

    private sealed class FixedRandom(double sample) : IRandomSource
    {
        public double NextDouble() => sample;
    }

    [Fact]
    public async Task DisposeStopsTheWorkerAndRejectsNewSessions()
    {
        var clock = new TestClock();
        var controller = new ClickSessionController(new RecordingEmitter(), clock);
        controller.Start(new());
        await clock.NextWaitAsync();

        await controller.DisposeAsync();
        await controller.DisposeAsync();
        Assert.Equal(SessionState.Disposed, controller.Snapshot.State);
        Assert.Throws<ObjectDisposedException>(() => controller.Start(new()));
    }
}
