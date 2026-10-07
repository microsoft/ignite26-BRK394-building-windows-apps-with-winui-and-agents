using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Windows.Foundation;
using Microsoft.Graphics.DirectX;
using Windows.UI;

namespace Hangar.Controls.CompositionStage;

internal enum StageBakeTone { Cyan, Airframe, Amber, Healthy, Floor }

internal sealed record StageLine2D(Vector2[] Points, bool Hot = false);

internal sealed record StageBakedWire(CompositionSurfaceBrush Brush, Vector2 SizeUnits, double BakeMs, bool Cached);

internal sealed class StageBakeStats
{
    public double BakeMs { get; set; }
    public int Cached { get; set; }
    public int Baked { get; set; }
}

internal static class StageBaker
{
    private sealed record CacheItem(CompositionDrawingSurface Surface, Size PixelSize);
    private static readonly Dictionary<string, CacheItem> Cache = [];

    public static StageBakedWire BakeWire(Compositor compositor, StagePalette palette, string id,
        IReadOnlyList<StageLine2D> lines, Vector2 sizeUnits, float ppu, StageBakeTone tone, StageBakeStats stats)
    {
        sizeUnits = new Vector2(Math.Max(1, sizeUnits.X), Math.Max(1, sizeUnits.Y));
        ppu = Math.Clamp(ppu, 1.25f, 5.5f);
        var pixel = ClampPixels(sizeUnits * ppu);
        var key = $"{palette.CacheKey}|{id}|{tone}|{pixel.Width:F0}x{pixel.Height:F0}|{palette.HighContrast}";
        if (Cache.TryGetValue(key, out var cached))
        {
            stats.Cached++;
            return new StageBakedWire(MakeBrush(compositor, cached.Surface), sizeUnits, 0, true);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var device = CanvasDevice.GetSharedDevice();
        var graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device);
        var surface = graphics.CreateDrawingSurface(pixel, DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        using (var ds = CanvasComposition.CreateDrawingSession(surface))
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            if (!palette.HighContrast && tone != StageBakeTone.Floor)
            {
                using var glow = new CanvasCommandList(device);
                using (var gd = glow.CreateDrawingSession())
                {
                    DrawLines(gd, lines, pixel, sizeUnits, GlowColor(palette, tone), tone == StageBakeTone.Airframe ? 4.2f : 7.5f);
                }

                ds.DrawImage(new GaussianBlurEffect
                {
                    Source = glow,
                    BlurAmount = tone == StageBakeTone.Airframe ? 4.5f : 8.0f,
                    BorderMode = EffectBorderMode.Soft,
                    Optimization = EffectOptimization.Speed,
                });
            }

            DrawLines(ds, lines, pixel, sizeUnits, CoreColor(palette, tone), tone == StageBakeTone.Airframe ? 1.15f : 1.65f);
            if (tone == StageBakeTone.Cyan || tone == StageBakeTone.Amber || tone == StageBakeTone.Healthy)
            {
                DrawLines(ds, lines, pixel, sizeUnits, StagePalette.WithAlpha(Color.FromArgb(255, 255, 255, 255), palette.HighContrast ? (byte)0 : (byte)90), 0.65f);
            }
        }

        sw.Stop();
        stats.Baked++;
        stats.BakeMs += sw.Elapsed.TotalMilliseconds;
        Cache[key] = new CacheItem(surface, pixel);
        return new StageBakedWire(MakeBrush(compositor, surface), sizeUnits, sw.Elapsed.TotalMilliseconds, false);
    }

    private static CompositionSurfaceBrush MakeBrush(Compositor compositor, ICompositionSurface surface)
    {
        var brush = compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        return brush;
    }

    private static Size ClampPixels(Vector2 requested)
    {
        var scale = Math.Min(1f, 4096f / Math.Max(requested.X, requested.Y));
        return new Size(Math.Max(1, MathF.Ceiling(requested.X * scale)), Math.Max(1, MathF.Ceiling(requested.Y * scale)));
    }

    private static Color CoreColor(StagePalette palette, StageBakeTone tone) => tone switch
    {
        StageBakeTone.Airframe => palette.AirframeColor,
        StageBakeTone.Amber => palette.AmberColor,
        StageBakeTone.Healthy => palette.HealthyColor,
        StageBakeTone.Floor => StagePalette.WithAlpha(palette.AirframeColor, palette.HighContrast ? (byte)180 : (byte)100),
        _ => palette.CyanCoreColor,
    };

    private static Color GlowColor(StagePalette palette, StageBakeTone tone) => tone switch
    {
        StageBakeTone.Airframe => StagePalette.WithAlpha(palette.AirframeColor, 62),
        StageBakeTone.Amber => StagePalette.WithAlpha(palette.AmberColor, 74),
        StageBakeTone.Healthy => StagePalette.WithAlpha(palette.HealthyColor, 82),
        _ => palette.CyanGlowColor,
    };

    private static void DrawLines(CanvasDrawingSession ds, IReadOnlyList<StageLine2D> lines, Size pixel, Vector2 sizeUnits, Color color, float stroke)
    {
        var sx = (float)pixel.Width / Math.Max(1, sizeUnits.X);
        var sy = (float)pixel.Height / Math.Max(1, sizeUnits.Y);
        using var brush = new CanvasSolidColorBrush(ds, color);
        foreach (var line in lines)
        {
            for (var i = 1; i < line.Points.Length; i++)
            {
                var a = new Vector2(line.Points[i - 1].X * sx, line.Points[i - 1].Y * sy);
                var b = new Vector2(line.Points[i].X * sx, line.Points[i].Y * sy);
                ds.DrawLine(a, b, brush, stroke);
            }
        }
    }
}


