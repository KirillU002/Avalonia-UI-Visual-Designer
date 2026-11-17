using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace UiaDiagnostics;

// Identical source used in archived Designer revisions and the dependency-free
// probe. No Designer API, provider interception, or manual COM reference changes.
internal static class PlainWindowProbe
{
    public static int Run()
    {
        var folder=Path.GetFullPath(Environment.GetEnvironmentVariable("UIA_PROBE_OUTPUT")!);
        Directory.CreateDirectory(folder);
        var cycles=int.Parse(Environment.GetEnvironmentVariable("UIA_PROBE_CYCLES")??"200");
        if(cycles is <1 or >2000)throw new ArgumentOutOfRangeException(nameof(cycles));
        var clientPath=Environment.GetEnvironmentVariable("UIA_STRESS_CLIENT");
        using var client=clientPath is null?null:Start(clientPath);
        if(client is not null)Require(Read(client)=="READY","Client initialization failed.");
        var references=new List<WeakReference>();var timer=Stopwatch.StartNew();
        for(var i=0;i<cycles;i++)
        {
            references.Add(Cycle(client));
            File.AppendAllText(Path.Combine(folder,"cycles.jsonl"),JsonSerializer.Serialize(new{Cycle=i+1,Utc=DateTimeOffset.UtcNow,Pid=Environment.ProcessId})+Environment.NewLine);
        }
        Wait(200);Collect();var connected=references.Count(r=>r.IsAlive);
        if(client is not null)
        {
            client.StandardInput.WriteLine("STOP");client.StandardInput.Flush();
            Await(()=>client.HasExited);Require(client.ExitCode==0,"Client failed.");
        }
        Wait(1000);Collect();
        using var process=Process.GetCurrentProcess();
        var result=new{Completed=cycles,ConnectedAlive=connected,ReleasedAlive=references.Count(r=>r.IsAlive),Client=client is not null,Ambient="uncontrolled",ElapsedMs=timer.ElapsedMilliseconds,
            Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,Avalonia=typeof(Window).Assembly.GetName().Version?.ToString(),process.HandleCount,Threads=process.Threads.Count,process.PrivateMemorySize64,Success=true};
        File.WriteAllText(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        // Opt-in forensic hold, after the unchanged GC checkpoint. A debugger can
        // inspect the retained objects without rerunning the scenario. Requests
        // only collect the weak-reference sample; no provider is modified.
        if (Environment.GetEnvironmentVariable("UIA_PROBE_FORENSIC_HOLD") == "1")
        {
            File.WriteAllText(Path.Combine(folder, "hold-ready.json"), JsonSerializer.Serialize(new { Pid = Environment.ProcessId, result.ReleasedAlive }));
            var hold = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "release.txt")) && hold.Elapsed < TimeSpan.FromMinutes(3))
            {
                var request = Path.Combine(folder, "sample.request");
                if (File.Exists(request))
                {
                    var label = File.ReadAllText(request);
                    File.Delete(request);
                    Collect();
                    File.AppendAllText(Path.Combine(folder, "hold-samples.jsonl"), JsonSerializer.Serialize(new
                    { Utc = DateTimeOffset.UtcNow, Label = label, Alive = references.Count(r => r.IsAlive) }) + Environment.NewLine);
                }
                Wait(20);
            }
        }
        GC.KeepAlive(references);
        return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference Cycle(Process? client)
    {
        var window=new Window{Title="UIA plain lifetime probe",Width=400,Height=300,ShowInTaskbar=false,ShowActivated=false,Content=new TextBox{Text="UIA differential"}};
        if (Environment.GetEnvironmentVariable("UIA_PROBE_WAIT_CLOSE") == "1") window.ShowInTaskbar = true;
        window.Show();Wait(20);window.UpdateLayout();Task<string?>? done=null;
        if (Environment.GetEnvironmentVariable("UIA_PROBE_WAIT_CLOSE") == "1")
        {
            var folder = Path.GetFullPath(Environment.GetEnvironmentVariable("UIA_PROBE_OUTPUT")!);
            File.WriteAllText(Path.Combine(folder, "open-window.json"), JsonSerializer.Serialize(new
            { Pid = Environment.ProcessId, Hwnd = window.TryGetPlatformHandle()!.Handle.ToInt64(), window.Title }));
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "close.request")))
            {
                if (timer.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Forensic close signal missing");
                Wait(20);
            }
        }
        if(client is not null)
        {
            client.StandardInput.WriteLine(window.TryGetPlatformHandle()!.Handle.ToInt64()+" 100");client.StandardInput.Flush();
            Require(Read(client)?.StartsWith("ACQUIRED 00000000")==true,"Node acquisition failed.");done=client.StandardOutput.ReadLineAsync();
        }
        window.Close();
        if(done is not null){Await(()=>done.IsCompleted);Require(done.Result?.StartsWith("DONE ")==true,"Query completion lost.");}
        Require(!window.IsVisible,"Close failed.");return new WeakReference(window);
    }
    private static Process Start(string path)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        start.ArgumentList.Add(Path.GetFullPath(path));start.ArgumentList.Add(Environment.ProcessId.ToString());return Process.Start(start)!;
    }
    private static string? Read(Process client){var read=client.StandardOutput.ReadLineAsync();Await(()=>read.IsCompleted);return read.GetAwaiter().GetResult();}
    private static void Await(Func<bool> ready){var timer=Stopwatch.StartNew();while(!ready()){if(timer.Elapsed>TimeSpan.FromSeconds(20))throw new TimeoutException();Wait(2);}}
    private static void Wait(int ms){using var cancel=new CancellationTokenSource(ms);Dispatcher.UIThread.MainLoop(cancel.Token);}
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
