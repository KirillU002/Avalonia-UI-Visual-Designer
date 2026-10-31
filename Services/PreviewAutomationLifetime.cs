using Avalonia.Automation.Peers;
using Avalonia.Controls;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FormDesigner.Services;

/// <summary>Per-closed-window UIA release for the pinned Avalonia Win32 11.1.1 backend.</summary>
internal static class PreviewAutomationLifetime
{
    private sealed record Api(FieldInfo Nodes, MethodInfo FindNode, MethodInfo Disconnect);
    private static readonly Lazy<Api> Win32Api = new(ResolveApi);

    [DllImport("user32.dll")]
    private static extern uint InSendMessageEx(IntPtr reserved);

    public static bool MustDeferClose(Control window) => OperatingSystem.IsWindows()
        && ControlAutomationPeer.FromElement(window) is not null && InSendMessageEx(IntPtr.Zero) != 0;

    public static Action? CaptureDisconnect(Control window)
    {
        if (!OperatingSystem.IsWindows() || ControlAutomationPeer.FromElement(window) is not { } peer) return null;
        var api = Win32Api.Value;
        var table = api.Nodes.GetValue(null)!;
        var arguments = new object?[] { peer, null };
        // Do not create an automation peer/provider just to tear it down.
        if (api.FindNode.Invoke(table, arguments) is not true || arguments[1] is not { } provider) return null;
        return () => Marshal.ThrowExceptionForHR((int)api.Disconnect.Invoke(null, new[] { provider })!);
    }

    private static Api ResolveApi()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Avalonia.Win32");
        if (assembly.GetName().Version != new Version(11, 1, 1, 0))
            throw new NotSupportedException("Revalidate Preview UIA release for the new Avalonia Win32 version.");
        // 11.1.1 WM_DESTROY calls UiaReturnRawElementProvider(..., null), but does not
        // disconnect providers retained by external clients. There is no public per-window API.
        var node = assembly.GetType("Avalonia.Win32.Automation.AutomationNode", throwOnError: true)!;
        var nodes = node.GetField("s_nodes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(node.FullName, "s_nodes");
        var find = nodes.FieldType.GetMethod("TryGetValue")!;
        var api = assembly.GetType("Avalonia.Win32.Interop.Automation.UiaCoreProviderApi", throwOnError: true)!;
        return new(nodes, find, api.GetMethod("UiaDisconnectProvider", BindingFlags.Public | BindingFlags.Static)!);
    }
}
