using Avalonia.Controls;
using FormDesigner.PluginContracts;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Loader;

namespace FormDesigner.Services;

public sealed record PluginPreviewBuildFailure(
    string ControlType,
    string PluginId,
    string DescriptorType,
    string ExceptionType,
    string Message,
    string StackTrace)
{
    public string UserMessage => string.IsNullOrWhiteSpace(Message)
        ? ExceptionType
        : Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
}

/// <summary>
/// Centralizes preview creation for adapter controls. Hosts keep the fallback UI, while this
/// helper preserves the diagnostics that used to be lost in broad catch blocks.
/// </summary>
public static class PluginPreviewControlBuilder
{
    public static bool TryBuild(
        string host,
        IControlDescriptor descriptor,
        IDesignControlNode control,
        IPreviewContext context,
        out Control? preview,
        out PluginPreviewBuildFailure? failure)
    {
        var isPluginControl = !string.IsNullOrWhiteSpace(control.PluginId);
        if (isPluginControl)
        {
            Debug.WriteLine(
                "PLUGIN_CONTROL_PREVIEW_REQUESTED " +
                $"host={host}; descriptorId={control.DescriptorId}; pluginId={control.PluginId}; modelType={control.TypeKey}");
            Debug.WriteLine(
                "PLUGIN_CONTROL_DESCRIPTOR_RESOLVED " +
                $"host={host}; descriptorId={descriptor.TypeKey}; descriptorClrType={descriptor.GetType().FullName}; " +
                $"pluginVersion={control.PluginVersion}; assemblyLoadContext={GetLoadContextName(descriptor.GetType().Assembly)}");
            Debug.WriteLine(
                "PLUGIN_CONTROL_BUILD_PREVIEW_START " +
                $"host={host}; descriptorId={descriptor.TypeKey}");
        }

        try
        {
            preview = descriptor.BuildPreview(control, context)
                ?? throw new InvalidOperationException($"Descriptor '{descriptor.TypeKey}' returned a null preview control.");
            failure = null;

            if (isPluginControl)
            {
                Debug.WriteLine(
                    "PLUGIN_CONTROL_BUILD_PREVIEW_RETURNED " +
                    $"host={host}; descriptorId={descriptor.TypeKey}; returnedClrType={preview.GetType().FullName}; " +
                    $"baseType={preview.GetType().BaseType?.FullName ?? "-"}; assembly={preview.GetType().Assembly.FullName}; " +
                    $"assemblyLoadContext={GetLoadContextName(preview.GetType().Assembly)}; " +
                    $"width={preview.Width}; height={preview.Height}");
                LogAvaloniaAssemblyIdentity(host, descriptor, preview);
            }

            return true;
        }
        catch (Exception ex)
        {
            preview = null;
            failure = new PluginPreviewBuildFailure(
                control.TypeKey,
                control.PluginId,
                descriptor.GetType().FullName ?? descriptor.TypeKey,
                ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message,
                ex.ToString());

            if (isPluginControl)
            {
                Debug.WriteLine(
                    "PLUGIN_CONTROL_BUILD_PREVIEW_FAILED " +
                    $"host={host}; descriptorId={descriptor.TypeKey}; exceptionType={failure.ExceptionType}; " +
                    $"message={failure.Message}; stackTrace={failure.StackTrace}");
            }

            return false;
        }
    }

    public static void LogPlaceholderReplacement(string host, PluginPreviewBuildFailure? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.PluginId))
            return;

        Debug.WriteLine(
            "PLUGIN_CONTROL_PREVIEW_REPLACED_WITH_PLACEHOLDER " +
            $"host={host}; controlType={failure.ControlType}; pluginId={failure.PluginId}; " +
            $"exactReason={failure.ExceptionType}:{failure.UserMessage}");
    }

    private static void LogAvaloniaAssemblyIdentity(string host, IControlDescriptor descriptor, Control preview)
    {
        var hostAssembly = typeof(Control).Assembly;
        var avaloniaReference = descriptor.GetType().Assembly
            .GetReferencedAssemblies()
            .FirstOrDefault(reference => string.Equals(reference.Name, hostAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase));
        var previewControlBase = FindAvaloniaControlBase(preview.GetType());
        var sameRuntimeAssembly = previewControlBase is not null
            && ReferenceEquals(previewControlBase.Assembly, hostAssembly);

        Debug.WriteLine(
            "PLUGIN_AVALONIA_ASSEMBLY_IDENTITY " +
            $"host={host}; hostAssembly={hostAssembly.FullName}; pluginReferencedAssembly={avaloniaReference?.FullName ?? "-"}; " +
            $"sameRuntimeAssembly={sameRuntimeAssembly}; hostAlc={GetLoadContextName(hostAssembly)}; " +
            $"pluginAlc={GetLoadContextName(descriptor.GetType().Assembly)}; " +
            $"returnedControlAlc={GetLoadContextName(preview.GetType().Assembly)}");
    }

    private static Type? FindAvaloniaControlBase(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, typeof(Control).FullName, StringComparison.Ordinal))
                return current;
        }

        return null;
    }

    private static string GetLoadContextName(System.Reflection.Assembly assembly)
    {
        return AssemblyLoadContext.GetLoadContext(assembly)?.Name ?? "Default";
    }
}
