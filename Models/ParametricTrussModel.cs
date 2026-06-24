using System.Globalization;

namespace CSIModellingTools.Models;

public enum TrussType
{
    Warren,
    Pratt,
    Howe,
    K,
    SimpleFrame,
    SpiralStaircase,
    FishBellyTruss,
    VariablePanelWidthTruss
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Critical
}

public enum ChordSlopeMode
{
    Pitch,
    OneSided
}

public enum EtabsExportMode
{
    EraseAndRedraw,
    AddAsNew
}

public enum SupportNodeMode
{
    EndBottomNodes,
    AllBottomChordNodes,
    NoSupports
}

public enum SupportRestraintType
{
    FirstPinOthersRoller,
    AllPinned,
    AllZRollers
}

public enum SpiralStairRotationDirection
{
    Anticlockwise,
    Clockwise
}

public enum FishBellyBottomChordShape
{
    Parabolic,
    LinearToMiddle,
    CircularArcApproximation
}

public enum FishBellyWebPattern
{
    VerticalAlternatingDiagonal,
    VerticalSameDirectionDiagonal,
    Warren,
    Pratt,
    Howe,
    CrossBracing
}

public enum VariablePanelWidthVariation
{
    Parabolic,
    SmoothCosine,
    LinearToMiddle
}

public static class ParametricMemberGroups
{
    public const string TopChord = "TopChord";
    public const string BottomChord = "BottomChord";
    public const string Diagonal = "Diagonal";
    public const string Vertical = "Vertical";
    public const string EndPost = "EndPost";
    public const string Secondary = "Secondary";
    public const string InnerStringer = "InnerStringer";
    public const string OuterStringer = "OuterStringer";
    public const string RadialTread = "RadialTread";
    public const string CentralColumn = "CentralColumn";
    public const string LandingBeam = "LandingBeam";

    public static IReadOnlyList<string> All { get; } =
    [
        TopChord,
        BottomChord,
        Diagonal,
        Vertical,
        EndPost,
        Secondary,
        InnerStringer,
        OuterStringer,
        RadialTread,
        CentralColumn,
        LandingBeam
    ];
}

public sealed class ModelPoint3d
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public ModelPoint3d Clone()
    {
        return new ModelPoint3d { X = X, Y = Y, Z = Z };
    }

    public static ModelPoint3d Interpolate(ModelPoint3d start, ModelPoint3d end, double t)
    {
        return new ModelPoint3d
        {
            X = start.X + (end.X - start.X) * t,
            Y = start.Y + (end.Y - start.Y) * t,
            Z = start.Z + (end.Z - start.Z) * t
        };
    }
}

public sealed class ParametricNode
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double PreviewX { get; set; }
    public double PreviewZ { get; set; }
    public bool IsSupport { get; set; }
    public bool IsTopChord { get; set; }
    public bool IsBottomChord { get; set; }
}

public sealed class ParametricMember
{
    public string Id { get; set; } = "";
    public string StartNodeId { get; set; } = "";
    public string EndNodeId { get; set; } = "";
    public string Group { get; set; } = "";
    public string SectionName { get; set; } = "";
    public bool ReleaseMoments { get; set; }
}

public sealed class ParametricShell
{
    public string Id { get; set; } = "";
    public List<string> NodeIds { get; set; } = [];
    public string Group { get; set; } = "";
    public string ShellPropertyName { get; set; } = "";
}

public sealed class ParametricLoad
{
    public string Id { get; set; } = "";
    public string LoadPattern { get; set; } = "";
    public string TargetType { get; set; } = "Node";
    public string TargetId { get; set; } = "";
    public string Direction { get; set; } = "GlobalZ";
    public double Magnitude { get; set; }
}

public enum ParametricTrussLoadTarget
{
    TopChord,
    BottomChord
}

public enum ParametricTrussLoadInputType
{
    LineLoadKnPerM,
    AreaLoadKpa
}

public enum ParametricTrussLoadApplicationMode
{
    PanelNodes,
    MemberLine
}

public sealed class ParametricTrussLoadDefinition
{
    public string Id { get; set; } = "";
    public string LoadPattern { get; set; } = "";
    public ParametricTrussLoadTarget Target { get; set; } = ParametricTrussLoadTarget.TopChord;
    public ParametricTrussLoadInputType InputType { get; set; } = ParametricTrussLoadInputType.LineLoadKnPerM;
    public ParametricTrussLoadApplicationMode ApplicationMode { get; set; } = ParametricTrussLoadApplicationMode.PanelNodes;
    public double Magnitude { get; set; }
    public double PanelWidth { get; set; } = 1.0;

    public double EquivalentLineLoadKnPerM =>
        InputType == ParametricTrussLoadInputType.AreaLoadKpa
            ? Magnitude * PanelWidth
            : Magnitude;

    public string TargetDisplay =>
        Target == ParametricTrussLoadTarget.BottomChord ? "Bottom chord" : "Top chord";

    public string InputDisplay =>
        InputType == ParametricTrussLoadInputType.AreaLoadKpa
            ? $"{Magnitude:0.###} kPa x {PanelWidth:0.###} m"
            : $"{Magnitude:0.###} kN/m";

    public string ApplicationDisplay =>
        ApplicationMode == ParametricTrussLoadApplicationMode.MemberLine ? "Line load" : "Panel nodes";

    public string EquivalentDisplay => $"{EquivalentLineLoadKnPerM:0.###} kN/m";

    public string SummaryDisplay => $"{TargetDisplay} / {LoadPattern} / {InputDisplay}";

    public ParametricTrussLoadDefinition Clone()
    {
        return new ParametricTrussLoadDefinition
        {
            Id = Id,
            LoadPattern = LoadPattern,
            Target = Target,
            InputType = InputType,
            ApplicationMode = ApplicationMode,
            Magnitude = Magnitude,
            PanelWidth = PanelWidth
        };
    }
}

public sealed class ParametricTrussModel
{
    public string TrussId { get; set; } = "TR01";
    public string GroupName { get; set; } = "WPF_TRUSS_TR01";
    public TrussType TrussType { get; set; } = TrussType.Warren;
    public double Span { get; set; }
    public double Height { get; set; }
    public int PanelCount { get; set; }
    public double RoofSlopePercent { get; set; }
    public double BottomChordSlopePercent { get; set; }
    public ChordSlopeMode TopChordSlopeMode { get; set; } = ChordSlopeMode.Pitch;
    public ChordSlopeMode BottomChordSlopeMode { get; set; } = ChordSlopeMode.Pitch;
    public SupportNodeMode SupportNodeMode { get; set; } = SupportNodeMode.EndBottomNodes;
    public SupportRestraintType SupportRestraintType { get; set; } = SupportRestraintType.FirstPinOthersRoller;
    public ModelPoint3d StartPoint { get; set; } = new();
    public ModelPoint3d EndPoint { get; set; } = new();
    public List<ParametricNode> Nodes { get; set; } = [];
    public List<ParametricMember> Members { get; set; } = [];
    public List<ParametricShell> Shells { get; set; } = [];
    public List<ParametricLoad> Loads { get; set; } = [];
    public Dictionary<string, string> SectionAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; set; } = [];
}

public sealed class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = "";

    public string SeverityText => Severity.ToString();
    public bool IsCritical => Severity == ValidationSeverity.Critical;
}

public sealed class ParametricValidationResult
{
    public List<ValidationIssue> Issues { get; set; } = [];
    public bool HasCriticalIssues => Issues.Any(issue => issue.IsCritical);
}

public static class EtabsNameUtility
{
    public static string BuildSafeName(string prefix, string? rawName, int maxLength = 60)
    {
        string text = ((prefix ?? "") + (rawName ?? "")).Trim();
        if (text.Length == 0)
            text = "TRUSS";

        string safe = new(text
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray());

        while (safe.Contains("__", StringComparison.Ordinal))
            safe = safe.Replace("__", "_", StringComparison.Ordinal);

        safe = safe.Trim('_');
        if (safe.Length == 0)
            safe = "TRUSS";

        return safe.Length > maxLength ? safe[..maxLength] : safe;
    }

    public static string FormatIndex(int index)
    {
        return index.ToString("000", CultureInfo.InvariantCulture);
    }
}
