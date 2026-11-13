[CmdletBinding()]
param(
    [string]$ProjectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')),
    [string]$UnityPath = '',
    [string]$OutputDirectory = '',
    [int]$TimeoutSeconds = 240,
    [ValidateSet('NavigationAuthoringVerification.Run', 'NavMeshSaveVerification.Run')]
    [string]$VerificationMethod = 'NavigationAuthoringVerification.Run'
)
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $ProjectPath '.codex-artifacts/navmesh-authoring-verification' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$testProject = Join-Path $OutputDirectory 'verify-project'
if (Test-Path -LiteralPath (Join-Path $testProject 'Temp/UnityLockfile')) {
    throw "The isolated verification project is already open: $testProject"
}
if (-not $UnityPath) {
    $versionLine = Get-Content -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
    $version = ($versionLine -replace '^m_EditorVersion: ', '').Trim()
    $candidates = @(
        (Join-Path $ProjectPath "../tools/Unity/$version/Editor/Unity.exe"),
        (Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe")
    )
    $UnityPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $UnityPath -or -not (Test-Path -LiteralPath $UnityPath)) { throw 'Pass -UnityPath with the Unity version pinned by this project.' }
$UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path

foreach ($relative in @('Assets/Runtime', 'Assets/Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $testProject $relative) -Force | Out-Null
}
$sourceFiles = @(
    @{ source = 'Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs'; destination = 'Assets/Runtime/NavMeshAuthoringDraft.cs' },
    @{ source = 'Assets/Scripts/World/NavigationAuthoring/NavigationPolygonGeometry.cs'; destination = 'Assets/Runtime/NavigationPolygonGeometry.cs' },
    @{ source = 'Assets/Scripts/World/NavigationAuthoring/NavMeshDraftPreview.cs'; destination = 'Assets/Runtime/NavMeshDraftPreview.cs' },
    @{ source = 'Assets/Editor/NavigationAuthoring/NavMeshAuthoringAssetIO.cs'; destination = 'Assets/Editor/NavMeshAuthoringAssetIO.cs' },
    @{ source = 'Assets/Editor/NavigationAuthoring/NavMeshAuthoringWindow.cs'; destination = 'Assets/Editor/NavMeshAuthoringWindow.cs' },
    @{ source = 'Assets/Editor/NavigationAuthoring/FestivalNavMeshDraftPresets.cs'; destination = 'Assets/Editor/FestivalNavMeshDraftPresets.cs' }
)
$snapshot = foreach ($entry in $sourceFiles) {
    $source = Join-Path $ProjectPath $entry.source
    $destination = Join-Path $testProject $entry.destination
    Copy-Item -LiteralPath $source -Destination $destination -Force
    [pscustomobject]@{ path = $entry.source; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NavigationAuthoringVerification.cs') -Destination (Join-Path $testProject 'Assets/Editor/NavigationAuthoringVerification.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NavMeshSaveVerification.cs') -Destination (Join-Path $testProject 'Assets/Editor/NavMeshSaveVerification.cs') -Force
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $testProject 'ProjectSettings/ProjectVersion.txt') -Force
'{"name":"MmorpgClient","rootNamespace":"MmorpgClient","autoReferenced":true}' | Set-Content -LiteralPath (Join-Path $testProject 'Assets/Runtime/MmorpgClient.asmdef') -Encoding utf8
$snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'source-snapshot.json') -Encoding utf8

# These are the same official packages used by the client, embedded only in the
# throwaway verification project so registry availability does not affect a run.
$navigationArchive = Join-Path $ProjectPath 'tools/unity-packages/com.unity.ai.navigation-2.0.16.tgz'
if ((Get-FileHash -LiteralPath $navigationArchive -Algorithm SHA1).Hash.ToLowerInvariant() -ne 'd0cf77b009bbdba7a0bf95e0de8b25be0659b9a4') {
    throw 'The pinned official AI Navigation archive has an unexpected SHA-1.'
}
$navigationPackage = Join-Path $testProject 'Packages/com.unity.ai.navigation'
New-Item -ItemType Directory -Path $navigationPackage -Force | Out-Null
& tar -xzf $navigationArchive --strip-components=1 -C $navigationPackage
if ($LASTEXITCODE -ne 0) { throw 'Failed to extract the official AI Navigation package.' }
$newtonsoftSource = Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Library/PackageCache') -Directory |
    Where-Object { $_.Name -like 'com.unity.nuget.newtonsoft-json@*' -and
        (Get-Content -LiteralPath (Join-Path $_.FullName 'package.json') -Raw | ConvertFrom-Json).version -eq '3.2.2' } |
    Select-Object -First 1
if (-not $newtonsoftSource) { throw 'Open the main client once to restore its Newtonsoft 3.2.2 package, then run this check again.' }
$newtonsoftPackage = Join-Path $testProject 'Packages/com.unity.nuget.newtonsoft-json'
New-Item -ItemType Directory -Path $newtonsoftPackage -Force | Out-Null
Get-ChildItem -LiteralPath $newtonsoftSource.FullName -Force | Copy-Item -Destination $newtonsoftPackage -Recurse -Force
@'
{
  "dependencies": {
    "com.unity.ai.navigation": "2.0.16",
    "com.unity.nuget.newtonsoft-json": "3.2.2",
    "com.unity.modules.ai": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0"
  }
}
'@ | Set-Content -LiteralPath (Join-Path $testProject 'Packages/manifest.json') -Encoding utf8

$log = Join-Path $OutputDirectory 'unity-verification.log'
$reportName = if ($VerificationMethod -eq 'NavMeshSaveVerification.Run') { 'save-verification.json' } else { 'verification.json' }
'{"result":"RUNNING"}' | Set-Content -LiteralPath (Join-Path $OutputDirectory $reportName) -Encoding utf8
$arguments = @('-batchmode', '-projectPath', ('"' + $testProject + '"'), '-executeMethod', $VerificationMethod,
    '-navmeshSourceProject', ('"' + $ProjectPath + '"'), '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    throw "The isolated verification exceeded $TimeoutSeconds seconds. See $log"
}
foreach ($entry in $snapshot) {
    $current = (Get-FileHash -LiteralPath (Join-Path $ProjectPath $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($current -ne $entry.sha256) { throw "Source changed during verification: $($entry.path). Run again after editing completes." }
}
if ($process.ExitCode -ne 0) { throw "Unity verification exited $($process.ExitCode). See $log" }
$report = Get-Content -LiteralPath (Join-Path $OutputDirectory $reportName) -Raw | ConvertFrom-Json
if ($report.result -ne 'PASS') { throw "Verification did not pass. See $OutputDirectory/$reportName" }
Write-Output "PASS $($report.checks.Count) checks. Report: $OutputDirectory/$reportName"
