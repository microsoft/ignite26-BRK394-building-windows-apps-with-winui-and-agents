using System.Numerics;
using System.Runtime.InteropServices;
using Hangar.Models;
using Hangar.Services;
using Hangar.ViewModels;
using Hangar.Views;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace Hangar;

public sealed partial class MainWindow : Window
{
    private readonly FleetDataService _data = FleetDataService.Instance;
    private DispatcherQueueTimerHolder? _infoBarTimer;

    public MainWindow()
    {
        ViewModel = new ShellViewModel(DispatcherQueue);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SizeForStage();

        NavigationService.Initialize(RootFrame);
        NavigationService.Navigated += OnNavigated;

        _data.HeroStatusChanged += (_, _) => UpdateBadge();
        _data.Reset += (_, _) => UpdateBadge();
        UpdateBadge();

        RootGrid.Loaded += (_, _) => StartChipBreathing();
        var (startPage, startTheme) = ReadDebugStart();
        if (startTheme is { } theme)
        {
            RootGrid.RequestedTheme = theme;
        }

        NavigationService.Navigate(startPage, null, new SuppressNavigationTransitionInfo());
        NavView.Loaded += (_, _) => OnNavigated(null, NavigationService.CurrentPage ?? typeof(FleetPage));
    }

    public ShellViewModel ViewModel { get; }

    private void SizeForStage()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;

        // Target: 2560x1600 @150% => 1707x1067 effective pixels.
        var width = Math.Min((int)(1707 * scale), work.Width);
        var height = Math.Min((int)(1067 * scale), work.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + ((work.Height - height) / 2),
            width,
            height));
    }

    private void StartChipBreathing()
    {
        var visual = ElementCompositionPreview.GetElementVisual(ChipDot);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(4, 4, 0);

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 1f);
        opacity.InsertKeyFrame(0.5f, 0.35f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f)));
        opacity.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f)));
        opacity.Duration = TimeSpan.FromMilliseconds(2400);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.Opacity), opacity);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, Vector3.One);
        scale.InsertKeyFrame(0.5f, new Vector3(1.35f, 1.35f, 1f));
        scale.InsertKeyFrame(1f, Vector3.One);
        scale.Duration = TimeSpan.FromMilliseconds(2400);
        scale.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.Scale), scale);
    }

    private void UpdateBadge()
    {
        ToolTipService.SetToolTip(NavWorkOrdersItem, $"Work orders · {_data.OpenWorkOrderCount} open");
        WorkOrderBadge.Visibility = Visibility.Visible;
    }

    private void OnNavigated(object? sender, Type page)
    {
        var tag = page.Name.Replace("Page", string.Empty, StringComparison.Ordinal);
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (!ReferenceEquals(NavView.SelectedItem, item))
        {
            NavView.SelectedItem = item;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        var page = PageForTag(tag);
        if (page is not null && NavigationService.CurrentPage != page)
        {
            NavigationService.Navigate(page);
        }
    }

    private static Type? PageForTag(string tag) => tag switch
    {
        "Fleet" => typeof(FleetPage),
        "Aircraft" => typeof(AircraftPage),
        "WorkOrders" => typeof(WorkOrdersPage),
        "Inspections" => typeof(InspectionsPage),
        _ => null,
    };

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.UpdateSuggestions(sender.Text);
        }
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var suggestion = args.ChosenSuggestion as SearchSuggestion ?? ViewModel.Suggestions.FirstOrDefault();
        switch (suggestion?.Target)
        {
            case Aircraft aircraft:
                NavigationService.Navigate(typeof(AircraftPage), new AircraftNavigationArgs(aircraft.Tail));
                break;
            case WorkOrder order:
                NavigationService.Navigate(typeof(WorkOrdersPage), new WorkOrdersNavigationArgs(order.NumberText));
                break;
            default:
                if (!string.IsNullOrWhiteSpace(args.QueryText))
                {
                    NavigationService.Navigate(typeof(WorkOrdersPage), new WorkOrdersNavigationArgs(args.QueryText.Trim()));
                }

                break;
        }

        sender.Text = string.Empty;
        ViewModel.UpdateSuggestions(string.Empty);
    }

    private void OnCountdownChipClick(object sender, RoutedEventArgs e) =>
        NavigationService.Navigate(typeof(AircraftPage), new AircraftNavigationArgs(FleetDataService.HeroTail));

    private void OnToggleThemeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var light = RootGrid.ActualTheme == ElementTheme.Dark;
        RootGrid.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
    }

    private void OnFocusSearchInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void OnDemoResetInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _data.ResetDemo();
        NavigationService.Navigate(typeof(FleetPage), "reset", new DrillInNavigationTransitionInfo());
        RootFrame.BackStack.Clear();

        ResetInfoBar.IsOpen = true;
        _infoBarTimer?.Stop();
        _infoBarTimer = new DispatcherQueueTimerHolder(DispatcherQueue, TimeSpan.FromSeconds(2.5), () => ResetInfoBar.IsOpen = false);
    }

    /// <summary>
    /// Debug-only rehearsal hook: LocalState\startpage.txt containing e.g. "WorkOrders" or "Aircraft light"
    /// opens that page (and theme) at launch so every screen can be captured deterministically.
    /// </summary>
    private static (Type Page, ElementTheme? Theme) ReadDebugStart()
    {
#if DEBUG
        try
        {
            var file = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "startpage.txt");
            if (File.Exists(file))
            {
                var parts = File.ReadAllText(file).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var page = parts.Length > 0 ? PageForTag(parts[0]) : null;
                ElementTheme? theme = parts.Length > 1 && parts[1].Equals("light", StringComparison.OrdinalIgnoreCase) ? ElementTheme.Light : null;
                InspectionsPage.DebugAutoSign = parts.Contains("sign", StringComparer.OrdinalIgnoreCase);
                return (page ?? typeof(FleetPage), theme);
            }
        }
        catch (IOException)
        {
        }
#endif
        return (typeof(FleetPage), null);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private sealed class DispatcherQueueTimerHolder
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

        public DispatcherQueueTimerHolder(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan delay, Action action)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = delay;
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => action();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();
    }
}
