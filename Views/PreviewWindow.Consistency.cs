using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.Views;

public sealed record PreviewControlDiagnostic(string Event, string FormId, string ControlId, string Type, string ParentId,
    string PreviewMode, bool Created, bool Attached, bool Visible, double X, double Y, double Width, double Height,
    string NativeType, string FailureReason);

public partial class PreviewWindow
{
    private const string ConsistencyCategory = "Preview consistency";
    private readonly bool _detailedPreviewDiagnostics = Environment.GetEnvironmentVariable("FORMDESIGNER_PREVIEW_DIAGNOSTICS") == "1";
    private readonly Dictionary<string, Control> _builtPreviewControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _previewControlFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedPreviewProviders = new(StringComparer.Ordinal);
    private readonly List<PreviewControlDiagnostic> _previewControlDiagnostics = new();
    private readonly Dictionary<string, string> _importedSourceTypes = new(StringComparer.Ordinal);
    private bool _importedSourceHasResources;
    private long _previewBuildGeneration;
    private string _actualPreviewBuildMode = "";
    public IReadOnlyList<PreviewControlDiagnostic> ControlDiagnostics => _previewControlDiagnostics;
    public IReadOnlyList<DocumentDiagnosticModel> BuildIssues => _previewRuntimeService.Current?.Diagnostics
        .Where(d => d.Category == ConsistencyCategory || d.Category == "Preview binding").ToArray() ?? Array.Empty<DocumentDiagnosticModel>();
    public event EventHandler<PreviewControlDiagnostic>? BuildDiagnosticEmitted;

    // Copy only value metadata; never retain a source session, VM or source map.
    public void SetImportedSourceContext(FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlRoundTripDocument source)
    {
        _importedSourceTypes.Clear();
        foreach (var reference in source.SourceMap.Controls)
            _importedSourceTypes[reference.ControlId] = reference.Element.LocalName;
        _importedSourceHasResources = XDocument.Parse(source.OriginalText).Descendants()
            .Any(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal) || e.Name.LocalName.EndsWith(".Styles", StringComparison.Ordinal));
    }

    private void BeginPreviewBuild()
    {
        var fallback = _exportedAxamlPreview is not null && _isRenderingDocument;
        _previewBuildGeneration++;
        _builtPreviewControls.Clear();
        if (!fallback)
        {
            _previewControlDiagnostics.Clear(); _previewControlFailures.Clear(); _failedPreviewProviders.Clear();
            EnsurePreviewRuntimeContext().RemoveDiagnostics(ConsistencyCategory);
        }
        _actualPreviewBuildMode = fallback ? "AXAML/LegacyFallback" : _previewLifecycleMode;
        EmitPreviewBuild("PREVIEW_BUILD_START");
        foreach (var model in _document.Controls)
        {
            EmitPreviewBuild("PREVIEW_CONTROL_DISCOVERED", model);
            if (!_registry.TryGetControl(model.Type, out _))
                ReportPreviewFailure(model, "MissingDescriptor", $"Не удалось отобразить компонент {model.Name}: тип {model.Type} недоступен.");
            if (_importedSourceTypes.TryGetValue(model.Id, out var sourceType))
            {
                var projectedType = model.Type == DesignerControlTypes.Group ? model.ChildLayoutMode switch
                {
                    DesignerLayoutModes.Grid => "Grid", DesignerLayoutModes.Stack => "StackPanel",
                    DesignerLayoutModes.Dock => "DockPanel", DesignerLayoutModes.Flex => "WrapPanel", _ => "Canvas"
                } : model.Type;
                if (sourceType != projectedType)
                    ReportPreviewFailure(model, "UnsupportedControl", $"Компонент {model.Name} ({sourceType}) представлен упрощённой моделью {projectedType}. Preview и экспорт модели не воспроизводят исходный тип AXAML.");
            }
        }
        if (_importedSourceHasResources)
            ReportPreviewFailure(null, "ResourceResolutionFailed", "Preview и экспорт модели не переносят произвольные ресурсы и стили импортированного AXAML. Исходный файл сохраняется через source-preserving patch.", code: "PREVIEW_RESOURCE_FAILED");
    }

    private void TrackPreviewControl(DesignerControlFileModel model, Control view)
    {
        _builtPreviewControls[model.Id] = view;
        EmitPreviewBuild("PREVIEW_CONTROL_CREATED", model, view);
    }

    private void ReportPreviewResourceFailure(string provider, Exception exception)
    {
        _failedPreviewProviders.Add(provider);
        ReportPreviewFailure(null, "ThemeMissing", $"Не удалось применить ресурсы поставщика {provider}. Стандартные компоненты остаются доступны.", exception,
            "PREVIEW_RESOURCE_FAILED");
    }

    private void ReportPreviewFailure(DesignerControlFileModel? model, string reason, string message, Exception? exception = null,
        string code = "PREVIEW_CONTROL_FAILED")
    {
        if (model is not null && _previewControlFailures.GetValueOrDefault(model.Id) == reason) return;
        if (model is not null) _previewControlFailures[model.Id] = reason;
        var context = EnsurePreviewRuntimeContext();
        context.AddWarning("Preview", reason == "BindingFailed" ? "Preview binding" : ConsistencyCategory, message, "Проверьте компонент, layout и доступность зависимости.",
            model is null ? null : context.FindControlById(model.Id));
        // Never put raw AXAML, binding values, connection strings or exception messages in events.
        EmitPreviewBuild(code, model, reason: reason + (exception is null ? "" : ":" + exception.GetType().Name), important: true);
        SyncPreviewRuntimeDiagnostics();
        RuntimeBadgeOverlay.IsVisible = true;
    }

    private void CompletePreviewBuild()
    {
        var generation = _previewBuildGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (_databasePreviewClosed || generation != _previewBuildGeneration) return;
            UpdateLayout();
            var root = PreviewSurfaceBorder.Child;
            if (_actualPreviewBuildMode == "AXAML" && root is Control content)
            {
                var named = content.GetVisualDescendants().OfType<Control>().Prepend(content)
                    .Where(c => !string.IsNullOrEmpty(c.Name)).GroupBy(c => c.Name!).ToDictionary(g => g.Key, g => g.First());
                foreach (var model in _document.Controls)
                {
                    var name = _exportedAxamlPreview!.StandardControlNames.TryGetValue(model.Id, out var exported) ? exported : model.Name;
                    if (named.TryGetValue(name, out var actual)) TrackPreviewControl(model, actual);
                }
            }
            foreach (var model in _document.Controls)
            {
                _builtPreviewControls.TryGetValue(model.Id, out var view);
                var attached = view?.GetVisualRoot() == this;
                var authoredHidden = IsAuthoredPreviewHidden(model);
                if (view is null && !authoredHidden && !_previewControlFailures.ContainsKey(model.Id))
                {
                    var invalidParent = !string.IsNullOrEmpty(model.ParentId) && !_document.Controls.Any(c => c.Id == model.ParentId);
                    ReportPreviewFailure(model, invalidParent ? "InvalidParent" : "LayoutProjectionFailed",
                        $"Не удалось отобразить компонент {model.Name}: он отсутствует в визуальном дереве Preview.");
                }
                var reason = _previewControlFailures.GetValueOrDefault(model.Id, "");
                if (reason.Length == 0 && authoredHidden) reason = "AuthoredHidden";
                else if (reason.Length == 0 && view is not null && (!view.IsVisible || view.Opacity <= 0)) reason = "Visibility/Clipping";
                else if (reason.Length == 0 && view is not null && attached && (view.Bounds.Width <= 0 || view.Bounds.Height <= 0)) reason = "ZeroBounds";
                EmitPreviewBuild(attached ? "PREVIEW_LAYOUT_COMPLETED" : "PREVIEW_CONTROL_SKIPPED", model, view, reason);
            }
            if (BuildIssues.Count > 0) _lastSyncedPreviewDiagnosticsVersion = -1;
            SyncPreviewRuntimeDiagnostics();
            if (BuildIssues.Count > 0) RuntimeBadgeOverlay.IsVisible = true;
            EmitPreviewBuild("PREVIEW_BUILD_SUMMARY", reason: $"models={_document.Controls.Count}; attached={_builtPreviewControls.Values.Count(c => c.GetVisualRoot() == this)}; issues={BuildIssues.Count}", important: true);
        }, DispatcherPriority.Background);
    }

    private void EmitPreviewBuild(string code, DesignerControlFileModel? model = null, Control? view = null, string reason = "", bool important = false)
    {
        if (!_detailedPreviewDiagnostics && !important) return;
        var point = view?.TranslatePoint(default, PreviewSurfaceBorder) ?? default;
        var visible = view is not null && view.IsVisible && view.Opacity > 0 && view.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible && c.Opacity > 0);
        var record = new PreviewControlDiagnostic(code, string.IsNullOrEmpty(_currentFormId) ? "standalone-preview" : _currentFormId,
            model?.Id ?? "", model?.Type ?? "", model?.ParentId ?? "", _actualPreviewBuildMode,
            view is not null && !_previewControlFailures.ContainsKey(model?.Id ?? ""), view?.GetVisualRoot() == this, visible,
            point.X, point.Y, view?.Bounds.Width ?? 0, view?.Bounds.Height ?? 0, view?.GetType().FullName ?? "", reason);
        _previewControlDiagnostics.Add(record);
        Trace.WriteLine(code + " " + JsonSerializer.Serialize(record));
        BuildDiagnosticEmitted?.Invoke(this, record);
    }

    private void ApplyPreviewRootTheme(string axaml)
    {
        var value = XDocument.Parse(axaml).Root?.Attribute("RequestedThemeVariant")?.Value;
        if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase)) RequestedThemeVariant = ThemeVariant.Dark;
        else if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)) RequestedThemeVariant = ThemeVariant.Light;
    }

    private bool IsAuthoredPreviewHidden(DesignerControlFileModel model)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var node = model; node is not null && visited.Add(node.Id);
             node = _document.Controls.FirstOrDefault(c => c.Id == node.ParentId))
            if (!IsRuntimeControlVisible(node) || node.Opacity <= 0) return true;
        return false;
    }
}
