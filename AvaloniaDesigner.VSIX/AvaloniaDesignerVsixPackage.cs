using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Avalonia UI Visual Designer", "External AXAML Designer host bridge", "0.1.10")]
[ProvideMenuResource("Menus.ctmenu", 5)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(Guids.PackageString)]
public sealed class AvaloniaDesignerVsixPackage : AsyncPackage
{
    private const string ActivityLogSource = "Avalonia UI Visual Designer";
    private VsHostBridgeClient? _bridge;

    static AvaloniaDesignerVsixPackage()
    {
        VsixPackageLoadProbe.Write("AVALONIA_DESIGNER_VSIX_PACKAGE_STATIC_CONSTRUCTOR");
    }

    public AvaloniaDesignerVsixPackage()
    {
        WriteDiagnostic("AVALONIA_DESIGNER_VSIX_PACKAGE_CONSTRUCTOR");
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        WriteDiagnostic("AVALONIA_DESIGNER_VSIX_INITIALIZE_START");
        try
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            WriteDiagnostic($"AVALONIA_DESIGNER_VSIX_COMMAND_REGISTRATION_UI_THREAD onUiThread={ThreadHelper.CheckAccess()}");
            _bridge = new VsHostBridgeClient(this);
            await OpenInAvaloniaDesignerCommand.InitializeAsync(this, _bridge, WriteDiagnostic);
            WriteDiagnostic("AVALONIA_DESIGNER_VSIX_INITIALIZE_SUCCESS");
        }
        catch (Exception ex)
        {
            WriteDiagnostic("AVALONIA_DESIGNER_VSIX_INITIALIZE_FAILED", ex);
            throw;
        }
    }

    private static void WriteDiagnostic(string message, Exception? exception = null)
    {
        VsixPackageLoadProbe.Write(message, exception);
        if (exception is null)
            ActivityLog.LogInformation(ActivityLogSource, message);
        else
            ActivityLog.LogError(ActivityLogSource, $"{message}{Environment.NewLine}{exception}");
    }

}
