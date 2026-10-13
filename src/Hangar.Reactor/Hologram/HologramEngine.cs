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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Hangar.Reactor.Hologram;

/// <summary>
/// Holographic wireframe A321neo rendered with Win2D: perspective camera with orbit / zoom-to-part, additive bloom,
/// a scanning sweep, spinning fan, blinking hot blade and a spring-driven "explode" of Engine 2.
/// (Composition Scenes was not viable on this SDK — see docs/SCENES-SPIKE.md.)
/// </summary>
public sealed class HologramEngine
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
    private const float EngineYaw = 1.12f, EnginePitch = 0.24f, EngineDistance = 16f;

    // Exploded-view state: one damped spring per Engine 2 component, released in a cascade.
    private readonly EngineSubPart[] _subs;
    private readonly Vector3[] _subCenter;
    private readonly float[] _subTop, _subBottom, _subX, _subV;
    private readonly float _maxDelay;
    private readonly int _hotSub = -1;
    private readonly Vector3 _engineCenterCollapsed, _engineCenterExploded;
    private double _explodeChangedAt = -100, _pulseStart = -100;
    private float _labelAlpha;
    private readonly List<LabelSlot> _labelSlots = [];
    private readonly List<Rect> _labelRects = [];

    private static bool Overlaps(Rect a, Rect b) =>
        a.X < b.X + b.Width + 6 && b.X < a.X + a.Width + 6 && a.Y < b.Y + b.Height + 2 && b.Y < a.Y + a.Height + 2;
    private bool _engineFocus;
    private PointerDeviceType _pointerType = PointerDeviceType.Mouse;
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
    private float _explode, _explodeGoal;
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

    // Amber flags attention while collapsed; once exploded the broken part reads as Critical red.
    private Color FaultColor => Mix(_amber, _heatColor, Math.Clamp(_explode, 0f, 1f));
    private bool _light, _highContrast;
    private Vector2? _hotTip;
    private Vector3 _hotTipWorld;

    // Dive into the faulty blade tip (hologram → borescope handoff).
    private const float DiveDuration = 0.85f;
    private double _diveStart = -1;
    private bool _diveFired;

    private CanvasRenderTarget? _target2D;
    private CanvasTextFormat? _labelFormat, _subFormat, _tagFormat;

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private FrameworkElement? _host;

    public HologramEngine()
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        // Cache per-component centroids/extents once; frames only transform + project.
        _subs = [.. WireMesh.SubParts];
        var n = _subs.Length;
        _subCenter = new Vector3[n];
        _subTop = new float[n];
        _subBottom = new float[n];
        _subX = new float[n];
        _subV = new float[n];
        var counts = new int[n];
        var minY = Enumerable.Repeat(float.MaxValue, n).ToArray();
        var maxY = Enumerable.Repeat(float.MinValue, n).ToArray();
        foreach (var line in _lines.Where(l => l.Sub >= 0))
        {
            if (line.IsHot)
            {
                _hotSub = line.Sub;
            }

            foreach (var p in line.Points)
            {
                _subCenter[line.Sub] += p;
                counts[line.Sub]++;
                minY[line.Sub] = Math.Min(minY[line.Sub], p.Y);
                maxY[line.Sub] = Math.Max(maxY[line.Sub], p.Y);
            }
        }

        for (var i = 0; i < n; i++)
        {
            _subCenter[i] /= Math.Max(1, counts[i]);
            _subTop[i] = maxY[i] - _subCenter[i].Y;
            _subBottom[i] = _subCenter[i].Y - minY[i];
        }

        _maxDelay = n == 0 ? 0f : _subs.Max(s => s.Delay);
        Vector3 min0 = new(float.MaxValue), max0 = new(float.MinValue), min1 = min0, max1 = max0;
        foreach (var line in _lines.Where(l => l.Sub >= 0))
        {
            foreach (var p in line.Points)
            {
                var q = SubPoint(line.Sub, p, 1f, false);
                min0 = Vector3.Min(min0, p);
                max0 = Vector3.Max(max0, p);
                min1 = Vector3.Min(min1, q);
                max1 = Vector3.Max(max1, q);
            }
        }

        _engineCenterCollapsed = n == 0 ? WireMesh.Engine2Origin : (min0 + max0) / 2f;
        // Biased toward the near (fan) end so the perspective-enlarged inlet doesn't run under the side panel.
        _engineCenterExploded = n == 0 ? WireMesh.Engine2Origin : (min1 + max1) / 2f + new Vector3(1.1f, 0f, 0f);

    }

    public event EventHandler<EnginePart>? PartSelected;

    public event EventHandler<bool>? ExplodedChanged;

    /// <summary>The user tapped (or pressed Enter on) the flagged blade tip in the exploded view.</summary>
    public event EventHandler? FaultInvoked;

    /// <summary>Raised once the camera has finished diving into the faulty blade.</summary>
    public event EventHandler? DiveCompleted;

    /// <summary>Fly the camera into the red blade tip and flash, then raise <see cref="DiveCompleted"/>.</summary>
    public void DiveToFault()
    {
        if (_diveStart >= 0)
        {
            return;
        }

        if (!_animationsEnabled || _hotTip is null)
        {
            DiveCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        _diveStart = _clock.Elapsed.TotalSeconds;
        _diveFired = false;
        _lastInteraction = _diveStart;
    }

    private float DiveProgress => _diveStart < 0 ? 0f : Math.Clamp((float)(_clock.Elapsed.TotalSeconds - _diveStart) / DiveDuration, 0f, 1f);

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
        var now = _clock.Elapsed.TotalSeconds;
        // Reversing mid-flight: shift the clock so already-released parts keep moving.
        var elapsed = Math.Clamp(now - _explodeChangedAt, 0, _maxDelay);
        _explodeChangedAt = now - (_maxDelay - elapsed);
        if (exploded)
        {
            _pulseStart = now;
        }

        ExplodedChanged?.Invoke(this, exploded);
        UpdateAutomationName();
    }

    /// <summary>Fly the camera to Engine 2 and burst it into its exploded view.</summary>
    public void ExplodeEngine()
    {
        _engineFocus = true;
        if (SelectedPart != EnginePart.None)
        {
            Select(EnginePart.None);
        }

        _goalYaw = NearestAngle(EngineYaw);
        _goalPitch = EnginePitch;
        _goalDistance = EngineDistance;
        _lastInteraction = _clock.Elapsed.TotalSeconds;
        SetExploded(true);
    }

    /// <summary>Reverse the cascade and return the camera to the default orbit.</summary>
    public void CollapseEngine() => ResetView();

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
            EnginePart.Nacelle => (1.05f, 0.26f, 18f),
            EnginePart.Fan => (1.22f, 0.15f, 13.5f),
            EnginePart.Compressor => (1.0f, 0.3f, 13f),
            EnginePart.Turbine => (1.85f, 0.26f, 13f),
            _ when _engineFocus => (EngineYaw, EnginePitch, EngineDistance),
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
        _diveStart = -1;
        _engineFocus = false;
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

    /// <summary>Binds the engine to the hosting element (theme, automation name, cursor, focus).</summary>
    public void Attach(FrameworkElement host)
    {
        _host = host;
        RefreshTheme();
        _lastFrame = _clock.Elapsed.TotalSeconds;
        UpdateAutomationName();
    }

    public void Detach()
    {
        _host = null;
        _target2D?.Dispose();
        _target2D = null;
    }

    public void RefreshTheme()
    {
        if (_host is null)
        {
            return;
        }

        _cyan = ThemeRes.Get<Color>(_host, "AccentCyanColor");
        _amber = ThemeRes.Get<Color>(_host, "AttentionColor");
        _green = ThemeRes.Get<Color>(_host, "HealthyColor");
        _canvas = ThemeRes.Get<Color>(_host, "CanvasColor");
        _heatColor = ThemeRes.Get<Color>(_host, "CriticalColor");
        _text = ThemeRes.Brush(_host, "PrimaryTextBrush") is SolidColorBrush b ? b.Color : _cyan;
        _highContrast = _accessibility.HighContrast;
        _light = !_highContrast && _host.ActualTheme == ElementTheme.Light;
        _animationsEnabled = _uiSettings.AnimationsEnabled;
    }

    private void UpdateAutomationName()
    {
        if (_host is null)
        {
            return;
        }

        var state = _status == AircraftStatus.Healthy ? "Engine 2 healthy" : "Engine 2 needs attention, fan blade 14 flagged";
        var part = SelectedPart == EnginePart.None ? "no part selected" : $"{SelectedPart} selected";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_host, $"3D hologram of CA 2207, A321neo. {state}; {part}. Arrow keys orbit, Home resets.");
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

    private static Vector3 Roll(Vector3 p, Vector3 o, float angle)
    {
        float dy = p.Y - o.Y, dz = p.Z - o.Z, c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector3(p.X, o.Y + dy * c - dz * s, o.Z + dy * s + dz * c);
    }

    /// <summary>Position of a point on component <paramref name="sub"/> at explode amount <paramref name="x"/>.</summary>
    private Vector3 SubPoint(int sub, Vector3 p, float x, bool spins)
    {
        var s = _subs[sub];
        var o = WireMesh.Engine2Origin;
        if (s.Roll != 0f)
        {
            p = Roll(p, o, s.Roll * x);
        }

        p += s.RadialDir * (s.Radial * x);
        if (spins)
        {
            p = Roll(p, o, _spin);
        }

        return p + s.Axial * x;
    }

    private Vector3 PartCenter(EnginePart part)
    {
        var sum = Vector3.Zero;
        var count = 0;
        for (var i = 0; i < _subs.Length; i++)
        {
            if (_subs[i].Group == part)
            {
                sum += SubPoint(i, _subCenter[i], _subX[i], false);
                count++;
            }
        }

        return count == 0 ? WireMesh.Engine2Origin : sum / count;
    }

    private Vector3 EngineCenter => Vector3.Lerp(_engineCenterCollapsed, _engineCenterExploded, Math.Clamp(_explode, 0f, 1f));

    private Vector3 Transform(WireLine line, Vector3 p, float lag = 0f)
    {
        if (line.Sub >= 0)
        {
            return SubPoint(line.Sub, p, _subX[line.Sub] - _subV[line.Sub] * lag, line.Spins);
        }

        return line.Spins ? Roll(p, p.Z < 0 ? Engine1Origin : WireMesh.Engine2Origin, _spin) : p;
    }

    private void Step(double now, float dt)
    {
        var since = now - _explodeChangedAt;
        var exploding = _explodeGoal > 0.5f;
        float sum = 0f, maxSpeed = 0f;
        for (var i = 0; i < _subs.Length; i++)
        {
            // Explode front-to-back; collapse reverses the cascade.
            var delay = exploding ? _subs[i].Delay : _maxDelay - _subs[i].Delay;
            var goal = since >= delay ? _explodeGoal : 1f - _explodeGoal;
            if (_animationsEnabled)
            {
                var accel = 150f * (goal - _subX[i]) - (exploding ? 10.5f : 17f) * _subV[i];
                _subV[i] += accel * dt;
                _subX[i] += _subV[i] * dt;
            }
            else
            {
                _subX[i] = _explodeGoal;
                _subV[i] = 0f;
            }

            sum += _subX[i];
            maxSpeed = Math.Max(maxSpeed, Math.Abs(_subV[i]));
        }

        _explode = _subs.Length == 0 ? _explodeGoal : sum / _subs.Length;
        var settled = exploding && since > _maxDelay + 0.2 && maxSpeed < 0.15f;
        _labelAlpha = _animationsEnabled
            ? Math.Clamp(_labelAlpha + (settled ? dt * 2.2f : -dt * 6f), 0f, 1f)
            : settled ? 1f : 0f;

        var idle = _animationsEnabled && SelectedPart == EnginePart.None && !_engineFocus && !_pressed && now - _lastInteraction > 4;
        _idleAmount += ((idle ? 1f : 0f) - _idleAmount) * Math.Min(1f, dt * 0.8f);
        _goalYaw += 0.06f * _idleAmount * dt;

        var k = _animationsEnabled ? 1f - MathF.Exp(-dt * 4.5f) : 1f;
        _yaw += (_goalYaw - _yaw) * k;
        _pitch += (_goalPitch - _pitch) * k;
        _distance += (_goalDistance - _distance) * k;
        var goalTarget = SelectedPart != EnginePart.None ? PartCenter(SelectedPart) : _engineFocus ? EngineCenter : DefaultTarget;
        if (_diveStart >= 0 && now - _diveStart > DiveDuration + 0.4)
        {
            // Back from the borescope: let the camera pull out of the blade naturally.
            _diveStart = -1;
        }

        if (_diveStart >= 0)
        {
            // Accelerating push into the blade tip; the flash overlay covers the last stretch.
            var d = DiveProgress;
            var ease = d * d * d;
            goalTarget = _hotTipWorld;
            _target = Vector3.Lerp(_target, goalTarget, Math.Min(1f, dt * 9f));
            _distance = MathF.Max(0.9f, _goalDistance * (1f - 0.93f * ease));
            if (d >= 1f && !_diveFired)
            {
                _diveFired = true;
                _dispatcher.TryEnqueue(() => DiveCompleted?.Invoke(this, EventArgs.Empty));
            }
        }

        _target = Vector3.Lerp(_target, goalTarget, k);

        if (_animationsEnabled)
        {
            // The fan winds down while exploded so labels stay readable.
            var rate = _spinRate * (1f - 0.92f * Math.Clamp(_explode, 0f, 1f));
            _spin = (_spin + rate * dt * MathF.Tau) % MathF.Tau;
        }

        _selectionAge += dt;
    }

    public void Draw(CanvasControl sender, CanvasDrawingSession ds)
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
        // Narrow gaps between the floating cards (e.g. with the Ask Hangar pane open) frame the plane
        // tighter so the tail never runs under a card; wide gaps keep the original 0.62 framing.
        var narrow = 1f - Math.Clamp((availW - 320f) / 280f, 0f, 1f);
        narrow = narrow * narrow * (3f - 2f * narrow);
        var focal = Math.Min(availH, availW * (0.62f - 0.18f * narrow)) / 2f / MathF.Tan(FieldOfView / 2f);

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

        _labelFormat ??= new CanvasTextFormat { FontFamily = "Segoe UI Variable Display",         FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Normal };
                _subFormat ??= new CanvasTextFormat { FontFamily = "Cascadia Mono", FontSize = 10.5f };
                _tagFormat ??= new CanvasTextFormat { FontFamily = "Cascadia Mono", FontSize = 9.5f };

        var glow = !_light && !_highContrast;
        var celebrateT = (float)(now - _celebrateStart);
        var celebrating = celebrateT is >= 0f and < 2.6f;
        var healthy = _status == AircraftStatus.Healthy;
        var blink = _animationsEnabled ? 0.5f + 0.5f * MathF.Sin((float)now * MathF.Tau * 0.9f) : 1f;
        var hotBlink = _animationsEnabled ? MathF.Pow(0.5f + 0.5f * MathF.Sin((float)now * MathF.Tau * 1.7f), 2f) : 1f;
        var sweepX = 26f - (float)(now % 5.5 / 5.5) * 54f;
        var focusPart = _hover != EnginePart.None ? _hover : SelectedPart;
        var baseAlpha = _highContrast ? 1f : _light ? 0.72f : 0.58f;
        // While exploded, the rest of the aircraft recedes (~15%) and is pushed away from the camera.
        var ex = Math.Clamp(_explode, 0f, 1f);
        var airframeFade = _highContrast ? 1f - 0.6f * ex : 1f - 0.85f * ex;
        var pushBack = Vector3.Normalize(_target - eye) * (7f * ex);
        _floorFade = 1f - 0.55f * ex;
        _safe = new Rect(left + 8f, Math.Max(24f, top * 0.5f), Math.Max(80f, w - right - left - 16f), Math.Max(80f, h - bottom - 12f - Math.Max(24f, top * 0.5f)));

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
                    // Collapsed, Engine 2 breathes amber (attention). Exploded, it becomes a cyan technical
                    // drawing with depth/group shading, and amber is reserved for the faulty fan blade 14.
                    var attention = healthy ? _green : Mix(_amber, _cyan, 0.15f * (1f - blink));
                    var attentionAlpha = healthy ? 0.85f : 0.55f + 0.45f * blink;
                    var (tint, groupAlpha) = GroupStyle(line.Part);
                    color = Mix(attention, tint, ex);
                    alpha = attentionAlpha + (groupAlpha - attentionAlpha) * ex;
                    if (line.Part == EnginePart.Turbine && !healthy)
                    {
                        color = Mix(color, _heatColor, 0.55f * _heat * (1f - ex));
                    }

                    if (line.Sub >= 0 && line.Sub == _hotSub && !healthy)
                    {
                        color = line.IsHot ? Mix(FaultColor, _light ? _heatColor : Colors.White, 0.55f * hotBlink) : FaultColor;
                        alpha = line.IsHot ? 0.55f + 0.45f * hotBlink : 0.7f + 0.3f * hotBlink;
                        width = line.IsHot ? 2.6f : 1.4f + 0.4f * ex;
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
                    alpha *= airframeFade;
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
                    if (!line.IsEngine2)
                    {
                        world += pushBack;
                    }

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
                            segAlpha = Math.Min(1f, segAlpha + 0.4f * s * (line.IsEngine2 ? 1f : airframeFade));
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
                        _hotTipWorld = world;
                    }

                    prev = sp;
                    prevOk = ok;
                }
            }

            if (glow && _animationsEnabled)
            {
                DrawTrails(lds, Project);
            }

            DrawAirflow(lds, Project, now, healthy, ex);
            DrawEffects(lds, Project, now, healthy, celebrateT);
            _labelRects.Clear();
            var labelsOwnStage = _labelAlpha > 0.5f && SelectedPart == EnginePart.None && _hover == EnginePart.None;
            Vector2? calloutAnchor = null;
            if (!labelsOwnStage && focusPart != EnginePart.None && Project(PartCenter(focusPart), out var anchor, out _))
            {
                calloutAnchor = anchor;
                _labelRects.Add(CalloutRect(anchor));
            }

            if (_labelAlpha > 0.01f)
            {
                DrawPartLabels(lds, Project, healthy);
            }

            if (calloutAnchor is Vector2 ca)
            {
                DrawCallout(lds, focusPart, ca, now, healthy);
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

        if (_diveStart >= 0)
        {
            DrawDiveFlash(ds, sender, w, h);
        }
    }

    /// <summary>Red bloom from the blade tip that whites out the stage as the camera arrives.</summary>
    private void DrawDiveFlash(CanvasDrawingSession ds, CanvasControl sender, float w, float h)
    {
        var d = DiveProgress;
        var tip = _hotTip ?? new Vector2(w / 2f, h / 2f);
        var reach = MathF.Max(w, h) * (0.15f + 1.2f * d * d);
        var core = _highContrast ? _text : _heatColor;
        using var bloom = new CanvasRadialGradientBrush(sender, [
            new CanvasGradientStop { Position = 0f, Color = WithAlpha(_light ? core : Mix(core, Colors.White, 0.5f), Math.Clamp(d * 1.6f, 0f, 1f)) },
            new CanvasGradientStop { Position = 0.35f, Color = WithAlpha(core, Math.Clamp(d * 1.2f, 0f, 0.85f)) },
            new CanvasGradientStop { Position = 1f, Color = WithAlpha(core, 0f) },
        ])
        {
            Center = tip,
            RadiusX = reach,
            RadiusY = reach,
        };
        ds.FillRectangle(0, 0, w, h, bloom);

        // Final beat fades to the page canvas so the drill-in lands on a calm frame.
        var fade = Math.Clamp((d - 0.7f) / 0.3f, 0f, 1f);
        if (fade > 0f)
        {
            ds.FillRectangle(0, 0, w, h, WithAlpha(_canvas, fade * fade));
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
    private Rect _safe;
    private float _floorFade = 1f;

    private (Color Tint, float Alpha) GroupStyle(EnginePart part)
    {
        if (_highContrast)
        {
            return (_cyan, 1f);
        }

        var bright = _light ? _cyan : Mix(_cyan, Colors.White, 0.3f);
        var deep = _light ? _cyan : Mix(_cyan, _canvas, 0.3f);
        return part switch
        {
            EnginePart.Fan => (bright, 0.95f),
            EnginePart.Compressor => (_cyan, 0.85f),
            EnginePart.Turbine => (Mix(_cyan, deep, 0.5f), 0.8f),
            _ => (deep, 0.66f),
        };
    }

    private void DrawFloor(CanvasDrawingSession ds, Projector project)
    {
        var floorAlpha = (_highContrast ? 0.6f : _light ? 0.22f : 0.16f) * _floorFade;
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

    // Air streaks flowing through both engines (intake → fan → bypass duct / core → nozzle, toward −X).
    private void DrawAirflow(CanvasDrawingSession ds, Projector project, double now, bool healthy, float ex)
    {
        var fade = (1f - ex) * (1f - ex);
        if (_highContrast || fade < 0.02f)
        {
            return;
        }

        var cool = healthy ? Mix(_cyan, _green, 0.55f) : _cyan;
        var hot = _light ? cool : Mix(cool, Colors.White, 0.55f);
        DrawEngineAirflow(ds, project, WireMesh.Engine2Origin, now, cool, hot, 0.55f * fade, 40, 30);
        DrawEngineAirflow(ds, project, Engine1Origin, now + 3.7, cool, hot, 0.3f * fade, 22, 16);
    }

    private void DrawEngineAirflow(CanvasDrawingSession ds, Projector project, Vector3 origin, double now, Color cool, Color hot, float alpha, int bypassCount, int coreCount)
    {
        static float Hash(int i, int k) => MathF.Abs(MathF.Sin(i * 12.9898f + k * 78.233f) * 43758.547f) % 1f;
        static float Smooth(float a, float b, float x) { var t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3f - 2f * t); }

        // Core annulus outer radius along the axis: squeezes through the compressor, swells through the turbines.
        static float CoreEnvelope(float dx) => dx switch
        {
            > -1.4f => 0.56f,
            > -2.6f => 0.56f - 0.1f * ((-1.4f - dx) / 1.2f),
            > -3.0f => 0.48f,
            > -4.1f => 0.48f + 0.1f * ((-3.0f - dx) / 1.1f),
            > -4.5f => 0.58f - 0.1f * ((-4.1f - dx) / 0.4f),
            _ => 0.48f,
        };

        var time = _animationsEnabled ? (float)now : 0f;
        for (var i = 0; i < bypassCount + coreCount; i++)
        {
            var core = i >= bypassCount;
            var a0 = Hash(i, 1) * MathF.Tau;
            var f = Hash(i, 2);
            var speed = (core ? 0.32f : 0.46f) * (0.85f + 0.3f * Hash(i, 3));
            var t = (Hash(i, 4) + time * speed) % 1f;
            var start = 1.6f;
            var endDx = core ? -5.6f : -3.6f;
            var dx = start + (endDx - start) * t;
            var tail = core ? 0.22f : 0.32f;

            Vector3 Point(float d)
            {
                // Funnel in from a wider stream ahead of the inlet.
                var funnel = 1f + 0.22f * Smooth(0f, 1.6f, d);
                float r;
                float swirl;
                if (core)
                {
                    r = (0.2f + f * (CoreEnvelope(d) - 0.22f)) * funnel;
                    swirl = 0.18f * Math.Clamp(-d, 0f, 4.5f);
                }
                else
                {
                    r = (0.68f + f * 0.34f) * funnel;
                    swirl = 0.06f * Math.Clamp(-d, 0f, 1.4f);
                }

                var ang = a0 + swirl;
                return new Vector3(origin.X + d, origin.Y + r * MathF.Cos(ang), origin.Z + r * MathF.Sin(ang));
            }

            if (!project(Point(dx), out var head, out _) || !project(Point(dx + tail), out var back, out _))
            {
                continue;
            }

            var life = MathF.Sin(MathF.PI * t);
            var heat = core ? Smooth(-2.6f, -3.2f, dx) : 0f;
            var color = Mix(cool, hot, heat);
            var a = alpha * life * (core ? 0.9f : 0.7f);
            ds.DrawLine(back, head, WithAlpha(color, a * 0.35f), core ? 2.6f : 2.2f);
            ds.DrawLine(back, head, WithAlpha(color, a), core ? 1.1f : 0.9f);
        }
    }

    private void DrawEffects(CanvasDrawingSession ds, Projector project, double now, bool healthy, float celebrateT)
    {
        var heatAmount = _heat * (1f - Math.Clamp(_explode, 0f, 1f));
        if (!healthy && heatAmount > 0.01f && project(PartCenter(EnginePart.Turbine), out var turbine, out var depth))
        {
            var radius = 260f / depth * (0.7f + 0.3f * heatAmount);
            using var heat = new CanvasRadialGradientBrush(ds, WithAlpha(_heatColor, 0.35f * heatAmount), WithAlpha(_heatColor, 0f))
            {
                Center = turbine,
                RadiusX = radius,
                RadiusY = radius,
            };
            ds.FillCircle(turbine, radius, heat);
        }

        if (!healthy && _hotTip is Vector2 tip)
        {
            if (_explode > 0.01f)
            {
                using var glow = new CanvasRadialGradientBrush(ds, WithAlpha(_heatColor, 0.45f * _explode), WithAlpha(_heatColor, 0f))
                {
                    Center = tip,
                    RadiusX = 34f,
                    RadiusY = 34f,
                };
                ds.FillCircle(tip, 34f, glow);
            }

            for (var k = 0; k < 2; k++)
            {
                var t = (float)((now + k * 0.7) % 1.4 / 1.4);
                var ping = _animationsEnabled ? t : 0.4f;
                ds.DrawCircle(tip, 4f + 26f * ping, WithAlpha(FaultColor, (1f - ping) * 0.9f), 1.5f);
            }

            ds.FillCircle(tip, 3f, WithAlpha(_light ? FaultColor : Colors.White, 0.95f));
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

        var pt = (float)(now - _pulseStart);
        if (pt is >= 0f and < 0.9f && IsExploded && _animationsEnabled && project(EngineCenter, out var core, out var coreDepth))
        {
            var pulseColor = healthy ? _green : _cyan;
            var glowRadius = 900f / coreDepth * (0.4f + EaseOut(Math.Clamp(pt / 0.5f, 0f, 1f)));
            using (var glow = new CanvasRadialGradientBrush(ds, WithAlpha(pulseColor, 0.4f * (1f - pt / 0.9f)), WithAlpha(pulseColor, 0f))
            {
                Center = core,
                RadiusX = glowRadius,
                RadiusY = glowRadius * 0.7f,
            })
            {
                ds.FillEllipse(core, glowRadius, glowRadius * 0.7f, glow);
            }

            for (var k = 0; k < 3; k++)
            {
                var t = Math.Clamp((pt - k * 0.12f) / 0.6f, 0f, 1f);
                if (t is > 0f and < 1f)
                {
                    var r = 30f + 1400f / coreDepth * EaseOut(t);
                    ds.DrawEllipse(core, r, r * 0.62f, WithAlpha(pulseColor, (1f - t) * 0.9f), 2.5f * (1f - t) + 0.6f);
                }
            }
        }
    }

    private void DrawTrails(CanvasDrawingSession ds, Projector project)
    {
        foreach (var line in _lines)
        {
            if (line.Sub < 0 || Math.Abs(_subV[line.Sub]) < 0.6f || line.Points.Length < 2)
            {
                continue;
            }

            for (var lagIndex = 1; lagIndex <= 2; lagIndex++)
            {
                var lag = 0.045f * lagIndex;
                var color = WithAlpha(_cyan, lagIndex == 1 ? 0.16f : 0.08f);
                Vector2? prev = null;
                foreach (var p in line.Points)
                {
                    if (!project(Transform(line, p, lag), out var s, out _))
                    {
                        prev = null;
                        continue;
                    }

                    if (prev is Vector2 a)
                    {
                        ds.DrawLine(a, s, color, 1f);
                    }

                    prev = s;
                }
            }
        }
    }

    private readonly record struct LabelSlot(int Sub, string Title, string? Detail, bool Hot, Vector2 Top, Vector2 Bottom);

    private (int Sub, string Title, string Detail)[]? _groupLabels;

    private (int Sub, string Title, string Detail)[] GroupLabels => _groupLabels ??=
    [
        (FindSub("Thrust reverser (near)"), "NACELLE", "COWL · FAN CASE · REVERSERS"),
        (FindSub("Fan disk"), "FAN MODULE", "DISK · 22 BLADES · OGV ×30"),
        (FindSub("HPC stage 4"), "COMPRESSOR", "BOOSTER ×3 · HPC ×8 · COMBUSTOR"),
        (FindSub("LPT stage 3"), "TURBINE", "HPT ×2 · LPT ×5 · N1/N2 SHAFTS"),
    ];

    private int FindSub(string name) => Array.FindIndex(_subs, s => s.Name == name);

    /// <summary>
    /// Technical-illustration callouts. By default one label per module (plus the flagged blade); with a module
    /// selected, one per component in it. Labels alternate above/below the axis, step their leaders until they
    /// clear every placed tag (and the selection callout), and stay inside the safe area between the panels.
    /// </summary>
    private void DrawPartLabels(CanvasDrawingSession ds, Projector project, bool healthy)
    {
        _labelSlots.Clear();
        void AddSlot(int i, string title, string? detail)
        {
            if (i < 0)
            {
                return;
            }

            var top = _subCenter[i] + new Vector3(0f, _subTop[i], 0f);
            var bottom = _subCenter[i] - new Vector3(0f, _subBottom[i], 0f);
            if (project(SubPoint(i, top, _subX[i], false), out var t, out _) && project(SubPoint(i, bottom, _subX[i], false), out var b, out _))
            {
                _labelSlots.Add(new LabelSlot(i, title, detail, i == _hotSub, t, b));
            }
        }

        if (SelectedPart == EnginePart.None)
        {
            AddSlot(_hotSub, "FAN BLADE 14", healthy ? "SIGNED OFF · OK" : "LE NICK · 0.42 mm");
            foreach (var (sub, title, detail) in GroupLabels)
            {
                AddSlot(sub, title, detail);
            }
        }
        else
        {
            for (var i = 0; i < _subs.Length; i++)
            {
                if (_subs[i].Group == SelectedPart && !string.IsNullOrEmpty(_subs[i].Label) && i != _hotSub)
                {
                    AddSlot(i, _subs[i].Label!, null);
                }
            }
        }

        var a = _labelAlpha * Math.Clamp(_selectionAge * 3f, 0f, 1f);
        if (a < 0.01f)
        {
            return;
        }

        var ordered = _labelSlots.OrderByDescending(s => s.Hot).ThenBy(s => (s.Top.X + s.Bottom.X) / 2f).ToList();
        var alternate = 0;
        foreach (var slot in ordered)
        {
            var preferUp = slot.Hot || alternate++ % 2 == 0;
            if (!TryPlaceLabel(slot, preferUp, out var rect, out var up))
            {
                continue;
            }

            _labelRects.Add(rect);
            var hot = slot.Hot && !healthy;
            var accent = healthy ? _green : hot ? FaultColor : _cyan;
            var textColor = _light ? _text : slot.Detail is null ? accent : Mix(accent, Colors.White, 0.35f);
            var anchor = up ? slot.Top : slot.Bottom;
            var endY = up ? (float)(rect.Y + rect.Height) + 3f : (float)rect.Y - 3f;
            var cx = (float)(rect.X + rect.Width / 2);
            var elbow = new Vector2(anchor.X, anchor.Y + (endY - anchor.Y) * a);
            // Subtle by default; HighContrast keeps full strength.
            var tone = _highContrast ? 1f : hot ? 0.85f : 0.62f;
            ds.FillCircle(anchor, 1.8f, WithAlpha(accent, 0.8f * tone * a));
            ds.DrawLine(anchor, elbow, WithAlpha(accent, 0.55f * tone * a), 0.8f);
            ds.DrawLine(Math.Min(anchor.X, cx - 5f), endY, Math.Max(anchor.X, cx + 5f), endY, WithAlpha(accent, 0.7f * tone * a), 0.8f);

            if (!_highContrast)
            {
                ds.FillRoundedRectangle(rect, 3f, 3f, WithAlpha(_canvas, 0.55f * a));
            }

            if (hot)
            {
                ds.DrawRoundedRectangle(rect, 3f, 3f, WithAlpha(FaultColor, 0.55f * a), 0.8f);
            }

            var titleFormat = slot.Detail is null ? _tagFormat! : _subFormat!;
            titleFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
            ds.DrawText(slot.Title, (float)rect.X, (float)rect.Y + 1f, (float)rect.Width, 15f, WithAlpha(textColor, 0.85f * tone * a), titleFormat);
            titleFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
            if (slot.Detail is not null)
            {
                _tagFormat!.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                ds.DrawText(slot.Detail, (float)rect.X, (float)rect.Y + 15f, (float)rect.Width, 13f, WithAlpha(_light ? _text : accent, 0.65f * tone * a), _tagFormat);
                _tagFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
            }
        }
    }

    private bool TryPlaceLabel(LabelSlot slot, bool preferUp, out Rect rect, out bool up)
    {
        var width = Math.Max(slot.Title.Length * (slot.Detail is null ? 6f : 6.5f), (slot.Detail?.Length ?? 0) * 6f) + 12f;
        var height = slot.Detail is null ? 16f : 29f;
        Rect? fallback = null;
        var fallbackUp = preferUp;
        for (var pass = 0; pass < 2; pass++)
        {
            up = pass == 0 ? preferUp : !preferUp;
            var anchor = up ? slot.Top : slot.Bottom;
            for (var step = 0; step < 10; step++)
            {
                var length = (slot.Hot ? 130f : 34f) + 20f * step;
                var y = up ? anchor.Y - length - height : anchor.Y + length;
                var x = Math.Clamp(anchor.X - width / 2f, (float)_safe.X, (float)(_safe.X + _safe.Width) - width);
                rect = new Rect(x, y, width, height);
                if (y < _safe.Y || y + height > _safe.Y + _safe.Height)
                {
                    break;
                }

                if (fallback is null)
                {
                    fallback = rect;
                    fallbackUp = up;
                }

                var candidate = rect;
                if (!_labelRects.Any(r => Overlaps(r, candidate)))
                {
                    return true;
                }
            }
        }

        // Only the flagged blade is guaranteed a tag; everything else is dropped rather than overlapped.
        up = fallbackUp;
        rect = fallback ?? default;
        return slot.Hot && fallback is not null;
    }

    private Rect CalloutRect(Vector2 anchor)
    {
        var dir = anchor.X + 300 > _safe.X + _safe.Width ? -1f : 1f;
        var elbow = anchor + new Vector2(70f * dir, -80f);
        var end = elbow + new Vector2(200f * dir, 0f);
        return new Rect(Math.Min(anchor.X, end.X), elbow.Y - 30f, Math.Abs(end.X - anchor.X), 110f);
    }

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);

    private void DrawCallout(CanvasDrawingSession ds, EnginePart part, Vector2 anchor, double now, bool healthy)
    {
        var flagged = !healthy;
        var (title, detail) = part switch
        {
            EnginePart.Fan => ("FAN · BLADE 14", flagged ? "LEADING-EDGE NICK · 0.42 mm" : "BLADE 14 · SIGNED OFF"),
            EnginePart.Nacelle => ("NACELLE", flagged ? "INLET COWL · NOMINAL" : "INLET COWL · NOMINAL"),
            EnginePart.Compressor => ("HP COMPRESSOR", "N2 VIB 0.8 ips · IN BAND"),
            EnginePart.Turbine => ("HP TURBINE", "EGT MARGIN 42 °C · OK"),
            _ => (string.Empty, string.Empty),
        };
        var accent = flagged && part == EnginePart.Fan ? FaultColor : healthy ? _green : _cyan;
        var appear = Math.Clamp(_selectionAge * 3f, 0f, 1f);
        if (_hover != EnginePart.None && _hover != SelectedPart)
        {
            appear = 1f;
        }

        var flip = anchor.X + 300 > _safe.X + _safe.Width;
        var dir = flip ? -1f : 1f;
        var elbow = anchor + new Vector2(70f * dir, -80f);
        var end = elbow + new Vector2(180f * dir * appear, 0f);
        var lineColor = WithAlpha(accent, (_highContrast ? 0.95f : 0.75f) * appear);

        var pulse = _animationsEnabled ? 0.5f + 0.5f * MathF.Sin((float)now * 5f) : 1f;
        ds.DrawCircle(anchor, 6f + 3f * pulse, lineColor, 1.2f);
        ds.FillCircle(anchor, 2.2f, lineColor);
        ds.DrawLine(anchor, elbow, lineColor, 1f);
        ds.DrawLine(elbow, end, lineColor, 1f);

        var textX = flip ? end.X : elbow.X;
        _labelFormat!.HorizontalAlignment = CanvasHorizontalAlignment.Left;
        ds.DrawText(title, textX + 2f, elbow.Y - 22f, WithAlpha(_light ? _text : Mix(accent, Colors.White, 0.3f), (_highContrast ? 1f : 0.88f) * appear), _labelFormat);
        ds.DrawText(detail, textX + 2f, elbow.Y + 5f, WithAlpha(_light ? _text : accent, (_highContrast ? 0.9f : 0.7f) * appear), _subFormat!);
        if (flagged && part == EnginePart.Fan && IsExploded)
        {
            ds.DrawText("TAP BLADE TIP  →  BORESCOPE", textX + 2f, elbow.Y + 23f, WithAlpha(_light ? _text : Mix(accent, Colors.White, 0.2f), (0.35f + 0.25f * pulse) * appear), _tagFormat!);
        }
    }

    private void DrawScanlines(CanvasDrawingSession ds, float w, float h)
    {
        var c = WithAlpha(_canvas, 0.22f);
        for (var y = 0f; y < h; y += 3f)
        {
            ds.DrawLine(0, y, w, y, c, 1f);
        }
    }

    private bool IsEngineHit(Point point, float slop)
    {
        foreach (var (_, r) in _partBounds)
        {
            var inflated = new Rect(r.X - 24, r.Y - 24, r.Width + 48, r.Height + 48);
            if (inflated.Contains(point))
            {
                return true;
            }
        }

        return HitTest(point, slop) != EnginePart.None;
    }

    /// <summary>The red blade tip is a generous target once the engine is exploded and the finding is open.</summary>
    private bool IsFaultHit(Point point, float slop) =>
        IsExploded && _diveStart < 0 && _status != AircraftStatus.Healthy && _hotTip is { } tip
        && Vector2.Distance(tip, new Vector2((float)point.X, (float)point.Y)) < slop + 22f;

    private float PointerSlop => _pointerType is PointerDeviceType.Touch or PointerDeviceType.Pen ? 26f : 14f;

    private EnginePart HitTest(Point point, float threshold = 14f)
    {
        var p = new Vector2((float)point.X, (float)point.Y);
        var best = EnginePart.None;
        var bestDistance = threshold;
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

    public void PointerPressed(UIElement root, PointerRoutedEventArgs e)
    {
        root.CapturePointer(e.Pointer);
        _pressed = true;
        _dragging = false;
        _pressPoint = _lastPoint = e.GetCurrentPoint(root).Position;
        _pointerType = e.Pointer.PointerDeviceType;
        _lastInteraction = _clock.Elapsed.TotalSeconds;
        root.Focus(FocusState.Pointer);
        e.Handled = true;
    }


    public void PointerMoved(UIElement root, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(root).Position;
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
            _hover = IsExploded ? HitTest(pos) : EnginePart.None;
            var clickable = _hover != EnginePart.None || (!IsExploded && IsEngineHit(pos, 14f)) || IsFaultHit(pos, 14f);
            CursorHelper.Set(root, clickable ? InputSystemCursorShape.Hand : InputSystemCursorShape.SizeAll);
        }

        _lastPoint = pos;
    }

    public void PointerReleased(UIElement root, PointerRoutedEventArgs e)
    {
        var wasClick = _pressed && !_dragging;
        _pressed = false;
        _dragging = false;
        root.ReleasePointerCapture(e.Pointer);
        if (!wasClick)
        {
            return;
        }

        var pos = e.GetCurrentPoint(root).Position;
        var slop = PointerSlop;
        if (!IsExploded)
        {
            if (IsEngineHit(pos, slop))
            {
                ExplodeEngine();
            }
        }
        else if (IsFaultHit(pos, slop))
        {
            FaultInvoked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            var part = HitTest(pos, slop);
            if (part == EnginePart.None)
            {
                CollapseEngine();
            }
            else
            {
                Select(part == SelectedPart ? EnginePart.None : part);
            }
        }

        e.Handled = true;
    }

    public void PointerExited() => _hover = EnginePart.None;

    public void PointerCanceled()
    {
        _pressed = false;
        _dragging = false;
    }

    public void PointerWheelChanged(UIElement root, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(root).Properties.MouseWheelDelta;
        _goalDistance = Math.Clamp(_goalDistance * (1f - delta / 1200f), 8f, 90f);
        _lastInteraction = _clock.Elapsed.TotalSeconds;
        e.Handled = true;
    }

    public bool HandleKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Left: _goalYaw += 0.087f; break;
            case VirtualKey.Right: _goalYaw -= 0.087f; break;
            case VirtualKey.Up: _goalPitch = Math.Clamp(_goalPitch + 0.035f, -0.1f, 1.2f); break;
            case VirtualKey.Down: _goalPitch = Math.Clamp(_goalPitch - 0.035f, -0.1f, 1.2f); break;
            case VirtualKey.Home: ResetView(); break;
            case VirtualKey.Enter when IsExploded && _hotTip is not null && _status != AircraftStatus.Healthy:
                FaultInvoked?.Invoke(this, EventArgs.Empty);
                break;
            case VirtualKey.Escape when SelectedPart != EnginePart.None:
                Select(EnginePart.None);
                break;
            case VirtualKey.Escape when IsExploded:
                CollapseEngine();
                break;
            default:
                return false;
        }

        _lastInteraction = _clock.Elapsed.TotalSeconds;
        return true;
    }
}
