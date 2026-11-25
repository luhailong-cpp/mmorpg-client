# 《五行奇谈》首领试炼 V2 Unity UI 与美术交接规范（供 Codex）

> 本文档严格对应已落地并通过双版服务端与客户端纯 C# 单测验证的 **首领试炼 V2** 契约与逻辑层：
> - 权威服务端：`d:/luyuan/xuanming-server-mmo-cpp`（C++ `battle`/`scene` + Go `match` + 70 张权威表）
> - Java 对账服务端：`d:/luyuan/xuanming-server-mmo`（`game-battle-engine` / `game-battle` / `game-scene` / `game-match`）
> - 客户端纯 C# 逻辑层：`d:/luyuan/mmorpg-client/Assets/Scripts/Game/`（`CoreGameplayLoopModels.cs`、`CoreGameplayLoopClient.cs`、`TrialJournalModels.cs`、`BattleClient.cs`）
>
> **铁律**：UI 层**严禁直接发包、严禁私自计算破绽/蓄势/减伤/困难解锁条件、严禁替队友提交防御、严禁手写 Tip 错误码数字、严禁在 `MmorpgClient.Game.*` 引入 `UnityEngine`**。本机已有 4 卡片老版 `BossTrialPrepPanel.cs` 与战斗 HUD 均保持兼容，请按本文升级至 V2 交互与视觉。

---

## 一、需求摘要（V2 五大体验升级）

1. **群攻主点 100% / 副目标溅射 60%（-40%）**：仅作用于首领试炼敌方怪物；选多目标技能时必须用金色主锁「聚火 100%」与灰色虚线副锁「溅射 60%」明确传达“选谁当主目标很重要”。
2. **统一「破绽（+35% / +20%）」正反馈爆发**：成功防御/护盾/反震/控断慢速重劈、打断或击杀回春护卫（额外 6% 反噬）、驱散或控断护势联防阵（「崩势」双破绽），都会令首领（及护势护卫）陷入统一「破绽」直到下回合末（直伤与毒火全吃，与二阶段 `+15%` 加算封顶 `+50%`）。
3. **护势联防阵 + 轻反制「蓄势」+ 困难动态「顺势应变」**：护势护卫为全体存活敌方提供 35%/40% 物法减伤，同系直伤触发轻反制为首领叠「蓄势」（0–3 层，每层重劈 `+12%`，可驱散）；困难模式按前 2 回合玩家物法直伤总额动态切换阵型。
4. **困难专属第 5 张卡片「重甲首领 · 双卫同阵」（Dungeon `112`，`boss_trial_kind = 8`）**：仅困难开放，与困难单卫共享 15 份材料上限（绝不逼肝），但拥有独立试炼志格 `Entry 105`、3 枚专属战法印 `1051–1053` 与里程碑 `105`「破阵双绝」徽章 + 留灯院跋文。
5. **精简精英阵 + 3–5 人裂地双劈 + 二阶段「决战姿态」+ 18s 节奏 + 一键化劲防御**：
   - 多人局不再刷白板凑数怪，改为 2–5 人追加「策应卫·破军 (`109`)」（普攻破甲 -20%，阵亡清 1 层蓄势）、4–5 人追加「策应卫·鼓噪 (`110`)」（回合末 +1 蓄势，阵亡清蓄势 + 4% 首领震荡）；双卫 3–5 人仅加破军卫（全场至多 4 怪）。
   - 3–5 人局慢速重劈升级为「裂地双劈」点名 2 名玩家/宠（2/2 全化解 +35%，1/2 半化解 +20% 防背锅）。
   - 首领半血或全部特殊护卫阵亡于回合末切入二阶段「决战姿态（破釜）」（易伤 `+15%`，重劈改为每 2 回合循环）。
   - 首领试炼行动窗缩短至 **18 秒**，提供「一键化劲防御」快捷按钮、「记忆上回合技能」高亮，且撤退（`BATTLE_ACTION_FLEE`）100% 确定性带宠离场。

---

## 二、权威 C# API 绑定表（均已落库并通过编译）

| 界面 / 交互 | 调用入口 | 读写字段与方法 | 说明与防线 |
|---|---|---|---|
| **U01 备战 5 卡选局页** | `CoreGameplayLoopClient` | `BuildBossTrialsV2()` → `IReadOnlyList<BossTrialCardView>`（5 张卡，第 5 张 `BossTrialKind == 8`，`IsHardOnly == true`，`HardDungeonId == 112`）<br>`EnterBossTrial(uint dungeonId)` | 当 `card.IsHardOnly == true` 时**隐藏「普通」按钮**，仅渲染困难入口与 `card.HardUnlocked` 锁态；`EnterBossTrial(112u)` 已内置 `HardBossTrialUnlocked` 门禁检查 |
| **U02 战斗内 V2 HUD** | `CoreGameplayLoopClient` / `CoreGameplayLoopModels` | `BuildBattleHud()` → `BattleCoreHudView`：<br>- `IsBossTrial`, `BossTrialKind`, `BattleDifficulty`<br>- `BossPhase`, `IsBossPhaseTwo`, `BossPhaseBannerText`<br>- `BossMomentumStacks` (0..3)<br>- `HasBossStance`, `BossStanceType` (1=物防/2=法防), `BossStanceRemainRounds`<br>- `IsBossExposed`, `BossExposedPct` (0/20/35), `BossExposedUntilRound`<br>- `BossStatusSummaryText`<br>- `Telegraphs[i].TargetActorIds` & `IsDualTarget` | 每次 `Battle.OnBattleStart` / `Battle.OnTurnResult` / `Battle.OnStateUpdated` 重新调用 `BuildBattleHud()`，严禁本地推算剩余回合 |
| **U02 聚火/溅射预览** | `CoreGameplayLoopModels` | `IsSplashFocusApplicable(BattleStateS2C state, uint skillMaxTargets)` | 返回 `true` 时：悬停/选中主目标显示金色准星「聚火 100%」，其余受影响敌方显示灰色副框「溅射 60%」；实际回放飘字读 `BattleEventItem.IsSplashReduced` |
| **U02 一键化劲防御** | `CoreGameplayLoopClient` + `BattleClient` | `core.CollectMyQuickParryActorIds()` → 本回合被点名且尚未交令的本人/本人宠 `actor_id` 列表<br>`battle.SubmitQuickParryDefend(ulong actorId, Action<bool> completed)` | **V8 防线**：严禁向 `SubmitQuickParryDefend` 传入队友 ID；按钮仅在 `CollectMyQuickParryActorIds().Count > 0` 时弹出，复用既有 `SubmitBattleAction(DEFEND)` |
| **U02 记忆上回合技能** | `BattleClient` | `battle.GetLastUsedSkillId(ulong actorId = 0)` | 打开人物/宝宝技能栏时，若返回值非 0 且该技能仍在当前可用列表中，高亮边框并标「上次使用」角标，实现 1 次点击复放 |
| **U02 机制触发横幅** | `CoreGameplayLoopModels` | `DescribeBossMechanicEvent(BattleEventItem ev)` → `BossMechanicBannerView` (`MechanicKind`, `Title`, `Subtitle`, `Value`, `IsPositiveForPlayer`) | 当 `ev.EventType == eBattleEventType.BattleEventBossMechanic` 时调用；`IsPositiveForPlayer == true` 用暖金破招横幅+清脆音效，`false` 用深绯警示横幅 |
| **U03 结算高光徽章** | `CoreGameplayLoopClient` / `CoreGameplayLoopModels` | `core.BuildLastBossTrialHighlights()` / `BuildBossTrialHighlightBadges(BattleSettlementData)` → `IReadOnlyList<BossTrialHighlightBadge>` | 最多展示 5 枚权威高光徽章：`化劲破招 ×N`、`断疗反噬 ×N`、`崩势破盾 ×N`、`化劲宗师`、`单骑破阵` |
| **试炼志 Entry 105** | `TrialJournalModels` | `TryParseJumpTarget("boss_trial:hard:dual_guard", out var target)` → `Dungeon 112`<br>`TryGetEntryMonsterIds(105u, ...)`<br>`GetEntryGuardMonsterIds(105u)` → `[102u, 103u]` | `TrialJournalWindow` 渲染 Entry 105 时若 `GetEntryGuardMonsterIds(entryId).Count == 2`，双栏并排展示回春护卫(`102`)与护势护卫(`103`)头像 |

---

## 三、`BATTLE_EVENT_BOSS_MECHANIC`（事件号 `22`）完整映射表

| `mechanic_kind` | 机制名 | `value` 语义 | `IsPositiveForPlayer` | 推荐 UI / VFX 表现 |
|---:|---|---|---:|---|
| `1` | 化劲破绽 | `35`（完整）或 `20`（半化解） | `true` | 首领身躯金色碎甲裂纹 + 飘字「化劲破绽 +35%」 |
| `2` | 断疗反噬 | 首领被反噬扣除的气血值 | `true` | 护卫至首领青金灵脉断裂光束 + 飘字「断疗反噬」+ 破绽图标 |
| `3` | 崩势破盾 | `35` | `true` | 全体敌方联防阵法碎裂特效 + 首领与护卫同时挂破绽 |
| `4` | 阵脚震荡 | 破军=`1`（清 1 蓄势）；鼓噪=首领震荡掉血量 | `true` | 策应卫退场冲击波震向首领，顶部蓄势珠熄灭 |
| `5` | 决战姿态 · 破釜 | `2`（阶段号） | `false` | 顶部阶段条切换深绯金边「二阶段 · 决战姿态」，战场边缘轻脉动红光 |
| `6` | 护势蓄势 | 当前蓄势层数（`1..3`） | `false` | 首领血条旁 3 颗蓄势火珠逐颗点亮（满 3 层脉冲提示「可驱散」） |
| `7` | 破军破甲 | `20`（-20% 防御） | `false` | 受击我方单位脚下碎盾减益角标（2 回合，可净化） |
| `8` | 回春净益 | `1`（清除减益数） | `false` | 首领身上青绿净光闪过，飘字「回春净益」 |
| `9` | 断脉气馁 | `15`（-15% 物攻） | `true` | 断脉符印压制青绿治疗光，首领头顶挂「气馁 -15%」1 回合 |

---

## 四、逐屏 UI 改动清单（仅供 Codex 修改 `Assets/Scripts/UI/`）

### 1. U01 首领试炼备战页（升级 `Assets/Scripts/UI/Ugui/CoreLoop/BossTrialPrepPanel.cs`）
- 数据源由 `_core.BuildBossTrials()` 切换为 **`_core.BuildBossTrialsV2()`**（5 张卡片）。
- 5 卡横向排布适配 2560×1080 与常见 16:9 / 全面屏安全区（建议卡片宽度收窄至约 `365px` 间距 `24px` 或改为「上方 4 张常规首领 + 右侧/底部横幅高难终局卡：重甲首领·双卫同阵」，避免旧代码 `320 + i * 475` 第 5 张越界！）。
- 第 5 张卡（`card.IsHardOnly == true`）：
  - 仅渲染单一金色高难入口按钮（`困难 +15 · 专属徽章`），未解锁时展示锁态并弹 `card.HardLockHint`。
  - 副标题标明「与困难单卫共享 15 份材料，额外解锁试炼志『破阵双绝』徽章」。

### 2. U02 战斗内 V2 HUD 与操作减负（新建/升级 `BattleCoreHudOverlay` & 技能目标预览）
- **顶部首领状态带**（仅 `hud.IsBossTrial == true` 时显示，避让 `Screen.safeArea` 与小游戏胶囊）：
  - 左侧：阶段徽章（`一阶段` 玉青 / `二阶段 · 决战姿态` 绯金），切阶段时播 1.2s 轻横幅。
  - 中部：状态芯片区（来自 `hud`）：
    - `IsBossExposed` → 醒目琥珀金芯片「破绽 +{BossExposedPct}%（至第 {BossExposedUntilRound} 回合末）」
    - `BossMomentumStacks > 0` → 3 颗蓄势火珠「蓄势 {BossMomentumStacks}/3（可驱散）」
    - `HasBossStance` → 阵法芯片「物防联防阵 / 法防联防阵 · 余 {BossStanceRemainRounds} 回合（可驱散）」
  - 右侧：预告条（渲染 `hud.Telegraphs`；当 `t.IsDualTarget` 时双人头像/名字并排标示「裂地双劈」）。
- **战场脚底/头顶标注**：
  - 3–5 人裂地双劈：对 `t.TargetActorIds` 内所有目标同时挂红色重劈预警锁定圈。
  - 选多目标技能且 `CoreGameplayLoopModels.IsSplashFocusApplicable(state, skillMaxTargets)` 为 `true` 时：主目标挂金色「聚火 100%」，其他敌方挂灰色「溅射 60%」。
  - 伤害飘字当 `ev.IsSplashReduced == true` 时缩小 15% 并附淡灰角标「溅射」；首领处于破绽时直伤与毒火飘字带金边放大 15%。
- **一键化劲防御浮窗**（`WaitingAction` 阶段）：
  - 当 `core.CollectMyQuickParryActorIds()` 非空时，在技能栏上方弹出 1 个紧凑金边快捷按钮：
    - 仅本人被点名：「一键化劲防御（主角）」→ 调 `battle.SubmitQuickParryDefend(myPlayerId)`；
    - 仅宠被点名：「一键化劲防御（灵宠）」→ 调 `battle.SubmitQuickParryDefend(petActorId)`；
    - 人宠双被点名：并排 2 颗按钮或按当前选中操作单位显示，逐单位调用 `battle.SubmitQuickParryDefend(id)`。
- **记忆上回合技能**：读取 `battle.GetLastUsedSkillId(currentActorId)`，在技能栏首格/对应格显示淡金外框与「上回合」角标。
- **18s 倒计时与确定退出**：
  - 倒计时直接读服务端 `BattleStateS2C.ActionDeadlineMs`（首领试炼服务端已下发 18 秒窗口，严禁前端写死 25s）。
  - 撤退按钮在首领试炼中提示「确认退出试炼（100% 离场，不消耗任何场外资产）」，点击发送既有 `BATTLE_ACTION_FLEE`。

### 3. U03 结算高光与试炼志（`CoreSettlementRewardSection` / `TrialJournalWindow`）
- 结算页在材料拆解卡下方增加一行「本场战法高光」胶囊带，遍历 `core.BuildLastBossTrialHighlights()`（为空则隐藏整行）。
- `TrialJournalWindow.cs` 遇到 `entry.EntryId == 105` 时调用 `TrialJournalModels.GetEntryGuardMonsterIds(105u)` 展示双护卫头像（`102` + `103`），并兼容 `TryParseJumpTarget("boss_trial:hard:dual_guard", ...)` 一键跳转备战页。

---

## 五、美术与 VFX 资源增补清单（路径沿用 `BattleArtCatalog` 契约，缺图自动回退不报错）

| 资源用途 | 建议资源路径（`Assets/Resources/...`） | 规格 / 风格要求 | 缺图降级 |
|---|---|---|---|
| 策应卫·破军（Monster `109`）立绘/帧条 | `Battle/Monsters/109/{idle,attack,hit,die}_E_strip.png` | 东方志怪重戟轻甲武卫，铁青+暗红破甲纹 | 自动回退程序化剪影/复用护卫剪影 |
| 策应卫·鼓噪（Monster `110`）立绘/帧条 | `Battle/Monsters/110/{idle,attack,hit,die}_E_strip.png` | 东方志怪战鼓旗手，赤金鼓面+云纹战旗 | 自动回退程序化剪影 |
| 双卫同阵卡片图标（`boss_trial_kind = 8`） | `UI/Ugui/SeasonEconomy/Icons/boss_avatar_dual_guard.png` | 重甲首领居中、两侧回春青莲+护势玄盾双徽 | 回退既有重甲首领头像 |
| 「破绽」裂纹特效条 | `Battle/Fx/boss_exposed_crack_strip.png` | 8 帧 256×256 暖金裂纹光环，持续环绕受破绽敌方 | 回退金色脚底光圈 |
| 「崩势破盾」碎阵特效条 | `Battle/Fx/boss_stance_break_strip.png` | 10 帧 512×512 阵法瓦解爆裂波纹 | 回退通用驱散波纹 |
| 「决战姿态」阶段切换光效 | `Battle/Fx/boss_phase2_enrage_strip.png` | 10 帧 512×512 绯金气浪冲天 | 回退红金横幅渐入 |
| 状态小图标（破绽/蓄势/破甲/气馁/聚火） | `UI/Ugui/Battle/BossTrialV2/icon_{exposed,momentum,sunder,dishearten,focus,splash}.png` | 64×64 透明底扁平国风徽记 | 回退文字胶囊芯片 |
| 「破阵双绝」试炼志徽章（`badge_trial_dual_guard`） | `UI/Ugui/TrialJournal/Badges/badge_trial_dual_guard.png` | 128×128 玉青镶金双剑交错纹章 | 回退通用里程碑徽章 |

---

## 六、离线联调样本 JSON（Codex 无服务端即可 1:1 渲染 U01 / U02 / U03 全状态）

```json
{
  "u01_prep_cards": [
    { "bossTrialKind": 1, "normalDungeonId": 101, "hardDungeonId": 102, "isHardOnly": false, "bossName": "重甲首领 · 回春阵", "guardTag": "回春护卫", "normalRewardUnits": 10, "hardRewardUnits": 15, "hardUnlocked": true },
    { "bossTrialKind": 2, "normalDungeonId": 103, "hardDungeonId": 104, "isHardOnly": false, "bossName": "重甲首领 · 护势阵", "guardTag": "护势护卫", "normalRewardUnits": 10, "hardRewardUnits": 15, "hardUnlocked": true },
    { "bossTrialKind": 3, "normalDungeonId": 105, "hardDungeonId": 106, "isHardOnly": false, "bossName": "玄盾首领", "guardTag": "灵障护卫", "normalRewardUnits": 10, "hardRewardUnits": 15, "hardUnlocked": true },
    { "bossTrialKind": 4, "normalDungeonId": 107, "hardDungeonId": 108, "isHardOnly": false, "bossName": "幽毒首领", "guardTag": "玄毒护卫", "normalRewardUnits": 10, "hardRewardUnits": 15, "hardUnlocked": true },
    { "bossTrialKind": 8, "normalDungeonId": 0,   "hardDungeonId": 112, "isHardOnly": true,  "bossName": "重甲首领 · 双卫同阵", "guardTag": "回春 + 护势", "normalRewardUnits": 0,  "hardRewardUnits": 15, "hardUnlocked": true }
  ],
  "u02_battle_state_phase2_dual_cleave": {
    "battleId": 9901,
    "roundIndex": 5,
    "roundLimit": 15,
    "bossTrialKind": 8,
    "battleDifficulty": 2,
    "bossPhase": 2,
    "bossMomentumStacks": 2,
    "bossStanceType": 1,
    "bossStanceRemainRounds": 2,
    "bossExposedPct": 35,
    "bossExposedUntilRound": 6,
    "pendingActorIds": [3001, 300101, 3002],
    "telegraphs": [
      {
        "sourceActorId": 9223372041149743104,
        "targetActorId": 3001,
        "targetActorIds": [3001, 3002],
        "executeRound": 5,
        "intentKind": 1,
        "status": 2,
        "label": "裂地双劈：本回合慢速重劈双目标，全员防御/护盾化解触发完整破绽"
      },
      {
        "sourceActorId": 9223372041149743105,
        "targetActorId": 9223372041149743104,
        "targetActorIds": [9223372041149743104],
        "executeRound": 6,
        "intentKind": 2,
        "status": 1,
        "label": "回春祈愈（进阶）：下回合为首领回复10%血并净1减益；打断或击杀触发反噬破绽"
      }
    ],
    "sampleTurnEvents": [
      { "eventType": "BATTLE_EVENT_DAMAGE", "sourceId": 3001, "targetId": 9223372041149743104, "skillTableId": 10001, "value": 1620, "isSplashReduced": false },
      { "eventType": "BATTLE_EVENT_DAMAGE", "sourceId": 3001, "targetId": 9223372041149743105, "skillTableId": 10001, "value": 720,  "isSplashReduced": true },
      { "eventType": "BATTLE_EVENT_BOSS_MECHANIC", "sourceId": 3001, "targetId": 9223372041149743104, "mechanicKind": 3, "value": 35 },
      { "eventType": "BATTLE_EVENT_BOSS_MECHANIC", "sourceId": 9223372041149743104, "targetId": 9223372041149743104, "mechanicKind": 5, "value": 2 }
    ]
  },
  "u03_settlement_highlights": {
    "outcome": "BATTLE_OUTCOME_SIDE_A_WIN",
    "totalRounds": 7,
    "battleDifficulty": 2,
    "bossTrialKind": 8,
    "materialCreditUnitsAdded": 15,
    "materialTradableUnitsAdded": 1,
    "trialFacts": {
      "journalEntryId": 105,
      "initialHumanCount": 1,
      "cleaveParryCount": 2,
      "healInterruptBacklashCount": 1,
      "stanceBreakCount": 1,
      "allSelfCleavesParried": true,
      "selfParriedCleaveAtLeastOnce": true
    },
    "expectedHighlightTitles": [
      "化劲破招 ×2",
      "断疗反噬 ×1",
      "崩势破盾 ×1",
      "化劲宗师",
      "单骑破阵"
    ]
  }
}
```

---

## 七、Codex 验收检查项

1. `pwsh -File tools/client_compile_check.ps1` **0 错误**；新增脚本、图片与目录均配齐 `.meta`。
2. 零 `Task.Run` / `Thread` / `HttpClient` / `.Result`（WebGL2 / IL2CPP 安全）。
3. 严禁修改 `Assets/Scripts/Game/` 纯 C# 层破坏现有 EditMode 单测；若需扩展字段优先走现有 `CoreGameplayLoopModels` 只读属性。
4. 离线跑通 4 条演示流：① U01 5 卡展示与第 5 张卡仅困难锁态；② U02 聚火/溅射预览 + 一键化劲防御（不给队友弹出）+ 破绽/蓄势/二阶段横幅；③ U03 高光徽章与 15 份材料拆解；④ 试炼志 Entry 105 双护卫头像与 `boss_trial:hard:dual_guard` 跳转。
