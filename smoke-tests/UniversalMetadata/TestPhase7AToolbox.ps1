param([Parameter(Mandatory)][string]$RunnerDirectory,[Parameter(Mandatory)][string]$StandaloneDirectory,
      [Parameter(Mandatory)][string]$Vsix,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw "Каталог результатов уже существует: $out"}
New-Item -ItemType Directory -Path $out | Out-Null
$runner=(Resolve-Path $RunnerDirectory).Path
$standalone=(Resolve-Path $StandaloneDirectory).Path
$package=(Resolve-Path $Vsix).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($package,(Join-Path $out 'package'))
foreach($kind in @('standalone','vshost')){
    $dir=if($kind -eq 'vshost'){Join-Path $out 'package/VsHost'}else{
        $copy=Join-Path $out 'standalone'
        Copy-Item -LiteralPath $standalone -Destination $copy -Recurse
        $copy
    }
    # Only the test entry point and, for the standalone probe, the test's host reference.
    # All production assemblies and plugins of the VSIX are read from the ZIP payload.
    foreach($name in @('FormDesigner.ExportSmokeTests.dll','FormDesigner.ExportSmokeTests.deps.json','FormDesigner.ExportSmokeTests.runtimeconfig.json')){
        Copy-Item -LiteralPath (Join-Path $runner $name) -Destination $dir
    }
    if($kind -eq 'standalone'){
        foreach($name in @('AvaloniaDesigner.VsHost.dll','AvaloniaDesigner.Host.Protocol.dll')){
            if(!(Test-Path (Join-Path $dir $name))){Copy-Item (Join-Path $runner $name) -Destination $dir}
        }
    }
    $dotnet=(Get-Command dotnet).Source
    & $dotnet smoke-tests/UiaStability/NativeCapture/bin/Release/net8.0/NativeCapture.dll "$out/$kind-capture" $dotnet -- (Join-Path $dir 'FormDesigner.ExportSmokeTests.dll') --phase7a-host $kind "$out/$kind.json" *> "$out/$kind.log"
    if($LASTEXITCODE -ne 0){throw "$kind startup failed. See $out/$kind.log"}
}
$a=Get-Content -Raw -LiteralPath "$out/standalone.json" | ConvertFrom-Json
$b=Get-Content -Raw -LiteralPath "$out/vshost.json" | ConvertFrom-Json
foreach($key in @('Rows','Searches','ImportedCards','Descriptors','Metadata','FrameworkControls','Plugins')){
    if((ConvertTo-Json -Depth 12 -Compress -InputObject $a.$key) -cne (ConvertTo-Json -Depth 12 -Compress -InputObject $b.$key)){throw "Различается $key"}
}
if($a.Rows.Count -eq 0 -or $a.Rows.Count -ne $a.Descriptors.Count -or ($a.Rows.Id | Sort-Object -Unique).Count -ne $a.Rows.Count){throw 'Missing or duplicate registration'}
if ($a.ImportedCards.Count -ne $a.Rows.Count) {throw "Imported Toolbox visibility differs"}
if ($a.FrameworkControls.Count -ne $a.Metadata.Count -or $a.FrameworkControls.Count -eq 0) {throw 'Missing metadata/recognition inventory'}
if(@($a.Rows | Where-Object { !$_.Type -or !$_.Assembly }).Count){throw 'Missing deployed native type identity'}
if (@($a.Plugins | Where-Object { $_.Status -ne 0 -and $_.Status -ne 1 }).Count) {throw 'Packaged plugin discovery failed'}
if ((Test-Path (Join-Path $standalone 'Plugins/EremexDesignerPlugin/EremexDesignerPlugin.dll')) -and
    $a.Rows.Id -notcontains 'Eremex.TextEditor') {throw 'Installed Eremex plugin did not contribute Toolbox controls'}
Write-Output "TOOLBOX_PARITY PASS $($a.Rows.Count) ordered descriptors; $(@($a.Searches.PSObject.Properties).Count) search queries."
