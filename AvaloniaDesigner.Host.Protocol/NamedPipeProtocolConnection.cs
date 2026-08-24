using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace AvaloniaDesigner.Host.Protocol;

/// <summary>Length-prefixed XML transport for a single trusted local named-pipe connection.</summary>
public sealed class NamedPipeProtocolConnection : IDisposable
{
    private static readonly ConcurrentDictionary<Type, XmlSerializer> Serializers = new();

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public NamedPipeProtocolConnection(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public static NamedPipeServerStream CreateServer(string pipeName) => new(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    public static NamedPipeClientStream CreateClient(string pipeName) => new(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);

    public async Task SendAsync<TPayload>(string messageType, string requestId, string documentId, TPayload payload, CancellationToken cancellationToken = default)
    {
        await SendAsync(new DesignerHostEnvelope
        {
            ProtocolVersion = DesignerHostProtocol.CurrentVersion,
            RequestId = requestId ?? string.Empty,
            DocumentId = documentId ?? string.Empty,
            MessageType = messageType ?? string.Empty,
            Payload = Serialize(payload)
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(DesignerHostEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var bytes = Encoding.UTF8.GetBytes(Serialize(envelope));
        var lengthBytes = BitConverter.GetBytes(bytes.Length);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DesignerHostEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var lengthBuffer = new byte[sizeof(int)];
        if (!await ReadExactlyAsync(lengthBuffer, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer, 0);
        // Windows named pipes can surface a peer close as a zeroed header after connection.
        // Zero is never a valid protocol frame, so treat it as a clean disconnect.
        if (length == 0)
            return null;
        if (length < 0 || length > 16 * 1024 * 1024)
            throw new InvalidDataException($"IPC message has an invalid length: {length}.");

        var body = new byte[length];
        if (!await ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false))
            throw new EndOfStreamException("IPC connection closed in the middle of a message.");

        return Deserialize<DesignerHostEnvelope>(Encoding.UTF8.GetString(body))
            ?? throw new InvalidDataException("IPC message could not be deserialized.");
    }

    public TPayload? GetPayload<TPayload>(DesignerHostEnvelope envelope) =>
        string.IsNullOrWhiteSpace(envelope.Payload)
            ? default
            : Deserialize<TPayload>(envelope.Payload);

    private static string Serialize<T>(T value)
    {
        using var writer = new Utf8StringWriter();
        GetSerializer(typeof(T)).Serialize(writer, value);
        return writer.ToString();
    }

    private static T? Deserialize<T>(string text)
    {
        using var reader = new StringReader(text);
        return (T?)GetSerializer(typeof(T)).Deserialize(reader);
    }

    private static XmlSerializer GetSerializer(Type type) =>
        Serializers.GetOrAdd(type, static item => new XmlSerializer(item));

    private async Task<bool> ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return offset == 0;
            offset += read;
        }
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NamedPipeProtocolConnection));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _writeLock.Dispose();
        _stream.Dispose();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter()
            : base(CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
