using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Charts;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Hangar.Views;

public sealed partial class AircraftPage : Page
{
    private const int Flights = 50;
    private bool _syncingChips;

    public AircraftPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RenderAll();
    }

    public FleetDataService Data => FleetDataService.Instance;

    private int ScrubIndex => (int)Math.Round(Scrubber.Value);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is "reset")
        {
            ResetState();
        }

        ConnectedAnimationService.GetForCurrentView().GetAnimation("tail")?.TryStart(HeaderTail);

        if (e.Parameter is AircraftNavigationArgs { Part: not EnginePart.None } args)
        {
            // "Show on aircraft" from Work Orders: explode and isolate the part.
            Stage.SetExploded(true);
            Stage.Select(args.Part);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Data.Reset += OnDataReset;
        Data.HeroStatusChanged += OnHeroStatusChanged;
        RenderAll();
        Stage.SetStatus(Data.Hero.Status);
        Stage.Focus(FocusState.Programmatic);

        if (DebugFocusPart != EnginePart.None)
        {
            Stage.SetExploded(true);
            Stage.Select(DebugFocusPart);
            DebugFocusPart = EnginePart.None;
        }
    }

    /// <summary>Debug rehearsal hook (startpage.txt): explode and focus this part on first load.</summary>
    internal static EnginePart DebugFocusPart { get; set; }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Data.Reset -= OnDataReset;
        Data.HeroStatusChanged -= OnHeroStatusChanged;
    }

    private void OnDataReset(object? sender, EventArgs e) => ResetState();

    private void ResetState()
    {
        Scrubber.Value = Flights - 1;
        Stage.SetExploded(false);
        Stage.Select(EnginePart.None);
        Stage.ResetView();
        Stage.SetStatus(Data.Hero.Status);
        RenderAll();
    }

    private void OnHeroStatusChanged(object? sender, EventArgs e)
    {
        Stage.SetStatus(Data.Hero.Status);
        RenderStatus();
        if (Data.Hero.Status == AircraftStatus.Healthy)
        {
            Stage.Celebrate();
        }
    }

    private void RenderAll()
    {
        var hero = Data.Hero;
        EyebrowText.Text = Ui.Upper($"{hero.Flight} · {hero.Type} · {hero.Station} → JFK {hero.NextDeparture}");
        HeaderTail.Text = hero.Tail;
        CyclesText.Text = hero.Cycles.ToString("N0");
        HoursText.Text = hero.FlightHours.ToString("N0");
        NextCheckText.Text = $"{hero.DaysToNextCheck} d";
        NextCheckKind.Text = hero.NextCheck;
        StationText.Text = hero.Station;
        RenderStatus();
        RenderChart();
        RenderPart(Stage.SelectedPart);
    }

    private void RenderStatus()
    {
        var healthy = Data.Hero.Status == AircraftStatus.Healthy;
        var key = healthy ? "HealthyBrush" : "AttentionBrush";
        StatusPill.Background = ThemeRes.Brush(this, healthy ? "HealthySoftBrush" : "AttentionSoftBrush");
        StatusPill.BorderBrush = ThemeRes.Brush(this, key);
        StatusDot.Fill = ThemeRes.Brush(this, key);
        StatusText.Foreground = ThemeRes.Brush(this, key);
        StatusText.Text = healthy ? "Released to service" : "Engine 2 vibration · hold";
        AutomationProperties.SetName(StatusPill, StatusText.Text);
    }

    private void RenderChart()
    {
        var index = Math.Clamp(ScrubIndex, 0, Flights - 1);
        var vib = Data.Vibration;
        var avg = Data.FleetAverageVibration;
        var value = vib[index];
        var over = value >= FleetDataService.VibrationThreshold;

        var f = ChartFactory.For(VibrationChart, this).Axes(Flights, 0, 2.6, visible: true, ySpacing: 0.5);
        f.Area(avg.Select(v => v + 0.22), "ChartTargetBrush", "ChartBandBrush", 0, "Fleet average band");
        f.Line(avg, "ChartTargetBrush", 1, StrokeDashStyle.Dot, "Fleet average");
        f.Reference(Flights, FleetDataService.VibrationThreshold, "CriticalBrush", StrokeDashStyle.Dash, "Limit 1.8");
        f.Line(vib.Take(index + 1), "AccentCyanBrush", 2.5, StrokeDashStyle.Solid, "CA 2207 Engine 2");

        // The excursion above the limit is redrawn in amber so the spike reads at a glance.
        var spikeStart = Enumerable.Range(0, index + 1).FirstOrDefault(i => vib[i] >= FleetDataService.VibrationThreshold, -1);
        if (spikeStart > 0)
        {
            var amber = vib.Take(index + 1).Select((v, i) => i >= spikeStart - 1 ? v : double.NaN).ToList();
            f.Line(amber, "AttentionBrush", 3, StrokeDashStyle.Solid, "Excursion");
        }

        f.Marker(index, value, over ? "AttentionBrush" : "AccentCyanBrush", MarkerShape.Circle, $"Flight {index + 1}");

        ScrubValueText.Text = value.ToString("0.00");
        ScrubValueText.Foreground = ThemeRes.Brush(this, over ? "AttentionBrush" : "AccentCyanBrush");
        ScrubFlightText.Text = $"ips · flight {index + 1}";
        LastVibText.Text = $"{value:0.00} ips";
        LastVibText.Foreground = ThemeRes.Brush(this, over ? "AttentionBrush" : "PrimaryTextBrush");
        AutomationProperties.SetName(Scrubber, $"Flight timeline, flight {index + 1}, vibration {value:0.00} ips");

        // Heat overlay tracks vibration: cool below 1.0 ips, fully hot at the 2.31 spike.
        Stage.SetHeat(Math.Clamp((value - 1.0) / (vib.Max() - 1.0), 0, 1));
    }

    private void OnScrubberChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RenderChart();
        }
    }

    private void OnExplodeToggled(object sender, RoutedEventArgs e) => Stage.SetExploded(ExplodeToggle.IsChecked == true);

    private void OnStageExplodedChanged(object? sender, bool exploded)
    {
        if (ExplodeToggle.IsChecked != exploded)
        {
            ExplodeToggle.IsChecked = exploded;
        }
    }

    private void OnPartChipClick(object sender, RoutedEventArgs e)
    {
        if (_syncingChips || sender is not ToggleButton { Tag: string tag } chip)
        {
            return;
        }

        var part = Enum.Parse<EnginePart>(tag);
        if (chip.IsChecked == true)
        {
            Stage.SetExploded(true);
            Stage.Select(part);
        }
        else
        {
            Stage.Select(EnginePart.None);
        }
    }

    private void OnStagePartSelected(object? sender, EnginePart part) => RenderPart(part);

    private void RenderPart(EnginePart part)
    {
        _syncingChips = true;
        foreach (var child in PartChips.Children)
        {
            if (child is ToggleButton { Tag: string tag } chip)
            {
                chip.IsChecked = tag == part.ToString();
            }
        }

        _syncingChips = false;

        var (title, body) = part switch
        {
            EnginePart.Nacelle => ("Nacelle & thrust reverser", "Inlet and cowl clean on walkaround. No acoustic panel damage. 1 open work order."),
            EnginePart.Fan => ("Fan · stage 1 blade 14", "Leading-edge nick suspected — the likely source of the N1 vibration spike. Borescope inspection required before 06:15."),
            EnginePart.Compressor => ("High-pressure compressor", "Borescope history clean. Trend within the fleet band for the last 50 flights."),
            EnginePart.Turbine => ("HP turbine", "EGT margin 42 °C. Blade coating within limits at the last shop visit."),
            _ => ("Tap a part to isolate it", "Explode Engine 2 to separate the nacelle, fan, compressor and turbine."),
        };

        PartTitle.Text = title;
        PartBody.Text = body;
        PartEyebrow.Text = part == EnginePart.None ? "SELECTED PART" : "SELECTED PART · ENGINE 2";
        PartTitle.Foreground = ThemeRes.Brush(this, part == EnginePart.None ? "PrimaryTextBrush" : "AccentCyanBrush");
        WorkOrdersButtonText.Text = part == EnginePart.None ? "Engine 2 work orders" : $"{ComponentFor(part)} work orders";
    }

    private static string ComponentFor(EnginePart part) => part switch
    {
        EnginePart.Fan => "Fan blade",
        EnginePart.Turbine => "HPT blade",
        EnginePart.Compressor => "Compressor",
        EnginePart.Nacelle => "Nacelle",
        _ => "Engine 2",
    };

    private void OnWorkOrdersClick(object sender, RoutedEventArgs e)
    {
        var part = Stage.SelectedPart;
        NavigationService.Navigate(typeof(WorkOrdersPage), new WorkOrdersNavigationArgs(part == EnginePart.None ? FleetDataService.HeroTail : ComponentFor(part)));
    }

    private void OnResetViewClick(object sender, RoutedEventArgs e) => Stage.ResetView();
}
