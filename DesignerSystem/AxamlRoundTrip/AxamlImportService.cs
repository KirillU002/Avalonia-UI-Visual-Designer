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
            DesignerDocumentFormat.Validate(sourceText ?? string.Empty, DesignerDocumentKind.AxamlRoundTrip, nameof(AxamlImportService));
            syntax = AxamlSyntaxDocument.Parse(sourceText ?? string.Empty);
        }
        catch (Exception ex) when (ex is AxamlSyntaxException or ArgumentException)
        {
            report.Add("Document", AxamlCapabilityLevel.UnsafeToSave, ex.Message);
            diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_IMPORT_FAILED", AxamlDiagnosticSeverity.Error, ex.ToString()));
            var placeholderSyntax = AxamlSyntaxDocument.Parse("<UserControl />");
            var placeholderMap = new AxamlSourceMap(null);
            AppendCapabilityDiagnostics(sourcePath, report, diagnostics);
            return new AxamlImportResult(
                new DesignerDocumentFileModel(),
                new AxamlRoundTripDocument(sourcePath ?? string.Empty, placeholderSyntax, placeholderMap, report),
                diagnostics);
        }

        var rootType = syntax.Root.LocalName;
        diagnostics.Add(new AxamlRoundTripDiagnostic("AXAML_IMPORT_ROOT_RESOLVED", AxamlDiagnosticSeverity.Information, $"type={rootType}"));
        var supportedRoot = IsAvaloniaElement(syntax.Root) && rootType is "Window" or "UserControl";
        var canvases = syntax.Root.Children.Where(element => IsAvaloniaElement(element) && element.LocalName == "Canvas").ToArray();
        // Do not flatten layouts across an unknown parent or choose one of several content roots.
        var contentRoots = syntax.Root.Children.Where(element => !element.LocalName.Contains('.')).ToArray();
        var canvas = supportedRoot && canvases.Length == 1 && contentRoots.Length == 1 ? canvases[0] : null;
        var document = CreateDocument(rootType, syntax.Root);
        var sourceMap = new AxamlSourceMap(canvas);
        var importedControls = new List<(DesignerControlFileModel Control, int ZIndex, int SourceOrder)>();
        var sourceOrder = 0;

        if (!supportedRoot)
            AddOpaqueSubtree(syntax.Root, "UnsupportedRoot", report, diagnostics);
        else
        {
            report.AddElement(CreateContainerCapability(syntax.Root));
            foreach (var child in syntax.Root.Children.Where(child => child != canvas))
                AddOpaqueSubtree(child, "UnsupportedContainerOrSubtree", report, diagnostics);
            if (canvas is not null)
                report.AddElement(CreateContainerCapability(canvas));
        }

        foreach (var element in canvas?.Children ?? Enumerable.Empty<AxamlElementSyntax>())
        {
            var controlType = element.LocalName;
            if (!IsAvaloniaElement(element) || !SupportedControlTypes.Contains(controlType))
            {
                AddOpaqueSubtree(element, "UnsupportedControl", report, diagnostics);
                continue;
            }

            if (element.Children.Count > 0 || HasUnsupportedInnerContent(syntax.Text, element))
            {
                AddOpaqueSubtree(element, "UnsupportedContent", report, diagnostics);
                continue;
            }

            var capability = new AxamlElementCapability(element, AxamlElementCapabilityMode.Editable);
            var control = CreateControl(controlType, element, knownControlIdsByName, diagnostics, capability);
            var zIndex = int.TryParse(element.GetAttributeValue("Canvas.ZIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedZIndex)
                ? parsedZIndex
                : sourceOrder;
            importedControls.Add((ToFileModel(control), zIndex, sourceOrder));
            sourceOrder++;
            var reference = new AxamlSourceReference(control.Id, control.Type, element, capability);
            foreach (var property in AxamlRoundTripPropertyMap.PropertiesFor(control.Type))
            {
                reference.SnapshotValues[property.Key] = property.Read(control);
            }

            sourceMap.Add(reference);
            report.AddElement(capability);
            diagnostics.Add(new AxamlRoundTripDiagnostic(
                "AXAML_IMPORT_CONTROL",
                AxamlDiagnosticSeverity.Information,
                $"type={controlType}; name={control.Name}; supported=true"));
        }

        foreach (var imported in importedControls.OrderBy(item => item.ZIndex).ThenBy(item => item.SourceOrder))
            document.Controls.Add(imported.Control);

        AppendCapabilityDiagnostics(sourcePath, report, diagnostics);
        diagnostics.Add(new AxamlRoundTripDiagnostic(
            "AXAML_CAPABILITY_REPORT",
            report.DocumentReadOnly ? AxamlDiagnosticSeverity.Warning : AxamlDiagnosticSeverity.Information,
            $"level={report.Level}; controls={document.Controls.Count}; entries={report.Entries.Count}"));
        return new AxamlImportResult(document, new AxamlRoundTripDocument(sourcePath ?? string.Empty, syntax, sourceMap, report), diagnostics);
    }

    private static bool IsAvaloniaElement(AxamlElementSyntax element) =>
        element.NamespaceUri is "" or "https://github.com/avaloniaui";

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
        AxamlElementCapability capability)
    {
        var control = new DesignControlModel { Type = controlType };
        foreach (var property in AxamlRoundTripPropertyMap.PropertiesFor(controlType))
        {
            var attribute = property.ResolveExistingAttribute(element);
            var editable = attribute is null || property.TryWrite(control, attribute.Value);
            capability.Properties.Add(new(property.Key, attribute?.Name ?? property.PreferredAttributeName,
                editable ? AxamlPropertyCapabilityMode.Editable : AxamlPropertyCapabilityMode.Preserved,
                editable ? "" : IsMarkupExtension(attribute!.Value) ? "MarkupExtension" : "UnsupportedValue"));
        }

        if (string.IsNullOrWhiteSpace(control.Name))
            control.Name = controlType;
        if (knownControlIdsByName is not null && knownControlIdsByName.TryGetValue(control.Name, out var existingId))
            control.Id = existingId;

        var knownAttributeNames = AxamlRoundTripPropertyMap.PropertiesFor(controlType)
            .SelectMany(property => property.AttributeNames)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes.Where(attribute => !knownAttributeNames.Contains(attribute.Name) && !IsNamespace(attribute)))
        {
            capability.Properties.Add(new(attribute.Name, attribute.Name, AxamlPropertyCapabilityMode.Preserved, "UnknownAttribute"));
            diagnostics.Add(new AxamlRoundTripDiagnostic(
                "AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED",
                AxamlDiagnosticSeverity.Information,
                $"element={element.Name}; attribute={attribute.Name}"));
        }

        if (capability.Properties.Any(p => p.Mode != AxamlPropertyCapabilityMode.Editable))
            capability.Mode = AxamlElementCapabilityMode.PartiallyEditable;
        return control;
    }

    private static bool IsNamespace(AxamlAttributeSyntax attribute) => attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal);

    private static AxamlElementCapability CreateContainerCapability(AxamlElementSyntax element)
    {
        var capability = new AxamlElementCapability(element, AxamlElementCapabilityMode.Editable);
        // Root metadata and container attributes are retained; this phase exposes only leaf editors.
        foreach (var attribute in element.Attributes.Where(a => !IsNamespace(a)))
        {
            capability.Properties.Add(new(attribute.Name, attribute.Name, AxamlPropertyCapabilityMode.Preserved, "ContainerMetadata"));
            if (IsMarkupExtension(attribute.Value) || attribute.Name is not ("x:Class" or "x:Name" or "Name" or "Title" or "Width" or "Height"))
                capability.Mode = AxamlElementCapabilityMode.PartiallyEditable;
        }
        return capability;
    }

    private static void AddOpaqueSubtree(AxamlElementSyntax element, string reason, AxamlCapabilityReport report,
        ICollection<AxamlRoundTripDiagnostic> diagnostics)
    {
        var pending = new Stack<(AxamlElementSyntax Element, string Reason)>();
        pending.Push((element, reason));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var capability = new AxamlElementCapability(current.Element, AxamlElementCapabilityMode.Opaque, current.Reason);
            foreach (var attribute in current.Element.Attributes.Where(a => !IsNamespace(a)))
                capability.Properties.Add(new(attribute.Name, attribute.Name, AxamlPropertyCapabilityMode.Preserved, current.Reason));
            report.AddElement(capability);
            diagnostics.Add(new("AXAML_IMPORT_UNKNOWN_NODE_PRESERVED", AxamlDiagnosticSeverity.Information,
                $"element={current.Element.Name}; reason={current.Reason}"));
            foreach (var child in current.Element.Children.AsEnumerable().Reverse())
                pending.Push((child, "OpaqueAncestor:" + current.Element.Name));
        }
    }

    private static void AppendCapabilityDiagnostics(string? path, AxamlCapabilityReport report, ICollection<AxamlRoundTripDiagnostic> diagnostics)
    {
        foreach (var element in report.Elements)
            diagnostics.Add(new("AXAML_ELEMENT_CAPABILITY", AxamlDiagnosticSeverity.Information,
                $"name={element.Name}; type={element.Element.Name}; mode={element.Mode}; reason={element.Reason}; " +
                $"editableProperties={string.Join(",", element.Properties.Where(p => p.Mode == AxamlPropertyCapabilityMode.Editable).Select(p => p.SourceName))}; " +
                $"opaqueProperties={string.Join(",", element.Properties.Where(p => p.Mode != AxamlPropertyCapabilityMode.Editable).Select(p => p.SourceName))}"));
        diagnostics.Add(new("AXAML_DOCUMENT_CAPABILITY", AxamlDiagnosticSeverity.Information,
            $"document={path}; editableElements={report.Elements.Count(e => e.Mode == AxamlElementCapabilityMode.Editable)}; " +
            $"partialElements={report.Elements.Count(e => e.Mode == AxamlElementCapabilityMode.PartiallyEditable)}; " +
            $"opaqueElements={report.Elements.Count(e => e.Mode == AxamlElementCapabilityMode.Opaque)}; " +
            $"editableProperties={report.Elements.Sum(e => e.Properties.Count(p => p.Mode == AxamlPropertyCapabilityMode.Editable))}; " +
            $"opaqueProperties={report.Elements.Sum(e => e.Properties.Count(p => p.Mode != AxamlPropertyCapabilityMode.Editable))}; " +
            $"fatalIssues={report.Entries.Count(e => e.Level == AxamlCapabilityLevel.UnsafeToSave)}; " +
            $"documentReadOnly={report.DocumentReadOnly}; reason={report.ReadOnlyReason}"));
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
    private static bool TryParseDouble(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

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
    private readonly Func<DesignControlModel, string, bool> _write;

    public AxamlRoundTripProperty(string key, IEnumerable<string> attributeNames, Func<DesignControlModel, string> read, Func<DesignControlModel, string, bool> write)
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

    public bool TryWrite(DesignControlModel control, string value)
    {
        return !value.TrimStart().StartsWith("{", StringComparison.Ordinal) && _write(control, WebUtility.HtmlDecode(value));
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
        new(key, attributeNames, read, (control, value) => { write(control, value); return true; });

    private static AxamlRoundTripProperty Number(string key, Func<DesignControlModel, double> read, Action<DesignControlModel, double> write) =>
        new(key, new[] { key }, control => read(control).ToString("0.###", CultureInfo.InvariantCulture), (control, value) =>
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
                return false;
            write(control, parsed);
            return true;
        });

    private static AxamlRoundTripProperty Bool(string key, Func<DesignControlModel, bool> read, Action<DesignControlModel, bool> write) =>
        new(key, new[] { key }, control => read(control) ? "True" : "False", (control, value) =>
        {
            if (!bool.TryParse(value, out var parsed))
                return false;
            write(control, parsed);
            return true;
        });
}
