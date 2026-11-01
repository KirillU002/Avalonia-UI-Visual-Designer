param([int]$Samples=30,[int[]]$Counts=@(20,50,100,300,500),[switch]$UndoOnly,[switch]$DirectRestore,[string[]]$EditScenarios=@('Resize','Property'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$output=Join-Path $root 'artifacts/layout-phase7a3'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$env:DROP_BENCH_OUTPUT_DIRECTORY=$output
$env:DROP_BENCH_SAMPLES=[string]$Samples
$env:DROP_BENCH_LABEL='current'
Remove-Item Env:DROP_BENCH_HISTORY_PAIR -ErrorAction SilentlyContinue
Remove-Item Env:DROP_BENCH_ABLATION -ErrorAction SilentlyContinue
if($DirectRestore){$env:UNDO_BENCH_DIRECT_RESTORE='1'}else{Remove-Item Env:UNDO_BENCH_DIRECT_RESTORE -ErrorAction SilentlyContinue}
if(-not $UndoOnly){
    foreach($scenario in @('ToolboxGrid','ToolboxStackPanel','ToolboxDockPanel','ToolboxWrapPanel','ToolboxCanvas','Move','Reparent')){
        foreach($count in $Counts){
            $env:DROP_BENCH_SCENARIO=$scenario;$env:DROP_BENCH_COUNT=[string]$count
            $log=Join-Path $output "current-$scenario-$count.log"
            & dotnet $runner --layout-drop-benchmark *> $log
            if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 12;throw "Drop benchmark failed: $scenario/$count"}
            Get-Content -LiteralPath $log -Tail 1
        }
    }
}
foreach($scenario in $EditScenarios){
    foreach($count in $Counts){
        $env:UNDO_BENCH_SCENARIO=$scenario;$env:DROP_BENCH_COUNT=[string]$count
        $prefix=if($DirectRestore){'direct'}else{'undo'}
        $log=Join-Path $output "$prefix-$scenario-$count.log"
        & dotnet $runner --undo-feasibility-benchmark *> $log
        if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 12;throw "Undo benchmark failed: $scenario/$count"}
        Get-Content -LiteralPath $log -Tail 1
    }
}
