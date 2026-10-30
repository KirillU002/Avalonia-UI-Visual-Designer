param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference = 'Stop'
$probeRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$programPath = Join-Path $probeRoot 'Program.cs'
$probePath = Join-Path $probeRoot 'GeneratedLayoutProbe.cs'
if (Test-Path -LiteralPath $probePath) { throw 'Probe file already exists; refusing to overwrite it.' }
$project = @(Get-ChildItem -LiteralPath $probeRoot -Filter '*.csproj')
if ($project.Count -ne 1) { throw 'Expected one generated project.' }
$originalBytes = [System.IO.File]::ReadAllBytes($programPath)
$original = [System.Text.Encoding]::UTF8.GetString($originalBytes)
$marker = 'BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)'
if (-not $original.Contains($marker)) { throw 'Generated Program entry point was not found.' }
$probe = @'
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

public static class GeneratedLayoutProbe
{
    public static void Start(AppBuilder builder, string[] args)
    {
        builder.SetupWithoutStarting();
        var windowType = Assembly.GetExecutingAssembly().GetTypes().Single(t => t.Name == "MainWindow" && typeof(Window).IsAssignableFrom(t));
        var window = (Window)Activator.CreateInstance(windowType)!;
        window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false;
        try
        {
            window.Show(); Pump(250); window.UpdateLayout();
            if (window.Content is not Grid root) throw new Exception("Generated Window root is not Grid.");
            var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
            if (!window.GetVisualDescendants().OfType<TextBox>().Any() || !window.GetVisualDescendants().OfType<Button>().Any()) throw new Exception("Generated controls were lost.");
            var before = grid.Bounds.Size;
            window.Width += 240; window.Height += 120; window.UpdateLayout(); Pump(200);
            var after = grid.Bounds.Size;
            if (after.Width < before.Width + 220 || after.Height < before.Height + 100) throw new Exception("Generated Grid did not resize: " + before + " -> " + after);
            if (!double.IsNaN(root.Width) || !double.IsNaN(root.Height)) throw new Exception("Generated root has fixed dimensions.");
            Console.WriteLine("LAYOUT_RUNTIME_OK runtime=" + Environment.Version + " before=" + before + " after=" + after + " root=" + root.GetType().Name);
        }
        catch (Exception ex) { Console.WriteLine("LAYOUT_RUNTIME_FAILED " + ex); Environment.ExitCode = 1; }
        finally { window.Close(); }
    }
    private static void Pump(int milliseconds)
    {
        using var cancellation = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(cancellation.Token);
    }
}
'@
try {
    [System.IO.File]::WriteAllText($programPath, $original.Replace($marker, 'global::GeneratedLayoutProbe.Start(BuildAvaloniaApp(), args)'), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($probePath, $probe, [System.Text.UTF8Encoding]::new($false))
    & dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Instrumented generated project failed to build.' }
    [xml]$projectXml = Get-Content -LiteralPath $project[0].FullName -Raw
    $framework = [string]$projectXml.Project.PropertyGroup.TargetFramework
    $assemblyPath = Join-Path $probeRoot ('bin\Debug\' + $framework + '\' + $project[0].BaseName + '.dll')
    $stdout = Join-Path $probeRoot 'layout-runtime.stdout.log'
    $stderr = Join-Path $probeRoot 'layout-runtime.stderr.log'
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"' + $assemblyPath + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Generated runtime layout probe timed out.' }
    $process.WaitForExit()
    Get-Content -LiteralPath $stdout
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw 'Generated runtime layout probe failed.' }
}
finally {
    [System.IO.File]::WriteAllBytes($programPath, $originalBytes)
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath }
}
# Leave a normally runnable generated binary after a successful probe.
& dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Restored generated project failed to build.' }
