using System.Collections.Concurrent;

namespace RM.AutoClicker.Desktop.Tests;

internal sealed class RecordingDiagnostics
{
    public ConcurrentQueue<(string Event, Exception? Error)> Entries { get; } = new();

    public void Write(string eventName, Exception? exception) => Entries.Enqueue((eventName, exception));
}
