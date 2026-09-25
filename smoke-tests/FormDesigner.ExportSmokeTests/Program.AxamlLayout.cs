using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static (AxamlImportResult Result, MainWindowViewModel Vm) LayoutFixture(string name)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "Layout", name + ".axaml");
        var result = new AxamlImportService().Import(File.ReadAllText(path), path);
        RequireIdentity(result);
        var vm = CreateViewModel("Layout" + name);
        vm.LoadAxamlImportedDocument(result, path);
        RequireCapability(vm.CreateActiveAxamlPatch().CanApply && !vm.CreateActiveAxamlPatch().HasChanges, "VM load mutated layout source: " + name);
        vm.AxamlLayoutBounds = AxamlLayoutProjection.Arrange(result.RoundTripDocument, vm.Controls, 600, 400);
        return (result, vm);
    }

    private static Rect LayoutBounds(MainWindowViewModel vm, string name) => vm.AxamlLayoutBounds[vm.Controls.Single(c => c.Name == name).Id];
    private static void Near(double actual, double expected, string reason) => RequireCapability(Math.Abs(actual - expected) < 0.1, $"{reason}: expected={expected}; actual={actual}");

    private static void AssertLayoutGridButton(SmokeContext context)
    {
        var (result, vm) = LayoutFixture("GridButton");
        RequireCapability(vm.Controls.Count == 2 && vm.Controls.Single(c => c.Type == "Button").ParentId == vm.Controls.Single(c => c.Type == "Grid").Id, "Grid hierarchy missing.");
        var action = vm.Controls.Single(c => c.Name == "Action");
        RequireCapability(!vm.CanMoveAxamlControl(action) && vm.CanResizeAxamlControl(action), "Grid child cannot use Canvas coordinates.");
        Near(LayoutBounds(vm, "Action").Width, 100, "Grid child Width");
        action.Text = "New";
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Content=\"Old\"", "Content=\"New\""), "Grid edit must be a single attribute edit.");
        vm.MarkAxamlRoundTripSaved("layout.axaml", patch.PatchedText);
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Unnamed Grid identity changed after ACK.");
        action.X += 100;
        RequireCapability(!vm.CreateActiveAxamlPatch().CanApply, "Non-Canvas coordinate mutation must be rejected.");
    }

    private static void AssertLayoutGridDefinitions(SmokeContext context)
    {
        var (result, vm) = LayoutFixture("GridDefinitions");
        var grid = vm.Controls.Single(c => c.Type == "Grid");
        RequireCapability(grid.GridRowDefinitions == "Auto,*" && grid.GridColumnDefinitions == "100,2*,*", "Definition syntax missing.");
        var action = LayoutBounds(vm, "Action");
        Near(action.X, 100, "Grid column"); Near(action.Y, 30, "Auto row"); Near(action.Width, 500, "Column span"); Near(action.Height, 370, "Star row");
        vm.Controls.Single(c => c.Name == "Action").GridColumnSpan = 1;
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && !patch.PatchedText.Contains("Canvas.Left"), "Grid attached property patch lost semantics.");
    }

    private static void AssertLayoutStackPanel(SmokeContext context)
    {
        var (_, vm) = LayoutFixture("GridStackPanel");
        Near(LayoutBounds(vm, "First").Y, 0, "Stack first"); Near(LayoutBounds(vm, "Second").Y, 30, "Stack spacing");
        var stack = vm.Controls.Single(c => c.Type == "StackPanel");
        stack.LayoutSpacing = 18;
        RequireCapability(vm.CreateActiveAxamlPatch().Edits.Count == 1 && vm.CreateActiveAxamlPatch().PatchedText.Contains("Spacing=\"18\""), "Stack property patch incorrect.");
        vm.Controls.Single(c => c.Name == "First").StackOrder = 100;
        RequireCapability(!vm.CreateActiveAxamlPatch().CanApply, "Unsupported StackPanel reordering must be blocked.");
        var auto = new AxamlImportService().Import("<Window><StackPanel><Button Name=\"Auto\" Content=\"Measure me\"/><TextBox Name=\"AutoInput\" Text=\"Input\"/></StackPanel></Window>");
        vm.LoadAxamlImportedDocument(auto);
        vm.AxamlLayoutBounds = AxamlLayoutProjection.Arrange(auto.RoundTripDocument, vm.Controls, 600, 400);
        RequireCapability(LayoutBounds(vm, "Auto").Height > 10 && LayoutBounds(vm, "AutoInput").Height > 10,
            "Auto-sized native controls must have measured content, not zero-height boxes.");
    }

    private static void AssertLayoutDockPanel(SmokeContext context)
    {
        var (result, vm) = LayoutFixture("DockPanel");
        Near(LayoutBounds(vm, "First").Width, 100, "Dock left"); Near(LayoutBounds(vm, "Second").X, 100, "Dock fill position"); Near(LayoutBounds(vm, "Second").Width, 480, "Dock fill size");
        vm.Controls.Single(c => c.Name == "Second").Text = "Updated";
        RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == result.RoundTripDocument.OriginalText.Replace("Text=\"Fill\"", "Text=\"Updated\""), "Dock semantics changed during text edit.");
        var noFill = new AxamlImportService().Import("<Window><DockPanel LastChildFill=\"FALSE\"><Button Name=\"Top\" DockPanel.Dock=\"Top\" Height=\"30\"/><TextBox Name=\"Left\" DockPanel.Dock=\"Left\" Width=\"120\"/></DockPanel></Window>");
        vm.LoadAxamlImportedDocument(noFill);
        vm.AxamlLayoutBounds = AxamlLayoutProjection.Arrange(noFill.RoundTripDocument, vm.Controls, 600, 400);
        Near(LayoutBounds(vm, "Left").Y, 30, "Dock Top"); Near(LayoutBounds(vm, "Left").Width, 120, "LastChildFill false");
    }

    private static void AssertLayoutPreservedSyntax(SmokeContext context)
    {
        foreach (var name in new[] { "Styles", "Bindings", "UnknownSibling", "Preservation" })
        {
            var (result, vm) = LayoutFixture(name);
            RequireCapability(!result.CapabilityReport.DocumentReadOnly && vm.Controls.Count >= 2, "Opaque syntax blocked known layout: " + name);
            vm.Controls.Single(c => c.Name == "Action").Text = "New";
            var patch = vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Content=\"Old\"", "Content=\"New\""), "Source preservation failed: " + name);
        }
    }

    private static void AssertLayoutUnknownParent(SmokeContext context)
    {
        var empty = new AxamlImportService().Import("<Window><Canvas><!-- empty supported Canvas --></Canvas></Window>");
        RequireCapability(empty.CapabilityReport.Structure!.EmptyProjectionMessage.Length == 0, "Empty supported Canvas is not a projection failure.");
        var (result, vm) = LayoutFixture("UnknownParent");
        RequireCapability(vm.Controls.Count == 0 && result.CapabilityReport.Structure!.VisualElements == 2
            && result.CapabilityReport.Structure.FirstBlocker.Contains("custom:Container")
            && result.CapabilityReport.Structure.EmptyProjectionMessage.Length > 0, "Opaque subtree needs an explained empty projection.");
        var surface = CreateMainWindowDesignerSurface(new SmokeContext(context.Scenario, vm, context.ProjectPath, context.Xaml, context.CSharp, context.GeneratedFiles, context.ChecklistText, context.DiagnosticsText));
        RequireCapability(surface.Canvas.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Подробнее")), "Empty surface report action missing.");
    }

    private static void AssertLayoutDeepNested(SmokeContext context)
    {
        var (result, vm) = LayoutFixture("DeepNested");
        RequireCapability(vm.Controls.Count == 7 && vm.AxamlLayoutBounds.Count == 7, "Nested tree incomplete.");
        var action = vm.Controls.Single(c => c.Name == "Action");
        RequireCapability(vm.CanMoveAxamlControl(action), "Nested Canvas must retain local coordinate editing.");
        action.X = 70; action.Y = 80;
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 2 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Canvas.Left=\"20\"", "Canvas.Left=\"70\"").Replace("Canvas.Top=\"25\"", "Canvas.Top=\"80\""), "Nested Canvas patch changed ancestors.");
    }

    private static void AssertLayoutDeletionProtection(SmokeContext context)
    {
        var (result, _) = LayoutFixture("UnknownSibling");
        result.Document.Controls.Clear();
        RequireCapability(!new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document).CanApply, "Deleting parent must not destroy preserved subtree.");
    }

    private static void AssertLayoutRealMainWindow(SmokeContext context)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var result = new AxamlImportService().Import(File.ReadAllText(path), path);
        var report = result.CapabilityReport.Structure!;
        RequireIdentity(result);
        RequireCapability(report.VisualRoot == "Window/Grid" && report.ImportedElements > 0 && report.VisualElements > report.ImportedElements,
            "Real source must have a partial, nonempty projection.");
        RequireCapability(result.RoundTripDocument.SourceMap.Controls.All(r => !AxamlImportStructureReport.IsPropertyElement(r.Element)), "Property element entered projection.");
        var vm = CreateViewModel("RealLayout"); vm.LoadAxamlImportedDocument(result, path);
        var surface = CreateMainWindowDesignerSurface(new SmokeContext(context.Scenario, vm, context.ProjectPath, context.Xaml, context.CSharp, context.GeneratedFiles, context.ChecklistText, context.DiagnosticsText));
        RequireCapability(vm.AxamlLayoutBounds.Count == vm.Controls.Count && vm.AxamlLayoutBounds.Values.Any(r => r.Width > 0 && r.Height > 0), "Real layout has no measured controls.");
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Rendering mutated real source.");
        var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "axaml-layout");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "MainWindow-import-report.txt"), report.Format());
        surface.Canvas.Measure(new Size(1600, 920));
        surface.Canvas.Arrange(new Rect(0, 0, 1600, 920));
        using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1600, 920)))
        {
            bitmap.Render(surface.Canvas);
            bitmap.Save(Path.Combine(output, "MainWindow-projection.png"));
        }
        Console.WriteLine($"REAL_LAYOUT XML={report.TotalElements}; visual={report.VisualElements}; imported={report.ImportedElements}; partial={report.PartialElements}; opaque={report.OpaqueVisualElements}; skipped={report.SkippedSubtrees}; bindings={report.Bindings}; styles={report.Styles}; firstBlocker={report.FirstBlocker}");
    }

    private static void AssertLayoutRebaseUnnamedSiblings(SmokeContext context)
    {
        const string source = "<Window><Grid><Button Content=\"Remove\"/><Button Content=\"Keep\"/></Grid></Window>";
        var result = new AxamlImportService().Import(source);
        var vm = CreateViewModel("UnnamedRebase"); vm.LoadAxamlImportedDocument(result);
        vm.Controls.Remove(vm.Controls.Single(c => c.Text == "Remove"));
        var keep = vm.Controls.Single(c => c.Text == "Keep");
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply, "Nested leaf deletion failed.");
        vm.MarkAxamlRoundTripSaved("rebase.axaml", patch.PatchedText);
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Unnamed sibling source identity changed after deletion ACK.");
        keep.Text = "After ACK";
        var next = vm.CreateActiveAxamlPatch();
        RequireCapability(next.CanApply && next.Edits.Count == 1 && next.PatchedText.Contains("After ACK"), "Second patch lost unnamed sibling identity.");
    }

    private static void AssertLayoutSimpleGreenBaseline(SmokeContext context)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Samples", "VisualStudioPoC", "SimpleAvaloniaApp", "MainWindow.axaml");
        var source = File.ReadAllText(path);
        var result = new AxamlImportService().Import(source, path);
        var vm = CreateViewModel("SimpleGreenBaseline"); vm.LoadAxamlImportedDocument(result, path);
        var added = vm.TryCreateControlFromToolboxDrop("Button", 240, 280, null, true, vm.ActiveDocumentId)
            ?? throw new InvalidOperationException("Canvas toolbox insertion regressed.");
        added.Text = "Added"; added.Width = 180; added.Height = 42; added.X = 260; added.Y = 290;
        vm.Controls.Single(c => c.Name == "Button1").Text = "Changed";
        var patch = vm.CreateActiveAxamlPatch(source);
        RequireCapability(patch.CanApply && patch.PatchedText.Contains("<!-- keep me -->") && patch.PatchedText.Contains("keep-me"), "Canvas add/edit lost preserved syntax.");
        vm.MarkAxamlRoundTripSaved(path, patch.PatchedText);
        var acknowledged = vm.CreateActiveAxamlPatch();
        RequireCapability(acknowledged.CanApply && !acknowledged.HasChanges, "Canvas ACK created an invalid or phantom patch: "
            + string.Join("; ", acknowledged.Diagnostics.Select(d => d.Details))
            + "; edits=" + string.Join("; ", acknowledged.Edits.Select(e => $"{e.Start}:{e.Length}:{e.NewText}"))
            + "; orders=" + string.Join(", ", vm.Controls.Select(c => c.Name + ":" + c.StackOrder)));
        added.X = 300;
        RequireCapability(vm.CreateActiveAxamlPatch().CanApply && vm.CreateActiveAxamlPatch().Edits.Count == 1, "Second edit after insertion ACK failed.");
        RequireCapability(vm.TryCreateControlFromToolboxDrop("Group", 0, 0, null, true, vm.ActiveDocumentId) is null,
            "Source-only Group alias must not be serialized as an Avalonia Group control.");
    }
}
