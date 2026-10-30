#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$ProbeOnly,
    [switch]$UseDocker,
    [switch]$UseExistingTestDatabase,
    [string]$Image = 'postgres:16-bookworm'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
$psqlCommand = Get-Command psql -ErrorAction SilentlyContinue
$localPortOpen = $false
$probeClient = [System.Net.Sockets.TcpClient]::new()
try {
    $probeTask = $probeClient.ConnectAsync('127.0.0.1', 5432)
    $localPortOpen = $probeTask.Wait(1500) -and $probeClient.Connected
} catch { $localPortOpen = $false }
finally { $probeClient.Dispose() }

if ($ProbeOnly -or (!$UseDocker -and !$UseExistingTestDatabase)) {
    [pscustomobject]@{
        DockerCli = if ($dockerCommand) { $dockerCommand.Source } else { $null }
        PostgreSqlCli = if ($psqlCommand) { $psqlCommand.Source } else { $null }
        Local5432Open = $localPortOpen
        TestConnectionConfigured = ![string]::IsNullOrWhiteSpace($env:FORMDESIGNER_TEST_POSTGRES)
    } | ConvertTo-Json
    return
}
if ($UseDocker -and $UseExistingTestDatabase) { throw 'Choose one test database mode.' }
if ($UseDocker -and !$dockerCommand) { throw 'Docker is unavailable. No software was installed. See Docs/PostgreSqlIntegrationPhase2.md.' }
if ($UseExistingTestDatabase -and [string]::IsNullOrWhiteSpace($env:FORMDESIGNER_TEST_POSTGRES)) {
    throw 'Set FORMDESIGNER_TEST_POSTGRES to an explicitly authorized, disposable test database.'
}

$containerName = 'formdesigner-pg-test-' + [guid]::NewGuid().ToString('N')
$containerCreated = $false
$previousConnection = $env:FORMDESIGNER_TEST_POSTGRES
$previousPassword = $env:POSTGRES_PASSWORD
Push-Location $repoRoot
try {
    if ($UseDocker) {
        & $dockerCommand.Source info --format '{{.ServerVersion}}'
        if ($LASTEXITCODE -ne 0) { throw 'Docker daemon is unavailable.' }
        $env:POSTGRES_PASSWORD = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $dockerArgs = @('run', '--detach', '--rm', '--pull=missing', '--name', $containerName,
            '--label', 'formdesigner.integration-test=true', '--env', 'POSTGRES_PASSWORD',
            '--env', 'POSTGRES_DB=designer_integration', '--env', 'POSTGRES_USER=designer_test',
            '--publish', '127.0.0.1::5432', '--tmpfs', '/var/lib/postgresql/data:rw,nosuid', $Image)
        & $dockerCommand.Source @dockerArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated PostgreSQL test container.' }
        $containerCreated = $true
        $ready = $false
        for ($attempt = 0; $attempt -lt 160; $attempt++) {
            & $dockerCommand.Source exec $containerName pg_isready -U designer_test -d designer_integration *> $null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            Start-Sleep -Milliseconds 250
        }
        if (!$ready) { throw 'Test PostgreSQL did not become ready within 40 seconds.' }
        $binding = (& $dockerCommand.Source port $containerName 5432/tcp | Select-Object -First 1)
        if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Cannot resolve loopback test port.' }
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder['Host'] = '127.0.0.1'; $builder['Port'] = [int]$Matches[1]
        $builder['Database'] = 'designer_integration'; $builder['Username'] = 'designer_test'
        $builder['Password'] = $env:POSTGRES_PASSWORD; $builder['Ssl Mode'] = 'Disable'
        $env:FORMDESIGNER_TEST_POSTGRES = $builder.ConnectionString
        # Only the image ID, never docker inspect Config.Env (contains the password).
        & $dockerCommand.Source inspect --format '{{.Image}}' $containerName
    }
    & dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj -m:1 --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Integration runner build failed.' }
    & dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --postgres-checks
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL checks failed; inspect the redacted PASS/FAIL output.' }
    # A fresh process matches normal plugin startup and avoids the known late-load lifecycle defect.
    & dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --postgres-eremex-checks
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL Eremex checks failed.' }
}
finally {
    $env:FORMDESIGNER_TEST_POSTGRES = $previousConnection
    $env:POSTGRES_PASSWORD = $previousPassword
    if ($containerCreated) {
        & $dockerCommand.Source rm --force $containerName | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Remove the test container manually: docker rm --force $containerName" }
    }
    Pop-Location
}
