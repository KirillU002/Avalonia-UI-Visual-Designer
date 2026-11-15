#Requires -Version 7.0
param(
    [string]$Runner = 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll',
    [string]$Output = 'artifacts/universal-phase3b1/stress',
    [string[]]$Scenarios = @('plain','editor','preview-legacy','preview-axaml','popup','menu','mutations','undo','forms','project'),
    [int]$Cycles = 100,
    [int]$Runs = 1,
    [switch]$Client,
    [string]$ClientDll = 'smoke-tests/UiaStability/UiaClient/bin/Release/net8.0/UiaClient.dll',
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$runnerPath = (Resolve-Path -LiteralPath $Runner).Path
$clientPath = if($Client){(Resolve-Path -LiteralPath $ClientDll).Path}else{''}
$outputPath = [IO.Path]::GetFullPath($Output)
if((Test-Path -LiteralPath $outputPath) -and @(Get-ChildItem -LiteralPath $outputPath -Force).Count){throw 'Output directory is not empty. Use a fresh path to preserve earlier failures.'}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$results = @()
foreach($scenario in $Scenarios){
    if($scenario -notmatch '^[a-z-]+$'){throw 'Invalid scenario name'}
    for($run=1;$run -le $Runs;$run++){
        $folder=Join-Path $outputPath ($scenario+'-'+$run)
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $env:UIA_STRESS_SCENARIO=$scenario
        $env:UIA_STRESS_CYCLES=$Cycles.ToString()
        $env:UIA_STRESS_OUTPUT=$folder
        $env:FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY=Join-Path $folder 'native'
        if($Client){$env:UIA_STRESS_CLIENT=$clientPath}else{Remove-Item Env:UIA_STRESS_CLIENT -ErrorAction SilentlyContinue}
        $timer=[Diagnostics.Stopwatch]::StartNew()
        $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"'+$runnerPath+'" --uia-stability') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $folder 'stdout.log') -RedirectStandardError (Join-Path $folder 'stderr.log')
        $timedOut=-not $process.WaitForExit($TimeoutSeconds*1000)
        if($timedOut){$process.Kill($true);$process.WaitForExit()}
        $summary=if(Test-Path -LiteralPath (Join-Path $folder 'result.json')){Get-Content -LiteralPath (Join-Path $folder 'result.json') -Raw|ConvertFrom-Json}else{$null}
        $entry=[pscustomobject]@{Scenario=$scenario;Run=$run;Pid=$process.Id;Client=$Client.IsPresent;ExitCode=$process.ExitCode;TimedOut=$timedOut;ElapsedMs=$timer.ElapsedMilliseconds;Completed=$summary.Completed;Success=($process.ExitCode -eq 0 -and $null -ne $summary -and $summary.Completed -eq $Cycles);Summary=$summary}
        $results+=$entry
        $results|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $outputPath 'results.json') -Encoding utf8
        Write-Output ($scenario+' #'+$run+' exit='+$process.ExitCode+' completed='+$summary.Completed+' elapsedMs='+$timer.ElapsedMilliseconds)
        if(-not $entry.Success){Get-Content -LiteralPath (Join-Path $folder 'stderr.log') -Tail 8}
        $process.Dispose()
    }
}
Remove-Item Env:UIA_STRESS_SCENARIO,Env:UIA_STRESS_CYCLES,Env:UIA_STRESS_OUTPUT,Env:UIA_STRESS_CLIENT,Env:FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY -ErrorAction SilentlyContinue
if(@($results|Where-Object {-not $_.Success}).Count){exit 1}
