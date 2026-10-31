using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void RunLayoutPropertyChecks(Action<string,Action> check)
    {
        check("EmptyNativeContentMatchesAxaml",()=>
        {
            var vm=ResponsiveScene("A"); vm.Controls.Single(c=>c.Name=="Title").Text=""; vm.Controls.Single(c=>c.Name=="SaveButton").Text="";
            // An authored 1 DIP minimum keeps the empty label in the actual visual tree.
            LayoutPlacementProperties.SetLiteral(vm.Controls.Single(c=>c.Name=="Title"),"MinHeight",1d);
            vm.Controls.Single(c=>c.Name=="NameInput").PlaceholderText="";
            var window=CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(100);
            try
            {
                using var legacy=ShowDataGridRegressionPreview(vm,false); using var axaml=ShowDataGridRegressionPreview(vm,true);
                var d=ObserveDesigner(window,vm,"Empty"); var l=ObservePreview(legacy.Window,vm,"Empty",false); var a=ObservePreview(axaml.Window,vm,"Empty",true);
                AssertLayoutMatches(a,l); AssertLayoutMatches(a,d);
            }
            finally { window.CloseForExternalHost(); }
        });
        check("ReservedNamespacesRuntimeLoader",()=>
        {
            var assembly=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NamespaceProbe"+Guid.NewGuid().ToString("N")),System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var constructor=typeof(Avalonia.Metadata.XmlnsDefinitionAttribute).GetConstructor(new[]{typeof(string),typeof(string)})!;
            assembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(constructor,new object[]{"https://github.com/avaloniaui","Fixture.Extensions"}));
            assembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(constructor,new object[]{"urn:fixture:widgets","Fixture.Widgets"}));
            const string source="<Grid xmlns=\"https://github.com/avaloniaui\" xmlns:w=\"urn:fixture:widgets\"><!--keep--><Button Content=\"{Binding Value}\"/><w:Widget/></Grid>";
            var method=typeof(FormDesigner.Services.RuntimeAxamlPreviewLoader).GetMethod("QualifyPluginNamespaces",BindingFlags.NonPublic|BindingFlags.Static)!;
            var result=(string)method.Invoke(null,new object[]{source,new Assembly[]{assembly}})!; var xml=System.Xml.Linq.XDocument.Parse(result);
            RequireCapability(xml.Root!.Name.NamespaceName=="https://github.com/avaloniaui" && xml.Root.Elements().First().Name.NamespaceName=="https://github.com/avaloniaui","Plugin metadata hijacked the reserved Avalonia namespace.");
            RequireCapability(xml.Root.Elements().Last().Name.NamespaceName.StartsWith("clr-namespace:Fixture.Widgets;") && result.Contains("<!--keep-->") && result.Contains("{Binding Value}"),"Runtime qualification lost aliases/comments/bindings.");
        });
        check("ConstraintInspectorJsonUndo", () =>
        {
            var vm = NestedModel();
            // Use the real completed Toolbox command boundary before editing properties.
            var c = vm.TryCreateControlFromToolboxDrop("Button",20,20,null,false,vm.ActiveDocumentId)!; vm.SelectSingleControl(c); var width = c.Width;
            foreach (var entry in new[] { ("MinWidth","100"), ("MaxWidth","180"), ("MinHeight","30"), ("MaxHeight","60") })
            {
                var row = vm.PropertyGridCategories.SelectMany(g => g.Rows).Single(r => r.Key == entry.Item1); row.Value = entry.Item2; row.CommitValue();
            }
            RequireCapability(c.WidthMode == "Auto" && c.Width == width && vm.SelectedControl?.Id == c.Id, "Constraint edit changed authored size/selection.");
            vm.GenerateXaml(); RequireContains(vm.GeneratedXaml, "MaxWidth=\"180\"", "MaxWidth was not exported."); RequireContains(vm.GeneratedXaml, "MaxHeight=\"60\"", "MaxHeight was not exported.");
            var restored = CreateViewModel("ConstraintsReload"); restored.LoadDocumentJson(vm.ExportWorkspaceJson());
            RequireCapability(LayoutPlacementProperties.Maximum(restored.Controls.Single(), "MaxWidth") == 180, "JSON lost constraints.");
            GridEditorUndo(vm); RequireCapability(vm.Controls.Single().CustomProperties.Count < 4, "Constraint edit did not enter Undo history.");
            GridEditorUndo(vm, true); RequireCapability(LayoutPlacementProperties.Maximum(vm.Controls.Single(), "MaxHeight") == 60, "Redo lost MaxHeight.");
            var current = vm.Controls.Single(); LayoutPlacementProperties.SetLiteral(current, "MaxWidth", "{Binding Limit}"); vm.SelectSingleControl(current);
            typeof(MainWindowViewModel).GetMethod("RebuildPropertyGrid",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(vm,new object[]{"BindingConstraintRegression"});
            RequireCapability(vm.PropertyGridCategories.SelectMany(g => g.Rows).Single(r => r.Key == "MaxWidth").IsReadOnly, "Constraint expression became a numeric editor.");
        });
        check("FingerprintCacheKeepsStaleGuards", () =>
        {
            var vm = NestedModel(); var c = vm.CreateControl("Button",0,0); LayoutPlacementProperties.SetLiteral(c,"MinWidth",10d); MeasureNested(vm);
            var first = vm.GridDropLayoutKey; for (var i=0;i<100;i++) RequireCapability(ReferenceEquals(first,vm.GridDropLayoutKey), "Unchanged fingerprint was serialized again.");
            var point=new Point(20,20); var key=vm.ResolveLayoutDropTarget(point,c).GeometryKey; MeasureNested(vm);
            RequireCapability(vm.ResolveLayoutDropTarget(point,c).GeometryKey==key,"An unchanged native redraw invalidated the gesture token.");
            c.CustomProperties.Single(p=>p.Key=="MinWidth").ValueJson = "200";
            RequireCapability(first != vm.GridDropLayoutKey && !vm.ResolveLayoutDropTarget(new Point(20,20),c).IsValid, "Direct CustomProperty edit reused stale geometry.");
            MeasureNested(vm); first = vm.GridDropLayoutKey; vm.DesignWidth += 20; RequireCapability(first != vm.GridDropLayoutKey, "Viewport resize did not invalidate fingerprint.");
            MeasureNested(vm); first = vm.GridDropLayoutKey; vm.Controls.Remove(c); RequireCapability(first != vm.GridDropLayoutKey, "Removal reused a cached tree.");
        });
        check("LayoutWarningsDoNotMutateDocument", () =>
        {
            var vm = NestedModel(); var canvas = NestedHost(vm,DesignerLayoutModes.Absolute); var a = NestedLeaf(vm,canvas,"Overflow"); a.Width=500;
            var b = NestedLeaf(vm,canvas,"Overlap",1); b.X=20;
            LayoutPlacementProperties.SetLiteral(a,"MinWidth",300d); LayoutPlacementProperties.SetLiteral(a,"MaxWidth",100d);
            MeasureNested(vm); var before=JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()); vm.RefreshDiagnostics();
            RequireCapability(vm.Diagnostics.Any(d=>d.Category=="Layout" && d.Message.Contains("MinWidth")) && vm.Diagnostics.Any(d=>d.Category=="Layout" && d.Message.Contains("перекрываются") && d.Severity==DocumentDiagnosticSeverity.Info), "Native constraints/intentional overlap diagnostics are missing.");
            RequireCapability(vm.Diagnostics.Any(d=>d.Category=="Layout" && d.Message.Contains("Фиксированный Width")), "Fixed overflow warning is missing.");
            RequireCapability(before==JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()), "Diagnostics rewrote the layout.");
        });
        check("ThreeFormLayoutIsolationAndHistory", () =>
        {
            var vm=CreateViewModel("ThreeLayouts"); vm.LoadDocumentJson(JsonSerializer.Serialize(ResponsiveScene("A").CreatePreviewDocumentSnapshot())); var gridForm=vm.ActiveFormDocument!.Id;
            vm.CreateNewForm(); var canvasForm=vm.ActiveFormDocument!.Id; var canvas=vm.CreateControl("Button",37,53,null,true); var canvasId=canvas.Id;
            vm.CreateNewForm("Grid"); var nestedForm=vm.ActiveFormDocument!.Id; var host=NestedHost(vm,DesignerLayoutModes.Stack); var child=NestedLeaf(vm,host,"nested"); var childId=child.Id;
            foreach(var id in new[]{gridForm,canvasForm,nestedForm,canvasForm,gridForm,nestedForm}) vm.SetActiveForm(id,"layout6-isolation");
            RequireCapability(vm.Controls.Single(c=>c.Id==childId).ParentId==host.Id, "Nested parent was lost between forms.");
            var restored=CreateViewModel("ThreeReload"); restored.LoadDocumentJson(vm.ExportWorkspaceJson());
            restored.SetActiveForm(canvasForm,"verify"); RequireCapability(!restored.UsesNativeLayout && restored.Controls.Single(c=>c.Id==canvasId).X==37, "Canvas migrated or lost coordinates.");
            restored.SetActiveForm(nestedForm,"verify"); RequireCapability(restored.Controls.Single(c=>c.Id==childId).ParentId==host.Id, "JSON lost nested ParentId.");
        });
        foreach(var zoom in new[]{.5,.75,1,1.25,1.5,2})
            check("ZoomRouted_"+zoom,()=>
            {
                using var f=NestedPointerFixture(); var a=NestedHost(f.Vm,DesignerLayoutModes.Stack); var b=NestedHost(f.Vm,DesignerLayoutModes.Stack,1); var c=NestedLeaf(f.Vm,a,"Zoom");
                typeof(MainWindow).GetMethod("SetSurfaceZoom",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(f.Window,new object?[]{zoom,null}); f.Refresh();
                var actualZoom=(double)typeof(MainWindow).GetField("_surfaceZoom",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Window)!;
                RequireCapability(Math.Abs(actualZoom-zoom)<.001,$"Requested zoom {zoom} was silently replaced by {actualZoom}.");
                var scroll=f.Surface.GetVisualDescendants().OfType<ScrollViewer>().Single(s=>s.Name=="DesignerViewportScrollViewer"); scroll.Offset=default;
                NativeItemsUiWait(100); f.Window.UpdateLayout();
                var commit=Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(f.Window)!.Compositor.RequestCommitAsync(); var deadline=DateTime.UtcNow.AddSeconds(3);
                while(!commit.IsCompleted && DateTime.UtcNow<deadline) NativeItemsUiWait(20);
                NestedPointerMove(f,c,NestedPoint(f.Vm,b.Id)); RequireCapability(c.ParentId==b.Id && f.Vm.SelectedControl?.Id==c.Id,"Zoom/DIP routing changed the target or selection.");
                if(zoom==1 || zoom==2)
                {
                    var content=(Control)f.Window.Content!; using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width,(int)content.Bounds.Height));
                    image.Render(content); image.Save(Path.Combine(LayoutPhase6Folder,"zoom-"+(int)(zoom*100)+".png"));
                }
                Console.WriteLine($"OBSERVED layout6 zoom={zoom}; RenderScaling={f.Window.RenderScaling}; scroll={scroll.Offset}");
            });
    }
}
