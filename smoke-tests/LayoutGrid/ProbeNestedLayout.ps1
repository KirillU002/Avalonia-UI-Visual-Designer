param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference = 'Stop'
$probeRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$programPath = Join-Path $probeRoot 'Program.cs'
$probePath = Join-Path $probeRoot 'GeneratedNestedLayoutProbe.cs'
if (Test-Path -LiteralPath $probePath) { throw 'Probe already exists.' }
$project = @(Get-ChildItem -LiteralPath $probeRoot -Filter '*.csproj')
if ($project.Count -ne 1) { throw 'Expected one generated project.' }
$originalBytes = [System.IO.File]::ReadAllBytes($programPath)
$original = [System.Text.Encoding]::UTF8.GetString($originalBytes)
$marker = 'BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)'
if (-not $original.Contains($marker)) { throw 'Generated entry point was not found.' }
$probe = @'
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
public static class GeneratedNestedLayoutProbe
{
    public static void Start(AppBuilder builder, string[] args)
    {
        builder.SetupWithoutStarting();
        var type = Assembly.GetExecutingAssembly().GetTypes().Single(t => t.Name == "MainWindow" && typeof(Window).IsAssignableFrom(t));
        var window = (Window)Activator.CreateInstance(type)!;
        window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false;
        try
        {
            window.Show(); Pump(250); window.UpdateLayout();
            if (window.Content is not Grid) throw new Exception("Root is not Grid.");
            var dock = window.FindControl<DockPanel>("DockBody")!;
            var header = (StackPanel)window.FindControl<Border>("HeaderStack")!.Child!;
            var wrap = (WrapPanel)window.FindControl<Border>("FooterWrap")!.Child!;
            var button = window.FindControl<Button>("ApplyButton")!;
            var input = window.FindControl<TextBox>("SearchInput")!;
            var data = window.FindControl<DataGrid>("NestedDataGrid")!;
            if (!button.GetVisualAncestors().Contains(header) || !data.GetVisualAncestors().Contains(dock) || !dock.LastChildFill || wrap.Children.Count != 7)
                throw new Exception("Generated nested hierarchy is incorrect.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(80);
            if (input.Text != "nested runtime action") throw new Exception("Moved nested button lost runtime Logic/NameScope.");
            var before = data.Bounds.Size; var wrapBefore = wrap.DesiredSize.Height;
            var rowsBefore = wrap.Children.Select(c => c.Bounds.Y).Distinct().Count();
            window.Width += 250; window.Height += 120; Pump(250); window.UpdateLayout();
            var rowsAfter = wrap.Children.Select(c => c.Bounds.Y).Distinct().Count();
            if (data.Bounds.Width <= before.Width + 200 || data.Bounds.Height <= before.Height + 70 || rowsBefore <= 1 || rowsAfter != 1 || wrap.DesiredSize.Height >= wrapBefore)
                throw new Exception("Generated Dock fill or Wrap reflow failed after resize.");
            Console.WriteLine($"NESTED_RUNTIME_OK root=Grid hierarchy=StackPanel/DockPanel/WrapPanel logic=passed dataBefore={before} dataAfter={data.Bounds.Size} wrapRows={rowsBefore}->{rowsAfter} wrapDesired={wrapBefore}->{wrap.DesiredSize.Height}");
        }
        finally { window.Close(); }
    }
    private static void Pump(int milliseconds) { using var stop = new CancellationTokenSource(milliseconds); Dispatcher.UIThread.MainLoop(stop.Token); }
}
'@
try {
    [System.IO.File]::WriteAllText($programPath, $original.Replace($marker, 'global::GeneratedNestedLayoutProbe.Start(BuildAvaloniaApp(), args)'), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($probePath, $probe, [System.Text.UTF8Encoding]::new($false))
    & dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Instrumented generated build failed.' }
    [xml]$projectXml = Get-Content -LiteralPath $project[0].FullName -Raw
    $framework = [string]$projectXml.Project.PropertyGroup.TargetFramework
    $assemblyPath = Join-Path $probeRoot ('bin\Debug\' + $framework + '\' + $project[0].BaseName + '.dll')
    $stdout = Join-Path $probeRoot 'nested-runtime.stdout.log'; $stderr = Join-Path $probeRoot 'nested-runtime.stderr.log'
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"' + $assemblyPath + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Generated runtime timed out.' }
    $process.WaitForExit(); Get-Content -LiteralPath $stdout
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw 'Generated runtime failed.' }
}
finally {
    [System.IO.File]::WriteAllBytes($programPath, $originalBytes)
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath }
}
& dotnet build $project[0].FullName --no-restore -m:1 --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Restored generated build failed.' }
