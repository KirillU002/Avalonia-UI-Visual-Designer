param([int]$Repeats=3,[string[]]$RunnerArguments=@('--preview-memory-checks'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$output=Join-Path $root 'artifacts/layout-phase7a2/crash-diagnostics'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runner=Join-Path $root 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$env:FORMDESIGNER_NATIVE_DIAGNOSTICS_DIRECTORY=$output
for($i=1;$i -le $Repeats;$i++){
    $start=[DateTimeOffset]::UtcNow
    $out=Join-Path $output "run-$i.stdout.log"
    $err=Join-Path $output "run-$i.stderr.log"
    $arguments=@('"'+$runner+'"')+$RunnerArguments
    $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    if(-not $process.WaitForExit(600000)){$process.Kill();throw 'Preview runner timeout.'}
    $process.WaitForExit()
    $dump=Join-Path $env:LOCALAPPDATA "CrashDumps/dotnet.exe.$($process.Id).dmp"
    $record=[pscustomobject]@{Run=$i;Pid=$process.Id;StartedUtc=$start;FinishedUtc=[DateTimeOffset]::UtcNow;ExitCode=$process.ExitCode;DumpAvailable=(Test-Path -LiteralPath $dump);DumpPath=$null}
    if($record.DumpAvailable){$record.DumpPath=$dump}
    $record | ConvertTo-Json | Set-Content (Join-Path $output "run-$i.result.json") -Encoding UTF8
    Get-Content -LiteralPath $out -Tail 2
    if($process.ExitCode -ne 0){Get-Content -LiteralPath $err -Tail 8;throw "Preview runner failed: $($process.ExitCode); pid=$($process.Id)"}
}
