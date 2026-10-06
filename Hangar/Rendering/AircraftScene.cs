using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Scenes;
using Windows.UI;

namespace Hangar.Rendering;

public enum EnginePart
{
    None,
    Nacelle,
    Fan,
    Compressor,
    Turbine,
}

/// <summary>
/// Procedural, stylised A321neo built entirely from Composition Scenes meshes. Engine 2
/// (starboard) is split into nacelle / fan / compressor / turbine nodes that can be
/// "exploded" along the engine axis with spring animations.
/// Coordinates: metres, +X forward, +Y up, +Z starboard.
/// </summary>
public sealed class AircraftScene : IDisposable
{
    private const int Seg = 48;

    private readonly Compositor _compositor;
    private readonly ContainerVisual _perspective;
    private readonly SceneVisual _scene;
    private readonly SceneNode _place;   // centre of the visual + pixels-per-metre scale
    private readonly SceneNode _pitch;   // camera pitch
    private readonly SceneNode _yaw;     // turntable yaw
    private readonly SceneNode _focus;   // shifts aircraft so the focus point is at the origin
    private readonly SceneNode _aircraft;
    private readonly SceneNode _engine2;
    private readonly Dictionary<EnginePart, SceneNode> _parts = new();
    private readonly Dictionary<EnginePart, SceneMetallicRoughnessMaterial> _partMaterials = new();
    private readonly Dictionary<EnginePart, Vector4> _partBaseColors = new();
    private SceneMetallicRoughnessMaterial _ringMaterial = null!;

    private static readonly Vector3 Engine2Position = new(3.2f, -1.55f, 5.9f);

    private Vector2 _size;
    private float _yawDegrees = -38f;
    private float _pitchDegrees = 16f;
    private float _zoom = 1f;
    private bool _exploded;

    public AircraftScene(Compositor compositor)
    {
        _compositor = compositor;
        _perspective = compositor.CreateContainerVisual();
        _scene = SceneVisual.Create(compositor);
        _perspective.Children.InsertAtTop(_scene);

        _place = SceneNode.Create(compositor);
        _pitch = SceneNode.Create(compositor);
        _yaw = SceneNode.Create(compositor);
        _focus = SceneNode.Create(compositor);
        _aircraft = SceneNode.Create(compositor);
        _engine2 = SceneNode.Create(compositor);

        _scene.Root = _place;
        _place.Children.Add(_pitch);
        _pitch.Children.Add(_yaw);
        _yaw.Children.Add(_focus);
        _focus.Children.Add(_aircraft);

        BuildFloor();
        BuildAirframe();
        BuildEngine(_engine2, Engine2Position, isEngine2: true);

        var engine1 = SceneNode.Create(compositor);
        BuildEngine(engine1, new Vector3(Engine2Position.X, Engine2Position.Y, -Engine2Position.Z), isEngine2: false);

        _yaw.Transform.RotationAxis = Vector3.UnitY;
        ApplyCamera();
    }

    public Visual Visual => _perspective;

    public bool IsExploded => _exploded;

    public EnginePart SelectedPart { get; private set; } = EnginePart.None;

    public void Resize(Vector2 size)
    {
        _size = size;
        _perspective.Size = size;
        _scene.Size = size;
        var center = new Vector3(size.X / 2, size.Y / 2 + size.Y * 0.04f, 0);
        var perspective = Matrix4x4.Identity;
        perspective.M34 = -1f / (Math.Max(size.X, 1) * 1.6f);
        _perspective.TransformMatrix =
            Matrix4x4.CreateTranslation(-center) * perspective * Matrix4x4.CreateTranslation(center);
        _place.Transform.Translation = center;
        ApplyScale(animate: false);
    }

    /// <summary>Pointer-drag orbit. Deltas in DIPs.</summary>
    public void Orbit(float dx, float dy)
    {
        _yawDegrees += dx * 0.35f;
        _pitchDegrees = Math.Clamp(_pitchDegrees - dy * 0.25f, -5f, 60f);
        ApplyCamera();
    }

    public void Zoom(float delta)
    {
        _zoom = Math.Clamp(_zoom * (1 + delta), 0.6f, 2.4f);
        ApplyScale(animate: false);
    }

    public void ResetView()
    {
        _zoom = 1f;
        AnimateYawTo(_exploded ? -62f : -38f);
        _pitchDegrees = _exploded ? 12f : 16f;
        ApplyCamera(yaw: false);
        ApplyScale(animate: true);
    }

    /// <summary>The single celebratory moment: a 15° turntable turn.</summary>
    public void Celebrate() => AnimateYawTo(_yawDegrees + 15f, durationMs: 1400);

    public void SetExploded(bool exploded)
    {
        _exploded = exploded;
        Spring(_parts[EnginePart.Nacelle], exploded ? new Vector3(0, 2.3f, 0) : Vector3.Zero);
        Spring(_parts[EnginePart.Fan], exploded ? new Vector3(3.4f, 0, 0) : Vector3.Zero, period: 0.07f);
        Spring(_parts[EnginePart.Compressor], exploded ? new Vector3(1.4f, 0, 0) : Vector3.Zero, period: 0.08f);
        Spring(_parts[EnginePart.Turbine], exploded ? new Vector3(-2.4f, 0, 0) : Vector3.Zero, period: 0.09f);

        // Bring Engine 2 to the centre of the stage while exploded.
        var focus = exploded ? -(Engine2Position + new Vector3(0.6f, 0.6f, 0)) : new Vector3(0, 0, 0);
        Spring(_focus, focus, period: 0.09f);
        AnimateYawTo(exploded ? -62f : -38f);
        ApplyScale(animate: true);
    }

    public void Select(EnginePart part)
    {
        SelectedPart = part;
        foreach (var (key, material) in _partMaterials)
        {
            bool selected = key == part;
            material.BaseColorFactor = selected ? new Vector4(0.23f, 0.82f, 0.94f, 1f) : _partBaseColors[key];
            material.EmissiveFactor = selected ? new Vector3(0.05f, 0.30f, 0.36f) : Vector3.Zero;
        }
    }

    public void SetStatusRing(Color color)
    {
        _ringMaterial.BaseColorFactor = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1);
        _ringMaterial.EmissiveFactor = new Vector3(color.R / 255f, color.G / 255f, color.B / 255f) * 0.85f;
    }

    private void ApplyCamera(bool yaw = true)
    {
        // Composition's Y axis points down; flip the world by rotating 180° about X, then pitch.
        _pitch.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI + _pitchDegrees * MathF.PI / 180f);
        if (yaw)
        {
            _yaw.Transform.RotationAngleInDegrees = _yawDegrees;
        }
    }

    private void ApplyScale(bool animate)
    {
        if (_size.X <= 0)
        {
            return;
        }

        float baseScale = Math.Min(_size.X / 52f, _size.Y / 30f);
        float scale = baseScale * _zoom * (_exploded ? 2.6f : 1f);
        var target = new Vector3(scale, scale, scale);
        if (animate)
        {
            var spring = _compositor.CreateSpringVector3Animation();
            spring.FinalValue = target;
            spring.DampingRatio = 0.78f;
            spring.Period = TimeSpan.FromMilliseconds(90);
            _place.Transform.StartAnimation("Scale", spring);
        }
        else
        {
            _place.Transform.Scale = target;
        }
    }

    private void AnimateYawTo(float degrees, int durationMs = 900)
    {
        var from = _yawDegrees;
        _yawDegrees = degrees;
        var anim = _compositor.CreateScalarKeyFrameAnimation();
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f));
        anim.InsertKeyFrame(0f, from);
        anim.InsertKeyFrame(1f, degrees, ease);
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        _yaw.Transform.StartAnimation("RotationAngleInDegrees", anim);
    }

    private void Spring(SceneNode node, Vector3 target, float period = 0.08f)
    {
        var spring = _compositor.CreateSpringVector3Animation();
        spring.FinalValue = target;
        spring.DampingRatio = 0.62f;
        spring.Period = TimeSpan.FromSeconds(period);
        node.Transform.StartAnimation("Translation", spring);
    }

    private SceneMetallicRoughnessMaterial Material(Vector4 color, float metallic, float roughness, Vector3? emissive = null)
    {
        var m = SceneMetallicRoughnessMaterial.Create(_compositor);
        m.BaseColorFactor = color;
        m.MetallicFactor = metallic;
        m.RoughnessFactor = roughness;
        m.IsDoubleSided = true;
        m.EmissiveFactor = new Vector3(color.X, color.Y, color.Z) * 0.5f; // DEBUG
        if (emissive is { } e)
        {
            m.EmissiveFactor = e;
        }

        return m;
    }

    private SceneNode AddMesh(SceneNode parent, MeshBuilder builder, SceneMaterial material)
    {
        var node = SceneNode.Create(_compositor);
        var renderer = SceneMeshRendererComponent.Create(_compositor);
        renderer.Mesh = builder.Build(_compositor);
        renderer.Material = material;
        node.Components.Add(renderer);
        parent.Children.Add(node);
        return node;
    }

    private void BuildFloor()
    {
        var floor = new MeshBuilder { Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, -4.2f, 0) };
        floor.Disc(0, 30f, 96, facePositiveX: true);
        AddMesh(_yaw, floor, Material(new Vector4(0.05f, 0.07f, 0.09f, 1), 0.6f, 0.35f));

        var ring = new MeshBuilder { Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, -4.17f, 0) };
        ring.Disc(0, 26.4f, 128, facePositiveX: true, innerRadius: 26.0f);
        _ringMaterial = Material(new Vector4(1f, 0.69f, 0.13f, 1), 0f, 0.5f, new Vector3(0.9f, 0.6f, 0.1f));
        AddMesh(_yaw, ring, _ringMaterial);

        var grid = new MeshBuilder { Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, -4.18f, 0) };
        for (int i = 1; i <= 4; i++)
        {
            float r = i * 5.4f;
            grid.Disc(0, r + 0.04f, 128, facePositiveX: true, innerRadius: r);
        }

        AddMesh(_yaw, grid, Material(new Vector4(0.14f, 0.20f, 0.25f, 1), 0f, 0.8f));
    }

    private void BuildAirframe()
    {
        var white = Material(new Vector4(0.86f, 0.89f, 0.92f, 1), 0.35f, 0.32f);
        var belly = Material(new Vector4(0.32f, 0.37f, 0.43f, 1), 0.45f, 0.4f);
        var livery = Material(new Vector4(0.12f, 0.55f, 0.68f, 1), 0.5f, 0.3f);
        var glass = Material(new Vector4(0.03f, 0.05f, 0.08f, 1), 0.9f, 0.1f);

        // Fuselage: A321neo is ~44.5 m long, ~4 m diameter.
        var fus = new MeshBuilder();
        fus.Lathe(new[]
        {
            new Vector2(22.4f, 0.0f), new Vector2(22.2f, 0.55f), new Vector2(21.6f, 1.15f), new Vector2(20.6f, 1.6f),
            new Vector2(19.0f, 1.92f), new Vector2(16.5f, 2.02f), new Vector2(-12f, 2.02f), new Vector2(-16f, 1.8f),
            new Vector2(-19.5f, 1.25f), new Vector2(-21.6f, 0.6f), new Vector2(-22.2f, 0.25f), new Vector2(-22.3f, 0f),
        }, Seg);
        AddMesh(_aircraft, fus, white);

        // Belly fairing + cockpit glazing.
        var fairing = new MeshBuilder { Transform = Matrix4x4.CreateScale(1, 0.45f, 1.05f) * Matrix4x4.CreateTranslation(1.5f, -1.75f, 0) };
        fairing.Lathe(new[] { new Vector2(7f, 0), new Vector2(5.5f, 1.4f), new Vector2(-4f, 1.5f), new Vector2(-6.5f, 0) }, 32);
        AddMesh(_aircraft, fairing, belly);

        var cockpit = new MeshBuilder { Transform = Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(20.75f, 0.85f, 0) };
        cockpit.Box(Vector3.Zero, new Vector3(1.9f, 0.32f, 0.6f));
        var cockpitNode = AddMesh(_aircraft, cockpit, glass);
        cockpitNode.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.42f);

        // Cheatline stripe.
        var stripe = new MeshBuilder();
        stripe.Lathe(new[] { new Vector2(14f, 2.035f), new Vector2(-14f, 2.035f) }, Seg);
        var stripeNode = AddMesh(_aircraft, stripe, livery);
        stripeNode.Transform.Scale = new Vector3(1, 0.06f, 1.0f);
        stripeNode.Transform.Translation = new Vector3(0, -0.2f, 0);

        // Wings (both sides): sweep ~25°, span ~35.8 m, sharklets.
        foreach (float side in new[] { 1f, -1f })
        {
            var wing = new MeshBuilder();
            wing.Slab(new[]
            {
                new Vector2(5.5f, 1.6f * side), new Vector2(-1.5f, 1.6f * side), new Vector2(-6.2f, 17.4f * side),
                new Vector2(-4.4f, 17.4f * side),
            }, 0.6f, 0.15f);
            var wingNode = AddMesh(_aircraft, wing, white);
            wingNode.Transform.Translation = new Vector3(0, -1.2f, 0);
            wingNode.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.09f * side);

            var sharklet = new MeshBuilder { Transform = Matrix4x4.CreateRotationX(-MathF.PI / 2 * side) };
            sharklet.Slab(new[] { new Vector2(-4.3f, 0), new Vector2(-6.2f, 0), new Vector2(-7.0f, 2.4f * side), new Vector2(-6.2f, 2.4f * side) }, 0.18f, 0.08f);
            var sharkletNode = AddMesh(_aircraft, sharklet, livery);
            sharkletNode.Transform.Translation = new Vector3(0, -1.2f + 1.55f, 17.4f * side);

            var stab = new MeshBuilder();
            stab.Slab(new[] { new Vector2(-16.2f, 0.6f * side), new Vector2(-19.2f, 0.6f * side), new Vector2(-21.6f, 6.4f * side), new Vector2(-20.2f, 6.4f * side) }, 0.3f, 0.1f);
            var stabNode = AddMesh(_aircraft, stab, white);
            stabNode.Transform.Translation = new Vector3(0, 0.6f, 0);
        }

        // Vertical stabiliser with livery.
        var fin = new MeshBuilder { Transform = Matrix4x4.CreateRotationX(-MathF.PI / 2) };
        fin.Slab(new[] { new Vector2(-14.5f, 0), new Vector2(-20.6f, 0), new Vector2(-22.6f, 6.6f), new Vector2(-19.8f, 6.6f) }, 0.4f, 0.14f);
        var finNode = AddMesh(_aircraft, fin, livery);
        finNode.Transform.Translation = new Vector3(0, 1.6f, 0);
    }

    private void BuildEngine(SceneNode engineNode, Vector3 position, bool isEngine2)
    {
        engineNode.Transform.Translation = position;
        _aircraft.Children.Add(engineNode);

        var nacelleColor = new Vector4(0.80f, 0.83f, 0.87f, 1);
        var fanColor = new Vector4(0.42f, 0.46f, 0.52f, 1);
        var compressorColor = new Vector4(0.72f, 0.58f, 0.36f, 1);
        var turbineColor = new Vector4(0.30f, 0.27f, 0.26f, 1);

        // Pylon connects the nacelle to the wing.
        var pylon = new MeshBuilder();
        pylon.Slab(new[] { new Vector2(1.4f, 0), new Vector2(-2.6f, 0), new Vector2(-3.4f, 0.9f), new Vector2(0.6f, 0.9f) }, 0.4f, 0.35f);
        var pylonNode = AddMesh(engineNode, pylon, Material(new Vector4(0.78f, 0.81f, 0.85f, 1), 0.35f, 0.35f));
        pylonNode.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);
        pylonNode.Transform.Translation = new Vector3(0, 0.9f, 0);

        // Nacelle: outer cowl + inner duct (inward normals) + intake lip.
        var nacelle = new MeshBuilder();
        nacelle.Lathe(new[]
        {
            new Vector2(2.15f, 0.92f), new Vector2(2.2f, 1.0f), new Vector2(2.05f, 1.1f), new Vector2(1.4f, 1.17f),
            new Vector2(0.0f, 1.14f), new Vector2(-1.0f, 1.04f), new Vector2(-1.7f, 0.86f),
        }, Seg);
        nacelle.Lathe(new[] { new Vector2(2.15f, 0.92f), new Vector2(1.2f, 0.96f), new Vector2(-1.7f, 0.80f) }, Seg, inward: true);
        nacelle.Disc(-1.7f, 0.86f, Seg, facePositiveX: false, innerRadius: 0.80f);
        var nacelleMat = Material(nacelleColor, 0.4f, 0.28f);
        var lipMat = Material(new Vector4(0.55f, 0.58f, 0.62f, 1), 0.95f, 0.12f);
        var nacelleNode = SceneNode.Create(_compositor);
        engineNode.Children.Add(nacelleNode);
        AddMesh(nacelleNode, nacelle, nacelleMat);
        var lip = new MeshBuilder();
        lip.Lathe(new[] { new Vector2(2.24f, 0.93f), new Vector2(2.26f, 0.97f), new Vector2(2.24f, 1.01f) }, Seg);
        AddMesh(nacelleNode, lip, lipMat);

        // Fan: spinner + 18 wide-chord swept blades.
        var fanNode = SceneNode.Create(_compositor);
        engineNode.Children.Add(fanNode);
        var spinner = new MeshBuilder();
        spinner.Lathe(new[] { new Vector2(2.05f, 0f), new Vector2(1.95f, 0.12f), new Vector2(1.7f, 0.26f), new Vector2(1.35f, 0.34f), new Vector2(1.1f, 0.34f) }, 32);
        var spinnerMat = Material(new Vector4(0.12f, 0.13f, 0.15f, 1), 0.9f, 0.2f);
        AddMesh(fanNode, spinner, spinnerMat);
        var blades = new MeshBuilder();
        const int bladeCount = 18;
        for (int i = 0; i < bladeCount; i++)
        {
            blades.Transform = Matrix4x4.CreateRotationX(MathF.Tau * i / bladeCount);
            // Twisted blade: root chord pitched steeply, tip flatter, swept back.
            var rootLead = new Vector3(1.55f, 0.32f, 0.10f);
            var rootTrail = new Vector3(1.20f, 0.32f, -0.12f);
            var tipLead = new Vector3(1.45f, 0.90f, 0.22f);
            var tipTrail = new Vector3(1.28f, 0.90f, -0.22f);
            blades.QuadDoubleSided(rootLead, rootTrail, tipTrail, tipLead);
        }

        var fanMat = Material(fanColor, 0.85f, 0.22f);
        AddMesh(fanNode, blades, fanMat);
        var fanHub = new MeshBuilder();
        fanHub.Lathe(new[] { new Vector2(1.1f, 0.34f), new Vector2(1.1f, 0.18f) }, 32);
        AddMesh(fanNode, fanHub, spinnerMat);

        // Compressor: banded drum.
        var compressorNode = SceneNode.Create(_compositor);
        engineNode.Children.Add(compressorNode);
        var compressor = new MeshBuilder();
        compressor.Lathe(new[] { new Vector2(1.0f, 0.2f), new Vector2(1.0f, 0.52f), new Vector2(0.2f, 0.45f), new Vector2(-0.3f, 0.42f), new Vector2(-0.3f, 0.2f) }, Seg);
        for (int i = 0; i < 6; i++)
        {
            float x = 0.9f - i * 0.2f;
            float r = 0.52f - i * 0.016f;
            compressor.Lathe(new[] { new Vector2(x + 0.04f, r), new Vector2(x + 0.02f, r + 0.07f), new Vector2(x - 0.02f, r + 0.07f), new Vector2(x - 0.04f, r) }, Seg);
        }

        var compressorMat = Material(compressorColor, 0.9f, 0.3f);
        AddMesh(compressorNode, compressor, compressorMat);

        // Turbine: hot section + exhaust plug.
        var turbineNode = SceneNode.Create(_compositor);
        engineNode.Children.Add(turbineNode);
        var turbine = new MeshBuilder();
        turbine.Lathe(new[] { new Vector2(-0.35f, 0.2f), new Vector2(-0.35f, 0.5f), new Vector2(-1.1f, 0.62f), new Vector2(-1.6f, 0.6f), new Vector2(-1.6f, 0.2f) }, Seg);
        for (int i = 0; i < 4; i++)
        {
            float x = -0.5f - i * 0.28f;
            turbine.Lathe(new[] { new Vector2(x + 0.05f, 0.56f), new Vector2(x, 0.66f), new Vector2(x - 0.05f, 0.56f) }, Seg);
        }

        turbine.Lathe(new[] { new Vector2(-1.6f, 0.36f), new Vector2(-2.1f, 0.3f), new Vector2(-2.55f, 0.12f), new Vector2(-2.7f, 0f) }, 32);
        var turbineMat = Material(turbineColor, 0.85f, 0.38f);
        AddMesh(turbineNode, turbine, turbineMat);

        if (isEngine2)
        {
            _parts[EnginePart.Nacelle] = nacelleNode;
            _parts[EnginePart.Fan] = fanNode;
            _parts[EnginePart.Compressor] = compressorNode;
            _parts[EnginePart.Turbine] = turbineNode;
            _partMaterials[EnginePart.Nacelle] = nacelleMat;
            _partMaterials[EnginePart.Fan] = fanMat;
            _partMaterials[EnginePart.Compressor] = compressorMat;
            _partMaterials[EnginePart.Turbine] = turbineMat;
            _partBaseColors[EnginePart.Nacelle] = nacelleColor;
            _partBaseColors[EnginePart.Fan] = fanColor;
            _partBaseColors[EnginePart.Compressor] = compressorColor;
            _partBaseColors[EnginePart.Turbine] = turbineColor;
        }
    }

    public void Dispose()
    {
        _scene.Dispose();
        _perspective.Dispose();
    }
}
