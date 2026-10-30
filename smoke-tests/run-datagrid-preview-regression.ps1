param([switch]$LocalDb, [switch]$IncludeExport)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousConnection = $env:FORMDESIGNER_TEST_SQLSERVER
$createdInstance = $null
Push-Location $repo
try {
    if ($LocalDb) {
        $localDbCommand = Get-Command SqlLocalDB -ErrorAction Stop
        $sqlCmdCommand = Get-Command sqlcmd -ErrorAction Stop
        $candidate = 'FormDesignerPreviewP0_' + [guid]::NewGuid().ToString('N')
        & $localDbCommand.Source create $candidate -s
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated LocalDB instance.' }
        $createdInstance = $candidate
        & $sqlCmdCommand.Source -S "(localdb)\$createdInstance" -E -C -b -l 15 -Q 'CREATE DATABASE [FormDesignerPreviewP0]'
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated test database.' }
        & $sqlCmdCommand.Source -S "(localdb)\$createdInstance" -E -C -b -l 15 -d FormDesignerPreviewP0 -i (Join-Path $PSScriptRoot 'SqlServer\DataGridPreviewFixture.sql') -f 65001
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create test fixture.' }
        $env:FORMDESIGNER_TEST_SQLSERVER = "Server=(localdb)\$createdInstance;Database=FormDesignerPreviewP0;Integrated Security=true;Encrypt=false;TrustServerCertificate=true;Connect Timeout=5"
    }
    # Without -LocalDb, live tests require an explicitly configured dedicated test
    # database with the supplied fixture. No external database is provisioned here.
    dotnet build .\smoke-tests\FormDesigner.ExportSmokeTests\FormDesigner.ExportSmokeTests.csproj -m:1 --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Runner build failed.' }
    dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --datagrid-preview-checks
    if ($LASTEXITCODE -ne 0) { throw 'DataGrid Preview checks failed.' }
    if ($LocalDb) {
        dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --datagrid-preview-source-check
        if ($LASTEXITCODE -ne 0) { throw 'SQL Settings/schema/BindingSource workflow failed.' }
    }
    if ($IncludeExport) {
        dotnet .\smoke-tests\FormDesigner.ExportSmokeTests\bin\Debug\net8.0\FormDesigner.ExportSmokeTests.dll --datagrid-preview-export-check
        if ($LASTEXITCODE -ne 0) { throw 'Generated runtime check failed.' }
    }
}
finally {
    $env:FORMDESIGNER_TEST_SQLSERVER = $previousConnection
    if ($createdInstance -and $createdInstance -match '^FormDesignerPreviewP0_[a-f0-9]{32}$') {
        & $localDbCommand.Source stop $createdInstance -i
        & $localDbCommand.Source delete $createdInstance
    }
    Pop-Location
}
