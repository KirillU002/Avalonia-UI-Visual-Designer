$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$runnerProject = Join-Path $scriptRoot "FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts\smoke-tests"

Write-Host "Running FormDesigner export smoke tests..."
Write-Host "Artifacts: $artifactsRoot"

dotnet build-server shutdown | Out-Null
dotnet run -c Release --project $runnerProject -- $artifactsRoot
exit $LASTEXITCODE
