using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System;
using System.IO;

namespace FormDesigner;

public partial class App : Application
{
    private readonly DesignerRegistry _registry = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            ConfigureDesignerSystem();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(_registry),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void ConfigureDesignerSystem()
    {
        BuiltInControlRegistrar.Register(_registry);
        _registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider());

        var logger = new TraceDesignerLogger();
        var loader = new PluginLoader(logger);
        var pluginFolder = Path.Combine(AppContext.BaseDirectory, "Plugins");
        loader.LoadFromFolder(pluginFolder, _registry, replaceDiagnostics: true);
    }
}
