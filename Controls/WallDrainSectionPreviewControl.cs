using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class WallDrainSectionPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(WallDrainModel),
            typeof(WallDrainSectionPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public WallDrainModel? Model
    {
        get => (WallDrainModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 249, 252)), null, new Rect(0, 0, ActualWidth, ActualHeight));

        WallDrainModel? model = Model;
        if (ActualWidth < 80 || ActualHeight < 80 || model == null || model.Nodes.Count == 0 || (model.FrameMembers.Count == 0 && model.ShellPanels.Count == 0))
        {
            DrawText(drawingContext, "Wall/drain preview unavailable", new Point(18, 18), 13, Brushes.DimGray);
            return;
        }

        double minX = model.Nodes.Min(node => node.X);
        double maxX = model.Nodes.Max(node => node.X);
        double minZ = model.Nodes.Min(node => node.Z);
        double maxZ = model.Nodes.Max(node => node.Z);
        if (Math.Abs(maxX - minX) < 0.000001)
        {
            minX -= 0.5;
            maxX += 0.5;
        }
        if (Math.Abs(maxZ - minZ) < 0.000001)
            maxZ = minZ + 1.0;

        const double left = 58;
        const double right = 30;
        const double top = 58;
        const double bottom = 58;
        double drawWidth = Math.Max(1, ActualWidth - left - right);
        double drawHeight = Math.Max(1, ActualHeight - top - bottom);
        double scale = Math.Min(drawWidth / (maxX - minX), drawHeight / (maxZ - minZ));
        double usedWidth = (maxX - minX) * scale;
        double usedHeight = (maxZ - minZ) * scale;
        double originX = left + (drawWidth - usedWidth) / 2.0;
        double originY = top + (drawHeight - usedHeight) / 2.0;

        Point Map(double x, double z)
        {
            return new Point(
                originX + (x - minX) * scale,
                originY + (maxZ - z) * scale);
        }

        DrawGrid(drawingContext, originX, originY, usedWidth, usedHeight);
        DrawSectionPanels(drawingContext, model, Map);
        DrawLoads(drawingContext, model, Map);
        DrawDimensions(drawingContext, model, originX, originY, usedWidth, usedHeight);
        DrawText(drawingContext, $"{model.StructureId} / {FormatShape(model.ShapeMode)} / X-Z section", new Point(18, 12), 13, Brushes.DimGray);
        DrawText(drawingContext, $"Nodes {model.Nodes.Count}  Frames {model.FrameMembers.Count}  Shells {model.ShellPanels.Count}  Loads {model.SurfaceLoads.Count}", new Point(18, ActualHeight - 28), 12, Brushes.SlateGray);
    }

    private static void DrawSectionPanels(DrawingContext dc, WallDrainModel model, Func<double, double, Point> map)
    {
        var nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        if (model.FrameMembers.Count > 0)
        {
            foreach (WallDrainFrameMember member in model.FrameMembers)
            {
                if (!nodes.TryGetValue(member.StartNodeId, out WallDrainNode? start) ||
                    !nodes.TryGetValue(member.EndNodeId, out WallDrainNode? end))
                {
                    continue;
                }

                var pen = new Pen(new SolidColorBrush(ColorForGroup(member.Group)), IsVerticalGroup(member.Group) ? 2.8 : 3.2);
                if (string.IsNullOrWhiteSpace(member.SectionName))
                    pen.DashStyle = DashStyles.Dash;

                dc.DrawLine(pen, map(start.X, start.Z), map(end.X, end.Z));
            }

            return;
        }

        var drawnEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WallDrainShellPanel panel in model.ShellPanels)
        {
            List<WallDrainNode> panelNodes = panel.NodeIds
                .Where(nodes.ContainsKey)
                .Select(nodeId => nodes[nodeId])
                .ToList();
            if (panelNodes.Count < 3)
                continue;

            var pen = new Pen(new SolidColorBrush(ColorForGroup(panel.Group)), IsVerticalGroup(panel.Group) ? 2.2 : 2.6);
            if (string.IsNullOrWhiteSpace(panel.ShellPropertyName))
                pen.DashStyle = DashStyles.Dash;

            for (int index = 0; index < panelNodes.Count; index++)
            {
                WallDrainNode start = panelNodes[index];
                WallDrainNode end = panelNodes[(index + 1) % panelNodes.Count];
                string edgeKey = BuildEdgeKey(start, end);
                if (!drawnEdges.Add(edgeKey))
                    continue;

                dc.DrawLine(pen, map(start.X, start.Z), map(end.X, end.Z));
            }
        }
    }

    private static void DrawLoads(DrawingContext dc, WallDrainModel model, Func<double, double, Point> map)
    {
        if (model.SurfaceLoads.Count == 0)
            return;

        double leftX = model.Nodes.Min(node => node.X);
        double rightX = model.Nodes.Max(node => node.X);
        double minZ = model.Nodes.Min(node => node.Z);
        double maxZ = model.Nodes.Max(node => node.Z);
        double height = Math.Max(maxZ - minZ, 0.000001);
        var loadPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.6);
        foreach (WallDrainSurfaceLoad load in model.SurfaceLoads)
        {
            if (load.Kind == WallDrainLoadKind.Triangular)
            {
                DrawLoadArrow(dc, map(leftX - 0.12, minZ + height * 0.25), true, loadPen);
                DrawLoadArrow(dc, map(leftX - 0.12, minZ + height * 0.55), true, loadPen);
                DrawLoadArrow(dc, map(leftX - 0.12, minZ + height * 0.85), true, loadPen, 18);
            }
            else
            {
                DrawLoadArrow(dc, map(rightX + 0.12, minZ + height * 0.35), false, loadPen);
                DrawLoadArrow(dc, map(rightX + 0.12, minZ + height * 0.65), false, loadPen);
            }
        }

        string summary = string.Join(" + ", model.SurfaceLoads.Select(load => load.Kind == WallDrainLoadKind.Triangular ? "triangular" : "UDL"));
        DrawBadge(dc, $"Loads: {summary}", new Point(18, 38));
    }

    private static void DrawLoadArrow(DrawingContext dc, Point target, bool leftToRight, Pen pen, double length = 30)
    {
        double sign = leftToRight ? 1 : -1;
        Point tail = new(target.X - sign * length, target.Y);
        Point head = new(target.X - sign * 5, target.Y);
        dc.DrawLine(pen, tail, head);
        dc.DrawLine(pen, head, new Point(head.X - sign * 7, head.Y - 5));
        dc.DrawLine(pen, head, new Point(head.X - sign * 7, head.Y + 5));
    }

    private static void DrawDimensions(DrawingContext dc, WallDrainModel model, double x, double y, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1);
        double dimY = y + height + 28;
        dc.DrawLine(pen, new Point(x, dimY), new Point(x + width, dimY));
        dc.DrawLine(pen, new Point(x, dimY - 5), new Point(x, dimY + 5));
        dc.DrawLine(pen, new Point(x + width, dimY - 5), new Point(x + width, dimY + 5));
        DrawText(dc, $"Length Y {model.LengthY:0.###} m", new Point(x + width / 2 - 44, dimY + 8), 12, Brushes.SlateGray);

        double dimX = x - 26;
        dc.DrawLine(pen, new Point(dimX, y), new Point(dimX, y + height));
        dc.DrawLine(pen, new Point(dimX - 5, y), new Point(dimX + 5, y));
        dc.DrawLine(pen, new Point(dimX - 5, y + height), new Point(dimX + 5, y + height));
        DrawText(dc, $"Height {model.Height:0.###} m", new Point(dimX - 20, y + height / 2.0 - 8), 12, Brushes.SlateGray);
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

    private static bool IsVerticalGroup(string group)
    {
        return WallDrainPanelGroups.VerticalWallGroups.Contains(group);
    }

    private static string FormatShape(WallDrainShapeMode shape)
    {
        return shape switch
        {
            WallDrainShapeMode.OneSidedWall => "1-sided wall",
            WallDrainShapeMode.LWall => "L wall",
            WallDrainShapeMode.UDrain => "U drain",
            WallDrainShapeMode.BoxDrain => "Box drain",
            _ => shape.ToString()
        };
    }

    private static Color ColorForGroup(string group)
    {
        return group switch
        {
            WallDrainPanelGroups.BaseSlab or WallDrainPanelGroups.TopSlab => Color.FromRgb(71, 85, 105),
            WallDrainPanelGroups.Buttress or WallDrainPanelGroups.Counterfort => Color.FromRgb(217, 119, 6),
            WallDrainPanelGroups.RightWall => Color.FromRgb(124, 58, 237),
            _ => Color.FromRgb(37, 99, 235)
        };
    }

    private static string BuildEdgeKey(WallDrainNode start, WallDrainNode end)
    {
        string a = $"{Math.Round(start.X, 5):0.#####},{Math.Round(start.Z, 5):0.#####}";
        string b = $"{Math.Round(end.X, 5):0.#####},{Math.Round(end.Z, 5):0.#####}";
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
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

    private static void DrawBadge(DrawingContext dc, string text, Point point)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11.5,
            new SolidColorBrush(Color.FromRgb(127, 29, 29)),
            pixelsPerDip);

        Rect background = new(point.X - 6, point.Y - 3, formattedText.Width + 12, formattedText.Height + 6);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(228, 254, 242, 242)), new Pen(new SolidColorBrush(Color.FromRgb(254, 202, 202)), 1), background, 4, 4);
        dc.DrawText(formattedText, point);
    }
}
