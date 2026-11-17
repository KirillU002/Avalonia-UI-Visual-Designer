using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System.Text.Json;
namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static readonly string[] New5G={"ReversibleStackPanel","LayoutTransformControl","TransitioningContentControl"};
    private static readonly string[] Types5G=Types5F.Concat(New5G).Append("MaskedTextBox").ToArray();
    private static List<(string Key,string Value)> Values5G(string type)
    {
        var values=Types5F.Contains(type)?Values5F(type):new List<(string Key,string Value)>();
        values.RemoveAll(v=>v.Key=="RenderTransform");
        values.Add(("RenderTransform","translate(4px,6px) rotate(15deg) scale(1.1,0.9) skew(3deg,0deg)"));
        values.Add(("FlowDirection","RightToLeft"));values.Add(("OpacityMask","#E0FFFFFF"));
        if(StandardControlProperties.Find(type,"TextDecorations")is not null)values.Add(("TextDecorations","Underline,Strikethrough"));
        foreach(var key in new[]{"SelectionBrush","SelectionForegroundBrush"})
            if(StandardControlProperties.Find(type,key)is not null&&!values.Any(v=>v.Key==key))values.Add((key,"#FFABCDEF"));
        if(type=="ReversibleStackPanel")values.AddRange(new[]{("ReverseOrder","True"),("Orientation","Vertical"),("Spacing","7")});
        if(type=="LayoutTransformControl")values.AddRange(new[]{("LayoutTransform","rotate(90deg) scale(1.5,1)"),("UseRenderTransform","False")});
        if(type=="TransitioningContentControl")values.Add(("IsTransitionReversed","True"));
        return values;
    }
    private static FormDesigner.ViewModels.MainWindowViewModel Scene5G(string layout="Grid",string? only=null)
    {
        var vm=GridFirstVm();if(layout=="Canvas"){vm.NewFormLayoutChoice=FormDesigner.ViewModels.MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);}
        vm.DesignWidth=1400;vm.DesignHeight=1800;vm.SurfaceGridRowDefinitions="*,*,*,*,*,*,*,*";vm.SurfaceGridColumnDefinitions="*,*";
        var types=only is null?Types5G:new[]{only};
        for(var i=0;i<types.Length;i++)
        {
            var c=vm.CreateControl(types[i],20+i%2*650,20+i/2*200,null,true);c.Name="Proof5G_"+c.Type;c.Width=500;c.Height=150;c.WidthMode=c.HeightMode="Fixed";c.GridRow=i/2;c.GridColumn=i%2;c.Text=c.Type=="MaskedTextBox"?"123456":"Phase 5F текст";
            vm.SelectSingleControl(c);
            foreach(var(key,value)in Values5G(c.Type)){var row=GetPropertyGridRow(vm,key);RequireCapability(row.UsesMetadata&&!row.IsReadOnly,"No typed editor "+c.Type+"."+key);vm.CommitPropertyGridTextEdit(row,value);RequireCapability(!row.HasValidationError,"Rejected "+c.Type+"."+key+": "+row.ValidationMessage);}
            vm.EndPropertyGridTextEdit();
            if(New5G.Contains(c.Type))
                for(var n=0;n<(c.Type=="ReversibleStackPanel"?2:1);n++){var child=vm.CreateControl("TextBlock",0,0,c.Id);child.Name=c.Name+"_Child"+n;child.Text="Child "+n;child.Width=100;child.Height=30;child.WidthMode=child.HeightMode="Fixed";child.StackOrder=n;}
        }
        return vm;
    }
    private static void Assert5G(Control native,string type)
    {
        RequireCapability(native.GetType()==StandardControlProperties.PropertyTypeFor(type),"Wrong native type "+type);
        foreach(var(key,text)in Values5G(type))
        {
            var p=StandardControlProperties.Find(type,key)!;RequireCapability(StandardControlProperties.TryParseValue(p,text,out var expected),"Bad fixture "+key);
            var actual=native.GetValue(p.Property!);
            var equal=actual is TextDecorationCollection a&&expected is TextDecorationCollection b?a.Select(d=>d.Location).SequenceEqual(b.Select(d=>d.Location)):Equal5F(actual,expected);
            RequireCapability(equal,"Wrong native "+type+"."+key+": actual="+(actual is ITransform at?at.Value.ToString():MetadataValueCodec.Display(actual))+" expected="+(expected is ITransform et?et.Value.ToString():MetadataValueCodec.Display(expected))+" UseRenderTransform="+(native is LayoutTransformControl lc?lc.UseRenderTransform.ToString():"n/a"));
        }
        if(native is ReversibleStackPanel panel){RequireCapability(panel.Children.Count==2&&panel.Children[0].Bounds.Y>panel.Children[1].Bounds.Y,"Native reverse arrangement lost");}
        if(native is LayoutTransformControl decorator)RequireCapability(decorator.Child?.Name=="Proof5G_LayoutTransformControl_Child0","Decorator Child lost");
        if(native is TransitioningContentControl content)RequireCapability(content.Content is Control cc&&(cc is TextBlock{Text:"Child 0"}||cc.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Child 0")),"Native Content lost: "+content.Content?.GetType().FullName);
    }
    private static int Run5GChecks()
    {
        var pass=0;var fail=0;void Check(string name,Action action){var filter=Environment.GetEnvironmentVariable("UNIVERSAL5G_FILTER");if(filter is not null&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;try{action();pass++;Console.WriteLine("PASS UNIVERSAL5G_"+name);}catch(Exception e){fail++;Console.WriteLine("FAIL UNIVERSAL5G_"+name+": "+e);}finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}}
        Check("RegistrationSearch",()=>{var vm=GridFirstVm();foreach(var type in New5G){RequireCapability(vm.Registry.GetControls().Count(d=>d.TypeKey==type)==1,"Duplicate/missing "+type);vm.ToolboxSearchText=type;RequireCapability(vm.ToolboxGroups.SelectMany(g=>g.Items).Single(i=>i.Type==type).Icon!="AV","Missing native item/glyph");}});
        foreach(var type in Types5G)
        {
            foreach(var layout in new[]{"Grid","Canvas"})Check(type+"_JsonNative_"+layout,()=>{var vm=Scene5G(layout,type);var path=Path.GetFullPath("artifacts/universal-phase5g/json/"+type+layout+".json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,vm.ExportWorkspaceJson());var reopened=GridFirstVm();reopened.LoadDocumentJson(File.ReadAllText(path));RequireCapability(JsonSerializer.Serialize(vm.Controls)==JsonSerializer.Serialize(reopened.Controls),"JSON lost state");var c=reopened.Controls.Single(c=>c.Type==type);foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(reopened,mode))Assert5G(Native5F(p.Window,c),type);});
            Check(type+"_Designer",()=>{var vm=Scene5G(only:type);var c=vm.Controls.Single(c=>c.Type==type);var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(60);w.UpdateLayout();var native=Layout7Wrappers(w)[c.Id].GetVisualDescendants().OfType<Control>().First(v=>v.GetType()==StandardControlProperties.PropertyTypeFor(type));Assert5G(native,type);});
            Check(type+"_Imported",()=>{var attributes=string.Join(" ",Values5G(type).Select(p=>p.Key+"='"+System.Security.SecurityElement.Escape(p.Value)+"'"));var children=type=="ReversibleStackPanel"?"<a:TextBlock Text='Child 0' Height='30'/><a:TextBlock Text='Child 1' Height='30'/>":New5G.Contains(type)?"<a:TextBlock Name='Proof5G_LayoutTransformControl_Child0' Text='Child 0' Width='100' Height='30'/>":"";var vm=Universal4AVm("<a:"+type+" Name='Target' Width='500' Height='150' "+attributes+">"+children+"</a:"+type+">");var c=vm.Controls.Single(c=>c.Name=="Target");foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(vm,mode))Assert5G(Native5F(p.Window,c,type),type);});
        }
        foreach(var type in New5G)Check(type+"_RoutedDropResize",()=>{using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();var p=GridCellPoint(f.Vm,"",0,0);var data=GridToolboxData(f,type);RouteGridToolbox(f,data,DragDrop.DragOverEvent,p);RouteGridToolbox(f,data,DragDrop.DropEvent,p);f.Refresh();f.AssertIdle();var c=f.Vm.Controls.Single();RequireCapability(c.Type==type&&f.Vm.SelectedControl?.Id==c.Id&&c.X==0&&c.Y==0,"Drop lost selection/placement");c.Width=200;c.Height=100;c.WidthMode=c.HeightMode="Fixed";f.Refresh();var start=f.ResizePoint();f.Down(start);f.Move(start+new Vector(20,15));f.Up(start+new Vector(20,15));f.Refresh();RequireCapability(c.Width>200,"Resize failed");GridEditorUndo(f.Vm);GridEditorUndo(f.Vm,true);});
        foreach(var sample in new[]{("Button","FlowDirection","LeftToRight","RightToLeft"),("Border","OpacityMask","#80FFFFFF","#E0FFFFFF"),("TextBlock","TextDecorations","Underline","Underline,Strikethrough"),("Button","RenderTransform","matrix(1,0,0,1,0,0)","translate(4px,6px) rotate(30deg) scale(2,0.5)"),("ReversibleStackPanel","ReverseOrder","False","True"),("LayoutTransformControl","LayoutTransform","scale(1,1)","rotate(90deg)")})Check(sample.Item1+"_MinimalPatchResetUndoRedo",()=>{var(type,key,before,after)=sample;var vm=Universal4AVm($"<a:{type} Name='Target' {key}='{before}' u:Keep='&amp;yes'/> <!-- keep -->");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),after);vm.EndPropertyGridTextEdit();var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("u:Keep='&amp;yes'"),"Nonminimal patch");vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));var reset=vm.CreateActiveAxamlPatch().PatchedText;GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Undo reset lost expression");GridEditorUndo(vm,true);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==reset,"Redo reset changed source");GridEditorUndo(vm);GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo changed original source");});
        foreach(var expression in new[]{"{Binding NeverRun}","{StaticResource Keep}","{DynamicResource Keep}"})Check("Expressions_"+expression.Split(' ')[0],()=>{var vm=Universal4AVm($"<a:TextBlock Name='Target' RenderTransform='{expression}' OpacityMask='{expression}' TextDecorations='{expression}'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());foreach(var key in new[]{"RenderTransform","OpacityMask","TextDecorations"})RequireCapability(GetPropertyGridRow(vm,key).IsReadOnly,"Expression editable "+key);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Expression changed");});
        Check("TransformGroupOpaque",()=>{var vm=Universal4AVm("<a:Button Name='Target'><a:Button.RenderTransform><a:TransformGroup><a:TranslateTransform X='4'/><a:RotateTransform Angle='30'/></a:TransformGroup></a:Button.RenderTransform></a:Button>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target"));RequireCapability(GetPropertyGridRow(vm,"RenderTransform").IsReadOnly,"Object group flattened");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"TransformGroup XML changed");});
        Check("TransformOrderAndValidation",()=>{var p=StandardControlProperties.Find("Button","RenderTransform")!;StandardControlProperties.TryParseValue(p,"translate(10px,0px) rotate(90deg)",out var a);StandardControlProperties.TryParseValue(p,"rotate(90deg) translate(10px,0px)",out var b);RequireCapability(a is ITransform ta&&b is ITransform tb&&ta.Value!=tb.Value,"Transform order lost");foreach(var bad in new[]{"translate(4,6)","rotate(NaNdeg)","url(evil)","matrix(1,0,0,1,Infinity,0)",string.Concat(Enumerable.Repeat("scale(2) ",17)),"scale(1e300)"})RequireCapability(!StandardControlProperties.TryEncode(p,bad,out _),"Invalid transform accepted: "+bad);});
        Check("DecorationValidation",()=>{var p=StandardControlProperties.Find("TextBlock","TextDecorations")!;foreach(var bad in new[]{"None","Underline,Unknown","Underline,Underline","{Binding X}","<TextDecoration/>"})RequireCapability(!StandardControlProperties.TryEncode(p,bad,out _),"Invalid decoration accepted");});
        Check("TransitionsAndRuntimeStateReadOnly",()=>{var vm=Scene5G(only:"TransitioningContentControl");vm.SelectSingleControl(vm.Controls.Single(c=>c.Type=="TransitioningContentControl"));foreach(var key in new[]{"Transitions","PageTransition"})RequireCapability(GetPropertyGridRow(vm,key).IsReadOnly,"Complex animation editable "+key);var masks=Scene5G(only:"MaskedTextBox");masks.SelectSingleControl(masks.Controls.Single());RequireCapability(GetPropertyGridRow(masks,"CaretIndex").IsReadOnly,"Transient caret authoring promoted");});
        Check("BoxShadowMultiple",()=>{var p=StandardControlProperties.Find("Border","BoxShadow")!;RequireCapability(StandardControlProperties.TryEncode(p,"inset 1 2 3 1 #80112233, 4 5 6 blue",out _),"Valid shadows rejected");foreach(var bad in new[]{"1 2 -3 black","NaN 0 3 red",new string('1',4097)})RequireCapability(!StandardControlProperties.TryEncode(p,bad,out _),"Invalid shadow accepted");});
        Check("MultiForm",()=>{var vm=Scene5G(only:"ReversibleStackPanel");var id=vm.ActiveDocumentId;vm.CreateNewForm();var c=vm.CreateControl("LayoutTransformControl",0,0);vm.SelectSingleControl(c);vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"LayoutTransform"),"rotate(30deg)");vm.EndPropertyGridTextEdit();vm.SetActiveForm(id,"5g");RequireCapability(vm.Controls.Count==3&&vm.Controls.Any(c=>c.Type=="ReversibleStackPanel"),"Forms mixed");});
        foreach(var mode in new[]{false,true})Check("Memory_"+mode,()=>{var vm=Scene5G();var refs=Enumerable.Range(0,10).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);GC.KeepAlive(vm);});
        if(fail==0&&Environment.GetEnvironmentVariable("UNIVERSAL5G_FILTER")is null){Directory.CreateDirectory("artifacts/universal-phase5g");File.WriteAllText("artifacts/universal-phase5g/inspector-proof.json",JsonSerializer.Serialize(Types5G.SelectMany(type=>Values5G(type).Select(p=>new{Type=StandardControlProperties.PropertyTypeFor(type)!.FullName,p.Key,p.Value,Inspector=true,Json=true,Designer=true,AxamlPreview=true,LegacyPreview=true,ImportedPreview=true}))));}
        Console.WriteLine($"UNIVERSAL5G_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    private static int Generate5GProjects(){var folder=Environment.GetEnvironmentVariable("UNIVERSAL5G_EXPORT_DIR")!;RequireCapability(!Directory.Exists(folder),"Fresh output required");foreach(var layout in new[]{"Grid","Canvas"}){var vm=Scene5G(layout);Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));File.WriteAllText(Path.Combine(folder,layout+"-expected.json"),JsonSerializer.Serialize(vm.Controls.Where(c=>string.IsNullOrEmpty(c.ParentId)&&Types5G.Contains(c.Type)).Select(c=>new{Name=c.Name,Type=StandardControlProperties.PropertyTypeFor(c.Type)!.FullName,Properties=Values5G(c.Type).ToDictionary(p=>p.Key,p=>p.Value)})));}return 0;}
}
