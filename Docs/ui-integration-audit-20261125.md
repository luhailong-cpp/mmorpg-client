# Unity 客户端全量 UI 美术与协议接入台账（2026-11-25）

本记录汇总 `D:\work\image\designs\` 截至 2026-11-25 02:14 已完成与进行中 UI 美术包在 `d:\luyuan\mmorpg-client` 的实际接入状态，严格区分**既有/并行会话已接入**、**本轮新接入**与**受美术未完或后端协议尚未落地阻塞的待办项**。

## 一、本轮新接入内容（72 张运行时无字素材 + 协议同步 + UGUI 接线）

1. **协议生成同步（修复基线 3 处 `SurrenderBattle*` 编译缺口）**
   - 依仓规从 `../xuanming-server-mmo-cpp` 执行 `tools/gen_proto.ps1` 与 `tools/gen_messageids.ps1`，刷新 `Assets/Scripts/Proto/Generated/{BattleData,MatchErrorTip,MatchService,PlayerBattle}.cs` 与 `Assets/Scripts/Net/MessageIds.cs`（178 条）。
2. **`season-economy-ui-20261124`（37/37 SHA-256 一致）**
   - 导入 `Assets/Resources/UI/Ugui/SeasonEconomyV1/{Icons,Controls}/`（16 张 256×256 图标 + 21 张无字控件，9 张离线验证横向三段切片按 `CONTROL-USAGE.md` 配置 `spriteBorder`，其余固定比例）。
   - 新增共享工具 `Assets/Scripts/UI/Ugui/SeasonEconomyUiArt.cs` 及单测 `Assets/Tests/EditMode/Role/SeasonEconomyUiArtTests.cs`。
   - 接线 7 处现有 UGUI：`BossTrialPrepPanel`（选态试炼卡 + 全部 8 张机制意图图标 + 可交易角标）、`SeasonRotationPanel`（护卫/双势徽章 + 可交易角标）、`FormalPvpLobbyPanel`（三态人数档位胶囊）、`BattleCoreHudOverlay`（阴阳双势原比例横幅 + 护卫独立气血框实读 `BattleStateS2C.Actors` 真实血量 + 预告条与意图图标）、`BattleUnitView`（头顶 `target_lock_marker` 重击锁定标与 `badge_protect_guard` 金盾）、`CoreSettlementRewardSection`（温和未达标横条与可交易角标）、`EquipmentEnhancementView`（+11 满配皇冠、`pity_bar_*` 保底进度条与满保底金色必成按钮）、`EquipmentWindow.Materials`（两阶段永久首套/周精进进度条、无每日打卡提示条、周重置倒计时与 `±5` 快捷步长）。
3. **`craft-quality-skill-ui-20261124`（23/23 SHA-256 一致）**
   - 导入 `Assets/Resources/UI/Ugui/CraftQualitySkill/`（16 张 `CONTROL-USAGE.md` 核验控件）与 `CraftQualitySkill/Icons/`（7 张 512×512 运行时图标，排除 QA 对照图 `preview-64.png`），自动满足 `EquipmentScreenModel.CraftIconRoot`（道具 3405–3408）与 3 本成长战法（12020 愈战愈勇 / 12021 深谋远虑 / 12022 步步为营）。
   - 接线 `EquipmentWindow.Polish`（三档手艺徽章、5 档品相底板、8 次打磨保底条、宗师署名印、定星保底符图标）、`SkillCraftWindow`（卷宗 420×140 原比例页签、稀世绝学角标、材料栏三图标、官府托管横带、名匠宗师徽章与署名印）及 `SkillPanel`（成长战法图标回落）。
4. **`economy-trade-ui-v1`（12/12 可复用独立切片 SHA-256 一致）**
   - 导入 `Assets/Resources/UI/Ugui/EconomyTradeV1/`（12 张无字独立切片，排除未验九宫格的 7 张 `panel_*.png`；`diff_alert_banner` 按 `MANIFEST.md` 配置横向三段 `330,0,330,0`）。
   - 已将 `badge_item_tradable_t`、`badge_item_bound_b`、`diff_alert_banner` 接入 `EquipmentWindow.Polish` 装备行与「转为仅自用二次确认」弹窗。

## 二、全量 UI 美术包总览

| 美术包 (`D:\work\image\designs\`) | 美术状态 | 客户端接入状态 | 说明 |
|---|---|---|---|
| `lunjian-ui-v1`（含 `completion-20261124`） | 已完成 | 已接入 | 47 PNG 在 `LunjianV1/`，对应 `UI/Ugui/Ranked/` |
| `official-passport-ui-v1` | 已完成 | 已接入 | 5 张运行时切片在 `RefreshV8/passport_*.png`，对应 `QdaoServerSelectView.Passport.cs` |
| `appearance-series-v1` | 已完成 | 已接入（并行会话） | 11/11 PNG 在 `AppearanceSeriesV1/`，对应 `AppearanceUiRoot.Series.cs` |
| `core-gameplay-loop-ui-v1` | 已完成 | 已接入（并行会话） | 10 PNG 在 `CoreLoopV1/`，对应 `UI/Ugui/CoreLoop/` |
| `spring-beast-ui-20261124` | 已完成 | 已接入（并行会话） | 19/19 切片在 `FestivalBeastV1/`，对应 `UI/Ugui/FestivalBeast/` + 活动窗 |
| `pet-memory-ui-v1` | 已完成 | 已接入（并行会话） | 7/7 UI 资源在 `PetFlipV1/`，灵宠头像实读 `QdaoPetCatalog`，对应 `UI/Ugui/PetFlip/` |
| `trial-journal-ui-v1` | 已完成 | 已接入（并行会话） | 按交接复用 `AppearanceChronicleV2` + `Equipment20261117/Icons`，对应 `UI/Ugui/TrialJournal/` |
| `season-team-tournament-v1` | 已完成 | 已接入（并行会话） | `TournamentV1/hud-entry-base.png` + `MailV1/TeamV2`，对应 `UI/Ugui/Tournament/` |
| `season-economy-ui-20261124` | 已完成 | **本轮已全量接入** | 37/37 PNG 在 `SeasonEconomyV1/`，已接 7 处业务界面 |
| `craft-quality-skill-ui-20261124` | 已完成 | **本轮已全量接入** | 16 控件 + 7 图标共 23/23 PNG 在 `CraftQualitySkill/`，已接打磨/制书/名匠/技能栏 |
| `economy-trade-ui-v1` | 已完成 | **素材已导入 + T/B 隔离已接；新交易/信用页待后端协议** | 12 独立切片已导入 `EconomyTradeV1/`；当面交易、卖家信用、p50/定向托管、解封符、官方商城在客户端与服务端尚无对应 Proto/RPC，禁止伪造假数据页面 |
| `season-ui-v1` | **制作未完成** | **功能 UI 已就绪；专属美术待补齐** | `manifest.json` 仍标 `blocked_on_equipped_card_unknown`（缺已佩戴卡与 3 枚徽章，横幅 `hold`）；`SeasonSealPanel.cs` 已用既有合规底框跑通 `SeasonClient` 全部读写功能 |

## 三、验证记录（2026-11-25 02:14）

- `pwsh -NoProfile -File tools/client_compile_check.ps1 -ShowErrors 30` -> `exit=0 files=718 errors=0`
- 12 组 EditMode NUnit 单测（`$env:TEMP` 编译运行并清理）-> **`178/178` 全通过，`0` 失败**：
  `SeasonEconomyUiArtTests` (4)、`CoreGameplayLoopTests` (2)、`RankedClientTests` (6)、`TournamentClientTests` (2)、`TournamentUiModelTests` (3)、`TrialJournalClientTests` (6)、`FestivalBeastClientTests` (5)、`FestivalBeastUiStateTests` (3)、`PetFlipUiPresentationTests` (6)、`EquipmentMaterialRewardClientTests` (34)、`EquipForgeUiStateTests` (63)、`EquipmentScreenModelTests` (44)。
- SHA-256 与 `.meta` / GUID 全仓审计：新导入 72 张 PNG 逐字节 SHA-256 100% 一致；全仓 `Assets/` 共 `11776` 个 `.meta`，0 缺失、0 孤儿、0 重复 GUID。

## 四、待 Unity 实机 / 后端补齐项

1. **Unity PlayMode 视觉与实机复核**：本轮未启动 Unity 编辑器进行 PlayMode 截屏；需在 Unity 内以 1920×1080、2560×1080 及小游戏安全区/胶囊遮挡下实看 `SeasonEconomyV1` / `CraftQualitySkill` / `EconomyTradeV1` 切片拉伸与中文字体字距。
2. **`economy-trade-ui-v1` 五套新业务界面**：待服务端落地当面交易、卖家信用档案/评价、集市 p50/定向托管单、解封剥离符与官方商城 Proto/RPC 并同步客户端后，直接基于已导入的 `EconomyTradeV1/` 切片接线。
3. **`season-ui-v1` 专属皮肤**：待美术补齐已佩戴卡、3 枚徽章与修订横幅并终验后，再替换 `SeasonSealPanel.cs` 的图标与卡片贴图。
4. **顺带发现的历史 `.meta` 提醒**：已提交文件 `Assets/Tests/EditMode/Battle/EquipmentMaterialRewardClientTests.cs.meta`（提交 `9f4f8d2d`）的 `guid` 为 33 位十六进制（非标准 32 位），本轮未越界修改。
