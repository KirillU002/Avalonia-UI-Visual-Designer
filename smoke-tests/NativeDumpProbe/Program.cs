using System.Runtime.InteropServices;

// Read-only DbgEng client for existing Windows crash dumps, without installing a
// debugger or loading anything into the Designer. Vtable indices are from SDK dbgeng.h.
public static class Program
{
    [DllImport("dbgeng.dll")] private static extern int DebugCreate(ref Guid iid,out IntPtr client);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Query(IntPtr self,ref Guid iid,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Open(IntPtr self,[MarshalAs(UnmanagedType.LPStr)]string path);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Callback(IntPtr self,IntPtr callback);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Wait(IntPtr self,uint flags,uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Execute(IntPtr self,uint output,[MarshalAs(UnmanagedType.LPStr)]string command,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int End(IntPtr self,uint flags);
    private static T Method<T>(IntPtr obj,int index) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),index*IntPtr.Size));
    [ComVisible(true),Guid("4bf58045-d654-4c40-b0af-683090f356dc"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOutput{[PreserveSig]int Output(uint mask,[MarshalAs(UnmanagedType.LPStr)]string text);}
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)] public sealed class OutputSink:IOutput
    {public int Output(uint mask,string text){Console.Write(text);return 0;}}
    public static void Main(string[] args)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        if(args.Length!=2)throw new ArgumentException("dump path, symbol cache path");
        var iid=new Guid("27fe5639-8407-4f47-8364-ee118fb08ac8");Marshal.ThrowExceptionForHR(DebugCreate(ref iid,out var client));
        IntPtr control=default,callback=default;var sink=new OutputSink();
        try
        {
            iid=new Guid("5182e668-105e-416e-ad92-24ef800424ba");Marshal.ThrowExceptionForHR(Method<Query>(client,0)(client,ref iid,out control));
            callback=Marshal.GetComInterfaceForObject(sink,typeof(IOutput));Marshal.ThrowExceptionForHR(Method<Callback>(client,34)(client,callback));
            Marshal.ThrowExceptionForHR(Method<Open>(client,19)(client,Path.GetFullPath(args[0])));
            Marshal.ThrowExceptionForHR(Method<Wait>(control,93)(control,0,15000));
            var execute=Method<Execute>(control,66);
            Marshal.ThrowExceptionForHR(execute(control,0,".sympath srv*"+Path.GetFullPath(args[1])+"*https://msdl.microsoft.com/download/symbols",0));
            foreach(var command in new[]{".ecxr",".exr -1","k 40","lmv m uiautomationcore","~* k 12","!analyze -v"})
            {Console.WriteLine("\nPROBE_COMMAND "+command);Marshal.ThrowExceptionForHR(execute(control,0,command,0));}
        }
        finally
        {
            if(control!=IntPtr.Zero)Marshal.Release(control);
            Method<Callback>(client,34)(client,IntPtr.Zero);
            Method<End>(client,26)(client,0);
            Marshal.Release(client);if(callback!=IntPtr.Zero)Marshal.Release(callback);GC.KeepAlive(sink);
        }
    }
}
