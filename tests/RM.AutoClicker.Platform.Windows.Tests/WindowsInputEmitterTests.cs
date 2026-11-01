using System.Runtime.InteropServices;
using RM.AutoClicker.Core.Execution;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Platform.Windows.Input;

namespace RM.AutoClicker.Platform.Windows.Tests;

public sealed class WindowsInputEmitterTests
{
    [Fact]
    public void InputStructureHasTheNativeX64Layout()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(40, Marshal.SizeOf<NativeInput>());
        Assert.Equal(32, Marshal.SizeOf<InputUnion>());
        Assert.Equal(8, Marshal.OffsetOf<NativeInput>(nameof(NativeInput.Data)).ToInt32());
    }

    [Theory]
    [InlineData(ClickButton.Left, 0x0002, 0x0004)]
    [InlineData(ClickButton.Right, 0x0008, 0x0010)]
    [InlineData(ClickButton.Middle, 0x0020, 0x0040)]
    public void SendsMatchingDownAndUpWithoutMovingTheCursor(ClickButton button, uint down, uint up)
    {
        var api = new RecordingApi();
        var emitter = new WindowsInputEmitter(api, new TargetGuard());
        Assert.True(emitter.SendClick(button).IsSuccess);
        var inputs = Assert.Single(api.Calls);
        Assert.Equal(2, inputs.Length);
        Assert.Equal(down, inputs[0].Data.Mouse.Flags);
        Assert.Equal(up, inputs[1].Data.Mouse.Flags);
        Assert.All(inputs, input => Assert.Equal(0u, input.Type));
        emitter.ReleasePendingButtons();
        Assert.Single(api.Calls);
    }

    [Fact]
    public void PartialSendReleasesOnlyTheButtonItPressed()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 1, 5));
        var emitter = new WindowsInputEmitter(api, new TargetGuard());
        Assert.False(emitter.SendClick(ClickButton.Right).IsSuccess);
        Assert.Throws<InvalidOperationException>(() => emitter.SendClick(ClickButton.Left));

        emitter.ReleasePendingButtons();
        var release = Assert.Single(api.Calls[1]);
        Assert.Equal(0x0010u, release.Data.Mouse.Flags);
        emitter.ReleasePendingButtons();
        Assert.Equal(2, api.Calls.Count);
    }

    [Fact]
    public void FailedReleasePreservesPendingStateForRetry()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 1));
        api.Results.Enqueue(new(1, 0, 5));
        var emitter = new WindowsInputEmitter(api, new TargetGuard());
        emitter.SendClick(ClickButton.Left);

        Assert.Throws<InputEmissionException>(emitter.ReleasePendingButtons);
        Assert.Throws<InvalidOperationException>(() => emitter.SendClick(ClickButton.Left));
        emitter.ReleasePendingButtons();
        Assert.True(emitter.SendClick(ClickButton.Middle).IsSuccess);
    }

    [Fact]
    public void ZeroInsertedEventsDoNotReleaseAnUnownedButton()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 0));
        var emitter = new WindowsInputEmitter(api, new TargetGuard());
        emitter.SendClick(ClickButton.Left);
        emitter.ReleasePendingButtons();
        Assert.Single(api.Calls);
    }

    [Fact]
    public void ProtectedTargetDoesNotReceiveInputAndCanResume()
    {
        var api = new RecordingApi();
        var guard = new TargetGuard { Allowed = false };
        var emitter = new WindowsInputEmitter(api, guard);

        var result = emitter.SendClick(ClickButton.Left);
        Assert.True(result.IsSuppressed);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.InsertedEvents);
        emitter.ReleasePendingButtons();
        Assert.Empty(api.Calls);

        guard.Allowed = true;
        Assert.True(emitter.SendClick(ClickButton.Left).IsSuccess);
        guard.Allowed = false;
        Assert.True(emitter.SendClick(ClickButton.Right).IsSuppressed);
        Assert.Single(api.Calls);
    }

    [Fact]
    public void ProtectionDoesNotPreventReleasingAnOwnedButton()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 1));
        var guard = new TargetGuard();
        var emitter = new WindowsInputEmitter(api, guard);
        emitter.SendClick(ClickButton.Left);

        guard.Allowed = false;
        emitter.ReleasePendingButtons();
        Assert.Equal(0x0004u, Assert.Single(api.Calls[1]).Data.Mouse.Flags);
    }

    [Fact]
    public async Task ControllerDisposeRecoversAPartialPressAfterTransientReleaseFailure()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 1));
        api.Results.Enqueue(new(1, 0, 5));
        var emitter = new WindowsInputEmitter(api, new TargetGuard());
        var controller = new ClickSessionController(emitter);
        controller.Start(new());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionState.Faulted, controller.Snapshot.State);

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        Assert.Equal(3, api.Calls.Count);
        Assert.Equal(0x0004u, Assert.Single(api.Calls[2]).Data.Mouse.Flags);
        Assert.True(emitter.SendClick(ClickButton.Middle).IsSuccess);
    }

    [Fact]
    public async Task ResetFaultKeepsTheSessionBlockedUntilThePendingButtonIsReleased()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 1));
        api.Results.Enqueue(new(1, 0, 5));
        api.Results.Enqueue(new(1, 0, 5));
        await using var controller = new ClickSessionController(new WindowsInputEmitter(api, new TargetGuard()));
        Assert.True(controller.Start(new(Button: ClickButton.Right)));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(controller.ResetFault());
        Assert.Equal(SessionState.Faulted, controller.Snapshot.State);
        Assert.Equal(UserMessageCode.SessionRelease, controller.Snapshot.Failure?.Code);
        Assert.False(controller.Start(new(MaximumClicks: 1)));
        Assert.Equal(3, api.Calls.Count);
        Assert.All(api.Calls.Skip(1), inputs => Assert.Equal(0x0010u, Assert.Single(inputs).Data.Mouse.Flags));

        Assert.True(controller.ResetFault());
        Assert.Equal(SessionState.Idle, controller.Snapshot.State);
        Assert.Null(controller.Snapshot.Failure);
        Assert.Equal(0x0010u, Assert.Single(api.Calls[3]).Data.Mouse.Flags);
        Assert.True(controller.Start(new(Button: ClickButton.Middle, MaximumClicks: 1)));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, controller.Snapshot.SentClicks);
        Assert.Equal(CompletionReason.ClickLimit, controller.Snapshot.CompletionReason);
        Assert.Equal(5, api.Calls.Count);
        Assert.Equal(0x0020u, api.Calls[4][0].Data.Mouse.Flags);
    }

    [Fact]
    public async Task ControllerDisposeReportsPersistentReleaseFailureWithoutLooping()
    {
        var api = new RecordingApi();
        api.Results.Enqueue(new(2, 1));
        api.Results.Enqueue(new(1, 0, 5));
        api.Results.Enqueue(new(1, 0, 5));
        var emitter = new WindowsInputEmitter(api, new TargetGuard());
        var controller = new ClickSessionController(emitter);
        controller.Start(new());
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InputEmissionException>(async () => await controller.DisposeAsync());
        await Assert.ThrowsAsync<InputEmissionException>(async () => await controller.DisposeAsync());

        Assert.Equal(3, api.Calls.Count);
        Assert.Equal(SessionState.Disposed, controller.Snapshot.State);
        Assert.Equal(5, controller.Snapshot.Failure?.InputResult?.ErrorCode);
    }

    private sealed class TargetGuard : IClickTargetGuard
    {
        public bool Allowed { get; set; } = true;
        public bool CanSendClick() => Allowed;
    }

    private sealed class RecordingApi : IWindowsInputApi
    {
        public List<NativeInput[]> Calls { get; } = [];
        public Queue<InputSendResult> Results { get; } = new();

        public InputSendResult Send(NativeInput[] inputs)
        {
            Calls.Add(inputs);
            return Results.TryDequeue(out var result) ? result : new(inputs.Length, inputs.Length);
        }
    }
}
