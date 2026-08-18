<#
.SYNOPSIS
    Generate Assets/Scripts/Net/MessageIds.cs from the parent repo's
    proto/message_id.txt, so client RPC IDs never drift from the server.

.DESCRIPTION
    Reads `<id>=<MethodName>` lines and emits a typed C# constant for each
    RPC the client cares about (whitelist below). Anything not whitelisted
    is dropped to keep the file scoped to client-relevant traffic.

.PARAMETER ProtoRoot
    Parent mmorpg repo root. Defaults to ../../../ (this client repo lives
    at client/unity/ inside that repo as a submodule).
#>
param(
    [string]$ProtoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
)

$ErrorActionPreference = "Stop"

# proto/message_id.txt 是源契约(服务器 CLAUDE.md §4:proto 源在 proto/,
# proto-gen 会把新分配的 id 写回这里);generated/_unified 只是某次生成的
# 快照,可能落后于源(2026-08 就因它缺 battle/match 段漏生成过)。
$source = Join-Path $ProtoRoot "proto/message_id.txt"
if (-not (Test-Path $source)) {
    $source = Join-Path $ProtoRoot "generated/proto/_unified/proto/message_id.txt"
}
if (-not (Test-Path $source)) { throw "message_id.txt not found under $ProtoRoot" }

# whitelist: server method name (without `Service.` prefix) -> client constant name.
# This is the contract surface the Unity client speaks. Keep it small and
# audited; new RPCs require an explicit entry here.
$whitelist = @{
    "ClientPlayerLoginLogin"                       = "Login"
    "ClientPlayerLoginCreatePlayer"                = "CreatePlayer"
    "ClientPlayerLoginEnterGame"                   = "EnterGame"
    "ClientPlayerLoginLeaveGame"                   = "LeaveGame"
    "ClientPlayerLoginRefreshToken"                = "RefreshToken"
    "ClientPlayerLoginDisconnect"                  = "Disconnect"

    "SceneSceneClientPlayerEnterScene"             = "EnterScene"
    "SceneSceneClientPlayerSceneInfoC2S"           = "SceneInfoC2S"
    "SceneSkillClientPlayerReleaseSkill"           = "ReleaseSkill"
    "SceneSkillClientPlayerListSkills"             = "ListSkills"

    "SceneMovementClientPlayerMoveStart"           = "MoveStart"
    "SceneMovementClientPlayerMoveStop"            = "MoveStop"
    "SceneMovementClientPlayerMoveSync"            = "MoveSync"
    "SceneMovementClientPlayerTeleportRequest"     = "TeleportRequest"

    "SceneSceneClientPlayerNotifyEnterScene"       = "NotifyEnterScene"
    "SceneSceneClientPlayerNotifySceneInfo"        = "NotifySceneInfo"
    "SceneSceneClientPlayerNotifyActorCreate"      = "NotifyActorCreate"
    "SceneSceneClientPlayerNotifyActorListCreate"  = "NotifyActorListCreate"
    "SceneSceneClientPlayerNotifyActorDestroy"     = "NotifyActorDestroy"
    "SceneSceneClientPlayerNotifyActorListDestroy" = "NotifyActorListDestroy"
    "SceneSkillClientPlayerNotifySkillUsed"        = "NotifySkillUsed"
    "SceneSkillClientPlayerNotifySkillInterrupted" = "NotifySkillInterrupted"
    "SceneClientPlayerCommonSendTipToClient"       = "TipToClient"
    "SceneClientPlayerCommonKickPlayer"            = "KickPlayer"
    "SceneClientPlayerCommonRedirectToGate"        = "RedirectToGate"

    "SceneMovementClientPlayerNotifyMoveAck"       = "NotifyMoveAck"
    "SceneMovementClientPlayerNotifyActorMove"     = "NotifyActorMove"
    "SceneMovementClientPlayerNotifyActorMoveList" = "NotifyActorMoveList"
    "SceneMovementClientPlayerNotifyTeleport"      = "NotifyTeleport"

    # 回合制战斗(proto/battle/player_battle.proto, service BattleClientPlayer)
    "BattleClientPlayerSubmitBattleAction"         = "SubmitBattleAction"
    "BattleClientPlayerGetBattleState"             = "GetBattleState"
    "BattleClientPlayerNotifyBattleStart"          = "NotifyBattleStart"
    "BattleClientPlayerNotifyTurnResult"           = "NotifyTurnResult"
    "BattleClientPlayerNotifyBattleEnd"            = "NotifyBattleEnd"
    "BattleClientPlayerNotifyBattleReconnect"      = "NotifyBattleReconnect"

    # 匹配/切磋(proto/match/match_service.proto, service MatchService)
    "MatchServiceJoinQueue"                        = "JoinQueue"
    "MatchServiceCancelQueue"                      = "CancelQueue"
    "MatchServiceGetQueueStatus"                   = "GetQueueStatus"
    "MatchServiceChallengePlayer"                  = "ChallengePlayer"
    "MatchServiceRespondChallenge"                 = "RespondChallenge"
    "MatchServiceNotifyChallengeInvite"            = "NotifyChallengeInvite"
    "MatchServiceNotifyChallengeResult"            = "NotifyChallengeResult"
}

$idByMethod = @{}
foreach ($line in Get-Content -LiteralPath $source) {
    if ($line -match '^\s*(\d+)\s*=\s*([A-Za-z0-9_]+)\s*$') {
        $idByMethod[$Matches[2]] = [int]$Matches[1]
    }
}

$missing = @()
$emit = New-Object System.Collections.Generic.List[object]
foreach ($k in $whitelist.Keys) {
    if ($idByMethod.ContainsKey($k)) {
        $emit.Add([pscustomobject]@{ Const = $whitelist[$k]; Id = $idByMethod[$k]; Method = $k })
    } else {
        $missing += $k
    }
}
$emit = $emit | Sort-Object Const

$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$outFile  = Join-Path $repoRoot "Assets/Scripts/Net/MessageIds.cs"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("// <auto-generated>")
[void]$sb.AppendLine("// Generated by tools/gen_messageids.ps1 from proto/message_id.txt.")
[void]$sb.AppendLine("// Do not edit by hand. Re-run after the server regenerates IDs.")
[void]$sb.AppendLine("// </auto-generated>")
[void]$sb.AppendLine("namespace MmorpgClient.Net")
[void]$sb.AppendLine("{")
[void]$sb.AppendLine("    public static class MessageIds")
[void]$sb.AppendLine("    {")
foreach ($e in $emit) {
    [void]$sb.AppendLine(("        public const uint {0,-22} = {1,4}; // {2}" -f $e.Const, $e.Id, $e.Method))
}
[void]$sb.AppendLine("    }")
[void]$sb.AppendLine("}")

Set-Content -LiteralPath $outFile -Value $sb.ToString() -Encoding UTF8 -NoNewline
Write-Host "[gen_messageids] wrote $outFile ($($emit.Count) entries)"
if ($missing.Count -gt 0) {
    Write-Warning ("[gen_messageids] missing in message_id.txt: " + ($missing -join ", "))
}
