#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ProjectPath,[string]$ProbeFile='MetadataRuntimeProbe.cs.txt',[string]$ResultName='generated-runtime.txt')
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectPath).Path
$project=@(Get-ChildItem -LiteralPath $root -File -Filter '*.csproj')
if($project.Count -ne 1){throw 'Expected one generated project'}
$entry=Join-Path $root 'App.axaml.cs';$probe=Join-Path $root 'MetadataRuntimeProbe.cs'
if(Test-Path -LiteralPath $probe){throw 'Fresh generated project required'}
$saved=[IO.File]::ReadAllBytes($entry)
if($ProbeFile -notmatch '^[A-Za-z0-9.-]+\.cs\.txt$' -or $ResultName -notmatch '^[A-Za-z0-9.-]+\.txt$'){throw 'Invalid probe/result filename'}
$env:METADATA_RUNTIME_RESULT=Join-Path (Split-Path -Parent $root) $ResultName
if(Test-Path -LiteralPath $env:METADATA_RUNTIME_RESULT){Remove-Item -LiteralPath $env:METADATA_RUNTIME_RESULT}
try{
 $source=[Text.Encoding]::UTF8.GetString($saved);$marker='desktop.MainWindow = new MainWindow();'
 if(-not $source.Contains($marker)){throw 'Generated startup marker missing'}
 [IO.File]::WriteAllText($entry,$source.Replace($marker,'{ '+$marker+' global::MetadataRuntimeProbe.Attach(desktop, desktop.MainWindow); }'),[Text.UTF8Encoding]::new($false))
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $ProbeFile) -Destination $probe
 & dotnet restore $project[0].FullName -v:q;if($LASTEXITCODE -ne 0){throw 'Generated restore failed'}
 & dotnet build $project[0].FullName -c Release --no-restore -m:1 -v:q;if($LASTEXITCODE -ne 0){throw 'Generated build failed'}
 [xml]$xml=Get-Content -LiteralPath $project[0].FullName -Raw
 $dll=Join-Path $root ('bin/Release/'+[string]$xml.Project.PropertyGroup.TargetFramework+'/'+$project[0].BaseName+'.dll')
 $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"'+$dll+'"') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'runtime.stdout.log') -RedirectStandardError (Join-Path $root 'runtime.stderr.log')
 if(-not $process.WaitForExit(45000)){$process.Kill();$process.WaitForExit();throw 'Generated native runtime timed out'}
 $process.WaitForExit()
 if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $env:METADATA_RUNTIME_RESULT)){throw 'Generated native runtime failed'}
 $resultText=Get-Content -LiteralPath $env:METADATA_RUNTIME_RESULT -Raw
 Write-Output $resultText
 if(-not $resultText.StartsWith('PASS',[StringComparison]::Ordinal)){throw 'Generated native runtime assertions failed'}
}finally{
 [IO.File]::WriteAllBytes($entry,$saved)
 if(Test-Path -LiteralPath $probe){Remove-Item -LiteralPath $probe}
 & dotnet build $project[0].FullName -c Release --no-restore -m:1 -v:q
}
