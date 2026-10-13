using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Charts;
using Microsoft.UI.Xaml.Media;

namespace Hangar.Helpers;

/// <summary>
/// Thin builder over the first-party WinAppSDK 2.5 experimental Chart control. All charts in HANGAR are
/// index-on-X cartesian charts, so the factory owns the shared X samples and axis plumbing.
/// </summary>
public sealed class ChartFactory
{
    private readonly Chart _chart;
    private readonly FrameworkElement _themeSource;
    private Samples? _x;
    private CartesianAxis? _xAxis;
    private LinearAxis? _yAxis;

    private ChartFactory(Chart chart, FrameworkElement themeSource)
    {
        _chart = chart;
        _themeSource = themeSource;
    }

    public static ChartFactory For(Chart chart, FrameworkElement? themeSource = null)
    {
        chart.Series.Clear();
        chart.Data.Clear();
        chart.Axes.Clear();
        chart.ShowLegend = false;
        chart.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        return new ChartFactory(chart, themeSource ?? chart);
    }

    public Brush Brush(string key) => ThemeRes.Brush(_themeSource, key);

    /// <summary>Configures X (0..count-1) and Y axes. Sparklines hide both axes entirely.</summary>
    public ChartFactory Axes(int count, double yMin, double yMax, bool visible = false, double? ySpacing = null)
    {
        _x = new Samples { ItemsSource = Enumerable.Range(0, count).Select(i => (double)i).ToList() };
        _chart.Data.Add(_x);

        _xAxis = new CategoryAxis { IsVisible = visible };
        // The Y axis must stay "visible" or the chart auto-ranges and ignores Minimum/Maximum; hide its chrome instead.
        _yAxis = new LinearAxis { Minimum = yMin, Maximum = yMax, IsVisible = true, Spacing = ySpacing };

        foreach (var axis in new CartesianAxis[] { _xAxis, _yAxis })
        {
            axis.ShowTickMarks = false;
            axis.ShowTickLabels = visible;
            axis.GridLines = GridLines.None;
            axis.AxisLineBrush = visible ? Brush("ChartGridBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            axis.TickLabelBrush = Brush("TertiaryTextBrush");
        }

        if (visible)
        {
            _xAxis.IsVisible = false;
            _yAxis.GridLines = GridLines.Major;
            _yAxis.GridLineMajorBrush = Brush("ChartGridBrush");
        }

        _chart.Axes.Add(_xAxis);
        _chart.Axes.Add(_yAxis);

        if (!visible)
        {
            // With tick labels hidden the chart auto-ranges to the data and ignores Minimum/Maximum, clipping
            // the lowest points. Two invisible series pinned at the bounds force the full range.
            Pin(count, yMin);
            Pin(count, yMax);
        }

        return this;
    }

    private void Pin(int count, double value)
    {
        var series = new LineSeries();
        Configure(series, Enumerable.Repeat(value, count), "ChartGridBrush", 0, null);
        series.Stroke = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public AreaSeries Area(IEnumerable<double> values, string strokeKey, string fillKey, double thickness = 2, string? title = null)
    {
        // AreaSeries strokes the whole polygon (including baseline and edges), so draw the fill stroke-less
        // and lay a LineSeries on top for the crisp data edge.
        var list = values.ToList();
        var series = new AreaSeries();
        Configure(series, list, strokeKey, 0, null);
        series.Fill = ToSolid(Brush(fillKey), Brush(strokeKey));
        Line(list, strokeKey, thickness, StrokeDashStyle.Solid, title);
        return series;
    }

    public LineSeries Line(IEnumerable<double> values, string strokeKey, double thickness = 1.5, StrokeDashStyle dash = StrokeDashStyle.Solid, string? title = null)
    {
        var series = new LineSeries { StrokeDashStyle = dash };
        Configure(series, values, strokeKey, thickness, title);
        return series;
    }

    /// <summary>A constant horizontal reference line (target / threshold).</summary>
    public LineSeries Reference(int count, double value, string strokeKey, StrokeDashStyle dash = StrokeDashStyle.Dash, string? title = null) =>
        Line(Enumerable.Repeat(value, count), strokeKey, 1.5, dash, title);

    /// <summary>Highlights a single point with a marker by plotting a one-sample series at <paramref name="index"/>.</summary>
    public LineSeries Marker(int index, double value, string brushKey, MarkerShape shape = MarkerShape.Circle, string? title = null)
    {
        var xs = new Samples { ItemsSource = new List<double> { index } };
        var ys = new Samples { ItemsSource = new List<double> { value } };
        _chart.Data.Add(xs);
        _chart.Data.Add(ys);
        var series = new LineSeries
        {
            XValues = xs,
            YValues = ys,
            XAxis = _xAxis,
            YAxis = _yAxis,
            Stroke = Brush(brushKey),
            StrokeThickness = 0,
            ShowDataMarkers = true,
            MarkerShape = shape,
            DataMarkerBrush = Brush(brushKey),
            Title = title ?? string.Empty,
        };
        _chart.Series.Add(series);
        return series;
    }

    // Charts only renders solid fills. Always hand it a fresh SolidColorBrush: under Native AOT a gradient
    // resource can surface as a plain Brush projection, so fall back to the stroke colour at 25% alpha.
    private static Brush ToSolid(Brush fill, Brush stroke)
    {
        if (fill is SolidColorBrush solid)
        {
            return new SolidColorBrush(solid.Color) { Opacity = solid.Opacity };
        }

        var color = fill is GradientBrush g && g.GradientStops.Count > 0
            ? g.GradientStops[0].Color
            : stroke is SolidColorBrush s ? s.Color : Microsoft.UI.Colors.Gray;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, color.R, color.G, color.B));
    }

    private void Configure(CartesianSeries series, IEnumerable<double> values, string strokeKey, double thickness, string? title)
    {
        if (_x is null)
        {
            throw new InvalidOperationException("Call Axes() before adding series.");
        }

        var list = values.ToList();
        var ys = new Samples { ItemsSource = list };
        _chart.Data.Add(ys);
        var xs = _x;
        if (_x.ItemsSource is IList<double> all && list.Count != all.Count)
        {
            // Partial series (e.g. a scrubbed timeline) get their own X samples so X/Y stay the same length.
            xs = new Samples { ItemsSource = Enumerable.Range(0, list.Count).Select(i => (double)i).ToList() };
            _chart.Data.Add(xs);
        }

        series.XValues = xs;
        series.YValues = ys;
        series.XAxis = _xAxis;
        series.YAxis = _yAxis;
        series.Stroke = Brush(strokeKey);
        series.StrokeThickness = thickness;
        series.ShowDataMarkers = false;
        series.MarkerShape = MarkerShape.None;
        series.Title = title ?? string.Empty;
        _chart.Series.Add(series);
    }
}
