# 主界面系统入口

2026-10-22 重新设计：以 `image/designs/mail-ui-v1/01-mail-event.png`、`team-ui-v2/team-ui-v2.png` 为主要视觉依据，物件画法承接 `gameplay-ui/02-inventory.png`。主界面使用米白纸绢、玉绿选中页签、细暖金云纹、楷书文字和独立轮廓的道家 Q 版物件。

四个分类使用完整纸绢页签，展开为两列图文纸卡。面板随入口数量收高，只有一个入口的交易分类使用窄纸窗。地图和线路合为一块信息纸牌，背包为布袋与纸质名签。战斗、观战、角色沿用既有右侧常驻位置与点击尺寸，并使用统一的新纸卡。

| 分类 | 收纳的入口 |
| --- | --- |
| 修行 | 宝宝、技能 |
| 历练 | 任务、活动、论剑榜 |
| 仙友 | 组队、帮会、仙友会、仙笺 |
| 交易 | 聚宝斋 |

再次点击分类、点击关闭或面板外区域、按 Escape 均可收起。打开系统后收起分类。组队申请、邀请和新邮件继续更新原入口文案，并在“仙友”页签聚合待处理提示。各系统继续维护快捷键、服务端线路信息、战斗重连文案与禁用状态。

## 接入与素材

- [WorldHudNavigation.cs](../Assets/Scripts/UI/Ugui/WorldHudNavigation.cs) 管理布局与显示。原按钮保留在所属 HUD 内，用包装节点的 CanvasGroup 收纳，保留业务回调及所属 Root 的可用性控制。
- 分类底板排序 139，原系统 HUD 排序 140，正式窗口位于其上。队伍暂离／归队继续由原系统控制。
- 技能入口沿用角色系统可用性，调用现有 `SkillUiRoot.Open()`。
- 本版完整使用 `Assets/Resources/UI/Ugui/WorldHud20261022/` 的 16 枚物件图标和 6 张纸绢控件。关闭和待处理提示沿用已有资源。美术、提示词、逐图来源和派生关系位于 `image/designs/main-hud-20261022-v2/`；内置绘图入口未披露的实际型号与质量在记录中为未确认。
- 字体使用客户端既有楷书。图标等比显示，纸窗和纸卡按各自 Sprite 边界九宫格缩放。

## 复查

Unity 编辑器入口：`MMORPG/UI/Capture categorized world HUD`。本版结果输出到 `.codex-artifacts/world-hud-20261022/`，覆盖四个分辨率及收起／四分类状态。运行 `MmorpgClient.Tests.EditMode.Battle.HudCanvasLayeringTests` 检查正式 Root 的排序与射线层级。

本版已留存[原生验证结果](VerificationEvidence/world-hud-20261022/verification.json)：32 项检查通过，包含四个分辨率下的 20 张原生截图。可查看[收起布局](VerificationEvidence/world-hud-20261022/00-closed_2560x1080.png)和[修行分类](VerificationEvidence/world-hud-20261022/01-cultivation_2560x1080.png)。验证范围为隔离的编辑器原生 UI 样例，无网络或在线账号。

`image/designs/main-hud-20261022-v2/preview.html` 是使用相同资源和几何的离线交互预览；入口反馈仅为设计演示，不发送游戏请求。

上一版的原生截图、包核对与在线入场记录保存在 [2026-10-21 验证目录](VerificationEvidence/world-hud-20261021/verification.json)，仅对应上一版。此前在线入场在主城 HUD 出现前被服务端以 `tip=3023` 拒绝，本版通过隔离的编辑器样例验证布局与交互，在线效果须在可进入主城的会话中另行验收。
