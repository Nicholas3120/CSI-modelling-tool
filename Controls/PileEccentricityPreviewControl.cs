using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TrussModelling.Models;

namespace TrussModelling.Controls;

public sealed class PileEccentricityPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(PileEccentricityPreviewModel),
            typeof(PileEccentricityPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public PileEccentricityPreviewModel? Model
    {
        get => (PileEccentricityPreviewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), null, new Rect(new Point(0, 0), RenderSize));
        if (ActualWidth < 120 || ActualHeight < 120)
            return;

        PileEccentricityPreviewModel? model = Model;
        if (model == null || (model.Groups.Count == 0 && model.Piles.Count == 0))
        {
            DrawText(drawingContext, "Preview unavailable", new Point(24, 22), 14, Brushes.DimGray);
            return;
        }

        List<Point> points = BuildModelPoints(model);
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        ExpandIfFlat(ref minX, ref maxX);
        ExpandIfFlat(ref minY, ref maxY);

        const double left = 72;
        const double right = 38;
        const double top = 92;
        const double bottom = 44;
        double drawableWidth = Math.Max(1, ActualWidth - left - right);
        double drawableHeight = Math.Max(1, ActualHeight - top - bottom);
        double scale = Math.Min(drawableWidth / (maxX - minX), drawableHeight / (maxY - minY));
        double usedWidth = (maxX - minX) * scale;
        double usedHeight = (maxY - minY) * scale;
        double originX = left + (drawableWidth - usedWidth) / 2.0;
        double originY = top + (drawableHeight - usedHeight) / 2.0;

        Point Map(double x, double y)
        {
            return new Point(
                originX + (x - minX) * scale,
                originY + (maxY - y) * scale);
        }

        DrawGrid(drawingContext, originX, originY, usedWidth, usedHeight);
        DrawTieBeams(drawingContext, model, Map);
        DrawPiles(drawingContext, model, Map);
        DrawGroups(drawingContext, model, Map);
        DrawLegend(drawingContext, model);
    }

    private static List<Point> BuildModelPoints(PileEccentricityPreviewModel model)
    {
        var points = new List<Point>();
        points.AddRange(model.Piles.Select(pile => new Point(pile.X, pile.Y)));
        foreach (PileEccentricityPreviewGroup group in model.Groups)
        {
            points.Add(new Point(group.ColumnX, group.ColumnY));
            points.Add(new Point(group.CentroidX, group.CentroidY));
        }

        foreach (PileEccentricityPreviewTieBeam tieBeam in model.TieBeams)
        {
            points.Add(new Point(tieBeam.FromX, tieBeam.FromY));
            points.Add(new Point(tieBeam.ToX, tieBeam.ToY));
        }

        if (points.Count == 0)
            points.Add(new Point());

        return points;
    }

    private static void DrawGrid(DrawingContext dc, double x, double y, double width, double height)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(225, 231, 239)), 1);
        for (int index = 0; index <= 5; index++)
        {
            double gx = x + width * index / 5.0;
            dc.DrawLine(gridPen, new Point(gx, y), new Point(gx, y + height));

            double gy = y + height * index / 5.0;
            dc.DrawLine(gridPen, new Point(x, gy), new Point(x + width, gy));
        }

        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1), new Rect(x, y, width, height));
    }

    private static void DrawTieBeams(
        DrawingContext dc,
        PileEccentricityPreviewModel model,
        Func<double, double, Point> map)
    {
        var tiePen = new Pen(new SolidColorBrush(Color.FromRgb(79, 70, 229)), 3.0);
        foreach (PileEccentricityPreviewTieBeam tieBeam in model.TieBeams)
        {
            Point start = map(tieBeam.FromX, tieBeam.FromY);
            Point end = map(tieBeam.ToX, tieBeam.ToY);
            dc.DrawLine(tiePen, start, end);
            DrawArrowHead(dc, start, end, tiePen.Brush);
        }
    }

    private static void DrawPiles(
        DrawingContext dc,
        PileEccentricityPreviewModel model,
        Func<double, double, Point> map)
    {
        var border = new Pen(Brushes.White, 1.4);
        int index = 0;
        foreach (PileEccentricityPreviewPile pile in model.Piles)
        {
            Point point = map(pile.X, pile.Y);
            Brush brush = PileBrush(pile.Status);
            dc.DrawEllipse(brush, border, point, 8.0, 8.0);
            DrawPileLabel(dc, pile.PileId, point, index);
            index++;
        }
    }

    private static void DrawPileLabel(DrawingContext dc, string label, Point pilePoint, int index)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        FormattedText text = CreateFormattedText(label, 10.5, Brushes.DimGray, pixelsPerDip);
        double xOffset = index % 2 == 0 ? 9.0 : -text.Width - 9.0;
        double yOffset = index % 4 < 2 ? -text.Height - 4.0 : 4.0;
        Point point = new(pilePoint.X + xOffset, pilePoint.Y + yOffset);
        Rect background = new(point.X - 3, point.Y - 1, text.Width + 6, text.Height + 2);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(218, 255, 255, 255)), null, background, 3, 3);
        dc.DrawText(text, point);
    }

    private static void DrawGroups(
        DrawingContext dc,
        PileEccentricityPreviewModel model,
        Func<double, double, Point> map)
    {
        var centroidPen = new Pen(new SolidColorBrush(Color.FromRgb(14, 116, 144)), 2.0);
        var columnPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 2.0);
        var eccPen = new Pen(new SolidColorBrush(Color.FromRgb(217, 119, 6)), 2.0);

        foreach (PileEccentricityPreviewGroup group in model.Groups)
        {
            Point centroid = map(group.CentroidX, group.CentroidY);
            Point column = map(group.ColumnX, group.ColumnY);

            dc.DrawLine(centroidPen, new Point(centroid.X - 9, centroid.Y), new Point(centroid.X + 9, centroid.Y));
            dc.DrawLine(centroidPen, new Point(centroid.X, centroid.Y - 9), new Point(centroid.X, centroid.Y + 9));
            dc.DrawRectangle(null, columnPen, new Rect(column.X - 7, column.Y - 7, 14, 14));

            dc.DrawLine(eccPen, centroid, column);
            DrawArrowHead(dc, centroid, column, eccPen.Brush);
        }
    }

    private static void DrawLegend(DrawingContext dc, PileEccentricityPreviewModel model)
    {
        DrawText(dc, "Plan preview", new Point(18, 10), 12.5, Brushes.DimGray);
        DrawLegendSymbol(dc, new Point(18, 36), new SolidColorBrush(Color.FromRgb(37, 99, 235)), "Pile");
        DrawLegendCross(dc, new Point(86, 44), new SolidColorBrush(Color.FromRgb(14, 116, 144)), "Centroid");
        DrawLegendSquare(dc, new Point(176, 44), new SolidColorBrush(Color.FromRgb(220, 38, 38)), "Column");
        DrawLegendLine(dc, new Point(262, 44), new SolidColorBrush(Color.FromRgb(217, 119, 6)), "Ecc.");
        DrawLegendLine(dc, new Point(328, 44), new SolidColorBrush(Color.FromRgb(79, 70, 229)), "Tie");

        double x = 18;
        double y = 64;
        foreach (PileEccentricityPreviewGroup group in model.Groups.Take(2))
        {
            string text = $"{group.GroupId}: ex {group.Ex:0.###} m, ey {group.Ey:0.###} m, Mx {group.MxkNm:0.#}, My {group.MykNm:0.#}";
            DrawText(dc, text, new Point(x, y), 10.8, Brushes.DimGray);
            x += 330;
        }
    }

    private static void DrawLegendSymbol(DrawingContext dc, Point point, Brush brush, string label)
    {
        dc.DrawEllipse(brush, new Pen(Brushes.White, 1), point, 5.0, 5.0);
        DrawText(dc, label, new Point(point.X + 10, point.Y - 8), 10.5, Brushes.DimGray);
    }

    private static void DrawLegendCross(DrawingContext dc, Point point, Brush brush, string label)
    {
        var pen = new Pen(brush, 1.6);
        dc.DrawLine(pen, new Point(point.X - 6, point.Y), new Point(point.X + 6, point.Y));
        dc.DrawLine(pen, new Point(point.X, point.Y - 6), new Point(point.X, point.Y + 6));
        DrawText(dc, label, new Point(point.X + 10, point.Y - 8), 10.5, Brushes.DimGray);
    }

    private static void DrawLegendSquare(DrawingContext dc, Point point, Brush brush, string label)
    {
        dc.DrawRectangle(null, new Pen(brush, 1.6), new Rect(point.X - 5, point.Y - 5, 10, 10));
        DrawText(dc, label, new Point(point.X + 10, point.Y - 8), 10.5, Brushes.DimGray);
    }

    private static void DrawLegendLine(DrawingContext dc, Point point, Brush brush, string label)
    {
        dc.DrawLine(new Pen(brush, 2), new Point(point.X - 8, point.Y), new Point(point.X + 8, point.Y));
        DrawText(dc, label, new Point(point.X + 13, point.Y - 8), 10.5, Brushes.DimGray);
    }

    private static void DrawArrowHead(DrawingContext dc, Point start, Point end, Brush brush)
    {
        Vector direction = start - end;
        if (direction.Length < 0.001)
            return;

        direction.Normalize();
        Vector normal = new(-direction.Y, direction.X);
        Point p1 = end + direction * 10 + normal * 5;
        Point p2 = end + direction * 10 - normal * 5;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(end, true, true);
            context.LineTo(p1, true, false);
            context.LineTo(p2, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static Brush PileBrush(string status)
    {
        if (status.Contains("Uplift", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Tension", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(217, 119, 6));
        }

        if (status.Contains("Exceeds", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromRgb(220, 38, 38));

        return new SolidColorBrush(Color.FromRgb(37, 99, 235));
    }

    private static void ExpandIfFlat(ref double minimum, ref double maximum)
    {
        if (Math.Abs(maximum - minimum) >= 0.000001)
            return;

        minimum -= 0.5;
        maximum += 0.5;
    }

    private static void DrawText(DrawingContext dc, string text, Point point, double fontSize, Brush brush)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        FormattedText formattedText = CreateFormattedText(text, fontSize, brush, pixelsPerDip);
        dc.DrawText(formattedText, point);
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, Brush brush, double pixelsPerDip)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush,
            pixelsPerDip);
    }
}
