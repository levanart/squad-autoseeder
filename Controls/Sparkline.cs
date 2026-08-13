using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Autoseeder.Client.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Sparkline),
        new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values { get => (IEnumerable?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth < 100 || ActualHeight < 60) return;

        const double left = 38;
        const double right = 6;
        const double top = 4;
        const double bottom = 18;
        var plotWidth = Math.Max(1, ActualWidth - left - right);
        var plotHeight = Math.Max(1, ActualHeight - top - bottom);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        var axisBrush = new SolidColorBrush(Color.FromArgb(150, 155, 159, 170));

        for (var index = 0; index <= 4; index++)
        {
            var ratio = index / 4d;
            var y = top + plotHeight * ratio;
            drawingContext.DrawLine(gridPen, new Point(left, y), new Point(left + plotWidth, y));
            var value = Maximum * (1 - ratio);
            DrawText(drawingContext, $"{value:0}{Unit}", axisBrush, 9, new Point(0, y - 6));
        }

        for (var index = 0; index <= 6; index++)
        {
            var x = left + plotWidth * index / 6d;
            drawingContext.DrawLine(gridPen, new Point(x, top), new Point(x, top + plotHeight));
        }

        DrawText(drawingContext, "−120с", axisBrush, 9, new Point(left, top + plotHeight + 3));
        DrawText(drawingContext, "−60с", axisBrush, 9, new Point(left + plotWidth / 2 - 10, top + plotHeight + 3));
        DrawText(drawingContext, "сейчас", axisBrush, 9, new Point(left + plotWidth - 30, top + plotHeight + 3));

        var values = Values?.Cast<object>().Select(Convert.ToDouble).ToArray() ?? [];
        if (values.Length < 2) return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < values.Length; index++)
            {
                var x = left + index * plotWidth / Math.Max(1, values.Length - 1);
                var normalized = Math.Clamp(values[index] / Math.Max(1, Maximum), 0, 1);
                var point = new Point(x, top + plotHeight - normalized * plotHeight);
                if (index == 0) context.BeginFigure(point, false, false);
                else context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round }, geometry);
    }

    private void DrawText(DrawingContext context, string text, Brush brush, double size, Point origin)
    {
        var formatted = new FormattedText(
            text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, origin);
    }
}
