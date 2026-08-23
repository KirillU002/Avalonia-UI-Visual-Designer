using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>
/// Produces minimal source edits for the subset imported by AxamlImportService.
/// It never serializes a complete document and treats unsupported syntax as opaque text.
/// </summary>
public sealed class AxamlPatchWriter
{
    public AxamlPatchResult CreatePatch(
        AxamlRoundTripDocument roundTripDocument,
        DesignerDocumentFileModel document,
        string? currentSourceText = null)
    {
        ArgumentNullException.ThrowIfNull(roundTripDocument);
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<AxamlRoundTripDiagnostic>();
        var source = roundTripDocument.OriginalText;
        if (currentSourceText is not null && roundTripDocument.HasExternalChanges(currentSourceText))
        {
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_EXTERNAL_CHANGE_DETECTED", AxamlDiagnosticSeverity.Warning, "checksum does not match imported source"));
            return AxamlPatchResult.ExternalChange(source, diagnostics);
        }

        if (!roundTripDocument.CapabilityReport.CanSafelyPatch)
        {
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_PATCH_BLOCKED", AxamlDiagnosticSeverity.Warning, $"capability={roundTripDocument.CapabilityReport.Level}"));
            return AxamlPatchResult.Unsafe(source, diagnostics);
        }

        var currentById = document.Controls
            .Where(control => !string.IsNullOrWhiteSpace(control.Id))
            .ToDictionary(control => control.Id, StringComparer.Ordinal);
        var edits = new List<AxamlTextEdit>();
        var referencedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in roundTripDocument.SourceMap.Controls)
        {
            referencedIds.Add(reference.ControlId);
            if (!currentById.TryGetValue(reference.ControlId, out var fileControl))
            {
                edits.Add(new AxamlTextEdit(reference.Element.ElementSpan.Start, reference.Element.ElementSpan.Length, string.Empty));
                continue;
            }

            var control = ToRuntimeModel(fileControl);
            AppendPropertyEdits(source, reference, control, edits);
        }

        var addedControls = document.Controls.Where(control => !referencedIds.Contains(control.Id)).ToList();
        if (addedControls.Count > 0)
            AppendNewControlInsert(source, roundTripDocument, addedControls, edits);

        if (HasOverlaps(edits))
        {
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_PATCH_BLOCKED", AxamlDiagnosticSeverity.Error, "generated edits overlap"));
            return AxamlPatchResult.Unsafe(source, diagnostics);
        }

        var patched = ApplyEdits(source, edits);
        diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_PATCH_CREATED", AxamlDiagnosticSeverity.Information, $"edits count={edits.Count}"));
        return AxamlPatchResult.Success(patched, edits.OrderBy(edit => edit.Start).ToList(), diagnostics);
    }

    public static string ApplyEdits(string source, IEnumerable<AxamlTextEdit> edits)
    {
        var result = source;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
        {
            if (edit.Start < 0 || edit.Length < 0 || edit.End > result.Length)
                throw new ArgumentOutOfRangeException(nameof(edits), "AXAML patch is outside source bounds.");
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
        }

        return result;
    }

    private static void AppendPropertyEdits(string source, AxamlSourceReference reference, DesignControlModel current, ICollection<AxamlTextEdit> edits)
    {
        var additions = new List<(string Name, string Value)>();
        foreach (var property in AxamlRoundTripPropertyMap.PropertiesFor(reference.ControlType))
        {
            if (!reference.EditableProperties.Contains(property.Key))
                continue;

            var currentValue = property.Read(current);
            if (!reference.SnapshotValues.TryGetValue(property.Key, out var originalValue) || string.Equals(currentValue, originalValue, StringComparison.Ordinal))
                continue;

            var attribute = property.ResolveExistingAttribute(reference.Element);
            if (attribute is not null)
                edits.Add(new AxamlTextEdit(attribute.ValueSpan.Start, attribute.ValueSpan.Length, EscapeAttributeValue(currentValue)));
            else
                additions.Add((property.PreferredAttributeName, currentValue));
        }

        if (additions.Count == 0)
            return;

        var insertion = reference.Element.IsSelfClosing
            ? reference.Element.OpeningTagSpan.End - 2
            : reference.Element.OpeningTagSpan.End - 1;
        var addedText = string.Concat(additions.Select(item => $" {item.Name}=\"{EscapeAttributeValue(item.Value)}\""));
        edits.Add(new AxamlTextEdit(insertion, 0, addedText));
    }

    private static void AppendNewControlInsert(
        string source,
        AxamlRoundTripDocument roundTripDocument,
        IReadOnlyList<DesignerControlFileModel> controls,
        ICollection<AxamlTextEdit> edits)
    {
        var canvas = roundTripDocument.SourceMap.CanvasElement;
        if (canvas.IsSelfClosing || canvas.EndTagSpan.IsEmpty)
            throw new InvalidOperationException("Cannot insert a control into a self-closing Canvas.");

        var canvasIndent = AxamlSyntaxDocument.GetLineIndent(source, canvas.OpeningTagSpan.Start);
        var childIndent = canvas.Children.Count > 0
            ? AxamlSyntaxDocument.GetLineIndent(source, canvas.Children[0].OpeningTagSpan.Start)
            : canvasIndent + roundTripDocument.Syntax.IndentUnit;
        var insertion = AxamlSyntaxDocument.GetLineStart(source, canvas.EndTagSpan.Start);
        var fragments = controls.Select(control => CreateFragment(
            control,
            roundTripDocument.Syntax.NewLine,
            childIndent,
            childIndent + roundTripDocument.Syntax.IndentUnit,
            canvas.Parent?.FindAttribute("xmlns:x") is not null));
        var text = string.Join(roundTripDocument.Syntax.NewLine, fragments) + roundTripDocument.Syntax.NewLine;
        edits.Add(new AxamlTextEdit(insertion, 0, text));
    }

    private static string CreateFragment(
        DesignerControlFileModel fileControl,
        string newLine,
        string elementIndent,
        string attributeIndent,
        bool supportsXName)
    {
        var control = ToRuntimeModel(fileControl);
        var attributes = new List<(string Name, string Value)>
        {
            (supportsXName ? "x:Name" : "Name", control.Name),
            ("Width", control.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
            ("Height", control.Height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
            ("Canvas.Left", control.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
            ("Canvas.Top", control.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
        };

        var contentAttribute = control.Type switch
        {
            DesignerControlTypes.Button or DesignerControlTypes.CheckBox => "Content",
            DesignerControlTypes.TextBox or DesignerControlTypes.TextBlock => "Text",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(contentAttribute) && !string.IsNullOrWhiteSpace(control.Text))
            attributes.Add((contentAttribute, control.Text));
        if (control.Type == DesignerControlTypes.TextBox && !string.IsNullOrWhiteSpace(control.PlaceholderText))
            attributes.Add(("Watermark", control.PlaceholderText));

        var body = string.Join(newLine, attributes.Select(attribute => attributeIndent + attribute.Name + "=\"" + EscapeAttributeValue(attribute.Value) + "\""));
        return elementIndent + "<" + control.Type + newLine + body + " />";
    }

    private static bool HasOverlaps(IEnumerable<AxamlTextEdit> edits)
    {
        var previousEnd = -1;
        foreach (var edit in edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.Length))
        {
            if (edit.Start < previousEnd)
                return true;
            previousEnd = Math.Max(previousEnd, edit.End);
        }

        return false;
    }

    private static string EscapeAttributeValue(string value) =>
        (value ?? string.Empty).Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private static DesignControlModel ToRuntimeModel(DesignerControlFileModel control) => new()
    {
        Id = control.Id,
        Type = control.Type,
        Name = control.Name,
        Text = control.Text,
        PlaceholderText = control.PlaceholderText,
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
        Margin = control.Margin,
        HorizontalAlignment = control.HorizontalAlignment,
        VerticalAlignment = control.VerticalAlignment,
        IsVisible = control.IsVisible,
        X = control.X,
        Y = control.Y,
        Width = control.Width,
        Height = control.Height
    };
}
