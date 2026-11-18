using Avalonia.Controls;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Text.Json;
using System.Diagnostics;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int Audit6AExpressions()
    {
        var rows=AvaloniaMetadataCatalog.DiscoverControls().SelectMany(type=>AvaloniaMetadataCatalog.Get(type).Properties.Select(p=>new{
            Control=type.FullName,Assembly=type.Assembly.GetName().Name,p.Key,Identity=(p.Property is null?"CLR:":"AP:")+p.OwnerType.FullName+"."+p.Name,
            OwnerType=p.OwnerType.FullName,PropertyType=p.PropertyType.FullName,p.ReadOnly,p.Bindable,p.Attached,p.Inherited,
            ExpressionTarget=PropertyExpressions.CanTarget(p),Reason=PropertyExpressions.CanTarget(p)?"Conditional fx target; value grammar/source mapping/context still validated":"Read-only/runtime/structural/data graph or no reviewed target policy"
        }));
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL6A_AUDIT_OUTPUT")!,JsonSerializer.Serialize(rows));
        Console.WriteLine("EXPRESSION6A_AUDIT PASS existing metadata; no control/VM/converter construction");return 0;
    }
    private static MainWindowViewModel GeneratedScene6A(string layout)
    {
        var vm=GridFirstVm();vm.DesignWidth=1000;vm.DesignHeight=850;
        if(layout=="Canvas"){vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);}
        vm.XamlVerbosity=MainWindowViewModel.XamlVerbosityFullStyled;vm.SurfaceLayoutRows=10;vm.SurfaceGridRowDefinitions=string.Join(",",Enumerable.Repeat("*",10));
        void Add(string type,string name,params (string Key,PropertyValueKind Kind,string Text)[] values)
        {
            var c=vm.CreateControl(type,0,0);c.Name=name;c.Width=260;c.Height=40;c.WidthMode=c.HeightMode="Fixed";
            c.GridRow=vm.Controls.Count-1;c.X=20;c.Y=(vm.Controls.Count-1)*60;
            vm.SelectSingleControl(c);foreach(var v in values)Commit6A(vm,v.Kind,v.Text,v.Key);
        }
        Add("TextBox","Input6A",("Text",PropertyValueKind.Binding,"{Binding Path=Name, Mode=TwoWay}"));
        Add("TextBlock","Output6A",("Text",PropertyValueKind.Binding,"{Binding Path=Name, Mode=OneWay, StringFormat='Value: {0}'}"));
        Add("TextBlock","Named6A",("Text",PropertyValueKind.Binding,"{Binding Path=Text, ElementName=Input6A}"));
        Add("TextBlock","Self6A",("Text",PropertyValueKind.Binding,"{Binding Path=Name, RelativeSource={RelativeSource Self}}"));
        Add("TextBlock","Missing6A",("Text",PropertyValueKind.Binding,"{Binding Path=Absent, FallbackValue='Fallback'}"));
        Add("TextBlock","Null6A",("Text",PropertyValueKind.Binding,"{Binding Path=NullableName, TargetNullValue='NoName'}"));
        Add("Border","Static6A",("Background",PropertyValueKind.StaticResource,"{StaticResource ThemeAccentBrush}"));
        Add("Border","Dynamic6A",("Background",PropertyValueKind.DynamicResource,"{DynamicResource ThemeAccentBrush}"));
        Add("Button","Command6A",("Command",PropertyValueKind.Binding,"{Binding SaveCommand}"),("CommandParameter",PropertyValueKind.Binding,"{Binding Name}"));
        return vm;
    }
    private static int Generate6AProjects()
    {
        var folder=Environment.GetEnvironmentVariable("UNIVERSAL6A_EXPORT_DIR")!;
        RequireCapability(!Directory.Exists(folder),"Fresh export directory required");
        foreach(var layout in new[]{"Grid","Canvas"})
        {
            var vm=GeneratedScene6A(layout);var original=vm.ExportWorkspaceJson();var jsonPath=Path.GetFullPath("artifacts/universal-phase6a/generated-"+layout+".json");File.WriteAllText(jsonPath,original);var copy=GridFirstVm();copy.LoadDocumentJson(File.ReadAllText(jsonPath));
            Pump(copy.ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));
            RequireCapability(!copy.GeneratedCSharp.Contains("SaveCommand")&&!copy.GeneratedCSharp.Contains("NullableName"),"Fake application ViewModel generated");
            File.WriteAllText(Path.Combine(folder,layout+"-expressions.json"),JsonSerializer.Serialize(copy.Controls.SelectMany(c=>c.CustomProperties.Where(v=>v.Key.StartsWith(PropertyExpressions.Prefix,StringComparison.Ordinal)).Select(v=>new{c.Name,c.Type,v.Key,v.ValueJson}))));
        }
        Console.WriteLine("GENERATED6A_PROJECTS PASS Grid+Canvas; JSON reopen before export");return 0;
    }
    private static int Profile6AEditor()
    {
        var rows=new List<object>();var vm=Json6A();var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);window.UpdateLayout();
        try
        {
            for(var i=-5;i<30;i++)
            {
                void Measure(string operation,Action action)
                {
                    var bytes=GC.GetAllocatedBytesForCurrentThread();var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                    var timer=Stopwatch.StartNew();action();timer.Stop();
                    if(i>=0)rows.Add(new{Operation=operation,Iteration=i,Ms=timer.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,GC=Enumerable.Range(0,3).Select(n=>GC.CollectionCount(n)-gc[n]).ToArray()});
                }
                Measure("OpenEditor",()=>{var editor=new PropertyExpressionEditorWindow(vm,vm.OpenPropertyExpressionEditor(Row6A(vm))!);try{editor.Show();editor.UpdateLayout();}finally{editor.Close();}});
                Measure("LiteralToBinding",()=>{Commit6A(vm,PropertyValueKind.Binding,"{Binding Name}");window.UpdateLayout();});
                Measure("BindingToResource",()=>{Commit6A(vm,PropertyValueKind.DynamicResource,"{DynamicResource ManualKey}");window.UpdateLayout();});
                Measure("ResourceToLiteral",()=>{Commit6A(vm,PropertyValueKind.Literal,"literal");window.UpdateLayout();});
                vm.ResetPropertyExpression(Row6A(vm));window.UpdateLayout();
            }
        }
        finally{window.CloseForExternalHost();SmokeWindows.Clear();Dispose5AProfileVm(vm);}
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL6A_PROFILE_OUTPUT")!,JsonSerializer.Serialize(rows));
        Console.WriteLine("PROFILE6A_EDITOR PASS 5 warm-up + 30 x 4 operations");return 0;
    }
}
