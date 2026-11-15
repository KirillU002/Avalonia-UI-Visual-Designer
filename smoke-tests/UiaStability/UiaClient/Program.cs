using System.Diagnostics;
using System.Runtime.InteropServices;

// An owned MTA diagnostic client. It queries only HWNDs belonging to the supplied
// test PID. Legacy node APIs intentionally match the historical native stack;
// they are not an accessibility implementation or a production dependency.
internal static class Program
{
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("oleaut32.dll")] private static extern int VariantClear(IntPtr value);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("uiautomationcore.dll")] private static extern int UiaNodeFromHandle(IntPtr hwnd, out IntPtr node);
    [DllImport("uiautomationcore.dll")] private static extern int UiaGetPropertyValue(IntPtr node, int property, IntPtr value);
    [DllImport("uiautomationcore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UiaNodeRelease(IntPtr node);

    private static int Main(string[] args)
    {
        var expectedPid = uint.Parse(args[0]);
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0)); // MTA
        try
        {
            Console.WriteLine("READY"); Console.Out.Flush();
            while (Console.ReadLine() is { } request && request != "STOP")
            {
                var fields = request.Split(' ');
                var hwnd = new IntPtr(long.Parse(fields[0]));
                var duration = Math.Clamp(int.Parse(fields[1]), 1, 10000);
                GetWindowThreadProcessId(hwnd, out var actualPid);
                if (actualPid != expectedPid) throw new InvalidOperationException("HWND is not owned by the test PID.");
                var hr = UiaNodeFromHandle(hwnd, out var node);
                Console.WriteLine($"ACQUIRED {hr:X8}"); Console.Out.Flush();
                var queries = 0; var failures = 0;
                if (node != IntPtr.Zero)
                {
                    var variant = Marshal.AllocCoTaskMem(24); // VARIANT on Windows x64
                    try
                    {
                        var watch = Stopwatch.StartNew();
                        while (watch.ElapsedMilliseconds < duration)
                        {
                            // IsValuePatternAvailable, the pattern implicated in both dumps.
                            for (var offset = 0; offset < 24; offset += 8) Marshal.WriteInt64(variant, offset, 0);
                            hr = UiaGetPropertyValue(node, 30043, variant);
                            if (hr < 0) failures++; else queries++;
                            VariantClear(variant);
                            Thread.Sleep(1);
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(variant); UiaNodeRelease(node); }
                }
                Console.WriteLine($"DONE {queries} {failures}"); Console.Out.Flush();
            }
            return 0;
        }
        finally { CoUninitialize(); }
    }
}
