using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;
namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static int RunUniversal5EContextChecks()
    {
        var pass=0;var fail=0;
        void Check(string name,Action test){try{test();pass++;Console.WriteLine("PASS CONTEXT5E_"+name);}catch(Exception e){fail++;Console.WriteLine("FAIL CONTEXT5E_"+name+": "+e);}}
        Check("QueuedSelectionRender",()=>{
            using var f=new PointerFixture();
            var timer=(DispatcherTimer)typeof(MainWindow).GetField("_designerRenderTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Window)!;
            timer.Interval=TimeSpan.FromMinutes(1); // Control scheduling order, not a production delay.
            var id=f.First.Id;var point=f.ControlPoint(f.First);var trace=new List<object>();
            f.Send(RawPointerEventType.RightButtonDown,point,RawInputModifiers.RightMouseButton);
            var pending=(bool)typeof(MainWindow).GetField("_isDesignerRenderScheduled",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Window)!;
            RequireCapability(pending,"Scenario did not queue the selection render");
            f.Send(RawPointerEventType.RightButtonUp,point);NativeItemsUiWait(20);
            var shell=Layout7Wrappers(f.Window)[id];var menu=shell.ContextMenu??throw new Exception("Menu missing before render");
            trace.Add(new{Code="BeforeTick",Open=menu.IsOpen,Attached=shell.GetVisualRoot()!=null});
            menu.Closed+=(_,_)=>trace.Add(new{Code="Closed",Stack=Environment.StackTrace});
            typeof(MainWindow).GetMethod("DesignerRenderTimer_Tick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(f.Window,new object?[]{null,EventArgs.Empty});
            var current=Layout7Wrappers(f.Window)[id];
            trace.Add(new{Code="AfterTick",Open=current.ContextMenu?.IsOpen,SameWrapper=ReferenceEquals(shell,current)});
            Directory.CreateDirectory("artifacts/universal-phase5e");File.WriteAllText("artifacts/universal-phase5e/context-order.json",JsonSerializer.Serialize(trace,new JsonSerializerOptions{WriteIndented=true}));
            RequireCapability(current.ContextMenu?.IsOpen==true,"Queued selection render retired the open menu owner");current.ContextMenu!.Close();
        });
        Check("StaleWrapperAcrossReload",()=>{
            using var f=new PointerFixture();var id=f.First.Id;var retired=Layout7Wrappers(f.Window)[id];var session=f.Vm.DocumentSessionId;var doc=f.Vm.ActiveDocumentId;var json=f.Vm.ExportWorkspaceJson();
            f.Vm.LoadDocumentJson(json);f.Refresh();RequireCapability(f.Vm.ActiveDocumentId==doc&&f.Vm.DocumentSessionId!=session,"Reload did not reuse document id in a new session");
            retired.RaiseEvent(new ContextRequestedEventArgs{RoutedEvent=Avalonia.Controls.Control.ContextRequestedEvent});NativeItemsUiWait(20);
            RequireCapability(Layout7Wrappers(f.Window)[id].ContextMenu?.IsOpen!=true,"Retired session opened a menu in the reloaded project");
        });
        Console.WriteLine($"CONTEXT5E_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
}
