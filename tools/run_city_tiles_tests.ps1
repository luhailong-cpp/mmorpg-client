<#
.SYNOPSIS
  Run the checked-in CityTileStreaming tests and real 4K importer checks in a fresh isolated Unity project.
.DESCRIPTION
  Never opens or changes the main Unity project. Copies the real runtime source assembly and plugin,
  exact importer/test sources, and the main layout/source/walkmask required by CurrentMapBuilder.
  Records SHA-256 for every copy; no lifecycle or world-rectangle implementation is substituted.
  The synthetic 4K texture is a test fixture, never published artwork. Uses cached packages from the
  project or installed editor. -PrepareOnly writes copies/evidence without starting Unity or tests.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$UnityExe = '',
    [string]$EvidencePath = '',
    [int]$TimeoutSeconds = 900,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not $EvidencePath) {
    $EvidencePath = Join-Path $ProjectPath ('Docs/VerificationEvidence/city-tiles4k-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Force -Path $EvidencePath | Out-Null
if (-not $UnityExe) {
    $projectFile = Join-Path $ProjectPath 'MmorpgClient.csproj'
    if (Test-Path -LiteralPath $projectFile) {
        [xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
        foreach ($reference in $projectXml.Project.ItemGroup.Reference) {
            if ($reference.HintPath -match '^(.*[\\/]Editor)[\\/]Data[\\/]Managed[\\/]UnityEngine[\\/]UnityEngine.dll$') {
                $candidate = Join-Path $Matches[1] 'Unity.exe'
                if (Test-Path -LiteralPath $candidate) { $UnityExe = $candidate; break }
            }
        }
    }
    if (-not $UnityExe) {
        $version = ((Get-Content -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') |
            Select-String '^m_EditorVersion: (.+)$').Matches.Groups[1].Value)
        $candidate = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe"
        if (Test-Path -LiteralPath $candidate) { $UnityExe = $candidate }
    }
}
if (-not $UnityExe -or -not (Test-Path -LiteralPath $UnityExe)) { throw 'Unity.exe was not found. Pass -UnityExe for this machine.' }
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$priorArtifacts = @('source-hashes.json', 'invocation.json', 'summary.json', 'editmode-results.xml', 'unity-editmode.log', 'actual-import-settings.json') |
    ForEach-Object { Join-Path $EvidencePath $_ } | Where-Object { Test-Path -LiteralPath $_ }
if ($priorArtifacts) {
    $archive = Join-Path $EvidencePath "previous-runs/$runId"
    New-Item -ItemType Directory -Force -Path $archive | Out-Null
    foreach ($artifact in $priorArtifacts) { Move-Item -LiteralPath $artifact -Destination $archive }
}
$isolated = Join-Path $ProjectPath ".codex-artifacts/city-tiles4k-validation/$runId"
New-Item -ItemType Directory -Force -Path "$isolated/Assets/Runtime", "$isolated/Assets/Editor", "$isolated/Assets/Tests", "$isolated/Packages", "$isolated/ProjectSettings" | Out-Null
$hashes = [Collections.Generic.List[object]]::new()
function Copy-TestSource([string]$relative, [string]$target) {
    $source = Join-Path $ProjectPath $relative
    $destination = Join-Path $isolated $target
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $copyHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($hash -ne $copyHash) { throw "Copy hash mismatch: $relative" }
    $hashes.Add([ordered]@{ source = $relative; sha256 = $hash; testedCopy = $target; testedCopySha256 = $copyHash; matches = $true })
}
$runtimeRoot = Join-Path $ProjectPath 'Assets/Scripts'
$runtimeSources = @(Get-ChildItem -LiteralPath $runtimeRoot -Filter '*.cs' -Recurse -File | Sort-Object FullName)
if ($runtimeSources.Count -eq 0) { throw 'No runtime source files found in Assets/Scripts.' }
foreach ($runtimeSource in $runtimeSources) {
    $relative = $runtimeSource.FullName.Substring($runtimeRoot.Length).TrimStart('\', '/').Replace('\', '/')
    Copy-TestSource ("Assets/Scripts/$relative") ("Assets/Runtime/$relative")
}
Copy-TestSource 'Assets/Scripts/MmorpgClient.asmdef' 'Assets/Runtime/MmorpgClient.asmdef'
Copy-TestSource 'Assets/Plugins/Google.Protobuf.dll' 'Assets/Plugins/Google.Protobuf.dll'
if (Test-Path -LiteralPath (Join-Path $ProjectPath 'Assets/Plugins/Google.Protobuf.dll.meta')) {
    Copy-TestSource 'Assets/Plugins/Google.Protobuf.dll.meta' 'Assets/Plugins/Google.Protobuf.dll.meta'
}
Copy-TestSource 'Assets/Editor/Tianyong/CityTile4KImporter.cs' 'Assets/Editor/CityTile4KImporter.cs'
Copy-TestSource 'Assets/Editor/CurrentMapTileImporter.cs' 'Assets/Editor/CurrentMapTileImporter.cs'
Copy-TestSource 'Assets/Tests/EditMode/Tianyong/CityTileStreamingTests.cs' 'Assets/Tests/CityTileStreamingTests.cs'
Copy-TestSource 'Assets/Tests/EditMode/Tianyong/CurrentMapTileStreamingTests.cs' 'Assets/Tests/CurrentMapTileStreamingTests.cs'
Copy-TestSource 'Assets/Tests/EditMode/Tianyong/CurrentMapHexGridTests.cs' 'Assets/Tests/CurrentMapHexGridTests.cs'
# 真实 CurrentMapBuilder 读取 layout、source 和 walkmask；不复制 tiles.json 或海量 4K 分片。
foreach ($resourceName in @('layout.png', 'source.json', 'walkmask.txt')) {
    $relative = "Assets/Resources/World/CurrentMaps20261009/main/$resourceName"
    Copy-TestSource $relative $relative
    if (Test-Path -LiteralPath (Join-Path $ProjectPath ($relative + '.meta'))) {
        Copy-TestSource ($relative + '.meta') ($relative + '.meta')
    }
}
$verifier = 'Assets/Editor/Tianyong/CityTileProductionVerification.cs'
Copy-TestSource $verifier 'Assets/Editor/CityTileProductionVerification.cs'
$fixtureSource = Join-Path $ProjectPath 'tools/tests/CityTileImportVerificationTests.cs'
if (-not (Test-Path -LiteralPath $fixtureSource)) { throw "Importer test fixture is missing: $fixtureSource" }
Copy-TestSource 'tools/tests/CityTileImportVerificationTests.cs' 'Assets/Tests/CityTileImportVerificationTests.cs'
Copy-TestSource 'tools/tests/CurrentMapTileImportVerificationTests.cs' 'Assets/Tests/CurrentMapTileImportVerificationTests.cs'
Set-Content -LiteralPath "$isolated/Assets/Editor/CityTileValidation.Editor.asmdef" -Encoding utf8 -Value '{"name":"CityTileValidation.Editor","references":["MmorpgClient"],"includePlatforms":["Editor"]}'
Set-Content -LiteralPath "$isolated/Assets/Tests/CityTileValidation.Tests.asmdef" -Encoding utf8 -Value '{"name":"CityTileValidation.Tests","references":["MmorpgClient","CityTileValidation.Editor"],"includePlatforms":["Editor"],"optionalUnityReferences":["TestAssemblies"]}'
$dependencies = [ordered]@{}
$builtinPackages = Join-Path (Split-Path -Parent $UnityExe) 'Data/Resources/PackageManager/BuiltInPackages'
$packageRoots = @((Join-Path $ProjectPath 'Library/PackageCache'), $builtinPackages)
function Add-CachedPackage([string]$name) {
    if ($dependencies.Contains($name)) { return }
    $package = $packageRoots | Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Directory } |
        Where-Object { $_.Name -eq $name -or $_.Name.StartsWith($name + '@') } | Sort-Object Name | Select-Object -First 1
    if (-not $package) { throw "Required cached package not found: $name" }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
    $packageManifest = Get-Content -LiteralPath (Join-Path $package.FullName 'package.json') -Raw | ConvertFrom-Json
    foreach ($dependency in $packageManifest.dependencies.PSObject.Properties) {
        Add-CachedPackage $dependency.Name
    }
}
foreach ($name in @('com.unity.test-framework', 'com.unity.ext.nunit', 'com.unity.ugui')) {
    Add-CachedPackage $name
}
# 全量真实运行时使用本机编辑器自带模块；全部从本机解析，避免缺模块或触发联网补包。
foreach ($module in Get-ChildItem -LiteralPath $builtinPackages -Directory -Filter 'com.unity.modules.*') {
    Add-CachedPackage $module.Name
}
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$isolated/Packages/manifest.json" -Encoding utf8
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Destination "$isolated/ProjectSettings/ProjectVersion.txt"
$hashes | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$EvidencePath/source-hashes.json" -Encoding utf8
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $isolated + '"'), '-runTests', '-testPlatform', 'EditMode',
    '-testResults', ('"' + "$EvidencePath/editmode-results.xml" + '"'), '-logFile', ('"' + "$EvidencePath/unity-editmode.log" + '"'),
    '-cityTileValidationEvidence', ('"' + $EvidencePath + '"'))
$invocation = [ordered]@{
    startedAt = (Get-Date).ToString('o'); unityExe = $UnityExe; isolatedProject = $isolated; arguments = $arguments
    mainProjectOpened = $false; completeArtworkValidated = $false; prepareOnly = [bool]$PrepareOnly
    runtimeSourceCount = $runtimeSources.Count
    fixtureDifferences = @('The complete production runtime source assembly and protobuf plugin are copied without implementation substitutes.',
        'Only selected tile tests and editor importers are run; no game scenes or PlayMode bootstrap/network are started.',
        'One unchanged current layout with source/walkmask and original metadata exercises the real CurrentMapBuilder and map Dispose.',
        'No published tile manifest or native artwork tiles are copied; synthetic 4096 textures exercise importer settings only.',
        'Minimal assemblies and cached test packages replace full project configuration; headless EditMode has no GPU performance evidence.')
}
$invocation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$EvidencePath/invocation.json" -Encoding utf8
Write-Output "Isolated project: $isolated"
if ($PrepareOnly) {
    $preparation = [ordered]@{ status = 'prepared_only'; engineStarted = $false; testsExecuted = $false;
        isolatedProject = $isolated; runtimeSourceCount = $runtimeSources.Count; sourceCopiesMatch = $true }
    $preparation | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$EvidencePath/summary.json" -Encoding utf8
    $preparation | ConvertTo-Json -Depth 4 | Write-Output
    exit 0
}
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity PID: $($process.Id)"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $process.WaitForExit(1000)) {
    if ((Get-Date) -gt $deadline) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "Isolated Unity test run timed out after $TimeoutSeconds seconds; see $EvidencePath/unity-editmode.log"
    }
}
$process.Refresh()
$summary = [ordered]@{ completedAt = (Get-Date).ToString('o'); exitCode = $process.ExitCode; sourceCopiesMatch = $true; sourceFilesUnchangedDuringRun = $true; tests = $null }
foreach ($entry in $hashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $ProjectPath $entry.source) -Algorithm SHA256).Hash -ne $entry.sha256) {
        $summary.sourceFilesUnchangedDuringRun = $false
    }
}
$resultsPath = Join-Path $EvidencePath 'editmode-results.xml'
if (Test-Path -LiteralPath $resultsPath) {
    [xml]$results = Get-Content -LiteralPath $resultsPath -Raw
    $run = $results.'test-run'
    $summary.tests = [ordered]@{ result = $run.result; total = [int]$run.total; passed = [int]$run.passed; failed = [int]$run.failed; skipped = [int]$run.skipped }
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$EvidencePath/summary.json" -Encoding utf8
$summary | ConvertTo-Json -Depth 6 | Write-Output
if ($process.ExitCode -ne 0 -or $null -eq $summary.tests -or $summary.tests.result -ne 'Passed' -or -not $summary.sourceFilesUnchangedDuringRun) { exit 1 }
exit 0
