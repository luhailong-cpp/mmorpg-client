# 战斗剧本面板(BattleScriptUI)

> 2026-11-12。服务端设计见服务端仓 `docs/design/battle-script.md`(§7 客户端协议)。**代码已写完,未编译、未在 Unity 里跑过**,待 Codex 按下面「验证」执行。

## 做了什么

- 主城 HUD 新入口「战斗剧本 [Y]」(论剑榜右侧一格,`WorldHudEntry.BattleScript`,冒险分类)。
- 窗口左边「我的剧本」:名字、审核状态(审核中 / 已通过 / 已驳回:原因 · 生效第 N 版)、选用、删除。只有至少通过过一次审核的剧本能选用。
- 右边:玩法选择(全部玩法 / 单人副本 / 组队副本 / 论剑 1v1 / 组队论剑 / 切磋)、该玩法当前选用与清除、官方剧本列表与选用、「导入剧本」:粘贴 protobuf JSON 提交为新剧本(进入待审)。
- 规则不合法时显示服务端给的定位(英文,如 `script 0 role 2 rule 10: unknown skill 4242`)。剧本整份不超过服务端回的 `max_script_bytes`(900 字节,gate 1KB 包长上限决定),客户端提交前先自查。
- 写请求(提交 / 删除 / 选用)超时按结局未知处理:刷新列表成功前不再发写请求。

## 文件

| 文件 | 说明 |
|---|---|
| `Assets/Scripts/Game/BattleScripts/BattleScriptClient.cs` | 请求与状态:单飞、代次与连接身份隔离、tip 文案、JSON 导入解析 |
| `Assets/Scripts/UI/Ugui/BattleScripts/BattleScriptUiRoot.cs` | 入口与生命周期(写法同 RankUiRoot) |
| `Assets/Scripts/UI/Ugui/BattleScripts/BattleScriptWindow.cs` | 窗口(全部代码搭建,不依赖预制体) |
| `Assets/Scripts/UI/Ugui/WorldHudNavigation.cs` | 枚举末尾追加 `BattleScript`(不挪动已有值)与说明文字 |
| 12 个面板的 `HideOtherPanels` 与 `RankUiRoot` | 大面板互斥:各加一行收起战斗剧本面板 |
| `tools/gen_proto.ps1` | 生成清单加 `generated/code/proto/tip/match_error_tip.proto`(面板用 `match_error` 枚举给文案);`battle_script.proto` 已在清单里 |
| `Assets/Tests/EditMode/BattleScripts/*` | 12 条 EditMode 用例与 asmdef |

## 重新生成协议

服务端仓先导表(新 tip 码)并生成协议,再在客户端仓执行:

```
pwsh -File tools/gen_proto.ps1 -ProtoRoot D:/luyuan/xuanming-server-mmo-cpp
```

核对 `Assets/Scripts/Proto/Generated/` 出现 `MatchErrorTip.cs`(含 `KMatchBattleScript*` 8 个码),`BattleScript.cs`、`MatchService.cs` 与服务端当前协议一致;`Assets/Scripts/Net/Generated/Rpc/MatchServiceRpc.cs` 有 `ListBattleScripts / SubmitBattleScript / DeleteBattleScript / SetBattleScriptLoadout` 四个消息号常量(由服务端 protogen 写入)。

## 验证(交 Codex)

1. `tools/client_compile_check.ps1` 离线编译检查通过。
2. EditMode:`MmorpgClient.Tests.EditMode.BattleScripts` 全绿;另跑一遍 `UiShutdownTests` 等全量 EditMode,确认新面板与互斥改动没有打红别的用例。
3. 实机(本机全栈):打开面板 → 粘贴一份剧本提交 → 运营工具 `battlescriptreview -list` 看到它并通过 → 刷新显示「已通过」→ 选用到「组队副本」→ 开挂机打一局,出招符合剧本;删除后选用消失。

## 未完成 / 待定

- **界面没有设计稿**:`D:/work/image/designs` 里没有战斗剧本面板的稿子,按客户端 AGENTS 的像素一致规则,本面板布局属于「未定稿」,现在复用论剑榜的框体、页签与按钮美术临时排版,定稿后按稿子重排。
- 可视化逐条编辑(下拉选条件 / 目标 / 行动)没做,先只有 JSON 导入;按副本细分的选用服务端已支持,界面只出「任意副本」。
