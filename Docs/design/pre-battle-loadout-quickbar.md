# 战前准备期一键换流派（方案 A · 零新图复用 `tab_loadout_*` 与 `SwitchEquipLoadout`）设计文档

> 日期：2026-11-26（同日同步升级至方案 A 三合一 7 套共享流派席位）  
> 状态：已落地并升级至 1..7 统一席位（装备 + 战法 + 加点共享同一份 7 套权限：创角免费 2 套 + 85 级满级 +1 套 + 轻享卡 +2 套 + 按月按套租用 30 灵石/套/30 天，到期只锁不删；零新图，复用已落盘 `EquipLoadoutV1` 与 `SkillBuildLoadoutV1` 透明切片）  
> 关联上位设计：服务端仓 `docs/planning/equipment-loadout-presets-v1.md`（V2 方案 A）、`docs/planning/skill-build-loadout-7slots-v1.md`  
> 适用仓库：`mmorpg-client`（同步服务端 `EquipLoadoutSnapshot` 1..7 统一席位投影）

---

## 1. 需求分析

### 1.1 背景与痛点
装备流派方案预设 V1 已在服务端与客户端 `EquipmentWindow` 完整闭环（1..5 套 8 槽装备引用 + 0 空格原位原子互换 + 可选联动战法方案与加点方案 + 付费/绑灵/扩容符解锁）。  
然而玩家在实战备战场景中仍需退出当前玩法界面、重新打开装备界面再切换方案，割裂了最核心的“看机制/看灵脉/看对手规则后临场换整套流派”的策略爽感：
- **群仙问道赛**：BO3 局间换阵倒计时里看到对手战队与下一局生效五行灵脉，或首局开战前禁完灵脉看到 3 局序列、确认出战阵容、发起热身约战时，想直接点 1..5 快捷条切抗性/控场/输出套；
- **高难副本（首领试炼 · 备战 / 五行幻境 10 层爬塔）**：看到本周首领试炼五行词缀与首领反制提示，或看到五行幻境当前层五行灵脉时，想直接点 1..5 切换整套方案再开战；
- **论剑与切磋**：在正式人宠会武大厅（含排队等待期）看到本周赛季词缀，或在道友切磋坊发起自养约战 / 收到自养切磋邀请倒计时弹窗看到对手与自定义规则（物法易势/禁控/速决/3 禁招）时，想在倒计时内直接一键换装再应战。

### 1.2 边界与范围（方案 A）
- **覆盖全部进房前与局间倒计时备战入口（`!IsInBattle`）**：
  1. `TournamentWindow`：BO3 局间换阵倒计时弹层（`IntermissionSwap`）、BO3 灵脉禁选弹层（`BanLeyline`）、开战确认出战阵容弹层（`ConfirmLineup` 且 `LineupModalIsForMatchStart == true`）、赛前热身约战弹层（`WarmupSpar`）、赛事总览我的战队行动卡（非封榜期）；
  2. `BossTrialPrepPanel`：首领试炼备战界面（位于奖励规则下方、左侧安全区）；
  3. `WuxingTowerPanel`：五行幻境十层爬塔面板（位于已铭刻符印区与开战按钮之间）；
  4. `FormalPvpLobbyPanel`：正式人宠会武大厅（空闲与排队等待期均可切，匹配成局进入 `BattlePhase.Preparing` 自动锁死）；
  5. `CustomDuelChallengePanel`（关闭公平 3×3 免费模板的自养约战态）与 `BattleChallengePopup`（收到自养切磋邀请的应答倒计时弹窗）。
- **明确不纳入本次范围**：
  - 不修改任何 `.proto` / 配置表 / 服务端逻辑，不涉及进入战斗房间后的局内 15s/25s `BattlePrepState`（此时 `scene` 已挂 `InBattleComp` 冻结 `SwitchEquipLoadout` 且快照已发往 `battle` 节点）；
  - 不新增美术切图，零新图直接复用 `Assets/Resources/UI/Ugui/EquipLoadoutV1/tab_loadout_{active,unlocked,locked}.png`（260×64，横向三段 9-slice `border=(32,0,32,0)`）。

---

## 2. 设计分析

### 2.1 三层解耦架构（遵守 `Docs/rules/engineering-principles.md`）
1. **通信协调层（已有，零新增 RPC）**：继续复用 `PlayerFeaturesClient.RequestEquipLoadouts()` 与 `PlayerFeaturesClient.SwitchEquipLoadout(schemeId, applyLinkedSchemes: true)`；切换成功后 `GameClient` 既有的 `OnEquipLoadoutSwitched` 监听会自动同步刷新属性面板与技能列表。
2. **纯 C# 领域 / 视图模型层（扩展 `EquipLoadoutModel.cs`，零 `UnityEngine` 依赖）**：
   - 新增不可变快照模型 `PreBattleLoadoutBarState` 与 `PreBattleLoadoutSlotView`（显式携带 `SchemeId = 1..5`，不靠数组下标承载业务编号）；
   - 新增 `BuildPreBattleBarState(...)`：统一推导同步态、加载态、切换中、待确认解锁阻塞、入战锁定态（仅 `BattlePhase.None` / `BattlePhase.Queued` 可切）、缺件提示、各槽位精灵键与点击动作类型（`None` / `Switch` / `UnlockHint` / `Refresh`）；
   - 将 `BuildSwitchToast(SwitchEquipLoadoutResponse)` 收口为单一真相点（同时供 `EquipmentWindow`、`CoreGameplayUiRoot`、`TournamentUiRoot`、`BattleUiRoot` 复用）；
   - 新增 `FormatLockedPreBattleHint(uint schemeId, EquipLoadoutSnapshot)`（在备战/倒计时中点未解锁套位仅轻提示引导去装备界面解锁，不强行关闭当前倒计时窗口）；
   - 新增 `ComputePreBattleBarSignature(...)`，供 `TournamentWindow.SyncModalVisual` 仅在流派状态真实变更时才重建弹窗，避免打断 `TMP_InputField` 输入。
3. **共享表现组件（新增 `PreBattleLoadoutQuickBar.cs`）**：
   - 统一渲染横排 1..5 `tab_loadout_*` 快捷条（支持紧凑单行与标准双行布局），自动调用 `SeasonEconomyUiArt.ApplyHorizontalSlice` 保持 9-slice 左右端头比例不挤压，缺图安全回退纯色底；
   - 所有动态名称与状态文本强制 `richText = false`；不依赖鼠标 `Hover`；触控高度 ≥ 40px；严格落在各界面既定安全区内（含 `Screen.safeArea` 与小游戏右上 140px 胶囊避让）。

### 2.2 各界面布局位置（零重叠、零遮挡、零拉伸固定背景）
| 界面 | 设计画布 | 快捷条矩形 `(x, y, w, h)` | 显示时机与交互说明 |
|---|---|---|---|
| `BossTrialPrepPanel` | 2560×1080 | `(286, 890, 1422, 62)` | 常驻显示于奖励规则下方、`EnterTrial` 左侧；支持单行 5 签 + 标题/状态 |
| `WuxingTowerPanel` | 2560×1080 | `(808, 762, 852, 58)` | 位于中部卡片符印区（底 `748`）与操作钮（顶 `830`）之间 |
| `FormalPvpLobbyPanel` | 2560×1080 | `(760, 478, 1520, 56)` | 与「选择匹配规模」同行右排，空闲与 `Queued` 排队中可切，`Preparing` 自动置灰 |
| `CustomDuelChallengePanel` | 2560×1080 | `(480, 646, 1600, 74)` | 仅关闭「公平 3×3 免费模板」时显示（复用免费预设行空位 `634..746`） |
| `BattleChallengePopup` | 2560×1080（弹窗 760×500） | `(36, 232, 688, 122)` | 仅收到非免费模板的自养切磋邀请时显示（复用免费预设行空位 `228..360`） |
| `TournamentWindow` · BO3 局间换阵 | 1920×1080（固定底图 760×540） | `(56, 356, 648, 56)` | 保持 `v2-a-intermission-panel` 760×540 原尺寸不拉伸；略微收紧 2×2 阵容卡行高至 48px，腾出紧凑单行快捷条；队长与全体队员均可切换个人流派 |
| `TournamentWindow` · BO3 灵脉禁选 | 1920×1080（9-slice 680×536） | `(40, 392, 600, 62)` | 紧接 3 局灵脉序列预览下方 |
| `TournamentWindow` · 确认出战阵容 | 1920×1080（9-slice 640×动态） | `(48, modalH-188, 544, 62)` | 仅 `LineupModalIsForMatchStart == true` 显示 |
| `TournamentWindow` · 赛前热身约战 | 1920×1080（9-slice 720×626） | `(52, 446, 616, 62)` | 紧接热身首发阵容区下方 |
| `TournamentWindow` · 赛事总览行动卡 | 1920×1080（行动卡宽 596） | `(24, 416, 548, 60)` | 有战队且非封榜期时置于主行动按钮上方空位 |

---

## 3. 思想分析

1. **最小完备增量（KISS / DRY）**：不引入新协议与新状态容器，将 6 处业务界面的 1..5 快捷换装逻辑全部收口到「1 个纯 C# 推导模型 + 1 个无状态 uGUI 渲染器」，保证所有入口对忙碌态、入战锁定态、缺件提示、800ms 节流错误与联动战法/加点完全同口径。
2. **严格遵守 Actor / 事件边界**：`TournamentWindow` 不直接持有 `PlayerFeaturesClient`，所有状态经不可变 `PreBattleLoadoutBarState` 向下输入、所有操作经事件回抛给 `TournamentUiRoot` 执行；`CoreGameplayUiRoot`、`TournamentUiRoot`、`BattleUiRoot` 均做到订阅与退订严格对称。
3. **倒计时防打断体验**：在 BO3 局间倒计时或切磋应答倒计时中点击未解锁套位，绝不强行关闭当前备战弹窗跳转装备界面，而是温和 Toast 提示前往「装备 → 流派方案」解锁，杜绝误关错过倒计时或开战时机。

---

## 4. 漏洞分析与防线

| # | 潜在风险 / 边界 | 后果 | 防护措施 |
|---|---|---|---|
| 1 | **匹配成局 `BattlePhase.Preparing` 或已进战斗后仍点击快捷条** | 触发 `kEquipInBattle` 报错或状态错乱 | `EquipLoadoutModel.IsPreBattleSwitchAllowed(phase)` 仅放行 `None` / `Queued`；其余相位自动置灰禁用并提示“即将进入战局，已锁定换装”；服务端 `CheckWritable` 仍作最终权威兜底。 |
| 2 | **首次打开备战面板时从未打开过装备界面导致 `EquipLoadouts == null`** | 快捷条长期空白或无法切换 | `CoreGameplayUiRoot.Bind`、各面板 `Show()`、`TournamentUiRoot.Open()` 及 `BattleChallengePopup.Show()` 均在快照为空且非加载中时自动补拉 `RequestEquipLoadouts()`，并在加载失败时提供“刷新方案”按钮。 |
| 3 | **切号、断线重连或窗口销毁后 `OnEquipLoadoutSwitched` / `OnChanged` 回调命中死对象或串号** | `MissingReferenceException` 或旧角色状态污染新角色 | `CoreGameplayUiRoot`、`TournamentUiRoot`、`BattleUiRoot` 均在 `Unbind` / `OnDisable` / `OnDestroy` 对称 `-=` 退订，且 `PlayerFeaturesClient` 内部已有 `(epoch, playerId)` 世代拦截。 |
| 4 | **`TournamentWindow` 弹窗内 `Features.OnChanged` 触发全量重建打断 `WarmupSpar` 输入框** | 玩家输入目标战队编号时焦点丢失 | 仅当 `ComputePreBattleBarSignature` 实际变化时触发弹窗重建，且输入草稿已实时同步至 `_model.WarmupTargetSquadIdDraft`。 |
| 5 | **玩家自定义方案名含 `<color>` 等富文本标签** | 快捷条排版错乱或文字注入 | 所有快捷条标签与状态文本强制 `richText = false` 并开启单行裁剪/自动缩放。 |
| 6 | **在免费 3×3 公平模板下误展示个人装备流派快捷条** | 误导玩家以为装备对公平模板生效 | `CustomDuelChallengePanel` 与 `BattleChallengePopup` 均按 `!_useFreePreset` 严格条件展示；娱乐场 `SeasonRotationPanel` 永不挂载。 |
| 7 | **WebGL2 小游戏 / IL2CPP 兼容性与触控盲区** | 小游戏卡死或手机端误触/遮挡胶囊 | 零托管线程/零 `Task` 阻塞，零 `Hover` 依赖，所有快捷条高度 ≥ 40px 且避让右上角 140px 胶囊区。 |

---

## 5. 步骤分析与执行步骤

1. **扩展纯 C# 模型 `Assets/Scripts/UI/Ugui/Gameplay/EquipLoadoutModel.cs`**：
   - 新增 `PreBattleLoadoutClickKind`、`PreBattleLoadoutSlotView`、`PreBattleLoadoutBarState`、`IsPreBattleSwitchAllowed`、`BuildPreBattleBarState`、`BuildSwitchToast`、`FormatLockedPreBattleHint`、`ComputePreBattleBarSignature`。
   - 让 `EquipmentWindow.Loadout.cs` 复用 `EquipLoadoutModel.BuildSwitchToast` 消除重复格式化逻辑。
2. **新增共享 uGUI 渲染器 `Assets/Scripts/UI/Ugui/Gameplay/PreBattleLoadoutQuickBar.cs`（同步新建唯一 GUID `.meta`）**：
   - 复用 `EquipLoadoutModel.TabActiveKey / TabUnlockedKey / TabLockedKey` 与 `SeasonEconomyUiArt.ApplyHorizontalSlice`，实现标准/紧凑两种排布与点击派发。
3. **接入全部 6 处战前准备界面与 3 处总根事件生命周期**：
   - `CoreGameplayUiRoot.cs`：补充自动首拉 `RequestEquipLoadouts()` 与对称订阅 `OnEquipLoadoutSwitched` 弹 Toast；
   - `BossTrialPrepPanel.cs`、`WuxingTowerPanel.cs`、`FormalPvpLobbyPanel.cs`、`CustomDuelChallengePanel.cs`：接入 `PreBattleLoadoutQuickBar` 并在 `Show()` 兜底拉取快照；
   - `TournamentUiRoot.cs` & `TournamentWindow.cs`：对称订阅 `PlayerFeaturesClient`，通过不可变 `PreBattleLoadoutBarState` 与回抛事件接入 BO3 局间换阵、灵脉禁选、开战确认阵容、热身约战与总览行动卡；
   - `BattleUiRoot.cs` & `BattleChallengePopup.cs`：对称订阅 `PlayerFeaturesClient`，在非公平自养切磋倒计时弹窗挂载快捷条并实时刷新与弹 Toast。
4. **补充 EditMode NUnit 单测并执行编译与测试门禁**：
   - 扩展 `Assets/Tests/EditMode/Battle/EquipLoadoutUiTests.cs`，覆盖纯模型推导（未同步/加载中/空闲/排队中可切/Preparing 锁定/忙碌/待确认解锁/缺件提示/未解锁提示/切换 Toast/签名稳定与变更）与 RPC 切换联动；
   - 运行 `pwsh -File tools/client_compile_check.ps1` 验证运行时程序集 0 报错；
   - 编译并反射运行 `EquipLoadoutUiTests`（含完整 EditMode 测试编译检查）留存真实运行证据。
