using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Hangar.Controls.CompositionStage;

internal sealed class StageCameraRig
{
    private readonly ContainerVisual _root;
    private readonly ContainerVisual _world;
    private readonly CompositionPropertySet _props;
    private readonly bool _animationsEnabled;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private Vector2 _center;
    private float _focal;
    private bool _turntable;

    public StageCameraRig(ContainerVisual root, ContainerVisual world, StageCamera camera, bool animationsEnabled)
    {
        _root = root;
        _world = world;
        _animationsEnabled = animationsEnabled;
        _props = world.Compositor.CreatePropertySet();
        _yaw = camera.Yaw;
        _pitch = camera.Pitch;
        _distance = camera.Distance;
        _center = camera.Center;
        _focal = camera.Focal;
        var target = StageMath.ToUnit(camera.TargetWorld);
        _props.InsertScalar("Yaw", _yaw);
        _props.InsertScalar("Pitch", _pitch);
        _props.InsertScalar("Distance", _distance);
        _props.InsertScalar("CenterX", _center.X);
        _props.InsertScalar("CenterY", _center.Y);
        _props.InsertScalar("Focal", _focal);
        _props.InsertScalar("FloatY", 0);
        _props.InsertScalar("TargetX", target.X);
        _props.InsertScalar("TargetY", target.Y);
        _props.InsertScalar("TargetZ", target.Z);
        StartWorldExpression();
    }

    public StageCamera Snapshot => new(_yaw, _pitch, _distance, StageMath.DefaultTargetWorld, _center, _focal);

    public void Resize(double width, double height, Thickness padding)
    {
        _root.Size = new Vector2((float)Math.Max(1, width), (float)Math.Max(1, height));
        _center = StageMath.FocusCenter(width, height, padding.Left, padding.Top, padding.Right, padding.Bottom, out _focal);
        _props.InsertScalar("CenterX", _center.X);
        _props.InsertScalar("CenterY", _center.Y);
        _props.InsertScalar("Focal", _focal);
        var p = Matrix4x4.Identity;
        p.M34 = -1f / Math.Max(1, _focal);
        _root.TransformMatrix = Matrix4x4.CreateTranslation(-_center.X, -_center.Y, 0) * p * Matrix4x4.CreateTranslation(_center.X, _center.Y, 0);
    }

    public void FlyTo(float yaw, float pitch, float distance, bool spring)
    {
        StopTurntable();
        _yaw = yaw;
        _pitch = Math.Clamp(pitch, -0.1f, 1.2f);
        _distance = Math.Clamp(distance, 8f, 80f);
        if (!_animationsEnabled || !spring)
        {
            _props.InsertScalar("Yaw", _yaw);
            _props.InsertScalar("Pitch", _pitch);
            _props.InsertScalar("Distance", _distance);
            return;
        }

        StartScalarSpring("Yaw", _yaw, 0.86f, 70);
        StartScalarSpring("Pitch", _pitch, 0.86f, 70);
        StartScalarSpring("Distance", _distance, 0.9f, 95);
    }

    public void Nudge(float dyaw, float dpitch)
    {
        _yaw += dyaw;
        _pitch = Math.Clamp(_pitch + dpitch, -0.1f, 1.2f);
        if (!_animationsEnabled)
        {
            _props.InsertScalar("Yaw", _yaw);
            _props.InsertScalar("Pitch", _pitch);
            return;
        }

        StartScalarSpring("Yaw", _yaw, 0.82f, 58);
        StartScalarSpring("Pitch", _pitch, 0.82f, 58);
    }

    public void Zoom(float multiplier)
    {
        _distance = Math.Clamp(_distance * multiplier, 8f, 80f);
        if (_animationsEnabled)
        {
            StartScalarSpring("Distance", _distance, 0.88f, 70);
        }
        else
        {
            _props.InsertScalar("Distance", _distance);
        }
    }

    public void StartTurntable()
    {
        if (!_animationsEnabled || _turntable)
        {
            return;
        }

        _turntable = true;
        var linear = _world.Compositor.CreateLinearEasingFunction();
        var yaw = _world.Compositor.CreateScalarKeyFrameAnimation();
        yaw.InsertKeyFrame(0, _yaw, linear);
        yaw.InsertKeyFrame(1, _yaw + MathF.Tau, linear);
        yaw.Duration = TimeSpan.FromSeconds(90);
        yaw.IterationBehavior = AnimationIterationBehavior.Forever;
        _props.StartAnimation("Yaw", yaw);
        var bob = _world.Compositor.CreateScalarKeyFrameAnimation();
        bob.InsertKeyFrame(0, 0);
        bob.InsertKeyFrame(0.5f, -6f);
        bob.InsertKeyFrame(1, 0);
        bob.Duration = TimeSpan.FromSeconds(6);
        bob.IterationBehavior = AnimationIterationBehavior.Forever;
        _props.StartAnimation("FloatY", bob);
    }

    public void StopTurntable()
    {
        if (!_turntable)
        {
            return;
        }

        _props.StopAnimation("Yaw");
        _props.StopAnimation("FloatY");
        _props.InsertScalar("Yaw", _yaw);
        _props.InsertScalar("FloatY", 0);
        _turntable = false;
    }

    private void StartWorldExpression()
    {
        var expression = _world.Compositor.CreateExpressionAnimation(
            "Matrix4x4.CreateTranslation(Vector3(-p.TargetX, -p.TargetY, -p.TargetZ)) * " +
            "Matrix4x4.CreateFromAxisAngle(Vector3(0f, 1f, 0f), p.Yaw) * " +
            "Matrix4x4.CreateFromAxisAngle(Vector3(1f, 0f, 0f), p.Pitch) * " +
            "Matrix4x4.CreateTranslation(Vector3(p.CenterX, p.CenterY + p.FloatY, p.Focal - p.Distance * 40f))");
        expression.SetReferenceParameter("p", _props);
        _world.StartAnimation(nameof(Visual.TransformMatrix), expression);
    }

    private void StartScalarSpring(string property, float finalValue, float damping, int periodMs)
    {
        var spring = _world.Compositor.CreateSpringScalarAnimation();
        spring.FinalValue = finalValue;
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(periodMs);
        _props.StartAnimation(property, spring);
    }
}


