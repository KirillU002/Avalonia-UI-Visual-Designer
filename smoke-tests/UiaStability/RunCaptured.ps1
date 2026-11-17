#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('minimal','editor','differential')][string]$Scenario='minimal',
    [string[]]$Controls=@('TextBox'),
    [ValidateSet('plain','editor','mutations','undo','forms','project','preview-legacy','preview-axaml','preview-imported','menu','popup')][string]$StressScenario='mutations',
    [ValidateSet('normal','root-disconnect')][string]$Close='normal',
    [ValidateSet('owner','popup')][string]$Scope='owner',
    [int]$Cycles=200,
    [string]$Target='smoke-tests/UiaStability/MinimalAvalonia/bin/Release/net8.0/MinimalAvalonia.dll',
    [switch]$Client,
    [string]$ClientDll='smoke-tests/UiaStability/UiaTreeClient/bin/Release/net8.0-windows/UiaTreeClient.dll'
)
$ErrorActionPreference='Stop'
if($Scenario -eq 'editor'){
    if(-not $PSBoundParameters.ContainsKey('Controls')){$Controls=@('TreeView')}
    if(-not $PSBoundParameters.ContainsKey('Target')){$Target='smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'}
}
$folder=[IO.Path]::GetFullPath($Output)
if((Test-Path -LiteralPath $folder) -and @(Get-ChildItem -LiteralPath $folder -Force).Count){throw 'Use a fresh output directory; previous failures must be preserved.'}
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$capture=(Resolve-Path smoke-tests/UiaStability/NativeCapture/bin/Release/net8.0/NativeCapture.dll).Path
$targetPath=(Resolve-Path -LiteralPath $Target).Path
$dotnet=(Get-Command dotnet).Source
$results=@()
try{
    if($Client){$env:UIA_STRESS_CLIENT=(Resolve-Path -LiteralPath $ClientDll).Path}else{Remove-Item Env:UIA_STRESS_CLIENT -ErrorAction SilentlyContinue}
    foreach($kind in $Controls){
        if($kind -notmatch '^[A-Za-z]+$'){throw 'Invalid control name'}
        $case=Join-Path $folder $kind
        $env:UIA_PROBE_OUTPUT=$case
        $env:UIA_PROBE_CONTROL=$kind
        $env:UIA_PROBE_CLOSE=$Close
        $env:UIA_PROBE_SCOPE=$Scope
        $env:UIA_PROBE_CYCLES=$Cycles.ToString()
        $env:UIA_STRESS_CONTROL=$kind
        $env:UIA_STRESS_SCENARIO=$StressScenario
        $env:UIA_STRESS_OUTPUT=$case
        $env:UIA_STRESS_CYCLES=$Cycles.ToString()
        $env:UIA_STRESS_RELEASE_CLIENT_FIRST='1'
        $env:FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY=Join-Path $case 'native'
        $arguments=@($targetPath)
        if($Scenario -eq 'editor'){$arguments+='--uia-stability'}
        if($Scenario -eq 'differential'){$arguments+='--uia-differential'}
        & $dotnet $capture ($case+'-capture') $dotnet -- @arguments *> ($case+'.log')
        $exitCode=$LASTEXITCODE
        $summary=if(Test-Path -LiteralPath (Join-Path $case 'result.json')){Get-Content -LiteralPath (Join-Path $case 'result.json') -Raw | ConvertFrom-Json}else{$null}
        $captureResult=if(Test-Path -LiteralPath ($case+'-capture/result.json')){Get-Content -LiteralPath ($case+'-capture/result.json') -Raw|ConvertFrom-Json}else{$null}
        $entry=[pscustomobject]@{Scenario=$Scenario;Control=$kind;ExitCode=$exitCode;Result=$summary;Capture=$captureResult}
        $results+=$entry
        $results|ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $folder 'results.json') -Encoding utf8
        Write-Output ($Scenario+'/'+$kind+': exit='+$exitCode+' completed='+$summary.Completed)
        if($exitCode -ne 0 -or $null -eq $summary -or $summary.Completed -ne $Cycles -or $null -eq $captureResult -or $captureResult.CaptureFailed -or $captureResult.Captured){throw 'Stopped on first failed observation or captured exception. See original log and capture result.'}
    }
}finally{
    Remove-Item Env:UIA_PROBE_OUTPUT,Env:UIA_PROBE_CONTROL,Env:UIA_PROBE_CLOSE,Env:UIA_PROBE_SCOPE,Env:UIA_PROBE_CYCLES,Env:UIA_STRESS_CONTROL,Env:UIA_STRESS_SCENARIO,Env:UIA_STRESS_OUTPUT,Env:UIA_STRESS_CYCLES,Env:UIA_STRESS_RELEASE_CLIENT_FIRST,Env:UIA_STRESS_CLIENT,Env:FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY -ErrorAction SilentlyContinue
}
