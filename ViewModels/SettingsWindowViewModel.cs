using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.Localization;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormDesigner.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _source;
    private bool _trackDirty;
    private bool _isRefreshingOptions;

    [ObservableProperty]
    private SettingsTextCatalog texts = SettingsTextCatalog.Russian;

    [ObservableProperty]
    private SettingsSectionModel? selectedSection;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private string interfaceLanguage = SettingsTextCatalog.LanguageRussian;

    [ObservableProperty]
    private string appThemeMode = SettingsTextCatalog.ThemeLight;

    [ObservableProperty]
    private string uiDensityMode = MainWindowViewModel.UiDensityCompact;

    [ObservableProperty]
    private SettingsOptionModel? selectedLanguageOption;

    [ObservableProperty]
    private SettingsOptionModel? selectedThemeOption;

    [ObservableProperty]
    private SettingsOptionModel? selectedDensityOption;

    [ObservableProperty]
    private SettingsOptionModel? selectedSqlAuthenticationOption;

    [ObservableProperty]
    private SettingsOptionModel? selectedLogLevelOption;

    [ObservableProperty]
    private bool confirmNewProjectWithUnsavedChanges = true;

    [ObservableProperty]
    private bool enableRecoveryAutosave = true;

    [ObservableProperty]
    private bool enableExperimentalLayoutTab;

    [ObservableProperty]
    private bool showPropertyTooltips = true;

    [ObservableProperty]
    private bool compactPropertyInspector = true;

    [ObservableProperty]
    private bool showAdvancedProperties;

    [ObservableProperty]
    private bool isDesignerGridVisible = true;

    [ObservableProperty]
    private bool isCanvasSnappingEnabled = true;

    [ObservableProperty]
    private int gridStep = 10;

    [ObservableProperty]
    private bool showPreviewRuntimeBadge;

    [ObservableProperty]
    private bool compactPreviewRuntimeBadge = true;

    [ObservableProperty]
    private bool autoHidePreviewRuntimeBadge = true;

    [ObservableProperty]
    private bool useExportedAxamlPreview;

    [ObservableProperty]
    private bool fallbackToLegacyPreviewOnAxamlError = true;

    [ObservableProperty]
    private bool showGeneratedAxamlOnPreviewError = true;

    [ObservableProperty]
    private bool cleanAxamlPreviewTemporaryFiles = true;

    [ObservableProperty]
    private bool previewTopmost;

    [ObservableProperty]
    private int previewDefaultZoomPercent = 100;

    [ObservableProperty]
    private bool validateBuildAfterExport;

    [ObservableProperty]
    private bool verboseBuildLogs = true;

    [ObservableProperty]
    private bool keepSuccessfulBuildArtifacts = true;

    [ObservableProperty]
    private bool cleanOldArtifactsAutomatically = true;

    [ObservableProperty]
    private bool exportSqlConnectionString;

    [ObservableProperty]
    private int buildTimeoutSeconds = 120;

    [ObservableProperty]
    private bool useCustomNuGetSource;

    [ObservableProperty]
    private string customNuGetSource = "";

    [ObservableProperty]
    private bool allowInsecureNuGetSource;

    [ObservableProperty]
    private bool includeNuGetOrgFallback = true;

    [ObservableProperty]
    private bool generateNuGetConfigInExportedProject = true;

    [ObservableProperty]
    private string nuGetSourceTestStatusText = "";

    [ObservableProperty]
    private bool useGlobalSqlServerSettings = true;

    [ObservableProperty]
    private string sqlServerName = "";

    [ObservableProperty]
    private string sqlDatabaseName = "";

    [ObservableProperty]
    private string sqlAuthenticationMode = SqlServerSettingsModel.AuthWindows;

    [ObservableProperty]
    private string sqlUserName = "";

    [ObservableProperty]
    private string sqlPassword = "";

    [ObservableProperty]
    private bool sqlSavePassword;

    [ObservableProperty]
    private bool sqlTrustServerCertificate = true;

    [ObservableProperty]
    private bool sqlEncryptConnection;

    [ObservableProperty]
    private int sqlConnectionTimeoutSeconds = 15;

    [ObservableProperty]
    private string sqlDefaultSchema = "dbo";

    [ObservableProperty]
    private int sqlDefaultPreviewTopN = 100;

    [ObservableProperty]
    private bool saveLogsToFile = true;

    [ObservableProperty]
    private string logLevel = "Info";

    [ObservableProperty]
    private int maxLogFilesCount = 10;

    [ObservableProperty]
    private int maxLogFileSizeMb = 20;

    [ObservableProperty]
    private string logsFolderPath = "";

    [ObservableProperty]
    private bool enableTraceDiagnostics;

    [ObservableProperty]
    private bool enableDeveloperWarnings = true;

    [ObservableProperty]
    private bool resetLayoutOnNextStart;

    public SettingsWindowViewModel(MainWindowViewModel source, string settingsFilePath)
    {
        _source = source;
        SettingsFilePath = settingsFilePath;
        CopyFromSource();
        Texts = SettingsTextCatalog.ForLanguage(InterfaceLanguage);
        RebuildLocalizedContent(InterfaceLanguage, preserveSelection: false);
        SelectedSection = Sections.FirstOrDefault();
        StatusText = Texts.ReadyStatus;
        PropertyChanged += SettingsWindowViewModel_PropertyChanged;
        _trackDirty = true;
    }

    public ObservableCollection<SettingsSectionModel> Sections { get; } = new();
    public ObservableCollection<SettingsOptionModel> LanguageOptions { get; } = new();
    public ObservableCollection<SettingsOptionModel> ThemeOptions { get; } = new();
    public ObservableCollection<SettingsOptionModel> DensityOptions { get; } = new();
    public ObservableCollection<SettingsOptionModel> SqlAuthenticationOptions { get; } = new();
    public ObservableCollection<SettingsOptionModel> LogLevelOptions { get; } = new();

    public string SettingsFilePath { get; }

    public string UnsavedStatusText => IsDirty ? Texts.StatusUnsaved : Texts.StatusSaved;
    public string UnsavedStatusBackground => IsDirty ? "#FFF7ED" : "#ECFDF5";
    public string UnsavedStatusBorder => IsDirty ? "#FDBA74" : "#86EFAC";
    public string UnsavedStatusForeground => IsDirty ? "#9A3412" : "#166534";
    public string SectionTitle => SelectedSection?.Title ?? "";
    public string SectionSubtitle => SelectedSection?.Subtitle ?? "";
    public string EffectiveNuGetSourceText =>
        UseCustomNuGetSource && !string.IsNullOrWhiteSpace(CustomNuGetSource)
            ? CustomNuGetSource.Trim()
            : ExportPipelineService.DefaultNuGetSourceUrl;
    public string NuGetSummaryText => UseCustomNuGetSource && !string.IsNullOrWhiteSpace(CustomNuGetSource)
        ? Texts.IsEnglish
            ? $"Custom source: {CustomNuGetSource.Trim()}. nuget.org fallback: {(IncludeNuGetOrgFallback ? "enabled" : "disabled")}."
            : $"Custom source: {CustomNuGetSource.Trim()}. Fallback nuget.org: {(IncludeNuGetOrgFallback ? "включён" : "выключен")}."
        : Texts.IsEnglish
            ? $"Default source: {ExportPipelineService.DefaultNuGetSourceUrl}."
            : $"Источник по умолчанию: {ExportPipelineService.DefaultNuGetSourceUrl}.";
    public bool IsSqlLoginSelected => string.Equals(SqlAuthenticationMode, SqlServerSettingsModel.AuthSqlLogin, StringComparison.Ordinal);
    public bool IsSqlConfigured => !string.IsNullOrWhiteSpace(SqlServerName) && !string.IsNullOrWhiteSpace(SqlDatabaseName);
    public string SqlStatusText => IsSqlConfigured
        ? $"{Texts.SqlConfigured}: {SqlServerName.Trim()} / {SqlDatabaseName.Trim()} / {SqlAuthenticationMode}."
        : Texts.SqlNotConfigured;
    public string SqlConnectionPreviewText
    {
        get
        {
            var result = new SqlConnectionStringBuilderService().Build(CaptureSqlSettings());
            return result.Success ? result.MaskedConnectionString : result.ErrorMessage;
        }
    }

    public bool IsGeneralSelected => IsSelected("general");
    public bool IsInterfaceSelected => IsSelected("interface");
    public bool IsPreviewSelected => IsSelected("preview");
    public bool IsExportSelected => IsSelected("export");
    public bool IsNuGetSelected => IsSelected("nuget");
    public bool IsSqlSelected => IsSelected("sql");
    public bool IsLogsSelected => IsSelected("logs");
    public bool IsAdvancedSelected => IsSelected("advanced");

    public event EventHandler? ApplyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? CloseRequested;

    partial void OnSelectedSectionChanged(SettingsSectionModel? value)
    {
        OnPropertyChanged(nameof(IsGeneralSelected));
        OnPropertyChanged(nameof(IsInterfaceSelected));
        OnPropertyChanged(nameof(IsPreviewSelected));
        OnPropertyChanged(nameof(IsExportSelected));
        OnPropertyChanged(nameof(IsNuGetSelected));
        OnPropertyChanged(nameof(IsSqlSelected));
        OnPropertyChanged(nameof(IsLogsSelected));
        OnPropertyChanged(nameof(IsAdvancedSelected));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionSubtitle));

        if (value is not null)
            Debug.WriteLine($"SETTINGS_SECTION_SELECTED section={value.Id}; title={value.Title}");
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(UnsavedStatusText));
        OnPropertyChanged(nameof(UnsavedStatusBackground));
        OnPropertyChanged(nameof(UnsavedStatusBorder));
        OnPropertyChanged(nameof(UnsavedStatusForeground));
    }

    partial void OnSelectedLanguageOptionChanged(SettingsOptionModel? value)
    {
        if (value is null || _isRefreshingOptions)
            return;

        var old = InterfaceLanguage;
        InterfaceLanguage = value.Value;
        Debug.WriteLine($"SETTINGS_LANGUAGE_CHANGED old={old}; new={InterfaceLanguage}");
        RebuildLocalizedContent(InterfaceLanguage, preserveSelection: true);
    }

    partial void OnSelectedThemeOptionChanged(SettingsOptionModel? value)
    {
        if (value is not null && !_isRefreshingOptions)
            AppThemeMode = value.Value;
    }

    partial void OnSelectedDensityOptionChanged(SettingsOptionModel? value)
    {
        if (value is not null && !_isRefreshingOptions)
            UiDensityMode = value.Value;
    }

    partial void OnSelectedSqlAuthenticationOptionChanged(SettingsOptionModel? value)
    {
        if (value is null || _isRefreshingOptions)
            return;

        SqlAuthenticationMode = value.Value;
        OnPropertyChanged(nameof(IsSqlLoginSelected));
        RaiseSqlComputedProperties();
    }

    partial void OnSelectedLogLevelOptionChanged(SettingsOptionModel? value)
    {
        if (value is not null && !_isRefreshingOptions)
            LogLevel = value.Value;
    }

    partial void OnSqlServerNameChanged(string value) => RaiseSqlComputedProperties();
    partial void OnSqlDatabaseNameChanged(string value) => RaiseSqlComputedProperties();
    partial void OnSqlUserNameChanged(string value) => RaiseSqlComputedProperties();
    partial void OnSqlPasswordChanged(string value) => RaiseSqlComputedProperties();
    partial void OnSqlTrustServerCertificateChanged(bool value) => RaiseSqlComputedProperties();
    partial void OnSqlEncryptConnectionChanged(bool value) => RaiseSqlComputedProperties();
    partial void OnSqlConnectionTimeoutSecondsChanged(int value) => RaiseSqlComputedProperties();
    partial void OnUseCustomNuGetSourceChanged(bool value) => RaiseNuGetComputedProperties();
    partial void OnCustomNuGetSourceChanged(string value) => RaiseNuGetComputedProperties();
    partial void OnIncludeNuGetOrgFallbackChanged(bool value) => RaiseNuGetComputedProperties();

    [RelayCommand]
    private void Apply()
    {
        var changedKeys = CountChangedKeys();
        ApplyToSource();
        IsDirty = false;
        StatusText = Texts.AppliedStatus;
        Debug.WriteLine($"SETTINGS_APPLY changedKeys={changedKeys}");
        Debug.WriteLine($"SETTINGS_LANGUAGE_APPLIED language={InterfaceLanguage}; requiresRestart=True");
        ApplyRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Save()
    {
        var changedKeys = CountChangedKeys();
        ApplyToSource();
        IsDirty = false;
        StatusText = Texts.SavedStatus;
        Debug.WriteLine($"SETTINGS_SAVE changedKeys={changedKeys}");
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        var discarded = CountChangedKeys();
        Debug.WriteLine($"SETTINGS_CANCEL discardedChanges={discarded}");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppSettingsModel();
        ApplyGeneral(defaults.General);
        ApplyInterface(defaults.Interface);
        ApplyCanvas(defaults.CanvasEditor);
        ApplyPreview(defaults.Preview);
        ApplyBuildAndLogs(defaults.BuildAndLogs);
        ApplySql(defaults.SqlServer);
        ApplyAdvanced(defaults.Advanced);
        RebuildLocalizedContent(InterfaceLanguage, preserveSelection: true);
        IsDirty = true;
        StatusText = Texts.DefaultsLoadedStatus;
        Debug.WriteLine("SETTINGS_RESET_DEFAULTS scope=All");
    }

    [RelayCommand]
    private void ClearNuGetSource()
    {
        UseCustomNuGetSource = false;
        CustomNuGetSource = "";
        AllowInsecureNuGetSource = false;
        IncludeNuGetOrgFallback = true;
        GenerateNuGetConfigInExportedProject = true;
        NuGetSourceTestStatusText = Texts.IsEnglish
            ? "Custom NuGet source cleared. nuget.org will be used."
            : "Custom NuGet source очищен. Будет использован nuget.org.";
        Debug.WriteLine("NUGET_SETTINGS_CHANGED custom=false; sourceKind=Default");
    }

    [RelayCommand]
    private void TestNuGetSource()
    {
        var source = EffectiveNuGetSourceText;
        Debug.WriteLine($"NUGET_SOURCE_TEST_STARTED source={source}");

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !AllowInsecureNuGetSource)
        {
            NuGetSourceTestStatusText = Texts.IsEnglish
                ? "HTTP source requires allowInsecureConnections."
                : "HTTP source требует включить allowInsecureConnections.";
            Debug.WriteLine($"NUGET_SOURCE_TEST_FAILED source={source}; reason=HTTP requires allowInsecureConnections");
            return;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            NuGetSourceTestStatusText = Texts.IsEnglish
                ? $"Source looks valid: {source}"
                : $"Source выглядит корректно: {source}";
            Debug.WriteLine($"NUGET_SOURCE_TEST_SUCCESS source={source}");
            return;
        }

        if (Directory.Exists(source))
        {
            NuGetSourceTestStatusText = Texts.IsEnglish
                ? $"Local folder found: {source}"
                : $"Локальная папка найдена: {source}";
            Debug.WriteLine($"NUGET_SOURCE_TEST_SUCCESS source={source}");
            return;
        }

        NuGetSourceTestStatusText = Texts.IsEnglish
            ? "Source is not an HTTP/HTTPS URL or existing folder."
            : "Source не похож на HTTP/HTTPS URL или существующую папку.";
        Debug.WriteLine($"NUGET_SOURCE_TEST_FAILED source={source}; reason=Unsupported or missing path");
    }

    [RelayCommand]
    private async Task TestSqlConnectionAsync()
    {
        var settings = CaptureSqlSettings();
        var service = new SqlConnectionStringBuilderService();
        var result = service.Build(settings);
        if (!result.Success)
        {
            StatusText = result.ErrorMessage;
            Debug.WriteLine($"SETTINGS_VALIDATION_FAILED key=SqlConnection; reason={result.ErrorMessage}");
            return;
        }

        StatusText = Texts.CheckingSqlConnection;
        var test = await service.TestConnectionAsync(settings);
        StatusText = test.Message;
    }

    public void SelectSection(string sectionId)
    {
        foreach (var section in Sections)
        {
            if (string.Equals(section.Id, sectionId, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSection = section;
                break;
            }
        }
    }

    public void ApplyToSource()
    {
        _source.InterfaceLanguage = InterfaceLanguage;
        _source.AppThemeMode = AppThemeMode;
        _source.UiDensityMode = UiDensityMode;
        _source.ConfirmNewProjectWithUnsavedChanges = ConfirmNewProjectWithUnsavedChanges;
        _source.EnableRecoveryAutosave = EnableRecoveryAutosave;

        _source.EnableExperimentalLayoutTab = EnableExperimentalLayoutTab;
        _source.ShowPropertyTooltips = ShowPropertyTooltips;
        _source.CompactPropertyInspector = CompactPropertyInspector;
        _source.ShowAdvancedProperties = ShowAdvancedProperties;
        _source.SnapStep = Math.Clamp(GridStep, 1, 200);
        _source.IsDesignerGridVisible = IsDesignerGridVisible;
        _source.IsCanvasSnappingEnabled = IsCanvasSnappingEnabled;

        _source.ShowPreviewRuntimeBadge = ShowPreviewRuntimeBadge;
        _source.CompactPreviewRuntimeBadge = CompactPreviewRuntimeBadge;
        _source.AutoHidePreviewRuntimeBadge = AutoHidePreviewRuntimeBadge;
        _source.UseExportedAxamlPreview = UseExportedAxamlPreview;
        _source.FallbackToLegacyPreviewOnAxamlError = FallbackToLegacyPreviewOnAxamlError;
        _source.ShowGeneratedAxamlOnPreviewError = ShowGeneratedAxamlOnPreviewError;
        _source.CleanAxamlPreviewTemporaryFiles = CleanAxamlPreviewTemporaryFiles;
        _source.PreviewTopmost = PreviewTopmost;
        _source.PreviewDefaultZoomPercent = Math.Clamp(PreviewDefaultZoomPercent, 25, 300);

        _source.ValidateBuildAfterExport = ValidateBuildAfterExport;
        _source.VerboseBuildLogs = VerboseBuildLogs;
        _source.KeepSuccessfulBuildArtifacts = KeepSuccessfulBuildArtifacts;
        _source.CleanOldArtifactsAutomatically = CleanOldArtifactsAutomatically;
        _source.ExportSqlConnectionString = ExportSqlConnectionString;
        _source.BuildTimeoutSeconds = Math.Clamp(BuildTimeoutSeconds, 10, 3600);

        _source.UseCustomNuGetSource = UseCustomNuGetSource;
        _source.CustomNuGetSource = CustomNuGetSource;
        _source.AllowInsecureNuGetSource = AllowInsecureNuGetSource;
        _source.IncludeNuGetOrgFallback = IncludeNuGetOrgFallback;
        _source.GenerateNuGetConfigInExportedProject = GenerateNuGetConfigInExportedProject;

        _source.UseGlobalSqlServerSettings = UseGlobalSqlServerSettings;
        _source.SqlServerName = SqlServerName;
        _source.SqlDatabaseName = SqlDatabaseName;
        _source.SqlAuthenticationMode = SqlConnectionStringBuilderService.NormalizeAuthMode(SqlAuthenticationMode);
        _source.SqlUserName = SqlUserName;
        _source.SqlPassword = SqlPassword;
        _source.SqlSavePassword = SqlSavePassword;
        _source.SqlTrustServerCertificate = SqlTrustServerCertificate;
        _source.SqlEncryptConnection = SqlEncryptConnection;
        _source.SqlConnectionTimeoutSeconds = Math.Clamp(SqlConnectionTimeoutSeconds, 1, 300);
        _source.SqlDefaultSchema = string.IsNullOrWhiteSpace(SqlDefaultSchema) ? "dbo" : SqlDefaultSchema.Trim();
        _source.SqlDefaultPreviewTopN = Math.Clamp(SqlDefaultPreviewTopN, 1, 10000);

        _source.SaveLogsToFile = SaveLogsToFile;
        _source.LogLevel = string.IsNullOrWhiteSpace(LogLevel) ? "Info" : LogLevel;
        _source.MaxLogFilesCount = Math.Clamp(MaxLogFilesCount, 1, 100);
        _source.MaxLogFileSizeMb = Math.Clamp(MaxLogFileSizeMb, 1, 1024);
        _source.EnableTraceDiagnostics = EnableTraceDiagnostics;
        _source.EnableDeveloperWarnings = EnableDeveloperWarnings;
        _source.ResetLayoutOnNextStart = ResetLayoutOnNextStart;
    }

    private void CopyFromSource()
    {
        InterfaceLanguage = SettingsTextCatalog.NormalizeLanguage(_source.InterfaceLanguage);
        AppThemeMode = NormalizeTheme(_source.AppThemeMode);
        UiDensityMode = NormalizeDensity(_source.UiDensityMode);
        ConfirmNewProjectWithUnsavedChanges = _source.ConfirmNewProjectWithUnsavedChanges;
        EnableRecoveryAutosave = _source.EnableRecoveryAutosave;

        EnableExperimentalLayoutTab = _source.EnableExperimentalLayoutTab;
        ShowPropertyTooltips = _source.ShowPropertyTooltips;
        CompactPropertyInspector = _source.CompactPropertyInspector;
        ShowAdvancedProperties = _source.ShowAdvancedProperties;
        IsDesignerGridVisible = _source.IsDesignerGridVisible;
        IsCanvasSnappingEnabled = _source.IsCanvasSnappingEnabled;
        GridStep = Math.Max(1, _source.SnapStep);

        ShowPreviewRuntimeBadge = _source.ShowPreviewRuntimeBadge;
        CompactPreviewRuntimeBadge = _source.CompactPreviewRuntimeBadge;
        AutoHidePreviewRuntimeBadge = _source.AutoHidePreviewRuntimeBadge;
        UseExportedAxamlPreview = _source.UseExportedAxamlPreview;
        FallbackToLegacyPreviewOnAxamlError = _source.FallbackToLegacyPreviewOnAxamlError;
        ShowGeneratedAxamlOnPreviewError = _source.ShowGeneratedAxamlOnPreviewError;
        CleanAxamlPreviewTemporaryFiles = _source.CleanAxamlPreviewTemporaryFiles;
        PreviewTopmost = _source.PreviewTopmost;
        PreviewDefaultZoomPercent = _source.PreviewDefaultZoomPercent;

        ValidateBuildAfterExport = _source.ValidateBuildAfterExport;
        VerboseBuildLogs = _source.VerboseBuildLogs;
        KeepSuccessfulBuildArtifacts = _source.KeepSuccessfulBuildArtifacts;
        CleanOldArtifactsAutomatically = _source.CleanOldArtifactsAutomatically;
        ExportSqlConnectionString = _source.ExportSqlConnectionString;
        BuildTimeoutSeconds = _source.BuildTimeoutSeconds;

        UseCustomNuGetSource = _source.UseCustomNuGetSource;
        CustomNuGetSource = _source.CustomNuGetSource;
        AllowInsecureNuGetSource = _source.AllowInsecureNuGetSource;
        IncludeNuGetOrgFallback = _source.IncludeNuGetOrgFallback;
        GenerateNuGetConfigInExportedProject = _source.GenerateNuGetConfigInExportedProject;
        NuGetSourceTestStatusText = _source.NuGetSourceTestStatusText;

        UseGlobalSqlServerSettings = _source.UseGlobalSqlServerSettings;
        SqlServerName = _source.SqlServerName;
        SqlDatabaseName = _source.SqlDatabaseName;
        SqlAuthenticationMode = SqlConnectionStringBuilderService.NormalizeAuthMode(_source.SqlAuthenticationMode);
        SqlUserName = _source.SqlUserName;
        SqlPassword = _source.SqlPassword;
        SqlSavePassword = _source.SqlSavePassword;
        SqlTrustServerCertificate = _source.SqlTrustServerCertificate;
        SqlEncryptConnection = _source.SqlEncryptConnection;
        SqlConnectionTimeoutSeconds = _source.SqlConnectionTimeoutSeconds;
        SqlDefaultSchema = _source.SqlDefaultSchema;
        SqlDefaultPreviewTopN = _source.SqlDefaultPreviewTopN;

        SaveLogsToFile = _source.SaveLogsToFile;
        LogLevel = NormalizeLogLevel(_source.LogLevel);
        MaxLogFilesCount = _source.MaxLogFilesCount;
        MaxLogFileSizeMb = _source.MaxLogFileSizeMb;
        LogsFolderPath = _source.LogsFolderPath;
        EnableTraceDiagnostics = _source.EnableTraceDiagnostics;
        EnableDeveloperWarnings = _source.EnableDeveloperWarnings;
        ResetLayoutOnNextStart = _source.ResetLayoutOnNextStart;
    }

    private SqlServerSettingsModel CaptureSqlSettings() =>
        new()
        {
            ServerName = SqlServerName,
            DatabaseName = SqlDatabaseName,
            AuthenticationMode = SqlConnectionStringBuilderService.NormalizeAuthMode(SqlAuthenticationMode),
            UserName = SqlUserName,
            Password = SqlPassword,
            SavePassword = SqlSavePassword,
            TrustServerCertificate = SqlTrustServerCertificate,
            EncryptConnection = SqlEncryptConnection,
            ConnectionTimeoutSeconds = Math.Clamp(SqlConnectionTimeoutSeconds, 1, 300),
            DefaultSchema = string.IsNullOrWhiteSpace(SqlDefaultSchema) ? "dbo" : SqlDefaultSchema.Trim(),
            DefaultPreviewTopN = Math.Clamp(SqlDefaultPreviewTopN, 1, 10000),
            UseGlobalSettingsForSqlSources = UseGlobalSqlServerSettings,
            ExportConnectionStringInGeneratedCode = ExportSqlConnectionString
        };

    private void ApplyGeneral(GeneralSettingsModel settings)
    {
        InterfaceLanguage = SettingsTextCatalog.NormalizeLanguage(settings.Language);
        AppThemeMode = NormalizeTheme(settings.Theme);
        ConfirmNewProjectWithUnsavedChanges = settings.ConfirmNewProjectWithUnsavedChanges;
        EnableRecoveryAutosave = settings.EnableRecoveryAutosave;
    }

    private void ApplyInterface(InterfaceSettingsModel settings)
    {
        ShowPropertyTooltips = settings.ShowPropertyTooltips;
        CompactPropertyInspector = settings.CompactPropertyInspector;
        ShowAdvancedProperties = settings.ShowAdvancedProperties;
        GridStep = settings.GridStep;
    }

    private void ApplyCanvas(CanvasEditorSettingsModel settings)
    {
        IsDesignerGridVisible = settings.IsDesignerGridVisible;
        IsCanvasSnappingEnabled = settings.IsCanvasSnappingEnabled;
    }

    private void ApplyPreview(PreviewSettingsModel settings)
    {
        ShowPreviewRuntimeBadge = settings.ShowRuntimeBadge;
        CompactPreviewRuntimeBadge = settings.CompactRuntimeBadge;
        AutoHidePreviewRuntimeBadge = settings.AutoHideRuntimeBadge;
        UseExportedAxamlPreview = settings.UseExportedAxamlPreview;
        FallbackToLegacyPreviewOnAxamlError = settings.FallbackToLegacyPreviewOnAxamlError;
        ShowGeneratedAxamlOnPreviewError = settings.ShowGeneratedAxamlOnPreviewError;
        CleanAxamlPreviewTemporaryFiles = settings.CleanAxamlPreviewTemporaryFiles;
        PreviewTopmost = settings.PreviewTopmost;
        PreviewDefaultZoomPercent = settings.PreviewDefaultZoomPercent;
        EnableExperimentalLayoutTab = settings.EnableExperimentalLayoutTab;
    }

    private void ApplyBuildAndLogs(BuildAndLogsSettingsModel settings)
    {
        ValidateBuildAfterExport = settings.ValidateBuildAfterExport;
        VerboseBuildLogs = settings.VerboseBuildLogs;
        KeepSuccessfulBuildArtifacts = settings.KeepSuccessfulBuildArtifacts;
        CleanOldArtifactsAutomatically = settings.CleanOldArtifactsAutomatically;
        SaveLogsToFile = settings.SaveLogsToFile;
        UseCustomNuGetSource = settings.UseCustomNuGetSource;
        CustomNuGetSource = settings.CustomNuGetSource;
        AllowInsecureNuGetSource = settings.AllowInsecureNuGetSource;
        IncludeNuGetOrgFallback = settings.IncludeNuGetOrgFallback;
        GenerateNuGetConfigInExportedProject = settings.GenerateNuGetConfigInExportedProject;
        ExportSqlConnectionString = settings.ExportSqlConnectionString;
        BuildTimeoutSeconds = settings.BuildTimeoutSeconds;
        LogLevel = NormalizeLogLevel(settings.LogLevel);
        MaxLogFilesCount = settings.MaxLogFilesCount;
        MaxLogFileSizeMb = settings.MaxLogFileSizeMb;
    }

    private void ApplySql(SqlServerSettingsModel settings)
    {
        UseGlobalSqlServerSettings = settings.UseGlobalSettingsForSqlSources;
        SqlServerName = settings.ServerName;
        SqlDatabaseName = settings.DatabaseName;
        SqlAuthenticationMode = SqlConnectionStringBuilderService.NormalizeAuthMode(settings.AuthenticationMode);
        SqlUserName = settings.UserName;
        SqlPassword = settings.Password;
        SqlSavePassword = settings.SavePassword;
        SqlTrustServerCertificate = settings.TrustServerCertificate;
        SqlEncryptConnection = settings.EncryptConnection;
        SqlConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds;
        SqlDefaultSchema = settings.DefaultSchema;
        SqlDefaultPreviewTopN = settings.DefaultPreviewTopN;
        ExportSqlConnectionString = settings.ExportConnectionStringInGeneratedCode;
    }

    private void ApplyAdvanced(AdvancedSettingsModel settings)
    {
        EnableTraceDiagnostics = settings.EnableTraceDiagnostics;
        EnableDeveloperWarnings = settings.EnableDeveloperWarnings;
        ResetLayoutOnNextStart = settings.ResetLayoutOnNextStart;
    }

    private void RebuildLocalizedContent(string language, bool preserveSelection)
    {
        var selectedId = preserveSelection ? SelectedSection?.Id : "general";
        Texts = SettingsTextCatalog.ForLanguage(language);
        _isRefreshingOptions = true;
        try
        {
            RefillOptions(LanguageOptions, Texts.CreateLanguageOptions());
            RefillOptions(ThemeOptions, Texts.CreateThemeOptions());
            RefillOptions(DensityOptions, Texts.CreateDensityOptions());
            RefillOptions(SqlAuthenticationOptions, Texts.CreateSqlAuthenticationOptions());
            RefillOptions(LogLevelOptions, Texts.CreateLogLevelOptions());

            SelectedLanguageOption = FindOption(LanguageOptions, InterfaceLanguage);
            SelectedThemeOption = FindOption(ThemeOptions, AppThemeMode);
            SelectedDensityOption = FindOption(DensityOptions, UiDensityMode);
            SelectedSqlAuthenticationOption = FindOption(SqlAuthenticationOptions, SqlAuthenticationMode);
            SelectedLogLevelOption = FindOption(LogLevelOptions, LogLevel);

            Sections.Clear();
            Sections.Add(new SettingsSectionModel("general", Texts.GeneralTitle, Texts.GeneralSubtitle, "GN"));
            Sections.Add(new SettingsSectionModel("interface", Texts.InterfaceTitle, Texts.InterfaceSubtitle, "UI"));
            Sections.Add(new SettingsSectionModel("preview", Texts.PreviewTitle, Texts.PreviewSubtitle, "PV"));
            Sections.Add(new SettingsSectionModel("export", Texts.ExportTitle, Texts.ExportSubtitle, "EX"));
            Sections.Add(new SettingsSectionModel("nuget", Texts.NuGetTitle, Texts.NuGetSubtitle, "NG"));
            Sections.Add(new SettingsSectionModel("sql", Texts.SqlTitle, Texts.SqlSubtitle, "SQL"));
            Sections.Add(new SettingsSectionModel("logs", Texts.LogsTitle, Texts.LogsSubtitle, "LG"));
            Sections.Add(new SettingsSectionModel("advanced", Texts.AdvancedTitle, Texts.AdvancedSubtitle, "AD"));
            SelectedSection = Sections.FirstOrDefault(section => string.Equals(section.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? Sections.FirstOrDefault();
        }
        finally
        {
            _isRefreshingOptions = false;
        }

        RaiseLocalizedComputedProperties();
    }

    private void SettingsWindowViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_trackDirty || _isRefreshingOptions)
            return;

        if (e.PropertyName is nameof(IsDirty) or nameof(StatusText) or nameof(Texts) or nameof(SelectedSection)
            or nameof(SelectedLanguageOption) or nameof(SelectedThemeOption) or nameof(SelectedDensityOption)
            or nameof(SelectedSqlAuthenticationOption) or nameof(SelectedLogLevelOption)
            or nameof(IsGeneralSelected) or nameof(IsInterfaceSelected) or nameof(IsPreviewSelected)
            or nameof(IsExportSelected) or nameof(IsNuGetSelected) or nameof(IsSqlSelected)
            or nameof(IsLogsSelected) or nameof(IsAdvancedSelected)
            or nameof(EffectiveNuGetSourceText) or nameof(NuGetSummaryText)
            or nameof(SqlConnectionPreviewText) or nameof(SqlStatusText) or nameof(IsSqlConfigured)
            or nameof(IsSqlLoginSelected)
            or nameof(UnsavedStatusText) or nameof(UnsavedStatusBackground)
            or nameof(UnsavedStatusBorder) or nameof(UnsavedStatusForeground)
            or nameof(SectionTitle) or nameof(SectionSubtitle))
            return;

        IsDirty = true;
    }

    private int CountChangedKeys()
    {
        var count = 0;
        void Count(bool changed)
        {
            if (changed)
                count++;
        }

        Count(!string.Equals(_source.InterfaceLanguage, InterfaceLanguage, StringComparison.Ordinal));
        Count(!string.Equals(_source.AppThemeMode, AppThemeMode, StringComparison.Ordinal));
        Count(!string.Equals(_source.UiDensityMode, UiDensityMode, StringComparison.Ordinal));
        Count(_source.ConfirmNewProjectWithUnsavedChanges != ConfirmNewProjectWithUnsavedChanges);
        Count(_source.EnableRecoveryAutosave != EnableRecoveryAutosave);
        Count(_source.EnableExperimentalLayoutTab != EnableExperimentalLayoutTab);
        Count(_source.ShowPropertyTooltips != ShowPropertyTooltips);
        Count(_source.CompactPropertyInspector != CompactPropertyInspector);
        Count(_source.ShowAdvancedProperties != ShowAdvancedProperties);
        Count(_source.IsDesignerGridVisible != IsDesignerGridVisible);
        Count(_source.IsCanvasSnappingEnabled != IsCanvasSnappingEnabled);
        Count(_source.SnapStep != GridStep);
        Count(_source.ShowPreviewRuntimeBadge != ShowPreviewRuntimeBadge);
        Count(_source.CompactPreviewRuntimeBadge != CompactPreviewRuntimeBadge);
        Count(_source.AutoHidePreviewRuntimeBadge != AutoHidePreviewRuntimeBadge);
        Count(_source.UseExportedAxamlPreview != UseExportedAxamlPreview);
        Count(_source.FallbackToLegacyPreviewOnAxamlError != FallbackToLegacyPreviewOnAxamlError);
        Count(_source.ShowGeneratedAxamlOnPreviewError != ShowGeneratedAxamlOnPreviewError);
        Count(_source.CleanAxamlPreviewTemporaryFiles != CleanAxamlPreviewTemporaryFiles);
        Count(_source.PreviewTopmost != PreviewTopmost);
        Count(_source.PreviewDefaultZoomPercent != PreviewDefaultZoomPercent);
        Count(_source.ValidateBuildAfterExport != ValidateBuildAfterExport);
        Count(_source.VerboseBuildLogs != VerboseBuildLogs);
        Count(_source.KeepSuccessfulBuildArtifacts != KeepSuccessfulBuildArtifacts);
        Count(_source.CleanOldArtifactsAutomatically != CleanOldArtifactsAutomatically);
        Count(_source.ExportSqlConnectionString != ExportSqlConnectionString);
        Count(_source.UseCustomNuGetSource != UseCustomNuGetSource);
        Count(!string.Equals(_source.CustomNuGetSource, CustomNuGetSource, StringComparison.Ordinal));
        Count(_source.AllowInsecureNuGetSource != AllowInsecureNuGetSource);
        Count(_source.IncludeNuGetOrgFallback != IncludeNuGetOrgFallback);
        Count(_source.GenerateNuGetConfigInExportedProject != GenerateNuGetConfigInExportedProject);
        Count(_source.UseGlobalSqlServerSettings != UseGlobalSqlServerSettings);
        Count(!string.Equals(_source.SqlServerName, SqlServerName, StringComparison.Ordinal));
        Count(!string.Equals(_source.SqlDatabaseName, SqlDatabaseName, StringComparison.Ordinal));
        Count(!string.Equals(_source.SqlAuthenticationMode, SqlAuthenticationMode, StringComparison.Ordinal));
        Count(!string.Equals(_source.SqlUserName, SqlUserName, StringComparison.Ordinal));
        Count(!string.Equals(_source.SqlPassword, SqlPassword, StringComparison.Ordinal));
        Count(_source.SqlSavePassword != SqlSavePassword);
        Count(_source.SqlTrustServerCertificate != SqlTrustServerCertificate);
        Count(_source.SqlEncryptConnection != SqlEncryptConnection);
        Count(_source.SqlDefaultPreviewTopN != SqlDefaultPreviewTopN);
        Count(_source.SaveLogsToFile != SaveLogsToFile);
        Count(!string.Equals(_source.LogLevel, LogLevel, StringComparison.Ordinal));
        return count;
    }

    private bool IsSelected(string id) =>
        string.Equals(SelectedSection?.Id, id, StringComparison.OrdinalIgnoreCase);

    private void RaiseSqlComputedProperties()
    {
        OnPropertyChanged(nameof(IsSqlConfigured));
        OnPropertyChanged(nameof(SqlStatusText));
        OnPropertyChanged(nameof(SqlConnectionPreviewText));
    }

    private void RaiseNuGetComputedProperties()
    {
        OnPropertyChanged(nameof(EffectiveNuGetSourceText));
        OnPropertyChanged(nameof(NuGetSummaryText));
        Debug.WriteLine($"NUGET_SETTINGS_CHANGED custom={UseCustomNuGetSource}; sourceKind={ExportPipelineService.GetNuGetSourceKind(EffectiveNuGetSourceText)}");
    }

    private void RaiseLocalizedComputedProperties()
    {
        OnPropertyChanged(nameof(UnsavedStatusText));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionSubtitle));
        OnPropertyChanged(nameof(NuGetSummaryText));
        OnPropertyChanged(nameof(SqlStatusText));
    }

    private static void RefillOptions(ObservableCollection<SettingsOptionModel> target, IReadOnlyList<SettingsOptionModel> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private static SettingsOptionModel? FindOption(IEnumerable<SettingsOptionModel> values, string value) =>
        values.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeTheme(string? value)
    {
        if (string.Equals(value, SettingsTextCatalog.ThemeDark, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Тёмная", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Темная", StringComparison.OrdinalIgnoreCase))
            return SettingsTextCatalog.ThemeDark;

        if (string.Equals(value, SettingsTextCatalog.ThemeSystem, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Системная", StringComparison.OrdinalIgnoreCase))
            return SettingsTextCatalog.ThemeSystem;

        return SettingsTextCatalog.ThemeLight;
    }

    private static string NormalizeDensity(string? value)
    {
        return value switch
        {
            MainWindowViewModel.UiDensityComfortable => MainWindowViewModel.UiDensityComfortable,
            MainWindowViewModel.UiDensityDense => MainWindowViewModel.UiDensityDense,
            _ => MainWindowViewModel.UiDensityCompact
        };
    }

    private static string NormalizeLogLevel(string? value)
    {
        return value switch
        {
            "Error" => "Error",
            "Warning" => "Warning",
            "Debug" => "Debug",
            "Trace" => "Trace",
            _ => "Info"
        };
    }
}
