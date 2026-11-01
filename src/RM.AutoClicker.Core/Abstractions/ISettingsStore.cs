using RM.AutoClicker.Core.Models;

namespace RM.AutoClicker.Core.Abstractions;

public interface ISettingsStore
{
    SettingsLoadResult Load();

    Task SaveAsync(AppSettings settings);
}
