# MMORPG Unity Client

Unity 6000.5.8f1 client for the MMORPG server (parent repo:
[luyuancpp/mmorpg](https://github.com/luyuancpp/mmorpg)). This repo is
mounted as a git submodule at `client/unity/` inside the superproject.

## Quick start

1. **Open in Unity**: use Unity `6000.5.8f1`, then select this repo's root.
2. **Generate proto C# stubs** (required after first checkout and after
   any `.proto` change in the parent repo):

   ```pwsh
   pwsh -File tools/gen_proto.ps1
   pwsh -File tools/gen_messageids.ps1
   ```

   `gen_proto.ps1` calls `protoc` against the parent repo's `proto/`
   tree and writes to `Assets/Scripts/Proto/Generated/`.
   `gen_messageids.ps1` regenerates `Assets/Scripts/Net/MessageIds.cs`
   from the server's authoritative `proto/message_id.txt`.

3. **Generate standard scenes and prefabs**: after scripts compile, run
   `MMORPG > World > Tianyong > Rebuild standard test content` once. It creates
   `AppRoot.prefab`, `Bootstrap.unity`, the offline `TianyongSandbox.unity`, the
   Tianyong config/materials and its debug-player prefab, then registers both
   scenes in Build Settings. See `Docs/TianyongMap.md` for PlayMode controls and
   batch commands.

## Architecture

```
Bootstrap.unity
  +-- AppRoot.prefab / AppBootstrap
       +-- GameClient
       +-- GatewayHttpClient   (HTTP -> Java gateway: server-list, assign-gate)
       +-- GateTcpClient       (TCP  -> C++ Gate node, MuduoCodec framing)
       +-- ActorWorld          (entity_id -> GameObject view cache)
       +-- SkillFx             (cast ring / beam / hit flash primitives)
       +-- TianyongMapRuntime  (chunked world, navigation and themes)
```

* **Wire format** is muduo's `ProtobufCodec`:
  `[len:i32 BE][nameLen:i32 BE][type_name\0][body][adler32:i32 BE]`,
  with adler-32 covering `[nameLen .. body]`.
* **RPC envelope**: C2S = `ClientRequest{ id, message_id, body }`,
  S2C reply/notify = `MessageContent{ id, message_id, serialized_message,
  error_message }`. Replies match by `id`; notifies have `id == 0` and
  dispatch by `message_id`.
* **Token verify** is the first protobuf frame after TCP connect:
  `ClientTokenVerifyRequest{ payload, signature }` from the gateway's
  `assign-gate` response. Server replies `ClientTokenVerifyResponse{ success }`.
* **战斗直连(第二条连接)**:回合制战斗的流量不经 gate。开局 / 观战接入时
  battle 节点经大厅推 `NotifyBattleAssigned{ host, port, token_payload,
  token_signature, expire_at_ms, role }`,`BattleDirectLink` 据此连 battle 节点
  客户端面,首包 `BattleTokenVerifyRequest{ payload, signature }`(两字段原样透传),
  握手通过后 `SubmitBattleAction / GetBattleState / StopWatchBattle / SetAutoBattle`
  与本人的战斗 S2C 走这条连接,其余消息仍走大厅(`DirectRoutingBattleTransport`
  按消息号分流)。直连建不起来或中途断开时自动回落 gate 中继;意外断开先用同票
  重连 1 次,再经大厅 `MatchService.RequestBattleTicket` 补签 1 次。线协议与大厅
  连接完全一致,复用 `GateTcpClient` / `MuduoCodec`。契约与服务端实现见父仓
  `docs/design/turn-based-battle-server.md` §18。

## Production checklist

The repo currently ships the client foundation; before going live you
still need to address:

| Area                | Status                                              |
| ------------------- | --------------------------------------------------- |
| Login flow          | done (HTTP gateway + token verify + Login + Enter)  |
| Scene rendering     | playable Tianyong map; actor models remain placeholders |
| Skill FX            | placeholder ring/beam/flash in `SkillFx`            |
| Reconnect           | exponential backoff in `GameDemo`                   |
| Battle direct link  | wired (`BattleDirectLink`, ticket handshake, fallback to gate); needs a live-stack smoke against the parent repo's battle node |
| Refresh token       | wired (`MessageIds.RefreshToken=127`)               |
| Logging             | leveled file sink under `persistentDataPath/logs/`  |
| Settings            | PlayerPrefs (`ClientSettings`) for gateway/account  |
| **Movement**        | client movement wired; server nav validation pending |
| **Real assets**     | Addressables / animations / audio not yet wired     |
| **Localization**    | tip table loader not yet wired                      |
| **Secure storage**  | refresh token must NOT live in `PlayerPrefs`        |
| **Anti-cheat**      | encrypt/sign critical RPCs at the application layer |
| **Build pipeline**  | CI workflow present; needs `UNITY_LICENSE` secret   |

## Layout

```
Assets/
  Scenes/Bootstrap.unity              production entry scene (generated)
  Scenes/World/TianyongSandbox.unity  offline map test scene (generated)
  Prefabs/App/AppRoot.prefab           production root (generated)
  Resources/World/Tianyong/            config + source textures
  Plugins/Google.Protobuf.dll          vendored (netstandard2.0, 3.28.3)
  Scripts/
    Core/MmorpgLogger.cs               leveled console + file logger
    Core/ClientSettings.cs             PlayerPrefs settings
    Game/GameClient.cs                 high-level client facade
    Net/                               gateway HTTP, gate TCP, codec, ids
    Proto/Generated/                   protoc output (regenerated)
    UI/AppBootstrap.cs                 production uGUI/client entry
    World/ActorWorld.cs                entity_id -> GameObject cache
    World/SkillFx.cs                   placeholder skill FX
    World/Tianyong/                    playable Tianyong runtime
tools/
  gen_proto.ps1                        protoc invoker
  gen_messageids.ps1                   message_id.txt -> MessageIds.cs
.github/workflows/ci.yml               Unity build matrix + script lint
```

## Releasing

1. Pull the latest server `proto/message_id.txt` and rerun
   `tools/gen_messageids.ps1` and `tools/gen_proto.ps1`.
2. Bump version in `ProjectSettings/ProjectSettings.asset`.
3. Push to `main`. The CI matrix builds Standalone, WebGL, and Android.
4. Bump the submodule pointer in the parent `mmorpg` repo so the server
   side knows which client commit it expects.
