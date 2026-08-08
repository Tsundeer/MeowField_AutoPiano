using System.Windows;
using System.Windows.Media;
using MeowField.App.Controls;

namespace MeowField.App;

public static class ThemeService
{
    public static void Apply(bool dark)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) == true || dictionary.Source?.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (existing is null) dictionaries.Insert(0, replacement);
        else
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = replacement;
        }
        if (System.Windows.Application.Current.MainWindow is { } window) InvalidateTree(window);
    }

    private static void InvalidateTree(DependencyObject element)
    {
        if (element is MidiTimelineControl timeline) timeline.InvalidateVisual();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) InvalidateTree(VisualTreeHelper.GetChild(element, index));
    }
}
