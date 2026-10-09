using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Hangar.Controls;

/// <summary>
/// Win2D hologram globe of the Contoso Air network. Orthographic dot-matrix earth, great-circle
/// routes, live red-eye comets, and CA 2207's scrubbed leg colored by Engine 2 vibration.
/// The control renders into a page-wide canvas and interpolates between a compact slot (the
/// Network card) and an expanded slot (the stage) so the transition is one continuous spring.
/// </summary>
public sealed partial class FleetGlobe : UserControl
{
    const double Deg = Math.PI / 180.0;
    const int ArcSamples = 64;
    const double DefaultLon = -108, DefaultLat = 34;

    static readonly Vector3[] LandDots = LoadLand();

    readonly FleetDataService _data = FleetDataService.Instance;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly AccessibilitySettings _accessibility = new();
    readonly UISettings _uiSettings = new();
    readonly Dictionary<(string, string), Vector3[]> _arcs = [];

    readonly Spring _yaw = new(5.5, DefaultLon);
    readonly Spring _pitch = new(5.5, DefaultLat);
    readonly Spring _zoom = new(9, 1);
    readonly Spring _expand = new(10, 0);

    double _lastFrame, _legStart = -10;
    int _flight = 50, _frame;
    bool _subscribed, _dragging, _expanded;
    Point _dragLast;
    double _dragLastTime, _velYaw, _velPitch;
    Rect _compactSlot, _expandedSlot;
    double _lastReportedExpand = -1;

    Color _line, _accent, _amber, _critical, _canvas, _text;
    bool _light, _highContrast, _animationsEnabled = true;

    CanvasRenderTarget? _landCache;
    (float Yaw, float Pitch, float R, int Theme) _landKey = (float.NaN, 0, 0, 0);

    public FleetGlobe()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RefreshTheme();
        _data.Reset += OnDataReset;
    }

    /// <summary>Raised every frame while the expand spring moves (0 = compact card, 1 = stage).</summary>
    public event EventHandler<double>? ExpandProgressChanged;

    public bool IsExpanded => _expanded;
    public int Flight => _flight;
    public double ExpandProgress => _expand.Value;

    public void SetSlots(Rect compact, Rect expanded)
    {
        _compactSlot = compact;
        _expandedSlot = expanded;
    }

    /// <summary>Highlights CA 2207's leg for <paramref name="flight"/> (1-based) and springs the camera onto it.</summary>
    public void SetFlight(int flight)
    {
        flight = Math.Clamp(flight, 1, _data.Legs.Count);
        if (flight == _flight && !double.IsNaN(_legStart) && _legStart > -10) return;
        _flight = flight;
        _legStart = _clock.Elapsed.TotalSeconds;
        var leg = _data.Legs[flight - 1];
        var a = Unit(FleetDataService.Airports[leg.From]);
        var b = Unit(FleetDataService.Airports[leg.To]);
        var mid = Vector3.Normalize(a + b);
        double lat = Math.Asin(Math.Clamp(mid.Y, -1, 1)) / Deg;
        double lon = Math.Atan2(mid.X, mid.Z) / Deg;
        _yaw.Target = _yaw.Value + WrapDegrees(lon - _yaw.Value);
        _pitch.Target = Math.Clamp(lat - 4, -20, 60);
        if (!_animationsEnabled) { _yaw.Snap(); _pitch.Snap(); }
    }

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        _expand.Target = expanded ? 1 : 0;
        if (!expanded) { _zoom.Target = 1; _dragging = false; }
        if (!_animationsEnabled) { _expand.Snap(); _zoom.Snap(); }
    }

    public void ResetView()
    {
        _zoom.Target = 1;
        SetFlight(_flight);
    }

    /// <summary>Screen point (in this control's coordinates) of an airport once the globe is fully expanded.</summary>
    public Point ExpandedPointOf(string code)
    {
        if (_expandedSlot.Width <= 0) return new Point(ActualWidth / 2, ActualHeight / 2);
        var cam = Camera.Create(_expandedSlot, RadiusFactor(1), _zoom.Target, _yaw.Target, _pitch.Target);
        var p = cam.Project(cam.View(Unit(FleetDataService.Airports[code])));
        return new Point(p.X, p.Y);
    }

    // ---- input (routed in by the host) ----------------------------------------------------

    public void BeginDrag(Point p)
    {
        _dragging = true;
        _dragLast = p;
        _dragLastTime = _clock.Elapsed.TotalSeconds;
        _velYaw = _velPitch = 0;
    }

    public void Drag(Point p)
    {
        if (!_dragging) return;
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Max(1e-3, now - _dragLastTime);
        double r = Math.Max(60, CurrentRadius());
        double dYaw = -(p.X - _dragLast.X) / r / Deg;
        double dPitch = (p.Y - _dragLast.Y) / r / Deg;
        _yaw.Value += dYaw; _yaw.Target = _yaw.Value; _yaw.Velocity = 0;
        _pitch.Value = Math.Clamp(_pitch.Value + dPitch, -70, 75); _pitch.Target = _pitch.Value; _pitch.Velocity = 0;
        _velYaw = 0.6 * _velYaw + 0.4 * (dYaw / dt);
        _velPitch = 0.6 * _velPitch + 0.4 * (dPitch / dt);
        _dragLast = p;
        _dragLastTime = now;
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_clock.Elapsed.TotalSeconds - _dragLastTime > 0.12) return;
        _yaw.Target = _yaw.Value + Math.Clamp(_velYaw * 0.22, -60, 60);
        _pitch.Target = Math.Clamp(_pitch.Value + _velPitch * 0.22, -70, 75);
    }

    public void ZoomBy(double wheelDelta) => _zoom.Target = Math.Clamp(_zoom.Target * Math.Pow(1.12, wheelDelta / 120.0), 0.75, 2.6);

    public void Spin(double yawDegrees, double pitchDegrees)
    {
        _yaw.Target += yawDegrees;
        _pitch.Target = Math.Clamp(_pitch.Target + pitchDegrees, -70, 75);
    }

    // ---- lifecycle ------------------------------------------------------------------------

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshTheme();
        _lastFrame = _clock.Elapsed.TotalSeconds;
        if (!_subscribed) { CompositionTarget.Rendering += OnRendering; _subscribed = true; }
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) { CompositionTarget.Rendering -= OnRendering; _subscribed = false; }
        _landCache?.Dispose();
        _landCache = null;
    }

    void OnDataReset(object? sender, EventArgs e) => DispatcherQueue?.TryEnqueue(() =>
    {
        SetExpanded(false);
        _zoom.Target = 1;
        SetFlight(_data.Legs.Count);
    });

    void RefreshTheme()
    {
        _accent = ThemeRes.Get<Color>(this, "AccentCyanColor");
        _amber = ThemeRes.Get<Color>(this, "AttentionColor");
        _critical = ThemeRes.Get<Color>(this, "CriticalColor");
        _canvas = ThemeRes.Get<Color>(this, "CanvasColor");
        _text = ThemeRes.Brush(this, "PrimaryTextBrush") is SolidColorBrush s ? s.Color : Colors.White;
        _highContrast = _accessibility.HighContrast;
        _light = !_highContrast && ActualTheme == ElementTheme.Light;
        _animationsEnabled = _uiSettings.AnimationsEnabled;
        _line = _highContrast ? _text : _light ? Color.FromArgb(255, 0x0B, 0x6A, 0x80) : _accent;
        _landKey = (float.NaN, 0, 0, 0);
        Canvas.Invalidate();
    }

    void OnRendering(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastFrame, 0, 0.05);
        _lastFrame = now;

        foreach (var s in (Spring[])[_yaw, _pitch, _zoom, _expand]) s.Step(dt);

        if (Math.Abs(_expand.Value - _lastReportedExpand) > 1e-4)
        {
            _lastReportedExpand = _expand.Value;
            ExpandProgressChanged?.Invoke(this, _expand.Value);
        }

        // Pause when the page or slot is not visible.
        if (Visibility != Visibility.Visible || ActualWidth < 1 || CurrentRect().Width < 8) return;

        // Compact + settled: the comets crawl, so 30fps is indistinguishable and halves the cost.
        bool busy = _expanded || _dragging || !_yaw.Settled || !_pitch.Settled || !_zoom.Settled || !_expand.Settled
                    || now - _legStart < 6;
        if (!busy && (++_frame & 1) == 1) return;
        Canvas.Invalidate();
    }

    // ---- drawing --------------------------------------------------------------------------

    static double RadiusFactor(double expand) => 0.94 + (0.80 - 0.94) * expand;

    Rect CurrentRect()
    {
        double t = _expand.Value;
        if (_compactSlot.Width <= 0) return _expandedSlot;
        if (_expandedSlot.Width <= 0) return _compactSlot;
        return new Rect(
            Lerp(_compactSlot.X, _expandedSlot.X, t), Lerp(_compactSlot.Y, _expandedSlot.Y, t),
            Math.Max(0, Lerp(_compactSlot.Width, _expandedSlot.Width, t)), Math.Max(0, Lerp(_compactSlot.Height, _expandedSlot.Height, t)));
    }

    double CurrentRadius()
    {
        var r = CurrentRect();
        return Math.Min(r.Width, r.Height) * 0.5 * RadiusFactor(Math.Clamp(_expand.Value, 0, 1)) * _zoom.Value;
    }

    void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var rect = CurrentRect();
        if (rect.Width < 8 || rect.Height < 8) return;
        var ds = args.DrawingSession;
        double now = _clock.Elapsed.TotalSeconds;
        double expand = Math.Clamp(_expand.Value, 0, 1);
        var cam = Camera.Create(rect, RadiusFactor(expand), _zoom.Value, _yaw.Value, _pitch.Value);
        float s = Math.Clamp(cam.R / 300f, 0.55f, 1.6f);

        using var layer = ds.CreateLayer(1f, rect);

        DrawEarth(sender, ds, cam);

        using var lines = new CanvasCommandList(sender);
        var labels = new List<(Vector2 At, Vector2 Away, string Text, Color Color)>();
        using (var lds = lines.CreateDrawingSession())
        {
            DrawNetwork(lds, cam, s, now);
            DrawHero(lds, cam, s, now);
            DrawLeg(sender, lds, cam, s, now, labels);
        }
        DrawGlow(ds, lines, 5f * s);
        DrawLabels(sender, ds, labels, (float)Lerp(10.5, 12.5, expand));
    }

    void DrawEarth(CanvasControl sender, CanvasDrawingSession ds, Camera cam)
    {
        int theme = _highContrast ? 2 : _light ? 1 : 0;
        var key = ((float)Math.Round(cam.YawDeg, 2), (float)Math.Round(cam.PitchDeg, 2), (float)Math.Round(cam.R, 1), theme);
        float size = MathF.Ceiling(cam.R * 2.36f / 32f) * 32f + 32f;

        if (_landCache is null || _landCache.Device != sender.Device || _landCache.Size.Width < size
            || _landCache.Size.Width > size * 1.6 || Math.Abs(_landCache.Dpi - sender.Dpi) > 0.1)
        {
            _landCache?.Dispose();
            _landCache = new CanvasRenderTarget(sender, size, size);
            _landKey = (float.NaN, 0, 0, 0);
        }

        float half = (float)_landCache.Size.Width / 2f;
        if (!key.Equals(_landKey))
        {
            _landKey = key;
            using var cds = _landCache.CreateDrawingSession();
            cds.Clear(Colors.Transparent);
            var local = cam with { C = new Vector2(half, half) };
            PaintEarth(sender, cds, local);
        }
        ds.DrawImage(_landCache, cam.C.X - half, cam.C.Y - half);
    }

    void PaintEarth(ICanvasResourceCreator rc, CanvasDrawingSession ds, Camera cam)
    {
        float r = cam.R;
        var c = cam.C;

        // Ocean body: subtly lit from the upper left so the disc reads as a sphere.
        if (_highContrast)
        {
            ds.FillCircle(c, r, _canvas);
        }
        else
        {
            using var body = new CanvasRadialGradientBrush(rc,
            [
                new CanvasGradientStop { Position = 0f, Color = _light ? Color.FromArgb(255, 0xF2, 0xF7, 0xF9) : Color.FromArgb(235, 0x10, 0x26, 0x30) },
                new CanvasGradientStop { Position = 0.75f, Color = _light ? Color.FromArgb(255, 0xE1, 0xEC, 0xF0) : Color.FromArgb(240, 0x0A, 0x16, 0x1D) },
                new CanvasGradientStop { Position = 1f, Color = _light ? Color.FromArgb(255, 0xCF, 0xE0, 0xE7) : Color.FromArgb(245, 0x07, 0x0F, 0x15) },
            ])
            {
                Center = c + new Vector2(-0.32f, -0.38f) * r,
                RadiusX = r * 1.45f,
                RadiusY = r * 1.45f,
            };
            ds.FillCircle(c, r, body);

            // Atmosphere: a soft halo hugging the limb.
            using var halo = new CanvasRadialGradientBrush(rc,
            [
                new CanvasGradientStop { Position = 0f, Color = WithAlpha(_line, 0) },
                new CanvasGradientStop { Position = 0.80f, Color = WithAlpha(_line, 0) },
                new CanvasGradientStop { Position = 0.885f, Color = WithAlpha(_line, _light ? 0.20 : 0.30) },
                new CanvasGradientStop { Position = 0.905f, Color = WithAlpha(_line, _light ? 0.10 : 0.16) },
                new CanvasGradientStop { Position = 1f, Color = WithAlpha(_line, 0) },
            ])
            {
                Center = c,
                RadiusX = r * 1.13f,
                RadiusY = r * 1.13f,
            };
            ds.FillCircle(c, r * 1.13f, halo);
        }

        // Graticule every 30°.
        float gw = Math.Max(0.5f, r / 500f);
        var grid = WithAlpha(_line, _highContrast ? 0.5 : _light ? 0.16 : 0.11);
        for (int lon = -180; lon < 180; lon += 30)
            DrawCurve(ds, cam, i => Unit(-90 + i * 2.0, lon), 91, grid, gw);
        for (int lat = -60; lat <= 60; lat += 30)
            DrawCurve(ds, cam, i => Unit(lat, -180 + i * 2.0), 181, grid, gw);

        // Dot-matrix land with a fresnel lift towards the limb.
        float dot = Math.Clamp(r * 0.0062f, 0.62f, 2.3f);
        double baseAlpha = _highContrast ? 1.0 : _light ? 0.70 : 0.62;
        using var dots = new CanvasCommandList(rc);
        using (var dds = dots.CreateDrawingSession())
        {
            foreach (var p in LandDots)
            {
                var v = cam.View(p);
                if (v.Z <= 0.0f) continue;
                double fade = SmoothStep(0.0, 0.28, v.Z);
                double fresnel = 0.62 + 0.38 * (1 - v.Z);
                dds.FillCircle(cam.Project(v), dot * (float)(0.75 + 0.25 * fade), WithAlpha(_line, baseAlpha * fade * fresnel));
            }
        }
        DrawGlow(ds, dots, Math.Max(1.2f, r / 160f), wideFactor: 2.2f, nearAlpha: 0.65f);

        // Crisp limb.
        ds.DrawCircle(c, r, WithAlpha(_line, _highContrast ? 1 : _light ? 0.42 : 0.40), Math.Max(0.8f, r / 300f));
    }

    void DrawNetwork(CanvasDrawingSession ds, Camera cam, float s, double now)
    {
        // Stations: parked aircraft as dim dots sized by count.
        var parked = new Dictionary<string, int>();
        foreach (var f in _data.LiveFlights)
            if (!f.IsAirborne && !f.IsHero) parked[f.From] = parked.GetValueOrDefault(f.From) + 1;

        foreach (var (code, ap) in FleetDataService.Airports)
        {
            var v = cam.View(Unit(ap));
            if (v.Z <= 0.02f) continue;
            var p = cam.Project(v);
            int n = parked.GetValueOrDefault(code);
            double a = (_highContrast ? 0.9 : 0.42) * SmoothStep(0.02, 0.25, v.Z);
            if (n > 0) ds.FillCircle(p, (1.3f + 0.42f * n) * s, WithAlpha(_line, a));
            else ds.DrawCircle(p, 1.8f * s, WithAlpha(_line, a * 0.8), 0.7f * s);
        }

        // Airborne red-eyes: faint full route + comet with a fading trail.
        foreach (var f in _data.LiveFlights)
        {
            if (!f.IsAirborne) continue;
            var arc = Arc(f.From, f.To);
            DrawArcSpan(ds, cam, arc, 0, 1, WithAlpha(_line, _highContrast ? 0.6 : _light ? 0.18 : 0.13), 0.7f * s, 0.7f * s);

            double prog = Math.Clamp(_data.Progress(f), 0, 1);
            const double trail = 0.2;
            double t0 = Math.Max(0, prog - trail);
            const int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                double a0 = t0 + (prog - t0) * i / steps, a1 = t0 + (prog - t0) * (i + 1) / steps;
                double k = (i + 1.0) / steps;
                var (p0, vis0) = cam.ProjectLifted(SampleArc(arc, a0));
                var (p1, vis1) = cam.ProjectLifted(SampleArc(arc, a1));
                if (!vis0 || !vis1) continue;
                ds.DrawLine(p0, p1, WithAlpha(_line, (_highContrast ? 1 : 0.85) * k * k), (0.5f + 1.5f * (float)k) * s, RoundStroke);
            }
            var (head, visible) = cam.ProjectLifted(SampleArc(arc, prog));
            if (!visible) continue;
            ds.FillCircle(head, 4.2f * s, WithAlpha(_line, _highContrast ? 0 : 0.22));
            ds.FillCircle(head, 1.9f * s, _highContrast ? _line : Blend(_line, _light ? _line : Colors.White, 0.55));
        }
    }

    void DrawHero(CanvasDrawingSession ds, Camera cam, float s, double now)
    {
        var v = cam.View(Unit(FleetDataService.Airports["SEA"]));
        if (v.Z <= 0) return;
        var p = cam.Project(v);
        double b = _animationsEnabled ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * now / 2.4) : 0.5;
        ds.FillCircle(p, (5.5f + 7f * (float)b) * s, WithAlpha(_amber, _highContrast ? 0 : 0.10 + 0.16 * (1 - b)));
        ds.DrawCircle(p, (5.5f + 7f * (float)b) * s, WithAlpha(_amber, 0.55 * (1 - b)), 1.1f * s);
        ds.FillCircle(p, 3.0f * s, _amber);
    }

    void DrawLeg(ICanvasResourceCreator rc, CanvasDrawingSession ds, Camera cam, float s, double now,
        List<(Vector2, Vector2, string, Color)> labels)
    {
        var leg = _data.Legs[_flight - 1];
        var arc = Arc(leg.From, leg.To);
        var color = VibrationColor(leg.Vibration);
        double age = now - _legStart;
        double draw = _animationsEnabled ? EaseOutCubic(Math.Clamp(age / 0.7, 0, 1)) : 1;
        float w = Math.Max(1.6f, 2.6f * s);

        // Under-glow + main stroke, drawn in progressively.
        DrawArcSpan(ds, cam, arc, 0, draw, WithAlpha(color, _highContrast ? 0 : 0.28), w * 2.6f, w * 2.6f);
        DrawArcSpan(ds, cam, arc, 0, draw, color, w, w);

        // Endpoints.
        var (pa, va) = cam.ProjectLifted(arc[0]);
        var (pb, vb) = cam.ProjectLifted(arc[^1]);
        var (pm, _) = cam.ProjectLifted(arc[ArcSamples / 2]);
        if (va) { ds.DrawCircle(pa, 4.2f * s, color, 1.4f * s); ds.FillCircle(pa, 1.8f * s, color); }
        if (vb && draw > 0.95) { ds.DrawCircle(pb, 4.2f * s, color, 1.4f * s); ds.FillCircle(pb, 1.8f * s, color); }
        if (va) labels.Add((pa, SafeNormalize(pa - pm), leg.From, color));
        if (vb) labels.Add((pb, SafeNormalize(pb - pm), leg.To, color));

        // Crew-report location on flight 44: climb-out of DEN.
        if (leg.CrewReport is not null)
        {
            var (ph, vh) = cam.ProjectLifted(SampleArc(arc, 0.12));
            if (vh && draw > 0.12)
            {
                double pulse = _animationsEnabled ? (now % 1.6) / 1.6 : 0.4;
                ds.DrawCircle(ph, (4 + 11 * (float)pulse) * s, WithAlpha(_critical, 0.9 * (1 - pulse)), 1.3f * s);
                ds.FillCircle(ph, 3.2f * s, _critical);
                ds.DrawCircle(ph, 5.4f * s, _critical, 1f * s);
            }
        }

        // Plane glyph: flies the leg on a slow loop once the arc has drawn in.
        double tp = draw < 1 ? draw : 0.08 + 0.84 * EaseInOut(((age - 0.7) / 6.0) % 1.0);
        var (pp, vp) = cam.ProjectLifted(SampleArc(arc, tp));
        var (pn, _) = cam.ProjectLifted(SampleArc(arc, Math.Min(1, tp + 0.01)));
        var (pq, _) = cam.ProjectLifted(SampleArc(arc, Math.Max(0, tp - 0.01)));
        if (vp)
        {
            var dir = SafeNormalize(pn - pq);
            float scale = Math.Max(0.8f, 1.15f * s);
            var nrm = new Vector2(-dir.Y, dir.X);
            Vector2 P(float fx, float fy) => pp + dir * fx * scale + nrm * fy * scale;
            using var plane = CanvasGeometry.CreatePolygon(rc,
            [
                P(9, 0), P(3, 1.6f), P(-1, 9), P(-3.4f, 9), P(-1.4f, 1.6f), P(-6, 1.4f), P(-8, 4.4f), P(-9.4f, 4.4f),
                P(-8.2f, 0), P(-9.4f, -4.4f), P(-8, -4.4f), P(-6, -1.4f), P(-1.4f, -1.6f), P(-3.4f, -9), P(-1, -9), P(3, -1.6f),
            ]);
            ds.FillCircle(pp, 9f * scale, WithAlpha(color, _highContrast ? 0 : 0.16));
            ds.FillGeometry(plane, _highContrast ? color : Blend(color, _light ? color : Colors.White, 0.35));
        }
    }

    void DrawLabels(ICanvasResourceCreator rc, CanvasDrawingSession ds, List<(Vector2 At, Vector2 Away, string Text, Color Color)> labels, float size)
    {
        if (labels.Count == 0) return;
        using var fmt = new CanvasTextFormat { FontFamily = "Cascadia Mono", FontSize = size, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        foreach (var (at, away, text, color) in labels)
        {
            using var layout = new CanvasTextLayout(rc, text, fmt, 200, 40);
            var b = layout.LayoutBounds;
            float pw = (float)b.Width + 12, ph = (float)b.Height + 4;
            var dir = away.LengthSquared() < 0.01f ? new Vector2(0, -1) : away;
            var center = at + dir * (12 + pw * 0.5f * MathF.Abs(dir.X) + ph * 0.5f * MathF.Abs(dir.Y));
            var box = new Rect(center.X - pw / 2, center.Y - ph / 2, pw, ph);
            var fill = _highContrast ? _canvas : _light ? Color.FromArgb(230, 0xFA, 0xFC, 0xFD) : Color.FromArgb(205, 0x08, 0x10, 0x16);
            ds.FillRoundedRectangle(box, ph / 2, ph / 2, fill);
            ds.DrawRoundedRectangle(box, ph / 2, ph / 2, WithAlpha(color, _highContrast ? 1 : 0.55), 1f);
            ds.DrawTextLayout(layout, (float)box.X + 6, (float)box.Y + 2 - (float)b.Y, _highContrast ? _text : _light ? Blend(color, Colors.Black, 0.25) : Blend(color, Colors.White, 0.35));
        }
    }

    void DrawGlow(CanvasDrawingSession ds, CanvasCommandList list, float blur, float wideFactor = 3.2f, float nearAlpha = 1f)
    {
        if (_light || _highContrast) { ds.DrawImage(list); return; }
        using var wide = new GaussianBlurEffect { Source = list, BlurAmount = blur * wideFactor, BorderMode = EffectBorderMode.Soft, Optimization = EffectOptimization.Speed };
        using var near = new GaussianBlurEffect { Source = list, BlurAmount = blur, BorderMode = EffectBorderMode.Soft, Optimization = EffectOptimization.Speed };
        var old = ds.Blend;
        ds.Blend = CanvasBlend.Add;
        ds.DrawImage(wide);
        ds.DrawImage(near, 0, 0, new Rect(-1e5, -1e5, 2e5, 2e5), nearAlpha);
        ds.DrawImage(list);
        ds.Blend = old;
    }

    // ---- geometry helpers -----------------------------------------------------------------

    static readonly CanvasStrokeStyle RoundStroke = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

    static void DrawCurve(CanvasDrawingSession ds, Camera cam, Func<int, Vector3> point, int count, Color color, float width)
    {
        var prev = cam.View(point(0));
        for (int i = 1; i < count; i++)
        {
            var cur = cam.View(point(i));
            if (prev.Z > 0 && cur.Z > 0)
            {
                float fade = (float)SmoothStep(0, 0.3, Math.Min(prev.Z, cur.Z));
                ds.DrawLine(cam.Project(prev), cam.Project(cur), WithAlpha(color, color.A / 255.0 * fade), width);
            }
            prev = cur;
        }
    }

    static void DrawArcSpan(CanvasDrawingSession ds, Camera cam, Vector3[] arc, double from, double to, Color color, float w0, float w1)
    {
        if (to <= from) return;
        int i0 = (int)Math.Floor(from * ArcSamples), i1 = (int)Math.Ceiling(to * ArcSamples);
        var (prev, prevVis) = cam.ProjectLifted(SampleArc(arc, from));
        for (int i = i0 + 1; i <= i1; i++)
        {
            double t = Math.Min(to, (double)i / ArcSamples);
            var (cur, vis) = cam.ProjectLifted(SampleArc(arc, t));
            if (vis && prevVis) ds.DrawLine(prev, cur, color, (float)Lerp(w0, w1, t), RoundStroke);
            prev = cur; prevVis = vis;
        }
    }

    Vector3[] Arc(string from, string to)
    {
        if (_arcs.TryGetValue((from, to), out var cached)) return cached;
        var a = Unit(FleetDataService.Airports[from]);
        var b = Unit(FleetDataService.Airports[to]);
        double omega = Math.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));
        double lift = Math.Clamp(0.2 * omega, 0.018, 0.2);
        var pts = new Vector3[ArcSamples + 1];
        for (int i = 0; i <= ArcSamples; i++)
        {
            double t = (double)i / ArcSamples;
            Vector3 p = omega < 1e-4 ? a : Vector3.Normalize(
                a * (float)(Math.Sin((1 - t) * omega) / Math.Sin(omega)) + b * (float)(Math.Sin(t * omega) / Math.Sin(omega)));
            pts[i] = p * (float)(1 + lift * Math.Sin(Math.PI * t));
        }
        return _arcs[(from, to)] = pts;
    }

    static Vector3 SampleArc(Vector3[] arc, double t)
    {
        double x = Math.Clamp(t, 0, 1) * ArcSamples;
        int i = Math.Min(ArcSamples - 1, (int)x);
        return Vector3.Lerp(arc[i], arc[i + 1], (float)(x - i));
    }

    Color VibrationColor(double ips) =>
        ips > FleetDataService.VibrationThreshold ? _critical : ips >= 1.4 ? _amber : _line;

    static Vector3 Unit(Airport a) => Unit(a.Lat, a.Lon);

    static Vector3 Unit(double lat, double lon)
    {
        double la = lat * Deg, lo = lon * Deg;
        return new((float)(Math.Cos(la) * Math.Sin(lo)), (float)Math.Sin(la), (float)(Math.Cos(la) * Math.Cos(lo)));
    }

    static Vector3[] LoadLand()
    {
        try
        {
            var asm = typeof(FleetGlobe).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("land-dots.bin", StringComparison.OrdinalIgnoreCase));
            if (name is null) return [];
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            var dots = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                double lat = reader.ReadInt16() / 100.0, lon = reader.ReadInt16() / 100.0;
                dots[i] = Unit(lat, lon);
            }
            return dots;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FleetGlobe: land mask unavailable ({ex.Message})");
            return [];
        }
    }

    static double WrapDegrees(double d) => ((d % 360) + 540) % 360 - 180;
    static double Lerp(double a, double b, double t) => a + (b - a) * t;
    static double SmoothStep(double e0, double e1, double x) { double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); }
    static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    static Vector2 SafeNormalize(Vector2 v) => v.LengthSquared() < 1e-6f ? Vector2.Zero : Vector2.Normalize(v);
    static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    static Color Blend(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)Lerp(a.R, b.R, t), (byte)Lerp(a.G, b.G, t), (byte)Lerp(a.B, b.B, t));

    readonly record struct Camera(Vector2 C, float R, float Cy, float Sy, float Cp, float Sp, double YawDeg, double PitchDeg)
    {
        public static Camera Create(Rect rect, double factor, double zoom, double yawDeg, double pitchDeg)
        {
            float r = (float)(Math.Min(rect.Width, rect.Height) * 0.5 * factor * zoom);
            var c = new Vector2((float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2));
            double y = yawDeg * Deg, p = pitchDeg * Deg;
            return new(c, r, (float)Math.Cos(y), (float)Math.Sin(y), (float)Math.Cos(p), (float)Math.Sin(p), yawDeg, pitchDeg);
        }

        public Vector3 View(Vector3 v)
        {
            float x1 = v.X * Cy - v.Z * Sy, z1 = v.X * Sy + v.Z * Cy;
            return new(x1, v.Y * Cp - z1 * Sp, v.Y * Sp + z1 * Cp);
        }

        public Vector2 Project(Vector3 v) => new(C.X + R * v.X, C.Y - R * v.Y);

        /// <summary>Projects a point above the surface; hidden only when it is behind the sphere's disc.</summary>
        public (Vector2 Point, bool Visible) ProjectLifted(Vector3 world)
        {
            var v = View(world);
            return (Project(v), v.Z > 0 || v.X * v.X + v.Y * v.Y > 1f);
        }
    }

    sealed class Spring(double omega, double value)
    {
        public double Value = value, Target = value, Velocity;

        public bool Settled => Math.Abs(Value - Target) < 1e-3 && Math.Abs(Velocity) < 1e-3;

        public void Snap() { Value = Target; Velocity = 0; }

        public void Step(double dt)
        {
            // Critically damped (c = 2ω), sub-stepped for stability.
            while (dt > 0)
            {
                double h = Math.Min(dt, 1 / 120.0);
                double a = -omega * omega * (Value - Target) - 2 * omega * Velocity;
                Velocity += a * h;
                Value += Velocity * h;
                dt -= h;
            }
            if (Settled) Snap();
        }
    }
}
