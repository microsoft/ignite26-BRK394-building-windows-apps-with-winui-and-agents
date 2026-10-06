using Hangar.Controls;
using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Hangar.Views;

public sealed partial class FleetPage : Page
{
    public FleetPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RenderCharts();
    }

    public FleetDataService Data => FleetDataService.Instance;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is "reset")
        {
            Scroller.ChangeView(null, 0, null, true);
        }

        RefreshSummary();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Data.Reset += OnDataChanged;
        Data.HeroStatusChanged += OnDataChanged;
        RenderCharts();
        RefreshSummary();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Data.Reset -= OnDataChanged;
        Data.HeroStatusChanged -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        RenderCharts();
        RefreshSummary();
    }

    private void RenderCharts()
    {
        var healthy = Data.Hero.Status == AircraftStatus.Healthy;

        AvailabilityKpi.SetSpark(Data.AvailabilitySpark, "AccentCyanBrush", "AccentAreaBrush");
        AogKpi.SetSpark(Data.AogSpark, "CriticalBrush", "CriticalAreaBrush");
        OpenWoKpi.SetSpark(Data.OpenWoSpark, "AccentCyanBrush", "AccentAreaBrush");
        OnTimeKpi.SetSpark(Data.OnTimeSpark, "HealthyBrush", "HealthyAreaBrush");

        var series = Data.Availability30.ToList();
        if (healthy)
        {
            series[^1] = 97.6;
        }

        var today = series[^1];
        var factory = ChartFactory.For(AvailabilityChart, this).Axes(series.Count, 94, 100, visible: true, ySpacing: 1);
        factory.Area(series, "AccentCyanBrush", "AccentAreaBrush", 2.5, "Availability");
        factory.Reference(series.Count, 98, "ChartTargetBrush", title: "Target");
        factory.Marker(series.Count - 1, today, healthy ? "HealthyBrush" : "AttentionBrush", title: "Today");

        var pillBrush = healthy ? "HealthyBrush" : "AttentionBrush";
        DipPill.Background = ThemeRes.Brush(this, healthy ? "HealthySoftBrush" : "AttentionSoftBrush");
        DipPill.BorderBrush = ThemeRes.Brush(this, pillBrush);
        DipText.Foreground = ThemeRes.Brush(this, pillBrush);
        if (DipPill.Child is StackPanel { Children: [Microsoft.UI.Xaml.Shapes.Ellipse dot, ..] })
        {
            dot.Fill = ThemeRes.Brush(this, pillBrush);
        }

        DipText.Text = healthy ? $"Today {today:0.0}% · CA 2207 released" : $"Today {today:0.0}% · CA 2207 held";
    }

    private void RefreshSummary()
    {
        var fleet = Data.Fleet;
        var attention = fleet.Count(a => a.Status == AircraftStatus.Attention);
        var healthyCount = fleet.Count(a => a.Status == AircraftStatus.Healthy);
        var maintenance = fleet.Count(a => a.Status == AircraftStatus.Maintenance);
        var aog = fleet.Count(a => a.Status == AircraftStatus.Aog);

        HealthyCount.Text = $"{healthyCount} serviceable";
        AttentionCount.Text = attention > 0 ? $"{attention} needs attention" : string.Empty;
        MaintenanceCount.Text = $"{maintenance} in check";
        AogCount.Text = $"{aog} AOG";

        OpenWoKpi.Value = Data.OpenWorkOrderCount.ToString("N0");

        if (attention == 0)
        {
            HeadlineText.Text = "All clear for first wave";
            SubheadText.Text = "CA 2207 signed off by Maya Okafor. Engine 2 within limits — departing 06:15 as scheduled.";
            SpotlightButton.Visibility = Visibility.Collapsed;
            AvailabilityKpi.Value = "97.6%";
        }
        else
        {
            HeadlineText.Text = attention == 1 ? "1 aircraft needs you" : $"{attention} aircraft need you";
            SubheadText.Text = "CA 2207 landed with an Engine 2 vibration exceedance. Next departure 06:15, 180 passengers.";
            SpotlightButton.Visibility = Visibility.Visible;
            AvailabilityKpi.Value = "95.2%";
        }
    }

    private void OnSpotlightClick(object sender, RoutedEventArgs e)
    {
        var index = Data.Fleet.IndexOf(Data.Hero);
        var tile = FleetRepeater.TryGetElement(index) is Button { Content: FleetTile t } ? t : null;
        OpenAircraft(Data.Hero, tile);
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Aircraft aircraft } button)
        {
            OpenAircraft(aircraft, button.Content as FleetTile);
        }
    }

    private static void OpenAircraft(Aircraft aircraft, FleetTile? tile)
    {
        if (tile is not null)
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("tail", tile.TailElement);
        }

        NavigationService.Navigate(
            typeof(AircraftPage),
            new AircraftNavigationArgs(aircraft.Tail, FromFleetTile: tile is not null),
            new SuppressNavigationTransitionInfo());
    }
}
