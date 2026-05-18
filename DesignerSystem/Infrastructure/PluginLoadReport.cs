using System;
using System.Collections.Generic;
using System.IO;

namespace FormDesigner.DesignerSystem.Infrastructure;

public enum PluginLoadStatus
{
    Ok,
    Warning,
    Error,
    Skipped
}

public sealed class PluginLoadReport
{
    public string AssemblyPath { get; init; } = "";
    public string PluginId { get; init; } = "";
    public string PluginTitle { get; init; } = "";
    public string PluginVersion { get; init; } = "";
    public string ApiVersion { get; init; } = "";
    public PluginLoadStatus Status { get; init; } = PluginLoadStatus.Ok;
    public string Message { get; init; } = "";
    public IReadOnlyList<string> RegisteredControls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public string AssemblyFileName => string.IsNullOrWhiteSpace(AssemblyPath)
        ? "unknown"
        : Path.GetFileName(AssemblyPath);

    public int ControlCount => RegisteredControls.Count;

    public bool HasPluginIdentity => !string.IsNullOrWhiteSpace(PluginId)
        || !string.IsNullOrWhiteSpace(PluginTitle);

    public string DisplayName => !string.IsNullOrWhiteSpace(PluginTitle)
        ? PluginTitle
        : !string.IsNullOrWhiteSpace(PluginId)
            ? PluginId
            : AssemblyFileName;

    public string StatusTitle => Status switch
    {
        PluginLoadStatus.Ok => "OK",
        PluginLoadStatus.Warning => "Warning",
        PluginLoadStatus.Error => "Error",
        _ => "Skipped"
    };
}

