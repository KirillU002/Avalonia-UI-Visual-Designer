using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using AvaloniaDesigner.Host.Protocol;
using AvaloniaDesigner.VsHost;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static (AxamlImportResult Result, MainWindowViewModel Vm) CoverageFixture(string name)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "Coverage", name + ".axaml");
        var result = new AxamlImportService().Import(File.ReadAllText(path), path);
        RequireIdentity(result);
        var vm = CreateViewModel("Coverage" + name); vm.LoadAxamlImportedDocument(result, path);
        RequireCapability(vm.CreateActiveAxamlPatch().CanApply && !vm.CreateActiveAxamlPatch().HasChanges, "Import mutated source.");
        vm.AxamlLayoutBounds = AxamlLayoutProjection.Arrange(result.RoundTripDocument, vm.Controls, 600, 400);
        return (result, vm);
    }

    private static PropertyGridRowViewModel CoverageEditor(MainWindowViewModel vm, string name, string key)
    {
        vm.SelectSingleControl(vm.Controls.Single(c => c.Name == name));
        return vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == key);
    }

    private static void AssertCoverageTabs(SmokeContext context)
    {
        var (result, vm) = CoverageFixture("Tabs");
        RequireCapability(vm.Controls.Count == 7, "All pages and their hierarchy must be imported.");
        RequireCapability(LayoutBounds(vm, "First").Height > 0 && LayoutBounds(vm, "Second").Height == 0, "Only selected page is projected.");
        var header = CoverageEditor(vm, "FirstPage", "Header");
        RequireCapability(!header.IsReadOnly && header.Editor == PropertyGridEditorKind.Text, "Header must have a real text editor.");
        header.Value = "New";
        header.CommitValue();
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Header=\"Old\"", "Header=\"New\""), "Header patch changed preserved source.");
        vm.MarkAxamlRoundTripSaved("tabs.axaml", patch.PatchedText);
        RequireCapability(vm.CreateActiveAxamlPatch().CanApply && !vm.CreateActiveAxamlPatch().HasChanges, "Header ACK changed identities.");
        var selected = CoverageEditor(vm, "Tabs", "SelectedIndex");
        RequireCapability(selected.Editor == PropertyGridEditorKind.Number && !selected.IsReadOnly, "SelectedIndex needs a numeric editor.");
        selected.Value = "1";
        selected.CommitValue();
        var next = vm.CreateActiveAxamlPatch();
        RequireCapability(next.CanApply && next.Edits.Count == 1, "SelectedIndex must be a single source edit.");
        vm.AxamlLayoutBounds = AxamlLayoutProjection.Arrange(vm.ActiveAxamlSourceDocument!, vm.Controls, 600, 400);
        RequireCapability(LayoutBounds(vm, "Second").Height > 0 && LayoutBounds(vm, "First").Height == 0, "Second page was lost or not projected.");
    }

    private static void AssertCoverageContent(SmokeContext context)
    {
        var (result, vm) = CoverageFixture("Content");
        RequireCapability(vm.Controls.Count == 5, "Content wrappers were counted as controls or traversal stopped.");
        var owner = vm.Controls.Single(c => c.Name == "Owner");
        RequireCapability(vm.CanHostChildren(owner) && !vm.CanEditAxamlProperty(owner, "Text"), "Visual Content must not be replaced by a text attribute.");
        RequireCapability(LayoutBounds(vm, "Label").Width > 0, "Content child has no native bounds: " + string.Join("; ", vm.Controls.Select(c => c.Name + "=" + vm.AxamlLayoutBounds[c.Id])));
        RequireCapability(LayoutBounds(vm, "Input").Height >= 600, "Scroll content was constrained to the viewport instead of native scroll measurement.");
        vm.Controls.Single(c => c.Name == "Label").Text = "New";
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Text=\"Old\"", "Text=\"New\""), "Content source semantics changed.");
        var scroll = CoverageEditor(vm, "Scroll", "VerticalScrollBarVisibility");
        RequireCapability(scroll.Editor == PropertyGridEditorKind.Enum && !scroll.IsReadOnly, "Scroll visibility requires native enum options.");
        scroll.Value = "Hidden";
        scroll.CommitValue();
        RequireCapability(vm.CreateActiveAxamlPatch().CanApply && vm.CreateActiveAxamlPatch().Edits.Count == 2, "Scroll enum did not create minimal patch.");
    }

    private static void AssertCoverageWrap(SmokeContext context)
    {
        var (result, vm) = CoverageFixture("Wrap");
        Near(LayoutBounds(vm, "Two").X, 100, "Wrap second item"); Near(LayoutBounds(vm, "Three").Y, 30, "Wrap next line");
        var orientation = CoverageEditor(vm, "Wrap", "Orientation"); orientation.Value = "Vertical"; orientation.CommitValue();
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Orientation=\"Horizontal\"", "Orientation=\"Vertical\""), "Wrap patch changed source semantics: " + string.Join("; ", patch.Diagnostics.Select(d => d.Details)) + "; " + patch.PatchedText);
    }

    private static void AssertCoveragePreserved(SmokeContext context)
    {
        var (result, vm) = CoverageFixture("Preserved");
        RequireCapability(vm.Controls.Count == 4 && !vm.Controls.Any(c => c.Text == "Not a live control" || c.Text == "Opaque child"), "Templates or unknown ancestors entered projection.");
        RequireCapability(CoverageEditor(vm, "Tabs", "SelectedIndex").IsReadOnly && CoverageEditor(vm, "Page", "Header").IsReadOnly, "Binding/resource expressions must stay read-only.");
        vm.Controls.Single(c => c.Name == "Editable").Text = "New";
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Content=\"Old\"", "Content=\"New\""), "Binding/styles/resources/unknown syntax was modified.");
        RequireCapability(!vm.CreateActiveAxamlPatch(result.RoundTripDocument.OriginalText + " ").CanApply, "Conflict protection was weakened.");
    }

    private static void AssertCoverageUnsafeContent(SmokeContext context)
    {
        foreach (var source in new[]
        {
            "<Window><ScrollViewer><Button/><TextBox/></ScrollViewer></Window>",
            "<Window><ScrollViewer Content=\"literal\"><Button/></ScrollViewer></Window>",
            "<Window><ScrollViewer><ScrollViewer.Content><Button/></ScrollViewer.Content><TextBox/></ScrollViewer></Window>"
        })
        {
            var result = new AxamlImportService().Import(source); RequireIdentity(result);
            RequireCapability(result.Document.Controls.Count == 0, "Ambiguous content must remain opaque.");
        }
        var bound = new AxamlImportService().Import("<Window><TabControl ItemsSource=\"{Binding Items}\"><TabItem Header=\"Opaque\"><Button/></TabItem></TabControl></Window>");
        RequireIdentity(bound);
        RequireCapability(bound.Document.Controls.Count == 1, "Do not evaluate ItemsSource or combine it with static item projection.");
    }

    private static void AssertCoverageRealMainWindow(SmokeContext context)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var source = File.ReadAllText(path);
        var result = new AxamlImportService().Import(source, path); RequireIdentity(result);
        var vm = CreateViewModel("CoverageReal"); vm.LoadAxamlImportedDocument(result, path);
        var surface = CreateMainWindowDesignerSurface(new SmokeContext(context.Scenario, vm, context.ProjectPath, context.Xaml, context.CSharp, context.GeneratedFiles, context.ChecklistText, context.DiagnosticsText));
        var report = result.CapabilityReport.Structure!;
        RequireCapability(report.ImportedElements > 900 && report.VisualElements == 1473 && !result.CapabilityReport.DocumentReadOnly, "Real coverage did not increase safely.");
        RequireCapability(vm.AxamlLayoutBounds.Values.Any(b => b.Width > 0 && b.Height > 0), "Real projection became empty.");
        var page = result.RoundTripDocument.SourceMap.Controls.First(r => r.Element.LocalName == "TabItem" && r.Capability.CanEditProperty("Header"));
        var model = vm.Controls.Single(c => c.Id == page.ControlId);
        vm.SelectSingleControl(model);
        var editor = vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "Header");
        editor.Value = "Coverage test";
        editor.CommitValue();
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1, "Real Inspector Header edit was not minimal.");
        var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "axaml-phase3");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "MainWindow-after.txt"), report.Format());
        surface.Canvas.Measure(new Size(1600, 920)); surface.Canvas.Arrange(new Rect(0, 0, 1600, 920));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1600, 920));
        bitmap.Render(surface.Canvas); bitmap.Save(Path.Combine(output, "MainWindow-after.png"));
        Console.WriteLine($"COVERAGE imported={report.ImportedElements}; total={report.VisualElements}; opaque={report.OpaqueVisualElements}; skipped={report.SkippedSubtrees}; partial={report.PartialElements}");
        foreach (var group in report.Blockers.GroupBy(b => b.Type).OrderByDescending(g => g.Sum(b => b.BlockedVisualDescendants)).Take(10))
            Console.WriteLine($"REMAINING {group.Key}: instances={group.Count()}; blocked={group.Sum(b => b.BlockedVisualDescendants)}; known={group.Sum(b => b.KnownVisualDescendants)}");
    }

    private static void AssertCoverageIpc(SmokeContext context)
    {
        var vm = context.ViewModel;
        var window = new VsHostWindow(new TestDesignerHostServices()) { DataContext = vm }; window.Show();
        var pipe = $"{DesignerHostProtocol.PipePrefix}.coverage.{Guid.NewGuid():N}";
        using var bridge = new VsHostBridge(vm, window, pipe); bridge.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var task = Task.Run(async () =>
        {
            using var client = NamedPipeProtocolConnection.CreateClient(pipe); await client.ConnectAsync(cancellation.Token);
            Console.WriteLine("COVERAGE_IPC connected");
            using var connection = new NamedPipeProtocolConnection(client);
            var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "Coverage", "Tabs.axaml"));
            await connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "open", "tabs", CreateVsHostOpenDocumentPayload(source, 1), cancellation.Token);
            RequireCapability((await connection.ReceiveAsync(cancellation.Token))!.MessageType == DesignerHostMessageTypes.DocumentOpened, "IPC open failed.");
            Console.WriteLine("COVERAGE_IPC opened");
            await Dispatcher.UIThread.InvokeAsync(() => { var row = CoverageEditor(vm, "FirstPage", "Header"); row.Value = "IPC header"; row.CommitValue(); });
            Console.WriteLine("COVERAGE_IPC edited");
            var apply = Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);
            var envelope = (await connection.ReceiveAsync(cancellation.Token))!;
            await apply;
            Console.WriteLine("COVERAGE_IPC patch received");
            RequireCapability(envelope.MessageType == DesignerHostMessageTypes.ApplyDesignerPatch, "IPC patch missing.");
            var payload = connection.GetPayload<ApplyDesignerPatchPayload>(envelope)!;
            RequireCapability(payload.Edits.Count == 1 && payload.ExpectedVersion == 1 && payload.ExpectedChecksum == DesignerHostProtocol.ComputeChecksum(source), "IPC patch lost version/minimality.");
            var text = AxamlPatchWriter.ApplyEdits(source, payload.Edits.Select(e => new AxamlTextEdit(e.Start, e.Length, e.NewText)));
            RequireCapability(text == source.Replace("Header=\"Old\"", "Header=\"IPC header\""), "VS-buffer patch lost preserved syntax.");
            await connection.SendAsync(DesignerHostMessageTypes.PatchApplied, envelope.RequestId, "tabs", new PatchAppliedPayload { Text = text, Version = 2, Checksum = DesignerHostProtocol.ComputeChecksum(text) }, cancellation.Token);
            await connection.SendAsync(DesignerHostMessageTypes.Hello, "barrier", "tabs", new HelloPayload(), cancellation.Token); await connection.ReceiveAsync(cancellation.Token);
            Console.WriteLine("COVERAGE_IPC acknowledged");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireCapability(vm.CreateActiveAxamlPatch().CanApply && !vm.CreateActiveAxamlPatch().HasChanges, "IPC ACK created phantom edit.");
                var row = CoverageEditor(vm, "Tabs", "SelectedIndex"); row.Value = "1"; row.CommitValue();
                RequireCapability(vm.CreateActiveAxamlPatch().Edits.Count == 1, "Second edit after IPC ACK failed.");
            });
        });
        try { Pump(task); } finally { window.CloseForBridgeShutdown(); }
    }
}
