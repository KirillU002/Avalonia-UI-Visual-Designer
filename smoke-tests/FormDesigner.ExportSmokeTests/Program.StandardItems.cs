using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Collections;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void ConfigureToolboxStandardItems(MainWindowViewModel vm)
    {
        vm.Controls.Clear(); vm.BindingSources.Clear();
        var form = vm.ActiveFormDocument!.Id;
        foreach (var type in new[] { DesignerControlTypes.DropDownButton, DesignerControlTypes.SplitButton, DesignerControlTypes.ListBox })
        {
            var model = vm.TryCreateControlFromToolboxDrop(type, 30 + vm.Controls.Count * 240, 30, null, false, form)
                ?? throw new Exception("Toolbox drop rejected " + type);
            model.Name = type == DesignerControlTypes.DropDownButton ? "DropMenu" : type == DesignerControlTypes.SplitButton ? "ActionSplit" : "ChoiceList";
        }
        var list = vm.Controls.Single(c => c.Type == DesignerControlTypes.ListBox);
        StandardItemsProperties.Write(list, "Items", new[] { "Alpha", "Beta", "Gamma" });
        StandardItemsProperties.Write(list, "SelectedIndex", 1);
        var target = Control(DesignerControlTypes.TextBlock, "ResultLabel", 20, 270, 440, 40); target.Text = "initial"; vm.Controls.Add(target);
        var split = vm.Controls.Single(c => c.Type == DesignerControlTypes.SplitButton);
        vm.Interactions.Add(new() { SourceControlName = split.Name, EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionSetProperty, TargetControlName = target.Name, TargetProperty = "Text", TextTemplate = "primary action" });
        var menu = StandardItemsProperties.Menu(split); menu[0].Text = "Menu action"; vm.ApplyStandardItems(split, menu);
        vm.Interactions.Add(new() { SourceControlName = StandardItemsProperties.MenuSource(split, menu[0]).Name, EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionSetProperty, TargetControlName = target.Name, TargetProperty = "Text", TextTemplate = "menu action" });
    }

    private static void AssertToolboxStandardItems(SmokeContext context)
    {
        var vm = context.ViewModel;
        foreach (var type in new[] { DesignerControlTypes.DropDownButton, DesignerControlTypes.SplitButton, DesignerControlTypes.ListBox })
        {
            var model = vm.Controls.Single(c => c.Type == type);
            if (!vm.ToolboxItems.Any(i => i.Type == type)) throw new Exception("Control missing from Toolbox: " + type);
            vm.SelectControls(new[] { model }, model);
            foreach (var key in new[] { "Width", "Height", "FontFamily", "FontSize", "Background", "Foreground", "BorderThickness", "Padding", "IsEnabled", type == DesignerControlTypes.ListBox ? "Items" : StandardItemsProperties.MenuKey })
                if (!vm.PropertyGridCategories.SelectMany(c => c.Rows).Any(r => r.Key == key)) throw new Exception("Inspector property missing: " + type + "." + key);
            var font = vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "FontSize"); font.Value = "19"; font.CommitValue();
            var enabled = vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "IsEnabled"); enabled.BoolValue = false;
            if (StandardItemsControlFactory.Create(StandardItemsControlFactory.Snapshot(model)).IsEnabled) throw new Exception("Inspector IsEnabled must affect the native property.");
            enabled.BoolValue = true;
            var before = model.Width; model.X += 13; model.Width += 19;
            if (model.Width != before + 19 || vm.SelectedControl?.Id != model.Id) throw new Exception("Standard move/resize/selection lost the model.");
            var surface = CreateSmokeWindow(vm);
            typeof(MainWindow).GetMethod("RenderDesigner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, null);
            var create = typeof(MainWindow).GetMethod("CreateStandardItemsPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var native = (Avalonia.Controls.Control)create.Invoke(surface, new object[] { model })!;
            if (native.GetType().Name != type) throw new Exception("Canvas must use native Avalonia " + type);
        }
        var source = vm.Controls.Single(c => c.Type == DesignerControlTypes.SplitButton);
        var original = StandardItemsProperties.Menu(source);
        var stableId = original[0].Id;
        var edited = original.Select(i => i.Clone()).Reverse().ToList(); edited[0].Text = "Renamed item";
        vm.ApplyStandardItems(source, edited);
        if (!StandardItemsProperties.Menu(source).Any(i => i.Id == stableId)) throw new Exception("Reordering must preserve item IDs and Logic.");
        var editor = new StandardItemsEditorWindow(edited, true, _ => { });
        editor.AddItem(); editor.MoveSelected(-1); editor.RemoveSelected(); editor.Close();
        vm.ApplyStandardItems(source, original);
        var restored = CreateViewModel("StandardItemsReload"); restored.LoadDocumentJson(vm.ExportWorkspaceJson());
        if (restored.Controls.Count != vm.Controls.Count || StandardItemsProperties.Menu(restored.Controls.Single(c => c.Type == DesignerControlTypes.SplitButton)).Count != 2)
            throw new Exception("JSON reload lost standard controls or menu items.");
        var removedOwner = restored.Controls.Single(c => c.Type == DesignerControlTypes.SplitButton);
        var removedMenuNames = StandardItemsProperties.Menu(removedOwner).Select(item => StandardItemsProperties.MenuSource(removedOwner, item).Name).ToHashSet();
        restored.SelectControls(new[] { removedOwner }, removedOwner);
        restored.DeleteSelectedCommand.Execute(null);
        if (restored.Interactions.Any(i => removedMenuNames.Contains(i.SourceControlName))) throw new Exception("Deleting a menu owner must remove its menu Logic rules.");
        RequireContains(context.Xaml, "<DropDownButton", "DropDownButton AXAML missing.");
        RequireContains(context.Xaml, "<SplitButton.Flyout>", "Real Flyout AXAML missing.");
        RequireContains(context.Xaml, "<ListBox", "ListBox AXAML missing.");
        RequireContains(context.Xaml, "<x:String>Beta</x:String>", "Static ListBox values missing.");
        RequireStandardPreview(vm, axaml: false, bound: false);
        RequireStandardPreview(vm, axaml: true, bound: false);
        DotnetBuild(context.ProjectPath);
        RequireGeneratedStandardItems(context);
        RequireStandaloneStandardItemsProcess(context);
    }

    private static void RequireStandardPreview(MainWindowViewModel vm, bool axaml, bool bound)
    {
        vm.GenerateXaml();
        if (!bound) RequireContains(vm.GeneratedXaml, "FontSize=\"19\"", "Inspector font edits must reach exported AXAML.");
        var snapshot = vm.GenerateExportedAxamlPreviewSnapshot();
        var window = axaml ? new PreviewWindow(snapshot, vm.Registry) : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
        try
        {
            window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false; window.Show();
            NativeItemsUiWait(300);
            Pump((Task)typeof(PreviewWindow).GetMethod("PreloadBindingPreviewRowsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!);
            if (axaml)
            {
                Pump((Task)typeof(PreviewWindow).GetMethod("LoadExportedAxamlPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { snapshot })!);
                var status = window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text;
                if (status != "AXAML Preview ready") throw new Exception("Expected native AXAML preview, got " + status);
            }
            else typeof(PreviewWindow).GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            NativeItemsUiWait(80);
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
            var rows = (list.ItemsSource ?? list.Items).Cast<object>().ToArray();
            if (rows.Length != (bound ? 12 : 3)) throw new Exception("ListBox Preview lost actual items: " + rows.Length + "; axaml=" + axaml + "; names=" + string.Join("|", rows.Take(3).Select(r => StandardItemsControlFactory.Display(r, "Name"))) + "; top=" + snapshot.Document.BindingSources.FirstOrDefault()?.PreviewTopN);
            if (bound)
            {
                if (StandardItemsControlFactory.Display(rows[0], "Name") != "Real order 001") throw new Exception("Bound ListBox must display actual DLL values.");
                return;
            }
            list.SelectedIndex = 2;
            if (!Equals(list.SelectedItem, "Gamma")) throw new Exception("Native ListBox selection did not work.");
            var split = window.GetLogicalDescendants().OfType<SplitButton>().Single();
            var primary = split.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PART_PrimaryButton");
            if (primary is null) throw new Exception("SplitButton native primary template part missing.");
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); NativeItemsUiWait(100);
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "primary action")) throw new Exception("SplitButton primary Logic action failed; axaml=" + axaml + "; labels=" + string.Join("|", window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)));
            split = window.GetLogicalDescendants().OfType<SplitButton>().Single();
            var secondary = split.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_SecondaryButton");
            secondary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); NativeItemsUiWait(80);
            if (split.Flyout is not Flyout { IsOpen: true, Content: StackPanel stack }) throw new Exception("SplitButton arrow must open a real Flyout.");
            stack.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); NativeItemsUiWait(100);
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "menu action")) throw new Exception("Menu Logic must be independent of the primary action.");
            var drop = window.GetLogicalDescendants().OfType<DropDownButton>().Single();
            typeof(DropDownButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drop, null); NativeItemsUiWait(50);
            if (!drop.Flyout!.IsOpen) throw new Exception("DropDownButton activation did not open its Flyout.");
            drop.Flyout.Hide();
        }
        finally { window.Close(); }
    }

    private static void RequireGeneratedStandardItems(SmokeContext context)
    {
        var assembly = LoadGeneratedApplicationAssembly(Path.Combine(context.ProjectPath, "bin", "Debug", "net6.0", context.Scenario.Name + ".dll"));
        var window = (Window)Activator.CreateInstance(assembly.GetType(context.ViewModel.ExportProjectNamespace + ".MainWindow")!)!;
        try
        {
            window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false; window.Show(); Pump(Task.Delay(100));
            var split = window.FindControl<SplitButton>("ActionSplit")!;
            split.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_PrimaryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (window.FindControl<TextBlock>("ResultLabel")!.Text != "primary action") throw new Exception("Compiled primary action did not run.");
            ((StackPanel)((Flyout)split.Flyout!).Content!).Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (window.FindControl<TextBlock>("ResultLabel")!.Text != "menu action") throw new Exception("Compiled menu action did not run.");
            var list = window.FindControl<ListBox>("ChoiceList")!; list.SelectedIndex = 2;
            if (!Equals(list.SelectedItem, "Gamma")) throw new Exception("Compiled native ListBox selection failed.");
        }
        finally { window.Close(); }
    }

    private static void ConfigureToolboxListBinding(MainWindowViewModel vm)
    {
        ConfigureDllTablePreviewRealRows(vm); vm.Controls.Clear();
        var source = vm.BindingSources.Single();
        source.UseDemoData = source.AllowPreviewSampleFallback = true;
        // A real provider is available. ListBox must prefer it to global demo switches.
        var list = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.ListBox, 20, 20, null, false, vm.ActiveFormDocument!.Id)!;
        list.Name = "BoundList"; list.BindingSourceId = source.Id;
        StandardItemsProperties.Write(list, "ItemTemplate.DisplayField", "Name");
        vm.LoadPreviewRowsForBindingSourceAsync(source).GetAwaiter().GetResult();
    }
    private static void AssertToolboxListBinding(SmokeContext context)
    {
        RequireContains(context.Xaml, "ItemsSource=\"{Binding", "ListBox export lost its BindingSource.");
        RequireContains(context.Xaml, "ListBox.ItemTemplate", "Bound display field must use a real ItemTemplate.");
        RequireStandardPreview(context.ViewModel, false, true); RequireStandardPreview(context.ViewModel, true, true);
        RequireNotContains(context.CSharp, "Ada Lovelace", "BindingSource data must not be replaced with demo rows.");
        DotnetBuild(context.ProjectPath);
        var assembly = LoadGeneratedApplicationAssembly(Path.Combine(context.ProjectPath, "bin", "Debug", "net6.0", context.Scenario.Name + ".dll"));
        var window = (Window)Activator.CreateInstance(assembly.GetType(context.ViewModel.ExportProjectNamespace + ".MainWindow")!)!;
        try
        {
            var list = window.FindControl<ListBox>("BoundList")!;
            if (list.ItemsSource is not IList collection) throw new Exception("Compiled ListBox must bind its generated collection.");
            if (collection.Count != 0) throw new Exception("DLL export must not silently invent portable runtime data.");
            var row = Activator.CreateInstance(collection.GetType().GetGenericArguments()[0])!;
            row.GetType().GetProperty("Name")!.SetValue(row, "Runtime collection value"); collection.Add(row);
            list.SelectedIndex = 0;
            if (!ReferenceEquals(row, list.SelectedItem)) throw new Exception("Compiled ListBox must select actual objects from ItemsSource.");
        }
        finally { window.Close(); }
    }

    private static void ConfigureToolboxStandardMultiForm(MainWindowViewModel vm)
    {
        ConfigureToolboxStandardItems(vm); vm.CreateNewForm(DesignerLayoutModes.Absolute); ConfigureToolboxStandardItems(vm);
    }
    private static void AssertToolboxStandardMultiForm(SmokeContext context)
    {
        if (context.ViewModel.CurrentProject.Forms.Count != 2) throw new Exception("Expected two forms.");
        var windows = context.GeneratedFiles.Where(f => f.Path.EndsWith(".axaml")).ToArray();
        if (windows.Count(f => f.Content.Contains("<SplitButton")) != 2) throw new Exception("Each form must export its own controls and actions.");
    }
    private static void AssertToolboxStandardRoundTrip(SmokeContext context)
    {
        const string source = "<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Canvas><!--keep--><DropDownButton x:Name=\"Drop\" Width=\"180\" Content=\"Open\" Tag=\"unknown-preserve\"><DropDownButton.Flyout><Flyout><StackPanel><Button Content=\"One\"/></StackPanel></Flyout></DropDownButton.Flyout></DropDownButton><SplitButton x:Name=\"Split\" Content=\"{Binding Caption}\" Width=\"180\"><SplitButton.Flyout><MenuFlyout><MenuItem Header=\"Two\"/></MenuFlyout></SplitButton.Flyout></SplitButton><ListBox x:Name=\"List\" Width=\"240\" SelectedIndex=\"1\"><x:String>A</x:String><x:String>B</x:String></ListBox></Canvas></Window>";
        var (_, vm, surface) = Phase4Document(context, source);
        NativeItemsUiWait(180);
        if (!surface.Canvas.GetVisualDescendants().OfType<DropDownButton>().Any() || !surface.Canvas.GetVisualDescendants().OfType<SplitButton>().Any()) throw new Exception("Import must project native menu buttons.");
        Phase4Edit(vm, "Drop", "Width", "200"); Phase5Minimal(vm, source, "Width=\"180\" Content=\"Open\"", "Width=\"200\" Content=\"Open\"");
    }

    private static void NativeItemsUiWait(int milliseconds)
    {
        using var cancellation = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(cancellation.Token);
    }

    private static void ConfigureToolboxEmptyList(MainWindowViewModel vm)
    {
        vm.Controls.Clear(); vm.BindingSources.Clear();
        var path = CreateLinqToSqlPreviewRowsAssembly("SmokeEmptyListBox", "Smoke.Data.EmptyList", "EmptyRow", "dbo.EmptyTable", 0);
        vm.ImportBindingSourcesFromAssembly(path);
        var source = vm.BindingSources.Single(); source.UseDemoData = source.AllowPreviewSampleFallback = true;
        var list = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.ListBox, 20, 20, null, false, vm.ActiveFormDocument!.Id)!;
        list.BindingSourceId = source.Id;
    }
    private static void AssertToolboxEmptyList(SmokeContext context)
    {
        foreach (var axaml in new[] { false, true })
        {
            var vm = context.ViewModel; vm.GenerateXaml(); var snapshot = vm.GenerateExportedAxamlPreviewSnapshot();
            var window = axaml ? new PreviewWindow(snapshot, vm.Registry) : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
            try
            {
                Pump((Task)typeof(PreviewWindow).GetMethod("PreloadBindingPreviewRowsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!);
                if (axaml) Pump((Task)typeof(PreviewWindow).GetMethod("LoadExportedAxamlPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { snapshot })!);
                else typeof(PreviewWindow).GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
                if ((list.ItemsSource ?? list.Items).Cast<object>().Any()) throw new Exception("Empty real table must stay empty even when demo switches are enabled.");
            }
            finally { window.Close(); }
        }
    }
    private static void ConfigureToolboxPostgresList(MainWindowViewModel vm)
    {
        ConfigurePostgreSqlDataGridExport(vm); vm.Controls.Clear();
        var source = vm.BindingSources.Single(); source.SourceConnectionString = "Host=127.0.0.1;Port=1;Database=test;Username=test;Timeout=1";
        source.UseDemoData = source.AllowPreviewSampleFallback = true;
        var list = vm.TryCreateControlFromToolboxDrop(DesignerControlTypes.ListBox, 20, 20, null, false, vm.ActiveFormDocument!.Id)!;
        list.Name = "PostgresList"; list.BindingSourceId = source.Id;
    }
    private static void AssertToolboxPostgresList(SmokeContext context)
    {
        RequireContains(context.CSharp, "FromPostgreSqlAsync", "ListBox must reuse the existing provider loader.");
        RequireContains(context.Xaml, "ItemsSource=\"{Binding pg_customersView}", "PostgreSQL collection binding is missing.");
        RequireNotContains(context.CSharp, PostgresSecretMarker, "New controls must preserve the existing secret policy.");
        if (!context.ViewModel.RequiredPackages.Any(p => p.Id == "Npgsql")) throw new Exception("Used PostgreSQL provider dependency missing.");
        foreach (var axaml in new[] { false, true })
        {
            var vm = context.ViewModel; var snapshot = vm.GenerateExportedAxamlPreviewSnapshot();
            var window = axaml ? new PreviewWindow(snapshot, vm.Registry) : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
            try
            {
                Pump((Task)typeof(PreviewWindow).GetMethod("PreloadBindingPreviewRowsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!);
                if (axaml) Pump((Task)typeof(PreviewWindow).GetMethod("LoadExportedAxamlPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { snapshot })!);
                else typeof(PreviewWindow).GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
                if ((list.ItemsSource ?? list.Items).Cast<object>().Any()) throw new Exception("Connection failure must not produce sample ListBox items.");
                if (!window.FindControl<Border>("DatabaseErrorBanner")!.IsVisible) throw new Exception("ListBox connection error must be visible.");
            }
            finally { window.Close(); }
        }
        RequireGeneratedListFailure(context);
    }

    private static void RequireGeneratedListFailure(SmokeContext context)
    {
        DotnetBuild(context.ProjectPath);
        var assembly = LoadGeneratedApplicationAssembly(Path.Combine(context.ProjectPath, "bin", "Debug", "net6.0", context.Scenario.Name + ".dll"));
        var window = (Window)Activator.CreateInstance(assembly.GetType(context.ViewModel.ExportProjectNamespace + ".MainWindow")!)!;
        const string key = "FORMDESIGNER_POSTGRES_PG_CUSTOMERS";
        var old = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "Host=127.0.0.1;Port=1;Database=test;Username=test;Timeout=1");
            var vm = window.DataContext!;
            Pump((Task)vm.GetType().GetMethods().Single(m => m.Name.EndsWith("FromPostgreSqlAsync")).Invoke(vm, new object[] { CancellationToken.None })!);
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
            if (list.ItemsSource is not IEnumerable rows || rows.Cast<object>().Any()) throw new Exception("Compiled failed source must leave a bound empty ListBox.");
            RequireContains((string)vm.GetType().GetProperty("PostgreSqlStatus")!.GetValue(vm)!, "PostgreSQL", "Runtime source error must be visible.");
        }
        finally { Environment.SetEnvironmentVariable(key, old); window.Close(); }
    }
}
