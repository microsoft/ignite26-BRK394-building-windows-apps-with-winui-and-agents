using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Hangar.Reactor;

/// <summary>Hangar brand palette, theme-aware (Runway Cyan accent, Safety Amber for attention only).</summary>
internal static class Palette
{
    private static SolidColorBrush Hex(uint rgb) =>
        new(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private static readonly SolidColorBrush AccentDark = Hex(0x3AD1F0), AccentLight = Hex(0x0E9FC0);
    private static readonly SolidColorBrush AmberDark = Hex(0xFFB020), AmberLight = Hex(0xC77700);
    private static readonly SolidColorBrush CriticalDark = Hex(0xFF5A5F), CriticalLight = Hex(0xD13438);
    private static readonly SolidColorBrush HealthyDark = Hex(0x4ADE80), HealthyLight = Hex(0x14834A);
    private static readonly SolidColorBrush MaintDark = Hex(0x9AA4B2), MaintLight = Hex(0x5B6573);

    public static SolidColorBrush Accent(bool dark) => dark ? AccentDark : AccentLight;
    public static SolidColorBrush Amber(bool dark) => dark ? AmberDark : AmberLight;
    public static SolidColorBrush Critical(bool dark) => dark ? CriticalDark : CriticalLight;
    public static SolidColorBrush Healthy(bool dark) => dark ? HealthyDark : HealthyLight;
    public static SolidColorBrush Maintenance(bool dark) => dark ? MaintDark : MaintLight;

    public static SolidColorBrush Tint(bool dark, uint rgb, byte alpha)
    {
        var b = Hex(rgb);
        var c = b.Color;
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }
}
