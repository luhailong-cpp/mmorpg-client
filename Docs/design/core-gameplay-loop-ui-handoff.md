# 《五行奇谈》核心玩法 V1（有空就玩）Unity UI 详细交接与设计规范（供 ChatGPT / Codex）

> 本文档严格对应已落地并通过验证的服务端（`xuanming-server-mmo-cpp` C++ / Go）与客户端纯 C# 逻辑层（`mmorpg-client` `MmorpgClient.Game.*`）。UI 层**严禁直接发包、严禁私自计算段位/材料/解锁条件、严禁手写错误码数字**，全部通过本文绑定的纯 C# 门面与只读视图驱动。

---

## 一、需求分析

1. **核心玩法定位（「有空就玩，永不清零」）**：
   - 玩家上线 10 分钟即可单人带灵狐打 1 场首领试炼或 1 场正式人宠会武；隔几天/几周回来从原处继续，无每日必清红点与回归补课清单。
   - 永久首套装备材料奖励 **660 份**（600 份造首套 + 60 份余量，含至多 60 份胜局/PVE 通关可交易），满 660 后自然进入阶段二（每周 100 份绑定精进池），正式评分、试炼志与赛季印始终正常累计。
2. **四大核心拍板规则（必须在 UI 明确传达）**：
   - **正式人宠会武（`PVP_TEAM`）= 本人真实养成**：进场使用真实 8 槽装备、+10/+11、词条、法宝、已学技能、赛季印与真实宝宝；**免费 3×3 预设严禁出现在正式会武页**，仅用于娱乐轮换（护卫救援 / 物法易势 5V5）。
   - **正式人宠会武五档规模开关化**：协议与 UI 完整支持 `1V1 / 2V2 / 3V3 / 4V4 / 5V5`。主力常驻为 `1V1 / 3V3 / 5V5`（冷启动默认仅开 `1V1 / 3V3` 保秒排，`5V5` 标「即将开放」），`2V2 / 4V4` 明确标注「特殊玩法」（按服务端开关启停），绝不前端写死开放态。
   - **免费人宠 PVP 仅限「物法易势 5V5」**：十人公平团战（Dungeon `110`），每 2 回合切换物势/法势（`+15%` 对应直伤），不计正式评分；护卫救援（Dungeon `109`）为 1–5 人免费人宠 PVE，同样不计正式评分。
   - **3 只不同机制首领 + 困难高回报 + PVP 负场防挂机**：首发 3 只首领（4 组普通/困难变体 `101..108`），普通胜 `+10`（含至多 1 可交易），困难胜 `+15`（基础 10 + 困难绑定加量 5，通关任意普通试炼一次全解锁）；PVP 负/平场须打满 ≥3 回合、≥2 回合手动且有有效战斗贡献才给 `+10` 全绑定，不达标 `+0` 且不扣永久 660 池，可交易份数仅给胜局与 PVE 通关。
   - **低打扰、零卖数值的小额消费位**：主城/养成页预留 4 个干净槽位（衣橱外观 3402、赛季手记/零数值外观里程碑、轻享省心卡与配装槽位 3→6、聚宝斋材料次要来源），首测实钱开关可控关闭（展示「首测预览 · 未开直充」），零战力售卖、零全屏首充弹窗。

---

## 二、设计分析

1. **数据流分层（单向数据流）**：
   - **网络与业务真相层（已由服务端 + 本机纯 C# 完成）**：
     - `CoreGameplayLoopClient`（`MmorpgClient.Game.CoreLoop`）：统一收口首领试炼、正式会武、护卫救援、物法易势 5V5、120 秒等待取消转单人试炼状态机及全套 UI 只读 ViewModel 构建。
     - `SeasonClient`（`MmorpgClient.Game.Season`，挂于 `GameClient.Season`）：RPC `453–455` 赛季技能/赛季印拉取、切换与幂等追赶。
     - `RankedClient`（`GameClient.Ranked`）、`TrialJournalClient`（`GameClient.TrialJournal`）、`BattleClient`（`GameClient.Battle`）、`PlayerFeaturesClient`（`GameClient.Features`）、`PetClient`（`GameClient.Pets`）、`AppearanceClient`、`AfkPassClient`。
   - **表现层（交由 ChatGPT / Codex 编写 Unity uGUI）**：
     - 仅订阅 `Changed` / `Notice` / `OnPhaseChanged` / `OnBattleEnd` / `OnChanged`，读取只读 ViewModel 渲染，并将按钮点击映射为门面方法调用。
2. **禁止在 UI 重复造轮子**：
   - 不得手写 `JoinQueueRequest`、不得缓存 `*TableManager` 行对象、不得用 `UnityEngine` 污染 `MmorpgClient.Game.*`、不得在 UI 里算 `660` 剩余量或猜 `HardUnlocked`（全部读 `CoreGameplayLoopModels` 与快照字段）。

---

## 三、思想分析

- **看得懂、选得清、打完就有收获**：把「为什么赢/输」直接前移到战前预览卡片（首领机制 + 反制提示）与战中公开预告（`BattleTelegraphEntry`）、物法易势横幅（`active_polarity`）与结算拆解（基础/加量、可交易/自用、反挂机未达标原因）。
- **克制信息密度**：主城只放 **3 个核心数字 + 4 个核心玩法/养成入口 + 4 个低打扰非数值小额消费位**，砍掉一切任务清单感与每日倒计时压迫。

---

## 四、漏洞分析与前端防线

| 风险 | 服务端与纯 C# 防线 | UI 层必须守住的约束 |
|---|---|---|
| 1. 正式会武误展示免费 3×3 预设 | `RankedProfileSnapshot.UsesOwnProgression == true`；`EnterFormalPvp` 固定传 `characterPresetId=0, petPresetId=0` | 正式人宠会武页**严禁渲染 3×3 预设选择卡**，改为展示当前本人穿戴装备/出战宝宝/赛季印摘要卡 |
| 2. 服务端动态切换开放规模后前端按钮错位 | `CoreGameplayLoopModels.BuildFormalPvpScales` 动态按服务端 `AllowedTeamSizes`/`PrimaryTeamSizes`/`SpecialTeamSizes` 归类 | 严禁硬编码只画 1V1/3V3；必须遍历 `BuildFormalPvpScales()` 5 项，按 `Status` 渲染开放/即将开放/特殊玩法标签并按 `CanEnterWithCurrentParty` 置灰 |
| 3. 120s 取消排队转单人试炼时刚好匹配成局 | `CoreGameplayLoopClient.CancelQueueAndSwitchToBossTrial` 监听 `BattleClient.OnPhaseChanged`：收到 `Preparing/WaitingAction` 立即终止切换回原局 | 点击「改打单人试炼」后按 `FallbackSwitchState` 展示「正在取消匹配…」禁用连点，收到 `ReturnedToMatchedBattle` 时收起弹窗并提示「匹配已成局，正在进入原定对局」 |
| 4. PVP 负场未达门槛误以为「系统吞奖」 | `BattleEndS2C.MaterialPvpLossIneligible == true`、`CoreSettlementRewardView.DetailNote` 明确说明门槛且不扣 660 | 结算页检测到 `view.PvpLossIneligible == true` 时用清晰提示条高亮显示 `view.Headline` 与 `view.DetailNote`，不得静默显示 `+0` |
| 5. 跨端与小游戏安全区遮挡 | 无 | 所有顶部导航、主城核心 3 数字条、关闭按钮、战斗回合倒计时必须避开 `Screen.safeArea` 与微信/抖音小游戏右上角胶囊区 |
| 6. WebGL2 / IL2CPP 崩溃 | 纯 C# 层零多线程、零动态反射 | UI 层严禁 `Task.Run` / `Thread` / `HttpClient` / `.Result` / `.Wait()`；新增文件与目录全部配齐 `.meta` |

---

## 五、步骤分析（给 ChatGPT / Codex 的建议落地顺序）

1. **挂载入口**：在 `AppBootstrap` / `GameplayUiRoot` 中创建并持有 `CoreGameplayLoopClient`（传入 `game.Battle`, `teamUiRoot.MatchEntry`, `game.Ranked`, `game.TrialJournal`, `game.Season`, `game.Features`），随会话销毁时 `Dispose()`。
2. **主城总览 HUD (`CoreMainCityHubPanel`)**：绑定 `core.BuildMainCityHub(...)`，画顶部 3 核心数字 + 4 玩法入口 + 底部/侧边 4 个低打扰小额消费位。
3. **三张核心玩法选局页**：
   - 首领试炼页 (`BossTrialPrepPanel`)：绑定 `core.BuildBossTrials()` + `core.EnterBossTrial(dungeonId)`。
   - 正式人宠会武页 (`FormalPvpLobbyPanel`)：绑定 `core.BuildFormalPvpScales()` + `core.EnterFormalPvp(size)` + 120 秒等待提示条（绑定 `core.CancelQueueAndSwitchToBossTrial()`）。
   - 首季轮换娱乐场页 (`SeasonRotationPanel`)：绑定 `core.BuildRotations()` + 3×3 免费人宠预设选择（读 `Ranked.Profile.CharacterPresets / PetPresets`）+ `core.EnterGuardRescue` / `core.EnterPolarity5v5`。
4. **战斗内核心 HUD 叠加层 (`BattleCoreHudOverlay`)**：绑定 `core.BuildBattleHud()`，渲染首领/护卫公开预告条、物法易势换势横幅、护卫救援保全徽标。
5. **通用结算奖励拆解块 (`CoreSettlementRewardSection`) + 赛季印抽屉 (`SeasonSealPanel`)**：绑定 `core.BuildLastSettlementReward()` 与 `game.Season`，串通材料自选领取 (`PlayerFeaturesClient`) 与 5 条可点击演示流。

---

## 六、给 ChatGPT / Codex 的完整可执行 UI 规范（可直接整段复制）

### 6.1 权威 C# API 绑定表（全量已存在且通过编译与单测）

> 命名空间：`MmorpgClient.Game.CoreLoop`、`MmorpgClient.Game.Season`、`MmorpgClient.Game.Ranked`、`MmorpgClient.Game.TrialJournal`、`MmorpgClient.Game.Battle`、`MmorpgClient.Game.PlayerFeatures`。

| 业务模块 | C# 类型 / 入口 | 核心读取属性 / 视图方法 | 核心写操作 / 意图方法 | 订阅事件 |
|---|---|---|---|---|
| **核心循环总门面** | `CoreGameplayLoopClient` | `BuildMainCityHub(paymentEnabled, hasAfkPass, unlockedLoadoutSlots)`<br>`BuildBossTrials()`<br>`BuildFormalPvpScales()`<br>`BuildRotations(activeSeasonWeek)`<br>`BuildBattleHud()`<br>`BuildLastSettlementReward()`<br>`FallbackSwitchState`, `Status` | `EnterBossTrial(uint dungeonId)`<br>`EnterFormalPvp(uint targetSize)`<br>`EnterGuardRescue(uint charPreset, uint petPreset)`<br>`EnterPolarity5v5(uint charPreset, uint petPreset)`<br>`CancelQueueAndSwitchToBossTrial(uint fallbackDungeonId = 101u)` | `Changed`<br>`Notice` |
| **赛季技能与赛季印** | `GameClient.Season` (`SeasonClient.Instance`) | `Snapshot` (`SeasonStateSnapshot`: `SeasonId`, `SeasonName`, `EquippedSealId`, `ProgressPoints`, `MaxPoints`, `CurrentMaxHealthBps`, `CurrentDefenseBps`, `Seals`, `SeasonSkills`, `CanSwitchSeal`, `CanCatchup`, `CatchupRemainingTotalPrice`, `CurrencyBalance`)<br>`StatePending`, `ActionPending`, `Status`, `StatusIsError` | `RequestState(bool force = false)`<br>`EquipSeal(uint sealId)`<br>`CatchupSeal(uint pointsToBuy = 0u)` | `Changed`<br>`Notice`<br>`SealCatchupCompleted` |
| **正式会武档案与排位结算** | `GameClient.Ranked` (`RankedClient.Instance`) | `Profile` (`RankedProfileSnapshot`: `CurrentSeason`, `CurrentProgress`, `AllowedTeamSizes`, `PrimaryTeamSizes`, `SpecialTeamSizes`, `UsesOwnProgression`, `DefaultOpenTeamSize`, `IsTeamSizeOpen(size)`, `IsSpecialTeamSize(size)`, `CharacterPresets`, `PetPresets`)<br>`ActiveSettlement` (`MaterialCreditUnitsAdded`, `MaterialTradableUnitsAdded`, `MaterialPvpLossIneligible`, `AuthoritativeView`) | `RequestProfile(bool includeHistory = true)`<br>`SavePreset(uint charPreset, uint petPreset)`（仅娱乐轮换默认预设偏好）<br>`UpdateCosmetic(RankedCosmeticLoadout)`<br>`DismissSettlement()` | `Changed`<br>`SettlementOpened` |
| **五行试炼志 & 困难解锁** | `GameClient.TrialJournal` (`TrialJournalClient`) | `Snapshot` (`TrialJournalSnapshot`: `HardBossTrialUnlocked`, `Chapters`, `Milestones`, `BoundSandBalance`, `VoucherBalance`, `RedeemableArtifacts`)<br>`ClaimableMilestoneCount` | `RequestSnapshot(bool force = false)`<br>`ClaimMilestone(uint milestoneId)`<br>`RedeemArtifactVoucher(uint artifactItemConfigId)` | `Changed`<br>`Notice`<br>`MilestoneClaimed`<br>`ArtifactRedeemed` |
| **战斗状态与终局快照** | `GameClient.Battle` (`BattleClient.Instance`) | `Phase`, `State` (`BattleStateS2C`: `RoundIndex`, `Telegraphs`, `ActivePolarity`, `NextPolaritySwitchRound`, `ProtectedGuardActorId`), `LastBattleEnd` (`BattleEndS2C`), `LastQueueStatus` (`QueuedSeconds`, `WaitPromptThresholdSeconds`, `CurrentRatingGap`), `IsCancellingQueue`, `AutoBattleLatched` | `SubmitAction(...)`, `SetAutoBattle(bool)`, `CancelQueue()`, `AckTurnPlayed()`, `AckBattleEnd()` | `OnPhaseChanged`<br>`OnBattleStart`<br>`OnTurnResult`<br>`OnBattleEnd`<br>`OnQueueStatus`<br>`OnError` |
| **材料自选领取与装备打造** | `GameClient.Features` (`PlayerFeaturesClient.Instance`) | `EquipmentMaterialReward` (`EquipmentMaterialRewardSnapshot`: `LifetimeEarned`/`660`, `TradableEarnedTotal`/`60`, `AvailableUnits`, `AvailableTradableUnits`, `BatchUnits`, `BatchTradableUnits`, `WeeklyPhaseActive`, `EarnedThisWeek`/`100`, `CanClaim`) | `RequestEquipmentMaterialReward()`<br>`ClaimEquipmentMaterialReward(uint craftCount, uint stoneCount)` | `OnChanged`<br>`OnMaterialRewardClaimed` |
| **灵宠出战与 20 点条目** | `GameClient.Pets` (`PetClient.Instance`) | 既有宝宝列表与出战宠快照（新号进场景自动获得 1 只灵狐 `pet_table_id=1` 并自动出战） | 既有 `PetClient` 换出战/条目分配接口 | 既有 `PetClient` 事件 |
| **小额外观与托管卡** | `AppearanceUiRoot` / `AfkPassClient` | 10 元易容卡（`Item 3402`）1 小时试用与永久解锁；轻享挂机月卡 `Active` / `RemainingText` | 复用既有 `AppearanceUiRoot.ShowPanel()`、`AfkPassClient.Buy()` 与聚宝斋入口 | 既有事件 |

---

### 6.2 逐屏 UI 设计与交互细节

#### 屏 1：主城总览 HUD（`CoreMainCityHubPanel`）
- **布局原则**：东方志怪清雅水墨/玉青质感，低密度、无弹窗轰炸；顶部条必须避开 `Screen.safeArea` 与小游戏右上角胶囊。
- **顶部「3 个核心数字条」**（来自 `MainCityHubView`）：
  1. `MaterialProgressLabel` + `MaterialProgressValue`（如 `永久首套材料 · 120/660（待领 20）`，阶段二自动切换 `周精进绑定材料 · 30/100`）；`ClaimableMaterialUnits > 0` 时右侧挂柔和「去自选领取」胶囊按钮（不用刺眼抖动红点）。
  2. `FormalPvpLabel` + `FormalPvpValue`（如 `青锋 · 1540 分`）。
  3. `SeasonAndJournalLabel` + `SeasonAndJournalValue`（如 `破军印 40/100 · 试炼志可领 1`），点击打开赛季印与试炼志抽屉。
- **主入口区（4 张主玩法/养成入口按钮）**：
  - 「首领试炼（1–5 人）」→ 打开屏 2；
  - 「人宠会武（正式排位）」→ 打开屏 3；
  - 「首季轮换（免费人宠）」→ 打开屏 4；
  - 「装备 / 灵宠养成」→ 复用既有装备打造与灵宠面板。
- **底部/右侧低打扰「非数值小额消费与交易预留栏」**（遍历 `hub.SmallSpendSlots` 4 项）：
  1. `wardrobe`（易容衣橱与灵宠外观）：跳转 `AppearanceUiRoot.ShowPanel()`，卡片内带「零战斗数值」淡标签，后续可直接扩展灵宠外观/施法特效子页签；
  2. `season_journal`（赛季手记 · 外观里程碑）：跳转 `RankedUiRoot` 外观/试炼志里程碑页；
  3. `convenience_loadouts`（轻享省心卡与配装槽位）：展示 `AfkPassClient` 状态 + `配装方案 3/6 槽位` 预留位；
  4. `material_market`（聚宝斋 · 材料次要来源）：跳转交易行，文案明确标注「玩家自由交易，不扣减永久 660 免费奖励」。
  - 当 `slot.PaymentEnabled == false`（首测未开实钱支付）时，右上角角标显示 `首测预览 · 未开直充`，点击可正常预览全部外观与规则说明，但不弹出充值窗。

#### 屏 2：首领试炼备战页（`BossTrialPrepPanel`）
- **数据源**：`core.BuildBossTrials()` 返回 4 张卡片（覆盖 3 只不同机制首领，对应 Dungeon `101..108`）：
  1. **重甲首领 · 回春阵**（普通 `101` / 困难 `102`）：回春护卫回血 + 慢重劈预告；
  2. **重甲首领 · 护势阵**（普通 `103` / 困难 `104`）：物法护势轮换 + 慢重劈预告；
  3. **玄盾首领**（普通 `105` / 困难 `106`）：灵障护卫每两回合蓄盾 + 未破盾反震；
  4. **幽毒首领**（普通 `107` / 困难 `108`）：玄毒护卫叠毒 + 封灵控场。
- **每张卡必显信息**：
  - 首领头像（复用 `BattleArtCatalog` 怪物 `101..107`）、机制摘要 `CoreMechanicSummary`、一行反制提示 `CounterHint`。
  - 难度切换芯片：**普通（胜 +10 份，含至多 1 可交易）** / **困难（胜 +15 份 = 10 基础 + 5 困难绑定加量）**。
  - 若 `card.HardUnlocked == false`，困难芯片显示锁图标，hover/点击 toast 提示 `card.HardLockHint`（「通关任意一只普通首领试炼后开放困难难度」）；组队时文案补充「全队成员均须已解锁困难」。
- **单人/组队提示**：单人显示「单人带出战灵宠立即开战」，组队队长显示「按当前队伍 {N} 人立即开战（无需凑满 5 人）」。主按钮调用 `core.EnterBossTrial(selectedDungeonId)`。

#### 屏 3：正式人宠会武大厅（`FormalPvpLobbyPanel`）
- **核心识别横幅**：顶部明确标示「**正式排位 · 使用本人真实装备、技能、赛季印与出战宝宝**」；**严禁出现免费 3×3 预设卡片**，左侧改为展示本人当前装备平均强化/已装备赛季印/当前出战宝宝简卡（带「去配装/调宠」快捷跳转）。
- **5 档规模选择器**（遍历 `core.BuildFormalPvpScales()` 1V1–5V5）：
  - `Open`（冷启动 `1V1`、`3V3`）：高亮可选，徽标「常驻 · 开放中」；
  - `PrimaryUpcoming`（冷启动 `5V5`）：灰态不可点，徽标「即将开放」；
  - `SpecialModeClosed` / `SpecialModeOpen`（`2V2`、`4V4`）：独立虚线/异色边框徽标「特殊玩法 · 暂未开放」或「特殊玩法 · 开放中」；
  - 组队限制：当 `slot.CanEnterWithCurrentParty == false` 时按钮置灰并在下方直接展示 `slot.BlockedReason`（如 4 人队选 5V5 提示「再邀一位好友即可报名」）。
- **组队报名**：正式会武无需队员确认免费预设，队长直接选已开放且符合当前人数的档位点「开始匹配」→ `core.EnterFormalPvp(selectedSize)`。
- **120 秒等待兜底条**（驱动源：`Battle.Phase == Queued` 且 `Battle.LastQueueStatus.QueuedSeconds >= WaitPromptThresholdSeconds`）：
  - 不强踢队列、不补机器人；底部浮出轻量提示条：「已等待 mm:ss（当前分差 ±X），真人不足可继续等待或改打单人首领试炼」。
  - 两个并列按钮：**「继续等待」**（收起提示条）与 **「取消并去单人试炼」**（调用 `core.CancelQueueAndSwitchToBossTrial(101u)`）。
  - 展示 `core.FallbackSwitchState`：`Cancelling` 时按钮禁用显示「正在取消匹配…」；`ReturnedToMatchedBattle` 时自动关闭等待条并飘字「匹配已成局，正在返回原定对局…」。

#### 屏 4：首季轮换娱乐场页（`SeasonRotationPanel`）
- **数据源**：`core.BuildRotations(activeSeasonWeek)` 返回 2 张轮换卡 + 复用 `Ranked.Profile.CharacterPresets / PetPresets` 的免费 3×3 预设选区：
  1. **护卫救援（前 4 周 · PVE 1–5 人 · Dungeon `109`）**：免费 3×3 人宠，目标击败首领并保全回春护卫，不计正式评分，胜 `+10`（含至多 1 可交易）。点击调用 `core.EnterGuardRescue(charPresetId, petPresetId)`。
  2. **物法易势 5V5（后 4 周 · 仅 5V5 公平团战 · Dungeon `110`）**：明确标注「**仅限 5V5 · 免费 3×3 人宠 · 每 2 回合切换物势/法势 · 不计正式评分**」，胜 `+15`（含至多 1 可交易），有效负/平 `+10` 全绑定。点击调用 `core.EnterPolarity5v5(charPresetId, petPresetId)`（在队内时自动提示先离队单人报名）。
- **免费 3×3 预设选区**：仅在本屏展示！人物三选一（101 破阵 / 102 控场 / 103 守心）× 宝宝三选一（201 破甲金猊 / 202 灵咒灵狐 / 203 护主石灵），带醒目提示「免费试用能力仅在本娱乐局生效，不带入正式排位」。

#### 屏 5：战斗内核心 HUD 叠加层（`BattleCoreHudOverlay`）
- **数据源**：每次 `Battle.OnBattleStart` / `Battle.OnTurnResult` 时调用 `core.BuildBattleHud()`：
  - **公开预告浮条（`HasTelegraphs == true`）**：在顶部安全区下方展示 1–2 行紧凑预告牌（如 `[本回合发动] 重甲首领 -> 慢动作重劈（目标：主角）`、`[即将发动] 回春护卫 -> 回春治疗`）；若目标单位在场上可见，在目标头顶挂红色锁定小图标；若 `Status == 4` 显示「已受控打断」灰绿态反馈。
  - **物法易势横幅（`HasPolarity == true`）**：顶部居中显示 `hud.PolarityBannerText`（物势用暖金剑徽、法势用青蓝符徽，清晰标出下次换势回合号，交指令前可见）。
  - **护卫救援目标牌（`HasProtectedGuard == true`）**：顶部显示 `hud.ProtectedGuardBannerText`，并在 `ProtectedGuardActorId` 单位血条上方挂金色盾牌「须保全」角标。
- **宝宝三分类选招提示**：攻击 / 障碍 / 辅助为三个同级选招页签，底部小字固定提示「每回合人物与宝宝各行动 1 次，宝宝三分类合计施放 1 招」。

#### 屏 6：结算奖励拆解块（`CoreSettlementRewardSection`）与赛季印抽屉（`SeasonSealPanel`）
- **通用结算材料卡**（绑定 `core.BuildLastSettlementReward()`）：
  - 正常获得材料：主行显示 `view.Headline`（例如 `材料自选额度 +15（基础 10 + 加量 5）`），副行显示 `view.DetailNote`（明确拆出可交易与仅自用份数），并附阶段进度条（阶段一 `lifetime_earned/660` + `可交易 tradable_earned_total/60`；阶段二 `本周绑定 earned_this_week/100`）与「去自选领取（待领 {ClaimableUnits}）」快捷按钮。
  - **PVP 反挂机未达标（`view.PvpLossIneligible == true`）**：用琥珀色说明卡高亮显示 `view.Headline` 与 `view.DetailNote`，明确写清「需满 3 回合、至少 2 回合手动且有有效贡献；本次不扣除永久 660 额度」。
- **赛季印与赛季技能抽屉**（绑定 `GameClient.Season`）：
  - 展示 `Snapshot.SeasonName`、当前装配赛季印（`SeasonModels.FindEquippedSeal`）、气血/防御轻加成百分比（`SeasonModels.FormatBpsPercent`）、4 个当季免费赛季技能只读卡片（无需升级），底部提供「免费切换赛季印」（`Season.EquipSeal`）与「一键追赶至满进度」（`Season.CatchupSeal`，显示服务端报价并走幂等保护）。

---

### 6.3 必须可点击跑通的 5 条验收演示流

1. **新号 10 分钟体验流**：进入主城（确认已自动获赠并出战灵狐、顶部 3 数字清晰、无每日打卡弹窗）→ 点首领试炼 → 选重甲首领普通 `101` 开战 → 战中看到重劈与回春预告 → 胜局结算看到 `+10`（含 `1` 可交易）→ 跳转材料自选领取页按比例拆分打造材料/百炼石。
2. **困难解锁流**：未首通前点困难芯片见解锁提示 → 通关任意普通首领试炼后，`TrialJournal.Snapshot.HardBossTrialUnlocked` 变为 `true` → 困难芯片自动解锁 → 进困难 `102/106/108` 胜局看到 `+15`（基础 10 + 困难加量 5）。
3. **正式自养会武 + 120s 兜底流**：进正式人宠会武页（无免费预设区，仅 `1V1`/`3V3` 开放、`5V5` 即将开放、`2V2`/`4V4` 标注特殊玩法）→ 选 `1V1` 报名 → 模拟 120 秒等待出现兜底条 → 点「取消并去单人试炼」观察 `Cancelling → SwitchedToBossTrial`（以及成局竞态下 `ReturnedToMatchedBattle`）。
4. **娱乐轮换免费人宠流**：进首季轮换页 → 选「物法易势 5V5」并选 3×3 免费预设（如 102 控场 + 201 破甲）→ 报名 `MATCH_MODE_POLARITY_5V5` → 战中看到物势/法势横幅与下次换势回合 → 若模拟挂机/速死负场，结算页明确弹出 `MaterialPvpLossIneligible` 门槛说明。
5. **低打扰小额消费预览流**：从主城底部 4 个非数值预留位分别点开衣橱（10 元易容卡 3402 试穿）、赛季手记/试炼志里程碑、轻享省心卡/配装槽位预览、聚宝斋材料次要来源；全程零战力售卖、零强弹充值框。

---

### 6.4 UI 严禁事项清单（红线）

- 严禁在主城或二级页放置「每日任务 / 今日必做 0/2 / 连续签到 / 回归补课」清单与周期性红点。
- 严禁在正式人宠会武页展示或发送免费 3×3 预设（仅娱乐轮换页允许）。
- 严禁在小额消费位放入任何加攻击/速度/防御/暴击/控制或额外材料额度的付费项。
- 严禁本地推测段位分、材料余量或困难解锁态；一切以服务端快照为准。
- 严禁在首领试炼/会武等待时伪造机器人对手或强制踢出匹配队列。
