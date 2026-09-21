<#
.SYNOPSIS
    Generate C# protobuf classes from the parent mmorpg repo's proto tree.

.DESCRIPTION
    Invokes protoc against the proto/ tree of the parent repository (resolved
    relative to this client repo) and emits .cs files into
    Assets/Scripts/Proto/Generated/. Run this whenever a .proto changes.

.PARAMETER ProtoRoot
    Path to the parent mmorpg repo root. Defaults to ../../ (the natural
    layout when this client is consumed as a git submodule at
    client/unity/ inside the mmorpg superproject).

.PARAMETER Protoc
    Path to a protoc executable. Defaults to looking up protoc on PATH.

.EXAMPLE
    pwsh -File tools/gen_proto.ps1
    pwsh -File tools/gen_proto.ps1 -ProtoRoot F:/work/mmorpg
#>
param(
    [string]$ProtoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path,
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
    "proto/scene/scene_info.proto",
    "proto/scene/player_scene.proto",
    "proto/scene/player_skill.proto",
    "proto/scene/player_movement.proto",
    "proto/scene/player_lifecycle.proto",
    "proto/scene/client_player_common.proto",
    # 场景域的 tip 码(含跨 zone 传送的 kZoneTravel* 与 kEnterSceneFailed)。
    # 客户端要按码分辨「传送被拒」和别的 tip,就必须有这份枚举 —— 缺了它只能显示裸编号,
    # 而 AGENTS §7.5 禁止在客户端手抄 tip 数字(号由导表器发,下次导表就可能对不上)。
    "generated/code/proto/tip/scene_error_tip.proto",

    # 角色属性加点(docs/design/player-attribute-allocation.md)
    "proto/scene/player_attribute.proto",

    # 宝宝(宠物)系统(docs/design/player-pet.md)
    "proto/common/component/player_pet_comp.proto",
    "proto/scene/player_pet.proto",
    # 背包、任务、活动窗口：权威快照与显式整理背包。
    "proto/scene/player_bag.proto",
    "proto/scene/player_mission.proto",
    "proto/scene/player_activity.proto",

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
    "generated/code/proto/tip/friend_error_tip.proto"
)

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
