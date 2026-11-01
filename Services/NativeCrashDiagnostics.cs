using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace FormDesigner.Services;

// Value-only, opt-in journal. Native AVs are not intercepted/recovered here:
// Windows WER or an external debugger must capture the crash dump.
public static class NativeCrashDiagnostics
{
    private static readonly object Gate=new();
    private static StreamWriter? _writer;
    private static int _initialized, _modulesCaptured;
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    public static void InitializeFromEnvironment()
    {
        if(Interlocked.Exchange(ref _initialized,1)!=0)return;
        var folder=Environment.GetEnvironmentVariable("FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY");
        if(string.IsNullOrWhiteSpace(folder))return;
        try
        {
            Directory.CreateDirectory(folder);
            _writer=new StreamWriter(Path.Combine(folder,$"process-{Environment.ProcessId}.jsonl"),append:false){AutoFlush=true};
            Write("PROCESS_STARTED");
            AppDomain.CurrentDomain.UnhandledException+=(_,e)=>Write("MANAGED_UNHANDLED",reason:e.ExceptionObject is Exception ex?$"{ex.GetType().FullName}; HRESULT=0x{ex.HResult:X8}; {ex.StackTrace}":"non-Exception");
            AppDomain.CurrentDomain.ProcessExit+=(_,_)=>
            {
                Write("PROCESS_EXIT",reason:$"ExitCode={Environment.ExitCode}");
                lock(Gate){try{_writer?.Dispose();}catch(IOException){}finally{_writer=null;}}
            };
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){Debug.WriteLine("Native diagnostic journal unavailable: "+ex.GetType().Name);}
    }
    public static void Write(string code,string previewId="",string mode="",int activeWindows=0,string reason="")
    {
        if(_writer is null)return;
        lock(Gate)
        {
            try
            {
                if(_writer is null||_writer.BaseStream.Position>2*1024*1024)return;
                _writer.WriteLine(JsonSerializer.Serialize(new {Utc=DateTimeOffset.UtcNow,Pid=Environment.ProcessId,
                    NativeThreadId=OperatingSystem.IsWindows()?GetCurrentThreadId():0,Code=code,PreviewId=previewId,Mode=mode,ActiveWindows=activeWindows,Reason=reason}));
            }
            catch(Exception ex) when(ex is IOException or ObjectDisposedException){Debug.WriteLine("Native diagnostic write failed: "+ex.GetType().Name);}
        }
    }
    public static void CaptureModules(string reason)
    {
        if(_writer is null||Interlocked.Exchange(ref _modulesCaptured,1)!=0)return;
        try
        {
            using var process=Process.GetCurrentProcess();
            var modules=process.Modules.Cast<ProcessModule>().Select(m=>new {Name=m.ModuleName,Version=m.FileVersionInfo.FileVersion}).ToArray();
            Write("NATIVE_MODULE_SNAPSHOT",reason:JsonSerializer.Serialize(new {reason,Runtime=RuntimeInformation.FrameworkDescription,OS=RuntimeInformation.OSDescription,Modules=modules}));
        }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException){Write("MODULE_SNAPSHOT_FAILED",reason:ex.GetType().Name);}
    }
}
