[CmdletBinding()]
param(
    [string]$ProjectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')),
    [string]$UnityPath = 'D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe',
    [string]$OutputDirectory = '',
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $ProjectPath '.codex-artifacts/authored-festival-export-20261112' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$isolated = Join-Path $OutputDirectory 'export-project'
if (Test-Path -LiteralPath (Join-Path $isolated 'Temp/UnityLockfile')) { throw 'The isolated export project is already open.' }
$snapshot = [Collections.Generic.List[object]]::new()
function Copy-Input([string]$Relative, [string]$Destination = '') {
    if (-not $Destination) { $Destination = $Relative }
    $source = Join-Path $ProjectPath $Relative
    $target = Join-Path $isolated $Destination
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    $snapshot.Add([pscustomobject]@{path=$Relative;sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()})
}
Copy-Input 'Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs' 'Assets/Runtime/NavMeshAuthoringDraft.cs'
Copy-Input 'Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs.meta' 'Assets/Runtime/NavMeshAuthoringDraft.cs.meta'
Copy-Input 'Assets/Scripts/World/NavigationAuthoring/NavigationPolygonGeometry.cs' 'Assets/Runtime/NavigationPolygonGeometry.cs'
Copy-Input 'Assets/Editor/NavigationAuthoring/NavMeshAuthoringAssetIO.cs' 'Assets/Editor/NavMeshAuthoringAssetIO.cs'
Copy-Input 'tools/tests/navmesh_authoring/ExportAuthoredFestivalMaps.cs' 'Assets/Editor/ExportAuthoredFestivalMaps.cs'
Copy-Input 'ProjectSettings/ProjectVersion.txt'
'{"name":"MmorpgClient","rootNamespace":"MmorpgClient","autoReferenced":true}' | Set-Content -LiteralPath (Join-Path $isolated 'Assets/Runtime/MmorpgClient.asmdef') -Encoding utf8
$draftFolder = 'Assets/MapNavigationDrafts/Festival20261111'
foreach ($key in @('26000-mid-autumn', '26008-spring')) {
    $relative = "$draftFolder/$key.asset"
    $text = Get-Content -Raw -LiteralPath (Join-Path $ProjectPath $relative)
    Copy-Input $relative
    Copy-Input ($relative + '.meta')
    $navGuid = [regex]::Match($text, 'bakedData: \{[^}]*guid: ([0-9a-f]{32})').Groups[1].Value
    if (-not $navGuid) { throw "The saved draft has no baked navigation: $key" }
    $navMeta = Get-ChildItem -LiteralPath (Join-Path $ProjectPath $draftFolder) -File -Filter "$key.navmesh-*.asset.meta" |
        Where-Object { (Get-Content -Raw -LiteralPath $_.FullName) -match "(?m)^guid: $navGuid\s*$" }
    if (@($navMeta).Count -ne 1) { throw "Cannot resolve the saved native NavMesh GUID: $key" }
    $navRelative = $draftFolder + '/' + $navMeta.Name
    Copy-Input $navRelative
    Copy-Input ($navRelative.Substring(0, $navRelative.Length - 5))
    foreach ($match in [regex]::Matches($text, '(?m)^\s+assetPath: (Assets/[^\r\n]+\.png)\s*$')) {
        Copy-Input $match.Groups[1].Value
        Copy-Input ($match.Groups[1].Value + '.meta')
    }
}
New-Item -ItemType Directory -Path (Join-Path $isolated 'Packages/com.unity.ai.navigation'), (Join-Path $isolated 'Packages/com.unity.nuget.newtonsoft-json') -Force | Out-Null
$archive = Join-Path $ProjectPath 'tools/unity-packages/com.unity.ai.navigation-2.0.16.tgz'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA1).Hash.ToLowerInvariant() -ne 'd0cf77b009bbdba7a0bf95e0de8b25be0659b9a4') { throw 'The pinned AI Navigation archive SHA differs.' }
& tar -xzf $archive --strip-components=1 -C (Join-Path $isolated 'Packages/com.unity.ai.navigation')
if ($LASTEXITCODE -ne 0) { throw 'Cannot extract the official navigation package.' }
$jsonPackage = Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Library/PackageCache') -Directory |
    Where-Object { $_.Name -like 'com.unity.nuget.newtonsoft-json@*' -and (Get-Content -Raw -LiteralPath (Join-Path $_.FullName 'package.json') | ConvertFrom-Json).version -eq '3.2.2' } | Select-Object -First 1
if (-not $jsonPackage) { throw 'The restored Newtonsoft 3.2.2 package is required.' }
Get-ChildItem -LiteralPath $jsonPackage.FullName -Force | Copy-Item -Destination (Join-Path $isolated 'Packages/com.unity.nuget.newtonsoft-json') -Recurse -Force
'{"dependencies":{"com.unity.ai.navigation":"2.0.16","com.unity.nuget.newtonsoft-json":"3.2.2","com.unity.modules.ai":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}' | Set-Content -LiteralPath (Join-Path $isolated 'Packages/manifest.json') -Encoding utf8
$snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'input-snapshot.json') -Encoding utf8
$exportRoot = Join-Path $OutputDirectory 'delivery'
$arguments = @('-batchmode','-nographics','-projectPath',('"'+$isolated+'"'),'-executeMethod','ExportAuthoredFestivalMaps.Run',
    '-navmeshSourceProject',('"'+$ProjectPath+'"'),'-authoredExportOut',('"'+$exportRoot+'"'),'-logFile',('"'+(Join-Path $OutputDirectory 'unity-export.log')+'"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit($TimeoutSeconds*1000)) { $process.Kill(); throw 'The isolated authored-map export timed out.' }
foreach ($entry in $snapshot) {
    if ((Get-FileHash -LiteralPath (Join-Path $ProjectPath $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Source changed during export; no user input was overwritten: $($entry.path)" }
}
if ($process.ExitCode -ne 0) { throw "Authored-map export failed. See $OutputDirectory/unity-export.log" }
$receipt = Get-Content -Raw -LiteralPath (Join-Path $exportRoot 'delivery.json') | ConvertFrom-Json
if ($receipt.result -ne 'PASS') { throw 'The export receipt did not pass.' }
Write-Output "PASS authored-map export. Delivery: $exportRoot/delivery.json"
