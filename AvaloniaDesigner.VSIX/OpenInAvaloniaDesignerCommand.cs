using AvaloniaDesigner.Host.Protocol;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

internal sealed class OpenInAvaloniaDesignerCommand
{
    private readonly AsyncPackage _package;
    private readonly VsHostBridgeClient _bridge;
    private readonly VsOutputWindowLogger _output;
    private readonly Action<string, Exception?> _diagnosticLog;
    private VsDocumentBuffer? _buffer;
    private VsDocumentSnapshot? _snapshot;

    private OpenInAvaloniaDesignerCommand(
        AsyncPackage package,
        VsHostBridgeClient bridge,
        VsOutputWindowLogger output,
        Action<string, Exception?> diagnosticLog)
    {
        _package = package;
        _bridge = bridge;
        _output = output;
        _diagnosticLog = diagnosticLog;
        _bridge.PatchReceived += ApplyPatchAsync;
        _bridge.ReloadRequested += ReloadFromVisualStudioAsync;
        _bridge.Disconnected += () => Log("VSIX_HOST_DISCONNECTED");
        _bridge.Log += Log;
    }

    public static async Task InitializeAsync(
        AsyncPackage package,
        VsHostBridgeClient bridge,
        Action<string, Exception?> diagnosticLog)
    {
        const string serviceType = "IMenuCommandService";
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_SERVICE_REQUEST_START serviceType=" + serviceType, null);
            var menu = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            diagnosticLog(
                "AVALONIA_DESIGNER_VSIX_COMMAND_SERVICE_REQUEST_RESULT serviceNull=" + (menu is null) +
                " serviceType=" + (menu?.GetType().FullName ?? "<null>"),
                null);
            if (menu is null)
                throw new InvalidOperationException("Visual Studio menu command service is unavailable.");

            var commandId = new CommandID(Guids.CommandSet, CommandIds.OpenInDesigner);
            diagnosticLog($"AVALONIA_DESIGNER_VSIX_COMMAND_CREATE_START commandSet={Guids.CommandSet:D} commandId=0x{CommandIds.OpenInDesigner:X4}", null);
            var command = new OpenInAvaloniaDesignerCommand(package, bridge, new VsOutputWindowLogger(package), diagnosticLog);
#pragma warning disable VSSDK007 // OleMenuCommand callbacks cannot return a task; ExecuteAsync reports operational errors itself.
            var menuCommand = new OleMenuCommand(
                (_, _) => ThreadHelper.JoinableTaskFactory.RunAsync(command.ExecuteAsync).FileAndForget("AvaloniaDesigner/Open"),
                commandId);
#pragma warning restore VSSDK007
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_CREATE_SUCCESS", null);

            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_ADD_START", null);
            menu.AddCommand(menuCommand);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_ADD_SUCCESS", null);

            var registered = menu.FindCommand(commandId);
            diagnosticLog("AVALONIA_DESIGNER_VSIX_COMMAND_LOOKUP_AFTER_ADD found=" + (registered is not null), null);
            if (registered is null)
                throw new InvalidOperationException("OleMenuCommandService did not retain the Open Designer command.");

            diagnosticLog("AVALONIA_DESIGNER_VSIX_OPEN_DESIGNER_COMMAND_REGISTERED", null);
        }
        catch (Exception ex)
        {
            diagnosticLog("AVALONIA_DESIGNER_VSIX_OPEN_DESIGNER_COMMAND_REGISTRATION_FAILED", ex);
            throw;
        }
    }

    private async Task ExecuteAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        Log("OPEN_DESIGNER_COMMAND_EXECUTED");
        var bufferResult = await GetDocumentBufferAsync();
        var captureError = string.Empty;
        if (bufferResult.Buffer is null
            || !bufferResult.Buffer.TryCaptureActiveAxaml(out var snapshot, out captureError))
        {
            var message = bufferResult.Buffer is null ? bufferResult.Error : captureError;
            VsShellUtilities.ShowMessageBox(_package, message, "Avalonia UI Visual Designer", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        _snapshot = snapshot;
        Log($"ACTIVE_DOCUMENT_RESOLVED path={snapshot.FilePath}; version={snapshot.Version}");
        try
        {
            var opened = await _bridge.OpenDocumentAsync(snapshot);
            if (!opened.CanEdit)
                VsShellUtilities.ShowMessageBox(_package, opened.Status, "Avalonia UI Visual Designer", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        catch (System.IO.FileNotFoundException ex)
        {
            Log("VSIX_VSHOST_START_FAILED", ex);
            VsShellUtilities.ShowMessageBox(
                _package,
                "Avalonia Designer host не найден.\r\nПереустановите расширение.",
                "Avalonia UI Visual Designer",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        catch (OperationCanceledException ex)
        {
            Log("VSIX_IPC_CONNECT_FAILED", ex);
            VsShellUtilities.ShowMessageBox(
                _package,
                "Не удалось подключиться к Avalonia Designer host.",
                "Avalonia UI Visual Designer",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        catch (Exception ex)
        {
            Log("VSIX_OPEN_FAILED", ex);
            VsShellUtilities.ShowMessageBox(_package, ex.Message, "Avalonia UI Visual Designer", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private async Task ApplyPatchAsync(ApplyDesignerPatchPayload patch)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        if (_snapshot is null)
            throw new VsSourceVersionConflictException("В Visual Studio нет исходного snapshot для Designer patch.");

        if (!DesignerHostPatchGuard.Matches(new OpenDocumentPayload
            {
                Version = _snapshot.Version,
                Checksum = _snapshot.Checksum
            }, patch))
            throw new VsSourceVersionConflictException("AXAML был изменён в Visual Studio после открытия Designer.");

        if (_buffer is null)
            throw new VsSourceVersionConflictException("Visual Studio document buffer is unavailable for the Designer patch.");

        if (!_buffer.TryApplyPatch(_snapshot, patch.Edits, out var applied, out var error))
            throw new VsSourceVersionConflictException(error);

        _snapshot = applied;
        await _bridge.SendPatchAppliedAsync(Guid.NewGuid().ToString("N"), applied.DocumentId, applied);
    }

    private async Task ReloadFromVisualStudioAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var bufferResult = await GetDocumentBufferAsync();
        var captureError = string.Empty;
        if (bufferResult.Buffer is null
            || !bufferResult.Buffer.TryCaptureActiveAxaml(out var snapshot, out captureError))
        {
            Log($"VSIX_RELOAD_FAILED {(bufferResult.Buffer is null ? bufferResult.Error : captureError)}");
            return;
        }

        _snapshot = snapshot;
        await _bridge.ReloadDocumentAsync(snapshot);
    }

    private async Task<(VsDocumentBuffer? Buffer, string Error)> GetDocumentBufferAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        if (_buffer is not null)
            return (_buffer, string.Empty);

        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE;
        if (dte is null)
        {
            Log("VSIX_DTE_SERVICE_UNAVAILABLE");
            return (null, "Visual Studio DTE service is unavailable.");
        }

        _buffer = new VsDocumentBuffer(dte);
        Log("VSIX_DTE_SERVICE_RESOLVED");
        return (_buffer, string.Empty);
    }

    private void Log(string message)
    {
        _output.Write(message);
        _diagnosticLog(message, null);
    }

    private void Log(string message, Exception exception)
    {
        _output.Write($"{message}{Environment.NewLine}{exception}");
        _diagnosticLog(message, exception);
    }
}
