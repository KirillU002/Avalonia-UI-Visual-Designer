using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static readonly string[] TemplateOwners = { "ListBox", "ComboBox", "ItemsControl" };

    private static MainWindowViewModel TemplateScene(string layout)
    {
        var vm = GridFirstVm();
        if (layout == "Canvas") { vm.NewFormLayoutChoice = MainWindowViewModel.NewFormCanvasChoice; vm.NewProjectEditorCommand!.Execute(null); }
        var appearance = vm.AppearanceDraft();
        appearance.Resources.Add(new("TemplateBrush","SolidColorBrush","#2468AC","Light"));
        appearance.Resources.Add(new("TemplateBrush","SolidColorBrush","#AC6824","Dark"));
        appearance.Styles.Add(new("TextBlock","TextBlock#TemplateLabel",new(){new("Opacity","0.76")}));
        appearance.Resources.Add(new("RowSize","Double","17"));
        vm.SaveAppearance(vm.ActiveDocumentId,appearance);
        foreach (var type in TemplateOwners)
        {
            var owner = vm.CreateControl(type,0,100 * vm.Controls.Count);
            owner.Name = "Rows" + type;
            var data = new CollectionItemsContract();
            foreach (var label in new[] { type + " A", type + " B" })
                data.Items.Add(new() { Kind = CollectionItemsContract.ItemKind(type,"Items"), Text = label });
            var storage = CollectionItemsContract.StorageKey("Items");
            var previous = owner.CustomProperties.FirstOrDefault(p => p.Key == storage);
            if (previous != null) owner.CustomProperties.Remove(previous);
            owner.CustomProperties.Add(new() { Key = storage, ValueJson = JsonSerializer.Serialize(data) });
            var draft = vm.OpenItemTemplate(owner);
            try
            {
                var stack = draft.CreateControl("StackPanel",0,0);
                var label = draft.CreateControl("TextBlock",0,0,stack.Id);
                label.Name = "TemplateLabel";
                draft.SelectSingleControl(label);
                Commit6A(draft,PropertyValueKind.Binding,"{Binding .}");
                Commit6A(draft,PropertyValueKind.Unset,"","Opacity");
                Commit6A(draft,PropertyValueKind.StaticResource,"{StaticResource RowSize}","FontSize");
                Commit6A(draft,PropertyValueKind.DynamicResource,"{DynamicResource TemplateBrush}","Foreground");
                vm.SaveItemTemplate(vm.ActiveDocumentId,owner,draft);
            }
            finally { draft.CloseItemTemplate(); }
        }
        return vm;
    }

    public sealed record TemplateRuntimeRow(string Title);

    private static int TemplateProjects(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var layout in new[]{"Grid","Canvas"})
        {
            var vm = TemplateScene(layout);
            var bound = vm.CreateControl("ItemsControl",240,180);
            bound.Name = "BoundOrders";
            var draft = vm.OpenItemTemplate(bound);
            try
            {
                var text = draft.CreateControl("TextBlock",0,0);
                text.Name = "BoundTitle";
                draft.SelectSingleControl(text);
                Commit6A(draft,PropertyValueKind.Binding,"{Binding Title}");
                var named = draft.CreateControl("TextBlock",0,0);
                named.Name="BoundEcho";
                draft.SelectSingleControl(named);
                Commit6A(draft,PropertyValueKind.Binding,"{Binding Text, ElementName=BoundTitle}");
                var self=draft.CreateControl("TextBlock",0,0);
                self.Name="SelfEcho";
                draft.SelectSingleControl(self);
                RequireCapability(PropertyExpressions.TryBinding(new("Name",RelativeSource:"Self"),out var expression,out var reason),reason);
                Commit6A(draft,PropertyValueKind.Binding,expression);
                var group=draft.CreateControl("DockPanel",0,0);
                var wrap=draft.CreateControl("WrapPanel",0,0,group.Id);
                var border=draft.CreateControl("Border",0,0,wrap.Id);
                var grid=draft.CreateControl("Grid",0,0,border.Id);
                var stack=draft.CreateControl("StackPanel",0,0,grid.Id);
                draft.CreateControl("Button",0,0,stack.Id).Name="RowButton";
                draft.CreateControl("CheckBox",0,0,stack.Id).Name="RowCheck";
                var image=draft.CreateControl("Image",0,0,stack.Id);
                image.Name="RowImage";
                draft.SelectSingleControl(image);
                var rows = draft.PropertyGridCategories.SelectMany(c=>c.Rows).Where(r=>r.PropertyMetadata?.Native.Property==Image.SourceProperty && r.CanEditExpression).ToArray();
                if (rows.Length!=1) throw new InvalidOperationException("Image.Source expression targets="+rows.Length+"; "+
                    string.Join(",",draft.PropertyGridCategories.SelectMany(c=>c.Rows).Where(r=>r.Key.Contains("Source")).Select(r=>r.Key+"/"+r.PropertyMetadata?.Native.Key+"/"+r.CanEditExpression)));
                var sourceRow=rows[0];
                RequireCapability(draft.CommitPropertyExpression(sourceRow,new(PropertyValueKind.Binding,"{Binding Thumbnail}"),out var error),error);
                vm.SaveItemTemplate(vm.ActiveDocumentId,bound,draft,"Orders");
            }
            finally { draft.CloseItemTemplate(); }
            var file = Path.Combine(folder,layout+".json");
            File.WriteAllText(file,vm.ExportWorkspaceJson());
            var reopened = GridFirstVm();
            try
            {
                reopened.LoadDocumentJson(File.ReadAllText(file));
                Pump(reopened.ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));
            }
            finally { Dispose5AProfileVm(reopened); Dispose5AProfileVm(vm); }
        }
        return 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] CloseTemplateDraft(MainWindowViewModel vm, DesignControlModel owner)
    {
        var draft = vm.OpenItemTemplate(owner);
        var child = draft.CreateControl("TextBlock",0,0);
        var refs = new[] { new WeakReference(draft),new WeakReference(child) };
        draft.CloseItemTemplate();
        return refs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] CloseTemplatePreview(MainWindowViewModel form,bool axaml)
    {
        using var preview = ShowDataGridRegressionPreview(form,axaml);
        var items = preview.Window.GetVisualDescendants().OfType<ItemsControl>().First(i=>i is ListBox && i.ItemTemplate!=null);
        var content = items.GetVisualDescendants().OfType<TextBlock>().First(t=>t.Name=="TemplateLabel");
        return new[]{new WeakReference(preview.Window),new WeakReference(items),new WeakReference(content)};
    }

    private static int Run6CChecks()
    {
        var passed = 0;
        var failed = 0;
        void Check(string name,Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch(Exception ex) { failed++; Console.WriteLine("FAIL " + name + " " + ex); }
        }
        Check("JSON, authoring, binding and exported template scope",() =>
        {
            var form = TemplateScene("Grid");
            var reopened = GridFirstVm();
            try
            {
                reopened.LoadDocumentJson(form.ExportWorkspaceJson());
                reopened.GenerateXaml();
                var xml = XElement.Parse(reopened.GeneratedXaml);
                var av = xml.Name.Namespace;
                foreach (var type in TemplateOwners)
                {
                    var owner = reopened.Controls.Single(c => c.Type == type);
                    var model = owner.ItemTemplate!;
                    RequireCapability(model.Controls.Count == 2 && model.Controls[1].ParentId == model.Controls[0].Id,"Template hierarchy");
                    var node = xml.Descendants(av+type).Single().Element(av+(type+".ItemTemplate"))!.Element(av+"DataTemplate")!;
                    RequireCapability(node.Descendants(av+"TextBlock").Single().Attribute("Text")!.Value == "{Binding .}", "Item binding");
                }
                foreach (var mode in new[] { false,true })
                    using (var preview = ShowDataGridRegressionPreview(reopened,mode))
                    {
                        var items = preview.Window.GetVisualDescendants().OfType<ItemsControl>().Where(i => TemplateOwners.Contains(i.GetType().Name) && i.ItemTemplate != null).ToArray();
                        foreach (var type in TemplateOwners)
                        {
                            var control = items.Single(i => i.GetType().Name == type && i.Items.Count == 2);
                            RequireCapability(control.Items[0] as string == type+" A", "Data item lost in "+type+" preview="+mode);
                            var visual = control.ItemTemplate!.Build(control.Items[0])!;
                            visual.DataContext = control.Items[0];
                            var label = visual.GetVisualDescendants().OfType<TextBlock>().Single();
                            RequireCapability(label.Text == type+" A" && label.FontSize == 17, "Item context in "+type+" preview="+mode);
                        }
                    }
            }
            finally { Dispose5AProfileVm(form); Dispose5AProfileVm(reopened); }
        });
        Check("isolated draft, apply, delete, Undo and Redo",() =>
        {
            var vm = GridFirstVm();
            var owner = vm.CreateControl("ItemsControl",0,0);
            var draft = vm.OpenItemTemplate(owner);
            try
            {
                foreach (var type in new[]{"TextBlock","Image","Border","StackPanel","Grid","DockPanel","WrapPanel","Button","CheckBox"})
                    draft.CreateControl(type,0,0);
                RequireCapability(owner.ItemTemplate == null && vm.SelectedControl == owner, "Draft touched form");
                vm.SaveItemTemplate(vm.ActiveDocumentId,owner,draft);
                vm.SaveItemTemplate(vm.ActiveDocumentId,owner,null);
                GridEditorUndo(vm);
                RequireCapability(vm.Controls.Single().ItemTemplate?.Controls.Count == 9,"Delete undo");
                vm.RedoCommand.Execute(null);
                RequireCapability(vm.Controls.Single().ItemTemplate == null,"Delete redo");
            }
            finally { draft.CloseItemTemplate(); Dispose5AProfileVm(vm); }
        });
        Check("draft child reorder, remove, Undo and Redo",() =>
        {
            var vm=GridFirstVm();
            var owner=vm.CreateControl("ListBox",0,0);
            var draft=vm.OpenItemTemplate(owner);
            try
            {
                var stack=draft.CreateControl("StackPanel",0,0);
                var first=draft.CreateControl("TextBlock",0,0,stack.Id);
                var second=draft.CreateControl("TextBlock",0,0,stack.Id);
                draft.SelectSingleControl(second);
                draft.MoveSelectedStackChildUpCommand.Execute(null);
                RequireCapability(second.StackOrder<first.StackOrder,"Reorder lost");
                GridEditorUndo(draft);
                var restored=draft.Controls.Where(c=>c.ParentId==stack.Id).OrderBy(c=>c.StackOrder).Select(c=>c.Id).ToArray();
                RequireCapability(restored.SequenceEqual(new[]{first.Id,second.Id}),"Reorder Undo did not restore sibling order");
                GridEditorUndo(draft,true);
                draft.SelectSingleControl(draft.Controls.Single(c=>c.Id==first.Id));
                draft.DeleteSelectedCommand.Execute(null);
                RequireCapability(draft.Controls.All(c=>c.Id!=first.Id),"Child not deleted");
                GridEditorUndo(draft);
                RequireCapability(draft.Controls.Any(c=>c.Id==first.Id && c.ParentId==stack.Id),"Subtree Undo");
                RequireCapability(owner.ItemTemplate==null,"Draft history affected owner");
            }
            finally {draft.CloseItemTemplate();Dispose5AProfileVm(vm);}
        });
        Check("source-preserving item binding and opaque templates",() =>
        {
            var body = "<a:ListBox Name='Rows'><a:ListBox.ItemTemplate><a:DataTemplate><!--keep--><a:StackPanel><a:TextBlock Text='{Binding Title}' Tag='unknown'/></a:StackPanel></a:DataTemplate></a:ListBox.ItemTemplate></a:ListBox>";
            var vm = Universal4AVm(body);
            var original = vm.ActiveAxamlSourceDocument!.OriginalText;
            var field = vm.StyleFields.Single(f => f.TemplateOwner == "Rows");
            vm.SaveSourceStyleValue(vm.ActiveDocumentId,field,"{Binding Caption}");
            var patch = vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply && patch.PatchedText == original.Replace("{Binding Title}","{Binding Caption}"),"Template scalar patch");
            RequireCapability(patch.Edits.Count == 1 && !vm.CreateActiveAxamlPatch(original+"<!--external-->").CanApply,"Source conflict or patch granularity");
            vm.MarkAxamlRoundTripSaved("template.axaml",patch.PatchedText);
            GridEditorUndo(vm);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == original,"Byte-exact template Undo after ACK");
            GridEditorUndo(vm,true);
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText == patch.PatchedText,"Byte-exact template Redo after ACK");
            Dispose5AProfileVm(vm);
        });
        Check("shared surface, inspector, selection and accepted draft",() =>
        {
            var vm = GridFirstVm();
            var owner = vm.CreateControl("ListBox",0,0);
            var window = CreateSmokeWindow(vm);
            try
            {
                window.Show();
                vm.CloseStartScreenCommand.Execute(null);
                vm.SelectSingleControl(owner);
                var surface = window.FindControl<FormDesigner.Views.DesignerSurface>("DesignerSurface")!;
                var openButton = window.FindControl<FormDesigner.Views.DesignerPropertyInspector>("DesignerPropertyInspector")!
                    .GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "ItemTemplate");
                openButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var panel = window.FindControl<Grid>("TemplateWorkspace")!;
                RequireCapability(panel.IsVisible && panel.DataContext is MainWindowViewModel && !ReferenceEquals(vm,panel.DataContext), "Template mode not entered: "+vm.StatusText);
                var draft = (MainWindowViewModel)panel.DataContext!;
                RequireCapability(ReferenceEquals(surface.Parent, window.FindControl<Border>("TemplateSurfaceSlot")), "Surface not reused");
                var nested = draft.CreateControl("Grid",0,0);
                var label = draft.CreateControl("TextBlock",0,0,nested.Id);
                draft.SelectSingleControl(label);
                Commit6A(draft,PropertyValueKind.Binding,"{Binding Title}");
                panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Применить")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                RequireCapability(!panel.IsVisible && owner.ItemTemplate?.Controls.Count == 2 && vm.SelectedControl == owner,
                    "Template apply or selection restoration: "+window.FindControl<TextBlock>("TemplateMessage")!.Text);
            }
            finally { window.CloseForExternalHost(); Dispose5AProfileVm(vm); }
        });
        Check("closed AXAML and Legacy template windows release materialized rows",() =>
        {
            var vm=TemplateScene("Grid");
            try
            {
                var refs=CloseTemplatePreview(vm,false).Concat(CloseTemplatePreview(vm,true)).ToArray();
                NativeItemsUiWait(1000);
                ForceFullGc();
                RequireCapability(refs.All(r=>!r.IsAlive),"Retained window or realized template objects: "+refs.Count(r=>r.IsAlive));
            }
            finally { Dispose5AProfileVm(vm); }
        });
        Check("discarded editor sessions and nested controls are collectable",() =>
        {
            var vm=GridFirstVm();
            try
            {
                var owner=vm.CreateControl("ListBox",0,0);
                var references=Enumerable.Range(0,3).SelectMany(_=>CloseTemplateDraft(vm,owner)).ToArray();
                NativeItemsUiWait(1000);
                ForceFullGc();
                RequireCapability(references.All(r=>!r.IsAlive),"Retained template drafts or children: "+references.Count(r=>r.IsAlive));
            }
            finally { Dispose5AProfileVm(vm); }
        });
        Check("multi-form stale draft rejected",() =>
        {
            var vm = GridFirstVm();
            var owner = vm.CreateControl("ComboBox",0,0);
            var original = vm.ActiveDocumentId;
            var draft = vm.OpenItemTemplate(owner);
            try
            {
                draft.CreateControl("TextBlock",0,0);
                vm.CreateNewForm();
                bool refused = false;
                try { vm.SaveItemTemplate(original,owner,draft); } catch (InvalidOperationException) { refused = true; }
                RequireCapability(refused && vm.Controls.Count == 0 && owner.ItemTemplate == null,"Cross-form write");
            }
            finally { draft.CloseItemTemplate(); Dispose5AProfileVm(vm); }
        });
        Check("imported safe native template and actual row path",() =>
        {
            var vm = Universal4AVm("<a:ItemsControl Name='Rows'><a:ItemsControl.ItemTemplate><a:DataTemplate><a:StackPanel><a:TextBlock Text='{Binding Title}'/></a:StackPanel></a:DataTemplate></a:ItemsControl.ItemTemplate></a:ItemsControl>");
            var tree = FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlLayoutProjection.CreateNativeTree(vm.ActiveAxamlSourceDocument!,vm.Controls);
            var items = tree.Controls.Values.OfType<ItemsControl>().Single();
            var row = new TemplateRuntimeRow("From current item");
            var view = items.ItemTemplate!.Build(row)!;
            view.DataContext = row;
            RequireCapability(view.GetVisualDescendants().OfType<TextBlock>().Single().Text == row.Title,"Imported template binding");
            Dispose5AProfileVm(vm);
        });
        Check("compiled and custom templates retain source",() =>
        {
            var vm = Universal4AVm("<a:ListBox Name='Rows'><a:ListBox.ItemTemplate><a:DataTemplate x:DataType='vm:Row'><a:TextBlock Text='{CompiledBinding Title}'/></a:DataTemplate></a:ListBox.ItemTemplate></a:ListBox>");
            RequireCapability(!vm.StyleFields.Any(f=>f.TemplateOwner=="Rows") && vm.CreateActiveAxamlPatch().PatchedText == vm.ActiveAxamlSourceDocument!.OriginalText,"Opaque template changed");
            Dispose5AProfileVm(vm);
        });
        Check("owner ItemsSource and item binding use different contexts",() =>
        {
            var vm = GridFirstVm();
            var owner = vm.CreateControl("ItemsControl",0,0);
            var draft = vm.OpenItemTemplate(owner);
            try
            {
                var label = draft.CreateControl("TextBlock",0,0);
                draft.SelectSingleControl(label);
                Commit6A(draft,PropertyValueKind.Binding,"{Binding Title}");
                vm.SaveItemTemplate(vm.ActiveDocumentId,owner,draft,"Orders");
                using var preview = ShowDataGridRegressionPreview(vm,false);
                var native = preview.Window.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.GetType()==typeof(ItemsControl) && i.ItemTemplate!=null);
                native.DataContext = new { Orders = new[] { new TemplateRuntimeRow("row") } };
                Dispatcher.UIThread.RunJobs();
                RequireCapability(native.Items.Count == 1 && native.Items[0] is TemplateRuntimeRow,"Owner collection path unresolved");
                var subtree = native.ItemTemplate!.Build(native.Items[0])!;
                subtree.DataContext = native.Items[0];
                RequireCapability(subtree.GetVisualDescendants().OfType<TextBlock>().Single().Text == "row","Row path unresolved");
            }
            finally { draft.CloseItemTemplate(); Dispose5AProfileVm(vm); }
        });
        Console.WriteLine($"Phase 6C: {passed}/{passed + failed}");
        return failed == 0 ? 0 : 1;
    }
}
