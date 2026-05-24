# watch.ps1 — hot-reload dev loop.
#
# Runs the client via `dotnet watch run` so editing C# applies live to the running game
# (method-body edits via .NET Hot Reload) or auto-rebuilds + relaunches on structural edits.
# No more close -> build -> deploy -> reopen.
#
#   .\watch.ps1
#
# The game runs from bin/ (not the deployed single-file exe), so it needs the WZ directory.
# That's taken from $env:MAPLECLAUDE_WZ_DIR, or — since the deploy folder IS the MapleStory/WZ
# folder — from the same .deploy.local the build's auto-deploy uses. No machine paths live here.
#
# MAPLECLAUDE_DEBUG=1 also opens the live debug overlay: tick "drag" and drag a position knob
# (e.g. the CharCreate panels/fields) to tune layout with zero rebuild, then bake the value.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$wz = $env:MAPLECLAUDE_WZ_DIR
if ([string]::IsNullOrWhiteSpace($wz)) { $wz = $env:MAPLECLAUDE_DEPLOY_DIR }   # deploy folder = WZ folder
if ([string]::IsNullOrWhiteSpace($wz) -and (Test-Path '.deploy.local')) {
    $wz = (Get-Content '.deploy.local' -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($wz) -or -not (Test-Path $wz)) {
    Write-Error "No WZ directory. Set MAPLECLAUDE_WZ_DIR / MAPLECLAUDE_DEPLOY_DIR, or put your MapleStory/WZ folder path in .deploy.local."
    exit 1
}

$env:MAPLECLAUDE_WZ_DIR = $wz
$env:MAPLECLAUDE_DEBUG = '1'   # live position-tuning overlay (DragMode)

# Skip the single-file publish + deploy on each rebuild. These go through as MSBuild env-var
# properties, NOT `-p:` args: `dotnet watch` parses `-p` as an alias for `--project`, so
# `--project ... -p:Foo=Bar` dies with "Cannot specify both '--project' and '-p' options."
# (MSBuild reads environment variables as properties, so $(NoAutoPublish) picks these up.)
$env:NoAutoPublish = 'true'
$env:NoAutoDeploy = 'true'
# Auto-restart on edits Hot Reload can't apply (new fields/signatures) instead of prompting.
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = 'true'

Write-Host "Hot-reload: watching src/MapleClaude  (WZ: $wz)" -ForegroundColor Cyan
Write-Host "Edit + save C# -> applied live, or auto-relaunched on structural edits. Ctrl+C to stop." -ForegroundColor DarkGray

dotnet watch --project src/MapleClaude run
