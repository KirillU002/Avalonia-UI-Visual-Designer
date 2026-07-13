using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Data;
using System;

namespace FormDesigner.Services;

public static class RuntimeAxamlPreviewLoader
{
    public const string LoaderName = nameof(AvaloniaRuntimeXamlLoader);
    public const string SyntheticBaseUri = "avares://FormDesigner/RuntimePreview.axaml";

    public static object Load(string axaml)
    {
        if (string.IsNullOrWhiteSpace(axaml))
            throw new InvalidOperationException("Runtime Preview AXAML is empty.");

        EnsureRuntimeCompilerAssembliesLoaded(axaml);
        var document = new RuntimeXamlLoaderDocument(new Uri(SyntheticBaseUri), axaml);
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            DesignMode = false,
            LocalAssembly = null,
            UseCompiledBindingsByDefault = false
        };
        return AvaloniaRuntimeXamlLoader.Load(document, configuration);
    }

    private static void EnsureRuntimeCompilerAssembliesLoaded(string axaml)
    {
        GC.KeepAlive(typeof(Binding));
        GC.KeepAlive(typeof(ItemsRepeater).Assembly);
        if (axaml.Contains("DataGrid", StringComparison.Ordinal))
            GC.KeepAlive(typeof(DataGrid).Assembly);
    }
}
