using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class PlateGirder2DPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ParametricPlateGirderModel),
            typeof(PlateGirder2DPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ParametricPlateGirderModel? Model
    {
        get => (ParametricPlateGirderModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), null, new Rect(0, 0, ActualWidth, ActualHeight));

        ParametricPlateGirderModel? model = Model;
        if (ActualWidth < 80 || ActualHeight < 80 || model == null || model.Nodes.Count == 0)
        {
            DrawText(drawingContext, "Plate girder preview unavailable", new Point(18, 18), 13, Brushes.DimGray);
            return;
        }

        const double left = 64;
        const double right = 32;
        const double top = 44;
        const double bottom = 64;
        double drawWidth = Math.Max(1, ActualWidth - left - right);
        double drawHeight = Math.Max(1, ActualHeight - top - bottom);
        double scale = Math.Min(drawWidth / model.Length, drawHeight / model.Depth);
        double originX = left + (drawWidth - model.Length * scale) / 2.0;
        double originY = top + (drawHeight - model.Depth * scale) / 2.0 + model.Depth * scale;

        Point Map(double x, double z)
        {
            return new Point(
                originX + (x - model.OriginX) * scale,
                originY - (z - model.OriginZ) * scale);
        }

        DrawGrid(drawingContext, originX, originY - model.Depth * scale, model.Length * scale, model.Depth * scale);
        DrawWebPanels(drawingContext, model, Map);
        DrawOpeningOutline(drawingContext, model, Map);
        DrawStiffenerPanels(drawingContext, model, Map);
        DrawDimensions(drawingContext, model, Map, originX, originY, scale);
        DrawText(drawingContext, $"{model.PlateGirderId} / {model.ShellPanels.Count} quad shells", new Point(18, 12), 13, Brushes.DimGray);
    }

    private static void DrawWebPanels(DrawingContext dc, ParametricPlateGirderModel model, Func<double, double, Point> map)
    {
        Dictionary<string, PlateGirderNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var webFill = new SolidColorBrush(Color.FromArgb(42, 37, 99, 235));
        var webPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 0.8);

        foreach (PlateGirderShellPanel panel in model.ShellPanels.Where(panel => panel.Group == PlateGirderShellGroup.Web))
            DrawPanel(dc, panel, nodes, map, webFill, webPen);
    }

    private static void DrawStiffenerPanels(DrawingContext dc, ParametricPlateGirderModel model, Func<double, double, Point> map)
    {
        Dictionary<string, PlateGirderNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var stiffenerFill = new SolidColorBrush(Color.FromArgb(116, 217, 119, 6));
        var stiffenerPen = new Pen(new SolidColorBrush(Color.FromRgb(180, 83, 9)), 1.0);

        foreach (PlateGirderShellPanel panel in model.ShellPanels.Where(panel => IsStiffener(panel.Group)))
            DrawPanel(dc, panel, nodes, map, stiffenerFill, stiffenerPen);
    }

    private static void DrawPanel(
        DrawingContext dc,
        PlateGirderShellPanel panel,
        IReadOnlyDictionary<string, PlateGirderNode> nodes,
        Func<double, double, Point> map,
        Brush fill,
        Pen pen)
    {
        List<PlateGirderNode> panelNodes = panel.NodeIds
            .Where(nodes.ContainsKey)
            .Select(nodeId => nodes[nodeId])
            .ToList();
        if (panelNodes.Count != 4)
            return;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(map(panelNodes[0].X, panelNodes[0].Z), true, true);
            for (int index = 1; index < panelNodes.Count; index++)
                context.LineTo(map(panelNodes[index].X, panelNodes[index].Z), true, false);
        }
        geometry.Freeze();

        bool degenerateInElevation =
            panelNodes.Max(node => node.X) - panelNodes.Min(node => node.X) < 0.000001 ||
            panelNodes.Max(node => node.Z) - panelNodes.Min(node => node.Z) < 0.000001;

        if (degenerateInElevation)
        {
            Point start = map(panelNodes.Min(node => node.X), panelNodes.Min(node => node.Z));
            Point end = map(panelNodes.Max(node => node.X), panelNodes.Max(node => node.Z));
            dc.DrawLine(new Pen(pen.Brush, Math.Max(2.4, pen.Thickness + 1.2)), start, end);
        }
        else
        {
            dc.DrawGeometry(fill, pen, geometry);
        }
    }

    private static void DrawOpeningOutline(DrawingContext dc, ParametricPlateGirderModel model, Func<double, double, Point> map)
    {
        if (!model.HasWebOpening)
            return;

        double left = model.OriginX + model.OpeningCenterX - model.OpeningWidth / 2.0;
        double right = model.OriginX + model.OpeningCenterX + model.OpeningWidth / 2.0;
        double bottom = model.OriginZ + model.OpeningCenterZ - model.OpeningHeight / 2.0;
        double top = model.OriginZ + model.OpeningCenterZ + model.OpeningHeight / 2.0;
        Point p1 = map(left, top);
        Point p2 = map(right, bottom);
        Rect opening = new(p1, p2);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.8), opening);
    }

    private static void DrawDimensions(DrawingContext dc, ParametricPlateGirderModel model, Func<double, double, Point> map, double originX, double originY, double scale)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1);
        Point start = map(model.OriginX, model.OriginZ);
        Point end = map(model.OriginX + model.Length, model.OriginZ);
        double dimY = originY + 30;
        dc.DrawLine(pen, new Point(start.X, dimY), new Point(end.X, dimY));
        DrawText(dc, $"Length {model.Length:0.###} m", new Point((start.X + end.X) / 2.0 - 42, dimY + 8), 12, Brushes.SlateGray);

        double dimX = originX - 28;
        dc.DrawLine(pen, new Point(dimX, originY), new Point(dimX, originY - model.Depth * scale));
        DrawText(dc, $"Depth {model.Depth:0.###} m", new Point(dimX - 22, originY - model.Depth * scale / 2.0 - 8), 12, Brushes.SlateGray);
    }

    private static void DrawGrid(DrawingContext dc, double x, double y, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(225, 231, 239)), 1);
        for (int index = 0; index <= 5; index++)
        {
            double gx = x + width * index / 5.0;
            double gy = y + height * index / 5.0;
            dc.DrawLine(pen, new Point(gx, y), new Point(gx, y + height));
            dc.DrawLine(pen, new Point(x, gy), new Point(x + width, gy));
        }
    }

    private static bool IsStiffener(PlateGirderShellGroup group)
    {
        return group is PlateGirderShellGroup.OpeningTopStiffener or
            PlateGirderShellGroup.OpeningBottomStiffener or
            PlateGirderShellGroup.OpeningLeftStiffener or
            PlateGirderShellGroup.OpeningRightStiffener;
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
