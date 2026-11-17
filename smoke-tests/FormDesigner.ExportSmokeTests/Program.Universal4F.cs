using AvaloniaDesigner.Host.Protocol;
using AvaloniaDesigner.VsHost;
using FormDesigner.ViewModels;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal4FChecks()
    {
        int passed = 0, failed = 0;
        void Check(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS PHASE4F_" + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL PHASE4F_" + name + ": " + e); }
        }

        // Drive the actual host message handler. The in-memory stream replaces only
        // transport here; separate real-pipe tests cover framing and ACK ordering.
        foreach (var kind in new[] { DesignerHostMessageTypes.OpenDocument, DesignerHostMessageTypes.DocumentChanged })
        foreach (var otherDocument in new[] { false, true })
        {
            if (kind == DesignerHostMessageTypes.DocumentChanged && otherDocument) continue;
            Check("DirtyDocumentRejects_" + kind + "_" + otherDocument, () =>
            {
                using var fixture = new Bridge4FFixture();
                var original = fixture.Open("first", Source4F("First"), 1);
                RequireCapability(original.MessageType == DesignerHostMessageTypes.DocumentOpened, "Initial document did not open");
                fixture.Vm.InsertAxamlControl("Button", Named4C(fixture.Vm, "First").Id, 0, 0);
                var before = Text4C(fixture.Vm);
                var snapshot = fixture.Vm.ActiveSession.CurrentSnapshot;
                var selection = fixture.Vm.SelectedControl?.Id;
                var session = fixture.Vm.DocumentSessionId;
                var response = fixture.Open(otherDocument ? "second" : "first", Source4F("Second"), 2, kind);
                RequireCapability(response.MessageType == DesignerHostMessageTypes.Error
                    && fixture.Payload<ErrorPayload>(response)!.Code == "UNSAVED_DESIGNER_CHANGES",
                    "Incoming document silently replaced unacknowledged Designer edits");
                RequireCapability(Text4C(fixture.Vm) == before && fixture.Vm.HasUnsavedChanges
                    && fixture.Vm.ActiveSession.CurrentSnapshot == snapshot
                    && fixture.Vm.DocumentSessionId == session && fixture.Vm.SelectedControl?.Id == selection,
                    "Rejected document changed source/history/selection/session");
            });
        }
        Check("CleanDocumentSwitchAndWrongDocumentAck", () =>
        {
            using var fixture = new Bridge4FFixture();
            fixture.Open("first", Source4F("First"), 1);
            RequireCapability(fixture.Open("second", Source4F("Second"), 2).MessageType == DesignerHostMessageTypes.DocumentOpened,
                "Clean switch rejected");
            var before = Text4C(fixture.Vm);
            fixture.Send(DesignerHostMessageTypes.PatchApplied, "first", new PatchAppliedPayload
            { Text = Source4F("First"), Version = 9, Checksum = DesignerHostProtocol.ComputeChecksum(Source4F("First")) }, false);
            RequireCapability(Text4C(fixture.Vm) == before && before.Contains("Second") && !before.Contains("First"),
                "ACK for another document changed active source");
        });
        Check("ExplicitReloadRecoversDirtyDocument", () =>
        {
            using var fixture = new Bridge4FFixture();
            var source = Source4F("First"); fixture.Open("first", source, 1);
            fixture.Vm.InsertAxamlControl("TextBox", Named4C(fixture.Vm, "First").Id, 0, 0);
            RequireCapability(fixture.Open("first", source, 2, DesignerHostMessageTypes.ReloadDocument).MessageType == DesignerHostMessageTypes.DocumentOpened,
                "Explicit reload did not recover");
            RequireCapability(!fixture.Vm.HasUnsavedChanges && Text4C(fixture.Vm) == source, "Reload did not reconcile exact source");
        });
        Check("ClosedDocumentRejectsChanges", () =>
        {
            using var fixture = new Bridge4FFixture(); fixture.Open("first", Source4F("First"), 1);
            fixture.Send(DesignerHostMessageTypes.CloseDocument, "first", new EmptyPayload(), false);
            var response = fixture.Open("first", Source4F("Second"), 2, DesignerHostMessageTypes.DocumentChanged);
            RequireCapability(response.MessageType == DesignerHostMessageTypes.Error
                && fixture.Payload<ErrorPayload>(response)!.Code == "DOCUMENT_CHANGE_UNAVAILABLE", "Closed document was changed");
        });
        Check("StaleReceiveLoopCannotDisconnectReplacement", () =>
        {
            using var client = new AvaloniaDesigner.VSIX.VsHostBridgeClient(new Microsoft.VisualStudio.Shell.AsyncPackage());
            var type = client.GetType();
            var field = type.GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var oldStream = new EndOfPipe4FStream();
            using var old = new NamedPipeProtocolConnection(oldStream);
            using var replacement = new NamedPipeProtocolConnection(new MemoryStream());
            field.SetValue(client, old);
            var receive = type.GetMethod("ReceiveLoopAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var args = receive.GetParameters().Length == 0 ? null : new object[] { old };
            var loop = (Task)receive.Invoke(client, args)!;
            field.SetValue(client, replacement);
            oldStream.End.TrySetResult(0); Pump(loop);
            RequireCapability(ReferenceEquals(field.GetValue(client), replacement), "Old receive callback disconnected new pipe");
        });
        Check("DisposeCompletesPendingRequest", () =>
        {
            var client = new AvaloniaDesigner.VSIX.VsHostBridgeClient(new Microsoft.VisualStudio.Shell.AsyncPackage());
            var pending = (System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<DesignerHostEnvelope>>)
                client.GetType().GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
            var completion = new TaskCompletionSource<DesignerHostEnvelope>(); pending.TryAdd("waiting", completion);
            client.Dispose();
            RequireCapability(completion.Task.IsCompleted, "Disposal stranded the pending request");
            _ = completion.Task.Exception;
        });
        Check("DisconnectedAckMustNotReportSuccess", () =>
        {
            using var client = new AvaloniaDesigner.VSIX.VsHostBridgeClient(new Microsoft.VisualStudio.Shell.AsyncPackage());
            try
            {
                client.SendPatchAppliedAsync("request", "doc", new AvaloniaDesigner.VSIX.VsDocumentSnapshot()).GetAwaiter().GetResult();
                throw new Exception("Disconnected ACK silently reported success");
            }
            catch (InvalidOperationException) { }
        });
        Check("ShutdownRequestsOwnedHostShutdown", () =>
        {
            var client = new AvaloniaDesigner.VSIX.VsHostBridgeClient(new Microsoft.VisualStudio.Shell.AsyncPackage());
            var stream = new MemoryStream();
            client.GetType().GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, new NamedPipeProtocolConnection(stream));
            client.ShutdownAsync().GetAwaiter().GetResult();
            using var read = new NamedPipeProtocolConnection(new MemoryStream(stream.ToArray()));
            var shutdown = read.ReceiveAsync().GetAwaiter().GetResult();
            RequireCapability(shutdown?.MessageType == DesignerHostMessageTypes.HostShutdown, "Owned host was abandoned on bridge disposal");
        });
        Check("ShutdownCannotDiscardDirtyDesigner", () =>
        {
            using var fixture = new Bridge4FFixture(); fixture.Open("first", Source4F("First"), 1);
            fixture.Vm.InsertAxamlControl("TextBox", Named4C(fixture.Vm, "First").Id, 0, 0);
            var source = Text4C(fixture.Vm);
            fixture.Send(DesignerHostMessageTypes.HostShutdown, "first", new EmptyPayload(), false);
            RequireCapability(!fixture.IsDisposed && fixture.Vm.HasUnsavedChanges && Text4C(fixture.Vm) == source,
                "Host shutdown discarded unacknowledged edits");
        });
        Check("WorkspaceOnlyDirtyDoesNotBlockOpen", () =>
        {
            using var fixture = new Bridge4FFixture(); fixture.Open("first", Source4F("First"), 1);
            fixture.Vm.ActiveSession.SetCurrentSnapshot(fixture.Vm.ActiveSession.CurrentSnapshot + " ");
            RequireCapability(fixture.Vm.HasUnsavedChanges && !fixture.Vm.CreateActiveAxamlPatch().HasChanges,
                "Test needs a workspace-only snapshot difference");
            RequireCapability(fixture.Open("second", Source4F("Second"), 2).MessageType == DesignerHostMessageTypes.DocumentOpened,
                "Workspace-only state incorrectly blocked a clean source switch");
        });
        Check("WorkspaceOnlyDirtyDoesNotKeepCleanHostAlive", () =>
        {
            using var fixture = new Bridge4FFixture(); fixture.Open("first", Source4F("First"), 1);
            fixture.Vm.ActiveSession.SetCurrentSnapshot(fixture.Vm.ActiveSession.CurrentSnapshot + " ");
            fixture.Send(DesignerHostMessageTypes.HostShutdown, "first", new EmptyPayload(), false);
            RequireCapability(fixture.IsDisposed, "A source-clean host was kept alive by workspace-only state");
        });
        Check("PendingScalarEditDoesNotDependOnSnapshotDirtyFlag", () =>
        {
            using var fixture = new Bridge4FFixture();
            fixture.Open("first", Source4F("First").Replace("Text='{Binding Safe}'", "Text='literal'"), 1);
            fixture.Vm.Controls.Single(c => c.Type == "TextBlock").Text = "rapid edit";
            fixture.Vm.ActiveSession.SetSavedSnapshot(fixture.Vm.ActiveSession.CurrentSnapshot);
            RequireCapability(!fixture.Vm.HasUnsavedChanges && fixture.Vm.CreateActiveAxamlPatch().HasChanges,
                "Test needs source edits ahead of the snapshot dirty flag");
            var response = fixture.Open("second", Source4F("Second"), 2);
            RequireCapability(response.MessageType == DesignerHostMessageTypes.Error,
                "Pending scalar edit was overwritten before snapshot update");
        });
        Check("InitialOpenReplacesHostSeedModel", () =>
        {
            using var fixture = new Bridge4FFixture();
            fixture.Vm.CreateControl("Button", 0, 0);
            RequireCapability(!fixture.Vm.IsAxamlRoundTripDocument && fixture.Vm.HasUnsavedChanges, "Test needs initial JSON host seed");
            RequireCapability(fixture.Open("first", Source4F("First"), 1).MessageType == DesignerHostMessageTypes.DocumentOpened,
                "Initial host model blocked the first AXAML snapshot");
        });
        Console.WriteLine($"PHASE4F_CHECKS passed={passed}; failed={failed}");
        return failed == 0 ? 0 : 1;
    }

    private static string Source4F(string name) => "<Window xmlns='https://github.com/avaloniaui'><StackPanel Name='" + name
        + "'><!--preserve--><TextBlock Text='{Binding Safe}'/></StackPanel></Window>";

    private sealed class EndOfPipe4FStream : MemoryStream
    {
        public TaskCompletionSource<int> End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => End.Task;
    }

    private sealed class Bridge4FFixture : IDisposable
    {
        public MainWindowViewModel Vm { get; } = GridFirstVm();
        private readonly VsHostWindow _window;
        private readonly VsHostBridge _bridge;
        private NamedPipeProtocolConnection? _wire;
        public bool IsDisposed => (bool)typeof(VsHostBridge).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_bridge)!;
        public Bridge4FFixture()
        {
            _window = new VsHostWindow(new TestDesignerHostServices()) { DataContext = Vm };
            _bridge = new VsHostBridge(Vm, _window, "unused-phase4f-" + Guid.NewGuid().ToString("N"));
        }
        public DesignerHostEnvelope Open(string id, string text, long version, string kind = DesignerHostMessageTypes.OpenDocument) =>
            kind == DesignerHostMessageTypes.DocumentChanged
                ? Send(kind, id, new DocumentChangedPayload { Text = text, Version = version, Checksum = DesignerHostProtocol.ComputeChecksum(text) })!
                : Send(kind, id, CreateVsHostOpenDocumentPayload(text, version))!;
        public T? Payload<T>(DesignerHostEnvelope response) => _wire!.GetPayload<T>(response);
        public DesignerHostEnvelope? Send<T>(string kind, string id, T payload, bool responseExpected = true)
        {
            // Serialize the request through the production codec as well.
            using var requestStream = new MemoryStream();
            using var requestWire = new NamedPipeProtocolConnection(requestStream);
            requestWire.SendAsync(kind, Guid.NewGuid().ToString("N"), id, payload).GetAwaiter().GetResult();
            requestStream.Position = 0;
            var request = requestWire.ReceiveAsync().GetAwaiter().GetResult()!;
            _wire?.Dispose();
            var stream = new MemoryStream(); _wire = new NamedPipeProtocolConnection(stream);
            typeof(VsHostBridge).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_bridge, _wire);
            Pump((Task)typeof(VsHostBridge).GetMethod("HandleMessageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_bridge, new object[] { request })!);
            if (!responseExpected) return null;
            stream.Position = 0;
            return _wire.ReceiveAsync().GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            _bridge.Dispose(); _window.CloseForBridgeShutdown(); Dispose5AProfileVm(Vm);
        }
    }
}
