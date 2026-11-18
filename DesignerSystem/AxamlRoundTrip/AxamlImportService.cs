using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>
/// Builds a DesignerDocument projection for the conservative Phase 1 AXAML subset.
/// Syntax unknown to this service remains in the source document and is never
/// reconstructed by this service.
/// </summary>
public sealed class AxamlImportService
{
    public AxamlImportResult Import(
        string sourceText,
        string? sourcePath = null,
        IReadOnlyDictionary<string, string>? knownControlIdsByName = null,
        bool detailedDiagnostics = false)
        => ImportCore(sourceText,sourcePath,knownControlIdsByName,detailedDiagnostics,null);

    internal AxamlImportResult ImportParsed(AxamlSyntaxDocument syntax,string? sourcePath,
        IReadOnlyDictionary<string,string>? knownControlIdsByName)
        => ImportCore(syntax.Text,sourcePath,knownControlIdsByName,false,syntax);

    private AxamlImportResult ImportCore(string sourceText,string? sourcePath,
        IReadOnlyDictionary<string,string>? knownControlIdsByName,bool detailedDiagnostics,AxamlSyntaxDocument? preparedSyntax)
    {
        detailedDiagnostics |= string.Equals(Environment.GetEnvironmentVariable("FORMDESIGNER_AXAML_IMPORT_DIAGNOSTICS"), "verbose", StringComparison.OrdinalIgnoreCase);
        var diagnostics = new List<AxamlRoundTripDiagnostic>
        {
            new("AXAML_IMPORT_START", AxamlDiagnosticSeverity.Information, $"path={sourcePath ?? string.Empty}; length={sourceText?.Length ?? 0}")
        };
        var report = new AxamlCapabilityReport();
        AxamlSyntaxDocument syntax;

        try
        {
            DesignerDocumentFormat.Validate(sourceText ?? string.Empty, DesignerDocumentKind.AxamlRoundTrip, nameof(AxamlImportService));
            if(preparedSyntax is not null) syntax=preparedSyntax;
            else using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.SyntaxParse"))
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
        // Do not flatten layouts across an unknown parent or choose one of several content roots.
        var directRoots = syntax.Root.Children.Where(element => !element.LocalName.Contains('.')).ToArray();
        var contentWrappers = syntax.Root.Children.Where(element => AxamlImportStructureReport.IsPropertyElement(element)
            && element.LocalName.EndsWith(".Content", StringComparison.Ordinal)).ToArray();
        var rootContent = supportedRoot && directRoots.Length == 0 && contentWrappers.Length == 1 && contentWrappers[0].Children.Count == 1
            && !HasUnsupportedInnerContent(syntax.Text, contentWrappers[0], ignoreChildElements: true)
            && !syntax.Root.Attributes.Any(a => a.Name == "Content" || a.Name.EndsWith(".Content", StringComparison.Ordinal))
            && AxamlPropertyElementInfo.Inspect(contentWrappers[0], syntax.Text).Metadata?.Name == "Content" ? contentWrappers[0] : null;
        var contentRoots = contentWrappers.Length == 0 ? directRoots : rootContent?.Children.ToArray() ?? Array.Empty<AxamlElementSyntax>();
        var canvases = contentRoots.Where(element => IsAvaloniaElement(element) && element.LocalName == "Canvas").ToArray();
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
            foreach (var child in syntax.Root.Children.Where(AxamlImportStructureReport.IsPropertyElement))
                if (child == rootContent) report.AddElement(CreateContainerCapability(child));
                else AddOpaqueSubtree(child, "PropertyElementPreserved", report, diagnostics);
            if (contentWrappers.Length > 0 && rootContent is null)
                foreach (var child in directRoots) AddOpaqueSubtree(child, "AmbiguousContentRoots", report, diagnostics);
            if (canvas is not null)
                report.AddElement(CreateContainerCapability(canvas));
        }

        void ImportElement(AxamlElementSyntax element, string? parentId, string parentType)
        {
            var controlType = element.LocalName;
            if (AxamlImportStructureReport.IsPropertyElement(element))
            {
                AddOpaqueSubtree(element, "PropertyElementPreserved", report, diagnostics);
                return;
            }
            AxamlControlMetadata? metadata;
            using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.TypeResolution"))
                metadata = AxamlControlMetadata.FindSourceElement(element, parentType);
            if (metadata is null)
            {
                AddOpaqueSubtree(element, IsAvaloniaElement(element) ? "UnsupportedControl" : "UnsupportedCustomControl", report, diagnostics);
                return;
            }

            var isContainer = metadata.ContainerKind != AxamlContainerKind.None;
            var childWrappers = element.Children.Where(metadata.IsChildProperty).ToArray();
            var directChildren = element.Children.Where(e => !AxamlImportStructureReport.IsPropertyElement(e)).ToArray();
            var visualChildren = directChildren.Concat(childWrappers.SelectMany(e => e.Children)).ToArray();
            var textContent = "";
            var hasTextContent = metadata.TextContentProperty is not null && TryReadTextContent(syntax.Text, element, out textContent);
            if ((!isContainer && (visualChildren.Length > 0 || !hasTextContent && HasUnsupportedInnerContent(syntax.Text, element)))
                || (visualChildren.Length == 0 && metadata.ContainerKind is AxamlContainerKind.SingleContent or AxamlContainerKind.HeaderedContent
                    && !hasTextContent && HasUnsupportedInnerContent(syntax.Text, element))
                || (hasTextContent && (visualChildren.Length > 0 || element.FindAttribute(metadata.TextContentProperty!) is not null))
                || (metadata.ContainerKind is AxamlContainerKind.SingleContent or AxamlContainerKind.HeaderedContent or AxamlContainerKind.Decorator && visualChildren.Length > 1)
                || childWrappers.Length > 1 || (childWrappers.Length > 0 && directChildren.Length > 0)
                || (visualChildren.Length > 0 && element.FindAttribute(metadata.ChildProperty ?? "") is not null))
            {
                AddOpaqueSubtree(element, "UnsupportedContent", report, diagnostics);
                return;
            }

            var capability = new AxamlElementCapability(element, AxamlElementCapabilityMode.Editable);
            var control = CreateControl(controlType, element, knownControlIdsByName, diagnostics, capability);
            var scalarElements = new Dictionary<string, AxamlPropertyElementInfo>(StringComparer.Ordinal);
            using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.PropertyElements"))
            {
            foreach (var property in element.Children.Count == 0 ? Array.Empty<AxamlRoundTripProperty>() : AxamlRoundTripPropertyMap.PropertiesFor(controlType).Where(p => p.Metadata is not null))
            {
                var candidates = element.Children.Where(e => AxamlImportStructureReport.IsPropertyElement(e)
                    && e.LocalName.EndsWith("." + property.Metadata!.Name, StringComparison.Ordinal)).ToArray();
                if (candidates.Length != 1 || property.ResolveExistingAttribute(element) is not null) continue;
                var info = AxamlPropertyElementInfo.Inspect(candidates[0], syntax.Text);
                if (info.Metadata != property.Metadata || info.ValueSpan is not { } span || !property.TryWrite(control, syntax.Text.Substring(span.Start, span.Length))) continue;
                scalarElements.Add(property.Key, info with { Mode = AxamlPropertyCapabilityMode.Editable, Reason = "ScalarTextSpan" });
            }
            }
            if (StandardItemsProperties.IsMenuButton(controlType)) ImportStaticFlyout(control, element);
            if (hasTextContent)
            {
                var literal = metadata.LiteralProperties.Single(p => p.Key == metadata.TextContentProperty);
                literal.Write(control, textContent!);
                PreserveProperty(capability, literal.Key, "TextContentPreserved");
            }
            // Generic containers already participate in the shared ParentId hierarchy.
            // Source type remains on Element; it is never regenerated from the Group alias.
            control.Type = metadata.ProjectionType;
            control.ParentId = parentId ?? "";
            control.StackOrder = element.Parent?.Children.IndexOf(element) ?? 0;
            control.ChildLayoutMode = isContainer && (controlType != "Button" || visualChildren.Length > 0)
                ? DesignerLayoutModes.GetModeForControlType(control.Type) : "";
            if (controlType == "StackPanel" && element.FindAttribute("Spacing") is null) control.LayoutSpacing = 0;
            if (controlType == "Grid")
            {
                ImportDefinitions(element, control, capability);
                control.ShowGridLines = false;
            }
            foreach (var property in capability.Properties.ToArray())
            {
                var incompatible = property.Key.StartsWith("Canvas.", StringComparison.Ordinal) && parentType != "Canvas"
                    || property.Key == "DockPanel.Dock" && parentType != "DockPanel"
                    || property.Key.StartsWith("Grid", StringComparison.Ordinal) && property.Key is "GridRow" or "GridColumn" or "GridRowSpan" or "GridColumnSpan" && parentType != "Grid";
                if (incompatible)
                    capability.Properties[capability.Properties.IndexOf(property)] = property with { Mode = AxamlPropertyCapabilityMode.Unsupported, Reason = "ParentLayout:" + parentType };
            }
            // A property element overrides the absent attribute; do not synthesize a second value.
            foreach (var propertyElement in element.Children.Where(AxamlImportStructureReport.IsPropertyElement))
            {
                var sourceName = propertyElement.LocalName.Split('.').Last();
                foreach (var property in capability.Properties.Where(p => p.SourceName == sourceName || p.SourceName.EndsWith("." + sourceName, StringComparison.Ordinal)).ToArray())
                    if (!scalarElements.ContainsKey(property.Key))
                        capability.Properties[capability.Properties.IndexOf(property)] = property with { Mode = AxamlPropertyCapabilityMode.Preserved, Reason = "PropertyElementValue" };
            }
            foreach (var property in capability.Properties.Where(p => p.Mode == AxamlPropertyCapabilityMode.Editable).ToArray())
                if (!scalarElements.ContainsKey(property.Key) && element.FindAttribute(property.SourceName) is null && AxamlControlMetadata.HasSourceValue(element, property.SourceName))
                    capability.Properties[capability.Properties.IndexOf(property)] = property with { Mode = AxamlPropertyCapabilityMode.Preserved, Reason = "QualifiedSourceValuePreserved" };
            if (visualChildren.Length > 0 && metadata.ContainerKind is AxamlContainerKind.SingleContent or AxamlContainerKind.HeaderedContent)
                foreach (var property in capability.Properties.Where(p => p.SourceName == metadata.ChildProperty).ToArray())
                    capability.Properties[capability.Properties.IndexOf(property)] = property with { Mode = AxamlPropertyCapabilityMode.Preserved, Reason = "VisualContentChild" };
            if (controlType is "ComboBox" or "ListBox" && (AxamlControlMetadata.HasSourceValue(element, "SelectedItem") || AxamlControlMetadata.HasSourceValue(element, "SelectedValue")))
                PreserveProperty(capability, "SelectedIndex", "SelectionSourcePreserved");
            if (capability.Properties.Any(p => p.Mode == AxamlPropertyCapabilityMode.Preserved
                || p.Mode == AxamlPropertyCapabilityMode.Unsupported && AxamlControlMetadata.HasSourceValue(element, p.SourceName)))
                capability.Mode = AxamlElementCapabilityMode.PartiallyEditable;
            var zIndex = int.TryParse(element.GetAttributeValue("Canvas.ZIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedZIndex)
                ? parsedZIndex
                : sourceOrder;
            var reference = new AxamlSourceReference(control.Id, control.Type, element, capability) { ParentId = parentId };
            AxamlPatchWriter.ImportExpressions(reference,control);
            using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.FileModelConstruction"))
                importedControls.Add((ToFileModel(control), zIndex, sourceOrder));
            sourceOrder++;
            using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.SourceReferenceGeneration"))
            {
            foreach (var scalar in scalarElements) reference.PropertyElements.Add(scalar.Key, scalar.Value);
            foreach (var property in AxamlRoundTripPropertyMap.PropertiesFor(controlType))
            {
                reference.SnapshotValues[property.Key] = property.Read(control);
            }
            reference.SnapshotValues["@ChildLayoutMode"] = control.ChildLayoutMode;

            sourceMap.Add(reference);
            report.AddElement(capability);
            }
            diagnostics.Add(new AxamlRoundTripDiagnostic(
                "AXAML_IMPORT_CONTROL",
                AxamlDiagnosticSeverity.Information,
                $"type={controlType}; name={control.Name}; supported=true"));
            void ImportChild(AxamlElementSyntax child)
            {
                if (metadata.ContainerKind == AxamlContainerKind.Items && (AxamlControlMetadata.HasSourceValue(element, "ItemsSource")
                    || !metadata.AcceptsItem(child)))
                    AddOpaqueSubtree(child, "UnsupportedStaticItemOrItemsSource", report, diagnostics);
                else ImportElement(child, control.Id, controlType);
            }
            foreach (var child in element.Children)
            {
                if (metadata.IsChildProperty(child))
                {
                    report.AddElement(CreateContainerCapability(child));
                    foreach (var item in child.Children) ImportChild(item);
                }
                else if (AxamlImportStructureReport.IsPropertyElement(child))
                {
                    if (scalarElements.Values.Any(p => p.Element == child)) report.AddElement(CreateContainerCapability(child));
                    else AddOpaqueSubtree(child, "PropertyElementPreserved", report, diagnostics);
                }
                else ImportChild(child);
            }
        }

        using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.SourceMapProjection"))
        {
            if (supportedRoot)
            {
                if (canvas is not null)
                    foreach (var child in canvas.Children) ImportElement(child, null, "Canvas");
                else if (contentRoots.Length == 1)
                    ImportElement(contentRoots[0], null, rootType);
                else
                    foreach (var child in contentRoots) AddOpaqueSubtree(child, "AmbiguousContentRoots", report, diagnostics);
            }
        }

        foreach (var imported in importedControls.OrderBy(item => item.ZIndex).ThenBy(item => item.SourceOrder))
            document.Controls.Add(imported.Control);

        foreach (var element in AxamlImportStructureReport.Descendants(syntax.Root).Where(AxamlImportStructureReport.IsPropertyElement))
            report.PropertyElements.Add(sourceMap.Controls.SelectMany(r => r.PropertyElements.Values).FirstOrDefault(p => p.Element == element)
                ?? AxamlPropertyElementInfo.Inspect(element, syntax.Text));
        using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.StructureReport"))
            report.Structure = AxamlImportStructureReport.Create(sourcePath, syntax, sourceMap, report, diagnostics, detailedDiagnostics);
        foreach (var type in new[] { "Expander", "ComboBox", "ItemsControl", "ListBox", "TreeView", "ProgressBar" })
        {
            var references = sourceMap.Controls.Where(r => r.Element.LocalName == type).ToArray();
            var prefix = "AXAML_" + type.ToUpperInvariant();
            diagnostics.Add(new(prefix + "_IMPORTED", AxamlDiagnosticSeverity.Information, $"count={references.Length}"));
            diagnostics.Add(new(prefix + (type == "Expander" ? "_CHILDREN_IMPORTED" : "_ITEMS_IMPORTED"), AxamlDiagnosticSeverity.Information,
                $"count={sourceMap.Controls.Count(r => references.Any(parent => r.ParentId == parent.ControlId))}"));
        }
        if (detailedDiagnostics)
            foreach (var reference in sourceMap.Controls)
                foreach (var property in reference.Capability.Properties.Where(p => p.Mode != AxamlPropertyCapabilityMode.Editable))
                    diagnostics.Add(new("AXAML_PROPERTY_OPAQUE", AxamlDiagnosticSeverity.Information,
                        $"path={AxamlImportStructureReport.PathOf(reference.Element)}; property={property.SourceName}; reason={property.Reason}"));
        else
        {
            var unknownAttributes = diagnostics.Count(d => d.Code == "AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED");
            diagnostics.RemoveAll(d => d.Code is "AXAML_ELEMENT_DISCOVERED" or "AXAML_ELEMENT_IMPORTED" or "AXAML_ELEMENT_OPAQUE" or "AXAML_IMPORT_CONTROL" or "AXAML_IMPORT_UNKNOWN_NODE_PRESERVED" or "AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED");
            if (unknownAttributes > 0)
                diagnostics.Add(new("AXAML_IMPORT_UNKNOWN_ATTRIBUTE_PRESERVED", AxamlDiagnosticSeverity.Information, $"count={unknownAttributes}"));
            diagnostics.Add(new("AXAML_PROPERTY_OPAQUE", AxamlDiagnosticSeverity.Information,
                $"count={report.Elements.Sum(e => e.Properties.Count(p => p.Mode != AxamlPropertyCapabilityMode.Editable))}; detailed=false"));
        }
        AppendCapabilityDiagnostics(sourcePath, report, diagnostics, detailedDiagnostics);
        diagnostics.Add(new AxamlRoundTripDiagnostic(
            "AXAML_CAPABILITY_REPORT",
            report.DocumentReadOnly ? AxamlDiagnosticSeverity.Warning : AxamlDiagnosticSeverity.Information,
            $"level={report.Level}; controls={document.Controls.Count}; entries={report.Entries.Count}"));
        var mapped = new AxamlRoundTripDocument(sourcePath ?? string.Empty, syntax, sourceMap, report);
        var fields = mapped.StyleCatalog.Fields;
        if (fields.Count != 0)
            document.StyleSourceValues = fields.ToDictionary(f => f.Id, f => f.Value, StringComparer.Ordinal);
        return new AxamlImportResult(document, mapped, diagnostics);
    }

    private static void ImportDefinitions(AxamlElementSyntax element, DesignControlModel control, AxamlElementCapability capability)
    {
        foreach (var axis in new[] { "Row", "Column" })
        {
            var definitions = element.Children.FirstOrDefault(e => e.LocalName == "Grid." + axis + "Definitions");
            if (definitions is null) continue;
            var value = string.Join(",", definitions.Children.Select(e => e.GetAttributeValue(axis == "Row" ? "Height" : "Width") ?? "*"));
            if (axis == "Row") control.GridRowDefinitions = value;
            else control.GridColumnDefinitions = value;
            var key = "Grid" + axis + "Definitions";
            var property = capability.Properties.FirstOrDefault(p => p.Key == key);
            if (property is not null)
                capability.Properties[capability.Properties.IndexOf(property)] = property with { Mode = AxamlPropertyCapabilityMode.Preserved, Reason = "DefinitionElementsPreserved" };
        }
    }

    private static void ImportStaticFlyout(DesignControlModel model, AxamlElementSyntax element)
    {
        var flyout = element.Children.FirstOrDefault(c => c.LocalName == element.LocalName + ".Flyout");
        if (flyout is null) return;
        var items = new List<StandardMenuItem>();
        var placement = flyout.Children.FirstOrDefault(c => c.LocalName is "Flyout" or "MenuFlyout")?.GetAttributeValue("Placement");
        if (placement is not null && !placement.TrimStart().StartsWith('{')) StandardItemsProperties.Write(model, "Flyout.Placement", placement);
        void Read(AxamlElementSyntax current)
        {
            if (!IsAvaloniaElement(current)) return;
            if (current.LocalName is "Button" or "MenuItem")
            {
                var text = current.GetAttributeValue(current.LocalName == "Button" ? "Content" : "Header");
                if (text is not null && !text.TrimStart().StartsWith('{'))
                    items.Add(new StandardMenuItem { Id = "source_" + items.Count, Text = text,
                        IsEnabled = !string.Equals(current.GetAttributeValue("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase) });
                return;
            }
            if (current.LocalName is "Flyout" or "MenuFlyout" or "StackPanel" or "Flyout.Content")
                foreach (var child in current.Children) Read(child);
        }
        foreach (var child in flyout.Children) Read(child);
        StandardItemsProperties.Write(model, StandardItemsProperties.MenuKey, items);
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
        DesignControlModel control;
        using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.ModelConstruction")) control = CreateSourceControlDefaults(controlType);
        IReadOnlyList<AxamlRoundTripProperty> properties;
        using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.MetadataLookup")) properties = AxamlRoundTripPropertyMap.PropertiesFor(controlType);
        foreach (var property in properties)
        {
            AxamlAttributeSyntax? attribute;
            bool ambiguous;
            using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.PropertyResolution"))
            {
                attribute = property.ResolveExistingAttribute(element);
                ambiguous = property.HasAmbiguousAttribute(element);
            }
            var editable = !ambiguous;
            if (editable && attribute is not null)
                using (FormDesigner.Services.LayoutDropProfile.Stage("AXAML.ValueConversion")) editable = property.TryWrite(control, attribute.Value);
            capability.Properties.Add(new(property.Key, attribute?.Name ?? property.PreferredAttributeName,
                editable ? AxamlPropertyCapabilityMode.Editable : AxamlPropertyCapabilityMode.Preserved,
                editable ? "" : ambiguous ? "AmbiguousAttributeAliases" : IsMarkupExtension(attribute!.Value) ? "MarkupExtension" : "UnsupportedValue"));
        }

        if (string.IsNullOrWhiteSpace(control.Name))
            control.Name = controlType;
        var identity = element.GetAttributeValue("x:Name") ?? element.GetAttributeValue("Name")
            ?? "@path:" + AxamlImportStructureReport.PathOf(element);
        if (knownControlIdsByName is not null && knownControlIdsByName.TryGetValue(identity, out var existingId))
            control.Id = existingId;

        var knownAttributeNames = AxamlRoundTripPropertyMap.AttributeNamesFor(controlType);
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

    private static void PreserveProperty(AxamlElementCapability capability, string key, string reason)
    {
        var property = capability.Properties.Single(p => p.Key == key);
        capability.Properties[capability.Properties.IndexOf(property)] = property with { Mode = AxamlPropertyCapabilityMode.Preserved, Reason = reason };
    }

    private static bool TryReadTextContent(string source, AxamlElementSyntax element, out string text)
    {
        text = "";
        if (element.IsSelfClosing) return false;
        var content = source.Substring(element.ContentStart, element.EndTagSpan.Start - element.ContentStart);
        foreach (var child in element.Children.OrderByDescending(c => c.ElementSpan.Start))
            content = content.Remove(child.ElementSpan.Start - element.ContentStart, child.ElementSpan.Length);
        try
        {
            // Decode XML entities/CDATA without resolving external resources or executing markup.
            text = XElement.Parse("<value>" + content + "</value>").Value.Trim();
            return text.Length > 0;
        }
        catch (XmlException) { return false; }
    }

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

    private static void AppendCapabilityDiagnostics(string? path, AxamlCapabilityReport report, ICollection<AxamlRoundTripDiagnostic> diagnostics, bool detailed = false)
    {
        foreach (var element in detailed ? report.Elements : Enumerable.Empty<AxamlElementCapability>())
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
        ParentId = model.ParentId,
        ChildLayoutMode = model.ChildLayoutMode,
        LayoutOrientation = model.LayoutOrientation,
        LayoutSpacing = model.LayoutSpacing,
        GridRow = model.GridRow, GridColumn = model.GridColumn,
        GridRowSpan = model.GridRowSpan, GridColumnSpan = model.GridColumnSpan,
        StackOrder = model.StackOrder,
        GridRowDefinitions = model.GridRowDefinitions, GridColumnDefinitions = model.GridColumnDefinitions,
        ShowGridLines = model.ShowGridLines,
        CustomProperties = model.CustomProperties.Select(p => new DesignPropertyValueFileModel { Key = p.Key, ValueJson = p.ValueJson }).ToList(),
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

    internal static DesignControlModel CreateSourceControlDefaults(string controlType) =>
        new() { UsesSourceLayout = true, Type = controlType, VerticalAlignment = "Stretch", Padding = 0 };

    private static bool HasUnsupportedInnerContent(string source, AxamlElementSyntax element, bool ignoreChildElements = false)
    {
        if (element.IsSelfClosing || element.EndTagSpan.IsEmpty)
            return false;

        var contentLength = element.EndTagSpan.Start - element.ContentStart;
        if (contentLength <= 0)
            return false;

        var content = source.Substring(element.ContentStart, contentLength);
        foreach (var child in element.Children.Where(e => ignoreChildElements || AxamlImportStructureReport.IsPropertyElement(e)).OrderByDescending(c => c.ElementSpan.Start))
            content = content.Remove(child.ElementSpan.Start - element.ContentStart, child.ElementSpan.Length);
        content = Regex.Replace(content, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        return !string.IsNullOrWhiteSpace(content);
    }
}

internal sealed class AxamlRoundTripProperty
{
    private readonly Func<DesignControlModel, string> _read;
    private readonly Func<DesignControlModel, string, bool> _write;
    private readonly bool _allowNullLiteral;

    public AxamlRoundTripProperty(string key, IEnumerable<string> attributeNames, Func<DesignControlModel, string> read, Func<DesignControlModel, string, bool> write, bool allowNullLiteral = false,
        FormDesigner.DesignerSystem.Metadata.AvaloniaPropertyInfo? metadata = null)
    {
        Key = key;
        AttributeNames = attributeNames.ToArray();
        _read = read;
        _write = write;
        _allowNullLiteral = allowNullLiteral;
        Metadata = metadata;
    }

    public string Key { get; }
    public FormDesigner.DesignerSystem.Metadata.AvaloniaPropertyInfo? Metadata { get; }
    public IReadOnlyList<string> AttributeNames { get; }
    public string PreferredAttributeName => AttributeNames[0];
    public string Read(DesignControlModel control) => _read(control);

    public bool TryWrite(DesignControlModel control, string value)
    {
        if (value.TrimStart().StartsWith("{", StringComparison.Ordinal) && !(_allowNullLiteral && value == "{x:Null}")) return false;
        value = WebUtility.HtmlDecode(value);
        if (value.TrimStart().StartsWith("{", StringComparison.Ordinal) && !(_allowNullLiteral && value == "{x:Null}")) return false;
        // The Inspector's null marker is not an AXAML string literal null. Preserve
        // that source spelling rather than silently turning e.g. GroupName into null.
        if (Metadata?.PropertyType == typeof(string) && value == FormDesigner.DesignerSystem.Metadata.MetadataValueCodec.NullText) return false;
        try
        {
            if (Key == "Margin") _ = Avalonia.Thickness.Parse(value);
            if (Metadata is null && (Key is "Background" or "Foreground" or "BorderBrush")) _ = Avalonia.Media.Brush.Parse(value);
            if (Key == "GridRowDefinitions") _ = Avalonia.Controls.RowDefinitions.Parse(value);
            if (Key == "GridColumnDefinitions") _ = Avalonia.Controls.ColumnDefinitions.Parse(value);
            if (Key == "HorizontalAlignment" && !Enum.TryParse<Avalonia.Layout.HorizontalAlignment>(value, out _)) return false;
            if (Key == "VerticalAlignment" && !Enum.TryParse<Avalonia.Layout.VerticalAlignment>(value, out _)) return false;
            if (Key == "LayoutOrientation" && value is not ("Horizontal" or "Vertical")) return false;
            return _write(control, value);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) { return false; }
    }

    public AxamlAttributeSyntax? ResolveExistingAttribute(AxamlElementSyntax element)
    {
        for (var i = 0; i < AttributeNames.Count; i++)
            if (element.FindAttribute(AttributeNames[i]) is { } attribute) return attribute;
        return null;
    }

    public bool HasAmbiguousAttribute(AxamlElementSyntax element)
    {
        var count = 0;
        foreach (var attribute in element.Attributes)
            for (var i = 0; i < AttributeNames.Count; i++)
                if (attribute.Name == AttributeNames[i] && ++count > 1) return true;
        return false;
    }
}

internal static class AxamlRoundTripPropertyMap
{
    public static bool CanInsertControl(string type) => type is DesignerControlTypes.Button or DesignerControlTypes.TextBox
        or DesignerControlTypes.TextBlock or DesignerControlTypes.Border or DesignerControlTypes.CheckBox;

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
        Integer("GridRow", "Grid.Row", c => c.GridRow, (c, v) => c.GridRow = v, 0),
        Integer("GridColumn", "Grid.Column", c => c.GridColumn, (c, v) => c.GridColumn = v, 0),
        Integer("GridRowSpan", "Grid.RowSpan", c => c.GridRowSpan, (c, v) => c.GridRowSpan = v, 1),
        Integer("GridColumnSpan", "Grid.ColumnSpan", c => c.GridColumnSpan, (c, v) => c.GridColumnSpan = v, 1),
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

    private sealed record PropertySet(IReadOnlyList<AxamlRoundTripProperty> Properties, IReadOnlySet<string> AttributeNames);
    private static readonly IReadOnlySet<string> EmptyNames = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Lazy<IReadOnlyDictionary<string, PropertySet>> SourceProperties = new(() =>
        AxamlControlMetadata.SourceTypeNames.Append("Group").ToDictionary(type => type, type =>
        {
            var properties = BuildPropertiesFor(type);
            return new PropertySet(properties, properties.SelectMany(p => p.AttributeNames).ToHashSet(StringComparer.Ordinal));
        }, StringComparer.Ordinal));

    public static IReadOnlyList<AxamlRoundTripProperty> PropertiesFor(string controlType) =>
        SourceProperties.Value.TryGetValue(controlType, out var set) ? set.Properties : Array.Empty<AxamlRoundTripProperty>();
    public static IReadOnlySet<string> AttributeNamesFor(string controlType) =>
        SourceProperties.Value.TryGetValue(controlType, out var set) ? set.AttributeNames : EmptyNames;

    private static IReadOnlyList<AxamlRoundTripProperty> BuildPropertiesFor(string controlType)
    {
        var literals = AxamlControlMetadata.Find(controlType)?.LiteralProperties ?? Array.Empty<AxamlLiteralProperty>();
        var keys = literals.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var nativeType=AxamlControlMetadata.Find(controlType)?.NativeType
            ?? FormDesigner.DesignerSystem.Metadata.StandardControlProperties.PropertyTypeFor(controlType);
        var nativeKeys=nativeType is null?null:FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.Get(nativeType).Properties.Select(p=>p.Key).ToHashSet(StringComparer.Ordinal);
        return StandardPropertiesFor(controlType).Where(p => !keys.Contains(p.Key)
            && (nativeKeys is null||nativeKeys.Contains(p.Key=="Name"?"Name":p.PreferredAttributeName)))
            .Concat(literals.Select(p => new AxamlRoundTripProperty(p.Key,
                new[] { p.Key, controlType + "." + p.Key },
                c => ReadLiteral(p, c), p.Write,
                allowNullLiteral: p.Metadata?.Nullable == true, metadata: p.Metadata)))
            .Concat(nativeType is null ? Array.Empty<AxamlRoundTripProperty>() : FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.Get(nativeType).Properties
                .Where(p=>p.Key is "Command" or "CommandParameter" && !p.ReadOnly && p.Property is not null)
                .Select(p=>new AxamlRoundTripProperty(p.Key,new[]{p.Key,controlType+"."+p.Key},c=>p.DefaultText,(_,_)=>false,metadata:p))).ToArray();
    }

    private static string ReadLiteral(AxamlLiteralProperty property, DesignControlModel control)
    {
        var text = property.Read(control);
        return property.Metadata is not null && text == FormDesigner.DesignerSystem.Metadata.MetadataValueCodec.NullText ? "{x:Null}" : text;
    }

    private static IReadOnlyList<AxamlRoundTripProperty> StandardPropertiesFor(string controlType) => controlType switch
    {
        DesignerControlTypes.Button or DesignerControlTypes.DropDownButton or DesignerControlTypes.SplitButton or "ToggleSwitch" or "RadioButton" or "HyperlinkButton" or "RepeatButton" or "Label" or "ToggleButton" => Button,
        "MaskedTextBox" or "AccessText" or "SelectableTextBlock" => TextBlock,
        DesignerControlTypes.TextBox => TextBox,
        DesignerControlTypes.TextBlock => TextBlock,
        DesignerControlTypes.CheckBox => CheckBox,
        DesignerControlTypes.Border => Common,
        DesignerControlTypes.Group or "DockPanel" or "Canvas" or "ScrollViewer" or "TabControl" or "TabItem" or "WrapPanel" or "Expander" or "ComboBox" or "ComboBoxItem"
            or "ContentControl" or "Carousel" or "TabStrip" or "TabStripItem" or "ItemsControl" or "ListBox" or "ListBoxItem" or "TreeView" or "TreeViewItem" or "ProgressBar" or "Menu" or "MenuItem" or "ContextMenu" => Common,
        DesignerControlTypes.LayoutGrid => Common.Concat(new[]
        {
            Text("GridRowDefinitions", "RowDefinitions", c => c.GridRowDefinitions, (c, v) => c.GridRowDefinitions = v),
            Text("GridColumnDefinitions", "ColumnDefinitions", c => c.GridColumnDefinitions, (c, v) => c.GridColumnDefinitions = v)
        }).ToArray(),
        DesignerControlTypes.StackLayout => Common.Concat(new[]
        {
            Text("LayoutOrientation", "Orientation", c => c.LayoutOrientation, (c, v) => c.LayoutOrientation = v),
            new AxamlRoundTripProperty("LayoutSpacing", new[] { "Spacing" }, c => c.LayoutSpacing.ToString(CultureInfo.InvariantCulture), (c, v) =>
            {
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < 0) return false;
                c.LayoutSpacing = n; return true;
            })
        }).ToArray(),
        _ => FormDesigner.DesignerSystem.Metadata.StandardControlProperties.SupportedTypes.ContainsKey(controlType) ? Common : Array.Empty<AxamlRoundTripProperty>()
    };

    private static AxamlRoundTripProperty Integer(string key, string attribute, Func<DesignControlModel, int> read, Action<DesignControlModel, int> write, int minimum) =>
        new(key, new[] { attribute }, c => read(c).ToString(CultureInfo.InvariantCulture), (c, value) =>
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < minimum) return false;
            write(c, n); return true;
        });

    private static AxamlRoundTripProperty Text(string key, string attributeName, Func<DesignControlModel, string> read, Action<DesignControlModel, string> write) =>
        Text(key, new[] { attributeName }, read, write);

    private static AxamlRoundTripProperty Text(string key, IEnumerable<string> attributeNames, Func<DesignControlModel, string> read, Action<DesignControlModel, string> write) =>
        new(key, attributeNames, read, (control, value) => { write(control, value); return true; });

    private static AxamlRoundTripProperty Number(string key, Func<DesignControlModel, double> read, Action<DesignControlModel, double> write) =>
        new(key, new[] { key }, control => read(control).ToString("0.###", CultureInfo.InvariantCulture), (control, value) =>
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
                return false;
            if (key is "Width" or "Height" && parsed < 0) return false;
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
