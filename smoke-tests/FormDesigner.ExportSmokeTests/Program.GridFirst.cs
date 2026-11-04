using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void RequireGridFirst(MainWindowViewModel vm)
    {
        RequireCapability(vm.UsesNativeLayout&&vm.SurfaceLayoutMode=="Grid"&&vm.SurfaceLayoutRows==1&&vm.SurfaceLayoutColumns==1&&
            vm.SurfaceGridRowDefinitions=="*"&&vm.SurfaceGridColumnDefinitions=="*"&&vm.SurfaceLayoutSpacing==0,"New form is not the established one-cell native Grid.");
    }
    private static int RunGridFirstChecks()
    {
        Directory.CreateDirectory(GridFirstFolder);var passed=0;var failed=0;
        void Check(string name,Action action)
        {
            var filter=Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{action();passed++;Console.WriteLine("PASS GRID_FIRST_"+name);}
            catch(Exception error){failed++;Console.WriteLine("FAIL GRID_FIRST_"+name+": "+error);}
            finally{foreach(var window in SmokeWindows)window.CloseForExternalHost();SmokeWindows.Clear();}
        }
        Check("ConstructorDefault",()=>RequireGridFirst(GridFirstVm()));
        Check("NewProjectDefault",()=>{var vm=GridFirstVm();vm.CreateControl("Button",0,0);vm.NewProjectEditorCommand!.Execute(null);RequireGridFirst(vm);RequireCapability(vm.Controls.Count==0&&vm.CurrentProject.Forms.Count==1,"New Project retained old contents.");});
        Check("WorkspaceFactoryDefault",()=>RequireCapability(new ProjectWorkspaceService().CreateWorkspace().Project.Forms.Single().Document.LayoutSemanticsVersion==2,"Workspace factory missed Grid default."));
        Check("AddFormDefaults",()=>{var vm=GridFirstVm();foreach(var i in Enumerable.Range(0,3)){vm.CreateNewForm();RequireGridFirst(vm);}RequireCapability(vm.CurrentProject.Forms.Count==4,"Adding forms lost documents.");});
        Check("DocumentServiceDefault",()=>{var project=new DesignerProjectModel();var form=new ProjectDocumentService().AddForm(project);RequireCapability(form.Document.SurfaceLayoutMode=="Grid"&&form.Document.LayoutSemanticsVersion==2,"Document service missed Grid default.");});
        Check("CanvasChoiceIsOneShot",()=>
        {
            var vm=GridFirstVm();vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewFormEditorCommand!.Execute(null);
            RequireCapability(!vm.UsesNativeLayout&&vm.SurfaceLayoutMode=="Absolute","Explicit Canvas ignored.");
            RequireCapability(vm.NewFormLayoutChoice==MainWindowViewModel.NewFormGridChoice,"Canvas choice leaked into the next creation.");
            NativeItemsUiWait(80);vm.NewFormEditorCommand!.Execute(null);RequireGridFirst(vm);
        });
        Check("CanvasNewProjectChoiceIsOneShot",()=>
        {
            var vm=GridFirstVm();vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);
            RequireCapability(!vm.UsesNativeLayout&&vm.SurfaceLayoutMode=="Absolute","New Project ignored explicit Canvas.");
            vm.NewProjectEditorCommand!.Execute(null);RequireGridFirst(vm);
        });
        foreach(var explicitRoot in new[]{false,true})Check("LegacyJsonCanvas_"+explicitRoot,()=>
        {
            var vm=GridFirstVm();var json=explicitRoot?GridFirstLegacyJson():"{\"Controls\":[{\"Id\":\"legacy\",\"Type\":\"Button\",\"Name\":\"LegacyButton\",\"X\":37,\"Y\":53,\"Width\":140,\"Height\":40}]}";
            vm.LoadDocumentJson(json);var original=vm.Controls.Select(c=>new{c.Id,c.X,c.Y,c.Width,c.Height}).ToArray();
            RequireCapability(!vm.UsesNativeLayout&&vm.SurfaceLayoutMode=="Absolute","Old JSON migrated to Grid.");
            var restored=GridFirstVm();restored.LoadDocumentJson(vm.ExportWorkspaceJson());
            RequireCapability(!restored.UsesNativeLayout&&JsonSerializer.Serialize(original)==JsonSerializer.Serialize(restored.Controls.Select(c=>new{c.Id,c.X,c.Y,c.Width,c.Height})),"Canvas Save/Load changed geometry or identity.");
            restored.GenerateXaml();RequireCapability(restored.GeneratedXaml.Contains("RootCanvas")&&restored.GeneratedXaml.Contains("Canvas.Left="),"Legacy Canvas export migrated.");
        });
        Check("LegacyWorkspaceFallback",()=>
        {
            var service=new ProjectWorkspaceService();RequireCapability(service.TryDeserializeWorkspace("{\"Project\":{\"Forms\":[{\"Name\":\"Old\",\"Document\":{\"Controls\":[]}}]},\"Session\":{}}",out var old),"Old workspace rejected.");
            RequireCapability(old.Project.Forms.Single().Document.SurfaceLayoutMode=="Absolute"&&old.Project.Forms.Single().Document.LayoutSemanticsVersion==1,"Workspace fallback inherited the new-creation default.");
            var empty=new WorkspaceModel();service.EnsureWorkspaceDefaults(empty);RequireCapability(empty.Project.Forms.Single().Document.SurfaceLayoutMode=="Absolute","Repair fallback changed historical semantics.");
        });
        Check("GridSaveLoad",()=>
        {
            var vm=GridFirstVm();vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;vm.SurfaceGridColumnDefinitions="2*,*";vm.SurfaceLayoutColumns=2;
            var button=vm.CreateControl("Button",0,0);button.GridRow=1;button.GridColumnSpan=2;button.Margin="8,4";
            var restored=GridFirstVm();restored.LoadDocumentJson(vm.ExportWorkspaceJson());
            RequireCapability(restored.UsesNativeLayout&&restored.SurfaceGridRowDefinitions=="Auto,*"&&restored.Controls.Single().Id==button.Id&&restored.Controls.Single().GridColumnSpan==2&&restored.Controls.Single().Margin=="8,4","Grid JSON lost layout.");
        });
        foreach(var type in new[]{"Button","TextBox","DataGrid","ListBox","SplitButton","DropDownButton","StackPanel"})Check("RoutedToolbox_"+type,()=>
        {
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();RequireGridFirst(f.Vm);
            f.Vm.AddGridRowDefinitionCommand.Execute(null);f.Vm.AddGridColumnDefinitionCommand.Execute(null);f.Refresh();
            var p=GridCellPoint(f.Vm,"",1,1);var data=GridToolboxData(f,type);
            RequireCapability(RouteGridToolbox(f,data,DragDrop.DragOverEvent,p).DragEffects==DragDropEffects.Copy,"Grid hover rejected new default form.");
            RouteGridToolbox(f,data,DragDrop.DropEvent,p);f.Refresh();f.AssertIdle();var control=f.Vm.Controls.Single();
            RequireCapability(control.ParentId==""&&control.GridRow==1&&control.GridColumn==1&&control.X==0&&control.Y==0&&f.Vm.SelectedControl?.Id==control.Id,"Drop lost placement/selection.");
            RequireCapability(DropWrappers(f.Window).ContainsKey(control.Id)&&f.Surface.Session?.SelectedControl?.Id==control.Id,"Designer/session selection missing.");
            var rows=f.Vm.PropertyGridCategories.SelectMany(c=>c.Rows).ToArray();
            RequireCapability(rows.Any(r=>r.Key=="GridRow"&&r.Value=="1")&&rows.Any(r=>r.Key=="GridColumn"&&r.Value=="1")&&rows.All(r=>r.Key is not ("X" or "Y")),"Inspector did not switch to Grid placement.");
            var id=control.Id;GridEditorUndo(f.Vm);RequireCapability(f.Vm.Controls.Count==0,"Drop was not atomic.");GridEditorUndo(f.Vm,true);RequireCapability(f.Vm.Controls.Single().Id==id&&f.Vm.Controls.Single().GridRow==1,"Redo changed identity/placement.");
        });
        Check("ExplicitCanvasRoutedMoveResize",()=>
        {
            using var f=new PointerFixture();f.Vm.CreateNewForm("Absolute");f.Refresh();f.DropToolbox(new Point(180,180));f.Refresh();
            var c=f.First;var before=new Point(c.X,c.Y);var p=f.ControlPoint(c);f.Down(p);f.Move(p+new Vector(60,40));f.Up(p+new Vector(60,40));f.Refresh();
            RequireCapability(new Point(c.X,c.Y)!=before&&!f.Vm.UsesNativeLayout,"Canvas move stopped using free coordinates.");
            var width=c.Width;p=f.ResizePoint();f.Down(p);f.Move(p+new Vector(40,30));f.Up(p+new Vector(40,30));f.AssertIdle();RequireCapability(c.Width>width,"Canvas resize failed.");
            var rows=f.Vm.PropertyGridCategories.SelectMany(g=>g.Rows).ToArray();RequireCapability(rows.Any(r=>r.Key=="X")&&rows.Any(r=>r.Key=="Y"),"Canvas Inspector lost coordinates.");
        });
        Check("MultiFormIsolation",()=>
        {
            var vm=GridFirstVm();var first=vm.ActiveDocumentId;var button=vm.CreateControl("Button",0,0);vm.SelectSingleControl(button);
            var second=vm.CreateNewForm().Id;vm.CreateControl("TextBox",0,0);var canvas=vm.CreateNewForm("Absolute").Id;
            var absolute=vm.CreateControl("Button",37,53,null,true);var fourth=vm.CreateNewForm().Id;
            var stack=vm.CreateControl("StackPanel",0,0);var grid=vm.CreateControl("DataGrid",0,0,stack.Id);
            foreach(var id in new[]{first,second,canvas,fourth,first})
            {
                vm.SetActiveForm(id,"grid-first-isolation");RequireCapability(vm.UsesNativeLayout==(id!=canvas),"Root leaked across forms.");
                if(id==first){var c=vm.Controls.Single();vm.SelectSingleControl(c);RequireCapability(c.Id==button.Id&&ReferenceEquals(vm.SelectedControl,vm.ActiveSession.SelectedControl),"Inspector selection points into another session.");}
            }
            var restored=GridFirstVm();restored.LoadDocumentJson(vm.ExportWorkspaceJson());restored.SetActiveForm(canvas,"canvas-reopen");RequireCapability(!restored.UsesNativeLayout&&restored.Controls.Single().X==37,"Mixed Save/Load migrated Canvas.");
            restored.SetActiveForm(fourth,"nested-reopen");RequireCapability(restored.Controls.Single(c=>c.Id==grid.Id).ParentId==stack.Id,"Mixed Save/Load lost ParentId.");
            var renamed=restored.CurrentProject.Forms.Single(f=>f.Id==second);var item=new ProjectExplorerItemModel{Source=renamed};
            renamed.Name="Renamed";restored.CommitProjectExplorerRenameCommand.Execute(item);RequireCapability(renamed.Document.FormTitle=="Renamed","Rename failed.");
            restored.DeleteProjectExplorerItemCommand.Execute(item);RequireCapability(restored.CurrentProject.Forms.Count==3,"Delete damaged project.");
        });
        foreach(var mode in new[]{false,true})Check("PreviewResize_"+mode,()=>
        {
            var vm=GridFirstVm();vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;
            vm.CreateControl("TextBlock",0,0).Text="Header";var model=vm.CreateControl("DataGrid",0,0);model.GridRow=1;model.ShowFilterRow=model.ShowFooter=model.ShowGroupPanel=false;
            using var preview=ShowDataGridRegressionPreview(vm,mode);var grid=preview.Window.GetVisualDescendants().OfType<DataGrid>().Single();var size=grid.Bounds.Size;
            preview.Window.Width+=240;preview.Window.Height+=120;NativeItemsUiWait(200);preview.Window.UpdateLayout();
            RequireCapability(grid.GetVisualRoot()==preview.Window&&grid.Bounds.Width>size.Width+200&&grid.Bounds.Height>size.Height+80,"New Grid Preview did not resize.");
        });
        Check("PreviewSettingsUnchanged",()=>
        {
            var vm=GridFirstVm();RequireCapability(vm.ResolvePreviewMode().PreviewMode=="AXAML","AXAML Preview default lost.");
            vm.UseExportedAxamlPreview=false;vm.NewProjectEditorCommand!.Execute(null);RequireCapability(vm.ResolvePreviewMode().PreviewMode=="Legacy","New Grid project overwrote explicit Legacy.");
        });
        Check("GridExportIgnoresResponsiveConversion",()=>
        {
            var vm=GridFirstVm();vm.CreateControl("Button",0,0);vm.LayoutExportMode=MainWindowViewModel.LayoutExportModeResponsive;vm.GenerateXaml();
            var root=XDocument.Parse(vm.GeneratedXaml).Root!.Elements().Last();RequireCapability(root.Name.LocalName=="Grid"&&!vm.GeneratedXaml.Contains("RootCanvas")&&!vm.GeneratedXaml.Contains("Canvas.Left="),"New Grid was converted during Export.");
        });
        foreach(var root in new[]{"Canvas","Grid"})Check("ImportedRoot_"+root,()=>
        {
            var text="<Window xmlns=\"https://github.com/avaloniaui\"><"+root+"><!-- retained --><Button Name=\"ImportedButton\" Content=\"Keep\"/></"+root+"></Window>";
            var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(text),"existing.axaml");
            RequireCapability(vm.IsAxamlRoundTripDocument&&!vm.UsesNativeLayout&&vm.ResolvePreviewMode().PreviewMode=="Legacy","Creation default leaked into imported workflow.");
            RequireCapability((vm.ActiveAxamlSourceDocument!.SourceMap.CanvasElement is not null)==(root=="Canvas"),"Source root was replaced.");
            var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&!patch.HasChanges&&patch.PatchedText==text,"Import modified the source.");
        });
        Console.WriteLine($"Grid-first checks: {passed}/{passed+failed}");return failed==0?0:1;
    }
}
