using System.Globalization;
using System.Text.RegularExpressions;

namespace CSIModellingTools.Features.IfcImport;

public sealed class EtabsFrameExporter : IEtabsFrameExporter
{
    private const string UnknownMaterial = "UNKNOWN_MATERIAL";
    private readonly Func<IEtabsFrameExportGateway> _gatewayFactory;

    public EtabsFrameExporter()
        : this(() => new RealEtabsFrameExportGateway())
    {
    }

    public EtabsFrameExporter(Func<IEtabsFrameExportGateway> gatewayFactory)
    {
        _gatewayFactory = gatewayFactory;
    }

    public EtabsExportResult ExportFramesToEtabs(IfcImportResult result, EtabsExportOptions options)
    {
        options ??= new EtabsExportOptions();
        var exportResult = new EtabsExportResult();
        if (result == null)
        {
            exportResult.IsError = true;
            exportResult.Message = "No IFC import result was provided for ETABS export.";
            return exportResult;
        }

        try
        {
            using IEtabsFrameExportGateway gateway = _gatewayFactory();
            gateway.Connect(options.EtabsInstanceId);
            if (options.TargetMode == EtabsExportTargetMode.CreateNewModel)
                gateway.CreateNewModel(options.EtabsUnits);

            gateway.SetUnits(options.EtabsUnits);
            gateway.EnsureUnlocked();
            gateway.EnsureGroup(options.ExportGroupName);

            foreach (AnalyticalFrameElement frame in result.Frames)
                ExportFrame(frame, options, gateway, exportResult);

            exportResult.IsError = false;
            exportResult.Message = $"Exported {exportResult.ExportedFrameCount} IFC frame(s) to ETABS. Skipped {exportResult.SkippedFrameCount}.";
        }
        catch (Exception ex)
        {
            exportResult.IsError = true;
            exportResult.Message = "ETABS frame export failed before frame export could complete: " + ex.Message;
            exportResult.Warnings.Add(new EtabsExportWarning
            {
                Severity = EtabsExportWarningSeverity.Error,
                Category = EtabsExportWarningCategory.Connection,
                Message = ex.Message
            });
        }

        return exportResult;
    }

    private static void ExportFrame(
        AnalyticalFrameElement frame,
        EtabsExportOptions options,
        IEtabsFrameExportGateway gateway,
        EtabsExportResult result)
    {
        try
        {
            if (!ShouldExportConfidence(frame, options, result))
                return;

            string materialName = ResolveMaterialName(frame, options, result);
            if (materialName.Length == 0)
                return;

            SectionInfo sectionInfo = ResolveSectionInfo(frame, options, result);
            if (sectionInfo.SectionName.Length == 0)
                return;

            EtabsMaterialKind materialKind = ResolveMaterialKind(frame, sectionInfo);
            if (!gateway.MaterialExists(materialName))
            {
                gateway.CreateMaterial(materialName, materialKind);
                result.CreatedMaterialCount++;
            }

            if (!gateway.FrameSectionExists(sectionInfo.SectionName))
            {
                gateway.CreateFrameSection(sectionInfo.SectionName, materialName, sectionInfo);
                result.CreatedSectionCount++;
            }

            string frameName = gateway.AddFrameByCoordinates(
                frame.StartPoint,
                frame.EndPoint,
                sectionInfo.SectionName,
                BuildPreferredFrameName(frame, options));

            result.ExportedFrameNames.Add(frameName);
            gateway.AssignFrameToGroup(frameName, options.ExportGroupName);
            if (options.PreserveSourceGuid && !string.IsNullOrWhiteSpace(frame.SourceGuid))
                gateway.AssignFrameToGroup(frameName, BuildSourceGuidGroupName(frame.SourceGuid));

            result.ExportedFrameCount++;
        }
        catch (Exception ex)
        {
            result.SkippedFrameCount++;
            result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Error, EtabsExportWarningCategory.Export, "Frame was skipped because ETABS export failed: " + ex.Message));
        }
    }

    private static bool ShouldExportConfidence(
        AnalyticalFrameElement frame,
        EtabsExportOptions options,
        EtabsExportResult result)
    {
        if (frame.Confidence == IfcRecognitionConfidence.High)
            return true;

        if (frame.Confidence == IfcRecognitionConfidence.Medium &&
            !options.ExportOnlyHighConfidence &&
            options.ExportMediumConfidenceWithWarnings)
        {
            result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Confidence, "Exporting medium-confidence frame."));
            return true;
        }

        result.SkippedFrameCount++;
        result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Confidence, $"Skipped {frame.Confidence} confidence frame."));
        return false;
    }

    private static string ResolveMaterialName(
        AnalyticalFrameElement frame,
        EtabsExportOptions options,
        EtabsExportResult result)
    {
        string materialName = (frame.MaterialName ?? "").Trim();
        bool unknown = materialName.Length == 0 ||
            string.Equals(materialName, UnknownMaterial, StringComparison.OrdinalIgnoreCase);

        if (!unknown)
            return BuildSafeEtabsName(materialName, "IFC_MAT_", 60);

        if (options.SkipUnknownMaterials)
        {
            result.SkippedFrameCount++;
            result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Material, "Skipped frame because material is unknown."));
            return "";
        }

        string fallback = ResolveMaterialKind(frame, frame.SectionInfo) == EtabsMaterialKind.Steel
            ? options.DefaultSteelMaterial
            : options.DefaultConcreteMaterial;
        result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Material, $"Unknown material mapped to default ETABS material '{fallback}'."));
        return BuildSafeEtabsName(fallback, "IFC_MAT_", 60);
    }

    private static SectionInfo ResolveSectionInfo(
        AnalyticalFrameElement frame,
        EtabsExportOptions options,
        EtabsExportResult result)
    {
        SectionInfo source = frame.SectionInfo ?? new SectionInfo();
        if (source.ShapeType != IfcSectionShapeType.Unknown)
        {
            source.SectionName = BuildSafeEtabsName(
                string.IsNullOrWhiteSpace(source.SectionName) ? frame.SectionName : source.SectionName,
                "IFC_SEC_",
                60);
            return source;
        }

        if (TryInferRectangleSectionFromNames(frame, out SectionInfo inferredSection))
        {
            result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Section, $"Section profile was inferred from element name as {inferredSection.Width * 1000.0:0.#}x{inferredSection.Depth * 1000.0:0.#} mm. Please verify."));
            return inferredSection;
        }

        if (options.SkipUnknownSections)
        {
            result.SkippedFrameCount++;
            result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Section, "Skipped frame because section profile is unknown."));
            return new SectionInfo();
        }

        result.Warnings.Add(BuildWarning(frame, EtabsExportWarningSeverity.Warning, EtabsExportWarningCategory.Section, $"Unknown section mapped to default ETABS rectangular section '{options.DefaultUnknownSectionName}'."));
        return new SectionInfo
        {
            SectionName = BuildSafeEtabsName(options.DefaultUnknownSectionName, "IFC_SEC_", 60),
            ShapeType = IfcSectionShapeType.Rectangle,
            Width = 0.3,
            Depth = 0.3,
            OriginalIfcProfileType = "UnknownDefault"
        };
    }

    private static bool TryInferRectangleSectionFromNames(AnalyticalFrameElement frame, out SectionInfo section)
    {
        section = new SectionInfo();
        string text = string.Join(
            " ",
            frame.SectionName ?? "",
            frame.SectionInfo?.SectionName ?? "",
            frame.SourceName ?? "");

        MatchCollection matches = Regex.Matches(
            text,
            @"(?<!\d)(?<width>\d{2,5}(?:\.\d+)?)\s*[xX×]\s*(?<depth>\d{2,5}(?:\.\d+)?)(?!\d)");
        if (matches.Count == 0)
            return false;

        Match match = matches
            .Cast<Match>()
            .OrderByDescending(candidate => candidate.Index)
            .First();

        if (!double.TryParse(match.Groups["width"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double widthMm) ||
            !double.TryParse(match.Groups["depth"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double depthMm) ||
            widthMm <= 0 ||
            depthMm <= 0)
        {
            return false;
        }

        string prefix = string.Equals(frame.IfcType, "IfcColumn", StringComparison.OrdinalIgnoreCase) ? "C" : "B";
        section = new SectionInfo
        {
            ShapeType = IfcSectionShapeType.Rectangle,
            Width = widthMm / 1000.0,
            Depth = depthMm / 1000.0,
            SectionName = BuildSafeEtabsName($"{prefix}{widthMm:0.#}x{depthMm:0.#}", "IFC_SEC_", 60),
            OriginalIfcProfileType = "InferredFromName"
        };
        return true;
    }

    private static EtabsMaterialKind ResolveMaterialKind(AnalyticalFrameElement frame, SectionInfo sectionInfo)
    {
        string material = (frame.MaterialName ?? "").ToUpperInvariant();
        if (material.Contains("STEEL", StringComparison.Ordinal) ||
            material.StartsWith("S", StringComparison.Ordinal) ||
            sectionInfo.ShapeType == IfcSectionShapeType.ISection)
        {
            return EtabsMaterialKind.Steel;
        }

        return EtabsMaterialKind.Concrete;
    }

    private static string BuildPreferredFrameName(AnalyticalFrameElement frame, EtabsExportOptions options)
    {
        if (options.PreserveSourceGuid && !string.IsNullOrWhiteSpace(frame.SourceGuid))
            return BuildSafeEtabsName("IFC_" + frame.SourceGuid, "IFC_FRAME_", 60);

        string seed = string.IsNullOrWhiteSpace(frame.SourceName) ? frame.IfcType : frame.SourceName;
        return BuildSafeEtabsName(seed, "IFC_FRAME_", 60);
    }

    private static string BuildSourceGuidGroupName(string sourceGuid)
    {
        return BuildSafeEtabsName("IFC_GUID_" + sourceGuid, "IFC_GUID_", 60);
    }

    internal static string BuildSafeEtabsName(string? rawName, string fallbackPrefix, int maxLength)
    {
        string text = (rawName ?? "").Trim();
        if (text.Length == 0)
            text = fallbackPrefix + "UNKNOWN";

        string safe = new(text
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray());

        while (safe.Contains("__", StringComparison.Ordinal))
            safe = safe.Replace("__", "_", StringComparison.Ordinal);

        safe = safe.Trim('_');
        if (safe.Length == 0)
            safe = fallbackPrefix + "UNKNOWN";

        return safe.Length > maxLength ? safe[..maxLength] : safe;
    }

    private static EtabsExportWarning BuildWarning(
        AnalyticalFrameElement frame,
        EtabsExportWarningSeverity severity,
        EtabsExportWarningCategory category,
        string message)
    {
        return new EtabsExportWarning
        {
            SourceGuid = frame.SourceGuid,
            SourceName = frame.SourceName,
            Severity = severity,
            Category = category,
            Message = message
        };
    }
}
