using System.Numerics;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.UI;

namespace Hangar.Reactor.Ink;

public enum InkTool { Pen, Highlighter, Eraser }

public sealed class InkStroke
{
    public List<Vector2> Points { get; set; } = new();

    /// <summary>Theme resource key, so ink re-colours with the app theme.</summary>
    public string ColorKey { get; set; } = "AttentionBrush";

    public float Width { get; set; } = 4;

    public bool Highlighter { get; set; }
}

/// <summary>
/// Win2D replacement for InkCanvas/InkPresenter: pointer-driven strokes (pen, mouse, touch),
/// highlighter, stroke eraser, JSON persistence and PNG export.
/// </summary>
public sealed class InkSurface
{
    private static readonly CanvasStrokeStyle RoundStyle = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
        LineJoin = CanvasLineJoin.Round,
    };

    private readonly List<InkStroke> _strokes = new();
    private InkStroke? _active;
    private uint? _pointerId;
    private bool _erasing;

    public InkTool Tool { get; set; } = InkTool.Pen;

    public string ColorKey { get; set; } = "AttentionBrush";

    public float PenWidth { get; set; } = 4;

    public bool IsEnabled { get; set; } = true;

    public int Count => _strokes.Count;

    /// <summary>Stroke collection changed (stroke committed, erased, cleared or loaded).</summary>
    public event Action? Changed;

    /// <summary>The surface needs a redraw.</summary>
    public event Action? Invalidated;

    public void Pressed(UIElement sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled || _pointerId is not null)
        {
            return;
        }

        var point = e.GetCurrentPoint(sender);
        if (point.Properties.IsRightButtonPressed)
        {
            return;
        }

        sender.CapturePointer(e.Pointer);
        _pointerId = e.Pointer.PointerId;
        var position = point.Position.ToVector2();
        _erasing = Tool == InkTool.Eraser || point.Properties.IsEraser;
        if (_erasing)
        {
            EraseAt(position);
        }
        else
        {
            _active = new InkStroke
            {
                ColorKey = ColorKey,
                Width = Tool == InkTool.Highlighter ? 16 : PenWidth,
                Highlighter = Tool == InkTool.Highlighter,
            };
            _active.Points.Add(position);
            Invalidated?.Invoke();
        }

        e.Handled = true;
    }

    public void Moved(UIElement sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId)
        {
            return;
        }

        foreach (var p in e.GetIntermediatePoints(sender).Reverse())
        {
            var position = p.Position.ToVector2();
            if (_erasing)
            {
                EraseAt(position);
            }
            else if (_active is { } stroke && Vector2.DistanceSquared(stroke.Points[^1], position) >= 1)
            {
                stroke.Points.Add(position);
            }
        }

        Invalidated?.Invoke();
        e.Handled = true;
    }

    public void Released(UIElement sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId)
        {
            return;
        }

        sender.ReleasePointerCapture(e.Pointer);
        End();
        e.Handled = true;
    }

    public void Canceled() => End();

    /// <summary>Adds a programmatic stroke (debug rehearsal hook).</summary>
    public void AddStroke(IEnumerable<Vector2> points)
    {
        var stroke = new InkStroke { ColorKey = ColorKey, Width = PenWidth };
        stroke.Points.AddRange(points);
        _strokes.Add(stroke);
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    private void End()
    {
        _pointerId = null;
        _erasing = false;
        if (_active is { } stroke)
        {
            _active = null;
            _strokes.Add(stroke);
            Changed?.Invoke();
        }

        Invalidated?.Invoke();
    }

    private void EraseAt(Vector2 p)
    {
        var removed = _strokes.RemoveAll(s => Hit(s, p, s.Width / 2 + 10));
        if (removed > 0)
        {
            Changed?.Invoke();
            Invalidated?.Invoke();
        }
    }

    private static bool Hit(InkStroke stroke, Vector2 p, float radius)
    {
        var pts = stroke.Points;
        if (pts.Count == 1)
        {
            return Vector2.Distance(pts[0], p) <= radius;
        }

        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var ab = pts[i] - a;
            var t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
            if (Vector2.Distance(a + ab * t, p) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        _active = null;
        _pointerId = null;
        if (_strokes.Count == 0)
        {
            Invalidated?.Invoke();
            return;
        }

        _strokes.Clear();
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public string Save() => JsonSerializer.Serialize(_strokes.Select(s => new StrokeDto(
        s.ColorKey, s.Width, s.Highlighter, s.Points.SelectMany(p => new[] { p.X, p.Y }).ToArray())).ToList());

    public void Load(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<StrokeDto>>(json) ?? new();
        _strokes.Clear();
        foreach (var d in dtos)
        {
            var stroke = new InkStroke { ColorKey = d.ColorKey, Width = d.Width, Highlighter = d.Highlighter };
            for (var i = 0; i + 1 < d.Points.Length; i += 2)
            {
                stroke.Points.Add(new Vector2(d.Points[i], d.Points[i + 1]));
            }

            if (stroke.Points.Count > 0)
            {
                _strokes.Add(stroke);
            }
        }

        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public void Draw(CanvasDrawingSession ds, Func<string, Color> color)
    {
        foreach (var stroke in _strokes)
        {
            DrawStroke(ds, stroke, color(stroke.ColorKey));
        }

        if (_active is { } active)
        {
            DrawStroke(ds, active, color(active.ColorKey));
        }
    }

    /// <summary>Renders the strokes to a transparent PNG (used for the sign-off signature).</summary>
    public async Task SavePngAsync(string path, float width, float height, float dpi, Color color)
    {
        using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, dpi);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            Draw(ds, _ => color);
        }

        await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    private static void DrawStroke(CanvasDrawingSession ds, InkStroke stroke, Color color)
    {
        if (stroke.Highlighter)
        {
            color = Color.FromArgb(110, color.R, color.G, color.B);
        }

        var pts = stroke.Points;
        if (pts.Count == 1)
        {
            ds.FillCircle(pts[0], stroke.Width / 2, color);
            return;
        }

        // Quadratic smoothing through segment midpoints (similar to InkPresenter's FitToCurve).
        using var builder = new CanvasPathBuilder(ds);
        builder.BeginFigure(pts[0]);
        for (var i = 1; i < pts.Count - 1; i++)
        {
            builder.AddQuadraticBezier(pts[i], (pts[i] + pts[i + 1]) / 2);
        }

        builder.AddLine(pts[^1]);
        builder.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(builder);
        ds.DrawGeometry(geometry, color, stroke.Width, RoundStyle);
    }

    private sealed record StrokeDto(string ColorKey, float Width, bool Highlighter, float[] Points);
}
