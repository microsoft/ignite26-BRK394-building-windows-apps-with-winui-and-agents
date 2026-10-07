using System.Numerics;
using Hangar.Helpers;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Hangar.Controls.CompositionStage;

internal sealed record StageStreak(SpriteVisual Visual);

internal static class StageStreaks
{
    private const int MaxStreaks = 60;
    private const float Thickness = 5f;

    public static StageStreak[] Create(
        Compositor compositor,
        StagePalette palette,
        IReadOnlyList<ContainerVisual> subContainers,
        IReadOnlyList<Vector3> centers)
    {
        var count = Math.Min(Math.Min(subContainers.Count, centers.Count), Math.Min(WireMesh.SubParts.Count, MaxStreaks));
        var streaks = new StageStreak[count];
        for (var i = 0; i < count; i++)
        {
            var sub = WireMesh.SubParts[i];
            var sign = sub.Axial.X >= 0 ? 1f : -1f;
            var length = Math.Clamp(Math.Abs(sub.Axial.X) * StageMath.K * 0.75f, 34f, 76f);
            var visual = compositor.CreateSpriteVisual();
            visual.Size = new Vector2(length, Thickness);
            visual.Brush = CreateBrush(compositor, palette, sign < 0);
            visual.Opacity = 0;
            visual.Scale = new Vector3(0.08f, 1f, 1f);
            visual.CenterPoint = sign > 0 ? new Vector3(length, Thickness * 0.5f, 0) : new Vector3(0, Thickness * 0.5f, 0);
            visual.Offset = centers[i] + new Vector3(sign > 0 ? -length : 0, -Thickness * 0.5f, 0);
            subContainers[i].Children.InsertAtTop(visual);
            streaks[i] = new StageStreak(visual);
        }

        return streaks;
    }

    public static void RunExplode(IReadOnlyList<StageStreak> streaks)
    {
        for (var i = 0; i < streaks.Count; i++)
        {
            var visual = streaks[i].Visual;
            var delay = TimeSpan.FromSeconds(WireMesh.SubParts[i].Delay);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Opacity = 0;
            visual.Scale = new Vector3(0.08f, 1f, 1f);
            var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0f, new Vector3(0.08f, 1f, 1f));
            scale.InsertKeyFrame(0.55f, Vector3.One);
            scale.InsertKeyFrame(1f, new Vector3(0.86f, 1f, 1f));
            scale.Duration = TimeSpan.FromMilliseconds(1150);
            scale.DelayTime = delay;
            visual.StartAnimation(nameof(Visual.Scale), scale);

            var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 0f);
            opacity.InsertKeyFrame(0.08f, 0.9f);
            opacity.InsertKeyFrame(0.7f, 0.72f);
            opacity.InsertKeyFrame(1f, 0f);
            opacity.Duration = TimeSpan.FromMilliseconds(1150);
            opacity.DelayTime = delay;
            visual.StartAnimation(nameof(Visual.Opacity), opacity);
        }
    }

    public static void Hide(IReadOnlyList<StageStreak> streaks)
    {
        foreach (var streak in streaks)
        {
            streak.Visual.StopAnimation(nameof(Visual.Opacity));
            streak.Visual.StopAnimation(nameof(Visual.Scale));
            streak.Visual.Opacity = 0;
            streak.Visual.Scale = new Vector3(0.08f, 1f, 1f);
        }
    }

    private static CompositionLinearGradientBrush CreateBrush(Compositor compositor, StagePalette palette, bool reverse)
    {
        var accent = palette.CyanCore.Color;
        var transparent = StagePalette.WithAlpha(accent, 0);
        var bright = StagePalette.WithAlpha(accent, palette.HighContrast ? (byte)220 : (byte)153);
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0, 0.5f);
        brush.EndPoint = new Vector2(1, 0.5f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0f, reverse ? bright : transparent));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1f, reverse ? transparent : bright));
        return brush;
    }
}
