using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal4BChecks()
    {
        var pass = 0; var fail = 0;
        void Check(string name, Action action)
        {
            var filter=Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter,StringComparison.OrdinalIgnoreCase)) return;
            try { action(); pass++; Console.WriteLine("PASS UNIVERSAL4B_"+name); }
            catch(Exception error) { fail++; Console.WriteLine("FAIL UNIVERSAL4B_"+name+": "+error); }
            finally { foreach(var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
        }
        foreach (var fixture in new[] { "Login", "Nested" }) Check("Geometry_"+fixture, () =>
        {
            var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(),"Samples","RoundTrip","Phase4B",fixture+".axaml"));
            var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(new AxamlImportService().Import(text), fixture+".axaml");
            var original = (Window)RuntimeAxamlPreviewLoader.Load(text);
            var tree = AxamlLayoutProjection.CreateNativeTree(vm.ActiveAxamlSourceDocument!,vm.Controls);
            var projected = new Window { Content=tree.Root, SystemDecorations=SystemDecorations.None, ShowInTaskbar=false, Position=new PixelPoint(-3000,-3000) };
            try
            {
                original.SystemDecorations=SystemDecorations.None; original.ShowInTaskbar=false; original.Position=new PixelPoint(-4000,-4000);
                original.Width=projected.Width=800; original.Height=projected.Height=600;
                original.Show(); projected.Show(); NativeItemsUiWait(80);original.UpdateLayout();projected.UpdateLayout();
                foreach(var size in new[]{new Size(800,600),new Size(1200,800),new Size(1600,900)})
                {
                original.Width=projected.Width=size.Width;original.Height=projected.Height=size.Height;
                NativeItemsUiWait(25);original.UpdateLayout();projected.UpdateLayout();
                RequireCapability(original.ClientSize==projected.ClientSize,"Client area differs.");
                foreach(var model in vm.Controls)
                {
                    var sourceRef=vm.ActiveAxamlSourceDocument!.SourceMap.ByControlId[model.Id];
                    var name=sourceRef.Element.GetAttributeValue("Name");if(name is null)continue;
                    var expected=original.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==name);
                    var actual=tree.Controls[model.Id];
                    RequireCapability(actual.GetType()==expected.GetType()&&actual.GetVisualRoot()==projected,"Native type/attachment lost: "+name);
                    var a=actual.TranslatePoint(default,(Control)projected.Content!)!.Value;var e=expected.TranslatePoint(default,(Control)original.Content!)!.Value;
                    RequireCapability(Math.Abs(a.X-e.X)<=1&&Math.Abs(a.Y-e.Y)<=1&&Math.Abs(actual.Bounds.Width-expected.Bounds.Width)<=1&&Math.Abs(actual.Bounds.Height-expected.Bounds.Height)<=1,
                        $"Geometry {fixture}/{name}: actual {a} {actual.Bounds}; expected {e} {expected.Bounds}");
                    RequireCapability(actual.Margin==expected.Margin&&actual.HorizontalAlignment==expected.HorizontalAlignment&&actual.VerticalAlignment==expected.VerticalAlignment,"Placement properties differ: "+name);
                }
                }
                RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Geometry changed source.");
            }
            finally {projected.Close();original.Close();}
        });
        foreach(var mode in new[]{false,true}) Check("PreviewNativeScalars_"+mode,()=>
        {
            var vm=Universal4AVm("<a:Button x:Name='Target' FontFamily='Arial' FontStyle='Italic' Opacity='0.6' Background='#123456' Foreground='#abcdef' BorderBrush='#fedcba' IsEnabled='False'><a:Button.Padding>1,2,3,4</a:Button.Padding></a:Button>");
            using var preview=ShowDataGridRegressionPreview(vm,mode);
            var host=(Control)preview.Window.FindControl<Border>("PreviewSurfaceBorder")!.Child!;
            var actual=host.GetVisualDescendants().Prepend(host).OfType<Button>().Single();
            RequireCapability(actual.Name=="Target"&&actual.Padding==new Thickness(1,2,3,4)&&actual.FontFamily.Name=="Arial"&&actual.FontStyle==Avalonia.Media.FontStyle.Italic&&actual.Opacity==.6,"Imported Preview lost source scalar/name.");
            RequireCapability(!actual.IsEnabled&&actual.Background is Avalonia.Media.ISolidColorBrush bg&&bg.Color==Avalonia.Media.Color.Parse("#123456")
                &&actual.Foreground is Avalonia.Media.ISolidColorBrush fg&&fg.Color==Avalonia.Media.Color.Parse("#abcdef")
                &&actual.BorderBrush is Avalonia.Media.ISolidColorBrush border&&border.Color==Avalonia.Media.Color.Parse("#fedcba"),"Native source brush/behavior lost.");
            RequireCapability(!preview.Window.BuildIssues.Any(d=>d.Message.Contains("не гарантирует перенос")),"Resolved scalar still reported unsupported.");
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Preview changed source.");
        });
        foreach(var mode in new[]{false,true})Check("PendingCanvasInsertion_"+mode,()=>
        {
            var vm=Universal4AVm("<a:Canvas><a:Button Name='Existing' Content='Existing'/></a:Canvas>");
            var originalBuffer=vm.ActiveAxamlSourceDocument!.OriginalText;
            var added=vm.CreateControl("Button",23,37);added.Text="Pending";added.Background="#123456";added.IsVisible=false;added.Opacity=.6;
            RequireCapability(vm.ActiveAxamlSourceDocument!.SourceMap.TryGet(added.Id,out _)&&vm.CreateActiveAxamlPatch(originalBuffer).HasChanges,"Working source map or pending insertion lost.");
            using var preview=ShowDataGridRegressionPreview(vm,mode);
            var native=preview.Window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Pending");
            RequireCapability(native.Background is Avalonia.Media.ISolidColorBrush brush&&brush.Color==Avalonia.Media.Color.Parse("#123456")
                &&!native.IsVisible&&native.Opacity==.6,"Pending source insertion lost model appearance/state.");
            RequireCapability(vm.CreateActiveAxamlPatch().CanApply&&vm.CreateActiveAxamlPatch().HasChanges,"Preview damaged pending insertion patch.");
        });
        Check("OpaqueTemplatesBindingsResources",()=>
        {
            var vm=Universal4AVm("<a:Button x:Name='Target' Content='{Binding UserName}' Background='{DynamicResource Accent}'><a:Button.Template><a:ControlTemplate><a:TextBlock Text='{Binding Dangerous}'/></a:ControlTemplate></a:Button.Template></a:Button>");
            using var preview=ShowDataGridRegressionPreview(vm,true);
            RequireCapability(preview.Window.BuildIssues.Any(d=>d.Message.Contains("Bindings/сложные объекты")),"Opaque values have no explanation.");
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Opaque source changed.");
        });
        Check("DataGridOpaqueIsExplicit",()=>
        {
            var vm=Universal4AVm("<a:Grid><a:DataGrid Name='Data'/><a:Button Name='Safe' Content='Visible'/></a:Grid>");
            using var preview=ShowDataGridRegressionPreview(vm,false);
            RequireCapability(preview.Window.BuildIssues.Any(d=>d.Message.Contains("opaque visual nodes 1")),"Unsupported DataGrid silently omitted.");
            RequireCapability(preview.Window.GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()=="Visible"),"Opaque sibling hid standard Button.");
        });
        Check("ImportedThemeAndRootScopeDiagnostics",()=>
        {
            var text="<Window xmlns='https://github.com/avaloniaui' RequestedThemeVariant='Dark' FontSize='24' Background='{DynamicResource Missing}'><Button Name='Target'/></Window>";
            var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(text),"theme.axaml");
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))
                RequireCapability(preview.Window.RequestedThemeVariant==Avalonia.Styling.ThemeVariant.Dark&&preview.Window.BuildIssues.Any(d=>d.Message.Contains("Корневые")&&d.Message.Contains("FontSize")&&d.Message.Contains("Background")),"Root theme/scope capability is misleading.");
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Root scope warning changed source.");
        });
        Check("SourceMapStableView",()=>
        {
            var source=new AxamlImportService().Import(Universal4ASource(100));var map=source.RoundTripDocument.SourceMap;
            RequireCapability(ReferenceEquals(map.Controls,map.Controls)&&map.Controls.Count==source.Document.Controls.Count,"SourceMap rebuilt its control list.");
            RequireCapability(map.Controls.All(r=>map.ByControlId[r.ControlId]==r&&r.Element.ElementSpan.End<=source.RoundTripDocument.OriginalText.Length),"Index/span integrity lost.");
            RequireCapability(source.RoundTripDocument.CapabilityReport.Structure!.Format().Contains("path="),"Lazy report lost detailed lines.");
        });
        Check("LazyAndVerboseReportEquivalent",()=>
        {
            var text=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"Views","MainWindow.axaml"));
            var normal=new AxamlImportService().Import(text);var detailed=new AxamlImportService().Import(text,detailedDiagnostics:true);
            RequireCapability(normal.CapabilityReport.Structure!.Format()==detailed.CapabilityReport.Structure!.Format(),"Lazy diagnostics changed report content.");
            RequireCapability(detailed.Diagnostics.Count>normal.Diagnostics.Count,"Verbose discovery events missing.");
        });
        Check("PreviewSnapshotUnaffectedByAck",()=>
        {
            var vm=Universal4AVm("<a:Button Name='Target' Content='Old' Padding='1,2,3,4'/>");
            using var preview=ShowDataGridRegressionPreview(vm,true);vm.SelectSingleControl(vm.Controls.Single());
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Padding"),"4,3,2,1");var patch=vm.CreateActiveAxamlPatch();
            vm.MarkAxamlRoundTripSaved("phase4b.axaml",patch.PatchedText);
            var native=preview.Window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Old");
            RequireCapability(native.Padding==new Thickness(1,2,3,4)&&!vm.CreateActiveAxamlPatch().HasChanges,"ACK mutated an open Preview snapshot.");
        });
        Check("ClosedProjectAndPreviewMetadataReleased",()=>
        {
            var sources=new List<(Window ClosedPreview,WeakReference Map)>();
            foreach(var mode in new[]{false,true})for(var i=0;i<20;i++)sources.Add(ClosedImported4BPreview(mode));
            NativeItemsUiWait(100);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            RequireCapability(sources.All(w=>!w.Map.IsAlive),"Closed Preview retained a closed document SourceMap.");
            GC.KeepAlive(sources); // Retain closed windows deliberately: do not rely on Window/UIA collectibility.
        });
        foreach(var mode in new[]{false,true})Check("ImportedCollectionsPreview_"+mode,()=>
        {
            var vm=Universal4AVm("<a:Grid RowDefinitions='Auto,Auto,*,*' ColumnDefinitions='*,*'>"+
                "<a:Menu Name='MainMenu'><a:MenuItem Header='File'><a:MenuItem Header='Open'/><a:Separator/></a:MenuItem></a:Menu>"+
                "<a:ComboBox Name='Choices' Grid.Row='1' SelectedIndex='0'><a:ComboBoxItem Content='A'/><a:ComboBoxItem Content='B'/></a:ComboBox>"+
                "<a:ListBox Name='List' Grid.Row='1' Grid.Column='1'><a:ListBoxItem Content='L1'/><a:ListBoxItem Content='L2'/></a:ListBox>"+
                "<a:TabControl Name='Pages' Grid.Row='2'><a:TabItem Header='One'><a:TextBlock Text='T1'/></a:TabItem><a:TabItem Header='Two'><a:TextBlock Text='T2'/></a:TabItem></a:TabControl>"+
                "<a:TreeView Name='Nodes' Grid.Row='3'><a:TreeViewItem Header='Branch'><a:TreeViewItem Header='Leaf'/></a:TreeViewItem></a:TreeView></a:Grid>");
            using var preview=ShowDataGridRegressionPreview(vm,mode);
            T Native<T>(string name)where T:Control=>preview.Window.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
            var menu=Native<Menu>("MainMenu");var entry=(MenuItem)menu.Items[0]!;
            var combo=Native<ComboBox>("Choices");
            try
            {
                entry.IsSubMenuOpen=true;NativeItemsUiWait(30);
                RequireCapability(entry.IsSubMenuOpen&&entry.Items[0]is MenuItem{Header:"Open"}&&entry.Items[1]is Separator,"Menu hierarchy lost.");
                entry.IsSubMenuOpen=false;
                combo.IsDropDownOpen=true;NativeItemsUiWait(30);combo.SelectedIndex=1;
                RequireCapability(combo.SelectedItem is ComboBoxItem{Content:"B"},"ComboBox selection failed.");combo.IsDropDownOpen=false;
                var list=Native<ListBox>("List");list.SelectedIndex=1;RequireCapability(list.SelectedItem is ListBoxItem{Content:"L2"},"ListBox selection failed.");
                var tabs=Native<TabControl>("Pages");tabs.SelectedIndex=1;NativeItemsUiWait(30);RequireCapability(tabs.SelectedContent is TextBlock{Text:"T2"},"Tab content failed.");
                var branch=(TreeViewItem)Native<TreeView>("Nodes").Items[0]!;branch.IsExpanded=true;NativeItemsUiWait(30);
                RequireCapability(branch.Items[0]is TreeViewItem{Header:"Leaf"}leaf&&leaf.GetVisualRoot()==preview.Window,"Expanded TreeView child missing in visual tree.");
                RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Preview selection wrote source.");
            }
            finally{entry.IsSubMenuOpen=false;combo.IsDropDownOpen=false;}
        });
        foreach(var type in new[]{"Button","TextBox","TextBlock","Border","Grid","Canvas","StackPanel","DockPanel","WrapPanel","ScrollViewer","TabControl","Menu","ComboBox","ListBox","TreeView","Calendar","DatePicker","TimePicker","Slider","ProgressBar","ToggleSwitch"})
            Check("NativeFidelity_"+type,()=>
            {
                var body=type switch {
                    "Button" => "Content='Action' Padding='2,4,6,8' CornerRadius='1,2,3,4'",
                    "TextBox" => "Text='Typed' Watermark='Hint' Padding='3,5,7,9'",
                    "TextBlock" => "Text='Title' FontFamily='Arial' FontStyle='Italic' FontSize='22'",
                    "Border" => "Padding='2,4,6,8' BorderThickness='1,2,3,4' CornerRadius='1,3,5,7'",
                    "Slider" or "ProgressBar" => "Minimum='0' Maximum='100' Value='25'",
                    "ToggleSwitch" => "IsChecked='True'", _ => "" };
                var vm=Universal4AVm($"<a:Grid><a:{type} Name='Target' Width='300' Height='240' Margin='3,5,7,9' Opacity='0.7' {body}/></a:Grid>");
                var native=(Window)RuntimeAxamlPreviewLoader.Load(vm.ActiveAxamlSourceDocument!.OriginalText);
                var tree=AxamlLayoutProjection.CreateNativeTree(vm.ActiveAxamlSourceDocument,vm.Controls);
                var projected=new Window{Content=tree.Root,Width=800,Height=600,SystemDecorations=SystemDecorations.None,ShowInTaskbar=false,Position=new PixelPoint(-4000,-4000)};
                try {
                    native.SystemDecorations=SystemDecorations.None;native.Width=800;native.Height=600;native.Position=new PixelPoint(-3000,-3000);native.ShowInTaskbar=false;
                    native.Show();projected.Show();NativeItemsUiWait(25);native.UpdateLayout();projected.UpdateLayout();
                    var expected=native.GetVisualDescendants().OfType<Control>().Single(c=>c.Name=="Target");
                    var actual=tree.Controls[vm.Controls.Single(c=>c.Name=="Target").Id];
                    RequireCapability(actual.GetType()==expected.GetType()&&actual.GetVisualRoot()==projected,"Type/visual root differs.");
                    var a=actual.TranslatePoint(default,tree.Root)!.Value;var e=expected.TranslatePoint(default,(Control)native.Content!)!.Value;
                    RequireCapability(Math.Abs(a.X-e.X)<=1&&Math.Abs(a.Y-e.Y)<=1&&Math.Abs(actual.Bounds.Width-expected.Bounds.Width)<=1&&Math.Abs(actual.Bounds.Height-expected.Bounds.Height)<=1,"Bounds differ: "+actual.Bounds+" vs "+expected.Bounds);
                    RequireCapability(actual.Margin==expected.Margin&&actual.Opacity==expected.Opacity&&actual.HorizontalAlignment==expected.HorizontalAlignment&&actual.VerticalAlignment==expected.VerticalAlignment,"Scalar placement differs.");
                    if(actual is Avalonia.Controls.Primitives.TemplatedControl t&&expected is Avalonia.Controls.Primitives.TemplatedControl et)
                        RequireCapability(t.Padding==et.Padding&&t.FontSize==et.FontSize&&t.FontStyle==et.FontStyle&&t.BorderThickness==et.BorderThickness,"Styled scalar differs.");
                    RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Native projection mutated source.");
                } finally {projected.Close();native.Close();}
            });
        foreach(var relative in new[]{"Layout/GridStackPanel.axaml","Layout/GridDefinitions.axaml","Layout/DockPanel.axaml","Layout/DeepNested.axaml","Layout/Bindings.axaml","Layout/Styles.axaml","Layout/Preservation.axaml","Coverage/Tabs.axaml","Coverage/Content.axaml","Coverage/Wrap.axaml","Coverage/Preserved.axaml","Phase4/Combined.axaml","Phase4/ComboStatic.axaml","Phase4/ComboBindings.axaml","Phase5/Tree.axaml","Phase5/List.axaml"})
            Check("RealFixture_"+relative,()=>
            {
                var path=Path.Combine(FindRepositoryRoot(),"Samples","RoundTrip",relative);var text=File.ReadAllText(path);
                var source=new AxamlImportService().Import(text,path);var patch=new AxamlPatchWriter().CreatePatch(source.RoundTripDocument,source.Document);
                RequireCapability(patch.CanApply&&patch.PatchedText==text,"Fixture no-op is not source-preserving.");
                Console.WriteLine($"FIXTURE4B {relative}; imported={source.Document.Controls.Count}; opaque={source.CapabilityReport.Structure!.OpaqueVisualElements}");
            });
        Console.WriteLine($"UNIVERSAL4B_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (Window,WeakReference) ClosedImported4BPreview(bool mode)
    {
        var vm=Universal4AVm("<a:Button Name='Target' Content='Close' Padding='1,2,3,4'/>");
        var weak=new WeakReference(vm.ActiveAxamlSourceDocument!.SourceMap);
        var preview=ShowDataGridRegressionPreview(vm,mode);preview.Dispose();
        // An open editor legitimately owns the same map as Preview. The harness also
        // keeps VMs rooted through the existing static PropertyGrid ValueTrace callback.
        // Close the editor document before testing its release, while retaining Window.
        Console.WriteLine("MEMORY4B activeEditorOwnsMap="+ReferenceEquals(vm.ActiveAxamlSourceDocument!.SourceMap,weak.Target)
            +"; traceRoot="+(PropertyGridRowViewModel.ValueTrace?.GetInvocationList().Any(d=>ReferenceEquals(d.Target,vm))==true));
        vm.NewProjectEditorCommand!.Execute(null);
        RequireCapability(vm.ActiveAxamlSourceDocument is null,"New Project did not close imported document.");
        return (preview.Window,weak);
    }
    private static int RunUniversal4BProfile()
    {
        var samples = new List<object>();
        foreach (var scene in new[] { ("Small", Universal4ASource(10)), ("Medium", Universal4ASource(100)),
            ("MainWindow", File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"))) })
        for (var iteration = -5; iteration < 30; iteration++)
        {
            using var profile = Environment.GetEnvironmentVariable("UNIVERSAL4B_STAGES") == "0" ? null : LayoutDropProfile.Begin("4b", "Import");
            var reflection = AvaloniaMetadataCatalog.BuildCount;
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var gc = Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            var timer = Stopwatch.StartNew(); var result = new AxamlImportService().Import(scene.Item2); timer.Stop();
            var importMs = timer.Elapsed.TotalMilliseconds; var importBytes = GC.GetAllocatedBytesForCurrentThread()-allocated;
            var target = result.Document.Controls.First(c => c.Type == "Button" || c.Name == "N0");
            allocated = GC.GetAllocatedBytesForCurrentThread(); timer.Restart(); target.Opacity = .6;
            var patch = new AxamlPatchWriter().CreatePatch(result.RoundTripDocument, result.Document); timer.Stop();
            RequireCapability(patch.CanApply && patch.HasChanges, "Profile did not perform a real patch.");
            if (iteration >= 0) samples.Add(new { Scene = scene.Item1, Iteration = iteration, ImportMs = importMs, ImportBytes = importBytes,
                PatchMs = timer.Elapsed.TotalMilliseconds, PatchBytes = GC.GetAllocatedBytesForCurrentThread()-allocated,
                GC = Enumerable.Range(0,3).Select(i => GC.CollectionCount(i)-gc[i]).ToArray(),
                MetadataBuilds = AvaloniaMetadataCatalog.BuildCount-reflection, Stages = profile?.Stages.ToArray(), Counters = profile?.Counters,
                ManagedBytes = GC.GetTotalMemory(false), PeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 });
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL4B_OUTPUT")!, JsonSerializer.Serialize(samples));
        Console.WriteLine("PROFILE4B samples=" + samples.Count); return 0;
    }
}
