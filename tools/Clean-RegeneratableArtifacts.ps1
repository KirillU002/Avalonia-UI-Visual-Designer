[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$IncludeSmokeTemp
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\')
$tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')

function Get-DirectoryBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return [int64]0
    }

    return [int64]((Get-ChildItem -LiteralPath $Path -Force -File -Recurse -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum)
}

function Assert-ApprovedPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $repositoryPrefix = $repositoryRoot + [IO.Path]::DirectorySeparatorChar
    $smokeTempPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar + "FormDesignerSmoke"
    $isInsideRepository = $fullPath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)
    $isInsideSmokeTemp = $fullPath.StartsWith($smokeTempPrefix, [StringComparison]::OrdinalIgnoreCase)
    if (-not $isInsideRepository -and -not $isInsideSmokeTemp) {
        throw "Refusing to clean a path outside the repository or FormDesigner smoke temp root: $fullPath"
    }

    if ([string]::Equals($fullPath, $repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean the repository root."
    }
}

function Assert-NotTracked([string]$Path) {
    if (-not $Path.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    $relativePath = $Path.Substring($repositoryRoot.Length + 1)
    $tracked = @(git -C $repositoryRoot ls-files -- $relativePath)
    if ($tracked.Count -gt 0) {
        throw "Refusing to clean a tracked path: $Path"
    }
}

function Add-Target([System.Collections.Generic.List[string]]$Targets, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-ApprovedPath $fullPath
    Assert-NotTracked $fullPath
    if ($Targets -notcontains $fullPath) {
        $Targets.Add($fullPath)
    }
}

$targets = [System.Collections.Generic.List[string]]::new()
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
if (Test-Path -LiteralPath $artifactsRoot) {
    Get-ChildItem -LiteralPath $artifactsRoot -Directory -Force |
        Where-Object {
            $_.Name -match "^(host-services-|smoke-|ide-density-build$|isolated-build$)"
        } |
        ForEach-Object { Add-Target $targets $_.FullName }
}

Add-Target $targets (Join-Path $repositoryRoot "smoke-tests\\artifacts")

# Older smoke runs used project-local artifacts folders. They are only cleaned in
# test/plugin projects, and Add-Target refuses a tracked directory.
Get-ChildItem -LiteralPath $repositoryRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -eq "artifacts" -and
        $_.FullName -notmatch "\\.git(\\|$)" -and
        $_.FullName -match "\\(smoke-tests|PluginContracts|Plugins)\\"
    } |
    ForEach-Object { Add-Target $targets $_.FullName }

Get-ChildItem -LiteralPath $repositoryRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @("bin", "obj", "tempobj", "TestResults") -and
        $_.FullName -notmatch "\\artifacts(\\|$)" -and
        $_.FullName -notmatch "\\.git(\\|$)"
    } |
    Sort-Object { $_.FullName.Length } |
    ForEach-Object {
        $candidatePath = $_.FullName
        $hasTargetAncestor = $targets | Where-Object {
            $candidatePath.StartsWith($_.TrimEnd('\') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if (-not $hasTargetAncestor) {
            Add-Target $targets $candidatePath
        }
    }

if ($IncludeSmokeTemp) {
    @(
        "FormDesignerSmokeValidation",
        "FormDesignerSmokeExports",
        "FormDesignerSmokeDlls"
    ) | ForEach-Object { Add-Target $targets (Join-Path $tempRoot $_) }
}

[int64]$deletedBytes = 0
foreach ($target in $targets) {
    [int64]$sizeBeforeDelete = Get-DirectoryBytes $target
    Write-Output "ARTIFACT_CLEANUP_START path=$target; reason=regeneratable-build-or-smoke-artifact; sizeBeforeDelete=$sizeBeforeDelete"
    try {
        Remove-Item -LiteralPath $target -Recurse -Force
        $deletedBytes += $sizeBeforeDelete
        Write-Output "ARTIFACT_CLEANUP_SUCCESS path=$target; reason=regeneratable-build-or-smoke-artifact; sizeBeforeDelete=$sizeBeforeDelete"
    }
    catch {
        Write-Output "ARTIFACT_CLEANUP_FAILED path=$target; exception=$($_.Exception.GetType().Name); message=$($_.Exception.Message)"
        throw
    }
}

Write-Output "CLEANUP_COMPLETE deletedBytes=$deletedBytes; targets=$($targets.Count)"
