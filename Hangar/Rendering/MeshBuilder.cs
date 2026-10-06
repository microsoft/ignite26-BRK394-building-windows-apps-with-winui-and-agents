using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Scenes;
using Windows.Foundation;
using WinRT;

namespace Hangar.Rendering;

/// <summary>
/// Accumulates procedural geometry (positions + normals + indices) and uploads it into a
/// Composition <see cref="SceneMesh"/>. All primitives honour <see cref="Transform"/> so
/// many pieces (e.g. fan blades) can be merged into a single draw call.
/// </summary>
public sealed class MeshBuilder
{
    private readonly List<Vector3> _positions = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<uint> _indices = new();

    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    public int VertexCount => _positions.Count;

    private uint AddVertex(Vector3 p, Vector3 n)
    {
        _positions.Add(Vector3.Transform(p, Transform));
        var tn = Vector3.TransformNormal(n, Transform);
        _normals.Add(tn.LengthSquared() > 0 ? Vector3.Normalize(tn) : Vector3.UnitY);
        return (uint)(_positions.Count - 1);
    }

    private void Tri(uint a, uint b, uint c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>
    /// Surface of revolution around the X axis. Profile points are (x, radius). Shading is flat
    /// along the profile and smooth around the circumference for a crisp "machined" look.
    /// </summary>
    public void Lathe(IReadOnlyList<Vector2> profile, int segments, bool inward = false)
    {
        for (int s = 0; s < profile.Count - 1; s++)
        {
            var p0 = profile[s];
            var p1 = profile[s + 1];
            var d = p1 - p0;
            if (d.LengthSquared() < 1e-8f)
            {
                continue;
            }

            var n2 = Vector2.Normalize(new Vector2(d.Y, -d.X));
            if (inward)
            {
                n2 = -n2;
            }

            uint baseIndex = (uint)_positions.Count;
            for (int k = 0; k <= segments; k++)
            {
                float a = MathF.Tau * k / segments;
                float c = MathF.Cos(a), sn = MathF.Sin(a);
                var normal = new Vector3(n2.X, n2.Y * c, n2.Y * sn);
                AddVertex(new Vector3(p0.X, p0.Y * c, p0.Y * sn), normal);
                AddVertex(new Vector3(p1.X, p1.Y * c, p1.Y * sn), normal);
            }

            for (int k = 0; k < segments; k++)
            {
                uint i0 = baseIndex + (uint)(k * 2);
                if (inward)
                {
                    Tri(i0, i0 + 3, i0 + 1);
                    Tri(i0, i0 + 2, i0 + 3);
                }
                else
                {
                    Tri(i0, i0 + 1, i0 + 3);
                    Tri(i0, i0 + 3, i0 + 2);
                }
            }
        }
    }

    /// <summary>Flat annulus/disc perpendicular to the X axis.</summary>
    public void Disc(float x, float radius, int segments, bool facePositiveX, float innerRadius = 0f)
    {
        var n = facePositiveX ? Vector3.UnitX : -Vector3.UnitX;
        uint baseIndex = (uint)_positions.Count;
        for (int k = 0; k <= segments; k++)
        {
            float a = MathF.Tau * k / segments;
            float c = MathF.Cos(a), sn = MathF.Sin(a);
            AddVertex(new Vector3(x, innerRadius * c, innerRadius * sn), n);
            AddVertex(new Vector3(x, radius * c, radius * sn), n);
        }

        for (int k = 0; k < segments; k++)
        {
            uint i0 = baseIndex + (uint)(k * 2);
            if (facePositiveX)
            {
                Tri(i0, i0 + 1, i0 + 3);
                Tri(i0, i0 + 3, i0 + 2);
            }
            else
            {
                Tri(i0, i0 + 3, i0 + 1);
                Tri(i0, i0 + 2, i0 + 3);
            }
        }
    }

    /// <summary>Double-sided quad (front and back faces) — used for thin blades.</summary>
    public void QuadDoubleSided(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Quad(a, b, c, d);
        Quad(d, c, b, a);
    }

    /// <summary>Quad with explicit corners and a computed normal.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var cross = Vector3.Cross(b - a, c - a);
        var n = cross.LengthSquared() > 0 ? Vector3.Normalize(cross) : Vector3.UnitY;
        uint i0 = AddVertex(a, n);
        uint i1 = AddVertex(b, n);
        uint i2 = AddVertex(c, n);
        uint i3 = AddVertex(d, n);
        Tri(i0, i1, i2);
        Tri(i0, i2, i3);
    }

    /// <summary>Axis-aligned box.</summary>
    public void Box(Vector3 center, Vector3 size)
    {
        var h = size / 2;
        Vector3 P(float x, float y, float z) => center + new Vector3(x * h.X, y * h.Y, z * h.Z);
        Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
        Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
        Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
        Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
        Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
    }

    /// <summary>
    /// Extrudes a convex planform (XZ plane) into a slab centred on Y=0 that tapers toward the
    /// tip (|Z| max). Used for wings and stabilisers. Rendered double-sided-safe by emitting
    /// both windings of the caps.
    /// </summary>
    public void Slab(IReadOnlyList<Vector2> planform, float thicknessRoot, float thicknessTip)
    {
        int n = planform.Count;
        var top = new Vector3[n];
        var bottom = new Vector3[n];
        float maxSpan = planform.Max(p => MathF.Abs(p.Y));

        for (int i = 0; i < n; i++)
        {
            float t = maxSpan > 0 ? MathF.Abs(planform[i].Y) / maxSpan : 0;
            float half = (thicknessRoot + (thicknessTip - thicknessRoot) * t) / 2;
            top[i] = new Vector3(planform[i].X, half, planform[i].Y);
            bottom[i] = new Vector3(planform[i].X, -half, planform[i].Y);
        }

        for (int i = 1; i < n - 1; i++)
        {
            TriBoth(top[0], top[i], top[i + 1], Vector3.UnitY);
            TriBoth(bottom[0], bottom[i], bottom[i + 1], -Vector3.UnitY);
        }

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            QuadDoubleSided(bottom[i], bottom[j], top[j], top[i]);
        }
    }

    private void TriBoth(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        uint i0 = AddVertex(a, n);
        uint i1 = AddVertex(b, n);
        uint i2 = AddVertex(c, n);
        Tri(i0, i1, i2);
        Tri(i0, i2, i1);
    }

    public SceneMesh Build(Compositor compositor)
    {
        var mesh = SceneMesh.Create(compositor);
        mesh.PrimitiveTopology = DirectXPrimitiveTopology.TriangleList;

        var positions = new float[_positions.Count * 3];
        var normals = new float[_normals.Count * 3];
        for (int i = 0; i < _positions.Count; i++)
        {
            positions[i * 3] = _positions[i].X;
            positions[i * 3 + 1] = _positions[i].Y;
            positions[i * 3 + 2] = _positions[i].Z;
            normals[i * 3] = _normals[i].X;
            normals[i * 3 + 1] = _normals[i].Y;
            normals[i * 3 + 2] = _normals[i].Z;
        }

        mesh.FillMeshAttribute(SceneAttributeSemantic.Vertex, DirectXPixelFormat.R32G32B32Float, ToBuffer(MemoryMarshal.AsBytes(positions.AsSpan())));
        mesh.FillMeshAttribute(SceneAttributeSemantic.Normal, DirectXPixelFormat.R32G32B32Float, ToBuffer(MemoryMarshal.AsBytes(normals.AsSpan())));

        if (_positions.Count <= ushort.MaxValue)
        {
            var idx = new ushort[_indices.Count];
            for (int i = 0; i < idx.Length; i++)
            {
                idx[i] = (ushort)_indices[i];
            }

            mesh.FillMeshAttribute(SceneAttributeSemantic.Index, DirectXPixelFormat.R16UInt, ToBuffer(MemoryMarshal.AsBytes(idx.AsSpan())));
        }
        else
        {
            mesh.FillMeshAttribute(SceneAttributeSemantic.Index, DirectXPixelFormat.R32UInt, ToBuffer(MemoryMarshal.AsBytes(_indices.ToArray().AsSpan())));
        }

        return mesh;
    }

    private static unsafe MemoryBuffer ToBuffer(ReadOnlySpan<byte> bytes)
    {
        var buffer = new MemoryBuffer((uint)bytes.Length);
        using IMemoryBufferReference reference = buffer.CreateReference();

        IntPtr unknown = MarshalInterface<IMemoryBufferReference>.FromManaged(reference);
        try
        {
            // IMemoryBufferByteAccess
            var iid = new Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out IntPtr byteAccess));
            try
            {
                // GetBuffer is vtable slot 3 (after IUnknown's three methods).
                var getBuffer = (delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)(*(*(void***)byteAccess + 3));
                byte* data;
                uint capacity;
                Marshal.ThrowExceptionForHR(getBuffer(byteAccess, &data, &capacity));
                bytes.CopyTo(new Span<byte>(data, (int)capacity));
            }
            finally
            {
                Marshal.Release(byteAccess);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }

        return buffer;
    }
}
