using System.Diagnostics;
using System.Numerics;
using Hangar.Models;
using Hangar.Reactor.Ink;
using Hangar.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using static Hangar.Reactor.Kit;
using static Microsoft.UI.Reactor.Advanced.Factories;
using static Microsoft.UI.Reactor.Factories;

namespace Hangar.Reactor.Pages;

/// <summary>
/// Borescope inspection + sign-off. The scope viewport, fault reticle, annotation ink, signature pad
/// and release burst are all Win2D; everything else is Reactor elements.
/// </summary>
public sealed class InspectionsPage : HangarComponent<PageProps>
{
    private const string Technician = "Maya Okafor";
    private const string Finding = "Fan blade 14 leading-edge nick · 0.4 mm · within limits (AMM 72-21-00)";
    private const string AnnotationFile = "inspection-N2207C.json";
    private const string ScopeImageUri = "ms-appx:///Assets/Inspection/borescope.png";

    // Borescope frame coordinate of the leading-edge nick in Assets/Inspection/borescope.png.
    private const float NickU = 0.505f;
    private const float NickV = 0.452f;

    private static readonly string[] PenColors = ["AttentionBrush", "AccentCyanBrush", "CriticalBrush", "HealthyBrush", "PrimaryTextBrush"];

    // Ink survives navigation, like the original NavigationCacheMode="Required" page.
    private static readonly InkSurface ScopeInk = new() { ColorKey = "AttentionBrush", PenWidth = 4 };
    private static readonly InkSurface SignatureInk = new() { ColorKey = "PrimaryTextBrush", PenWidth = 2.6f };

    private static FleetDataService Data => FleetDataService.Instance;

#if DEBUG
    /// <summary>Debug-only rehearsal hook: writes a synthetic signature and signs off.</summary>
    internal static bool DebugAutoSign { get; set; }
#endif

    public override Element Render()
    {
        var p = Props;
        var propsRef = UseRef(p);
        propsRef.Current = p;
        UseDataVersion();

        var released = Data.Hero.Status == AircraftStatus.Healthy;
        var wide = UseBreakpoint(1200);

        var (tool, setTool) = UseState(ScopeInk.Tool);
        var (penColor, setPenColor) = UseState(ScopeInk.ColorKey);
        var (signatureCount, setSignatureCount) = UseState(SignatureInk.Count);
        var (flash, setFlash) = UseState<(string Id, string Text)?>(null);
        var (signing, setSigning) = UseState(false);

        var scopeCanvas = UseRef<CanvasControl?>(null);
        var signatureCanvas = UseRef<CanvasControl?>(null);
        var burstCanvas = UseRef<CanvasControl?>(null);
        var scopeHost = UseRef<FrameworkElement?>(null);
        var signatureHost = UseRef<FrameworkElement?>(null);
        var bitmap = UseRef<CanvasBitmap?>(null);
        var arrival = UseRef<Stopwatch?>(null);
        var burst = UseRef<Stopwatch?>(null);
        var bannerAnimate = UseRef(false);
        var flashTimer = UseRef<Microsoft.UI.Dispatching.DispatcherQueueTimer?>(null);

        var hologramTail = (p.Arg as InspectionNavigationArgs)?.Tail;

        Color Res(FrameworkElement? fe, string key) => fe is null ? Microsoft.UI.Colors.Transparent : Col(fe, key);

        void InvalidateAll()
        {
            scopeCanvas.Current?.Invalidate();
            signatureCanvas.Current?.Invalidate();
            burstCanvas.Current?.Invalidate();
        }

        // Ink surface → Reactor state + redraws; data reset; arrival animation; render loop for animations.
        UseEffect(() =>
        {
            Action onScopeInvalidated = () => scopeCanvas.Current?.Invalidate();
            Action onSignatureInvalidated = () => signatureCanvas.Current?.Invalidate();
            Action onSignatureChanged = () => setSignatureCount(SignatureInk.Count);
            EventHandler onReset = (_, _) =>
            {
                ScopeInk.Clear();
                SignatureInk.Clear();
                ScopeInk.Tool = InkTool.Pen;
                ScopeInk.ColorKey = PenColors[0];
                setTool(InkTool.Pen);
                setPenColor(PenColors[0]);
                setSigning(false);
                burst.Current = null;
                arrival.Current = null;
                InvalidateAll();
            };
            EventHandler<object> onFrame = (_, _) =>
            {
                if (arrival.Current is { ElapsedMilliseconds: > 2200 }) arrival.Current = null;
                if (burst.Current is { ElapsedMilliseconds: > 1600 }) burst.Current = null;
                if (arrival.Current is not null) scopeCanvas.Current?.Invalidate();
                if (burst.Current is not null) burstCanvas.Current?.Invalidate();
            };

            ScopeInk.Invalidated += onScopeInvalidated;
            SignatureInk.Invalidated += onSignatureInvalidated;
            SignatureInk.Changed += onSignatureChanged;
            Data.Reset += onReset;
            CompositionTarget.Rendering += onFrame;

            if (propsRef.Current.Arg is InspectionNavigationArgs && AnimationsEnabled())
            {
                arrival.Current = Stopwatch.StartNew();
            }

            return () =>
            {
                ScopeInk.Invalidated -= onScopeInvalidated;
                SignatureInk.Invalidated -= onSignatureInvalidated;
                SignatureInk.Changed -= onSignatureChanged;
                Data.Reset -= onReset;
                CompositionTarget.Rendering -= onFrame;
                flashTimer.Current?.Stop();
            };
        }, Array.Empty<object>());

        UseEffect(() => InvalidateAll(), p.IsDark, released);

        ScopeInk.Tool = tool;
        ScopeInk.ColorKey = penColor;
        SignatureInk.IsEnabled = !released && !signing;

        var hasSignature = signatureCount > 0;

        void Flash(string id, string text)
        {
            setFlash((id, text));
            flashTimer.Current?.Stop();
            var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1.6);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => setFlash(null);
            flashTimer.Current = timer;
            timer.Start();
        }

        async void SaveInk()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(AnnotationFile, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, ScopeInk.Save());
                Flash("SaveInkButton", "Saved");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Flash("SaveInkButton", "Save failed");
            }
        }

        async void RestoreInk()
        {
            try
            {
                if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(AnnotationFile) is not StorageFile file)
                {
                    Flash("RestoreInkButton", "Nothing saved");
                    return;
                }

                ScopeInk.Load(await FileIO.ReadTextAsync(file));
                Flash("RestoreInkButton", "Restored");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Flash("RestoreInkButton", "Restore failed");
            }
        }

        async void SignAndRelease()
        {
            if (SignatureInk.Count == 0 || Data.Hero.Status == AircraftStatus.Healthy || signing)
            {
                return;
            }

            setSigning(true);
            string? path = null;
            try
            {
                if (signatureCanvas.Current is { } c && c.ActualWidth > 0)
                {
                    path = Path.Combine(ApplicationData.Current.LocalFolder.Path, $"signature-{DateTime.Now:yyyyMMddHHmmssfff}.png");
                    var dpi = 96f * (float)(c.XamlRoot?.RasterizationScale ?? 1.0);
                    await SignatureInk.SavePngAsync(path, (float)c.ActualWidth, (float)c.ActualHeight, dpi, Col(c, "PrimaryTextBrush"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                path = null;
            }

            bannerAnimate.Current = true;
            if (AnimationsEnabled()) burst.Current = Stopwatch.StartNew();
            Data.SignOff(Technician, Finding);
            if (path is not null && Data.SignOffWorkOrder is { } wo)
            {
                wo.SignaturePath = path;
            }

            setSigning(false);
        }

#if DEBUG
        void DebugSign()
        {
            if (signatureHost.Current is not { } host) return;
            var w = (float)host.ActualWidth;
            var h = (float)host.ActualHeight;
            var wave = Enumerable.Range(0, 61).Select(i =>
            {
                var t = i / 60f;
                return new Vector2(w * (0.12f + 0.7f * t), h * (0.55f + 0.18f * MathF.Sin(t * MathF.PI * 5) * (1 - t * 0.5f)));
            });
            SignatureInk.AddStroke(wave);
            SignatureInk.AddStroke([new Vector2(w * 0.2f, h * 0.78f), new Vector2(w * 0.5f, h * 0.74f), new Vector2(w * 0.86f, h * 0.7f)]);
            SignAndRelease();
        }
#endif

        // ── Scope drawing ──
        void DrawScope(CanvasDrawingSession ds)
        {
            if (scopeCanvas.Current is not { } c) return;
            var w = (float)c.ActualWidth;
            var h = (float)c.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var ms = arrival.Current?.ElapsedMilliseconds ?? -1;
            var animating = ms >= 0;
            var imageScale = animating ? 1.12f - 0.12f * ExpoOut(ms / 700f) : 1f;
            var imageOpacity = animating ? 0.72f + 0.28f * ExpoOut(ms / 620f) : 1f;
            var reticleScale = animating ? Keyframes(ms, ReticleScaleFrames) : 1f;
            var reticleOpacity = released ? 0f : animating ? Keyframes(ms, ReticleOpacityFrames) : 0.6f;

            // UniformToFill mapping (same as the original Image + fault-guide maths).
            var iw = bitmap.Current?.SizeInPixels.Width ?? (uint)w;
            var ih = bitmap.Current?.SizeInPixels.Height ?? (uint)h;
            var scale = MathF.Max(w / iw, h / ih);
            var dw = iw * scale;
            var dh = ih * scale;
            var dest = new Rect((w - dw) / 2, (h - dh) / 2, dw, dh);

            if (bitmap.Current is { } bmp)
            {
                var sw = dw * imageScale;
                var sh = dh * imageScale;
                ds.DrawImage(bmp, new Rect((w - sw) / 2, (h - sh) / 2, sw, sh), bmp.Bounds, imageOpacity, CanvasImageInterpolation.HighQualityCubic);
            }

            // Top scrim (HeroScrimBrush flipped at 0.42).
            var scrim = ScrimColor(c);
            using (var top = new CanvasLinearGradientBrush(ds, WithAlpha(scrim, scrim.A / 255.0 * 0.42), WithAlpha(scrim, 0))
                   { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, 180) })
            {
                ds.FillRectangle(0, 0, w, 180, top);
            }

            // Lens vignette: 34px #33000000 frame at 0.55 opacity.
            using (var outer = CanvasGeometry.CreateRoundedRectangle(ds, 0, 0, w, h, 16, 16))
            using (var inner = CanvasGeometry.CreateRectangle(ds, 34, 34, MathF.Max(0, w - 68), MathF.Max(0, h - 68)))
            using (var frame = outer.CombineWith(inner, Matrix3x2.Identity, CanvasGeometryCombine.Exclude))
            {
                ds.FillGeometry(frame, Color.FromArgb(28, 0, 0, 0));
            }

            // Fault reticle at the nick.
            if (reticleOpacity > 0.001f)
            {
                var center = new Vector2((float)(dest.X + dest.Width * NickU), (float)(dest.Y + dest.Height * NickV));
                var critical = WithAlpha(Res(c, "CriticalBrush"), reticleOpacity);
                var s = reticleScale;
                ds.DrawCircle(center, (48 - 1.25f) * s, critical, 2.5f * s);
                ds.DrawCircle(center, (31 - 0.75f) * s, WithAlpha(critical, reticleOpacity * 0.75), 1.5f * s);
                ds.DrawLine(center + new Vector2(0, -48) * s, center + new Vector2(0, -32) * s, critical, 2 * s);
                ds.DrawLine(center + new Vector2(0, 32) * s, center + new Vector2(0, 48) * s, critical, 2 * s);
                ds.DrawLine(center + new Vector2(-48, 0) * s, center + new Vector2(-32, 0) * s, critical, 2 * s);
                ds.DrawLine(center + new Vector2(32, 0) * s, center + new Vector2(48, 0) * s, critical, 2 * s);
            }

            ScopeInk.Draw(ds, key => Res(c, key));
        }

        void DrawSignature(CanvasDrawingSession ds)
        {
            if (signatureCanvas.Current is { } c) SignatureInk.Draw(ds, key => Res(c, key));
        }

        void DrawBurst(CanvasDrawingSession ds)
        {
            if (burst.Current is not { } clock || burstCanvas.Current is not { } c || scopeHost.Current is not { } host) return;
            var origin = host.TransformToVisual(c).TransformPoint(new Point(host.ActualWidth / 2, host.ActualHeight / 2));
            var center = new Vector2((float)origin.X, (float)origin.Y);
            var healthy = Res(c, "HealthyBrush");
            var ms = clock.ElapsedMilliseconds;
            Ring(ms, 0, 0.6f, 2.2f, 1100, 6);
            Ring(ms, 160, 0.5f, 2.8f, 1300, 2);

            void Ring(long t, int delay, float from, float to, int duration, float thickness)
            {
                var k = (t - delay) / (float)duration;
                if (k < 0 || k > 1) return;
                var r = 160 * (from + (to - from) * ExpoOut(k));
                ds.DrawCircle(center, r, WithAlpha(healthy, 1 - k), thickness);
            }
        }

        // ── Header ──
        var statusPill = (released
                ? Pill("AIRWORTHY · RELEASED", "HealthyBrush", "HealthySoftBrush", 8)
                : Pill("FAN BLADE 14 · HOLD", "AttentionBrush", "AttentionSoftBrush", 8))
            .AutomationId("InspectionStatusPill")
            .AutomationName(released ? "Status: airworthy, released to service" : "Status: hold, fan blade 14 finding");

        var header = Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
            [
                VStack(2,
                    Eyebrow("CA 2207 · ENGINE 2 · FAN STAGE · BORESCOPE"),
                    HStack(18,
                        TextBlock(Data.Hero.Tail).FontFamily(MonoFont).FontSize(15).SemiBold().Foreground(Primary)
                            .VAlign(VerticalAlignment.Center)
                            .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1),
                        statusPill),
                    Body("Probe BSI-4 · 6 mm articulating · captured 02:31 by Maya Okafor").Foreground(Secondary).Margin(0, 6, 0, 0)),
                hologramTail is not null
                    ? Button(Body("← From hologram · Engine 2 · Fan blade 14"),
                            () => propsRef.Current.Navigate(PageKind.Aircraft, new AircraftNavigationArgs(hologramTail, EnginePart.Fan)))
                        .Background(B("GlassBrush")).BorderBrush(B("GlassStrokeBrush")).BorderThickness(1)
                        .CornerRadius(18).Padding(14, 8).VAlign(VerticalAlignment.Top)
                        .AutomationId("BackToHologramButton").AutomationName("Back to hologram fan blade")
                        .Grid(0, 1)
                    : Empty(),
            ]).Set(g => g.ColumnSpacing = 20).Margin(40, 28, 40, 20);

        // ── Scope viewport ──
        var scopeSurface = Grid([GridSize.Star(1)], [GridSize.Star(1)],
            [
                Win2DCanvas((ds, _) => DrawScope(ds), async c =>
                    {
                        bitmap.Current ??= await CanvasBitmap.LoadAsync(c, new Uri(ScopeImageUri));
                        c.Invalidate();
                    })
                    .UseSharedDevice()
                    .Set(c =>
                    {
                        scopeCanvas.Current = c;
                        c.IsHitTestVisible = false;
                    }),
                Mono("BSI-4 · 6 mm · 0°").Foreground(Secondary).Margin(18)
                    .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top).IsHitTestVisible(false),
                Mono("LE nick · 0.38 mm").Foreground(Secondary).Margin(18)
                    .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom).IsHitTestVisible(false),
            ])
            .Background(B("SubtleFillColorTransparentBrush"))
            .AutomationId("InspectionInkCanvas")
            .AutomationName("Inspection annotation canvas. Borescope image of Engine 2 fan stage, blade 14 leading edge")
            .OnMount(fe =>
            {
                fe.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.None;
                HangarCursor(fe, Microsoft.UI.Input.InputSystemCursorShape.Cross);
            })
            .OnPointerPressed((s, e) => ScopeInk.Pressed((UIElement)s, e))
            .OnPointerMoved((s, e) => ScopeInk.Moved((UIElement)s, e))
            .OnPointerReleased((s, e) => ScopeInk.Released((UIElement)s, e))
            .OnPointerCanceled((_, _) => ScopeInk.Canceled())
            .OnPointerCaptureLost((_, _) => ScopeInk.Canceled());

        var scope = Border(scopeSurface)
            .Background(B("GlassBrush")).BorderBrush(B("GlassStrokeBrush")).BorderThickness(1).CornerRadius(16)
            .OnMount(fe => scopeHost.Current = fe);

        // ── Ink toolbar ──
        Element ToolButton(InkTool t, string glyph, string label, string id) =>
            Button(Glyph(glyph, 16), () => setTool(t))
                .Background(tool == t ? B("AccentCyanSoftBrush") : B("SubtleFillColorTransparentBrush"))
                .BorderBrush(tool == t ? Cyan : B("SubtleFillColorTransparentBrush"))
                .Foreground(tool == t ? Cyan : Primary)
                .BorderThickness(1).CornerRadius(8).Width(40).Height(36).Padding(0)
                .ToolTip(label).AutomationId(id).AutomationName(tool == t ? $"{label}, selected" : label);

        Element Swatch(string key, int i)
        {
            var selected = penColor == key && tool != InkTool.Eraser;
            return Button(Border(Empty()).Width(14).Height(14).CornerRadius(7).Background(B(key))
                        .BorderBrush(B("GlassStrokeBrush")).BorderThickness(1),
                    () =>
                    {
                        setPenColor(key);
                        if (tool == InkTool.Eraser) setTool(InkTool.Pen);
                    })
                .Background(B("SubtleFillColorTransparentBrush"))
                .BorderBrush(selected ? Primary : B("SubtleFillColorTransparentBrush"))
                .BorderThickness(selected ? 2 : 1).CornerRadius(14).Width(28).Height(28).Padding(0)
                .ToolTip(ColorName(key)).AutomationId($"InkColor{i}").AutomationName($"Ink colour {ColorName(key)}");
        }

        Element ActionButton(string id, string glyph, string? label, string name, Action onClick)
        {
            var flashing = flash is { } f && f.Id == id;
            Element content = label is null ? Glyph(glyph, 14) : IconLabel(glyph, flashing ? flash!.Value.Text : label);
            return Button(content, onClick)
                .Foreground(flashing ? Cyan : Primary)
                .CornerRadius(8).Padding(label is null ? 10 : 12, 7)
                .VAlign(VerticalAlignment.Center)
                .ToolTip(flashing ? flash!.Value.Text : name).AutomationId(id).AutomationName(name)
                .Set(b => Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(b, flashing ? flash!.Value.Text : string.Empty));
        }

        var toolbar = Border(HStack(6,
                ToolButton(InkTool.Pen, "\uEE56", "Ballpoint pen", "InkPenButton"),
                ToolButton(InkTool.Highlighter, "\uE7E6", "Highlighter", "InkHighlighterButton"),
                ToolButton(InkTool.Eraser, "\uE75C", "Stroke eraser", "InkEraserButton"),
                VDivider(24).Margin(4, 0, 4, 0),
                HStack(2, PenColors.Select((k, i) => Swatch(k, i)).ToArray()),
                VDivider(24).Margin(4, 0, 4, 0),
                ActionButton("SaveInkButton", "\uE74E", "Save", "Save annotations", SaveInk),
                ActionButton("RestoreInkButton", "\uE777", "Restore", "Restore saved annotations", RestoreInk),
                ActionButton("ClearInkButton", "\uE894", null, "Clear annotations", () => ScopeInk.Clear())))
            .Background(B("GlassBrush")).BorderBrush(B("GlassStrokeBrush")).BorderThickness(1).CornerRadius(16)
            .Padding(8).HAlign(HorizontalAlignment.Left)
            .AutomationId("InspectionInkToolbar").AutomationName("Annotation tools");

        var scopeColumn = Grid([GridSize.Star(1)], [GridSize.Star(1), GridSize.Auto],
            [scope, toolbar.Grid(1, 0)]).Set(g => g.RowSpacing = 14);

        // ── Sign-off panel ──
        Element Metric(string label, string value, bool primary) =>
            VStack(2, Eyebrow(label), Mono(value, 20).Foreground(primary ? Primary : Secondary));

        var signaturePad = Grid([GridSize.Star(1)], [GridSize.Star(1)],
            [
                Border(Empty()).Height(1).Background(B("GlassStrokeBrush")).Margin(20, 0, 20, 34)
                    .VAlign(VerticalAlignment.Bottom).IsHitTestVisible(false),
                hasSignature || released
                    ? Empty()
                    : Body("Sign here").Foreground(Tertiary).Margin(20, 0, 0, 12)
                        .VAlign(VerticalAlignment.Bottom).IsHitTestVisible(false),
                Win2DCanvas((ds, _) => DrawSignature(ds))
                    .UseSharedDevice()
                    .Set(c =>
                    {
                        signatureCanvas.Current = c;
                        c.IsHitTestVisible = false;
                    }),
            ])
            .Height(132).Background(B("SurfaceBrush")).BorderBrush(B("GlassStrokeBrush")).BorderThickness(1).CornerRadius(10)
            .AutomationId("SignatureInkCanvas").AutomationName(hasSignature ? "Signature pad, signed" : "Signature pad, empty")
            .OnMount(fe =>
            {
                signatureHost.Current = fe;
                fe.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.None;
                HangarCursor(fe, Microsoft.UI.Input.InputSystemCursorShape.Pin);
#if DEBUG
                if (DebugAutoSign)
                {
                    DebugAutoSign = false;
                    var timer = fe.DispatcherQueue.CreateTimer();
                    timer.Interval = TimeSpan.FromSeconds(2);
                    timer.IsRepeating = false;
                    timer.Tick += (_, _) => DebugSign();
                    timer.Start();
                }
#endif
            })
            .OnPointerPressed((s, e) => SignatureInk.Pressed((UIElement)s, e))
            .OnPointerMoved((s, e) => SignatureInk.Moved((UIElement)s, e))
            .OnPointerReleased((s, e) => SignatureInk.Released((UIElement)s, e))
            .OnPointerCanceled((_, _) => SignatureInk.Canceled())
            .OnPointerCaptureLost((_, _) => SignatureInk.Canceled());

        var signOff = Border(VStack(16,
                VStack(4,
                    Eyebrow("FINDING · WO 2207-014"),
                    SectionTitle("Fan blade 14 · leading-edge nick").TextWrapping(TextWrapping.Wrap)),
                Grid([GridSize.Star(1), GridSize.Star(1), GridSize.Star(1)], [GridSize.Auto],
                    [
                        Metric("DEPTH", "0.4 mm", true),
                        Metric("LIMIT", "0.76 mm", false).Grid(0, 1),
                        Metric("AMM", "72-21-00", false).Grid(0, 2),
                    ]).Set(g => g.ColumnSpacing = 12),
                Border(HStack(8,
                        Glyph("\uE73E", 14).Foreground(B("HealthyBrush")),
                        Body("Within serviceable limits · blend not required").Foreground(B("HealthyBrush"))
                            .VAlign(VerticalAlignment.Center)))
                    .Background(B("HealthySoftBrush")).CornerRadius(8).Padding(12, 8),
                Border(Empty()).Height(1).Background(B("GlassStrokeBrush")),
                Grid([GridSize.Auto, GridSize.Star(1)], [GridSize.Auto],
                    [
                        PersonPicture().DisplayName(Technician).Width(40).Height(40).AutomationName(Technician),
                        VStack(0,
                            Body(Technician).Foreground(Primary),
                            Body("B1 licensed engineer · Lic. EASA-B1-48213").Foreground(Tertiary))
                            .VAlign(VerticalAlignment.Center).Grid(0, 1),
                    ]).Set(g => g.ColumnSpacing = 12),
                VStack(8,
                    Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
                        [
                            Eyebrow("SIGNATURE").VAlign(VerticalAlignment.Center),
                            Button(TextBlock("Clear").FontSize(13).Foreground(!released && hasSignature ? Cyan : B("TextFillColorDisabledBrush")), () => SignatureInk.Clear())
                                .Background(B("SubtleFillColorTransparentBrush")).BorderThickness(0)
                                .Padding(6, 2).CornerRadius(4).IsEnabled(!released && hasSignature)
                                .AutomationId("ClearSignatureButton").AutomationName("Clear signature")
                                .Grid(0, 1),
                        ]),
                    signaturePad),
                Button(HStack(10,
                            Glyph("\uE8FB", 16),
                            TextBlock(signing ? "Signing…" : "Sign & release to service").SemiBold().VAlign(VerticalAlignment.Center))
                        .HAlign(HorizontalAlignment.Center).Padding(0, 6, 0, 6),
                        SignAndRelease)
                    .AccentButton().CornerRadius(8).HAlign(HorizontalAlignment.Stretch)
                    .IsEnabled(!released && hasSignature && !signing)
                    .AutomationId("SignAndReleaseButton").AutomationName("Sign and release to service"),
                Body(released ? $"Signed by {Technician} · {Data.Hero.StatusNote}" : "Releases CA 2207 for the 05:52 departure")
                    .Foreground(released ? B("HealthyBrush") : Tertiary)
                    .TextAlignment(TextAlignment.Center).HAlign(HorizontalAlignment.Center)
                    .AutomationId("ReleaseNote")))
            .Background(B("GlassBrush")).BorderBrush(B("GlassStrokeBrush")).BorderThickness(1).CornerRadius(16)
            .Padding(24).VAlign(VerticalAlignment.Center)
            .AutomationId("SignOffPanel");

        // ── Released banner (fades + rises in once, right after signing) ──
        var banner = released
            ? Border(VStack(2,
                    Eyebrow("RELEASED TO SERVICE · 03:12").Foreground(B("HealthyBrush")).HAlign(HorizontalAlignment.Center),
                    PageTitle("Wheels up at 05:52").HAlign(HorizontalAlignment.Center)))
                .Background(B("GlassBrush")).BorderBrush(B("HealthyBrush")).BorderThickness(1).CornerRadius(16)
                .Padding(28, 18).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                .IsHitTestVisible(false)
                .AutomationId("ReleasedBanner")
                .OnMount(fe =>
                {
                    if (!bannerAnimate.Current || !AnimationsEnabled()) return;
                    bannerAnimate.Current = false;
                    PlayBanner(fe);
                })
            : Empty();

        var body = Grid([GridSize.Star(1), GridSize.Px(wide ? 380 : 340)], [GridSize.Star(1)],
            [
                scopeColumn,
                banner,
                signOff.Grid(0, 1),
            ]).Set(g => g.ColumnSpacing = 24).Margin(40, 0, 40, 32);

        return Grid([GridSize.Star(1)], [GridSize.Auto, GridSize.Star(1)],
            [
                Image(ScopeImageUri).Set(img => img.Stretch = Stretch.UniformToFill)
                    .Opacity(0.25).IsHitTestVisible(false).Grid(0, 0, 2, 1),
                Border(Empty()).Background(B("HeroScrimBrush")).Opacity(0.86).IsHitTestVisible(false).Grid(0, 0, 2, 1),
                header,
                body.Grid(1, 0),
                Win2DCanvas((ds, _) => DrawBurst(ds))
                    .UseSharedDevice()
                    .Set(c =>
                    {
                        burstCanvas.Current = c;
                        c.IsHitTestVisible = false;
                    })
                    .Grid(0, 0, 2, 1),
            ]).AutomationId("InspectionsRoot");
    }

    // ── Animation helpers ──
    private static readonly (float T, float V)[] ReticleScaleFrames =
        [(0, 3), (560, 0.9f), (740, 1.04f), (920, 1), (1180, 1.08f), (1460, 1), (1740, 1.06f), (2100, 1)];

    private static readonly (float T, float V)[] ReticleOpacityFrames =
        [(0, 0), (180, 1), (920, 0.56f), (1180, 0.86f), (1460, 0.58f), (1740, 0.78f), (2100, 0.6f)];

    private static float Keyframes(long ms, (float T, float V)[] frames)
    {
        if (ms <= frames[0].T) return frames[0].V;
        for (var i = 1; i < frames.Length; i++)
        {
            if (ms <= frames[i].T)
            {
                var (t0, v0) = frames[i - 1];
                var (t1, v1) = frames[i];
                var k = (ms - t0) / (t1 - t0);
                var eased = i == 1 ? ExpoOut(k) : 0.5f - 0.5f * MathF.Cos(MathF.PI * k);
                return v0 + (v1 - v0) * eased;
            }
        }

        return frames[^1].V;
    }

    /// <summary>WinUI ExponentialEase (Exponent=5, EaseOut).</summary>
    private static float ExpoOut(float t)
    {
        t = Math.Clamp(t, 0, 1);
        var u = 1 - t;
        return 1 - (MathF.Exp(5 * u) - 1) / (MathF.Exp(5) - 1);
    }

    private static void PlayBanner(FrameworkElement fe)
    {
        var transform = new TranslateTransform { Y = 24 };
        fe.RenderTransform = transform;
        fe.Opacity = 0;
        var story = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, BeginTime = TimeSpan.FromMilliseconds(220), Duration = TimeSpan.FromMilliseconds(420) };
        Storyboard.SetTarget(fade, fe);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var rise = new DoubleAnimation
        {
            From = 24,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(220),
            Duration = TimeSpan.FromMilliseconds(620),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
        };
        Storyboard.SetTarget(rise, transform);
        Storyboard.SetTargetProperty(rise, "Y");
        story.Children.Add(fade);
        story.Children.Add(rise);
        story.Completed += (_, _) => fe.Opacity = 1;
        story.Begin();
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

    private static Color ScrimColor(FrameworkElement fe) =>
        Hangar.Helpers.ThemeRes.Get<object>(fe, "HeroScrimBrush") is GradientBrush g && g.GradientStops.Count > 0
            ? g.GradientStops[g.GradientStops.Count - 1].Color
            : Microsoft.UI.Colors.Black;

    private static string ColorName(string key) => key switch
    {
        "AttentionBrush" => "amber",
        "AccentCyanBrush" => "cyan",
        "CriticalBrush" => "red",
        "HealthyBrush" => "green",
        _ => "white",
    };

    private static void HangarCursor(FrameworkElement fe, Microsoft.UI.Input.InputSystemCursorShape shape) =>
        Hologram.CursorHelper.Set(fe, shape);
}
