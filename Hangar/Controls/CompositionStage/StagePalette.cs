using Hangar.Helpers;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Hangar.Controls.CompositionStage;

internal sealed class StagePalette
{
    public StagePalette(CompositionColorBrush cyanCore, CompositionColorBrush cyanGlow, CompositionColorBrush airframe,
        CompositionColorBrush amber, CompositionColorBrush hot, CompositionColorBrush healthy, CompositionColorBrush label,
        CompositionColorBrush canvas, bool highContrast)
    {
        CyanCore = cyanCore;
        CyanGlow = cyanGlow;
        Airframe = airframe;
        Amber = amber;
        Hot = hot;
        Healthy = healthy;
        Label = label;
        Canvas = canvas;
        HighContrast = highContrast;
    }

    public CompositionColorBrush CyanCore { get; }
    public CompositionColorBrush CyanGlow { get; }
    public CompositionColorBrush Airframe { get; }
    public CompositionColorBrush Amber { get; }
    public CompositionColorBrush Hot { get; }
    public CompositionColorBrush Healthy { get; }
    public CompositionColorBrush Label { get; }
    public CompositionColorBrush Canvas { get; }
    public bool HighContrast { get; }

    public static StagePalette Create(FrameworkElement owner, Compositor compositor)
    {
        var accessibility = new AccessibilitySettings();
        var highContrast = accessibility.HighContrast;
        var accent = ThemeRes.Get<Color>(owner, "AccentCyanColor");
        var amber = ThemeRes.Get<Color>(owner, "AttentionColor");
        var healthy = ThemeRes.Get<Color>(owner, "HealthyColor");
        var critical = ThemeRes.Get<Color>(owner, "CriticalColor");
        var canvas = ThemeRes.Get<Color>(owner, "CanvasColor");
        var text = ThemeRes.Brush(owner, "PrimaryTextBrush") is Microsoft.UI.Xaml.Media.SolidColorBrush b ? b.Color : accent;
        var light = !highContrast && owner.ActualTheme == ElementTheme.Light;
        var core = highContrast ? text : light ? Darken(accent, 0.38f) : accent;
        var glow = highContrast ? Transparent(core) : WithAlpha(accent, (byte)(light ? 42 : 52));
        var air = highContrast ? text : WithAlpha(light ? Darken(accent, 0.45f) : accent, light ? (byte)160 : (byte)118);

        return new StagePalette(
            compositor.CreateColorBrush(core),
            compositor.CreateColorBrush(glow),
            compositor.CreateColorBrush(air),
            compositor.CreateColorBrush(amber),
            compositor.CreateColorBrush(critical),
            compositor.CreateColorBrush(healthy),
            compositor.CreateColorBrush(text),
            compositor.CreateColorBrush(canvas),
            highContrast);
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Transparent(Color color) => WithAlpha(color, 0);

    private static Color Darken(Color color, float amount)
    {
        var k = 1 - Math.Clamp(amount, 0, 1);
        return Color.FromArgb(color.A, (byte)(color.R * k), (byte)(color.G * k), (byte)(color.B * k));
    }
}