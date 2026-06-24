using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CSIModellingTools.Models;

public enum PlateGirderShellGroup
{
    Web,
    TopFlange,
    BottomFlange,
    OpeningTopStiffener,
    OpeningBottomStiffener,
    OpeningLeftStiffener,
    OpeningRightStiffener
}

public sealed class PlateGirderNode
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class PlateGirderShellPanel
{
    public string Id { get; set; } = "";
    public List<string> NodeIds { get; set; } = [];
    public PlateGirderShellGroup Group { get; set; } = PlateGirderShellGroup.Web;
    public string ShellPropertyName { get; set; } = "";
}

public sealed class PlateGirderOpening : INotifyPropertyChanged
{
    private string _id = "";
    private double _centerX;
    private double _centerZ;
    private double _width = 1.5;
    private double _height = 0.7;
    private bool _strengthen = true;
    private bool _strengthenTop = true;
    private bool _strengthenBottom = true;
    private bool _strengthenLeft = true;
    private bool _strengthenRight = true;
    private double _stiffenerOutstand = 0.15;
    private double _stiffenerExtension;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set => SetProperty(ref _id, value ?? ""); }
    public double CenterX { get => _centerX; set => SetProperty(ref _centerX, value); }
    public double CenterZ { get => _centerZ; set => SetProperty(ref _centerZ, value); }
    public double Width { get => _width; set => SetProperty(ref _width, value); }
    public double Height { get => _height; set => SetProperty(ref _height, value); }
    public bool Strengthen { get => _strengthen; set => SetProperty(ref _strengthen, value); }
    public bool StrengthenTop { get => _strengthenTop; set => SetProperty(ref _strengthenTop, value); }
    public bool StrengthenBottom { get => _strengthenBottom; set => SetProperty(ref _strengthenBottom, value); }
    public bool StrengthenLeft { get => _strengthenLeft; set => SetProperty(ref _strengthenLeft, value); }
    public bool StrengthenRight { get => _strengthenRight; set => SetProperty(ref _strengthenRight, value); }
    public double StiffenerOutstand { get => _stiffenerOutstand; set => SetProperty(ref _stiffenerOutstand, value); }
    public double StiffenerExtension { get => _stiffenerExtension; set => SetProperty(ref _stiffenerExtension, value); }

    public PlateGirderOpening Clone()
    {
        return new PlateGirderOpening
        {
            Id = Id,
            CenterX = CenterX,
            CenterZ = CenterZ,
            Width = Width,
            Height = Height,
            Strengthen = Strengthen,
            StrengthenTop = StrengthenTop,
            StrengthenBottom = StrengthenBottom,
            StrengthenLeft = StrengthenLeft,
            StrengthenRight = StrengthenRight,
            StiffenerOutstand = StiffenerOutstand,
            StiffenerExtension = StiffenerExtension
        };
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class ParametricPlateGirderModel
{
    public string PlateGirderId { get; set; } = "PG01";
    public string GroupName { get; set; } = "WPF_PLATE_GIRDER_PG01";
    public string WebGroupName { get; set; } = "WPF_PLATE_GIRDER_PG01_WEB";
    public string FlangeGroupName { get; set; } = "WPF_PLATE_GIRDER_PG01_FLANGE";
    public string StiffenerGroupName { get; set; } = "WPF_PLATE_GIRDER_PG01_STIFFENER";
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double OriginZ { get; set; }
    public double Length { get; set; } = 12.0;
    public double Depth { get; set; } = 1.8;
    public double FlangeWidth { get; set; } = 0.45;
    public double WebThickness { get; set; } = 0.012;
    public double FlangeThickness { get; set; } = 0.02;
    public double StiffenerThickness { get; set; } = 0.012;
    public double WebSteelYieldStrengthMpa { get; set; } = 355.0;
    public double FlangeSteelYieldStrengthMpa { get; set; } = 355.0;
    public double StiffenerSteelYieldStrengthMpa { get; set; } = 355.0;
    public double ElasticModulusGpa { get; set; } = 200.0;
    public double WebElasticModulusGpa { get; set; } = 200.0;
    public double FlangeElasticModulusGpa { get; set; } = 200.0;
    public double StiffenerElasticModulusGpa { get; set; } = 200.0;
    public double AnalysisUniformLoadKnPerM { get; set; } = 30.0;
    public bool ApplyTopFlangeAreaLoad { get; set; }
    public string LoadPattern { get; set; } = "";
    public int LengthDivisions { get; set; } = 24;
    public int DepthDivisions { get; set; } = 8;
    public int FlangeWidthDivisions { get; set; } = 2;
    public bool GenerateTopFlange { get; set; } = true;
    public bool GenerateBottomFlange { get; set; } = true;
    public bool HasWebOpening { get; set; } = true;
    public double OpeningCenterX { get; set; } = 6.0;
    public double OpeningCenterZ { get; set; } = 0.9;
    public double OpeningWidth { get; set; } = 1.5;
    public double OpeningHeight { get; set; } = 0.7;
    public bool StrengthenOpening { get; set; } = true;
    public bool StrengthenOpeningTop { get; set; } = true;
    public bool StrengthenOpeningBottom { get; set; } = true;
    public bool StrengthenOpeningLeft { get; set; } = true;
    public bool StrengthenOpeningRight { get; set; } = true;
    public double OpeningStiffenerWidth { get; set; } = 0.15;
    public double OpeningStiffenerExtension { get; set; }
    public string WebShellPropertyName { get; set; } = "";
    public string FlangeShellPropertyName { get; set; } = "";
    public string StiffenerShellPropertyName { get; set; } = "";
    public double GammaM0 { get; set; } = 1.0;
    public List<PlateGirderOpening> Openings { get; set; } = [];
    public List<PlateGirderNode> Nodes { get; set; } = [];
    public List<PlateGirderShellPanel> ShellPanels { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public double TopFlangeAreaLoadKnPerM2 =>
        FlangeWidth > 0.000001 ? AnalysisUniformLoadKnPerM / FlangeWidth : 0.0;
}

public sealed class PlateGirderSectionResult
{
    public double X { get; set; }
    public double InertiaY { get; set; }
    public double NeutralAxisZ { get; set; }
    public double MomentCapacityKnM { get; set; }
    public double DemandMomentKnM { get; set; }
    public double Utilization { get; set; }
    public double ShearCapacityKn { get; set; }
    public double DemandShearKn { get; set; }
    public double ShearUtilization { get; set; }
    public int SectionClass { get; set; }
    public int FlangeClass { get; set; }
    public int WebClass { get; set; }
    public string MomentCapacityBasis { get; set; } = "";
    public string ShearCapacityBasis { get; set; } = "";
    public double DeflectionMm { get; set; }
    public bool WithinOpening { get; set; }
    public bool HasStiffener { get; set; }
}

public sealed class PlateGirderAnalysisResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public double MinimumMomentCapacityKnM { get; set; }
    public double MaximumDemandMomentKnM { get; set; }
    public double MaximumUtilization { get; set; }
    public double MinimumShearCapacityKn { get; set; }
    public double MaximumDemandShearKn { get; set; }
    public double MaximumShearUtilization { get; set; }
    public double MaximumDeflectionMm { get; set; }
    public List<PlateGirderSectionResult> Stations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class PlateGirderEtabsDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class PlateGirderEtabsDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<string> ShellProperties { get; set; } = [];
    public List<PlateGirderShellPropertyDefinition> ShellPropertyDefinitions { get; set; } = [];
    public List<string> LoadPatterns { get; set; } = [];
    public List<string> Stories { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class PlateGirderShellPropertyDefinition
{
    public string Name { get; set; } = "";
    public string AreaType { get; set; } = "";
    public string ShellType { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double Thickness { get; set; }
    public double YieldStrengthMpa { get; set; } = 355.0;
    public double ElasticModulusGpa { get; set; } = 200.0;
}

public sealed class PlateGirderEtabsDrawRequest
{
    public string? EtabsInstanceId { get; set; }
    public ParametricPlateGirderModel Model { get; set; } = new();
    public bool UpdateExistingGroup { get; set; } = true;
}

public sealed class PlateGirderEtabsDrawResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public int CreatedShellCount { get; set; }
    public List<string> ShellObjectNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
