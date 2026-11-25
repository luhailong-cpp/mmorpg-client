# 灵宠翻牌小游戏（本地纯逻辑）设计与 UI 对接方案

> 对应仓库：`mmorpg-client`（Unity 客户端，仅本地逻辑与本机持久化，不新增服务端业务接口）。
> 职责划分：本方案与源码负责完整核心玩法、状态机、本机纪录持久化、生命周期收敛与自动化测试；正式 UI、美术与动画由另一台机器的 Codex 依据本文档与公开 API 实现。

---

## 1. 需求分析

### 1.1 定位与硬边界
- **玩法定位**：战斗之外的单人休闲小游戏，单局目标 1–2 分钟；全免费、不限游玩次数。
- **零联动隔离**：不发材料/经验/货币，不关联每日任务、排行榜、付费提示或真实宝宝养成系统；不新增任何服务端 RPC 或 `.proto`。
- **双形态兼容**：必须同时满足微信/抖音小游戏（WebGL2 单线程禁阻塞、禁多线程/异步文件 IO、禁 `Reflection.Emit`）与 iOS/Android App（IL2CPP/AOT）约束。
- **不改全局世界**：不暂停联网世界、不修改 `Time.timeScale`、不绕过现有登录、实名防沉迷、账号注销及平台限制。

### 1.2 核心玩法规则
1. 一盘 12 张牌（槽位 `0..11`），6 种灵宠图案（`A..F`），每种恰好 2 张；正式开局随机洗牌，全部背面朝上（`FaceDown`）。
2. 点未配对牌，翻开作为本手第 1 张（`FaceUp`），翻牌次数不变。
3. 再点另一张未配对牌，翻开作为本手第 2 张，**翻牌次数立即 +1**。
4. 两张图案相同：标记为已配对（`Matched`），留在原位亮起，进入下一手。
5. 两张图案不同：两张保持 `FaceUp` 亮起供玩家观察，**不自动倒计时盖回**。
6. 失配停留后，下一次点击任一未配对牌（允许点击刚才失配的两张中的任一张）：**先盖回原两张，再把本次点击当作新一手第 1 张**，翻牌次数不变。
7. 点击已配对牌、本手第 1 张或非法位置（`<0` 或 `>=12`），不改变次数和盘面（含失配停留时误点已配对牌或非法位置，也不盖回失配牌）。
8. 配齐 6 对立即完成，**严格只完成一次**；无倒计时、无次数上限、无失败惩罚。
9. 盘中无重新洗牌按钮；仅在完成后点击“再玩一局”才生成新盘。
10. 成绩只比较翻牌次数（越少越好），用时不参与成绩。

### 1.3 结果、本机纪录与容错
- 终局比较：无旧纪录 → `FirstRecord`（首次纪录）；少于旧纪录 → `NewBest`（刷新纪录）；等于旧纪录 → `TiedBest`（追平纪录）；多于旧纪录 → `NormalCompletion`（普通完成，旧纪录不变）。
- 设备级持久化：本机最佳属于当前设备当前安装，不按账号/角色/区服隔离；**只持久化最佳次数**，不持久化整副牌，不保存密码、令牌等账号信息。
- 缺失或损坏（空值、非法格式、版本号不匹配、非数字、`< 6`、读取异常）一律按“暂无纪录”处理。
- 保存失败不得阻止继续游玩，需明确暴露“本机纪录未保存”状态（`PetFlipRecordPersistenceStatus.Unsaved`）。

### 1.4 中断与生命周期
- 暂时关闭面板、切后台：同一进程、同一登录角色同区下保留盘面，回来继续。
- 临时断线（如重连中）：停止一切牌面操作，保留盘面；同角色同区会话恢复后继续。
- 明确登出、换角色、切服或关闭进程：丢弃内存中盘面（保留设备本机最佳）。
- 战斗、观战、加载与强制弹窗优先：翻牌页暂停收起，禁止在阻塞态继续翻牌或新开盘。
- 防御重复点击、迟到回调、面板销毁重建，杜绝重复计数、二次终局或开出多盘。

---

## 2. 设计分析

### 2.1 模块分层（对齐 `Docs/rules/engineering-principles.md`）
新增目录 `Assets/Scripts/Game/PetFlip/`（命名空间 `MmorpgClient.Game.PetFlip`），采用最小且职责清晰的三段结构：

1. **不可变契约层（`PetFlipTypes.cs`）**：
   - 纯 C# 值类型与只读快照，零 `UnityEngine` 依赖。
   - 跨模块/UI 一律通过只读快照（`PetFlipSnapshot`、`PetFlipActionResult`、`PetFlipCompletionResult`、`PetFlipLocalRecordSnapshot`）与事件传递，绝不暴露可变数组或内部状态引用。
   - 严格区分**实例与布局**：牌位 `SlotIndex`（在哪）与灵宠图案 `SpiritPetCardPattern`（是什么）及翻牌状态 `PetFlipCardState`（状态）显式分离；背面牌 `VisiblePattern` 为 `null`，防止表现层误读暗牌图案。
2. **纯规则与编解码层（`PetFlipBoardModel.cs`、`PetFlipBestRecordStore.cs`）**：
   - `PetFlipBoardModel`：纯 C# 单盘状态机，封装 12 牌校验、Fisher-Yates 洗牌与全部翻牌规则；逻辑同步落定，绝不等待 UI 动画回调才算分或配对。
   - `PetFlipBestRecordCodec` + `IPetFlipBestRecordStore`：版本化纯文本编码 `1|<bestFlips>`（要求 `>= 6`），隔离存储读写便于单测注入缺失/损坏/保存失败。
   - `PlayerPrefsPetFlipBestRecordStore.cs`：唯一引用 `UnityEngine.PlayerPrefs` 的薄适配器，同步读写键 `mmorpg.minigame.pet_flip.best_flips.v1`，捕获一切存储异常，天然兼容 WebGL2 单线程与 IL2CPP。
3. **会话协调与宿主桥接层（`PetFlipClient.cs` + `GameClient` / `AppBootstrap` 最小挂接）**：
   - `PetFlipClient`：纯 C# 会话/生命周期控制器，持有当前进程唯一有效盘面、本机最佳缓存、双掩码中断态（宿主中断 `_hostInterrupts` 与 UI/外部中断 `_externalInterrupts`）、视图绑定令牌（`viewToken`）与单调盘号（`RoundId`）+ 修订号（`Revision`）。
   - `GameClient` 构造时挂载 `PetFlip = PetFlipClient.Attach(new PlayerPrefsPetFlipBestRecordStore())`，在 `Tick()` 中以零堆分配方式同步在线角色、区服、重连、战斗、观战与重定向/加载状态；在 `DisconnectInternal` / `LeaveGameOnConnection` / `OnApplicationPause` 精确分流临时断线、切后台与明确登出/换角；`AppBootstrap.OnDestroy` 负责 `Dispose()`。

### 2.2 为什么不用复杂框架或第三方库
- 本模块是 12 张牌的确定性本地状态机 + 单整数持久化，C# 标准库 + `UnityEngine.PlayerPrefs` 即可完整覆盖且零包体/裁剪风险，符合 KISS / YAGNI 与双形态零第三方隐患原则。

---

## 3. 思想分析

1. **逻辑唯一真相（Logic-First, Animation-Agnostic）**：翻牌、失配盖回、计次、配对、终局结算与本机最佳保存均在调用瞬间同步完成；`PetFlipActionResult` 返回本次动作的精确变化明细（如 `CoveredFirstSlotIndex` / `CoveredSecondSlotIndex` / `FlippedSlotIndex` / `MatchedPattern`），UI 据此播放视觉过渡，动画中途被切走、重复点击或迟到完成回调都不会破坏权威盘面。
2. **按状态组合而非单布尔暂停（位掩码正交收敛）**：将暂停/中断拆成 `PetFlipInterruptFlags` 位掩码，并区分宿主态 `_hostInterrupts`（由 `GameClient.Tick` 驱动）与外部态 `_externalInterrupts`（由 UI 控制面板关闭、强制弹窗、外部加载遮罩等），彻底消除“多层中断叠加误提前恢复”及“每帧同步覆写 UI 弹窗标志”两类经典缺陷。
3. **长寿命控制器 + 短寿命视图令牌（防重建与迟到回调）**：盘面归属长寿命 `PetFlipClient` 而非 UI `MonoBehaviour`。UI 每次重建通过 `AttachView` 领取新 `viewToken` 并自动替代旧视图监听；旧视图迟到的 `DetachView`、点击或动画回调携带过期 `viewToken` 或过期 `expectedRoundId` 时被确定性拒绝。

---

## 4. 漏洞分析与防御对策

| 潜在漏洞 / 风险场景 | 根因 | 本方案防御手段 |
|---|---|---|
| 失配停留时误点已配对牌或越界槽位导致失配牌被意外盖回 | 先清失配再校验目标槽位合法性 | 严格先验槽位范围与 `State != Matched`，合法后才执行盖回原两张并翻开新第一张 |
| 失配后点击刚才失配的两张之一被误判为“点击同一张” | 把 `MismatchShowing` 与 `WaitingSecondCard` 混为一谈 | `PetFlipHandState` 显式区分 `WaitingSecondCard` 与 `MismatchShowing`；失配态点击任一未配对牌（含原两张之一）均合法 |
| 终局面板重建或重复读取导致首次纪录被误算成追平纪录 | 每次读取终局时临时拿 `FlipCount` 与已保存的 `BestFlipCount` 重新比较 | 终局瞬间计算并缓存不可变 `PetFlipCompletionResult`，整局只结算一次、`OnRoundCompleted` 仅触发一次 |
| 战斗中叠加切后台，切回前台时误把游戏恢复成可操作 | 单一 `bool _isPaused` 无法表达多中断源 | `PetFlipInterruptFlags` 位掩码 + `_hostInterrupts \| _externalInterrupts`；仅当全部位清零才可交互 |
| `GameClient.Tick()` 覆盖 UI 设置的 `ForcedModal` / UI `Loading` 标志 | 宿主与 UI 读写同一份中断字段 | 宿主仅写 `_hostInterrupts`，UI 仅写 `_externalInterrupts`，合并视图为按位或 |
| 面板乱序重建（新面板先 `AttachView`，旧面板后 `OnDestroy`）把新面板误关 | 旧面板销毁无条件触发关闭/退订 | `AttachView` 颁发单调 `viewToken`；旧面板调用 `DetachView(oldToken)` 发现不匹配直接安全忽略 |
| 临时断线自动重连时 `GameClient.ResetConnectionState()` 暂时清零 `PlayerId` 导致误清盘 | 仅凭 `PlayerId == 0` 立即清盘 | `SyncHostState` 与 `DisconnectInternal` 区分 `_reconnecting` / `_redirecting` 与真正登出；重连/迁移期间仅挂起并保留 `_boardOwnerSession`，落回后核对 `(PlayerId, ZoneId)` 才决定续盘或清盘 |
| 首次保存失败后继续玩，第二局更差成绩把内存最佳覆盖或阻塞游玩 | 写入失败抛异常或未记录内存最佳与落盘状态差 | 写入异常全捕获不抛出；内存保留本次最佳 `BestFlipCount` 防止更差成绩覆写，同时标记 `PersistenceStatus = Unsaved` 并将 `PersistedBestFlipCount` 保持为已确认落盘值 |
| 非 Unity 进程（如 Roslyn 无头测试）构造 `GameClient` 调用 `PlayerPrefs` 抛安全异常 | 直接调用 Unity 外部托管桥 | `PlayerPrefsPetFlipBestRecordStore` 读写均包 `try/catch`，测试层通过 `IPetFlipBestRecordStore` 注入纯内存实现 |
| 正式开局误用固定测试牌序 | 生产构造函数开放固定牌组参数 | 默认构造函数与 `Attach()` 固定使用 Fisher-Yates 随机洗牌；固定牌组仅允许通过显式 `CreateForTesting(...)` 注入，且测试牌序仅放在 `Assets/Tests/` |

---

## 5. 步骤分析与执行步骤

1. **步骤 1（方案落盘）**：编写本设计文档 `Docs/design/pet-flip-minigame.md`。
2. **步骤 2（核心实现）**：在 `Assets/Scripts/Game/PetFlip/` 新增 `PetFlipTypes.cs`、`PetFlipBestRecordStore.cs`、`PlayerPrefsPetFlipBestRecordStore.cs`、`PetFlipBoardModel.cs`、`PetFlipClient.cs` 及其对应 Unity `.meta` 文件。
3. **步骤 3（生命周期挂接）**：在 `GameClient.cs`、`GameClient.Reconnect.cs`、`GameClient.RoleSwitch.cs` 与 `AppBootstrap.cs` 接入 `PetFlip` 创建、每帧零分配宿主状态同步、前后台/断线/登出/换角/销毁收敛。
4. **步骤 4（自动化测试与脚本）**：在 `Assets/Tests/EditMode/Tianyong/PetFlipClientTests.cs` 编写全场景 NUnit 单测（含完整固定验收样例与边界场景）及 `.meta`，并新增 `tools/run_pet_flip_tests.ps1` 离线编译执行脚本。
5. **步骤 5（门禁验证）**：运行 `tools/client_compile_check.ps1`、`tools/run_pet_flip_tests.ps1` 与 `tools/run_activity_tests.ps1`（回归确认无破坏），核对 `git status` / `git diff` 仅含本次改动且不执行 commit/push。

---

## 6. 给 UI 同事（Codex）的接口调用说明

### 6.1 获取实例
- **生产运行期**：优先使用 `AppBootstrap.Instance?.GameClient?.PetFlip`（或 `PetFlipClient.Instance`）。
- **独立 UI 预览 / EditMode 沙盒**：可直接构造 `var client = new PetFlipClient(new MyInMemoryStore()); client.NotifySessionRestored(10001, 1);` 驱动界面，不依赖网络与服务器。
- **固定牌面验收测试**：仅在测试中使用 `PetFlipClient.CreateForTesting(store, () => testDeck)`；正式代码请始终使用默认随机洗牌实例。

### 6.2 推荐 UI 生命周期接入流程
1. **面板创建 / 绑定（`Awake` / `OnEnable`）**：
   ```csharp
   _viewToken = _petFlip.AttachView(RenderSnapshot);
   RenderSnapshot(_petFlip.GetSnapshot());
   ```
   > 若面板被重建，新面板调用 `AttachView` 会自动失效旧面板的 `_viewToken` 并替换回调，避免重复订阅和双盘竞争。
2. **点击入口打开 / 恢复一局**：
   ```csharp
   PetFlipActionResult result = _petFlip.StartOrResume(_viewToken);
   // result.Accepted == true：成功开新盘（StartedNewRound）或恢复既有盘面（ResumedRound）
   // result.Accepted == false：按 result.RejectReason 提示（如 NoActiveSession / BlockedByInterrupt）
   ```
3. **点击牌位 `0..11`**：
   ```csharp
   var currentRoundId = _petFlip.GetSnapshot().RoundId;
   PetFlipActionResult result = _petFlip.FlipCard(slotIndex, expectedRoundId: currentRoundId, viewToken: _viewToken);
   ```
   - 逻辑已同步生效，无需等翻牌动画结束再回调计次。
   - 根据 `result.Outcome` 播放对应反馈：
     - `FirstCardFlipped`：翻开 `result.FlippedSlotIndex`。
     - `MismatchClearedAndFirstCardFlipped`：盖回 `result.CoveredFirstSlotIndex` 与 `result.CoveredSecondSlotIndex`，并翻开 `result.FlippedSlotIndex`。
     - `PairMatched`：翻开 `result.FlippedSlotIndex`，高亮 `result.FirstHandSlotIndex` 与 `result.SecondHandSlotIndex` 已配对。
     - `PairMismatched`：翻开 `result.FlippedSlotIndex`，保持两张亮起不自动盖回。
     - `RoundCompleted`：同配对成功并弹出终局结果（取 `result.Snapshot.CompletionResult` 与 `result.Snapshot.LocalRecord`）。
     - `Rejected`：忽略或轻提示，盘面与次数未变化。
4. **终局后点击“再玩一局”**：
   ```csharp
   PetFlipActionResult result = _petFlip.StartNewRound(expectedRoundId: currentRoundId, viewToken: _viewToken);
   ```
   - 仅当 `snapshot.CanStartNewRound == true`（即 `Phase == Completed` 且无中断）时允许调用；盘中无重新洗牌按钮，未完成调用将被 `RoundNotCompleted` 拒绝。
5. **暂时关闭面板（点关闭按钮或 `CityPanelRegistry.HideOthers`）**：
   ```csharp
   _petFlip.Pause(PetFlipInterruptFlags.PanelClosed, _viewToken);
   ```
   - 盘面保留在内存中；下次点入口调用 `StartOrResume(_viewToken)` 即继续原盘。
6. **战斗 / 观战 / 加载 / 强制弹窗优先收起**：
   - `GameClient` 已自动同步 `Battle`、`Spectate`、`Loading`（重定向/传送/等入场）、`Disconnected` 与 `ApplicationBackground`。
   - UI 独有的强制弹窗（实名窗、防沉迷提示、账号注销确认、断线强弹窗）或 UI 级加载遮罩，由 UI 调用：
     ```csharp
     _petFlip.SetInterrupt(PetFlipInterruptFlags.ForcedModal, hasForcedModal);
     _petFlip.SetInterrupt(PetFlipInterruptFlags.Loading, hasUiLoadingOverlay);
     ```
   - 在 `RenderSnapshot(PetFlipSnapshot snapshot)` 中检查 `snapshot.ShouldCollapsePanel`：为 `true` 时立即隐藏翻牌面板（若是被战斗/观战/加载/强制弹窗打断而收起，建议顺手调 `_petFlip.Pause(PetFlipInterruptFlags.PanelClosed, _viewToken)`，这样战斗结束后不会未经玩家点击自动弹出）。
   - 检查 `snapshot.IsDisconnected`：为 `true` 时禁止交互（如展示“连接中断，等待恢复…”）；若同角色恢复，`IsDisconnected` 自动变回 `false` 可继续玩；若重连失败或登出，`snapshot.HasBoard` 变为 `false` 且 `ShouldCollapsePanel` 为 `true`。
7. **面板销毁（`OnDestroy`）与进程销毁的区别**：
   - **UI 面板销毁 / 重建**：只调 `_petFlip.DetachView(_viewToken, pausePanel: true)`，**绝不要调 `_petFlip.Dispose()`**！这样进程内同角色仍保留盘面，重建后可无缝续盘。
   - **进程 / 宿主销毁**：由 `AppBootstrap.OnDestroy()` 统一调用 `GameClient.PetFlip?.Dispose()`，释放内存盘面与事件监听（不清除已落盘本机最佳）。
8. **图案资源映射**：
   - 逻辑只输出 `SpiritPetCardPattern`（`A`、`B`、`C`、`D`、`E`、`F`）共 6 种枚举；背面牌 `VisiblePattern == null`。UI/美术自由映射 6 种枚举到 6 套灵宠卡牌图，不绑定真实养成宠物 ID。

---

## 7. 关键状态样例数据（供 UI 渲染与联调）

> 以下样例对应固定验收盘 `[A, B, C, D, E, F, A, B, C, D, E, F]`，展示各关键节点下 `PetFlipActionResult` / `PetFlipSnapshot` 的真实字段值（序列化为 JSON 示意）。

### 7.1 入口（首次开局，无旧本机纪录）
调用：`StartOrResume(viewToken: 1)`
```json
{
  "Accepted": true,
  "Outcome": "StartedNewRound",
  "RejectReason": "None",
  "Snapshot": {
    "Revision": 2,
    "RoundId": 1,
    "Session": { "PlayerId": 10001, "ZoneId": 1 },
    "BoardOwnerSession": { "PlayerId": 10001, "ZoneId": 1 },
    "HasActiveSession": true,
    "HasBoard": true,
    "Phase": "Playing",
    "HandState": "WaitingFirstCard",
    "ActiveInterrupts": "None",
    "CanInteract": true,
    "CanStartNewRound": false,
    "ShouldCollapsePanel": false,
    "IsPaused": false,
    "IsDisconnected": false,
    "FlipCount": 0,
    "MatchedPairCount": 0,
    "RemainingPairCount": 6,
    "FirstSelectedSlotIndex": -1,
    "SecondSelectedSlotIndex": -1,
    "Cards": [
      { "SlotIndex": 0, "State": "FaceDown", "VisiblePattern": null, "IsClickable": true },
      { "SlotIndex": 1, "State": "FaceDown", "VisiblePattern": null, "IsClickable": true }
    ],
    "LocalRecord": {
      "HasBestRecord": false,
      "BestFlipCount": null,
      "PersistedBestFlipCount": null,
      "PersistenceStatus": "NoRecord",
      "IsUnsaved": false
    },
    "CompletionResult": null
  }
}
```
（注：`Cards` 固定长度为 12，槽位 `0..11` 均为 `FaceDown`、`VisiblePattern: null`、`IsClickable: true`。）

### 7.2 首翻（点击位置 `0`）
调用：`FlipCard(0, expectedRoundId: 1, viewToken: 1)`
```json
{
  "Accepted": true,
  "Outcome": "FirstCardFlipped",
  "RejectReason": "None",
  "FlippedSlotIndex": 0,
  "FirstHandSlotIndex": 0,
  "SecondHandSlotIndex": -1,
  "CoveredFirstSlotIndex": -1,
  "CoveredSecondSlotIndex": -1,
  "FlippedPattern": "A",
  "MatchedPattern": null,
  "Snapshot": {
    "Revision": 3,
    "RoundId": 1,
    "Phase": "Playing",
    "HandState": "WaitingSecondCard",
    "FlipCount": 0,
    "MatchedPairCount": 0,
    "RemainingPairCount": 6,
    "FirstSelectedSlotIndex": 0,
    "SecondSelectedSlotIndex": -1,
    "Cards[0]": { "SlotIndex": 0, "State": "FaceUp", "VisiblePattern": "A", "IsClickable": false },
    "Cards[1]": { "SlotIndex": 1, "State": "FaceDown", "VisiblePattern": null, "IsClickable": true }
  }
}
```

### 7.3 失配（接着点击位置 `1`）
调用：`FlipCard(1, expectedRoundId: 1, viewToken: 1)`
```json
{
  "Accepted": true,
  "Outcome": "PairMismatched",
  "RejectReason": "None",
  "FlippedSlotIndex": 1,
  "FirstHandSlotIndex": 0,
  "SecondHandSlotIndex": 1,
  "CoveredFirstSlotIndex": -1,
  "CoveredSecondSlotIndex": -1,
  "FlippedPattern": "B",
  "MatchedPattern": null,
  "Snapshot": {
    "Revision": 4,
    "RoundId": 1,
    "Phase": "Playing",
    "HandState": "MismatchShowing",
    "FlipCount": 1,
    "MatchedPairCount": 0,
    "RemainingPairCount": 6,
    "FirstSelectedSlotIndex": 0,
    "SecondSelectedSlotIndex": 1,
    "Cards[0]": { "SlotIndex": 0, "State": "FaceUp", "VisiblePattern": "A", "IsClickable": true },
    "Cards[1]": { "SlotIndex": 1, "State": "FaceUp", "VisiblePattern": "B", "IsClickable": true },
    "Cards[6]": { "SlotIndex": 6, "State": "FaceDown", "VisiblePattern": null, "IsClickable": true }
  }
}
```
> 接着点击位置 `6`：`Outcome = "MismatchClearedAndFirstCardFlipped"`，`CoveredFirstSlotIndex = 0`，`CoveredSecondSlotIndex = 1`，`FlippedSlotIndex = 6`，`FlippedPattern = "A"`，`FlipCount` 仍为 `1`，`Cards[0]` 与 `Cards[1]` 回到 `FaceDown`，仅 `Cards[6]` 为 `FaceUp`。

### 7.4 配中（再点击位置 `0`，与 `6` 配成一对 `A`）
调用：`FlipCard(0, expectedRoundId: 1, viewToken: 1)`
```json
{
  "Accepted": true,
  "Outcome": "PairMatched",
  "RejectReason": "None",
  "FlippedSlotIndex": 0,
  "FirstHandSlotIndex": 6,
  "SecondHandSlotIndex": 0,
  "CoveredFirstSlotIndex": -1,
  "CoveredSecondSlotIndex": -1,
  "FlippedPattern": "A",
  "MatchedPattern": "A",
  "Snapshot": {
    "Revision": 6,
    "RoundId": 1,
    "Phase": "Playing",
    "HandState": "WaitingFirstCard",
    "FlipCount": 2,
    "MatchedPairCount": 1,
    "RemainingPairCount": 5,
    "FirstSelectedSlotIndex": -1,
    "SecondSelectedSlotIndex": -1,
    "Cards[0]": { "SlotIndex": 0, "State": "Matched", "VisiblePattern": "A", "IsClickable": false },
    "Cards[6]": { "SlotIndex": 6, "State": "Matched", "VisiblePattern": "A", "IsClickable": false }
  }
}
```

### 7.5 完成（配齐 6 对，首次纪录且保存成功）
调用：新盘依次点击 `0, 6, 1, 7, 2, 8, 3, 9, 4, 10, 5, 11` 的最后一击 `FlipCard(11)`
```json
{
  "Accepted": true,
  "Outcome": "RoundCompleted",
  "RejectReason": "None",
  "FlippedSlotIndex": 11,
  "FirstHandSlotIndex": 5,
  "SecondHandSlotIndex": 11,
  "FlippedPattern": "F",
  "MatchedPattern": "F",
  "Snapshot": {
    "Revision": 14,
    "RoundId": 2,
    "Phase": "Completed",
    "HandState": "None",
    "CanInteract": false,
    "CanStartNewRound": true,
    "ShouldCollapsePanel": false,
    "FlipCount": 6,
    "MatchedPairCount": 6,
    "RemainingPairCount": 0,
    "LocalRecord": {
      "HasBestRecord": true,
      "BestFlipCount": 6,
      "PersistedBestFlipCount": 6,
      "PersistenceStatus": "Saved",
      "IsUnsaved": false
    },
    "CompletionResult": {
      "RoundId": 2,
      "FlipCount": 6,
      "MatchedPairCount": 6,
      "PreviousBestFlipCount": null,
      "BestFlipCount": 6,
      "Comparison": "FirstRecord",
      "RecordPersistenceStatus": "Saved",
      "IsRecordUnsaved": false
    }
  }
}
```

### 7.6 暂停（战斗优先打断或暂关面板）
调用：`SetInterrupt(PetFlipInterruptFlags.Battle, true)`（或宿主进入战斗自动同步）
```json
{
  "Revision": 7,
  "RoundId": 1,
  "Phase": "Paused",
  "HandState": "WaitingFirstCard",
  "ActiveInterrupts": "Battle",
  "CanInteract": false,
  "CanStartNewRound": false,
  "ShouldCollapsePanel": true,
  "IsPaused": true,
  "IsDisconnected": false,
  "FlipCount": 2,
  "MatchedPairCount": 1,
  "RemainingPairCount": 5,
  "Cards[0]": { "SlotIndex": 0, "State": "Matched", "VisiblePattern": "A", "IsClickable": false },
  "Cards[1]": { "SlotIndex": 1, "State": "FaceDown", "VisiblePattern": null, "IsClickable": false }
}
```
> 暂停期间所有 `Cards[*].IsClickable` 为 `false`，`FlipCard` 返回 `Outcome = "Rejected"`、`RejectReason = "BlockedByInterrupt"`（暂关面板/切后台时为 `"GamePaused"`），盘面与次数原样保留。

### 7.7 保存失败（刷新/首创本机最佳时写入失败，但不阻止继续玩）
场景：旧本机最佳为 `9`（已保存），本局以 `6` 次完成，持久化存储返回写入失败。
```json
{
  "Accepted": true,
  "Outcome": "RoundCompleted",
  "RejectReason": "None",
  "Snapshot": {
    "RoundId": 3,
    "Phase": "Completed",
    "CanInteract": false,
    "CanStartNewRound": true,
    "FlipCount": 6,
    "MatchedPairCount": 6,
    "LocalRecord": {
      "HasBestRecord": true,
      "BestFlipCount": 6,
      "PersistedBestFlipCount": 9,
      "PersistenceStatus": "Unsaved",
      "IsUnsaved": true
    },
    "CompletionResult": {
      "RoundId": 3,
      "FlipCount": 6,
      "MatchedPairCount": 6,
      "PreviousBestFlipCount": 9,
      "BestFlipCount": 6,
      "Comparison": "NewBest",
      "RecordPersistenceStatus": "Unsaved",
      "IsRecordUnsaved": true
    }
  }
}
```
> UI 依据 `LocalRecord.IsUnsaved == true`（或 `CompletionResult.IsRecordUnsaved == true`）展示“本机纪录未保存”提示；此时 `Snapshot.CanStartNewRound` 仍为 `true`，玩家可立即点击“再玩一局”不受任何阻塞。

---

## 8. 正式 UI 与美术接入记录（2026-11-25）

### 8.1 已导入资源与 SHA-256 锁定
来源：`D:\work\image\designs\pet-memory-ui-v1\assets\`（提交 `ccb5dfe8ac2eda18d49df2b8d3489e5cd5c29ecb`）导入至 `Assets/Resources/UI/Ugui/PetFlipV1/`（不导入 `previews/` 整图遮罩）：

| 文件 | 原生尺寸 | SHA-256 | 用途 |
|---|---:|---|---|
| `window-frame.png` | 1922×818 | `15a7bd09e828d29aa67dceb0b54681ff232b039e9365e0aa24fe458bb722c6f9` | 横屏窗体无字底图 |
| `window-frame-portrait.png` | 1024×1536 | `7641c2a58604f8b6e8897f0e32f47c0978fa433d6dac883cfcaf8afb0947eaaa` | 窄竖屏窗体无字底图 |
| `card-back.png` | 870×1158 | `55c37fd6bd8988321391d102a7ea24fbac30382675f065f596d327dfa2e43553` | 12 张暗牌共用牌背与入口图标 |
| `card-front.png` | 870×1158 | `10f0af1c857cdf4a44cd0b07a43e6a5eb9cc6f69742697a599098c5abf7927fa` | 明牌共用空白肖像框 |
| `matched-mark.png` | 72×72 | `5d4c8a27e078ffec1f34fe96a8a4b4c8cf2cbc4ba959d0ce7d9c89f58932d887` | 已配对玉绿金边勾标 |
| `button-primary.png` | 300×90 | `9f6245d2ca5fec91fcdfdf32ca76abbbe9a8c721f533847af78ac71d54f14e30` | 主按钮备用素材 |
| `button-secondary.png` | 292×90 | `c18540a95a2e37739aae2b15575985a77c9e02d6af45187d51b6f54259c70bf2` | 副按钮备用素材 |

6 种灵宠头像与显示名直接复用客户端权威目录 `QdaoPetCatalog`（`Assets/Resources/World/Pets/QdaoPets20261005/`）：
`A -> 01-zhuling（烛翎）`、`B -> 03-shuangtuan（霜团貂）`、`C -> 04-guideng（桂灯童）`、`D -> 08-zhufengli（竹风狸）`、`E -> 10-xuanchaogui（玄潮龟）`、`F -> 11-xiajiaolu（霞角鹿）`。

### 8.2 客户端 UI 代码结构
- `Assets/Scripts/UI/Ugui/PetFlip/PetFlipUiPresentation.cs`：纯 C# 展示推导层（页面路由、四类终局文案、失配/断线/未保存提示、资源防缺失与防同图占位校验、横竖屏 12 槽几何计算与无障碍命名）。
- `Assets/Scripts/UI/Ugui/PetFlip/PetFlipArtCatalog.cs`：`PetFlipV1` 精灵与 `QdaoPetCatalog` 头像统一加载、校验及圆遮罩/圆角便签生成。
- `Assets/Scripts/UI/Ugui/PetFlip/PetFlipPanelView.cs`：严格按 `1922×818`（4×3）与 `1024×1536`（3×4）坐标排布五种页面态与 12 张牌位，使用 `RealtimeTween` 做轻巧翻开/配对弹跳反馈，动画不决定任何计次、配对或保存逻辑。
- `Assets/Scripts/UI/Ugui/PetFlip/PetFlipUiRoot.cs`：主城自举根宿主（`CanvasSortingOrder = 186`），向 `WorldHudNavigation` 的「历练」分类注册直接入口 `WorldHudEntry.PetFlip`（无红点），通过 `AttachView` / `DetachView` 绑定长寿命 `PetFlipClient`，接入 `CityPanelRegistry`、`EquipmentUiFit` 安全区适配及战斗/观战/强制弹窗优先收起。

### 8.3 验证分层边界
- **样例预览完成**：`D:\work\image\designs\pet-memory-ui-v1\previews\01-entry.png`～`06-narrow-board.png`（设计稿参考）。
- **真实逻辑已接入**：`PetFlipUiRoot` 与 `PetFlipPanelView` 全部绑定 `GameClient.PetFlip`，离线 Roslyn 编译与纯逻辑/UI 展示单测已通过。
- **实际平台已验证**：尚未在 Unity 编辑器 PlayMode 截图逐像素核验、iOS/Android App 真机与微信/抖音小游戏宿主实测；需在 Unity 运行环境完成端到端截图与交互验收后才可标记平台验证完成。
