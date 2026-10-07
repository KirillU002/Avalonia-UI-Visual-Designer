using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public sealed record AxamlBlockerImpact(string Type, string Namespace, string Path, string Category,
    int DirectChildren, int BlockedVisualDescendants, int KnownVisualDescendants, int ReachableKnownDescendants, string Reason)
{
    public static IReadOnlyList<AxamlBlockerImpact> Analyze(AxamlSyntaxDocument syntax, AxamlCapabilityReport capability)
    {
        var opaque = capability.Elements.Where(c => c.Mode == AxamlElementCapabilityMode.Opaque).ToDictionary(c => c.Element);
        return AxamlImportStructureReport.Descendants(syntax.Root)
            .Where(e => opaque.ContainsKey(e) && (e.Parent is null || !opaque.ContainsKey(e.Parent))
                && AxamlImportStructureReport.Descendants(e).Any(AxamlImportStructureReport.IsVisualCandidate))
            .Select(e => new AxamlBlockerImpact(e.Name, e.NamespaceUri, AxamlImportStructureReport.PathOf(e), CategoryOf(e),
                e.Children.Count, AxamlImportStructureReport.Descendants(e).Skip(1).Count(AxamlImportStructureReport.IsVisualCandidate),
                AxamlImportStructureReport.Descendants(e).Skip(1).Count(n => AxamlImportStructureReport.IsVisualCandidate(n) && IsKnown(n)),
                e.Children.Sum(Reachable), opaque[e].Reason)).ToArray();
    }

    private static bool IsKnown(AxamlElementSyntax e) => e.NamespaceUri is "" or "https://github.com/avaloniaui"
        && AxamlControlMetadata.Find(e.LocalName) is not null;

    // Estimate excludes the next unknown boundary. It is not permission to flatten a parent.
    private static int Reachable(AxamlElementSyntax e)
    {
        if (!AxamlImportStructureReport.IsVisualCandidate(e) || !IsKnown(e)) return 0;
        var metadata = AxamlControlMetadata.Find(e.LocalName)!;
        if (metadata.ContainerKind == AxamlContainerKind.None) return e.Children.Any(c => AxamlImportStructureReport.IsVisualCandidate(c)) ? 0 : 1;
        return 1 + e.Children.Sum(c => metadata.IsChildProperty(c) ? c.Children.Sum(Reachable) : Reachable(c));
    }

    public static string CategoryOf(AxamlElementSyntax e)
    {
        if (AxamlImportStructureReport.IsPropertyElement(e)) return "PropertyElement";
        if (e.NamespaceUri is not ("" or "https://github.com/avaloniaui")) return "CustomControl";
        // Only the already loaded trusted Avalonia assembly is inspected, never a project assembly.
        var type = typeof(Control).Assembly.GetType("Avalonia.Controls." + e.LocalName);
        if (type is null) return "Unknown";
        if (typeof(SelectingItemsControl).IsAssignableFrom(type)) return "Selector/ItemsControl";
        if (typeof(ItemsControl).IsAssignableFrom(type)) return "ItemsControl";
        if (typeof(HeaderedContentControl).IsAssignableFrom(type)) return "HeaderedContentControl";
        if (typeof(ContentControl).IsAssignableFrom(type)) return "ContentControl";
        if (typeof(Panel).IsAssignableFrom(type)) return "Panel";
        if (typeof(Decorator).IsAssignableFrom(type)) return "Decorator";
        return typeof(Control).IsAssignableFrom(type) ? "Control" : "NonVisual";
    }
}
