using AvaloniaDesigner.Host.Protocol;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaDesigner.VsHost;
using FormDesigner.DesignerSystem;
using FormDesigner.Models;
using FormDesigner.Views;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void AssertProtocolPreservesSnapshot(SmokeContext context)
    {
        foreach (var source in new[] { "<Window>\r\n\t<!-- text -->\r\n</Window>",
            "\uFEFF<Window>\n<!-- Кириллица \U0001F600 -->\r</Window>",
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml")) })
        {
            using var stream = new MemoryStream();
            using var connection = new NamedPipeProtocolConnection(stream);
            var payload = CreateVsHostOpenDocumentPayload(source, 1);
            connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "open", "test", payload).GetAwaiter().GetResult();
            stream.Position = 0;
            var envelope = connection.ReceiveAsync().GetAwaiter().GetResult()!;
            var received = connection.GetPayload<OpenDocumentPayload>(envelope)!;
            if (received.Text != source || received.Checksum != DesignerHostProtocol.ComputeChecksum(received.Text))
                throw new InvalidOperationException($"Snapshot changed in IPC: before={source.Length}, after={received.Text.Length}, " +
                    $"CR before={source.Count(c => c == '\r')}, after={received.Text.Count(c => c == '\r')}; " +
                    $"checksum={payload.Checksum}, received={DesignerHostProtocol.ComputeChecksum(received.Text)}");
            var ack = RoundTripPayload(new PatchAppliedPayload { Text = source, Checksum = payload.Checksum, Version = 2 });
            var change = RoundTripPayload(new DocumentChangedPayload { Text = source, Checksum = payload.Checksum, Version = 3 });
            var edit = RoundTripPayload(new TextEditPayload { NewText = source });
            if (ack.Text != source || change.Text != source || edit.NewText != source)
                throw new InvalidOperationException("ACK, DocumentChanged or inserted text changed in transport.");
        }
    }

    private static T RoundTripPayload<T>(T payload)
    {
        using var stream = new MemoryStream();
        using var connection = new NamedPipeProtocolConnection(stream);
        connection.SendAsync("test", "test", "test", payload).GetAwaiter().GetResult();
        stream.Position = 0;
        var envelope = connection.ReceiveAsync().GetAwaiter().GetResult()!;
        return connection.GetPayload<T>(envelope)!;
    }

    private static void AssertJsonFormatGuard(SmokeContext context)
    {
        var vm = CreateViewModel("JsonFormatGuard");
        try
        {
            vm.LoadDocumentJson("<Window><Canvas /></Window>", "MainWindow.axaml");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("DOCUMENT_FORMAT_MISMATCH"))
        {
            return;
        }
        throw new InvalidOperationException("AXAML must be rejected before JSON deserialization.");
    }

    private static void AssertRealMainWindowRoundTrip(SmokeContext context)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        var source = File.ReadAllText(path);
        var result = new AxamlImportService().Import(source, path);
        var vm = CreateViewModel("ComplexAxaml");
        vm.LoadAxamlImportedDocument(result, path);
        var patch = vm.CreateActiveAxamlPatch(source);
        if (!patch.CanApply || patch.HasChanges || patch.PatchedText != source)
            throw new InvalidOperationException("Real AXAML must open safely and survive a no-edit round trip verbatim.");
    }

    private static string ComplexSource() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Samples", "VisualStudioPoC",
        "ComplexAxamlVisualStudioRoundTrip", "MainWindow.axaml"));

    private static void AssertComplexSourcePreservation(SmokeContext context)
    {
        var source = ComplexSource();
        var imported = new AxamlImportService().Import(source);
        if (imported.CapabilityReport.Level != AxamlCapabilityLevel.PartiallyEditable
            || imported.Document.Controls.Count != 3)
            throw new InvalidOperationException("Unknown/custom namesakes and nested subtrees must remain opaque.");
        var writer = new AxamlPatchWriter();
        var noEdit = writer.CreatePatch(imported.RoundTripDocument, imported.Document, source);
        if (noEdit.HasChanges || noEdit.PatchedText != source)
            throw new InvalidOperationException("Styles, bindings, comments and unknown syntax must survive verbatim.");
        var button = imported.Document.Controls.Single(c => c.Name == "Button1");
        button.Text = "New & safe";
        button.Width = 220;
        button.X = 180;
        button.Y = 120;
        var patch = writer.CreatePatch(imported.RoundTripDocument, imported.Document, source);
        var expected = source.Replace("Content=\"Old\"", "Content=\"New &amp; safe\"")
            .Replace("Width=\"160\"", "Width=\"220\"").Replace("Canvas.Left=\"100\"", "Canvas.Left=\"180\"")
            .Replace("Canvas.Top=\"100\"", "Canvas.Top=\"120\"");
        if (!patch.CanApply || patch.Edits.Count != 4 || patch.PatchedText != expected)
            throw new InvalidOperationException("Only four attribute values may change in complex AXAML.");
        if (!writer.CreatePatch(imported.RoundTripDocument, imported.Document, source + " ").ExternalChangeDetected)
            throw new InvalidOperationException("External source modification must still conflict.");
    }

    private static void AssertJsonWorkflowAndReopen(SmokeContext context)
    {
        var vm = context.ViewModel;
        var json = vm.ExportDocumentJson();
        vm.LoadAxamlImportedDocument(new AxamlImportService().Import(ComplexSource()), "complex.axaml");
        var session = vm.ActiveSession;
        try { vm.LoadDocumentJson(ComplexSource(), "complex.axaml"); }
        catch (InvalidDataException) { }
        if (!ReferenceEquals(session, vm.ActiveSession) || !vm.IsAxamlRoundTripDocument)
            throw new InvalidOperationException("Rejected JSON input must not clear a live AXAML context.");
        vm.LoadDocumentJson(json, "project.formdesigner.json");
        if (vm.DocumentKind != DesignerDocumentKind.ProjectJson || vm.Controls.Count == 0)
            throw new InvalidOperationException("Existing JSON workflow regressed.");

        EnsureAvaloniaRuntimeInitialized();
        var host = new TestDesignerHostServices();
        var window = new MainWindow(host) { DataContext = vm };
        var path = Path.Combine(context.ProjectPath, "reopen.axaml");
        File.WriteAllText(path, ComplexSource());
        try
        {
            var load = typeof(MainWindow).GetMethod("LoadStandaloneDocumentAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Pump((Task)load.Invoke(window, new object[] { path })!);
            if (vm.DocumentKind != DesignerDocumentKind.AxamlRoundTrip || vm.CreateActiveAxamlPatch().PatchedText != ComplexSource())
                throw new InvalidOperationException("Standalone AXAML reopen used the wrong format.");
        }
        finally { window.CloseForExternalHost(); File.Delete(path); }
    }

    private static void AssertVsBufferCoordinates(SmokeContext context)
    {
        const string text = "<Window>\r\n\t<Button Content=\"Old\" />\n</Window>";
        var position = AvaloniaDesigner.VSIX.VsTextPatch.Position(text, text.IndexOf("Old", StringComparison.Ordinal));
        if (position != (2, 19))
            throw new InvalidOperationException($"CRLF source offsets were not mapped to DTE line/column: {position}");
        try
        {
            AvaloniaDesigner.VSIX.VsTextPatch.Validate(text, new[] {
                new TextEditPayload { Start = 2, Length = 4 }, new TextEditPayload { Start = 3, Length = 1 } });
        }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Overlapping patches must be rejected before the first buffer mutation.");
    }

    private static void Pump(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(40))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        if (!task.IsCompleted) throw new TimeoutException("AXAML lifecycle test timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void AssertIpcEditReloadLifecycle(SmokeContext context)
    {
        EnsureAvaloniaRuntimeInitialized();
        var vm = context.ViewModel;
        var window = new VsHostWindow(new TestDesignerHostServices()) { DataContext = vm };
        var settings = (AppSettingsModel)typeof(MainWindow).GetField("_appSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        settings.Session.LastProjectPath = Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml");
        settings.Session.ReopenLastWorkspaceOnStartup = true;
        window.Show();
        if (vm.WorkspaceToasts.Any(t => t.Title == "Reopen failed"))
            throw new InvalidOperationException("VsHost invoked standalone file reopen.");

        var pipe = $"{DesignerHostProtocol.PipePrefix}.lifecycle.{Guid.NewGuid():N}";
        using var bridge = new VsHostBridge(vm, window, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        bridge.Start();
        var exercise = Task.Run(async () =>
        {
            using var client = NamedPipeProtocolConnection.CreateClient(pipe);
            await client.ConnectAsync(cancellation.Token);
            using var connection = new NamedPipeProtocolConnection(client);
            const string id = "unsaved-buffer.axaml";
            var source = ComplexSource().Replace("Text=\"Hello\"", "Text=\"Unsaved VS text\"");
            async Task Open(string text, long version)
            {
                await connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "open" + version, id, CreateVsHostOpenDocumentPayload(text, version), cancellation.Token);
                var response = await connection.ReceiveAsync(cancellation.Token);
                if (response?.MessageType != DesignerHostMessageTypes.DocumentOpened)
                    throw new InvalidOperationException($"IPC open failed: {response?.Payload}");
            }
            await Open(source, 1);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (vm.Controls.Single(c => c.Name == "TextBox1").Text != "Unsaved VS text")
                    throw new InvalidOperationException("Host did not use the supplied unsaved buffer.");
                var surface = window.FindControl<DesignerSurface>("DesignerSurface")!;
                if (!ReferenceEquals(surface.Session, vm.ActiveSession))
                    throw new InvalidOperationException("Shared DesignerSurface did not attach the imported session.");
                var button = vm.Controls.Single(c => c.Name == "Button1");
                vm.SelectSingleControl(button);
                button.Text = "IPC edit";
                button.Width = 220;
                button.X = 180;
            });
            var apply = Dispatcher.UIThread.InvokeAsync(bridge.ApplyAsync);
            var envelope = (await connection.ReceiveAsync(cancellation.Token))!;
            await apply;
            if (envelope.MessageType != DesignerHostMessageTypes.ApplyDesignerPatch)
                throw new InvalidOperationException("Apply did not return a patch.");
            var patch = connection.GetPayload<ApplyDesignerPatchPayload>(envelope)!;
            if (patch.ExpectedChecksum != DesignerHostProtocol.ComputeChecksum(source) || patch.ExpectedVersion != 1 || patch.Edits.Count != 3)
                throw new InvalidOperationException("Patch base snapshot or minimal edits are incorrect.");
            var updated = AxamlPatchWriter.ApplyEdits(source, patch.Edits.Select(e => new AxamlTextEdit(e.Start, e.Length, e.NewText)));
            // An acknowledgement must not mark edits made after Apply as saved.
            await Dispatcher.UIThread.InvokeAsync(() => vm.Controls.Single(c => c.Name == "Button1").Text = "Next edit");
            await connection.SendAsync(DesignerHostMessageTypes.PatchApplied, envelope.RequestId, id,
                new PatchAppliedPayload { Text = updated, Version = 2, Checksum = DesignerHostProtocol.ComputeChecksum(updated) }, cancellation.Token);
            await connection.SendAsync(DesignerHostMessageTypes.Hello, "barrier", id, new HelloPayload(), cancellation.Token);
            await connection.ReceiveAsync(cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var next = vm.CreateActiveAxamlPatch(updated);
                if (!vm.ActiveSession.IsDirty || !next.CanApply || next.Edits.Count != 1)
                    throw new InvalidOperationException("Acknowledgement lost newer edits or failed to rebase the source map.");
            });
            await Open(updated, 3);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (vm.DocumentKind != DesignerDocumentKind.AxamlRoundTrip || vm.CreateActiveAxamlPatch(updated).HasChanges)
                    throw new InvalidOperationException("Reload did not establish an AXAML baseline.");
            });
            var stale = CreateVsHostOpenDocumentPayload(source, 1);
            await connection.SendAsync(DesignerHostMessageTypes.ReloadDocument, "stale", id, stale, cancellation.Token);
            var rejected = (await connection.ReceiveAsync(cancellation.Token))!;
            if (rejected.MessageType != DesignerHostMessageTypes.Error
                || connection.GetPayload<ErrorPayload>(rejected)!.Code != "SOURCE_VERSION_CONFLICT")
                throw new InvalidOperationException("Stale reload was not rejected.");
            var invalid = CreateVsHostOpenDocumentPayload(source, 5);
            invalid.Checksum = "invalid";
            await connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "invalid", id, invalid, cancellation.Token);
            rejected = (await connection.ReceiveAsync(cancellation.Token))!;
            if (rejected.MessageType != DesignerHostMessageTypes.Error
                || connection.GetPayload<ErrorPayload>(rejected)!.Code != "DOCUMENT_CHECKSUM_MISMATCH")
                throw new InvalidOperationException("Real checksum corruption was not rejected.");
            var realSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"));
            await Open(realSource, 4);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (vm.CreateActiveAxamlPatch(realSource).PatchedText != realSource || vm.ActiveAxamlCapabilityReport?.Level != AxamlCapabilityLevel.PartiallyEditable)
                    throw new InvalidOperationException("Real MainWindow did not open as a partial document with opaque layout.");
            });
        }, cancellation.Token);
        try { Pump(exercise); }
        finally { window.CloseForBridgeShutdown(); }
    }

    private static void AssertRealDocumentExternalProcess(SmokeContext context)
    {
        var executable = Path.Combine(FindRepositoryRoot(), "AvaloniaDesigner.VsHost", "bin", "Debug", "net6.0", "AvaloniaDesigner.VsHost.exe");
        var pipe = $"{DesignerHostProtocol.PipePrefix}.real.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var process = Process.Start(new ProcessStartInfo(executable, $"--pipe {pipe}")
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true
        })!;
        try
        {
            using var client = NamedPipeProtocolConnection.CreateClient(pipe);
            client.ConnectAsync(cancellation.Token).GetAwaiter().GetResult();
            using var connection = new NamedPipeProtocolConnection(client);
            var paths = new[] { Path.Combine(FindRepositoryRoot(), "Samples", "VisualStudioPoC", "SimpleAvaloniaApp", "MainWindow.axaml"),
                Path.Combine(FindRepositoryRoot(), "Samples", "VisualStudioPoC", "ComplexAxamlVisualStudioRoundTrip", "MainWindow.axaml"),
                Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml") };
            long version = 0;
            foreach (var path in paths.Concat(paths.Reverse()))
            {
                var open = CreateVsHostOpenDocumentPayload(File.ReadAllText(path), ++version);
                open.FilePath = path;
                connection.SendAsync(DesignerHostMessageTypes.OpenDocument, "open" + version, path, open, cancellation.Token).GetAwaiter().GetResult();
                var response = connection.ReceiveAsync(cancellation.Token).GetAwaiter().GetResult()!;
                if (response.MessageType != DesignerHostMessageTypes.DocumentOpened)
                    throw new InvalidOperationException($"External VsHost failed to open {path}: {response.Payload}");
                var capabilities = connection.GetPayload<DocumentOpenedPayload>(response)!;
                if (!capabilities.CanEdit || capabilities.CapabilityLevel != nameof(AxamlCapabilityLevel.PartiallyEditable)
                    || capabilities.Status.Contains("только для чтения") || capabilities.Status.Contains("Ограниченный режим"))
                    throw new InvalidOperationException($"Partial source was presented as read-only: {path}: {capabilities.Status}");
                Console.WriteLine($"REAL_VSHOST_OPEN path={path}; length={open.Text.Length}; checksum={open.Checksum}; capability={connection.GetPayload<DocumentOpenedPayload>(response)!.CapabilityLevel}");
            }
            connection.SendAsync(DesignerHostMessageTypes.HostShutdown, "shutdown", "", new EmptyPayload(), cancellation.Token).GetAwaiter().GetResult();
            if (!process.WaitForExit(10_000)) throw new TimeoutException("VsHost shutdown failed.");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        }
    }
}
