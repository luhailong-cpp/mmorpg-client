<#
.SYNOPSIS
  用真实 Unity 播放器跑移动验收(DevAutoPilot -moveTest):登录 → 出生点 → 连续 WASD → 撞墙 →
  绕过客户端 mask 硬冲墙(服务器必须纠偏)→ 点击寻路 → 退出;同一账号跑两遍验证重登落位。

.DESCRIPTION
  对应 2026-09-05 的"客户端/服务端导航与出生点不一致"修复(xuanming-server-mmo
  docs/design/nav-spawn-fix-2026-09-05.md)。每一轮播放器日志里由 DevAutoPilot 打出:
    [AutoPilot][tag] move: spawn feet=(x,y,z) walkable=True ...
    [AutoPilot][tag] move: wasd dir=... / move: wall ... / move: server_wall ... / move: path ...
    [AutoPilot][tag] RESULT=PASS stage=move_test spawn=(..) final=(..) acks=N snaps=0 ...
  本脚本断言:
    1. 每一轮 RESULT=PASS 且退出码 0;
    2. 每一轮 spawn walkable=True;
    3. 第 2 轮(重登)的 spawn 与第 1 轮的 final 水平距离 ≤ 2m(服务器把最后位置存下来了);
    4. 可选 -FreshAccount:再用一个新账号跑一轮(新角色首次登录,服务器落到默认出生点 (200,0,180) 附近);
    5. 合法道路不回拉:整轮 [GameClient] 日志里 "reconcile snap/settle" 的行数减去 server_wall 阶段
       自己报告的 snaps(那一段是故意绕过客户端 mask 冲墙,服务器回拉是预期)必须为 0
       (DevAutoPilot 内部已按阶段判 FAIL,这里再用原始日志兜底,防止阶段外/阶段间的回拉漏网)。

  前置:服务端栈已起且 scene 节点已加载新烘焙的导航(scene 日志有 "nav loaded: ... tiles=N" 与 "nav registered for scene 1");
        播放器已按新代码出包(见 nav-spawn-fix 文档 §Codex 步骤 4)。

.EXAMPLE
  pwsh -File tools/run_move_test.ps1
  pwsh -File tools/run_move_test.ps1 -Account robot_move_old -FreshAccount robot_move_new_$(Get-Date -Format HHmmss)
#>
[CmdletBinding()]
param(
    [string]$ExePath = "E:/work/tmp/showcase_player/mmorpg.exe",
    [string]$Gateway = "http://127.0.0.1:8081",
    [uint32]$Zone = 1,
    # 旧角色:用已有存档的账号(例如此前存着 (0,0,0) 的那个)验证"旧存档无效位置被服务器落到出生点"
    [string]$Account = "robot_move_self_20260905_0734",
    [string]$Password = "123456",
    # 新角色:留空则跳过;给一个从未登录过的账号名(login 侧 robot_ 前缀走 DevPasswordAuth,自动建号)
    [string]$FreshAccount = "",
    [int]$TimeoutSec = 150,
    [string]$LogDir = "E:/work/tmp/move_test",
    [int]$ScreenWidth = 1280,
    [int]$ScreenHeight = 720
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    Write-Host "[move] FAIL 播放器不存在: $ExePath(先按 nav-spawn-fix 文档出包)"
    exit 1
}
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Invoke-Round([string]$tag, [string]$account) {
    $log = Join-Path $LogDir "move_$tag.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    $playerArgs = @(
        "-logFile", $log,
        "-screen-fullscreen", "0",
        "-screen-width", "$ScreenWidth",
        "-screen-height", "$ScreenHeight",
        "-gateway", $Gateway,
        "-zone", "$Zone",
        "-account", $account,
        "-password", $Password,
        "-moveTest",
        "-quitOnMoveTestEnd",
        # scene 节点刚重拉时 etcd 端口租约要等几十秒才释放,进场会晚;GameClient 自己等 60s,别让驾驶员 30s 先放弃
        "-loginTimeout", "60",
        "-logTag", $tag
    )
    Write-Host "[move] round=$tag account=$account"
    $p = Start-Process -FilePath $ExePath -ArgumentList $playerArgs -PassThru
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        Write-Host "[move] round=$tag 超时 ${TimeoutSec}s,强杀"
        try { $p.Kill() } catch {}
        Start-Sleep -Seconds 2
    }
    # 退出后再等 1s 让 Unity 把日志尾部刷出来
    Start-Sleep -Seconds 1
    $text = if (Test-Path $log) { Get-Content $log -Raw } else { "" }
    $result = [regex]::Match($text, "\[AutoPilot\]\[$tag\] (RESULT=(PASS|FAIL)[^\r\n]*)")
    $spawn = [regex]::Match($text, "\[AutoPilot\]\[$tag\] move: spawn feet=\(([-0-9.]+),([-0-9.]+),([-0-9.]+)\) walkable=(True|False)")
    $final = [regex]::Match($text, "RESULT=PASS stage=move_test spawn=\([^)]*\) final=\(([-0-9.]+),([-0-9.]+),([-0-9.]+)\)")
    $snaps = ([regex]::Matches($text, "\[GameClient\] \[move\] reconcile (snap|settle)")).Count
    $acks = ([regex]::Matches($text, "\[GameClient\] \[move\] ack ")).Count
    $wall = [regex]::Match($text, "\[AutoPilot\]\[$tag\] move: server_wall [^\r\n]*snaps=(\d+)")
    $selfLine = [regex]::Match($text, "\[GameClient\] \[actor\] self [^\r\n]*")
    [pscustomobject]@{
        WallSnaps  = if ($wall.Success) { [int]$wall.Groups[1].Value } else { 0 }
        Tag        = $tag
        Account    = $account
        ExitCode   = $p.ExitCode
        Log        = $log
        Result     = if ($result.Success) { $result.Groups[1].Value } else { "(no RESULT line)" }
        Pass       = $result.Success -and $result.Groups[2].Value -eq "PASS" -and $p.ExitCode -eq 0
        SpawnOk    = $spawn.Success -and $spawn.Groups[4].Value -eq "True"
        Spawn      = if ($spawn.Success) { @([double]$spawn.Groups[1].Value, [double]$spawn.Groups[2].Value, [double]$spawn.Groups[3].Value) } else { $null }
        Final      = if ($final.Success) { @([double]$final.Groups[1].Value, [double]$final.Groups[2].Value, [double]$final.Groups[3].Value) } else { $null }
        Snaps      = $snaps
        Acks       = $acks
        SelfActor  = if ($selfLine.Success) { $selfLine.Value } else { "(no self actor line)" }
    }
}

function Format-Vec($v) { if ($null -eq $v) { "(n/a)" } else { "({0:n2},{1:n2},{2:n2})" -f $v[0], $v[1], $v[2] } }
function DistXZ($a, $b) { [math]::Sqrt(($a[0]-$b[0])*($a[0]-$b[0]) + ($a[2]-$b[2])*($a[2]-$b[2])) }

$rounds = @()
$rounds += Invoke-Round "R1" $Account
Start-Sleep -Seconds 3   # 让 scene 侧 HandleExitGameNode 的存盘先落地,再重登
$rounds += Invoke-Round "R2" $Account
if ($FreshAccount) { $rounds += Invoke-Round "NEW" $FreshAccount }

$ok = $true
foreach ($r in $rounds) {
    Write-Host ("[move] {0} exit={1} pass={2} spawn={3} spawnOk={4} final={5} acks={6} snaps={7} (server_wall={8})" -f `
        $r.Tag, $r.ExitCode, $r.Pass, (Format-Vec $r.Spawn), $r.SpawnOk, (Format-Vec $r.Final), $r.Acks, $r.Snaps, $r.WallSnaps)
    Write-Host "[move]   $($r.SelfActor)"
    Write-Host "[move]   $($r.Result)"
    if (-not $r.Pass) { $ok = $false }
    if (-not $r.SpawnOk) { $ok = $false; Write-Host "[move]   FAIL: 出生点不可走或未打印" }
    $stray = $r.Snaps - $r.WallSnaps
    if ($stray -ne 0) { $ok = $false; Write-Host "[move]   FAIL: server_wall 之外出现 $stray 次服务器回拉(合法道路被回拉,查 scene 日志 'move corrected')" }
}

$r1 = $rounds[0]; $r2 = $rounds[1]
if ($r1.Final -and $r2.Spawn) {
    $d = DistXZ $r1.Final $r2.Spawn
    Write-Host ("[move] relogin: R1.final={0} -> R2.spawn={1} distXZ={2:n2}m (expect <= 2)" -f (Format-Vec $r1.Final), (Format-Vec $r2.Spawn), $d)
    if ($d -gt 2.0) { $ok = $false; Write-Host "[move]   FAIL: 重登没有回到上次位置(服务器没存/存了非法点被重置)" }
} else {
    $ok = $false
    Write-Host "[move] relogin: 缺少 R1.final 或 R2.spawn,无法比对"
}
if ($FreshAccount) {
    $n = $rounds[2]
    if ($n.Spawn) {
        $d = DistXZ $n.Spawn @(200.0, 0.0, 180.0)
        Write-Host ("[move] fresh: spawn={0} distXZ to default (200,0,180) = {1:n2}m (expect <= 2)" -f (Format-Vec $n.Spawn), $d)
        if ($d -gt 2.0) { $ok = $false; Write-Host "[move]   FAIL: 新角色没有落在默认出生点" }
    }
}

Write-Host ("[move] {0}  logs: {1}" -f ($(if ($ok) { "PASS" } else { "FAIL" })), (($rounds | ForEach-Object { $_.Log }) -join ", "))
exit $(if ($ok) { 0 } else { 1 })
