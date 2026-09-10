<#
.SYNOPSIS
  主城移动观感离线验收:同步真工程到副本工程 → 离线编译体检 → batchmode 出沙盒播放器 →
  播放器自动走位截图(TianyongSandboxAutoDrive)→ 汇总帧数与日志。

.DESCRIPTION
  用户的编辑器常开在真工程上(Temp/UnityLockfile),batchmode 只能打副本工程。副本工程保留自己的
  Library(首次从已解析依赖的工程 robocopy 一份,避免 batchmode 卡在 UPM 下载)。
  每个 -Tag 的播放器与截图各自落在独立目录,修改前(before)与修改后(after)可以逐张对比:
    E:\work\tmp\citymove_player\<Tag>\mmorpg_sandbox.exe
    E:\work\tmp\citymove_shots\<Tag>\NN_<观察点>.png
  播放器日志 E:\work\tmp\citymove_player_<Tag>.log 里每张截图一行:
    [SandboxDrive] shot=<名字> file=… feet=(x,y,z) moving=… sprite=<当前帧> markers=<点击圈数> camera=…
  退出码:0 = 出包 + 走位 + 截图全部完成;其余见各阶段输出。

.EXAMPLE
  pwsh -File tools/run_city_move_verify.ps1 -Tag before
  pwsh -File tools/run_city_move_verify.ps1 -Tag after
  pwsh -File tools/run_city_move_verify.ps1 -Tag after -SkipBuild     # 只重跑播放器
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$Tag = (Get-Date -Format 'yyyyMMdd_HHmmss'),
    [string]$Src = "E:\work\mmorpg-client",
    [string]$Dst = "E:\work\tmp\citymove_verify_project",
    [string]$LibrarySeed = "E:\work\tmp\shotverify_project\Library",
    [string]$UnityExe = "",
    [int]$Width = 1920,
    [int]$Height = 1080,
    [int]$BuildTimeout = 1800,
    [int]$RunTimeout = 240,
    [switch]$SkipSync,
    [switch]$SkipBuild,
    [switch]$SkipCompileCheck
)
$ErrorActionPreference = 'Continue'

# Sync uses /PURGE: never let a typo target the user's project or another workspace.
$Src = [IO.Path]::GetFullPath($Src).TrimEnd('\', '/')
$Dst = [IO.Path]::GetFullPath($Dst).TrimEnd('\', '/')
$verifyRoot = [IO.Path]::GetFullPath('E:\work\tmp').TrimEnd('\', '/') + '\'
if (-not $Dst.StartsWith($verifyRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $Src.Equals($Dst, [StringComparison]::OrdinalIgnoreCase) -or
    $Src.StartsWith($Dst + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $Dst.StartsWith($Src + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Verification copy must be a separate child of $verifyRoot : $Dst"
}
$inspectPath = $Dst
while ($inspectPath) {
    if (Test-Path -LiteralPath $inspectPath) {
        if ((Get-Item -LiteralPath $inspectPath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Verification copy must not traverse a reparse point: $inspectPath"
        }
    }
    $inspectPath = [IO.Path]::GetDirectoryName($inspectPath)
}
if (-not (Test-Path -LiteralPath (Join-Path $Src 'Assets')) -or
    -not (Test-Path -LiteralPath (Join-Path $Src 'ProjectSettings'))) {
    throw "Source is not a Unity project: $Src"
}


$playerDir = "E:\work\tmp\citymove_player\$Tag"
$shotDir   = "E:\work\tmp\citymove_shots\$Tag"
$buildLog  = "E:\work\tmp\citymove_build_$Tag.log"
$playerLog = "E:\work\tmp\citymove_player_$Tag.log"
$exe       = Join-Path $playerDir "mmorpg_sandbox.exe"

# Output cleanup and recursive sync must not follow links into another location.
foreach ($guardedPath in @($playerDir, $shotDir, $buildLog, $playerLog)) {
    $ancestor = [IO.Path]::GetFullPath($guardedPath)
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Verification output must not traverse a reparse point: $ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}
foreach ($guardedTree in @($Dst, $playerDir, $shotDir)) {
    if (Test-Path -LiteralPath $guardedTree) {
        $linkedItem = Get-ChildItem -LiteralPath $guardedTree -Force -Recurse -Attributes ReparsePoint -ErrorAction Stop |
            Select-Object -First 1
        if ($linkedItem) { throw "Verification target contains a reparse point: $($linkedItem.FullName)" }
    }
}


function L($m) { "$(Get-Date -Format T) $m" }

if (-not $UnityExe) {
    $hub = Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "Editor\Unity.exe") } |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $hub) { L "Unity.exe not found under C:\Program Files\Unity\Hub\Editor"; exit 2 }
    $UnityExe = Join-Path $hub.FullName "Editor\Unity.exe"
}

if (-not $SkipBuild) {
    if (-not $SkipSync) {
        L "robocopy $Src -> $Dst (keep copy Library)"
        robocopy $Src $Dst /E /PURGE /XD Library Temp Logs obj .git UserSettings .vs .idea .codex .codex-artifacts /XF *.csproj *.sln *.slnx /NFL /NDL /NJH /NJS /NP /MT:8 | Out-Null
        $rc = $LASTEXITCODE
        L "robocopy exit=$rc (<8 = ok)"
        if ($rc -ge 8) { L "ROBOCOPY FAILED"; exit 1 }
        if (-not (Test-Path "$Dst\Library\PackageCache") -and (Test-Path $LibrarySeed)) {
            L "first run: seeding Library from $LibrarySeed"
            robocopy $LibrarySeed "$Dst\Library" /E /NFL /NDL /NJH /NJS /NP /MT:8 /XD Bee BuildPlayerData BurstCache Artifacts ShaderCache StateCache | Out-Null
            L "seed robocopy exit=$LASTEXITCODE (<8 = ok)"
        }
    }

    if (-not $SkipCompileCheck) {
        L "offline compile check"
        & pwsh -NoProfile -File (Join-Path $Src "tools\client_compile_check.ps1") -ProjectPath $Src -ShowErrors 12 2>&1 | ForEach-Object { L ("  " + $_) }
        if ($LASTEXITCODE -ne 0) { L "COMPILE RED - stop before building"; exit 3 }
    }

    New-Item -ItemType Directory -Force -Path $playerDir | Out-Null
    if (Test-Path $buildLog) { Remove-Item $buildLog -Force -ErrorAction SilentlyContinue }
    L "unity batchmode build ($UnityExe) -> $playerDir"
    # Unity.exe 是启动器,直接调用会立即返回;必须 Start-Process -Wait
    $ub = Start-Process -FilePath $UnityExe -WindowStyle Hidden -PassThru -ArgumentList @(
        "-batchmode", "-nographics", "-quit", "-accept-apiupdate",
        "-projectPath", $Dst,
        "-executeMethod", "MmorpgClient.Editor.Tianyong.TianyongSandboxVerifyBuild.Build",
        "-sandboxOut", ($playerDir -replace '\\', '/'),
        "-logFile", $buildLog)
    if (-not $ub.WaitForExit($BuildTimeout * 1000)) {
        L "BUILD TIMEOUT ${BuildTimeout}s - killing Unity"
        try { $ub.Kill() } catch {}
        exit 4
    }
    L "unity exit=$($ub.ExitCode)"
    Select-String -Path $buildLog -Pattern "error CS|SandboxVerifyBuild|Build completed|Exiting batchmode|Aborting batchmode" -ErrorAction SilentlyContinue |
        Select-Object -Last 14 | ForEach-Object { L ("  " + $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length))) }
    if ($ub.ExitCode -ne 0 -or -not (Select-String -LiteralPath $buildLog -Pattern '\[SandboxVerifyBuild\] result=Succeeded' -Quiet)) {
        L 'BUILD FAILED - refusing to run a stale player'
        exit 4
    }
}

if (-not (Test-Path $exe)) { L "NO EXE: $exe"; exit 5 }
Get-Item $exe | ForEach-Object { L "exe: $($_.LastWriteTime.ToString('s')) size=$($_.Length)" }

if (Test-Path $shotDir) { Remove-Item "$shotDir\*" -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
if (Test-Path $playerLog) { Remove-Item $playerLog -Force -ErrorAction SilentlyContinue }

# 截图需要真实图形设备,不能加 -nographics
$playerArgs = @(
    "-logFile", $playerLog,
    "-screen-fullscreen", "0",
    "-screen-width", "$Width",
    "-screen-height", "$Height",
    "-sandboxDrive",
    "-shotDir", ($shotDir -replace '\\', '/'),
    "-driveTimeout", "$([Math]::Max(60, $RunTimeout - 20))"
)
L "run player: $exe $($playerArgs -join ' ')"
$p = Start-Process -FilePath $exe -WindowStyle Hidden -ArgumentList $playerArgs -PassThru
if (-not $p.WaitForExit($RunTimeout * 1000)) {
    L "RUN TIMEOUT ${RunTimeout}s - killing player"
    try { $p.Kill() } catch {}
    exit 6
}
Start-Sleep -Seconds 1
L "player exit=$($p.ExitCode)"

$frames = @(Get-ChildItem $shotDir -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name)
L "frames=$($frames.Count) dir=$shotDir"
L "---- [SandboxDrive] ----"
Select-String -Path $playerLog -Pattern "\[SandboxDrive\]" -ErrorAction SilentlyContinue |
    ForEach-Object { L ("  " + $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length))) }
L "---- exceptions ----"
$bad = Select-String -Path $playerLog -Pattern "NullReference|Exception|error CS|Could not load|Failed to load|shot failed" -ErrorAction SilentlyContinue
if ($bad) { $bad | Select-Object -First 30 | ForEach-Object { L ("  " + $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length))) } }
else { L "  (none)" }
if ($bad -or $p.ExitCode -ne 0) { L 'FAIL: player reported errors'; exit 6 }

$pass = Select-String -Path $playerLog -Pattern "\[SandboxDrive\] RESULT=PASS" -Quiet -ErrorAction SilentlyContinue
if (-not $pass) { L "FAIL: drive did not report RESULT=PASS"; exit 6 }
if ($frames.Count -lt 20) { L "FAIL: too few frames"; exit 7 }
L "OK"
exit 0
