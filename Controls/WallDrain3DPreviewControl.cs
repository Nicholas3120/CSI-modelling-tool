using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using CSIModellingTools.Models;

namespace CSIModellingTools.Controls;

public sealed class WallDrain3DPreviewControl : HelixViewport3D
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(WallDrainModel),
            typeof(WallDrain3DPreviewControl),
            new PropertyMetadata(null, OnModelChanged));

    public WallDrainModel? Model
    {
        get => (WallDrainModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public WallDrain3DPreviewControl()
    {
        Background = Brushes.Transparent;
        IsRotationEnabled = true;
        IsPanEnabled = true;
        IsZoomEnabled = true;
        IsMoveEnabled = true;
        IsHeadLightEnabled = true;
        ShowCoordinateSystem = true;
        ShowViewCube = true;
        ZoomExtentsWhenLoaded = true;
        ModelUpDirection = new Vector3D(0, 0, 1);
        Loaded += (_, _) => RebuildScene();
    }

    private static void OnModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WallDrain3DPreviewControl control)
            control.RebuildScene();
    }

    private void RebuildScene()
    {
        Children.Clear();
        Children.Add(new DefaultLights());

        WallDrainModel? model = Model;
        if (model == null || model.Nodes.Count == 0 || (model.FrameMembers.Count == 0 && model.ShellPanels.Count == 0))
            return;

        Dictionary<string, WallDrainNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        if (model.ShellPanels.Count > 0)
            AddShellPanels(model, nodes);
        if (model.FrameMembers.Count > 0)
            AddFrames(model, nodes);

        AddSupportNodes(model);

        (Point3D min, Point3D max) = GetBounds(model.Nodes);
        Point3D center = new((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
        double sceneSize = Math.Max((max - min).Length, 1.0);
        Camera = BuildCamera(center, sceneSize);
        DefaultCamera = Camera;
        if (IsLoaded)
            Dispatcher.BeginInvoke(() => ZoomExtents(250), DispatcherPriority.Background);
    }

    private void AddFrames(WallDrainModel model, IReadOnlyDictionary<string, WallDrainNode> nodes)
    {
        double diameter = Math.Max(Math.Min(model.Height, model.LengthY) * 0.018, 0.035);
        foreach (WallDrainFrameMember member in model.FrameMembers)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out WallDrainNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out WallDrainNode? end))
            {
                continue;
            }

            Children.Add(new PipeVisual3D
            {
                Point1 = ToPoint(start),
                Point2 = ToPoint(end),
                Diameter = diameter,
                InnerDiameter = 0,
                ThetaDiv = 10,
                Fill = BrushFrom(ColorForGroup(member.Group))
            });
        }
    }

    private void AddShellPanels(WallDrainModel model, IReadOnlyDictionary<string, WallDrainNode> nodes)
    {
        foreach (WallDrainShellPanel panel in model.ShellPanels)
        {
            List<WallDrainNode> panelNodes = panel.NodeIds
                .Where(nodes.ContainsKey)
                .Select(nodeId => nodes[nodeId])
                .ToList();
            if (panelNodes.Count < 3)
                continue;

            var mesh = new MeshGeometry3D();
            foreach (WallDrainNode node in panelNodes)
                mesh.Positions.Add(ToPoint(node));

            for (int index = 1; index < panelNodes.Count - 1; index++)
            {
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
            }

            Brush brush = BrushFrom(ColorForShellGroup(panel.Group));
            var material = new DiffuseMaterial(brush);
            Children.Add(new ModelVisual3D
            {
                Content = new GeometryModel3D(mesh, material)
                {
                    BackMaterial = material
                }
            });
        }
    }

    private void AddSupportNodes(WallDrainModel model)
    {
        double radius = Math.Max(Math.Min(model.Height, model.LengthY) * 0.015, 0.04);
        foreach (WallDrainNode node in model.Nodes.Where(node => node.IsSupport))
        {
            Children.Add(new SphereVisual3D
            {
                Center = ToPoint(node),
                Radius = radius,
                Fill = BrushFrom(Color.FromRgb(14, 116, 144)),
                ThetaDiv = 10,
                PhiDiv = 8
            });
        }
    }

    private static (Point3D Min, Point3D Max) GetBounds(IEnumerable<WallDrainNode> nodes)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (WallDrainNode node in nodes)
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
        double distance = Math.Max(length * 1.8, 6.0);
        Point3D position = new(center.X + distance, center.Y - distance * 1.1, center.Z + distance * 0.75);
        return new PerspectiveCamera(position, center - position, new Vector3D(0, 0, 1), 38);
    }

    private static Point3D ToPoint(WallDrainNode node)
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
            WallDrainPanelGroups.BaseSlab or WallDrainPanelGroups.TopSlab => Color.FromRgb(71, 85, 105),
            WallDrainPanelGroups.Buttress or WallDrainPanelGroups.Counterfort => Color.FromRgb(217, 119, 6),
            WallDrainPanelGroups.RightWall => Color.FromRgb(124, 58, 237),
            _ => Color.FromRgb(37, 99, 235)
        };
    }

    private static Color ColorForShellGroup(string group)
    {
        Color color = ColorForGroup(group);
        return Color.FromArgb(158, color.R, color.G, color.B);
    }
}
