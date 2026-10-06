using System.Numerics;
using Hangar.Helpers;
using Hangar.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;

namespace Hangar.Controls;

/// <summary>
/// 2.5D aircraft stage (documented fallback for Composition Scenes): layered vector art in a
/// PerspectiveTransform3D, orbit by drag/arrow keys, Engine 2 explode on springs, a status ring
/// that breathes amber and turns green once on sign-off.
/// </summary>
public sealed partial class AircraftStage : UserControl
{
    private const double DefaultYaw = -10;
    private const double DefaultPitch = 4;
    private const double MaxYaw = 35;
    private const double MaxPitch = 10;

    private readonly (Button Button, EnginePart Part, float ExplodeX, float ExplodeY, FrameworkElement Label)[] _parts;
    private double _yaw = DefaultYaw;
    private double _pitch = DefaultPitch;
    private bool _isExploded;
    private bool _isBreathing;
    private EnginePart _selected = EnginePart.None;
    private AircraftStatus _status = AircraftStatus.Attention;

    public AircraftStage()
    {
        InitializeComponent();

        _parts =
        [
            (NacelleButton, EnginePart.Nacelle, -30f, -70f, NacelleLabel),
            (TurbineButton, EnginePart.Turbine, -170f, 70f, TurbineLabel),
            (CompressorButton, EnginePart.Compressor, 40f, 90f, CompressorLabel),
            (FanButton, EnginePart.Fan, 230f, 40f, FanLabel),
        ];

        foreach (var p in _parts)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(p.Button, true);
        }

        FloorGrid.Data = BuildFloorGrid();
        FanBlades.Data = BuildFanBlades();
        ApplyOrbit();

        KeyDown += OnStageKeyDown;
        ActualThemeChanged += (_, _) => ApplyStatusBrushes();
        Loaded += (_, _) => ApplyStatusBrushes();
        Unloaded += (_, _) => StopBreathing();
    }

    public event EventHandler<EnginePart>? PartSelected;

    public event EventHandler<bool>? ExplodedChanged;

    public bool IsExploded => _isExploded;

    public EnginePart SelectedPart => _selected;

    public void SetExploded(bool exploded)
    {
        if (exploded == _isExploded)
        {
            return;
        }

        _isExploded = exploded;
        foreach (var p in _parts)
        {
            var visual = ElementCompositionPreview.GetElementVisual(p.Button);
            var spring = visual.Compositor.CreateSpringVector3Animation();
            spring.Target = "Translation";
            spring.FinalValue = exploded ? new Vector3(p.ExplodeX, p.ExplodeY, 0) : Vector3.Zero;
            spring.DampingRatio = exploded ? 0.62f : 0.8f;
            spring.Period = TimeSpan.FromMilliseconds(exploded ? 70 : 55);
            visual.StartAnimation("Translation", spring);

            Fade(p.Label, exploded ? 1 : 0, exploded ? 260 : 120, exploded ? 180 : 0);
        }

        // The nacelle shell becomes translucent so the core reads as "lifted out".
        Fade(NacelleShell, exploded ? 0.45 : 1, 300, 0);
        ExplodedChanged?.Invoke(this, exploded);
    }

    public void Select(EnginePart part)
    {
        _selected = part;
        var soft = ThemeRes.Brush(this, "AccentCyanSoftBrush");
        var line = ThemeRes.Brush(this, "AccentCyanBrush");
        var none = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var p in _parts)
        {
            var on = p.Part == part;
            p.Button.Background = on ? soft : none;
            p.Button.BorderBrush = on ? line : none;
        }
    }

    /// <summary>Heat glow on the hot section, 0 (cool) to 1 (spike).</summary>
    public void SetHeat(double heat)
    {
        heat = Math.Clamp(heat, 0, 1);
        NacelleHeat.Opacity = heat * 0.85;
        TurbineHeat.Opacity = heat;
    }

    public void SetStatus(AircraftStatus status)
    {
        _status = status;
        ApplyStatusBrushes();
    }

    public void ResetView()
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        AnimateOrbitTo(DefaultYaw, DefaultPitch, 450);
        SetExploded(false);
        Select(EnginePart.None);
    }

    /// <summary>The single celebratory moment: green burst + a 15° turn and settle.</summary>
    public void Celebrate()
    {
        var sb = new Storyboard();
        var scale = new ScaleTransform { CenterX = Ring.ActualWidth / 2, CenterY = Ring.ActualHeight / 2 };
        Burst.RenderTransform = scale;

        sb.Children.Add(Anim(Burst, "Opacity", 1, 0, 900, null));
        sb.Children.Add(Anim(scale, "ScaleX", 0.9, 1.6, 900, new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4 }));
        sb.Children.Add(Anim(scale, "ScaleY", 0.9, 1.6, 900, new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4 }));

        var turn = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
        var start = _yaw;
        turn.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = start });
        turn.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(700),
            Value = start + 15,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
        turn.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(1600),
            Value = start,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 },
        });
        Storyboard.SetTarget(turn, AirframeTransform);
        Storyboard.SetTargetProperty(turn, "RotationY");
        sb.Children.Add(turn);
        sb.Completed += (_, _) => AirframeTransform.RotationY = _yaw;
        sb.Begin();
    }

    private void ApplyStatusBrushes()
    {
        var healthy = _status == AircraftStatus.Healthy;
        RingLine.Stroke = ThemeRes.Brush(this, healthy ? "HealthyBrush" : "AttentionBrush");
        RingGlow.Stroke = ThemeRes.Brush(this, healthy ? "HealthySoftBrush" : "AttentionSoftBrush");
        RingLine.StrokeDashArray = healthy ? null : [6, 3];
        Burst.Stroke = ThemeRes.Brush(this, "HealthyBrush");
        if (healthy)
        {
            StopBreathing();
            SetHeat(0);
        }
        else
        {
            StartBreathing();
        }
    }

    private void StartBreathing()
    {
        if (_isBreathing || !IsLoaded)
        {
            return;
        }

        _isBreathing = true;
        var glow = ElementCompositionPreview.GetElementVisual(RingGlow);
        var compositor = glow.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        var breathe = compositor.CreateScalarKeyFrameAnimation();
        breathe.InsertKeyFrame(0f, 0.25f);
        breathe.InsertKeyFrame(0.5f, 1f, ease);
        breathe.InsertKeyFrame(1f, 0.25f, ease);
        breathe.Duration = TimeSpan.FromSeconds(2.4);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        glow.StartAnimation("Opacity", breathe);
    }

    private void StopBreathing()
    {
        if (!_isBreathing)
        {
            return;
        }

        _isBreathing = false;
        var glow = ElementCompositionPreview.GetElementVisual(RingGlow);
        glow.StopAnimation("Opacity");
        glow.Opacity = 1f;
    }

    private void OnPartClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<EnginePart>(tag, out var part))
        {
            var next = part == _selected ? EnginePart.None : part;
            Select(next);
            PartSelected?.Invoke(this, next);
        }
    }

    private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e) => Focus(FocusState.Pointer);

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        _yaw = Math.Clamp(_yaw + (e.Delta.Translation.X * 0.12), -MaxYaw, MaxYaw);
        _pitch = Math.Clamp(_pitch - (e.Delta.Translation.Y * 0.06), -MaxPitch, MaxPitch);
        ApplyOrbit();
    }

    private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
    }

    private void OnStageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left: _yaw = Math.Max(-MaxYaw, _yaw - 5); break;
            case VirtualKey.Right: _yaw = Math.Min(MaxYaw, _yaw + 5); break;
            case VirtualKey.Up: _pitch = Math.Min(MaxPitch, _pitch + 2); break;
            case VirtualKey.Down: _pitch = Math.Max(-MaxPitch, _pitch - 2); break;
            case VirtualKey.Home: ResetView(); e.Handled = true; return;
            default: return;
        }

        e.Handled = true;
        AnimateOrbitTo(_yaw, _pitch, 220);
    }

    private void ApplyOrbit()
    {
        AirframeTransform.RotationY = _yaw;
        AirframeTransform.RotationX = _pitch;
        ShadowScale.ScaleX = 0.82 + (0.18 * Math.Cos(_yaw * Math.PI / 180 * 1.6));
        ShadowScale.ScaleY = 1 + (Math.Abs(_yaw) / MaxYaw * 0.35);
    }

    private void AnimateOrbitTo(double yaw, double pitch, int ms)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        sb.Children.Add(Anim(AirframeTransform, "RotationY", null, yaw, ms, ease, dependent: true));
        sb.Children.Add(Anim(AirframeTransform, "RotationX", null, pitch, ms, ease, dependent: true));
        sb.Completed += (_, _) => ApplyOrbit();
        sb.Begin();
        ShadowScale.ScaleX = 0.82 + (0.18 * Math.Cos(yaw * Math.PI / 180 * 1.6));
        ShadowScale.ScaleY = 1 + (Math.Abs(yaw) / MaxYaw * 0.35);
    }

    private static void Fade(UIElement target, double to, int ms, int delayMs)
    {
        var sb = new Storyboard();
        var a = Anim(target, "Opacity", null, to, ms, new CubicEase { EasingMode = EasingMode.EaseOut });
        a.BeginTime = TimeSpan.FromMilliseconds(delayMs);
        sb.Children.Add(a);
        sb.Begin();
    }

    private static DoubleAnimation Anim(DependencyObject target, string property, double? from, double to, int ms, EasingFunctionBase? ease, bool dependent = true)
    {
        var a = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = ease,
            EnableDependentAnimation = dependent,
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        return a;
    }

    private static Geometry BuildFloorGrid()
    {
        // Hangar floor markings converging on a vanishing point behind the aircraft.
        var group = new GeometryGroup();
        var vp = new Point(800, 360);
        const double top = 700;
        const double bottom = 900;
        for (var i = -12; i <= 12; i++)
        {
            var x0 = 800 + (i * 150);
            var t = (top - vp.Y) / (bottom - vp.Y);
            var x1 = vp.X + ((x0 - vp.X) * t);
            group.Children.Add(new LineGeometry { StartPoint = new Point(x1, top), EndPoint = new Point(x0, bottom) });
        }

        var y = top;
        var step = 10.0;
        while (y <= bottom)
        {
            group.Children.Add(new LineGeometry { StartPoint = new Point(0, y), EndPoint = new Point(1600, y) });
            y += step;
            step *= 1.45;
        }

        return group;
    }

    private static Geometry BuildFanBlades()
    {
        var group = new GeometryGroup();
        const double cx = 40, cy = 74, rx = 28, ry = 70;
        const int blades = 22;
        for (var i = 0; i < blades; i++)
        {
            var a = i * 2 * Math.PI / blades;
            var a2 = a + 0.18;
            var inner = new Point(cx + (Math.Cos(a) * rx * 0.28), cy + (Math.Sin(a) * ry * 0.28));
            var outer = new Point(cx + (Math.Cos(a2) * rx * 0.94), cy + (Math.Sin(a2) * ry * 0.94));
            group.Children.Add(new LineGeometry { StartPoint = inner, EndPoint = outer });
        }

        return group;
    }
}
