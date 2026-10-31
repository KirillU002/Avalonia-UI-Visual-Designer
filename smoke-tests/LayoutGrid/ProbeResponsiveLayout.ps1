param([Parameter(Mandatory=$true)][string]$ProjectPath, [Parameter(Mandatory=$true)][string]$Scene, [Parameter(Mandatory=$true)][string]$ReferencePath)
$ErrorActionPreference = 'Stop'
$probeRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$reference = (Resolve-Path -LiteralPath $ReferencePath).Path
$programPath = Join-Path $probeRoot 'Program.cs'
$probePath = Join-Path $probeRoot 'GeneratedResponsiveProbe.cs'
if (Test-Path -LiteralPath $probePath) { throw 'Probe exists.' }
$project = @(Get-ChildItem -LiteralPath $probeRoot -Filter '*.csproj')
if ($project.Count -ne 1) { throw 'Expected one project.' }
$originalBytes = [System.IO.File]::ReadAllBytes($programPath)
$original = [System.Text.Encoding]::UTF8.GetString($originalBytes)
$marker = 'BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)'
if (-not $original.Contains($marker)) { throw 'Entry point missing.' }
$probe = @'
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
public static class GeneratedResponsiveProbe
{
    public static void Start(AppBuilder builder, string[] args)
    {
        builder.SetupWithoutStarting();
        var scene = Environment.GetEnvironmentVariable("LAYOUT_PROBE_SCENE")!;
        var expected = JsonSerializer.Deserialize<List<Observation>>(File.ReadAllText(Environment.GetEnvironmentVariable("LAYOUT_PROBE_REFERENCE")!))!.Where(o => o.Scene == scene && o.Mode == "Designer").ToArray();
        if (expected.Length == 0) throw new Exception("No reference geometry.");
        var type = Assembly.GetExecutingAssembly().GetTypes().Single(t => t.Name == "MainWindow" && typeof(Window).IsAssignableFrom(t));
        var window = (Window)Activator.CreateInstance(type)!;
        window.Position = new PixelPoint(-4000,-4000); window.ShowInTaskbar = false; window.ShowActivated = false;
        var results = new List<Observation>(); var failures = new List<string>();
        try
        {
            window.Show(); Pump(250);
            if(scene=="Bound")
            {
                var grid=window.FindControl<Control>("Results");
                var rows=grid?.GetType().GetProperty("ItemsSource")?.GetValue(grid) as System.Collections.IEnumerable;
                if(rows?.Cast<object>().Any()!=true) throw new Exception("Generated manual BindingSource has no fixture rows.");
            }
            foreach (var size in expected.Select(o => (o.ViewportWidth,o.ViewportHeight)).Distinct())
            {
                window.Width = size.ViewportWidth; window.Height = size.ViewportHeight; Pump(220); window.UpdateLayout();
                var current = expected.Where(o => o.ViewportWidth == size.ViewportWidth && o.ViewportHeight == size.ViewportHeight).ToArray();
                var named = current.ToDictionary(e => e.Id, e => window.FindControl<Control>(e.Name) ?? throw new Exception("Missing generated control " + e.Name));
                foreach (var e in expected.Where(o => o.ViewportWidth == size.ViewportWidth && o.ViewportHeight == size.ViewportHeight))
                {
                    var c = named[e.Id];
                    var parent = c.GetVisualAncestors().OfType<Control>().FirstOrDefault(v => named.Values.Contains(v));
                    var parentId = parent is null ? "" : named.Single(p => ReferenceEquals(p.Value,parent)).Key;
                    Control? panel = parent ?? window.Content as Control;
                    if (panel is Border border) panel = border.Child;
                    var layout = panel switch { Grid => "Grid", StackPanel => "Stack", WrapPanel => "Flex", DockPanel => "Dock", Canvas => "Absolute", _ => "Unknown" };
                    var p = c.TranslatePoint(default, window)!.Value;
                    var a = e with { Mode = "Runtime", ParentId = parentId, Layout = layout, X = p.X, Y = p.Y, Width = c.Bounds.Width, Height = c.Bounds.Height, ViewportWidth = window.ClientSize.Width, ViewportHeight = window.ClientSize.Height };
                    results.Add(a);
                    var delta = new[] { Math.Abs(a.X-e.X),Math.Abs(a.Y-e.Y),Math.Abs(a.Width-e.Width),Math.Abs(a.Height-e.Height) }.Max();
                    if (delta > 1.1 || a.ParentId != e.ParentId || a.Layout != e.Layout || Math.Abs(a.ViewportWidth-e.ViewportWidth) > .1 || Math.Abs(a.ViewportHeight-e.ViewportHeight) > .1)
                        failures.Add($"{scene} {size} {e.Name}: designer=({e.X},{e.Y},{e.Width},{e.Height}); runtime=({a.X},{a.Y},{a.Width},{a.Height}); client={window.ClientSize}; delta={delta}");
                }
            }
            File.WriteAllText("runtime-observations.json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var failure in failures) Console.WriteLine("RESPONSIVE_RUNTIME_MISMATCH " + failure);
            Console.WriteLine($"RESPONSIVE_RUNTIME scene={scene}; measurements={results.Count}; failures={failures.Count}");
        }
        finally { window.Close(); }
        if (failures.Count != 0) throw new Exception("Generated runtime geometry differs from Designer.");
    }
    private static void Pump(int milliseconds) { using var stop = new CancellationTokenSource(milliseconds); Dispatcher.UIThread.MainLoop(stop.Token); }
    public sealed record Observation(string Scene,string Mode,double ViewportWidth,double ViewportHeight,string Id,string Name,string ParentId,string Layout,double X,double Y,double Width,double Height);
}
'@
$env:LAYOUT_PROBE_SCENE = $Scene; $env:LAYOUT_PROBE_REFERENCE = $reference
try {
    [System.IO.File]::WriteAllText($programPath, $original.Replace($marker, 'global::GeneratedResponsiveProbe.Start(BuildAvaloniaApp(), args)'), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($probePath, $probe, [System.Text.UTF8Encoding]::new($false))
    & dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Instrumented build failed.' }
    [xml]$xml = Get-Content -LiteralPath $project[0].FullName -Raw
    $framework = [string]$xml.Project.PropertyGroup.TargetFramework
    $assembly = Join-Path $probeRoot ('bin\Debug\' + $framework + '\' + $project[0].BaseName + '.dll')
    $stdout = Join-Path $probeRoot 'runtime.stdout.log'; $stderr = Join-Path $probeRoot 'runtime.stderr.log'
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"' + $assembly + '"') -WorkingDirectory $probeRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Generated runtime timed out.' }
    $process.WaitForExit(); Get-Content -LiteralPath $stdout
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw 'Generated runtime failed.' }
}
finally {
    [System.IO.File]::WriteAllBytes($programPath, $originalBytes)
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath }
    & dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
}
