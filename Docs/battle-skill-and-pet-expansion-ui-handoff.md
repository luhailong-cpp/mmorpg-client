# 《五行奇谈》战斗战法扩展（A+B）与宝宝/战法构筑（C+D）Gemini UI 与验证交接规范

> **对应服务端设计**：[battle-skill-and-pet-cultivation-expansion.md](../../xuanming-server-mmo-cpp/docs/planning/battle-skill-and-pet-cultivation-expansion.md)  
> **已落盘范围**：
> - 权威服务端 `xuanming-server-mmo-cpp`：表 Schema、`Skill.xlsx`（A40 定名「太清灵盾」+ 24 新技）、`Buff.xlsx`（+30 新 Buff）、`SkillScrollCraft.xlsx`（+3 新卷宗 4/5/6）、`PetTier.xlsx`（5 阶飞升参数）、`Pet.xlsx`（部分常规宠种 2/3 阶分化）、`Tip.xlsx`（+10 Tip）、`MessageLimiter.xlsx`（已追加 500..503）、Protobuf 契约、战斗引擎（策略选人 + 蓄力 + 专精 + 追击封控 + 连击破绽 + 净心护阵）、场景服（宝宝条目 3 方案 + 五阶突破 + 人物战法 3 套构筑方案）及 C++ 单测；负责人已执行 `.\dev.bat gen`（产出 `500..503` 消息号与全部表/Proto）。
> - 客户端纯 C# 逻辑层 `mmorpg-client`：`PetClient.cs`、`PetClient.Entries.cs`、`SkillBookModels.cs`、`SkillBookMapper.cs`、`SkillBookClient.cs`、`SkillUiRoot.cs`、`CharacterSupportWindow.cs`、`BattleUnitStatusInspector.cs`、`tools/gen_messageids.ps1` 及 EditMode 单测；负责人已执行 `.\gen.bat client`（已生成 `MessageIds` 500..503 及全部 C# Proto/RPC）。
> - **未执行项（全部移交 Gemini）**：因 `MessageLimiter.xlsx` 新追加了 500..503，需先补跑一次 `.\dev.bat export`（刷新 `messagelimiter.*`）、随后做 C++/Unity 编译与单元测试、以及下方 **4 项增量客户端 UGUI 新界面/扩展接线**（外加与并行会话已落盘的首领试炼 V2 UI 一并验收）与双分辨率实机验收。

---

## 一、必须先执行的导出刷新、编译与单测步骤

### 1. 权威服务端刷新限流表导出
工作目录：`d:\luyuan\xuanming-server-mmo-cpp`
```powershell
.\dev.bat export
```
- **说明**：`.\dev.bat gen` 已由负责人执行完毕，新 RPC 消息号已确定并在 `data/MessageLimiter.xlsx` 增量补齐：
  - `500 = ScenePetClientPlayerSwitchPetEntryPreset`（10 / 1s）
  - `501 = ScenePetClientPlayerAscendPetTier`（5 / 1s）
  - `502 = SceneSkillClientPlayerSaveSkillLoadout`（10 / 1s）
  - `503 = SceneSkillClientPlayerSwitchSkillLoadout`（10 / 1s）
- 只需补跑一次 `.\dev.bat export` 把 `MessageLimiter.xlsx` 的 4 行刷进 `generated/tables/messagelimiter.*`。

### 2. 客户端编译体检
工作目录：`d:\luyuan\mmorpg-client`
```powershell
pwsh -NoProfile -File tools/client_compile_check.ps1 -ShowErrors 30
```
- 期望：`exit=0, errors=0`（`.\gen.bat client` 已运行，`MessageIds.cs` 已含 `500..503`）。

### 3. 服务端 C++ 串行编译与单测（严禁并发 `/m:1`）
工作目录：`d:\luyuan\xuanming-server-mmo-cpp`
- 按序串行 MSBuild（`/m:1 /p:MultiProcessorCompilation=false /p:CL_MPCount=1`）编译 `table.lib`、`battle.lib`、`scene.lib`、`battle.exe`、`scene.exe`、`turn_battle_engine_test.exe`、`skill_test.exe`。
- 期望通过：
  - `.\bin\scene.exe --validate-tables generated/tables` 退出 0；
  - `turn_battle_engine_test.exe --gtest_filter="ModernSkillCatalogTest.*"` 全绿（含 89 项目录、70 项主动施放、策略选人、蓄力与打断、专精禁攻、追击封控、连击破绽、净心护阵）；
  - `skill_test.exe --gtest_filter="PetEntriesTest.*:PlayerSkillBookTest.*"` 全绿（含宝宝 3 方案切换、五阶飞升扣材扣银+预算 20→22、人物战法装配槽位/互斥组/切换与战斗快照同步）。

### 4. 客户端 EditMode 单测
- 跑通 `PetEntriesClientTests`（含方案切换与灵阶突破幂等重试）与 `BattleUnitStatusInspectorTests`。

---

## 二、需要 Gemini 新做 / 升级的 4 项增量 Unity UI（全部 API 已落盘）

> **铁律**：UI 层严禁直接发包、严禁私自推算预算/材料消耗/槽位冲突/蓄力与护盾数值；一律消费下列纯 C# API，并满足 1920×1080、2560×1080 与小游戏安全区/触控无 Hover 约束。  
> **说明**：同目录 [boss-trial-v2-ui-handoff.md](boss-trial-v2-ui-handoff.md) 的首领试炼 V2 基础 UGUI 代码已由并行会话接入工作区（`BossTrialPrepPanel` / `BattleCoreHudOverlay` 等），Gemini 只需保留并连同本批一并做编译体检、缺图补图与双分辨率实机验收，无需重复重写。

### UI-1（扩展包 A+B 战内可读性）：战斗单位头顶「持久护盾条 + 蓄力标记」与「点击单位/Buff 状态详情弹窗」
- **权威 API**：`MmorpgClient.Game.Battle.BattleUnitStatusInspector.Inspect(BattleActorState actor, Func<uint, string> resolveSkillName)`
- **消费字段**：
  - `summary.TotalShieldCapacity` / `summary.ShieldRatio`：在 `BattleUnitView` 血条上方叠加一层**持久护盾条**（容量为 0 时隐藏，按 `ShieldRatio` 显示长度）。
  - `summary.IsCharging` / `summary.ChargingSkillName`：为正在蓄力（E01 蓄锋破阵 / E02 蓄雷天刑）的敌我单位头顶显示**「蓄力中 · {ChargingSkillName}」**状态标记，提示双方当前处于蓄力窗口。
  - `summary.Buffs`：在战斗内新增轻触/点击单位名牌或 Buff 行弹出的**状态详情小卡**（非选目标模式或长按/点击 Buff 栏触发，兼容触控无 Hover），逐条列出每个 Buff 的来源名、剩余回合 `RemainRounds`、护盾剩余容量 `RemainingCapacity`、剩余次数 `RemainingHits`（含 E08 净心护阵与 B05/B06）、回生印余次 `RemainingTicks`、叠层 `Stacks`（含 E07 连击破绽 `x/5`）。

### UI-2（扩展包 C-1）：宝宝条目页顶部「3 套方案一键切换条 + 用途标签 + 一键清零」
- **修改目标**：`Assets/Scripts/UI/Ugui/Pet/PetCultivationPanel.Entries.cs`
- **权威 API**：
  - 读：`pet.Entries.ActivePresetSlot`（1..3）、`pet.Entries.Presets`（每项含 `SlotId`、`Tag`、`UsedPoints`）、`PetClient.DescribePresetTag(tag)`；
  - 切方案/改标签：`PetClient.Instance.SwitchEntryPreset(petId, slotId, tag, stateVersion, configVersion, cb)`（仅切槽位时 `tag` 传 `PetClient.KeepPresetTag`）；
  - 一键清零：复走既有 `PetClient.Instance.SaveEntries(petId, stateVersion, configVersion, emptyDict, cb)`（服务端已支持空 `target` 免费清零）。
- **交互约束**：战斗中（`PetClient.Instance.CultivationWritesBlocked`）或有未保存草稿时先弹确认/禁用切方案按钮；切换成功后清空草稿并按服务端返回 `Entries` 全量刷新。

### UI-3（扩展包 C-2）：宝宝五阶「灵阶突破」入口与确认弹窗
- **修改目标**：`Assets/Scripts/UI/Ugui/Pet/PetCultivationPanel.cs`（现有五阶徽章弹窗旁）
- **权威 API**：
  - 读：`pet.CultivationTier`、`pet.CultivationTierName`、`pet.NextTier`（`PetTierAscensionInfo`：`NextTier`、`NextTierName`、`MinPetLevel`、`CostItemId`、`CostItemCount`、`OwnedItemCount`、`CostGold`、`BonusEntryPoints`、`CanAscend`、`BlockTipId`）；
  - 写：`PetClient.Instance.AscendTier(petId, pet.CultivationTier, (ok, newTier, list, err) => ...)`。
- **展示要求**：
  - 徽章与预览保持不变，已满阶（`pet.NextTier == null || pet.NextTier.NextTier == 0`）显示「已达巅峰 · 圣兽（+8 条目预算）」；
  - 未满阶展示下一阶徽章预览、所需宝宝等级、材料进度（`OwnedItemCount / CostItemCount` 悟道残页）、银两消耗及突破后条目点总预算提升（`20 → 22 → 24 → 26 → 28`）；
  - 按钮可用性直接绑定 `pet.NextTier.CanAscend`（不可突破时灰态并显示 `PetClient.DescribeTip("", new TipInfoMessage { Id = pet.NextTier.BlockTipId })`）。

### UI-4（扩展包 D + A/B 技能书增强）：技能书「战法构筑装配（3 套流派方案）」页签与副目标策略/互斥标签
- **修改目标**：`Assets/Scripts/UI/Ugui/Skills/SkillPanel.cs`（及新增子视图 `SkillLoadoutSubPanel.cs`）
- **权威 API**：
  - 读：`snapshot.Loadout`（`SkillLoadoutView`：`CustomEnabled`、`ActiveSchemeId`、`MaxPassiveSlots=3`、`MaxPursuitSlots=1`、`MaxOpeningSlots=1`、`Schemes` 1..3、`SkillLoadoutView.DescribeRoleTag(roleTag)`）；
  - 目录行新增只读字段：`entry.Equipped`（已装配徽章）、`entry.Definition.SecondaryTargetLabel`（策略技详情页显示「副目标：气血最低」等标签）、`entry.Definition.exclusiveGroup`（`> 0` 时提示「专精互斥组」）；
  - 写：`((ISkillLoadoutSource)SkillBookClient.Instance).SaveLoadout(schemeId, roleTag, passiveIds, pursuitIds, openingIds, cb)` 与 `SwitchLoadout(schemeId, cb)`。
- **功能结构**：
  - 仅角色技能书（`!context.IsPet`）顶部新增「战法装配」切换页签（宝宝技能页不显示）；
  - 上栏放「方案一 / 方案二 / 方案三」一键切换按钮 + 用途标签选择（默认/输出/控场/辅助）；
  - 中栏展示 **开场槽 ×1 / 被动槽 ×3 / 追击槽 ×1** 槽位（点击移除，下方已学非主动列表点击装入；装入同 `exclusiveGroup` 战法时提示互斥或替换旧项后点「保存并启用」）；
  - 战斗中只读锁定保存与切换按钮，仅允许查看当前装配方案。

---

## 三、需要增补的资源清单（仅列用途与资源键，不指定美术风格，缺图回落占位不阻断编译）

1. **新技能图标（24 张，路径 `Assets/Resources/UI/Skills/{id}.png`）**：
   - 策略定向 16 招：`10101..10114`（S01–S14）、`11101..11102`（SP01–SP02）；
   - 率土构筑 8 招：`10115..10116`（E01–E02）、`12101..12102`（E03–E04）、`13101..13102`（E05–E06）、`14101..14102`（E07–E08）。
   - A40（`10040`）已正式定名为 **「太清灵盾」**，如有旧文字标签同步替换为「太清灵盾」。
2. **新卷宗页签资源（可选，可复用现有卷宗页签）**：卷宗 `4 兵谋定向卷`、`5 奇策战法卷`、`6 奇策绝学卷`。
3. **战内蓄力状态图标（1 张）**：无字图标（缺图时仅显示文字「蓄力中」）。
