using System.Text.Json;

namespace RM.AutoClicker.Desktop;

internal static class AppDiagnostics
{
    private static readonly DiagnosticLog Log = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RM Corporation", "RM Auto Clicker", "logs"));

    public static void Write(string eventName, Exception? exception = null) => Log.Write(eventName, exception);
}

internal sealed class DiagnosticLog(string directory)
{
    private readonly object _sync = new();

    public void Write(string eventName, Exception? exception = null)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "app.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                    File.Move(path, path + ".previous", true);

                File.AppendAllText(path, JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.UtcNow, Event = eventName, Error = exception?.ToString() }) + Environment.NewLine);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.WriteLine(failure.Message);
            }
        }
    }
}
