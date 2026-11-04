#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ApplicationDirectory,
      [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
      [string]$ExpectedTitle='Beta 0.1 RC')
$ErrorActionPreference='Stop'
$app=(Resolve-Path -LiteralPath $ApplicationDirectory).Path
$out=[IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $out -Force|Out-Null
if(Get-Process FormDesigner -ErrorAction SilentlyContinue){throw 'Close other Designer processes before this read-only launch probe'}
# Keep existing user preferences/recovery in memory only. Never put them in evidence/packages.
$userDataRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FormDesigner'
$saved=@{}
foreach($name in @('app-settings.json','Recovery/active-session.recovery.json')){
 $path=Join-Path $userDataRoot $name
 $saved[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
$process=$null
try{
 $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $app 'FormDesigner.exe'))
 $start.WorkingDirectory=$out;$start.UseShellExecute=$false;$start.WindowStyle='Hidden'
 $start.Environment['DOTNET_ROOT']=Join-Path $out 'no-installed-runtime'
 $start.Environment['DOTNET_ROOT_X64']=$start.Environment['DOTNET_ROOT']
 $start.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
 $start.Environment['PATH']=$env:SystemRoot+'\System32;'+$env:SystemRoot
 $process=[Diagnostics.Process]::Start($start)
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 do{Start-Sleep -Milliseconds 100;$process.Refresh()}while(-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
 if($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero){throw 'Published apphost did not create a native window'}
 $modules=@($process.Modules|ForEach-Object FileName)
 $core=@($modules|Where-Object {[IO.Path]::GetFileName($_) -eq 'coreclr.dll'})
 if($core.Count -ne 1 -or -not $core[0].StartsWith($app+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Apphost did not use bundled CoreCLR'}
 $forbidden=@($modules|Where-Object {[IO.Path]::GetFileName($_) -match '^(Eremex|Microsoft.VisualStudio)'})
 if($forbidden.Count){throw 'Standard standalone loaded vendor/VS modules'}
 if($process.MainWindowTitle -notlike ('*'+$ExpectedTitle)){throw ('Unexpected window title: '+$process.MainWindowTitle)}
 $result=@{NativeWindow=$true;Title=$process.MainWindowTitle;BundledCoreClr=$true;VendorOrVisualStudioModules=$forbidden.Count;NoSdkOnPath=$true;FirstCleanProfileTest=$false}
 # No edits are made. Graceful close can persist window state; restore the exact original files below.
 $process.CloseMainWindow()|Out-Null
 if(-not $process.WaitForExit(10000)){$process.Kill();$process.WaitForExit();$result['ForcedCloseForModalDialog']=$true}
 $result|ConvertTo-Json|Set-Content (Join-Path $out 'standalone-apphost.json')
 Write-Output 'PASS published self-contained apphost: native Beta window, bundled CoreCLR, no Eremex/VisualStudio modules.'
}finally{
 if($process -and -not $process.HasExited){$process.Kill();$process.WaitForExit()}
 foreach($path in $saved.Keys){
  if($null -eq $saved[$path]){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
  else{[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null;[IO.File]::WriteAllBytes($path,$saved[$path])}
 }
 if($process){$process.Dispose()}
}
