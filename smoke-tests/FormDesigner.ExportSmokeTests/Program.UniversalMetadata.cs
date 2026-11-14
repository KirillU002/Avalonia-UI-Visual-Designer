using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string UniversalFolder => Path.Combine(FindRepositoryRoot(), "artifacts", "universal-phase1");
    private static int RunUniversalMetadataChecks()
    {
        Directory.CreateDirectory(UniversalFolder);
        var passed = 0; var failed = 0;
        void Check(string name, Action action)
        {
            var filter = Environment.GetEnvironmentVariable("METADATA_TEST_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { action(); passed++; Console.WriteLine("PASS METADATA_" + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL METADATA_" + name + ": " + error); }
            finally { foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
        }
        Check("StandardDiscovery", () =>
        {
            var controls = AvaloniaMetadataCatalog.DiscoverControls();
            RequireCapability(controls.Contains(typeof(Slider)) && controls.Contains(typeof(Window)) && controls.Contains(typeof(Panel)), "Missing standard types.");
            RequireCapability(controls.All(t => typeof(Control).IsAssignableFrom(t)), "Non-visual type in discovery.");
            RequireCapability(!AvaloniaMetadataCatalog.Get(typeof(Window)).ToolboxCandidate, "Window enabled as leaf.");
            RequireCapability(!AvaloniaMetadataCatalog.Get(typeof(DataGridRow)).ToolboxCandidate && !AvaloniaMetadataCatalog.Get(typeof(ContextMenu)).ToolboxCandidate,
                "Infrastructure/popup classified as an ordinary Toolbox candidate.");
            var definition = AvaloniaMetadataCatalog.Get(typeof(RowDefinition));
            RequireCapability(!definition.ToolboxCandidate && !definition.IsVisual && definition.Properties.Any(p => p.Key == "Height" && p.Editor == MetadataEditor.GridLength),
                "Non-visual Grid definition was classified as a control or lost GridLength metadata.");
        });
        Check("StyledAndInherited", () =>
        {
            var minimum = AvaloniaMetadataCatalog.Get(typeof(Slider)).Properties.Single(p => p.Key == "Minimum");
            RequireCapability(minimum.Property is not null && minimum.Inherited && minimum.Kind == "StyledProperty", "Inherited styled property missing.");
            RequireCapability(minimum.HasDefaultValue && minimum.DefaultText == "0", "Minimum metadata default incorrect.");
        });
        Check("DirectAndReadOnly", () =>
        {
            var bounds = AvaloniaMetadataCatalog.Get(typeof(Button)).Properties.Single(p => p.Key == "Bounds");
            RequireCapability(bounds.Kind == "DirectProperty" && bounds.ReadOnly, "Bounds must remain read-only direct property.");
        });
        Check("AddOwnerFontNative", () =>
        {
            var vm = GridFirstVm(); var model = vm.CreateControl("Slider", 0, 0);
            model.FontSize = 23; model.Foreground = "#123456";
            using var preview = ShowDataGridRegressionPreview(vm, false);
            var native = preview.Window.GetVisualDescendants().OfType<Slider>().Single(c => c.Name == model.Name);
            RequireCapability(native.FontSize == 23 && native.Foreground?.ToString() == "#ff123456", "AddOwner properties were treated as CLR-only and not applied.");
            RequireCapability(AvaloniaMetadataCatalog.Get(typeof(Slider)).Properties.Single(p => p.Key == "FontFamily").Property is not null,
                "AddOwner FontFamily lost AvaloniaProperty identity.");
        });
        Check("Attached", () => RequireCapability(AvaloniaMetadataCatalog.Get(typeof(Grid)).Properties.Any(p => p.Key == "Grid.Row" && p.Attached), "Grid.Row metadata missing."));
        Check("NoArbitraryGetters", () =>
        {
            ThrowingMetadataControl.Reads = 0;
            var metadata = AvaloniaMetadataCatalog.Get(typeof(ThrowingMetadataControl));
            RequireCapability(metadata.Properties.Any(p => p.Name == "Dangerous") && ThrowingMetadataControl.Reads == 0, "Discovery invoked a getter.");
            RequireCapability(!metadata.ToolboxCandidate, "Untrusted custom type auto-enabled.");
        });
        Check("CacheReuseAndProfile", () =>
        {
            var first = AvaloniaMetadataCatalog.Get(typeof(Slider)); var before = AvaloniaMetadataCatalog.BuildCount;
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++) RequireCapability(ReferenceEquals(first, AvaloniaMetadataCatalog.Get(typeof(Slider))), "Metadata recreated.");
            Console.WriteLine($"PROFILE cacheCalls=1000; elapsedMs={clock.Elapsed.TotalMilliseconds:F3}; allocated={GC.GetAllocatedBytesForCurrentThread() - bytes}; builds={AvaloniaMetadataCatalog.BuildCount - before}");
            RequireCapability(AvaloniaMetadataCatalog.BuildCount == before, "Warm discovery rebuilt metadata.");
        });
        Check("CollectibleCache", () =>
        {
            var reference = CollectibleMetadataType();
            for (var i = 0; i < 8 && reference.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            RequireCapability(!reference.IsAlive, "Metadata cache retained a collectible type.");
        });
        foreach (var pair in new[]
        {
            (typeof(Thickness), "2,3,4,5", MetadataEditor.Thickness), (typeof(CornerRadius), "1,2,3,4", MetadataEditor.CornerRadius),
            (typeof(GridLength), "2*", MetadataEditor.GridLength), (typeof(IBrush), "#ff123456", MetadataEditor.Color),
            (typeof(FontFamily), "Inter", MetadataEditor.Font), (typeof(Orientation), "Vertical", MetadataEditor.Enumeration),
            (typeof(bool?), "(null)", MetadataEditor.Enumeration)
        })
            Check("Codec_" + pair.Item1.Name, () => RequireCapability(MetadataValueCodec.EditorFor(pair.Item1) == pair.Item3
                && MetadataValueCodec.TryParse(pair.Item1, pair.Item2, out _), "Wrong typed editor/codec."));
        Check("ComplexAndInvalidSafety", () =>
        {
            RequireCapability(!MetadataValueCodec.TryParse(typeof(double), "NaN", out _), "NaN accepted.");
            RequireCapability(!MetadataValueCodec.TryParse(typeof(Control), "anything", out _), "Complex object parsed as text.");
            RequireCapability(!MetadataValueCodec.TryParse(typeof(string), "{Binding Secret}", out _), "Binding accepted as scalar.");
            RequireCapability(MetadataValueCodec.Display(new ThrowingToString()) == "<ThrowingToString>", "Arbitrary ToString executed.");
        });
        Check("ScalarMarkupAndFiniteStructures", () =>
        {
            var accepted = new[] { (typeof(FontFamily), "{Binding Font}"), (typeof(Thickness), "NaN"), (typeof(CornerRadius), "Infinity") }
                .Where(pair => MetadataValueCodec.TryParse(pair.Item1, pair.Item2, out _)).Select(pair => pair.Item1.Name).ToArray();
            RequireCapability(accepted.Length == 0, "Unsafe scalar values accepted for: " + string.Join(", ", accepted));
        });
        foreach (var name in StandardControlProperties.PilotTypes.Keys)
        {
            Check(name + "_JsonInspectorDesignerPreview", () => VerifyMetadataVerticalSlice(name));
            Check(name + "_SourcePreservation", () => VerifyMetadataSourcePatch(name));
            Check(name + "_SourceInspectorNative", () =>
            {
                var key = name == "ToggleSwitch" ? "IsChecked" : "Value";
                var initial = name == "ToggleSwitch" ? "False" : "12";
                var updated = name == "ToggleSwitch" ? "True" : "42";
                var source = $"<Window xmlns=\"https://github.com/avaloniaui\"><Grid><!--keep--><{name} Name=\"SourceNative\" Width=\"260\" Height=\"70\" {key}=\"{initial}\" /></Grid></Window>";
                var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source), "metadata.axaml");
                var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(160);
                var model = vm.Controls.Single(c => c.Name == "SourceNative"); vm.SelectSingleControl(model);
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, key), updated); NativeItemsUiWait(100);
                DropPumpUntil(() => !(bool)typeof(FormDesigner.Views.MainWindow).GetField("_isDesignerRenderScheduled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!,
                    "source Inspector render completion");
                window.UpdateLayout();
                RequireCapability(vm.SelectedControl?.Id == model.Id, "Source Inspector lost selection.");
                var native = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SourceNative" && c.GetType() == StandardControlProperties.PilotTypes[name]);
                var property = StandardControlProperties.Find(name, key)!;
                Console.WriteLine($"SOURCE_NATIVE type={name}; model={StandardControlProperties.Read(property, model.CustomProperties.SingleOrDefault(p => p.Key == key)?.ValueJson)}; native={MetadataValueCodec.Display(native.GetValue(property.Property!))}; bounds={native.Bounds}; root={native.GetVisualRoot()?.GetType().Name}; pending={typeof(FormDesigner.Views.MainWindow).GetField("_isDesignerRenderScheduled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)}");
                RequireCapability(MetadataValueCodec.Display(native.GetValue(property.Property!)) == updated && native.GetVisualRoot() == window && native.Bounds.Width > 0,
                    "Source Inspector did not update an attached native control.");
                var patch = vm.CreateActiveAxamlPatch();
                RequireCapability(patch.CanApply && patch.PatchedText == source.Replace(key + "=\"" + initial + "\"", key + "=\"" + updated + "\""), "Source Inspector did not produce a minimal patch.");
            });
        }
        Check("PropertyDefaultsAndNull", () =>
        {
            var vm = GridFirstVm(); var model = vm.CreateControl("ToggleSwitch", 0, 0); vm.SelectSingleControl(model);
            var row = GetPropertyGridRow(vm, "IsChecked");
            RequireCapability(row.IsEnumEditor && row.Options.Any(o => o.Value == "(null)") && row.DefaultValue == "False", "Nullable editor/default missing.");
            vm.CommitPropertyGridTextEdit(row, "(null)");
            RequireCapability(model.CustomProperties.Single(p => p.Key == "IsChecked").ValueJson == "null", "Null was not serialized as null.");
            var padding = GetPropertyGridRow(vm, "Padding");
            RequireCapability(padding.Editor == PropertyGridEditorKind.Thickness, "Thickness editor was not selected.");
            vm.CommitPropertyGridTextEdit(padding, "2,3,4,5");
            var before = vm.ExportWorkspaceJson(); var invalidRow = GetPropertyGridRow(vm, "Padding"); vm.CommitPropertyGridTextEdit(invalidRow, "broken");
            var after = vm.ExportWorkspaceJson();
            File.WriteAllText(Path.Combine(UniversalFolder, "invalid-before.json"), before);
            File.WriteAllText(Path.Combine(UniversalFolder, "invalid-after.json"), after);
            RequireCapability(invalidRow.HasValidationError, "Invalid input was not diagnosed.");
            var beforeNode = System.Text.Json.Nodes.JsonNode.Parse(before)!;
            var afterNode = System.Text.Json.Nodes.JsonNode.Parse(after)!;
            foreach (var root in new[] { beforeNode, afterNode })
                foreach (var form in root["Project"]!["Forms"]!.AsArray()) form!.AsObject().Remove("UpdatedUtc");
            RequireCapability(System.Text.Json.Nodes.JsonNode.DeepEquals(beforeNode, afterNode), "Invalid typed value changed document content (excluding normal export timestamp).");
            RequireCapability(vm.GenerateExportedAxamlPreviewSnapshot().ExportAxaml.Contains("IsChecked=\"{x:Null}\""), "Nullable export did not emit x:Null.");
            foreach (var mode in new[] { false, true })
            {
                using var preview = ShowDataGridRegressionPreview(vm, mode);
                RequireCapability(preview.Window.GetVisualDescendants().OfType<ToggleSwitch>().Single(c => c.Name == model.Name).IsChecked is null,
                    "Nullable value lost in native Preview.");
            }
        });
        Check("UndoRedoSelection", () =>
        {
            var vm = GridFirstVm(); var model = vm.CreateControl("Slider", 0, 0); var id = model.Id; vm.SelectSingleControl(model);
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(160);
            var row = GetPropertyGridRow(vm, "Value"); vm.CommitPropertyGridTextEdit(row, "37"); NativeItemsUiWait(160);
            RequireCapability(vm.SelectedControl?.Id == id, "Inspector lost selection.");
            GridEditorUndo(vm); NativeItemsUiWait(90); GridEditorUndo(vm, true); NativeItemsUiWait(90);
            RequireCapability(vm.GetControl(id)?.CustomProperties.Any(p => p.Key == "Value" && p.ValueJson == "37") == true, "Undo/Redo lost scalar value or ControlId.");
        });
        Check("EnumInspectorNative", () =>
        {
            var vm = GridFirstVm(); var model = vm.CreateControl("Slider", 0, 0); vm.SelectSingleControl(model);
            var row = GetPropertyGridRow(vm, "Orientation");
            RequireCapability(row.IsEnumEditor && row.Options.Any(o => o.Value == "Horizontal") && row.Options.Any(o => o.Value == "Vertical"), "Enum options missing.");
            vm.CommitPropertyGridTextEdit(row, "Vertical");
            foreach (var mode in new[] { false, true })
            {
                using var preview = ShowDataGridRegressionPreview(vm, mode);
                RequireCapability(preview.Window.GetVisualDescendants().OfType<Slider>().Single(c => c.Name == model.Name).Orientation == Orientation.Vertical,
                    "Enum Inspector value did not reach native Preview.");
            }
        });
        Check("SourceBindingRemainsOpaque", () =>
        {
            const string source = "<Window xmlns=\"https://github.com/avaloniaui\"><Grid><Slider Name=\"Bound\" Value=\"{Binding Amount}\" /></Grid></Window>";
            var import = new AxamlImportService().Import(source);
            var control = import.Document.Controls.Single(c => c.Name == "Bound");
            RequireCapability(new AxamlPatchWriter().CreatePatch(import.RoundTripDocument, import.Document, source).PatchedText == source, "Binding no-op damaged source.");
            control.CustomProperties.Add(new DesignPropertyValueFileModel { Key = "Value", ValueJson = "42" });
            RequireCapability(!new AxamlPatchWriter().CreatePatch(import.RoundTripDocument, import.Document, source).CanApply, "Binding was replaced by a literal.");
        });
        foreach (var hasNamespace in new[] { true, false })
            Check("SourceNullNamespace_" + hasNamespace, () =>
            {
                var source = "<Window xmlns=\"https://github.com/avaloniaui\"" + (hasNamespace ? " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" : "")
                    + "><ToggleSwitch Name=\"Nullable\" IsChecked=\"False\" /></Window>";
                var import = new AxamlImportService().Import(source); var model = import.Document.Controls.Single();
                var value = model.CustomProperties.Single(p => p.Key == "IsChecked"); value.ValueJson = "null";
                var patch = new AxamlPatchWriter().CreatePatch(import.RoundTripDocument, import.Document, source);
                File.WriteAllText(Path.Combine(UniversalFolder, "null-source-" + hasNamespace + ".json"), JsonSerializer.Serialize(new { source, patch.CanApply, patch.PatchedText, patch.Diagnostics }));
                RequireCapability(hasNamespace ? patch.CanApply && patch.PatchedText.Contains("IsChecked=\"{x:Null}\"") : !patch.CanApply && patch.PatchedText == source,
                    "Null generated an invalid prefix or damaged source.");
            });
        Check("UnknownPropertiesSurviveJson", () =>
        {
            var vm = MetadataScene(); var model = vm.Controls.First();
            model.CustomProperties.Add(new DesignPropertyValueModel { Key = "Future.Complex", ValueJson = "{\"binding\":\"preserve\",\"items\":[1,2]}" });
            var restored = GridFirstVm(); restored.LoadDocumentJson(vm.ExportWorkspaceJson(), "unknown.json");
            RequireCapability(restored.GetControl(model.Id)!.CustomProperties.Single(p => p.Key == "Future.Complex").ValueJson == model.CustomProperties.Last().ValueJson,
                "Unknown property data was lost.");
        });
        Check("MultiFormMetadataIsolation", () =>
        {
            var vm = MetadataScene(); var first = vm.ActiveDocumentId;
            var sliderId = vm.Controls.Single(c => c.Type == "Slider").Id;
            var canvas = vm.CreateNewForm("Absolute").Id;
            var toggle = vm.CreateControl("ToggleSwitch", 37, 53, null, true);
            StandardControlProperties.TryWriteModel(toggle, StandardControlProperties.Find("ToggleSwitch", "IsChecked")!, "False");
            var toggleId = toggle.Id;
            var restored = GridFirstVm(); restored.LoadDocumentJson(vm.ExportWorkspaceJson());
            foreach (var form in new[] { first, canvas, first, canvas })
            {
                restored.SetActiveForm(form, "metadata-isolation");
                var id = form == first ? sliderId : toggleId;
                restored.SelectSingleControl(restored.GetControl(id)!);
                RequireCapability(restored.SelectedControl?.Id == id && restored.UsesNativeLayout == (form == first), "Form/selection metadata leaked across sessions.");
                var row = GetPropertyGridRow(restored, form == first ? "Value" : "IsChecked");
                RequireCapability(row.Value == (form == first ? "37" : "False"), "Inspector inherited another form's metadata value.");
                restored.GenerateXaml();
                RequireCapability(form == first ? restored.GeneratedXaml.Contains("<Slider") && !restored.GeneratedXaml.Contains("Canvas.Left=")
                    : restored.GeneratedXaml.Contains("<ToggleSwitch") && restored.GeneratedXaml.Contains("Canvas.Left=\"37\""), "Export lost per-form layout semantics.");
            }
        });
        foreach (var type in StandardControlProperties.PilotTypes.Keys)
            Check(type + "_RoutedGridToolbox", () =>
            {
                using var f = new PointerFixture(); f.Vm.NewProjectEditorCommand!.Execute(null);
                f.Vm.AddGridRowDefinitionCommand.Execute(null); f.Vm.AddGridColumnDefinitionCommand.Execute(null); f.Refresh();
                var point = GridCellPoint(f.Vm, "", 1, 1); var data = GridToolboxData(f, type);
                RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); RouteGridToolbox(f, data, DragDrop.DropEvent, point);
                f.Refresh(); f.AssertIdle(); var model = f.Vm.Controls.Single();
                RequireCapability(model.Type == type && model.GridRow == 1 && model.GridColumn == 1 && model.X == 0 && model.Y == 0 && f.Vm.SelectedControl?.Id == model.Id,
                    "Routed Toolbox drop lost identity/placement/selection.");
                RequireCapability(f.Window.GetVisualDescendants().OfType<Control>().Any(c => c.GetType() == StandardControlProperties.PilotTypes[type] && c.Name == model.Name),
                    "Routed drop produced no native control.");
                GridEditorUndo(f.Vm); GridEditorUndo(f.Vm, true);
                RequireCapability(f.Vm.Controls.Single().Id == model.Id && f.Vm.Controls.Single().GridColumn == 1, "Toolbox Undo/Redo lost identity or placement.");
                f.Refresh();
                var wrapper = DropWrappers(f.Window)[model.Id];
                var click = wrapper.TranslatePoint(new Point(wrapper.Bounds.Width / 2, wrapper.Bounds.Height / 2), f.Window)!.Value;
                RequireCapability(f.Window.InputHitTest(click) is Visual hit && hit.GetVisualAncestors().Prepend(hit).OfType<Control>()
                    .Any(c => c.Tag is DesignControlModel tagged && tagged.Id == model.Id), "Native click missed the component's actual bounds.");
                f.Click(click); f.NoMarquee(); f.AssertIdle();
                Console.WriteLine("METADATA_CLICK " + JsonSerializer.Serialize(new { type, click, Bounds = wrapper.Bounds.ToString(), Selected = f.Vm.SelectedControl?.Id,
                    SessionSelected = f.Surface.Session?.SelectedControl?.Id, Expected = model.Id, Events = f.Window.DesignerPointerDiagnostics.TakeLast(10) }));
                RequireCapability(f.Vm.SelectedControl?.Id == model.Id && f.Surface.Session?.SelectedControl?.Id == model.Id,
                    "Native pointer click lost Designer/Inspector selection.");
            });
        foreach (var mode in new[] { false, true })
            Check("CancelledAnimatedClose_" + mode, () =>
            {
                var vm = MetadataScene(); var model = vm.Controls.Single(c => c.Type == "ProgressBar");
                StandardControlProperties.TryWriteModel(model, StandardControlProperties.Find("ProgressBar", "IsIndeterminate")!, "True");
                using var preview = ShowDataGridRegressionPreview(vm, mode);
                DropPumpUntil(() => !preview.Window.FindControl<Border>("LoadingOverlay")!.IsVisible, "animated preview initialization");
                var native = preview.Window.GetVisualDescendants().OfType<ProgressBar>().Single(c => c.Name == model.Name);
                EventHandler<WindowClosingEventArgs> cancel = (_, e) => e.Cancel = true;
                preview.Window.Closing += cancel;
                try { preview.Window.Close(); RequireCapability(preview.Window.IsVisible && native.IsIndeterminate, "Cancelled close stopped a live content animation."); }
                finally { preview.Window.Closing -= cancel; }
                RequireCapability(model.CustomProperties.Single(p => p.Key == "IsIndeterminate").ValueJson == "true", "Closing changed the document snapshot.");
            });
        foreach (var mode in new[] { false, true })
            Check("AnimatedPreviewLifetime_" + mode, () =>
            {
                var vm = MetadataScene(); var bar = vm.Controls.Single(c => c.Type == "ProgressBar");
                StandardControlProperties.TryWriteModel(bar, StandardControlProperties.Find("ProgressBar", "IsIndeterminate")!, "True");
                NativeItemsUiWait(200); // Settle the previous cancelled-close fixture before measuring observer delta.
                var before = MemoryAnimationObservers();
                var references = Enumerable.Range(0, 3).Select(_ => CloseMemoryPreview(vm, mode, false)).ToArray();
                VerifyMemoryReleased(references);
                Console.WriteLine($"OBSERVED metadataProgressBar mode={mode}; aliveWindows={references.Count(r => r.Window.IsAlive)}; clockBefore={before}; clockAfter={MemoryAnimationObservers()}");
                RequireCapability(MemoryAnimationObservers() == before, "Metadata ProgressBar retained animation observers.");
            });
        foreach (var mode in new[] { false, true })
            Check("CancelledOwnerAnimatedClose_" + mode, () =>
            {
                var vm = MetadataScene(); var model = vm.Controls.Single(c => c.Type == "ProgressBar");
                StandardControlProperties.TryWriteModel(model, StandardControlProperties.Find("ProgressBar", "IsIndeterminate")!, "True");
                var owner = NewMemoryPreview(vm, mode); var child = NewMemoryPreview(vm, mode);
                owner.Show(); child.Show(owner); NativeItemsUiWait(600);
                var bar = child.GetVisualDescendants().OfType<ProgressBar>().Single(c => c.Name == model.Name);
                EventHandler<WindowClosingEventArgs> cancel = (_, e) => e.Cancel = true;
                owner.Closing += cancel;
                try { owner.Close(); RequireCapability(owner.IsVisible && child.IsVisible && bar.IsIndeterminate, "Cancelled owner close stopped a live child animation."); }
                finally { owner.Closing -= cancel; owner.Close(); }
            });
        Check("CoverageReport", () =>
        {
            var registry = new DesignerRegistry(); BuiltInControlRegistrar.Register(registry);
            RequireCapability(!AvaloniaMetadataCatalog.Get(typeof(Image)).Properties.Any(p => p.Name.StartsWith("__AvaloniaReserved", StringComparison.Ordinal)
                || p.Key == "Visual.SavedAdornerLayer"), "Private runtime properties entered the public metadata schema.");
            MetadataCoverageReport.Write(registry, Path.Combine(FindRepositoryRoot(), "Docs"));
        });
        Check("GenerateRuntimeFixture", () =>
        {
            var vm = MetadataScene();
            File.WriteAllText(Path.Combine(UniversalFolder, "scene.formdesigner.json"), vm.ExportWorkspaceJson());
            Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(UniversalFolder, "generated")));
            RequireCapability(Directory.GetFiles(Path.Combine(UniversalFolder, "generated"), "*.csproj").Length == 1, "Generated project missing.");
        });
        Console.WriteLine($"Universal metadata checks: {passed}/{passed + failed}"); return failed == 0 ? 0 : 1;
    }
    private static MainWindowViewModel MetadataScene()
    {
        var vm = GridFirstVm(); vm.SurfaceGridRowDefinitions = "*,*,*"; vm.SurfaceLayoutRows = 3;
        var index = 0;
        foreach (var type in StandardControlProperties.PilotTypes.Keys)
        {
            var model = vm.CreateControl(type, 0, 0); model.Name = "Metadata_" + type; model.GridRow = index++;
            model.Width = 260; model.Height = 70; model.WidthMode = model.HeightMode = "Fixed";
            StandardControlProperties.TryWriteModel(model, StandardControlProperties.Find(type, "Padding")!, "2,3,4,5");
            if (type != "ToggleSwitch") StandardControlProperties.TryWriteModel(model, StandardControlProperties.Find(type, "Value")!, "37");
            else StandardControlProperties.TryWriteModel(model, StandardControlProperties.Find(type, "IsChecked")!, "True");
        }
        return vm;
    }
    private static void VerifyMetadataVerticalSlice(string type)
    {
        var vm = MetadataScene(); var model = vm.Controls.Single(c => c.Type == type); vm.SelectSingleControl(model);
        var before = vm.ExportWorkspaceJson(); var restored = GridFirstVm(); restored.LoadDocumentJson(before, "metadata.json");
        RequireCapability(restored.GetControl(model.Id)?.Type == type, "JSON lost type/ControlId.");
        var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(180);
        RequireCapability(window.GetVisualDescendants().OfType<Control>().Any(c => c.GetType() == StandardControlProperties.PilotTypes[type]
            && c.GetVisualRoot() is not null && c.Bounds.Width > 0), "Designer has no attached native control: " + type);
        foreach (var axaml in new[] { false, true })
        {
            using var preview = ShowDataGridRegressionPreview(vm, axaml);
            DropPumpUntil(() => !preview.Window.FindControl<Border>("LoadingOverlay")!.IsVisible
                && (int)typeof(FormDesigner.Views.PreviewWindow).GetField("_previewActivities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview.Window)! == 0, "metadata Preview initialization");
            var native = preview.Window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.GetType() == StandardControlProperties.PilotTypes[type] && c.Name == model.Name);
            RequireCapability(native is { IsVisible: true } && native.GetVisualRoot() is not null && native.Bounds.Width > 0, "Preview lost native control: " + type + "/" + axaml);
            if (native is Slider or ToggleSwitch) RequireCapability(native.IsHitTestVisible, "Designer input mask leaked into runtime Preview.");
            if (native is Slider slider) RequireCapability(slider.Value == 37 && slider.Padding == new Thickness(2, 3, 4, 5), "Slider scalar/Thickness lost.");
            if (native is ProgressBar bar) RequireCapability(bar.Value == 37, "ProgressBar value lost.");
            if (native is ToggleSwitch toggle) RequireCapability(toggle.IsChecked == true, "ToggleSwitch state lost.");
        }
        var export = vm.GenerateExportedAxamlPreviewSnapshot().ExportAxaml;
        RequireCapability(export.Contains("<" + type) && export.Contains("Padding=\"2,3,4,5\""), "Export omitted metadata properties.");
    }
    private static void VerifyMetadataSourcePatch(string type)
    {
        var key = type == "ToggleSwitch" ? "IsChecked" : "Value"; var initial = type == "ToggleSwitch" ? "False" : "12";
        var updated = type == "ToggleSwitch" ? "True" : "42";
        var text = $"<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:q=\"urn:unknown\"><Grid><!--keep--><{type} x:Name=\"Native\" {key}=\"{initial}\" q:Unknown=\"preserve\" Tag=\"{{Binding UserData}}\" /></Grid></Window>";
        var import = new AxamlImportService().Import(text, "metadata.axaml");
        var model = import.Document.Controls.Single(c => c.Name == "Native");
        var noop = new AxamlPatchWriter().CreatePatch(import.RoundTripDocument, import.Document, text);
        File.WriteAllText(Path.Combine(UniversalFolder, type + "-noop.json"), JsonSerializer.Serialize(new { text, noop.CanApply, noop.PatchedText, noop.Diagnostics }));
        RequireCapability(noop.CanApply && noop.PatchedText == text, "No-op changed source.");
        RequireCapability(StandardControlProperties.TryEncode(StandardControlProperties.Find(type, key)!, updated, out var encoded), "Metadata write failed.");
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == key);
        if (stored is null) model.CustomProperties.Add(stored = new DesignPropertyValueFileModel { Key = key });
        stored.ValueJson = encoded;
        var patch = new AxamlPatchWriter().CreatePatch(import.RoundTripDocument, import.Document, text);
        RequireCapability(patch.CanApply && patch.PatchedText == text.Replace(key + "=\"" + initial + "\"", key + "=\"" + updated + "\""), "Patch damaged comments/binding/unknown syntax.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CollectibleMetadataType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MetadataCollectible" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType("CollectibleControl", TypeAttributes.Public, typeof(Control)).CreateType()!;
        AvaloniaMetadataCatalog.Get(type); return new WeakReference(type);
    }
    public sealed class ThrowingMetadataControl : Control
    {
        public static int Reads;
        public string Dangerous { get { Reads++; throw new InvalidOperationException("Getter must not execute"); } set { } }
    }
    private sealed class ThrowingToString { public override string ToString() => throw new InvalidOperationException("No ToString fallback"); }

    private static int RunUniversalMetadataProfile()
    {
        Directory.CreateDirectory(UniversalFolder);
        var coldBytes = GC.GetAllocatedBytesForCurrentThread(); var cold = Stopwatch.StartNew();
        AvaloniaMetadataCatalog.Get(typeof(Slider)); cold.Stop(); coldBytes = GC.GetAllocatedBytesForCurrentThread() - coldBytes;
        var vm = MetadataScene(); var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(200);
        var builds = AvaloniaMetadataCatalog.BuildCount; var observations = new List<object>(); var times = new List<double>(); var allocations = new List<long>();
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(); var managedBefore = GC.GetTotalMemory(false);
        for (var i = -3; i < 30; i++)
        {
            var control = vm.Controls[i < 0 ? i + 3 : i % 3]; var allocated = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
            vm.SelectSingleControl(control); var selectionMs = clock.Elapsed.TotalMilliseconds;
            window.UpdateLayout(); clock.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            observations.Add(new { Iteration = i, Control = control.Type, ElapsedMs = clock.Elapsed.TotalMilliseconds, SelectionMs = selectionMs,
                LayoutMs = clock.Elapsed.TotalMilliseconds - selectionMs, AllocatedBytes = allocated });
            if (i >= 0) { times.Add(clock.Elapsed.TotalMilliseconds); allocations.Add(allocated); }
            NativeItemsUiWait(15);
        }
        var sortedTimes = times.OrderBy(t => t).ToArray(); var sortedAllocations = allocations.OrderBy(a => a).ToArray();
        var report = new { ColdMetadataMs = cold.Elapsed.TotalMilliseconds, ColdMetadataAllocated = coldBytes,
            WarmInspectorMedianMs = (sortedTimes[14] + sortedTimes[15]) / 2, WarmInspectorP95Ms = sortedTimes[28],
            WarmInspectorMedianAllocated = (sortedAllocations[14] + sortedAllocations[15]) / 2d, MetadataRebuildsDuringSwitches = AvaloniaMetadataCatalog.BuildCount - builds,
            WarmInspectorP95Allocated = allocations.OrderBy(a => a).ElementAt(28),
            GCCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray(), ManagedBefore = managedBefore, ManagedAfter = GC.GetTotalMemory(false),
            Samples = 30, Warmup = 3, Includes = "Synchronous selection/Inspector/native layout; no compositor timing", Observations = observations };
        File.WriteAllText(Path.Combine(UniversalFolder, "metadata-profile.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear();
        Console.WriteLine(JsonSerializer.Serialize(report)); return report.MetadataRebuildsDuringSwitches == 0 ? 0 : 1;
    }

}
