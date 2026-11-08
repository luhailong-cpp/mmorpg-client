param(
    [string]$ProjectPath = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$EvidencePath,
    [string]$OverlayRoot = '',
    [string]$UnityExecutable = 'D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe',
    [switch]$WaitForCompletion
)
$ErrorActionPreference = 'Stop'
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
if (-not $EvidencePath) { throw 'Provide a new evidence directory for this run.' }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $ProjectPath '.codex-artifacts'))
if (-not $EvidencePath.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidencePath must be a new directory beneath the client .codex-artifacts directory.'
}
for ($ancestor = $EvidencePath; $ancestor -and $ancestor.Length -ge $allowedRoot.Length; $ancestor = Split-Path $ancestor -Parent) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Output directory may not traverse a junction or symbolic link: $ancestor"
    }
}
$isolated = Join-Path $EvidencePath 'project'
if (Test-Path -LiteralPath $isolated) { throw "Run directory already exists: $isolated" }
if (-not (Test-Path -LiteralPath $UnityExecutable)) { throw 'Unity executable is missing.' }
New-Item -ItemType Directory -Force -Path $EvidencePath, "$isolated/Assets/Plugins", "$isolated/Assets/Editor", "$isolated/Packages", "$isolated/ProjectSettings" | Out-Null
$compileTemp = Join-Path $EvidencePath 'compile'
New-Item -ItemType Directory -Force -Path $compileTemp | Out-Null
$runtimeInputs = @(Get-ChildItem -LiteralPath "$ProjectPath/Assets/Scripts" -Filter '*.cs' -Recurse | ForEach-Object {
    @{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$runtimeInputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$EvidencePath/runtime-inputs.json" -Encoding utf8
$originalTmp = $env:TMP; $originalTemp = $env:TEMP
try {
    $env:TMP = $compileTemp; $env:TEMP = $compileTemp
    & pwsh -NoProfile -File (Join-Path $ProjectPath 'tools/client_compile_check.ps1') -ProjectPath $ProjectPath -ShowErrors 30 2>&1 |
        Tee-Object -FilePath (Join-Path $EvidencePath 'runtime-compile.log')
    $compileExit = $LASTEXITCODE
} finally { $env:TMP = $originalTmp; $env:TEMP = $originalTemp }
if ($compileExit -ne 0) { throw 'Runtime compilation failed; no Unity process started.' }
foreach ($inputFile in $runtimeInputs) {
    if ((Get-FileHash -LiteralPath $inputFile.path -Algorithm SHA256).Hash -ne $inputFile.sha256) {
        throw "Runtime source changed during compilation: $($inputFile.path)"
    }
}
$compileDirectory = Join-Path $compileTemp 'mmorpg_compile_check'
$assembly = Join-Path $compileDirectory 'check.dll'
$overlayFiles = @()
$overlayInputs = @()
if ($OverlayRoot) {
    $OverlayRoot = [IO.Path]::GetFullPath($OverlayRoot)
    if (-not (Test-Path -LiteralPath "$OverlayRoot/Assets/Scripts")) { throw 'OverlayRoot must contain Assets/Scripts.' }
    $response = Get-Content -LiteralPath "$compileDirectory/poll.rsp"
    $overlayFiles = @(Get-ChildItem -LiteralPath "$OverlayRoot/Assets/Scripts" -Filter '*.cs' -Recurse)
    $overlayInputs = @($overlayFiles | ForEach-Object {
        @{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $overlayInputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$EvidencePath/overlay-inputs.json" -Encoding utf8
    foreach ($file in $overlayFiles) {
        $relative = [IO.Path]::GetRelativePath($OverlayRoot, $file.FullName).Replace('\','/')
        $response = @($response | Where-Object { $_.Replace('\','/') -ne ('"' + $relative + '"') })
        $response += '"' + $file.FullName + '"'
    }
    $response | Set-Content -LiteralPath "$compileDirectory/overlay.rsp" -Encoding utf8
    $compiler = Get-ChildItem -LiteralPath 'C:/Program Files/dotnet/sdk' -Directory |
        Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'Roslyn/bincore/csc.dll' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    Push-Location -LiteralPath $ProjectPath
    try {
        & dotnet exec $compiler "@$compileDirectory/overlay.rsp" 2>&1 | Tee-Object -FilePath "$EvidencePath/overlay-compile.log"
        if ($LASTEXITCODE -ne 0) { throw 'Candidate runtime compilation failed.' }
    } finally { Pop-Location }
}
$currentRuntimePaths = @(Get-ChildItem -LiteralPath "$ProjectPath/Assets/Scripts" -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
if (@(Compare-Object -ReferenceObject @($runtimeInputs.path) -DifferenceObject $currentRuntimePaths).Count -ne 0) {
    throw 'Runtime source file set changed during compilation.'
}
foreach ($inputFile in @($runtimeInputs) + @($overlayInputs)) {
    if ((Get-FileHash -LiteralPath $inputFile.path -Algorithm SHA256).Hash -ne $inputFile.sha256) {
        throw "Source changed during candidate compilation: $($inputFile.path)"
    }
}
if ($OverlayRoot) {
    $currentOverlayPaths = @(Get-ChildItem -LiteralPath "$OverlayRoot/Assets/Scripts" -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
    if (($overlayInputs.Count -ne $currentOverlayPaths.Count) -or
        ($overlayInputs.Count -gt 0 -and @(Compare-Object -ReferenceObject @($overlayInputs.path) -DifferenceObject $currentOverlayPaths).Count -ne 0)) {
        throw 'Candidate source file set changed during compilation.'
    }
}
Copy-Item -LiteralPath $assembly -Destination "$isolated/Assets/Plugins/check.dll"
Copy-Item -LiteralPath "$ProjectPath/Assets/Plugins/Google.Protobuf.dll" -Destination "$isolated/Assets/Plugins/Google.Protobuf.dll"
$pluginMeta = Get-Content -LiteralPath "$ProjectPath/Assets/Plugins/Google.Protobuf.dll.meta" -Raw
$pluginMeta = $pluginMeta -replace 'guid: [a-f0-9]+', ('guid: ' + [guid]::NewGuid().ToString('N'))
$pluginMeta = $pluginMeta -replace 'isExplicitlyReferenced: 0', 'isExplicitlyReferenced: 1'
$pluginMeta | Set-Content -LiteralPath "$isolated/Assets/Plugins/check.dll.meta" -Encoding utf8
foreach ($relative in @('Assets/Resources/Fonts','Assets/TextMesh Pro','Assets/Resources/UI/Ugui/MailV1','Assets/Resources/UI/Ugui/MailV1Fidelity','Assets/Resources/UI/Ugui/Native')) {
    $destination = Join-Path $isolated $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $ProjectPath $relative) -Destination $destination -Recurse
}
if ($OverlayRoot -and (Test-Path -LiteralPath "$OverlayRoot/Assets/Resources")) {
    foreach ($file in Get-ChildItem -LiteralPath "$OverlayRoot/Assets/Resources" -Recurse -File) {
        $destination = Join-Path $isolated ([IO.Path]::GetRelativePath($OverlayRoot,$file.FullName))
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
Copy-Item -LiteralPath "$ProjectPath/Assets/Editor/Mail/MailUiVerification.cs" -Destination "$isolated/Assets/Editor/MailUiVerification.cs"
if ($OverlayRoot) {
    $isolatedCaptureScript = "$isolated/Assets/Editor/MailUiVerification.cs"
    $captureScriptText = Get-Content -LiteralPath $isolatedCaptureScript -Raw
    $geometryNotePattern = '(?m)^([ \t]*)public string geometryNote=.*;\r?$'
    if ([regex]::Matches($captureScriptText,$geometryNotePattern).Count -ne 1) {
        throw 'Expected exactly one geometryNote field in the isolated capture report.'
    }
    $captureScriptText = [regex]::Replace($captureScriptText, $geometryNotePattern,
        '$1public string geometryNote="Isolated overlay candidate: geometry must be inspected independently; no production-equivalence or pixel-parity claim.";')
    $captureScriptText | Set-Content -LiteralPath $isolatedCaptureScript -Encoding utf8
}
@{name='MailIsolatedCapture';references=@('UnityEngine.UI','Unity.TextMeshPro');includePlatforms=@('Editor');overrideReferences=$true;precompiledReferences=@('check.dll','Google.Protobuf.dll')} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$isolated/Assets/Editor/MailIsolatedCapture.asmdef" -Encoding utf8
@'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class MailIsolatedCapture {
    public static void Run() {
        try {
            MailUiVerification.OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            MailUiVerification.CaptureOriginalSize();
            Debug.Log("MAIL_ISOLATED_CAPTURE_FINISHED: capture only, not visual acceptance");
            EditorApplication.Exit(0);
        } catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
'@ | Set-Content -LiteralPath "$isolated/Assets/Editor/MailIsolatedCapture.cs" -Encoding utf8
$cache = Join-Path $ProjectPath 'Library/PackageCache'
$dependencies = [ordered]@{}
foreach ($name in @('com.unity.ugui')) {
    $package = Get-ChildItem -LiteralPath $cache -Directory | Where-Object { $_.Name -eq $name -or $_.Name.StartsWith($name+'@') } | Select-Object -First 1
    if (-not $package) { throw "Missing cached package: $name" }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\','/')
}
foreach ($module in @('animation','audio','imageconversion','imgui','jsonserialize','particlesystem','physics','physics2d','ui','uielements','unitywebrequest','unitywebrequesttexture','unitywebrequestassetbundle','assetbundle','director')) {
    $dependencies['com.unity.modules.'+$module] = '1.0.0'
}
@{dependencies=$dependencies} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$isolated/Packages/manifest.json" -Encoding utf8
foreach ($file in @('ProjectVersion.txt','ProjectSettings.asset')) {
    Copy-Item -LiteralPath "$ProjectPath/ProjectSettings/$file" -Destination "$isolated/ProjectSettings/$file"
}
$captureInputs = @(Get-ChildItem -LiteralPath "$isolated/Assets","$isolated/Packages","$isolated/ProjectSettings" -Recurse -File | ForEach-Object {
    @{path=[IO.Path]::GetRelativePath($isolated,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$captureInputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$EvidencePath/capture-inputs-before-unity.json" -Encoding utf8
$sourceProfile = if ($OverlayRoot) { 'overlay' } else { 'baseline' }
$arguments = @('-batchmode','-projectPath',('"'+$isolated+'"'),'-executeMethod','MailIsolatedCapture.Run','-logFile',('"'+$EvidencePath+'/unity.log"'))
$process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{pid=$process.Id;startedAtUtc=[DateTime]::UtcNow.ToString('O');arguments=$arguments;project=$isolated;productionProjectOpened=$false;networkStarted=$false;captureOnly=$true;visualAcceptance=$false;
  sourceProfile=$sourceProfile;overlayRoot=$OverlayRoot;
  runtimeSHA256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;
  overlayFiles=$overlayInputs} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$EvidencePath/invocation.json" -Encoding utf8
Write-Output "Capture launched: PID=$($process.Id), evidence=$EvidencePath"
if ($WaitForCompletion) {
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Capture timed out; process retained for inspection, PID=$($process.Id)" }
    }
    $reportPath = Join-Path $EvidencePath 'captures/original-size-capture.json'
    $captureReport = if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } else { $null }
    $captureFiles = if ($captureReport) { @($captureReport.files) } else { @() }
    $captureDirectory = [IO.Path]::GetFullPath((Join-Path $EvidencePath 'captures'))
    $filesPresent = $captureFiles.Count -eq 8 -and @($captureFiles | Sort-Object -Unique).Count -eq 8 -and @($captureFiles | Where-Object {
        -not $_ -or -not [IO.Path]::GetFullPath($_).StartsWith($captureDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $_ -PathType Leaf) -or (Get-Item -LiteralPath $_).Length -eq 0
    }).Count -eq 0
    $complete = $process.ExitCode -eq 0 -and $captureReport.status -eq 'captured-unverified' -and
        $captureReport.captureProfile -eq 'original-image-size' -and $captureReport.screenshots -eq 8 -and
        $captureReport.referenceWidth -eq 1931 -and $captureReport.referenceHeight -eq 814 -and
        $captureReport.captureOnly -eq $true -and $captureReport.pixelParityVerified -eq $false -and
        @($captureReport.errors).Count -eq 0 -and $filesPresent
    $changedInputs = @($captureInputs | Where-Object {
        $path = Join-Path $isolated $_.path
        -not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $_.sha256
    } | ForEach-Object { $_.path })
    @{pid=$process.Id;exitCode=$process.ExitCode;completedAtUtc=[DateTime]::UtcNow.ToString('O');captureCompleted=$complete;
      sourceProfile=$sourceProfile;overlayRoot=$OverlayRoot;
      visualAcceptance=$false;report=$reportPath;changedInputsAfterUnity=$changedInputs;
      note='Unity may save dynamic TMP font caches on exit; initial input hashes are recorded separately.'} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$EvidencePath/completion.json" -Encoding utf8
    if (-not $complete) { throw 'Capture did not complete successfully; inspect unity.log and completion.json.' }
    Write-Output 'Eight native offline screenshots captured; visual acceptance remains false.'
}
