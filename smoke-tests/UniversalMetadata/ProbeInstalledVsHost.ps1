param(
    [Parameter(Mandatory=$true)][string]$HostExecutable,
    [Parameter(Mandatory=$true)][string]$Output
)
# Real installed payload, production protocol codec and launch-only full capture.
# This is a host/packaging check, not interactive VSIX/DTE acceptance.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$hostPath=(Resolve-Path -LiteralPath $HostExecutable).Path
$folder=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $folder){throw 'Fresh probe output directory required'}
New-Item -ItemType Directory -Path $folder|Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $repo 'AvaloniaDesigner.Host.Protocol/bin/Release/net8.0/AvaloniaDesigner.Host.Protocol.dll'))|Out-Null
$capture=Join-Path $repo 'smoke-tests/UiaStability/NativeCapture/bin/Release/net8.0/NativeCapture.dll'
$captureFolder=Join-Path $folder 'capture'
$pipe='AvaloniaDesigner.VsHost.installed.'+[guid]::NewGuid().ToString('N')
$env:UIA_EVIDENCE_ROOT=Join-Path $repo 'artifacts/uia-evidence-vault'
$env:UIA_CAPTURE_TIMEOUT_SECONDS='120';$env:UIA_CAPTURE_MANAGED_FATAL='1'
$arguments=@($capture,$captureFolder,$hostPath,'--','--pipe',$pipe)|ForEach-Object {'"'+$_+'"'}
$process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $arguments -WorkingDirectory (Split-Path $hostPath) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $folder 'stdout.log') -RedirectStandardError (Join-Path $folder 'stderr.log')
$cancel=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(90))
$connection=$null
$records=@()
try {
    $stream=[AvaloniaDesigner.Host.Protocol.NamedPipeProtocolConnection]::CreateClient($pipe)
    $stream.ConnectAsync($cancel.Token).GetAwaiter().GetResult()
    $connection=[AvaloniaDesigner.Host.Protocol.NamedPipeProtocolConnection]::new($stream)
    $hello=[AvaloniaDesigner.Host.Protocol.HelloPayload]::new()
    $connection.SendAsync[AvaloniaDesigner.Host.Protocol.HelloPayload]('Hello','hello','',$hello,$cancel.Token).GetAwaiter().GetResult()
    $response=$connection.ReceiveAsync($cancel.Token).GetAwaiter().GetResult()
    if($response.MessageType -ne 'HelloAck' -or $response.RequestId -ne 'hello'){throw 'Installed host handshake failed'}
    $version=0
    $files=@('Samples/VisualStudioPoC/SimpleAvaloniaApp/MainWindow.axaml','Samples/VisualStudioPoC/ComplexAxamlVisualStudioRoundTrip/MainWindow.axaml','Views/MainWindow.axaml')
    foreach($relative in $files){
        $path=Join-Path $repo $relative
        $payload=[AvaloniaDesigner.Host.Protocol.OpenDocumentPayload]::new()
        $payload.FilePath=$path;$payload.Text=[IO.File]::ReadAllText($path);$payload.Version=++$version
        $payload.Checksum=[AvaloniaDesigner.Host.Protocol.DesignerHostProtocol]::ComputeChecksum($payload.Text)
        $request='open'+$version
        $connection.SendAsync[AvaloniaDesigner.Host.Protocol.OpenDocumentPayload]('OpenDocument',$request,$path,$payload,$cancel.Token).GetAwaiter().GetResult()
        $response=$connection.ReceiveAsync($cancel.Token).GetAwaiter().GetResult()
        if($response.MessageType -ne 'DocumentOpened' -or $response.RequestId -ne $request -or $response.DocumentId -ne $path){throw ('Installed host open failed: '+$response.Payload)}
        $capability=$connection.GetPayload[AvaloniaDesigner.Host.Protocol.DocumentOpenedPayload]($response)
        if(-not $capability.CanEdit){throw ('Expected safe partial editing: '+$path)}
        $records+=[pscustomobject]@{File=$relative;Version=$version;Checksum=$payload.Checksum;Capability=$capability.CapabilityLevel}
    }
} finally {
    if($connection){
        try{$connection.SendAsync[AvaloniaDesigner.Host.Protocol.EmptyPayload]('HostShutdown','shutdown','',[AvaloniaDesigner.Host.Protocol.EmptyPayload]::new(),$cancel.Token).GetAwaiter().GetResult()}
        finally{$connection.Dispose()}
    }
    $cancel.Dispose()
    if(-not $process.WaitForExit(15000)){throw 'Installed host did not exit; preserve capture and diagnose'}
}
$result=Get-Content (Join-Path $captureFolder 'result.json') -Raw|ConvertFrom-Json
if($process.ExitCode -ne 0 -or $result.ExitCode -ne 0 -or $result.Captured -or $result.CaptureFailed -or $result.TimedOut){throw 'Installed host crashed or failed capture; inspect forensic output'}
[pscustomobject]@{Executable=$hostPath;HostPid=$result.Pid;ExitCode=$result.ExitCode;Documents=$records;InteractiveVsix='NOT VERIFIED'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $folder 'result.json') -Encoding utf8
Write-Output 'INSTALLED_VSHOST_PROBE PASS actual installed path; three real sources; normal exit; native capture clean'
