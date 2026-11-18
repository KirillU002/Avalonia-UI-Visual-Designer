using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Media;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel Json6A(string type="TextBlock")
    {var vm=GridFirstVm();var c=vm.CreateControl(type,0,0);c.Name="Target6A";vm.SelectSingleControl(c);return vm;}
    private static PropertyGridRowViewModel Row6A(MainWindowViewModel vm,string key="Text")=>GetPropertyGridRow(vm,key);
    private static void Commit6A(MainWindowViewModel vm,PropertyValueKind kind,string text,string key="Text")
    {RequireCapability(vm.CommitPropertyExpression(Row6A(vm,key),new(kind,text),out var error),error+"; "+key+"; "+vm.StatusText);}
    private static int RunUniversal6AChecks()
    {
        int passed=0,failed=0;
        void Check(string name,Action test)
        {
            var filter=Environment.GetEnvironmentVariable("UNIVERSAL6A_FILTER");if(filter is not null&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{test();passed++;Console.WriteLine("PASS UNIVERSAL6A_"+name);}
            catch(Exception e){failed++;Console.WriteLine("FAIL UNIVERSAL6A_"+name+": "+e);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}
        }
        Check("InstalledBindingApi",()=>{foreach(var key in new[]{"Path","Mode","UpdateSourceTrigger","ElementName","RelativeSource","FallbackValue","TargetNullValue","StringFormat","Converter"})RequireCapability(typeof(Binding).GetProperty(key)is not null,"Not an installed API: "+key);});
        foreach(var mode in Enum.GetNames<BindingMode>())Check("Mode_"+mode,()=>{RequireCapability(PropertyExpressions.TryBinding(new("User.Name",mode),out var text,out _),"Mode rejected");RequireCapability(PropertyExpressions.TryParseBinding(text,out var b)&&b.Path=="User.Name"&&b.Mode==mode,"Mode lost");});
        foreach(var trigger in Enum.GetNames<UpdateSourceTrigger>())Check("Trigger_"+trigger,()=>{var accepted=PropertyExpressions.TryBinding(new("Name",UpdateSourceTrigger:trigger),out var text,out _);RequireCapability(accepted==(trigger=="Default"||PropertyExpressions.SupportsMarkupUpdateSourceTrigger),"Installed markup capability mismatch");if(accepted)RequireCapability(PropertyExpressions.TryParseBinding(text,out var b)&&b.UpdateSourceTrigger==trigger,"Trigger lost");});
        foreach(var path in new[]{"",".","UserName","User.Name","Customers[0].Name"})Check("Path_"+path,()=>RequireCapability(PropertyExpressions.TryBinding(new(path),out var text,out _)&&PropertyExpressions.TryParseBinding(text,out _),"Valid path rejected"));
        foreach(var bad in new[]{"{Binding Call()}","{Binding User, Unknown=1}","{Binding User, Mode=Bad}","{Binding User, Path=Other}","{Binding User, Converter={local:Execute}}","{Binding User, FallbackValue={StaticResource Nested}}","{Binding User, Source={x:Static Evil}}","{CompiledBinding Name}","{MultiBinding}"})Check("Rejected_"+bad,()=>RequireCapability(!PropertyExpressions.TryParseBinding(bad,out _),"Unsafe binding parsed"));
        Check("BindingParameters",()=>{var b=new BindingEditorValue("User.Name","TwoWay","Default","Input",FallbackValue:"A, B & <C>",TargetNullValue:"(null)",StringFormat:"Value: {0}",ConverterKey:"Convert");RequireCapability(PropertyExpressions.TryBinding(b,out var text,out _)&&PropertyExpressions.TryParseBinding(text,out var restored)&&restored==b,"Parameters changed");});
        foreach(var relative in new[]{"Self","TemplatedParent"})Check("Relative_"+relative,()=>{RequireCapability(PropertyExpressions.TryBinding(new("Name",RelativeSource:relative),out var text,out _)&&PropertyExpressions.TryParseBinding(text,out var b)&&b.RelativeSource==relative,"Relative source lost");});
        Check("ConflictingSources",()=>RequireCapability(!PropertyExpressions.TryBinding(new("Name",ElementName:"Input",RelativeSource:"Self"),out _,out _),"Conflicting source accepted"));
        foreach(var kind in new[]{PropertyValueKind.Binding,PropertyValueKind.StaticResource,PropertyValueKind.DynamicResource})Check("JsonUndoRedo_"+kind,()=>{
            var vm=Json6A();var c=vm.SelectedControl!;var snapshot=vm.ActiveSession.UndoSnapshots.Count;
            var text=kind==PropertyValueKind.Binding?"{Binding UserName, Mode=OneWay}":"{"+kind+" ThemeTextBrush}";
            Commit6A(vm,kind,text);RequireCapability(vm.ActiveSession.UndoSnapshots.Count==snapshot+1,"Not atomic");
            var json=vm.ExportWorkspaceJson();var path=Path.GetFullPath("artifacts/universal-phase6a/json-"+kind+".json");File.WriteAllText(path,json);
            var clone=GridFirstVm();clone.LoadDocumentJson(File.ReadAllText(path));RequireCapability(PropertyExpressions.TryRead(clone.Controls.Single().CustomProperties,"Text",out var value)&&value.Text==text&&value.Kind==kind,"JSON lost semantics");
            GridEditorUndo(vm);RequireCapability(!vm.Controls.Single().CustomProperties.Any(p=>p.Key==PropertyExpressions.Prefix+"Text"),"Undo retained expression");GridEditorUndo(vm,true);
            RequireCapability(PropertyExpressions.TryRead(vm.Controls.Single().CustomProperties,"Text",out value)&&value.Text==text,"Redo lost expression");
        });
        foreach(var initial in new[]{"Old &amp; value","{Binding UserName, Mode=OneWay}","{StaticResource Accent}","{DynamicResource Accent}"})Check("SourceRoundTrip_"+initial,()=>{
            var vm=Universal4AVm("<a:TextBlock Name='Target6A' Text=\""+initial+"\" u:Keep='&amp;raw'/> <!-- neighbour -->");vm.SelectSingleControl(vm.Controls.Single());var original=vm.CreateActiveAxamlPatch().PatchedText;
            Commit6A(vm,PropertyValueKind.Binding,"{Binding Path=UserName, Mode=TwoWay, FallbackValue='A & B'}");var patch=vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("u:Keep='&amp;raw'")&&patch.PatchedText.Contains("<!-- neighbour -->"),"Nonminimal/damaged patch");
            var edited=patch.PatchedText;RequireCapability(vm.ResetPropertyExpression(Row6A(vm)),"Reset blocked");var reset=vm.CreateActiveAxamlPatch().PatchedText;RequireCapability(!reset.Contains(" Text="),"Binding reset not deleted");
            GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==edited,"Undo Reset changed text");GridEditorUndo(vm,true);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==reset,"Redo Reset changed text");
            GridEditorUndo(vm);GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo not exact");
        });
        foreach(var initial in new[]{"Old","{Binding Original}"})Check("UndoAfterAck_"+initial,()=>{
            var vm=Universal4AVm("<a:TextBlock Name='Target6A' Text='"+initial+"'/>");vm.SelectSingleControl(vm.Controls.Single());
            Commit6A(vm,PropertyValueKind.Binding,"{Binding Changed}");var applied=vm.CreateActiveAxamlPatch().PatchedText;vm.MarkAxamlRoundTripSaved("ack6a.axaml",applied,vm.ActiveSession.CurrentSnapshot);
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"ACK phantom edit");GridEditorUndo(vm);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("Text='"+initial+"'"),"Undo after ACK lost original expression/literal");GridEditorUndo(vm,true);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==applied,"Redo after ACK differs");
        });
        Check("BindingToLiteral",()=>{var vm=Universal4AVm("<a:TextBlock Name='Target6A' Text='{Binding Keep}'/>");vm.SelectSingleControl(vm.Controls.Single());Commit6A(vm,PropertyValueKind.Literal,"Literal & <safe>");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("Text='Literal &amp; &lt;safe&gt;'"),"Literal switch lost escaping");});
        foreach(var expression in new[]{"{CompiledBinding Name}","{MultiBinding}","{TemplateBinding Content}","{Binding Path=Name, ConverterParameter={local:Unknown}}"})Check("Opaque_"+expression,()=>{
            var vm=Universal4AVm("<a:TextBlock Name='Target6A' Text='"+expression+"'/>");vm.SelectSingleControl(vm.Controls.Single());var original=vm.CreateActiveAxamlPatch().PatchedText;
            RequireCapability(!Row6A(vm).CanEditExpression&&Row6A(vm).IsReadOnly,"Opaque editable");RequireCapability(!vm.CommitPropertyExpression(Row6A(vm),new(PropertyValueKind.Binding,"{Binding Replacement}"),out _)&&vm.CreateActiveAxamlPatch().PatchedText==original,"Opaque replaced");
        });
        Check("CompiledContext",()=>{var vm=Universal4AVm("<a:StackPanel x:CompileBindings='True'><a:TextBlock Name='Target6A' Text='Literal'/></a:StackPanel>");vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target6A"));RequireCapability(!vm.CommitPropertyExpression(Row6A(vm),new(PropertyValueKind.Binding,"{Binding Guess}"),out _),"Implicit compiled binding created");});
        Check("ScopedResourceKeys",()=>{var vm=Universal4AVm("<a:StackPanel><a:StackPanel.Resources><a:SolidColorBrush x:Key='Accent'>Red</a:SolidColorBrush></a:StackPanel.Resources><a:TextBlock Name='Target6A'/></a:StackPanel>");vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target6A"));RequireCapability(vm.OpenPropertyExpressionEditor(Row6A(vm))!.ResourceKeys.Contains("Accent"),"Scoped key missing");});
        Check("SourceMetadataNoIO",()=>{var vm=Json6A();var source=new BindingSourceModel{Name="Offline",Path="Rows",SourceKind="DLL",SourceAssemblyPath="Z:\\missing\\forbidden.dll",SourceConnectionString="Password=DO_NOT_COPY"};source.Fields.Add(new BindingFieldModel{Path="Name"});vm.BindingSources.Add(source);var context=vm.OpenPropertyExpressionEditor(Row6A(vm))!;RequireCapability(context.Paths.Contains("Rows[0].Name")&&!JsonSerializer.Serialize(context.Paths).Contains("DO_NOT_COPY"),"Schema/credential failure");});
        Check("LegacyBindingReplacement",()=>{var vm=Json6A("TextBox");vm.SelectedControl!.TextBindingPath="Legacy.Path";Commit6A(vm,PropertyValueKind.Binding,"{Binding NewPath}");RequireCapability(vm.SelectedControl.TextBindingPath==""&&!vm.SupportsFieldBinding(vm.SelectedControl),"Two competing binding systems");GridEditorUndo(vm);RequireCapability(vm.Controls.Single().TextBindingPath=="Legacy.Path","Legacy binding Undo lost");});
        Check("LazyEditorAndActualApply",()=>{var count=PropertyExpressionEditorWindow.CreationCount;var vm=Json6A();RequireCapability(PropertyExpressionEditorWindow.CreationCount==count,"Eager editor");var editor=new PropertyExpressionEditorWindow(vm,vm.OpenPropertyExpressionEditor(Row6A(vm))!);try{editor.ValueMode.SelectedItem="Binding";editor.PathEditor.Text="Name";editor.BindingModeEditor.SelectedItem="OneWay";RequireCapability(editor.Apply(),editor.ErrorText.Text??"Apply failed");RequireCapability(PropertyExpressions.TryRead(vm.SelectedControl!.CustomProperties,"Text",out var e)&&e.Kind==PropertyValueKind.Binding,"Editor didn't apply");}finally{editor.Close();}});
        Check("StaleEditor",()=>{var vm=Json6A();var row=Row6A(vm);vm.CreateControl("TextBox",0,0);RequireCapability(!vm.CommitPropertyExpression(row,new(PropertyValueKind.Binding,"{Binding Wrong}"),out _),"Stale row changed selection");});
        Check("MultiForm",()=>{var vm=Json6A();Commit6A(vm,PropertyValueKind.Binding,"{Binding First}");var id=vm.ActiveDocumentId;vm.CreateNewForm();var c=vm.CreateControl("TextBlock",0,0);vm.SelectSingleControl(c);Commit6A(vm,PropertyValueKind.DynamicResource,"{DynamicResource Second}");vm.SetActiveForm(id,"6a");RequireCapability(PropertyExpressions.TryRead(vm.Controls.Single().CustomProperties,"Text",out var e)&&e.Text=="{Binding First}","Form states mixed");});
        Check("CommandReferences",()=>{var vm=Json6A("Button");Commit6A(vm,PropertyValueKind.Binding,"{Binding SaveCommand}","Command");vm.GenerateXaml();RequireCapability(vm.GeneratedXaml.Contains("Command=\"{Binding SaveCommand}\"")&&!vm.GeneratedCSharp.Contains("SaveCommand"),"Command became generated handler/fake VM");});
        Check("InvalidPayloadPreservedAndExportStopped",()=>{var vm=Json6A();vm.SelectedControl!.CustomProperties.Add(new(){Key=PropertyExpressions.Prefix+"Text",ValueJson="{\"Version\":9,\"Kind\":1,\"Text\":\"{Binding X}\"}"});var output=Path.GetFullPath("artifacts/universal-phase6a/invalid-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"sentinel"),"keep");var stopped=false;try{Pump(vm.ExportCurrentResultToProjectAsync(output));}catch(NotSupportedException){stopped=true;}RequireCapability(stopped&&Directory.GetFiles(output).Length==1,"Partial invalid export");});
        Check("UnknownStaticResourceAtomic",()=>{var vm=Json6A();Commit6A(vm,PropertyValueKind.StaticResource,"{StaticResource MissingKey}");var output=Path.GetFullPath("artifacts/universal-phase6a/static-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"sentinel"),"keep");var stopped=false;try{Pump(vm.ExportCurrentResultToProjectAsync(output));}catch(InvalidOperationException){stopped=true;}RequireCapability(stopped&&Directory.GetFiles(output).Length==1,"Missing resource published partial output");});
        foreach(var mode in new[]{false,true})Check("SafePreview_"+mode,()=>{var vm=Json6A();Commit6A(vm,PropertyValueKind.Binding,"{Binding UserName}");using var p=ShowDataGridRegressionPreview(vm,mode);var native=p.Window.GetVisualDescendants().OfType<TextBlock>().First(c=>c.Name=="Target6A");RequireCapability(native.Text!="{Binding UserName}"&&native.Text!="TextBlock","Fake binding value in Preview");});
        Check("AlternateCompiledPrefix",()=>{var vm=Universal4AVm("<a:StackPanel xmlns:d='http://schemas.microsoft.com/winfx/2006/xaml' d:CompileBindings='True'><a:TextBlock Name='Target6A' Text='Literal'/></a:StackPanel>");vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target6A"));RequireCapability(!vm.CommitPropertyExpression(Row6A(vm),new(PropertyValueKind.Binding,"{Binding Guess}"),out _),"Alternate compiled namespace bypassed");});
        Check("SourceSchemaAssociation",()=>{var vm=Json6A();var source=new BindingSourceModel{Path="Rows"};source.Fields.Add(new(){Path="Name"});vm.BindingSources.Add(source);Commit6A(vm,PropertyValueKind.Binding,"{Binding Rows[0].Name}");RequireCapability(vm.SelectedControl!.BindingSourceId==source.Id,"Existing source identity not reused");});
        Check("NestedContentGuard",()=>{var vm=Universal4AVm("<a:Button Name='Target6A'><a:Button.Content><a:TextBlock Text='Child'/></a:Button.Content></a:Button>");vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target6A"));RequireCapability(!Row6A(vm).CanEditExpression,"Nested Content can be replaced implicitly");});
        Check("SourceStructureComposition",()=>{var vm=Universal4AVm("<a:StackPanel Name='Host'><a:TextBlock Name='Target6A' Text='Old'/><a:TextBlock Name='Other' Text='Keep'/></a:StackPanel>");vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target6A"));Commit6A(vm,PropertyValueKind.Binding,"{Binding Name}");RequireCapability(vm.ReorderAxamlControl(vm.SelectedControl!.Id,1),vm.StatusText);var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.PatchedText.Contains("{Binding Name}")&&patch.PatchedText.IndexOf("Name='Other'",StringComparison.Ordinal)<patch.PatchedText.IndexOf("Name='Target6A'",StringComparison.Ordinal),"Structure/expression composition lost state");});
        Check("InvalidXmlLiteral",()=>{var vm=Json6A();RequireCapability(!vm.CommitPropertyExpression(Row6A(vm),new(PropertyValueKind.Literal,"bad"+(char)1),out _),"Invalid XML character accepted");});
        Check("LiteralPreview",()=>{var vm=Json6A();Commit6A(vm,PropertyValueKind.Binding,"{Binding Unknown}");Commit6A(vm,PropertyValueKind.Literal,"Visible literal");foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))RequireCapability(preview.Window.GetVisualDescendants().OfType<TextBlock>().Any(c=>c.Name=="Target6A"&&c.Text=="Visible literal"),"Literal not projected");});
        Check("TrustedNativeRuntime",Runtime6A);
        Check("ConflictPreservesSource",()=>{var vm=Universal4AVm("<a:TextBlock Name='Target6A' Text='Old'/>");vm.SelectSingleControl(vm.Controls.Single());Commit6A(vm,PropertyValueKind.Binding,"{Binding Name}");RequireCapability(!vm.CreateActiveAxamlPatch(vm.ActiveAxamlSourceDocument!.OriginalText+"<!--external-->").CanApply,"Stale source overwritten");});
        Check("EditorMemory",()=>{var vm=Json6A();var refs=Enumerable.Range(0,5).Select(_=>CloseExpression6A(vm)).ToArray();for(var i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();NativeItemsUiWait(40);}RequireCapability(refs.All(r=>!r.IsAlive),"Closed expression editor retained");});
        Console.WriteLine($"UNIVERSAL6A_CHECKS passed={passed}; failed={failed}");return failed==0?0:1;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CloseExpression6A(MainWindowViewModel vm)
    {var w=new PropertyExpressionEditorWindow(vm,vm.OpenPropertyExpressionEditor(Row6A(vm))!);w.Show();NativeItemsUiWait(20);w.Close();return new(w);}
    private static void Runtime6A()
    {
        var xml="<Window xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Window.Resources><SolidColorBrush x:Key='Accent' Color='Red'/></Window.Resources><StackPanel><TextBox Name='Input' Text='{Binding Name, Mode=TwoWay}'/><TextBlock Name='Output' Text='{Binding Name, Mode=OneWay}'/><Border Name='Static' Background='{StaticResource Accent}'/><Border Name='Dynamic' Background='{DynamicResource Accent}'/><Button Name='Command' Command='{Binding SaveCommand}'/><TextBlock Name='Named' Text='{Binding Text, ElementName=Input}'/></StackPanel></Window>";
        var window=(Window)RuntimeAxamlPreviewLoader.Load(xml);var model=new Native6AViewModel();window.DataContext=model;
        try{window.Show();NativeItemsUiWait(80);window.UpdateLayout();var controls=window.GetVisualDescendants().OfType<Control>().Where(c=>!string.IsNullOrEmpty(c.Name)).GroupBy(c=>c.Name!).ToDictionary(g=>g.Key,g=>g.First());
            RequireCapability(((TextBlock)controls["Output"]).Text=="Initial","Binding initial failed");model.Name="Changed";NativeItemsUiWait(30);RequireCapability(((TextBlock)controls["Output"]).Text=="Changed","OneWay failed");
            ((TextBox)controls["Input"]).Text="Edited";NativeItemsUiWait(30);RequireCapability(model.Name=="Edited"&&((TextBlock)controls["Named"]).Text=="Edited","TwoWay/ElementName failed");
            window.Resources["Accent"]=Brushes.Blue;NativeItemsUiWait(30);RequireCapability(((Border)controls["Static"]).Background is ISolidColorBrush{Color:var staticColor}&&staticColor==Colors.Red&&((Border)controls["Dynamic"]).Background is ISolidColorBrush{Color:var dynamicColor}&&dynamicColor==Colors.Blue,"Static/Dynamic semantics lost");
            ((Button)controls["Command"]).Command!.Execute(null);RequireCapability(model.Executions==1,"Command failed");
        }finally{window.Close();}
    }
    private sealed class Native6AViewModel:INotifyPropertyChanged
    {
        private string _name="Initial";public string Name{get=>_name;set{_name=value;PropertyChanged?.Invoke(this,new(nameof(Name)));}}
        public int Executions{get;private set;}public ICommand SaveCommand=>new CommunityToolkit.Mvvm.Input.RelayCommand(()=>Executions++);
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
