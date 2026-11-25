# 瑞兽迎春活动 UI 与交互接入设计（`FestivalBeastV1`）

## 1. 需求分析

「瑞兽迎春」为单人携本人合法出战宝宝挑战「春禧兽」的轻量 14 天春节活动，UI 层仅交付 5 处业务表面（复用既有宝宝管理、材料自选领取与人物易容页面，不新增难度选择、活动币、碎片兑换、通行证或 NPC 跑腿导航）：

1. **活动列表入口卡片**：嵌入现有 `GameplayWindow.Activities.cs`，识别服务端 `PlayerActivityEntryKind.FestivalBeast (2)`，展示活动阶段、胜场进度与永久待领提醒，点击直达主面板。
2. **活动主面板（`FestivalBeastMainPanel`）**：对照 `D:\work\image\designs\spring-beast-ui-20261124\screens\01-activity-main.png`，展示活动名、规则/关闭按钮、北京时间（UTC+8）起止时间与倒计时、春禧兽展示台与立绘、累计 3 胜单一目标（3 节点 + `X/3`，非 3 个领奖箱）、当前出战宝宝准备区与「查看宝宝」入口、分离展示的「当前可赚材料额度」与「已赚取未领取跨日材料余额」、严格按优先级切换的底部主按钮，以及低优先级「人物外观」入口（不在失败/领奖态推销）。
3. **规则弹窗（`FestivalBeastRulesDialog`）**：对照 `02-rules.png`，4 条清晰规则行 + 动态文字「知道了」。
4. **既有战斗界面重击预告（`FestivalBeastBattleOverlay` + `BattleScreen` + `BattleUnitView`）**：对照 `04-warning.png`，在战斗中将回合数显示为 `第 N/12 回合`；R2/5/8/11 显示「下回合：春禧兽将重击【名字】」、R3/6/9/12 显示「本回合重击【名字】，在人宠正常行动后发动。」及防/控短提示；目标单位头顶叠加图标+形状+文字（不只靠颜色）；预告生效/取消/兑现严格以服务端字段为准，不凭控制图标自行取消。
5. **既有结算界面活动区块（`FestivalBeastSettlementSection` + `BattleResultPanel`）**：对照 `03-victory.png`，按 6 种结算态（普通胜利、首次三胜、已有资格再胜、已拥有再胜、失败/逃跑/作废、结算处理中）动态呈现，材料文案统一用「本局新增可领取额度 +N」，领取神兽须等服务端 `durable=true` 才确认成功且不自动顶替出战宝宝。

## 2. 设计分析与美术资产来源

- **素材导入**：从 `D:\work\image\designs\spring-beast-ui-20261124\unity-slices\png\` 逐字节导入全部 19 张 PNG 到 `Assets/Resources/UI/Ugui/FestivalBeastV1/`，全部校验 SHA-256 一致，并按 `manifest.json` 精确写入 Unity `TextureImporter` `.meta`（`Sprite Single`、`FullRect`、`100 PPU`、`Bilinear/Clamp`、关闭 Mipmap 及精确 `spriteBorder: {x:L, y:B, z:R, w:T}`）。
- **伸缩约束**：
  - `sliced`（`window_frame`、`info_row`、`content_card`）：uGUI `Image.Type.Sliced`。
  - `horizontal`（`button_primary`、`button_secondary`、`warning_bar`）：uGUI `Image.Type.Sliced` 且保持原高比、仅横向伸缩。
  - `progress_fill`（全不透明绿色填充）：放于 `RectMask2D` 裁切轨道内，绝不溢出金边。
  - `simple`（`title_plate`、`button_close`、`beast_illustration`、`beast_portrait`、`pet_display`、`pet_info_slot`、`portrait_frame`、`progress_track`、`progress_done`、`progress_pending`、`activity_icon_taiji`、`festival_icon_lantern`）：`Image.Type.Simple` + `preserveAspect = true`。
- **双形态平台合规**：零多线程、零 `Reflection.Emit`、零悬停依赖；主容器全量响应 `Screen.safeArea` 避让异形屏与小游戏右上角胶囊；所有按钮触控高度 ≥ 52 逻辑像素（在 `1280×720` / `1920×1080` / `2560×1080` 均满足 ≥ 44pt 触控规范）。

## 3. 架构分层与思想分析

- **纯 C# 数据与恢复层**（零 `UnityEngine`）：`FestivalBeastClient` + `FestivalBeastModels` + `FestivalBeastUiState`，负责全部权威 RPC（476–478）、本机磁盘与跨设备无 UUID 恢复、UTC+8 时间格式化、按钮优先级与全部 18+ 状态文案推导。
- **uGUI 视图层**（`Assets/Scripts/UI/Ugui/FestivalBeast/`）：仅订阅 `FestivalBeastClient` 事件并渲染 `FestivalBeastUiState` 产出的只读视图模型；关闭面板绝不清请求、不放弃资格、不等于逃跑。
- **既有界面最小增量集成**：`GameplayWindow.Activities.cs` 识别 `FestivalBeast` 入口卡片；`BattleScreen.cs` 显示 `第 N/12 回合`；`BattleUnitView.cs` 补充头顶文字标签；`BattleUiRoot.cs` 嵌入 `FestivalBeastSettlementSection`。所有既有未提交改动保持完整。

## 4. 漏洞与一致性防线

- **严禁客户端算资格/胜场/发奖**：按钮状态与文案唯一依赖 `FestivalBeastDetailSnapshot` 与 `FestivalBeastSettlementSnapshot`。
- **领奖持久确认门禁**：`RewardClaimed && !RewardClaimDurable` 时锁定为「正在确认领取」，不提前显示已获得或弹祝贺。
- **已结束且已领取**：主按钮隐藏（不出现「继续挑战」）。
- **额度隔离**：严格区分「今日/当期可赚额度」与「已赚未领跨日余额」，绝不混淆，也不写成“已入包”。
- **开发演示隔离**：仅 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 提供只读演示快照生成器，不触碰任何写接口或玩家资产；线上 100% 绑定 `GameClient.FestivalBeast`。

## 5. 执行步骤与验收范围

1. 逐字节导入 19 张切图并按 `manifest.json` 生成精确 `.meta`。
2. 实现纯 C# `FestivalBeastUiState.cs`（覆盖全部主面板、规则、战斗预告、结算 6 态与开发演示状态）。
3. 实现 `FestivalBeastUiArt`、`FestivalBeastMainPanel`、`FestivalBeastRulesDialog`、`FestivalBeastBattleOverlay`、`FestivalBeastSettlementSection`、`FestivalBeastUiRoot`。
4. 最小增量接入 `GameplayWindow.Activities`、`GameplayUiRoot`、`BattleUnitView`、`BattleScreen`、`BattleUiRoot`。
5. 编写全状态 EditMode 单测 `FestivalBeastUiStateTests.cs`，通过 `client_compile_check.ps1` 与 Roslyn NUnit 全量测试。

> 验收边界：本次完成客户端源码、切图导入、纯 C# 状态机与编译/单测验证；未启动 Unity Editor 执行真机像素截图对比，真机屏幕与图集压缩视觉验收须在 Unity 编辑器/目标设备补测。
