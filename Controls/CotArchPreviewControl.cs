using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class CotArchPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(CotArchModel),
        typeof(CotArchPreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public CotArchModel? Model
    {
        get => (CotArchModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 252)), null, new Rect(new Point(), RenderSize));

        CotArchModel? model = Model;
        if (model == null || model.Nodes.Count == 0 || ActualWidth < 100 || ActualHeight < 100)
        {
            DrawText(drawingContext, "CoT Arch preview unavailable", new Point(20, 20), 14, Brushes.SlateGray);
            return;
        }

        double minX = model.Nodes.Min(node => node.X);
        double maxX = model.Nodes.Max(node => node.X);
        double minZ = model.Nodes.Min(node => node.Z);
        double maxZ = model.Nodes.Max(node => node.Z);
        const double left = 58;
        const double right = 34;
        const double top = 58;
        const double bottom = 72;
        double drawWidth = Math.Max(1, ActualWidth - left - right);
        double drawHeight = Math.Max(1, ActualHeight - top - bottom);
        double scale = Math.Min(drawWidth / Math.Max(maxX - minX, 1), drawHeight / Math.Max(maxZ - minZ, 1));
        double usedWidth = (maxX - minX) * scale;
        double usedHeight = (maxZ - minZ) * scale;
        double ox = left + (drawWidth - usedWidth) / 2.0;
        double oy = top + (drawHeight - usedHeight) / 2.0;
        Dictionary<string, CotArchNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);

        Point Map(CotArchNode node) => new(ox + (node.X - minX) * scale, oy + (maxZ - node.Z) * scale);

        DrawGrid(drawingContext, ox, oy, usedWidth, usedHeight);

        foreach (CotArchMember member in model.Members.OrderBy(MemberDrawOrder))
        {
            if (!nodes.TryGetValue(member.StartNodeId, out CotArchNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out CotArchNode? end))
            {
                continue;
            }

            Pen pen = new(new SolidColorBrush(ColorFor(member.Kind)), StrokeFor(member.Kind))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            if (string.IsNullOrWhiteSpace(member.SectionName))
                pen.DashStyle = DashStyles.Dash;

            drawingContext.DrawLine(pen, Map(start), Map(end));
        }

        foreach (CotArchNode node in model.Nodes.Where(node => node.IsArchNode || node.IsPostTop || node.IsSupportBase))
        {
            Point point = Map(node);
            Brush fill = node.IsSupportBase ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) :
                node.IsSpringing ? Brushes.White :
                node.IsPostTop ? new SolidColorBrush(Color.FromRgb(236, 72, 153)) :
                Brushes.White;
            Pen pen = new(node.IsSpringing ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : Brushes.SlateGray, node.IsSpringing ? 2.1 : 1);
            double radius = node.IsSpringing ? 5.0 : node.IsSupportBase ? 4.5 : 3.5;
            drawingContext.DrawEllipse(fill, pen, point, radius, radius);
        }

        DrawText(drawingContext, $"{model.ModelPrefix} | {model.ArchSegmentCount} arch segments | {model.VerticalPostCount} posts | {model.FrameMemberCount} frames", new Point(18, 12), 13, Brushes.SlateGray);
        DrawLegend(drawingContext);
    }

    private static int MemberDrawOrder(CotArchMember member)
    {
        return member.Kind switch
        {
            CotArchMemberKind.SupportColumn => 0,
            CotArchMemberKind.TensionTie => 1,
            CotArchMemberKind.VerticalPost => 2,
            CotArchMemberKind.UpperBeam => 3,
            CotArchMemberKind.Arch => 4,
            _ => 5
        };
    }

    private static double StrokeFor(CotArchMemberKind kind)
    {
        return kind switch
        {
            CotArchMemberKind.Arch => 3.0,
            CotArchMemberKind.SupportColumn => 3.2,
            CotArchMemberKind.TensionTie => 2.8,
            _ => 2.6
        };
    }

    private static Color ColorFor(CotArchMemberKind kind)
    {
        return kind switch
        {
            CotArchMemberKind.Arch => Color.FromRgb(37, 99, 235),
            CotArchMemberKind.SupportColumn => Color.FromRgb(220, 38, 38),
            CotArchMemberKind.TensionTie => Color.FromRgb(168, 85, 247),
            CotArchMemberKind.VerticalPost => Color.FromRgb(217, 70, 239),
            CotArchMemberKind.UpperBeam => Color.FromRgb(217, 70, 239),
            _ => Color.FromRgb(51, 65, 85)
        };
    }

    private static void DrawGrid(DrawingContext dc, double x, double y, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
        for (int index = 0; index <= 5; index++)
        {
            double gx = x + width * index / 5.0;
            double gy = y + height * index / 5.0;
            dc.DrawLine(pen, new Point(gx, y), new Point(gx, y + height));
            dc.DrawLine(pen, new Point(x, gy), new Point(x + width, gy));
        }
    }

    private void DrawLegend(DrawingContext dc)
    {
        double y = Math.Max(ActualHeight - 38, 32);
        DrawText(dc, "Arch", new Point(22, y - 8), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(ColorFor(CotArchMemberKind.Arch)), 3), new Point(58, y), new Point(92, y));
        DrawText(dc, "Posts / beam", new Point(108, y - 8), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(ColorFor(CotArchMemberKind.UpperBeam)), 2.6), new Point(188, y), new Point(222, y));
        DrawText(dc, "Tie", new Point(238, y - 8), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(ColorFor(CotArchMemberKind.TensionTie)), 2.8), new Point(264, y), new Point(298, y));
        DrawText(dc, "Supports", new Point(314, y - 8), 11, Brushes.SlateGray);
        dc.DrawLine(new Pen(new SolidColorBrush(ColorFor(CotArchMemberKind.SupportColumn)), 3.2), new Point(372, y), new Point(406, y));
    }

    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush)
    {
        double dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
    }
}
