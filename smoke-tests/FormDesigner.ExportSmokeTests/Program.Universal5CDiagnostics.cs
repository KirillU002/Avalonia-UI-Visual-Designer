using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static void Write5CCoverage(FormDesigner.ViewModels.MainWindowViewModel vm,string folder)
    {
        StandardLibraryCoverageReport.Write(vm.Registry,folder,Environment.GetEnvironmentVariable("UNIVERSAL5C_RUNTIME_VERIFIED")=="1"?Phase5ATypes.Concat(Phase5BTypes).Concat(Phase5CTypes).ToHashSet():null);
        var rows=new List<string>{"Control,Key,Editor,ReadOnly,ValueSource"};
        foreach(var type in Phase5CTypes){var scene=Scene5C(only:type);scene.SelectSingleControl(scene.Controls.Single(c=>c.Type==type));
            foreach(var row in scene.PropertyGridCategories.SelectMany(c=>c.Rows))rows.Add(string.Join(",",new[]{type,row.Key,row.Editor.ToString(),row.IsReadOnly.ToString(),row.ValueSource.ToString()}.Select(s=>"\""+s.Replace("\"","\"\"")+"\"")));Dispose5AProfileVm(scene);}
        File.WriteAllLines(Path.Combine(folder,"Phase5CPropertyGrid.csv"),rows);Dispose5AProfileVm(vm);
    }
    private static int RunUniversal5CContextDiagnostics()
    {
        var records=new List<object>();var failures=0;
        foreach(var selectFirst in new[]{false,true}){
            using var f=new PointerFixture();if(selectFirst){f.Vm.SelectSingleControl(f.First);f.Refresh();}
            var clock=Stopwatch.StartNew();
            void Log(string code,Control? target=null)=>records.Add(new{SelectFirst=selectFirst,Code=code,Ms=clock.Elapsed.TotalMilliseconds,Target=target?.GetHashCode(),Attached=target?.GetVisualRoot()is not null,Open=target?.ContextMenu?.IsOpen,Stack=code=="Closed"?Environment.StackTrace:null});
            var seen=new HashSet<Control>();
            void Observe(Control wrapper){if(!seen.Add(wrapper))return;wrapper.DetachedFromVisualTree+=(_,_)=>Log("Detached",wrapper);
                wrapper.PropertyChanged+=(_,e)=>{if(e.Property==Avalonia.Controls.Control.ContextMenuProperty&&wrapper.ContextMenu is {} menu){Log("Assigned",wrapper);menu.Opened+=(_,_)=>Log("Opened",wrapper);menu.Closed+=(_,_)=>Log("Closed",wrapper);}};}
            foreach(var w in Layout7Wrappers(f.Window).Values)Observe(w);
            f.Window.AddHandler(Avalonia.Controls.Control.ContextRequestedEvent,(_,_)=>{foreach(var w in Layout7Wrappers(f.Window).Values)Observe(w);Log("ContextRequested");},RoutingStrategies.Tunnel|RoutingStrategies.Bubble,true);
            var point=f.ControlPoint(f.First);Log("Down");f.Send(RawPointerEventType.RightButtonDown,point,RawInputModifiers.RightMouseButton);Log("Up");f.Send(RawPointerEventType.RightButtonUp,point);NativeItemsUiWait(80);
            var target=Layout7Wrappers(f.Window)[f.First.Id];Log("After80",target);if(target.ContextMenu?.IsOpen!=true)failures++;target.ContextMenu?.Close();
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL5C_CONTEXT_OUTPUT")!,JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("CONTEXT_DIAGNOSTICS failures="+failures);return failures==0?0:1;
    }
}
