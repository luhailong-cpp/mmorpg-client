<#
.SYNOPSIS
  Run the checked-in CityTileStreaming tests and real 4K importer checks in a fresh isolated Unity project.
.DESCRIPTION
  Never opens or changes the main Unity project. Copies exact loader/importer/test sources and one
  unchanged legacy texture, records SHA-256 for each, and substitutes only the world-rectangle owner.
  The synthetic 4K texture is a test fixture, never published artwork. Requires cached Test Framework
  and NUnit packages in the main project's Library/PackageCache; no package downloads are requested.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$UnityExe = '',
    [string]$EvidencePath = '',
    [int]$TimeoutSeconds = 900
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
Copy-TestSource 'Assets/Scripts/World/Tianyong/CityTileManifest.cs' 'Assets/Runtime/CityTileManifest.cs'
Copy-TestSource 'Assets/Scripts/World/Tianyong/CityTileStreaming.cs' 'Assets/Runtime/CityTileStreaming.cs'
Copy-TestSource 'Assets/Editor/Tianyong/CityTile4KImporter.cs' 'Assets/Editor/CityTile4KImporter.cs'
Copy-TestSource 'Assets/Tests/EditMode/Tianyong/CityTileStreamingTests.cs' 'Assets/Tests/CityTileStreamingTests.cs'
Copy-TestSource 'Assets/Resources/World/Tianyong/SceneTiles6x6/Tiles/tianyong_r01_c01.png' 'Assets/Resources/World/Tianyong/SceneTiles6x6/Tiles/tianyong_r01_c01.png'
$verifier = 'Assets/Editor/Tianyong/CityTileProductionVerification.cs'
Copy-TestSource $verifier 'Assets/Editor/CityTileProductionVerification.cs'
$fixtureSource = Join-Path $ProjectPath 'tools/tests/CityTileImportVerificationTests.cs'
if (-not (Test-Path -LiteralPath $fixtureSource)) { throw "Importer test fixture is missing: $fixtureSource" }
Copy-TestSource 'tools/tests/CityTileImportVerificationTests.cs' 'Assets/Tests/CityTileImportVerificationTests.cs'
Set-Content -LiteralPath "$isolated/Assets/Runtime/WorldRectFixture.cs" -Encoding utf8 -Value @'
using UnityEngine;
namespace MmorpgClient.World.Tianyong {
    // Isolation-only substitute: the real TianyongPaintedCity has scene dependencies.
    public static class TianyongPaintedCity {
        public static readonly Rect PaintingWorldRect = new Rect(50f, 0f, 300f, 300f);
    }
}
'@
Set-Content -LiteralPath "$isolated/Assets/Runtime/CityTileValidation.Runtime.asmdef" -Encoding utf8 -Value '{"name":"CityTileValidation.Runtime"}'
Set-Content -LiteralPath "$isolated/Assets/Editor/CityTileValidation.Editor.asmdef" -Encoding utf8 -Value '{"name":"CityTileValidation.Editor","references":["CityTileValidation.Runtime"],"includePlatforms":["Editor"]}'
Set-Content -LiteralPath "$isolated/Assets/Tests/CityTileValidation.Tests.asmdef" -Encoding utf8 -Value '{"name":"CityTileValidation.Tests","references":["CityTileValidation.Runtime","CityTileValidation.Editor"],"includePlatforms":["Editor"],"optionalUnityReferences":["TestAssemblies"]}'
$dependencies = [ordered]@{}
foreach ($name in @('com.unity.test-framework', 'com.unity.ext.nunit')) {
    $package = Get-ChildItem -LiteralPath (Join-Path $ProjectPath 'Library/PackageCache') -Directory |
        Where-Object { $_.Name.StartsWith($name + '@') } | Sort-Object Name | Select-Object -First 1
    if (-not $package) { throw "Required cached package not found: $name" }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
}
$dependencies['com.unity.modules.imgui'] = '1.0.0'
$dependencies['com.unity.modules.jsonserialize'] = '1.0.0'
$dependencies['com.unity.modules.imageconversion'] = '1.0.0'
$dependencies['com.unity.modules.physics'] = '1.0.0'
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$isolated/Packages/manifest.json" -Encoding utf8
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Destination "$isolated/ProjectSettings/ProjectVersion.txt"
$hashes | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$EvidencePath/source-hashes.json" -Encoding utf8
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $isolated + '"'), '-runTests', '-testPlatform', 'EditMode',
    '-testResults', ('"' + "$EvidencePath/editmode-results.xml" + '"'), '-logFile', ('"' + "$EvidencePath/unity-editmode.log" + '"'),
    '-cityTileValidationEvidence', ('"' + $EvidencePath + '"'))
$invocation = [ordered]@{
    startedAt = (Get-Date).ToString('o'); unityExe = $UnityExe; isolatedProject = $isolated; arguments = $arguments
    mainProjectOpened = $false; completeArtworkValidated = $false
    fixtureDifferences = @('TianyongPaintedCity replaced by its exact fixed (50,0,300,300) world rectangle.',
        'Only Manifest, Streaming, Importer, optional production verifier and tests copied; no game scenes/bootstrap/network are run.',
        'One unchanged legacy PNG exercises Resources lifecycle; one generated 4096 texture exercises importer settings only.',
        'Minimal assemblies and cached test packages replace full project configuration; headless EditMode has no GPU performance evidence.')
}
$invocation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$EvidencePath/invocation.json" -Encoding utf8
Write-Output "Isolated project: $isolated"
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
