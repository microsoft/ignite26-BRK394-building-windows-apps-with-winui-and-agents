using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls.Tabular;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Hangar.Views;

public sealed partial class WorkOrdersPage : Page
{
    private static readonly DateTime YearAgo = FleetDataService.Now.AddMonths(-12);

    private TableViewSource? _source;
    private string _query = string.Empty;
    private bool _isGrouped = true;
    private bool _suppressFilterText;

    public WorkOrdersPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FleetDataService Data => FleetDataService.Instance;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        EnsureSource();

        switch (e.Parameter)
        {
            case "reset":
                ResetView();
                break;
            case WorkOrdersNavigationArgs args when args.Query is not null:
                ClearChips();
                SetQuery(args.Query);
                ApplyFilter();
                break;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Data.Reset += OnDataReset;
        Data.HeroStatusChanged += OnHeroStatusChanged;
        EnsureSource();
        RefreshSummary();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Data.Reset -= OnDataReset;
        Data.HeroStatusChanged -= OnHeroStatusChanged;
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        _source = TableViewSource.From(Data.WorkOrders);
        Table.ItemsSource = _source;
        ApplyGrouping();
        ApplyFilter();
    }

    private void OnDataReset(object? sender, EventArgs e) => ResetView();

    private void OnHeroStatusChanged(object? sender, EventArgs e)
    {
        ApplyFilter();
        UpdateDetail(Table.SelectedItem as WorkOrder);
    }

    private void ResetView()
    {
        ClearChips();
        SetQuery(string.Empty);
        _isGrouped = true;
        GroupToggle.IsChecked = true;
        _source?.ClearSort();
        Table.ClearSort();
        Table.DeselectAll();
        ApplyGrouping();
        ApplyFilter();
        UpdateDetail(null);
    }

    private void SetQuery(string query)
    {
        _suppressFilterText = true;
        FilterBox.Text = query;
        _suppressFilterText = false;
        _query = query.Trim();
    }

    private void ClearChips()
    {
        foreach (var chip in Chips)
        {
            chip.IsChecked = false;
        }
    }

    private IEnumerable<ToggleButton> Chips =>
        [FanBladeChip, HeroChip, RecentChip, OpenChip, InProgressChip, DeferredChip, ClosedChip];

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilterText)
        {
            return;
        }

        _query = FilterBox.Text.Trim();
        ApplyFilter();
    }

    private void OnChipClick(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        ClearChips();
        SetQuery(string.Empty);
        ApplyFilter();
    }

    private void OnGroupToggleClick(object sender, RoutedEventArgs e)
    {
        _isGrouped = GroupToggle.IsChecked == true;
        ApplyGrouping();
        RefreshSummary();
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e) => Table.CollapseAllGroups();

    private void OnExpandClick(object sender, RoutedEventArgs e) => Table.ExpandAllGroups();

    private void ApplyGrouping()
    {
        if (_source is null)
        {
            return;
        }

        if (_isGrouped)
        {
            _source.GroupBy(item => item is WorkOrder w ? w.Component : string.Empty);
        }
        else
        {
            _source.ClearGroupBy();
        }

        CollapseButton.IsEnabled = _isGrouped;
        ExpandButton.IsEnabled = _isGrouped;
    }

    private void ApplyFilter()
    {
        if (_source is null)
        {
            return;
        }

        var statuses = new HashSet<WorkOrderStatus>();
        if (OpenChip.IsChecked == true) statuses.Add(WorkOrderStatus.Open);
        if (InProgressChip.IsChecked == true) statuses.Add(WorkOrderStatus.InProgress);
        if (DeferredChip.IsChecked == true) statuses.Add(WorkOrderStatus.Deferred);
        if (ClosedChip.IsChecked == true) statuses.Add(WorkOrderStatus.Closed);

        var fanOnly = FanBladeChip.IsChecked == true;
        var heroOnly = HeroChip.IsChecked == true;
        var recentOnly = RecentChip.IsChecked == true;
        var query = _query;

        var isFiltered = statuses.Count > 0 || fanOnly || heroOnly || recentOnly || query.Length > 0;
        ClearFiltersButton.Visibility = isFiltered ? Visibility.Visible : Visibility.Collapsed;

        bool Matches(WorkOrder w) =>
            (!fanOnly || w.Component == "Fan blade")
            && (!heroOnly || w.Tail == FleetDataService.HeroTail)
            && (!recentOnly || w.Opened >= YearAgo)
            && (statuses.Count == 0 || statuses.Contains(w.Status))
            && (query.Length == 0 || MatchesQuery(w, query));

        WorkOrderFormat.UpdateGroupStats(Data.WorkOrders.Where(Matches));

        if (isFiltered)
        {
            _source.Filter(item => item is WorkOrder w && Matches(w));
        }
        else
        {
            _source.ClearFilter();
        }

        // Group header stats are computed from the filter, so re-apply grouping to refresh them.
        ApplyGrouping();
        RefreshSummary();
    }

    private static bool MatchesQuery(WorkOrder w, string query) =>
        w.Tail.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Component.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.NumberText.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Finding.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.PartNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Technician.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Ata.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void RefreshSummary()
    {
        var visible = Data.WorkOrders.Where(IsVisible).ToList();
        VisibleCountText.Text = visible.Count.ToString("N0");
        OpenCountText.Text = visible.Count(w => w.Status != WorkOrderStatus.Closed).ToString("N0");
        HoursText.Text = visible.Sum(w => w.Hours).ToString("N0");
        EyebrowText.Text = $"MAINTENANCE RECORDS · {Data.WorkOrders.Count:N0} WORK ORDERS";
    }

    private bool IsVisible(WorkOrder w)
    {
        if (ClearFiltersButton.Visibility == Visibility.Collapsed)
        {
            return true;
        }

        var statuses = new List<WorkOrderStatus>();
        if (OpenChip.IsChecked == true) statuses.Add(WorkOrderStatus.Open);
        if (InProgressChip.IsChecked == true) statuses.Add(WorkOrderStatus.InProgress);
        if (DeferredChip.IsChecked == true) statuses.Add(WorkOrderStatus.Deferred);
        if (ClosedChip.IsChecked == true) statuses.Add(WorkOrderStatus.Closed);

        return (FanBladeChip.IsChecked != true || w.Component == "Fan blade")
            && (HeroChip.IsChecked != true || w.Tail == FleetDataService.HeroTail)
            && (RecentChip.IsChecked != true || w.Opened >= YearAgo)
            && (statuses.Count == 0 || statuses.Contains(w.Status))
            && (_query.Length == 0 || MatchesQuery(w, _query));
    }

    private void OnSelectionChanged(object sender, object e) => UpdateDetail(Table.SelectedItem as WorkOrder);

    private void UpdateDetail(WorkOrder? wo)
    {
        ValidationText.Visibility = Visibility.Collapsed;
        if (wo is null)
        {
            DetailTitle.Text = "Select a work order";
            DetailSubtitle.Text = "Double-click Hours to edit inline. Values must be greater than 0.";
            DetailIcon.Glyph = "\uE8FD";
            ShowOnAircraftButton.IsEnabled = false;
            return;
        }

        DetailTitle.Text = $"{wo.NumberText} · {wo.Tail} · {wo.Component} — {wo.Finding}";
        var signed = string.IsNullOrEmpty(wo.SignedBy) ? string.Empty : $" · signed by {wo.SignedBy}";
        DetailSubtitle.Text = $"{wo.StatusText} · {wo.Technician} · opened {wo.OpenedText} · {wo.Hours:0.0} h · ATA {wo.Ata} · P/N {wo.PartNumber}{signed}";
        DetailIcon.Glyph = wo.IsMappable ? "\uE709" : "\uE8FD";
        ShowOnAircraftButton.IsEnabled = wo.IsMappable;
    }

    private void OnCellEditEnding(object sender, TableViewCellEditEndingEventArgs e)
    {
        if (e.Item is WorkOrder wo && wo.HasErrors)
        {
            var message = wo.GetErrors(nameof(WorkOrder.Hours)).Cast<object>().FirstOrDefault()?.ToString();
            ValidationText.Text = message ?? "Hours must be greater than 0.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        ValidationText.Visibility = Visibility.Collapsed;
        RefreshSummary();
    }

    private void OnShowOnAircraftClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkOrder wo })
        {
            ShowOnAircraft(wo);
        }
    }

    private void OnShowSelectedOnAircraftClick(object sender, RoutedEventArgs e)
    {
        if (Table.SelectedItem is WorkOrder wo)
        {
            ShowOnAircraft(wo);
        }
    }

    private static void ShowOnAircraft(WorkOrder wo)
    {
        if (!wo.IsMappable)
        {
            return;
        }

        NavigationService.Navigate(
            typeof(AircraftPage),
            new AircraftNavigationArgs(wo.Tail, wo.MappedPart),
            new DrillInNavigationTransitionInfo());
    }
}
