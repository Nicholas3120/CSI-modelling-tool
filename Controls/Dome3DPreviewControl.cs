using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using TrussModelling.Models;

namespace TrussModelling.Controls;

public sealed class Dome3DPreviewControl : HelixViewport3D
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ParametricDomeModel),
            typeof(Dome3DPreviewControl),
            new PropertyMetadata(null, OnModelChanged));

    public ParametricDomeModel? Model
    {
        get => (ParametricDomeModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public Dome3DPreviewControl()
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
        if (dependencyObject is Dome3DPreviewControl control)
            control.RebuildScene();
    }

    private void RebuildScene()
    {
        Children.Clear();
        Children.Add(new DefaultLights());

        ParametricDomeModel? model = Model;
        if (model == null || model.Nodes.Count == 0)
            return;

        Dictionary<string, DomeNode> nodes = model.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        AddShellMesh(model, nodes);
        AddFrames(model, nodes);
        AddBaseNodes(model);

        (Point3D min, Point3D max) = GetBounds(model.Nodes);
        Point3D center = new((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
        double sceneSize = Math.Max((max - min).Length, 1.0);
        Camera = BuildCamera(center, sceneSize);
        DefaultCamera = Camera;
        if (IsLoaded)
            Dispatcher.BeginInvoke(() => ZoomExtents(250), DispatcherPriority.Background);
    }

    private void AddShellMesh(ParametricDomeModel model, IReadOnlyDictionary<string, DomeNode> nodes)
    {
        if (model.ShellPanels.Count == 0)
            return;

        var mesh = new MeshGeometry3D();
        foreach (DomeShellPanel panel in model.ShellPanels)
        {
            List<DomeNode> panelNodes = panel.NodeIds
                .Where(nodes.ContainsKey)
                .Select(nodeId => nodes[nodeId])
                .ToList();

            if (panelNodes.Count == 3)
            {
                AddTriangle(mesh, panelNodes[0], panelNodes[1], panelNodes[2]);
            }
            else if (panelNodes.Count == 4)
            {
                AddTriangle(mesh, panelNodes[0], panelNodes[1], panelNodes[2]);
                AddTriangle(mesh, panelNodes[0], panelNodes[2], panelNodes[3]);
            }
        }

        var brush = new SolidColorBrush(Color.FromArgb(72, 37, 99, 235));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });
    }

    private void AddFrames(ParametricDomeModel model, IReadOnlyDictionary<string, DomeNode> nodes)
    {
        double diameter = Math.Max(model.BaseRadius * 0.01, 0.04);
        foreach (DomeFrameMember member in model.FrameMembers)
        {
            if (!nodes.TryGetValue(member.StartNodeId, out DomeNode? start) ||
                !nodes.TryGetValue(member.EndNodeId, out DomeNode? end))
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

    private void AddBaseNodes(ParametricDomeModel model)
    {
        if (!model.GenerateSupportsAtBase)
            return;

        double radius = Math.Max(model.BaseRadius * 0.02, 0.08);
        foreach (DomeNode node in model.Nodes.Where(node => node.RingIndex == 0))
        {
            Children.Add(new SphereVisual3D
            {
                Center = ToPoint(node),
                Radius = radius,
                Fill = BrushFrom(Color.FromRgb(14, 116, 144)),
                ThetaDiv = 12,
                PhiDiv = 8
            });
        }
    }

    private static void AddTriangle(MeshGeometry3D mesh, DomeNode a, DomeNode b, DomeNode c)
    {
        int index = mesh.Positions.Count;
        mesh.Positions.Add(ToPoint(a));
        mesh.Positions.Add(ToPoint(b));
        mesh.Positions.Add(ToPoint(c));
        mesh.TriangleIndices.Add(index);
        mesh.TriangleIndices.Add(index + 1);
        mesh.TriangleIndices.Add(index + 2);
    }

    private static (Point3D Min, Point3D Max) GetBounds(IEnumerable<DomeNode> nodes)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (DomeNode node in nodes)
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
        Point3D position = new(center.X + distance, center.Y - distance * 1.15, center.Z + distance * 0.65);
        return new PerspectiveCamera(position, center - position, new Vector3D(0, 0, 1), 38);
    }

    private static Point3D ToPoint(DomeNode node)
    {
        return new Point3D(node.X, node.Y, node.Z);
    }

    private static Brush BrushFrom(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ColorForGroup(DomeMemberGroup group)
    {
        return group switch
        {
            DomeMemberGroup.Radial => Color.FromRgb(5, 150, 105),
            DomeMemberGroup.Diagonal => Color.FromRgb(220, 38, 38),
            DomeMemberGroup.BaseRing => Color.FromRgb(124, 58, 237),
            DomeMemberGroup.CrownRing => Color.FromRgb(217, 119, 6),
            _ => Color.FromRgb(37, 99, 235)
        };
    }
}
