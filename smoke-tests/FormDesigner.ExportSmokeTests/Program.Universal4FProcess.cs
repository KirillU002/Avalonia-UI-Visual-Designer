using AvaloniaDesigner.Host.Protocol;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal4FHostCycles()
    {
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("PHASE4F_OUTPUT")!);
        var executable = Path.GetFullPath(Environment.GetEnvironmentVariable("PHASE4F_HOST")!);
        var capture = Path.Combine(FindRepositoryRoot(), "smoke-tests/UiaStability/NativeCapture/bin/Release/net8.0/NativeCapture.dll");
        RequireCapability(!Directory.Exists(output), "Fresh process output required"); Directory.CreateDirectory(output);
        var results = new List<object>();
        // Five warm-ups, fifty independent processes. Each owns a separate pipe
        // and a crash capture directory, including its own PID and module list.
        for (var iteration = -5; iteration < 50; iteration++)
        {
            var folder = Path.Combine(output, "run-" + (iteration + 5).ToString("D2"));
            var pipe = DesignerHostProtocol.PipePrefix + ".phase4f." + Guid.NewGuid().ToString("N");
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("PHASE4F_DOTNET") ?? "dotnet")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (var argument in new[] { capture, folder, executable, "--", "--pipe", pipe }) info.ArgumentList.Add(argument);
            // Reserve and hash preservation are provided by the existing capture.
            info.Environment["UIA_CAPTURE_TIMEOUT_SECONDS"] = "90";
            info.Environment["UIA_CAPTURE_MANAGED_FATAL"] = "1";
            info.Environment["UIA_EVIDENCE_ROOT"] = Path.Combine(FindRepositoryRoot(), "artifacts/uia-evidence-vault");
            using var debugger = Process.Start(info)!;
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var samples = new Dictionary<string, double>();
            var watch = Stopwatch.StartNew();
            NamedPipeProtocolConnection? connection = null;
            long privateBytes = 0, workingSet = 0; int handles = 0, threads = 0;
            try
            {
                var stream = NamedPipeProtocolConnection.CreateClient(pipe);
                stream.ConnectAsync(cancel.Token).GetAwaiter().GetResult();
                connection = new NamedPipeProtocolConnection(stream);
                Request4F(connection, DesignerHostMessageTypes.Hello, "", new HelloPayload(), cancel.Token, DesignerHostMessageTypes.HelloAck);
                samples["LaunchHandshake"] = watch.Elapsed.TotalMilliseconds;
                var first = Source4F("First");
                watch.Restart();
                var opened = Request4F(connection, DesignerHostMessageTypes.OpenDocument, "first", CreateVsHostOpenDocumentPayload(first, 1), cancel.Token, DesignerHostMessageTypes.DocumentOpened);
                RequireCapability(connection.GetPayload<DocumentOpenedPayload>(opened)!.CanEdit, "Packaged host cannot edit supported source");
                samples["OpenSmall"] = watch.Elapsed.TotalMilliseconds;
                connection.SendAsync(DesignerHostMessageTypes.CloseDocument, "close", "first", new EmptyPayload(), cancel.Token).GetAwaiter().GetResult();
                Request4F(connection, DesignerHostMessageTypes.Hello, "", new HelloPayload(), cancel.Token, DesignerHostMessageTypes.HelloAck);
                watch.Restart();
                Request4F(connection, DesignerHostMessageTypes.OpenDocument, "second", CreateVsHostOpenDocumentPayload(Source4F("Second"), 2), cancel.Token, DesignerHostMessageTypes.DocumentOpened);
                samples["ReopenOtherDocument"] = watch.Elapsed.TotalMilliseconds;
                // The launcher publishes PID in its first complete JSONL line.
                var launched = File.ReadLines(Path.Combine(folder, "capture.jsonl")).First();
                using var launch = JsonDocument.Parse(launched);
                using (var host = Process.GetProcessById(launch.RootElement.GetProperty("Value").GetProperty("Pid").GetInt32()))
                {
                    privateBytes = host.PrivateMemorySize64; workingSet = host.WorkingSet64;
                    handles = host.HandleCount; threads = host.Threads.Count;
                }
            }
            finally
            {
                // No force-kill or global process enumeration: only this owned
                // host receives shutdown; capture enforces its own outer timeout.
                if (connection is not null)
                {
                    try { connection.SendAsync(DesignerHostMessageTypes.HostShutdown, "shutdown", "", new EmptyPayload(), cancel.Token).GetAwaiter().GetResult(); }
                    finally { connection.Dispose(); }
                }
                if (!debugger.WaitForExit(15000)) throw new InvalidOperationException("Owned host did not exit; preserve capture and diagnose");
            }
            using var outcome = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "result.json")));
            var result = outcome.RootElement;
            RequireCapability(debugger.ExitCode == 0 && result.GetProperty("ExitCode").GetInt32() == 0
                && !result.GetProperty("Captured").GetBoolean() && !result.GetProperty("CaptureFailed").GetBoolean()
                && !result.GetProperty("TimedOut").GetBoolean(), "Host process failed: " + folder);
            var pid = result.GetProperty("Pid").GetInt32();
            try { using var survivor = Process.GetProcessById(pid); RequireCapability(survivor.HasExited, "Owned VsHost survived shutdown"); }
            catch (ArgumentException) { }
            results.Add(new { Iteration = iteration, Pid = pid, Samples = samples, PrivateBytes = privateBytes,
                WorkingSet = workingSet, Handles = handles, Threads = threads, ExitCode = debugger.ExitCode });
            File.WriteAllText(Path.Combine(output, "process-results.json"), JsonSerializer.Serialize(results));
            Console.WriteLine($"PHASE4F_HOST iteration={iteration}; pid={pid}; exit=0; open={samples["OpenSmall"]:F2}ms");
        }
        Console.WriteLine("PHASE4F_HOST_CYCLES PASS 55 processes; 110 document opens; 55 closes; 55 shutdowns; zero survivors");
        return 0;
    }

    private static DesignerHostEnvelope Request4F<T>(NamedPipeProtocolConnection connection, string kind, string documentId,
        T payload, CancellationToken token, string expected)
    {
        var requestId = Guid.NewGuid().ToString("N");
        connection.SendAsync(kind, requestId, documentId, payload, token).GetAwaiter().GetResult();
        var response = connection.ReceiveAsync(token).GetAwaiter().GetResult();
        RequireCapability(response?.RequestId == requestId && response.DocumentId == documentId && response.MessageType == expected,
            "Unexpected response to " + kind + ": " + response?.MessageType);
        return response!;
    }
}
