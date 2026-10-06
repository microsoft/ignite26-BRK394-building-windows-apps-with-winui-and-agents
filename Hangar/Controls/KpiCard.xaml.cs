using Hangar.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Hangar.Controls;

public sealed partial class KpiCard : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(KpiCard), new PropertyMetadata(string.Empty, OnTextChanged));

    private IReadOnlyList<double> _values = [];
    private string _strokeKey = "AccentCyanBrush";
    private string _fillKey = "AccentAreaBrush";

    public KpiCard()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Render();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public void SetSpark(IReadOnlyList<double> values, string strokeKey, string fillKey)
    {
        _values = values;
        _strokeKey = strokeKey;
        _fillKey = fillKey;
        Render();
    }

    private void Render()
    {
        if (_values.Count < 2)
        {
            return;
        }

        var width = Spark.ActualWidth;
        var height = Spark.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var min = _values.Min();
        var max = _values.Max();
        var range = Math.Max(max - min, 0.001);
        const double inset = 4;
        var points = _values
            .Select((v, i) => new Point(
                inset + (i / (double)(_values.Count - 1) * (width - (inset * 2))),
                inset + ((1 - ((v - min) / range)) * (height - (inset * 2)))))
            .ToList();

        var line = new PathFigure { StartPoint = points[0] };
        var area = new PathFigure { StartPoint = new Point(points[0].X, height), IsClosed = true };
        area.Segments.Add(new LineSegment { Point = points[0] });
        foreach (var point in points.Skip(1))
        {
            line.Segments.Add(new LineSegment { Point = point });
            area.Segments.Add(new LineSegment { Point = point });
        }

        area.Segments.Add(new LineSegment { Point = new Point(points[^1].X, height) });

        SparkLine.Data = new PathGeometry { Figures = { line } };
        SparkArea.Data = new PathGeometry { Figures = { area } };
        SparkLine.Stroke = ThemeRes.Brush(this, _strokeKey);
        SparkArea.Fill = ThemeRes.Brush(this, _fillKey);
        SparkDot.Fill = ThemeRes.Brush(this, _strokeKey);
        SparkDot.Margin = new Thickness(points[^1].X - 3.5, points[^1].Y - 3.5, 0, 0);
    }

    private void OnSparkSizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (KpiCard)d;
        AutomationProperties.SetName(card, $"{card.Label}: {card.Value}. {card.Caption}");
    }
}
