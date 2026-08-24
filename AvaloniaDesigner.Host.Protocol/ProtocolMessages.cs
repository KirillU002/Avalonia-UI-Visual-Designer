using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AvaloniaDesigner.Host.Protocol;

/// <summary>
/// Versioned, local-only contract shared by the VSSDK bridge and the external Avalonia host.
/// It deliberately has no dependency on Avalonia, Visual Studio, Eremex, or Designer assemblies.
/// </summary>
public static class DesignerHostProtocol
{
    public const int CurrentVersion = 1;
    public const string PipePrefix = "AvaloniaDesigner.VsHost";

    public static string ComputeChecksum(string text)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
            builder.Append(value.ToString("X2"));
        return builder.ToString();
    }
}

public static class DesignerHostMessageTypes
{
    public const string Hello = "Hello";
    public const string HelloAck = "HelloAck";
    public const string OpenDocument = "OpenDocument";
    public const string DocumentOpened = "DocumentOpened";
    public const string ApplyDesignerPatch = "ApplyDesignerPatch";
    public const string PatchApplied = "PatchApplied";
    public const string DocumentChanged = "DocumentChanged";
    public const string ReloadDocument = "ReloadDocument";
    public const string ReloadRequested = "ReloadRequested";
    public const string CloseDocument = "CloseDocument";
    public const string HostShutdown = "HostShutdown";
    public const string Error = "Error";
}

public static class DesignerHostPatchGuard
{
    public static bool Matches(OpenDocumentPayload snapshot, ApplyDesignerPatchPayload patch) =>
        snapshot is not null
        && patch is not null
        && snapshot.Version == patch.ExpectedVersion
        && string.Equals(snapshot.Checksum, patch.ExpectedChecksum, StringComparison.Ordinal);
}

public sealed class DesignerHostEnvelope
{
    public int ProtocolVersion { get; set; } = DesignerHostProtocol.CurrentVersion;
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
}

public sealed class HelloPayload
{
    public string ClientName { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public int MinimumProtocolVersion { get; set; } = DesignerHostProtocol.CurrentVersion;
}

public sealed class HelloAckPayload
{
    public string HostName { get; set; } = "AvaloniaDesigner.VsHost";
    public string HostVersion { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; } = DesignerHostProtocol.CurrentVersion;
}

public sealed class OpenDocumentPayload
{
    public string FilePath { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public long Version { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string AvaloniaVersion { get; set; } = string.Empty;
}

public sealed class DocumentOpenedPayload
{
    public bool CanEdit { get; set; }
    public string CapabilityLevel { get; set; } = string.Empty;
    public List<CapabilityEntryPayload> Capabilities { get; set; } = new();
    public string Status { get; set; } = string.Empty;
}

public sealed class CapabilityEntryPayload
{
    public string Subject { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ApplyDesignerPatchPayload
{
    public long ExpectedVersion { get; set; }
    public string ExpectedChecksum { get; set; } = string.Empty;
    public List<TextEditPayload> Edits { get; set; } = new();
}

public sealed class TextEditPayload
{
    public int Start { get; set; }
    public int Length { get; set; }
    public string NewText { get; set; } = string.Empty;
}

public sealed class PatchAppliedPayload
{
    public long Version { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class DocumentChangedPayload
{
    public string Text { get; set; } = string.Empty;
    public long Version { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

public sealed class ErrorPayload
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class EmptyPayload
{
}
