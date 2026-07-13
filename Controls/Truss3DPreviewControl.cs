using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CSIModellingTools.Models;
using HelixToolkit.Wpf;

namespace CSIModellingTools.Controls;

public sealed class Truss3DPreviewControl : HelixViewport3D
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ParametricTrussModel),
            typeof(Truss3DPreviewControl),
            new PropertyMetadata(null, OnModelChanged));

    public ParametricTrussModel? Model
    {
        get => (ParametricTrussModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public Truss3DPreviewControl()
    {
        Background = Brushes.Transparent;
        IsRotationEnabled = true;
        IsPanEnabled = true;
        IsZoomEnabled = true;
        IsMoveEnabled = true;
        IsInertiaEnabled = true;
        IsHeadLightEnabled = true;
        RotateAroundMouseDownPoint = true;
        ZoomAroundMouseDownPoint = true;
        ShowCoordinateSystem = true;
        ShowViewCube = true;
        ZoomExtentsWhenLoaded = true;
        ModelUpDirection = new Vector3D(0, 0, 1);
        Loaded += (_, _) => RebuildScene();
    }

    private static void OnModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is Truss3DPreviewControl control)
            control.RebuildScene();
    }

    private void RebuildScene()
    {
        Children.Clear();
        Children.Add(new DefaultLights());

        ParametricTrussModel? model = Model;
        if (model == null || model.Nodes.Count == 0)
        {
            Camera = new PerspectiveCamera(new Point3D(8, -10, 6), new Vector3D(-8, 10, -6), new Vector3D(0, 0, 1), 40);
            DefaultCamera = Camera;
            return;
        }

        Dictionary<string, ParametricNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        (Point3D min, Point3D max) = GetBounds(model.Nodes);
        Point3D center = new((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
        double sceneSize = Math.Max((max - min).Length, Math.Max(model.Span, model.YBaySpacing));
        sceneSize = Math.Max(sceneSize, 1.0);
        double memberDiameter = Math.Max(sceneSize * 0.0045, 0.025);
        double nodeRadius = Math.Max(memberDiameter * 1.6, 0.045);

        foreach (ParametricMember member in model.Members)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out ParametricNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out ParametricNode? end))
            {
                continue;
            }

            Children.Add(new PipeVisual3D
            {
                Point1 = ToPoint(start),
                Point2 = ToPoint(end),
                Diameter = member.Group.Contains("YZ", StringComparison.OrdinalIgnoreCase) ? memberDiameter * 1.06 : memberDiameter,
                InnerDiameter = 0,
                ThetaDiv = 10,
                Fill = BrushFrom(ColorForMember(member))
            });
        }

        int nodeLimit = model.Nodes.Count <= 450 ? model.Nodes.Count : model.Nodes.Count(node => node.IsSupport);
        foreach (ParametricNode node in model.Nodes
            .Where(node => node.IsSupport || model.Nodes.Count <= 450)
            .Take(nodeLimit))
        {
            Children.Add(new SphereVisual3D
            {
                Center = ToPoint(node),
                Radius = node.IsSupport ? nodeRadius * 1.35 : nodeRadius,
                Fill = BrushFrom(node.IsSupport ? Color.FromRgb(14, 116, 144) : Color.FromRgb(248, 250, 252)),
                ThetaDiv = 10,
                PhiDiv = 8
            });
        }

        Camera = BuildCamera(center, sceneSize);
        DefaultCamera = Camera;
        if (IsLoaded)
            Dispatcher.BeginInvoke(() => ZoomExtents(250), DispatcherPriority.Background);
    }

    private static (Point3D Min, Point3D Max) GetBounds(IEnumerable<ParametricNode> nodes)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (ParametricNode node in nodes)
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            minZ = Math.Min(minZ, node.Z);
            maxX = Math.Max(maxX, node.X);
            maxY = Math.Max(maxY, node.Y);
            maxZ = Math.Max(maxZ, node.Z);
        }

        return (new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }

    private static PerspectiveCamera BuildCamera(Point3D center, double length)
    {
        double distance = Math.Max(length * 1.55, 8.0);
        Point3D position = new(center.X + distance, center.Y - distance * 0.95, center.Z + distance * 0.55);
        return new PerspectiveCamera(position, center - position, new Vector3D(0, 0, 1), 38);
    }

    private static Point3D ToPoint(ParametricNode node)
    {
        return new Point3D(node.X, node.Y, node.Z);
    }

    private static Brush BrushFrom(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
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
            ParametricMemberGroups.YZTopChord => Color.FromRgb(245, 158, 11),
            ParametricMemberGroups.YZBottomChord => Color.FromRgb(180, 83, 9),
            ParametricMemberGroups.YZDiagonal => Color.FromRgb(234, 88, 12),
            ParametricMemberGroups.YZVertical => Color.FromRgb(202, 138, 4),
            ParametricMemberGroups.YZEndPost => Color.FromRgb(161, 98, 7),
            ParametricMemberGroups.Secondary => Color.FromRgb(217, 119, 6),
            _ => Color.FromRgb(30, 41, 59)
        };
    }

    private static Color ColorForMember(ParametricMember member)
    {
        return TryParseHexColor(member.PreviewColorHex, out Color color)
            ? color
            : ColorForGroup(member.Group);
    }

    private static bool TryParseHexColor(string? value, out Color color)
    {
        color = default;
        string text = (value ?? "").Trim();
        if (text.StartsWith("#", StringComparison.Ordinal))
            text = text[1..];

        if (text.Length != 6)
            return false;

        try
        {
            byte r = Convert.ToByte(text[0..2], 16);
            byte g = Convert.ToByte(text[2..4], 16);
            byte b = Convert.ToByte(text[4..6], 16);
            color = Color.FromRgb(r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
