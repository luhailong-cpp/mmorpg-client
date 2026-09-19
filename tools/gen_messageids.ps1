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
    # 帮会：只公开玩家读取与成员操作；评分写入属于服务端。
    "GuildServiceCreateGuild" = "CreateGuild"
    "GuildServiceGetGuild" = "GetGuild"
    "GuildServiceGetPlayerGuild" = "GetPlayerGuild"
    "GuildServiceApplyJoinGuild" = "ApplyJoinGuild"
    "GuildServiceLeaveGuild" = "LeaveGuild"
    "GuildServiceDisbandGuild" = "DisbandGuild"
    "GuildServiceSetAnnouncement" = "SetGuildAnnouncement"
    "GuildServiceGetGuildRank" = "GetGuildRank"
    "GuildServiceGetGuildRankByGuild" = "GetGuildRankByGuild"

    # 帮会二期:管理与审批(proto/guild/guild.proto,设计 docs/design/guild-phase2/02-management.md §16)。
    # 申请制入口 GuildServiceApplyJoinGuild 已在上面那段(它是 JoinGuild 被删后原地替换的那一行),
    # **不要在这里再写一遍** —— PowerShell 的 @{} 字面量遇到重复键会直接抛
    # "Duplicate keys are not allowed in hash literals",整个脚本跑不起来。
    # 注意 19 号消息 id 由 JoinGuild 易主给了 SetGuildMemberRole:这里只写方法名,
    # 号从 proto/message_id.txt 现场读,不写死(当前 19 / 216..223)。
    "GuildServiceSetGuildMemberRole"       = "SetGuildMemberRole"
    "GuildServiceKickGuildMember"          = "KickGuildMember"
    "GuildServiceTransferGuildLeader"      = "TransferGuildLeader"
    "GuildServiceCancelGuildApplication"   = "CancelGuildApplication"
    "GuildServiceListMyGuildApplications"  = "ListMyGuildApplications"
    "GuildServiceListGuildApplications"    = "ListGuildApplications"
    "GuildServiceReviewGuildApplication"   = "ReviewGuildApplication"
    # 推送占位:S→C 只收不发(服务端白名单拒绝客户端调用),客户端用 GameClient.OnNotify 注册。
    # 漏配这一条不会报错,只会少生成一个常量、订阅代码编不过(TravelToZone 就踩过)。
    "GuildServiceNotifyGuildChanged"       = "NotifyGuildChanged"

    # 聚宝斋(proto/trade/jubaozhai.proto, service ClientPlayerJubaozhai)。
    # 内部 TradeAdmin(SeedListing)刻意不列:gate 不转发,客户端也不应持有其消息号。
    "ClientPlayerJubaozhaiBrowseListings"          = "JubaozhaiBrowseListings"
    "ClientPlayerJubaozhaiGetListingDetail"        = "JubaozhaiGetListingDetail"
    "ClientPlayerJubaozhaiSetFavorite"             = "JubaozhaiSetFavorite"
    "ClientPlayerJubaozhaiGetMyShelf"              = "JubaozhaiGetMyShelf"

    "ClientPlayerLoginLogin"                       = "Login"
    "ClientPlayerLoginCreatePlayer"                = "CreatePlayer"
    "ClientPlayerLoginEnterGame"                   = "EnterGame"
    "ClientPlayerLoginLeaveGame"                   = "LeaveGame"
    "ClientPlayerLoginRefreshToken"                = "RefreshToken"
    "ClientPlayerLoginDisconnect"                  = "Disconnect"

    "SceneSceneClientPlayerEnterScene"             = "EnterScene"
    # 跨 zone 场景传送(服务端 docs/design/cross-zone-scene-travel.md CZ-7)。漏配只会出一条
    # Warning、常量静默不生成,Assets/Scripts/Game/WorldTravel/ZoneTravelClient.cs 就编不过。
    "SceneSceneClientPlayerTravelToZone"           = "TravelToZone"
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

    # 二期:观战 + 自动战斗(设计文档 §10/§11,service BattleClientPlayer)
    "BattleClientPlayerStopWatchBattle"            = "StopWatchBattle"
    "BattleClientPlayerSetAutoBattle"              = "SetAutoBattle"
    "BattleClientPlayerNotifySpectateState"        = "NotifySpectateState"
    "BattleClientPlayerNotifySpectateTurnResult"   = "NotifySpectateTurnResult"
    "BattleClientPlayerNotifySpectateEnd"          = "NotifySpectateEnd"

    # 匹配/切磋(proto/match/match_service.proto, service MatchService)
    "MatchServiceJoinQueue"                        = "JoinQueue"
    "MatchServiceCancelQueue"                      = "CancelQueue"
    "MatchServiceGetQueueStatus"                   = "GetQueueStatus"
    "MatchServiceChallengePlayer"                  = "ChallengePlayer"
    "MatchServiceRespondChallenge"                 = "RespondChallenge"
    "MatchServiceNotifyChallengeInvite"            = "NotifyChallengeInvite"
    "MatchServiceNotifyChallengeResult"            = "NotifyChallengeResult"

    # 二期:观战匹配(设计文档 §10,service MatchService)
    "MatchServiceWatchBattle"                      = "WatchBattle"
    "MatchServiceListWatchableBattles"             = "ListWatchableBattles"

    # 角色属性加点(proto/scene/player_attribute.proto, service SceneAttributeClientPlayer)
    "SceneAttributeClientPlayerGetAttributePanel"            = "GetAttributePanel"
    "SceneAttributeClientPlayerAllocateAttributePoints"      = "AllocateAttributePoints"
    "SceneAttributeClientPlayerResetAttributePoints"         = "ResetAttributePoints"
    "SceneAttributeClientPlayerAutoAllocateAttributePoints"  = "AutoAllocateAttributePoints"
    "SceneAttributeClientPlayerCreateAttributeScheme"        = "CreateAttributeScheme"
    "SceneAttributeClientPlayerSwitchAttributeScheme"        = "SwitchAttributeScheme"
    "SceneAttributeClientPlayerRenameAttributeScheme"        = "RenameAttributeScheme"
    "SceneAttributeClientPlayerNotifyAttributePanelChanged"  = "NotifyAttributePanelChanged"
    "SceneAttributeClientPlayerGmSetPlayerLevel"             = "GmSetPlayerLevel"

    # 宝宝(宠物)系统(proto/scene/player_pet.proto, service ScenePetClientPlayer)
    "ScenePetClientPlayerGetPetList"                         = "GetPetList"
    "ScenePetClientPlayerSummonPet"                          = "SummonPet"
    "ScenePetClientPlayerRecallPet"                          = "RecallPet"
    "ScenePetClientPlayerAllocatePetPoints"                  = "AllocatePetPoints"
    "ScenePetClientPlayerResetPetPoints"                     = "ResetPetPoints"
    "ScenePetClientPlayerAutoAllocatePetPoints"              = "AutoAllocatePetPoints"
    "ScenePetClientPlayerRenamePet"                          = "RenamePet"
    "ScenePetClientPlayerNotifyPetListChanged"               = "NotifyPetListChanged"
    "ScenePetClientPlayerGmGrantPet"                         = "GmGrantPet"
    # 背包、任务、活动窗口（正式协议注册表分配编号）。
    "SceneBagClientPlayerGetBag"                            = "GetBag"
    "SceneBagClientPlayerSortBag"                           = "SortBag"
    "SceneMissionClientPlayerGetMissionList"                = "GetMissionList"
    "SceneActivityClientPlayerGetActivityList"              = "GetActivityList"

    # 客户端直连 battle 节点(turn-based-battle-server.md §18):落点分配推送 + 丢票补签
    "BattleClientPlayerNotifyBattleAssigned"       = "NotifyBattleAssigned"
    "MatchServiceRequestBattleTicket"              = "RequestBattleTicket"

    # 组队(proto/team/team.proto, service ClientPlayerTeam;docs/design/team-system.md §H.1)。
    # 后三条是 S→C 推送:服务端写成返回 Empty 的 rpc 只为拿消息号,客户端用 GameClient.OnNotify 注册。
    # 数值不写死在这里,由 proto/message_id.txt 现场读取(当前 201..215)。
    "ClientPlayerTeamCreateTeam"                   = "CreateTeam"
    "ClientPlayerTeamGetMyTeam"                    = "GetMyTeam"
    "ClientPlayerTeamApplyJoinTeam"                = "ApplyJoinTeam"
    "ClientPlayerTeamHandleApplication"            = "HandleApplication"
    "ClientPlayerTeamInviteToTeam"                 = "InviteToTeam"
    "ClientPlayerTeamRespondInvite"                = "RespondInvite"
    "ClientPlayerTeamListMyInvites"                = "ListMyInvites"
    "ClientPlayerTeamLeaveTeam"                    = "LeaveTeam"
    "ClientPlayerTeamKickMember"                   = "KickMember"
    "ClientPlayerTeamTransferLeader"               = "TransferLeader"
    "ClientPlayerTeamDisbandTeam"                  = "DisbandTeam"
    "ClientPlayerTeamStartTeamMatch"               = "StartTeamMatch"
    "ClientPlayerTeamNotifyTeamSnapshot"           = "NotifyTeamSnapshot"
    "ClientPlayerTeamNotifyTeamInvite"             = "NotifyTeamInvite"
    "ClientPlayerTeamNotifyTeamEvent"              = "NotifyTeamEvent"
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

Set-Content -LiteralPath $outFile -Value ($sb.ToString().Replace("`r`n", "`n")) -Encoding UTF8 -NoNewline
Write-Host "[gen_messageids] wrote $outFile ($($emit.Count) entries)"
if ($missing.Count -gt 0) {
    Write-Warning ("[gen_messageids] missing in message_id.txt: " + ($missing -join ", "))
}
