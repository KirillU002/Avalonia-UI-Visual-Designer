#Requires -Version 7.0
param([string]$OutputRoot='artifacts/beta01-rc', [switch]$OfficialRelease)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output=if([IO.Path]::IsPathRooted($OutputRoot)){[IO.Path]::GetFullPath($OutputRoot)}else{[IO.Path]::GetFullPath((Join-Path $repo $OutputRoot))}
$allowed=Join-Path $repo 'artifacts'
if(-not $output.StartsWith($allowed+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a fresh directory inside repository artifacts'}
if(Test-Path -LiteralPath $output){throw 'Output already exists. Select a fresh OutputRoot; this script never deletes an existing RC.'}
New-Item -ItemType Directory -Path (Join-Path $output 'evidence') -Force|Out-Null
$label=if($OfficialRelease){'Beta 0.1'}else{'Beta 0.1 RC'}
$stem=if($OfficialRelease){'Avalonia-UI-Visual-Designer-Beta-0.1'}else{'Avalonia-UI-Visual-Designer-Beta-0.1-RC'}
Push-Location $repo
try{
 function Invoke-RcDotnet([string[]]$Arguments,[string]$Log){& dotnet @Arguments *> $Log;if($LASTEXITCODE -ne 0){throw ('dotnet failed; see '+$Log)}}
 foreach($entry in @(@{project='FormDesigner.csproj';name='Standalone'},@{project='AvaloniaDesigner.VsHost/AvaloniaDesigner.VsHost.csproj';name='VsHost'})){
  $stage=Join-Path $output ('staging/'+$entry.name)
  Invoke-RcDotnet -Arguments @('publish',$entry.project,'-c','Release','-r','win-x64','--self-contained','true','-p:BuildExperimentalEremexPlugin=false','-p:PublishTrimmed=false','-p:DebugType=None','-p:DebugSymbols=false','-o',$stage,'-m:1','-v:q') -Log (Join-Path $output ('evidence/publish-'+$entry.name+'.log'))
  $plugins=Join-Path $stage 'Plugins'
  if(Test-Path -LiteralPath $plugins){$resolved=(Resolve-Path -LiteralPath $plugins).Path;if(-not $resolved.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected plugin stage'};Remove-Item -LiteralPath $resolved -Recurse -Force}
 }
 $assets=Get-Content obj/project.assets.json -Raw|ConvertFrom-Json
 $cache=@($assets.packageFolders.PSObject.Properties.Name)[0]
 & (Join-Path $PSScriptRoot 'Collect-ReleaseNotices.ps1') -PublishDirectory (Join-Path $output 'staging/Standalone') -PackageCache $cache -OutputDirectory (Join-Path $output 'staging/Standalone/ThirdPartyNotices')
 Copy-Item -LiteralPath (Join-Path $output 'staging/Standalone/ThirdPartyNotices') -Destination (Join-Path $output 'staging/VsHost/ThirdPartyNotices') -Recurse
 $readme=@'
Avalonia UI Visual Designer — Beta 0.1 RC
Product 0.1.0-beta.1 | Windows x64 self-contained | Unsigned preview build

Unpack the complete archive and launch FormDesigner.exe. Visual Studio, SDK and
Eremex are not required to launch. SDK is required for export restore/build;
default generated net6.0 applications require the corresponding runtime.

Grid is the default for new forms; Canvas and Legacy Preview remain available.
Old Canvas JSON is not migrated. Imported AXAML remains a separate workflow.
PostgreSQL is experimental (live acceptance pending). Eremex is optional and is
not included. Ordinary Window/UIA retention and large-form latency remain known
issues. Resolve deferred recovery before autosave of a new document resumes.

This is an unpublished release candidate, not an implied GitHub release/tag.
Verify SHA256SUMS.txt. ThirdPartyNotices contains package metadata/vendor notices.
Source, installation details and limitations:
https://github.com/KirillU002/Avalonia-UI-Visual-Designer
'@
 if($OfficialRelease){
  $readme=$readme.Replace('Beta 0.1 RC','Beta 0.1').Replace('This is an unpublished release candidate, not an implied GitHub release/tag.', 'Beta 0.1 pre-release; known limitations apply. Release notes and downloads:')
  $readme=$readme.Replace('https://github.com/KirillU002/Avalonia-UI-Visual-Designer', 'https://github.com/KirillU002/Avalonia-UI-Visual-Designer/releases/tag/v0.1.0-beta.1')
 }
 $readme|Set-Content -LiteralPath (Join-Path $output ('staging/Standalone/'+$(if($OfficialRelease){'README.txt'}else{'README-RC.txt'})))
 $hostDirectory=Join-Path $output 'staging/VsHost'
 Invoke-RcDotnet -Arguments @('build','AvaloniaDesigner.VSIX/AvaloniaDesigner.VSIX.csproj','-c','Release','-p:BuildExperimentalEremexPlugin=false',('-p:VsHostPackageDirectory='+$hostDirectory),'-m:1','-v:q') -Log (Join-Path $output 'evidence/vsix-build.log')
 New-Item -ItemType Directory -Path (Join-Path $output 'dist') -Force|Out-Null
 Copy-Item -LiteralPath AvaloniaDesigner.VSIX/bin/Release/net472/AvaloniaDesigner.VSIX.vsix -Destination (Join-Path $output ('dist/'+$stem+'.vsix'))
 [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $output 'staging/Standalone'),(Join-Path $output ('dist/'+$stem+'-win-x64.zip')))
 Get-ChildItem -LiteralPath (Join-Path $output 'dist') -File|ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name)}|Set-Content (Join-Path $output 'dist/SHA256SUMS.txt')
 Write-Output ($label+' artifacts prepared at '+$output+'. This packaging script does not create tags or publish releases.')
}finally{Pop-Location}
