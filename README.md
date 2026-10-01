# MMORPG Unity Client

Unity 6000.6.0f1 client for the MMORPG server (server repo:
[luyuancpp/mmorpg](https://github.com/luyuancpp/mmorpg)). This repo is a
standalone sibling of the server repo (`<workspace>/mmorpg-client` next to
`<workspace>/mmorpg`); it is no longer a submodule at `client/unity/`.

## Quick start

1. **Open in Unity**: use Unity `6000.6.0f1`, then select this repo's root.
2. **Generate proto C# stubs** (required after first checkout and after
   any `.proto` change in the server repo):

   ```pwsh
   pwsh -File tools/gen_proto.ps1
   pwsh -File tools/gen_messageids.ps1
   # any other layout: point both scripts at the server repo root
   pwsh -File tools/gen_proto.ps1 -ProtoRoot D:/luyuan/wuxingqitan/mmorpg
   pwsh -File tools/gen_messageids.ps1 -ProtoRoot D:/luyuan/wuxingqitan/mmorpg
   ```

   Both scripts default `-ProtoRoot` to the sibling server repo
   (`tools/../../mmorpg`). The pre-split default `../../..` pointed into the
   old submodule superproject and would now resolve to the workspace's
   parent directory.
   `gen_proto.ps1` calls `protoc` against the server repo's `proto/`
   tree (plus the tip enums under `generated/code/proto/tip/`, so run it
   only after the server's table exporter) and writes to
   `Assets/Scripts/Proto/Generated/`.
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
* **战斗直连(第二条连接,战斗的唯一通路)**:回合制战斗的流量不经 gate —— gate 两种
  路由模式都拒绝战斗消息(只回一条 id=0 的 `TipToClient(kServiceUnavailable)`),没有
  gate 中继可回落。开局 / 观战接入时 battle 节点推 `NotifyBattleAssigned{ host, port,
  token_payload, token_signature, expire_at_ms, role }`(还没有活直连时经大厅),
  `BattleDirectLink` 据此连 battle 节点客户端面,首包 `BattleTokenVerifyRequest{ payload,
  signature }`(两字段原样透传)。握手通过后 `SubmitBattleAction / GetBattleState /
  StopWatchBattle / SetAutoBattle` 与本人的战斗帧只走这条连接;大厅只承载
  `NotifyBattleAssigned / NotifyBattleStart / NotifyBattleReconnect` 与 scene 结算后推的
  `NotifyBattleEnd`(两条连接都挂处理器,按 battle_id 幂等)。分流在
  `DirectRoutingBattleTransport`,直连状态经窄接口 `IBattleChannel` 交给
  `BattleClient` / `SpectateClient`。
  - **未就绪**(开局握手中 / 断线恢复中):四条战斗 RPC 本地立即失败(`link: not ready`),
    单向消息丢弃,**绝不改走大厅**;`SubmitAction` 返回 false,出手按钮置灰;自动战斗开关
    只记意愿,就绪后补发。
  - **就绪时**:参战方自动 `GetBattleState` 补拉(开局到握手之间、断线期间的战斗帧不会
    经大厅补发);观众由服务端在握手成功时推一份 `SpectateStateS2C` 快照作为首帧。
  - **恢复**:意外断开先用同票重连 1 次,再经大厅 `MatchService.RequestBattleTicket`
    补签,每局最多 3 次,自动补签按 1s / 2s / 4s 退避(±20% 抖动,以票据 `expire_at_ms`
    剩余时间封顶);大厅重连提示、开局自愈与 UI「重新连接」立即补签并重置预算。
    补签回 `common_error.kInvalidParameter` = 战斗已结束,收敛回空闲;预算用完 = 连不上,
    战斗屏常驻「无法连接战斗服务器」横幅 +「重新连接」按钮,本局由服务端回合超时替玩家出手。
    直连就绪补拉回来的权威状态直接刷到已开的战斗屏;就绪补拉遇传输错误而同一局直连仍就绪时
    (链路没断就不会再有就绪事件),按 1s / 2s / 4s(±20%)自动重拉,每轮最多 3 次,
    换局 / 收尾 / 断线 / 下一次就绪 / 手动补拉时作废计划。
  - **主城入口四档**(战斗屏没开时,`BattleUiRoot.DecideEntryMode` 按 `BattleClient` 实时状态决定;
    大厅重连后权威状态没拉到前相位停在空闲、战斗屏开不了,横幅无处显示,所以借用这个入口):
    「战斗」= 没有进行中的对局,点开排队面板;「连接战斗中…」(不可点)= 仍有对局、直连正在补签 / 建连
    (大厅重连后到首次就绪前,或从入口点了重连、结果未回);「重新连接战斗」= 直连已判定连不上,
    点了立即补签;「返回战斗」= 直连已就绪而权威状态还没拉到(自动重拉等待中或已用完),点了立即补拉。
  - 大厅断线仍会拆直连,重新登录后由 `NotifyBattleReconnect` 补签重建。
  线协议与大厅连接完全一致,复用 `GateTcpClient` / `MuduoCodec`。契约与服务端实现见服务端仓
  `docs/design/turn-based-battle-server.md` §18 与 §22(D66–D74)。
  实机验收:`pwsh -File tools/run_crosszone_pair.ps1`,判据含每侧 `direct_turns == turns`。

## Production checklist

The repo currently ships the client foundation; before going live you
still need to address:

| Area                | Status                                              |
| ------------------- | --------------------------------------------------- |
| Login flow          | done (HTTP gateway + token verify + Login + Enter)  |
| Scene rendering     | playable Tianyong map; actor models remain placeholders |
| Skill FX            | placeholder ring/beam/flash in `SkillFx`            |
| Reconnect           | exponential backoff in `GameDemo`                   |
| Battle direct link  | wired as the only battle path (`BattleDirectLink`, ticket handshake, no gate fallback, reissue with backoff, reconnect banner); needs a live-stack `tools/run_crosszone_pair.ps1` smoke (`direct_turns == turns`) against the server repo's battle node |
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

1. Pull the latest server repo (sibling `../mmorpg`, or pass `-ProtoRoot`)
   and rerun `tools/gen_messageids.ps1` and `tools/gen_proto.ps1`.
2. Bump version in `ProjectSettings/ProjectSettings.asset`.
3. Push to `main`. The CI matrix builds Standalone, WebGL, and Android.
4. 在服务端仓(同级 `../mmorpg`)的 `PROGRESS.md` 或对应设计文档里记下本次客户端提交的
   sha,供服务端对照所期望的客户端版本。两仓相互独立,没有 submodule 指针可 bump。
