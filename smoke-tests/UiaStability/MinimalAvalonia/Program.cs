using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    internal static string Folder = "";
    internal static int ExitCode;
    [STAThread] private static int Main(string[] args)
    {
        Folder=Path.GetFullPath(Environment.GetEnvironmentVariable("UIA_PROBE_OUTPUT")??"artifacts/universal-phase3b2/minimal");
        Directory.CreateDirectory(Folder);
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            try { Log("MANAGED_UNHANDLED", new { Exception = error.ExceptionObject.ToString(), error.IsTerminating }); }
            catch (IOException) { }
        };
        if(args.Contains("--uia-differential"))
        {
            AppBuilder.Configure<ProbeApp>().UsePlatformDetect().SetupWithoutStarting();
            return UiaDiagnostics.PlainWindowProbe.Run();
        }
        AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        return ExitCode;
    }
    internal static void Log(string code,object value) => File.AppendAllText(Path.Combine(Folder,"lifetime.jsonl"),JsonSerializer.Serialize(new {Utc=DateTimeOffset.UtcNow,Pid=Environment.ProcessId,Tid=GetCurrentThreadId(),Code=code,Value=value})+Environment.NewLine);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint InSendMessageEx(IntPtr value);

    internal static async Task Run(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var kind=Environment.GetEnvironmentVariable("UIA_PROBE_CONTROL")??"TextBox";
        var close=Environment.GetEnvironmentVariable("UIA_PROBE_CLOSE")??"normal";
        var count=int.Parse(Environment.GetEnvironmentVariable("UIA_PROBE_CYCLES")??"200");
        if(count is <1 or >2000 || close is not ("normal" or "root-disconnect"))throw new ArgumentException("Invalid diagnostic options.");
        var clientPath=Environment.GetEnvironmentVariable("UIA_STRESS_CLIENT");
        using var client=clientPath is null?null:StartClient(clientPath);
        if(client is not null && await client.StandardOutput.ReadLineAsync()!="READY")throw new Exception("UIA client initialization failed.");
        var weak=new List<WeakReference>();var watch=Stopwatch.StartNew();
        Log("START",new {kind,close,count,Scope=Environment.GetEnvironmentVariable("UIA_PROBE_SCOPE")??"owner",Client=client is not null,Ambient="uncontrolled",Runtime=RuntimeInformation.FrameworkDescription,OS=RuntimeInformation.OSDescription,Avalonia=typeof(Window).Assembly.GetName().Version?.ToString(),Apartment=Thread.CurrentThread.GetApartmentState().ToString()});
        try
        {
            for(var i=0;i<count;i++)
            {
                Log("CYCLE_START",new{i});weak.Add(await Cycle(lifetime,kind,close,client,i));Log("CYCLE_END",new{i});
                if((i+1)%25==0){Collect();using var p=Process.GetCurrentProcess();Log("CHECKPOINT",new{Cycle=i+1,Alive=weak.Count(r=>r.IsAlive),Managed=GC.GetTotalMemory(false),p.PrivateMemorySize64,p.HandleCount,Threads=p.Threads.Count});}
            }
            await Task.Delay(200);Collect();var connectedAlive=weak.Count(r=>r.IsAlive);
            if(client is not null){await client.StandardInput.WriteLineAsync("STOP");await client.WaitForExitAsync();if(client.ExitCode!=0)throw new Exception("UIA client exit failed.");}
            await Task.Delay(1000);Collect();var releasedAlive=weak.Count(r=>r.IsAlive);
            var result=new{kind,close,Completed=count,ConnectedAlive=connectedAlive,ReleasedAlive=releasedAlive,ElapsedMs=watch.ElapsedMilliseconds,Success=true};
            File.WriteAllText(Path.Combine(Folder,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Log("COMPLETE",result);
            if(Environment.GetEnvironmentVariable("UIA_PROBE_HOLD")=="1")
            {
                File.WriteAllText(Path.Combine(Folder,"dump-ready.json"),JsonSerializer.Serialize(new{Pid=Environment.ProcessId,Alive=releasedAlive}));
                var hold=Stopwatch.StartNew();while(!File.Exists(Path.Combine(Folder,"release.txt"))&&hold.Elapsed<TimeSpan.FromSeconds(60))await Task.Delay(20);
            }
        }
        finally{if(client is not null&&!client.HasExited){client.Kill();await client.WaitForExitAsync();}}
    }
    private static Process StartClient(string dll)
    {
        var s=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        s.ArgumentList.Add(Path.GetFullPath(dll));s.ArgumentList.Add(Environment.ProcessId.ToString());return Process.Start(s)!;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static async Task<WeakReference> Cycle(IClassicDesktopStyleApplicationLifetime lifetime,string kind,string close,Process? client,int iteration)
    {
        var control=Create(kind);control.Name="ProbeControl";
        var window=new Window{Title="UIA minimal "+kind,Width=400,Height=300,ShowInTaskbar=false,ShowActivated=false,Content=control};
        lifetime.MainWindow=window;
        var known=new HashSet<int>();var disconnecting=false;TopLevel? automationRoot=null;
        Win32Properties.AddWndProcHookCallback(window,(IntPtr hwnd,uint message,IntPtr w,IntPtr l,ref bool handled)=>
        {
            if(disconnecting&&message==0x003D)handled=true;
            if(message==0x0002)
            {
                Snapshot(window,known,"WM_DESTROY",iteration,automationRoot);
                if(close=="root-disconnect" && ExistingRoot(window) is {} root)
                {
                    disconnecting=true;
                    var api=Backend().GetType("Avalonia.Win32.Interop.Automation.UiaCoreProviderApi")!;
                    var hr=(int)api.GetMethod("UiaDisconnectProvider")!.Invoke(null,new[]{root})!;
                    Log("DISCONNECT",new{iteration,HResult=$"0x{hr:X8}",InSendMessage=InSendMessageEx(IntPtr.Zero)});Marshal.ThrowExceptionForHR(hr);
                }
            }
            return IntPtr.Zero;
        });
        window.Closed+=(_,_)=>Snapshot(window,known,"CLOSED",iteration,automationRoot);
        window.Show();await Task.Delay(20);window.UpdateLayout();
        Open(control,kind);
        if(Environment.GetEnvironmentVariable("UIA_PROBE_SCOPE")=="popup")
        {
            await Task.Delay(20);window.UpdateLayout();
            var popupContent=kind switch
            {
                "Menu"=>((MenuItem)((Menu)control).Items[0]!).Items[0] as Control,
                "ContextMenu"=>control.ContextMenu!.Items[0] as Control,
                "Flyout"=>((Flyout)((Button)control).Flyout!).Content as Control,
                _=>throw new ArgumentException("Direct popup probe supports Menu, ContextMenu and Flyout.")
            };
            automationRoot=TopLevel.GetTopLevel(popupContent!)??throw new Exception("No native popup root.");
            if(ReferenceEquals(automationRoot,window))throw new Exception("Popup uses owner/overlay root; direct HWND probe unavailable.");
            Log("POPUP_TARGET",new{iteration,Type=automationRoot.GetType().FullName,Handle=automationRoot.TryGetPlatformHandle()!.Handle.ToInt64()});
        }
        Snapshot(window,known,"OPEN",iteration,automationRoot);
        Task<string?>? done=null;
        if(client is not null)
        {
            await client.StandardInput.WriteLineAsync((automationRoot??window).TryGetPlatformHandle()!.Handle.ToInt64()+" 150");
            var acquired=await client.StandardOutput.ReadLineAsync();if(acquired?.StartsWith("ACQUIRED 00000000")!=true)throw new Exception("Acquire: "+acquired);
            if(Environment.GetEnvironmentVariable("UIA_PROBE_REQUIRE_VALUE")=="1"&&int.Parse(acquired.Split(' ')[3])<1)
                throw new Exception("Diagnostic scope did not discover a ValuePattern provider.");
            Log("CLIENT_ACQUIRED",new{iteration,acquired});done=client.StandardOutput.ReadLineAsync();
        }
        Snapshot(window,known,"BEFORE_CLOSE",iteration,automationRoot);
        window.Close();lifetime.MainWindow=null;
        if(done is not null){var text=await done;if(text?.StartsWith("DONE ")!=true)throw new Exception("Lost query completion.");Log("CLIENT_DONE",new{iteration,text});}
        if(window.IsVisible)throw new Exception("Window not closed.");
        Snapshot(window,known,"AFTER_CLOSE",iteration,automationRoot);
        return new WeakReference(window);
    }
    private static Control Create(string kind)=>kind switch
    {
        "TextBox"=>new TextBox{Text="test"},
        "Menu"=>Menu(),
        "ContextMenu"=>new Button{Content="Context",ContextMenu=new ContextMenu{ItemsSource=new[]{new MenuItem{Header="Item"}}}},
        "Flyout"=>new Button{Content="Flyout",Flyout=new Flyout{Content=new TextBox{Text="Popup"}}},
        "TreeView"=>new TreeView{ItemsSource=new[]{new TreeViewItem{Header="Root",ItemsSource=new[]{new TreeViewItem{Header="Child"}},IsExpanded=true}}},
        "TabControl"=>new TabControl{ItemsSource=new[]{new TabItem{Header="A",Content=new TextBox{Text="Tab"}},new TabItem{Header="B",Content="B"}},SelectedIndex=0},
        "ComboBox"=>new ComboBox{ItemsSource=new[]{"A","B"},SelectedIndex=0},
        "ListBox"=>new ListBox{ItemsSource=new[]{"A","B"},SelectedIndex=0},
        _=>throw new ArgumentException("Unsupported control")
    };
    private static Menu Menu(){var item=new MenuItem{Header="Root"};item.Items.Add(new MenuItem{Header="Child"});var menu=new Menu();menu.Items.Add(item);return menu;}
    private static void Open(Control control,string kind)
    {
        if(control is ComboBox c)c.IsDropDownOpen=true;
        if(control is Menu m)((MenuItem)m.Items[0]!).IsSubMenuOpen=true;
        if(kind=="ContextMenu")control.ContextMenu!.Open(control);
        if(kind=="Flyout")((Button)control).Flyout!.ShowAt(control);
    }
    private static Assembly Backend()=>AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Avalonia.Win32");
    private static object Table()=>Backend().GetType("Avalonia.Win32.Automation.AutomationNode")!.GetField("s_nodes",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    private static object? ExistingRoot(Window window)
    {
        var peer=ControlAutomationPeer.FromElement(window);if(peer is null)return null;var table=Table();var args=new object?[]{peer,null};
        return table.GetType().GetMethod("TryGetValue")!.Invoke(table,args) is true?args[1]:null;
    }
    private static void Snapshot(Window window,HashSet<int> known,string stage,int iteration,TopLevel? additionalRoot=null)
    {
        var rows=new List<object>();
        foreach(var entry in (IEnumerable)Table())
        {
            var t=entry.GetType();var peer=t.GetProperty("Key")!.GetValue(entry)!;var node=t.GetProperty("Value")!.GetValue(entry)!;var id=RuntimeHelpers.GetHashCode(node);
            if(peer is ControlAutomationPeer p && (ReferenceEquals(p.Owner.GetVisualRoot(),window)||(additionalRoot is not null&&ReferenceEquals(p.Owner.GetVisualRoot(),additionalRoot))||known.Contains(id)))
            {known.Add(id);rows.Add(new{Id=id,Peer=peer.GetType().FullName,Control=p.Owner.GetType().FullName,Provider=node.GetType().FullName});}
        }
        Log("PROVIDERS",new{iteration,stage,window.IsVisible,DispatcherAccess=Dispatcher.UIThread.CheckAccess(),Rows=rows});
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
}
internal sealed class ProbeApp:Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop){base.OnFrameworkInitializationCompleted();return;}
        desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async()=>{try{await Program.Run(desktop);}catch(Exception e){Program.ExitCode=1;Program.Log("MANAGED_FAILURE",new{Type=e.GetType().FullName,e.StackTrace});}finally{desktop.Shutdown(Program.ExitCode);}});
        base.OnFrameworkInitializationCompleted();
    }
}
