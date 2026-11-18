<#
.SYNOPSIS
  Verifies generated Unity script metadata and the offline compiler preflight without launching Unity.
#>
[CmdletBinding()]
param([string]$EvidencePath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$syncScript = Join-Path $projectRoot 'tools/sync_proto_script_meta.ps1'
$compileScript = Join-Path $projectRoot 'tools/client_compile_check.ps1'
if (-not $EvidencePath) {
    $EvidencePath = Join-Path $projectRoot ('.codex-artifacts/proto-script-meta-tests/run-' + [Guid]::NewGuid().ToString('N'))
}
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
if (Test-Path -LiteralPath $EvidencePath) { throw 'EvidencePath must be a new directory.' }
New-Item -ItemType Directory -Path $EvidencePath | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$badGuid = 'c6ff4cd302ed4be69761bdd473758eaeb'
$validGuid = 'c6ff4cd302ed4be69761bdd473758eae'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Fixture([string]$Name, [string]$Metadata = '') {
    $root = Join-Path $EvidencePath ('fixtures/' + $Name + '/Assets/Scripts/Proto/Generated')
    New-Item -ItemType Directory -Path $root | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'EquipmentEffects.cs'), 'public sealed class EquipmentEffects { }')
    if ($Metadata.Length) { [IO.File]::WriteAllText((Join-Path $root 'EquipmentEffects.cs.meta'), $Metadata) }
    return $root
}
function MetaHash([string]$Root) {
    return (Get-FileHash -LiteralPath (Join-Path $Root 'EquipmentEffects.cs.meta') -Algorithm SHA256).Hash
}
function Expect-Rejection([scriptblock]$Action, [string]$Text) {
    $errorText = ''
    try { & $Action | Out-Null } catch { $errorText = $_.Exception.Message }
    Assert-True ($errorText.Contains($Text)) ('Expected rejection containing: ' + $Text + '; actual: ' + $errorText)
}
function Check([string]$Name, [scriptblock]$Action) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.ToString() }
    $checks.Add([ordered]@{ name=$Name; passed=$null -eq $failure; error=$failure })
    Write-Output ("{0}: {1}" -f $(if ($null -eq $failure) { 'PASS' } else { 'FAIL' }), $Name)
}

Check 'reject-33-character-guid-without-changing-source-or-meta' {
    $root = Fixture 'bad guid' "fileFormatVersion: 2`nguid: $badGuid`n"
    $before = MetaHash $root
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $root 'EquipmentEffects.cs')).Hash
    Expect-Rejection { & $syncScript -GeneratedDirectory $root -CheckOnly } 'Missing or invalid Unity script GUID'
    Assert-True ((MetaHash $root) -eq $before) 'CheckOnly modified metadata.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $root 'EquipmentEffects.cs')).Hash -eq $sourceHash) 'CheckOnly modified message code.'
}
Check 'repair-invalid-guid-preserves-importer-and-message-source' {
    $root = Fixture 'repair' "fileFormatVersion: 2`nguid: $badGuid`nMonoImporter:`n  executionOrder: 12`n  userData: preserved`n"
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $root 'EquipmentEffects.cs')).Hash
    $result = & $syncScript -GeneratedDirectory $root
    $text = Get-Content -LiteralPath (Join-Path $root 'EquipmentEffects.cs.meta') -Raw
    Assert-True ($result.Repaired -eq 1 -and $result.Created -eq 0) 'Invalid GUID was not repaired exactly once.'
    Assert-True ($text -match '(?m)^guid: [0-9a-f]{32}\r?$') 'Repaired GUID is not valid.'
    Assert-True ($text.Contains('executionOrder: 12') -and $text.Contains('userData: preserved')) 'Existing importer settings were lost.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $root 'EquipmentEffects.cs')).Hash -eq $sourceHash) 'Repair modified message code.'
    & $syncScript -GeneratedDirectory $root -CheckOnly | Out-Null
}
Check 'valid-guid-is-byte-for-byte-stable' {
    $root = Fixture 'valid' "fileFormatVersion: 2`r`nguid: $validGuid`r`nMonoImporter:`r`n  executionOrder: 0`r`n"
    $before = MetaHash $root
    $result = & $syncScript -GeneratedDirectory $root
    Assert-True ($result.Repaired -eq 0 -and $result.Created -eq 0) 'Valid metadata was replaced.'
    Assert-True ((MetaHash $root) -eq $before) 'Valid metadata changed bytes.'
}
Check 'missing-meta-gets-mono-importer-and-stable-guid' {
    $root = Fixture 'missing'
    Expect-Rejection { & $syncScript -GeneratedDirectory $root -CheckOnly } 'Missing or invalid Unity script GUID'
    $result = & $syncScript -GeneratedDirectory $root
    Assert-True ($result.Created -eq 1) 'Missing metadata was not created.'
    $text = Get-Content -LiteralPath (Join-Path $root 'EquipmentEffects.cs.meta') -Raw
    Assert-True ($text.Contains('MonoImporter:')) 'Generated script did not receive MonoImporter metadata.'
    $before = MetaHash $root
    & $syncScript -GeneratedDirectory $root | Out-Null
    Assert-True ((MetaHash $root) -eq $before) 'Second generation changed the new GUID.'
}
Check 'missing-guid-keeps-existing-importer-settings' {
    $root = Fixture 'missing field' "fileFormatVersion: 2`r`nMonoImporter:`r`n  executionOrder: 7`r`n"
    $result = & $syncScript -GeneratedDirectory $root
    Assert-True ($result.Repaired -eq 1) 'Missing GUID field was not repaired.'
    $text = Get-Content -LiteralPath (Join-Path $root 'EquipmentEffects.cs.meta') -Raw
    Assert-True ($text.Contains('executionOrder: 7')) 'Importer settings were not preserved.'
    & $syncScript -GeneratedDirectory $root -CheckOnly | Out-Null
}
Check 'zero-guid-is-rejected-and-repaired' {
    $root = Fixture 'zero' ("fileFormatVersion: 2`nguid: " + ('0' * 32) + "`n")
    Expect-Rejection { & $syncScript -GeneratedDirectory $root -CheckOnly } 'Missing or invalid Unity script GUID'
    & $syncScript -GeneratedDirectory $root | Out-Null
    & $syncScript -GeneratedDirectory $root -CheckOnly | Out-Null
}
Check 'duplicate-valid-guids-fail-before-repairing-other-files' {
    $root = Fixture 'duplicate' "fileFormatVersion: 2`nguid: $validGuid`n"
    [IO.File]::WriteAllText((Join-Path $root 'Other.cs'), 'public class Other { }')
    [IO.File]::WriteAllText((Join-Path $root 'Other.cs.meta'), "fileFormatVersion: 2`nguid: $validGuid`n")
    [IO.File]::WriteAllText((Join-Path $root 'AMissing.cs'), 'public class AMissing { }')
    $before = MetaHash $root
    Expect-Rejection { & $syncScript -GeneratedDirectory $root } 'Duplicate Unity GUID'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'AMissing.cs.meta'))) 'Validation failure caused partial writes.'
    Assert-True ((MetaHash $root) -eq $before) 'Duplicate identity was silently replaced.'
}
Check 'multiple-guid-fields-and-malformed-headers-fail-closed' {
    $root = Fixture 'multiple fields' "fileFormatVersion: 2`nguid: $badGuid`nguid: $validGuid`n"
    Expect-Rejection { & $syncScript -GeneratedDirectory $root } 'Multiple Unity GUID fields'
    $root = Fixture 'bad header' "fileFormatVersion: broken`nguid: $validGuid`n"
    Expect-Rejection { & $syncScript -GeneratedDirectory $root } 'Invalid Unity metadata header'
}
Check 'offline-compiler-gate-rejects-ignored-unity-script' {
    $root = Fixture 'compiler gate' "fileFormatVersion: 2`nguid: $badGuid`n"
    $fixtureProject = Split-Path (Split-Path (Split-Path (Split-Path $root -Parent) -Parent) -Parent) -Parent
    $log = Join-Path $EvidencePath 'compiler-gate.log'
    & (Get-Process -Id $PID).Path -NoProfile -File $compileScript -ProjectPath $fixtureProject -UnityRoot $fixtureProject 2>&1 |
        Set-Content -LiteralPath $log -Encoding utf8
    Assert-True ($LASTEXITCODE -eq 1) 'Offline compiler gate did not reject invalid Unity metadata.'
    Assert-True ((Get-Content -LiteralPath $log -Raw).Contains('Unity protobuf metadata validation failed')) 'Failure did not come from the Unity metadata preflight.'
}

$passed = @($checks | Where-Object passed).Count
$report = [ordered]@{
    total=$checks.Count; passed=$passed; failed=$checks.Count-$passed
    unityStarted=$false; messageSourcesEdited=$false
    scripts=@($syncScript,$compileScript,$PSCommandPath | ForEach-Object {
        [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
    checks=@($checks.ToArray())
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidencePath 'results.json') -Encoding utf8
Write-Output "Unity protobuf metadata tests: $passed/$($checks.Count). Evidence: $EvidencePath"
if ($passed -ne $checks.Count) { exit 1 }
