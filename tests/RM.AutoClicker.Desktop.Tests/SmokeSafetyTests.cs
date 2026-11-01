using RM.AutoClicker.Core.Execution;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.ViewModels;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Tests;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class SmokeSafetyTests
{
    [Fact]
    public async Task SmokeModeBlocksToggleWithAValidFormAndReadyHotkeys()
    {
        var emitter = new SmokeInputEmitter();
        await using var controller = new ClickSessionController(emitter);
        var model = new MainViewModel(TestStrings.Portuguese, controller, new UnusedSettings(), true,
            writeDiagnostic: new RecordingDiagnostics().Write);
        model.Attach(new SmokeHotkeys());

        Assert.Empty(model.ValidationMessage);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.CanEmphasizeStart);
        model.Toggle();
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Guid.Empty, controller.Snapshot.SessionId);
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
        Assert.Equal(0, emitter.Attempts);
    }

    [Theory]
    [InlineData(ClickButton.Left)]
    [InlineData(ClickButton.Right)]
    [InlineData(ClickButton.Middle)]
    public async Task SmokeEmitterRejectsAndRecordsAccidentalEngineStarts(ClickButton button)
    {
        var emitter = new SmokeInputEmitter();
        await using var controller = new ClickSessionController(emitter);
        Assert.True(controller.Start(new(Button: button, MaximumClicks: 1)));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, emitter.Attempts);
        Assert.Equal(0, controller.Snapshot.SentClicks);
        Assert.Equal(SessionState.Faulted, controller.Snapshot.State);
        Assert.Equal(UserMessageCode.SessionUnexpected, controller.Snapshot.Failure?.Code);
    }

    private sealed class UnusedSettings : ISettingsStore
    {
        public SettingsLoadResult Load() => throw new InvalidOperationException("Smoke must not load user settings.");
        public Task SaveAsync(AppSettings settings) => throw new InvalidOperationException("Smoke must not save user settings.");
    }
}
