<#
.SYNOPSIS
    Generate C# protobuf classes from the server mmorpg repo's proto tree.

.DESCRIPTION
    Invokes protoc against the proto/ tree of the server repository (a sibling
    of this client repo by default, see -ProtoRoot) and emits .cs files into
    Assets/Scripts/Proto/Generated/. Run this whenever a .proto changes.

.PARAMETER ProtoRoot
    Path to the server mmorpg repo root. Defaults to ../../mmorpg relative to
    this script, i.e. the sibling-repo layout (<workspace>/mmorpg-client next to
    <workspace>/mmorpg). The old default ../../.. assumed the retired submodule
    layout (client/unity/ inside the server repo) and now resolves to the
    workspace's parent directory; pass -ProtoRoot explicitly for any other layout.

.PARAMETER Protoc
    Path to a protoc executable. Defaults to looking up protoc on PATH.

.EXAMPLE
    pwsh -File tools/gen_proto.ps1
    pwsh -File tools/gen_proto.ps1 -ProtoRoot D:/luyuan/wuxingqitan/mmorpg
#>
param(
    # 独立同级仓布局:本仓 <工作区>/mmorpg-client,服务端仓 <工作区>/mmorpg(不再是 submodule)
    [string]$ProtoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../mmorpg')),
    [string]$Protoc = "protoc"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$outDir   = Join-Path $repoRoot "Assets/Scripts/Proto/Generated"

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# .proto files the client needs (request/response/notify on the gate channel).
# protoc resolves transitive imports via --proto_path automatically.
$files = @(
    "proto/chat/chat.proto",
    "proto/guild/guild.proto",
    "generated/code/proto/tip/guild_error_tip.proto",
    "proto/common/base/common.proto",
    "proto/common/base/empty.proto",
    "proto/common/base/message.proto",
    "proto/common/base/rpc_message.proto",
    "proto/common/base/session.proto",
    "proto/common/base/tip.proto",
    "proto/common/base/user_accounts.proto",
    "proto/common/component/actor_comp.proto",
    "proto/common/component/base_comp.proto",
    "proto/common/component/player_skill_comp.proto",
    "proto/common/component/actor_attribute_state_comp.proto",
    "proto/common/component/player_attribute_comp.proto",
    "proto/common/component/currency_comp.proto",
    "proto/db/proto_option.proto",
    "proto/login/login.proto",
    # 登录域的 tip 码(含角色名 kRoleNameInvalid / kRoleNameTaken / kRoleNameSensitive 与建角可重试失败)。
    # Assets/Scripts/Game/Role/RoleNameRules.cs 按 login_error.K* 枚举映射文案,缺了这一行它就引用不存在的类型(CS0246);
    # 与 guild / trade 同形:由服务端导表器生成,客户端不手抄数字(服务端 docs/design/guild-phase2/03-names.md §3.22)。
    "generated/code/proto/tip/login_error_tip.proto",
    "proto/scene/scene_info.proto",
    "proto/scene/player_scene.proto",
    "proto/scene/player_skill.proto",
    # 技能域 tip(四职业技能书 / 修习的拒绝原因,服务端 docs/design/class-skills.md §4),客户端按枚举映射文案。
    "generated/code/proto/tip/skill_error_tip.proto",
    "proto/scene/player_movement.proto",
    "proto/scene/player_lifecycle.proto",
    "proto/scene/client_player_common.proto",
    # 场景域的 tip 码(含跨 zone 传送的 kZoneTravel* 与 kEnterSceneFailed)。
    # 客户端要按码分辨「传送被拒」和别的 tip,就必须有这份枚举 —— 缺了它只能显示裸编号,
    # 而 AGENTS §7.5 禁止在客户端手抄 tip 数字(号由导表器发,下次导表就可能对不上)。
    "generated/code/proto/tip/scene_error_tip.proto",
    # 跨服域 tip,目前只用 kSceneTransferInProgress —— 服务端已挂归属交接(PlayerFrozenComp /
    # PlayerTravelHandoffComp,跨区传送与同 zone 跨节点换图都会挂)时,TravelToZone 的同步拒绝码
    # (PlayerLifecycleSystem::RequestZoneTravel)。收进来是为了让 DescribeTravelTip 按枚举名给文案,
    # 不手写 13000(AGENTS §7.5)。
    "generated/code/proto/tip/cross_server_error_tip.proto",

    # 角色属性加点(docs/design/player-attribute-allocation.md)
    "proto/scene/player_attribute.proto",

    # 宝宝(宠物)系统(docs/design/player-pet.md)
    "proto/common/component/player_pet_comp.proto",
    "proto/scene/player_pet.proto",
    # 背包、任务、活动窗口：权威快照与显式整理背包。
    "proto/scene/player_bag.proto",
    "proto/scene/player_mission.proto",
    "proto/scene/player_activity.proto",
    # 装备穿脱错误码由服务端 Tip.xlsx 导表生成，客户端不手写码值。
    "generated/code/proto/tip/equip_error_tip.proto",

    # 回合制战斗 + 匹配/切磋(docs/design/turn-based-battle-server.md 一期客户端协议)
    "proto/battle/battle_data.proto",
    "proto/battle/player_battle.proto",
    "proto/match/match_service.proto",

    # 货币(docs/design/currency.md)。注意:handler 生成器(gen_messageids.ps1 + protogen)
    # 是按服务端 proto 全量出 handler 的,这份清单漏配哪个 proto,就会多出一批引用不存在
    # 类型的 handler(CS0246)。服务端新增 proto 时,这里必须同步加,否则下次重生成必红。
    "proto/scene/player_currency.proto",

    # 聚宝斋 P1(docs/design/jubaozhai-market.md,service ClientPlayerJubaozhai,namespace Trade)。
    # 只列客户端协议文件:proto/trade/trade_admin.proto(内部 SeedListing)与 trade_table.proto(建表 schema)
    # 刻意不进客户端包。trade_error / common_error 两份 tip 由服务端导表器生成,客户端按枚举映射文案。
    "proto/trade/jubaozhai.proto",
    "proto/trade/commerce.proto",
    "proto/mail/mail.proto",
    "proto/rank/rank.proto",
    "proto/common/base/service_call_auth.proto",
    "proto/match/match_network.proto",
    "generated/code/proto/tip/mail_error_tip.proto",
    "generated/code/proto/tip/rank_error_tip.proto",
    "generated/code/proto/tip/trade_error_tip.proto",
    "generated/code/proto/tip/common_error_tip.proto",

    # 组队(docs/design/team-system.md §H.1,service ClientPlayerTeam,package teampb → namespace Teampb)。
    # 这两行是「客户端合并门禁」:服务端默认 proto_gen.yaml 里已有 team 块且 enable_unity_client=true,
    # 任何人用默认配置跑 dev.bat proto / dev.bat gen 都会往 Assets/Scripts/Net/Generated/Handlers 写
    # 15 个 ClientPlayerTeam*Handler.cs;少了这两行,那些 handler 引用的 Teampb.* 不存在 → CS0246。
    "proto/team/team.proto",
    "generated/code/proto/tip/team_error_tip.proto",

    # 好友客户端 handler 引用 Friendpb.*，生成清单必须包含对应协议。
    # friend_table.proto 刻意不收:它是纯服务端存储结构,客户端只看 friend.proto。
    # friend_error 与上面 trade / team 同形:由服务端导表器生成,客户端按枚举映射文案、不手抄数字。
    # 本行生成的枚举要含 15007–15009(FriendBlocked / FriendBlockListFull / FriendTargetInboxFull),
    # 所以只能在服务端导表器跑完之后再跑本脚本。
    "proto/friend/friend.proto",
    "generated/code/proto/tip/friend_error_tip.proto",
    # 保留神秘商人已入库客户端；服务端尚未合入时仅跳过这两份协议的重生成。
    "proto/scene/player_mystery_merchant.proto",
    "generated/code/proto/tip/mystery_merchant_error_tip.proto"
)

$merchantProtoFiles = @(
    'proto/scene/player_mystery_merchant.proto',
    'generated/code/proto/tip/mystery_merchant_error_tip.proto'
)
$merchantPresent = @($merchantProtoFiles | Where-Object { Test-Path -LiteralPath (Join-Path $ProtoRoot $_) })
if ($merchantPresent.Count -eq 0) {
    $files = @($files | Where-Object { $_ -notin $merchantProtoFiles })
    Write-Warning '[gen_proto] 当前服务端没有神秘商人协议；保留客户端已入库类型，消息号由gen_messageids.ps1生成未支持的0。'
} elseif ($merchantPresent.Count -ne $merchantProtoFiles.Count) {
    throw '神秘商人协议与导表提示码不完整；请先完成服务端生成，不能发布部分契约。'
}
# 组队恢复与补位已合为同一份正式契约；无论业务别名文件是否存在，都必须保护恢复字段及正式编号。
$teamSource = Get-Content -LiteralPath (Join-Path $ProtoRoot 'proto/team/team.proto') -Raw
function Get-TeamMessageBody([string]$name) {
    $pattern = '(?s)\bmessage\s+' + [regex]::Escape($name) + '\s*\{(.*?)\}'
    $match = [regex]::Match($teamSource, $pattern)
    if (-not $match.Success) { throw "组队协议兼容预检：缺少消息 $name" }
    return $match.Groups[1].Value
}
$teamContracts = @(
    @{ Message = 'GetMyTeamRequest'; Type = 'string'; Field = 'match_request_id'; Number = 2 }
    @{ Message = 'GetMyTeamRequest'; Type = 'bool'; Field = 'regroup'; Number = 3 }
    @{ Message = 'StartTeamMatchRequest'; Type = 'string'; Field = 'request_id'; Number = 3 }
    @{ Message = 'StartTeamMatchRequest'; Type = 'uint32'; Field = 'target_team_size'; Number = 4 }
    @{ Message = 'TeamResponse'; Type = 'TeamMatchIntent'; Field = 'match_intent'; Number = 3 }
    @{ Message = 'TeamMatchIntent'; Type = 'string'; Field = 'request_id'; Number = 1 }
    @{ Message = 'TeamMatchIntent'; Type = 'uint64'; Field = 'battle_id'; Number = 2 }
    @{ Message = 'TeamMatchIntent'; Type = 'uint32'; Field = 'state'; Number = 3 }
    @{ Message = 'TeamView'; Type = 'uint32'; Field = 'match_target_size'; Number = 13 }
    @{ Message = 'TeamView'; Type = 'uint64'; Field = 'match_queued_at_ms'; Number = 14 }
    @{ Message = 'TeamView'; Type = 'uint32'; Field = 'match_battle_config_id'; Number = 15 }
    @{ Message = 'TeamView'; Type = 'bool'; Field = 'match_fill_supported'; Number = 16 }
)
foreach ($contract in $teamContracts) {
    $body = Get-TeamMessageBody $contract.Message
    $pattern = '(?m)^\s*' + [regex]::Escape($contract.Type) + '\s+' + [regex]::Escape($contract.Field) +
        '\s*=\s*' + $contract.Number + '\s*;'
    if ($body -notmatch $pattern) {
        throw "组队协议兼容预检：缺少正式契约 $($contract.Message).$($contract.Field) = $($contract.Number) ($($contract.Type))；不能覆盖恢复或补位接口。"
    }
}
$teamBridge = Join-Path $repoRoot 'Assets/Scripts/Proto/TeamBackfillProtocol.cs'
if (Test-Path -LiteralPath $teamBridge) {
    $bridgeSource = Get-Content -LiteralPath $teamBridge -Raw
    $partialPattern = '(?m)^\s*(?:(?:public|internal|sealed|abstract|static)\s+)*partial\s+class\s+(?:GetMyTeamRequest|StartTeamMatchRequest|TeamView)\b'
    if ($bridgeSource -match $partialPattern) {
        throw '组队协议兼容预检：仍存在旧补位桥接partial成员；请移除桥接并使用正式生成字段，静态TeamBackfillProtocol别名可保留。'
    }
}

Push-Location $ProtoRoot
try {
    Write-Host "[gen_proto] proto root: $ProtoRoot"
    Write-Host "[gen_proto] output dir: $outDir"

    $args = @("--proto_path=$ProtoRoot", "--csharp_out=$outDir") + $files
    & $Protoc @args
    if ($LASTEXITCODE -ne 0) { throw "protoc exited with $LASTEXITCODE" }

    Write-Host "[gen_proto] done. Generated files:"
    Get-ChildItem $outDir -Filter *.cs | ForEach-Object { Write-Host "  $($_.Name)" }
}
finally { Pop-Location }
