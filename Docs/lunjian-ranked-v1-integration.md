# 论剑排位首发 V1 —— Unity 客户端接入与联调接口清单

> 对应主仓设计：[ranked-play-season-ui-candidate.md](../../xuanming-server-mmo-cpp/docs/design/ranked-play-season-ui-candidate.md)、[ranked-play-v1-implementation.md](../../xuanming-server-mmo-cpp/docs/design/ranked-play-v1-implementation.md)
> UI 美术源包：`D:\work\image\designs\lunjian-ui-v1\`（严格采用 47 张无字 PNG，已剔除 `ui-window.png` 与 `button-primary-cloud.png`）

## 1. 客户端绑定与适配位置

| 层级 | 路径 | 职责 |
|---|---|---|
| 生成协议 / 消息号 / 配表 | `Assets/Scripts/Proto/Generated/{MatchService,Team,BattleData,PlayerBattle,PlayerBattleResultComp,Rank}.cs`、`Assets/Scripts/Net/Generated/Rpc/{MatchServiceRpc,ClientPlayerTeamRpc}.cs`、`Assets/Scripts/Table/Generated/{Rankedrule,Rankedpreset}Table*.cs` | 与权威仓 `5c0a518a6` 同步的 proto/表/RPC 桩（禁止手改） |
| 47 张无字 PNG 资源 | `Assets/Resources/UI/Ugui/LunjianV1/*.png`（及 `.meta`） | 47 张 SHA-256 校验通过的 Sprite（RGBA32、PPU=100、无 9-slice、无 mipmaps） |
| 纯 C# 网络适配（零 UnityEngine） | `Assets/Scripts/Game/Ranked/{RankedModels,RankedClient}.cs` | RPC 448–451、`PENDING` 有界退避轮询、会话/连接/代次隔离 |
| 既有客户端适配扩展 | `Assets/Scripts/Game/Team/{TeamModels,TeamViewMapper,TeamClient,TeamClient.ActionRecovery}.cs`、`Assets/Scripts/Game/Battle/{BattleClient,BattleMatchEntryClient}.cs`、`Assets/Scripts/Game/Rank/RankClient.cs`、`Assets/Scripts/Game/GameClient.cs` | 队内预设确认 RPC 452 + 4 人排 5V5 拦截 + 120s 排队状态 + 赛季榜键 |
| UGUI 界面实现 | `Assets/Scripts/UI/Ugui/Ranked/{RankedArtCatalog,RankedLobbyPanel,RankedSharedDrawer,RankedSettlementPanel,RankedUiRoot}.cs` | 主城「论剑」入口、1920x1080 安全区大厅 8 态、三页签共享抽屉、单局结算页、复用 `AppearanceUiRoot`（10 元易容卡）与 `GameplayUiRoot.OpenEquipmentMaterials()`（材料自选领取） |
| 既有 HUD 与战斗结算挂钩 | `Assets/Scripts/UI/Ugui/WorldHudNavigation.cs`、`Assets/Scripts/UI/Ugui/Battle/BattleUiRoot.cs`、`Assets/Scripts/UI/Ugui/Gameplay/{GameplayUiRoot,EquipmentWindow.Materials}.cs` | 注册 `WorldHudEntry.Ranked`、接管排位现场终局与重登补弹并回写 `AckBattleEnd` / `NoteShown` |
| EditMode 单测 | `Assets/Tests/EditMode/Ranked/RankedClientTests.cs` | 覆盖 RPC 448–452、`PENDING→CONFIRMED` 轮询、日额度满 `+0`、4 人队拦 5V5、断线作废旧回调 |

## 2. 权威接口清单（可直接随交付件转发）

### 2.1 权威读取矩阵（防误读 / 防串账）

| 业务事实 | 权威接口与字段 | 客户端读取点 | 备注 |
|---|---|---|---|
| 积分与段位（单局前/后、变化） | `MatchService.GetRankedBattleResult`（451）`result.{status,rating_before_centi,rating_after_centi,rating_delta_centi,tier_before,tier_after}` | `RankedClient.ActiveSettlement.AuthoritativeView` | 单位为百分制（显示除以 100 取整）；客户端绝不本地算分或推段位，`PENDING` 时仅展示「积分待确认」 |
| 大厅当前积分、段位、有效场次、参赛亮段资格 | `MatchService.GetRankedProfile`（448）`current_progress.{qualified,rating_centi,current_tier,peak_rating_centi,valid_matches,wins,losses,draws}` | `RankedClient.Profile.CurrentProgress` | `qualified=false` 显示「未亮段」且不在本季榜 |
| 4/8/12 永久展示权益与佩戴态 | `MatchService.GetRankedProfile`（448）`entitlements[]`、`cosmetic_loadout`；修改走 `UpdateRankedCosmetic`（450） | `RankedClient.Profile.{Entitlements,CosmeticLoadout}` | 仅展示，非打造材料；仅服务端确认回包后更新 |
| 单局日常材料自选额度增量（入账，不入包） | 现场 `BattleEndS2C`（150）或重登补查 `GetRecentBattleResults`（371）`material_credit_units_added`、`season_id` | `RankedClient.ActiveSettlement.MaterialCreditUnitsAdded` | 每合格局 `+10`，与 PVE 共享每日上限 `20` 份；上限满时为 `0`，赛季照记 |
| 材料实际领取入包 | 既有 `GetEquipmentMaterialReward`（407）/ `ClaimEquipmentMaterialReward`（408） | `GameplayUiRoot.OpenEquipmentMaterials()` → `EquipmentWindow.Materials` | 玩家确认领取后才按打造材料/百炼石拆分入背包 |
| 10 元小额外观（易容卡） | 既有 `SceneAppearanceClientPlayer`（442–447） | 大厅右上角 `appearance-entry` → `AppearanceUiRoot.ShowPanel()` | 买卡前后竞技快照与计分规则完全一致 |
| 排行榜与封榜快照 | `ClientPlayerRank.GetRankPage`（292）/ `GetRankAround`（272），`board={board_type:MATCH_PVP_TEAM,scope:GLOBAL,period:season_key,generation:rank_generation}` | `RankedUiRoot` 内 `RankClient` | 必须使用 `current_season.{season_key,rank_generation}` 或历史季封榜键 |

### 2.2 分场景真实消息、字段、单位与确认时点

1. **大厅**：
   - 进厅发 `448 GetRankedProfileRequest { include_history: true }`；重登/切角/重连后清本地缓存并重拉 448。
   - 改选预设发 `449 SaveRankedPresetRequest { character_preset_id, pet_preset_id, rule_version }`；**确认时点**：收到 `SaveRankedPresetResponse` 且 `error_message.id == 0` 才写回 `SelectedPresets`（`16035` 预设无效、`16036` 规则版本过旧、`16034` 上季结算中）。
   - 组队中确认预设发 `452 ConfirmRankedPresetRequest { expected_team_id, character_preset_id, pet_preset_id, rule_version, request_id }`；**确认时点**：收到 `TeamResponse`（带新 `TeamView.ranked_presets_all_confirmed` / 成员 `ranked_preset_confirmed`）或 `213 NotifyTeamSnapshot` 推送。
2. **报名 / 取消**：
   - 单人发 `157 JoinQueueRequest { player_id, mode: MATCH_MODE_PVP_TEAM(7), battle_config_id: 5, team_size: 1..5, character_preset_id, pet_preset_id, ranked_rule_version, request_id }`；整队由队长发 `211 StartTeamMatchRequest { battle_config_id: 5, expected_team_id, target_team_size: 1..5, match_mode: MATCH_MODE_PVP_TEAM(7), ranked_rule_version, request_id }`。
   - 排队期间每 3 秒轮询 `153 GetQueueStatusRequest { player_id, request_id }`，读取 `state`、`queued_seconds`（秒）、`wait_prompt_seconds`（默认 120 秒）、`current_rating_spread`（分差点数，100/200/300 封顶）、`team_size`、`season_id`。
   - 取消发 `148 CancelQueueRequest { player_id, queue_ticket, request_id }`；**确认时点**：非全局单排以 148 成功回包（且未抢先开战）收敛，全局/随队补位必须以 `153 GetQueueStatusResponse.state == NOT_QUEUED`（且若存在未决加入意图则 `request_cancelled == true`）为准，确保取消与成局互斥。
3. **入战 / 重连**：
   - 收到 `143 NotifyBattleStart` 或重连 `144 NotifyBattleReconnect` + `140 GetBattleState` 进入既有 `BattleScreen`；零宠物账号使用 `pet_id = 0` 且 `competitive_temporary = true` 的局内 actor ID 正常参战与重连。
4. **个人结算**：
   - 现场 `150 BattleEndS2C`（或重登 `371 GetRecentBattleResults` 返回未读 `PlayerBattleResult`）触发 `RankedSettlementPanel`，弹出即调用 `372 AckBattleResults`（`NoteShown`）；同时发 `451 GetRankedBattleResultRequest { battle_id }`。
   - 若 451 返回 `status = RANKED_MATCH_RATING_PENDING(1)`，按 `2/3/5/8/10/12s` 有界轮询并保留「积分待确认」，直到 `CONFIRMED(2)` / `VOID(3)` / `ZERO_ROUND(4)`。
5. **榜页 / 赛季记录 / 已获外观**：
   - 榜页：`292 GetRankPage` / `272 GetRankAround`；
   - 赛季记录与历届榜首：`448 GetRankedProfile` 的 `current_season`、`current_progress`、`history[]`、`past_seasons[]`；
   - 外观佩戴与隐藏玉符：`450 UpdateRankedCosmeticRequest { loadout }`，服务端确认后更新。

## 3. 脱敏样例（字段名严格对应交付 C# / Proto 绑定）

### 3.1 正常路径（大厅 448 + 结算 150 + 451 跨段确认）
```json
{
  "getRankedProfileResponse_448": {
    "currentSeason": { "seasonId": 1, "seasonKey": "s1", "seasonName": "首测赛季", "rankGeneration": 1, "ruleVersion": 1, "state": "RANKED_SEASON_STATE_ACTIVE", "battleConfigId": 5, "waitPromptSeconds": 120 },
    "currentProgress": { "seasonId": 1, "seasonKey": "s1", "qualified": true, "ratingCenti": 154000, "currentTier": "RANKED_TIER_QINGFENG", "peakRatingCenti": 154000, "peakTier": "RANKED_TIER_QINGFENG", "validMatches": 7, "wins": 4, "losses": 3, "draws": 0 },
    "selectedPresets": { "characterPresetId": 101, "petPresetId": 203 },
    "cosmeticLoadout": { "equippedAvatarPatternCode": "s1_avatar_pattern", "equippedCardBgCode": "", "equippedTalismanCode": "", "hideTalisman": false }
  },
  "battleEndS2C_150": {
    "battleId": 9001001, "outcome": "BATTLE_OUTCOME_SIDE_A_WIN", "seasonId": 1, "materialCreditUnitsAdded": 10,
    "settlement": { "battleId": 9001001, "competitiveRanked": true, "seasonId": 1, "matchMode": 7, "totalRounds": 6, "participated": true }
  },
  "getRankedBattleResultResponse_451": {
    "result": {
      "battleId": 9001001, "seasonId": 1, "teamSize": 2, "status": "RANKED_MATCH_RATING_CONFIRMED",
      "outcome": "BATTLE_OUTCOME_SIDE_A_WIN", "playerTeamIndex": 0, "totalRounds": 6, "participated": true, "fled": false,
      "ratingBeforeCenti": 154000, "ratingAfterCenti": 155600, "ratingDeltaCenti": 1600,
      "tierBefore": "RANKED_TIER_QINGFENG", "tierAfter": "RANKED_TIER_YUNZHANG",
      "newlyQualified": false, "seasonValidMatchesAfter": 8,
      "newlyUnlockedRewards": [{ "seasonId": 1, "kind": "RANKED_REWARD_CARD_BACKGROUND", "tierValue": 8, "itemCode": "s1_card_bg" }]
    }
  }
}
```

### 3.2 等待 120s 提示与取消确认（153）
```json
{
  "waiting120s_153": { "state": "QUEUE_STATE_QUEUED", "queuedSeconds": 122, "waitPromptSeconds": 120, "currentRatingSpread": 300, "teamSize": 2, "seasonId": 1, "queueTicket": "tkt_masked_01" },
  "cancelledConfirmed_153": { "state": "QUEUE_STATE_NOT_QUEUED", "requestCancelled": true, "queueTicket": "" }
}
```

### 3.3 对局作废（150 + 451）
```json
{
  "battleEndS2C_void_150": { "battleId": 9001002, "outcome": "BATTLE_OUTCOME_VOID", "seasonId": 1, "materialCreditUnitsAdded": 0, "settlement": { "outcome": "BATTLE_OUTCOME_VOID", "competitiveRanked": true, "seasonId": 1 } },
  "getRankedBattleResultResponse_void_451": { "result": { "battleId": 9001002, "seasonId": 1, "teamSize": 5, "status": "RANKED_MATCH_RATING_VOID", "participated": false, "ratingDeltaCenti": 0 } }
}
```

### 3.4 积分待确认（451 `PENDING`）
```json
{
  "getRankedBattleResultResponse_pending_451": {
    "result": { "battleId": 9001003, "seasonId": 1, "teamSize": 1, "status": "RANKED_MATCH_RATING_PENDING" }
  }
}
```

### 3.5 日材料额度已满（150 `+0` + 451 赛季照记）
```json
{
  "battleEndS2C_capFull_150": { "battleId": 9001004, "outcome": "BATTLE_OUTCOME_SIDE_A_WIN", "seasonId": 1, "materialCreditUnitsAdded": 0, "settlement": { "competitiveRanked": true, "seasonId": 1, "matchMode": 7, "totalRounds": 5, "participated": true } },
  "getRankedBattleResultResponse_capFull_451": { "result": { "battleId": 9001004, "seasonId": 1, "teamSize": 3, "status": "RANKED_MATCH_RATING_CONFIRMED", "participated": true, "ratingDeltaCenti": 1600, "seasonValidMatchesAfter": 9 } }
}
```

### 3.6 上季结算中（448 + 157 拒绝）
```json
{
  "getRankedProfileResponse_settling_448": {
    "currentSeason": { "seasonId": 1, "seasonKey": "s1", "seasonName": "首测赛季", "rankGeneration": 1, "ruleVersion": 1, "state": "RANKED_SEASON_STATE_SETTLING", "rankBoardSealed": false }
  },
  "joinQueueResponse_settling_157": {
    "errorMessage": { "id": 16034 }
  }
}
```

## 4. 本次客户端验收记录

- `pwsh -NoProfile -File tools/client_compile_check.ps1 -ShowErrors 30`：`exit=0, files=662, errors=0, refs=csproj (MmorpgClient.csproj, 270 refs)`。
- `RankedClientTests`（Roslyn + Unity cached NUnit 反射实跑）：**4/4 PASS**。
- 资源与 `.meta` 校验：`Assets/Resources/UI/Ugui/LunjianV1/*.png` 恰为 **47** 张（全套 SHA-256 与 `asset-manifest.json` 一致），`ui-window.png` 与 `button-primary-cloud.png` 不存在，`MISSING_META_COUNT=0`，`ORPHAN_META_COUNT=0`。
- 待现场真机/Editor PlayMode 验收项：Unity Editor 实景运行下核对 1920x1080 / 异形屏安全区 / 小游戏右上角胶囊避让的视觉效果及连双版真机进程全链路交互。
