<#
.SYNOPSIS
    Sync binary config tables (*.pb) from the authoritative server repo into
    Assets/StreamingAssets/Tables/ and validate Unity .meta files on generated
    Table/RPC C# trees.

.DESCRIPTION
    Reads the authoritative binary table file list from
    Assets/Scripts/Table/Generated/*TableManager.cs (`BinaryFileName`) so the
    bundled data matches TableLoader.TableFileNames(useBinary: true) 1-to-1.
    Unchanged .pb files are skipped by SHA-256 to avoid noisy diffs/reimports;
    orphaned .pb files no longer referenced by TableLoader are removed.

.PARAMETER ServerRoot
    Path to the authoritative C++/Go server repo root. When omitted, auto-probes
    sibling directories (xuanming-server-mmo-cpp, xuanming-server-mmo, mmorpg)
    for proto/message_id.txt + generated/client_tables.
#>
param(
    [string]$ServerRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = (Resolve-Path (Join-Path $scriptDir "..")).Path
$tableCodeDir = Join-Path $repoRoot "Assets/Scripts/Table/Generated"
$rpcCodeDir   = Join-Path $repoRoot "Assets/Scripts/Net/Generated/Rpc"
$dstDir       = Join-Path $repoRoot "Assets/StreamingAssets/Tables"

if ([string]::IsNullOrWhiteSpace($ServerRoot)) {
    foreach ($candidate in @('xuanming-server-mmo-cpp', 'xuanming-server-mmo', 'mmorpg')) {
        $probe = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "../$candidate"))
        if ((Test-Path -LiteralPath (Join-Path $probe 'proto/message_id.txt')) -and
            (Test-Path -LiteralPath (Join-Path $probe 'generated/client_tables'))) {
            $ServerRoot = $probe
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($ServerRoot)) {
        throw "Authoritative server repo with generated/client_tables not found (probed ../xuanming-server-mmo-cpp, ../xuanming-server-mmo, ../mmorpg); run server export or pass -ServerRoot explicitly."
    }
}
$ServerRoot = (Resolve-Path -LiteralPath $ServerRoot).Path
# Fail-closed: only consume server-filtered generated/client_tables (never fall back to unfiltered server generated/tables).
$srcDir = Join-Path $ServerRoot "generated/client_tables"
if (-not (Test-Path -LiteralPath $srcDir)) {
    throw "Server generated/client_tables directory not found: $srcDir (run server export first; refusing to read unfiltered generated/tables)."
}
if (-not (Test-Path -LiteralPath $tableCodeDir)) {
    throw "Client Table/Generated directory not found: $tableCodeDir (run server export first)."
}

# Collect exact BinaryFileName list from generated *TableManager.cs (fail-closed on mismatch).
$managers = @(Get-ChildItem -LiteralPath $tableCodeDir -File -Filter '*TableManager.cs' | Sort-Object Name)
if ($managers.Count -eq 0) {
    throw "No *TableManager.cs found under $tableCodeDir."
}

$expected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$orderedNames = New-Object 'System.Collections.Generic.List[string]'
foreach ($mgr in $managers) {
    $text = Get-Content -LiteralPath $mgr.FullName -Raw
    if ($text -notmatch 'public const string BinaryFileName = "([^"]+)";') {
        throw "Missing BinaryFileName constant in $($mgr.FullName)"
    }
    $binName = $Matches[1]
    if ($expected.Add($binName)) { $orderedNames.Add($binName) }
}

$missing = @($orderedNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $srcDir $_)) })
if ($missing.Count -gt 0) {
    throw ("Missing binary table(s) in $srcDir : " + ($missing -join ", "))
}

if (-not (Test-Path -LiteralPath $dstDir)) {
    New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
}

$copied = 0
$unchanged = 0
$totalBytes = 0L
foreach ($name in $orderedNames) {
    $srcFile = Join-Path $srcDir $name
    $dstFile = Join-Path $dstDir $name
    $srcHash = (Get-FileHash -LiteralPath $srcFile -Algorithm SHA256).Hash
    $needsCopy = $true
    if (Test-Path -LiteralPath $dstFile) {
        $dstHash = (Get-FileHash -LiteralPath $dstFile -Algorithm SHA256).Hash
        if ($srcHash -eq $dstHash) { $needsCopy = $false }
    }
    if ($needsCopy) {
        Copy-Item -LiteralPath $srcFile -Destination $dstFile -Force
        $copied++
    } else {
        $unchanged++
    }
    $totalBytes += (Get-Item -LiteralPath $dstFile).Length
}

# Prune stale .pb files (and their Unity .meta) removed from TableLoader.
$removed = 0
foreach ($existing in @(Get-ChildItem -LiteralPath $dstDir -File -Filter '*.pb')) {
    if (-not $expected.Contains($existing.Name)) {
        Remove-Item -LiteralPath $existing.FullName -Force
        $metaPath = $existing.FullName + '.meta'
        if (Test-Path -LiteralPath $metaPath) { Remove-Item -LiteralPath $metaPath -Force }
        $removed++
    }
}

# Ensure newly exported Table/Generated and Rpc C# files have valid Unity .meta GUIDs.
$metaScript = Join-Path $scriptDir 'sync_proto_script_meta.ps1'
$tableMeta = & $metaScript -GeneratedDirectory $tableCodeDir
$rpcMeta = if (Test-Path -LiteralPath $rpcCodeDir) { & $metaScript -GeneratedDirectory $rpcCodeDir } else { $null }

Write-Host "[sync_tables] server: $ServerRoot"
Write-Host "[sync_tables] output: $dstDir ($($orderedNames.Count) tables, $([Math]::Round($totalBytes / 1KB, 1)) KB; copied=$copied, unchanged=$unchanged, removed=$removed)"
Write-Host "[sync_tables] table script meta: checked=$($tableMeta.Checked), created=$($tableMeta.Created), repaired=$($tableMeta.Repaired)"
if ($null -ne $rpcMeta) {
    Write-Host "[sync_tables] rpc script meta:   checked=$($rpcMeta.Checked), created=$($rpcMeta.Created), repaired=$($rpcMeta.Repaired)"
}
