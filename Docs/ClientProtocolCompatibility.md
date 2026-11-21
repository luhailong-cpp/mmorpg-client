# 客户端协议分支兼容

2026-11-15：PVE 经验入账契约新增 `LevelComp.total_exp = 2` 与 `PlayerBattleResult.exp_gain = 18`（uint64）。前者是角色累计实得经验，后者是本局实际入账额；旧存档、旧战果缺字段时为 0。升级曲线未配置，累计经验不自动改等级。`ActorComp.cs` 与 `PlayerBattleResultComp.cs` 使用服务端正式 protoc 在独立目录生成后同步，没有手改生成类。

2026-10-19 的客户端同步同时保留本地恢复逻辑与远端神秘商人、归队、组队补位界面。服务端两套 Git 分支的新增协议尚未统一；本次没有改动服务端，没有猜测或分配新的消息号、字段号。

事实来源固定为两个服务端版本：

- `xuanming-server-mmo-cpp` 的 `origin/main`，提交 `fea7f5e9be55017597752bf7b4ec433bd3e12850`：`proto/team/team.proto`、`proto/scene/player_mystery_merchant.proto`、`proto/message_id.txt`。这是远端补位与神秘商人的协议来源，不代表已确认线上部署。
- `xuanming-server-mmo-cpp` 的本地提交 `85a457a55f6116f2715364bcf955fdd33137ae59`：`proto/team/team.proto`、`proto/mail/mail.proto`、`proto/rank/rank.proto`、`proto/message_id.txt`。组队开局恢复、邮件、排行榜、探针与交易采用这一套契约；Java 仓 `xuanming-server-mmo` 的 `game-proto/src/main/proto/proto/team/team.proto` 与 `game-proto/src/main/resources/contract/message_id.txt` 也使用这套恢复契约。

## 组队：同号、不同 wire type 的过渡桥接

| 消息字段 | 本地 CPP/Java 恢复契约 | CPP 远端补位契约 |
|---|---|---|
| `GetMyTeamRequest` 字段 2 | `string match_request_id`，wire type 2 | `bool regroup`，wire type 0 |
| `StartTeamMatchRequest` 字段 3 | `string request_id`，wire type 2 | `uint32 target_team_size`，wire type 0 |
| `TeamResponse` 字段 3 | `TeamMatchIntent match_intent` | 没有此字段 |
| `TeamView` 字段 13–16 | 没有这些字段 | 目标人数、入队时间、副本、补位能力 |

`.proto` 不能声明同号双字段。客户端继续使用本地 CPP/Java 恢复契约生成的 `Assets/Scripts/Proto/Generated/Team.cs`，由 `Assets/Scripts/Proto/TeamBackfillProtocol.cs` 的 partial 类型访问远端已有的 varint 字段。桥接读取和修改 `UnknownFieldSet`，只替换对应字段号、wire type 0 的值，保留同号的恢复 string、其他 wire type 和全部其他未知字段。原生成的序列化、解析、克隆和合并仍承担全部消息编码工作。补位枚举以 `TeamBackfillProtocol` 常量表示远端已有数值 2、15、16，未修改生成文件或发明新值。

服务端只解释自己认识的 tag，另一分支的不同 wire type 仍可作为未知字段保留。客户端只有收到 `match_fill_supported = true` 才允许发起补位；UI 与直接调用 API 都执行这一检查。开局请求同时携带原请求 UUID 和目标人数，持久化和重试保持同一份请求。已有正式 `MatchIntent` 时始终按其权威状态恢复，包括补位请求；没有 `MatchIntent` 的旧补位响应只在明确成功或权威队列核对队伍、目标人数、副本一致后解除本地记录。补位超时之后不会对忽略 UUID 的旧分支盲目重发。

`TeamProtocolCompatibilityTests` 覆盖双 wire type 的两个输入顺序、克隆、合并、清零、同号其他 wire type、未知字段保留和官方样例字节。同步时另外将上述两个真实 proto 分别用 protoc 生成隔离的验证类型，交叉解析并核对字节；这些验证别名类型只存在仓库外，不属于生产协议。

这是过渡桥接，不是新的服务端统一协议。正式统一 `.proto` 后，应先核对全部字段号和 wire type，再移除对应 partial 成员、恢复生成枚举引用并重生 `Team.cs`。若正式 schema 已包含这些属性而 partial 还存在，应立即停止生成，避免重复成员或误删恢复 API。生成器 preflight 必须检查所选 proto 仍包含字段 2/3 的两个恢复 string、`TeamResponse.match_intent = 3`，且尚未直接声明桥接的六个 varint 属性。

## 神秘商人：保留功能，默认未开放

CPP 远端契约的查询、购买、公告消息号为 264、265、266；本地 CPP/Java 契约的同号分别是邮件推送、网络探针提交、聚宝斋市场订单列表。两套消息号不能在同一连接上同时生效。

本次默认按本地 CPP/Java 契约生成：`GetMysteryMerchantInfo`、`BuyMysteryMerchantGoods`、`NotifyMysteryMerchant` 保留公开常量，未分配时值为 0。`MysteryMerchantClient` 只有在三项消息号全部非 0 且互不重复时才注册公告、发送查询或购买；默认角标显示「神秘商人 未开放」，窗口说明「当前服务器未开放神秘商人」。全部远端模型、窗口、资源、协议类型和原行为测试保留；测试显式注入独立假号，绝不把假号作为生产映射。

两套服务端分支统一并分配无冲突消息号后，从正式事实源重跑 `tools/gen_proto.ps1` 和 `tools/gen_messageids.ps1` 即可启用现有功能。生成清单和 whitelist 继续保留三个方法；当前源仓完全没有神秘商人 proto 时保留已入库类型，部分文件缺失时拒绝生成。

## 邮件和排行榜

采用本地 CPP/Java 的现行 schema 与消息号，保留客户端双方的行为实现。远端 `GetRankPage = 275` 已与本地探针方案冲突，必须映射到当前正式 `ClientPlayerRankGetRankPage` 的消息号；不能继续沿用旧生成值。邮件附件/清理等业务字段同样以本地现行 proto 为准，界面和回调适配该事实源。

2026-11-12：服务端 `proto/rank/rank.proto` 新增枚举 `RankBoardType`，登记评分榜的 `board_type`（1 = 1V1、2 = 5V5、3 = PVP_TEAM，线上取值不变）。`RankClient` 与论剑榜的模式页签都用生成常量选榜，不写数字；拉到这一版服务端后先从服务端仓重跑 `tools/gen_proto.ps1`，否则 `Rank.cs` 里没有这个枚举，编译报 CS0246。

## 组队写请求恢复（2026-10-21）

普通组队写请求在发送前保存 UUID 和完整正文。故障回包、未知传输结局或重建后加载的原件继续保留，只能按同一请求恢复；其他写不能覆盖它。只有此前没有未知尝试、且首次发送被 Gate 按原请求明确限流时，才清理本次未执行意图并允许新的写。旧未知请求重试遇到限流，只能证明这一次没有执行，不能抹掉之前的未知结果。清理本机原件失败时保留保护状态。

## Gate 心跳与短重连（2026-11-06）

本节以 `../xuanming-server-mmo-cpp/proto/common/base/message.proto` 和该仓 `docs/design/gate-client-heartbeat.md` 为事实源，协议通过正式生成器同步。新版 gate 验签成功即开始 30 秒读空闲判定，未验签连接最多保留 10 秒；客户端和 gate 必须同批升级。

- `GateTcpClient` 在网络读线程处理成功验签通告，立即启动独立心跳线程。`GateHeartbeat` 用单调时钟按通告间隔发包，不依赖渲染、`Time.timeScale`、`Poll` 或业务流量；默认间隔 10000ms、超时 30000ms。间隔为 0 不启动心跳，重复验签不重复启动。
- 任何入站字节都刷新判活时间；`ClientHeartbeatResponse` 由网络层认领，匹配的最新序号用于 RTT，不进入业务队列。超时关闭 socket，读线程统一派发一次断线；EOF、显式关闭、换连接都会结束心跳线程。战斗直连使用另一种验签消息，不会误启 gate 心跳。
- `GameClient.Reconnect.cs` 对游戏内意外断线发起一次总预算 20 秒的恢复：重新 AssignGate 领取新票据，使用已有 access token 登录，沿用原角色进入，不保存密码、不重新选角或建角。需要时先用 refresh token 换新令牌。失败、排队或超时回到原选服流程；显式登出、顶号和切服不自动抢回会话，旧管线靠代次失效。
- 两个宿主 `AppBootstrap` / `Core.Bootstrap` 转发 `OnApplicationPause`。恢复前台先处理已收到的踢线/重定向，然后关闭旧连接并走上述恢复。连接恢复仍使用原有断线清理、在途调用失败、场景重建及战斗补签路径。

自定义改动文件：`Assets/Scripts/Net/GateHeartbeat.cs`、`GateTcpClient.cs`，`Assets/Scripts/Game/GameClient.Reconnect.cs`、`GameClient.cs`，两个宿主，以及 `Assets/Tests/EditMode/Net/GateHeartbeatTests.cs`、`GateTcpClientHeartbeatTests.cs`、`GameClientReconnectTests.cs`（新增文件均含 `.meta`）。同期组队跟随改动由另一会话负责，本项未改动其实现。

本轮验证：全部 446 个运行时源码 Roslyn 编译通过；10 个心跳时序、8 个真实回环 TCP 心跳、10 个既有 TCP 生命周期测试通过；7 个真实 Unity EditMode 恢复测试通过，其中一例通过本地 HTTP 和 TCP 完整执行 AssignGate、验签、token Login、原角色 EnterGame。另用真实 10000/30000 参数连续 120100ms 不调用 `Poll`，收到 13 条心跳，首条 45ms，结束时连接仍存活。最终恢复测试使用全部当前源码，没有 HEAD 替换，源码哈希核对未变化；早期隔离尝试保留日志，不计为最终通过证据。未做移动设备实机挂起测试或完整播放器出包。

源码提交 `f8076ce9596f84c745a5b86fdf9c4de5dad414e0` 对应的验收报告已归档。[心跳与连接生命周期](VerificationEvidence/gate-heartbeat-20261106/transport-results.json)、[Unity EditMode 重连](VerificationEvidence/gate-heartbeat-20261106/unity-reconnect-results.xml)、[120 秒回环 TCP](VerificationEvidence/gate-heartbeat-20261106/heartbeat-120s.json)。

本机证据在 `.codex-artifacts/heartbeat-20261106/`：`runtime-compile-current.log`、`transport/results.json`、`recovery-current/unity-results.xml`、`recovery-current/source-hashes.json`、`heartbeat-120s.json`。本机可复跑：

```powershell
Set-Location D:\luyuan\mmorpg-client
pwsh -File tools/client_compile_check.ps1
pwsh -File .codex-artifacts/heartbeat-20261106/run-transport-tests.ps1 -ProjectPath D:\luyuan\mmorpg-client -EvidencePath D:\luyuan\mmorpg-client\.codex-artifacts\heartbeat-20261106\transport
pwsh -File .codex-artifacts/heartbeat-20261106/run-120s.ps1
pwsh -File .codex-artifacts/heartbeat-20261106/compile-current-recovery.ps1 -ProjectPath D:\luyuan\mmorpg-client -EvidencePath D:\luyuan\mmorpg-client\.codex-artifacts\heartbeat-20261106\recovery-current
```

恢复测试最后一步在已准备的最小 Unity 工程中运行：将 `recovery-current/HeartbeatRuntime.dll` 复制到同级 `unity-recovery/Assets/Plugins/`，将当前 `GameClientReconnectTests.cs` 复制到其 `Assets/Tests/`，运行 `D:\luyuan\tools\Unity\6000.6.0f1\Editor\Unity.exe -batchmode -nographics -projectPath D:\luyuan\mmorpg-client\.codex-artifacts\heartbeat-20261106\unity-recovery -runTests -testPlatform EditMode -testFilter MmorpgClient.Tests.EditMode.Net.GameClientReconnectTests -testResults D:\luyuan\mmorpg-client\.codex-artifacts\heartbeat-20261106\recovery-current\unity-results.xml -logFile D:\luyuan\mmorpg-client\.codex-artifacts\heartbeat-20261106\recovery-current\unity.log`。该容器只隔离资源导入，加载的是上述全部当前源码编译的 DLL；常规开发也可直接在主项目 Unity Test Runner 执行这四个 Net 测试夹具。

## 组队跟随改为服务器驱动(2026-11-06 起,2026-11-11 补齐)

事实来源:服务端 `proto/scene/player_movement.proto` 末尾追加的两个方法,消息号由生成器分配:381 `SceneMovementClientPlayerTeamFollow`(`TeamFollowC2S{follow}`,单向)、382 `SceneMovementClientPlayerNotifyTeamFollowState`(`TeamFollowStateS2C{driven, leader_entity, slot, reason}`,推送)。客户端只用生成的 `SceneMovementClientPlayerRpc`(`TeamFollowMessageId` / `OnNotifyTeamFollowState`),不进 `MessageIds.cs` 的白名单。设计与时序见服务端 `docs/design/team-follow-server-trail.md` §5。

- **谁在走**:队员走位由服务器沿队长走过的路驱动,客户端不再自己算轨迹(`TeamFollowTrail` / `TeamFollowPlanner` 已删除)。收到 driven=true 后本地角色按发给自己的 133 `ActorMoveS2C` 插值显示,本地控制器不走、不上报移动,点地 / 按键 / 点 NPC 寻路只出"正在跟随队长"的提示;driven=false 时落到最后一条移动给的位置,交回本地控制。
- **到达先后**:发给自己的 133 与 382 不保证先后。客户端只在被驱动期间使用发给自己的 133,其余时间收到的一律忽略;130 `TeleportS2C` 任何时候照常吸附;被驱动期间到达的 137 `MoveAck` 只是驱动开始前那次上报的迟到裁决,不拿它回拉。
- **声明**(`Game/Team/TeamFollowDeclaration.cs`):在队、不是队长、没暂离就跟。每次入场(`GameClient.SceneEntryEpoch` 变化)声明一次 true;声明过仍没被驱动,宽限 1.5 秒后每 5 秒再说一次,队长不在线时不重试;点「暂离」立刻发 false,暂离中仍被驱动每秒再撤一次;点「归队」立刻发 true。跟随状态只认服务器:入场通知与断线会把本地的驱动状态清零,服务器对仍在队形里的队员入场后补推 driven=true(包括开通挂机后杀掉客户端、再登录接回的角色)。原来的归队请求 `TeamClient.RequestRegroup` 不再由跟随使用。
- **新客户端连旧服务器**:旧服务器不认识 381,也从不推 382,客户端停在"还没接上"(界面显示队长暂时不在视野内),本地角色照常由玩家自己控制;不会出现角色被锁住的情况。旧客户端连新服务器:不发 381,服务器按原规则对待(归队瞬移到队长脚下)。
- **测试**:`Assets/Tests/EditMode/Tianyong/TeamFollowDeclarationTests.cs`(声明时机与 `IsFollower`)、`TeamRegroupPolicyTests.cs`(重试节奏)、`ActorWorldFoundationTests.cs` 的服务器驱动五条(驱动中按 133 外推、停止驱动落到最后位置之后不再挪人、`Clear` 复位、控制器被驱动时关掉 CharacterController 且只出提示、382 与入场通知的派发)。截至本节写入时都没有在 Unity 里运行过。

## Gate 错误信封与重定向（2026-11-11）

本节以服务端仓 `docs/design/client-request-ordering.md`（O4、O6、O9、§4.6、§4.7、§11 的「Q4 结论」）为事实源，没有协议改动。

- Gate 拒绝请求时回带请求号、`message_id` 为原请求号、body 为空的错误信封，码包括 1003、1008、1006、1010 等。`OutstandingCalls` 按请求号认领，结局为 `ServerTip`；应答类型是 `Empty` 时也不会把空 body 当成功。认领不到的信封只记 `late reply dropped`，不交给推送处理器。
- 单向请求的请求号为 0，例如 MoveStart / MoveSync / MoveStop，它们的信封按 `message_id` 交给各自的处理器，只记日志。没有处理器的单向信封记为 `[rpc] one-way request rejected`。
- Gate 推 124 之前，会给还在排队的请求逐条回 1003。客户端分不出这种 1003 和「发出后失败」的 1003，所以一律按结果未知处理：不改业务状态，提示稍后重试，写请求复用幂等键。帮会和组队的「待确认」状态会在连接对象变化时自动解除。
- 收到 124 后，`RedirectFlow` 把旧 gate 记为已搬走。之后对它的 `CallMethod` 当场以 `NotSent("gate redirected")` 失败，单向发送不再发出，`IsGateReady` 为假，直到换到新连接或断线。
- 客户端只在进场失败（3023）之后、同一连接上换角时发 LeaveGame（2026-11-12 起，见文末一节），而且等它回包才发下一条 EnterGame，那时游戏内各功能都还没开始发请求，所以 gate 的 LeaveGame 屏障实际碰不到。战斗 RPC 只走直连，不会碰到 gate 的 battle 闸。
- 不等回包就连发的依赖请求只有两类：匹配排队的 JoinQueue → CancelQueue，以及跨功能的 CancelQueue 与 StartTeamMatch / RespondGuildTrialInvite。两类都由 gate 路由 lane 按收包顺序执行。客户端只在功能内单飞，不做全局串行。

改动文件：`Assets/Scripts/Game/GameClient.cs`、`Assets/Scripts/Net/RpcStatus.cs`（注释）、`Assets/Scripts/Game/Battle/DirectRoutingBattleTransport.cs`（注释），以及新测试 `Assets/Tests/EditMode/Net/GameClientGateEnvelopeTests.cs`（含 `.meta`，5 例，真实回环 TCP）。尚未编译，也没有在 Unity 里运行。验证命令见服务端仓 `docs/PROGRESS.md` 2026-11-11「客户端请求保序:客户端配合」条。

帮会补充（同日）：`GuildClient` 不再对任何失败都隔离面板——确定未执行的失败（本地 `NotSent`，gate / 路由服转发前的 1008、1010、1006、1005、2028 信封）只解除忙碌、提示重试；1003、超时、解析失败等结果未知的失败仍隔离到换连接，捐献 / 兑换两类都保留本机记录、只能按原 client_request_id 恢复（测试 `Assets/Tests/EditMode/Guild/GuildRequestFailureTests.cs`，未编译、未运行，验证命令见服务端仓 `docs/PROGRESS.md` 2026-11-11「帮会请求失败分类」条）。

## 战斗道具预留与背包满改投邮件（2026-11-12）

本节以服务端仓 `docs/design/battle-item-reservation.md`、`docs/design/battle-overflow-mail.md` 为事实源。服务端只追加字段，消息号不变；旧客户端忽略新字段，行为与原来相同。

| 消息字段 | 含义 | 客户端用在哪 |
|---|---|---|
| `BagInfo.held_items = 4`（`BagHeldItemInfo { config_id, count }`，`proto/scene/player_bag.proto`） | 只有人物背包带。这种物品有多少押在还没结算的战斗里：交易、捐献暂时动不了，那一局结算或作废之后放开 | 背包详情加一行「战斗结算中 N（结算后可交易）」；聚宝斋「从背包选择」只让选没被占住的数量，全被占住就不让选 |
| `BattleEndS2C.items_mailed = 4`（`proto/battle/player_battle.proto`） | 本局获得的道具两个背包都放不下，服务器整批改由邮件发放 | 结算屏在道具行之后加一行「背包已满，以上道具改由邮件发放，请留意邮箱」。写「改由」不写「已」：收到结算时邮件多半还在寄送途中，邮件服务不可达时会更晚到 |
| `PlayerBattleResult.items_mailed = 17`（`proto/common/component/player_battle_result_comp.proto`） | 同一个事实，记在近期战斗结果里 | 落地补查到的结果补弹时，道具行之后同样加这一行（见下一节「近期战斗结果补查与补弹」） |

- 被占住的数量只用于显示和选物品时的上限，能不能出包由服务器裁决。押的是数量，不指定哪一堆：同一种分几堆时，任何一堆最多只能选出这种物品没被占住的总数。
- 展示推导都在 `EquipBagModel`（`HeldCountOf`、`AvailableCountOf`、`AvailableInStack`、`HeldLine`），不依赖 UnityEngine。背包格子上的数量角标没有改：效果图没有这个状态的设计。
- 改动文件：`Assets/Scripts/UI/Ugui/Gameplay/EquipBagModel.cs`、`GameplayWindow.Fidelity.cs`，`Assets/Scripts/UI/Ugui/Jubaozhai/CommerceWindow.cs`，`Assets/Scripts/UI/Ugui/Battle/BattleResultPanel.cs`；测试 `Assets/Tests/EditMode/Battle/EquipForgeModelTests.cs`（2 例）、`DamageNumberLayoutTests.cs`（1 例）。
- 这些代码引用了还没生成的类型和属性（`BagHeldItemInfo`、`BagInfo.HeldItems`、`BattleEndS2C.ItemsMailed`、`PlayerBattleResult.ItemsMailed`）：先从服务端当前 proto 重跑 `tools/gen_proto.ps1`，客户端才能编译。尚未生成、未编译，也没有在 Unity 里运行；验证命令见服务端仓 `docs/PROGRESS.md` 2026-11-12「战斗掉落两包都放不下改投邮件」条。

## 近期战斗结果补查与补弹（2026-11-12）

本节以服务端仓 `docs/design/battle-result-recovery.md` §6、§7 为事实源。协议早已生成（`SceneBattleResultClientPlayerRpc`：`GetRecentBattleResults` 371、`AckBattleResults` 372），本节不改协议，只把一直没接的客户端半边接上：以前断线或离线期间结算入账的那一局，玩家重登后看不到结果。

- **什么时候查**：每次落地查一次（与 `LandingSyncClient` 同一口径：`InGame` 且传输就绪，`(PlayerId, 连接身份)` 变了就是一次新的落地；`InGame` 在 `NotifyEnterScene` 之后才为真）。收到没有战斗上下文的 `BattleEndS2C` 再查一次（`BattleClient.OnBattleEndWithoutContext`）。两种触发共用单飞：在途时再来的触发，只在它回来之后多查一次。
- **失败**：传输错误、超时或回包带 tip，按 15 / 30 / 60 秒封顶退避，成功即归零。
- **弹什么**：回包里 `seen = false`、本进程没展示过的，按 `result_seq` 从小到大逐个交给 `BattleUiRoot`。`BattleUiRoot` 只在空闲时弹（没有战斗屏、观战屏，也没有结算面板在显示），确认一局再弹下一局；确认键写「知道了」，不收战斗屏，也不调 `AckBattleEnd`。说明行是「上次未查看的战斗 · 时间 结束 · 共 N 回合」。明细只列服务端记下的实际入账（经验、货币、获得与消耗的道具、改由邮件发放、物品行被截断、阵亡 / 逃离）。旧战果没有经验字段，沿用原有明细。作废局显示「本局作废」和作废说明行。
- **确认**：面板真正弹出之后确认已读，现场的结算面板也确认（否则下次落地还会再弹一次）。一次至多 10 个 battle_id。失败（1006 冻结 / 交接中、超时）不重弹，留在待确认列表里，下次落地先补发再查。待确认只在内存里，进程退出最多让同一局再弹一次。没有上下文的作废局已经用提示告诉过玩家，视同展示过：直接确认，补查时不再弹面板。
- **改动文件**：新 `Assets/Scripts/Game/Battle/BattleResultRecoveryClient.cs`（含 `.meta`，纯逻辑，不引用 UnityEngine）；`BattleClient.cs`（新事件 `OnBattleEndWithoutContext`，作废提示照旧）；`GameClient.cs`（`BattleResults` 装配与 `Tick` 驱动）；`Assets/Scripts/UI/Ugui/Battle/BattleResultPanel.cs`（`ShowRecovered`、`BuildRecoveredLines`、`BuildRecoveredSubtitle`，胜负标题与展示抽成 `ResolveTitle` / `Present` 两段，现场与补弹共用）；`BattleUiRoot.cs`（补弹排队与确认分支）。
- **测试**：新 `Assets/Tests/EditMode/Battle/BattleResultRecoveryClientTests.cs`（含 `.meta`，13 例，假传输驱动：落地只查一次、只交未读并按序、没进场不查、10 个一批确认、现场展示过不再补弹、无上下文终局单飞多查一次、作废提示直接确认、15 / 30 / 60 秒退避、确认失败下次落地先补发、1006 留着、上一次落地的回包丢弃、没展示过的下次落地再交、服务端已读清掉待确认）；`DamageNumberLayoutTests.cs` 加 4 例（补弹明细行、无获得与逃离、作废、说明行）。
- 尚未生成、未编译，也没有在 Unity 里运行；`PlayerBattleResult.ItemsMailed` 要先重跑 `tools/gen_proto.ps1`。验证命令见服务端仓 `docs/PROGRESS.md` 2026-11-12「近期战斗结果客户端补查与补弹」条。

## 强化期望等级与幂等键、临时格取回、进场失败后换角（2026-11-12）

本节以服务端仓 `proto/scene/player_bag.proto`、`docs/design/equipment-enhance-inlay.md` §4.3 / §6.1、`docs/design/client-request-idempotency.md` §2.1、`docs/design/bag-rule-policy-layering.md` §6.6、`docs/design/battle-overflow-mail.md`、`docs/design/client-request-ordering.md` §6.2 为事实源。消息字段与 `SceneBagClientPlayerRpc.RetrieveTemporaryItems`（374）早已生成进本仓，只差 `bag_error` 的 tip 枚举（见末条）。

- **幂等键**（`Assets/Scripts/Net/ClientRequestKeys.cs`）：每次点击、本地检查都过了才生成一个非 0 的随机 64 位键，这次操作拿到明确结果之前的重发都带它。`ShouldResendOnce` 只对超时（`OutstandingCalls.TimeoutDetail`）与信封 1003 为真；断线、1008 等确定没执行的信封、解析失败都不重发。信封串的反解收在 `RpcStatus.TryParseLegacyServerTip`，与 `ToLegacyError` 同处。带键请求在响应体里收到 1005 = 客户端把旧键用在了新操作上，记 `[client-request] key reused for another operation: <方法>` 错误日志。帮会经济的取键改为调同一个函数。
- **强化**（`PlayerFeaturesClient.EnhanceEquip`）：请求带 `expected_level`（发包时快照里这件装备的等级，0 级填 0；两份快照缺一份时取还在的那份，都没有填 0）与 `client_request_id`。超时或 1003、连接与角色没变时用同一个请求对象原样重发一次，在途标志与发包间隔不动；重发也不明就按原样只重拉两份快照。`replayed = true` 当首次结果发布，每次点击至多一条 `OnEnhanceResult`，提示与特效只播一次。`kEquipEnhanceLevelChanged` 显示「无法强化:装备强化等级已变化,请刷新后再试」并重拉人物背包与装备栏。穿脱、镶嵌、取下不带键，行为不变。
- **神秘商人购买、新建加点方案**：同一规则。购买的 1003 改按结果不明处理（以前当成确定没买成），重发后仍不明进入 `RequiresReconnect`；新建方案重发后仍不明就报错并重拉面板。其它加点写请求不带键、不重发。
- **临时格**（`PlayerFeaturesClient.Temporary.cs`、`TemporaryBagModel.cs`、`TemporaryBagDialog.cs`）：背包页「工具」里新增「临时格 n/容量」入口，打开面板逐件「取回」或「全部取回」（`item_id` 与 `take_all` 二选一），成功用回包里的人物背包与临时格两份全量整体覆盖。`BagTemporaryItemNotFound` 重拉两个包；`BagTemporaryRetrieveBagFull` 提示先腾出人物背包空间（全部取回时加「或改为逐件取回」）；`BagTemporaryRetrieveBusy` 提示交接或传送途中稍后再试；战斗中不在本地拦。取回不带键，结果不明不重发、只重拉两个包。取回单飞，与整理、穿脱 / 强化 / 镶嵌 / 取下互斥，两次发包至少隔 700 毫秒。进游戏、从战斗界面回到大世界、打开 / 刷新背包页时拉一次临时格；占用 ≥ 90% 时背包页状态行显示提醒，由不满变满时飘字一次。面板里的满格说明按 2026-11-12 起的结算规则写：人物背包也放不下的新掉落改由邮件发放，邮件暂时发不出时那一局结算延后，取回腾位后自动补发。
- **进场失败后换角**（`GameClient.RoleSwitch.cs`，`GameClient.ConnectAndEnter` / `EnterGameAndWaitScene`，`RoleFlowUi.Choose`）：有选角界面时，收到 3023 而连接还在就不拆连接，回到选角页，提示行显示「进入场景失败，请重新进入或选择其他角色」，一次登录最多回选角 3 次；重定向、短重连、无界面路径照旧拆连接回选服。同一连接上要进另一个角色（含新建的角色），先 LeaveGame（超时或 1003 时最多发 3 次，确认不了就拆连接），再 EnterGame；C++ 版 LeaveGame 会清掉本连接的登录态，EnterGame 回 2028 时在同一连接上重新 Login 一次再进。已绑定角色时换角收到 2005 先 LeaveGame，不当成登录会话不存在去重新登录。同一角色的 EnterGame 超时或 1003 后用同一个 request_id 重发一次；重发撞上前一次进场还占着锁（2005）时接着等进场结果。`GameClient.RedirectFlowWorstCaseSec` 因此多算一次 EnterGame（105 → 120 秒），`CityTravelRequest.CrossZoneTimeoutSeconds` 随之 195 → 210 秒。
- **原图验收**：临时格面板、工具里的入口、状态行的满格提醒都没有效果图，沿用装备弹窗与工具栏的皮肤，只做了功能，视觉验收另算。
- **测试**：新文件 `Assets/Tests/EditMode/Net/ClientRequestKeysTests.cs`、`EnterGameRoleSwitchTests.cs`、`Assets/Tests/EditMode/Battle/TemporaryBagClientTests.cs`（均含 `.meta`）；`EquipForgeClientTests.cs`、`AttributeClientTests.cs`、`Tianyong/MysteryMerchantClientTests.cs` 各补幂等键用例，原来「强化超时绝不重发」与购买超时的三条用例按新规则改为超时两次才收尾。
- **生成物**：`tools/gen_proto.ps1` 加了 `generated/code/proto/tip/bag_error_tip.proto`，要先从服务端当前 proto 重跑它，生成 `BagErrorTip.cs`，否则 `bag_error` 报 CS0103。尚未生成、未编译，也没有在 Unity 里运行；命令见服务端仓 `docs/PROGRESS.md` 2026-11-12「客户端·强化期望等级与幂等键、临时格取回、进场失败后换角」条。

## 本地开发版银两测试按钮与构建（2026-11-16）

在 Unity Play 或显式 Development 测试包中，角色进入本机测试服后，游戏画面顶部显示「本地 GM：增加 10 万银两」。每次主动点击调用现有 `SceneCurrencyClientPlayerRpc.GmAddCurrency`，固定 `currency_type = 0`、`amount = 100000`，只增加当前角色的银两。收到服务器成功响应才显示 `balance_after`，并刷新当前角色的背包与宠物商店余额。`MMORPG/GM/银两测试说明` 编辑器菜单只显示使用说明，不发送请求；游戏内按钮共用唯一在途状态。

`Assets/Scripts/App/LocalGmCurrencyClient.cs` 与 `LocalGmCurrencyDebugUi.cs` 的全部实现都在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内；正式包不包含这两个新增类型、自动挂载或加银两实现。已有生成的 GM 协议类型与 RPC 桩保持原状，不能把生成桩的存在当作正式包包含测试按钮。开发按钮仅在 gateway HTTP(S) 地址与分配的 gate TCP 地址都是 loopback、已进入角色且连接就绪时显示，每次实际发送前再次检查。没有内嵌管理员密钥，也没有凭客户端按钮授予账号权限。

服务端沿用既有 Gate 与 Scene 的运行模式闸门：`GATE_RUN_MODE` / `SCENE_RUN_MODE` 的 dev/test 模式允许客户端 GM，prod、未设置或无法识别的模式拒绝。当前 localdev 测试服的测试账号均可使用此能力，并非仅某个管理员账号；不能声称开发服普通账号被账号权限隔离。面向玩家的正式包通过编译排除入口，生产服通过服务端模式拒绝请求。客户端 loopback 检查是本地测试范围限制，服务端仍是授权边界。

请求在途时按钮不可再次提交。超时、断线、解析失败或服务端故障等无法确认的结局显示「结果未知」，不会自动补发；角色或连接已变化也不会把旧响应当作当前角色成功。此 GM 协议没有幂等键，用户应先核对余额；未知结果后的按钮明确表示一次「新加 10 万」操作。成功后的延迟刷新只执行只读查询，并核对原角色与原连接。

正式构建入口为 `Assets/Editor/ClientPlayerBuild.cs`，由 `tools/build_client.ps1` 调用。默认 `BuildOptions.None`；只有显式 `-Development` 才设置 `BuildOptions.Development`，让 Unity 定义 `DEVELOPMENT_BUILD`。脚本默认分别创建 `Builds/windows-x64-release-<构建编号>` 或 `Builds/windows-x64-development-<构建编号>`；显式 `-OutDir` 必须是尚不存在的新目录。执行前需退出占用同一工程的 Unity 编辑器。

```powershell
Set-Location D:\luyuan\mmorpg-client
# 普通发布包：不含新增测试按钮和发送实现。
pwsh -File tools/build_client.ps1
# 本机测试包：包含开发功能，勿作为普通玩家发布包。
pwsh -File tools/build_client.ps1 -Development
```

构建使用当前已启用且保存的场景、Windows x64 Player 目标和 `BuildPipeline.BuildPlayer`，不修改工程构建设置；若项目手工配置 `UNITY_EDITOR` / `DEVELOPMENT_BUILD` 宏则拒绝，以免污染正式包。输出 `build-manifest.json` 记录实际 `BuildReport` 结果、请求与实际构建选项、Unity 版本、后端、场景、设置哈希及 EXE SHA-256。外层脚本按数值旗标、`buildFlavor`、开发标志与完整输出核验，枚举字符串仅用于诊断，避免 Unity Mono 的废弃零值别名影响判断；失败产物保留，不覆盖既有输出目录。构建会话限制为 2 个 Job Worker、1 个导入 Worker，并记录退出时内存统计；没有更改系统分页文件或资源导入设置，没有启用 LZ4。

本轮已完成的验证：

- 全部 483 个当前运行时源码分别按 Development / Release 编译成功；元数据检查确认 Development 有新增 GM 类型与方法，Release 中新增 GM 类型和方法均为 0，编译期间源码未变化。证据：`.codex-artifacts/local-gm-runtime-20261116/full-runtime-20261116/verification-summary.json`。
- 纯逻辑测试 20/20 通过，覆盖端点门禁、固定货币与金额、单飞、未知结果、会话变化及刷新。证据：`.codex-artifacts/local-gm-runtime-20261116/tests-results.json`。
- Unity 原生首批 21 项为 20 通过、1 失败；失败项是 UI 测试依赖内部对象名字，修正后单独重跑该 UI 用例 1/1 通过。保留首批失败证据，不将其改写为全通过：`native-editmode.xml` 与 `native-ui-final.xml`，均位于上述证据目录。
- 构建脚本 PowerShell 解析、Development / Release 参数检查，以及使用本机 Unity 6000.6.0f1 真实引用的 Editor 构建入口离线编译通过；未以离线编译代替实际 Player 打包。
- 服务端 Gate 运行模式与 GM 闸门定向测试 6/6 通过；使用实际 Scene 闸门头文件的独立进程探针 5/5 通过（未设置/prod/未知值拒绝，dev/test 允许）。完整 Scene 测试程序因配置加载失败未执行，不能记为通过。证据在服务端仓 `build/verification/gm-client-gate-audit-20261116/`。

出包最终状态：首轮 `Builds/windows-x64-gm-test-20261116` 在读取 Resources 纹理时发生 native 内存不足崩溃（dump 确认 Mimalloc error 12；日志 `Logs/build-client-20261116-093437-411-7004.log`）。修正构建清单兼容性并限制并发后，`windows-x64-gm-test-20261116-r2` 重试由用户明确取消，构建进程已停止。本轮没有生成新的可运行 Player，也未完成连接真实服务器后的游戏内按钮点击与余额变化验收；不得将原生 UI 夹具通过当作这项验收。以上测试不构成客户端画面与原图一致的验收。

## 客户端统一限流、降级与熔断约定（2026-11-20）

客户端长连接（大厅 `GameClient` 与战斗直连 `BattleDirectLink`）均固定落在单台边缘节点，不照搬分布式后端的 Redis 限流，而是用纯内存简单类挂在统一出口 `IRpcChannel`（[RpcGuard.cs](../Assets/Scripts/Net/RpcGuard.cs)、[GuardedRpcChannel.cs](../Assets/Scripts/Net/GuardedRpcChannel.cs)）上，统一覆盖全部业务 RPC 与战斗直连 RPC，并在换连接或断线时一处清零：

- **统一限流（`SlidingWindowLimiter` + `RpcGuard`）**：按 `messageId` 自动查表 `MessageLimiterTableManager` 并支持配置表热更实时重建（表里 `max_requests == 0` 视为不限；未配置或未加载表时走服务端缺省 3 次 / 1.5 秒——C++ 服务端 `MessageLimiter` 与 Java `MessageRateLimiter` 均已统一为精确单调毫秒/纳秒滑动窗口 `>=`，客户端外留 0.5 秒吸收公网上行抖动）。会话生命周期与单向移动控制号（`Login`、`CreatePlayer`、`EnterGame`、`LeaveGame`、`Disconnect`、`RefreshToken`、`EnterScene`、`GetSyncDigest`、`Move*`）豁免。本地超限时同步返回 `RpcStatus.ServerTip(1008)`，不发包、不消耗服务端非法包计数；对外提供只读 `GameClient.CanSend(messageId)`、`BattleDirectLink.CanSend(messageId)` 与 `RpcGuard.IsRateLimited(messageId)` 给界面置灰按钮。
- **统一熔断（`SimpleCircuitBreaker` + `RpcGuard`）**：标准三态（`Closed` / `Open` / `HalfOpen`），按**服务域**分桶（`team` / `match` / `battle` / `rank` / `guild` / `mail` / `jubaozhai` / `attribute` / `pet` / `mystery_merchant` / `afk_pass` / `scene_*`，未归类消息按号隔离，绝不全局一把锁）。`CallMethod` 收口处自动将 `Timeout` 与 `TipFaults.IsFault(tipId)` 计为失败（默认连续 3 次打开 15 秒）；`HalfOpen` 仅放行 1 个在途探测。熔断打开期先于限流拦截并返回 `RpcStatus.NotSent("circuit open")`，不误扣限流配额、不触发幂等重发，也不触发旧业务客户端的 `RequiresReconnect` 锁死（`TeamClient`、`GuildClient`、`TeamInvitationDirectory`、`MysteryMerchantClient` 均已兼容）。**红线**：`1008` 限流与非故障业务拒绝绝不计入熔断，若在 `HalfOpen` 遇 `1008` 仅调 `ReleaseProbe()` 释放槽位；协议体内部故障（如 `TeamResponse.Team == null`）通过 `RpcGuard.RecordServiceFault(messageId)` 衔接计数。
- **服务端侧配套（主仓 C++ / Go）**：
  - C++ 边缘层 `MessageLimiter`（`gate` 与 `battle` 直连共用）已由墙钟整秒 + `>` 升级为 `std::chrono::steady_clock` 毫秒精确滑动窗口 + `>=`，用 `std::deque<uint64_t>` 替换 `boost::circular_buffer::resize` 缺陷，支持 `max_requests == 0` 无限与配置热更。
  - Go 统一路由服 `client_rpc_router` 新增 `shared/circuitbreaker` 按目标实例（endpoint）隔离的三态熔断（默认连续 5 次拨号/转发故障打开 10 秒），选点自动跳过熔断实例切流向健康节点，全员熔断时快速回 `1003`（`outcome="circuit_open"`），节点下线随 `onRemove` 自动清理，调用方主动取消不误触发熔断。
- **业务层降级与 UI**：`TeamClient`、`BattleMatchEntryClient` 与 `RankClient` 生产均共享注入 `GameClient.RpcGuard`（零重复计数），业务层只保留降级与界面态（如 `MatchNetworkPreparing` 期间停发后台刷新、`BattleQueuePanel` 在冷却期置灰开始与取消按钮并显示原因，绝不将拦截点击排队延后自动开战）。

## 主角权威状态互斥即时同步与未来 UnityHFSM 接入边界（2026-11-21）

事实源：服务端 `proto/scene/client_player_common.proto` 与 `docs/design/scene-actor-state-sync-and-client-hfsm-boundary.md`。原 `66`（`ActorBaseAttributesS2C`）以 10Hz 节流广播 AOI 旁观者且不携带行为状态，主角本地 UI 与未来实时动作需要服务端态变化即刻到端。

### 1. 主角即时状态位图同步（已落码并经 EditMode 43/43 验证）

- **协议契约**：`SceneClientPlayerCommon.NotifySelfActorStateFlags`（消息号 `440`，`CLIENT_RPC_PUSH`，生成入口为 `SceneClientPlayerCommonRpc.OnNotifySelfActorStateFlags`），载荷 `SelfActorStateFlagsS2C { uint64 action_state_mask, uint64 combat_state_mask, uint64 state_seq }`：
  - `action_state_mask`（`eActorState`）：`bit 0 = Combat`、`bit 1 = TeamFollow`、`bit 2 = Mounted`；
  - `combat_state_mask`（`eActorCombatState`）：`bit 1 = Silence`；
  - 服务端在整轮行为变更（含打断跟随/骑乘退出钩子+入战斗态）合并后仅向**主角本人**单播 1 次；无变化、冻结交接或旁观者不收此包；切换场景/重连落地后若有非零态会由服务端补推 1 次。
- **客户端落点**：
  - `Assets/Scripts/Game/ActorStateFlags.cs`：`ActorActionStateFlags` / `ActorCombatStateFlags` 位枚举与不可变值快照 `SelfActorStateSnapshot`；
  - `Assets/Scripts/Game/GameClient.ActorState.cs` & `GameClient.cs`：暴露 `SelfActorState`、`SelfActionStateMask`、`SelfCombatStateMask` 与主线程事件 `OnSelfActorStateChanged(prev, next)`；按单调 `state_seq` 丢弃乱序旧包，同掩码不重复触发事件；`NotifyEnterScene` 与断线统一调用 `ResetSelfActorStateFlags()` 复位并发出回归默认态通知；
  - `Assets/Tests/EditMode/Role/SelfActorStateFlagsTests.cs`（3/3 在 Unity `6000.6.0f1` 隔离 EditMode 通过）：覆盖差异事件、乱序丢弃、同掩码去重与进场复位后 `state_seq=1` 重开窗口。

### 2. `Inspiaaa/UnityHFSM` 选型与强边界（实时 MMO 动作 / 技能演出规划）

- **何时接入**：当前客户端尚无实时 MMO 角色动作与连招演出系统，**暂不预埋依赖包**；当正式开始制作实时 MMO 角色动作 / 技能演出状态机时，适合接入 `Inspiaaa/UnityHFSM`（建议以本地 `.tgz` 进 `Packages/` 或源码 vendor，并先核对 MIT 许可与源码零禁用 API）。
- **平台兼容性**：`UnityHFSM` 为纯 C#、单线程 `Update()` 驱动，不使用 `Thread` / `Task.Run` / `System.Reflection.Emit`，满足 [Docs/rules/minigame-and-app-platforms.md](rules/minigame-and-app-platforms.md) 对微信/抖音小游戏 WebGL2 与 App IL2CPP/AOT 的硬约束；内置层级状态与 `needsExitTime` 适合处理攻击前后摇、收刀与受控强切。
- **架构红线（「表现层归客户端、状态权威归服务端」）**：
  1. **只管表现与防误触**：客户端 `UnityHFSM` 只承载移动 / 骑乘 / 施法动作融合、特效挂点、取消窗口动画节奏以及基于 `SelfActorState` 的 UI 按钮置灰；**禁止**在客户端本地裁决状态互斥、强打断逻辑、真实冷却或受击结果。
  2. **由 `OnSelfActorStateChanged` 单向纠偏**：表现层状态机必须订阅 `GameClient.OnSelfActorStateChanged(prev, next)`（并在 `OnDisable`/`OnDestroy` 对称退订），收到服务端沉默、眩晕、脱离跟随、下马或被强制打断时，直接触发 `ForceRequestStateChange` 向 `SelfActorState` 收敛，不得因本地动画 `needsExitTime` 未结束而拒绝服务端权威状态。
  3. **单线程有序与周期清零**：全部转换在 Unity 主线程 `Update` 或 `GameClient` 收包回调后有序执行；换场景或断线随 `ResetSelfActorStateFlags()` 同步复位状态机。
