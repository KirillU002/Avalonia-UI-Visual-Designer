using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaDesigner.VSIX;

internal sealed class VsDocumentSnapshot
{
    public string DocumentId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public long Version { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

/// <summary>
/// Uses the active Visual Studio TextDocument. Patches modify only the in-memory buffer;
/// no File.WriteAllText call is permitted in the VSIX project.
/// </summary>
internal sealed class VsDocumentBuffer
{
    private readonly DTE _dte;
    private long _version;

    public VsDocumentBuffer(DTE dte)
    {
        _dte = dte ?? throw new ArgumentNullException(nameof(dte));
    }

    public bool TryCaptureActiveAxaml(out VsDocumentSnapshot snapshot, out string error)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryCaptureAxaml(_dte.ActiveDocument?.FullName, out snapshot, out error);
    }

    public bool TryCaptureAxaml(string? path, out VsDocumentSnapshot snapshot, out string error)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        snapshot = new VsDocumentSnapshot();
        error = string.Empty;
        var document = path is null ? null : FindOpenDocument(path);
        if (document is null || !document.FullName.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
        {
            error = "Откройте или выберите конкретный .axaml документ в Visual Studio.";
            return false;
        }

        if (!TryGetTextDocument(document, out var textDocument))
        {
            error = "Visual Studio не предоставила текстовый буфер для активного AXAML документа.";
            return false;
        }

        var text = ReadText(textDocument);
        snapshot = new VsDocumentSnapshot
        {
            DocumentId = document.FullName,
            FilePath = document.FullName,
            Text = text,
            Version = ++_version,
            Checksum = AvaloniaDesigner.Host.Protocol.DesignerHostProtocol.ComputeChecksum(text)
        };
        return true;
    }

    public bool TryApplyPatch(VsDocumentSnapshot openedSnapshot, IReadOnlyList<AvaloniaDesigner.Host.Protocol.TextEditPayload> edits, out VsDocumentSnapshot appliedSnapshot, out string error)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        appliedSnapshot = new VsDocumentSnapshot();
        error = string.Empty;
        var document = FindOpenDocument(openedSnapshot.FilePath);
        if (document is null || !TryGetTextDocument(document, out var textDocument))
        {
            error = "AXAML документ больше не открыт в Visual Studio.";
            return false;
        }

        var currentText = ReadText(textDocument);
        var currentChecksum = AvaloniaDesigner.Host.Protocol.DesignerHostProtocol.ComputeChecksum(currentText);
        if (!string.Equals(currentChecksum, openedSnapshot.Checksum, StringComparison.Ordinal))
        {
            error = "AXAML был изменён в Visual Studio после открытия Designer.";
            return false;
        }

        var ordered = VsTextPatch.Validate(currentText, edits);
        var ownsUndo = !_dte.UndoContext.IsOpen;
        if (ownsUndo) _dte.UndoContext.Open("Avalonia Designer patch");
        try
        {
            foreach (var edit in ordered)
            {
                var from = VsTextPatch.Position(currentText, edit.Start);
                var to = VsTextPatch.Position(currentText, edit.Start + edit.Length);
                var start = textDocument.StartPoint.CreateEditPoint();
                start.MoveToLineAndOffset(from.Line, from.Column);
                var end = textDocument.StartPoint.CreateEditPoint();
                end.MoveToLineAndOffset(to.Line, to.Column);
                start.ReplaceText(end, edit.NewText ?? string.Empty, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
            }
        }
        catch
        {
            if (ownsUndo) _dte.UndoContext.SetAborted();
            throw;
        }
        finally
        {
            if (ownsUndo && _dte.UndoContext.IsOpen) _dte.UndoContext.Close();
        }

        var patchedText = ReadText(textDocument);
        appliedSnapshot = new VsDocumentSnapshot
        {
            DocumentId = openedSnapshot.DocumentId,
            FilePath = openedSnapshot.FilePath,
            Text = patchedText,
            Version = ++_version,
            Checksum = AvaloniaDesigner.Host.Protocol.DesignerHostProtocol.ComputeChecksum(patchedText)
        };
        return true;
    }

    private Document? FindOpenDocument(string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (Document candidate in _dte.Documents)
        {
            if (string.Equals(candidate.FullName, filePath, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static bool TryGetTextDocument(Document document, out TextDocument textDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        textDocument = null!;
        try
        {
            var candidate = document.Object("TextDocument") as TextDocument;
            if (candidate is null)
                return false;

            textDocument = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ReadText(TextDocument document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var editPoint = document.StartPoint.CreateEditPoint();
        return editPoint.GetText(document.EndPoint);
    }
}
