using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class CityOfTomorrowPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(CityOfTomorrowModel), typeof(CityOfTomorrowPreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public CityOfTomorrowModel? Model
    {
        get => (CityOfTomorrowModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 252)), null, new Rect(new Point(), RenderSize));
        CityOfTomorrowModel? model = Model;
        if (model == null || model.Nodes.Count == 0 || ActualWidth < 100 || ActualHeight < 100)
        {
            DrawText(dc, "Preview unavailable", new Point(20, 20), 14, Brushes.SlateGray);
            return;
        }

        double minX = model.Nodes.Min(n => n.X), maxX = model.Nodes.Max(n => n.X);
        double minZ = model.Nodes.Min(n => n.Z), maxZ = model.Nodes.Max(n => n.Z);
        const double left = 50, right = 30, top = 55, bottom = 70;
        double width = Math.Max(1, ActualWidth - left - right), height = Math.Max(1, ActualHeight - top - bottom);
        double scale = Math.Min(width / Math.Max(maxX - minX, 1), height / Math.Max(maxZ - minZ, 1));
        double usedWidth = (maxX - minX) * scale, usedHeight = (maxZ - minZ) * scale;
        double ox = left + (width - usedWidth) / 2, oy = top + (height - usedHeight) / 2;
        var nodes = model.Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
        Point Map(CityNode n) => new(ox + (n.X - minX) * scale, oy + (maxZ - n.Z) * scale);

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
        for (int i = 0; i <= 6; i++)
        {
            double gx = ox + usedWidth * i / 6, gy = oy + usedHeight * i / 6;
            dc.DrawLine(gridPen, new Point(gx, oy), new Point(gx, oy + usedHeight));
            dc.DrawLine(gridPen, new Point(ox, gy), new Point(ox + usedWidth, gy));
        }

        CityNode? centreBottom = model.Nodes.OrderBy(n => n.Z).FirstOrDefault(n => Math.Abs(n.X) < 0.000001);
        CityNode? centreTop = model.Nodes.OrderByDescending(n => n.Z).FirstOrDefault(n => Math.Abs(n.X) < 0.000001);
        if (centreBottom != null && centreTop != null)
            dc.DrawLine(new Pen(Brushes.LightSlateGray, 1) { DashStyle = DashStyles.Dash }, Map(centreBottom), Map(centreTop));

        foreach (CityMember member in model.Members.OrderBy(m => m.IsTensionOnly ? 1 : 0))
        {
            if (!nodes.TryGetValue(member.StartNodeKey, out CityNode? start) || !nodes.TryGetValue(member.EndNodeKey, out CityNode? end)) continue;
            var pen = new Pen(new SolidColorBrush(ColorFor(member)), member.IsTensionOnly ? 2 : 2.7)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (string.IsNullOrWhiteSpace(member.SectionName)) pen.DashStyle = DashStyles.Dash;
            dc.DrawLine(pen, Map(start), Map(end));
        }

        foreach (CityNode node in model.Nodes.Where(n => n.IsPrimaryJoint || n.IsSupport))
        {
            Point p = Map(node);
            dc.DrawEllipse(node.IsSupport ? Brushes.DarkSlateBlue : Brushes.White, new Pen(Brushes.SlateGray, 1), p, node.IsSupport ? 4.5 : 3, node.IsSupport ? 4.5 : 3);
        }

        DrawText(dc, $"{model.StructureId} | {model.TotalPanels} panels | panel width {model.PanelWidth:0.###} m", new Point(18, 12), 13, Brushes.SlateGray);
        DrawText(dc, "Frame", new Point(22, ActualHeight - 38), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 85)), 3), new Point(72, ActualHeight - 30), new Point(106, ActualHeight - 30));
        DrawText(dc, "Tension-only cable", new Point(122, ActualHeight - 38), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2.2), new Point(228, ActualHeight - 30), new Point(262, ActualHeight - 30));
        DrawText(dc, "Global tie", new Point(278, ActualHeight - 38), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 2.5), new Point(336, ActualHeight - 30), new Point(370, ActualHeight - 30));
    }

    private static Color ColorFor(CityMember member)
    {
        if (member.Group == CityMemberGroups.GlobalTie) return Color.FromRgb(220, 38, 38);
        if (member.IsTensionOnly) return Color.FromRgb(37, 99, 235);
        if (member.Group == CityMemberGroups.MidRail) return Color.FromRgb(220, 38, 38);
        if (member.Group == CityMemberGroups.VerticalPost) return Color.FromRgb(5, 150, 105);
        return Color.FromRgb(51, 65, 85);
    }

    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush)
    {
        double dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
    }
}
