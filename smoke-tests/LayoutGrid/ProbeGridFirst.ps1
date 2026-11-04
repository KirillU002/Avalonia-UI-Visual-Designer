param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ProjectPath).Path
$project=@(Get-ChildItem -LiteralPath $root -Filter '*.csproj')
if($project.Count -ne 1){throw 'Expected one generated project'}
$entry=Join-Path $root 'App.axaml.cs'
$probe=Join-Path $root 'GridFirstRuntimeProbe.cs'
if(Test-Path -LiteralPath $probe){throw 'Probe already exists'}
$saved=@{}
$saved[$entry]=[IO.File]::ReadAllBytes($entry)
$env:GRID_FIRST_RESULT=Join-Path (Split-Path -Parent $root) 'generated-runtime.txt'
if(Test-Path -LiteralPath $env:GRID_FIRST_RESULT){Remove-Item -LiteralPath $env:GRID_FIRST_RESULT}
try{
 $source=[Text.Encoding]::UTF8.GetString($saved[$entry]);$marker='desktop.MainWindow = new MainWindow();'
 if(-not $source.Contains($marker)){throw 'Generated startup marker missing'}
 [IO.File]::WriteAllText($entry,$source.Replace($marker,'{ '+$marker+' global::GridFirstRuntimeProbe.Attach(desktop, desktop.MainWindow); }'),[Text.UTF8Encoding]::new($false))
 foreach($file in Get-ChildItem -LiteralPath $root -Filter '*.axaml.cs'){
  $source=[IO.File]::ReadAllText($file.FullName)
  if($source.Contains('InitializeComponent();')){
   if(-not $saved.ContainsKey($file.FullName)){$saved[$file.FullName]=[IO.File]::ReadAllBytes($file.FullName)}
   [IO.File]::WriteAllText($file.FullName,$source.Replace('InitializeComponent();','InitializeComponent(); global::GridFirstRuntimeProbe.Track(this);'),[Text.UTF8Encoding]::new($false))
  }
 }
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GridFirstRuntime.cs.txt') -Destination $probe
 & dotnet restore $project[0].FullName --verbosity quiet
 if($LASTEXITCODE -ne 0){throw 'Generated restore failed'}
 & dotnet build $project[0].FullName -c Release --no-restore -m:1 --verbosity quiet
 if($LASTEXITCODE -ne 0){throw 'Generated build failed'}
 [xml]$xml=Get-Content -LiteralPath $project[0].FullName -Raw
 $dll=Join-Path $root ('bin/Release/'+[string]$xml.Project.PropertyGroup.TargetFramework+'/'+$project[0].BaseName+'.dll')
 $stdout=Join-Path $root 'runtime.stdout.log';$stderr=Join-Path $root 'runtime.stderr.log'
 $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"'+$dll+'"') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
 if(-not $process.WaitForExit(45000)){$process.Kill();throw 'Generated runtime timed out'}
 $process.WaitForExit();Get-Content -LiteralPath $stdout
 if($process.ExitCode -ne 0){Get-Content -LiteralPath $stderr;throw 'Generated runtime failed'}
 if(-not(Test-Path -LiteralPath $env:GRID_FIRST_RESULT)){throw 'Fresh runtime result missing'}
 Get-Content -LiteralPath $env:GRID_FIRST_RESULT
}finally{
 foreach($path in $saved.Keys){[IO.File]::WriteAllBytes($path,$saved[$path])}
 if(Test-Path -LiteralPath $probe){Remove-Item -LiteralPath $probe}
 & dotnet build $project[0].FullName -c Release --no-restore -m:1 --verbosity quiet
}
