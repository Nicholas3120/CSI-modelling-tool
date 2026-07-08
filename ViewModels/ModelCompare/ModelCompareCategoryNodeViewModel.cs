using CSIModellingTools.Models;

namespace CSIModellingTools.ViewModels;

/// <summary>
/// One row in the Project Explorer tree: a comparison category (or the "All" summary) with its
/// added/removed/modified/unchanged counts. Selecting a node filters the results list to that category.
/// </summary>
public sealed class ModelCompareCategoryNodeViewModel
{
    public ModelCompareCategoryNodeViewModel(
        string displayName,
        string objectTypeFilter,
        int added,
        int removed,
        int modified,
        int unchanged)
    {
        DisplayName = displayName;
        ObjectTypeFilter = objectTypeFilter;
        Added = added;
        Removed = removed;
        Modified = modified;
        Unchanged = unchanged;
    }

    public string DisplayName { get; }

    // Value pushed into the existing object-type filter when this node is selected ("All" for the summary node).
    public string ObjectTypeFilter { get; }

    public int Added { get; }
    public int Removed { get; }
    public int Modified { get; }
    public int Unchanged { get; }
    public int Total => Added + Removed + Modified + Unchanged;

    public bool HasChanges => Added + Removed + Modified > 0;

    public string AddedText => $"+{Added}";
    public string RemovedText => $"-{Removed}";
    public string ModifiedText => $"~{Modified}";
    public string TotalText => Total.ToString();
}
