using System.Windows;

namespace GtaRpAssistant.App.DesignSystem;

public enum ApplicationTheme { Light = 0, Gray = 1 }

public sealed class ThemeService
{
    public ApplicationTheme Current { get; private set; }

    public void Apply(int value)
    {
        var forced = Environment.GetEnvironmentVariable("GTA_RP_UI_THEME");
        var theme = string.Equals(forced, "gray", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Gray
            : Enum.IsDefined(typeof(ApplicationTheme), value) ? (ApplicationTheme)value : ApplicationTheme.Light;
        var dictionaries = System.Windows.Application.Current?.Resources.MergedDictionaries;
        if (dictionaries is null) return;
        var existing = dictionaries.FirstOrDefault(x => x.Source?.OriginalString.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var source = theme == ApplicationTheme.Gray ? "DesignSystem/Tokens/GrayColors.xaml" : "DesignSystem/Tokens/Colors.xaml";
        var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (existing is null) dictionaries.Insert(0, replacement); else dictionaries[dictionaries.IndexOf(existing)] = replacement;
        Current = theme;
    }
}
