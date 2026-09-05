<#
.SYNOPSIS
  用两份真实 Unity 播放器实例做跨区 1v1 匹配验证(A 登 zone 1、B 登 zone 2)。

.DESCRIPTION
  两实例都由 DevAutoPilot 驱动(见 Assets/Scripts/App/DevAutoPilot.cs):
    选区 → 登录 → 进场景 → JoinQueue(1v1, battle_config) → 自动战斗 → 打完退出。
  脚本等待两进程退出(默认最多 5 分钟),解析两份播放器日志并断言
  (对齐 robot/battle_smoke_cross_zone_scenario.go 的 zone-placement / battle-id / turn-count 三条):
    1. 两边 "stage=in_game … gate=ip:port" 的 gate 地址不同(落区证据:本地各 zone 的 gate 端口
       不同,相同即两人其实进了同一 zone,匹配成功也不算跨区);
    2. 两边都有 "BattleStart battle_id=N" 且 battle_id 相同;
    3. 两边都有 "BattleEnd battle_id=N … turns=N" 且 turns ≥ 1(开局即终局不算打完);
    4. 两进程退出码都是 0(DevAutoPilot:0=打完,1=任一阶段超时/失败)。
  输出 PASS/FAIL 摘要(PASS 行的 gate 是运行时真实地址,不是入参回显),脚本退出码 0/1。
  注意:超时强杀不发 LeaveGame,scene 侧 battle:lock 要等解冻(默认 ≈5-6 分钟)才能复跑,
        否则登录即收 BattleReconnect,自动驾驶会立刻报 RESULT=FAIL stage=queue。

  前置:服务端双 zone + match + battle 已起(见 xuanming-server-mmo robot/etc/battle_smoke_cross_zone.yaml 头注释),
        播放器已由 tools/build_crosszone_player.ps1 出到 -ExePath。
  账号 robot_ 前缀走 login 侧 DevPasswordAuth,与服务端 robot 冒烟(robot_9003/9004)错开。

.EXAMPLE
  pwsh -File tools/run_crosszone_pair.ps1
  pwsh -File tools/run_crosszone_pair.ps1 -TimeoutSec 400 -LogDir E:/work/tmp/crosszone_run1
#>
[CmdletBinding()]
param(
    [string]$ExePath = "E:/work/tmp/crosszone_player/mmorpg.exe",
    [string]$Gateway = "http://127.0.0.1:8081",
    [uint32]$ZoneA = 1,
    [uint32]$ZoneB = 2,
    [string]$AccountA = "robot_9101",
    [string]$AccountB = "robot_9102",
    [string]$Password = "123456",
    [string]$Mode = "1v1",
    [uint32]$BattleConfig = 1,
    [int]$TimeoutSec = 300,
    [string]$LogDir = "E:/work/tmp",
    # 演出截图(视觉验收):给出目录则两实例各自截帧到 <ShotDir>/A、<ShotDir>/B
    [string]$ShotDir = "",
    [double]$ShotInterval = 0.4,
    [int]$ScreenWidth = 1280,
    [int]$ScreenHeight = 720
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    Write-Host "[pair] FAIL 播放器不存在: $ExePath(先跑 tools/build_crosszone_player.ps1)"
    exit 1
}
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$sides = @(
    @{ Tag = "A"; Zone = $ZoneA; Account = $AccountA; Log = (Join-Path $LogDir "crosszone_player_A.log") },
    @{ Tag = "B"; Zone = $ZoneB; Account = $AccountB; Log = (Join-Path $LogDir "crosszone_player_B.log") }
)

foreach ($s in $sides) {
    if (Test-Path $s.Log) { Remove-Item $s.Log -Force }
    # -screen-* 保证两窗口不全屏互抢焦点;runInBackground=1 已在 ProjectSettings 打开,
    # 失焦实例照常 Update 收网络消息。
    $s.Args = @(
        "-logFile", $s.Log,
        "-screen-fullscreen", "0",
        "-screen-width", "$ScreenWidth",
        "-screen-height", "$ScreenHeight",
        "-gateway", $Gateway,
        "-zone", "$($s.Zone)",
        "-account", $s.Account,
        "-password", $Password,
        "-autoQueue", $Mode,
        "-battleConfig", "$BattleConfig",
        "-autoBattle",
        "-quitOnBattleEnd",
        "-logTag", $s.Tag
    )
    if ($ShotDir) {
        $sideShots = Join-Path $ShotDir $s.Tag
        New-Item -ItemType Directory -Force -Path $sideShots | Out-Null
        $s.Args += @("-shotDir", $sideShots, "-shotInterval", "$ShotInterval")
    }
}

# 两实例几乎同时起:各自登录 ~几秒,排队 60s 窗口足够互相等到
$procs = @{}
foreach ($s in $sides) {
    Write-Host "[pair] start $($s.Tag): zone=$($s.Zone) account=$($s.Account) log=$($s.Log)"
    $procs[$s.Tag] = Start-Process -FilePath $ExePath -ArgumentList $s.Args -PassThru
    Start-Sleep -Milliseconds 500
}

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$timedOut = $false
while ($true) {
    $alive = @($procs.Values | Where-Object { -not $_.HasExited })
    if ($alive.Count -eq 0) { break }
    if ((Get-Date) -gt $deadline) {
        $timedOut = $true
        foreach ($p in $alive) {
            Write-Host "[pair] timeout ${TimeoutSec}s: killing pid=$($p.Id)"
            try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch {}
        }
        break
    }
    Start-Sleep -Seconds 2
}
# 进程退出后播放器日志可能还在落盘
Start-Sleep -Seconds 1

function Get-FirstMatch {
    param([string]$Path, [string]$Pattern)
    if (-not (Test-Path $Path)) { return $null }
    $m = Select-String -Path $Path -Pattern $Pattern | Select-Object -First 1
    if ($null -eq $m) { return $null }
    return $m.Matches[0]
}

$results = @{}
$failures = New-Object System.Collections.Generic.List[string]
foreach ($s in $sides) {
    $tag = $s.Tag
    $p = $procs[$tag]
    $exit = if ($p.HasExited) { $p.ExitCode } else { $null }
    $esc = [regex]::Escape("[AutoPilot][$tag]")
    # 落区证据:DevAutoPilot 的 in_game 行带 gate=ip:port;老播放器没有该字段时
    # 回落到 GameClient 自己的 "[GameClient] assigned gate ip:port" 行
    $gate  = Get-FirstMatch $s.Log "$esc stage=in_game .*gate=(\S+)"
    if (-not $gate -or $gate.Groups[1].Value -eq "-") {
        $gate = Get-FirstMatch $s.Log "\[GameClient\] assigned gate (\S+)"
    }
    $start = Get-FirstMatch $s.Log "$esc BattleStart battle_id=(\d+)"
    $end   = Get-FirstMatch $s.Log "$esc BattleEnd battle_id=(\d+) outcome=(\S+) turns=(\d+)"
    $res   = Get-FirstMatch $s.Log "$esc RESULT=(PASS|FAIL)(.*)$"

    $r = [ordered]@{
        Tag = $tag; Exit = $exit
        Gate          = if ($gate)  { $gate.Groups[1].Value }  else { $null }
        StartBattleId = if ($start) { $start.Groups[1].Value } else { $null }
        EndBattleId   = if ($end)   { $end.Groups[1].Value }   else { $null }
        Outcome       = if ($end)   { $end.Groups[2].Value }   else { $null }
        Turns         = if ($end)   { $end.Groups[3].Value }   else { $null }
        Result        = if ($res)   { $res.Groups[1].Value + $res.Groups[2].Value } else { "(no RESULT line)" }
    }
    $results[$tag] = $r
    Write-Host ("[pair] {0}: exit={1} gate={2} BattleStart={3} BattleEnd={4} outcome={5} turns={6} {7}" -f
        $tag, $exit, $r.Gate, $r.StartBattleId, $r.EndBattleId, $r.Outcome, $r.Turns, $r.Result)

    if (-not (Test-Path $s.Log)) { $failures.Add("$tag 没有日志文件 $($s.Log)") }
    if ($null -eq $r.Gate)          { $failures.Add("$tag 没有 gate 落区记录(in_game 行 gate= / [GameClient] assigned gate)") }
    if ($null -eq $r.StartBattleId) { $failures.Add("$tag 没有 BattleStart") }
    if ($null -eq $r.EndBattleId)   { $failures.Add("$tag 没有 BattleEnd") }
    if ($end -and [int]$r.Turns -lt 1) { $failures.Add("$tag turns=$($r.Turns)(期望 ≥ 1:开局即终局不算打完)") }
    if ($exit -ne 0)                { $failures.Add("$tag 退出码=$exit(期望 0)") }
}

if ($timedOut) { $failures.Add("等待 ${TimeoutSec}s 超时,已强杀未退出的实例") }
$a = $results["A"]; $b = $results["B"]
# zone-placement:与 robot 参考脚本一致,两人分到同一 gate 即不是跨区,匹配成功也判失败
if ($a.Gate -and $b.Gate -and $a.Gate -eq $b.Gate) {
    $failures.Add("两实例分到同一 gate $($a.Gate)(zone_a=$ZoneA zone_b=$ZoneB),不是跨区对局(zone 2 的 gate/login 没起或 assign-gate 回落?)")
}
if ($a.StartBattleId -and $b.StartBattleId -and $a.StartBattleId -ne $b.StartBattleId) {
    $failures.Add("battle_id 不一致: A=$($a.StartBattleId) B=$($b.StartBattleId)(两人没匹配进同一场)")
}
if ($a.StartBattleId -and $a.EndBattleId -and $a.StartBattleId -ne $a.EndBattleId) {
    $failures.Add("A 的 BattleEnd battle_id 与 BattleStart 不一致")
}
if ($b.StartBattleId -and $b.EndBattleId -and $b.StartBattleId -ne $b.EndBattleId) {
    $failures.Add("B 的 BattleEnd battle_id 与 BattleStart 不一致")
}

if ($failures.Count -eq 0) {
    Write-Host ("[pair] CROSS_ZONE_PAIR_PASS battle_id={0} zone_a={1} gate_a={2} zone_b={3} gate_b={4} a_outcome={5} b_outcome={6} a_turns={7} b_turns={8}" -f
        $a.StartBattleId, $ZoneA, $a.Gate, $ZoneB, $b.Gate, $a.Outcome, $b.Outcome, $a.Turns, $b.Turns)
    exit 0
}

Write-Host "[pair] CROSS_ZONE_PAIR_FAIL"
foreach ($f in $failures) { Write-Host "  - $f" }
foreach ($s in $sides) {
    if (Test-Path $s.Log) {
        Write-Host "---- $($s.Tag) AutoPilot 日志行 ($($s.Log)) ----"
        Select-String -Path $s.Log -Pattern "\[AutoPilot\]" | Select-Object -Last 30 | ForEach-Object { Write-Host $_.Line }
    }
}
exit 1
