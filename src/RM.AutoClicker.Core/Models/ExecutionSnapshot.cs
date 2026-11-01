namespace RM.AutoClicker.Core.Models;

public enum SessionState
{
    Idle,
    Starting,
    Running,
    Stopping,
    Faulted,
    Disposed
}

public enum CompletionReason
{
    None,
    UserStopped,
    ClickLimit,
    DurationLimit,
    Failed
}

public sealed record SessionFailure(string Message, InputSendResult? InputResult = null,
    UserMessageCode Code = UserMessageCode.SessionUnexpected);

public sealed record ExecutionSnapshot(
    Guid SessionId,
    SessionState State,
    long SentClicks,
    TimeSpan Elapsed,
    TimeSpan StartDelayRemaining,
    CompletionReason CompletionReason,
    SessionFailure? Failure,
    bool IsInputSuppressed = false);
