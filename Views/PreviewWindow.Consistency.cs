using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.Views;

public sealed record PreviewControlDiagnostic(string Event, string FormId, string ControlId, string Type, string ParentId,
    string PreviewMode, bool Created, bool Attached, bool Visible, double X, double Y, double Width, double Height,
    string NativeType, string FailureReason)
{
    public PreviewRenderCapability RenderCapability { get; init; }
}
public enum PreviewRenderCapability { FullyRendered, PartiallyRendered, Placeholder, Opaque, Unsupported }

public partial class PreviewWindow
{
    private const string ConsistencyCategory = "Preview consistency";
    private readonly bool _detailedPreviewDiagnostics = Environment.GetEnvironmentVariable("FORMDESIGNER_PREVIEW_DIAGNOSTICS") == "1";
    private readonly Dictionary<string, Control> _builtPreviewControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _previewControlFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedPreviewProviders = new(StringComparer.Ordinal);
    private readonly List<PreviewControlDiagnostic> _previewControlDiagnostics = new();
    private FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlRoundTripDocument? _importedNativeSource;
    private bool _importedSourceHasResources;
    private string[] _importedRootGaps = Array.Empty<string>();
    private long _previewBuildGeneration;
    private string _actualPreviewBuildMode = "";
    public IReadOnlyList<PreviewControlDiagnostic> ControlDiagnostics => _previewControlDiagnostics;
    public IReadOnlyList<DocumentDiagnosticModel> BuildIssues => _previewRuntimeService.Current?.Diagnostics
        .Where(d => d.Category == ConsistencyCategory || d.Category == "Preview binding").ToArray() ?? Array.Empty<DocumentDiagnosticModel>();
    public event EventHandler<PreviewControlDiagnostic>? BuildDiagnosticEmitted;

    // The window owns its source baseline wrapper. Syntax/map contain metadata only;
    // a later editor ACK replaces its baseline and cannot mutate this snapshot.
    public void SetImportedSourceContext(FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlRoundTripDocument source)
    {
        _importedNativeSource = new(source.SourcePath, source.Syntax, source.SourceMap, source.CapabilityReport);
        ApplyPreviewRootTheme(source.OriginalText);
        _importedSourceHasResources = FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportStructureReport.Descendants(source.Syntax.Root)
            .Any(e => e.LocalName.EndsWith(".Resources", StringComparison.Ordinal) || e.LocalName.EndsWith(".Styles", StringComparison.Ordinal)
                || e.LocalName.EndsWith(".DataTemplates", StringComparison.Ordinal)
                || e.LocalName is "DataTemplate" or "ControlTemplate" or "TreeDataTemplate" or "ItemsPanelTemplate");
        _importedRootGaps = source.Syntax.Root.Attributes.Where(a => a.Name != "xmlns" && !a.Name.StartsWith("xmlns:", StringComparison.Ordinal)
            && (a.Name is not ("x:Class" or "x:Name" or "Name" or "Title" or "Width" or "Height" or "Background" or "RequestedThemeVariant")
                || System.Net.WebUtility.HtmlDecode(a.Value).TrimStart().StartsWith('{')))
            .Select(a => a.Name).Distinct().ToArray();
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

        }
        if (_importedSourceHasResources)
            ReportPreviewFailure(null, "ResourceResolutionFailed", "Импортированные шаблоны, сложные стили и внешние ресурсы доступны лишь частично. Поддерживаются простые стили окна. Исходный AXAML не изменяется.", code: "PREVIEW_RESOURCE_FAILED");
        if (_importedRootGaps.Length > 0)
            ReportPreviewFailure(null, "PartialSourceValue", "Корневые свойства импортированного окна не проецируются: " + string.Join(", ", _importedRootGaps) + ". Исходный AXAML сохранён.", code: "PREVIEW_SOURCE_PROPERTY_PARTIAL");
    }

    private bool RenderImportedSource()
    {
        BeginPreviewBuild();
        using var stage = LayoutDropProfile.Stage("AXAML.PreviewProjection");
        var source = _importedNativeSource!;
        var models = _document.Controls.Select(CreateRuntimeControlModel).ToArray();
        (Control Root, IReadOnlyDictionary<string, Control> Controls) tree;
        try { tree = FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlLayoutProjection.CreateNativeTree(source, models); }
        catch (Exception exception)
        {
            ReportPreviewFailure(null, "LayoutProjectionFailed", "Не удалось построить безопасную проекцию импортированного AXAML. Исходный текст сохранён без изменений.", exception);
            CompletePreviewBuild();
            return false;
        }
        PreviewSurfaceBorder.Child = tree.Root;
        foreach (var model in _document.Controls)
            if (tree.Controls.TryGetValue(model.Id, out var control)) TrackPreviewControl(model, control);
        var partial = new List<string>();
        foreach (var reference in source.SourceMap.Controls)
        {
            var unsupported = reference.Capability.Properties.Where(p => p.Mode != FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlPropertyCapabilityMode.Editable
                && p.Reason is not ("VisualContentChild" or "TextContentPreserved" or "DefinitionElementsPreserved")
                && FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlControlMetadata.HasSourceValue(reference.Element, p.SourceName)).Select(p => p.SourceName).Distinct().Take(6).ToArray();
            var complex = reference.Element.Children.Where(FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportStructureReport.IsPropertyElement)
                .Where(e => !FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlControlMetadata.Find(reference.Element.LocalName)!.IsChildProperty(e)
                    && !reference.PropertyElements.Values.Any(p => p.Element == e) && e.LocalName is not ("Grid.RowDefinitions" or "Grid.ColumnDefinitions"))
                .Select(e => e.LocalName).ToArray();
            if (unsupported.Length == 0 && complex.Length == 0) continue;
            _previewControlFailures[reference.ControlId] = "PartialSourceValue";
            partial.Add((reference.Element.GetAttributeValue("x:Name") ?? reference.Element.GetAttributeValue("Name") ?? reference.Element.LocalName)
                + ": " + string.Join(", ", unsupported.Concat(complex)));
        }
        var opaque = source.CapabilityReport.Elements.Where(e => e.Mode == FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlElementCapabilityMode.Opaque
            && FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportStructureReport.IsVisualCandidate(e.Element)).ToArray();
        if (_detailedPreviewDiagnostics)
            foreach (var entry in opaque)
            {
                // An opaque node deliberately has no model ControlId; identify its source span.
                var diagnostic = new PreviewControlDiagnostic("PREVIEW_CONTROL_SKIPPED", string.IsNullOrEmpty(_currentFormId) ? "standalone-preview" : _currentFormId,
                    "source-span:" + entry.Element.ElementSpan.Start, entry.Element.LocalName, "", _actualPreviewBuildMode,
                    false, false, false, 0, 0, 0, 0, "", entry.Reason) { RenderCapability = PreviewRenderCapability.Opaque };
                _previewControlDiagnostics.Add(diagnostic);
                Trace.WriteLine(diagnostic.Event + " " + JsonSerializer.Serialize(diagnostic));
                BuildDiagnosticEmitted?.Invoke(this, diagnostic);
            }
        if (partial.Count > 0 || opaque.Length > 0)
        {
            var message = $"Imported Preview: частично показано {partial.Count}, opaque visual nodes {opaque.Length}. "
                + string.Join("; ", partial.Take(4).Concat(opaque.Take(4).Select(e => e.Element.Name + " " + e.Name + ": " + e.Reason)))
                + ". Bindings/сложные объекты не исполняются; исходный AXAML сохраняется без изменений.";
            ReportPreviewFailure(null, "PartialSourceValue", message, code: "PREVIEW_SOURCE_PROPERTY_PARTIAL");
        }
        CompletePreviewBuild();
        return true;
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
            if (_importedNativeSource is null && _actualPreviewBuildMode == "AXAML" && root is Control content)
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
                if (reason.Length == 0 && _importedNativeSource is not null && (_importedSourceHasResources || _importedRootGaps.Length > 0)) reason = "SourceScopePreserved";
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
            view is not null && (!_previewControlFailures.TryGetValue(model?.Id ?? "", out var failure) || failure is "UnsupportedSourceProperty" or "PartialSourceValue"), view?.GetVisualRoot() == this, visible,
            point.X, point.Y, view?.Bounds.Width ?? 0, view?.Bounds.Height ?? 0, view?.GetType().FullName ?? "", reason)
        { RenderCapability = reason is "MissingDescriptor" or "InvalidParent" or "LayoutProjectionFailed" ? PreviewRenderCapability.Unsupported
            : view is null ? PreviewRenderCapability.Opaque
            : reason is "Placeholder" ? PreviewRenderCapability.Placeholder
            : reason.Length == 0 || reason is "AuthoredHidden" ? PreviewRenderCapability.FullyRendered : PreviewRenderCapability.PartiallyRendered };
        _previewControlDiagnostics.Add(record);
        Trace.WriteLine(code + " " + JsonSerializer.Serialize(record));
        BuildDiagnosticEmitted?.Invoke(this, record);
    }

    private void ApplyPreviewRootTheme(string axaml)
    {
        var value = XDocument.Parse(axaml).Root?.Attribute("RequestedThemeVariant")?.Value.Trim();
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
