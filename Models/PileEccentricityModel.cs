using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CSIModellingTools.Models;

public static class TieBeamDirectionEffectLabels
{
    public const string Downward = "Downward";
    public const string Upward = "Upward";

    public static IReadOnlyList<string> All { get; } =
    [
        Downward,
        Upward
    ];
}

public static class StandardPileGroupLayoutLabels
{
    public const string OnePile = "1 pile";
    public const string TwoPiles = "2 piles";
    public const string ThreePiles = "3 piles";
    public const string FourPiles = "4 piles";

    public static IReadOnlyList<string> All { get; } =
    [
        OnePile,
        TwoPiles,
        ThreePiles,
        FourPiles
    ];
}

public abstract class PileEccentricityEditableRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PileEccentricityPileGroupRow : PileEccentricityEditableRow
{
    private string _groupId = "";
    private double _columnX;
    private double _columnY;
    private double _verticalLoadNkN;
    private double _additionalMxkNm;
    private double _additionalMykNm;
    private bool _isActive = true;

    public string GroupId { get => _groupId; set => SetProperty(ref _groupId, value ?? ""); }
    public double ColumnX { get => _columnX; set => SetProperty(ref _columnX, value); }
    public double ColumnY { get => _columnY; set => SetProperty(ref _columnY, value); }
    public double VerticalLoadNkN { get => _verticalLoadNkN; set => SetProperty(ref _verticalLoadNkN, value); }
    public double AdditionalMxkNm { get => _additionalMxkNm; set => SetProperty(ref _additionalMxkNm, value); }
    public double AdditionalMykNm { get => _additionalMykNm; set => SetProperty(ref _additionalMykNm, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public PileEccentricityPileGroupRow Clone()
    {
        return new PileEccentricityPileGroupRow
        {
            GroupId = GroupId,
            ColumnX = ColumnX,
            ColumnY = ColumnY,
            VerticalLoadNkN = VerticalLoadNkN,
            AdditionalMxkNm = AdditionalMxkNm,
            AdditionalMykNm = AdditionalMykNm,
            IsActive = IsActive
        };
    }
}

public sealed class PileEccentricityPileRow : PileEccentricityEditableRow
{
    private string _groupId = "";
    private string _pileId = "";
    private double _originalX;
    private double _originalY;
    private double _shiftX;
    private double _shiftY;
    private double _x;
    private double _y;
    private double _diameter = 0.6;
    private double _compressionCapacitykN = 1200.0;
    private double _tensionCapacitykN;
    private double _verticalStiffnessKv = 1.0;
    private bool _isActive = true;

    public string GroupId { get => _groupId; set => SetProperty(ref _groupId, value ?? ""); }
    public string PileId { get => _pileId; set => SetProperty(ref _pileId, value ?? ""); }

    public double OriginalX
    {
        get => _originalX;
        set
        {
            if (SetProperty(ref _originalX, value))
                UpdateActualPositionFromOriginalAndShift();
        }
    }

    public double OriginalY
    {
        get => _originalY;
        set
        {
            if (SetProperty(ref _originalY, value))
                UpdateActualPositionFromOriginalAndShift();
        }
    }

    public double ShiftX
    {
        get => _shiftX;
        set => SetShiftX(value, nameof(ShiftX));
    }

    public double ShiftY
    {
        get => _shiftY;
        set => SetShiftY(value, nameof(ShiftY));
    }

    public double ShiftXmm
    {
        get => ShiftX * 1000.0;
        set => SetShiftX(value / 1000.0, nameof(ShiftXmm));
    }

    public double ShiftYmm
    {
        get => ShiftY * 1000.0;
        set => SetShiftY(value / 1000.0, nameof(ShiftYmm));
    }

    public double X { get => _x; set => SetProperty(ref _x, value); }
    public double Y { get => _y; set => SetProperty(ref _y, value); }
    public double Diameter
    {
        get => _diameter;
        set => SetDiameter(value, nameof(Diameter));
    }

    public double DiameterMm
    {
        get => Diameter * 1000.0;
        set => SetDiameter(value / 1000.0, nameof(DiameterMm));
    }

    public double CompressionCapacitykN { get => _compressionCapacitykN; set => SetProperty(ref _compressionCapacitykN, value); }
    public double TensionCapacitykN { get => _tensionCapacitykN; set => SetProperty(ref _tensionCapacitykN, value); }
    public double VerticalStiffnessKv { get => _verticalStiffnessKv; set => SetProperty(ref _verticalStiffnessKv, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public PileEccentricityPileRow Clone()
    {
        return new PileEccentricityPileRow
        {
            GroupId = GroupId,
            PileId = PileId,
            OriginalX = OriginalX,
            OriginalY = OriginalY,
            ShiftX = ShiftX,
            ShiftY = ShiftY,
            X = X,
            Y = Y,
            Diameter = Diameter,
            CompressionCapacitykN = CompressionCapacitykN,
            TensionCapacitykN = TensionCapacitykN,
            VerticalStiffnessKv = VerticalStiffnessKv,
            IsActive = IsActive
        };
    }

    public void SetOriginalPosition(double x, double y)
    {
        _originalX = x;
        _originalY = y;
        OnPropertyChanged(nameof(OriginalX));
        OnPropertyChanged(nameof(OriginalY));
        UpdateActualPositionFromOriginalAndShift();
    }

    private void UpdateActualPositionFromOriginalAndShift()
    {
        double x = OriginalX + ShiftX;
        double y = OriginalY + ShiftY;
        SetProperty(ref _x, x, nameof(X));
        SetProperty(ref _y, y, nameof(Y));
    }

    private void SetShiftX(double value, string propertyName)
    {
        if (!SetProperty(ref _shiftX, value, propertyName))
            return;

        if (propertyName != nameof(ShiftX))
            OnPropertyChanged(nameof(ShiftX));
        if (propertyName != nameof(ShiftXmm))
            OnPropertyChanged(nameof(ShiftXmm));

        UpdateActualPositionFromOriginalAndShift();
    }

    private void SetShiftY(double value, string propertyName)
    {
        if (!SetProperty(ref _shiftY, value, propertyName))
            return;

        if (propertyName != nameof(ShiftY))
            OnPropertyChanged(nameof(ShiftY));
        if (propertyName != nameof(ShiftYmm))
            OnPropertyChanged(nameof(ShiftYmm));

        UpdateActualPositionFromOriginalAndShift();
    }

    private void SetDiameter(double value, string propertyName)
    {
        if (!SetProperty(ref _diameter, value, propertyName))
            return;

        if (propertyName != nameof(Diameter))
            OnPropertyChanged(nameof(Diameter));
        if (propertyName != nameof(DiameterMm))
            OnPropertyChanged(nameof(DiameterMm));
    }
}

public sealed class PileEccentricityTieBeamRow : PileEccentricityEditableRow
{
    private string _tieBeamId = "";
    private string _fromGroupId = "";
    private string _toGroupId = "";
    private double _width = 0.6;
    private double _depth = 0.9;
    private double _concreteStrengthNmm2 = 30.0;
    private double _steelYieldStrengthNmm2 = 500.0;
    private double _coverMm = 50.0;
    private double _linkDiameterMm = 10.0;
    private double _tensionBarDiameterMm = 20.0;
    private double _compressionBarDiameterMm = 20.0;
    private double _length;
    private bool _useAutoLength = true;
    private double _transferPercentagePercent = 100.0;
    private bool _autoDirectionByCoordinate;
    private string _manualFromEffect = TieBeamDirectionEffectLabels.Downward;
    private string _manualToEffect = TieBeamDirectionEffectLabels.Upward;
    private bool _isActive = true;

    public string TieBeamId { get => _tieBeamId; set => SetProperty(ref _tieBeamId, value ?? ""); }
    public string FromGroupId { get => _fromGroupId; set => SetProperty(ref _fromGroupId, value ?? ""); }
    public string ToGroupId { get => _toGroupId; set => SetProperty(ref _toGroupId, value ?? ""); }
    public double Width { get => _width; set => SetWidth(value, nameof(Width)); }
    public double Depth { get => _depth; set => SetDepth(value, nameof(Depth)); }
    public double WidthMm { get => Width * 1000.0; set => SetWidth(value / 1000.0, nameof(WidthMm)); }
    public double DepthMm { get => Depth * 1000.0; set => SetDepth(value / 1000.0, nameof(DepthMm)); }
    public double ConcreteStrengthNmm2 { get => _concreteStrengthNmm2; set => SetProperty(ref _concreteStrengthNmm2, value); }
    public double SteelYieldStrengthNmm2 { get => _steelYieldStrengthNmm2; set => SetProperty(ref _steelYieldStrengthNmm2, value); }
    public double CoverMm { get => _coverMm; set => SetProperty(ref _coverMm, value); }
    public double LinkDiameterMm { get => _linkDiameterMm; set => SetProperty(ref _linkDiameterMm, value); }
    public double TensionBarDiameterMm { get => _tensionBarDiameterMm; set => SetProperty(ref _tensionBarDiameterMm, value); }
    public double CompressionBarDiameterMm { get => _compressionBarDiameterMm; set => SetProperty(ref _compressionBarDiameterMm, value); }
    public double Length { get => _length; set => SetProperty(ref _length, value); }
    public bool UseAutoLength { get => _useAutoLength; set => SetProperty(ref _useAutoLength, value); }
    public double TransferPercentagePercent { get => _transferPercentagePercent; set => SetProperty(ref _transferPercentagePercent, value); }
    public bool AutoDirectionByCoordinate
    {
        get => _autoDirectionByCoordinate;
        set
        {
            if (SetProperty(ref _autoDirectionByCoordinate, value))
                OnPropertyChanged(nameof(ManualDirectionEnabled));
        }
    }

    public bool ManualDirectionEnabled => !AutoDirectionByCoordinate;
    public string ManualFromEffect { get => _manualFromEffect; set => SetProperty(ref _manualFromEffect, value ?? TieBeamDirectionEffectLabels.Downward); }
    public string ManualToEffect { get => _manualToEffect; set => SetProperty(ref _manualToEffect, value ?? TieBeamDirectionEffectLabels.Upward); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public PileEccentricityTieBeamRow Clone()
    {
        return new PileEccentricityTieBeamRow
        {
            TieBeamId = TieBeamId,
            FromGroupId = FromGroupId,
            ToGroupId = ToGroupId,
            Width = Width,
            Depth = Depth,
            ConcreteStrengthNmm2 = ConcreteStrengthNmm2,
            SteelYieldStrengthNmm2 = SteelYieldStrengthNmm2,
            CoverMm = CoverMm,
            LinkDiameterMm = LinkDiameterMm,
            TensionBarDiameterMm = TensionBarDiameterMm,
            CompressionBarDiameterMm = CompressionBarDiameterMm,
            Length = Length,
            UseAutoLength = UseAutoLength,
            TransferPercentagePercent = TransferPercentagePercent,
            AutoDirectionByCoordinate = AutoDirectionByCoordinate,
            ManualFromEffect = ManualFromEffect,
            ManualToEffect = ManualToEffect,
            IsActive = IsActive
        };
    }

    private void SetWidth(double value, string propertyName)
    {
        if (!SetProperty(ref _width, value, propertyName))
            return;

        if (propertyName != nameof(Width))
            OnPropertyChanged(nameof(Width));
        if (propertyName != nameof(WidthMm))
            OnPropertyChanged(nameof(WidthMm));
    }

    private void SetDepth(double value, string propertyName)
    {
        if (!SetProperty(ref _depth, value, propertyName))
            return;

        if (propertyName != nameof(Depth))
            OnPropertyChanged(nameof(Depth));
        if (propertyName != nameof(DepthMm))
            OnPropertyChanged(nameof(DepthMm));
    }
}

public sealed class PileEccentricityCalculationInput
{
    public List<PileEccentricityPileGroupRow> PileGroups { get; set; } = [];
    public List<PileEccentricityPileRow> Piles { get; set; } = [];
    public List<PileEccentricityTieBeamRow> TieBeams { get; set; } = [];
    public bool UseIdealTieBeamTransfer { get; set; }
}

public sealed class PileEccentricityCalculationResult
{
    public List<PileEccentricityGeometrySummary> GeometrySummaries { get; set; } = [];
    public List<PileEccentricityPileLoadResult> IsolatedPileLoads { get; set; } = [];
    public List<PileEccentricityTieBeamSummary> TieBeamSummaries { get; set; } = [];
    public List<PileEccentricityPileLoadResult> RevisedPileLoads { get; set; } = [];
    public List<PileEccentricityComparisonRow> Comparisons { get; set; } = [];
    public List<PileEccentricityCalculationStep> CalculationSteps { get; set; } = [];
    public List<ValidationIssue> Messages { get; set; } = [];
    public PileEccentricityPreviewModel Preview { get; set; } = new();
}

public sealed class PileEccentricityCalculationStep
{
    public string Section { get; set; } = "";
    public string Item { get; set; } = "";
    public string Formula { get; set; } = "";
    public string Substitution { get; set; } = "";
    public string Result { get; set; } = "";
}

public sealed class PileEccentricityGeometrySummary
{
    public string GroupId { get; set; } = "";
    public int ActivePileCount { get; set; }
    public double CentroidX { get; set; }
    public double CentroidY { get; set; }
    public double Ex { get; set; }
    public double Ey { get; set; }
    public double MxkNm { get; set; }
    public double MykNm { get; set; }
    public double SumX2 { get; set; }
    public double SumY2 { get; set; }
}

public sealed class PileEccentricityPileLoadResult
{
    public string GroupId { get; set; } = "";
    public string PileId { get; set; } = "";
    public double XLocal { get; set; }
    public double YLocal { get; set; }
    public double BaseLoadkN { get; set; }
    public double LoadFromMxkN { get; set; }
    public double LoadFromMykN { get; set; }
    public double FinalLoadkN { get; set; }
    public string Status { get; set; } = "OK";
}

public sealed class PileEccentricityTieBeamSummary
{
    public string TieBeamId { get; set; } = "";
    public string FromGroupId { get; set; } = "";
    public string ToGroupId { get; set; } = "";
    public string MomentTransferred { get; set; } = "";
    public double MxTransferredkNm { get; set; }
    public double MyTransferredkNm { get; set; }
    public double MomentMagnitudekNm { get; set; }
    public double PointLoadkN { get; set; }
    public double LoadDistanceA { get; set; }
    public double LoadDistanceB { get; set; }
    public string CaseType { get; set; } = "";
    public double FromSupportReactionkN { get; set; }
    public double ToSupportReactionkN { get; set; }
    public string FromPileStatus { get; set; } = "";
    public string ToPileStatus { get; set; } = "";
    public double FromPileMagnitudekN { get; set; }
    public double ToPileMagnitudekN { get; set; }
    public double DesignMomentkNm { get; set; }
    public string MomentType { get; set; } = "";
    public string MomentLocation { get; set; } = "";
    public double Length { get; set; }
    public double TransferForcekN { get; set; }
    public string FromEffect { get; set; } = "";
    public string ToEffect { get; set; } = "";
    public double FromVerticalDeltaKn { get; set; }
    public double ToVerticalDeltaKn { get; set; }
    public double BeamWidthMm { get; set; }
    public double BeamDepthMm { get; set; }
    public double CoverMm { get; set; }
    public double LinkDiameterMm { get; set; }
    public double EffectiveDepthMm { get; set; }
    public double CompressionSteelDepthMm { get; set; }
    public double ConcreteStrengthNmm2 { get; set; }
    public double SteelYieldStrengthNmm2 { get; set; }
    public double ConcreteDesignStrengthNmm2 { get; set; }
    public double SteelDesignStrengthNmm2 { get; set; }
    public double EurocodeK { get; set; }
    public double EurocodeKLimit { get; set; }
    public string EurocodeKCheck { get; set; } = "";
    public string CompressionBarRequired { get; set; } = "";
    public double LeverArmMm { get; set; }
    public double RequiredTensionSteelMm2 { get; set; }
    public double RequiredCompressionSteelMm2 { get; set; }
    public double TensionBarDiameterMm { get; set; }
    public double CompressionBarDiameterMm { get; set; }
    public int SuggestedTensionBarCount { get; set; }
    public int SuggestedCompressionBarCount { get; set; }
    public string SuggestedTensionBars { get; set; } = "";
    public string SuggestedCompressionBars { get; set; } = "";
    public string TensionFace { get; set; } = "";
    public string SectionDesignStatus { get; set; } = "";
}

public sealed class PileEccentricityComparisonRow
{
    public string GroupId { get; set; } = "";
    public string CaseName { get; set; } = "";
    public double RvkN { get; set; }
    public double MxRemainingkNm { get; set; }
    public double MyRemainingkNm { get; set; }
    public double MaxPileLoadkN { get; set; }
    public double MinPileLoadkN { get; set; }
    public string Uplift { get; set; } = "No";
}

public sealed class PileEccentricityPreviewModel
{
    public List<PileEccentricityPreviewGroup> Groups { get; set; } = [];
    public List<PileEccentricityPreviewPile> Piles { get; set; } = [];
    public List<PileEccentricityPreviewTieBeam> TieBeams { get; set; } = [];
}

public sealed class PileEccentricityPreviewGroup
{
    public string GroupId { get; set; } = "";
    public double ColumnX { get; set; }
    public double ColumnY { get; set; }
    public double CentroidX { get; set; }
    public double CentroidY { get; set; }
    public double Ex { get; set; }
    public double Ey { get; set; }
    public double MxkNm { get; set; }
    public double MykNm { get; set; }
}

public sealed class PileEccentricityPreviewPile
{
    public string GroupId { get; set; } = "";
    public string PileId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double FinalLoadkN { get; set; }
    public string Status { get; set; } = "OK";
}

public sealed class PileEccentricityPreviewTieBeam
{
    public string TieBeamId { get; set; } = "";
    public string FromGroupId { get; set; } = "";
    public string ToGroupId { get; set; } = "";
    public double FromX { get; set; }
    public double FromY { get; set; }
    public double ToX { get; set; }
    public double ToY { get; set; }
    public string FromEffect { get; set; } = "";
    public string ToEffect { get; set; } = "";
    public double TransferForcekN { get; set; }
    public string MomentTransferred { get; set; } = "";
}
