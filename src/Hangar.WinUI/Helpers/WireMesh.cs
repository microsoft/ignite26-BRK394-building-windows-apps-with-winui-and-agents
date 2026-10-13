using System.Numerics;
using Hangar.Models;

namespace Hangar.Helpers;

/// <summary>One polyline of the hologram. Engine 2 lines carry the part they belong to.</summary>
public sealed record WireLine(Vector3[] Points, EnginePart Part, bool IsEngine2, bool Spins, bool IsHot = false, int Sub = -1);

/// <summary>
/// One separable Engine 2 component for the exploded view. The final offset is
/// <c>Axial + RadialDir * Radial</c> plus a <c>Roll</c> about the engine axis; <c>Delay</c> drives the cascade.
/// </summary>
public sealed record EngineSubPart(string Name, EnginePart Group, float Delay, Vector3 Axial, Vector3 RadialDir, float Radial, float Roll, string? Label, bool LabelUp);

/// <summary>
/// Procedural A321neo-style wireframe (metres, nose +X, up +Y, span ±Z).
/// Engine 2 sits at +Z and is split into nacelle, fan, compressor and turbine.
/// </summary>
public static class WireMesh
{
    public static readonly Vector3 Engine2Origin = new(6.4f, -2.5f, 5.8f);
    private static readonly Vector3 Engine1Origin = new(6.4f, -2.5f, -5.8f);

    /// <summary>Engine 2 components in cascade order (front to back), rebuilt by <see cref="BuildA321"/>.</summary>
    public static readonly List<EngineSubPart> SubParts = [];

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
        AddDetailedEngine2(lines, Engine2Origin);
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

    /// <summary>LEAP-1A style Engine 2 split into ~55 separable components for the exploded view.</summary>
    private static void AddDetailedEngine2(List<WireLine> lines, Vector3 o)
    {
        SubParts.Clear();
        const float Step = 0.034f;
        var slot = 0;

        // Spread the core along the axis (centred on the combustor) so every stage separates.
        static float Stretch(float dx) => (dx + 2.4f) * 1.55f;
        static Vector3 Ax(float dx, float extra, float y = 0f, float z = 0f) => new(Stretch(dx) + extra, y, z);

        int Sub(string name, EnginePart group, Vector3 axial, string? label = null, bool up = true, float roll = 0f, Vector3 radialDir = default, float radial = 0f, float delay = -1f)
        {
            SubParts.Add(new EngineSubPart(name, group, delay >= 0 ? delay : slot++ * Step, axial, radialDir, radial, roll, label, up));
            return SubParts.Count - 1;
        }

        void L(int sub, Vector3[] pts, bool spins = false, bool hot = false) =>
            lines.Add(new WireLine(pts, SubParts[sub].Group, true, spins, hot, sub));

        Vector3 C(float dx) => new(o.X + dx, o.Y, o.Z);

        Vector3[] Arc(float dx, float r, float from, float to, int n)
        {
            var pts = new Vector3[n + 1];
            for (var i = 0; i <= n; i++)
            {
                pts[i] = Polar(o, dx, r, from + (to - from) * i / n);
            }

            return pts;
        }

        void Disk(int sub, float dx, float hub, float tip, int blades, float twist, bool spins = false)
        {
            L(sub, Ring(C(dx), hub, 16), spins);
            L(sub, Ring(C(dx - 0.05f), tip, 28), spins);
            L(sub, Ring(C(dx), hub * 0.55f, 12), spins);
            for (var i = 0; i < blades; i++)
            {
                var a = i * MathF.Tau / blades;
                L(sub, [Polar(o, dx, hub, a), Polar(o, dx - 0.05f, tip, a + twist)], spins);
            }
        }

        // Spinner.
        var spinner = Sub("Spinner", EnginePart.Fan, Ax(-0.2f, 1.9f), "SPINNER", up: false);
        L(spinner, Ring(C(-0.42f), 0.3f, 16), true);
        L(spinner, Ring(C(-0.2f), 0.2f, 12), true);
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.Tau / 8;
            L(spinner, [Polar(o, -0.42f, 0.3f, a), Polar(o, -0.2f, 0.2f, a + 0.2f), C(0.08f)], true);
        }

        // Inlet cowl.
        var cowl = Sub("Inlet cowl", EnginePart.Nacelle, Ax(-0.3f, 2.6f, 2.3f), "INLET COWL");
        float[] cowlDx = [0f, -0.25f, -0.55f];
        float[] cowlR = [1.12f, 1.19f, 1.22f];
        for (var k = 0; k < cowlDx.Length; k++)
        {
            L(cowl, Ring(C(cowlDx[k]), cowlR[k], 28));
        }

        L(cowl, Ring(C(-0.08f), 1.02f, 28));
        for (var i = 0; i < 12; i++)
        {
            var a = i * MathF.Tau / 12;
            L(cowl, [Polar(o, -0.08f, 1.02f, a), Polar(o, 0f, 1.12f, a), Polar(o, -0.25f, 1.19f, a), Polar(o, -0.55f, 1.22f, a)]);
        }

        // Fan disk + 22 individually separable blades that fan out radially.
        var fanAxial = Ax(-0.55f, 1.2f);
        var disk = Sub("Fan disk", EnginePart.Fan, fanAxial, "FAN DISK", up: false);
        L(disk, Ring(C(-0.5f), 0.32f, 16), true);
        L(disk, Ring(C(-0.64f), 0.32f, 16), true);
        L(disk, Ring(C(-0.57f), 0.16f, 12), true);
        for (var i = 0; i < 11; i++)
        {
            var a = i * MathF.Tau / 11;
            L(disk, [Polar(o, -0.57f, 0.16f, a), Polar(o, -0.5f, 0.32f, a), Polar(o, -0.64f, 0.32f, a), Polar(o, -0.57f, 0.16f, a)], true);
        }

        var bladeBase = slot++ * Step;
        for (var i = 0; i < 22; i++)
        {
            var a = i * MathF.Tau / 22;
            var hot = i == 14;
            var blade = Sub($"Fan blade {i}", EnginePart.Fan, fanAxial, hot ? "FAN BLADE 14" : null, up: true,
                roll: 0.05f, radialDir: new Vector3(0f, MathF.Cos(a + 0.2f), MathF.Sin(a + 0.2f)), radial: 0.95f, delay: bladeBase + i * 0.006f);
            L(blade,
            [
                Polar(o, -0.42f, 0.32f, a - 0.04f), Polar(o, -0.48f, 0.66f, a + 0.08f), Polar(o, -0.55f, 1.0f, a + 0.24f),
                Polar(o, -0.6f, 1.0f, a + 0.32f), Polar(o, -0.62f, 0.66f, a + 0.18f), Polar(o, -0.6f, 0.32f, a + 0.05f),
                Polar(o, -0.42f, 0.32f, a - 0.04f),
            ], true);
            if (hot)
            {
                L(blade, [Polar(o, -0.45f, 0.32f, a), Polar(o, -0.52f, 0.66f, a + 0.12f), Polar(o, -0.58f, 1.0f, a + 0.28f)], true, true);
            }
        }

        // Outlet guide vanes.
        var ogv = Sub("OGV", EnginePart.Fan, Ax(-0.85f, 0.6f), "OGV · STATORS", up: false);
        L(ogv, Ring(C(-0.85f), 0.62f, 24));
        L(ogv, Ring(C(-0.88f), 1.1f, 32));
        for (var i = 0; i < 30; i++)
        {
            var a = i * MathF.Tau / 30;
            L(ogv, [Polar(o, -0.85f, 0.62f, a), Polar(o, -0.88f, 1.1f, a + 0.05f)]);
        }

        // Fan case.
        var fanCase = Sub("Fan case", EnginePart.Nacelle, Ax(-0.95f, 0.3f, 2.9f), "FAN CASE");
        L(fanCase, Ring(C(-0.6f), 1.24f, 28));
        L(fanCase, Ring(C(-0.98f), 1.25f, 28));
        L(fanCase, Ring(C(-1.36f), 1.21f, 28));
        for (var i = 0; i < 12; i++)
        {
            var a = i * MathF.Tau / 12 + 0.13f;
            L(fanCase, [Polar(o, -0.6f, 1.24f, a), Polar(o, -0.98f, 1.25f, a), Polar(o, -1.36f, 1.21f, a)]);
        }

        // Low-pressure booster.
        for (var k = 0; k < 3; k++)
        {
            var dx = -1.05f - 0.15f * k;
            var booster = Sub($"Booster {k + 1}", EnginePart.Compressor, Ax(dx, 0.35f, k % 2 == 0 ? 0.12f : -0.12f),
                k == 0 ? "LP BOOSTER ×3" : null, up: true, roll: k % 2 == 0 ? 0.3f : -0.3f);
            Disk(booster, dx, 0.36f, 0.6f, 18, 0.18f);
        }

        // Thrust reverser halves split open like clamshells.
        float[] trDx = [-1.4f, -2.6f, -3.6f, -4.4f];
        float[] trR = [1.2f, 1.16f, 1.0f, 0.78f];
        for (var side = 0; side < 2; side++)
        {
            var near = side == 0;
            float from = near ? 0f : MathF.PI, to = near ? MathF.PI : MathF.Tau;
            var tr = Sub(near ? "Thrust reverser (near)" : "Thrust reverser (far)", EnginePart.Nacelle,
                new Vector3(-0.6f, 0.45f, near ? 2.1f : -2.1f), near ? "THRUST REVERSER" : null, up: true, roll: near ? 0.28f : -0.28f);
            for (var k = 0; k < trDx.Length; k++)
            {
                L(tr, Arc(trDx[k], trR[k], from, to, 14));
            }

            for (var j = 0; j <= 6; j++)
            {
                var a = from + (to - from) * j / 6;
                L(tr, [Polar(o, trDx[0], trR[0], a), Polar(o, trDx[1], trR[1], a), Polar(o, trDx[2], trR[2], a), Polar(o, trDx[3], trR[3], a)]);
            }
        }

        // High-pressure compressor: eight separate rings.
        for (var k = 0; k < 8; k++)
        {
            var dx = -1.55f - 0.13f * k;
            var hpc = Sub($"HPC stage {k + 1}", EnginePart.Compressor, Ax(dx, 0.1f, k % 2 == 0 ? 0.1f : -0.1f),
                k == 3 ? "HPC · 8 STAGES" : null, up: false, roll: k % 2 == 0 ? 0.35f : -0.35f);
            Disk(hpc, dx, 0.28f, 0.5f - 0.015f * k, 20, 0.15f);
        }

        // Fuel nozzle ring + combustor annulus.
        var nozzles = Sub("Fuel nozzle ring", EnginePart.Compressor, Ax(-2.62f, -0.05f, 0.15f), "FUEL NOZZLES ×16", up: true, roll: 0.25f);
        L(nozzles, Ring(C(-2.62f), 0.44f, 24));
        for (var i = 0; i < 16; i++)
        {
            var a = i * MathF.Tau / 16;
            L(nozzles, [Polar(o, -2.62f, 0.44f, a), Polar(o, -2.66f, 0.5f, a), Polar(o, -2.74f, 0.42f, a)]);
        }

        var combustor = Sub("Combustor", EnginePart.Compressor, Ax(-2.85f, -0.15f), "COMBUSTOR", up: false);
        L(combustor, Ring(C(-2.72f), 0.52f, 28));
        L(combustor, Ring(C(-2.98f), 0.5f, 28));
        L(combustor, Ring(C(-2.72f), 0.34f, 20));
        L(combustor, Ring(C(-2.98f), 0.34f, 20));
        for (var i = 0; i < 12; i++)
        {
            var a = i * MathF.Tau / 12;
            L(combustor, [Polar(o, -2.72f, 0.52f, a), Polar(o, -2.98f, 0.5f, a)]);
            L(combustor, [Polar(o, -2.72f, 0.34f, a + 0.26f), Polar(o, -2.98f, 0.34f, a + 0.26f)]);
        }

        // High- and low-pressure turbines.
        for (var k = 0; k < 2; k++)
        {
            var dx = -3.1f - 0.15f * k;
            var hpt = Sub($"HPT stage {k + 1}", EnginePart.Turbine, Ax(dx, -0.35f, k == 0 ? 0.1f : -0.1f),
                k == 0 ? "HPT ×2" : null, up: true, roll: k == 0 ? -0.3f : 0.3f);
            Disk(hpt, dx, 0.28f, 0.5f, 24, -0.2f);
        }

        for (var k = 0; k < 5; k++)
        {
            var dx = -3.45f - 0.15f * k;
            var lpt = Sub($"LPT stage {k + 1}", EnginePart.Turbine, Ax(dx, -0.55f, k % 2 == 0 ? -0.1f : 0.1f),
                k == 2 ? "LPT · 5 STAGES" : null, up: false, roll: k % 2 == 0 ? -0.32f : 0.32f);
            Disk(lpt, dx, 0.32f, 0.58f + 0.04f * k, 26, -0.25f);
        }

        // Concentric N1 / N2 shafts drop out beneath the core.
        void Shaft(int sub, float r, float from, float to)
        {
            L(sub, Ring(C(from), r, 10));
            L(sub, Ring(C(to), r, 10));
            for (var i = 0; i < 6; i++)
            {
                var a = i * MathF.Tau / 6;
                L(sub, [Polar(o, from, r, a), Polar(o, to, r, a)]);
            }
        }

        var n2 = Sub("N2 shaft", EnginePart.Turbine, new Vector3(0f, -0.95f, 0.4f), "N2 SHAFT", up: false);
        Shaft(n2, 0.14f, -1.4f, -3.3f);
        var n1 = Sub("N1 shaft", EnginePart.Turbine, new Vector3(0.2f, -1.35f, 0.8f), "N1 SHAFT", up: false);
        Shaft(n1, 0.07f, -0.5f, -4.3f);

        // Pylon mount + accessory gearbox.
        var pylon = Sub("Pylon mount", EnginePart.Nacelle, new Vector3(-0.2f, 1.6f, 0f), "PYLON MOUNT");
        float yTop = 1.25f, yHi = 1.55f;
        foreach (var z in new[] { -0.18f, 0.18f })
        {
            L(pylon, [new(o.X - 0.6f, o.Y + yTop, o.Z + z), new(o.X - 3.2f, o.Y + yTop, o.Z + z), new(o.X - 3.0f, o.Y + yHi, o.Z + z), new(o.X - 0.9f, o.Y + yHi, o.Z + z), new(o.X - 0.6f, o.Y + yTop, o.Z + z)]);
        }

        for (var j = 0; j <= 5; j++)
        {
            var x = o.X - 0.8f - j * 0.44f;
            L(pylon, [new(x, o.Y + yTop, o.Z - 0.18f), new(x, o.Y + yTop, o.Z + 0.18f)]);
        }

        var agb = Sub("Accessory gearbox", EnginePart.Nacelle, new Vector3(0.6f, -0.45f, 1.9f), "ACCESSORY GEARBOX", up: false);
        float gx0 = o.X - 1.0f, gx1 = o.X - 2.0f, gy0 = o.Y - 1.22f, gy1 = o.Y - 1.48f, gz = 0.32f;
        foreach (var y in new[] { gy0, gy1 })
        {
            L(agb, [new(gx0, y, o.Z - gz), new(gx1, y, o.Z - gz), new(gx1, y, o.Z + gz), new(gx0, y, o.Z + gz), new(gx0, y, o.Z - gz)]);
        }

        foreach (var (x, z) in new[] { (gx0, -gz), (gx1, -gz), (gx1, gz), (gx0, gz) })
        {
            L(agb, [new(x, gy0, o.Z + z), new(x, gy1, o.Z + z)]);
        }

        for (var j = 0; j < 3; j++)
        {
            L(agb, Ring(new Vector3(gx0 - 0.25f - j * 0.25f, gy1 - 0.05f, o.Z), 0.08f, 8));
        }

        // Core nozzle + exhaust cone.
        var nozzle = Sub("Core nozzle", EnginePart.Turbine, Ax(-4.3f, -0.9f, 0.1f), "CORE NOZZLE", up: true);
        L(nozzle, Ring(C(-4.1f), 0.6f, 24));
        L(nozzle, Ring(C(-4.5f), 0.48f, 24));
        for (var i = 0; i < 12; i++)
        {
            var a = i * MathF.Tau / 12;
            L(nozzle, [Polar(o, -4.1f, 0.6f, a), Polar(o, -4.5f, 0.48f, a)]);
        }

        var cone = Sub("Exhaust cone", EnginePart.Turbine, Ax(-4.4f, -1.4f), "EXHAUST CONE", up: false);
        L(cone, Ring(C(-4.05f), 0.32f, 16));
        L(cone, Ring(C(-4.4f), 0.24f, 14));
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.Tau / 8;
            L(cone, [Polar(o, -4.05f, 0.32f, a), Polar(o, -4.4f, 0.24f, a), C(-4.95f)]);
        }
    }

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
