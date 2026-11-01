using FormDesigner.Views;
using System.Runtime.InteropServices;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    [DllImport("uiautomationcore.dll")]private static extern int UiaNodeFromHandle(IntPtr hwnd,out IntPtr node);
    [DllImport("uiautomationcore.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool UiaNodeRelease(IntPtr node);
    private static void AttachMemoryUiaClient(PreviewWindow window)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        var hwnd=window.TryGetPlatformHandle()!.Handle;
        // UIA clients query from an MTA thread, not from the provider's UI thread.
        var query=Task.Run(()=>{var hr=UiaNodeFromHandle(hwnd,out var node);return (hr,node);});
        try{DropPumpUntil(()=>query.IsCompleted,"external UIA node acquisition");}
        catch{_ = query.ContinueWith(t=>{if(t.IsCompletedSuccessfully&&t.Result.node!=IntPtr.Zero)UiaNodeRelease(t.Result.node);});throw;}
        var (hr,node)=query.GetAwaiter().GetResult();Marshal.ThrowExceptionForHR(hr);
        RequireCapability(node!=IntPtr.Zero,"UIA returned an empty node.");
        var id=window.PreviewId;
        FormDesigner.Services.NativeCrashDiagnostics.Write("TEST_UIA_NODE_ACQUIRED",id);
        // Only the native node value is captured. The task never retains a Window.
        window.Closed+=(_,_)=>{_ = Task.Run(()=>{var released=UiaNodeRelease(node);FormDesigner.Services.NativeCrashDiagnostics.Write(released?"TEST_UIA_NODE_RELEASED":"TEST_UIA_NODE_RELEASE_FAILED",id);});};
    }
}
