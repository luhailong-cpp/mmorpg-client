<#
.SYNOPSIS
  Creates or repairs Unity script GUIDs for generated protobuf sources.
.DESCRIPTION
  Valid metadata is left byte-for-byte unchanged. CheckOnly performs no writes.
  Duplicate GUIDs and malformed metadata headers are rejected rather than guessed.
#>
[CmdletBinding()]
param(
    [string]$GeneratedDirectory = (Join-Path $PSScriptRoot '../Assets/Scripts/Proto/Generated'),
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$GeneratedDirectory = (Resolve-Path -LiteralPath $GeneratedDirectory).Path
$sources = @(Get-ChildItem -LiteralPath $GeneratedDirectory -File -Filter '*.cs' | Sort-Object Name)
$plans = [Collections.Generic.List[object]]::new()
$guids = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$guidPattern = '(?m)^guid:[^\r\n]*'
$headerPattern = '(?m)^fileFormatVersion: 2\r?$'

# 先完整检查再写入，重复身份不能通过自动换号掩盖。
foreach ($source in $sources) {
    $metaPath = $source.FullName + '.meta'
    $exists = Test-Path -LiteralPath $metaPath
    $text = if ($exists) { [IO.File]::ReadAllText($metaPath) } else { '' }
    if ($exists -and $text -notmatch $headerPattern) { throw "Invalid Unity metadata header: $metaPath" }
    $guidMatches = [regex]::Matches($text, $guidPattern)
    if ($guidMatches.Count -gt 1) { throw "Multiple Unity GUID fields: $metaPath" }
    $guid = if ($guidMatches.Count) { $guidMatches[0].Value.Substring(5).Trim() } else { '' }
    $valid = $guid -match '^[0-9a-fA-F]{32}$' -and $guid -ne ('0' * 32)
    if ($valid) {
        if ($guids.ContainsKey($guid)) { throw "Duplicate Unity GUID in $metaPath and $($guids[$guid])" }
        $guids.Add($guid, $metaPath)
        continue
    }
    if ($CheckOnly) { throw "Missing or invalid Unity script GUID (expected 32 hexadecimal characters and a nonzero GUID): $metaPath" }
    $plans.Add([pscustomobject]@{ Path = $metaPath; Exists = $exists; Text = $text; HasGuid = $guidMatches.Count -eq 1 })
}

$created = 0
$repaired = 0
foreach ($plan in $plans) {
    do { $guid = [Guid]::NewGuid().ToString('N') } while ($guids.ContainsKey($guid))
    $guids.Add($guid, $plan.Path)
    if (-not $plan.Exists) {
        $text = "fileFormatVersion: 2`nguid: $guid`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
        ++$created
    }
    elseif ($plan.HasGuid) {
        $text = [regex]::Replace($plan.Text, $guidPattern, "guid: $guid")
        ++$repaired
    }
    else {
        $newline = if ($plan.Text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $text = [regex]::Replace($plan.Text, $headerPattern, "fileFormatVersion: 2$newline" + "guid: $guid")
        ++$repaired
    }

    # Unity 可能已打开工程；使用同目录原子替换，避免导入器读到写了一半的 GUID。
    # 临时名以点开头，Unity 不将其当成项目资产导入。
    $temporary = Join-Path $GeneratedDirectory ('.' + [IO.Path]::GetFileName($plan.Path) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $text, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $plan.Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
[pscustomobject]@{ Checked = $sources.Count; Created = $created; Repaired = $repaired; CheckOnly = [bool]$CheckOnly }
