using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Abstractions;

public interface IClickSessionController : IAsyncDisposable
{
    ExecutionSnapshot Snapshot { get; }

    Task Completion { get; }

    bool Start(ClickPlan plan);

    Task StopAsync();

    bool ResetFault();
}
