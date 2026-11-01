$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$output=Join-Path $root 'artifacts/layout-phase7a3'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$env:DROP_BENCH_OUTPUT_DIRECTORY=$output
$cases=@()
foreach($kind in @('Captions','Commit')){foreach($n in @(20,100,500)){foreach($depth in @(1,10,30)){$cases+=,@($kind,$n,$depth)}}}
foreach($kind in @('Dto','Serialize')){foreach($n in @(20,50,100,300,500)){$cases+=,@($kind,$n,1)}}
foreach($case in $cases){
    $kind,$n,$depth=$case
    $env:HISTORY_BENCH_KIND=$kind;$env:DROP_BENCH_COUNT=[string]$n;$env:HISTORY_BENCH_DEPTH=[string]$depth
    $log=Join-Path $output "history-$kind-$n-$depth.log"
    & dotnet $runner --history-cost-benchmark *> $log
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 12;throw "History benchmark failed: $kind/$n/$depth"}
    Get-Content -LiteralPath $log -Tail 1
}
