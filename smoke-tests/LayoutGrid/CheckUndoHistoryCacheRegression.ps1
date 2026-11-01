$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$output=Join-Path $root 'artifacts/undo-history-cache-poc/regression'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:FORMDESIGNER_UNDO_HISTORY_CACHE='1'
$failures=[Collections.Generic.List[string]]::new()
$env:DROP_BENCH_OUTPUT_DIRECTORY=$output
foreach($name in @('SMOKE_SCENARIO_FILTER','SMOKE_SCENARIO_NAMES_FILE','FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS','FORMDESIGNER_LAYOUT_DROP_PROFILE','DROP_BENCH_HISTORY_PAIR','FORMDESIGNER_TEST_SQLSERVER','FORMDESIGNER_TEST_POSTGRES')){
    Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
}
foreach($suite in @('undo-history-cache','layout72','layout7a','nested-layout','grid-drop','grid-editor','native-grid','designer-pointer','layout-validation','stabilization','datagrid-preview','postgres','preview-memory')){
    $log=Join-Path $output "$suite.log"
    & dotnet $runner "--$suite-checks" *> $log
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $log -Tail 15;$failures.Add($suite)}
    Get-Content -LiteralPath $log -Tail 1
}
$env:SMOKE_SCENARIO_NAMES_FILE=Join-Path $root 'artifacts/layout-phase7a/regression-scenarios.txt'
& dotnet $runner (Join-Path $output 'broad') *> (Join-Path $output 'broad.log')
if($LASTEXITCODE -ne 0){Get-Content -LiteralPath (Join-Path $output 'broad.log') -Tail 20;$failures.Add('broad')}
Get-Content -LiteralPath (Join-Path $output 'broad.log') -Tail 5
if($failures.Count){Write-Output ('Failed suites: '+($failures -join ', '));exit 1}
