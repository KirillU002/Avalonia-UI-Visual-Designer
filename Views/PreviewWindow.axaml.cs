using Avalonia;
using Avalonia.Controls;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormDesigner.Views;

/// <summary>
/// Отдельное окно запуска формы "как у пользователя".
/// Оно рендерит сохраненный документ без дизайнерских рамок, выделения и служебных слоев.
/// </summary>
public partial class PreviewWindow : Window
{
    private const double RuntimeToolbarHeight = 52;
    private const int MaxPreviewDataGridRows = 120;
    private const string RuntimeDataGridGroupFieldFormat = "formdesigner-preview-datagrid-group-field";
    private const string RuntimeDataGridUngroupFieldFormat = "formdesigner-preview-datagrid-ungroup-field";

    private DesignerDocumentFileModel _document = new();
    private readonly Dictionary<string, DesignerFormDocument> _projectFormsById = new(StringComparer.OrdinalIgnoreCase);
    private string _currentFormId = "";
    private readonly Dictionary<string, Bitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Signature, IReadOnlyList<Dictionary<string, string>> Rows)> _sqlPreviewRowsBySourceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly PreviewRuntimeService _previewRuntimeService = new();
    private readonly IDesignerRegistry _registry;
    private RuntimeDataGridHeaderDragState? _runtimeDataGridHeaderDrag;
    private bool _isRenderingDocument;
    private bool _renderDocumentAgainRequested;
    private bool _isRenderDocumentScheduled;
    private readonly DispatcherTimer _renderDocumentTimer = new();
    private int _lastSyncedPreviewDiagnosticsVersion = -1;

    private Dictionary<string, Dictionary<string, string>> _dataGridFilterValuesByControlId => EnsurePreviewRuntimeContext().DataGridFilterValuesByControlId;

    private Dictionary<string, string> _runtimeTextBoxValuesByControlId => EnsurePreviewRuntimeContext().TextBoxValuesByControlId;

    private Dictionary<string, string> _runtimeTextBlockValuesByControlId => EnsurePreviewRuntimeContext().TextBlockValuesByControlId;

    private Dictionary<string, string> _runtimeButtonContentByControlId => EnsurePreviewRuntimeContext().ButtonContentByControlId;

    private Dictionary<string, bool> _runtimeCheckBoxValuesByControlId => EnsurePreviewRuntimeContext().CheckBoxValuesByControlId;

    private Dictionary<string, bool> _runtimeVisibilityByControlId => EnsurePreviewRuntimeContext().VisibilityByControlId;

    private Dictionary<string, bool> _runtimeEnabledByControlId => EnsurePreviewRuntimeContext().EnabledByControlId;

    public PreviewWindow()
        : this(CreateFallbackRegistry())
    {
    }

    public PreviewWindow(IDesignerRegistry registry)
    {
        _registry = registry;
        InitializeComponent();
        ApplyWindowSettings();
        Opened += PreviewWindow_Opened;
        KeyDown += PreviewWindow_KeyDown;
        SizeChanged += PreviewWindow_SizeChanged;
        _renderDocumentTimer.Interval = TimeSpan.FromMilliseconds(33);
        _renderDocumentTimer.Tick += RenderDocumentTimer_Tick;
        AddHandler(InputElement.PointerMovedEvent, PreviewWindow_PointerMoved, RoutingStrategies.Tunnel, true);
        AddHandler(InputElement.PointerReleasedEvent, PreviewWindow_PointerReleased, RoutingStrategies.Tunnel, true);
    }

    public PreviewWindow(DesignerDocumentFileModel document, IDesignerRegistry registry)
        : this(registry)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ApplyWindowSettings();
    }

    public PreviewWindow(
        DesignerDocumentFileModel document,
        IDesignerRegistry registry,
        IEnumerable<DesignerFormDocument> projectForms,
        string currentFormId)
        : this(document, registry)
    {
        _currentFormId = currentFormId ?? string.Empty;
        foreach (var form in projectForms)
        {
            if (!string.IsNullOrWhiteSpace(form.Id))
                _projectFormsById[form.Id] = form;
        }
    }

    private PreviewRuntimeContext EnsurePreviewRuntimeContext()
    {
        if (_previewRuntimeService.Current is { } context)
            return context;

        var nextContext = _previewRuntimeService.Start(
            _document.Controls.Select(CreateRuntimeControlModel),
            ToRuntimeBindingSources(),
            ToRuntimeInteractions());
        SyncPreviewRuntimeDiagnostics();
        return nextContext;
    }

    private void ReloadPreviewRuntimeContext()
    {
        _previewRuntimeService.Reload(
            _document.Controls.Select(CreateRuntimeControlModel),
            ToRuntimeBindingSources(),
            ToRuntimeInteractions());
        _lastSyncedPreviewDiagnosticsVersion = -1;
        SyncPreviewRuntimeDiagnostics();
    }

    private void SyncPreviewRuntimeDiagnostics()
    {
        if (_previewRuntimeService.Current is not { } context)
            return;

        if (context.DiagnosticsVersion == _lastSyncedPreviewDiagnosticsVersion)
            return;

        _lastSyncedPreviewDiagnosticsVersion = context.DiagnosticsVersion;
        var errors = context.Diagnostics.Count(item => item.Severity == DocumentDiagnosticSeverity.Error);
        var warnings = context.Diagnostics.Count(item => item.Severity == DocumentDiagnosticSeverity.Warning);
        PreviewRuntimeStatusText.Text = errors > 0
            ? $"Runtime errors: {errors}, warnings: {warnings}"
            : warnings > 0
                ? $"Runtime warnings: {warnings}"
                : "Runtime ready";
        ToolTip.SetTip(
            PreviewRuntimeStatusText,
            context.Diagnostics.Count == 0
                ? "Preview runtime is isolated from the document model."
                : string.Join(Environment.NewLine, context.Diagnostics.Take(6).Select(item => $"{item.Category}: {item.Message}")));
    }

    private IEnumerable<InteractionModel> ToRuntimeInteractions()
    {
        return _document.Interactions.Select(interaction => new InteractionModel
        {
            Id = interaction.Id,
            SourceControlName = interaction.SourceControlName,
            EventName = interaction.EventName,
            ActionType = interaction.ActionType,
            TargetControlName = interaction.TargetControlName,
            TargetProperty = interaction.TargetProperty,
            SourcePath = interaction.SourcePath,
            TextTemplate = interaction.TextTemplate,
            MessageTitle = interaction.MessageTitle,
            TargetFormId = interaction.TargetFormId,
            TargetFormName = interaction.TargetFormName,
            OpenMode = InteractionModel.NormalizeOpenMode(interaction.OpenMode),
            CloseCurrentAfterOpen = interaction.CloseCurrentAfterOpen
        });
    }

    private static IDesignerRegistry CreateFallbackRegistry()
    {
        var registry = new DesignerRegistry();
        BuiltInControlRegistrar.Register(registry);
        return registry;
    }

    private async void PreviewWindow_Opened(object? sender, EventArgs e)
    {
        ShowLoading("Подготавливаем окно", "Собираем форму так, как её увидит пользователь при запуске.");

        try
        {
            await Task.Delay(180);
            EnsurePreviewRuntimeContext();
            await PreloadBindingPreviewRowsAsync();
            RenderDocument();
        }
        finally
        {
            HideLoading();
        }
    }

    private void PreviewWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async void ResetPreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        await ResetPreviewRuntimeAsync();
    }

    private async void ReloadPreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        await ReloadPreviewRuntimeAsync();
    }

    private void ClosePreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task ResetPreviewRuntimeAsync()
    {
        ShowLoading("Reset Preview", "Сбрасываем runtime-состояние и заново применяем preview data.");
        try
        {
            EnsurePreviewRuntimeContext();
            _previewRuntimeService.Reset();
            _lastSyncedPreviewDiagnosticsVersion = -1;
            SyncPreviewRuntimeDiagnostics();
            await PreloadBindingPreviewRowsAsync();
            RenderDocument();
        }
        finally
        {
            HideLoading();
        }
    }

    private async Task ReloadPreviewRuntimeAsync()
    {
        ShowLoading("Reload Preview", "Пересобираем runtime tree, bindings и interactions.");
        try
        {
            _sqlPreviewRowsBySourceId.Clear();
            ReloadPreviewRuntimeContext();
            await PreloadBindingPreviewRowsAsync();
            RenderDocument();
        }
        finally
        {
            HideLoading();
        }
    }

    private async void PreviewWindow_PointerMoved(object? sender, PointerEventArgs e)
    {
        var dragState = _runtimeDataGridHeaderDrag;
        if (dragState is null || dragState.IsDragDropActive)
            return;

        var pointerPoint = e.GetCurrentPoint(this);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            _runtimeDataGridHeaderDrag = null;
            return;
        }

        var currentPosition = e.GetPosition(dragState.DataGrid);
        var deltaX = Math.Abs(currentPosition.X - dragState.StartPosition.X);
        var deltaY = currentPosition.Y - dragState.StartPosition.Y;

        // Горизонтальное движение оставляем штатному DataGrid для reorder/resize.
        // Drag-to-group стартует только когда заголовок явно потянули вверх к panel.
        if (currentPosition.Y > -8 || Math.Abs(deltaY) < 10 || deltaX > Math.Abs(deltaY) * 1.6)
            return;

        dragState.IsDragDropActive = true;
        var data = new DataObject();
        data.Set(RuntimeDataGridGroupFieldFormat, dragState.Field.Path);
        data.Set(DataFormats.Text, dragState.Field.Header);

        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
        finally
        {
            if (ReferenceEquals(_runtimeDataGridHeaderDrag, dragState))
                _runtimeDataGridHeaderDrag = null;
        }
    }

    private void PreviewWindow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _runtimeDataGridHeaderDrag = null;
    }

    private async Task PreloadBindingPreviewRowsAsync()
    {
        var sqlSources = _document.BindingSources
            .Where(SqlPreviewDataLoader.CanLoad)
            .DistinctBy(source => source.Id)
            .ToList();

        foreach (var source in sqlSources)
        {
            var signature = SqlPreviewDataLoader.BuildSignature(source);
            if (_sqlPreviewRowsBySourceId.TryGetValue(source.Id, out var cached)
                && string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var rows = await SqlPreviewDataLoader.LoadRowsAsync(source);
                _sqlPreviewRowsBySourceId[source.Id] = (signature, rows);
            }
            catch
            {
                _sqlPreviewRowsBySourceId.Remove(source.Id);
            }
        }
    }

    private IReadOnlyList<Dictionary<string, string>> GetCachedPreviewRows(string? bindingSourceId)
    {
        if (string.IsNullOrWhiteSpace(bindingSourceId))
            return Array.Empty<Dictionary<string, string>>();

        return _sqlPreviewRowsBySourceId.TryGetValue(bindingSourceId, out var cached)
            ? cached.Rows
            : Array.Empty<Dictionary<string, string>>();
    }

    private Dictionary<string, string> GetPreviewDataGridFilterValues(string controlId)
    {
        if (!_dataGridFilterValuesByControlId.TryGetValue(controlId, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _dataGridFilterValuesByControlId[controlId] = values;
        }

        return values;
    }

    private System.Collections.IEnumerable? ResolvePreviewBindingItems(string bindingSourceId)
    {
        var source = GetBindingSource(bindingSourceId);
        if (source is null)
            return null;

        if (_sqlPreviewRowsBySourceId.TryGetValue(bindingSourceId, out var cached) && cached.Rows.Count > 0)
            return BindingPreviewItemsBuilder.ConvertRows(cached.Rows);

        return BindingPreviewItemsBuilder.BuildSampleItems(CreateRuntimeBindingSourceModel(source));
    }

    private void ApplyWindowSettings()
    {
        // Здесь применяются только свойства самой формы:
        // заголовок, размеры, режим окна и фон рабочей поверхности.
        Title = string.IsNullOrWhiteSpace(_document.FormTitle) ? "Form1" : _document.FormTitle;
        RequestedThemeVariant = ResolveThemeVariant(_document.FormTheme);
        CanResize = _document.FormCanResize;
        ShowInTaskbar = _document.FormShowInTaskbar;
        Topmost = _document.FormTopmost;
        SystemDecorations = _document.FormHasSystemDecorations ? SystemDecorations.Full : SystemDecorations.None;
        WindowStartupLocation = NormalizeStartupLocation(_document.FormStartupLocation);

        Width = Math.Max(320, _document.DesignWidth);
        Height = Math.Max(220, _document.DesignHeight) + RuntimeToolbarHeight;
        WindowState = NormalizeWindowState(_document.FormWindowState);

        PreviewSurfaceBorder.Background = ParseBrush(_document.SurfaceBackground, "#FFFFFF");
        PreviewCanvas.Background = ParseBrush(_document.SurfaceBackground, "#FFFFFF");
        PreviewCanvas.MinWidth = Math.Max(300, _document.DesignWidth);
        PreviewCanvas.MinHeight = Math.Max(200, _document.DesignHeight);
    }

    private static ThemeVariant ResolveThemeVariant(string? themeName)
    {
        return DesignerThemeCatalog.NormalizeThemeName(themeName) == DesignerThemeCatalog.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }

    private void RenderDocument()
    {
        if (_isRenderingDocument)
        {
            _renderDocumentAgainRequested = true;
            return;
        }

        try
        {
            _isRenderingDocument = true;

            // Предпросмотр строится из корневых контролов документа и затем рекурсивно добавляет детей.
            PreviewCanvas.Children.Clear();

            var actualRootWidth = PreviewCanvas.Bounds.Width > 0 ? PreviewCanvas.Bounds.Width : _document.DesignWidth;
            var actualRootHeight = PreviewCanvas.Bounds.Height > 0 ? PreviewCanvas.Bounds.Height : _document.DesignHeight;

            AddControlsToCanvas(
                PreviewCanvas,
                null,
                _document.DesignWidth,
                _document.DesignHeight,
                actualRootWidth,
                actualRootHeight);
        }
        finally
        {
            _isRenderingDocument = false;
        }

        if (_renderDocumentAgainRequested)
        {
            _renderDocumentAgainRequested = false;
            Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
        }
    }

    private void PreviewWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Debug.WriteLine(
            $"PREVIEW_WINDOW_RESIZE old={e.PreviousSize.Width.ToString(CultureInfo.InvariantCulture)}x{e.PreviousSize.Height.ToString(CultureInfo.InvariantCulture)}; " +
            $"new={e.NewSize.Width.ToString(CultureInfo.InvariantCulture)}x{e.NewSize.Height.ToString(CultureInfo.InvariantCulture)}; " +
            $"canvas={PreviewCanvas.Bounds.Width.ToString(CultureInfo.InvariantCulture)}x{PreviewCanvas.Bounds.Height.ToString(CultureInfo.InvariantCulture)}; zoom=1");
        ScheduleRenderDocument();
    }

    private void ScheduleRenderDocument()
    {
        _isRenderDocumentScheduled = true;
        if (!_renderDocumentTimer.IsEnabled)
            _renderDocumentTimer.Start();
    }

    private void RenderDocumentTimer_Tick(object? sender, EventArgs e)
    {
        _renderDocumentTimer.Stop();
        if (!_isRenderDocumentScheduled)
            return;

        _isRenderDocumentScheduled = false;
        var stopwatch = Stopwatch.StartNew();
        RenderDocument();
        stopwatch.Stop();
        if (stopwatch.Elapsed.TotalMilliseconds >= 16)
            Debug.WriteLine($"[FormDesigner:Perf] Preview render: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");
    }

    private void AddControlsToCanvas(
        Canvas host,
        DesignerControlFileModel? parent,
        double baseParentWidth,
        double baseParentHeight,
        double actualParentWidth,
        double actualParentHeight)
    {
        var layoutMode = parent is null
            ? DesignerLayoutModes.NormalizeMode(_document.SurfaceLayoutMode)
            : string.IsNullOrWhiteSpace(parent.ChildLayoutMode)
                ? DesignerLayoutModes.NormalizeMode(_registry.GetRequiredControl(parent.Type).ChildLayoutMode)
                : DesignerLayoutModes.NormalizeMode(parent.ChildLayoutMode);
        var children = GetChildControlsInVisualOrder(parent?.Id, layoutMode)
            .Where(control => control.IsVisible && IsRuntimeControlVisible(control))
            .ToList();
        if (children.Count == 0)
            return;

        if (DesignerLayoutModes.IsAbsolute(layoutMode))
        {
            foreach (var child in children)
            {
                AddRenderedControl(host, child, new Rect(child.X, child.Y, child.Width, child.Height));
            }

            return;
        }

        var orientation = parent is null ? _document.SurfaceLayoutOrientation : parent.LayoutOrientation;
        var spacing = parent is null ? _document.SurfaceLayoutSpacing : parent.LayoutSpacing;
        var columns = parent is null ? _document.SurfaceLayoutColumns : parent.Columns;
        var rows = parent is null ? _document.SurfaceLayoutRows : parent.Rows;
        var padding = parent?.Padding ?? 0;

        var frames = LayoutArrangementHelper.ArrangeChildren(
            layoutMode,
            orientation,
            spacing,
            columns,
            rows,
            padding,
            actualParentWidth,
            actualParentHeight,
            children.Select(child => new LayoutArrangementHelper.ChildSnapshot(
                child.Id,
                child.Width,
                child.Height,
                child.GridRow,
                child.GridColumn,
                child.GridRowSpan,
                child.GridColumnSpan,
                child.StackOrder)).ToList())
            .ToDictionary(frame => frame.Id, StringComparer.Ordinal);

        foreach (var child in children)
        {
            if (!frames.TryGetValue(child.Id, out var frame))
                continue;

            AddRenderedControl(host, child, new Rect(frame.X, frame.Y, frame.Width, frame.Height));
        }
    }

    private void AddRenderedControl(Canvas host, DesignerControlFileModel control, Rect frame)
    {
        var wrapper = CreatePreviewWrapper(control, frame.Width, frame.Height);
        Canvas.SetLeft(wrapper, frame.X);
        Canvas.SetTop(wrapper, frame.Y);
        wrapper.ZIndex = GetCanvasVisualZIndex(control);
        host.Children.Add(wrapper);
    }

    private Border CreatePreviewWrapper(DesignerControlFileModel control, double renderedWidth, double renderedHeight)
    {
        // Как и в дизайнере, каждый элемент оборачивается в отдельный Canvas,
        // чтобы вложенные контролы рисовались в локальных координатах родителя.
        var renderControl = CreateRenderControl(control, renderedWidth, renderedHeight);
        var preview = CreatePreviewControl(renderControl);
        var root = new Canvas
        {
            Width = renderedWidth,
            Height = renderedHeight,
            ClipToBounds = false
        };

        root.Children.Add(preview);
        Canvas.SetLeft(preview, 0);
        Canvas.SetTop(preview, 0);

        if (CanHostChildren(control))
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
                control,
                control.Width,
                control.Height,
                renderedWidth,
                renderedHeight);

            root.Children.Add(childHost);
            Canvas.SetLeft(childHost, 0);
            Canvas.SetTop(childHost, 0);
        }

        return new Border
        {
            Width = renderedWidth,
            Height = renderedHeight,
            Background = Brushes.Transparent,
            Opacity = control.Opacity,
            IsEnabled = IsRuntimeControlEnabled(control),
            Child = root
        };
    }

    private bool IsRuntimeControlVisible(DesignerControlFileModel control)
    {
        return !_runtimeVisibilityByControlId.TryGetValue(control.Id, out var isVisible) || isVisible;
    }

    private bool IsRuntimeControlEnabled(DesignerControlFileModel control)
    {
        return !_runtimeEnabledByControlId.TryGetValue(control.Id, out var isEnabled) || isEnabled;
    }

    private static DesignerControlFileModel CreateRenderControl(DesignerControlFileModel control, double renderedWidth, double renderedHeight)
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
            Stretch = control.Stretch,
            X = control.X,
            Y = control.Y,
            Width = renderedWidth,
            Height = renderedHeight,
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

    private static DesignControlModel CreateRuntimeControlModel(DesignerControlFileModel control)
    {
        var model = new DesignControlModel
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
            ShowFooter = control.ShowFooter
        };

        foreach (var property in control.CustomProperties)
        {
            model.CustomProperties.Add(new DesignPropertyValueModel
            {
                Key = property.Key,
                ValueJson = property.ValueJson
            });
        }

        return model;
    }

    private Control CreatePreviewControl(DesignerControlFileModel control)
    {
        var descriptor = _registry.GetRequiredControl(control.Type);
        var services = new DesignerServiceProvider()
            .Add<IBuiltInPreviewBridge>(new PreviewWindowBuiltInPreviewBridge(this))
            .Add<IPreviewBindingItemsProvider>(new DelegatePreviewBindingItemsProvider(ResolvePreviewBindingItems));
        var context = new DesignerPreviewContext(
            DesignerPreviewMode.RuntimePreview,
            services,
            parentId => GetChildControls(parentId)
                .Select(child => (IDesignControlNode)new DesignControlFileNodeAdapter(child))
                .ToList(),
            BindingMetadataMapper.ToMetadataMap(ToRuntimeBindingSources()));

        try
        {
            return descriptor.BuildPreview(new DesignControlFileNodeAdapter(control), context);
        }
        catch
        {
            return CreateMissingPreview(control);
        }
    }

    private Control CreateBuiltInPreviewControl(DesignerControlFileModel control)
    {
        return control.Type switch
        {
            DesignerControlTypes.Group => CreateGroupPreview(control),
            DesignerControlTypes.Button => CreateButtonPreview(control),
            DesignerControlTypes.TextBox => CreateTextBoxPreview(control),
            DesignerControlTypes.TextBlock => CreateTextBlockPreview(control),
            DesignerControlTypes.CheckBox => CreateCheckBoxPreview(control),
            DesignerControlTypes.Border => CreateBorderPreview(control),
            DesignerControlTypes.Image => CreateImagePreview(control),
            DesignerControlTypes.StackLayout => CreateStackLayoutPreview(control),
            DesignerControlTypes.LayoutGrid => CreateGridPreview(control),
            DesignerControlTypes.FlexLayout => CreateFlexLayoutPreview(control),
            DesignerControlTypes.DataGrid => CreateModernDataGridPreview(control),
            _ => new Border
            {
                Width = control.Width,
                Height = control.Height,
                Background = ParseBrush("#F8FAFC", "#F8FAFC"),
                BorderBrush = ParseBrush("#CBD5E1", "#CBD5E1"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = control.Type,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                IsHitTestVisible = false
            }
        };
    }

    private Control CreateMissingPreview(DesignerControlFileModel control)
    {
        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush("#FFF7ED", "#FFF7ED"),
            BorderBrush = ParseBrush("#FB923C", "#FB923C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = $"{control.Type}\nНет доступного preview",
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private IReadOnlyList<BindingSourceModel> ToRuntimeBindingSources()
    {
        return _document.BindingSources.Select(source => new BindingSourceModel
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
            SourceQuery = source.SourceQuery
        }).Select(model =>
        {
            var source = _document.BindingSources.First(item => item.Id == model.Id);
            foreach (var field in source.Fields)
                model.Fields.Add(CreateRuntimeBindingFieldModel(field));

            return model;
        }).ToList();
    }

    private BindingSourceModel CreateRuntimeBindingSourceModel(BindingSourceFileModel source)
    {
        var model = new BindingSourceModel
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
            SourceQuery = source.SourceQuery
        };

        foreach (var field in source.Fields)
            model.Fields.Add(CreateRuntimeBindingFieldModel(field));

        return model;
    }

    private static BindingFieldModel CreateRuntimeBindingFieldModel(BindingFieldFileModel field)
    {
        var allowSort = field.AllowSort && field.IsSortable;
        return new BindingFieldModel
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

    private sealed class PreviewWindowBuiltInPreviewBridge : IBuiltInPreviewBridge
    {
        private readonly PreviewWindow _owner;
        private readonly Dictionary<string, Func<DesignerControlFileModel, Control>> _builders;

        public PreviewWindowBuiltInPreviewBridge(PreviewWindow owner)
        {
            _owner = owner;
            _builders = new Dictionary<string, Func<DesignerControlFileModel, Control>>(StringComparer.OrdinalIgnoreCase)
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
            if (control is not DesignControlFileNodeAdapter adapter || !_builders.TryGetValue(typeKey, out var builder))
                return _owner.CreateMissingPreview(new DesignerControlFileModel { Type = typeKey, Width = 180, Height = 48 });

            return builder(adapter.Model);
        }
    }

    private Control CreateStackLayoutPreview(DesignerControlFileModel control)
    {
        var direction = DesignerLayoutModes.NormalizeOrientation(control.LayoutOrientation) == DesignerLayoutModes.Horizontal
            ? "Horizontal"
            : "Vertical";
        return CreateLayoutContainerPreview(control, $"Stack • {direction}", "#DBEAFE", "#2563EB");
    }

    private Control CreateFlexLayoutPreview(DesignerControlFileModel control)
    {
        var direction = DesignerLayoutModes.NormalizeOrientation(control.LayoutOrientation) == DesignerLayoutModes.Horizontal
            ? "Wrap by rows"
            : "Wrap by columns";
        return CreateLayoutContainerPreview(control, $"Flex • {direction}", "#DCFCE7", "#16A34A");
    }

    private Control CreateLayoutContainerPreview(DesignerControlFileModel control, string title, string tint, string accent)
    {
        var accentBrush = ParseBrush(accent, accent);
        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush(control.Background, tint),
            BorderBrush = ParseBrush(control.BorderBrush, accent),
            BorderThickness = UniformThickness(Math.Max(1, control.BorderThickness)),
            CornerRadius = UniformCornerRadius(Math.Max(8, control.CornerRadius)),
            Child = new StackPanel
            {
                Margin = UniformThickness(Math.Max(8, control.Padding)),
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
                            Foreground = ParseBrush(control.Foreground, "#475569"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        IsHitTestVisible = false
                    }
                }
            },
            IsHitTestVisible = false
        };
    }

    private Control CreateGroupPreview(DesignerControlFileModel control)
    {
        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false
        };
    }

    private Control CreateButtonPreview(DesignerControlFileModel control)
    {
        var text = _runtimeButtonContentByControlId.TryGetValue(control.Id, out var runtimeContent)
            ? runtimeContent
            : ResolvePreviewTextValue(control, string.IsNullOrWhiteSpace(control.Text) ? "Кнопка" : control.Text);

        var button = new Button
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush(control.Background, "#2563EB"),
            BorderBrush = ParseBrush(control.BorderBrush, "#1D4ED8"),
            BorderThickness = UniformThickness(control.BorderThickness),
            CornerRadius = UniformCornerRadius(control.CornerRadius),
            Padding = UniformThickness(control.Padding),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = CreatePreviewText(
                text,
                control,
                control.Foreground,
                HorizontalAlignment.Center,
                VerticalAlignment.Center)
        };

        button.Click += (_, _) =>
        {
            if (ApplyRuntimeInteractions(control, InteractionModel.EventButtonClick, BuildRuntimeSourceValues(control, text)))
                Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
        };

        return button;
    }

    private Control CreateTextBoxPreview(DesignerControlFileModel control)
    {
        var hasDesignText = !string.IsNullOrWhiteSpace(control.Text) || !string.IsNullOrWhiteSpace(control.TextBindingPath);
        var text = hasDesignText
            ? ResolvePreviewTextValue(control, string.IsNullOrWhiteSpace(control.Text) ? string.Empty : control.Text)
            : string.Empty;

        var textBox = new TextBox
        {
            Width = control.Width,
            Height = control.Height,
            Text = _runtimeTextBoxValuesByControlId.TryGetValue(control.Id, out var runtimeText) ? runtimeText : text,
            Watermark = string.IsNullOrWhiteSpace(control.PlaceholderText) ? "Введите текст..." : control.PlaceholderText,
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(8, control.FontSize),
            FontWeight = ParseFontWeight(control.FontWeight),
            Foreground = ParseBrush(control.Foreground, "#0F172A"),
            Background = ParseBrush(control.Background, "#FFFFFF"),
            BorderBrush = ParseBrush(control.BorderBrush, "#94A3B8"),
            BorderThickness = UniformThickness(control.BorderThickness),
            CornerRadius = UniformCornerRadius(control.CornerRadius),
            Padding = UniformThickness(control.Padding),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        textBox.TextChanged += (_, _) =>
        {
            _runtimeTextBoxValuesByControlId[control.Id] = textBox.Text ?? string.Empty;
            if (ApplyRuntimeInteractions(control, InteractionModel.EventTextBoxTextChanged, BuildRuntimeSourceValues(control, textBox.Text ?? string.Empty)))
                Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
        };

        return textBox;
    }

    private Control CreateTextBlockPreview(DesignerControlFileModel control)
    {
        var text = _runtimeTextBlockValuesByControlId.TryGetValue(control.Id, out var runtimeText)
            ? runtimeText
            : ResolvePreviewTextValue(control, string.IsNullOrWhiteSpace(control.Text) ? "Текст" : control.Text);

        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = Brushes.Transparent,
            Child = CreatePreviewText(
                text,
                control,
                control.Foreground,
                HorizontalAlignment.Left,
                VerticalAlignment.Center)
        };
    }

    private Control CreateCheckBoxPreview(DesignerControlFileModel control)
    {
        var caption = ResolvePreviewTextValue(control, string.IsNullOrWhiteSpace(control.Text) ? "Флажок" : control.Text);
        var checkBox = new CheckBox
        {
            Width = control.Width,
            Height = control.Height,
            IsChecked = _runtimeCheckBoxValuesByControlId.TryGetValue(control.Id, out var isChecked) && isChecked,
            Content = caption,
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(8, control.FontSize),
            FontWeight = ParseFontWeight(control.FontWeight),
            Foreground = ParseBrush(control.Foreground, "#0F172A"),
            Padding = new Thickness(6, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        checkBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != ToggleButton.IsCheckedProperty)
                return;

            _runtimeCheckBoxValuesByControlId[control.Id] = checkBox.IsChecked == true;
            var eventName = checkBox.IsChecked == true
                ? InteractionModel.EventCheckBoxChecked
                : InteractionModel.EventCheckBoxUnchecked;
            if (ApplyRuntimeInteractions(control, eventName, BuildRuntimeSourceValues(control, checkBox.IsChecked == true ? "true" : "false")))
                Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
        };

        return checkBox;
    }

    private Control CreateBorderPreview(DesignerControlFileModel control)
    {
        var text = _runtimeTextBlockValuesByControlId.TryGetValue(control.Id, out var runtimeText)
            ? runtimeText
            : ResolvePreviewTextValue(control, control.Text);
        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush(control.Background, "#F8FAFC"),
            BorderBrush = ParseBrush(control.BorderBrush, "#CBD5E1"),
            BorderThickness = UniformThickness(control.BorderThickness),
            CornerRadius = UniformCornerRadius(control.CornerRadius),
            Padding = UniformThickness(control.Padding),
            Child = string.IsNullOrWhiteSpace(text)
                ? null
                : CreatePreviewText(
                    text,
                    control,
                    control.Foreground,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Top)
        };
    }

    private string ResolvePreviewTextValue(DesignerControlFileModel control, string fallback)
    {
        if (string.IsNullOrWhiteSpace(control.TextBindingPath))
            return fallback;

        var field = ResolvePreviewBindingField(GetBindingFields(control.BindingSourceId), control.TextBindingPath);
        return string.IsNullOrWhiteSpace(field?.SampleValue) ? fallback : field.SampleValue;
    }

    private static BindingFieldFileModel? ResolvePreviewBindingField(IEnumerable<BindingFieldFileModel>? fields, string bindingPath)
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

    private Dictionary<string, string> BuildRuntimeSourceValues(DesignerControlFileModel source, string currentValue)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Value"] = currentValue
        };

        switch (source.Type)
        {
            case DesignerControlTypes.TextBox:
            case DesignerControlTypes.TextBlock:
            case DesignerControlTypes.Border:
                values[InteractionModel.TargetPropertyText] = currentValue;
                break;

            case DesignerControlTypes.Button:
                values[InteractionModel.TargetPropertyContent] = currentValue;
                break;

            case DesignerControlTypes.CheckBox:
                values[InteractionModel.TargetPropertyIsChecked] = currentValue;
                break;
        }

        return values;
    }

    private bool ApplyRuntimeInteractions(
        DesignerControlFileModel source,
        string eventName,
        IReadOnlyDictionary<string, string> sourceValues)
    {
        var context = EnsurePreviewRuntimeContext();
        var runtimeSource = context.FindControlById(source.Id) ?? CreateRuntimeControlModel(source);
        var result = _previewRuntimeService.ExecuteInteractions(runtimeSource, eventName, sourceValues);
        foreach (var message in result.Messages)
        {
            _ = ShowRuntimeMessageAsync(message.Message, message.Title);
        }

        foreach (var request in result.OpenFormRequests)
        {
            _ = OpenRuntimeFormAsync(request);
        }

        SyncPreviewRuntimeDiagnostics();
        return result.HasVisualChanges;
    }

    private async Task OpenRuntimeFormAsync(PreviewOpenFormRequest request)
    {
        try
        {
            if (!_projectFormsById.TryGetValue(request.TargetFormId, out var targetForm))
            {
                EnsurePreviewRuntimeContext().AddError(
                    "Preview interaction failed",
                    "Preview errors",
                    $"OpenForm target form not found in preview project: '{request.TargetFormName}'.",
                    "Check that the target form still exists in Project Explorer.");
                SyncPreviewRuntimeDiagnostics();
                return;
            }

            var previewWindow = new PreviewWindow(
                ClonePreviewFormDocument(targetForm.Document),
                _registry,
                _projectFormsById.Values,
                targetForm.Id)
            {
                Title = targetForm.DisplayName
            };

            if (string.Equals(request.OpenMode, InteractionModel.OpenModeShowDialog, StringComparison.OrdinalIgnoreCase))
                await previewWindow.ShowDialog(this);
            else
                previewWindow.Show(this);

            if (request.CloseCurrentAfterOpen)
                Close();
        }
        catch (Exception ex)
        {
            EnsurePreviewRuntimeContext().AddError(
                "Preview interaction failed",
                "Preview errors",
                $"OpenForm failed in preview runtime. {ex.Message}",
                "Check OpenForm interaction settings and reload preview.");
            SyncPreviewRuntimeDiagnostics();
        }
    }

    private static DesignerDocumentFileModel ClonePreviewFormDocument(DesignerDocumentFileModel document)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(document);
        return System.Text.Json.JsonSerializer.Deserialize<DesignerDocumentFileModel>(json) ?? new DesignerDocumentFileModel();
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
            EnsurePreviewRuntimeContext().AddError(
                "Preview interaction failed",
                "Preview errors",
                $"ShowMessage failed in preview runtime. {ex.Message}",
                "Check that the preview window is still active and try the interaction again.");
            SyncPreviewRuntimeDiagnostics();
        }
    }

    private Control CreateImagePreview(DesignerControlFileModel control)
    {
        var bitmap = TryLoadBitmap(control.ImageSource);
        Control content;

        if (bitmap is not null)
        {
            content = new Image
            {
                Width = control.Width,
                Height = control.Height,
                Source = bitmap,
                Stretch = ParseStretch(control.Stretch),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
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
                        Foreground = ParseBrush(control.Foreground, "#0F172A"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(control.ImageSource) ? "Укажите путь к файлу или avares URI" : control.ImageSource,
                        Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = Math.Max(80, control.Width - 40),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            };
        }

        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush(control.Background, "#F8FAFC"),
            BorderBrush = ParseBrush(control.BorderBrush, "#CBD5E1"),
            BorderThickness = UniformThickness(control.BorderThickness),
            CornerRadius = UniformCornerRadius(control.CornerRadius),
            ClipToBounds = true,
            Child = content
        };
    }

    private Control CreateGridPreview(DesignerControlFileModel control)
    {
        var grid = new Grid
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush(control.Background, "#FFFFFF")
        };

        for (var columnIndex = 0; columnIndex < Math.Max(1, control.Columns); columnIndex++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        for (var rowIndex = 0; rowIndex < Math.Max(1, control.Rows); rowIndex++)
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        for (var rowIndex = 0; rowIndex < Math.Max(1, control.Rows); rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < Math.Max(1, control.Columns); columnIndex++)
            {
                var cell = new Border
                {
                    BorderBrush = ParseBrush(control.BorderBrush, "#94A3B8"),
                    BorderThickness = control.ShowGridLines ? UniformThickness(Math.Max(1, control.BorderThickness)) : new Thickness(0),
                    Background = Brushes.Transparent
                };

                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = ParseBrush(control.Background, "#FFFFFF"),
            BorderBrush = ParseBrush(control.BorderBrush, "#94A3B8"),
            BorderThickness = UniformThickness(control.BorderThickness),
            Child = grid
        };
    }

    private Control CreateDataGridPreview(DesignerControlFileModel control)
    {
        // Для окна предпросмотра таблица тоже рисуется кастомно:
        // так проще обеспечить одинаковый внешний вид с дизайнером и прокрутку по колесу.
        var fields = GetBindingFields(control.BindingSourceId).ToList();
        var groupedFields = control.AllowGrouping
            ? fields
                .Where(field => field.GroupOrder >= 0)
                .OrderBy(field => field.GroupOrder)
                .ThenBy(field => field.Header)
                .ToList()
            : new List<BindingFieldFileModel>();
        var visibleFields = fields.Where(field => field.IsVisible).ToList();
        var showGroupPanel = control.AllowGrouping && (control.ShowGroupPanel || groupedFields.Count > 0);

        if (GetBindingSource(control.BindingSourceId) is null)
            return CreateDataGridEmptyStatePreview(control, "DataGrid: источник данных не выбран", "Выберите BindingSource во вкладке Данные.");

        if (fields.Count == 0)
            return CreateDataGridEmptyStatePreview(control, "BindingSource выбран, но поля не добавлены", "Добавьте поля вручную или импортируйте схему из DLL/SQL.");

        if (visibleFields.Count == 0)
            return CreateDataGridEmptyStatePreview(control, "Все поля BindingSource скрыты", "Включите видимость хотя бы одной колонки.");

        var themePalette = DesignerThemeCatalog.Get(_document.FormTheme);
        var headerBackgroundColor = ParseColor(control.Background, themePalette.DataGridHeaderBackground);
        var bodyBackgroundColor = ParseColor(control.DataGridRowBackground, themePalette.DataGridRowBackground);
        var alternateRowColor = ParseColor(control.DataGridAlternateRowBackground, themePalette.DataGridAlternateRowBackground);
        var borderColor = ParseColor(control.BorderBrush, "#CBD5E1");
        var headerBrush = new SolidColorBrush(headerBackgroundColor);
        var bodyBrush = new SolidColorBrush(bodyBackgroundColor);
        var alternateRowBrush = new SolidColorBrush(alternateRowColor);
        var separatorBrush = new SolidColorBrush(borderColor);
        var headerForeground = ContrastBrush(headerBackgroundColor);

        var layout = new Grid
        {
            Width = control.Width,
            Height = control.Height,
            ClipToBounds = true,
            RowDefinitions = showGroupPanel ? new RowDefinitions("Auto,Auto,*") : new RowDefinitions("Auto,*")
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
                        string.IsNullOrWhiteSpace(control.Name) ? "DataGrid" : control.Name,
                        control,
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
                    FontSize = Math.Max(10, control.FontSize - 1),
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
                        FontSize = Math.Max(10, control.FontSize - 1),
                        FontWeight = FontWeight.SemiBold
                    }
                });
            }

            Grid.SetRow(chips, 1);
            layout.Children.Add(chips);
        }

        var headerTable = new Grid
        {
            Background = headerBrush
        };

        var bodyTable = new Grid
        {
            Background = bodyBrush
        };

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            headerTable.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            bodyTable.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        var headerHeight = GetClassicPreviewDataGridHeaderHeight(control.FontSize);
        var rowHeight = GetClassicPreviewDataGridRowHeight(control.FontSize);
        var groupedAreaHeight = showGroupPanel ? Math.Max(40, rowHeight + 8) : 0;
        var availableRowsHeight = Math.Max(rowHeight, control.Height - headerHeight - groupedAreaHeight - 18);
        var visibleRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(3, (int)Math.Ceiling(availableRowsHeight / rowHeight)));
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

            if (control.AllowGrouping && field.GroupOrder >= 0)
                headerText += $" [{field.GroupOrder + 1}]";

            AddDataGridCell(headerTable, 0, columnIndex, headerText, headerBrush, headerForeground, control, fontWeight: FontWeight.SemiBold);

            for (var rowIndex = 0; rowIndex < previewRowCount; rowIndex++)
            {
                var rowBackground = rowIndex % 2 == 0
                    ? bodyBrush
                    : alternateRowBrush;
                var rowForeground = ParseBrush(control.Foreground, "#0F172A");
                var content = GetPreviewRowValue(field);
                AddDataGridCell(bodyTable, rowIndex, columnIndex, content, rowBackground, rowForeground, control);
            }
        }

        var tableContainer = new Grid
        {
            Background = bodyBrush,
            ClipToBounds = true,
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        tableContainer.Children.Add(headerTable);
        var scrollViewer = new ScrollViewer
        {
            Content = bodyTable,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scrollViewer, 1);
        tableContainer.Children.Add(scrollViewer);

        Grid.SetRow(tableContainer, showGroupPanel ? 2 : 1);
        layout.Children.Add(tableContainer);

        var previewBorder = new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = bodyBrush,
            BorderBrush = separatorBrush,
            BorderThickness = UniformThickness(control.BorderThickness),
            Child = layout
        };

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
        DesignerControlFileModel control,
        FontWeight? fontWeight = null,
        double? fontSize = null)
    {
        var cell = new Border
        {
            Background = background,
            BorderBrush = ParseBrush(control.BorderBrush, "#CBD5E1"),
            BorderThickness = new Thickness(0.5),
            Padding = GetClassicPreviewDataGridCellPadding(control.FontSize),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(control.FontFamily),
                FontSize = fontSize ?? Math.Max(11, control.FontSize),
                FontWeight = fontWeight ?? ParseFontWeight(control.FontWeight),
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
        // Локальная прокрутка тела таблицы, чтобы длинные выборки читались как обычный DataGrid.
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

    private Control CreateDataGridEmptyStatePreview(DesignerControlFileModel control, string title, string description)
    {
        var themePalette = DesignerThemeCatalog.Get(_document.FormTheme);
        var backgroundColor = ParseColor(control.DataGridRowBackground, themePalette.DataGridRowBackground);
        var borderColor = ParseColor(control.DataGridOuterBorderBrush, themePalette.AccentStrongBrush);
        var foregroundColor = ParseColor(control.DataGridRowForeground, "#0F172A");
        var isDark = IsDarkColor(backgroundColor);
        var mutedColor = BlendColor(foregroundColor, isDark ? Color.Parse("#CBD5E1") : Color.Parse("#64748B"), 0.55);

        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = new SolidColorBrush(backgroundColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = UniformThickness(Math.Max(1, control.BorderThickness)),
            CornerRadius = new CornerRadius(Math.Max(0, control.CornerRadius)),
            Padding = new Thickness(18),
            ClipToBounds = true,
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
                        FontFamily = new FontFamily(control.FontFamily),
                        FontSize = Math.Max(12, control.FontSize),
                        FontWeight = FontWeight.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = description,
                        Foreground = new SolidColorBrush(mutedColor),
                        FontFamily = new FontFamily(control.FontFamily),
                        FontSize = Math.Max(11, control.FontSize - 1),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = Math.Max(220, control.Width - 48)
                    }
                }
            }
        };
    }

    private Control CreateModernDataGridPreview(DesignerControlFileModel control)
    {
        return CreateRuntimeDataGridPreview(control);
    }

    private Control CreateRuntimeDataGridPreview(DesignerControlFileModel control)
    {
        var fields = GetBindingFields(control.BindingSourceId).ToList();
        var groupedFields = control.AllowGrouping
            ? fields
                .Where(field => field.GroupOrder >= 0)
                .OrderBy(field => field.GroupOrder)
                .ThenBy(field => field.Header)
                .ToList()
            : new List<BindingFieldFileModel>();
        var visibleFields = fields.Where(field => field.IsVisible).ToList();
        var showGroupPanel = control.AllowGrouping && (control.ShowGroupPanel || groupedFields.Count > 0);

        if (GetBindingSource(control.BindingSourceId) is null)
            return CreateDataGridEmptyStatePreview(control, "DataGrid: источник данных не выбран", "Выберите BindingSource во вкладке Данные.");

        if (fields.Count == 0)
            return CreateDataGridEmptyStatePreview(control, "BindingSource выбран, но поля не добавлены", "Добавьте поля вручную или импортируйте схему из DLL/SQL.");

        if (visibleFields.Count == 0)
            return CreateDataGridEmptyStatePreview(control, "Все поля BindingSource скрыты", "Включите видимость хотя бы одной колонки.");

        var showSummaryFooter = ShouldShowPreviewDataGridSummaryFooter(control.ShowFooter, visibleFields);

        var themePalette = DesignerThemeCatalog.Get(_document.FormTheme);
        var headerBackgroundColor = ParseColor(control.DataGridHeaderBackground, themePalette.DataGridHeaderBackground);
        var bodyBackgroundColor = ParseColor(control.DataGridRowBackground, themePalette.DataGridRowBackground);
        var alternateRowColor = ParseColor(control.DataGridAlternateRowBackground, themePalette.DataGridAlternateRowBackground);
        var outerBorderColor = ParseColor(control.DataGridOuterBorderBrush, themePalette.AccentStrongBrush);
        var gridLineColor = ParseColor(control.DataGridGridLineBrush, "#D7E2EE");
        var rowForegroundColor = ParseColor(control.DataGridRowForeground, "#0F172A");
        var sourceRows = GetCachedPreviewRows(control.BindingSourceId).Count > 0
            ? ClonePreviewWindowRows(GetCachedPreviewRows(control.BindingSourceId))
            : BuildPreviewWindowRows(visibleFields, Math.Max(24, (int)Math.Ceiling(control.Height / Math.Max(18, control.DataGridRowHeight)) + 8));
        var filterValues = GetPreviewDataGridFilterValues(control.Id);
        var visibleRows = new ObservableCollection<Dictionary<string, string>>();
        var collectionView = CreateRuntimeDataGridCollectionView(visibleRows, groupedFields);
        var currentSummaryRows = new List<Dictionary<string, string>>();
        Action refreshFooter = () => { };

        void RefreshRows()
        {
            var filteredRows = ApplyPreviewWindowFilter(
                ClonePreviewWindowRows(sourceRows),
                visibleFields,
                filterValues,
                control.FilterMode);
            filteredRows = ApplyPreviewWindowGroupingOrder(filteredRows, groupedFields);
            currentSummaryRows = filteredRows;

            visibleRows.Clear();
            foreach (var row in filteredRows)
                visibleRows.Add(row);

            collectionView?.Refresh();
            refreshFooter();
        }

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = collectionView is not null ? collectionView : visibleRows,
            Background = new SolidColorBrush(bodyBackgroundColor),
            RowBackground = new SolidColorBrush(bodyBackgroundColor),
            Foreground = new SolidColorBrush(rowForegroundColor),
            BorderBrush = new SolidColorBrush(outerBorderColor),
            BorderThickness = UniformThickness(Math.Max(1, control.BorderThickness)),
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(10, control.DataGridRowFontSize),
            FontWeight = ParseFontWeight(control.DataGridRowFontWeight),
            RowHeight = Math.Max(18, control.DataGridRowHeight),
            ColumnHeaderHeight = Math.Max(24, control.DataGridHeaderHeight),
            HeadersVisibility = control.DataGridShowHeader ? DataGridHeadersVisibility.Column : DataGridHeadersVisibility.None,
            GridLinesVisibility = ResolveDataGridLinesVisibility(control),
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            IsReadOnly = true
        };

        dataGrid.SelectionChanged += (_, _) =>
        {
            var rowValues = dataGrid.SelectedItem is IReadOnlyDictionary<string, string> selectedRow
                ? selectedRow
                : null;
            if (rowValues is null)
            {
                EnsurePreviewRuntimeContext().AddWarning(
                    control.Name,
                    "Preview warnings",
                    $"DataGrid selected item is null for '{control.Name}'.",
                    "Select a row with runtime data before testing SelectionChanged.",
                    CreateRuntimeControlModel(control));
                SyncPreviewRuntimeDiagnostics();
                return;
            }

            if (ApplyRuntimeInteractions(control, InteractionModel.EventDataGridSelectionChanged, rowValues))
            {
                Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
            }
        };

        foreach (var field in visibleFields)
        {
            var column = new DataGridTextColumn
            {
                Header = field.Header,
                Binding = new Binding($"[{field.Path}]"),
                Width = CreateRuntimeDataGridColumnWidth(field.Width),
                MinWidth = Math.Max(0, field.MinWidth),
                CanUserResize = field.AllowResize,
                CanUserSort = field.AllowSort && field.IsSortable,
                SortMemberPath = field.Path,
                Tag = field
            };

            if (field.MaxWidth > 0)
                column.MaxWidth = Math.Max(field.MinWidth, field.MaxWidth);

            dataGrid.Columns.Add(column);
        }

        RefreshRows();

        var rows = new RowDefinitions();
        if (showGroupPanel)
            rows.Add(new RowDefinition(GridLength.Auto));
        rows.Add(new RowDefinition(GridLength.Auto));
        if (control.ShowFilterRow)
            rows.Add(new RowDefinition(GridLength.Auto));
        rows.Add(new RowDefinition(1, GridUnitType.Star));
        if (showSummaryFooter)
            rows.Add(new RowDefinition(GridLength.Auto));

        var layout = new Grid
        {
            Width = control.Width,
            Height = control.Height,
            RowDefinitions = rows,
            ClipToBounds = true
        };

        var rowIndex = 0;
        if (showGroupPanel)
        {
            var chips = new WrapPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            if (groupedFields.Count == 0)
            {
                chips.Children.Add(new Border
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 2),
                    Child = new TextBlock
                    {
                        Text = "Перетащите колонку сюда для группировки",
                        Foreground = new SolidColorBrush(Color.Parse("#0C4A6E")),
                        FontSize = Math.Max(10, control.FontSize - 1),
                        FontWeight = FontWeight.SemiBold
                    }
                });
            }

            foreach (var field in groupedFields)
            {
                chips.Children.Add(CreateRuntimeDataGridGroupChip(control, field));
            }

            if (groupedFields.Count > 0)
                chips.Children.Add(CreateRuntimeDataGridClearGroupingButton(control));

            var groupDropTarget = new Border
            {
                Background = new SolidColorBrush(Color.Parse(groupedFields.Count == 0 ? "#EFF6FF" : "#F8FAFC")),
                BorderBrush = new SolidColorBrush(Color.Parse("#7DD3FC")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 7),
                Margin = new Thickness(0, 0, 0, 8),
                MinHeight = 38,
                Child = chips
            };
            AttachRuntimeDataGridGroupDropTarget(groupDropTarget, control);
            AttachRuntimeDataGridUngroupDropTarget(dataGrid, control);
            AttachRuntimeDataGridHeaderDrag(dataGrid, groupDropTarget, control);

            Grid.SetRow(groupDropTarget, rowIndex++);
            layout.Children.Add(groupDropTarget);
        }

        var titleShell = new Border
        {
            Background = new SolidColorBrush(headerBackgroundColor),
            BorderBrush = new SolidColorBrush(gridLineColor),
            BorderThickness = new Thickness(0, 0, 0, control.DataGridShowRowLines ? 1 : 0),
            Padding = new Thickness(16, 10),
            Child = new TextBlock
            {
                Text = GetModernDataGridTitle(control),
                FontFamily = new FontFamily(control.FontFamily),
                FontSize = Math.Max(14, control.FontSize + 1),
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(ParseColor(control.DataGridHeaderForeground, "#0F172A"))
            }
        };
        Grid.SetRow(titleShell, rowIndex++);
        layout.Children.Add(titleShell);

        if (control.ShowFilterRow)
        {
            var filterGrid = new Grid
            {
                Background = new SolidColorBrush(headerBackgroundColor),
                ColumnDefinitions = new ColumnDefinitions()
            };

            for (var index = 0; index < visibleFields.Count; index++)
            {
                var field = visibleFields[index];
                filterGrid.ColumnDefinitions.Add(CreateRuntimeFilterColumnDefinition(field.Width));
                var textBox = new TextBox
                {
                    Watermark = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header,
                    Text = filterValues.TryGetValue(field.Path, out var value) ? value : string.Empty,
                    IsEnabled = field.AllowFilter,
                    Margin = new Thickness(4),
                    FontSize = Math.Max(10, control.DataGridRowFontSize - 1)
                };
                textBox.TextChanged += (_, _) =>
                {
                    filterValues[field.Path] = textBox.Text ?? string.Empty;
                    RefreshRows();
                };

                Grid.SetColumn(textBox, index);
                filterGrid.Children.Add(textBox);
            }

            Grid.SetRow(filterGrid, rowIndex++);
            layout.Children.Add(filterGrid);
        }

        Grid.SetRow(dataGrid, rowIndex++);
        layout.Children.Add(dataGrid);

        if (showSummaryFooter)
        {
            var footerGrid = new Grid
            {
                Background = new SolidColorBrush(headerBackgroundColor),
                ColumnDefinitions = new ColumnDefinitions()
            };

            foreach (var field in visibleFields)
                footerGrid.ColumnDefinitions.Add(CreateRuntimeFilterColumnDefinition(field.Width));

            refreshFooter = () =>
            {
                footerGrid.Children.Clear();
                for (var index = 0; index < visibleFields.Count; index++)
                {
                    var field = visibleFields[index];
                    var footerCell = CreateRuntimeDataGridFooterCell(
                        control,
                        field,
                        CalculatePreviewDataGridSummaryText(field, currentSummaryRows),
                        new SolidColorBrush(headerBackgroundColor),
                        new SolidColorBrush(gridLineColor),
                        new SolidColorBrush(ParseColor(control.DataGridHeaderForeground, "#0F172A")));

                    Grid.SetColumn(footerCell, index);
                    footerGrid.Children.Add(footerCell);
                }
            };
            refreshFooter();

            Grid.SetRow(footerGrid, rowIndex);
            layout.Children.Add(footerGrid);
        }

        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(outerBorderColor),
            BorderThickness = UniformThickness(Math.Max(1, control.BorderThickness)),
            CornerRadius = new CornerRadius(Math.Max(8, control.CornerRadius)),
            ClipToBounds = true,
            Child = layout
        };
    }

    private void AttachRuntimeDataGridHeaderDrag(DataGrid dataGrid, Control groupDropTarget, DesignerControlFileModel control)
    {
        dataGrid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!control.AllowGrouping || !groupDropTarget.IsVisible)
                return;

            var point = e.GetCurrentPoint(dataGrid);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            var position = point.Position;
            if (IsNearRuntimeDataGridColumnResizeEdge(dataGrid, position))
                return;

            var field = ResolveRuntimeDataGridHeaderField(dataGrid, position);
            if (field is null || !field.IsVisible)
                return;

            _runtimeDataGridHeaderDrag = new RuntimeDataGridHeaderDragState(control, field, dataGrid, position);
        }, RoutingStrategies.Tunnel, true);
    }

    private void AttachRuntimeDataGridGroupDropTarget(Border groupDropTarget, DesignerControlFileModel control)
    {
        var normalBackground = groupDropTarget.Background;
        var activeBackground = new SolidColorBrush(Color.Parse("#DBEAFE"));

        DragDrop.SetAllowDrop(groupDropTarget, true);
        groupDropTarget.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (TryResolveRuntimeDataGridGroupField(e, control, out BindingFieldFileModel? ignoredField))
            {
                e.DragEffects = DragDropEffects.Copy;
                groupDropTarget.Background = activeBackground;
                e.Handled = true;
                return;
            }

            e.DragEffects = DragDropEffects.None;
        });
        groupDropTarget.AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            groupDropTarget.Background = normalBackground;
        });
        groupDropTarget.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            groupDropTarget.Background = normalBackground;
            if (!TryResolveRuntimeDataGridGroupField(e, control, out var field) || field is null)
                return;

            AddPreviewWindowDataGridGroup(control, field);
            e.Handled = true;
        });
    }

    private Border CreateRuntimeDataGridGroupChip(DesignerControlFileModel control, BindingFieldFileModel field)
    {
        var removeButton = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            MinWidth = 22,
            MinHeight = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#0C4A6E")),
            FontSize = Math.Max(12, control.FontSize),
            FontWeight = FontWeight.Bold
        };
        ToolTip.SetTip(removeButton, "Убрать группировку");
        removeButton.Click += (_, e) =>
        {
            RemovePreviewWindowDataGridGroup(control, field);
            e.Handled = true;
        };

        var chip = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E0F2FE")),
            BorderBrush = new SolidColorBrush(Color.Parse("#7DD3FC")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Группа {field.GroupOrder + 1}: {field.Header}",
                        Foreground = new SolidColorBrush(Color.Parse("#0C4A6E")),
                        FontSize = Math.Max(10, control.FontSize - 1),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    removeButton
                }
            }
        };

        chip.PointerPressed += async (_, e) =>
        {
            var pointer = e.GetCurrentPoint(chip);
            if (!pointer.Properties.IsLeftButtonPressed)
                return;

            var data = new DataObject();
            data.Set(RuntimeDataGridUngroupFieldFormat, field.Path);
            data.Set(DataFormats.Text, field.Header);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            e.Handled = true;
        };

        return chip;
    }

    private Button CreateRuntimeDataGridClearGroupingButton(DesignerControlFileModel control)
    {
        var button = new Button
        {
            Content = "Очистить группировку",
            Background = new SolidColorBrush(Color.Parse("#F8FAFC")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            FontSize = Math.Max(10, control.FontSize - 1),
            FontWeight = FontWeight.SemiBold
        };
        ToolTip.SetTip(button, "Снять группировку со всех колонок");
        button.Click += (_, e) =>
        {
            ClearPreviewWindowDataGridGrouping(control);
            e.Handled = true;
        };

        return button;
    }

    private void AttachRuntimeDataGridUngroupDropTarget(Control target, DesignerControlFileModel control)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (TryResolveRuntimeDataGridUngroupField(e, control, out BindingFieldFileModel? ignoredField))
            {
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (e.Data.Contains(RuntimeDataGridUngroupFieldFormat))
                e.DragEffects = DragDropEffects.None;
        });
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (!TryResolveRuntimeDataGridUngroupField(e, control, out var field) || field is null)
                return;

            RemovePreviewWindowDataGridGroup(control, field);
            e.Handled = true;
        });
    }

    private bool TryResolveRuntimeDataGridGroupField(DragEventArgs e, DesignerControlFileModel control, out BindingFieldFileModel? field)
    {
        field = null;
        if (!control.AllowGrouping || !e.Data.Contains(RuntimeDataGridGroupFieldFormat))
            return false;

        var path = e.Data.Get(RuntimeDataGridGroupFieldFormat) as string;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        field = GetBindingFields(control.BindingSourceId)
            .FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

        return field is { IsVisible: true };
    }

    private bool TryResolveRuntimeDataGridUngroupField(DragEventArgs e, DesignerControlFileModel control, out BindingFieldFileModel? field)
    {
        field = null;
        if (!control.AllowGrouping || !e.Data.Contains(RuntimeDataGridUngroupFieldFormat))
            return false;

        var path = e.Data.Get(RuntimeDataGridUngroupFieldFormat) as string;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        field = GetBindingFields(control.BindingSourceId)
            .FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

        return field is { GroupOrder: >= 0 };
    }

    private void AddPreviewWindowDataGridGroup(DesignerControlFileModel control, BindingFieldFileModel targetField)
    {
        if (!control.AllowGrouping)
            return;

        var fields = GetBindingFields(control.BindingSourceId).ToList();
        var field = fields.FirstOrDefault(item => string.Equals(item.Path, targetField.Path, StringComparison.OrdinalIgnoreCase));
        if (field is null)
            return;

        if (field.GroupOrder < 0)
        {
            var nextOrder = fields
                .Where(item => item.GroupOrder >= 0)
                .Select(item => item.GroupOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            field.GroupOrder = nextOrder;
        }

        NormalizePreviewWindowGroupOrders(fields);
        control.ShowGroupPanel = true;
        ScheduleRenderDocument();
    }

    private void RemovePreviewWindowDataGridGroup(DesignerControlFileModel control, BindingFieldFileModel targetField)
    {
        var fields = GetBindingFields(control.BindingSourceId).ToList();
        var field = fields.FirstOrDefault(item => string.Equals(item.Path, targetField.Path, StringComparison.OrdinalIgnoreCase));
        if (field is null || field.GroupOrder < 0)
            return;

        field.GroupOrder = -1;
        NormalizePreviewWindowGroupOrders(fields);
        ScheduleRenderDocument();
    }

    private void ClearPreviewWindowDataGridGrouping(DesignerControlFileModel control)
    {
        var fields = GetBindingFields(control.BindingSourceId).ToList();
        if (!fields.Any(field => field.GroupOrder >= 0))
            return;

        foreach (var field in fields.Where(field => field.GroupOrder >= 0))
            field.GroupOrder = -1;

        ScheduleRenderDocument();
    }

    private static void NormalizePreviewWindowGroupOrders(IReadOnlyList<BindingFieldFileModel> fields)
    {
        var groupedFields = fields
            .Where(field => field.GroupOrder >= 0)
            .OrderBy(field => field.GroupOrder)
            .ThenBy(field => field.Header)
            .ToList();

        for (var index = 0; index < groupedFields.Count; index++)
            groupedFields[index].GroupOrder = index;
    }

    private static BindingFieldFileModel? ResolveRuntimeDataGridHeaderField(DataGrid dataGrid, Point position)
    {
        var headerHeight = Math.Max(20, dataGrid.ColumnHeaderHeight);
        if (position.Y < 0 || position.Y > headerHeight)
            return null;

        var offset = 0d;
        foreach (var column in dataGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            var width = ResolveRuntimeDataGridColumnActualWidth(column);
            if (position.X >= offset && position.X <= offset + width)
                return column.Tag as BindingFieldFileModel;

            offset += width;
        }

        return null;
    }

    private static bool IsNearRuntimeDataGridColumnResizeEdge(DataGrid dataGrid, Point position)
    {
        var headerHeight = Math.Max(20, dataGrid.ColumnHeaderHeight);
        if (position.Y < 0 || position.Y > headerHeight)
            return false;

        var offset = 0d;
        foreach (var column in dataGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            offset += ResolveRuntimeDataGridColumnActualWidth(column);
            if (Math.Abs(position.X - offset) <= 7)
                return true;
        }

        return false;
    }

    private static double ResolveRuntimeDataGridColumnActualWidth(DataGridColumn column)
    {
        if (column.ActualWidth > 0)
            return column.ActualWidth;

        if (column.Width.IsAbsolute && column.Width.Value > 0)
            return column.Width.Value;

        return Math.Max(96, column.MinWidth);
    }

    private static DataGridGridLinesVisibility ResolveDataGridLinesVisibility(DesignerControlFileModel control)
    {
        return (control.DataGridShowRowLines, control.DataGridShowColumnLines) switch
        {
            (true, true) => DataGridGridLinesVisibility.All,
            (true, false) => DataGridGridLinesVisibility.Horizontal,
            (false, true) => DataGridGridLinesVisibility.Vertical,
            _ => DataGridGridLinesVisibility.None
        };
    }

    private static DataGridLength CreateRuntimeDataGridColumnWidth(string? width)
    {
        var normalized = string.IsNullOrWhiteSpace(width) ? "*" : width.Trim();
        if (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            var starText = normalized[..^1];
            var starValue = string.IsNullOrWhiteSpace(starText)
                ? 1
                : double.TryParse(starText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedStar) && parsedStar > 0
                    ? parsedStar
                    : 1;
            return new DataGridLength(starValue, DataGridLengthUnitType.Star);
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixelValue) && pixelValue > 0
            ? new DataGridLength(pixelValue, DataGridLengthUnitType.Pixel)
            : new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    private static ColumnDefinition CreateRuntimeFilterColumnDefinition(string? width)
    {
        var normalized = string.IsNullOrWhiteSpace(width) ? "*" : width.Trim();
        if (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            var starText = normalized[..^1];
            var starValue = string.IsNullOrWhiteSpace(starText)
                ? 1
                : double.TryParse(starText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedStar) && parsedStar > 0
                    ? parsedStar
                    : 1;
            return new ColumnDefinition(starValue, GridUnitType.Star);
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixelValue) && pixelValue > 0
            ? new ColumnDefinition(pixelValue, GridUnitType.Pixel)
            : new ColumnDefinition(1, GridUnitType.Star);
    }

    private Control CreateModernDataGridPreviewCore(DesignerControlFileModel control)
    {
        var fields = GetBindingFields(control.BindingSourceId).ToList();
        var groupedFields = control.AllowGrouping
            ? fields
                .Where(field => field.GroupOrder >= 0)
                .OrderBy(field => field.GroupOrder)
                .ThenBy(field => field.Header)
                .ToList()
            : new List<BindingFieldFileModel>();
        var visibleFields = fields.Where(field => field.IsVisible).ToList();
        var showGroupPanel = control.AllowGrouping && (control.ShowGroupPanel || groupedFields.Count > 0);

        if (GetBindingSource(control.BindingSourceId) is null)
            return CreateDataGridEmptyStatePreview(control, "DataGrid: источник данных не выбран", "Выберите BindingSource во вкладке Данные.");

        if (fields.Count == 0)
            return CreateDataGridEmptyStatePreview(control, "BindingSource выбран, но поля не добавлены", "Добавьте поля вручную или импортируйте схему из DLL/SQL.");

        if (visibleFields.Count == 0)
            return CreateDataGridEmptyStatePreview(control, "Все поля BindingSource скрыты", "Включите видимость хотя бы одной колонки.");

        var themePalette = DesignerThemeCatalog.Get(_document.FormTheme);
        var headerBackgroundColor = ParseColor(control.DataGridHeaderBackground, themePalette.DataGridHeaderBackground);
        var bodyBackgroundColor = ParseColor(control.DataGridRowBackground, themePalette.DataGridRowBackground);
        var alternateRowColor = ParseColor(control.DataGridAlternateRowBackground, themePalette.DataGridAlternateRowBackground);
        var glowColor = ParseColor(control.DataGridGlowColor, themePalette.AccentStrongBrush);
        var outerBorderColor = ParseColor(control.DataGridOuterBorderBrush, themePalette.AccentStrongBrush);
        var gridLineColor = ParseColor(control.DataGridGridLineBrush, "#D7E2EE");
        var borderColor = ParseColor(control.BorderBrush, "#CBD5E1");
        var rowForegroundColor = ParseColor(control.DataGridRowForeground, "#0F172A");
        var hoverRowColor = ParseColor(control.DataGridHoverRowBackground, "#EFF6FF");
        var selectedRowColor = ParseColor(control.DataGridSelectedRowBackground, "#DBEAFE");
        var selectedRowForegroundColor = ParseColor(control.DataGridSelectedRowForeground, "#0F172A");
        var isDarkChrome = IsDarkColor(bodyBackgroundColor);
        var chromeBrush = new SolidColorBrush(bodyBackgroundColor);
        var headerBrush = new SolidColorBrush(headerBackgroundColor);
        var alternateRowBrush = new SolidColorBrush(control.DataGridShowAlternatingRows ? alternateRowColor : bodyBackgroundColor);
        var separatorBrush = new SolidColorBrush(gridLineColor);
        var accentBrush = new SolidColorBrush(glowColor);
        var outerBorderBrush = new SolidColorBrush(outerBorderColor);
        var headerForeground = new SolidColorBrush(ParseColor(control.DataGridHeaderForeground, IsDarkColor(headerBackgroundColor) ? "#F8FAFC" : "#0F172A"));
        var rowForeground = new SolidColorBrush(rowForegroundColor);
        var hoverRowBrush = new SolidColorBrush(hoverRowColor);
        var selectedRowBrush = new SolidColorBrush(selectedRowColor);
        var selectedRowForeground = new SolidColorBrush(selectedRowForegroundColor);
        var titleForeground = new SolidColorBrush(isDarkChrome ? Color.Parse("#F8FAFC") : Color.Parse("#0F172A"));
        var mutedBrush = new SolidColorBrush(BlendPreviewColor(
            rowForegroundColor,
            isDarkChrome ? Color.Parse("#CBD5E1") : Color.Parse("#94A3B8"),
            isDarkChrome ? 0.34 : 0.58));
        var groupChipBackground = new SolidColorBrush(isDarkChrome
            ? Color.FromArgb(34, 96, 165, 250)
            : Color.Parse("#E0F2FE"));
        var groupChipBorder = new SolidColorBrush(isDarkChrome
            ? BlendPreviewColor(borderColor, glowColor, 0.38)
            : BlendPreviewColor(Color.Parse("#BAE6FD"), glowColor, 0.24));
        var groupChipForeground = new SolidColorBrush(isDarkChrome
            ? Color.Parse("#DBEAFE")
            : Color.Parse("#0C4A6E"));
        var filterValues = GetPreviewDataGridFilterValues(control.Id);

        var layout = new Grid
        {
            Width = control.Width,
            Height = control.Height,
            ClipToBounds = true,
            RowDefinitions = showGroupPanel ? new RowDefinitions("Auto,*") : new RowDefinitions("*")
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
                        FontSize = Math.Max(10, control.FontSize - 1),
                        FontWeight = FontWeight.SemiBold
                    }
                });
            }

            foreach (var field in groupedFields)
            {
                chips.Children.Add(CreatePreviewDataGridGroupChip(control, field, groupChipBackground, groupChipBorder, groupChipForeground));
            }

            if (groupedFields.Count > 0)
                chips.Children.Add(CreatePreviewDataGridClearGroupingButton(control, groupChipForeground));

            Grid.SetRow(chips, 0);
            layout.Children.Add(chips);
        }

        var tableChrome = new Border
        {
            Background = chromeBrush,
            BorderBrush = outerBorderBrush,
            BorderThickness = UniformThickness(Math.Max(1, control.BorderThickness)),
            CornerRadius = new CornerRadius(Math.Max(16, control.CornerRadius + 8)),
            ClipToBounds = true,
            Child = new Grid
            {
                Background = chromeBrush,
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                ClipToBounds = true
            }
        };

        Grid.SetRow(tableChrome, showGroupPanel ? 1 : 0);
        layout.Children.Add(tableChrome);

        var tableContainer = (Grid)tableChrome.Child!;
        var titleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("6,*"),
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
            Text = GetModernDataGridTitle(control),
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(14, control.FontSize + 1),
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
            Child = titleGrid
        };
        tableContainer.Children.Add(titleShell);

        var headerTable = new Grid
        {
            Background = Brushes.Transparent
        };

        var bodyTable = new Grid
        {
            Background = Brushes.Transparent
        };

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            headerTable.ColumnDefinitions.Add(CreatePreviewDataGridColumnDefinition(visibleFields[columnIndex]));
            bodyTable.ColumnDefinitions.Add(CreatePreviewDataGridColumnDefinition(visibleFields[columnIndex]));
        }

        var headerHeight = control.DataGridShowHeader ? Math.Max(24, control.DataGridHeaderHeight) : 0;
        var rowHeight = Math.Max(18, control.DataGridRowHeight);
        var cellPadding = UniformThickness(control.DataGridCellPadding);
        var headerCellBorderThickness = new Thickness(0, 0, control.DataGridShowColumnLines ? 1 : 0, 0);
        var bodyCellBorderThickness = new Thickness(0, 0, control.DataGridShowColumnLines ? 1 : 0, control.DataGridShowRowLines ? 1 : 0);
        var groupedAreaHeight = showGroupPanel ? Math.Max(42, rowHeight + 8) : 0;
        var filterHeight = control.ShowFilterRow ? Math.Max(34, Math.Ceiling(rowHeight * 0.95)) : 0;
        var availableRowsHeight = Math.Max(rowHeight, control.Height - headerHeight - filterHeight - groupedAreaHeight - 12);
        var visibleRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(4, (int)Math.Ceiling(availableRowsHeight / rowHeight)));
        var previewRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(18, visibleRowCount + 6));
        var sqlPreviewRows = GetCachedPreviewRows(control.BindingSourceId);
        var usesSqlPreviewRows = sqlPreviewRows.Count > 0;
        var previewRows = ApplyPreviewWindowSort(
            ApplyPreviewWindowFilter(
                usesSqlPreviewRows
                ? ClonePreviewWindowRows(sqlPreviewRows)
                : BuildPreviewWindowRows(visibleFields, previewRowCount),
                visibleFields,
                filterValues,
                control.FilterMode),
            visibleFields);
        var renderedRowCount = Math.Min(MaxPreviewDataGridRows, Math.Max(previewRows.Count, usesSqlPreviewRows ? 1 : previewRowCount));

        headerTable.RowDefinitions.Add(new RowDefinition(headerHeight, GridUnitType.Pixel));
        if (control.ShowFilterRow)
            headerTable.RowDefinitions.Add(new RowDefinition(filterHeight, GridUnitType.Pixel));
        for (var rowIndex = 0; rowIndex < renderedRowCount; rowIndex++)
            bodyTable.RowDefinitions.Add(new RowDefinition(rowHeight, GridUnitType.Pixel));

        var headerShell = new Border
        {
            Background = headerBrush,
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 0, 0, control.DataGridShowRowLines ? 1 : 0),
            Padding = new Thickness(control.DataGridCellPadding, 0),
            Child = headerTable
        };

        for (var columnIndex = 0; columnIndex < visibleFields.Count; columnIndex++)
        {
            var field = visibleFields[columnIndex];
            var headerCell = CreatePreviewDataGridHeaderCell(
                control,
                field,
                Brushes.Transparent,
                separatorBrush,
                headerForeground,
                mutedBrush,
                accentBrush,
                headerCellBorderThickness,
                () => TogglePreviewWindowSort(control, field));

            Grid.SetRow(headerCell, 0);
            Grid.SetColumn(headerCell, columnIndex);
            headerTable.Children.Add(headerCell);

            if (control.ShowFilterRow)
            {
                var filterCell = CreatePreviewDataGridFilterCell(
                    control,
                    field,
                    filterValues,
                    separatorBrush,
                    headerForeground,
                    mutedBrush,
                    new Thickness(0, 0, control.DataGridShowColumnLines ? 1 : 0, control.DataGridShowRowLines ? 1 : 0));

                Grid.SetRow(filterCell, 1);
                Grid.SetColumn(filterCell, columnIndex);
                headerTable.Children.Add(filterCell);
            }

            for (var rowIndex = 0; rowIndex < renderedRowCount; rowIndex++)
            {
                var rowBackground = rowIndex % 2 == 0
                    ? chromeBrush
                    : alternateRowBrush;

                var bodyCell = CreatePreviewDataGridBodyCell(
                    control,
                    field,
                    rowIndex < previewRows.Count
                        ? previewRows[rowIndex].GetValueOrDefault(field.Path, string.Empty)
                        : string.Empty,
                    rowBackground,
                    separatorBrush,
                    rowForeground,
                    mutedBrush,
                    hoverRowBrush,
                    selectedRowBrush,
                    selectedRowForeground,
                    bodyCellBorderThickness,
                    cellPadding,
                    useSemanticFormatting: !usesSqlPreviewRows);

                Grid.SetRow(bodyCell, rowIndex);
                Grid.SetColumn(bodyCell, columnIndex);
                bodyTable.Children.Add(bodyCell);
            }
        }

        if (control.DataGridShowHeader || control.ShowFilterRow)
        {
            Grid.SetRow(headerShell, 1);
            tableContainer.Children.Add(headerShell);
        }
        var scrollViewer = new ScrollViewer
        {
            Content = bodyTable,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Grid.SetRow(scrollViewer, 2);
        tableContainer.Children.Add(scrollViewer);

        var previewBorder = new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = layout
        };

        previewBorder.PointerWheelChanged += (_, e) => HandlePreviewDataGridWheel(scrollViewer, e, rowHeight);
        return previewBorder;
    }

    private string GetModernDataGridTitle(DesignerControlFileModel control)
    {
        if (!string.IsNullOrWhiteSpace(control.Text))
            return control.Text.Trim();

        var source = _document.BindingSources.FirstOrDefault(item => string.Equals(item.Id, control.BindingSourceId, StringComparison.OrdinalIgnoreCase));
        if (source is not null && !string.IsNullOrWhiteSpace(source.Name))
            return source.Name.Trim();

        return string.IsNullOrWhiteSpace(control.Name) ? "Таблица" : control.Name.Trim();
    }

    private Control CreatePreviewDataGridGroupChip(
        DesignerControlFileModel control,
        BindingFieldFileModel field,
        IBrush background,
        IBrush border,
        IBrush foreground)
    {
        var removeButton = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            MinWidth = 22,
            MinHeight = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = foreground,
            FontSize = Math.Max(12, control.FontSize),
            FontWeight = FontWeight.Bold
        };
        ToolTip.SetTip(removeButton, "Убрать группировку");
        removeButton.Click += (_, e) =>
        {
            RemovePreviewWindowDataGridGroup(control, field);
            e.Handled = true;
        };

        return new Border
        {
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Группа {field.GroupOrder + 1}: {field.Header}",
                        Foreground = foreground,
                        FontSize = Math.Max(10, control.FontSize - 1),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    removeButton
                }
            }
        };
    }

    private Button CreatePreviewDataGridClearGroupingButton(DesignerControlFileModel control, IBrush foreground)
    {
        var button = new Button
        {
            Content = "Очистить группировку",
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Foreground = foreground,
            FontSize = Math.Max(10, control.FontSize - 1),
            FontWeight = FontWeight.SemiBold
        };
        ToolTip.SetTip(button, "Снять группировку со всех колонок");
        button.Click += (_, e) =>
        {
            ClearPreviewWindowDataGridGrouping(control);
            e.Handled = true;
        };

        return button;
    }

    private void TogglePreviewWindowSort(DesignerControlFileModel control, BindingFieldFileModel targetField)
    {
        if (!CanPreviewDataGridSort(targetField))
            return;

        var fields = GetBindingFields(control.BindingSourceId).ToList();
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

            field.SortDirection = BindingFieldModel.SortDirectionNone;
            field.SortOrder = -1;
        }

        targetField.SortDirection = nextDirection;
        targetField.SortOrder = string.Equals(nextDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase) ? -1 : 0;
        ScheduleRenderDocument();
    }

    private Border CreatePreviewDataGridHeaderCell(
        DesignerControlFileModel control,
        BindingFieldFileModel field,
        IBrush background,
        IBrush separatorBrush,
        IBrush foreground,
        IBrush mutedBrush,
        IBrush accentBrush,
        Thickness borderThickness,
        Action onClick)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header,
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(8, control.DataGridHeaderFontSize),
            FontWeight = ParseFontWeight(control.DataGridHeaderFontWeight),
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = GetPreviewDataGridHorizontalAlignment(field.HeaderAlignment),
            TextAlignment = GetPreviewDataGridTextAlignment(field.HeaderAlignment),
            TextTrimming = GetPreviewDataGridTextTrimming(field.TextTrimming),
            TextWrapping = GetPreviewDataGridTextWrapping(field.TextWrapping),
            MaxLines = Math.Max(0, field.MaxLines)
        };

        var iconStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        iconStack.Children.Add(CreatePreviewDataGridFilterIcon(CanPreviewDataGridSort(field) ? accentBrush : mutedBrush));

        if (control.AllowGrouping && field.GroupOrder >= 0)
        {
            iconStack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#DBEAFE")),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = (field.GroupOrder + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = Math.Max(10, control.DataGridHeaderFontSize - 2),
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#1D4ED8"))
                }
            });
        }

        if (CanPreviewDataGridSort(field) && !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
        {
            iconStack.Children.Add(new TextBlock
            {
                Text = string.Equals(field.SortDirection, BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase) ? "↓" : "↑",
                FontSize = Math.Max(10, control.DataGridHeaderFontSize - 1),
                FontWeight = FontWeight.Bold,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
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
            Padding = new Thickness(0, 0, Math.Max(4, control.DataGridCellPadding), 0),
            Child = content
        };

        if (CanPreviewDataGridSort(field))
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

    private Border CreatePreviewDataGridFilterCell(
        DesignerControlFileModel control,
        BindingFieldFileModel field,
        IDictionary<string, string> filterValues,
        IBrush separatorBrush,
        IBrush foreground,
        IBrush mutedBrush,
        Thickness borderThickness)
    {
        Control content;
        if (field.AllowFilter)
        {
            var textBox = new TextBox
            {
                Text = filterValues.TryGetValue(field.Path, out var value) ? value : string.Empty,
                Watermark = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header,
                FontFamily = new FontFamily(control.FontFamily),
                FontSize = Math.Max(10, control.DataGridRowFontSize - 1),
                Foreground = foreground,
                Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
                BorderBrush = mutedBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };

            textBox.TextChanged += (_, _) =>
            {
                filterValues[field.Path] = textBox.Text ?? string.Empty;
                Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
            };

            content = textBox;
        }
        else
        {
            content = new TextBlock
            {
                Text = "Без фильтра",
                FontFamily = new FontFamily(control.FontFamily),
                FontSize = Math.Max(10, control.DataGridRowFontSize - 1),
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

    private Border CreatePreviewDataGridBodyCell(
        DesignerControlFileModel control,
        BindingFieldFileModel field,
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
        bool useSemanticFormatting)
    {
        var content = CreatePreviewDataGridValuePresenter(control, field, text, foreground, mutedBrush, useSemanticFormatting);
        var cell = new Border
        {
            Background = background,
            BorderBrush = separatorBrush,
            BorderThickness = borderThickness,
            Padding = padding,
            Child = content
        };

        cell.PointerEntered += (_, _) => cell.Background = hoverBackground;
        cell.PointerExited += (_, _) => cell.Background = background;
        cell.PointerPressed += (_, e) =>
        {
            cell.Background = selectedBackground;
            SetPresenterForeground(content, selectedForeground);
            e.Handled = true;
        };

        return cell;
    }

    private static Border CreateRuntimeDataGridFooterCell(
        DesignerControlFileModel control,
        BindingFieldFileModel field,
        string text,
        IBrush background,
        IBrush separatorBrush,
        IBrush foreground)
    {
        var normalizedSummaryType = BindingFieldModel.NormalizeSummaryType(field.SummaryType);
        return new Border
        {
            Background = background,
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 1, control.DataGridShowColumnLines ? 1 : 0, 0),
            Padding = new Thickness(Math.Max(6, control.DataGridCellPadding), 6),
            MinHeight = Math.Max(30, Math.Ceiling(control.DataGridRowHeight * 0.9)),
            Child = new TextBlock
            {
                Text = normalizedSummaryType == BindingFieldModel.SummaryTypeNone ? string.Empty : text,
                FontFamily = new FontFamily(control.FontFamily),
                FontSize = Math.Max(10, control.DataGridRowFontSize),
                FontWeight = FontWeight.SemiBold,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = GetPreviewDataGridHorizontalAlignment(field.CellAlignment),
                TextAlignment = GetPreviewDataGridTextAlignment(field.CellAlignment),
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
    }

    private static bool ShouldShowPreviewDataGridSummaryFooter(bool showFooter, IEnumerable<BindingFieldFileModel> fields)
    {
        return showFooter && fields.Any(field =>
            BindingFieldModel.NormalizeSummaryType(field.SummaryType) != BindingFieldModel.SummaryTypeNone);
    }

    private static string CalculatePreviewDataGridSummaryText(BindingFieldFileModel field, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var summaryType = BindingFieldModel.NormalizeSummaryType(field.SummaryType);
        if (summaryType == BindingFieldModel.SummaryTypeNone)
            return string.Empty;

        if (summaryType == BindingFieldModel.SummaryTypeCount)
            return FormatPreviewDataGridSummaryValue(field, summaryType, rows.Count);

        var values = rows
            .Select(row => row.TryGetValue(field.Path, out var value) ? value : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (values.Count == 0)
            return string.Empty;

        var numbers = values
            .Select(value => TryParsePreviewWindowNumber(value, out var number) ? (double?)number : null)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToList();

        return summaryType switch
        {
            BindingFieldModel.SummaryTypeSum when numbers.Count > 0 =>
                FormatPreviewDataGridSummaryValue(field, summaryType, numbers.Sum()),
            BindingFieldModel.SummaryTypeAvg when numbers.Count > 0 =>
                FormatPreviewDataGridSummaryValue(field, summaryType, numbers.Average()),
            BindingFieldModel.SummaryTypeMin when numbers.Count > 0 =>
                FormatPreviewDataGridSummaryValue(field, summaryType, numbers.Min()),
            BindingFieldModel.SummaryTypeMax when numbers.Count > 0 =>
                FormatPreviewDataGridSummaryValue(field, summaryType, numbers.Max()),
            BindingFieldModel.SummaryTypeMin =>
                FormatPreviewDataGridSummaryValue(field, summaryType, values.Min(StringComparer.CurrentCultureIgnoreCase) ?? string.Empty),
            BindingFieldModel.SummaryTypeMax =>
                FormatPreviewDataGridSummaryValue(field, summaryType, values.Max(StringComparer.CurrentCultureIgnoreCase) ?? string.Empty),
            _ => string.Empty
        };
    }

    private static string FormatPreviewDataGridSummaryValue(BindingFieldFileModel field, string summaryType, object value)
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
                // Неверный пользовательский формат не должен ломать окно предпросмотра.
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

    private Control CreatePreviewDataGridValuePresenter(
        DesignerControlFileModel control,
        BindingFieldFileModel field,
        string text,
        IBrush foreground,
        IBrush mutedBrush,
        bool useSemanticFormatting)
    {
        var signature = $"{field.Header} {field.Path} {field.TypeName}".ToLowerInvariant();
        var displayText = BindingFieldModel.FormatDisplayValue(text, field.FormatString, field.NullText, field.TypeName);
        var horizontalAlignment = GetPreviewDataGridHorizontalAlignment(field.CellAlignment);
        var textAlignment = GetPreviewDataGridTextAlignment(field.CellAlignment);
        var textTrimming = GetPreviewDataGridTextTrimming(field.TextTrimming);
        var textWrapping = GetPreviewDataGridTextWrapping(field.TextWrapping);
        var maxLines = Math.Max(0, field.MaxLines);

        if (useSemanticFormatting && PreviewDataGridLooksLikePercentage(signature, displayText))
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
                    FontFamily = new FontFamily(control.FontFamily),
                    FontSize = Math.Max(10, control.DataGridRowFontSize - 1),
                    FontWeight = ParseFontWeight(control.DataGridRowFontWeight),
                    Foreground = new SolidColorBrush(Color.Parse(isNegative ? "#B91C1C" : "#15803D")),
                    TextTrimming = textTrimming,
                    TextWrapping = textWrapping,
                    MaxLines = maxLines
                }
            };
        }

        if (useSemanticFormatting && PreviewDataGridLooksLikeBoolean(signature))
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
                    FontFamily = new FontFamily(control.FontFamily),
                    FontSize = Math.Max(10, control.DataGridRowFontSize - 1),
                    FontWeight = ParseFontWeight(control.DataGridRowFontWeight),
                    Foreground = new SolidColorBrush(Color.Parse(isTrue ? "#1D4ED8" : "#475569")),
                    TextTrimming = textTrimming,
                    TextWrapping = textWrapping,
                    MaxLines = maxLines
                }
            };
        }

        if (useSemanticFormatting && PreviewDataGridLooksLikeStatus(signature))
        {
            var (badgeBackground, badgeForeground, badgeBorder) = GetPreviewDataGridStatusPalette(displayText);
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
                    FontFamily = new FontFamily(control.FontFamily),
                    FontSize = Math.Max(10, control.DataGridRowFontSize - 1),
                    FontWeight = ParseFontWeight(control.DataGridRowFontWeight),
                    Foreground = new SolidColorBrush(badgeForeground),
                    TextTrimming = textTrimming,
                    TextWrapping = textWrapping,
                    MaxLines = maxLines
                }
            };
        }

        if (useSemanticFormatting && PreviewDataGridLooksLikeRating(signature))
        {
            return new TextBlock
            {
                Text = BuildPreviewDataGridRatingStars(displayText),
                FontFamily = new FontFamily(control.FontFamily),
                FontSize = Math.Max(11, control.DataGridRowFontSize),
                FontWeight = ParseFontWeight(control.DataGridRowFontWeight),
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
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(11, control.DataGridRowFontSize),
            FontWeight = ParseFontWeight(control.DataGridRowFontWeight),
            Foreground = useSemanticFormatting && PreviewDataGridLooksLikeSecondaryText(signature) ? mutedBrush : foreground,
            TextTrimming = textTrimming,
            TextWrapping = textWrapping,
            MaxLines = maxLines,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = horizontalAlignment,
            TextAlignment = textAlignment
        };

        return textBlock;
    }

    private static HorizontalAlignment GetPreviewDataGridHorizontalAlignment(string? alignment)
    {
        return BindingFieldModel.NormalizeAlignment(alignment) switch
        {
            BindingFieldModel.AlignmentCenter => HorizontalAlignment.Center,
            BindingFieldModel.AlignmentRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };
    }

    private static TextAlignment GetPreviewDataGridTextAlignment(string? alignment)
    {
        return BindingFieldModel.NormalizeAlignment(alignment) switch
        {
            BindingFieldModel.AlignmentCenter => TextAlignment.Center,
            BindingFieldModel.AlignmentRight => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    private static TextTrimming GetPreviewDataGridTextTrimming(string? trimming)
    {
        return BindingFieldModel.NormalizeTextTrimming(trimming) switch
        {
            BindingFieldModel.TextTrimmingNone => TextTrimming.None,
            BindingFieldModel.TextTrimmingWordEllipsis => TextTrimming.WordEllipsis,
            _ => TextTrimming.CharacterEllipsis
        };
    }

    private static TextWrapping GetPreviewDataGridTextWrapping(string? wrapping)
    {
        return BindingFieldModel.NormalizeTextWrapping(wrapping) switch
        {
            BindingFieldModel.TextWrappingWrap => TextWrapping.Wrap,
            _ => TextWrapping.NoWrap
        };
    }

    private static bool CanPreviewDataGridSort(BindingFieldFileModel field) => field.AllowSort && field.IsSortable;

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

    private static Avalonia.Controls.Shapes.Path CreatePreviewDataGridFilterIcon(IBrush brush)
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

    private static ColumnDefinition CreatePreviewDataGridColumnDefinition(BindingFieldFileModel field)
    {
        var definition = CreatePreviewDataGridColumnDefinition(field.Width);
        definition.MinWidth = Math.Max(0, field.MinWidth);
        if (field.MaxWidth > 0)
            definition.MaxWidth = Math.Max(definition.MinWidth, field.MaxWidth);
        return definition;
    }

    private static ColumnDefinition CreatePreviewDataGridColumnDefinition(string? width)
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

    private static List<Dictionary<string, string>> BuildPreviewWindowRows(IReadOnlyList<BindingFieldFileModel> fields, int rowCount)
    {
        var rows = new List<Dictionary<string, string>>(rowCount);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
                row[field.Path] = CreatePreviewWindowValue(field.Header, field.Path, field.TypeName, field.SampleValue, rowIndex);

            rows.Add(row);
        }

        return rows;
    }

    private static List<Dictionary<string, string>> ClonePreviewWindowRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        return rows
            .Select(row => new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<Dictionary<string, string>> ApplyPreviewWindowSort(
        List<Dictionary<string, string>> rows,
        IReadOnlyList<BindingFieldFileModel> fields)
    {
        var sortedField = fields
            .Where(CanPreviewDataGridSort)
            .Where(field => !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase))
            .OrderBy(field => field.SortOrder < 0 ? int.MaxValue : field.SortOrder)
            .FirstOrDefault();

        if (sortedField is null)
            return rows;

        var descending = string.Equals(sortedField.SortDirection, BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase);
        var orderedRows = descending
            ? rows.OrderByDescending(row => GetPreviewWindowSortKey(sortedField, row.GetValueOrDefault(sortedField.Path, string.Empty)))
            : rows.OrderBy(row => GetPreviewWindowSortKey(sortedField, row.GetValueOrDefault(sortedField.Path, string.Empty)));

        return orderedRows.ToList();
    }

    private static List<Dictionary<string, string>> ApplyPreviewWindowGroupingOrder(
        List<Dictionary<string, string>> rows,
        IReadOnlyList<BindingFieldFileModel> groupedFields)
    {
        var activeGroups = groupedFields
            .Where(field => field.GroupOrder >= 0)
            .OrderBy(field => field.GroupOrder)
            .ThenBy(field => field.Header)
            .ToList();

        if (activeGroups.Count == 0)
            return rows;

        IOrderedEnumerable<Dictionary<string, string>>? orderedRows = null;
        foreach (var field in activeGroups)
        {
            orderedRows = orderedRows is null
                ? rows.OrderBy(row => row.GetValueOrDefault(field.Path, string.Empty), StringComparer.OrdinalIgnoreCase)
                : orderedRows.ThenBy(row => row.GetValueOrDefault(field.Path, string.Empty), StringComparer.OrdinalIgnoreCase);
        }

        return orderedRows?.ToList() ?? rows;
    }

    private static DataGridCollectionView? CreateRuntimeDataGridCollectionView(
        ObservableCollection<Dictionary<string, string>> rows,
        IReadOnlyList<BindingFieldFileModel> groupedFields)
    {
        var activeGroups = groupedFields
            .Where(field => field.GroupOrder >= 0)
            .OrderBy(field => field.GroupOrder)
            .ThenBy(field => field.Header)
            .ToList();

        if (activeGroups.Count == 0)
            return null;

        var view = new DataGridCollectionView(rows, isDataSorted: true, isDataInGroupOrder: true);
        foreach (var field in activeGroups)
            view.GroupDescriptions.Add(new PreviewDictionaryDataGridGroupDescription(field));

        return view;
    }

    private static List<Dictionary<string, string>> ApplyPreviewWindowFilter(
        List<Dictionary<string, string>> rows,
        IReadOnlyList<BindingFieldFileModel> fields,
        IReadOnlyDictionary<string, string> filterValues,
        string? filterMode)
    {
        var activeFilters = fields
            .Where(field => field.IsVisible && field.AllowFilter)
            .Select(field => new
            {
                Field = field,
                Query = filterValues.TryGetValue(field.Path, out var value) ? value?.Trim() ?? string.Empty : string.Empty
            })
            .Where(filter => !string.IsNullOrWhiteSpace(filter.Query))
            .ToList();

        if (activeFilters.Count == 0)
            return rows;

        return rows
            .Where(row => activeFilters.All(filter =>
                MatchesPreviewDataGridFilter(
                    row.GetValueOrDefault(filter.Field.Path, string.Empty),
                    filter.Query,
                    filterMode)))
            .ToList();
    }

    private static bool MatchesPreviewDataGridFilter(string? value, string query, string? filterMode)
    {
        var text = value ?? string.Empty;
        return DesignControlModel.NormalizeDataGridFilterMode(filterMode) switch
        {
            DesignControlModel.DataGridFilterModeStartsWith => text.StartsWith(query, StringComparison.OrdinalIgnoreCase),
            DesignControlModel.DataGridFilterModeEquals => string.Equals(text, query, StringComparison.OrdinalIgnoreCase),
            _ => text.Contains(query, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static (int Kind, double Number, DateTime Date, string Text) GetPreviewWindowSortKey(BindingFieldFileModel field, string value)
    {
        if (TryParsePreviewWindowNumber(value, out var number))
            return (0, number, default, string.Empty);

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return (1, 0, date, string.Empty);
        }

        return (2, 0, default, value ?? string.Empty);
    }

    private static string CreatePreviewWindowValue(string header, string path, string typeName, string sampleValue, int rowIndex)
    {
        var signature = $"{header} {path} {typeName}".ToLowerInvariant();

        if (PreviewDataGridLooksLikeRating(signature))
            return rowIndex switch
            {
                0 => "4.9",
                1 => "4.2",
                2 => "3.8",
                3 => "4.6",
                4 => "2.9",
                _ => ((rowIndex % 5) + 1).ToString("0.0", CultureInfo.InvariantCulture)
            };

        if (PreviewDataGridLooksLikePercentage(signature, sampleValue))
            return rowIndex switch
            {
                0 => "2.2%",
                1 => "-1.9%",
                2 => "4.7%",
                3 => "0.1%",
                4 => "3.6%",
                _ => $"{((rowIndex % 7) - 2) * 1.1:0.0}%"
            };

        if (PreviewDataGridLooksLikeStatus(signature))             return rowIndex switch             {                 0 => "??????? ????????",                 1 => "??????? ????",                 2 => "????? ???",                 3 => "??????? ???????",                 4 => "? ??????",                 _ => "???????"             }; 
        if (PreviewDataGridLooksLikeBoolean(signature))
            return rowIndex % 2 == 0 ? "Да" : "Нет";

        if (PreviewDataGridLooksLikeCurrency(signature))
            return $"{12500 + (rowIndex * 1450):N0} ₽";

        if (PreviewDataGridLooksLikeDate(signature))
            return DateTime.Today.AddDays(-rowIndex * 3).ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

        if (PreviewDataGridLooksLikeNumeric(signature, sampleValue))
        {
            if (TryParsePreviewWindowNumber(sampleValue, out var sampleNumber))
                return (sampleNumber + rowIndex).ToString("0.##", CultureInfo.InvariantCulture);

            return (1000 + rowIndex).ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(sampleValue))
            return rowIndex == 0 ? sampleValue : $"{sampleValue} • {rowIndex + 1}";

        return rowIndex == 0 ? (string.IsNullOrWhiteSpace(header) ? path : header) : $"{(string.IsNullOrWhiteSpace(header) ? path : header)} {rowIndex + 1}";
    }

    private static bool TryParsePreviewWindowNumber(string? value, out double number)
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

    private static bool PreviewDataGridLooksLikeEmail(string signature)
    {
        return signature.Contains("email", StringComparison.Ordinal)
            || signature.Contains("mail", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeName(string signature)
    {
        return signature.Contains("name", StringComparison.Ordinal)
            || signature.Contains("fio", StringComparison.Ordinal)
            || signature.Contains("full", StringComparison.Ordinal)
            || signature.Contains("customer", StringComparison.Ordinal)
            || signature.Contains("client", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeLocation(string signature)
    {
        return signature.Contains("province", StringComparison.Ordinal)
            || signature.Contains("country", StringComparison.Ordinal)
            || signature.Contains("city", StringComparison.Ordinal)
            || signature.Contains("region", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeDate(string signature)
    {
        return signature.Contains("date", StringComparison.Ordinal)
            || signature.Contains("created", StringComparison.Ordinal)
            || signature.Contains("updated", StringComparison.Ordinal)
            || signature.Contains("time", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeBoolean(string signature)
    {
        return signature.Contains("bool", StringComparison.Ordinal)
            || signature.Contains("active", StringComparison.Ordinal)
            || signature.Contains("enabled", StringComparison.Ordinal)
            || signature.Contains("available", StringComparison.Ordinal)
            || signature.Contains("visible", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeRating(string signature)
    {
        return signature.Contains("rating", StringComparison.Ordinal)
            || signature.Contains("score", StringComparison.Ordinal)
            || signature.Contains("rank", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeStatus(string signature)
    {
        return signature.Contains("status", StringComparison.Ordinal)
            || signature.Contains("state", StringComparison.Ordinal)
            || signature.Contains("stage", StringComparison.Ordinal)
            || signature.Contains("result", StringComparison.Ordinal)
            || signature.Contains("workflow", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikeCurrency(string signature)
    {
        return signature.Contains("price", StringComparison.Ordinal)
            || signature.Contains("cost", StringComparison.Ordinal)
            || signature.Contains("amount", StringComparison.Ordinal)
            || signature.Contains("sum", StringComparison.Ordinal)
            || signature.Contains("total", StringComparison.Ordinal)
            || signature.Contains("salary", StringComparison.Ordinal);
    }

    private static bool PreviewDataGridLooksLikePercentage(string signature, string? value)
    {
        return signature.Contains("%", StringComparison.Ordinal)
            || signature.Contains("percent", StringComparison.Ordinal)
            || signature.Contains("rate", StringComparison.Ordinal)
            || signature.Contains("gdp", StringComparison.Ordinal)
            || signature.Contains("unemployment", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(value) && value.Contains('%', StringComparison.Ordinal));
    }

    private static bool PreviewDataGridLooksLikeSecondaryText(string signature)
    {
        return PreviewDataGridLooksLikeEmail(signature)
            || signature.Contains("description", StringComparison.Ordinal)
            || signature.Contains("comment", StringComparison.Ordinal);
    }

    private static (Color Background, Color Foreground, Color Border) GetPreviewDataGridStatusPalette(string text)
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

    private static bool PreviewDataGridLooksLikeNumeric(string signature, string? value)
    {
        if (PreviewDataGridLooksLikePercentage(signature, value)
            || PreviewDataGridLooksLikeCurrency(signature)
            || PreviewDataGridLooksLikeRating(signature))
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
            || TryParsePreviewWindowNumber(value, out _);
    }

    private static string BuildPreviewDataGridRatingStars(string text)
    {
        if (!TryParsePreviewWindowNumber(text, out var value))
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

    private static Color BlendPreviewColor(Color from, Color to, double amount)
    {
        var mix = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(from.A + ((to.A - from.A) * mix)),
            (byte)Math.Round(from.R + ((to.R - from.R) * mix)),
            (byte)Math.Round(from.G + ((to.G - from.G) * mix)),
            (byte)Math.Round(from.B + ((to.B - from.B) * mix)));
    }

    private static string GetPreviewRowValue(BindingFieldFileModel field)
    {
        if (!string.IsNullOrWhiteSpace(field.SampleValue))
            return field.SampleValue;

        return !string.IsNullOrWhiteSpace(field.Header)
            ? field.Header
            : field.Path;
    }

    private TextBlock CreatePreviewText(
        string text,
        DesignerControlFileModel control,
        string foreground,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = ParseBrush(foreground, "#0F172A"),
            FontFamily = new FontFamily(control.FontFamily),
            FontSize = Math.Max(8, control.FontSize),
            FontWeight = ParseFontWeight(control.FontWeight),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment
        };
    }

    private IReadOnlyList<DesignerControlFileModel> GetChildControlsInVisualOrder(string? parentId, string? layoutMode)
    {
        var children = GetChildControls(parentId).ToList();
        var normalizedLayoutMode = DesignerLayoutModes.NormalizeMode(layoutMode);
        if (normalizedLayoutMode == DesignerLayoutModes.Stack)
        {
            return children
                .Select((control, index) => new { Control = control, Index = index })
                .OrderBy(item => Math.Max(0, item.Control.StackOrder))
                .ThenBy(item => item.Index)
                .Select(item => item.Control)
                .ToList();
        }

        if (!DesignerLayoutModes.IsAbsolute(normalizedLayoutMode))
            return children;

        return children
            .Select((control, index) => new { Control = control, Index = index })
            .OrderBy(item => GetImplicitCanvasLayer(item.Control, children))
            .ThenBy(item => item.Index)
            .Select(item => item.Control)
            .ToList();
    }

    private int GetCanvasVisualZIndex(DesignerControlFileModel control)
    {
        var parentLayoutMode = GetPreviewLayoutModeForParent(control.ParentId);
        if (!DesignerLayoutModes.IsAbsolute(parentLayoutMode))
            return 0;

        var orderedSiblings = GetChildControlsInVisualOrder(control.ParentId, parentLayoutMode);
        for (var index = 0; index < orderedSiblings.Count; index++)
        {
            if (string.Equals(orderedSiblings[index].Id, control.Id, StringComparison.Ordinal))
                return index;
        }

        return 0;
    }

    private string GetPreviewLayoutModeForParent(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(NormalizeParentId(parentId)))
            return DesignerLayoutModes.NormalizeMode(_document.SurfaceLayoutMode);

        var parent = GetControl(parentId);
        if (parent is null)
            return DesignerLayoutModes.Absolute;

        if (!string.IsNullOrWhiteSpace(parent.ChildLayoutMode))
            return DesignerLayoutModes.NormalizeMode(parent.ChildLayoutMode);

        try
        {
            return DesignerLayoutModes.NormalizeMode(_registry.GetRequiredControl(parent.Type).ChildLayoutMode);
        }
        catch
        {
            return DesignerLayoutModes.Absolute;
        }
    }

    private DesignerControlFileModel? GetControl(string? id)
    {
        var normalizedId = NormalizeParentId(id);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return null;

        return _document.Controls.FirstOrDefault(control =>
            string.Equals(control.Id, normalizedId, StringComparison.Ordinal));
    }

    private int GetImplicitCanvasLayer(DesignerControlFileModel control, IReadOnlyList<DesignerControlFileModel> siblings)
    {
        return IsBackgroundBorder(control, siblings) ? 0 : 1;
    }

    private bool IsBackgroundBorder(DesignerControlFileModel control, IReadOnlyList<DesignerControlFileModel> siblings)
    {
        if (!string.Equals(control.Type, DesignerControlTypes.Border, StringComparison.OrdinalIgnoreCase))
            return false;

        if (GetChildControls(control.Id).Any())
            return false;

        var borderWidth = Math.Max(0, control.Width);
        var borderHeight = Math.Max(0, control.Height);
        if (borderWidth <= 0 || borderHeight <= 0)
            return false;

        return siblings.Any(sibling =>
            !string.Equals(sibling.Id, control.Id, StringComparison.Ordinal)
            && !string.Equals(sibling.Type, DesignerControlTypes.Border, StringComparison.OrdinalIgnoreCase)
            && sibling.IsVisible
            && CanvasBoundsContains(
                control.X,
                control.Y,
                borderWidth,
                borderHeight,
                sibling.X,
                sibling.Y,
                Math.Max(0, sibling.Width),
                Math.Max(0, sibling.Height)));
    }

    private static bool CanvasBoundsContains(
        double outerX,
        double outerY,
        double outerWidth,
        double outerHeight,
        double innerX,
        double innerY,
        double innerWidth,
        double innerHeight)
    {
        const double tolerance = 1.0;
        return innerWidth > 0
            && innerHeight > 0
            && innerX >= outerX - tolerance
            && innerY >= outerY - tolerance
            && innerX + innerWidth <= outerX + outerWidth + tolerance
            && innerY + innerHeight <= outerY + outerHeight + tolerance;
    }

    private IEnumerable<DesignerControlFileModel> GetChildControls(string? parentId)
    {
        return _document.Controls
            .Where(control => string.Equals(NormalizeParentId(control.ParentId), NormalizeParentId(parentId), StringComparison.Ordinal))
            .ToList();
    }

    private BindingSourceFileModel? GetBindingSource(string? bindingSourceId)
    {
        return _document.BindingSources.FirstOrDefault(source =>
            string.Equals(source.Id, bindingSourceId ?? string.Empty, StringComparison.Ordinal));
    }

    private IEnumerable<BindingFieldFileModel> GetBindingFields(string? bindingSourceId)
    {
        return OrderBindingFieldsForDisplay(GetBindingSource(bindingSourceId)?.Fields ?? Enumerable.Empty<BindingFieldFileModel>());
    }

    private static IEnumerable<BindingFieldFileModel> OrderBindingFieldsForDisplay(IEnumerable<BindingFieldFileModel> fields)
    {
        return fields
            .Select((field, index) => new { Field = field, Index = index })
            .OrderBy(item => item.Field.VisibleIndex < 0 ? int.MaxValue : item.Field.VisibleIndex)
            .ThenBy(item => item.Index)
            .Select(item => item.Field);
    }

    private bool CanHostChildren(DesignerControlFileModel control)
    {
        return _registry.GetRequiredControl(control.Type).CanHostChildren;
    }

    private Bitmap? TryLoadBitmap(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (_imageCache.TryGetValue(source, out var cached))
            return cached;

        try
        {
            Bitmap? bitmap = null;

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
            else if (Path.IsPathRooted(source) && File.Exists(source))
            {
                using var stream = File.OpenRead(source);
                bitmap = new Bitmap(stream);
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

    private void ShowLoading(string title, string description)
    {
        LoadingTitleText.Text = title;
        LoadingDescriptionText.Text = description;
        LoadingOverlay.IsVisible = true;
    }

    private void HideLoading()
    {
        LoadingOverlay.IsVisible = false;
    }

    private static string NormalizeParentId(string? parentId)
    {
        return string.IsNullOrWhiteSpace(parentId) ? string.Empty : parentId.Trim();
    }

    private static WindowState NormalizeWindowState(string? value)
    {
        return value?.Trim() switch
        {
            MainWindowViewModel.WindowStateMaximized or "Заполнить рабочую область" or "Развернутое" or "Maximized" => WindowState.Maximized,
            MainWindowViewModel.WindowStateFullScreen or "FullScreen" => WindowState.FullScreen,
            _ => WindowState.Normal
        };
    }

    private static WindowStartupLocation NormalizeStartupLocation(string? value)
    {
        return value?.Trim() switch
        {
            MainWindowViewModel.StartupLocationCenterOwner or "CenterOwner" => WindowStartupLocation.CenterOwner,
            MainWindowViewModel.StartupLocationManual or "Manual" => WindowStartupLocation.Manual,
            _ => WindowStartupLocation.CenterScreen
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
            return new SolidColorBrush(Color.Parse(fallback));
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

    private static Color BlendColor(Color from, Color to, double amount)
    {
        var ratio = Math.Clamp(amount, 0d, 1d);
        static byte Mix(byte a, byte b, double ratio) => (byte)Math.Round(a + ((b - a) * ratio));
        return Color.FromArgb(
            Mix(from.A, to.A, ratio),
            Mix(from.R, to.R, ratio),
            Mix(from.G, to.G, ratio),
            Mix(from.B, to.B, ratio));
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

    private sealed class RuntimeDataGridHeaderDragState
    {
        public RuntimeDataGridHeaderDragState(
            DesignerControlFileModel control,
            BindingFieldFileModel field,
            DataGrid dataGrid,
            Point startPosition)
        {
            Control = control;
            Field = field;
            DataGrid = dataGrid;
            StartPosition = startPosition;
        }

        public DesignerControlFileModel Control { get; }

        public BindingFieldFileModel Field { get; }

        public DataGrid DataGrid { get; }

        public Point StartPosition { get; }

        public bool IsDragDropActive { get; set; }
    }

    private sealed class PreviewDictionaryDataGridGroupDescription : DataGridGroupDescription
    {
        private readonly string _path;
        private readonly string _propertyName;

        public PreviewDictionaryDataGridGroupDescription(BindingFieldFileModel field)
        {
            _path = field.Path;
            _propertyName = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header;
        }

        public override string PropertyName => _propertyName;

        public override object GroupKeyFromItem(object item, int level, CultureInfo culture)
        {
            if (item is IReadOnlyDictionary<string, string> row
                && row.TryGetValue(_path, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return "(пусто)";
        }
    }
}

