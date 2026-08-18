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
    "proto/db/proto_option.proto",
    "proto/login/login.proto",
    "proto/scene/scene_info.proto",
    "proto/scene/player_scene.proto",
    "proto/scene/player_skill.proto",
    "proto/scene/player_movement.proto",
    "proto/scene/player_lifecycle.proto",
    "proto/scene/client_player_common.proto",

    # 回合制战斗 + 匹配/切磋(docs/design/turn-based-battle-server.md 一期客户端协议)
    "proto/battle/battle_data.proto",
    "proto/battle/player_battle.proto",
    "proto/match/match_service.proto"
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
