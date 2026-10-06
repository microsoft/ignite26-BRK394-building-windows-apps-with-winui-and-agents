using System.Numerics;
using Hangar.Models;

namespace Hangar.Helpers;

/// <summary>One polyline of the hologram. Engine 2 lines carry the part they belong to.</summary>
public sealed record WireLine(Vector3[] Points, EnginePart Part, bool IsEngine2, bool Spins, bool IsHot = false);

/// <summary>
/// Procedural A321neo-style wireframe (metres, nose +X, up +Y, span ±Z).
/// Engine 2 sits at +Z and is split into nacelle, fan, compressor and turbine.
/// </summary>
public static class WireMesh
{
    public static readonly Vector3 Engine2Origin = new(6.4f, -2.5f, 5.8f);
    private static readonly Vector3 Engine1Origin = new(6.4f, -2.5f, -5.8f);

    public static List<WireLine> BuildA321()
    {
        var lines = new List<WireLine>();
        void Air(params Vector3[] pts) => lines.Add(new WireLine(pts, EnginePart.None, false, false));

        // Fuselage: ring sections + stringers.
        float[] stations = [22.2f, 21.8f, 21.0f, 20.0f, 18.5f, 16f, 13.4f, 10.8f, 8.2f, 5.6f, 3f, 0.4f, -2.2f, -4.8f, -7.4f, -10f, -11.5f, -13f, -14.5f, -16f, -17.5f, -19f, -20.5f, -22f];
        foreach (var x in stations)
        {
            var (cy, r) = FuselageProfile(x);
            Air(Ring(new Vector3(x, cy, 0), r, 28));
        }
        for (var i = 0; i < 14; i++)
        {
            var a = i / 14f * MathF.Tau;
            Air(stations.Select(x => { var (cy, r) = FuselageProfile(x); return new Vector3(x, cy + r * MathF.Cos(a), r * MathF.Sin(a)); }).ToArray());
        }
        // Cockpit glazing.
        Air(new(20.9f, 0.55f, -0.75f), new(20.3f, 1.05f, -0.8f), new(20.3f, 1.05f, 0.8f), new(20.9f, 0.55f, 0.75f), new(20.9f, 0.55f, -0.75f));

        foreach (var s in new[] { -1f, 1f })
        {
            // Wing.
            Vector3 rLe = new(5.0f, -1.3f, s * 1.9f), rTe = new(-3.5f, -1.3f, s * 1.9f);
            Vector3 tLe = new(-4.3f, 0.1f, s * 17f), tTe = new(-6.2f, 0.1f, s * 17f);
            Air(rLe, tLe, tTe, rTe);
            for (var i = 1; i <= 8; i++)
            {
                var t = i / 9f;
                Air(Vector3.Lerp(rLe, tLe, t), Vector3.Lerp(rTe, tTe, t));
            }
            foreach (var c in new[] { 0.25f, 0.7f })
                Air(Vector3.Lerp(rLe, rTe, c), Vector3.Lerp(tLe, tTe, c));
            Air(tLe, new(-5.7f, 2.5f, s * 17.5f), new(-6.4f, 2.5f, s * 17.5f), tTe); // sharklet

            // Horizontal stabiliser.
            Vector3 hLe = new(-17.6f, 0.9f, s * 0.9f), hTe = new(-21.4f, 0.9f, s * 0.9f);
            Vector3 hTl = new(-21.0f, 1.3f, s * 6.4f), hTt = new(-22.6f, 1.3f, s * 6.4f);
            Air(hLe, hTl, hTt, hTe);
            for (var i = 1; i <= 3; i++) Air(Vector3.Lerp(hLe, hTl, i / 4f), Vector3.Lerp(hTe, hTt, i / 4f));
        }

        // Vertical stabiliser + rudder.
        Vector3 vLe = new(-15.5f, 1.8f, 0), vTe = new(-21.4f, 1.8f, 0), vTl = new(-21.6f, 8.6f, 0), vTt = new(-23.3f, 8.6f, 0);
        Air(vLe, vTl, vTt, vTe);
        for (var i = 1; i <= 4; i++) Air(Vector3.Lerp(vLe, vTl, i / 5f), Vector3.Lerp(vTe, vTt, i / 5f));
        Air(Vector3.Lerp(vLe, vTe, 0.7f), Vector3.Lerp(vTl, vTt, 0.7f));

        AddEngine(lines, Engine1Origin, false);
        AddEngine(lines, Engine2Origin, true);
        return lines;
    }

    private static (float Cy, float R) FuselageProfile(float x)
    {
        if (x > 16f)
        {
            var t = Math.Clamp((x - 16f) / 6.4f, 0f, 1f);
            return (-0.35f * t * t, 2f * MathF.Sqrt(MathF.Max(0.02f, 1f - t * t)));
        }
        if (x < -10f)
        {
            var t = Math.Clamp((-10f - x) / 12f, 0f, 1f);
            return (1.35f * MathF.Pow(t, 1.5f), 2f * (1f - t) + 0.25f * t);
        }
        return (0f, 2f);
    }

    private static Vector3[] Ring(Vector3 c, float r, int n)
    {
        var pts = new Vector3[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var a = i / (float)n * MathF.Tau;
            pts[i] = new Vector3(c.X, c.Y + r * MathF.Cos(a), c.Z + r * MathF.Sin(a));
        }
        return pts;
    }

    private static Vector3 Polar(Vector3 o, float dx, float r, float a) => new(o.X + dx, o.Y + r * MathF.Cos(a), o.Z + r * MathF.Sin(a));

    private static void AddEngine(List<WireLine> lines, Vector3 o, bool e2)
    {
        void Add(EnginePart part, bool spins, Vector3[] pts) => lines.Add(new WireLine(pts, e2 ? part : EnginePart.None, e2, spins));

        // Pylon (airframe).
        lines.Add(new WireLine([new(o.X - 0.3f, o.Y + 1.15f, o.Z), new(2.4f, -0.95f, o.Z), new(-2.0f, -0.95f, o.Z), new(o.X - 3.8f, o.Y + 1.0f, o.Z)], EnginePart.None, false, false));

        // Nacelle.
        (float Dx, float R)[] nac = [(0f, 1.12f), (-0.4f, 1.2f), (-1.4f, 1.22f), (-2.6f, 1.16f), (-3.6f, 1.0f), (-4.4f, 0.78f)];
        foreach (var (dx, r) in nac) Add(EnginePart.Nacelle, false, Ring(o + new Vector3(dx, 0, 0), r, 28));
        for (var i = 0; i < 12; i++)
        {
            var a = i / 12f * MathF.Tau;
            Add(EnginePart.Nacelle, false, nac.Select(n => Polar(o, n.Dx, n.R, a)).ToArray());
        }

        // Fan: rim, hub, twisted blades, spinner.
        Add(EnginePart.Fan, false, Ring(o + new Vector3(-0.55f, 0, 0), 1.02f, 32));
        Add(EnginePart.Fan, false, Ring(o + new Vector3(-0.5f, 0, 0), 0.32f, 16));
        for (var i = 0; i < 22; i++)
        {
            var a = i / 22f * MathF.Tau;
            Vector3[] blade = [Polar(o, -0.45f, 0.32f, a), Polar(o, -0.52f, 0.66f, a + 0.12f), Polar(o, -0.6f, 1.0f, a + 0.28f)];
            lines.Add(new WireLine(blade, e2 ? EnginePart.Fan : EnginePart.None, e2, true, e2 && i == 14));
        }
        for (var i = 0; i < 8; i++)
        {
            var a = i / 8f * MathF.Tau;
            Add(EnginePart.Fan, true, [Polar(o, -0.5f, 0.32f, a), Polar(o, -0.2f, 0.2f, a + 0.3f), o + new Vector3(0.05f, 0, 0)]);
        }

        // Compressor: tapering drum.
        var comp = Enumerable.Range(0, 7).Select(i => (Dx: -1.3f - i * 0.2f, R: 0.72f - i * 0.035f)).ToArray();
        foreach (var (dx, r) in comp) Add(EnginePart.Compressor, false, Ring(o + new Vector3(dx, 0, 0), r, 24));
        for (var i = 0; i < 8; i++)
        {
            var a = i / 8f * MathF.Tau;
            Add(EnginePart.Compressor, true, comp.Select(c => Polar(o, c.Dx, c.R, a + c.Dx * 0.4f)).ToArray());
        }
        foreach (var k in new[] { 0, 3, 6 })
            for (var i = 0; i < 12; i++)
            {
                var a = i / 12f * MathF.Tau;
                Add(EnginePart.Compressor, true, [Polar(o, comp[k].Dx, comp[k].R * 0.35f, a), Polar(o, comp[k].Dx, comp[k].R, a + 0.15f)]);
            }

        // Turbine + exhaust cone.
        var turb = Enumerable.Range(0, 6).Select(i => (Dx: -2.8f - i * 0.18f, R: 0.55f + i * 0.026f)).ToArray();
        foreach (var (dx, r) in turb) Add(EnginePart.Turbine, false, Ring(o + new Vector3(dx, 0, 0), r, 24));
        for (var i = 0; i < 8; i++)
        {
            var a = i / 8f * MathF.Tau;
            Add(EnginePart.Turbine, true, turb.Select(c => Polar(o, c.Dx, c.R, a - c.Dx * 0.5f)).ToArray());
        }
        foreach (var k in new[] { 1, 4 })
            for (var i = 0; i < 14; i++)
            {
                var a = i / 14f * MathF.Tau;
                Add(EnginePart.Turbine, true, [Polar(o, turb[k].Dx, turb[k].R * 0.4f, a), Polar(o, turb[k].Dx, turb[k].R, a - 0.2f)]);
            }
        Add(EnginePart.Turbine, false, Ring(o + new Vector3(-3.75f, 0, 0), 0.3f, 16));
        for (var i = 0; i < 8; i++)
        {
            var a = i / 8f * MathF.Tau;
            Add(EnginePart.Turbine, false, [Polar(o, -3.75f, 0.3f, a), o + new Vector3(-4.9f, 0, 0)]);
        }
    }
}
