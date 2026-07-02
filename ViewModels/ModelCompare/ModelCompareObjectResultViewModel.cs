using System.ComponentModel;
using System.Globalization;
using CSIModellingTools.Models;

namespace CSIModellingTools.ViewModels;

/// <summary>
/// Aggregates all of the per-field difference rows for a single physical object (frame, area, or
/// property/material definition) into one row that summarises what changed. The underlying rows are
/// retained for the expandable detail view and for ETABS selection.
/// </summary>
public sealed class ModelCompareObjectResultViewModel : INotifyPropertyChanged
{
    private ModelCompareReviewStatus _reviewStatus;

    public ModelCompareObjectResultViewModel(
        ModelCompareObjectType objectType,
        ModelCompareMemberType memberType,
        string story,
        string objectName,
        string location,
        IReadOnlyList<ModelCompareResultRowViewModel> rows)
    {
        ObjectType = objectType;
        MemberType = memberType;
        Story = (story ?? "").Trim();
        ObjectName = (objectName ?? "").Trim();
        Location = location ?? "";
        Rows = rows;
        PrimaryChangeType = DerivePrimaryChangeType(rows);
        Importance = rows.Count == 0
            ? ModelCompareChangeImportance.Info
            : rows.Max(row => row.Importance);
        ConfidenceLevel = rows.Count == 0
            ? ModelCompareConfidenceLevel.High
            : rows.Min(row => row.ConfidenceLevel);
        ChangeSummary = BuildChangeSummary(rows);

        ModelCompareResultRowViewModel? reference = rows.Count > 0 ? rows[0] : null;
        string newLabel = (reference?.NewLabel ?? "").Trim();
        string oldLabel = (reference?.OldLabel ?? "").Trim();
        DisplayName = !string.IsNullOrWhiteSpace(newLabel) ? newLabel
            : !string.IsNullOrWhiteSpace(oldLabel) ? oldLabel
            : ObjectName;
        TraceText = BuildTraceText(reference);

        SearchText = BuildSearchText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModelCompareObjectType ObjectType { get; }
    public ModelCompareMemberType MemberType { get; }
    public string Story { get; }
    public string ObjectName { get; }
    public string Location { get; }
    public IReadOnlyList<ModelCompareResultRowViewModel> Rows { get; }
    public ModelCompareChangeType PrimaryChangeType { get; }
    public ModelCompareChangeImportance Importance { get; }
    public ModelCompareConfidenceLevel ConfidenceLevel { get; }
    public string ChangeSummary { get; }
    public string SearchText { get; }
    public string DisplayName { get; }
    public string TraceText { get; }

    public bool IsSelectableInEtabs => ObjectType is ModelCompareObjectType.Frame or ModelCompareObjectType.Area or ModelCompareObjectType.Joint;

    public string MemberTypeText => MemberType switch
    {
        ModelCompareMemberType.Beam => "Beam",
        ModelCompareMemberType.Column => "Column",
        ModelCompareMemberType.Brace => "Brace",
        ModelCompareMemberType.Area => "Area / shell",
        ModelCompareMemberType.Other => "Other",
        _ => "Definition"
    };

    public string ObjectTypeText => ObjectType switch
    {
        ModelCompareObjectType.Frame => "Frame",
        ModelCompareObjectType.Area => "Area",
        ModelCompareObjectType.FrameProperty => "Frame property",
        ModelCompareObjectType.AreaProperty => "Area property",
        ModelCompareObjectType.Material => "Material",
        _ => ObjectType.ToString()
    };

    public string StoryGroup => string.IsNullOrWhiteSpace(Story) ? "(model-wide)" : Story;

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

    private static ModelCompareChangeType DerivePrimaryChangeType(IReadOnlyList<ModelCompareResultRowViewModel> rows)
    {
        if (rows.Any(row => row.ChangeType == ModelCompareChangeType.Added))
            return ModelCompareChangeType.Added;
        if (rows.Any(row => row.ChangeType == ModelCompareChangeType.Removed))
            return ModelCompareChangeType.Removed;
        if (rows.Any(row => row.ChangeType == ModelCompareChangeType.Moved))
            return ModelCompareChangeType.Moved;
        return ModelCompareChangeType.Modified;
    }

    private static string BuildChangeSummary(IReadOnlyList<ModelCompareResultRowViewModel> rows)
    {
        var tokens = new List<string>();
        foreach (ModelCompareResultRowViewModel row in rows)
        {
            string token = row.ChangeType switch
            {
                ModelCompareChangeType.Added => "Added",
                ModelCompareChangeType.Removed => "Removed",
                ModelCompareChangeType.Moved => row.MovementDistance.HasValue
                    ? $"Moved {row.MovementDistance.Value.ToString("0.###", CultureInfo.InvariantCulture)} m"
                    : "Moved",
                _ => $"{DescribeField(row.ObjectDescription)}: {Blank(row.OldValue)} → {Blank(row.NewValue)}"
            };

            if (!tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                tokens.Add(token);
        }

        return string.Join("   ·   ", tokens);
    }

    private static string DescribeField(string objectDescription)
    {
        int index = objectDescription.LastIndexOf(" / ", StringComparison.Ordinal);
        string field = index >= 0 ? objectDescription[(index + 3)..] : objectDescription;
        field = field.Trim();
        if (field.Length == 0)
            return "Changed";

        return char.ToUpperInvariant(field[0]) + field[1..];
    }

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    private static string BuildTraceText(ModelCompareResultRowViewModel? reference)
    {
        if (reference == null)
            return "";

        var lines = new List<string>();
        string names = FormatOldNew(reference.OldEtabsObjectName, reference.NewEtabsObjectName);
        if (names.Length > 0)
            lines.Add($"ETABS name: {names}");

        string ids = FormatOldNew(ShortId(reference.OldUid), ShortId(reference.NewUid));
        if (ids.Length > 0)
            lines.Add($"Tracking ID: {ids}");

        if (!string.IsNullOrWhiteSpace(reference.NewObjectLocation))
            lines.Add($"Location: {reference.NewObjectLocation}");
        else if (!string.IsNullOrWhiteSpace(reference.OldObjectLocation))
            lines.Add($"Location: {reference.OldObjectLocation}");

        return string.Join("\n", lines);
    }

    private static string FormatOldNew(string oldValue, string newValue)
    {
        string left = (oldValue ?? "").Trim();
        string right = (newValue ?? "").Trim();
        if (left.Length == 0 && right.Length == 0)
            return "";
        if (left.Length == 0)
            return right;
        if (right.Length == 0)
            return left;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? left : $"{left} → {right}";
    }

    private static string ShortId(string? uid)
    {
        string value = (uid ?? "").Trim();
        return value.Length > 8 ? value[..8] : value;
    }

    private string BuildSearchText()
    {
        IEnumerable<string> rowText = Rows.SelectMany(row => new[]
        {
            row.SearchText,
            row.OldValue,
            row.NewValue,
            row.ObjectDescription
        });

        return string.Join(" ", new[]
        {
            ObjectName,
            Story,
            MemberTypeText,
            ObjectTypeText,
            PrimaryChangeType.ToString(),
            ConfidenceLevel.ToString(),
            ChangeSummary
        }.Concat(rowText).Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
