using Microsoft.Windows.ApplicationModel.Resources;

namespace RM.AutoClicker.Desktop.Localization;

internal static class MrtAppStrings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs;

    static MrtAppStrings()
    {
        Catalogs = LoadCatalogs();
    }

    public static AppStrings Create(string language)
    {
        return new AppStrings(language, (requestedLanguage, key) => Catalogs[requestedLanguage].GetValueOrDefault(key.Replace('.', '/')));
    }

    private static Dictionary<string, Dictionary<string, string>> LoadCatalogs()
    {
        var manager = new ResourceManager(ResourceLoader.GetDefaultResourceFilePath());
        var map = manager.MainResourceMap.GetSubtree("Resources");
        var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in new[] { "pt-BR", "en-US", "es" })
        {
            var context = manager.CreateResourceContext();
            context.QualifierValues["Language"] = language;
            var resources = new Dictionary<string, string>(StringComparer.Ordinal);
            for (uint index = 0; index < map.ResourceCount; index++)
            {
                var resource = map.GetValueByIndex(index, context);
                resources.Add(resource.Key, resource.Value.ValueAsString);
            }
            catalogs.Add(language, resources);
        }
        return catalogs;
    }
}
