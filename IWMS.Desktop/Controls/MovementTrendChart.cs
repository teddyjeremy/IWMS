using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IWMS.Desktop.Controls;

public sealed record MovementTrendPoint(DateTime Date, decimal Receipts, decimal Issues, decimal Net);

public sealed class MovementTrendChart : FrameworkElement
{
    private IReadOnlyList<MovementTrendPoint> _points = Array.Empty<MovementTrendPoint>();

    public IReadOnlyList<MovementTrendPoint> Points
    {
        get => _points;
        set
        {
            _points = value ?? Array.Empty<MovementTrendPoint>();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var minWidth = Math.Max(680, _points.Count * 26);
        var width = double.IsInfinity(availableSize.Width) ? minWidth : Math.Max(availableSize.Width, minWidth);
        return new Size(width, 280);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
            return;

        var background = Freeze(Color.FromRgb(255, 255, 255));
        dc.DrawRectangle(background, null, new Rect(0, 0, width, height));

        if (_points.Count == 0)
        {
            dc.DrawText(Text("No movement history was returned for this period.", 13, Color.FromRgb(100, 116, 139)), new Point(12, 16));
            return;
        }

        const double left = 52;
        const double right = 16;
        const double top = 12;
        const double bottom = 40;
        var plotWidth = Math.Max(10, width - left - right);
        var plotHeight = Math.Max(10, height - top - bottom);
        var peak = (double)_points.Max(point => Math.Max(point.Receipts, point.Issues));
        var max = peak <= 0 ? 1 : peak;
        var grid = new Pen(Freeze(Color.FromRgb(226, 232, 240)), 1);
        grid.Freeze();

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = top + plotHeight - (plotHeight * tick / 4);
            dc.DrawLine(grid, new Point(left, y), new Point(width - right, y));
            dc.DrawText(Text((max * tick / 4).ToString("0.#", CultureInfo.CurrentCulture), 11, Color.FromRgb(100, 116, 139)), new Point(0, y - 8));
        }

        var slot = plotWidth / _points.Count;
        var barWidth = Math.Max(3, Math.Min(12, slot * 0.28));
        var receiptBrush = Freeze(Color.FromRgb(15, 118, 110));
        var issueBrush = Freeze(Color.FromRgb(194, 65, 12));
        var labelStep = _points.Count <= 16 ? 1 : _points.Count <= 32 ? 4 : 8;

        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index];
            var center = left + (slot * index) + (slot / 2);
            var receiptHeight = plotHeight * (double)point.Receipts / max;
            var issueHeight = plotHeight * (double)point.Issues / max;
            if (receiptHeight >= 1)
                dc.DrawRoundedRectangle(receiptBrush, null, new Rect(center - barWidth - 1, top + plotHeight - receiptHeight, barWidth, receiptHeight), 1.5, 1.5);
            if (issueHeight >= 1)
                dc.DrawRoundedRectangle(issueBrush, null, new Rect(center + 1, top + plotHeight - issueHeight, barWidth, issueHeight), 1.5, 1.5);

            if (index % labelStep == 0)
            {
                dc.DrawText(
                    Text(point.Date.ToString("dd MMM", CultureInfo.CurrentCulture), 10, Color.FromRgb(100, 116, 139)),
                    new Point(center - 16, top + plotHeight + 10));
            }
        }

        if (peak <= 0)
        {
            dc.DrawText(Text("No stock was received or issued in this period.", 13, Color.FromRgb(100, 116, 139)), new Point(left, top + 8));
        }
    }

    private FormattedText Text(string value, double size, Color color) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Freeze(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
