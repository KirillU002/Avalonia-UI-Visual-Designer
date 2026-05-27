using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.EditorCommands;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormDesigner.Views;

/// <summary>
/// Основное окно конструктора.
/// Здесь UI из XAML "оживает": drag-and-drop, выделение, ресайз,
/// направляющие выравнивания, палитра цветов и пользовательский предпросмотр.
/// </summary>
public partial class MainWindow : Window
{
    private const double DesignPreviewChromeHeight = 36;
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(45);
    private static readonly double[] SurfaceZoomLevels = { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0 };
    // Avalonia validates application-format identifiers and rejects values with '/'.
    private static readonly DataFormat<string> ControlTypeDataFormat = DataFormat.CreateStringApplicationFormat("formdesigner-control-type");
    private static readonly FilePickerFileType DesignerDocumentFileType = new("Документы конструктора форм")
    {
        Patterns = new[] { "*.formdesigner.json", "*.json" }
    };
    private static readonly FilePickerFileType ImageFileType = new("Изображения")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico" }
    };
    private static readonly FilePickerFileType AssemblyFileType = new(".NET сборки")
    {
        Patterns = new[] { "*.dll" }
    };

    // Кэши и служебные словари нужны, чтобы не пересоздавать тяжелые объекты и
    // быстро находить visual-обертку по модели контрола во время drag/resize.
    private readonly Dictionary<string, Bitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _wrapperByControlId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Point> _dragRootStartPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Signature, IReadOnlyList<Dictionary<string, string>> Rows)> _sqlPreviewRowsBySourceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sqlPreviewRowsLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DesignControlModel> _dragSelectionRoots = new();
    private readonly List<DesignControlModel> _marqueeBaseSelection = new();
    private readonly List<CanvasSnapCandidate> _snapCandidates = new();

    // Текущее состояние активной операции мышью на дизайнерской поверхности.
    private Border? _draggedBorder;
    private DesignControlModel? _draggedModel;
    private Point _dragStartPointerPosition;
    private string _highlightedContainerId = "";

    private bool _isDragging;
    private bool _isMarqueeSelecting;
    private bool _isMarqueeAdditive;
    private bool _isMarqueeToggle;
    private bool _isPanningViewport;
    private bool _isMiniMapDraggingViewport;
    private bool _isResizing;
    private bool _isResizingDiagnosticsPane;
    private bool _isResizingLeftDockPanel;
    private bool _isResizingRightDockPanel;
    private bool _isResizingGridColumn;
    private DesignControlModel? _resizingModel;
    private Rect _miniMapContentBounds;
    private Rect _miniMapViewportBounds;
    private Point _marqueeStart;
    private Point _marqueeCurrent;
    private Point _panStartViewportPosition;
    private Point _diagnosticsPaneResizeStart;
    private Point _dockPanelResizeStart;
    private Point _resizeStart;
    private double _startWidth;
    private double _startHeight;
    private double _diagnosticsPaneResizeStartHeight;
    private double _dockPanelResizeStartSize;
    private double _miniMapScale = 1.0;
    private Vector _panStartViewportOffset;
    private Vector _miniMapDragOffset;
    private Size _miniMapViewportHostSize;
    private double _surfaceZoom = 1.0;
    private bool _isSpacePressed;
    private bool _isSpacePanGesture;
    private bool _isUpdatingZoomPresetSelection;

    private const double MarqueeActivationThreshold = 4;
    private const double SmartMeasurementMaxDistance = 240;
    private const double SmartMeasurementTickSize = 6;
    private const int MaxPreviewDataGridRows = 120;

    private sealed record CanvasSnapCandidate(string Id, string ParentId, Rect Bounds);

    private bool _isResizingDesignSurface;
    private Point _designResizeStart;
    private double _designStartWidth;
    private double _designStartHeight;

    private bool _isApplyingTextChanges;
    private MainWindowViewModel? _attachedViewModel;
    private PreviewWindow? _launchPreviewWindow;
    private HelpWindow? _helpWindow;
    private Flyout? _activeColorFlyout;
    private TextBox? _inlineCanvasEditor;
    private DesignControlModel? _inlineCanvasEditingModel;
    private string? _inlineCanvasEditingProperty;
    private bool _isClosingInlineCanvasEditor;
    private string _pendingContextMenuControlId = string.Empty;
    private readonly AutosaveRecoveryService _autosaveRecoveryService = new();
    private readonly AppSettingsService _appSettingsService = new();
    private readonly DocumentBackupService _documentBackupService = new();
    private readonly DispatcherTimer _settingsSaveTimer = new();
    private AppSettingsModel _appSettings = new();
    private readonly DispatcherTimer _autosaveTimer = new();
    private readonly DispatcherTimer _previewFilterRefreshTimer = new();
    private readonly DispatcherTimer _designerRenderTimer = new();
    private bool _isDesignerRenderScheduled;
    private string _scheduledDesignerRenderSessionId = string.Empty;
    private string _dragGestureSessionId = string.Empty;
    private string _resizeGestureSessionId = string.Empty;
    private bool _isAutosaveRunning;
    private bool _hasCheckedRecoveryOnStartup;
    private bool _isApplyingAppSettings;
    private bool _isCloseConfirmed;
    private bool _suppressRecoverySessionChangeHandling;
    private string _lastObservedDocumentSessionId = string.Empty;

    /// <summary>
    /// Подключает обработчики окна и запускает первичную синхронизацию предпросмотра.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_DataContextChanged;
        KeyDown += MainWindow_KeyDown;
        KeyUp += MainWindow_KeyUp;
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, DesignerViewport_PointerWheelChanged, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerPressedEvent, DesignerViewport_PointerPressed, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerMovedEvent, DesignerViewport_PointerMoved, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerReleasedEvent, DesignerViewport_PointerReleased, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.SizeChanged += (_, _) => RenderMiniMap();
        DesignerViewportScrollViewer.PropertyChanged += DesignerViewportScrollViewer_PropertyChanged;
        DesignViewportRoot.SizeChanged += (_, _) => RenderMiniMap();
        MiniMapCanvas.SizeChanged += (_, _) => RenderMiniMap();
        Opened += (_, _) =>
        {
            RefreshPreviewMetricsAndSurface();
            ApplySurfaceZoom();
        };
        Opened += MainWindow_Opened;
        Closing += MainWindow_Closing;
        PositionChanged += (_, _) =>
        {
            RefreshPreviewMetricsAndSurface();
            ScheduleSettingsSave();
        };
        SizeChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel && viewModel.IsImmersiveDesignerMode)
                RefreshPreviewMetricsAndSurface();
            ScheduleSettingsSave();
        };

        _autosaveTimer.Interval = AutosaveInterval;
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        _previewFilterRefreshTimer.Interval = TimeSpan.FromMilliseconds(350);
        _previewFilterRefreshTimer.Tick += PreviewFilterRefreshTimer_Tick;
        _designerRenderTimer.Interval = TimeSpan.FromMilliseconds(33);
        _designerRenderTimer.Tick += DesignerRenderTimer_Tick;
        _settingsSaveTimer.Interval = TimeSpan.FromSeconds(1);
        _settingsSaveTimer.Tick += SettingsSaveTimer_Tick;
        _appSettings = _appSettingsService.Load();
        SetZoomPresetSelection(_surfaceZoom);
        UpdateDesignerViewportCursor();
    }

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private IEnumerable<DesignControlModel> GetActiveChildControls(string? parentId)
    {
        return VM.GetChildControls(parentId);
    }

    private BindingSourceModel? GetActiveBindingSource(string? bindingSourceId)
    {
        return VM.GetBindingSource(bindingSourceId);
    }

    private IEnumerable<BindingFieldModel> GetActiveBindingFields(string? bindingSourceId)
    {
        return VM.GetBindingFields(bindingSourceId);
    }

    private IReadOnlyList<BindingSourceModel> GetActiveBindingSources()
    {
        return VM.BindingSources.ToList();
    }

    private void MainWindow_DataContextChanged(object? sender, EventArgs e)
    {
        // Окно живет дольше конкретной ViewModel, поэтому аккуратно переподписываемся,
        // чтобы не держать старый документ в памяти и не получать двойные события.
        if (_attachedViewModel is not null)
        {
            _attachedViewModel.DesignerChanged -= ViewModel_DesignerChanged;
            _attachedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _attachedViewModel.ExternalEditorCommandRequested -= ViewModel_ExternalEditorCommandRequested;
        }

        _attachedViewModel = DataContext as MainWindowViewModel;

        if (_attachedViewModel is not null)
        {
            _attachedViewModel.DesignerChanged += ViewModel_DesignerChanged;
            _attachedViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _attachedViewModel.ExternalEditorCommandRequested += ViewModel_ExternalEditorCommandRequested;
            ApplyAppSettingsToViewModel(_attachedViewModel);
            _lastObservedDocumentSessionId = _attachedViewModel.DocumentSessionId;
            UpdateWindowTitle();
        }

        RefreshPreviewMetrics();
        RenderDesigner();
    }

    private static StructureTreeItemModel? GetStructureTreeItem(object? sender)
    {
        return (sender as Control)?.DataContext as StructureTreeItemModel;
    }

    private void StructureSelectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
            VM.SelectSingleControl(control);

        e.Handled = true;
    }

    private void StructureToggleVisibilityButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
            VM.ToggleStructureControlVisibility(control);

        e.Handled = true;
    }

    private void StructureToggleLockButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
            VM.ToggleStructureControlLock(control);

        e.Handled = true;
    }

    private void StructureDuplicateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
        {
            VM.SelectSingleControl(control);
            VM.TryExecuteEditorCommand(EditorCommandId.Duplicate);
        }

        e.Handled = true;
    }

    private void StructureDeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
        {
            VM.SelectSingleControl(control);
            VM.TryExecuteEditorCommand(EditorCommandId.Delete);
        }

        e.Handled = true;
    }

    private void StructureMoveUpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
            VM.MoveStructureControlLayer(control, towardFront: true);

        e.Handled = true;
    }

    private void StructureMoveDownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GetStructureTreeItem(sender)?.Control is { } control)
            VM.MoveStructureControlLayer(control, towardFront: false);

        e.Handled = true;
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (_hasCheckedRecoveryOnStartup)
            return;

        ApplySessionWindowState(_appSettings.Session);
        _hasCheckedRecoveryOnStartup = true;
        var recoveryHandled = await CheckRecoveryOnStartupAsync();
        if (!recoveryHandled)
            await TryRestoreLastSessionDocumentAsync();

        ApplySessionViewportState(_appSettings.Session);
        _autosaveTimer.Start();
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isCloseConfirmed)
        {
            e.Cancel = true;
            if (!await EnsureUnsavedChangesHandledAsync())
                return;

            _isCloseConfirmed = true;
            await SaveAppSettingsNowAsync();
            _autosaveRecoveryService.TryDeleteDraft();
            Close();
            return;
        }

        _autosaveTimer.Stop();
        _previewFilterRefreshTimer.Stop();
        _settingsSaveTimer.Stop();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
            return;

        if (e.PropertyName == nameof(MainWindowViewModel.IsUserPreviewMode))
        {
            ResetInteractiveRuntimePreviewState();

            RenderDesigner();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentDocumentDisplayName)
            || e.PropertyName == nameof(MainWindowViewModel.HasUnsavedChanges))
        {
            UpdateWindowTitle();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsCommandPaletteOpen)
            && viewModel.IsCommandPaletteOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CommandPaletteSearchTextBox.Focus();
                CommandPaletteSearchTextBox.SelectAll();
            }, DispatcherPriority.Background);
        }

        if (IsSessionProperty(e.PropertyName) || IsExportSettingsProperty(e.PropertyName))
            ScheduleSettingsSave();

        if (e.PropertyName != nameof(MainWindowViewModel.DocumentSessionId))
            return;

        if (string.Equals(_lastObservedDocumentSessionId, viewModel.DocumentSessionId, StringComparison.Ordinal))
            return;

        _lastObservedDocumentSessionId = viewModel.DocumentSessionId;
        ResetDocumentVisualState(viewModel.DocumentSessionId);
        RenderDesigner();

        if (!_hasCheckedRecoveryOnStartup || _suppressRecoverySessionChangeHandling)
            return;

        if (!viewModel.HasUnsavedChanges)
        {
            _autosaveRecoveryService.TryDeleteDraft();
            viewModel.AutosaveStatusText = "Черновик очищен для новой сессии.";
        }
    }

    private async void ViewModel_ExternalEditorCommandRequested(EditorCommandId id)
    {
        switch (id)
        {
            case EditorCommandId.New:
                NewDocumentButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.Open:
                OpenDocumentButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.OpenProject:
                OpenDocumentButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.Save:
                SaveDocumentButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.SaveProject:
                SaveDocumentButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.SaveAs:
                SaveDocumentAsButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.AddAsset:
                await ImportProjectAssetAsync();
                break;
            case EditorCommandId.RecentFiles:
                VM.StatusText = "Recent files are available from the toolbar flyout.";
                break;
            case EditorCommandId.RestoreAutosave:
                await CheckRecoveryOnStartupAsync();
                break;
            case EditorCommandId.TogglePreviewMode:
                await LaunchPreviewAsync();
                break;
            case EditorCommandId.OpenHelp:
            case EditorCommandId.OpenQuickStart:
            case EditorCommandId.OpenPluginSdkDocs:
                OpenHelpWindow();
                break;
            case EditorCommandId.OpenColumnEditor:
                OpenDataGridColumnEditorButton_Click(this, new RoutedEventArgs());
                break;
            case EditorCommandId.CopyCurrentGeneratedFile:
                await CopyCurrentGeneratedFileAsync();
                break;
            case EditorCommandId.ValidateExportBuild:
                await ValidateExportBuildAsync();
                break;
            case EditorCommandId.ExportToProject:
                await ExportToProjectAsync();
                break;
            case EditorCommandId.ExportAsZip:
                await ExportAsZipAsync();
                break;
            case EditorCommandId.OpenValidationFolder:
                OpenValidationFolder();
                break;
            case EditorCommandId.CopyXaml:
                await CopyGeneratedXamlAsync();
                break;
            case EditorCommandId.CopyCSharp:
                await CopyGeneratedCSharpAsync();
                break;
            case EditorCommandId.ZoomIn:
                SetSurfaceZoom(GetAdjacentSurfaceZoom(zoomIn: true));
                break;
            case EditorCommandId.ZoomOut:
                SetSurfaceZoom(GetAdjacentSurfaceZoom(zoomIn: false));
                break;
            case EditorCommandId.Zoom100:
                SetSurfaceZoom(1.0);
                break;
            case EditorCommandId.FitToScreen:
                FitSurfaceToViewport();
                break;
            case EditorCommandId.RunSmokeTests:
                await RunSmokeTestsAsync();
                break;
            case EditorCommandId.ReopenLastWorkspace:
                await TryRestoreLastSessionDocumentAsync(ignoreSetting: true);
                break;
        }
    }

    private async Task RunSmokeTestsAsync()
    {
        var scriptPath = System.IO.Path.Combine(AppContext.BaseDirectory, "smoke-tests", "run-smoke-tests.ps1");
        if (!File.Exists(scriptPath))
            scriptPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "smoke-tests", "run-smoke-tests.ps1");

        if (!File.Exists(scriptPath))
        {
            const string message = "Smoke test script was not found.";
            VM.StatusText = message;
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategorySmokeTests, message);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Smoke tests unavailable", message, isPersistent: true);
            return;
        }

        var task = VM.StartWorkspaceTask("Running smoke tests", scriptPath, 0);
        VM.OpenOutputPanelCommand.Execute(null);
        VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategorySmokeTests, "Smoke tests started.", scriptPath);

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data))
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    VM.LogWorkspace(
                        args.Data.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? WorkspaceLogLevel.Error : WorkspaceLogLevel.Info,
                        MainWindowViewModel.OutputCategorySmokeTests,
                        args.Data);
                });
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data))
                    return;

                Dispatcher.UIThread.Post(() =>
                    VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategorySmokeTests, args.Data));
            };

            if (!process.Start())
                throw new InvalidOperationException("Could not start smoke tests process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            VM.ReportWorkspaceTask(task, 20, "Building generated projects");
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                VM.CompleteWorkspaceTask(task, "Smoke tests passed");
                VM.StatusText = "Smoke tests passed.";
                VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "Smoke tests passed");
            }
            else
            {
                VM.FailWorkspaceTask(task, $"Exit code {process.ExitCode}");
                VM.StatusText = $"Smoke tests failed: exit code {process.ExitCode}";
                VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Smoke tests failed", VM.StatusText, isPersistent: true);
            }
        }
        catch (Exception ex)
        {
            VM.FailWorkspaceTask(task, ex.Message);
            VM.StatusText = $"Smoke tests failed: {ex.Message}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Smoke tests failed", ex.Message, isPersistent: true);
        }
    }

    private async Task CopyGeneratedXamlAsync()
    {
        VM.GenerateXamlCommand.Execute(null);
        await CopyTextToClipboardAsync(VM.GeneratedXaml, "XAML copied. Install required NuGet packages when the checklist asks for them.");
        VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryExport, "XAML copied to clipboard.");
        VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "XAML copied");
    }

    private async Task CopyGeneratedCSharpAsync()
    {
        VM.GenerateXamlCommand.Execute(null);
        await CopyTextToClipboardAsync(VM.GeneratedCSharp, "C# copied. Check the generated namespace in the target project.");
        VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryExport, "C# copied to clipboard.");
        VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "C# copied");
    }

    private async Task CopyCurrentGeneratedFileAsync()
    {
        var file = VM.SelectedGeneratedFile;
        if (file is null)
        {
            VM.StatusText = "Select a generated file first.";
            return;
        }

        await CopyTextToClipboardAsync(file.Content, $"{file.Path} copied.");
        VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryExport, $"Copied generated file: {file.Path}");
    }

    private async Task ValidateExportBuildAsync()
    {
        VM.OpenOutputPanelCommand.Execute(null);
        var task = VM.StartWorkspaceTask("Validate export build", "Temporary Avalonia project", 0);
        var artifactsRoot = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "export-validation");

        try
        {
            var result = await VM.ValidateCurrentExportBuildAsync(
                artifactsRoot,
                message =>
                {
                    Dispatcher.UIThread.Post(() =>
                        VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategoryExport, message));
                    return Task.CompletedTask;
                });

            if (result.Status == ExportBuildValidationStatus.Passed)
            {
                VM.CompleteWorkspaceTask(task, "Build passed");
                VM.StatusText = $"Export build passed: {result.ProjectPath}";
                VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "Build passed", result.ProjectPath);
                VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryExport, "Export build validation passed.", result.ProjectPath);
            }
            else
            {
                VM.FailWorkspaceTask(task, $"Exit code {result.ExitCode}");
                VM.StatusText = $"Export build failed: exit code {result.ExitCode}";
                VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Build failed", VM.StatusText, isPersistent: true);
                VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryExport, "Export build validation failed.", result.Output);
            }
        }
        catch (Exception ex)
        {
            VM.FailWorkspaceTask(task, ex.Message);
            VM.StatusText = $"Export build validation failed: {ex.Message}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Build validation failed", ex.Message, isPersistent: true);
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryExport, "Build validation failed.", ex.Message);
        }
    }

    private async Task ExportToProjectAsync()
    {
        if (StorageProvider is null || !StorageProvider.CanPickFolder)
        {
            VM.StatusText = "Folder picker is unavailable in this environment.";
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export generated files to Avalonia project",
            AllowMultiple = false
        });
        var targetPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        if (!await ConfirmExportToProjectAsync(targetPath))
        {
            VM.StatusText = "Export to project cancelled.";
            return;
        }

        VM.OpenOutputPanelCommand.Execute(null);
        var task = VM.StartWorkspaceTask("Export to project", targetPath, 0);
        try
        {
            await VM.ExportCurrentResultToProjectAsync(
                targetPath,
                message =>
                {
                    Dispatcher.UIThread.Post(() =>
                        VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategoryExport, message));
                    return Task.CompletedTask;
                });
            VM.CompleteWorkspaceTask(task, "Export completed");
            VM.StatusText = $"Generated files exported: {targetPath}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "Export completed", targetPath);
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryExport, "Export to project completed.", targetPath);
        }
        catch (Exception ex)
        {
            VM.FailWorkspaceTask(task, ex.Message);
            VM.StatusText = $"Export to project failed: {ex.Message}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Export failed", ex.Message, isPersistent: true);
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryExport, "Export to project failed.", ex.Message);
        }
    }

    private async Task ExportAsZipAsync()
    {
        if (StorageProvider is null || !StorageProvider.CanSave)
        {
            VM.StatusText = "ZIP export is unavailable in this environment.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export generated files as ZIP",
            SuggestedFileName = "avalonia-generated-export.zip",
            DefaultExtension = "zip",
            ShowOverwritePrompt = true
        });
        var zipPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(zipPath))
            return;

        var task = VM.StartWorkspaceTask("Export ZIP", zipPath, 0);
        try
        {
            await VM.ExportCurrentResultAsZipAsync(zipPath);
            VM.CompleteWorkspaceTask(task, "ZIP exported");
            VM.StatusText = $"ZIP exported: {zipPath}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "ZIP exported", zipPath);
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryExport, "ZIP export completed.", $"{zipPath}\nIncluded generated files, README.generated.md, required-packages.txt and export-diagnostics.txt.");
        }
        catch (Exception ex)
        {
            VM.FailWorkspaceTask(task, ex.Message);
            VM.StatusText = $"ZIP export failed: {ex.Message}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "ZIP export failed", ex.Message, isPersistent: true);
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryExport, "ZIP export failed.", ex.Message);
        }
    }

    private void OpenValidationFolder()
    {
        var folder = VM.CurrentExportBuildValidation.ProjectPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            VM.StatusText = "Run Validate build first.";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Warning, "Validation folder is unavailable", VM.StatusText);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
            VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategoryExport, "Opened validation folder.", folder);
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Could not open validation folder: {ex.Message}";
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Could not open validation folder", ex.Message, isPersistent: true);
        }
    }

    private async Task<bool> ConfirmExportToProjectAsync(string targetPath)
    {
        var existingFiles = VM.GeneratedFiles
            .Select(file => System.IO.Path.Combine(targetPath, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(System.IO.Path.GetFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filesText = VM.GeneratedFiles.Count == 0
            ? "No generated files yet. The export will refresh first."
            : $"{VM.GeneratedFiles.Count} generated files will be written.";
        var packagesText = VM.RequiredPackages.Count == 0
            ? "No additional packages required."
            : $"{VM.RequiredPackages.Count} package(s) required. Commands will be written to required-packages.txt.";
        var overwriteText = existingFiles.Count == 0
            ? "No generated files with the same names were found in the selected folder."
            : $"Overwrite warning: {string.Join(", ", existingFiles.Take(5))}{(existingFiles.Count > 5 ? "..." : "")}";

        var dialog = new Window
        {
            Title = "Export to Avalonia project",
            Width = 520,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var exportButton = new Button
        {
            Content = "Export",
            MinWidth = 110,
            Classes = { "export-primary" }
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 100,
            Classes = { "export-secondary" }
        };

        exportButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        dialog.Content = new Border
        {
            Padding = new Thickness(18),
            Background = Brushes.White,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Export generated files?",
                                FontSize = 18,
                                FontWeight = FontWeight.Bold
                            },
                            new TextBlock
                            {
                                Text = targetPath,
                                Foreground = Brushes.SlateGray,
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    new Border
                    {
                        [Grid.RowProperty] = 1,
                        Margin = new Thickness(0, 16, 0, 16),
                        Padding = new Thickness(12),
                        Background = new SolidColorBrush(Color.Parse("#F8FAFC")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#D7E2EE")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Child = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock { Text = filesText, TextWrapping = TextWrapping.Wrap },
                                new TextBlock { Text = packagesText, TextWrapping = TextWrapping.Wrap },
                                new TextBlock
                                {
                                    Text = overwriteText,
                                    TextWrapping = TextWrapping.Wrap,
                                    Foreground = existingFiles.Count == 0
                                        ? Brushes.SlateGray
                                        : new SolidColorBrush(Color.Parse("#B45309")),
                                    FontWeight = existingFiles.Count == 0 ? FontWeight.Normal : FontWeight.SemiBold
                                },
                                new TextBlock
                                {
                                    Text = "Files are written directly to the selected folder. Build validation can be run after export.",
                                    Foreground = Brushes.SlateGray,
                                    TextWrapping = TextWrapping.Wrap
                                }
                            }
                        }
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 2,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, exportButton }
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private void FitSurfaceToViewport()
    {
        var chromeHeight = VM.FormHasSystemDecorations ? DesignPreviewChromeHeight : 0;
        var targetWidth = Math.Max(1, VM.PreviewFormWidth);
        var targetHeight = Math.Max(1, VM.PreviewFormHeight + chromeHeight);
        var viewportWidth = Math.Max(1, DesignerViewportScrollViewer.Viewport.Width - 48);
        var viewportHeight = Math.Max(1, DesignerViewportScrollViewer.Viewport.Height - 48);
        var zoom = Math.Clamp(Math.Min(viewportWidth / targetWidth, viewportHeight / targetHeight), 0.25, 2.0);

        SetSurfaceZoom(zoom);
        DesignerViewportScrollViewer.Offset = ClampViewportOffset(new Vector(0, 0));
    }

    private void ResetInteractiveRuntimePreviewState()
    {
        _previewFilterRefreshTimer.Stop();
        _sqlPreviewRowsLoading.Clear();
        _sqlPreviewRowsBySourceId.Clear();

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ClearPreviewRuntimeDiagnostics();
    }

    private void ResetDocumentVisualState(string sessionId)
    {
        _designerRenderTimer.Stop();
        _isDesignerRenderScheduled = false;
        _scheduledDesignerRenderSessionId = sessionId;
        _dragGestureSessionId = string.Empty;
        _resizeGestureSessionId = string.Empty;

        _isDragging = false;
        _isMarqueeSelecting = false;
        _isMarqueeAdditive = false;
        _isMarqueeToggle = false;
        _isResizing = false;
        _isResizingGridColumn = false;
        _isResizingDesignSurface = false;
        _draggedBorder = null;
        _draggedModel = null;
        _resizingModel = null;
        _highlightedContainerId = string.Empty;
        _dragSelectionRoots.Clear();
        _marqueeBaseSelection.Clear();
        _dragRootStartPositions.Clear();
        ClearSnapCandidateSnapshot();
        _wrapperByControlId.Clear();

        GuideOverlayCanvas.Children.Clear();
        SelectionOverlayCanvas.Children.Clear();
        DesignerCanvas.Children.Clear();
        MiniMapCanvas.Children.Clear();
        ResetInteractiveRuntimePreviewState();
    }

    private void UpdateWindowTitle()
    {
        if (DataContext is MainWindowViewModel viewModel)
            Title = $"{viewModel.CurrentDocumentDisplayName} - Конструктор форм Avalonia";
    }

    private void ApplyAppSettingsToViewModel(MainWindowViewModel viewModel)
    {
        _isApplyingAppSettings = true;
        try
        {
            viewModel.ApplyAppSettings(_appSettings);
        }
        finally
        {
            _isApplyingAppSettings = false;
        }

        if (!string.IsNullOrWhiteSpace(_appSettingsService.LastError))
            viewModel.StatusText = $"Настройки приложения сброшены: {_appSettingsService.LastError}";
    }

    private void ApplySessionWindowState(SessionStateModel session)
    {
        try
        {
            if (session.WindowWidth >= 900)
                Width = session.WindowWidth;
            if (session.WindowHeight >= 640)
                Height = session.WindowHeight;
            if (session.WindowX != 0 || session.WindowY != 0)
                Position = new PixelPoint(session.WindowX, session.WindowY);
            if (Enum.TryParse<WindowState>(session.WindowState, out var state))
                WindowState = state;
        }
        catch
        {
            // Broken session geometry should never block startup.
        }
    }

    private void ApplySessionViewportState(SessionStateModel session)
    {
        if (session.SurfaceZoom > 0)
            SetSurfaceZoom(Math.Clamp(session.SurfaceZoom, 0.25, 2.0));

        Dispatcher.UIThread.Post(() =>
        {
            DesignerViewportScrollViewer.Offset = new Vector(
                Math.Max(0, session.ViewportOffsetX),
                Math.Max(0, session.ViewportOffsetY));

            if (!string.IsNullOrWhiteSpace(session.SelectedControlId))
                VM.TrySelectControlById(session.SelectedControlId);
        }, DispatcherPriority.Loaded);
    }

    private void ScheduleSettingsSave()
    {
        if (_isApplyingAppSettings || DataContext is not MainWindowViewModel)
            return;

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private async void SettingsSaveTimer_Tick(object? sender, EventArgs e)
    {
        _settingsSaveTimer.Stop();
        await SaveAppSettingsNowAsync();
    }

    private async Task SaveAppSettingsNowAsync()
    {
        if (DataContext is not MainWindowViewModel)
            return;

        _appSettings.RecentFiles = VM.RecentFiles.ToList();
        _appSettings.PropertyGrid = VM.CapturePropertyGridSettings();
        _appSettings.PropertyGridFavorites = VM.CapturePropertyGridFavorites();
        _appSettings.PropertyGridCollapsedCategories = VM.CapturePropertyGridCollapsedCategories();
        _appSettings.CanvasEditor = VM.CaptureCanvasEditorSettings();
        _appSettings.ExportCache = VM.CaptureExportCache();
        _appSettings.Session = CaptureSessionState();
        await _appSettingsService.SaveAsync(_appSettings);
    }

    private SessionStateModel CaptureSessionState()
    {
        return new SessionStateModel
        {
            LastDocumentPath = VM.CurrentDocumentPath,
            WindowWidth = Width,
            WindowHeight = Height,
            WindowX = Position.X,
            WindowY = Position.Y,
            WindowState = WindowState.ToString(),
            SurfaceZoom = _surfaceZoom,
            ViewportOffsetX = DesignerViewportScrollViewer.Offset.X,
            ViewportOffsetY = DesignerViewportScrollViewer.Offset.Y,
            WorkspaceMode = VM.WorkspaceMode,
            SelectedControlId = VM.SelectedControl?.Id ?? "",
            OpenDocumentIds = VM.Workspace.Session.OpenDocumentIds.ToList(),
            ActiveDocumentId = VM.ActiveFormDocument?.Id ?? "",
            LastProjectPath = string.IsNullOrWhiteSpace(VM.CurrentProjectPath) ? VM.CurrentDocumentPath : VM.CurrentProjectPath,
            ReopenLastWorkspaceOnStartup = VM.ReopenLastWorkspaceOnStartup,
            EditorShell = VM.CaptureEditorShellLayoutState()
        };
    }

    private static bool IsSessionProperty(string? propertyName)
    {
        return propertyName is nameof(MainWindowViewModel.CurrentDocumentPath)
            or nameof(MainWindowViewModel.WorkspaceMode)
            or nameof(MainWindowViewModel.IsLeftDockOpen)
            or nameof(MainWindowViewModel.IsRightDockOpen)
            or nameof(MainWindowViewModel.IsBottomDockOpen)
            or nameof(MainWindowViewModel.ActiveBottomDockTab)
            or nameof(MainWindowViewModel.SelectedOutputCategory)
            or nameof(MainWindowViewModel.ReopenLastWorkspaceOnStartup)
            or nameof(MainWindowViewModel.LeftDockPanelWidth)
            or nameof(MainWindowViewModel.RightDockPanelWidth)
            or nameof(MainWindowViewModel.DiagnosticsPaneHeight)
            or nameof(MainWindowViewModel.IsDiagnosticsPaneExpanded)
            or nameof(MainWindowViewModel.IsCanvasSnappingEnabled)
            or nameof(MainWindowViewModel.IsDesignerGridVisible)
            or nameof(MainWindowViewModel.IsSmartGuidesEnabled)
            or nameof(MainWindowViewModel.IsDistanceHintsEnabled)
            or nameof(MainWindowViewModel.IgnoreLockedDuringSelection)
            or nameof(MainWindowViewModel.IsSelectionToolbarEnabled)
            or nameof(MainWindowViewModel.PropertyGridSettingsVersion);
    }

    private static bool IsExportSettingsProperty(string? propertyName)
    {
        return propertyName is nameof(MainWindowViewModel.ExportTarget)
            or nameof(MainWindowViewModel.ExportProjectNamespace)
            or nameof(MainWindowViewModel.DataGridExportMode)
            or nameof(MainWindowViewModel.LayoutExportMode)
            or nameof(MainWindowViewModel.XamlVerbosity)
            or nameof(MainWindowViewModel.IncludeExportComments)
            or nameof(MainWindowViewModel.IncludeSampleData)
            or nameof(MainWindowViewModel.IncludeCrudSkeleton)
            or nameof(MainWindowViewModel.IncludeCommunityToolkitAttributes)
            or nameof(MainWindowViewModel.IncludePluginRuntimeReferences)
            or nameof(MainWindowViewModel.GeneratedXaml)
            or nameof(MainWindowViewModel.GeneratedCSharp)
            or nameof(MainWindowViewModel.IsExportCacheStale)
            or nameof(MainWindowViewModel.ExportCacheStatusText);
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        if (_isAutosaveRunning
            || DataContext is not MainWindowViewModel
            || !VM.HasUnsavedChanges
            || VM.IsBusy
            || _isDragging
            || _isMarqueeSelecting
            || _isResizing
            || _isResizingGridColumn
            || _isResizingDesignSurface)
        {
            return;
        }

        _isAutosaveRunning = true;

        try
        {
            var draft = new RecoveryDraftFileModel
            {
                SessionId = VM.DocumentSessionId,
                DocumentPath = VM.CurrentDocumentPath,
                DocumentDisplayName = VM.GetRecoveryDisplayName(),
                LastAutosaveUtc = DateTime.UtcNow,
                HasUnsavedChanges = true,
                DocumentJson = VM.ExportDocumentJson()
            };

            await _autosaveRecoveryService.SaveDraftAsync(draft);
            VM.AutosaveStatusText = $"Черновик автосохранён: {DateTime.Now:HH:mm:ss}";
            _appSettings.Autosave.LastAutosaveUtc = draft.LastAutosaveUtc;
            _appSettings.Autosave.LastDraftPath = _autosaveRecoveryService.RecoveryFilePath;
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryBackgroundTasks, "Autosave completed.", _autosaveRecoveryService.RecoveryFilePath);
            ScheduleSettingsSave();
        }
        catch (Exception ex)
        {
            VM.AutosaveStatusText = $"Ошибка автосохранения: {ex.Message}";
        }
        finally
        {
            _isAutosaveRunning = false;
        }
    }

    private void PreviewFilterRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _previewFilterRefreshTimer.Stop();

        if (DataContext is MainWindowViewModel viewModel && viewModel.IsUserPreviewMode)
            RenderDesigner();
    }

    private void SchedulePreviewFilterRefresh()
    {
        _previewFilterRefreshTimer.Stop();
        _previewFilterRefreshTimer.Start();
    }

    private async Task<bool> CheckRecoveryOnStartupAsync()
    {
        var draft = await _autosaveRecoveryService.TryLoadDraftAsync();
        if (draft is null)
            return false;

        if (!draft.HasUnsavedChanges)
        {
            _autosaveRecoveryService.TryDeleteDraft();
            return false;
        }

        var recoveryWindow = new RecoveryWindow(draft);
        var decision = await recoveryWindow.ShowDialog<RecoveryDialogResult>(this);

        switch (decision)
        {
            case RecoveryDialogResult.RestoreDraft:
                _suppressRecoverySessionChangeHandling = true;
                try
                {
                    VM.LoadDocumentJson(
                        draft.DocumentJson,
                        string.IsNullOrWhiteSpace(draft.DocumentPath) ? null : draft.DocumentPath,
                        markAsSaved: false);
                }
                finally
                {
                    _suppressRecoverySessionChangeHandling = false;
                    _lastObservedDocumentSessionId = VM.DocumentSessionId;
                }

                if (!string.IsNullOrWhiteSpace(draft.DocumentPath))
                    VM.AddOrUpdateRecentFile(draft.DocumentPath);
                VM.StatusText = "Восстановлен автосохранённый черновик.";
                VM.AutosaveStatusText = $"Восстановлен черновик от {draft.LastAutosaveUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
                await SaveAppSettingsNowAsync();
                return true;

            case RecoveryDialogResult.DeleteDraft:
                _autosaveRecoveryService.TryDeleteDraft();
                VM.AutosaveStatusText = "Recovery-файл удалён.";
                return false;

            case RecoveryDialogResult.OpenNormally:
            case RecoveryDialogResult.None:
            default:
                VM.AutosaveStatusText = $"Найден черновик от {draft.LastAutosaveUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}. Восстановление отложено.";
                return false;
        }
    }

    private async Task TryRestoreLastSessionDocumentAsync(bool ignoreSetting = false)
    {
        if (!ignoreSetting && !_appSettings.Session.ReopenLastWorkspaceOnStartup)
        {
            VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategoryGeneral, "Reopen last workspace is disabled.");
            return;
        }

        var lastPath = string.IsNullOrWhiteSpace(_appSettings.Session.LastProjectPath)
            ? _appSettings.Session.LastDocumentPath
            : _appSettings.Session.LastProjectPath;
        if (string.IsNullOrWhiteSpace(lastPath))
            return;

        if (!File.Exists(lastPath))
        {
            VM.StatusText = $"Последний файл недоступен: {lastPath}";
            VM.LogWorkspace(WorkspaceLogLevel.Warning, MainWindowViewModel.OutputCategoryGeneral, VM.StatusText);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Warning, "Last workspace unavailable", lastPath, isPersistent: true);
            VM.IsStartScreenVisible = true;
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(lastPath);
            VM.LoadDocumentJson(json, lastPath);
            VM.AddOrUpdateRecentFile(lastPath);
            VM.StatusText = $"Восстановлена последняя сессия: {System.IO.Path.GetFileName(lastPath)}";
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryGeneral, VM.StatusText, lastPath);
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Не удалось восстановить последнюю сессию: {ex.Message}";
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryGeneral, VM.StatusText, lastPath);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Reopen failed", ex.Message, isPersistent: true);
            VM.IsStartScreenVisible = true;
        }
    }

    private void DesignerViewportScrollViewer_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty)
        {
            RenderMiniMap();
            ScheduleSettingsSave();
        }
    }

    private void ViewModel_DesignerChanged(object? sender, EventArgs e)
    {
        if (_isDragging || _isMarqueeSelecting || _isResizing || _isResizingGridColumn || _isResizingDesignSurface)
            return;

        ScheduleDesignerRender();
    }

    private void ScheduleDesignerRender()
    {
        _scheduledDesignerRenderSessionId = VM.DocumentSessionId;
        _isDesignerRenderScheduled = true;
        if (!_designerRenderTimer.IsEnabled)
            _designerRenderTimer.Start();
    }

    private void DesignerRenderTimer_Tick(object? sender, EventArgs e)
    {
        _designerRenderTimer.Stop();
        if (!_isDesignerRenderScheduled)
            return;

        _isDesignerRenderScheduled = false;
        if (!string.Equals(_scheduledDesignerRenderSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
            return;

        var stopwatch = Stopwatch.StartNew();
        RenderDesigner();
        stopwatch.Stop();
        if (stopwatch.Elapsed.TotalMilliseconds >= 16)
            Debug.WriteLine($"[FormDesigner:Perf] Designer render: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");
    }

    private void RefreshPreviewMetricsAndSurface()
    {
        RefreshPreviewMetrics();
        RenderDesigner();
    }

    private void RefreshPreviewMetrics()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Считываем параметры текущего монитора, чтобы режимы "Рабочая область" и "Полный экран"
        // в дизайнере выглядели так же, как будут выглядеть у пользователя при запуске.
        var screen = Screens?.ScreenFromVisual(this)
            ?? Screens?.ScreenFromWindow(this)
            ?? Screens?.All.FirstOrDefault(item => item.IsPrimary)
            ?? Screens?.All.FirstOrDefault();

        if (screen is null)
            return;

        var scaling = screen.Scaling <= 0 ? 1d : screen.Scaling;
        var monitorName = screen.IsPrimary ? "Основной монитор" : "Текущий монитор";

        viewModel.UpdatePreviewDisplayMetrics(
            screen.Bounds.Width / scaling,
            screen.Bounds.Height / scaling,
            screen.WorkingArea.Width / scaling,
            screen.WorkingArea.Height / scaling,
            monitorName);
    }

    private void ApplySurfaceZoom()
    {
        if (DesignSurfaceHost is null || DesignViewportRoot is null)
            return;

        DesignSurfaceHost.RenderTransform = new ScaleTransform(_surfaceZoom, _surfaceZoom);
        DesignViewportRoot.Width = Math.Max(1, DesignSurfaceHost.Width * _surfaceZoom);
        DesignViewportRoot.Height = Math.Max(1, DesignSurfaceHost.Height * _surfaceZoom);
        DesignViewportRoot.InvalidateMeasure();
        DesignViewportRoot.InvalidateArrange();
        DesignerViewportScrollViewer.InvalidateMeasure();
        SetZoomPresetSelection(_surfaceZoom);
        RenderMiniMap();
    }

    private void SetSurfaceZoom(double zoom, Point? viewportAnchor = null)
    {
        var normalizedZoom = NormalizeSurfaceZoom(zoom);
        if (Math.Abs(normalizedZoom - _surfaceZoom) < 0.001)
        {
            SetZoomPresetSelection(normalizedZoom);
            return;
        }

        var currentOffset = DesignerViewportScrollViewer.Offset;
        var anchor = viewportAnchor ?? new Point(
            Math.Max(0, DesignerViewportScrollViewer.Viewport.Width / 2),
            Math.Max(0, DesignerViewportScrollViewer.Viewport.Height / 2));
        var safeZoom = Math.Max(0.001, _surfaceZoom);
        var designAnchor = new Point(
            (currentOffset.X + anchor.X) / safeZoom,
            (currentOffset.Y + anchor.Y) / safeZoom);

        _surfaceZoom = normalizedZoom;
        VM.SetEditorZoom(_surfaceZoom);
        ApplySurfaceZoom();

        Dispatcher.UIThread.Post(() =>
        {
            var targetOffset = new Vector(
                (designAnchor.X * _surfaceZoom) - anchor.X,
                (designAnchor.Y * _surfaceZoom) - anchor.Y);
            DesignerViewportScrollViewer.Offset = ClampViewportOffset(targetOffset);
            ScheduleSettingsSave();
        }, DispatcherPriority.Render);
    }

    private static double NormalizeSurfaceZoom(double zoom)
    {
        var closest = SurfaceZoomLevels[0];
        var bestDistance = Math.Abs(zoom - closest);

        foreach (var candidate in SurfaceZoomLevels)
        {
            var distance = Math.Abs(candidate - zoom);
            if (distance < bestDistance)
            {
                closest = candidate;
                bestDistance = distance;
            }
        }

        return closest;
    }

    private double GetAdjacentSurfaceZoom(bool zoomIn)
    {
        var currentIndex = Array.FindIndex(SurfaceZoomLevels, level => Math.Abs(level - _surfaceZoom) < 0.001);
        if (currentIndex < 0)
            currentIndex = Array.FindLastIndex(SurfaceZoomLevels, level => level <= _surfaceZoom);

        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = zoomIn
            ? Math.Min(SurfaceZoomLevels.Length - 1, currentIndex + 1)
            : Math.Max(0, currentIndex - 1);
        return SurfaceZoomLevels[nextIndex];
    }

    private void SetZoomPresetSelection(double zoom)
    {
        if (ZoomPresetComboBox is null)
            return;

        var targetText = $"{Math.Round(zoom * 100):0}%";
        _isUpdatingZoomPresetSelection = true;

        try
        {
            ComboBoxItem? targetItem = null;
            foreach (var item in ZoomPresetComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), targetText, StringComparison.Ordinal))
                {
                    targetItem = item;
                    break;
                }
            }

            ZoomPresetComboBox.SelectedItem = targetItem;
        }
        finally
        {
            _isUpdatingZoomPresetSelection = false;
        }
    }

    private static double? ParseZoomPercent(object? value)
    {
        var raw = value switch
        {
            ComboBoxItem comboBoxItem => comboBoxItem.Content?.ToString(),
            _ => value?.ToString()
        };

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? percent / 100d
            : null;
    }

    private Vector ClampViewportOffset(Vector desiredOffset)
    {
        var maxX = Math.Max(0, DesignerViewportScrollViewer.Extent.Width - DesignerViewportScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, DesignerViewportScrollViewer.Extent.Height - DesignerViewportScrollViewer.Viewport.Height);
        return new Vector(
            Math.Clamp(desiredOffset.X, 0, maxX),
            Math.Clamp(desiredOffset.Y, 0, maxY));
    }

    private void RenderMiniMap()
    {
        if (DataContext is not MainWindowViewModel || MiniMapHost is null || MiniMapCanvas is null || MiniMapStatusTextBlock is null)
            return;

        var viewport = DesignerViewportScrollViewer.Viewport;
        var extent = DesignerViewportScrollViewer.Extent;
        var shouldShow = !VM.IsUserPreviewMode
            && viewport.Width > 0
            && viewport.Height > 0
            && _surfaceZoom > 1.001;

        if (!shouldShow || DesignSurfaceHost.Width <= 0 || DesignSurfaceHost.Height <= 0)
        {
            _isMiniMapDraggingViewport = false;
            MiniMapHost.IsVisible = false;
            MiniMapCanvas.Children.Clear();
            MiniMapStatusTextBlock.Text = $"{Math.Round(_surfaceZoom * 100):0}%";
            return;
        }

        MiniMapHost.IsVisible = true;
        MiniMapCanvas.Cursor = new Cursor(StandardCursorType.Hand);

        var canvasWidth = MiniMapCanvas.Width > 0 ? MiniMapCanvas.Width : Math.Max(1, MiniMapCanvas.Bounds.Width);
        var canvasHeight = MiniMapCanvas.Height > 0 ? MiniMapCanvas.Height : Math.Max(1, MiniMapCanvas.Bounds.Height);
        var hostWidth = Math.Max(1, DesignSurfaceHost.Width);
        var hostHeight = Math.Max(1, DesignSurfaceHost.Height);
        const double padding = 10;
        var availableWidth = Math.Max(20, canvasWidth - (padding * 2));
        var availableHeight = Math.Max(20, canvasHeight - (padding * 2));
        _miniMapScale = Math.Min(availableWidth / hostWidth, availableHeight / hostHeight);

        if (!double.IsFinite(_miniMapScale) || _miniMapScale <= 0)
            _miniMapScale = 1;

        var contentWidth = hostWidth * _miniMapScale;
        var contentHeight = hostHeight * _miniMapScale;
        var contentX = (canvasWidth - contentWidth) / 2;
        var contentY = (canvasHeight - contentHeight) / 2;
        _miniMapContentBounds = new Rect(contentX, contentY, contentWidth, contentHeight);

        var safeZoom = Math.Max(0.001, _surfaceZoom);
        var viewportHostWidth = Math.Min(hostWidth, viewport.Width / safeZoom);
        var viewportHostHeight = Math.Min(hostHeight, viewport.Height / safeZoom);
        var viewportHostX = Math.Clamp(DesignerViewportScrollViewer.Offset.X / safeZoom, 0, Math.Max(0, hostWidth - viewportHostWidth));
        var viewportHostY = Math.Clamp(DesignerViewportScrollViewer.Offset.Y / safeZoom, 0, Math.Max(0, hostHeight - viewportHostHeight));
        _miniMapViewportHostSize = new Size(viewportHostWidth, viewportHostHeight);
        _miniMapViewportBounds = new Rect(
            contentX + (viewportHostX * _miniMapScale),
            contentY + (viewportHostY * _miniMapScale),
            Math.Max(18, viewportHostWidth * _miniMapScale),
            Math.Max(18, viewportHostHeight * _miniMapScale));

        MiniMapStatusTextBlock.Text = $"{Math.Round(_surfaceZoom * 100):0}% • область {Math.Round(viewportHostWidth):0}×{Math.Round(viewportHostHeight):0}";
        MiniMapCanvas.Children.Clear();

        var frame = new Border
        {
            Width = contentWidth,
            Height = contentHeight,
            Background = new SolidColorBrush(Color.Parse("#EEF4FB")),
            BorderBrush = new SolidColorBrush(Color.Parse("#8FA7C6")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(frame, contentX);
        Canvas.SetTop(frame, contentY);
        MiniMapCanvas.Children.Add(frame);

        var titleBarHeight = DesignSurfaceTitleBar.IsVisible ? DesignPreviewChromeHeight : 0;
        if (titleBarHeight > 0)
        {
            var titleBar = new Border
            {
                Width = contentWidth,
                Height = titleBarHeight * _miniMapScale,
                Background = new SolidColorBrush(Color.Parse("#D6E2F1")),
                BorderBrush = new SolidColorBrush(Color.Parse("#7F97B6")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(titleBar, contentX);
            Canvas.SetTop(titleBar, contentY);
            MiniMapCanvas.Children.Add(titleBar);
        }

        var surfaceTop = GetDesignerCanvasHostTop();
        foreach (var control in VM.Controls)
        {
            var absolute = VM.GetAbsolutePosition(control);
            var miniRect = new Border
            {
                Width = Math.Max(3, control.Width * _miniMapScale),
                Height = Math.Max(3, control.Height * _miniMapScale),
                Background = new SolidColorBrush(Color.Parse(VM.IsControlSelected(control) ? "#60A5FA99" : "#0F172A33")),
                BorderBrush = new SolidColorBrush(Color.Parse(VM.IsControlSelected(control) ? "#2563EB" : "#64748B")),
                BorderThickness = new Thickness(VM.IsControlSelected(control) ? 1.5 : 1),
                CornerRadius = new CornerRadius(3),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(miniRect, contentX + (absolute.X * _miniMapScale));
            Canvas.SetTop(miniRect, contentY + ((absolute.Y + surfaceTop) * _miniMapScale));
            MiniMapCanvas.Children.Add(miniRect);
        }

        var viewportRect = new Border
        {
            Width = _miniMapViewportBounds.Width,
            Height = _miniMapViewportBounds.Height,
            Background = new SolidColorBrush(Color.FromArgb(52, 37, 99, 235)),
            BorderBrush = new SolidColorBrush(Color.Parse("#1D4ED8")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(viewportRect, _miniMapViewportBounds.X);
        Canvas.SetTop(viewportRect, _miniMapViewportBounds.Y);
        MiniMapCanvas.Children.Add(viewportRect);
    }

    private double GetDesignerCanvasHostLeft()
    {
        var left = Canvas.GetLeft(DesignerCanvas);
        return double.IsNaN(left) ? 0 : left;
    }

    private double GetDesignerCanvasHostTop()
    {
        var top = Canvas.GetTop(DesignerCanvas);
        return double.IsNaN(top) ? 0 : top;
    }

    private Point ClampMiniMapPointToContent(Point point)
    {
        return new Point(
            Math.Clamp(point.X, _miniMapContentBounds.X, _miniMapContentBounds.Right),
            Math.Clamp(point.Y, _miniMapContentBounds.Y, _miniMapContentBounds.Bottom));
    }

    private Point MiniMapPointToHost(Point point)
    {
        var clamped = ClampMiniMapPointToContent(point);
        return new Point(
            (clamped.X - _miniMapContentBounds.X) / Math.Max(0.001, _miniMapScale),
            (clamped.Y - _miniMapContentBounds.Y) / Math.Max(0.001, _miniMapScale));
    }

    private void NavigateViewportToMiniMapPoint(Point point)
    {
        var hostPoint = MiniMapPointToHost(point);
        var targetOffset = new Vector(
            (hostPoint.X * _surfaceZoom) - (DesignerViewportScrollViewer.Viewport.Width / 2),
            (hostPoint.Y * _surfaceZoom) - (DesignerViewportScrollViewer.Viewport.Height / 2));
        DesignerViewportScrollViewer.Offset = ClampViewportOffset(targetOffset);
    }

    private void MoveViewportToMiniMapTopLeft(Point miniMapTopLeft)
    {
        var maxMiniMapX = Math.Max(_miniMapContentBounds.X, _miniMapContentBounds.Right - _miniMapViewportBounds.Width);
        var maxMiniMapY = Math.Max(_miniMapContentBounds.Y, _miniMapContentBounds.Bottom - _miniMapViewportBounds.Height);
        var clampedTopLeft = new Point(
            Math.Clamp(miniMapTopLeft.X, _miniMapContentBounds.X, maxMiniMapX),
            Math.Clamp(miniMapTopLeft.Y, _miniMapContentBounds.Y, maxMiniMapY));
        var hostTopLeft = new Point(
            (clampedTopLeft.X - _miniMapContentBounds.X) / Math.Max(0.001, _miniMapScale),
            (clampedTopLeft.Y - _miniMapContentBounds.Y) / Math.Max(0.001, _miniMapScale));
        var clampedHostTopLeft = new Point(
            Math.Clamp(hostTopLeft.X, 0, Math.Max(0, DesignSurfaceHost.Width - _miniMapViewportHostSize.Width)),
            Math.Clamp(hostTopLeft.Y, 0, Math.Max(0, DesignSurfaceHost.Height - _miniMapViewportHostSize.Height)));

        DesignerViewportScrollViewer.Offset = ClampViewportOffset(new Vector(
            clampedHostTopLeft.X * _surfaceZoom,
            clampedHostTopLeft.Y * _surfaceZoom));
    }

    private Point GetDesignHostPosition(PointerEventArgs e)
    {
        return GetDesignHostPosition(e.GetPosition(DesignViewportRoot));
    }

    private Point GetDesignHostPosition(DragEventArgs e)
    {
        return GetDesignHostPosition(e.GetPosition(DesignViewportRoot));
    }

    private Point GetDesignHostPosition(Point viewportRootPosition)
    {
        var safeZoom = Math.Max(0.001, _surfaceZoom);
        return new Point(viewportRootPosition.X / safeZoom, viewportRootPosition.Y / safeZoom);
    }

    private Point GetDesignCanvasPosition(PointerEventArgs e)
    {
        return ToDesignerCanvasPosition(GetDesignHostPosition(e));
    }

    private Point GetDesignCanvasPosition(DragEventArgs e)
    {
        return ToDesignerCanvasPosition(GetDesignHostPosition(e));
    }

    private Point ToDesignerCanvasPosition(Point hostPoint)
    {
        return new Point(hostPoint.X - GetDesignerCanvasHostLeft(), hostPoint.Y - GetDesignerCanvasHostTop());
    }

    private void UpdateDesignerViewportCursor()
    {
        var cursorType = (_isPanningViewport || _isSpacePressed)
            ? StandardCursorType.SizeAll
            : StandardCursorType.Arrow;
        DesignerViewportScrollViewer.Cursor = new Cursor(cursorType);
    }

    private void StartViewportPan(PointerEventArgs e, bool isSpacePanGesture)
    {
        _isPanningViewport = true;
        _isSpacePanGesture = isSpacePanGesture;
        _panStartViewportPosition = e.GetPosition(DesignerViewportScrollViewer);
        _panStartViewportOffset = DesignerViewportScrollViewer.Offset;
        UpdateDesignerViewportCursor();
        e.Pointer.Capture(DesignerViewportScrollViewer);
        e.Handled = true;
    }

    private void StopViewportPan(PointerEventArgs? e = null)
    {
        _isPanningViewport = false;
        _isSpacePanGesture = false;
        UpdateDesignerViewportCursor();

        if (e is not null)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void DesignerViewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        var nextZoom = e.Delta.Y >= 0
            ? GetAdjacentSurfaceZoom(zoomIn: true)
            : GetAdjacentSurfaceZoom(zoomIn: false);
        var viewportAnchor = e.GetPosition(DesignerViewportScrollViewer);
        SetSurfaceZoom(nextZoom, viewportAnchor);
        e.Handled = true;
    }

    private void DesignerViewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(DesignerViewportScrollViewer);
        if (point.Properties.IsMiddleButtonPressed)
        {
            StartViewportPan(e, isSpacePanGesture: false);
            return;
        }

        if (_isSpacePressed && point.Properties.IsLeftButtonPressed)
            StartViewportPan(e, isSpacePanGesture: true);
    }

    private void DesignerViewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanningViewport)
            return;

        var currentPosition = e.GetPosition(DesignerViewportScrollViewer);
        var delta = currentPosition - _panStartViewportPosition;
        DesignerViewportScrollViewer.Offset = ClampViewportOffset(
            new Vector(_panStartViewportOffset.X - delta.X, _panStartViewportOffset.Y - delta.Y));
        e.Handled = true;
    }

    private void DesignerViewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanningViewport)
            return;

        var point = e.GetCurrentPoint(DesignerViewportScrollViewer);
        var middleStillPressed = point.Properties.IsMiddleButtonPressed;
        var leftStillPressed = point.Properties.IsLeftButtonPressed;
        if (middleStillPressed || (_isSpacePanGesture && leftStillPressed))
            return;

        StopViewportPan(e);
    }

    private bool TryExecuteEditorShortcut(KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return false;

        var isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var sourceEditsText = e.Source is TextBox or ComboBox;
        EditorCommandId? commandId = null;

        if (isCtrl && isShift && e.Key == Key.P)
            commandId = EditorCommandId.OpenCommandPalette;
        else if (e.Key == Key.F1)
            commandId = EditorCommandId.OpenHelp;
        else if (e.Key == Key.F5)
            commandId = EditorCommandId.TogglePreviewMode;
        else if (e.Key == Key.F12)
            commandId = EditorCommandId.ToggleDesignFrames;
        else if (isCtrl && isShift && e.Key == Key.S)
            commandId = EditorCommandId.SaveAs;
        else if (isCtrl && e.Key == Key.S)
            commandId = EditorCommandId.Save;
        else if (isCtrl && e.Key == Key.O)
            commandId = EditorCommandId.Open;
        else if (isCtrl && e.Key == Key.N)
            commandId = EditorCommandId.New;
        else if (isCtrl && IsKeyNamed(e, "D0", "NumPad0"))
            commandId = EditorCommandId.Zoom100;
        else if (isCtrl && IsKeyNamed(e, "OemPlus", "Add"))
            commandId = EditorCommandId.ZoomIn;
        else if (isCtrl && IsKeyNamed(e, "OemMinus", "Subtract"))
            commandId = EditorCommandId.ZoomOut;
        else if (!sourceEditsText)
        {
            if (isCtrl && e.Key == Key.Z)
                commandId = EditorCommandId.Undo;
            else if (isCtrl && e.Key == Key.Y)
                commandId = EditorCommandId.Redo;
            else if (isCtrl && e.Key == Key.X)
                commandId = EditorCommandId.Cut;
            else if (isCtrl && e.Key == Key.C)
                commandId = EditorCommandId.Copy;
            else if (isCtrl && e.Key == Key.V)
                commandId = EditorCommandId.Paste;
            else if (isCtrl && e.Key == Key.A)
                commandId = EditorCommandId.SelectAll;
            else if (isCtrl && e.Key == Key.D)
                commandId = EditorCommandId.Duplicate;
            else if (isCtrl && isShift && e.Key == Key.G)
                commandId = EditorCommandId.Ungroup;
            else if (isCtrl && e.Key == Key.G)
                commandId = EditorCommandId.Group;
            else if (isCtrl && isShift && e.Key == Key.L)
                commandId = EditorCommandId.Unlock;
            else if (isCtrl && e.Key == Key.L)
                commandId = EditorCommandId.Lock;
            else if (e.Key == Key.Delete)
                commandId = EditorCommandId.Delete;
            else if (e.Key == Key.PageUp)
                commandId = EditorCommandId.BringToFront;
            else if (e.Key == Key.PageDown)
                commandId = EditorCommandId.SendToBack;
        }

        if (commandId is null)
            return false;

        var command = viewModel.GetEditorCommand(commandId.Value);
        if (command is null)
            return false;

        if (!command.CanExecute(null))
        {
            viewModel.StatusText = string.IsNullOrWhiteSpace(command.DisabledReason)
                ? $"Command is disabled: {command.Title}"
                : $"{command.Title}: {command.DisabledReason}";
            e.Handled = true;
            return true;
        }

        command.Execute(null);
        viewModel.RefreshEditorCommands();
        e.Handled = true;
        return true;
    }

    private static bool IsKeyNamed(KeyEventArgs e, params string[] names)
    {
        var keyName = e.Key.ToString();
        return names.Any(name => string.Equals(keyName, name, StringComparison.OrdinalIgnoreCase));
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel)
            return;

        if (e.Key == Key.Space && e.Source is not TextBox and not ComboBox)
        {
            _isSpacePressed = true;
            UpdateDesignerViewportCursor();
        }

        if (e.Key == Key.Escape && _inlineCanvasEditor is not null)
        {
            CloseInlineCanvasEditor(commitChanges: false);
            e.Handled = true;
            return;
        }

        if (TryExecuteEditorShortcut(e))
            return;

        if (e.Key == Key.F1)
        {
            OpenHelpWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F11)
        {
            VM.ToggleImmersiveDesignerModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            VM.ToggleUserPreviewModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            _ = LaunchPreviewAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && VM.IsUserPreviewMode)
        {
            VM.ToggleUserPreviewModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && VM.IsImmersiveDesignerMode)
        {
            VM.ToggleImmersiveDesignerModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (VM.IsUserPreviewMode)
            return;

        if (e.Source is TextBox or ComboBox)
            return;

        if (e.Key == Key.Escape && VM.HasSelectedControl)
        {
            VM.TryExecuteEditorCommand(EditorCommandId.ClearSelection);
            ClearGuideOverlay();
            ClearSelectionOverlay();
            RenderDesigner();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && VM.SelectedControl is not null && VM.SupportsText(VM.SelectedControl))
        {
            BeginInlineCanvasInteraction(VM.SelectedControl);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            SaveDocumentButton_Click(this, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O)
        {
            OpenDocumentButton_Click(this, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N)
        {
            NewDocumentButton_Click(this, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            VM.UndoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            VM.RedoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key == Key.L)
        {
            VM.UnlockSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.L)
        {
            VM.LockSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key == Key.G)
        {
            VM.UngroupSelectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.G)
        {
            VM.GroupSelectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.D)
        {
            VM.DuplicateSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key == Key.C)
        {
            VM.CopyStyleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key == Key.V)
        {
            VM.PasteStyleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            VM.CopySelectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
        {
            VM.PasteSelectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
        {
            VM.SelectAllControls();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageUp)
        {
            VM.BringSelectionToFrontCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageDown)
        {
            VM.SendSelectionToBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            VM.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (VM.SelectedControl is null)
            return;

        if (TryHandleCanvasNudge(e))
            return;
    }

    private bool TryHandleCanvasNudge(KeyEventArgs e)
    {
        var isArrow = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if (!isArrow)
            return false;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var step = Math.Max(1, VM.SnapStep);
            var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            VM.MoveSelectedControl(dx, dy);
            e.Handled = true;
            return true;
        }

        var large = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var commandId = e.Key switch
        {
            Key.Left => large ? EditorCommandId.NudgeLargeLeft : EditorCommandId.NudgeLeft,
            Key.Right => large ? EditorCommandId.NudgeLargeRight : EditorCommandId.NudgeRight,
            Key.Up => large ? EditorCommandId.NudgeLargeUp : EditorCommandId.NudgeUp,
            Key.Down => large ? EditorCommandId.NudgeLargeDown : EditorCommandId.NudgeDown,
            _ => (EditorCommandId?)null
        };

        if (commandId.HasValue)
        {
            VM.TryExecuteEditorCommand(commandId.Value);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
            return;

        _isSpacePressed = false;
        if (!_isPanningViewport)
            UpdateDesignerViewportCursor();
    }

    private void CommandPaletteTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (e.Key == Key.Escape)
        {
            viewModel.CloseCommandPaletteView();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.ExecuteSelectedCommandPaletteView();
            e.Handled = true;
        }
    }

    private void CommandPaletteListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.ExecuteSelectedCommandPaletteView();
        e.Handled = true;
    }

    private async void ToolboxItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsUserPreviewMode)
            return;

        if (sender is not Border border || border.DataContext is not ToolboxItem item)
            return;

        // В drag-and-drop передаем только тип контрола.
        // Конкретная модель будет создана уже в момент drop на поверхности.
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(ControlTypeDataFormat, item.Type));

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }

    private void DesignerCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDragging || _isResizing || _isResizingDesignSurface || _isPanningViewport || DataContext is not MainWindowViewModel)
            return;

        if (VM.IsUserPreviewMode)
            return;

        if (_inlineCanvasEditor is not null)
        {
            CloseInlineCanvasEditor(commitChanges: true);
            e.Handled = true;
            return;
        }

        _pendingContextMenuControlId = string.Empty;
        _isMarqueeSelecting = true;
        _isMarqueeToggle = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _isMarqueeAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _marqueeBaseSelection.Clear();
        _marqueeBaseSelection.AddRange(VM.GetSelectedControls());
        _marqueeStart = GetDesignCanvasPosition(e);
        _marqueeCurrent = _marqueeStart;
        ClearGuideOverlay();
        ClearSelectionOverlay();

        if (sender is InputElement element)
            e.Pointer.Capture(element);

        e.Handled = true;
    }

    private void DesignerCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!_isMarqueeSelecting)
            return;

        _marqueeCurrent = GetDesignCanvasPosition(e);
        var selectionRect = CreateSelectionRect(_marqueeStart, _marqueeCurrent);

        if (selectionRect.Width >= MarqueeActivationThreshold || selectionRect.Height >= MarqueeActivationThreshold)
            RenderSelectionMarquee(selectionRect);
        else
            ClearGuideOverlay();

        e.Handled = true;
    }

    private void DesignerCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!_isMarqueeSelecting)
            return;

        _isMarqueeSelecting = false;
        _isMarqueeToggle = false;
        e.Pointer.Capture(null);

        var selectionRect = CreateSelectionRect(_marqueeStart, _marqueeCurrent);
        var hasSelectionArea = selectionRect.Width >= MarqueeActivationThreshold || selectionRect.Height >= MarqueeActivationThreshold;

        if (!hasSelectionArea)
        {
            if (!_isMarqueeAdditive && !_isMarqueeToggle)
                VM.ClearSelection();

            ClearGuideOverlay();
            _marqueeBaseSelection.Clear();
            RenderDesigner();
            e.Handled = true;
            return;
        }

        var hitControls = GetControlsInSelection(selectionRect);

        if (_isMarqueeToggle)
        {
            var toggled = _marqueeBaseSelection.ToList();
            foreach (var control in hitControls)
            {
                if (toggled.Any(selected => selected.Id == control.Id))
                    toggled.RemoveAll(selected => selected.Id == control.Id);
                else
                    toggled.Add(control);
            }

            VM.SelectControls(toggled, hitControls.LastOrDefault() ?? toggled.LastOrDefault());
        }
        else if (_isMarqueeAdditive)
        {
            var merged = _marqueeBaseSelection.ToList();
            foreach (var control in hitControls.Where(control => merged.All(selected => selected.Id != control.Id)))
                merged.Add(control);

            VM.SelectControls(merged, hitControls.LastOrDefault() ?? VM.SelectedControl);
        }
        else
        {
            VM.SelectControls(hitControls, hitControls.LastOrDefault());
        }

        ClearGuideOverlay();
        ClearSelectionOverlay();
        _marqueeBaseSelection.Clear();
        RenderDesigner();
        e.Handled = true;
    }

    private void DesignerCanvas_DragOver(object? sender, DragEventArgs e)
    {
        if (VM.IsUserPreviewMode)
        {
            e.DragEffects = DragDropEffects.None;
            ClearGuideOverlay();
            return;
        }

        e.DragEffects = e.DataTransfer.Contains(ControlTypeDataFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        if (e.DragEffects == DragDropEffects.Copy)
        {
            ClearGuideOverlay();
            var position = GetDesignCanvasPosition(e);
            RenderContainerHighlight(VM.FindDeepestContainerAt(position.X, position.Y));
        }
        else
        {
            ClearGuideOverlay();
        }
    }

    private void DesignerCanvas_Drop(object? sender, DragEventArgs e)
    {
        if (VM.IsUserPreviewMode)
            return;

        if (!e.DataTransfer.Contains(ControlTypeDataFormat))
            return;

        var type = e.DataTransfer.TryGetValue(ControlTypeDataFormat);
        if (string.IsNullOrWhiteSpace(type))
            return;

        // При drop ищем самый глубокий контейнер под курсором и уже в его локальных координатах
        // создаем новый контрол. Благодаря этому drag работает и для вложенных Border/Grid.
        var position = GetDesignCanvasPosition(e);
        var targetContainer = VM.FindDeepestContainerAt(position.X, position.Y);
        var localPosition = VM.ToLocalPosition(targetContainer?.Id, position.X, position.Y);
        VM.CreateControl(type, localPosition.X, localPosition.Y, targetContainer?.Id, bypassGridSnap: IsSnapBypassed(e.KeyModifiers));
        ClearGuideOverlay();
    }

    private void RenderDesigner()
    {
        if (_attachedViewModel is null)
            return;

        RefreshPreviewMetrics();
        _isApplyingTextChanges = true;

        try
        {
            // Поверхность в дизайнере имитирует реальную форму:
            // отдельно рисуем "рамку окна", сетку, слой направляющих и сами контролы.
            var surfaceWidth = VM.PreviewFormWidth;
            var surfaceHeight = VM.PreviewFormHeight;
            var chromeHeight = VM.FormHasSystemDecorations ? DesignPreviewChromeHeight : 0;

            DesignSurfaceHost.Width = surfaceWidth;
            DesignSurfaceHost.Height = surfaceHeight + chromeHeight;

            DesignSurfaceTitleBar.Width = surfaceWidth;
            DesignSurfaceTitleBar.Height = chromeHeight;
            DesignSurfaceTitleBar.IsVisible = chromeHeight > 0;

            DesignSurfaceBorder.Width = surfaceWidth;
            DesignSurfaceBorder.Height = surfaceHeight;
            DesignSurfaceBorder.CornerRadius = chromeHeight > 0
                ? new CornerRadius(0, 0, 10, 10)
                : new CornerRadius(10);

            Canvas.SetTop(DesignSurfaceBorder, chromeHeight);
            Canvas.SetTop(GridOverlayCanvas, chromeHeight);
            Canvas.SetTop(GuideOverlayCanvas, chromeHeight);
            Canvas.SetTop(DesignerCanvas, chromeHeight);
            Canvas.SetTop(SelectionOverlayCanvas, chromeHeight);

            Canvas.SetLeft(DesignResizeHandle, Math.Max(0, surfaceWidth - 10));
            Canvas.SetTop(DesignResizeHandle, Math.Max(0, surfaceHeight + chromeHeight - 10));
            ApplySurfaceZoom();

            RenderGridOverlay();
            GuideOverlayCanvas.Children.Clear();
            SelectionOverlayCanvas.Children.Clear();
            DesignerCanvas.Children.Clear();
            _wrapperByControlId.Clear();

            if (!VM.IsUserPreviewMode && VM.Controls.Count == 0)
            {
                DesignerCanvas.Children.Add(CreateEmptyStateCard());
            }

            AddControlsToCanvas(
                DesignerCanvas,
                null,
                VM.DesignWidth,
                VM.DesignHeight,
                VM.PreviewFormWidth,
                VM.PreviewFormHeight,
                VM.IsUserPreviewMode);

            RenderIdleSelectionOverlay();
            RenderMiniMap();
        }
        finally
        {
            _isApplyingTextChanges = false;
        }
    }

    private void AddControlsToCanvas(
        Canvas host,
        DesignControlModel? parent,
        double baseParentWidth,
        double baseParentHeight,
        double actualParentWidth,
        double actualParentHeight,
        bool useUserPreview)
    {
        var children = GetActiveChildControls(parent?.Id)
            .Where(model => !useUserPreview || model.IsVisible)
            .ToList();
        if (children.Count == 0)
            return;

        var layoutMode = parent is null
            ? DesignerLayoutModes.NormalizeMode(VM.SurfaceLayoutMode)
            : VM.GetLayoutModeForControl(parent);

        if (DesignerLayoutModes.IsAbsolute(layoutMode))
        {
            foreach (var child in children)
            {
                Rect frame;
                if (useUserPreview)
                {
                    var resolved = AnchorLayoutHelper.ResolveFrame(
                        child.X,
                        child.Y,
                        child.Width,
                        child.Height,
                        baseParentWidth,
                        baseParentHeight,
                        actualParentWidth,
                        actualParentHeight,
                        child.AnchorLeft,
                        child.AnchorTop,
                        child.AnchorRight,
                        child.AnchorBottom);
                    frame = new Rect(resolved.X, resolved.Y, resolved.Width, resolved.Height);
                }
                else
                {
                    frame = new Rect(child.X, child.Y, child.Width, child.Height);
                }

                AddRenderedControl(host, child, frame, useUserPreview);
            }

            return;
        }

        var orientation = parent is null ? VM.SurfaceLayoutOrientation : parent.LayoutOrientation;
        var spacing = parent is null ? VM.SurfaceLayoutSpacing : parent.LayoutSpacing;
        var columns = parent is null ? VM.SurfaceLayoutColumns : parent.Columns;
        var rows = parent is null ? VM.SurfaceLayoutRows : parent.Rows;
        var padding = parent?.Padding ?? 0;

        var snapshots = children
            .Select(child => new LayoutArrangementHelper.ChildSnapshot(
                child.Id,
                child.Width,
                child.Height,
                child.GridRow,
                child.GridColumn,
                child.GridRowSpan,
                child.GridColumnSpan,
                child.StackOrder))
            .ToList();
        var frames = LayoutArrangementHelper.ArrangeChildren(
            layoutMode,
            orientation,
            spacing,
            columns,
            rows,
            padding,
            actualParentWidth,
            actualParentHeight,
            snapshots)
            .ToDictionary(frame => frame.Id, StringComparer.Ordinal);

        foreach (var child in children)
        {
            if (!frames.TryGetValue(child.Id, out var frame))
                continue;

            AddRenderedControl(host, child, new Rect(frame.X, frame.Y, frame.Width, frame.Height), useUserPreview);
        }
    }

    private void AddRenderedControl(Canvas host, DesignControlModel model, Rect frame, bool useUserPreview)
    {
        var wrapper = CreateDesignerWrapper(model, frame.Width, frame.Height);
        _wrapperByControlId[model.Id] = wrapper;
        Canvas.SetLeft(wrapper, frame.X);
        Canvas.SetTop(wrapper, frame.Y);
        host.Children.Add(wrapper);
    }

    private static DesignControlModel CreateRenderModel(DesignControlModel model, double renderedWidth, double renderedHeight)
    {
        var clone = model.Clone();
        clone.Id = model.Id;
        clone.Width = renderedWidth;
        clone.Height = renderedHeight;
        return clone;
    }

    private Control CreateEmptyStateCard()
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F8FAFC")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Width = 320,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Перетащите элементы сюда",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#0F172A"))
                    },
                    new TextBlock
                    {
                        Text = "Собирайте форму визуально, меняйте размеры, а справа настраивайте текст, цвета, шрифты, изображения и привязку DataGrid.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#475569"))
                    }
                }
            },
            IsHitTestVisible = false
        };

        Canvas.SetLeft(card, 24);
        Canvas.SetTop(card, 24);
        return card;
    }

    private void RenderGridOverlay()
    {
        // Сетка нужна только как визуальная опора при компоновке.
        // Сами контролы живут на отдельном Canvas поверх нее.
        GridOverlayCanvas.Children.Clear();

        if (_attachedViewModel is null || VM.IsUserPreviewMode || !VM.IsDesignerGridVisible)
            return;

        var step = Math.Max(10, VM.SnapStep);
        var minorBrush = ParseBrush(VM.SurfaceGridMinorColor, "#DCE4EE");
        var majorBrush = ParseBrush(VM.SurfaceGridMajorColor, "#B7C7DA");
        var surfaceWidth = VM.PreviewFormWidth;
        var surfaceHeight = VM.PreviewFormHeight;

        var verticalIndex = 1;
        for (double x = step; x < surfaceWidth; x += step, verticalIndex++)
        {
            GridOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, surfaceHeight),
                Stroke = verticalIndex % 5 == 0 ? majorBrush : minorBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }

        var horizontalIndex = 1;
        for (double y = step; y < surfaceHeight; y += step, horizontalIndex++)
        {
            GridOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(surfaceWidth, y),
                Stroke = horizontalIndex % 5 == 0 ? majorBrush : minorBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }

        RenderLayoutGridOverlay(0, 0, surfaceWidth, surfaceHeight, VM.SurfaceLayoutMode, VM.SurfaceLayoutColumns, VM.SurfaceLayoutRows);

        foreach (var container in VM.Controls.Where(control => control.ShowGridLines && VM.GetLayoutModeForControl(control) == DesignerLayoutModes.Grid))
        {
            if (!_wrapperByControlId.TryGetValue(container.Id, out var wrapper))
                continue;
            var origin = wrapper.TranslatePoint(new Point(0, 0), GridOverlayCanvas);
            if (origin is null)
                continue;

            RenderLayoutGridOverlay(
                origin.Value.X,
                origin.Value.Y,
                Math.Max(1, wrapper.Width),
                Math.Max(1, wrapper.Height),
                VM.GetLayoutModeForControl(container),
                container.Columns,
                container.Rows);
        }
    }

    private void RenderLayoutGridOverlay(
        double offsetX,
        double offsetY,
        double width,
        double height,
        string layoutMode,
        int columns,
        int rows)
    {
        if (DesignerLayoutModes.NormalizeMode(layoutMode) != DesignerLayoutModes.Grid)
            return;

        var normalizedColumns = Math.Max(1, columns);
        var normalizedRows = Math.Max(1, rows);
        var brush = new SolidColorBrush(Color.FromArgb(150, 37, 99, 235));

        for (var column = 1; column < normalizedColumns; column++)
        {
            var x = offsetX + (width / normalizedColumns * column);
            GridOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, offsetY),
                EndPoint = new Point(x, offsetY + height),
                Stroke = brush,
                StrokeThickness = 1.4,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 },
                IsHitTestVisible = false
            });
        }

        for (var row = 1; row < normalizedRows; row++)
        {
            var y = offsetY + (height / normalizedRows * row);
            GridOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(offsetX, y),
                EndPoint = new Point(offsetX + width, y),
                Stroke = brush,
                StrokeThickness = 1.4,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 },
                IsHitTestVisible = false
            });
        }
    }

    private void ClearGuideOverlay()
    {
        _highlightedContainerId = "";
        GuideOverlayCanvas.Children.Clear();
    }

    private void ClearSelectionOverlay()
    {
        SelectionOverlayCanvas.Children.Clear();
    }

    private void RenderIdleSelectionOverlay()
    {
        ClearSelectionOverlay();

        if (_attachedViewModel is null || VM.IsUserPreviewMode || !VM.HasMultipleSelection)
            return;

        var selectedRoots = VM.GetVisibleEditableSelectedRootControls();
        if (selectedRoots.Count <= 1)
            return;

        RenderSelectionBounds(selectedRoots, SelectionOverlayCanvas, includeToolbar: VM.IsSelectionToolbarEnabled);
    }

    private void RenderContainerHighlight(DesignControlModel? container)
    {
        _highlightedContainerId = container?.Id ?? "";

        if (container is null)
            return;

        // Во время drag подсвечиваем контейнер, в который сейчас потенциально упадет контрол.
        var position = VM.GetAbsolutePosition(container);
        GuideOverlayCanvas.Children.Add(new Rectangle
        {
            Width = Math.Max(0, container.Width),
            Height = Math.Max(0, container.Height),
            Stroke = new SolidColorBrush(Color.Parse("#F59E0B")),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 6, 4 },
            Fill = new SolidColorBrush(Color.FromArgb(18, 245, 158, 11)),
            IsHitTestVisible = false
        });

        if (GuideOverlayCanvas.Children[^1] is Rectangle highlight)
        {
            Canvas.SetLeft(highlight, position.X);
            Canvas.SetTop(highlight, position.Y);
        }

        var label = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFFBEB")),
            BorderBrush = new SolidColorBrush(Color.Parse("#F59E0B")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 3),
            Child = new TextBlock
            {
                Text = $"Drop into {container.Name}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#92400E"))
            },
            IsHitTestVisible = false
        };

        Canvas.SetLeft(label, Math.Max(0, position.X + 8));
        Canvas.SetTop(label, Math.Max(0, position.Y - 24));
        GuideOverlayCanvas.Children.Add(label);
    }

    private void RenderContainerPaddingHighlight(DesignControlModel? container)
    {
        if (container is null || container.Padding <= 0)
            return;

        var innerBounds = GetContainerInnerBounds(container);
        if (innerBounds.Width <= 1 || innerBounds.Height <= 1)
            return;

        var paddingRect = new Rectangle
        {
            Width = innerBounds.Width,
            Height = innerBounds.Height,
            Stroke = new SolidColorBrush(Color.Parse("#38BDF8")),
            StrokeThickness = 1.5,
            StrokeDashArray = new AvaloniaList<double> { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(10, 56, 189, 248)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(paddingRect, innerBounds.X);
        Canvas.SetTop(paddingRect, innerBounds.Y);
        GuideOverlayCanvas.Children.Add(paddingRect);
    }

    private void DrawGuideLine(bool isVertical, double coordinate)
    {
        // Линия рисуется на отдельном overlay-слое и служит только подсказкой выравнивания.
        var surfaceWidth = VM.PreviewFormWidth;
        var surfaceHeight = VM.PreviewFormHeight;
        GuideOverlayCanvas.Children.Add(new Line
        {
            StartPoint = isVertical ? new Point(coordinate, 0) : new Point(0, coordinate),
            EndPoint = isVertical ? new Point(coordinate, surfaceHeight) : new Point(surfaceWidth, coordinate),
            Stroke = new SolidColorBrush(Color.Parse("#EC4899")),
            StrokeThickness = 2,
            IsHitTestVisible = false
        });
    }

    private static Rect CreateSelectionRect(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        return new Rect(x, y, width, height);
    }

    private void RenderSelectionMarquee(Rect selectionRect)
    {
        ClearGuideOverlay();

        if (selectionRect.Width < 1 || selectionRect.Height < 1)
            return;

        var marquee = new Rectangle
        {
            Width = selectionRect.Width,
            Height = selectionRect.Height,
            Stroke = new SolidColorBrush(Color.Parse("#2563EB")),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 5, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(36, 37, 99, 235)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(marquee, selectionRect.X);
        Canvas.SetTop(marquee, selectionRect.Y);
        GuideOverlayCanvas.Children.Add(marquee);
    }

    private void RenderSelectionBounds(
        IEnumerable<DesignControlModel> controls,
        Canvas? host = null,
        bool includeToolbar = false)
    {
        host ??= GuideOverlayCanvas;
        var bounds = controls
            .Select(GetAbsoluteBounds)
            .ToList();

        if (bounds.Count == 0)
            return;

        var left = bounds.Min(bound => bound.X);
        var top = bounds.Min(bound => bound.Y);
        var right = bounds.Max(bound => bound.Right);
        var bottom = bounds.Max(bound => bound.Bottom);

        var outline = new Rectangle
        {
            Width = Math.Max(0, right - left),
            Height = Math.Max(0, bottom - top),
            Stroke = new SolidColorBrush(Color.Parse("#2563EB")),
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 8, 4 },
            Fill = new SolidColorBrush(Color.FromArgb(12, 37, 99, 235)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(outline, left);
        Canvas.SetTop(outline, top);
        host.Children.Add(outline);

        if (includeToolbar)
            RenderSelectionToolbar(host, new Rect(left, top, right - left, bottom - top));
    }

    private void RenderSelectionToolbar(Canvas host, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var toolbar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0F172A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#1E3A8A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 18,
                OffsetY = 6,
                Color = Color.FromArgb(34, 15, 23, 42)
            }),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    CreateSelectionToolbarButton("L", VM.AlignSelectionLeftCommand, "Align left"),
                    CreateSelectionToolbarButton("T", VM.AlignSelectionTopCommand, "Align top"),
                    CreateSelectionToolbarButton("R", VM.AlignSelectionRightCommand, "Align right"),
                    CreateSelectionToolbarButton("B", VM.AlignSelectionBottomCommand, "Align bottom"),
                    CreateSelectionToolbarButton("CX", VM.AlignSelectionCenterCommand, "Align center"),
                    CreateSelectionToolbarButton("CY", VM.AlignSelectionMiddleCommand, "Align middle"),
                    CreateSelectionToolbarButton("DH", VM.DistributeSelectionHorizontalCommand, "Distribute horizontal"),
                    CreateSelectionToolbarButton("DV", VM.DistributeSelectionVerticalCommand, "Distribute vertical"),
                    CreateSelectionToolbarButton("W", VM.MatchSelectionWidthCommand, "Same width"),
                    CreateSelectionToolbarButton("H", VM.MatchSelectionHeightCommand, "Same height")
                }
            }
        };

        toolbar.PointerPressed += (_, e) => e.Handled = true;
        toolbar.PointerMoved += (_, e) => e.Handled = true;
        toolbar.PointerReleased += (_, e) => e.Handled = true;

        Canvas.SetLeft(toolbar, Math.Clamp(bounds.X, 0, Math.Max(0, VM.PreviewFormWidth - 300)));
        Canvas.SetTop(toolbar, Math.Max(0, bounds.Y - 44));
        host.Children.Add(toolbar);
    }

    private static Button CreateSelectionToolbarButton(object content, System.Windows.Input.ICommand command, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            Command = command,
            Width = 30,
            Height = 28,
            Padding = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.Parse("#1E293B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#334155")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private static Button CreateSelectionToolbarButton(string text, System.Windows.Input.ICommand command, string tooltip)
    {
        return CreateSelectionToolbarButton(new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, command, tooltip);
    }

    private IReadOnlyList<DesignControlModel> GetControlsInSelection(Rect selectionRect)
    {
        var hitControls = VM.Controls
            .Where(control => control.IsVisible)
            .Where(control => !VM.IgnoreLockedDuringSelection || !control.IsLocked)
            .Where(control => RectanglesIntersect(selectionRect, GetAbsoluteBounds(control)))
            .OrderBy(control => GetAbsoluteBounds(control).Y)
            .ThenBy(control => GetAbsoluteBounds(control).X)
            .ToList();

        return hitControls
            .Where(control => !hitControls.Any(other => other.Id != control.Id && IsDescendantOf(control, other)))
            .ToList();
    }

    private Rect GetAbsoluteBounds(DesignControlModel control)
    {
        if (_wrapperByControlId.TryGetValue(control.Id, out var wrapper))
        {
            var translated = wrapper.TranslatePoint(default, DesignerCanvas);
            if (translated.HasValue)
            {
                var width = wrapper.Bounds.Width > 0 ? wrapper.Bounds.Width : control.Width;
                var height = wrapper.Bounds.Height > 0 ? wrapper.Bounds.Height : control.Height;
                return new Rect(translated.Value.X, translated.Value.Y, Math.Max(0, width), Math.Max(0, height));
            }
        }

        var position = VM.GetAbsolutePosition(control);
        return new Rect(position.X, position.Y, Math.Max(0, control.Width), Math.Max(0, control.Height));
    }

    private bool IsDescendantOf(DesignControlModel control, DesignControlModel ancestor)
    {
        var parent = VM.GetControl(control.ParentId);

        while (parent is not null)
        {
            if (parent.Id == ancestor.Id)
                return true;

            parent = VM.GetControl(parent.ParentId);
        }

        return false;
    }

    private void BuildSnapCandidateSnapshot(IEnumerable<DesignControlModel> excludedControls)
    {
        var excludedIds = excludedControls
            .Select(control => control.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _snapCandidates.Clear();
        foreach (var control in VM.Controls)
        {
            if (excludedIds.Contains(control.Id) || !control.IsVisible)
                continue;

            _snapCandidates.Add(new CanvasSnapCandidate(
                control.Id,
                control.ParentId ?? string.Empty,
                GetAbsoluteBounds(control)));
        }
    }

    private void ClearSnapCandidateSnapshot()
    {
        _snapCandidates.Clear();
    }

    private static bool RectanglesIntersect(Rect first, Rect second)
    {
        return first.X < second.Right
            && first.Right > second.X
            && first.Y < second.Bottom
            && first.Bottom > second.Y;
    }

    private double ActiveSnapThreshold => Math.Clamp(VM.SnapThreshold, 1, 40);

    private static bool IsSnapBypassed(KeyModifiers keyModifiers)
    {
        return keyModifiers.HasFlag(KeyModifiers.Alt);
    }

    private double ApplyGridSnap(double value, KeyModifiers keyModifiers)
    {
        return !VM.IsCanvasSnappingEnabled || IsSnapBypassed(keyModifiers) ? value : VM.Snap(value);
    }

    private bool ShouldUseControlSnap(KeyModifiers keyModifiers)
    {
        return VM.IsCanvasSnappingEnabled && VM.IsControlSnapEnabled && !IsSnapBypassed(keyModifiers);
    }

    private void UpdateDragGuides(DesignControlModel active)
    {
        ClearGuideOverlay();

        // Ищем кандидатов только среди соседей в том же контейнере:
        // тогда линии выравнивания не "прилипают" к элементам из других уровней вложенности.
        var activeAbsolute = VM.GetAbsolutePosition(active);
        var targetContainer = VM.FindDeepestContainerAt(activeAbsolute.X + active.Width / 2, activeAbsolute.Y + active.Height / 2);
        if (targetContainer?.Id == active.Id || (targetContainer is not null && IsDescendantOf(targetContainer, active)))
            targetContainer = VM.GetControl(targetContainer.ParentId);

        var alignmentContainer = targetContainer ?? VM.GetControl(active.ParentId);
        RenderContainerHighlight(alignmentContainer);
        RenderContainerPaddingHighlight(alignmentContainer);

        var alignmentParentId = alignmentContainer?.Id ?? string.Empty;
        if (_snapCandidates.Count == 0)
            BuildSnapCandidateSnapshot(new[] { active });

        var candidates = _snapCandidates
            .Where(candidate => candidate.Id != active.Id)
            .Where(candidate => string.Equals(candidate.ParentId, alignmentParentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var activeLeft = activeAbsolute.X;
        var activeCenter = activeAbsolute.X + active.Width / 2;
        var activeRight = activeAbsolute.X + active.Width;
        var activeTop = activeAbsolute.Y;
        var activeMiddle = activeAbsolute.Y + active.Height / 2;
        var activeBottom = activeAbsolute.Y + active.Height;

        var bestVertical = ChooseBestAlignment(
            FindBestAlignment(activeLeft, activeCenter, activeRight, candidates, vertical: true),
            FindBestContainerAlignment(activeLeft, activeCenter, activeRight, alignmentContainer, vertical: true));
        var bestHorizontal = ChooseBestAlignment(
            FindBestAlignment(activeTop, activeMiddle, activeBottom, candidates, vertical: false),
            FindBestContainerAlignment(activeTop, activeMiddle, activeBottom, alignmentContainer, vertical: false));

        if (bestVertical.HasValue)
        {
            active.X += bestVertical.Value.Offset;
            if (VM.IsSmartGuidesEnabled)
                DrawGuideLine(true, bestVertical.Value.TargetCoordinate);
        }

        if (bestHorizontal.HasValue)
        {
            active.Y += bestHorizontal.Value.Offset;
            if (VM.IsSmartGuidesEnabled)
                DrawGuideLine(false, bestHorizontal.Value.TargetCoordinate);
        }

        VM.ClampControlToSurface(active);

        var adjustedAbsolute = VM.GetAbsolutePosition(active);
        var activeBounds = new Rect(adjustedAbsolute.X, adjustedAbsolute.Y, Math.Max(0, active.Width), Math.Max(0, active.Height));
        if (VM.IsDistanceHintsEnabled)
            RenderSpacingGuides(activeBounds, alignmentContainer, candidates.Select(candidate => candidate.Bounds).ToList());
    }

    private void UpdateResizeGuides(DesignControlModel active)
    {
        ClearGuideOverlay();

        var activeAbsolute = VM.GetAbsolutePosition(active);
        var right = activeAbsolute.X + active.Width;
        var bottom = activeAbsolute.Y + active.Height;
        var container = VM.GetControl(active.ParentId);
        if (_snapCandidates.Count == 0)
            BuildSnapCandidateSnapshot(new[] { active });

        var candidates = _snapCandidates
            .Where(candidate => candidate.Id != active.Id)
            .Where(candidate => string.Equals(candidate.ParentId, active.ParentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToList();

        RenderContainerHighlight(container);
        RenderContainerPaddingHighlight(container);

        var bestVertical = ChooseBestAlignment(
            FindBestResizeAlignment(right, candidates, vertical: true),
            FindBestContainerResizeAlignment(right, container, vertical: true));
        var bestHorizontal = ChooseBestAlignment(
            FindBestResizeAlignment(bottom, candidates, vertical: false),
            FindBestContainerResizeAlignment(bottom, container, vertical: false));

        if (bestVertical.HasValue)
        {
            active.Width += bestVertical.Value.Offset;
            if (VM.IsSmartGuidesEnabled)
                DrawGuideLine(true, bestVertical.Value.TargetCoordinate);
        }

        if (bestHorizontal.HasValue)
        {
            active.Height += bestHorizontal.Value.Offset;
            if (VM.IsSmartGuidesEnabled)
                DrawGuideLine(false, bestHorizontal.Value.TargetCoordinate);
        }

        VM.ClampControlToSurface(active);
        if (VM.IsDistanceHintsEnabled)
            RenderResizeSizeHint(active);
    }

    private (double Offset, double TargetCoordinate)? FindBestAlignment(
        double start,
        double center,
        double end,
        IReadOnlyList<CanvasSnapCandidate> candidates,
        bool vertical)
    {
        (double Offset, double TargetCoordinate)? best = null;

        foreach (var candidate in candidates)
        {
            var startCandidate = vertical ? candidate.Bounds.X : candidate.Bounds.Y;
            var sizeCandidate = vertical ? candidate.Bounds.Width : candidate.Bounds.Height;
            var centerCandidate = startCandidate + sizeCandidate / 2;
            var endCandidate = startCandidate + sizeCandidate;

            EvaluateAlignment(start, startCandidate);
            EvaluateAlignment(center, centerCandidate);
            EvaluateAlignment(end, endCandidate);
        }

        return best;

        void EvaluateAlignment(double sourceCoordinate, double targetCoordinate)
        {
            var delta = targetCoordinate - sourceCoordinate;
            if (Math.Abs(delta) > ActiveSnapThreshold)
                return;

            if (!best.HasValue || Math.Abs(delta) < Math.Abs(best.Value.Offset))
                best = (delta, targetCoordinate);
        }
    }

    private (double Offset, double TargetCoordinate)? FindBestResizeAlignment(
        double edgeCoordinate,
        IReadOnlyList<CanvasSnapCandidate> candidates,
        bool vertical)
    {
        (double Offset, double TargetCoordinate)? best = null;

        foreach (var candidate in candidates)
        {
            var startCandidate = vertical ? candidate.Bounds.X : candidate.Bounds.Y;
            var sizeCandidate = vertical ? candidate.Bounds.Width : candidate.Bounds.Height;
            var centerCandidate = startCandidate + sizeCandidate / 2;
            var endCandidate = startCandidate + sizeCandidate;

            EvaluateAlignment(edgeCoordinate, startCandidate);
            EvaluateAlignment(edgeCoordinate, centerCandidate);
            EvaluateAlignment(edgeCoordinate, endCandidate);
        }

        return best;

        void EvaluateAlignment(double sourceCoordinate, double targetCoordinate)
        {
            var delta = targetCoordinate - sourceCoordinate;
            if (Math.Abs(delta) > ActiveSnapThreshold)
                return;

            if (!best.HasValue || Math.Abs(delta) < Math.Abs(best.Value.Offset))
                best = (delta, targetCoordinate);
        }
    }

    private static (double Offset, double TargetCoordinate)? ChooseBestAlignment(
        (double Offset, double TargetCoordinate)? first,
        (double Offset, double TargetCoordinate)? second)
    {
        if (!first.HasValue)
            return second;

        if (!second.HasValue)
            return first;

        return Math.Abs(first.Value.Offset) <= Math.Abs(second.Value.Offset)
            ? first
            : second;
    }

    private (double Offset, double TargetCoordinate)? FindBestContainerAlignment(
        double start,
        double center,
        double end,
        DesignControlModel? container,
        bool vertical)
    {
        var bounds = GetContainerInnerBounds(container);
        var targetStart = vertical ? bounds.X : bounds.Y;
        var targetCenter = vertical ? bounds.X + (bounds.Width / 2) : bounds.Y + (bounds.Height / 2);
        var targetEnd = vertical ? bounds.Right : bounds.Bottom;
        (double Offset, double TargetCoordinate)? best = null;

        EvaluateAlignment(start, targetStart);
        EvaluateAlignment(center, targetCenter);
        EvaluateAlignment(end, targetEnd);

        return best;

        void EvaluateAlignment(double sourceCoordinate, double targetCoordinate)
        {
            var delta = targetCoordinate - sourceCoordinate;
            if (Math.Abs(delta) > ActiveSnapThreshold)
                return;

            if (!best.HasValue || Math.Abs(delta) < Math.Abs(best.Value.Offset))
                best = (delta, targetCoordinate);
        }
    }

    private (double Offset, double TargetCoordinate)? FindBestContainerResizeAlignment(
        double edgeCoordinate,
        DesignControlModel? container,
        bool vertical)
    {
        var bounds = GetContainerInnerBounds(container);
        var targetStart = vertical ? bounds.X : bounds.Y;
        var targetCenter = vertical ? bounds.X + (bounds.Width / 2) : bounds.Y + (bounds.Height / 2);
        var targetEnd = vertical ? bounds.Right : bounds.Bottom;
        (double Offset, double TargetCoordinate)? best = null;

        EvaluateAlignment(edgeCoordinate, targetStart);
        EvaluateAlignment(edgeCoordinate, targetCenter);
        EvaluateAlignment(edgeCoordinate, targetEnd);

        return best;

        void EvaluateAlignment(double sourceCoordinate, double targetCoordinate)
        {
            var delta = targetCoordinate - sourceCoordinate;
            if (Math.Abs(delta) > ActiveSnapThreshold)
                return;

            if (!best.HasValue || Math.Abs(delta) < Math.Abs(best.Value.Offset))
                best = (delta, targetCoordinate);
        }
    }

    private Rect GetContainerInnerBounds(DesignControlModel? container)
    {
        if (container is null)
            return new Rect(0, 0, Math.Max(0, VM.PreviewFormWidth), Math.Max(0, VM.PreviewFormHeight));

        var position = VM.GetAbsolutePosition(container);
        var inset = Math.Max(0, container.Padding);
        var width = Math.Max(0, container.Width - (inset * 2));
        var height = Math.Max(0, container.Height - (inset * 2));

        if (width <= 1 || height <= 1)
            return new Rect(position.X, position.Y, Math.Max(0, container.Width), Math.Max(0, container.Height));

        return new Rect(position.X + inset, position.Y + inset, width, height);
    }

    private void RenderSpacingGuides(Rect activeBounds, DesignControlModel? container, IReadOnlyList<Rect> candidateBounds)
    {
        var containerBounds = GetContainerInnerBounds(container);

        var left = FindNearestHorizontalSpacing(activeBounds, containerBounds, candidateBounds, searchLeft: true);
        var right = FindNearestHorizontalSpacing(activeBounds, containerBounds, candidateBounds, searchLeft: false);
        var top = FindNearestVerticalSpacing(activeBounds, containerBounds, candidateBounds, searchTop: true);
        var bottom = FindNearestVerticalSpacing(activeBounds, containerBounds, candidateBounds, searchTop: false);

        DrawDistanceGuide(left);
        DrawDistanceGuide(right);
        DrawDistanceGuide(top);
        DrawDistanceGuide(bottom);
        DrawEqualSpacingBadge(left, right);
        DrawEqualSpacingBadge(top, bottom);
    }

    private (bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? FindNearestHorizontalSpacing(
        Rect activeBounds,
        Rect containerBounds,
        IReadOnlyList<Rect> candidateBounds,
        bool searchLeft)
    {
        (bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? best = null;

        var containerCross = containerBounds.Height > 16
            ? Math.Clamp(activeBounds.Y + (activeBounds.Height / 2), containerBounds.Y + 8, containerBounds.Bottom - 8)
            : activeBounds.Y + (activeBounds.Height / 2);
        if (searchLeft)
        {
            var distance = activeBounds.X - containerBounds.X;
            Consider(containerBounds.X, activeBounds.X, containerCross, distance, isContainerReference: true);
        }
        else
        {
            var distance = containerBounds.Right - activeBounds.Right;
            Consider(activeBounds.Right, containerBounds.Right, containerCross, distance, isContainerReference: true);
        }

        foreach (var candidate in candidateBounds)
        {
            var overlapStart = Math.Max(activeBounds.Y, candidate.Y);
            var overlapEnd = Math.Min(activeBounds.Bottom, candidate.Bottom);
            if (overlapEnd - overlapStart < 1)
                continue;

            var cross = (overlapStart + overlapEnd) / 2;
            if (searchLeft && candidate.Right <= activeBounds.X)
                Consider(candidate.Right, activeBounds.X, cross, activeBounds.X - candidate.Right, isContainerReference: false);
            else if (!searchLeft && candidate.X >= activeBounds.Right)
                Consider(activeBounds.Right, candidate.X, cross, candidate.X - activeBounds.Right, isContainerReference: false);
        }

        return best;

        void Consider(double start, double end, double cross, double distance, bool isContainerReference)
        {
            if (distance <= 0 || distance > SmartMeasurementMaxDistance)
                return;

            if (!best.HasValue || distance < best.Value.Distance)
                best = (true, start, end, cross, distance, isContainerReference);
        }
    }

    private (bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? FindNearestVerticalSpacing(
        Rect activeBounds,
        Rect containerBounds,
        IReadOnlyList<Rect> candidateBounds,
        bool searchTop)
    {
        (bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? best = null;

        var containerCross = containerBounds.Width > 16
            ? Math.Clamp(activeBounds.X + (activeBounds.Width / 2), containerBounds.X + 8, containerBounds.Right - 8)
            : activeBounds.X + (activeBounds.Width / 2);
        if (searchTop)
        {
            var distance = activeBounds.Y - containerBounds.Y;
            Consider(containerBounds.Y, activeBounds.Y, containerCross, distance, isContainerReference: true);
        }
        else
        {
            var distance = containerBounds.Bottom - activeBounds.Bottom;
            Consider(activeBounds.Bottom, containerBounds.Bottom, containerCross, distance, isContainerReference: true);
        }

        foreach (var candidate in candidateBounds)
        {
            var overlapStart = Math.Max(activeBounds.X, candidate.X);
            var overlapEnd = Math.Min(activeBounds.Right, candidate.Right);
            if (overlapEnd - overlapStart < 1)
                continue;

            var cross = (overlapStart + overlapEnd) / 2;
            if (searchTop && candidate.Bottom <= activeBounds.Y)
                Consider(candidate.Bottom, activeBounds.Y, cross, activeBounds.Y - candidate.Bottom, isContainerReference: false);
            else if (!searchTop && candidate.Y >= activeBounds.Bottom)
                Consider(activeBounds.Bottom, candidate.Y, cross, candidate.Y - activeBounds.Bottom, isContainerReference: false);
        }

        return best;

        void Consider(double start, double end, double cross, double distance, bool isContainerReference)
        {
            if (distance <= 0 || distance > SmartMeasurementMaxDistance)
                return;

            if (!best.HasValue || distance < best.Value.Distance)
                best = (false, start, end, cross, distance, isContainerReference);
        }
    }

    private void DrawDistanceGuide((bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? guide)
    {
        if (!guide.HasValue)
            return;

        var value = guide.Value;
        var guideBrush = new SolidColorBrush(Color.Parse(value.IsContainerReference ? "#0EA5E9" : "#2563EB"));
        var labelBackground = new SolidColorBrush(Color.Parse(value.IsContainerReference ? "#E0F2FE" : "#DBEAFE"));
        var labelForeground = new SolidColorBrush(Color.Parse(value.IsContainerReference ? "#0C4A6E" : "#1D4ED8"));
        var text = Math.Round(value.Distance).ToString(CultureInfo.InvariantCulture);

        if (value.IsHorizontal)
        {
            GuideOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(value.Start, value.Cross),
                EndPoint = new Point(value.End, value.Cross),
                Stroke = guideBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });

            GuideOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(value.Start, value.Cross - SmartMeasurementTickSize),
                EndPoint = new Point(value.Start, value.Cross + SmartMeasurementTickSize),
                Stroke = guideBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });

            GuideOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(value.End, value.Cross - SmartMeasurementTickSize),
                EndPoint = new Point(value.End, value.Cross + SmartMeasurementTickSize),
                Stroke = guideBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });

            AddDistanceLabel(text, labelBackground, labelForeground, ((value.Start + value.End) / 2) - 14, value.Cross - 14);
        }
        else
        {
            GuideOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(value.Cross, value.Start),
                EndPoint = new Point(value.Cross, value.End),
                Stroke = guideBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });

            GuideOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(value.Cross - SmartMeasurementTickSize, value.Start),
                EndPoint = new Point(value.Cross + SmartMeasurementTickSize, value.Start),
                Stroke = guideBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });

            GuideOverlayCanvas.Children.Add(new Line
            {
                StartPoint = new Point(value.Cross - SmartMeasurementTickSize, value.End),
                EndPoint = new Point(value.Cross + SmartMeasurementTickSize, value.End),
                Stroke = guideBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });

            AddDistanceLabel(text, labelBackground, labelForeground, value.Cross + 8, ((value.Start + value.End) / 2) - 12);
        }
    }

    private void DrawEqualSpacingBadge(
        (bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? first,
        (bool IsHorizontal, double Start, double End, double Cross, double Distance, bool IsContainerReference)? second)
    {
        if (!first.HasValue || !second.HasValue)
            return;

        var a = first.Value;
        var b = second.Value;
        if (a.IsHorizontal != b.IsHorizontal)
            return;

        if (Math.Abs(a.Distance - b.Distance) > ActiveSnapThreshold)
            return;

        var background = new SolidColorBrush(Color.Parse("#DCFCE7"));
        var foreground = new SolidColorBrush(Color.Parse("#166534"));
        var x = a.IsHorizontal
            ? ((a.Start + a.End + b.Start + b.End) / 4) - 22
            : Math.Max(a.Cross, b.Cross) + 12;
        var y = a.IsHorizontal
            ? Math.Min(a.Cross, b.Cross) - 32
            : ((a.Start + a.End + b.Start + b.End) / 4) - 12;

        AddDistanceLabel($"= {Math.Round((a.Distance + b.Distance) / 2).ToString(CultureInfo.InvariantCulture)}", background, foreground, x, y);
    }

    private void AddDistanceLabel(string text, IBrush background, IBrush foreground, double x, double y)
    {
        var label = new Border
        {
            Background = background,
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = foreground
            },
            IsHitTestVisible = false
        };

        Canvas.SetLeft(label, Math.Max(0, x));
        Canvas.SetTop(label, Math.Max(0, y));
        GuideOverlayCanvas.Children.Add(label);
    }

    private void RenderResizeSizeHint(DesignControlModel active)
    {
        var bounds = GetAbsoluteBounds(active);
        var background = new SolidColorBrush(Color.Parse("#0F172A"));
        var foreground = Brushes.White;
        var text = $"{Math.Round(active.Width).ToString(CultureInfo.InvariantCulture)} x {Math.Round(active.Height).ToString(CultureInfo.InvariantCulture)}";
        AddDistanceLabel(text, background, foreground, bounds.Right + 8, bounds.Bottom + 8);
    }

    private Border CreateDesignerWrapper(DesignControlModel model, double renderedWidth, double renderedHeight)
    {
        // У каждого элемента на форме есть не только его визуальное превью,
        // но и служебная обертка: рамка выделения, маркер размера, child-host и обработчики мыши.
        var isSelected = VM.IsControlSelected(model);
        var isPrimary = VM.SelectedControl?.Id == model.Id;
        var isUserPreviewMode = VM.IsUserPreviewMode;
        var renderModel = CreateRenderModel(model, renderedWidth, renderedHeight);
        var preview = CreatePreviewControl(renderModel);

        var root = new Canvas
        {
            Width = renderedWidth,
            Height = renderedHeight,
            ClipToBounds = false
        };

        root.Children.Add(preview);
        Canvas.SetLeft(preview, 0);
        Canvas.SetTop(preview, 0);

        if (VM.CanHostChildren(model))
        {
            var childHost = new Canvas
            {
                Width = renderedWidth,
                Height = renderedHeight,
                Background = Brushes.Transparent,
                ClipToBounds = true
            };

            AddControlsToCanvas(
                childHost,
                model,
                model.Width,
                model.Height,
                renderedWidth,
                renderedHeight,
                useUserPreview: isUserPreviewMode);

            root.Children.Add(childHost);
            Canvas.SetLeft(childHost, 0);
            Canvas.SetTop(childHost, 0);
        }

        if (!isUserPreviewMode)
        {
            var label = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0F172A")),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 3),
                Child = new TextBlock
                {
                    Text = model.Name,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                },
                IsHitTestVisible = false
            };

            Canvas.SetLeft(label, 8);
            Canvas.SetTop(label, 8);
            root.Children.Add(label);

            var topRightBadgeY = 8d;

            void AddTopRightBadge(string text, string background, double minLeft)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.Parse(background)),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(7, 2.5),
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 10.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    },
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(badge, Math.Max(8, renderedWidth - minLeft));
                Canvas.SetTop(badge, topRightBadgeY);
                root.Children.Add(badge);
                topRightBadgeY += 26;
            }

            if (!model.IsVisible)
                AddTopRightBadge("Скрыт", "#B45309", 72);

            if (model.IsLocked)
                AddTopRightBadge("Locked", "#334155", 82);

            if (isSelected && VM.HasMultipleSelection)
                AddTopRightBadge(isPrimary ? "Главный" : "Выбран", isPrimary ? "#2563EB" : "#C2410C", isPrimary ? 84 : 92);

            var resizeHitArea = new Border
            {
                Width = 18,
                Height = 18,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.BottomRightCorner),
                Tag = model,
                IsVisible = isPrimary && CanResizeControl(model)
            };

            var resizeVisual = new Border
            {
                Width = 11,
                Height = 11,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#2563EB")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(5),
                IsHitTestVisible = false,
                IsVisible = isPrimary && CanResizeControl(model)
            };

            Canvas.SetLeft(resizeHitArea, Math.Max(0, renderedWidth - 9));
            Canvas.SetTop(resizeHitArea, Math.Max(0, renderedHeight - 9));
            Canvas.SetLeft(resizeVisual, Math.Max(0, renderedWidth - 11));
            Canvas.SetTop(resizeVisual, Math.Max(0, renderedHeight - 11));

            resizeHitArea.PointerPressed += ResizeHandle_PointerPressed;
            resizeHitArea.PointerMoved += ResizeHandle_PointerMoved;
            resizeHitArea.PointerReleased += ResizeHandle_PointerReleased;

            root.Children.Add(resizeHitArea);
            root.Children.Add(resizeVisual);
        }

        var wrapper = new Border
        {
            Width = renderedWidth,
            Height = renderedHeight,
            BorderThickness = isUserPreviewMode
                ? new Thickness(0)
                : isSelected ? new Thickness(isPrimary ? 2 : 1.5) : new Thickness(1),
            BorderBrush = isUserPreviewMode
                ? Brushes.Transparent
                : isSelected
                    ? new SolidColorBrush(Color.Parse(model.IsLocked ? "#0F766E" : isPrimary ? "#2563EB" : "#F97316"))
                    : new SolidColorBrush(Color.Parse(model.IsLocked ? "#64748B" : "#66CBD5E1")),
            Background = Brushes.Transparent,
            Opacity = model.Opacity,
            Tag = model,
            Child = root,
            ContextMenu = isUserPreviewMode ? null : CreateControlContextMenu(model)
        };

        if (!isUserPreviewMode)
        {
            wrapper.PointerPressed += Control_PointerPressed;
            wrapper.PointerMoved += Control_PointerMoved;
            wrapper.PointerReleased += Control_PointerReleased;
        }

        return wrapper;
    }

    private static bool CanResizeControl(DesignControlModel model)
    {
        return !model.IsLocked && model.Type != DesignerControlTypes.Group;
    }

    private Control CreatePreviewControl(DesignControlModel model)
    {
        var descriptor = VM.Registry.GetRequiredControl(model.Type);
        var services = new DesignerServiceProvider()
            .Add<IBuiltInPreviewBridge>(new MainWindowPreviewBridge(this))
            .Add<IPreviewBindingItemsProvider>(new DelegatePreviewBindingItemsProvider(ResolvePreviewBindingItems));
        var context = new DesignerPreviewContext(
            DesignerPreviewMode.Designer,
            services,
            parentId => GetActiveChildControls(parentId)
                .Select(child => (IDesignControlNode)new DesignControlNodeAdapter(child))
                .ToList(),
            BindingMetadataMapper.ToMetadataMap(GetActiveBindingSources()));

        try
        {
            return descriptor.BuildPreview(new DesignControlNodeAdapter(model), context);
        }
        catch
        {
            return CreateMissingPreview(model);
        }
    }

    private Control CreateBuiltInPreviewControl(DesignControlModel model)
    {
        return model.Type switch
        {
            DesignerControlTypes.Group => CreateGroupPreview(model),
            DesignerControlTypes.Button => CreateButtonPreview(model),
            DesignerControlTypes.TextBox => CreateTextBoxPreview(model),
            DesignerControlTypes.TextBlock => CreateTextBlockPreview(model),
            DesignerControlTypes.CheckBox => CreateCheckBoxPreview(model),
            DesignerControlTypes.Border => CreateBorderPreview(model),
            DesignerControlTypes.Image => CreateImagePreview(model),
            DesignerControlTypes.StackLayout => CreateStackLayoutPreview(model),
            DesignerControlTypes.LayoutGrid => CreateGridPreview(model),
            DesignerControlTypes.FlexLayout => CreateFlexLayoutPreview(model),
            DesignerControlTypes.DataGrid => CreateModernDataGridPreview(model),
            _ => new Border
            {
                Width = model.Width,
                Height = model.Height,
                Background = ParseBrush("#F8FAFC", "#F8FAFC"),
                BorderBrush = ParseBrush("#CBD5E1", "#CBD5E1"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = model.Type,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                IsHitTestVisible = false
            }
        };
    }

    private Control CreateMissingPreview(DesignControlModel model)
    {
        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush("#FFF7ED", "#FFF7ED"),
            BorderBrush = ParseBrush("#FB923C", "#FB923C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = $"{model.Type}\nНет доступного preview",
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap
            },
            IsHitTestVisible = false
        };
    }

    private sealed class MainWindowPreviewBridge : IBuiltInPreviewBridge
    {
        private readonly MainWindow _owner;
        private readonly Dictionary<string, Func<DesignControlModel, Control>> _builders;

        public MainWindowPreviewBridge(MainWindow owner)
        {
            _owner = owner;
            _builders = new Dictionary<string, Func<DesignControlModel, Control>>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerControlTypes.Group] = _owner.CreateGroupPreview,
                [DesignerControlTypes.Button] = _owner.CreateButtonPreview,
                [DesignerControlTypes.TextBox] = _owner.CreateTextBoxPreview,
                [DesignerControlTypes.TextBlock] = _owner.CreateTextBlockPreview,
                [DesignerControlTypes.CheckBox] = _owner.CreateCheckBoxPreview,
                [DesignerControlTypes.Border] = _owner.CreateBorderPreview,
                [DesignerControlTypes.Image] = _owner.CreateImagePreview,
                [DesignerControlTypes.StackLayout] = _owner.CreateStackLayoutPreview,
                [DesignerControlTypes.LayoutGrid] = _owner.CreateGridPreview,
                [DesignerControlTypes.FlexLayout] = _owner.CreateFlexLayoutPreview,
                [DesignerControlTypes.DataGrid] = _owner.CreateModernDataGridPreview
            };
        }

        public Control BuildPreview(string typeKey, IDesignControlNode control, IPreviewContext context)
        {
            if (control is not DesignControlNodeAdapter adapter || !_builders.TryGetValue(typeKey, out var builder))
                return _owner.CreateMissingPreview(new DesignControlModel { Type = typeKey, Width = 180, Height = 48 });

            return builder(adapter.Model);
        }
    }

    private Control CreateStackLayoutPreview(DesignControlModel model)
    {
        var direction = DesignerLayoutModes.NormalizeOrientation(model.LayoutOrientation) == DesignerLayoutModes.Horizontal
            ? "Horizontal"
            : "Vertical";
        return CreateLayoutContainerPreview(model, $"Stack • {direction}", "#DBEAFE", "#2563EB");
    }

    private Control CreateFlexLayoutPreview(DesignControlModel model)
    {
        var direction = DesignerLayoutModes.NormalizeOrientation(model.LayoutOrientation) == DesignerLayoutModes.Horizontal
            ? "Wrap by rows"
            : "Wrap by columns";
        return CreateLayoutContainerPreview(model, $"Flex • {direction}", "#DCFCE7", "#16A34A");
    }

    private Control CreateLayoutContainerPreview(DesignControlModel model, string title, string tint, string accent)
    {
        var accentBrush = ParseBrush(accent, accent);
        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, tint),
            BorderBrush = ParseBrush(model.BorderBrush, accent),
            BorderThickness = UniformThickness(Math.Max(1, model.BorderThickness)),
            CornerRadius = UniformCornerRadius(Math.Max(8, model.CornerRadius)),
            Child = new StackPanel
            {
                Margin = UniformThickness(Math.Max(8, model.Padding)),
                Spacing = 14,
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(24, 15, 23, 42)),
                        CornerRadius = new CornerRadius(999),
                        Padding = new Thickness(10, 4),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = new TextBlock
                        {
                            Text = title,
                            Foreground = accentBrush,
                            FontWeight = FontWeight.SemiBold
                        },
                        IsHitTestVisible = false
                    },
                    new Border
                    {
                        BorderBrush = accentBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                        Child = new TextBlock
                        {
                            Text = "Элементы внутри раскладываются автоматически",
                            Margin = new Thickness(12),
                            Foreground = ParseBrush(model.Foreground, "#475569"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        IsHitTestVisible = false
                    }
                }
            },
            IsHitTestVisible = false
        };
    }

    private Control CreateGroupPreview(DesignControlModel model)
    {
        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false
        };
    }

    private Control CreateButtonPreview(DesignControlModel model)
    {
        var text = ResolvePreviewTextValue(model, string.IsNullOrWhiteSpace(model.Text) ? "Кнопка" : model.Text);
        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, "#2563EB"),
            BorderBrush = ParseBrush(model.BorderBrush, "#1D4ED8"),
            BorderThickness = UniformThickness(model.BorderThickness),
            CornerRadius = UniformCornerRadius(model.CornerRadius),
            Padding = UniformThickness(model.Padding),
            Child = new Grid
            {
                Children =
                {
                    CreatePreviewText(
                        text,
                        model,
                        model.Foreground,
                        HorizontalAlignment.Center,
                        VerticalAlignment.Center)
                }
            },
            IsHitTestVisible = false
        };
    }

    private Control CreateTextBoxPreview(DesignControlModel model)
    {
        var hasDesignText = !string.IsNullOrWhiteSpace(model.Text) || !string.IsNullOrWhiteSpace(model.TextBindingPath);
        var text = hasDesignText
            ? ResolvePreviewTextValue(model, string.IsNullOrWhiteSpace(model.Text) ? string.Empty : model.Text)
            : string.IsNullOrWhiteSpace(model.PlaceholderText) ? "TextBox" : model.PlaceholderText;

        var foreground = !hasDesignText
            ? "#94A3B8"
            : model.Foreground;

        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, "#FFFFFF"),
            BorderBrush = ParseBrush(model.BorderBrush, "#94A3B8"),
            BorderThickness = UniformThickness(model.BorderThickness),
            CornerRadius = UniformCornerRadius(model.CornerRadius),
            Padding = UniformThickness(model.Padding),
            Child = CreatePreviewText(
                text,
                model,
                foreground,
                HorizontalAlignment.Left,
                VerticalAlignment.Center),
            IsHitTestVisible = false
        };
    }

    private Control CreateTextBlockPreview(DesignControlModel model)
    {
        var text = ResolvePreviewTextValue(model, string.IsNullOrWhiteSpace(model.Text) ? "Текст" : model.Text);

        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = Brushes.Transparent,
            Child = CreatePreviewText(
                text,
                model,
                model.Foreground,
                HorizontalAlignment.Left,
                VerticalAlignment.Center),
            IsHitTestVisible = false
        };
    }

    private Control CreateCheckBoxPreview(DesignControlModel model)
    {
        var caption = ResolvePreviewTextValue(model, string.IsNullOrWhiteSpace(model.Text) ? "Флажок" : model.Text);

        var layout = new Grid
        {
            Width = model.Width,
            Height = model.Height,
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(6, 0, 6, 0),
            IsHitTestVisible = false
        };

        layout.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            BorderBrush = ParseBrush(model.BorderBrush, "#475569"),
            BorderThickness = UniformThickness(Math.Max(1, model.BorderThickness)),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        });

        var text = CreatePreviewText(
            caption,
            model,
            model.Foreground,
            HorizontalAlignment.Left,
            VerticalAlignment.Center);
        Grid.SetColumn(text, 1);
        layout.Children.Add(text);

        return layout;
    }

    private Control CreateBorderPreview(DesignControlModel model)
    {
        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, "#F8FAFC"),
            BorderBrush = ParseBrush(model.BorderBrush, "#CBD5E1"),
            BorderThickness = UniformThickness(model.BorderThickness),
            CornerRadius = UniformCornerRadius(model.CornerRadius),
            Padding = UniformThickness(model.Padding),
            Child = CreatePreviewText(
                ResolvePreviewTextValue(model, string.IsNullOrWhiteSpace(model.Text) ? "Контейнер" : model.Text),
                model,
                model.Foreground,
                HorizontalAlignment.Left,
                VerticalAlignment.Top),
            IsHitTestVisible = false
        };
    }

    private string ResolvePreviewTextValue(DesignControlModel model, string fallback)
    {
        if (string.IsNullOrWhiteSpace(model.TextBindingPath))
            return fallback;

        var source = GetActiveBindingSource(model.BindingSourceId);
        var field = ResolvePreviewBindingField(source?.Fields, model.TextBindingPath);
        return string.IsNullOrWhiteSpace(field?.SampleValue) ? fallback : field.SampleValue;
    }

    private static BindingFieldModel? ResolvePreviewBindingField(IEnumerable<BindingFieldModel>? fields, string bindingPath)
    {
        if (fields is null || string.IsNullOrWhiteSpace(bindingPath))
            return null;

        foreach (var field in fields)
        {
            var directPath = field.Path?.Trim() ?? string.Empty;
            var sanitizedPath = SanitizeBindingToken(directPath, "Field");
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

    private static string SanitizeBindingToken(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new System.Text.StringBuilder(source.Length + 8);

        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                builder.Append(ch);
            else if (ch is ' ' or '-' or '.')
                builder.Append('_');
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private Control CreateImagePreview(DesignControlModel model)
    {
        Control content;
        var image = TryCreateImageControl(model);

        if (image is not null)
        {
            content = image;
        }
        else
        {
            content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Изображение",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ParseBrush(model.Foreground, "#0F172A"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(model.ImageSource) ? "Укажите путь к файлу или avares URI" : model.ImageSource,
                        Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = Math.Max(80, model.Width - 40),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            };
        }

        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, "#F8FAFC"),
            BorderBrush = ParseBrush(model.BorderBrush, "#CBD5E1"),
            BorderThickness = UniformThickness(model.BorderThickness),
            CornerRadius = UniformCornerRadius(model.CornerRadius),
            ClipToBounds = true,
            Child = content,
            IsHitTestVisible = false
        };
    }

    private Control CreateGridPreview(DesignControlModel model)
    {
        var grid = new Grid
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, "#FFFFFF"),
            IsHitTestVisible = false
        };

        for (var columnIndex = 0; columnIndex < Math.Max(1, model.Columns); columnIndex++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        for (var rowIndex = 0; rowIndex < Math.Max(1, model.Rows); rowIndex++)
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        for (var rowIndex = 0; rowIndex < Math.Max(1, model.Rows); rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < Math.Max(1, model.Columns); columnIndex++)
            {
                var cell = new Border
                {
                    BorderBrush = ParseBrush(model.BorderBrush, "#94A3B8"),
                    BorderThickness = model.ShowGridLines ? UniformThickness(Math.Max(1, model.BorderThickness)) : new Thickness(0),
                    Background = Brushes.Transparent
                };

                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = ParseBrush(model.Background, "#FFFFFF"),
            BorderBrush = ParseBrush(model.BorderBrush, "#94A3B8"),
            BorderThickness = UniformThickness(model.BorderThickness),
            Child = grid,
            IsHitTestVisible = false
        };
    }

    private Control CreateDataGridPreview(DesignControlModel model)
    {
        // Вместо настоящего Avalonia DataGrid рисуем собственный легкий макет таблицы.
        // Так дизайнер сохраняет полный контроль над внешним видом и поведением в разных режимах.
        var fields = GetActiveBindingFields(model.BindingSourceId).ToList();
        var groupedFields = model.AllowGrouping
            ? fields
                .Where(field => field.GroupOrder >= 0)
                .OrderBy(field => field.GroupOrder)
                .ThenBy(field => field.Header)
                .ToList()
            : new List<BindingFieldModel>();
        var visibleFields = fields.Where(field => field.IsVisible).ToList();
        var showGroupPanel = model.AllowGrouping && (model.ShowGroupPanel || groupedFields.Count > 0);

        if (GetActiveBindingSource(model.BindingSourceId) is null)
            return CreateDataGridEmptyStatePreview(model, "DataGrid: источник данных не выбран", "Выберите BindingSource во вкладке Данные.");

        if (fields.Count == 0)
            return CreateDataGridEmptyStatePreview(model, "BindingSource выбран, но поля не добавлены", "Добавьте поля вручную или импортируйте схему из DLL/SQL.");

        if (visibleFields.Count == 0)
            return CreateDataGridEmptyStatePreview(model, "Все поля BindingSource скрыты", "Включите видимость хотя бы одной колонки.");

        var themePalette = DesignerThemeCatalog.Get(VM.FormTheme);
        var headerBackgroundColor = ParseColor(model.Background, themePalette.DataGridHeaderBackground);
        var bodyBackgroundColor = ParseColor(model.DataGridRowBackground, themePalette.DataGridRowBackground);
        var alternateRowColor = ParseColor(model.DataGridAlternateRowBackground, themePalette.DataGridAlternateRowBackground);
        var borderColor = ParseColor(model.BorderBrush, "#CBD5E1");
        var headerBrush = new SolidColorBrush(headerBackgroundColor);
        var bodyBrush = new SolidColorBrush(bodyBackgroundColor);
        var alternateRowBrush = new SolidColorBrush(alternateRowColor);
        var separatorBrush = new SolidColorBrush(borderColor);
        var headerForeground = ContrastBrush(headerBackgroundColor);

        var layout = new Grid
        {
            Width = model.Width,
            Height = model.Height,
            ClipToBounds = true,
            RowDefinitions = showGroupPanel ? new RowDefinitions("Auto,Auto,*") : new RowDefinitions("Auto,*"),
            IsHitTestVisible = false
        };

        layout.Children.Add(new Border
        {
            Background = headerBrush,
            Padding = new Thickness(10, 8),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    CreatePreviewText(
                        string.IsNullOrWhiteSpace(model.Name) ? "DataGrid" : model.Name,
                        model,
                        headerForeground is SolidColorBrush headerForegroundBrush ? headerForegroundBrush.Color.ToString() : "#FFFFFF",
                        HorizontalAlignment.Left,
                        VerticalAlignment.Center)
                }
            }
        });

        if (showGroupPanel)
        {
            var chips = new WrapPanel
            {
                Margin = new Thickness(10, 8, 10, 6)
            };

            if (groupedFields.Count == 0)
            {
                chips.Children.Add(new TextBlock
                {
                    Text = "Перетащите колонку сюда для группировки",
                    Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                    FontSize = Math.Max(10, model.FontSize - 1),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            foreach (var field in groupedFields)
            {
                chips.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#E0F2FE")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#7DD3FC")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock
                    {
                        Text = $"Группа {field.GroupOrder + 1}: {field.Header}",
                        Foreground = new SolidColorBrush(Color.Parse("#0C4A6E")),
                        FontSize = Math.Max(10, model.FontSize - 1),
                        FontWeight = FontWeight.SemiBold
                    }
                });
            }

            Grid.SetRow(chips, 1);
            layout.Children.Add(chips);
        }

        var headerTable = new Grid
        {
            Background = headerBrush,
            IsHitTestVisible = false
        };

        var bodyTable = new Grid
        {
            Background = bodyBrush,
            IsHitTestVisible = false
        };

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            headerTable.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            bodyTable.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        var headerHeight = GetClassicPreviewDataGridHeaderHeight(model.FontSize);
        var rowHeight = GetClassicPreviewDataGridRowHeight(model.FontSize);
        var groupedAreaHeight = showGroupPanel ? Math.Max(40, rowHeight + 8) : 0;
        var availableRowsHeight = Math.Max(rowHeight, model.Height - headerHeight - groupedAreaHeight - 18);
        var visibleRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(2, (int)Math.Ceiling(availableRowsHeight / rowHeight)));
        var previewRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(20, visibleRowCount + 8));

        headerTable.RowDefinitions.Add(new RowDefinition(headerHeight, GridUnitType.Pixel));
        for (var rowIndex = 0; rowIndex < previewRowCount; rowIndex++)
            bodyTable.RowDefinitions.Add(new RowDefinition(rowHeight, GridUnitType.Pixel));

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            var field = visibleFields[columnIndex];
            var headerText = field.Header;

            if (!string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
                headerText += string.Equals(field.SortDirection, BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase) ? " ↓" : " ↑";

            if (model.AllowGrouping && field.GroupOrder >= 0)
                headerText += $" [{field.GroupOrder + 1}]";

            AddDataGridCell(headerTable, 0, columnIndex, headerText, headerBrush, headerForeground, model, fontWeight: FontWeight.SemiBold);
            for (var rowIndex = 0; rowIndex < previewRowCount; rowIndex++)
            {
                var content = string.Empty;
                var rowBackground = rowIndex % 2 == 0
                    ? bodyBrush
                    : alternateRowBrush;
                var rowForeground = ParseBrush(model.Foreground, "#0F172A");

                AddDataGridCell(bodyTable, rowIndex, columnIndex, content, rowBackground, rowForeground, model);
            }
        }

        var tableContainer = new Grid
        {
            Background = bodyBrush,
            ClipToBounds = true,
            IsHitTestVisible = VM.IsUserPreviewMode,
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        tableContainer.Children.Add(headerTable);
        var scrollViewer = new ScrollViewer
        {
            Content = bodyTable,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsHitTestVisible = VM.IsUserPreviewMode
        };
        Grid.SetRow(scrollViewer, 1);
        tableContainer.Children.Add(scrollViewer);

        Grid.SetRow(tableContainer, showGroupPanel ? 2 : 1);
        layout.Children.Add(tableContainer);

        var previewBorder = new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = bodyBrush,
            BorderBrush = separatorBrush,
            BorderThickness = UniformThickness(model.BorderThickness),
            Child = layout,
            IsHitTestVisible = VM.IsUserPreviewMode
        };

        if (VM.IsUserPreviewMode)
            previewBorder.PointerWheelChanged += (_, e) => HandlePreviewDataGridWheel(scrollViewer, e, rowHeight);

        return previewBorder;
    }

    private static void AddDataGridCell(
        Grid table,
        int row,
        int column,
        string text,
        IBrush background,
        IBrush foreground,
        DesignControlModel model,
        FontWeight? fontWeight = null,
        double? fontSize = null)
    {
        var cell = new Border
        {
            Background = background,
            BorderBrush = ParseBrush(model.BorderBrush, "#CBD5E1"),
            BorderThickness = new Thickness(0.5),
            Padding = GetClassicPreviewDataGridCellPadding(model.FontSize),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(model.FontFamily),
                FontSize = fontSize ?? Math.Max(11, model.FontSize),
                FontWeight = fontWeight ?? ParseFontWeight(model.FontWeight),
                Foreground = foreground,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        table.Children.Add(cell);
    }

    private static void HandlePreviewDataGridWheel(ScrollViewer scrollViewer, PointerWheelEventArgs e, double rowHeight)
    {
        // Колесико двигает только тело таблицы, не влияя на общую прокрутку формы.
        var maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (maxOffsetY <= 0)
            return;

        var wheelStep = Math.Max(24, rowHeight * 2);
        var nextOffsetY = Math.Clamp(scrollViewer.Offset.Y - (e.Delta.Y * wheelStep), 0, maxOffsetY);
        if (Math.Abs(nextOffsetY - scrollViewer.Offset.Y) < 0.1)
            return;

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextOffsetY);
        e.Handled = true;
    }

    private Control CreateDataGridEmptyStatePreview(DesignControlModel model, string title, string description)
    {
        var themePalette = DesignerThemeCatalog.Get(VM.FormTheme);
        var backgroundColor = ParseColor(model.DataGridRowBackground, themePalette.DataGridRowBackground);
        var borderColor = ParseColor(model.DataGridOuterBorderBrush, themePalette.AccentStrongBrush);
        var foregroundColor = ParseColor(model.DataGridRowForeground, "#0F172A");
        var isDark = IsDarkColor(backgroundColor);
        var mutedColor = BlendColor(foregroundColor, isDark ? Color.Parse("#CBD5E1") : Color.Parse("#64748B"), 0.55);

        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = new SolidColorBrush(backgroundColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = UniformThickness(Math.Max(1, model.BorderThickness)),
            CornerRadius = new CornerRadius(Math.Max(0, model.CornerRadius)),
            Padding = new Thickness(18),
            ClipToBounds = true,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = new SolidColorBrush(foregroundColor),
                        FontFamily = new FontFamily(model.FontFamily),
                        FontSize = Math.Max(12, model.FontSize),
                        FontWeight = FontWeight.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = description,
                        Foreground = new SolidColorBrush(mutedColor),
                        FontFamily = new FontFamily(model.FontFamily),
                        FontSize = Math.Max(11, model.FontSize - 1),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = Math.Max(220, model.Width - 48)
                    }
                }
            }
        };
    }

    private Control CreateModernDataGridPreview(DesignControlModel model)
    {
        var fields = GetActiveBindingFields(model.BindingSourceId).ToList();
        var groupedFields = model.AllowGrouping
            ? fields
                .Where(field => field.GroupOrder >= 0)
                .OrderBy(field => field.GroupOrder)
                .ThenBy(field => field.Header)
                .ToList()
            : new List<BindingFieldModel>();
        var visibleFields = fields.Where(field => field.IsVisible).ToList();
        var showGroupPanel = model.AllowGrouping && (model.ShowGroupPanel || groupedFields.Count > 0);

        if (GetActiveBindingSource(model.BindingSourceId) is null)
            return CreateDataGridEmptyStatePreview(model, "DataGrid: источник данных не выбран", "Выберите BindingSource во вкладке Данные.");

        if (fields.Count == 0)
            return CreateDataGridEmptyStatePreview(model, "BindingSource выбран, но поля не добавлены", "Добавьте поля вручную или импортируйте схему из DLL/SQL.");

        if (visibleFields.Count == 0)
            return CreateDataGridEmptyStatePreview(model, "Все поля BindingSource скрыты", "Включите видимость хотя бы одной колонки.");

        var showSummaryFooter = ShouldShowModernDataGridSummaryFooter(model.ShowFooter, visibleFields);
        var themePalette = DesignerThemeCatalog.Get(VM.FormTheme);
        var headerBackgroundColor = ParseColor(model.DataGridHeaderBackground, themePalette.DataGridHeaderBackground);
        var bodyBackgroundColor = ParseColor(model.DataGridRowBackground, themePalette.DataGridRowBackground);
        var alternateRowColor = ParseColor(model.DataGridAlternateRowBackground, themePalette.DataGridAlternateRowBackground);
        var glowColor = ParseColor(model.DataGridGlowColor, themePalette.AccentStrongBrush);
        var outerBorderColor = ParseColor(model.DataGridOuterBorderBrush, themePalette.AccentStrongBrush);
        var gridLineColor = ParseColor(model.DataGridGridLineBrush, "#D7E2EE");
        var borderColor = ParseColor(model.BorderBrush, "#CBD5E1");
        var foregroundColor = ParseColor(model.Foreground, "#0F172A");
        var rowForegroundColor = ParseColor(model.DataGridRowForeground, "#0F172A");
        var hoverRowColor = ParseColor(model.DataGridHoverRowBackground, "#EFF6FF");
        var selectedRowColor = ParseColor(model.DataGridSelectedRowBackground, "#DBEAFE");
        var selectedRowForegroundColor = ParseColor(model.DataGridSelectedRowForeground, "#0F172A");
        var isDarkChrome = IsDarkColor(bodyBackgroundColor);
        var chromeBrush = new SolidColorBrush(bodyBackgroundColor);
        var headerBrush = new SolidColorBrush(headerBackgroundColor);
        var alternateRowBrush = new SolidColorBrush(model.DataGridShowAlternatingRows ? alternateRowColor : bodyBackgroundColor);
        var separatorBrush = new SolidColorBrush(gridLineColor);
        var accentBrush = new SolidColorBrush(glowColor);
        var outerBorderBrush = new SolidColorBrush(outerBorderColor);
        var headerForeground = new SolidColorBrush(ParseColor(model.DataGridHeaderForeground, IsDarkColor(headerBackgroundColor) ? "#F8FAFC" : "#0F172A"));
        var rowForeground = new SolidColorBrush(rowForegroundColor);
        var hoverRowBrush = new SolidColorBrush(hoverRowColor);
        var selectedRowBrush = new SolidColorBrush(selectedRowColor);
        var selectedRowForeground = new SolidColorBrush(selectedRowForegroundColor);
        var titleForeground = new SolidColorBrush(isDarkChrome ? Color.Parse("#F8FAFC") : Color.Parse("#0F172A"));
        var mutedBrush = new SolidColorBrush(BlendColor(
            rowForegroundColor,
            isDarkChrome ? Color.Parse("#CBD5E1") : Color.Parse("#94A3B8"),
            isDarkChrome ? 0.34 : 0.58));
        var groupChipBackground = new SolidColorBrush(isDarkChrome
            ? Color.FromArgb(34, 96, 165, 250)
            : Color.Parse("#E0F2FE"));
        var groupChipBorder = new SolidColorBrush(isDarkChrome
            ? BlendColor(borderColor, glowColor, 0.38)
            : BlendColor(Color.Parse("#BAE6FD"), glowColor, 0.24));
        var groupChipForeground = new SolidColorBrush(isDarkChrome
            ? Color.Parse("#DBEAFE")
            : Color.Parse("#0C4A6E"));
        var showInteractivePreview = false;
        var showColumnResizeHandles = !showInteractivePreview && !model.IsLocked && VM.IsControlSelected(model);
        var filterValues = GetDataGridFilterValues(model.Id);

        var layout = new Grid
        {
            Width = model.Width,
            Height = model.Height,
            ClipToBounds = true,
            RowDefinitions = showGroupPanel ? new RowDefinitions("Auto,*") : new RowDefinitions("*"),
            IsHitTestVisible = showInteractivePreview || showColumnResizeHandles
        };

        if (showGroupPanel)
        {
            var chips = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            if (groupedFields.Count == 0)
            {
                chips.Children.Add(new Border
                {
                    Background = groupChipBackground,
                    BorderBrush = groupChipBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12, 7),
                    Child = new TextBlock
                    {
                        Text = "Перетащите колонку сюда для группировки",
                        Foreground = groupChipForeground,
                        FontSize = Math.Max(10, model.FontSize - 1),
                        FontWeight = FontWeight.SemiBold
                    }
                });
            }

            foreach (var field in groupedFields)
            {
                chips.Children.Add(new Border
                {
                    Background = groupChipBackground,
                    BorderBrush = groupChipBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(10, 5),
                    Margin = new Thickness(0, 0, 8, 8),
                    Child = new TextBlock
                    {
                        Text = $"Группа {field.GroupOrder + 1}: {field.Header}",
                        Foreground = groupChipForeground,
                        FontSize = Math.Max(10, model.FontSize - 1),
                        FontWeight = FontWeight.SemiBold
                    }
                });
            }

            Grid.SetRow(chips, 0);
            layout.Children.Add(chips);
        }

        var tableChrome = new Border
        {
            Background = chromeBrush,
            BorderBrush = outerBorderBrush,
            BorderThickness = UniformThickness(Math.Max(1, model.BorderThickness)),
            CornerRadius = new CornerRadius(Math.Max(16, model.CornerRadius + 8)),
            ClipToBounds = true,
            Child = new Grid
            {
                Background = chromeBrush,
                RowDefinitions = showSummaryFooter ? new RowDefinitions("Auto,Auto,*,Auto") : new RowDefinitions("Auto,Auto,*"),
                ClipToBounds = true
            }
        };

        Grid.SetRow(tableChrome, showGroupPanel ? 1 : 0);
        layout.Children.Add(tableChrome);

        var tableContainer = (Grid)tableChrome.Child!;
        tableContainer.IsHitTestVisible = showInteractivePreview || showColumnResizeHandles;

        var titleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("6,*"),
            ColumnSpacing = 12,
            IsHitTestVisible = showInteractivePreview
        };
        titleGrid.Children.Add(new Border
        {
            Background = accentBrush,
            Width = isDarkChrome ? 7 : 6,
            Height = 26,
            CornerRadius = new CornerRadius(999),
            VerticalAlignment = VerticalAlignment.Center
        });

        var titleText = new TextBlock
        {
            Text = GetModernDataGridTitle(model),
            FontFamily = new FontFamily(model.FontFamily),
            FontSize = Math.Max(14, model.FontSize + 1),
            FontWeight = FontWeight.Bold,
            Foreground = titleForeground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(titleText, 1);
        titleGrid.Children.Add(titleText);

        var titleShell = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(16, 14, 16, 10),
            Child = titleGrid,
            IsHitTestVisible = showInteractivePreview
        };
        tableContainer.Children.Add(titleShell);

        var headerTable = new Grid
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = showInteractivePreview || showColumnResizeHandles
        };

        var bodyTable = new Grid
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = showInteractivePreview || showColumnResizeHandles
        };

        var footerTable = new Grid
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = showInteractivePreview
        };

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            headerTable.ColumnDefinitions.Add(CreateModernDataGridColumnDefinition(visibleFields[columnIndex]));
            bodyTable.ColumnDefinitions.Add(CreateModernDataGridColumnDefinition(visibleFields[columnIndex]));
            footerTable.ColumnDefinitions.Add(CreateModernDataGridColumnDefinition(visibleFields[columnIndex]));
        }

        var headerHeight = model.DataGridShowHeader ? Math.Max(24, model.DataGridHeaderHeight) : 0;
        var rowHeight = Math.Max(18, model.DataGridRowHeight);
        var cellPadding = UniformThickness(model.DataGridCellPadding);
        var headerCellBorderThickness = new Thickness(0, 0, model.DataGridShowColumnLines ? 1 : 0, 0);
        var bodyCellBorderThickness = new Thickness(0, 0, model.DataGridShowColumnLines ? 1 : 0, model.DataGridShowRowLines ? 1 : 0);
        var groupedAreaHeight = showGroupPanel ? Math.Max(42, rowHeight + 8) : 0;
        var filterHeight = model.ShowFilterRow ? Math.Max(34, Math.Ceiling(rowHeight * 0.95)) : 0;
        var footerHeight = showSummaryFooter ? Math.Max(32, Math.Ceiling(rowHeight * 0.95)) : 0;
        var availableRowsHeight = Math.Max(rowHeight, model.Height - headerHeight - filterHeight - footerHeight - groupedAreaHeight - 12);
        var visibleRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(4, (int)Math.Ceiling(availableRowsHeight / rowHeight)));
        var previewRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(18, visibleRowCount + 6));
        var sqlPreviewRows = showInteractivePreview
            ? GetCachedInteractivePreviewRows(model.BindingSourceId)
            : Array.Empty<Dictionary<string, string>>();
        var usesSqlPreviewRows = sqlPreviewRows.Count > 0;
        var previewRows = showInteractivePreview
            ? ApplyModernPreviewSort(
                ApplyModernPreviewFilter(
                    usesSqlPreviewRows
                    ? ClonePreviewRows(sqlPreviewRows)
                    : BuildModernPreviewRows(visibleFields, previewRowCount),
                    visibleFields,
                    filterValues,
                    model.FilterMode),
                visibleFields,
                model.Id)
            : new List<Dictionary<string, string>>();
        var renderedRowCount = showInteractivePreview
            ? Math.Min(MaxPreviewDataGridRows, Math.Max(previewRows.Count, usesSqlPreviewRows ? 1 : previewRowCount))
            : previewRowCount;
        var summaryRows = previewRows.Count > 0
            ? previewRows
            : BuildModernPreviewRows(visibleFields, previewRowCount);

        headerTable.RowDefinitions.Add(new RowDefinition(headerHeight, GridUnitType.Pixel));
        if (model.ShowFilterRow)
            headerTable.RowDefinitions.Add(new RowDefinition(filterHeight, GridUnitType.Pixel));
        for (var rowIndex = 0; rowIndex < renderedRowCount; rowIndex++)
            bodyTable.RowDefinitions.Add(new RowDefinition(rowHeight, GridUnitType.Pixel));
        if (showSummaryFooter)
            footerTable.RowDefinitions.Add(new RowDefinition(footerHeight, GridUnitType.Pixel));

        var headerHost = new Grid
        {
            ClipToBounds = false,
            IsHitTestVisible = showInteractivePreview || showColumnResizeHandles
        };
        headerHost.Children.Add(headerTable);

        var headerResizeOverlay = new Canvas
        {
            Background = Brushes.Transparent,
            ClipToBounds = false,
            IsHitTestVisible = showColumnResizeHandles
        };
        headerHost.Children.Add(headerResizeOverlay);

        var headerShell = new Border
        {
            Background = headerBrush,
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 0, 0, model.DataGridShowRowLines ? 1 : 0),
            Padding = new Thickness(model.DataGridCellPadding, 0),
            Child = headerHost,
            IsHitTestVisible = showInteractivePreview || showColumnResizeHandles
        };

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            var field = visibleFields[columnIndex];
            var headerCell = CreateModernDataGridHeaderCell(
                field,
                model,
                Brushes.Transparent,
                separatorBrush,
                headerForeground,
                mutedBrush,
                accentBrush,
                headerCellBorderThickness,
                showInteractivePreview,
                () => ToggleModernDataGridSort(model, field));

            Grid.SetRow(headerCell, 0);
            Grid.SetColumn(headerCell, columnIndex);
            headerTable.Children.Add(headerCell);

            if (model.ShowFilterRow)
            {
                var filterCell = CreateModernDataGridFilterCell(
                    model,
                    field,
                    filterValues,
                    separatorBrush,
                    headerForeground,
                    mutedBrush,
                    new Thickness(0, 0, model.DataGridShowColumnLines ? 1 : 0, model.DataGridShowRowLines ? 1 : 0),
                    showInteractivePreview);

                Grid.SetRow(filterCell, 1);
                Grid.SetColumn(filterCell, columnIndex);
                headerTable.Children.Add(filterCell);
            }

            for (var rowIndex = 0; rowIndex < renderedRowCount; rowIndex++)
            {
                var rowBackground = rowIndex % 2 == 0
                    ? chromeBrush
                    : alternateRowBrush;

                var rowKey = showInteractivePreview && rowIndex < previewRows.Count
                    ? CreateModernDataGridRowKey(previewRows[rowIndex], rowIndex)
                    : rowIndex.ToString(CultureInfo.InvariantCulture);
                var rowValues = showInteractivePreview && rowIndex < previewRows.Count
                    ? previewRows[rowIndex]
                    : null;
                var isSelectedRow = showInteractivePreview && IsRuntimeDataGridRowSelected(model.Id, rowKey);
                var content = showInteractivePreview
                    ? rowIndex < previewRows.Count
                        ? previewRows[rowIndex].GetValueOrDefault(field.Path, string.Empty)
                        : string.Empty
                    : string.Empty;

                var bodyCell = CreateModernDataGridBodyCell(
                    model,
                    field,
                    rowIndex,
                    columnIndex,
                    content,
                    isSelectedRow ? selectedRowBrush : rowBackground,
                    separatorBrush,
                    isSelectedRow ? selectedRowForeground : rowForeground,
                    mutedBrush,
                    hoverRowBrush,
                    selectedRowBrush,
                    selectedRowForeground,
                    bodyCellBorderThickness,
                    cellPadding,
                    showInteractivePreview,
                    useSemanticFormatting: !usesSqlPreviewRows,
                    isSelectedRow,
                    () => SelectRuntimeDataGridRow(model, rowKey, rowValues));

                Grid.SetRow(bodyCell, rowIndex);
                Grid.SetColumn(bodyCell, columnIndex);
                bodyTable.Children.Add(bodyCell);
            }

            if (showSummaryFooter)
            {
                var footerCell = CreateModernDataGridFooterCell(
                    model,
                    field,
                    CalculateModernDataGridSummaryText(field, summaryRows),
                    headerBrush,
                    separatorBrush,
                    headerForeground,
                    new Thickness(0, model.DataGridShowRowLines ? 1 : 0, model.DataGridShowColumnLines ? 1 : 0, 0),
                    cellPadding);

                Grid.SetRow(footerCell, 0);
                Grid.SetColumn(footerCell, columnIndex);
                footerTable.Children.Add(footerCell);
            }
        }

        if (showColumnResizeHandles)
        {
            AttachModernDataGridColumnResizeHandles(
                model,
                visibleFields,
                headerTable,
                bodyTable,
                headerResizeOverlay,
                accentBrush);
        }

        if (model.DataGridShowHeader || model.ShowFilterRow)
        {
            Grid.SetRow(headerShell, 1);
            tableContainer.Children.Add(headerShell);
        }
        var scrollViewer = new ScrollViewer
        {
            Content = bodyTable,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsHitTestVisible = showInteractivePreview
        };

        Grid.SetRow(scrollViewer, 2);
        tableContainer.Children.Add(scrollViewer);

        if (showSummaryFooter)
        {
            var footerShell = new Border
            {
                Background = headerBrush,
                BorderBrush = separatorBrush,
                BorderThickness = new Thickness(0, model.DataGridShowRowLines ? 1 : 0, 0, 0),
                Padding = new Thickness(model.DataGridCellPadding, 0),
                Child = footerTable,
                IsHitTestVisible = showInteractivePreview
            };

            Grid.SetRow(footerShell, 3);
            tableContainer.Children.Add(footerShell);
        }

        var previewBorder = new Border
        {
            Width = model.Width,
            Height = model.Height,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = layout,
            IsHitTestVisible = showInteractivePreview || showColumnResizeHandles
        };

        if (showInteractivePreview)
            previewBorder.PointerWheelChanged += (_, e) => HandlePreviewDataGridWheel(scrollViewer, e, rowHeight);

        return previewBorder;
    }

    private string GetModernDataGridTitle(DesignControlModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.Text))
            return model.Text.Trim();

        var source = GetActiveBindingSource(model.BindingSourceId);
        if (source is not null && !string.IsNullOrWhiteSpace(source.Name))
            return source.Name.Trim();

        return string.IsNullOrWhiteSpace(model.Name) ? "Таблица" : model.Name.Trim();
    }

    private void ToggleModernDataGridSort(DesignControlModel model, BindingFieldModel targetField)
    {
        if (!CanModernDataGridSort(targetField))
            return;

        if (VM.IsUserPreviewMode)
        {
            VM.StatusText = "Режим просмотра скрывает рамки дизайнера. Для runtime-сортировки откройте «Предпросмотр запуска».";
            return;
        }

        var fields = GetActiveBindingFields(model.BindingSourceId).ToList();
        if (!fields.Contains(targetField))
            return;

        var nextDirection = targetField.SortDirection switch
        {
            BindingFieldModel.SortDirectionAscending => BindingFieldModel.SortDirectionDescending,
            BindingFieldModel.SortDirectionDescending => BindingFieldModel.SortDirectionNone,
            _ => BindingFieldModel.SortDirectionAscending
        };

        foreach (var field in fields)
        {
            if (ReferenceEquals(field, targetField))
                continue;

            if (!string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase) || field.SortOrder >= 0)
            {
                field.SortDirection = BindingFieldModel.SortDirectionNone;
                field.SortOrder = -1;
            }
        }

        targetField.SortDirection = nextDirection;
        targetField.SortOrder = string.Equals(nextDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase) ? -1 : 0;
    }

    private void ToggleModernDataGridRuntimeSort(DesignControlModel model, BindingFieldModel targetField)
    {
        VM.StatusText = "Режим просмотра не выполняет runtime-сортировку. Откройте «Предпросмотр запуска».";
    }

    private string GetModernDataGridEffectiveSortDirection(DesignControlModel model, BindingFieldModel field)
    {
        return field.SortDirection;
    }

    private Border CreateModernDataGridHeaderCell(
        BindingFieldModel field,
        DesignControlModel model,
        IBrush background,
        IBrush separatorBrush,
        IBrush foreground,
        IBrush mutedBrush,
        IBrush accentBrush,
        Thickness borderThickness,
        bool isInteractive,
        Action onClick)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header,
            FontFamily = new FontFamily(model.FontFamily),
            FontSize = Math.Max(8, model.DataGridHeaderFontSize),
            FontWeight = ParseFontWeight(model.DataGridHeaderFontWeight),
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = GetModernDataGridHorizontalAlignment(field.HeaderAlignment),
            TextAlignment = GetModernDataGridTextAlignment(field.HeaderAlignment),
            TextTrimming = GetModernDataGridTextTrimming(field.TextTrimming),
            TextWrapping = GetModernDataGridTextWrapping(field.TextWrapping),
            MaxLines = Math.Max(0, field.MaxLines)
        };

        var iconStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        iconStack.Children.Add(CreateModernDataGridFilterIcon(CanModernDataGridSort(field) ? accentBrush : mutedBrush));

        if (model.AllowGrouping && field.GroupOrder >= 0)
        {
            iconStack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#DBEAFE")),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = (field.GroupOrder + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = Math.Max(10, model.DataGridHeaderFontSize - 2),
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#1D4ED8"))
                }
            });
        }

        var sortDirection = GetModernDataGridEffectiveSortDirection(model, field);
        if (CanModernDataGridSort(field) && !string.Equals(sortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
        {
            iconStack.Children.Add(new TextBlock
            {
                Text = string.Equals(sortDirection, BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase) ? "↓" : "↑",
                FontSize = Math.Max(10, model.DataGridHeaderFontSize - 1),
                FontWeight = FontWeight.Bold,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            Children =
            {
                title
            }
        };

        Grid.SetColumn(iconStack, 1);
        content.Children.Add(iconStack);

        var cell = new Border
        {
            Background = background,
            BorderBrush = separatorBrush,
            BorderThickness = borderThickness,
            Padding = new Thickness(0, 0, Math.Max(4, model.DataGridCellPadding), 0),
            Child = content
        };

        if (CanModernDataGridSort(field) && isInteractive)
        {
            cell.Cursor = new Cursor(StandardCursorType.Hand);
            cell.PointerPressed += (_, e) =>
            {
                onClick();
                e.Handled = true;
            };
        }

        return cell;
    }

    private Border CreateModernDataGridFilterCell(
        DesignControlModel model,
        BindingFieldModel field,
        IDictionary<string, string> filterValues,
        IBrush separatorBrush,
        IBrush foreground,
        IBrush mutedBrush,
        Thickness borderThickness,
        bool isInteractive)
    {
        Control content;
        if (field.AllowFilter)
        {
            var textBox = new TextBox
            {
                Text = filterValues.TryGetValue(field.Path, out var value) ? value : string.Empty,
                Watermark = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header,
                FontFamily = new FontFamily(model.FontFamily),
                FontSize = Math.Max(10, model.DataGridRowFontSize - 1),
                Foreground = foreground,
                Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
                BorderBrush = mutedBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4),
                IsEnabled = isInteractive,
                IsHitTestVisible = isInteractive,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (isInteractive)
            {
                textBox.TextChanged += (_, _) =>
                {
                    filterValues[field.Path] = textBox.Text ?? string.Empty;
                    SchedulePreviewFilterRefresh();
                };
                textBox.KeyUp += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Dispatcher.UIThread.Post(RenderDesigner, DispatcherPriority.Background);
                        e.Handled = true;
                    }
                };
                textBox.LostFocus += (_, _) =>
                {
                    Dispatcher.UIThread.Post(RenderDesigner, DispatcherPriority.Background);
                };
            }

            content = textBox;
        }
        else
        {
            content = new TextBlock
            {
                Text = "Без фильтра",
                FontFamily = new FontFamily(model.FontFamily),
                FontSize = Math.Max(10, model.DataGridRowFontSize - 1),
                Foreground = mutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = separatorBrush,
            BorderThickness = borderThickness,
            Padding = new Thickness(2, 3),
            Child = content
        };
    }

    private void AttachModernDataGridColumnResizeHandles(
        DesignControlModel model,
        IReadOnlyList<BindingFieldModel> visibleFields,
        Grid headerTable,
        Grid bodyTable,
        Canvas overlay,
        IBrush accentBrush)
    {
        if (model.IsLocked || visibleFields.Count == 0)
            return;

        const double handleWidth = 12;
        var fallbackWidth = Math.Max(56, model.Width / Math.Max(1, visibleFields.Count));

        var activeIndex = -1;
        var startPointerX = 0d;
        var startWidth = 0d;

        void ApplyLiveWidth(int columnIndex, double width)
        {
            var gridLength = new GridLength(width, GridUnitType.Pixel);
            headerTable.ColumnDefinitions[columnIndex].Width = gridLength;
            bodyTable.ColumnDefinitions[columnIndex].Width = gridLength;
        }

        void CommitResize(int columnIndex)
        {
            var field = visibleFields[columnIndex];
            var finalWidth = ClampModernDataGridColumnWidth(field, GetInteractivePreviewColumnWidth(headerTable.ColumnDefinitions[columnIndex], fallbackWidth));
            field.Width = Math.Round(finalWidth).ToString(CultureInfo.InvariantCulture);
            VM.StatusText = $"Ширина колонки «{field.Header}» изменена: {Math.Round(finalWidth)} px";
        }

        void RenderHandles()
        {
            overlay.Width = Math.Max(1, headerTable.Bounds.Width > 0 ? headerTable.Bounds.Width : model.Width);
            overlay.Height = Math.Max(1, headerTable.Bounds.Height);
            overlay.Children.Clear();

            if (!VM.IsControlSelected(model))
                return;

            var runningOffset = 0d;
            for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
            {
                runningOffset += GetInteractivePreviewColumnWidth(headerTable.ColumnDefinitions[columnIndex], fallbackWidth);

                if (!visibleFields[columnIndex].AllowResize)
                    continue;

                var handleIndex = columnIndex;
                var handle = new Border
                {
                    Width = handleWidth,
                    Height = Math.Max(1, headerTable.Bounds.Height),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.SizeWestEast),
                    Child = new Border
                    {
                        Width = 2,
                        CornerRadius = new CornerRadius(999),
                        Background = accentBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Opacity = 0.9
                    }
                };

                Canvas.SetLeft(handle, Math.Max(0, runningOffset - (handleWidth * 0.5)));
                Canvas.SetTop(handle, 0);

                handle.PointerPressed += (_, e) =>
                {
                    var point = e.GetCurrentPoint(handle);
                    if (!point.Properties.IsLeftButtonPressed)
                        return;

                    activeIndex = handleIndex;
                    startPointerX = e.GetPosition(overlay).X;
                    startWidth = GetInteractivePreviewColumnWidth(headerTable.ColumnDefinitions[handleIndex], fallbackWidth);
                    _isResizingGridColumn = true;
                    VM.BeginUndoBatch();
                    e.Pointer.Capture(handle);
                    e.Handled = true;
                };

                handle.PointerMoved += (_, e) =>
                {
                    if (!_isResizingGridColumn || activeIndex != handleIndex)
                        return;

                    var delta = e.GetPosition(overlay).X - startPointerX;
                    var newWidth = ClampModernDataGridColumnWidth(visibleFields[handleIndex], startWidth + delta);
                    ApplyLiveWidth(handleIndex, newWidth);
                    Canvas.SetLeft(handle, Math.Max(0, e.GetPosition(overlay).X - (handleWidth * 0.5)));
                    e.Handled = true;
                };

                handle.PointerReleased += (_, e) =>
                {
                    if (!_isResizingGridColumn || activeIndex != handleIndex)
                        return;

                    _isResizingGridColumn = false;
                    activeIndex = -1;
                    CommitResize(handleIndex);
                    VM.CommitUndoBatch();
                    e.Pointer.Capture(null);
                    e.Handled = true;
                };

                handle.PointerCaptureLost += (_, _) =>
                {
                    if (!_isResizingGridColumn || activeIndex != handleIndex)
                        return;

                    _isResizingGridColumn = false;
                    activeIndex = -1;
                    CommitResize(handleIndex);
                    VM.CommitUndoBatch();
                };

                overlay.Children.Add(handle);
            }
        }

        headerTable.SizeChanged += (_, _) =>
        {
            if (!_isResizingGridColumn)
                RenderHandles();
        };

        Dispatcher.UIThread.Post(RenderHandles, DispatcherPriority.Loaded);
    }

    private Border CreateModernDataGridBodyCell(
        DesignControlModel model,
        BindingFieldModel field,
        int rowIndex,
        int columnIndex,
        string text,
        IBrush background,
        IBrush separatorBrush,
        IBrush foreground,
        IBrush mutedBrush,
        IBrush hoverBackground,
        IBrush selectedBackground,
        IBrush selectedForeground,
        Thickness borderThickness,
        Thickness padding,
        bool showData,
        bool useSemanticFormatting,
        bool isSelectedRow,
        Action onSelectRow)
    {
        var content = showData
            ? CreateModernDataGridValuePresenter(model, field, text, foreground, mutedBrush, useSemanticFormatting)
            : CreateModernDataGridSkeletonBar(model, rowIndex, columnIndex, mutedBrush);

        var cell = new Border
        {
            Background = background,
            BorderBrush = separatorBrush,
            BorderThickness = borderThickness,
            Padding = padding,
            Child = content
        };

        if (showData)
        {
            cell.Cursor = new Cursor(StandardCursorType.Hand);
            cell.PointerEntered += (_, _) =>
            {
                if (!isSelectedRow)
                    cell.Background = hoverBackground;
            };
            cell.PointerExited += (_, _) =>
            {
                cell.Background = isSelectedRow ? selectedBackground : background;
            };
            cell.PointerPressed += (_, e) =>
            {
                onSelectRow();
                cell.Background = selectedBackground;
                SetPresenterForeground(content, selectedForeground);
                e.Handled = true;
            };
        }

        return cell;
    }

    private static Border CreateModernDataGridFooterCell(
        DesignControlModel model,
        BindingFieldModel field,
        string text,
        IBrush background,
        IBrush separatorBrush,
        IBrush foreground,
        Thickness borderThickness,
        Thickness padding)
    {
        var normalizedSummaryType = BindingFieldModel.NormalizeSummaryType(field.SummaryType);
        return new Border
        {
            Background = background,
            BorderBrush = separatorBrush,
            BorderThickness = borderThickness,
            Padding = padding,
            Child = new TextBlock
            {
                Text = normalizedSummaryType == BindingFieldModel.SummaryTypeNone ? string.Empty : text,
                FontFamily = new FontFamily(model.FontFamily),
                FontSize = Math.Max(10, model.DataGridRowFontSize),
                FontWeight = FontWeight.SemiBold,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = GetModernDataGridHorizontalAlignment(field.CellAlignment),
                TextAlignment = GetModernDataGridTextAlignment(field.CellAlignment),
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
    }

    private static bool ShouldShowModernDataGridSummaryFooter(bool showFooter, IEnumerable<BindingFieldModel> fields)
    {
        return showFooter && fields.Any(field =>
            BindingFieldModel.NormalizeSummaryType(field.SummaryType) != BindingFieldModel.SummaryTypeNone);
    }

    private static string CalculateModernDataGridSummaryText(BindingFieldModel field, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var summaryType = BindingFieldModel.NormalizeSummaryType(field.SummaryType);
        if (summaryType == BindingFieldModel.SummaryTypeNone)
            return string.Empty;

        if (summaryType == BindingFieldModel.SummaryTypeCount)
            return FormatModernDataGridSummaryValue(field, summaryType, rows.Count);

        var values = rows
            .Select(row => row.TryGetValue(field.Path, out var value) ? value : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (values.Count == 0)
            return string.Empty;

        var numbers = values
            .Select(value => TryParseModernDataGridNumber(value, out var number) ? (double?)number : null)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToList();

        return summaryType switch
        {
            BindingFieldModel.SummaryTypeSum when numbers.Count > 0 =>
                FormatModernDataGridSummaryValue(field, summaryType, numbers.Sum()),
            BindingFieldModel.SummaryTypeAvg when numbers.Count > 0 =>
                FormatModernDataGridSummaryValue(field, summaryType, numbers.Average()),
            BindingFieldModel.SummaryTypeMin when numbers.Count > 0 =>
                FormatModernDataGridSummaryValue(field, summaryType, numbers.Min()),
            BindingFieldModel.SummaryTypeMax when numbers.Count > 0 =>
                FormatModernDataGridSummaryValue(field, summaryType, numbers.Max()),
            BindingFieldModel.SummaryTypeMin =>
                FormatModernDataGridSummaryValue(field, summaryType, values.Min(StringComparer.CurrentCultureIgnoreCase) ?? string.Empty),
            BindingFieldModel.SummaryTypeMax =>
                FormatModernDataGridSummaryValue(field, summaryType, values.Max(StringComparer.CurrentCultureIgnoreCase) ?? string.Empty),
            _ => string.Empty
        };
    }

    private static string FormatModernDataGridSummaryValue(BindingFieldModel field, string summaryType, object value)
    {
        var format = field.SummaryFormat?.Trim();
        if (!string.IsNullOrWhiteSpace(format))
        {
            try
            {
                if (format.Contains("{0", StringComparison.Ordinal))
                    return string.Format(CultureInfo.CurrentCulture, format, value);

                if (value is IFormattable formattable)
                    return formattable.ToString(format, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // Если пользователь ошибся в формате, не ломаем превью, а показываем безопасное значение.
            }
        }

        return summaryType switch
        {
            BindingFieldModel.SummaryTypeCount => $"Count: {Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.CurrentCulture)}",
            BindingFieldModel.SummaryTypeSum when value is IFormattable formattable => $"Sum: {formattable.ToString("N2", CultureInfo.CurrentCulture)}",
            BindingFieldModel.SummaryTypeAvg when value is IFormattable formattable => $"Avg: {formattable.ToString("N2", CultureInfo.CurrentCulture)}",
            BindingFieldModel.SummaryTypeMin => $"Min: {value}",
            BindingFieldModel.SummaryTypeMax => $"Max: {value}",
            _ => value?.ToString() ?? string.Empty
        };
    }

    private static void SetPresenterForeground(Control control, IBrush foreground)
    {
        switch (control)
        {
            case TextBlock textBlock:
                textBlock.Foreground = foreground;
                break;
            case ContentControl { Content: Control child }:
                SetPresenterForeground(child, foreground);
                break;
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                    SetPresenterForeground(child, foreground);
                break;
        }
    }

    private Control CreateModernDataGridValuePresenter(
        DesignControlModel model,
        BindingFieldModel field,
        string text,
        IBrush foreground,
        IBrush mutedBrush,
        bool useSemanticFormatting)
    {
        var signature = $"{field.Header} {field.Path} {field.TypeName}".ToLowerInvariant();
        var displayText = field.FormatDisplayValue(text);
        var horizontalAlignment = GetModernDataGridHorizontalAlignment(field.CellAlignment);
        var textAlignment = GetModernDataGridTextAlignment(field.CellAlignment);
        var textTrimming = GetModernDataGridTextTrimming(field.TextTrimming);
        var textWrapping = GetModernDataGridTextWrapping(field.TextWrapping);
        var maxLines = Math.Max(0, field.MaxLines);

        if (useSemanticFormatting && ModernDataGridLooksLikePercentage(signature, displayText))
        {
            var isNegative = displayText.Contains('-', StringComparison.Ordinal);
            return new Border
            {
                Background = new SolidColorBrush(Color.Parse(isNegative ? "#FEE2E2" : "#DCFCE7")),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 4),
                HorizontalAlignment = horizontalAlignment,
                Child = new TextBlock
                {
                    Text = displayText,
                    FontFamily = new FontFamily(model.FontFamily),
                    FontSize = Math.Max(10, model.DataGridRowFontSize - 1),
                    FontWeight = ParseFontWeight(model.DataGridRowFontWeight),
                    Foreground = new SolidColorBrush(Color.Parse(isNegative ? "#B91C1C" : "#15803D")),
                    TextTrimming = textTrimming,
                    TextWrapping = textWrapping,
                    MaxLines = maxLines
                }
            };
        }

        if (useSemanticFormatting && ModernDataGridLooksLikeBoolean(signature))
        {
            var isTrue = string.Equals(text, "Да", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

            return new Border
            {
                Background = new SolidColorBrush(Color.Parse(isTrue ? "#DBEAFE" : "#E2E8F0")),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 4),
                HorizontalAlignment = horizontalAlignment,
                Child = new TextBlock
                {
                    Text = displayText,
                    FontFamily = new FontFamily(model.FontFamily),
                    FontSize = Math.Max(10, model.DataGridRowFontSize - 1),
                    FontWeight = ParseFontWeight(model.DataGridRowFontWeight),
                    Foreground = new SolidColorBrush(Color.Parse(isTrue ? "#1D4ED8" : "#475569")),
                    TextTrimming = textTrimming,
                    TextWrapping = textWrapping,
                    MaxLines = maxLines
                }
            };
        }

        if (useSemanticFormatting && ModernDataGridLooksLikeStatus(signature))
        {
            var (badgeBackground, badgeForeground, badgeBorder) = GetModernDataGridStatusPalette(displayText);
            return new Border
            {
                Background = new SolidColorBrush(badgeBackground),
                BorderBrush = new SolidColorBrush(badgeBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(12, 5),
                HorizontalAlignment = horizontalAlignment,
                Child = new TextBlock
                {
                    Text = displayText,
                    FontFamily = new FontFamily(model.FontFamily),
                    FontSize = Math.Max(10, model.DataGridRowFontSize - 1),
                    FontWeight = ParseFontWeight(model.DataGridRowFontWeight),
                    Foreground = new SolidColorBrush(badgeForeground),
                    TextTrimming = textTrimming,
                    TextWrapping = textWrapping,
                    MaxLines = maxLines
                }
            };
        }

        if (useSemanticFormatting && ModernDataGridLooksLikeRating(signature))
        {
            return new TextBlock
            {
                Text = BuildModernDataGridRatingStars(displayText),
                FontFamily = new FontFamily(model.FontFamily),
                FontSize = Math.Max(11, model.DataGridRowFontSize),
                FontWeight = ParseFontWeight(model.DataGridRowFontWeight),
                Foreground = new SolidColorBrush(Color.Parse("#F59E0B")),
                TextTrimming = textTrimming,
                TextWrapping = textWrapping,
                MaxLines = maxLines,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = horizontalAlignment,
                TextAlignment = textAlignment
            };
        }

        var textBlock = new TextBlock
        {
            Text = displayText,
            FontFamily = new FontFamily(model.FontFamily),
            FontSize = Math.Max(11, model.DataGridRowFontSize),
            FontWeight = ParseFontWeight(model.DataGridRowFontWeight),
            Foreground = useSemanticFormatting && ModernDataGridLooksLikeSecondaryText(signature) ? mutedBrush : foreground,
            TextTrimming = textTrimming,
            TextWrapping = textWrapping,
            MaxLines = maxLines,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = horizontalAlignment,
            TextAlignment = textAlignment
        };

        return textBlock;
    }

    private static Border CreateModernDataGridSkeletonBar(
        DesignControlModel model,
        int rowIndex,
        int columnIndex,
        IBrush mutedBrush)
    {
        var width = 56 + ((rowIndex * 17) + (columnIndex * 23)) % 72;
        return new Border
        {
            Width = width,
            Height = Math.Max(8, model.FontSize - 2),
            Background = mutedBrush,
            CornerRadius = new CornerRadius(999),
            Opacity = 0.22,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = GetModernDataGridHorizontalAlignment(model.DataGridTextAlignment)
        };
    }

    private static double GetClassicPreviewDataGridHeaderHeight(double fontSize)
    {
        return Math.Max(34, Math.Ceiling(fontSize * 1.8 + 14));
    }

    private static double GetClassicPreviewDataGridRowHeight(double fontSize)
    {
        return Math.Max(28, Math.Ceiling(fontSize * 1.7 + 14));
    }

    private static Thickness GetClassicPreviewDataGridCellPadding(double fontSize)
    {
        var vertical = Math.Max(6, Math.Ceiling(fontSize * 0.35));
        return new Thickness(8, vertical);
    }

    private static double GetModernPreviewDataGridHeaderHeight(double fontSize)
    {
        return Math.Max(42, Math.Ceiling(fontSize * 2.0 + 14));
    }

    private static double GetModernPreviewDataGridRowHeight(double fontSize)
    {
        return Math.Max(34, Math.Ceiling(fontSize * 1.9 + 16));
    }

    private static Thickness GetModernPreviewDataGridHeaderPadding(double fontSize)
    {
        var top = Math.Max(10, Math.Ceiling(fontSize * 0.45));
        var bottom = Math.Max(12, Math.Ceiling(fontSize * 0.55));
        return new Thickness(16, top, 16, bottom);
    }

    private static Thickness GetModernPreviewDataGridCellPadding(double fontSize)
    {
        var vertical = Math.Max(11, Math.Ceiling(fontSize * 0.55));
        return new Thickness(16, vertical);
    }

    private static HorizontalAlignment GetModernDataGridHorizontalAlignment(string? alignment)
    {
        return BindingFieldModel.NormalizeAlignment(alignment) switch
        {
            BindingFieldModel.AlignmentCenter => HorizontalAlignment.Center,
            BindingFieldModel.AlignmentRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };
    }

    private static TextAlignment GetModernDataGridTextAlignment(string? alignment)
    {
        return BindingFieldModel.NormalizeAlignment(alignment) switch
        {
            BindingFieldModel.AlignmentCenter => TextAlignment.Center,
            BindingFieldModel.AlignmentRight => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    private static TextTrimming GetModernDataGridTextTrimming(string? trimming)
    {
        return BindingFieldModel.NormalizeTextTrimming(trimming) switch
        {
            BindingFieldModel.TextTrimmingNone => TextTrimming.None,
            BindingFieldModel.TextTrimmingWordEllipsis => TextTrimming.WordEllipsis,
            _ => TextTrimming.CharacterEllipsis
        };
    }

    private static TextWrapping GetModernDataGridTextWrapping(string? wrapping)
    {
        return BindingFieldModel.NormalizeTextWrapping(wrapping) switch
        {
            BindingFieldModel.TextWrappingWrap => TextWrapping.Wrap,
            _ => TextWrapping.NoWrap
        };
    }

    private static bool CanModernDataGridSort(BindingFieldModel field) => field.AllowSort && field.IsSortable;

    private static Avalonia.Controls.Shapes.Path CreateModernDataGridFilterIcon(IBrush brush)
    {
        return new Avalonia.Controls.Shapes.Path
        {
            Width = 10,
            Height = 8,
            Stretch = Stretch.Fill,
            Fill = brush,
            Data = Geometry.Parse("M1,1 L5,6 L9,1 L7.8,0 L5,3.2 L2.2,0 Z"),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static ColumnDefinition CreateModernDataGridColumnDefinition(BindingFieldModel field)
    {
        var definition = CreateModernDataGridColumnDefinition(field.Width);
        definition.MinWidth = Math.Max(0, field.MinWidth);
        if (field.MaxWidth > 0)
            definition.MaxWidth = Math.Max(definition.MinWidth, field.MaxWidth);
        return definition;
    }

    private static ColumnDefinition CreateModernDataGridColumnDefinition(string? width)
    {
        if (string.IsNullOrWhiteSpace(width))
            return new ColumnDefinition(1, GridUnitType.Star);

        var normalized = width.Trim().Replace(',', '.');
        if (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            var factorPart = normalized[..^1];
            if (string.IsNullOrWhiteSpace(factorPart))
                return new ColumnDefinition(1, GridUnitType.Star);

            return double.TryParse(factorPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var starValue) && starValue > 0
                ? new ColumnDefinition(starValue, GridUnitType.Star)
                : new ColumnDefinition(1, GridUnitType.Star);
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixelValue) && pixelValue > 0
            ? new ColumnDefinition(pixelValue, GridUnitType.Pixel)
            : new ColumnDefinition(1, GridUnitType.Star);
    }

    private static double ClampModernDataGridColumnWidth(BindingFieldModel field, double width)
    {
        var minWidth = Math.Max(0, field.MinWidth);
        var maxWidth = Math.Max(0, field.MaxWidth);
        var clamped = Math.Max(minWidth, width);
        return maxWidth > 0 ? Math.Min(maxWidth, clamped) : clamped;
    }

    private static double GetInteractivePreviewColumnWidth(ColumnDefinition definition, double fallbackWidth)
    {
        if (definition.ActualWidth > 1)
            return definition.ActualWidth;

        if (definition.Width.IsAbsolute && definition.Width.Value > 0)
            return definition.Width.Value;

        return fallbackWidth;
    }

    private static List<Dictionary<string, string>> BuildModernPreviewRows(IReadOnlyList<BindingFieldModel> fields, int rowCount)
    {
        var rows = new List<Dictionary<string, string>>(rowCount);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
                row[field.Path] = CreateModernPreviewValue(field.Header, field.Path, field.TypeName, field.SampleValue, rowIndex);

            rows.Add(row);
        }

        return rows;
    }

    private static List<Dictionary<string, string>> ClonePreviewRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        return rows
            .Select(row => new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private List<Dictionary<string, string>> ApplyModernPreviewSort(
        List<Dictionary<string, string>> rows,
        IReadOnlyList<BindingFieldModel> fields,
        string controlId)
    {
        BindingFieldModel? sortedField;
        string sortDirection;

        sortedField = fields
            .Where(CanModernDataGridSort)
            .Where(field => !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
            .OrderBy(field => field.SortOrder < 0 ? int.MaxValue : field.SortOrder)
            .FirstOrDefault();
        sortDirection = sortedField?.SortDirection ?? BindingFieldModel.SortDirectionNone;

        if (sortedField is null || string.Equals(sortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
            return rows;

        var descending = string.Equals(sortDirection, BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase);
        var orderedRows = descending
            ? rows.OrderByDescending(row => GetModernDataGridSortKey(sortedField, row.GetValueOrDefault(sortedField.Path, string.Empty)))
            : rows.OrderBy(row => GetModernDataGridSortKey(sortedField, row.GetValueOrDefault(sortedField.Path, string.Empty)));

        return orderedRows.ToList();
    }

    private static List<Dictionary<string, string>> ApplyModernPreviewFilter(
        List<Dictionary<string, string>> rows,
        IReadOnlyList<BindingFieldModel> fields,
        IReadOnlyDictionary<string, string> filterValues,
        string? filterMode)
    {
        if (filterValues.Count == 0)
            return rows;

        var activeFilters = fields
            .Where(field => field.AllowFilter)
            .Select(field => new
            {
                Field = field,
                Query = filterValues.TryGetValue(field.Path, out var value) ? value?.Trim() : string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Query))
            .ToList();

        if (activeFilters.Count == 0)
            return rows;

        return rows
            .Where(row => activeFilters.All(filter =>
                MatchesModernDataGridFilter(
                    row.GetValueOrDefault(filter.Field.Path, string.Empty),
                    filter.Query!,
                    filterMode)))
            .ToList();
    }

    private static bool MatchesModernDataGridFilter(string? value, string query, string? filterMode)
    {
        var text = value ?? string.Empty;
        return DesignControlModel.NormalizeDataGridFilterMode(filterMode) switch
        {
            DesignControlModel.DataGridFilterModeStartsWith => text.StartsWith(query, StringComparison.OrdinalIgnoreCase),
            DesignControlModel.DataGridFilterModeEquals => string.Equals(text, query, StringComparison.OrdinalIgnoreCase),
            _ => text.Contains(query, StringComparison.OrdinalIgnoreCase)
        };
    }

    private Dictionary<string, string> GetDataGridFilterValues(string controlId)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateModernDataGridRowKey(IReadOnlyDictionary<string, string> row, int rowIndex)
    {
        if (row.Count == 0)
            return rowIndex.ToString(CultureInfo.InvariantCulture);

        return string.Join(
            "\u001F",
            row
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}={item.Value}"));
    }

    private bool IsRuntimeDataGridRowSelected(string controlId, string rowKey)
    {
        return false;
    }

    private void SelectRuntimeDataGridRow(
        DesignControlModel model,
        string rowKey,
        IReadOnlyDictionary<string, string>? rowValues)
    {
        VM.StatusText = "Режим просмотра не выполняет DataGrid.SelectionChanged. Откройте «Предпросмотр запуска».";
    }

    private Dictionary<string, string> BuildRuntimeSourceValues(DesignControlModel source, string currentValue)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (source.Type)
        {
            case DesignerControlTypes.TextBox:
            case DesignerControlTypes.TextBlock:
                values[InteractionModel.TargetPropertyText] = currentValue;
                break;

            case DesignerControlTypes.Button:
                values[InteractionModel.TargetPropertyContent] = currentValue;
                break;

            case DesignerControlTypes.CheckBox:
                values[InteractionModel.TargetPropertyIsChecked] = currentValue;
                break;
        }

        values["Value"] = currentValue;
        return values;
    }

    private bool ApplyRuntimeInteractions(
        DesignControlModel source,
        string eventName,
        IReadOnlyDictionary<string, string> sourceValues)
    {
        return false;
    }

    private async Task ShowRuntimeMessageAsync(string message, string? title)
    {
        try
        {
            var closeButton = new Button
            {
                Content = "OK",
                MinWidth = 92,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var dialog = new Window
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Сообщение" : title,
                Width = 380,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(18),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(message) ? "Сообщение" : message,
                            TextWrapping = TextWrapping.Wrap
                        },
                        closeButton
                    }
                }
            };

            closeButton.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Preview message failed: {ex.Message}";
        }
    }

    private static (int Kind, double Number, DateTime Date, string Text) GetModernDataGridSortKey(BindingFieldModel field, string value)
    {
        if (TryParseModernDataGridNumber(value, out var number))
            return (0, number, default, string.Empty);

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return (1, 0, date, string.Empty);
        }

        return (2, 0, default, value ?? string.Empty);
    }

    private static string CreateModernPreviewValue(string header, string path, string typeName, string sampleValue, int rowIndex)
    {
        var signature = $"{header} {path} {typeName}".ToLowerInvariant();

        if (ModernDataGridLooksLikeRating(signature))
            return rowIndex switch
            {
                0 => "4.9",
                1 => "4.2",
                2 => "3.8",
                3 => "4.6",
                4 => "2.9",
                _ => ((rowIndex % 5) + 1).ToString("0.0", CultureInfo.InvariantCulture)
            };

        if (ModernDataGridLooksLikePercentage(signature, sampleValue))
            return rowIndex switch
            {
                0 => "2.2%",
                1 => "-1.9%",
                2 => "4.7%",
                3 => "0.1%",
                4 => "3.6%",
                _ => $"{((rowIndex % 7) - 2) * 1.1:0.0}%"
            };

        if (ModernDataGridLooksLikeStatus(signature))             return rowIndex switch             {                 0 => "??????? ????????",                 1 => "??????? ????",                 2 => "????? ???",                 3 => "??????? ???????",                 4 => "? ??????",                 _ => "???????"             }; 
        if (ModernDataGridLooksLikeBoolean(signature))
            return rowIndex % 2 == 0 ? "Да" : "Нет";

        if (ModernDataGridLooksLikeCurrency(signature))
            return $"{12500 + (rowIndex * 1450):N0} ₽";

        if (ModernDataGridLooksLikeDate(signature))
            return DateTime.Today.AddDays(-rowIndex * 3).ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

        if (ModernDataGridLooksLikeNumeric(signature, sampleValue))
        {
            if (TryParseModernDataGridNumber(sampleValue, out var sampleNumber))
                return (sampleNumber + rowIndex).ToString("0.##", CultureInfo.InvariantCulture);

            return (1000 + rowIndex).ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(sampleValue))
            return rowIndex == 0 ? sampleValue : $"{sampleValue} • {rowIndex + 1}";

        return rowIndex == 0 ? (string.IsNullOrWhiteSpace(header) ? path : header) : $"{(string.IsNullOrWhiteSpace(header) ? path : header)} {rowIndex + 1}";
    }

    private static bool TryParseModernDataGridNumber(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var filtered = new string(value
            .Where(ch => char.IsDigit(ch) || ch is '-' or '+' or '.' or ',')
            .ToArray());

        return double.TryParse(filtered.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            || double.TryParse(filtered, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
    }

    private static bool ModernDataGridLooksLikeEmail(string signature)
    {
        return signature.Contains("email", StringComparison.Ordinal)
            || signature.Contains("mail", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeName(string signature)
    {
        return signature.Contains("name", StringComparison.Ordinal)
            || signature.Contains("fio", StringComparison.Ordinal)
            || signature.Contains("full", StringComparison.Ordinal)
            || signature.Contains("customer", StringComparison.Ordinal)
            || signature.Contains("client", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeLocation(string signature)
    {
        return signature.Contains("province", StringComparison.Ordinal)
            || signature.Contains("country", StringComparison.Ordinal)
            || signature.Contains("city", StringComparison.Ordinal)
            || signature.Contains("region", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeDate(string signature)
    {
        return signature.Contains("date", StringComparison.Ordinal)
            || signature.Contains("created", StringComparison.Ordinal)
            || signature.Contains("updated", StringComparison.Ordinal)
            || signature.Contains("time", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeBoolean(string signature)
    {
        return signature.Contains("bool", StringComparison.Ordinal)
            || signature.Contains("active", StringComparison.Ordinal)
            || signature.Contains("enabled", StringComparison.Ordinal)
            || signature.Contains("available", StringComparison.Ordinal)
            || signature.Contains("visible", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeRating(string signature)
    {
        return signature.Contains("rating", StringComparison.Ordinal)
            || signature.Contains("score", StringComparison.Ordinal)
            || signature.Contains("rank", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeStatus(string signature)
    {
        return signature.Contains("status", StringComparison.Ordinal)
            || signature.Contains("state", StringComparison.Ordinal)
            || signature.Contains("stage", StringComparison.Ordinal)
            || signature.Contains("result", StringComparison.Ordinal)
            || signature.Contains("workflow", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikeCurrency(string signature)
    {
        return signature.Contains("price", StringComparison.Ordinal)
            || signature.Contains("cost", StringComparison.Ordinal)
            || signature.Contains("amount", StringComparison.Ordinal)
            || signature.Contains("sum", StringComparison.Ordinal)
            || signature.Contains("total", StringComparison.Ordinal)
            || signature.Contains("salary", StringComparison.Ordinal);
    }

    private static bool ModernDataGridLooksLikePercentage(string signature, string? value)
    {
        return signature.Contains("%", StringComparison.Ordinal)
            || signature.Contains("percent", StringComparison.Ordinal)
            || signature.Contains("rate", StringComparison.Ordinal)
            || signature.Contains("gdp", StringComparison.Ordinal)
            || signature.Contains("unemployment", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(value) && value.Contains('%', StringComparison.Ordinal));
    }

    private static bool ModernDataGridLooksLikeSecondaryText(string signature)
    {
        return ModernDataGridLooksLikeEmail(signature)
            || signature.Contains("description", StringComparison.Ordinal)
            || signature.Contains("comment", StringComparison.Ordinal);
    }

    private static (Color Background, Color Foreground, Color Border) GetModernDataGridStatusPalette(string text)
    {
        var value = text?.ToLowerInvariant() ?? string.Empty;

        if (value.Contains("закры") || value.Contains("done") || value.Contains("complete") || value.Contains("готов"))
            return (Color.Parse("#DCFCE7"), Color.Parse("#166534"), Color.Parse("#86EFAC"));

        if (value.Contains("ожида") || value.Contains("pending") || value.Contains("сч") || value.Contains("invoice"))
            return (Color.Parse("#FEF3C7"), Color.Parse("#92400E"), Color.Parse("#FCD34D"));

        if (value.Contains("ошиб") || value.Contains("reject") || value.Contains("cancel") || value.Contains("fail"))
            return (Color.Parse("#FEE2E2"), Color.Parse("#991B1B"), Color.Parse("#FCA5A5"));

        return (Color.Parse("#DBEAFE"), Color.Parse("#1D4ED8"), Color.Parse("#93C5FD"));
    }

    private static bool ModernDataGridLooksLikeNumeric(string signature, string? value)
    {
        if (ModernDataGridLooksLikePercentage(signature, value)
            || ModernDataGridLooksLikeCurrency(signature)
            || ModernDataGridLooksLikeRating(signature))
        {
            return true;
        }

        return signature.Contains("id", StringComparison.Ordinal)
            || signature.Contains("count", StringComparison.Ordinal)
            || signature.Contains("number", StringComparison.Ordinal)
            || signature.Contains("qty", StringComparison.Ordinal)
            || signature.Contains("int", StringComparison.Ordinal)
            || signature.Contains("decimal", StringComparison.Ordinal)
            || signature.Contains("double", StringComparison.Ordinal)
            || signature.Contains("float", StringComparison.Ordinal)
            || TryParseModernDataGridNumber(value, out _);
    }

    private static string BuildModernDataGridRatingStars(string text)
    {
        if (!TryParseModernDataGridNumber(text, out var value))
            value = 4;

        var filled = (int)Math.Round(Math.Clamp(value, 0, 5), MidpointRounding.AwayFromZero);
        return string.Concat(Enumerable.Range(0, 5).Select(index => index < filled ? "★" : "☆"));
    }

    private static string GetCycledPreviewValue(int rowIndex, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return string.Empty;

        var normalizedIndex = Math.Abs(rowIndex) % values.Count;
        return values[normalizedIndex];
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        var mix = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(from.A + ((to.A - from.A) * mix)),
            (byte)Math.Round(from.R + ((to.R - from.R) * mix)),
            (byte)Math.Round(from.G + ((to.G - from.G) * mix)),
            (byte)Math.Round(from.B + ((to.B - from.B) * mix)));
    }

    private Control? TryCreateImageControl(DesignControlModel model)
    {
        var bitmap = TryLoadBitmap(model.ImageSource);
        if (bitmap is null)
            return null;

        return new Image
        {
            Width = model.Width,
            Height = model.Height,
            Source = bitmap,
            Stretch = ParseStretch(model.Stretch),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
    }

    private Bitmap? TryLoadBitmap(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (_imageCache.TryGetValue(source, out var cached))
            return cached;

        try
        {
            Bitmap bitmap;

            if (source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = AssetLoader.Open(new Uri(source));
                bitmap = new Bitmap(stream);
            }
            else if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri) && absoluteUri.IsFile && File.Exists(absoluteUri.LocalPath))
            {
                using var stream = File.OpenRead(absoluteUri.LocalPath);
                bitmap = new Bitmap(stream);
            }
            else if (System.IO.Path.IsPathRooted(source) && File.Exists(source))
            {
                using var stream = File.OpenRead(source);
                bitmap = new Bitmap(stream);
            }
            else
            {
                _imageCache[source] = null;
                return null;
            }

            _imageCache[source] = bitmap;
            return bitmap;
        }
        catch
        {
            _imageCache[source] = null;
            return null;
        }
    }

    private static TextBlock CreatePreviewText(
        string text,
        DesignControlModel model,
        string foreground,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = ParseBrush(foreground, "#0F172A"),
            FontFamily = new FontFamily(model.FontFamily),
            FontSize = model.FontSize,
            FontWeight = ParseFontWeight(model.FontWeight),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment
        };
    }

    private static Thickness UniformThickness(double value)
    {
        return new Thickness(Math.Max(0, value));
    }

    private static CornerRadius UniformCornerRadius(double value)
    {
        return new CornerRadius(Math.Max(0, value));
    }

    private static IBrush ParseBrush(string? value, string fallback)
    {
        try
        {
            return Brush.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value);
        }
        catch
        {
            return Brush.Parse(fallback);
        }
    }

    private static Color ParseColor(string? value, string fallback)
    {
        try
        {
            return Color.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value);
        }
        catch
        {
            return Color.Parse(fallback);
        }
    }

    private static IBrush ContrastBrush(Color color)
    {
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        return luminance > 0.6 ? Brushes.Black : Brushes.White;
    }

    private static bool IsDarkColor(Color color)
    {
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        return luminance < 0.45;
    }

    private static FontWeight ParseFontWeight(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "thin" => FontWeight.Thin,
            "light" => FontWeight.Light,
            "medium" => FontWeight.Medium,
            "semibold" => FontWeight.SemiBold,
            "bold" => FontWeight.Bold,
            "black" => FontWeight.Black,
            _ => FontWeight.Normal
        };
    }

    private static Stretch ParseStretch(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "fill" => Stretch.Fill,
            "uniformtofill" => Stretch.UniformToFill,
            "none" => Stretch.None,
            _ => Stretch.Uniform
        };
    }

    private ContextMenu CreateControlContextMenu(DesignControlModel model)
    {
        MenuItem CreateCommandItem(EditorCommandId commandId, string? fallbackHeader = null)
        {
            var command = VM.GetEditorCommand(commandId);
            var header = fallbackHeader ?? command?.Title ?? commandId.ToString();
            if (command is not null && !string.IsNullOrWhiteSpace(command.Shortcut))
                header = $"{header}\t{command.Shortcut}";

            var item = new MenuItem
            {
                Header = header,
                Command = command
            };
            ToolTip.SetTip(item, command?.Hint);
            return item;
        }

        var duplicateItem = CreateCommandItem(EditorCommandId.Duplicate, "Дублировать");
        var lockItem = CreateCommandItem(EditorCommandId.Lock, "Заблокировать");
        var unlockItem = CreateCommandItem(EditorCommandId.Unlock, "Разблокировать");
        var deleteItem = CreateCommandItem(EditorCommandId.Delete, "Удалить");
        var alignLeftItem = CreateCommandItem(EditorCommandId.AlignLeft, "По левому краю");
        var alignTopItem = CreateCommandItem(EditorCommandId.AlignTop, "По верхнему краю");
        var alignRightItem = CreateCommandItem(EditorCommandId.AlignRight, "По правому краю");
        var alignCenterItem = CreateCommandItem(EditorCommandId.AlignCenter, "По центру");
        var alignBottomItem = CreateCommandItem(EditorCommandId.AlignBottom, "По нижнему краю");

        var alignMiddleItem = new MenuItem
        {
            Header = "По середине",
            Command = VM.AlignSelectionMiddleCommand
        };

        var distributeHorizontalItem = CreateCommandItem(EditorCommandId.DistributeHorizontal, "Распределить по горизонтали");
        var distributeVerticalItem = CreateCommandItem(EditorCommandId.DistributeVertical, "Распределить по вертикали");

        var matchWidthItem = new MenuItem
        {
            Header = "Одинаковая ширина",
            Command = VM.MatchSelectionWidthCommand
        };

        var matchHeightItem = new MenuItem
        {
            Header = "Одинаковая высота",
            Command = VM.MatchSelectionHeightCommand
        };

        var matchSizeItem = new MenuItem
        {
            Header = "Одинаковый размер",
            Command = VM.MatchSelectionSizeCommand
        };

        var copyStyleItem = new MenuItem
        {
            Header = "Копировать стиль",
            Command = VM.CopyStyleCommand
        };

        var pasteStyleItem = new MenuItem
        {
            Header = "Вставить стиль",
            Command = VM.PasteStyleCommand
        };

        var bringToFrontItem = CreateCommandItem(EditorCommandId.BringToFront, "На передний план");
        var sendToBackItem = CreateCommandItem(EditorCommandId.SendToBack, "На задний план");

        var wrapInContainerItem = new MenuItem
        {
            Header = "Обернуть в контейнер"
        };
        wrapInContainerItem.Click += (_, _) => VM.WrapSelectionInContainer();

        var alignMenu = new MenuItem
        {
            Header = "Выровнять",
            ItemsSource = new object[]
            {
                alignLeftItem,
                alignTopItem,
                alignRightItem,
                alignBottomItem,
                alignCenterItem,
                alignMiddleItem
            }
        };

        var distributeMenu = new MenuItem
        {
            Header = "Распределить",
            ItemsSource = new object[]
            {
                distributeHorizontalItem,
                distributeVerticalItem
            }
        };

        var sizeMenu = new MenuItem
        {
            Header = "Размер",
            ItemsSource = new object[]
            {
                matchWidthItem,
                matchHeightItem,
                matchSizeItem
            }
        };

        var styleMenu = new MenuItem
        {
            Header = "Стиль",
            ItemsSource = new object[]
            {
                copyStyleItem,
                pasteStyleItem
            }
        };

        var menuItems = new List<object>();
        if (VM.SupportsText(model))
        {
            var editableProperty = GetInlineCanvasEditableProperty(model);
            var editTextItem = new MenuItem
            {
                Header = $"Изменить: {GetInlineCanvasPropertyTitle(model, editableProperty)}",
                IsEnabled = !model.IsLocked
            };
            editTextItem.Click += (_, _) => OpenInlineCanvasEditor(model, editableProperty);
            menuItems.Add(editTextItem);
            menuItems.Add(new Separator());
        }

        menuItems.Add(duplicateItem);
        menuItems.Add(lockItem);
        menuItems.Add(unlockItem);
        menuItems.Add(deleteItem);
        menuItems.Add(new Separator());
        menuItems.Add(alignMenu);
        menuItems.Add(distributeMenu);
        menuItems.Add(sizeMenu);
        menuItems.Add(styleMenu);
        menuItems.Add(bringToFrontItem);
        menuItems.Add(sendToBackItem);
        menuItems.Add(new Separator());
        menuItems.Add(wrapInContainerItem);

        var contextMenu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = menuItems
        };

        contextMenu.Opened += (_, _) =>
        {
            VM.RefreshEditorCommands();
            var selectedRootCount = VM.GetVisibleEditableSelectedRootControls().Count;
            var canArrange = selectedRootCount > 1;
            var canDistribute = selectedRootCount > 2;
            lockItem.IsEnabled = VM.CanLockSelected;
            unlockItem.IsEnabled = VM.CanUnlockSelected;
            alignMenu.IsEnabled = canArrange;
            alignLeftItem.IsEnabled = canArrange;
            alignTopItem.IsEnabled = canArrange;
            alignRightItem.IsEnabled = canArrange;
            alignBottomItem.IsEnabled = canArrange;
            alignCenterItem.IsEnabled = canArrange;
            alignMiddleItem.IsEnabled = canArrange;
            distributeMenu.IsEnabled = canDistribute;
            distributeHorizontalItem.IsEnabled = canDistribute;
            distributeVerticalItem.IsEnabled = canDistribute;
            sizeMenu.IsEnabled = canArrange;
            matchWidthItem.IsEnabled = canArrange;
            matchHeightItem.IsEnabled = canArrange;
            matchSizeItem.IsEnabled = canArrange;
            copyStyleItem.IsEnabled = VM.CanCopyStyle;
            pasteStyleItem.IsEnabled = VM.CanPasteStyle;
            wrapInContainerItem.IsEnabled = VM.CanWrapSelectionInContainer();
        };

        return contextMenu;
    }

    private async void BeginInlineCanvasInteraction(DesignControlModel model)
    {
        if (VM.IsUserPreviewMode)
            return;

        if (model.IsLocked)
            return;

        if (model.Type == DesignerControlTypes.Image)
        {
            CloseInlineCanvasEditor(commitChanges: true);
            await PickImageForControlAsync(model);
            return;
        }

        if (!VM.SupportsText(model))
            return;

        var propertyName = GetInlineCanvasEditableProperty(model);
        OpenInlineCanvasEditor(model, propertyName);
    }

    private void OpenInlineCanvasEditor(DesignControlModel model, string propertyName)
    {
        CloseInlineCanvasEditor(commitChanges: true);

        if (!ReferenceEquals(VM.SelectedControl, model))
            VM.SelectSingleControl(model);

        var frame = GetInlineCanvasEditorFrame(model, propertyName);
        var isMultiline = model.Type is DesignerControlTypes.TextBlock or DesignerControlTypes.Border;
        var editor = new TextBox
        {
            Width = frame.Width,
            Height = frame.Height,
            MinWidth = Math.Min(120, frame.Width),
            MinHeight = 30,
            Text = GetInlineCanvasText(model, propertyName),
            AcceptsReturn = isMultiline,
            TextWrapping = isMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontFamily = new FontFamily(model.FontFamily),
            FontSize = Math.Max(11, model.FontSize),
            FontWeight = ParseFontWeight(model.FontWeight),
            Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Foreground = ParseBrush(model.Foreground, "#0F172A"),
            BorderBrush = new SolidColorBrush(Color.Parse("#2563EB")),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 6),
            Tag = model,
            Watermark = propertyName == nameof(DesignControlModel.PlaceholderText) ? "Введите placeholder..." : "Введите текст..."
        };

        editor.Watermark = $"Введите {GetInlineCanvasPropertyTitle(model, propertyName).ToLowerInvariant()}...";
        editor.KeyDown += InlineCanvasEditor_KeyDown;
        editor.LostFocus += InlineCanvasEditor_LostFocus;

        _inlineCanvasEditor = editor;
        _inlineCanvasEditingModel = model;
        _inlineCanvasEditingProperty = propertyName;

        Canvas.SetLeft(editor, frame.X);
        Canvas.SetTop(editor, frame.Y);
        DesignSurfaceHost.Children.Add(editor);
        editor.Focus();
        editor.SelectAll();
    }

    private void InlineCanvasEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || !ReferenceEquals(textBox, _inlineCanvasEditor))
            return;

        var isMultiline = textBox.AcceptsReturn;
        if (e.Key == Key.Escape)
        {
            CloseInlineCanvasEditor(commitChanges: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && (!isMultiline || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            CloseInlineCanvasEditor(commitChanges: true);
            e.Handled = true;
        }
    }

    private void InlineCanvasEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || !ReferenceEquals(textBox, _inlineCanvasEditor))
            return;

        CloseInlineCanvasEditor(commitChanges: true);
    }

    private void CloseInlineCanvasEditor(bool commitChanges)
    {
        if (_isClosingInlineCanvasEditor || _inlineCanvasEditor is null)
            return;

        _isClosingInlineCanvasEditor = true;
        try
        {
            var editor = _inlineCanvasEditor;
            var model = _inlineCanvasEditingModel;
            var propertyName = _inlineCanvasEditingProperty;
            var text = editor.Text ?? string.Empty;

            editor.KeyDown -= InlineCanvasEditor_KeyDown;
            editor.LostFocus -= InlineCanvasEditor_LostFocus;

            if (DesignSurfaceHost.Children.Contains(editor))
                DesignSurfaceHost.Children.Remove(editor);

            _inlineCanvasEditor = null;
            _inlineCanvasEditingModel = null;
            _inlineCanvasEditingProperty = null;

            if (commitChanges && model is not null && !string.IsNullOrWhiteSpace(propertyName))
            {
                ApplyInlineCanvasTextValue(model, propertyName, text);
                RefreshFromPropertyPanel();
                VM.StatusText = $"Изменено поле «{GetInlineCanvasPropertyTitle(model, propertyName)}» у {model.Name}.";
                VM.StatusText = propertyName == nameof(DesignControlModel.PlaceholderText)
                    ? $"Изменен placeholder у {model.Name}."
                    : $"Изменен текст у {model.Name}.";
            }
        }
        finally
        {
            _isClosingInlineCanvasEditor = false;
        }
    }

    private void ApplyInlineCanvasTextValue(DesignControlModel model, string propertyName, string value)
    {
        switch (propertyName)
        {
            case nameof(DesignControlModel.PlaceholderText):
                model.PlaceholderText = value;
                break;
            default:
                model.Text = value;
                break;
        }
    }

    private static string GetInlineCanvasEditableProperty(DesignControlModel model)
    {
        if (model.Type == DesignerControlTypes.TextBox && string.IsNullOrWhiteSpace(model.Text))
            return nameof(DesignControlModel.PlaceholderText);

        return nameof(DesignControlModel.Text);
    }

    private static string GetInlineCanvasText(DesignControlModel model, string propertyName)
    {
        return propertyName == nameof(DesignControlModel.PlaceholderText)
            ? model.PlaceholderText
            : model.Text;
    }

    private string GetInlineCanvasPropertyTitle(DesignControlModel model, string propertyName)
    {
        var fallback = propertyName == nameof(DesignControlModel.PlaceholderText)
            ? "placeholder"
            : "текст";

        return VM.GetPropertyDisplayTitle(model, propertyName, fallback);
    }

    private (double X, double Y, double Width, double Height) GetInlineCanvasEditorFrame(DesignControlModel model, string propertyName)
    {
        var absolute = VM.GetAbsolutePosition(model);
        var inset = 0d;
        var minHeight = Math.Max(32, model.FontSize + 18);

        switch (model.Type)
        {
            case DesignerControlTypes.TextBox:
            case DesignerControlTypes.Border:
                inset = Math.Max(0, model.Padding);
                break;
            case DesignerControlTypes.CheckBox:
                inset = 28;
                break;
        }

        var x = absolute.X + inset;
        var y = DesignPreviewChromeHeight + absolute.Y + (model.Type == DesignerControlTypes.Border ? inset : 0);
        var width = Math.Max(96, model.Width - (inset * 2));
        var height = model.Type is DesignerControlTypes.TextBlock or DesignerControlTypes.Border
            ? Math.Max(minHeight, model.Height - (propertyName == nameof(DesignControlModel.PlaceholderText) ? 0 : inset * 2))
            : Math.Max(minHeight, model.Height - (propertyName == nameof(DesignControlModel.PlaceholderText) ? 0 : inset));

        return (x, y, width, height);
    }

    private async Task PickImageForControlAsync(DesignControlModel model)
    {
        if (!ReferenceEquals(VM.SelectedControl, model))
            VM.SelectSingleControl(model);

        var selectedPath = await PickImagePathAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        model.ImageSource = selectedPath;
        VM.StatusText = $"Изображение для {model.Name} обновлено.";
    }

    private async Task<string?> PickImagePathAsync()
    {
        if (StorageProvider is null || !StorageProvider.CanOpen)
        {
            VM.StatusText = "Выбор изображения недоступен в этом окружении";
            return null;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите изображение",
                AllowMultiple = false,
                FileTypeFilter = new[] { ImageFileType }
            });

            var file = files.FirstOrDefault();
            if (file is null)
                return null;

            return file.TryGetLocalPath() ?? file.Path.AbsolutePath;
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка выбора изображения: {ex.Message}";
            return null;
        }
    }

    private async Task ImportProjectAssetAsync()
    {
        var path = await PickImagePathAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        VM.RegisterProjectAsset(path);
        VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "Asset imported", System.IO.Path.GetFileName(path));
        await SaveAppSettingsNowAsync();
    }

    private void ProjectExplorerItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ProjectExplorerItemModel item })
            return;

        if (item.CanOpen)
            VM.OpenProjectExplorerItemCommand.Execute(item);
        else if (item.IsFolder)
            item.IsExpanded = !item.IsExpanded;

        e.Handled = true;
    }

    private void ProjectExplorerTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || VM.SelectedProjectExplorerItem is null)
            return;

        VM.OpenProjectExplorerItemCommand.Execute(VM.SelectedProjectExplorerItem);
        e.Handled = true;
    }

    private void Control_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (sender is not Border border || border.Tag is not DesignControlModel model)
            return;

        var pointerProperties = e.GetCurrentPoint(border).Properties;

        if (pointerProperties.IsRightButtonPressed)
        {
            if (_inlineCanvasEditor is not null)
                CloseInlineCanvasEditor(commitChanges: true);

            if (!VM.IsControlSelected(model))
                VM.SelectSingleControl(model);

            _pendingContextMenuControlId = model.Id;
            e.Pointer.Capture(border);
            e.Handled = true;
            return;
        }

        _pendingContextMenuControlId = string.Empty;

        if (_inlineCanvasEditor is not null && !ReferenceEquals(_inlineCanvasEditingModel, model))
            CloseInlineCanvasEditor(commitChanges: true);

        if (!pointerProperties.IsLeftButtonPressed)
            return;

        if (model.IsLocked)
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            BeginInlineCanvasInteraction(model);
            e.Handled = true;
            return;
        }

        ClearGuideOverlay();
        ClearSelectionOverlay();

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+клик не начинает drag сразу, а меняет состав мультивыделения.
            VM.ToggleControlSelection(model);
            RenderDesigner();
            e.Handled = true;
            return;
        }

        if (!VM.IsAbsoluteLayoutParent(model.ParentId))
        {
            VM.SelectSingleControl(model);
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragGestureSessionId = VM.DocumentSessionId;

        if (VM.IsControlSelected(model) && VM.HasMultipleSelection)
            VM.SelectControls(VM.GetSelectedControls(), model);
        else
            VM.SelectSingleControl(model);

        _dragSelectionRoots.Clear();
        _dragSelectionRoots.AddRange(VM.GetEditableSelectedRootControls());
        _dragRootStartPositions.Clear();

        foreach (var root in _dragSelectionRoots)
            _dragRootStartPositions[root.Id] = new Point(root.X, root.Y);

        BuildSnapCandidateSnapshot(_dragSelectionRoots);
        _draggedBorder = border;
        _draggedModel = model;
        _dragStartPointerPosition = GetDesignCanvasPosition(e);
        VM.BeginUndoBatch();
        VM.BeginPropertyGridLiveGesture();

        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void Control_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!_isDragging || _draggedBorder is null || _draggedModel is null || _dragSelectionRoots.Count == 0)
            return;
        if (!string.Equals(_dragGestureSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
        {
            _isDragging = false;
            _dragGestureSessionId = string.Empty;
            _draggedBorder = null;
            _draggedModel = null;
            _dragSelectionRoots.Clear();
            _dragRootStartPositions.Clear();
            ClearGuideOverlay();
            return;
        }

        // Во время drag двигаем не все выделенные элементы подряд,
        // а только корневые, чтобы дочерние не смещались дважды через родителя.
        var position = GetDesignCanvasPosition(e);
        var dx = position.X - _dragStartPointerPosition.X;
        var dy = position.Y - _dragStartPointerPosition.Y;
        var keyModifiers = e.KeyModifiers;

        foreach (var root in _dragSelectionRoots)
        {
            if (!_dragRootStartPositions.TryGetValue(root.Id, out var start))
                continue;

            root.X = ApplyGridSnap(start.X + dx, keyModifiers);
            root.Y = ApplyGridSnap(start.Y + dy, keyModifiers);
            VM.ClampControlToSurface(root);

            if (_wrapperByControlId.TryGetValue(root.Id, out var wrapper))
            {
                Canvas.SetLeft(wrapper, root.X);
                Canvas.SetTop(wrapper, root.Y);
            }
        }

        if (_dragSelectionRoots.Count == 1)
        {
            var active = _dragSelectionRoots[0];
            if (ShouldUseControlSnap(keyModifiers))
                UpdateDragGuides(active);
            else
                ClearGuideOverlay();

            if (_wrapperByControlId.TryGetValue(active.Id, out var wrapper))
            {
                Canvas.SetLeft(wrapper, active.X);
                Canvas.SetTop(wrapper, active.Y);
            }
        }
        else
        {
            ClearGuideOverlay();
            RenderSelectionBounds(_dragSelectionRoots);
        }

        e.Handled = true;
    }

    private void Control_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (sender is Border releasedBorder
            && releasedBorder.Tag is DesignControlModel releasedModel
            && string.Equals(_pendingContextMenuControlId, releasedModel.Id, StringComparison.OrdinalIgnoreCase))
        {
            _pendingContextMenuControlId = string.Empty;
            e.Pointer.Capture(null);
            ClearGuideOverlay();
            _isDragging = false;
            _dragGestureSessionId = string.Empty;
            _draggedBorder = null;
            _draggedModel = null;
            _dragSelectionRoots.Clear();
            _dragRootStartPositions.Clear();
            ClearSnapCandidateSnapshot();
            VM.EndPropertyGridLiveGesture();

            Dispatcher.UIThread.Post(() =>
            {
                var target = _wrapperByControlId.TryGetValue(releasedModel.Id, out var currentWrapper)
                    ? currentWrapper
                    : releasedBorder;

                if (target.ContextMenu is { } contextMenu)
                {
                    contextMenu.Close();
                    contextMenu.Open(target);
                }
            }, DispatcherPriority.Input);

            e.Handled = true;
            return;
        }

        _pendingContextMenuControlId = string.Empty;
        e.Pointer.Capture(null);

        if (!string.Equals(_dragGestureSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
        {
            _isDragging = false;
            _dragGestureSessionId = string.Empty;
            _draggedBorder = null;
            _draggedModel = null;
            _dragSelectionRoots.Clear();
            _dragRootStartPositions.Clear();
            ClearSnapCandidateSnapshot();
            ClearGuideOverlay();
            e.Handled = true;
            return;
        }

        if (_dragSelectionRoots.Count == 1)
        {
            var draggedRoot = _dragSelectionRoots[0];
            var absolutePosition = VM.GetAbsolutePosition(draggedRoot);
            var probeX = absolutePosition.X + (draggedRoot.Width / 2);
            var probeY = absolutePosition.Y + (draggedRoot.Height / 2);
            var targetContainer = VM.FindDeepestContainerAt(probeX, probeY);

            if (targetContainer?.Id == draggedRoot.Id)
                targetContainer = VM.GetControl(targetContainer.ParentId);

            VM.ReparentControl(draggedRoot, targetContainer?.Id, absolutePosition.X, absolutePosition.Y);
        }

        _isDragging = false;
        _dragGestureSessionId = string.Empty;
        ClearGuideOverlay();
        _draggedBorder = null;
        _draggedModel = null;
        _dragSelectionRoots.Clear();
        _dragRootStartPositions.Clear();
        ClearSnapCandidateSnapshot();
        VM.EndPropertyGridLiveGesture();
        VM.CommitUndoBatch();
        RenderDesigner();
    }

    private void ResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (sender is not Border border || border.Tag is not DesignControlModel model)
            return;

        if (model.IsLocked)
            return;

        // При старте ресайза запоминаем исходный размер, а дальше считаем дельту мыши.
        _isResizing = true;
        ClearSelectionOverlay();
        _resizeGestureSessionId = VM.DocumentSessionId;
        _resizingModel = model;
        _resizeStart = GetDesignCanvasPosition(e);
        _startWidth = model.Width;
        _startHeight = model.Height;
        BuildSnapCandidateSnapshot(new[] { model });
        VM.BeginUndoBatch();
        VM.BeginPropertyGridLiveGesture();

        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void ResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!_isResizing || _resizingModel is null)
            return;
        if (!string.Equals(_resizeGestureSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
        {
            _isResizing = false;
            _resizeGestureSessionId = string.Empty;
            _resizingModel = null;
            ClearSnapCandidateSnapshot();
            ClearGuideOverlay();
            return;
        }

        // Ограничиваем размер пределами текущего контейнера,
        // чтобы элемент нельзя было "растянуть" за границы формы или родителя.
        var position = GetDesignCanvasPosition(e);
        var dx = position.X - _resizeStart.X;
        var dy = position.Y - _resizeStart.Y;

        var parent = VM.GetControl(_resizingModel.ParentId);
        var containerWidth = parent?.Width ?? VM.PreviewFormWidth;
        var containerHeight = parent?.Height ?? VM.PreviewFormHeight;
        var maxWidth = Math.Max(40, containerWidth - _resizingModel.X);
        var maxHeight = Math.Max(24, containerHeight - _resizingModel.Y);

        var keyModifiers = e.KeyModifiers;
        _resizingModel.Width = Math.Clamp(ApplyGridSnap(_startWidth + dx, keyModifiers), 40, maxWidth);
        _resizingModel.Height = Math.Clamp(ApplyGridSnap(_startHeight + dy, keyModifiers), 24, maxHeight);

        if (ShouldUseControlSnap(keyModifiers))
            UpdateResizeGuides(_resizingModel);
        else
        {
            ClearGuideOverlay();
            if (VM.IsDistanceHintsEnabled)
                RenderResizeSizeHint(_resizingModel);
        }
    }

    private void ResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!string.Equals(_resizeGestureSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
        {
            _isResizing = false;
            _resizeGestureSessionId = string.Empty;
            _resizingModel = null;
            e.Pointer.Capture(null);
            ClearSnapCandidateSnapshot();
            ClearGuideOverlay();
            e.Handled = true;
            return;
        }

        if (_resizingModel is not null)
        {
            var parent = VM.GetControl(_resizingModel.ParentId);
            var containerWidth = parent?.Width ?? VM.PreviewFormWidth;
            var containerHeight = parent?.Height ?? VM.PreviewFormHeight;
            _resizingModel.Width = Math.Clamp(ApplyGridSnap(_resizingModel.Width, e.KeyModifiers), 40, Math.Max(40, containerWidth - _resizingModel.X));
            _resizingModel.Height = Math.Clamp(ApplyGridSnap(_resizingModel.Height, e.KeyModifiers), 24, Math.Max(24, containerHeight - _resizingModel.Y));
        }

        _isResizing = false;
        _resizeGestureSessionId = string.Empty;
        _resizingModel = null;
        ClearSnapCandidateSnapshot();
        e.Pointer.Capture(null);
        ClearGuideOverlay();
        VM.EndPropertyGridLiveGesture();
        VM.CommitUndoBatch();
        RenderDesigner();
    }

    private void DesignResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        _isResizingDesignSurface = true;
        _resizeGestureSessionId = VM.DocumentSessionId;
        _designResizeStart = GetDesignHostPosition(e);
        _designStartWidth = VM.DesignWidth;
        _designStartHeight = VM.DesignHeight;
        VM.BeginUndoBatch();

        if (sender is InputElement element)
            e.Pointer.Capture(element);

        e.Handled = true;
    }

    private void DesignResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!_isResizingDesignSurface)
            return;
        if (!string.Equals(_resizeGestureSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
        {
            _isResizingDesignSurface = false;
            _resizeGestureSessionId = string.Empty;
            return;
        }

        var current = GetDesignHostPosition(e);
        var dx = current.X - _designResizeStart.X;
        var dy = current.Y - _designResizeStart.Y;

        VM.DesignWidth = Math.Max(300, _designStartWidth + dx);
        VM.DesignHeight = Math.Max(200, _designStartHeight + dy);
    }

    private void DesignResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (VM.IsUserPreviewMode || _isPanningViewport)
            return;

        if (!string.Equals(_resizeGestureSessionId, VM.DocumentSessionId, StringComparison.Ordinal))
        {
            _isResizingDesignSurface = false;
            _resizeGestureSessionId = string.Empty;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        VM.DesignWidth = Math.Max(300, VM.Snap(VM.DesignWidth));
        VM.DesignHeight = Math.Max(200, VM.Snap(VM.DesignHeight));

        _isResizingDesignSurface = false;
        _resizeGestureSessionId = string.Empty;
        e.Pointer.Capture(null);
        VM.CommitUndoBatch();
        RenderDesigner();
    }

    private void LeftDockSplitter_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginDockPanelResize(sender, e, DockPanelResizeKind.Left);
    }

    private void LeftDockSplitter_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingLeftDockPanel)
            return;

        var current = e.GetPosition(this);
        VM.LeftDockPanelWidth = Math.Clamp(_dockPanelResizeStartSize + current.X - _dockPanelResizeStart.X, 220, 420);
        e.Handled = true;
    }

    private void RightDockSplitter_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginDockPanelResize(sender, e, DockPanelResizeKind.Right);
    }

    private void RightDockSplitter_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingRightDockPanel)
            return;

        var current = e.GetPosition(this);
        VM.RightDockPanelWidth = Math.Clamp(_dockPanelResizeStartSize + _dockPanelResizeStart.X - current.X, 280, 560);
        e.Handled = true;
    }

    private void BeginDockPanelResize(object? sender, PointerPressedEventArgs e, DockPanelResizeKind kind)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _isResizingLeftDockPanel = kind == DockPanelResizeKind.Left;
        _isResizingRightDockPanel = kind == DockPanelResizeKind.Right;
        _dockPanelResizeStart = e.GetPosition(this);
        _dockPanelResizeStartSize = kind == DockPanelResizeKind.Left
            ? VM.LeftDockPanelWidth
            : VM.RightDockPanelWidth;

        if (sender is InputElement element)
            e.Pointer.Capture(element);

        e.Handled = true;
    }

    private void DockSplitter_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingLeftDockPanel && !_isResizingRightDockPanel)
            return;

        _isResizingLeftDockPanel = false;
        _isResizingRightDockPanel = false;
        e.Pointer.Capture(null);
        ScheduleSettingsSave();
        e.Handled = true;
    }

    private void DockSplitter_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isResizingLeftDockPanel = false;
        _isResizingRightDockPanel = false;
        ScheduleSettingsSave();
    }

    private void DiagnosticsPaneResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!VM.IsDiagnosticsPaneExpanded)
            return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _isResizingDiagnosticsPane = true;
        _diagnosticsPaneResizeStart = e.GetPosition(this);
        _diagnosticsPaneResizeStartHeight = VM.DiagnosticsPaneHeight;

        if (sender is InputElement element)
            e.Pointer.Capture(element);

        e.Handled = true;
    }

    private void DiagnosticsPaneResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingDiagnosticsPane)
            return;

        var current = e.GetPosition(this);
        var delta = _diagnosticsPaneResizeStart.Y - current.Y;
        var maxHeight = Math.Max(140, Bounds.Height - 180);
        VM.BottomDockPanelHeight = Math.Clamp(_diagnosticsPaneResizeStartHeight + delta, 140, maxHeight);
        e.Handled = true;
    }

    private void DiagnosticsPaneResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingDiagnosticsPane)
            return;

        _isResizingDiagnosticsPane = false;
        e.Pointer.Capture(null);
        ScheduleSettingsSave();
        e.Handled = true;
    }

    private void DiagnosticsPaneResizeHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isResizingDiagnosticsPane = false;
        ScheduleSettingsSave();
    }

    private enum DockPanelResizeKind
    {
        Left,
        Right
    }

    private void RefreshFromPropertyPanel()
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null)
            return;

        VM.ClampControlToSurface(VM.SelectedControl);
    }

    private void MiniMapCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || !MiniMapHost.IsVisible)
            return;

        var point = e.GetCurrentPoint(MiniMapCanvas);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(MiniMapCanvas);
        if (_miniMapViewportBounds.Contains(position))
        {
            _isMiniMapDraggingViewport = true;
            _miniMapDragOffset = position - _miniMapViewportBounds.Position;
            e.Pointer.Capture(MiniMapCanvas);
        }
        else
        {
            NavigateViewportToMiniMapPoint(position);
        }

        e.Handled = true;
    }

    private void MiniMapCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMiniMapDraggingViewport)
            return;

        var position = e.GetPosition(MiniMapCanvas);
        MoveViewportToMiniMapTopLeft(new Point(
            position.X - _miniMapDragOffset.X,
            position.Y - _miniMapDragOffset.Y));
        e.Handled = true;
    }

    private void MiniMapCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isMiniMapDraggingViewport)
            return;

        _isMiniMapDraggingViewport = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ZoomOutButton_Click(object? sender, RoutedEventArgs e)
    {
        SetSurfaceZoom(GetAdjacentSurfaceZoom(zoomIn: false));
    }

    private void ResetZoomButton_Click(object? sender, RoutedEventArgs e)
    {
        SetSurfaceZoom(1.0);
    }

    private void ZoomInButton_Click(object? sender, RoutedEventArgs e)
    {
        SetSurfaceZoom(GetAdjacentSurfaceZoom(zoomIn: true));
    }

    private void ZoomPresetComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingZoomPresetSelection || sender is not ComboBox comboBox)
            return;

        var value = ParseZoomPercent(comboBox.SelectedItem);
        if (value.HasValue)
            SetSurfaceZoom(value.Value);
    }

    private IReadOnlyList<DesignControlModel> GetSelectionTargets(string propertyName, bool rootsOnly = false)
    {
        var targets = rootsOnly ? VM.GetSelectedRootControls() : VM.GetSelectedControls();
        return targets
            .Where(control => VM.SupportsProperty(control, propertyName))
            .ToList();
    }

    private async void LaunchPreviewButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await LaunchPreviewAsync();
    }

    private void OpenHelpButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenHelpWindow();
    }

    private void OpenHelpWindow()
    {
        if (_helpWindow is { IsVisible: true })
        {
            _helpWindow.Activate();
            return;
        }

        var helpWindow = new HelpWindow();
        helpWindow.Closed += HelpWindow_Closed;
        _helpWindow = helpWindow;
        helpWindow.Show(this);
        helpWindow.Activate();

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.StatusText = "Открыта подробная справка по конструктору форм.";
    }

    private async Task LaunchPreviewAsync()
    {
        if (VM.IsBusy)
            return;

        try
        {
            VM.BeginBusy("Подготавливаем предпросмотр", "Собираем окно запуска и визуальные элементы формы.");
            await Task.Delay(160);

            var snapshot = VM.CreatePreviewDocumentSnapshot();
            var projectForms = VM.CreatePreviewProjectFormsSnapshot();

            if (_launchPreviewWindow is not null)
            {
                _launchPreviewWindow.Close();
                _launchPreviewWindow = null;
            }

            var previewWindow = new PreviewWindow(snapshot, VM.Registry, projectForms, VM.ActiveFormDocument?.Id ?? string.Empty);
            previewWindow.Closed += LaunchPreviewWindow_Closed;
            _launchPreviewWindow = previewWindow;
            previewWindow.Show();
            VM.EndBusy("Открыт предпросмотр запуска.");
        }
        catch (Exception ex)
        {
            VM.EndBusy();
            VM.StatusText = $"Ошибка открытия предпросмотра: {ex.Message}";
        }
    }

    private async Task EnsureInteractivePreviewDataAsync()
    {
        await Task.CompletedTask;
    }

    private IReadOnlyList<Dictionary<string, string>> GetCachedInteractivePreviewRows(string? bindingSourceId)
    {
        if (string.IsNullOrWhiteSpace(bindingSourceId))
            return Array.Empty<Dictionary<string, string>>();

        return _sqlPreviewRowsBySourceId.TryGetValue(bindingSourceId, out var cached)
            ? cached.Rows
            : Array.Empty<Dictionary<string, string>>();
    }

    private System.Collections.IEnumerable? ResolvePreviewBindingItems(string bindingSourceId)
    {
        var source = GetActiveBindingSource(bindingSourceId);
        if (source is null)
            return null;

        return BindingPreviewItemsBuilder.BuildSampleItems(source);
    }

    private void LaunchPreviewWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is PreviewWindow previewWindow)
            previewWindow.Closed -= LaunchPreviewWindow_Closed;

        if (ReferenceEquals(_launchPreviewWindow, sender))
            _launchPreviewWindow = null;
    }

    private void HelpWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is HelpWindow helpWindow)
            helpWindow.Closed -= HelpWindow_Closed;

        if (ReferenceEquals(_helpWindow, sender))
            _helpWindow = null;
    }

    private void ApplyStringPropertyToSelection(string propertyName, string value)
    {
        foreach (var control in GetSelectionTargets(propertyName))
        {
            switch (propertyName)
            {
                case nameof(DesignControlModel.Text):
                    control.Text = value;
                    break;
                case nameof(DesignControlModel.PlaceholderText):
                    control.PlaceholderText = value;
                    break;
                case nameof(DesignControlModel.ImageSource):
                    control.ImageSource = value;
                    break;
                case nameof(DesignControlModel.Background):
                    control.Background = value;
                    break;
                case nameof(DesignControlModel.Foreground):
                    control.Foreground = value;
                    break;
                case nameof(DesignControlModel.BorderBrush):
                    control.BorderBrush = value;
                    break;
                case nameof(DesignControlModel.DataGridGlowColor):
                    control.DataGridGlowColor = value;
                    break;
                case nameof(DesignControlModel.DataGridHeaderBackground):
                    control.DataGridHeaderBackground = value;
                    break;
                case nameof(DesignControlModel.DataGridHeaderForeground):
                    control.DataGridHeaderForeground = value;
                    break;
                case nameof(DesignControlModel.DataGridRowBackground):
                    control.DataGridRowBackground = value;
                    break;
                case nameof(DesignControlModel.DataGridAlternateRowBackground):
                    control.DataGridAlternateRowBackground = value;
                    break;
                case nameof(DesignControlModel.DataGridRowForeground):
                    control.DataGridRowForeground = value;
                    break;
                case nameof(DesignControlModel.DataGridHoverRowBackground):
                    control.DataGridHoverRowBackground = value;
                    break;
                case nameof(DesignControlModel.DataGridSelectedRowBackground):
                    control.DataGridSelectedRowBackground = value;
                    break;
                case nameof(DesignControlModel.DataGridSelectedRowForeground):
                    control.DataGridSelectedRowForeground = value;
                    break;
                case nameof(DesignControlModel.DataGridGridLineBrush):
                    control.DataGridGridLineBrush = value;
                    break;
                case nameof(DesignControlModel.DataGridOuterBorderBrush):
                    control.DataGridOuterBorderBrush = value;
                    break;
                case nameof(DesignControlModel.DataGridTextAlignment):
                    control.DataGridTextAlignment = DesignControlModel.NormalizeDataGridTextAlignment(value);
                    break;
                case nameof(DesignControlModel.DataGridHeaderFontWeight):
                    control.DataGridHeaderFontWeight = value;
                    break;
                case nameof(DesignControlModel.DataGridRowFontWeight):
                    control.DataGridRowFontWeight = value;
                    break;
                case nameof(DesignControlModel.FontFamily):
                    control.FontFamily = value;
                    break;
                case nameof(DesignControlModel.FontWeight):
                    control.FontWeight = value;
                    break;
                case nameof(DesignControlModel.Stretch):
                    control.Stretch = value;
                    break;
                case nameof(DesignControlModel.LayoutOrientation):
                    control.LayoutOrientation = value;
                    break;
            }
        }
    }

    private void ApplyDoublePropertyToSelection(string propertyName, double value)
    {
        foreach (var control in GetSelectionTargets(propertyName))
        {
            switch (propertyName)
            {
                case nameof(DesignControlModel.X):
                    control.X = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.Y):
                    control.Y = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.Width):
                    control.Width = Math.Max(40, value);
                    break;
                case nameof(DesignControlModel.Height):
                    control.Height = Math.Max(24, value);
                    break;
                case nameof(DesignControlModel.Opacity):
                    control.Opacity = Math.Clamp(value, 0, 1);
                    break;
                case nameof(DesignControlModel.BorderThickness):
                    control.BorderThickness = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.CornerRadius):
                    control.CornerRadius = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.FontSize):
                    control.FontSize = Math.Max(8, value);
                    break;
                case nameof(DesignControlModel.Padding):
                    control.Padding = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.DataGridHeaderFontSize):
                    control.DataGridHeaderFontSize = Math.Max(8, value);
                    break;
                case nameof(DesignControlModel.DataGridRowFontSize):
                    control.DataGridRowFontSize = Math.Max(8, value);
                    break;
                case nameof(DesignControlModel.DataGridHeaderHeight):
                    control.DataGridHeaderHeight = Math.Max(24, value);
                    break;
                case nameof(DesignControlModel.DataGridRowHeight):
                    control.DataGridRowHeight = Math.Max(18, value);
                    break;
                case nameof(DesignControlModel.DataGridCellPadding):
                    control.DataGridCellPadding = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.LayoutSpacing):
                    control.LayoutSpacing = Math.Max(0, value);
                    break;
            }

            VM.ClampControlToSurface(control);
        }
    }

    private void ApplyIntPropertyToSelection(string propertyName, int value)
    {
        foreach (var control in GetSelectionTargets(propertyName))
        {
            switch (propertyName)
            {
                case nameof(DesignControlModel.Columns):
                    control.Columns = Math.Max(1, value);
                    break;
                case nameof(DesignControlModel.Rows):
                    control.Rows = Math.Max(1, value);
                    break;
            }
        }
    }

    private void ApplyBoolPropertyToSelection(string propertyName, bool value)
    {
        foreach (var control in GetSelectionTargets(propertyName))
        {
            switch (propertyName)
            {
                case nameof(DesignControlModel.IsVisible):
                    control.IsVisible = value;
                    break;
                case nameof(DesignControlModel.AutoGenerateColumns):
                    control.AutoGenerateColumns = value;
                    break;
                case nameof(DesignControlModel.DataGridShowHeader):
                    control.DataGridShowHeader = value;
                    break;
                case nameof(DesignControlModel.DataGridShowRowLines):
                    control.DataGridShowRowLines = value;
                    break;
                case nameof(DesignControlModel.DataGridShowColumnLines):
                    control.DataGridShowColumnLines = value;
                    break;
                case nameof(DesignControlModel.DataGridShowAlternatingRows):
                    control.DataGridShowAlternatingRows = value;
                    break;
                case nameof(DesignControlModel.ShowFilterRow):
                    control.ShowFilterRow = value;
                    break;
                case nameof(DesignControlModel.ShowGroupPanel):
                    control.ShowGroupPanel = value;
                    break;
                case nameof(DesignControlModel.AllowGrouping):
                    control.AllowGrouping = value;
                    break;
                case nameof(DesignControlModel.ShowFooter):
                    control.ShowFooter = value;
                    break;
            }
        }
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseInt(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
    }

    private string? GetCurrentColorValue(string propertyName)
    {
        if (propertyName == nameof(MainWindowViewModel.SurfaceBackground))
            return VM.SurfaceBackground;

        if (VM.SelectedControl is null)
            return null;

        if (VM.IsSelectedCustomDescriptorProperty(propertyName))
            return VM.GetSelectedCustomPropertyString(propertyName);

        return propertyName switch
        {
            nameof(DesignControlModel.Background) => VM.SelectedControl.Background,
            nameof(DesignControlModel.Foreground) => VM.SelectedControl.Foreground,
            nameof(DesignControlModel.BorderBrush) => VM.SelectedControl.BorderBrush,
            nameof(DesignControlModel.DataGridGlowColor) => VM.SelectedControl.DataGridGlowColor,
            nameof(DesignControlModel.DataGridHeaderBackground) => VM.SelectedControl.DataGridHeaderBackground,
            nameof(DesignControlModel.DataGridHeaderForeground) => VM.SelectedControl.DataGridHeaderForeground,
            nameof(DesignControlModel.DataGridRowBackground) => VM.SelectedControl.DataGridRowBackground,
            nameof(DesignControlModel.DataGridAlternateRowBackground) => VM.SelectedControl.DataGridAlternateRowBackground,
            nameof(DesignControlModel.DataGridRowForeground) => VM.SelectedControl.DataGridRowForeground,
            nameof(DesignControlModel.DataGridHoverRowBackground) => VM.SelectedControl.DataGridHoverRowBackground,
            nameof(DesignControlModel.DataGridSelectedRowBackground) => VM.SelectedControl.DataGridSelectedRowBackground,
            nameof(DesignControlModel.DataGridSelectedRowForeground) => VM.SelectedControl.DataGridSelectedRowForeground,
            nameof(DesignControlModel.DataGridGridLineBrush) => VM.SelectedControl.DataGridGridLineBrush,
            nameof(DesignControlModel.DataGridOuterBorderBrush) => VM.SelectedControl.DataGridOuterBorderBrush,
            _ => null
        };
    }

    private string GetColorFallback(string propertyName)
    {
        if (VM.IsSelectedCustomDescriptorProperty(propertyName))
            return VM.GetSelectedCustomPropertyColorFallback(propertyName);

        return propertyName switch
        {
            nameof(MainWindowViewModel.SurfaceBackground) => "#FFFFFF",
            nameof(DesignControlModel.Background) => "#FFFFFF",
            nameof(DesignControlModel.Foreground) => "#0F172A",
            nameof(DesignControlModel.BorderBrush) => "#94A3B8",
            nameof(DesignControlModel.DataGridGlowColor) => "#60A5FA",
            nameof(DesignControlModel.DataGridHeaderBackground) => "#E2E8F0",
            nameof(DesignControlModel.DataGridHeaderForeground) => "#0F172A",
            nameof(DesignControlModel.DataGridRowBackground) => "#FFFFFF",
            nameof(DesignControlModel.DataGridAlternateRowBackground) => "#F8FAFC",
            nameof(DesignControlModel.DataGridRowForeground) => "#0F172A",
            nameof(DesignControlModel.DataGridHoverRowBackground) => "#EFF6FF",
            nameof(DesignControlModel.DataGridSelectedRowBackground) => "#DBEAFE",
            nameof(DesignControlModel.DataGridSelectedRowForeground) => "#0F172A",
            nameof(DesignControlModel.DataGridGridLineBrush) => "#D7E2EE",
            nameof(DesignControlModel.DataGridOuterBorderBrush) => "#60A5FA",
            _ => "#FFFFFF"
        };
    }

    private void ApplyColorValue(string propertyName, string value)
    {
        if (propertyName == nameof(MainWindowViewModel.SurfaceBackground))
        {
            VM.SurfaceBackground = value;
            return;
        }

        if (VM.IsSelectedCustomDescriptorProperty(propertyName))
        {
            var descriptor = VM.GetDescriptor(VM.SelectedControl?.Type)
                .Properties
                .FirstOrDefault(property => string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(property.BuiltInPropertyName));

            if (descriptor is not null)
                VM.SetDescriptorCustomPropertyFromString(descriptor, value);

            return;
        }

        ApplyStringPropertyToSelection(propertyName, value);
    }

    private static string FormatSolidColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private async Task<string?> ShowColorPickerFlyoutAsync(Control target, string propertyName, string initialValue)
    {
        _activeColorFlyout?.Hide();

        var colorView = new ColorView
        {
            Color = ParseColor(initialValue, GetColorFallback(propertyName)),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsHexInputVisible = true,
            Width = 360,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var applyButton = new Button
        {
            Content = "Применить",
            MinWidth = 96,
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 96,
            IsCancel = true
        };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                applyButton
            }
        };

        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new Border
            {
                Width = 392,
                Padding = new Thickness(16),
                Background = Brush.Parse("#FFFFFF"),
                BorderBrush = Brush.Parse("#D7E2EE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        colorView,
                        buttonsPanel
                    }
                }
            }
        };

        _activeColorFlyout = flyout;

        var completion = new TaskCompletionSource<string?>();

        void Complete(string? value)
        {
            completion.TrySetResult(value);
            flyout.Hide();
        }

        applyButton.Click += (_, _) => Complete(FormatSolidColor(colorView.Color));
        cancelButton.Click += (_, _) => Complete(null);
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeColorFlyout, flyout))
                _activeColorFlyout = null;

            completion.TrySetResult(null);
        };

        flyout.ShowAt(target);
        return await completion.Task;
    }

    private void SelectedNumericTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox textBox || textBox.Tag is not string propertyName)
            return;

        if (!textBox.IsFocused)
            return;

        if (!TryParseDouble(textBox.Text, out var value))
            return;

        if (propertyName is nameof(DesignControlModel.X) or nameof(DesignControlModel.Y))
        {
            switch (propertyName)
            {
                case nameof(DesignControlModel.X):
                    VM.SelectedControl.X = Math.Max(0, value);
                    break;
                case nameof(DesignControlModel.Y):
                    VM.SelectedControl.Y = Math.Max(0, value);
                    break;
            }

            VM.ClampControlToSurface(VM.SelectedControl);
        }
        else
        {
            ApplyDoublePropertyToSelection(propertyName, value);
        }

        RefreshFromPropertyPanel();
    }

    private void SelectedIntegerTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox textBox || textBox.Tag is not string propertyName)
            return;

        if (!textBox.IsFocused)
            return;

        if (!TryParseInt(textBox.Text, out var value))
            return;

        ApplyIntPropertyToSelection(propertyName, value);

        RefreshFromPropertyPanel();
    }

    private void SelectedTextPropertyTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox textBox || textBox.Tag is not string propertyName)
            return;

        if (!textBox.IsFocused)
            return;

        var value = textBox.Text ?? string.Empty;
        ApplyStringPropertyToSelection(propertyName, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not ComboBox comboBox || comboBox.Tag is not string propertyName)
            return;

        if (!comboBox.IsKeyboardFocusWithin && !comboBox.IsDropDownOpen)
            return;

        if (comboBox.SelectedItem is not string value)
            return;

        ApplyStringPropertyToSelection(propertyName, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedCheckBox_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not CheckBox checkBox || checkBox.Tag is not string propertyName)
            return;

        ApplyBoolPropertyToSelection(propertyName, checkBox.IsChecked == true);
        RefreshFromPropertyPanel();
    }

    private void PropertyGridTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PropertyGridRowViewModel row })
            return;

        row.CommitValue();
        RefreshFromPropertyPanel();
    }

    private void PropertyGridTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control { DataContext: PropertyGridRowViewModel row })
            return;

        row.CommitValue();
        RefreshFromPropertyPanel();
        e.Handled = true;
    }

    private async void PropertyGridColorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not PropertyGridRowViewModel row)
            return;

        var selectedColor = await ShowColorPickerFlyoutAsync(button, row.Key, row.Value);
        if (string.IsNullOrWhiteSpace(selectedColor))
            return;

        row.Value = selectedColor;
        row.CommitValue();
        RefreshFromPropertyPanel();
    }

    private void PropertyGridActionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PropertyGridRowViewModel row })
            return;

        if (string.Equals(row.Key, "Columns", StringComparison.OrdinalIgnoreCase))
        {
            OpenDataGridColumnEditorButton_Click(sender, e);
            return;
        }

        VM.StatusText = $"Action editor is not available for {row.Label}.";
    }

    private void SurfaceNumericTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || sender is not TextBox textBox || textBox.Tag is not string propertyName)
            return;

        switch (propertyName)
        {
            case nameof(MainWindowViewModel.DesignWidth):
                if (TryParseDouble(textBox.Text, out var width))
                    VM.DesignWidth = Math.Max(300, width);
                break;

            case nameof(MainWindowViewModel.DesignHeight):
                if (TryParseDouble(textBox.Text, out var height))
                    VM.DesignHeight = Math.Max(200, height);
                break;

            case nameof(MainWindowViewModel.SnapStep):
                if (TryParseInt(textBox.Text, out var snap))
                    VM.SnapStep = Math.Max(1, snap);
                break;

            case nameof(MainWindowViewModel.SnapThreshold):
                if (TryParseInt(textBox.Text, out var threshold))
                    VM.SnapThreshold = Math.Clamp(threshold, 1, 40);
                break;

            case nameof(MainWindowViewModel.SurfaceLayoutSpacing):
                if (TryParseDouble(textBox.Text, out var spacing))
                    VM.SurfaceLayoutSpacing = Math.Max(0, spacing);
                break;

            case nameof(MainWindowViewModel.SurfaceLayoutColumns):
                if (TryParseInt(textBox.Text, out var columns))
                    VM.SurfaceLayoutColumns = Math.Max(1, columns);
                break;

            case nameof(MainWindowViewModel.SurfaceLayoutRows):
                if (TryParseInt(textBox.Text, out var rows))
                    VM.SurfaceLayoutRows = Math.Max(1, rows);
                break;
        }
    }

    private void AddBindingSourceButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.AddBindingSource();
    }

    private void AddSqlBindingSourceButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.AddSqlBindingSource();
    }

    private async void ImportBindingSourcesFromAssemblyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (StorageProvider is null || !StorageProvider.CanOpen)
        {
            VM.StatusText = "Выбор сборки недоступен в этом окружении";
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импортировать BindingSource из сборки",
                AllowMultiple = false,
                FileTypeFilter = new[] { AssemblyFileType }
            });

            var file = files.FirstOrDefault();
            var localPath = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
                return;

            VM.BeginBusy("Импортируем BindingSource", "Читаем типы из выбранной сборки и подготавливаем источники данных.");
            await Task.Delay(120);
            var importedCount = VM.ImportBindingSourcesFromAssembly(localPath);
            VM.EndBusy();
            if (importedCount > 0)
                VM.StatusText = $"Импортировано источников: {importedCount} из {System.IO.Path.GetFileName(localPath)}";
        }
        catch (Exception ex)
        {
            VM.EndBusy();
            VM.StatusText = $"Ошибка импорта сборки: {ex.Message}";
        }
    }

    private async void InstallPluginButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (StorageProvider is null || !StorageProvider.CanOpen)
        {
            VM.StatusText = "Выбор plugin DLL недоступен в этом окружении";
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Установить plugin DLL",
                AllowMultiple = false,
                FileTypeFilter = new[] { AssemblyFileType }
            });

            var file = files.FirstOrDefault();
            var localPath = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
                return;

            VM.BeginBusy("Устанавливаем plugin", "Копируем DLL в папку Plugins и подключаем новые контролы к конструктору.");
            await Task.Delay(120);

            var installFolder = InstallPluginPackage(localPath);
            var loader = new PluginLoader(new TraceDesignerLogger());
            loader.LoadFromFolder(installFolder, VM.Registry);
            VM.RefreshRegistryBackedCollections();
            VM.EndBusy();

            VM.StatusText = $"Plugin установлен: {System.IO.Path.GetFileName(localPath)}";
        }
        catch (Exception ex)
        {
            VM.EndBusy();
            VM.StatusText = $"Ошибка установки plugin: {ex.Message}";
        }
    }

    private static string InstallPluginPackage(string pluginAssemblyPath)
    {
        var sourceAssemblyPath = System.IO.Path.GetFullPath(pluginAssemblyPath);
        var sourceFolder = System.IO.Path.GetDirectoryName(sourceAssemblyPath)
            ?? throw new InvalidOperationException("Не удалось определить папку выбранной DLL.");

        var pluginsRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Plugins");
        Directory.CreateDirectory(pluginsRoot);

        var sourceFolderFull = System.IO.Path.GetFullPath(sourceFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var pluginsRootFull = System.IO.Path.GetFullPath(pluginsRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        if (sourceFolderFull.StartsWith(pluginsRootFull, StringComparison.OrdinalIgnoreCase))
            return sourceFolderFull;

        var targetFolder = System.IO.Path.Combine(pluginsRoot, System.IO.Path.GetFileNameWithoutExtension(sourceAssemblyPath));
        Directory.CreateDirectory(targetFolder);

        foreach (var file in GetPluginPackageFiles(sourceFolderFull, sourceAssemblyPath))
        {
            var destination = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }

        return targetFolder;
    }

    private static IEnumerable<string> GetPluginPackageFiles(string sourceFolder, string selectedAssemblyPath)
    {
        var selectedAssemblyName = System.IO.Path.GetFileNameWithoutExtension(selectedAssemblyPath);
        var isHostOutputFolder = string.Equals(
            sourceFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            System.IO.Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        var files = Directory.GetFiles(sourceFolder)
            .Where(ShouldCopyPluginArtifact)
            .ToList();

        if (!isHostOutputFolder)
            return files;

        return files.Where(file =>
            string.Equals(System.IO.Path.GetFileNameWithoutExtension(file), selectedAssemblyName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(System.IO.Path.GetFileName(file), System.IO.Path.GetFileName(selectedAssemblyPath), StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldCopyPluginArtifact(string filePath)
    {
        var extension = System.IO.Path.GetExtension(filePath);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private async void RefreshBindingSourceFromDatabaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            VM.BeginBusy("Подключаемся к БД", "Читаем схему таблицы и подготавливаем поля для BindingSource.");
            await Task.Delay(120);
            await VM.RefreshSelectedBindingSourceFromDatabaseAsync();
            VM.EndBusy();
        }
        catch (Exception ex)
        {
            VM.EndBusy();
            VM.StatusText = $"Ошибка подключения к БД: {ex.Message}";
        }
    }

    private void ClearBindingSourceQueryButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.ClearSelectedBindingSourceQuery();
    }

    private void RemoveBindingSourceButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.RemoveSelectedBindingSource();
    }

    private void AddBindingFieldButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.AddBindingField();
    }

    private void RemoveBindingFieldButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not BindingFieldModel field)
            return;

        VM.RemoveBindingField(field);
    }

    private void SetSelectedGridColumnWidthPresetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not BindingFieldModel field
            || button.Tag is not string preset
            || string.IsNullOrWhiteSpace(preset))
        {
            return;
        }

        VM.BeginUndoBatch();
        try
        {
            field.Width = preset;
            VM.StatusText = $"Ширина колонки «{field.Header}» установлена: {preset}";
        }
        finally
        {
            VM.CommitUndoBatch();
        }
    }

    private void MakeSelectedGridColumnsEqualWidthButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var columns = VM.SelectedGridColumnsForControl;
        if (columns.Count == 0)
            return;

        VM.BeginUndoBatch();
        try
        {
            foreach (var field in columns.Where(static field => field.IsVisible))
                field.Width = "*";

            VM.StatusText = "Для всех видимых колонок установлена одинаковая ширина";
        }
        finally
        {
            VM.CommitUndoBatch();
        }
    }

    private void OpenDataGridColumnEditorButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (VM.SelectedControl is null || !VM.CanEditDataBinding)
        {
            VM.StatusText = "Выберите DataGrid, чтобы открыть редактор колонок";
            return;
        }

        var editor = new DataGridColumnEditorWindow(
            VM,
            VM.SelectedControl,
            VM.SelectedBindingSourceForControl);
        editor.Show(this);
    }

    private async void OpenDocumentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!await EnsureUnsavedChangesHandledAsync())
            return;

        if (StorageProvider is null || !StorageProvider.CanOpen)
        {
            VM.StatusText = "Открытие файла недоступно в этом окружении";
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть документ конструктора",
                AllowMultiple = false,
                FileTypeFilter = new[] { DesignerDocumentFileType }
            });

            var file = files.FirstOrDefault();
            if (file is null)
                return;

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var localPath = file.TryGetLocalPath() ?? file.Name;
            VM.LoadDocumentJson(json, localPath);
            VM.AddOrUpdateRecentFile(localPath);
            _autosaveRecoveryService.TryDeleteDraft();
            VM.AutosaveStatusText = "Черновик очищен после открытия документа.";
            VM.StatusText = $"Открыт документ: {file.Name}";
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryGeneral, "File opened.", localPath);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "File opened", file.Name);
            await SaveAppSettingsNowAsync();
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка открытия: {ex.Message}";
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryGeneral, "Open failed.", ex.Message);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Open failed", ex.Message, isPersistent: true);
        }
    }

    private async void OpenRecentFileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecentFileModel recentFile })
            return;

        if (!await EnsureUnsavedChangesHandledAsync())
            return;

        if (!File.Exists(recentFile.FilePath))
        {
            var unavailableWindow = new RecentFileUnavailableWindow(recentFile.FilePath);
            var decision = await unavailableWindow.ShowDialog<RecentFileUnavailableDialogResult>(this);
            if (decision == RecentFileUnavailableDialogResult.Remove)
            {
                VM.RemoveRecentFile(recentFile);
                await SaveAppSettingsNowAsync();
            }

            VM.StatusText = $"Recent file недоступен: {recentFile.FilePath}";
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(recentFile.FilePath);
            VM.LoadDocumentJson(json, recentFile.FilePath);
            VM.AddOrUpdateRecentFile(recentFile.FilePath);
            _autosaveRecoveryService.TryDeleteDraft();
            VM.AutosaveStatusText = "Черновик очищен после открытия документа.";
            VM.StatusText = $"Открыт документ: {System.IO.Path.GetFileName(recentFile.FilePath)}";
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryGeneral, "Recent file opened.", recentFile.FilePath);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "Recent opened", recentFile.Title);
            await SaveAppSettingsNowAsync();
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка открытия recent file: {ex.Message}";
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryGeneral, "Recent open failed.", ex.Message);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Recent open failed", ex.Message, isPersistent: true);
        }
    }

    private async void RemoveRecentFileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecentFileModel recentFile })
            return;

        VM.RemoveRecentFile(recentFile);
        VM.LogWorkspace(WorkspaceLogLevel.Info, MainWindowViewModel.OutputCategoryGeneral, $"Removed recent file: {recentFile.FilePath}");
        await SaveAppSettingsNowAsync();
    }

    private async void ToggleRecentPinButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecentFileModel recentFile })
            return;

        VM.ToggleRecentFilePinned(recentFile);
        await SaveAppSettingsNowAsync();
    }

    private async void RestoreBackupButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VM.CurrentDocumentPath))
        {
            VM.StatusText = "Backup доступен после сохранения документа в файл.";
            return;
        }

        var backups = _documentBackupService.ListBackups(VM.CurrentDocumentPath);
        if (backups.Count == 0)
        {
            VM.StatusText = "Для текущего документа backup ещё не создавался.";
            return;
        }

        if (!await EnsureUnsavedChangesHandledAsync())
            return;

        var dialog = new BackupRestoreWindow(VM.CurrentDocumentPath, backups);
        var backup = await dialog.ShowDialog<BackupFileModel?>(this);
        if (backup is null)
            return;

        try
        {
            var json = await File.ReadAllTextAsync(backup.FilePath);
            VM.LoadDocumentJson(json, VM.CurrentDocumentPath, markAsSaved: false);
            VM.StatusText = $"Восстановлен backup: {backup.DisplayName}";
            VM.AutosaveStatusText = "Backup открыт как несохранённое состояние.";
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка восстановления backup: {ex.Message}";
        }
    }

    private async void NewDocumentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!await EnsureUnsavedChangesHandledAsync())
            return;

        VM.NewDocumentCommand.Execute(null);
        _autosaveRecoveryService.TryDeleteDraft();
        VM.AutosaveStatusText = "Черновик очищен для нового документа.";
        await SaveAppSettingsNowAsync();
    }

    private async void CopyGeneratedXamlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.GenerateXamlCommand.Execute(null);
        await CopyTextToClipboardAsync(VM.GeneratedXaml, "XAML скопирован. Если в подсказке указан NuGet, установите его в новом проекте.");
    }

    private async void CopyGeneratedCSharpButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        VM.GenerateXamlCommand.Execute(null);
        await CopyTextToClipboardAsync(VM.GeneratedCSharp, "C# скопирован. Проверьте namespace формы в новом проекте.");
    }

    private async void CopyMainWindowXamlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyExportForTargetAsync(MainWindowViewModel.ExportTargetMainWindow, copyXaml: true);
    }

    private async void CopyMainWindowCSharpButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyExportForTargetAsync(MainWindowViewModel.ExportTargetMainWindow, copyXaml: false);
    }

    private async void CopyFormWindowXamlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyExportForTargetAsync(MainWindowViewModel.ExportTargetGeneratedWindow, copyXaml: true);
    }

    private async void CopyFormWindowCSharpButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyExportForTargetAsync(MainWindowViewModel.ExportTargetGeneratedWindow, copyXaml: false);
    }

    private async Task CopyExportForTargetAsync(string exportTarget, bool copyXaml)
    {
        VM.ExportTarget = exportTarget;
        VM.GenerateXamlCommand.Execute(null);
        var targetName = exportTarget == MainWindowViewModel.ExportTargetMainWindow
            ? "MainWindow"
            : "Form1Window";
        await CopyTextToClipboardAsync(
            copyXaml ? VM.GeneratedXaml : VM.GeneratedCSharp,
            copyXaml
                ? $"{targetName}.axaml скопирован."
                : $"{targetName}.axaml.cs скопирован.");
    }

    private async Task CopyTextToClipboardAsync(string text, string successStatus)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            VM.StatusText = "Буфер обмена недоступен в этом окружении.";
            return;
        }

        await clipboard.SetTextAsync(text);
        VM.StatusText = successStatus;
    }

    private async Task<bool> EnsureUnsavedChangesHandledAsync()
    {
        if (DataContext is not MainWindowViewModel || !VM.HasUnsavedChanges)
            return true;

        var dialog = new UnsavedChangesWindow(VM.CurrentDocumentDisplayName);
        var decision = await dialog.ShowDialog<UnsavedChangesDialogResult>(this);
        return decision switch
        {
            UnsavedChangesDialogResult.Save => await SaveCurrentDocumentAsync(),
            UnsavedChangesDialogResult.Discard => true,
            _ => false
        };
    }

    private async void SaveDocumentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveCurrentDocumentAsync();
    }

    private async void SaveDocumentAsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveDocumentAsAsync();
    }

    private async Task<bool> SaveCurrentDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(VM.CurrentDocumentPath))
            return await SaveDocumentAsAsync();

        return await SaveDocumentToPathAsync(VM.CurrentDocumentPath);
    }

    private async Task<bool> SaveDocumentAsAsync()
    {
        if (StorageProvider is null || !StorageProvider.CanSave)
        {
            VM.StatusText = "Сохранение недоступно в этом окружении";
            return false;
        }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить документ конструктора",
                SuggestedFileName = string.IsNullOrWhiteSpace(VM.CurrentDocumentPath)
                    ? "form-designer.formdesigner.json"
                    : System.IO.Path.GetFileName(VM.CurrentDocumentPath),
                DefaultExtension = "json",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { DesignerDocumentFileType }
            });

            if (file is null)
                return false;

            return await SaveDocumentToStorageFileAsync(file);
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка сохранения: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> SaveDocumentToPathAsync(string path)
    {
        try
        {
            var json = VM.ExportDocumentJson();
            var backup = await _documentBackupService.TryCreateBackupAsync(path);
            await SaveTextAtomicallyAsync(path, json);
            VM.MarkDocumentSaved(path);
            VM.AddOrUpdateRecentFile(path);
            _autosaveRecoveryService.TryDeleteDraft();
            VM.AutosaveStatusText = backup is null
                ? "Черновик очищен после сохранения."
                : $"Черновик очищен. Backup: {backup.DisplayName}";
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryGeneral, "File saved.", path);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "File saved", System.IO.Path.GetFileName(path));
            await SaveAppSettingsNowAsync();
            return true;
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка сохранения: {ex.Message}";
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryGeneral, "Save failed.", ex.Message);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Save failed", ex.Message, isPersistent: true);
            return false;
        }
    }

    private async Task<bool> SaveDocumentToStorageFileAsync(IStorageFile file)
    {
        try
        {
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
                return await SaveDocumentToPathAsync(localPath);

            var json = VM.ExportDocumentJson();

            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
                stream.Seek(0, SeekOrigin.Begin);
            }

            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            await writer.FlushAsync();

            VM.MarkDocumentSaved(file.Name);
            _autosaveRecoveryService.TryDeleteDraft();
            VM.AutosaveStatusText = "Черновик очищен после сохранения.";
            VM.LogWorkspace(WorkspaceLogLevel.Success, MainWindowViewModel.OutputCategoryGeneral, "File saved.", file.Name);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Success, "File saved", file.Name);
            await SaveAppSettingsNowAsync();
            return true;
        }
        catch (Exception ex)
        {
            VM.StatusText = $"Ошибка сохранения: {ex.Message}";
            VM.LogWorkspace(WorkspaceLogLevel.Error, MainWindowViewModel.OutputCategoryGeneral, "Save failed.", ex.Message);
            VM.ShowWorkspaceToast(WorkspaceToastLevel.Error, "Save failed", ex.Message, isPersistent: true);
            return false;
        }
    }

    private static async Task SaveTextAtomicallyAsync(string path, string text)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, text);
        File.Move(tempPath, path, overwrite: true);
    }

    private async void BrowseImageButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!GetSelectionTargets(nameof(DesignControlModel.ImageSource)).Any())
            return;

        var selectedPath = await PickImagePathAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        ApplyStringPropertyToSelection(nameof(DesignControlModel.ImageSource), selectedPath);
        VM.StatusText = $"Выбрано изображение: {System.IO.Path.GetFileName(selectedPath)}";
    }

    private async void OpenColorPickerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string propertyName)
            return;

        // Все цветовые кнопки работают через один обработчик:
        // имя редактируемого свойства передается в Tag самой кнопки.
        if (propertyName != nameof(MainWindowViewModel.SurfaceBackground) && VM.SelectedControl is null)
            return;

        var initialValue = GetCurrentColorValue(propertyName);
        if (string.IsNullOrWhiteSpace(initialValue))
            return;

        var selectedColor = await ShowColorPickerFlyoutAsync(button, propertyName, initialValue);
        if (string.IsNullOrWhiteSpace(selectedColor))
            return;

        ApplyColorValue(propertyName, selectedColor);
        RefreshFromPropertyPanel();
    }

    private void ApplyColorPresetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Color presets are hidden in the current UI; the handler remains only for legacy XAML compatibility.
    }

    private void ApplySurfaceVisualPresetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Surface presets are hidden in the current UI; the handler remains only for legacy XAML compatibility.
    }

    private void ApplyControlVisualPresetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (VM.SelectedControl is null || sender is not Button button || button.Tag is not string tag)
            return;

        var parts = tag.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return;

        var controlType = parts[0];
        var presetKey = parts[1];
        var targets = GetSelectedControlsByType(controlType);
        if (targets.Count == 0)
            return;

        foreach (var control in targets)
        {
            switch (controlType)
            {
                case "Button":
                    ApplyButtonVisualPreset(control, presetKey);
                    break;

                case "DataGrid":
                    ApplyDataGridVisualPreset(control, presetKey);
                    break;
            }
        }

        VM.StatusText = targets.Count == 1
            ? $"Применен пресет {button.Content} к {targets[0].Name}"
            : $"Применен пресет {button.Content} к элементам: {targets.Count}";
    }

    private IReadOnlyList<DesignControlModel> GetSelectedControlsByType(string controlType)
    {
        var selected = VM.GetSelectedControls()
            .Where(control => string.Equals(control.Type, controlType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selected.Count > 0)
            return selected;

        return VM.SelectedControl is not null && string.Equals(VM.SelectedControl.Type, controlType, StringComparison.OrdinalIgnoreCase)
            ? new[] { VM.SelectedControl }
            : Array.Empty<DesignControlModel>();
    }

    private static void ApplyButtonVisualPreset(DesignControlModel control, string presetKey)
    {
        control.FontWeight = "SemiBold";
        control.Padding = Math.Max(10, control.Padding);
        control.BorderThickness = Math.Max(1, control.BorderThickness);

        switch (presetKey)
        {
            case "Classic":
                control.Background = "#2563EB";
                control.Foreground = "#FFFFFF";
                control.BorderBrush = "#1D4ED8";
                control.CornerRadius = 8;
                break;

            case "DeepBlue":
                control.Background = "#153E75";
                control.Foreground = "#F8FAFC";
                control.BorderBrush = "#60A5FA";
                control.CornerRadius = 12;
                break;

            case "Glass":
                control.Background = "linear-gradient(135deg, #FFFFFF 0%, #DBEAFE 56%, #93C5FD 100%)";
                control.Foreground = "#0F172A";
                control.BorderBrush = "#BFDBFE";
                control.CornerRadius = 14;
                break;

            case "Night":
                control.Background = "#1E293B";
                control.Foreground = "#F8FAFC";
                control.BorderBrush = "#475569";
                control.CornerRadius = 10;
                break;

            case "Emerald":
                control.Background = "#0F766E";
                control.Foreground = "#ECFEFF";
                control.BorderBrush = "#14B8A6";
                control.CornerRadius = 12;
                break;
        }
    }

    private static void ApplyDataGridVisualPreset(DesignControlModel control, string presetKey)
    {
        control.BorderThickness = Math.Max(1, control.BorderThickness);
        control.DataGridShowHeader = true;
        control.DataGridRowFontWeight = "Normal";
        control.DataGridHeaderFontWeight = "SemiBold";

        switch (presetKey)
        {
            case "Classic":
                control.Background = "#FFFFFF";
                control.Foreground = "#0F172A";
                control.BorderBrush = "#94A3B8";
                control.DataGridGlowColor = "#94A3B8";
                control.DataGridHeaderBackground = "#E2E8F0";
                control.DataGridHeaderForeground = "#0F172A";
                control.DataGridRowForeground = "#0F172A";
                control.DataGridHoverRowBackground = "#F1F5F9";
                control.DataGridSelectedRowBackground = "#CBD5E1";
                control.DataGridSelectedRowForeground = "#0F172A";
                control.DataGridGridLineBrush = "#CBD5E1";
                control.DataGridOuterBorderBrush = "#94A3B8";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#F8FAFC";
                control.DataGridShowAlternatingRows = true;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = true;
                control.DataGridHeaderHeight = 40;
                control.DataGridRowHeight = 32;
                control.DataGridCellPadding = 10;
                control.DataGridHeaderFontSize = 13;
                control.DataGridRowFontSize = 13;
                control.CornerRadius = 8;
                break;

            case "GlowLight":
            case "LightBlue":
                control.Background = "#EFF6FF";
                control.Foreground = "#0F172A";
                control.BorderBrush = "#3B82F6";
                control.DataGridGlowColor = "#3B82F6";
                control.DataGridHeaderBackground = "#DBEAFE";
                control.DataGridHeaderForeground = "#1E3A8A";
                control.DataGridRowForeground = "#0F172A";
                control.DataGridHoverRowBackground = "#E0F2FE";
                control.DataGridSelectedRowBackground = "#BFDBFE";
                control.DataGridSelectedRowForeground = "#0F172A";
                control.DataGridGridLineBrush = "#BFDBFE";
                control.DataGridOuterBorderBrush = "#3B82F6";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#F8FBFF";
                control.DataGridShowAlternatingRows = true;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = true;
                control.DataGridHeaderHeight = 44;
                control.DataGridRowHeight = 36;
                control.DataGridCellPadding = 12;
                control.DataGridHeaderFontSize = 13;
                control.DataGridRowFontSize = 13;
                control.CornerRadius = 16;
                break;

            case "GlowDark":
            case "DarkHeader":
                control.Background = "#FFFFFF";
                control.Foreground = "#0F172A";
                control.BorderBrush = "#1E293B";
                control.DataGridGlowColor = "#38BDF8";
                control.DataGridHeaderBackground = "#0F172A";
                control.DataGridHeaderForeground = "#F8FAFC";
                control.DataGridRowForeground = "#0F172A";
                control.DataGridHoverRowBackground = "#EFF6FF";
                control.DataGridSelectedRowBackground = "#DBEAFE";
                control.DataGridSelectedRowForeground = "#0F172A";
                control.DataGridGridLineBrush = "#CBD5E1";
                control.DataGridOuterBorderBrush = "#1E293B";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#F8FAFC";
                control.DataGridShowAlternatingRows = true;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = true;
                control.DataGridHeaderHeight = 44;
                control.DataGridRowHeight = 34;
                control.DataGridCellPadding = 12;
                control.DataGridHeaderFontSize = 13;
                control.DataGridRowFontSize = 13;
                control.CornerRadius = 12;
                break;

            case "Compact":
                control.Background = "#FFFFFF";
                control.Foreground = "#0F172A";
                control.BorderBrush = "#CBD5E1";
                control.DataGridGlowColor = "#64748B";
                control.DataGridHeaderBackground = "#F1F5F9";
                control.DataGridHeaderForeground = "#0F172A";
                control.DataGridRowForeground = "#0F172A";
                control.DataGridHoverRowBackground = "#F8FAFC";
                control.DataGridSelectedRowBackground = "#E0F2FE";
                control.DataGridSelectedRowForeground = "#0F172A";
                control.DataGridGridLineBrush = "#E2E8F0";
                control.DataGridOuterBorderBrush = "#CBD5E1";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#FFFFFF";
                control.DataGridShowAlternatingRows = false;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = true;
                control.DataGridHeaderHeight = 32;
                control.DataGridRowHeight = 26;
                control.DataGridCellPadding = 6;
                control.DataGridHeaderFontSize = 12;
                control.DataGridRowFontSize = 12;
                control.CornerRadius = 6;
                break;

            case "Comfortable":
                control.Background = "#F8FAFF";
                control.Foreground = "#1E1B4B";
                control.BorderBrush = "#818CF8";
                control.DataGridGlowColor = "#818CF8";
                control.DataGridHeaderBackground = "#EEF2FF";
                control.DataGridHeaderForeground = "#312E81";
                control.DataGridRowForeground = "#1E1B4B";
                control.DataGridHoverRowBackground = "#E0E7FF";
                control.DataGridSelectedRowBackground = "#C7D2FE";
                control.DataGridSelectedRowForeground = "#1E1B4B";
                control.DataGridGridLineBrush = "#DDE6F5";
                control.DataGridOuterBorderBrush = "#818CF8";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#F8FAFF";
                control.DataGridShowAlternatingRows = true;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = true;
                control.DataGridHeaderHeight = 52;
                control.DataGridRowHeight = 44;
                control.DataGridCellPadding = 16;
                control.DataGridHeaderFontSize = 14;
                control.DataGridRowFontSize = 14;
                control.CornerRadius = 18;
                break;

            case "Enterprise":
                control.Background = "#F8FAFC";
                control.Foreground = "#0F172A";
                control.BorderBrush = "#475569";
                control.DataGridGlowColor = "#2563EB";
                control.DataGridHeaderBackground = "#334155";
                control.DataGridHeaderForeground = "#F8FAFC";
                control.DataGridRowForeground = "#0F172A";
                control.DataGridHoverRowBackground = "#E2E8F0";
                control.DataGridSelectedRowBackground = "#CBD5E1";
                control.DataGridSelectedRowForeground = "#0F172A";
                control.DataGridGridLineBrush = "#94A3B8";
                control.DataGridOuterBorderBrush = "#475569";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#F1F5F9";
                control.DataGridShowAlternatingRows = true;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = true;
                control.DataGridHeaderHeight = 42;
                control.DataGridRowHeight = 34;
                control.DataGridCellPadding = 10;
                control.DataGridHeaderFontSize = 13;
                control.DataGridRowFontSize = 13;
                control.CornerRadius = 4;
                break;

            case "Minimal":
                control.Background = "#FFFFFF";
                control.Foreground = "#111827";
                control.BorderBrush = "#E5E7EB";
                control.DataGridGlowColor = "#94A3B8";
                control.DataGridHeaderBackground = "#FFFFFF";
                control.DataGridHeaderForeground = "#334155";
                control.DataGridRowForeground = "#111827";
                control.DataGridHoverRowBackground = "#FAFAFA";
                control.DataGridSelectedRowBackground = "#EEF2FF";
                control.DataGridSelectedRowForeground = "#111827";
                control.DataGridGridLineBrush = "#EEF2F7";
                control.DataGridOuterBorderBrush = "#E5E7EB";
                control.DataGridRowBackground = "#FFFFFF";
                control.DataGridAlternateRowBackground = "#FFFFFF";
                control.DataGridShowAlternatingRows = false;
                control.DataGridShowRowLines = true;
                control.DataGridShowColumnLines = false;
                control.DataGridHeaderHeight = 38;
                control.DataGridRowHeight = 34;
                control.DataGridCellPadding = 10;
                control.DataGridHeaderFontSize = 12;
                control.DataGridRowFontSize = 13;
                control.CornerRadius = 8;
                break;
        }
    }

    private void ApplySampleImageButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetSelectionTargets(nameof(DesignControlModel.ImageSource)).Count == 0)
            return;

        ApplyStringPropertyToSelection(nameof(DesignControlModel.ImageSource), "avares://FormDesigner/Assets/avalonia-logo.ico");
    }
}
