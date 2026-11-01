param([ValidateSet('Vm','Ui','Captions')][string]$Family='Vm',[int[]]$Counts=@(20,50,100,300,500),[int[]]$Depths=@(0,10,30),[int]$Samples=30,[string]$OutputRoot='',
    [string[]]$Scenarios=@('ToolboxCanvas','ToolboxGrid','ToolboxDockPanel','Move','Reparent'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
if(-not $OutputRoot){$OutputRoot=Join-Path $root 'artifacts/undo-history-cache-poc'}
$output=Join-Path $OutputRoot $Family.ToLowerInvariant()
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:DROP_BENCH_OUTPUT_DIRECTORY=$output;$env:DROP_BENCH_SAMPLES=[string]$Samples
$env:DROP_BENCH_DIRECT_UNDO='1'
$env:HISTORY_CACHE_PAIRS_ONLY=if($Family -eq 'Captions'){'1'}else{'0'}
foreach($name in @('DROP_BENCH_ABLATION','FORMDESIGNER_LAYOUT_DROP_PROFILE','FORMDESIGNER_LAYOUT_DROP_STACKS','FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS','SMOKE_SCENARIO_FILTER')){
    Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
}
foreach($n in $Counts){foreach($h in $Depths){
    # Alternate order to limit process warm-up/thermal ordering bias; never run arms concurrently.
    $arms=if(($n+$h)%3 -eq 0){@(1,0)}else{@(0,1)}
    $cases=if($Family -ne 'Ui'){@($Family)}else{$Scenarios}
    foreach($scenario in $cases){foreach($enabled in $arms){
        $label=if($enabled){'on'}else{'off'}
        $env:FORMDESIGNER_UNDO_HISTORY_CACHE=[string]$enabled
        $env:DROP_BENCH_COUNT=[string]$n;$env:HISTORY_BENCH_DEPTH=[string]$h
        $env:DROP_BENCH_HISTORY_PAIR=[string]$h;$env:DROP_BENCH_SCENARIO=$scenario;$env:DROP_BENCH_LABEL="cache-$label-H$h"
        $file=if($Family -ne 'Ui'){"$($Family.ToLowerInvariant())-$label-$n-$h"}else{"cache-$label-H$h-$scenario-$n"}
        if(Test-Path -LiteralPath (Join-Path $output "$file.json")){Write-Output "EXISTS $file";continue}
        $log=Join-Path $output "$file.log"
        $arg=if($Family -ne 'Ui'){'--undo-history-cache-benchmark'}else{'--layout-drop-benchmark'}
        & dotnet $runner $arg *> $log
        if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 12;throw "History cache benchmark failed: $file"}
        Get-Content -LiteralPath $log -Tail 1
    }}
}}
