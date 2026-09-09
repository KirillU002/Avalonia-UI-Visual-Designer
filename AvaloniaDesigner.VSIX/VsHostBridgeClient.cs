using AvaloniaDesigner.Host.Protocol;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

/// <summary>
/// VSSDK-side bridge only. It owns a local named-pipe connection and a child process, never
/// references or loads Avalonia, Eremex, DesignerSurface, or visual plugin assemblies.
/// </summary>
internal sealed class VsHostBridgeClient : IDisposable
{
    private readonly AsyncPackage _package;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DesignerHostEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cancellation = new();
    private NamedPipeProtocolConnection? _connection;
    private Process? _process;
    private string _pipeName = string.Empty;
    private bool _disposed;

    public VsHostBridgeClient(AsyncPackage package) => _package = package;

    public event Func<ApplyDesignerPatchPayload, Task>? PatchReceived;
    public event Func<Task>? ReloadRequested;
    public event Action<string>? Log;
    public event Action? Disconnected;

    public async Task EnsureConnectedAsync()
    {
        if (_connection is not null)
        {
            Log?.Invoke("VSIX_VSHOST_REUSE");
            return;
        }

        var restarting = _process is not null;
        _pipeName = $"{DesignerHostProtocol.PipePrefix}.{Process.GetCurrentProcess().Id}.{Guid.NewGuid():N}";
        Log?.Invoke("VSIX_VSHOST_LOOKUP");
        var executable = ResolveVsHostExecutable();
        Log?.Invoke($"{(restarting ? "VSIX_VSHOST_RESTART" : "VSIX_VSHOST_START")} path={executable}");
        _process = Process.Start(new ProcessStartInfo
        {
                FileName = executable,
                Arguments = $"--pipe \"{_pipeName}\"",
                WorkingDirectory = Path.GetDirectoryName(executable)
                    ?? throw new InvalidOperationException("Avalonia Designer host directory is unavailable."),
                UseShellExecute = false,
                CreateNoWindow = false
        }) ?? throw new InvalidOperationException("Не удалось запустить AvaloniaDesigner.VsHost.exe.");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => OnDisconnected("VSIX_HOST_DISCONNECTED process exited");

        var client = NamedPipeProtocolConnection.CreateClient(_pipeName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        Log?.Invoke("VSIX_IPC_CONNECT_START");
        await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
        _connection = new NamedPipeProtocolConnection(client);
        _ = ReceiveLoopAsync();
        Log?.Invoke("VSIX_IPC_CONNECTED");

        var ack = await SendRequestAsync(DesignerHostMessageTypes.Hello, string.Empty, new HelloPayload
        {
            ClientName = "AvaloniaDesigner.VSIX",
            ClientVersion = typeof(VsHostBridgeClient).Assembly.GetName().Version?.ToString() ?? "0.1"
        }).ConfigureAwait(false);
        if (!string.Equals(ack.MessageType, DesignerHostMessageTypes.HelloAck, StringComparison.Ordinal))
            throw new InvalidOperationException("VsHost did not acknowledge the protocol handshake.");
        Log?.Invoke("VSIX_HOST_READY");
    }

    public async Task<DocumentOpenedPayload> OpenDocumentAsync(VsDocumentSnapshot snapshot)
    {
        await EnsureConnectedAsync().ConfigureAwait(false);
        Log?.Invoke($"VSIX_OPEN_DOCUMENT_SENT path={snapshot.FilePath}; version={snapshot.Version}");
        var response = await SendRequestAsync(DesignerHostMessageTypes.OpenDocument, snapshot.DocumentId, new OpenDocumentPayload
        {
            FilePath = snapshot.FilePath,
            Text = snapshot.Text,
            Version = snapshot.Version,
            Checksum = snapshot.Checksum
        }).ConfigureAwait(false);

        if (string.Equals(response.MessageType, DesignerHostMessageTypes.Error, StringComparison.Ordinal))
            throw new InvalidOperationException(_connection!.GetPayload<ErrorPayload>(response)?.Message ?? "VsHost failed to open AXAML.");

        Log?.Invoke($"VSIX_OPEN_DOCUMENT path={snapshot.FilePath}; version={snapshot.Version}");
        return _connection!.GetPayload<DocumentOpenedPayload>(response)
            ?? throw new InvalidOperationException("VsHost did not return DocumentOpened payload.");
    }

    public async Task ReloadDocumentAsync(VsDocumentSnapshot snapshot)
    {
        await EnsureConnectedAsync().ConfigureAwait(false);
        await SendRequestAsync(DesignerHostMessageTypes.ReloadDocument, snapshot.DocumentId, new OpenDocumentPayload
        {
            FilePath = snapshot.FilePath,
            Text = snapshot.Text,
            Version = snapshot.Version,
            Checksum = snapshot.Checksum
        }).ConfigureAwait(false);
    }

    private async Task<DesignerHostEnvelope> SendRequestAsync<TPayload>(string messageType, string documentId, TPayload payload)
    {
        var connection = _connection ?? throw new InvalidOperationException("VsHost is not connected.");
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<DesignerHostEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Could not create IPC request.");

        try
        {
            await connection.SendAsync(messageType, requestId, documentId, payload, _cancellation.Token).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested && _connection is not null)
            {
                var envelope = await _connection.ReceiveAsync(_cancellation.Token).ConfigureAwait(false);
                if (envelope is null)
                    break;

                if (_pending.TryRemove(envelope.RequestId, out var completion))
                {
                    completion.TrySetResult(envelope);
                    continue;
                }

                if (string.Equals(envelope.MessageType, DesignerHostMessageTypes.ApplyDesignerPatch, StringComparison.Ordinal))
                {
                    var patch = _connection.GetPayload<ApplyDesignerPatchPayload>(envelope);
                    if (patch is not null)
                        await HandlePatchAsync(envelope, patch).ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(envelope.MessageType, DesignerHostMessageTypes.ReloadRequested, StringComparison.Ordinal))
                {
                    var reload = ReloadRequested;
                    if (reload is not null)
                        _ = reload();
                    continue;
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log?.Invoke($"VSIX_HOST_DISCONNECTED {ex}");
        }
        finally
        {
            OnDisconnected("VSIX_HOST_DISCONNECTED pipe closed");
        }
    }

    private async Task HandlePatchAsync(DesignerHostEnvelope envelope, ApplyDesignerPatchPayload patch)
    {
        Log?.Invoke($"VSIX_PATCH_RECEIVED document={envelope.DocumentId}; edits={patch.Edits.Count}; version={patch.ExpectedVersion}");
        var handler = PatchReceived;
        if (handler is null)
        {
            await SendErrorAsync(envelope, "PATCH_HANDLER_UNAVAILABLE", "Visual Studio bridge cannot apply a Designer patch.").ConfigureAwait(false);
            return;
        }

        try
        {
            await handler(patch).ConfigureAwait(false);
        }
        catch (VsSourceVersionConflictException ex)
        {
            Log?.Invoke($"VSIX_VERSION_CONFLICT {ex.Message}");
            await SendErrorAsync(envelope, "SOURCE_VERSION_CONFLICT", ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SendErrorAsync(envelope, "PATCH_APPLY_FAILED", ex.Message, ex.ToString()).ConfigureAwait(false);
        }
    }

    public Task SendPatchAppliedAsync(string requestId, string documentId, VsDocumentSnapshot snapshot)
    {
        Log?.Invoke($"VSIX_PATCH_APPLIED document={snapshot.FilePath}; version={snapshot.Version}");
        return _connection?.SendAsync(DesignerHostMessageTypes.PatchApplied, requestId, documentId, new PatchAppliedPayload
        {
            Version = snapshot.Version,
            Checksum = snapshot.Checksum,
            Text = snapshot.Text
        }, _cancellation.Token) ?? Task.CompletedTask;
    }

    private Task SendErrorAsync(DesignerHostEnvelope request, string code, string message, string details = "") =>
        _connection?.SendAsync(DesignerHostMessageTypes.Error, request.RequestId, request.DocumentId, new ErrorPayload
        {
            Code = code,
            Message = message,
            Details = details
        }, _cancellation.Token) ?? Task.CompletedTask;

    private string ResolveVsHostExecutable()
    {
        var extensionAssemblyPath = typeof(VsHostBridgeClient).Assembly.Location;
        Log?.Invoke($"VSIX_EXTENSION_ASSEMBLY_LOCATION path={extensionAssemblyPath}");

        var extensionRoot = Path.GetDirectoryName(extensionAssemblyPath);
        if (string.IsNullOrWhiteSpace(extensionRoot))
            throw new InvalidOperationException("Avalonia Designer extension root cannot be resolved from its assembly location.");

        Log?.Invoke($"VSIX_EXTENSION_ROOT_RESOLVED path={extensionRoot}");
        var bundled = GetBundledVsHostExecutablePath(extensionAssemblyPath);
        Log?.Invoke($"VSIX_VSHOST_EXPECTED_PATH path={bundled}");
        var exists = File.Exists(bundled);
        Log?.Invoke($"VSIX_VSHOST_EXISTS exists={exists}");
        if (exists)
            return bundled;

        LogBundledHostDirectoryContents(extensionRoot);
        throw new FileNotFoundException("Avalonia Designer host is not found inside the installed extension.", bundled);
    }

    internal static string GetBundledVsHostExecutablePath(string extensionAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(extensionAssemblyPath))
            throw new ArgumentException("Extension assembly location is required.", nameof(extensionAssemblyPath));

        var extensionRoot = Path.GetDirectoryName(extensionAssemblyPath);
        if (string.IsNullOrWhiteSpace(extensionRoot))
            throw new ArgumentException("Extension assembly location does not contain a directory.", nameof(extensionAssemblyPath));

        return Path.Combine(extensionRoot, "VsHost", "AvaloniaDesigner.VsHost.exe");
    }

    private void LogBundledHostDirectoryContents(string extensionRoot)
    {
        try
        {
            var hostDirectory = Path.Combine(extensionRoot, "VsHost");
            if (!Directory.Exists(hostDirectory))
            {
                Log?.Invoke("VSIX_VSHOST_CONTENTS exists=false");
                return;
            }

            var names = Directory.EnumerateFiles(hostDirectory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(25);
            Log?.Invoke($"VSIX_VSHOST_CONTENTS exists=true files={string.Join(",", names)}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"VSIX_VSHOST_CONTENTS_FAILED {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDisconnected(string details)
    {
        if (_connection is null)
            return;
        _connection.Dispose();
        _connection = null;
        foreach (var item in _pending.Values)
            item.TrySetException(new InvalidOperationException("Avalonia Designer disconnected."));
        Disconnected?.Invoke();
        Log?.Invoke(details);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancellation.Cancel();
        _connection?.Dispose();
        _process?.Dispose();
        _cancellation.Dispose();
    }
}

internal sealed class VsSourceVersionConflictException : Exception
{
    public VsSourceVersionConflictException(string message)
        : base(message)
    {
    }
}
