#requires -Version 7.0
<#
.SYNOPSIS
  使用工程中已启用的场景构建 Windows x64 客户端。
.DESCRIPTION
  先关闭打开此工程的 Unity 编辑器。脚本按 ProjectVersion.txt 查找 Unity Hub
  默认安装目录和工作区的 tools/Unity 目录，也可以用 -UnityExe 指定编辑器。
  默认构建到 Builds/windows-x64-<本次构建编号>/mmorpg.exe，日志在 Logs/ 下。
  -OutDir 可以指定一个尚不存在的输出目录。加 -Zip 会在其旁边生成 ZIP。
.EXAMPLE
  pwsh -File tools/build_client.ps1
.EXAMPLE
  pwsh -File tools/build_client.ps1 -OutDir 'Builds/QA Client' -Zip
#>
[CmdletBinding()]
param(
    [string]$UnityExe,
    [string]$OutDir,
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
if (-not $OutDir) { $OutDir = Join-Path $projectPath "Builds/windows-x64-$buildId" }
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
    '-projectPath', $projectPath,
    '-buildTarget', 'win64',
    '-standaloneBuildSubtarget', 'Player',
    '-buildWindows64Player', $playerExe,
    '-logFile', $logFile
)

Write-Host "[build] Unity $unityVersion | $UnityExe"
Write-Host "[build] output=$OutDir"
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

Write-Host "[build] exe=$playerExe"
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
