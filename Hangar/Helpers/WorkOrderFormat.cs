using Hangar.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hangar.Helpers;

/// <summary>x:Bind function helpers for Work Orders cell and group templates.</summary>
public static class WorkOrderFormat
{
    private static Dictionary<string, (int Open, double Hours)> _groupStats = [];
    private static double _maxGroupHours = 1;

    public static void UpdateGroupStats(IEnumerable<WorkOrder> visible)
    {
        _groupStats = visible
            .GroupBy(w => w.Component)
            .ToDictionary(g => g.Key, g => (g.Count(w => w.Status != WorkOrderStatus.Closed), g.Sum(w => w.Hours)));
        _maxGroupHours = Math.Max(1, _groupStats.Values.Select(v => v.Hours).DefaultIfEmpty(1).Max());
    }

    public static string GroupHours(object key) =>
        key is string k && _groupStats.TryGetValue(k, out var s) ? $"{s.Hours:N0} h" : string.Empty;

    public static string GroupOpen(object key) =>
        key is string k && _groupStats.TryGetValue(k, out var s) ? $"{s.Open} open" : string.Empty;

    public static double GroupBarWidth(object key) =>
        key is string k && _groupStats.TryGetValue(k, out var s) ? Math.Max(4, 160 * s.Hours / _maxGroupHours) : 0;

    public static string Hours(double hours) => $"{hours:0.0}";

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0][..1],
            _ => $"{parts[0][0]}{parts[^1][0]}",
        };
    }

    public static ImageSource? Signature(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        return new BitmapImage(new Uri(path)) { DecodePixelHeight = 56 };
    }

    public static Visibility HasSignature(string? path) =>
        string.IsNullOrEmpty(path) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NoSignature(string? path) =>
        string.IsNullOrEmpty(path) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
