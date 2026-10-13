using System.Collections.Immutable;
using System.Globalization;
using Hangar.Helpers;
using Hangar.Models;
using Hangar.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Xaml;
using Windows.System;
using static Hangar.Reactor.Kit;
using static Microsoft.UI.Reactor.Factories;
using Adv = Microsoft.UI.Reactor.Advanced.Factories;
using XamlControls = Microsoft.UI.Xaml.Controls;

namespace Hangar.Reactor.Pages;

/// <summary>
/// Work Orders — the fleet's paper trail, on Reactor's built-in <c>DataGrid&lt;T&gt;</c> (Reactor.Advanced):
/// virtualized rows, header sorting, column resizing, keyboard navigation, single selection and in-cell
/// Hours editing with validation. DataGrid has no grouping, so component groups are emitted as header
/// rows by the data source and rendered full-width through the cell template.
/// </summary>
public sealed class WorkOrdersPage : HangarComponent<PageProps>
{
    private const double RowH = 44;
    private const double SelectColumn = 40; // DataGrid adds a 40px ✓ column in Single selection mode.
    private static readonly DateTime YearAgo = FleetDataService.Now.AddMonths(-12);
    private static FleetDataService Data => FleetDataService.Instance;

    private sealed record Filters(bool Fan = false, bool Hero = false, bool Recent = false,
        bool Open = false, bool InProgress = false, bool Deferred = false, bool Closed = false)
    {
        public bool Any => Fan || Hero || Recent || Open || InProgress || Deferred || Closed;
    }

    private sealed record GroupInfo(string Name, int Count, int Open, double Hours, double Bar, bool Collapsed);

    /// <summary>One grid row: either a work order or a synthetic component-group header.</summary>
    private sealed class WorkOrderRow
    {
        public WorkOrderRow(WorkOrder wo) { Wo = wo; Key = "w:" + wo.Number; }
        public WorkOrderRow(GroupInfo group) { Group = group; Key = "g:" + group.Name; }

        public string Key { get; }
        public WorkOrder? Wo { get; }
        public GroupInfo? Group { get; }

        public string Number => Wo?.NumberText ?? Group!.Name;
        public string Tail => Wo?.Tail ?? string.Empty;
        public string Component => Wo?.Component ?? Group!.Name;
        public string Ata => Wo?.Ata ?? string.Empty;
        public string Finding => Wo?.Finding ?? string.Empty;
        public string Technician => Wo?.Technician ?? string.Empty;
        public string Status => Wo?.StatusText ?? string.Empty;
        public string PartNumber => Wo?.PartNumber ?? string.Empty;
        public DateTime Opened => Wo?.Opened ?? default;
        public double Hours => Wo?.Hours ?? Group!.Hours;
        public string SignOff => Wo?.SignedBy ?? string.Empty;
        public string Map => string.Empty;
    }

    /// <summary>
    /// Stable observable source for the grid. The page pushes the filtered/grouped structure in; the grid's
    /// header sort arrives as <see cref="DataRequest.Sort"/> (ServerSort) and is applied within each group.
    /// </summary>
    private sealed class WorkOrderSource : IObservableDataSource<WorkOrderRow>
    {
        private IReadOnlyList<(WorkOrderRow? Header, IReadOnlyList<WorkOrderRow> Items)> _groups = [];
        private string? _signature;

        public event EventHandler? DataChanged;

        public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerSort | DataSourceCapabilities.ServerCount;

        public void Update(IReadOnlyList<(WorkOrderRow? Header, IReadOnlyList<WorkOrderRow> Items)> groups, string signature)
        {
            if (signature == _signature) return;
            var first = _signature is null;
            _groups = groups;
            _signature = signature;
            if (!first)
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => DataChanged?.Invoke(this, EventArgs.Empty));
        }

        public Task<DataPage<WorkOrderRow>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
        {
            var sort = request.Sort?.FirstOrDefault();
            var rows = new List<WorkOrderRow>();
            foreach (var (header, items) in _groups)
            {
                if (header is not null) rows.Add(header);
                rows.AddRange(sort is null
                    ? items
                    : sort.Direction == SortDirection.Ascending
                        ? items.OrderBy(r => SortValue(r.Wo!, sort.Field))
                        : items.OrderByDescending(r => SortValue(r.Wo!, sort.Field)));
            }

            var offset = int.TryParse(request.ContinuationToken, out var o) ? o : 0;
            var page = rows.Skip(offset).Take(request.PageSize).ToList();
            var next = offset + page.Count < rows.Count ? (offset + page.Count).ToString(CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new DataPage<WorkOrderRow>(page, next, rows.Count));
        }

        public RowKey GetRowKey(WorkOrderRow item) => new(item.Key);
    }

    private sealed class HoursValidator : IValidator
    {
        public const string Message = "Hours must be greater than 0";

        public ValidationMessage? Validate(object? value, string field) =>
            TryParseHours(value, out _) ? null : new ValidationMessage(field, Message);
    }

    /// <summary>Callbacks the (once-built) column definitions reach back into the current render through.</summary>
    private sealed class PageCallbacks
    {
        public Action<string?> SetValidation = _ => { };
    }

    public override Element Render()
    {
        var p = Props;
        UseDataVersion();
        var (query, setQuery) = UseState(p.Arg is WorkOrdersNavigationArgs args ? args.Query : string.Empty);
        var (filters, setFilters) = UseState(new Filters());
        var (grouped, setGrouped) = UseState(true);
        var (collapsed, setCollapsed) = UseState(ImmutableHashSet<string>.Empty);
        var (selected, setSelected) = UseState(0);
        var (validation, setValidation) = UseState<string?>(null);
        var (edits, setEdits) = UseState(0);
        var (resetVersion, setResetVersion) = UseState(0);
        var (tableWidth, setTableWidth) = UseState(0.0);
        var editsRef = UseRef(0);
        var resetRef = UseRef(0);
        var findingResize = UseRef<Action<double>?>(null);
        var source = UseRef<WorkOrderSource?>(null);
        source.Current ??= new WorkOrderSource();
        var callbacks = UseRef<PageCallbacks?>(null);
        callbacks.Current ??= new PageCallbacks();
        callbacks.Current.SetValidation = setValidation;
        var columns = UseRef<IReadOnlyList<FieldDescriptor>?>(null);
        columns.Current ??= BuildColumns(callbacks.Current);

        UseEffect(() =>
        {
            EventHandler onReset = (_, _) =>
            {
                setQuery(string.Empty);
                setFilters(new Filters());
                setGrouped(true);
                setCollapsed(ImmutableHashSet<string>.Empty);
                setSelected(0);
                setValidation(null);
                setResetVersion(++resetRef.Current); // remounts the grid: clears its sort + selection
            };
            Data.Reset += onReset;
            return () => Data.Reset -= onReset;
        }, Array.Empty<object>());

        // Finding is the flexible column: give it whatever the fixed columns leave over.
        UseEffect(() =>
        {
            if (tableWidth > 0)
            {
                var fixedWidth = columns.Current.Where(c => c.Name != "Finding").Sum(c => c.Width ?? 120);
                findingResize.Current?.Invoke(Math.Max(220, tableWidth - 2 - SelectColumn - fixedWidth - 12));
            }
            return () => { };
        }, new object[] { tableWidth, resetVersion });

        // ── Filter + group ──
        var q = query.Trim();
        var statuses = new HashSet<WorkOrderStatus>();
        if (filters.Open) statuses.Add(WorkOrderStatus.Open);
        if (filters.InProgress) statuses.Add(WorkOrderStatus.InProgress);
        if (filters.Deferred) statuses.Add(WorkOrderStatus.Deferred);
        if (filters.Closed) statuses.Add(WorkOrderStatus.Closed);

        bool Matches(WorkOrder w) =>
            (!filters.Fan || w.Component == "Fan blade")
            && (!filters.Hero || w.Tail == FleetDataService.HeroTail)
            && (!filters.Recent || w.Opened >= YearAgo)
            && (statuses.Count == 0 || statuses.Contains(w.Status))
            && (q.Length == 0 || MatchesQuery(w, q));

        var visible = Data.WorkOrders.Where(Matches).ToList();

        var groups = new List<(WorkOrderRow? Header, IReadOnlyList<WorkOrderRow> Items)>();
        var groupNames = new List<string>();
        if (grouped)
        {
            var byComponent = visible.GroupBy(w => w.Component).ToList();
            var maxHours = Math.Max(1, byComponent.Select(g => g.Sum(w => w.Hours)).DefaultIfEmpty(1).Max());
            foreach (var g in byComponent)
            {
                groupNames.Add(g.Key);
                var hours = g.Sum(w => w.Hours);
                var isCollapsed = collapsed.Contains(g.Key);
                var info = new GroupInfo(g.Key, g.Count(), g.Count(w => w.Status != WorkOrderStatus.Closed), hours,
                    Math.Max(4, 160 * hours / maxHours), isCollapsed);
                groups.Add((new WorkOrderRow(info), isCollapsed ? [] : g.Select(w => new WorkOrderRow(w)).ToList()));
            }
        }
        else if (visible.Count > 0)
        {
            groups.Add((null, visible.Select(w => new WorkOrderRow(w)).ToList()));
        }

        var signature = string.Join(",", groups.SelectMany(g => (g.Header is null ? g.Items : g.Items.Prepend(g.Header)).Select(r => r.Key)))
            + $"|{visible.Sum(w => w.Hours):0.0}|{visible.Count(w => w.SignedBy is not null)}|{edits}";
        source.Current.Update(groups, signature);

        var sel = selected == 0 ? null : Data.WorkOrders.FirstOrDefault(w => w.Number == selected);

        void ShowOnAircraft(WorkOrder wo)
        {
            if (wo.IsMappable) p.Navigate(PageKind.Aircraft, new AircraftNavigationArgs(wo.Tail, wo.MappedPart));
        }

        void ResetFilters()
        {
            setFilters(new Filters());
            setQuery(string.Empty);
        }

        // ── Header ──
        Element Kpi(string value, string label, string id, ThemeRef? brush = null) =>
            VStack(0,
                KpiValue(value, 40).Foreground(brush ?? Primary).HAlign(HorizontalAlignment.Right),
                Eyebrow(label, 12).HAlign(HorizontalAlignment.Right))
            .AutomationId(id).AutomationName($"{label}: {value}");

        var header = Grid([GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
        [
            VStack(4,
                Eyebrow($"MAINTENANCE RECORDS · {Data.WorkOrders.Count:N0} WORK ORDERS").AutomationId("WorkOrdersEyebrow"),
                Hero("Paper trail"),
                Body("Every finding, every fix, every signature. Group, filter and sort the whole fleet history in one view.", 16)
                    .Margin(0, 2, 0, 0)),
            HStack(32,
                Kpi(visible.Count.ToString("N0"), "SHOWING", "WorkOrdersShowing"),
                Kpi(visible.Count(w => w.Status != WorkOrderStatus.Closed).ToString("N0"), "OPEN", "WorkOrdersOpen", Cyan),
                Kpi(visible.Sum(w => w.Hours).ToString("N0"), "LABOR HOURS", "WorkOrdersHours"))
                .VAlign(VerticalAlignment.Bottom).Margin(24, 0, 0, 4).Grid(0, 1),
        ]);

        // ── Toolbar ──
        TextBlockElement ChipText(string text) => TextBlock(text).FontSize(13).VAlign(VerticalAlignment.Center);

        Element FilterChip(string id, Element content, string name, bool on, Func<Filters, Filters> toggle) =>
            Chip(content, on, () => setFilters(toggle(filters)))
                .AutomationId(id).AutomationName(on ? $"{name}, filter on" : $"{name}, filter off");

        var isFiltered = filters.Any || q.Length > 0;
        var chips = HStack(8,
            FilterChip("ChipFanBlade", HStack(8, Glyph("\uE945", 12), ChipText("Fan blade")), "Fan blade", filters.Fan, f => f with { Fan = !f.Fan }),
            FilterChip("ChipHeroTail", ChipText(FleetDataService.HeroTail).FontFamily(MonoFont).SemiBold(), FleetDataService.HeroTail, filters.Hero, f => f with { Hero = !f.Hero }),
            FilterChip("ChipLast12Months", ChipText("Last 12 months"), "Last 12 months", filters.Recent, f => f with { Recent = !f.Recent }),
            VDivider(20).Margin(4, 0),
            FilterChip("ChipOpen", ChipText("Open"), "Open", filters.Open, f => f with { Open = !f.Open }),
            FilterChip("ChipInProgress", ChipText("In progress"), "In progress", filters.InProgress, f => f with { InProgress = !f.InProgress }),
            FilterChip("ChipDeferred", ChipText("Deferred"), "Deferred", filters.Deferred, f => f with { Deferred = !f.Deferred }),
            FilterChip("ChipClosed", ChipText("Closed"), "Closed", filters.Closed, f => f with { Closed = !f.Closed }),
            isFiltered
                ? Button(HStack(8, Glyph("\uE894", 12), ChipText("Clear")), ResetFilters)
                    .Background(B("SubtleFillColorTransparentBrush")).BorderThickness(0).CornerRadius(16).Padding(12, 6)
                    .AutomationId("ClearFiltersButton").AutomationName("Clear filters")
                : Empty())
            .VAlign(VerticalAlignment.Center);

        ButtonElement RoundIcon(string glyph, string tip, string id, Action onClick) =>
            Button(Glyph(glyph, 12).HAlign(HorizontalAlignment.Center), onClick)
                .Width(36).Height(32).Padding(0).CornerRadius(16).IsEnabled(grouped)
                .ToolTip(tip).AutomationId(id).AutomationName(tip);

        var groupToggle = ToggleButton("Group by component", grouped, setGrouped)
            .Set(t =>
            {
                if (t.Content is not string label) return;
                var row = new XamlControls.StackPanel { Orientation = XamlControls.Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new XamlControls.FontIcon { Glyph = "\uF168", FontSize = 12 });
                row.Children.Add(new XamlControls.TextBlock { Text = label, FontSize = 13 });
                t.Content = row;
            })
            .CornerRadius(16).Padding(14, 6).VAlign(VerticalAlignment.Center)
            .AutomationId("GroupByComponentToggle");

        var toolbar = Grid([GridSize.Auto, GridSize.Star(1), GridSize.Auto], [GridSize.Auto],
        [
            TextBox(query, setQuery, "Filter tail, component, WO#, finding, part…")
                .Width(360).VAlign(VerticalAlignment.Center)
                .Set(t => t.CornerRadius = new CornerRadius(16))
                .AutomationId("WorkOrdersFilterBox").AutomationName("Filter work orders"),
            chips.Margin(16, 0, 16, 0).Grid(0, 1),
            HStack(8,
                groupToggle,
                RoundIcon("\uE70E", "Collapse all groups", "CollapseGroupsButton", () => setCollapsed(groupNames.ToImmutableHashSet())),
                RoundIcon("\uE70D", "Expand all groups", "ExpandGroupsButton", () => setCollapsed(ImmutableHashSet<string>.Empty)))
                .VAlign(VerticalAlignment.Center).Grid(0, 2),
        ]);

        // ── DataGrid templates ──
        Element HeaderView(HeaderContext h)
        {
            var col = h.Column;
            if (col.Name == "Finding") findingResize.Current = h.Resize;
            var title = col.DisplayName ?? col.Name;
            var label = HStack(6,
                TextBlock(title).FontSize(14).SemiBold().Foreground(Primary).VAlign(VerticalAlignment.Center),
                h.CurrentSort is { } dir ? Glyph(dir == SortDirection.Ascending ? "\uE70E" : "\uE70D", 10).Foreground(Cyan) : Empty());

            Element content = !col.Sortable
                ? label.Margin(4, 0, 0, 0)
                : Button(label, h.ToggleSort)
                    .Background(B("SubtleFillColorTransparentBrush")).BorderThickness(0).CornerRadius(4).Padding(6, 4)
                    .Margin(-2, 0, 0, 0).HAlign(HorizontalAlignment.Left)
                    .ToolTip(col.Description ?? $"Sort by {title}")
                    .AutomationId($"SortBy{col.Name}")
                    .AutomationName(h.CurrentSort is { } d
                        ? $"Sort by {title}, {(d == SortDirection.Ascending ? "ascending" : "descending")}"
                        : $"Sort by {title}");
            return Border(content.VAlign(VerticalAlignment.Center)).Height(RowH);
        }

        Element GroupHeader(GroupInfo g) =>
            Button(
                Grid([GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(1), GridSize.Auto, GridSize.Auto], [GridSize.Px(RowH)],
                [
                    Glyph(g.Collapsed ? "\uE76C" : "\uE70D", 12).Foreground(Secondary).VAlign(VerticalAlignment.Center),
                    TextBlock(g.Name).FontFamily(Kit.Display).FontSize(16).SemiBold().Foreground(Primary)
                        .VAlign(VerticalAlignment.Center).Grid(0, 1),
                    Border(TextBlock($"{g.Count:N0}").FontSize(12).SemiBold().Foreground(Secondary))
                        .Background(B("NeutralSoftBrush")).BorderBrush(B("SurfaceStrokeBrush")).BorderThickness(1)
                        .CornerRadius(10).Padding(8, 1, 8, 2).VAlign(VerticalAlignment.Center).Grid(0, 2),
                    TextBlock($"{g.Open} open").FontSize(12).SemiBold().Foreground(Cyan)
                        .VAlign(VerticalAlignment.Center).Grid(0, 4),
                    HStack(10,
                        Grid([GridSize.Px(160)], [GridSize.Px(6)],
                        [
                            Border(null).Background(B("ChartGridBrush")).CornerRadius(3),
                            Border(null).Width(g.Bar).HAlign(HorizontalAlignment.Left).Background(Cyan).CornerRadius(3),
                        ]).VAlign(VerticalAlignment.Center),
                        TextBlock($"{g.Hours:N0} h").FontFamily(MonoFont).FontSize(12).Foreground(Secondary)
                            .Width(64).TextAlignment(TextAlignment.Right).VAlign(VerticalAlignment.Center))
                        .Grid(0, 5),
                ]).Set(grid => grid.ColumnSpacing = 14),
                () => setCollapsed(g.Collapsed ? collapsed.Remove(g.Name) : collapsed.Add(g.Name)))
            .Width(Math.Max(0, tableWidth - 2)).Height(RowH)
            .Set(b => b.HorizontalContentAlignment = HorizontalAlignment.Stretch)
            .Background(B("SubtleFillColorSecondaryBrush")).BorderThickness(0).CornerRadius(0).Padding(16, 0, 20, 0)
            .AutomationId($"Group_{g.Name.Replace(' ', '_')}")
            .AutomationName($"{g.Name}, {g.Count} work orders, {g.Open} open, {(g.Collapsed ? "collapsed" : "expanded")}");

        Element SignOffCell(WorkOrder wo)
        {
            if (!string.IsNullOrEmpty(wo.SignaturePath) && File.Exists(wo.SignaturePath))
                return Border(Image(wo.SignaturePath).Set(img => img.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform)
                        .AutomationName($"Ink signature, {wo.SignedBy}"))
                    .Height(30).Padding(6, 2).HAlign(HorizontalAlignment.Left)
                    .Background(B("InkSurfaceBrush")).BorderBrush(B("HealthyBrush")).BorderThickness(1).CornerRadius(6)
                    .ToolTip(wo.SignedBy ?? "Signed");
            return TextBlock(wo.SignedBy ?? string.Empty).FontSize(13).Foreground(Tertiary).TextTrimming(TextTrimming.CharacterEllipsis);
        }

        Element CellView(CellContext<WorkOrderRow> c)
        {
            if (c.Row.Group is { } g)
            {
                // Full-width group header: a Canvas doesn't clip, so the header overflows from the first
                // cell back over the ✓ column and across the whole row.
                return c.Column.Name == "Number"
                    ? Canvas(GroupHeader(g).Canvas(-SelectColumn, -RowH / 2))
                    : Empty();
            }

            var wo = c.Row.Wo!;
            Element content = c.Column.Name switch
            {
                "Number" => Mono(wo.NumberText).Foreground(Cyan),
                "Tail" => Mono(wo.Tail).SemiBold(),
                "Component" => TextBlock(wo.Component).FontSize(14).Foreground(Primary).TextTrimming(TextTrimming.CharacterEllipsis),
                "Ata" => Mono(wo.Ata).Foreground(Secondary),
                "Finding" => TextBlock(wo.Finding).FontSize(14).Foreground(Primary).TextTrimming(TextTrimming.CharacterEllipsis).ToolTip(wo.Finding),
                "Technician" => HStack(10,
                    Border(TextBlock(WorkOrderFormat.Initials(wo.Technician)).FontSize(11).SemiBold().Foreground(Primary)
                            .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                        .Width(28).Height(28).CornerRadius(14).Background(B("NeutralSoftBrush"))
                        .BorderBrush(B("SurfaceStrokeBrush")).BorderThickness(1).AccessibilityHidden(),
                    TextBlock(wo.Technician).FontSize(14).Foreground(Primary).VAlign(VerticalAlignment.Center)
                        .TextTrimming(TextTrimming.CharacterEllipsis)),
                "Status" => StatusPill(wo),
                "PartNumber" => Mono(wo.PartNumber).Foreground(Secondary),
                "Opened" => Mono(wo.OpenedText),
                "Hours" => Mono(WorkOrderFormat.Hours(wo.Hours)).ToolTip("Click to edit labor hours"),
                "SignOff" => SignOffCell(wo),
                "Map" => wo.IsMappable
                    ? Button(Glyph("\uE709", 14).Foreground(Cyan).HAlign(HorizontalAlignment.Center), () => ShowOnAircraft(wo))
                        .Width(36).Height(32).Padding(0).Background(B("SubtleFillColorTransparentBrush")).BorderThickness(0)
                        .HAlign(HorizontalAlignment.Center)
                        .ToolTip("Show on aircraft").AutomationId($"ShowOnAircraft_{wo.Number}").AutomationName($"Show {wo.NumberText} on aircraft")
                    : Empty(),
                _ => Empty(),
            };
            return c.Column.Name == "Map" ? content : content.Margin(4, 0, 12, 0);
        }

        var empty = VStack(8,
                Glyph("\uE721", 32).Foreground(Tertiary).HAlign(HorizontalAlignment.Center),
                SectionTitle("No work orders match").HAlign(HorizontalAlignment.Center),
                Body("Try clearing a filter chip or the search text.").HAlign(HorizontalAlignment.Center))
            .Margin(24, 64, 24, 64).HAlign(HorizontalAlignment.Center).AutomationId("WorkOrdersEmpty");

        var grid = Component<DataGridComponent<WorkOrderRow>, DataGridElement<WorkOrderRow>>(new DataGridElement<WorkOrderRow>
        {
            Source = source.Current,
            Columns = columns.Current,
            SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode.Single,
            OnSelectionChanged = keys =>
            {
                var key = keys.FirstOrDefault().Value;
                if (key is not null && key.StartsWith("w:", StringComparison.Ordinal)
                    && int.TryParse(key.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    setSelected(number);
            },
            OnRowChanged = (_, _) =>
            {
                setValidation(null);
                setEdits(++editsRef.Current);
                return Task.CompletedTask;
            },
            RowHeight = RowH,
            EstimatedRowHeight = RowH,
            Editable = true,
            EditMode = EditMode.Cell,
            AllowColumnReorder = false,
            AllowColumnResize = true,
            CellTemplate = CellView,
            HeaderTemplate = HeaderView,
            EmptyTemplate = empty,
        }).WithKey($"wo-grid-{resetVersion}");

        var table = Border(Grid([GridSize.Star(1)], [GridSize.Star(1)],
            [
                // Header band painted behind the grid's own header row.
                Border(null).Height(RowH).VAlign(VerticalAlignment.Top)
                    .Background(B("SubtleFillColorSecondaryBrush")).BorderBrush(B("SurfaceStrokeBrush"))
                    .Set(b =>
                    {
                        b.BorderThickness = new Thickness(0, 0, 0, 1);
                        b.CornerRadius = new CornerRadius(11, 11, 0, 0);
                    }),
                grid,
            ]))
            .Background(B("SurfaceBrush")).BorderBrush(B("SurfaceStrokeBrush")).BorderThickness(1).CornerRadius(12)
            .OnSizeChanged((_, e) => setTableWidth(Math.Round(e.NewSize.Width)))
            .AutomationId("WorkOrdersTable").AutomationName("Work orders");

        // ── Detail strip ──
        var title = sel is null ? "Select a work order" : $"{sel.NumberText} · {sel.Tail} · {sel.Component} — {sel.Finding}";
        var subtitle = sel is null
            ? "Click Hours to edit inline. Values must be greater than 0."
            : $"{sel.StatusText} · {sel.Technician} · opened {sel.OpenedText} · {sel.Hours:0.0} h · ATA {sel.Ata} · P/N {sel.PartNumber}"
              + (string.IsNullOrEmpty(sel.SignedBy) ? string.Empty : $" · signed by {sel.SignedBy}");

        var detail = Border(Grid([GridSize.Auto, GridSize.Star(1), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
            [
                Glyph(sel?.IsMappable == true ? "\uE709" : "\uE8FD", 18).Foreground(Secondary),
                VStack(2,
                    TextBlock(title).SemiBold().Foreground(Primary).TextTrimming(TextTrimming.CharacterEllipsis).AutomationId("DetailTitle"),
                    TextBlock(subtitle).FontSize(14).Foreground(Secondary).TextTrimming(TextTrimming.CharacterEllipsis).AutomationId("DetailSubtitle"))
                    .VAlign(VerticalAlignment.Center).Grid(0, 1),
                validation is null
                    ? Empty()
                    : TextBlock(validation).Foreground(B("CriticalBrush")).VAlign(VerticalAlignment.Center)
                        .AutomationId("HoursValidationText").Grid(0, 2),
                Button(HStack(10, Glyph("\uE709", 14), TextBlock("Show on aircraft").SemiBold().VAlign(VerticalAlignment.Center)),
                        () => { if (sel is not null) ShowOnAircraft(sel); })
                    .AccentButton().CornerRadius(20).Padding(18, 10).IsEnabled(sel?.IsMappable == true)
                    .AutomationId("ShowOnAircraftButton").AutomationName("Show selected work order on aircraft")
                    .Grid(0, 3),
            ]).Set(g => g.ColumnSpacing = 20))
            .Padding(20, 14, 20, 14)
            .Background(B("SurfaceBrush")).BorderBrush(B("SurfaceStrokeBrush")).BorderThickness(1).CornerRadius(12)
            .AutomationId("DetailStrip");

        return Grid([GridSize.Star(1)], [GridSize.Auto, GridSize.Auto, GridSize.Star(1), GridSize.Auto],
        [
            header.Margin(0, 0, 0, 20),
            toolbar.Margin(0, 0, 0, 20).Grid(1, 0),
            table.Grid(2, 0),
            detail.Margin(0, 20, 0, 0).Grid(3, 0),
        ]).Padding(40, 28, 40, 28);
    }

    private static Element StatusPill(WorkOrder wo)
    {
        var (color, soft) = Status(wo.Status);
        return Border(HStack(6, Dot(B(color), 6),
                TextBlock(wo.StatusText).FontSize(11).SemiBold().Foreground(B(color)).VAlign(VerticalAlignment.Center)))
            .Background(B(soft)).BorderBrush(B(color)).BorderThickness(1).CornerRadius(11)
            .Padding(9, 2, 10, 3).HAlign(HorizontalAlignment.Left);
    }

    private static IReadOnlyList<FieldDescriptor> BuildColumns(PageCallbacks callbacks)
    {
        FieldDescriptor Col(string name, string display, double width, Func<WorkOrderRow, object?> get, bool sortable = true)
        {
            var builder = Adv.Column(name, get, displayName: display, width: width);
            if (!sortable) builder = builder.NotSortable();
            return builder;
        }

        Element HoursEditor(object? value, Action<object> set) =>
            TextBox(value is double d ? d.ToString("0.0", CultureInfo.CurrentCulture) : value?.ToString() ?? string.Empty, s =>
                {
                    set(s);
                    callbacks.SetValidation(TryParseHours(s, out _) ? null : HoursValidator.Message);
                }, string.Empty)
                .Width(76).FontFamily(MonoFont).FontSize(13)
                .AutomationId("HoursEditor").AutomationName("Labor hours")
                .OnGotFocus((s, _) => ((XamlControls.TextBox)s).SelectAll())
                .OnKeyDown((_, e) => { if (e.Key == VirtualKey.Escape) callbacks.SetValidation(null); });

        var hours = Adv.Column<WorkOrderRow>("Hours", r => r.Hours, displayName: "Hours", width: 88)
            .Validate(new HoursValidator())
            .WithEditor(HoursEditor)
            .Build() with
        {
            Description = "Labor hours · click a value to edit",
            IsReadOnly = false,
            SetValue = (item, value) =>
            {
                var row = (WorkOrderRow)item;
                if (row.Wo is not null && TryParseHours(value, out var h)) row.Wo.Hours = Math.Round(h, 1);
                return row;
            },
        };

        return
        [
            Col("Number", "WO #", 116, r => r.Number),
            Col("Tail", "Tail", 100, r => r.Tail),
            Col("Component", "Component", 132, r => r.Component),
            Col("Ata", "ATA", 76, r => r.Ata),
            Col("Finding", "Finding", 300, r => r.Finding),
            Col("Technician", "Technician", 184, r => r.Technician),
            Col("Status", "Status", 128, r => r.Status),
            Col("PartNumber", "Part #", 124, r => r.PartNumber),
            Col("Opened", "Opened", 116, r => r.Opened),
            hours,
            Col("SignOff", "Sign-off", 100, r => r.SignOff, sortable: false),
            Col("Map", "", 44, r => r.Map, sortable: false),
        ];
    }

    private static bool TryParseHours(object? value, out double hours)
    {
        hours = value switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) => parsed,
            _ => double.NaN,
        };
        return hours is >= 0.1 and <= 500;
    }

    private static IComparable SortValue(WorkOrder w, string key) => key switch
    {
        "Number" => w.Number,
        "Tail" => w.Tail,
        "Component" => w.Component,
        "Ata" => w.Ata,
        "Finding" => w.Finding,
        "Technician" => w.Technician,
        "Status" => (int)w.Status,
        "PartNumber" => w.PartNumber,
        "Opened" => w.Opened,
        "Hours" => w.Hours,
        _ => w.Number,
    };

    private static bool MatchesQuery(WorkOrder w, string query) =>
        w.Tail.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Component.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.NumberText.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Finding.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.PartNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Technician.Contains(query, StringComparison.OrdinalIgnoreCase)
        || w.Ata.Contains(query, StringComparison.OrdinalIgnoreCase);
}
