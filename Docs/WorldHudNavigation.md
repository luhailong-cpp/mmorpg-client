# 主界面系统入口

主界面将原先分散的系统按钮收进四个分类，地图、线路、背包保留常驻入口。每个系统使用图标和中文名称共同识别。

| 分类 | 入口 |
| --- | --- |
| 修行 | 角色、宝宝、技能 |
| 历练 | 任务、活动、战斗、观战、论剑榜 |
| 仙友 | 组队、帮会、仙友会、仙笺 |
| 交易 | 聚宝斋 |

再次点击分类、点击关闭或面板外区域、按 Escape 均可收起。打开系统后收起分类。组队申请、邀请和新邮件仍通过原入口文案更新，并在“仙友”分类显示待处理提示。原快捷键、服务端线路信息、战斗重连文案与按钮禁用状态继续由各系统维护。

## 接入与素材

- [WorldHudNavigation.cs](../Assets/Scripts/UI/Ugui/WorldHudNavigation.cs) 管理分类、布局和显示。原按钮保留在各自 HUD 层级内，使用包装节点的 CanvasGroup 收纳，保留业务回调及所属 Root 的可用性控制。
- 原系统 HUD 保持排序 140，分类底板为 139，窗口仍在其上方。队伍“暂离／归队”保留原有条件直达操作。
- 通过现有角色入口的可用性控制新增技能书快捷入口，调用现有 `SkillUiRoot.Open()`。
- 八枚新图标位于 `Assets/Resources/UI/Ugui/WorldHud20261021/`。其余图标、米白面板、玉绿标题牌及金边按钮复用现有资源。风格依据 `image/designs/mail-ui-v1/01-mail-event.png` 和 `image/designs/team-ui-v2/team-ui-v2.png`；生成与派生记录保存在 `image/designs/main-hud-20261021/`。

## 验证记录

2026-10-21 的原生 uGUI 验证覆盖 2560×1080、1920×1080、1600×900、1280×720，共 20 张截图、32 组检查。检查包括分类展开收起、原按钮状态和回调、可点击区域及布局、动态重连文案、组队与邮件提示聚合及清除。`HudCanvasLayeringTests` 验证正式 Root 的画布排序及射线检测，1 项通过。

- [原生验收结果](VerificationEvidence/world-hud-20261021/verification.json)
- [HUD 层级测试](VerificationEvidence/world-hud-20261021/hud-layering-tests.xml)
- [常驻布局](VerificationEvidence/world-hud-20261021/00-closed_2560x1080.png)
- [修行分类](VerificationEvidence/world-hud-20261021/01-cultivation_2560x1080.png)
- [历练分类](VerificationEvidence/world-hud-20261021/02-adventure_2560x1080.png)
- [仙友分类](VerificationEvidence/world-hud-20261021/03-social_2560x1080.png)
- [交易分类](VerificationEvidence/world-hud-20261021/04-trade_2560x1080.png)

2026-10-22 继续核对已有成品 `Builds/play-20261021-ready/client/mmorpg.exe`：29 个 HUD 源码、图标及导入设置文件与构建输入清单一致，成品中的八个图标资源路径齐全，4 个关键二进制文件与构建清单一致。导航源码与成品 PDB 记录的哈希相符。详见[成品核对结果](VerificationEvidence/world-hud-20261021/package-audit-20261022.json)。

上述截图来自隔离的编辑器原生 UI 样例，成品核对为文件与资源检查。

2026-10-22 尝试正式播放器在线验收：复用已登录客户端，在已有角色的选角界面点击一次“进入仙境”，客户端在主城 HUD 出现前返回选服／登录视图。近期客户端日志记录服务端入场拒绝 `tip=3023`（进入场景失败），因此本次主城分类交互尚未验收通过。没有输入账号凭据、新建角色、打开邮件或触发交易、领奖及战斗；未修改服务端和数据库。详见[实机入场记录](VerificationEvidence/world-hud-20261021/online-entry-20261022.json)及[现场截图](VerificationEvidence/world-hud-20261021/online-entry-failure-20261022.png)。

## 启动与复查

手动启动入口：`Builds/play-20261021-ready/Start-Client.cmd`。它调用单实例启动脚本，传入 `-manual-play` 并清除自动驾驶相关环境变量；不自动登录或战斗。现有启动脚本指定本机网关 `http://127.0.0.1:8081`。

只检查启动计划、不启动进程：

```powershell
& .\Builds\play-20261021-ready\Start-Client.ps1 -PlanOnly
```

修改分类导航后，在 Unity 编辑器使用 `MMORPG/UI/Capture categorized world HUD` 复查，结果输出到 `.codex-artifacts/world-hud-20261021/`。布局及射线层级回归运行 `MmorpgClient.Tests.EditMode.Battle.HudCanvasLayeringTests`。
