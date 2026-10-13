using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using System.Numerics;
using static Hangar.Reactor.Kit;
using static Microsoft.UI.Reactor.Factories;

namespace Hangar.Reactor.Pages;

public sealed class FleetPage : HangarComponent<PageProps>
{
    private static FleetDataService Data => FleetDataService.Instance;

    public override Element Render()
    {
        var p = Props;
        UseDataVersion();
        var (width, setWidth) = UseState(1200.0);

        var hero = Data.Hero;
        var healthy = hero.Status == AircraftStatus.Healthy;
        var needs = Data.Fleet.Count(a => a.Status == AircraftStatus.Attention);
        var heroText = needs switch { 0 => "All clear for first wave", 1 => "1 aircraft needs you", _ => $"{needs} aircraft need you" };
        var subhead = healthy
            ? "CA 2207 signed off by Maya Okafor. Engine 2 within limits — departing 06:15 as scheduled."
            : "CA 2207 landed with an Engine 2 vibration exceedance. Next departure 06:15, 180 passengers.";

        var spotlight = Button(
                HStack(10, Glyph("\uE709", 16), TextBlock("Open CA 2207").SemiBold().FontSize(15).VAlign(VerticalAlignment.Center), Glyph("\uE72A", 14)),
                () => p.Navigate(PageKind.Aircraft, new AircraftNavigationArgs(FleetDataService.HeroTail)))
            .AccentButton().CornerRadius(22).Padding(20, 10, 20, 11)
            .VAlign(VerticalAlignment.Bottom).Margin(0, 0, 0, 6)
            .AutomationId("FleetSpotlightButton").AutomationName("Open CA 2207");

        var header = Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
        [
            VStack(4,
                Eyebrow($"NIGHT SHIFT · SEA HANGAR 3 · {Data.Fleet.Count} AIRCRAFT"),
                Hero(heroText).AutomationId("FleetHeroText"),
                Body(subhead, 16).Margin(0, 2, 0, 0)),
            spotlight.Grid(0, 1),
        ]);

        // ── KPIs ──
        Element Kpi(string id, string label, string value, string caption, IReadOnlyList<double> spark, string color) =>
            Border(VStack(0,
                    Eyebrow(label, 11),
                    KpiValue(value).Margin(0, 2, 0, 0),
                    TextBlock(caption).FontSize(12).Foreground(Secondary).TextTrimming(TextTrimming.CharacterEllipsis),
                    Charts.Sparkline(spark, color, p.IsDark).Height(56).Margin(0, 10, 0, 0)))
                .Background(B("SurfaceBrush")).BorderBrush(B("SurfaceStrokeBrush")).BorderThickness(1).CornerRadius(12)
                .Padding(20, 18, 20, 12).AutomationId(id).AutomationName($"{label}: {value}");

        var kpis = Grid([GridSize.Star(1), GridSize.Star(1), GridSize.Star(1), GridSize.Star(1)], [GridSize.Auto],
        [
            Kpi("KpiAvailability", "FLEET AVAILABILITY", $"{Data.TodayAvailability:0.0}%", $"Target {FleetDataService.AvailabilityTarget:0.0}% · 30 days", Data.AvailabilitySpark, "AccentCyanColor"),
            Kpi("KpiAog", "AIRCRAFT ON GROUND", Data.Fleet.Count(a => a.Status == AircraftStatus.Aog).ToString(), "N2231C · awaiting part at SEA", Data.AogSpark, "CriticalColor").Grid(0, 1),
            Kpi("KpiOpenWorkOrders", "OPEN WORK ORDERS", Data.OpenWorkOrderCount.ToString("N0"), "14 due before first wave", Data.OpenWoSpark, "AccentCyanColor").Grid(0, 2),
            Kpi("KpiOnTime", "ON-TIME DEPARTURE", "94%", "+1.2 pts vs. last week", Data.OnTimeSpark, "HealthyColor").Grid(0, 3),
        ]).Set(g => g.ColumnSpacing = 16);

        // ── Availability chart card ──
        var legend = HStack(20,
            HStack(8, Border(null).Width(14).Height(3).CornerRadius(1.5).Background(Cyan).VAlign(VerticalAlignment.Center),
                TextBlock("Availability").FontSize(13).Foreground(Secondary)),
            HStack(8, TextBlock("- - -").FontSize(11).Foreground(B("ChartTargetBrush")).VAlign(VerticalAlignment.Center),
                TextBlock($"Target {FleetDataService.AvailabilityTarget:0}%").FontSize(13).Foreground(Secondary)),
            healthy
                ? Pill($"Today {Data.TodayAvailability:0.0}% · CA 2207 released", "HealthyBrush", "HealthySoftBrush", 6, fontSize: 11)
                : Pill($"Today {Data.TodayAvailability:0.0}% · CA 2207 held", "AttentionBrush", "AttentionSoftBrush", 6, fontSize: 11))
            .VAlign(VerticalAlignment.Top);

        var availability = Border(VStack(16,
                Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
                [
                    VStack(2,
                        SectionTitle("Fleet availability · 30 days"),
                        TextBlock("Percentage of the fleet serviceable at first departure wave").FontSize(13).Foreground(Secondary)),
                    legend.Grid(0, 1),
                ]),
                Charts.AvailabilityChart(Data.Availability30, healthy, p.IsDark).Height(260).AutomationId("AvailabilityChart")))
            .Background(B("SurfaceBrush")).BorderBrush(B("SurfaceStrokeBrush")).BorderThickness(1).CornerRadius(12)
            .Padding(24, 20, 24, 16);

        // ── Fleet tiles ──
        int Count(AircraftStatus s) => Data.Fleet.Count(a => a.Status == s);
        var counts = HStack(20,
            TextBlock($"{Count(AircraftStatus.Healthy)} serviceable").FontSize(13).Foreground(Secondary),
            TextBlock($"{Count(AircraftStatus.Attention)} needs attention").FontSize(13).Foreground(B("AttentionBrush")),
            TextBlock($"{Count(AircraftStatus.Maintenance)} in check").FontSize(13).Foreground(Secondary),
            TextBlock($"{Count(AircraftStatus.Aog)} AOG").FontSize(13).Foreground(B("CriticalBrush")))
            .VAlign(VerticalAlignment.Bottom);

        const double gap = 12, minTile = 232;
        var cols = Math.Max(1, (int)((width + gap) / (minTile + gap)));
        var fleet = Data.Fleet.ToList();
        var rows = (fleet.Count + cols - 1) / cols;
        var tiles = fleet.Select((a, i) => Component<FleetTile, FleetTileProps>(
                new FleetTileProps(a.Tail, a.Flight, a.Type, a.Status, string.IsNullOrEmpty(a.StatusNote) ? $"{a.Station} · {a.NextDeparture}" : a.StatusNote,
                    () => p.Navigate(PageKind.Aircraft, new AircraftNavigationArgs(a.Tail, FromFleetTile: true)), p.IsDark))
                .WithKey(a.Tail).Grid(i / cols, i % cols))
            .ToArray();
        var tileGrid = Grid(Enumerable.Repeat(GridSize.Star(1), cols).ToArray(), Enumerable.Repeat(GridSize.Px(148), rows).ToArray(), tiles)
            .Set(g => { g.ColumnSpacing = gap; g.RowSpacing = gap; })
            .OnSizeChanged((_, e) =>
            {
                if (Math.Abs(e.NewSize.Width - width) > 1) setWidth(e.NewSize.Width);
            });

        var page = VStack(24,
                header,
                kpis,
                availability,
                VStack(16,
                    Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto], [SectionTitle("Fleet"), counts.Grid(0, 1)]).Margin(0, 8, 0, 0),
                    tileGrid))
            .Padding(40, 28, 40, 40);

        return ScrollViewer(page).AutomationId("FleetScroll");
    }
}

public sealed record FleetTileProps(string Tail, string Flight, string Type, AircraftStatus Status, string Note, Action OnClick, bool IsDark);

public sealed class FleetTile : Component<FleetTileProps>
{
    private const string PlanePath = "M50,4 C54,4 56,10 56,18 L56,38 L94,58 L94,65 L56,54 L56,78 L68,88 L68,94 L50,89 L32,94 L32,88 L44,78 L44,54 L6,65 L6,58 L44,38 L44,18 C44,10 46,4 50,4 Z";

    public override Element Render()
    {
        var p = Props;
        var (label, color, soft) = Status(p.Status);
        var attention = p.Status == AircraftStatus.Attention;

        Element halo = attention
            ? Border(null).Background(B("AttentionSoftBrush")).BorderBrush(B("AttentionBrush")).BorderThickness(2).CornerRadius(12)
                .Margin(-1).IsHitTestVisible(false)
                .Keyframes("breathe", "halo", kb => kb.Duration(2400).Loop()
                    .At(0f, opacity: 0.25f).At(0.5f, opacity: 1f).At(1f, opacity: 0.25f))
            : Empty();

        var silhouette = Path2D()
            .Fill(Res(attention ? "AttentionBrush" : "AircraftBodyBrush", p.IsDark))
            .Set(path =>
            {
                path.Data = PathDataParser.Parse(PlanePath);
                path.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform;
            })
            .Width(52).Height(52).Opacity(attention ? 0.35 : 0.16)
            .Rotation(90).CenterPoint(new Vector3(26, 26, 0))
            .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom).Margin(0, 0, 10, -6)
            .IsHitTestVisible(false);

        var ring = Grid([GridSize.Px(18)], [GridSize.Px(18)],
        [
            Border(null).BorderBrush(B(color)).BorderThickness(2).CornerRadius(9),
            Border(null).Background(B(color)).Width(8).Height(8).CornerRadius(4),
        ]).VAlign(VerticalAlignment.Center);

        var body = Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto, GridSize.Auto, GridSize.Star(1), GridSize.Auto],
        [
            Tail(p.Tail, 18),
            ring.Grid(0, 1),
            Body($"{p.Flight} · {p.Type}").Grid(1, 0, 1, 2),
            TextBlock(p.Note).FontSize(12).Foreground(Secondary).MaxLines(1).TextTrimming(TextTrimming.CharacterEllipsis)
                .VAlign(VerticalAlignment.Bottom).Margin(0, 0, 0, 10).Grid(2, 0, 1, 2),
            Pill(label, color, soft, 0, p.Status == AircraftStatus.Maintenance ? Secondary : null).Grid(3, 0, 1, 2),
        ]).Set(g => g.RowSpacing = 2).Padding(18, 16, 18, 16);

        var content = Grid([GridSize.Star(1)], [GridSize.Star(1)], [halo, silhouette, body]);

        return Button(content, p.OnClick)
            .ApplyStyle("TileButtonStyle")
            .AutomationId(p.Tail)
            .AutomationName($"{p.Tail}, {p.Flight}, {label}")
            .ToolTip($"Open {p.Tail}");
    }
}
