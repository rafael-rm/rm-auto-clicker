using System.Xml.Linq;
using RM.AutoClicker.Desktop.Localization;

namespace RM.AutoClicker.Tests;

internal static class TestStrings
{
    public static AppStrings Portuguese => Create("pt-BR");

    public static AppStrings Create(string language)
    {
        var resources = new[] { "pt-BR", "en-US", "es" }.ToDictionary(
            culture => culture,
            culture => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Strings", culture, "Resources.resw"))
                .Root!.Elements("data").ToDictionary(element => (string)element.Attribute("name")!, element => element.Element("value")!.Value));
        return new AppStrings(language, (culture, key) => resources[culture].GetValueOrDefault(key));
    }
}
