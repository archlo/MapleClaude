<#
.SYNOPSIS
    Builds a single self-contained MapleClaude.exe and (optionally) drops it
    into the MapleStory folder defined by $env:MAPLECLAUDE_DEPLOY_DIR.

.DESCRIPTION
    Wraps `dotnet publish` with the project's single-file settings, then
    surfaces the path to the resulting .exe. If MAPLECLAUDE_DEPLOY_DIR is set,
    copies MapleClaude.exe into that directory.

    Never hardcodes a deploy path. The target dir is supplied via env var
    (per machine, never committed).

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Deploy
    Copy the resulting .exe into $env:MAPLECLAUDE_DEPLOY_DIR after publish.
    Defaults to true when the env var is set, false otherwise.

.EXAMPLE
    .\publish.ps1

.EXAMPLE
    .\publish.ps1 -Configuration Debug

.EXAMPLE
    $env:MAPLECLAUDE_DEPLOY_DIR = "X:\path\to\maplestory"
    .\publish.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Nullable[bool]]$Deploy = $null
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$mainProject = Join-Path $repoRoot 'src\MapleClaude\MapleClaude.csproj'
$publishDir  = Join-Path $repoRoot "artifacts\publish\$Configuration\win-x64"

Write-Host "==> Publishing MapleClaude ($Configuration, win-x64, single-file)..." -ForegroundColor Cyan
& dotnet publish $mainProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --output $publishDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exePath = Join-Path $publishDir 'MapleClaude.exe'
if (-not (Test-Path $exePath)) {
    throw "Published exe not found at $exePath"
}

$exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
Write-Host ""
Write-Host "==> Built single-file exe:" -ForegroundColor Green
Write-Host "    $exePath ($exeSize MB)" -ForegroundColor Green

# Decide whether to deploy
if ($null -eq $Deploy) {
    $Deploy = -not [string]::IsNullOrWhiteSpace($env:MAPLECLAUDE_DEPLOY_DIR)
}

if ($Deploy) {
    if ([string]::IsNullOrWhiteSpace($env:MAPLECLAUDE_DEPLOY_DIR)) {
        Write-Warning "MAPLECLAUDE_DEPLOY_DIR is not set; skipping deploy."
        Write-Host "    To enable: `$env:MAPLECLAUDE_DEPLOY_DIR = '<your MapleStory folder>'" -ForegroundColor Yellow
        return
    }

    $deployDir = $env:MAPLECLAUDE_DEPLOY_DIR
    if (-not (Test-Path $deployDir)) {
        throw "Deploy directory does not exist: $deployDir"
    }

    Write-Host ""
    Write-Host "==> Deploying MapleClaude.exe -> $deployDir" -ForegroundColor Cyan
    Copy-Item -Path $exePath -Destination (Join-Path $deployDir 'MapleClaude.exe') -Force
    Write-Host "    Done." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "==> Skipping deploy (use -Deploy or set MAPLECLAUDE_DEPLOY_DIR)." -ForegroundColor DarkGray
}
