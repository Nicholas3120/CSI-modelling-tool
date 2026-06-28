using System.ComponentModel;
using CSIModellingTools.Models;

namespace CSIModellingTools.ViewModels;

public enum ModelCompareReviewStatus
{
    Unreviewed,
    Reviewed,
    Ignored,
    NeedsChecking
}

public sealed class ModelCompareResultRowViewModel : ModelCompareResultRow, INotifyPropertyChanged
{
    private ModelCompareReviewStatus _reviewStatus;

    public ModelCompareResultRowViewModel(ModelCompareResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        ChangeType = row.ChangeType;
        ObjectType = row.ObjectType;
        ObjectDescription = row.ObjectDescription;
        OldValue = row.OldValue;
        NewValue = row.NewValue;
        Importance = row.Importance;
        Confidence = row.Confidence;
        ConfidenceLevel = row.ConfidenceLevel;
        MatchMethod = row.MatchMethod;
        MatchReason = row.MatchReason;
        CoordinateDifference = row.CoordinateDifference;
        MovementDistance = row.MovementDistance;
        LengthDifference = row.LengthDifference;
        OrientationDifferenceDegrees = row.OrientationDifferenceDegrees;
        SearchText = row.SearchText;
        OldEtabsObjectName = row.OldEtabsObjectName;
        NewEtabsObjectName = row.NewEtabsObjectName;
        OldObjectLocation = row.OldObjectLocation;
        NewObjectLocation = row.NewObjectLocation;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModelCompareReviewStatus ReviewStatus
    {
        get => _reviewStatus;
        set
        {
            if (_reviewStatus == value)
                return;

            _reviewStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReviewStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReviewStatusText)));
        }
    }

    public string ReviewStatusText
    {
        get => ReviewStatus switch
        {
            ModelCompareReviewStatus.NeedsChecking => "Needs checking",
            _ => ReviewStatus.ToString()
        };
        set => ReviewStatus = value switch
        {
            "Reviewed" => ModelCompareReviewStatus.Reviewed,
            "Ignored" => ModelCompareReviewStatus.Ignored,
            "Needs checking" => ModelCompareReviewStatus.NeedsChecking,
            _ => ModelCompareReviewStatus.Unreviewed
        };
    }

    public string MatchMethodText => MatchMethod switch
    {
        ModelCompareMatchMethod.ExactCoordinates => "Exact coordinates",
        ModelCompareMatchMethod.ReversedIJ => "Reversed I-J",
        ModelCompareMatchMethod.SameFrameName => "Same frame name",
        ModelCompareMatchMethod.NearGeometry => "Near geometry",
        ModelCompareMatchMethod.ExactAreaGeometry => "Exact area geometry",
        ModelCompareMatchMethod.Unmatched => "Unmatched",
        _ => "N/A"
    };

    public string DiagnosticMetrics
    {
        get
        {
            var values = new List<string>();
            if (CoordinateDifference.HasValue)
                values.Add($"coord={CoordinateDifference.Value:0.###}");
            if (MovementDistance.HasValue)
                values.Add($"move={MovementDistance.Value:0.###}");
            if (LengthDifference.HasValue)
                values.Add($"length={LengthDifference.Value:0.###}");
            if (OrientationDifferenceDegrees.HasValue)
                values.Add($"angle={OrientationDifferenceDegrees.Value:0.###} deg");
            return string.Join(", ", values);
        }
    }
}
