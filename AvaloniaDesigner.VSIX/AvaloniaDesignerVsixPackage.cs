using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Avalonia UI Visual Designer", "External AXAML Designer host bridge", "0.1")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(Guids.PackageString)]
public sealed class AvaloniaDesignerVsixPackage : AsyncPackage
{
    internal VsHostBridgeClient? BridgeClient { get; private set; }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        BridgeClient = new VsHostBridgeClient(this);
        await OpenInAvaloniaDesignerCommand.InitializeAsync(this, BridgeClient);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            BridgeClient?.Dispose();
        base.Dispose(disposing);
    }
}
