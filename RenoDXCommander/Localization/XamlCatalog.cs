using System.ComponentModel;
using System.Xml.Linq;
using Microsoft.UI.Xaml.Data;

namespace RenoDXCommander.Localization;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class XamlCatalog : INotifyPropertyChanged
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Resources = new(() =>
        new[] { Loc.English, Loc.SimplifiedChinese }.ToDictionary(language => language, language =>
        {
            using var stream = typeof(XamlCatalog).Assembly.GetManifestResourceStream(
                $"RenoDXCommander.Localization.{language}.resw")!;
            return XDocument.Load(stream).Root!.Elements("data").ToDictionary(
                entry => Alias(entry.Attribute("name")!.Value), entry => entry.Element("value")!.Value);
        }));

    private static string Alias(string key)
    {
        var parts = key.Split('.', 2);
        return parts[0] + "_" + (parts[1].EndsWith("ToolTip", StringComparison.Ordinal) ? "ToolTip" : parts[1]);
    }
    public string this[string key] => Resources.Value[Loc.Language][key];
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new("Item[]"));
}
