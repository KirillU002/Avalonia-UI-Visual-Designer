using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaDesigner.VsHost;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.ViewModels;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // Run each host in a fresh process from its deployed payload (including Plugins).
    // Exercise OnFrameworkInitializationCompleted; comparing two calls to the registrar
    // would miss missing startup steps and missing deployed dependencies.
    private static int CaptureHostToolbox(string host, string output)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        if (host == "vshost")
        {
            var options = typeof(VsHostApp).Assembly.GetType("AvaloniaDesigner.VsHost.VsHostStartupOptions", true)!;
            options.GetProperty("Current")!.SetValue(null,options.GetMethod("Parse")!.Invoke(null,
                new object[]{new[]{"--pipe","ToolboxParity-" + Guid.NewGuid().ToString("N")}}));
            AppBuilder.Configure<VsHostApp>().UsePlatformDetect().WithInterFont().SetupWithLifetime(lifetime);
        }
        else AppBuilder.Configure<FormDesigner.App>().UsePlatformDetect().WithInterFont().SetupWithLifetime(lifetime);
        var window = (FormDesigner.Views.MainWindow)lifetime.MainWindow!;
        try
        {
            var vm = (MainWindowViewModel)window.DataContext!;
            var registry = (DesignerRegistry)vm.Registry;
            var discovered = AvaloniaMetadataCatalog.DiscoverControls();
            var recognition = new AxamlTypeRecognition();
            var frameworkControls = discovered.Select(type =>
            {
                var assembly = type.Assembly.GetName().Name;
                var qualified = "clr-namespace:" + type.Namespace + ";assembly=" + assembly;
                return new { type.FullName, type.Namespace, Assembly=assembly, type.IsAbstract,
                    QualifiedRecognition=recognition.Resolve(qualified,type.Name)?.FrameworkType==type,
                    DefaultNamespace=recognition.Resolve("https://github.com/avaloniaui",type.Name)?.FrameworkType==type,
                    SourceAdapter=AxamlControlMetadata.Find(type.Name)?.NativeType==type };
            }).ToArray();
            var rows = vm.ToolboxGroups.SelectMany(group => group.Items.Select(item =>
            {
                var descriptor = registry.GetRequiredControl(item.Type);
                var identity = DesignerRegistry.NativeIdentity(descriptor);
                var type = identity.FrameworkType;
                return new {Id=item.Type,item.Title,item.Category,item.Description,item.Icon,item.ProviderId,Group=group.Title,
                    Type=identity.FullName,Assembly=identity.Assembly,descriptor.IsContainer,descriptor.CanHostChildren,
                    descriptor.ChildLayoutMode,Descriptor=descriptor.GetType().FullName, Properties=descriptor.Properties.Select(p=>p.Key),
                    NativeExport=DesignerRegistry.CanExportNativeType(registry,descriptor), Registered=registry.TryGetControl(item.Type,out _), MetadataCount=type==null?0:AvaloniaMetadataCatalog.Get(type).Properties.Count,
                    ImportAdapter=AxamlControlMetadata.Find(item.Type)!=null,Insert=AxamlStructuralPolicy.CanInsertToolboxItem(item.Type)};
            })).ToArray();
            var searches = new Dictionary<string,string[]>();
            foreach (var query in new[]{"Grid","Text","Eremex","Фигура","missing-7a"})
            {
                vm.ToolboxSearchText=query;
                searches[query]=vm.ToolboxGroups.SelectMany(g=>g.Items.Select(i=>i.Type)).ToArray();
            }
            vm.ToolboxSearchText = "";
            var imported = new AxamlImportService().Import("<Window xmlns='https://github.com/avaloniaui'><Grid><Button Content='Catalog'/></Grid></Window>");
            vm.LoadAxamlImportedDocument(imported);
            var importedCards = vm.ToolboxGroups.SelectMany(g => g.Items.Select(i => new
                { Id=i.Type, i.Title, i.Category, i.Icon, i.OperationHint, Group=g.Title })).ToArray();
            if (!vm.IsAxamlRoundTripDocument || !rows.Select(r=>r.Id).SequenceEqual(importedCards.Select(r=>r.Id)))
                throw new InvalidOperationException("Opening an imported document changed the host Toolbox.");
            File.WriteAllText(output,JsonSerializer.Serialize(new {Rows=rows,Searches=searches,ImportedCards=importedCards,
                Descriptors=registry.GetControls().Select(d=>d.TypeKey), Plugins=registry.GetPluginLoadReports().Select(p=>new {p.PluginId,p.Status,p.Message,p.RegisteredControls}),
                FrameworkControls=frameworkControls, Metadata=discovered.Select(t=>t.AssemblyQualifiedName)},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        finally { window.CloseForExternalHost(); }
    }
}
