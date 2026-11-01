using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Execution;

public sealed class ClickSessionController : IClickSessionController
{
    private readonly object _sync = new();
    private readonly IInputEmitter _emitter;
    private readonly TimeProvider _time;
    private readonly IntervalPolicy _intervals;
    private CancellationTokenSource? _cancellation;
    private Task _worker = Task.CompletedTask;
    private Task? _disposal;
    private SessionState _state;
    private Guid _sessionId;
    private long _sentClicks;
    private long _requestedTimestamp;
    private long? _runningTimestamp;
    private TimeSpan _elapsed;
    private TimeSpan _initialDelay;
    private CompletionReason _reason;
    private SessionFailure? _failure;
    private bool _inputSuppressed;
    private bool _releasePendingFailed;
    private bool _disposeRequested;

    public ClickSessionController(IInputEmitter emitter, TimeProvider? time = null, IRandomSource? random = null)
    {
        _emitter = emitter;
        _time = time ?? TimeProvider.System;
        _intervals = new IntervalPolicy(random ?? new SystemRandomSource());
    }

    public ExecutionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var elapsed = _runningTimestamp is { } started && _state is SessionState.Running or SessionState.Stopping
                    ? _time.GetElapsedTime(started)
                    : _elapsed;
                var delayRemaining = _state == SessionState.Starting
                    ? _initialDelay - _time.GetElapsedTime(_requestedTimestamp)
                    : TimeSpan.Zero;
                return new(_sessionId, _state, _sentClicks, elapsed,
                    delayRemaining > TimeSpan.Zero ? delayRemaining : TimeSpan.Zero, _reason, _failure, _inputSuppressed);
            }
        }
    }

    public Task Completion
    {
        get
        {
            lock (_sync)
                return _worker;
        }
    }

    public bool Start(ClickPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            if (_state != SessionState.Idle || !_worker.IsCompleted)
                return false;

            _sessionId = Guid.NewGuid();
            _sentClicks = 0;
            _elapsed = TimeSpan.Zero;
            _runningTimestamp = null;
            _requestedTimestamp = _time.GetTimestamp();
            _initialDelay = plan.InitialDelay;
            _reason = CompletionReason.None;
            _failure = null;
            _inputSuppressed = false;
            _state = SessionState.Starting;
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _worker = Task.Run(() => RunAsync(plan, token));
            return true;
        }
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_state is SessionState.Starting or SessionState.Running)
            {
                _state = SessionState.Stopping;
                _cancellation?.Cancel();
            }

            return _worker;
        }
    }

    public bool ResetFault()
    {
        lock (_sync)
        {
            if (_disposeRequested || _state != SessionState.Faulted || !_worker.IsCompleted)
                return false;

            try
            {
                _emitter.ReleasePendingButtons();
                _releasePendingFailed = false;
            }
            catch (Exception exception)
            {
                _failure = new($"Could not release the pending button: {exception.Message}", Code: UserMessageCode.SessionRelease);
                return false;
            }

            _state = SessionState.Idle;
            _failure = null;
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposeRequested = true;
            _disposal ??= DisposeCoreAsync();
            return new ValueTask(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await StopAsync().ConfigureAwait(false);
        try
        {
            if (_releasePendingFailed)
            {
                _emitter.ReleasePendingButtons();
                _releasePendingFailed = false;
            }
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _failure = new($"Could not release the button during shutdown: {exception.Message}",
                    (exception as InputEmissionException)?.Result, UserMessageCode.SessionRelease);
                _reason = CompletionReason.Failed;
            }
            throw;
        }
        finally
        {
            lock (_sync)
                _state = SessionState.Disposed;
        }
    }

    private async Task RunAsync(ClickPlan plan, CancellationToken token)
    {
        var reason = CompletionReason.UserStopped;
        SessionFailure? failure = null;

        try
        {
            if (plan.InitialDelay > TimeSpan.Zero)
                await Task.Delay(plan.InitialDelay, _time, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            long started;
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                _state = SessionState.Running;
                started = _time.GetTimestamp();
                _runningTimestamp = started;
            }

            var scheduled = TimeSpan.Zero;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var elapsed = _time.GetElapsedTime(started);
                if (plan.MaximumDuration is { } duration && elapsed >= duration)
                {
                    reason = CompletionReason.DurationLimit;
                    break;
                }

                var remaining = scheduled - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    if (plan.MaximumDuration is { } deadline && remaining > deadline - elapsed)
                        remaining = deadline - elapsed;

                    await Task.Delay(remaining, _time, token).ConfigureAwait(false);
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var result = _emitter.SendClick(plan.Button);
                if (!result.IsSuppressed && !result.IsSuccess)
                    throw new InputEmissionException(result);

                long sent;
                lock (_sync)
                {
                    _inputSuppressed = result.IsSuppressed;
                    sent = result.IsSuppressed ? _sentClicks : ++_sentClicks;
                }

                if (plan.MaximumClicks is { } maximum && sent >= maximum)
                {
                    reason = CompletionReason.ClickLimit;
                    break;
                }

                var interval = _intervals.GetNextInterval(plan);
                scheduled += interval;
                var afterSend = _time.GetElapsedTime(started);
                if (scheduled <= afterSend)
                    scheduled = afterSend + interval;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            reason = CompletionReason.UserStopped;
        }
        catch (Exception exception)
        {
            failure = new(exception.Message, (exception as InputEmissionException)?.Result,
                exception is InputEmissionException ? UserMessageCode.SessionInput : UserMessageCode.SessionUnexpected);
        }
        finally
        {
            var releaseFailed = false;
            try
            {
                _emitter.ReleasePendingButtons();
            }
            catch (Exception exception)
            {
                releaseFailed = true;
                failure = new($"{failure?.Message} Could not release the button: {exception.Message}".Trim(), Code: UserMessageCode.SessionRelease);
            }

            lock (_sync)
            {
                _elapsed = _runningTimestamp is { } started ? _time.GetElapsedTime(started) : TimeSpan.Zero;
                _failure = failure;
                _releasePendingFailed = releaseFailed;
                _inputSuppressed = false;
                _reason = failure is null ? reason : CompletionReason.Failed;
                _state = failure is null ? SessionState.Idle : SessionState.Faulted;
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
    }
}
