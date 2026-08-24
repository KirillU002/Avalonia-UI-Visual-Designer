using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.ViewModels;
using System.Linq;

namespace AvaloniaDesigner.VsHost;

public sealed class VsHostApp : Application
{
    private readonly DesignerRegistry _registry = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var hostServices = new VsHostDesignerHostServices();
            BuiltInControlRegistrar.Register(_registry);
            _registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider(hostServices.Paths.ApplicationBaseDirectory));

            var viewModel = new MainWindowViewModel(_registry, hostServices);
            var window = new VsHostWindow(hostServices)
            {
                DataContext = viewModel,
                Title = "Avalonia UI Visual Designer - Visual Studio bridge"
            };
            hostServices.AttachTopLevel(window);

            var bridge = new VsHostBridge(viewModel, window, VsHostStartupOptions.Current.PipeName);
            window.ApplyRequested += async (_, _) => await bridge.ApplyAsync();
            window.ReloadRequested += async (_, _) => await bridge.RequestReloadAsync();
            window.Closed += (_, _) => bridge.Dispose();
            bridge.Start();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var validators = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var validator in validators)
            BindingPlugins.DataValidators.Remove(validator);
    }
}
