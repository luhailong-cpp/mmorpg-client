<#
.SYNOPSIS
    Generate Assets/Scripts/Net/MessageIds.cs from the server repo's
    proto/message_id.txt, so client RPC IDs never drift from the server.

.DESCRIPTION
    Reads `<id>=<MethodName>` lines and emits a typed C# constant for each
    RPC the client cares about (whitelist below). Anything not whitelisted
    is dropped to keep the file scoped to client-relevant traffic.

.PARAMETER ProtoRoot
    Server mmorpg repo root. Defaults to ../../mmorpg relative to this script,
    i.e. the sibling-repo layout (<workspace>/mmorpg-client next to
    <workspace>/mmorpg). The old default ../../.. assumed the retired submodule
    layout and now resolves to the workspace's parent directory; pass
    -ProtoRoot explicitly for any other layout.
#>
param(
    [string]$ProtoRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = (Resolve-Path (Join-Path $scriptDir "..")).Path
if ([string]::IsNullOrWhiteSpace($ProtoRoot)) {
    foreach ($candidate in @('xuanming-server-mmo-cpp', 'xuanming-server-mmo', 'mmorpg')) {
        $probe = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "../$candidate"))
        if ((Test-Path -LiteralPath (Join-Path $probe 'proto/message_id.txt')) -and
            (Test-Path -LiteralPath (Join-Path $probe 'go/shared/generated/tip/faults.go'))) {
            $ProtoRoot = $probe
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($ProtoRoot)) {
        throw "未找到同级权威服务端仓（已探测 ../xuanming-server-mmo-cpp、../xuanming-server-mmo、../mmorpg）；请显式传入 -ProtoRoot。"
    }
}
$ProtoRoot = (Resolve-Path -LiteralPath $ProtoRoot).Path

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
    "GuildServiceRenameGuild" = "RenameGuild"
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
    # 帮会二期 B5 经济:捐献 / 升级 / 商店(设计 docs/design/guild-phase2/05-economy.md §5.33)。
    # 注意 53 / 76 / 120 是从 FriendService 易主来的旧号(B5a),客户端必须随本批重生 MessageIds.cs。
    "GuildServiceGetGuildDonateOptions"    = "GetGuildDonateOptions"
    "GuildServiceDonateToGuild"            = "DonateToGuild"
    "GuildServiceUpgradeGuild"             = "UpgradeGuild"
    "GuildServiceGetGuildShop"             = "GetGuildShop"
    "GuildServiceBuyGuildShopGoods"        = "BuyGuildShopGoods"
    # 帮会二期 B6 活动:灯会 / 团圆 / 历练(设计 docs/design/guild-phase2/06-activities.md §6.36)。
    # 五个号在 B6a 一次加齐;历练两个在服务端 B6b 落地前固定回"未开放",客户端 B6b 才用到,先占好常量。
    # 常量名不与别的域撞:GetActivityList 是 scene 的活动窗口、RespondInvite 是组队邀请,与这里的不是一回事。
    "GuildServiceGetGuildActivities"       = "GetGuildActivities"
    "GuildServiceLightGuildLantern"        = "LightGuildLantern"
    "GuildServiceClaimGuildReunion"        = "ClaimGuildReunion"
    "GuildServiceStartGuildTrial"          = "StartGuildTrial"
    "GuildServiceRespondGuildTrialInvite"  = "RespondGuildTrialInvite"

    # 聚宝斋(proto/trade/jubaozhai.proto, service ClientPlayerJubaozhai)。
    # 内部 TradeAdmin(SeedListing)刻意不列:gate 不转发,客户端也不应持有其消息号。
    "ClientPlayerJubaozhaiBrowseListings"          = "JubaozhaiBrowseListings"
    "ClientPlayerJubaozhaiGetListingDetail"        = "JubaozhaiGetListingDetail"
    "ClientPlayerJubaozhaiSetFavorite"             = "JubaozhaiSetFavorite"
    "ClientPlayerJubaozhaiGetMyShelf"              = "JubaozhaiGetMyShelf"
    "ClientPlayerJubaozhaiListOfficialSkus"        = "JubaozhaiListOfficialSkus"
    "ClientPlayerJubaozhaiCreateOfficialOrder"     = "JubaozhaiCreateOfficialOrder"
    "ClientPlayerJubaozhaiGetCommerceOrder"        = "JubaozhaiGetCommerceOrder"
    "ClientPlayerJubaozhaiCancelCommerceOrder"     = "JubaozhaiCancelCommerceOrder"
    "ClientPlayerJubaozhaiListCommerceOrders"      = "JubaozhaiListCommerceOrders"
    "ClientPlayerJubaozhaiCreatePlayerTrade"       = "JubaozhaiCreatePlayerTrade"
    "ClientPlayerJubaozhaiConfirmPlayerTrade"      = "JubaozhaiConfirmPlayerTrade"
    "ClientPlayerJubaozhaiCreateConsignment"       = "JubaozhaiCreateConsignment"
    "ClientPlayerJubaozhaiBuyConsignment"          = "JubaozhaiBuyConsignment"
    "ClientPlayerJubaozhaiCancelConsignment"       = "JubaozhaiCancelConsignment"
    "ClientPlayerJubaozhaiPlaceMarketOrder"        = "JubaozhaiPlaceMarketOrder"
    "ClientPlayerJubaozhaiCancelMarketOrder"       = "JubaozhaiCancelMarketOrder"
    "ClientPlayerJubaozhaiListMarketOrders"        = "JubaozhaiListMarketOrders"
    "ClientPlayerJubaozhaiGetMarketDepth"          = "JubaozhaiGetMarketDepth"
    "ClientPlayerJubaozhaiCreateAuctionLot"        = "JubaozhaiCreateAuctionLot"
    "ClientPlayerJubaozhaiPlaceAuctionBid"         = "JubaozhaiPlaceAuctionBid"
    "ClientPlayerJubaozhaiGetAuctionLot"           = "JubaozhaiGetAuctionLot"
    "ClientPlayerJubaozhaiCancelAuctionLot"        = "JubaozhaiCancelAuctionLot"
    "ClientPlayerJubaozhaiBrowseCommerceOffers"    = "JubaozhaiBrowseCommerceOffers"
    "ClientPlayerJubaozhaiListTradeMarkets"        = "JubaozhaiListTradeMarkets"
    "ClientPlayerJubaozhaiGetSellerCredit"         = "JubaozhaiGetSellerCredit"
    "ClientPlayerJubaozhaiSubmitSellerReview"      = "JubaozhaiSubmitSellerReview"
    "ClientPlayerJubaozhaiGetMarketReferencePrice" = "JubaozhaiGetMarketReferencePrice"
    "ClientPlayerJubaozhaiGetPlayerTradeQuota"     = "JubaozhaiGetPlayerTradeQuota"
    "ClientPlayerMailListMails"                    = "MailListMails"
    "ClientPlayerMailReadMail"                     = "MailReadMail"
    "ClientPlayerMailClaimMailAttachments"         = "MailClaimMailAttachments"
    "ClientPlayerMailDeleteMails"                  = "MailDeleteMails"
    "ClientPlayerMailClearReadMails"               = "MailClearReadMails"
    "ClientPlayerMailNotifyMailChanged"            = "MailNotifyMailChanged"
    "ClientPlayerRankGetRankPage"                  = "RankGetRankPage"
    "ClientPlayerRankGetRankAround"                = "RankGetRankAround"

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
    # 落地恢复:按域版本摘要(服务端 docs/design/landing-recovery-sync-digest.md §4.1)。GetSyncDigest 的应答是 Empty,
    # 数据走 NotifySyncDigest 推送;两个号只在 Assets/Scripts/Game/Sync/LandingSyncClient.cs 里发送与注册。
    # 漏配只出一条 Warning、常量不生成,LandingSyncClient.cs 就是 CS0117。
    "SceneSceneClientPlayerGetSyncDigest"          = "GetSyncDigest"
    "SceneSceneClientPlayerNotifySyncDigest"       = "NotifySyncDigest"
    "SceneSceneClientPlayerSceneInfoC2S"           = "SceneInfoC2S"
    "SceneSkillClientPlayerReleaseSkill"           = "ReleaseSkill"
    "SceneSkillClientPlayerListSkills"             = "ListSkills"
    # 四职业技能修习(服务端 docs/design/class-skills.md §4)
    "SceneSkillClientPlayerUpgradeSkill"           = "UpgradeSkill"
    # 人物战法构筑装配 3 套流派方案(服务端 docs/design/battle-skill-and-pet-cultivation-expansion.md)
    "SceneSkillClientPlayerSaveSkillLoadout"       = "SaveSkillLoadout"
    "SceneSkillClientPlayerSwitchSkillLoadout"     = "SwitchSkillLoadout"

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

    # 二期:观战 + 自动战斗 + PVP延迟投降(设计文档 §10/§11 & ranked-play-v2-overhaul.md,service BattleClientPlayer)
    "BattleClientPlayerStopWatchBattle"            = "StopWatchBattle"
    "BattleClientPlayerSetAutoBattle"              = "SetAutoBattle"
    "BattleClientPlayerSurrenderBattle"            = "SurrenderBattle"
    "BattleClientPlayerNotifySurrenderVote"        = "NotifySurrenderVote"
    "BattleClientPlayerNotifySpectateState"        = "NotifySpectateState"
    "BattleClientPlayerNotifySpectateTurnResult"   = "NotifySpectateTurnResult"
    "BattleClientPlayerNotifySpectateEnd"          = "NotifySpectateEnd"

    # 匹配/切磋(proto/match/match_service.proto, service MatchService)
    "MatchServiceJoinQueue"                        = "JoinQueue"
    "MatchServiceCancelQueue"                      = "CancelQueue"
    "MatchServiceGetQueueStatus"                   = "GetQueueStatus"
    "MatchServiceGetNetworkProbePlan"              = "GetNetworkProbePlan"
    "MatchServiceCommitNetworkProbe"               = "CommitNetworkProbe"
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
    "ScenePetClientPlayerGetPetEntries"                      = "GetPetEntries"
    "ScenePetClientPlayerPreviewPetEntries"                  = "PreviewPetEntries"
    "ScenePetClientPlayerSavePetEntries"                     = "SavePetEntries"
    "ScenePetClientPlayerSwitchPetEntryPreset"               = "SwitchPetEntryPreset"
    "ScenePetClientPlayerAscendPetTier"                      = "AscendPetTier"
    "ScenePetClientPlayerSummonPet"                          = "SummonPet"
    "ScenePetClientPlayerRecallPet"                          = "RecallPet"
    "ScenePetClientPlayerAllocatePetPoints"                  = "AllocatePetPoints"
    "ScenePetClientPlayerResetPetPoints"                     = "ResetPetPoints"
    "ScenePetClientPlayerAutoAllocatePetPoints"              = "AutoAllocatePetPoints"
    "ScenePetClientPlayerRenamePet"                          = "RenamePet"
    "ScenePetClientPlayerNotifyPetListChanged"               = "NotifyPetListChanged"
    "ScenePetClientPlayerGmGrantPet"                         = "GmGrantPet"
    "ScenePetClientPlayerGetPetShop"                         = "GetPetShop"
    "ScenePetClientPlayerBuyPet"                             = "BuyPet"
    # 背包、任务、活动窗口（正式协议注册表分配编号）。
    "SceneBagClientPlayerGetBag"                            = "GetBag"
    "SceneBagClientPlayerSortBag"                           = "SortBag"
    "SceneBagClientPlayerEquipItem"                         = "EquipItem"
    "SceneBagClientPlayerUnequipItem"                       = "UnequipItem"
    # 装备强化 / 镶嵌 / 取下宝石(服务端 docs/design/equipment-enhance-inlay.md §3.3)。号由服务端 proto-gen 发,
    # 发号之前 message_id.txt 里没有这三行:脚本只出一条 Warning、常量不生成,PlayerFeaturesClient.cs 就是预期的 CS0117。
    "SceneBagClientPlayerEnhanceEquip"                      = "EnhanceEquip"
    "SceneBagClientPlayerInlayGem"                          = "InlayGem"
    "SceneBagClientPlayerRemoveGem"                         = "RemoveGem"
    "SceneMissionClientPlayerGetMissionList"                = "GetMissionList"
    "SceneActivityClientPlayerGetActivityList"              = "GetActivityList"

    # 客户端直连 battle 节点(turn-based-battle-server.md §18):落点分配推送 + 丢票补签
    "BattleClientPlayerNotifyBattleAssigned"       = "NotifyBattleAssigned"
    "BattleClientPlayerUpdateBattlePrep"           = "UpdateBattlePrep"
    "BattleClientPlayerNotifyBattlePrepUpdate"     = "NotifyBattlePrepUpdate"
    "BattleClientPlayerSendBattlePing"             = "SendBattlePing"
    "BattleClientPlayerNotifyBattlePing"           = "NotifyBattlePing"
    "MatchServiceRequestBattleTicket"              = "RequestBattleTicket"

    # 组队(proto/team/team.proto, service ClientPlayerTeam;docs/design/team-system.md §H.1)。
    # 后三条是 S→C 推送:服务端写成返回 Empty 的 rpc 只为拿消息号,客户端用 GameClient.OnNotify 注册。
    # 数值不写死在这里,由 proto/message_id.txt 现场读取(当前 201..215)。
    # 神秘商人方法必须由服务端分配消息号；尚未合入时生成0，由客户端明确显示未开放。
    "SceneMysteryMerchantClientPlayerGetMysteryMerchantInfo"  = "GetMysteryMerchantInfo"
    "SceneMysteryMerchantClientPlayerBuyMysteryMerchantGoods" = "BuyMysteryMerchantGoods"
    "SceneMysteryMerchantClientPlayerNotifyMysteryMerchant"   = "NotifyMysteryMerchant"
    # 挂机月卡(proto/scene/player_afk_pass.proto;服务端 docs/design/afk-monthly-card.md §6)。GmSetAfkPass 只给联调与 robot,客户端不发。
    # 服务端尚未分配时生成 0,由 AfkPassMessageIds.IsSupported 显示未开放。
    "SceneAfkPassClientPlayerGetAfkPass"           = "GetAfkPass"
    "SceneAfkPassClientPlayerBuyAfkPass"           = "BuyAfkPass"
    "SceneAfkPassClientPlayerSetAfkPassSettings"   = "SetAfkPassSettings"
    "SceneAfkPassClientPlayerAckHostedRecap"       = "AckHostedRecap"
    # 角色形象切换与 1 小时免费试用(proto/scene/player_appearance.proto;docs/design/character-appearance-switch.md)。
    "SceneAppearanceClientPlayerGetAppearanceInfo"            = "GetAppearanceInfo"
    "SceneAppearanceClientPlayerSwitchAppearance"             = "SwitchAppearance"
    "SceneAppearanceClientPlayerStartAppearanceTrial"         = "StartAppearanceTrial"
    "SceneAppearanceClientPlayerCancelAppearanceTrial"        = "CancelAppearanceTrial"
    "SceneAppearanceClientPlayerNotifyActorAppearanceChanged" = "NotifyActorAppearanceChanged"
    "SceneAppearanceClientPlayerNotifyAppearanceState"        = "NotifyAppearanceState"
    "SceneAppearanceClientPlayerEquipPetCosmetic"             = "EquipPetCosmetic"
    "SceneAppearanceClientPlayerEquipSkillVfx"                = "EquipSkillVfx"
    # 赛季技能与单槽赛季印（proto/scene/player_season.proto;docs/design/season-skills-and-seal-design.md）。
    "SceneSeasonClientPlayerGetSeasonState"                   = "GetSeasonState"
    "SceneSeasonClientPlayerEquipSeasonSeal"                  = "EquipSeasonSeal"
    "SceneSeasonClientPlayerCatchupSeasonSeal"                = "CatchupSeasonSeal"
    # 官方灵石百宝阁 V1（proto/scene/player_spirit_shop.proto;docs/design/spirit-shop-and-starter-packs-v1.md）。
    "SceneSpiritShopClientPlayerGetSpiritShopInfo"            = "GetSpiritShopInfo"
    "SceneSpiritShopClientPlayerBuySpiritShopGoods"           = "BuySpiritShopGoods"
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
    "ClientPlayerTeamSetTeamFormation"             = "SetTeamFormation"
    "ClientPlayerTeamRecallTeamMembers"            = "RecallTeamMembers"
}

$optionalUnsupportedMethods = @(
    'SceneMysteryMerchantClientPlayerGetMysteryMerchantInfo',
    'SceneMysteryMerchantClientPlayerBuyMysteryMerchantGoods',
    'SceneMysteryMerchantClientPlayerNotifyMysteryMerchant',
    'SceneAfkPassClientPlayerGetAfkPass',
    'SceneAfkPassClientPlayerBuyAfkPass',
    'SceneAfkPassClientPlayerSetAfkPassSettings',
    'SceneAfkPassClientPlayerAckHostedRecap',
    'ClientPlayerTeamSetTeamFormation',
    'ClientPlayerTeamRecallTeamMembers',
    'SceneAppearanceClientPlayerGetAppearanceInfo',
    'SceneAppearanceClientPlayerSwitchAppearance',
    'SceneAppearanceClientPlayerStartAppearanceTrial',
    'SceneAppearanceClientPlayerCancelAppearanceTrial',
    'SceneAppearanceClientPlayerNotifyActorAppearanceChanged',
    'SceneAppearanceClientPlayerNotifyAppearanceState',
    'SceneAppearanceClientPlayerEquipPetCosmetic',
    'SceneAppearanceClientPlayerEquipSkillVfx',
    'SceneSeasonClientPlayerGetSeasonState',
    'SceneSeasonClientPlayerEquipSeasonSeal',
    'SceneSeasonClientPlayerCatchupSeasonSeal',
    'SceneSpiritShopClientPlayerGetSpiritShopInfo',
    'SceneSpiritShopClientPlayerBuySpiritShopGoods'
)
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
    } elseif ($k -in $optionalUnsupportedMethods) {
        $emit.Add([pscustomobject]@{ Const = $whitelist[$k]; Id = 0; Method = "$k (unsupported)" })
        $missing += $k
    } else {
        $missing += $k
    }
}
$emit = $emit | Sort-Object Const

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

# 故障分类只取服务器导表器的Tip.xlsx派生事实；客户端不维护第二份手工故障表。
$faultSource = Join-Path $ProtoRoot 'go/shared/generated/tip/faults.go'
if (-not (Test-Path -LiteralPath $faultSource)) { throw '缺少导表器的tip故障分类，不能生成交易恢复契约。' }
$faultIds = [regex]::Matches((Get-Content -LiteralPath $faultSource -Raw), '\{Code:\s*(\d+),') |
    ForEach-Object { [uint32]$_.Groups[1].Value } | Sort-Object -Unique
if ($faultIds.Count -eq 0) { throw 'tip故障分类为空，停止生成。' }
$faultLines = $faultIds | ForEach-Object { "            ${_}u," }
$faultFile = Join-Path $repoRoot 'Assets/Scripts/Net/Generated/TipFaults.cs'
$faultCode = @"
// <auto-generated>来自服务器Tip.xlsx导表器的faults.go；运行tools/gen_messageids.ps1，不要手改。</auto-generated>
namespace MmorpgClient.Net.Generated
{
    public static class TipFaults
    {
        private static readonly System.Collections.Generic.HashSet<uint> Codes = new()
        {
$($faultLines -join "`n")
        };
        public static bool IsFault(uint code) => Codes.Contains(code);
    }
}
"@
Set-Content -LiteralPath $faultFile -Value ($faultCode.Replace("`r`n", "`n")) -Encoding UTF8
