using Avalonia.Threading;
using AvaloniaDesigner.Host.Protocol;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VsHost;

/// <summary>
/// Owns one Visual Studio document snapshot. It never writes an AXAML file: ApplyAsync returns
/// source-preserving edits to the bridge, and only the editor buffer decides when to persist them.
/// </summary>
public sealed partial class VsHostBridge : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly VsHostWindow _window;
    private readonly string _pipeName;
    private readonly AxamlImportService _importService = new();
    private readonly CancellationTokenSource _cancellation = new();
    private NamedPipeProtocolConnection? _connection;
    private OpenDocumentPayload? _document;
    private bool _started;
    private bool _disposed;

    public VsHostBridge(MainWindowViewModel viewModel, VsHostWindow window, string pipeName)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentException("Pipe name is required.", nameof(pipeName))
            : pipeName;
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        Log("VSHOST_START", $"pipe={_pipeName}");
        _ = AcceptAndRunAsync();
    }

    public async Task ApplyAsync()
    {
        if (_document is null)
        {
            SetStatus("Нет открытого AXAML-документа.");
            return;
        }

        if (_connection is null)
        {
            SetStatus("Visual Studio отключена. Изменения не отправлены.");
            return;
        }

        try
        {
            Log("VSHOST_PATCH_CREATE_START", $"document={_document.FilePath}; version={_document.Version}");
            var patch = await Dispatcher.UIThread.InvokeAsync(() => _viewModel.CreateActiveAxamlPatch(_document.Text));
            if (!patch.CanApply)
            {
                SetStatus("AXAML был изменён в Visual Studio. Перезагрузите документ.");
                Log("VSHOST_PATCH_CREATE_FAILED", "reason=external-change-or-unsafe-patch");
                return;
            }

            if (!patch.HasChanges)
            {
                SetStatus("Изменений для применения нет.");
                return;
            }

            var requestId = Guid.NewGuid().ToString("N");
            var payload = new ApplyDesignerPatchPayload
            {
                ExpectedVersion = _document.Version,
                ExpectedChecksum = _document.Checksum,
                Edits = patch.Edits.Select(edit => new TextEditPayload
                {
                    Start = edit.Start,
                    Length = edit.Length,
                    NewText = edit.NewText
                }).ToList()
            };

            Log("VSHOST_PATCH_CREATED", $"document={_document.FilePath}; edits={payload.Edits.Count}; version={payload.ExpectedVersion}");
            await _connection.SendAsync(DesignerHostMessageTypes.ApplyDesignerPatch, requestId, _documentId, payload, _cancellation.Token);
            SetStatus("Изменения отправлены в Visual Studio...");
            Log("VSHOST_PATCH_SENT", $"document={_document.FilePath}; edits={payload.Edits.Count}; version={payload.ExpectedVersion}");
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось подготовить изменения: {ex.Message}");
            Log("VSHOST_PATCH_CREATE_FAILED", ex.ToString());
        }
    }

    public async Task RequestReloadAsync()
    {
        if (_document is null || _connection is null)
        {
            SetStatus("Visual Studio недоступна для перезагрузки.");
            return;
        }

        await _connection.SendAsync(DesignerHostMessageTypes.ReloadRequested, Guid.NewGuid().ToString("N"), _documentId, new EmptyPayload(), _cancellation.Token);
        SetStatus("Запрошен актуальный AXAML из Visual Studio...");
    }

    private string _documentId = string.Empty;

    private async Task AcceptAndRunAsync()
    {
        try
        {
            using var server = NamedPipeProtocolConnection.CreateServer(_pipeName);
            await server.WaitForConnectionAsync(_cancellation.Token);
            _connection = new NamedPipeProtocolConnection(server);
            SetStatus("Visual Studio подключена.");
            Log("VSHOST_PROTOCOL_HANDSHAKE", "pipe connected");

            while (!_cancellation.IsCancellationRequested)
            {
                var message = await _connection.ReceiveAsync(_cancellation.Token);
                if (message is null)
                    break;
                await HandleMessageAsync(message);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Visual Studio bridge error: {ex.Message}");
            Log("VSHOST_DISCONNECTED", ex.ToString());
        }
        finally
        {
            _connection?.Dispose();
            _connection = null;
            if (!_disposed)
                SetStatus("Avalonia Designer отключён от Visual Studio.");
            Log("VSHOST_DISCONNECTED", "pipe closed");
        }
    }

    private async Task HandleMessageAsync(DesignerHostEnvelope message)
    {
        if (message.ProtocolVersion != DesignerHostProtocol.CurrentVersion)
        {
            await SendErrorAsync(message, "PROTOCOL_VERSION_MISMATCH", $"Expected {DesignerHostProtocol.CurrentVersion}, got {message.ProtocolVersion}.");
            return;
        }

        switch (message.MessageType)
        {
            case DesignerHostMessageTypes.Hello:
                await SendAsync(DesignerHostMessageTypes.HelloAck, message, new HelloAckPayload
                {
                    HostVersion = typeof(VsHostBridge).Assembly.GetName().Version?.ToString() ?? "1.0",
                    ProtocolVersion = DesignerHostProtocol.CurrentVersion
                });
                break;

            case DesignerHostMessageTypes.OpenDocument:
            case DesignerHostMessageTypes.ReloadDocument:
                var document = _connection?.GetPayload<OpenDocumentPayload>(message);
                if (document is null)
                {
                    await SendErrorAsync(message, "DOCUMENT_PAYLOAD_MISSING", "OpenDocument payload is missing.");
                    return;
                }
                if (message.MessageType == DesignerHostMessageTypes.ReloadDocument)
                    Log("VSHOST_DOCUMENT_RELOAD", $"document={message.DocumentId}");
                await OpenDocumentAsync(message.DocumentId, document, message);
                break;

            case DesignerHostMessageTypes.DocumentChanged:
                await HandleDocumentChangedAsync(message);
                break;

            case DesignerHostMessageTypes.PatchApplied:
                var applied = _connection?.GetPayload<PatchAppliedPayload>(message);
                if (applied is not null)
                    await PatchAppliedAsync(applied);
                break;

            case DesignerHostMessageTypes.Error:
                var error = _connection?.GetPayload<ErrorPayload>(message);
                if (error is not null)
                    HandleBridgeError(error);
                break;

            case DesignerHostMessageTypes.CloseDocument:
                SetStatus("Документ закрыт в Visual Studio.");
                break;

            case DesignerHostMessageTypes.HostShutdown:
                Dispose();
                Dispatcher.UIThread.Post(_window.CloseForBridgeShutdown);
                break;

            default:
                await SendErrorAsync(message, "UNSUPPORTED_MESSAGE", $"Message '{message.MessageType}' is not supported by VsHost.");
                break;
        }
    }

    private async Task OpenDocumentAsync(string documentId, OpenDocumentPayload document, DesignerHostEnvelope request)
    {
        if (!string.Equals(document.Checksum, DesignerHostProtocol.ComputeChecksum(document.Text), StringComparison.Ordinal))
        {
            await SendErrorAsync(request, "DOCUMENT_CHECKSUM_MISMATCH", "The AXAML snapshot checksum does not match its text.");
            return;
        }

        try
        {
            Log("VSHOST_DOCUMENT_RECEIVED", $"path={document.FilePath}; version={document.Version}; targetFramework={document.TargetFramework}");
            Log("VSHOST_AXAML_IMPORT_START", $"path={document.FilePath}; length={document.Text.Length}");
            var result = _importService.Import(document.Text, document.FilePath);
            if (!result.CapabilityReport.CanSafelyPatch)
            {
                await SendAsync(DesignerHostMessageTypes.DocumentOpened, request, ToOpenedPayload(result, canEdit: false));
                SetStatus("AXAML открыт только для просмотра: безопасный patch невозможен.");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _viewModel.LoadAxamlImportedDocument(result, document.FilePath);
                _window.Title = $"Avalonia UI Visual Designer - {System.IO.Path.GetFileName(document.FilePath)} - Visual Studio bridge";
            });

            _document = document;
            _documentId = documentId;
            SetStatus($"Подключено к Visual Studio: {System.IO.Path.GetFileName(document.FilePath)}");
            Log("VSHOST_AXAML_IMPORT_SUCCESS", $"controls={result.Document.Controls.Count}; capability={result.CapabilityReport.Level}");
            Log("VSHOST_SURFACE_ATTACHED", $"document={documentId}; surface=FormDesigner.Views.DesignerSurface");
            await SendAsync(DesignerHostMessageTypes.DocumentOpened, request, ToOpenedPayload(result, canEdit: true));
        }
        catch (Exception ex)
        {
            SetStatus($"AXAML Preview не удалось открыть: {ex.Message}");
            Log("VSHOST_AXAML_IMPORT_FAILED", ex.ToString());
            await SendErrorAsync(request, "AXAML_IMPORT_FAILED", ex.Message, ex.ToString());
        }
    }

    private async Task PatchAppliedAsync(PatchAppliedPayload applied)
    {
        if (_document is null)
            return;

        if (!string.Equals(applied.Checksum, DesignerHostProtocol.ComputeChecksum(applied.Text), StringComparison.Ordinal))
        {
            SetStatus("Visual Studio вернула некорректное подтверждение patch.");
            Log("VSHOST_PATCH_APPLY_FAILED", "reason=checksum-mismatch");
            return;
        }

        _document.Text = applied.Text;
        _document.Version = applied.Version;
        _document.Checksum = applied.Checksum;
        await Dispatcher.UIThread.InvokeAsync(() => _viewModel.MarkAxamlRoundTripSaved(_document.FilePath, applied.Text));
        SetStatus("Изменения применены к буферу Visual Studio. Нажмите Ctrl+S для сохранения.");
        Log("VSHOST_PATCH_APPLIED", $"acknowledgedVersion={applied.Version}");
    }

    private void HandleBridgeError(ErrorPayload error)
    {
        if (string.Equals(error.Code, "SOURCE_VERSION_CONFLICT", StringComparison.Ordinal))
        {
            SetStatus("AXAML был изменён в Visual Studio после открытия Designer. Перезагрузите документ.");
            Log("VSHOST_DOCUMENT_RELOAD", "reason=source-version-conflict");
            return;
        }

        SetStatus(error.Message);
        Log("VSHOST_BRIDGE_ERROR", $"code={error.Code}; details={error.Details}");
    }

    private static DocumentOpenedPayload ToOpenedPayload(AxamlImportResult result, bool canEdit) => new()
    {
        CanEdit = canEdit,
        CapabilityLevel = result.CapabilityReport.Level.ToString(),
        Status = canEdit ? "AXAML imported into the shared DesignerSurface." : "AXAML is read-only in this proof of concept.",
        Capabilities = result.CapabilityReport.Entries.Select(entry => new CapabilityEntryPayload
        {
            Subject = entry.Subject,
            Level = entry.Level.ToString(),
            Message = entry.Message
        }).ToList()
    };

    private Task SendAsync<TPayload>(string messageType, DesignerHostEnvelope request, TPayload payload)
    {
        return _connection?.SendAsync(messageType, request.RequestId, request.DocumentId, payload, _cancellation.Token)
            ?? Task.CompletedTask;
    }

    private Task SendErrorAsync(DesignerHostEnvelope request, string code, string message, string details = "") =>
        SendAsync(DesignerHostMessageTypes.Error, request, new ErrorPayload { Code = code, Message = message, Details = details });

    private void SetStatus(string value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _window.SetBridgeStatus(value);
            return;
        }

        Dispatcher.UIThread.Post(() => _window.SetBridgeStatus(value));
    }

    private static void Log(string eventName, string details) =>
        System.Diagnostics.Trace.WriteLine($"{eventName}: {details}");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancellation.Cancel();
        _connection?.Dispose();
        _cancellation.Dispose();
    }
}
