using System.Globalization;
using RM.AutoClicker.Core.Abstractions;

namespace RM.AutoClicker.Desktop.Localization;

public sealed class AppStrings(string language, Func<string, string, string?> lookup) : IAppStrings
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    public string Language { get; } = language;
    public static IReadOnlyList<string> Preferences { get; } = Array.AsReadOnly(new[] { "auto", "pt-BR", "en-US", "es" });

    public static string ResolveLanguage(string preference, string systemLanguage)
    {
        var candidate = preference == "auto" ? systemLanguage : preference;
        return candidate.Split('-')[0].ToLowerInvariant() switch
        {
            "pt" => "pt-BR",
            "es" => "es",
            _ => "en-US"
        };
    }

    public string Get(string key, params object[] arguments)
    {
        if (!_cache.TryGetValue(key, out var template))
        {
            template = lookup(Language, key);
            if (string.IsNullOrWhiteSpace(template))
                template = lookup("en-US", key);
            if (string.IsNullOrWhiteSpace(template))
                throw new InvalidOperationException($"Missing localized resource: {key}.");
            _cache.Add(key, template);
        }
        return arguments.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }
}
