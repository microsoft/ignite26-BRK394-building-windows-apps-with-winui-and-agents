using Hangar.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using System.Numerics;
using Windows.UI;
using static Microsoft.UI.Reactor.Advanced.Factories;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Reactor.Advanced.Win2D;

namespace Hangar.Reactor;

/// <summary>Resolves a theme colour for the canvas's actual theme.</summary>
public delegate Color ThemeColor(string key);

public sealed record ChartProps(Action<CanvasDrawingSession, float, float, ThemeColor> Draw, object RedrawKey, string Name);

/// <summary>Win2D canvas that redraws when <see cref="ChartProps.RedrawKey"/> changes (and on resize).</summary>
public sealed class ChartCanvas : Component<ChartProps>
{
    public override Element Render()
    {
        var canvas = UseRef<CanvasControl?>(null);
        var p = Props;
        return Win2DCanvas((ds, _) =>
            {
                if (canvas.Current is not { } c || c.ActualWidth <= 0 || c.ActualHeight <= 0) return;
                p.Draw(ds, (float)c.ActualWidth, (float)c.ActualHeight, k => Kit.Col(c, k));
            }, p.RedrawKey)
            .UseSharedDevice()
            .Set(c =>
            {
                canvas.Current = c;
                c.IsHitTestVisible = false;
            })
            .AutomationName(p.Name);
    }
}

public static class Charts
{
    public static Element Chart(Action<CanvasDrawingSession, float, float, ThemeColor> draw, object key, string name) =>
        Component<ChartCanvas, ChartProps>(new ChartProps(draw, key, name));

    private static readonly CanvasStrokeStyle Dash = new() { DashStyle = CanvasDashStyle.Dash, DashCap = CanvasCapStyle.Flat };
    private static readonly CanvasStrokeStyle Dot = new() { DashStyle = CanvasDashStyle.Dot, DashCap = CanvasCapStyle.Round };
    private static readonly CanvasStrokeStyle Round = new() { LineJoin = CanvasLineJoin.Round, StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

    private static CanvasTextFormat Label(float size = 11, CanvasHorizontalAlignment align = CanvasHorizontalAlignment.Right) => new()
    {
        FontFamily = "Segoe UI Variable Text",
        FontSize = size,
        HorizontalAlignment = align,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private static CanvasGeometry Polyline(ICanvasResourceCreator rc, IReadOnlyList<Vector2> pts, float? baseline = null)
    {
        using var pb = new CanvasPathBuilder(rc);
        if (baseline is { } b)
        {
            pb.BeginFigure(pts[0].X, b);
            foreach (var p in pts) pb.AddLine(p);
            pb.AddLine(pts[^1].X, b);
            pb.EndFigure(CanvasFigureLoop.Closed);
        }
        else
        {
            pb.BeginFigure(pts[0]);
            for (var i = 1; i < pts.Count; i++) pb.AddLine(pts[i]);
            pb.EndFigure(CanvasFigureLoop.Open);
        }

        return CanvasGeometry.CreatePath(pb);
    }

    private static CanvasLinearGradientBrush Fade(ICanvasResourceCreator rc, Color c, float top, float bottom, double a0, double a1) =>
        new(rc, Kit.WithAlpha(c, a0), Kit.WithAlpha(c, a1)) { StartPoint = new(0, top), EndPoint = new(0, bottom) };

    // ── KPI sparkline: area + 2px line + 7px end dot (KpiCard.xaml) ──
    public static Element Sparkline(IReadOnlyList<double> values, string colorKey, bool isDark) =>
        Chart((ds, w, h, col) =>
        {
            if (values.Count < 2) return;
            var min = values.Min();
            var range = Math.Max(values.Max() - min, 0.001);
            const float inset = 4;
            var pts = values.Select((v, i) => new Vector2(
                inset + (i / (float)(values.Count - 1) * (w - (inset * 2))),
                inset + ((float)(1 - ((v - min) / range)) * (h - (inset * 2))))).ToList();
            var c = col(colorKey);
            using var area = Polyline(ds, pts, h);
            using var fill = Fade(ds, c, 0, h, 0.42, 0.0);
            ds.FillGeometry(area, fill);
            using var line = Polyline(ds, pts);
            ds.DrawGeometry(line, c, 2, Round);
            ds.FillCircle(pts[^1], 3.5f, c);
        }, (colorKey, isDark, values.Count, values[^1]), "Trend");

    // ── Fleet availability · 30 days (Y 94–100, target 98, today marker) ──
    public static Element AvailabilityChart(IReadOnlyList<double> series, bool healthy, bool isDark) =>
        Chart((ds, w, h, col) =>
        {
            const double lo = 94, hi = 100;
            const float left = 32, right = 8, top = 8, bottom = 10;
            var plotW = w - left - right;
            var plotH = h - top - bottom;
            float Y(double v) => top + (float)((hi - v) / (hi - lo)) * plotH;
            float X(int i) => left + (i / (float)(series.Count - 1)) * plotW;

            var grid = col("ChartGridColor");
            var tick = col("TertiaryTextBrush");
            using var fmt = Label();
            for (var v = lo; v <= hi; v += 1)
            {
                ds.DrawLine(left, Y(v), w - right, Y(v), grid, 1);
                ds.DrawText(v.ToString("0"), new Windows.Foundation.Rect(0, Y(v) - 8, left - 10, 16), tick, fmt);
            }

            ds.DrawLine(left, top, left, top + plotH, grid, 1);

            var cyan = col("AccentCyanColor");
            var pts = series.Select((v, i) => new Vector2(X(i), Y(v))).ToList();
            using var area = Polyline(ds, pts, top + plotH);
            using var fill = Fade(ds, cyan, top, top + plotH, 0.38, 0.22);
            ds.FillGeometry(area, fill);
            using var line = Polyline(ds, pts);
            ds.DrawGeometry(line, cyan, 2.5f, Round);

            ds.DrawLine(left, Y(FleetDataService.AvailabilityTarget), w - right, Y(FleetDataService.AvailabilityTarget), col("ChartTargetBrush"), 1, Dash);

            var today = pts[^1];
            var mark = col(healthy ? "HealthyColor" : "AttentionColor");
            ds.FillCircle(today, 9, Kit.WithAlpha(mark, 0.25));
            ds.FillCircle(today, 5, mark);
        }, (isDark, healthy, series[^1]), "Fleet availability, last 30 days");

    // ── Engine 2 N1 vibration · last 50 flights ──
    public static Element VibrationChart(IReadOnlyList<double> vib, IReadOnlyList<double> avg, int index, bool isDark) =>
        Chart((ds, w, h, col) =>
        {
            const double lo = 0, hi = 2.6;
            const float left = 30, right = 10, top = 8, bottom = 8;
            var n = vib.Count;
            var plotW = w - left - right;
            var plotH = h - top - bottom;
            float Y(double v) => top + (float)((hi - v) / (hi - lo)) * plotH;
            float X(int i) => left + (i / (float)(n - 1)) * plotW;

            var grid = col("ChartGridColor");
            var tick = col("TertiaryTextBrush");
            using var fmt = Label(10);
            for (var v = lo; v <= hi + 0.001; v += 0.5)
            {
                ds.DrawLine(left, Y(v), w - right, Y(v), grid, 1);
                ds.DrawText(v.ToString("0.0"), new Windows.Foundation.Rect(0, Y(v) - 8, left - 8, 16), tick, fmt);
            }

            // Fleet average band + dotted average.
            var target = col("ChartTargetBrush");
            var bandPts = avg.Select((v, i) => new Vector2(X(i), Y(v + 0.22))).ToList();
            using (var band = Polyline(ds, bandPts, top + plotH))
            {
                ds.FillGeometry(band, col("ChartBandBrush"));
            }

            using (var avgLine = Polyline(ds, avg.Select((v, i) => new Vector2(X(i), Y(v))).ToList()))
            {
                ds.DrawGeometry(avgLine, target, 1, Dot);
            }

            var critical = col("CriticalColor");
            ds.DrawLine(left, Y(FleetDataService.VibrationThreshold), w - right, Y(FleetDataService.VibrationThreshold), critical, 1.2f, Dash);

            var cyan = col("AccentCyanColor");
            var amber = col("AttentionColor");
            var pts = vib.Take(index + 1).Select((v, i) => new Vector2(X(i), Y(v))).ToList();
            if (pts.Count >= 2)
            {
                using var line = Polyline(ds, pts);
                ds.DrawGeometry(line, cyan, 2.5f, Round);
            }

            var spikeStart = Enumerable.Range(0, index + 1).FirstOrDefault(i => vib[i] >= FleetDataService.VibrationThreshold, -1);
            if (spikeStart > 0)
            {
                var exc = pts.Skip(spikeStart - 1).ToList();
                if (exc.Count >= 2)
                {
                    using var line = Polyline(ds, exc);
                    ds.DrawGeometry(line, amber, 3, Round);
                }
            }

            var over = vib[index] >= FleetDataService.VibrationThreshold;
            var m = new Vector2(X(index), Y(vib[index]));
            var mc = over ? amber : cyan;
            ds.DrawLine(m.X, top, m.X, top + plotH, Kit.WithAlpha(mc, 0.35), 1);
            ds.FillCircle(m, 9, Kit.WithAlpha(mc, 0.25));
            ds.FillCircle(m, 5, mc);
        }, (isDark, index), "Engine 2 N1 vibration, last 50 flights");
}
