using Avalonia;
using System;

namespace AvaloniaDesigner.VsHost;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VsHostStartupOptions.Current = VsHostStartupOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<VsHostApp>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
