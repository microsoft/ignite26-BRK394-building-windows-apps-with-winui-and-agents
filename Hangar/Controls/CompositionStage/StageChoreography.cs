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
        foreach (var part in _scene.Engine2Sprites)
        {
            var sub = WireMesh.SubParts[part.Sub];
            var targetOffset = exploded ? part.RestOffset + part.ExplodedOffset : part.RestOffset;
            var targetRoll = exploded ? sub.Roll : 0f;
            var delay = exploded ? sub.Delay : StageMath.ReverseDelay(part.Sub, _maxDelay);
            if (instant || !_animationsEnabled)
            {
                part.Visual.Offset = targetOffset;
                part.Visual.RotationAngle = targetRoll;
            }
            else
            {
                AnimateVectorSpring(part.Visual, nameof(Visual.Offset), targetOffset, TimeSpan.FromSeconds(delay));
                AnimateScalarSpring(part.Visual, nameof(Visual.RotationAngle), targetRoll, TimeSpan.FromSeconds(delay));
            }
        }

        AnimateScalar(_scene.Airframe, nameof(Visual.Opacity), exploded ? 0.18f : 1f, TimeSpan.FromMilliseconds(360), TimeSpan.Zero);
        AnimateScalar(_scene.Engine1, nameof(Visual.Opacity), exploded ? 0.15f : 1f, TimeSpan.FromMilliseconds(360), TimeSpan.Zero);
        if (exploded)
        {
            RunPulse();
            RunStreaks();
            QueueLabels();
        }
        else
        {
            HideStreaks();
            ApplySelection();
        }
    }

    private void ApplySelection()
    {
        if (_scene is null)
        {
            return;
        }

        foreach (var part in _scene.Engine2Sprites)
        {
            part.Visual.Opacity = SelectedPart == EnginePart.None || part.Part == SelectedPart ? 1f : 0.15f;
        }
    }

    private void RefreshHotBlade()
    {
        if (_scene is null || _palette is null)
        {
            return;
        }

        _palette.HotColor = StagePalette.Lerp(_palette.AmberColor, _palette.HotColor, (float)_heat);
        var healthy = _status == AircraftStatus.Healthy;
        foreach (var part in _scene.Engine2Sprites)
        {
            part.Visual.Brush = healthy ? part.HealthyBrush : part.IsHot ? part.HotBrush : part.NormalBrush;
            if (part.IsHot)
            {
                part.Visual.Opacity = healthy ? 0.72f : (float)Math.Clamp(0.62 + _heat * 0.38, 0.62, 1.0);
            }
        }
    }

    private void RunPulse()
    {
        if (_scene is null || !_animationsEnabled)
        {
            return;
        }

        var from = new Vector3(-110, -110, 0.6f * StageMath.K);
        var to = new Vector3(-110, -110, -5.0f * StageMath.K);
        _scene.PulseRing.Opacity = 1;
        _scene.PulseRing.Scale = Vector3.One;
        _scene.PulseRing.Offset = from;
        AnimateVector(_scene.PulseRing, nameof(Visual.Offset), to, TimeSpan.FromMilliseconds(920), TimeSpan.Zero);
        AnimateVector(_scene.PulseRing, nameof(Visual.Scale), new Vector3(1.65f), TimeSpan.FromMilliseconds(920), TimeSpan.Zero, Vector3.One);
        AnimateScalar(_scene.PulseRing, nameof(Visual.Opacity), 0f, TimeSpan.FromMilliseconds(920), TimeSpan.Zero, 0.9f);
    }

    private void RunStreaks()
    {
        if (_scene is null || !_animationsEnabled)
        {
            return;
        }

        var anchors = _scene.Engine2Sprites.Where(s => s.Sub % 7 == 0).Take(_scene.Streaks.Count).ToArray();
        for (var i = 0; i < _scene.Streaks.Count; i++)
        {
            var streak = _scene.Streaks[i];
            var anchor = anchors.Length == 0 ? new Vector3(-64, -18, 0) : anchors[i % anchors.Length].RestOffset;
            var from = anchor + new Vector3(-42, 10 - i * 4, 0);
            var to = from + new Vector3(80, 0, -90 - i * 12);
            streak.Offset = from;
            streak.Opacity = 0.65f;
            streak.RotationAngle = (i - 2) * 0.16f;
            AnimateVector(streak, nameof(Visual.Offset), to, TimeSpan.FromMilliseconds(720), TimeSpan.FromMilliseconds(80 * i));
            AnimateScalar(streak, nameof(Visual.Opacity), 0f, TimeSpan.FromMilliseconds(720), TimeSpan.FromMilliseconds(80 * i), 0.65f);
        }
    }

    private void HideStreaks()
    {
        if (_scene is null)
        {
            return;
        }

        foreach (var streak in _scene.Streaks)
        {
            streak.StopAnimation(nameof(Visual.Opacity));
            streak.Opacity = 0;
        }
    }

    private void RunCelebrate()
    {
        if (_scene is null || !_animationsEnabled)
        {
            return;
        }

        foreach (var part in _scene.Engine2Sprites)
        {
            part.Visual.Brush = part.HealthyBrush;
        }

        RunPulse();
        AnimateScalar(_scene.Engine2, nameof(Visual.Opacity), 0.72f, TimeSpan.FromMilliseconds(170), TimeSpan.Zero, 1f);
        AnimateScalar(_scene.Engine2, nameof(Visual.Opacity), 1f, TimeSpan.FromMilliseconds(420), TimeSpan.FromMilliseconds(170), 0.72f);
        QueueHealthyReset();
    }

    private void QueueHealthyReset()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(720);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { timer.Stop(); RefreshHotBlade(); };
        timer.Start();
    }

    private void QueueLabels(TimeSpan? delay = null)
    {
        _labelTimer?.Stop();
        _labelTimer = DispatcherQueue.CreateTimer();
        _labelTimer.Interval = delay ?? TimeSpan.FromSeconds(_animationsEnabled ? _maxDelay + 0.85f : 0.02f);
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
            if (_scene is null || _cameraRig is null || !_animationsEnabled || IsExploded || _idleRunning)
            {
                return;
            }

            if (Environment.TickCount64 / 1000.0 - _lastInteraction > 4)
            {
                _idleRunning = true;
                ShowLabels(false);
                _cameraRig.StartTurntable();
            }
        };
        _idleTimer.Start();
    }

    private void StopIdle()
    {
        _idleRunning = false;
        _cameraRig?.StopTurntable();
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
        foreach (var part in _scene.Engine2Sprites)
        {
            part.Visual.StopAnimation(nameof(Visual.Offset));
            part.Visual.StopAnimation(nameof(Visual.RotationAngle));
        }

        HideStreaks();
    }

    private static void AnimateVectorSpring(Visual visual, string property, Vector3 target, TimeSpan delay)
    {
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = target;
        spring.DelayTime = delay;
        spring.DampingRatio = 0.58f;
        spring.Period = TimeSpan.FromMilliseconds(330);
        visual.StartAnimation(property, spring);
    }

    private static void AnimateScalarSpring(Visual visual, string property, float target, TimeSpan delay)
    {
        var spring = visual.Compositor.CreateSpringScalarAnimation();
        spring.FinalValue = target;
        spring.DelayTime = delay;
        spring.DampingRatio = 0.58f;
        spring.Period = TimeSpan.FromMilliseconds(330);
        visual.StartAnimation(property, spring);
    }

    private static void AnimateVector(Visual visual, string property, Vector3 target, TimeSpan duration, TimeSpan delay, Vector3? start = null)
    {
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        if (start is { } s) animation.InsertKeyFrame(0f, s);
        animation.InsertKeyFrame(1f, target);
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation(property, animation);
    }

    private static void AnimateScalar(Visual visual, string property, float target, TimeSpan duration, TimeSpan delay, float? start = null)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        if (start is { } s) animation.InsertKeyFrame(0f, s);
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
            if (!_dragging && Math.Abs(pos.X - _pressPoint.X) + Math.Abs(pos.Y - _pressPoint.Y) > 5) _dragging = true;
            if (_dragging)
            {
                _yaw += (float)(pos.X - _lastPoint.X) * 0.008f;
                _pitch = Math.Clamp(_pitch + (float)(pos.Y - _lastPoint.Y) * 0.006f, -0.1f, 1.2f);
                _lastInteraction = Environment.TickCount64 / 1000.0;
                ApplyCamera(true);
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
            if (IsExploded) QueueLabels(TimeSpan.FromMilliseconds(250));
            return;
        }

        var pos = e.GetCurrentPoint(Root).Position;
        if (!IsExploded)
        {
            if (IsEngineHit(pos, 40)) ExplodeEngine();
        }
        else
        {
            var part = HitTest(pos);
            if (part == EnginePart.None) CollapseEngine(); else Select(part == SelectedPart ? EnginePart.None : part);
        }

        e.Handled = true;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => ProtectedCursor = null;

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        _dragging = false;
        if (IsExploded) QueueLabels(TimeSpan.FromMilliseconds(250));
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        StopIdle();
        ShowLabels(false);
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        _distance = Math.Clamp(_distance * (1f - delta / 1200f), 8f, 80f);
        _lastInteraction = Environment.TickCount64 / 1000.0;
        ApplyCamera(true);
        if (IsExploded) QueueLabels(TimeSpan.FromMilliseconds(250));
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
        if (_scene is null) return EnginePart.None;
        var camera = CurrentCamera();
        var best = (Part: EnginePart.None, D2: slop * slop);
        for (var i = 0; i < _scene.SubExplodedCentersUnit.Length; i++)
        {
            var p = StageMath.Project(_scene.SubExplodedCentersUnit[i], camera);
            if (p is null) continue;
            var d2 = Math.Pow(p.Value.X - pos.X, 2) + Math.Pow(p.Value.Y - pos.Y, 2);
            if (d2 < best.D2) best = (WireMesh.SubParts[i].Group, d2);
        }

        return best.Part;
    }

    private bool IsEngineHit(Point pos, double margin)
    {
        if (_scene is null) return false;
        var camera = CurrentCamera();
        var hits = _scene.SubCentersUnit.Select(p => StageMath.Project(p, camera)).Where(p => p is not null).Select(p => p!.Value).ToArray();
        if (hits.Length == 0) return false;
        return pos.X >= hits.Min(p => p.X) - margin && pos.X <= hits.Max(p => p.X) + margin && pos.Y >= hits.Min(p => p.Y) - margin && pos.Y <= hits.Max(p => p.Y) + margin;
    }
}


