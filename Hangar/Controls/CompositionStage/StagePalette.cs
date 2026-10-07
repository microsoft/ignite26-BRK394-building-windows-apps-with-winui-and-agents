using Hangar.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Hangar.Controls.CompositionStage;

internal sealed class StagePalette
{
    public StagePalette(Color cyanCore, Color cyanGlow, Color airframe, Color amber, Color hot,
        Color healthy, Color label, Color canvas, bool highContrast, Compositor compositor)
    {
        CyanCoreColor = cyanCore;
        CyanGlowColor = cyanGlow;
        AirframeColor = airframe;
        AmberColor = amber;
        HotColor = hot;
        HealthyColor = healthy;
        LabelColor = label;
        CanvasColor = canvas;
        HighContrast = highContrast;
        CyanCore = compositor.CreateColorBrush(cyanCore);
        CyanGlow = compositor.CreateColorBrush(cyanGlow);
        Airframe = compositor.CreateColorBrush(airframe);
        Amber = compositor.CreateColorBrush(amber);
        Hot = compositor.CreateColorBrush(hot);
        Healthy = compositor.CreateColorBrush(healthy);
        Label = compositor.CreateColorBrush(label);
        Canvas = compositor.CreateColorBrush(canvas);
        CacheKey = $"{(highContrast ? "hc" : "std")}-{cyanCore}-{cyanGlow}-{airframe}-{amber}-{hot}-{healthy}-{canvas}";
    }

    public CompositionColorBrush CyanCore { get; }
    public CompositionColorBrush CyanGlow { get; }
    public CompositionColorBrush Airframe { get; }
    public CompositionColorBrush Amber { get; }
    public CompositionColorBrush Hot { get; }
    public CompositionColorBrush Healthy { get; }
    public CompositionColorBrush Label { get; }
    public CompositionColorBrush Canvas { get; }
    public Color CyanCoreColor { get; }
    public Color CyanGlowColor { get; }
    public Color AirframeColor { get; }
    public Color AmberColor { get; }
    public Color HotColor { get; set; }
    public Color HealthyColor { get; }
    public Color LabelColor { get; }
    public Color CanvasColor { get; }
    public bool HighContrast { get; }
    public string CacheKey { get; }

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
        var glow = highContrast ? Transparent(core) : WithAlpha(accent, (byte)(light ? 42 : 62));
        var air = highContrast ? text : WithAlpha(light ? Darken(accent, 0.45f) : accent, light ? (byte)170 : (byte)128);
        return new StagePalette(core, glow, air, amber, critical, healthy, text, canvas, highContrast, compositor);
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Transparent(Color color) => WithAlpha(color, 0);

    private static Color Darken(Color color, float amount)
    {
        var k = 1 - Math.Clamp(amount, 0, 1);
        return Color.FromArgb(color.A, (byte)(color.R * k), (byte)(color.G * k), (byte)(color.B * k));
    }
}
