using CSIModellingTools.Models;

namespace CSIModellingTools.Services;

public sealed partial class Sap2000ModellingService
{
    public SectionPropertyUpdateResult UpdateSectionDesignerFrameProperty(Sap2000SectionDesignerFramePropertyUpdateRequest request)
    {
        var warnings = new List<string>();

        try
        {
            Sap2000SectionDesignerFrameSection section = request.Section;
            string propertyName = (section.Name ?? "").Trim();
            if (propertyName.Length == 0)
                throw new InvalidOperationException("Section Designer frame section name is required.");

            if (section.Shapes.Count == 0)
                throw new InvalidOperationException($"SAP2000 Section Designer section '{propertyName}' has no readable shapes to migrate.");

            List<string> unsupportedShapes = section.Shapes
                .Where(shape => !CanWriteSap2000SectionDesignerShape(shape))
                .Select(DescribeSap2000SectionDesignerShapeIssue)
                .ToList();
            if (unsupportedShapes.Count > 0)
                throw new InvalidOperationException($"SAP2000 Section Designer section '{propertyName}' could not be migrated because {unsupportedShapes.Count} shape(s) are not supported/readable: {string.Join("; ", unsupportedShapes)}.");

            string baseMaterialName = FirstNonBlank(
                section.BaseMaterialName,
                section.Shapes.Select(shape => shape.MaterialName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                section.Shapes.Select(shape => shape.RebarMaterialName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));
            if (baseMaterialName.Length == 0)
                throw new InvalidOperationException($"SAP2000 Section Designer section '{propertyName}' did not expose a base material.");

            SAP2000v1.cSapModel sapModel = GetRequiredSapModelObject(GetSap2000Object(request.Sap2000InstanceId));
            SAP2000v1.eUnits? originalUnits = TryGetPresentUnits(sapModel);
            bool createdProperty = false;

            try
            {
                TrySetPresentUnitsToKnM(sapModel, warnings);
                TryUnlockModelForDrawing(sapModel, warnings);

                if (Sap2000FramePropertyExists(sapModel, propertyName))
                    throw new InvalidOperationException($"SAP2000 frame section '{propertyName}' already exists in the target model.");

                EnsureSap2000MaterialsExist(sapModel, RequiredSap2000SectionDesignerMaterials(section), $"Section Designer frame section '{propertyName}'");

                int ret = sapModel.PropFrame.SetSDSection(
                    propertyName,
                    baseMaterialName,
                    section.DesignType,
                    section.Color,
                    section.Notes ?? "",
                    "");
                if (ret != 0)
                    throw new InvalidOperationException($"SAP2000 could not create Section Designer frame section '{propertyName}'. Return code: {ret}.");

                createdProperty = true;

                SAP2000v1.cPropFrameSDShape? sdShapeApi = sapModel.PropFrame.SDShape;
                if (sdShapeApi == null)
                    throw new InvalidOperationException("SAP2000 did not expose the Section Designer shape API.");

                var shapeFailures = new List<string>();
                foreach (Sap2000SectionDesignerShape shape in section.Shapes)
                {
                    int shapeRet = SetSap2000SectionDesignerShape(sdShapeApi, propertyName, shape);
                    if (shapeRet != 0)
                        shapeFailures.Add($"{DescribeSap2000SectionDesignerShape(shape)} returned {shapeRet}");
                }

                if (shapeFailures.Count > 0)
                    throw new InvalidOperationException($"SAP2000 could not recreate {shapeFailures.Count} Section Designer shape(s): {string.Join("; ", shapeFailures)}.");

                return new SectionPropertyUpdateResult
                {
                    IsError = false,
                    Message = $"SAP2000 Section Designer frame section '{propertyName}' was created with {section.Shapes.Count} shape(s).",
                    Warnings = warnings
                };
            }
            catch
            {
                if (createdProperty)
                    TryDeleteIncompleteSap2000FrameProperty(sapModel, propertyName, warnings);
                throw;
            }
            finally
            {
                if (originalUnits != null)
                    TryRestorePresentUnits(sapModel, originalUnits.Value);
            }
        }
        catch (Exception ex)
        {
            return new SectionPropertyUpdateResult
            {
                IsError = true,
                Message = ex.Message,
                Warnings = warnings
            };
        }
    }

    private static Sap2000SectionDesignerFrameSection? TryReadSap2000SectionDesignerFrameSection(
        SAP2000v1.cSapModel sapModel,
        string sectionName,
        List<string> warnings)
    {
        try
        {
            string baseMaterialName = "";
            int numberItems = 0;
            string[] shapeNames = [];
            int[] shapeTypes = [];
            int designType = 0;
            int color = -1;
            string notes = "";
            string guid = "";

            int ret = sapModel.PropFrame.GetSDSection(
                sectionName,
                ref baseMaterialName,
                ref numberItems,
                ref shapeNames,
                ref shapeTypes,
                ref designType,
                ref color,
                ref notes,
                ref guid);
            if (ret != 0)
            {
                warnings.Add($"SAP2000 Section Designer section '{sectionName}' could not be read. Return code: {ret}.");
                return null;
            }

            var section = new Sap2000SectionDesignerFrameSection
            {
                Name = sectionName,
                BaseMaterialName = baseMaterialName,
                DesignType = designType,
                Color = color,
                Notes = notes,
                Guid = guid
            };

            SAP2000v1.cPropFrameSDShape? sdShapeApi = sapModel.PropFrame.SDShape;
            if (sdShapeApi == null)
            {
                warnings.Add($"SAP2000 Section Designer section '{sectionName}' was found, but the Section Designer shape API was not available.");
                return section;
            }

            int count = Math.Min(numberItems, Math.Min(shapeNames.Length, shapeTypes.Length));
            if (count < numberItems)
                warnings.Add($"SAP2000 Section Designer section '{sectionName}' reported {numberItems} shape(s), but only {count} shape name/type pair(s) were returned.");

            for (int index = 0; index < count; index++)
            {
                string shapeName = shapeNames[index] ?? "";
                int typeCode = shapeTypes[index];
                Sap2000SectionDesignerShape shape = ReadSap2000SectionDesignerShape(sdShapeApi, sectionName, shapeName, typeCode);
                section.Shapes.Add(shape);

                if (!string.IsNullOrWhiteSpace(shape.UnsupportedReason))
                    warnings.Add($"SAP2000 Section Designer shape '{shapeName}' in section '{sectionName}' could not be fully read: {shape.UnsupportedReason}");
            }

            return section;
        }
        catch (Exception ex)
        {
            warnings.Add($"SAP2000 Section Designer section '{sectionName}' read failed: {ex.Message}");
            return null;
        }
    }

    private static Sap2000SectionDesignerShape ReadSap2000SectionDesignerShape(
        SAP2000v1.cPropFrameSDShape sdShapeApi,
        string sectionName,
        string shapeName,
        int typeCode)
    {
        var shape = new Sap2000SectionDesignerShape
        {
            TypeCode = typeCode,
            ShapeName = shapeName
        };

        try
        {
            int ret;
            string matProp = "";
            string propName = "";
            string stressStrainOverwrite = "";
            int color = -1;
            double xCenter = 0;
            double yCenter = 0;
            double x1 = 0;
            double y1 = 0;
            double x2 = 0;
            double y2 = 0;
            double h = 0;
            double w = 0;
            double bf = 0;
            double tf = 0;
            double tw = 0;
            double bfb = 0;
            double tfb = 0;
            double dis = 0;
            double rotation = 0;
            double diameter = 0;
            double thickness = 0;
            double angle = 0;
            double radius = 0;
            double spacing = 0;
            double cover = 0;
            int numberPoints = 0;
            int numberBars = 0;
            int[] pointNumbers = [];
            int[] edgeNumbers = [];
            string[] rebarSizes = [];
            double[] x = [];
            double[] y = [];
            double[] radii = [];
            double[] spacings = [];
            double[] covers = [];
            bool reinf = false;
            bool endBars = false;
            string rebarSize = "";
            string matRebar = "";

            switch (typeCode)
            {
                case 1:
                    ret = sdShapeApi.GetISection(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref h, ref bf, ref tf, ref tw, ref bfb, ref tfb, ref rotation);
                    return ret == 0
                        ? PopulateShape(shape, matProp, propName, color, xCenter, yCenter, h, bf, tf, tw, bfb, tfb, rotation)
                        : MarkUnsupported(shape, $"GetISection returned {ret}");

                case 2:
                    ret = sdShapeApi.GetChannel(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref h, ref bf, ref tf, ref tw, ref rotation);
                    return ret == 0
                        ? PopulateShape(shape, matProp, propName, color, xCenter, yCenter, h, bf, tf, tw, 0, 0, rotation)
                        : MarkUnsupported(shape, $"GetChannel returned {ret}");

                case 3:
                case 61:
                    ret = sdShapeApi.GetTee(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref h, ref bf, ref tf, ref tw, ref rotation);
                    return ret == 0
                        ? PopulateShape(shape, matProp, propName, color, xCenter, yCenter, h, bf, tf, tw, 0, 0, rotation)
                        : MarkUnsupported(shape, $"GetTee returned {ret}");

                case 4:
                case 62:
                    ret = sdShapeApi.GetAngle(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref h, ref bf, ref tf, ref tw, ref rotation);
                    return ret == 0
                        ? PopulateShape(shape, matProp, propName, color, xCenter, yCenter, h, bf, tf, tw, 0, 0, rotation)
                        : MarkUnsupported(shape, $"GetAngle returned {ret}");

                case 5:
                    ret = sdShapeApi.GetDblAngle(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref h, ref w, ref tf, ref tw, ref dis, ref rotation);
                    return ret == 0
                        ? PopulateShape(shape, matProp, propName, color, xCenter, yCenter, h, w, tf, tw, 0, 0, rotation, dis)
                        : MarkUnsupported(shape, $"GetDblAngle returned {ret}");

                case 6:
                case 63:
                    ret = sdShapeApi.GetTube(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref h, ref w, ref tf, ref tw, ref rotation);
                    return ret == 0
                        ? PopulateShape(shape, matProp, propName, color, xCenter, yCenter, h, w, tf, tw, 0, 0, rotation)
                        : MarkUnsupported(shape, $"GetTube returned {ret}");

                case 7:
                case 64:
                    ret = sdShapeApi.GetPipe(sectionName, shapeName, ref matProp, ref propName, ref color, ref xCenter, ref yCenter, ref diameter, ref thickness);
                    return ret == 0
                        ? PopulatePipeShape(shape, matProp, propName, color, xCenter, yCenter, diameter, thickness)
                        : MarkUnsupported(shape, $"GetPipe returned {ret}");

                case 8:
                    ret = sdShapeApi.GetPlate(sectionName, shapeName, ref matProp, ref color, ref xCenter, ref yCenter, ref thickness, ref w, ref rotation);
                    return ret == 0
                        ? PopulatePlateShape(shape, matProp, color, xCenter, yCenter, thickness, w, rotation)
                        : MarkUnsupported(shape, $"GetPlate returned {ret}");

                case 101:
                    ret = sdShapeApi.GetSolidRect(sectionName, shapeName, ref matProp, ref stressStrainOverwrite, ref color, ref xCenter, ref yCenter, ref h, ref w, ref rotation, ref reinf, ref matRebar);
                    return ret == 0
                        ? PopulateSolidRectShape(shape, matProp, stressStrainOverwrite, color, xCenter, yCenter, h, w, rotation, reinf, matRebar)
                        : MarkUnsupported(shape, $"GetSolidRect returned {ret}");

                case 102:
                    ret = sdShapeApi.GetSolidCircle(sectionName, shapeName, ref matProp, ref stressStrainOverwrite, ref color, ref xCenter, ref yCenter, ref diameter, ref reinf, ref numberBars, ref rotation, ref cover, ref rebarSize, ref matRebar);
                    return ret == 0
                        ? PopulateSolidCircleShape(shape, matProp, stressStrainOverwrite, color, xCenter, yCenter, diameter, reinf, numberBars, rotation, cover, rebarSize, matRebar)
                        : MarkUnsupported(shape, $"GetSolidCircle returned {ret}");

                case 103:
                    ret = sdShapeApi.GetSolidSegment(sectionName, shapeName, ref matProp, ref color, ref xCenter, ref yCenter, ref angle, ref rotation, ref radius);
                    return ret == 0
                        ? PopulateSolidArcShape(shape, matProp, color, xCenter, yCenter, angle, rotation, radius)
                        : MarkUnsupported(shape, $"GetSolidSegment returned {ret}");

                case 104:
                    ret = sdShapeApi.GetSolidSector(sectionName, shapeName, ref matProp, ref color, ref xCenter, ref yCenter, ref angle, ref rotation, ref radius);
                    return ret == 0
                        ? PopulateSolidArcShape(shape, matProp, color, xCenter, yCenter, angle, rotation, radius)
                        : MarkUnsupported(shape, $"GetSolidSector returned {ret}");

                case 201:
                    ret = sdShapeApi.GetPolygon(sectionName, shapeName, ref matProp, ref stressStrainOverwrite, ref numberPoints, ref x, ref y, ref radii, ref color, ref reinf, ref matRebar);
                    return ret == 0
                        ? PopulatePolygonShape(shape, matProp, stressStrainOverwrite, numberPoints, x, y, radii, color, reinf, matRebar)
                        : MarkUnsupported(shape, $"GetPolygon returned {ret}");

                case 301:
                    ret = sdShapeApi.GetReinfSingle(sectionName, shapeName, ref xCenter, ref yCenter, ref rebarSize, ref matRebar);
                    return ret == 0
                        ? PopulateReinfSingleShape(shape, xCenter, yCenter, rebarSize, matRebar)
                        : MarkUnsupported(shape, $"GetReinfSingle returned {ret}");

                case 302:
                    ret = sdShapeApi.GetReinfLine(sectionName, shapeName, ref x1, ref y1, ref x2, ref y2, ref spacing, ref rebarSize, ref endBars, ref matRebar);
                    return ret == 0
                        ? PopulateReinfLineShape(shape, x1, y1, x2, y2, spacing, rebarSize, endBars, matRebar)
                        : MarkUnsupported(shape, $"GetReinfLine returned {ret}");

                case 303:
                    ret = sdShapeApi.GetReinfRectangular(sectionName, shapeName, ref xCenter, ref yCenter, ref h, ref w, ref rotation, ref matRebar);
                    return ret == 0
                        ? PopulateReinfRectShape(shape, xCenter, yCenter, h, w, rotation, matRebar)
                        : MarkUnsupported(shape, $"GetReinfRectangular returned {ret}");

                case 304:
                    ret = sdShapeApi.GetReinfCircle(sectionName, shapeName, ref xCenter, ref yCenter, ref diameter, ref numberBars, ref rotation, ref rebarSize, ref matRebar);
                    return ret == 0
                        ? PopulateReinfCircleShape(shape, xCenter, yCenter, diameter, numberBars, rotation, rebarSize, matRebar)
                        : MarkUnsupported(shape, $"GetReinfCircle returned {ret}");

                case 305:
                    ret = sdShapeApi.GetReinfCorner(sectionName, shapeName, ref numberPoints, ref pointNumbers, ref rebarSizes);
                    return ret == 0
                        ? PopulateReinfCornerShape(shape, numberPoints, pointNumbers, rebarSizes)
                        : MarkUnsupported(shape, $"GetReinfCorner returned {ret}");

                case 306:
                    ret = sdShapeApi.GetReinfEdge(sectionName, shapeName, ref numberPoints, ref edgeNumbers, ref rebarSizes, ref spacings, ref covers);
                    return ret == 0
                        ? PopulateReinfEdgeShape(shape, numberPoints, edgeNumbers, rebarSizes, spacings, covers)
                        : MarkUnsupported(shape, $"GetReinfEdge returned {ret}");

                case 401:
                    ret = sdShapeApi.GetRefLine(sectionName, shapeName, ref x1, ref y1, ref x2, ref y2);
                    return ret == 0
                        ? PopulateRefLineShape(shape, x1, y1, x2, y2)
                        : MarkUnsupported(shape, $"GetRefLine returned {ret}");

                case 402:
                    ret = sdShapeApi.GetRefCircle(sectionName, shapeName, ref xCenter, ref yCenter, ref diameter);
                    return ret == 0
                        ? PopulateRefCircleShape(shape, xCenter, yCenter, diameter)
                        : MarkUnsupported(shape, $"GetRefCircle returned {ret}");

                default:
                    return MarkUnsupported(shape, $"Section Designer shape type {typeCode} is not currently supported.");
            }
        }
        catch (Exception ex)
        {
            return MarkUnsupported(shape, ex.Message);
        }
    }

    private static Sap2000SectionDesignerShape PopulateShape(
        Sap2000SectionDesignerShape shape,
        string materialName,
        string propertyName,
        int color,
        double xCenter,
        double yCenter,
        double height,
        double width,
        double flangeThickness,
        double webThickness,
        double bottomFlangeWidth,
        double bottomFlangeThickness,
        double rotation,
        double distance = 0)
    {
        shape.MaterialName = materialName;
        shape.PropertyName = propertyName;
        shape.Color = color;
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Height = height;
        shape.Width = width;
        shape.FlangeThickness = flangeThickness;
        shape.WebThickness = webThickness;
        shape.BottomFlangeWidth = bottomFlangeWidth;
        shape.BottomFlangeThickness = bottomFlangeThickness;
        shape.Rotation = rotation;
        shape.Distance = distance;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulatePipeShape(Sap2000SectionDesignerShape shape, string materialName, string propertyName, int color, double xCenter, double yCenter, double diameter, double thickness)
    {
        shape.MaterialName = materialName;
        shape.PropertyName = propertyName;
        shape.Color = color;
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Diameter = diameter;
        shape.Thickness = thickness;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulatePlateShape(Sap2000SectionDesignerShape shape, string materialName, int color, double xCenter, double yCenter, double thickness, double width, double rotation)
    {
        shape.MaterialName = materialName;
        shape.Color = color;
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Thickness = thickness;
        shape.Width = width;
        shape.Rotation = rotation;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateSolidRectShape(Sap2000SectionDesignerShape shape, string materialName, string stressStrainOverwrite, int color, double xCenter, double yCenter, double height, double width, double rotation, bool reinforcement, string rebarMaterialName)
    {
        shape.MaterialName = materialName;
        shape.StressStrainOverwrite = stressStrainOverwrite;
        shape.Color = color;
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Height = height;
        shape.Width = width;
        shape.Rotation = rotation;
        shape.Reinforcement = reinforcement;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateSolidCircleShape(Sap2000SectionDesignerShape shape, string materialName, string stressStrainOverwrite, int color, double xCenter, double yCenter, double diameter, bool reinforcement, int numberBars, double rotation, double cover, string rebarSize, string rebarMaterialName)
    {
        shape.MaterialName = materialName;
        shape.StressStrainOverwrite = stressStrainOverwrite;
        shape.Color = color;
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Diameter = diameter;
        shape.Reinforcement = reinforcement;
        shape.NumberBars = numberBars;
        shape.Rotation = rotation;
        shape.Cover = cover;
        shape.RebarSize = rebarSize;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateSolidArcShape(Sap2000SectionDesignerShape shape, string materialName, int color, double xCenter, double yCenter, double angle, double rotation, double radius)
    {
        shape.MaterialName = materialName;
        shape.Color = color;
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Angle = angle;
        shape.Rotation = rotation;
        shape.Radius = radius;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulatePolygonShape(Sap2000SectionDesignerShape shape, string materialName, string stressStrainOverwrite, int numberPoints, double[] x, double[] y, double[] radii, int color, bool reinforcement, string rebarMaterialName)
    {
        int count = Math.Min(numberPoints, Math.Min(x.Length, Math.Min(y.Length, radii.Length)));
        shape.MaterialName = materialName;
        shape.StressStrainOverwrite = stressStrainOverwrite;
        shape.NumberPoints = count;
        shape.XCoordinates = x.Take(count).ToList();
        shape.YCoordinates = y.Take(count).ToList();
        shape.CornerRadii = radii.Take(count).ToList();
        shape.Color = color;
        shape.Reinforcement = reinforcement;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateReinfSingleShape(Sap2000SectionDesignerShape shape, double xCenter, double yCenter, string rebarSize, string rebarMaterialName)
    {
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.RebarSize = rebarSize;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateReinfLineShape(Sap2000SectionDesignerShape shape, double x1, double y1, double x2, double y2, double spacing, string rebarSize, bool endBars, string rebarMaterialName)
    {
        shape.X1 = x1;
        shape.Y1 = y1;
        shape.X2 = x2;
        shape.Y2 = y2;
        shape.Spacing = spacing;
        shape.RebarSize = rebarSize;
        shape.EndBars = endBars;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateReinfRectShape(Sap2000SectionDesignerShape shape, double xCenter, double yCenter, double height, double width, double rotation, string rebarMaterialName)
    {
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Height = height;
        shape.Width = width;
        shape.Rotation = rotation;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateReinfCircleShape(Sap2000SectionDesignerShape shape, double xCenter, double yCenter, double diameter, int numberBars, double rotation, string rebarSize, string rebarMaterialName)
    {
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Diameter = diameter;
        shape.NumberBars = numberBars;
        shape.Rotation = rotation;
        shape.RebarSize = rebarSize;
        shape.RebarMaterialName = rebarMaterialName;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateReinfCornerShape(Sap2000SectionDesignerShape shape, int numberItems, int[] pointNumbers, string[] rebarSizes)
    {
        int count = Math.Min(numberItems, Math.Min(pointNumbers.Length, rebarSizes.Length));
        shape.NumberPoints = count;
        shape.PointNumbers = pointNumbers.Take(count).ToList();
        shape.RebarSizes = rebarSizes.Take(count).ToList();
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateReinfEdgeShape(Sap2000SectionDesignerShape shape, int numberItems, int[] edgeNumbers, string[] rebarSizes, double[] spacings, double[] covers)
    {
        int count = Math.Min(numberItems, Math.Min(edgeNumbers.Length, Math.Min(rebarSizes.Length, Math.Min(spacings.Length, covers.Length))));
        shape.NumberPoints = count;
        shape.EdgeNumbers = edgeNumbers.Take(count).ToList();
        shape.RebarSizes = rebarSizes.Take(count).ToList();
        shape.Spacings = spacings.Take(count).ToList();
        shape.Covers = covers.Take(count).ToList();
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateRefLineShape(Sap2000SectionDesignerShape shape, double x1, double y1, double x2, double y2)
    {
        shape.X1 = x1;
        shape.Y1 = y1;
        shape.X2 = x2;
        shape.Y2 = y2;
        return shape;
    }

    private static Sap2000SectionDesignerShape PopulateRefCircleShape(Sap2000SectionDesignerShape shape, double xCenter, double yCenter, double diameter)
    {
        shape.XCenter = xCenter;
        shape.YCenter = yCenter;
        shape.Diameter = diameter;
        return shape;
    }

    private static Sap2000SectionDesignerShape MarkUnsupported(Sap2000SectionDesignerShape shape, string reason)
    {
        shape.UnsupportedReason = reason;
        return shape;
    }

    private static int SetSap2000SectionDesignerShape(
        SAP2000v1.cPropFrameSDShape sdShapeApi,
        string sectionName,
        Sap2000SectionDesignerShape shape)
    {
        string shapeName = shape.ShapeName ?? "";

        return shape.TypeCode switch
        {
            1 => sdShapeApi.SetISection(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Height, shape.Width, shape.FlangeThickness, shape.WebThickness, shape.BottomFlangeWidth, shape.BottomFlangeThickness),
            2 => sdShapeApi.SetChannel(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Height, shape.Width, shape.FlangeThickness, shape.WebThickness),
            3 or 61 => sdShapeApi.SetTee(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Height, shape.Width, shape.FlangeThickness, shape.WebThickness),
            4 or 62 => sdShapeApi.SetAngle(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Height, shape.Width, shape.FlangeThickness, shape.WebThickness),
            5 => sdShapeApi.SetDblAngle(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Height, shape.Width, shape.FlangeThickness, shape.WebThickness, shape.Distance, shape.Rotation),
            6 or 63 => sdShapeApi.SetTube(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Height, shape.Width, shape.FlangeThickness, shape.WebThickness),
            7 or 64 => sdShapeApi.SetPipe(sectionName, ref shapeName, shape.MaterialName, shape.PropertyName, shape.XCenter, shape.YCenter, shape.Color, shape.Diameter, shape.Thickness),
            8 => sdShapeApi.SetPlate(sectionName, ref shapeName, shape.MaterialName, shape.XCenter, shape.YCenter, shape.Rotation, shape.Color, shape.Thickness, shape.Width),
            101 => sdShapeApi.SetSolidRect(sectionName, ref shapeName, shape.MaterialName, shape.StressStrainOverwrite, shape.XCenter, shape.YCenter, shape.Height, shape.Width, shape.Rotation, shape.Color, shape.Reinforcement, shape.RebarMaterialName),
            102 => sdShapeApi.SetSolidCircle(sectionName, ref shapeName, shape.MaterialName, shape.StressStrainOverwrite, shape.XCenter, shape.YCenter, shape.Diameter, shape.Color, shape.Reinforcement, shape.NumberBars, shape.Rotation, shape.Cover, shape.RebarSize, shape.RebarMaterialName),
            103 => sdShapeApi.SetSolidSegment(sectionName, ref shapeName, shape.MaterialName, shape.XCenter, shape.YCenter, shape.Angle, shape.Rotation, shape.Radius, shape.Color),
            104 => sdShapeApi.SetSolidSector(sectionName, ref shapeName, shape.MaterialName, shape.XCenter, shape.YCenter, shape.Angle, shape.Rotation, shape.Radius, shape.Color),
            201 => SetSap2000SectionDesignerPolygon(sdShapeApi, sectionName, shape, ref shapeName),
            301 => sdShapeApi.SetReinfSingle(sectionName, ref shapeName, shape.XCenter, shape.YCenter, shape.RebarSize, shape.RebarMaterialName),
            302 => sdShapeApi.SetReinfLine(sectionName, ref shapeName, shape.X1, shape.Y1, shape.X2, shape.Y2, shape.Spacing, shape.RebarSize, shape.EndBars, shape.RebarMaterialName),
            303 => sdShapeApi.SetReinfRectangular(sectionName, ref shapeName, shape.XCenter, shape.YCenter, shape.Height, shape.Width, shape.Rotation, shape.RebarMaterialName),
            304 => sdShapeApi.SetReinfCircle(sectionName, ref shapeName, shape.XCenter, shape.YCenter, shape.Diameter, shape.NumberBars, shape.Rotation, shape.RebarSize, shape.RebarMaterialName),
            305 => SetSap2000SectionDesignerReinfCorner(sdShapeApi, sectionName, shape, ref shapeName),
            306 => SetSap2000SectionDesignerReinfEdge(sdShapeApi, sectionName, shape, ref shapeName),
            401 => sdShapeApi.SetRefLine(sectionName, ref shapeName, shape.X1, shape.Y1, shape.X2, shape.Y2),
            402 => sdShapeApi.SetRefCircle(sectionName, ref shapeName, shape.XCenter, shape.YCenter, shape.Diameter),
            _ => 1
        };
    }

    private static int SetSap2000SectionDesignerPolygon(
        SAP2000v1.cPropFrameSDShape sdShapeApi,
        string sectionName,
        Sap2000SectionDesignerShape shape,
        ref string shapeName)
    {
        double[] x = shape.XCoordinates.ToArray();
        double[] y = shape.YCoordinates.ToArray();
        double[] radii = shape.CornerRadii.ToArray();
        int count = Math.Min(shape.NumberPoints, Math.Min(x.Length, Math.Min(y.Length, radii.Length)));
        return sdShapeApi.SetPolygon(sectionName, ref shapeName, shape.MaterialName, shape.StressStrainOverwrite, count, ref x, ref y, ref radii, shape.Color, shape.Reinforcement, shape.RebarMaterialName);
    }

    private static int SetSap2000SectionDesignerReinfCorner(
        SAP2000v1.cPropFrameSDShape sdShapeApi,
        string sectionName,
        Sap2000SectionDesignerShape shape,
        ref string shapeName)
    {
        int count = Math.Min(shape.PointNumbers.Count, shape.RebarSizes.Count);
        if (count == 0)
            return 0;

        int ret = 0;
        for (int index = 0; index < count; index++)
        {
            ret = sdShapeApi.SetReinfCorner(sectionName, ref shapeName, shape.PointNumbers[index], shape.RebarSizes[index], false);
            if (ret != 0)
                return ret;
        }

        return ret;
    }

    private static int SetSap2000SectionDesignerReinfEdge(
        SAP2000v1.cPropFrameSDShape sdShapeApi,
        string sectionName,
        Sap2000SectionDesignerShape shape,
        ref string shapeName)
    {
        int count = Math.Min(shape.EdgeNumbers.Count, Math.Min(shape.RebarSizes.Count, Math.Min(shape.Spacings.Count, shape.Covers.Count)));
        if (count == 0)
            return 0;

        int ret = 0;
        for (int index = 0; index < count; index++)
        {
            ret = sdShapeApi.SetReinfEdge(sectionName, ref shapeName, shape.EdgeNumbers[index], shape.RebarSizes[index], shape.Spacings[index], shape.Covers[index], false);
            if (ret != 0)
                return ret;
        }

        return ret;
    }

    private static bool CanWriteSap2000SectionDesignerShape(Sap2000SectionDesignerShape shape)
    {
        if (!string.IsNullOrWhiteSpace(shape.UnsupportedReason))
            return false;

        return shape.TypeCode is
            1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or
            61 or 62 or 63 or 64 or
            101 or 102 or 103 or 104 or
            201 or
            301 or 302 or 303 or 304 or 305 or 306 or
            401 or 402;
    }

    private static string FormatSap2000SectionDesignerSummary(SAP2000v1.cSapModel sapModel, string sectionName, Sap2000SectionDesignerFrameSection section)
    {
        string sectionProps = GetFrameSectionSummary(sapModel, sectionName);
        string shapeSummary = $"Section Designer: {section.Shapes.Count} shape(s)";
        int unsupportedCount = section.Shapes.Count(shape => !CanWriteSap2000SectionDesignerShape(shape));
        if (unsupportedCount > 0)
            shapeSummary += $", {unsupportedCount} unsupported/read warning(s)";

        return string.IsNullOrWhiteSpace(sectionProps)
            ? shapeSummary
            : $"{shapeSummary}; {sectionProps}";
    }

    private static bool Sap2000FramePropertyExists(SAP2000v1.cSapModel sapModel, string propertyName)
    {
        try
        {
            SAP2000v1.eFramePropType propType = SAP2000v1.eFramePropType.General;
            return sapModel.PropFrame.GetTypeOAPI(propertyName, ref propType) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> RequiredSap2000SectionDesignerMaterials(Sap2000SectionDesignerFrameSection section)
    {
        var materials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSap2000MaterialName(materials, section.BaseMaterialName);
        foreach (Sap2000SectionDesignerShape shape in section.Shapes)
        {
            AddSap2000MaterialName(materials, shape.MaterialName);
            AddSap2000MaterialName(materials, shape.RebarMaterialName);
        }

        return materials.ToList();
    }

    private static void EnsureSap2000MaterialsExist(SAP2000v1.cSapModel sapModel, IEnumerable<string> materialNames, string targetDescription)
    {
        List<string> missing = materialNames
            .Select(materialName => (materialName ?? "").Trim())
            .Where(materialName => materialName.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(materialName => !Sap2000MaterialExists(sapModel, materialName))
            .OrderBy(materialName => materialName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException($"SAP2000 cannot create {targetDescription} because required material(s) do not exist in the target model: {string.Join(", ", missing)}. Check/migrate those Materials first.");
    }

    private static bool Sap2000MaterialExists(SAP2000v1.cSapModel sapModel, string materialName)
    {
        try
        {
            SAP2000v1.eMatType materialType = SAP2000v1.eMatType.NoDesign;
            int subType = 0;
            if (sapModel.PropMaterial.GetTypeOAPI(materialName, ref materialType, ref subType) == 0)
                return true;
        }
        catch
        {
            // Fall back to GetMaterial below.
        }

        try
        {
            SAP2000v1.eMatType materialType = SAP2000v1.eMatType.NoDesign;
            int color = 0;
            string notes = "";
            string guid = "";
            return sapModel.PropMaterial.GetMaterial(materialName, ref materialType, ref color, ref notes, ref guid) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void AddSap2000MaterialName(ISet<string> materials, string? materialName)
    {
        string trimmed = (materialName ?? "").Trim();
        if (trimmed.Length > 0)
            materials.Add(trimmed);
    }

    private static void TryDeleteIncompleteSap2000FrameProperty(SAP2000v1.cSapModel sapModel, string propertyName, List<string> warnings)
    {
        try
        {
            int ret = sapModel.PropFrame.Delete(propertyName);
            if (ret == 0)
                warnings.Add($"Removed incomplete SAP2000 Section Designer frame section '{propertyName}' after migration failed.");
            else
                warnings.Add($"Could not remove incomplete SAP2000 Section Designer frame section '{propertyName}' after migration failed. Return code: {ret}.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not remove incomplete SAP2000 Section Designer frame section '{propertyName}' after migration failed: {ex.Message}");
        }
    }

    private static string DescribeSap2000SectionDesignerShapeIssue(Sap2000SectionDesignerShape shape)
    {
        string reason = string.IsNullOrWhiteSpace(shape.UnsupportedReason)
            ? "unsupported shape type"
            : shape.UnsupportedReason;
        return $"{DescribeSap2000SectionDesignerShape(shape)} ({reason})";
    }

    private static string DescribeSap2000SectionDesignerShape(Sap2000SectionDesignerShape shape)
    {
        string name = string.IsNullOrWhiteSpace(shape.ShapeName) ? "<unnamed>" : shape.ShapeName.Trim();
        return $"{name} / {Sap2000SectionDesignerShapeTypeLabel(shape.TypeCode)}";
    }

    private static string Sap2000SectionDesignerShapeTypeLabel(int typeCode)
    {
        return typeCode switch
        {
            1 => "I-section",
            2 => "Channel",
            3 => "Tee",
            4 => "Angle",
            5 => "Double angle",
            6 => "Box/tube",
            7 => "Pipe",
            8 => "Plate",
            61 => "Concrete tee",
            62 => "Concrete L",
            63 => "Concrete box",
            64 => "Concrete pipe",
            65 => "Concrete cross",
            101 => "Solid rectangle",
            102 => "Solid circle",
            103 => "Solid segment",
            104 => "Solid sector",
            201 => "Polygon",
            301 => "Reinforcing single",
            302 => "Reinforcing line",
            303 => "Reinforcing rectangle",
            304 => "Reinforcing circle",
            305 => "Reinforcing corner",
            306 => "Reinforcing edge",
            401 => "Reference line",
            402 => "Reference circle",
            _ => $"Type {typeCode}"
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}
