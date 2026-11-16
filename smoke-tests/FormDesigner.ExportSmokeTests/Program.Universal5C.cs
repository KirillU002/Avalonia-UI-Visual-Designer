using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static readonly string[] Phase5CTypes={"Label","AccessText","ToggleButton","CalendarDatePicker","MaskedTextBox","ButtonSpinner"};
    private static (string Key,string Value)[] Values5C(string type)=>type switch{
        "Label"=>new[]{("Padding","4,6,8,10"),("IsEnabled","True")},
        "AccessText"=>new[]{("ShowAccessKey","True"),("TextWrapping","Wrap"),("MaxLines","2"),("LetterSpacing","1.5")},
        "ToggleButton"=>new[]{("IsChecked","True"),("IsThreeState","True"),("ClickMode","Release")},
        "CalendarDatePicker"=>new[]{("SelectedDate","2026-11-16T00:00:00"),("DisplayDate","2026-11-01T00:00:00"),("FirstDayOfWeek","Monday"),("IsTodayHighlighted","True"),("SelectedDateFormat","Custom"),("CustomDateFormatString","yyyy-MM-dd")},
        "MaskedTextBox"=>new[]{("Mask","000-000"),("AsciiOnly","True"),("HidePromptOnLeave","False"),("Watermark","Введите код"),("MaxLength","16")},
        _=>new[]{("AllowSpin","True"),("ShowButtonSpinner","True"),("ButtonSpinnerLocation","Left"),("ValidSpinDirection","Increase")}
    };
    private static MainWindowViewModel Scene5C(string layout="Grid",string? only=null)
    {
        var vm=GridFirstVm();if(layout=="Canvas"){vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);}
        vm.DesignWidth=1000;vm.DesignHeight=750;vm.SurfaceGridRowDefinitions="*,*,*";vm.SurfaceGridColumnDefinitions="*,*";
        var types=only is null?Phase5CTypes:new[]{only};
        for(var i=0;i<types.Length;i++){
            var c=vm.CreateControl(types[i],20+i%2*450,20+i/2*210,null,true);c.Name="Phase5C_"+c.Type;
            c.Width=330;c.Height=130;c.WidthMode=c.HeightMode="Fixed";c.GridRow=i/2;c.GridColumn=i%2;
            if(StandardControlProperties.TextPropertyName(c.Type)is not null)c.Text=c.Type=="MaskedTextBox"?"123456":"_Name & literal";
            foreach(var (key,value)in Values5C(c.Type))SetUniversal2(c,key,value);
            if(c.Type=="ButtonSpinner"){var child=vm.CreateControl("TextBox",0,0,c.Id);child.Name="SpinnerContent";child.Text="Spin content";child.Width=180;child.Height=40;child.WidthMode=child.HeightMode="Fixed";}
        }
        return vm;
    }
    private static Control Native5C(Control root,DesignControlModel model,string? type=null)=>root.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==model.Name&&c.GetType()==StandardControlProperties.SupportedTypes[type??model.Type]);
    private static void AssertNative5C(Control control,string type)
    {
        RequireCapability(control.GetType()==StandardControlProperties.SupportedTypes[type]&&control.GetVisualRoot()is not null&&control.Bounds.Width>0&&control.Bounds.Height>0,"Missing native "+type+" "+control.Bounds);
        foreach(var (key,text)in Values5C(type)){
            var p=StandardControlProperties.Find(type,key)!;RequireCapability(StandardControlProperties.TryParseValue(p,text,out var expected),"Invalid test literal "+key);
            var actual=control.GetValue(p.Property!);RequireCapability(Equals(actual,expected),$"Native {type}.{key}: {MetadataValueCodec.Display(actual)} != {text}");
        }
        if(type is "Label" or "ToggleButton")RequireCapability(Equals(((ContentControl)control).Content,"_Name & literal"),"Content lost");
        if(control is AccessText access)RequireCapability(access.Text=="_Name & literal"&&char.ToUpperInvariant(access.AccessKey)=='N',"AccessText mnemonic lost");
        if(control is MaskedTextBox mask)RequireCapability(mask.Text=="123-456","Native mask not applied: "+mask.Text);
        if(control is ButtonSpinner spinner){var content=spinner.Content as Control;RequireCapability(content is not null&&content.GetVisualDescendants().Prepend(content).OfType<TextBox>().Any(t=>t.Text=="Spin content"&&(t.Name=="SpinnerContent"||t.GetVisualAncestors().OfType<Control>().Any(w=>w.Tag is DesignControlModel {Name:"SpinnerContent"}))),"Spinner Content lost: "+string.Join(";",spinner.GetVisualDescendants().OfType<TextBox>().Select(t=>t.Name+"="+t.Text)));}
    }
    private static int RunUniversal5CChecks()
    {
        var pass=0;var fail=0;
        void Check(string name,Action action){var filter=Environment.GetEnvironmentVariable("UNIVERSAL5C_FILTER");if(filter is not null&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{action();pass++;Console.WriteLine("PASS UNIVERSAL5C_"+name);}catch(Exception error){fail++;Console.WriteLine("FAIL UNIVERSAL5C_"+name+": "+error);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}}
        foreach(var type in Phase5CTypes){
            Check(type+"_RegistryInspector",()=>{
                var vm=Scene5C(only:type);var c=vm.Controls.Single(c=>c.Type==type);vm.SelectSingleControl(c);
                RequireCapability(vm.Registry.GetControls().Count(d=>d.TypeKey==type)==1,"Duplicate registration");vm.ToolboxSearchText=type;
                RequireCapability(vm.ToolboxGroups.SelectMany(g=>g.Items).Any(i=>i.Type==type&&i.Icon!="AV"),"Search/icon missing");
                foreach(var (key,_)in Values5C(type))RequireCapability(!GetPropertyGridRow(vm,key).IsReadOnly,"Readonly scalar "+key);
                if(StandardControlProperties.TextPropertyName(type)is not null)RequireCapability(!GetPropertyGridRow(vm,"Text").IsReadOnly,"Model Text missing");
            });
            foreach(var layout in new[]{"Grid","Canvas"})Check(type+"_NativeJson_"+layout,()=>{
                var vm=Scene5C(layout,type);var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(100);window.UpdateLayout();
                AssertNative5C(Native5C(window,vm.Controls.Single(c=>c.Type==type)),type);
                var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());
                RequireCapability(JsonSerializer.Serialize(vm.Controls)==JsonSerializer.Serialize(clone.Controls),"JSON control state changed");
                foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(clone,mode))AssertNative5C(Native5C(preview.Window,clone.Controls.Single(c=>c.Type==type)),type);
                clone.GenerateXaml();RequireCapability(System.Xml.Linq.XDocument.Parse(clone.GeneratedXaml).Descendants().Any(e=>e.Name.LocalName==type),"Native export missing");
            });
            Check(type+"_RoutedDropResizeUndo",()=>{
                using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();var p=GridCellPoint(f.Vm,"",0,0);var data=GridToolboxData(f,type);
                RouteGridToolbox(f,data,DragDrop.DragOverEvent,p);RouteGridToolbox(f,data,DragDrop.DropEvent,p);f.Refresh();f.AssertIdle();var c=f.Vm.Controls.Single();
                RequireCapability(c.Type==type&&c.X==0&&c.Y==0&&f.Vm.SelectedControl?.Id==c.Id,"Grid Drop placement/selection failed");
                // Auto/Stretch fills a Grid cell, whose far corner may be outside the
                // fixture viewport. Exercise authored fixed sizing, as Phase 5B does.
                c.Width=180;c.Height=90;c.WidthMode=c.HeightMode="Fixed";f.Vm.SelectSingleControl(c);f.Refresh();var start=f.ResizePoint();var width=c.Width;f.Down(start);f.Move(start+new Vector(20,15));f.Up(start+new Vector(20,15));f.Refresh();f.AssertIdle();RequireCapability(c.Width>width,"Resize failed");
                GridEditorUndo(f.Vm);GridEditorUndo(f.Vm,true);f.Refresh();RequireCapability(f.Vm.SelectedControl?.Id==c.Id,"Undo selection lost");
            });
            Check(type+"_SourcePatchReset",()=>{
                var (key,value)=Values5C(type)[0];var initial=type switch{"Label"=>"1","AccessText"=>"False","ToggleButton"=>"False","CalendarDatePicker"=>"2026-01-02","MaskedTextBox"=>"LL-000",_=>"False"};
                var vm=Universal4AVm($"<a:{type} Name='Target' {key}='{initial}' u:Unknown='keep'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),value);var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("u:Unknown='keep'"),"Minimal patch failed");
                GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo source changed");GridEditorUndo(vm,true);vm.SelectSingleControl(vm.Controls.Single());
                vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));RequireCapability(!vm.CreateActiveAxamlPatch().PatchedText.Contains(key+"='"),"Reset did not remove attribute");GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Undo Reset lost attribute");
            });
            Check(type+"_ProtectedExpressions",()=>{
                var (key,_)=Values5C(type)[0];var vm=Universal4AVm($"<a:{type} Name='Protected' {key}='{{Binding Unexecuted}}' Opacity='{{DynamicResource Alpha}}' u:Keep='yes'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;
                vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,key).IsReadOnly,"Binding editable");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Source expression changed");
            });
        }
        foreach(var layout in new[]{"Stack","Wrap","Dock"})Check("Nested_"+layout,()=>{
            var vm=Scene5C();var parent=vm.CreateControl(layout=="Dock"?"Group":layout+"Panel",0,0);if(layout=="Dock")parent.ChildLayoutMode="Dock";parent.Width=950;parent.Height=700;parent.WidthMode=parent.HeightMode="Fixed";
            foreach(var c in vm.Controls.Where(c=>Phase5CTypes.Contains(c.Type))){c.ParentId=parent.Id;c.StackOrder=Array.IndexOf(Phase5CTypes,c.Type);}
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))foreach(var c in vm.Controls.Where(c=>Phase5CTypes.Contains(c.Type)))AssertNative5C(Native5C(preview.Window,c),c.Type);
        });
        Check("SourceNativeAll",()=>{
            var seed=Scene5C();seed.GenerateXaml();var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(seed.GeneratedXaml),"phase5c.axaml");
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))foreach(var type in Phase5CTypes)AssertNative5C(Native5C(preview.Window,vm.Controls.Single(c=>c.Name=="Phase5C_"+type),type),type);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==seed.GeneratedXaml,"Native source preview modified source");
        });
        Check("UniformCollectionMetadata",()=>{var m=CollectionMetadataCatalog.Get(typeof(UniformGrid)).Single(p=>p.PropertyName=="Children");RequireCapability(m.CanAdd&&m.CanReorder&&m.SerializationStrategy=="ExistingParentIdAndStackOrder","UniformGrid metadata contradicts its existing editor workflow");});
        Check("ExplorerHierarchySelection",()=>{
            var vm=Scene5C();NativeItemsUiWait(100);
            IEnumerable<StructureTreeItemModel> Walk(IEnumerable<StructureTreeItemModel> roots)=>roots.SelectMany(n=>new[]{n}.Concat(Walk(n.Children)));
            var nodes=Walk(vm.StructureTreeItems).ToArray();RequireCapability(vm.Controls.All(c=>nodes.Any(n=>n.Id==c.Id&&n.Type==c.Type)),"Structure omitted a native control");
            var owner=vm.Controls.Single(c=>c.Type=="ButtonSpinner");var child=vm.Controls.Single(c=>c.ParentId==owner.Id);RequireCapability(nodes.Single(n=>n.Id==owner.Id).Children.Any(n=>n.Id==child.Id),"Explorer lost Content hierarchy");
            vm.SelectedStructureItem=nodes.Single(n=>n.Id==child.Id);RequireCapability(vm.SelectedControl?.Id==child.Id&&vm.PropertyGridContextControlId==child.Id&&vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.ContextControlId==child.Id),"Explorer/Inspector selected a different child: "+JsonSerializer.Serialize(new{Expected=child.Id,Selected=vm.SelectedControl?.Id,Tree=vm.SelectedStructureItem?.Id,Inspector=vm.PropertyGridContextControlId,Rows=vm.PropertyGridCategories.SelectMany(c=>c.Rows).Select(r=>r.ContextControlId).Distinct().ToArray()}));
        });
        Check("ContextRequestRetiredWrapper",()=>{
            using var f=new PointerFixture();var retired=Layout7Wrappers(f.Window)[f.First.Id];f.Vm.SelectSingleControl(f.First);f.Refresh();var current=Layout7Wrappers(f.Window)[f.First.Id];
            RequireCapability(!ReferenceEquals(retired,current)&&retired.GetVisualRoot()is null,"Fixture did not retire the selection wrapper");
            retired.RaiseEvent(new ContextRequestedEventArgs{RoutedEvent=Avalonia.Controls.Control.ContextRequestedEvent});NativeItemsUiWait(40);
            RequireCapability(current.ContextMenu?.IsOpen==true&&retired.ContextMenu is null,"Context request used a retired wrapper instead of the active component");current.ContextMenu!.Close();
        });
        foreach(var reason in new[]{"DocumentSwitch","ControlRemoved"})Check("ContextRequestCancelled_"+reason,()=>{
            using var f=new PointerFixture();var original=Layout7Wrappers(f.Window)[f.First.Id];var model=f.First;
            if(reason=="DocumentSwitch")f.Vm.CreateNewForm();else f.Vm.Controls.Remove(model);f.Refresh();
            original.RaiseEvent(new ContextRequestedEventArgs{RoutedEvent=Avalonia.Controls.Control.ContextRequestedEvent});NativeItemsUiWait(40);
            RequireCapability(original.ContextMenu is null,"Stale context request created a menu after "+reason);
        });
        Check("SpinnerCapacity",()=>{foreach(var layout in new[]{"Grid","Canvas"}){var vm=Scene5C(layout,"ButtonSpinner");var parent=vm.Controls.Single(c=>c.Type=="ButtonSpinner");var before=JsonSerializer.Serialize(vm.Controls);RequireCapability(vm.TryCreateControlFromToolboxDrop("Button",0,0,parent.Id,true,vm.ActiveDocumentId)is null&&JsonSerializer.Serialize(vm.Controls)==before,"Occupied spinner changed project");}});
        Check("TypedEditsUndoJsonMultiForm",()=>{
            var vm=Scene5C(only:"ToggleButton");var c=vm.Controls.Single();vm.SelectSingleControl(c);vm.EndPropertyGridTextEdit();vm.BeginUndoBatch();vm.CommitUndoBatch();var count=vm.ActiveSession.UndoSnapshots.Count;
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"IsChecked"),MetadataValueCodec.NullText);RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count+1,"Nullable edit not atomic");
            GridEditorUndo(vm);RequireCapability(StandardControlProperties.Read(StandardControlProperties.Find("ToggleButton","IsChecked")!,vm.Controls.Single().CustomProperties.Single(p=>p.Key=="IsChecked").ValueJson)=="True","Nullable Undo failed");GridEditorUndo(vm,true);
            var first=vm.ActiveDocumentId;vm.CreateNewForm();var second=vm.ActiveDocumentId;vm.CreateControl("MaskedTextBox",0,0);vm.SetActiveForm(first,"5c");var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());clone.SetActiveForm(first,"5c");RequireCapability(clone.Controls.Single().Id==c.Id,"Multi Form identity mixed");clone.SetActiveForm(second,"5c");RequireCapability(clone.Controls.Single().Type=="MaskedTextBox","Second form lost");
        });
        Check("LiteralMarkupEscaping",()=>{foreach(var type in new[]{"Label","AccessText","MaskedTextBox","ToggleButton"}){var vm=Scene5C(only:type);var c=vm.Controls.Single();if(type=="MaskedTextBox")SetUniversal2(c,"Mask","");c.Text="{literal}";foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode)){var view=Native5C(preview.Window,c);RequireCapability(view is ContentControl cc?Equals(cc.Content,"{literal}"):view is TextBox tb?tb.Text=="{literal}":((AccessText)view).Text=="{literal}","Literal became markup");}}});
        Check("ValidationAndReadonly",()=>{
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("MaskedTextBox","Mask")!,new string('0',1025),out _),"Oversized mask accepted");
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("CalendarDatePicker","CustomDateFormatString")!,"%",out _),"Invalid date format accepted");
            var vm=Scene5C(only:"Label");vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,"Target").IsReadOnly,"Complex Target became text editor");
            vm=Scene5C(only:"MaskedTextBox");vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,"Culture").IsReadOnly&&GetPropertyGridRow(vm,"MaskProvider").IsReadOnly,"Complex mask objects editable");
        });
        foreach(var mode in new[]{false,true})Check("Interactive_"+mode,()=>{
            var vm=Scene5C();using var p=ShowDataGridRegressionPreview(vm,mode);var toggle=(ToggleButton)Native5C(p.Window,vm.Controls.Single(c=>c.Type=="ToggleButton"));
            toggle.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Space});toggle.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Key=Key.Space});RequireCapability(toggle.IsChecked is null,"Three-state keyboard toggle failed");
            var date=(CalendarDatePicker)Native5C(p.Window,vm.Controls.Single(c=>c.Type=="CalendarDatePicker"));date.IsDropDownOpen=true;NativeItemsUiWait(50);RequireCapability(date.IsDropDownOpen,"Calendar popup failed");date.IsDropDownOpen=false;
            var spinner=(ButtonSpinner)Native5C(p.Window,vm.Controls.Single(c=>c.Type=="ButtonSpinner"));var spins=0;spinner.Spin+=(_,_)=>spins++;
            var button=spinner.GetVisualDescendants().OfType<Button>().First(b=>b.Name?.Contains("Increase")==true);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));RequireCapability(spins==1,"Native Spin event failed");
        });
        foreach(var mode in new[]{false,true})Check("Memory20_"+mode,()=>{var vm=Scene5C();var refs=Enumerable.Range(0,20).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);GC.KeepAlive(vm);});
        foreach(var layout in new[]{"Grid","Canvas"})Check("Geometry_"+layout,()=>{
            var vm=Scene5C(layout);var owner=CreateSmokeWindow(vm);owner.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(100);owner.UpdateLayout();var canvas=owner.GetVisualDescendants().OfType<FormDesigner.Views.DesignerSurface>().Single().Canvas;
            using var axaml=ShowDataGridRegressionPreview(vm,true);using var legacy=ShowDataGridRegressionPreview(vm,false);
            foreach(var p in new[]{axaml,legacy}){p.Window.SystemDecorations=SystemDecorations.None;p.Window.Width=1000;p.Window.Height=750;NativeItemsUiWait(60);p.Window.UpdateLayout();}
            Rect Frame(Control c,Visual root)=>new Rect(c.Bounds.Size).TransformToAABB(c.TransformToVisual(root)!.Value);
            foreach(var model in vm.Controls.Where(c=>Phase5CTypes.Contains(c.Type))){var d=Frame(Native5C(owner,model),canvas);var a=Frame(Native5C(axaml.Window,model),axaml.Window.FindControl<Border>("PreviewSurfaceBorder")!);var b=Frame(Native5C(legacy.Window,model),legacy.Window.FindControl<Border>("PreviewSurfaceBorder")!);
                bool Near(Rect x,Rect y)=>Math.Abs(x.X-y.X)<=2&&Math.Abs(x.Y-y.Y)<=2&&Math.Abs(x.Width-y.Width)<=2&&Math.Abs(x.Height-y.Height)<=2;
                RequireCapability(Near(d,a)&&Near(a,b),$"Native geometry {layout}/{model.Type}: Designer={d}; AXAML={a}; Legacy={b}");}
        });
        Console.WriteLine($"UNIVERSAL5C_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    private static int GenerateUniversal5CProjects(){var folder=Environment.GetEnvironmentVariable("UNIVERSAL5C_EXPORT_DIR")!;RequireCapability(!Directory.Exists(folder),"Fresh output required");foreach(var layout in new[]{"Grid","Canvas"})Pump(Scene5C(layout).ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));return 0;}
    private static int RunUniversal5CAudit(){var vm=GridFirstVm();var folder=Environment.GetEnvironmentVariable("UNIVERSAL5C_OUTPUT_DIR")!;MetadataCoverageReport.Write(vm.Registry,folder,Environment.GetEnvironmentVariable("UNIVERSAL5C_RUNTIME_VERIFIED")=="1"?Phase5ATypes.Concat(Phase5BTypes).Concat(Phase5CTypes).ToHashSet():null);Write5CCoverage(vm,folder);return 0;}
}
