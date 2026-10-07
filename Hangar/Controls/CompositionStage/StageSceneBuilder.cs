using System.Numerics;
using Hangar.Helpers;
using Hangar.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;

namespace Hangar.Controls.CompositionStage;

internal sealed class StageScene
{
    public required ContainerVisual Root { get; init; }
    public required ContainerVisual World { get; init; }
    public required SpriteVisual Backdrop { get; init; }
    public required ContainerVisual Airframe { get; init; }
    public required ContainerVisual Engine1 { get; init; }
    public required ContainerVisual Engine2 { get; init; }
    public required ContainerVisual[] SubContainers { get; init; }
    public required ShapeVisual PulseRing { get; init; }
    public required List<StageVisualEntry> Entries { get; init; }
    public required Vector3[] SubCentersUnit { get; init; }
    public required Vector3[] SubExplodedCentersUnit { get; init; }
    public int BatchCount { get; init; }
    public int ShapeVisualCount { get; init; }
}

internal sealed record StageVisualEntry(ShapeVisual Visual, CompositionSpriteShape Core, CompositionSpriteShape Glow, StageBatch Batch);

internal static class StageSceneBuilder
{
    public static StageScene Build(Compositor compositor, StagePalette palette, bool animationsEnabled)
    {
        var root = compositor.CreateContainerVisual();
        var world = compositor.CreateContainerVisual();
        var airframe = compositor.CreateContainerVisual();
        var engine1 = compositor.CreateContainerVisual();
        var engine2 = compositor.CreateContainerVisual();
        var lines = WireMesh.BuildA321();
        var subs = WireMesh.SubParts.ToArray();
        var subContainers = Enumerable.Range(0, subs.Length).Select(_ => compositor.CreateContainerVisual()).ToArray();
        var entries = new List<StageVisualEntry>();

        var backdrop = CreateBackdrop(compositor, palette);
        root.Children.InsertAtTop(backdrop);
        root.Children.InsertAtTop(world);
        world.Children.InsertAtTop(CreateFloor(compositor, palette));
        world.Children.InsertAtTop(airframe);
        world.Children.InsertAtTop(engine1);
        world.Children.InsertAtTop(engine2);
        foreach (var c in subContainers)
        {
            c.CenterPoint = StageMath.Engine2OriginUnit;
            c.RotationAxis = Vector3.UnitX;
            engine2.Children.InsertAtTop(c);
        }

        foreach (var batch in StageMath.BuildBatches(lines))
        {
            var entry = CreateBatchVisual(compositor, palette, batch);
            entries.Add(entry);
            if (batch.Spins && animationsEnabled)
            {
                StartSpin(compositor, entry.Visual);
            }

            switch (batch.Kind)
            {
                case StageBatchKind.Engine2 when batch.Sub >= 0:
                    subContainers[batch.Sub].Children.InsertAtTop(entry.Visual);
                    break;
                case StageBatchKind.Engine2:
                    engine2.Children.InsertAtTop(entry.Visual);
                    break;
                case StageBatchKind.Engine1:
                    engine1.Children.InsertAtTop(entry.Visual);
                    break;
                default:
                    airframe.Children.InsertAtTop(entry.Visual);
                    break;
            }
        }

        var pulse = CreatePulseRing(compositor, palette);
        engine2.Children.InsertAtTop(pulse);
        var centers = ComputeSubCenters(entries, subs.Length, false);
        var exploded = ComputeSubCenters(entries, subs.Length, true);
        return new StageScene
        {
            Root = root,
            World = world,
            Backdrop = backdrop,
            Airframe = airframe,
            Engine1 = engine1,
            Engine2 = engine2,
            SubContainers = subContainers,
            PulseRing = pulse,
            Entries = entries,
            SubCentersUnit = centers,
            SubExplodedCentersUnit = exploded,
            BatchCount = entries.Count,
            ShapeVisualCount = entries.Count + 2
        };
    }

    private static StageVisualEntry CreateBatchVisual(Compositor compositor, StagePalette palette, StageBatch batch)
    {
        var shape = compositor.CreateShapeVisual();
        shape.Size = batch.Size;
        shape.TransformMatrix = batch.Transform;
        shape.Opacity = batch.Kind switch
        {
            StageBatchKind.Airframe => 0.82f,
            StageBatchKind.Engine1 => 0.42f,
            _ => 1f,
        };
        shape.CenterPoint = StageMath.Engine2OriginUnit;
        shape.RotationAxis = Vector3.UnitX;

        using var geometry = CreateGeometry(batch.Points);
        var path = compositor.CreatePathGeometry(new CompositionPath(geometry));
        var glow = compositor.CreateSpriteShape(path);
        glow.StrokeBrush = batch.IsHot ? palette.Amber : palette.CyanGlow;
        glow.StrokeThickness = palette.HighContrast ? 0 : batch.Kind == StageBatchKind.Airframe ? 3.2f : 5.0f;
        var core = compositor.CreateSpriteShape(path);
        core.StrokeBrush = batch.IsHot ? palette.Amber : batch.Kind == StageBatchKind.Airframe ? palette.Airframe : palette.CyanCore;
        core.StrokeThickness = batch.Kind == StageBatchKind.Airframe ? 1.05f : 1.35f;
        shape.Shapes.Add(glow);
        shape.Shapes.Add(core);
        return new StageVisualEntry(shape, core, glow, batch);
    }

    private static CanvasGeometry CreateGeometry(Vector2[] points)
    {
        var builder = new CanvasPathBuilder(CanvasDevice.GetSharedDevice());
        builder.BeginFigure(points[0]);
        for (var i = 1; i < points.Length; i++)
        {
            builder.AddLine(points[i]);
        }

        builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    private static SpriteVisual CreateBackdrop(Compositor compositor, StagePalette palette)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = palette.Canvas;
        sprite.Opacity = 0.72f;
        return sprite;
    }

    private static ShapeVisual CreateFloor(Compositor compositor, StagePalette palette)
    {
        var points = new List<Vector2>();
        var minX = -26f * StageMath.K;
        var maxX = 24f * StageMath.K;
        var minZ = -20f * StageMath.K;
        var maxZ = 20f * StageMath.K;
        for (var x = -24; x <= 24; x += 4)
        {
            points.Add(new Vector2(x * StageMath.K - minX, minZ - minZ));
            points.Add(new Vector2(x * StageMath.K - minX, maxZ - minZ));
        }

        for (var z = -20; z <= 20; z += 4)
        {
            points.Add(new Vector2(minX - minX, z * StageMath.K - minZ));
            points.Add(new Vector2(maxX - minX, z * StageMath.K - minZ));
        }

        var shape = compositor.CreateShapeVisual();
        shape.Size = new Vector2(maxX - minX, maxZ - minZ);
        shape.Opacity = 0.22f;
        shape.TransformMatrix = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, -1, 0, 0,
            minX, -(-4.3f * StageMath.K), minZ, 1);
        using var geometry = CreateSegmentGeometry(points);
        var path = compositor.CreatePathGeometry(new CompositionPath(geometry));
        var glow = compositor.CreateSpriteShape(path);
        glow.StrokeBrush = palette.CyanGlow;
        glow.StrokeThickness = palette.HighContrast ? 0 : 2.8f;
        var core = compositor.CreateSpriteShape(path);
        core.StrokeBrush = palette.Airframe;
        core.StrokeThickness = 0.9f;
        shape.Shapes.Add(glow);
        shape.Shapes.Add(core);
        return shape;
    }

    private static CanvasGeometry CreateSegmentGeometry(List<Vector2> points)
    {
        var builder = new CanvasPathBuilder(CanvasDevice.GetSharedDevice());
        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            builder.BeginFigure(points[i]);
            builder.AddLine(points[i + 1]);
            builder.EndFigure(CanvasFigureLoop.Open);
        }

        return CanvasGeometry.CreatePath(builder);
    }

    private static ShapeVisual CreatePulseRing(Compositor compositor, StagePalette palette)
    {
        var shape = compositor.CreateShapeVisual();
        shape.Size = new Vector2(220, 220);
        shape.Offset = StageMath.ToUnit(WireMesh.Engine2Origin + new Vector3(0.4f, 0, 0)) - new Vector3(110, 110, 0);
        shape.TransformMatrix = Matrix4x4.Identity;
        shape.CenterPoint = new Vector3(110, 110, 0);
        shape.RotationAxis = Vector3.UnitY;
        shape.RotationAngle = MathF.PI / 2f;
        shape.Opacity = 0;
        using var ellipse = CanvasGeometry.CreateEllipse(CanvasDevice.GetSharedDevice(), 110, 110, 92, 92);
        var path = compositor.CreatePathGeometry(new CompositionPath(ellipse));
        var pulseGlow = compositor.CreateSpriteShape(path);
        pulseGlow.StrokeBrush = palette.CyanGlow;
        pulseGlow.StrokeThickness = palette.HighContrast ? 0 : 8;
        var pulseCore = compositor.CreateSpriteShape(path);
        pulseCore.StrokeBrush = palette.CyanCore;
        pulseCore.StrokeThickness = 1.5f;
        shape.Shapes.Add(pulseGlow);
        shape.Shapes.Add(pulseCore);
        return shape;
    }

    private static void StartSpin(Compositor compositor, Visual visual)
    {
        visual.CenterPoint = StageMath.Engine2OriginUnit;
        visual.RotationAxis = Vector3.UnitX;
        var spin = compositor.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(1f, MathF.Tau);
        spin.Duration = TimeSpan.FromSeconds(10);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.RotationAngle), spin);
    }

    private static Vector3[] ComputeSubCenters(List<StageVisualEntry> entries, int count, bool exploded)
    {
        var sums = new Vector3[count];
        var seen = new int[count];
        foreach (var e in entries.Where(e => e.Batch.Sub >= 0))
        {
            var sub = e.Batch.Sub;
            sums[sub] += e.Batch.CenterUnit + (exploded ? StageMath.ExplodeOffsetUnit(sub) : Vector3.Zero);
            seen[sub]++;
        }

        for (var i = 0; i < count; i++)
        {
            sums[i] = seen[i] == 0 ? StageMath.Engine2OriginUnit : sums[i] / seen[i];
        }

        return sums;
    }
}