using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Services;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static int RunUniversal4CProfile()
    {
        var output=Environment.GetEnvironmentVariable("UNIVERSAL4C_PROFILE_OUTPUT")!;
        var samples=new List<object>();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        RequireCapability(!File.Exists(output)&&!File.Exists(output+".partial.json"),"Fresh profile output required");
        try
        {
        var sources=new[]{("Small",4), ("Medium",100), ("Large",-1)};
        foreach(var (name,count) in sources)
        {
            var text=count<0?File.ReadAllText(Path.Combine(FindRepositoryRoot(),"Views","MainWindow.axaml"))
                :"<Window xmlns='https://github.com/avaloniaui'><Grid><StackPanel Name='Host'>"+string.Concat(Enumerable.Range(0,count).Select(i=>$"<Button Name='B{i}' Content='value'/><!--keep-->"))+"</StackPanel></Grid></Window>";
            for(var iteration=-5;iteration<30;iteration++)
            {
                var vm=GridFirstVm();
                try
                {
                    vm.LoadAxamlImportedDocument(new AxamlImportService().Import(text,"benchmark.axaml"));
                    var entry=vm.ActiveAxamlSourceDocument!.SourceMap.Controls.First(r=>r.Element.LocalName is "StackPanel" or "Canvas" or "Grid"
                        &&vm.GetAxamlStructuralCapability(r.ControlId).CanAddChild
                        &&vm.ActiveAxamlSourceDocument.SourceMap.Controls.Any(c=>c.ParentId==r.ControlId&&c.Element.LocalName is "Button" or "TextBox" or "TextBlock"&&c.Element.Children.Count==0));
                    var leaf=vm.ActiveAxamlSourceDocument.SourceMap.Controls.First(c=>c.ParentId==entry.ControlId&&c.Element.LocalName is "Button" or "TextBox" or "TextBlock"&&c.Element.Children.Count==0);
                    string added="";
                    void Measure(string operation,Action action)
                    {
                        using var scope=LayoutDropProfile.Begin(vm.ActiveDocumentId,operation);
                        var bytes=GC.GetAllocatedBytesForCurrentThread();var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                        var watch=Stopwatch.StartNew();action();watch.Stop();
                        if(iteration>=0){using var process=Process.GetCurrentProcess();samples.Add(new{Scene=name,Operation=operation,Iteration=iteration,Ms=watch.Elapsed.TotalMilliseconds,
                            Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),PeakWorkingSet=process.PeakWorkingSet64,
                            Stages=scope.Stages.ToArray(),Counters=scope.Counters});}
                    }
                    Measure("Insert",()=>added=(vm.InsertAxamlControl("StackPanel",entry.ControlId,0,0,index:int.MaxValue)??throw new Exception(vm.StatusText)).Id);
                    Measure("Reorder",()=>RequireCapability(vm.ReorderAxamlControl(added,0),vm.StatusText));
                    Measure("Reparent",()=>RequireCapability(vm.ReparentAxamlControl(leaf.ControlId,added,0,0),vm.StatusText));
                    Measure("RemoveSubtree",()=>RequireCapability(vm.RemoveAxamlControls(new[]{added}),vm.StatusText));
                    NativeItemsUiWait(160);
                    Measure("Undo",()=>vm.UndoEditorCommand!.Execute(null));
                    NativeItemsUiWait(100);
                    NativeItemsUiWait(160);
                    Measure("Redo",()=>vm.RedoEditorCommand!.Execute(null));
                    NativeItemsUiWait(100);
                    Measure("Patch",()=>RequireCapability(vm.CreateActiveAxamlPatch().CanApply,"Profile patch blocked"));
                    Measure("PreviewRefresh",()=>{using var preview=ShowDataGridRegressionPreview(vm,true);RequireCapability(!preview.Window.BuildIssues.Any(d=>d.Message.Contains("Не удалось построить")),"Profile native tree failed");});
                }
                finally{Dispose5AProfileVm(vm);}
            }
            Console.WriteLine("PROFILE4C "+name+" warmup=5 samples=30/operation");
            File.WriteAllText(output+".partial.json",JsonSerializer.Serialize(samples));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,JsonSerializer.Serialize(samples));return 0;
        }
        catch(Exception error)
        {
            File.WriteAllText(output+".partial.json",JsonSerializer.Serialize(samples));
            Console.Error.WriteLine("PROFILE4C_MANAGED_FAILURE "+error);return 1;
        }
    }
    private static int GenerateUniversal4CRuntime()
    {
        var folder=Environment.GetEnvironmentVariable("UNIVERSAL4C_GENERATED_DIR")!;
        RequireCapability(!Directory.Exists(folder),"Fresh generated directory required");
        var project=GridFirstVm();Pump(project.ExportCurrentResultToProjectAsync(folder));
        var path=Path.Combine(folder,"MainWindow.axaml");var className=System.Xml.Linq.XDocument.Parse(File.ReadAllText(path)).Root!.Attributes().Single(a=>a.Name.LocalName=="Class").Value;
        var vm=Universal4AVm("<a:Grid Name='Root' ColumnDefinitions='*,*'><a:StackPanel Name='From'><a:Button Name='Moved' Content='preserved' Tag='unknown'/><!--KEEP--><a:TextBox Name='Deleted'/><a:ComboBox Name='Combo'><a:ComboBoxItem Name='C1' Content='one'/><a:ComboBoxItem Name='C2' Content='two'/></a:ComboBox><a:ListBox Name='List'><a:ListBoxItem Name='L1' Content='one'/><a:ListBoxItem Name='L2' Content='two'/></a:ListBox></a:StackPanel><a:Canvas Name='To' Grid.Column='1'/></a:Grid>");
        var styled=new HashSet<string>();
        foreach(var type in new[]{"Button","TextBox","TextBlock","CheckBox","Border","StackPanel","Grid","Canvas"})
        {var c=vm.InsertAxamlControl(type,Named4C(vm,"From").Id,0,0)??throw new Exception(vm.StatusText);vm.SelectSingleControl(c);if(type is "Button" or "TextBox" or "Border")styled.Add(c.Name);}
        foreach(var name in new[]{"Combo","List"}){vm.SelectSingleControl(Named4C(vm,name));var session=vm.OpenCollectionEditor("Items")??throw new Exception(vm.StatusText);RequireCapability(session.Add()&&session.Move(-1),session.ValidationMessage);vm.CloseCollectionEditor(session);}
        var target=Named4C(vm,"To");vm.AxamlLayoutBounds=AxamlLayoutProjection.Arrange(vm.ActiveAxamlSourceDocument!,vm.Controls,vm.DesignWidth,vm.DesignHeight);var origin=vm.GetAbsolutePosition(target);
        RequireCapability(vm.ReparentAxamlControl(Named4C(vm,"Moved").Id,target.Id,origin.X+30,origin.Y+40),vm.StatusText);
        RequireCapability(vm.RemoveAxamlControls(new[]{Named4C(vm,"Deleted").Id}),vm.StatusText);
        var patch=Text4C(vm);File.WriteAllText(path,patch.Replace("<a:Window xmlns=","<a:Window x:Class='"+className+"' xmlns="));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(folder)!,"structural-runtime-expectations.json"),JsonSerializer.Serialize(vm.ActiveAxamlSourceDocument!.SourceMap.Controls.Select(r=>{var c=vm.Controls.Single(c=>c.Id==r.ControlId);return new{Id=r.ControlId,Name=c.Name,Type=r.Element.LocalName,ParentId=r.ParentId,ExpectedCornerRadius=styled.Contains(c.Name)&&r.Element.FindAttribute("CornerRadius")is not null?(double?)c.CornerRadius:null,ExpectedPadding=styled.Contains(c.Name)&&r.Element.FindAttribute("Padding")is not null?(double?)c.Padding:null,ExpectedBorderThickness=styled.Contains(c.Name)&&r.Element.FindAttribute("BorderThickness")is not null?(double?)c.BorderThickness:null};})));
        Console.WriteLine("GENERATED4C "+folder);return 0;
    }
}
