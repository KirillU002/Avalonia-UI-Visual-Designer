using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public enum AxamlCapabilityLevel
{
    FullyEditable,
    PartiallyEditable,
    ReadOnly,
    UnsafeToSave
}

public sealed record AxamlCapabilityEntry(string Subject, AxamlCapabilityLevel Level, string Message);

public enum AxamlElementCapabilityMode { Editable, PartiallyEditable, Opaque }
public enum AxamlPropertyCapabilityMode { Editable, Preserved, Unsupported }

public sealed record AxamlPropertyCapability(string Key, string SourceName, AxamlPropertyCapabilityMode Mode, string Reason = "");

public sealed record AxamlStructuralCapability(bool CanAddChild, bool CanRemove, bool CanReorder,
    bool CanReparent, bool CanEditContent, bool CanEditCollection, bool CanInsertSibling, bool CanModifyLayout, string Reason);

internal sealed record AxamlArchivedSource(string Text, AxamlSourceReference Reference,
    IReadOnlyDictionary<string, AxamlSourceReference> References);

public sealed class AxamlElementCapability
{
    public AxamlElementCapability(AxamlElementSyntax element, AxamlElementCapabilityMode mode, string reason = "")
    {
        Element = element;
        Mode = mode;
        Reason = reason;
    }

    public AxamlElementSyntax Element { get; }
    public string Name => Element.GetAttributeValue("x:Name") ?? Element.GetAttributeValue("Name") ?? "";
    public AxamlElementCapabilityMode Mode { get; internal set; }
    public string Reason { get; }
    public List<AxamlPropertyCapability> Properties { get; } = new();
    public bool CanEditProperty(string key) => Mode != AxamlElementCapabilityMode.Opaque
        && Properties.Any(p => p.Key == key && p.Mode == AxamlPropertyCapabilityMode.Editable);
}

public sealed class AxamlCapabilityReport
{
    public AxamlImportStructureReport? Structure { get; internal set; }
    public List<AxamlCapabilityEntry> Entries { get; } = new();
    public List<AxamlElementCapability> Elements { get; } = new();
    public List<AxamlPropertyElementInfo> PropertyElements { get; } = new();
    public AxamlCapabilityLevel Level { get; private set; } = AxamlCapabilityLevel.FullyEditable;
    public bool CanSafelyPatch => Level is AxamlCapabilityLevel.FullyEditable or AxamlCapabilityLevel.PartiallyEditable;
    public bool CanOpen => Level != AxamlCapabilityLevel.UnsafeToSave;
    public bool DocumentReadOnly => !CanSafelyPatch;
    public string ReadOnlyReason => string.Join("; ", Entries.Where(e => e.Level >= AxamlCapabilityLevel.ReadOnly).Select(e => e.Message));
    public string StatusMessage => Level switch
    {
        AxamlCapabilityLevel.FullyEditable => "AXAML открыт для редактирования.",
        AxamlCapabilityLevel.PartiallyEditable when Structure is { EmptyProjectionMessage.Length: > 0 } => Structure.EmptyProjectionMessage,
        AxamlCapabilityLevel.PartiallyEditable when !Elements.Any(e => e.Properties.Any(p => p.Mode == AxamlPropertyCapabilityMode.Editable)) =>
            "Частичная поддержка AXAML: в текущей проекции нет редактируемых элементов. Исходная разметка сохранена без изменений.",
        AxamlCapabilityLevel.PartiallyEditable => $"Частичное редактирование: импортировано {Structure?.ImportedElements} из {Structure?.VisualElements} визуальных элементов. Остальная разметка сохраняется без изменений.",
        _ => "Документ открыт только для чтения: " + ReadOnlyReason
    };

    public void AddElement(AxamlElementCapability element)
    {
        Elements.Add(element);
        Add(element.Name.Length == 0 ? element.Element.Name : element.Name,
            element.Mode == AxamlElementCapabilityMode.Editable ? AxamlCapabilityLevel.FullyEditable : AxamlCapabilityLevel.PartiallyEditable,
            element.Reason.Length == 0 ? element.Mode.ToString() : element.Reason);
    }

    public void Add(string subject, AxamlCapabilityLevel level, string message)
    {
        Entries.Add(new AxamlCapabilityEntry(subject, level, message));
        if (level > Level)
            Level = level;
    }
}

public enum AxamlDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record AxamlRoundTripDiagnostic(string Code, AxamlDiagnosticSeverity Severity, string Details = "");

public sealed class AxamlSourceReference
{
    public AxamlSourceReference(string controlId, string controlType, AxamlElementSyntax element, AxamlElementCapability capability)
    {
        ControlId = controlId;
        ControlType = controlType;
        Element = element;
        Capability = capability;
    }

    public string ControlId { get; }
    public string ControlType { get; }
    public string? ParentId { get; internal set; }
    public AxamlElementSyntax Element { get; }
    public Dictionary<string, string> SnapshotValues { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FormDesigner.DesignerSystem.Metadata.PropertyExpressionValue> ExpressionValues { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AxamlPropertyElementInfo> PropertyElements { get; } = new(StringComparer.Ordinal);
    public AxamlElementCapability Capability { get; }
    public IEnumerable<string> EditableProperties => Capability.Properties
        .Where(p => p.Mode == AxamlPropertyCapabilityMode.Editable).Select(p => p.Key);
}

public sealed class AxamlSourceMap
{
    private readonly Dictionary<string, AxamlSourceReference> _byControlId = new(StringComparer.Ordinal);
    private readonly List<AxamlSourceReference> _controls = new();
    private readonly IReadOnlyList<AxamlSourceReference> _controlsView;

    public AxamlSourceMap(AxamlElementSyntax? canvasElement)
    {
        CanvasElement = canvasElement;
        _controlsView = _controls.AsReadOnly();
    }

    public AxamlElementSyntax? CanvasElement { get; }
    public bool CanInsertControls => CanvasElement is { IsSelfClosing: false } canvas && !canvas.EndTagSpan.IsEmpty;
    public IReadOnlyDictionary<string, AxamlSourceReference> ByControlId => _byControlId;
    public IReadOnlyList<AxamlSourceReference> Controls => _controlsView;

    public void Add(AxamlSourceReference reference)
    {
        if (_byControlId.TryGetValue(reference.ControlId, out var previous)) _controls[_controls.IndexOf(previous)] = reference;
        else _controls.Add(reference);
        _byControlId[reference.ControlId] = reference;
    }
    public bool TryGet(string controlId, out AxamlSourceReference reference) => _byControlId.TryGetValue(controlId, out reference!);
}

/// <summary>
/// In-memory metadata for an imported AXAML document. It deliberately never becomes
/// part of DesignerDocumentFileModel or an exported project.
/// </summary>
public sealed class AxamlRoundTripDocument
{
    public AxamlRoundTripDocument(string sourcePath, AxamlSyntaxDocument syntax, AxamlSourceMap sourceMap, AxamlCapabilityReport capabilityReport)
    {
        SourcePath = sourcePath ?? string.Empty;
        _styleCatalog = null;
        Syntax = syntax;
        SourceMap = sourceMap;
        CapabilityReport = capabilityReport;
        OriginalText = syntax.Text;
        SourceChecksum = ComputeChecksum(OriginalText);
    }

    public string SourcePath { get; private set; }
    private AxamlStyleValues? _styleCatalog;
    public AxamlStyleValues StyleCatalog => _styleCatalog ??= AxamlStyleValues.Read(Syntax);
    public AxamlSyntaxDocument Syntax { get; private set; }
    public AxamlSourceMap SourceMap { get; private set; }
    public AxamlCapabilityReport CapabilityReport { get; private set; }
    public string OriginalText { get; private set; }
    public string SourceChecksum { get; private set; }
    public Encoding TextEncoding { get; private set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    // Document-owned undo evidence. No project JSON, global Type or live control cache.
    internal Dictionary<string, AxamlArchivedSource> RemovedSources { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> PendingTypes { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string,(IReadOnlyList<double> Rows,IReadOnlyList<double> Columns)> MeasuredGridTracks { get; } = new(StringComparer.Ordinal);

    public void SetTextEncoding(Encoding encoding)
    {
        TextEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    internal void ReplaceSource(string sourcePath, AxamlSyntaxDocument syntax, AxamlSourceMap sourceMap, AxamlCapabilityReport capabilityReport)
    {
        SourcePath = sourcePath ?? string.Empty;
        _styleCatalog = null;
        Syntax = syntax;
        SourceMap = sourceMap;
        CapabilityReport = capabilityReport;
        OriginalText = syntax.Text;
        SourceChecksum = ComputeChecksum(OriginalText);
    }

    public bool HasExternalChanges(string currentText) => !string.Equals(SourceChecksum, ComputeChecksum(currentText), StringComparison.Ordinal);

    public static string ComputeChecksum(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public sealed class AxamlImportResult
{
    public AxamlImportResult(DesignerDocumentFileModel document, AxamlRoundTripDocument roundTripDocument, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics)
    {
        Document = document;
        RoundTripDocument = roundTripDocument;
        Diagnostics = diagnostics;
    }

    public DesignerDocumentFileModel Document { get; }
    public AxamlRoundTripDocument RoundTripDocument { get; }
    public AxamlCapabilityReport CapabilityReport => RoundTripDocument.CapabilityReport;
    public IReadOnlyList<AxamlRoundTripDiagnostic> Diagnostics { get; }
}

public sealed record AxamlTextEdit(int Start, int Length, string NewText)
{
    public int End => Start + Length;
}

public sealed class AxamlPatchResult
{
    private AxamlPatchResult(bool canApply, bool externalChangeDetected, string patchedText, IReadOnlyList<AxamlTextEdit> edits, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics)
    {
        CanApply = canApply;
        ExternalChangeDetected = externalChangeDetected;
        PatchedText = patchedText;
        Edits = edits;
        Diagnostics = diagnostics;
    }

    public bool CanApply { get; }
    public bool ExternalChangeDetected { get; }
    public string PatchedText { get; }
    public IReadOnlyList<AxamlTextEdit> Edits { get; }
    public IReadOnlyList<AxamlRoundTripDiagnostic> Diagnostics { get; }
    public bool HasChanges => Edits.Count > 0;
    public IReadOnlyDictionary<string, int> ControlElementStarts { get; internal set; } = new Dictionary<string, int>();
    internal AxamlSyntaxDocument? ParsedSyntax { get; set; }

    public static AxamlPatchResult Success(string patchedText, IReadOnlyList<AxamlTextEdit> edits, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics) =>
        new(true, false, patchedText, edits, diagnostics);

    public static AxamlPatchResult ExternalChange(string originalText, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics) =>
        new(false, true, originalText, Array.Empty<AxamlTextEdit>(), diagnostics);

    public static AxamlPatchResult Unsafe(string originalText, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics) =>
        new(false, false, originalText, Array.Empty<AxamlTextEdit>(), diagnostics);
}
