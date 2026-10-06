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

    private bool _inkConfigured;

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
            ResetState();
        }
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
    }

    private bool HasSignature => SignatureInk.InkPresenter.StrokeContainer.GetStrokes().Count > 0;

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
        BurstRing.Opacity = 0;
        BurstRing2.Opacity = 0;
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
