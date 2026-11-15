using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static MainWindow Phase3Show(MainWindowViewModel vm)
    { var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(150);w.UpdateLayout();return w; }
    private static MainWindowViewModel Phase3Source(string attributes)
    {
        var source="<Window xmlns=\"https://github.com/avaloniaui\"><Grid><!-- keep --><NumericUpDown Name=\"Source3\" "+attributes+" /></Grid></Window>";
        var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source,"phase3.axaml"),"phase3.axaml");
        vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Source3"));return vm;
    }
    private static int RunPropertyGrid3AChecks()
    {
        Directory.CreateDirectory(PropertyGrid3AFolder);int passed=0,failed=0;
        void Check(string name,Action action)
        {
            var filter=Environment.GetEnvironmentVariable("PROPERTYGRID3A_FILTER");if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{action();passed++;Console.WriteLine("PASS PROPERTYGRID3A_"+name);}catch(Exception e){failed++;Console.WriteLine("FAIL PROPERTYGRID3A_"+name+": "+e);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}
        }
        Check("SourceInfinityNoop",()=>
        {
            var vm=Phase3Source("MaxWidth=\"Infinity\"");
            var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&!patch.HasChanges,"No-op removed explicit Infinity: "+patch.PatchedText);
            RequireCapability(!GetPropertyGridRow(vm,"MaxWidth").IsReadOnly,"Literal Infinity was classified as a complex expression.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"MaxWidth"),"200");
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("MaxWidth=\"200\""),"Finite constraint edit failed.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"MaxWidth"),"Infinity");
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Explicit Infinity edit became attribute removal.");
            vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,"MaxWidth"));
            RequireCapability(!vm.CreateActiveAxamlPatch().PatchedText.Contains("MaxWidth="),"Only Reset should remove the local source constraint.");
        });
        Check("SourceResetRemovesOnlyAttribute",()=>
        {
            var vm=Phase3Source("Value='12.5' Minimum='0' Tag='{Binding Keep}'");
            var w=Phase3Show(vm);var before=vm.CreateActiveAxamlPatch().PatchedText;var count=vm.ActiveSession.UndoSnapshots.Count;
            GetPropertyGridRow(vm,"Value").ResetToDefault();var patch=vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText==before.Replace("Value='12.5'",""),"Reset was not a single attribute removal: "+patch.PatchedText);
            RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count+1,"Reset was not atomic.");
            GridEditorUndo(vm);RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Undo reset lost original literal: "+vm.CreateActiveAxamlPatch().PatchedText);
            GridEditorUndo(vm,true);var redo=vm.CreateActiveAxamlPatch();RequireCapability(redo.CanApply&&redo.PatchedText==patch.PatchedText,"Redo reset lost deletion: "+JsonSerializer.Serialize(new{redo.CanApply,redo.PatchedText,Properties=vm.Controls.Single(c=>c.Name=="Source3").CustomProperties.Select(p=>p.Key).ToArray(),Undo=vm.ActiveSession.UndoSnapshots.Count,Redo=vm.ActiveSession.RedoSnapshots.Count}));
        });
        Check("SelectionDoesNotWriteOrInvalidateRedo",()=>
        {
            var vm=Universal2Scene();var before=JsonSerializer.Serialize(vm.Controls.Select(c=>new{c.Id,Properties=c.CustomProperties.Select(p=>new{p.Key,p.ValueJson}).ToArray()}));
            var w=Phase3Show(vm);var undo=vm.ActiveSession.UndoSnapshots.Count;
            for(var i=0;i<18;i++){vm.SelectSingleControl(vm.Controls[i%vm.Controls.Count]);w.UpdateLayout();NativeItemsUiWait(8);}
            var after=JsonSerializer.Serialize(vm.Controls.Select(c=>new{c.Id,Properties=c.CustomProperties.Select(p=>new{p.Key,p.ValueJson}).ToArray()}));
            RequireCapability(before==after&&vm.ActiveSession.UndoSnapshots.Count==undo,"Recycling wrote values/history while selecting.");
        });
        Check("MetadataSchemaAndComplexRows",()=>
        {
            var vm=Universal2Scene();vm.SelectSingleControl(vm.Controls.Single(c=>c.Type=="NumericUpDown"));Phase3Show(vm);
            var row=GetPropertyGridRow(vm,"Value");var schema=row.PropertyMetadata!;
            RequireCapability(schema.PropertyType==typeof(decimal?)&&schema.Native.Property is not null&&schema.IsNullable&&schema.Importable&&schema.Exportable,
                "Actual API metadata/capabilities missing.");
            var context=GetPropertyGridRow(vm,"DataContext");RequireCapability(context.IsReadOnly&&context.IsReadOnlyEditor&&context.PropertyMetadata?.Name=="DataContext","Complex DataContext was scalarized.");
            var grid=GetPropertyGridRow(vm,"GridRow");RequireCapability(grid.PropertyMetadata?.IsAttached==true&&grid.PropertyMetadata.Name=="Row","Attached alias metadata lost.");
        });
        foreach(var test in new[]{("NumericUpDown","Value","19.75"),("RadioButton","IsChecked","False"),("Calendar","FirstDayOfWeek","Monday"),
            ("NumericUpDown","Padding","2,4,6,8"),("NumericUpDown","CornerRadius","1,3,5,7"),("NumericUpDown","BorderBrush","#80446688"),
            ("NumericUpDown","FontStyle","Italic"),("NumericUpDown","FontStretch","Condensed")})
        {
            var (type,key,value)=test;
            Check("TypedEditUndoJsonPreview_"+key,()=>
            {
                var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type==type);vm.SelectSingleControl(model);var w=Phase3Show(vm);
                vm.EndPropertyGridTextEdit();vm.BeginUndoBatch();vm.CommitUndoBatch();var count=vm.ActiveSession.UndoSnapshots.Count;var old=model.CustomProperties.FirstOrDefault(p=>p.Key==key)?.ValueJson;
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),value);var metadata=StandardControlProperties.Find(type,key)!;
                RequireCapability(StandardControlProperties.TryParseValue(metadata,value,out var expected),"Bad fixture.");
                RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count+1,"Edit was not one snapshot: "+JsonSerializer.Serialize(new{Before=count,After=vm.ActiveSession.UndoSnapshots.Count,key,Current=vm.GetControl(model.Id)?.CustomProperties.FirstOrDefault(p=>p.Key==key)?.ValueJson,
                    Status=vm.StatusText,Fields=new[]{"_undoBatchDepth","_isPropertyGridEditUndoBatchOpen","_isEditingPropertyGrid","_inspectorDocumentRevision","_editingPropertyGridRevision"}.ToDictionary(k=>k,k=>typeof(MainWindowViewModel).GetField(k,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm))}));
                GridEditorUndo(vm);RequireCapability(vm.GetControl(model.Id)!.CustomProperties.FirstOrDefault(p=>p.Key==key)?.ValueJson==old,"Undo lost typed value.");GridEditorUndo(vm,true);
                var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());var restored=clone.GetControl(model.Id)!;
                RequireCapability(restored.ParentId==model.ParentId&&restored.GridRow==model.GridRow,"JSON lost placement.");
                foreach(var mode in new[]{false,true}){using var p=ShowDataGridRegressionPreview(clone,mode);var native=Universal2Native(p.Window,restored);
                    var actual=native.GetValue(metadata.Property!);RequireCapability(actual is ISolidColorBrush brush&&expected is ISolidColorBrush e?brush.Color==e.Color:Equals(actual,expected),"Native property differs after JSON/Preview: "+key);}
            });
        }
        Check("NullableEnumAndExactNumericCodecs",()=>
        {
            foreach(var item in new[]{(typeof(int),"2147483647"),(typeof(long),"9007199254740993"),(typeof(float),"1.25"),(typeof(double),"1.23456789012345"),(typeof(decimal),"79228162514264337593543950334")})
                RequireCapability(MetadataValueCodec.TryParse(item.Item1,item.Item2,out var parsed)&&MetadataValueCodec.Display(parsed)==item.Item2,"Numeric precision lost: "+item.Item1);
            RequireCapability(MetadataValueCodec.TryParse(typeof(Avalonia.Layout.Orientation?),"(null)",out var n)&&n is null,"Nullable enum failed.");
            RequireCapability(!MetadataValueCodec.TryParse(typeof(long),"9223372036854775808",out _),"Long overflow accepted.");
        });
        Check("NullableAndEmptyStringNative",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="RadioButton");vm.SelectSingleControl(model);Phase3Show(vm);
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"GroupName"),"");RequireCapability(model.CustomProperties.Single(p=>p.Key=="GroupName").ValueJson=="\"\"","Empty string replaced with default.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"GroupName"),"(null)");RequireCapability(model.CustomProperties.Single(p=>p.Key=="GroupName").ValueJson=="null","Null string was not JSON null.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"IsChecked"),"(null)");
            foreach(var mode in new[]{false,true}){using var p=ShowDataGridRegressionPreview(vm,mode);var native=(RadioButton)Universal2Native(p.Window,model);RequireCapability(native.GroupName is null&&native.IsChecked is null,"Nullable native state lost.");}
        });
        Check("ValidationPreservesModel",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);Phase3Show(vm);
            var before=model.CustomProperties.Single(p=>p.Key=="Value").ValueJson;
            var row=GetPropertyGridRow(vm,"Value");vm.CommitPropertyGridTextEdit(row,"101");RequireCapability(row.HasValidationError&&model.CustomProperties.Single(p=>p.Key=="Value").ValueJson==before,"Out-of-range value changed model.");
            RequireCapability(StandardControlProperties.TryWriteModel(model,StandardControlProperties.Find(model.Type,"Maximum")!,"200"),"Increasing Maximum incorrectly compared with old Maximum.");
            RequireCapability(!StandardControlProperties.TryWriteModel(model,StandardControlProperties.Find(model.Type,"Minimum")!,"201"),"Minimum exceeded Maximum.");
        });
        Check("ResetDeletesLocalEvenWhenEqualDefault",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);Phase3Show(vm);
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"AllowSpin"),"True");
            var row=GetPropertyGridRow(vm,"AllowSpin");RequireCapability(row.HasLocalValue==true&&row.CanResetToDefault,"Explicit default confused with absence: "+JsonSerializer.Serialize(new{row.HasLocalValue,row.CanResetToDefault,row.IsReadOnly,row.Value,Json=model.CustomProperties.FirstOrDefault(p=>p.Key=="AllowSpin")?.ValueJson,Current=vm.GetControl(model.Id)!.CustomProperties.FirstOrDefault(p=>p.Key=="AllowSpin")?.ValueJson}));
            var count=vm.ActiveSession.UndoSnapshots.Count;row.ResetToDefault();RequireCapability(!model.CustomProperties.Any(p=>p.Key=="AllowSpin")&&vm.ActiveSession.UndoSnapshots.Count==count+1,"Reset wrote default literal or duplicate snapshots.");
            GridEditorUndo(vm);RequireCapability(vm.GetControl(model.Id)!.CustomProperties.Any(p=>p.Key=="AllowSpin"),"Undo reset did not restore explicit default.");
            GridEditorUndo(vm,true);RequireCapability(!vm.GetControl(model.Id)!.CustomProperties.Any(p=>p.Key=="AllowSpin"),"Redo reset did not remove property.");
            using var p=ShowDataGridRegressionPreview(vm,true);RequireCapability(((NumericUpDown)Universal2Native(p.Window,vm.GetControl(model.Id)!)).AllowSpin,"Native default not restored.");
        });
        Check("StaleRowAfterUndoAndFormSwitch",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);Phase3Show(vm);
            var stale=GetPropertyGridRow(vm,"Value");vm.CommitPropertyGridTextEdit(stale,"20");GridEditorUndo(vm);
            var expected=vm.GetControl(model.Id)!.CustomProperties.Single(p=>p.Key=="Value").ValueJson;
            vm.CommitPropertyGridEdit(stale,"55");stale.ResetToDefault();RequireCapability(vm.GetControl(model.Id)!.CustomProperties.Single(p=>p.Key=="Value").ValueJson==expected,"Old Row wrote into restored model.");
            vm.CreateNewForm("Grid");vm.CommitPropertyGridEdit(stale,"88");RequireCapability(vm.Controls.Count==0,"Old Row modified another form.");
        });
        Check("SourceBindingResourceAndTemplateProtection",()=>
        {
            var vm=Phase3Source("Value='{Binding Keep}' BorderBrush='{DynamicResource Accent}' Tag='{StaticResource Keep}'");Phase3Show(vm);
            var before=vm.CreateActiveAxamlPatch().PatchedText;
            foreach(var key in new[]{"Value","BorderBrush"}){var row=GetPropertyGridRow(vm,key);RequireCapability(row.IsReadOnly&&!row.CanResetToDefault,"Protected source became editable.");vm.CommitPropertyGridEdit(row,"0");row.ResetToDefault();}
            RequireCapability(GetPropertyGridRow(vm,"Value").ValueSource==PropertyGridValueSource.Binding&&GetPropertyGridRow(vm,"BorderBrush").ValueSource==PropertyGridValueSource.Resource,"Value Source classification wrong.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Increment"),"0.5");var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.PatchedText.Contains("Value='{Binding Keep}'")&&patch.PatchedText.Contains("BorderBrush='{DynamicResource Accent}'"),"Protected expression was rewritten.");
        });
        Check("ThicknessAndCornerNativeEditors",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);var w=Phase3Show(vm);
            foreach(var key in new[]{"Padding","CornerRadius"})
            {
                var editor=w.GetVisualDescendants().OfType<PropertyGridCompoundEditor>().Single(e=>e.Row?.Key==key);editor.ExpandComponents();w.UpdateLayout();
                var count=vm.ActiveSession.UndoSnapshots.Count;editor.Components[0].Focus();editor.Components[0].Text="9";
                editor.Components[0].RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});NativeItemsUiWait(50);
                RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count+1,"Compound editor created multiple snapshots.");
                var metadata=StandardControlProperties.Find(model.Type,key)!;var stored=model.CustomProperties.Single(p=>p.Key==key).ValueJson;
                StandardControlProperties.TryParseValue(metadata,StandardControlProperties.Read(metadata,stored),out var actual);
                RequireCapability(actual is Thickness t?t.Left==9:actual is CornerRadius c&&c.TopLeft==9,"Component order/value wrong.");
            }
        });
        Check("GridLengthEditorAndUndo",()=>
        {
            var vm=GridFirstVm();vm.SurfaceGridRowDefinitions="Auto,2*,140";vm.SurfaceLayoutRows=3;var w=Phase3Show(vm);vm.SelectSingleControl(null);NativeItemsUiWait(50);w.UpdateLayout();
            var editor=w.GetVisualDescendants().OfType<PropertyGridGridLengthEditor>().Single(e=>e.Row?.Key=="SurfaceGridRowDefinitions");editor.ExpandDefinitions();w.UpdateLayout();
            RequireCapability(editor.Definitions.Select(d=>d.DisplaySizeKind).SequenceEqual(new[]{"Auto","Star","Pixel"}),"Grid units were converted by displaying editor.");
            var count=vm.ActiveSession.UndoSnapshots.Count;editor.CommitDefinition(1,new GridLength(3,GridUnitType.Star));RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count+1&&RowDefinitions.Parse(vm.SurfaceGridRowDefinitions)[1].Height==new GridLength(3,GridUnitType.Star),"GridLength change not atomic.");
            GridEditorUndo(vm);RequireCapability(RowDefinitions.Parse(vm.SurfaceGridRowDefinitions)[1].Height==new GridLength(2,GridUnitType.Star),"GridLength undo failed.");GridEditorUndo(vm,true);
            using var p=ShowDataGridRegressionPreview(vm,true);var grid=p.Window.GetVisualDescendants().OfType<Grid>().First(g=>g.Name=="RootLayout");RequireCapability(grid.RowDefinitions[0].Height.IsAuto&&grid.RowDefinitions[1].Height==new GridLength(3,GridUnitType.Star)&&grid.RowDefinitions[2].Height==new GridLength(140),"Native Grid units differ.");
        });
        Check("StyleEffectiveValueAndClearLocal",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);var w=Phase3Show(vm);
            var canvas=w.GetVisualDescendants().OfType<DesignerSurface>().Single().Canvas;
            canvas.Styles.Add(new Style(s=>s.OfType<NumericUpDown>()){Setters={new Setter(TemplatedControl.FontStyleProperty,FontStyle.Oblique)}});
            NativeItemsUiWait(70);w.UpdateLayout();
            void Read(){typeof(MainWindowViewModel).GetMethod("RefreshNativeInspectorValues",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,new object?[]{Universal2Native(w,vm.GetControl(model.Id)!)});}
            Read();var row=GetPropertyGridRow(vm,"FontStyle");RequireCapability(row.ValueSource==PropertyGridValueSource.Style&&row.Value=="Oblique"&&row.HasLocalValue==false,"Style/default/effective value conflated: "+row.PresentationDescription);
            vm.CommitPropertyGridTextEdit(row,"Italic");NativeItemsUiWait(80);GetPropertyGridRow(vm,"FontStyle").ResetToDefault();NativeItemsUiWait(80);w.UpdateLayout();Read();
            var after=(NumericUpDown)Universal2Native(w,vm.GetControl(model.Id)!);var info=GetPropertyGridRow(vm,"FontStyle");
            RequireCapability(after.FontStyle==FontStyle.Oblique&&!vm.GetControl(model.Id)!.CustomProperties.Any(p=>p.Key=="FontStyle"),"Reset wrote metadata default instead of exposing Style: "+JsonSerializer.Serialize(new{Native=after.FontStyle.ToString(),Json=vm.GetControl(model.Id)!.CustomProperties.FirstOrDefault(p=>p.Key=="FontStyle")?.ValueJson,info.HasLocalValue,info.CanResetToDefault,info.Value,info.EffectiveValue,Styles=canvas.Styles.Count,info.SourceDetail}));
        });
        Check("GradientReadOnlyAndNoGetterSerialization",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);var w=Phase3Show(vm);
            var native=(NumericUpDown)Universal2Native(w,model);native.BorderBrush=new LinearGradientBrush();
            typeof(MainWindowViewModel).GetMethod("RefreshNativeInspectorValues",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,new object?[]{native});
            var row=GetPropertyGridRow(vm,"BorderBrush");RequireCapability(row.IsReadOnly&&!row.CanResetToDefault,"Gradient became a scalar Color editor.");
            var before=JsonSerializer.Serialize(model.CustomProperties);vm.CommitPropertyGridEdit(row,"Red");RequireCapability(before==JsonSerializer.Serialize(model.CustomProperties),"Gradient was overwritten.");
        });
        Check("EnumNativeInputAndRecycling",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);var w=Phase3Show(vm);
            var combo=w.GetVisualDescendants().OfType<PropertyGridEnumEditor>().Single(e=>e.Row?.Key=="FontStyle");
            var count=vm.ActiveSession.UndoSnapshots.Count;combo.CommitSelection("Italic");NativeItemsUiWait(60);
            RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count+1&&model.CustomProperties.Single(p=>p.Key=="FontStyle").ValueJson=="\"Italic\"","Enum explicit selection failed.");
            combo=w.GetVisualDescendants().OfType<PropertyGridEnumEditor>().Single(e=>e.Row?.Key=="FontStyle");combo.Focus();
            combo.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Up});NativeItemsUiWait(50);
            RequireCapability(model.CustomProperties.Single(p=>p.Key=="FontStyle").ValueJson!="\"Italic\"","Native keyboard selection was ignored.");
        });
        Check("FavoritesSearchAndScroll",()=>
        {
            var vm=Universal2Scene();var numeric=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(numeric);var w=Phase3Show(vm);
            vm.TogglePropertyGridFavoriteCommand.Execute(GetPropertyGridRow(vm,"Increment"));
            RequireCapability(GetPropertyGridRow(vm,"Increment").IsFavorite,"Favorite lost.");vm.PropertyGridSearchText="Increment";w.UpdateLayout();
            RequireCapability(vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.Key=="Increment")&&vm.SelectedControl?.Id==numeric.Id,"Search lost target.");
            vm.PropertyGridSearchText="";w.UpdateLayout();var inspector=w.FindControl<DesignerPropertyInspector>("DesignerPropertyInspector")!;
            var scroll=inspector.GetVisualAncestors().OfType<ScrollViewer>().First();scroll.Offset=new Vector(0,120);w.UpdateLayout();var offset=scroll.Offset.Y;
            var another=vm.CreateControl("NumericUpDown",0,0);vm.SelectSingleControl(another);w.UpdateLayout();RequireCapability(Math.Abs(scroll.Offset.Y-offset)<1.1,"Same-schema selection reset scroll.");
        });
        Check("MultiFormTypedValuesAndReset",()=>
        {
            var vm=Universal2Scene();var first=vm.ActiveDocumentId;var a=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(a);Phase3Show(vm);
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Value"),"33.25");var second=vm.CreateNewForm("Absolute").Id;
            var b=vm.CreateControl("NumericUpDown",10,20);vm.SelectSingleControl(b);vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Value"),"44.75");
            vm.SetActiveForm(first,"phase3");RequireCapability(vm.GetControl(a.Id)!.CustomProperties.Single(p=>p.Key=="Value").ValueJson=="33.25","Form 1 changed.");
            vm.SelectSingleControl(vm.GetControl(a.Id)!);GetPropertyGridRow(vm,"Value").ResetToDefault();GridEditorUndo(vm);
            var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());clone.SetActiveForm(second,"reopen");RequireCapability(clone.GetControl(b.Id)!.CustomProperties.Single(p=>p.Key=="Value").ValueJson=="44.75","Form 2 state/history mixed.");
        });
        Check("RepeatedSelectionReleasesOldRows",()=>
        {
            var vm=Universal2Scene();var w=Phase3Show(vm);var reference=MakeOldInspectorRow(vm,w);
            ForceFullGc();RequireCapability(!reference.IsAlive,"Recycled views retain old RowVM.");GC.KeepAlive(w);GC.KeepAlive(vm);
        });
        Check("ContinueTypingAfterEnter",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);var w=Phase3Show(vm);
            PropertyGridTextEditor Editor()=>w.GetVisualDescendants().OfType<PropertyGridTextEditor>().Single(e=>e.Row?.Key=="Value");
            var editor=Editor();editor.Focus();editor.Text="17";editor.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});NativeItemsUiWait(50);
            editor=Editor();RequireCapability(editor.IsFocused,"Commit recreated editor/lost focus.");editor.SelectionStart=0;editor.SelectionEnd=editor.Text!.Length;
            editor.RaiseEvent(new TextInputEventArgs{RoutedEvent=InputElement.TextInputEvent,Text="18.25"});
            w.GetVisualDescendants().OfType<PropertyGridTextEditor>().First(e=>e.Row?.Key=="Name").Focus();NativeItemsUiWait(50);
            RequireCapability(model.CustomProperties.Single(p=>p.Key=="Value").ValueJson=="18.25","Typing after Enter was lost on focus change.");
        });
        Check("InheritedValueSource",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);var w=Phase3Show(vm);w.FontStyle=FontStyle.Italic;w.UpdateLayout();
            var native=Universal2Native(w,model);typeof(MainWindowViewModel).GetMethod("RefreshNativeInspectorValues",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,new object?[]{native});
            var row=GetPropertyGridRow(vm,"FontStyle");RequireCapability(row.ValueSource==PropertyGridValueSource.Inherited&&row.EffectiveValue=="Italic"&&row.HasLocalValue==false,"Inherited priority/effective value not identified: "+row.PresentationDescription);
        });
        Check("ReplacedSessionReleased",()=>
        {
            var vm=Universal2Scene();var w=Phase3Show(vm);var reference=ReplaceInspectorSession(vm,w);ForceFullGc();
            RequireCapability(!reference.IsAlive,"Presentation retains old document session after New Project.");GC.KeepAlive(vm);GC.KeepAlive(w);
        });
        Check("ExportFixtures",()=>
        {
            foreach(var layout in new[]{"Grid","Canvas"})
            {
                var vm=Universal2Scene(layout);var n=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(n);
                foreach(var pair in new[]{("Padding","2,4,6,8"),("CornerRadius","1,3,5,7"),("BorderBrush","#80446688"),("FontStyle","Italic"),("FontStretch","Condensed"),("Value","19.75")})
                    vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,pair.Item1),pair.Item2);
                SetUniversal2(n,"AllowSpin","True");vm.SelectSingleControl(n);GetPropertyGridRow(vm,"AllowSpin").ResetToDefault();
                if(layout=="Grid"){vm.SurfaceGridRowDefinitions="Auto,2*,140";vm.SurfaceLayoutRows=3;vm.SurfaceGridColumnDefinitions="160,*";}
                var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());
                foreach(var mode in new[]{false,true}){using var p=ShowDataGridRegressionPreview(clone,mode);var actual=(NumericUpDown)Universal2Native(p.Window,clone.GetControl(n.Id)!);
                    RequireCapability(actual.Padding==new Thickness(2,4,6,8)&&actual.CornerRadius==new CornerRadius(1,3,5,7)&&actual.Value==19.75m&&actual.AllowSpin,"Edited native values lost before export.");}
                File.WriteAllText(Path.Combine(PropertyGrid3AFolder,layout+".json"),clone.ExportWorkspaceJson());Pump(clone.ExportCurrentResultToProjectAsync(Path.Combine(PropertyGrid3AFolder,"exports",layout)));
            }
        });
        Console.WriteLine($"PropertyGrid Phase3A checks: {passed}/{passed+failed}");return failed==0?0:1;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeOldInspectorRow(MainWindowViewModel vm,MainWindow w)
    {
        vm.SelectSingleControl(vm.Controls[0]);w.UpdateLayout();var old=GetPropertyGridRow(vm,"Name");var reference=new WeakReference(old);
        for(var i=1;i<15;i++){vm.SelectSingleControl(vm.Controls[i%vm.Controls.Count]);w.UpdateLayout();NativeItemsUiWait(5);}
        return reference;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceInspectorSession(MainWindowViewModel vm,MainWindow w)
    {
        var old=vm.ActiveSession;var weak=new WeakReference(old);vm.ConfirmNewProjectWithUnsavedChanges=false;
        vm.NewProjectEditorCommand!.Execute(null);NativeItemsUiWait(100);w.UpdateLayout();RequireCapability(old.IsDisposed,"New Project did not dispose old session.");return weak;
    }
}
