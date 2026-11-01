namespace RM.AutoClicker.Core.Abstractions;

public interface IAppStrings
{
    string Language { get; }
    string Get(string key, params object[] arguments);
}
