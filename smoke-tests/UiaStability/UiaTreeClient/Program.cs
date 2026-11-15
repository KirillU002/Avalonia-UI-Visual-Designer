using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

// Diagnostic MTA client using the shipped Windows desktop UIA wrapper. It never
// enumerates the desktop, reads text values, or invokes actions on user windows.
internal static class Program
{
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    private static int Main(string[] args)
    {
        var targetPid = uint.Parse(args[0]);
        Console.WriteLine("READY"); Console.Out.Flush();
        while (Console.ReadLine() is { } request && request != "STOP")
        {
            var fields = request.Split(' '); var hwnd = new IntPtr(long.Parse(fields[0]));
            GetWindowThreadProcessId(hwnd, out var actualPid);
            if (targetPid != actualPid) throw new InvalidOperationException("HWND is not owned by the test PID.");
            var nodes = new List<AutomationElement>();
            void Walk(AutomationElement item, int depth)
            {
                if (nodes.Count >= 128 || depth > 8) return;
                nodes.Add(item);
                try
                {
                    for (var child = TreeWalker.RawViewWalker.GetFirstChild(item); child is not null && nodes.Count < 128; child = TreeWalker.RawViewWalker.GetNextSibling(child)) Walk(child, depth + 1);
                }
                catch (ElementNotAvailableException) { }
            }
            Walk(AutomationElement.FromHandle(hwnd), 0);
            var queries = 0; var unavailable = 0; var valueProviders = 0;
            foreach (var node in nodes)
                try { if (node.GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty) is true) valueProviders++; queries++; }
                catch (ElementNotAvailableException) { unavailable++; }
            Console.WriteLine($"ACQUIRED 00000000 {nodes.Count} {valueProviders}"); Console.Out.Flush();
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < Math.Clamp(int.Parse(fields[1]), 1, 10000))
            {
                foreach (var node in nodes)
                    try { _ = node.GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty); queries++; }
                    catch (ElementNotAvailableException) { unavailable++; }
                Thread.Sleep(1);
            }
            Console.WriteLine($"DONE {queries} {unavailable} {nodes.Count} {valueProviders}"); Console.Out.Flush();
        }
        return 0;
    }
}
