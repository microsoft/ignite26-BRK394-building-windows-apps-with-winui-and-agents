using Hangar.Helpers;
using Hangar.Models;
using Hangar.Reactor.Hologram;
using Hangar.Services;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Hangar.Reactor.Kit;
using static Microsoft.UI.Reactor.Advanced.Factories;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Reactor.Advanced.Win2D;

namespace Hangar.Reactor.Pages;

public sealed class AircraftPage : HangarComponent<PageProps>
{
    private const int Flights = 50;
    private static FleetDataService Data => FleetDataService.Instance;

#if DEBUG
    /// <summary>Debug rehearsal hook: explode and focus this part on first load.</summary>
    internal static EnginePart DebugFocusPart { get; set; }
#endif

    public override Element Render()
    {
        var p = Props;
        var propsRef = UseRef(p);
        propsRef.Current = p;
        UseDataVersion();

        var engineRef = UseRef<HologramEngine?>(null);
        engineRef.Current ??= new HologramEngine { FocusPadding = new Thickness(380, 150, 480, 120) };
        var engine = engineRef.Current;
        var canvasRef = UseRef<CanvasControl?>(null);
        var stageRef = UseRef<FrameworkElement?>(null);

        var (scrub, setScrub) = UseState(Flights - 1);
        var (part, setPart) = UseState(EnginePart.None);
        var (exploded, setExploded) = UseState(false);

        void ExplodeAndSelect(EnginePart target)
        {
            if (!engine.IsExploded) engine.ExplodeEngine();
            engine.Select(target);
        }

        // Engine events → Reactor state; data events → engine.
        UseEffect(() =>
        {
            EventHandler<EnginePart> onPart = (_, x) => setPart(x);
            EventHandler<bool> onExploded = (_, x) => { setExploded(x); setPart(engine.SelectedPart); };
            EventHandler onFault = (_, _) =>
            {
                if (engine.SelectedPart != EnginePart.Fan) engine.Select(EnginePart.Fan);
                engine.DiveToFault();
            };
            EventHandler onDive = (_, _) => propsRef.Current.Navigate(PageKind.Inspections,
                new InspectionNavigationArgs(Data.Hero.Tail, "Fan blade 14 · leading-edge nick"));
            EventHandler onReset = (_, _) =>
            {
                setScrub(Flights - 1);
                engine.SetExploded(false);
                engine.Select(EnginePart.None);
                engine.ResetView();
                engine.SetStatus(Data.Hero.Status);
            };
            EventHandler onHero = (_, _) =>
            {
                engine.SetStatus(Data.Hero.Status);
                if (Data.Hero.Status == AircraftStatus.Healthy) engine.Celebrate();
            };
            engine.PartSelected += onPart;
            engine.ExplodedChanged += onExploded;
            engine.FaultInvoked += onFault;
            engine.DiveCompleted += onDive;
            Data.Reset += onReset;
            Data.HeroStatusChanged += onHero;
            return () =>
            {
                engine.PartSelected -= onPart;
                engine.ExplodedChanged -= onExploded;
                engine.FaultInvoked -= onFault;
                engine.DiveCompleted -= onDive;
                Data.Reset -= onReset;
                Data.HeroStatusChanged -= onHero;
            };
        }, Array.Empty<object>());

        // Render loop: invalidate the canvas every frame while mounted; follow theme changes.
        UseEffect(() =>
        {
            var lastTheme = ElementTheme.Default;
            EventHandler<object> onFrame = (_, _) =>
            {
                if (stageRef.Current is { } host && host.ActualTheme != lastTheme)
                {
                    lastTheme = host.ActualTheme;
                    engine.RefreshTheme();
                }

                canvasRef.Current?.Invalidate();
            };
            CompositionTarget.Rendering += onFrame;
            return () =>
            {
                CompositionTarget.Rendering -= onFrame;
                engine.Detach();
            };
        }, Array.Empty<object>());

        var hero = Data.Hero;
        var healthy = hero.Status == AircraftStatus.Healthy;
        engine.SetStatus(hero.Status);

        var vib = Data.Vibration;
        var index = Math.Clamp(scrub, 0, Flights - 1);
        var value = vib[index];
        var over = value >= FleetDataService.VibrationThreshold;
        engine.SetHeat(Math.Clamp((value - 1.0) / (vib.Max() - 1.0), 0, 1));

        // ── Stage (full bleed) ──
        var stage = Grid([GridSize.Star(1)], [GridSize.Star(1)],
            [
                Win2DCanvas((ds, _) =>
                    {
                        if (canvasRef.Current is { } c) engine.Draw(c, ds);
                    })
                    .UseSharedDevice()
                    .Set(c =>
                    {
                        canvasRef.Current = c;
                        c.IsHitTestVisible = false;
                    }),
            ])
            .Background(B("SubtleFillColorTransparentBrush"))
            .IsTabStop(true)
            .AutomationId("HologramStage")
            .OnMount(fe =>
            {
                if (stageRef.Current == fe) return;
                stageRef.Current = fe;
                engine.Attach(fe);
                engine.SetStatus(Data.Hero.Status);
                fe.Focus(FocusState.Pointer);
                if (propsRef.Current.Arg is AircraftNavigationArgs { Part: not EnginePart.None } args)
                    ExplodeAndSelect(args.Part);
#if DEBUG
                if (DebugFocusPart != EnginePart.None)
                {
                    ExplodeAndSelect(DebugFocusPart);
                    DebugFocusPart = EnginePart.None;
                }
#endif
            })
            .OnPointerPressed((s, e) => engine.PointerPressed((UIElement)s, e))
            .OnPointerMoved((s, e) => engine.PointerMoved((UIElement)s, e))
            .OnPointerReleased((s, e) => engine.PointerReleased((UIElement)s, e))
            .OnPointerExited((_, _) => engine.PointerExited())
            .OnPointerCanceled((_, _) => engine.PointerCanceled())
            .OnPointerWheelChanged((s, e) => engine.PointerWheelChanged((UIElement)s, e))
            .OnKeyDown((_, e) =>
            {
                if (engine.HandleKey(e.Key)) e.Handled = true;
            });

        // ── Header ──
        var header = VStack(2,
            Eyebrow(Ui.Upper($"{hero.Flight} · {hero.Type} · {hero.Station} → JFK {hero.NextDeparture}")),
            HStack(18,
                TextBlock(hero.Tail).FontFamily(MonoFont).FontSize(56).SemiBold().Foreground(Primary)
                    .AutomationId("AircraftTailHeader")
                    .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1),
                (healthy
                    ? Pill("Released to service", "HealthyBrush", "HealthySoftBrush", 8)
                    : Pill("Engine 2 vibration · hold", "AttentionBrush", "AttentionSoftBrush", 8))
                    .AutomationId("AircraftStatusPill")));

        // ── Vitals ──
        Element Stat(string label, string val, string? sub = null) =>
            VStack(2,
                Eyebrow(label, 11),
                KpiValue(val, 26),
                sub is null ? Empty() : TextBlock(sub).FontSize(13).Foreground(Tertiary));

        Element Row(string label, string val, ThemeRef? brush = null) =>
            Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
            [
                TextBlock(label).FontSize(14).Foreground(Primary),
                Mono(val, 13).Foreground(brush ?? Primary).Grid(0, 1),
            ]);

        var component = ComponentFor(part);
        var vitals = Glass(VStack(20,
                SectionTitle("Vitals"),
                Grid([GridSize.Star(1), GridSize.Star(1)], [GridSize.Auto, GridSize.Auto],
                [
                    Stat("CYCLES", hero.Cycles.ToString("N0")),
                    Stat("FLIGHT HOURS", hero.FlightHours.ToString("N0")).Grid(0, 1),
                    Stat("NEXT CHECK", $"{hero.DaysToNextCheck} d", hero.NextCheck).Grid(1, 0),
                    Stat("STATION", hero.Station, "Hangar 3 · Bay 2").Grid(1, 1),
                ]).Set(g => { g.ColumnSpacing = 16; g.RowSpacing = 18; }),
                Border(null).Height(1).Background(B("GlassStrokeBrush")),
                VStack(10,
                    Eyebrow("ENGINE 2 · LEAP-1A", 11),
                    Row("N1 vibration · last", $"{vib[^1]:0.00} ips", vib[^1] >= FleetDataService.VibrationThreshold ? B("AttentionBrush") : null),
                    Row("Limit", $"{FleetDataService.VibrationThreshold:0.00} ips"),
                    Row("ESN", "598-214")),
                Button(IconLabel("\uE9D5", part == EnginePart.None ? "Engine 2 work orders" : $"{component} work orders"),
                        () => p.Navigate(PageKind.WorkOrders, new WorkOrdersNavigationArgs(part == EnginePart.None ? FleetDataService.HeroTail : component)))
                    .HAlign(HorizontalAlignment.Stretch).Padding(16, 10).CornerRadius(8)
                    .AutomationId("AircraftWorkOrdersButton")).Padding(24))
            .VAlign(VerticalAlignment.Top).AutomationId("VitalsPanel");

        // ── Vibration + selected part ──
        var (title, body) = part switch
        {
            EnginePart.Nacelle => ("Nacelle & thrust reverser", "Inlet and cowl clean on walkaround. No acoustic panel damage. 1 open work order."),
            EnginePart.Fan => ("Fan · stage 1 blade 14", "Leading-edge nick suspected — the likely source of the N1 vibration spike. Borescope inspection required before 06:15."),
            EnginePart.Compressor => ("High-pressure compressor", "Borescope history clean. Trend within the fleet band for the last 50 flights."),
            EnginePart.Turbine => ("HP turbine", "EGT margin 42 °C. Blade coating within limits at the last shop visit."),
            _ when exploded => ("Engine 2 · exploded", "50+ LEAP-1A parts across four modules. Tap a chip to isolate the nacelle, fan, compressor or turbine; tap empty space or press Esc to collapse."),
            _ => ("Tap the engine to explode it", "Tap Engine 2 or press Explode to separate 50+ LEAP-1A parts; tap a chip to isolate the nacelle, fan, compressor or turbine. Esc collapses."),
        };
        var showBorescope = part == EnginePart.Fan && !healthy;

        Element LegendItem(Element swatch, string text) =>
            HStack(6, swatch.VAlign(VerticalAlignment.Center), TextBlock(text).FontSize(12).Foreground(Secondary));

        var vibration = Glass(VStack(14,
                Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
                [
                    VStack(2, Eyebrow("ENGINE 2 · N1 VIBRATION", 11), SectionTitle("Last 50 flights")),
                    VStack(0,
                        KpiValue(value.ToString("0.00"), 30).Foreground(over ? B("AttentionBrush") : Cyan).HAlign(HorizontalAlignment.Right)
                            .AutomationId("ScrubValueText"),
                        Mono($"ips · flight {index + 1}", 12).Foreground(Tertiary).HAlign(HorizontalAlignment.Right))
                        .VAlign(VerticalAlignment.Bottom).Grid(0, 1),
                ]),
                Charts.VibrationChart(vib, Data.FleetAverageVibration, index, p.IsDark).Height(240).AutomationId("VibrationChart"),
                HStack(18,
                    LegendItem(Border(null).Width(14).Height(3).Background(Cyan), "CA 2207 Eng 2"),
                    LegendItem(Border(null).Width(14).Height(10).Background(B("ChartBandBrush")), "Fleet average"),
                    LegendItem(TextBlock("- -").FontSize(12).SemiBold().Foreground(B("CriticalBrush")), $"Limit {FleetDataService.VibrationThreshold:0.0}")),
                Border(null).Height(1).Background(B("GlassStrokeBrush")),
                VStack(6,
                    Eyebrow(part == EnginePart.None ? "SELECTED PART" : "SELECTED PART · ENGINE 2", 11),
                    SectionTitle(title).Foreground(part == EnginePart.None ? Primary : Cyan).AutomationId("PartTitle"),
                    Body(body),
                    showBorescope
                        ? Button(IconLabel("\uE890", "Borescope blade 14", 14, true), () =>
                            {
                                if (!engine.IsExploded) ExplodeAndSelect(EnginePart.Fan);
                                engine.DiveToFault();
                            })
                            .AccentButton().Padding(16, 10).CornerRadius(8).Margin(0, 8, 0, 0).HAlign(HorizontalAlignment.Stretch)
                            .AutomationId("AircraftBorescopeButton")
                        : Empty())).Padding(24, 22, 24, 20))
            .VAlign(VerticalAlignment.Top).AutomationId("VibrationPanel");

        // ── Dock ──
        Element PartChip(EnginePart x, string label) =>
            Chip(TextBlock(label), part == x, () =>
            {
                if (part == x) engine.Select(EnginePart.None);
                else ExplodeAndSelect(x);
            }).AutomationId($"PartChip{x}").AutomationName(label);

        Element DockDivider() => Border(null).Width(1).Margin(0, 4).Background(B("GlassStrokeBrush"));

        var dock = Border(HStack(16,
                Chip(IconLabel("\uF156", "Explode", 14, true), exploded, () =>
                    {
                        if (engine.IsExploded) engine.CollapseEngine();
                        else engine.ExplodeEngine();
                    })
                    .Padding(16, 8).AutomationId("ExplodeToggle").AutomationName(exploded ? "Collapse engine" : "Explode engine"),
                HStack(6,
                    PartChip(EnginePart.Nacelle, "Nacelle"),
                    PartChip(EnginePart.Fan, "Fan"),
                    PartChip(EnginePart.Compressor, "Compressor"),
                    PartChip(EnginePart.Turbine, "Turbine")).VAlign(VerticalAlignment.Center),
                DockDivider(),
                HStack(12,
                    Glyph("\uE81C", 14).Foreground(Secondary),
                    Mono("FLT 1", 12).Foreground(Tertiary).VAlign(VerticalAlignment.Center),
                    Slider(index, 0, Flights - 1, v => setScrub((int)Math.Round(v)))
                        .StepFrequency(1).ThumbToolTip(false).Width(360).VAlign(VerticalAlignment.Center)
                        .AutomationId("FlightScrubber")
                        .AutomationName($"Flight timeline, flight {index + 1}, vibration {value:0.00} ips"),
                    Mono("FLT 50", 12).Foreground(Tertiary).VAlign(VerticalAlignment.Center)).VAlign(VerticalAlignment.Center),
                DockDivider(),
                Button(Glyph("\uE72C", 14), () => engine.ResetView()).Padding(10, 8).CornerRadius(8)
                    .ToolTip("Reset view").AutomationId("ResetViewButton").AutomationName("Reset view")))
            .Background(B("GlassBrush")).BorderBrush(B("GlassStrokeBrush")).BorderThickness(1).CornerRadius(24)
            .Padding(16, 10).HAlign(HorizontalAlignment.Center).AutomationId("StageDock");

        var overlay = Grid([GridSize.Px(340), GridSize.Star(1), GridSize.Px(440)], [GridSize.Auto, GridSize.Star(1), GridSize.Auto],
            [
                header.Grid(0, 0, 1, 2),
                vitals.Grid(1, 0),
                vibration.Grid(0, 2, 2, 1),
                dock.Grid(2, 0, 1, 3),
            ])
            .Set(g => { g.ColumnSpacing = 24; g.RowSpacing = 20; })
            .Padding(40, 28, 40, 28);

        return Grid([GridSize.Star(1)], [GridSize.Star(1)], [stage, overlay]);
    }

    private static string ComponentFor(EnginePart part) => part switch
    {
        EnginePart.Fan => "Fan blade",
        EnginePart.Turbine => "HPT blade",
        EnginePart.Compressor => "Compressor",
        EnginePart.Nacelle => "Nacelle",
        _ => "Engine 2",
    };
}
