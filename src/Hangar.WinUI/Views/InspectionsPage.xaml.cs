using System.Runtime.InteropServices.WindowsRuntime;
using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Input.Inking;

namespace Hangar.Views;

public sealed partial class InspectionsPage : Page
{
    private const string Technician = "Maya Okafor";
    private const string Finding = "Fan blade 14 leading-edge nick · 0.4 mm · within limits (AMM 72-21-00)";
    private const string AnnotationFile = "inspection-N2207C.isf";
    // Borescope frame coordinate of the leading-edge nick in Assets/Inspection/borescope.png.
    private const double NickU = 0.55;
    private const double NickV = 0.45;
    private const double FaultReticleSize = 96;

    private bool _inkConfigured;
    private bool _pendingArrivalAnimation;
    private string? _hologramTail;
    private Storyboard? _arrivalStoryboard;
    private int _scopeImagePixelWidth;
    private int _scopeImagePixelHeight;

    public InspectionsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) =>
        {
            ApplyInkPalette();
            RenderStatus();
        };
    }

    public FleetDataService Data => FleetDataService.Instance;

    private bool IsReleased => Data.Hero.Status == AircraftStatus.Healthy;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is "reset")
        {
            _hologramTail = null;
            _pendingArrivalAnimation = false;
            ResetState();
            UpdateHologramBreadcrumb();
            SetFinalHandoffVisuals();
            return;
        }

        if (e.Parameter is InspectionNavigationArgs args)
        {
            _hologramTail = args.Tail;
            _pendingArrivalAnimation = true;
        }
        else
        {
            _hologramTail = null;
            _pendingArrivalAnimation = false;
        }

        DispatcherQueue.TryEnqueue(RefreshNavigationState);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Data.Reset += OnDataReset;
        Data.HeroStatusChanged += OnHeroStatusChanged;

        if (!_inkConfigured)
        {
            ConfigureInk();
            _inkConfigured = true;
        }

        RenderStatus();
        UpdateHologramBreadcrumb();
        UpdateFaultGuidePosition();
        if (_pendingArrivalAnimation)
        {
            PlayArrivalFromHologram();
        }
#if DEBUG
        if (DebugAutoSign)
        {
            DebugAutoSign = false;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => DebugSign();
            timer.Start();
        }
#endif
    }

    /// <summary>Debug-only rehearsal hook (startpage.txt "Inspections dark sign"): writes a synthetic signature and signs off.</summary>
    internal static bool DebugAutoSign { get; set; }

#if DEBUG
    private void DebugSign()
    {
        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(SignatureInk.InkPresenter.CopyDefaultDrawingAttributes());
        var w = SignatureInk.ActualWidth;
        var h = SignatureInk.ActualHeight;
        var points = new List<Point>();
        for (var i = 0; i <= 60; i++)
        {
            var t = i / 60.0;
            points.Add(new Point(w * (0.12 + 0.7 * t), h * (0.55 + 0.18 * Math.Sin(t * Math.PI * 5) * (1 - t * 0.5))));
        }

        SignatureInk.InkPresenter.StrokeContainer.AddStroke(builder.CreateStroke(points));
        SignatureInk.InkPresenter.StrokeContainer.AddStroke(builder.CreateStroke(new[]
        {
            new Point(w * 0.2, h * 0.78), new Point(w * 0.5, h * 0.74), new Point(w * 0.86, h * 0.7),
        }));
        UpdateSignatureState();
        OnSignAndRelease(SignButton, new RoutedEventArgs());
    }
#endif

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Data.Reset -= OnDataReset;
        Data.HeroStatusChanged -= OnHeroStatusChanged;
    }

    private void OnDataReset(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ResetState);

    private void OnHeroStatusChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RenderStatus);

    private void ConfigureInk()
    {
        var devices = CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Touch;

        ScopeInk.InkPresenter.InputDeviceTypes = devices;
        SignatureInk.InkPresenter.InputDeviceTypes = devices;

        SignatureInk.InkPresenter.StrokesCollected += (_, _) => UpdateSignatureState();
        SignatureInk.InkPresenter.StrokesErased += (_, _) => UpdateSignatureState();

        ApplyInkPalette();
        ScopeToolbar.ActiveTool = PenButton;
    }

    private void ApplyInkPalette()
    {
        PenButton.Palette = new List<Brush>
        {
            ThemeRes.Brush(this, "AttentionBrush"),
            ThemeRes.Brush(this, "AccentCyanBrush"),
            ThemeRes.Brush(this, "CriticalBrush"),
            ThemeRes.Brush(this, "HealthyBrush"),
            ThemeRes.Brush(this, "PrimaryTextBrush"),
        };
        PenButton.SelectedBrushIndex = 0;

        var signature = SignatureInk.InkPresenter.CopyDefaultDrawingAttributes();
        signature.Color = ColorOf("PrimaryTextBrush");
        signature.Size = new Size(2.6, 2.6);
        signature.FitToCurve = true;
        signature.IgnorePressure = false;
        SignatureInk.InkPresenter.UpdateDefaultDrawingAttributes(signature);
    }

    private Windows.UI.Color ColorOf(string brushKey) =>
        ThemeRes.Brush(this, brushKey) is SolidColorBrush solid ? solid.Color : Microsoft.UI.Colors.White;

    private void RenderStatus()
    {
        var released = IsReleased;
        StatusPill.Background = ThemeRes.Brush(this, released ? "HealthySoftBrush" : "AttentionSoftBrush");
        var fg = ThemeRes.Brush(this, released ? "HealthyBrush" : "AttentionBrush");
        StatusDot.Fill = fg;
        StatusText.Foreground = fg;
        StatusText.Text = released ? "AIRWORTHY · RELEASED" : "FAN BLADE 14 · HOLD";
        AutomationProperties.SetName(StatusPill, released ? "Status: airworthy, released to service" : "Status: hold, fan blade 14 finding");

        SignButton.IsEnabled = !released && HasSignature;
        SignatureInk.InkPresenter.IsInputEnabled = !released;
        ClearSignatureButton.IsEnabled = !released;
        ReleaseNote.Text = released
            ? $"Signed by {Technician} · {Data.Hero.StatusNote}"
            : "Releases CA 2207 for the 05:52 departure";
        ReleaseNote.Foreground = ThemeRes.Brush(this, released ? "HealthyBrush" : "TertiaryTextBrush");
        ReleasedBanner.Opacity = released ? 1 : 0;
        UpdateFaultGuideVisibility();
    }

    private bool HasSignature => SignatureInk.InkPresenter.StrokeContainer.GetStrokes().Count > 0;

    /// <summary>Pre-fills the release entry from an AI draft. Never signs — the engineer still signs.</summary>
    public void PrefillReleaseEntry(string text)
    {
        ReleaseEntryBox.Text = text;
        ReleaseEntryDraftNote.Visibility = Visibility.Visible;
        ReleaseEntryBox.Focus(FocusState.Programmatic);
        ReleaseEntryBox.SelectionStart = text.Length;
    }

    private void UpdateSignatureState()
    {
        SignatureHint.Visibility = HasSignature ? Visibility.Collapsed : Visibility.Visible;
        SignButton.IsEnabled = !IsReleased && HasSignature;
    }

    private void ResetState()
    {
        ScopeInk.InkPresenter.StrokeContainer.Clear();
        SignatureInk.InkPresenter.StrokeContainer.Clear();
        SignatureRender.Children.Clear();
        SignatureInk.Visibility = Visibility.Visible;
        ReleaseEntryBox.Text = string.Empty;
        ReleaseEntryDraftNote.Visibility = Visibility.Collapsed;
        BurstRing.Opacity = 0;
        BurstRing2.Opacity = 0;
        SetFinalHandoffVisuals();
        UpdateSignatureState();
        RenderStatus();
        if (_inkConfigured)
        {
            ScopeToolbar.ActiveTool = PenButton;
        }
    }

    private async void OnSaveInk(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(AnnotationFile, CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            using (var output = stream.GetOutputStreamAt(0))
            {
                await ScopeInk.InkPresenter.StrokeContainer.SaveAsync(output);
                await output.FlushAsync();
            }

            Flash(SaveInkButton, "Saved");
        }
        catch (Exception ex)
        {
            Flash(SaveInkButton, "Save failed");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private async void OnRestoreInk(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(AnnotationFile) is not StorageFile file)
            {
                Flash(RestoreInkButton, "Nothing saved");
                return;
            }

            using var stream = await file.OpenReadAsync();
            using var input = stream.GetInputStreamAt(0);
            ScopeInk.InkPresenter.StrokeContainer.Clear();
            await ScopeInk.InkPresenter.StrokeContainer.LoadAsync(input);
            Flash(RestoreInkButton, "Restored");
        }
        catch (Exception ex)
        {
            Flash(RestoreInkButton, "Restore failed");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void OnClearInk(object sender, RoutedEventArgs e) => ScopeInk.InkPresenter.StrokeContainer.Clear();

    private void OnClearSignature(object sender, RoutedEventArgs e)
    {
        SignatureInk.InkPresenter.StrokeContainer.Clear();
        SignatureRender.Children.Clear();
        UpdateSignatureState();
    }

    private static void Flash(Button button, string message)
    {
        ToolTipService.SetToolTip(button, message);
        AutomationProperties.SetHelpText(button, message);
        if (ToolTipService.GetToolTip(button) is ToolTip tip)
        {
            tip.IsOpen = true;
        }
        else
        {
            var flyout = new Flyout { Content = new TextBlock { Text = message } };
            flyout.ShowAt(button);
            var timer = button.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1.2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => flyout.Hide();
            timer.Start();
        }
    }

    private async void OnSignAndRelease(object sender, RoutedEventArgs e)
    {
        if (!HasSignature || IsReleased)
        {
            return;
        }

        SignButton.IsEnabled = false;
        string? path = null;
        try
        {
            path = await SaveSignatureAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        Data.SignOff(Technician, Finding);
        if (path is not null && Data.SignOffWorkOrder is { } wo)
        {
            wo.SignaturePath = path;
        }

        RenderStatus();
        Celebrate();
    }

    private void RefreshNavigationState()
    {
        UpdateHologramBreadcrumb();
        RenderStatus();
        UpdateFaultGuidePosition();
        if (_pendingArrivalAnimation)
        {
            PlayArrivalFromHologram();
        }
        else
        {
            SetFinalHandoffVisuals();
        }
    }

    private void UpdateHologramBreadcrumb()
    {
        BackToHologramButton.Visibility = _hologramTail is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBackToHologram(object sender, RoutedEventArgs e)
    {
        var tail = string.IsNullOrWhiteSpace(_hologramTail) ? Data.Hero.Tail : _hologramTail;
        NavigationService.Navigate(
            typeof(AircraftPage),
            new AircraftNavigationArgs(tail, EnginePart.Fan),
            new DrillInNavigationTransitionInfo());
    }

    private void OnScopeImageOpened(object sender, RoutedEventArgs e)
    {
        if (ScopeImage.Source is BitmapSource bitmap)
        {
            _scopeImagePixelWidth = bitmap.PixelWidth;
            _scopeImagePixelHeight = bitmap.PixelHeight;
        }

        UpdateFaultGuidePosition();
    }

    private void OnScopeHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateFaultGuidePosition();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SignOffColumn.Width = new GridLength(e.NewSize.Width < 1200 ? 340 : 380);
    }

    private void UpdateFaultGuideVisibility()
    {
        var visible = !IsReleased;
        FaultReticle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            FaultReticle.Opacity = 0;
        }
        else if (!_pendingArrivalAnimation && _arrivalStoryboard is null)
        {
            FaultReticle.Opacity = 0.6;
            FaultReticleScale.ScaleX = 1;
            FaultReticleScale.ScaleY = 1;
        }
    }

    private void UpdateFaultGuidePosition()
    {
        var viewportWidth = ScopeHost.ActualWidth;
        var viewportHeight = ScopeHost.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var imageWidth = _scopeImagePixelWidth > 0 ? _scopeImagePixelWidth : viewportWidth;
        var imageHeight = _scopeImagePixelHeight > 0 ? _scopeImagePixelHeight : viewportHeight;
        var scale = Math.Max(viewportWidth / imageWidth, viewportHeight / imageHeight);
        var drawnWidth = imageWidth * scale;
        var drawnHeight = imageHeight * scale;
        var drawnLeft = (viewportWidth - drawnWidth) / 2;
        var drawnTop = (viewportHeight - drawnHeight) / 2;

        var x = drawnLeft + drawnWidth * NickU;
        var y = drawnTop + drawnHeight * NickV;
        Canvas.SetLeft(FaultReticle, x - FaultReticleSize / 2);
        Canvas.SetTop(FaultReticle, y - FaultReticleSize / 2);
    }

    private void PlayArrivalFromHologram()
    {
        _pendingArrivalAnimation = false;
        _arrivalStoryboard?.Stop();
        _arrivalStoryboard = null;
        UpdateFaultGuidePosition();

        if (!AnimationsEnabled())
        {
            SetFinalHandoffVisuals();
            return;
        }

        ScopeImageTransform.ScaleX = 1.12;
        ScopeImageTransform.ScaleY = 1.12;
        ScopeImage.Opacity = 0.72;
        FaultReticle.Visibility = IsReleased ? Visibility.Collapsed : Visibility.Visible;
        FaultReticle.Opacity = IsReleased ? 0 : 0;
        FaultReticleScale.ScaleX = 3;
        FaultReticleScale.ScaleY = 3;

        var story = new Storyboard();
        AddDouble(story, ScopeImageTransform, "ScaleX", 1.12, 1, 700, 0);
        AddDouble(story, ScopeImageTransform, "ScaleY", 1.12, 1, 700, 0);
        AddDouble(story, ScopeImage, "Opacity", 0.72, 1, 620, 0);
        AddFaultScale(story, "ScaleX");
        AddFaultScale(story, "ScaleY");

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(180), Value = 1 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(920), Value = 0.56 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1180), Value = 0.86 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1460), Value = 0.58 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1740), Value = 0.78 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(2100), Value = 0.6 });
        Storyboard.SetTarget(fade, FaultReticle);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);

        story.Completed += (_, _) =>
        {
            _arrivalStoryboard = null;
            SetFinalHandoffVisuals();
        };
        _arrivalStoryboard = story;
        story.Begin();
    }

    private void SetFinalHandoffVisuals()
    {
        ScopeImageTransform.ScaleX = 1;
        ScopeImageTransform.ScaleY = 1;
        ScopeImage.Opacity = 1;
        FaultReticleScale.ScaleX = 1;
        FaultReticleScale.ScaleY = 1;
        FaultReticle.Visibility = IsReleased ? Visibility.Collapsed : Visibility.Visible;
        FaultReticle.Opacity = IsReleased ? 0 : 0.6;
        UpdateFaultGuidePosition();
    }

    private static bool AnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private static void AddDouble(Storyboard story, DependencyObject target, string property, double from, double to, int durationMs, int delayMs)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }

    private void AddFaultScale(Storyboard story, string property)
    {
        var easeOut = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var pulseEase = new SineEase { EasingMode = EasingMode.EaseInOut };
        var scale = new DoubleAnimationUsingKeyFrames();
        scale.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 3 });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(560), Value = 0.9, EasingFunction = easeOut });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(740), Value = 1.04, EasingFunction = pulseEase });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(920), Value = 1, EasingFunction = pulseEase });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1180), Value = 1.08, EasingFunction = pulseEase });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1460), Value = 1, EasingFunction = pulseEase });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1740), Value = 1.06, EasingFunction = pulseEase });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(2100), Value = 1, EasingFunction = pulseEase });
        Storyboard.SetTarget(scale, FaultReticleScale);
        Storyboard.SetTargetProperty(scale, property);
        story.Children.Add(scale);
    }

    /// <summary>
    /// RenderTargetBitmap does not capture live ink, so strokes are re-drawn as XAML paths before capture.
    /// </summary>
    private async Task<string> SaveSignatureAsync()
    {
        DrawStrokesAsPaths();
        SignatureInk.Visibility = Visibility.Collapsed;
        SignatureRender.UpdateLayout();

        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(SignatureRender);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var dpi = 96 * (XamlRoot?.RasterizationScale ?? 1.0);

        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
            $"signature-{DateTime.Now:yyyyMMddHHmmssfff}.png",
            CreationCollisionOption.ReplaceExisting);

        using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,
                (uint)bitmap.PixelHeight,
                dpi,
                dpi,
                pixels);
            await encoder.FlushAsync();
        }

        return file.Path;
    }

    private void DrawStrokesAsPaths()
    {
        SignatureRender.Children.Clear();
        foreach (var stroke in SignatureInk.InkPresenter.StrokeContainer.GetStrokes())
        {
            var segments = stroke.GetRenderingSegments();
            if (segments.Count == 0)
            {
                continue;
            }

            var figure = new PathFigure { StartPoint = segments[0].Position, IsClosed = false, IsFilled = false };
            for (var i = 1; i < segments.Count; i++)
            {
                var s = segments[i];
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = s.BezierControlPoint1,
                    Point2 = s.BezierControlPoint2,
                    Point3 = s.Position,
                });
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            SignatureRender.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(stroke.DrawingAttributes.Color),
                StrokeThickness = stroke.DrawingAttributes.Size.Width,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            });
        }
    }

    private void Celebrate()
    {
        var cx = (Root.ActualWidth - 440) / 2;
        var cy = Root.ActualHeight / 2;
        foreach (var ring in new[] { BurstRing, BurstRing2 })
        {
            Canvas.SetLeft(ring, cx - ring.Width / 2);
            Canvas.SetTop(ring, cy - ring.Height / 2);
        }

        var story = new Storyboard();
        AddBurst(story, BurstRing, BurstScale, 0, 0.6, 2.2, 1100);
        AddBurst(story, BurstRing2, BurstScale2, 160, 0.5, 2.8, 1300);

        var bannerFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(220),
            Duration = TimeSpan.FromMilliseconds(420),
        };
        Storyboard.SetTarget(bannerFade, ReleasedBanner);
        Storyboard.SetTargetProperty(bannerFade, "Opacity");
        story.Children.Add(bannerFade);

        var bannerRise = new DoubleAnimation
        {
            From = 24,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(220),
            Duration = TimeSpan.FromMilliseconds(620),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
        };
        Storyboard.SetTarget(bannerRise, BannerTransform);
        Storyboard.SetTargetProperty(bannerRise, "TranslateY");
        story.Children.Add(bannerRise);

        story.Begin();
    }

    private static void AddBurst(Storyboard story, Ellipse ring, ScaleTransform scale, int delayMs, double from, double to, int durationMs)
    {
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4 };

        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var grow = new DoubleAnimation { From = from, To = to, BeginTime = begin, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, property);
            story.Children.Add(grow);
        }

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = duration, Value = 0, EasingFunction = ease });
        Storyboard.SetTarget(fade, ring);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);
    }
}
