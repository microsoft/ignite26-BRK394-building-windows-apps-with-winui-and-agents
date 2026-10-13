using Microsoft.UI.Xaml;

namespace Hangar.Helpers;

/// <summary>Static helpers for x:Bind function bindings (used instead of converters).</summary>
public static class Ui
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static string Upper(string? value) => value?.ToUpperInvariant() ?? string.Empty;
}
