param([Parameter(Mandatory=$true)][string]$PublishDirectory,
      [Parameter(Mandatory=$true)][string]$PackageCache,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$publish=(Resolve-Path -LiteralPath $PublishDirectory).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$deps=Get-Content -LiteralPath (Join-Path $publish 'FormDesigner.deps.json') -Raw|ConvertFrom-Json
$packages=@($deps.libraries.PSObject.Properties|Where-Object {$_.Value.type -eq 'package'}|ForEach-Object Name)
$config=Get-Content -LiteralPath (Join-Path $publish 'FormDesigner.runtimeconfig.json') -Raw|ConvertFrom-Json
$runtime=$config.runtimeOptions.includedFrameworks|Where-Object name -eq 'Microsoft.NETCore.App'
$packages+=('Microsoft.NETCore.App.Runtime.win-x64/'+$runtime.version)
$index=@()
foreach($entry in ($packages|Sort-Object -Unique)){
 $parts=$entry.Split('/');$id=$parts[0];$version=$parts[1]
 $folder=Join-Path (Join-Path $PackageCache $id.ToLowerInvariant()) $version.ToLowerInvariant()
 if(-not (Test-Path -LiteralPath $folder)){throw ('Package not in cache: '+$entry)}
 $nuspec=@(Get-ChildItem -LiteralPath $folder -File -Filter '*.nuspec')[0]
 [xml]$xml=Get-Content -LiteralPath $nuspec.FullName -Raw
 $license=$xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
 $copyright=$xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='copyright']")
 $dest=Join-Path $OutputDirectory ($id+'.'+$version)
 $files=@(Get-ChildItem -LiteralPath $folder -File|Where-Object Name -match '^(LICENSE|COPYING|NOTICE|THIRD.PARTY.NOTICES)')
 if($license -and $license.type -eq 'file'){$declared=Join-Path $folder $license.InnerText;if(Test-Path -LiteralPath $declared){$files+=Get-Item -LiteralPath $declared}}
 $copied=@()
 foreach($file in ($files|Sort-Object FullName -Unique)){New-Item -ItemType Directory -Path $dest -Force|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dest $file.Name);$copied+=$file.Name}
 $index+=@{package=$id;version=$version;license=if($license){$license.InnerText}else{''};licenseType=if($license){$license.type}else{''};copyright=if($copyright){$copyright.InnerText}else{''};providedNotices=$copied;packageUrl='https://www.nuget.org/packages/'+$id+'/'+$version}
}
$index|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDirectory 'packages.json')
Write-Output ('Collected package metadata/notices: '+$index.Count)
