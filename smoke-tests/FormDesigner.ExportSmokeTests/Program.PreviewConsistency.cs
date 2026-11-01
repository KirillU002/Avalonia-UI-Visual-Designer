using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using FormDesigner.PluginContracts;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string ConsistencyFolder => Path.Combine(FindRepositoryRoot(),"artifacts","preview-consistency-audit");
    private static MainWindowViewModel AllRegistryScene(bool native)
    {
        var vm=CreateViewModel("RegistryAudit");
        vm.LoadDocumentJson(JsonSerializer.Serialize(new DesignerDocumentFileModel{LayoutSemanticsVersion=native?2:1,
            SurfaceLayoutMode=native?DesignerLayoutModes.Grid:DesignerLayoutModes.Absolute,SurfaceLayoutColumns=4,SurfaceLayoutRows=4,
            SurfaceGridColumnDefinitions="*,*,*,*",SurfaceGridRowDefinitions="*,*,*,*",DesignWidth=1200,DesignHeight=800}));
        var entries=vm.Registry.GetControls().ToArray();
        for(var i=0;i<entries.Length;i++)
        {
            var c=vm.CreateControl(entries[i].TypeKey,0,0);c.Name="Audit_"+entries[i].TypeKey;c.StackOrder=i;
            c.Width=240;c.Height=140;c.WidthMode=c.HeightMode="Fixed";c.GridRow=i/4;c.GridColumn=i%4;c.X=(i%4)*290+10;c.Y=(i/4)*190+10;
            c.Margin="3";
        }
        vm.DataGridExportMode=MainWindowViewModel.DataGridExportModeReal;vm.IncludeSampleData=false;
        return vm;
    }
    private static int RunPreviewConsistencyChecks()
    {
        Environment.SetEnvironmentVariable("FORMDESIGNER_PREVIEW_DIAGNOSTICS","1");
        Directory.CreateDirectory(ConsistencyFolder);var failed=0;var passed=0;var observations=new List<object>();
        void Check(string name,Action action){try{action();passed++;Console.WriteLine("PASS CONSISTENCY_"+name);}catch(Exception e){failed++;Console.WriteLine("FAIL CONSISTENCY_"+name+": "+e);}}
        foreach(var native in new[]{false,true})
        {
            var vm=AllRegistryScene(native);var scene=native?"RegistryNative":"RegistryCanvas";
            File.WriteAllText(Path.Combine(ConsistencyFolder,scene+".json"),vm.ExportWorkspaceJson());
            File.WriteAllText(Path.Combine(ConsistencyFolder,scene+".axaml"),vm.GenerateExportedAxamlPreviewSnapshot().ExportAxaml);
            foreach(var axaml in new[]{false,true})Check(scene+"_"+(axaml?"AXAML":"Legacy"),()=>
            {
                using var preview=ShowDataGridRegressionPreview(vm,axaml);
                var host=preview.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                preview.Window.Width=1200;preview.Window.Height=800;NativeItemsUiWait(150);preview.Window.UpdateLayout();
                var views=host.GetVisualDescendants().OfType<Control>().ToArray();
                var rows=new List<object>();
                for(var i=0;i<vm.Controls.Count;i++)
                {
                    var model=vm.Controls[i];Control? view=views.FirstOrDefault(c=>c.Name==model.Name);
                    if(view is null&&!native&&!axaml&&preview.Window.FindControl<Canvas>("PreviewCanvas")!.Children.ElementAtOrDefault(i) is Border{Child:Canvas frame})view=frame.Children.FirstOrDefault();
                    var point=view?.TranslatePoint(default,host);
                    rows.Add(new{Name=model.Name,model.Type,NativeType=view?.GetType().FullName,Attached=view?.GetVisualRoot()!=null,Visible=view?.IsVisible,
                        X=point?.X,Y=point?.Y,Width=view?.Bounds.Width,Height=view?.Bounds.Height,Parent=view?.GetVisualParent()?.GetType().Name});
                }
                observations.Add(new{Scene=scene,Mode=axaml?"AXAML":"Legacy",Status=preview.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text,Rows=rows});
                Console.WriteLine("TREE "+JsonSerializer.Serialize(observations[^1]));
                RequireCapability(rows.Count==vm.Controls.Count,"Model/control count mismatch.");
                RequireCapability(views.Length>vm.Controls.Count,"No native visual tree.");
            });
        }
        File.WriteAllText(Path.Combine(ConsistencyFolder,"baseline-trees.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
        Check("RootResources",()=>
        {
            const string source="""
<Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" RequestedThemeVariant="Dark" Width="800" Height="600">
 <Window.Resources><SolidColorBrush x:Key="Accent">#FF125634</SolidColorBrush></Window.Resources>
 <Window.Styles><Style Selector="Button.audit"><Setter Property="FontSize" Value="23"/></Style></Window.Styles>
 <Grid ColumnDefinitions="*,*"><Button Name="ResourceButton" Classes="audit" Background="{DynamicResource Accent}" Content="Resource"/>
 <Button Grid.Column="1" Name="StaticButton" Background="{StaticResource Accent}" Content="Static"/></Grid>
</Window>
""";
            var transformed=GeneratedAxamlService.Create(source,AxamlGenerationMode.RuntimePreview,true,true);
            var loaded=(Control)RuntimeAxamlPreviewLoader.Load(transformed.Axaml);
            var p=new PreviewWindow(new ExportedAxamlPreviewModel{Axaml=transformed.Axaml,ExportAxaml=source,FallbackToLegacyPreviewOnError=false},CreateViewModel("resources").Registry);
            p.Position=new PixelPoint(-3000,-3000);p.Show();NativeItemsUiWait(350);
            try{var button=p.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ResourceButton");
                Console.WriteLine("RESOURCE background="+button.Background+" variant="+button.ActualThemeVariant);
                RequireCapability(button.Background?.ToString()=="#ff125634","Root resource lost during attachment.");
                RequireCapability(button.FontSize==23,"Root Style was lost.");
                RequireCapability(p.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="StaticButton").Background?.ToString()=="#ff125634","StaticResource was lost.");
                RequireCapability(button.ActualThemeVariant==Avalonia.Styling.ThemeVariant.Dark,"Root ThemeVariant lost during adaptation.");}
            finally{p.Close();}
        });
        foreach(var axaml in new[]{false,true})
        {
            Check("ImportedSourceLimitsAreExplicit_"+axaml,()=>
            {
                const string source="""
<Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Width="800" Height="600">
 <!-- retained source -->
 <Window.Resources><SolidColorBrush x:Key="ImportedAccent">Red</SolidColorBrush></Window.Resources>
 <Grid><ComboBox Name="ImportedCombo"><ComboBoxItem Content="One"/></ComboBox><Button Name="ImportedButton" Content="Preserved"/></Grid>
</Window>
""";
                var vm=CreateViewModel("ImportedAudit");
                vm.LoadAxamlImportedDocument(new FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportService().Import(source),"audit.axaml");
                var before=vm.CreateActiveAxamlPatch();RequireCapability(before.CanApply&&!before.HasChanges,"Import changed source.");
                using var p=ShowDataGridRegressionPreview(vm,axaml);
                var combo=vm.Controls.Single(c=>c.Name=="ImportedCombo");
                RequireCapability(p.Window.ControlDiagnostics.Any(d=>d.ControlId==combo.Id&&d.FailureReason.StartsWith("UnsupportedControl")),"Source proxy silently lost its original native type.");
                RequireCapability(p.Window.BuildIssues.Any(d=>d.Message.Contains("ресурсы")),"Lost source resources were not explained.");
                var after=vm.CreateActiveAxamlPatch();RequireCapability(after.CanApply&&!after.HasChanges&&after.PatchedText==source,"Preview modified the source-preserving session.");
                Console.WriteLine("IMPORTED_LIMIT mode="+axaml+"; nativeCombo="+p.Window.GetVisualDescendants().OfType<ComboBox>().Count()+"; modelType="+combo.Type);
            });
            Check("MissingDescriptorIsReported_"+axaml,()=>
            {
                var vm=AllRegistryScene(true);var bad=vm.Controls.Single(c=>c.Type=="TextBlock");bad.Type="Unavailable.AuditControl";
                using var p=ShowDataGridRegressionPreview(vm,axaml);
                RequireCapability(p.Window.GetVisualDescendants().OfType<Button>().Any(c=>c.Name=="Audit_Button"),"One unavailable type removed other components.");
                RequireCapability(p.Window.BuildIssues.Any(),"An unavailable control was silently replaced.");
                RequireCapability(p.Window.ControlDiagnostics.Any(d=>d.ControlId==bad.Id&&d.FailureReason.StartsWith("MissingDescriptor")),"The missing descriptor was not identified.");
            });
            foreach(var native in new[]{false,true})Check("OpacityAppliedOnce_"+native+"_"+axaml,()=>
            {
                var vm=AllRegistryScene(native);var model=vm.Controls.Single(c=>c.Type=="ListBox");model.Opacity=.5;
                using var p=ShowDataGridRegressionPreview(vm,axaml);
                var list=p.Window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name==model.Name);
                var effective=list.GetVisualAncestors().OfType<Control>().Prepend(list).Aggregate(1d,(value,c)=>value*c.Opacity);
                RequireCapability(Math.Abs(effective-.5)<.001,"Authored Opacity=.5 became "+effective);
            });
            Check("CanvasManagedNativeTypes_"+axaml,()=>
            {
                var vm=AllRegistryScene(false);using var p=ShowDataGridRegressionPreview(vm,axaml);
                var host=p.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                RequireCapability(host.GetVisualDescendants().OfType<WrapPanel>().Any(),"Canvas form contains a WrapPanel model but no native WrapPanel.");
            });
            Check("AuthoredVisibility_"+axaml,()=>
            {
                var vm=AllRegistryScene(true);vm.Controls.Single(c=>c.Type=="TextBlock").IsVisible=false;
                using var p=ShowDataGridRegressionPreview(vm,axaml);
                RequireCapability(p.Window.GetVisualDescendants().OfType<Control>().Single(c=>c.Name=="Audit_TextBlock").IsVisible==false,"Authored hidden control was shown.");
            });
            Check("AuthoredEnabled_"+axaml,()=>
            {
                var vm=AllRegistryScene(true);var model=vm.Controls.Single(c=>c.Type=="ListBox");
                StandardItemsProperties.Write(model,"IsEnabled",false);
                using var p=ShowDataGridRegressionPreview(vm,axaml);
                RequireCapability(p.Window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name==model.Name).IsEnabled==false,"Authored disabled ListBox was enabled.");
            });
            Check("FailedContributionKeepsStandard_"+axaml,()=>
            {
                var vm=AllRegistryScene(true);vm.Registry.RegisterRuntimePreviewContributionProvider(new BrokenConsistencyProvider());
                using var p=ShowDataGridRegressionPreview(vm,axaml);
                RequireCapability(p.Window.GetVisualDescendants().OfType<Button>().Any(c=>c.Name=="Audit_Button"),"A failing plugin initializer removed standard controls.");
            });
        }
        Check("JsonSaveLoadRegistryAndHierarchy",()=>
        {
            foreach(var original in new[]{AllRegistryScene(false),AllRegistryScene(true),ConsistencyNestedScene()})
            {
                var json=original.ExportWorkspaceJson();var restored=CreateViewModel("restored-audit");restored.LoadDocumentJson(json);
                RequireCapability(original.Controls.Select(c=>(c.Id,c.Type,c.ParentId,c.StackOrder,c.GridRow,c.GridColumn,c.GridRowSpan,c.GridColumnSpan,c.Margin,c.Padding,c.HorizontalAlignment,c.VerticalAlignment))
                    .SequenceEqual(restored.Controls.Select(c=>(c.Id,c.Type,c.ParentId,c.StackOrder,c.GridRow,c.GridColumn,c.GridRowSpan,c.GridColumnSpan,c.Margin,c.Padding,c.HorizontalAlignment,c.VerticalAlignment))),"JSON lost hierarchy, order or layout.");
                foreach(var mode in new[]{false,true})using(ShowDataGridRegressionPreview(restored,mode)){}
            }
        });
        Check("ExportZipPackagesDesignerImage",()=>
        {
            var vm=AllRegistryScene(true);var original=vm.Controls.Single(c=>c.Type=="Image").ImageSource;
            var zip=Path.Combine(ConsistencyFolder,"RegistryAssets.zip");var task=vm.ExportCurrentResultAsZipAsync(zip);
            while(!task.IsCompleted)NativeItemsUiWait(1);task.GetAwaiter().GetResult();
            using var archive=System.IO.Compression.ZipFile.OpenRead(zip);
            RequireCapability(archive.Entries.Any(e=>e.FullName=="Assets/Designer/avalonia-logo.ico"&&e.Length>0),"ZIP lost the embedded designer image.");
            using var project=new StreamReader(archive.Entries.Single(e=>e.FullName.EndsWith(".csproj")).Open());
            RequireCapability(project.ReadToEnd().Contains("AvaloniaResource Include=\"Assets/Designer/**\""),"ZIP csproj does not embed packaged assets.");
            using var axaml=new StreamReader(archive.Entries.Single(e=>e.FullName.EndsWith("MainWindow.axaml")).Open());
            var text=axaml.ReadToEnd();RequireCapability(text.Contains("/Assets/Designer/avalonia-logo.ico")&&!text.Contains("avares://FormDesigner/Assets/"),"ZIP still references the designer assembly.");
            RequireCapability(vm.Controls.Single(c=>c.Type=="Image").ImageSource==original,"Export mutated the document resource.");
        });
        RunConsistencyGeometry(Check);
        Console.WriteLine($"Preview consistency checks: {passed}/{passed+failed}");return failed==0?0:1;
    }
    private sealed class BrokenConsistencyProvider:IDesignerRuntimePreviewContributionProvider
    {
        public string ProviderId=>"Audit.Broken";
        public DesignerRuntimePreviewContribution GetRuntimePreviewContribution(DesignerRuntimePreviewContributionContext context)=>new()
        {ProviderId=ProviderId,Initialize=()=>throw new InvalidOperationException("Unavailable experimental plugin theme")};
    }
}
