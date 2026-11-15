using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Rendering.Composition;
using FormDesigner.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static string Collection3BFolder => Path.Combine(FindRepositoryRoot(),"artifacts","universal-phase3b");
    private static MainWindowViewModel CollectionScene(string layout="Grid")
    {
        var vm=GridFirstVm();vm.DesignWidth=1200;vm.DesignHeight=900;
        if(layout=="Canvas"){vm.LayoutSemanticsVersion=1;vm.SurfaceLayoutMode=DesignerLayoutModes.Absolute;}
        vm.SurfaceGridRowDefinitions="Auto,*,*";vm.SurfaceLayoutRows=3;vm.SurfaceGridColumnDefinitions="*,*";vm.SurfaceLayoutColumns=2;
        var types=new[]{"Menu","ComboBox","TabControl","TreeView","ListBox","SplitButton"};
        for(var i=0;i<types.Length;i++)
        {var c=vm.CreateControl(types[i],20+i%2*420,30+i/2*250);c.GridRow=i/2;c.GridColumn=i%2;c.Width=360;c.Height=i<2?45:200;c.WidthMode=c.HeightMode="Fixed";}
        return vm;
    }
    private static CollectionEditorSession OpenCollection(MainWindowViewModel vm,string type,string property="Items")
    { vm.SelectSingleControl(vm.Controls.Single(c=>c.Type==type));return vm.OpenCollectionEditor(property)??throw new Exception(vm.StatusText); }
    private static Control CollectionNative(Window window,DesignControlModel model) => window.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==model.Name);
    private static void SeedCollection(MainWindowViewModel vm,string type)
    {
        var session=OpenCollection(vm,type,type=="SplitButton"?StandardItemsProperties.MenuKey:"Items");
        session.Add();var id=session.SelectedItemId;
        session.SetValue(id,type is "ComboBox" or "ListBox"?"Content":"Header","Первый <&>");
        if(type is "Menu" or "TreeView"){session.Add(nested:true);session.SetValue(session.SelectedItemId,"Header","Вложенный");session.Select(id);}
        if(type=="Menu")session.Add(separator:true);
        session.Add();session.SetValue(session.SelectedItemId,type is "ComboBox" or "ListBox"?"Content":"Header","Второй");
        if(type=="TabControl")session.SetValue(session.SelectedItemId,"Content","Содержимое вкладки");
        vm.CloseCollectionEditor(session);
    }
    private static int RunCollections3BChecks()
    {
        Directory.CreateDirectory(Collection3BFolder);int pass=0,fail=0;
        void Check(string name,Action action)
        {
            var filter=Environment.GetEnvironmentVariable("COLLECTION3B_FILTER");if(filter is not null&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{action();pass++;Console.WriteLine("PASS COLLECTION3B_"+name);}catch(Exception error){fail++;Console.WriteLine("FAIL COLLECTION3B_"+name+": "+error);}
            finally{foreach(var window in SmokeWindows)window.CloseForExternalHost();SmokeWindows.Clear();}
        }
        Check("MetadataSignaturesAndCapabilities",()=>
        {
            foreach(var type in new[]{typeof(Menu),typeof(ContextMenu),typeof(ComboBox),typeof(ListBox),typeof(TabControl),typeof(TreeView)})
            {var item=CollectionMetadataCatalog.Get(type).Single(p=>p.PropertyName=="Items");RequireCapability(item.CanAdd&&item.CanReorder&&item.IsControlCollection&&item.CollectionType!=typeof(string),"Missing reviewed Items capability.");}
            var columns=CollectionMetadataCatalog.Get(typeof(DataGrid)).Single(p=>p.PropertyName=="Columns");RequireCapability(columns.SerializationStrategy=="ExistingDataGridColumnsEditor","Column editor replaced.");
            RequireCapability(CollectionMetadataCatalog.Get(typeof(Button)).Where(p=>p.PropertyName is "Styles" or "Resources").All(p=>!p.CanAdd),"Arbitrary enumerable enabled.");
        });
        Check("AddRemoveReorderStableIdsAndUndo",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"Menu");var n=vm.ActiveSession.UndoSnapshots.Count;
            RequireCapability(s.Add()&&vm.ActiveSession.UndoSnapshots.Count==n+1,"Add not atomic.");var first=s.SelectedItemId;s.Add();var second=s.SelectedItemId;
            RequireCapability(s.Move(-1)&&s.Roots[0].Id==second,"Reorder lost identity.");var history=vm.ActiveSession.UndoSnapshots.Count;s.Select(first);RequireCapability(vm.ActiveSession.UndoSnapshots.Count==history,"Selection wrote history.");
            s.Remove();RequireCapability(s.Count==1,"Remove failed.");GridEditorUndo(vm);RequireCapability(s.Count==2&&s.Roots.Any(i=>i.Id==first)&&s.SelectedItemId==first,"Undo lost IDs/selection.");GridEditorUndo(vm,true);RequireCapability(s.Count==1,"Redo failed.");
        });
        Check("NestedRemoveAndLogicPreservation",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"SplitButton",StandardItemsProperties.MenuKey);s.Add();var parent=s.SelectedItemId;s.Add(nested:true);var child=s.SelectedItemId;
            var item=s.SelectedItem!;vm.EditStandardMenuLogic(vm.SelectedControl!,item);var rule=vm.Interactions.Single();var ruleName=rule.SourceControlName;
            s.Select(parent);s.Remove();RequireCapability(!vm.Interactions.Any(i=>i.SourceControlName==ruleName),"Removed subtree kept Logic.");GridEditorUndo(vm);
            RequireCapability(s.Roots.Any(r=>r.Id==parent)&&s.Roots.Single(r=>r.Id==parent).Children.Any(r=>r.Id==child)&&vm.Interactions.Any(i=>i.SourceControlName==ruleName),"Undo lost subtree/Logic.");
        });
        Check("RenamePreservesNestedLogic",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"SplitButton",StandardItemsProperties.MenuKey);s.Add();s.Add(nested:true);var id=s.SelectedItemId;
            vm.EditStandardMenuLogic(vm.SelectedControl!,s.SelectedItem!);var before=vm.Interactions.Single().SourceControlName;
            RequireCapability(s.SetValue(id,"Name","NestedAction")&&vm.Interactions.Single().SourceControlName=="NestedAction","Rename removed Logic.");
            GridEditorUndo(vm);RequireCapability(vm.Interactions.Single().SourceControlName==before,"Undo rename lost Logic identity.");
        });
        Check("CollectionNamesAreUniqueAcrossOwnersAndProperties",()=>
        {
            var vm=CollectionScene();var menu=OpenCollection(vm,"Menu");menu.Add();
            RequireCapability(menu.SetValue(menu.SelectedItemId,"Name","SharedItemName"),"Initial item name rejected.");
            var combo=OpenCollection(vm,"ComboBox");combo.Add();var history=vm.ActiveSession.UndoSnapshots.Count;
            RequireCapability(!combo.SetValue(combo.SelectedItemId,"Name","SharedItemName")&&combo.SelectedItem!.Name.Length==0&&vm.ActiveSession.UndoSnapshots.Count==history,"Duplicate name across owners accepted or wrote history.");
            var context=OpenCollection(vm,"Menu","ContextMenu.Items");context.Add();history=vm.ActiveSession.UndoSnapshots.Count;
            RequireCapability(!context.SetValue(context.SelectedItemId,"Name","SharedItemName")&&vm.ActiveSession.UndoSnapshots.Count==history,"Duplicate name across properties accepted.");
        });
        foreach(var type in new[]{"Menu","ComboBox","TabControl","TreeView"})Check(type+"_RoutedToolbox",()=>
        {
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();var data=GridToolboxData(f,type);var point=GridCellPoint(f.Vm,"",0,0);
            RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);f.Refresh();
            var model=f.Vm.Controls.Single(c=>c.Type==type);RequireCapability(f.Vm.SelectedControl?.Id==model.Id&&model.GridRow==0&&model.GridColumn==0,"Routed drop lost selection/placement.");
            RequireCapability(CollectionNative(f.Window,model).GetType().Name==type,"Designer native control missing after drop.");
        });
        Check("ExistingPropertyGridEditsWithoutFullRebuild",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"Menu");s.Add();var w=Phase3Show(vm);var row=GetPropertyGridRow(vm,"@collection/Header");
            var before=vm.ActiveSession.UndoSnapshots.Count;var categories=vm.PropertyGridCategories.ToArray();vm.CommitPropertyGridTextEdit(row,"Изменённый");
            RequireCapability(s.SelectedItem!.Text=="Изменённый"&&vm.ActiveSession.UndoSnapshots.Count==before+1,"Property edit not atomic.");
            RequireCapability(categories.SequenceEqual(vm.PropertyGridCategories),"Item property recreated PropertyGrid categories.");
            vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,"@collection/Header"));RequireCapability(s.SelectedItem!.Text=="","Item reset failed.");GridEditorUndo(vm);RequireCapability(s.SelectedItem!.Text=="Изменённый","Reset undo failed.");
        });
        Check("NativeCollectionEditorTreeAndSelection",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"TreeView");s.Add();s.Add(nested:true);
            var w=new StandardItemsEditorWindow(vm,s){ShowInTaskbar=false};w.Show();NativeItemsUiWait(100);w.UpdateLayout();
            RequireCapability(w.ItemInspector!.Context==vm&&w.CollectionTree!.Items.Count==1,"Editor is disconnected from existing VM.");
            w.CollectionTree!.SelectedItem=s.Roots[0];RequireCapability(s.SelectedItemId==s.Roots[0].Id,"Native tree selection did not reach Inspector.");
            var text=w.GetVisualDescendants().OfType<PropertyGridTextEditor>().Single(e=>e.Row?.Key=="@collection/Header");text.Focus();text.Text="Из UI";text.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
            RequireCapability(s.SelectedItem!.Text=="Из UI","Routed editor input failed.");w.Close();RequireCapability(s.IsDisposed&&vm.ActiveCollectionEditor is null,"Window did not release session.");
        });
        Check("StaleCallbacksAndFormIsolation",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"Menu");s.Add();var old=GetPropertyGridRow(vm,"@collection/Header");s.Add();vm.CommitPropertyGridTextEdit(old,"STALE");RequireCapability(s.SelectedItem!.Text!="STALE","Old item callback edited new item.");
            vm.CloseCollectionEditor(s);vm.CommitPropertyGridTextEdit(old,"AFTER CLOSE");RequireCapability(!vm.ExportWorkspaceJson().Contains("AFTER CLOSE"),"Closed editor callback wrote document.");
            var form=vm.ActiveDocumentId;vm.AddFormEditorCommand!.Execute(null);RequireCapability(vm.ActiveDocumentId!=form&&!s.Add(),"Old session edited another form.");
        });
        Check("BindingAndResourceSafety",()=>
        {
            var vm=CollectionScene();var list=vm.Controls.Single(c=>c.Type=="ListBox");StandardItemsProperties.Write(list,"ItemsSource","{Binding Rows}");vm.SelectSingleControl(list);var original=JsonSerializer.Serialize(list.CustomProperties);
            RequireCapability(vm.OpenCollectionEditor("Items") is null&&JsonSerializer.Serialize(list.CustomProperties)==original,"Binding was replaced with Items.");
            StandardItemsProperties.Write(list,"ItemsSource","{DynamicResource Rows}");RequireCapability(vm.OpenCollectionEditor("Items") is null,"Resource source overwritten.");
        });
        Check("VersionsCorruptionUnknownFieldsAndCycles",()=>
        {
            var vm=CollectionScene();var model=vm.Controls.Single(c=>c.Type=="Menu");vm.SelectSingleControl(model);
            foreach(var raw in new[]{"{\"Version\":99,\"Items\":[]}","{bad","{\"Version\":1,\"Items\":null}","{\"Version\":1,\"Items\":[],\"Future\":42}"})
            {StandardItemsProperties.Write(model,CollectionItemsContract.StorageKey("Items"),0);model.CustomProperties.Single(p=>p.Key==CollectionItemsContract.StorageKey("Items")).ValueJson=raw;
             RequireCapability(vm.OpenCollectionEditor("Items") is null&&model.CustomProperties.Single(p=>p.Key==CollectionItemsContract.StorageKey("Items")).ValueJson==raw,"Unsafe payload overwritten.");}
            var cycle=new StandardMenuItem();cycle.Children.Add(cycle);var threw=false;try{_ = cycle.Clone();}catch(FormatException){threw=true;}RequireCapability(threw,"Cycle did not fail closed.");
        });
        foreach(var type in new[]{"Menu","ComboBox","TabControl","TreeView","ListBox"})
        {
            Check(type+"_JsonBothNativePreviews",()=>
            {
                var vm=CollectionScene();SeedCollection(vm,type);var model=vm.Controls.Single(c=>c.Type==type);model.CornerRadius=9;StandardItemsProperties.Write(model,"SelectedIndex",0);
                var json=vm.ExportWorkspaceJson();var clone=GridFirstVm();clone.LoadDocumentJson(json);var restored=clone.Controls.Single(c=>c.Id==model.Id);
                RequireCapability(restored.ParentId==model.ParentId&&restored.GridRow==model.GridRow&&CollectionItemsContract.Read(restored,"Items") is not null,"JSON lost placement/contract.");
                foreach(var axaml in new[]{false,true})
                {
                    using var p=ShowDataGridRegressionPreview(clone,axaml);var native=(ItemsControl)CollectionNative(p.Window,restored);
                    RequireCapability(native.GetType().Name==type&&native.Items.Count==CollectionItemsContract.Read(restored,"Items")!.Items.Count,"Native collection missing "+type);
                    RequireCapability(native.GetVisualRoot()==p.Window,"Control absent from real visual tree.");
                    RequireCapability(((Avalonia.Controls.Primitives.TemplatedControl)native).CornerRadius==new CornerRadius(9),"Common CornerRadius lost in "+type+"; AXAML="+axaml);
                    if(native is TabControl tabs){tabs.SelectedIndex=1;p.Window.UpdateLayout();RequireCapability(tabs.SelectedItem is TabItem&&tabs.SelectedContent?.ToString()=="Содержимое вкладки","Tab switching failed.");}
                    if(native is ComboBox combo){combo.IsDropDownOpen=true;NativeItemsUiWait(30);RequireCapability(combo.IsDropDownOpen,"Combo popup failed.");combo.SelectedIndex=1;combo.IsDropDownOpen=false;RequireCapability(combo.SelectedItem is ComboBoxItem,"Combo selection failed.");}
                    if(native is TreeView tree){var node=(TreeViewItem)tree.Items[0]!;node.IsExpanded=true;p.Window.UpdateLayout();RequireCapability(node.Items[0] is TreeViewItem&&node.IsExpanded,"Tree hierarchy missing.");node.IsExpanded=false;}
                    if(native is ListBox list){list.SelectedIndex=1;RequireCapability(list.SelectedItem is ListBoxItem,"Typed ListBox selection failed.");}
                    if(native is Menu menu){var node=(MenuItem)menu.Items[0]!;node.IsSubMenuOpen=true;NativeItemsUiWait(30);RequireCapability(node.Items[0] is MenuItem&&node.IsSubMenuOpen,"Nested menu failed.");node.IsSubMenuOpen=false;}
                }
            });
        }
        Check("ContextMenuBothModes",()=>
        {
            var vm=CollectionScene();var s=OpenCollection(vm,"TreeView","ContextMenu.Items");s.Add();s.Add(nested:true);vm.CloseCollectionEditor(s);
            foreach(var mode in new[]{false,true}){using var p=ShowDataGridRegressionPreview(vm,mode);var root=CollectionNative(p.Window,vm.Controls.Single(c=>c.Type=="TreeView"));root.ContextMenu!.Open(root);NativeItemsUiWait(40);RequireCapability(root.ContextMenu.IsOpen&&root.ContextMenu.Items[0] is MenuItem,"ContextMenu did not open.");root.ContextMenu.Close();}
        });
        Check("ExplorerHierarchyAndOwnerSelection",()=>
        {
            var vm=CollectionScene();SeedCollection(vm,"TreeView");var owner=vm.Controls.Single(c=>c.Type=="TreeView");
            IEnumerable<StructureTreeItemModel> Walk(IEnumerable<StructureTreeItemModel> nodes)=>nodes.SelectMany(n=>new[]{n}.Concat(Walk(n.Children)));
            var entry=Walk(vm.StructureTreeItems).First(n=>n.CollectionOwnerId==owner.Id&&n.CollectionItemId.Length>0);vm.SelectedStructureItem=entry;
            RequireCapability(vm.SelectedControl?.Id==owner.Id&&vm.ActiveCollectionEditor?.SelectedItemId==entry.CollectionItemId,"Explorer treated item as Canvas control.");
            RequireCapability(vm.SelectedStructureItem?.CollectionItemId==entry.CollectionItemId,"Explorer visual selection returned to owner.");
            vm.ActiveCollectionEditor!.SetValue(entry.CollectionItemId,"Header","Explorer edited");
            RequireCapability(vm.SelectedStructureItem?.CollectionItemId==entry.CollectionItemId,"Explorer lost item after property edit.");
        });
        Check("ImportedMinimalPatchesAndReadOnlyStructure",()=>
        {
            var source="<Window xmlns=\"https://github.com/avaloniaui\"><Grid><!--keep--><Menu Name=\"M\"><MenuItem Header=\"File\" Tag=\"{Binding X}\"><MenuItem Header=\"Open\"/><Separator/></MenuItem></Menu></Grid></Window>";
            var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source,"collections.axaml"),"collections.axaml");
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"No-op regenerated source.");RequireCapability(vm.Controls.Any(c=>c.Name=="M"),"Imported names: "+JsonSerializer.Serialize(vm.Controls.Select(c=>new{c.Type,c.Name})));vm.SelectSingleControl(vm.Controls.First(c=>c.Name=="M"));RequireCapability(vm.OpenCollectionEditor("Items") is null,"Source structural write enabled.");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"Read-only structural attempt changed source.");
        });
        Check("ExpansionStateAndNoCrossControlLeak",()=>
        {
            var vm=Universal2Scene();var first=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(first);var w=Phase3Show(vm);
            var editor=w.GetVisualDescendants().OfType<PropertyGridCompoundEditor>().Single(e=>e.Row?.Key=="Padding");RequireCapability(editor.Components.Count==0,"Nested fields eagerly built.");editor.ExpandComponents();
            vm.SelectSingleControl(vm.Controls.Single(c=>c.Type=="Calendar"));w.UpdateLayout();vm.SelectSingleControl(first);w.UpdateLayout();
            RequireCapability(vm.IsPropertyGridExpanded(GetPropertyGridRow(vm,"Padding"))&&w.GetVisualDescendants().OfType<PropertyGridCompoundEditor>().Single(e=>e.Row?.Key=="Padding").Components.Count==4,"Expansion state lost.");
        });
        Check("ClosedCollectionSessionCollectible",()=>
        {
            var vm=CollectionScene();var weak=CollectionSessionWeak(vm);ForceFullGc();RequireCapability(!weak.IsAlive,"Closed collection session retained by owner/rows.");GC.KeepAlive(vm);
        });
        Check("ClosedDialogReleasesSessionWithWindowAlive",()=>
        {
            var vm=CollectionScene();var (window,reference)=ClosedCollectionDialog(vm);NativeItemsUiWait(1000);ForceFullGc();
            if(reference.IsAlive)DumpRetainedPreviewIfRequested();
            RequireCapability(!reference.IsAlive,"Closed dialog retains its disposed session after dispatcher idle.");GC.KeepAlive(window);GC.KeepAlive(vm);
        });
        Check("TwentyEditorCyclesReleaseSessions",()=>
        {
            var vm=CollectionScene();var windows=new List<Window>();var refs=new List<WeakReference>();
            for(var i=0;i<20;i++){var pair=ClosedCollectionDialog(vm);windows.Add(pair.Item1);refs.Add(pair.Item2);}
            NativeItemsUiWait(1000);ForceFullGc();RequireCapability(refs.All(r=>!r.IsAlive)&&vm.ActiveCollectionEditor is null,"Collection sessions accumulated across 20 closes.");GC.KeepAlive(windows);GC.KeepAlive(vm);
        });
        Check("NestedFlyoutLogicBothPreviews",()=>
        {
            var vm=CollectionScene();var target=vm.CreateControl("TextBlock",0,0);target.Text="initial";target.GridRow=2;target.GridColumn=0;
            var s=OpenCollection(vm,"SplitButton",StandardItemsProperties.MenuKey);s.Add();s.Add(nested:true);var leaf=s.SelectedItem!;var owner=vm.SelectedControl!;
            vm.Interactions.Add(new(){SourceControlName=StandardItemsProperties.MenuSource(owner,leaf).Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionSetProperty,TargetControlName=target.Name,TargetProperty="Text",TextTemplate="nested-clicked"});vm.CloseCollectionEditor(s);
            foreach(var mode in new[]{false,true})
            {
                using var p=ShowDataGridRegressionPreview(vm,mode);var split=(SplitButton)CollectionNative(p.Window,owner);var menu=(MenuFlyout)split.Flyout!;
                var parent=(MenuItem)menu.Items[^1]!;((MenuItem)parent.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));NativeItemsUiWait(100);
                RequireCapability(p.Window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="nested-clicked"),"Nested Flyout Logic failed, AXAML="+mode);
            }
        });
        Check("NestedDepthLimit",()=>
        {
            var root=new StandardMenuItem();var cursor=root;for(var i=0;i<18;i++){var child=new StandardMenuItem();cursor.Children.Add(child);cursor=child;}
            var rejected=false;try{CollectionItemsContract.Validate(new(){Items=new(){root}},"MenuItem");}catch(FormatException){rejected=true;}RequireCapability(rejected,"Unbounded hierarchy accepted.");
        });
        Check("MultiFormHistoryAndCollectionIsolation",()=>
        {
            var vm=CollectionScene();var form1=vm.ActiveDocumentId;var s=OpenCollection(vm,"Menu");s.Add();s.SetValue(s.SelectedItemId,"Header","Только первая");vm.CloseCollectionEditor(s);
            vm.AddFormEditorCommand!.Execute(null);var form2=vm.ActiveDocumentId;var menu=vm.CreateControl("Menu",0,0);vm.SelectSingleControl(menu);var second=vm.OpenCollectionEditor("Items")!;second.Add();second.SetValue(second.SelectedItemId,"Header","Только вторая");vm.CloseCollectionEditor(second);
            vm.SetActiveForm(form1,"collection3b");RequireCapability(CollectionItemsContract.Read(vm.Controls.Single(c=>c.Type=="Menu"),"Items")!.Items[0].Text=="Только первая","Forms mixed collection values.");
            vm.SetActiveForm(form2,"collection3b");GridEditorUndo(vm);RequireCapability(!CollectionItemsContract.Read(vm.Controls.Single(c=>c.Type=="Menu"),"Items")!.Items.Any(i=>i.Text=="Только первая"),"Undo crossed forms.");
            var restored=GridFirstVm();restored.LoadDocumentJson(vm.ExportWorkspaceJson());RequireCapability(restored.Forms.Count==2,"Multi Form JSON lost a form.");
        });
        Check("ExportFixturesGridCanvasAndMultiForm",()=>
        {
            foreach(var layout in new[]{"Grid","Canvas"})
            {
                var vm=CollectionScene(layout);foreach(var type in new[]{"Menu","ComboBox","TabControl","TreeView","ListBox","SplitButton"})SeedCollection(vm,type);
                var context=OpenCollection(vm,"TreeView","ContextMenu.Items");context.Add();vm.CloseCollectionEditor(context);
                foreach(var c in vm.Controls.Where(c=>c.Type is "TabControl" or "ComboBox" or "ListBox"))StandardItemsProperties.Write(c,"SelectedIndex",0);
                File.WriteAllText(Path.Combine(Collection3BFolder,layout+".json"),vm.ExportWorkspaceJson());Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(Collection3BFolder,"exports",layout)));
                _=System.Xml.Linq.XDocument.Parse(vm.GeneratedXaml);
                foreach(var mode in new[]{false,true}){using var p=ShowDataGridRegressionPreview(vm,mode);foreach(var model in vm.Controls)RequireCapability(CollectionNative(p.Window,model).GetType().Name==model.Type,"Native collection lost in "+layout);}
            }
        });
        Console.WriteLine($"Collection 3B checks: {pass}/{pass+fail}");return fail==0?0:1;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference CollectionSessionWeak(MainWindowViewModel vm)
    {var s=OpenCollection(vm,"Menu");s.Add();var weak=new WeakReference(s);vm.CloseCollectionEditor(s);return weak;}
    [MethodImpl(MethodImplOptions.NoInlining)] private static (Window,WeakReference) ClosedCollectionDialog(MainWindowViewModel vm)
    {var s=OpenCollection(vm,"Menu");s.Add();var window=new StandardItemsEditorWindow(vm,s){ShowInTaskbar=false};window.Show();NativeItemsUiWait(40);vm.PropertyGridSearchText="Name";window.UpdateLayout();window.Close();return(window,new WeakReference(s));}
    private static int RunCollections3BProfile()
    {
        var profileFolder=Environment.GetEnvironmentVariable("COLLECTION3B_PROFILE_OUTPUT")??Collection3BFolder;
        Directory.CreateDirectory(profileFolder);var samples=new List<object>();
        var vm=GridFirstVm();var menu=vm.CreateControl("Menu",0,0);menu.Width=600;menu.Height=60;menu.WidthMode=menu.HeightMode="Fixed";
        var main=Phase3Show(vm);
        var selectionOnly=Environment.GetEnvironmentVariable("COLLECTION3B_PROFILE_SELECTION_ONLY")=="1";
        var outputName=selectionOnly?"collection-selection-performance":"collection-performance";
        var counts=Environment.GetEnvironmentVariable("COLLECTION3B_PROFILE_COUNTS") is { } values?values.Split(',').Select(int.Parse).ToArray():selectionOnly?new[]{1}:new[]{1,10,100};
        if(counts.Any(n=>n<1||n>1000))throw new ArgumentOutOfRangeException(nameof(counts));
        foreach(var count in counts)
        {
            var contract=new CollectionItemsContract { Items=Enumerable.Range(0,count).Select(i=>new StandardMenuItem{Text="Элемент "+i}).ToList() };
            StandardItemsProperties.Write(menu,CollectionItemsContract.StorageKey("Items"),contract);var seed=vm.ExportWorkspaceJson();
            for(var iteration=-3;iteration<30;iteration++)
            {
                vm.LoadDocumentJson(seed);menu=vm.Controls.Single(c=>c.Type=="Menu");vm.SelectSingleControl(menu);main.UpdateLayout();NativeItemsUiWait(15);
                CollectionEditorSession? session=null;StandardItemsEditorWindow? editor=null;
                void Measure(string operation,Action action)
                {
                    var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var bytes=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                    using var profile=FormDesigner.Services.LayoutDropProfile.Begin(vm.ActiveDocumentId,"collection-profile");
                    action();editor!.UpdateLayout();var synchronous=watch.Elapsed.TotalMilliseconds;double? compositor=null;
                    ElementComposition.GetElementVisual(editor)?.Compositor.RequestCompositionUpdate(()=>compositor=watch.Elapsed.TotalMilliseconds);
                    var callbackWait=Stopwatch.StartNew();
                    while(compositor is null&&callbackWait.Elapsed<TimeSpan.FromSeconds(2))NativeItemsUiWait(2);
                    RequireCapability(compositor is not null,"Collection profiler compositor checkpoint timed out.");profile.Dispose();
                    var sample=new{Count=count,Iteration=iteration,Operation=operation,ElapsedMs=synchronous,FirstCompositorUpdateMs=compositor,AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes,
                        GcCollections=Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gc[g]).ToArray(),Profile=profile.Stages,Nodes=session!.Count,
                        NativeEditors=editor.GetVisualDescendants().OfType<PropertyGridTextEditor>().Count()};
                    samples.Add(sample);
                    File.AppendAllText(Path.Combine(profileFolder,outputName+"-progress.jsonl"),JsonSerializer.Serialize(sample)+Environment.NewLine);
                }
                Measure("Open",()=>{session=vm.OpenCollectionEditor("Items")!;editor=new StandardItemsEditorWindow(vm,session){ShowInTaskbar=false};editor.Show();});
                Measure("Add",()=>RequireCapability(session!.Add(),"Add failed."));
                Measure("Reorder",()=>RequireCapability(session!.Move(-1),"Reorder failed."));
                Measure("Selection",()=>session!.Select(session.Roots.First(n=>n.Id!=session.SelectedItemId).Id));
                if(count==100)Measure("NestedAdd",()=>RequireCapability(session!.Add(nested:true),"Nested add failed."));
                editor!.Close();RequireCapability(session!.IsDisposed,"Profile leaked session.");
                if(count==1&&!selectionOnly)
                {
                    foreach(var additions in new[]{10,100})
                    {
                        vm.LoadDocumentJson(seed);vm.SelectSingleControl(vm.Controls.Single(c=>c.Type=="Menu"));session=vm.OpenCollectionEditor("Items")!;
                        editor=new StandardItemsEditorWindow(vm,session){ShowInTaskbar=false};editor.Show();editor.UpdateLayout();NativeItemsUiWait(10);
                        Measure("AddSequence"+additions,()=>{for(var j=0;j<additions;j++)RequireCapability(session.Add(),"Sequence add failed.");});editor.Close();
                    }
                }
            }
        }
        foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();
        File.WriteAllText(Path.Combine(profileFolder,outputName+".json"),JsonSerializer.Serialize(new{Warmup=3,MeasuredPerScenario=30,RenderScaling=main.RenderScaling,Samples=samples},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COLLECTION3B_PROFILE samples="+samples.Count+"; warm-up retained; no timing thresholds.");return 0;
    }
}
