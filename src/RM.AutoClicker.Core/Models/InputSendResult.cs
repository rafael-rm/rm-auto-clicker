namespace RM.AutoClicker.Core.Models;

public readonly record struct InputSendResult(int RequestedEvents, int InsertedEvents, int ErrorCode = 0)
{
    public static InputSendResult Suppressed => new(0, 0) { IsSuppressed = true };
    public bool IsSuppressed { get; init; }
    public bool IsSuccess => !IsSuppressed && RequestedEvents > 0 && InsertedEvents == RequestedEvents;
}

public sealed class InputEmissionException(InputSendResult result)
    : Exception($"Could not send the click ({result.InsertedEvents}/{result.RequestedEvents} events; code {result.ErrorCode}). Check permissions and the target application.")
{
    public InputSendResult Result { get; } = result;
}
