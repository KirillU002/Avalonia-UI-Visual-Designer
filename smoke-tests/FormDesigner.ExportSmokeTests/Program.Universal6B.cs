using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel StyleScene(string layout)
    {
        var vm = GridFirstVm();
        if (layout == "Canvas") { vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice; vm.NewProjectEditorCommand!.Execute(null); }
        var button = vm.CreateControl("Button",0,0); button.Name="SaveButton"; vm.SelectSingleControl(button);
        Commit6A(vm,PropertyValueKind.Unset,"","Background");
        Commit6A(vm,PropertyValueKind.Unset,"","CornerRadius");
        var input = vm.CreateControl("TextBox",0,0); input.Name="NameInput"; vm.SelectSingleControl(input);
        Commit6A(vm,PropertyValueKind.Unset,"","FontSize");
        var sheet = new FormAppearance();
        sheet.Classes[button.Id]="primary";
        sheet.Resources.AddRange(new FormResource[] {
            new("Accent6B","SolidColorBrush","#123456","Light"), new("Accent6B","SolidColorBrush","#ABCDEF","Dark"),
            new("Accent6B","SolidColorBrush","#654321","Default"),new("TextSize6B","Double","23"),
            new("Greeting6B","String","A & B"),new("Spacing6B","Thickness","1,2,3,4"),
            new("Weight6B","FontWeight","Bold"),new("Family6B","FontFamily","Arial"),
            new("Corner6B","CornerRadius","9"),new("Color6B","Color","Red")
        });
        sheet.Styles.Add(new("Button","Button.primary",new(){new("Background","{DynamicResource Accent6B}")}));
        sheet.Styles.Add(new("Button","#SaveButton",new(){new("CornerRadius","9")}));
        sheet.Styles.Add(new("TextBox","TextBox",new(){new("FontSize","{StaticResource TextSize6B}")}));
        vm.SaveAppearance(vm.ActiveDocumentId,sheet);
        return vm;
    }

    private static int StyleProjects(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var layout in new[]{"Grid","Canvas"})
        {
            var vm=StyleScene(layout);
            var container = vm.CreateControl("StackPanel", 0, 0);
            container.Name = "StyleStack";
            var appearance = vm.AppearanceDraft();
            foreach (var type in new[] { "TextBlock", "CheckBox", "Border" })
            {
                var child = vm.CreateControl(type, 0, 0, container.Id);
                child.Name = type + "InStack";
                vm.SelectSingleControl(child);
                Commit6A(vm, PropertyValueKind.Unset, "", "Opacity");
                appearance.Styles.Add(new(type, type + "#" + child.Name, new() { new("Opacity", "0.64") }));
            }
            vm.SaveAppearance(vm.ActiveDocumentId, appearance);
            var file=Path.Combine(folder,layout+".json");
            File.WriteAllText(file,vm.ExportWorkspaceJson());
            var copy=GridFirstVm(); copy.LoadDocumentJson(File.ReadAllText(file));
            Pump(copy.ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));
            Dispose5AProfileVm(copy);Dispose5AProfileVm(vm);
        }
        return 0;
    }

    private static void AssertStyleResourceIpc(SmokeContext context)
    {
        const string source = """
            <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:custom="using:External">
              <Window.Resources><x:Double x:Key='TitleSize'>14</x:Double></Window.Resources>
              <Window.Styles><Style Selector='Button'><Setter Property='CornerRadius' Value='2'/></Style></Window.Styles>
              <Grid><!-- KEEP --><Button custom:Unknown="KEEP" Content="{Binding Keep}"/></Grid>
            </Window>
            """;
        var vm = context.ViewModel;
        AssertAxamlIpcEdits(context, source,
            new[] { ("Window", "TitleSize", "17"), ("Button", "CornerRadius", "5") },
            (scope, key, value) => vm.SaveSourceStyleValue(vm.ActiveDocumentId,
                vm.StyleFields.Single(field => scope == "Window" ? field.Resource?.Key == key : field.Target == scope && field.Property == key), value),
            source.Replace(">14<", ">17<").Replace("Value='2'", "Value='5'"));
    }

    private const string SourceStyles = """
        <Window.Resources><ResourceDictionary><SolidColorBrush x:Key='Paint' Color='#112233'/>
        <ResourceDictionary.ThemeDictionaries><ResourceDictionary x:Key='Dark'><SolidColorBrush x:Key='Paint' Color='#111111'/></ResourceDictionary></ResourceDictionary.ThemeDictionaries>
        <ControlTheme x:Key='T' TargetType='Button'><Setter Property='Template'><ControlTemplate><u:Unknown/></ControlTemplate></Setter></ControlTheme>
        <DataTemplate x:Key='D'><TextBlock Text='{Binding Keep}'/></DataTemplate></ResourceDictionary></Window.Resources>
        <Window.Styles><Style Selector='Button.primary'><Setter Property='CornerRadius' Value='6'/></Style>
        <!-- preserved --><Style Selector='Button:pointerover'><Setter Property='Background' Value='{DynamicResource Paint}'/></Style></Window.Styles>
        <Grid><Button Name='SaveButton' Classes='primary' u:Keep='yes' Content='{Binding Name}'/></Grid>
        """;

    private static int Run6BChecks()
    {
        int passed=0, failed=0;
        void Check(string name,Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS "+name); }
            catch(Exception ex){ failed++; Console.WriteLine("FAIL "+name+" "+ex); }
        }
        void Reject(Action action)
        {
            try { action(); } catch(InvalidOperationException) { return; }
            throw new Exception("Expected validation to reject input.");
        }
        foreach(var selector in new[]{"Button","Button.primary","Button#SaveButton","#SaveButton"})
            Check("selector "+selector,()=>FormStyleAuthoring.SelectorSuffix("Button",selector));
        foreach(var selector in new[]{"Button:focus","Button > TextBox","Button.child.more","u|MyButton",".class"})
            Check("opaque selector "+selector,()=>Reject(()=>FormStyleAuthoring.SelectorSuffix("Button",selector)));
        Check("metadata applicability",()=>RequireCapability(FormStyleAuthoring.Properties("TextBlock").All(p=>p.Key!="CornerRadius") && FormStyleAuthoring.Properties("Button").Any(p=>p.Key=="CornerRadius"),"Metadata mismatch"));
        Check("duplicate key",()=>{ var s=new FormAppearance(); s.Resources.Add(new("A","Double","4"));s.Resources.Add(new("A","Double","5"));Reject(()=>FormStyleAuthoring.Validate(s)); });
        Check("incompatible resource",()=>{var s=new FormAppearance();s.Resources.Add(new("Brush","SolidColorBrush","Blue"));Reject(()=>FormStyleAuthoring.CheckSetter(s,"Button",new("FontSize","{StaticResource Brush}")));});
        Check("binding setter protected",()=>Reject(()=>FormStyleAuthoring.CheckSetter(new(),"Button",new("Foreground","{Binding Name}"))));
        Check("custom resource blocked",()=>Reject(()=>FormStyleAuthoring.ResourceValue(new("A","u:Converter","test"))));
        Check("version guard",()=>Reject(()=>FormStyleAuthoring.Validate(new(){Version=2})));
        Check("JSON migration",()=>{var vm=GridFirstVm();var json=vm.ExportWorkspaceJson();RequireCapability(!json.Contains("\"Appearance\""),"Unexpected migration");vm.LoadDocumentJson(json);RequireCapability(vm.AppearanceDraft().Resources.Count==0,"Old document");});
        Check("unsupported appearance rejected before changing the workspace",()=>{
            var vm = StyleScene("Grid");
            var document = vm.ActiveDocumentId;
            var original = JsonSerializer.Serialize(vm.AppearanceDraft());
            foreach (var invalid in new[] { """{"Version":2}""", """{"Version":1,"Styles":null}""" })
                Reject(()=>vm.LoadDocumentJson("""{"Appearance":""" + invalid + "}"));
            RequireCapability(vm.ActiveDocumentId == document && JsonSerializer.Serialize(vm.AppearanceDraft()) == original,
                "Failed load changed the current workspace");
        });
        Check("style and resource history",()=>{
            var vm=StyleScene("Grid"); var before=JsonSerializer.Serialize(vm.AppearanceDraft());
            void Mutation(Action<FormAppearance> action)
            {
                var s=vm.AppearanceDraft();action(s);vm.SaveAppearance(vm.ActiveDocumentId,s);
                var after=JsonSerializer.Serialize(vm.AppearanceDraft());GridEditorUndo(vm);
                RequireCapability(JsonSerializer.Serialize(vm.AppearanceDraft())==before,"Undo lost original");GridEditorUndo(vm,true);
                RequireCapability(JsonSerializer.Serialize(vm.AppearanceDraft())==after,"Redo differs");before=after;
            }
            Mutation(s=>s.Styles.Add(new("Button","Button",new())));
            Mutation(s=>s.Styles[^1].Setters.Add(new("Padding","2")));
            Mutation(s=>s.Styles[^1].Setters.Add(new("Margin","3")));
            Mutation(s=>s.Styles[^1].Setters.Reverse());
            Mutation(s=>s.Styles[^1].Setters[0]=new("Margin","4"));
            Mutation(s=>s.Styles[^1].Setters.RemoveAt(0));
            Mutation(s=>s.Styles.RemoveAt(s.Styles.Count-1));
            Mutation(s=>s.Resources.Add(new("Temp","String","v")));
            Mutation(s=>s.Resources[^1]=s.Resources[^1] with{Key="Renamed",Value="a&b"});
            Mutation(s=>s.Resources.RemoveAt(s.Resources.Count-1));
            var saved=vm.ExportWorkspaceJson();var copy=GridFirstVm();copy.LoadDocumentJson(saved);
            RequireCapability(JsonSerializer.Serialize(copy.AppearanceDraft())==before,"Reopen differs");
        });
        foreach (var label in new[]{"Button.primary · CornerRadius","Window · Paint (SolidColorBrush)","Dark · Paint (SolidColorBrush)"})
            Check("source patch and ACK "+label,()=>{
                var vm=Universal4AVm(SourceStyles);var original=vm.CreateActiveAxamlPatch().PatchedText;
                var field=vm.StyleFields.Single(f=>f.Label==label);
                var value=field.Resource==null?"7":"#AABBCC";
                vm.SaveSourceStyleValue(vm.ActiveDocumentId,field,value);
                var patch=vm.CreateActiveAxamlPatch();
                var expected=original.Remove(field.Span.Start,field.Span.Length).Insert(field.Span.Start,value);
                RequireCapability(patch.CanApply && patch.Edits.Count==1 && patch.PatchedText==expected,"Patch changed neighboring AXAML");
                vm.MarkAxamlRoundTripSaved("style.axaml",expected,vm.ActiveSession.CurrentSnapshot);
                GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo after ACK");
                GridEditorUndo(vm,true);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==expected,"Redo after ACK");
            });
        Check("template and scope guards",()=>{
            var vm=Universal4AVm(SourceStyles.Replace("<Grid>","<Grid><Grid.Resources><SolidColorBrush x:Key='Paint' Color='Yellow'/></Grid.Resources>"));
            RequireCapability(vm.StyleFields.Count==3 && vm.StyleNotices().Any(s=>s.Contains("ControlTheme")) && vm.StyleNotices().Any(s=>s.Contains("DataTemplate")),"Unsafe scope or missing metadata");
        });
        foreach(var mode in new[]{false,true}) Check("native preview "+mode,()=>{
            var vm=StyleScene("Grid");using var preview=ShowDataGridRegressionPreview(vm,mode);
            var button=preview.Window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="SaveButton");
            var input=preview.Window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="NameInput");
            RequireCapability(button.CornerRadius.TopLeft==9 && input.FontSize==23,"Style missing "+button.CornerRadius+" / "+input.FontSize);
            RequireCapability(button.Background is ISolidColorBrush b && b.Color==Color.Parse("#123456"),"Wrong theme brush: "+button.Background);
        });
        Check("visual editor add style and setter",()=>{
            var vm=GridFirstVm();var editor=new FormStyleEditorWindow(vm);
            editor.Show();editor.UpdateLayout();
            try
            {
                void Click(string title)
                {
                    var button=editor.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,title));
                    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    editor.UpdateLayout();
                }
                Click("Добавить стиль");
                var property=editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Items.Cast<object>().Any(x=>Equals(x,"CornerRadius")));
                property.SelectedItem="CornerRadius"; editor.UpdateLayout();
                var input=editor.GetVisualDescendants().OfType<FormDesigner.Controls.PropertyGridTextEditor>().First();
                input.Focus();input.Text="11";property.Focus();
                Click("Добавить Setter");
                RequireCapability(vm.AppearanceDraft().Styles.Single().Setters.Single()==new FormStyleSetter("CornerRadius","11"),"Visual setter commit");
                Click("Удалить стиль");
                RequireCapability(vm.AppearanceDraft().Styles.Count==0,"Visual delete");
            }
            finally {editor.Close();}
        });
        Check("source stale and exact opaque preservation",()=>{
            var vm=Universal4AVm(SourceStyles);
            var f=vm.StyleFields.First();
            vm.SaveSourceStyleValue(vm.ActiveDocumentId,f,f.Resource==null ? "7" : "#998877");
            Reject(()=>vm.SaveSourceStyleValue(vm.ActiveDocumentId,f,"Red"));
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("<ControlTemplate><u:Unknown/></ControlTemplate>"),"Template lost");
        });
        Check("foreign default selector namespace is read-only",()=>{
            var source = "<Window xmlns='https://github.com/avaloniaui' xmlns:a='https://github.com/avaloniaui'>" +
                "<a:Window.Styles xmlns='using:External'><a:Style Selector='Button'><a:Setter Property='Opacity' Value='0.8'/></a:Style></a:Window.Styles><Grid/></Window>";
            var catalog = AxamlStyleValues.Read(AxamlSyntaxDocument.Parse(source));
            RequireCapability(catalog.Fields.Count == 0 && catalog.Notices.Count > 0, "Foreign selector treated as standard");
        });
        Run6BUiChecks(Check);
        Check("Designer native values",()=>{
            var vm=StyleScene("Grid");var window=CreateSmokeWindow(vm);window.Show();
            try
            {
                vm.CloseStartScreenCommand.Execute(null);window.UpdateLayout(); NativeItemsUiWait(80);
                var button=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Classes.Contains("primary") && c.Name=="SaveButton");
                RequireCapability(button.CornerRadius.TopLeft==9 && button.Background is ISolidColorBrush b && b.Color==Color.Parse("#123456"),"Designer local/style mismatch "+button.CornerRadius+" / "+button.Background);
            }
            finally{window.CloseForExternalHost();SmokeWindows.Clear();}
        });
        Check("imported native values",()=>{
            var vm=Universal4AVm(SourceStyles);using var preview=ShowDataGridRegressionPreview(vm,true);
            RequireCapability(preview.Window.GetVisualDescendants().OfType<Button>().Any(b=>b.Classes.Contains("primary") && b.CornerRadius.TopLeft==6),"Imported Setter missing");
        });
        Check("editor release",()=>{
            var vm=GridFirstVm();
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            static WeakReference Open(MainWindowViewModel vm){var window=new FormStyleEditorWindow(vm);window.Show();window.UpdateLayout();window.Close();return new WeakReference(window);}
            var weak=Open(vm);Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            RequireCapability(!weak.IsAlive,"Closed editor retained");
        });
        CheckStyleSourceSafety(Check);
        Console.WriteLine($"PHASE6B {passed}/{passed+failed}");return failed==0?0:1;
    }
}
