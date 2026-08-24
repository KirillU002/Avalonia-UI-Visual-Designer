using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

internal sealed class VsOutputWindowLogger
{
    private static readonly Guid PaneId = new("4D9D0B9F-3EE3-4C17-A853-1C028445DD1C");
    private readonly AsyncPackage _package;
    private IVsOutputWindowPane? _pane;

    public VsOutputWindowLogger(AsyncPackage package) => _package = package;

    public void Write(string message)
    {
        Trace.WriteLine(message);
#pragma warning disable VSSDK007 // Output is best-effort diagnostics; command execution must never wait for it.
        _package.JoinableTaskFactory.RunAsync(() => WriteAsync(message)).FileAndForget("AvaloniaDesigner/Output");
#pragma warning restore VSSDK007
    }

    private async Task WriteAsync(string message)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var outputWindow = await _package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow is null)
            return;

        if (_pane is null)
        {
            var paneId = PaneId;
            outputWindow.CreatePane(ref paneId, "Avalonia UI Visual Designer", 1, 1);
            outputWindow.GetPane(ref paneId, out _pane);
        }

        _pane?.OutputStringThreadSafe($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
    }
}
