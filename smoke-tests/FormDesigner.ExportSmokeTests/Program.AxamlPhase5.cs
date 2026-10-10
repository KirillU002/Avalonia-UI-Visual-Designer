using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Input;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string Phase5Source(string name) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "Phase5", name + ".axaml"));
    private static void Phase5Minimal(MainWindowViewModel vm, string original, string from, string to)
    {
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == original.Replace(from, to), "Phase 5 patch changed unrelated source or was blocked.");
    }

    private static void AssertPhase5ItemsStatic(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, Phase5Source("Items"));
        var items = surface.Canvas.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.GetType() == typeof(ItemsControl));
        RequireCapability(items.Items.Count == 2 && items.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "First"), "Static ItemsControl content not rendered.");
        RequireCapability(vm.Controls.Single(c => c.Name == "First").ParentId == vm.Controls.Single(c => c.Name == "Items").Id, "Items hierarchy flattened.");
        RequireCapability(LayoutBounds(vm, "Second").Y > LayoutBounds(vm, "First").Y, "ItemsControl native stacking was lost.");
    }

    private static void AssertPhase5ItemsBinding(SmokeContext context)
    {
        var source = "<Window><ItemsControl Name=\"Items\" ItemsSource=\"{Binding Items}\" Width=\"240\" Height=\"50\"/></Window>";
        var (_, vm, surface) = Phase4Document(context, source);
        var view = surface.Canvas.GetVisualDescendants().OfType<ItemsControl>().Single();
        RequireCapability(view.Items.Count == 0 && view.DataContext is null, "User data/bindings were executed or replaced with mock items.");
        Phase4Edit(vm, "Items", "Width", "260"); Phase5Minimal(vm, source, "Width=\"240\"", "Width=\"260\"");
    }

    private static void AssertPhase5Templates(SmokeContext context)
    {
        foreach (var type in new[] { "ItemsControl", "ListBox", "TreeView" })
        {
            var template = type == "TreeView" ? "TreeDataTemplate ItemsSource=\"{Binding Children}\"" : "DataTemplate";
            var close = type == "TreeView" ? "TreeDataTemplate" : "DataTemplate";
            var source = $"<Window><{type} Name=\"Owner\" Width=\"240\" ItemsSource=\"{{Binding Items}}\"><{type}.ItemTemplate><{template}><TextBlock Text=\"{{Binding Name}}\"/></{close}></{type}.ItemTemplate></{type}></Window>";
            var (result, vm, _) = Phase4Document(context, source);
            RequireCapability(vm.Controls.Count == 1 && result.CapabilityReport.Structure!.VisualElements == 1, "Template nodes were treated as live controls.");
            Phase4Edit(vm, "Owner", "Width", "260"); Phase5Minimal(vm, source, "Width=\"240\"", "Width=\"260\"");
        }
    }

    private static void AssertPhase5ListStatic(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, Phase5Source("List"));
        var list = Phase4View<ListBox>(surface);
        RequireCapability(list.Items.Count == 3 && list.SelectedIndex == 1 && list.SelectedItem is ListBoxItem { Tag: DesignControlModel m } && m.Name == "Second", "ListBox native items/selection incorrect.");
        RequireCapability(list.GetVisualDescendants().OfType<ListBoxItem>().Any(i => i.IsSelected), "Selected ListBox item is not visually selected.");
        RequireCapability(vm.Controls.Single(c => c.Name == "Label").ParentId == vm.Controls.Single(c => c.Name == "Third").Id, "ListBoxItem content hierarchy lost.");
        Phase5Screenshot(surface, "List");
    }

    private static void AssertPhase5ListIndex(SmokeContext context)
    {
        var source = Phase5Source("List"); var (_, vm, surface) = Phase4Document(context, source);
        Phase4Edit(vm, "List", "SelectedIndex", "2");
        Phase5Minimal(vm, source, "SelectedIndex=\"1\"", "SelectedIndex=\"2\"");
        RequireCapability(Phase4View<ListBox>(surface).SelectedIndex == 2, "Inspector did not update native ListBox selection.");
    }

    private static void AssertPhase5ListBinding(SmokeContext context)
    {
        foreach (var form in new[] { "SelectedItem=\"{Binding Current}\"", "SelectingItemsControl.SelectedItem=\"{Binding Current}\"" })
        {
            var source = Phase5Source("List").Replace("SelectedIndex=\"1\"", form);
            var (_, vm, _) = Phase4Document(context, source);
            RequireCapability(CoverageEditor(vm, "List", "SelectedIndex").IsReadOnly, "SelectedIndex can override a selection Binding.");
            Phase4Edit(vm, "List", "Width", "260"); Phase5Minimal(vm, source, "Width=\"240\"", "Width=\"260\"");
        }
    }

    private static void AssertPhase5ListMode(SmokeContext context)
    {
        var source = Phase5Source("List"); var (_, vm, surface) = Phase4Document(context, source);
        Phase4Edit(vm, "List", "SelectionMode", "Multiple");
        Phase5Minimal(vm, source, "SelectionMode=\"Single\"", "SelectionMode=\"Multiple\"");
        RequireCapability(Phase4View<ListBox>(surface).SelectionMode == SelectionMode.Multiple, "SelectionMode not updated natively.");
    }

    private static void AssertPhase5TreeStatic(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, Phase5Source("Tree"));
        var tree = Phase4View<TreeView>(surface);
        RequireCapability(tree.Items.Count == 2 && tree.Items[0] is TreeViewItem { IsExpanded: true } root && root.Items[0] is TreeViewItem child && child.Items.Count == 1,
            "TreeView hierarchy is not native TreeViewItem/Items.");
        RequireCapability(vm.Controls.Single(c => c.Name == "Leaf").ParentId == vm.Controls.Single(c => c.Name == "Child").Id, "Nested TreeViewItem flattened.");
        RequireCapability(tree.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Leaf"), "Expanded native leaf not rendered.");
        RequireCapability(LayoutBounds(vm, "Leaf").Width > 0, "Native TreeViewItem bounds missing.");
        Phase5Screenshot(surface, "Tree");
    }

    private static void AssertPhase5TreeHeader(SmokeContext context)
    {
        var source = Phase5Source("Tree"); var (_, vm, surface) = Phase4Document(context, source);
        Phase4Edit(vm, "Child", "Header", "Updated"); Phase5Minimal(vm, source, "Header=\"Child\"", "Header=\"Updated\"");
        RequireCapability(surface.Canvas.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Updated"), "Tree header Inspector edit not rendered.");
    }

    private static void AssertPhase5TreeCollapsed(SmokeContext context)
    {
        var source = Phase5Source("Tree"); var (_, vm, surface) = Phase4Document(context, source);
        var count = vm.Controls.Count;
        Phase4Edit(vm, "Section", "IsExpanded", "False");
        // There are two expanded nodes; only the selected node's value span may change.
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.Edits[0].NewText == "False", "Tree expanded patch not minimal.");
        RequireCapability(vm.Controls.Count == count && LayoutBounds(vm, "Leaf").Height == 0, "Collapsed tree lost model or retained visible descendant bounds.");
        Phase4Edit(vm, "Section", "IsExpanded", "True");
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges && surface.Canvas.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Leaf"), "Re-expansion lost nested nodes.");
    }

    private static void AssertPhase5TreeBinding(SmokeContext context)
    {
        var source = "<Window><TreeView Name=\"Tree\" ItemsSource=\"{Binding Nodes}\" SelectedItem=\"{Binding Current}\" Width=\"300\" Height=\"100\"/></Window>";
        var (_, vm, surface) = Phase4Document(context, source);
        RequireCapability(Phase4View<TreeView>(surface).Items.Count == 0, "Tree binding executed.");
        Phase4Edit(vm, "Tree", "Width", "320"); Phase5Minimal(vm, source, "Width=\"300\"", "Width=\"320\"");
    }

    private static void AssertPhase5Progress(SmokeContext context)
    {
        var source = Phase5Source("Progress"); var (_, vm, surface) = Phase4Document(context, source, showHost: true);
        var view = Phase4View<ProgressBar>(surface);
        RequireCapability(view.Minimum == 0 && view.Maximum == 200 && view.Value == 80 && !view.IsIndeterminate && view.Bounds.Width == 300, "Native ProgressBar literals/layout incorrect.");
        Phase4Edit(vm, "Progress", "Value", "120.5"); Phase5Minimal(vm, source, "Value=\"80\"", "Value=\"120.5\"");
        RequireCapability(Phase4View<ProgressBar>(surface).Value == 120.5, "Numeric ProgressBar Inspector edit not rendered.");
        view = Phase4View<ProgressBar>(surface);
        var indicator = view.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Indicator");
        RequireCapability(Math.Abs(view.Percentage - 60.25) < 0.01 && Math.Abs(indicator.Bounds.Width - 180.75) < 1,
            "Native ProgressBar indicator does not render Value/range proportionally.");
        Phase5Screenshot(surface, "Progress");
    }

    private static void AssertPhase5ProgressIndeterminate(SmokeContext context)
    {
        var source = Phase5Source("Progress"); var (_, vm, surface) = Phase4Document(context, source, showHost: true);
        Phase4Edit(vm, "Progress", "IsIndeterminate", "True"); Phase5Minimal(vm, source, "IsIndeterminate=\"False\"", "IsIndeterminate=\"True\"");
        RequireCapability(Phase4View<ProgressBar>(surface).IsIndeterminate, "Indeterminate Inspector edit not rendered.");
    }

    private static void AssertPhase5ProgressBinding(SmokeContext context)
    {
        var source = Phase5Source("Progress").Replace("Value=\"80\"", "Value=\"{Binding Progress}\"");
        var (_, vm, _) = Phase4Document(context, source);
        RequireCapability(CoverageEditor(vm, "Progress", "Value").IsReadOnly && !CoverageEditor(vm, "Progress", "Width").IsReadOnly, "Binding capability is not property granular.");
        Phase4Edit(vm, "Progress", "Width", "320"); Phase5Minimal(vm, source, "Width=\"300\"", "Width=\"320\"");
    }

    private static void AssertPhase5ProgressRange(SmokeContext context)
    {
        var source = Phase5Source("Progress"); var (_, vm, surface) = Phase4Document(context, source);
        Phase4Edit(vm, "Progress", "Maximum", "300"); Phase5Minimal(vm, source, "Maximum=\"200\"", "Maximum=\"300\"");
        RequireCapability(Phase4View<ProgressBar>(surface).Maximum == 300, "Progress maximum not projected.");
        vm.MarkAxamlRoundTripSaved("Phase4.axaml", vm.CreateActiveAxamlPatch().PatchedText);
        Phase4Edit(vm, "Progress", "Orientation", "Vertical");
        RequireCapability(Phase4View<ProgressBar>(surface).Orientation == Avalonia.Layout.Orientation.Vertical && vm.CreateActiveAxamlPatch().Edits.Count == 1, "Progress Orientation not native/minimal.");
    }

    private static void AssertPhase5NoEdit(SmokeContext context)
    {
        foreach (var name in new[] { "Items", "List", "Tree", "Progress" }) RequireIdentity(new AxamlImportService().Import(Phase5Source(name)));
    }

    private static void AssertPhase5AckConflict(SmokeContext context)
    {
        var (_, vm, _) = Phase4Document(context, Phase5Source("Tree"));
        Phase4Edit(vm, "Child", "Header", "ACK one"); var first = vm.CreateActiveAxamlPatch();
        vm.MarkAxamlRoundTripSaved("Phase4.axaml", first.PatchedText);
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "ACK created phantom edits.");
        Phase4Edit(vm, "Child", "Header", "ACK two"); Phase5Minimal(vm, first.PatchedText, "Header=\"ACK one\"", "Header=\"ACK two\"");
        RequireCapability(!vm.CreateActiveAxamlPatch(first.PatchedText + " ").CanApply, "Checksum conflict was ignored.");
    }

    private static void AssertPhase5UnsafeItems(SmokeContext context)
    {
        foreach (var type in new[] { "ItemsControl", "ListBox", "TreeView", "TreeViewItem" })
        {
            var result = new AxamlImportService().Import($"<Window><{type} ItemsSource=\"{{Binding Items}}\"><TreeViewItem Header=\"Preserve\"/></{type}></Window>");
            RequireCapability(result.Document.Controls.Count == 1, "Static items replaced ItemsSource ownership."); RequireIdentity(result);
        }
        var source = "<Window><ListBox SelectedIndex=\"1\"><custom:Unknown xmlns:custom=\"using:X\"/><ListBoxItem Content=\"Known\"/></ListBox></Window>";
        var (_, _, surface) = Phase4Document(context, source);
        RequireCapability(Phase4View<ListBox>(surface).SelectedIndex == 0, "Opaque item shifted source SelectedIndex mapping.");
    }

    private static void AssertPhase5SourcePreservation(SmokeContext context)
    {
        var source = "<Window xmlns:custom=\"using:X\"><Window.Styles><!-- style KEEP --><Style Selector=\"ProgressBar\"><Setter Property=\"Opacity\" Value=\"0.9\"/></Style></Window.Styles><Window.Resources><custom:Resource x:Key=\"KEY\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"/></Window.Resources><!-- KEEP --><ProgressBar Name=\"Progress\" Value=\"80\" custom:Unknown=\"KEEP\"/></Window>";
        var (_, vm, _) = Phase4Document(context, source);
        Phase4Edit(vm, "Progress", "Value", "90"); Phase5Minimal(vm, source, "Value=\"80\"", "Value=\"90\"");
    }

    private static void AssertPhase5RealMainWindow(SmokeContext context)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var (result, vm, surface) = Phase4Document(context, File.ReadAllText(path), path);
        var report = result.CapabilityReport.Structure!;
        RequireCapability(report.ImportedElements == 1470 && report.VisualElements == 1473 && report.OpaqueVisualElements == 3 && report.SkippedSubtrees == 3, "Real MainWindow coverage/classification differs.");
        var types = new[] { "ItemsControl", "ListBox", "TreeView", "ProgressBar" };
        var references = result.RoundTripDocument.SourceMap.Controls.Where(r => types.Contains(r.Element.LocalName)).ToArray();
        var rendered = new HashSet<string>();
        var measured = new HashSet<string>();
        foreach (var expander in result.RoundTripDocument.SourceMap.Controls.Where(r => r.Element.LocalName == "Expander"))
            AxamlControlMetadata.Find("Expander")!.LiteralProperties.Single(p => p.Key == "IsExpanded").Write(vm.Controls.Single(c => c.Id == expander.ControlId), "True");
        foreach (var reference in references)
        {
            ActivateAxamlAncestors(vm, result.RoundTripDocument, reference.ControlId);
            typeof(MainWindow).GetMethod("RenderDesigner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface.GetLogicalAncestors().OfType<MainWindow>().Single(), null);
            AxamlLayoutProjection.PrepareOffscreen(surface.Canvas);
            surface.Canvas.Measure(new Size(1600, 920)); surface.Canvas.Arrange(new Rect(0, 0, 1600, 920));
            foreach (var view in surface.Canvas.GetVisualDescendants().OfType<Control>().Where(v => v is ItemsControl or ProgressBar))
                if (view.Tag is DesignControlModel model && references.Any(r => r.ControlId == model.Id))
                {
                    rendered.Add(model.Id);
                    if (view.Bounds.Width > 0 && view.Bounds.Height > 0) measured.Add(model.Id);
                }
        }
        RequireCapability(rendered.Count == references.Length, "Imported owner is absent from real shared surface.");
        foreach (var type in types)
            RequireCapability(references.Any(r => r.Element.LocalName == type && measured.Contains(r.ControlId)), "New type has no measured native view on real shared surface: " + type);
        var editable = result.RoundTripDocument.SourceMap.Controls.Count(r => r.Capability.Properties.Any(p => p.Mode == AxamlPropertyCapabilityMode.Editable));
        Console.WriteLine($"PHASE5_COVERAGE imported={report.ImportedElements}; total={report.VisualElements}; opaque={report.OpaqueVisualElements}; skipped={report.SkippedSubtrees}; partial={report.PartialElements}; editable={editable}; nativePresent={rendered.Count}/{references.Length}; nativeBounds={measured.Count}/{references.Length}");
        foreach (var type in types) Console.WriteLine($"PHASE5_TYPE {type}: imported={references.Count(r => r.Element.LocalName == type)}; nativePresent={references.Count(r => r.Element.LocalName == type && rendered.Contains(r.ControlId))}; nativeBounds={references.Count(r => r.Element.LocalName == type && measured.Contains(r.ControlId))}");
        foreach (var group in report.Blockers.GroupBy(b => b.Type)) Console.WriteLine($"PHASE5_REMAINING {group.Key}: instances={group.Count()}; blocked={group.Sum(b => b.BlockedVisualDescendants)}");
        var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "axaml-phase5"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "MainWindow-report.txt"), report.Format()); Phase5Screenshot(surface, "MainWindow");
    }

    private static void ActivateAxamlAncestors(MainWindowViewModel vm, AxamlRoundTripDocument document, string id)
    {
        for (var current = vm.Controls.Single(c => c.Id == id); !string.IsNullOrEmpty(current.ParentId); current = vm.Controls.Single(c => c.Id == current.ParentId))
        {
            var parent = document.SourceMap.ByControlId[current.ParentId];
            if (parent.Element.LocalName != "TabControl") continue;
            var siblings = vm.Controls.Where(c => c.ParentId == current.ParentId).OrderBy(c => document.SourceMap.ByControlId[c.Id].Element.ElementSpan.Start).ToArray();
            AxamlControlMetadata.Find("TabControl")!.LiteralProperties[0].Write(vm.Controls.Single(c => c.Id == current.ParentId), Array.IndexOf(siblings, current).ToString());
        }
    }

    private static void AssertPhase5Ipc(SmokeContext context)
    {
        var source = "<Window xmlns:custom=\"using:X\"><Grid><ItemsControl Name=\"Items\" Width=\"240\" custom:Unknown=\"KEEP\"><!-- KEEP --><TextBlock Text=\"First\"/></ItemsControl><ListBox Name=\"List\" SelectedIndex=\"0\"><ListBoxItem Content=\"One\"/><ListBoxItem Content=\"Two\"/></ListBox><TreeView Name=\"Tree\"><TreeViewItem Name=\"Section\" Header=\"Old\" IsExpanded=\"True\"/></TreeView><ProgressBar Name=\"Progress\" Value=\"20\"/></Grid></Window>";
        AssertAxamlIpcEdits(context, source, new[] { ("Items", "Width", "260"), ("List", "SelectedIndex", "1"), ("Section", "Header", "IPC header"), ("Progress", "Value", "40"), ("Section", "IsExpanded", "False") });
    }

    private static void AssertPhase5PointerSelection(SmokeContext context)
    {
        foreach (var fixture in new[] { "Tree", "List", "Items", "Progress" })
        {
            var (_, vm, surface) = Phase4Document(context, Phase5Source(fixture));
            var name = fixture == "Tree" ? "Leaf" : fixture == "List" ? "Second" : fixture;
            var target = surface.Canvas.GetVisualDescendants().OfType<Border>().First(b => b.Tag is DesignControlModel m && m.Name == name);
            using var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
            var press = new PointerPressedEventArgs(target, pointer, target, new Point(1, 1), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1);
            target.RaiseEvent(press);
            RequireCapability(press.Handled && vm.SelectedControl?.Name == name, "Pointer selected owning control instead of static item: " + name);
            var key = fixture == "Tree" ? "Header" : fixture == "List" ? "Content" : fixture == "Progress" ? "Value" : "Width";
            RequireCapability(!vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == key).IsReadOnly, "Selected item Inspector missing or read-only.");
        }
    }

    private static void AssertPhase5ItemGeometry(SmokeContext context)
    {
        var source = Phase5Source("Tree").Replace("Header=\"Child\"", "Header=\"Child\" Margin=\"4\" Width=\"220\"");
        var (_, vm, surface) = Phase4Document(context, source);
        Phase4Edit(vm, "Child", "Margin", "8"); Phase5Minimal(vm, source, "Margin=\"4\"", "Margin=\"8\"");
        var item = surface.Canvas.GetVisualDescendants().OfType<TreeViewItem>().Single(v => v.Tag is DesignControlModel m && m.Name == "Child");
        RequireCapability(item.Margin.Left == 8 && item.Width == 220, "TreeViewItem native geometry differs from source/Inspector.");
    }

    private static void AssertPhase5QualifiedBindings(SmokeContext context)
    {
        foreach (var (type, key) in new[] { ("ProgressBar", "Value"), ("TreeViewItem", "Header"), ("ListBox", "SelectedIndex") })
        {
            var source = $"<Window><{type} Name=\"Owner\" {type}.{key}=\"{{Binding Preserved}}\" Width=\"240\"/></Window>";
            var (_, vm, _) = Phase4Document(context, source);
            RequireCapability(CoverageEditor(vm, "Owner", key).IsReadOnly, "Qualified binding can be overwritten: " + type + "." + key);
            Phase4Edit(vm, "Owner", "Width", "260"); Phase5Minimal(vm, source, "Width=\"240\"", "Width=\"260\"");
        }
        var inherited = "<Window><ProgressBar Name=\"Progress\" RangeBase.Value=\"{Binding Progress}\" Width=\"240\"/></Window>";
        var (_, inheritedVm, _) = Phase4Document(context, inherited);
        RequireCapability(CoverageEditor(inheritedVm, "Progress", "Value").IsReadOnly, "Inherited qualified value can be shadowed.");
        Phase4Edit(inheritedVm, "Progress", "Width", "260"); Phase5Minimal(inheritedVm, inherited, "Width=\"240\"", "Width=\"260\"");
    }

    private static void AssertPhase5ItemsWrappers(SmokeContext context)
    {
        var source = "<Window><ListBox Name=\"List\" SelectedIndex=\"1\" Width=\"240\" Height=\"140\"><ItemsControl.Items><ListBoxItem Content=\"One\"/><ListBoxItem Content=\"Two\"/></ItemsControl.Items></ListBox></Window>";
        var (result, vm, surface) = Phase4Document(context, source);
        RequireCapability(result.CapabilityReport.Structure!.VisualElements == 3 && Phase4View<ListBox>(surface).SelectedIndex == 1, "Inherited Items wrapper is not traversed/classified correctly.");
        var children = vm.Controls.Where(c => c.ParentId == vm.Controls.Single(m => m.Name == "List").Id).ToArray();
        children[0].StackOrder = 2;
        children[1].StackOrder = 0;
        RequireCapability(!vm.CreateActiveAxamlPatch().CanApply, "Items order changed without a source-order patch.");
    }

    private static void Phase5Screenshot(DesignerSurface surface, string name)
    {
        var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "axaml-phase5"); Directory.CreateDirectory(output);
        var size = name == "MainWindow" ? new PixelSize(1600, 920) : new PixelSize(600, 400);
        var background = surface.Canvas.Background;
        try
        {
            surface.Canvas.Background = Avalonia.Media.Brushes.White;
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size); bitmap.Render(surface.Canvas); bitmap.Save(Path.Combine(output, name + ".png"));
        }
        finally { surface.Canvas.Background = background; }
    }
}
