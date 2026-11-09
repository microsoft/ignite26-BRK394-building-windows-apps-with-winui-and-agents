using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Charts;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Hangar.Views;

public sealed partial class AircraftPage : Page
{
    private const int Flights = 50;
    private bool _syncingChips;
    private bool _syncingExplode;

    public AircraftPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RenderAll();
        PreviewKeyDown += OnPagePreviewKeyDown;
        LayoutUpdated += (_, _) => UpdateGlobeSlots();
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
            ExplodeAndSelect(args.Part);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Data.Reset += OnDataReset;
        Data.HeroStatusChanged += OnHeroStatusChanged;
        RenderAll();
        Stage.SetStatus(Data.Hero.Status);
        Stage.Focus(FocusState.Pointer);

        if (DebugFocusPart != EnginePart.None)
        {
            ExplodeAndSelect(DebugFocusPart);
            DebugFocusPart = EnginePart.None;
        }
        else if (DebugExplode)
        {
            Stage.ExplodeEngine();
        }

        DebugExplode = false;
    }

    /// <summary>Debug rehearsal hook (startpage.txt): explode and focus this part on first load.</summary>
    internal static EnginePart DebugFocusPart { get; set; }

    /// <summary>Debug rehearsal hook (startpage.txt "explode"): fly to Engine 2 and explode it on first load.</summary>
    internal static bool DebugExplode { get; set; }

    private void ExplodeAndSelect(EnginePart part)
    {
        if (!Stage.IsExploded)
        {
            Stage.ExplodeEngine();
        }

        Stage.Select(part);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Data.Reset -= OnDataReset;
        Data.HeroStatusChanged -= OnHeroStatusChanged;
    }

    private void OnDataReset(object? sender, EventArgs e) => ResetState();

    private void ResetState()
    {
        CollapseGlobe(focusStage: false);
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
        RenderPart(Stage.SelectedPart);
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
        AirborneText.Text = $"{Data.AirborneCount} airborne";
        LegendAirborneText.Text = $"Airborne fleet ({Data.AirborneCount})";
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

        // Flight 44 (DEN → SEA) carries the hail crew report: pin it once the scrubber has passed it.
        if (index > FleetDataService.HailFlightIndex)
        {
            f.Marker(FleetDataService.HailFlightIndex, vib[FleetDataService.HailFlightIndex], "AttentionBrush", MarkerShape.Diamond, "Flight 44 · hail encounter on climb");
        }

        f.Marker(index, value, over ? "AttentionBrush" : "AccentCyanBrush", index == FleetDataService.HailFlightIndex ? MarkerShape.Diamond : MarkerShape.Circle, $"Flight {index + 1}");

        ScrubValueText.Text = value.ToString("0.00");
        ScrubValueText.Foreground = ThemeRes.Brush(this, over ? "AttentionBrush" : "AccentCyanBrush");
        ScrubFlightText.Text = $"ips · flight {index + 1}";
        LastVibText.Text = $"{value:0.00} ips";
        LastVibText.Foreground = ThemeRes.Brush(this, over ? "AttentionBrush" : "PrimaryTextBrush");
        AutomationProperties.SetName(Scrubber, $"Flight timeline, flight {index + 1}, vibration {value:0.00} ips");

        // Heat overlay tracks vibration: cool below 1.0 ips, fully hot at the 2.31 spike.
        Stage.SetHeat(Math.Clamp((value - 1.0) / (vib.Max() - 1.0), 0, 1));

        Globe.SetFlight(index + 1);
        UpdateHailCallout();
    }

    private void OnScrubberChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RenderChart();
        }
    }

    private void OnExplodeToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingExplode)
        {
            return;
        }

        if (ExplodeToggle.IsChecked == true)
        {
            Stage.ExplodeEngine();
        }
        else
        {
            Stage.CollapseEngine();
        }
    }

    private void OnStageExplodedChanged(object? sender, bool exploded)
    {
        if (ExplodeToggle.IsChecked != exploded)
        {
            _syncingExplode = true;
            ExplodeToggle.IsChecked = exploded;
            _syncingExplode = false;
        }

        RenderPart(Stage.SelectedPart);
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
            ExplodeAndSelect(part);
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
            _ when Stage.IsExploded => ("Engine 2 · expanded", "50+ LEAP-1A parts across four modules. Tap a chip to isolate the nacelle, fan, compressor or turbine; tap empty space or press Esc to collapse."),
            _ => ("Tap the engine to explore it", "Tap Engine 2 or press Explore to separate 50+ LEAP-1A parts; tap a chip to isolate the nacelle, fan, compressor or turbine. Esc collapses."),
        };

        PartTitle.Text = title;
        PartBody.Text = body;
        PartEyebrow.Text = part == EnginePart.None ? "SELECTED PART" : "SELECTED PART · ENGINE 2";
        PartTitle.Foreground = ThemeRes.Brush(this, part == EnginePart.None ? "PrimaryTextBrush" : "AccentCyanBrush");
        WorkOrdersButtonText.Text = part == EnginePart.None ? "Engine 2 work orders" : $"{ComponentFor(part)} work orders";
        BorescopeButton.Visibility = part == EnginePart.Fan && Data.Hero.Status != AircraftStatus.Healthy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBorescopeClick(object sender, RoutedEventArgs e)
    {
        if (!Stage.IsExploded)
        {
            ExplodeAndSelect(EnginePart.Fan);
        }

        Stage.DiveToFault();
    }

    private void OnStageFaultInvoked(object? sender, EventArgs e)
    {
        if (Stage.SelectedPart != EnginePart.Fan)
        {
            Stage.Select(EnginePart.Fan);
        }

        Stage.DiveToFault();
    }

    private void OnStageDiveCompleted(object? sender, EventArgs e) =>
        NavigationService.Navigate(
            typeof(InspectionsPage),
            new InspectionNavigationArgs(Data.Hero.Tail, "Fan blade 14 · leading-edge nick"),
            new DrillInNavigationTransitionInfo());

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

    // ───────────────────────── Fleet globe ─────────────────────────

    private Rect _compactSlot;
    private Rect _expandedSlot;
    private double _globeProgress;
    private CompositeTransform? _stageTransform;
    private bool _globeDragging;

    /// <summary>Programmatically scrub CA 2207's history to a 1-based flight (e.g. from an "Ask Hangar" citation chip).</summary>
    public void ScrubToFlight(int flight) => Scrubber.Value = Math.Clamp(flight, 1, Flights) - 1;

    /// <summary>Expand the fleet globe to fill the stage.</summary>
    public void ExpandGlobe()
    {
        if (Globe.IsExpanded)
        {
            return;
        }

        Globe.SetExpanded(true);
        GlobeBackButton.Visibility = Visibility.Visible;
        GlobeBackButton.Focus(FocusState.Programmatic);
    }

    /// <summary>Collapse the globe back into its card and dive back into the aircraft.</summary>
    public void CollapseGlobe(bool focusStage = true)
    {
        Globe.SetExpanded(false);
        if (focusStage)
        {
            Stage.Focus(FocusState.Programmatic);
        }
    }

    private void UpdateGlobeSlots()
    {
        double w = RootGrid.ActualWidth, h = RootGrid.ActualHeight;
        if (w < 1 || h < 1 || GlobeSlot.ActualWidth < 1 || GlobeSlot.ActualHeight < 1)
        {
            return;
        }

        var compact = GlobeSlot.TransformToVisual(Globe).TransformBounds(new Rect(0, 0, GlobeSlot.ActualWidth, GlobeSlot.ActualHeight));
        var expanded = new Rect(404, 110, Math.Max(200, w - 908), Math.Max(200, h - 220));

        if (compact != _compactSlot || expanded != _expandedSlot)
        {
            _compactSlot = compact;
            _expandedSlot = expanded;
            Globe.SetSlots(compact, expanded);
            GlobeInput.Margin = new Thickness(expanded.X, expanded.Y, 0, 0);
            GlobeInput.Width = expanded.Width;
            GlobeInput.Height = expanded.Height;
            UpdateHailCallout();
        }
    }

    private void UpdateHailCallout()
    {
        bool show = ScrubIndex == FleetDataService.HailFlightIndex;
        double p = _globeProgress;

        // Compact: caption row inside the card, under the globe. Expanded: chip at the top of the globe stage.
        HailCaption.Opacity = show ? 1 - p : 0;
        AutomationProperties.SetAccessibilityView(HailCaption, show && p < 0.5 ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        bool chip = show && p > 0.01;
        HailCallout.Opacity = chip ? p : 0;
        HailCallout.Visibility = chip ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetAccessibilityView(HailCallout, chip && p >= 0.5 ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        if (!chip || _expandedSlot.Width < 1)
        {
            return;
        }

        HailCallout.MaxWidth = Math.Max(160, _expandedSlot.Width);
        HailCallout.Measure(new Size(HailCallout.MaxWidth, double.PositiveInfinity));
        double cw = HailCallout.DesiredSize.Width;

        // Keep the chip clear of the "N2207C" header and status pill at any width.
        double headerBottom = 0;
        foreach (var el in new FrameworkElement[] { HeaderTail, StatusPill })
        {
            if (el.ActualHeight > 0)
            {
                var b = el.TransformToVisual(RootGrid).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                headerBottom = Math.Max(headerBottom, b.Bottom);
            }
        }

        HailCalloutShift.X = _expandedSlot.X + (_expandedSlot.Width - cw) / 2;
        HailCalloutShift.Y = Math.Max(_expandedSlot.Y + 4, headerBottom + 12);
    }

    private void OnGlobeExpandProgressChanged(object? sender, double p)
    {
        if (_stageTransform is null)
        {
            _stageTransform = new CompositeTransform();
            Stage.RenderTransformOrigin = new Point(0.5, 0.5);
            Stage.RenderTransform = _stageTransform;
        }

        // The hologram shrinks and fades toward CA 2207's parked location at SEA on the expanded globe.
        var sea = Globe.ExpandedPointOf("SEA");
        double sx = Stage.ActualWidth / 2, sy = Stage.ActualHeight / 2;
        double scale = 1 - 0.85 * p;
        _stageTransform.ScaleX = scale;
        _stageTransform.ScaleY = scale;
        _stageTransform.TranslateX = (sea.X - sx) * p;
        _stageTransform.TranslateY = (sea.Y - sy) * p;
        Stage.Opacity = Math.Clamp(1 - p * 1.4, 0, 1);
        Stage.IsHitTestVisible = p < 0.5;

        GlobeBackButton.Opacity = p;
        GlobeBackButton.Visibility = p > 0.01 || Globe.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        GlobeInput.IsHitTestVisible = p > 0.5;
        GlobeCard.Opacity = 1;

        // In the expanded state the card trades its mini globe for a legend + FLT 44 detail.
        _globeProgress = p;
        GlobeExpandedPanel.Opacity = Math.Clamp((p - 0.3) / 0.7, 0, 1);
        GlobeExpandedPanel.Visibility = p > 0.01 ? Visibility.Visible : Visibility.Collapsed;
        GlobeHint.Opacity = 1 - p;
        UpdateHailCallout();
    }

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl)
        {
            return;
        }

        if (!Globe.IsExpanded)
        {
            if (e.Key == VirtualKey.G)
            {
                ExpandGlobe();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
            case VirtualKey.G:
                CollapseGlobe();
                break;
            case VirtualKey.Left:
                Globe.Spin(-10, 0);
                break;
            case VirtualKey.Right:
                Globe.Spin(10, 0);
                break;
            case VirtualKey.Up:
                Globe.Spin(0, 8);
                break;
            case VirtualKey.Down:
                Globe.Spin(0, -8);
                break;
            case VirtualKey.Add:
            case (VirtualKey)0xBB:
                Globe.ZoomBy(120);
                break;
            case VirtualKey.Subtract:
            case (VirtualKey)0xBD:
                Globe.ZoomBy(-120);
                break;
            case VirtualKey.Home:
                Globe.ResetView();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnGlobeCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            ExpandGlobe();
            e.Handled = true;
        }
    }

    private void OnGlobeCardPointerEntered(object sender, PointerRoutedEventArgs e) => GlobeHint.Opacity = 1;

    private void OnGlobeCardPointerExited(object sender, PointerRoutedEventArgs e) => GlobeHint.Opacity = 0;

    private void OnGlobeCardTapped(object sender, TappedRoutedEventArgs e)
    {
        ExpandGlobe();
        e.Handled = true;
    }

    private void OnGlobePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _globeDragging = GlobeInput.CapturePointer(e.Pointer);
        Globe.BeginDrag(e.GetCurrentPoint(Globe).Position);
        e.Handled = true;
    }

    private void OnGlobePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_globeDragging)
        {
            Globe.Drag(e.GetCurrentPoint(Globe).Position);
            e.Handled = true;
        }
    }

    private void OnGlobePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_globeDragging)
        {
            return;
        }

        _globeDragging = false;
        Globe.EndDrag();
        GlobeInput.ReleasePointerCapture(e.Pointer);
    }

    private void OnGlobePointerWheel(object sender, PointerRoutedEventArgs e)
    {
        Globe.ZoomBy(e.GetCurrentPoint(Globe).Properties.MouseWheelDelta);
        e.Handled = true;
    }

    private void OnGlobeBackClick(object sender, RoutedEventArgs e) => CollapseGlobe();
}
