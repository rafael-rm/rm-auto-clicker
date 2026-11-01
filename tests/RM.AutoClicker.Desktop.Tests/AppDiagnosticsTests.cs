using System.Text.Json;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class AppDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RMAutoClicker.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SeparateLogInstancesKeepParallelEventsInTheirOwnDirectories()
    {
        var firstDirectory = Path.Combine(_directory, "first");
        var secondDirectory = Path.Combine(_directory, "second");
        var first = new DiagnosticLog(firstDirectory);
        var second = new DiagnosticLog(secondDirectory);
        await Task.WhenAll(Task.Run(() => first.Write("First", new IOException("Simulated failure"))),
            Task.Run(() => second.Write("Second")));

        using var firstEntry = JsonDocument.Parse(Assert.Single(File.ReadAllLines(Path.Combine(firstDirectory, "app.jsonl"))));
        using var secondEntry = JsonDocument.Parse(Assert.Single(File.ReadAllLines(Path.Combine(secondDirectory, "app.jsonl"))));
        Assert.Equal("First", firstEntry.RootElement.GetProperty("Event").GetString());
        Assert.Contains("Simulated failure", firstEntry.RootElement.GetProperty("Error").GetString());
        Assert.Equal("Second", secondEntry.RootElement.GetProperty("Event").GetString());
        Assert.Equal(JsonValueKind.Null, secondEntry.RootElement.GetProperty("Error").ValueKind);
    }

    [Fact]
    public void RotationPreservesThePreviousLogInTheConfiguredDirectory()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "app.jsonl");
        var previous = new string('x', 1_048_577);
        File.WriteAllText(path, previous);
        File.WriteAllText(path + ".previous", "Older log");

        new DiagnosticLog(_directory).Write("Rotated");

        Assert.Equal(previous, File.ReadAllText(path + ".previous"));
        using var entry = JsonDocument.Parse(Assert.Single(File.ReadAllLines(path)));
        Assert.Equal("Rotated", entry.RootElement.GetProperty("Event").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
