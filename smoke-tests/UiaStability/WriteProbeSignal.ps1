#Requires -Version 7.0
# Forensic file protocol only. Publish a complete request; never overwrite an
# unconsumed request or expose a reader to a partially written/locked file.
param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][ValidateSet('sample.request','close.request','release.txt')][string]$Name,
    [string]$Label='checkpoint'
)
$ErrorActionPreference='Stop'
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts'))+[IO.Path]::DirectorySeparatorChar
$folder=(Resolve-Path -LiteralPath $Output).ProviderPath
if(-not $folder.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Only owned repository artifact directories are supported'}
$target=Join-Path $folder $Name
$temporary=Join-Path $folder ('.signal-'+[guid]::NewGuid().ToString('N')+'.tmp')
$bytes=[Text.Encoding]::UTF8.GetBytes($Label)
$stream=[IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
[IO.File]::Move($temporary,$target,$false)
