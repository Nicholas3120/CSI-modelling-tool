using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CSIModellingTools.Models;

public enum EditableRowStatus
{
    Ok,
    New,
    Edited,
    Deleted,
    Error
}

public sealed class LoadCaseCombinationDataRequest
{
    public string? EtabsInstanceId { get; set; }
}

public sealed class LoadCaseCombinationDataResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<EtabsInstanceInfo> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    public List<EtabsLoadPatternRow> LoadPatterns { get; set; } = [];
    public List<EtabsLoadCaseRow> LoadCases { get; set; } = [];
    public List<EtabsLoadCombinationRow> LoadCombinations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class LoadPatternUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
    public string PatternType { get; set; } = "Dead";
    public double SelfWeightMultiplier { get; set; }
}

public sealed class StaticLoadCaseUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
    public string LoadPatternName { get; set; } = "";
    public double ScaleFactor { get; set; } = 1.0;
    public List<StaticLoadCaseItemRow> Items { get; set; } = [];
}

public sealed class LoadCombinationUpdateRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
    public string ComboType { get; set; } = "Linear Additive";
    public List<EtabsComboItemRow> Items { get; set; } = [];
}

public sealed class LoadCaseCombinationDeleteRequest
{
    public string? EtabsInstanceId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class LoadCaseCombinationUpdateResult
{
    public bool IsError { get; set; }
    public string Message { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
}

public abstract class EditableEtabsRow : INotifyPropertyChanged
{
    private EditableRowStatus _status = EditableRowStatus.Ok;
    private string _remarks = "";
    private bool _trackChanges;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditableRowStatus Status
    {
        get => _status;
        set
        {
            if (SetPropertyWithoutTracking(ref _status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status == EditableRowStatus.Ok ? "OK" : Status.ToString();

    public string Remarks
    {
        get => _remarks;
        set => SetPropertyWithoutTracking(ref _remarks, value ?? "");
    }

    public void AcceptChanges()
    {
        _trackChanges = true;
        Status = EditableRowStatus.Ok;
        Remarks = "";
    }

    public void MarkNew()
    {
        _trackChanges = true;
        Status = EditableRowStatus.New;
    }

    public void MarkDeleted()
    {
        Status = EditableRowStatus.Deleted;
    }

    public void MarkError(string message)
    {
        Status = EditableRowStatus.Error;
        Remarks = message;
    }

    protected bool SetTrackedProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetPropertyWithoutTracking(ref field, value, propertyName))
            return false;

        if (_trackChanges && Status == EditableRowStatus.Ok)
            Status = EditableRowStatus.Edited;

        return true;
    }

    protected bool SetPropertyWithoutTracking<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

public sealed class EtabsLoadPatternRow : EditableEtabsRow
{
    private string _name = "";
    private string _patternType = "";
    private double _selfWeightMultiplier;
    private string _usedBy = "";

    public string Name
    {
        get => _name;
        set => SetTrackedProperty(ref _name, value ?? "");
    }

    public string PatternType
    {
        get => _patternType;
        set => SetTrackedProperty(ref _patternType, value ?? "");
    }

    public double SelfWeightMultiplier
    {
        get => _selfWeightMultiplier;
        set => SetTrackedProperty(ref _selfWeightMultiplier, value);
    }

    public string UsedBy
    {
        get => _usedBy;
        set => SetPropertyWithoutTracking(ref _usedBy, value ?? "");
    }

    public EtabsLoadPatternRow CloneAsNew(string name)
    {
        var clone = new EtabsLoadPatternRow
        {
            Name = name,
            PatternType = PatternType,
            SelfWeightMultiplier = SelfWeightMultiplier
        };
        clone.MarkNew();
        return clone;
    }
}

public sealed class StaticLoadCaseItemRow : INotifyPropertyChanged
{
    private string _loadType = "Load";
    private string _name = "";
    private double _scaleFactor = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LoadType
    {
        get => _loadType;
        set => SetProperty(ref _loadType, value ?? "Load");
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    public double ScaleFactor
    {
        get => _scaleFactor;
        set => SetProperty(ref _scaleFactor, value);
    }

    public StaticLoadCaseItemRow Clone()
    {
        return new StaticLoadCaseItemRow
        {
            LoadType = LoadType,
            Name = Name,
            ScaleFactor = ScaleFactor
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

public sealed class EtabsLoadCaseRow : EditableEtabsRow
{
    private string _name = "";
    private string _caseType = "";
    private string _subType = "";
    private string _itemsSummary = "";
    private bool _isEditable = true;
    private ObservableCollection<StaticLoadCaseItemRow> _items = [];
    private bool _updatingItems;

    public EtabsLoadCaseRow()
    {
        _items.CollectionChanged += Items_CollectionChanged;
    }

    public string Name
    {
        get => _name;
        set => SetTrackedProperty(ref _name, value ?? "");
    }

    public string CaseType
    {
        get => _caseType;
        set => SetPropertyWithoutTracking(ref _caseType, value ?? "");
    }

    public string SubType
    {
        get => _subType;
        set => SetPropertyWithoutTracking(ref _subType, value ?? "");
    }

    public string ItemsSummary
    {
        get => _itemsSummary;
        set => SetPropertyWithoutTracking(ref _itemsSummary, value ?? "");
    }

    public bool IsEditable
    {
        get => _isEditable;
        set => SetPropertyWithoutTracking(ref _isEditable, value);
    }

    public ObservableCollection<StaticLoadCaseItemRow> Items
    {
        get => _items;
        set => ReplaceItems(value ?? []);
    }

    public EtabsLoadCaseRow CloneAsNew(string name)
    {
        var clone = new EtabsLoadCaseRow
        {
            Name = name,
            CaseType = "LinearStatic",
            SubType = SubType,
            ItemsSummary = ItemsSummary,
            IsEditable = true,
            Items = new ObservableCollection<StaticLoadCaseItemRow>(Items.Select(item => item.Clone()))
        };
        clone.MarkNew();
        return clone;
    }

    private void ReplaceItems(IEnumerable<StaticLoadCaseItemRow> items)
    {
        _updatingItems = true;
        try
        {
            UnsubscribeItems(_items);
            _items.CollectionChanged -= Items_CollectionChanged;
            _items = new ObservableCollection<StaticLoadCaseItemRow>(items);
            SubscribeItems(_items);
            _items.CollectionChanged += Items_CollectionChanged;
            UpdateItemsSummary();
            OnPropertyChanged(nameof(Items));
        }
        finally
        {
            _updatingItems = false;
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (StaticLoadCaseItemRow item in e.OldItems.OfType<StaticLoadCaseItemRow>())
                item.PropertyChanged -= Item_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (StaticLoadCaseItemRow item in e.NewItems.OfType<StaticLoadCaseItemRow>())
                item.PropertyChanged += Item_PropertyChanged;
        }

        UpdateItemsSummary();
        MarkItemsEdited();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateItemsSummary();
        MarkItemsEdited();
    }

    private void MarkItemsEdited()
    {
        if (!_updatingItems && Status == EditableRowStatus.Ok)
            Status = EditableRowStatus.Edited;
    }

    private void UpdateItemsSummary()
    {
        ItemsSummary = BuildItemsSummary(Items);
    }

    private void SubscribeItems(IEnumerable<StaticLoadCaseItemRow> items)
    {
        foreach (StaticLoadCaseItemRow item in items)
            item.PropertyChanged += Item_PropertyChanged;
    }

    private void UnsubscribeItems(IEnumerable<StaticLoadCaseItemRow> items)
    {
        foreach (StaticLoadCaseItemRow item in items)
            item.PropertyChanged -= Item_PropertyChanged;
    }

    private static string BuildItemsSummary(IEnumerable<StaticLoadCaseItemRow> items)
    {
        List<StaticLoadCaseItemRow> itemList = items.ToList();
        if (itemList.Count == 0)
            return "";

        var parts = new List<string>();
        for (int index = 0; index < itemList.Count; index++)
        {
            StaticLoadCaseItemRow item = itemList[index];
            string name = string.IsNullOrWhiteSpace(item.Name) ? "(blank)" : item.Name.Trim();
            string factor = Math.Abs(item.ScaleFactor).ToString("0.###", CultureInfo.InvariantCulture);

            if (index == 0)
            {
                string sign = item.ScaleFactor < 0 ? "-" : "";
                parts.Add($"{sign}{factor} {name}");
            }
            else
            {
                string sign = item.ScaleFactor < 0 ? "-" : "+";
                parts.Add($"{sign} {factor} {name}");
            }
        }

        return string.Join(" ", parts);
    }
}

public sealed class EtabsLoadCombinationRow : EditableEtabsRow
{
    private string _name = "";
    private string _comboType = "";
    private string _itemsSummary = "";
    private ObservableCollection<EtabsComboItemRow> _items = [];
    private bool _updatingItems;

    public EtabsLoadCombinationRow()
    {
        _items.CollectionChanged += Items_CollectionChanged;
    }

    public string Name
    {
        get => _name;
        set => SetTrackedProperty(ref _name, value ?? "");
    }

    public string ComboType
    {
        get => _comboType;
        set => SetTrackedProperty(ref _comboType, value ?? "");
    }

    public string ItemsSummary
    {
        get => _itemsSummary;
        set => SetPropertyWithoutTracking(ref _itemsSummary, value ?? "");
    }

    public ObservableCollection<EtabsComboItemRow> Items
    {
        get => _items;
        set => ReplaceItems(value ?? []);
    }

    public EtabsLoadCombinationRow CloneAsNew(string name)
    {
        var clone = new EtabsLoadCombinationRow
        {
            Name = name,
            ComboType = ComboType,
            ItemsSummary = ItemsSummary,
            Items = new ObservableCollection<EtabsComboItemRow>(Items.Select(item => item.Clone()))
        };
        clone.MarkNew();
        return clone;
    }

    private void ReplaceItems(IEnumerable<EtabsComboItemRow> items)
    {
        _updatingItems = true;
        try
        {
            UnsubscribeItems(_items);
            _items.CollectionChanged -= Items_CollectionChanged;
            _items = new ObservableCollection<EtabsComboItemRow>(items);
            SubscribeItems(_items);
            _items.CollectionChanged += Items_CollectionChanged;
            UpdateItemsSummary();
            OnPropertyChanged(nameof(Items));
        }
        finally
        {
            _updatingItems = false;
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (EtabsComboItemRow item in e.OldItems.OfType<EtabsComboItemRow>())
                item.PropertyChanged -= Item_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (EtabsComboItemRow item in e.NewItems.OfType<EtabsComboItemRow>())
                item.PropertyChanged += Item_PropertyChanged;
        }

        UpdateItemsSummary();
        MarkItemsEdited();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateItemsSummary();
        MarkItemsEdited();
    }

    private void MarkItemsEdited()
    {
        if (!_updatingItems && Status == EditableRowStatus.Ok)
            Status = EditableRowStatus.Edited;
    }

    private void UpdateItemsSummary()
    {
        ItemsSummary = BuildItemsSummary(Items);
    }

    private void SubscribeItems(IEnumerable<EtabsComboItemRow> items)
    {
        foreach (EtabsComboItemRow item in items)
            item.PropertyChanged += Item_PropertyChanged;
    }

    private void UnsubscribeItems(IEnumerable<EtabsComboItemRow> items)
    {
        foreach (EtabsComboItemRow item in items)
            item.PropertyChanged -= Item_PropertyChanged;
    }

    private static string BuildItemsSummary(IEnumerable<EtabsComboItemRow> items)
    {
        List<EtabsComboItemRow> itemList = items.ToList();
        if (itemList.Count == 0)
            return "";

        var parts = new List<string>();
        for (int index = 0; index < itemList.Count; index++)
        {
            EtabsComboItemRow item = itemList[index];
            string name = string.IsNullOrWhiteSpace(item.Name) ? "(blank)" : item.Name.Trim();
            string factor = Math.Abs(item.Factor).ToString("0.###", CultureInfo.InvariantCulture);
            string sourceTypeLabel = new string((item.SourceType ?? "")
                .Where(char.IsLetterOrDigit)
                .ToArray());
            string sourceSuffix = sourceTypeLabel == "LoadCombo" ||
                sourceTypeLabel == "Combination" ||
                sourceTypeLabel == "Combo"
                ? " combo"
                : "";

            if (index == 0)
            {
                string sign = item.Factor < 0 ? "-" : "";
                parts.Add($"{sign}{factor} {name}{sourceSuffix}");
            }
            else
            {
                string sign = item.Factor < 0 ? "-" : "+";
                parts.Add($"{sign} {factor} {name}{sourceSuffix}");
            }
        }

        return string.Join(" ", parts);
    }
}

public sealed class EtabsComboItemRow : INotifyPropertyChanged
{
    private string _sourceType = "Load Case";
    private string _name = "";
    private double _factor = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceType
    {
        get => _sourceType;
        set => SetProperty(ref _sourceType, value ?? "Load Case");
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    public double Factor
    {
        get => _factor;
        set => SetProperty(ref _factor, value);
    }

    public EtabsComboItemRow Clone()
    {
        return new EtabsComboItemRow
        {
            SourceType = SourceType,
            Name = Name,
            Factor = Factor
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
