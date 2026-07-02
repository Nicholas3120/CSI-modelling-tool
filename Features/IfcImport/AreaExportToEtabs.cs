namespace CSIModellingTools.Features.IfcImport;

public sealed class AreaExportToEtabs : IEtabsAreaExporter
{
    private const string UnknownMaterial = "UNKNOWN_MATERIAL";
    private readonly Func<IEtabsAreaExportGateway> _gatewayFactory;

    public AreaExportToEtabs()
        : this(() => new RealEtabsAreaExportGateway())
    {
    }

    public AreaExportToEtabs(Func<IEtabsAreaExportGateway> gatewayFactory)
    {
        _gatewayFactory = gatewayFactory;
    }

    public EtabsExportResult ExportAreasToEtabs(IfcImportResult result, EtabsExportOptions options, IProgress<EtabsExportProgress>? progress = null, bool assignDiaphragms = true)
    {
        options ??= new EtabsExportOptions();
        var exportResult = new EtabsExportResult();
        if (result == null)
        {
            exportResult.IsError = true;
            exportResult.Message = "No IFC import result was provided for ETABS area export.";
            return exportResult;
        }

        try
        {
            progress?.Report(new EtabsExportProgress(0, "Connecting to ETABS for areas..."));
            using IEtabsAreaExportGateway gateway = _gatewayFactory();
            gateway.Connect(options.EtabsInstanceId);
            if (options.TargetMode == EtabsExportTargetMode.CreateNewModel)
                gateway.CreateNewModel(options.EtabsUnits);

            gateway.SetUnits(options.EtabsUnits);
            gateway.EnsureUnlocked();
            // Stories are set once by the frame export; the area export must not redefine them
            // (that would fail on the now-populated model), it just adds areas.
            gateway.EnsureGroup(options.ExportGroupName);

            int total = result.Areas.Count;
            int processed = 0;
            int lastReportedPercent = -1;
            foreach (AnalyticalAreaElement area in result.Areas)
            {
                ExportArea(area, options, gateway, exportResult);
                processed++;
                if (progress != null && total > 0)
                {
                    int percent = (int)(processed * 100.0 / total);
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress.Report(new EtabsExportProgress(percent, $"Exporting areas {processed}/{total}"));
                    }
                }
            }

            if (assignDiaphragms)
                EtabsFrameExporter.RunDiaphragmStep(gateway.AssignRigidDiaphragms, exportResult, progress);

            exportResult.IsError = false;
            exportResult.Message = $"Exported {exportResult.ExportedAreaCount} IFC area(s) to ETABS. Skipped {exportResult.SkippedAreaCount}.";
        }
        catch (Exception ex)
        {
            exportResult.IsError = true;
            exportResult.Message = "ETABS area export failed before area export could complete: " + ex.Message;
            exportResult.Warnings.Add(new EtabsExportWarning
            {
                Severity = EtabsExportWarningSeverity.Error,
                Category = EtabsExportWarningCategory.Connection,
                Message = ex.Message
            });
        }

        return exportResult;
    }

    private static void ExportArea(
        AnalyticalAreaElement area,
        EtabsExportOptions options,
        IEtabsAreaExportGateway gateway,
        EtabsExportResult result)
    {
        try
        {
            // Type selection (slabs vs walls) is independent of import, so a deselected
            // type is silently filtered out here rather than counted as a skip.
            bool isWall = IsWall(area);
            if (isWall ? !options.ExportWallAreas : !options.ExportSlabAreas)
                return;

            if (!ShouldExportConfidence(area, options, result))
                return;

            if (area.BoundaryPoints.Count < 3)
            {
                result.SkippedAreaCount++;
                result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Geometry, "Skipped area because it has fewer than three boundary points."));
                return;
            }

            if (!double.IsFinite(area.Thickness) || area.Thickness <= 0)
            {
                result.SkippedAreaCount++;
                result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Area, "Skipped area because thickness is unknown."));
                return;
            }

            string materialName = ResolveMaterialName(area, options, result);
            if (materialName.Length == 0)
                return;

            string propertyName = BuildAreaPropertyName(area, options);
            EtabsMaterialKind materialKind = ResolveMaterialKind(area);
            if (!gateway.MaterialExists(materialName))
            {
                gateway.CreateMaterial(materialName, materialKind);
                result.CreatedMaterialCount++;
            }

            if (!gateway.AreaPropertyExists(propertyName))
            {
                gateway.CreateAreaProperty(propertyName, materialName, area.Thickness, IsWall(area));
                result.CreatedAreaPropertyCount++;
            }

            string areaName = gateway.AddAreaByCoordinates(
                area.BoundaryPoints,
                propertyName,
                BuildPreferredAreaName(area, options));

            result.ExportedAreaNames.Add(areaName);
            gateway.AssignAreaToGroup(areaName, options.ExportGroupName);
            if (options.PreserveSourceGuid && !string.IsNullOrWhiteSpace(area.SourceGuid))
                gateway.AssignAreaToGroup(areaName, BuildSourceGuidGroupName(area.SourceGuid));

            result.ExportedAreaCount++;
        }
        catch (Exception ex)
        {
            result.SkippedAreaCount++;
            result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Error, EtabsExportWarningCategory.Export, "Area was skipped because ETABS export failed: " + ex.Message));
        }
    }

    private static bool ShouldExportConfidence(
        AnalyticalAreaElement area,
        EtabsExportOptions options,
        EtabsExportResult result)
    {
        if (area.Confidence == IfcRecognitionConfidence.High)
            return true;

        if (area.Confidence == IfcRecognitionConfidence.Medium &&
            !options.ExportOnlyHighConfidence &&
            options.ExportMediumConfidenceWithWarnings)
        {
            result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Confidence, "Exporting medium-confidence area."));
            return true;
        }

        result.SkippedAreaCount++;
        result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Confidence, $"Skipped {area.Confidence} confidence area."));
        return false;
    }

    private static string ResolveMaterialName(
        AnalyticalAreaElement area,
        EtabsExportOptions options,
        EtabsExportResult result)
    {
        string materialName = (area.MaterialName ?? "").Trim();
        bool unknown = materialName.Length == 0 ||
            string.Equals(materialName, UnknownMaterial, StringComparison.OrdinalIgnoreCase);

        if (!unknown)
            return EtabsFrameExporter.BuildSafeEtabsName(materialName, "IFC_MAT_", 60);

        if (options.SkipUnknownMaterials)
        {
            result.SkippedAreaCount++;
            result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Material, "Skipped area because material is unknown."));
            return "";
        }

        string fallback = ResolveMaterialKind(area) == EtabsMaterialKind.Steel
            ? options.DefaultSteelMaterial
            : options.DefaultConcreteMaterial;
        result.Warnings.Add(BuildWarning(area, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Material, $"Unknown material mapped to default ETABS material '{fallback}'."));
        return EtabsFrameExporter.BuildSafeEtabsName(fallback, "IFC_MAT_", 60);
    }

    private static string BuildAreaPropertyName(AnalyticalAreaElement area, EtabsExportOptions options)
    {
        string type = IsWall(area) ? "WALL" : "SLAB";
        string thickness = Math.Round(area.Thickness * 1000.0, MidpointRounding.AwayFromZero).ToString("0");
        string seed = string.IsNullOrWhiteSpace(area.SourceName)
            ? $"{options.DefaultAreaPropertyName}_{type}_{thickness}"
            : $"{type}_{area.SourceName}_{thickness}";
        return EtabsFrameExporter.BuildSafeEtabsName(seed, "IFC_AREA_", 60);
    }

    private static string BuildPreferredAreaName(AnalyticalAreaElement area, EtabsExportOptions options)
    {
        if (options.PreserveSourceGuid && !string.IsNullOrWhiteSpace(area.SourceGuid))
            return EtabsFrameExporter.BuildSafeEtabsName("IFC_AREA_" + area.SourceGuid, "IFC_AREA_", 60);

        string seed = string.IsNullOrWhiteSpace(area.SourceName) ? area.IfcType : area.SourceName;
        return EtabsFrameExporter.BuildSafeEtabsName(seed, "IFC_AREA_", 60);
    }

    private static string BuildSourceGuidGroupName(string sourceGuid)
    {
        return EtabsFrameExporter.BuildSafeEtabsName("IFC_GUID_" + sourceGuid, "IFC_GUID_", 60);
    }

    private static EtabsMaterialKind ResolveMaterialKind(AnalyticalAreaElement area)
    {
        string material = (area.MaterialName ?? "").ToUpperInvariant();
        if (material.Contains("STEEL", StringComparison.Ordinal) ||
            material.StartsWith("S", StringComparison.Ordinal))
        {
            return EtabsMaterialKind.Steel;
        }

        return EtabsMaterialKind.Concrete;
    }

    private static bool IsWall(AnalyticalAreaElement area)
    {
        return area.IfcType.Contains("Wall", StringComparison.OrdinalIgnoreCase);
    }

    private static EtabsExportWarning BuildWarning(
        AnalyticalAreaElement area,
        EtabsExportWarningSeverity severity,
        EtabsExportWarningCategory category,
        string message)
    {
        return new EtabsExportWarning
        {
            SourceGuid = area.SourceGuid,
            SourceName = area.SourceName,
            Severity = severity,
            Category = category,
            Message = message
        };
    }
}
