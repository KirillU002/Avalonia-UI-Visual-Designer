using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>
/// Builds a DesignerDocument projection for the conservative Phase 1 AXAML subset.
/// Syntax unknown to this service remains in the source document and is never
/// reconstructed by this service.
/// </summary>
public sealed class AxamlImportService
{
    private static readonly HashSet<string> SupportedControlTypes = new(StringComparer.Ordinal)
    {
        DesignerControlTypes.Button,
        DesignerControlTypes.TextBox,
        DesignerControlTypes.TextBlock,
        DesignerControlTypes.Border,
        DesignerControlTypes.CheckBox
    };

    public AxamlImportResult Import(
        string sourceText,
        string? sourcePath = null,
        IReadOnlyDictionary<string, string>? knownControlIdsByName = null)
    {
        var diagnostics = new List<AxamlRoundTripDiagnostic>
        {
            new("AXAML_IMPORT_START", AxamlDiagnosticSeverity.Information, $"path={sourcePath ?? string.Empty}; length={sourceText?.Length ?? 0}")
        };
        var report = new AxamlCapabilityReport();
        AxamlSyntaxDocument syntax;

        try
        {
            syntax = AxamlSyntaxDocument.Parse(sourceText ?? string.Empty);
        }
        catch (Exception ex) when (ex is AxamlSyntaxException or ArgumentException)
        {
            report.Add("Document", AxamlCapabilityLevel.UnsafeToSave, ex.Message);
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_IMPORT_FAILED", AxamlDiagnosticSeverity.Error, ex.ToString()));
            var placeholderSyntax = AxamlSyntaxDocument.Parse("<UserControl />");
            var placeholderMap = new AxamlSourceMap(placeholderSyntax.Root);
            return new AxamlImportResult(
                new DesignerDocumentFileModel(),
                new AxamlRoundTripDocument(sourcePath ?? string.Empty, placeholderSyntax, placeholderMap, report),
                diagnostics);
        }

        var rootType = syntax.Root.LocalName;
        diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_IMPORT_ROOT_RESOLVED", AxamlDiagnosticSeverity.Information, $"type={rootType}"));
        if (!string.Equals(rootType, "Window", StringComparison.Ordinal) && !string.Equals(rootType, "UserControl", StringComparison.Ordinal))
        {
            report.Add(rootType, AxamlCapabilityLevel.ReadOnly, "Phase 1 supports Window and UserControl roots only.");
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_CAPABILITY_REPORT", AxamlDiagnosticSeverity.Warning, "readonly root"));
            var map = new AxamlSourceMap(syntax.Root);
            return new AxamlImportResult(CreateDocument(rootType, syntax.Root), new AxamlRoundTripDocument(sourcePath ?? string.Empty, syntax, map, report), diagnostics);
        }

        var canvas = syntax.Root.Children.FirstOrDefault(element => string.Equals(element.LocalName, "Canvas", StringComparison.Ordinal));
        if (canvas is null || canvas.IsSelfClosing)
        {
            report.Add(rootType, AxamlCapabilityLevel.ReadOnly, "Phase 1 requires a non-empty direct Canvas child.");
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_CAPABILITY_REPORT", AxamlDiagnosticSeverity.Warning, "readonly; Canvas missing"));
            var map = new AxamlSourceMap(syntax.Root);
            return new AxamlImportResult(CreateDocument(rootType, syntax.Root), new AxamlRoundTripDocument(sourcePath ?? string.Empty, syntax, map, report), diagnostics);
        }

        report.Add(rootType, AxamlCapabilityLevel.FullyEditable, "Root is supported.");
        report.Add("Canvas", AxamlCapabilityLevel.FullyEditable, "Direct Canvas is supported.");
        var document = CreateDocument(rootType, syntax.Root);
        var sourceMap = new AxamlSourceMap(canvas);
        var importedControls = new List<(DesignerControlFileModel Control, int ZIndex, int SourceOrder)>();
        var sourceOrder = 0;

        foreach (var element in canvas.Children)
        {
            var controlType = element.LocalName;
            if (!SupportedControlTypes.Contains(controlType))
            {
                report.Add(controlType, AxamlCapabilityLevel.PartiallyEditable, "Unsupported element is preserved without modification.");
                diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_IMPORT_UNKNOWN_NODE_PRESERVED", AxamlDiagnosticSeverity.Warning, $"element={element.Name}"));
                continue;
            }

            if (element.Children.Count > 0 || HasUnsupportedInnerContent(syntax.Text, element))
            {
                report.Add(controlType, AxamlCapabilityLevel.PartiallyEditable, "Nested child or raw content syntax is preserved and is not editable in Phase 1.");
                diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_IMPORT_UNKNOWN_NODE_PRESERVED", AxamlDiagnosticSeverity.Warning, $"element={element.Name}; reason=nested children or raw content"));
                continue;
            }

            var control = CreateControl(controlType, element, knownControlIdsByName, diagnostics, report);
            var zIndex = int.TryParse(element.GetAttributeValue("Canvas.ZIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedZIndex)
                ? parsedZIndex
                : sourceOrder;
            importedControls.Add((ToFileModel(control), zIndex, sourceOrder));
            sourceOrder++;
            var reference = new AxamlSourceReference(control.Id, control.Type, element);
            foreach (var property in AxamlRoundTripPropertyMap.PropertiesFor(control.Type))
            {
                reference.SnapshotValues[property.Key] = property.Read(control);
                var attribute = property.ResolveExistingAttribute(element);
                if (attribute is null || !IsMarkupExtension(attribute.Value))
                    reference.EditableProperties.Add(property.Key);
                else
                {
                    report.Add($"{control.Name}.{property.Key}", AxamlCapabilityLevel.PartiallyEditable, "Markup extension is preserved read-only in Phase 1.");
                }
            }

            sourceMap.Add(reference);
            report.Add(control.Name, AxamlCapabilityLevel.FullyEditable, "Control is supported.");
            diagnostics.Add(new AxamlRoundTripDiagnostic(
                "AXAML_IMPORT_CONTROL",
                AxamlDiagnosticSeverity.Information,
                $"type={controlType}; name={control.Name}; supported=true"));
        }

        foreach (var imported in importedControls.OrderBy(item => item.ZIndex).ThenBy(item => item.SourceOrder))
            document.Controls.Add(imported.Control);

        diagnostics.Add(new AxamlRoundTripDiagnostic(
            "AXAML_CAPABILITY_REPORT",
            report.Level == AxamlCapabilityLevel.FullyEditable ? AxamlDiagnosticSeverity.Information : AxamlDiagnosticSeverity.Warning,
            $"level={report.Level}; controls={document.Controls.Count}; entries={report.Entries.Count}"));
        return new AxamlImportResult(document, new AxamlRoundTripDocument(sourcePath ?? string.Empty, syntax, sourceMap, report), diagnostics);
    }

    private static DesignerDocumentFileModel CreateDocument(string rootType, AxamlElementSyntax root)
    {
        var document = new DesignerDocumentFileModel
        {
            FormTitle = root.GetAttributeValue("Title") ?? root.GetAttributeValue("x:Name") ?? rootType
        };

        if (TryParseDouble(root.GetAttributeValue("Width"), out var width))
            document.DesignWidth = Math.Max(300, width);
        if (TryParseDouble(root.GetAttributeValue("Height"), out var height))
            document.DesignHeight = Math.Max(200, height);

        return document;
    }

    private static DesignControlModel CreateControl(
        string controlType,
        AxamlElementSyntax element,
        IReadOnlyDictionary<string, string>? knownControlIdsByName,
        ICollection<AxamlRoundTripDiagnostic> diagnostics,
        AxamlCapabilityReport report)
    {
        var control = new DesignControlModel { Type = controlType };
        foreach (var property in AxamlRoundTripPropertyMap.PropertiesFor(controlType))
        {
            var attribute = property.ResolveExistingAttribute(element);
            if (attribute is null)
                continue;

            property.TryWrite(control, attribute.Value);
        }

        if (string.IsNullOrWhiteSpace(control.Name))
            control.Name = controlType;
        if (knownControlIdsByName is not null && knownControlIdsByName.TryGetValue(control.Name, out var existingId))
            control.Id = existingId;

        var knownAttributeNames = AxamlRoundTripPropertyMap.PropertiesFor(controlType)
            .SelectMany(property => property.AttributeNames)
            .Append("xmlns")
            .Append("Canvas.ZIndex")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes.Where(attribute => !knownAttributeNames.Contains(attribute.Name)))
        {
            report.Add($"{control.Name}.{attribute.Name}", AxamlCapabilityLevel.PartiallyEditable, "Unknown attribute will be preserved.");
            diagnostics.Add(new AxamlRoundTripDiagnostic(
                "AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED",
                AxamlDiagnosticSeverity.Information,
                $"element={element.Name}; attribute={attribute.Name}"));
        }

        return control;
    }

    private static DesignerControlFileModel ToFileModel(DesignControlModel model) => new()
    {
        Id = model.Id,
        Type = model.Type,
        Name = model.Name,
        Text = model.Text,
        PlaceholderText = model.PlaceholderText,
        Background = model.Background,
        Foreground = model.Foreground,
        BorderBrush = model.BorderBrush,
        BorderThickness = model.BorderThickness,
        CornerRadius = model.CornerRadius,
        FontFamily = model.FontFamily,
        FontSize = model.FontSize,
        FontWeight = model.FontWeight,
        Opacity = model.Opacity,
        Padding = model.Padding,
        Margin = model.Margin,
        HorizontalAlignment = model.HorizontalAlignment,
        VerticalAlignment = model.VerticalAlignment,
        IsVisible = model.IsVisible,
        X = model.X,
        Y = model.Y,
        Width = model.Width,
        Height = model.Height
    };

    private static bool IsMarkupExtension(string value) => value.TrimStart().StartsWith("{", StringComparison.Ordinal);
    private static bool TryParseDouble(string? value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool HasUnsupportedInnerContent(string source, AxamlElementSyntax element)
    {
        if (element.IsSelfClosing || element.EndTagSpan.IsEmpty)
            return false;

        var contentLength = element.EndTagSpan.Start - element.ContentStart;
        if (contentLength <= 0)
            return false;

        var content = source.Substring(element.ContentStart, contentLength);
        content = Regex.Replace(content, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        return !string.IsNullOrWhiteSpace(content);
    }
}

internal sealed class AxamlRoundTripProperty
{
    private readonly Func<DesignControlModel, string> _read;
    private readonly Action<DesignControlModel, string> _write;

    public AxamlRoundTripProperty(string key, IEnumerable<string> attributeNames, Func<DesignControlModel, string> read, Action<DesignControlModel, string> write)
    {
        Key = key;
        AttributeNames = attributeNames.ToArray();
        _read = read;
        _write = write;
    }

    public string Key { get; }
    public IReadOnlyList<string> AttributeNames { get; }
    public string PreferredAttributeName => AttributeNames[0];
    public string Read(DesignControlModel control) => _read(control);

    public void TryWrite(DesignControlModel control, string value)
    {
        if (!value.TrimStart().StartsWith("{", StringComparison.Ordinal))
            _write(control, WebUtility.HtmlDecode(value));
    }

    public AxamlAttributeSyntax? ResolveExistingAttribute(AxamlElementSyntax element) =>
        AttributeNames.Select(element.FindAttribute).FirstOrDefault(attribute => attribute is not null);
}

internal static class AxamlRoundTripPropertyMap
{
    private static readonly IReadOnlyList<AxamlRoundTripProperty> Common = new[]
    {
        Text("Name", new[] { "x:Name", "Name" }, control => control.Name, (control, value) => control.Name = value),
        Number("Width", control => control.Width, (control, value) => control.Width = value),
        Number("Height", control => control.Height, (control, value) => control.Height = value),
        Number("Opacity", control => control.Opacity, (control, value) => control.Opacity = value),
        Bool("IsVisible", control => control.IsVisible, (control, value) => control.IsVisible = value),
        Text("Margin", "Margin", control => control.Margin, (control, value) => control.Margin = value),
        Text("HorizontalAlignment", "HorizontalAlignment", control => control.HorizontalAlignment, (control, value) => control.HorizontalAlignment = value),
        Text("VerticalAlignment", "VerticalAlignment", control => control.VerticalAlignment, (control, value) => control.VerticalAlignment = value),
        Number("Canvas.Left", control => control.X, (control, value) => control.X = value),
        Number("Canvas.Top", control => control.Y, (control, value) => control.Y = value),
        Text("Background", "Background", control => control.Background, (control, value) => control.Background = value),
        Text("Foreground", "Foreground", control => control.Foreground, (control, value) => control.Foreground = value),
        Text("BorderBrush", "BorderBrush", control => control.BorderBrush, (control, value) => control.BorderBrush = value),
        Number("BorderThickness", control => control.BorderThickness, (control, value) => control.BorderThickness = value),
        Number("CornerRadius", control => control.CornerRadius, (control, value) => control.CornerRadius = value),
        Number("Padding", control => control.Padding, (control, value) => control.Padding = value),
        Number("FontSize", control => control.FontSize, (control, value) => control.FontSize = value),
        Text("FontWeight", "FontWeight", control => control.FontWeight, (control, value) => control.FontWeight = value)
    };

    private static readonly IReadOnlyList<AxamlRoundTripProperty> Button = Common.Concat(new[]
    {
        Text("Text", "Content", control => control.Text, (control, value) => control.Text = value)
    }).ToArray();

    private static readonly IReadOnlyList<AxamlRoundTripProperty> TextBox = Common.Concat(new[]
    {
        Text("Text", "Text", control => control.Text, (control, value) => control.Text = value),
        Text("PlaceholderText", "Watermark", control => control.PlaceholderText, (control, value) => control.PlaceholderText = value)
    }).ToArray();

    private static readonly IReadOnlyList<AxamlRoundTripProperty> TextBlock = Common.Concat(new[]
    {
        Text("Text", "Text", control => control.Text, (control, value) => control.Text = value)
    }).ToArray();

    private static readonly IReadOnlyList<AxamlRoundTripProperty> CheckBox = Common.Concat(new[]
    {
        Text("Text", "Content", control => control.Text, (control, value) => control.Text = value)
    }).ToArray();

    public static IReadOnlyList<AxamlRoundTripProperty> PropertiesFor(string controlType) => controlType switch
    {
        DesignerControlTypes.Button => Button,
        DesignerControlTypes.TextBox => TextBox,
        DesignerControlTypes.TextBlock => TextBlock,
        DesignerControlTypes.CheckBox => CheckBox,
        DesignerControlTypes.Border => Common,
        _ => Array.Empty<AxamlRoundTripProperty>()
    };

    private static AxamlRoundTripProperty Text(string key, string attributeName, Func<DesignControlModel, string> read, Action<DesignControlModel, string> write) =>
        Text(key, new[] { attributeName }, read, write);

    private static AxamlRoundTripProperty Text(string key, IEnumerable<string> attributeNames, Func<DesignControlModel, string> read, Action<DesignControlModel, string> write) =>
        new(key, attributeNames, read, write);

    private static AxamlRoundTripProperty Number(string key, Func<DesignControlModel, double> read, Action<DesignControlModel, double> write) =>
        new(key, new[] { key }, control => read(control).ToString("0.###", CultureInfo.InvariantCulture), (control, value) =>
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                write(control, parsed);
        });

    private static AxamlRoundTripProperty Bool(string key, Func<DesignControlModel, bool> read, Action<DesignControlModel, bool> write) =>
        new(key, new[] { key }, control => read(control) ? "True" : "False", (control, value) =>
        {
            if (bool.TryParse(value, out var parsed))
                write(control, parsed);
        });
}
