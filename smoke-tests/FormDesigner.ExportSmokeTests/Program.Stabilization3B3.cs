using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunStabilization3B3Checks()
    {
        int pass=0,fail=0;
        void Check(string name,Action action)
        {
            try{action();pass++;Console.WriteLine("PASS STABILITY3B3_"+name);}
            catch(Exception error){fail++;Console.WriteLine("FAIL STABILITY3B3_"+name+": "+error);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}
        }
        MainWindowViewModel Source(string type,string attributes,out string text)
        {
            text="<a:Window xmlns:a='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>\r\n"+
                "  <a:Grid RowDefinitions='Auto,2*,120'><!-- preserved -->\r\n    <a:"+type+" x:Name='Target'\t"+attributes+" Tag='{Binding Keep}' />\r\n"+
                "    <a:Button x:Name='Neighbour' Content='Untouched &amp; safe' />\r\n  </a:Grid>\r\n</a:Window>";
            var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(text,"3b3.axaml"),"3b3.axaml");
            vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target"));
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==text&&!vm.CreateActiveAxamlPatch().HasChanges,"Import no-op changed source.");return vm;
        }
        void Navigate(MainWindowViewModel vm,bool redo=false)
        {
            var command=redo?vm.RedoEditorCommand!:vm.UndoEditorCommand!;
            RequireCapability(command.CanExecute(null),"History command stale in same UI turn; no dispatcher wait is allowed.");command.Execute(null);
            RequireCapability(vm.UndoEditorCommand!.CanExecute(null)==vm.HasUndo&&vm.RedoEditorCommand!.CanExecute(null)==vm.HasRedo,"Command/history mismatch after navigation.");
        }
        foreach(var test in new[]{
            ("NumericUpDown","Value","Value='12.50'"),
            ("RadioButton","GroupName","GroupName='O&apos;Reilly &amp; Co'"),
            ("NumericUpDown","IsEnabled","IsEnabled='False'"),
            ("Calendar","FirstDayOfWeek","FirstDayOfWeek='Monday'"),
            ("NumericUpDown","Padding","Padding='1,2,3,4'"),
            ("NumericUpDown","CornerRadius","CornerRadius='1,3,5,7'"),
            ("NumericUpDown","BorderBrush","BorderBrush='#80446688'"),
            ("RadioButton","IsChecked","IsChecked='{x:Null}'")})
        {
            var(type,key,attribute)=test;
            Check("ImmediateResetUndoRedo_"+key,()=>
            {
                var vm=Source(type,attribute,out var source);var id=vm.SelectedControl!.Id;var parent=vm.SelectedControl.ParentId;
                var row=GetPropertyGridRow(vm,key);RequireCapability(row.CanResetToDefault,"Fixture property is not resettable: "+key);
                var n=vm.ActiveSession.UndoSnapshots.Count;vm.ResetPropertyGridProperty(row);
                var reset=vm.CreateActiveAxamlPatch();RequireCapability(reset.CanApply&&reset.Edits.Count==1&&reset.PatchedText==source.Replace(attribute,""),"Reset changed neighbouring source: "+reset.PatchedText);
                RequireCapability(vm.ActiveSession.UndoSnapshots.Count==n+1,"Reset was not atomic.");
                for(var i=0;i<3;i++)
                {
                    Navigate(vm);var undo=vm.CreateActiveAxamlPatch();RequireCapability(!undo.HasChanges&&undo.PatchedText==source,"Undo lost exact source spelling/quotes/escaping.");
                    RequireCapability(vm.SelectedControl?.Id==id&&vm.SelectedControl.ParentId==parent,"Undo lost identity/selection/parent.");
                    Navigate(vm,true);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==reset.PatchedText,"Redo lost attribute removal.");
                }
                var saved=vm.CreateActiveAxamlPatch().PatchedText;_ = XDocument.Parse(saved);vm.MarkAxamlRoundTripSaved("3b3.axaml",saved);
                RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges&&vm.CreateActiveAxamlPatch(saved).CanApply,"Save/rebase left stale source map.");
                RequireCapability(vm.CreateActiveAxamlPatch(saved+" ").ExternalChangeDetected,"Checksum guard lost.");
            });
        }
        Check("EditResetUndoChain",()=>
        {
            var vm=Source("RadioButton","GroupName='Original'",out var source);
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"GroupName"),"Новое & <значение>");var edited=vm.CreateActiveAxamlPatch().PatchedText;
            RequireCapability(edited!=source&&edited.Contains("&amp;")&&edited.Contains("&lt;"),"String edit not escaped.");
            vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,"GroupName"));Navigate(vm);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==edited,"Undo Reset lost edited string.");Navigate(vm);
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Second Undo lost original source.");Navigate(vm,true);Navigate(vm,true);
            RequireCapability(!vm.CreateActiveAxamlPatch().PatchedText.Contains("GroupName="),"Redo chain lost Reset.");
        });
        foreach(var expression in new[]{"{Binding Value}","{StaticResource Accent}","{DynamicResource Accent}"})
            Check("ProtectedExpression_"+expression.Split(' ')[0],()=>
            {
                var key=expression.StartsWith("{Binding")?"Value":"BorderBrush";
                var vm=Source("NumericUpDown",key+"='"+expression+"'",out var source);var row=GetPropertyGridRow(vm,key);
                RequireCapability(row.IsReadOnly&&!row.CanResetToDefault,"Expression became destructive scalar.");
                vm.ResetPropertyGridProperty(row);vm.CommitPropertyGridTextEdit(row,"0");RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Protected expression changed.");
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Increment"),"0.5");Navigate(vm);
                RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"Neighbour Undo changed expression.");
            });
        Check("AttachedPropertyAndGridLengthPreserved",()=>
        {
            var vm=Source("NumericUpDown","Grid.Row='1' Value='12.5'",out var source);
            vm.BeginUndoBatch();vm.SelectedControl!.GridRow=2;vm.CommitUndoBatch();
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("Grid.Row='2'"),"Attached property patch not produced.");Navigate(vm);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"Attached property Undo lost source.");Navigate(vm,true);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText.Contains("RowDefinitions='Auto,2*,120'"),"GridLength units changed.");
            // Imported source currently exposes placement through its model/drag path;
            // do not invent an Inspector GridRow editor or a semantic unset adapter.
            var definitions=Source("NumericUpDown","Value='12.5'",out var original);
            var grid=definitions.Controls.Single(c=>c.Type=="Grid");definitions.BeginUndoBatch();grid.GridRowDefinitions="Auto,3*,140";definitions.CommitUndoBatch();
            RequireCapability(definitions.CreateActiveAxamlPatch().PatchedText.Contains("RowDefinitions='Auto,3*,140'"),"GridLength edit lost Auto/Star/Pixel.");
            Navigate(definitions);RequireCapability(definitions.CreateActiveAxamlPatch().PatchedText==original,"GridLength Undo did not restore exact source.");Navigate(definitions,true);
            RequireCapability(definitions.CreateActiveAxamlPatch().PatchedText.Contains("RowDefinitions='Auto,3*,140'"),"GridLength Redo failed.");
        });
        Check("SeparateSourceDocumentsAndStaleRows",()=>
        {
            var first=Source("NumericUpDown","Value='12.5'",out var a);var second=Source("NumericUpDown","Value='33'",out var b);
            var stale=GetPropertyGridRow(first,"Value");first.ResetPropertyGridProperty(stale);
            RequireCapability(!second.HasUndo&&second.CreateActiveAxamlPatch().PatchedText==b,"Histories mixed.");Navigate(first);
            RequireCapability(first.CreateActiveAxamlPatch().PatchedText==a,"First document not restored.");
            first.LoadAxamlImportedDocument(new AxamlImportService().Import(b,"second.axaml"),"second.axaml");stale.ResetToDefault();
            RequireCapability(!first.HasUndo&&!first.UndoEditorCommand!.CanExecute(null)&&first.CreateActiveAxamlPatch().PatchedText==b,"New source reused old history/row.");
        });
        Check("MultiFormJsonHistoryCommands",()=>
        {
            var vm=GridFirstVm();var c=vm.CreateControl("NumericUpDown",0,0);vm.SelectSingleControl(c);var first=vm.ActiveDocumentId;
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Value"),"12.5");var count=vm.ActiveSession.UndoSnapshots.Count;
            vm.CreateNewForm("Canvas");var second=vm.ActiveDocumentId;
            RequireCapability(!vm.HasUndo&&!vm.UndoEditorCommand!.CanExecute(null),"New form inherited command state.");
            vm.SetActiveForm(first,"3b3-test");RequireCapability(vm.ActiveSession.UndoSnapshots.Count==count&&vm.UndoEditorCommand!.CanExecute(null),"Form history command stale.");
            Navigate(vm);Navigate(vm,true);vm.SetActiveForm(second,"3b3-test");RequireCapability(!vm.HasUndo,"Second form history changed.");
            var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());RequireCapability(clone.Workspace.Project.Forms.Count==2,"JSON lost forms.");
        });
        Console.WriteLine($"Stability 3B3 checks: {pass}/{pass+fail}");return fail==0?0:1;
    }
}
