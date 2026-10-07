using System.Numerics;
using Hangar.Controls.CompositionStage;
using Hangar.Helpers;
using Hangar.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Hangar.Controls;

public sealed partial class CompositionAircraftStage
{
    private bool _idleRunning;

    private void ApplyExplode(bool exploded, bool instant = false)
    {
        if (_scene is null)
        {
            return;
        }

        StopIdle();
        ShowLabels(false);
        for (var i = 0; i < _scene.SubContainers.Length; i++)
        {
            var sub = WireMesh.SubParts[i];
            var targetOffset = exploded ? StageMath.ExplodeOffsetUnit(i) : Vector3.Zero;
            var targetRoll = exploded ? sub.Roll : 0f;
            var delay = exploded ? sub.Delay : StageMath.ReverseDelay(i, _maxDelay);
            if (instant || !_animationsEnabled)
            {
                _scene.SubContainers[i].Offset = targetOffset;
                _scene.SubContainers[i].RotationAngle = targetRoll;
            }
            else
            {
                AnimateVectorSpring(_scene.SubContainers[i], nameof(Visual.Offset), targetOffset, TimeSpan.FromSeconds(delay));
                AnimateScalarSpring(_scene.SubContainers[i], nameof(Visual.RotationAngle), targetRoll, TimeSpan.FromSeconds(delay));
            }
        }

        if (exploded && !instant && _animationsEnabled)
        {
            StageStreaks.RunExplode(_scene.Streaks);
        }
        else
        {
            StageStreaks.Hide(_scene.Streaks);
        }

        AnimateScalar(_scene.Airframe, nameof(Visual.Opacity), exploded ? 0.15f : 1f, TimeSpan.FromMilliseconds(360), TimeSpan.Zero);
        AnimateScalar(_scene.Engine1, nameof(Visual.Opacity), exploded ? 0.15f : 1f, TimeSpan.FromMilliseconds(360), TimeSpan.Zero);
        if (exploded)
        {
            RunPulse();
            QueueLabels();
        }
        else
        {
            ApplySelection();
        }
    }

    private void ApplySelection()
    {
        if (_scene is null)
        {
            return;
        }

        for (var i = 0; i < _scene.SubContainers.Length; i++)
        {
            var group = WireMesh.SubParts[i].Group;
            _scene.SubContainers[i].Opacity = SelectedPart == EnginePart.None || group == SelectedPart ? 1f : 0.15f;
        }

        foreach (var entry in _scene.Entries.Where(e => e.Batch.Kind == StageBatchKind.Engine2 && e.Batch.Sub < 0))
        {
            entry.Visual.Opacity = SelectedPart == EnginePart.None || entry.Batch.Part == SelectedPart ? 1f : 0.15f;
        }
    }

    private void RefreshHotBlade()
    {
        if (_scene is null || _palette is null)
        {
            return;
        }

        var amber = ThemeRes.Get<Color>(this, "AttentionColor");
        var critical = ThemeRes.Get<Color>(this, "CriticalColor");
        _palette.Hot.Color = StagePalette.Lerp(amber, critical, (float)_heat);
        foreach (var entry in _scene.Entries.Where(e => e.Batch.IsHot))
        {
            if (_status == AircraftStatus.Healthy)
            {
                entry.Core.StrokeBrush = _palette.CyanCore;
                entry.Glow.StrokeBrush = _palette.CyanGlow;
            }
            else
            {
                entry.Core.StrokeBrush = _heat > 0.05 ? _palette.Hot : _palette.Amber;
                entry.Glow.StrokeBrush = _heat > 0.05 ? _palette.Hot : _palette.Amber;
            }
        }
    }

    private void RunPulse()
    {
        if (_scene is null || !_animationsEnabled)
        {
            return;
        }

        var origin = WireMesh.Engine2Origin;
        var from = StageMath.ToUnit(origin + new Vector3(0.6f, 0, 0)) - new Vector3(110, 110, 0);
        var to = StageMath.ToUnit(origin + new Vector3(-5.0f, 0, 0)) - new Vector3(110, 110, 0);
        _scene.PulseRing.Opacity = 1;
        _scene.PulseRing.Offset = from;
        AnimateVector(_scene.PulseRing, nameof(Visual.Offset), to, TimeSpan.FromMilliseconds(920), TimeSpan.Zero);
        AnimateVector(_scene.PulseRing, nameof(Visual.Scale), new Vector3(1.6f), TimeSpan.FromMilliseconds(920), TimeSpan.Zero, Vector3.One);
        AnimateScalar(_scene.PulseRing, nameof(Visual.Opacity), 0f, TimeSpan.FromMilliseconds(920), TimeSpan.Zero, 0.9f);
    }

    private void RunCelebrate()
    {
        if (_scene is null || !_animationsEnabled)
        {
            return;
        }

        RunPulse();
        AnimateScalar(_scene.Engine2, nameof(Visual.Opacity), 0.72f, TimeSpan.FromMilliseconds(170), TimeSpan.Zero, 1f);
        AnimateScalar(_scene.Engine2, nameof(Visual.Opacity), 1f, TimeSpan.FromMilliseconds(420), TimeSpan.FromMilliseconds(170), 0.72f);
    }

    private void QueueLabels()
    {
        _labelTimer?.Stop();
        _labelTimer = DispatcherQueue.CreateTimer();
        _labelTimer.Interval = TimeSpan.FromSeconds(_animationsEnabled ? _maxDelay + 0.85f : 0.02f);
        _labelTimer.IsRepeating = false;
        _labelTimer.Tick += (_, _) => ShowLabels(IsExploded);
        _labelTimer.Start();
    }

    private void StartIdleTimer()
    {
        _idleTimer?.Stop();
        _idleTimer = DispatcherQueue.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(1);
        _idleTimer.IsRepeating = true;
        _idleTimer.Tick += (_, _) =>
        {
            if (_scene is null || !_animationsEnabled || IsExploded || _idleRunning)
            {
                return;
            }

            if (Environment.TickCount64 / 1000.0 - _lastInteraction > 4)
            {
                _idleRunning = true;
                _scene.World.CenterPoint = StageMath.ToUnit(StageMath.DefaultTargetWorld);
                _scene.World.RotationAxis = Vector3.UnitY;
                var turn = _scene.World.Compositor.CreateScalarKeyFrameAnimation();
                turn.InsertKeyFrame(1f, MathF.Tau);
                turn.Duration = TimeSpan.FromSeconds(90);
                turn.IterationBehavior = AnimationIterationBehavior.Forever;
                _scene.World.StartAnimation(nameof(Visual.RotationAngle), turn);
                var bob = _scene.World.Compositor.CreateVector3KeyFrameAnimation();
                bob.InsertKeyFrame(0f, Vector3.Zero);
                bob.InsertKeyFrame(0.5f, new Vector3(0, -6, 0));
                bob.InsertKeyFrame(1f, Vector3.Zero);
                bob.Duration = TimeSpan.FromSeconds(6);
                bob.IterationBehavior = AnimationIterationBehavior.Forever;
                _scene.World.StartAnimation(nameof(Visual.Offset), bob);
            }
        };
        _idleTimer.Start();
    }

    private void StopIdle()
    {
        if (_scene is null)
        {
            return;
        }

        _idleRunning = false;
        _scene.World.StopAnimation(nameof(Visual.RotationAngle));
        _scene.World.StopAnimation(nameof(Visual.Offset));
        _scene.World.RotationAngle = 0;
        _scene.World.Offset = Vector3.Zero;
    }

    private void StopSceneAnimations()
    {
        if (_scene is null)
        {
            return;
        }

        _scene.World.StopAnimation(nameof(Visual.TransformMatrix));
        _scene.PulseRing.StopAnimation(nameof(Visual.Offset));
        _scene.PulseRing.StopAnimation(nameof(Visual.Opacity));
        foreach (var container in _scene.SubContainers)
        {
            container.StopAnimation(nameof(Visual.Offset));
            container.StopAnimation(nameof(Visual.RotationAngle));
        }

        StageStreaks.Hide(_scene.Streaks);
    }

    private static void AnimateVectorSpring(Visual visual, string property, Vector3 target, TimeSpan delay)
    {
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = target;
        spring.DelayTime = delay;
        spring.DampingRatio = 0.55f;
        spring.Period = TimeSpan.FromMilliseconds(360);
        visual.StartAnimation(property, spring);
    }

    private static void AnimateScalarSpring(Visual visual, string property, float target, TimeSpan delay)
    {
        var spring = visual.Compositor.CreateSpringScalarAnimation();
        spring.FinalValue = target;
        spring.DelayTime = delay;
        spring.DampingRatio = 0.55f;
        spring.Period = TimeSpan.FromMilliseconds(360);
        visual.StartAnimation(property, spring);
    }
    private static void AnimateMatrix(Visual visual, Matrix4x4 target, TimeSpan duration)
    {
        visual.TransformMatrix = target;
    }

    private static void AnimateVector(Visual visual, string property, Vector3 target, TimeSpan duration, TimeSpan delay, Vector3? start = null)
    {
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        if (start is { } s)
        {
            animation.InsertKeyFrame(0f, s);
        }

        animation.InsertKeyFrame(1f, target);
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation(property, animation);
    }

    private static void AnimateScalar(Visual visual, string property, float target, TimeSpan duration, TimeSpan delay, float? start = null)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        if (start is { } s)
        {
            animation.InsertKeyFrame(0f, s);
        }

        animation.InsertKeyFrame(1f, target);
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation(property, animation);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        StopIdle();
        _pressed = true;
        _dragging = false;
        _pressPoint = _lastPoint = e.GetCurrentPoint(Root).Position;
        Root.CapturePointer(e.Pointer);
        ShowLabels(false);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(Root).Position;
        if (_pressed)
        {
            if (!_dragging && Math.Abs(pos.X - _pressPoint.X) + Math.Abs(pos.Y - _pressPoint.Y) > 5)
            {
                _dragging = true;
            }

            if (_dragging)
            {
                _yaw += (float)(pos.X - _lastPoint.X) * 0.008f;
                _pitch = Math.Clamp(_pitch + (float)(pos.Y - _lastPoint.Y) * 0.006f, -0.1f, 1.2f);
                _lastInteraction = Environment.TickCount64 / 1000.0;
                ApplyCamera(false);
            }
        }
        else
        {
            var clickable = IsExploded ? HitTest(pos) != EnginePart.None : IsEngineHit(pos, 40);
            ProtectedCursor = InputSystemCursor.Create(clickable ? InputSystemCursorShape.Hand : InputSystemCursorShape.SizeAll);
        }

        _lastPoint = pos;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var click = _pressed && !_dragging;
        _pressed = false;
        _dragging = false;
        Root.ReleasePointerCapture(e.Pointer);
        if (!click)
        {
            if (IsExploded)
            {
                ShowLabels(true);
            }

            return;
        }

        var pos = e.GetCurrentPoint(Root).Position;
        if (!IsExploded)
        {
            if (IsEngineHit(pos, 40))
            {
                ExplodeEngine();
            }
        }
        else
        {
            var part = HitTest(pos);
            if (part == EnginePart.None)
            {
                CollapseEngine();
            }
            else
            {
                Select(part == SelectedPart ? EnginePart.None : part);
                ShowLabels(true);
            }
        }

        e.Handled = true;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => ProtectedCursor = null;

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        _dragging = false;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        StopIdle();
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        _distance = Math.Clamp(_distance * (1f - delta / 1200f), 8f, 80f);
        _lastInteraction = Environment.TickCount64 / 1000.0;
        ApplyCamera(false);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left: FlyCamera(_yaw + 0.087f, _pitch, _distance); break;
            case VirtualKey.Right: FlyCamera(_yaw - 0.087f, _pitch, _distance); break;
            case VirtualKey.Up: FlyCamera(_yaw, _pitch + 0.035f, _distance); break;
            case VirtualKey.Down: FlyCamera(_yaw, _pitch - 0.035f, _distance); break;
            case VirtualKey.Home: ResetView(); break;
            case VirtualKey.Escape when SelectedPart != EnginePart.None: Select(EnginePart.None); break;
            case VirtualKey.Escape when IsExploded: CollapseEngine(); break;
            default: base.OnKeyDown(e); return;
        }

        e.Handled = true;
    }

    private EnginePart HitTest(Point pos, double slop = 40)
    {
        if (_scene is null)
        {
            return EnginePart.None;
        }

        var camera = CurrentCamera();
        var best = (Part: EnginePart.None, D2: slop * slop);
        for (var i = 0; i < _scene.SubExplodedCentersUnit.Length; i++)
        {
            var p = StageMath.Project(_scene.SubExplodedCentersUnit[i], camera);
            if (p is null)
            {
                continue;
            }

            var dx = p.Value.X - pos.X;
            var dy = p.Value.Y - pos.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < best.D2)
            {
                best = (WireMesh.SubParts[i].Group, d2);
            }
        }

        return best.Part;
    }

    private bool IsEngineHit(Point pos, double margin)
    {
        if (_scene is null)
        {
            return false;
        }

        var camera = CurrentCamera();
        var hits = _scene.SubCentersUnit.Select(p => StageMath.Project(p, camera)).Where(p => p is not null).Select(p => p!.Value).ToArray();
        if (hits.Length == 0)
        {
            return false;
        }

        var minX = hits.Min(p => p.X) - margin;
        var maxX = hits.Max(p => p.X) + margin;
        var minY = hits.Min(p => p.Y) - margin;
        var maxY = hits.Max(p => p.Y) + margin;
        return pos.X >= minX && pos.X <= maxX && pos.Y >= minY && pos.Y <= maxY;
    }
}