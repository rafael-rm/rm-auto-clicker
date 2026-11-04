using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.ViewModels;
using RM.AutoClicker.Tests;

namespace RM.AutoClicker.Desktop.Tests;

public sealed class MinimizeToTrayPreferenceTests
{
    [Fact]
    public void ReopeningRestoresThePreferenceWithoutSavingAgain()
    {
        var store = new Settings(new(MinimizeToTray: true));
        var model = CreateModel(store);

        Assert.True(model.MinimizeToTray);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task ChangingThePreferenceDoesNotSaveInvalidOrUnconfirmedPlanEdits()
    {
        var original = new AppSettings(Plan: new(ClicksPerSecond: 12), ThemeIndex: 2,
            Language: "es", Hotkey: new(0x41, HotkeyModifiers.Control));
        var store = new Settings(original);
        var model = CreateModel(store);
        model.ClicksPerSecond = double.NaN;
        model.ThemeIndex = 1;

        model.MinimizeToTray = true;
        await model.MinimizeToTraySaveTask;

        Assert.Equal(original with { MinimizeToTray = true }, store.Value);
        Assert.True(double.IsNaN(model.ClicksPerSecond));
        Assert.Equal(1, model.ThemeIndex);
        Assert.True(CreateModel(store).MinimizeToTray);
    }

    [Fact]
    public async Task FailedSaveRestoresTheSavedBehaviorAndAllowsRetry()
    {
        var store = new Settings(new()) { FailSaving = true };
        var diagnostics = new List<string>();
        var model = CreateModel(store, diagnostics);

        model.MinimizeToTray = true;
        await model.MinimizeToTraySaveTask;

        Assert.False(model.MinimizeToTray);
        Assert.False(store.Value.MinimizeToTray);
        Assert.True(model.HasMessage);
        Assert.Contains("MinimizeToTraySaveFailed", diagnostics);

        store.FailSaving = false;
        model.MinimizeToTray = true;
        await model.MinimizeToTraySaveTask;

        Assert.True(store.Value.MinimizeToTray);
        Assert.True(model.MinimizeToTray);
        Assert.False(model.HasMessage);
    }

    [Fact]
    public async Task ProtectedSettingsDoNotAllowAnUnpersistedBehaviorChange()
    {
        var store = new Settings(new()) { CanSave = false };
        var model = CreateModel(store);

        model.MinimizeToTray = true;
        await model.MinimizeToTraySaveTask;

        Assert.False(model.CanChangeMinimizeToTray);
        Assert.False(model.MinimizeToTray);
        Assert.Equal(0, store.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializedLanguageBehaviorAndPlanSavesPreserveAllConfirmedValues(bool behaviorFirst)
    {
        var store = new Settings(new(Plan: new(ClicksPerSecond: 12))) { BlockFirstSave = true };
        var model = CreateModel(store);
        if (behaviorFirst) model.MinimizeToTray = true;
        else model.LanguageIndex = 3;
        var firstSave = behaviorFirst ? model.MinimizeToTraySaveTask : model.LanguageSaveTask;
        Task saving = Task.CompletedTask;
        try
        {
            await store.FirstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (behaviorFirst) model.LanguageIndex = 3;
            else model.MinimizeToTray = true;
            model.ClicksPerSecond = 14;
            saving = model.SaveAsync();

            Assert.Equal(1, store.Attempts);
            Assert.False(saving.IsCompleted);
        }
        finally
        {
            store.ResumeFirstSave.TrySetResult();
            await Task.WhenAll(firstSave, saving, model.LanguageSaveTask, model.MinimizeToTraySaveTask)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(store.Value.MinimizeToTray);
        Assert.Equal("es", store.Value.Language);
        Assert.Equal(14, store.Value.Plan!.ClicksPerSecond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEarlierSaveDoesNotRevertTheLatestChoice(bool failFirstSave)
    {
        var store = new Settings(new()) { BlockFirstSave = true, FailFirstSave = failFirstSave };
        var model = CreateModel(store);
        model.MinimizeToTray = true;
        var firstSave = model.MinimizeToTraySaveTask;
        try
        {
            await store.FirstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.MinimizeToTray = false;
        }
        finally
        {
            store.ResumeFirstSave.TrySetResult();
            await Task.WhenAll(firstSave, model.MinimizeToTraySaveTask).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(model.MinimizeToTray);
        Assert.False(store.Value.MinimizeToTray);
    }

    private static MainViewModel CreateModel(Settings settings, List<string>? diagnostics = null) =>
        new(TestStrings.Portuguese, new IdleController(), settings, false,
            writeDiagnostic: (name, _) => diagnostics?.Add(name));

    private sealed class Settings(AppSettings initial) : ISettingsStore
    {
        public AppSettings Value { get; private set; } = initial;
        public bool CanSave { get; init; } = true;
        public bool FailSaving { get; set; }
        public bool BlockFirstSave { get; init; }
        public bool FailFirstSave { get; init; }
        public int Attempts { get; private set; }
        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SettingsLoadResult Load() => new(Value, CanSave: CanSave);

        public async Task SaveAsync(AppSettings settings)
        {
            var attempt = ++Attempts;
            if (BlockFirstSave && attempt == 1)
            {
                FirstSaveEntered.TrySetResult();
                await ResumeFirstSave.Task;
            }
            if (FailSaving || FailFirstSave && attempt == 1)
                throw new IOException("Simulated preference save failure.");
            Value = settings;
        }
    }

    private sealed class IdleController : IClickSessionController
    {
        public ExecutionSnapshot Snapshot { get; } = new(Guid.Empty, SessionState.Idle, 0,
            TimeSpan.Zero, TimeSpan.Zero, CompletionReason.None, null);
        public Task Completion => Task.CompletedTask;
        public bool Start(ClickPlan plan) => throw new InvalidOperationException("These tests must never start clicking.");
        public Task StopAsync() => Task.CompletedTask;
        public bool ResetFault() => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
