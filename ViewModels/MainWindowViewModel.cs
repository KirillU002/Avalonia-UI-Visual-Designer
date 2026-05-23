using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.EditorCommands;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.Services;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FormDesigner.ViewModels;

/// <summary>
/// Центральная модель состояния конструктора форм.
/// Хранит документ, выделение, историю изменений, источники данных
/// и результат генерации итогового XAML/C#.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public const string LayoutExportModeCanvas = "Canvas layout";
    public const string LayoutExportModeResponsive = "Responsive layout (experimental)";

    public const string GenerationModeCleanUi = "Чистый UI";
    public const string GenerationModeDemoData = "С демонстрационными данными";
    public const string DataGridExportModePlaceholder = "Placeholder без NuGet";
    public const string DataGridExportModeVisual = "Visual table без NuGet";
    public const string DataGridExportModePortable = DataGridExportModeVisual;
    public const string DataGridExportModeReal = "Real Avalonia DataGrid (нужен NuGet)";
    private const string LegacyDataGridExportModePortable = "Безопасная таблица без NuGet";
    public const string ExportTargetMainWindow = "Замена MainWindow";
    public const string ExportTargetGeneratedWindow = "Отдельное окно Form1Window";
    public const string XamlVerbosityCompact = "Компактный";
    public const string XamlVerbosityFullStyled = "Полный со стилями";
    public const string WorkspaceModeDesign = "Дизайн";
    public const string WorkspaceModeData = "Данные";
    public const string WorkspaceModeCode = "Код";
    public const string WorkspaceModePlugins = "Плагины";
    public const string WorkspaceModeLogic = "Логика";
    public const string WorkspaceModeDiagnostics = "Диагностика";
    public const string WorkspaceModeHistory = "История";
    public const string ProblemsFilterAll = "All";
    public const string ProblemsFilterErrors = "Errors";
    public const string ProblemsFilterWarnings = "Warnings";
    public const string ProblemsFilterHints = "Hints";
    public const string PropertyGridCategoryFavorites = "Favorites";
    public const string PropertyGridCategoryCommon = "Common";
    public const string PropertyGridCategoryLayout = "Layout";
    public const string PropertyGridCategoryAppearance = "Appearance";
    public const string PropertyGridCategoryData = "Data";
    public const string PropertyGridCategoryBehavior = "Behavior";
    public const string PropertyGridCategoryInteraction = "Interaction";
    public const string PropertyGridCategoryExport = "Export";
    public const string PropertyGridCategoryAdvanced = "Advanced";

    public const string WindowStateNormal = "Обычное";
    public const string WindowStateMaximized = "Рабочая область";
    public const string WindowStateFullScreen = "Полный экран";
    public const string StartupLocationManual = "Вручную";
    public const string StartupLocationCenterScreen = "По центру экрана";
    public const string StartupLocationCenterOwner = "По центру владельца";

    public event Action<EditorCommandId>? ExternalEditorCommandRequested;

    private static readonly TimeSpan HistoryGroupingWindow = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> UnsafeGeneratedIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while",
        "Application", "AvaloniaObject", "Binding", "Border", "Button", "Canvas", "CheckBox",
        "Control", "DataContext", "DataGrid", "DataGridTextColumn", "DataTemplate", "Grid",
        "Image", "InitializeComponent", "Panel", "StackPanel", "Style", "TextBlock", "TextBox",
        "UniformGrid", "UserControl", "Window", "WrapPanel"
    };

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private readonly IDesignerRegistry _registry;
    private readonly DocumentDiagnosticsService _diagnosticsService;
    private readonly ReusableTemplateStorageService _templateStorageService = new();
    private readonly List<DocumentDiagnosticModel> _previewRuntimeDiagnostics = new();
    private IXamlWriter? _activeXamlWriter;
    private XamlExportContext? _activeXamlExportContext;
    private Dictionary<string, IDesignControlNode>? _activeXamlControlNodes;
    private IReadOnlyDictionary<string, string>? _activeXamlControlNameMap;
    private LayoutExportPlan? _activeLayoutExportPlan;

    private bool _isHistorySuspended;
    private int _undoBatchDepth;
    private bool _undoBatchTrackHistory;
    private bool _isUpdatingSelectionState;
    private string _currentSnapshot = "";
    private string _savedSnapshot = "";
    private string _exportCacheDocumentSnapshotHash = "";
    private string _exportCacheSettingsSignature = "";
    private DateTime _exportCacheGeneratedUtc;
    private DateTime _lastHistoryMutationUtc = DateTime.UtcNow;
    private DesignerDocumentFileModel? _clipboardDocument;
    private ControlStyleSnapshot? _styleClipboard;
    private string _activeFormTheme = DesignerThemeCatalog.Light;
    private bool _isApplyingThemePalette;
    private bool _isApplyingDocument;
    private bool _isUpdatingStructureSelection;
    private bool _isStructureTreeRefreshSuspended;
    private bool _isRebuildingPropertyGrid;
    private double _previewScreenWidth = 1920;
    private double _previewScreenHeight = 1080;
    private double _previewWorkingAreaWidth = 1920;
    private double _previewWorkingAreaHeight = 1040;
    private string _previewScreenName = "Текущий монитор";
    private int _templateInsertionOffset;
    private readonly EditorCommandService _editorCommandService = new();
    private readonly PropertyGridUserSettings _propertyGridUserSettings = new();
    private readonly DispatcherTimer _propertyGridLiveRefreshTimer;
    private static readonly TimeSpan PropertyGridLiveRefreshInterval = TimeSpan.FromMilliseconds(66);
    private DateTime _lastPropertyGridLiveRefreshUtc = DateTime.MinValue;
    private bool _isPropertyGridLiveGesture;
    private bool _hasPendingPropertyGridLiveRefresh;
    private int _propertyGridSettingsVersion;

    // Toolbox теперь строится из registry дескрипторов, а не из зашитого списка.
    public ObservableCollection<ToolboxItem> ToolboxItems { get; } = new();
    public ObservableCollection<ToolboxItem> PluginToolboxItems { get; } = new();
    public ObservableCollection<EditorCommand> EditorCommands => _editorCommandService.Commands;
    public ObservableCollection<EditorCommand> CommandPaletteCommands { get; } = new();
    public ObservableCollection<DescriptorPropertyEditorViewModel> DescriptorCustomPropertyEditors { get; } = new();
    public ObservableCollection<ImportedDllInfoModel> ImportedDllCatalog { get; } = new();
    public ObservableCollection<ImportedDllInfoModel> FilteredImportedDllCatalog { get; } = new();
    public ObservableCollection<InstalledPluginInfoModel> InstalledPlugins { get; } = new();
    public ObservableCollection<DocumentDiagnosticModel> Diagnostics { get; } = new();
    public ObservableCollection<StructureTreeItemModel> StructureTreeItems { get; } = new();
    public ObservableCollection<PropertyGridCategoryViewModel> PropertyGridCategories { get; } = new();
    public ObservableCollection<UndoRedoHistoryItemModel> UndoRedoHistoryItems { get; } = new();
    public ObservableCollection<ReusableTemplateModel> ReusableTemplates { get; } = new();
    public ObservableCollection<RecentFileModel> RecentFiles { get; } = new();

    // Плоский список всех контролов документа. Иерархия восстанавливается через ParentId.
    public ObservableCollection<DesignControlModel> Controls { get; } = new();

    // Источники данных для DataGrid и генерации CRUD-логики.
    public ObservableCollection<BindingSourceModel> BindingSources { get; } = new();

    // Interaction layer: simple form logic without hand-written code.
    public ObservableCollection<InteractionModel> Interactions { get; } = new();

    // Отдельно храним Id выделения, чтобы оно переживало сериализацию и undo/redo.
    public ObservableCollection<string> SelectedControlIds { get; } = new();

    public ObservableCollection<string> AvailableFontFamilies { get; } = new()
    {
        "Inter",
        "Segoe UI",
        "Arial",
        "Verdana",
        "Tahoma",
        "Times New Roman",
        "Consolas"
    };

    public ObservableCollection<string> AvailableFontWeights { get; } = new()
    {
        "Thin",
        "Light",
        "Normal",
        "Medium",
        "SemiBold",
        "Bold",
        "Black"
    };

    public ObservableCollection<string> AvailableStretchModes { get; } = new()
    {
        "None",
        "Uniform",
        "UniformToFill",
        "Fill"
    };

    public ObservableCollection<string> AvailableLayoutModes { get; } = new()
    {
        DesignerLayoutModes.Absolute,
        DesignerLayoutModes.Stack,
        DesignerLayoutModes.Grid,
        DesignerLayoutModes.Flex
    };

    public ObservableCollection<string> AvailableLayoutOrientations { get; } = new()
    {
        DesignerLayoutModes.Vertical,
        DesignerLayoutModes.Horizontal
    };

    public ObservableCollection<string> AvailableDataGridTextAlignments { get; } = new()
    {
        DesignControlModel.DataGridTextAlignmentLeft,
        DesignControlModel.DataGridTextAlignmentCenter,
        DesignControlModel.DataGridTextAlignmentRight
    };

    public ObservableCollection<string> AvailableDataGridFilterModes { get; } = new()
    {
        DesignControlModel.DataGridFilterModeContains,
        DesignControlModel.DataGridFilterModeStartsWith,
        DesignControlModel.DataGridFilterModeEquals
    };

    public ObservableCollection<string> AvailableColumnAlignments { get; } = new()
    {
        BindingFieldModel.AlignmentLeft,
        BindingFieldModel.AlignmentCenter,
        BindingFieldModel.AlignmentRight
    };

    public ObservableCollection<string> AvailableColumnTextTrimmings { get; } = new()
    {
        BindingFieldModel.TextTrimmingNone,
        BindingFieldModel.TextTrimmingCharacterEllipsis,
        BindingFieldModel.TextTrimmingWordEllipsis
    };

    public ObservableCollection<string> AvailableColumnTextWrappings { get; } = new()
    {
        BindingFieldModel.TextWrappingNoWrap,
        BindingFieldModel.TextWrappingWrap
    };

    public ObservableCollection<string> AvailableFieldSortDirections { get; } = new()
    {
        BindingFieldModel.SortDirectionNone,
        BindingFieldModel.SortDirectionAscending,
        BindingFieldModel.SortDirectionDescending
    };

    public ObservableCollection<string> AvailableFieldSummaryTypes { get; } = new()
    {
        BindingFieldModel.SummaryTypeNone,
        BindingFieldModel.SummaryTypeCount,
        BindingFieldModel.SummaryTypeSum,
        BindingFieldModel.SummaryTypeAvg,
        BindingFieldModel.SummaryTypeMin,
        BindingFieldModel.SummaryTypeMax
    };

    public ObservableCollection<string> AvailableFormWindowStates { get; } = new()
    {
        WindowStateNormal,
        WindowStateMaximized,
        WindowStateFullScreen
    };

    public ObservableCollection<string> AvailableFormStartupLocations { get; } = new()
    {
        StartupLocationManual,
        StartupLocationCenterScreen,
        StartupLocationCenterOwner
    };

    public ObservableCollection<string> AvailableFormThemes { get; } = new(DesignerThemeCatalog.AvailableThemes);

    public ObservableCollection<string> AvailableGenerationModes { get; } = new()
    {
        GenerationModeCleanUi,
        GenerationModeDemoData
    };

    public ObservableCollection<string> AvailableDataGridExportModes { get; } = new()
    {
        DataGridExportModePlaceholder,
        DataGridExportModeVisual,
        DataGridExportModeReal
    };

    public ObservableCollection<string> AvailableExportTargets { get; } = new()
    {
        ExportTargetMainWindow,
        ExportTargetGeneratedWindow
    };

    public ObservableCollection<string> AvailableXamlVerbosities { get; } = new()
    {
        XamlVerbosityCompact,
        XamlVerbosityFullStyled
    };

    public ObservableCollection<string> AvailableLayoutExportModes { get; } = new()
    {
        LayoutExportModeCanvas,
        LayoutExportModeResponsive
    };

    public ObservableCollection<string> AvailableInteractionEvents { get; } = new()
    {
        InteractionModel.EventButtonClick,
        InteractionModel.EventTextBoxTextChanged,
        InteractionModel.EventCheckBoxChecked,
        InteractionModel.EventCheckBoxUnchecked,
        InteractionModel.EventDataGridSelectionChanged
    };

    public ObservableCollection<string> AvailableInteractionActions { get; } = new()
    {
        InteractionModel.ActionSetProperty,
        InteractionModel.ActionClearProperty,
        InteractionModel.ActionToggleVisibility,
        InteractionModel.ActionEnableDisable,
        InteractionModel.ActionShowMessage
    };

    public ObservableCollection<string> AvailableInteractionTargetProperties { get; } = new()
    {
        InteractionModel.TargetPropertyText,
        InteractionModel.TargetPropertyContent,
        InteractionModel.TargetPropertyIsChecked,
        InteractionModel.TargetPropertyIsVisible,
        InteractionModel.TargetPropertyIsEnabled
    };

    public ObservableCollection<InteractionOptionModel> AvailableInteractionEventOptions { get; } = new()
    {
        new(InteractionModel.EventButtonClick, "Кнопка: клик", "Срабатывает, когда пользователь нажал кнопку."),
        new(InteractionModel.EventTextBoxTextChanged, "Текстовое поле: текст изменён", "Срабатывает при изменении текста в TextBox."),
        new(InteractionModel.EventCheckBoxChecked, "Флажок: включён", "Срабатывает, когда CheckBox переключили во включенное состояние."),
        new(InteractionModel.EventCheckBoxUnchecked, "Флажок: выключен", "Срабатывает, когда CheckBox сняли."),
        new(InteractionModel.EventDataGridSelectionChanged, "Таблица: выбрана строка", "Срабатывает, когда пользователь выбрал строку DataGrid.")
    };

    public ObservableCollection<InteractionOptionModel> AvailableInteractionActionOptions { get; } = new()
    {
        new(InteractionModel.ActionSetProperty, "Записать значение", "Записывает поле или шаблон в выбранное свойство цели."),
        new(InteractionModel.ActionClearProperty, "Очистить значение", "Очищает текст, содержимое или сбрасывает состояние цели."),
        new(InteractionModel.ActionToggleVisibility, "Показать / скрыть", "Переключает видимость выбранного элемента."),
        new(InteractionModel.ActionEnableDisable, "Включить / отключить", "Меняет доступность элемента по значению true/false, 1/0, да/нет."),
        new(InteractionModel.ActionShowMessage, "Показать сообщение", "Показывает текст в preview/status; удобно для проверки сценария.")
    };

    public ObservableCollection<InteractionOptionModel> AvailableInteractionTargetPropertyOptions { get; } = new()
    {
        new(InteractionModel.TargetPropertyText, "Текст", "TextBox.Text или TextBlock.Text."),
        new(InteractionModel.TargetPropertyContent, "Содержимое", "Button.Content или содержимое похожего элемента."),
        new(InteractionModel.TargetPropertyIsChecked, "Отмечено", "CheckBox.IsChecked: true/false."),
        new(InteractionModel.TargetPropertyIsVisible, "Видимость", "Показывает или скрывает элемент."),
        new(InteractionModel.TargetPropertyIsEnabled, "Доступность", "Включает или отключает элемент.")
    };

    public ObservableCollection<string> AvailableWorkspaceModes { get; } = new()
    {
        WorkspaceModeDesign,
        WorkspaceModeData,
        WorkspaceModeCode,
        WorkspaceModePlugins,
        WorkspaceModeLogic,
        WorkspaceModeHistory
    };

    public ObservableCollection<string> AvailableProblemsFilters { get; } = new()
    {
        ProblemsFilterAll,
        ProblemsFilterErrors,
        ProblemsFilterWarnings,
        ProblemsFilterHints
    };

    [ObservableProperty]
    private DesignControlModel? selectedControl;

    [ObservableProperty]
    private bool isCommandPaletteOpen;

    [ObservableProperty]
    private string commandPaletteSearchText = "";

    [ObservableProperty]
    private EditorCommand? selectedCommandPaletteCommand;

    private StructureTreeItemModel? selectedStructureItem;
    public StructureTreeItemModel? SelectedStructureItem
    {
        get => selectedStructureItem;
        set
        {
            if (!SetProperty(ref selectedStructureItem, value) || _isUpdatingStructureSelection)
                return;

            if (value?.Control is DesignControlModel control)
            {
                SelectSingleControl(control);
                return;
            }

            ClearSelection();
        }
    }

    [ObservableProperty]
    private BindingSourceModel? selectedBindingSource;

    [ObservableProperty]
    private InteractionModel? selectedInteraction;

    [ObservableProperty]
    private string generatedXaml = "";

    [ObservableProperty]
    private string generatedCSharp = "";

    [ObservableProperty]
    private string generatedBindingGuide = "";

    [ObservableProperty]
    private string generationMode = GenerationModeCleanUi;

    [ObservableProperty]
    private string dataGridExportMode = DataGridExportModePortable;

    [ObservableProperty]
    private string exportTarget = ExportTargetMainWindow;

    [ObservableProperty]
    private string exportProjectNamespace = "AvaloniaApplication1";

    [ObservableProperty]
    private string xamlVerbosity = XamlVerbosityCompact;

    [ObservableProperty]
    private string layoutExportMode = LayoutExportModeCanvas;

    [ObservableProperty]
    private bool includeExportComments;

    [ObservableProperty]
    private string workspaceMode = WorkspaceModeDesign;

    [ObservableProperty]
    private bool includeSampleData;

    [ObservableProperty]
    private bool includeCrudSkeleton;

    [ObservableProperty]
    private bool includeCommunityToolkitAttributes;

    [ObservableProperty]
    private bool includePluginRuntimeReferences;

    [ObservableProperty]
    private double designWidth = 1200;

    [ObservableProperty]
    private double designHeight = 800;

    [ObservableProperty]
    private int snapStep = 10;

    [ObservableProperty]
    private bool isGridSnapEnabled = true;

    [ObservableProperty]
    private bool isControlSnapEnabled = true;

    [ObservableProperty]
    private int snapThreshold = 6;

    [ObservableProperty]
    private string surfaceBackground = "#FFFFFF";

    [ObservableProperty]
    private string surfaceGridMinorColor = "#DCE4EE";

    [ObservableProperty]
    private string surfaceGridMajorColor = "#B7C7DA";

    [ObservableProperty]
    private string surfaceLayoutMode = DesignerLayoutModes.Absolute;

    [ObservableProperty]
    private string surfaceLayoutOrientation = DesignerLayoutModes.Vertical;

    [ObservableProperty]
    private double surfaceLayoutSpacing = 12;

    [ObservableProperty]
    private int surfaceLayoutColumns = 3;

    [ObservableProperty]
    private int surfaceLayoutRows = 3;

    [ObservableProperty]
    private string currentDocumentPath = "";

    [ObservableProperty]
    private string documentSessionId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string statusText = "Готово";

    [ObservableProperty]
    private string autosaveStatusText = "Черновик ещё не создавался.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string busyTitle = "Загрузка";

    [ObservableProperty]
    private string busyDescription = "Подождите, выполняется операция.";

    [ObservableProperty]
    private string formTitle = "Form1";

    [ObservableProperty]
    private string formTheme = DesignerThemeCatalog.Light;

    [ObservableProperty]
    private string formWindowState = WindowStateNormal;

    [ObservableProperty]
    private string formStartupLocation = StartupLocationCenterScreen;

    [ObservableProperty]
    private bool formCanResize = true;

    [ObservableProperty]
    private bool formShowInTaskbar = true;

    [ObservableProperty]
    private bool formTopmost;

    [ObservableProperty]
    private bool formHasSystemDecorations = true;

    [ObservableProperty]
    private bool isImmersiveDesignerMode;

    [ObservableProperty]
    private bool isUserPreviewMode;

    [ObservableProperty]
    private bool isDiagnosticsPaneExpanded = true;

    [ObservableProperty]
    private double diagnosticsPaneHeight = 250;

    [ObservableProperty]
    private bool isLeftDockOpen = true;

    [ObservableProperty]
    private bool isRightDockOpen = true;

    [ObservableProperty]
    private bool isBottomDockOpen;

    [ObservableProperty]
    private double leftDockPanelWidth = 280;

    [ObservableProperty]
    private double rightDockPanelWidth = 380;

    [ObservableProperty]
    private string selectedProblemsFilter = ProblemsFilterAll;

    [ObservableProperty]
    private string structureSearchText = "";

    [ObservableProperty]
    private string propertyGridSearchText = "";

    [ObservableProperty]
    private string importedDllSearchText = "";

    public event EventHandler? DesignerChanged;
    public IDesignerRegistry Registry => _registry;
    public bool IsCleanUiGenerationMode => string.Equals(GenerationMode, GenerationModeCleanUi, StringComparison.Ordinal);
    public bool IsDemoDataGenerationMode => !IsCleanUiGenerationMode;
    public bool ShouldGenerateDemoRuntimeCode => IsDemoDataGenerationMode || IncludeSampleData || IncludeCrudSkeleton;
    public bool ShouldExportPlaceholderDataGrid => string.Equals(NormalizeDataGridExportMode(DataGridExportMode), DataGridExportModePlaceholder, StringComparison.Ordinal);
    public bool ShouldExportVisualDataGrid => string.Equals(NormalizeDataGridExportMode(DataGridExportMode), DataGridExportModeVisual, StringComparison.Ordinal);
    public bool ShouldExportRealDataGrid => string.Equals(NormalizeDataGridExportMode(DataGridExportMode), DataGridExportModeReal, StringComparison.Ordinal);
    public bool ShouldExportPortableDataGrid => !ShouldExportRealDataGrid;
    public string DataGridExportModeHint => NormalizeDataGridExportMode(DataGridExportMode) switch
    {
        DataGridExportModePlaceholder => "Компактная заглушка Border/TextBlock. Не рабочая таблица, зато без NuGet и без fake-колонок.",
        DataGridExportModeReal => "Настоящий Avalonia DataGrid. Работает как таблица, но в новом проекте нужен NuGet Avalonia.Controls.DataGrid.",
        _ => "Визуальный макет таблицы без NuGet. Используется только при реальных полях BindingSource; без полей автоматически экспортируется placeholder."
    };
    public bool IsMainWindowExportTarget => string.Equals(ExportTarget, ExportTargetMainWindow, StringComparison.Ordinal);
    public bool IsGeneratedWindowExportTarget => !IsMainWindowExportTarget;
    public bool IsCompactXamlExport => string.Equals(XamlVerbosity, XamlVerbosityCompact, StringComparison.Ordinal);
    public bool IsFullStyledXamlExport => !IsCompactXamlExport;
    public bool ShouldIncludeExportComments => IncludeExportComments || IsFullStyledXamlExport;
    public bool IsResponsiveLayoutExportMode => string.Equals(NormalizeLayoutExportMode(LayoutExportMode), LayoutExportModeResponsive, StringComparison.Ordinal);
    public string LayoutExportModeHint => BuildLayoutExportPlan().Details;
    public string ExportLayoutBadgeText => BuildLayoutExportPlan().BadgeText;
    public bool IsDesignMode => string.Equals(WorkspaceMode, WorkspaceModeDesign, StringComparison.Ordinal);
    public bool IsDataMode => string.Equals(WorkspaceMode, WorkspaceModeData, StringComparison.Ordinal);
    public bool IsCodeMode => string.Equals(WorkspaceMode, WorkspaceModeCode, StringComparison.Ordinal);
    public bool IsPluginsMode => string.Equals(WorkspaceMode, WorkspaceModePlugins, StringComparison.Ordinal);
    public bool IsLogicMode => string.Equals(WorkspaceMode, WorkspaceModeLogic, StringComparison.Ordinal);
    public bool IsDiagnosticsMode => string.Equals(WorkspaceMode, WorkspaceModeDiagnostics, StringComparison.Ordinal);
    public bool IsHistoryMode => string.Equals(WorkspaceMode, WorkspaceModeHistory, StringComparison.Ordinal);
    public bool CanShowLeftDock => IsDesignerSidePanelsVisible && (IsDesignMode || IsDataMode || IsHistoryMode);
    public bool CanShowRightDock => IsDesignerSidePanelsVisible && !IsHistoryMode;
    public bool IsLeftDockPanelVisible => CanShowLeftDock && IsLeftDockOpen;
    public bool IsRightDockPanelVisible => CanShowRightDock && IsRightDockOpen;
    public bool IsLeftDockRailVisible => CanShowLeftDock && !IsLeftDockOpen;
    public bool IsRightDockRailVisible => CanShowRightDock && !IsRightDockOpen;
    public bool IsBottomDockPanelVisible => IsDesignerSidePanelsVisible && IsBottomDockOpen;
    public bool IsBottomDockRailVisible => IsDesignerSidePanelsVisible && !IsBottomDockOpen;
    public bool IsLeftRailVisible => IsLeftDockPanelVisible;
    public bool IsRightInspectorVisible => IsRightDockPanelVisible;
    public bool IsDesignModePanelVisible => IsDesignMode && IsDesignerSidePanelsVisible;
    public bool IsDataModePanelVisible => IsDataMode && IsDesignerSidePanelsVisible;
    public bool IsCodeModePanelVisible => IsCodeMode && IsDesignerSidePanelsVisible;
    public bool IsPluginsModePanelVisible => IsPluginsMode && IsDesignerSidePanelsVisible;
    public bool IsLogicModePanelVisible => IsLogicMode && IsDesignerSidePanelsVisible;
    public bool IsContextualToolbarVisible => HasSelectedControl && IsDesignMode && !IsUserPreviewMode;
    public bool IsCompactDiagnosticsBarVisible => IsBottomDockRailVisible;
    public string LeftDockToggleText => IsLeftDockOpen ? "Скрыть левую" : "Показать левую";
    public string RightDockToggleText => IsRightDockOpen ? "Скрыть правую" : "Показать правую";
    public string LeftDockHeaderTitle => IsDataMode ? "Данные" : IsHistoryMode ? "История" : "Компоненты";
    public string RightDockHeaderTitle => IsDataMode ? "Данные" : IsPluginsMode ? "Плагины" : IsCodeMode ? "Экспорт" : IsLogicMode ? "Логика" : "Свойства";
    public string ProblemsDockButtonText => HasDiagnostics
        ? $"Problems {Diagnostics.Count}"
        : "Problems";
    public string ProblemsPanelTitle => HasDiagnostics ? $"Problems ({Diagnostics.Count})" : "Problems";
    public string ProblemsRailSummary => HasDiagnostics
        ? $"Errors {DiagnosticErrorCount} · Warnings {DiagnosticWarningCount} · Hints {DiagnosticInfoCount}"
        : "No problems";
    public string EditorShellLayoutSummary =>
        $"Левая {LeftDockPanelWidth:0}px · Правая {RightDockPanelWidth:0}px · Problems {DiagnosticsPaneHeight:0}px";
    public int LeftRailSelectedIndex => IsDataMode ? 4 : IsHistoryMode ? 3 : 0;
    public int RightInspectorSelectedIndex => IsDataMode ? 1 : IsPluginsMode ? 2 : IsCodeMode ? 3 : IsLogicMode ? 5 : 0;
    public string WorkspaceModeDescription => WorkspaceMode switch
    {
        WorkspaceModeData => "Данные: источники, BindingSource, SQL/DLL и привязки.",
        WorkspaceModeCode => "Код: экспорт XAML/C# и список нужных NuGet.",
        WorkspaceModePlugins => "Плагины: установка расширений и plugin-компоненты.",
        WorkspaceModeLogic => "Логика: события элементов → действия без ручного кода.",
        WorkspaceModeDiagnostics => "Диагностика: ошибки, предупреждения и переход к проблемам.",
        WorkspaceModeHistory => "История: видимый Undo/Redo и откат к шагу.",
        _ => "Дизайн: canvas, компоненты, структура и свойства."
    };
    public string GenerationOptionsSummary => IsCleanUiGenerationMode
        ? $"Чистый UI: BindingSource используется только как схема колонок. Demo-код, тестовые модели, fake data и CRUD не генерируются. Цель: {ExportTarget}, XAML: {XamlVerbosity}, DataGrid: {DataGridExportMode}."
        : $"С демонстрационными данными: будет сгенерирован sample ViewModel, коллекции, фильтры и CRUD-заготовки. Цель: {ExportTarget}, XAML: {XamlVerbosity}, DataGrid: {DataGridExportMode}.";
    public bool HasExportWarnings => HasExportChecklistIssues;
    public IReadOnlyList<ExportChecklistItem> ExportChecklistItems => BuildExportChecklist();
    public int ExportChecklistErrorCount => ExportChecklistItems.Count(item => item.Severity == ExportChecklistSeverity.Error);
    public int ExportChecklistWarningCount => ExportChecklistItems.Count(item => item.Severity == ExportChecklistSeverity.Warning);
    public bool HasExportChecklistIssues => ExportChecklistErrorCount > 0 || ExportChecklistWarningCount > 0;
    public string ExportStatusText => ExportChecklistErrorCount > 0
        ? "Export has errors"
        : ExportChecklistWarningCount > 0
            ? "Ready with warnings"
            : "Ready";
    public string ExportStatusBadgeBackground => ExportChecklistErrorCount > 0
        ? "#FEF2F2"
        : ExportChecklistWarningCount > 0
            ? "#FFFBEB"
            : "#ECFDF5";
    public string ExportStatusBadgeBorder => ExportChecklistErrorCount > 0
        ? "#FECACA"
        : ExportChecklistWarningCount > 0
            ? "#FDE68A"
            : "#86EFAC";
    public string ExportStatusBadgeForeground => ExportChecklistErrorCount > 0
        ? "#991B1B"
        : ExportChecklistWarningCount > 0
            ? "#92400E"
            : "#14532D";
    public string ExportDataGridBadgeText => BuildDataGridExportSummary();
    public string ExportViewModelBadgeText => $"ViewModel: {(ShouldGenerateViewModelForExport() ? "yes" : "no")}";
    public string ExportInteractionsBadgeText => $"Interactions: {Interactions.Count}";
    public string ExportCompactSummary => BuildExportCompactSummary();
    public string ExportSummaryText => BuildExportSummaryText();
    public string ExportDependenciesSummary => BuildExportDependenciesSummary();

    public bool HasSelectedControl => SelectedControlIds.Count > 0;
    public bool HasSelectedBindingSource => SelectedBindingSource is not null;
    public bool HasNoSelectedBindingSource => SelectedBindingSource is null;
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public bool HasNoDiagnostics => !HasDiagnostics;
    public bool IsDiagnosticsPaneCollapsed => !IsDiagnosticsPaneExpanded;
    public bool IsDiagnosticsPaneBodyVisible => IsDiagnosticsPaneExpanded;
    public bool HasDiagnosticErrors => DiagnosticErrorCount > 0;
    public bool HasDiagnosticWarnings => DiagnosticWarningCount > 0;
    public int DiagnosticErrorCount => Diagnostics.Count(item => item.Severity == DocumentDiagnosticSeverity.Error);
    public int DiagnosticWarningCount => Diagnostics.Count(item => item.Severity == DocumentDiagnosticSeverity.Warning);
    public int DiagnosticInfoCount => Diagnostics.Count(item => item.Severity == DocumentDiagnosticSeverity.Info);
    public IReadOnlyList<DocumentDiagnosticModel> FilteredDiagnostics => SelectedProblemsFilter switch
    {
        ProblemsFilterErrors => Diagnostics.Where(item => item.Severity == DocumentDiagnosticSeverity.Error).ToList(),
        ProblemsFilterWarnings => Diagnostics.Where(item => item.Severity == DocumentDiagnosticSeverity.Warning).ToList(),
        ProblemsFilterHints => Diagnostics.Where(item => item.Severity == DocumentDiagnosticSeverity.Info).ToList(),
        _ => Diagnostics.ToList()
    };
    public bool HasFilteredDiagnostics => FilteredDiagnostics.Count > 0;
    public bool HasNoFilteredDiagnostics => !HasFilteredDiagnostics;
    public string ProblemsFilterSummary => SelectedProblemsFilter switch
    {
        ProblemsFilterErrors => $"Ошибки: {DiagnosticErrorCount}",
        ProblemsFilterWarnings => $"Предупреждения: {DiagnosticWarningCount}",
        ProblemsFilterHints => $"Подсказки: {DiagnosticInfoCount}",
        _ => DiagnosticsSummary
    };
    public double DiagnosticsPaneHostHeight => IsDiagnosticsPaneExpanded ? DiagnosticsPaneHeight : 56;
    public double BottomDockPanelHeight
    {
        get => DiagnosticsPaneHeight;
        set => DiagnosticsPaneHeight = Math.Clamp(value, 140, 520);
    }
    public string DiagnosticsPaneToggleText => IsDiagnosticsPaneExpanded ? "Скрыть список" : "Показать список";
    public string DiagnosticsCompactSummary => $"Ошибки: {DiagnosticErrorCount}  Предупреждения: {DiagnosticWarningCount}  Подсказки: {DiagnosticInfoCount}";
    public string DiagnosticsSummary => HasDiagnostics
        ? $"Найдено: ошибок {DiagnosticErrorCount}, предупреждений {DiagnosticWarningCount}, подсказок {DiagnosticInfoCount}."
        : "Проблем не найдено. Документ выглядит согласованным.";
    public string DiagnosticsStateText => HasDiagnostics
        ? "Проверьте сообщения ниже и перейдите к проблемным местам по кнопке."
        : "Проблем не найдено";
    public bool HasSelectedBindingSourceImportMetadata => SelectedBindingSource is not null
        && (!string.IsNullOrWhiteSpace(SelectedBindingSource.SourceAssemblyPath)
            || !string.IsNullOrWhiteSpace(SelectedBindingSource.SourceConnectionString)
            || !string.IsNullOrWhiteSpace(SelectedBindingSource.SourceTableName)
            || !string.IsNullOrWhiteSpace(SelectedBindingSource.SourceQuery));
    public bool HasBindingSources => BindingSources.Count > 0;
    public bool HasControls => Controls.Count > 0;
    public bool HasReusableTemplates => ReusableTemplates.Count > 0;
    public string ReusableTemplatesSummary
    {
        get
        {
            var builtInCount = ReusableTemplates.Count(template => template.IsBuiltIn);
            var customCount = ReusableTemplates.Count(template => !template.IsBuiltIn);
            return $"Встроенных: {builtInCount}. Пользовательских: {customCount}. Шаблон вставляется как обычные элементы документа.";
        }
    }

    public bool HasStructureTreeControls => Controls.Count > 0;
    public bool HasNoStructureTreeControls => !HasStructureTreeControls;
    public bool HasStructureSearchText => !string.IsNullOrWhiteSpace(StructureSearchText);
    public bool IsStructureTreeEmptyStateVisible =>
        !HasStructureTreeControls
        || (HasStructureSearchText && (StructureTreeItems.FirstOrDefault()?.Children.Count ?? 0) == 0);
    public string StructureTreeEmptyText => HasStructureSearchText
        ? "Ничего не найдено. Попробуйте другой фрагмент имени или типа."
        : "Элементов пока нет. Перетащите компонент на форму.";
    public string StructureTreeSummary
    {
        get
        {
            var containerCount = Controls.Count(CanHostChildren);
            var hiddenCount = Controls.Count(control => !control.IsVisible);
            var lockedCount = Controls.Count(control => control.IsLocked);
            return $"Элементов: {Controls.Count}. Контейнеров: {containerCount}. Скрыто: {hiddenCount}. Заблокировано: {lockedCount}.";
        }
    }

    public bool HasUndo => _undoStack.Count > 0;
    public bool HasRedo => _redoStack.Count > 0;
    public bool HasUndoRedoHistory => UndoRedoHistoryItems.Count > 0;
    public int UndoRedoHistoryCurrentIndex => _undoStack.Count;
    public int UndoRedoHistoryTotalCount => _undoStack.Count + 1 + _redoStack.Count;
    public string UndoRedoHistorySummary => UndoRedoHistoryTotalCount <= 1
        ? "История пока пуста. Сделайте изменение на форме, и оно появится здесь."
        : $"Шаг {UndoRedoHistoryCurrentIndex + 1} из {UndoRedoHistoryTotalCount}. Undo: {_undoStack.Count}, Redo: {_redoStack.Count}.";
    public bool HasUnsavedChanges => _currentSnapshot != _savedSnapshot;
    public string DirtyStateText => HasUnsavedChanges ? "Есть несохранённые изменения" : "Все изменения сохранены";
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public string RecentFilesSummary => HasRecentFiles
        ? $"Последних файлов: {RecentFiles.Count}"
        : "Последние файлы появятся после открытия или сохранения проекта.";
    public bool IsExportCacheStale => string.IsNullOrWhiteSpace(GeneratedXaml)
        || !string.Equals(GetSnapshotHash(_currentSnapshot), _exportCacheDocumentSnapshotHash, StringComparison.Ordinal)
        || !string.Equals(BuildExportSettingsSignature(), _exportCacheSettingsSignature, StringComparison.Ordinal);
    public string ExportCacheStatusText => IsExportCacheStale
        ? "Export устарел, нажмите «Обновить»"
        : $"Export актуален{(_exportCacheGeneratedUtc == default ? "" : $" · {_exportCacheGeneratedUtc.ToLocalTime():HH:mm:ss}")}";
    public string ExportCacheStatusBackground => IsExportCacheStale ? "#FEF3C7" : "#DCFCE7";
    public string ExportCacheStatusBorder => IsExportCacheStale ? "#F59E0B" : "#86EFAC";
    public string ExportCacheStatusForeground => IsExportCacheStale ? "#78350F" : "#166534";
    public bool HasMultipleSelection => SelectedControlIds.Count > 1;
    public int SelectionCount => SelectedControlIds.Count;
    public bool CanDuplicateSelected => SelectedControlIds.Count > 0;
    public bool CanLockSelected => GetSelectedControls().Any(control => !control.IsLocked);
    public bool CanUnlockSelected => GetSelectedControls().Any(control => control.IsLocked);
    public bool CanGroupSelection => CanGroupSelectedControls();
    public bool CanUngroupSelection => GetVisibleEditableSelectedRootControls().Any(control => control.Type == DesignerControlTypes.Group);
    public bool CanSelectedControlHostChildren => CanHostChildren(SelectedControl);
    public bool CanCopySelection => SelectedControlIds.Count > 0;
    public bool CanSaveSelectionAsTemplate => SelectedControlIds.Count > 0;
    public bool CanPasteSelection => _clipboardDocument is not null;
    public bool CanCopyStyle => SelectedControl is not null;
    public bool CanPasteStyle => _styleClipboard is not null && GetVisibleEditableSelectedRootControls().Count > 0;
    public bool CanChangeZOrder => GetEditableSelectedRootControls().Count > 0;
    public bool CanArrangeSelection => GetVisibleEditableSelectedRootControls().Count > 1;
    public bool CanDistributeSelection => GetVisibleEditableSelectedRootControls().Count > 2;
    public bool IsDesignerShellHeaderVisible => !IsImmersiveDesignerMode;
    public bool IsDesignerSidePanelsVisible => !IsImmersiveDesignerMode;
    public bool IsDesignerSurfaceToolbarVisible => true;
    public bool IsFormSizeManagedByMonitor => FormWindowState is WindowStateMaximized or WindowStateFullScreen;
    public bool IsFormSizeEditable => !IsFormSizeManagedByMonitor;
    public bool CanResizeDesignSurface => FormWindowState == WindowStateNormal && !IsUserPreviewMode;
    public double PreviewFormWidth => FormWindowState == WindowStateFullScreen
        ? _previewScreenWidth
        : FormWindowState == WindowStateMaximized
            ? _previewWorkingAreaWidth
            : DesignWidth;
    public double PreviewFormHeight => FormWindowState == WindowStateFullScreen
        ? _previewScreenHeight
        : FormWindowState == WindowStateMaximized
            ? _previewWorkingAreaHeight
            : DesignHeight;
    public string PreviewSurfaceSummary => IsFormSizeManagedByMonitor
        ? $"{_previewScreenName}: {PreviewFormWidth:0} x {PreviewFormHeight:0} px"
        : $"Размер формы: {DesignWidth:0} x {DesignHeight:0} px";
    public string PreviewSurfaceModeSummary => FormWindowState switch
    {
        WindowStateFullScreen => "Размер берется по полному разрешению текущего монитора.",
        WindowStateMaximized => "Форма заполняет рабочую область текущего монитора: с учетом панели задач и системной рамки.",
        _ => "Размер задается вручную и влияет на обычное окно."
    };
    public string ImmersiveModeButtonText => IsImmersiveDesignerMode ? "Выйти из полноэкранного режима" : "Полноэкранное редактирование";
    public string UserPreviewModeButtonText => IsUserPreviewMode ? "Показать рамки дизайнера" : "Скрыть рамки дизайнера";
    public string FormWindowDecorationsSummary => FormHasSystemDecorations ? "Системная рамка" : "Без системной рамки";

    public string FormThemeDescription => DesignerThemeCatalog.NormalizeThemeName(FormTheme) == DesignerThemeCatalog.Dark
        ? "Тёмная тема задаёт глубокий фон, светлый текст и контрастные акценты для ввода и таблиц."
        : "Светлая тема задаёт нейтральный фон, тёмный текст и яркий акцент для кнопок и активных элементов.";

    public string CurrentDocumentDisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(CurrentDocumentPath)
                ? "Без имени.formdesigner.json"
                : Path.GetFileName(CurrentDocumentPath);

            return HasUnsavedChanges ? $"{name} *" : name;
        }
    }

    public bool CanEditText => SupportsText(SelectedControl);
    public bool CanEditPlaceholder => SupportsPlaceholder(SelectedControl);
    public bool CanEditImageSource => SupportsImageSource(SelectedControl);
    public bool CanEditStretch => SupportsStretch(SelectedControl);
    public bool CanEditBackground => SupportsBackground(SelectedControl);
    public bool CanEditForeground => SupportsForeground(SelectedControl);
    public bool CanEditBorder => SupportsBorder(SelectedControl);
    public bool CanEditCornerRadius => SupportsCornerRadius(SelectedControl);
    public bool CanEditFont => SupportsFont(SelectedControl);
    public bool CanEditPadding => SupportsPadding(SelectedControl);
    public bool CanEditGridLayout => SupportsGridLayout(SelectedControl);
    public bool CanEditDataBinding => SupportsDataBinding(SelectedControl);
    public bool CanEditFieldBinding => SupportsFieldBinding(SelectedControl);
    public bool CanEditBindingEditor => CanEditDataBinding || CanEditFieldBinding;
    public bool CanChooseBindingField => CanEditFieldBinding && AvailableBindingFieldsForControl.Count > 0;
    public bool CanEditSelectedLayoutFlow => SupportsFlowLayout(SelectedControl);
    public bool CanEditSelectedLayoutGrid => SupportsGridLayout(SelectedControl);
    public bool CanEditSelectedAbsolutePosition => SelectedControl is not null && IsAbsoluteLayoutParent(SelectedControl.ParentId);
    public bool CanEditSelectedAnchors => SelectedControl is not null && IsAbsoluteLayoutParent(SelectedControl.ParentId);
    public bool IsSelectedControlManagedByLayout => SelectedControl is not null && !IsAbsoluteLayoutParent(SelectedControl.ParentId);
    public bool CanEditSurfaceFlowLayout => DesignerLayoutModes.IsFlow(SurfaceLayoutMode);
    public bool CanEditSurfaceGridLayout => DesignerLayoutModes.NormalizeMode(SurfaceLayoutMode) == DesignerLayoutModes.Grid;
    public bool CanApplyButtonVisualPresets => SelectedControl?.Type == DesignerControlTypes.Button;
    public bool CanEditDataGridBasic => SelectedControl?.Type == DesignerControlTypes.DataGrid;
    public bool CanEditCommonBackground => CanEditBackground && !CanEditDataGridBasic;
    public bool CanEditCommonForeground => CanEditForeground && !CanEditDataGridBasic;
    public bool CanEditCommonBorder => CanEditBorder && !CanEditDataGridBasic;
    public bool CanEditCommonFont => CanEditFont && !CanEditDataGridBasic;
    public bool CanEditClassicBindingEditor => CanEditBindingEditor && !CanEditDataGridBasic;
    public bool CanApplyDataGridVisualPresets => false;
    public bool CanEditDataGridGlowColor => false;
    public bool CanEditDataGridRowBackground => false;
    public bool CanEditDataGridAlternateRowBackground => false;
    public bool CanEditDataGridAdvancedVisuals => false;
    public bool CanEditDataGridTextAlignment => false;
    public bool HasDescriptorCustomProperties => DescriptorCustomPropertyEditors.Count > 0;
    public bool HasImportedDllCatalog => FilteredImportedDllCatalog.Count > 0;
    public bool HasAnyImportedDllCatalogEntries => ImportedDllCatalog.Count > 0;
    public bool HasPluginToolboxItems => PluginToolboxItems.Count > 0;
    public bool HasInstalledPlugins => InstalledPlugins.Count > 0;
    public string ImportedDllCatalogSummary => !HasAnyImportedDllCatalogEntries
        ? "Пока нет импортированных DLL. Используйте кнопку «Импорт DLL...» во вкладке «Данные» справа."
        : HasImportedDllCatalog
            ? $"Найдено DLL: {FilteredImportedDllCatalog.Count}"
            : "По текущему запросу DLL не найдены.";
    public string PluginInstallFolderPath => Path.Combine(AppContext.BaseDirectory, "Plugins");
    public string InstalledPluginsSummary => !HasInstalledPlugins
        ? "Пока не установлено ни одного plugin-пакета. Нажмите «Установить plugin...», выберите DLL плагина и конструктор добавит его контролы в этот раздел."
        : $"Установлено plugin-пакетов: {InstalledPlugins.Count}. Перетаскивайте карточки ниже прямо на форму.";
    public string PluginDiagnosticsSummary
    {
        get
        {
            if (_registry is not DesignerRegistry registry)
                return "Plugin diagnostics недоступны для текущего registry.";

            var reports = registry.GetPluginLoadReports();
            var okCount = reports.Count(report => report.Status == PluginLoadStatus.Ok);
            var warningCount = reports.Count(report => report.Status == PluginLoadStatus.Warning);
            var errorCount = reports.Count(report => report.Status == PluginLoadStatus.Error);
            var controlCount = _registry.GetControls()
                .Count(descriptor => descriptor is not MissingPluginDescriptor && IsPluginDescriptor(descriptor));

            return $"DLL найдено: {registry.LastPluginAssemblyScanCount}. Загружено: {okCount}. Warning: {warningCount}. Error: {errorCount}. Controls: {controlCount}.";
        }
    }

    public bool HasPropertyGridRows => PropertyGridCategories.Any(category => category.Rows.Count > 0);
    public bool HasNoPropertyGridRows => !HasPropertyGridRows;
    public string PropertyGridSelectionTitle => SelectedControl is null
        ? "Форма"
        : $"{SelectedControl.Name} · {SelectedControl.Type}";
    public string PropertyGridSelectionSubtitle => SelectedControl is null
        ? "Document"
        : SelectedControl.Type;
    public string PropertyGridSelectionMetrics => SelectedControl is null
        ? $"{DesignWidth:0} x {DesignHeight:0}"
        : $"X:{SelectedControl.X:0} Y:{SelectedControl.Y:0}  {SelectedControl.Width:0} x {SelectedControl.Height:0}";
    public int PropertyGridSettingsVersion => _propertyGridSettingsVersion;
    public EditorCommand? NewEditorCommand => GetEditorCommand(EditorCommandId.New);
    public EditorCommand? OpenEditorCommand => GetEditorCommand(EditorCommandId.Open);
    public EditorCommand? SaveEditorCommand => GetEditorCommand(EditorCommandId.Save);
    public EditorCommand? SaveAsEditorCommand => GetEditorCommand(EditorCommandId.SaveAs);
    public EditorCommand? UndoEditorCommand => GetEditorCommand(EditorCommandId.Undo);
    public EditorCommand? RedoEditorCommand => GetEditorCommand(EditorCommandId.Redo);
    public EditorCommand? DeleteEditorCommand => GetEditorCommand(EditorCommandId.Delete);
    public EditorCommand? DuplicateEditorCommand => GetEditorCommand(EditorCommandId.Duplicate);
    public EditorCommand? CopyEditorCommand => GetEditorCommand(EditorCommandId.Copy);
    public EditorCommand? PasteEditorCommand => GetEditorCommand(EditorCommandId.Paste);
    public EditorCommand? GroupEditorCommand => GetEditorCommand(EditorCommandId.Group);
    public EditorCommand? UngroupEditorCommand => GetEditorCommand(EditorCommandId.Ungroup);
    public EditorCommand? LockEditorCommand => GetEditorCommand(EditorCommandId.Lock);
    public EditorCommand? UnlockEditorCommand => GetEditorCommand(EditorCommandId.Unlock);
    public EditorCommand? BringToFrontEditorCommand => GetEditorCommand(EditorCommandId.BringToFront);
    public EditorCommand? SendToBackEditorCommand => GetEditorCommand(EditorCommandId.SendToBack);
    public EditorCommand? PreviewEditorCommand => GetEditorCommand(EditorCommandId.TogglePreviewMode);
    public EditorCommand? HelpEditorCommand => GetEditorCommand(EditorCommandId.OpenHelp);
    public EditorCommand? ToggleLeftPanelEditorCommand => GetEditorCommand(EditorCommandId.ToggleLeftPanel);
    public EditorCommand? ToggleRightPanelEditorCommand => GetEditorCommand(EditorCommandId.ToggleRightPanel);
    public EditorCommand? ToggleProblemsPanelEditorCommand => GetEditorCommand(EditorCommandId.ToggleProblemsPanel);
    public EditorCommand? ResetLayoutEditorCommand => GetEditorCommand(EditorCommandId.ResetLayout);
    public EditorCommand? ToggleDesignFramesEditorCommand => GetEditorCommand(EditorCommandId.ToggleDesignFrames);
    public EditorCommand? RefreshGeneratedCodeEditorCommand => GetEditorCommand(EditorCommandId.RefreshGeneratedCode);
    public EditorCommand? CopyXamlEditorCommand => GetEditorCommand(EditorCommandId.CopyXaml);
    public EditorCommand? CopyCSharpEditorCommand => GetEditorCommand(EditorCommandId.CopyCSharp);
    public EditorCommand? OpenExportDiagnosticsEditorCommand => GetEditorCommand(EditorCommandId.OpenExportDiagnostics);
    public string PropertyGridEmptyText => SelectedControl is null
        ? "Выберите элемент на canvas или в структуре, чтобы редактировать его свойства."
        : "Поиск не нашел свойств для выбранного элемента.";
    public bool HasPluginLoadIssues => _registry is DesignerRegistry registry
        && registry.GetPluginLoadReports().Any(report => report.Status is PluginLoadStatus.Warning or PluginLoadStatus.Error);
    public string SelectedTextLabel => GetPropertyDisplayTitle(SelectedControl, nameof(DesignControlModel.Text), "Текст");
    public string SelectedBackgroundLabel => SelectedControl?.Type == DesignerControlTypes.DataGrid ? "Шапка таблицы" : "Фон";
    public string SelectedLockStateSummary => SelectedControl switch
    {
        null => "Блокировка недоступна, пока элемент не выбран.",
        { IsLocked: true } => "Элемент заблокирован: на поверхности его нельзя двигать, менять размер и выбирать обычным кликом.",
        _ => "Элемент доступен для обычного редактирования на поверхности."
    };
    public string SelectedControlLayoutHint => SelectedControl is null
        ? string.Empty
        : $"Позиция {SelectedControl.Name} сейчас задается auto-layout контейнером. Меняйте порядок через z-order и настраивайте сам layout у родителя.";

    public IReadOnlyList<BindingFieldModel> AvailableBindingFieldsForControl
    {
        get
        {
            var source = SelectedBindingSourceForControl;
            return source is null ? Array.Empty<BindingFieldModel>() : OrderBindingFieldsForDisplay(source.Fields).ToList();
        }
    }

    public BindingFieldModel? SelectedBindingFieldForControl
    {
        get
        {
            if (!CanEditFieldBinding)
                return null;

            return ResolveBindingFieldForControl(SelectedBindingSourceForControl, SelectedControl?.TextBindingPath);
        }
        set
        {
            if (SelectedControl is null || !CanEditFieldBinding)
                return;

            if (value is null)
            {
                if (!string.IsNullOrWhiteSpace(SelectedControl.TextBindingPath))
                    SelectedControl.TextBindingPath = "";
                return;
            }

            var source = SelectedBindingSourceForControl;
            if (source is null)
                return;

            var bindingPath = BuildFieldBindingPath(source, value);
            if (string.Equals(SelectedControl.TextBindingPath, bindingPath, StringComparison.Ordinal))
                return;

            SelectedControl.TextBindingPath = bindingPath;
        }
    }

    public bool HasSelectedBindingPreview => SelectedBindingPreviewFields.Count > 0;

    public IReadOnlyList<BindingFieldModel> SelectedBindingPreviewFields
    {
        get
        {
            var source = SelectedBindingSourceForControl;
            if (source is null)
                return Array.Empty<BindingFieldModel>();

            if (CanEditFieldBinding)
            {
                var selectedField = SelectedBindingFieldForControl;
                if (selectedField is not null)
                    return new[] { selectedField };
            }

            var orderedFields = OrderBindingFieldsForDisplay(source.Fields).ToList();
            var previewFields = orderedFields
                .Where(field => CanEditDataBinding ? field.IsVisible : true)
                .Take(6)
                .ToList();

            if (previewFields.Count == 0)
                previewFields = orderedFields.Take(6).ToList();

            return previewFields;
        }
    }

    public string SelectedBindingPreviewTitle => CanEditFieldBinding
        ? "Пример выбранного поля"
        : "Пример данных источника";

    public bool HasGridColumnWidthEditor => CanEditDataBinding;

    public IReadOnlyList<BindingFieldModel> SelectedGridColumnsForControl
    {
        get
        {
            var source = SelectedBindingSourceForControl;
            return source is null ? Array.Empty<BindingFieldModel>() : OrderBindingFieldsForDisplay(source.Fields).ToList();
        }
    }

    public string SelectedGridColumnEditorTitle
    {
        get
        {
            return SelectedControl?.Type switch
            {
                DesignerControlTypes.DataGrid => "Колонки DataGrid",
                "Demo.TreeList" => "Колонки TreeList",
                _ => "Колонки связанного источника"
            };
        }
    }

    public string SelectedGridColumnEditorSummary
    {
        get
        {
            if (!CanEditDataBinding)
                return "Выберите таблицу, чтобы настроить ширину колонок.";

            var source = SelectedBindingSourceForControl;
            if (source is null)
                return "Сначала выберите источник данных для этой таблицы.";

            return "Ширина понимает два режима: 120 = пиксели, * = равная доля, 2* = двойная доля. Если этот же BindingSource используется в другой таблице, ширина колонок обновится и там тоже.";
        }
    }

    public string SelectedGridColumnCompactSummary
    {
        get
        {
            if (!CanEditDataBinding)
                return "Выберите DataGrid или TreeList, чтобы настроить колонки.";

            var source = SelectedBindingSourceForControl;
            if (source is null)
                return "Источник данных не выбран. Подключите BindingSource, чтобы редактировать колонки.";

            var totalCount = source.Fields.Count;
            if (totalCount == 0)
                return "Источник данных выбран, но колонок пока нет. Откройте редактор и добавьте поля.";

            var visibleCount = source.Fields.Count(field => field.IsVisible);
            var hiddenCount = totalCount - visibleCount;
            var sortableCount = source.Fields.Count(field => field.AllowSort && field.IsSortable);
            var sortedCount = source.Fields.Count(field => !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase));
            var groupedCount = source.Fields.Count(field => field.GroupOrder >= 0);
            var summaryCount = source.Fields.Count(field => BindingFieldModel.NormalizeSummaryType(field.SummaryType) != BindingFieldModel.SummaryTypeNone);

            return $"Всего: {totalCount}, видимых: {visibleCount}, скрытых: {hiddenCount}, сортируемых: {sortableCount}, сортировок: {sortedCount}, группировок: {groupedCount}, итогов: {summaryCount}.";
        }
    }

    public bool CanEditDataGridInteractions => SelectedControl?.Type == DesignerControlTypes.DataGrid;
    public bool HasSelectedDataGridInteractions => SelectedDataGridInteractions.Count > 0;

    public IReadOnlyList<InteractionModel> SelectedDataGridInteractions
    {
        get
        {
            if (SelectedControl is null)
                return Array.Empty<InteractionModel>();

            return Interactions
                .Where(interaction => string.Equals(interaction.SourceControlName, SelectedControl.Name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(interaction => interaction.TargetControlName)
                .ThenBy(interaction => interaction.TargetProperty)
                .ToList();
        }
    }

    public IReadOnlyList<string> InteractionTargetControlNames => Controls
        .Select(control => control.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> InteractionValuePathHints => new[]
        {
            InteractionModel.TargetPropertyText,
            InteractionModel.TargetPropertyContent,
            InteractionModel.TargetPropertyIsChecked,
            "Value"
        }
        .Concat(BindingSources.SelectMany(source => source.Fields.Select(field => field.Path)))
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> InteractionSourceFieldPaths
    {
        get
        {
            var source = SelectedBindingSourceForControl;
            return source is null
                ? Array.Empty<string>()
                : OrderBindingFieldsForDisplay(source.Fields)
                    .Select(field => field.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }

    public string InteractionDesignerSummary
    {
        get
        {
            if (!CanEditDataGridInteractions)
                return "Выберите DataGrid, чтобы настроить простую логику выбора строки.";

            var count = SelectedDataGridInteractions.Count;
            if (count == 0)
                return "Действий пока нет. Добавьте правило, чтобы выбранная строка DataGrid заполняла TextBox, TextBlock, Button или CheckBox.";

            return $"Настроено действий: {count}. В preview mode клик по строке применит эти правила к связанным контролам.";
        }
    }

    public bool HasInteractions => Interactions.Count > 0;
    public bool HasNoInteractions => !HasInteractions;
    public bool CanEditSelectedInteraction => SelectedInteraction is not null;

    public string LogicDesignerSummary
    {
        get
        {
            if (Interactions.Count == 0)
                return "Правил логики пока нет. Добавьте interaction, чтобы связать события элементов с действиями.";

            var sources = Interactions
                .Select(interaction => interaction.SourceControlName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return $"Interactions: {Interactions.Count}. Источников событий: {sources}. В preview mode поддерживаются основные действия без изменения документа.";
        }
    }

    public string SelectedInteractionEventHint => FindInteractionOption(
        AvailableInteractionEventOptions,
        SelectedInteraction?.EventName)?.Description
        ?? "Событие определяет, когда сработает правило.";

    public string SelectedInteractionActionHint => FindInteractionOption(
        AvailableInteractionActionOptions,
        SelectedInteraction?.ActionType)?.Description
        ?? "Действие определяет, что нужно сделать после события.";

    public string SelectedInteractionTargetPropertyHint => FindInteractionOption(
        AvailableInteractionTargetPropertyOptions,
        SelectedInteraction?.TargetProperty)?.Description
        ?? "Свойство цели показывает, куда будет записано значение.";

    public IReadOnlyList<string> InteractionSourceControlNames => Controls
        .Where(IsSupportedInteractionSource)
        .Select(control => control.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> SelectedInteractionSourcePaths
    {
        get
        {
            if (SelectedInteraction is null)
                return Array.Empty<string>();

            var source = FindControlByName(SelectedInteraction.SourceControlName);
            if (source is null)
                return Array.Empty<string>();

            if (source.Type == DesignerControlTypes.DataGrid)
            {
                var bindingSource = GetBindingSource(source.BindingSourceId);
                return bindingSource is null
                    ? Array.Empty<string>()
                    : OrderBindingFieldsForDisplay(bindingSource.Fields)
                        .Select(field => field.Path)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }

            return source.Type switch
            {
                DesignerControlTypes.TextBox => new[] { InteractionModel.TargetPropertyText },
                DesignerControlTypes.TextBlock => new[] { InteractionModel.TargetPropertyText },
                DesignerControlTypes.Button => new[] { InteractionModel.TargetPropertyContent },
                DesignerControlTypes.CheckBox => new[] { InteractionModel.TargetPropertyIsChecked },
                _ => Array.Empty<string>()
            };
        }
    }

    public string SelectedBindingEditorSummary
    {
        get
        {
            if (!CanEditBindingEditor)
                return "Выберите DataGrid или текстовый контрол, чтобы настроить привязку.";

            var source = SelectedBindingSourceForControl;
            if (source is null)
                return "Источник данных для выбранного элемента пока не выбран.";

            if (CanEditDataBinding)
                return SelectedDataGridBindingSummary;

            var field = SelectedBindingFieldForControl;
            if (field is null)
                return $"{source.Name} -> {source.Path}. Теперь выберите поле, которое должно отображаться в контроле.";

            return $"{SelectedControl?.Name} <- {source.Name}.{field.Path}  (путь привязки: {SelectedControl?.TextBindingPath})";
        }
    }

    public string SelectedControlSummary
    {
        get
        {
            if (SelectedControlIds.Count == 0)
                return "Ничего не выбрано";

            if (SelectedControl is null)
                return $"Выбрано элементов: {SelectedControlIds.Count}";

            if (SelectedControlIds.Count > 1)
            {
                var primary = SelectedControl?.Name ?? "нет";
                var lockedCount = GetSelectedControls().Count(control => control.IsLocked);
                var lockedSuffix = lockedCount == 0 ? "" : $"  Заблокировано: {lockedCount}";
                return $"Выбрано элементов: {SelectedControlIds.Count}. Главный элемент: {primary}{lockedSuffix}";
            }

            var parent = GetControl(SelectedControl.ParentId);
            var parentSuffix = parent is null ? "" : $"  Родитель: {parent.Name}";
            var lockSuffix = SelectedControl.IsLocked ? "  [Locked]" : "";
            return $"{SelectedControl.Name} [{SelectedControl.Type}]  X:{SelectedControl.X:0} Y:{SelectedControl.Y:0}  {SelectedControl.Width:0}x{SelectedControl.Height:0}{parentSuffix}{lockSuffix}";
        }
    }

    public string SelectedBindingSourceImportSummary
    {
        get
        {
            if (SelectedBindingSource is null)
                return "Ручной источник привязки";

            if (!string.IsNullOrWhiteSpace(SelectedBindingSource.SourceAssemblyPath))
            {
                var assemblyName = Path.GetFileName(SelectedBindingSource.SourceAssemblyPath);
                var tablePart = string.IsNullOrWhiteSpace(SelectedBindingSource.SourceTableName)
                    ? ""
                    : $"  Таблица: {SelectedBindingSource.SourceTableName}";

                return $"{SelectedBindingSource.SourceTypeFullName}  [{assemblyName}]{tablePart}";
            }

            if (!string.IsNullOrWhiteSpace(SelectedBindingSource.SourceConnectionString)
                || !string.IsNullOrWhiteSpace(SelectedBindingSource.SourceQuery)
                || !string.IsNullOrWhiteSpace(SelectedBindingSource.SourceTableName))
            {
                var sourceLabel = BuildSqlConnectionSummary(SelectedBindingSource.SourceConnectionString);
                var objectLabel = !string.IsNullOrWhiteSpace(SelectedBindingSource.SourceQuery)
                    ? "SQL-запрос"
                    : $"{NormalizeSqlSchemaName(SelectedBindingSource.SourceSchemaName)}.{NormalizeSqlTableName(SelectedBindingSource.SourceTableName)}";

                return $"{sourceLabel}  [{objectLabel}]";
            }

            return "Ручной источник привязки";
        }
    }

    public string SelectedDataGridBindingSummary
    {
        get
        {
            if (!CanEditDataBinding)
                return "Выберите DataGrid, чтобы настроить источник данных.";

            var source = SelectedBindingSourceForControl;
            if (source is null)
                return "Источник данных для DataGrid не выбран.";

            if (source.Fields.Count == 0)
                return $"{source.Name} -> {source.Path}: BindingSource выбран, но поля не добавлены.";

            var visibleCount = source.Fields.Count(field => field.IsVisible);
            var sortedCount = source.Fields.Count(field => !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase));
            var groupedCount = source.Fields.Count(field => field.GroupOrder >= 0);
            var hiddenCount = source.Fields.Count - visibleCount;

            return $"{source.Name} -> {source.Path} : {source.ItemTypeName} (видимых: {visibleCount}, скрытых: {hiddenCount}, сортировок: {sortedCount}, группировок: {groupedCount})";
        }
    }

    public BindingSourceModel? SelectedBindingSourceForControl
    {
        get => GetBindingSource(SelectedControl?.BindingSourceId);
        set
        {
            if (SelectedControl is null)
                return;

            var id = value?.Id ?? "";
            if (SelectedControl.BindingSourceId == id)
                return;

            SelectedControl.BindingSourceId = id;
            if (SupportsFieldBinding(SelectedControl))
            {
                if (value is null)
                {
                    SelectedControl.TextBindingPath = "";
                }
                else if (ResolveBindingFieldForControl(value, SelectedControl.TextBindingPath) is null)
                {
                    SelectedControl.TextBindingPath = "";
                }
            }

            OnPropertyChanged(nameof(SelectedBindingSourceForControl));
            OnPropertyChanged(nameof(SelectedDataGridBindingSummary));
            OnPropertyChanged(nameof(AvailableBindingFieldsForControl));
            OnPropertyChanged(nameof(SelectedBindingFieldForControl));
            OnPropertyChanged(nameof(CanChooseBindingField));
            OnPropertyChanged(nameof(SelectedBindingEditorSummary));
            OnPropertyChanged(nameof(SelectedBindingPreviewFields));
            OnPropertyChanged(nameof(HasSelectedBindingPreview));
            OnPropertyChanged(nameof(SelectedBindingPreviewTitle));
            OnPropertyChanged(nameof(HasGridColumnWidthEditor));
            OnPropertyChanged(nameof(SelectedGridColumnsForControl));
            OnPropertyChanged(nameof(SelectedGridColumnEditorSummary));
            OnPropertyChanged(nameof(SelectedGridColumnCompactSummary));
        }
    }

    /// <summary>
    /// Инициализирует коллекции документа и создает первый пустой проект конструктора.
    /// </summary>
    public MainWindowViewModel(IDesignerRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnosticsService = new DocumentDiagnosticsService(_registry);
        _propertyGridLiveRefreshTimer = new DispatcherTimer
        {
            Interval = PropertyGridLiveRefreshInterval
        };
        _propertyGridLiveRefreshTimer.Tick += PropertyGridLiveRefreshTimer_Tick;
        Controls.CollectionChanged += Controls_CollectionChanged;
        BindingSources.CollectionChanged += BindingSources_CollectionChanged;
        Interactions.CollectionChanged += Interactions_CollectionChanged;
        Diagnostics.CollectionChanged += Diagnostics_CollectionChanged;
        SelectedControlIds.CollectionChanged += SelectedControlIds_CollectionChanged;
        RecentFiles.CollectionChanged += RecentFiles_CollectionChanged;

        RefreshRegistryBackedCollections();
        LoadReusableTemplates();

        CreateNewDocumentCore(markAsSaved: true);
        RebuildStructureTree();
        RebuildImportedDllCatalog();
        RefreshDiagnostics();
        GenerateXaml();
        RegisterEditorCommands();
        RefreshEditorCommands();
        RaiseEditorCommandProperties();
    }

    public void RefreshRegistryBackedCollections()
    {
        RefreshToolboxItemsFromRegistry();
        RebuildInstalledPluginCatalog();
        RefreshDiagnostics();
    }

    private void RegisterEditorCommands()
    {
        RegisterEditorCommand(EditorCommandId.New, "New Document", "Create a new form document.", "\uE710", "Ctrl+N", EditorCommandCategory.File, () => RequestExternalEditorCommand(EditorCommandId.New));
        RegisterEditorCommand(EditorCommandId.Open, "Open...", "Open an existing .formdesigner.json document.", "\uE8E5", "Ctrl+O", EditorCommandCategory.File, () => RequestExternalEditorCommand(EditorCommandId.Open));
        RegisterEditorCommand(EditorCommandId.Save, "Save", "Save the current document.", "\uE74E", "Ctrl+S", EditorCommandCategory.File, () => RequestExternalEditorCommand(EditorCommandId.Save));
        RegisterEditorCommand(EditorCommandId.SaveAs, "Save As...", "Save the current document to a new file.", "\uE792", "Ctrl+Shift+S", EditorCommandCategory.File, () => RequestExternalEditorCommand(EditorCommandId.SaveAs));
        RegisterEditorCommand(EditorCommandId.RecentFiles, "Recent Files", "Open the recent files flyout.", "", "", EditorCommandCategory.File, () => RequestExternalEditorCommand(EditorCommandId.RecentFiles), () => StateWhen(HasRecentFiles, "No recent files."));
        RegisterEditorCommand(EditorCommandId.RestoreAutosave, "Restore Autosave", "Open autosave recovery if a draft exists.", "", "", EditorCommandCategory.File, () => RequestExternalEditorCommand(EditorCommandId.RestoreAutosave));

        RegisterEditorCommand(EditorCommandId.Undo, "Undo", "Undo the last document change.", "\uE7A7", "Ctrl+Z", EditorCommandCategory.Edit, Undo, () => StateWhen(HasUndo, "Undo stack is empty."));
        RegisterEditorCommand(EditorCommandId.Redo, "Redo", "Redo the last undone document change.", "\uE7A6", "Ctrl+Y", EditorCommandCategory.Edit, Redo, () => StateWhen(HasRedo, "Redo stack is empty."));
        RegisterEditorCommand(EditorCommandId.Cut, "Cut", "Copy and delete the selected elements.", "\uE8C6", "Ctrl+X", EditorCommandCategory.Edit, () => { CopySelection(); DeleteSelected(); }, () => StateWhen(CanCopySelection, "Select at least one element."));
        RegisterEditorCommand(EditorCommandId.Copy, "Copy", "Copy selected elements.", "\uE8C8", "Ctrl+C", EditorCommandCategory.Edit, CopySelection, () => StateWhen(CanCopySelection, "Select at least one element."));
        RegisterEditorCommand(EditorCommandId.Paste, "Paste", "Paste copied elements.", "\uE77F", "Ctrl+V", EditorCommandCategory.Edit, PasteSelection, () => StateWhen(CanPasteSelection, "Clipboard is empty."));
        RegisterEditorCommand(EditorCommandId.Delete, "Delete", "Delete selected elements.", "\uE74D", "Delete", EditorCommandCategory.Edit, DeleteSelected, () => StateWhen(HasSelectedControl, "Select an element to delete."), isDangerous: true);
        RegisterEditorCommand(EditorCommandId.Duplicate, "Duplicate", "Duplicate selected elements.", "\uE8C8", "Ctrl+D", EditorCommandCategory.Edit, DuplicateSelected, () => StateWhen(CanDuplicateSelected, "Select an element to duplicate."));
        RegisterEditorCommand(EditorCommandId.SelectAll, "Select All", "Select every top-level element on the form.", "", "Ctrl+A", EditorCommandCategory.Edit, SelectAllControls, () => StateWhen(Controls.Count > 0, "The form is empty."));

        RegisterEditorCommand(EditorCommandId.BringToFront, "Bring to Front", "Move selection to the front.", "", "PageUp", EditorCommandCategory.Arrange, BringSelectionToFront, () => StateWhen(CanChangeZOrder, "Select an editable element."));
        RegisterEditorCommand(EditorCommandId.SendToBack, "Send to Back", "Move selection to the back.", "", "PageDown", EditorCommandCategory.Arrange, SendSelectionToBack, () => StateWhen(CanChangeZOrder, "Select an editable element."));
        RegisterEditorCommand(EditorCommandId.AlignLeft, "Align Left", "Align selected elements to the left.", "", "", EditorCommandCategory.Arrange, AlignSelectionLeft, () => StateWhen(CanArrangeSelection, "Select at least two editable elements."));
        RegisterEditorCommand(EditorCommandId.AlignRight, "Align Right", "Align selected elements to the right.", "", "", EditorCommandCategory.Arrange, AlignSelectionRight, () => StateWhen(CanArrangeSelection, "Select at least two editable elements."));
        RegisterEditorCommand(EditorCommandId.AlignTop, "Align Top", "Align selected elements to the top.", "", "", EditorCommandCategory.Arrange, AlignSelectionTop, () => StateWhen(CanArrangeSelection, "Select at least two editable elements."));
        RegisterEditorCommand(EditorCommandId.AlignBottom, "Align Bottom", "Align selected elements to the bottom.", "", "", EditorCommandCategory.Arrange, AlignSelectionBottom, () => StateWhen(CanArrangeSelection, "Select at least two editable elements."));
        RegisterEditorCommand(EditorCommandId.AlignCenter, "Align Center", "Align selected elements by center.", "", "", EditorCommandCategory.Arrange, AlignSelectionCenter, () => StateWhen(CanArrangeSelection, "Select at least two editable elements."));
        RegisterEditorCommand(EditorCommandId.DistributeHorizontal, "Distribute Horizontal", "Distribute selected elements horizontally.", "", "", EditorCommandCategory.Arrange, DistributeSelectionHorizontal, () => StateWhen(CanDistributeSelection, "Select at least three editable elements."));
        RegisterEditorCommand(EditorCommandId.DistributeVertical, "Distribute Vertical", "Distribute selected elements vertically.", "", "", EditorCommandCategory.Arrange, DistributeSelectionVertical, () => StateWhen(CanDistributeSelection, "Select at least three editable elements."));

        RegisterEditorCommand(EditorCommandId.Group, "Group", "Group selected elements.", "", "Ctrl+G", EditorCommandCategory.Group, GroupSelection, () => StateWhen(CanGroupSelection, "Select at least two elements that can be grouped."));
        RegisterEditorCommand(EditorCommandId.Ungroup, "Ungroup", "Ungroup selected groups.", "", "Ctrl+Shift+G", EditorCommandCategory.Group, UngroupSelection, () => StateWhen(CanUngroupSelection, "Select a group."));
        RegisterEditorCommand(EditorCommandId.Lock, "Lock", "Lock selected elements.", "", "Ctrl+L", EditorCommandCategory.Group, LockSelected, () => StateWhen(CanLockSelected, "Selection is already locked or empty."));
        RegisterEditorCommand(EditorCommandId.Unlock, "Unlock", "Unlock selected elements.", "", "Ctrl+Shift+L", EditorCommandCategory.Group, UnlockSelected, () => StateWhen(CanUnlockSelected, "Selection is not locked."));
        RegisterEditorCommand(EditorCommandId.ToggleVisibility, "Toggle Visibility", "Show or hide selected elements.", "", "", EditorCommandCategory.Group, ToggleSelectedVisibility, () => StateWhen(HasSelectedControl, "Select an element."));

        RegisterEditorCommand(EditorCommandId.ToggleLeftPanel, "Toggle Left Panel", "Show or hide the left dock panel.", "", "", EditorCommandCategory.View, ToggleLeftDockPanel);
        RegisterEditorCommand(EditorCommandId.ToggleRightPanel, "Toggle Right Panel", "Show or hide the inspector panel.", "", "", EditorCommandCategory.View, ToggleRightDockPanel);
        RegisterEditorCommand(EditorCommandId.ToggleProblemsPanel, "Toggle Problems", "Show or hide diagnostics/problems.", "", "", EditorCommandCategory.View, ToggleBottomDockPanel);
        RegisterEditorCommand(EditorCommandId.ResetLayout, "Reset Layout", "Restore default dock panel layout.", "", "", EditorCommandCategory.View, ResetEditorShellLayout);
        RegisterEditorCommand(EditorCommandId.ZoomIn, "Zoom In", "Increase canvas zoom.", "", "Ctrl++", EditorCommandCategory.View, () => RequestExternalEditorCommand(EditorCommandId.ZoomIn));
        RegisterEditorCommand(EditorCommandId.ZoomOut, "Zoom Out", "Decrease canvas zoom.", "", "Ctrl+-", EditorCommandCategory.View, () => RequestExternalEditorCommand(EditorCommandId.ZoomOut));
        RegisterEditorCommand(EditorCommandId.Zoom100, "Zoom 100%", "Reset canvas zoom to 100%.", "", "Ctrl+0", EditorCommandCategory.View, () => RequestExternalEditorCommand(EditorCommandId.Zoom100));
        RegisterEditorCommand(EditorCommandId.FitToScreen, "Fit to Screen", "Fit canvas in the viewport.", "", "", EditorCommandCategory.View, () => RequestExternalEditorCommand(EditorCommandId.FitToScreen));
        RegisterEditorCommand(EditorCommandId.ToggleDesignFrames, "Toggle Design Frames", "Hide or show designer frames.", "", "F12", EditorCommandCategory.View, ToggleUserPreviewMode);
        RegisterEditorCommand(EditorCommandId.TogglePreviewMode, "Launch Preview", "Open runtime preview window.", "", "F5", EditorCommandCategory.View, () => RequestExternalEditorCommand(EditorCommandId.TogglePreviewMode));

        RegisterEditorCommand(EditorCommandId.OpenColumnEditor, "Open Column Editor", "Edit DataGrid columns.", "", "", EditorCommandCategory.Tools, () => RequestExternalEditorCommand(EditorCommandId.OpenColumnEditor), () => StateWhen(SelectedControl?.Type == DesignerControlTypes.DataGrid, "Selected element is not DataGrid."));
        RegisterEditorCommand(EditorCommandId.OpenBindingSourceEditor, "Open BindingSource Editor", "Switch to Data tools.", "", "", EditorCommandCategory.Tools, () => WorkspaceMode = WorkspaceModeData);
        RegisterEditorCommand(EditorCommandId.OpenInteractionDesigner, "Open Interaction Designer", "Switch to Logic tools.", "", "", EditorCommandCategory.Tools, () => WorkspaceMode = WorkspaceModeLogic);
        RegisterEditorCommand(EditorCommandId.OpenPluginDiagnostics, "Open Plugin Diagnostics", "Switch to plugin diagnostics.", "", "", EditorCommandCategory.Tools, () => WorkspaceMode = WorkspaceModePlugins);

        RegisterEditorCommand(EditorCommandId.RefreshGeneratedCode, "Refresh Generated Code", "Regenerate XAML/C# export.", "", "", EditorCommandCategory.Export, GenerateXaml);
        RegisterEditorCommand(EditorCommandId.CopyXaml, "Copy XAML", "Generate and copy XAML.", "", "", EditorCommandCategory.Export, () => RequestExternalEditorCommand(EditorCommandId.CopyXaml));
        RegisterEditorCommand(EditorCommandId.CopyCSharp, "Copy C#", "Generate and copy C#.", "", "", EditorCommandCategory.Export, () => RequestExternalEditorCommand(EditorCommandId.CopyCSharp));
        RegisterEditorCommand(EditorCommandId.RunSmokeTests, "Run Smoke Tests", "Run export smoke tests from the repository.", "", "", EditorCommandCategory.Export, () => RequestExternalEditorCommand(EditorCommandId.RunSmokeTests));
        RegisterEditorCommand(EditorCommandId.OpenExportDiagnostics, "Open Export Diagnostics", "Open export/code diagnostics.", "", "", EditorCommandCategory.Export, () => WorkspaceMode = WorkspaceModeCode);

        RegisterEditorCommand(EditorCommandId.OpenHelp, "Open Help", "Open product documentation.", "", "F1", EditorCommandCategory.Help, () => RequestExternalEditorCommand(EditorCommandId.OpenHelp));
        RegisterEditorCommand(EditorCommandId.OpenQuickStart, "Open Quick Start", "Open the onboarding help.", "", "", EditorCommandCategory.Help, () => RequestExternalEditorCommand(EditorCommandId.OpenQuickStart));
        RegisterEditorCommand(EditorCommandId.OpenPluginSdkDocs, "Open Plugin SDK Docs", "Open plugin developer documentation.", "", "", EditorCommandCategory.Help, () => RequestExternalEditorCommand(EditorCommandId.OpenPluginSdkDocs));
        RegisterEditorCommand(EditorCommandId.OpenCommandPalette, "Command Palette", "Search and run editor commands.", "", "Ctrl+Shift+P", EditorCommandCategory.Tools, OpenCommandPalette);
    }

    private EditorCommand RegisterEditorCommand(
        EditorCommandId id,
        string title,
        string description,
        string icon,
        string shortcut,
        EditorCommandCategory category,
        Action execute,
        Func<EditorCommandState>? getState = null,
        bool isDangerous = false)
    {
        return _editorCommandService.Register(new EditorCommandDefinition
        {
            Id = id,
            Title = title,
            Description = description,
            Icon = icon,
            Shortcut = shortcut,
            Category = category,
            Execute = execute,
            GetState = getState,
            IsDangerous = isDangerous
        });
    }

    private static EditorCommandState StateWhen(bool condition, string disabledReason)
    {
        return condition ? EditorCommandState.Enabled : EditorCommandState.Disabled(disabledReason);
    }

    private void RequestExternalEditorCommand(EditorCommandId id)
    {
        ExternalEditorCommandRequested?.Invoke(id);
    }

    public EditorCommand? GetEditorCommand(EditorCommandId id)
    {
        return _editorCommandService.Find(id);
    }

    public bool TryExecuteEditorCommand(EditorCommandId id)
    {
        var executed = _editorCommandService.TryExecute(id);
        RefreshEditorCommands();
        return executed;
    }

    public void RefreshEditorCommands()
    {
        _editorCommandService.Refresh();
        if (IsCommandPaletteOpen)
            RefreshCommandPaletteCommands();
    }

    private void RaiseEditorCommandProperties()
    {
        OnPropertyChanged(nameof(NewEditorCommand));
        OnPropertyChanged(nameof(OpenEditorCommand));
        OnPropertyChanged(nameof(SaveEditorCommand));
        OnPropertyChanged(nameof(SaveAsEditorCommand));
        OnPropertyChanged(nameof(UndoEditorCommand));
        OnPropertyChanged(nameof(RedoEditorCommand));
        OnPropertyChanged(nameof(DeleteEditorCommand));
        OnPropertyChanged(nameof(DuplicateEditorCommand));
        OnPropertyChanged(nameof(CopyEditorCommand));
        OnPropertyChanged(nameof(PasteEditorCommand));
        OnPropertyChanged(nameof(GroupEditorCommand));
        OnPropertyChanged(nameof(UngroupEditorCommand));
        OnPropertyChanged(nameof(LockEditorCommand));
        OnPropertyChanged(nameof(UnlockEditorCommand));
        OnPropertyChanged(nameof(BringToFrontEditorCommand));
        OnPropertyChanged(nameof(SendToBackEditorCommand));
        OnPropertyChanged(nameof(PreviewEditorCommand));
        OnPropertyChanged(nameof(HelpEditorCommand));
        OnPropertyChanged(nameof(ToggleLeftPanelEditorCommand));
        OnPropertyChanged(nameof(ToggleRightPanelEditorCommand));
        OnPropertyChanged(nameof(ToggleProblemsPanelEditorCommand));
        OnPropertyChanged(nameof(ResetLayoutEditorCommand));
        OnPropertyChanged(nameof(ToggleDesignFramesEditorCommand));
        OnPropertyChanged(nameof(RefreshGeneratedCodeEditorCommand));
        OnPropertyChanged(nameof(CopyXamlEditorCommand));
        OnPropertyChanged(nameof(CopyCSharpEditorCommand));
        OnPropertyChanged(nameof(OpenExportDiagnosticsEditorCommand));
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        IsCommandPaletteOpen = true;
        RefreshEditorCommands();
        RefreshCommandPaletteCommands();
        SelectedCommandPaletteCommand = CommandPaletteCommands.FirstOrDefault(command => command.IsEnabled)
            ?? CommandPaletteCommands.FirstOrDefault();
    }

    [RelayCommand]
    private void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
        CommandPaletteSearchText = "";
        CommandPaletteCommands.Clear();
    }

    public void CloseCommandPaletteView()
    {
        CloseCommandPalette();
    }

    [RelayCommand]
    private void ExecuteEditorCommand(EditorCommand? command)
    {
        if (command is null)
            return;

        if (!command.CanExecute(null))
        {
            StatusText = string.IsNullOrWhiteSpace(command.DisabledReason)
                ? $"Command is disabled: {command.Title}"
                : $"{command.Title}: {command.DisabledReason}";
            return;
        }

        command.Execute(null);
        CloseCommandPalette();
        RefreshEditorCommands();
    }

    [RelayCommand]
    private void ExecuteSelectedCommandPaletteCommand()
    {
        ExecuteEditorCommand(SelectedCommandPaletteCommand);
    }

    public void ExecuteSelectedCommandPaletteView()
    {
        ExecuteEditorCommand(SelectedCommandPaletteCommand);
    }

    private void RefreshCommandPaletteCommands()
    {
        var currentId = SelectedCommandPaletteCommand?.Id;
        CommandPaletteCommands.Clear();
        foreach (var command in _editorCommandService.Search(CommandPaletteSearchText, includeDisabled: true).Take(80))
            CommandPaletteCommands.Add(command);

        SelectedCommandPaletteCommand = CommandPaletteCommands.FirstOrDefault(command => command.Id == currentId)
            ?? CommandPaletteCommands.FirstOrDefault(command => command.IsEnabled)
            ?? CommandPaletteCommands.FirstOrDefault();
    }

    partial void OnCommandPaletteSearchTextChanged(string value)
    {
        if (IsCommandPaletteOpen)
            RefreshCommandPaletteCommands();
    }

    private void ToggleSelectedVisibility()
    {
        var targets = GetVisibleEditableSelectedRootControls().ToList();
        if (targets.Count == 0)
            return;

        var shouldShow = targets.Any(control => !control.IsVisible);
        BeginUndoBatch();
        foreach (var control in targets)
            control.IsVisible = shouldShow;
        CommitUndoBatch();
        StatusText = shouldShow ? "Selection is visible." : "Selection is hidden.";
    }

    [RelayCommand]
    private void ReloadPlugins()
    {
        if (_registry is not DesignerRegistry registry)
        {
            StatusText = "Plugin reload недоступен для текущего registry.";
            return;
        }

        try
        {
            registry.ClearPluginRegistrations();
            var loader = new PluginLoader(new TraceDesignerLogger());
            loader.LoadFromFolder(PluginInstallFolderPath, registry, replaceDiagnostics: true);
            RefreshRegistryBackedCollections();
            StatusText = "Plugins reloaded. Toolbox и diagnostics обновлены.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка reload plugins: {ex.Message}";
        }
    }

    private void RefreshToolboxItemsFromRegistry()
    {
        ToolboxItems.Clear();
        PluginToolboxItems.Clear();

        foreach (var descriptor in _registry.GetControls())
        {
            if (descriptor is MissingPluginDescriptor)
                continue;

            var targetCollection = ShouldShowInMainToolbox(descriptor)
                ? ToolboxItems
                : PluginToolboxItems;

            targetCollection.Add(new ToolboxItem
            {
                Title = descriptor.Title,
                Type = descriptor.TypeKey,
                Category = descriptor.Category,
                Description = descriptor.Description
            });
        }

        OnPropertyChanged(nameof(HasPluginToolboxItems));
    }

    private bool IsPluginDescriptor(IControlDescriptor descriptor)
    {
        return descriptor.GetType().Assembly != typeof(MainWindowViewModel).Assembly;
    }

    private void LoadReusableTemplates()
    {
        ReusableTemplates.Clear();

        foreach (var template in ReusableTemplateCatalog.CreateBuiltInTemplates())
            ReusableTemplates.Add(template);

        foreach (var template in _templateStorageService.LoadCustomTemplates())
            ReusableTemplates.Add(template);

        RaiseReusableTemplateProperties();
    }

    private void SaveCustomReusableTemplates()
    {
        _templateStorageService.SaveCustomTemplates(ReusableTemplates.Where(template => !template.IsBuiltIn));
        RaiseReusableTemplateProperties();
    }

    private void RaiseReusableTemplateProperties()
    {
        OnPropertyChanged(nameof(HasReusableTemplates));
        OnPropertyChanged(nameof(ReusableTemplatesSummary));
    }

    private bool ShouldShowInMainToolbox(IControlDescriptor descriptor)
    {
        if (!IsPluginDescriptor(descriptor))
            return true;

        return string.Equals(descriptor.TypeKey, "Demo.DevButton", StringComparison.Ordinal)
            || string.Equals(descriptor.TypeKey, "Demo.TreeList", StringComparison.Ordinal);
    }

    private void RebuildInstalledPluginCatalog()
    {
        var pluginGroups = BuildInstalledPluginCatalogItems();

        InstalledPlugins.Clear();
        foreach (var plugin in pluginGroups)
            InstalledPlugins.Add(plugin);

        OnPropertyChanged(nameof(HasInstalledPlugins));
        OnPropertyChanged(nameof(InstalledPluginsSummary));
        OnPropertyChanged(nameof(PluginDiagnosticsSummary));
        OnPropertyChanged(nameof(HasPluginLoadIssues));
    }

    private IReadOnlyList<InstalledPluginInfoModel> BuildInstalledPluginCatalogItems()
    {
        if (_registry is DesignerRegistry designerRegistry)
        {
            var reports = designerRegistry.GetPluginLoadReports()
                .Where(report => report.HasPluginIdentity || report.Status is PluginLoadStatus.Warning or PluginLoadStatus.Error)
                .OrderBy(report => report.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(report => report.AssemblyPath, StringComparer.OrdinalIgnoreCase)
                .Select(ToInstalledPluginInfo)
                .ToList();

            if (reports.Count > 0)
                return reports;
        }

        return _registry.GetControls()
            .Where(descriptor => descriptor is not MissingPluginDescriptor && IsPluginDescriptor(descriptor))
            .GroupBy(descriptor => descriptor.GetType().Assembly.Location, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => Path.GetFileNameWithoutExtension(group.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sampleDescriptor = group.First();
                var assembly = sampleDescriptor.GetType().Assembly;
                var controls = group
                    .Select(descriptor => $"{descriptor.Title} ({descriptor.TypeKey})")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var pluginName = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(group.Key);
                var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

                return new InstalledPluginInfoModel
                {
                    PluginName = pluginName,
                    PluginId = pluginName,
                    Version = version,
                    AssemblyPath = group.Key,
                    ControlCount = controls.Count,
                    ControlsSummary = controls.Count == 0 ? "Без контролов" : string.Join(", ", controls),
                    Summary = $"Контролов: {controls.Count}",
                    Status = "OK"
                };
            })
            .ToList();
    }

    private static InstalledPluginInfoModel ToInstalledPluginInfo(PluginLoadReport report)
    {
        var details = report.Errors.Concat(report.Warnings).ToList();
        return new InstalledPluginInfoModel
        {
            PluginName = report.DisplayName,
            PluginId = string.IsNullOrWhiteSpace(report.PluginId) ? report.AssemblyFileName : report.PluginId,
            Version = string.IsNullOrWhiteSpace(report.PluginVersion) ? "n/a" : report.PluginVersion,
            ApiVersion = string.IsNullOrWhiteSpace(report.ApiVersion) ? "n/a" : report.ApiVersion,
            AssemblyPath = report.AssemblyPath,
            ControlCount = report.ControlCount,
            ControlsSummary = report.RegisteredControls.Count == 0
                ? "Контролы не зарегистрированы"
                : string.Join(", ", report.RegisteredControls),
            Summary = report.Message,
            Status = report.StatusTitle,
            ErrorDetails = details.Count == 0 ? "" : string.Join(Environment.NewLine, details)
        };
    }

    private void RebuildImportedDllCatalog()
    {
        var groupedDlls = BindingSources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceAssemblyPath))
            .GroupBy(source => source.SourceAssemblyPath.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => Path.GetFileName(group.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sourceNames = group
                    .Select(source => source.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var typeNames = group
                    .Select(source => source.ItemTypeName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ImportedDllInfoModel
                {
                    FileName = Path.GetFileName(group.Key),
                    AssemblyPath = group.Key,
                    SourceCount = group.Count(),
                    SourceNames = sourceNames.Count == 0 ? "Источники не определены" : string.Join(", ", sourceNames),
                    TypeNames = typeNames.Count == 0 ? "Типы не определены" : string.Join(", ", typeNames),
                    Summary = $"Источников: {group.Count()} • Типов: {typeNames.Count}"
                };
            })
            .ToList();

        ImportedDllCatalog.Clear();
        foreach (var dll in groupedDlls)
            ImportedDllCatalog.Add(dll);

        RefreshImportedDllCatalogFilter();
    }

    private void RefreshImportedDllCatalogFilter()
    {
        var search = ImportedDllSearchText?.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? ImportedDllCatalog
            : new ObservableCollection<ImportedDllInfoModel>(ImportedDllCatalog.Where(item =>
                item.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.AssemblyPath.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.SourceNames.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.TypeNames.Contains(search, StringComparison.OrdinalIgnoreCase)));

        FilteredImportedDllCatalog.Clear();
        foreach (var dll in filtered)
            FilteredImportedDllCatalog.Add(dll);

        OnPropertyChanged(nameof(HasImportedDllCatalog));
        OnPropertyChanged(nameof(HasAnyImportedDllCatalogEntries));
        OnPropertyChanged(nameof(ImportedDllCatalogSummary));
    }

    public BindingSourceModel? GetBindingSource(string? bindingSourceId)
    {
        if (string.IsNullOrWhiteSpace(bindingSourceId))
            return null;

        return BindingSources.FirstOrDefault(source => source.Id == bindingSourceId);
    }

    public void UpdatePreviewDisplayMetrics(double screenWidth, double screenHeight, double workingAreaWidth, double workingAreaHeight, string? screenName)
    {
        // Превью в дизайнере ориентируется на текущий монитор пользователя,
        // но размеры документа при этом не меняются напрямую.
        var normalizedScreenWidth = Math.Max(300, screenWidth);
        var normalizedScreenHeight = Math.Max(200, screenHeight);
        var normalizedWorkingAreaWidth = Math.Max(300, workingAreaWidth);
        var normalizedWorkingAreaHeight = Math.Max(200, workingAreaHeight);
        var normalizedScreenName = string.IsNullOrWhiteSpace(screenName) ? "Текущий монитор" : screenName.Trim();

        if (Math.Abs(_previewScreenWidth - normalizedScreenWidth) < 0.1
            && Math.Abs(_previewScreenHeight - normalizedScreenHeight) < 0.1
            && Math.Abs(_previewWorkingAreaWidth - normalizedWorkingAreaWidth) < 0.1
            && Math.Abs(_previewWorkingAreaHeight - normalizedWorkingAreaHeight) < 0.1
            && string.Equals(_previewScreenName, normalizedScreenName, StringComparison.Ordinal))
        {
            return;
        }

        _previewScreenWidth = normalizedScreenWidth;
        _previewScreenHeight = normalizedScreenHeight;
        _previewWorkingAreaWidth = normalizedWorkingAreaWidth;
        _previewWorkingAreaHeight = normalizedWorkingAreaHeight;
        _previewScreenName = normalizedScreenName;

        RaisePreviewProperties();
        MarkExportCacheStale();
    }

    partial void OnImportedDllSearchTextChanged(string value)
    {
        RefreshImportedDllCatalogFilter();
    }

    public DesignControlModel? GetControl(string? controlId)
    {
        if (string.IsNullOrWhiteSpace(controlId))
            return null;

        return Controls.FirstOrDefault(control => control.Id == controlId);
    }

    public IReadOnlyList<BindingFieldModel> GetBindingFields(string? bindingSourceId)
    {
        var source = GetBindingSource(bindingSourceId);
        return source is null ? Array.Empty<BindingFieldModel>() : OrderBindingFieldsForDisplay(source.Fields).ToList();
    }

    private BindingFieldModel? ResolveBindingFieldForControl(BindingSourceModel? source, string? bindingPath)
    {
        if (source is null || string.IsNullOrWhiteSpace(bindingPath))
            return null;

        foreach (var field in source.Fields)
        {
            var directPath = field.Path?.Trim() ?? string.Empty;
            var sanitizedPath = SanitizeIdentifier(directPath, "Field");

            if (string.Equals(bindingPath, directPath, StringComparison.Ordinal)
                || string.Equals(bindingPath, sanitizedPath, StringComparison.Ordinal)
                || bindingPath.EndsWith("." + directPath, StringComparison.Ordinal)
                || bindingPath.EndsWith("." + sanitizedPath, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }

    private string BuildFieldBindingPath(BindingSourceModel source, BindingFieldModel field)
    {
        var context = GetCrudGenerationContext(source);
        if (context is null)
            return field.Path;

        var propertyName = SanitizeIdentifier(field.Path, SanitizeIdentifier(field.Header, "Field"));
        return $"{context.CurrentItemPropertyName}.{propertyName}";
    }

    public IEnumerable<DesignControlModel> GetChildControls(string? parentId)
    {
        var normalized = NormalizeId(parentId);
        return Controls.Where(control => NormalizeId(control.ParentId) == normalized);
    }

    private IEnumerable<DesignControlModel> GetRootControlsForExport()
    {
        var roots = GetChildControls(null);
        return _activeLayoutExportPlan?.UsesResponsiveStack == true
            ? roots.OrderBy(control => control.Y).ThenBy(control => control.X).ThenBy(control => control.Name).ToList()
            : roots;
    }

    public IControlDescriptor GetDescriptor(string? typeKey)
    {
        return _registry.GetRequiredControl(typeKey ?? string.Empty);
    }

    public bool CanHostChildren(DesignControlModel? control)
    {
        return control is not null && GetDescriptor(control.Type).CanHostChildren;
    }

    public void ToggleStructureControlVisibility(DesignControlModel? control)
    {
        if (control is null)
            return;

        SelectSingleControl(control);
        control.IsVisible = !control.IsVisible;
        StatusText = control.IsVisible
            ? $"Элемент {control.Name} снова видим."
            : $"Элемент {control.Name} скрыт на поверхности.";
    }

    public void ToggleStructureControlLock(DesignControlModel? control)
    {
        if (control is null)
            return;

        SelectSingleControl(control);
        control.IsLocked = !control.IsLocked;
        RaiseSelectionProperties();
        StatusText = control.IsLocked
            ? $"Элемент {control.Name} заблокирован."
            : $"Элемент {control.Name} разблокирован.";
    }

    public void MoveStructureControlLayer(DesignControlModel? control, bool towardFront)
    {
        if (control is null)
            return;

        if (control.IsLocked)
        {
            StatusText = $"Элемент {control.Name} заблокирован. Сначала разблокируйте его.";
            return;
        }

        var parentId = NormalizeId(control.ParentId);
        var siblings = Controls
            .Where(item => NormalizeId(item.ParentId) == parentId)
            .ToList();
        var currentIndex = siblings.FindIndex(item => string.Equals(item.Id, control.Id, StringComparison.OrdinalIgnoreCase));
        var targetIndex = currentIndex + (towardFront ? 1 : -1);

        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= siblings.Count)
        {
            StatusText = towardFront
                ? $"{control.Name} уже на переднем слое внутри своего контейнера."
                : $"{control.Name} уже на заднем слое внутри своего контейнера.";
            return;
        }

        (siblings[currentIndex], siblings[targetIndex]) = (siblings[targetIndex], siblings[currentIndex]);
        RebuildControlTree(new Dictionary<string, List<DesignControlModel>>(StringComparer.OrdinalIgnoreCase)
        {
            [parentId] = siblings
        });

        SelectSingleControl(control);
        NotifyDesignerStateChanged();
        StatusText = towardFront
            ? $"{control.Name} перемещен на слой ближе."
            : $"{control.Name} перемещен на слой дальше.";
    }

    [RelayCommand]
    private void SelectStructureTreeItem(StructureTreeItemModel? item)
    {
        if (item?.Control is { } control)
            SelectSingleControl(control);
    }

    [RelayCommand]
    private void RenameStructureTreeItem(StructureTreeItemModel? item)
    {
        if (item?.Control is not { } control)
            return;

        SelectSingleControl(control);
        StatusText = $"Переименуйте {control.Name} прямо в строке дерева.";
    }

    [RelayCommand]
    private void DuplicateStructureTreeItem(StructureTreeItemModel? item)
    {
        if (item?.Control is not { } control)
            return;

        SelectSingleControl(control);
        DuplicateSelected();
    }

    [RelayCommand]
    private void DeleteStructureTreeItem(StructureTreeItemModel? item)
    {
        if (item?.Control is not { } control)
            return;

        SelectSingleControl(control);
        DeleteSelected();
    }

    [RelayCommand]
    private void ToggleStructureTreeVisibility(StructureTreeItemModel? item)
    {
        ToggleStructureControlVisibility(item?.Control);
    }

    [RelayCommand]
    private void ToggleStructureTreeLock(StructureTreeItemModel? item)
    {
        ToggleStructureControlLock(item?.Control);
    }

    [RelayCommand]
    private void BringStructureTreeItemToFront(StructureTreeItemModel? item)
    {
        if (item?.Control is not { } control)
            return;

        SelectSingleControl(control);
        BringSelectionToFront();
    }

    [RelayCommand]
    private void SendStructureTreeItemToBack(StructureTreeItemModel? item)
    {
        if (item?.Control is not { } control)
            return;

        SelectSingleControl(control);
        SendSelectionToBack();
    }

    [RelayCommand]
    private void GroupStructureTreeSelection()
    {
        GroupSelection();
    }

    [RelayCommand]
    private void UngroupStructureTreeItem(StructureTreeItemModel? item)
    {
        if (item?.Control is { } control)
            SelectSingleControl(control);

        UngroupSelection();
    }

    [RelayCommand]
    private void ExpandStructureTreeItem(StructureTreeItemModel? item)
    {
        SetStructureTreeExpanded(item, isExpanded: true, includeDescendants: true);
    }

    [RelayCommand]
    private void CollapseStructureTreeItem(StructureTreeItemModel? item)
    {
        SetStructureTreeExpanded(item, isExpanded: false, includeDescendants: true);
    }

    private static void SetStructureTreeExpanded(StructureTreeItemModel? item, bool isExpanded, bool includeDescendants)
    {
        if (item is null)
            return;

        item.IsExpanded = isExpanded;
        if (!includeDescendants)
            return;

        foreach (var child in item.Children)
            SetStructureTreeExpanded(child, isExpanded, includeDescendants: true);
    }

    private IReadOnlyList<DesignPropertyDescriptor> GetCustomDescriptorProperties(DesignControlModel? control)
    {
        if (control is null)
            return Array.Empty<DesignPropertyDescriptor>();

        return GetDescriptor(control.Type).Properties
            .Where(property => string.IsNullOrWhiteSpace(property.BuiltInPropertyName))
            .ToList();
    }

    private DesignPropertyDescriptor? GetCustomDescriptorProperty(DesignControlModel? control, string propertyKey)
    {
        return GetCustomDescriptorProperties(control)
            .FirstOrDefault(property => string.Equals(property.Key, propertyKey, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsSelectedCustomDescriptorProperty(string propertyKey)
    {
        return SelectedControl is not null && GetCustomDescriptorProperty(SelectedControl, propertyKey) is not null;
    }

    private static string NormalizeCustomPropertyKey(string propertyKey)
    {
        return string.IsNullOrWhiteSpace(propertyKey) ? string.Empty : propertyKey.Trim();
    }

    private static bool TryReadCustomPropertyJson(DesignControlModel control, string propertyKey, out string valueJson)
    {
        var existing = control.CustomProperties.FirstOrDefault(value => string.Equals(value.Key, propertyKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            valueJson = "null";
            return false;
        }

        valueJson = existing.ValueJson;
        return true;
    }

    private static T DeserializeCustomPropertyValue<T>(string? valueJson, T fallback)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
            return fallback;

        try
        {
            var value = JsonSerializer.Deserialize<T>(valueJson);
            return value is null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private string GetDescriptorCustomPropertyDisplayValue(DesignPropertyDescriptor descriptor, DesignControlModel? control)
    {
        if (control is null)
            return string.Empty;

        var fallbackJson = string.IsNullOrWhiteSpace(descriptor.DefaultValueJson)
            ? "null"
            : descriptor.DefaultValueJson;
        var valueJson = TryReadCustomPropertyJson(control, descriptor.Key, out var currentJson)
            ? currentJson
            : fallbackJson;

        return descriptor.Editor switch
        {
            PropertyEditorKind.Number => DeserializeCustomPropertyValue<double>(valueJson, DeserializeCustomPropertyValue<double>(fallbackJson, 0d))
                .ToString(CultureInfo.InvariantCulture),
            PropertyEditorKind.Bool => DeserializeCustomPropertyValue<bool>(valueJson, DeserializeCustomPropertyValue<bool>(fallbackJson, false))
                ? "True"
                : "False",
            _ => DeserializeCustomPropertyValue<string>(valueJson, DeserializeCustomPropertyValue<string>(fallbackJson, string.Empty))
        };
    }

    internal string GetDescriptorCustomPropertyString(DesignPropertyDescriptor descriptor)
    {
        return GetDescriptorCustomPropertyDisplayValue(descriptor, SelectedControl);
    }

    internal bool GetDescriptorCustomPropertyBool(DesignPropertyDescriptor descriptor)
    {
        if (SelectedControl is null)
            return false;

        var fallbackJson = string.IsNullOrWhiteSpace(descriptor.DefaultValueJson)
            ? "false"
            : descriptor.DefaultValueJson;
        var valueJson = TryReadCustomPropertyJson(SelectedControl, descriptor.Key, out var currentJson)
            ? currentJson
            : fallbackJson;
        return DeserializeCustomPropertyValue<bool>(valueJson, DeserializeCustomPropertyValue<bool>(fallbackJson, false));
    }

    internal string GetDescriptorCustomPropertyColorPreview(DesignPropertyDescriptor descriptor)
    {
        var value = GetDescriptorCustomPropertyString(descriptor);
        return string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value;
    }

    public string? GetSelectedCustomPropertyString(string propertyKey)
    {
        var descriptor = GetCustomDescriptorProperty(SelectedControl, NormalizeCustomPropertyKey(propertyKey));
        return descriptor is null ? null : GetDescriptorCustomPropertyString(descriptor);
    }

    public string GetSelectedCustomPropertyColorFallback(string propertyKey)
    {
        var descriptor = GetCustomDescriptorProperty(SelectedControl, NormalizeCustomPropertyKey(propertyKey));
        if (descriptor is null)
            return "#FFFFFF";

        if (!string.IsNullOrWhiteSpace(descriptor.DefaultValueJson))
        {
            var defaultColor = DeserializeCustomPropertyValue<string>(descriptor.DefaultValueJson, "#FFFFFF");
            return string.IsNullOrWhiteSpace(defaultColor) ? "#FFFFFF" : defaultColor;
        }

        return "#FFFFFF";
    }

    private IEnumerable<DesignControlModel> GetCustomPropertyTargets(string propertyKey)
    {
        if (SelectedControl is null)
            return Enumerable.Empty<DesignControlModel>();

        var targets = GetSelectedControls()
            .Where(control => GetCustomDescriptorProperty(control, propertyKey) is not null)
            .ToList();

        if (targets.Count > 0)
            return targets;

        return GetCustomDescriptorProperty(SelectedControl, propertyKey) is null
            ? Enumerable.Empty<DesignControlModel>()
            : new[] { SelectedControl };
    }

    private bool ApplyDescriptorCustomPropertyJson(DesignPropertyDescriptor descriptor, string valueJson)
    {
        var propertyKey = NormalizeCustomPropertyKey(descriptor.Key);
        if (string.IsNullOrWhiteSpace(propertyKey))
            return false;

        var changed = false;
        foreach (var control in GetCustomPropertyTargets(propertyKey))
        {
            var existing = control.CustomProperties.FirstOrDefault(value => string.Equals(value.Key, propertyKey, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (string.Equals(existing.ValueJson, valueJson, StringComparison.Ordinal))
                    continue;

                existing.ValueJson = valueJson;
            }
            else
            {
                control.CustomProperties.Add(new DesignPropertyValueModel
                {
                    Key = propertyKey,
                    ValueJson = valueJson
                });
            }

            changed = true;
        }

        if (!changed)
            return false;

        RefreshDescriptorCustomPropertyEditors();
        NotifyDesignerStateChanged();
        return true;
    }

    internal void SetDescriptorCustomPropertyFromString(DesignPropertyDescriptor descriptor, string value)
    {
        string valueJson;
        switch (descriptor.Editor)
        {
            case PropertyEditorKind.Number:
                if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantNumber)
                    && !double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out invariantNumber))
                {
                    return;
                }

                valueJson = JsonSerializer.Serialize(invariantNumber);
                break;

            case PropertyEditorKind.Bool:
                valueJson = JsonSerializer.Serialize(string.Equals(value, "True", StringComparison.OrdinalIgnoreCase));
                break;

            default:
                valueJson = JsonSerializer.Serialize(value ?? string.Empty);
                break;
        }

        ApplyDescriptorCustomPropertyJson(descriptor, valueJson);
    }

    internal void SetDescriptorCustomPropertyFromBool(DesignPropertyDescriptor descriptor, bool value)
    {
        ApplyDescriptorCustomPropertyJson(descriptor, JsonSerializer.Serialize(value));
    }

    private void RebuildDescriptorCustomPropertyEditors()
    {
        DescriptorCustomPropertyEditors.Clear();

        foreach (var descriptor in GetCustomDescriptorProperties(SelectedControl))
            DescriptorCustomPropertyEditors.Add(new DescriptorPropertyEditorViewModel(this, descriptor));

        OnPropertyChanged(nameof(HasDescriptorCustomProperties));
    }

    private void RefreshDescriptorCustomPropertyEditors()
    {
        foreach (var editor in DescriptorCustomPropertyEditors)
            editor.RefreshFromModel();

        OnPropertyChanged(nameof(HasDescriptorCustomProperties));
    }

    private DesignPropertyDescriptor? GetDescriptorPropertyDescriptor(DesignControlModel? control, string propertyName)
    {
        if (control is null)
            return null;

        var descriptor = GetDescriptor(control.Type);
        return descriptor.Properties.FirstOrDefault(property =>
            string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(property.BuiltInPropertyName, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    public string GetPropertyDisplayTitle(DesignControlModel? control, string propertyName, string fallback)
    {
        var descriptorProperty = GetDescriptorPropertyDescriptor(control, propertyName);
        return string.IsNullOrWhiteSpace(descriptorProperty?.Title)
            ? fallback
            : descriptorProperty.Title;
    }

    private bool DescriptorDeclaresProperty(DesignControlModel? control, string propertyName)
    {
        return GetDescriptorPropertyDescriptor(control, propertyName) is not null;
    }

    public bool SupportsText(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.Text));
    }

    public bool SupportsPlaceholder(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.PlaceholderText));
    }

    public bool SupportsImageSource(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.ImageSource));
    }

    public bool SupportsStretch(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.Stretch));
    }

    public bool SupportsBackground(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.Background));
    }

    public bool SupportsForeground(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.Foreground));
    }

    public bool SupportsBorder(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.BorderBrush))
            || DescriptorDeclaresProperty(control, nameof(DesignControlModel.BorderThickness));
    }

    public bool SupportsCornerRadius(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.CornerRadius));
    }

    public bool SupportsFont(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.FontFamily))
            || DescriptorDeclaresProperty(control, nameof(DesignControlModel.FontSize))
            || DescriptorDeclaresProperty(control, nameof(DesignControlModel.FontWeight));
    }

    public bool SupportsPadding(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.Padding));
    }

    public bool SupportsFlowLayout(DesignControlModel? control)
    {
        return control?.Type is DesignerControlTypes.StackLayout or DesignerControlTypes.FlexLayout;
    }

    public bool SupportsGridLayout(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.Columns))
            || DescriptorDeclaresProperty(control, nameof(DesignControlModel.Rows));
    }

    public bool SupportsDataBinding(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.AutoGenerateColumns))
            || control?.Type == DesignerControlTypes.DataGrid;
    }

    public bool SupportsFieldBinding(DesignControlModel? control)
    {
        return DescriptorDeclaresProperty(control, nameof(DesignControlModel.TextBindingPath));
    }

    public static bool IsSupportedInteractionTarget(DesignControlModel control)
    {
        return control.Type is DesignerControlTypes.TextBox
            or DesignerControlTypes.TextBlock
            or DesignerControlTypes.Button
            or DesignerControlTypes.CheckBox;
    }

    public static bool IsSupportedInteractionSource(DesignControlModel control)
    {
        return control.Type is DesignerControlTypes.Button
            or DesignerControlTypes.TextBox
            or DesignerControlTypes.CheckBox
            or DesignerControlTypes.DataGrid;
    }

    public static string GetDefaultInteractionEvent(DesignControlModel? control)
    {
        return control?.Type switch
        {
            DesignerControlTypes.Button => InteractionModel.EventButtonClick,
            DesignerControlTypes.TextBox => InteractionModel.EventTextBoxTextChanged,
            DesignerControlTypes.CheckBox => InteractionModel.EventCheckBoxChecked,
            DesignerControlTypes.DataGrid => InteractionModel.EventDataGridSelectionChanged,
            _ => InteractionModel.EventButtonClick
        };
    }

    public static string GetDefaultInteractionSourcePath(DesignControlModel? control)
    {
        return control?.Type switch
        {
            DesignerControlTypes.TextBox => InteractionModel.TargetPropertyText,
            DesignerControlTypes.TextBlock => InteractionModel.TargetPropertyText,
            DesignerControlTypes.Button => InteractionModel.TargetPropertyContent,
            DesignerControlTypes.CheckBox => InteractionModel.TargetPropertyIsChecked,
            _ => ""
        };
    }

    public static string GetDefaultInteractionTargetProperty(DesignControlModel? control)
    {
        return control?.Type switch
        {
            DesignerControlTypes.Button => InteractionModel.TargetPropertyContent,
            DesignerControlTypes.CheckBox => InteractionModel.TargetPropertyIsChecked,
            _ => InteractionModel.TargetPropertyText
        };
    }

    public bool SupportsProperty(DesignControlModel? control, string propertyName)
    {
        return propertyName switch
        {
            nameof(DesignControlModel.Name) => control is not null,
            nameof(DesignControlModel.Text) => SupportsText(control),
            nameof(DesignControlModel.PlaceholderText) => SupportsPlaceholder(control),
            nameof(DesignControlModel.ImageSource) => SupportsImageSource(control),
            nameof(DesignControlModel.Stretch) => SupportsStretch(control),
            nameof(DesignControlModel.Background) => SupportsBackground(control),
            nameof(DesignControlModel.Foreground) => SupportsForeground(control),
            nameof(DesignControlModel.BorderBrush) => SupportsBorder(control),
            nameof(DesignControlModel.BorderThickness) => SupportsBorder(control),
            nameof(DesignControlModel.CornerRadius) => SupportsCornerRadius(control),
            nameof(DesignControlModel.FontFamily) => SupportsFont(control),
            nameof(DesignControlModel.FontWeight) => SupportsFont(control),
            nameof(DesignControlModel.FontSize) => SupportsFont(control),
            nameof(DesignControlModel.Padding) => SupportsPadding(control),
            nameof(DesignControlModel.LayoutOrientation) => SupportsFlowLayout(control),
            nameof(DesignControlModel.LayoutSpacing) => SupportsFlowLayout(control) || SupportsGridLayout(control),
            nameof(DesignControlModel.Columns) => SupportsGridLayout(control),
            nameof(DesignControlModel.Rows) => SupportsGridLayout(control),
            nameof(DesignControlModel.AutoGenerateColumns) => SupportsDataBinding(control),
            nameof(DesignControlModel.BindingSourceId) => SupportsDataBinding(control) || SupportsFieldBinding(control),
            nameof(DesignControlModel.TextBindingPath) => SupportsFieldBinding(control),
            nameof(DesignControlModel.DataGridGlowColor) => SupportsDataBinding(control),
            nameof(DesignControlModel.DataGridRowBackground) => SupportsDataBinding(control),
            nameof(DesignControlModel.DataGridAlternateRowBackground) => SupportsDataBinding(control),
            nameof(DesignControlModel.DataGridHeaderBackground) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridHeaderForeground) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridRowForeground) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridHoverRowBackground) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridSelectedRowBackground) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridSelectedRowForeground) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridGridLineBrush) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridOuterBorderBrush) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridHeaderFontSize) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridHeaderFontWeight) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridRowFontSize) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridRowFontWeight) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridHeaderHeight) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridRowHeight) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridCellPadding) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridShowHeader) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridShowRowLines) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridShowColumnLines) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridShowAlternatingRows) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.ShowFilterRow) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.FilterMode) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.ShowGroupPanel) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.AllowGrouping) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.ShowFooter) => control?.Type == DesignerControlTypes.DataGrid,
            nameof(DesignControlModel.DataGridTextAlignment) => SupportsDataBinding(control),
            nameof(DesignControlModel.Opacity) => control is not null,
            nameof(DesignControlModel.IsVisible) => control is not null,
            nameof(DesignControlModel.IsLocked) => control is not null,
            nameof(DesignControlModel.X) => control is not null,
            nameof(DesignControlModel.Y) => control is not null,
            nameof(DesignControlModel.Width) => control is not null,
            nameof(DesignControlModel.Height) => control is not null,
            nameof(DesignControlModel.AnchorLeft) => control is not null,
            nameof(DesignControlModel.AnchorTop) => control is not null,
            nameof(DesignControlModel.AnchorRight) => control is not null,
            nameof(DesignControlModel.AnchorBottom) => control is not null,
            _ => false
        };
    }

    public string GetLayoutModeForControl(DesignControlModel? control)
    {
        return control is null
            ? DesignerLayoutModes.Absolute
            : DesignerLayoutModes.NormalizeMode(GetDescriptor(control.Type).ChildLayoutMode);
    }

    public string GetLayoutModeForParent(string? parentId)
    {
        var parent = GetControl(parentId);
        return parent is null
            ? DesignerLayoutModes.NormalizeMode(SurfaceLayoutMode)
            : GetLayoutModeForControl(parent);
    }

    public bool IsAbsoluteLayoutParent(string? parentId)
    {
        return DesignerLayoutModes.IsAbsolute(GetLayoutModeForParent(parentId));
    }

    public bool IsControlSelected(DesignControlModel? control)
    {
        return control is not null && SelectedControlIds.Contains(control.Id);
    }

    public IReadOnlyList<DesignControlModel> GetSelectedControls()
    {
        return SelectedControlIds
            .Select(GetControl)
            .Where(control => control is not null)
            .Cast<DesignControlModel>()
            .ToList();
    }

    public IReadOnlyList<DesignControlModel> GetEditableSelectedControls()
    {
        return GetSelectedControls()
            .Where(control => !control.IsLocked)
            .ToList();
    }

    public IReadOnlyList<DesignControlModel> GetEditableSelectedRootControls()
    {
        var selected = GetEditableSelectedControls();
        return selected
            .Where(control => !selected.Any(other => other.Id != control.Id && IsDescendant(control.ParentId, other.Id)))
            .ToList();
    }

    public IReadOnlyList<DesignControlModel> GetVisibleEditableSelectedControls()
    {
        return GetSelectedControls()
            .Where(control => control.IsVisible && !control.IsLocked)
            .ToList();
    }

    public IReadOnlyList<DesignControlModel> GetVisibleEditableSelectedRootControls()
    {
        var selected = GetVisibleEditableSelectedControls();
        return selected
            .Where(control => !selected.Any(other => other.Id != control.Id && IsDescendant(control.ParentId, other.Id)))
            .ToList();
    }

    public void ApplyToSelectedControls(Action<DesignControlModel> apply, bool rootsOnly = false)
    {
        var targets = rootsOnly ? GetSelectedRootControls() : GetSelectedControls();
        foreach (var control in targets)
            apply(control);
    }

    public void SelectSingleControl(DesignControlModel? control)
    {
        SetSelection(control is null ? Array.Empty<DesignControlModel>() : new[] { control }, control);
    }

    public void SelectControls(IEnumerable<DesignControlModel> controls, DesignControlModel? primaryControl = null)
    {
        var selected = controls
            .Where(control => control is not null)
            .DistinctBy(control => control.Id)
            .ToList();

        var primary = primaryControl is not null && selected.Any(control => control.Id == primaryControl.Id)
            ? primaryControl
            : selected.LastOrDefault();

        SetSelection(selected, primary);
    }

    public void ToggleControlSelection(DesignControlModel control)
    {
        var selected = GetSelectedControls().ToList();

        if (selected.Any(item => item.Id == control.Id))
            selected.RemoveAll(item => item.Id == control.Id);
        else
            selected.Add(control);

        var primary = selected.LastOrDefault();
        SetSelection(selected, primary);
    }

    public void ClearSelection()
    {
        SetSelection(Array.Empty<DesignControlModel>(), null);
    }

    public void SelectAllControls()
    {
        var controls = Controls
            .Where(control => control.IsVisible && !control.IsLocked)
            .ToList();
        SetSelection(controls, controls.LastOrDefault());
    }

    private void RebuildStructureTree()
    {
        if (_isStructureTreeRefreshSuspended)
            return;

        var previousExpandedIds = EnumerateStructureTreeItems()
            .Where(item => item.IsExpanded)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = StructureSearchText.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(query);
        var source = Controls.ToList();
        var validIds = source
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var childrenByParent = source
            .GroupBy(control =>
            {
                var parentId = NormalizeId(control.ParentId);
                return validIds.Contains(parentId) ? parentId : "";
            }, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var root = new StructureTreeItemModel(
            control: null,
            id: "form-root",
            name: string.IsNullOrWhiteSpace(FormTitle) ? "Form" : FormTitle,
            type: "Form",
            text: "",
            isContainer: true,
            isGroup: false,
            isHidden: false,
            isLocked: false);
        root.IsExpanded = true;
        root.IsSearchMatch = hasSearch && MatchesStructureSearch(root.Name, root.Type, root.Text, query);

        var visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        StructureTreeItemModel? BuildNode(DesignControlModel control)
        {
            if (!visitedIds.Add(control.Id))
                return null;

            var node = CreateStructureNode(control);
            node.IsExpanded = hasSearch
                || node.IsContainer
                || previousExpandedIds.Count == 0
                || previousExpandedIds.Contains(node.Id);
            node.IsSearchMatch = hasSearch && MatchesStructureSearch(control.Name, control.Type, control.Text, query);

            if (childrenByParent.TryGetValue(control.Id, out var children))
            {
                foreach (var child in children)
                {
                    var childNode = BuildNode(child);
                    if (childNode is not null)
                        node.Children.Add(childNode);
                }
            }

            return !hasSearch || node.IsSearchMatch || node.Children.Count > 0 ? node : null;
        }

        void AddChildren(StructureTreeItemModel parentNode, string parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children))
                return;

            foreach (var child in children)
            {
                var childNode = BuildNode(child);
                if (childNode is not null)
                    parentNode.Children.Add(childNode);
            }
        }

        AddChildren(root, "");

        foreach (var orphan in source.Where(control => !visitedIds.Contains(control.Id)))
        {
            var orphanNode = BuildNode(orphan);
            if (orphanNode is not null)
                root.Children.Add(orphanNode);
        }

        StructureTreeItems.Clear();
        StructureTreeItems.Add(root);
        ApplyStructureDiagnosticsBadges();
        RefreshStructureSelection();
        RaiseStructureTreeProperties();
    }

    private StructureTreeItemModel CreateStructureNode(DesignControlModel control)
    {
        var isGroup = string.Equals(control.Type, DesignerControlTypes.Group, StringComparison.OrdinalIgnoreCase);
        var isMissingPlugin = !_registry.TryGetControl(control.Type, out _);
        return new StructureTreeItemModel(
            control,
            control.Id,
            control.Name,
            control.Type,
            control.Text,
            CanHostChildren(control),
            isGroup,
            !control.IsVisible,
            control.IsLocked,
            isMissingPlugin);
    }

    private static bool MatchesStructureSearch(string name, string type, string text, string query)
    {
        return name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || type.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(text) && text.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshStructureSelection()
    {
        var selectedIds = SelectedControlIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        StructureTreeItemModel? selectedItem = null;
        var primaryId = SelectedControl?.Id ?? "";

        foreach (var item in EnumerateStructureTreeItems())
        {
            var isSelected = item.Control is not null && selectedIds.Contains(item.Control.Id);
            item.IsSelected = isSelected;

            if (item.Control is not null
                && !string.IsNullOrWhiteSpace(primaryId)
                && string.Equals(item.Control.Id, primaryId, StringComparison.OrdinalIgnoreCase))
            {
                selectedItem = item;
            }
            else if (selectedItem is null && isSelected)
            {
                selectedItem = item;
            }
        }

        selectedItem ??= StructureTreeItems.FirstOrDefault();

        _isUpdatingStructureSelection = true;
        SelectedStructureItem = selectedItem;
        _isUpdatingStructureSelection = false;
        RaiseStructureTreeProperties();
    }

    private IEnumerable<StructureTreeItemModel> EnumerateStructureTreeItems()
    {
        foreach (var root in StructureTreeItems)
        {
            yield return root;

            foreach (var child in EnumerateStructureTreeItems(root))
                yield return child;
        }
    }

    private static IEnumerable<StructureTreeItemModel> EnumerateStructureTreeItems(StructureTreeItemModel parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;

            foreach (var descendant in EnumerateStructureTreeItems(child))
                yield return descendant;
        }
    }

    private void RaiseStructureTreeProperties()
    {
        OnPropertyChanged(nameof(HasStructureTreeControls));
        OnPropertyChanged(nameof(HasNoStructureTreeControls));
        OnPropertyChanged(nameof(HasStructureSearchText));
        OnPropertyChanged(nameof(IsStructureTreeEmptyStateVisible));
        OnPropertyChanged(nameof(StructureTreeEmptyText));
        OnPropertyChanged(nameof(StructureTreeSummary));
    }

    private void ApplyStructureDiagnosticsBadges()
    {
        var diagnosticsByControlId = Diagnostics
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.RelatedControlId))
            .GroupBy(diagnostic => diagnostic.RelatedControlId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Errors = group.Count(item => item.Severity == DocumentDiagnosticSeverity.Error),
                    Warnings = group.Count(item => item.Severity == DocumentDiagnosticSeverity.Warning)
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in EnumerateStructureTreeItems())
        {
            if (item.Control is null || !diagnosticsByControlId.TryGetValue(item.Control.Id, out var counts))
            {
                item.DiagnosticErrorCount = 0;
                item.DiagnosticWarningCount = 0;
                continue;
            }

            item.DiagnosticErrorCount = counts.Errors;
            item.DiagnosticWarningCount = counts.Warnings;
        }
    }

    private void RefreshStructureNode(DesignControlModel control)
    {
        var item = EnumerateStructureTreeItems()
            .FirstOrDefault(node => node.Control is not null
                && string.Equals(node.Control.Id, control.Id, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            RebuildStructureTree();
            return;
        }

        item.Name = control.Name;
        item.Text = control.Text;
        item.IsHidden = !control.IsVisible;
        item.IsLocked = control.IsLocked;
        RaiseStructureTreeProperties();
    }

    private static bool IsStructureTreeProperty(string? propertyName)
    {
        return propertyName is nameof(DesignControlModel.Name)
            or nameof(DesignControlModel.Type)
            or nameof(DesignControlModel.Text)
            or nameof(DesignControlModel.ParentId)
            or nameof(DesignControlModel.IsVisible)
            or nameof(DesignControlModel.IsLocked);
    }

    private static bool RequiresStructureTreeRebuild(string? propertyName)
    {
        return propertyName is nameof(DesignControlModel.ParentId)
            or nameof(DesignControlModel.Type);
    }

    [RelayCommand]
    public void RefreshDiagnostics()
    {
        var diagnostics = _diagnosticsService
            .Validate(Controls, BindingSources, Interactions, CurrentDocumentPath, DesignWidth, DesignHeight)
            .ToList();
        AppendPluginLoaderDiagnostics(diagnostics);
        AppendExportDiagnostics(diagnostics);
        AppendPreviewRuntimeDiagnostics(diagnostics);

        Diagnostics.Clear();
        foreach (var diagnostic in diagnostics)
            Diagnostics.Add(diagnostic);

        RaiseDiagnosticsProperties();
        RaiseExportChecklistProperties();
    }

    private void AppendPluginLoaderDiagnostics(ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (_registry is not DesignerRegistry registry)
            return;

        foreach (var report in registry.GetPluginLoadReports()
                     .Where(report => report.Status is PluginLoadStatus.Warning or PluginLoadStatus.Error))
        {
            var details = report.Errors.Concat(report.Warnings).ToList();
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = report.Status == PluginLoadStatus.Error
                    ? DocumentDiagnosticSeverity.Error
                    : DocumentDiagnosticSeverity.Warning,
                Source = report.DisplayName,
                Category = "Plugin loading",
                Message = report.Message,
                Recommendation = details.Count == 0
                    ? "Проверьте DLL во вкладке Plugins."
                    : string.Join(Environment.NewLine, details)
            });
        }
    }

    public void SetPreviewRuntimeDiagnostics(IEnumerable<DocumentDiagnosticModel> diagnostics)
    {
        _previewRuntimeDiagnostics.Clear();
        _previewRuntimeDiagnostics.AddRange(diagnostics);
        RefreshDiagnostics();
    }

    public void ClearPreviewRuntimeDiagnostics()
    {
        if (_previewRuntimeDiagnostics.Count == 0)
            return;

        _previewRuntimeDiagnostics.Clear();
        RefreshDiagnostics();
    }

    private void AppendPreviewRuntimeDiagnostics(ICollection<DocumentDiagnosticModel> diagnostics)
    {
        foreach (var diagnostic in _previewRuntimeDiagnostics)
            diagnostics.Add(diagnostic);
    }

    private void AppendExportDiagnostics(ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (HasExportNamespaceError())
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Error,
                Source = "Export",
                Category = "Namespace",
                Message = "Namespace проекта для экспорта пустой или некорректный.",
                Recommendation = "Укажите корректный namespace, например AvaloniaApplication1. Он должен состоять из C# identifiers, разделённых точками."
            });
        }

        var layoutPlan = BuildLayoutExportPlan();
        if (IsResponsiveLayoutExportMode)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = layoutPlan.FallbackToCanvas ? DocumentDiagnosticSeverity.Warning : DocumentDiagnosticSeverity.Info,
                Source = "Export",
                Category = "Layout",
                Message = layoutPlan.FallbackToCanvas
                    ? "Responsive layout недоступен для текущей формы, используется Canvas layout."
                    : "Responsive layout включён в экспериментальном режиме.",
                Recommendation = layoutPlan.Details
            });
        }

        if (ShouldExportRealDataGrid && Controls.Any(control => control.Type == DesignerControlTypes.DataGrid))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = "Export",
                Category = "DataGrid",
                Message = "Экспорт содержит настоящий Avalonia DataGrid.",
                Recommendation = "В новом Avalonia-проекте добавьте NuGet package Avalonia.Controls.DataGrid той же версии, что и Avalonia. XAML использует явный namespace assembly=Avalonia.Controls.DataGrid.",
            });
        }

        if (ShouldExportPortableDataGrid && Interactions.Any(interaction => IsDataGridSelectionChangedEvent(interaction.EventName)))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = "Export",
                Category = "Interactions",
                Message = "В portable export DataGrid экспортируется как безопасный визуальный placeholder.",
                Recommendation = "Для runtime-логики SelectionChanged включите режим 'Настоящий Avalonia DataGrid' и установите NuGet Avalonia.Controls.DataGrid. C# обработчик будет сгенерирован только для real DataGrid export."
            });
        }

        if (ShouldExportPortableDataGrid)
        {
            foreach (var grid in Controls.Where(control => control.Type == DesignerControlTypes.DataGrid))
            {
                var source = GetBindingSource(grid.BindingSourceId);
                string? message = null;
                string? recommendation = null;

                if (source is null)
                {
                    message = $"DataGrid '{grid.NameOrFallback()}' будет экспортирован как placeholder без источника данных.";
                    recommendation = "Подключите реальный BindingSource, если хотите получить таблицу с колонками.";
                }
                else if (source.Fields.Count == 0)
                {
                    message = $"DataGrid '{grid.NameOrFallback()}' будет экспортирован как placeholder: BindingSource не содержит полей.";
                    recommendation = "Добавьте поля в BindingSource или импортируйте схему из DLL/SQL.";
                }
                else if (!source.Fields.Any(field => field.IsVisible))
                {
                    message = $"DataGrid '{grid.NameOrFallback()}' будет экспортирован как placeholder: все поля BindingSource скрыты.";
                    recommendation = "Включите видимость хотя бы одной колонки перед экспортом.";
                }

                if (message is null)
                    continue;

                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = "Export",
                    Category = "DataGrid",
                    Message = message,
                    Recommendation = recommendation ?? "",
                    RelatedControlId = grid.Id,
                    RelatedControlName = grid.Name
                });
            }
        }

        if (IsCleanUiGenerationMode)
        {
            foreach (var grid in Controls.Where(control => control.Type == DesignerControlTypes.DataGrid && !string.IsNullOrWhiteSpace(control.BindingSourceId)))
            {
                var source = GetBindingSource(grid.BindingSourceId);
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = grid.NameOrFallback(),
                    Category = "Export",
                    Message = $"DataGrid '{grid.NameOrFallback()}' использует BindingSource только как схему колонок.",
                    Recommendation = source is null
                        ? "Проверьте BindingSource. В чистом экспорте ItemsSource и demo data не генерируются."
                        : $"В чистом экспорте будут сгенерированы колонки из '{source.NameOrFallback()}', но ItemsSource нужно подключить в вашем runtime ViewModel вручную.",
                    RelatedControlId = grid.Id,
                    RelatedControlName = grid.Name
                });
            }
        }

        if (!IncludePluginRuntimeReferences)
        {
            foreach (var pluginControl in Controls.Where(IsPluginRuntimeControl))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = pluginControl.NameOrFallback(),
                    Category = "Export",
                    Message = $"Plugin control '{pluginControl.Type}' будет экспортирован безопасным placeholder-ом.",
                    Recommendation = "Включите 'Include plugin runtime references', если новый проект должен ссылаться на runtime-сборку плагина.",
                    RelatedControlId = pluginControl.Id,
                    RelatedControlName = pluginControl.Name
                });
            }
        }
    }

    [RelayCommand]
    private void NavigateToDiagnostic(DocumentDiagnosticModel? diagnostic)
    {
        if (diagnostic is null)
            return;

        var navigated = false;

        if (!string.IsNullOrWhiteSpace(diagnostic.RelatedBindingSourceId))
        {
            var source = GetBindingSource(diagnostic.RelatedBindingSourceId);
            if (source is not null)
            {
                SelectedBindingSource = source;
                navigated = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.RelatedControlId))
        {
            var control = GetControl(diagnostic.RelatedControlId);
            if (control is not null)
            {
                SelectSingleControl(control);
                navigated = true;
            }
        }

        StatusText = navigated
            ? $"Открыта проблема: {diagnostic.Message}"
            : $"Проблема отмечена, но связанный объект уже недоступен: {diagnostic.Message}";
    }

    [RelayCommand]
    private void ToggleDiagnosticsPane()
    {
        IsDiagnosticsPaneExpanded = !IsDiagnosticsPaneExpanded;
    }

    [RelayCommand]
    private void OpenDiagnosticsMode()
    {
        IsBottomDockOpen = true;
        IsDiagnosticsPaneExpanded = true;
        StatusText = HasDiagnostics
            ? "Открыта нижняя панель Problems."
            : "Problems открыты: активных сообщений нет.";
    }

    [RelayCommand]
    private void ToggleLeftDockPanel()
    {
        IsLeftDockOpen = !IsLeftDockOpen;
    }

    [RelayCommand]
    private void ShowLeftDockPanel()
    {
        IsLeftDockOpen = true;
    }

    [RelayCommand]
    private void HideLeftDockPanel()
    {
        IsLeftDockOpen = false;
    }

    [RelayCommand]
    private void ToggleRightDockPanel()
    {
        IsRightDockOpen = !IsRightDockOpen;
    }

    [RelayCommand]
    private void ShowRightDockPanel()
    {
        IsRightDockOpen = true;
    }

    [RelayCommand]
    private void HideRightDockPanel()
    {
        IsRightDockOpen = false;
    }

    [RelayCommand]
    private void ToggleBottomDockPanel()
    {
        IsBottomDockOpen = !IsBottomDockOpen;
        if (IsBottomDockOpen)
            IsDiagnosticsPaneExpanded = true;
    }

    [RelayCommand]
    private void CloseBottomDockPanel()
    {
        IsBottomDockOpen = false;
    }

    [RelayCommand]
    private void SetProblemsFilter(string? filter)
    {
        SelectedProblemsFilter = AvailableProblemsFilters.Contains(filter ?? "")
            ? filter!
            : ProblemsFilterAll;
    }

    [RelayCommand]
    private void ResetEditorShellLayout()
    {
        IsLeftDockOpen = true;
        IsRightDockOpen = true;
        IsBottomDockOpen = HasDiagnosticErrors;
        IsDiagnosticsPaneExpanded = true;
        LeftDockPanelWidth = 280;
        RightDockPanelWidth = 380;
        DiagnosticsPaneHeight = 220;
        SelectedProblemsFilter = ProblemsFilterAll;
        StatusText = "Расположение панелей сброшено.";
    }

    private void SetLockStateForSelection(bool isLocked)
    {
        var targets = GetSelectedControls();
        if (targets.Count == 0)
            return;

        var changed = false;
        foreach (var control in targets)
        {
            if (control.IsLocked == isLocked)
                continue;

            control.IsLocked = isLocked;
            changed = true;
        }

        if (!changed)
            return;

        RaiseSelectionProperties();
        NotifyDesignerStateChanged();
        StatusText = isLocked
            ? $"Заблокировано элементов: {targets.Count(control => control.IsLocked)}."
            : "Выделенные элементы разблокированы.";
    }

    public double Snap(double value)
    {
        if (!IsGridSnapEnabled || SnapStep <= 1)
            return value;

        return Math.Round(value / SnapStep) * SnapStep;
    }

    public (double X, double Y) GetAbsolutePosition(DesignControlModel control)
    {
        var x = control.X;
        var y = control.Y;
        var currentParent = GetControl(control.ParentId);

        while (currentParent is not null)
        {
            x += currentParent.X;
            y += currentParent.Y;
            currentParent = GetControl(currentParent.ParentId);
        }

        return (x, y);
    }

    public DesignControlModel? FindDeepestContainerAt(double absoluteX, double absoluteY)
    {
        DesignControlModel? result = null;
        var maxDepth = -1;

        foreach (var control in Controls.Where(CanHostChildren).Where(control => !control.IsLocked))
        {
            var position = GetAbsolutePosition(control);
            var inside = absoluteX >= position.X
                && absoluteX <= position.X + control.Width
                && absoluteY >= position.Y
                && absoluteY <= position.Y + control.Height;

            if (!inside)
                continue;

            var depth = GetControlDepth(control);
            if (depth > maxDepth)
            {
                result = control;
                maxDepth = depth;
            }
        }

        return result;
    }

    public (double X, double Y) ToLocalPosition(string? parentId, double absoluteX, double absoluteY)
    {
        var parent = GetControl(parentId);
        if (parent is null)
            return (absoluteX, absoluteY);

        var parentPosition = GetAbsolutePosition(parent);
        return (absoluteX - parentPosition.X, absoluteY - parentPosition.Y);
    }

    public void ReparentControl(DesignControlModel control, string? newParentId, double absoluteX, double absoluteY)
    {
        var normalizedParentId = NormalizeId(newParentId);

        if (normalizedParentId == control.Id || IsDescendant(normalizedParentId, control.Id))
            return;

        control.ParentId = normalizedParentId;
        if (IsAbsoluteLayoutParent(normalizedParentId))
        {
            var local = ToLocalPosition(normalizedParentId, absoluteX, absoluteY);
            control.X = Snap(local.X);
            control.Y = Snap(local.Y);
        }
        else
        {
            control.X = 0;
            control.Y = 0;
        }

        ClampControlToSurface(control);
    }

    public void ClampControlToSurface(DesignControlModel model)
    {
        var container = GetControl(model.ParentId);
        var containerWidth = container?.Width ?? PreviewFormWidth;
        var containerHeight = container?.Height ?? PreviewFormHeight;

        var maxWidth = Math.Max(40, containerWidth - Math.Max(0, model.X));
        var maxHeight = Math.Max(24, containerHeight - Math.Max(0, model.Y));

        if (model.Width > maxWidth)
            model.Width = maxWidth;

        if (model.Height > maxHeight)
            model.Height = maxHeight;

        if (IsAbsoluteLayoutParent(model.ParentId))
        {
            model.X = Math.Clamp(model.X, 0, Math.Max(0, containerWidth - model.Width));
            model.Y = Math.Clamp(model.Y, 0, Math.Max(0, containerHeight - model.Height));
        }
        else
        {
            model.X = 0;
            model.Y = 0;
        }
    }

    public void ClampAllControlsToSurface()
    {
        foreach (var control in Controls)
            ClampControlToSurface(control);
    }

    public void MoveSelectedControl(double dx, double dy)
    {
        var roots = GetEditableSelectedRootControls();
        if (roots.Count == 0)
            return;

        BeginUndoBatch();
        try
        {
            foreach (var control in roots)
            {
                if (!IsAbsoluteLayoutParent(control.ParentId))
                    continue;

                control.X += dx;
                control.Y += dy;
                ClampControlToSurface(control);
            }
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    public DesignControlModel CreateControl(string type, double x, double y, string? parentId = null, bool bypassGridSnap = false)
    {
        // Контрол всегда создается из стартового шаблона,
        // а координаты сразу привязываются к сетке и границам контейнера.
        var model = CreateDefaultControl(type);
        model.Name = GetUniqueControlName(type);
        model.ParentId = NormalizeId(parentId);

        if (IsAbsoluteLayoutParent(model.ParentId))
        {
            model.X = bypassGridSnap ? x : Snap(x);
            model.Y = bypassGridSnap ? y : Snap(y);
        }
        else
        {
            model.X = 0;
            model.Y = 0;
        }

        ClampControlToSurface(model);
        Controls.Add(model);
        SelectedControl = model;
        return model;
    }

    public BindingSourceModel AddBindingSource()
    {
        var name = GetUniqueBindingSourceName("BindingSource");

        var bindingSource = new BindingSourceModel
        {
            Name = name,
            Path = name,
            ItemTypeName = "RowItem",
            Description = "Ручной источник данных. Добавьте поля или импортируйте схему из DLL/SQL.",
            SourceKind = "Manual",
            SourceSchemaName = "dbo"
        };

        BindingSources.Add(bindingSource);
        SelectedBindingSource = bindingSource;
        return bindingSource;
    }

    public BindingSourceModel AddSqlBindingSource()
    {
        var source = AddBindingSource();
        source.Name = GetUniqueBindingSourceName("SqlSource");
        source.Path = source.Name;
        source.ItemTypeName = "SqlRow";
        source.Description = "Источник SQL Server. Укажите строку подключения и таблицу или SQL-запрос.";
        source.SourceKind = "SqlServer";
        source.SourceSchemaName = "dbo";
        source.SourceTableName = "";
        source.SourceQuery = "";
        StatusText = "Создан SQL BindingSource. Заполните строку подключения и нажмите «Подтянуть из БД».";
        RaiseBindingEditorProperties();
        return source;
    }

    public int ImportBindingSourcesFromAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new FileNotFoundException("Файл сборки не найден.", assemblyPath);

        // Одна DLL может содержать сразу несколько сущностей,
        // поэтому импорт возвращает набор BindingSourceModel, а не один объект.
        var discoveryResult = DiscoverBindingSourcesFromAssembly(assemblyPath);
        var importedSources = discoveryResult.Sources;
        if (importedSources.Count == 0)
        {
            StatusText = BuildBindingImportFailureStatus(assemblyPath, discoveryResult);
            return 0;
        }

        var importedCount = 0;
        BindingSourceModel? firstImported = null;

        BeginUndoBatch();
        try
        {
            foreach (var importedSource in importedSources)
            {
                var bindingSource = MergeImportedBindingSource(importedSource);
                firstImported ??= bindingSource;
                importedCount++;
            }

            if (firstImported is not null)
                SelectedBindingSource = firstImported;

            StatusText = BuildBindingImportSuccessStatus(assemblyPath, importedCount, discoveryResult);
        }
        finally
        {
            CommitUndoBatch();
        }

        return importedCount;
    }

    public async Task<int> RefreshSelectedBindingSourceFromDatabaseAsync()
    {
        if (SelectedBindingSource is null)
            return 0;

        // Перечитываем схему из БД и затем "накладываем" ее на существующий источник,
        // чтобы сохранить ручные подписи, сортировки и видимость колонок.
        var originalConnectionString = SelectedBindingSource.SourceConnectionString;
        var importedSource = await CreateBindingSourceFromDatabaseAsync(SelectedBindingSource);

        var objectLabel = !string.IsNullOrWhiteSpace(importedSource.SourceQuery)
            ? "SQL-запрос"
            : $"{NormalizeSqlSchemaName(importedSource.SourceSchemaName)}.{NormalizeSqlTableName(importedSource.SourceTableName)}";
        var usedCertificateFallback = !string.Equals(originalConnectionString, importedSource.SourceConnectionString, StringComparison.Ordinal);

        BeginUndoBatch();
        try
        {
            ApplyDatabaseSourceToSelectedBindingSource(importedSource);
            StatusText = usedCertificateFallback
                ? $"Подтянуто полей из БД: {importedSource.Fields.Count} ({objectLabel}). Для подключения автоматически включен TrustServerCertificate=True."
                : $"Подтянуто полей из БД: {importedSource.Fields.Count} ({objectLabel})";
        }
        finally
        {
            CommitUndoBatch();
        }

        return importedSource.Fields.Count;
    }

    public void ClearSelectedBindingSourceQuery()
    {
        if (SelectedBindingSource is null || string.IsNullOrWhiteSpace(SelectedBindingSource.SourceQuery))
            return;

        SelectedBindingSource.SourceQuery = "";
        StatusText = "SQL-запрос очищен. Теперь будет использоваться таблица.";
    }

    public void RemoveSelectedBindingSource()
    {
        if (SelectedBindingSource is null)
            return;

        BeginUndoBatch();
        try
        {
            var removedId = SelectedBindingSource.Id;
            BindingSources.Remove(SelectedBindingSource);

            foreach (var control in Controls.Where(control => control.BindingSourceId == removedId))
                control.BindingSourceId = "";

            SelectedBindingSource = BindingSources.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedBindingSourceForControl));
            OnPropertyChanged(nameof(SelectedDataGridBindingSummary));
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    public BindingFieldModel? AddBindingField()
    {
        if (SelectedBindingSource is null)
            return null;

        var index = SelectedBindingSource.Fields.Count + 1;
        var field = new BindingFieldModel
        {
            Header = $"Колонка {index}",
            Path = $"Field{index}",
            SampleValue = $"Значение {index}",
            Width = "*",
            TypeName = "string"
        };

        SelectedBindingSource.Fields.Add(field);
        return field;
    }

    public void RemoveBindingField(BindingFieldModel? field)
    {
        if (SelectedBindingSource is null || field is null)
            return;

        SelectedBindingSource.Fields.Remove(field);
    }

    [RelayCommand]
    private void ClearSelectedBindingSourceGrouping()
    {
        if (SelectedBindingSource is null)
            return;

        BeginUndoBatch();
        try
        {
            var changed = false;
            foreach (var field in SelectedBindingSource.Fields.Where(field => field.GroupOrder >= 0).ToList())
            {
                field.GroupOrder = -1;
                changed = true;
            }

            if (!changed)
                return;

            StatusText = "Группировка колонок очищена.";
            NotifyDesignerStateChanged();
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    public void ApplySampleImageToSelected()
    {
        if (SelectedControl?.Type != DesignerControlTypes.Image)
            return;

        SelectedControl.ImageSource = "avares://FormDesigner/Assets/avalonia-logo.ico";
    }

    public string ExportDocumentJson()
    {
        return JsonSerializer.Serialize(CreateDocumentFileModel(), JsonOptions);
    }

    public DesignerDocumentFileModel CreatePreviewDocumentSnapshot()
    {
        return CreateDocumentFileModel();
    }

    public string GetRecoveryDisplayName()
    {
        return string.IsNullOrWhiteSpace(CurrentDocumentPath)
            ? "Без имени.formdesigner.json"
            : Path.GetFileName(CurrentDocumentPath);
    }

    public void LoadDocumentJson(string json, string? sourcePath = null, bool markAsSaved = true)
    {
        var document = JsonSerializer.Deserialize<DesignerDocumentFileModel>(json, JsonOptions)
            ?? throw new InvalidOperationException("Не удалось прочитать документ конструктора.");

        ApplyDocument(document, sourcePath, markAsSaved, resetDocumentSession: true);
    }

    public void MarkDocumentSaved(string path)
    {
        CurrentDocumentPath = path;
        _savedSnapshot = _currentSnapshot;
        StatusText = $"Сохранен документ: {Path.GetFileName(path)}";
        RaiseDocumentStateProperties();
    }

    public void ApplyAppSettings(AppSettingsModel settings)
    {
        ApplyExportCache(settings.ExportCache);
        ApplyPropertyGridSettings(settings.PropertyGrid, settings.PropertyGridFavorites, settings.PropertyGridCollapsedCategories);

        RecentFiles.Clear();
        foreach (var recentFile in settings.RecentFiles
                     .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                     .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.OrderByDescending(item => item.LastOpenedUtc).First())
                     .OrderByDescending(item => item.LastOpenedUtc)
                     .Take(10))
        {
            RecentFiles.Add(recentFile);
        }

        if (AvailableWorkspaceModes.Contains(settings.Session.WorkspaceMode))
            WorkspaceMode = settings.Session.WorkspaceMode;

        ApplyEditorShellLayout(settings.Session.EditorShell);
    }

    public PropertyGridUserSettings CapturePropertyGridSettings()
    {
        return ClonePropertyGridSettings(_propertyGridUserSettings);
    }

    public List<string> CapturePropertyGridFavorites()
    {
        return _propertyGridUserSettings.FavoritePropertiesByTypeKey
            .SelectMany(pair => pair.Value.Select(value => FavoriteKey(pair.Key, value)))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<string> CapturePropertyGridCollapsedCategories()
    {
        return _propertyGridUserSettings.ExpandedCategoriesByTypeKey
            .SelectMany(pair => GetPropertyGridCategoryOrder().Where(category => !pair.Value.Contains(category)).Select(category => $"{pair.Key}.{category}"))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ExportCacheModel CaptureExportCache()
    {
        return new ExportCacheModel
        {
            ExportTarget = ExportTarget,
            ExportProjectNamespace = ExportProjectNamespace,
            DataGridExportMode = DataGridExportMode,
            LayoutExportMode = LayoutExportMode,
            XamlVerbosity = XamlVerbosity,
            IncludeExportComments = IncludeExportComments,
            IncludeSampleData = IncludeSampleData,
            IncludeCrudSkeleton = IncludeCrudSkeleton,
            IncludeCommunityToolkitAttributes = IncludeCommunityToolkitAttributes,
            IncludePluginRuntimeReferences = IncludePluginRuntimeReferences,
            GeneratedXaml = GeneratedXaml,
            GeneratedCSharp = GeneratedCSharp,
            GeneratedBindingGuide = GeneratedBindingGuide,
            DocumentSnapshotHash = _exportCacheDocumentSnapshotHash,
            SettingsSignature = _exportCacheSettingsSignature,
            GeneratedUtc = _exportCacheGeneratedUtc
        };
    }

    public EditorShellLayoutState CaptureEditorShellLayoutState()
    {
        return new EditorShellLayoutState
        {
            IsLeftPanelVisible = IsLeftDockOpen,
            IsRightPanelVisible = IsRightDockOpen,
            IsBottomPanelVisible = IsBottomDockOpen,
            LeftPanelWidth = Math.Clamp(LeftDockPanelWidth, 220, 420),
            RightPanelWidth = Math.Clamp(RightDockPanelWidth, 280, 560),
            BottomPanelHeight = Math.Clamp(DiagnosticsPaneHeight, 140, 520),
            ActiveLeftTab = IsDataMode ? "Data" : IsHistoryMode ? "History" : "Components",
            ActiveRightTab = IsDataMode ? "Data" : IsPluginsMode ? "Plugins" : IsCodeMode ? "Code" : IsLogicMode ? "Logic" : "Properties",
            ActiveBottomTab = "Diagnostics"
        };
    }

    public void ApplyEditorShellLayout(EditorShellLayoutState? layout)
    {
        if (layout is null)
            return;

        IsLeftDockOpen = layout.IsLeftPanelVisible;
        IsRightDockOpen = layout.IsRightPanelVisible;
        IsBottomDockOpen = layout.IsBottomPanelVisible;
        LeftDockPanelWidth = Math.Clamp(layout.LeftPanelWidth <= 0 ? 280 : layout.LeftPanelWidth, 220, 420);
        RightDockPanelWidth = Math.Clamp(layout.RightPanelWidth <= 0 ? 380 : layout.RightPanelWidth, 280, 560);
        DiagnosticsPaneHeight = Math.Clamp(layout.BottomPanelHeight <= 0 ? 220 : layout.BottomPanelHeight, 140, 520);
    }

    private void ApplyPropertyGridSettings(
        PropertyGridUserSettings? settings,
        IEnumerable<string>? legacyFavorites,
        IEnumerable<string>? legacyCollapsedCategories)
    {
        CopyPropertyGridSettingsInto(settings ?? new PropertyGridUserSettings(), _propertyGridUserSettings);

        if (_propertyGridUserSettings.FavoritePropertiesByTypeKey.Count == 0
            && legacyFavorites?.Any(item => !string.IsNullOrWhiteSpace(item)) == true)
        {
            MigrateLegacyPropertyGridFavorites(legacyFavorites);
        }

        if (_propertyGridUserSettings.ExpandedCategoriesByTypeKey.Count == 0
            && legacyCollapsedCategories?.Any(item => !string.IsNullOrWhiteSpace(item)) == true)
        {
            MigrateLegacyPropertyGridCollapsedCategories(legacyCollapsedCategories);
        }

        RemoveLegacyAutoFavorites();
        RebuildPropertyGrid();
    }

    private static PropertyGridUserSettings ClonePropertyGridSettings(PropertyGridUserSettings source)
    {
        var clone = new PropertyGridUserSettings();
        CopyPropertyGridSettingsInto(source, clone);
        return clone;
    }

    private static void CopyPropertyGridSettingsInto(PropertyGridUserSettings source, PropertyGridUserSettings target)
    {
        target.FavoritePropertiesByTypeKey.Clear();
        target.UserCustomizedTypeKeys.Clear();
        target.ExpandedCategoriesByTypeKey.Clear();

        var favoriteProperties = source.FavoritePropertiesByTypeKey ?? new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var customizedTypeKeys = source.UserCustomizedTypeKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expandedCategories = source.ExpandedCategoriesByTypeKey ?? new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in favoriteProperties)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                continue;

            target.FavoritePropertiesByTypeKey[pair.Key.Trim()] = pair.Value
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var typeKey in customizedTypeKeys.Where(value => !string.IsNullOrWhiteSpace(value)))
            target.UserCustomizedTypeKeys.Add(typeKey.Trim());

        foreach (var pair in expandedCategories)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                continue;

            target.ExpandedCategoriesByTypeKey[pair.Key.Trim()] = pair.Value
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void MigrateLegacyPropertyGridFavorites(IEnumerable<string> legacyFavorites)
    {
        foreach (var favorite in legacyFavorites.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()))
        {
            var dotIndex = favorite.IndexOf('.');
            if (dotIndex <= 0 || dotIndex >= favorite.Length - 1)
                continue;

            var typeKey = favorite[..dotIndex];
            var propertyKey = favorite[(dotIndex + 1)..];
            if (typeKey == "*" || IsLegacyDefaultPropertyGridFavorite(typeKey, propertyKey))
                continue;

            GetPropertyGridFavoriteSet(typeKey, create: true).Add(propertyKey);
            _propertyGridUserSettings.UserCustomizedTypeKeys.Add(typeKey);
        }
    }

    private static bool IsLegacyDefaultPropertyGridFavorite(string typeKey, string propertyKey)
    {
        if (string.Equals(typeKey, DesignerControlTypes.DataGrid, StringComparison.OrdinalIgnoreCase))
        {
            return propertyKey is nameof(DesignControlModel.BindingSourceId)
                or "Columns"
                or nameof(DesignControlModel.AutoGenerateColumns)
                or nameof(DesignControlModel.AllowGrouping)
                or nameof(DesignControlModel.ShowFilterRow)
                or nameof(DesignControlModel.ShowGroupPanel);
        }

        return false;
    }

    private void RemoveLegacyAutoFavorites()
    {
        _propertyGridUserSettings.FavoritePropertiesByTypeKey.Remove("*");
        _propertyGridUserSettings.UserCustomizedTypeKeys.Remove("*");

        foreach (var pair in _propertyGridUserSettings.FavoritePropertiesByTypeKey.ToList())
        {
            var removedAutoFavorite = false;
            foreach (var propertyKey in pair.Value.ToList())
            {
                if (!IsLegacyDefaultPropertyGridFavorite(pair.Key, propertyKey))
                    continue;

                pair.Value.Remove(propertyKey);
                removedAutoFavorite = true;
            }

            if (removedAutoFavorite && pair.Value.Count == 0)
            {
                _propertyGridUserSettings.FavoritePropertiesByTypeKey.Remove(pair.Key);
                _propertyGridUserSettings.UserCustomizedTypeKeys.Remove(pair.Key);
            }
        }
    }

    private void MigrateLegacyPropertyGridCollapsedCategories(IEnumerable<string> legacyCollapsedCategories)
    {
        var expanded = GetDefaultPropertyGridExpandedCategories();
        foreach (var category in legacyCollapsedCategories.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()))
            expanded.Remove(category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category);

        _propertyGridUserSettings.ExpandedCategoriesByTypeKey[CurrentPropertyGridTypeKey] = expanded;
    }

    private static string FavoriteKey(string typeKey, string propertyKey)
    {
        return $"{NormalizePropertyGridTypeKey(typeKey)}.{propertyKey}";
    }

    private string CurrentPropertyGridTypeKey => NormalizePropertyGridTypeKey(SelectedControl?.Type ?? "Form");

    private static string NormalizePropertyGridTypeKey(string? typeKey)
    {
        return string.IsNullOrWhiteSpace(typeKey) ? "Form" : typeKey.Trim();
    }

    private HashSet<string> GetPropertyGridFavoriteSet(string typeKey, bool create)
    {
        var normalizedType = NormalizePropertyGridTypeKey(typeKey);
        if (_propertyGridUserSettings.FavoritePropertiesByTypeKey.TryGetValue(normalizedType, out var set))
            return set;

        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (create)
            _propertyGridUserSettings.FavoritePropertiesByTypeKey[normalizedType] = set;

        return set;
    }

    private bool IsPropertyGridFavorite(string propertyKey)
    {
        var typeKey = CurrentPropertyGridTypeKey;
        return _propertyGridUserSettings.UserCustomizedTypeKeys.Contains(typeKey)
            && GetPropertyGridFavoriteSet(typeKey, create: false).Contains(propertyKey);
    }

    [RelayCommand]
    private void TogglePropertyGridFavorite(PropertyGridRowViewModel? row)
    {
        if (row is null)
            return;

        var typeKey = CurrentPropertyGridTypeKey;
        var favorites = _propertyGridUserSettings.UserCustomizedTypeKeys.Contains(typeKey)
            ? GetPropertyGridFavoriteSet(typeKey, create: true)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (favorites.Contains(row.Key))
            favorites.Remove(row.Key);
        else
            favorites.Add(row.Key);

        _propertyGridUserSettings.FavoritePropertiesByTypeKey[typeKey] = favorites;
        _propertyGridUserSettings.UserCustomizedTypeKeys.Add(typeKey);
        RaisePropertyGridSettingsChanged();
        RebuildPropertyGrid();
    }

    [RelayCommand]
    private void ResetPropertyGridView()
    {
        var typeKey = CurrentPropertyGridTypeKey;
        _propertyGridUserSettings.FavoritePropertiesByTypeKey.Remove(typeKey);
        _propertyGridUserSettings.UserCustomizedTypeKeys.Remove(typeKey);
        _propertyGridUserSettings.ExpandedCategoriesByTypeKey.Remove(typeKey);
        RaisePropertyGridSettingsChanged();
        RebuildPropertyGrid();
    }

    [RelayCommand]
    private void CollapseAllPropertyGridCategories()
    {
        _propertyGridUserSettings.ExpandedCategoriesByTypeKey[CurrentPropertyGridTypeKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RaisePropertyGridSettingsChanged();
        RebuildPropertyGrid();
    }

    [RelayCommand]
    private void ExpandBasicPropertyGridCategories()
    {
        _propertyGridUserSettings.ExpandedCategoriesByTypeKey[CurrentPropertyGridTypeKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PropertyGridCategoryFavorites,
            PropertyGridCategoryCommon,
            PropertyGridCategoryLayout,
            PropertyGridCategoryData
        };
        RaisePropertyGridSettingsChanged();
        RebuildPropertyGrid();
    }

    [RelayCommand]
    private void ClearPropertyGridSearch()
    {
        PropertyGridSearchText = "";
    }

    private void SetPropertyGridCategoryExpanded(PropertyGridCategoryViewModel category, bool isExpanded)
    {
        if (_isRebuildingPropertyGrid)
            return;

        var expanded = GetPropertyGridExpandedCategories(CurrentPropertyGridTypeKey, create: true);
        if (isExpanded)
            expanded.Add(category.Key);
        else
            expanded.Remove(category.Key);

        RaisePropertyGridSettingsChanged();
    }

    private HashSet<string> GetPropertyGridExpandedCategories(string typeKey, bool create)
    {
        var normalizedType = NormalizePropertyGridTypeKey(typeKey);
        if (_propertyGridUserSettings.ExpandedCategoriesByTypeKey.TryGetValue(normalizedType, out var set))
            return set;

        set = GetDefaultPropertyGridExpandedCategories();
        if (create)
            _propertyGridUserSettings.ExpandedCategoriesByTypeKey[normalizedType] = set;

        return set;
    }

    private static HashSet<string> GetDefaultPropertyGridExpandedCategories()
    {
        return new HashSet<string>(GetPropertyGridCategoryOrder().Where(category => category != PropertyGridCategoryAdvanced), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetPropertyGridCategoryOrder()
    {
        return new[]
        {
            PropertyGridCategoryFavorites,
            PropertyGridCategoryCommon,
            PropertyGridCategoryLayout,
            PropertyGridCategoryAppearance,
            PropertyGridCategoryData,
            PropertyGridCategoryBehavior,
            PropertyGridCategoryInteraction,
            PropertyGridCategoryExport,
            PropertyGridCategoryAdvanced
        };
    }

    private void RaisePropertyGridSettingsChanged()
    {
        _propertyGridSettingsVersion++;
        OnPropertyChanged(nameof(PropertyGridSettingsVersion));
    }

    public void BeginPropertyGridLiveGesture()
    {
        _isPropertyGridLiveGesture = true;
        _hasPendingPropertyGridLiveRefresh = false;
        _propertyGridLiveRefreshTimer.Stop();
        _lastPropertyGridLiveRefreshUtc = DateTime.MinValue;
        RefreshLivePropertyGridLayoutRows();
    }

    public void RequestPropertyGridLiveGestureRefresh()
    {
        if (!_isPropertyGridLiveGesture)
            return;

        var now = DateTime.UtcNow;
        if (now - _lastPropertyGridLiveRefreshUtc >= PropertyGridLiveRefreshInterval)
        {
            _propertyGridLiveRefreshTimer.Stop();
            _hasPendingPropertyGridLiveRefresh = false;
            RefreshLivePropertyGridLayoutRows(now);
            return;
        }

        _hasPendingPropertyGridLiveRefresh = true;
        if (!_propertyGridLiveRefreshTimer.IsEnabled)
        {
            var remaining = PropertyGridLiveRefreshInterval - (now - _lastPropertyGridLiveRefreshUtc);
            _propertyGridLiveRefreshTimer.Interval = remaining <= TimeSpan.Zero ? PropertyGridLiveRefreshInterval : remaining;
            _propertyGridLiveRefreshTimer.Start();
        }
    }

    public void EndPropertyGridLiveGesture()
    {
        _isPropertyGridLiveGesture = false;
        _hasPendingPropertyGridLiveRefresh = false;
        _propertyGridLiveRefreshTimer.Stop();
        _propertyGridLiveRefreshTimer.Interval = PropertyGridLiveRefreshInterval;
        RefreshLivePropertyGridLayoutRows(DateTime.UtcNow);
    }

    private void PropertyGridLiveRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _propertyGridLiveRefreshTimer.Stop();
        _propertyGridLiveRefreshTimer.Interval = PropertyGridLiveRefreshInterval;
        if (!_hasPendingPropertyGridLiveRefresh)
            return;

        _hasPendingPropertyGridLiveRefresh = false;
        RefreshLivePropertyGridLayoutRows(DateTime.UtcNow);
    }

    private static bool IsLiveLayoutProperty(string? propertyName)
    {
        return propertyName is nameof(DesignControlModel.X)
            or nameof(DesignControlModel.Y)
            or nameof(DesignControlModel.Width)
            or nameof(DesignControlModel.Height);
    }

    private void RefreshLivePropertyGridLayoutRows(DateTime? refreshedUtc = null)
    {
        var control = SelectedControl;
        if (control is null)
            return;

        TryRefreshPropertyGridRow(nameof(DesignControlModel.X), control.X.ToString(CultureInfo.InvariantCulture));
        TryRefreshPropertyGridRow(nameof(DesignControlModel.Y), control.Y.ToString(CultureInfo.InvariantCulture));
        TryRefreshPropertyGridRow(nameof(DesignControlModel.Width), control.Width.ToString(CultureInfo.InvariantCulture));
        TryRefreshPropertyGridRow(nameof(DesignControlModel.Height), control.Height.ToString(CultureInfo.InvariantCulture));
        OnPropertyChanged(nameof(PropertyGridSelectionMetrics));
        OnPropertyChanged(nameof(SelectedControlSummary));
        _lastPropertyGridLiveRefreshUtc = refreshedUtc ?? DateTime.UtcNow;
    }

    private bool TryRefreshPropertyGridRow(string key, string value, bool boolValue = false)
    {
        foreach (var category in PropertyGridCategories)
        {
            var row = category.Rows.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (row is null)
                continue;

            row.Refresh(value, boolValue, row.IsFavorite);
            return true;
        }

        return false;
    }

    private void RebuildPropertyGrid()
    {
        if (_isApplyingDocument)
            return;

        _isRebuildingPropertyGrid = true;
        try
        {
            var query = PropertyGridSearchText.Trim();
            var hasSearch = !string.IsNullOrWhiteSpace(query);
            var rows = BuildPropertyGridRows().ToList();
            if (hasSearch)
            {
                rows = rows
                    .Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || row.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            PropertyGridCategories.Clear();

            var favorites = rows.Where(row => row.IsFavorite).ToList();
            if (favorites.Count > 0)
                AddPropertyGridCategory(PropertyGridCategoryFavorites, "\u2605 Favorites", favorites, hasSearch);

            foreach (var category in GetPropertyGridCategoryOrder().Where(category => category != PropertyGridCategoryFavorites))
            {
                var categoryRows = rows.Where(row => string.Equals(row.Category, category, StringComparison.Ordinal)).ToList();
                if (categoryRows.Count > 0)
                    AddPropertyGridCategory(category, category, categoryRows, hasSearch);
            }
        }
        finally
        {
            _isRebuildingPropertyGrid = false;
        }

        RaisePropertyGridProperties();
    }

    private void RaisePropertyGridProperties()
    {
        OnPropertyChanged(nameof(HasPropertyGridRows));
        OnPropertyChanged(nameof(HasNoPropertyGridRows));
        OnPropertyChanged(nameof(PropertyGridSelectionTitle));
        OnPropertyChanged(nameof(PropertyGridSelectionSubtitle));
        OnPropertyChanged(nameof(PropertyGridSelectionMetrics));
        OnPropertyChanged(nameof(PropertyGridEmptyText));
    }

    partial void OnPropertyGridSearchTextChanged(string value)
    {
        RebuildPropertyGrid();
    }

    private void AddPropertyGridCategory(string key, string title, IReadOnlyList<PropertyGridRowViewModel> rows, bool forceExpanded)
    {
        var category = new PropertyGridCategoryViewModel(
            key,
            title,
            forceExpanded || GetPropertyGridExpandedCategories(CurrentPropertyGridTypeKey, create: false).Contains(key),
            SetPropertyGridCategoryExpanded);

        foreach (var row in rows)
            category.Rows.Add(row);

        category.NotifyRowsChanged();
        PropertyGridCategories.Add(category);
    }

    private IEnumerable<PropertyGridRowViewModel> BuildPropertyGridRows()
    {
        if (SelectedControl is null)
        {
            yield return CreateTextRow(PropertyGridCategoryCommon, "FormTitle", "Title", FormTitle, "Window title.", value => FormTitle = value);
            yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignWidth), "Width", DesignWidth, "Form width.", value => DesignWidth = Math.Max(300, value));
            yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignHeight), "Height", DesignHeight, "Form height.", value => DesignHeight = Math.Max(200, value));
            yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(SurfaceBackground), "Background", SurfaceBackground, "Form background color.", value => SurfaceBackground = value);
            yield return CreateEnumRow(PropertyGridCategoryBehavior, nameof(FormWindowState), "WindowState", FormWindowState, AvailableFormWindowStates, "Startup window state.", value => FormWindowState = value);
            yield return CreateEnumRow(PropertyGridCategoryBehavior, nameof(FormStartupLocation), "StartupLocation", FormStartupLocation, AvailableFormStartupLocations, "Startup location.", value => FormStartupLocation = value);
            yield break;
        }

        var control = SelectedControl;
        yield return CreateTextRow(PropertyGridCategoryCommon, nameof(DesignControlModel.Name), "Name", control.Name, "Element name used by export and interactions.", value => control.Name = value);
        if (control.Type == DesignerControlTypes.DataGrid)
            yield return CreateTextRow(PropertyGridCategoryCommon, nameof(DesignControlModel.Text), "Title", control.Text, "Optional DataGrid title shown in designer/export.", value => control.Text = value);
        if (control.Type != DesignerControlTypes.DataGrid && SupportsText(control))
            yield return CreateTextRow(PropertyGridCategoryCommon, nameof(DesignControlModel.Text), "Text / Content", control.Text, "Displayed text or content.", value => control.Text = value);
        if (SupportsPlaceholder(control))
            yield return CreateTextRow(PropertyGridCategoryCommon, nameof(DesignControlModel.PlaceholderText), "Watermark", control.PlaceholderText, "Placeholder text.", value => control.PlaceholderText = value);
        if (SupportsImageSource(control))
            yield return CreateTextRow(PropertyGridCategoryCommon, nameof(DesignControlModel.ImageSource), "ImageSource", control.ImageSource, "Image path or URI.", value => control.ImageSource = value);

        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignControlModel.X), "X", control.X, "Left position on canvas.", value => { control.X = Math.Max(0, value); ClampControlToSurface(control); });
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignControlModel.Y), "Y", control.Y, "Top position on canvas.", value => { control.Y = Math.Max(0, value); ClampControlToSurface(control); });
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignControlModel.Width), "Width", control.Width, "Element width.", value => { control.Width = value; ClampControlToSurface(control); });
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignControlModel.Height), "Height", control.Height, "Element height.", value => { control.Height = value; ClampControlToSurface(control); });
        yield return CreateBoolRow(PropertyGridCategoryLayout, nameof(DesignControlModel.AnchorLeft), "AnchorLeft", control.AnchorLeft, "Anchor to left edge.", value => control.AnchorLeft = value);
        yield return CreateBoolRow(PropertyGridCategoryLayout, nameof(DesignControlModel.AnchorTop), "AnchorTop", control.AnchorTop, "Anchor to top edge.", value => control.AnchorTop = value);
        yield return CreateBoolRow(PropertyGridCategoryLayout, nameof(DesignControlModel.AnchorRight), "AnchorRight", control.AnchorRight, "Anchor to right edge.", value => control.AnchorRight = value);
        yield return CreateBoolRow(PropertyGridCategoryLayout, nameof(DesignControlModel.AnchorBottom), "AnchorBottom", control.AnchorBottom, "Anchor to bottom edge.", value => control.AnchorBottom = value);

        if (CanHostChildren(control))
        {
            yield return CreateEnumRow(PropertyGridCategoryLayout, nameof(DesignControlModel.LayoutOrientation), "Orientation", control.LayoutOrientation, AvailableLayoutOrientations, "Child layout orientation.", value => control.LayoutOrientation = value);
            yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(DesignControlModel.LayoutSpacing), "Spacing", control.LayoutSpacing, "Spacing between child elements.", value => control.LayoutSpacing = Math.Max(0, value));
        }

        yield return CreateBoolRow(PropertyGridCategoryCommon, nameof(DesignControlModel.IsVisible), "IsVisible", control.IsVisible, "Show element on canvas/export.", value => control.IsVisible = value);
        yield return CreateBoolRow(PropertyGridCategoryCommon, nameof(DesignControlModel.IsLocked), "IsLocked", control.IsLocked, "Lock move/resize on canvas.", value => control.IsLocked = value);
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.Opacity), "Opacity", control.Opacity, "Opacity 0..1.", value => control.Opacity = Math.Clamp(value, 0, 1));
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.Background), "Background", control.Background, "Background color.", value => control.Background = value);
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.Foreground), "Foreground", control.Foreground, "Text foreground color.", value => control.Foreground = value);
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.BorderBrush), "BorderBrush", control.BorderBrush, "Border color.", value => control.BorderBrush = value);
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.BorderThickness), "BorderThickness", control.BorderThickness, "Border thickness.", value => control.BorderThickness = Math.Max(0, value));
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.CornerRadius), "CornerRadius", control.CornerRadius, "Corner radius.", value => control.CornerRadius = Math.Max(0, value));
        yield return CreateEnumRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.FontFamily), "FontFamily", control.FontFamily, AvailableFontFamilies, "Font family.", value => control.FontFamily = value);
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.FontSize), "FontSize", control.FontSize, "Font size.", value => control.FontSize = Math.Max(1, value));
        yield return CreateEnumRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.FontWeight), "FontWeight", control.FontWeight, AvailableFontWeights, "Font weight.", value => control.FontWeight = value);

        if (SupportsStretch(control))
            yield return CreateEnumRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.Stretch), "Stretch", control.Stretch, AvailableStretchModes, "Image stretch mode.", value => control.Stretch = value);

        if (control.Type == DesignerControlTypes.DataGrid)
        {
            foreach (var row in BuildDataGridPropertyRows(control))
                yield return row;
        }

        var interactionCount = Interactions.Count(interaction =>
            string.Equals(interaction.SourceControlName, control.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(interaction.TargetControlName, control.Name, StringComparison.OrdinalIgnoreCase));
        yield return CreateReadOnlyRow(PropertyGridCategoryInteraction, "Interactions", "Interactions", interactionCount.ToString(CultureInfo.InvariantCulture), "Rules that use this control.");

        var descriptorProperties = GetCustomDescriptorProperties(control).ToList();
        foreach (var descriptor in descriptorProperties)
            yield return CreateDescriptorPropertyRow(descriptor);

        var descriptorKeys = descriptorProperties.Select(descriptor => descriptor.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var customProperty in control.CustomProperties.Where(property => !descriptorKeys.Contains(property.Key)))
        {
            yield return CreateTextRow(
                PropertyGridCategoryAdvanced,
                customProperty.Key,
                customProperty.Key,
                customProperty.ValueJson,
                "Raw custom property preserved for missing/unknown plugin descriptors.",
                value =>
                {
                    customProperty.ValueJson = value;
                    NotifyDesignerStateChanged();
                },
                isAdvanced: true);
        }
    }

    private IEnumerable<PropertyGridRowViewModel> BuildDataGridPropertyRows(DesignControlModel control)
    {
        yield return CreateBindingSourceRow(control);
        yield return CreateBoolRow(PropertyGridCategoryData, nameof(DesignControlModel.AutoGenerateColumns), "AutoGenerateColumns", control.AutoGenerateColumns, "Generate columns from BindingSource fields.", value => control.AutoGenerateColumns = value);
        yield return CreateActionRow(PropertyGridCategoryData, "Columns", "Columns", SelectedGridColumnCompactSummary, "Open the DataGrid column editor.", "Edit...");
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridHeaderBackground), "HeaderBackground", control.DataGridHeaderBackground, "Header background.", value => control.DataGridHeaderBackground = value);
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridRowBackground), "RowBackground", control.DataGridRowBackground, "Row background.", value => control.DataGridRowBackground = value);
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridAlternateRowBackground), "AlternateRowBackground", control.DataGridAlternateRowBackground, "Alternate row background.", value => control.DataGridAlternateRowBackground = value);
        yield return CreateColorRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridOuterBorderBrush), "BorderBrush", control.DataGridOuterBorderBrush, "DataGrid outer border.", value => control.DataGridOuterBorderBrush = value);
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridRowFontSize), "FontSize", control.DataGridRowFontSize, "Row font size.", value => control.DataGridRowFontSize = Math.Max(1, value));
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridRowHeight), "RowHeight", control.DataGridRowHeight, "Row height.", value => control.DataGridRowHeight = Math.Max(20, value));
        yield return CreateNumberRow(PropertyGridCategoryAppearance, nameof(DesignControlModel.DataGridHeaderHeight), "HeaderHeight", control.DataGridHeaderHeight, "Header height.", value => control.DataGridHeaderHeight = Math.Max(20, value));
        yield return CreateBoolRow(PropertyGridCategoryBehavior, nameof(DesignControlModel.DataGridShowHeader), "ShowHeader", control.DataGridShowHeader, "Show column headers.", value => control.DataGridShowHeader = value);
        yield return CreateBoolRow(PropertyGridCategoryBehavior, nameof(DesignControlModel.ShowFilterRow), "AllowFilter", control.ShowFilterRow, "Show filter row for quick filtering.", value => control.ShowFilterRow = value);
        yield return CreateEnumRow(PropertyGridCategoryBehavior, nameof(DesignControlModel.FilterMode), "FilterMode", control.FilterMode, AvailableDataGridFilterModes, "Filter matching mode.", value => control.FilterMode = value);
        yield return CreateBoolRow(PropertyGridCategoryBehavior, nameof(DesignControlModel.ShowGroupPanel), "GroupPanel", control.ShowGroupPanel, "Show grouping panel.", value => control.ShowGroupPanel = value);
        yield return CreateBoolRow(PropertyGridCategoryBehavior, nameof(DesignControlModel.AllowGrouping), "AllowGrouping", control.AllowGrouping, "Allow grouping fields.", value => control.AllowGrouping = value);
        yield return CreateReadOnlyRow(PropertyGridCategoryBehavior, "AllowSort", "AllowSort", "true", "Sorting is exported per generated column.");
        yield return CreateBoolRow(PropertyGridCategoryBehavior, nameof(DesignControlModel.ShowFooter), "FooterSummaryRow", control.ShowFooter, "Show footer summary row.", value => control.ShowFooter = value);
        yield return CreateEnumRow(PropertyGridCategoryExport, nameof(DataGridExportMode), "DataGridExportMode", DataGridExportMode, AvailableDataGridExportModes, "Export mode used for DataGrid controls.", value => DataGridExportMode = value);
        yield return CreateReadOnlyRow(PropertyGridCategoryExport, "RuntimeNuGetRequired", "RuntimeNuGetRequired", ShouldExportRealDataGrid ? "Avalonia.Controls.DataGrid" : "none", "Required NuGet for the current DataGrid export mode.");
        yield return CreateColorRow(PropertyGridCategoryAdvanced, nameof(DesignControlModel.DataGridGridLineBrush), "GridLineBrush", control.DataGridGridLineBrush, "Grid line color.", value => control.DataGridGridLineBrush = value, isAdvanced: true);
        yield return CreateEnumRow(PropertyGridCategoryAdvanced, nameof(DesignControlModel.DataGridTextAlignment), "TextAlignment", control.DataGridTextAlignment, AvailableDataGridTextAlignments, "Cell text alignment.", value => control.DataGridTextAlignment = value, isAdvanced: true);
        yield return CreateNumberRow(PropertyGridCategoryAdvanced, nameof(DesignControlModel.DataGridCellPadding), "CellPadding", control.DataGridCellPadding, "Cell padding.", value => control.DataGridCellPadding = Math.Max(0, value), isAdvanced: true);
    }

    private PropertyGridRowViewModel CreateDescriptorPropertyRow(DesignPropertyDescriptor descriptor)
    {
        var category = string.IsNullOrWhiteSpace(descriptor.Category) ? PropertyGridCategoryAdvanced : NormalizePropertyGridCategory(descriptor.Category);
        var value = GetDescriptorCustomPropertyString(descriptor);
        var description = GetPropertyGridDescriptorDescription(descriptor);
        return descriptor.Editor switch
        {
            PropertyEditorKind.Bool => CreateBoolRow(category, descriptor.Key, descriptor.Title, GetDescriptorCustomPropertyBool(descriptor), description, boolValue => SetDescriptorCustomPropertyFromBool(descriptor, boolValue), isAdvanced: true),
            PropertyEditorKind.Color => CreateColorRow(category, descriptor.Key, descriptor.Title, value, description, color => SetDescriptorCustomPropertyFromString(descriptor, color), isAdvanced: true),
            PropertyEditorKind.Enum => CreateDescriptorEnumRow(category, descriptor, value),
            PropertyEditorKind.Number => CreateNumberRow(category, descriptor.Key, descriptor.Title, ParsePropertyGridNumber(value, 0), description, number => SetDescriptorCustomPropertyFromString(descriptor, number.ToString(CultureInfo.InvariantCulture)), isAdvanced: true),
            _ => CreateTextRow(category, descriptor.Key, descriptor.Title, value, description, text => SetDescriptorCustomPropertyFromString(descriptor, text), isAdvanced: true)
        };
    }

    private PropertyGridRowViewModel CreateDescriptorEnumRow(string category, DesignPropertyDescriptor descriptor, string value)
    {
        var row = CreateRow(category, descriptor.Key, descriptor.Title, PropertyGridEditorKind.Enum, value, GetPropertyGridDescriptorDescription(descriptor), (item, optionValue) => SetDescriptorCustomPropertyFromString(descriptor, optionValue), null, isAdvanced: true);
        row.SetOptions(descriptor.Options.Select(option => new PropertyGridOptionViewModel(option.Value, option.Title)));
        return row;
    }

    private static string GetPropertyGridDescriptorDescription(DesignPropertyDescriptor descriptor)
    {
        var editorHint = descriptor.Editor switch
        {
            PropertyEditorKind.Color => "HEX color value from plugin descriptor.",
            PropertyEditorKind.Number => "Numeric value from plugin descriptor.",
            PropertyEditorKind.Bool => "Boolean value from plugin descriptor.",
            PropertyEditorKind.Enum => "Plugin descriptor enum value.",
            PropertyEditorKind.Binding => "Binding-compatible plugin property.",
            PropertyEditorKind.Collection => "Serialized collection/custom plugin value.",
            _ => "Custom plugin property."
        };

        return descriptor.IsBindable ? $"{editorHint} Supports binding." : editorHint;
    }

    private static string NormalizePropertyGridCategory(string category)
    {
        return category.Trim().ToLowerInvariant() switch
        {
            "common" => PropertyGridCategoryCommon,
            "layout" => PropertyGridCategoryLayout,
            "appearance" => PropertyGridCategoryAppearance,
            "data" => PropertyGridCategoryData,
            "behavior" => PropertyGridCategoryBehavior,
            "interaction" => PropertyGridCategoryInteraction,
            "export" => PropertyGridCategoryExport,
            _ => PropertyGridCategoryAdvanced
        };
    }

    private static double ParsePropertyGridNumber(string value, double fallback)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant)
            || double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out invariant))
        {
            return invariant;
        }

        return fallback;
    }

    private PropertyGridRowViewModel CreateTextRow(string category, string key, string label, string value, string description, Action<string> apply, bool isAdvanced = false)
    {
        return CreateRow(category, key, label, PropertyGridEditorKind.Text, value, description, (_, newValue) => apply(newValue), null, isAdvanced);
    }

    private PropertyGridRowViewModel CreateNumberRow(string category, string key, string label, double value, string description, Action<double> apply, bool isAdvanced = false)
    {
        return CreateRow(category, key, label, PropertyGridEditorKind.Number, value.ToString(CultureInfo.InvariantCulture), description, (row, newValue) =>
        {
            if (double.TryParse(newValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant)
                || double.TryParse(newValue, NumberStyles.Any, CultureInfo.CurrentCulture, out invariant))
            {
                row.ValidationMessage = "";
                apply(invariant);
                return;
            }

            row.ValidationMessage = "Invalid number";
        }, null, isAdvanced);
    }

    private PropertyGridRowViewModel CreateBoolRow(string category, string key, string label, bool value, string description, Action<bool> apply, bool isAdvanced = false)
    {
        return CreateRow(category, key, label, PropertyGridEditorKind.Bool, value ? "True" : "False", description, null, (_, boolValue) => apply(boolValue), isAdvanced, boolValue: value);
    }

    private PropertyGridRowViewModel CreateEnumRow(string category, string key, string label, string value, IEnumerable<string> options, string description, Action<string> apply, bool isAdvanced = false)
    {
        var row = CreateRow(category, key, label, PropertyGridEditorKind.Enum, value, description, (_, newValue) => apply(newValue), null, isAdvanced);
        row.SetOptions(options.Select(option => new PropertyGridOptionViewModel(option, option)));
        return row;
    }

    private PropertyGridRowViewModel CreateColorRow(string category, string key, string label, string value, string description, Action<string> apply, bool isAdvanced = false)
    {
        return CreateRow(category, key, label, PropertyGridEditorKind.Color, value, description, (_, newValue) => apply(newValue), null, isAdvanced);
    }

    private PropertyGridRowViewModel CreateReadOnlyRow(string category, string key, string label, string value, string description)
    {
        return CreateRow(category, key, label, PropertyGridEditorKind.ReadOnly, value, description, null, null, isReadOnly: true);
    }

    private PropertyGridRowViewModel CreateActionRow(string category, string key, string label, string value, string description, string actionText)
    {
        return CreateRow(category, key, label, PropertyGridEditorKind.Action, value, description, null, null, actionText: actionText);
    }

    private PropertyGridRowViewModel CreateBindingSourceRow(DesignControlModel control)
    {
        var row = CreateRow(PropertyGridCategoryData, nameof(DesignControlModel.BindingSourceId), "BindingSource", PropertyGridEditorKind.BindingSource, control.BindingSourceId, "BindingSource used by this DataGrid.", (_, value) =>
        {
            control.BindingSourceId = value;
            RaiseBindingEditorProperties();
        }, null, isAdvanced: false);
        row.SetOptions(new[] { new PropertyGridOptionViewModel("", "(none)") }
            .Concat(BindingSources.Select(source => new PropertyGridOptionViewModel(source.Id, source.Name))));
        return row;
    }

    private PropertyGridRowViewModel CreateRow(
        string category,
        string key,
        string label,
        PropertyGridEditorKind editor,
        string value,
        string description,
        Action<PropertyGridRowViewModel, string>? applyValue,
        Action<PropertyGridRowViewModel, bool>? applyBool,
        bool isAdvanced = false,
        bool isReadOnly = false,
        bool boolValue = false,
        string actionText = "Edit...")
    {
        return new PropertyGridRowViewModel(
            key,
            label,
            category,
            editor,
            value,
            description,
            (row, newValue) =>
            {
                applyValue?.Invoke(row, newValue);
                if (row.HasValidationError)
                    return;

                RefreshFromPropertyGridEdit();
            },
            (row, newValue) =>
            {
                applyBool?.Invoke(row, newValue);
                RefreshFromPropertyGridEdit();
            },
            boolValue,
            IsPropertyGridFavorite(key),
            isAdvanced,
            isReadOnly,
            actionText);
    }

    private void RefreshFromPropertyGridEdit()
    {
        RefreshDescriptorCustomPropertyEditors();
        RaiseSelectionProperties();
        RaiseBindingEditorProperties();
        RaiseGenerationOptionsProperties();
        RebuildPropertyGrid();
    }

    public void AddOrUpdateRecentFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var normalizedPath = Path.GetFullPath(filePath);
        var existing = RecentFiles.FirstOrDefault(item => string.Equals(item.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            RecentFiles.Remove(existing);

        RecentFiles.Insert(0, new RecentFileModel
        {
            FilePath = normalizedPath,
            DisplayName = Path.GetFileName(normalizedPath),
            LastOpenedUtc = DateTime.UtcNow
        });

        while (RecentFiles.Count > 10)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    public void RemoveRecentFile(RecentFileModel? file)
    {
        if (file is null)
            return;

        RecentFiles.Remove(file);
    }

    public bool TrySelectControlById(string? controlId)
    {
        if (string.IsNullOrWhiteSpace(controlId))
            return false;

        var control = GetControl(controlId);
        if (control is null)
            return false;

        SelectSingleControl(control);
        return true;
    }

    private void ApplyExportCache(ExportCacheModel? cache)
    {
        if (cache is null)
            return;

        ExportTarget = AvailableExportTargets.Contains(cache.ExportTarget) ? cache.ExportTarget : ExportTargetMainWindow;
        ExportProjectNamespace = string.IsNullOrWhiteSpace(cache.ExportProjectNamespace) ? "AvaloniaApplication1" : cache.ExportProjectNamespace;
        DataGridExportMode = NormalizeDataGridExportMode(cache.DataGridExportMode);
        LayoutExportMode = NormalizeLayoutExportMode(cache.LayoutExportMode);
        XamlVerbosity = AvailableXamlVerbosities.Contains(cache.XamlVerbosity) ? cache.XamlVerbosity : XamlVerbosityCompact;
        IncludeExportComments = cache.IncludeExportComments;
        IncludeSampleData = cache.IncludeSampleData;
        IncludeCrudSkeleton = cache.IncludeCrudSkeleton;
        IncludeCommunityToolkitAttributes = cache.IncludeCommunityToolkitAttributes;
        IncludePluginRuntimeReferences = cache.IncludePluginRuntimeReferences;

        GeneratedXaml = cache.GeneratedXaml;
        GeneratedCSharp = cache.GeneratedCSharp;
        GeneratedBindingGuide = cache.GeneratedBindingGuide;
        _exportCacheDocumentSnapshotHash = cache.DocumentSnapshotHash;
        _exportCacheSettingsSignature = cache.SettingsSignature;
        _exportCacheGeneratedUtc = cache.GeneratedUtc;
        RaiseExportCacheProperties();
    }

    public void BeginBusy(string title, string description)
    {
        BusyTitle = string.IsNullOrWhiteSpace(title) ? "Загрузка" : title.Trim();
        BusyDescription = string.IsNullOrWhiteSpace(description)
            ? "Подождите, выполняется операция."
            : description.Trim();
        IsBusy = true;
    }

    public void EndBusy(string? statusText = null)
    {
        IsBusy = false;
        if (!string.IsNullOrWhiteSpace(statusText))
            StatusText = statusText.Trim();
    }

    [RelayCommand]
    private void ApplySuggestedBindings()
    {
        var context = GetPreferredCrudContext();
        if (context is null)
        {
            StatusText = "Сначала добавьте BindingSource и привяжите хотя бы один DataGrid.";
            return;
        }

        var updatedControls = 0;
        var mappedGridCount = 0;
        var mappedTextBoxCount = 0;
        var mappedButtonCount = 0;

        var targetGrids = GetControlsForSuggestedBindings(DesignerControlTypes.DataGrid)
            .Where(grid => string.IsNullOrWhiteSpace(grid.BindingSourceId)
                || string.Equals(grid.BindingSourceId, context.Source.Id, StringComparison.OrdinalIgnoreCase)
                || IsControlSelected(grid))
            .ToList();

        foreach (var grid in targetGrids)
        {
            if (string.Equals(grid.BindingSourceId, context.Source.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            grid.BindingSourceId = context.Source.Id;
            updatedControls++;
            mappedGridCount++;
        }

        var textBoxes = GetControlsForSuggestedBindings(DesignerControlTypes.TextBox);
        if (textBoxes.Count > 0)
        {
            if (!string.Equals(textBoxes[0].TextBindingPath, context.SearchTextPropertyName, StringComparison.Ordinal))
            {
                textBoxes[0].TextBindingPath = context.SearchTextPropertyName;
                updatedControls++;
            }

            if (string.IsNullOrWhiteSpace(textBoxes[0].PlaceholderText))
            {
                textBoxes[0].PlaceholderText = "Поиск...";
                updatedControls++;
            }

            mappedTextBoxCount++;

            var editableFields = context.Fields.Where(field => field.IsVisible).ToList();
            if (editableFields.Count == 0)
                editableFields = context.Fields.ToList();

            var editableCount = Math.Min(editableFields.Count, textBoxes.Count - 1);
            for (var index = 0; index < editableCount; index++)
            {
                var textBox = textBoxes[index + 1];
                var field = editableFields[index];
                var property = SanitizeIdentifier(field.Path, $"Field{index + 1}");
                var bindingPath = $"{context.CurrentItemPropertyName}.{property}";

                if (!string.Equals(textBox.TextBindingPath, bindingPath, StringComparison.Ordinal))
                {
                    textBox.TextBindingPath = bindingPath;
                    updatedControls++;
                }

                if (string.IsNullOrWhiteSpace(textBox.PlaceholderText))
                {
                    textBox.PlaceholderText = field.Header;
                    updatedControls++;
                }

                mappedTextBoxCount++;
            }
        }

        foreach (var button in GetControlsForSuggestedBindings(DesignerControlTypes.Button))
        {
            var action = DetectButtonAction(button);
            if (action == GeneratedButtonAction.None)
                continue;

            var actionKey = action.ToString();
            if (string.Equals(button.GeneratedButtonActionKey, actionKey, StringComparison.Ordinal))
                continue;

            button.GeneratedButtonActionKey = actionKey;
            updatedControls++;
            mappedButtonCount++;
        }

        SelectedBindingSource = context.Source;

        if (updatedControls == 0)
        {
            StatusText = "Рекомендуемые привязки уже были применены.";
            MarkExportCacheStale();
            return;
        }

        StatusText = $"Привязки применены: DataGrid {Math.Max(mappedGridCount, targetGrids.Count)}, TextBox {mappedTextBoxCount}, кнопки {mappedButtonCount}.";
        NotifyDesignerStateChanged();
    }

    [RelayCommand]
    private void AddInteractionForSelectedDataGrid()
    {
        if (SelectedControl?.Type != DesignerControlTypes.DataGrid)
        {
            StatusText = "Выберите DataGrid, чтобы добавить действие выбора строки.";
            return;
        }

        var bindingSource = GetBindingSource(SelectedControl.BindingSourceId);
        if (bindingSource is null || bindingSource.Fields.Count == 0)
        {
            StatusText = "Для логики DataGrid сначала подключите BindingSource с реальными полями.";
            return;
        }

        var target = Controls
            .Where(IsSupportedInteractionTarget)
            .FirstOrDefault(control => !string.Equals(control.Id, SelectedControl.Id, StringComparison.OrdinalIgnoreCase));
        var sourcePath = InteractionSourceFieldPaths.FirstOrDefault() ?? "";

        var interaction = new InteractionModel
        {
            SourceControlName = SelectedControl.Name,
            EventName = InteractionModel.EventDataGridSelectionChanged,
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = target?.Name ?? "",
            TargetProperty = GetDefaultInteractionTargetProperty(target),
            SourcePath = sourcePath
        };

        Interactions.Add(interaction);
        SelectedInteraction = interaction;
        StatusText = $"Добавлено действие логики для {SelectedControl.Name}.";
    }

    [RelayCommand]
    private void AddInteraction()
    {
        var source = SelectedControl is not null && IsSupportedInteractionSource(SelectedControl)
            ? SelectedControl
            : Controls.FirstOrDefault(IsSupportedInteractionSource);
        var target = Controls
            .Where(IsSupportedInteractionTarget)
            .FirstOrDefault(control => source is null || !string.Equals(control.Id, source.Id, StringComparison.OrdinalIgnoreCase));

        var interaction = new InteractionModel
        {
            SourceControlName = source?.Name ?? "",
            EventName = GetDefaultInteractionEvent(source),
            ActionType = InteractionModel.ActionSetProperty,
            TargetControlName = target?.Name ?? "",
            TargetProperty = GetDefaultInteractionTargetProperty(target),
            SourcePath = GetDefaultInteractionSourcePath(source)
        };

        Interactions.Add(interaction);
        SelectedInteraction = interaction;
        WorkspaceMode = WorkspaceModeLogic;
        StatusText = "Добавлено правило логики формы.";
    }

    [RelayCommand]
    private void SelectInteraction(InteractionModel? interaction)
    {
        if (interaction is null)
            return;

        SelectedInteraction = interaction;
        var source = FindControlByName(interaction.SourceControlName);
        if (source is not null)
            SelectSingleControl(source);
    }

    [RelayCommand]
    private void SelectInteractionSource(InteractionModel? interaction)
    {
        if (interaction is null)
            return;

        SelectedInteraction = interaction;
        var source = FindControlByName(interaction.SourceControlName);
        if (source is not null)
            SelectSingleControl(source);
    }

    [RelayCommand]
    private void SelectInteractionTarget(InteractionModel? interaction)
    {
        if (interaction is null)
            return;

        SelectedInteraction = interaction;
        var target = FindControlByName(interaction.TargetControlName);
        if (target is not null)
            SelectSingleControl(target);
    }

    [RelayCommand]
    private void RemoveInteraction(InteractionModel? interaction)
    {
        if (interaction is null)
            return;

        Interactions.Remove(interaction);
        if (SelectedInteraction == interaction)
            SelectedInteraction = null;
        StatusText = "Действие логики удалено.";
    }

    [RelayCommand]
    private void NewDocument()
    {
        CreateNewDocumentCore(markAsSaved: true);
        StatusText = "Создан новый документ";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var roots = GetSelectedRootControls().ToList();
        if (roots.Count == 0)
            return;

        BeginUndoBatch();
        try
        {
            var toRemove = roots.SelectMany(GetControlAndDescendants).DistinctBy(control => control.Id).ToList();
            var removedNames = toRemove
                .Select(control => control.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var control in toRemove)
                Controls.Remove(control);

            RemoveInteractionsReferencingControls(removedNames);
            ClearSelection();
            NotifyDesignerStateChanged();
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    [RelayCommand]
    private void LockSelected()
    {
        SetLockStateForSelection(true);
    }

    [RelayCommand]
    private void UnlockSelected()
    {
        SetLockStateForSelection(false);
    }

    [RelayCommand]
    private void ToggleLockSelected()
    {
        var selected = GetSelectedControls();
        if (selected.Count == 0)
            return;

        var shouldLock = selected.Any(control => !control.IsLocked);
        SetLockStateForSelection(shouldLock);
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        var selectedRoots = GetSelectedRootControls().ToList();
        if (selectedRoots.Count == 0)
            return;

        BeginUndoBatch();
        try
        {
            var originals = selectedRoots.SelectMany(GetControlAndDescendants).DistinctBy(control => control.Id).ToList();
            var clonesByOriginalId = new Dictionary<string, DesignControlModel>(StringComparer.OrdinalIgnoreCase);
            var newlyCreatedRoots = new List<DesignControlModel>();

            foreach (var original in originals)
            {
                var clone = original.Clone();
                clone.Id = Guid.NewGuid().ToString("N");
                clone.Name = GetUniqueControlName(clone.Type);
                clonesByOriginalId[original.Id] = clone;
            }

            foreach (var original in originals)
            {
                var clone = clonesByOriginalId[original.Id];
                clone.ParentId = clonesByOriginalId.TryGetValue(NormalizeId(original.ParentId), out var parentClone)
                    ? parentClone.Id
                    : NormalizeId(original.ParentId);

                if (selectedRoots.Any(root => root.Id == original.Id))
                {
                    clone.X += Math.Max(10, SnapStep);
                    clone.Y += Math.Max(10, SnapStep);
                    ClampControlToSurface(clone);
                    newlyCreatedRoots.Add(clone);
                }

                Controls.Add(clone);
            }

            var clonedNameMap = originals
                .Where(original => !string.IsNullOrWhiteSpace(original.Name))
                .Where(original => clonesByOriginalId.ContainsKey(original.Id))
                .ToDictionary(original => original.Name, original => clonesByOriginalId[original.Id].Name, StringComparer.OrdinalIgnoreCase);
            CloneInteractionsForNameMap(clonedNameMap);

            SetSelection(newlyCreatedRoots, newlyCreatedRoots.LastOrDefault());
            NotifyDesignerStateChanged();
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    [RelayCommand]
    private void InsertTemplate(ReusableTemplateModel? template)
    {
        if (template is null || template.Controls.Count == 0)
            return;

        var controlFiles = template.Controls
            .Where(control => !string.IsNullOrWhiteSpace(control.Type))
            .ToList();
        if (controlFiles.Count == 0)
            return;

        var sourceIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var controlsByOriginalId = new Dictionary<string, DesignControlModel>(StringComparer.OrdinalIgnoreCase);
        var controlNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rootIds = controlFiles
            .Where(control => string.IsNullOrWhiteSpace(control.ParentId) || controlFiles.All(candidate => !string.Equals(candidate.Id, control.ParentId, StringComparison.OrdinalIgnoreCase)))
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var insertX = Snap(40 + (_templateInsertionOffset % 8) * 24);
        var insertY = Snap(40 + (_templateInsertionOffset % 8) * 24);
        _templateInsertionOffset++;

        BeginUndoBatch();
        try
        {
        _isHistorySuspended = true;
        _isStructureTreeRefreshSuspended = true;

        try
        {
            foreach (var sourceFile in template.BindingSources)
            {
                var originalId = NormalizeId(sourceFile.Id);
                var source = FromBindingSourceFileModel(sourceFile);
                source.Id = Guid.NewGuid().ToString("N");

                var requestedName = string.IsNullOrWhiteSpace(source.Name) ? "TemplateSource" : source.Name;
                var uniqueName = GetUniqueBindingSourceName(requestedName);
                if (string.IsNullOrWhiteSpace(source.Path) || string.Equals(source.Path, source.Name, StringComparison.OrdinalIgnoreCase))
                    source.Path = uniqueName;
                source.Name = uniqueName;

                if (!string.IsNullOrWhiteSpace(originalId))
                    sourceIdMap[originalId] = source.Id;

                BindingSources.Add(source);
            }

            foreach (var controlFile in controlFiles)
            {
                var control = FromControlFileModel(controlFile);
                control.Id = Guid.NewGuid().ToString("N");
                control.Name = GetUniqueControlName(string.IsNullOrWhiteSpace(control.Name) ? control.Type : control.Name);
                if (!string.IsNullOrWhiteSpace(controlFile.Name))
                    controlNameMap[controlFile.Name] = control.Name;

                var bindingSourceId = NormalizeId(control.BindingSourceId);
                if (!string.IsNullOrWhiteSpace(bindingSourceId) && sourceIdMap.TryGetValue(bindingSourceId, out var newBindingSourceId))
                    control.BindingSourceId = newBindingSourceId;

                controlsByOriginalId[controlFile.Id] = control;
            }

            foreach (var controlFile in controlFiles)
            {
                var control = controlsByOriginalId[controlFile.Id];
                control.ParentId = controlsByOriginalId.TryGetValue(NormalizeId(controlFile.ParentId), out var newParent)
                    ? newParent.Id
                    : "";

                if (rootIds.Contains(controlFile.Id) && IsAbsoluteLayoutParent(control.ParentId))
                {
                    control.X += insertX;
                    control.Y += insertY;
                    ClampControlToSurface(control);
                }

                Controls.Add(control);
            }

            CloneInteractionsForNameMap(controlNameMap, template.Interactions);
        }
        finally
        {
            _isStructureTreeRefreshSuspended = false;
            _isHistorySuspended = false;
        }

        var insertedRoots = rootIds
            .Select(id => controlsByOriginalId.TryGetValue(id, out var control) ? control : null)
            .Where(control => control is not null)
            .Cast<DesignControlModel>()
            .ToList();

        RebuildStructureTree();
        SetSelection(insertedRoots, insertedRoots.LastOrDefault());
        NotifyDesignerStateChanged();
        StatusText = insertedRoots.Count == 1
            ? $"Шаблон «{template.Name}» вставлен как {insertedRoots[0].Name}."
            : $"Шаблон «{template.Name}» вставлен: элементов {controlFiles.Count}.";
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    [RelayCommand]
    private void SaveSelectionAsTemplate()
    {
        var roots = GetSelectedRootControls().ToList();
        if (roots.Count == 0)
            return;

        var selectedTree = roots
            .SelectMany(GetControlAndDescendants)
            .DistinctBy(control => control.Id)
            .ToList();
        var selectedIds = selectedTree
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedNames = selectedTree
            .Select(control => control.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bindingSourceIds = selectedTree
            .Where(control => !string.IsNullOrWhiteSpace(control.BindingSourceId))
            .Select(control => control.BindingSourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var minX = roots.Min(control => control.X);
        var minY = roots.Min(control => control.Y);
        var maxRight = roots.Max(control => control.X + control.Width);
        var maxBottom = roots.Max(control => control.Y + control.Height);

        var controlFiles = selectedTree
            .Select(ToControlFileModel)
            .ToList();

        foreach (var controlFile in controlFiles)
        {
            var parentId = NormalizeId(controlFile.ParentId);
            if (string.IsNullOrWhiteSpace(parentId) || !selectedIds.Contains(parentId))
            {
                controlFile.ParentId = "";
                controlFile.X -= minX;
                controlFile.Y -= minY;
            }
        }

        var template = new ReusableTemplateModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = GetUniqueTemplateName("Пользовательский шаблон"),
            Category = "Пользовательские",
            Description = roots.Count == 1
                ? $"Шаблон из элемента {roots[0].Name}."
                : $"Шаблон из выделения: {roots.Count} корневых элементов.",
            IsBuiltIn = false,
            Width = Math.Max(40, maxRight - minX),
            Height = Math.Max(24, maxBottom - minY),
            CreatedUtc = DateTime.UtcNow,
            Controls = controlFiles,
            BindingSources = BindingSources
                .Where(source => bindingSourceIds.Contains(source.Id))
                .Select(ToBindingSourceFileModel)
                .ToList(),
            Interactions = Interactions
                .Where(interaction =>
                    selectedNames.Contains(interaction.SourceControlName)
                    && (string.Equals(interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase)
                        || selectedNames.Contains(interaction.TargetControlName)))
                .Select(ToInteractionFileModel)
                .ToList()
        };

        ReusableTemplates.Add(template);
        SaveCustomReusableTemplates();
        StatusText = $"Сохранен пользовательский шаблон «{template.Name}».";
    }

    [RelayCommand]
    private void RenameTemplate(ReusableTemplateModel? template)
    {
        if (template is null || template.IsBuiltIn)
            return;

        template.Name = GetUniqueTemplateName(
            string.IsNullOrWhiteSpace(template.Name) ? "Пользовательский шаблон" : template.Name.Trim(),
            template.Id);
        SaveCustomReusableTemplates();
        StatusText = $"Шаблон переименован: «{template.Name}».";
    }

    [RelayCommand]
    private void DeleteTemplate(ReusableTemplateModel? template)
    {
        if (template is null || template.IsBuiltIn)
            return;

        var name = template.Name;
        ReusableTemplates.Remove(template);
        SaveCustomReusableTemplates();
        StatusText = $"Пользовательский шаблон «{name}» удален.";
    }

    [RelayCommand]
    private void GroupSelection()
    {
        var selectedRoots = GetVisibleEditableSelectedRootControls().ToList();
        if (selectedRoots.Count < 2)
            return;

        var commonParentId = NormalizeId(selectedRoots[0].ParentId);
        if (selectedRoots.Any(control => NormalizeId(control.ParentId) != commonParentId))
        {
            StatusText = "Для группировки выберите элементы из одного контейнера.";
            return;
        }

        BeginUndoBatch();
        try
        {
        var sourceBefore = Controls.ToList();
        var siblingOrderBefore = sourceBefore
            .Where(control => NormalizeId(control.ParentId) == commonParentId)
            .ToList();
        var selectedIds = selectedRoots
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderedChildren = siblingOrderBefore
            .Where(control => selectedIds.Contains(control.Id))
            .ToList();

        var left = selectedRoots.Min(control => control.X);
        var top = selectedRoots.Min(control => control.Y);
        var right = selectedRoots.Max(control => control.X + control.Width);
        var bottom = selectedRoots.Max(control => control.Y + control.Height);

        var group = CreateDefaultControl(DesignerControlTypes.Group);
        group.Id = Guid.NewGuid().ToString("N");
        group.Name = GetUniqueControlName(DesignerControlTypes.Group);
        group.ParentId = commonParentId;
        group.X = left;
        group.Y = top;
        group.Width = Math.Max(40, right - left);
        group.Height = Math.Max(24, bottom - top);

        Controls.Add(group);

        foreach (var child in orderedChildren)
        {
            child.ParentId = group.Id;
            child.X -= left;
            child.Y -= top;
            ClampControlToSurface(child);
        }

        var orderedSiblings = new List<DesignControlModel>();
        var groupInserted = false;

        foreach (var sibling in siblingOrderBefore)
        {
            if (selectedIds.Contains(sibling.Id))
            {
                if (!groupInserted)
                {
                    orderedSiblings.Add(group);
                    groupInserted = true;
                }

                continue;
            }

            orderedSiblings.Add(sibling);
        }

        RebuildControlTree(new Dictionary<string, List<DesignControlModel>>(StringComparer.OrdinalIgnoreCase)
        {
            [commonParentId] = orderedSiblings,
            [group.Id] = orderedChildren
        });

        SetSelection(new[] { group }, group);
        NotifyDesignerStateChanged();
        StatusText = $"Создана группа {group.Name}: элементов {orderedChildren.Count}.";
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    [RelayCommand]
    private void UngroupSelection()
    {
        var selectedGroups = GetVisibleEditableSelectedRootControls()
            .Where(control => control.Type == DesignerControlTypes.Group)
            .ToList();

        if (selectedGroups.Count == 0)
            return;

        BeginUndoBatch();
        try
        {
        var sourceBefore = Controls.ToList();
        var releasedControls = new List<DesignControlModel>();
        var customOrderByParent = new Dictionary<string, List<DesignControlModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var groupSet in selectedGroups.GroupBy(group => NormalizeId(group.ParentId), StringComparer.OrdinalIgnoreCase))
        {
            var parentId = groupSet.Key;
            var siblingsBefore = sourceBefore
                .Where(control => NormalizeId(control.ParentId) == parentId)
                .ToList();
            var groups = groupSet.ToList();
            var groupIds = groups
                .Select(group => group.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var childrenByGroupId = groups.ToDictionary(
                group => group.Id,
                group => sourceBefore.Where(control => NormalizeId(control.ParentId) == group.Id).ToList(),
                StringComparer.OrdinalIgnoreCase);

            var orderedSiblings = new List<DesignControlModel>();

            foreach (var sibling in siblingsBefore)
            {
                if (groupIds.Contains(sibling.Id))
                {
                    orderedSiblings.AddRange(childrenByGroupId[sibling.Id]);
                    continue;
                }

                orderedSiblings.Add(sibling);
            }

            customOrderByParent[parentId] = orderedSiblings;

            foreach (var group in groups)
            {
                foreach (var child in childrenByGroupId[group.Id])
                {
                    child.ParentId = parentId;
                    child.X += group.X;
                    child.Y += group.Y;
                    ClampControlToSurface(child);
                    releasedControls.Add(child);
                }
            }
        }

        foreach (var group in selectedGroups)
            Controls.Remove(group);

        RebuildControlTree(customOrderByParent);

        var releasedRoots = releasedControls
            .DistinctBy(control => control.Id)
            .ToList();

        SetSelection(releasedRoots, releasedRoots.LastOrDefault());
        NotifyDesignerStateChanged();
        StatusText = $"Групп снято: {selectedGroups.Count}.";
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    public bool CanWrapSelectionInContainer()
    {
        var selectedRoots = GetVisibleEditableSelectedRootControls().ToList();
        if (selectedRoots.Count == 0)
            return false;

        var commonParentId = NormalizeId(selectedRoots[0].ParentId);
        return selectedRoots.All(control => NormalizeId(control.ParentId) == commonParentId);
    }

    public void WrapSelectionInContainer()
    {
        var selectedRoots = GetVisibleEditableSelectedRootControls().ToList();
        if (selectedRoots.Count == 0)
            return;

        var commonParentId = NormalizeId(selectedRoots[0].ParentId);
        if (selectedRoots.Any(control => NormalizeId(control.ParentId) != commonParentId))
        {
            StatusText = "Для обертки в контейнер выберите элементы из одного контейнера.";
            return;
        }

        BeginUndoBatch();
        try
        {
        var sourceBefore = Controls.ToList();
        var siblingOrderBefore = sourceBefore
            .Where(control => NormalizeId(control.ParentId) == commonParentId)
            .ToList();
        var selectedIds = selectedRoots
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderedChildren = siblingOrderBefore
            .Where(control => selectedIds.Contains(control.Id))
            .ToList();

        var padding = 12d;
        var left = selectedRoots.Min(control => control.X);
        var top = selectedRoots.Min(control => control.Y);
        var right = selectedRoots.Max(control => control.X + control.Width);
        var bottom = selectedRoots.Max(control => control.Y + control.Height);

        var container = CreateDefaultControl(DesignerControlTypes.Border);
        container.Id = Guid.NewGuid().ToString("N");
        container.Name = GetUniqueControlName(DesignerControlTypes.Border);
        container.ParentId = commonParentId;
        container.Background = "Transparent";
        container.BorderBrush = "#CBD5E1";
        container.BorderThickness = 1;
        container.CornerRadius = 12;
        container.Padding = padding;
        container.X = Math.Max(0, left - padding);
        container.Y = Math.Max(0, top - padding);
        container.Width = Math.Max(80, (right - container.X) + padding);
        container.Height = Math.Max(48, (bottom - container.Y) + padding);

        Controls.Add(container);

        foreach (var child in orderedChildren)
        {
            child.ParentId = container.Id;
            child.X -= container.X;
            child.Y -= container.Y;
            ClampControlToSurface(child);
        }

        var orderedSiblings = new List<DesignControlModel>();
        var containerInserted = false;

        foreach (var sibling in siblingOrderBefore)
        {
            if (selectedIds.Contains(sibling.Id))
            {
                if (!containerInserted)
                {
                    orderedSiblings.Add(container);
                    containerInserted = true;
                }

                continue;
            }

            orderedSiblings.Add(sibling);
        }

        RebuildControlTree(new Dictionary<string, List<DesignControlModel>>(StringComparer.OrdinalIgnoreCase)
        {
            [commonParentId] = orderedSiblings,
            [container.Id] = orderedChildren
        });

        SetSelection(new[] { container }, container);
        NotifyDesignerStateChanged();
        StatusText = $"Элементы обернуты в контейнер {container.Name}.";
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    [RelayCommand]
    private void CopySelection()
    {
        var roots = GetSelectedRootControls().ToList();
        if (roots.Count == 0)
            return;

        var selectedTree = roots.SelectMany(GetControlAndDescendants).DistinctBy(control => control.Id).ToList();
        var bindingSourceIds = selectedTree
            .Where(control => !string.IsNullOrWhiteSpace(control.BindingSourceId))
            .Select(control => control.BindingSourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedNames = selectedTree
            .Select(control => control.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _clipboardDocument = new DesignerDocumentFileModel
        {
            Controls = selectedTree.Select(ToControlFileModel).ToList(),
            BindingSources = BindingSources
                .Where(source => bindingSourceIds.Contains(source.Id))
                .Select(ToBindingSourceFileModel)
                .ToList(),
            Interactions = Interactions
                .Where(interaction => selectedNames.Contains(interaction.SourceControlName)
                    && selectedNames.Contains(interaction.TargetControlName))
                .Select(ToInteractionFileModel)
                .ToList()
        };

        StatusText = roots.Count == 1
            ? $"Скопирован элемент {roots[0].Name}"
            : $"Скопировано элементов: {roots.Count}";
        OnPropertyChanged(nameof(CanPasteSelection));
    }

    [RelayCommand]
    private void PasteSelection()
    {
        if (_clipboardDocument is null || _clipboardDocument.Controls.Count == 0)
            return;

        BeginUndoBatch();
        try
        {
        foreach (var bindingSourceFile in _clipboardDocument.BindingSources)
        {
            if (BindingSources.Any(source => source.Id == bindingSourceFile.Id))
                continue;

            BindingSources.Add(FromBindingSourceFileModel(bindingSourceFile));
        }

        var clonesByOriginalId = new Dictionary<string, DesignControlModel>(StringComparer.OrdinalIgnoreCase);
        var rootIds = _clipboardDocument.Controls
            .Where(control => string.IsNullOrWhiteSpace(control.ParentId) || !_clipboardDocument.Controls.Any(candidate => candidate.Id == control.ParentId))
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pastedRoots = new List<DesignControlModel>();

        foreach (var controlFile in _clipboardDocument.Controls)
        {
            var clone = FromControlFileModel(controlFile);
            clone.Id = Guid.NewGuid().ToString("N");
            clone.Name = GetUniqueControlName(clone.Type);
            clonesByOriginalId[controlFile.Id] = clone;
        }

        foreach (var controlFile in _clipboardDocument.Controls)
        {
            var clone = clonesByOriginalId[controlFile.Id];
            clone.ParentId = clonesByOriginalId.TryGetValue(NormalizeId(controlFile.ParentId), out var parentClone)
                ? parentClone.Id
                : "";

            if (rootIds.Contains(controlFile.Id))
            {
                clone.X += Math.Max(16, SnapStep * 2);
                clone.Y += Math.Max(16, SnapStep * 2);
                ClampControlToSurface(clone);
                pastedRoots.Add(clone);
            }

            Controls.Add(clone);
        }

        var pastedNameMap = _clipboardDocument.Controls
            .Where(controlFile => !string.IsNullOrWhiteSpace(controlFile.Name))
            .Where(controlFile => clonesByOriginalId.ContainsKey(controlFile.Id))
            .ToDictionary(controlFile => controlFile.Name, controlFile => clonesByOriginalId[controlFile.Id].Name, StringComparer.OrdinalIgnoreCase);
        CloneInteractionsForNameMap(pastedNameMap, _clipboardDocument.Interactions);

        SetSelection(pastedRoots, pastedRoots.LastOrDefault());
        NotifyDesignerStateChanged();
        StatusText = pastedRoots.Count == 1
            ? $"Вставлен элемент {pastedRoots[0].Name}"
            : $"Вставлено элементов: {pastedRoots.Count}";
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    private void RemoveInteractionsReferencingControls(ISet<string> controlNames)
    {
        if (controlNames.Count == 0)
            return;

        foreach (var interaction in Interactions
            .Where(interaction => controlNames.Contains(interaction.SourceControlName)
                || controlNames.Contains(interaction.TargetControlName))
            .ToList())
        {
            Interactions.Remove(interaction);
        }
    }

    private void CloneInteractionsForNameMap(
        IReadOnlyDictionary<string, string> controlNameMap,
        IEnumerable<InteractionFileModel>? sourceInteractions = null)
    {
        if (controlNameMap.Count == 0)
            return;

        var source = sourceInteractions is null
            ? Interactions.Select(ToInteractionFileModel).ToList()
            : sourceInteractions.ToList();

        foreach (var interactionFile in source)
        {
            if (!controlNameMap.TryGetValue(interactionFile.SourceControlName, out var newSourceName))
            {
                continue;
            }

            var isShowMessage = string.Equals(interactionFile.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase);
            var hasTarget = controlNameMap.TryGetValue(interactionFile.TargetControlName, out var newTargetName);
            if (!isShowMessage && !hasTarget)
                continue;

            var clone = FromInteractionFileModel(interactionFile);
            clone.Id = Guid.NewGuid().ToString("N");
            clone.SourceControlName = newSourceName;
            clone.TargetControlName = hasTarget ? newTargetName! : "";
            Interactions.Add(clone);
        }
    }

    [RelayCommand]
    private void BringSelectionToFront()
    {
        ReorderSelection(true);
    }

    [RelayCommand]
    private void SendSelectionToBack()
    {
        ReorderSelection(false);
    }

    [RelayCommand]
    private void AlignSelectionLeft()
    {
        AlignSelectionCore(SelectionAlignment.Left);
    }

    [RelayCommand]
    private void AlignSelectionTop()
    {
        AlignSelectionCore(SelectionAlignment.Top);
    }

    [RelayCommand]
    private void AlignSelectionRight()
    {
        AlignSelectionCore(SelectionAlignment.Right);
    }

    [RelayCommand]
    private void AlignSelectionCenter()
    {
        AlignSelectionCore(SelectionAlignment.Center);
    }

    [RelayCommand]
    private void AlignSelectionBottom()
    {
        AlignSelectionCore(SelectionAlignment.Bottom);
    }

    [RelayCommand]
    private void AlignSelectionMiddle()
    {
        AlignSelectionCore(SelectionAlignment.Middle);
    }

    [RelayCommand]
    private void DistributeSelectionHorizontal()
    {
        DistributeSelectionCore(distributeHorizontally: true);
    }

    [RelayCommand]
    private void DistributeSelectionVertical()
    {
        DistributeSelectionCore(distributeHorizontally: false);
    }

    [RelayCommand]
    private void MatchSelectionWidth()
    {
        MatchSelectionSizeCore(matchWidth: true, matchHeight: false);
    }

    [RelayCommand]
    private void MatchSelectionHeight()
    {
        MatchSelectionSizeCore(matchWidth: false, matchHeight: true);
    }

    [RelayCommand]
    private void MatchSelectionSize()
    {
        MatchSelectionSizeCore(matchWidth: true, matchHeight: true);
    }

    [RelayCommand]
    private void CopyStyle()
    {
        if (SelectedControl is null)
            return;

        _styleClipboard = ControlStyleSnapshot.FromControl(SelectedControl);
        OnPropertyChanged(nameof(CanPasteStyle));
        StatusText = $"Стиль скопирован из {SelectedControl.Name}.";
    }

    [RelayCommand]
    private void PasteStyle()
    {
        if (_styleClipboard is null)
            return;

        var targets = GetVisibleEditableSelectedRootControls().ToList();

        if (targets.Count == 0)
            return;

        foreach (var control in targets)
            _styleClipboard.ApplyTo(control, this);

        NotifyDesignerStateChanged();
        StatusText = targets.Count == 1
            ? $"Стиль применен к {targets[0].Name}."
            : $"Стиль применен к элементам: {targets.Count}.";
    }

    [RelayCommand]
    private void ToggleImmersiveDesignerMode()
    {
        IsImmersiveDesignerMode = !IsImmersiveDesignerMode;
        StatusText = IsImmersiveDesignerMode
            ? "Включен полноэкранный режим редактирования"
            : "Возвращен стандартный режим конструктора";
    }

    [RelayCommand]
    private void ToggleUserPreviewMode()
    {
        IsUserPreviewMode = !IsUserPreviewMode;
        StatusText = IsUserPreviewMode
            ? "Режим просмотра: рамки и служебные элементы дизайнера скрыты"
            : "Рамки и служебные элементы дизайнера снова видны";
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var targetSnapshot = _undoStack.Pop();
        _redoStack.Push(_currentSnapshot);
        RestoreFromSnapshot(targetSnapshot);
        StatusText = "Отменено последнее действие";
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var targetSnapshot = _redoStack.Pop();
        _undoStack.Push(_currentSnapshot);
        RestoreFromSnapshot(targetSnapshot);
        StatusText = "Повторено последнее действие";
    }

    public void BeginUndoBatch(bool trackHistory = true)
    {
        if (_undoBatchDepth == 0)
            _undoBatchTrackHistory = trackHistory;
        else
            _undoBatchTrackHistory |= trackHistory;

        _undoBatchDepth++;
    }

    public void CommitUndoBatch()
    {
        if (_undoBatchDepth <= 0)
            return;

        _undoBatchDepth--;
        if (_undoBatchDepth > 0)
            return;

        var trackHistory = _undoBatchTrackHistory;
        _undoBatchTrackHistory = false;
        NotifyDesignerStateChanged(trackHistory);
    }

    [RelayCommand]
    private void RestoreHistoryState(UndoRedoHistoryItemModel? item)
    {
        if (item is null || item.IsCurrent)
            return;

        var snapshots = BuildHistorySnapshots();
        if (item.Index < 0 || item.Index >= snapshots.Count)
            return;

        var targetIndex = item.Index;
        _undoStack.Clear();
        _redoStack.Clear();

        foreach (var snapshot in snapshots.Take(targetIndex))
            _undoStack.Push(snapshot);

        for (var index = snapshots.Count - 1; index > targetIndex; index--)
            _redoStack.Push(snapshots[index]);

        RestoreFromSnapshot(snapshots[targetIndex]);
        StatusText = $"История: переход к шагу {targetIndex + 1} из {snapshots.Count}.";
    }

    [RelayCommand]
    public void GenerateXaml()
    {
        // Итоговый XAML строится прямо из текущего состояния документа,
        // поэтому предпросмотр и экспорт всегда используют одну и ту же модель.
        var usesManagedWindowLayout = IsFormSizeManagedByMonitor;
        var resolvedWidth = DesignWidth;
        var resolvedHeight = DesignHeight;
        var themePalette = DesignerThemeCatalog.Get(FormTheme);
        var exportNamespace = ResolveExportNamespace();
        var windowClassName = ResolveExportWindowClassName();
        var viewModelClassName = ResolveExportViewModelClassName();
        var exportControlNames = BuildExportControlNameMap(Controls, windowClassName, viewModelClassName);
        var layoutPlan = BuildLayoutExportPlan();
        var controlNodes = Controls.ToDictionary(
            controlModel => controlModel.Id,
            controlModel => (IDesignControlNode)new DesignControlNodeAdapter(controlModel),
            StringComparer.OrdinalIgnoreCase);
        var bindingSources = BindingMetadataMapper.ToMetadataMap(BindingSources);
        var services = new DesignerServiceProvider()
            .Add<IBuiltInXamlBridge>(new BuiltInXamlBridge(this));
        var exportContext = new XamlExportContext(
            services,
            parentId => GetChildControls(parentId)
                .Select(child => controlNodes[child.Id])
                .ToList(),
            (childNode, childIndent, childWriter, context) => TryAppendControlXamlViaDescriptor(childNode, childIndent, childWriter, context),
            bindingSources);
        var bodyBuilder = new StringBuilder();
        var bodyWriter = new StringBuilderXamlWriter(bodyBuilder);

        _activeXamlWriter = bodyWriter;
        _activeXamlExportContext = exportContext;
        _activeXamlControlNodes = controlNodes;
        _activeXamlControlNameMap = exportControlNames;
        _activeLayoutExportPlan = layoutPlan;

        try
        {
            foreach (var control in GetRootControlsForExport())
                AppendControlXaml(control, 2);
        }
        finally
        {
            _activeXamlWriter = null;
            _activeXamlExportContext = null;
            _activeXamlControlNodes = null;
            _activeXamlControlNameMap = null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<Window xmlns=\"https://github.com/avaloniaui\"");
        sb.AppendLine("        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        sb.AppendLine($"        x:Class=\"{EscapeXml(exportNamespace)}.{EscapeXml(windowClassName)}\"");
        sb.AppendLine("        x:CompileBindings=\"False\"");
        sb.AppendLine("        xmlns:primitives=\"clr-namespace:Avalonia.Controls.Primitives;assembly=Avalonia.Controls\"");
        if (ShouldExportRealDataGrid && Controls.Any(control => control.Type == DesignerControlTypes.DataGrid))
            sb.AppendLine("        xmlns:dataGrid=\"clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls.DataGrid\"");
        foreach (var xmlNamespace in exportContext.RegisteredNamespaces.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            sb.AppendLine($"        xmlns:{EscapeXml(xmlNamespace.Key)}=\"{EscapeXml(xmlNamespace.Value)}\"");
        sb.AppendLine($"        Title=\"{EscapeXml(FormTitle)}\"");
        if (!usesManagedWindowLayout)
        {
            sb.AppendLine($"        Width=\"{ToInvariant(resolvedWidth)}\"");
            sb.AppendLine($"        Height=\"{ToInvariant(resolvedHeight)}\"");
        }
        sb.AppendLine($"        RequestedThemeVariant=\"{EscapeXml(DesignerThemeCatalog.NormalizeThemeName(FormTheme))}\"");
        sb.AppendLine(IsCompactXamlExport
            ? $"        Background=\"{EscapeXml(SurfaceBackground)}\""
            : $"        Background=\"{{DynamicResource {ThemeResourceKeys.WindowBackgroundBrush}}}\"");
        sb.AppendLine($"        WindowState=\"{EscapeXml(ToAvaloniaWindowState(FormWindowState))}\"");
        sb.AppendLine($"        WindowStartupLocation=\"{EscapeXml(ToAvaloniaStartupLocation(FormStartupLocation))}\"");
        sb.AppendLine($"        CanResize=\"{BoolToXaml(FormCanResize)}\"");
        sb.AppendLine($"        ShowInTaskbar=\"{BoolToXaml(FormShowInTaskbar)}\"");
        sb.AppendLine($"        Topmost=\"{BoolToXaml(FormTopmost)}\"");
        sb.AppendLine($"        SystemDecorations=\"{EscapeXml(FormHasSystemDecorations ? "Full" : "None")}\">");
        if (IsFullStyledXamlExport)
        {
            AppendThemeResources(sb, themePalette);
            AppendThemeStyles(sb);
        }
        AppendRootContainerOpening(sb, usesManagedWindowLayout, resolvedWidth, resolvedHeight);
        if (ShouldIncludeExportComments && layoutPlan.FallbackToCanvas)
            sb.AppendLine($"    <!-- {EscapeXml(layoutPlan.Details)} -->");
        if (ShouldIncludeExportComments)
            AppendExportDependencyComments(sb);

        if (ShouldIncludeExportComments && BindingSources.Count > 0)
        {
            sb.AppendLine(ShouldGenerateDemoRuntimeCode
                ? "    <!-- Runtime demo BindingSource коллекции, созданные опциональным demo-режимом:"
                : "    <!-- BindingSource-схемы. Чистый UI использует их только для колонок DataGrid:");
            foreach (var source in BindingSources)
            {
                var fields = source.Fields.Count == 0
                    ? "columns are generated automatically"
                    : string.Join(", ", source.Fields.Select(field =>
                    {
                        var flags = new List<string>();
                        if (!field.IsVisible)
                            flags.Add("hidden");
                        if (!string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
                            flags.Add($"sort:{field.SortDirection}@{field.SortOrder}");
                        if (field.GroupOrder >= 0)
                            flags.Add($"group:{field.GroupOrder}");

                        return flags.Count == 0
                            ? field.Path
                            : $"{field.Path} [{string.Join("; ", flags)}]";
                    }));

                var sourceDescription = ShouldGenerateDemoRuntimeCode
                    ? $"{source.Path} : ObservableCollection<{source.ItemTypeName}>"
                    : $"{source.NameOrFallback()} ({source.Path})";
                sb.AppendLine($"         {EscapeXml(sourceDescription)} [{EscapeXml(fields)}]");
            }
            sb.AppendLine("    -->");
        }

        sb.Append(bodyBuilder.ToString());

        sb.AppendLine(GetRootContainerClosingTag());
        sb.AppendLine("</Window>");
        GeneratedXaml = sb.ToString();
        GeneratedCSharp = BuildGeneratedCSharp();
        GeneratedBindingGuide = BuildGeneratedBindingGuide();
        _exportCacheDocumentSnapshotHash = GetSnapshotHash(_currentSnapshot);
        _exportCacheSettingsSignature = BuildExportSettingsSignature();
        _exportCacheGeneratedUtc = DateTime.UtcNow;
        _activeLayoutExportPlan = null;
        RaiseExportChecklistProperties();
        RaiseExportCacheProperties();
    }

    private void MarkExportCacheStale()
    {
        RaiseExportChecklistProperties();
        RaiseExportCacheProperties();
    }

    private void RaiseExportCacheProperties()
    {
        OnPropertyChanged(nameof(IsExportCacheStale));
        OnPropertyChanged(nameof(ExportCacheStatusText));
        OnPropertyChanged(nameof(ExportCacheStatusBackground));
        OnPropertyChanged(nameof(ExportCacheStatusBorder));
        OnPropertyChanged(nameof(ExportCacheStatusForeground));
    }

    private string BuildExportSettingsSignature()
    {
        return string.Join("|",
            ExportTarget,
            ExportProjectNamespace,
            DataGridExportMode,
            LayoutExportMode,
            XamlVerbosity,
            IncludeExportComments,
            IncludeSampleData,
            IncludeCrudSkeleton,
            IncludeCommunityToolkitAttributes,
            IncludePluginRuntimeReferences);
    }

    private static string GetSnapshotHash(string snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
            return "";

        var bytes = Encoding.UTF8.GetBytes(snapshot);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private void AppendExportDependencyComments(StringBuilder sb)
    {
        var packages = GetRequiredExportNuGetPackages().ToList();
        sb.AppendLine("    <!-- Подсказки экспорта:");
        if (packages.Count == 0)
        {
            sb.AppendLine("         Дополнительные NuGet-пакеты не требуются. Достаточно обычного нового Avalonia-проекта.");
        }
        else
        {
            sb.AppendLine("         В новом Avalonia-проекте установите NuGet-пакеты:");
            foreach (var package in packages)
                sb.AppendLine($"         - {EscapeXml(package)}");
        }

        if (ShouldExportPortableDataGrid && Controls.Any(control => control.Type == DesignerControlTypes.DataGrid))
            sb.AppendLine($"         {BuildDataGridExportSummary()}. Для рабочей таблицы выберите Real Avalonia DataGrid и установите NuGet.");

        if (BindingSources.Count > 0 && !ShouldGenerateDemoRuntimeCode)
            sb.AppendLine("         BindingSource используется только как схема колонок. Тестовые модели и fake data не генерируются.");

        if (IncludePluginRuntimeReferences && Controls.Any(IsPluginRuntimeControl))
            sb.AppendLine("         Также подключите runtime DLL плагинов, которые используются на форме.");

        sb.AppendLine("    -->");
    }

    private IEnumerable<string> GetRequiredExportNuGetPackages()
    {
        if (ShouldExportRealDataGrid && Controls.Any(control => control.Type == DesignerControlTypes.DataGrid))
            yield return "Avalonia.Controls.DataGrid";

        if (ShouldGenerateDemoRuntimeCode || IncludeCommunityToolkitAttributes)
            yield return "CommunityToolkit.Mvvm";

        if (ShouldGenerateDemoRuntimeCode && BuildCrudGenerationContexts().Any(context => IsSqlServerSource(context.Source)))
            yield return "Microsoft.Data.SqlClient";
    }

    private string ResolveExportNamespace()
    {
        return IsMainWindowExportTarget
            ? SanitizeNamespace(ExportProjectNamespace, "AvaloniaApplication1")
            : "GeneratedForms";
    }

    private bool HasExportNamespaceError()
    {
        return IsMainWindowExportTarget && !IsValidNamespaceText(ExportProjectNamespace);
    }

    private static bool IsValidNamespaceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        return parts.All(IsValidCSharpIdentifier);
    }

    private static bool IsValidCSharpIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!IsIdentifierStart(value[0]))
            return false;

        return value.Skip(1).All(IsIdentifierPart);
    }

    private static bool IsIdentifierStart(char value)
    {
        return value == '_' || char.IsLetter(value);
    }

    private static bool IsIdentifierPart(char value)
    {
        return value == '_' || char.IsLetterOrDigit(value);
    }

    private string ResolveExportWindowClassName()
    {
        return IsMainWindowExportTarget
            ? "MainWindow"
            : "Form1Window";
    }

    private string ResolveExportViewModelClassName()
    {
        return IsMainWindowExportTarget
            ? "MainWindowViewModel"
            : "Form1ViewModel";
    }

    private static string SanitizeNamespace(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var parts = source
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((part, index) => SanitizeIdentifier(part, index == 0 ? fallback : "Namespace"))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        return parts.Count == 0 ? fallback : string.Join(".", parts);
    }

    private string BuildExportSummaryText()
    {
        var packages = GetRequiredExportNuGetPackages()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pluginControls = Controls
            .Where(IsPluginRuntimeControl)
            .Select(control => string.IsNullOrWhiteSpace(control.PluginId) ? control.Type : control.PluginId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasGeneratedViewModel = ShouldGenerateDemoRuntimeCode;
        var nugetText = packages.Count == 0 ? "не нужны" : string.Join(", ", packages);
        var pluginText = IncludePluginRuntimeReferences && pluginControls.Count > 0
            ? string.Join(", ", pluginControls)
            : "не нужны";

        return $"Цель: {(IsMainWindowExportTarget ? "MainWindow.axaml / MainWindow.axaml.cs" : $"{ResolveExportWindowClassName()}.axaml / {ResolveExportWindowClassName()}.axaml.cs")}\n"
            + $"Namespace: {ResolveExportNamespace()}\n"
            + $"Режим: {GenerationMode}; XAML: {XamlVerbosity}; {BuildDataGridExportSummary()}\n"
            + $"NuGet: {nugetText}\n"
            + $"DLL плагинов: {pluginText}\n"
            + $"ViewModel генерируется: {(hasGeneratedViewModel ? "да" : "нет")}";
    }

    private string BuildExportCompactSummary()
    {
        var packages = GetRequiredExportNuGetPackages()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pluginCount = IncludePluginRuntimeReferences
            ? Controls.Where(IsPluginRuntimeControl)
                .Select(GetPluginRuntimeRequirementName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            : 0;
        var modeText = IsCleanUiGenerationMode ? "Clean UI" : "Demo data";
        var targetText = IsMainWindowExportTarget ? "MainWindow" : "Form1Window";
        var layoutText = BuildLayoutExportPlan().ShortText;
        var nugetText = packages.Count == 0 ? "No NuGet" : $"{packages.Count} NuGet";
        var pluginText = pluginCount == 0 ? "No plugins" : $"{pluginCount} plugin DLL";

        return $"{ExportStatusText} · {modeText} · {targetText} · {layoutText} · {nugetText} · {pluginText}";
    }

    private LayoutExportPlan BuildLayoutExportPlan()
    {
        var requested = NormalizeLayoutExportMode(LayoutExportMode);
        if (!string.Equals(requested, LayoutExportModeResponsive, StringComparison.Ordinal))
        {
            return new LayoutExportPlan(
                RequestedMode: requested,
                EffectiveRootLayoutMode: DesignerLayoutModes.Absolute,
                UsesResponsiveStack: false,
                FallbackToCanvas: false,
                StackSpacing: SurfaceLayoutSpacing,
                RootMargin: "",
                ShortText: "Canvas",
                BadgeText: "Layout: Canvas",
                Value: "Canvas layout",
                Details: "Экспорт использует Canvas.Left/Canvas.Top и сохраняет абсолютные координаты формы.",
                Severity: ExportChecklistSeverity.Ok);
        }

        var fallbackReason = GetResponsiveLayoutFallbackReason();
        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            return new LayoutExportPlan(
                RequestedMode: requested,
                EffectiveRootLayoutMode: DesignerLayoutModes.Absolute,
                UsesResponsiveStack: false,
                FallbackToCanvas: true,
                StackSpacing: SurfaceLayoutSpacing,
                RootMargin: "",
                ShortText: "Canvas fallback",
                BadgeText: "Layout: Canvas fallback",
                Value: "Canvas fallback",
                Details: $"Responsive layout пока недоступен для текущей формы: {fallbackReason}. Экспорт безопасно переключён на Canvas layout.",
                Severity: ExportChecklistSeverity.Warning);
        }

        var roots = GetChildControls(null).OrderBy(control => control.Y).ThenBy(control => control.X).ToList();
        var spacing = CalculateResponsiveStackSpacing(roots);
        var margin = BuildResponsiveRootMargin(roots);

        return new LayoutExportPlan(
            RequestedMode: requested,
            EffectiveRootLayoutMode: DesignerLayoutModes.Stack,
            UsesResponsiveStack: true,
            FallbackToCanvas: false,
            StackSpacing: spacing,
            RootMargin: margin,
            ShortText: "Responsive",
            BadgeText: "Layout: Responsive StackPanel",
            Value: "Responsive StackPanel",
            Details: "Экспериментальный экспорт: простая вертикальная форма будет сгенерирована как StackPanel. Координаты модели не меняются.",
            Severity: ExportChecklistSeverity.Warning);
    }

    private string GetResponsiveLayoutFallbackReason()
    {
        var rootControls = GetChildControls(null)
            .OrderBy(control => control.Y)
            .ThenBy(control => control.X)
            .ToList();

        if (Controls.Any(control => !string.IsNullOrWhiteSpace(NormalizeId(control.ParentId))))
            return "есть вложенные элементы, группы или контейнеры";

        if (rootControls.Any(IsPluginRuntimeControl))
            return "на форме есть plugin controls, для них пока сохраняется Canvas export";

        var visibleControls = rootControls
            .Where(control => control.IsVisible)
            .OrderBy(control => control.Y)
            .ThenBy(control => control.X)
            .ToList();

        for (var index = 0; index < visibleControls.Count - 1; index++)
        {
            var current = visibleControls[index];
            var next = visibleControls[index + 1];
            if (next.Y < current.Y + current.Height - 1)
                return $"элементы '{current.NameOrFallback()}' и '{next.NameOrFallback()}' пересекаются по вертикали";
        }

        return "";
    }

    private static double CalculateResponsiveStackSpacing(IReadOnlyList<DesignControlModel> controls)
    {
        var visible = controls
            .Where(control => control.IsVisible)
            .OrderBy(control => control.Y)
            .ThenBy(control => control.X)
            .ToList();

        if (visible.Count < 2)
            return 12;

        var gaps = new List<double>();
        for (var index = 0; index < visible.Count - 1; index++)
        {
            var gap = visible[index + 1].Y - (visible[index].Y + visible[index].Height);
            if (gap >= 0)
                gaps.Add(gap);
        }

        return gaps.Count == 0
            ? 12
            : Math.Round(gaps.Average(), 1);
    }

    private static string BuildResponsiveRootMargin(IReadOnlyList<DesignControlModel> controls)
    {
        if (controls.Count == 0)
            return "20";

        var left = Math.Max(0, controls.Min(control => control.X));
        var top = Math.Max(0, controls.Min(control => control.Y));
        if (left <= 0.01 && top <= 0.01)
            return "0";

        return $"{ToInvariant(Math.Round(left, 1))},{ToInvariant(Math.Round(top, 1))},0,0";
    }

    private IReadOnlyList<ExportChecklistItem> BuildExportChecklist()
    {
        var exportDiagnostics = BuildExportDiagnosticsSnapshot();
        var exportSeverity = ToChecklistSeverity(exportDiagnostics);
        var namespaceError = HasExportNamespaceError();
        var xamlGenerated = !string.IsNullOrWhiteSpace(GeneratedXaml);
        var csharpGenerated = !string.IsNullOrWhiteSpace(GeneratedCSharp);
        var packages = GetRequiredExportNuGetPackages()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pluginControls = Controls
            .Where(IsPluginRuntimeControl)
            .ToList();
        var pluginNames = pluginControls
            .Select(GetPluginRuntimeRequirementName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasPortableDataGridInteraction = ShouldExportPortableDataGrid
            && Interactions.Any(interaction => IsDataGridSelectionChangedEvent(interaction.EventName));
        var dataGridStatus = BuildDataGridBindingChecklistStatus();
        var layoutStatus = BuildLayoutExportPlan();
        var exportableInteractionCount = GetExportableInteractions().Count;
        var unsupportedInteractionCount = Math.Max(0, Interactions.Count - exportableInteractionCount);

        return new List<ExportChecklistItem>
        {
            new()
            {
                Title = "XAML generated",
                Value = xamlGenerated ? (namespaceError ? "Error" : "OK") : "Error",
                Severity = !xamlGenerated || namespaceError ? ExportChecklistSeverity.Error : ExportChecklistSeverity.Ok,
                Details = !xamlGenerated
                    ? "XAML ещё не сгенерирован. Нажмите «Обновить»."
                    : namespaceError
                        ? "Namespace проекта некорректный."
                        : "Файл можно копировать с учётом предупреждений ниже."
            },
            new()
            {
                Title = "C# generated",
                Value = csharpGenerated ? (namespaceError ? "Error" : "OK") : "Error",
                Severity = !csharpGenerated || namespaceError ? ExportChecklistSeverity.Error : ExportChecklistSeverity.Ok,
                Details = !csharpGenerated
                    ? "C# ещё не сгенерирован. Нажмите «Обновить»."
                    : namespaceError
                        ? "Namespace проекта некорректный."
                        : "Code-behind сгенерирован для выбранной цели экспорта."
            },
            new()
            {
                Title = "Target",
                Value = IsMainWindowExportTarget ? "MainWindow" : "Form1Window",
                Severity = namespaceError ? ExportChecklistSeverity.Error : ExportChecklistSeverity.Ok,
                Details = namespaceError
                    ? "Исправьте namespace проекта в дополнительных настройках."
                    : ResolveExportNamespace()
            },
            new()
            {
                Title = "Layout export mode",
                Value = layoutStatus.Value,
                Severity = layoutStatus.Severity,
                Details = layoutStatus.Details
            },
            new()
            {
                Title = "DataGrid",
                Value = ShouldExportRealDataGrid
                    ? "Real Avalonia DataGrid"
                    : ShouldExportPlaceholderDataGrid
                        ? "Placeholder без NuGet"
                        : "Visual table без NuGet",
                Severity = dataGridStatus.Severity,
                Details = BuildDataGridExportSummary()
            },
            new()
            {
                Title = "Required NuGet",
                Value = packages.Count == 0 ? "none" : string.Join(", ", packages),
                Severity = packages.Count == 0 ? ExportChecklistSeverity.Ok : ExportChecklistSeverity.Warning,
                Details = packages.Count == 0
                    ? "Дополнительные NuGet-пакеты не нужны."
                    : $"Установите перед сборкой: {string.Join(", ", packages)}."
            },
            new()
            {
                Title = "Plugins",
                Value = pluginNames.Count == 0 || !IncludePluginRuntimeReferences ? "none" : $"{pluginNames.Count} DLL",
                Severity = pluginNames.Count == 0
                    ? ExportChecklistSeverity.Ok
                    : ExportChecklistSeverity.Warning,
                Details = pluginNames.Count == 0
                    ? "Plugin controls не используются."
                    : IncludePluginRuntimeReferences
                        ? $"Новый проект должен ссылаться на runtime DLL: {string.Join(", ", pluginNames)}."
                        : "Plugin controls будут экспортированы как безопасные placeholder-элементы."
            },
            new()
            {
                Title = "BindingSource status",
                Value = dataGridStatus.Value,
                Severity = dataGridStatus.Severity,
                Details = dataGridStatus.Details
            },
            new()
            {
                Title = "Interactions exported",
                Value = Interactions.Count == 0 ? "none" : $"{exportableInteractionCount}/{Interactions.Count}",
                Severity = unsupportedInteractionCount > 0 || hasPortableDataGridInteraction
                    ? ExportChecklistSeverity.Warning
                    : ExportChecklistSeverity.Ok,
                Details = Interactions.Count == 0
                    ? "Логика формы не настроена."
                    : hasPortableDataGridInteraction
                        ? "DataGrid.SelectionChanged не экспортируется как рабочий handler в portable DataGrid mode."
                        : unsupportedInteractionCount > 0
                            ? $"Будет экспортировано правил: {exportableInteractionCount}. Preview-only/unsupported: {unsupportedInteractionCount}. Проверьте diagnostics."
                            : "Будут экспортированы все реально настроенные обработчики без demo-кода."
            },
            new()
            {
                Title = "ViewModel generated",
                Value = ShouldGenerateViewModelForExport() ? "yes" : "no",
                Severity = ExportChecklistSeverity.Ok,
                Details = ShouldGenerateViewModelForExport()
                    ? "ViewModel нужен выбранному режиму генерации."
                    : "Clean UI не создаёт пустой ViewModel без необходимости."
            },
            new()
            {
                Title = "Export comments",
                Value = ShouldIncludeExportComments ? "on" : "off",
                Severity = ExportChecklistSeverity.Ok,
                Details = ShouldIncludeExportComments
                    ? "Подсказки будут добавлены в XAML/C#."
                    : "Код будет чище: подсказки остаются в интерфейсе конструктора."
            }
        };
    }

    private List<DocumentDiagnosticModel> BuildExportDiagnosticsSnapshot()
    {
        var diagnostics = new List<DocumentDiagnosticModel>();
        AppendExportDiagnostics(diagnostics);
        return diagnostics;
    }

    private static ExportChecklistSeverity ToChecklistSeverity(IEnumerable<DocumentDiagnosticModel> diagnostics)
    {
        var list = diagnostics.ToList();
        if (list.Any(item => item.Severity == DocumentDiagnosticSeverity.Error))
            return ExportChecklistSeverity.Error;

        return list.Any(item => item.Severity == DocumentDiagnosticSeverity.Warning)
            ? ExportChecklistSeverity.Warning
            : ExportChecklistSeverity.Ok;
    }

    private static string ToChecklistStatusValue(ExportChecklistSeverity severity)
    {
        return severity switch
        {
            ExportChecklistSeverity.Error => "Error",
            ExportChecklistSeverity.Warning => "Warning",
            _ => "OK"
        };
    }

    private (string Value, string Details, ExportChecklistSeverity Severity) BuildDataGridBindingChecklistStatus()
    {
        var grids = Controls
            .Where(control => control.Type == DesignerControlTypes.DataGrid)
            .ToList();
        if (grids.Count == 0)
            return ("no DataGrid", "На форме нет DataGrid.", ExportChecklistSeverity.Ok);

        var totalFieldCount = grids.Sum(CountExportableDataGridFields);
        var gridsWithoutFields = grids.Count(control => !HasExportableDataGridFields(control));
        if (gridsWithoutFields == grids.Count)
            return ("DataGrid without fields", "DataGrid будет экспортирован как placeholder, пока не добавлены реальные BindingSource fields.", ExportChecklistSeverity.Warning);

        if (gridsWithoutFields > 0)
            return ($"DataGrid with {totalFieldCount} fields", $"Есть реальные fields: {totalFieldCount}; DataGrid без fields: {gridsWithoutFields}.", ExportChecklistSeverity.Warning);

        return ($"DataGrid with {totalFieldCount} fields", "Все DataGrid имеют реальные видимые fields.", ExportChecklistSeverity.Ok);
    }

    private bool ShouldGenerateViewModelForExport()
    {
        return ShouldGenerateDemoRuntimeCode
            && (BuildCrudGenerationContexts().Any()
                || Controls.Any(control => control.Type == DesignerControlTypes.TextBox)
                || BindingSources.Count > 0);
    }

    private string GetPluginRuntimeRequirementName(DesignControlModel control)
    {
        var descriptor = GetDescriptor(control.Type);
        var assemblyPath = descriptor.GetType().Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath)
            && descriptor.GetType().Assembly != typeof(MainWindowViewModel).Assembly)
        {
            return Path.GetFileName(assemblyPath);
        }

        return string.IsNullOrWhiteSpace(control.PluginId) ? control.Type : control.PluginId;
    }

    private string BuildDataGridExportSummary()
    {
        var grids = Controls
            .Where(control => control.Type == DesignerControlTypes.DataGrid)
            .ToList();
        if (grids.Count == 0)
            return "DataGrid: none";

        if (ShouldExportRealDataGrid)
            return "DataGrid: real Avalonia DataGrid, requires NuGet";

        var exportableFieldCount = grids.Sum(control => CountExportableDataGridFields(control));
        var placeholderCount = grids.Count(control => !HasExportableDataGridFields(control));

        if (ShouldExportPlaceholderDataGrid)
        {
            return exportableFieldCount == 0
                ? "DataGrid: placeholder, no fields"
                : $"DataGrid: placeholder, {exportableFieldCount} fields ignored";
        }

        if (placeholderCount == grids.Count)
            return "DataGrid: placeholder, no fields";

        return placeholderCount == 0
            ? $"DataGrid: visual table, {exportableFieldCount} fields"
            : $"DataGrid: visual table, {exportableFieldCount} fields; placeholder: {placeholderCount}";
    }

    private bool HasExportableDataGridFields(DesignControlModel control)
    {
        return CountExportableDataGridFields(control) > 0;
    }

    private int CountExportableDataGridFields(DesignControlModel control)
    {
        var source = GetBindingSource(control.BindingSourceId);
        return source?.Fields.Count(field => field.IsVisible) ?? 0;
    }

    private string BuildExportDependenciesSummary()
    {
        var packages = GetRequiredExportNuGetPackages()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targetText = IsMainWindowExportTarget
            ? "замените содержимое MainWindow.axaml и MainWindow.axaml.cs в новом Avalonia Desktop проекте"
            : $"добавьте отдельное окно {ResolveExportWindowClassName()}.axaml и {ResolveExportWindowClassName()}.axaml.cs";
        var baseText = $"Как перенести код: {targetText}. Namespace сейчас: {ResolveExportNamespace()}.";
        var pluginNote = IncludePluginRuntimeReferences && Controls.Any(IsPluginRuntimeControl)
            ? " Также подключите runtime DLL плагинов, которые используются на форме."
            : "";

        if (packages.Count == 0)
            return $"{baseText} Дополнительные NuGet-пакеты не нужны. В безопасном режиме DataGrid не требует Avalonia.Controls.DataGrid.{pluginNote}";

        return $"{baseText} Нужно установить NuGet: {string.Join(", ", packages)}.{pluginNote}";
    }

    private string BuildGeneratedBindingGuide()
    {
        if (!ShouldGenerateDemoRuntimeCode)
        {
            return "Режим «Чистый UI»: BindingSource используется только как схема колонок. XAML генерирует таблицу и колонки, но намеренно не добавляет ItemsSource, demo-классы, demo rows, фильтры, fake CRUD и runtime-модели. Включайте режим «С демонстрационными данными» только если действительно нужен sample ViewModel.";
        }

        var crudContexts = BuildCrudGenerationContexts();
        if (crudContexts.Count == 0)
        {
            return "Добавьте DataGrid и подключите BindingSource, чтобы получить рекомендации по привязкам.";
        }

        var textBoxControls = Controls.Where(control => control.Type == DesignerControlTypes.TextBox).ToList();
        var buttonControls = Controls.Where(control => control.Type == DesignerControlTypes.Button).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Нажмите кнопку «Применить привязки», чтобы автоматически назначить рекомендованные биндинги для текущей формы.");
        sb.AppendLine();

        foreach (var context in crudContexts)
        {
            var gridControls = Controls
                .Where(control => control.Type == DesignerControlTypes.DataGrid && control.BindingSourceId == context.Source.Id)
                .ToList();

            sb.AppendLine($"Источник: {context.Source.Name} ({context.ItemTypeName})");

            foreach (var grid in gridControls)
            {
                sb.AppendLine($"- {grid.Name}.ItemsSource -> {{Binding {context.ViewCollectionPropertyName}}}");
                sb.AppendLine($"- {grid.Name}.SelectedItem -> {{Binding {context.SelectedItemPropertyName}, Mode=TwoWay}}");
            }

            if (textBoxControls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Рекомендуемые TextBox:");
                var searchBindingPath = string.IsNullOrWhiteSpace(textBoxControls[0].TextBindingPath)
                    ? context.SearchTextPropertyName
                    : textBoxControls[0].TextBindingPath;
                var searchMarker = string.IsNullOrWhiteSpace(textBoxControls[0].TextBindingPath) ? "рекомендация" : "применено";
                sb.AppendLine($"- {textBoxControls[0].Name}.Text -> {{Binding {searchBindingPath}, Mode=TwoWay}}  (поиск, {searchMarker})");

                var editableFields = context.Fields
                    .Where(field => field.IsVisible)
                    .Take(Math.Max(0, textBoxControls.Count - 1))
                    .ToList();

                for (var index = 0; index < editableFields.Count; index++)
                {
                    var textBox = textBoxControls[index + 1];
                    var field = editableFields[index];
                    var property = SanitizeIdentifier(field.Path, $"Field{index + 1}");
                    var recommendedPath = $"{context.CurrentItemPropertyName}.{property}";
                    var actualPath = string.IsNullOrWhiteSpace(textBox.TextBindingPath) ? recommendedPath : textBox.TextBindingPath;
                    var marker = string.IsNullOrWhiteSpace(textBox.TextBindingPath) ? "рекомендация" : "применено";
                    sb.AppendLine($"- {textBox.Name}.Text -> {{Binding {actualPath}, Mode=TwoWay}}  ({marker})");
                }
            }

            if (buttonControls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Распознанные кнопки:");
                foreach (var button in buttonControls)
                {
                    var action = ResolveGeneratedButtonAction(button);
                    var actionText = action switch
                    {
                        GeneratedButtonAction.Add => $"создание новой записи через BeginCreate{context.ItemTypeName}()",
                        GeneratedButtonAction.Save => $"сохранение через Save{context.ItemTypeName}()",
                        GeneratedButtonAction.Delete => $"удаление через DeleteSelected{context.ItemTypeName}()",
                        GeneratedButtonAction.Edit => $"редактирование через StartEditingSelected{context.ItemTypeName}()",
                        GeneratedButtonAction.Search => $"поиск через Apply{context.ItemTypeName}Filter()",
                        GeneratedButtonAction.Clear => $"сброс редактора через Reset{context.ItemTypeName}Editor()",
                        _ => "не распознана автоматически"
                    };

                    sb.AppendLine($"- {button.Name} ({button.Text}) -> {actionText}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildGeneratedCSharp()
    {
        return ShouldGenerateDemoRuntimeCode
            ? BuildGeneratedDemoCSharp()
            : BuildGeneratedCleanCSharp();
    }

    private string BuildGeneratedCleanCSharp()
    {
        var exportNamespace = ResolveExportNamespace();
        var windowClassName = ResolveExportWindowClassName();
        var viewModelClassName = ResolveExportViewModelClassName();
        var exportControlNames = BuildExportControlNameMap(Controls, windowClassName, viewModelClassName);
        var exportableInteractions = GetExportableInteractions();
        var hasInteractionHandlers = exportableInteractions.Count > 0;
        var hasShowMessageInteractions = exportableInteractions.Any(item =>
            string.Equals(item.Interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase));
        var hasRoutedInteractionHandlers = exportableInteractions.Any(item =>
            string.Equals(item.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.EventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.EventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase));
        var selectionInteractions = new List<(InteractionModel Interaction, DesignControlModel Source, DesignControlModel Target)>();
        var buttonControls = new List<DesignControlModel>();
        var hasButtons = false;
        var hasSelectionInteractions = false;
        var hasRealDataGrid = ShouldExportRealDataGrid && Controls.Any(control => control.Type == DesignerControlTypes.DataGrid);

        var usings = new List<string>
        {
            "using Avalonia.Controls;"
        };

        if (hasRoutedInteractionHandlers)
            usings.Add("using Avalonia.Interactivity;");
        if (hasShowMessageInteractions)
        {
            usings.Add("using Avalonia;");
            usings.Add("using Avalonia.Layout;");
            usings.Add("using Avalonia.Media;");
        }

        var sb = new StringBuilder();
        foreach (var line in usings.Distinct())
            sb.AppendLine(line);

        if (ShouldIncludeExportComments && hasRealDataGrid)
        {
            sb.AppendLine();
            sb.AppendLine("// DataGrid в XAML требует NuGet package: Avalonia.Controls.DataGrid.");
        }

        var packages = GetRequiredExportNuGetPackages().ToList();
        if (ShouldIncludeExportComments && packages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("// NuGet-пакеты, которые нужно установить для этого сгенерированного кода:");
            foreach (var package in packages)
                sb.AppendLine($"// - {package}");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {exportNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {windowClassName} : Window");
        sb.AppendLine("{");
        sb.AppendLine($"    public {windowClassName}()");
        sb.AppendLine("    {");
        sb.AppendLine("        InitializeComponent();");
        sb.AppendLine("    }");

        if (hasButtons)
        {
            sb.AppendLine();
            foreach (var button in buttonControls)
            {
                var handlerName = $"{exportControlNames[button.Id]}Click";
                var buttonInteractions = exportableInteractions
                    .Where(item => item.Source.Id == button.Id
                        && string.Equals(item.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var needsAsyncButtonHandler = buttonInteractions.Any(item =>
                    string.Equals(item.Interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"    private {(needsAsyncButtonHandler ? "async " : "")}void {handlerName}(object? sender, RoutedEventArgs e)");
                sb.AppendLine("    {");
                sb.AppendLine($"        // TODO: добавьте runtime-логику для кнопки {button.Name}.");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        if (hasSelectionInteractions)
        {
            sb.AppendLine();
            foreach (var group in selectionInteractions.GroupBy(item => item.Source.Id))
            {
                var source = group.First().Source;
                var sourceExportName = exportControlNames[source.Id];
                sb.AppendLine($"    private void {sourceExportName}_SelectionChanged(object? sender, SelectionChangedEventArgs e)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var selectedItem = {sourceExportName}.SelectedItem;");
                sb.AppendLine("        if (selectedItem is null)");
                sb.AppendLine("        {");
                foreach (var item in group)
                    AppendGeneratedInteractionAssignment(sb, item.Interaction, item.Target, exportControlNames[item.Target.Id], "null", 3);
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine();
                foreach (var item in group)
                    AppendGeneratedInteractionAssignment(sb, item.Interaction, item.Target, exportControlNames[item.Target.Id], "selectedItem", 2);
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            AppendGeneratedInteractionHelpers(sb, includeShowMessageHelper: hasShowMessageInteractions);
        }

        if (hasInteractionHandlers)
            AppendGeneratedInteractionHandlers(sb, exportableInteractions, exportControlNames, skipButtonClickHandlers: false);

        sb.AppendLine("}");

        return sb.ToString().TrimEnd();
    }

    private string BuildGeneratedDemoCSharp()
    {
        var exportNamespace = ResolveExportNamespace();
        var windowClassName = ResolveExportWindowClassName();
        var viewModelClassName = ResolveExportViewModelClassName();
        var exportControlNames = BuildExportControlNameMap(Controls, windowClassName, viewModelClassName);
        var exportableInteractions = GetExportableInteractions();
        var hasShowMessageInteractions = exportableInteractions.Any(item =>
            string.Equals(item.Interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase));
        var selectionInteractions = new List<(InteractionModel Interaction, DesignControlModel Source, DesignControlModel Target)>();
        var buttonControls = Controls.Where(control => control.Type == DesignerControlTypes.Button).ToList();
        var textBoxControls = Controls.Where(control => control.Type == DesignerControlTypes.TextBox).ToList();
        var crudContexts = BuildCrudGenerationContexts();
        var sqlContexts = crudContexts.Where(context => IsSqlServerSource(context.Source)).ToList();
        var hasSqlContexts = sqlContexts.Count > 0;
        var primaryCrud = crudContexts.FirstOrDefault();
        var hasViewModel = crudContexts.Count > 0 || textBoxControls.Count > 0 || BindingSources.Count > 0;
        var anchorControls = Controls
            .Where(control => (control.AnchorRight || control.AnchorBottom) && IsAbsoluteLayoutParent(control.ParentId))
            .OrderBy(GetControlDepth)
            .ThenBy(control => control.Name)
            .ToList();
        var hasAnchoredControls = anchorControls.Count > 0;
        var hasSelectionInteractions = false;

        var usings = new List<string>
        {
            "using Avalonia.Controls;",
            "using Avalonia.Interactivity;"
        };

        if (hasAnchoredControls)
        {
            usings.Add("using System;");
            usings.Add("using System.Collections.Generic;");
        }

        if (hasViewModel)
        {
            usings.Add("using CommunityToolkit.Mvvm.ComponentModel;");
            usings.Add("using System;");
            usings.Add("using System.Collections.Generic;");
            usings.Add("using System.Collections.ObjectModel;");
            usings.Add("using System.Globalization;");
            usings.Add("using System.Linq;");

            if (hasSqlContexts)
            {
                usings.Add("using Microsoft.Data.SqlClient;");
                usings.Add("using System.Data;");
                usings.Add("using System.Threading.Tasks;");
            }
        }
        if (hasShowMessageInteractions)
        {
            usings.Add("using Avalonia;");
            usings.Add("using Avalonia.Layout;");
            usings.Add("using Avalonia.Media;");
        }

        var sb = new StringBuilder();
        foreach (var line in usings.Distinct())
            sb.AppendLine(line);

        var packages = GetRequiredExportNuGetPackages().ToList();
        if (packages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("// NuGet-пакеты, которые нужно установить для этого demo-кода:");
            foreach (var package in packages)
                sb.AppendLine($"// - {package}");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {exportNamespace};");
        sb.AppendLine();

        if (primaryCrud is not null)
        {
            sb.AppendLine("/*");
            sb.AppendLine(" Следующий шаг после расстановки элементов на форме:");
            sb.AppendLine($" 1. DataGrid привяжите к {primaryCrud.ViewCollectionPropertyName}, а SelectedItem к {primaryCrud.SelectedItemPropertyName}.");
            if (textBoxControls.Count > 0)
                sb.AppendLine($" 2. Строку поиска привяжите к {primaryCrud.SearchTextPropertyName}.");
            sb.AppendLine($" 3. Поля редактирования привязывайте к {primaryCrud.CurrentItemPropertyName}.Свойство.");
            sb.AppendLine(" 4. Кнопки уже получают обработчики Click, а внутри вызовут методы ViewModel.");
            if (hasSqlContexts)
                sb.AppendLine(" 5. Для SQL Server сгенерированы методы Load...FromDatabaseAsync(). Перед запуском вынесите строку подключения в конфиг.");
            sb.AppendLine("*/");
            sb.AppendLine();
        }

        sb.AppendLine($"public partial class {windowClassName} : Window");
        sb.AppendLine("{");
        if (hasAnchoredControls)
        {
            sb.AppendLine("    private readonly List<AnchorBinding> _anchorBindings = new();");
            sb.AppendLine();
        }
        sb.AppendLine($"    public {windowClassName}()");
        sb.AppendLine("    {");
        sb.AppendLine("        InitializeComponent();");
        if (hasViewModel)
        {
            sb.AppendLine($"        DataContext = new {viewModelClassName}();");
            if (hasSqlContexts)
                sb.AppendLine("        Opened += Window_Opened;");
        }
        if (hasAnchoredControls)
        {
            sb.AppendLine("        ConfigureAnchors();");
            sb.AppendLine("        Opened += (_, _) => ApplyAnchors();");
            sb.AppendLine("        SizeChanged += (_, _) => ApplyAnchors();");
        }
        sb.AppendLine("    }");

        if (hasViewModel)
        {
            sb.AppendLine();
            sb.AppendLine($"    private {viewModelClassName} ViewModel => ({viewModelClassName})DataContext!;");
        }

        if (hasSqlContexts)
        {
            sb.AppendLine();
            sb.AppendLine("    private async void Window_Opened(object? sender, EventArgs e)");
            sb.AppendLine("    {");
            sb.AppendLine("        Opened -= Window_Opened;");
            sb.AppendLine("        await ViewModel.InitializeAsync();");
            sb.AppendLine("    }");
        }

        if (hasAnchoredControls)
        {
            sb.AppendLine();
            sb.AppendLine("    private void ConfigureAnchors()");
            sb.AppendLine("    {");
            sb.AppendLine("        _anchorBindings.Clear();");
            foreach (var control in anchorControls)
            {
                var parent = GetControl(control.ParentId);
                var parentExpression = parent is null ? "null" : exportControlNames[parent.Id];
                var baseParentWidth = parent?.Width ?? DesignWidth;
                var baseParentHeight = parent?.Height ?? DesignHeight;
                var controlName = exportControlNames[control.Id];
                sb.AppendLine($"        _anchorBindings.Add(new AnchorBinding({controlName}, {parentExpression}, {ToInvariant(baseParentWidth)}d, {ToInvariant(baseParentHeight)}d, {ToInvariant(control.X)}d, {ToInvariant(control.Y)}d, {ToInvariant(control.Width)}d, {ToInvariant(control.Height)}d, {BoolToCSharp(control.AnchorLeft)}, {BoolToCSharp(control.AnchorTop)}, {BoolToCSharp(control.AnchorRight)}, {BoolToCSharp(control.AnchorBottom)}));");
            }
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void ApplyAnchors()");
            sb.AppendLine("    {");
            sb.AppendLine("        var rootWidth = ClientSize.Width > 0 ? ClientSize.Width : Bounds.Width;");
            sb.AppendLine("        var rootHeight = ClientSize.Height > 0 ? ClientSize.Height : Bounds.Height;");
            sb.AppendLine();
            sb.AppendLine("        foreach (var binding in _anchorBindings)");
            sb.AppendLine("        {");
            sb.AppendLine("            var actualParentWidth = binding.Parent?.Bounds.Width ?? rootWidth;");
            sb.AppendLine("            var actualParentHeight = binding.Parent?.Bounds.Height ?? rootHeight;");
            sb.AppendLine("            var frame = ResolveAnchoredFrame(binding.X, binding.Y, binding.Width, binding.Height, binding.BaseParentWidth, binding.BaseParentHeight, actualParentWidth, actualParentHeight, binding.AnchorLeft, binding.AnchorTop, binding.AnchorRight, binding.AnchorBottom);");
            sb.AppendLine("            binding.Control.Width = frame.Width;");
            sb.AppendLine("            binding.Control.Height = frame.Height;");
            sb.AppendLine("            Canvas.SetLeft(binding.Control, frame.X);");
            sb.AppendLine("            Canvas.SetTop(binding.Control, frame.Y);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static AnchoredFrame ResolveAnchoredFrame(double x, double y, double width, double height, double baseParentWidth, double baseParentHeight, double actualParentWidth, double actualParentHeight, bool anchorLeft, bool anchorTop, bool anchorRight, bool anchorBottom)");
            sb.AppendLine("    {");
            sb.AppendLine("        var left = x;");
            sb.AppendLine("        var top = y;");
            sb.AppendLine("        var right = baseParentWidth - (x + width);");
            sb.AppendLine("        var bottom = baseParentHeight - (y + height);");
            sb.AppendLine();
            sb.AppendLine("        double resolvedX;");
            sb.AppendLine("        double resolvedWidth;");
            sb.AppendLine("        if (anchorLeft && anchorRight)");
            sb.AppendLine("        {");
            sb.AppendLine("            resolvedX = left;");
            sb.AppendLine("            resolvedWidth = actualParentWidth - left - right;");
            sb.AppendLine("        }");
            sb.AppendLine("        else if (!anchorLeft && anchorRight)");
            sb.AppendLine("        {");
            sb.AppendLine("            resolvedWidth = width;");
            sb.AppendLine("            resolvedX = actualParentWidth - right - resolvedWidth;");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            resolvedX = left;");
            sb.AppendLine("            resolvedWidth = width;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        double resolvedY;");
            sb.AppendLine("        double resolvedHeight;");
            sb.AppendLine("        if (anchorTop && anchorBottom)");
            sb.AppendLine("        {");
            sb.AppendLine("            resolvedY = top;");
            sb.AppendLine("            resolvedHeight = actualParentHeight - top - bottom;");
            sb.AppendLine("        }");
            sb.AppendLine("        else if (!anchorTop && anchorBottom)");
            sb.AppendLine("        {");
            sb.AppendLine("            resolvedHeight = height;");
            sb.AppendLine("            resolvedY = actualParentHeight - bottom - resolvedHeight;");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            resolvedY = top;");
            sb.AppendLine("            resolvedHeight = height;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        return new AnchoredFrame(");
            sb.AppendLine("            resolvedX,");
            sb.AppendLine("            resolvedY,");
            sb.AppendLine("            resolvedWidth < 0 ? 0 : resolvedWidth,");
            sb.AppendLine("            resolvedHeight < 0 ? 0 : resolvedHeight);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private readonly record struct AnchorBinding(Control Control, Control? Parent, double BaseParentWidth, double BaseParentHeight, double X, double Y, double Width, double Height, bool AnchorLeft, bool AnchorTop, bool AnchorRight, bool AnchorBottom);");
            sb.AppendLine("    private readonly record struct AnchoredFrame(double X, double Y, double Width, double Height);");
        }

        if (buttonControls.Count > 0)
        {
            sb.AppendLine();
            foreach (var button in buttonControls)
            {
                var handlerName = $"{exportControlNames[button.Id]}Click";
                var buttonInteractions = exportableInteractions
                    .Where(item => item.Source.Id == button.Id
                        && string.Equals(item.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var needsAsyncButtonHandler = buttonInteractions.Any(item =>
                    string.Equals(item.Interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"    private {(needsAsyncButtonHandler ? "async " : "")}void {handlerName}(object? sender, RoutedEventArgs e)");
                sb.AppendLine("    {");

                var action = ResolveGeneratedButtonAction(button);
                if (primaryCrud is null || !TryBuildCrudButtonCall(action, primaryCrud, out var callLine))
                {
                    sb.AppendLine($"        // TODO: добавьте логику для кнопки {button.Name}.");
                }
                else
                {
                    sb.AppendLine($"        ViewModel.{callLine};");
                }

                foreach (var interaction in buttonInteractions)
                {
                    AppendGeneratedInteractionAction(sb, interaction, exportControlNames, exportControlNames[button.Id], 2, needsAsyncButtonHandler);
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        if (hasSelectionInteractions)
        {
            sb.AppendLine();
            foreach (var group in selectionInteractions.GroupBy(item => item.Source.Id))
            {
                var source = group.First().Source;
                var sourceExportName = exportControlNames[source.Id];
                sb.AppendLine($"    private void {sourceExportName}_SelectionChanged(object? sender, SelectionChangedEventArgs e)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var selectedItem = {sourceExportName}.SelectedItem;");
                sb.AppendLine("        if (selectedItem is null)");
                sb.AppendLine("        {");
                foreach (var item in group)
                    AppendGeneratedInteractionAssignment(sb, item.Interaction, item.Target, exportControlNames[item.Target.Id], "null", 3);
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine();
                foreach (var item in group)
                    AppendGeneratedInteractionAssignment(sb, item.Interaction, item.Target, exportControlNames[item.Target.Id], "selectedItem", 2);
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            AppendGeneratedInteractionHelpers(sb, includeShowMessageHelper: hasShowMessageInteractions);
        }

        var extraInteractionHandlers = exportableInteractions
            .Where(item => !string.Equals(item.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (extraInteractionHandlers.Count > 0)
            AppendGeneratedInteractionHandlers(sb, extraInteractionHandlers, exportControlNames, skipButtonClickHandlers: false);

        sb.AppendLine("}");

        if (hasViewModel)
        {
            sb.AppendLine();
            sb.AppendLine($"public partial class {viewModelClassName} : ObservableObject");
            sb.AppendLine("{");

            foreach (var textBox in textBoxControls)
            {
                var propertyName = $"{SanitizeIdentifier(textBox.Name, "TextBox")}Text";
                var backingName = ToCamelCase(propertyName);
                sb.AppendLine("    [ObservableProperty]");
                sb.AppendLine($"    private string {backingName} = string.Empty;");
                sb.AppendLine();
            }

            foreach (var context in crudContexts)
            {
                var filterFields = GetFilterableFieldsForContext(context);
                sb.AppendLine($"    [ObservableProperty]");
                sb.AppendLine($"    private string {ToCamelCase(context.SearchTextPropertyName)} = string.Empty;");
                sb.AppendLine();
                foreach (var field in filterFields)
                {
                    var filterPropertyName = GetColumnFilterPropertyName(context, field);
                    sb.AppendLine("    [ObservableProperty]");
                    sb.AppendLine($"    private string {ToCamelCase(filterPropertyName)} = string.Empty;");
                    sb.AppendLine();
                }
                sb.AppendLine($"    [ObservableProperty]");
                sb.AppendLine($"    private {context.ItemTypeName}? {ToCamelCase(context.SelectedItemPropertyName)};");
                sb.AppendLine();
                sb.AppendLine($"    [ObservableProperty]");
                sb.AppendLine($"    private {context.ItemTypeName} {ToCamelCase(context.CurrentItemPropertyName)} = new();");
                sb.AppendLine();
                sb.AppendLine($"    public ObservableCollection<{context.ItemTypeName}> {context.CollectionPropertyName} {{ get; }} = new();");
                sb.AppendLine($"    public ObservableCollection<{context.ItemTypeName}> {context.ViewCollectionPropertyName} {{ get; }} = new();");
                sb.AppendLine();
            }

            foreach (var context in sqlContexts)
            {
                var sqlConstantBase = SanitizeIdentifier($"{context.ItemTypeName}Sql", "SqlSource");
                sb.AppendLine($"    private const string {sqlConstantBase}ConnectionString = {ToVerbatimCSharpString(context.Source.SourceConnectionString)};");
                sb.AppendLine($"    private const string {sqlConstantBase}CommandText = {ToVerbatimCSharpString(BuildSqlImportCommandText(context.Source.SourceSchemaName, context.Source.SourceTableName, context.Source.SourceQuery))};");
                sb.AppendLine();
            }

            sb.AppendLine($"    public {viewModelClassName}()");
            sb.AppendLine("    {");
            foreach (var context in crudContexts)
            {
                if (IsSqlServerSource(context.Source))
                {
                    sb.AppendLine($"        {context.CurrentItemPropertyName} = new {context.ItemTypeName}();");
                }
                else
                {
                    sb.AppendLine($"        Seed{context.ItemTypeName}();");
                    sb.AppendLine($"        Apply{context.ItemTypeName}Filter();");
                }
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            if (hasSqlContexts)
            {
                sb.AppendLine("    public async Task InitializeAsync()");
                sb.AppendLine("    {");
                foreach (var context in sqlContexts)
                    sb.AppendLine($"        await Load{context.ItemTypeName}FromDatabaseAsync();");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            foreach (var context in crudContexts)
            {
                var filterFields = GetFilterableFieldsForContext(context);
                var filterMode = GetSourceFilterMode(context.Source.Id);
                sb.AppendLine($"    partial void On{context.SearchTextPropertyName}Changed(string value) => Apply{context.ItemTypeName}Filter();");
                foreach (var field in filterFields)
                    sb.AppendLine($"    partial void On{GetColumnFilterPropertyName(context, field)}Changed(string value) => Apply{context.ItemTypeName}Filter();");
                sb.AppendLine();
                sb.AppendLine($"    partial void On{context.SelectedItemPropertyName}Changed({context.ItemTypeName}? value)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (value is null)");
                sb.AppendLine("            return;");
                sb.AppendLine();
                sb.AppendLine($"        {context.CurrentItemPropertyName} = value.Clone();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void BeginCreate{context.ItemTypeName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        {context.SelectedItemPropertyName} = null;");
                sb.AppendLine($"        {context.CurrentItemPropertyName} = new {context.ItemTypeName}();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void StartEditingSelected{context.ItemTypeName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        if ({context.SelectedItemPropertyName} is null)");
                sb.AppendLine("            return;");
                sb.AppendLine();
                sb.AppendLine($"        {context.CurrentItemPropertyName} = {context.SelectedItemPropertyName}.Clone();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void Reset{context.ItemTypeName}Editor()");
                sb.AppendLine("    {");
                sb.AppendLine($"        {context.CurrentItemPropertyName} = {context.SelectedItemPropertyName}?.Clone() ?? new {context.ItemTypeName}();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void Save{context.ItemTypeName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        if ({context.SelectedItemPropertyName} is null)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var createdItem = {context.CurrentItemPropertyName}.Clone();");
                sb.AppendLine($"            {context.CollectionPropertyName}.Add(createdItem);");
                sb.AppendLine($"            {context.SelectedItemPropertyName} = createdItem;");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine($"            {context.SelectedItemPropertyName}.CopyFrom({context.CurrentItemPropertyName});");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine($"        Apply{context.ItemTypeName}Filter();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void DeleteSelected{context.ItemTypeName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        if ({context.SelectedItemPropertyName} is null)");
                sb.AppendLine("            return;");
                sb.AppendLine();
                sb.AppendLine($"        {context.CollectionPropertyName}.Remove({context.SelectedItemPropertyName});");
                sb.AppendLine($"        {context.SelectedItemPropertyName} = null;");
                sb.AppendLine($"        {context.CurrentItemPropertyName} = new {context.ItemTypeName}();");
                sb.AppendLine($"        Apply{context.ItemTypeName}Filter();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void Apply{context.ItemTypeName}Filter()");
                sb.AppendLine("    {");
                sb.AppendLine($"        IEnumerable<{context.ItemTypeName}> query = {context.CollectionPropertyName};");
                sb.AppendLine();
                sb.AppendLine($"        if (!string.IsNullOrWhiteSpace({context.SearchTextPropertyName}))");
                sb.AppendLine("        {");
                sb.AppendLine($"            query = query.Where(item => {BuildSearchPredicate(context)});");
                sb.AppendLine("        }");
                foreach (var field in filterFields)
                {
                    var filterPropertyName = GetColumnFilterPropertyName(context, field);
                    var itemPropertyName = SanitizeIdentifier(field.Path, "Field");
                    sb.AppendLine();
                    sb.AppendLine($"        if (!string.IsNullOrWhiteSpace({filterPropertyName}))");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            query = query.Where(item => MatchesColumnFilter(item.{itemPropertyName}, {filterPropertyName}, \"{filterMode}\"));");
                    sb.AppendLine("        }");
                }
                sb.AppendLine();
                sb.AppendLine($"        ReplaceItems({context.ViewCollectionPropertyName}, query);");
                sb.AppendLine("    }");
                sb.AppendLine();

                if (IsSqlServerSource(context.Source))
                {
                    var sqlConstantBase = SanitizeIdentifier($"{context.ItemTypeName}Sql", "SqlSource");
                    sb.AppendLine($"    public async Task Load{context.ItemTypeName}FromDatabaseAsync()");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        var items = new List<{context.ItemTypeName}>();");
                    sb.AppendLine($"        using var connection = new SqlConnection({sqlConstantBase}ConnectionString);");
                    sb.AppendLine("        await connection.OpenAsync();");
                    sb.AppendLine();
                    sb.AppendLine("        using var command = connection.CreateCommand();");
                    sb.AppendLine($"        command.CommandText = {sqlConstantBase}CommandText;");
                    sb.AppendLine("        command.CommandType = CommandType.Text;");
                    sb.AppendLine("        command.CommandTimeout = 15;");
                    sb.AppendLine();
                    sb.AppendLine("        using var reader = await command.ExecuteReaderAsync();");
                    sb.AppendLine("        while (await reader.ReadAsync())");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            items.Add(new {context.ItemTypeName}");
                    sb.AppendLine("            {");
                    AppendGeneratedSqlAssignments(sb, context, 4);
                    sb.AppendLine("            });");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                    sb.AppendLine($"        ReplaceItems({context.CollectionPropertyName}, items);");
                    sb.AppendLine($"        {context.SelectedItemPropertyName} = null;");
                    sb.AppendLine($"        {context.CurrentItemPropertyName} = new {context.ItemTypeName}();");
                    sb.AppendLine($"        Apply{context.ItemTypeName}Filter();");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"    private void Seed{context.ItemTypeName}()");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        {context.CollectionPropertyName}.Add(new {context.ItemTypeName}");
                    sb.AppendLine("        {");
                    AppendSeedAssignments(sb, context, 3, variantIndex: 0);
                    sb.AppendLine("        });");
                    sb.AppendLine();
                    sb.AppendLine($"        {context.CollectionPropertyName}.Add(new {context.ItemTypeName}");
                    sb.AppendLine("        {");
                    AppendSeedAssignments(sb, context, 3, variantIndex: 1);
                    sb.AppendLine("        });");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("    private static bool ContainsText(object? value, string query)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (value is null)");
            sb.AppendLine("            return false;");
            sb.AppendLine();
            sb.AppendLine("        var text = Convert.ToString(value, CultureInfo.InvariantCulture);");
            sb.AppendLine("        return !string.IsNullOrWhiteSpace(text) && text.Contains(query, StringComparison.OrdinalIgnoreCase);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static bool MatchesColumnFilter(object? value, string query, string mode)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (value is null)");
            sb.AppendLine("            return false;");
            sb.AppendLine();
            sb.AppendLine("        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;");
            sb.AppendLine("        return mode switch");
            sb.AppendLine("        {");
            sb.AppendLine("            \"StartsWith\" => text.StartsWith(query, StringComparison.OrdinalIgnoreCase),");
            sb.AppendLine("            \"Equals\" => string.Equals(text, query, StringComparison.OrdinalIgnoreCase),");
            sb.AppendLine("            _ => text.Contains(query, StringComparison.OrdinalIgnoreCase)");
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)");
            sb.AppendLine("    {");
            sb.AppendLine("        target.Clear();");
            sb.AppendLine("        foreach (var item in source)");
            sb.AppendLine("            target.Add(item);");
            sb.AppendLine("    }");

            if (hasSqlContexts)
            {
                sb.AppendLine();
                sb.AppendLine("    private static string ReadString(SqlDataReader reader, string columnName)");
                sb.AppendLine("    {");
                sb.AppendLine("        var value = reader[columnName];");
                sb.AppendLine("        return value is null || value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    private static byte[] ReadBytes(SqlDataReader reader, string columnName)");
                sb.AppendLine("    {");
                sb.AppendLine("        var value = reader[columnName];");
                sb.AppendLine("        return value is byte[] bytes ? bytes : Array.Empty<byte>();");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    private static T ReadValue<T>(SqlDataReader reader, string columnName)");
                sb.AppendLine("    {");
                sb.AppendLine("        var value = reader[columnName];");
                sb.AppendLine("        if (value is null || value == DBNull.Value)");
                sb.AppendLine("            return default!;");
                sb.AppendLine();
                sb.AppendLine("        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);");
                sb.AppendLine("        if (targetType == typeof(Guid))");
                sb.AppendLine("            return (T)(object)(value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? Guid.Empty.ToString()));");
                sb.AppendLine("        if (targetType == typeof(DateTimeOffset))");
                sb.AppendLine("            return (T)(object)(value is DateTimeOffset dto ? dto : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? DateTimeOffset.MinValue.ToString(\"O\", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));");
                sb.AppendLine("        if (targetType == typeof(TimeSpan))");
                sb.AppendLine("            return (T)(object)(value is TimeSpan timeSpan ? timeSpan : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? \"00:00:00\", CultureInfo.InvariantCulture));");
                sb.AppendLine("        if (targetType.IsEnum)");
                sb.AppendLine("            return (T)Enum.Parse(targetType, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, ignoreCase: true);");
                sb.AppendLine();
                sb.AppendLine("        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);");
                sb.AppendLine("    }");
            }
            sb.AppendLine("}");

            foreach (var context in crudContexts)
            {
                sb.AppendLine();
                sb.AppendLine($"public partial class {context.ItemTypeName} : ObservableObject");
                sb.AppendLine("{");

                foreach (var field in context.Fields)
                {
                    var property = SanitizeIdentifier(field.Path, "Field");
                    var typeName = NormalizeCSharpType(field.TypeName);
                    sb.AppendLine("    [ObservableProperty]");
                    sb.AppendLine($"    private {typeName} {ToCamelCase(property)};");
                    sb.AppendLine();
                }

                sb.AppendLine($"    public {context.ItemTypeName} Clone()");
                sb.AppendLine("    {");
                sb.AppendLine($"        return new {context.ItemTypeName}");
                sb.AppendLine("        {");
                AppendCloneAssignments(sb, context, 3);
                sb.AppendLine("        };");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public void CopyFrom({context.ItemTypeName} source)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (source is null)");
                sb.AppendLine("            return;");
                sb.AppendLine();
                foreach (var field in context.Fields)
                {
                    var property = SanitizeIdentifier(field.Path, "Field");
                    sb.AppendLine($"        {property} = source.{property};");
                }
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private DesignControlModel CreateDefaultControl(string type)
    {
        var descriptor = _registry.GetRequiredControl(type);
        var services = new DesignerServiceProvider();
        var context = new DescriptorContext(FormTheme, BindingMetadataMapper.ToMetadata(BindingSources), services);
        var definition = descriptor.CreateDefaultDefinition(context);
        var model = DesignControlDefinitionMapper.ToRuntimeModel(definition);
        model.Type = type;
        model.DescriptorId = string.IsNullOrWhiteSpace(model.DescriptorId) ? descriptor.TypeKey : model.DescriptorId;
        model.PluginId = string.IsNullOrWhiteSpace(model.PluginId) ? definition.PluginId : model.PluginId;
        model.PluginVersion = string.IsNullOrWhiteSpace(model.PluginVersion) ? definition.PluginVersion : model.PluginVersion;

        return model;
    }

    private string GetUniqueControlName(string type)
    {
        var baseName = type switch
        {
            DesignerControlTypes.Group => "Group",
            DesignerControlTypes.StackLayout => "StackPanel",
            DesignerControlTypes.LayoutGrid => "Grid",
            DesignerControlTypes.FlexLayout => "WrapPanel",
            _ => type
        };

        if (baseName.Contains('.', StringComparison.Ordinal))
            baseName = baseName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? baseName;

        baseName = new string(baseName.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Control";

        if (char.IsDigit(baseName[0]))
            baseName = "Control" + baseName;

        var index = 1;
        var name = $"{baseName}{index}";
        while (Controls.Any(control => control.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            name = $"{baseName}{index}";
        }

        return name;
    }

    private string GetUniqueBindingSourceName(string baseName, string? excludeId = null)
    {
        if (BindingSources.All(source => source.Id == excludeId || !source.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        var index = 1;
        var candidate = $"{baseName}{index}";
        while (BindingSources.Any(source => source.Id != excludeId && source.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            candidate = $"{baseName}{index}";
        }

        return candidate;
    }

    private string GetUniqueTemplateName(string baseName, string? excludeId = null)
    {
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? "Пользовательский шаблон"
            : baseName.Trim();

        if (ReusableTemplates.All(template => template.Id == excludeId || !template.Name.Equals(normalizedBaseName, StringComparison.OrdinalIgnoreCase)))
            return normalizedBaseName;

        var index = 2;
        var candidate = $"{normalizedBaseName} {index}";
        while (ReusableTemplates.Any(template => template.Id != excludeId && template.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            candidate = $"{normalizedBaseName} {index}";
        }

        return candidate;
    }

    private BindingSourceDiscoveryResult DiscoverBindingSourcesFromAssembly(string assemblyPath)
    {
        var result = new BindingSourceDiscoveryResult();
        var request = new BindingImportRequest
        {
            AssemblyPath = assemblyPath
        };

        foreach (var provider in _registry.GetBindingProviders().Where(provider => provider.CanHandle(request)))
        {
            result.HasHandledProvider = true;

            try
            {
                var discovered = provider.DiscoverSources(request);
                result.Diagnostics.Add(discovered.Diagnostics);

                foreach (var source in discovered.Sources)
                    result.Sources.Add(BindingMetadataMapper.ToRuntimeModel(source));
            }
            catch (Exception ex)
            {
                result.ProviderErrors.Add($"{provider.Id}: {ex.Message}");
            }
        }

        if (!result.HasHandledProvider)
            result.ProviderErrors.Add("Для этой DLL не найден подходящий механизм импорта.");

        return result;
    }

    private static string BuildBindingImportFailureStatus(string assemblyPath, BindingSourceDiscoveryResult discoveryResult)
    {
        var fileName = Path.GetFileName(assemblyPath);
        var diagnostics = GetPrimaryBindingImportDiagnostics(discoveryResult);

        if (diagnostics is null)
        {
            if (discoveryResult.ProviderErrors.Count > 0)
                return $"Не удалось проанализировать {fileName}. {discoveryResult.ProviderErrors[0]}";

            return $"В {fileName} не найдено подходящих сущностей.";
        }

        var parts = new List<string>
        {
            $"В {fileName} не найдено подходящих сущностей."
        };

        var metrics = new List<string>();
        if (diagnostics.ScannedTypeCount > 0)
            metrics.Add($"просмотрено типов: {diagnostics.ScannedTypeCount}");

        if (diagnostics.IgnoredTypeCount > 0)
            metrics.Add($"неподходящих: {diagnostics.IgnoredTypeCount}");

        if (diagnostics.InfrastructureTypeCount > 0)
            metrics.Add($"инфраструктурных: {diagnostics.InfrastructureTypeCount}");

        if (diagnostics.TableAttributedTypeCount > 0)
            metrics.Add($"с [Table]: {diagnostics.TableAttributedTypeCount}");

        if (diagnostics.ColumnAttributedTypeCount > 0)
            metrics.Add($"с [Column]: {diagnostics.ColumnAttributedTypeCount}");

        if (diagnostics.LoaderExceptionCount > 0)
            metrics.Add($"не загрузилось типов: {diagnostics.LoaderExceptionCount}");

        if (metrics.Count > 0)
            parts.Add(string.Join(", ", metrics) + ".");

        if (diagnostics.LoaderExceptionCount > 0)
            parts.Add("DBML-сущности читаются напрямую из выбранной DLL; зависимые DLL нужны только для редких fallback-сценариев нестандартных сборок.");

        var candidateNames = diagnostics.CandidateTypeNames
            .Select(GetShortTypeDisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToList();

        if (candidateNames.Count > 0)
        {
            parts.Add($"Похожие кандидаты: {string.Join(", ", candidateNames)}.");
        }
        else
        {
            var infrastructureNames = diagnostics.InfrastructureTypeNames
                .Select(GetShortTypeDisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(3)
                .ToList();

            if (infrastructureNames.Count > 0)
                parts.Add($"Похоже, DLL в основном содержит инфраструктуру: {string.Join(", ", infrastructureNames)}.");
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.FailureMessage))
            parts.Add($"Ошибка reflection: {diagnostics.FailureMessage}");

        if (discoveryResult.ProviderErrors.Count > 0)
            parts.Add($"Дополнительно: {discoveryResult.ProviderErrors[0]}.");

        return string.Join(" ", parts);
    }

    private static string BuildBindingImportSuccessStatus(string assemblyPath, int importedCount, BindingSourceDiscoveryResult discoveryResult)
    {
        var fileName = Path.GetFileName(assemblyPath);
        var diagnostics = GetPrimaryBindingImportDiagnostics(discoveryResult);
        var status = $"Импортировано источников: {importedCount} из {fileName}.";

        if (diagnostics is null)
            return status;

        var notes = new List<string>();
        if (diagnostics.FailedCandidateTypeCount > 0)
            notes.Add($"пропущено проблемных сущностей: {diagnostics.FailedCandidateTypeCount}");

        if (diagnostics.LoaderExceptionCount > 0)
            notes.Add($"не загрузилось типов: {diagnostics.LoaderExceptionCount}");

        return notes.Count == 0
            ? status
            : $"{status} {string.Join(", ", notes)}.";
    }

    private static BindingImportDiagnostics GetPrimaryBindingImportDiagnostics(BindingSourceDiscoveryResult discoveryResult)
    {
        return discoveryResult.Diagnostics
            .OrderByDescending(item => item.ImportedSourceCount)
            .ThenByDescending(item => item.CandidateTypeCount)
            .ThenByDescending(item => item.ScannedTypeCount)
            .FirstOrDefault() ?? new BindingImportDiagnostics();
    }

    private static string GetShortTypeDisplayName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return string.Empty;

        var lastDotIndex = typeName.LastIndexOf('.');
        return lastDotIndex >= 0 && lastDotIndex < typeName.Length - 1
            ? typeName.Substring(lastDotIndex + 1)
            : typeName;
    }

    private BindingSourceModel MergeImportedBindingSource(BindingSourceModel importedSource)
    {
        var existing = BindingSources.FirstOrDefault(source =>
            source.SourceKind.Equals("Assembly", StringComparison.OrdinalIgnoreCase)
            && source.SourceAssemblyPath.Equals(importedSource.SourceAssemblyPath, StringComparison.OrdinalIgnoreCase)
            && source.SourceTypeFullName.Equals(importedSource.SourceTypeFullName, StringComparison.Ordinal));

        if (existing is null)
        {
            importedSource.Name = GetUniqueBindingSourceName(importedSource.Name);
            BindingSources.Add(importedSource);
            return importedSource;
        }

        var existingFieldsByPath = existing.Fields.ToDictionary(field => field.Path, StringComparer.OrdinalIgnoreCase);
        var preservePath = existing.Path;
        var preserveName = existing.Name;

        existing.Name = GetUniqueBindingSourceName(preserveName, existing.Id);
        existing.Path = string.IsNullOrWhiteSpace(preservePath) ? importedSource.Path : preservePath;
        existing.ItemTypeName = importedSource.ItemTypeName;
        existing.Description = importedSource.Description;
        existing.SourceKind = importedSource.SourceKind;
        existing.SourceAssemblyPath = importedSource.SourceAssemblyPath;
        existing.SourceTypeFullName = importedSource.SourceTypeFullName;
        existing.SourceTableName = importedSource.SourceTableName;

        existing.Fields.Clear();
        foreach (var importedField in importedSource.Fields)
        {
            if (existingFieldsByPath.TryGetValue(importedField.Path, out var previousField))
            {
                importedField.Header = string.IsNullOrWhiteSpace(previousField.Header) ? importedField.Header : previousField.Header;
                importedField.Width = string.IsNullOrWhiteSpace(previousField.Width) ? importedField.Width : previousField.Width;
                importedField.IsVisible = previousField.IsVisible;
                importedField.IsSortable = previousField.IsSortable;
                importedField.AllowSort = previousField.AllowSort && previousField.IsSortable;
                importedField.SortDirection = previousField.SortDirection;
                importedField.SortOrder = previousField.SortOrder;
                importedField.GroupOrder = previousField.GroupOrder;
                importedField.HeaderAlignment = previousField.HeaderAlignment;
                importedField.CellAlignment = previousField.CellAlignment;
                importedField.FormatString = previousField.FormatString;
                importedField.NullText = previousField.NullText;
                importedField.TextTrimming = previousField.TextTrimming;
                importedField.TextWrapping = previousField.TextWrapping;
                importedField.MaxLines = previousField.MaxLines;
                importedField.MinWidth = previousField.MinWidth;
                importedField.MaxWidth = previousField.MaxWidth;
                importedField.AllowResize = previousField.AllowResize;
                importedField.AllowFilter = previousField.AllowFilter;
                importedField.VisibleIndex = previousField.VisibleIndex;
                importedField.SummaryType = previousField.SummaryType;
                importedField.SummaryFormat = previousField.SummaryFormat;
            }

            existing.Fields.Add(importedField);
        }

        return existing;
    }

    private void ApplyDatabaseSourceToSelectedBindingSource(BindingSourceModel importedSource)
    {
        if (SelectedBindingSource is null)
            return;

        // Если поле уже настраивали вручную, стараемся сохранить его подпись,
        // сортировку и признак видимости при повторном чтении схемы из БД.
        var existing = SelectedBindingSource;
        var existingFieldsByPath = existing.Fields.ToDictionary(field => field.Path, StringComparer.OrdinalIgnoreCase);
        var preserveName = existing.Name;
        var preservePath = existing.Path;
        var preserveItemTypeName = existing.ItemTypeName;
        var preserveDescription = existing.Description;

        DetachBindingSource(existing);

        try
        {
            existing.Name = IsDefaultBindingSourceName(preserveName) ? importedSource.Name : preserveName;
            existing.Path = IsDefaultBindingSourcePath(preservePath, preserveName) ? importedSource.Path : preservePath;
            existing.ItemTypeName = IsDefaultItemTypeName(preserveItemTypeName) ? importedSource.ItemTypeName : preserveItemTypeName;
            existing.Description = IsDefaultBindingSourceDescription(preserveDescription) ? importedSource.Description : preserveDescription;
            existing.SourceKind = "SqlServer";
            existing.SourceAssemblyPath = "";
            existing.SourceTypeFullName = importedSource.SourceTypeFullName;
            existing.SourceTableName = importedSource.SourceTableName;
            existing.SourceConnectionString = importedSource.SourceConnectionString;
            existing.SourceSchemaName = importedSource.SourceSchemaName;
            existing.SourceQuery = importedSource.SourceQuery;

            existing.Fields.Clear();
            foreach (var importedField in importedSource.Fields)
            {
                if (existingFieldsByPath.TryGetValue(importedField.Path, out var previousField))
                {
                    importedField.Header = string.IsNullOrWhiteSpace(previousField.Header) ? importedField.Header : previousField.Header;
                    importedField.Width = string.IsNullOrWhiteSpace(previousField.Width) ? importedField.Width : previousField.Width;
                    importedField.IsVisible = previousField.IsVisible;
                    importedField.IsSortable = previousField.IsSortable;
                    importedField.AllowSort = previousField.AllowSort && previousField.IsSortable;
                    importedField.SortDirection = previousField.SortDirection;
                    importedField.SortOrder = previousField.SortOrder;
                    importedField.GroupOrder = previousField.GroupOrder;
                    importedField.HeaderAlignment = previousField.HeaderAlignment;
                    importedField.CellAlignment = previousField.CellAlignment;
                    importedField.FormatString = previousField.FormatString;
                    importedField.NullText = previousField.NullText;
                    importedField.TextTrimming = previousField.TextTrimming;
                    importedField.TextWrapping = previousField.TextWrapping;
                    importedField.MaxLines = previousField.MaxLines;
                    importedField.MinWidth = previousField.MinWidth;
                    importedField.MaxWidth = previousField.MaxWidth;
                    importedField.AllowResize = previousField.AllowResize;
                    importedField.AllowFilter = previousField.AllowFilter;
                    importedField.VisibleIndex = previousField.VisibleIndex;
                    importedField.SummaryType = previousField.SummaryType;
                    importedField.SummaryFormat = previousField.SummaryFormat;
                }

                existing.Fields.Add(importedField);
            }
        }
        finally
        {
            AttachBindingSource(existing);
        }

        OnPropertyChanged(nameof(HasSelectedBindingSourceImportMetadata));
        OnPropertyChanged(nameof(SelectedBindingSourceImportSummary));
        OnPropertyChanged(nameof(SelectedDataGridBindingSummary));
        NotifyDesignerStateChanged();
    }

    private async Task<BindingSourceModel> CreateBindingSourceFromDatabaseAsync(BindingSourceModel template)
    {
        if (string.IsNullOrWhiteSpace(template.SourceConnectionString))
            throw new InvalidOperationException("Введите строку подключения SQL Server.");

        if (string.IsNullOrWhiteSpace(template.SourceQuery) && string.IsNullOrWhiteSpace(template.SourceTableName))
            throw new InvalidOperationException("Укажите таблицу или SQL-запрос.");

        // В дизайнере нам нужны только колонki и несколько образцов значений,
        // поэтому для чтения используется укороченный sample-запрос.
        var schemaName = NormalizeSqlSchemaName(template.SourceSchemaName);
        var tableName = NormalizeSqlTableName(template.SourceTableName);
        var objectBaseName = !string.IsNullOrWhiteSpace(tableName)
            ? ExtractSqlObjectName(tableName)
            : "SqlQuery";
        var sourceName = $"{SanitizeIdentifier(objectBaseName, "Sql")}Source";
        var itemTypeName = $"{SanitizeIdentifier(TrimTrailingPlural(objectBaseName), "SqlRow")}Row";
        var path = $"{SanitizeIdentifier(objectBaseName, "Sql")}Items";
        var source = new BindingSourceModel
        {
            Name = sourceName,
            Path = path,
            ItemTypeName = itemTypeName,
            Description = BuildDatabaseSourceDescription(template.SourceConnectionString, schemaName, tableName, template.SourceQuery),
            SourceKind = "SqlServer",
            SourceTypeFullName = !string.IsNullOrWhiteSpace(template.SourceQuery) ? "SqlQuery" : $"{schemaName}.{tableName}",
            SourceTableName = tableName,
            SourceConnectionString = template.SourceConnectionString,
            SourceSchemaName = schemaName,
            SourceQuery = template.SourceQuery?.Trim() ?? ""
        };

        var connectionResult = await OpenSqlConnectionForDesignerAsync(template.SourceConnectionString);
        source.SourceConnectionString = connectionResult.EffectiveConnectionString;

        await using var connection = connectionResult.Connection;

        using var command = connection.CreateCommand();
        command.CommandText = BuildSqlSampleImportCommandText(schemaName, tableName, template.SourceQuery, sampleRowCount: 3);
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 15;

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var schema = reader.GetColumnSchema();
        if (schema.Count == 0)
            throw new InvalidOperationException("Запрос не вернул ни одной колонки.");

        var sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rowsRead = 0;

        while (rowsRead < 3 && await reader.ReadAsync())
        {
            for (var columnIndex = 0; columnIndex < schema.Count; columnIndex++)
            {
                var columnName = schema[columnIndex].ColumnName ?? $"Column{columnIndex + 1}";
                if (sampleValues.ContainsKey(columnName) || reader.IsDBNull(columnIndex))
                    continue;

                sampleValues[columnName] = ConvertDatabaseValueToSampleText(reader.GetValue(columnIndex));
            }

            rowsRead++;
        }

        for (var columnIndex = 0; columnIndex < schema.Count; columnIndex++)
        {
            var column = schema[columnIndex];
            source.Fields.Add(CreateBindingFieldFromDatabaseColumn(column, sampleValues, columnIndex));
        }

        return source;
    }

    private BindingSourceModel CreateBindingSourceFromType(Type type, string assemblyPath)
    {
        var tableName = GetTableName(type);
        var baseName = type.Name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? type.Name : $"{type.Name}s";
        var source = new BindingSourceModel
        {
            Name = baseName,
            Path = baseName,
            ItemTypeName = type.Name,
            Description = $"Импортировано из {Path.GetFileName(assemblyPath)}",
            SourceKind = "Assembly",
            SourceAssemblyPath = assemblyPath,
            SourceTypeFullName = type.FullName ?? type.Name,
            SourceTableName = tableName
        };

        foreach (var property in GetBindableProperties(type))
            source.Fields.Add(CreateBindingFieldFromProperty(property));

        return source;
    }

    private static BindingFieldModel CreateBindingFieldFromDatabaseColumn(DbColumn column, IReadOnlyDictionary<string, string> sampleValues, int columnIndex)
    {
        var columnName = string.IsNullOrWhiteSpace(column.ColumnName?.ToString())
            ? $"Column{columnIndex + 1}"
            : column.ColumnName.ToString()!;
        var dataType = column.DataType as Type ?? typeof(string);

        return new BindingFieldModel
        {
            Header = columnName,
            Path = columnName,
            SampleValue = sampleValues.TryGetValue(columnName, out var sample)
                ? sample
                : GetSampleValue(dataType),
            Width = IsCompactColumnType(dataType) ? "120" : "*",
            TypeName = GetFriendlyTypeName(dataType),
            IsVisible = true,
            IsSortable = IsSortablePropertyType(dataType),
            AllowSort = IsSortablePropertyType(dataType),
            SortDirection = BindingFieldModel.SortDirectionNone,
            SortOrder = -1,
            GroupOrder = -1,
            CellAlignment = IsCompactColumnType(dataType) ? BindingFieldModel.AlignmentCenter : BindingFieldModel.AlignmentLeft,
            SummaryType = BindingFieldModel.SummaryTypeNone,
            SummaryFormat = ""
        };
    }

    private static string BuildSqlImportCommandText(string schemaName, string tableName, string? sourceQuery)
    {
        // Это запрос для уже сгенерированной формы: здесь данные читаются полностью, без TOP.
        if (!string.IsNullOrWhiteSpace(sourceQuery))
        {
            return sourceQuery.Trim().TrimEnd(';');
        }

        var objectReference = BuildSqlObjectReference(schemaName, tableName);
        return $"SELECT * FROM {objectReference}";
    }

    private static string BuildSqlSampleImportCommandText(string schemaName, string tableName, string? sourceQuery, int sampleRowCount)
    {
        // Отдельная версия для дизайнера и импорта схемы:
        // ограничиваем выборку, чтобы быстро получить структуру таблицы и примеры строк.
        var boundedRowCount = Math.Max(1, sampleRowCount);
        if (!string.IsNullOrWhiteSpace(sourceQuery))
        {
            var query = sourceQuery.Trim().TrimEnd(';');
            return $"SELECT TOP ({boundedRowCount}) * FROM ({query}) AS DesignerSource";
        }

        var objectReference = BuildSqlObjectReference(schemaName, tableName);
        return $"SELECT TOP ({boundedRowCount}) * FROM {objectReference}";
    }

    private static string BuildSqlObjectReference(string schemaName, string tableName)
    {
        var normalizedSchema = NormalizeSqlSchemaName(schemaName);
        var normalizedTable = NormalizeSqlTableName(tableName);

        if (normalizedTable.Contains('.', StringComparison.Ordinal))
        {
            var parts = normalizedTable.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                normalizedSchema = parts[^2];
                normalizedTable = parts[^1];
            }
        }

        return $"{QuoteSqlIdentifier(normalizedSchema)}.{QuoteSqlIdentifier(normalizedTable)}";
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        var normalized = string.IsNullOrWhiteSpace(identifier) ? "dbo" : identifier.Trim().Trim('[', ']');
        return $"[{normalized.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string NormalizeSqlSchemaName(string? schemaName)
    {
        return string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName.Trim().Trim('[', ']');
    }

    private static string NormalizeSqlTableName(string? tableName)
    {
        return string.IsNullOrWhiteSpace(tableName) ? "" : tableName.Trim().Trim('[', ']');
    }

    private static string ExtractSqlObjectName(string tableName)
    {
        var normalized = NormalizeSqlTableName(tableName);
        if (string.IsNullOrWhiteSpace(normalized))
            return "Sql";

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? normalized : parts[^1];
    }

    private static string TrimTrailingPlural(string value)
    {
        return value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && value.Length > 1
            ? value[..^1]
            : value;
    }

    private static string BuildDatabaseSourceDescription(string connectionString, string schemaName, string tableName, string? sourceQuery)
    {
        var sourceLabel = BuildSqlConnectionSummary(connectionString);
        if (!string.IsNullOrWhiteSpace(sourceQuery))
            return $"SQL Server запрос из {sourceLabel}";

        return $"SQL Server таблица {NormalizeSqlSchemaName(schemaName)}.{NormalizeSqlTableName(tableName)} из {sourceLabel}";
    }

    private static async Task<(SqlConnection Connection, string EffectiveConnectionString, bool UsedCertificateFallback)> OpenSqlConnectionForDesignerAsync(string connectionString)
    {
        var primaryConnection = new SqlConnection(connectionString);

        try
        {
            await primaryConnection.OpenAsync();
            return (primaryConnection, connectionString, false);
        }
        catch (Exception ex) when (ShouldRetryWithTrustedCertificate(connectionString, ex))
        {
            await primaryConnection.DisposeAsync();

            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                TrustServerCertificate = true
            };

            var retryConnection = new SqlConnection(builder.ConnectionString);
            await retryConnection.OpenAsync();
            return (retryConnection, builder.ConnectionString, true);
        }
    }

    private static bool ShouldRetryWithTrustedCertificate(string connectionString, Exception ex)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (builder.TrustServerCertificate)
                return false;
        }
        catch
        {
            return false;
        }

        var combinedMessage = string.Join(" ", EnumerateExceptionMessages(ex))
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(combinedMessage))
            return false;

        var mentionsCertificate = combinedMessage.Contains("certificate", StringComparison.Ordinal)
            || combinedMessage.Contains("сертификат", StringComparison.Ordinal)
            || combinedMessage.Contains("цепочк", StringComparison.Ordinal);
        var mentionsTrustFailure = combinedMessage.Contains("not trusted", StringComparison.Ordinal)
            || combinedMessage.Contains("не довер", StringComparison.Ordinal)
            || combinedMessage.Contains("ssl", StringComparison.Ordinal);

        return mentionsCertificate && mentionsTrustFailure;
    }

    private static IEnumerable<string> EnumerateExceptionMessages(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
                yield return current.Message;
        }
    }

    private static string BuildSqlConnectionSummary(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "SQL Server";

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var server = string.IsNullOrWhiteSpace(builder.DataSource) ? "SQL Server" : builder.DataSource;
            var database = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "текущая база" : builder.InitialCatalog;
            return $"{server} / {database}";
        }
        catch
        {
            return "SQL Server";
        }
    }

    private static bool IsDefaultBindingSourceName(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            || name.StartsWith("BindingSource", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Source", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultBindingSourcePath(string? path, string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        return path.Equals(sourceName ?? "", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Items", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("BindingSource", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultItemTypeName(string? itemTypeName)
    {
        return string.IsNullOrWhiteSpace(itemTypeName)
            || itemTypeName.Equals("RowItem", StringComparison.OrdinalIgnoreCase)
            || itemTypeName.Equals("ItemRow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultBindingSourceDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            || description.Contains("Пример списка", StringComparison.OrdinalIgnoreCase)
            || description.Contains("дизайнер", StringComparison.OrdinalIgnoreCase);
    }

    private static string ConvertDatabaseValueToSampleText(object value)
    {
        return value switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
            byte[] bytes => $"<байты {bytes.Length}>",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IEnumerable<PropertyInfo> GetBindableProperties(Type type)
    {
        return GetPublicInstanceProperties(type)
            .Where(CanReadPropertySafely)
            .Where(property => !HasAttribute(property, "System.Data.Linq.Mapping.AssociationAttribute"))
            .Where(IsBindableScalarProperty)
            .OrderBy(property => property.Name);
    }

    private static bool IsBindableEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || type.IsNested)
            return false;

        if (HasAttribute(type, "System.Data.Linq.Mapping.TableAttribute"))
            return true;

        var properties = GetPublicInstanceProperties(type);
        var columnCount = properties.Count(property => HasAttribute(property, "System.Data.Linq.Mapping.ColumnAttribute"));
        if (columnCount > 0)
            return true;

        return properties.Count(property => CanReadPropertySafely(property) && IsBindableScalarProperty(property)) >= 2;
    }

    private static string GetTableName(MemberInfo typeOrProperty)
    {
        var tableAttribute = SafeGetCustomAttributes(typeOrProperty)
            .FirstOrDefault(attribute => string.Equals(attribute.AttributeType.FullName, "System.Data.Linq.Mapping.TableAttribute", StringComparison.Ordinal));

        if (tableAttribute is null)
            return string.Empty;

        var namedArgument = tableAttribute.NamedArguments
            .FirstOrDefault(argument => string.Equals(argument.MemberName, "Name", StringComparison.Ordinal));
        if (namedArgument.TypedValue.Value is string namedValue && !string.IsNullOrWhiteSpace(namedValue))
            return namedValue;

        if (tableAttribute.ConstructorArguments.Count > 0
            && tableAttribute.ConstructorArguments[0].Value is string constructorValue
            && !string.IsNullOrWhiteSpace(constructorValue))
        {
            return constructorValue;
        }

        return string.Empty;
    }

    private static bool HasAttribute(MemberInfo member, string attributeFullName)
    {
        return SafeGetCustomAttributes(member)
            .Any(attribute => string.Equals(attribute.AttributeType.FullName, attributeFullName, StringComparison.Ordinal));
    }

    private static BindingFieldModel CreateBindingFieldFromProperty(PropertyInfo property)
    {
        var propertyType = GetBindablePropertyType(property);

        return new BindingFieldModel
        {
            Header = property.Name,
            Path = property.Name,
            SampleValue = GetSampleValue(propertyType),
            Width = IsCompactColumnType(propertyType) ? "120" : "*",
            TypeName = GetFriendlyTypeName(propertyType),
            IsVisible = true,
            IsSortable = IsSortablePropertyType(propertyType),
            AllowSort = IsSortablePropertyType(propertyType),
            SortDirection = BindingFieldModel.SortDirectionNone,
            SortOrder = -1,
            GroupOrder = -1,
            CellAlignment = IsCompactColumnType(propertyType) ? BindingFieldModel.AlignmentCenter : BindingFieldModel.AlignmentLeft,
            SummaryType = BindingFieldModel.SummaryTypeNone,
            SummaryFormat = ""
        };
    }

    private static IEnumerable<Type> GetLoadableExportedTypes(Assembly assembly)
    {
        // У некоторых legacy-сборок часть типов может не загрузиться из-за отсутствующих зависимостей.
        // Вместо падения всего импорта берем только те типы, которые удалось открыть.
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static IReadOnlyList<PropertyInfo> GetPublicInstanceProperties(Type type)
    {
        try
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            return Array.Empty<PropertyInfo>();
        }
    }

    private static IEnumerable<CustomAttributeData> SafeGetCustomAttributes(MemberInfo member)
    {
        try
        {
            return CustomAttributeData.GetCustomAttributes(member);
        }
        catch
        {
            return Array.Empty<CustomAttributeData>();
        }
    }

    private static bool CanReadPropertySafely(PropertyInfo property)
    {
        try
        {
            return property.CanRead && property.GetMethod?.GetParameters().Length == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBindableScalarProperty(PropertyInfo property)
    {
        try
        {
            return IsScalarPropertyType(GetBindablePropertyType(property));
        }
        catch
        {
            return false;
        }
    }

    private static Type GetBindablePropertyType(PropertyInfo property)
    {
        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return string.Equals(propertyType.FullName, "System.Data.Linq.Binary", StringComparison.Ordinal)
            ? typeof(byte[])
            : propertyType;
    }

    private static bool IsScalarPropertyType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum || type.IsPrimitive)
            return true;

        return type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(byte[]);
    }

    private static bool IsSortablePropertyType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type != typeof(byte[]);
    }

    private static bool IsCompactColumnType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    private static string GetSampleValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
            return "Текст";

        if (type == typeof(bool))
            return "True";

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return "2026-04-12";

        if (type == typeof(TimeSpan))
            return "01:30:00";

        if (type == typeof(Guid))
            return "3F2504E0-4F89-41D3-9A0C-0305E82C3301";

        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            return "123.45";

        if (type == typeof(byte[]))
            return "<данные>";

        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            return names.FirstOrDefault() ?? type.Name;
        }

        return "123";
    }

    private static string GetFriendlyTypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(int))
            return "int";
        if (type == typeof(long))
            return "long";
        if (type == typeof(short))
            return "short";
        if (type == typeof(decimal))
            return "decimal";
        if (type == typeof(double))
            return "double";
        if (type == typeof(float))
            return "float";
        if (type == typeof(bool))
            return "bool";
        if (type == typeof(string))
            return "string";
        if (type == typeof(DateTime))
            return "DateTime";
        if (type == typeof(DateTimeOffset))
            return "DateTimeOffset";
        if (type == typeof(TimeSpan))
            return "TimeSpan";
        if (type == typeof(Guid))
            return "Guid";
        if (type == typeof(byte[]))
            return "byte[]";

        return type.Name;
    }

    private void AppendControlXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var writer = new StringBuilderXamlWriter(sb);
        var controlNodes = Controls.ToDictionary(controlModel => controlModel.Id, controlModel => (IDesignControlNode)new DesignControlNodeAdapter(controlModel), StringComparer.OrdinalIgnoreCase);
        var bindingSources = BindingMetadataMapper.ToMetadataMap(BindingSources);
        var services = new DesignerServiceProvider()
            .Add<IBuiltInXamlBridge>(new BuiltInXamlBridge(this));
        var context = new XamlExportContext(
            services,
            parentId => GetChildControls(parentId)
                .Select(child => controlNodes[child.Id])
                .ToList(),
            (childNode, childIndent, childWriter, exportContext) => TryAppendControlXamlViaDescriptor(childNode, childIndent, childWriter, exportContext),
            bindingSources);

        TryAppendControlXamlViaDescriptor(controlNodes[control.Id], indentLevel, writer, context);
    }

    private void AppendControlXaml(DesignControlModel control, int indentLevel)
    {
        if (_activeXamlWriter is null || _activeXamlExportContext is null || _activeXamlControlNodes is null)
            throw new InvalidOperationException("Shared XAML export context is not initialized.");

        if (_activeXamlControlNodes.TryGetValue(control.Id, out var controlNode))
        {
            TryAppendControlXamlViaDescriptor(controlNode, indentLevel, _activeXamlWriter, _activeXamlExportContext);
        }
    }

    private void AppendChildControlXaml(StringBuilder sb, DesignControlModel child, int indentLevel)
    {
        if (_activeXamlWriter is not null && _activeXamlExportContext is not null && _activeXamlControlNodes is not null)
        {
            AppendControlXaml(child, indentLevel);
            return;
        }

        AppendControlXaml(sb, child, indentLevel);
    }

    private bool TryAppendControlXamlViaDescriptor(IDesignControlNode controlNode, int indentLevel, IXamlWriter writer, IXamlExportContext context)
    {
        if (!IncludePluginRuntimeReferences
            && controlNode is DesignControlNodeAdapter adapter
            && IsPluginRuntimeControl(adapter.Model))
        {
            AppendPluginPlaceholderXaml(adapter.Model, indentLevel, writer);
            return true;
        }

        try
        {
            var descriptor = _registry.GetRequiredControl(controlNode.TypeKey);
            descriptor.AppendXaml(writer, controlNode, indentLevel, context);
            return true;
        }
        catch
        {
            if (ShouldIncludeExportComments)
                writer.WriteLine(indentLevel, $"<!-- Failed to export control: {EscapeXml(controlNode.TypeKey)} -->");
            return false;
        }
    }

    private void AppendPluginPlaceholderXaml(DesignControlModel control, int indentLevel, IXamlWriter writer)
    {
        var exportName = GetExportControlName(control);
        if (ShouldIncludeExportComments)
            writer.WriteLine(indentLevel, $"<!-- Plugin control '{EscapeXml(control.Type)}' заменен безопасным placeholder в режиме «Чистый UI». Включите runtime-ссылки плагинов, чтобы экспортировать реальный контрол. -->");
        writer.WriteLine(indentLevel, $"<Border x:Name=\"{EscapeXml(exportName)}\" {PlacementAttributes(control)} Background=\"#FFF7ED\" BorderBrush=\"#FB923C\" BorderThickness=\"1\" CornerRadius=\"8\"{CommonVisibilityAttributes(control)}>");
        writer.WriteLine(indentLevel + 1, $"<TextBlock Text=\"Placeholder плагина: {EscapeXml(control.Type)}\" Foreground=\"#9A3412\" TextWrapping=\"Wrap\" Margin=\"10\" VerticalAlignment=\"Center\" />");
        writer.WriteLine(indentLevel, "</Border>");
    }

    private sealed class BuiltInXamlBridge : IBuiltInXamlBridge
    {
        private readonly MainWindowViewModel _owner;
        private readonly Dictionary<string, Action<StringBuilder, DesignControlModel, int>> _appenders;

        public BuiltInXamlBridge(MainWindowViewModel owner)
        {
            _owner = owner;
            _appenders = new Dictionary<string, Action<StringBuilder, DesignControlModel, int>>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerControlTypes.Group] = (builder, control, indent) => _owner.AppendGroupXaml(builder, control, indent),
                [DesignerControlTypes.Button] = (builder, control, indent) => _owner.AppendButtonXaml(builder, control, indent),
                [DesignerControlTypes.TextBox] = (builder, control, indent) => _owner.AppendTextBoxXaml(builder, control, indent),
                [DesignerControlTypes.TextBlock] = (builder, control, indent) => _owner.AppendTextBlockXaml(builder, control, indent),
                [DesignerControlTypes.CheckBox] = (builder, control, indent) => _owner.AppendCheckBoxXaml(builder, control, indent),
                [DesignerControlTypes.Border] = (builder, control, indent) => _owner.AppendBorderXaml(builder, control, indent),
                [DesignerControlTypes.Image] = (builder, control, indent) => _owner.AppendImageXaml(builder, control, indent),
                [DesignerControlTypes.StackLayout] = (builder, control, indent) => _owner.AppendStackLayoutXaml(builder, control, indent),
                [DesignerControlTypes.LayoutGrid] = (builder, control, indent) => _owner.AppendLayoutGridXaml(builder, control, indent),
                [DesignerControlTypes.FlexLayout] = (builder, control, indent) => _owner.AppendFlexLayoutXaml(builder, control, indent),
                [DesignerControlTypes.DataGrid] = (builder, control, indent) => _owner.AppendDataGridXaml(builder, control, indent)
            };
        }

        public void AppendXaml(string typeKey, IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
        {
            if (control is not DesignControlNodeAdapter adapter || !_appenders.TryGetValue(typeKey, out var appender))
            {
                writer.WriteLine(indentLevel, $"<!-- No built-in exporter for {EscapeXml(typeKey)} -->");
                return;
            }

            var builder = new StringBuilder();
            appender(builder, adapter.Model, indentLevel);
            foreach (var line in builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (line.Length > 0)
                    writer.WriteLine(0, line);
            }
        }
    }

    private static string EscapeXml(string value) => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string ToInvariant(double value) => value.ToString(CultureInfo.InvariantCulture);
    private static string BoolToXaml(bool value) => value ? "True" : "False";
    private static string BoolToCSharp(bool value) => value ? "true" : "false";
    private static string Indent(int level) => new(' ', level * 2);
    private static bool CanSortBindingField(BindingFieldModel field) => field.AllowSort && field.IsSortable;
    private static bool IsBuiltInDesignerControlType(string type) => type switch
    {
        DesignerControlTypes.Group
            or DesignerControlTypes.Button
            or DesignerControlTypes.TextBox
            or DesignerControlTypes.TextBlock
            or DesignerControlTypes.CheckBox
            or DesignerControlTypes.Border
            or DesignerControlTypes.Image
            or DesignerControlTypes.StackLayout
            or DesignerControlTypes.LayoutGrid
            or DesignerControlTypes.FlexLayout
            or DesignerControlTypes.DataGrid => true,
        _ => false
    };

    private static bool IsPluginRuntimeControl(DesignControlModel control)
    {
        return !IsBuiltInDesignerControlType(control.Type)
            || !string.IsNullOrWhiteSpace(control.PluginId);
    }

    private static IEnumerable<BindingFieldModel> OrderBindingFieldsForDisplay(IEnumerable<BindingFieldModel> fields)
    {
        return fields
            .Select((field, index) => new { Field = field, Index = index })
            .OrderBy(item => item.Field.VisibleIndex < 0 ? int.MaxValue : item.Field.VisibleIndex)
            .ThenBy(item => item.Index)
            .Select(item => item.Field);
    }

    private static string ToBindingStringFormat(string? formatString)
    {
        if (string.IsNullOrWhiteSpace(formatString))
            return "";

        var trimmed = formatString.Trim();
        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
            return trimmed;

        return trimmed.Contains("{0", StringComparison.Ordinal)
            ? "{}" + trimmed
            : "{}{0:" + trimmed + "}";
    }

    private static bool CanFilterBindingField(BindingFieldModel field) => field.AllowFilter && field.IsVisible;

    private string GetSourceFilterMode(string sourceId)
    {
        var grid = Controls
            .Where(control => control.Type == DesignerControlTypes.DataGrid)
            .Where(control => control.ShowFilterRow)
            .FirstOrDefault(control => string.Equals(control.BindingSourceId, sourceId, StringComparison.OrdinalIgnoreCase));

        return DesignControlModel.NormalizeDataGridFilterMode(grid?.FilterMode);
    }

    private IReadOnlyList<BindingFieldModel> GetFilterableFieldsForContext(CrudGenerationContext context)
    {
        var hasFilterGrid = Controls.Any(control =>
            control.Type == DesignerControlTypes.DataGrid
            && control.ShowFilterRow
            && string.Equals(control.BindingSourceId, context.Source.Id, StringComparison.OrdinalIgnoreCase));

        if (!hasFilterGrid)
            return Array.Empty<BindingFieldModel>();

        return OrderBindingFieldsForDisplay(context.Fields)
            .Where(CanFilterBindingField)
            .GroupBy(field => field.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string GetColumnFilterPropertyName(CrudGenerationContext context, BindingFieldModel field)
    {
        return $"{context.ItemTypeName}{SanitizeIdentifier(field.Path, SanitizeIdentifier(field.Header, "Field"))}Filter";
    }

    private static string ThemeResourceReference(string resourceKey) => "{StaticResource " + resourceKey + "}";

    private string BrushAttribute(DesignControlModel control, string propertyName, string currentValue, Func<ThemeControlDefaults, string?> selector, Func<ThemeControlDefaults, string?> keySelector)
    {
        if (string.IsNullOrWhiteSpace(currentValue))
            return string.Empty;

        var xamlValue = ResolveBrushValue(control, currentValue, selector, keySelector);

        return $" {propertyName}=\"{xamlValue}\"";
    }

    private string ResolveBrushValue(DesignControlModel control, string currentValue, Func<ThemeControlDefaults, string?> selector, Func<ThemeControlDefaults, string?> keySelector)
    {
        if (IsCompactXamlExport)
            return EscapeXml(currentValue);

        var defaults = DesignerThemeCatalog.GetControlDefaults(control.Type, FormTheme);
        var defaultValue = selector(defaults);
        var resourceKey = keySelector(defaults);
        return ResolveBrushValue(currentValue, defaultValue, resourceKey);
    }

    private string ResolveBrushValue(string currentValue, string? defaultValue, string? resourceKey)
    {
        if (IsCompactXamlExport)
            return EscapeXml(currentValue);

        return !string.IsNullOrWhiteSpace(defaultValue)
            && !string.IsNullOrWhiteSpace(resourceKey)
            && DesignerThemeCatalog.AreEquivalent(currentValue, defaultValue)
                ? ThemeResourceReference(resourceKey)
                : EscapeXml(currentValue);
    }

    private string BackgroundAttribute(DesignControlModel control) =>
        BrushAttribute(control, "Background", control.Background, defaults => defaults.Background, defaults => defaults.BackgroundResourceKey);

    private string ForegroundAttribute(DesignControlModel control) =>
        BrushAttribute(control, "Foreground", control.Foreground, defaults => defaults.Foreground, defaults => defaults.ForegroundResourceKey);

    private string BorderBrushAttribute(DesignControlModel control) =>
        BrushAttribute(control, "BorderBrush", control.BorderBrush, defaults => defaults.BorderBrush, defaults => defaults.BorderBrushResourceKey);

    private void AppendThemeResources(StringBuilder sb, FormThemePalette palette)
    {
        sb.AppendLine("  <Window.Resources>");
        AppendBrushResource(sb, 2, ThemeResourceKeys.WindowBackgroundBrush, SurfaceBackground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.TextBrush, palette.TextBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.MutedTextBrush, palette.MutedTextBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.BorderBrush, palette.BorderBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.InputBackgroundBrush, palette.InputBackground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.ContainerBackgroundBrush, palette.ContainerBackground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.ButtonBackgroundBrush, palette.ButtonBackground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.ButtonForegroundBrush, palette.ButtonForeground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.ButtonBorderBrush, palette.ButtonBorderBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.AccentBrush, palette.AccentBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.AccentStrongBrush, palette.AccentStrongBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.AccentForegroundBrush, palette.AccentForegroundBrush);
        AppendBrushResource(sb, 2, ThemeResourceKeys.DataGridHeaderBackgroundBrush, palette.DataGridHeaderBackground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.DataGridHeaderForegroundBrush, palette.DataGridHeaderForeground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.DataGridRowBackgroundBrush, palette.DataGridRowBackground);
        AppendBrushResource(sb, 2, ThemeResourceKeys.DataGridAlternateRowBackgroundBrush, palette.DataGridAlternateRowBackground);
        sb.AppendLine("  </Window.Resources>");
    }

    private static void AppendBrushResource(StringBuilder sb, int indentLevel, string key, string color)
    {
        sb.AppendLine($"{Indent(indentLevel)}<SolidColorBrush x:Key=\"{key}\" Color=\"{EscapeXml(color)}\" />");
    }

    private void AppendThemeStyles(StringBuilder sb)
    {
        sb.AppendLine("  <Window.Styles>");
        sb.AppendLine("    <Style Selector=\"TextBlock\">");
        sb.AppendLine($"      <Setter Property=\"Foreground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.TextBrush)}\" />");
        sb.AppendLine("    </Style>");
        sb.AppendLine("    <Style Selector=\"Button\">");
        sb.AppendLine($"      <Setter Property=\"Background\" Value=\"{ThemeResourceReference(ThemeResourceKeys.ButtonBackgroundBrush)}\" />");
        sb.AppendLine($"      <Setter Property=\"Foreground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.ButtonForegroundBrush)}\" />");
        sb.AppendLine($"      <Setter Property=\"BorderBrush\" Value=\"{ThemeResourceReference(ThemeResourceKeys.ButtonBorderBrush)}\" />");
        sb.AppendLine("      <Setter Property=\"BorderThickness\" Value=\"1\" />");
        sb.AppendLine("      <Setter Property=\"CornerRadius\" Value=\"8\" />");
        sb.AppendLine("      <Setter Property=\"Padding\" Value=\"10\" />");
        sb.AppendLine("    </Style>");
        sb.AppendLine("    <Style Selector=\"TextBox\">");
        sb.AppendLine($"      <Setter Property=\"Background\" Value=\"{ThemeResourceReference(ThemeResourceKeys.InputBackgroundBrush)}\" />");
        sb.AppendLine($"      <Setter Property=\"Foreground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.TextBrush)}\" />");
        sb.AppendLine($"      <Setter Property=\"BorderBrush\" Value=\"{ThemeResourceReference(ThemeResourceKeys.BorderBrush)}\" />");
        sb.AppendLine("      <Setter Property=\"BorderThickness\" Value=\"1\" />");
        sb.AppendLine("      <Setter Property=\"Padding\" Value=\"10\" />");
        sb.AppendLine("    </Style>");
        sb.AppendLine("    <Style Selector=\"CheckBox\">");
        sb.AppendLine($"      <Setter Property=\"Foreground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.TextBrush)}\" />");
        sb.AppendLine("    </Style>");
        if (ShouldExportRealDataGrid && Controls.Any(control => control.Type == DesignerControlTypes.DataGrid))
        {
            sb.AppendLine("    <Style Selector=\"DataGrid\">");
            sb.AppendLine($"      <Setter Property=\"Background\" Value=\"{ThemeResourceReference(ThemeResourceKeys.DataGridRowBackgroundBrush)}\" />");
            sb.AppendLine($"      <Setter Property=\"Foreground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.TextBrush)}\" />");
            sb.AppendLine($"      <Setter Property=\"BorderBrush\" Value=\"{ThemeResourceReference(ThemeResourceKeys.BorderBrush)}\" />");
            sb.AppendLine("      <Setter Property=\"BorderThickness\" Value=\"1\" />");
            sb.AppendLine($"      <Setter Property=\"RowBackground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.DataGridRowBackgroundBrush)}\" />");
            sb.AppendLine("    </Style>");
            sb.AppendLine("    <Style Selector=\"DataGridColumnHeader\">");
            sb.AppendLine($"      <Setter Property=\"Background\" Value=\"{ThemeResourceReference(ThemeResourceKeys.DataGridHeaderBackgroundBrush)}\" />");
            sb.AppendLine($"      <Setter Property=\"Foreground\" Value=\"{ThemeResourceReference(ThemeResourceKeys.DataGridHeaderForegroundBrush)}\" />");
            sb.AppendLine($"      <Setter Property=\"BorderBrush\" Value=\"{ThemeResourceReference(ThemeResourceKeys.BorderBrush)}\" />");
            sb.AppendLine("      <Setter Property=\"BorderThickness\" Value=\"0,0,1,1\" />");
            sb.AppendLine("    </Style>");
        }
        sb.AppendLine("  </Window.Styles>");
    }
    private static string NormalizeId(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value;

    private static string SanitizeIdentifier(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new StringBuilder(source.Length + 8);

        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                builder.Append(ch);
            else if (ch is ' ' or '-' or '.')
                builder.Append('_');
        }

        if (builder.Length == 0)
            builder.Append(fallback);

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
            builder.Insert(0, '_');

        return builder.ToString();
    }

    private static Dictionary<string, string> BuildExportControlNameMap(
        IEnumerable<DesignControlModel> controls,
        params string[] additionalReservedNames)
    {
        var list = controls.ToList();
        var used = new HashSet<string>(UnsafeGeneratedIdentifiers, StringComparer.OrdinalIgnoreCase);
        foreach (var reserved in additionalReservedNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            used.Add(reserved);

        var preferredNames = list.ToDictionary(
            control => control.Id,
            control => SanitizeIdentifier(control.Name, SanitizeIdentifier(control.Type, "Control")),
            StringComparer.OrdinalIgnoreCase);
        var preferredCounts = preferredNames.Values
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var control in list)
        {
            var preferred = preferredNames[control.Id];
            if (preferredCounts[preferred] == 1 && !used.Contains(preferred))
            {
                result[control.Id] = preferred;
                used.Add(preferred);
            }
        }

        foreach (var control in list)
        {
            if (result.ContainsKey(control.Id))
                continue;

            var preferred = preferredNames[control.Id];
            var baseName = string.IsNullOrWhiteSpace(preferred)
                ? SanitizeIdentifier(control.Type, "Control")
                : preferred;
            var candidate = baseName;
            var index = 1;
            if (UnsafeGeneratedIdentifiers.Contains(candidate))
                candidate = $"{baseName}{index++}";

            while (used.Contains(candidate) || UnsafeGeneratedIdentifiers.Contains(candidate))
                candidate = $"{baseName}{index++}";

            result[control.Id] = candidate;
            used.Add(candidate);
        }

        return result;
    }

    private string GetExportControlName(DesignControlModel control)
    {
        if (_activeXamlControlNameMap is not null
            && _activeXamlControlNameMap.TryGetValue(control.Id, out var exportName))
        {
            return exportName;
        }

        var fallback = SanitizeIdentifier(control.Type, "Control");
        var candidate = SanitizeIdentifier(control.Name, fallback);
        return UnsafeGeneratedIdentifiers.Contains(candidate)
            ? $"{candidate}1"
            : candidate;
    }

    private static string NormalizeCSharpType(string? typeName)
    {
        return typeName?.Trim() switch
        {
            "int" => "int",
            "long" => "long",
            "short" => "short",
            "decimal" => "decimal",
            "double" => "double",
            "float" => "float",
            "bool" => "bool",
            "string" => "string",
            "DateTime" => "DateTime",
            "DateTimeOffset" => "DateTimeOffset",
            "TimeSpan" => "TimeSpan",
            "Guid" => "Guid",
            "byte[]" => "byte[]",
            _ => "string"
        };
    }

    private static bool RequiresSpecialLiteral(string? typeName)
    {
        return NormalizeCSharpType(typeName) is "DateTime" or "DateTimeOffset" or "TimeSpan" or "Guid" or "byte[]";
    }

    private static string ToCSharpLiteral(string? typeName, string? sampleValue)
    {
        var normalizedType = NormalizeCSharpType(typeName);
        var value = sampleValue?.Trim() ?? string.Empty;
        var numeric = value.Replace(" ", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);

        return normalizedType switch
        {
            "string" => $"\"{EscapeCSharp(value)}\"",
            "bool" => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            "int" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                ? intValue.ToString(CultureInfo.InvariantCulture)
                : "0",
            "long" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture)
                : "0L",
            "short" => short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue)
                ? $"(short){shortValue.ToString(CultureInfo.InvariantCulture)}"
                : "(short)0",
            "decimal" => decimal.TryParse(numeric, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue)
                ? $"{decimalValue.ToString(CultureInfo.InvariantCulture)}m"
                : "0m",
            "double" => double.TryParse(numeric, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue)
                ? $"{doubleValue.ToString(CultureInfo.InvariantCulture)}d"
                : "0d",
            "float" => float.TryParse(numeric, NumberStyles.Any, CultureInfo.InvariantCulture, out var floatValue)
                ? $"{floatValue.ToString(CultureInfo.InvariantCulture)}f"
                : "0f",
            "DateTime" => $"DateTime.Parse(\"{EscapeCSharp(value)}\", CultureInfo.InvariantCulture)",
            "DateTimeOffset" => $"DateTimeOffset.Parse(\"{EscapeCSharp(value)}\", CultureInfo.InvariantCulture)",
            "TimeSpan" => $"TimeSpan.Parse(\"{EscapeCSharp(value)}\", CultureInfo.InvariantCulture)",
            "Guid" => $"Guid.Parse(\"{EscapeCSharp(value)}\")",
            "byte[]" => "Array.Empty<byte>()",
            _ => $"\"{EscapeCSharp(value)}\""
        };
    }

    private static string EscapeCSharp(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private List<CrudGenerationContext> BuildCrudGenerationContexts()
    {
        var boundSources = Controls
            .Where(control => control.Type == DesignerControlTypes.DataGrid)
            .Select(control => GetBindingSource(control.BindingSourceId))
            .Where(source => source is not null)
            .Cast<BindingSourceModel>()
            .DistinctBy(source => source.Id)
            .ToList();

        var sourceCandidates = boundSources.Count > 0 ? boundSources : BindingSources.ToList();

        return sourceCandidates.Select(source =>
        {
            var itemTypeName = SanitizeIdentifier(source.ItemTypeName, "RowItem");
            var collectionPropertyName = SanitizeIdentifier(source.Path, SanitizeIdentifier(source.Name, "Items"));
            var fields = source.Fields.Count > 0
                ? source.Fields.ToList()
                : new List<BindingFieldModel>
                {
                    new() { Header = "Id", Path = "Id", SampleValue = "1", TypeName = "int" },
                    new() { Header = "Name", Path = "Name", SampleValue = "Новая запись", TypeName = "string" }
                };

            var searchFields = fields
                .Where(field => string.Equals(field.TypeName, "string", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            if (searchFields.Count == 0)
                searchFields = fields.Take(2).ToList();

            return new CrudGenerationContext
            {
                Source = source,
                ItemTypeName = itemTypeName,
                CollectionPropertyName = collectionPropertyName,
                ViewCollectionPropertyName = $"{collectionPropertyName}View",
                SearchTextPropertyName = $"{itemTypeName}SearchText",
                SelectedItemPropertyName = $"Selected{itemTypeName}",
                CurrentItemPropertyName = $"Current{itemTypeName}",
                Fields = fields,
                SearchFields = searchFields
            };
        }).ToList();
    }

    private CrudGenerationContext? GetCrudGenerationContext(BindingSourceModel? source)
    {
        if (source is null)
            return null;

        return BuildCrudGenerationContexts().FirstOrDefault(context => context.Source.Id == source.Id);
    }

    private List<InteractionModel> GetSelectionChangedSetPropertyInteractionsForGrid(DesignControlModel grid)
    {
        return Interactions
            .Where(interaction => IsDataGridSelectionChangedEvent(interaction.EventName))
            .Where(interaction => string.Equals(interaction.SourceControlName, grid.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private bool HasConfiguredInteractionEvent(DesignControlModel source, string eventName)
    {
        return Interactions.Any(interaction =>
            string.Equals(interaction.SourceControlName, source.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(InteractionModel.NormalizeEventName(interaction.EventName), eventName, StringComparison.OrdinalIgnoreCase)
            && IsSupportedInteractionSourceEvent(source, eventName));
    }

    private List<ExportableInteraction> GetExportableInteractions()
    {
        var result = new List<ExportableInteraction>();
        foreach (var interaction in Interactions)
        {
            var eventName = InteractionModel.NormalizeEventName(interaction.EventName);
            var source = FindControlByName(interaction.SourceControlName);
            var target = FindControlByName(interaction.TargetControlName);
            if (source is null || !IsSupportedInteractionSourceEvent(source, eventName))
                continue;

            if (IsDataGridSelectionChangedEvent(eventName) && !ShouldExportRealDataGrid)
                continue;

            if (!string.Equals(interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase))
            {
                if (target is null || !IsSupportedInteractionTargetAction(target, interaction))
                    continue;
            }

            result.Add(new ExportableInteraction(interaction, source, target, eventName));
        }

        return result;
    }

    private List<ExportableInteraction> GetExportableSelectionChangedInteractions()
    {
        return GetExportableInteractions()
            .Where(item => IsDataGridSelectionChangedEvent(item.EventName))
            .ToList();
    }

    private DesignControlModel? FindControlByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return Controls.FirstOrDefault(control => string.Equals(control.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSelectionChangedSetPropertyInteraction(InteractionModel interaction)
    {
        return IsDataGridSelectionChangedEvent(interaction.EventName)
            && string.Equals(interaction.ActionType, InteractionModel.ActionSetProperty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDataGridSelectionChangedEvent(string? eventName)
    {
        var normalized = InteractionModel.NormalizeEventName(eventName);
        return string.Equals(normalized, InteractionModel.EventDataGridSelectionChanged, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedInteractionSourceEvent(DesignControlModel source, string eventName)
    {
        return source.Type switch
        {
            DesignerControlTypes.Button => string.Equals(eventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.TextBox => string.Equals(eventName, InteractionModel.EventTextBoxTextChanged, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.CheckBox => string.Equals(eventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.DataGrid => IsDataGridSelectionChangedEvent(eventName),
            _ => false
        };
    }

    private static bool IsSupportedInteractionTargetAction(DesignControlModel target, InteractionModel interaction)
    {
        return interaction.ActionType switch
        {
            InteractionModel.ActionSetProperty or InteractionModel.ActionClearProperty => IsSupportedInteractionTarget(target)
                && IsSupportedInteractionTargetProperty(target, interaction.TargetProperty),
            InteractionModel.ActionToggleVisibility => true,
            InteractionModel.ActionEnableDisable => true,
            _ => false
        };
    }

    private static bool IsSupportedInteractionTargetProperty(DesignControlModel target, string? targetProperty)
    {
        var property = string.IsNullOrWhiteSpace(targetProperty)
            ? GetDefaultInteractionTargetProperty(target)
            : targetProperty.Trim();

        return target.Type switch
        {
            DesignerControlTypes.TextBox => string.Equals(property, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.TextBlock => string.Equals(property, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.Button => string.Equals(property, InteractionModel.TargetPropertyContent, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.CheckBox => string.Equals(property, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static GeneratedButtonAction DetectButtonAction(DesignControlModel button)
    {
        var text = $"{button.Name} {button.Text}".ToLowerInvariant();

        if (text.Contains("add") || text.Contains("new") || text.Contains("create") || text.Contains("добав") || text.Contains("нов"))
            return GeneratedButtonAction.Add;

        if (text.Contains("save") || text.Contains("apply") || text.Contains("сохран") || text.Contains("запис"))
            return GeneratedButtonAction.Save;

        if (text.Contains("delete") || text.Contains("remove") || text.Contains("удал"))
            return GeneratedButtonAction.Delete;

        if (text.Contains("edit") || text.Contains("update") || text.Contains("редакт") || text.Contains("измен"))
            return GeneratedButtonAction.Edit;

        if (text.Contains("search") || text.Contains("find") || text.Contains("filter") || text.Contains("поиск") || text.Contains("найти") || text.Contains("фильтр"))
            return GeneratedButtonAction.Search;

        if (text.Contains("clear") || text.Contains("reset") || text.Contains("очист") || text.Contains("сброс"))
            return GeneratedButtonAction.Clear;

        return GeneratedButtonAction.None;
    }

    private static GeneratedButtonAction ResolveGeneratedButtonAction(DesignControlModel button)
    {
        if (!string.IsNullOrWhiteSpace(button.GeneratedButtonActionKey)
            && Enum.TryParse<GeneratedButtonAction>(button.GeneratedButtonActionKey, ignoreCase: true, out var configuredAction))
        {
            return configuredAction;
        }

        return DetectButtonAction(button);
    }

    private static bool TryBuildCrudButtonCall(GeneratedButtonAction action, CrudGenerationContext context, out string callLine)
    {
        callLine = action switch
        {
            GeneratedButtonAction.Add => $"BeginCreate{context.ItemTypeName}()",
            GeneratedButtonAction.Save => $"Save{context.ItemTypeName}()",
            GeneratedButtonAction.Delete => $"DeleteSelected{context.ItemTypeName}()",
            GeneratedButtonAction.Edit => $"StartEditingSelected{context.ItemTypeName}()",
            GeneratedButtonAction.Search => $"Apply{context.ItemTypeName}Filter()",
            GeneratedButtonAction.Clear => $"Reset{context.ItemTypeName}Editor()",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(callLine);
    }

    private static string BuildSearchPredicate(CrudGenerationContext context)
    {
        return string.Join(" || ", context.SearchFields.Select(field =>
        {
            var property = SanitizeIdentifier(field.Path, "Field");
            return $"ContainsText(item.{property}, {context.SearchTextPropertyName})";
        }));
    }

    private static bool IsSqlServerSource(BindingSourceModel source)
    {
        return source.SourceKind.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToVerbatimCSharpString(string? value)
    {
        return "@\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendGeneratedInteractionHandlers(
        StringBuilder sb,
        IReadOnlyList<ExportableInteraction> interactions,
        IReadOnlyDictionary<string, string> exportControlNames,
        bool skipButtonClickHandlers)
    {
        if (interactions.Count == 0)
            return;

        sb.AppendLine();
        foreach (var group in interactions
            .Where(item => !skipButtonClickHandlers
                || !string.Equals(item.EventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => $"{item.Source.Id}\u001F{item.EventName}", StringComparer.OrdinalIgnoreCase))
        {
            var source = group.First().Source;
            var eventName = group.First().EventName;
            var sourceExportName = exportControlNames[source.Id];
            var handlerName = GetGeneratedInteractionHandlerName(sourceExportName, eventName);
            var needsAsync = group.Any(item => string.Equals(item.Interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase));
            var signature = GetGeneratedInteractionHandlerSignature(handlerName, eventName, needsAsync);
            if (string.IsNullOrWhiteSpace(signature))
                continue;

            sb.AppendLine($"    {signature}");
            sb.AppendLine("    {");

            if (IsDataGridSelectionChangedEvent(eventName))
            {
                sb.AppendLine($"        var selectedItem = {sourceExportName}.SelectedItem;");
                sb.AppendLine("        if (selectedItem is null)");
                sb.AppendLine("        {");
                foreach (var item in group)
                    AppendGeneratedInteractionAction(sb, item, exportControlNames, "null", 3, needsAsync);
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine();
                foreach (var item in group)
                    AppendGeneratedInteractionAction(sb, item, exportControlNames, "selectedItem", 2, needsAsync);
            }
            else
            {
                foreach (var item in group)
                    AppendGeneratedInteractionAction(sb, item, exportControlNames, sourceExportName, 2, needsAsync);
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        AppendGeneratedInteractionHelpers(
            sb,
            includeShowMessageHelper: interactions.Any(item =>
                string.Equals(item.Interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase)));
    }

    private static string GetGeneratedInteractionHandlerName(string sourceExportName, string eventName)
    {
        if (string.Equals(eventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase))
            return $"{sourceExportName}Click";

        if (string.Equals(eventName, InteractionModel.EventTextBoxTextChanged, StringComparison.OrdinalIgnoreCase))
            return $"{sourceExportName}_TextChanged";

        if (string.Equals(eventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase))
            return $"{sourceExportName}_Checked";

        if (string.Equals(eventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase))
            return $"{sourceExportName}_Unchecked";

        return IsDataGridSelectionChangedEvent(eventName)
            ? $"{sourceExportName}_SelectionChanged"
            : $"{sourceExportName}_Interaction";
    }

    private static string GetGeneratedInteractionHandlerSignature(string handlerName, string eventName, bool needsAsync)
    {
        var methodPrefix = needsAsync ? "private async void" : "private void";
        if (string.Equals(eventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase))
        {
            return $"{methodPrefix} {handlerName}(object? sender, RoutedEventArgs e)";
        }

        if (string.Equals(eventName, InteractionModel.EventTextBoxTextChanged, StringComparison.OrdinalIgnoreCase))
            return $"{methodPrefix} {handlerName}(object? sender, TextChangedEventArgs e)";

        if (IsDataGridSelectionChangedEvent(eventName))
            return $"{methodPrefix} {handlerName}(object? sender, SelectionChangedEventArgs e)";

        return string.Empty;
    }

    private static void AppendGeneratedInteractionAction(
        StringBuilder sb,
        ExportableInteraction item,
        IReadOnlyDictionary<string, string> exportControlNames,
        string sourceExpression,
        int indentLevel,
        bool awaitAsyncActions)
    {
        var interaction = item.Interaction;
        var indent = Indent(indentLevel);
        var valueExpression = $"ResolveInteractionValue({sourceExpression}, {ToVerbatimCSharpString(interaction.SourcePath)}, {ToVerbatimCSharpString(interaction.TextTemplate)})";

        if (string.Equals(interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase))
        {
            var titleLiteral = ToVerbatimCSharpString(string.IsNullOrWhiteSpace(interaction.MessageTitle) ? "Сообщение" : interaction.MessageTitle);
            var awaitPrefix = awaitAsyncActions ? "await " : "_ = ";
            sb.AppendLine($"{indent}{awaitPrefix}ShowMessageAsync({valueExpression}, {titleLiteral});");
            return;
        }

        if (item.Target is null || !exportControlNames.TryGetValue(item.Target.Id, out var targetExportName))
            return;

        var property = string.IsNullOrWhiteSpace(interaction.TargetProperty)
            ? GetDefaultInteractionTargetProperty(item.Target)
            : interaction.TargetProperty.Trim();

        switch (interaction.ActionType)
        {
            case InteractionModel.ActionClearProperty:
                AppendGeneratedClearProperty(sb, item.Target, targetExportName, property, indent);
                break;

            case InteractionModel.ActionToggleVisibility:
                if (TryGetBooleanStateForInteractionEvent(item.EventName, out var visibilityState))
                    sb.AppendLine($"{indent}{targetExportName}.IsVisible = {BoolToCSharp(visibilityState)};");
                else
                    sb.AppendLine($"{indent}{targetExportName}.IsVisible = !{targetExportName}.IsVisible;");
                break;

            case InteractionModel.ActionEnableDisable:
                if (TryGetBooleanStateForInteractionEvent(item.EventName, out var enabledState))
                    sb.AppendLine($"{indent}{targetExportName}.IsEnabled = {BoolToCSharp(enabledState)};");
                else
                    sb.AppendLine($"{indent}{targetExportName}.IsEnabled = ParseNullableBool({valueExpression}) ?? false;");
                break;

            default:
                if (item.Target.Type == DesignerControlTypes.CheckBox
                    && string.Equals(property, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"{indent}{targetExportName}.IsChecked = ParseNullableBool({valueExpression});");
                }
                else
                {
                    sb.AppendLine($"{indent}{targetExportName}.{property} = {valueExpression};");
                }

                break;
        }
    }

    private static void AppendGeneratedClearProperty(
        StringBuilder sb,
        DesignControlModel target,
        string targetExportName,
        string property,
        string indent)
    {
        if (target.Type == DesignerControlTypes.CheckBox
            && string.Equals(property, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{indent}{targetExportName}.IsChecked = null;");
            return;
        }

        if (string.Equals(property, InteractionModel.TargetPropertyIsVisible, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{indent}{targetExportName}.IsVisible = false;");
            return;
        }

        if (string.Equals(property, InteractionModel.TargetPropertyIsEnabled, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{indent}{targetExportName}.IsEnabled = false;");
            return;
        }

        sb.AppendLine($"{indent}{targetExportName}.{property} = string.Empty;");
    }

    private static void AppendGeneratedInteractionAssignment(
        StringBuilder sb,
        InteractionModel interaction,
        DesignControlModel target,
        string targetExportName,
        string selectedItemExpression,
        int indentLevel)
    {
        var indent = Indent(indentLevel);
        var property = string.IsNullOrWhiteSpace(interaction.TargetProperty)
            ? GetDefaultInteractionTargetProperty(target)
            : interaction.TargetProperty.Trim();

        if (string.Equals(selectedItemExpression, "null", StringComparison.Ordinal))
        {
            if (target.Type == DesignerControlTypes.CheckBox
                && string.Equals(property, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{indent}{targetExportName}.IsChecked = null;");
                return;
            }

            sb.AppendLine($"{indent}{targetExportName}.{property} = string.Empty;");
            return;
        }

        var sourcePathLiteral = ToVerbatimCSharpString(interaction.SourcePath);
        var textExpression = string.IsNullOrWhiteSpace(interaction.TextTemplate)
            ? $"GetSelectedValue({selectedItemExpression}, {sourcePathLiteral})"
            : $"ApplySelectedTemplate({selectedItemExpression}, {ToVerbatimCSharpString(interaction.TextTemplate)})";

        if (target.Type == DesignerControlTypes.CheckBox
            && string.Equals(property, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase))
        {
            var boolExpression = string.IsNullOrWhiteSpace(interaction.TextTemplate)
                ? $"GetSelectedBool({selectedItemExpression}, {sourcePathLiteral})"
                : $"ParseNullableBool({textExpression})";
            sb.AppendLine($"{indent}{targetExportName}.IsChecked = {boolExpression};");
            return;
        }

        sb.AppendLine($"{indent}{targetExportName}.{property} = {textExpression};");
    }

    private static bool TryGetBooleanStateForInteractionEvent(string eventName, out bool value)
    {
        if (string.Equals(eventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(eventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static void AppendGeneratedInteractionHelpers(StringBuilder sb, bool includeShowMessageHelper)
    {
        sb.AppendLine("    private static string ResolveInteractionValue(object? source, string propertyName, string template)");
        sb.AppendLine("    {");
        sb.AppendLine("        return string.IsNullOrWhiteSpace(template)");
        sb.AppendLine("            ? GetSelectedValue(source, propertyName)");
        sb.AppendLine("            : ApplySelectedTemplate(source, template);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static string GetSelectedValue(object? item, string propertyName)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("            return string.Empty;");
        sb.AppendLine();
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(propertyName))");
        sb.AppendLine("            return string.Empty;");
        sb.AppendLine();
        sb.AppendLine("        if (item is System.Collections.IDictionary dictionary && dictionary.Contains(propertyName))");
        sb.AppendLine("            return dictionary[propertyName]?.ToString() ?? string.Empty;");
        sb.AppendLine();
        sb.AppendLine("        var property = item.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);");
        sb.AppendLine("        return property?.GetValue(item)?.ToString() ?? string.Empty;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static bool? GetSelectedBool(object? item, string propertyName)");
        sb.AppendLine("    {");
        sb.AppendLine("        return ParseNullableBool(GetSelectedValue(item, propertyName));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static bool? ParseNullableBool(string value)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (bool.TryParse(value, out var boolValue))");
        sb.AppendLine("            return boolValue;");
        sb.AppendLine();
        sb.AppendLine("        if (int.TryParse(value, out var intValue))");
        sb.AppendLine("            return intValue != 0;");
        sb.AppendLine();
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static string ApplySelectedTemplate(object? item, string template)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(template))");
        sb.AppendLine("            return string.Empty;");
        sb.AppendLine();
        sb.AppendLine("        return System.Text.RegularExpressions.Regex.Replace(template, \"\\\\{(?<name>[^{}]+)\\\\}\", match => GetSelectedValue(item, match.Groups[\"name\"].Value.Trim()));");
        sb.AppendLine("    }");

        if (!includeShowMessageHelper)
            return;

        sb.AppendLine();
        sb.AppendLine("    private async System.Threading.Tasks.Task ShowMessageAsync(string message, string title = \"Сообщение\")");
        sb.AppendLine("    {");
        sb.AppendLine("        var closeButton = new Button");
        sb.AppendLine("        {");
        sb.AppendLine("            Content = \"OK\",");
        sb.AppendLine("            MinWidth = 92,");
        sb.AppendLine("            HorizontalAlignment = HorizontalAlignment.Right");
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        var dialog = new Window");
        sb.AppendLine("        {");
        sb.AppendLine("            Title = string.IsNullOrWhiteSpace(title) ? \"Сообщение\" : title,");
        sb.AppendLine("            Width = 380,");
        sb.AppendLine("            Height = 180,");
        sb.AppendLine("            WindowStartupLocation = WindowStartupLocation.CenterOwner,");
        sb.AppendLine("            Content = new StackPanel");
        sb.AppendLine("            {");
        sb.AppendLine("                Margin = new Thickness(18),");
        sb.AppendLine("                Spacing = 16,");
        sb.AppendLine("                Children =");
        sb.AppendLine("                {");
        sb.AppendLine("                    new TextBlock { Text = string.IsNullOrWhiteSpace(message) ? \"Сообщение\" : message, TextWrapping = TextWrapping.Wrap },");
        sb.AppendLine("                    closeButton");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        closeButton.Click += (_, _) => dialog.Close();");
        sb.AppendLine("        await dialog.ShowDialog(this);");
        sb.AppendLine("    }");
    }

    private static void AppendGeneratedSqlAssignments(StringBuilder sb, CrudGenerationContext context, int indentLevel)
    {
        for (var index = 0; index < context.Fields.Count; index++)
        {
            var field = context.Fields[index];
            var property = SanitizeIdentifier(field.Path, $"Field{index + 1}");
            var suffix = index == context.Fields.Count - 1 ? string.Empty : ",";
            sb.AppendLine($"{Indent(indentLevel)}{property} = {BuildGeneratedSqlReaderExpression(field)}{suffix}");
        }
    }

    private static string BuildGeneratedSqlReaderExpression(BindingFieldModel field)
    {
        var columnNameLiteral = ToVerbatimCSharpString(field.Path);
        return NormalizeCSharpType(field.TypeName) switch
        {
            "string" => $"ReadString(reader, {columnNameLiteral})",
            "byte[]" => $"ReadBytes(reader, {columnNameLiteral})",
            var typeName => $"ReadValue<{typeName}>(reader, {columnNameLiteral})"
        };
    }

    private static void AppendSeedAssignments(StringBuilder sb, CrudGenerationContext context, int indentLevel, int variantIndex)
    {
        var fields = context.Fields.Take(8).ToList();
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var property = SanitizeIdentifier(field.Path, $"Field{index + 1}");
            var suffix = index == fields.Count - 1 ? string.Empty : ",";
            var sample = field.SampleValue;
            sb.AppendLine($"{Indent(indentLevel)}{property} = {ToCSharpLiteral(field.TypeName, sample)}{suffix}");
        }
    }

    private static void AppendCloneAssignments(StringBuilder sb, CrudGenerationContext context, int indentLevel)
    {
        for (var index = 0; index < context.Fields.Count; index++)
        {
            var field = context.Fields[index];
            var property = SanitizeIdentifier(field.Path, "Field");
            var suffix = index == context.Fields.Count - 1 ? string.Empty : ",";
            sb.AppendLine($"{Indent(indentLevel)}{property} = {property}{suffix}");
        }
    }

    private static string GetVariantSampleValue(BindingFieldModel field, int variantIndex)
    {
        if (variantIndex == 0)
            return field.SampleValue;

        var normalizedType = NormalizeCSharpType(field.TypeName);
        var value = field.SampleValue?.Trim() ?? string.Empty;

        return normalizedType switch
        {
            "string" => string.IsNullOrWhiteSpace(value) ? $"Запись {variantIndex + 1}" : $"{value} {variantIndex + 1}",
            "bool" => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "False" : "True",
            "int" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) => (intValue + variantIndex + 1).ToString(CultureInfo.InvariantCulture),
            "long" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue) => (longValue + variantIndex + 1).ToString(CultureInfo.InvariantCulture),
            "short" when short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue) => (shortValue + variantIndex + 1).ToString(CultureInfo.InvariantCulture),
            "decimal" when decimal.TryParse(value.Replace(",", ".", StringComparison.Ordinal), NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue) => (decimalValue + variantIndex + 1).ToString(CultureInfo.InvariantCulture),
            "double" when double.TryParse(value.Replace(",", ".", StringComparison.Ordinal), NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue) => (doubleValue + variantIndex + 1).ToString(CultureInfo.InvariantCulture),
            "float" when float.TryParse(value.Replace(",", ".", StringComparison.Ordinal), NumberStyles.Any, CultureInfo.InvariantCulture, out var floatValue) => (floatValue + variantIndex + 1).ToString(CultureInfo.InvariantCulture),
            _ => value
        };
    }

    private static string SizeAttributes(double width, double height)
    {
        return $"Width=\"{ToInvariant(width)}\" Height=\"{ToInvariant(height)}\"";
    }

    private static string CanvasLayoutAttributes(DesignControlModel control)
    {
        return $"{SizeAttributes(control.Width, control.Height)} Canvas.Left=\"{ToInvariant(control.X)}\" Canvas.Top=\"{ToInvariant(control.Y)}\"";
    }

    private static string ToAvaloniaOrientation(string value)
    {
        return DesignerLayoutModes.NormalizeOrientation(value) == DesignerLayoutModes.Horizontal
            ? "Horizontal"
            : "Vertical";
    }

    private double GetParentLayoutSpacing(string? parentId)
    {
        var parent = GetControl(parentId);
        return parent is null ? SurfaceLayoutSpacing : parent.LayoutSpacing;
    }

    private string GetParentLayoutOrientation(string? parentId)
    {
        var parent = GetControl(parentId);
        return parent is null ? SurfaceLayoutOrientation : parent.LayoutOrientation;
    }

    private string GetExportLayoutModeForParent(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(NormalizeId(parentId)) && _activeLayoutExportPlan is not null)
            return DesignerLayoutModes.NormalizeMode(_activeLayoutExportPlan.EffectiveRootLayoutMode);

        return GetLayoutModeForParent(parentId);
    }

    private double GetExportParentLayoutSpacing(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(NormalizeId(parentId)) && _activeLayoutExportPlan is not null)
            return _activeLayoutExportPlan.StackSpacing;

        return GetParentLayoutSpacing(parentId);
    }

    private string GetExportParentLayoutOrientation(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(NormalizeId(parentId)) && _activeLayoutExportPlan?.UsesResponsiveStack == true)
            return DesignerLayoutModes.Vertical;

        return GetParentLayoutOrientation(parentId);
    }

    private string PlacementAttributes(DesignControlModel control)
    {
        var parentLayoutMode = GetExportLayoutModeForParent(control.ParentId);
        if (DesignerLayoutModes.IsAbsolute(parentLayoutMode))
            return CanvasLayoutAttributes(control);

        var builder = new StringBuilder(SizeAttributes(control.Width, control.Height));
        if (string.IsNullOrWhiteSpace(NormalizeId(control.ParentId)) && _activeLayoutExportPlan?.UsesResponsiveStack == true)
            return builder.ToString();

        var spacing = Math.Max(0, GetExportParentLayoutSpacing(control.ParentId));
        if (spacing <= 0)
            return builder.ToString();

        if (DesignerLayoutModes.NormalizeMode(parentLayoutMode) == DesignerLayoutModes.Grid
            || DesignerLayoutModes.NormalizeMode(parentLayoutMode) == DesignerLayoutModes.Flex)
        {
            builder.Append($" Margin=\"0,0,{ToInvariant(spacing)},{ToInvariant(spacing)}\"");
            return builder.ToString();
        }

        var orientation = DesignerLayoutModes.NormalizeOrientation(GetExportParentLayoutOrientation(control.ParentId));
        if (orientation == DesignerLayoutModes.Horizontal)
            builder.Append($" Margin=\"0,0,{ToInvariant(spacing)},0\"");
        else
            builder.Append($" Margin=\"0,0,0,{ToInvariant(spacing)}\"");

        return builder.ToString();
    }

    private void AppendRootContainerOpening(StringBuilder sb, bool usesManagedWindowLayout, double resolvedWidth, double resolvedHeight)
    {
        var layoutMode = DesignerLayoutModes.NormalizeMode(_activeLayoutExportPlan?.EffectiveRootLayoutMode ?? SurfaceLayoutMode);
        var sizeAttributes = usesManagedWindowLayout
            ? " HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\""
            : $" {SizeAttributes(resolvedWidth, resolvedHeight)}";
        var rootMargin = !string.IsNullOrWhiteSpace(_activeLayoutExportPlan?.RootMargin)
            ? $" Margin=\"{EscapeXml(_activeLayoutExportPlan.RootMargin)}\""
            : "";

        switch (layoutMode)
        {
            case DesignerLayoutModes.Stack:
                var orientation = _activeLayoutExportPlan?.UsesResponsiveStack == true
                    ? DesignerLayoutModes.Vertical
                    : SurfaceLayoutOrientation;
                var spacing = _activeLayoutExportPlan?.StackSpacing ?? SurfaceLayoutSpacing;
                sb.AppendLine($"  <StackPanel x:Name=\"RootLayout\" Orientation=\"{ToAvaloniaOrientation(orientation)}\" Spacing=\"{ToInvariant(spacing)}\"{rootMargin}{sizeAttributes}>");
                break;
            case DesignerLayoutModes.Grid:
                sb.AppendLine($"  <primitives:UniformGrid x:Name=\"RootLayout\" Columns=\"{Math.Max(1, SurfaceLayoutColumns)}\" Rows=\"{Math.Max(1, SurfaceLayoutRows)}\"{sizeAttributes}>");
                break;
            case DesignerLayoutModes.Flex:
                sb.AppendLine($"  <WrapPanel x:Name=\"RootLayout\" Orientation=\"{ToAvaloniaOrientation(SurfaceLayoutOrientation)}\"{sizeAttributes}>");
                break;
            default:
                sb.AppendLine(usesManagedWindowLayout
                    ? "  <Canvas x:Name=\"RootCanvas\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\">"
                    : $"  <Canvas x:Name=\"RootCanvas\" {SizeAttributes(resolvedWidth, resolvedHeight)}>");
                break;
        }
    }

    private string GetRootContainerClosingTag()
    {
        return DesignerLayoutModes.NormalizeMode(_activeLayoutExportPlan?.EffectiveRootLayoutMode ?? SurfaceLayoutMode) switch
        {
            DesignerLayoutModes.Stack => "  </StackPanel>",
            DesignerLayoutModes.Grid => "  </primitives:UniformGrid>",
            DesignerLayoutModes.Flex => "  </WrapPanel>",
            _ => "  </Canvas>"
        };
    }

    private static string CommonVisibilityAttributes(DesignControlModel control)
    {
        var builder = new StringBuilder();
        builder.Append($" Opacity=\"{ToInvariant(control.Opacity)}\"");
        if (!control.IsVisible)
            builder.Append(" IsVisible=\"False\"");
        return builder.ToString();
    }

    private static string TextStyleAttributes(DesignControlModel control)
    {
        return $" FontFamily=\"{EscapeXml(control.FontFamily)}\" FontSize=\"{ToInvariant(control.FontSize)}\" FontWeight=\"{EscapeXml(control.FontWeight)}\"";
    }

    private string BorderStyleAttributes(DesignControlModel control)
    {
        return $"{BorderBrushAttribute(control)} BorderThickness=\"{ToInvariant(control.BorderThickness)}\"";
    }

    private void AppendButtonXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var handlerName = $"{exportName}Click";
        var clickAttribute = ShouldGenerateDemoRuntimeCode || HasConfiguredInteractionEvent(control, InteractionModel.EventButtonClick)
            ? $" Click=\"{EscapeXml(handlerName)}\""
            : "";
        var contentAttribute = string.IsNullOrWhiteSpace(control.TextBindingPath)
            ? $"Content=\"{EscapeXml(control.Text)}\""
            : $"Content=\"{{Binding {EscapeXml(control.TextBindingPath)}}}\"";
        sb.AppendLine($"{Indent(indentLevel)}<Button x:Name=\"{EscapeXml(exportName)}\"{clickAttribute} {contentAttribute} {PlacementAttributes(control)}{BackgroundAttribute(control)}{ForegroundAttribute(control)}{TextStyleAttributes(control)}{BorderStyleAttributes(control)} Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)} />");
    }

    private void AppendGroupXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var children = GetChildControls(control.Id).ToList();
        sb.AppendLine($"{Indent(indentLevel)}<Canvas x:Name=\"{EscapeXml(exportName)}\" {PlacementAttributes(control)} ClipToBounds=\"True\"{CommonVisibilityAttributes(control)}>");

        foreach (var child in children)
            AppendChildControlXaml(sb, child, indentLevel + 1);

        sb.AppendLine($"{Indent(indentLevel)}</Canvas>");
    }

    private void AppendTextBoxXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var watermark = string.IsNullOrWhiteSpace(control.PlaceholderText) ? "" : $" Watermark=\"{EscapeXml(control.PlaceholderText)}\"";
        var textAttribute = string.IsNullOrWhiteSpace(control.TextBindingPath)
            ? $"Text=\"{EscapeXml(control.Text)}\""
            : $"Text=\"{{Binding {EscapeXml(control.TextBindingPath)}, Mode=TwoWay}}\"";
        var textChangedAttribute = HasConfiguredInteractionEvent(control, InteractionModel.EventTextBoxTextChanged)
            ? $" TextChanged=\"{EscapeXml(exportName)}_TextChanged\""
            : "";
        sb.AppendLine($"{Indent(indentLevel)}<TextBox x:Name=\"{EscapeXml(exportName)}\"{textChangedAttribute} {textAttribute} {PlacementAttributes(control)}{BackgroundAttribute(control)}{ForegroundAttribute(control)}{TextStyleAttributes(control)}{BorderStyleAttributes(control)} Padding=\"{ToInvariant(control.Padding)}\"{watermark}{CommonVisibilityAttributes(control)} />");
    }

    private void AppendTextBlockXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var textAttribute = string.IsNullOrWhiteSpace(control.TextBindingPath)
            ? $"Text=\"{EscapeXml(control.Text)}\""
            : $"Text=\"{{Binding {EscapeXml(control.TextBindingPath)}}}\"";
        sb.AppendLine($"{Indent(indentLevel)}<TextBlock x:Name=\"{EscapeXml(exportName)}\" {textAttribute} {PlacementAttributes(control)}{ForegroundAttribute(control)}{TextStyleAttributes(control)}{CommonVisibilityAttributes(control)} />");
    }

    private void AppendCheckBoxXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var contentAttribute = string.IsNullOrWhiteSpace(control.TextBindingPath)
            ? $"Content=\"{EscapeXml(control.Text)}\""
            : $"Content=\"{{Binding {EscapeXml(control.TextBindingPath)}}}\"";
        var checkedAttribute = HasConfiguredInteractionEvent(control, InteractionModel.EventCheckBoxChecked)
            ? $" Checked=\"{EscapeXml(exportName)}_Checked\""
            : "";
        var uncheckedAttribute = HasConfiguredInteractionEvent(control, InteractionModel.EventCheckBoxUnchecked)
            ? $" Unchecked=\"{EscapeXml(exportName)}_Unchecked\""
            : "";
        sb.AppendLine($"{Indent(indentLevel)}<CheckBox x:Name=\"{EscapeXml(exportName)}\"{checkedAttribute}{uncheckedAttribute} {contentAttribute} {PlacementAttributes(control)}{ForegroundAttribute(control)}{TextStyleAttributes(control)}{CommonVisibilityAttributes(control)} />");
    }

    private void AppendBorderXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var children = GetChildControls(control.Id).ToList();
        sb.AppendLine($"{Indent(indentLevel)}<Border x:Name=\"{EscapeXml(exportName)}\" {PlacementAttributes(control)}{BackgroundAttribute(control)}{BorderStyleAttributes(control)} CornerRadius=\"{ToInvariant(control.CornerRadius)}\" Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)}>");

        if (children.Count > 0 || !string.IsNullOrWhiteSpace(control.Text) || !string.IsNullOrWhiteSpace(control.TextBindingPath))
        {
            sb.AppendLine($"{Indent(indentLevel + 1)}<Canvas>");

            if (!string.IsNullOrWhiteSpace(control.Text) || !string.IsNullOrWhiteSpace(control.TextBindingPath))
            {
                var textAttribute = string.IsNullOrWhiteSpace(control.TextBindingPath)
                    ? $"Text=\"{EscapeXml(control.Text)}\""
                    : $"Text=\"{{Binding {EscapeXml(control.TextBindingPath)}}}\"";
                sb.AppendLine($"{Indent(indentLevel + 2)}<TextBlock {textAttribute}{ForegroundAttribute(control)}{TextStyleAttributes(control)} Canvas.Left=\"{ToInvariant(control.Padding)}\" Canvas.Top=\"{ToInvariant(control.Padding)}\" />");
            }

            foreach (var child in children)
                AppendChildControlXaml(sb, child, indentLevel + 2);

            sb.AppendLine($"{Indent(indentLevel + 1)}</Canvas>");
        }

        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private void AppendImageXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var source = string.IsNullOrWhiteSpace(control.ImageSource) ? "" : $" Source=\"{EscapeXml(control.ImageSource)}\"";
        sb.AppendLine($"{Indent(indentLevel)}<Border {PlacementAttributes(control)}{BackgroundAttribute(control)}{BorderStyleAttributes(control)} CornerRadius=\"{ToInvariant(control.CornerRadius)}\"{CommonVisibilityAttributes(control)}>");
        sb.AppendLine($"{Indent(indentLevel + 1)}<Image x:Name=\"{EscapeXml(exportName)}\"{source} Stretch=\"{EscapeXml(control.Stretch)}\" />");
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private void AppendStackLayoutXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var children = GetChildControls(control.Id).ToList();
        sb.AppendLine($"{Indent(indentLevel)}<Border x:Name=\"{EscapeXml(exportName)}\" {PlacementAttributes(control)}{BackgroundAttribute(control)}{BorderStyleAttributes(control)} CornerRadius=\"{ToInvariant(control.CornerRadius)}\" Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)}>");
        sb.AppendLine($"{Indent(indentLevel + 1)}<StackPanel Orientation=\"{ToAvaloniaOrientation(control.LayoutOrientation)}\" Spacing=\"{ToInvariant(control.LayoutSpacing)}\">");

        foreach (var child in children)
            AppendChildControlXaml(sb, child, indentLevel + 2);

        sb.AppendLine($"{Indent(indentLevel + 1)}</StackPanel>");
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private void AppendLayoutGridXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var children = GetChildControls(control.Id).ToList();
        sb.AppendLine($"{Indent(indentLevel)}<Border x:Name=\"{EscapeXml(exportName)}\" {PlacementAttributes(control)}{BackgroundAttribute(control)}{BorderStyleAttributes(control)} CornerRadius=\"{ToInvariant(control.CornerRadius)}\" Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)}>");
        if (ShouldIncludeExportComments && control.ShowGridLines)
            sb.AppendLine($"{Indent(indentLevel + 1)}<!-- Grid lines are shown in the designer preview; exported layout uses UniformGrid auto-placement. -->");
        sb.AppendLine($"{Indent(indentLevel + 1)}<primitives:UniformGrid Columns=\"{Math.Max(1, control.Columns)}\" Rows=\"{Math.Max(1, control.Rows)}\">");

        foreach (var child in children)
            AppendChildControlXaml(sb, child, indentLevel + 2);

        sb.AppendLine($"{Indent(indentLevel + 1)}</primitives:UniformGrid>");
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private void AppendFlexLayoutXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var children = GetChildControls(control.Id).ToList();
        sb.AppendLine($"{Indent(indentLevel)}<Border x:Name=\"{EscapeXml(exportName)}\" {PlacementAttributes(control)}{BackgroundAttribute(control)}{BorderStyleAttributes(control)} CornerRadius=\"{ToInvariant(control.CornerRadius)}\" Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)}>");
        sb.AppendLine($"{Indent(indentLevel + 1)}<WrapPanel Orientation=\"{ToAvaloniaOrientation(control.LayoutOrientation)}\">");

        foreach (var child in children)
            AppendChildControlXaml(sb, child, indentLevel + 2);

        sb.AppendLine($"{Indent(indentLevel + 1)}</WrapPanel>");
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private void AppendDataGridXaml(StringBuilder sb, DesignControlModel control, int indentLevel)
    {
        var exportName = GetExportControlName(control);
        var source = GetBindingSource(control.BindingSourceId);
        var themePalette = DesignerThemeCatalog.Get(FormTheme);
        var visibleFields = source is null
            ? new List<BindingFieldModel>()
            : OrderBindingFieldsForDisplay(source.Fields.Where(field => field.IsVisible)).ToList();
        var sortedFields = source?.Fields
            .Where(CanSortBindingField)
            .Where(field => !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
            .OrderBy(field => field.SortOrder < 0 ? int.MaxValue : field.SortOrder)
            .ThenBy(field => field.Header)
            .ToList() ?? new List<BindingFieldModel>();
        var groupedFields = control.AllowGrouping
            ? source?.Fields
            .Where(field => field.GroupOrder >= 0)
            .OrderBy(field => field.GroupOrder)
            .ThenBy(field => field.Header)
            .ToList() ?? new List<BindingFieldModel>()
            : new List<BindingFieldModel>();
        var summaryFields = control.ShowFooter
            ? visibleFields
                .Where(field => BindingFieldModel.NormalizeSummaryType(field.SummaryType) != BindingFieldModel.SummaryTypeNone)
                .ToList()
            : new List<BindingFieldModel>();
        var crudContext = ShouldGenerateDemoRuntimeCode ? GetCrudGenerationContext(source) : null;
        var itemsSourcePath = ShouldGenerateDemoRuntimeCode
            ? crudContext?.ViewCollectionPropertyName ?? source?.Path ?? ""
            : "";
        var selectedItemPath = ShouldGenerateDemoRuntimeCode
            ? crudContext?.SelectedItemPropertyName ?? ""
            : "";
        var itemsSource = string.IsNullOrWhiteSpace(itemsSourcePath) ? "" : $" ItemsSource=\"{{Binding {EscapeXml(itemsSourcePath)}}}\"";
        var selectedItem = string.IsNullOrWhiteSpace(selectedItemPath) ? "" : $" SelectedItem=\"{{Binding {EscapeXml(selectedItemPath)}, Mode=TwoWay}}\"";
        var rowBackground = ResolveBrushValue(control.DataGridRowBackground, themePalette.DataGridRowBackground, ThemeResourceKeys.DataGridRowBackgroundBrush);
        var alternatingRowBackground = control.DataGridShowAlternatingRows
            ? ResolveBrushValue(control.DataGridAlternateRowBackground, themePalette.DataGridAlternateRowBackground, ThemeResourceKeys.DataGridAlternateRowBackgroundBrush)
            : rowBackground;
        var dataGridTextAlignment = DesignControlModel.NormalizeDataGridTextAlignment(control.DataGridTextAlignment);
        var glowBrush = ResolveBrushValue(control.DataGridGlowColor, themePalette.AccentStrongBrush, ThemeResourceKeys.AccentStrongBrush);
        var headerBackground = ResolveBrushValue(control.DataGridHeaderBackground, control.Background, ThemeResourceKeys.DataGridHeaderBackgroundBrush);
        var headerForeground = ResolveBrushValue(control.DataGridHeaderForeground, control.Foreground, ThemeResourceKeys.DataGridHeaderForegroundBrush);
        var rowForeground = ResolveBrushValue(control.DataGridRowForeground, control.Foreground, ThemeResourceKeys.TextBrush);
        var hoverRowBackground = ResolveBrushValue(control.DataGridHoverRowBackground, "#EFF6FF", ThemeResourceKeys.AccentBrush);
        var selectedRowBackground = ResolveBrushValue(control.DataGridSelectedRowBackground, "#DBEAFE", ThemeResourceKeys.AccentBrush);
        var selectedRowForeground = ResolveBrushValue(control.DataGridSelectedRowForeground, control.Foreground, ThemeResourceKeys.TextBrush);
        var gridLineBrush = ResolveBrushValue(control.DataGridGridLineBrush, "#D7E2EE", ThemeResourceKeys.BorderBrush);
        var outerBorderBrush = ResolveBrushValue(control.DataGridOuterBorderBrush, themePalette.AccentStrongBrush, ThemeResourceKeys.AccentStrongBrush);
        var rowHeight = Math.Max(18, control.DataGridRowHeight);
        var headerHeight = Math.Max(24, control.DataGridHeaderHeight);
        var cellPadding = Math.Max(0, control.DataGridCellPadding);
        var headerBorderThickness = $"{(control.DataGridShowColumnLines ? "1" : "0")},{(control.DataGridShowRowLines ? "1" : "0")},0,{(control.DataGridShowRowLines ? "1" : "0")}";
        var cellBorderThickness = $"0,0,{(control.DataGridShowColumnLines ? "1" : "0")},{(control.DataGridShowRowLines ? "1" : "0")}";
        var gridLinesVisibility = (control.DataGridShowRowLines, control.DataGridShowColumnLines) switch
        {
            (true, true) => "All",
            (true, false) => "Horizontal",
            (false, true) => "Vertical",
            _ => "None"
        };
        var headersVisibility = control.DataGridShowHeader ? "Column" : "None";
        var filterableFields = visibleFields.Where(CanFilterBindingField).ToList();
        var shouldExportFilterRow = control.ShowFilterRow && source is not null && filterableFields.Count > 0;
        var shouldExportGroupPanel = source is not null
            && visibleFields.Count > 0
            && control.AllowGrouping
            && (control.ShowGroupPanel || groupedFields.Count > 0);
        var hasConfiguredSelectionInteractions = GetSelectionChangedSetPropertyInteractionsForGrid(control).Count > 0;
        var shouldExportSelectionChanged = GetExportableSelectionChangedInteractions().Any(item => item.Source.Id == control.Id);

        if (!ShouldExportRealDataGrid)
        {
            if (ShouldIncludeExportComments && hasConfiguredSelectionInteractions)
                sb.AppendLine($"{Indent(indentLevel)}<!-- Interaction SelectionChanged настроен в дизайнере, но portable placeholder не интерактивен. Для runtime-логики включите real DataGrid export и NuGet Avalonia.Controls.DataGrid. -->");
            AppendPortableDataGridPlaceholderXaml(
                sb,
                control,
                source,
                visibleFields,
                groupedFields,
                summaryFields,
                headerBackground,
                headerForeground,
                rowBackground,
                alternatingRowBackground,
                rowForeground,
                gridLineBrush,
                outerBorderBrush,
                rowHeight,
                headerHeight,
                cellPadding,
                indentLevel);
            return;
        }

        var shouldExportHost = shouldExportFilterRow || shouldExportGroupPanel;
        var hostRowCount = (shouldExportGroupPanel ? 1 : 0) + (shouldExportFilterRow ? 1 : 0);
        var dataGridIndent = shouldExportHost ? indentLevel + 1 : indentLevel;
        var hostNameAttribute = ShouldGenerateDemoRuntimeCode ? $" x:Name=\"{EscapeXml(exportName)}Host\"" : "";
        var dataGridNameAttribute = $" x:Name=\"{EscapeXml(exportName)}\"";
        var selectionChangedAttribute = shouldExportSelectionChanged ? $" SelectionChanged=\"{EscapeXml(exportName)}_SelectionChanged\"" : "";
        var dataGridPlacement = shouldExportHost
            ? $" Grid.Row=\"{hostRowCount}\""
            : $" {PlacementAttributes(control)}";
        var commonVisibilityAttributes = shouldExportHost ? "" : CommonVisibilityAttributes(control);

        if (ShouldIncludeExportComments)
            sb.AppendLine($"{Indent(indentLevel)}<!-- Требуется NuGet: Avalonia.Controls.DataGrid -->");
        if (shouldExportHost)
        {
            var rowDefinitions = string.Join(",", Enumerable.Repeat("Auto", hostRowCount).Concat(new[] { "*" }));
            sb.AppendLine($"{Indent(indentLevel)}<Grid{hostNameAttribute} {PlacementAttributes(control)} RowDefinitions=\"{rowDefinitions}\"{CommonVisibilityAttributes(control)}>");
            var nextHostRow = 0;

            if (shouldExportGroupPanel)
                AppendDataGridGroupPanelXaml(sb, groupedFields, headerBackground, gridLineBrush, indentLevel + 1, nextHostRow++);

            if (shouldExportFilterRow)
                AppendDataGridFilterRowXaml(sb, control, crudContext, visibleFields, headerBackground, gridLineBrush, cellPadding, indentLevel + 1, nextHostRow);
        }

        if (ShouldIncludeExportComments && !ShouldGenerateDemoRuntimeCode && source is not null)
            sb.AppendLine($"{Indent(dataGridIndent)}<!-- BindingSource '{EscapeXml(source.NameOrFallback())}' используется только как схема колонок в режиме «Чистый UI». Подключите ItemsSource в реальном ViewModel, когда будете готовы. -->");

        sb.AppendLine($"{Indent(dataGridIndent)}<dataGrid:DataGrid{dataGridNameAttribute}{dataGridPlacement} Background=\"{rowBackground}\" RowBackground=\"{rowBackground}\" Foreground=\"{rowForeground}\" BorderBrush=\"{outerBorderBrush}\" BorderThickness=\"{ToInvariant(control.BorderThickness)}\" FontFamily=\"{EscapeXml(control.FontFamily)}\" FontSize=\"{ToInvariant(control.DataGridRowFontSize)}\" FontWeight=\"{EscapeXml(control.DataGridRowFontWeight)}\" AutoGenerateColumns=\"{BoolToXaml(control.AutoGenerateColumns)}\" HeadersVisibility=\"{headersVisibility}\" GridLinesVisibility=\"{gridLinesVisibility}\" ColumnHeaderHeight=\"{ToInvariant(headerHeight)}\" RowHeight=\"{ToInvariant(rowHeight)}\"{itemsSource}{selectedItem}{selectionChangedAttribute}{commonVisibilityAttributes}>");
        sb.AppendLine($"{Indent(dataGridIndent + 1)}<dataGrid:DataGrid.Styles>");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}<Style Selector=\"DataGridColumnHeader\">");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Background\" Value=\"{headerBackground}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Foreground\" Value=\"{headerForeground}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"BorderBrush\" Value=\"{gridLineBrush}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"BorderThickness\" Value=\"{headerBorderThickness}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Padding\" Value=\"{ToInvariant(cellPadding)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"MinHeight\" Value=\"{ToInvariant(headerHeight)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"FontSize\" Value=\"{ToInvariant(control.DataGridHeaderFontSize)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"FontWeight\" Value=\"{EscapeXml(control.DataGridHeaderFontWeight)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}</Style>");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}<Style Selector=\"DataGridCell\">");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Foreground\" Value=\"{rowForeground}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"BorderBrush\" Value=\"{gridLineBrush}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"BorderThickness\" Value=\"{cellBorderThickness}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Padding\" Value=\"{ToInvariant(cellPadding)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"MinHeight\" Value=\"{ToInvariant(rowHeight)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"HorizontalContentAlignment\" Value=\"{dataGridTextAlignment}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}</Style>");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}<Style Selector=\"DataGridCell:pointerover\">");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Background\" Value=\"{hoverRowBackground}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}</Style>");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}<Style Selector=\"DataGridCell:selected\">");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Background\" Value=\"{selectedRowBackground}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"Foreground\" Value=\"{selectedRowForeground}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}</Style>");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}<Style Selector=\"DataGridRow\">");
        sb.AppendLine($"{Indent(dataGridIndent + 3)}<Setter Property=\"MinHeight\" Value=\"{ToInvariant(rowHeight)}\" />");
        sb.AppendLine($"{Indent(dataGridIndent + 2)}</Style>");
        sb.AppendLine($"{Indent(dataGridIndent + 1)}</dataGrid:DataGrid.Styles>");

        if (ShouldIncludeExportComments && (sortedFields.Count > 0 || groupedFields.Count > 0 || summaryFields.Count > 0))
        {
            sb.AppendLine($"{Indent(dataGridIndent + 1)}<!-- Метаданные конструктора:");
            if (sortedFields.Count > 0)
                sb.AppendLine($"{Indent(dataGridIndent + 2)}Сортировка: {EscapeXml(string.Join(", ", sortedFields.Select(field => $"{field.Path} {field.SortDirection} ({field.SortOrder})")))}");
            if (groupedFields.Count > 0)
                sb.AppendLine($"{Indent(dataGridIndent + 2)}Группировка: {EscapeXml(string.Join(", ", groupedFields.Select(field => $"{field.Path} ({field.GroupOrder})")))}");
            if (summaryFields.Count > 0)
            {
                sb.AppendLine($"{Indent(dataGridIndent + 2)}Итоги/Footer: {EscapeXml(string.Join(", ", summaryFields.Select(field => $"{field.Path} {BindingFieldModel.NormalizeSummaryType(field.SummaryType)}{(string.IsNullOrWhiteSpace(field.SummaryFormat) ? "" : $" [{field.SummaryFormat}]")}")))}");
                sb.AppendLine($"{Indent(dataGridIndent + 2)}Подсказка: создайте нижнюю summary-строку и пересчитывайте агрегаты по текущему ItemsSource/представлению.");
            }
            sb.AppendLine($"{Indent(dataGridIndent + 1)}-->");
        }

        if (!control.AutoGenerateColumns && source is not null && visibleFields.Count > 0)
        {
            sb.AppendLine($"{Indent(dataGridIndent + 1)}<dataGrid:DataGrid.Columns>");
            foreach (var field in visibleFields)
            {
                var headerAlignment = BindingFieldModel.NormalizeAlignment(field.HeaderAlignment);
                var textTrimming = BindingFieldModel.NormalizeTextTrimming(field.TextTrimming);
                var textWrapping = BindingFieldModel.NormalizeTextWrapping(field.TextWrapping);
                var maxLines = Math.Max(0, field.MaxLines);
                var minWidth = Math.Max(0, field.MinWidth);
                var maxWidth = Math.Max(0, field.MaxWidth);
                var minWidthAttribute = minWidth > 0 ? $" MinWidth=\"{ToInvariant(minWidth)}\"" : "";
                var maxWidthAttribute = maxWidth > 0 ? $" MaxWidth=\"{ToInvariant(Math.Max(minWidth, maxWidth))}\"" : "";
                var stringFormat = ToBindingStringFormat(field.FormatString);
                var stringFormatAttribute = string.IsNullOrWhiteSpace(stringFormat) ? "" : $" StringFormat=\"{EscapeXml(stringFormat)}\"";
                var nullTextAttribute = string.IsNullOrWhiteSpace(field.NullText) ? "" : $" TargetNullValue=\"{EscapeXml(field.NullText)}\"";
                var headerMaxLinesAttribute = maxLines > 0 ? $" MaxLines=\"{maxLines}\"" : "";

                sb.AppendLine($"{Indent(dataGridIndent + 2)}<dataGrid:DataGridTextColumn Header=\"{EscapeXml(field.Header)}\" SortMemberPath=\"{EscapeXml(field.Path)}\" Width=\"{EscapeXml(field.Width)}\" CanUserSort=\"{BoolToXaml(CanSortBindingField(field))}\" CanUserResize=\"{BoolToXaml(field.AllowResize)}\"{minWidthAttribute}{maxWidthAttribute}>");
                sb.AppendLine($"{Indent(dataGridIndent + 3)}<dataGrid:DataGridTextColumn.Binding>");
                sb.AppendLine($"{Indent(dataGridIndent + 4)}<Binding Path=\"{EscapeXml(field.Path)}\"{stringFormatAttribute}{nullTextAttribute} />");
                sb.AppendLine($"{Indent(dataGridIndent + 3)}</dataGrid:DataGridTextColumn.Binding>");
                sb.AppendLine($"{Indent(dataGridIndent + 3)}<dataGrid:DataGridTextColumn.HeaderTemplate>");
                sb.AppendLine($"{Indent(dataGridIndent + 4)}<DataTemplate>");
                sb.AppendLine($"{Indent(dataGridIndent + 5)}<TextBlock Text=\"{{Binding}}\" HorizontalAlignment=\"{headerAlignment}\" TextAlignment=\"{headerAlignment}\" TextTrimming=\"{textTrimming}\" TextWrapping=\"{textWrapping}\"{headerMaxLinesAttribute} />");
                sb.AppendLine($"{Indent(dataGridIndent + 4)}</DataTemplate>");
                sb.AppendLine($"{Indent(dataGridIndent + 3)}</dataGrid:DataGridTextColumn.HeaderTemplate>");
                sb.AppendLine($"{Indent(dataGridIndent + 2)}</dataGrid:DataGridTextColumn>");
            }
            sb.AppendLine($"{Indent(dataGridIndent + 1)}</dataGrid:DataGrid.Columns>");
        }

        sb.AppendLine($"{Indent(dataGridIndent)}</dataGrid:DataGrid>");
        if (shouldExportHost)
            sb.AppendLine($"{Indent(indentLevel)}</Grid>");
    }

    private void AppendPortableDataGridPlaceholderXaml(
        StringBuilder sb,
        DesignControlModel control,
        BindingSourceModel? source,
        IReadOnlyList<BindingFieldModel> visibleFields,
        IReadOnlyList<BindingFieldModel> groupedFields,
        IReadOnlyList<BindingFieldModel> summaryFields,
        string headerBackground,
        string headerForeground,
        string rowBackground,
        string alternatingRowBackground,
        string rowForeground,
        string gridLineBrush,
        string outerBorderBrush,
        double rowHeight,
        double headerHeight,
        double cellPadding,
        int indentLevel)
    {
        if (ShouldExportPlaceholderDataGrid)
        {
            var title = source is null || source.Fields.Count == 0 || visibleFields.Count == 0
                ? "DataGrid: добавьте BindingSource и поля"
                : "DataGrid: placeholder без NuGet";
            AppendPortableDataGridEmptyStateXaml(
                sb,
                control,
                title,
                rowBackground,
                outerBorderBrush,
                rowForeground,
                indentLevel);
            return;
        }

        if (source is null)
        {
            AppendPortableDataGridEmptyStateXaml(
                sb,
                control,
                "DataGrid: добавьте BindingSource и поля",
                rowBackground,
                outerBorderBrush,
                rowForeground,
                indentLevel);
            return;
        }

        if (source.Fields.Count == 0)
        {
            AppendPortableDataGridEmptyStateXaml(
                sb,
                control,
                "DataGrid: добавьте BindingSource и поля",
                rowBackground,
                outerBorderBrush,
                rowForeground,
                indentLevel);
            return;
        }

        if (visibleFields.Count == 0)
        {
            AppendPortableDataGridEmptyStateXaml(
                sb,
                control,
                "DataGrid: нет видимых колонок",
                rowBackground,
                outerBorderBrush,
                rowForeground,
                indentLevel);
            return;
        }

        var fields = visibleFields;
        var columnDefinitions = string.Join(",", fields.Select(field => ToPortableColumnDefinition(field.Width)));
        var filterableFields = fields.Where(CanFilterBindingField).ToList();
        var showGroupPanel = control.AllowGrouping && (control.ShowGroupPanel || groupedFields.Count > 0);
        var showFilterRow = control.ShowFilterRow && source is not null && filterableFields.Count > 0;
        var showFooter = control.ShowFooter && summaryFields.Count > 0;
        var sampleRowCount = Math.Clamp((int)Math.Floor(Math.Max(0, control.Height - headerHeight - 72) / Math.Max(18, rowHeight)), 3, 6);
        var borderThickness = Math.Max(0, control.BorderThickness);

        if (ShouldIncludeExportComments)
        {
            sb.AppendLine($"{Indent(indentLevel)}<!-- Безопасная визуальная таблица DataGrid. Использует только стандартные Avalonia controls и не требует Avalonia.Controls.DataGrid. -->");
            if (source is not null)
                sb.AppendLine($"{Indent(indentLevel)}<!-- BindingSource '{EscapeXml(source.NameOrFallback())}' используется как схема колонок. Когда подключите runtime data, можно заменить placeholder на настоящий DataGrid. -->");
        }

        var exportGroupPanel = showGroupPanel || IsFullStyledXamlExport;
        var exportHeader = control.DataGridShowHeader || IsFullStyledXamlExport;
        var exportFilterRow = showFilterRow || IsFullStyledXamlExport;
        var exportFooter = showFooter || IsFullStyledXamlExport;
        var rowDefinitions = new List<string>();
        var groupRowIndex = -1;
        var headerRowIndex = -1;
        var filterRowIndex = -1;
        var dataRowIndex = -1;
        var footerRowIndex = -1;

        if (exportGroupPanel)
        {
            groupRowIndex = rowDefinitions.Count;
            rowDefinitions.Add("Auto");
        }

        if (exportHeader)
        {
            headerRowIndex = rowDefinitions.Count;
            rowDefinitions.Add("Auto");
        }

        if (exportFilterRow)
        {
            filterRowIndex = rowDefinitions.Count;
            rowDefinitions.Add("Auto");
        }

        dataRowIndex = rowDefinitions.Count;
        rowDefinitions.Add("*");

        if (exportFooter)
        {
            footerRowIndex = rowDefinitions.Count;
            rowDefinitions.Add("Auto");
        }

        var groupVisibility = IsCompactXamlExport ? "" : $" IsVisible=\"{BoolToXaml(showGroupPanel)}\"";
        var headerVisibility = IsCompactXamlExport ? "" : $" IsVisible=\"{BoolToXaml(control.DataGridShowHeader)}\"";
        var filterVisibility = IsCompactXamlExport ? "" : $" IsVisible=\"{BoolToXaml(showFilterRow)}\"";
        var footerVisibility = IsCompactXamlExport ? "" : $" IsVisible=\"{BoolToXaml(showFooter)}\"";
        sb.AppendLine($"{Indent(indentLevel)}<Border {PlacementAttributes(control)} Background=\"{rowBackground}\" BorderBrush=\"{outerBorderBrush}\" BorderThickness=\"{ToInvariant(borderThickness)}\" CornerRadius=\"{ToInvariant(control.CornerRadius)}\" ClipToBounds=\"True\"{CommonVisibilityAttributes(control)}>");
        sb.AppendLine($"{Indent(indentLevel + 1)}<Grid RowDefinitions=\"{string.Join(",", rowDefinitions)}\">");

        if (exportGroupPanel)
        {
            sb.AppendLine($"{Indent(indentLevel + 2)}<Border Grid.Row=\"{groupRowIndex}\" Background=\"{headerBackground}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,0,0,1\" Padding=\"{ToInvariant(Math.Max(6, cellPadding))}\"{groupVisibility}>");
            if (groupedFields.Count > 0)
            {
                sb.AppendLine($"{Indent(indentLevel + 3)}<StackPanel Orientation=\"Horizontal\" Spacing=\"6\">");
                foreach (var field in groupedFields)
                    sb.AppendLine($"{Indent(indentLevel + 4)}<Border Background=\"#E0F2FE\" BorderBrush=\"#7DD3FC\" BorderThickness=\"1\" CornerRadius=\"999\" Padding=\"8,3\"><TextBlock Text=\"Группа: {EscapeXml(GetFieldDisplayHeader(field))}\" Foreground=\"#075985\" FontSize=\"12\" /></Border>");
                sb.AppendLine($"{Indent(indentLevel + 3)}</StackPanel>");
            }
            else
            {
                sb.AppendLine($"{Indent(indentLevel + 3)}<TextBlock Text=\"Перетащите колонку сюда для группировки\" Foreground=\"{headerForeground}\" Opacity=\"0.72\" />");
            }
            sb.AppendLine($"{Indent(indentLevel + 2)}</Border>");
        }

        if (exportHeader)
        {
            sb.AppendLine($"{Indent(indentLevel + 2)}<Grid Grid.Row=\"{headerRowIndex}\" ColumnDefinitions=\"{columnDefinitions}\" Background=\"{headerBackground}\" MinHeight=\"{ToInvariant(headerHeight)}\"{headerVisibility}>");
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                var headerAlignment = BindingFieldModel.NormalizeAlignment(field.HeaderAlignment);
                sb.AppendLine($"{Indent(indentLevel + 3)}<Border Grid.Column=\"{index}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,0,1,1\" Padding=\"{ToInvariant(Math.Max(6, cellPadding))}\">");
                sb.AppendLine($"{Indent(indentLevel + 4)}<TextBlock Text=\"{EscapeXml(GetFieldDisplayHeader(field))}\" Foreground=\"{headerForeground}\" FontSize=\"{ToInvariant(control.DataGridHeaderFontSize)}\" FontWeight=\"{EscapeXml(control.DataGridHeaderFontWeight)}\" HorizontalAlignment=\"{headerAlignment}\" TextAlignment=\"{headerAlignment}\" TextTrimming=\"CharacterEllipsis\" />");
                sb.AppendLine($"{Indent(indentLevel + 3)}</Border>");
            }
            sb.AppendLine($"{Indent(indentLevel + 2)}</Grid>");
        }

        if (exportFilterRow)
        {
            sb.AppendLine($"{Indent(indentLevel + 2)}<Grid Grid.Row=\"{filterRowIndex}\" ColumnDefinitions=\"{columnDefinitions}\" Background=\"{headerBackground}\"{filterVisibility}>");
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                sb.AppendLine($"{Indent(indentLevel + 3)}<Border Grid.Column=\"{index}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,0,1,1\" Padding=\"{ToInvariant(Math.Max(4, cellPadding * 0.5))}\">");
                if (field.AllowFilter)
                    sb.AppendLine($"{Indent(indentLevel + 4)}<TextBox Watermark=\"Фильтр: {EscapeXml(GetFieldDisplayHeader(field))}\" />");
                else
                    sb.AppendLine($"{Indent(indentLevel + 4)}<TextBlock Text=\"Без фильтра\" Opacity=\"0.55\" VerticalAlignment=\"Center\" />");
                sb.AppendLine($"{Indent(indentLevel + 3)}</Border>");
            }
            sb.AppendLine($"{Indent(indentLevel + 2)}</Grid>");
        }

        sb.AppendLine($"{Indent(indentLevel + 2)}<StackPanel Grid.Row=\"{dataRowIndex}\" Background=\"{rowBackground}\">");
        for (var rowIndex = 0; rowIndex < sampleRowCount; rowIndex++)
        {
            var rowBrush = control.DataGridShowAlternatingRows && rowIndex % 2 == 1 ? alternatingRowBackground : rowBackground;
            sb.AppendLine($"{Indent(indentLevel + 3)}<Grid ColumnDefinitions=\"{columnDefinitions}\" Background=\"{rowBrush}\" MinHeight=\"{ToInvariant(rowHeight)}\">");
            for (var columnIndex = 0; columnIndex < fields.Count; columnIndex++)
            {
                var field = fields[columnIndex];
                var cellAlignment = BindingFieldModel.NormalizeAlignment(field.CellAlignment);
                var trimming = BindingFieldModel.NormalizeTextTrimming(field.TextTrimming);
                var wrapping = BindingFieldModel.NormalizeTextWrapping(field.TextWrapping);
                var maxLines = Math.Max(0, field.MaxLines);
                var maxLinesAttribute = maxLines > 0 ? $" MaxLines=\"{maxLines}\"" : "";
                var sample = EscapeXml(GetVariantSampleValue(field, rowIndex));
                sb.AppendLine($"{Indent(indentLevel + 4)}<Border Grid.Column=\"{columnIndex}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,0,1,1\" Padding=\"{ToInvariant(Math.Max(6, cellPadding))}\">");
                sb.AppendLine($"{Indent(indentLevel + 5)}<TextBlock Text=\"{sample}\" Foreground=\"{rowForeground}\" FontSize=\"{ToInvariant(control.DataGridRowFontSize)}\" FontWeight=\"{EscapeXml(control.DataGridRowFontWeight)}\" HorizontalAlignment=\"{cellAlignment}\" TextAlignment=\"{cellAlignment}\" TextTrimming=\"{trimming}\" TextWrapping=\"{wrapping}\"{maxLinesAttribute} />");
                sb.AppendLine($"{Indent(indentLevel + 4)}</Border>");
            }
            sb.AppendLine($"{Indent(indentLevel + 3)}</Grid>");
        }
        sb.AppendLine($"{Indent(indentLevel + 2)}</StackPanel>");

        if (exportFooter)
        {
            sb.AppendLine($"{Indent(indentLevel + 2)}<Grid Grid.Row=\"{footerRowIndex}\" ColumnDefinitions=\"{columnDefinitions}\" Background=\"{headerBackground}\"{footerVisibility}>");
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                var summaryText = summaryFields.Any(summary => string.Equals(summary.Path, field.Path, StringComparison.OrdinalIgnoreCase))
                    ? BuildPortableSummaryText(field)
                    : "";
                sb.AppendLine($"{Indent(indentLevel + 3)}<Border Grid.Column=\"{index}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,1,1,0\" Padding=\"{ToInvariant(Math.Max(6, cellPadding))}\">");
                sb.AppendLine($"{Indent(indentLevel + 4)}<TextBlock Text=\"{EscapeXml(summaryText)}\" Foreground=\"{headerForeground}\" FontWeight=\"SemiBold\" TextTrimming=\"CharacterEllipsis\" />");
                sb.AppendLine($"{Indent(indentLevel + 3)}</Border>");
            }
            sb.AppendLine($"{Indent(indentLevel + 2)}</Grid>");
        }

        sb.AppendLine($"{Indent(indentLevel + 1)}</Grid>");
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private void AppendPortableDataGridEmptyStateXaml(
        StringBuilder sb,
        DesignControlModel control,
        string title,
        string background,
        string borderBrush,
        string foreground,
        int indentLevel)
    {
        var borderThickness = Math.Max(0, control.BorderThickness);
        const string placeholderBorderBrush = "#CBD5E1";
        const string placeholderForeground = "#64748B";
        sb.AppendLine($"{Indent(indentLevel)}<Border {PlacementAttributes(control)} Background=\"{background}\" BorderBrush=\"{placeholderBorderBrush}\" BorderThickness=\"{ToInvariant(borderThickness)}\" CornerRadius=\"{ToInvariant(control.CornerRadius)}\"{CommonVisibilityAttributes(control)}>");
        sb.AppendLine($"{Indent(indentLevel + 1)}<TextBlock Text=\"{EscapeXml(title)}\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Foreground=\"{placeholderForeground}\" TextAlignment=\"Center\" TextWrapping=\"Wrap\" />");
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private static string GetFieldDisplayHeader(BindingFieldModel field)
    {
        return string.IsNullOrWhiteSpace(field.Header)
            ? field.Path
            : field.Header;
    }

    private static string BuildPortableSummaryText(BindingFieldModel field)
    {
        var summaryType = BindingFieldModel.NormalizeSummaryType(field.SummaryType);
        return summaryType == BindingFieldModel.SummaryTypeNone
            ? ""
            : $"{summaryType}: {field.SampleValue}";
    }

    private static string ToPortableColumnDefinition(string? width)
    {
        var normalized = width?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "*";

        normalized = normalized.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.Equals(normalized, "Auto", StringComparison.OrdinalIgnoreCase))
            return "Auto";

        if (normalized.EndsWith("*", StringComparison.Ordinal)
            && (normalized.Length == 1 || double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            return normalized;
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels) && pixels > 0
            ? ToInvariant(pixels)
            : "*";
    }

    private void AppendDataGridFilterRowXaml(
        StringBuilder sb,
        DesignControlModel control,
        CrudGenerationContext? crudContext,
        IReadOnlyList<BindingFieldModel> visibleFields,
        string headerBackground,
        string gridLineBrush,
        double cellPadding,
        int indentLevel,
        int rowIndex)
    {
        var columnDefinitions = string.Join(",", visibleFields.Select(field => string.IsNullOrWhiteSpace(field.Width) ? "*" : EscapeXml(field.Width)));
        sb.AppendLine($"{Indent(indentLevel)}<Grid Grid.Row=\"{rowIndex}\" ColumnDefinitions=\"{columnDefinitions}\" Background=\"{headerBackground}\">");

        for (var index = 0; index < visibleFields.Count; index++)
        {
            var field = visibleFields[index];
            var filterTextBinding = crudContext is not null && field.AllowFilter
                ? $" Text=\"{{Binding {EscapeXml(GetColumnFilterPropertyName(crudContext, field))}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}}\""
                : "";

            sb.AppendLine($"{Indent(indentLevel + 1)}<Border Grid.Column=\"{index}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,0,1,1\" Padding=\"{ToInvariant(Math.Max(4, cellPadding * 0.5))}\">");
            if (field.AllowFilter)
            {
                sb.AppendLine($"{Indent(indentLevel + 3)}<TextBox Watermark=\"Фильтр: {EscapeXml(string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header)}\"{filterTextBinding} />");
            }
            else
            {
                sb.AppendLine($"{Indent(indentLevel + 3)}<TextBlock Text=\"Без фильтра\" Opacity=\"0.55\" VerticalAlignment=\"Center\" />");
            }
            sb.AppendLine($"{Indent(indentLevel + 1)}</Border>");
        }

        sb.AppendLine($"{Indent(indentLevel)}</Grid>");
    }

    private void AppendDataGridGroupPanelXaml(
        StringBuilder sb,
        IReadOnlyList<BindingFieldModel> groupedFields,
        string headerBackground,
        string gridLineBrush,
        int indentLevel,
        int rowIndex)
    {
        sb.AppendLine($"{Indent(indentLevel)}<Border Grid.Row=\"{rowIndex}\" Background=\"{headerBackground}\" BorderBrush=\"{gridLineBrush}\" BorderThickness=\"0,0,0,1\" Padding=\"10,8\">");
        if (groupedFields.Count == 0)
        {
            sb.AppendLine($"{Indent(indentLevel + 1)}<TextBlock Text=\"Перетащите колонку сюда для группировки\" Opacity=\"0.62\" VerticalAlignment=\"Center\" />");
        }
        else
        {
            sb.AppendLine($"{Indent(indentLevel + 1)}<WrapPanel>");
            foreach (var field in groupedFields)
            {
                sb.AppendLine($"{Indent(indentLevel + 2)}<Border Background=\"#E0F2FE\" BorderBrush=\"#7DD3FC\" BorderThickness=\"1\" CornerRadius=\"999\" Padding=\"10,5\" Margin=\"0,0,8,6\">");
                sb.AppendLine($"{Indent(indentLevel + 3)}<TextBlock Text=\"Группа {field.GroupOrder + 1}: {EscapeXml(field.Header)}\" Foreground=\"#0C4A6E\" FontWeight=\"SemiBold\" />");
                sb.AppendLine($"{Indent(indentLevel + 2)}</Border>");
            }
            sb.AppendLine($"{Indent(indentLevel + 1)}</WrapPanel>");
        }
        sb.AppendLine($"{Indent(indentLevel)}</Border>");
    }

    private DesignerDocumentFileModel CreateDocumentFileModel()
    {
        return new DesignerDocumentFileModel
        {
            DesignWidth = DesignWidth,
            DesignHeight = DesignHeight,
            SnapStep = SnapStep,
            IsGridSnapEnabled = IsGridSnapEnabled,
            IsControlSnapEnabled = IsControlSnapEnabled,
            SnapThreshold = SnapThreshold,
            SurfaceBackground = SurfaceBackground,
            SurfaceGridMinorColor = SurfaceGridMinorColor,
            SurfaceGridMajorColor = SurfaceGridMajorColor,
            SurfaceLayoutMode = SurfaceLayoutMode,
            SurfaceLayoutOrientation = SurfaceLayoutOrientation,
            SurfaceLayoutSpacing = SurfaceLayoutSpacing,
            SurfaceLayoutColumns = SurfaceLayoutColumns,
            SurfaceLayoutRows = SurfaceLayoutRows,
            FormTitle = FormTitle,
            FormTheme = FormTheme,
            FormWindowState = FormWindowState,
            FormStartupLocation = FormStartupLocation,
            FormCanResize = FormCanResize,
            FormShowInTaskbar = FormShowInTaskbar,
            FormTopmost = FormTopmost,
            FormHasSystemDecorations = FormHasSystemDecorations,
            Controls = Controls.Select(ToControlFileModel).ToList(),
            BindingSources = BindingSources.Select(ToBindingSourceFileModel).ToList(),
            Interactions = Interactions.Select(ToInteractionFileModel).ToList()
        };
    }

    private static InteractionFileModel ToInteractionFileModel(InteractionModel interaction)
    {
        return new InteractionFileModel
        {
            Id = interaction.Id,
            SourceControlName = interaction.SourceControlName,
            EventName = InteractionModel.NormalizeEventName(interaction.EventName),
            ActionType = string.IsNullOrWhiteSpace(interaction.ActionType) ? InteractionModel.ActionSetProperty : interaction.ActionType,
            TargetControlName = interaction.TargetControlName,
            TargetProperty = string.IsNullOrWhiteSpace(interaction.TargetProperty) ? InteractionModel.TargetPropertyText : interaction.TargetProperty,
            SourcePath = interaction.SourcePath,
            TextTemplate = interaction.TextTemplate,
            MessageTitle = interaction.MessageTitle
        };
    }

    private static InteractionModel FromInteractionFileModel(InteractionFileModel interactionFile)
    {
        return new InteractionModel
        {
            Id = string.IsNullOrWhiteSpace(interactionFile.Id) ? Guid.NewGuid().ToString("N") : interactionFile.Id,
            SourceControlName = interactionFile.SourceControlName,
            EventName = InteractionModel.NormalizeEventName(interactionFile.EventName),
            ActionType = string.IsNullOrWhiteSpace(interactionFile.ActionType) ? InteractionModel.ActionSetProperty : interactionFile.ActionType,
            TargetControlName = interactionFile.TargetControlName,
            TargetProperty = string.IsNullOrWhiteSpace(interactionFile.TargetProperty) ? InteractionModel.TargetPropertyText : interactionFile.TargetProperty,
            SourcePath = interactionFile.SourcePath,
            TextTemplate = interactionFile.TextTemplate,
            MessageTitle = interactionFile.MessageTitle
        };
    }

    private static DesignerControlFileModel ToControlFileModel(DesignControlModel control)
    {
        return new DesignerControlFileModel
        {
            Id = control.Id,
            Type = control.Type,
            Name = control.Name,
            DescriptorId = control.DescriptorId,
            PluginId = control.PluginId,
            PluginVersion = control.PluginVersion,
            ParentId = control.ParentId,
            Text = control.Text,
            PlaceholderText = control.PlaceholderText,
            ImageSource = control.ImageSource,
            Background = control.Background,
            Foreground = control.Foreground,
            BorderBrush = control.BorderBrush,
            BorderThickness = control.BorderThickness,
            CornerRadius = control.CornerRadius,
            FontFamily = control.FontFamily,
            FontSize = control.FontSize,
            FontWeight = control.FontWeight,
            Opacity = control.Opacity,
            Padding = control.Padding,
            LayoutOrientation = control.LayoutOrientation,
            LayoutSpacing = control.LayoutSpacing,
            IsVisible = control.IsVisible,
            IsLocked = control.IsLocked,
            Stretch = control.Stretch,
            X = control.X,
            Y = control.Y,
            Width = control.Width,
            Height = control.Height,
            AnchorLeft = control.AnchorLeft,
            AnchorTop = control.AnchorTop,
            AnchorRight = control.AnchorRight,
            AnchorBottom = control.AnchorBottom,
            Columns = control.Columns,
            Rows = control.Rows,
            ShowGridLines = control.ShowGridLines,
            AutoGenerateColumns = control.AutoGenerateColumns,
            BindingSourceId = control.BindingSourceId,
            TextBindingPath = control.TextBindingPath,
            GeneratedButtonActionKey = control.GeneratedButtonActionKey,
            DataGridGlowColor = control.DataGridGlowColor,
            DataGridRowBackground = control.DataGridRowBackground,
            DataGridAlternateRowBackground = control.DataGridAlternateRowBackground,
            DataGridTextAlignment = control.DataGridTextAlignment,
            DataGridHeaderBackground = control.DataGridHeaderBackground,
            DataGridHeaderForeground = control.DataGridHeaderForeground,
            DataGridRowForeground = control.DataGridRowForeground,
            DataGridHoverRowBackground = control.DataGridHoverRowBackground,
            DataGridSelectedRowBackground = control.DataGridSelectedRowBackground,
            DataGridSelectedRowForeground = control.DataGridSelectedRowForeground,
            DataGridGridLineBrush = control.DataGridGridLineBrush,
            DataGridOuterBorderBrush = control.DataGridOuterBorderBrush,
            DataGridHeaderFontSize = control.DataGridHeaderFontSize,
            DataGridHeaderFontWeight = control.DataGridHeaderFontWeight,
            DataGridRowFontSize = control.DataGridRowFontSize,
            DataGridRowFontWeight = control.DataGridRowFontWeight,
            DataGridHeaderHeight = control.DataGridHeaderHeight,
            DataGridRowHeight = control.DataGridRowHeight,
            DataGridCellPadding = control.DataGridCellPadding,
            DataGridShowHeader = control.DataGridShowHeader,
            DataGridShowRowLines = control.DataGridShowRowLines,
            DataGridShowColumnLines = control.DataGridShowColumnLines,
            DataGridShowAlternatingRows = control.DataGridShowAlternatingRows,
            ShowFilterRow = control.ShowFilterRow,
            FilterMode = DesignControlModel.NormalizeDataGridFilterMode(control.FilterMode),
            ShowGroupPanel = control.ShowGroupPanel,
            AllowGrouping = control.AllowGrouping,
            ShowFooter = control.ShowFooter,
            CustomProperties = control.CustomProperties.Select(property => new DesignPropertyValueFileModel
            {
                Key = property.Key,
                ValueJson = property.ValueJson
            }).ToList()
        };
    }

    private static BindingSourceFileModel ToBindingSourceFileModel(BindingSourceModel source)
    {
        return new BindingSourceFileModel
        {
            Id = source.Id,
            Name = source.Name,
            Path = source.Path,
            ItemTypeName = source.ItemTypeName,
            Description = source.Description,
            SourceKind = source.SourceKind,
            SourceAssemblyPath = source.SourceAssemblyPath,
            SourceTypeFullName = source.SourceTypeFullName,
            SourceTableName = source.SourceTableName,
            SourceConnectionString = source.SourceConnectionString,
            SourceSchemaName = source.SourceSchemaName,
            SourceQuery = source.SourceQuery,
            Fields = source.Fields.Select(ToBindingFieldFileModel).ToList()
        };
    }

    private static BindingFieldFileModel ToBindingFieldFileModel(BindingFieldModel field)
    {
        var allowSort = field.AllowSort && field.IsSortable;
        return new BindingFieldFileModel
        {
            Header = field.Header,
            Path = field.Path,
            SampleValue = field.SampleValue,
            Width = field.Width,
            TypeName = field.TypeName,
            IsVisible = field.IsVisible,
            IsSortable = allowSort,
            SortDirection = field.SortDirection,
            SortOrder = field.SortOrder,
            GroupOrder = field.GroupOrder,
            HeaderAlignment = BindingFieldModel.NormalizeAlignment(field.HeaderAlignment),
            CellAlignment = BindingFieldModel.NormalizeAlignment(field.CellAlignment),
            FormatString = field.FormatString,
            NullText = field.NullText,
            TextTrimming = BindingFieldModel.NormalizeTextTrimming(field.TextTrimming),
            TextWrapping = BindingFieldModel.NormalizeTextWrapping(field.TextWrapping),
            MaxLines = Math.Max(0, field.MaxLines),
            MinWidth = Math.Max(0, field.MinWidth),
            MaxWidth = Math.Max(0, field.MaxWidth),
            AllowResize = field.AllowResize,
            AllowSort = allowSort,
            AllowFilter = field.AllowFilter,
            VisibleIndex = Math.Max(-1, field.VisibleIndex),
            SummaryType = BindingFieldModel.NormalizeSummaryType(field.SummaryType),
            SummaryFormat = field.SummaryFormat
        };
    }

    private static BindingFieldModel FromBindingFieldFileModel(BindingFieldFileModel fieldFile)
    {
        var allowSort = fieldFile.IsSortable && fieldFile.AllowSort;
        return new BindingFieldModel
        {
            Header = fieldFile.Header,
            Path = fieldFile.Path,
            SampleValue = fieldFile.SampleValue,
            Width = fieldFile.Width,
            TypeName = fieldFile.TypeName,
            IsVisible = fieldFile.IsVisible,
            IsSortable = allowSort,
            SortDirection = string.IsNullOrWhiteSpace(fieldFile.SortDirection) ? BindingFieldModel.SortDirectionNone : fieldFile.SortDirection,
            SortOrder = fieldFile.SortOrder,
            GroupOrder = fieldFile.GroupOrder,
            HeaderAlignment = BindingFieldModel.NormalizeAlignment(fieldFile.HeaderAlignment),
            CellAlignment = BindingFieldModel.NormalizeAlignment(fieldFile.CellAlignment),
            FormatString = fieldFile.FormatString,
            NullText = fieldFile.NullText,
            TextTrimming = BindingFieldModel.NormalizeTextTrimming(fieldFile.TextTrimming),
            TextWrapping = BindingFieldModel.NormalizeTextWrapping(fieldFile.TextWrapping),
            MaxLines = Math.Max(0, fieldFile.MaxLines),
            MinWidth = Math.Max(0, fieldFile.MinWidth),
            MaxWidth = Math.Max(0, fieldFile.MaxWidth),
            AllowResize = fieldFile.AllowResize,
            AllowSort = allowSort,
            AllowFilter = fieldFile.AllowFilter,
            VisibleIndex = Math.Max(-1, fieldFile.VisibleIndex),
            SummaryType = BindingFieldModel.NormalizeSummaryType(fieldFile.SummaryType),
            SummaryFormat = fieldFile.SummaryFormat
        };
    }

    private static BindingSourceModel FromBindingSourceFileModel(BindingSourceFileModel sourceFile)
    {
        var source = new BindingSourceModel
        {
            Id = string.IsNullOrWhiteSpace(sourceFile.Id) ? Guid.NewGuid().ToString("N") : sourceFile.Id,
            Name = string.IsNullOrWhiteSpace(sourceFile.Name) ? "Source" : sourceFile.Name,
            Path = string.IsNullOrWhiteSpace(sourceFile.Path) ? "Items" : sourceFile.Path,
            ItemTypeName = string.IsNullOrWhiteSpace(sourceFile.ItemTypeName) ? "ItemRow" : sourceFile.ItemTypeName,
            Description = sourceFile.Description,
            SourceKind = string.IsNullOrWhiteSpace(sourceFile.SourceKind) ? "Manual" : sourceFile.SourceKind,
            SourceAssemblyPath = sourceFile.SourceAssemblyPath,
            SourceTypeFullName = sourceFile.SourceTypeFullName,
            SourceTableName = sourceFile.SourceTableName,
            SourceConnectionString = sourceFile.SourceConnectionString,
            SourceSchemaName = string.IsNullOrWhiteSpace(sourceFile.SourceSchemaName) ? "dbo" : sourceFile.SourceSchemaName,
            SourceQuery = sourceFile.SourceQuery
        };

        foreach (var fieldFile in sourceFile.Fields)
            source.Fields.Add(FromBindingFieldFileModel(fieldFile));

        return source;
    }

    private static DesignControlModel FromControlFileModel(DesignerControlFileModel controlFile)
    {
        var model = new DesignControlModel
        {
            Id = string.IsNullOrWhiteSpace(controlFile.Id) ? Guid.NewGuid().ToString("N") : controlFile.Id,
            Type = controlFile.Type,
            Name = controlFile.Name,
            DescriptorId = controlFile.DescriptorId,
            PluginId = controlFile.PluginId,
            PluginVersion = controlFile.PluginVersion,
            ParentId = NormalizeId(controlFile.ParentId),
            Text = controlFile.Text,
            PlaceholderText = controlFile.PlaceholderText,
            ImageSource = controlFile.ImageSource,
            Background = controlFile.Background,
            Foreground = controlFile.Foreground,
            BorderBrush = controlFile.BorderBrush,
            BorderThickness = controlFile.BorderThickness,
            CornerRadius = controlFile.CornerRadius,
            FontFamily = string.IsNullOrWhiteSpace(controlFile.FontFamily) ? "Inter" : controlFile.FontFamily,
            FontSize = controlFile.FontSize,
            FontWeight = string.IsNullOrWhiteSpace(controlFile.FontWeight) ? "Normal" : controlFile.FontWeight,
            Opacity = controlFile.Opacity,
            Padding = controlFile.Padding,
            LayoutOrientation = DesignerLayoutModes.NormalizeOrientation(controlFile.LayoutOrientation),
            LayoutSpacing = controlFile.LayoutSpacing,
            IsVisible = controlFile.IsVisible,
            IsLocked = controlFile.IsLocked,
            Stretch = string.IsNullOrWhiteSpace(controlFile.Stretch) ? "Uniform" : controlFile.Stretch,
            X = controlFile.X,
            Y = controlFile.Y,
            Width = controlFile.Width,
            Height = controlFile.Height,
            AnchorLeft = controlFile.AnchorLeft,
            AnchorTop = controlFile.AnchorTop,
            AnchorRight = controlFile.AnchorRight,
            AnchorBottom = controlFile.AnchorBottom,
            Columns = controlFile.Columns,
            Rows = controlFile.Rows,
            ShowGridLines = controlFile.ShowGridLines,
            AutoGenerateColumns = controlFile.AutoGenerateColumns,
            BindingSourceId = controlFile.BindingSourceId,
            TextBindingPath = controlFile.TextBindingPath,
            GeneratedButtonActionKey = controlFile.GeneratedButtonActionKey,
            DataGridGlowColor = string.IsNullOrWhiteSpace(controlFile.DataGridGlowColor)
                ? DesignerThemeCatalog.Get(DesignerThemeCatalog.Light).AccentStrongBrush
                : controlFile.DataGridGlowColor,
            DataGridRowBackground = string.IsNullOrWhiteSpace(controlFile.DataGridRowBackground)
                ? DesignerThemeCatalog.Get(DesignerThemeCatalog.Light).DataGridRowBackground
                : controlFile.DataGridRowBackground,
            DataGridAlternateRowBackground = string.IsNullOrWhiteSpace(controlFile.DataGridAlternateRowBackground)
                ? DesignerThemeCatalog.Get(DesignerThemeCatalog.Light).DataGridAlternateRowBackground
                : controlFile.DataGridAlternateRowBackground,
            DataGridTextAlignment = DesignControlModel.NormalizeDataGridTextAlignment(controlFile.DataGridTextAlignment),
            DataGridHeaderBackground = string.IsNullOrWhiteSpace(controlFile.DataGridHeaderBackground)
                ? controlFile.Background
                : controlFile.DataGridHeaderBackground,
            DataGridHeaderForeground = string.IsNullOrWhiteSpace(controlFile.DataGridHeaderForeground)
                ? controlFile.Foreground
                : controlFile.DataGridHeaderForeground,
            DataGridRowForeground = string.IsNullOrWhiteSpace(controlFile.DataGridRowForeground)
                ? controlFile.Foreground
                : controlFile.DataGridRowForeground,
            DataGridHoverRowBackground = string.IsNullOrWhiteSpace(controlFile.DataGridHoverRowBackground)
                ? "#EFF6FF"
                : controlFile.DataGridHoverRowBackground,
            DataGridSelectedRowBackground = string.IsNullOrWhiteSpace(controlFile.DataGridSelectedRowBackground)
                ? "#DBEAFE"
                : controlFile.DataGridSelectedRowBackground,
            DataGridSelectedRowForeground = string.IsNullOrWhiteSpace(controlFile.DataGridSelectedRowForeground)
                ? controlFile.Foreground
                : controlFile.DataGridSelectedRowForeground,
            DataGridGridLineBrush = string.IsNullOrWhiteSpace(controlFile.DataGridGridLineBrush)
                ? controlFile.BorderBrush
                : controlFile.DataGridGridLineBrush,
            DataGridOuterBorderBrush = string.IsNullOrWhiteSpace(controlFile.DataGridOuterBorderBrush)
                ? controlFile.DataGridGlowColor
                : controlFile.DataGridOuterBorderBrush,
            DataGridHeaderFontSize = controlFile.DataGridHeaderFontSize <= 0 ? controlFile.FontSize : controlFile.DataGridHeaderFontSize,
            DataGridHeaderFontWeight = string.IsNullOrWhiteSpace(controlFile.DataGridHeaderFontWeight) ? "SemiBold" : controlFile.DataGridHeaderFontWeight,
            DataGridRowFontSize = controlFile.DataGridRowFontSize <= 0 ? controlFile.FontSize : controlFile.DataGridRowFontSize,
            DataGridRowFontWeight = string.IsNullOrWhiteSpace(controlFile.DataGridRowFontWeight) ? controlFile.FontWeight : controlFile.DataGridRowFontWeight,
            DataGridHeaderHeight = controlFile.DataGridHeaderHeight <= 0 ? 46 : controlFile.DataGridHeaderHeight,
            DataGridRowHeight = controlFile.DataGridRowHeight <= 0 ? 36 : controlFile.DataGridRowHeight,
            DataGridCellPadding = controlFile.DataGridCellPadding < 0 ? 0 : controlFile.DataGridCellPadding,
            DataGridShowHeader = controlFile.DataGridShowHeader,
            DataGridShowRowLines = controlFile.DataGridShowRowLines,
            DataGridShowColumnLines = controlFile.DataGridShowColumnLines,
            DataGridShowAlternatingRows = controlFile.DataGridShowAlternatingRows,
            ShowFilterRow = controlFile.ShowFilterRow,
            FilterMode = DesignControlModel.NormalizeDataGridFilterMode(controlFile.FilterMode),
            ShowGroupPanel = controlFile.ShowGroupPanel,
            AllowGrouping = controlFile.AllowGrouping,
            ShowFooter = controlFile.ShowFooter
        };

        foreach (var property in controlFile.CustomProperties)
        {
            model.CustomProperties.Add(new DesignPropertyValueModel
            {
                Key = property.Key,
                ValueJson = property.ValueJson
            });
        }

        return model;
    }

    private void CreateNewDocumentCore(bool markAsSaved, bool resetDocumentSession = true)
    {
        var normalizedTheme = DesignerThemeCatalog.NormalizeThemeName(FormTheme);
        var palette = DesignerThemeCatalog.Get(normalizedTheme);
        var emptyDocument = new DesignerDocumentFileModel
        {
            DesignWidth = 1200,
            DesignHeight = 800,
            SnapStep = 10,
            IsGridSnapEnabled = true,
            IsControlSnapEnabled = true,
            SnapThreshold = 6,
            SurfaceBackground = palette.SurfaceBackground,
            SurfaceGridMinorColor = palette.SurfaceGridMinorColor,
            SurfaceGridMajorColor = palette.SurfaceGridMajorColor,
            SurfaceLayoutMode = DesignerLayoutModes.Absolute,
            SurfaceLayoutOrientation = DesignerLayoutModes.Vertical,
            SurfaceLayoutSpacing = 12,
            SurfaceLayoutColumns = 3,
            SurfaceLayoutRows = 3,
            FormTitle = "Form1",
            FormTheme = normalizedTheme,
            FormWindowState = WindowStateNormal,
            FormStartupLocation = StartupLocationCenterScreen,
            FormCanResize = true,
            FormShowInTaskbar = true,
            FormTopmost = false,
            FormHasSystemDecorations = true
        };

        ApplyDocument(emptyDocument, "", markAsSaved, resetDocumentSession);
    }

    private void ApplyDocument(
        DesignerDocumentFileModel document,
        string? sourcePath,
        bool markAsSaved,
        bool resetDocumentSession = true,
        bool resetHistory = true)
    {
        _isHistorySuspended = true;
        _isApplyingDocument = true;

        try
        {
            SelectedControl = null;
            Controls.Clear();
            BindingSources.Clear();
            Interactions.Clear();

            var normalizedTheme = DesignerThemeCatalog.NormalizeThemeName(
                string.IsNullOrWhiteSpace(document.FormTheme)
                    ? DesignerThemeCatalog.InferThemeName(document.SurfaceBackground)
                    : document.FormTheme);
            var palette = DesignerThemeCatalog.Get(normalizedTheme);

            FormTheme = normalizedTheme;
            _activeFormTheme = normalizedTheme;
            DesignWidth = Math.Max(300, document.DesignWidth);
            DesignHeight = Math.Max(200, document.DesignHeight);
            SnapStep = Math.Max(1, document.SnapStep);
            IsGridSnapEnabled = document.IsGridSnapEnabled;
            IsControlSnapEnabled = document.IsControlSnapEnabled;
            SnapThreshold = Math.Clamp(document.SnapThreshold, 1, 40);
            SurfaceBackground = string.IsNullOrWhiteSpace(document.SurfaceBackground) ? palette.SurfaceBackground : document.SurfaceBackground;
            SurfaceGridMinorColor = string.IsNullOrWhiteSpace(document.SurfaceGridMinorColor) ? palette.SurfaceGridMinorColor : document.SurfaceGridMinorColor;
            SurfaceGridMajorColor = string.IsNullOrWhiteSpace(document.SurfaceGridMajorColor) ? palette.SurfaceGridMajorColor : document.SurfaceGridMajorColor;
            SurfaceLayoutMode = DesignerLayoutModes.NormalizeMode(document.SurfaceLayoutMode);
            SurfaceLayoutOrientation = DesignerLayoutModes.NormalizeOrientation(document.SurfaceLayoutOrientation);
            SurfaceLayoutSpacing = Math.Max(0, document.SurfaceLayoutSpacing);
            SurfaceLayoutColumns = Math.Max(1, document.SurfaceLayoutColumns);
            SurfaceLayoutRows = Math.Max(1, document.SurfaceLayoutRows);
            FormTitle = string.IsNullOrWhiteSpace(document.FormTitle) ? "Form1" : document.FormTitle;
            FormWindowState = NormalizeFormWindowState(document.FormWindowState);
            FormStartupLocation = NormalizeFormStartupLocation(document.FormStartupLocation);
            FormCanResize = document.FormCanResize;
            FormShowInTaskbar = document.FormShowInTaskbar;
            FormTopmost = document.FormTopmost;
            FormHasSystemDecorations = document.FormHasSystemDecorations;

            foreach (var sourceFile in document.BindingSources)
                BindingSources.Add(FromBindingSourceFileModel(sourceFile));

            foreach (var controlFile in document.Controls)
            {
                var runtimeControl = FromControlFileModel(controlFile);
                runtimeControl.Name = string.IsNullOrWhiteSpace(runtimeControl.Name)
                    ? GetUniqueControlName(controlFile.Type)
                    : runtimeControl.Name;
                runtimeControl.DataGridRowBackground = string.IsNullOrWhiteSpace(runtimeControl.DataGridRowBackground)
                    ? palette.DataGridRowBackground
                    : runtimeControl.DataGridRowBackground;
                runtimeControl.DataGridAlternateRowBackground = string.IsNullOrWhiteSpace(runtimeControl.DataGridAlternateRowBackground)
                    ? palette.DataGridAlternateRowBackground
                    : runtimeControl.DataGridAlternateRowBackground;
                runtimeControl.DataGridTextAlignment = DesignControlModel.NormalizeDataGridTextAlignment(runtimeControl.DataGridTextAlignment);
                runtimeControl.DataGridGlowColor = string.IsNullOrWhiteSpace(runtimeControl.DataGridGlowColor)
                    ? palette.AccentStrongBrush
                    : runtimeControl.DataGridGlowColor;
                runtimeControl.DataGridHeaderBackground = string.IsNullOrWhiteSpace(runtimeControl.DataGridHeaderBackground)
                    ? runtimeControl.Background
                    : runtimeControl.DataGridHeaderBackground;
                runtimeControl.DataGridHeaderForeground = string.IsNullOrWhiteSpace(runtimeControl.DataGridHeaderForeground)
                    ? runtimeControl.Foreground
                    : runtimeControl.DataGridHeaderForeground;
                runtimeControl.DataGridRowForeground = string.IsNullOrWhiteSpace(runtimeControl.DataGridRowForeground)
                    ? runtimeControl.Foreground
                    : runtimeControl.DataGridRowForeground;
                runtimeControl.DataGridHoverRowBackground = string.IsNullOrWhiteSpace(runtimeControl.DataGridHoverRowBackground)
                    ? "#EFF6FF"
                    : runtimeControl.DataGridHoverRowBackground;
                runtimeControl.DataGridSelectedRowBackground = string.IsNullOrWhiteSpace(runtimeControl.DataGridSelectedRowBackground)
                    ? "#DBEAFE"
                    : runtimeControl.DataGridSelectedRowBackground;
                runtimeControl.DataGridSelectedRowForeground = string.IsNullOrWhiteSpace(runtimeControl.DataGridSelectedRowForeground)
                    ? runtimeControl.Foreground
                    : runtimeControl.DataGridSelectedRowForeground;
                runtimeControl.DataGridGridLineBrush = string.IsNullOrWhiteSpace(runtimeControl.DataGridGridLineBrush)
                    ? runtimeControl.BorderBrush
                    : runtimeControl.DataGridGridLineBrush;
                runtimeControl.DataGridOuterBorderBrush = string.IsNullOrWhiteSpace(runtimeControl.DataGridOuterBorderBrush)
                    ? runtimeControl.DataGridGlowColor
                    : runtimeControl.DataGridOuterBorderBrush;
                Controls.Add(runtimeControl);
            }

            foreach (var interactionFile in document.Interactions)
                Interactions.Add(FromInteractionFileModel(interactionFile));

            ClampAllControlsToSurface();
            SelectedBindingSource = BindingSources.FirstOrDefault();
            CurrentDocumentPath = sourcePath ?? "";
            if (resetDocumentSession)
                DocumentSessionId = Guid.NewGuid().ToString("N");
        }
        finally
        {
            _isApplyingDocument = false;
            _isHistorySuspended = false;
        }

        if (resetHistory)
            ResetHistory(markAsSaved);

        ClearSelection();
        NotifyDesignerStateChanged(trackHistory: false);
    }

    private void RestoreFromSnapshot(string snapshot)
    {
        // Undo/redo хранит состояние целиком в JSON.
        // При восстановлении проще и надежнее пересобрать документ полностью.
        var currentPath = CurrentDocumentPath;
        var selectedControlIds = SelectedControlIds.ToList();
        var primaryControlId = SelectedControl?.Id ?? "";
        var selectedBindingSourceId = SelectedBindingSource?.Id ?? "";
        var selectedInteractionId = SelectedInteraction?.Id ?? "";

        ApplyDocument(
            JsonSerializer.Deserialize<DesignerDocumentFileModel>(snapshot, JsonOptions) ?? new DesignerDocumentFileModel(),
            currentPath,
            markAsSaved: false,
            resetDocumentSession: false,
            resetHistory: false);
        _currentSnapshot = snapshot;
        _lastHistoryMutationUtc = DateTime.UtcNow;

        RestoreSelectionContextAfterSnapshot(
            selectedControlIds,
            primaryControlId,
            selectedBindingSourceId,
            selectedInteractionId);

        RaiseDocumentStateProperties();
        RefreshDiagnostics();
        MarkExportCacheStale();
        DesignerChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreSelectionContextAfterSnapshot(
        IReadOnlyList<string> selectedControlIds,
        string primaryControlId,
        string selectedBindingSourceId,
        string selectedInteractionId)
    {
        var restoredSelection = selectedControlIds
            .Select(GetControl)
            .Where(control => control is not null)
            .Cast<DesignControlModel>()
            .ToList();

        var primaryControl = GetControl(primaryControlId)
            ?? restoredSelection.LastOrDefault();
        if (primaryControl is not null && restoredSelection.All(control => control.Id != primaryControl.Id))
            restoredSelection.Add(primaryControl);
        SetSelection(restoredSelection, primaryControl);

        if (!string.IsNullOrWhiteSpace(selectedBindingSourceId))
            SelectedBindingSource = BindingSources.FirstOrDefault(source => string.Equals(source.Id, selectedBindingSourceId, StringComparison.OrdinalIgnoreCase))
                ?? BindingSources.FirstOrDefault();

        SelectedInteraction = !string.IsNullOrWhiteSpace(selectedInteractionId)
            ? Interactions.FirstOrDefault(interaction => string.Equals(interaction.Id, selectedInteractionId, StringComparison.OrdinalIgnoreCase))
            : null;

        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        RaiseInteractionLookupProperties();
    }

    private void ResetHistory(bool markAsSaved)
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _currentSnapshot = JsonSerializer.Serialize(CreateDocumentFileModel(), JsonOptions);
        _savedSnapshot = markAsSaved ? _currentSnapshot : _savedSnapshot;
        if (!markAsSaved && string.IsNullOrWhiteSpace(_savedSnapshot))
            _savedSnapshot = "";
        _lastHistoryMutationUtc = DateTime.UtcNow;
        RaiseDocumentStateProperties();
    }

    private IReadOnlyList<string> BuildHistorySnapshots()
    {
        var snapshots = new List<string>(_undoStack.Count + 1 + _redoStack.Count);
        snapshots.AddRange(_undoStack.Reverse());

        if (!string.IsNullOrWhiteSpace(_currentSnapshot))
            snapshots.Add(_currentSnapshot);

        snapshots.AddRange(_redoStack);
        return snapshots;
    }

    private void RebuildUndoRedoHistoryItems()
    {
        var snapshots = BuildHistorySnapshots();
        var currentIndex = _undoStack.Count;

        UndoRedoHistoryItems.Clear();

        for (var index = 0; index < snapshots.Count; index++)
        {
            var description = index == 0
                ? ("Начальное состояние", "Документ создан, открыт или восстановлен.")
                : DescribeHistoryTransition(snapshots[index - 1], snapshots[index]);
            var isCurrent = index == currentIndex;

            UndoRedoHistoryItems.Add(new UndoRedoHistoryItemModel
            {
                Index = index,
                Snapshot = snapshots[index],
                Title = description.Item1,
                Description = description.Item2,
                PositionText = (index + 1).ToString("00", CultureInfo.InvariantCulture),
                StateText = isCurrent
                    ? "Текущее"
                    : index < currentIndex ? "Undo" : "Redo",
                IsCurrent = isCurrent,
                IsPast = index < currentIndex,
                IsFuture = index > currentIndex
            });
        }
    }

    private static (string Title, string Description) DescribeHistoryTransition(string previousSnapshot, string snapshot)
    {
        var previous = TryReadHistoryDocument(previousSnapshot);
        var current = TryReadHistoryDocument(snapshot);
        if (previous is null || current is null)
            return ("Изменение документа", "Снимок состояния документа обновлен.");

        var previousControls = previous.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        var currentControls = current.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        var addedControls = current.Controls.Where(control => !previousControls.ContainsKey(control.Id)).ToList();
        var removedControls = previous.Controls.Where(control => !currentControls.ContainsKey(control.Id)).ToList();

        if (addedControls.Any(control => string.Equals(control.Type, DesignerControlTypes.Group, StringComparison.OrdinalIgnoreCase)))
            return ("Сгруппированы элементы", "Создана группа и элементы перенесены внутрь неё.");

        if (removedControls.Any(control => string.Equals(control.Type, DesignerControlTypes.Group, StringComparison.OrdinalIgnoreCase)))
            return ("Разгруппированы элементы", "Группа удалена, а дочерние элементы возвращены на поверхность.");

        if (addedControls.Count == 1)
            return ($"Добавлен {GetHistoryControlCaption(addedControls[0])}", "Элемент появился на дизайнерской поверхности.");

        if (addedControls.Count > 1)
            return ($"Добавлены элементы: {addedControls.Count}", "На форму добавлено несколько элементов.");

        if (removedControls.Count == 1)
            return ($"Удален {GetHistoryControlCaption(removedControls[0])}", "Элемент удален из документа.");

        if (removedControls.Count > 1)
            return ($"Удалены элементы: {removedControls.Count}", "Из документа удалено несколько элементов.");

        foreach (var currentControl in current.Controls)
        {
            if (!previousControls.TryGetValue(currentControl.Id, out var previousControl))
                continue;

            var caption = GetHistoryControlCaption(currentControl);
            if (!string.Equals(previousControl.Name, currentControl.Name, StringComparison.Ordinal))
                return ($"Переименован {GetHistoryControlTypeName(currentControl.Type)}", $"Новое имя элемента: {currentControl.Name}.");

            if (Math.Abs(previousControl.X - currentControl.X) > 0.01 || Math.Abs(previousControl.Y - currentControl.Y) > 0.01)
                return ($"Перемещен {caption}", $"Позиция: X {currentControl.X:0}, Y {currentControl.Y:0}.");

            if (Math.Abs(previousControl.Width - currentControl.Width) > 0.01 && Math.Abs(previousControl.Height - currentControl.Height) > 0.01)
                return ($"Изменен размер {caption}", $"Размер: {currentControl.Width:0} x {currentControl.Height:0}.");

            if (Math.Abs(previousControl.Width - currentControl.Width) > 0.01)
                return ($"Изменена ширина {caption}", $"Ширина: {currentControl.Width:0}.");

            if (Math.Abs(previousControl.Height - currentControl.Height) > 0.01)
                return ($"Изменена высота {caption}", $"Высота: {currentControl.Height:0}.");

            if (!string.Equals(previousControl.BindingSourceId, currentControl.BindingSourceId, StringComparison.Ordinal))
                return ($"Изменен BindingSource у {caption}", "Элемент привязан к другому источнику данных.");

            if (!string.Equals(previousControl.Text, currentControl.Text, StringComparison.Ordinal))
                return ($"Изменен текст {caption}", string.IsNullOrWhiteSpace(currentControl.Text) ? "Текст очищен." : $"Текст: {currentControl.Text}.");

            if (previousControl.IsVisible != currentControl.IsVisible)
                return (currentControl.IsVisible ? $"Показан {caption}" : $"Скрыт {caption}", "Изменена видимость элемента.");

            if (previousControl.IsLocked != currentControl.IsLocked)
                return (currentControl.IsLocked ? $"Заблокирован {caption}" : $"Разблокирован {caption}", "Изменена защита элемента от редактирования.");

            if (!string.Equals(previousControl.ParentId, currentControl.ParentId, StringComparison.Ordinal))
                return ($"Изменена структура {caption}", "Элемент перенесен в другой контейнер или слой.");

            if (IsControlAppearanceChanged(previousControl, currentControl))
                return ($"Изменен внешний вид {caption}", "Обновлены цвета, шрифт, границы или визуальный preset.");

            if (IsControlDataGridBehaviorChanged(previousControl, currentControl))
                return ($"Изменены настройки DataGrid {currentControl.Name}", "Обновлены фильтр, группировка, footer, колонки или поведение таблицы.");

            if (HasCustomPropertiesChanged(previousControl, currentControl))
                return ($"Изменены plugin-свойства {caption}", "Обновлены descriptor-driven custom properties.");
        }

        var previousSources = previous.BindingSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var currentSources = current.BindingSources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        var addedSources = current.BindingSources.Where(source => !previousSources.ContainsKey(source.Id)).ToList();
        var removedSources = previous.BindingSources.Where(source => !currentSources.ContainsKey(source.Id)).ToList();

        if (addedSources.Count == 1)
            return ($"Добавлен BindingSource «{addedSources[0].Name}»", "Создан новый источник данных.");

        if (removedSources.Count == 1)
            return ($"Удален BindingSource «{removedSources[0].Name}»", "Источник данных удален из документа.");

        foreach (var currentSource in current.BindingSources)
        {
            if (!previousSources.TryGetValue(currentSource.Id, out var previousSource))
                continue;

            if (previousSource.Fields.Count != currentSource.Fields.Count)
                return ($"Изменены поля BindingSource «{currentSource.Name}»", $"Колонок: {currentSource.Fields.Count}.");

            if (JsonSerializer.Serialize(previousSource, JsonOptions) != JsonSerializer.Serialize(currentSource, JsonOptions))
                return ($"Изменен BindingSource «{currentSource.Name}»", "Обновлены параметры подключения, таблица, запрос или свойства полей.");
        }

        if (IsFormLayoutChanged(previous, current))
            return ("Изменены параметры формы", $"Размер формы: {current.DesignWidth:0} x {current.DesignHeight:0}.");

        if (JsonSerializer.Serialize(previous, JsonOptions) != JsonSerializer.Serialize(current, JsonOptions))
            return ("Изменение документа", "Обновлены свойства формы, элементов или источников данных.");

        return ("Без изменений", "Состояние совпадает с предыдущим снимком.");
    }

    private static DesignerDocumentFileModel? TryReadHistoryDocument(string snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<DesignerDocumentFileModel>(snapshot, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetHistoryControlCaption(DesignerControlFileModel control)
    {
        var typeName = GetHistoryControlTypeName(control.Type);
        return string.IsNullOrWhiteSpace(control.Name) ? typeName : $"{typeName} «{control.Name}»";
    }

    private static string GetHistoryControlTypeName(string type)
    {
        return string.IsNullOrWhiteSpace(type) ? "элемент" : type;
    }

    private static bool IsControlAppearanceChanged(DesignerControlFileModel previous, DesignerControlFileModel current)
    {
        return previous.Background != current.Background
            || previous.Foreground != current.Foreground
            || previous.BorderBrush != current.BorderBrush
            || Math.Abs(previous.BorderThickness - current.BorderThickness) > 0.01
            || Math.Abs(previous.CornerRadius - current.CornerRadius) > 0.01
            || previous.FontFamily != current.FontFamily
            || Math.Abs(previous.FontSize - current.FontSize) > 0.01
            || previous.FontWeight != current.FontWeight
            || Math.Abs(previous.Opacity - current.Opacity) > 0.01
            || Math.Abs(previous.Padding - current.Padding) > 0.01
            || previous.ImageSource != current.ImageSource;
    }

    private static bool IsControlDataGridBehaviorChanged(DesignerControlFileModel previous, DesignerControlFileModel current)
    {
        return previous.AutoGenerateColumns != current.AutoGenerateColumns
            || previous.ShowFilterRow != current.ShowFilterRow
            || previous.FilterMode != current.FilterMode
            || previous.ShowGroupPanel != current.ShowGroupPanel
            || previous.AllowGrouping != current.AllowGrouping
            || previous.ShowFooter != current.ShowFooter
            || previous.DataGridHeaderBackground != current.DataGridHeaderBackground
            || previous.DataGridHeaderForeground != current.DataGridHeaderForeground
            || previous.DataGridRowBackground != current.DataGridRowBackground
            || previous.DataGridAlternateRowBackground != current.DataGridAlternateRowBackground
            || previous.DataGridRowForeground != current.DataGridRowForeground
            || previous.DataGridGridLineBrush != current.DataGridGridLineBrush
            || previous.DataGridOuterBorderBrush != current.DataGridOuterBorderBrush
            || Math.Abs(previous.DataGridHeaderHeight - current.DataGridHeaderHeight) > 0.01
            || Math.Abs(previous.DataGridRowHeight - current.DataGridRowHeight) > 0.01
            || Math.Abs(previous.DataGridCellPadding - current.DataGridCellPadding) > 0.01;
    }

    private static bool HasCustomPropertiesChanged(DesignerControlFileModel previous, DesignerControlFileModel current)
    {
        return JsonSerializer.Serialize(previous.CustomProperties, JsonOptions)
            != JsonSerializer.Serialize(current.CustomProperties, JsonOptions);
    }

    private static bool IsFormLayoutChanged(DesignerDocumentFileModel previous, DesignerDocumentFileModel current)
    {
        return Math.Abs(previous.DesignWidth - current.DesignWidth) > 0.01
            || Math.Abs(previous.DesignHeight - current.DesignHeight) > 0.01
            || previous.FormTitle != current.FormTitle
            || previous.FormTheme != current.FormTheme
            || previous.FormWindowState != current.FormWindowState
            || previous.FormStartupLocation != current.FormStartupLocation
            || previous.SurfaceBackground != current.SurfaceBackground
            || previous.SurfaceLayoutMode != current.SurfaceLayoutMode
            || previous.SurfaceLayoutOrientation != current.SurfaceLayoutOrientation
            || Math.Abs(previous.SurfaceLayoutSpacing - current.SurfaceLayoutSpacing) > 0.01
            || previous.SurfaceLayoutColumns != current.SurfaceLayoutColumns
            || previous.SurfaceLayoutRows != current.SurfaceLayoutRows;
    }

    private int GetControlDepth(DesignControlModel control)
    {
        var depth = 0;
        var parent = GetControl(control.ParentId);
        while (parent is not null)
        {
            depth++;
            parent = GetControl(parent.ParentId);
        }

        return depth;
    }

    private bool IsDescendant(string? controlId, string ancestorId)
    {
        var current = GetControl(controlId);

        while (current is not null)
        {
            if (current.Id == ancestorId)
                return true;

            current = GetControl(current.ParentId);
        }

        return false;
    }

    private IEnumerable<DesignControlModel> GetControlAndDescendants(DesignControlModel root)
    {
        yield return root;

        foreach (var child in GetChildControls(root.Id).ToList())
        {
            foreach (var descendant in GetControlAndDescendants(child))
                yield return descendant;
        }
    }

    public IReadOnlyList<DesignControlModel> GetSelectedRootControls()
    {
        var selected = GetSelectedControls();
        return selected
            .Where(control => !selected.Any(other => other.Id != control.Id && IsDescendant(control.ParentId, other.Id)))
            .ToList();
    }

    private bool CanGroupSelectedControls()
    {
        var roots = GetVisibleEditableSelectedRootControls();
        if (roots.Count < 2)
            return false;

        var parentId = NormalizeId(roots[0].ParentId);
        return roots.All(control => NormalizeId(control.ParentId) == parentId);
    }

    private CrudGenerationContext? GetPreferredCrudContext()
    {
        var contexts = BuildCrudGenerationContexts();
        if (contexts.Count == 0)
            return null;

        var selectedGridSourceId = GetSelectedControls()
            .Where(control => control.Type == DesignerControlTypes.DataGrid)
            .Select(control => control.BindingSourceId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        if (!string.IsNullOrWhiteSpace(selectedGridSourceId))
            return contexts.FirstOrDefault(context => string.Equals(context.Source.Id, selectedGridSourceId, StringComparison.OrdinalIgnoreCase));

        if (SelectedBindingSource is not null)
            return contexts.FirstOrDefault(context => context.Source.Id == SelectedBindingSource.Id) ?? contexts[0];

        return contexts[0];
    }

    private List<DesignControlModel> GetControlsForSuggestedBindings(string controlType)
    {
        var selectedTargets = GetSelectedControls()
            .Where(control => control.Type == controlType)
            .OrderBy(control => control.Y)
            .ThenBy(control => control.X)
            .ThenBy(control => control.Name)
            .ToList();

        if (selectedTargets.Count > 0)
            return selectedTargets;

        return Controls
            .Where(control => control.Type == controlType)
            .OrderBy(control => control.Y)
            .ThenBy(control => control.X)
            .ThenBy(control => control.Name)
            .ToList();
    }

    private void SetSelection(IEnumerable<DesignControlModel> controls, DesignControlModel? primaryControl)
    {
        _isUpdatingSelectionState = true;
        SelectedControlIds.Clear();

        foreach (var control in controls.DistinctBy(control => control.Id))
            SelectedControlIds.Add(control.Id);

        SelectedControl = primaryControl;
        _isUpdatingSelectionState = false;
        RaiseSelectionProperties();
        RefreshStructureSelection();
    }

    private void SelectedControlIds_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdatingSelectionState)
            return;

        RaiseSelectionProperties();
        RefreshStructureSelection();
    }

    private void RecentFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecentFiles));
        OnPropertyChanged(nameof(RecentFilesSummary));
    }

    private void Diagnostics_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseDiagnosticsProperties();
    }

    private void RegisterHistorySnapshot()
    {
        if (_isHistorySuspended)
            return;

        // Группируем частые мелкие изменения в один шаг истории,
        // чтобы перетаскивание мышью не засоряло undo сотнями записей.
        var snapshot = JsonSerializer.Serialize(CreateDocumentFileModel(), JsonOptions);
        if (snapshot == _currentSnapshot)
            return;

        var now = DateTime.UtcNow;
        var shouldCreateUndoEntry = _undoStack.Count == 0 || now - _lastHistoryMutationUtc > HistoryGroupingWindow || _redoStack.Count > 0;

        if (shouldCreateUndoEntry && !string.IsNullOrWhiteSpace(_currentSnapshot))
            _undoStack.Push(_currentSnapshot);

        _redoStack.Clear();
        _currentSnapshot = snapshot;
        _lastHistoryMutationUtc = now;
        RaiseDocumentStateProperties();
    }

    private void RaiseDocumentStateProperties()
    {
        RebuildUndoRedoHistoryItems();
        OnPropertyChanged(nameof(HasUndo));
        OnPropertyChanged(nameof(HasRedo));
        OnPropertyChanged(nameof(HasUndoRedoHistory));
        OnPropertyChanged(nameof(UndoRedoHistoryCurrentIndex));
        OnPropertyChanged(nameof(UndoRedoHistoryTotalCount));
        OnPropertyChanged(nameof(UndoRedoHistorySummary));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(DirtyStateText));
        OnPropertyChanged(nameof(CurrentDocumentDisplayName));
        OnPropertyChanged(nameof(FormWindowDecorationsSummary));
        RaiseExportCacheProperties();
    }

    private void RaiseDiagnosticsProperties()
    {
        ApplyStructureDiagnosticsBadges();
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(HasNoDiagnostics));
        OnPropertyChanged(nameof(IsDiagnosticsPaneCollapsed));
        OnPropertyChanged(nameof(IsDiagnosticsPaneBodyVisible));
        OnPropertyChanged(nameof(HasDiagnosticErrors));
        OnPropertyChanged(nameof(HasDiagnosticWarnings));
        OnPropertyChanged(nameof(DiagnosticErrorCount));
        OnPropertyChanged(nameof(DiagnosticWarningCount));
        OnPropertyChanged(nameof(DiagnosticInfoCount));
        OnPropertyChanged(nameof(DiagnosticsPaneHostHeight));
        OnPropertyChanged(nameof(DiagnosticsPaneToggleText));
        OnPropertyChanged(nameof(DiagnosticsCompactSummary));
        OnPropertyChanged(nameof(DiagnosticsSummary));
        OnPropertyChanged(nameof(DiagnosticsStateText));
        OnPropertyChanged(nameof(ProblemsDockButtonText));
        OnPropertyChanged(nameof(ProblemsPanelTitle));
        OnPropertyChanged(nameof(ProblemsRailSummary));
        OnPropertyChanged(nameof(IsCompactDiagnosticsBarVisible));
        OnPropertyChanged(nameof(EditorShellLayoutSummary));
        RaiseProblemsFilterProperties();
    }

    private void RaiseProblemsFilterProperties()
    {
        OnPropertyChanged(nameof(FilteredDiagnostics));
        OnPropertyChanged(nameof(HasFilteredDiagnostics));
        OnPropertyChanged(nameof(HasNoFilteredDiagnostics));
        OnPropertyChanged(nameof(ProblemsFilterSummary));
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(HasSelectedControl));
        OnPropertyChanged(nameof(IsContextualToolbarVisible));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(CanDuplicateSelected));
        OnPropertyChanged(nameof(CanLockSelected));
        OnPropertyChanged(nameof(CanUnlockSelected));
        OnPropertyChanged(nameof(CanGroupSelection));
        OnPropertyChanged(nameof(CanUngroupSelection));
        OnPropertyChanged(nameof(CanSelectedControlHostChildren));
        OnPropertyChanged(nameof(CanCopySelection));
        OnPropertyChanged(nameof(CanSaveSelectionAsTemplate));
        OnPropertyChanged(nameof(CanPasteSelection));
        OnPropertyChanged(nameof(CanCopyStyle));
        OnPropertyChanged(nameof(CanPasteStyle));
        OnPropertyChanged(nameof(CanChangeZOrder));
        OnPropertyChanged(nameof(CanArrangeSelection));
        OnPropertyChanged(nameof(CanDistributeSelection));
        OnPropertyChanged(nameof(CanApplyButtonVisualPresets));
        OnPropertyChanged(nameof(CanEditDataGridBasic));
        OnPropertyChanged(nameof(CanEditCommonBackground));
        OnPropertyChanged(nameof(CanEditCommonForeground));
        OnPropertyChanged(nameof(CanEditCommonBorder));
        OnPropertyChanged(nameof(CanEditCommonFont));
        OnPropertyChanged(nameof(CanEditClassicBindingEditor));
        OnPropertyChanged(nameof(CanApplyDataGridVisualPresets));
        OnPropertyChanged(nameof(CanEditSelectedLayoutFlow));
        OnPropertyChanged(nameof(CanEditSelectedLayoutGrid));
        OnPropertyChanged(nameof(CanEditSelectedAbsolutePosition));
        OnPropertyChanged(nameof(CanEditSelectedAnchors));
        OnPropertyChanged(nameof(IsSelectedControlManagedByLayout));
        OnPropertyChanged(nameof(SelectedControlLayoutHint));
        OnPropertyChanged(nameof(SelectedLockStateSummary));
        OnPropertyChanged(nameof(SelectedControlSummary));
        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        RefreshEditorCommands();
    }

    private void RaiseBindingEditorProperties()
    {
        OnPropertyChanged(nameof(CanEditFieldBinding));
        OnPropertyChanged(nameof(CanEditBindingEditor));
        OnPropertyChanged(nameof(CanEditClassicBindingEditor));
        OnPropertyChanged(nameof(CanChooseBindingField));
        OnPropertyChanged(nameof(AvailableBindingFieldsForControl));
        OnPropertyChanged(nameof(SelectedBindingSourceForControl));
        OnPropertyChanged(nameof(SelectedBindingFieldForControl));
        OnPropertyChanged(nameof(SelectedDataGridBindingSummary));
        OnPropertyChanged(nameof(SelectedBindingEditorSummary));
        OnPropertyChanged(nameof(SelectedBindingPreviewFields));
        OnPropertyChanged(nameof(HasSelectedBindingPreview));
        OnPropertyChanged(nameof(SelectedBindingPreviewTitle));
        OnPropertyChanged(nameof(HasGridColumnWidthEditor));
        OnPropertyChanged(nameof(SelectedGridColumnsForControl));
        OnPropertyChanged(nameof(SelectedGridColumnEditorTitle));
        OnPropertyChanged(nameof(SelectedGridColumnEditorSummary));
        OnPropertyChanged(nameof(SelectedGridColumnCompactSummary));
    }

    private void RaiseInteractionDesignerProperties()
    {
        OnPropertyChanged(nameof(CanEditDataGridInteractions));
        OnPropertyChanged(nameof(SelectedDataGridInteractions));
        OnPropertyChanged(nameof(HasSelectedDataGridInteractions));
        OnPropertyChanged(nameof(InteractionSourceFieldPaths));
        OnPropertyChanged(nameof(InteractionDesignerSummary));
        OnPropertyChanged(nameof(HasInteractions));
        OnPropertyChanged(nameof(HasNoInteractions));
        OnPropertyChanged(nameof(CanEditSelectedInteraction));
        OnPropertyChanged(nameof(LogicDesignerSummary));
        OnPropertyChanged(nameof(SelectedInteractionEventHint));
        OnPropertyChanged(nameof(SelectedInteractionActionHint));
        OnPropertyChanged(nameof(SelectedInteractionTargetPropertyHint));
    }

    private void RaiseInteractionLookupProperties()
    {
        OnPropertyChanged(nameof(InteractionTargetControlNames));
        OnPropertyChanged(nameof(InteractionSourceControlNames));
        OnPropertyChanged(nameof(InteractionValuePathHints));
    }

    private static InteractionOptionModel? FindInteractionOption(
        IEnumerable<InteractionOptionModel> options,
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildControlTree(IReadOnlyDictionary<string, List<DesignControlModel>>? customOrderByParent = null)
    {
        var source = Controls.ToList();
        var rebuilt = new List<DesignControlModel>();

        List<DesignControlModel> ResolveChildren(string? parentId)
        {
            var normalizedParentId = NormalizeId(parentId);
            if (customOrderByParent is not null && customOrderByParent.TryGetValue(normalizedParentId, out var ordered))
                return ordered;

            return source
                .Where(control => NormalizeId(control.ParentId) == normalizedParentId)
                .ToList();
        }

        void AddSubtree(DesignControlModel control)
        {
            rebuilt.Add(control);

            foreach (var child in ResolveChildren(control.Id))
                AddSubtree(child);
        }

        foreach (var root in ResolveChildren(null))
            AddSubtree(root);

        _isStructureTreeRefreshSuspended = true;
        _isHistorySuspended = true;
        Controls.Clear();
        foreach (var control in rebuilt)
            Controls.Add(control);
        _isHistorySuspended = false;
        _isStructureTreeRefreshSuspended = false;
        RebuildStructureTree();
    }

    private void ReorderSelection(bool toFront)
    {
        var selectedRoots = GetEditableSelectedRootControls().ToList();
        if (selectedRoots.Count == 0)
            return;

        var source = Controls.ToList();
        var orderByParent = new Dictionary<string, List<DesignControlModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in selectedRoots.GroupBy(control => NormalizeId(control.ParentId)))
        {
            var parentId = group.Key;
            var currentSiblings = source.Where(control => NormalizeId(control.ParentId) == parentId).ToList();
            var selectedIds = group.Select(control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedSiblings = currentSiblings.Where(control => selectedIds.Contains(control.Id)).ToList();
            var otherSiblings = currentSiblings.Where(control => !selectedIds.Contains(control.Id)).ToList();

            orderByParent[parentId] = toFront
                ? otherSiblings.Concat(selectedSiblings).ToList()
                : selectedSiblings.Concat(otherSiblings).ToList();
        }

        List<DesignControlModel> ResolveChildren(string parentId)
        {
            if (orderByParent.TryGetValue(parentId, out var ordered))
                return ordered;

            return source.Where(control => NormalizeId(control.ParentId) == parentId).ToList();
        }

        var rebuilt = new List<DesignControlModel>();

        void AddSubtree(DesignControlModel control)
        {
            rebuilt.Add(control);
            foreach (var child in ResolveChildren(control.Id))
                AddSubtree(child);
        }

        foreach (var root in ResolveChildren(""))
            AddSubtree(root);

        _isStructureTreeRefreshSuspended = true;
        _isHistorySuspended = true;
        Controls.Clear();
        foreach (var control in rebuilt)
            Controls.Add(control);
        _isHistorySuspended = false;
        _isStructureTreeRefreshSuspended = false;
        RebuildStructureTree();

        NotifyDesignerStateChanged();
        StatusText = toFront ? "Выделенное перемещено на передний план" : "Выделенное перемещено на задний план";
    }

    private void AlignSelectionCore(SelectionAlignment alignment)
    {
        var selectedRoots = GetVisibleEditableSelectedRootControls().ToList();
        var anchor = GetSelectionAnchorControl(selectedRoots);
        if (anchor is null || selectedRoots.Count < 2)
            return;

        BeginUndoBatch();
        try
        {
        var anchorAbsolute = GetAbsolutePosition(anchor);
        var anchorLeft = anchorAbsolute.X;
        var anchorTop = anchorAbsolute.Y;
        var anchorRight = anchorAbsolute.X + anchor.Width;
        var anchorBottom = anchorAbsolute.Y + anchor.Height;

        foreach (var control in selectedRoots.Where(control => control.Id != anchor.Id))
        {
            var currentAbsolute = GetAbsolutePosition(control);
            var targetAbsoluteX = currentAbsolute.X;
            var targetAbsoluteY = currentAbsolute.Y;

            switch (alignment)
            {
                case SelectionAlignment.Left:
                    targetAbsoluteX = anchorLeft;
                    break;
                case SelectionAlignment.Top:
                    targetAbsoluteY = anchorTop;
                    break;
                case SelectionAlignment.Right:
                    targetAbsoluteX = anchorRight - control.Width;
                    break;
                case SelectionAlignment.Center:
                    targetAbsoluteX = anchorLeft + (anchor.Width - control.Width) / 2;
                    break;
                case SelectionAlignment.Bottom:
                    targetAbsoluteY = anchorBottom - control.Height;
                    break;
                case SelectionAlignment.Middle:
                    targetAbsoluteY = anchorTop + (anchor.Height - control.Height) / 2;
                    break;
            }

            var local = ToLocalPosition(control.ParentId, targetAbsoluteX, targetAbsoluteY);
            control.X = Snap(local.X);
            control.Y = Snap(local.Y);
            ClampControlToSurface(control);
        }

        NotifyDesignerStateChanged();
        StatusText = alignment switch
        {
            SelectionAlignment.Left => "Выделенное выровнено по левому краю",
            SelectionAlignment.Top => "Выделенное выровнено по верхнему краю",
            SelectionAlignment.Right => "Выделенное выровнено по правому краю",
            SelectionAlignment.Bottom => "Выделенное выровнено по нижнему краю",
            SelectionAlignment.Center => "Выделенное выровнено по центру",
            SelectionAlignment.Middle => "Выделенное выровнено по середине",
            _ => "Выделенное выровнено"
        };
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    private void DistributeSelectionCore(bool distributeHorizontally)
    {
        var selectedRoots = GetVisibleEditableSelectedRootControls()
            .Select(control => new
            {
                Control = control,
                Bounds = GetAbsoluteBounds(control)
            })
            .OrderBy(item => distributeHorizontally ? item.Bounds.X : item.Bounds.Y)
            .ToList();

        if (selectedRoots.Count < 3)
            return;

        BeginUndoBatch();
        try
        {
        var start = distributeHorizontally
            ? selectedRoots.Min(item => item.Bounds.X)
            : selectedRoots.Min(item => item.Bounds.Y);
        var end = distributeHorizontally
            ? selectedRoots.Max(item => item.Bounds.Right)
            : selectedRoots.Max(item => item.Bounds.Bottom);
        var totalSize = distributeHorizontally
            ? selectedRoots.Sum(item => item.Bounds.Width)
            : selectedRoots.Sum(item => item.Bounds.Height);
        var gap = (end - start - totalSize) / (selectedRoots.Count - 1);
        var cursor = start;

        foreach (var item in selectedRoots)
        {
            var control = item.Control;
            var currentAbsolute = GetAbsolutePosition(control);
            var targetAbsoluteX = distributeHorizontally ? cursor : currentAbsolute.X;
            var targetAbsoluteY = distributeHorizontally ? currentAbsolute.Y : cursor;
            var local = ToLocalPosition(control.ParentId, targetAbsoluteX, targetAbsoluteY);

            control.X = Snap(local.X);
            control.Y = Snap(local.Y);
            ClampControlToSurface(control);

            cursor += (distributeHorizontally ? item.Bounds.Width : item.Bounds.Height) + gap;
        }

        NotifyDesignerStateChanged();
        StatusText = distributeHorizontally
            ? "Выделенное распределено по горизонтали"
            : "Выделенное распределено по вертикали";
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    private RectInfo GetAbsoluteBounds(DesignControlModel control)
    {
        var position = GetAbsolutePosition(control);
        return new RectInfo(position.X, position.Y, Math.Max(0, control.Width), Math.Max(0, control.Height));
    }

    private void MatchSelectionSizeCore(bool matchWidth, bool matchHeight)
    {
        var selectedRoots = GetVisibleEditableSelectedRootControls().ToList();
        var anchor = GetSelectionAnchorControl(selectedRoots);
        if (anchor is null || selectedRoots.Count < 2)
            return;

        BeginUndoBatch();
        try
        {
        foreach (var control in selectedRoots.Where(control => control.Id != anchor.Id))
        {
            if (matchWidth)
                control.Width = anchor.Width;

            if (matchHeight)
                control.Height = anchor.Height;

            ClampControlToSurface(control);
        }

        NotifyDesignerStateChanged();
        StatusText = (matchWidth, matchHeight) switch
        {
            (true, true) => "Размер выделения выровнен полностью",
            (true, false) => "Ширина выделения выровнена",
            (false, true) => "Высота выделения выровнена",
            _ => "Размер выделения обновлен"
        };
        }
        finally
        {
            CommitUndoBatch();
        }
    }

    private DesignControlModel? GetSelectionAnchorControl(IReadOnlyList<DesignControlModel> selectedRoots)
    {
        if (selectedRoots.Count == 0)
            return null;

        if (SelectedControl is not null && selectedRoots.Any(control => control.Id == SelectedControl.Id))
            return SelectedControl;

        return selectedRoots.LastOrDefault();
    }

    private void Controls_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Подписываемся на каждый контрол отдельно, чтобы любое изменение его свойств
        // сразу отражалось в XAML, предпросмотре и истории документа.
        if (e.OldItems is not null)
        {
            foreach (DesignControlModel control in e.OldItems)
                control.PropertyChanged -= Control_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (DesignControlModel control in e.NewItems)
                control.PropertyChanged += Control_PropertyChanged;
        }

        if (!_isHistorySuspended)
        {
            var validIds = Controls.Select(control => control.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filteredSelection = SelectedControlIds.Where(validIds.Contains).Select(GetControl).Where(control => control is not null).Cast<DesignControlModel>().ToList();
            if (filteredSelection.Count != SelectedControlIds.Count)
                SetSelection(filteredSelection, filteredSelection.LastOrDefault());
        }

        OnPropertyChanged(nameof(HasControls));
        OnPropertyChanged(nameof(SelectedControlSummary));
        RaiseInteractionDesignerProperties();
        RaiseInteractionLookupProperties();
        if (!_isStructureTreeRefreshSuspended)
            RebuildStructureTree();
        NotifyDesignerStateChanged();
    }

    private void Control_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedControl)
        {
            if (_isPropertyGridLiveGesture && IsLiveLayoutProperty(e.PropertyName))
            {
                RequestPropertyGridLiveGestureRefresh();
            }
            else
            {
                RefreshDescriptorCustomPropertyEditors();
                RebuildPropertyGrid();
                OnPropertyChanged(nameof(SelectedControlSummary));
                OnPropertyChanged(nameof(SelectedLockStateSummary));
                OnPropertyChanged(nameof(CanEditText));
                OnPropertyChanged(nameof(SelectedTextLabel));
                OnPropertyChanged(nameof(CanEditPlaceholder));
                OnPropertyChanged(nameof(CanEditImageSource));
                OnPropertyChanged(nameof(CanEditStretch));
                OnPropertyChanged(nameof(CanEditBackground));
                OnPropertyChanged(nameof(CanEditCommonBackground));
                OnPropertyChanged(nameof(CanEditForeground));
                OnPropertyChanged(nameof(CanEditCommonForeground));
                OnPropertyChanged(nameof(CanEditBorder));
                OnPropertyChanged(nameof(CanEditCommonBorder));
                OnPropertyChanged(nameof(CanEditCommonFont));
                OnPropertyChanged(nameof(CanEditClassicBindingEditor));
                OnPropertyChanged(nameof(CanEditDataGridBasic));
                OnPropertyChanged(nameof(CanEditDataGridRowBackground));
                OnPropertyChanged(nameof(CanEditDataGridAlternateRowBackground));
                OnPropertyChanged(nameof(CanEditDataGridAdvancedVisuals));
                OnPropertyChanged(nameof(CanEditDataGridTextAlignment));
                OnPropertyChanged(nameof(CanEditDataGridGlowColor));
                OnPropertyChanged(nameof(CanEditCornerRadius));
                OnPropertyChanged(nameof(CanEditFont));
                OnPropertyChanged(nameof(SelectedBackgroundLabel));
                OnPropertyChanged(nameof(CanEditPadding));
                OnPropertyChanged(nameof(CanEditGridLayout));
                OnPropertyChanged(nameof(CanEditDataBinding));
                OnPropertyChanged(nameof(CanEditFieldBinding));
                OnPropertyChanged(nameof(CanEditSelectedLayoutFlow));
                OnPropertyChanged(nameof(CanEditSelectedLayoutGrid));
                OnPropertyChanged(nameof(CanEditSelectedAbsolutePosition));
                OnPropertyChanged(nameof(CanEditSelectedAnchors));
                OnPropertyChanged(nameof(IsSelectedControlManagedByLayout));
                OnPropertyChanged(nameof(SelectedControlLayoutHint));
                OnPropertyChanged(nameof(CanSelectedControlHostChildren));
                OnPropertyChanged(nameof(HasDescriptorCustomProperties));
                RaiseBindingEditorProperties();
                RaiseInteractionDesignerProperties();
            }
        }

        if (e.PropertyName is nameof(DesignControlModel.Name) or nameof(DesignControlModel.Type))
            RaiseInteractionLookupProperties();

        if (sender is DesignControlModel changedControl
            && IsControlSelected(changedControl)
            && e.PropertyName == nameof(DesignControlModel.ParentId))
        {
            OnPropertyChanged(nameof(CanGroupSelection));
            OnPropertyChanged(nameof(CanUngroupSelection));
        }

        if (sender is DesignControlModel selectedChangedControl
            && IsControlSelected(selectedChangedControl)
            && e.PropertyName == nameof(DesignControlModel.IsLocked))
        {
            RaiseSelectionProperties();
        }

        if (sender is DesignControlModel structureChangedControl
            && IsStructureTreeProperty(e.PropertyName))
        {
            if (RequiresStructureTreeRebuild(e.PropertyName))
                RebuildStructureTree();
            else
                RefreshStructureNode(structureChangedControl);
        }

        NotifyDesignerStateChanged();
    }

    private void BindingSources_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BindingSourceModel source in e.OldItems)
                DetachBindingSource(source);
        }

        if (e.NewItems is not null)
        {
            foreach (BindingSourceModel source in e.NewItems)
                AttachBindingSource(source);
        }

        RebuildImportedDllCatalog();
        OnPropertyChanged(nameof(HasBindingSources));
        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        RaiseInteractionLookupProperties();
        RebuildPropertyGrid();
        NotifyDesignerStateChanged();
    }

    private void AttachBindingSource(BindingSourceModel source)
    {
        source.PropertyChanged += BindingSource_PropertyChanged;
        source.Fields.CollectionChanged += BindingSourceFields_CollectionChanged;
        foreach (var field in source.Fields)
            field.PropertyChanged += BindingField_PropertyChanged;
    }

    private void DetachBindingSource(BindingSourceModel source)
    {
        source.PropertyChanged -= BindingSource_PropertyChanged;
        source.Fields.CollectionChanged -= BindingSourceFields_CollectionChanged;
        foreach (var field in source.Fields)
            field.PropertyChanged -= BindingField_PropertyChanged;
    }

    private void BindingSource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RebuildImportedDllCatalog();
        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        RaiseInteractionLookupProperties();
        RebuildPropertyGrid();
        OnPropertyChanged(nameof(HasSelectedBindingSourceImportMetadata));
        OnPropertyChanged(nameof(SelectedBindingSourceImportSummary));
        NotifyDesignerStateChanged();
    }

    private void BindingSourceFields_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BindingFieldModel field in e.OldItems)
                field.PropertyChanged -= BindingField_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (BindingFieldModel field in e.NewItems)
                field.PropertyChanged += BindingField_PropertyChanged;
        }

        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        RaiseInteractionLookupProperties();
        RebuildPropertyGrid();
        NotifyDesignerStateChanged();
    }

    private void BindingField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        if (e.PropertyName is nameof(BindingFieldModel.Path))
            RaiseInteractionLookupProperties();
        RebuildPropertyGrid();
        NotifyDesignerStateChanged();
    }

    private void Interactions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (InteractionModel interaction in e.OldItems)
                interaction.PropertyChanged -= Interaction_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (InteractionModel interaction in e.NewItems)
                interaction.PropertyChanged += Interaction_PropertyChanged;
        }

        RaiseInteractionDesignerProperties();
        RebuildPropertyGrid();
        NotifyDesignerStateChanged();
    }

    private void Interaction_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseInteractionDesignerProperties();
        RebuildPropertyGrid();
        NotifyDesignerStateChanged();
    }

    partial void OnSelectedInteractionChanged(InteractionModel? value)
    {
        RaiseInteractionDesignerProperties();
    }

    partial void OnSelectedControlChanged(DesignControlModel? value)
    {
        if (!_isUpdatingSelectionState)
        {
            _isUpdatingSelectionState = true;
            SelectedControlIds.Clear();
            if (value is not null)
                SelectedControlIds.Add(value.Id);
            _isUpdatingSelectionState = false;
        }

        RebuildDescriptorCustomPropertyEditors();
        RebuildPropertyGrid();
        OnPropertyChanged(nameof(HasSelectedControl));
        OnPropertyChanged(nameof(CanEditText));
        OnPropertyChanged(nameof(SelectedTextLabel));
        OnPropertyChanged(nameof(CanEditPlaceholder));
        OnPropertyChanged(nameof(CanEditImageSource));
        OnPropertyChanged(nameof(CanEditStretch));
        OnPropertyChanged(nameof(CanEditBackground));
        OnPropertyChanged(nameof(CanEditCommonBackground));
        OnPropertyChanged(nameof(CanEditForeground));
        OnPropertyChanged(nameof(CanEditCommonForeground));
        OnPropertyChanged(nameof(CanEditBorder));
        OnPropertyChanged(nameof(CanEditCommonBorder));
        OnPropertyChanged(nameof(CanEditCommonFont));
        OnPropertyChanged(nameof(CanEditClassicBindingEditor));
        OnPropertyChanged(nameof(CanEditDataGridBasic));
        OnPropertyChanged(nameof(CanEditDataGridRowBackground));
        OnPropertyChanged(nameof(CanEditDataGridAlternateRowBackground));
        OnPropertyChanged(nameof(CanEditDataGridAdvancedVisuals));
        OnPropertyChanged(nameof(CanEditDataGridTextAlignment));
        OnPropertyChanged(nameof(CanEditDataGridGlowColor));
        OnPropertyChanged(nameof(CanEditCornerRadius));
        OnPropertyChanged(nameof(CanEditFont));
        OnPropertyChanged(nameof(SelectedBackgroundLabel));
        OnPropertyChanged(nameof(CanEditPadding));
        OnPropertyChanged(nameof(CanEditGridLayout));
        OnPropertyChanged(nameof(CanEditDataBinding));
        OnPropertyChanged(nameof(CanEditFieldBinding));
        OnPropertyChanged(nameof(CanEditSelectedLayoutFlow));
        OnPropertyChanged(nameof(CanEditSelectedLayoutGrid));
        OnPropertyChanged(nameof(CanEditSelectedAbsolutePosition));
        OnPropertyChanged(nameof(CanEditSelectedAnchors));
        OnPropertyChanged(nameof(IsSelectedControlManagedByLayout));
        OnPropertyChanged(nameof(SelectedControlLayoutHint));
        OnPropertyChanged(nameof(CanApplyButtonVisualPresets));
        OnPropertyChanged(nameof(CanApplyDataGridVisualPresets));
        OnPropertyChanged(nameof(CanDuplicateSelected));
        OnPropertyChanged(nameof(CanLockSelected));
        OnPropertyChanged(nameof(CanUnlockSelected));
        OnPropertyChanged(nameof(CanSelectedControlHostChildren));
        OnPropertyChanged(nameof(HasDescriptorCustomProperties));
        OnPropertyChanged(nameof(SelectedLockStateSummary));
        OnPropertyChanged(nameof(SelectedControlSummary));
        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
        RaiseSelectionProperties();
        NotifyDesignerStateChanged(trackHistory: false);
    }

    partial void OnSelectedBindingSourceChanged(BindingSourceModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedBindingSource));
        OnPropertyChanged(nameof(HasNoSelectedBindingSource));
        OnPropertyChanged(nameof(HasSelectedBindingSourceImportMetadata));
        OnPropertyChanged(nameof(SelectedBindingSourceImportSummary));
        RaiseBindingEditorProperties();
        RaiseInteractionDesignerProperties();
    }

    private void ApplyThemePalette(string themeName)
    {
        if (_isApplyingDocument || _isApplyingThemePalette)
            return;

        var normalizedTheme = DesignerThemeCatalog.NormalizeThemeName(themeName);
        var previousPalette = DesignerThemeCatalog.Get(_activeFormTheme);
        var nextPalette = DesignerThemeCatalog.Get(normalizedTheme);

        _isApplyingThemePalette = true;
        var previousHistoryState = _isHistorySuspended;
        _isHistorySuspended = true;

        try
        {
            SurfaceBackground = nextPalette.SurfaceBackground;
            SurfaceGridMinorColor = nextPalette.SurfaceGridMinorColor;
            SurfaceGridMajorColor = nextPalette.SurfaceGridMajorColor;

            foreach (var control in Controls)
                ApplyThemeToControl(control, previousPalette, nextPalette);

            _activeFormTheme = normalizedTheme;
        }
        finally
        {
            _isHistorySuspended = previousHistoryState;
            _isApplyingThemePalette = false;
        }

        RaisePreviewProperties();
        NotifyDesignerStateChanged();
    }

    private static void ApplyThemeToControl(DesignControlModel control, FormThemePalette previousPalette, FormThemePalette nextPalette)
    {
        var previousDefaults = DesignerThemeCatalog.GetControlDefaults(control.Type, previousPalette);
        var nextDefaults = DesignerThemeCatalog.GetControlDefaults(control.Type, nextPalette);

        if (ShouldReplaceThemeBoundValue(control.Background, previousDefaults.Background, nextDefaults.Background))
            control.Background = nextDefaults.Background!;

        if (ShouldReplaceThemeBoundValue(control.Foreground, previousDefaults.Foreground, nextDefaults.Foreground))
            control.Foreground = nextDefaults.Foreground!;

        if (ShouldReplaceThemeBoundValue(control.BorderBrush, previousDefaults.BorderBrush, nextDefaults.BorderBrush))
            control.BorderBrush = nextDefaults.BorderBrush!;

        if (control.Type == DesignerControlTypes.DataGrid
            && ShouldReplaceThemeBoundValue(control.DataGridGlowColor, previousPalette.AccentStrongBrush, nextPalette.AccentStrongBrush))
        {
            control.DataGridGlowColor = nextPalette.AccentStrongBrush;
        }

        if (control.Type == DesignerControlTypes.DataGrid
            && ShouldReplaceThemeBoundValue(control.DataGridOuterBorderBrush, previousPalette.AccentStrongBrush, nextPalette.AccentStrongBrush))
        {
            control.DataGridOuterBorderBrush = nextPalette.AccentStrongBrush;
        }

        if (control.Type == DesignerControlTypes.DataGrid
            && ShouldReplaceThemeBoundValue(control.DataGridHeaderBackground, previousPalette.DataGridHeaderBackground, nextPalette.DataGridHeaderBackground))
        {
            control.DataGridHeaderBackground = nextPalette.DataGridHeaderBackground;
        }

        if (control.Type == DesignerControlTypes.DataGrid
            && ShouldReplaceThemeBoundValue(control.DataGridHeaderForeground, previousPalette.DataGridHeaderForeground, nextPalette.DataGridHeaderForeground))
        {
            control.DataGridHeaderForeground = nextPalette.DataGridHeaderForeground;
        }

        if (control.Type == DesignerControlTypes.DataGrid
            && ShouldReplaceThemeBoundValue(control.DataGridRowBackground, previousPalette.DataGridRowBackground, nextPalette.DataGridRowBackground))
        {
            control.DataGridRowBackground = nextPalette.DataGridRowBackground;
        }

        if (control.Type == DesignerControlTypes.DataGrid
            && ShouldReplaceThemeBoundValue(control.DataGridAlternateRowBackground, previousPalette.DataGridAlternateRowBackground, nextPalette.DataGridAlternateRowBackground))
        {
            control.DataGridAlternateRowBackground = nextPalette.DataGridAlternateRowBackground;
        }
    }

    private static bool ShouldReplaceThemeBoundValue(string currentValue, string? previousValue, string? nextValue)
    {
        return !string.IsNullOrWhiteSpace(previousValue)
            && !string.IsNullOrWhiteSpace(nextValue)
            && DesignerThemeCatalog.AreEquivalent(currentValue, previousValue);
    }

    partial void OnDesignWidthChanged(double value)
    {
        if (value < 300)
        {
            DesignWidth = 300;
            return;
        }

        ClampAllControlsToSurface();
        RaisePreviewProperties();
        NotifyDesignerStateChanged();
    }

    partial void OnDesignHeightChanged(double value)
    {
        if (value < 200)
        {
            DesignHeight = 200;
            return;
        }

        ClampAllControlsToSurface();
        RaisePreviewProperties();
        NotifyDesignerStateChanged();
    }

    partial void OnSnapStepChanged(int value)
    {
        if (value < 1)
        {
            SnapStep = 1;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnIsGridSnapEnabledChanged(bool value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnIsControlSnapEnabledChanged(bool value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnSnapThresholdChanged(int value)
    {
        if (value < 1)
        {
            SnapThreshold = 1;
            return;
        }

        if (value > 40)
        {
            SnapThreshold = 40;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceBackgroundChanged(string value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceGridMinorColorChanged(string value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceGridMajorColorChanged(string value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceLayoutModeChanged(string value)
    {
        var normalized = DesignerLayoutModes.NormalizeMode(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SurfaceLayoutMode = normalized;
            return;
        }

        ClampAllControlsToSurface();
        OnPropertyChanged(nameof(CanEditSurfaceFlowLayout));
        OnPropertyChanged(nameof(CanEditSurfaceGridLayout));
        OnPropertyChanged(nameof(CanEditSelectedAbsolutePosition));
        OnPropertyChanged(nameof(CanEditSelectedAnchors));
        OnPropertyChanged(nameof(IsSelectedControlManagedByLayout));
        RaisePreviewProperties();
        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceLayoutOrientationChanged(string value)
    {
        var normalized = DesignerLayoutModes.NormalizeOrientation(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SurfaceLayoutOrientation = normalized;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceLayoutSpacingChanged(double value)
    {
        if (value < 0)
        {
            SurfaceLayoutSpacing = 0;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceLayoutColumnsChanged(int value)
    {
        if (value < 1)
        {
            SurfaceLayoutColumns = 1;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnSurfaceLayoutRowsChanged(int value)
    {
        if (value < 1)
        {
            SurfaceLayoutRows = 1;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnCurrentDocumentPathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentDocumentDisplayName));
        RefreshDiagnostics();
    }

    partial void OnIsDiagnosticsPaneExpandedChanged(bool value)
    {
        RaiseDiagnosticsProperties();
        RaiseEditorShellLayoutProperties();
    }

    partial void OnDiagnosticsPaneHeightChanged(double value)
    {
        var clamped = Math.Clamp(value, 140, 520);
        if (Math.Abs(value - clamped) > 0.01)
        {
            DiagnosticsPaneHeight = clamped;
            return;
        }

        OnPropertyChanged(nameof(DiagnosticsPaneHostHeight));
        OnPropertyChanged(nameof(BottomDockPanelHeight));
        OnPropertyChanged(nameof(EditorShellLayoutSummary));
    }

    partial void OnIsLeftDockOpenChanged(bool value)
    {
        RaiseEditorShellLayoutProperties();
    }

    partial void OnIsRightDockOpenChanged(bool value)
    {
        RaiseEditorShellLayoutProperties();
    }

    partial void OnIsBottomDockOpenChanged(bool value)
    {
        RaiseEditorShellLayoutProperties();
    }

    partial void OnLeftDockPanelWidthChanged(double value)
    {
        var clamped = Math.Clamp(value, 220, 420);
        if (Math.Abs(value - clamped) > 0.01)
        {
            LeftDockPanelWidth = clamped;
            return;
        }

        OnPropertyChanged(nameof(EditorShellLayoutSummary));
    }

    partial void OnRightDockPanelWidthChanged(double value)
    {
        var clamped = Math.Clamp(value, 280, 560);
        if (Math.Abs(value - clamped) > 0.01)
        {
            RightDockPanelWidth = clamped;
            return;
        }

        OnPropertyChanged(nameof(EditorShellLayoutSummary));
    }

    partial void OnSelectedProblemsFilterChanged(string value)
    {
        if (!AvailableProblemsFilters.Contains(value))
        {
            SelectedProblemsFilter = ProblemsFilterAll;
            return;
        }

        RaiseProblemsFilterProperties();
    }

    partial void OnStructureSearchTextChanged(string value)
    {
        RebuildStructureTree();
        RaiseStructureTreeProperties();
    }

    partial void OnFormThemeChanged(string value)
    {
        var normalized = DesignerThemeCatalog.NormalizeThemeName(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            FormTheme = normalized;
            return;
        }

        OnPropertyChanged(nameof(FormThemeDescription));
        ApplyThemePalette(normalized);
    }

    partial void OnFormTitleChanged(string value)
    {
        RebuildStructureTree();
        NotifyDesignerStateChanged();
    }

    partial void OnFormWindowStateChanged(string value)
    {
        var normalized = NormalizeFormWindowState(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            FormWindowState = normalized;
            return;
        }

        ClampAllControlsToSurface();
        RaisePreviewProperties();
        NotifyDesignerStateChanged();
    }

    partial void OnFormStartupLocationChanged(string value)
    {
        var normalized = NormalizeFormStartupLocation(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            FormStartupLocation = normalized;
            return;
        }

        NotifyDesignerStateChanged();
    }

    partial void OnFormCanResizeChanged(bool value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnFormShowInTaskbarChanged(bool value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnFormTopmostChanged(bool value)
    {
        NotifyDesignerStateChanged();
    }

    partial void OnFormHasSystemDecorationsChanged(bool value)
    {
        OnPropertyChanged(nameof(FormWindowDecorationsSummary));
        NotifyDesignerStateChanged();
    }

    partial void OnIsImmersiveDesignerModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDesignerShellHeaderVisible));
        OnPropertyChanged(nameof(IsDesignerSidePanelsVisible));
        RaiseWorkspaceModeProperties();
        OnPropertyChanged(nameof(ImmersiveModeButtonText));
    }

    partial void OnIsUserPreviewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDesignerShellHeaderVisible));
        OnPropertyChanged(nameof(IsDesignerSidePanelsVisible));
        OnPropertyChanged(nameof(IsDesignerSurfaceToolbarVisible));
        RaiseWorkspaceModeProperties();
        OnPropertyChanged(nameof(UserPreviewModeButtonText));
        OnPropertyChanged(nameof(CanResizeDesignSurface));
        NotifyDesignerStateChanged(trackHistory: false);
    }

    partial void OnWorkspaceModeChanged(string value)
    {
        if (string.Equals(value, WorkspaceModeDiagnostics, StringComparison.Ordinal))
        {
            IsBottomDockOpen = true;
            IsDiagnosticsPaneExpanded = true;
            WorkspaceMode = WorkspaceModeDesign;
            return;
        }

        if (!AvailableWorkspaceModes.Contains(value))
        {
            WorkspaceMode = WorkspaceModeDesign;
            return;
        }

        RaiseWorkspaceModeProperties();
    }

    partial void OnGenerationModeChanged(string value)
    {
        if (!AvailableGenerationModes.Contains(value))
        {
            GenerationMode = GenerationModeCleanUi;
            return;
        }

        if (string.Equals(value, GenerationModeDemoData, StringComparison.Ordinal))
        {
            IncludeSampleData = true;
            IncludeCrudSkeleton = true;
            IncludeCommunityToolkitAttributes = true;
        }

        RaiseGenerationOptionsProperties();
        RebuildPropertyGrid();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    partial void OnDataGridExportModeChanged(string value)
    {
        var normalized = NormalizeDataGridExportMode(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            DataGridExportMode = normalized;
            return;
        }

        RaiseGenerationOptionsProperties();
        RebuildPropertyGrid();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    partial void OnExportTargetChanged(string value)
    {
        if (!AvailableExportTargets.Contains(value))
        {
            ExportTarget = ExportTargetMainWindow;
            return;
        }

        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
    }

    partial void OnExportProjectNamespaceChanged(string value)
    {
        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
    }

    partial void OnXamlVerbosityChanged(string value)
    {
        if (!AvailableXamlVerbosities.Contains(value))
        {
            XamlVerbosity = XamlVerbosityCompact;
            return;
        }

        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
    }

    partial void OnLayoutExportModeChanged(string value)
    {
        var normalized = NormalizeLayoutExportMode(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            LayoutExportMode = normalized;
            return;
        }

        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    partial void OnIncludeExportCommentsChanged(bool value)
    {
        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
    }

    partial void OnIncludeSampleDataChanged(bool value)
    {
        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    partial void OnIncludeCrudSkeletonChanged(bool value)
    {
        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    partial void OnIncludeCommunityToolkitAttributesChanged(bool value)
    {
        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    partial void OnIncludePluginRuntimeReferencesChanged(bool value)
    {
        RaiseGenerationOptionsProperties();
        MarkExportCacheStale();
        RefreshDiagnostics();
    }

    private void RaiseGenerationOptionsProperties()
    {
        OnPropertyChanged(nameof(IsCleanUiGenerationMode));
        OnPropertyChanged(nameof(IsDemoDataGenerationMode));
        OnPropertyChanged(nameof(ShouldGenerateDemoRuntimeCode));
        OnPropertyChanged(nameof(ShouldExportPlaceholderDataGrid));
        OnPropertyChanged(nameof(ShouldExportVisualDataGrid));
        OnPropertyChanged(nameof(ShouldExportRealDataGrid));
        OnPropertyChanged(nameof(ShouldExportPortableDataGrid));
        OnPropertyChanged(nameof(DataGridExportModeHint));
        OnPropertyChanged(nameof(IsMainWindowExportTarget));
        OnPropertyChanged(nameof(IsGeneratedWindowExportTarget));
        OnPropertyChanged(nameof(IsCompactXamlExport));
        OnPropertyChanged(nameof(IsFullStyledXamlExport));
        OnPropertyChanged(nameof(ShouldIncludeExportComments));
        OnPropertyChanged(nameof(IsResponsiveLayoutExportMode));
        OnPropertyChanged(nameof(LayoutExportModeHint));
        OnPropertyChanged(nameof(ExportLayoutBadgeText));
        OnPropertyChanged(nameof(GenerationOptionsSummary));
        RaiseExportChecklistProperties();
    }

    private void RaiseExportChecklistProperties()
    {
        OnPropertyChanged(nameof(ExportChecklistItems));
        OnPropertyChanged(nameof(ExportChecklistErrorCount));
        OnPropertyChanged(nameof(ExportChecklistWarningCount));
        OnPropertyChanged(nameof(HasExportChecklistIssues));
        OnPropertyChanged(nameof(HasExportWarnings));
        OnPropertyChanged(nameof(ExportStatusText));
        OnPropertyChanged(nameof(ExportStatusBadgeBackground));
        OnPropertyChanged(nameof(ExportStatusBadgeBorder));
        OnPropertyChanged(nameof(ExportStatusBadgeForeground));
        OnPropertyChanged(nameof(ExportDataGridBadgeText));
        OnPropertyChanged(nameof(ExportLayoutBadgeText));
        OnPropertyChanged(nameof(ExportViewModelBadgeText));
        OnPropertyChanged(nameof(ExportInteractionsBadgeText));
        OnPropertyChanged(nameof(ExportCompactSummary));
        OnPropertyChanged(nameof(ExportSummaryText));
        OnPropertyChanged(nameof(ExportDependenciesSummary));
    }

    private void RaiseWorkspaceModeProperties()
    {
        OnPropertyChanged(nameof(IsDesignMode));
        OnPropertyChanged(nameof(IsDataMode));
        OnPropertyChanged(nameof(IsCodeMode));
        OnPropertyChanged(nameof(IsPluginsMode));
        OnPropertyChanged(nameof(IsLogicMode));
        OnPropertyChanged(nameof(IsDiagnosticsMode));
        OnPropertyChanged(nameof(IsHistoryMode));
        OnPropertyChanged(nameof(CanShowLeftDock));
        OnPropertyChanged(nameof(CanShowRightDock));
        OnPropertyChanged(nameof(IsLeftDockPanelVisible));
        OnPropertyChanged(nameof(IsRightDockPanelVisible));
        OnPropertyChanged(nameof(IsLeftDockRailVisible));
        OnPropertyChanged(nameof(IsRightDockRailVisible));
        OnPropertyChanged(nameof(IsBottomDockPanelVisible));
        OnPropertyChanged(nameof(IsBottomDockRailVisible));
        OnPropertyChanged(nameof(IsLeftRailVisible));
        OnPropertyChanged(nameof(IsRightInspectorVisible));
        OnPropertyChanged(nameof(IsDesignModePanelVisible));
        OnPropertyChanged(nameof(IsDataModePanelVisible));
        OnPropertyChanged(nameof(IsCodeModePanelVisible));
        OnPropertyChanged(nameof(IsPluginsModePanelVisible));
        OnPropertyChanged(nameof(IsLogicModePanelVisible));
        OnPropertyChanged(nameof(IsContextualToolbarVisible));
        OnPropertyChanged(nameof(IsCompactDiagnosticsBarVisible));
        OnPropertyChanged(nameof(LeftRailSelectedIndex));
        OnPropertyChanged(nameof(RightInspectorSelectedIndex));
        OnPropertyChanged(nameof(WorkspaceModeDescription));
        OnPropertyChanged(nameof(LeftDockToggleText));
        OnPropertyChanged(nameof(RightDockToggleText));
        OnPropertyChanged(nameof(LeftDockHeaderTitle));
        OnPropertyChanged(nameof(RightDockHeaderTitle));
    }

    private void RaiseEditorShellLayoutProperties()
    {
        OnPropertyChanged(nameof(IsLeftDockPanelVisible));
        OnPropertyChanged(nameof(IsRightDockPanelVisible));
        OnPropertyChanged(nameof(IsLeftDockRailVisible));
        OnPropertyChanged(nameof(IsRightDockRailVisible));
        OnPropertyChanged(nameof(IsBottomDockPanelVisible));
        OnPropertyChanged(nameof(IsBottomDockRailVisible));
        OnPropertyChanged(nameof(IsLeftRailVisible));
        OnPropertyChanged(nameof(IsRightInspectorVisible));
        OnPropertyChanged(nameof(IsCompactDiagnosticsBarVisible));
        OnPropertyChanged(nameof(LeftDockToggleText));
        OnPropertyChanged(nameof(RightDockToggleText));
        OnPropertyChanged(nameof(EditorShellLayoutSummary));
    }

    private sealed class CrudGenerationContext
    {
        public required BindingSourceModel Source { get; init; }
        public required string ItemTypeName { get; init; }
        public required string CollectionPropertyName { get; init; }
        public required string ViewCollectionPropertyName { get; init; }
        public required string SearchTextPropertyName { get; init; }
        public required string SelectedItemPropertyName { get; init; }
        public required string CurrentItemPropertyName { get; init; }
        public required IReadOnlyList<BindingFieldModel> Fields { get; init; }
        public required IReadOnlyList<BindingFieldModel> SearchFields { get; init; }
    }

    private sealed record ExportableInteraction(
        InteractionModel Interaction,
        DesignControlModel Source,
        DesignControlModel? Target,
        string EventName);

    private sealed record LayoutExportPlan(
        string RequestedMode,
        string EffectiveRootLayoutMode,
        bool UsesResponsiveStack,
        bool FallbackToCanvas,
        double StackSpacing,
        string RootMargin,
        string ShortText,
        string BadgeText,
        string Value,
        string Details,
        ExportChecklistSeverity Severity);

    private sealed class BindingSourceDiscoveryResult
    {
        public List<BindingSourceModel> Sources { get; } = new();
        public List<BindingImportDiagnostics> Diagnostics { get; } = new();
        public List<string> ProviderErrors { get; } = new();
        public bool HasHandledProvider { get; set; }
    }

    private enum GeneratedButtonAction
    {
        None,
        Add,
        Save,
        Delete,
        Edit,
        Search,
        Clear
    }

    private void RaisePreviewProperties()
    {
        OnPropertyChanged(nameof(IsFormSizeManagedByMonitor));
        OnPropertyChanged(nameof(IsFormSizeEditable));
        OnPropertyChanged(nameof(CanResizeDesignSurface));
        OnPropertyChanged(nameof(PreviewFormWidth));
        OnPropertyChanged(nameof(PreviewFormHeight));
        OnPropertyChanged(nameof(PreviewSurfaceSummary));
        OnPropertyChanged(nameof(PreviewSurfaceModeSummary));
    }

    private static string NormalizeFormWindowState(string? value)
    {
        return value?.Trim() switch
        {
            WindowStateMaximized or "Заполнить рабочую область" or "Развернутое" or "Maximized" => WindowStateMaximized,
            WindowStateFullScreen or "FullScreen" => WindowStateFullScreen,
            _ => WindowStateNormal
        };
    }

    private static string NormalizeDataGridExportMode(string? value)
    {
        return value?.Trim() switch
        {
            DataGridExportModePlaceholder => DataGridExportModePlaceholder,
            DataGridExportModeReal => DataGridExportModeReal,
            DataGridExportModeVisual or LegacyDataGridExportModePortable => DataGridExportModeVisual,
            _ => DataGridExportModeVisual
        };
    }

    private static string NormalizeLayoutExportMode(string? value)
    {
        return value?.Trim() switch
        {
            LayoutExportModeResponsive => LayoutExportModeResponsive,
            _ => LayoutExportModeCanvas
        };
    }

    private static string NormalizeFormStartupLocation(string? value)
    {
        return value?.Trim() switch
        {
            StartupLocationManual or "Manual" => StartupLocationManual,
            StartupLocationCenterOwner or "CenterOwner" => StartupLocationCenterOwner,
            _ => StartupLocationCenterScreen
        };
    }

    private static string ToAvaloniaWindowState(string value)
    {
        return NormalizeFormWindowState(value) switch
        {
            WindowStateMaximized => "Maximized",
            WindowStateFullScreen => "FullScreen",
            _ => "Normal"
        };
    }

    private static string ToAvaloniaStartupLocation(string value)
    {
        return NormalizeFormStartupLocation(value) switch
        {
            StartupLocationManual => "Manual",
            StartupLocationCenterOwner => "CenterOwner",
            _ => "CenterScreen"
        };
    }

    private sealed class DesignerAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public DesignerAssemblyLoadContext(string assemblyPath)
            : base($"designer-import:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolvedPath is not null)
                return LoadFromAssemblyPath(resolvedPath);

            var defaultAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(loaded => AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), assemblyName));
            if (defaultAssembly is not null)
                return defaultAssembly;

            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                return null;
            }
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return resolvedPath is null ? 0 : LoadUnmanagedDllFromPath(resolvedPath);
        }
    }

    private enum SelectionAlignment
    {
        Left,
        Top,
        Right,
        Bottom,
        Center,
        Middle
    }

    private readonly record struct RectInfo(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
    }

    private sealed class ControlStyleSnapshot
    {
        private static readonly string[] StylePropertyNames =
        {
            nameof(DesignControlModel.Background),
            nameof(DesignControlModel.Foreground),
            nameof(DesignControlModel.BorderBrush),
            nameof(DesignControlModel.BorderThickness),
            nameof(DesignControlModel.CornerRadius),
            nameof(DesignControlModel.FontFamily),
            nameof(DesignControlModel.FontSize),
            nameof(DesignControlModel.FontWeight),
            nameof(DesignControlModel.Opacity),
            nameof(DesignControlModel.Padding),
            nameof(DesignControlModel.Stretch),
            nameof(DesignControlModel.DataGridRowBackground),
            nameof(DesignControlModel.DataGridAlternateRowBackground),
            nameof(DesignControlModel.DataGridTextAlignment),
            nameof(DesignControlModel.DataGridGlowColor),
            nameof(DesignControlModel.DataGridHeaderBackground),
            nameof(DesignControlModel.DataGridHeaderForeground),
            nameof(DesignControlModel.DataGridRowForeground),
            nameof(DesignControlModel.DataGridHoverRowBackground),
            nameof(DesignControlModel.DataGridSelectedRowBackground),
            nameof(DesignControlModel.DataGridSelectedRowForeground),
            nameof(DesignControlModel.DataGridGridLineBrush),
            nameof(DesignControlModel.DataGridOuterBorderBrush),
            nameof(DesignControlModel.DataGridHeaderFontSize),
            nameof(DesignControlModel.DataGridHeaderFontWeight),
            nameof(DesignControlModel.DataGridRowFontSize),
            nameof(DesignControlModel.DataGridRowFontWeight),
            nameof(DesignControlModel.DataGridHeaderHeight),
            nameof(DesignControlModel.DataGridRowHeight),
            nameof(DesignControlModel.DataGridCellPadding),
            nameof(DesignControlModel.DataGridShowHeader),
            nameof(DesignControlModel.DataGridShowRowLines),
            nameof(DesignControlModel.DataGridShowColumnLines),
            nameof(DesignControlModel.DataGridShowAlternatingRows)
        };

        private readonly Dictionary<string, object?> _values;
        private readonly List<DesignPropertyValueModel> _customProperties;

        private ControlStyleSnapshot(string sourceType, Dictionary<string, object?> values, List<DesignPropertyValueModel> customProperties)
        {
            SourceType = sourceType;
            _values = values;
            _customProperties = customProperties;
        }

        public string SourceType { get; }

        public static ControlStyleSnapshot FromControl(DesignControlModel control)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var controlType = typeof(DesignControlModel);

            foreach (var propertyName in StylePropertyNames)
            {
                var property = controlType.GetProperty(propertyName);
                if (property is null || !property.CanRead)
                    continue;

                values[propertyName] = property.GetValue(control);
            }

            return new ControlStyleSnapshot(
                control.Type,
                values,
                control.CustomProperties.Select(property => property.Clone()).ToList());
        }

        public void ApplyTo(DesignControlModel target, MainWindowViewModel viewModel)
        {
            var controlType = typeof(DesignControlModel);

            foreach (var (propertyName, value) in _values)
            {
                if (!viewModel.SupportsProperty(target, propertyName))
                    continue;

                var property = controlType.GetProperty(propertyName);
                if (property is null || !property.CanWrite)
                    continue;

                property.SetValue(target, value);
            }

            if (!string.Equals(SourceType, target.Type, StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var property in _customProperties)
            {
                var existing = target.CustomProperties.FirstOrDefault(item => string.Equals(item.Key, property.Key, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    target.CustomProperties.Add(property.Clone());
                    continue;
                }

                existing.ValueJson = property.ValueJson;
            }
        }
    }

    private void NotifyDesignerStateChanged(bool trackHistory = true)
    {
        // Общая точка синхронизации всего конструктора:
        // фиксируем историю, пересобираем XAML и просим окно перерисовать поверхность.
        if (_isApplyingDocument)
            return;

        if (_undoBatchDepth > 0)
        {
            _undoBatchTrackHistory |= trackHistory;
            return;
        }

        if (trackHistory)
            RegisterHistorySnapshot();

        MarkExportCacheStale();
        RefreshDiagnostics();
        RefreshEditorCommands();
        DesignerChanged?.Invoke(this, EventArgs.Empty);
    }
}
