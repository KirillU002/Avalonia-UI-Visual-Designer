using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>Source inventory, separate from the smaller, safe visual projection.</summary>
public sealed class AxamlImportStructureReport
{
    public string DocumentPath { get; init; } = "";
    public string RootType { get; init; } = "";
    public string VisualRoot { get; init; } = "";
    public AxamlCapabilityLevel Mode { get; init; }
    public int TotalElements { get; init; }
    public int VisualElements { get; init; }
    public int ImportedElements { get; init; }
    public int ImplicitContainers { get; init; }
    public int PartialElements { get; init; }
    public int OpaqueVisualElements => VisualElements - ImportedElements - ImplicitContainers;
    public int SkippedSubtrees { get; init; }
    public int Bindings { get; init; }
    public int Styles { get; init; }
    public int Setters { get; init; }
    public string FirstBlocker { get; init; } = "";
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
    public string EmptyProjectionMessage => VisualElements > ImplicitContainers && ImportedElements == 0
        ? $"В документе найдено {VisualElements} визуальных элементов, но ни один не вошёл в проекцию. Основная причина: {FirstBlocker}."
        : "";

    public string Format()
    {
        var text = new StringBuilder("AXAML Import Report\n");
        text.AppendLine($"Document: {DocumentPath}\nMode: {Mode}\nRoot: {RootType}\nVisual root: {VisualRoot}");
        text.AppendLine($"XML elements: {TotalElements}; live visual candidates: {VisualElements}");
        text.AppendLine($"Designer controls: {ImportedElements}; implicit Canvas: {ImplicitContainers}; partial imported: {PartialElements}; opaque/skipped visual: {OpaqueVisualElements}; skipped subtrees: {SkippedSubtrees}");
        text.AppendLine($"Bindings: {Bindings}; Styles: {Styles}; Setters: {Setters}\nFirst projection blocker: {FirstBlocker}");
        text.AppendLine("Layout uses Avalonia Measure/Arrange. Styles, templates, bindings and custom controls are preserved, not executed.");
        foreach (var line in Lines) text.AppendLine(line);
        return text.ToString();
    }

    public static string PathOf(AxamlElementSyntax element)
    {
        var siblings = element.Parent?.Children.Where(e => e.Name == element.Name).ToList();
        var suffix = siblings?.Count > 1 ? $"[{siblings.IndexOf(element) + 1}]" : "";
        return (element.Parent is null ? "" : PathOf(element.Parent) + "/") + element.Name + suffix;
    }

    public static IEnumerable<AxamlElementSyntax> Descendants(AxamlElementSyntax root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    public static bool IsPropertyElement(AxamlElementSyntax element) => element.LocalName.Contains('.');

    // Items/Content wrappers contain live candidates; templates/resources/styles do not.
    public static bool IsVisualCandidate(AxamlElementSyntax element)
    {
        if (element.Parent is null || IsPropertyElement(element)) return false;
        for (var node = element; node.Parent is not null; node = node.Parent)
        {
            if (IsPropertyElement(node) && node.LocalName.Split('.').Last() is not ("Content" or "Child" or "Children" or "Items")) return false;
            if (node.LocalName is "Styles" or "Style" or "Setter" or "Resources" or "ResourceDictionary"
                or "DataTemplate" or "ControlTemplate" or "TreeDataTemplate" or "ItemsPanelTemplate"
                or "RowDefinition" or "ColumnDefinition" or "Binding" or "MultiBinding") return false;
        }
        return true;
    }

    internal static AxamlImportStructureReport Create(string? path, AxamlSyntaxDocument syntax, AxamlSourceMap map,
        AxamlCapabilityReport capability, ICollection<AxamlRoundTripDiagnostic> diagnostics)
    {
        var elements = Descendants(syntax.Root).ToList();
        var visual = elements.Where(IsVisualCandidate).ToList();
        var imported = map.Controls.Select(r => r.Element).ToHashSet();
        if (map.CanvasElement is { } canvas) imported.Add(canvas);
        var opaque = capability.Elements.Where(e => e.Mode == AxamlElementCapabilityMode.Opaque).ToDictionary(e => e.Element);
        var blockers = elements.Where(e => opaque.ContainsKey(e) && (e.Parent is null || !opaque.ContainsKey(e.Parent))
            && Descendants(e).Any(IsVisualCandidate)).ToList();
        var lines = new List<string>();
        lines.AddRange(syntax.Root.Attributes.Where(a => a.Name == "xmlns" || a.Name.StartsWith("xmlns:")).Select(a => $"Namespace: {a.Name}={a.Value}"));
        lines.Add("Top-level property elements: " + string.Join(", ", syntax.Root.Children.Where(IsPropertyElement).Select(e => e.Name)));
        lines.AddRange(elements.GroupBy(e => e.Name).OrderByDescending(g => g.Count()).Select(g => $"XML count: {g.Key}={g.Count()}; visual={g.Count(IsVisualCandidate)}"));
        foreach (var element in elements)
        {
            var mode = imported.Contains(element) ? "Imported" : opaque.ContainsKey(element) ? "Opaque" : "Metadata";
            var reason = opaque.TryGetValue(element, out var entry) ? entry.Reason : "";
            var detail = $"path={PathOf(element)}; type={element.Name}; namespace={element.NamespaceUri}; capability={mode}; reason={reason}";
            diagnostics.Add(new("AXAML_ELEMENT_DISCOVERED", AxamlDiagnosticSeverity.Information, detail));
            diagnostics.Add(new(mode == "Imported" ? "AXAML_ELEMENT_IMPORTED" : "AXAML_ELEMENT_OPAQUE", AxamlDiagnosticSeverity.Information, detail));
            lines.Add(detail);
        }
        foreach (var blocker in blockers)
            diagnostics.Add(new("AXAML_SUBTREE_SKIPPED", AxamlDiagnosticSeverity.Information,
                $"root={PathOf(blocker)}; descendantCount={Descendants(blocker).Count() - 1}; reason={opaque[blocker].Reason}"));
        var result = new AxamlImportStructureReport
        {
            DocumentPath = path ?? "", RootType = syntax.Root.Name,
            Mode = capability.Level,
            VisualRoot = string.Join(", ", syntax.Root.Children.Where(IsVisualCandidate).Select(PathOf)),
            TotalElements = elements.Count, VisualElements = visual.Count,
            // The implicit root Canvas is counted as projected visual, but not as a designer control.
            ImportedElements = map.Controls.Count,
            ImplicitContainers = map.CanvasElement is null ? 0 : 1,
            PartialElements = map.Controls.Count(r => r.Capability.Mode == AxamlElementCapabilityMode.PartiallyEditable),
            SkippedSubtrees = blockers.Count,
            Bindings = elements.Sum(e => e.Attributes.Count(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal)
                || a.Value.StartsWith("{CompiledBinding", StringComparison.Ordinal) || a.Value.StartsWith("{ReflectionBinding", StringComparison.Ordinal)))
                + elements.Count(e => e.LocalName == "Binding"),
            Styles = elements.Count(e => e.LocalName == "Style"), Setters = elements.Count(e => e.LocalName == "Setter"),
            FirstBlocker = blockers.FirstOrDefault() is { } first ? $"{PathOf(first)}: {opaque[first].Reason}" : "none",
            Lines = lines
        };
        diagnostics.Add(new("AXAML_ROOT_DETECTED", AxamlDiagnosticSeverity.Information, $"type={result.RootType}"));
        diagnostics.Add(new("AXAML_VISUAL_ROOT_DETECTED", AxamlDiagnosticSeverity.Information, $"path={result.VisualRoot}"));
        diagnostics.Add(new("AXAML_IMPORT_SUMMARY", AxamlDiagnosticSeverity.Information,
            $"totalElements={result.TotalElements}; visualElements={result.VisualElements}; importedElements={result.ImportedElements}; partialElements={result.PartialElements}; opaqueElements={result.OpaqueVisualElements}; skippedSubtrees={result.SkippedSubtrees}; bindings={result.Bindings}; styles={result.Styles}; designerControlsCreated={map.Controls.Count}"));
        return result;
    }
}
