using Hangar.Models;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;

namespace Hangar.Reactor;

public enum PageKind { Fleet, Aircraft, WorkOrders, Inspections }

public sealed record AircraftNavigationArgs(string Tail, EnginePart Part = EnginePart.None, bool FromFleetTile = false, int? Flight = null);

public sealed record WorkOrdersNavigationArgs(string Query);

public sealed record InspectionNavigationArgs(string Tail, string Finding, string? NotesPrefill = null);

/// <summary>Current page plus its argument. <see cref="Nonce"/> changes on every navigation so pages remount.</summary>
public sealed record Route(PageKind Page, object? Arg, int Nonce);

/// <summary>Props shared by every page component.</summary>
public sealed record PageProps(object? Arg, bool IsDark, Action<PageKind, object?> Navigate);

/// <summary>Reactor equivalents of the Hangar XAML styles (Themes/Styles.xaml) — theme resources only, no literals.</summary>
public static class Kit
{
    public const string Display = "Segoe UI Variable Display";
    public const string TextFont = "Segoe UI Variable Text";
    public const string MonoFont = "Cascadia Mono, Consolas";
    public const string Icons = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public static ThemeRef B(string key) => Theme.Ref(key);

    public static ThemeRef Primary => B("PrimaryTextBrush");
    public static ThemeRef Secondary => B("SecondaryTextBrush");
    public static ThemeRef Tertiary => B("TertiaryTextBrush");
    public static ThemeRef Cyan => B("AccentCyanBrush");

    // ── Typography ──────────────────────────────────────────────────
    public static TextBlockElement Eyebrow(string text, double size = 12) =>
        TextBlock(text).FontFamily(TextFont).FontSize(size).SemiBold().CharacterSpacing(120).Foreground(Secondary);

    public static TextBlockElement Hero(string text) =>
        TextBlock(text).FontFamily(Display).FontSize(68).SemiBold().LineHeight(72).Foreground(Primary)
            .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);

    public static TextBlockElement PageTitle(string text) =>
        TextBlock(text).FontFamily(Display).FontSize(40).SemiBold().Foreground(Primary);

    public static TextBlockElement SectionTitle(string text) =>
        TextBlock(text).FontFamily(Display).FontSize(20).SemiBold().Foreground(Primary)
            .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);

    public static TextBlockElement KpiValue(string text, double size = 40) =>
        TextBlock(text).FontFamily(Display).FontSize(size).SemiBold().Foreground(Primary);

    public static TextBlockElement Body(string text, double size = 14) =>
        TextBlock(text).FontFamily(TextFont).FontSize(size).Foreground(Secondary).TextWrapping(TextWrapping.Wrap);

    public static TextBlockElement Mono(string text, double size = 13) =>
        TextBlock(text).FontFamily(MonoFont).FontSize(size).Foreground(Primary);

    public static TextBlockElement Tail(string text, double size = 15) =>
        TextBlock(text).FontFamily(MonoFont).FontSize(size).SemiBold().Foreground(Primary);

    public static TextBlockElement Glyph(string glyph, double size = 16) =>
        TextBlock(glyph).FontFamily(Icons).FontSize(size).VAlign(VerticalAlignment.Center);

    // ── Containers ──────────────────────────────────────────────────
    public static BorderElement Card(Element child) =>
        Border(child).Background(B("SurfaceBrush")).BorderBrush(B("SurfaceStrokeBrush"))
            .BorderThickness(1).CornerRadius(12).Padding(20);

    public static BorderElement Glass(Element child) =>
        Border(child).Background(B("GlassBrush")).BorderBrush(B("GlassStrokeBrush"))
            .BorderThickness(1).CornerRadius(16).Padding(24);

    public static BorderElement Divider() => Border(null).Height(1).Background(B("SurfaceStrokeBrush"));

    public static BorderElement VDivider(double height = 24) =>
        Border(null).Width(1).Height(height).Background(B("SurfaceStrokeBrush")).VAlign(VerticalAlignment.Center);

    public static BorderElement Dot(ThemeRef brush, double size = 8) =>
        Border(null).Width(size).Height(size).CornerRadius(size / 2).Background(brush).VAlign(VerticalAlignment.Center);

    /// <summary>Soft-tinted pill with an optional status dot (Styles.xaml PillBorderStyle).</summary>
    public static BorderElement Pill(string text, string colorKey, string softKey, double dot = 6, ThemeRef? textBrush = null, double fontSize = 12) =>
        Border(HStack(6,
                dot > 0 ? Dot(B(colorKey), dot) : Empty(),
                TextBlock(text).FontFamily(TextFont).FontSize(fontSize).SemiBold().Foreground(textBrush ?? B(colorKey))
                    .VAlign(VerticalAlignment.Center)))
            .Background(B(softKey)).BorderBrush(B(colorKey)).BorderThickness(1).CornerRadius(12)
            .Padding(10, 3, 10, 4).HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Center);

    /// <summary>Icon + label content for buttons.</summary>
    public static Element IconLabel(string glyph, string label, double glyphSize = 14, bool semiBold = false) =>
        HStack(8, Glyph(glyph, glyphSize), semiBold ? TextBlock(label).SemiBold().VAlign(VerticalAlignment.Center) : TextBlock(label).VAlign(VerticalAlignment.Center));

    /// <summary>Toggle chip: a Button whose checked state is drawn with the accent-soft fill and accent stroke.</summary>
    public static ButtonElement Chip(Element content, bool isChecked, Action onClick) =>
        Button(content, onClick)
            .Background(isChecked ? B("AccentCyanSoftBrush") : B("SubtleFillColorSecondaryBrush"))
            .BorderBrush(isChecked ? Cyan : B("GlassStrokeBrush"))
            .Foreground(isChecked ? Cyan : Primary)
            .BorderThickness(1).CornerRadius(16).Padding(14, 6);

    // ── Status helpers ──────────────────────────────────────────────
    public static (string Label, string Color, string Soft) Status(AircraftStatus s) => s switch
    {
        AircraftStatus.Healthy => ("Serviceable", "HealthyBrush", "HealthySoftBrush"),
        AircraftStatus.Attention => ("Attention", "AttentionBrush", "AttentionSoftBrush"),
        AircraftStatus.Maintenance => ("In check", "NeutralStatusBrush", "NeutralSoftBrush"),
        _ => ("AOG", "CriticalBrush", "CriticalSoftBrush"),
    };

    public static (string Color, string Soft) Status(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Open => ("AccentCyanBrush", "AccentCyanSoftBrush"),
        WorkOrderStatus.InProgress => ("AttentionBrush", "AttentionSoftBrush"),
        WorkOrderStatus.Deferred => ("CriticalBrush", "CriticalSoftBrush"),
        _ => ("HealthyBrush", "HealthySoftBrush"),
    };

    // ── Win2D colour lookup (theme dictionaries via the canvas's actual theme) ──
    public static Color Col(FrameworkElement fe, string key)
    {
        try
        {
            return Hangar.Helpers.ThemeRes.Get<object>(fe, key) switch
            {
                Color c => c,
                SolidColorBrush b => b.Color,
                GradientBrush g when g.GradientStops.Count > 0 => g.GradientStops[0].Color,
                _ => Microsoft.UI.Colors.Magenta,
            };
        }
        catch
        {
            return Microsoft.UI.Colors.Magenta;
        }
    }

    /// <summary>Resolves a themed brush for APIs that need a concrete Brush (shapes). Theme dictionaries: Default = dark, Light, HighContrast.</summary>
    public static Brush Res(string key, bool isDark)
    {
        var theme = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? "HighContrast" : isDark ? "Default" : "Light";
        foreach (var d in Application.Current.Resources.MergedDictionaries)
        {
            if (d.ThemeDictionaries.TryGetValue(theme, out var td) && td is ResourceDictionary rd && rd.TryGetValue(key, out var v))
                return v as Brush ?? (v is Color c ? new SolidColorBrush(c) : new SolidColorBrush(Microsoft.UI.Colors.Transparent));
        }
        return Application.Current.Resources.TryGetValue(key, out var any) && any is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Magenta);
    }

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
}

/// <summary>Component base with a hook that re-renders when the shared fleet data changes.</summary>
public abstract class HangarComponent<TProps> : Component<TProps>
{
    protected int UseDataVersion()
    {
        var (version, setVersion) = UseState(0);
        var counter = UseRef(0);
        UseEffect(() =>
        {
            var data = Hangar.Services.FleetDataService.Instance;
            EventHandler onChange = (_, _) => setVersion(++counter.Current);
            data.Reset += onChange;
            data.HeroStatusChanged += onChange;
            return () =>
            {
                data.Reset -= onChange;
                data.HeroStatusChanged -= onChange;
            };
        }, Array.Empty<object>());
        return version;
    }
}
