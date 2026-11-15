using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel Universal4AVm(string body)
    {
        var source = "<a:Window xmlns='https://github.com/avaloniaui' xmlns:a='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:u='using:Unknown'>\r\n" + body + "\r\n</a:Window>";
        var imported = new AxamlImportService().Import(source, "phase4a.axaml");
        var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(imported, "phase4a.axaml");
        RequireCapability(vm.CreateActiveAxamlPatch().CanApply && !vm.CreateActiveAxamlPatch().HasChanges, "No-op import changed source.");
        return vm;
    }

    private static int RunUniversal4AChecks()
    {
        int pass = 0, fail = 0;
        void Check(string name, Action action)
        {
            try { action(); pass++; Console.WriteLine("PASS UNIVERSAL4A_" + name); }
            catch (Exception error) { fail++; Console.WriteLine("FAIL UNIVERSAL4A_" + name + ": " + error); }
            finally { foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
        }
        foreach (var test in new[] { ("Button", "FontFamily", "Arial", "Tahoma"), ("TextBox", "Padding", "1,2,3,4", "4,3,2,1"),
            ("Button", "CornerRadius", "1,3,5,7", "2,4,6,8"), ("Button", "FontStyle", "Normal", "Italic"),
            ("Button", "MinWidth", "20", "80"), ("NumericUpDown", "Value", "12.50", "15.75"),
            ("Calendar", "FirstDayOfWeek", "Monday", "Friday"), ("RadioButton", "IsChecked", "{x:Null}", "True") })
            Check("PropertyElement_" + test.Item2, () =>
            {
                var (type, key, original, next) = test;
                var vm = Universal4AVm($"<a:Grid><!--keep--><a:{type} x:Name='Target' u:Keep='raw'>\r\n  <a:{type}.{key}>  {original}  </a:{type}.{key}>\r\n</a:{type}></a:Grid>");
                var source = vm.ActiveAxamlSourceDocument!.OriginalText;
                vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Target")); var id = vm.SelectedControl!.Id;
                var row = GetPropertyGridRow(vm, key);
                RequireCapability(!row.IsReadOnly && row.CanResetToDefault, "Scalar element is not editable/resettable.");
                vm.CommitPropertyGridTextEdit(row, next);
                var patch = vm.CreateActiveAxamlPatch();
                RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == source.Replace(">  " + original + "  <", ">  " + next + "  <"), "Text-span patch changed neighbouring source: " + patch.PatchedText);
                vm.UndoEditorCommand!.Execute(null); RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Undo did not restore exact source.");
                vm.RedoEditorCommand!.Execute(null); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == patch.PatchedText, "Redo changed source.");
                vm.ResetPropertyGridProperty(GetPropertyGridRow(vm, key));
                var reset = vm.CreateActiveAxamlPatch();
                RequireCapability(reset.CanApply && !reset.PatchedText.Contains("<a:" + type + "." + key + ">"), "Reset left local property element.");
                vm.UndoEditorCommand.Execute(null); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == patch.PatchedText, "Undo Reset lost edited value.");
                vm.UndoEditorCommand.Execute(null); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == source && vm.SelectedControl?.Id == id, "Repeated Undo lost source/selection.");
                RequireCapability(vm.CreateActiveAxamlPatch(source + " ").ExternalChangeDetected, "Stale source accepted.");
            });
        foreach (var value in new[] { "{Binding UserName}", "{StaticResource Accent}", "{DynamicResource Accent}", "&#123;Binding UserName}", "<!--preserve-->Arial", "<![CDATA[Arial]]>", "<a:FontFamily>Arial</a:FontFamily>" })
            Check("ProtectedElement_" + value, () =>
            {
                var vm = Universal4AVm("<a:Button x:Name='Target'><a:Button.FontFamily>" + value + "</a:Button.FontFamily></a:Button>");
                vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Target")); var row = GetPropertyGridRow(vm, "FontFamily");
                RequireCapability(row.IsReadOnly && !row.CanResetToDefault, "Complex/expression element became scalar.");
                if (value.StartsWith('{') || value.StartsWith("&#123;"))
                    RequireCapability(row.Value == System.Net.WebUtility.HtmlDecode(value)
                        && row.ValueSource == (value.Contains("Resource") ? PropertyGridValueSource.Resource : PropertyGridValueSource.Binding), "Property element lost source expression/value-source classification.");
                vm.CommitPropertyGridTextEdit(row, "Tahoma"); vm.ResetPropertyGridProperty(row);
                RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Protected source changed.");
            });
        foreach (var body in new[] {
            "<a:Button x:Name='Target' FontFamily='Arial'><a:Button.FontFamily>Tahoma</a:Button.FontFamily></a:Button>",
            "<a:Button x:Name='Target'><a:Button.FontFamily>Arial</a:Button.FontFamily><a:Button.FontFamily>Tahoma</a:Button.FontFamily></a:Button>",
            "<a:Button x:Name='Target'><u:Button.FontFamily>Arial</u:Button.FontFamily></a:Button>",
            "<a:Button x:Name='Target'><a:Grid.FontFamily>Arial</a:Grid.FontFamily></a:Button>" })
            Check("AmbiguousOrForeignElement", () =>
            {
                var vm = Universal4AVm(body); vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Target"));
                RequireCapability(GetPropertyGridRow(vm, "FontFamily").IsReadOnly, "Ambiguous property element is editable.");
            });
        foreach (var type in new[] { "Menu", "ComboBox", "TreeView", "TabControl" })
            Check("Nested_" + type, () =>
            {
                var item = type == "Menu" ? "MenuItem" : type == "ComboBox" ? "ComboBoxItem" : type == "TreeView" ? "TreeViewItem" : "TabItem";
                var key = type == "ComboBox" ? "Content" : "Header";
                var vm = Universal4AVm($"<a:Grid><a:{type} x:Name='Owner' Width='320' Height='200'><a:{type}.Items><!--items--><a:{item} x:Name='Child' {key}='Old' FontStyle='Italic' /></a:{type}.Items></a:{type}></a:Grid>");
                var source = vm.ActiveAxamlSourceDocument!.OriginalText;
                vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Child")); var parent = vm.SelectedControl!.ParentId;
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, key), "New & text"); var patch = vm.CreateActiveAxamlPatch();
                RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == source.Replace(key + "='Old'", key + "='New &amp; text'"), "Nested item patch changed hierarchy.");
                vm.UndoEditorCommand!.Execute(null); RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges && vm.SelectedControl!.ParentId == parent, "Nested Undo lost source/parent.");
                var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout(); NativeItemsUiWait(60);
                var native = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Tag is DesignControlModel m && m.Name == "Child" && c.GetType().Name == item);
                ComboBox? combo = null;
                if (type == "ComboBox")
                {
                    combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Tag is DesignControlModel m && m.Name == "Owner");
                    combo.IsDropDownOpen = true; NativeItemsUiWait(80); window.UpdateLayout();
                    native = combo.Items.OfType<ComboBoxItem>().Single();
                    RequireCapability(native.GetVisualRoot() is not null, "Combo item not attached to popup visual tree.");
                }
                // Tab headers are template-owned native TabItems rather than tagged wrappers.
                if (type == "TabControl") native = window.GetVisualDescendants().OfType<TabItem>().FirstOrDefault();
                RequireCapability(native is not null && native.Bounds.Width > 0, "Native item absent from visual tree: " + item);
                if (combo is not null) combo.IsDropDownOpen = false;
                vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Owner")); var editor = vm.OpenCollectionEditor("Items");
                RequireCapability(editor is null && vm.CollectionReadOnlyReason(vm.SelectedControl!, "Items").Length != 0, "Unsafe imported structural mutation enabled.");
                if (editor is not null) vm.CloseCollectionEditor(editor);
            });
        Check("NativeScalarValues", () =>
        {
            var vm = Universal4AVm("<a:Grid><a:Button x:Name='Target' FontFamily='Arial' FontStyle='Italic' BorderThickness='1,2,3,4' CornerRadius='1,3,5,7' Padding='2,4,6,8' IsEnabled='False'/></a:Grid>");
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout();
            var native = window.GetVisualDescendants().OfType<Button>().First(c => c.Tag is DesignControlModel m && m.Name == "Target");
            RequireCapability(native.FontFamily.Name == "Arial" && native.FontStyle == Avalonia.Media.FontStyle.Italic && native.BorderThickness == new Thickness(1,2,3,4)
                && native.CornerRadius == new CornerRadius(1,3,5,7) && native.Padding == new Thickness(2,4,6,8) && !native.IsEnabled, "Native source scalars differ from model.");
        });
        Check("PropertyElementInventoryAndNestedContent", () =>
        {
            var vm = Universal4AVm("<a:Grid><a:Grid.RowDefinitions><a:RowDefinition Height='Auto'/><a:RowDefinition Height='2*'/></a:Grid.RowDefinitions><a:Button x:Name='Target'><a:Button.Content><a:StackPanel><a:TextBlock x:Name='Nested' Text='Safe'/></a:StackPanel></a:Button.Content><a:Control.ContextMenu><a:ContextMenu><a:MenuItem Header='Keep'/></a:ContextMenu></a:Control.ContextMenu></a:Button></a:Grid>");
            var elements = vm.ActiveAxamlCapabilityReport!.PropertyElements;
            RequireCapability(elements.Count == 3 && elements.All(p => p.Element.ElementSpan.Length > 0), "Property element source inventory incomplete.");
            RequireCapability(elements.Any(p => p.OwnerType == "Control" && p.PropertyName == "ContextMenu" && p.Metadata is not null), "Inherited owner/property resolution missing.");
            var nested = vm.Controls.Single(c => c.Name == "Nested"); RequireCapability(!string.IsNullOrEmpty(nested.ParentId), "Content wrapper flattened hierarchy.");
            vm.SelectSingleControl(nested); vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "Text"), "Changed");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("<a:Control.ContextMenu>"), "Opaque attachment lost.");
        });
        Check("PatchedAxamlNativePreview", () =>
        {
            var vm = Universal4AVm("<a:Grid><a:Button x:Name='Target'><a:Button.Padding>1,2,3,4</a:Button.Padding><a:Button.Content><a:TextBlock Text='Nested'/></a:Button.Content></a:Button></a:Grid>");
            vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Target")); vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "Padding"), "4,3,2,1");
            var native = (Window)FormDesigner.Services.RuntimeAxamlPreviewLoader.Load(vm.CreateActiveAxamlPatch().PatchedText);
            try
            {
                native.Show(); native.UpdateLayout();
                var button = native.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "Target");
                RequireCapability(button.Padding == new Thickness(4,3,2,1) && button.GetVisualDescendants().OfType<TextBlock>().Any(c => c.Text == "Nested"), "Patched AXAML lost native nested Content or value.");
            }
            finally { native.Close(); }
        });
        Check("RootContentWrapper", () =>
        {
            var vm = Universal4AVm("<a:Window.Content><!--root--><a:Grid><a:Button x:Name='Nested' Content='Old'/></a:Grid></a:Window.Content>");
            var source = vm.ActiveAxamlSourceDocument!.OriginalText;
            vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Nested")); vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "Text"), "New");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == source.Replace("Content='Old'", "Content='New'"), "Root Content wrapper lost source or hierarchy.");
            var ambiguous = Universal4AVm("<a:Window.Content><a:Button Content='First'/></a:Window.Content><a:Button Content='Second'/>");
            RequireCapability(ambiguous.Controls.Count == 0, "Importer silently chose an ambiguous root Content.");
        });
        Check("AttachedDockMetadataAndParentGuard", () =>
        {
            var vm = Universal4AVm("<a:DockPanel><a:Button x:Name='Target' DockPanel.Dock='Left' Content='Dock'/></a:DockPanel>");
            var source = vm.ActiveAxamlSourceDocument!.OriginalText;
            vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Target")); var row = GetPropertyGridRow(vm, "DockPanel.Dock");
            RequireCapability(!row.IsReadOnly && row.CanResetToDefault, "Attached enum lacks source editor.");
            vm.CommitPropertyGridTextEdit(row, "Right"); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == source.Replace("DockPanel.Dock='Left'", "DockPanel.Dock='Right'"), "Dock edit not minimal.");
            vm.UndoEditorCommand!.Execute(null); RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Dock Undo lost source.");
            var grid = Universal4AVm("<a:Grid><a:Button x:Name='Target' DockPanel.Dock='Right'/></a:Grid>");
            grid.SelectSingleControl(grid.Controls.Single(c => c.Name == "Target")); RequireCapability(GetPropertyGridRow(grid, "DockPanel.Dock").IsReadOnly, "Dock is editable outside its parent context.");
        });
        Check("EntityEscapingAndDuplicateAliases", () =>
        {
            var vm = Universal4AVm("<a:RadioButton x:Name='Target'><a:RadioButton.GroupName>O&apos;Reilly &amp; Co</a:RadioButton.GroupName></a:RadioButton>");
            var source = vm.ActiveAxamlSourceDocument!.OriginalText;
            vm.SelectSingleControl(vm.Controls.Single(c => c.Name == "Target"));
            RequireCapability(GetPropertyGridRow(vm, "GroupName").Value == "O'Reilly & Co", "XML entities were not decoded once.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "GroupName"), "<name> & text");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("&lt;name&gt; &amp; text"), "Element XML escaping failed.");
            vm.UndoEditorCommand!.Execute(null); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == source, "Undo changed original entities.");
            var duplicate = Universal4AVm("<a:Button x:Name='Target' FontFamily='Arial' Button.FontFamily='Tahoma'/>");
            duplicate.SelectSingleControl(duplicate.Controls.Single(c => c.Name == "Target")); RequireCapability(GetPropertyGridRow(duplicate, "FontFamily").IsReadOnly, "Duplicate attribute aliases are patchable.");
        });
        Check("ReplacedSourceCollectible", () =>
        {
            var (vm, document, model) = Universal4AReplacedSource(); ForceFullGc();
            RequireCapability(!document.IsAlive && !model.IsAlive, "Source metadata cache retained a replaced document/control."); GC.KeepAlive(vm);
        });
        Check("UnsupportedBrushPreserved", () =>
        {
            var vm = Universal4AVm("<a:Button x:Name='Target' Background='UnsupportedBrush(value)'/>");
            vm.SelectSingleControl(vm.Controls.Single()); RequireCapability(GetPropertyGridRow(vm, "Background").IsReadOnly, "Invalid native brush admitted as an editable literal.");
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout();
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Unsupported brush was replaced in source.");
        });
        Check("ResetClearsNativeLocalValue", () =>
        {
            var vm = Universal4AVm("<a:Button x:Name='Target' Padding='1,2,3,4'/>"); vm.SelectSingleControl(vm.Controls.Single());
            var reference = vm.ActiveAxamlSourceDocument!.SourceMap.ByControlId[vm.SelectedControl!.Id];
            var before = (Button)AxamlPhase4Projection.Create(reference, vm.SelectedControl)!;
            RequireCapability(before.IsSet(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty), "Fixture has no explicit native local value.");
            vm.ResetPropertyGridProperty(GetPropertyGridRow(vm, "Padding"));
            var after = (Button)AxamlPhase4Projection.Create(reference, vm.SelectedControl)!;
            RequireCapability(!after.IsSet(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty), "Reset reapplied a local default over style/inheritance.");
        });
        Check("ResetAckUndoRedoSemantics", () =>
        {
            var vm = Universal4AVm("<a:NumericUpDown x:Name='Target'><!--keep--><a:NumericUpDown.Value>12.50</a:NumericUpDown.Value></a:NumericUpDown>");
            vm.SelectSingleControl(vm.Controls.Single()); vm.ResetPropertyGridProperty(GetPropertyGridRow(vm, "Value"));
            var saved = vm.CreateActiveAxamlPatch().PatchedText; vm.MarkAxamlRoundTripSaved("phase4a.axaml", saved);
            RequireCapability(vm.UndoEditorCommand!.CanExecute(null), "ACK lost Undo history."); vm.UndoEditorCommand.Execute(null);
            var undo = vm.CreateActiveAxamlPatch(); RequireCapability(undo.CanApply && undo.HasChanges && undo.PatchedText.Contains("<!--keep-->"), "Undo after ACK lost source semantics.");
            var native = (Window)FormDesigner.Services.RuntimeAxamlPreviewLoader.Load(undo.PatchedText);
            try { RequireCapability(((NumericUpDown)native.Content!).Value == 12.5m, "Undo after ACK lost decimal value."); }
            finally { native.Close(); }
            vm.RedoEditorCommand!.Execute(null); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == saved, "Redo after ACK did not restore Reset.");
        });
        foreach (var axaml in new[] { false, true }) Check("ImportedPreviewAppliesSourceScalar_" + axaml, () =>
        {
            var diagnostics = Environment.GetEnvironmentVariable("FORMDESIGNER_PREVIEW_DIAGNOSTICS");
            try
            {
            Environment.SetEnvironmentVariable("FORMDESIGNER_PREVIEW_DIAGNOSTICS", "1");
            var vm = Universal4AVm("<a:Button x:Name='Target' FontFamily='Arial'><a:Button.Padding>1,2,3,4</a:Button.Padding></a:Button>");
            using var preview = ShowDataGridRegressionPreview(vm, axaml);
            var host = preview.Window.FindControl<Border>("PreviewSurfaceBorder")!;
            var native = ((Control)host.Child!).GetVisualDescendants().Prepend((Control)host.Child!).OfType<Button>().Single();
            RequireCapability(native.Padding == new Thickness(1,2,3,4) && native.FontFamily.Name == "Arial", "Imported Preview omitted source literals.");
            RequireCapability(native.GetVisualRoot() == preview.Window && native.Bounds.Width > 0
                && preview.Window.ControlDiagnostics.Any(d => d.ControlId == vm.Controls.Single().Id && d.Created && d.Attached), "Preview warning hid standard Button.");
            Console.WriteLine($"OBSERVED_SOURCE_PREVIEW mode={(axaml ? "AXAML" : "Legacy")}; padding={native!.Padding}; expected=1,2,3,4");
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Model Preview modified imported source.");
            }
            finally { Environment.SetEnvironmentVariable("FORMDESIGNER_PREVIEW_DIAGNOSTICS", diagnostics); }
        });
        Check("LiteralNullMarkerPreserved", () =>
        {
            var vm = Universal4AVm("<a:RadioButton x:Name='Target' GroupName='(null)'/>"); vm.SelectSingleControl(vm.Controls.Single());
            RequireCapability(GetPropertyGridRow(vm, "GroupName").IsReadOnly && !vm.CreateActiveAxamlPatch().HasChanges, "AXAML literal null marker became semantic null.");
        });
        Check("ExplicitDefaultOverridesStyle", () =>
        {
            var vm = Universal4AVm("<a:Window.Styles><a:Style Selector='Button'><a:Setter Property='FontStyle' Value='Italic'/></a:Style></a:Window.Styles><a:Button x:Name='Target'/>");
            vm.SelectSingleControl(vm.Controls.Single()); vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "FontStyle"), "Italic");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "FontStyle"), "Normal"); var patch = vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply && patch.HasChanges && patch.Edits.Count == 1 && patch.PatchedText.Contains("FontStyle=\"Normal\""), "Explicit registration default was dropped.");
            var native = (Window)FormDesigner.Services.RuntimeAxamlPreviewLoader.Load(patch.PatchedText);
            try { native.Show(); native.UpdateLayout(); RequireCapability(((Button)native.Content!).FontStyle == Avalonia.Media.FontStyle.Normal, "Runtime used Style instead of explicit default."); }
            finally { native.Close(); }
            vm.UndoEditorCommand!.Execute(null); RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("FontStyle=\"Italic\""), "Undo lost local presence.");
            vm.UndoEditorCommand.Execute(null); RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Second Undo did not clear local value.");
        });
        Check("ExplicitNullDefaultRequiresNamespace", () =>
        {
            var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(new AxamlImportService().Import("<Window xmlns='https://github.com/avaloniaui'><DatePicker Name='Target'/></Window>"), "null.axaml");
            vm.SelectSingleControl(vm.Controls.Single()); vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "SelectedDate"), "2026-11-15T00:00:00+00:00");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "SelectedDate"), MetadataValueCodec.NullText);
            RequireCapability(!vm.CreateActiveAxamlPatch().CanApply, "Explicit null default generated an undeclared x prefix.");
        });
        Console.WriteLine($"UNIVERSAL4A_CHECKS passed={pass}; failed={fail}"); return fail == 0 ? 0 : 1;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (MainWindowViewModel, WeakReference, WeakReference) Universal4AReplacedSource()
    {
        var vm = Universal4AVm("<a:Button x:Name='Old'><a:Button.FontFamily>Arial</a:Button.FontFamily></a:Button>");
        vm.SelectSingleControl(vm.Controls.Single()); var document = new WeakReference(vm.ActiveAxamlSourceDocument!); var model = new WeakReference(vm.SelectedControl!);
        vm.LoadAxamlImportedDocument(new AxamlImportService().Import("<Window><Button Name='New'/></Window>"), "new.axaml");
        return (vm, document, model);
    }

    private static int GenerateUniversal4ARuntime()
    {
        var directory = Environment.GetEnvironmentVariable("UNIVERSAL4A_GENERATED_DIR")!;
        RequireCapability(!Directory.Exists(directory), "Fresh generated directory required.");
        var export = GridFirstVm(); Pump(export.ExportCurrentResultToProjectAsync(directory));
        var path = Path.Combine(directory, "MainWindow.axaml");
        var className = System.Xml.Linq.XDocument.Parse(File.ReadAllText(path)).Root!.Attributes().Single(a => a.Name.LocalName == "Class").Value;
        var vm = Universal4AVm("<a:Grid RowDefinitions='Auto,Auto,*,*' ColumnDefinitions='*,*'>" +
            "<a:Menu x:Name='MainMenu'><a:MenuItem Header='File'><a:MenuItem x:Name='Action' Header='Old'/><a:Separator/></a:MenuItem></a:Menu>" +
            "<a:Button x:Name='Target' Grid.Row='1' FontFamily='Arial' Content='Source'><a:Button.Padding>1,2,3,4</a:Button.Padding></a:Button>" +
            "<a:ComboBox x:Name='Choices' Grid.Row='1' Grid.Column='1' SelectedIndex='0'><a:ComboBoxItem Content='One'/><a:ComboBoxItem Content='Two'/></a:ComboBox>" +
            "<a:TabControl x:Name='Pages' Grid.Row='2'><a:TabItem Header='First'><a:TextBlock Text='Body'/></a:TabItem><a:TabItem Header='Second'><a:TextBlock Text='Other'/></a:TabItem></a:TabControl>" +
            "<a:TreeView x:Name='Nodes' Grid.Row='3'><a:TreeViewItem x:Name='Branch' Header='Branch' IsExpanded='False'><a:TreeViewItem Header='Leaf'/></a:TreeViewItem></a:TreeView></a:Grid>");
        foreach (var (name, key, value) in new[] { ("Target", "Padding", "4,3,2,1"), ("Target", "FontStyle", "Italic"), ("Action", "Header", "New & text"), ("Branch", "IsExpanded", "True") })
        {
            vm.SelectSingleControl(vm.Controls.Single(c => c.Name == name));
            var row = GetPropertyGridRow(vm, key);
            if (row.Editor == PropertyGridEditorKind.Bool) row.BoolValue = bool.Parse(value); else vm.CommitPropertyGridTextEdit(row, value);
        }
        var patch = vm.CreateActiveAxamlPatch(); RequireCapability(patch.CanApply && patch.HasChanges, "Runtime fixture patch failed.");
        File.WriteAllText(path, patch.PatchedText.Replace("<a:Window xmlns=", "<a:Window x:Class='" + className + "' xmlns="));
        Console.WriteLine("GENERATED4A " + directory); return 0;
    }
    private static string Universal4ASource(int count) => "<Window xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><StackPanel>" +
        string.Concat(Enumerable.Range(0, count).Select(i => $"<NumericUpDown x:Name='N{i}' Value='12.5' FontFamily='Arial' IsHitTestVisible='True'/><!--keep-->")) + "</StackPanel></Window>";

    private static int RunUniversal4AInventory()
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var result = new AxamlImportService().Import(File.ReadAllText(path), path);
        var doc = result.RoundTripDocument;
        var types = AvaloniaMetadataCatalog.DiscoverControls().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());
        var rows = doc.SourceMap.Controls.Select(reference =>
        {
            types.TryGetValue(reference.Element.LocalName, out var type);
            var schema = type is null ? null : AvaloniaMetadataCatalog.Get(type);
            var attributes = reference.Element.Attributes.Where(a => a.Name != "xmlns" && !a.Name.StartsWith("xmlns:")).Select(a =>
            {
                var name = a.Name.StartsWith(reference.Element.LocalName + ".") ? a.Name[(reference.Element.LocalName.Length + 1)..] : a.Name;
                var p = schema?.Properties.FirstOrDefault(p => p.Key == name || p.Name == name);
                var cap = reference.Capability.Properties.FirstOrDefault(p => p.SourceName == a.Name);
                return new { a.Name, Recognized = p is not null || a.Name is "Name" or "x:Name", Editable = cap?.Mode == AxamlPropertyCapabilityMode.Editable,
                    Mode = cap?.Mode.ToString(), cap?.Reason, Expression = a.Value.TrimStart().StartsWith('{') };
            }).ToArray();
            return new { Type = reference.Element.LocalName, Path = AxamlImportStructureReport.PathOf(reference.Element), Attributes = attributes,
                EditableSlots = reference.EditableProperties.Count(), Collections = schema?.Collections.Select(c => new { c.PropertyName, c.ImportCapability, c.CanEditItems }).ToArray() };
        }).ToArray();
        var inventory = new { Source = "Views/MainWindow.axaml", Structure = new { result.CapabilityReport.Structure!.TotalElements, result.CapabilityReport.Structure.VisualElements,
            result.CapabilityReport.Structure.ImportedElements, result.CapabilityReport.Structure.OpaqueVisualElements, result.CapabilityReport.Structure.PropertyElements },
            RecognizedProperties = rows.Sum(r => r.Attributes.Count(a => a.Recognized)), EditableProperties = rows.Sum(r => r.Attributes.Count(a => a.Editable)),
            EditableSlots = rows.Sum(r => r.EditableSlots), UnsupportedExpressions = rows.Sum(r => r.Attributes.Count(a => a.Expression && !a.Editable)),
            OpaqueNodes = result.CapabilityReport.Elements.Count(e => e.Mode == AxamlElementCapabilityMode.Opaque),
            SourceCollections = doc.SourceMap.Controls.Where(r => AxamlControlMetadata.Find(r.Element.LocalName)?.ContainerKind == AxamlContainerKind.Items)
                .Select(r => new { Type = r.Element.LocalName, ImportedItems = doc.SourceMap.Controls.Count(c => c.ParentId == r.ControlId), StructuralEditable = false }).ToArray(),
            PropertyElements = result.CapabilityReport.PropertyElements.Select(p => new { p.OwnerType, p.PropertyName, Recognized = p.Metadata is not null,
                Mode = p.Mode.ToString(), p.Reason, Span = p.Element.ElementSpan, p.ValueSpan, NestedTypes = p.Element.Children.Select(c => c.LocalName).ToArray() }).ToArray(), Rows = rows };
        var output = Environment.GetEnvironmentVariable("UNIVERSAL4A_OUTPUT") ?? Path.Combine(FindRepositoryRoot(), "artifacts", "universal-phase4a", "inventory.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(inventory.Structure));
        Console.WriteLine($"PROPERTIES recognized={inventory.RecognizedProperties}; editable-present={inventory.EditableProperties}; editable-slots={inventory.EditableSlots}; expressions-preserved={inventory.UnsupportedExpressions}");
        RequireCapability(new AxamlPatchWriter().CreatePatch(doc, result.Document).PatchedText == doc.OriginalText, "MainWindow no-op changed source.");
        return 0;
    }

    private static int RunUniversal4AProfile()
    {
        var records = new List<object>();
        foreach (var scene in new[] { ("small", Universal4ASource(10)), ("medium", Universal4ASource(100)),
            ("MainWindow", File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"))) })
        {
            for (var i = -3; i < 30; i++)
            {
                using var stages = FormDesigner.Services.LayoutDropProfile.Begin("profile4a", "import");
                var start = GC.GetAllocatedBytesForCurrentThread(); var gc = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                var timer = Stopwatch.StartNew(); var imported = new AxamlImportService().Import(scene.Item2); timer.Stop();
                var importMs = timer.Elapsed.TotalMilliseconds; var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
                var targetId = imported.RoundTripDocument.SourceMap.Controls.First(r => r.Element.LocalName is "NumericUpDown" or "Button").ControlId; var target = imported.Document.Controls.Single(c => c.Id == targetId);
                start = GC.GetAllocatedBytesForCurrentThread(); timer.Restart(); target.Opacity = .6;
                var patch = new AxamlPatchWriter().CreatePatch(imported.RoundTripDocument, imported.Document); timer.Stop();
                RequireCapability(patch.CanApply && patch.HasChanges, "Profile did not perform real patch.");
                if (i >= 0) records.Add(new { Scene = scene.Item1, Iteration = i, ImportMs = importMs, ImportBytes = bytes, PatchMs = timer.Elapsed.TotalMilliseconds,
                    PatchBytes = GC.GetAllocatedBytesForCurrentThread() - start, GC0 = GC.CollectionCount(0)-gc[0], GC1 = GC.CollectionCount(1)-gc[1], GC2 = GC.CollectionCount(2)-gc[2],
                    ManagedBytes = GC.GetTotalMemory(false), WorkingSet = Environment.WorkingSet, Stages = stages.Stages.ToArray() });
            }
        }
        var output = Environment.GetEnvironmentVariable("UNIVERSAL4A_OUTPUT")!;
        File.WriteAllText(output, JsonSerializer.Serialize(records)); Console.WriteLine("PROFILE4A samples=" + records.Count); return 0;
    }

    private static int RunUniversal4AUiProfile()
    {
        var records = new List<object>();
        foreach (var count in new[] { 20, 100 })
        {
            var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(new AxamlImportService().Import(Universal4ASource(count)), "profile.axaml");
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout();
            for (var i = -3; i < 30; i++)
            {
                void Measure(string operation, Action action)
                {
                    var bytes = GC.GetAllocatedBytesForCurrentThread(); var gc = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                    var timer = Stopwatch.StartNew(); action(); window.UpdateLayout(); timer.Stop();
                    if (i >= 0) records.Add(new { Count = count, Iteration = i, Operation = operation, Ms = timer.Elapsed.TotalMilliseconds,
                        Bytes = GC.GetAllocatedBytesForCurrentThread()-bytes, GC0 = GC.CollectionCount(0)-gc[0], GC1 = GC.CollectionCount(1)-gc[1], GC2 = GC.CollectionCount(2)-gc[2] });
                }
                Measure("Selection", () => vm.SelectSingleControl(vm.Controls.Single(c => c.Name == (i % 2 == 0 ? "N0" : "N1"))));
                Measure("Edit", () => vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm, "Value"), "15.5"));
                Measure("Patch", () => RequireCapability(vm.CreateActiveAxamlPatch().CanApply, "Patch failed."));
                Measure("Undo", () => vm.UndoEditorCommand!.Execute(null));
                RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges, "Undo lost original AXAML.");
                Measure("Redo", () => vm.RedoEditorCommand!.Execute(null));
                vm.UndoEditorCommand!.Execute(null); window.UpdateLayout();
            }
            window.CloseForExternalHost(); SmokeWindows.Clear();
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL4A_OUTPUT")!, JsonSerializer.Serialize(records));
        Console.WriteLine("UI_PROFILE4A samples=" + records.Count); return 0;
    }
}
