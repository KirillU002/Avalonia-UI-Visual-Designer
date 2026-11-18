using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void CheckStyleSourceSafety(Action<string,Action> check)
    {
        check("opaque resource entries and theme scopes have read-only notices", () =>
        {
            const string body = """
                <Window.Resources><ResourceDictionary>
                  <x:String x:Key="Editable">EDIT-ME</x:String>
                  <x:String x:Key="Commented">a<!-- keep -->b</x:String>
                  <x:String x:Key="SelfClosing"/>
                  <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="FutureTheme"><x:String x:Key="Accent">opaque</x:String></ResourceDictionary>
                    <ResourceDictionary x:Key="Dark"><x:String x:Key="Accent">first</x:String></ResourceDictionary>
                    <ResourceDictionary x:Key="Dark"><x:String x:Key="Accent">second</x:String></ResourceDictionary>
                  </ResourceDictionary.ThemeDictionaries>
                </ResourceDictionary></Window.Resources>
                <Grid/>
                """;
            var vm = Universal4AVm(body);
            try
            {
                var field = vm.StyleFields.Single();
                RequireCapability(field.Resource?.Key == "Editable", "Opaque entries must not be editable");
                var notices = vm.StyleNotices().ToArray();
                foreach (var key in new[] { "Commented", "SelfClosing", "FutureTheme", "Dark" })
                    RequireCapability(notices.Count(n => n.Contains(key) && n.Contains("только чтение")) == 1, "Missing or duplicate notice for " + key);
                var original = vm.CreateActiveAxamlPatch().PatchedText;
                vm.SaveSourceStyleValue(vm.ActiveDocumentId, field, "changed");
                var patch = vm.CreateActiveAxamlPatch();
                RequireCapability(patch.CanApply && patch.Edits.Count == 1 && patch.PatchedText == original.Replace("EDIT-ME", "changed"),
                    "A scalar edit changed read-only markup or neighbouring theme dictionaries");
            }
            finally { Dispose5AProfileVm(vm); }
        });

        check("XML lexemes survive scalar edit, ACK, Undo and Redo", () =>
        {
            const string body = """
                <Window.Resources>
                  <x:String x:Key="Editable">A &amp; B's</x:String>
                  <x:String x:Key="Numeric">A&#x42;C</x:String>
                  <x:String x:Key="Extension" u:Key="user">keep</x:String>
                </Window.Resources>
                <Window.Styles><Style Selector="Button"><Setter Property="FontSize" Value="&#x31;7"/></Style></Window.Styles>
                <Grid><Button/></Grid>
                """;
            var vm = Universal4AVm(body);
            var original = vm.CreateActiveAxamlPatch().PatchedText;
            var field = vm.StyleFields.Single();
            RequireCapability(field.Resource?.Key == "Editable" && field.Value == "A & B's", "Unexpected source capability");
            RequireCapability(vm.StyleNotices().Any(n => n.Contains("Numeric") && n.Contains("точного сохранения")), "Missing XML spelling notice");
            vm.SaveSourceStyleValue(vm.ActiveDocumentId, field, "C < D");
            var changed = original.Replace("A &amp; B's", "C &lt; D");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == changed, "Scalar edit changed a protected lexeme");
            vm.MarkAxamlRoundTripSaved("style.axaml", changed, vm.ActiveSession.CurrentSnapshot);
            GridEditorUndo(vm);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == original, "Undo lost XML spelling");
            GridEditorUndo(vm, true);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == changed, "Redo lost XML spelling");
        });

        check("empty string insertion after ACK", () =>
        {
            var vm = Universal4AVm("<Window.Resources><x:String x:Key='Text'>old</x:String></Window.Resources><Grid/>");
            var original = vm.CreateActiveAxamlPatch().PatchedText;
            vm.SaveSourceStyleValue(vm.ActiveDocumentId, vm.StyleFields.Single(), "");
            var patch = vm.CreateActiveAxamlPatch();
            RequireCapability(patch.PatchedText == original.Replace(">old</", "></") && vm.StyleFields.Single().Value == "", "Empty resource span lost");
            vm.MarkAxamlRoundTripSaved("style.axaml", patch.PatchedText, vm.ActiveSession.CurrentSnapshot);
            GridEditorUndo(vm);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == original, "Undo from empty AXAML content failed");
        });

        foreach (var axaml in new[] { false, true })
        {
            check("native class precedence over later type and name styles " + axaml, () =>
            {
                var vm = StyleScene("Grid");
                var sheet = vm.AppearanceDraft();
                foreach (var (selector,value) in new[] { ("Button.primary","12"), ("Button","3"), ("#SaveButton","2") })
                    sheet.Styles.Add(new FormStyle("Button", selector, new() { new("CornerRadius",value) }));
                vm.SaveAppearance(vm.ActiveDocumentId, sheet);
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var button = preview.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton");
                RequireCapability(button.CornerRadius.TopLeft == 12, "Avalonia selector priority lost: " + button.CornerRadius);
            });
        }
    }
}
