using Avalonia.Threading;
using AvaloniaDesigner.Host.Protocol;
using AvaloniaDesigner.VsHost;
namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
 private static void AssertUniversal4DTransactions(SmokeContext context)
 {
  var vm=context.ViewModel;var window=new VsHostWindow(new TestDesignerHostServices()){DataContext=vm};window.Show();
  var pipe=$"{DesignerHostProtocol.PipePrefix}.phase4d.{Guid.NewGuid():N}";
  using var bridge=new VsHostBridge(vm,window,pipe);bridge.Start();using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(120));
  var task=Task.Run(async()=>{
   async Task<NamedPipeProtocolConnection> Connect(){var stream=NamedPipeProtocolConnection.CreateClient(pipe);await stream.ConnectAsync(cancel.Token);return new NamedPipeProtocolConnection(stream);}
   async Task Barrier(NamedPipeProtocolConnection c){Console.WriteLine("STEP4D barrier");await c.SendAsync(DesignerHostMessageTypes.Hello,Guid.NewGuid().ToString("N"),"doc",new HelloPayload(),cancel.Token);RequireCapability((await c.ReceiveAsync(cancel.Token))!.MessageType==DesignerHostMessageTypes.HelloAck,"Barrier received unexpected patch");}
   string source="<Window xmlns='https://github.com/avaloniaui'><StackPanel Name='Host'><Button Name='Keep' Content='{Binding Safe}'/><!--keep--></StackPanel></Window>";long version=1;
   async Task Open(NamedPipeProtocolConnection c,string kind){await c.SendAsync(kind,"open","doc",CreateVsHostOpenDocumentPayload(source,version),cancel.Token);RequireCapability((await c.ReceiveAsync(cancel.Token))!.MessageType==DesignerHostMessageTypes.DocumentOpened,"Open/reload rejected");}
   async Task<DesignerHostEnvelope> Apply(NamedPipeProtocolConnection c){var action=Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);var m=(await c.ReceiveAsync(cancel.Token))!;await action;RequireCapability(m.MessageType==DesignerHostMessageTypes.ApplyDesignerPatch,"Apply missing");return m;}
   void Buffer(NamedPipeProtocolConnection c,DesignerHostEnvelope m){var p=c.GetPayload<ApplyDesignerPatchPayload>(m)!;RequireCapability(p.ExpectedVersion==version&&p.ExpectedChecksum==DesignerHostProtocol.ComputeChecksum(source),"Stale buffer overwrite");foreach(var e in AvaloniaDesigner.VSIX.VsTextPatch.Validate(source,p.Edits))source=source.Remove(e.Start,e.Length).Insert(e.Start,e.NewText);version++;}
   async Task Ack(NamedPipeProtocolConnection c,DesignerHostEnvelope m)=>await c.SendAsync(DesignerHostMessageTypes.PatchApplied,m.RequestId,"doc",new PatchAppliedPayload{Text=source,Version=version,Checksum=DesignerHostProtocol.ComputeChecksum(source)},cancel.Token);
   Console.WriteLine("STEP4D connect-first");var first=await Connect();await Barrier(first);await Open(first,DesignerHostMessageTypes.OpenDocument);
   await Dispatcher.UIThread.InvokeAsync(()=>vm.InsertAxamlControl("Button",Named4C(vm,"Host").Id,0,0));Console.WriteLine("STEP4D apply-first");var pending=await Apply(first);
   await Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);await Barrier(first); // No duplicate send while pending.
   await Dispatcher.UIThread.InvokeAsync(()=>vm.InsertAxamlControl("TextBlock",Named4C(vm,"Host").Id,0,0));
   Console.WriteLine("STEP4D ack-delayed");Buffer(first,pending);await Ack(first,pending);await Barrier(first);
   await Dispatcher.UIThread.InvokeAsync(()=>RequireCapability(vm.HasUnsavedChanges&&vm.CreateActiveAxamlPatch(source).HasChanges,"Delayed ACK lost unsent edit"));
   await Ack(first,pending);await Barrier(first); // duplicate ACK must not save newer changes
   await Dispatcher.UIThread.InvokeAsync(()=>RequireCapability(vm.HasUnsavedChanges,"Duplicate ACK marked newer edit saved"));
   Console.WriteLine("STEP4D apply-uncertain");var uncertain=await Apply(first);Buffer(first,uncertain);first.Dispose(); // Actual buffer applied; ACK lost.
   Console.WriteLine("STEP4D reconnect");using var second=await Connect();await Barrier(second);
   await Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);await Barrier(second); // Must not replay uncertain patch.
   Console.WriteLine("STEP4D recovery-open");await second.SendAsync(DesignerHostMessageTypes.OpenDocument,"reconnect","doc",CreateVsHostOpenDocumentPayload(source,version),cancel.Token);
   var recovery=(await second.ReceiveAsync(cancel.Token))!;RequireCapability(recovery.MessageType==DesignerHostMessageTypes.Error&&second.GetPayload<ErrorPayload>(recovery)!.Code=="RECOVERY_RELOAD_REQUIRED","Reconnect silently discarded local state");
   Console.WriteLine("STEP4D explicit-reload");var reloadAction=Dispatcher.UIThread.InvokeAsync(bridge.RequestReloadAsync);var reloadMessage=await second.ReceiveAsync(cancel.Token);Console.WriteLine("STEP4D reload-message-received");await reloadAction;RequireCapability(reloadMessage!.MessageType==DesignerHostMessageTypes.ReloadRequested,"Missing explicit recovery request");await Open(second,DesignerHostMessageTypes.ReloadDocument);
   await Dispatcher.UIThread.InvokeAsync(()=>RequireCapability(!vm.HasUnsavedChanges&&!vm.CreateActiveAxamlPatch(source).HasChanges,"Reload did not reconcile actual buffer"));
   await Dispatcher.UIThread.InvokeAsync(()=>vm.InsertAxamlControl("TextBox",Named4C(vm,"Host").Id,0,0));Console.WriteLine("STEP4D apply-invalid-ack");var wrong=await Apply(second);
   var damaged=source+"<!--other edit-->";await second.SendAsync(DesignerHostMessageTypes.PatchApplied,wrong.RequestId,"doc",new PatchAppliedPayload{Text=damaged,Version=version+1,Checksum=DesignerHostProtocol.ComputeChecksum(damaged)},cancel.Token);await Barrier(second);
   await Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);await Barrier(second);await Dispatcher.UIThread.InvokeAsync(()=>RequireCapability(vm.HasUnsavedChanges,"Invalid ACK saved source"));
   Console.WriteLine("STEP4D explicit-reload");var reloadAgain=Dispatcher.UIThread.InvokeAsync(bridge.RequestReloadAsync);var reloaded=await second.ReceiveAsync(cancel.Token);await reloadAgain;RequireCapability(reloaded!.MessageType==DesignerHostMessageTypes.ReloadRequested,"Recovery after invalid ACK missing");await Open(second,DesignerHostMessageTypes.ReloadDocument);
   await Dispatcher.UIThread.InvokeAsync(()=>vm.InsertAxamlControl("CheckBox",Named4C(vm,"Host").Id,0,0));Console.WriteLine("STEP4D apply-close");var closing=await Apply(second);
   await second.SendAsync(DesignerHostMessageTypes.CloseDocument,"close","doc",new EmptyPayload(),cancel.Token);await Barrier(second);await Ack(second,closing);await Barrier(second);
   await Dispatcher.UIThread.InvokeAsync(()=>RequireCapability(vm.HasUnsavedChanges,"Late ACK after close saved unrelated state"));
   RequireCapability(source.Contains("Content='{Binding Safe}'")&&source.Contains("<!--keep-->"),"Transaction damaged preserved source");
   Console.WriteLine("PHASE4D_IPC PASS delayed/duplicate ACK; single in-flight; disconnect/no replay; explicit recovery; invalid ACK; close during patch");
  });
  try{Pump(task);}finally{bridge.Dispose();window.CloseForBridgeShutdown();}
 }
}
