using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSIModellingTools.Models;
using CSIModellingTools.Services;

namespace CSIModellingTools.ViewModels;

public sealed class ModelMigrationViewModel : ObservableObject
{
    private const string EtabsProduct = "ETABS";
    private const string Sap2000Product = "SAP2000";

    private readonly EtabsParametricModellingService _etabsService = new();
    private readonly Sap2000ModellingService _sap2000Service = new();
    private readonly List<ModelMigrationPreviewRow> _allPreviewRows = [];
    private string _sourceProduct = EtabsProduct;
    private string _targetProduct = EtabsProduct;
    private CsiModelMigrationInstance? _selectedSourceInstance;
    private CsiModelMigrationInstance? _selectedTargetInstance;
    private CsiModelMigrationInstance? _selectedSourceEtabsInstance;
    private CsiModelMigrationInstance? _selectedSourceSap2000Instance;
    private CsiModelMigrationInstance? _selectedTargetEtabsInstance;
    private CsiModelMigrationInstance? _selectedTargetSap2000Instance;
    private string _sourceStatus = "No source model selected.";
    private string _targetStatus = "No target model selected.";
    private string _sourceEtabsStatus = "Not connected";
    private string _sourceSap2000Status = "Not connected";
    private string _targetEtabsStatus = "Not connected";
    private string _targetSap2000Status = "Not connected";
    private string _migrationStatus = "Ready.";
    private string _previewSearchText = "";
    private ModelMigrationEndpointSnapshot? _sourceSnapshot;
    private ModelMigrationEndpointSnapshot? _targetSnapshot;
    private bool _includeMaterials = true;
    private bool _includeFrameSections = true;
    private bool _includeAreaProperties = true;
    private bool _includeLoadPatterns = true;
    private bool _includeStaticLoadCases = true;
    private bool _includeResponseCombinations = true;
    private bool _includeCableTendonProperties = true;

    public ModelMigrationViewModel()
    {
        RefreshSourceInstancesCommand = new RelayCommand(_ => RefreshInstances(true));
        RefreshTargetInstancesCommand = new RelayCommand(_ => RefreshInstances(false));
        ReadSourceSetupCommand = new RelayCommand(_ => ReadSetup(true));
        ReadTargetSetupCommand = new RelayCommand(_ => ReadSetup(false));
        RefreshSourceEtabsInstancesCommand = new RelayCommand(_ => RefreshInstances(true, EtabsProduct));
        RefreshSourceSap2000InstancesCommand = new RelayCommand(_ => RefreshInstances(true, Sap2000Product));
        RefreshTargetEtabsInstancesCommand = new RelayCommand(_ => RefreshInstances(false, EtabsProduct));
        RefreshTargetSap2000InstancesCommand = new RelayCommand(_ => RefreshInstances(false, Sap2000Product));
        ReadSourceEtabsSetupCommand = new RelayCommand(_ => ReadSetup(true, EtabsProduct));
        ReadSourceSap2000SetupCommand = new RelayCommand(_ => ReadSetup(true, Sap2000Product));
        ReadTargetEtabsSetupCommand = new RelayCommand(_ => ReadSetup(false, EtabsProduct));
        ReadTargetSap2000SetupCommand = new RelayCommand(_ => ReadSetup(false, Sap2000Product));
        PreviewMigrationCommand = new RelayCommand(_ => PreviewMigration());
        ApplyMigrationCommand = new RelayCommand(_ => ApplyMigration(), _ => CanApplyMigration());
        ClearPreviewSearchCommand = new RelayCommand(_ => PreviewSearchText = "", _ => !string.IsNullOrWhiteSpace(PreviewSearchText));
    }

    public IReadOnlyList<string> Products { get; } = [EtabsProduct, Sap2000Product];
    public ObservableCollection<CsiModelMigrationInstance> SourceInstances { get; } = [];
    public ObservableCollection<CsiModelMigrationInstance> TargetInstances { get; } = [];
    public ObservableCollection<CsiModelMigrationInstance> SourceEtabsInstances { get; } = [];
    public ObservableCollection<CsiModelMigrationInstance> SourceSap2000Instances { get; } = [];
    public ObservableCollection<CsiModelMigrationInstance> TargetEtabsInstances { get; } = [];
    public ObservableCollection<CsiModelMigrationInstance> TargetSap2000Instances { get; } = [];
    public ObservableCollection<ModelMigrationPreviewRow> PreviewRows { get; } = [];
    public ObservableCollection<ValidationIssue> Messages { get; } = [];

    public ICommand RefreshSourceInstancesCommand { get; }
    public ICommand RefreshTargetInstancesCommand { get; }
    public ICommand ReadSourceSetupCommand { get; }
    public ICommand ReadTargetSetupCommand { get; }
    public ICommand RefreshSourceEtabsInstancesCommand { get; }
    public ICommand RefreshSourceSap2000InstancesCommand { get; }
    public ICommand RefreshTargetEtabsInstancesCommand { get; }
    public ICommand RefreshTargetSap2000InstancesCommand { get; }
    public ICommand ReadSourceEtabsSetupCommand { get; }
    public ICommand ReadSourceSap2000SetupCommand { get; }
    public ICommand ReadTargetEtabsSetupCommand { get; }
    public ICommand ReadTargetSap2000SetupCommand { get; }
    public ICommand PreviewMigrationCommand { get; }
    public ICommand ApplyMigrationCommand { get; }
    public ICommand ClearPreviewSearchCommand { get; }

    public string SourceProduct
    {
        get => _sourceProduct;
        set
        {
            if (SetProperty(ref _sourceProduct, NormalizeProduct(value)))
                ResetEndpoint(true);
        }
    }

    public string TargetProduct
    {
        get => _targetProduct;
        set
        {
            if (SetProperty(ref _targetProduct, NormalizeProduct(value)))
                ResetEndpoint(false);
        }
    }

    public CsiModelMigrationInstance? SelectedSourceInstance
    {
        get => _selectedSourceInstance;
        set
        {
            if (SetProperty(ref _selectedSourceInstance, value))
                ClearSnapshot(true);
        }
    }

    public CsiModelMigrationInstance? SelectedTargetInstance
    {
        get => _selectedTargetInstance;
        set
        {
            if (SetProperty(ref _selectedTargetInstance, value))
                ClearSnapshot(false);
        }
    }

    public CsiModelMigrationInstance? SelectedSourceEtabsInstance
    {
        get => _selectedSourceEtabsInstance;
        set
        {
            if (SetProperty(ref _selectedSourceEtabsInstance, value))
                ClearSnapshot(true);
        }
    }

    public CsiModelMigrationInstance? SelectedSourceSap2000Instance
    {
        get => _selectedSourceSap2000Instance;
        set
        {
            if (SetProperty(ref _selectedSourceSap2000Instance, value))
                ClearSnapshot(true);
        }
    }

    public CsiModelMigrationInstance? SelectedTargetEtabsInstance
    {
        get => _selectedTargetEtabsInstance;
        set
        {
            if (SetProperty(ref _selectedTargetEtabsInstance, value))
                ClearSnapshot(false);
        }
    }

    public CsiModelMigrationInstance? SelectedTargetSap2000Instance
    {
        get => _selectedTargetSap2000Instance;
        set
        {
            if (SetProperty(ref _selectedTargetSap2000Instance, value))
                ClearSnapshot(false);
        }
    }

    public string SourceStatus
    {
        get => _sourceStatus;
        set => SetProperty(ref _sourceStatus, value ?? "");
    }

    public string TargetStatus
    {
        get => _targetStatus;
        set => SetProperty(ref _targetStatus, value ?? "");
    }

    public string SourceEtabsStatus
    {
        get => _sourceEtabsStatus;
        set => SetProperty(ref _sourceEtabsStatus, value ?? "");
    }

    public string SourceSap2000Status
    {
        get => _sourceSap2000Status;
        set => SetProperty(ref _sourceSap2000Status, value ?? "");
    }

    public string TargetEtabsStatus
    {
        get => _targetEtabsStatus;
        set => SetProperty(ref _targetEtabsStatus, value ?? "");
    }

    public string TargetSap2000Status
    {
        get => _targetSap2000Status;
        set => SetProperty(ref _targetSap2000Status, value ?? "");
    }

    public string MigrationStatus
    {
        get => _migrationStatus;
        set => SetProperty(ref _migrationStatus, value ?? "");
    }

    public string PreviewSearchText
    {
        get => _previewSearchText;
        set
        {
            if (SetProperty(ref _previewSearchText, value ?? ""))
                ApplyPreviewSearch();
        }
    }

    public string PreviewSearchSummary
    {
        get
        {
            if (_allPreviewRows.Count == 0)
                return "No preview items.";

            return string.IsNullOrWhiteSpace(PreviewSearchText)
                ? $"Showing {PreviewRows.Count} item(s)."
                : $"Showing {PreviewRows.Count} of {_allPreviewRows.Count} item(s).";
        }
    }

    public string SourceSummary => _sourceSnapshot?.Summary ?? "No setup data read.";
    public string TargetSummary => _targetSnapshot?.Summary ?? "No setup data read.";

    public bool IncludeMaterials
    {
        get => _includeMaterials;
        set => SetScopeProperty(ref _includeMaterials, value);
    }

    public bool IncludeFrameSections
    {
        get => _includeFrameSections;
        set => SetScopeProperty(ref _includeFrameSections, value);
    }

    public bool IncludeAreaProperties
    {
        get => _includeAreaProperties;
        set => SetScopeProperty(ref _includeAreaProperties, value);
    }

    public bool IncludeLoadPatterns
    {
        get => _includeLoadPatterns;
        set => SetScopeProperty(ref _includeLoadPatterns, value);
    }

    public bool IncludeStaticLoadCases
    {
        get => _includeStaticLoadCases;
        set => SetScopeProperty(ref _includeStaticLoadCases, value);
    }

    public bool IncludeResponseCombinations
    {
        get => _includeResponseCombinations;
        set => SetScopeProperty(ref _includeResponseCombinations, value);
    }

    public bool IncludeCableTendonProperties
    {
        get => _includeCableTendonProperties;
        set => SetScopeProperty(ref _includeCableTendonProperties, value);
    }

    private void RefreshInstances(bool source)
    {
        string product = source ? SourceProduct : TargetProduct;
        RefreshInstancesPair(product);
    }

    private void RefreshInstances(bool source, string product)
    {
        RefreshInstancesPair(product);
    }

    private void RefreshInstancesPair(string product)
    {
        product = NormalizeProduct(product);
        var warnings = new List<string>();
        bool isError;
        string message;
        List<CsiModelMigrationInstance> instances;

        if (product == EtabsProduct)
        {
            EtabsInstanceListResult result = _etabsService.ListEtabsInstances();
            isError = result.IsError;
            message = result.Message;
            warnings.AddRange(result.Warnings);
            instances = result.Instances.Select(ToMigrationInstance).ToList();
        }
        else
        {
            Sap2000InstanceListResult result = _sap2000Service.ListSap2000Instances();
            isError = result.IsError;
            message = result.Message;
            warnings.AddRange(result.Warnings);
            instances = result.Instances.Select(ToMigrationInstance).ToList();
        }

        string sourcePreviousId = GetSelectedEndpointInstance(true, product)?.Id ?? "";
        string targetPreviousId = GetSelectedEndpointInstance(false, product)?.Id ?? "";
        ReplaceEndpointInstances(true, product, instances, sourcePreviousId);
        ReplaceEndpointInstances(false, product, instances, targetPreviousId);
        SetEndpointStatus(true, product, message);
        SetEndpointStatus(false, product, message);

        string summary = isError
            ? message
            : $"Refreshed {instances.Count} {product} instance(s) for From Model and To Model.";
        ShowMessages(warnings, isError ? ValidationSeverity.Critical : ValidationSeverity.Info, summary);
    }

    private void ReadSetup(bool source)
    {
        string product = source ? SourceProduct : TargetProduct;
        ReadSetup(source, product);
    }

    private void ReadSetup(bool source, string product)
    {
        product = NormalizeProduct(product);
        string sourceProduct = source ? product : ResolveReadProduct(true, product);
        string targetProduct = source ? ResolveReadProduct(false, product) : product;
        ReadSetupPair(sourceProduct, targetProduct);
    }

    private void ReadSetupPair(string sourceProduct, string targetProduct)
    {
        sourceProduct = NormalizeProduct(sourceProduct);
        targetProduct = NormalizeProduct(targetProduct);
        var warnings = new List<string>();
        bool hasError = false;
        bool hasWarning = false;
        int attemptedCount = 0;
        int readCount = 0;

        readCount += ReadSetupEndpoint(true, sourceProduct, warnings, ref hasError, ref hasWarning, ref attemptedCount) ? 1 : 0;
        readCount += ReadSetupEndpoint(false, targetProduct, warnings, ref hasError, ref hasWarning, ref attemptedCount) ? 1 : 0;

        string summary = attemptedCount == 0
            ? "No From/To model selected."
            : BuildReadSetupSummary(sourceProduct, targetProduct, readCount);

        if (readCount == 1 && hasWarning)
            summary += " The other side was not read.";

        ValidationSeverity severity = hasError
            ? ValidationSeverity.Critical
            : hasWarning
                ? ValidationSeverity.Warning
                : ValidationSeverity.Info;

        ShowMessages(warnings, severity, summary);
        CommandManager.InvalidateRequerySuggested();
    }

    private string ResolveReadProduct(bool source, string preferredProduct)
    {
        preferredProduct = NormalizeProduct(preferredProduct);
        if (GetSelectedEndpointInstance(source, preferredProduct) != null)
            return preferredProduct;

        string alternateProduct = preferredProduct == EtabsProduct ? Sap2000Product : EtabsProduct;
        return GetSelectedEndpointInstance(source, alternateProduct) != null
            ? alternateProduct
            : preferredProduct;
    }

    private static string BuildReadSetupSummary(string sourceProduct, string targetProduct, int readCount)
    {
        if (readCount == 2)
        {
            return sourceProduct == targetProduct
                ? $"Read {sourceProduct} setup data for From Model and To Model."
                : $"Read From Model {sourceProduct} setup data and To Model {targetProduct} setup data.";
        }

        return readCount == 1
            ? "Read setup data for one model side."
            : "Could not read setup data for the selected model side(s).";
    }

    private bool ReadSetupEndpoint(bool source, string product, List<string> warnings, ref bool hasError, ref bool hasWarning, ref int attemptedCount)
    {
        product = NormalizeProduct(product);
        CsiModelMigrationInstance? selected = GetSelectedEndpointInstance(source, product);
        if (selected == null)
        {
            string role = source ? "source" : "target";
            string message = $"Select a {role} {product} model first.";
            SetEndpointStatus(source, product, message);
            warnings.Add(message);
            hasWarning = true;
            return false;
        }

        attemptedCount++;
        EndpointReadResult result = product == EtabsProduct
            ? ReadEtabsSetup(source, selected.Id)
            : ReadSap2000Setup(source, selected.Id);

        if (result.IsError)
        {
            string role = source ? "From Model" : "To Model";
            warnings.Add($"{role}: {result.Message}");
            hasError = true;
            return false;
        }

        warnings.AddRange(result.Warnings);
        return true;
    }

    private EndpointReadResult ReadEtabsSetup(bool source, string instanceId)
    {
        SectionPropertyDataResult propertyResult = _etabsService.ListSectionPropertyData(new SectionPropertyDataRequest
        {
            EtabsInstanceId = instanceId
        });
        LoadCaseCombinationDataResult loadResult = _etabsService.ListLoadCaseCombinationData(new LoadCaseCombinationDataRequest
        {
            EtabsInstanceId = string.IsNullOrWhiteSpace(propertyResult.SelectedInstanceId)
                ? instanceId
                : propertyResult.SelectedInstanceId
        });

        string selectedId = FirstNonBlank(propertyResult.SelectedInstanceId, loadResult.SelectedInstanceId, instanceId);
        List<CsiModelMigrationInstance> instances = propertyResult.Instances.Count > 0
            ? propertyResult.Instances.Select(ToMigrationInstance).ToList()
            : loadResult.Instances.Select(ToMigrationInstance).ToList();
        ReplaceEndpointInstances(source, EtabsProduct, instances, selectedId);

        List<EtabsLoadCaseRow> staticLoadCases = loadResult.LoadCases
            .Where(IsMigratableStaticLoadCase)
            .ToList();

        var snapshot = new ModelMigrationEndpointSnapshot
        {
            Product = EtabsProduct,
            InstanceId = selectedId,
            Materials = propertyResult.Materials.Count,
            FrameSections = propertyResult.FrameProperties.Count,
            AreaProperties = propertyResult.AreaProperties.Count,
            LoadPatterns = loadResult.LoadPatterns.Count,
            StaticLoadCases = staticLoadCases.Count,
            ResponseCombinations = loadResult.LoadCombinations.Count
        };
        snapshot.SetMaterialRows(propertyResult.Materials);
        snapshot.SetFrameRows(propertyResult.FrameProperties);
        snapshot.SetAreaRows(propertyResult.AreaProperties);
        snapshot.SetLoadPatternRows(loadResult.LoadPatterns);
        snapshot.SetStaticLoadCaseRows(staticLoadCases);
        snapshot.SetLoadCombinationRows(loadResult.LoadCombinations);

        SetEndpointSnapshot(source, snapshot);
        bool isError = propertyResult.IsError || loadResult.IsError;
        string message = isError
            ? FirstNonBlank(propertyResult.Message, loadResult.Message, "ETABS setup data could not be read.")
            : "Read ETABS setup data.";
        SetEndpointStatus(source, EtabsProduct, message);

        List<string> warnings = propertyResult.Warnings.Concat(loadResult.Warnings).ToList();
        return new EndpointReadResult(isError, message, warnings);
    }

    private EndpointReadResult ReadSap2000Setup(bool source, string instanceId)
    {
        SectionPropertyDataResult propertyResult = _sap2000Service.ListSectionPropertyData(new SectionPropertyDataRequest
        {
            Sap2000InstanceId = instanceId
        });
        LoadCaseCombinationDataResult loadResult = _sap2000Service.ListLoadCaseCombinationData(new LoadCaseCombinationDataRequest
        {
            Sap2000InstanceId = string.IsNullOrWhiteSpace(propertyResult.SelectedInstanceId)
                ? instanceId
                : propertyResult.SelectedInstanceId
        });
        Sap2000ModelDataResult modelResult = _sap2000Service.ListModelData(new Sap2000ModelDataRequest
        {
            Sap2000InstanceId = FirstNonBlank(propertyResult.SelectedInstanceId, loadResult.SelectedInstanceId, instanceId)
        });

        string selectedId = FirstNonBlank(propertyResult.SelectedInstanceId, loadResult.SelectedInstanceId, modelResult.SelectedInstanceId, instanceId);
        List<CsiModelMigrationInstance> instances = propertyResult.Sap2000Instances.Count > 0
            ? propertyResult.Sap2000Instances.Select(ToMigrationInstance).ToList()
            : modelResult.Instances.Select(ToMigrationInstance).ToList();
        ReplaceEndpointInstances(source, Sap2000Product, instances, selectedId);

        List<EtabsLoadCaseRow> staticLoadCases = loadResult.LoadCases
            .Where(IsMigratableStaticLoadCase)
            .ToList();

        var snapshot = new ModelMigrationEndpointSnapshot
        {
            Product = Sap2000Product,
            InstanceId = selectedId,
            Materials = propertyResult.Materials.Count,
            FrameSections = propertyResult.FrameProperties.Count,
            AreaProperties = propertyResult.AreaProperties.Count,
            LoadPatterns = loadResult.LoadPatterns.Count,
            StaticLoadCases = staticLoadCases.Count,
            ResponseCombinations = loadResult.LoadCombinations.Count,
            CableSections = modelResult.CableSections.Count,
            TendonSections = modelResult.TendonSections.Count
        };
        snapshot.SetMaterialRows(propertyResult.Materials);
        snapshot.SetFrameRows(propertyResult.FrameProperties);
        snapshot.SetAreaRows(propertyResult.AreaProperties);
        snapshot.SetLoadPatternRows(loadResult.LoadPatterns);
        snapshot.SetStaticLoadCaseRows(staticLoadCases);
        snapshot.SetLoadCombinationRows(loadResult.LoadCombinations);
        snapshot.SetNames(MigrationScopeDefinition.CableTendonPropertiesKey, modelResult.CableSections.Select(name => "Cable: " + name).Concat(modelResult.TendonSections.Select(name => "Tendon: " + name)));

        SetEndpointSnapshot(source, snapshot);
        bool isError = propertyResult.IsError || loadResult.IsError || modelResult.IsError;
        string message = isError
            ? FirstNonBlank(propertyResult.Message, loadResult.Message, modelResult.Message, "SAP2000 setup data could not be read.")
            : "Read SAP2000 setup data.";
        SetEndpointStatus(source, Sap2000Product, message);
        List<string> warnings = propertyResult.Warnings
            .Concat(loadResult.Warnings)
            .Concat(modelResult.Warnings)
            .ToList();
        return new EndpointReadResult(isError, message, warnings);
    }

    private void PreviewMigration()
    {
        ClearPreviewRows();

        if (_sourceSnapshot == null || _targetSnapshot == null)
        {
            const string message = "Read source and target setup data before preview.";
            MigrationStatus = message;
            ShowMessages([], ValidationSeverity.Critical, message);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        List<MigrationScopeDefinition> selectedScopes = GetSelectedScopes().ToList();
        if (selectedScopes.Count == 0)
        {
            const string message = "Select at least one migration scope.";
            MigrationStatus = message;
            ShowMessages([], ValidationSeverity.Warning, message);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        var warnings = new List<string>();
        int skippedSameNameCount = 0;

        foreach (MigrationScopeDefinition scope in selectedScopes)
        {
            bool supported = _sourceSnapshot.Supports(scope.Key) && _targetSnapshot.Supports(scope.Key);
            if (!supported)
            {
                warnings.Add($"{scope.Label}: not supported for {_sourceSnapshot.Product} to {_targetSnapshot.Product}.");
                continue;
            }

            skippedSameNameCount += AddFromModelItemPreviewRows(scope, warnings);
        }

        if (_allPreviewRows.Count == 0)
        {
            const string message = "No From Model items found without the same name in To Model.";
            MigrationStatus = message;
            ShowMessages(warnings, ValidationSeverity.Warning, message);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        MigrationStatus = skippedSameNameCount > 0
            ? $"Preview ready. Found {_allPreviewRows.Count} From Model item(s); {skippedSameNameCount} same-name item(s) hidden."
            : $"Preview ready. Found {_allPreviewRows.Count} From Model item(s).";
        ShowMessages(warnings, ValidationSeverity.Info, MigrationStatus);
        CommandManager.InvalidateRequerySuggested();
    }

    private int AddFromModelItemPreviewRows(MigrationScopeDefinition scope, ICollection<string> warnings)
    {
        if (_sourceSnapshot == null || _targetSnapshot == null)
            return 0;

        IReadOnlyList<string> sourceItems = _sourceSnapshot.NamesFor(scope.Key);
        HashSet<string> targetItems = _targetSnapshot.NamesFor(scope.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sourceItems.Count == 0)
        {
            warnings.Add($"{scope.Label}: From Model has no items.");
            return 0;
        }

        int skippedSameNameCount = 0;
        foreach (string itemName in sourceItems)
        {
            if (targetItems.Contains(itemName))
            {
                skippedSameNameCount++;
                continue;
            }

            AddPreviewRow(new ModelMigrationPreviewRow
            {
                Include = false,
                ScopeKey = scope.Key,
                Category = scope.Label,
                ItemName = itemName,
                Status = "No same name in To Model",
                Action = "Check to migrate"
            });
        }

        if (skippedSameNameCount == sourceItems.Count)
        {
            warnings.Add($"{scope.Label}: all From Model names already exist in To Model.");
        }

        return skippedSameNameCount;
    }

    private bool CanApplyMigration()
    {
        return _sourceSnapshot != null &&
            _targetSnapshot != null &&
            _allPreviewRows.Any(row => row.Include);
    }

    private void ApplyMigration()
    {
        if (_sourceSnapshot == null || _targetSnapshot == null)
        {
            const string message = "Read source and target setup data before applying migration.";
            MigrationStatus = message;
            ShowMessages([], ValidationSeverity.Critical, message);
            return;
        }

        List<ModelMigrationPreviewRow> selectedRows = _allPreviewRows
            .Where(row => row.Include)
            .OrderBy(row => ScopeApplyOrder(row.ScopeKey))
            .ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedRows.Count == 0)
        {
            const string message = "Check at least one From Model item before applying migration.";
            MigrationStatus = message;
            ShowMessages([], ValidationSeverity.Warning, message);
            return;
        }

        var messages = new List<string>();
        int appliedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        foreach (ModelMigrationPreviewRow row in selectedRows)
        {
            MigrationApplyResult result = ApplyMigrationRow(row);
            messages.AddRange(result.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));

            if (result.IsError)
            {
                failedCount++;
                row.Status = "Failed";
                row.Action = result.Message;
                if (!string.IsNullOrWhiteSpace(result.Message))
                    messages.Add($"{row.Category} '{row.ItemName}': {result.Message}");
                continue;
            }

            if (result.IsSkipped)
            {
                skippedCount++;
                row.Include = false;
                row.Status = "Skipped";
                row.Action = result.Message;
                if (!string.IsNullOrWhiteSpace(result.Message))
                    messages.Add($"{row.Category} '{row.ItemName}': {result.Message}");
                continue;
            }

            appliedCount++;
            row.Include = false;
            row.Status = "Migrated";
            row.Action = "Applied";
            _targetSnapshot.AddName(row.ScopeKey, row.ItemName);
            if (!string.IsNullOrWhiteSpace(result.Message))
                messages.Add(result.Message);
        }

        MigrationStatus = failedCount == 0
            ? $"Applied {appliedCount} item(s); skipped {skippedCount}."
            : $"Applied {appliedCount} item(s); skipped {skippedCount}; failed {failedCount}.";
        ShowMessages(messages, failedCount > 0 ? ValidationSeverity.Critical : ValidationSeverity.Info, MigrationStatus);
        ApplyPreviewSearch();
        CommandManager.InvalidateRequerySuggested();
    }

    private MigrationApplyResult ApplyMigrationRow(ModelMigrationPreviewRow row)
    {
        if (_sourceSnapshot == null || _targetSnapshot == null)
            return MigrationApplyResult.Error("Migration data is not ready.");

        if (_targetSnapshot.ContainsName(row.ScopeKey, row.ItemName))
            return MigrationApplyResult.Skipped("Same name already exists in To Model.");

        return row.ScopeKey switch
        {
            MigrationScopeDefinition.MaterialsKey => ApplyMaterialMigration(row.ItemName),
            MigrationScopeDefinition.FrameSectionsKey => ApplyFrameSectionMigration(row.ItemName),
            MigrationScopeDefinition.AreaPropertiesKey => ApplyAreaPropertyMigration(row.ItemName),
            MigrationScopeDefinition.LoadPatternsKey => ApplyLoadPatternMigration(row.ItemName),
            MigrationScopeDefinition.StaticLoadCasesKey => ApplyStaticLoadCaseMigration(row.ItemName),
            MigrationScopeDefinition.ResponseCombinationsKey => ApplyLoadCombinationMigration(row.ItemName),
            _ => MigrationApplyResult.Error("This migration scope is not supported for apply.")
        };
    }

    private MigrationApplyResult ApplyMaterialMigration(string itemName)
    {
        if (_sourceSnapshot == null || !_sourceSnapshot.TryGetMaterial(itemName, out EtabsMaterialPropertyRow? material) || material == null)
            return MigrationApplyResult.Error("Source material details were not found.");

        var request = new MaterialPropertyUpdateRequest
        {
            EtabsInstanceId = TargetEtabsInstanceId,
            Sap2000InstanceId = TargetSap2000InstanceId,
            Name = material.Name,
            MaterialType = material.MaterialType,
            ElasticModulusMpa = EnsurePositiveOrDefault(material.ElasticModulusMpa, 30000.0),
            PoissonRatio = double.IsFinite(material.PoissonRatio) ? material.PoissonRatio : 0.2,
            UnitWeightKnPerM3 = double.IsFinite(material.UnitWeightKnPerM3) ? material.UnitWeightKnPerM3 : 0.0
        };

        return ApplyTargetSectionUpdate(request,
            etabsRequest => _etabsService.UpdateMaterialProperty(etabsRequest),
            sapRequest => _sap2000Service.UpdateMaterialProperty(sapRequest));
    }

    private MigrationApplyResult ApplyFrameSectionMigration(string itemName)
    {
        if (_sourceSnapshot == null || !_sourceSnapshot.TryGetFrame(itemName, out EtabsFramePropertyRow? frame) || frame == null)
            return MigrationApplyResult.Error("Source frame/member definition details were not found.");

        if (frame.DepthMm <= 0)
            return MigrationApplyResult.Error("Source frame/member definition geometry was not available for this section type.");

        var request = new FramePropertyUpdateRequest
        {
            EtabsInstanceId = TargetEtabsInstanceId,
            Sap2000InstanceId = TargetSap2000InstanceId,
            Name = frame.Name,
            ShapeType = frame.ShapeType,
            MaterialName = frame.MaterialName,
            Depth = MmToM(frame.DepthMm),
            Width = MmToM(frame.WidthMm),
            FlangeThickness = MmToM(frame.FlangeThicknessMm),
            WebThickness = MmToM(frame.WebThicknessMm)
        };

        return ApplyTargetSectionUpdate(request,
            etabsRequest => _etabsService.UpdateFrameProperty(etabsRequest),
            sapRequest => _sap2000Service.UpdateFrameProperty(sapRequest));
    }

    private MigrationApplyResult ApplyAreaPropertyMigration(string itemName)
    {
        if (_sourceSnapshot == null || !_sourceSnapshot.TryGetArea(itemName, out EtabsAreaPropertyRow? area) || area == null)
            return MigrationApplyResult.Error("Source area property details were not found.");

        var request = new AreaPropertyUpdateRequest
        {
            EtabsInstanceId = TargetEtabsInstanceId,
            Sap2000InstanceId = TargetSap2000InstanceId,
            Name = area.Name,
            AreaType = area.AreaType,
            SlabType = area.AreaType,
            ShellType = area.ShellType,
            MaterialName = area.MaterialName,
            Thickness = area.Thickness
        };

        return ApplyTargetSectionUpdate(request,
            etabsRequest => _etabsService.UpdateAreaProperty(etabsRequest),
            sapRequest => _sap2000Service.UpdateAreaProperty(sapRequest));
    }

    private MigrationApplyResult ApplyLoadPatternMigration(string itemName)
    {
        if (_sourceSnapshot == null || !_sourceSnapshot.TryGetLoadPattern(itemName, out EtabsLoadPatternRow? pattern) || pattern == null)
            return MigrationApplyResult.Error("Source load pattern details were not found.");

        var request = new LoadPatternUpdateRequest
        {
            EtabsInstanceId = TargetEtabsInstanceId,
            Sap2000InstanceId = TargetSap2000InstanceId,
            Name = pattern.Name,
            PatternType = pattern.PatternType,
            SelfWeightMultiplier = pattern.SelfWeightMultiplier
        };

        return ApplyTargetLoadUpdate(request,
            etabsRequest => _etabsService.UpdateLoadPattern(etabsRequest),
            sapRequest => _sap2000Service.UpdateLoadPattern(sapRequest));
    }

    private MigrationApplyResult ApplyStaticLoadCaseMigration(string itemName)
    {
        if (_sourceSnapshot == null || !_sourceSnapshot.TryGetStaticLoadCase(itemName, out EtabsLoadCaseRow? loadCase) || loadCase == null)
            return MigrationApplyResult.Error("Source static load case details were not found.");

        if (!IsMigratableStaticLoadCase(loadCase))
            return MigrationApplyResult.Error("Only linear static load cases can be migrated.");

        var request = new StaticLoadCaseUpdateRequest
        {
            EtabsInstanceId = TargetEtabsInstanceId,
            Sap2000InstanceId = TargetSap2000InstanceId,
            Name = loadCase.Name,
            Items = loadCase.Items.Select(item => item.Clone()).ToList()
        };

        return ApplyTargetLoadUpdate(request,
            etabsRequest => _etabsService.UpdateStaticLoadCase(etabsRequest),
            sapRequest => _sap2000Service.UpdateStaticLoadCase(sapRequest));
    }

    private MigrationApplyResult ApplyLoadCombinationMigration(string itemName)
    {
        if (_sourceSnapshot == null || !_sourceSnapshot.TryGetLoadCombination(itemName, out EtabsLoadCombinationRow? combination) || combination == null)
            return MigrationApplyResult.Error("Source response combination details were not found.");

        var request = new LoadCombinationUpdateRequest
        {
            EtabsInstanceId = TargetEtabsInstanceId,
            Sap2000InstanceId = TargetSap2000InstanceId,
            Name = combination.Name,
            ComboType = combination.ComboType,
            Items = combination.Items.Select(item => item.Clone()).ToList()
        };

        return ApplyTargetLoadUpdate(request,
            etabsRequest => _etabsService.UpdateLoadCombination(etabsRequest),
            sapRequest => _sap2000Service.UpdateLoadCombination(sapRequest));
    }

    private MigrationApplyResult ApplyTargetSectionUpdate<TRequest>(
        TRequest request,
        Func<TRequest, SectionPropertyUpdateResult> etabsApply,
        Func<TRequest, SectionPropertyUpdateResult> sap2000Apply)
    {
        if (_targetSnapshot == null)
            return MigrationApplyResult.Error("Target model is not ready.");

        SectionPropertyUpdateResult result = _targetSnapshot.Product == Sap2000Product
            ? sap2000Apply(request)
            : etabsApply(request);
        return MigrationApplyResult.FromSectionResult(result);
    }

    private MigrationApplyResult ApplyTargetLoadUpdate<TRequest>(
        TRequest request,
        Func<TRequest, LoadCaseCombinationUpdateResult> etabsApply,
        Func<TRequest, LoadCaseCombinationUpdateResult> sap2000Apply)
    {
        if (_targetSnapshot == null)
            return MigrationApplyResult.Error("Target model is not ready.");

        LoadCaseCombinationUpdateResult result = _targetSnapshot.Product == Sap2000Product
            ? sap2000Apply(request)
            : etabsApply(request);
        return MigrationApplyResult.FromLoadResult(result);
    }

    private IEnumerable<MigrationScopeDefinition> GetSelectedScopes()
    {
        if (IncludeMaterials)
            yield return MigrationScopeDefinition.Materials;
        if (IncludeFrameSections)
            yield return MigrationScopeDefinition.FrameSections;
        if (IncludeAreaProperties)
            yield return MigrationScopeDefinition.AreaProperties;
        if (IncludeLoadPatterns)
            yield return MigrationScopeDefinition.LoadPatterns;
        if (IncludeStaticLoadCases)
            yield return MigrationScopeDefinition.StaticLoadCases;
        if (IncludeResponseCombinations)
            yield return MigrationScopeDefinition.ResponseCombinations;
        if (IncludeCableTendonProperties)
            yield return MigrationScopeDefinition.CableTendonProperties;
    }

    private void ResetEndpoint(bool source)
    {
        ObservableCollection<CsiModelMigrationInstance> instances = source ? SourceInstances : TargetInstances;
        instances.Clear();
        if (source)
        {
            SelectedSourceInstance = null;
            SourceStatus = "No source model selected.";
            SourceEtabsInstances.Clear();
            SourceSap2000Instances.Clear();
            SelectedSourceEtabsInstance = null;
            SelectedSourceSap2000Instance = null;
            SourceEtabsStatus = "Not connected";
            SourceSap2000Status = "Not connected";
        }
        else
        {
            SelectedTargetInstance = null;
            TargetStatus = "No target model selected.";
            TargetEtabsInstances.Clear();
            TargetSap2000Instances.Clear();
            SelectedTargetEtabsInstance = null;
            SelectedTargetSap2000Instance = null;
            TargetEtabsStatus = "Not connected";
            TargetSap2000Status = "Not connected";
        }

        ClearSnapshot(source);
    }

    private void ClearSnapshot(bool source)
    {
        if (source)
        {
            _sourceSnapshot = null;
            OnPropertyChanged(nameof(SourceSummary));
        }
        else
        {
            _targetSnapshot = null;
            OnPropertyChanged(nameof(TargetSummary));
        }

        ClearPreviewRows();
        MigrationStatus = "Ready.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetEndpointSnapshot(bool source, ModelMigrationEndpointSnapshot snapshot)
    {
        if (source)
        {
            _sourceSnapshot = snapshot;
            OnPropertyChanged(nameof(SourceSummary));
        }
        else
        {
            _targetSnapshot = snapshot;
            OnPropertyChanged(nameof(TargetSummary));
        }

        ClearPreviewRows();
        MigrationStatus = "Ready.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetEndpointStatus(bool source, string status)
    {
        string product = source ? SourceProduct : TargetProduct;
        SetEndpointStatus(source, product, status);
    }

    private void SetEndpointStatus(bool source, string product, string status)
    {
        product = NormalizeProduct(product);
        if (source)
        {
            SourceStatus = status;
            if (product == EtabsProduct)
                SourceEtabsStatus = status;
            else
                SourceSap2000Status = status;
        }
        else
        {
            TargetStatus = status;
            if (product == EtabsProduct)
                TargetEtabsStatus = status;
            else
                TargetSap2000Status = status;
        }
    }

    private void ReplaceEndpointInstances(bool source, IEnumerable<CsiModelMigrationInstance> instances, string selectedId)
    {
        string product = source ? SourceProduct : TargetProduct;
        ReplaceEndpointInstances(source, product, instances, selectedId);
    }

    private void ReplaceEndpointInstances(bool source, string product, IEnumerable<CsiModelMigrationInstance> instances, string selectedId)
    {
        product = NormalizeProduct(product);
        List<CsiModelMigrationInstance> values = instances.ToList();
        ObservableCollection<CsiModelMigrationInstance> target = GetEndpointInstances(source, product);
        ObservableCollection<CsiModelMigrationInstance> legacyTarget = source ? SourceInstances : TargetInstances;

        ReplaceCollection(target, values);
        ReplaceCollection(legacyTarget, values);

        CsiModelMigrationInstance? selected = target.FirstOrDefault(instance =>
            string.Equals(instance.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? target.FirstOrDefault();

        SetSelectedEndpointInstance(source, product, selected);
        if (source)
            SelectedSourceInstance = selected;
        else
            SelectedTargetInstance = selected;
    }

    private ObservableCollection<CsiModelMigrationInstance> GetEndpointInstances(bool source, string product)
    {
        product = NormalizeProduct(product);
        if (source)
            return product == EtabsProduct ? SourceEtabsInstances : SourceSap2000Instances;

        return product == EtabsProduct ? TargetEtabsInstances : TargetSap2000Instances;
    }

    private CsiModelMigrationInstance? GetSelectedEndpointInstance(bool source, string product)
    {
        product = NormalizeProduct(product);
        if (source)
            return product == EtabsProduct ? SelectedSourceEtabsInstance : SelectedSourceSap2000Instance;

        return product == EtabsProduct ? SelectedTargetEtabsInstance : SelectedTargetSap2000Instance;
    }

    private void SetSelectedEndpointInstance(bool source, string product, CsiModelMigrationInstance? selected)
    {
        product = NormalizeProduct(product);
        if (source)
        {
            if (product == EtabsProduct)
                SelectedSourceEtabsInstance = selected;
            else
                SelectedSourceSap2000Instance = selected;
        }
        else
        {
            if (product == EtabsProduct)
                SelectedTargetEtabsInstance = selected;
            else
                SelectedTargetSap2000Instance = selected;
        }
    }

    private void SetScopeProperty(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            ClearPreviewRows();
            MigrationStatus = "Ready.";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void AddPreviewRow(ModelMigrationPreviewRow row)
    {
        _allPreviewRows.Add(row);
        if (MatchesPreviewSearch(row))
            PreviewRows.Add(row);

        OnPropertyChanged(nameof(PreviewSearchSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearPreviewRows()
    {
        _allPreviewRows.Clear();
        PreviewRows.Clear();
        OnPropertyChanged(nameof(PreviewSearchSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyPreviewSearch()
    {
        PreviewRows.Clear();
        foreach (ModelMigrationPreviewRow row in _allPreviewRows.Where(MatchesPreviewSearch))
            PreviewRows.Add(row);

        OnPropertyChanged(nameof(PreviewSearchSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool MatchesPreviewSearch(ModelMigrationPreviewRow row)
    {
        string searchText = (PreviewSearchText ?? "").Trim();
        if (searchText.Length == 0)
            return true;

        string[] terms = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            ContainsSearchText(row.Category, term) ||
            ContainsSearchText(row.ItemName, term) ||
            ContainsSearchText(row.Status, term) ||
            ContainsSearchText(row.Action, term));
    }

    private static bool ContainsSearchText(string value, string term)
    {
        return value?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ShowMessages(IEnumerable<string> warnings, ValidationSeverity summarySeverity, string summary)
    {
        var issues = new List<ValidationIssue>();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            issues.Add(new ValidationIssue
            {
                Severity = summarySeverity,
                Message = summary
            });
        }

        issues.AddRange(warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = warning
            }));

        ReplaceCollection(Messages, issues);
    }

    private static CsiModelMigrationInstance ToMigrationInstance(EtabsInstanceInfo instance)
    {
        return new CsiModelMigrationInstance
        {
            Product = EtabsProduct,
            Id = instance.Id,
            DisplayName = instance.DisplayName,
            ModelFile = instance.ModelFile
        };
    }

    private static CsiModelMigrationInstance ToMigrationInstance(Sap2000InstanceInfo instance)
    {
        return new CsiModelMigrationInstance
        {
            Product = Sap2000Product,
            Id = instance.Id,
            DisplayName = instance.DisplayName,
            ModelFile = instance.ModelFile
        };
    }

    private static string NormalizeProduct(string? value)
    {
        return string.Equals(value, Sap2000Product, StringComparison.OrdinalIgnoreCase)
            ? Sap2000Product
            : EtabsProduct;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }

    private string? TargetEtabsInstanceId => _targetSnapshot?.Product == EtabsProduct ? _targetSnapshot.InstanceId : null;
    private string? TargetSap2000InstanceId => _targetSnapshot?.Product == Sap2000Product ? _targetSnapshot.InstanceId : null;

    private static bool IsMigratableStaticLoadCase(EtabsLoadCaseRow loadCase)
    {
        return loadCase.IsEditable &&
            string.Equals(loadCase.CaseType, "LinearStatic", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScopeApplyOrder(string scopeKey)
    {
        return scopeKey switch
        {
            MigrationScopeDefinition.MaterialsKey => 0,
            MigrationScopeDefinition.LoadPatternsKey => 1,
            MigrationScopeDefinition.FrameSectionsKey => 2,
            MigrationScopeDefinition.AreaPropertiesKey => 3,
            MigrationScopeDefinition.StaticLoadCasesKey => 4,
            MigrationScopeDefinition.ResponseCombinationsKey => 5,
            _ => 99
        };
    }

    private static double MmToM(double value)
    {
        return value / 1000.0;
    }

    private static double EnsurePositiveOrDefault(double value, double defaultValue)
    {
        return double.IsFinite(value) && value > 0 ? value : defaultValue;
    }

    private sealed record MigrationApplyResult(bool IsError, bool IsSkipped, string Message, List<string> Warnings)
    {
        public static MigrationApplyResult Error(string message)
        {
            return new MigrationApplyResult(true, false, message, []);
        }

        public static MigrationApplyResult Skipped(string message)
        {
            return new MigrationApplyResult(false, true, message, []);
        }

        public static MigrationApplyResult FromSectionResult(SectionPropertyUpdateResult result)
        {
            return new MigrationApplyResult(result.IsError, false, result.Message, result.Warnings);
        }

        public static MigrationApplyResult FromLoadResult(LoadCaseCombinationUpdateResult result)
        {
            return new MigrationApplyResult(result.IsError, false, result.Message, result.Warnings);
        }
    }

    private sealed record EndpointReadResult(bool IsError, string Message, List<string> Warnings);

    private sealed class ModelMigrationEndpointSnapshot
    {
        public string Product { get; init; } = "";
        public string InstanceId { get; init; } = "";
        public int Materials { get; init; }
        public int FrameSections { get; init; }
        public int AreaProperties { get; init; }
        public int LoadPatterns { get; init; }
        public int StaticLoadCases { get; init; }
        public int ResponseCombinations { get; init; }
        public int CableSections { get; init; }
        public int TendonSections { get; init; }
        private Dictionary<string, SortedSet<string>> NamesByScope { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, EtabsMaterialPropertyRow> MaterialsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, EtabsFramePropertyRow> FramesByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, EtabsAreaPropertyRow> AreasByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, EtabsLoadPatternRow> LoadPatternsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, EtabsLoadCaseRow> StaticLoadCasesByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, EtabsLoadCombinationRow> LoadCombinationsByName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Summary => Product == EtabsProduct
            ? $"Materials: {Materials}; frame sections: {FrameSections}; area properties: {AreaProperties}; load patterns: {LoadPatterns}; static cases: {StaticLoadCases}; response combinations: {ResponseCombinations}."
            : $"Materials: {Materials}; frame sections: {FrameSections}; area properties: {AreaProperties}; load patterns: {LoadPatterns}; static cases: {StaticLoadCases}; response combinations: {ResponseCombinations}; cable sections: {CableSections}; tendon sections: {TendonSections}.";

        public int CountFor(string key)
        {
            return key switch
            {
                MigrationScopeDefinition.MaterialsKey => Materials,
                MigrationScopeDefinition.FrameSectionsKey => FrameSections,
                MigrationScopeDefinition.AreaPropertiesKey => AreaProperties,
                MigrationScopeDefinition.LoadPatternsKey => LoadPatterns,
                MigrationScopeDefinition.StaticLoadCasesKey => StaticLoadCases,
                MigrationScopeDefinition.ResponseCombinationsKey => ResponseCombinations,
                MigrationScopeDefinition.CableTendonPropertiesKey => CableSections + TendonSections,
                _ => 0
            };
        }

        public bool Supports(string key)
        {
            return key is MigrationScopeDefinition.MaterialsKey or
                MigrationScopeDefinition.FrameSectionsKey or
                MigrationScopeDefinition.AreaPropertiesKey or
                MigrationScopeDefinition.LoadPatternsKey or
                MigrationScopeDefinition.StaticLoadCasesKey or
                MigrationScopeDefinition.ResponseCombinationsKey;
        }

        public void SetNames(string key, IEnumerable<string> names)
        {
            var normalizedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()))
                normalizedNames.Add(name);

            NamesByScope[key] = normalizedNames;
        }

        public IReadOnlyList<string> NamesFor(string key)
        {
            return NamesByScope.TryGetValue(key, out SortedSet<string>? names)
                ? names.ToList()
                : [];
        }

        public bool ContainsName(string key, string name)
        {
            return NamesByScope.TryGetValue(key, out SortedSet<string>? names) &&
                names.Contains((name ?? "").Trim());
        }

        public void AddName(string key, string name)
        {
            string trimmedName = (name ?? "").Trim();
            if (trimmedName.Length == 0)
                return;

            if (!NamesByScope.TryGetValue(key, out SortedSet<string>? names))
            {
                names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                NamesByScope[key] = names;
            }

            names.Add(trimmedName);
        }

        public void SetMaterialRows(IEnumerable<EtabsMaterialPropertyRow> rows)
        {
            ReplaceMap(MaterialsByName, rows, row => row.Name);
            SetNames(MigrationScopeDefinition.MaterialsKey, MaterialsByName.Keys);
        }

        public void SetFrameRows(IEnumerable<EtabsFramePropertyRow> rows)
        {
            ReplaceMap(FramesByName, rows, row => row.Name);
            SetNames(MigrationScopeDefinition.FrameSectionsKey, FramesByName.Keys);
        }

        public void SetAreaRows(IEnumerable<EtabsAreaPropertyRow> rows)
        {
            ReplaceMap(AreasByName, rows, row => row.Name);
            SetNames(MigrationScopeDefinition.AreaPropertiesKey, AreasByName.Keys);
        }

        public void SetLoadPatternRows(IEnumerable<EtabsLoadPatternRow> rows)
        {
            ReplaceMap(LoadPatternsByName, rows, row => row.Name);
            SetNames(MigrationScopeDefinition.LoadPatternsKey, LoadPatternsByName.Keys);
        }

        public void SetStaticLoadCaseRows(IEnumerable<EtabsLoadCaseRow> rows)
        {
            ReplaceMap(StaticLoadCasesByName, rows, row => row.Name);
            SetNames(MigrationScopeDefinition.StaticLoadCasesKey, StaticLoadCasesByName.Keys);
        }

        public void SetLoadCombinationRows(IEnumerable<EtabsLoadCombinationRow> rows)
        {
            ReplaceMap(LoadCombinationsByName, rows, row => row.Name);
            SetNames(MigrationScopeDefinition.ResponseCombinationsKey, LoadCombinationsByName.Keys);
        }

        public bool TryGetMaterial(string name, out EtabsMaterialPropertyRow? row)
        {
            return MaterialsByName.TryGetValue((name ?? "").Trim(), out row);
        }

        public bool TryGetFrame(string name, out EtabsFramePropertyRow? row)
        {
            return FramesByName.TryGetValue((name ?? "").Trim(), out row);
        }

        public bool TryGetArea(string name, out EtabsAreaPropertyRow? row)
        {
            return AreasByName.TryGetValue((name ?? "").Trim(), out row);
        }

        public bool TryGetLoadPattern(string name, out EtabsLoadPatternRow? row)
        {
            return LoadPatternsByName.TryGetValue((name ?? "").Trim(), out row);
        }

        public bool TryGetStaticLoadCase(string name, out EtabsLoadCaseRow? row)
        {
            return StaticLoadCasesByName.TryGetValue((name ?? "").Trim(), out row);
        }

        public bool TryGetLoadCombination(string name, out EtabsLoadCombinationRow? row)
        {
            return LoadCombinationsByName.TryGetValue((name ?? "").Trim(), out row);
        }

        private static void ReplaceMap<T>(Dictionary<string, T> target, IEnumerable<T> rows, Func<T, string> nameSelector)
        {
            target.Clear();
            foreach (T row in rows)
            {
                string name = (nameSelector(row) ?? "").Trim();
                if (name.Length > 0)
                    target[name] = row;
            }
        }
    }

    private sealed record MigrationScopeDefinition(string Key, string Label)
    {
        public const string MaterialsKey = "Materials";
        public const string FrameSectionsKey = "FrameSections";
        public const string AreaPropertiesKey = "AreaProperties";
        public const string LoadPatternsKey = "LoadPatterns";
        public const string StaticLoadCasesKey = "StaticLoadCases";
        public const string ResponseCombinationsKey = "ResponseCombinations";
        public const string CableTendonPropertiesKey = "CableTendonProperties";

        public static readonly MigrationScopeDefinition Materials = new(MaterialsKey, "Materials");
        public static readonly MigrationScopeDefinition FrameSections = new(FrameSectionsKey, "Frame / member definitions");
        public static readonly MigrationScopeDefinition AreaProperties = new(AreaPropertiesKey, "Area properties");
        public static readonly MigrationScopeDefinition LoadPatterns = new(LoadPatternsKey, "Load patterns");
        public static readonly MigrationScopeDefinition StaticLoadCases = new(StaticLoadCasesKey, "Static load cases");
        public static readonly MigrationScopeDefinition ResponseCombinations = new(ResponseCombinationsKey, "Response combinations");
        public static readonly MigrationScopeDefinition CableTendonProperties = new(CableTendonPropertiesKey, "Cable / tendon definitions");
    }
}

public sealed class CsiModelMigrationInstance
{
    public string Product { get; init; } = "";
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ModelFile { get; init; } = "";

    public string ModelFileDisplay => string.IsNullOrWhiteSpace(ModelFile) ? "(unsaved model)" : ModelFile;
}

public sealed class ModelMigrationPreviewRow : INotifyPropertyChanged
{
    private bool _include;
    private string _scopeKey = "";
    private string _category = "";
    private string _itemName = "";
    private string _status = "";
    private string _action = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Include
    {
        get => _include;
        set
        {
            if (SetProperty(ref _include, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ScopeKey
    {
        get => _scopeKey;
        set => SetProperty(ref _scopeKey, value ?? "");
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value ?? "");
    }

    public string ItemName
    {
        get => _itemName;
        set => SetProperty(ref _itemName, value ?? "");
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value ?? "");
    }

    public string Action
    {
        get => _action;
        set => SetProperty(ref _action, value ?? "");
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
