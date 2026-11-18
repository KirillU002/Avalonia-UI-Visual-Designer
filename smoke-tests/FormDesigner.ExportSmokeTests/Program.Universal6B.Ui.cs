using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.Views;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences Close6BPreviewWithPendingGridScroll()
    {
        using var scope = ShowDataGridRegressionPreview(MemoryPreviewModel("TwentyCycles"), false);
        var window = scope.Window;
        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        var bar = grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .Single(s => ReferenceEquals(s.TemplatedParent, grid) && s.Name == "PART_VerticalScrollbar");
        RequireCapability(bar.AllowAutoHide && bar.HideDelay > TimeSpan.FromSeconds(1),
            "Native DataGrid lost its auto-hide configuration before close.");
        EventHandler<WindowClosingEventArgs> cancel = (_, e) => e.Cancel = true;
        window.Closing += cancel;
        try
        {
            window.Close();
            RequireCapability(window.IsVisible && bar.AllowAutoHide, "Cancelled Close changed the live DataGrid scrollbar.");
        }
        finally { window.Closing -= cancel; }
        var references = CaptureMemoryReferences(window);
        using var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true);
        var exit = new Avalonia.Input.PointerEventArgs(Avalonia.Input.InputElement.PointerExitedEvent, bar,
            pointer, window, default, 0, new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None);
        bar.RaiseEvent(exit);
        window.Close();
        return references;
    }

    private static void Run6BUiChecks(Action<string, Action> check)
    {
        check("Committed close releases the DataGrid scrollbar delay after cancelled close", () =>
            VerifyMemoryReleased(new[] { Close6BPreviewWithPendingGridScroll() }));
        check("compound Setter exported and restored as native CornerRadius", () =>
        {
            var vm = StyleScene("Grid");
            try
            {
                var sheet = vm.AppearanceDraft();
                sheet.Styles.Single(s => s.Selector == "#SaveButton").Setters[0] = new("CornerRadius", "1,2,3,4");
                vm.SaveAppearance(vm.ActiveDocumentId, sheet);
                vm.GenerateXaml();
                var style = XDocument.Parse(vm.GeneratedXaml).Descendants()
                    .Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute("Selector") == "Button#SaveButton");
                var text = style.Elements().Single(e => (string?)e.Attribute("Property") == "CornerRadius").Attribute("Value")!.Value;
                var expected = new Avalonia.CornerRadius(1, 2, 3, 4);
                RequireCapability(Avalonia.CornerRadius.Parse(text).Equals(expected), "Generated Setter changed corner order");
                using var preview = ShowDataGridRegressionPreview(vm, true);
                var button = preview.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton");
                RequireCapability(button.CornerRadius.Equals(expected), "Native Setter changed corner order");
            }
            finally { Dispose5AProfileVm(vm); }
        });

        check("invalid internal null marker preserves previous folder and zip", () =>
        {
            var vm = StyleScene("Grid");
            var folder = Path.GetFullPath(Path.Combine("artifacts", "universal-phase6b", "atomic", Guid.NewGuid().ToString("N")));
            var zip = folder + ".zip";
            try
            {
                var original = vm.ActiveDocumentId;
                vm.CreateNewForm();
                Pump(vm.ExportCurrentResultToProjectAsync(folder));
                Pump(vm.ExportCurrentResultAsZipAsync(zip));
                string[] FileHashes() => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Append(zip)
                    .OrderBy(f => f, StringComparer.Ordinal).Select(f => f + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToArray();
                var before = FileHashes();
                RequireCapability(before.Length > 2, "Valid export fixture missing");
                // Corrupt an inactive form to exercise export preflight independently of editor validation.
                var other = vm.CurrentProject.Forms.Single(f => f.Id == original);
                other.Document.Appearance!.Styles[0].Setters[0] = new("Background", MetadataValueCodec.NullText);
                foreach (var saveZip in new[] { false, true })
                {
                    try { Pump(saveZip ? vm.ExportCurrentResultAsZipAsync(zip) : vm.ExportCurrentResultToProjectAsync(folder)); }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Setter", StringComparison.Ordinal)) { continue; }
                    throw new InvalidOperationException("Invalid Setter reached export publication");
                }
                RequireCapability(before.SequenceEqual(FileHashes()), "Rejected export changed previously published files");
            }
            finally { Dispose5AProfileVm(vm); }
        });

        check("existing DesignerSurface responds to edit and Undo", () =>
        {
            var vm = StyleScene("Grid");
            var window = CreateSmokeWindow(vm);
            var selected = vm.SelectedControl?.Id;
            try
            {
                window.Show();
                vm.CloseStartScreenCommand.Execute(null);
                Button? FindButton() => window.GetVisualDescendants().OfType<Button>()
                    .SingleOrDefault(b => b.Name == "SaveButton" && b.Classes.Contains("primary"));
                DropPumpUntil(() => FindButton()?.CornerRadius.TopLeft == 9, "initial Designer style");
                var sheet = vm.AppearanceDraft();
                sheet.Styles.Single(s => s.Selector == "#SaveButton").Setters[0] = new("CornerRadius", "13");
                vm.SaveAppearance(vm.ActiveDocumentId, sheet);
                DropPumpUntil(() => FindButton()?.CornerRadius.TopLeft == 13, "edited Designer style");
                GridEditorUndo(vm);
                DropPumpUntil(() => FindButton()?.CornerRadius.TopLeft == 9, "restored Designer style");
                RequireCapability(vm.SelectedControl?.Id == selected, "Style edit or Undo changed selected control");
            }
            finally
            {
                window.CloseForExternalHost();
                SmokeWindows.Clear();
                Dispose5AProfileVm(vm);
            }
        });

        foreach (var axaml in new[] { false, true })
        {
            check("native theme switch, selectors and local precedence " + (axaml ? "AXAML" : "Legacy"), () =>
            {
                var vm = StyleScene("Grid");
                try
                {
                    var selected = vm.Controls.Single(c => c.Name == "SaveButton");
                    vm.SelectSingleControl(selected);
                    Commit6A(vm, PropertyValueKind.Literal, "31", "FontSize");
                    var other = vm.CreateControl("Button", 0, 0);
                    other.Name = "OtherButton";
                    vm.SelectSingleControl(other);
                    Commit6A(vm, PropertyValueKind.Unset, "", "CornerRadius");
                    Commit6A(vm, PropertyValueKind.Unset, "", "FontSize");
                    var sheet = vm.AppearanceDraft();
                    sheet.Styles.Add(new("Button", "Button", new() { new("FontSize", "24") }));
                    vm.SaveAppearance(vm.ActiveDocumentId, sheet);
                    using var preview = ShowDataGridRegressionPreview(vm, axaml);
                    var native = preview.Window.GetVisualDescendants().OfType<Button>().ToArray();
                    var save = native.Single(b => b.Name == "SaveButton");
                    var second = native.Single(b => b.Name == "OtherButton");
                    RequireCapability(save.FontSize == 31 && second.FontSize == 24, "Local or type setter precedence");
                    RequireCapability(save.CornerRadius.TopLeft == 9 && second.CornerRadius.TopLeft != 9, "Name selector leaked to sibling");
                    foreach (var (theme, hex) in new[] { (ThemeVariant.Light, "#123456"), (ThemeVariant.Dark, "#ABCDEF") })
                    {
                        preview.Window.RequestedThemeVariant = theme;
                        preview.Window.UpdateLayout();
                        var color = Color.Parse(hex);
                        RequireCapability(save.Background is ISolidColorBrush brush && brush.Color == color,
                            "Resource for " + theme + " was " + save.Background);
                        RequireCapability(second.Background is not ISolidColorBrush sibling || sibling.Color != color, "Class selector leaked to sibling");
                    }
                }
                finally { Dispose5AProfileVm(vm); }
            });
        }

        check("forms keep independent style and resource revisions on reopen", () =>
        {
            var vm = StyleScene("Grid");
            var reopened = GridFirstVm();
            try
            {
                var first = vm.ActiveDocumentId;
                vm.CreateNewForm();
                var second = vm.ActiveDocumentId;
                var sheet = vm.AppearanceDraft();
                RequireCapability(first != second && sheet.Styles.Count == 0 && sheet.Resources.Count == 0, "New form inherited appearance");
                sheet.Resources.Add(new("TextSize6B", "Double", "17"));
                sheet.Styles.Add(new("TextBox", "TextBox", new() { new("FontSize", "{StaticResource TextSize6B}") }));
                vm.SaveAppearance(second, sheet);
                reopened.LoadDocumentJson(vm.ExportWorkspaceJson());
                foreach (var (id, expected) in new[] { (first, "23"), (second, "17"), (first, "23") })
                {
                    reopened.SetActiveForm(id, "6b-test");
                    var active = reopened.AppearanceDraft();
                    RequireCapability(active.Resources.Single(r => r.Key == "TextSize6B").Value == expected, "Resource crossed form boundary");
                    RequireCapability(active.Styles.Count == (id == first ? 3 : 1), "Style collection crossed form boundary");
                }
            }
            finally { Dispose5AProfileVm(vm); Dispose5AProfileVm(reopened); }
        });

        check("resource type filter combines with scope search and preserves the selected entry", () =>
        {
            var vm = GridFirstVm();
            var sheet = vm.AppearanceDraft();
            var declarations = new FormResource[]
            {
                new("Swatch", "Color", "Red"),
                new("Swatch", "Color", "Blue", "Dark"),
                new("Caption", "String", "Title")
            };
            sheet.Resources.AddRange(declarations);
            vm.SaveAppearance(vm.ActiveDocumentId, sheet);
            var editor = new FormStyleEditorWindow(vm);
            try
            {
                editor.Show();
                editor.UpdateLayout();
                editor.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
                editor.UpdateLayout();
                var resources = editor.GetVisualDescendants().OfType<ListBox>().Single();
                var filter = editor.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.SelectedItem, "Все типы"));
                var fields = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
                var search = fields.Single(b => b.Watermark == "Поиск по ключу, типу или теме");
                var value = fields.Single(b => b.Watermark == "Значение ресурса");
                RequireCapability(filter.Items.Cast<string>().Skip(1).SequenceEqual(FormStyleAuthoring.ResourceTypes),
                    "Category choices differ from supported resource types");
                resources.SelectedIndex = 0;
                value.Text = "Green";
                search.Text = "оБщИй";
                DropPumpUntil(() => resources.ItemCount == 2, "search by the visible common scope");
                filter.SelectedItem = "Color";
                DropPumpUntil(() => resources.ItemCount == 1, "combine resource category and scope");
                RequireCapability(Equals(resources.SelectedItem, "Общий · Color · Swatch") && value.Text == "Green",
                    "Refiltering lost selection or overwrote draft");
                search.Text = "dark";
                DropPumpUntil(() => Equals(resources.Items.Cast<string>().SingleOrDefault(), "Dark · Color · Swatch"),
                    "resolve a key in the selected theme");
                RequireCapability(resources.SelectedIndex == -1, "Selection crossed theme boundaries");
                filter.SelectedItem = "String";
                DropPumpUntil(() => resources.ItemCount == 0, "resource filters with empty intersection");
                search.Text = "";
                DropPumpUntil(() => resources.ItemCount == 1, "clearing text retains the resource category");
                filter.SelectedIndex = 0;
                DropPumpUntil(() => resources.ItemCount == declarations.Length, "restore unfiltered declarations");
                RequireCapability(vm.AppearanceDraft().Resources.SequenceEqual(declarations), "Filtering mutated the dictionary");
            }
            finally { editor.Close(); Dispose5AProfileVm(vm); }
        });

        check("visual resource CRUD, search and duplicate validation", () =>
        {
            var vm = GridFirstVm();
            var editor = new FormStyleEditorWindow(vm);
            try
            {
                editor.Show();
                editor.UpdateLayout();
                editor.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
                editor.UpdateLayout();
                var fields = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
                var key = fields.Single(b => b.Watermark == "Ключ");
                var value = fields.Single(b => b.Watermark == "Значение ресурса");
                var search = fields.Single(b => b.Watermark == "Поиск по ключу, типу или теме");
                var resources = editor.GetVisualDescendants().OfType<ListBox>().Single();
                void Click(string text)
                {
                    var button = editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    editor.UpdateLayout();
                }
                key.Text = "AccentEditor"; value.Text = "Red";
                Click("Добавить ресурс");
                RequireCapability(vm.AppearanceDraft().Resources.Single() == new FormResource("AccentEditor", "SolidColorBrush", "Red"), "Wrong resource from editor");
                Click("Добавить ресурс");
                RequireCapability(vm.AppearanceDraft().Resources.Count == 1 && editor.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text?.Contains("Ключ уже существует", StringComparison.Ordinal) == true), "Duplicate validation missing");
                resources.SelectedIndex = 0; key.Text = "AccentRenamed"; value.Text = "Blue";
                Click("Сохранить ключ и значение");
                RequireCapability(vm.AppearanceDraft().Resources.Single() == new FormResource("AccentRenamed", "SolidColorBrush", "Blue"), "Rename/edit failed");
                search.Text = "missing";
                DropPumpUntil(() => resources.ItemCount == 0, "resource search excludes nonmatching rows");
                search.Text = "renamed";
                DropPumpUntil(() => resources.ItemCount == 1, "resource search restores matching row");
                resources.SelectedIndex = 0;
                Click("Удалить ресурс");
                RequireCapability(vm.AppearanceDraft().Resources.Count == 0, "Deletion failed");
            }
            finally { editor.Close(); Dispose5AProfileVm(vm); }
        });
    }
}
