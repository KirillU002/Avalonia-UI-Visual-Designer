param([string[]]$Scenarios=@('ToolboxGrid','Move','ToolboxCanvas'),[int[]]$Counts=@(20,100))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$output=Join-Path $root 'artifacts/layout-phase7a3/history-ui'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:DROP_BENCH_OUTPUT_DIRECTORY=$output;$env:DROP_BENCH_SAMPLES='30'
Remove-Item Env:DROP_BENCH_ABLATION -ErrorAction SilentlyContinue
foreach($scenario in $Scenarios){foreach($n in $Counts){foreach($depth in @(0,30)){
    $env:DROP_BENCH_SCENARIO=$scenario;$env:DROP_BENCH_COUNT=[string]$n
    $env:DROP_BENCH_HISTORY_PAIR=[string]$depth;$env:DROP_BENCH_LABEL="paired-H$depth"
    $log=Join-Path $output "$($env:DROP_BENCH_LABEL)-$scenario-$n.log"
    & dotnet $runner --layout-drop-benchmark *> $log
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 10;throw "Matched history UI failed $scenario/$n/$depth"}
    Get-Content -LiteralPath $log -Tail 1
}}}
