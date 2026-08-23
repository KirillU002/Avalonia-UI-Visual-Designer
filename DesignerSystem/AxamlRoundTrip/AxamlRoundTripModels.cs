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

public sealed class AxamlCapabilityReport
{
    public List<AxamlCapabilityEntry> Entries { get; } = new();
    public AxamlCapabilityLevel Level { get; private set; } = AxamlCapabilityLevel.FullyEditable;
    public bool CanSafelyPatch => Level is AxamlCapabilityLevel.FullyEditable or AxamlCapabilityLevel.PartiallyEditable;

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
    public AxamlSourceReference(string controlId, string controlType, AxamlElementSyntax element)
    {
        ControlId = controlId;
        ControlType = controlType;
        Element = element;
    }

    public string ControlId { get; }
    public string ControlType { get; }
    public AxamlElementSyntax Element { get; }
    public Dictionary<string, string> SnapshotValues { get; } = new(StringComparer.Ordinal);
    public HashSet<string> EditableProperties { get; } = new(StringComparer.Ordinal);
}

public sealed class AxamlSourceMap
{
    private readonly Dictionary<string, AxamlSourceReference> _byControlId = new(StringComparer.Ordinal);

    public AxamlSourceMap(AxamlElementSyntax canvasElement)
    {
        CanvasElement = canvasElement;
    }

    public AxamlElementSyntax CanvasElement { get; }
    public IReadOnlyDictionary<string, AxamlSourceReference> ByControlId => _byControlId;
    public IReadOnlyList<AxamlSourceReference> Controls => _byControlId.Values.ToList();

    public void Add(AxamlSourceReference reference) => _byControlId[reference.ControlId] = reference;
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
        Syntax = syntax;
        SourceMap = sourceMap;
        CapabilityReport = capabilityReport;
        OriginalText = syntax.Text;
        SourceChecksum = ComputeChecksum(OriginalText);
    }

    public string SourcePath { get; private set; }
    public AxamlSyntaxDocument Syntax { get; private set; }
    public AxamlSourceMap SourceMap { get; private set; }
    public AxamlCapabilityReport CapabilityReport { get; private set; }
    public string OriginalText { get; private set; }
    public string SourceChecksum { get; private set; }
    public Encoding TextEncoding { get; private set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public void SetTextEncoding(Encoding encoding)
    {
        TextEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    internal void ReplaceSource(string sourcePath, AxamlSyntaxDocument syntax, AxamlSourceMap sourceMap, AxamlCapabilityReport capabilityReport)
    {
        SourcePath = sourcePath ?? string.Empty;
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

    public static AxamlPatchResult Success(string patchedText, IReadOnlyList<AxamlTextEdit> edits, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics) =>
        new(true, false, patchedText, edits, diagnostics);

    public static AxamlPatchResult ExternalChange(string originalText, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics) =>
        new(false, true, originalText, Array.Empty<AxamlTextEdit>(), diagnostics);

    public static AxamlPatchResult Unsafe(string originalText, IReadOnlyList<AxamlRoundTripDiagnostic> diagnostics) =>
        new(false, false, originalText, Array.Empty<AxamlTextEdit>(), diagnostics);
}
