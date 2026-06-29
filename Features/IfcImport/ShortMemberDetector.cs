namespace CSIModellingTools.Features.IfcImport;

public sealed class ShortMemberDetector
{
    public List<IfcImportWarning> DetectShortMembers(
        IReadOnlyList<AnalyticalFrameElement> frames,
        double minimumLength)
    {
        var warnings = new List<IfcImportWarning>();
        if (!double.IsFinite(minimumLength) || minimumLength <= 0)
            return warnings;

        foreach (AnalyticalFrameElement frame in frames)
        {
            double length = Distance(frame.StartPoint, frame.EndPoint);
            if (length >= minimumLength)
                continue;

            string message = $"Frame length {length:0.###} m is below the configured minimum {minimumLength:0.###} m.";
            frame.Warnings.Add(message);
            warnings.Add(new IfcImportWarning
            {
                SourceGuid = frame.SourceGuid,
                SourceName = frame.SourceName,
                Severity = IfcImportWarningSeverity.Warning,
                Category = IfcImportWarningCategory.Geometry,
                Message = message
            });
        }

        return warnings;
    }

    private static double Distance(AnalyticalPoint first, AnalyticalPoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
