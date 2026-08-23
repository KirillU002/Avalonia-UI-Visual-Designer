using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.Services;
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
            IDesignerHostServices hostServices = new StandaloneDesignerHostServices();
            ConfigureDesignerSystem(hostServices);
            desktop.MainWindow = new MainWindow(hostServices)
            {
                DataContext = new MainWindowViewModel(_registry, hostServices),
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

    private void ConfigureDesignerSystem(IDesignerHostServices hostServices)
    {
        BuiltInControlRegistrar.Register(_registry);
        _registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider(hostServices.Paths.ApplicationBaseDirectory));

        var logger = new TraceDesignerLogger();
        var loader = new PluginLoader(logger);
        loader.LoadFromFolder(hostServices.Paths.PluginDirectory, _registry, replaceDiagnostics: true);
    }
}
