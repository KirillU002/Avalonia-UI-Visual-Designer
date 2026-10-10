using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaDesigner.Host.Protocol;
using AvaloniaDesigner.VsHost;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static readonly Dictionary<MainWindowViewModel, DesignerSurface> AxamlSmokeSurfaces = new();
    private static string Phase4Source(string name) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "Phase4", name + ".axaml"));

    private static (AxamlImportResult Result, MainWindowViewModel Vm, DesignerSurface Surface) Phase4Document(SmokeContext context, string source, string sourcePath = "Phase4.axaml", bool showHost = false)
    {
        var result = new AxamlImportService().Import(source, sourcePath);
        RequireIdentity(result);
        var vm = CreateViewModel(context.Scenario.Name);
        vm.LoadAxamlImportedDocument(result, sourcePath);
        DesignerSurface surface;
        if (showHost)
        {
            var window = new VsHostWindow(new TestDesignerHostServices()) { DataContext = vm };
            surface = window.FindControl<DesignerSurface>("DesignerSurface")!;
            window.Show();
            using var layoutWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            Dispatcher.UIThread.MainLoop(layoutWait.Token);
        }
        else
            surface = CreateMainWindowDesignerSurface(new SmokeContext(context.Scenario, vm, context.ProjectPath, context.Xaml, context.CSharp, context.GeneratedFiles, context.ChecklistText, context.DiagnosticsText));
        AxamlSmokeSurfaces[vm] = surface;
        AxamlLayoutProjection.PrepareOffscreen(surface.Canvas);
        surface.Canvas.Measure(new Size(600, 400)); surface.Canvas.Arrange(new Rect(0, 0, 600, 400));
        Dispatcher.UIThread.RunJobs();
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Opening a Phase 4 document changed source.");
        return (result, vm, surface);
    }

    private static T Phase4View<T>(DesignerSurface surface) where T : Control => surface.Canvas.GetVisualDescendants().OfType<T>().Single();
    private static void Phase4Screenshot(DesignerSurface surface, string name)
    {
        var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "axaml-phase4"); Directory.CreateDirectory(output);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600, 400));
        bitmap.Render(surface.Canvas); bitmap.Save(Path.Combine(output, name + ".png"));
    }
    private static void Phase4Edit(MainWindowViewModel vm, string name, string key, string value)
    {
        var row = CoverageEditor(vm, name, key);
        var selected = vm.SelectedControl;
        var ids = vm.Controls.Select(c => c.Id).ToArray();
        RequireCapability(!row.IsReadOnly, "Expected editable Phase 4 property: " + key);
        if (row.Editor == PropertyGridEditorKind.Bool) row.BoolValue = bool.Parse(value);
        else { row.Value = value; row.CommitValue(); }
        // Complete the synthetic Inspector gesture before waiting for its deferred render.
        if (vm.IsPropertyEditorFocused) vm.EndPropertyGridTextEdit();
        // RunJobs alone cannot deliver the native Windows timer used by the shared host.
        using var renderWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));
        Dispatcher.UIThread.MainLoop(renderWait.Token);
        if (AxamlSmokeSurfaces.TryGetValue(vm, out var surface))
        {
            var host = surface.GetLogicalAncestors().OfType<MainWindow>().Single();
            var pending = typeof(MainWindow).GetField("_isDesignerRenderScheduled", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while ((bool)pending.GetValue(host)! && deadline.Elapsed < TimeSpan.FromSeconds(3))
            {
                using var frameWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                Dispatcher.UIThread.MainLoop(frameWait.Token);
            }
            RequireCapability(!(bool)pending.GetValue(host)!, "Inspector render did not finish within the smoke deadline.");
            AxamlLayoutProjection.PrepareOffscreen(surface.Canvas);
            surface.Canvas.Measure(new Size(600, 400)); surface.Canvas.Arrange(new Rect(0, 0, 600, 400));
        }
        RequireCapability(ReferenceEquals(selected, vm.SelectedControl) && ids.SequenceEqual(vm.Controls.Select(c => c.Id)), "Inspector edit reset selection or document.");
    }

    private static void AssertPhase4ExpanderSimple(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, "<Window><Canvas><Expander Name=\"Expand\" Header=\"Settings\" Content=\"Body\" IsExpanded=\"True\" Width=\"240\"/></Canvas></Window>");
        var view = Phase4View<Expander>(surface);
        RequireCapability(view.Header?.ToString() == "Settings" && view.Content?.ToString() == "Body" && view.IsExpanded, "Literal Header/Content not displayed by real Expander.");
        RequireCapability(LayoutBounds(vm, "Expand").Height > 20, "Expander must measure its native content.");
    }

    private static void AssertPhase4ExpanderStack(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, Phase4Source("ExpanderStack"));
        var expand = vm.Controls.Single(c => c.Name == "Expand");
        var body = vm.Controls.Single(c => c.Name == "Body");
        RequireCapability(body.ParentId == expand.Id && vm.Controls.Single(c => c.Name == "Action").ParentId == body.Id, "Expander hierarchy was flattened.");
        var view = Phase4View<Expander>(surface);
        RequireCapability(view.Content is Border { Tag: DesignControlModel m } && m.Id == body.Id, "Expander does not own the shared Designer's Content wrapper.");
        RequireCapability(view.GetVisualDescendants().OfType<Button>().Any(), "Content Button was not rendered.");
        RequireCapability(view.IsHitTestVisible && view.GetVisualDescendants().OfType<Border>().Any(b => b.Tag == body && b.IsHitTestVisible), "Content wrappers cannot receive selection input.");
        Near(LayoutBounds(vm, "Expand").Width, 300, "Expander Width");
        Near(LayoutBounds(vm, "Expand").X, 14, "Expander Canvas.Left + Margin");
        Near(LayoutBounds(vm, "Action").Height, 40, "Expander Button Height");
        Phase4Screenshot(surface, "ExpanderStack");
    }

    private static void AssertPhase4ExpanderGrid(SmokeContext context)
    {
        var (result, vm, surface) = Phase4Document(context, Phase4Source("ExpanderGrid"));
        RequireCapability(vm.Controls.Count == 5 && Phase4View<Expander>(surface).Content is Control, "Explicit Expander.Content/Grid traversal failed.");
        RequireCapability(LayoutBounds(vm, "Action").X >= 100, "Grid.Column semantics lost inside Expander.");
        Phase4Edit(vm, "Expand", "Margin", "10");
        RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == result.RoundTripDocument.OriginalText.Replace("Margin=\"6\"", "Margin=\"10\""), "Margin edit changed nested Grid semantics.");
    }

    private static void AssertPhase4ExpanderCollapsed(SmokeContext context)
    {
        var source = Phase4Source("ExpanderStack").Replace("IsExpanded=\"True\"", "IsExpanded=\"False\"");
        var (_, vm, surface) = Phase4Document(context, source);
        RequireCapability(vm.Controls.Count == 4 && LayoutBounds(vm, "Body").Height == 0 && !Phase4View<Expander>(surface).IsExpanded, "Collapsed state removed children or displayed collapsed content.");
        Phase4Edit(vm, "Expand", "IsExpanded", "True");
        surface.Canvas.Measure(new Size(600, 400)); surface.Canvas.Arrange(new Rect(0, 0, 600, 400));
        Dispatcher.UIThread.RunJobs();
        RequireCapability(vm.Controls.Count == 4 && LayoutBounds(vm, "Body").Height > 0 && Phase4View<Expander>(surface).GetVisualDescendants().OfType<Button>().Any(), "Expanding did not restore existing visual children.");
    }

    private static void AssertPhase4ExpanderHeaderPatch(SmokeContext context)
    {
        var (result, vm, _) = Phase4Document(context, Phase4Source("ExpanderStack"));
        Phase4Edit(vm, "Expand", "Header", "New & safe");
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Header=\"Old\"", "Header=\"New &amp; safe\""), "Header edit is not a single source span.");
    }

    private static void AssertPhase4ExpanderExpandedPatch(SmokeContext context)
    {
        var (result, vm, surface) = Phase4Document(context, Phase4Source("ExpanderStack"));
        Phase4Edit(vm, "Expand", "IsExpanded", "False");
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("IsExpanded=\"True\"", "IsExpanded=\"False\""), "IsExpanded edit changed retained children.");
        RequireCapability(!Phase4View<Expander>(surface).IsExpanded && vm.Controls.Count == 4, "Inspector did not update actual Expander.");
    }

    private static void AssertPhase4ExpanderDirection(SmokeContext context)
    {
        foreach (var direction in new[] { "Up", "Left", "Right", "Down" })
        {
            var (result, vm, surface) = Phase4Document(context, Phase4Source("ExpanderStack"));
            Phase4Edit(vm, "Expand", "ExpandDirection", direction);
            RequireCapability(Phase4View<Expander>(surface).ExpandDirection.ToString() == direction && LayoutBounds(vm, "Body").Width > 0, "ExpandDirection is not projected natively.");
            var patch = vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("ExpandDirection=\"Down\"", "ExpandDirection=\"" + direction + "\""), "ExpandDirection patch changed other source.");
        }
    }

    private static void AssertPhase4ComboStatic(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, Phase4Source("ComboStatic"));
        var combo = Phase4View<ComboBox>(surface);
        RequireCapability(combo.Items.Count == 3 && combo.SelectedItem is ComboBoxItem { Content: "Two" } && combo.PlaceholderText == "Pick", "Real ComboBox static items/selection are incorrect.");
        RequireCapability(vm.Controls.Count == 4 && vm.Controls.Where(c => c.Name != "Choose").All(c => c.ParentId == vm.Controls.Single(c => c.Name == "Choose").Id), "Items do not preserve ParentId.");
        Near(LayoutBounds(vm, "Choose").Width, 200, "Combo Width"); Near(LayoutBounds(vm, "Choose").Height, 36, "Combo Height");
        Near(LayoutBounds(vm, "Choose").X, 22, "Combo Margin X"); Near(LayoutBounds(vm, "Choose").Y, 24, "Combo Margin Y");
        Phase4Screenshot(surface, "ComboStatic");
    }

    private static void AssertPhase4ComboSelectedIndex(SmokeContext context)
    {
        var (result, vm, surface) = Phase4Document(context, Phase4Source("ComboStatic"));
        Phase4Edit(vm, "Choose", "SelectedIndex", "2");
        RequireCapability(Phase4View<ComboBox>(surface).SelectedItem is ComboBoxItem { Content: "Three" }, "Inspector selection did not update native ComboBox.");
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("SelectedIndex=\"1\"", "SelectedIndex=\"2\""), "SelectedIndex must be a minimal edit.");
    }

    private static void AssertPhase4ComboBindings(SmokeContext context)
    {
        var (result, vm, surface) = Phase4Document(context, Phase4Source("ComboBindings"));
        var combo = Phase4View<ComboBox>(surface);
        RequireCapability(combo.Items.Count == 0 && combo.ItemsSource is null && combo.SelectedItem is null, "User ItemsSource/DataContext was evaluated or replaced with mock items.");
        RequireCapability(CoverageEditor(vm, "Choose", "ItemsSource").IsReadOnly && CoverageEditor(vm, "Choose", "SelectedItem").IsReadOnly && CoverageEditor(vm, "Choose", "SelectedIndex").IsReadOnly, "Binding source or selection can be overwritten.");
        Phase4Edit(vm, "Choose", "Width", "250");
        Phase4Edit(vm, "Choose", "Margin", "8");
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 2 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Width=\"200\"", "Width=\"250\"").Replace("Margin=\"3,5\"", "Margin=\"8\""), "Binding/template/style/unknown source changed during safe editing.");
    }

    private static void AssertPhase4SelectedItemBinding(SmokeContext context)
    {
        foreach (var selectionSource in new[] { "SelectedItem=\"{Binding CurrentItem}\"", "SelectingItemsControl.SelectedItem=\"{Binding CurrentItem}\"" })
        {
            var source = Phase4Source("ComboStatic").Replace("SelectedIndex=\"1\"", selectionSource);
            var (_, vm, surface) = Phase4Document(context, source);
            RequireCapability(Phase4View<ComboBox>(surface).SelectedItem is null && CoverageEditor(vm, "Choose", "SelectedIndex").IsReadOnly, "Static items must not override bound selection.");
            Phase4Edit(vm, "Choose", "Height", "40");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == source.Replace("Height=\"36\"", "Height=\"40\""), "Dimension edit overwrote SelectedItem binding.");
        }
        var propertySource = Phase4Source("ComboStatic").Replace("SelectedIndex=\"1\"", "")
            .Replace("<ComboBox.Items>", "<SelectingItemsControl.SelectedItem><Binding Path=\"CurrentItem\"/></SelectingItemsControl.SelectedItem><ComboBox.Items>");
        var (_, propertyVm, propertySurface) = Phase4Document(context, propertySource);
        RequireCapability(Phase4View<ComboBox>(propertySurface).SelectedItem is null && CoverageEditor(propertyVm, "Choose", "SelectedIndex").IsReadOnly,
            "Inherited selection property element must remain opaque and cannot be overridden by SelectedIndex.");
        Phase4Edit(propertyVm, "Choose", "Width", "250");
        RequireCapability(propertyVm.CreateActiveAxamlPatch().PatchedText == propertySource.Replace("Width=\"200\"", "Width=\"250\""), "Property-element selection binding changed.");
    }

    private static void AssertPhase4ItemsSourceBinding(SmokeContext context)
    {
        foreach (var source in new[]
        {
            Phase4Source("ComboStatic").Replace("SelectedIndex=\"1\"", "ItemsSource=\"{Binding Items}\""),
            Phase4Source("ComboStatic").Replace("SelectedIndex=\"1\"", "")
                .Replace("<ComboBox.Items>", "<ItemsControl.ItemsSource><Binding Path=\"Items\"/></ItemsControl.ItemsSource><ComboBox.Items>")
        })
        {
            var (_, vm, surface) = Phase4Document(context, source);
            RequireCapability(vm.Controls.Count == 1 && Phase4View<ComboBox>(surface).Items.Count == 0, "Static items must remain opaque when ItemsSource owns the collection.");
            Phase4Edit(vm, "Choose", "PlaceholderText", "Choose one");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == source.Replace("PlaceholderText=\"Pick\"", "PlaceholderText=\"Choose one\""), "Placeholder edit lost bound source/static opaque items.");
        }
    }

    private static void AssertPhase4ItemContent(SmokeContext context)
    {
        var (result, vm, _) = Phase4Document(context, Phase4Source("ComboStatic"));
        Phase4Edit(vm, "Two", "Content", "Updated");
        var patch = vm.CreateActiveAxamlPatch();
        RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == result.RoundTripDocument.OriginalText.Replace("Content=\"Two\"", "Content=\"Updated\""), "Item edit changed formatting/comment/unknown attribute.");
    }

    private static void AssertPhase4TextItems(SmokeContext context)
    {
        var (_, vm, surface) = Phase4Document(context, Phase4Source("TextItems"));
        var combo = Phase4View<ComboBox>(surface);
        RequireCapability(combo.Items.Count == 2 && combo.SelectedItem is ComboBoxItem { Content: "One & only" }, "XML text entities or typed string items were not imported.");
        RequireCapability(CoverageEditor(vm, "One", "Content").IsReadOnly, "Inner-text source must not acquire a competing Content attribute.");
        Phase4Edit(vm, "Choose", "SelectedIndex", "1");
        RequireCapability(Phase4View<ComboBox>(surface).SelectedItem is ComboBoxItem { Content: "Two" }, "Simple string selection failed.");
    }

    private static void AssertPhase4NoEdit(SmokeContext context)
    {
        foreach (var name in new[] { "ExpanderStack", "ExpanderGrid", "ComboStatic", "ComboBindings", "TextItems", "Combined" })
            RequireIdentity(new AxamlImportService().Import(Phase4Source(name)));
    }

    private static void AssertPhase4Ack(SmokeContext context)
    {
        var (_, vm, _) = Phase4Document(context, Phase4Source("Combined"));
        var ids = vm.Controls.Select(c => c.Id).ToArray();
        Phase4Edit(vm, "Expand", "Header", "First");
        var patch = vm.CreateActiveAxamlPatch(); vm.MarkAxamlRoundTripSaved("Phase4.axaml", patch.PatchedText);
        RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges && ids.SequenceEqual(vm.Controls.Select(c => c.Id)), "ACK lost identity or generated phantom edits.");
        Phase4Edit(vm, "Choose", "SelectedIndex", "1");
        var second = vm.CreateActiveAxamlPatch();
        RequireCapability(second.CanApply && second.Edits.Count == 1 && second.PatchedText == patch.PatchedText.Replace("SelectedIndex=\"0\"", "SelectedIndex=\"1\""), "Second edit after ACK failed.");
        RequireCapability(!vm.CreateActiveAxamlPatch(patch.PatchedText + " ").CanApply, "External checksum conflict was ignored.");
    }

    private static void AssertPhase4UnsafeContent(SmokeContext context)
    {
        foreach (var source in new[] { "<Window><Expander Content=\"text\"><Button/></Expander></Window>", "<Window><Expander><Button/><TextBox/></Expander></Window>", "<Window><Expander>text<Button/></Expander></Window>" })
        {
            var result = new AxamlImportService().Import(source); RequireIdentity(result);
            RequireCapability(result.Document.Controls.Count == 0, "Ambiguous Content cannot be projected safely.");
        }
        var combo = new AxamlImportService().Import("<Window><ComboBox SelectedIndex=\"1\"><custom:Unknown xmlns:custom=\"using:X\"/><ComboBoxItem Content=\"Known\"/></ComboBox></Window>");
        var (_, vm, surface) = Phase4Document(context, combo.RoundTripDocument.OriginalText);
        RequireCapability(Phase4View<ComboBox>(surface).SelectedItem is ComboBoxItem { Content: "Known" }, "Opaque item changed original SelectedIndex semantics.");
    }

    private static void AssertPhase4Diagnostics(SmokeContext context)
    {
        var simple = new AxamlImportService().Import(Phase4Source("Combined"));
        foreach (var code in new[] { "AXAML_EXPANDER_IMPORTED", "AXAML_EXPANDER_CHILDREN_IMPORTED", "AXAML_COMBOBOX_IMPORTED", "AXAML_COMBOBOX_ITEMS_IMPORTED", "AXAML_PROPERTY_OPAQUE", "AXAML_COVERAGE_SUMMARY" })
            RequireCapability(simple.Diagnostics.Any(d => d.Code == code), "Diagnostic missing: " + code);
        var detailed = new AxamlImportService().Import(Phase4Source("Combined"), detailedDiagnostics: true);
        RequireCapability(detailed.Diagnostics.Count > simple.Diagnostics.Count && !simple.Diagnostics.Any(d => d.Code == "AXAML_ELEMENT_DISCOVERED"), "Per-element logging is not opt-in.");
    }

    private static void AssertPhase4RealMainWindow(SmokeContext context)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var (result, vm, surface) = Phase4Document(context, File.ReadAllText(path), path);
        var report = result.CapabilityReport.Structure!;
        var expanders = result.RoundTripDocument.SourceMap.Controls.Where(r => r.Element.LocalName == "Expander").ToArray();
        var combos = result.RoundTripDocument.SourceMap.Controls.Where(r => r.Element.LocalName == "ComboBox").ToArray();
        RequireCapability(expanders.Length == 4 && combos.Length == 45 && report.ImportedElements >= 1439 && report.VisualElements == 1473
            && report.OpaqueVisualElements <= 34 && report.SkippedSubtrees <= 34, "Real Phase 4 coverage regressed.");
        var editVm = CreateViewModel("Phase4RealEdit"); editVm.LoadAxamlImportedDocument(result, path);
        editVm.SelectSingleControl(editVm.Controls.Single(c => c.Id == expanders[0].ControlId));
        var header = editVm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "Header");
        RequireCapability(!header.IsReadOnly, "Real Expander literal Header cannot be edited.");
        header.Value = "Phase 4 header"; header.CommitValue();
        var attribute = expanders[0].Element.FindAttribute("Header")!;
        var expected = result.RoundTripDocument.OriginalText.Remove(attribute.ValueSpan.Start, attribute.ValueSpan.Length).Insert(attribute.ValueSpan.Start, "Phase 4 header");
        var realPatch = editVm.CreateActiveAxamlPatch();
        RequireCapability(realPatch.CanApply && realPatch.Edits.Count == 1 && realPatch.PatchedText == expected,
            "Editing real Expander Header changed adjacent source or lost Inspector selection.");
        // Select each owning page through the existing literal metadata; do not edit or execute user bindings.
        var visibleExpanders = new HashSet<string>(); var visibleCombos = new HashSet<string>();
        foreach (var reference in expanders)
            AxamlControlMetadata.Find("Expander")!.LiteralProperties.Single(p => p.Key == "IsExpanded")
                .Write(vm.Controls.Single(c => c.Id == reference.ControlId), "True");
        foreach (var target in expanders.Concat(combos))
        {
            ActivateAxamlAncestors(vm, result.RoundTripDocument, target.ControlId);
            typeof(MainWindow).GetMethod("RenderDesigner", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(surface.GetLogicalAncestors().OfType<MainWindow>().Single(), null);
            AxamlLayoutProjection.PrepareOffscreen(surface.Canvas);
            surface.Canvas.Measure(new Size(1600, 920)); surface.Canvas.Arrange(new Rect(0, 0, 1600, 920));
            foreach (var expander in surface.Canvas.GetVisualDescendants().OfType<Expander>())
                if (expander.Bounds.Width > 0 && expander.Tag is DesignControlModel owner) visibleExpanders.Add(owner.Id);
            foreach (var comboView in surface.Canvas.GetVisualDescendants().OfType<ComboBox>())
                if (comboView.Bounds.Width > 0 && comboView.Tag is DesignControlModel owner) visibleCombos.Add(owner.Id);
        }
        RequireCapability(visibleExpanders.Count > 0 && visibleCombos.Count > 0, "New types were only counted, never rendered on real MainWindow.");
        var output = Path.Combine(FindRepositoryRoot(), "artifacts", "diagnostics", "axaml-phase4"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "MainWindow-report.txt"), report.Format());
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1600, 920)); bitmap.Render(surface.Canvas); bitmap.Save(Path.Combine(output, "MainWindow.png"));
        Console.WriteLine($"PHASE4_COVERAGE imported={report.ImportedElements}; total={report.VisualElements}; opaque={report.OpaqueVisualElements}; skipped={report.SkippedSubtrees}; partial={report.PartialElements}; Expander={expanders.Length}; ComboBox={combos.Length}; renderedExpanders={visibleExpanders.Count}; renderedCombos={visibleCombos.Count}");
        foreach (var group in report.Blockers.GroupBy(b => b.Type).OrderByDescending(g => g.Sum(b => b.BlockedVisualDescendants)))
            Console.WriteLine($"PHASE4_REMAINING {group.Key}: instances={group.Count()}; blocked={group.Sum(b => b.BlockedVisualDescendants)}");
    }

    private static void AssertPhase4Ipc(SmokeContext context)
        => AssertAxamlIpcEdits(context, Phase4Source("Combined"), new[] { ("Expand", "Header", "IPC header"), ("Choose", "SelectedIndex", "1"), ("Expand", "IsExpanded", "False") });

    private static void AssertAxamlIpcEdits(SmokeContext context, string sourceText, (string Name, string Key, string Value)[] edits)
    {
        var vm = context.ViewModel;
        var window = new VsHostWindow(new TestDesignerHostServices()) { DataContext = vm }; window.Show();
        var pipe = $"{DesignerHostProtocol.PipePrefix}.phase4.{Guid.NewGuid():N}";
        using var bridge = new VsHostBridge(vm, window, pipe); bridge.Start();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var task = Task.Run(async () =>
        {
            using var client = NamedPipeProtocolConnection.CreateClient(pipe); await client.ConnectAsync(cancel.Token);
            using var connection = new NamedPipeProtocolConnection(client);
            await connection.SendAsync(DesignerHostMessageTypes.Hello, "hello", "phase4", new HelloPayload(), cancel.Token);
            RequireCapability((await connection.ReceiveAsync(cancel.Token))!.MessageType == DesignerHostMessageTypes.HelloAck, "HelloAck missing.");
            var source = sourceText; long version = 1;
            await connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "open", "phase4", CreateVsHostOpenDocumentPayload(source, version), cancel.Token);
            RequireCapability((await connection.ReceiveAsync(cancel.Token))!.MessageType == DesignerHostMessageTypes.DocumentOpened, "Phase 4 IPC open failed.");
            foreach (var edit in edits)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Phase4Edit(vm, edit.Item1, edit.Item2, edit.Item3));
                var apply = Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);
                var envelope = (await connection.ReceiveAsync(cancel.Token))!; await apply;
                RequireCapability(envelope.MessageType == DesignerHostMessageTypes.ApplyDesignerPatch, "Phase 4 Apply did not send a patch.");
                var payload = connection.GetPayload<ApplyDesignerPatchPayload>(envelope)!;
                RequireCapability(payload.Edits.Count == 1 && payload.ExpectedVersion == version && payload.ExpectedChecksum == DesignerHostProtocol.ComputeChecksum(source), "Phase 4 patch is not minimal or lost base version/checksum.");
                foreach (var textEdit in AvaloniaDesigner.VSIX.VsTextPatch.Validate(source, payload.Edits))
                    source = source.Remove(textEdit.Start, textEdit.Length).Insert(textEdit.Start, textEdit.NewText);
                await connection.SendAsync(DesignerHostMessageTypes.PatchApplied, envelope.RequestId, "phase4", new PatchAppliedPayload { Text = source, Version = ++version, Checksum = DesignerHostProtocol.ComputeChecksum(source) }, cancel.Token);
                await connection.SendAsync(DesignerHostMessageTypes.Hello, "barrier", "phase4", new HelloPayload(), cancel.Token); await connection.ReceiveAsync(cancel.Token);
                await Dispatcher.UIThread.InvokeAsync(() => RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "ACK created phantom Phase 4 edits."));
            }
            RequireCapability(source.Contains("<!-- KEEP -->") && source.Contains("custom:Unknown=\"KEEP\""), "IPC Apply lost opaque source.");
        });
        try { Pump(task); } finally { window.CloseForBridgeShutdown(); }
    }
}
