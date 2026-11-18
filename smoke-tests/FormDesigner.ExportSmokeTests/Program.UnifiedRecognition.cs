using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.ViewModels;
using FormDesigner.Services;
using FormDesigner.Models;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

// Metadata inspection is permitted; execution during recognition fails the fixture.
public sealed class RecognitionProbeControl : Button
{
    public string? CategoryCode { get; set; }
    public RecognitionProbeControl() => throw new InvalidOperationException("Custom control constructor executed");
}

internal static partial class Program
{
    private const string RecognitionFixture = "smoke-tests/UniversalMetadata/Phase7ARecognition.axaml";

    private static IEnumerable<AxamlElementSyntax> SourceDescendants(AxamlElementSyntax node) =>
        new[]{node}.Concat(node.Children.SelectMany(SourceDescendants));

    private static int RunRecognitionChecks(string results)
    {
        Directory.CreateDirectory(results);
        var source = File.ReadAllText(RecognitionFixture);
        var imported = new AxamlImportService().Import(source,RecognitionFixture);
        var vm = GridFirstVm();
        var passed = 0;
        var failed = 0;
        void Check(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch(Exception ex){failed++;Console.WriteLine("FAIL " + name + " " + ex);}
        }
        try
        {
            vm.LoadAxamlImportedDocument(imported,RecognitionFixture);
            var doc = vm.ActiveAxamlSourceDocument!;
            var nodes = SourceDescendants(doc.Syntax.Root).ToArray();
            AxamlElementSyntax Node(string name) => nodes.First(n=>n.Name==name);
            var info = doc.Recognition;
            var custom = Node("local:RecognitionProbeControl");
            Check("unresolved and mismatched assemblies stay opaque",()=>{
                foreach(var n in new[]{custom,Node("bad:Button"),Node("u:Button"),Node("u:DataGridTextColumn")})
                {
                    var c = info.Inspect(doc,n,vm.Registry);
                    RequireCapability(!c.Recognized && c.Inspectable && c.Opaque && !c.Previewable && !c.Editable && !c.StructuralEditable,n.Name);
                }
            });
            Check("empty XML namespace is distinct from the Avalonia namespace", () =>
            {
                var text = "<Window xmlns='https://github.com/avaloniaui'><Grid><Button xmlns='' Name='Unknown'/><Button Name='Known'/></Grid></Window>";
                var input = new AxamlImportService().Import(text);
                var buttons = SourceDescendants(input.RoundTripDocument.Syntax.Root).Where(n => n.LocalName=="Button").ToArray();
                RequireCapability(info.Resolve(buttons[0]) == null && info.Resolve(buttons[1])?.FrameworkType == typeof(Button) &&
                    input.Document.Controls.All(c=>c.Name!="Unknown") && input.Document.Controls.Any(c=>c.Name=="Known"), "Namespace reset");
                RequireCapability(info.Resolve("clr-namespace:Avalonia.Controls","Button")==null,"Unqualified CLR assembly must have project context");
            });
            Check("overlay Panel retains native child order and custom boundary", () =>
            {
                var panelVm=Universal4AVm("<a:Panel x:Name='Overlay'><a:Button x:Name='First'/><q:Button xmlns:q='urn:unknown'/><a:TextBox x:Name='Last'/></a:Panel>");
                try
                {
                    var panel=panelVm.Controls.Single(c=>c.Name=="Overlay");
                    var tree=AxamlLayoutProjection.CreateNativeTree(panelVm.ActiveAxamlSourceDocument!,panelVm.Controls);
                    var view=(Panel)tree.Controls[panel.Id];
                    RequireCapability(view.GetType()==typeof(Panel) && view.Children is [Button, TextBox] &&
                        !panelVm.GetAxamlStructuralCapability(panel.Id).CanAddChild && !panelVm.CreateActiveAxamlPatch().HasChanges,"Native Panel and source boundaries");
                }
                finally { Dispose5AProfileVm(panelVm); }
            });
            Check("metadata-only custom type, base and property",()=>{
                info.InspectAssembly(typeof(Program).Assembly.Location);
                var identity=info.Resolve(custom);
                RequireCapability(identity != null && identity.FullName==typeof(RecognitionProbeControl).FullName && identity.BaseType==typeof(Button).FullName
                    && identity.Properties.Any(p=>p.StartsWith("CategoryCode")) && !info.Inspect(doc,custom,vm.Registry).Previewable,"Custom identity or capabilities");
            });
            Check("vendor XmlnsDefinition and exact CLR assembly",()=>{
                var file=Path.Combine(AppContext.BaseDirectory,"Plugins","EremexDesignerPlugin","Eremex.Avalonia.Controls.dll");
                var before=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Eremex.Avalonia.Controls");
                info.InspectAssembly(file);
                var type=info.Resolve(Node("ex:TextEditor"));
                RequireCapability(type?.FullName=="Eremex.AvaloniaUI.Controls.Editors.TextEditor" && type.AssemblyName=="Eremex.Avalonia.Controls" &&
                    type.Properties.Any() && AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Eremex.Avalonia.Controls")==before,"Vendor identity/load boundary: "+type?.FullName);
            });
            Check("standard and nested framework markup",()=>{
                foreach(var n in nodes.Where(n=>n.Name.StartsWith("av:")||n.Name.StartsWith("avc:")||n.Name.StartsWith("panels:")))
                    RequireCapability(info.Resolve(n)!=null,"Unresolved "+n.Name);
                RequireCapability(info.Resolve(Node("avc:Button"))?.FrameworkType==typeof(Button) && info.Resolve(Node("panels:StackPanel"))?.FrameworkType==typeof(StackPanel),"Qualified names");
            });
            Check("metadata and projection remain independent below opaque/template",()=>{
                var child=nodes.Single(n=>n.GetAttributeValue("Name")=="UnderOpaque");
                var c=info.Inspect(doc,child,vm.Registry);
                RequireCapability(c.Recognized && c.Opaque && !c.Previewable && !c.Editable && c.DescriptorExportable,"Opaque descendants");
                RequireCapability(nodes.Count(n=>n.LocalName=="DataTemplate")==2 && doc.SourceMap.Controls.All(r=>r.Element!=child),"Template/source boundaries");
            });
            Check("DataGrid native columns, nested panels, shapes, image and icons",()=>{
                var tree=AxamlLayoutProjection.CreateNativeTree(doc,vm.Controls);
                var reference=doc.SourceMap.Controls.Single(r=>r.Element.LocalName=="DataGrid");
                var grid=(DataGrid)tree.Controls[reference.ControlId];
                RequireCapability(grid.Columns.Count==2 && grid.Columns[0] is DataGridTextColumn { Header:"Title" } && grid.Columns[1] is DataGridCheckBoxColumn { Header:"Active" },"DataGrid headers");
                foreach(var n in new[]{"av:Path","av:PathIcon","av:Image","panels:StackPanel"})
                {
                    var rf=doc.SourceMap.Controls.Single(r=>r.Element==Node(n));
                    RequireCapability(tree.Controls[rf.ControlId].GetType()==info.Resolve(rf.Element)?.FrameworkType,n);
                }
            });
            Check("source explorer, raw attributes and selection",()=>{
                vm.ShowAxamlSourceTree=true;
                RequireCapability(ReferenceEquals(vm.SourceExplorerRoots.Single(),doc.Syntax.Root),"Explorer root");
                vm.SelectedSourceNode=custom;
                RequireCapability(vm.SelectedControl==null && vm.SourceInspectionText.Contains("CategoryCode") && vm.SourceInspectionText.Contains("literal &amp; raw"),"Read-only inspector: "+vm.SourceInspectionText);
                vm.SelectedSourceNode=Node("avc:Button");
                RequireCapability(vm.SelectedControl?.Name=="KnownButton" && vm.SourceInspectionText.Contains("{Binding Order.Title"),"Mapped inspector");
                vm.ShowAxamlSourceTree=false;
                RequireCapability(!vm.HasSourceInspection && vm.SourceExplorerRoots.Count==0,"Source view closed");
            });
            Check("safe source patch, exact Undo and Redo",()=>{
                var grid=vm.Controls.Single(c=>c.Name=="Orders");vm.SelectSingleControl(grid);
                RequireCapability(!vm.CanEditAxamlProperty(grid,"Height") && GetPropertyGridRow(vm,"Height").IsReadOnly,"Absent scalar must preserve source inheritance");
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Width"),"237");
                var patch=vm.CreateActiveAxamlPatch();
                RequireCapability(patch.CanApply && patch.Edits.Count==1 && patch.PatchedText.Contains("Width=\"237\"") &&
                    patch.PatchedText.Contains("<u:DataGridTextColumn Header=\"Unknown\"/>") && patch.PatchedText.Contains("<!-- preserve"),"Patch width");
                vm.MarkAxamlRoundTripSaved(RecognitionFixture,patch.PatchedText);
                vm.ShowAxamlSourceTree=true;
                var refreshed=vm.ActiveAxamlSourceDocument!;
                var newCustom=SourceDescendants(vm.SourceExplorerRoots.Single()).Single(n=>n.Name==custom.Name);
                RequireCapability(newCustom!=custom && refreshed.Recognition.Resolve(newCustom)?.FullName==typeof(RecognitionProbeControl).FullName, "ACK source identity");
                vm.SelectedSourceNode=custom;
                RequireCapability(vm.SelectedSourceNode==null, "Stale source node");
                GridEditorUndo(vm);
                var undone=vm.CreateActiveAxamlPatch();
                File.WriteAllText(Path.Combine(results,"after-undo.axaml"),undone.PatchedText);
                RequireCapability(undone.PatchedText==source,"Undo to original: "+undone.CanApply+" "+vm.StatusText);
                GridEditorUndo(vm,true);
                RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Redo");
            });
            Check("real MainWindow source is unchanged until a mapped literal edit", () =>
            {
                var file=Path.Combine(FindRepositoryRoot(),"Views","MainWindow.axaml");
                var original=File.ReadAllText(file);
                var input=new AxamlImportService().Import(original,file);
                var scene=GridFirstVm();
                try
                {
                    scene.LoadAxamlImportedDocument(input,file);
                    var unchanged=scene.CreateActiveAxamlPatch(original);
                    RequireCapability(unchanged.CanApply && !unchanged.HasChanges && unchanged.PatchedText==original,
                        "Large no-op: "+string.Join(", ",unchanged.Diagnostics.Select(d=>d.Details)));
                    var entry=input.RoundTripDocument.SourceMap.Controls.First(r =>
                        r.Element.LocalName=="Button" && r.Capability.CanEditProperty("Width") &&
                        int.TryParse(r.Element.GetAttributeValue("Width"),out var w) && w>0 && w!=237);
                    scene.SelectSingleControl(scene.Controls.Single(c=>c.Id==entry.ControlId));
                    scene.CommitPropertyGridTextEdit(GetPropertyGridRow(scene,"Width"),"237");
                    var patch=scene.CreateActiveAxamlPatch(original);
                    var span=entry.Element.FindAttribute("Width")!.ValueSpan;
                    RequireCapability(patch.CanApply && patch.Edits.Count==1 &&
                        patch.PatchedText==original.Remove(span.Start,span.Length).Insert(span.Start,"237"),"Large minimal patch");
                    GridEditorUndo(scene);
                    RequireCapability(scene.CreateActiveAxamlPatch(original).PatchedText==original,"Large undo");
                }
                finally { Dispose5AProfileVm(scene); }
            });
            Check("opaque source blocks folder and ZIP publication", () => AssertPublicationBlocked(vm, Path.Combine(results,"opaque"), "AXAML"));
            foreach(var parent in new[]{"Grid","Canvas","StackPanel","DockPanel","WrapPanel"})
                Check("source and JSON toolbox insertion into "+parent,()=>{
                    var project=NestedModel();
                    var ax=Universal4AVm("<a:"+parent+" x:Name='Parent'><a:TextBlock x:Name='First'/></a:"+parent+">");
                    try
                    {
                        var mode=parent switch { "Canvas" => DesignerLayoutModes.Absolute, "DockPanel" => DesignerLayoutModes.Dock,
                            "StackPanel" => DesignerLayoutModes.Stack, "WrapPanel" => DesignerLayoutModes.Flex, _ => DesignerLayoutModes.Grid };
                        var panel=NestedHost(project,mode);
                        MeasureNested(project);
                        var point=NestedPoint(project,panel.Id);
                        var newChild=project.TryCreateControlFromToolboxDrop("Button",point.X,point.Y,panel.Id,false,project.ActiveDocumentId);
                        RequireCapability(newChild?.ParentId==panel.Id,"JSON parent");
                        var target=ax.Controls.FirstOrDefault(c=>c.Name=="Parent");
                        var document=ax.ActiveAxamlSourceDocument!;
                        if(parent=="Canvas")
                        {
                            var root=document.SourceMap.CanvasElement!;
                            var capability=document.Recognition.Inspect(document,root,ax.Registry);
                            ax.ShowAxamlSourceTree=true;
                            ax.SelectedSourceNode=root;
                            RequireCapability(capability.Previewable && capability.StructuralEditable && !capability.Opaque &&
                                ax.SourceInspectionText.Contains("Корневой Canvas"),"Implicit Canvas capabilities");
                        }
                        var old=document.OriginalText;
                        RequireCapability(ax.TryCreateControlFromToolboxDrop("DataGrid",0,0,target?.Id,false,ax.ActiveDocumentId)==null && ax.StatusText.Contains("DataGrid") && !ax.CreateActiveAxamlPatch().HasChanges,"Unsupported insertion");
                        var inserted=ax.TryCreateControlFromToolboxDrop("Button",8,12,target?.Id,false,ax.ActiveDocumentId);
                        var patch=ax.CreateActiveAxamlPatch();
                        RequireCapability(inserted!=null && patch.CanApply && patch.HasChanges && patch.PatchedText.Contains("First") && patch.PatchedText!=old,"Supported insertion: "+ax.StatusText);
                    }
                    finally {Dispose5AProfileVm(project);Dispose5AProfileVm(ax);}
                });
            var audit=nodes.Select(n=>new {n.Name,n.NamespaceUri,OriginalMapped=doc.SourceMap.Controls.Any(r=>r.Element==n),
                Type=info.Resolve(n)?.FullName,Assembly=info.Resolve(n)?.AssemblyName,Span=n.ElementSpan}).ToArray();
            File.WriteAllText(Path.Combine(results,"recognition.json"),JsonSerializer.Serialize(audit,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally {Dispose5AProfileVm(vm);}
        Console.WriteLine($"PHASE7A {passed}/{passed+failed}");
        return failed==0 ? 0:1;
    }

    private static void AssertPublicationBlocked(MainWindowViewModel vm, string directory, string reason)
    {
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "existing.txt");
        var zip = directory + ".zip";
        File.WriteAllText(sentinel, "previous");
        File.WriteAllText(zip, "previous ZIP");
        foreach (Func<Task> publish in new Func<Task>[] { () => vm.ExportCurrentResultToProjectAsync(directory), () => vm.ExportCurrentResultAsZipAsync(zip) })
        {
            try { Pump(publish()); }
            catch (InvalidOperationException ex) when (ex.Message.Contains(reason, StringComparison.Ordinal)) { continue; }
            throw new InvalidOperationException("Expected export rejection for " + reason);
        }
        RequireCapability(Directory.GetFileSystemEntries(directory).Length == 1 && File.ReadAllText(sentinel) == "previous" &&
            File.ReadAllText(zip) == "previous ZIP","Previous export was changed");
    }

    private static int ExportRegistryCatalog(string directory)
    {
        Directory.CreateDirectory(directory);
        var registry = DesignerRegistry.CreateForHost(new StandaloneDesignerHostServices());
        var vm = new MainWindowViewModel(registry, new TestDesignerHostServices(true))
        {
            ExportProjectNamespace="SmokeGenerated.Registry", ExportTarget=MainWindowViewModel.ExportTargetMainWindow,
            DataGridExportMode=MainWindowViewModel.DataGridExportModeReal, IncludeSampleData=false,IncludePluginRuntimeReferences=true
        };
        var expected = new Dictionary<string,string>();
        var ids = new Dictionary<string,string>();
        try
        {
            foreach (var descriptor in registry.GetControls())
            {
                var native=DesignerRegistry.NativeIdentity(descriptor);
                if (native.FullName == null) throw new InvalidOperationException("Unknown native identity: "+descriptor.TypeKey);
                if (!DesignerRegistry.CanExportNativeType(registry,descriptor))
                {
                    var unsupported = new MainWindowViewModel(registry, new TestDesignerHostServices(true)) { IncludePluginRuntimeReferences = true };
                    try
                    {
                        unsupported.CreateControl(descriptor.TypeKey,0,0);
                        AssertPublicationBlocked(unsupported,Path.Combine(directory,"blocked",descriptor.TypeKey),descriptor.TypeKey);
                    }
                    finally { Dispose5AProfileVm(unsupported); }
                    continue;
                }
                var model=vm.CreateControl(descriptor.TypeKey,0,0);
                model.Name="RegistryProbe"+expected.Count;
                expected.Add(model.Name,native.FullName);
                ids.Add(model.Name,descriptor.TypeKey);
            }
            File.WriteAllText(Path.Combine(directory,"expected.json"),JsonSerializer.Serialize(expected));
            File.WriteAllText(Path.Combine(directory,"ids.json"),JsonSerializer.Serialize(ids));
            Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(directory,"Project")));
            return 0;
        }
        finally {Dispose5AProfileVm(vm);}
    }

    private static int AuditRegistryViews(string directory)
    {
        Directory.CreateDirectory(directory);
        var registry = DesignerRegistry.CreateForHost(new StandaloneDesignerHostServices());
        var vm = new MainWindowViewModel(registry, new TestDesignerHostServices(true));
        var surface = CreateSmokeWindow(vm);
        FormDesigner.Views.PreviewWindow? preview = null;
        try
        {
            var controls = registry.GetControls().Select(d => vm.CreateControl(d.TypeKey, 0, 0)).ToArray();
            var snapshot = vm.CreatePreviewDocumentSnapshot();
            preview = new FormDesigner.Views.PreviewWindow(snapshot, registry);
            var rows = controls.Select(control =>
            {
                vm.SelectSingleControl(control);
                var inspector = vm.PropertyGridCategories.SelectMany(c => c.Rows).Select(r => r.Key).Distinct().ToArray();
                var design = InvokePreviewFactory(surface, "CreatePreviewControl", control);
                var legacy = InvokePreviewFactory(preview, "CreatePreviewControl", snapshot.Controls.Single(c => c.Id == control.Id));
                var expected = DesignerRegistry.NativeIdentity(registry.GetRequiredControl(control.Type)).FullName;
                bool HasNative(Control view) => view.GetType().FullName == expected ||
                    view.GetVisualDescendants().Concat(view.GetLogicalDescendants().OfType<Avalonia.Visual>())
                        .Any(child => child.GetType().FullName == expected);
                return new { Id=control.Type, InspectorRows=inspector.Length, Designer=design.GetType().FullName,
                    Legacy=legacy.GetType().FullName, NativeDesigner=HasNative(design), NativeLegacy=HasNative(legacy) };
            }).ToArray();
            File.WriteAllText(Path.Combine(directory,"views.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
            RequireCapability(rows.Length==registry.GetControls().Count && rows.All(r=>r.InspectorRows>0), "Selected control Inspector");
            Console.WriteLine($"REGISTRY_VIEWS {rows.Length} inspector selections, {rows.Count(r=>r.NativeDesigner)} native Designer, {rows.Count(r=>r.NativeLegacy)} native Legacy");
            return 0;
        }
        finally { preview?.Close(); surface.CloseForExternalHost(); Dispose5AProfileVm(vm); }
    }

    private static int ProfileRecognition(string directory)
    {
        Directory.CreateDirectory(directory);
        var samples=new List<object>();
        var registry=DesignerRegistry.CreateForHost(new StandaloneDesignerHostServices());
        var vm=new MainWindowViewModel(registry);
        var refresh=typeof(MainWindowViewModel).GetMethod("RefreshToolboxItemsFromRegistry",BindingFlags.NonPublic|BindingFlags.Instance)!;
        try
        {
            foreach(var file in new[]{RecognitionFixture,"Views/MainWindow.axaml"})
            {
                string source=File.ReadAllText(file);
                var imported=new AxamlImportService().Import(source,file);
                vm.LoadAxamlImportedDocument(imported,file);
                var doc=vm.ActiveAxamlSourceDocument!;
                var nodes=SourceDescendants(doc.Syntax.Root).ToArray();
                var sel=vm.Controls.FirstOrDefault(c => c.Type=="TextBlock") ?? vm.Controls.FirstOrDefault();
                var inspectNode=nodes.First(n => n.LocalName=="TextBlock");
                vm.ShowAxamlSourceTree=sel==null;
                for(var iteration=-2;iteration<20;iteration++)
                {
                    void Measure(string name,Action action)
                    {
                        long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                        action();var elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        var allocation=GC.GetAllocatedBytesForCurrentThread()-bytes;
                        if(iteration>=0)samples.Add(new{file,iteration,name,elapsed,allocation});
                    }
                    if(file==RecognitionFixture)
                    {
                        Measure("Registry",()=>DesignerRegistry.CreateForHost(new StandaloneDesignerHostServices()));
                        Measure("Toolbox",()=>refresh.Invoke(vm,null));
                        Measure("Search",()=>vm.ToolboxSearchText="Grid");vm.ToolboxSearchText="";
                    }
                    Measure("Import",()=>new AxamlImportService().Import(source,file));
                    Measure("Resolve",()=>{foreach(var n in nodes) doc.Recognition.Resolve(n);});
                    vm.ClearSelection();vm.SelectedSourceNode=null;
                    Measure(sel==null?"Source Inspector":"Inspector",()=>
                    {
                        if (sel!=null) vm.SelectSingleControl(sel);
                        else { vm.SelectedSourceNode=inspectNode; _=vm.SourceInspectionText; }
                    });
                    Measure("Preview",()=>{var tree=AxamlLayoutProjection.CreateNativeTree(doc,vm.Controls);tree.Root.Measure(new Size(1000,720));tree.Root.Arrange(new Rect(0,0,1000,720));});
                }
            }
            File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(samples));
            return 0;
        }
        finally{Dispose5AProfileVm(vm);}
    }
}
