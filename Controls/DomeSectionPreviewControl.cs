using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class DomeSectionPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ParametricDomeModel),
            typeof(DomeSectionPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ParametricDomeModel? Model
    {
        get => (ParametricDomeModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), null, new Rect(0, 0, ActualWidth, ActualHeight));

        ParametricDomeModel? model = Model;
        if (ActualWidth < 80 || ActualHeight < 80 || model == null || model.Nodes.Count == 0)
        {
            DrawText(drawingContext, "Dome preview unavailable", new Point(18, 18), 13, Brushes.DimGray);
            return;
        }

        double maxRadius = model.Nodes
            .Select(node => Math.Sqrt(Math.Pow(node.X - model.BaseCenterX, 2) + Math.Pow(node.Y - model.BaseCenterY, 2)))
            .DefaultIfEmpty(model.BaseRadius)
            .Max();
        maxRadius = Math.Max(maxRadius, 1.0);
        double minZ = model.BaseElevationZ + model.LowerCutHeight;
        double maxZ = model.BaseElevationZ + model.UpperCutHeight;
        if (Math.Abs(maxZ - minZ) < 0.000001)
            maxZ = minZ + 1.0;

        const double left = 54;
        const double right = 28;
        const double top = 46;
        const double bottom = 54;
        double drawWidth = Math.Max(1, ActualWidth - left - right);
        double drawHeight = Math.Max(1, ActualHeight - top - bottom);
        double scaleX = drawWidth / (maxRadius * 2.0);
        double scaleY = drawHeight / (maxZ - minZ);
        double scale = Math.Min(scaleX, scaleY);
        double centerX = left + drawWidth / 2.0;
        double baseY = top + drawHeight;

        Point Map(double radius, double z)
        {
            double x = centerX + radius * scale;
            double y = baseY - (z - minZ) * scale;
            return new Point(x, y);
        }

        DrawGrid(drawingContext, left, top, drawWidth, drawHeight);

        var ringGroups = model.Nodes
            .GroupBy(node => node.RingIndex)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Z = group.Average(node => node.Z),
                Radius = group.Max(node => Math.Sqrt(Math.Pow(node.X - model.BaseCenterX, 2) + Math.Pow(node.Y - model.BaseCenterY, 2)))
            })
            .ToList();

        var leftProfile = new StreamGeometry();
        using (StreamGeometryContext context = leftProfile.Open())
        {
            bool started = false;
            foreach (var ring in ringGroups)
            {
                Point point = Map(-ring.Radius, ring.Z);
                if (!started)
                {
                    context.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        var rightProfile = new StreamGeometry();
        using (StreamGeometryContext context = rightProfile.Open())
        {
            bool started = false;
            foreach (var ring in ringGroups)
            {
                Point point = Map(ring.Radius, ring.Z);
                if (!started)
                {
                    context.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        var domePen = new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2.2);
        drawingContext.DrawGeometry(null, domePen, leftProfile);
        drawingContext.DrawGeometry(null, domePen, rightProfile);

        var ringPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1.0) { DashStyle = DashStyles.Dash };
        foreach (var ring in ringGroups)
        {
            Point leftPoint = Map(-ring.Radius, ring.Z);
            Point rightPoint = Map(ring.Radius, ring.Z);
            drawingContext.DrawLine(ringPen, leftPoint, rightPoint);
        }

        DrawText(drawingContext, $"{model.DomeId} / {model.RingCount} rings / {model.SegmentCount} segments", new Point(18, 12), 13, Brushes.DimGray);
        DrawText(drawingContext, $"Nodes {model.Nodes.Count}  Frames {model.FrameMembers.Count}  Shells {model.ShellPanels.Count}", new Point(18, ActualHeight - 28), 12, Brushes.SlateGray);
    }

    private static void DrawGrid(DrawingContext dc, double x, double y, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(225, 231, 239)), 1);
        for (int index = 0; index <= 4; index++)
        {
            double gx = x + width * index / 4.0;
            double gy = y + height * index / 4.0;
            dc.DrawLine(pen, new Point(gx, y), new Point(gx, y + height));
            dc.DrawLine(pen, new Point(x, gy), new Point(x + width, gy));
        }
    }

    private static void DrawText(DrawingContext dc, string text, Point point, double fontSize, Brush brush)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush,
            pixelsPerDip);

        dc.DrawText(formattedText, point);
    }
}
