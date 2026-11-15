using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

// Launch-only, process-local native debugger. No attach to arbitrary user PIDs,
// WER registry changes, exception recovery, retries, or production dependency.
internal static class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Startup { public uint Size; public string? Reserved, Desktop, Title; public uint X,Y,W,H,CX,CY,Fill,Flags; public ushort Show, ReservedSize; public IntPtr Reserved2, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }
    [StructLayout(LayoutKind.Sequential, Pack=4)] private struct ExceptionInfo { public uint Tid; public IntPtr Pointers; [MarshalAs(UnmanagedType.Bool)] public bool ClientPointers; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcessW(string exe,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string? cwd,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool WaitForDebugEventEx(IntPtr data,uint timeout);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ContinueDebugEvent(uint pid,uint tid,uint status);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenThread(uint rights,bool inherit,uint tid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetThreadContext(IntPtr thread,IntPtr context);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr targetProcess,out IntPtr target,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern uint GetFinalPathNameByHandleW(IntPtr file,StringBuilder path,uint count,uint flags);
    [DllImport("dbghelp.dll",SetLastError=true)] private static extern bool MiniDumpWriteDump(IntPtr process,uint pid,IntPtr file,uint type,ref ExceptionInfo exception,IntPtr streams,IntPtr callback);
    [DllImport("ntdll.dll")] private static extern void RtlFillMemory(IntPtr destination,nuint count,byte fill);

    private static int Main(string[] args)
    {
        if(args is ["--fault"]) { RtlFillMemory(IntPtr.Zero,1,0x42); return 99; }
        try { return Run(args); }
        catch(Exception error) { Console.Error.WriteLine(error); return 2; }
    }
    private static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Windows x64 required.");
        if (args.Length < 4 || args[2] != "--") throw new ArgumentException("output-directory executable -- argument ...");
        var folder=Path.GetFullPath(args[0]);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("Use a fresh capture directory.");
        Directory.CreateDirectory(folder);
        var executable=Path.GetFullPath(args[1]);
        var selfTest=args.Skip(3).Contains("--fault");
        var command=new StringBuilder(string.Join(" ",new[]{executable}.Concat(args.Skip(3)).Select(Quote)));
        var inherited=new List<IntPtr>();
        IntPtr Standard(int kind)
        {
            var original=GetStdHandle(kind);if(original==IntPtr.Zero||original==new IntPtr(-1))return IntPtr.Zero;
            Check(DuplicateHandle(new IntPtr(-1),original,new IntPtr(-1),out var copy,0,true,2));inherited.Add(copy);return copy;
        }
        ProcessInfo child;
        try
        {
            var startup=new Startup { Size=(uint)Marshal.SizeOf<Startup>(),Flags=0x100,Input=Standard(-10),Output=Standard(-11),Error=Standard(-12) };
            Check(CreateProcessW(executable,command,IntPtr.Zero,IntPtr.Zero,true,0x08000002,IntPtr.Zero,null,ref startup,out child)); // DEBUG_ONLY_THIS_PROCESS | CREATE_NO_WINDOW
        }
        finally {foreach(var handle in inherited)CloseHandle(handle);}
        var data=Marshal.AllocHGlobal(176);
        var captured=false; var exited=false; var timeout=false; var initialBreakpoint=false; var exitCode=0;
        var modules=new List<object>(); ulong uiaStart=0,uiaEnd=0;
        var timer=Stopwatch.StartNew();
        var limit=int.Parse(Environment.GetEnvironmentVariable("UIA_CAPTURE_TIMEOUT_SECONDS")??"300");
        Write("LAUNCHED",new {child.Pid,Executable=Path.GetFileName(executable),OS=RuntimeInformation.OSDescription,Runtime=RuntimeInformation.FrameworkDescription,Debugger=true});
        try
        {
            while(!exited)
            {
                if(!timeout&&timer.Elapsed.TotalSeconds>limit) { timeout=true; Check(TerminateProcess(child.Process,0xDEAD)); }
                if(!WaitForDebugEventEx(data,1000)) { if(Marshal.GetLastWin32Error()==121) continue; throw new Win32Exception(); }
                var kind=(uint)Marshal.ReadInt32(data); var pid=(uint)Marshal.ReadInt32(data,4); var tid=(uint)Marshal.ReadInt32(data,8);
                if(pid!=child.Pid) throw new InvalidOperationException("Unexpected debuggee PID.");
                uint disposition=0x00010002; // DBG_CONTINUE
                if(kind is 3 or 6)
                {
                    var file=Marshal.ReadIntPtr(data,16);
                    var baseAddress=(ulong)Marshal.ReadInt64(data,kind==3?40:24);
                    var path=new StringBuilder(32768);
                    if(file!=IntPtr.Zero && GetFinalPathNameByHandleW(file,path,(uint)path.Capacity,0)!=0)
                    {
                        var name=path.ToString(); modules.Add(new {Base=$"0x{baseAddress:X}",Path=name});
                        if(name.EndsWith("uiautomationcore.dll",StringComparison.OrdinalIgnoreCase))
                        {
                            // LOAD_DLL is delivered before Process.Modules necessarily
                            // exposes the pending mapping. Read its image size from PE.
                            using var image=File.OpenRead(name);using var pe=new PEReader(image);
                            uiaStart=baseAddress; uiaEnd=baseAddress+(uint)pe.PEHeaders.PEHeader!.SizeOfImage;
                            var version=FileVersionInfo.GetVersionInfo(name);
                            Write("UIA_LOADED",new {Base=$"0x{uiaStart:X}",End=$"0x{uiaEnd:X}",Version=$"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}",DisplayVersion=version.FileVersion});
                        }
                    }
                    if(file!=IntPtr.Zero) CloseHandle(file);
                }
                if(kind==1)
                {
                    var code=(uint)Marshal.ReadInt32(data,16); var address=(ulong)Marshal.ReadInt64(data,32); var first=Marshal.ReadInt32(data,168)!=0;
                    disposition=0x80010001;
                    if(code==0x80000003&&!initialBreakpoint){initialBreakpoint=true;disposition=0x00010002;}
                    if(code==0xC0000005 && !captured && (selfTest || address>=uiaStart && address<uiaEnd || !first))
                    {
                        var dump=Path.Combine(folder,$"av-{pid}-{tid}.dmp");
                        WriteFullDump(dump,child.Process,pid,tid,data);
                        captured=true;
                        File.WriteAllText(Path.Combine(folder,"modules.json"),JsonSerializer.Serialize(modules,new JsonSerializerOptions{WriteIndented=true}));
                        Write("FULL_DUMP_WRITTEN",new {pid,tid,FirstChance=first,Code=$"0x{code:X}",Address=$"0x{address:X}",Offset=uiaStart==0?null:$"0x{address-uiaStart:X}",Dump=dump,Bytes=new FileInfo(dump).Length});
                    }
                }
                if(kind==5) { exitCode=Marshal.ReadInt32(data,16); exited=true; }
                Check(ContinueDebugEvent(pid,tid,disposition));
            }
            var result=new {Pid=child.Pid,ExitCode=exitCode,TimedOut=timeout,Captured=captured,ElapsedMs=timer.ElapsedMilliseconds};
            File.WriteAllText(Path.Combine(folder,"modules.json"),JsonSerializer.Serialize(modules,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Write("EXIT",result); return timeout?124:exitCode;
        }
        finally { if(!exited)TerminateProcess(child.Process,0xDEAD); Marshal.FreeHGlobal(data); CloseHandle(child.Thread); CloseHandle(child.Process); }
        void Write(string code,object value) => File.AppendAllText(Path.Combine(folder,"capture.jsonl"),JsonSerializer.Serialize(new{Code=code,Utc=DateTimeOffset.UtcNow,Value=value})+Environment.NewLine);
    }
    private static void WriteFullDump(string file,IntPtr process,uint pid,uint tid,IntPtr debugEvent)
    {
        var thread=OpenThread(0x0048,false,tid); if(thread==IntPtr.Zero) throw new Win32Exception();
        var storage=Marshal.AllocHGlobal(1250); var record=Marshal.AllocHGlobal(152); var pointers=Marshal.AllocHGlobal(16);
        try
        {
            var context=new IntPtr((storage.ToInt64()+15)&~15L);
            Marshal.Copy(new byte[1232],0,context,1232); Marshal.WriteInt32(context,48,0x0010001F);
            Check(GetThreadContext(thread,context));
            var bytes=new byte[152]; Marshal.Copy(debugEvent+16,bytes,0,152); Marshal.Copy(bytes,0,record,152);
            Marshal.WriteIntPtr(pointers,record); Marshal.WriteIntPtr(pointers,8,context);
            var exception=new ExceptionInfo {Tid=tid,Pointers=pointers,ClientPointers=false};
            using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            Check(MiniDumpWriteDump(process,pid,output.SafeFileHandle.DangerousGetHandle(),0x1926,ref exception,IntPtr.Zero,IntPtr.Zero)); // full memory + handles + unloaded + thread/process/memory info
        }
        finally {Marshal.FreeHGlobal(pointers);Marshal.FreeHGlobal(record);Marshal.FreeHGlobal(storage);CloseHandle(thread);}
    }
    private static void Check(bool ok) {if(!ok)throw new Win32Exception();}
    private static string Quote(string value)
    {
        var result=new StringBuilder("\"");var slashes=0;
        foreach(var c in value)
        {
            if(c=='\\'){slashes++;continue;}
            result.Append('\\',c=='"'?slashes*2+1:slashes);slashes=0;result.Append(c);
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
}
