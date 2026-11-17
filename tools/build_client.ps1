#requires -Version 7.0
<#
.SYNOPSIS
  使用工程中已启用的场景构建 Windows x64 客户端。
.DESCRIPTION
  先关闭打开此工程的 Unity 编辑器。脚本按 ProjectVersion.txt 查找 Unity Hub
  默认安装目录和工作区的 tools/Unity 目录，也可以用 -UnityExe 指定编辑器。
  默认构建到 Builds/windows-x64-<本次构建编号>/mmorpg.exe，日志在 Logs/ 下。
  -OutDir 可以指定一个尚不存在的输出目录。加 -Zip 会在其旁边生成 ZIP。
  默认使用 BuildOptions.None。只有显式 -Development 才启用 DEVELOPMENT_BUILD，
  结果与实际构建选项（含数值旗标）记录在输出目录的 build-manifest.json。
  构建会话限制为 2 个 Job Worker、1 个导入 Worker，降低并发内存压力。
.EXAMPLE
  pwsh -File tools/build_client.ps1
.EXAMPLE
  pwsh -File tools/build_client.ps1 -OutDir 'Builds/QA Client' -Zip
.EXAMPLE
  pwsh -File tools/build_client.ps1 -Development -Zip
#>
[CmdletBinding()]
param(
    [string]$UnityExe,
    [string]$OutDir,
    [switch]$Development,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$projectVersion = Get-Content -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt') -Raw
if ($projectVersion -notmatch '(?m)^m_EditorVersion:[ \t]*(\S+)') {
    throw '无法从 ProjectSettings/ProjectVersion.txt 读取 Unity 版本。'
}
$unityVersion = $Matches[1]

if (-not $UnityExe) {
    $candidates = @(
        (Join-Path $env:ProgramFiles "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe")
        (Join-Path (Split-Path $projectPath -Parent) "tools/Unity/$unityVersion/Editor/Unity.exe")
    )
    $UnityExe = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $UnityExe -or -not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "找不到 Unity $unityVersion。请安装该版本，或用 -UnityExe 指定 Unity.exe。"
}
$UnityExe = (Resolve-Path -LiteralPath $UnityExe).Path

$buildId = "$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')-$PID"
$buildFlavor = if ($Development) { 'development' } else { 'release' }
if (-not $OutDir) { $OutDir = Join-Path $projectPath "Builds/windows-x64-$buildFlavor-$buildId" }
$OutDir = [IO.Path]::TrimEndingDirectorySeparator(
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir))
if (Test-Path -LiteralPath $OutDir) {
    throw "输出路径已存在，请指定新的 -OutDir。路径 $OutDir"
}
if ($Zip) {
    $tar = Get-Command tar.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $tar) { throw '找不到 tar.exe，无法生成 ZIP。' }
    $zipPath = "$OutDir.zip"
    if (Test-Path -LiteralPath $zipPath) { throw "ZIP 路径已存在。路径 $zipPath" }
}

$null = New-Item -ItemType Directory -Path $OutDir
$logDir = Join-Path $projectPath 'Logs'
$null = New-Item -ItemType Directory -Path $logDir -Force
$logFile = Join-Path $logDir "build-client-$buildId.log"
$playerExe = Join-Path $OutDir 'mmorpg.exe'

$unityArgs = @(
    '-batchmode', '-nographics', '-quit',
    '-job-worker-count', '2',
    '-desiredWorkerCount', '1',
    '-standbyWorkerCount', '1',
    '-idleWorkerShutdownDelay', '1000',
    '-log-memory-performance-stats',
    '-projectPath', $projectPath,
    '-buildTarget', 'win64',
    '-standaloneBuildSubtarget', 'Player',
    '-executeMethod', 'MmorpgClient.Editor.ClientPlayerBuild.Build',
    '-clientOutputDirectory', $OutDir,
    '-logFile', $logFile
)
if ($Development) { $unityArgs += '-clientDevelopment' }

Write-Host "[build] Unity $unityVersion | $UnityExe"
Write-Host "[build] output=$OutDir"
Write-Host "[build] flavor=$buildFlavor"
Write-Host "[build] log=$logFile"
$startInfo = [Diagnostics.ProcessStartInfo]::new($UnityExe)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
foreach ($argument in $unityArgs) { $startInfo.ArgumentList.Add($argument) }

# Wait for the editor itself. Compiler workers can outlive it on a failed build.
$process = [Diagnostics.Process]::Start($startInfo)
try {
    $process.WaitForExit()
    $unityExitCode = $process.ExitCode
} finally {
    $process.Dispose()
}

if ($unityExitCode -ne 0 -or
    -not (Test-Path -LiteralPath $playerExe -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $OutDir 'mmorpg_Data') -PathType Container) -or
    -not (Test-Path -LiteralPath (Join-Path $OutDir 'UnityPlayer.dll') -PathType Leaf)) {
    if (Test-Path -LiteralPath $logFile) {
        Get-Content -LiteralPath $logFile -Tail 40 | Out-Host
    }
    throw "客户端构建失败或产物不完整（Unity 退出码 $unityExitCode）。日志 $logFile"
}

$manifestPath = Join-Path $OutDir 'build-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Build manifest is missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedOptions = if ($Development) { 1 } else { 0 }
# Unity's Mono can include deprecated zero-valued aliases in enum.ToString().
# Numeric flags and the explicit flavor are authoritative; strings are diagnostic.
$validRequestedOptions = $manifest.requestedBuildOptionsValue -is [int] -or $manifest.requestedBuildOptionsValue -is [long]
$validActualOptions = $manifest.actualBuildOptionsValue -is [int] -or $manifest.actualBuildOptionsValue -is [long]
if ($manifest.schemaVersion -ne 1 -or $manifest.result -cne 'Succeeded' -or
    $manifest.target -cne 'StandaloneWindows64' -or $manifest.subtarget -cne 'Player' -or
    $manifest.buildFlavor -cne $buildFlavor -or
    -not $validRequestedOptions -or $manifest.requestedBuildOptionsValue -ne $expectedOptions -or
    -not $validActualOptions -or (($manifest.actualBuildOptionsValue -band 1) -ne $expectedOptions) -or
    $manifest.development -isnot [bool] -or $manifest.development -ne [bool]$Development -or
    [IO.Path]::GetFullPath($manifest.executable) -cne [IO.Path]::GetFullPath($playerExe) -or
    $manifest.executableSha256 -cne (Get-FileHash -LiteralPath $playerExe -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw "Build manifest does not match the requested player or build flavor: $manifestPath"
}

Write-Host "[build] exe=$playerExe"
Write-Host "[build] manifest=$manifestPath"
if ($Zip) {
    # Use tar's ZIP64 support for large Unity resource files and archives.
    $tempZip = "$zipPath.$buildId.tmp"
    $outParent = Split-Path $OutDir -Parent
    $outName = Split-Path $OutDir -Leaf
    Write-Host "[build] creating zip=$zipPath"
    try {
        & $tar.Source --format zip -cf $tempZip -C $outParent `
            --exclude '*/mmorpg_BackUpThisFolder_ButDontShipItWithYourGame' -- "./$outName"
        if ($LASTEXITCODE -ne 0) { throw "ZIP 压缩失败（tar 退出码 $LASTEXITCODE）。" }
        [IO.File]::Move($tempZip, $zipPath)
    } finally {
        if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force }
    }
    Write-Host "[build] zip=$zipPath"
}
