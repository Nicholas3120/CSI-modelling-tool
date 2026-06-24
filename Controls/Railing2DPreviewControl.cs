using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class Railing2DPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(SteelRailingModel),
            typeof(Railing2DPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SteelRailingModel? Model
    {
        get => (SteelRailingModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        Rect bounds = new(new Point(0, 0), RenderSize);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), null, bounds);

        if (ActualWidth < 80 || ActualHeight < 80)
            return;

        SteelRailingModel? model = Model;
        if (model == null || model.Nodes.Count == 0 || model.Members.Count == 0)
        {
            DrawText(drawingContext, "Preview unavailable", new Point(24, 24), 14, Brushes.DimGray);
            return;
        }

        double minX = model.Nodes.Min(node => node.PreviewX);
        double maxX = model.Nodes.Max(node => node.PreviewX);
        double minZ = Math.Min(0, model.Nodes.Min(node => node.PreviewZ));
        double maxZ = Math.Max(model.RailingHeight, model.Nodes.Max(node => node.PreviewZ));

        if (Math.Abs(maxX - minX) < 0.000001)
            maxX = minX + 1;
        if (Math.Abs(maxZ - minZ) < 0.000001)
            maxZ = minZ + 1;

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        const double left = 74;
        const double right = 36;
        const double top = 78;
        const double bottom = 88;
        double drawableWidth = Math.Max(1, ActualWidth - left - right);
        double drawableHeight = Math.Max(1, ActualHeight - top - bottom);
        double scale = Math.Min(drawableWidth / (maxX - minX), drawableHeight / (maxZ - minZ));
        double usedWidth = (maxX - minX) * scale;
        double usedHeight = (maxZ - minZ) * scale;
        double originX = left + (drawableWidth - usedWidth) / 2.0;
        double originY = top + (drawableHeight - usedHeight) / 2.0;

        Point Map(SteelRailingNode node)
        {
            double x = originX + (node.PreviewX - minX) * scale;
            double y = originY + (maxZ - node.PreviewZ) * scale;
            return new Point(x, y);
        }

        DrawGrid(drawingContext, originX, originY, usedWidth, usedHeight);
        DrawMembers(drawingContext, model, nodes, Map);
        DrawLoads(drawingContext, model, nodes, Map);
        DrawNodes(drawingContext, model, Map);
        DrawDimensions(drawingContext, model, originX, originY, usedWidth, usedHeight);
        DrawLegend(drawingContext, ActualWidth);
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

    private static void DrawMembers(
        DrawingContext dc,
        SteelRailingModel model,
        Dictionary<string, SteelRailingNode> nodes,
        Func<SteelRailingNode, Point> map)
    {
        foreach (SteelRailingMember member in model.Members)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out SteelRailingNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out SteelRailingNode? end))
            {
                continue;
            }

            var pen = new Pen(new SolidColorBrush(ColorForGroup(member.Group)), member.Group == SteelRailingMemberGroups.Post ? 3.0 : 2.6);
            if (string.IsNullOrWhiteSpace(member.SectionName))
                pen.DashStyle = DashStyles.Dash;

            dc.DrawLine(pen, map(start), map(end));
        }
    }

    private static void DrawLoads(
        DrawingContext dc,
        SteelRailingModel model,
        Dictionary<string, SteelRailingNode> nodes,
        Func<SteelRailingNode, Point> map)
    {
        if (model.Loads.Count == 0)
            return;

        var loadPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.7);
        SteelRailingLoad load = model.Loads[0];

        if (load.LoadType == RailingLoadType.PointLoad)
        {
            foreach (string nodeId in load.TargetNodeIds)
            {
                if (nodes.TryGetValue(nodeId, out SteelRailingNode? node))
                    DrawLoadArrow(dc, map(node), loadPen);
            }

            string direction = load.Direction == RailingLoadDirection.GlobalX ? "Global X" : "Global Y";
            DrawBadge(dc, $"Post point load {load.MagnitudeKn:0.###} kN at {load.PointHeight:0.###} m, {direction}", new Point(18, 46));
            return;
        }

        IEnumerable<SteelRailingMember> targetMembers = model.Members
            .Where(member => string.Equals(member.Group, load.TargetGroup, StringComparison.OrdinalIgnoreCase));

        foreach (SteelRailingMember member in targetMembers)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out SteelRailingNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out SteelRailingNode? end))
            {
                continue;
            }

            Point startPoint = map(start);
            Point endPoint = map(end);
            for (int index = 1; index <= 3; index++)
            {
                double t = index / 4.0;
                Point target = new(
                    startPoint.X + (endPoint.X - startPoint.X) * t,
                    startPoint.Y + (endPoint.Y - startPoint.Y) * t);
                DrawLoadArrow(dc, target, loadPen);
            }
        }

        string lineDirection = load.Direction == RailingLoadDirection.GlobalX ? "Global X" : "Global Y";
        DrawBadge(dc, $"{LoadTargetLabel(load.TargetGroup)} line load {load.MagnitudeKnPerM:0.###} kN/m, {lineDirection}", new Point(18, 46));
    }

    private static void DrawLoadArrow(DrawingContext dc, Point targetPoint, Pen pen)
    {
        Point tail = new(targetPoint.X, targetPoint.Y - 40);
        Point head = new(targetPoint.X, targetPoint.Y - 8);

        dc.DrawLine(pen, tail, head);
        dc.DrawLine(pen, head, new Point(head.X - 5, head.Y - 7));
        dc.DrawLine(pen, head, new Point(head.X + 5, head.Y - 7));
    }

    private static void DrawNodes(DrawingContext dc, SteelRailingModel model, Func<SteelRailingNode, Point> map)
    {
        var nodeBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var supportBrush = new SolidColorBrush(Color.FromRgb(14, 116, 144));
        var loadBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        var nodePen = new Pen(Brushes.White, 1.2);

        foreach (SteelRailingNode node in model.Nodes)
        {
            Point point = map(node);
            Brush brush = node.IsBaseNode ? supportBrush : node.IsLoadReferenceNode ? loadBrush : nodeBrush;
            double radius = node.IsBaseNode || node.IsLoadReferenceNode ? 4.6 : 3.6;
            dc.DrawEllipse(brush, nodePen, point, radius, radius);

            if (node.IsBaseNode)
                DrawSupport(dc, point);
        }
    }

    private static void DrawSupport(DrawingContext dc, Point point)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(point.X, point.Y + 8), true, true);
            context.LineTo(new Point(point.X - 9, point.Y + 23), true, false);
            context.LineTo(new Point(point.X + 9, point.Y + 23), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(60, 14, 116, 144)), new Pen(new SolidColorBrush(Color.FromRgb(14, 116, 144)), 1.4), geometry);
    }

    private static void DrawDimensions(DrawingContext dc, SteelRailingModel model, double x, double y, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1);
        double dimY = y + height + 32;
        dc.DrawLine(pen, new Point(x, dimY), new Point(x + width, dimY));
        dc.DrawLine(pen, new Point(x, dimY - 6), new Point(x, dimY + 6));
        dc.DrawLine(pen, new Point(x + width, dimY - 6), new Point(x + width, dimY + 6));
        DrawText(dc, $"Length {model.Length:0.###} m", new Point(x + width / 2 - 42, dimY + 8), 12, Brushes.SlateGray);

        double dimX = x - 28;
        dc.DrawLine(pen, new Point(dimX, y), new Point(dimX, y + height));
        dc.DrawLine(pen, new Point(dimX - 6, y), new Point(dimX + 6, y));
        dc.DrawLine(pen, new Point(dimX - 6, y + height), new Point(dimX + 6, y + height));
        DrawText(dc, $"Height {model.RailingHeight:0.###} m", new Point(dimX - 20, y + height / 2.0 - 8), 12, Brushes.SlateGray);

        DrawText(dc, $"{model.RailingId} / {model.SpanCount} spans / {model.PostCount} posts / X-Z plane", new Point(18, 12), 13, Brushes.DimGray);
    }

    private static void DrawLegend(DrawingContext dc, double actualWidth)
    {
        string[] groups =
        [
            SteelRailingMemberGroups.Post,
            SteelRailingMemberGroups.TopRail,
            SteelRailingMemberGroups.MidRail,
            SteelRailingMemberGroups.BottomRail
        ];

        double x = 18;
        double y = 72;
        double itemWidth = 104;
        double backgroundWidth = Math.Min(Math.Max(actualWidth - 36, 1), itemWidth * groups.Length + 10);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(216, 247, 249, 252)), null, new Rect(x - 6, y - 6, backgroundWidth, 28));

        foreach (string group in groups)
        {
            var pen = new Pen(new SolidColorBrush(ColorForGroup(group)), 2.6);
            dc.DrawLine(pen, new Point(x, y + 8), new Point(x + 22, y + 8));
            DrawText(dc, LegendLabel(group), new Point(x + 30, y), 11, Brushes.DimGray);
            x += itemWidth;
        }
    }

    private static string LegendLabel(string group)
    {
        return group switch
        {
            SteelRailingMemberGroups.TopRail => "Top",
            SteelRailingMemberGroups.MidRail => "Mid",
            SteelRailingMemberGroups.BottomRail => "Bottom",
            _ => "Post"
        };
    }

    private static string LoadTargetLabel(string group)
    {
        return string.Equals(group, SteelRailingMemberGroups.Post, StringComparison.OrdinalIgnoreCase)
            ? "Post"
            : "Top rail";
    }

    private static Color ColorForGroup(string group)
    {
        return group switch
        {
            SteelRailingMemberGroups.Post => Color.FromRgb(5, 150, 105),
            SteelRailingMemberGroups.TopRail => Color.FromRgb(37, 99, 235),
            SteelRailingMemberGroups.MidRail => Color.FromRgb(71, 85, 105),
            SteelRailingMemberGroups.BottomRail => Color.FromRgb(217, 119, 6),
            _ => Color.FromRgb(30, 41, 59)
        };
    }

    private static void DrawText(DrawingContext dc, string text, Point point, double fontSize, Brush brush)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        FormattedText formattedText = CreateFormattedText(text, fontSize, brush, pixelsPerDip);
        dc.DrawText(formattedText, point);
    }

    private static void DrawBadge(DrawingContext dc, string text, Point point)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        FormattedText formattedText = CreateFormattedText(text, 11.5, new SolidColorBrush(Color.FromRgb(127, 29, 29)), pixelsPerDip);
        Rect background = new(point.X - 6, point.Y - 3, formattedText.Width + 12, formattedText.Height + 6);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(228, 254, 242, 242)), new Pen(new SolidColorBrush(Color.FromRgb(254, 202, 202)), 1), background, 4, 4);
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
