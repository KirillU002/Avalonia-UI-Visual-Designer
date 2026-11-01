param([string]$Label='current',[int]$Samples=30,[string[]]$Scenarios=@('ToolboxGrid','ToolboxStackPanel','ToolboxDockPanel','ToolboxWrapPanel','ToolboxCanvas','Move','Reparent'),[int[]]$Counts=@(50,100,300,500))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$output=Join-Path $root 'artifacts/layout-phase7a2'
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($scenario in $Scenarios){foreach($count in $Counts){
    $env:DROP_BENCH_LABEL=$Label
    $env:DROP_BENCH_SCENARIO=$scenario
    $env:DROP_BENCH_COUNT=[string]$count
    $env:DROP_BENCH_SAMPLES=[string]$Samples
    $log=Join-Path $output "$Label-$scenario-$count.log"
    & dotnet $runner --layout-drop-benchmark *> $log
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 15;throw "Benchmark failed: $scenario $count"}
    Get-Content -LiteralPath $log -Tail 1
}}
