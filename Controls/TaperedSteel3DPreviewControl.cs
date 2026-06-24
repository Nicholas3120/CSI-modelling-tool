using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using TrussModelling.Models;

namespace TrussModelling.Controls;

public sealed class TaperedSteel3DPreviewControl : HelixViewport3D
{
    public static readonly DependencyProperty PreviewProperty =
        DependencyProperty.Register(
            nameof(Preview),
            typeof(TaperedSteelGenerationPreview),
            typeof(TaperedSteel3DPreviewControl),
            new PropertyMetadata(null, OnPreviewChanged));

    public TaperedSteelGenerationPreview? Preview
    {
        get => (TaperedSteelGenerationPreview?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public TaperedSteel3DPreviewControl()
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

    private static void OnPreviewChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TaperedSteel3DPreviewControl control)
            control.RebuildScene();
    }

    private void RebuildScene()
    {
        Children.Clear();
        Children.Add(new DefaultLights());

        TaperedSteelGenerationPreview? preview = Preview;
        if (preview == null || preview.Stations.Count < 2)
            return;

        TaperedSteelSectionGeometry geometry = preview.BaseGeometry;
        if (geometry.DepthM <= 0 || geometry.TopFlangeWidthM <= 0 || geometry.TopFlangeThicknessM <= 0 || geometry.WebThicknessM <= 0)
            return;

        double length = Math.Max(preview.PreviewLengthM, 3.0);
        var mesh = new MeshGeometry3D();

        for (int index = 0; index < preview.Stations.Count - 1; index++)
        {
            TaperedSteelStationPreview start = preview.Stations[index];
            TaperedSteelStationPreview end = preview.Stations[index + 1];
            double x0 = start.PositionRatio * length;
            double x1 = end.PositionRatio * length;

            AddStationSegment(mesh, preview, geometry, x0, x1, start.DepthM, end.DepthM);
        }

        var material = new DiffuseMaterial(BrushFrom(Color.FromRgb(96, 125, 139)));
        Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            }
        });

        Children.Add(new PipeVisual3D
        {
            Point1 = new Point3D(0, 0, GetReferenceZ(preview.ReferenceLine, preview.OriginalDepthM)),
            Point2 = new Point3D(length, 0, GetReferenceZ(preview.ReferenceLine, preview.TipDepthM)),
            Diameter = Math.Max(preview.OriginalDepthM * 0.018, 0.008),
            Fill = BrushFrom(Color.FromRgb(14, 116, 144)),
            ThetaDiv = 8
        });

        Point3D min = new(0, -geometry.TopFlangeWidthM / 2.0, -preview.OriginalDepthM);
        Point3D max = new(length, geometry.TopFlangeWidthM / 2.0, Math.Max(preview.OriginalDepthM, preview.TipDepthM));
        Point3D center = new((min.X + max.X) / 2.0, 0, (min.Z + max.Z) / 2.0);
        double sceneSize = Math.Max((max - min).Length, 1.0);
        Camera = BuildCamera(center, sceneSize);
        DefaultCamera = Camera;
        if (IsLoaded)
            Dispatcher.BeginInvoke(() => ZoomExtents(250), DispatcherPriority.Background);
    }

    private static void AddStationSegment(
        MeshGeometry3D mesh,
        TaperedSteelGenerationPreview preview,
        TaperedSteelSectionGeometry geometry,
        double x0,
        double x1,
        double depth0,
        double depth1)
    {
        if (preview.TaperType is TaperedSectionType.TubeSection or TaperedSectionType.USection)
        {
            AddTubeOrUStationSegment(mesh, preview, geometry, x0, x1, depth0, depth1);
            return;
        }

        double top0 = GetTopZ(preview.ReferenceLine, depth0);
        double top1 = GetTopZ(preview.ReferenceLine, depth1);
        double tfTop = geometry.TopFlangeThicknessM;
        double tw = geometry.WebThicknessM;

        AddTaperedBox(
            mesh,
            x0,
            x1,
            -geometry.TopFlangeWidthM / 2.0,
            geometry.TopFlangeWidthM / 2.0,
            top0 - tfTop,
            top0,
            top1 - tfTop,
            top1);

        bool isTee = preview.TaperType == TaperedSectionType.TSection;
        double webBottom0 = isTee ? top0 - depth0 : top0 - depth0 + geometry.BottomFlangeThicknessM;
        double webBottom1 = isTee ? top1 - depth1 : top1 - depth1 + geometry.BottomFlangeThicknessM;
        AddTaperedBox(
            mesh,
            x0,
            x1,
            -tw / 2.0,
            tw / 2.0,
            webBottom0,
            top0 - tfTop,
            webBottom1,
            top1 - tfTop);

        if (!isTee)
        {
            AddTaperedBox(
                mesh,
                x0,
                x1,
                -geometry.BottomFlangeWidthM / 2.0,
                geometry.BottomFlangeWidthM / 2.0,
                top0 - depth0,
                top0 - depth0 + geometry.BottomFlangeThicknessM,
                top1 - depth1,
                top1 - depth1 + geometry.BottomFlangeThicknessM);
        }
    }

    private static void AddTubeOrUStationSegment(
        MeshGeometry3D mesh,
        TaperedSteelGenerationPreview preview,
        TaperedSteelSectionGeometry geometry,
        double x0,
        double x1,
        double depth0,
        double depth1)
    {
        double top0 = GetTopZ(preview.ReferenceLine, depth0);
        double top1 = GetTopZ(preview.ReferenceLine, depth1);
        double width = geometry.TopFlangeWidthM;
        double tf = geometry.TopFlangeThicknessM;
        double tw = geometry.WebThicknessM;
        bool isUSection = preview.TaperType == TaperedSectionType.USection;
        double yLeft = -width / 2.0;
        double yRight = width / 2.0;

        AddTaperedBox(
            mesh,
            x0,
            x1,
            yLeft,
            yRight,
            top0 - tf,
            top0,
            top1 - tf,
            top1);

        double sideBottom0 = isUSection ? top0 - depth0 : top0 - depth0 + geometry.BottomFlangeThicknessM;
        double sideBottom1 = isUSection ? top1 - depth1 : top1 - depth1 + geometry.BottomFlangeThicknessM;
        AddTaperedBox(
            mesh,
            x0,
            x1,
            yLeft,
            yLeft + tw,
            sideBottom0,
            top0 - tf,
            sideBottom1,
            top1 - tf);
        AddTaperedBox(
            mesh,
            x0,
            x1,
            yRight - tw,
            yRight,
            sideBottom0,
            top0 - tf,
            sideBottom1,
            top1 - tf);

        if (!isUSection)
        {
            AddTaperedBox(
                mesh,
                x0,
                x1,
                yLeft,
                yRight,
                top0 - depth0,
                top0 - depth0 + geometry.BottomFlangeThicknessM,
                top1 - depth1,
                top1 - depth1 + geometry.BottomFlangeThicknessM);
        }
    }

    private static void AddTaperedBox(
        MeshGeometry3D mesh,
        double x0,
        double x1,
        double y0,
        double y1,
        double z0Bottom,
        double z0Top,
        double z1Bottom,
        double z1Top)
    {
        int start = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(x0, y0, z0Bottom));
        mesh.Positions.Add(new Point3D(x0, y1, z0Bottom));
        mesh.Positions.Add(new Point3D(x0, y1, z0Top));
        mesh.Positions.Add(new Point3D(x0, y0, z0Top));
        mesh.Positions.Add(new Point3D(x1, y0, z1Bottom));
        mesh.Positions.Add(new Point3D(x1, y1, z1Bottom));
        mesh.Positions.Add(new Point3D(x1, y1, z1Top));
        mesh.Positions.Add(new Point3D(x1, y0, z1Top));

        AddQuad(mesh, start + 0, start + 1, start + 2, start + 3);
        AddQuad(mesh, start + 4, start + 7, start + 6, start + 5);
        AddQuad(mesh, start + 0, start + 4, start + 5, start + 1);
        AddQuad(mesh, start + 3, start + 2, start + 6, start + 7);
        AddQuad(mesh, start + 1, start + 5, start + 6, start + 2);
        AddQuad(mesh, start + 0, start + 3, start + 7, start + 4);
    }

    private static void AddQuad(MeshGeometry3D mesh, int a, int b, int c, int d)
    {
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(b);
        mesh.TriangleIndices.Add(c);
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(c);
        mesh.TriangleIndices.Add(d);
    }

    private static double GetTopZ(TaperedReferenceLine referenceLine, double depth)
    {
        return referenceLine switch
        {
            TaperedReferenceLine.KeepCentroidLineStraight => depth / 2.0,
            TaperedReferenceLine.KeepBottomFlangeStraight => depth,
            _ => 0.0
        };
    }

    private static double GetReferenceZ(TaperedReferenceLine referenceLine, double depth)
    {
        return referenceLine switch
        {
            TaperedReferenceLine.KeepCentroidLineStraight => 0.0,
            TaperedReferenceLine.KeepBottomFlangeStraight => 0.0,
            _ => GetTopZ(referenceLine, depth)
        };
    }

    private static PerspectiveCamera BuildCamera(Point3D center, double length)
    {
        double distance = Math.Max(length * 1.2, 4.0);
        Point3D position = new(center.X + distance * 0.65, center.Y - distance, center.Z + distance * 0.42);
        return new PerspectiveCamera(position, center - position, new Vector3D(0, 0, 1), 35);
    }

    private static Brush BrushFrom(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
