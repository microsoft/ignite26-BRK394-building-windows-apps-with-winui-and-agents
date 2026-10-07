using System.Numerics;
using Hangar.Helpers;
using Hangar.Models;

namespace Hangar.Controls.CompositionStage;

internal enum StageBatchKind { Airframe, Engine1, Engine2 }

internal sealed record StageBatch(
    StageBatchKind Kind,
    EnginePart Part,
    int Sub,
    bool Spins,
    bool IsHot,
    Vector2[] Points,
    Vector2 Size,
    Matrix4x4 Transform,
    Vector3 CenterUnit);

internal sealed record StageCamera(float Yaw, float Pitch, float Distance, Vector3 TargetWorld, Vector2 Center, float Focal, float FloatY = 0);

internal static class StageMath
{
    public const float K = 40f;
    public const float FieldOfView = 0.62f;
    public static readonly Vector3 DefaultTargetWorld = new(-0.5f, -0.6f, 1.4f);
    public static readonly Vector3 Engine2OriginUnit = ToUnit(WireMesh.Engine2Origin);

    public static Vector3 ToUnit(Vector3 world) => new(world.X * K, -world.Y * K, -world.Z * K);
    public static Vector3 ToUnitOffset(Vector3 worldOffset) => new(worldOffset.X * K, -worldOffset.Y * K, -worldOffset.Z * K);
    public static Vector3 FromUnit(Vector3 unit) => new(unit.X / K, -unit.Y / K, -unit.Z / K);

    public static List<StageBatch> BuildBatches(IReadOnlyList<WireLine> lines)
    {
        var batches = new List<StageBatch>(lines.Count * 2);
        foreach (var line in lines)
        {
            if (line.Points.Length < 2)
            {
                continue;
            }

            var pts = line.Points.Select(ToUnit).ToArray();
            var start = 0;
            while (start < pts.Length - 1)
            {
                var end = FindRunEnd(pts, start);
                if (end <= start)
                {
                    end = start + 1;
                }

                batches.Add(BuildBatch(line, pts, start, end));
                start = end;
            }
        }

        return batches;
    }

    public static Matrix4x4 CompositionWorldMatrix(StageCamera camera)
    {
        var target = ToUnit(camera.TargetWorld);
        return Matrix4x4.CreateTranslation(-target)
            * Matrix4x4.CreateRotationY(camera.Yaw)
            * Matrix4x4.CreateRotationX(camera.Pitch)
            * Matrix4x4.CreateTranslation(camera.Center.X, camera.Center.Y + camera.FloatY, camera.Focal - camera.Distance * K);
    }

    public static Vector2? Project(Vector3 unitPoint, StageCamera camera)
    {
        var p = FromUnit(unitPoint);
        var eye = camera.TargetWorld + camera.Distance * new Vector3(
            MathF.Cos(camera.Pitch) * MathF.Cos(camera.Yaw),
            MathF.Sin(camera.Pitch),
            MathF.Cos(camera.Pitch) * MathF.Sin(camera.Yaw));
        var view = Matrix4x4.CreateLookAt(eye, camera.TargetWorld, Vector3.UnitY);
        var v = Vector3.Transform(p, view);
        var depth = -v.Z;
        if (depth < 0.3f)
        {
            return null;
        }

        return new Vector2(camera.Center.X + camera.Focal * v.X / depth, camera.Center.Y - camera.Focal * v.Y / depth);
    }

    public static Vector2 FocusCenter(double width, double height, double padLeft, double padTop, double padRight, double padBottom, out float focal)
    {
        var w = (float)Math.Max(4, width);
        var h = (float)Math.Max(4, height);
        var left = (float)Math.Min(padLeft, w * 0.4);
        var right = (float)Math.Min(padRight, w * 0.4);
        var top = (float)Math.Min(padTop, h * 0.3);
        var bottom = (float)Math.Min(padBottom, h * 0.3);
        var availH = Math.Max(120f, h - top - bottom);
        var availW = Math.Max(120f, w - left - right);
        focal = Math.Min(availH, availW * 0.62f) / 2f / MathF.Tan(FieldOfView / 2f);
        return new Vector2((left + w - right) / 2f, (top + h - bottom) / 2f);
    }

    public static Vector3 ExplodeOffsetUnit(int sub)
    {
        var s = WireMesh.SubParts[sub];
        return ToUnitOffset(s.Axial + s.RadialDir * s.Radial);
    }

    public static float ReverseDelay(int sub, float maxDelay) => Math.Max(0, maxDelay - WireMesh.SubParts[sub].Delay);

    private static int FindRunEnd(Vector3[] pts, int start)
    {
        const float eps = 0.04f * K;
        if (start + 2 >= pts.Length)
        {
            return pts.Length - 1;
        }

        var p0 = pts[start];
        Vector3 normal = default;
        var planeEnd = start + 1;
        for (var i = start + 2; i < pts.Length; i++)
        {
            normal = Vector3.Cross(pts[start + 1] - p0, pts[i] - p0);
            if (normal.LengthSquared() > 0.001f)
            {
                normal = Vector3.Normalize(normal);
                planeEnd = i;
                break;
            }
        }

        if (normal == default)
        {
            return pts.Length - 1;
        }

        for (var i = planeEnd + 1; i < pts.Length; i++)
        {
            if (Math.Abs(Vector3.Dot(pts[i] - p0, normal)) > eps)
            {
                return i - 1;
            }
        }

        return pts.Length - 1;
    }

    private static StageBatch BuildBatch(WireLine line, Vector3[] pts, int start, int end)
    {
        var run = pts[start..(end + 1)];
        MakeFrame(run, line, out var origin, out var u, out var v, out var n);
        var projected = run.Select(p => new Vector2(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v))).ToArray();
        var min = new Vector2(projected.Min(p => p.X), projected.Min(p => p.Y));
        var max = new Vector2(projected.Max(p => p.X), projected.Max(p => p.Y));
        const float pad = 8f;
        min -= new Vector2(pad);
        max += new Vector2(pad);
        var local = projected.Select(p => p - min).ToArray();
        var translate = origin + u * min.X + v * min.Y;
        var transform = new Matrix4x4(
            u.X, u.Y, u.Z, 0,
            v.X, v.Y, v.Z, 0,
            n.X, n.Y, n.Z, 0,
            translate.X, translate.Y, translate.Z, 1);
        var center = run.Aggregate(Vector3.Zero, (a, p) => a + p) / run.Length;
        var kind = line.IsEngine2 ? StageBatchKind.Engine2 : center.Z > 120 && center.Y > 35 ? StageBatchKind.Engine1 : StageBatchKind.Airframe;
        return new StageBatch(kind, line.Part, line.Sub, line.Spins, line.IsHot, local, max - min, transform, center);
    }

    private static void MakeFrame(Vector3[] run, WireLine line, out Vector3 origin, out Vector3 u, out Vector3 v, out Vector3 n)
    {
        origin = run[0];
        var seg = Vector3.Normalize(run[^1] - run[0]);
        n = default;
        for (var i = 2; i < run.Length; i++)
        {
            n = Vector3.Cross(run[1] - run[0], run[i] - run[0]);
            if (n.LengthSquared() > 0.001f)
            {
                n = Vector3.Normalize(n);
                break;
            }
        }

        if (n == default)
        {
            var radial = Vector3.Normalize((run[0] + run[^1]) * 0.5f - Engine2OriginUnit);
            var best = new[] { Vector3.UnitX, Vector3.UnitY, radial }.OrderByDescending(c => Vector3.Cross(seg, c).LengthSquared()).First();
            n = Vector3.Normalize(Vector3.Cross(seg, best));
        }

        u = seg.LengthSquared() > 0.001f ? seg : Vector3.UnitX;
        u = Vector3.Normalize(u - n * Vector3.Dot(u, n));
        v = Vector3.Normalize(Vector3.Cross(n, u));
    }
}