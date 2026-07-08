using System.ComponentModel;
using System.Globalization;
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
        MemberType = row.MemberType;
        Story = row.Story;
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
        OldLabel = row.OldLabel;
        NewLabel = row.NewLabel;
        OldUid = row.OldUid;
        NewUid = row.NewUid;
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

    // The field name shown in the detail pane's "Property" column — the tail of the description after " / "
    // (e.g. "section", "geometry"), or the whole description for added/removed rows.
    public string FieldName
    {
        get
        {
            int index = ObjectDescription.LastIndexOf(" / ", StringComparison.Ordinal);
            string field = index >= 0 ? ObjectDescription[(index + 3)..] : ObjectDescription;
            return field.Trim();
        }
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
                values.Add($"coord={CoordinateDifference.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (MovementDistance.HasValue)
                values.Add($"move={MovementDistance.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (LengthDifference.HasValue)
                values.Add($"length={LengthDifference.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (OrientationDifferenceDegrees.HasValue)
                values.Add($"angle={OrientationDifferenceDegrees.Value.ToString("0.###", CultureInfo.InvariantCulture)} deg");
            return string.Join(", ", values);
        }
    }
}
