using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

/// <summary>
/// Temporary VSPackage load probe. This command deliberately does not touch the IPC bridge,
/// Host.Protocol, DTE, AXAML, Avalonia, or the external host process.
/// </summary>
internal sealed class AvaloniaDesignerDiagnosticCommand
{
    private readonly AsyncPackage _package;
    private readonly Action<string, Exception?> _diagnosticLog;

    private AvaloniaDesignerDiagnosticCommand(AsyncPackage package, Action<string, Exception?> diagnosticLog)
    {
        _package = package;
        _diagnosticLog = diagnosticLog;
    }

    public static async Task InitializeAsync(AsyncPackage package, Action<string, Exception?> diagnosticLog)
    {
        var stage = "COMMAND_SERVICE_REQUEST";
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_REGISTRATION_UI_THREAD onUiThread=" + ThreadHelper.CheckAccess(), null);

            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_SERVICE_REQUEST_START serviceType=IMenuCommandService", null);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            diagnosticLog(
                "AVALONIA_DESIGNER_VSIX_COMMAND_SERVICE_REQUEST_RESULT serviceNull=" + (commandService is null) +
                " serviceType=" + (commandService?.GetType().FullName ?? "<null>"),
                null);

            if (commandService is null)
                throw new InvalidOperationException("Visual Studio menu command service is unavailable.");

            var commandId = new CommandID(Guids.CommandSet, CommandIds.DiagnosticLoadProbe);
            stage = "COMMAND_CREATE";
            diagnosticLog($"AVALONIA_DESIGNER_VSIX_COMMAND_CREATE_START commandSet={Guids.CommandSet:D} commandId=0x{CommandIds.DiagnosticLoadProbe:X4}", null);
            var command = new AvaloniaDesignerDiagnosticCommand(package, diagnosticLog);
            var menuCommand = new OleMenuCommand(command.Execute, commandId);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_CREATE_SUCCESS", null);

            stage = "COMMAND_ADD";
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_ADD_START", null);
            commandService.AddCommand(menuCommand);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_ADD_SUCCESS", null);

            stage = "COMMAND_LOOKUP_AFTER_ADD";
            var registered = commandService.FindCommand(commandId);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_LOOKUP_AFTER_ADD found=" + (registered is not null), null);
            if (registered is null)
                throw new InvalidOperationException("OleMenuCommandService did not retain the diagnostic command.");

            diagnosticLog("AVALONIA_DESIGNER_VSIX_DIAGNOSTIC_COMMAND_REGISTERED", null);
        }
        catch (Exception ex)
        {
            diagnosticLog($"AVALONIA_DESIGNER_VSIX_{stage}_FAILED", ex);
            throw;
        }
    }

    private void Execute(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _diagnosticLog("AVALONIA_DESIGNER_VSIX_DIAGNOSTIC_COMMAND_EXECUTED", null);
        VsShellUtilities.ShowMessageBox(
            _package,
            "Avalonia Designer VSPackage loaded.",
            "Avalonia UI Visual Designer",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
