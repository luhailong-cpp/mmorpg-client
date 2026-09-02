# Sync the qdao headband-boy 8-direction run strips from the imagegen output
# into Unity Resources, where QdaoBoySpriteAnimator loads them via
# `Resources.Load<Texture2D>("World/Characters/QdaoHeadbandBoy/walk_<DIR>")`.
#
# Source strips are the HD masters, 4096x512 (8 frames of 512x512, left to
# right). The animator slices them at runtime and plays them at 16 fps using
# 64 ppu and a normalized feet pivot of 0.08, so no editor sprite setup is
# needed. Destination names stay `walk_<DIR>.png` for Resources compatibility.
#
# Run:
#     pwsh -File mmorpg-client/sync_qdao_walk_to_resources.ps1
#
# Idempotent: overwrites destination PNGs in place.

[CmdletBinding()]
param(
    [string]$Source = 'E:\work\output\imagegen\qdao_headband_boy_run_v1',
    [string]$ProjectRoot = $PSScriptRoot
)

$dst = Join-Path $ProjectRoot 'Assets\Resources\World\Characters\QdaoHeadbandBoy'

if (-not (Test-Path $Source)) {
    Write-Error "imagegen source not found: $Source"
    exit 1
}

New-Item -ItemType Directory -Force $dst | Out-Null
$copied = 0
foreach ($dir in @('N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW')) {
    $strip = Join-Path $Source "qdao_headband_boy_run_${dir}_strip_4096x512_v1.png"
    if (-not (Test-Path $strip)) {
        Write-Error "missing strip: $strip"
        exit 1
    }
    Copy-Item $strip -Destination (Join-Path $dst "walk_${dir}.png") -Force
    $copied++
}

$totalMB = '{0:N1}' -f ((Get-ChildItem $dst -File | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host "[done] $copied strips -> $dst ($totalMB MB)"
