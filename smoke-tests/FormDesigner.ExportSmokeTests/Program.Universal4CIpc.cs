using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaDesigner.Host.Protocol;
using AvaloniaDesigner.VsHost;
using FormDesigner.Views;
using System.IO;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static void AssertUniversal4CStructuralIpc(SmokeContext context)
    {
        var vm=context.ViewModel;var window=new VsHostWindow(new TestDesignerHostServices()){DataContext=vm};window.Show();
        var pipe=$"{DesignerHostProtocol.PipePrefix}.structural.{Guid.NewGuid():N}";
        using var bridge=new VsHostBridge(vm,window,pipe);bridge.Start();
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var task=Task.Run(async()=>{
            using var client=NamedPipeProtocolConnection.CreateClient(pipe);await client.ConnectAsync(cancel.Token);
            using var connection=new NamedPipeProtocolConnection(client);
            async Task Barrier(){await connection.SendAsync(DesignerHostMessageTypes.Hello,"barrier","structural",new HelloPayload(),cancel.Token);RequireCapability((await connection.ReceiveAsync(cancel.Token))!.MessageType==DesignerHostMessageTypes.HelloAck,"IPC barrier failed");}
            await Barrier();
            var source="<Window xmlns='https://github.com/avaloniaui' xmlns:custom='using:X'><Grid><StackPanel Name='From'><Button Name='Move' Content='{Binding Keep}' custom:Unknown='KEEP'/><!-- KEEP --><TextBox Name='Delete'/></StackPanel><Canvas Name='To'/></Grid></Window>";long version=1;
            await connection.SendAsync(DesignerHostMessageTypes.OpenDocument,"open","structural",CreateVsHostOpenDocumentPayload(source,version),cancel.Token);
            RequireCapability((await connection.ReceiveAsync(cancel.Token))!.MessageType==DesignerHostMessageTypes.DocumentOpened,"Structural IPC open failed");
            string created="";
            var actions=new Action[]{
                ()=>created=(vm.InsertAxamlControl("TextBlock",Named4C(vm,"From").Id,0,0,index:int.MaxValue)??throw new Exception(vm.StatusText)).Id,
                ()=>RequireCapability(vm.ReorderAxamlControl(created,0),vm.StatusText),
                ()=>{var target=Named4C(vm,"To");vm.SelectSingleControl(target);vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Margin"),"5");vm.EndPropertyGridTextEdit();RequireCapability(vm.ReparentAxamlControl(Named4C(vm,"Move").Id,target.Id,30,40),vm.StatusText);},
                ()=>RequireCapability(vm.RemoveAxamlControls(new[]{Named4C(vm,"Delete").Id}),vm.StatusText),
                ()=>GridEditorUndo(vm),()=>GridEditorUndo(vm,true)};
            foreach(var action in actions)
            {
                await Dispatcher.UIThread.InvokeAsync(action);var apply=Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);
                var envelope=(await connection.ReceiveAsync(cancel.Token))!;await apply;
                RequireCapability(envelope.MessageType==DesignerHostMessageTypes.ApplyDesignerPatch,"Structural Apply missing");
                var payload=connection.GetPayload<ApplyDesignerPatchPayload>(envelope)!;
                RequireCapability(payload.Edits.Count>0&&payload.ExpectedVersion==version&&payload.ExpectedChecksum==DesignerHostProtocol.ComputeChecksum(source),"Structural patch lost version/checksum");
                foreach(var edit in AvaloniaDesigner.VSIX.VsTextPatch.Validate(source,payload.Edits))source=source.Remove(edit.Start,edit.Length).Insert(edit.Start,edit.NewText);
                _=FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlSyntaxDocument.Parse(source);
                await connection.SendAsync(DesignerHostMessageTypes.PatchApplied,envelope.RequestId,"structural",new PatchAppliedPayload{Text=source,Version=++version,Checksum=DesignerHostProtocol.ComputeChecksum(source)},cancel.Token);
                await Barrier();await Dispatcher.UIThread.InvokeAsync(()=>RequireCapability(!vm.CreateActiveAxamlPatch(source).HasChanges,"ACK produced phantom structural edits"));
            }
            RequireCapability(source.Contains("<!-- KEEP -->")&&source.Contains("custom:Unknown='KEEP'")&&source.Contains("Content='{Binding Keep}'"),"IPC damaged opaque XML");
            // A real installed VS buffer is manual acceptance; exercise its exact text-edit validator here.
            await Dispatcher.UIThread.InvokeAsync(()=>vm.InsertAxamlControl("Button",Named4C(vm,"From").Id,0,0));
            var pending=Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);var stale=(await connection.ReceiveAsync(cancel.Token))!;await pending;
            var stalePayload=connection.GetPayload<ApplyDesignerPatchPayload>(stale)!;
            var manual=source+"<!-- manual edit -->";RequireCapability(stalePayload.ExpectedChecksum!=DesignerHostProtocol.ComputeChecksum(manual),"Stale checksum did not conflict");
            await connection.SendAsync(DesignerHostMessageTypes.Error,stale.RequestId,"structural",new ErrorPayload{Code="SOURCE_VERSION_CONFLICT",Message="Manual buffer edit; stale structural patch rejected."},cancel.Token);await Barrier();
            RequireCapability(manual.EndsWith("<!-- manual edit -->"),"Conflict overwrote user text");
        });
        try{Pump(task);}finally{window.CloseForBridgeShutdown();}
    }
}
