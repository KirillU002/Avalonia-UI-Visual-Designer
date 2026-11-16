using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Text.Json;
namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static readonly string[] Phase5ETypes={"Button","TextBox","TextBlock","Border","Slider","NumericUpDown","ListBox","Calendar"};
    private static readonly string[] Common5E={"IsEnabled","IsHitTestVisible","Focusable","IsTabStop","TabIndex","UseLayoutRounding","ClipToBounds","FontStyle","FontStretch","ToolTip.Tip"};
    private static List<(string Key,string Value)> Values5E(string type)
    {
        var keys=Common5E.Concat(type switch{"TextBox"=>new[]{"IsReadOnly","MaxLength","AcceptsReturn","AcceptsTab","TextWrapping","TextAlignment"},"TextBlock"=>new[]{"TextAlignment","TextWrapping","TextTrimming","LineHeight","LetterSpacing","MaxLines"},"Border"=>new[]{"BoxShadow"},_=>Array.Empty<string>()});
        var result=new List<(string,string)>();
        foreach(var key in keys){var p=StandardControlProperties.Find(type,key);if(p is null)continue;
            var value=key switch{"TabIndex"=>"7","FontStyle"=>"Italic","FontStretch"=>"Condensed","ToolTip.Tip"=>"Tip & сохранение","TextWrapping"=>"Wrap","TextAlignment"=>"Center","TextTrimming"=>"CharacterEllipsis","LineHeight"=>"26","LetterSpacing"=>"1.5","MaxLines"=>"2","MaxLength"=>"120","BoxShadow"=>"4 6 8 2 #80000000",_=>p.DefaultValue is true?"False":"True"};
            result.Add((key,value));}
        return result;
    }
    private static MainWindowViewModel Scene5E(string layout="Grid",string? only=null)
    {
        var vm=GridFirstVm();if(layout=="Canvas"){vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);}
        vm.DesignWidth=1200;vm.DesignHeight=900;vm.SurfaceGridRowDefinitions="*,*,*,*";vm.SurfaceGridColumnDefinitions="*,*";
        var types=only is null?Phase5ETypes:new[]{only};
        for(var i=0;i<types.Length;i++){
            var c=vm.CreateControl(types[i],20+i%2*560,20+i/2*200);c.Name="Proof5E_"+c.Type;c.GridRow=i/2;c.GridColumn=i%2;c.Width=450;c.Height=150;c.WidthMode=c.HeightMode="Fixed";c.Text="Property proof";
            vm.SelectSingleControl(c);
            foreach(var (key,value)in Values5E(c.Type)){var row=GetPropertyGridRow(vm,key);RequireCapability(!row.IsReadOnly&&row.UsesMetadata,"Missing reviewed typed editor "+c.Type+"."+key);vm.CommitPropertyGridTextEdit(row,value);RequireCapability(!row.HasValidationError,"Valid property rejected "+key+": "+row.ValidationMessage);}
            vm.EndPropertyGridTextEdit();
        }
        return vm;
    }
    private static Control Native5E(Control root,DesignControlModel model,string? sourceType=null)
    {
        var type=StandardControlProperties.PropertyTypeFor(sourceType??model.Type);
        var named=root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c=>c.Name==model.Name&&c.GetType()==type);
        if(named is not null)return named;
        var tracked=(Dictionary<string,Control>)typeof(FormDesigner.Views.PreviewWindow).GetField("_builtPreviewControls",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(root)!;
        var rootControl=tracked[model.Id];
        // Legacy Canvas tracks a wrapper (also a Border for Border controls),
        // while imported trees can track the native control itself.
        return rootControl.GetVisualDescendants().OfType<Control>().Append(rootControl).First(c=>c.GetType()==type);
    }
    private static void Assert5E(Control native,string type)
    {
        foreach(var (key,value)in Values5E(type)){var p=StandardControlProperties.Find(type,key)!;RequireCapability(StandardControlProperties.TryParseValue(p,value,out var expected),"Bad fixture "+key);var actual=native.GetValue(p.Property!);var equal=actual is BoxShadows a&&expected is BoxShadows b?a.ToString()==b.ToString():Equals(actual,expected);RequireCapability(equal,$"Native {type}.{key}: {MetadataValueCodec.Display(actual)} != {value}");}
    }
    private static int RunUniversal5EChecks()
    {
        var pass=0;var fail=0;void Check(string name,Action test){var filter=Environment.GetEnvironmentVariable("UNIVERSAL5E_FILTER");if(filter!=null&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;try{test();pass++;Console.WriteLine("PASS UNIVERSAL5E_"+name);}catch(Exception e){fail++;Console.WriteLine("FAIL UNIVERSAL5E_"+name+": "+e);}finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}}
        foreach(var type in Phase5ETypes){
            foreach(var layout in new[]{"Grid","Canvas"})Check(type+"_InspectorJsonNative_"+layout,()=>{var vm=Scene5E(layout,type);var model=vm.Controls.Single();var file=Path.GetFullPath("artifacts/universal-phase5e/json/"+type+layout+".json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,vm.ExportWorkspaceJson());var reopened=GridFirstVm();reopened.LoadDocumentJson(File.ReadAllText(file));RequireCapability(JsonSerializer.Serialize(vm.Controls)==JsonSerializer.Serialize(reopened.Controls),"JSON state changed");foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(reopened,mode))Assert5E(Native5E(p.Window,model),type);});
            Check(type+"_ResetUndo",()=>{var vm=Scene5E(only:type);var id=vm.Controls.Single().Id;vm.SelectSingleControl(vm.Controls.Single());vm.EndPropertyGridTextEdit();vm.BeginUndoBatch();vm.CommitUndoBatch();var key=Values5E(type)[0].Key;var json=vm.Controls.Single().CustomProperties.Single(p=>p.Key==key).ValueJson;vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));RequireCapability(vm.Controls.Single().CustomProperties.All(p=>p.Key!=key),"Reset failed");GridEditorUndo(vm);RequireCapability(vm.Controls.Single().Id==id&&vm.Controls.Single().CustomProperties.Single(p=>p.Key==key).ValueJson==json,"Undo failed");GridEditorUndo(vm,true);});
        }
        foreach(var sample in new[]{("Button","FontStyle","Normal","Italic"),("TextBlock","LineHeight","18","26"),("Border","BoxShadow","1 2 3 black","4 6 8 2 #80000000"),("TextBox","MaxLength","20","120"),("ListBox","ToolTip.Tip","old","new")})Check(sample.Item1+"_SourceLiteralReset",()=>{var (type,key,before,after)=sample;var vm=Universal4AVm($"<a:{type} Name='Target' {key}='{before}' u:Keep='yes'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),after);var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("u:Keep='yes'"),"Unsafe/nonminimal patch");vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));RequireCapability(!vm.CreateActiveAxamlPatch().PatchedText.Contains(key+"='"),"Reset failed");GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Undo Reset lost source");GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo edit changed source");});
        foreach(var expression in new[]{"{Binding NeverRun}","{StaticResource Keep}","{DynamicResource Keep}"})Check("Expression_"+expression.Split(' ')[0],()=>{var vm=Universal4AVm($"<a:Border Name='Target' BoxShadow='{expression}' ToolTip.Tip='{expression}' u:Keep='yes'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,"BoxShadow").IsReadOnly&&GetPropertyGridRow(vm,"ToolTip.Tip").IsReadOnly,"Expression is editable");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Expression changed");});
        Check("BoxShadowValidation",()=>{var p=StandardControlProperties.Find("Border","BoxShadow")!;foreach(var bad in new[]{"broken","1 2 -3 black","NaN 2 3 black",new string('1',4097)})RequireCapability(!StandardControlProperties.TryEncode(p,bad,out _),"Invalid shadow accepted");StandardControlProperties.TryParseValue(p,"inset 2 3 4 1 #80112233, 5 6 7 blue",out var v);RequireCapability(v is BoxShadows shadows&&shadows.Count==2,"Multiple/inset shadows failed");var encoded=MetadataValueCodec.Display(v);RequireCapability(StandardControlProperties.TryParseValue(p,encoded,out _),"Shadow serialization not reversible");});
        Check("MultiFormAndNullable",()=>{var vm=Scene5E();var first=vm.ActiveDocumentId;vm.CreateNewForm();var c=vm.CreateControl("Button",0,0);vm.SelectSingleControl(c);vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"ToolTip.Tip"),"(null)");vm.EndPropertyGridTextEdit();vm.SetActiveForm(first,"5e");var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());clone.SetActiveForm(first,"5e");RequireCapability(clone.Controls.Count==8,"Form state mixed");clone.SetActiveForm(vm.CurrentProject.Forms.Last().Id,"5e");RequireCapability(clone.Controls.Single().CustomProperties.Single(p=>p.Key=="ToolTip.Tip").ValueJson=="null","Nullable value lost");});
        foreach(var mode in new[]{false,true})Check("Memory20_"+mode,()=>{var vm=Scene5E();var refs=Enumerable.Range(0,20).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);GC.KeepAlive(vm);});
        foreach(var bad in new[]{("Button","FontStyle","\"InvalidEnum\""),("Button","Focusable","\"{Binding\""),("Border","RenderTransform","{\"Angle\":20}"),("Path","Data","\"M 0\""),("Menu","Items.Collection","{\"Version\":1,\"Items\":null}")})Check(bad.Item1+"_ExportGuard_"+bad.Item2,()=>{var vm=GridFirstVm();var c=vm.CreateControl(bad.Item1,0,0);StandardItemsProperties.Write(c,bad.Item2,"dummy");c.CustomProperties.Single(p=>p.Key==bad.Item2).ValueJson=bad.Item3;var folder=Path.GetFullPath("artifacts/universal-phase5e/blocked/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"sentinel.txt"),"keep");var zip=folder+".zip";File.WriteAllText(zip,"previous valid output");foreach(var isZip in new[]{false,true}){var stopped=false;try{Pump(isZip?vm.ExportCurrentResultAsZipAsync(zip):vm.ExportCurrentResultToProjectAsync(folder));}catch(NotSupportedException){stopped=true;}RequireCapability(stopped,"Partial export accepted");}RequireCapability(File.ReadAllText(zip)=="previous valid output"&&Directory.GetFiles(folder).Length==1,"Previous output changed");});
        Check("CancelledZipKeepsPreviousOutput",()=>{var vm=Scene5E(only:"Button");vm.GenerateXaml();var result=(ExportResult)typeof(MainWindowViewModel).GetProperty("CurrentExportResult",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;var path=Path.GetFullPath("artifacts/universal-phase5e/cancelled-"+Guid.NewGuid().ToString("N")+".zip");File.WriteAllText(path,"previous output");try{Pump(new FormDesigner.Services.ExportPipelineService().ExportZipAsync(result,path,new System.Threading.CancellationToken(true)));throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}RequireCapability(File.ReadAllText(path)=="previous output","Cancelled ZIP destroyed previous output");});
        foreach(var type in Phase5ETypes)Check(type+"_DesignerNative",()=>{var vm=Scene5E(only:type);var c=vm.Controls.Single();var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(60);window.UpdateLayout();var shell=Layout7Wrappers(window)[c.Id];var native=shell.GetVisualDescendants().OfType<Control>().First(v=>v.GetType()==StandardControlProperties.PropertyTypeFor(type));Assert5E(native,type);});
        foreach(var type in Phase5ETypes)Check(type+"_ImportedNative",()=>{
            var attributes=string.Join(" ",Values5E(type).Select(p=>p.Key+"='"+System.Security.SecurityElement.Escape(p.Value)+"'"));
            var vm=Universal4AVm("<a:"+type+" Name='Target' "+attributes+"/>");
            var original=vm.ActiveAxamlSourceDocument!.OriginalText;
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))Assert5E(Native5E(preview.Window,vm.Controls.Single(),type),type);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Imported native Preview modified source");
        });
        if(fail==0){Directory.CreateDirectory("artifacts/universal-phase5e");File.WriteAllText("artifacts/universal-phase5e/inspector-proof.json",JsonSerializer.Serialize(Phase5ETypes.SelectMany(type=>Values5E(type).Select(p=>new{Type=StandardControlProperties.PropertyTypeFor(type)!.FullName,p.Key,p.Value,Inspector=true,Json=true,Designer=true,AxamlPreview=true,LegacyPreview=true,ImportedPreview=true}))));}
        Console.WriteLine($"UNIVERSAL5E_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    private static int RunUniversal5EAudit(){var vm=GridFirstVm();var folder=Environment.GetEnvironmentVariable("UNIVERSAL5E_OUTPUT_DIR")!;MetadataCoverageReport.Write(vm.Registry,folder);StandardLibraryCoverageReport.Write(vm.Registry,folder);Write5DPropertyInventory(vm.Registry,folder);Dispose5AProfileVm(vm);return 0;}
    private static int GenerateUniversal5EProjects(){var folder=Environment.GetEnvironmentVariable("UNIVERSAL5E_EXPORT_DIR")!;RequireCapability(!Directory.Exists(folder),"Fresh export required");foreach(var layout in new[]{"Grid","Canvas"}){var vm=Scene5E(layout);Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));File.WriteAllText(Path.Combine(folder,layout+"-expected.json"),JsonSerializer.Serialize(vm.Controls.Select(c=>new{Name=c.Name,Type=StandardControlProperties.PropertyTypeFor(c.Type)!.FullName,Properties=Values5E(c.Type).ToDictionary(p=>p.Key,p=>p.Value)})));}return 0;}
}
