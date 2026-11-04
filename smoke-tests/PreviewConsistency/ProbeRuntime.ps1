param([Parameter(Mandatory=$true)][string]$ProjectPath,[Parameter(Mandatory=$true)][string]$Manifest,[Parameter(Mandatory=$true)][string]$Scene,[ValidateSet('Debug','Release')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$folder=(Resolve-Path -LiteralPath $ProjectPath).Path
$manifestPath=(Resolve-Path -LiteralPath $Manifest).Path
$projects=@(Get-ChildItem -LiteralPath $folder -Filter '*.csproj')
if($projects.Count -ne 1){throw 'Expected one generated project'}
$entry=Join-Path $folder 'App.axaml.cs'
$probe=Join-Path $folder 'PreviewConsistencyProbe.cs'
if(Test-Path -LiteralPath $probe){throw 'Probe already exists'}
$original=[IO.File]::ReadAllBytes($entry)
$source=[Text.Encoding]::UTF8.GetString($original)
$marker='desktop.MainWindow = new MainWindow();'
if(-not $source.Contains($marker)){throw 'Generated startup not found'}
$activeClass=(Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json)[0].WindowClass
if($activeClass -notmatch '^[A-Za-z_][A-Za-z0-9_]*$'){throw 'Invalid generated window class'}
$env:PC_MANIFEST=$manifestPath;$env:PC_SCENE=$Scene
$env:PC_RESULT=Join-Path (Split-Path -Parent (Split-Path -Parent $folder)) "$Scene-runtime.json"
if(Test-Path -LiteralPath $env:PC_RESULT){Remove-Item -LiteralPath $env:PC_RESULT}
try{
    $startup='{ desktop.MainWindow = new '+$activeClass+'(); global::PreviewConsistencyProbe.Attach(desktop, desktop.MainWindow); }'
    [IO.File]::WriteAllText($entry,$source.Replace($marker,$startup),[Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RuntimeProbe.cs.txt') -Destination $probe
    & dotnet restore $projects[0].FullName --verbosity quiet
    if($LASTEXITCODE -ne 0){throw 'Generated restore failed'}
    & dotnet build $projects[0].FullName --no-restore -c $Configuration -m:1 --verbosity quiet
    if($LASTEXITCODE -ne 0){throw 'Generated build failed'}
    [xml]$xml=Get-Content -LiteralPath $projects[0].FullName -Raw
    $framework=[string]$xml.Project.PropertyGroup.TargetFramework
    $dll=Join-Path $folder ('bin/'+$Configuration+'/'+$framework+'/'+$projects[0].BaseName+'.dll')
    $stdout=Join-Path $folder 'runtime.stdout.log';$stderr=Join-Path $folder 'runtime.stderr.log'
    $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"'+$dll+'"') -WorkingDirectory $folder -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if(-not $process.WaitForExit(45000)){$process.Kill();throw 'Generated runtime timed out'}
    $process.WaitForExit();Get-Content -LiteralPath $stdout
    if($process.ExitCode -ne 0){Get-Content -LiteralPath $stderr;throw 'Generated runtime failed'}
    if(-not (Test-Path -LiteralPath $env:PC_RESULT)){throw 'Runtime did not produce fresh observations'}
}finally{
    [IO.File]::WriteAllBytes($entry,$original)
    Remove-Item -LiteralPath $probe -ErrorAction SilentlyContinue
    & dotnet build $projects[0].FullName --no-restore -c $Configuration -m:1 --verbosity quiet
}
