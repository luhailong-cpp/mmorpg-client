<#
.SYNOPSIS
  出跨区匹配验证用的 Windows 64 Development 播放器(batchmode 调用 CrossZoneVerifyBuild.Build)。

.DESCRIPTION
  产物:E:/work/tmp/crosszone_player/mmorpg.exe(可用 -OutDir 改)。
  日志:E:/work/tmp/crosszone_build.log;失败时自动 tail 日志并挑出 "error CS" 行。
  前置:Unity 编辑器未打开该工程(Temp/UnityLockfile 存在则 batchmode 会被拒)。

.EXAMPLE
  pwsh -File tools/build_crosszone_player.ps1
  pwsh -File tools/build_crosszone_player.ps1 -CompileCheckOnly   # 只验证脚本编译
#>
[CmdletBinding()]
param(
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe",
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutDir = "E:/work/tmp/crosszone_player",
    [string]$LogFile = "E:/work/tmp/crosszone_build.log",
    [switch]$CompileCheckOnly,
    [switch]$NoGraphics = $true,
    [int]$TailLines = 60
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $UnityExe)) { Write-Error "找不到 Unity.exe: $UnityExe" }
if (-not (Test-Path (Join-Path $ProjectPath "Assets"))) { Write-Error "不是 Unity 工程目录: $ProjectPath" }

$lock = Join-Path $ProjectPath "Temp/UnityLockfile"
if (Test-Path $lock) {
    Write-Warning "检测到 $lock:编辑器可能正开着该工程,batchmode 会被拒绝。先关掉编辑器再跑。"
}

New-Item -ItemType Directory -Force -Path (Split-Path $LogFile -Parent) | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (Test-Path $LogFile) { Remove-Item $LogFile -Force }

$method = if ($CompileCheckOnly) { "MmorpgClient.Editor.CrossZoneVerifyBuild.CompileCheck" } else { "MmorpgClient.Editor.CrossZoneVerifyBuild.Build" }
$unityArgs = @(
    "-batchmode",
    "-quit",
    "-projectPath", $ProjectPath,
    "-executeMethod", $method,
    "-crossZoneOut", $OutDir,
    "-logFile", $LogFile
)
if ($NoGraphics) { $unityArgs = @("-nographics") + $unityArgs }

Write-Host "[build] $UnityExe $($unityArgs -join ' ')"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -Wait -NoNewWindow
$sw.Stop()
$exe = Join-Path $OutDir "mmorpg.exe"

Write-Host ("[build] unity exit={0} elapsed={1:n0}s" -f $proc.ExitCode, $sw.Elapsed.TotalSeconds)

$ok = $proc.ExitCode -eq 0
if (-not $CompileCheckOnly) {
    $ok = $ok -and (Test-Path $exe)
}

if ($ok) {
    if ($CompileCheckOnly) {
        Write-Host "[build] COMPILE OK"
    } else {
        $size = (Get-Item $exe).Length
        Write-Host "[build] PASS exe=$exe size=$size"
        if (Test-Path $LogFile) {
            Select-String -Path $LogFile -Pattern "\[CrossZoneVerifyBuild\]" | ForEach-Object { Write-Host $_.Line }
        }
    }
    exit 0
}

Write-Host "[build] FAIL (exit=$($proc.ExitCode), exe exists=$(Test-Path $exe))"
if (Test-Path $LogFile) {
    $errors = Select-String -Path $LogFile -Pattern "error CS\d+|\[CrossZoneVerifyBuild\]|Build.*[Ff]ailed|Scripts have compiler errors|Aborting batchmode" |
        Select-Object -First 40
    if ($errors) {
        Write-Host "---- 关键错误行 ----"
        $errors | ForEach-Object { Write-Host $_.Line }
    }
    Write-Host "---- 日志末尾 $TailLines 行 ($LogFile) ----"
    Get-Content $LogFile -Tail $TailLines
} else {
    Write-Host "日志文件不存在: $LogFile"
}
exit 1
