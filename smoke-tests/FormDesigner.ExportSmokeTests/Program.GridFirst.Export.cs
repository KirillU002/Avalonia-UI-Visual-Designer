using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.IO.Compression;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel GridFirstMultiForm()
    {
        var vm=GridFirstVm();vm.NewProjectEditorCommand!.Execute(null);var home=vm.ActiveDocumentId;
        vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;
        var bar=vm.CreateControl("StackPanel",0,0);bar.Name="NavigationBar";bar.LayoutOrientation="Horizontal";
        var links=new List<DesignControlModel>();
        foreach(var name in new[]{"OpenGrid","OpenCanvas","OpenNested"}){var b=vm.CreateControl("Button",0,0,bar.Id);b.Name=name;b.Text=name;links.Add(b);}
        Rows("HomeRows",null,1);
        var targets=new List<string>();
        vm.CreateNewForm();targets.Add(vm.ActiveDocumentId);vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;
        var input=vm.CreateControl("TextBox",0,0);input.Name="GridInput";input.WidthMode="Fixed";input.Width=220;
        var apply=vm.CreateControl("Button",0,0);apply.Name="GridApply";apply.Margin="250,0,0,0";
        vm.Interactions.Add(new(){SourceControlName=apply.Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionSetProperty,TargetControlName=input.Name,TargetProperty="Text",TextTemplate="Grid accepted"});
        Rows("GridRows",null,1);Back();
        vm.CreateNewForm("Absolute");targets.Add(vm.ActiveDocumentId);var free=vm.CreateControl("Button",100,150,null,true);free.Name="CanvasButton";free.Width=160;free.Height=40;Back();
        vm.CreateNewForm();targets.Add(vm.ActiveDocumentId);vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;
        var stack=vm.CreateControl("StackPanel",0,0);stack.Name="NestedStack";stack.LayoutOrientation="Horizontal";
        foreach(var type in new[]{"ListBox","DropDownButton","SplitButton"}){var c=vm.CreateControl(type,0,0,stack.Id);c.Name=type;c.WidthMode="Fixed";c.Width=180;}
        Rows("NestedRows",null,1);Back();
        vm.SetActiveForm(home,"grid-first-runtime-home");
        for(var i=0;i<links.Count;i++)vm.Interactions.Add(new(){SourceControlName=links[i].Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionOpenForm,TargetFormId=targets[i],OpenMode=InteractionModel.OpenModeShow});
        vm.IncludeSampleData=true;
        return vm;
        void Rows(string name,string? parent,int row)
        {
            var source=ProductsSource();source.Id="grid-first-rows-"+vm.ActiveDocumentId;source.UseDemoData=true;vm.BindingSources.Add(source);
            var grid=vm.CreateControl("DataGrid",0,0,parent);grid.Name=name;grid.GridRow=row;grid.BindingSourceId=source.Id;grid.ShowFilterRow=grid.ShowGroupPanel=grid.ShowFooter=false;
        }
        void Back()
        {
            var b=vm.CreateControl("Button",20,20);b.Name="BackHome";b.Text="Home";b.HorizontalAlignment="Right";
            vm.Interactions.Add(new(){SourceControlName=b.Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionOpenForm,TargetFormId=home,OpenMode=InteractionModel.OpenModeShow});
        }
    }
    private static int RunGridFirstExport()
    {
        try
        {
            Directory.CreateDirectory(GridFirstFolder);var vm=GridFirstMultiForm();
            var modes=vm.CurrentProject.Forms.Select(f=>new{f.Id,f.Name,f.Document.LayoutSemanticsVersion,f.Document.SurfaceLayoutMode}).ToArray();
            RequireCapability(modes.Select(f=>f.SurfaceLayoutMode).SequenceEqual(new[]{"Grid","Grid","Absolute","Grid"}),"Four-form root defaults differ.");
            File.WriteAllText(Path.Combine(GridFirstFolder,"workspace.json"),vm.ExportWorkspaceJson());
            File.WriteAllText(Path.Combine(GridFirstFolder,"forms.json"),JsonSerializer.Serialize(modes,new JsonSerializerOptions{WriteIndented=true}));
            Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(GridFirstFolder,"export")));
            Pump(vm.ExportCurrentResultAsZipAsync(Path.Combine(GridFirstFolder,"GridFirst.zip")));
            using var zip=ZipFile.OpenRead(Path.Combine(GridFirstFolder,"GridFirst.zip"));
            RequireCapability(zip.Entries.Count(e=>e.FullName.EndsWith(".axaml"))==5,"ZIP lost one of the four forms.");
            Console.WriteLine("PASS GRID_FIRST_Export: four distinct Grid/Grid/Canvas/Grid roots; full ZIP; explicit local sample rows, no live database claim.");return 0;
        }
        catch(Exception error){Console.WriteLine("FAIL GRID_FIRST_Export: "+error);return 1;}
    }
}
