# Sync the qdao headband-boy 8-direction walk strips from the imagegen output
# into Unity Resources, where QdaoBoySpriteAnimator loads them via
# `Resources.Load<Texture2D>("World/Characters/QdaoHeadbandBoy/walk_<DIR>")`.
#
# Source strips are the HD masters, 4096x512 (8 frames of 512x512, left to right); the animator
# slices them at runtime, so no editor sprite setup is needed.
#
# Run:
#     pwsh -File mmorpg-client/sync_qdao_walk_to_resources.ps1
#
# Idempotent: overwrites destination PNGs in place.

[CmdletBinding()]
param(
    # v3_fixed = v1 strips with the S/N gait repaired (mirrored lower-body
    # frames giving a real alternating step; see fix_walk_gait.ps1 in there).
    [string]$Source = 'E:\work\output\imagegen\qdao_headband_boy_walk_v3_fixed',
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
    $strip = Join-Path $Source "qdao_headband_boy_walk_${dir}_strip_4096x512_v1.png"
    if (-not (Test-Path $strip)) {
        Write-Error "missing strip: $strip"
        exit 1
    }
    Copy-Item $strip -Destination (Join-Path $dst "walk_${dir}.png") -Force
    $copied++
}

$totalMB = '{0:N1}' -f ((Get-ChildItem $dst -File | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host "[done] $copied strips -> $dst ($totalMB MB)"
