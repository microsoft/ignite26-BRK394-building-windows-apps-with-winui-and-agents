using System.ComponentModel;
using System.Numerics;
using Hangar.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Hangar.Controls;

public sealed partial class FleetTile : UserControl
{
    public static readonly DependencyProperty AircraftProperty = DependencyProperty.Register(
        nameof(Aircraft), typeof(Aircraft), typeof(FleetTile), new PropertyMetadata(null, OnAircraftChanged));

    private bool _isBreathing;

    public FleetTile()
    {
        InitializeComponent();
        Unloaded += (_, _) => Detach(Aircraft);
        Loaded += (_, _) =>
        {
            Attach(Aircraft);
            Apply(false);
        };
    }

    public Aircraft? Aircraft
    {
        get => (Aircraft?)GetValue(AircraftProperty);
        set => SetValue(AircraftProperty, value);
    }

    /// <summary>The tail TextBlock, used as the source of the tile → Aircraft connected animation.</summary>
    public UIElement TailElement => TailText;

    private static void OnAircraftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tile = (FleetTile)d;
        tile.Detach(e.OldValue as Aircraft);
        tile.Attach(e.NewValue as Aircraft);
        tile.Apply(false);
    }

    private void Attach(Aircraft? aircraft)
    {
        if (aircraft is not null)
        {
            aircraft.PropertyChanged -= OnAircraftPropertyChanged;
            aircraft.PropertyChanged += OnAircraftPropertyChanged;
        }
    }

    private void Detach(Aircraft? aircraft)
    {
        if (aircraft is not null)
        {
            aircraft.PropertyChanged -= OnAircraftPropertyChanged;
        }
    }

    private void OnAircraftPropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply(true);

    private void Apply(bool useTransitions)
    {
        if (Aircraft is not { } aircraft)
        {
            return;
        }

        TailText.Text = aircraft.Tail;
        FlightText.Text = $"{aircraft.Flight} · {aircraft.Type}";
        NoteText.Text = string.IsNullOrEmpty(aircraft.StatusNote) ? $"{aircraft.Station} · {aircraft.NextDeparture}" : aircraft.StatusNote;
        ToolTipService.SetToolTip(NoteText, NoteText.Text);
        AutomationProperties.SetName(this, $"{aircraft.Tail}, {aircraft.Flight}, {aircraft.Status}. {NoteText.Text}");
        VisualStateManager.GoToState(this, aircraft.Status.ToString(), useTransitions);
        SetBreathing(aircraft.Status == AircraftStatus.Attention);
    }

    private void SetBreathing(bool on)
    {
        var halo = ElementCompositionPreview.GetElementVisual(Halo);
        if (on == _isBreathing)
        {
            return;
        }

        _isBreathing = on;
        if (!on)
        {
            halo.StopAnimation("Opacity");
            halo.Opacity = 1f;
            return;
        }

        // Spec: amber "breathes" on a 2.4 s cycle — calm, not alarming.
        var compositor = halo.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        var breathe = compositor.CreateScalarKeyFrameAnimation();
        breathe.InsertKeyFrame(0f, 0.25f);
        breathe.InsertKeyFrame(0.5f, 1f, ease);
        breathe.InsertKeyFrame(1f, 0.25f, ease);
        breathe.Duration = TimeSpan.FromSeconds(2.4);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        halo.StartAnimation("Opacity", breathe);
    }
}
