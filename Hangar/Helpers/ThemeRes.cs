using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Hangar.Helpers;

/// <summary>
/// Resolves theme-dictionary resources for the element's <em>actual</em> theme. Application.Current.Resources[key]
/// only honours the app-level theme, which is wrong once the shell toggles Night/Day shift at runtime.
/// </summary>
public static class ThemeRes
{
    private static readonly AccessibilitySettings Accessibility = new();

    public static string ThemeKey(FrameworkElement element) =>
        Accessibility.HighContrast ? "HighContrast" : element.ActualTheme == ElementTheme.Light ? "Light" : "Default";

    public static T Get<T>(FrameworkElement element, string key)
    {
        var themeKey = ThemeKey(element);
        if (TryFindThemed(Application.Current.Resources, themeKey, key, out var value) && value is T typed)
        {
            return typed;
        }

        return (T)Application.Current.Resources[key];
    }

    public static Brush Brush(FrameworkElement element, string key) => Get<Brush>(element, key);

    // Only inspects ThemeDictionaries: a plain TryGetValue would silently resolve through the
    // app-level theme (Dark) and shadow the element's Light/HighContrast value.
    private static bool TryFindThemed(ResourceDictionary dictionary, string themeKey, string key, out object? value)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themed)
            && themed is ResourceDictionary themedDictionary
            && themedDictionary.TryGetValue(key, out value))
        {
            return true;
        }

        for (var i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (TryFindThemed(dictionary.MergedDictionaries[i], themeKey, key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }
}
