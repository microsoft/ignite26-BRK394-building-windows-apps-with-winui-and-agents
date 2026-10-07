using System.Numerics;
using Hangar.Controls.CompositionStage;
using Hangar.Helpers;
using Hangar.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Hangar.Controls;

public sealed partial class CompositionAircraftStage : UserControl, IAircraftStage
{
    private const float DefaultYaw = 0.78f;
    private const float DefaultPitch = 0.30f;
    private const float DefaultDistance = 46f;
    private const float EngineYaw = 1.12f;
    private const float EnginePitch = 0.24f;
    private const float EngineDistance = 16f;
    private static readonly EnginePart[] Parts = [EnginePart.Nacelle, EnginePart.Fan, EnginePart.Compressor, EnginePart.Turbine];

    private readonly AccessibilitySettings _accessibility = new();
    private readonly UISettings _uiSettings = new();
    private StageScene? _scene;
    private StagePalette? _palette;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _labelTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _idleTimer;
    private Thickness _focusPadding;
    private Point _pressPoint;
    private Point _lastPoint;
    private bool _pressed;
    private bool _dragging;
    private bool _labelsVisible;
    private float _yaw = DefaultYaw, _pitch = DefaultPitch, _distance = DefaultDistance;
    private float _maxDelay;
    private double _lastInteraction;
    private AircraftStatus _status = AircraftStatus.Attention;
    private double _heat = 0.6;
    private bool _animationsEnabled = true;

    public CompositionAircraftStage()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            LogStageError("InitializeComponent", ex);
            throw;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => RebuildScene();
        try
        {
            _accessibility.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(RebuildScene);
        }
        catch (Exception ex)
        {
            LogStageError("HighContrastChanged", ex);
        }
    }

    private static void LogStageError(string where, Exception ex)
    {
        try
        {
            var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "stage-error.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:O} {where}\n{ex}\nInner: {ex.InnerException}\n\n");
        }
        catch
        {
        }
    }

    public event EventHandler<EnginePart>? PartSelected;
    public event EventHandler<bool>? ExplodedChanged;
    public bool IsExploded { get; private set; }
    public EnginePart SelectedPart { get; private set; } = EnginePart.None;

    public Thickness FocusPadding
    {
        get => _focusPadding;
        set
        {
            _focusPadding = value;
            ApplyCamera(false);
        }
    }

    public void SetExploded(bool exploded)
    {
        if (IsExploded == exploded)
        {
            return;
        }

        IsExploded = exploded;
        ApplyExplode(exploded);
        ExplodedChanged?.Invoke(this, exploded);
        UpdateAutomationName();
    }

    public void ExplodeEngine()
    {
        if (SelectedPart != EnginePart.None)
        {
            Select(EnginePart.None);
        }

        FlyCamera(EngineYaw, EnginePitch, EngineDistance);
        SetExploded(true);
    }

    public void CollapseEngine() => ResetView();

    public void Select(EnginePart part)
    {
        if (SelectedPart == part)
        {
            return;
        }

        SelectedPart = part;
        var (yaw, pitch, distance) = part switch
        {
            EnginePart.Nacelle => (1.05f, 0.26f, 18f),
            EnginePart.Fan => (1.22f, 0.15f, 13.5f),
            EnginePart.Compressor => (1.0f, 0.3f, 13f),
            EnginePart.Turbine => (1.85f, 0.26f, 13f),
            _ when IsExploded => (EngineYaw, EnginePitch, EngineDistance),
            _ => (DefaultYaw, DefaultPitch, DefaultDistance),
        };
        FlyCamera(yaw, pitch, distance);
        ApplySelection();
        PartSelected?.Invoke(this, part);
        UpdateAutomationName();
    }

    public void SetHeat(double value)
    {
        _heat = Math.Clamp(value, 0, 1);
        RefreshHotBlade();
    }

    public void SetStatus(AircraftStatus status)
    {
        _status = status;
        if (status == AircraftStatus.Healthy)
        {
            _heat = 0;
        }

        RefreshHotBlade();
        UpdateAutomationName();
    }

    public void ResetView()
    {
        Select(EnginePart.None);
        FlyCamera(DefaultYaw, DefaultPitch, DefaultDistance);
        SetExploded(false);
    }

    public void Celebrate()
    {
        FlyCamera(_yaw + 0.26f, _pitch, _distance);
        RunCelebrate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _animationsEnabled = _uiSettings.AnimationsEnabled;
        BuildScene();
        Focus(FocusState.Pointer);
        UpdateAutomationName();
        _lastInteraction = Environment.TickCount64 / 1000.0;
        StartIdleTimer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _idleTimer?.Stop();
        _labelTimer?.Stop();
        if (_scene is not null)
        {
            StopSceneAnimations();
        }

        ElementCompositionPreview.SetElementChildVisual(Host, null);
    }

    private void BuildScene()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(Host).Compositor;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _palette = StagePalette.Create(this, compositor);
        _scene = StageSceneBuilder.Build(compositor, _palette, _animationsEnabled);
        StagePerf.Report("legacy-shapes", _scene.Root, sw.Elapsed.TotalMilliseconds);
        _maxDelay = WireMesh.SubParts.Count == 0 ? 0 : WireMesh.SubParts.Max(s => s.Delay);
        ElementCompositionPreview.SetElementChildVisual(Host, _scene.Root);
        ApplyCamera(false);
        ApplySelection();
        RefreshHotBlade();
    }

    private void RebuildScene()
    {
        if (!IsLoaded)
        {
            return;
        }

        ElementCompositionPreview.SetElementChildVisual(Host, null);
        BuildScene();
        if (IsExploded)
        {
            ApplyExplode(true, instant: true);
        }
    }

    private StageCamera CurrentCamera()
    {
        var center = StageMath.FocusCenter(ActualWidth, ActualHeight, FocusPadding.Left, FocusPadding.Top, FocusPadding.Right, FocusPadding.Bottom, out var focal);
        return new StageCamera(_yaw, _pitch, _distance, StageMath.DefaultTargetWorld, center, focal);
    }

    private void ApplyCamera(bool animate)
    {
        if (_scene is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var camera = CurrentCamera();
        _scene.Root.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        _scene.Backdrop.Size = _scene.Root.Size;
        var p = Matrix4x4.Identity;
        p.M34 = -1f / Math.Max(1, camera.Focal);
        _scene.Root.TransformMatrix = Matrix4x4.CreateTranslation(-camera.Center.X, -camera.Center.Y, 0) * p * Matrix4x4.CreateTranslation(camera.Center.X, camera.Center.Y, 0);
        var target = StageMath.CompositionWorldMatrix(camera);
        if (animate && _animationsEnabled)
        {
            AnimateMatrix(_scene.World, target, TimeSpan.FromMilliseconds(520));
        }
        else
        {
            _scene.World.TransformMatrix = target;
        }

        SortEngine2Depth(camera);
        if (_labelsVisible)
        {
            LayoutLabels();
        }
    }

    private void SortEngine2Depth(StageCamera camera)
    {
        if (_scene is null)
        {
            return;
        }

        var eye = camera.TargetWorld + camera.Distance * new Vector3(
            MathF.Cos(camera.Pitch) * MathF.Cos(camera.Yaw),
            MathF.Sin(camera.Pitch),
            MathF.Cos(camera.Pitch) * MathF.Sin(camera.Yaw));
        var forward = Vector3.Normalize(camera.TargetWorld - eye);
        // ShapeVisual siblings have no depth buffer; additive-like translucency is forgiving, but keep near Engine 2 modules on top.
        foreach (var item in _scene.SubContainers.Select((container, index) => new
        {
            Container = container,
            Depth = Vector3.Dot(StageMath.FromUnit(_scene.SubExplodedCentersUnit[index]) - eye, forward),
        }).OrderByDescending(i => i.Depth))
        {
            _scene.Engine2.Children.Remove(item.Container);
            _scene.Engine2.Children.InsertAtTop(item.Container);
        }
    }
    private void FlyCamera(float yaw, float pitch, float distance)
    {
        StopIdle();
        _yaw = NearestAngle(yaw);
        _pitch = Math.Clamp(pitch, -0.1f, 1.2f);
        _distance = Math.Clamp(distance, 8f, 80f);
        _lastInteraction = Environment.TickCount64 / 1000.0;
        ApplyCamera(true);
    }

    private float NearestAngle(float goal) => goal + MathF.Tau * MathF.Round((_yaw - goal) / MathF.Tau);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyCamera(false);

    private void UpdateAutomationName()
    {
        var state = _status == AircraftStatus.Healthy ? "Engine 2 healthy" : "Engine 2 needs attention, fan blade 14 flagged";
        var part = SelectedPart == EnginePart.None ? "no part selected" : $"{SelectedPart} selected";
        AutomationProperties.SetName(this, $"Composition hologram of CA 2207, A321neo. {state}; {part}. Arrow keys orbit, Home resets, Escape collapses.");
    }

    private void ShowLabels(bool show)
    {
        _labelsVisible = show;
        LabelLayer.Opacity = show ? 1 : 0;
        if (show)
        {
            LayoutLabels();
        }
    }

    private void LayoutLabels()
    {
        if (_scene is null || _palette is null)
        {
            return;
        }

        LabelLayer.Children.Clear();
        if (!IsExploded)
        {
            return;
        }

        var camera = CurrentCamera();
        for (var i = 0; i < WireMesh.SubParts.Count; i++)
        {
            var sub = WireMesh.SubParts[i];
            if (string.IsNullOrWhiteSpace(sub.Label))
            {
                continue;
            }

            var anchor = StageMath.Project(_scene.SubExplodedCentersUnit[i], camera);
            if (anchor is not { } p)
            {
                continue;
            }

            var up = sub.LabelUp ? -1 : 1;
            var textPoint = new Point(p.X + 48, p.Y + up * 34);
            var line = new Line { X1 = p.X, Y1 = p.Y, X2 = textPoint.X - 6, Y2 = textPoint.Y + 8, StrokeThickness = 1, Stroke = new SolidColorBrush(_palette.Label.Color), Opacity = 0.78 };
            var text = new TextBlock { Text = sub.Label, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(_palette.Label.Color), CharacterSpacing = 110 };
            Canvas.SetLeft(text, textPoint.X);
            Canvas.SetTop(text, textPoint.Y);
            LabelLayer.Children.Add(line);
            LabelLayer.Children.Add(text);
        }
    }
}