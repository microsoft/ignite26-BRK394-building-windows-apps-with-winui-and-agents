using Hangar.Models;
using Hangar.Reactor;
using Hangar.Reactor.Pages;
using Hangar.Services;
using Hangar.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Windows.System;
using Windows.UI.Core;
using static Hangar.Reactor.Kit;
using static Microsoft.UI.Reactor.Factories;

#if DEBUG
DebugLog.Install();
#endif
ReactorApp.Run<App>("HANGAR · Contoso Aero", 1707, 1067);

#if DEBUG
static class DebugLog
{
    static readonly string Path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "debug.log");
    static readonly object Gate = new();

    public static void Install()
    {
        try { System.IO.File.Delete(Path); } catch { }
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => Write("first-chance", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("UNHANDLED", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("UNOBSERVED", e.Exception);
    }

    public static void Write(string kind, Exception? ex)
    {
        try
        {
            lock (Gate)
                System.IO.File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {kind} {ex?.GetType().FullName}: {ex?.Message}\n{(kind == "first-chance" ? new System.Diagnostics.StackTrace(1, false).ToString() : ex?.ToString())}\n");
        }
        catch { }
    }
}
#endif

class App : Component
{
    static bool _resourcesMerged;

    // Same layout as the WinUI app's AutoSuggestBox.ItemTemplate (glyph · mono title · detail).
    static readonly Lazy<Microsoft.UI.Xaml.DataTemplate> SuggestionTemplate = new(() =>
        (Microsoft.UI.Xaml.DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Padding="0,6" ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto" />
                  <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <FontIcon VerticalAlignment="Center" FontSize="16" Foreground="{ThemeResource AccentCyanBrush}" Glyph="{Binding Glyph}" />
                <StackPanel Grid.Column="1">
                  <TextBlock Style="{StaticResource MonoTextStyle}" Text="{Binding Title}" />
                  <TextBlock FontSize="12" Foreground="{ThemeResource SecondaryTextBrush}" Text="{Binding Detail}" TextTrimming="CharacterEllipsis" MaxLines="1" />
                </StackPanel>
              </Grid>
            </DataTemplate>
            """));
#if DEBUG
    static bool _autoCaptureStarted;
#endif

    static void EnsureResources()
    {
        if (_resourcesMerged) return;
        _resourcesMerged = true;
#if DEBUG
        Application.Current.UnhandledException += (_, e) => DebugLog.Write("XAML-UNHANDLED", e.Exception);
#endif
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d is Microsoft.UI.Xaml.Controls.XamlControlsResources))
            merged.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
        merged.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Colors.xaml") });
        merged.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Styles.xaml") });
        merged.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/NavigationView.xaml") });
    }

    // NavigationViewItemData has no slots for AutomationId, tooltip or InfoBadge, so the
    // realized NavigationViewItems are decorated here; the Style comes from Themes/NavigationView.xaml.
    static void ConfigureRail(Microsoft.UI.Xaml.Controls.NavigationView nv, int openCount)
    {
        if (nv.Style is null && Application.Current.Resources.TryGetValue("MainNavigationViewStyle", out var style))
            nv.Style = (Style)style;
        nv.IsSettingsVisible = false;
        foreach (var item in nv.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>())
        {
            var label = item.Content as string ?? string.Empty;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, $"Nav{item.Tag}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label);
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(item, label);
            if (item.Tag as string != nameof(PageKind.WorkOrders)) continue;
            if (item.InfoBadge is null)
            {
                item.InfoBadge = new Microsoft.UI.Xaml.Controls.InfoBadge();
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item.InfoBadge, "WorkOrderBadge");
            }
            item.InfoBadge.Value = openCount;
            item.InfoBadge.Visibility = openCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    static bool IsDown(VirtualKey k) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);

    public override Element Render()
    {
        EnsureResources();

        var (theme, setTheme) = UseState(ElementTheme.Dark);
        var themeRef = UseRef(theme);
        themeRef.Current = theme;
        var (_, bump) = UseState(0);
        var bumpRef = UseRef(0);
        var (route, setRoute) = UseState(new Route(PageKind.Fleet, null, 0));
        var routeRef = UseRef(route);
        routeRef.Current = route;
        var (resetVisible, setResetVisible) = UseState(false);
        var searchRef = UseRef<Microsoft.UI.Xaml.Controls.AutoSuggestBox?>(null);
        var chosenRef = UseRef<SearchSuggestion?>(null);
        var suggestionsRef = UseRef<(IReadOnlyList<SearchSuggestion>? Source, List<SearchSuggestion> Items)>((null, []));
        var vmRef = UseRef<ShellViewModel?>(null);
        vmRef.Current ??= new ShellViewModel(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        var vm = vmRef.Current;

        void Navigate(PageKind page, object? arg)
        {
            setRoute(new Route(page, arg, routeRef.Current.Nonce + 1));
        }

        UseEffect(() =>
        {
            void Bump() => bump(++bumpRef.Current);
            EventHandler onData = (_, _) => Bump();
            System.ComponentModel.PropertyChangedEventHandler onVm = (_, _) => Bump();
            FleetDataService.Instance.HeroStatusChanged += onData;
            FleetDataService.Instance.Reset += onData;
            vm.PropertyChanged += onVm;
            return () =>
            {
                FleetDataService.Instance.HeroStatusChanged -= onData;
                FleetDataService.Instance.Reset -= onData;
                vm.PropertyChanged -= onVm;
            };
        }, []);

        var isDark = theme != ElementTheme.Light;

        void ResetDemo()
        {
            FleetDataService.Instance.ResetDemo();
            Navigate(PageKind.Fleet, null);
            setResetVisible(true);
            var q = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _ = Task.Delay(2500).ContinueWith(_ => q.TryEnqueue(() => setResetVisible(false)));
        }

        // ── Search: built-in AutoSuggestBox ─────────────────────────
        void ClearSearch()
        {
            chosenRef.Current = null;
            vm.UpdateSuggestions(string.Empty);
            if (searchRef.Current is { } box) box.Text = string.Empty;
        }

        void Submit(SearchSuggestion? pick, string text)
        {
            pick ??= vm.Suggestions.FirstOrDefault();
            switch (pick?.Target)
            {
                case Aircraft ac:
                    Navigate(PageKind.Aircraft, new AircraftNavigationArgs(ac.Tail));
                    break;
                case WorkOrder wo:
                    Navigate(PageKind.WorkOrders, new WorkOrdersNavigationArgs(wo.NumberText));
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(text))
                        Navigate(PageKind.WorkOrders, new WorkOrdersNavigationArgs(text.Trim()));
                    break;
            }
            ClearSearch();
        }

        // The ASB's ItemsSource needs an IList; only swap it when the VM publishes a new list
        // so unrelated re-renders (the clock ticks every second) keep keyboard selection intact.
        if (!ReferenceEquals(suggestionsRef.Current.Source, vm.Suggestions))
            suggestionsRef.Current = (vm.Suggestions, vm.Suggestions.ToList());
        var suggestionItems = suggestionsRef.Current.Items;

        var search = AutoSuggestBox(
                onTextChanged: text =>
                {
                    chosenRef.Current = null;
                    vm.UpdateSuggestions(text);
                },
                onQuerySubmitted: text => Submit(chosenRef.Current, text))
            .PlaceholderText("Search tail, part, or work order")
            .QueryIcon(new SymbolIconData("Find"))
            .SuggestionChosen(title => chosenRef.Current = vm.Suggestions.FirstOrDefault(s => s.Title == title))
            .Set(asb =>
            {
                asb.UpdateTextOnSelect = false;
                asb.ItemTemplate ??= SuggestionTemplate.Value;
                if (!ReferenceEquals(asb.ItemsSource, suggestionItems)) asb.ItemsSource = suggestionItems;
            })
            .AutomationId("GlobalSearchBox").AutomationName("Search tail, part, or work order")
            .OnMount(fe => searchRef.Current = (Microsoft.UI.Xaml.Controls.AutoSuggestBox)fe)
            .MinWidth(320).MaxWidth(520).HAlign(HorizontalAlignment.Stretch).VAlign(VerticalAlignment.Center);

        // ── Title bar ───────────────────────────────────────────────
        Element chip = vm.IsReleased
            ? Border(HStack(8,
                    Glyph("\uE73E", 12).Foreground(B("HealthyBrush")),
                    Mono(vm.FlightLabel, 12).SemiBold().Foreground(B("HealthyBrush")),
                    TextBlock("Released to service").FontSize(12).SemiBold().Foreground(Primary),
                    Mono(vm.Countdown, 12).Foreground(Secondary)))
                .Background(B("HealthySoftBrush")).BorderBrush(B("HealthyBrush"))
                .BorderThickness(1).CornerRadius(12).Padding(10, 3, 10, 4)
            : Border(HStack(8,
                    Dot(B("AttentionBrush")),
                    Mono(vm.FlightLabel, 12).SemiBold().Foreground(B("AttentionBrush")),
                    Mono(vm.Countdown, 12).SemiBold().Foreground(Primary),
                    TextBlock("to departure").FontSize(12).Foreground(Secondary)))
                .Background(B("AttentionSoftBrush")).BorderBrush(B("AttentionBrush"))
                .BorderThickness(1).CornerRadius(12).Padding(10, 3, 10, 4);

        var chipButton = Button(chip, () => Navigate(PageKind.Aircraft, new AircraftNavigationArgs(FleetDataService.HeroTail)))
            .Background(B("SubtleFillColorTransparentBrush"))
            .BorderThickness(0).Padding(0)
            .ToolTip("Open CA 2207").AutomationId("DepartureCountdownChip").AutomationName("Open CA 2207 departure status");

        var rightHeader = HStack(12,
                Mono(vm.ClockText, 13).Foreground(Tertiary).VAlign(VerticalAlignment.Center),
                chipButton.VAlign(VerticalAlignment.Center),
                PersonPicture().DisplayName("Maya Okafor").Width(32).Height(32).ToolTip("Maya Okafor · Shift lead")
                    .AutomationId("ShiftLeadPicture").VAlign(VerticalAlignment.Center))
            .Margin(0, 0, 8, 0).VAlign(VerticalAlignment.Center);

        var titleBar = TitleBar("HANGAR")
            .Subtitle("Contoso Aero · Night shift")
            .Icon(new FontIconData("\uE709"))
            .Content(search)
            .RightHeader(rightHeader)
            .Tall()
            .Flex(shrink: 0);

        // ── Navigation: built-in NavigationView, restyled as the Store-style rail ──
        static NavigationViewItemData Nav(string label, string glyph, PageKind page) =>
            NavItem(label, tag: page.ToString()) with { IconElement = new FontIconData(glyph) };
        var openCount = FleetDataService.Instance.OpenWorkOrderCount;

        var props = new PageProps(route.Arg, isDark, Navigate);
        Element page = route.Page switch
        {
            PageKind.Aircraft => Component<AircraftPage, PageProps>(props),
            PageKind.WorkOrders => Component<WorkOrdersPage, PageProps>(props),
            PageKind.Inspections => Component<InspectionsPage, PageProps>(props),
            _ => Component<FleetPage, PageProps>(props),
        };
        page = page.WithKey($"{route.Page}-{route.Nonce}");

        var resetBar = InfoBar("Demo reset", "CA 2207 is back to its pre-inspection state.")
            .Success()
            .Set(b => b.IsOpen = resetVisible)
            .AutomationId("ResetInfoBar")
            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Top).Margin(0, 16, 0, 0)
            .MaxWidth(480);

        var nav = (NavigationView(
                [
                    Nav("Fleet", "\uE709", PageKind.Fleet),
                    Nav("Aircraft", "\uF158", PageKind.Aircraft),
                    Nav("Work Orders", "\uE9F9", PageKind.WorkOrders),
                    Nav("Inspections", "\uEDC6", PageKind.Inspections),
                ],
                Grid([GridSize.Star(1)], [GridSize.Star(1)], [page, resetBar])) with { SelectedTag = route.Page.ToString() })
            .PaneDisplayMode(Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Left)
            .PaneVisible(true)
            .PaneToggleButtonVisible(false)
            .BackButtonVisible(false)
            .TitleBarAutoPadding(false)
            .ItemInvoked(tag =>
            {
                if (Enum.TryParse<PageKind>(tag, out var target)) Navigate(target, null);
            })
            .Set(nv => ConfigureRail(nv, openCount))
            .AutomationId("MainNavigation")
            .Flex(grow: 1, basis: 0);

        var root = FlexColumn(titleBar, nav).Background(B("CanvasTintBrush"));
#if DEBUG
        root = root.OnMount(fe => AutoCapture(fe, setTheme, Navigate));
#endif

        return root
            .Backdrop(BackdropKind.MicaAlt)
            .RequestedTheme(theme)
            .OnPreviewKeyDown((s, e) =>
            {
                var ctrl = IsDown(VirtualKey.Control);
                var shift = IsDown(VirtualKey.Shift);
                if (ctrl && shift && e.Key == VirtualKey.L)
                {
                    setTheme(themeRef.Current == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light);
                    e.Handled = true;
                }
                else if (ctrl && shift && e.Key == VirtualKey.D)
                {
                    ResetDemo();
                    e.Handled = true;
                }
                else if (ctrl && !shift && e.Key == VirtualKey.K)
                {
                    searchRef.Current?.Focus(FocusState.Keyboard);
                    e.Handled = true;
                }
#if DEBUG
                else if (ctrl && shift && e.Key == VirtualKey.P && s is UIElement el && el.XamlRoot?.Content is UIElement r)
                {
                    var dark = themeRef.Current != ElementTheme.Light;
                    var name = $"{routeRef.Current.Page}-{(dark ? "dark" : "light")}.png".ToLowerInvariant();
                    _ = DebugCapture.SaveAsync(r, name, dark);
                    e.Handled = true;
                }
#endif
            });
    }

#if DEBUG
    static async void AutoCapture(FrameworkElement fe, Action<ElementTheme> setTheme, Action<PageKind, object?> navigate)
    {
        if (_autoCaptureStarted) return;
        _autoCaptureStarted = true;
        // LocalState survives `winapp run` redeploys, unlike the AppX folder.
        var flag = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "autocapture.flag");
        if (!File.Exists(flag)) flag = Path.Combine(AppContext.BaseDirectory, "autocapture.flag");
        if (!File.Exists(flag)) return;
        try { File.Delete(flag); } catch { }

        await Task.Delay(2500);
        (string name, PageKind page, bool dark)[] shots =
        [
            ("fleet", PageKind.Fleet, true),
            ("aircraft", PageKind.Aircraft, true),
            ("workorders", PageKind.WorkOrders, true),
            ("inspections", PageKind.Inspections, true),
            ("fleet", PageKind.Fleet, false),
            ("workorders", PageKind.WorkOrders, false),
        ];
        foreach (var (name, page, dark) in shots)
        {
            setTheme(dark ? ElementTheme.Dark : ElementTheme.Light);
            navigate(page, null);
            await Task.Delay(2200);
            if (fe.XamlRoot?.Content is UIElement root)
                await DebugCapture.SaveAsync(root, $"{name}-{(dark ? "dark" : "light")}.png", dark);
        }
        setTheme(ElementTheme.Dark);
        navigate(PageKind.Fleet, null);
    }
#endif
}
