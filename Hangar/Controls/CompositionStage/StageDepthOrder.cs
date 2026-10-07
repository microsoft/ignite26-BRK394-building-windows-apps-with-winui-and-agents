using System.Numerics;
using Microsoft.UI.Composition;

namespace Hangar.Controls.CompositionStage;

internal sealed class StageDepthOrder
{
    private readonly ContainerVisual _engineParts;
    private readonly ContainerVisual _ribs;
    private readonly List<StageSpritePart> _engineSprites;
    private int _engineSign = 1;
    private int _ribSign = 1;

    public StageDepthOrder(ContainerVisual engineParts, ContainerVisual ribs, List<StageSpritePart> engineSprites)
    {
        _engineParts = engineParts;
        _ribs = ribs;
        _engineSprites = engineSprites;
    }

    public void Update(StageCamera camera)
    {
        var eye = camera.TargetWorld + camera.Distance * new Vector3(MathF.Cos(camera.Pitch) * MathF.Cos(camera.Yaw), MathF.Sin(camera.Pitch), MathF.Cos(camera.Pitch) * MathF.Sin(camera.Yaw));
        var forward = Vector3.Normalize(camera.TargetWorld - eye);
        UpdateSign(Vector3.Dot(Vector3.UnitX, forward), ref _engineSign, () => ReorderEngine(_engineSign));
        UpdateSign(Vector3.Dot(Vector3.UnitX, forward), ref _ribSign, () => ReorderRibs(_ribSign));
    }

    private static void UpdateSign(float dot, ref int sign, Action reorder)
    {
        if (Math.Abs(dot) < 0.08f)
        {
            return;
        }

        var next = dot >= 0 ? 1 : -1;
        if (next == sign)
        {
            return;
        }

        sign = next;
        reorder();
    }

    private void ReorderEngine(int sign)
    {
        var ordered = sign > 0 ? _engineSprites.OrderBy(s => s.RestOffset.Z) : _engineSprites.OrderByDescending(s => s.RestOffset.Z);
        _engineParts.Children.RemoveAll();
        foreach (var sprite in ordered)
        {
            _engineParts.Children.InsertAtTop(sprite.Visual);
        }
    }

    private void ReorderRibs(int sign)
    {
        var ribs = _ribs.Children.ToArray();
        _ribs.Children.RemoveAll();
        foreach (var rib in sign > 0 ? ribs : ribs.Reverse())
        {
            _ribs.Children.InsertAtTop(rib);
        }
    }
}
