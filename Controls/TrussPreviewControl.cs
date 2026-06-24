using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TrussModelling.Models;

namespace TrussModelling.Controls;

public sealed class TrussPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ParametricTrussModel),
            typeof(TrussPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ParametricTrussModel? Model
    {
        get => (ParametricTrussModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        Rect bounds = new(new Point(0, 0), RenderSize);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), null, bounds);

        if (ActualWidth < 80 || ActualHeight < 80)
            return;

        ParametricTrussModel? model = Model;
        if (model == null || model.Nodes.Count == 0 || (model.Members.Count == 0 && model.Shells.Count == 0))
        {
            DrawText(drawingContext, "Preview unavailable", new Point(24, 24), 14, Brushes.DimGray);
            return;
        }

        double minX = model.Nodes.Min(node => node.PreviewX);
        double maxX = model.Nodes.Max(node => node.PreviewX);
        double minZ = Math.Min(0, model.Nodes.Min(node => node.PreviewZ));
        double maxZ = Math.Max(1, model.Nodes.Max(node => node.PreviewZ));

        if (Math.Abs(maxX - minX) < 0.000001)
            maxX = minX + 1;
        if (Math.Abs(maxZ - minZ) < 0.000001)
            maxZ = minZ + 1;

        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        List<LoadPreviewSummary> loadSummaries = BuildLoadSummaries(model, nodes);
        bool hasLoadPreview = loadSummaries.Count > 0;
        const double left = 78;
        const double right = 32;
        double top = hasLoadPreview ? Math.Max(112, 84 + loadSummaries.Count * 20) : 72;
        const double bottom = 84;
        double drawableWidth = Math.Max(1, ActualWidth - left - right);
        double drawableHeight = Math.Max(1, ActualHeight - top - bottom);

        double scaleX = drawableWidth / (maxX - minX);
        double scaleY = drawableHeight / (maxZ - minZ);
        double scale = Math.Min(scaleX, scaleY);
        double usedWidth = (maxX - minX) * scale;
        double usedHeight = (maxZ - minZ) * scale;
        double originX = left + (drawableWidth - usedWidth) / 2.0;
        double originY = top + (drawableHeight - usedHeight) / 2.0;

        Point Map(ParametricNode node)
        {
            double x = originX + (node.PreviewX - minX) * scale;
            double y = originY + (maxZ - node.PreviewZ) * scale;
            return new Point(x, y);
        }

        DrawGrid(drawingContext, originX, originY, usedWidth, usedHeight);
        DrawShells(drawingContext, model, nodes, Map);
        DrawMembers(drawingContext, model, nodes, Map);
        DrawLoads(drawingContext, model, nodes, Map);
        DrawNodes(drawingContext, model, Map);
        DrawDimensions(drawingContext, model, originX, originY, usedWidth, usedHeight);
        DrawLegend(drawingContext, ActualWidth, model);
        DrawLoadLabels(drawingContext, loadSummaries);
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

    private static void DrawShells(
        DrawingContext dc,
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodes,
        Func<ParametricNode, Point> map)
    {
        foreach (ParametricShell shell in model.Shells)
        {
            List<Point> points = shell.NodeIds
                .Where(nodes.ContainsKey)
                .Select(nodeId => map(nodes[nodeId]))
                .ToList();

            if (points.Count < 3)
                continue;

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                foreach (Point point in points.Skip(1))
                    context.LineTo(point, true, false);
            }

            geometry.Freeze();
            Color color = ColorForGroup(shell.Group);
            var fill = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)), 1.4);
            if (string.IsNullOrWhiteSpace(shell.ShellPropertyName))
                pen.DashStyle = DashStyles.Dash;

            dc.DrawGeometry(fill, pen, geometry);
        }
    }

    private static void DrawMembers(
        DrawingContext dc,
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodes,
        Func<ParametricNode, Point> map)
    {
        foreach (ParametricMember member in model.Members)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out ParametricNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out ParametricNode? end))
            {
                continue;
            }

            Color color = ColorForGroup(member.Group);
            var pen = new Pen(new SolidColorBrush(color), member.Group == ParametricMemberGroups.Diagonal ? 2.0 : 2.6);
            if (string.IsNullOrWhiteSpace(member.SectionName))
                pen.DashStyle = DashStyles.Dash;

            dc.DrawLine(pen, map(start), map(end));
        }
    }

    private static void DrawLoads(
        DrawingContext dc,
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodes,
        Func<ParametricNode, Point> map)
    {
        var loadPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.7);
        foreach (ParametricLoad load in model.Loads.Where(load => load.TargetType.Equals("Node", StringComparison.OrdinalIgnoreCase)))
        {
            if (!nodes.TryGetValue(load.TargetId, out ParametricNode? node))
                continue;

            Point nodePoint = map(node);
            DrawLoadArrow(dc, nodePoint, load.Magnitude, loadPen);
        }

        foreach (ParametricLoad load in model.Loads.Where(load => load.TargetType.Equals("MemberGroup", StringComparison.OrdinalIgnoreCase)))
        {
            IEnumerable<ParametricMember> targetMembers = model.Members
                .Where(member => string.Equals(member.Group, load.TargetId, StringComparison.OrdinalIgnoreCase));

            foreach (ParametricMember member in targetMembers)
            {
                if (!nodes.TryGetValue(member.StartNodeId, out ParametricNode? start) ||
                    !nodes.TryGetValue(member.EndNodeId, out ParametricNode? end))
                {
                    continue;
                }

                Point startPoint = map(start);
                Point endPoint = map(end);
                for (int index = 1; index <= 3; index++)
                {
                    double t = index / 4.0;
                    Point point = new(
                        startPoint.X + (endPoint.X - startPoint.X) * t,
                        startPoint.Y + (endPoint.Y - startPoint.Y) * t);
                    DrawLoadArrow(dc, point, load.Magnitude, loadPen);
                }
            }
        }
    }

    private static void DrawLoadArrow(DrawingContext dc, Point targetPoint, double magnitude, Pen pen)
    {
        double direction = magnitude < 0 ? 1 : -1;
        Point tail = new(targetPoint.X, targetPoint.Y - direction * 42);
        Point head = new(targetPoint.X, targetPoint.Y - direction * 8);

        dc.DrawLine(pen, tail, head);
        dc.DrawLine(pen, head, new Point(head.X - 5, head.Y - direction * 7));
        dc.DrawLine(pen, head, new Point(head.X + 5, head.Y - direction * 7));
    }

    private static List<LoadPreviewSummary> BuildLoadSummaries(ParametricTrussModel model, Dictionary<string, ParametricNode> nodes)
    {
        var summaries = new List<LoadPreviewSummary>();
        double span = Math.Max(model.Span, 0.000001);

        AddNodeLoadSummary(summaries, model, nodes, true, span);
        AddMemberGroupLoadSummary(summaries, model, ParametricMemberGroups.TopChord, "Top");
        AddNodeLoadSummary(summaries, model, nodes, false, span);
        AddMemberGroupLoadSummary(summaries, model, ParametricMemberGroups.BottomChord, "Bottom");

        return summaries;
    }

    private static void AddNodeLoadSummary(
        List<LoadPreviewSummary> summaries,
        ParametricTrussModel model,
        Dictionary<string, ParametricNode> nodes,
        bool topChord,
        double span)
    {
        List<ParametricLoad> loads = model.Loads
            .Where(load => load.TargetType.Equals("Node", StringComparison.OrdinalIgnoreCase))
            .Where(load => nodes.TryGetValue(load.TargetId, out ParametricNode? node) && (topChord ? node.IsTopChord : node.IsBottomChord))
            .ToList();

        if (loads.Count == 0)
            return;

        double equivalentKnPerM = loads.Sum(load => Math.Abs(load.Magnitude)) / span;
        summaries.Add(new LoadPreviewSummary(
            topChord ? "Top" : "Bottom",
            "panel nodes",
            equivalentKnPerM,
            loads.FirstOrDefault()?.LoadPattern ?? ""));
    }

    private static void AddMemberGroupLoadSummary(
        List<LoadPreviewSummary> summaries,
        ParametricTrussModel model,
        string memberGroup,
        string label)
    {
        List<ParametricLoad> loads = model.Loads
            .Where(load => load.TargetType.Equals("MemberGroup", StringComparison.OrdinalIgnoreCase))
            .Where(load => string.Equals(load.TargetId, memberGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (ParametricLoad load in loads)
        {
            summaries.Add(new LoadPreviewSummary(
                label,
                "chord UDL",
                Math.Abs(load.Magnitude),
                load.LoadPattern));
        }
    }

    private static void DrawLoadLabels(DrawingContext dc, IReadOnlyList<LoadPreviewSummary> summaries)
    {
        if (summaries.Count == 0)
            return;

        double x = 18;
        double y = 72;
        foreach (LoadPreviewSummary summary in summaries)
        {
            string pattern = string.IsNullOrWhiteSpace(summary.LoadPattern) ? "" : $" ({summary.LoadPattern})";
            DrawBadge(dc, $"{summary.ChordLabel}: {summary.MagnitudeKnPerM:0.###} kN/m -> {summary.ModeLabel}{pattern}", new Point(x, y));
            y += 20;
        }
    }

    private static void DrawNodes(DrawingContext dc, ParametricTrussModel model, Func<ParametricNode, Point> map)
    {
        var nodeBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        var supportBrush = new SolidColorBrush(Color.FromRgb(14, 116, 144));
        var nodePen = new Pen(Brushes.White, 1.2);

        foreach (ParametricNode node in model.Nodes)
        {
            Point point = map(node);
            dc.DrawEllipse(node.IsSupport ? supportBrush : nodeBrush, nodePen, point, 4.5, 4.5);

            if (node.IsSupport)
                DrawSupport(dc, point);
        }
    }

    private static void DrawSupport(DrawingContext dc, Point point)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(point.X, point.Y + 8), true, true);
            context.LineTo(new Point(point.X - 10, point.Y + 24), true, false);
            context.LineTo(new Point(point.X + 10, point.Y + 24), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(60, 14, 116, 144)), new Pen(new SolidColorBrush(Color.FromRgb(14, 116, 144)), 1.4), geometry);
    }

    private static void DrawDimensions(DrawingContext dc, ParametricTrussModel model, double x, double y, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1);
        double dimY = y + height + 30;
        dc.DrawLine(pen, new Point(x, dimY), new Point(x + width, dimY));
        dc.DrawLine(pen, new Point(x, dimY - 6), new Point(x, dimY + 6));
        dc.DrawLine(pen, new Point(x + width, dimY - 6), new Point(x + width, dimY + 6));
        DrawText(dc, $"Span {model.Span:0.###} m", new Point(x + width / 2 - 42, dimY + 8), 12, Brushes.SlateGray);

        double dimX = x - 28;
        dc.DrawLine(pen, new Point(dimX, y), new Point(dimX, y + height));
        dc.DrawLine(pen, new Point(dimX - 6, y), new Point(dimX + 6, y));
        dc.DrawLine(pen, new Point(dimX - 6, y + height), new Point(dimX + 6, y + height));
        DrawText(dc, $"Height {model.Height:0.###} m", new Point(dimX - 20, y + height / 2.0 - 8), 12, Brushes.SlateGray);

        DrawText(dc, $"{model.TrussId} / {model.TrussType} / {model.PanelCount} panels", new Point(18, 10), 13, Brushes.DimGray);
    }

    private static void DrawLegend(DrawingContext dc, double actualWidth, ParametricTrussModel model)
    {
        List<string> groups = model.Members
            .Select(member => member.Group)
            .Concat(model.Shells.Select(shell => shell.Group))
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        if (groups.Count == 0)
            return;

        double x = 18;
        double y = 40;
        double itemWidth = 112;
        double backgroundWidth = Math.Min(Math.Max(actualWidth - 36, 1), itemWidth * groups.Count + 10);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(216, 247, 249, 252)), null, new Rect(x - 6, y - 6, backgroundWidth, 28));

        foreach (string group in groups)
        {
            var pen = new Pen(new SolidColorBrush(ColorForGroup(group)), 2.5);
            dc.DrawLine(pen, new Point(x, y + 8), new Point(x + 22, y + 8));
            DrawText(dc, group, new Point(x + 30, y), 11, Brushes.DimGray);
            x += itemWidth;
        }
    }

    private static Color ColorForGroup(string group)
    {
        return group switch
        {
            ParametricMemberGroups.TopChord => Color.FromRgb(37, 99, 235),
            ParametricMemberGroups.BottomChord => Color.FromRgb(71, 85, 105),
            ParametricMemberGroups.Diagonal => Color.FromRgb(220, 38, 38),
            ParametricMemberGroups.Vertical => Color.FromRgb(5, 150, 105),
            ParametricMemberGroups.EndPost => Color.FromRgb(124, 58, 237),
            ParametricMemberGroups.Secondary => Color.FromRgb(217, 119, 6),
            ParametricMemberGroups.InnerStringer => Color.FromRgb(37, 99, 235),
            ParametricMemberGroups.OuterStringer => Color.FromRgb(5, 150, 105),
            ParametricMemberGroups.RadialTread => Color.FromRgb(217, 119, 6),
            ParametricMemberGroups.CentralColumn => Color.FromRgb(124, 58, 237),
            ParametricMemberGroups.LandingBeam => Color.FromRgb(14, 116, 144),
            "TreadShell" => Color.FromRgb(245, 158, 11),
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

    private readonly record struct LoadPreviewSummary(string ChordLabel, string ModeLabel, double MagnitudeKnPerM, string LoadPattern);
}
