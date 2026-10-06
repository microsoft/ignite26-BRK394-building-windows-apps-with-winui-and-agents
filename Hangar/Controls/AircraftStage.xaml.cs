using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hangar.Helpers;
using Hangar.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Hangar.Controls;

/// <summary>
/// Holographic wireframe A321neo rendered with Win2D: perspective camera with orbit / zoom-to-part, additive bloom,
/// a scanning sweep, spinning fan, blinking hot blade and a spring-driven "explode" of Engine 2.
/// (Composition Scenes was not viable on this SDK — see docs/SCENES-SPIKE.md.)
/// </summary>
public sealed partial class AircraftStage : UserControl
{
    private const float DefaultYaw = 0.78f;
    private const float DefaultPitch = 0.30f;
    private const float DefaultDistance = 46f;
    private const float FieldOfView = 0.62f;
    private const float FloorY = -4.3f;
    private static readonly Vector3 DefaultTarget = new(-0.5f, -0.6f, 1.4f);
    private static readonly Vector3 Engine1Origin = new(WireMesh.Engine2Origin.X, WireMesh.Engine2Origin.Y, -WireMesh.Engine2Origin.Z);
    private static readonly EnginePart[] Parts = [EnginePart.Nacelle, EnginePart.Fan, EnginePart.Compressor, EnginePart.Turbine];

    private readonly List<WireLine> _lines = WireMesh.BuildA321();
    private readonly Dictionary<EnginePart, Vector3> _centroids = [];
    private readonly List<(Vector2 A, Vector2 B, EnginePart Part)> _hitSegments = [];
    private readonly Dictionary<EnginePart, Rect> _partBounds = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AccessibilitySettings _accessibility = new();
    private readonly UISettings _uiSettings = new();
    private bool _animationsEnabled = true;

    // Camera: current values ease toward goals.
    private float _yaw = DefaultYaw, _pitch = DefaultPitch, _distance = DefaultDistance;
    private float _goalYaw = DefaultYaw, _goalPitch = DefaultPitch, _goalDistance = DefaultDistance;
    private Vector3 _target = DefaultTarget;
    private float _idleAmount;
    private double _lastInteraction = -10;

    // Animation state.
    private double _lastFrame;
    private float _explode, _explodeVelocity, _explodeGoal;
    private float _spin, _spinRate = 0.45f;
    private float _heat = 0.6f;
    private float _selectionAge = 10f;
    private double _celebrateStart = -100;
    private AircraftStatus _status = AircraftStatus.Attention;
    private EnginePart _hover = EnginePart.None;

    // Input.
    private bool _pressed, _dragging;
    private Point _pressPoint, _lastPoint;

    // Theme cache.
    private Color _cyan, _amber, _green, _canvas, _text, _heatColor;
    private bool _light, _highContrast;
    private Vector2? _hotTip;

    private CanvasRenderTarget? _target2D;
    private CanvasTextFormat? _labelFormat, _subFormat;

    public AircraftStage()
    {
        InitializeComponent();
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        foreach (var part in Parts)
        {
            var pts = _lines.Where(l => l.IsEngine2 && l.Part == part).SelectMany(l => l.Points).ToList();
            _centroids[part] = pts.Aggregate(Vector3.Zero, (a, p) => a + p) / pts.Count;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RefreshTheme();
    }

    public event EventHandler<EnginePart>? PartSelected;

    public event EventHandler<bool>? ExplodedChanged;

    public bool IsExploded { get; private set; }

    public EnginePart SelectedPart { get; private set; } = EnginePart.None;

    public void SetExploded(bool exploded)
    {
        if (IsExploded == exploded)
        {
            return;
        }

        IsExploded = exploded;
        _explodeGoal = exploded ? 1f : 0f;
        ExplodedChanged?.Invoke(this, exploded);
    }

    public void Select(EnginePart part)
    {
        if (SelectedPart == part)
        {
            return;
        }

        SelectedPart = part;
        _selectionAge = 0f;
        var (yaw, pitch, distance) = part switch
        {
            EnginePart.Nacelle => (0.95f, 0.24f, 17f),
            EnginePart.Fan => (1.22f, 0.12f, 13.5f),
            EnginePart.Compressor => (0.42f, 0.34f, 12.5f),
            EnginePart.Turbine => (-0.30f, 0.26f, 12.5f),
            _ => (DefaultYaw, DefaultPitch, DefaultDistance),
        };
        _goalYaw = NearestAngle(yaw);
        _goalPitch = pitch;
        _goalDistance = distance;
        UpdateAutomationName();
        PartSelected?.Invoke(this, part);
    }

    public void SetHeat(double value) => _heat = (float)Math.Clamp(value, 0, 1);

    public void SetStatus(AircraftStatus status)
    {
        _status = status;
        if (status == AircraftStatus.Healthy)
        {
            _heat = 0f;
        }

        UpdateAutomationName();
    }

    public void ResetView()
    {
        Select(EnginePart.None);
        _goalYaw = NearestAngle(DefaultYaw);
        _goalPitch = DefaultPitch;
        _goalDistance = DefaultDistance;
        SetExploded(false);
        _lastInteraction = -10;
    }

    public void Celebrate()
    {
        _celebrateStart = _clock.Elapsed.TotalSeconds;
        _goalYaw += 0.26f;
        _lastInteraction = _celebrateStart;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshTheme();
        _lastFrame = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
        UpdateAutomationName();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _target2D?.Dispose();
        _target2D = null;
    }

    private void OnRendering(object? sender, object e) => Canvas.Invalidate();

    private void RefreshTheme()
    {
        _cyan = ThemeRes.Get<Color>(this, "AccentCyanColor");
        _amber = ThemeRes.Get<Color>(this, "AttentionColor");
        _green = ThemeRes.Get<Color>(this, "HealthyColor");
        _canvas = ThemeRes.Get<Color>(this, "CanvasColor");
        _heatColor = ThemeRes.Get<Color>(this, "CriticalColor");
        _text = ThemeRes.Brush(this, "PrimaryTextBrush") is SolidColorBrush b ? b.Color : _cyan;
        _highContrast = _accessibility.HighContrast;
        _light = !_highContrast && ActualTheme == ElementTheme.Light;
        _animationsEnabled = _uiSettings.AnimationsEnabled;
    }

    private void UpdateAutomationName()
    {
        var state = _status == AircraftStatus.Healthy ? "Engine 2 healthy" : "Engine 2 needs attention, fan blade 14 flagged";
        var part = SelectedPart == EnginePart.None ? "no part selected" : $"{SelectedPart} selected";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"3D hologram of CA 2207, A321neo. {state}; {part}. Arrow keys orbit, Home resets.");
    }

    /// <summary>Screen padding (DIPs) occupied by overlay panels; the hologram is centred in the remaining area.</summary>
    public Thickness FocusPadding { get; set; }

    private float NearestAngle(float goal) => goal + MathF.Tau * MathF.Round((_yaw - goal) / MathF.Tau);

    private static Color WithAlpha(Color c, float a) => Color.FromArgb((byte)(Math.Clamp(a, 0f, 1f) * 255), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    private Vector3 ExplodeOffset(EnginePart part) => _explode * part switch
    {
        EnginePart.Nacelle => new Vector3(0f, 3.2f, 0f),
        EnginePart.Fan => new Vector3(3.5f, 0f, 0f),
        EnginePart.Compressor => new Vector3(-0.8f, 0.6f, 0f),
        EnginePart.Turbine => new Vector3(-3.8f, 0f, 0f),
        _ => Vector3.Zero,
    };

    private Vector3 PartCenter(EnginePart part) => _centroids[part] + ExplodeOffset(part);

    private Vector3 Transform(WireLine line, Vector3 p)
    {
        if (line.Spins)
        {
            var o = p.Z < 0 ? Engine1Origin : WireMesh.Engine2Origin;
            float dy = p.Y - o.Y, dz = p.Z - o.Z, c = MathF.Cos(_spin), s = MathF.Sin(_spin);
            p = new Vector3(p.X, o.Y + dy * c - dz * s, o.Z + dy * s + dz * c);
        }

        return line.IsEngine2 ? p + ExplodeOffset(line.Part) : p;
    }

    private void Step(double now, float dt)
    {
        if (_animationsEnabled)
        {
            var accel = 90f * (_explodeGoal - _explode) - 14f * _explodeVelocity;
            _explodeVelocity += accel * dt;
            _explode += _explodeVelocity * dt;
        }
        else
        {
            _explode = _explodeGoal;
            _explodeVelocity = 0f;
        }

        var idle = _animationsEnabled && SelectedPart == EnginePart.None && !_pressed && now - _lastInteraction > 4;
        _idleAmount += ((idle ? 1f : 0f) - _idleAmount) * Math.Min(1f, dt * 0.8f);
        _goalYaw += 0.06f * _idleAmount * dt;

        var k = _animationsEnabled ? 1f - MathF.Exp(-dt * 4.5f) : 1f;
        _yaw += (_goalYaw - _yaw) * k;
        _pitch += (_goalPitch - _pitch) * k;
        _distance += (_goalDistance - _distance) * k;
        var goalTarget = SelectedPart == EnginePart.None ? DefaultTarget : PartCenter(SelectedPart);
        _target = Vector3.Lerp(_target, goalTarget, k);

        if (_animationsEnabled)
        {
            _spin = (_spin + _spinRate * dt * MathF.Tau) % MathF.Tau;
        }

        _selectionAge += dt;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastFrame, 0, 0.05);
        _lastFrame = now;
        Step(now, dt);

        _viewSize = sender.Size;
        float w = (float)sender.Size.Width, h = (float)sender.Size.Height;
        if (w < 4 || h < 4)
        {
            return;
        }

        var pad = FocusPadding;
        float left = (float)Math.Min(pad.Left, w * 0.4), right = (float)Math.Min(pad.Right, w * 0.4);
        float top = (float)Math.Min(pad.Top, h * 0.3), bottom = (float)Math.Min(pad.Bottom, h * 0.3);
        var center = new Vector2((left + w - right) / 2f, (top + h - bottom) / 2f);
        var availH = Math.Max(120f, h - top - bottom);
        var availW = Math.Max(120f, w - left - right);
        var focal = Math.Min(availH, availW * 0.62f) / 2f / MathF.Tan(FieldOfView / 2f);

        var eye = _target + _distance * new Vector3(
            MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Sin(_yaw));
        var view = Matrix4x4.CreateLookAt(eye, _target, Vector3.UnitY);

        bool Project(Vector3 p, out Vector2 screen, out float depth)
        {
            var v = Vector3.Transform(p, view);
            depth = -v.Z;
            if (depth < 0.3f)
            {
                screen = default;
                return false;
            }

            screen = new Vector2(center.X + focal * v.X / depth, center.Y - focal * v.Y / depth);
            return true;
        }

        _labelFormat ??= new CanvasTextFormat { FontFamily = "Segoe UI Variable Display", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _subFormat ??= new CanvasTextFormat { FontFamily = "Cascadia Mono", FontSize = 12 };

        var ds = args.DrawingSession;
        var glow = !_light && !_highContrast;
        var celebrateT = (float)(now - _celebrateStart);
        var celebrating = celebrateT is >= 0f and < 2.6f;
        var healthy = _status == AircraftStatus.Healthy;
        var blink = _animationsEnabled ? 0.5f + 0.5f * MathF.Sin((float)now * MathF.Tau * 0.9f) : 1f;
        var hotBlink = _animationsEnabled ? MathF.Pow(0.5f + 0.5f * MathF.Sin((float)now * MathF.Tau * 1.7f), 2f) : 1f;
        var sweepX = 26f - (float)(now % 5.5 / 5.5) * 54f;
        var focusPart = _hover != EnginePart.None ? _hover : SelectedPart;
        var baseAlpha = _highContrast ? 1f : _light ? 0.72f : 0.58f;

        if (glow)
        {
            using var halo = new CanvasRadialGradientBrush(sender, WithAlpha(_cyan, 0.10f), WithAlpha(_cyan, 0f))
            {
                Center = center,
                RadiusX = availW * 0.55f,
                RadiusY = availH * 0.5f,
            };
            ds.FillRectangle(0, 0, w, h, halo);
        }

        using var lines = new CanvasCommandList(sender);
        _hitSegments.Clear();
        _partBounds.Clear();
        _hotTip = null;

        using (var lds = lines.CreateDrawingSession())
        {
            DrawFloor(lds, Project);

            foreach (var line in _lines)
            {
                Color color;
                float alpha = baseAlpha, width = _highContrast ? 1.5f : 1.1f;
                if (line.IsEngine2)
                {
                    color = healthy ? _green : Mix(_amber, _cyan, 0.15f * (1f - blink));
                    alpha = healthy ? 0.85f : 0.55f + 0.45f * blink;
                    if (line.Part == EnginePart.Turbine && !healthy)
                    {
                        color = Mix(color, _heatColor, 0.55f * _heat);
                    }

                    if (line.IsHot && !healthy)
                    {
                        color = Mix(_amber, _light ? _heatColor : Colors.White, 0.55f * hotBlink);
                        alpha = 0.55f + 0.45f * hotBlink;
                        width = 2.6f;
                    }

                    if (focusPart != EnginePart.None)
                    {
                        alpha *= line.Part == focusPart ? 1.15f : 0.32f;
                        width *= line.Part == focusPart ? 1.35f : 1f;
                    }
                }
                else
                {
                    color = _cyan;
                    alpha *= SelectedPart != EnginePart.None ? 0.3f : 1f;
                }

                if (celebrating)
                {
                    var pulse = 1f - celebrateT / 2.6f;
                    color = Mix(color, _green, pulse);
                    alpha = Math.Min(1f, alpha + 0.3f * pulse);
                }

                var pts = line.Points;
                Vector2 prev = default;
                var prevOk = false;
                for (var i = 0; i < pts.Length; i++)
                {
                    var world = Transform(line, pts[i]);
                    var ok = Project(world, out var sp, out var depth);
                    if (ok && prevOk)
                    {
                        var fade = _highContrast ? 1f : Math.Clamp(1.25f - (depth - _distance + 12f) / 55f, 0.3f, 1f);
                        var segColor = color;
                        var segAlpha = alpha * fade;
                        var scan = Math.Abs(world.X - sweepX);
                        if (glow && scan < 1.6f)
                        {
                            var s = 1f - scan / 1.6f;
                            segColor = Mix(segColor, Colors.White, 0.45f * s);
                            segAlpha = Math.Min(1f, segAlpha + 0.4f * s);
                        }

                        lds.DrawLine(prev, sp, WithAlpha(segColor, segAlpha), width);
                        if (line.IsEngine2)
                        {
                            _hitSegments.Add((prev, sp, line.Part));
                            Grow(line.Part, sp);
                        }
                    }

                    if (ok && line.IsHot && i == pts.Length - 1)
                    {
                        _hotTip = sp;
                    }

                    prev = sp;
                    prevOk = ok;
                }
            }

            DrawEffects(lds, Project, now, healthy, celebrateT);
            if (focusPart != EnginePart.None && Project(PartCenter(focusPart), out var anchor, out _))
            {
                DrawCallout(lds, focusPart, anchor, now, healthy);
            }
        }

        if (glow)
        {
            using var blur = new GaussianBlurEffect { Source = lines, BlurAmount = 7f, BorderMode = EffectBorderMode.Soft, Optimization = EffectOptimization.Speed };
            using var wide = new GaussianBlurEffect { Source = lines, BlurAmount = 22f, BorderMode = EffectBorderMode.Soft, Optimization = EffectOptimization.Speed };
            var old = ds.Blend;
            ds.Blend = CanvasBlend.Add;
            ds.DrawImage(wide);
            ds.DrawImage(blur);
            ds.DrawImage(lines);
            ds.Blend = old;
            DrawScanlines(ds, w, h);
        }
        else
        {
            ds.DrawImage(lines);
        }
    }

    private void Grow(EnginePart part, Vector2 p)
    {
        if (_partBounds.TryGetValue(part, out var r))
        {
            var x1 = Math.Min(r.X, p.X);
            var y1 = Math.Min(r.Y, p.Y);
            var x2 = Math.Max(r.X + r.Width, p.X);
            var y2 = Math.Max(r.Y + r.Height, p.Y);
            _partBounds[part] = new Rect(x1, y1, x2 - x1, y2 - y1);
        }
        else
        {
            _partBounds[part] = new Rect(p.X, p.Y, 0, 0);
        }
    }

    private delegate bool Projector(Vector3 p, out Vector2 screen, out float depth);

    private Size _viewSize;

    private void DrawFloor(CanvasDrawingSession ds, Projector project)
    {
        var floorAlpha = _highContrast ? 0.6f : _light ? 0.22f : 0.16f;
        for (var r = 6f; r <= 36f; r += 6f)
        {
            Vector2 prev = default;
            var prevOk = false;
            for (var i = 0; i <= 64; i++)
            {
                var a = i / 64f * MathF.Tau;
                var ok = project(new Vector3(r * MathF.Cos(a), FloorY, r * MathF.Sin(a)), out var sp, out _);
                if (ok && prevOk)
                {
                    ds.DrawLine(prev, sp, WithAlpha(_cyan, floorAlpha * (1.1f - r / 40f)), 1f);
                }

                prev = sp;
                prevOk = ok;
            }
        }

        for (var i = 0; i < 24; i++)
        {
            var a = i / 24f * MathF.Tau;
            if (project(new Vector3(6f * MathF.Cos(a), FloorY, 6f * MathF.Sin(a)), out var p1, out _)
                && project(new Vector3(36f * MathF.Cos(a), FloorY, 36f * MathF.Sin(a)), out var p2, out _))
            {
                ds.DrawLine(p1, p2, WithAlpha(_cyan, floorAlpha * 0.6f), 1f);
            }
        }
    }

    private void DrawEffects(CanvasDrawingSession ds, Projector project, double now, bool healthy, float celebrateT)
    {
        if (!healthy && _heat > 0.01f && project(PartCenter(EnginePart.Turbine), out var turbine, out var depth))
        {
            var radius = 260f / depth * (0.7f + 0.3f * _heat);
            using var heat = new CanvasRadialGradientBrush(ds, WithAlpha(_heatColor, 0.35f * _heat), WithAlpha(_heatColor, 0f))
            {
                Center = turbine,
                RadiusX = radius,
                RadiusY = radius,
            };
            ds.FillCircle(turbine, radius, heat);
        }

        if (!healthy && _hotTip is Vector2 tip)
        {
            for (var k = 0; k < 2; k++)
            {
                var t = (float)((now + k * 0.7) % 1.4 / 1.4);
                var ping = _animationsEnabled ? t : 0.4f;
                ds.DrawCircle(tip, 4f + 26f * ping, WithAlpha(_amber, (1f - ping) * 0.9f), 1.5f);
            }

            ds.FillCircle(tip, 3f, WithAlpha(_light ? _amber : Colors.White, 0.95f));
        }

        if (celebrateT is >= 0f and < 2.6f && project(WireMesh.Engine2Origin + new Vector3(-2f, 0f, 0f), out var e2, out _))
        {
            for (var k = 0; k < 3; k++)
            {
                var t = Math.Clamp((celebrateT - k * 0.35f) / 1.6f, 0f, 1f);
                if (t is > 0f and < 1f)
                {
                    ds.DrawCircle(e2, 20f + 340f * EaseOut(t), WithAlpha(_green, (1f - t) * 0.85f), 2.5f * (1f - t) + 0.5f);
                }
            }
        }
    }

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);

    private void DrawCallout(CanvasDrawingSession ds, EnginePart part, Vector2 anchor, double now, bool healthy)
    {
        var flagged = !healthy;
        var (title, detail) = part switch
        {
            EnginePart.Fan => ("FAN · BLADE 14", flagged ? "LEADING-EDGE NICK · 0.42 mm" : "BLADE 14 · SIGNED OFF"),
            EnginePart.Nacelle => ("NACELLE", flagged ? "INLET COWL · NOMINAL" : "INLET COWL · NOMINAL"),
            EnginePart.Compressor => ("HP COMPRESSOR", flagged ? "N2 VIB 1.9 ips · WATCH" : "N2 VIB 0.8 ips · OK"),
            EnginePart.Turbine => ("HP TURBINE", flagged ? "EGT MARGIN 18 °C · LOW" : "EGT MARGIN 41 °C · OK"),
            _ => (string.Empty, string.Empty),
        };
        var accent = flagged && part != EnginePart.Nacelle ? _amber : healthy ? _green : _cyan;
        var appear = Math.Clamp(_selectionAge * 3f, 0f, 1f);
        if (_hover != EnginePart.None && _hover != SelectedPart)
        {
            appear = 1f;
        }

        var flip = anchor.X + 300 > _viewSize.Width;
        var dir = flip ? -1f : 1f;
        var elbow = anchor + new Vector2(70f * dir, -80f);
        var end = elbow + new Vector2(200f * dir * appear, 0f);
        var lineColor = WithAlpha(accent, 0.95f * appear);

        var pulse = _animationsEnabled ? 0.5f + 0.5f * MathF.Sin((float)now * 5f) : 1f;
        ds.DrawCircle(anchor, 6f + 3f * pulse, lineColor, 1.5f);
        ds.FillCircle(anchor, 2.5f, lineColor);
        ds.DrawLine(anchor, elbow, lineColor, 1.3f);
        ds.DrawLine(elbow, end, lineColor, 1.3f);

        var textX = flip ? end.X : elbow.X;
        _labelFormat!.HorizontalAlignment = CanvasHorizontalAlignment.Left;
        ds.DrawText(title, textX + 2f, elbow.Y - 26f, WithAlpha(_light ? _text : Mix(accent, Colors.White, 0.35f), appear), _labelFormat);
        ds.DrawText(detail, textX + 2f, elbow.Y + 6f, WithAlpha(_light ? _text : accent, 0.9f * appear), _subFormat!);
    }

    private void DrawScanlines(CanvasDrawingSession ds, float w, float h)
    {
        var c = WithAlpha(_canvas, 0.22f);
        for (var y = 0f; y < h; y += 3f)
        {
            ds.DrawLine(0, y, w, y, c, 1f);
        }
    }

    private EnginePart HitTest(Point point)
    {
        var p = new Vector2((float)point.X, (float)point.Y);
        var best = EnginePart.None;
        var bestDistance = 10f;
        foreach (var (a, b, part) in _hitSegments)
        {
            var ab = b - a;
            var len = ab.LengthSquared();
            var t = len < 1e-4f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len, 0f, 1f);
            var d = Vector2.Distance(p, a + ab * t);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = part;
            }
        }

        if (best != EnginePart.None)
        {
            return best;
        }

        var bestArea = double.MaxValue;
        foreach (var (part, r) in _partBounds)
        {
            var area = r.Width * r.Height;
            if (r.Contains(point) && area < bestArea)
            {
                bestArea = area;
                best = part;
            }
        }

        return best;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Canvas.CapturePointer(e.Pointer);
        _pressed = true;
        _dragging = false;
        _pressPoint = _lastPoint = e.GetCurrentPoint(Canvas).Position;
        _lastInteraction = _clock.Elapsed.TotalSeconds;
        Focus(FocusState.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(Canvas).Position;
        if (_pressed)
        {
            if (!_dragging && Math.Abs(pos.X - _pressPoint.X) + Math.Abs(pos.Y - _pressPoint.Y) > 5)
            {
                _dragging = true;
            }

            if (_dragging)
            {
                _goalYaw += (float)(pos.X - _lastPoint.X) * 0.008f;
                _goalPitch = Math.Clamp(_goalPitch + (float)(pos.Y - _lastPoint.Y) * 0.006f, -0.1f, 1.2f);
                _lastInteraction = _clock.Elapsed.TotalSeconds;
            }
        }
        else
        {
            _hover = HitTest(pos);
            ProtectedCursor = InputSystemCursor.Create(_hover != EnginePart.None ? InputSystemCursorShape.Hand : InputSystemCursorShape.SizeAll);
        }

        _lastPoint = pos;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var wasClick = _pressed && !_dragging;
        _pressed = false;
        _dragging = false;
        Canvas.ReleasePointerCapture(e.Pointer);
        if (!wasClick)
        {
            return;
        }

        var part = HitTest(e.GetCurrentPoint(Canvas).Position);
        if (part == SelectedPart)
        {
            part = EnginePart.None;
        }

        if (part != SelectedPart)
        {
            Select(part);
        }

        e.Handled = true;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => _hover = EnginePart.None;

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(Canvas).Properties.MouseWheelDelta;
        _goalDistance = Math.Clamp(_goalDistance * (1f - delta / 1200f), 8f, 90f);
        _lastInteraction = _clock.Elapsed.TotalSeconds;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left: _goalYaw += 0.087f; break;
            case VirtualKey.Right: _goalYaw -= 0.087f; break;
            case VirtualKey.Up: _goalPitch = Math.Clamp(_goalPitch + 0.035f, -0.1f, 1.2f); break;
            case VirtualKey.Down: _goalPitch = Math.Clamp(_goalPitch - 0.035f, -0.1f, 1.2f); break;
            case VirtualKey.Home: ResetView(); break;
            case VirtualKey.Escape when SelectedPart != EnginePart.None:
                Select(EnginePart.None);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }

        _lastInteraction = _clock.Elapsed.TotalSeconds;
        e.Handled = true;
    }
}
