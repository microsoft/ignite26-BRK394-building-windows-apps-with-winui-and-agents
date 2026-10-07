using System.Numerics;
using Hangar.Helpers;
using Hangar.Models;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Hangar.Controls.CompositionStage;

internal sealed class StageScene
{
    public required ContainerVisual Root { get; init; }
    public required ContainerVisual World { get; init; }
    public required SpriteVisual Backdrop { get; init; }
    public required ContainerVisual Airframe { get; init; }
    public required ContainerVisual AirframeRibs { get; init; }
    public required ContainerVisual Engine1 { get; init; }
    public required ContainerVisual Engine2 { get; init; }
    public required ContainerVisual Engine2Parts { get; init; }
    public required ContainerVisual Engine2Effects { get; init; }
    public required List<StageSpritePart> Engine2Sprites { get; init; }
    public required List<StageSpritePart>[] SubSprites { get; init; }
    public required SpriteVisual PulseRing { get; init; }
    public required List<SpriteVisual> Streaks { get; init; }
    public required Vector3[] SubCentersUnit { get; init; }
    public required Vector3[] SubExplodedCentersUnit { get; init; }
    public required StageDepthOrder DepthOrder { get; init; }
    public double BakeMs { get; init; }
    public int CachedSurfaces { get; init; }
    public int BakedSurfaces { get; init; }
}

internal sealed record StageSpritePart(SpriteVisual Visual, int Sub, EnginePart Part, bool IsHot, bool Spins,
    Vector3 RestOffset, Vector3 ExplodedOffset, CompositionBrush NormalBrush, CompositionBrush HealthyBrush, CompositionBrush HotBrush);

internal static class StageSpriteScene
{
    private static readonly Vector3 Engine1Origin = new(6.4f, -2.5f, -5.8f);

    public static StageScene Build(Compositor compositor, StagePalette palette, bool animationsEnabled)
    {
        var stats = new StageBakeStats();
        var root = compositor.CreateContainerVisual();
        var world = compositor.CreateContainerVisual();
        var airframe = compositor.CreateContainerVisual();
        var ribs = compositor.CreateContainerVisual();
        var engine1 = compositor.CreateContainerVisual();
        var engine2 = compositor.CreateContainerVisual();
        var engine2Parts = compositor.CreateContainerVisual();
        var effects = compositor.CreateContainerVisual();
        var lines = WireMesh.BuildA321();
        var subs = WireMesh.SubParts.ToArray();
        var subSprites = Enumerable.Range(0, subs.Length).Select(_ => new List<StageSpritePart>()).ToArray();
        var engine2Sprites = new List<StageSpritePart>(subs.Length);
        var backdrop = CreateBackdrop(compositor, palette);

        root.Children.InsertAtTop(backdrop);
        root.Children.InsertAtTop(world);
        world.Children.InsertAtTop(CreateFloor(compositor, palette, stats));
        world.Children.InsertAtTop(airframe);
        world.Children.InsertAtTop(engine1);
        world.Children.InsertAtTop(engine2);
        airframe.Children.InsertAtTop(ribs);
        engine2.TransformMatrix = EngineFrame(WireMesh.Engine2Origin);
        engine1.TransformMatrix = EngineFrame(Engine1Origin);
        engine2.Children.InsertAtTop(engine2Parts);
        engine2.Children.InsertAtTop(effects);

        BuildAirframe(compositor, palette, stats, lines.Where(l => !l.IsEngine2).ToArray(), airframe, ribs);
        BuildEngine1(compositor, palette, stats, lines.Where(IsEngine1Line).ToArray(), engine1);
        BuildEngine2(compositor, palette, stats, lines.Where(l => l.IsEngine2).GroupBy(l => l.Sub), engine2Parts, engine2Sprites, subSprites);

        var pulse = CreatePulse(compositor, palette, stats);
        effects.Children.InsertAtTop(pulse);
        var streaks = CreateStreaks(compositor, palette, stats, effects);
        if (animationsEnabled)
        {
            foreach (var spin in engine2Sprites.Where(s => s.Spins))
            {
                StartSpin(compositor, spin.Visual);
            }
        }

        var centers = ComputeCenters(lines, subs.Length, false);
        var exploded = ComputeCenters(lines, subs.Length, true);
        return new StageScene
        {
            Root = root,
            World = world,
            Backdrop = backdrop,
            Airframe = airframe,
            AirframeRibs = ribs,
            Engine1 = engine1,
            Engine2 = engine2,
            Engine2Parts = engine2Parts,
            Engine2Effects = effects,
            Engine2Sprites = engine2Sprites,
            SubSprites = subSprites,
            PulseRing = pulse,
            Streaks = streaks,
            SubCentersUnit = centers,
            SubExplodedCentersUnit = exploded,
            DepthOrder = new StageDepthOrder(engine2Parts, ribs, engine2Sprites),
            BakeMs = stats.BakeMs,
            CachedSurfaces = stats.Cached,
            BakedSurfaces = stats.Baked,
        };
    }

    private static Matrix4x4 EngineFrame(Vector3 origin) => Matrix4x4.CreateRotationY(MathF.PI / 2f) * Matrix4x4.CreateTranslation(StageMath.ToUnit(origin));

    private static SpriteVisual CreateBackdrop(Compositor compositor, StagePalette palette)
    {
        var sprite = compositor.CreateSpriteVisual();
        var brush = compositor.CreateRadialGradientBrush();
        brush.EllipseCenter = new Vector2(0.55f, 0.42f);
        brush.EllipseRadius = new Vector2(0.78f, 0.68f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0, StagePalette.WithAlpha(palette.CyanCoreColor, palette.HighContrast ? (byte)28 : (byte)34)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.58f, StagePalette.WithAlpha(palette.CanvasColor, 210)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1, palette.CanvasColor));
        sprite.Brush = brush;
        return sprite;
    }

    private static void BuildEngine2(Compositor compositor, StagePalette palette, StageBakeStats stats,
        IEnumerable<IGrouping<int, WireLine>> groups, ContainerVisual parent, List<StageSpritePart> all, List<StageSpritePart>[] bySub)
    {
        foreach (var group in groups.Where(g => g.Key >= 0).OrderBy(g => g.Key))
        {
            var lines = group.ToArray();
            if (!TryMakeEngineSlice(compositor, palette, stats, $"e2-{group.Key}", lines, WireMesh.Engine2Origin, 5f, out var visual, out var rest, out var brushes))
            {
                continue;
            }

            var hot = lines.Any(l => l.IsHot);
            var part = new StageSpritePart(visual, group.Key, lines[0].Part, hot, lines.Any(l => l.Spins), rest,
                ToEngineLocal(StageMath.ExplodeOffsetUnit(group.Key)), brushes.Normal, brushes.Healthy, hot ? brushes.Hot : brushes.Normal);
            parent.Children.InsertAtTop(visual);
            all.Add(part);
            bySub[group.Key].Add(part);
        }
    }

    private static void BuildEngine1(Compositor compositor, StagePalette palette, StageBakeStats stats, WireLine[] lines, ContainerVisual parent)
    {
        foreach (var group in lines.GroupBy(l => Math.Clamp((int)MathF.Floor((AverageX(l.Points) - (Engine1Origin.X - 5f)) / 1.05f), 0, 5)))
        {
            if (TryMakeEngineSlice(compositor, palette, stats, $"e1-{group.Key}", group.ToArray(), Engine1Origin, 2.5f, out var visual, out _, out _))
            {
                visual.Opacity = 0.44f;
                parent.Children.InsertAtTop(visual);
            }
        }
    }

    private static void BuildAirframe(Compositor compositor, StagePalette palette, StageBakeStats stats, WireLine[] lines, ContainerVisual airframe, ContainerVisual ribs)
    {
        var ringLines = lines.Where(l => !IsEngine1Line(l) && IsFuselageRing(l)).OrderByDescending(l => l.Points[0].X).ToArray();
        foreach (var line in ringLines)
        {
            var sprite = MakePlaneSprite(compositor, palette, stats, $"rib-{line.Points[0].X:F1}", [line], line.Points[0], Vector3.UnitZ, -Vector3.UnitY, 2.35f, StageBakeTone.Airframe);
            sprite.Opacity = 0.46f;
            ribs.Children.InsertAtTop(sprite);
        }

        var rest = lines.Except(ringLines).Where(l => !IsEngine1Line(l)).ToArray();
        AddPlaneGroup(compositor, palette, stats, airframe, "wing-r", rest.Where(l => l.Points.Any(p => p.Z > 2 && p.X > -9 && p.X < 8)), Vector3.UnitZ, Vector3.UnitX);
        AddPlaneGroup(compositor, palette, stats, airframe, "wing-l", rest.Where(l => l.Points.Any(p => p.Z < -2 && p.X > -9 && p.X < 8)), -Vector3.UnitZ, Vector3.UnitX);
        AddPlaneGroup(compositor, palette, stats, airframe, "tail-r", rest.Where(l => l.Points.Any(p => p.Z > 2 && p.X < -14)), Vector3.UnitZ, Vector3.UnitX);
        AddPlaneGroup(compositor, palette, stats, airframe, "tail-l", rest.Where(l => l.Points.Any(p => p.Z < -2 && p.X < -14)), -Vector3.UnitZ, Vector3.UnitX);
        AddPlaneGroup(compositor, palette, stats, airframe, "fin", rest.Where(l => l.Points.Any(p => Math.Abs(p.Z) < 0.8f && p.X < -14 && p.Y > 1)), Vector3.UnitX, Vector3.UnitY);
        AddPlaneGroup(compositor, palette, stats, airframe, "fuse-lines", rest.Where(l => l.Points.Max(p => p.X) - l.Points.Min(p => p.X) > 12), Vector3.UnitX, Vector3.UnitY);
    }

    private static void AddPlaneGroup(Compositor compositor, StagePalette palette, StageBakeStats stats, ContainerVisual parent,
        string id, IEnumerable<WireLine> source, Vector3 axisA, Vector3 axisB)
    {
        var lines = source.Distinct().ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        var origin = lines.SelectMany(l => l.Points).Aggregate(Vector3.Zero, (a, p) => a + p) / lines.Sum(l => l.Points.Length);
        var sprite = MakePlaneSprite(compositor, palette, stats, id, lines, origin, axisA, axisB, 2.25f, StageBakeTone.Airframe);
        sprite.Opacity = 0.42f;
        parent.Children.InsertAtTop(sprite);
    }

    private static SpriteVisual CreateFloor(Compositor compositor, StagePalette palette, StageBakeStats stats)
    {
        var lines = new List<WireLine>();
        void Add(Vector3 a, Vector3 b) => lines.Add(new WireLine([a, b], EnginePart.None, false, false));
        for (var x = -24; x <= 24; x += 4) Add(new(x, -4.35f, -20), new(x, -4.35f, 20));
        for (var z = -20; z <= 20; z += 4) Add(new(-26, -4.35f, z), new(24, -4.35f, z));
        var sprite = MakePlaneSprite(compositor, palette, stats, "floor", lines, new(-26, -4.35f, -20), Vector3.UnitX, Vector3.UnitZ, 1.8f, StageBakeTone.Floor);
        sprite.Opacity = 0.24f;
        return sprite;
    }

    private static SpriteVisual CreatePulse(Compositor compositor, StagePalette palette, StageBakeStats stats)
    {
        var pts = Enumerable.Range(0, 65).Select(i =>
        {
            var a = i / 64f * MathF.Tau;
            return new Vector2(110 + MathF.Cos(a) * 92, 110 + MathF.Sin(a) * 92);
        }).ToArray();
        var baked = StageBaker.BakeWire(compositor, palette, "pulse", [new StageLine2D(pts)], new Vector2(220), 1.7f, StageBakeTone.Cyan, stats);
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = baked.Brush;
        sprite.Size = baked.SizeUnits;
        sprite.CenterPoint = new Vector3(110, 110, 0);
        sprite.RotationAxis = Vector3.UnitY;
        sprite.RotationAngle = MathF.PI / 2f;
        sprite.Opacity = 0;
        return sprite;
    }

    private static List<SpriteVisual> CreateStreaks(Compositor compositor, StagePalette palette, StageBakeStats stats, ContainerVisual parent)
    {
        var baked = StageBaker.BakeWire(compositor, palette, "streak", [new StageLine2D([new(8, 18), new(120, 18)])], new Vector2(128, 36), 2f, StageBakeTone.Cyan, stats);
        var streaks = new List<SpriteVisual>();
        for (var i = 0; i < 6; i++)
        {
            var sprite = compositor.CreateSpriteVisual();
            sprite.Brush = baked.Brush;
            sprite.Size = baked.SizeUnits;
            sprite.CenterPoint = new Vector3(64, 18, 0);
            sprite.Opacity = 0;
            parent.Children.InsertAtTop(sprite);
            streaks.Add(sprite);
        }

        return streaks;
    }

    private static bool TryMakeEngineSlice(Compositor compositor, StagePalette palette, StageBakeStats stats, string id, WireLine[] source,
        Vector3 origin, float ppu, out SpriteVisual visual, out Vector3 rest, out (CompositionBrush Normal, CompositionBrush Healthy, CompositionBrush Hot) brushes)
    {
        var converted = source.Select(l => new StageLine2D(l.Points.Select(p => new Vector2((p.Z - origin.Z) * StageMath.K, -(p.Y - origin.Y) * StageMath.K)).ToArray(), l.IsHot)).ToArray();
        var all = converted.SelectMany(l => l.Points).ToArray();
        visual = compositor.CreateSpriteVisual();
        rest = default;
        brushes = default;
        if (all.Length < 2)
        {
            return false;
        }

        var min = new Vector2(all.Min(p => p.X), all.Min(p => p.Y));
        var max = new Vector2(all.Max(p => p.X), all.Max(p => p.Y));
        var pad = 12f / ppu;
        var local = converted.Select(l => new StageLine2D(l.Points.Select(p => p - min + new Vector2(pad)).ToArray(), l.Hot)).ToArray();
        var size = max - min + new Vector2(pad * 2);
        var hot = source.Any(l => l.IsHot);
        var tone = hot ? StageBakeTone.Amber : StageBakeTone.Cyan;
        var normal = StageBaker.BakeWire(compositor, palette, id, local, size, ppu, tone, stats);
        var healthy = StageBaker.BakeWire(compositor, palette, id + "-healthy", local, size, ppu, StageBakeTone.Healthy, stats);
        var amber = hot ? normal : StageBaker.BakeWire(compositor, palette, id + "-hot", local, size, ppu, StageBakeTone.Amber, stats);
        visual.Brush = normal.Brush;
        visual.Size = normal.SizeUnits;
        visual.CenterPoint = new Vector3(normal.SizeUnits.X * 0.5f, normal.SizeUnits.Y * 0.5f, 0);
        visual.RotationAxis = Vector3.UnitZ;
        rest = new Vector3(min.X - pad, min.Y - pad, (AverageX(source.SelectMany(l => l.Points)) - origin.X) * StageMath.K);
        visual.Offset = rest;
        brushes = (normal.Brush, healthy.Brush, amber.Brush);
        return true;
    }

    private static SpriteVisual MakePlaneSprite(Compositor compositor, StagePalette palette, StageBakeStats stats, string id, IReadOnlyList<WireLine> source,
        Vector3 origin, Vector3 axisA, Vector3 axisB, float ppu, StageBakeTone tone)
    {
        axisA = Vector3.Normalize(axisA);
        axisB = Vector3.Normalize(axisB);
        var raw = source.Select(l => new StageLine2D(l.Points.Select(p => new Vector2(Vector3.Dot(p - origin, axisA) * StageMath.K, Vector3.Dot(p - origin, axisB) * StageMath.K)).ToArray())).ToArray();
        var all = raw.SelectMany(l => l.Points).ToArray();
        var min = new Vector2(all.Min(p => p.X), all.Min(p => p.Y));
        var max = new Vector2(all.Max(p => p.X), all.Max(p => p.Y));
        var pad = 12f / ppu;
        var lines = raw.Select(l => new StageLine2D(l.Points.Select(p => p - min + new Vector2(pad)).ToArray())).ToArray();
        var size = max - min + new Vector2(pad * 2);
        var baked = StageBaker.BakeWire(compositor, palette, id, lines, size, ppu, tone, stats);
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = baked.Brush;
        sprite.Size = baked.SizeUnits;
        var originUnit = StageMath.ToUnit(origin) + UnitAxis(axisA) * (min.X - pad) + UnitAxis(axisB) * (min.Y - pad);
        sprite.TransformMatrix = new Matrix4x4(UnitAxis(axisA).X, UnitAxis(axisA).Y, UnitAxis(axisA).Z, 0,
            UnitAxis(axisB).X, UnitAxis(axisB).Y, UnitAxis(axisB).Z, 0, 0, 0, 1, 0, originUnit.X, originUnit.Y, originUnit.Z, 1);
        return sprite;
    }

    private static Vector3 UnitAxis(Vector3 worldAxis) => Vector3.Normalize(StageMath.ToUnitOffset(worldAxis));
    private static bool IsEngine1Line(WireLine line) => !line.IsEngine2 && line.Points.Average(p => p.Z) < -3.3f && line.Points.Average(p => p.X) < 8;
    private static bool IsFuselageRing(WireLine line) => line.Points.Length > 18 && line.Points.Max(p => p.X) - line.Points.Min(p => p.X) < 0.05f && Vector3.Distance(line.Points[0], line.Points[^1]) < 0.08f;
    private static float AverageX(IEnumerable<Vector3> points) => points.Select(p => p.X).DefaultIfEmpty(0).Average();
    private static float AverageX(Vector3[] points) => points.Select(p => p.X).DefaultIfEmpty(0).Average();
    private static Vector3 ToEngineLocal(Vector3 unitOffset) => new(-unitOffset.Z, unitOffset.Y, unitOffset.X);

    private static Vector3[] ComputeCenters(IReadOnlyList<WireLine> lines, int count, bool exploded)
    {
        var sums = new Vector3[count];
        var seen = new int[count];
        foreach (var line in lines.Where(l => l.IsEngine2 && l.Sub >= 0))
        {
            var c = line.Points.Select(StageMath.ToUnit).Aggregate(Vector3.Zero, (a, p) => a + p) / line.Points.Length;
            sums[line.Sub] += c + (exploded ? StageMath.ExplodeOffsetUnit(line.Sub) : Vector3.Zero);
            seen[line.Sub]++;
        }

        for (var i = 0; i < count; i++) sums[i] = seen[i] == 0 ? StageMath.Engine2OriginUnit : sums[i] / seen[i];
        return sums;
    }

    private static void StartSpin(Compositor compositor, Visual visual)
    {
        var spin = compositor.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(0, 0);
        spin.InsertKeyFrame(1, MathF.Tau);
        spin.Duration = TimeSpan.FromSeconds(12);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.RotationAngle), spin);
    }
}
