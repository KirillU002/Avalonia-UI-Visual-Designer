using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string PropertyGrid3AFolder => Path.Combine(FindRepositoryRoot(), "artifacts", "universal-phase3a");
    private static MainWindowViewModel PropertyGrid3AScene(bool original)
    {
        if (original) return MetadataScene();
        var vm = GridFirstVm(); vm.DesignWidth = 1200; vm.DesignHeight = 1000;
        vm.SurfaceGridRowDefinitions = "*,*,*,*"; vm.SurfaceLayoutRows = 4;
        vm.SurfaceGridColumnDefinitions = "*,*"; vm.SurfaceLayoutColumns = 2;
        var types = new[] { "Button", "DataGrid", "Grid", "Calendar", "NumericUpDown", "ListBox", "DatePicker" };
        for (var i = 0; i < types.Length; i++)
        {
            var c = vm.CreateControl(types[i], 0, 0); c.GridRow = i / 2; c.GridColumn = i % 2;
            c.Width = 360; c.Height = types[i] is "Calendar" or "DataGrid" ? 180 : 40;
            c.WidthMode = c.HeightMode = "Fixed";
        }
        return vm;
    }
    private static int RunPropertyGrid3AProfile()
    {
        Directory.CreateDirectory(PropertyGrid3AFolder);
        var label = Environment.GetEnvironmentVariable("PROPERTYGRID3A_LABEL") ?? "baseline";
        var mode = Environment.GetEnvironmentVariable("FORMDESIGNER_PROPERTYGRID_REUSE") == "0" ? "disabled" : "enabled";
        var original = Environment.GetEnvironmentVariable("PROPERTYGRID3A_SCENE") == "original";
        if (label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new Exception("Invalid profile label");
        var vm = PropertyGrid3AScene(original); var first = Stopwatch.StartNew();
        var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout(); first.Stop();
        NativeItemsUiWait(200);
        var inspector = window.FindControl<FormDesigner.Views.DesignerPropertyInspector>("DesignerPropertyInspector")!;
        var observations = new List<object>(); var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var builds = FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.BuildCount;
        // Exercise every recycled transition as well as the first materialization.
        // The original scene retains Phase 2's one-pass warm-up for comparability.
        var rounds = original ? 30 : vm.Controls.Count * 30; var warmup = vm.Controls.Count * (original ? 1 : 3);
        for (var i = -warmup; i < rounds; i++)
        {
            var control = vm.Controls[(i + warmup) % vm.Controls.Count];
            var gcBefore = Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            using var profile = LayoutDropProfile.Begin(vm.ActiveDocumentId, "propertygrid-profile");
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
            using(LayoutDropProfile.Stage("InspectorSelectionChanged")) vm.SelectSingleControl(control);
            var selection = clock.Elapsed.TotalMilliseconds;
            using(LayoutDropProfile.Stage("InspectorMeasureArrange")) window.UpdateLayout();
            var layout = clock.Elapsed.TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            double? commit = null;
            ElementComposition.GetElementVisual(window)?.Compositor.RequestCompositionUpdate(() => commit = clock.Elapsed.TotalMilliseconds);
            while (commit is null && clock.Elapsed < TimeSpan.FromSeconds(2)) NativeItemsUiWait(2);
            profile.Dispose();
            if (!inspector.GetVisualDescendants().OfType<PropertyGridTextEditor>().Any())
                Console.WriteLine("EMPTY " + JsonSerializer.Serialize(new { Owner=inspector.Context?.GetType().Name,
                    Source=inspector.CategoriesItemsControl.ItemsSource?.GetType().ToString(), Items=inspector.CategoriesItemsControl.Items.Count,
                    Model=vm.PropertyGridCategories.Count, Nodes=inspector.GetVisualDescendants().Count(),
                    Text=inspector.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).Take(10).ToArray() }));
            RequireCapability(inspector.GetVisualDescendants().OfType<PropertyGridTextEditor>().Any(), "Profile rejected: native editors are missing.");
            RequireCapability(inspector.GetVisualDescendants().OfType<PropertyGridTextEditor>().All(e => e.Row?.ContextControlId == control.Id), "Profile rejected: recycled editor has stale context.");
            observations.Add(new { Iteration = i, Type = control.Type, SelectionMs = selection, LayoutMs = layout - selection,
                ElapsedMs = layout, FirstCompositorUpdateMs = commit, AllocatedBytes = allocated, Profile = profile.Stages, Counters = profile.Counters,
                GcCollections = Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gcBefore[g]).ToArray(),
                Rows = vm.PropertyGridCategories.Sum(c => c.Rows.Count), InspectorVisuals = inspector.GetVisualDescendants().Count(),
                Editors = inspector.GetVisualDescendants().OfType<PropertyGridTextEditor>().Count() });
            NativeItemsUiWait(15);
        }
        var result = new { Label = label, Mode = mode, OriginalScene = original, Warmup = warmup, Rounds = rounds, ClientSize=window.ClientSize.ToString(),window.RenderScaling,
            FirstWindowAndInspectorSetupMs = first.Elapsed.TotalMilliseconds, MetadataRebuilds = FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.BuildCount - builds,
            GcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gc[g]), Observations = observations };
        File.WriteAllText(Path.Combine(PropertyGrid3AFolder, label + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear();
        Console.WriteLine($"PROPERTYGRID3A_PROFILE {label}: {rounds} samples; mode={mode}; metadata rebuilds={result.MetadataRebuilds}"); return 0;
    }
}
