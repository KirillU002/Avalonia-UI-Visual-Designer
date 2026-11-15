#Requires -Version 7.0
param([switch]$AllowKnownCrash)
$ErrorActionPreference='Stop'
if(-not $AllowKnownCrash){throw 'This rejected experiment can crash its isolated test process. Pass -AllowKnownCrash explicitly; it is not a workaround or a normal regression suite.'}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$parent=Join-Path $repo 'artifacts/universal-phase3b1'
$output=Join-Path $parent ('rejected-repro-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force|Out-Null
$archive=Join-Path $output 'source.zip'
& git -C $repo archive HEAD --format=zip ('--output='+$archive)
if($LASTEXITCODE -ne 0){throw 'Cannot archive baseline'}
$source=Join-Path $output 'source'
Expand-Archive -LiteralPath $archive -DestinationPath $source
# All mutations are confined to this newly created archive, never the checkout.
$resolved=[IO.Path]::GetFullPath($source)
if(-not $resolved.StartsWith(([IO.Path]::GetFullPath($parent)+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid isolated source path'}
Copy-Item -LiteralPath (Join-Path $repo 'smoke-tests/FormDesigner.ExportSmokeTests/Program.UiaStability.cs') -Destination (Join-Path $source 'smoke-tests/FormDesigner.ExportSmokeTests/Program.UiaStability.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Experiments/PreviewAutomationLifetime.children.cs.txt') -Destination (Join-Path $source 'Services/PreviewAutomationLifetime.cs')
$entry=Join-Path $source 'smoke-tests/FormDesigner.ExportSmokeTests/Program.cs'
$text=[IO.File]::ReadAllText($entry)
if(-not $text.Contains('args.Contains("--uia-stability")')){
 $marker='        EnsureAvaloniaRuntimeInitialized();'
 $position=$text.IndexOf($marker)
 if($position -lt 0){throw 'Main entry marker changed'}
 $text=$text.Insert($position+$marker.Length,"`r`n        if (args.Contains(`"--uia-stability`")) return RunUiaStability();")
 [IO.File]::WriteAllText($entry,$text)
}
$project=Join-Path $source 'smoke-tests/FormDesigner.ExportSmokeTests/FormDesigner.ExportSmokeTests.csproj'
& dotnet build $project -c Release -m:1 -v:q
if($LASTEXITCODE -ne 0){throw 'Isolated repro build failed'}
$runner=Join-Path $source 'smoke-tests/FormDesigner.ExportSmokeTests/bin/Release/net8.0/FormDesigner.ExportSmokeTests.dll'
$client=Join-Path $PSScriptRoot 'UiaTreeClient/bin/Release/net8.0-windows/UiaTreeClient.dll'
Remove-Item Env:UIA_STRESS_RELEASE_CLIENT_FIRST -ErrorAction SilentlyContinue
& (Join-Path $PSScriptRoot 'RunStress.ps1') -Runner $runner -Output (Join-Path $output 'results') -Scenarios @('preview-legacy','preview-axaml') -Cycles 3 -Client -ClientDll $client
