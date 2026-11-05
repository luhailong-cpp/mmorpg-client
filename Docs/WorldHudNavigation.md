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
- 神秘商人状态条通过 `WorldHudEntry.MysteryMerchant` 接入同一布局；倒计时、可用性与点击仍由商人系统控制，禁止复用旧版线路入口坐标。
- 技能入口沿用角色系统可用性，调用现有 `SkillUiRoot.Open()`。
- 本版完整使用 `Assets/Resources/UI/Ugui/WorldHud20261022/` 的 16 枚物件图标和 6 张纸绢控件。关闭和待处理提示沿用已有资源。美术、提示词、逐图来源和派生关系位于 `image/designs/main-hud-20261022-v2/`；内置绘图入口未披露的实际型号与质量在记录中为未确认。
- 字体使用客户端既有楷书。图标等比显示，纸窗和纸卡按各自 Sprite 边界九宫格缩放。

## 复查

2026-11-05 主界面改为官方 uGUI 响应式布局，入口、状态条、任务／组队栏和暂离／归队使用同一套可用区域。`WorldHudLayout` 使用 `HorizontalLayoutGroup`、`VerticalLayoutGroup`、`GridLayoutGroup`、`LayoutElement`、`ContentSizeFitter` 和 `ScrollRect` 解算排布；Canvas Scaler 管理显示比例。窗口变窄时顶部自动分行、分类自动改单列；窗口变矮时侧栏内容可滚动，保留选页及滚动状态。根节点使用锚点跟随当前 Canvas 视口与安全区，极小窗口按最小逻辑视口整体缩小以保持边界。

各功能按钮继续保留原 Canvas／会话祖先。布局占位的实际边界转换到所属 Canvas，分类遮罩及滚轮／拖拽同步到源按钮，避免移动层级破坏可用性或点击。只有视口、安全区、分类或入口结构变化时强制重建布局。

本次原生验证覆盖 640×480、800×600、1024×768、1280×720、1600×900、1920×1080、2560×1080、3440×1440、3840×2160、720×1280 和 1280×480，并在同一棵 UI 树中切换窗口尺寸。143 组检查、66 张截图通过，包含可见目标不重叠、原生射线点击、侧栏滚动到底部与收起／展开。输出：`.codex-artifacts/world-hud-responsive-20261105/verification.json`。此为隔离的原生 UI 验证，不建立在线账号或网络会话。

相关 EditMode 回归测试 126/126 通过：神秘商人、HUD 画布层级、侧栏短视口及状态恢复、跨 Canvas 分类按钮滚轮／拖拽。测试结果：`Logs/world-hud-responsive-20261105-tests.xml`。

留存证据：[布局报告](VerificationEvidence/world-hud-responsive-20261105/verification.json)、[126 项回归](VerificationEvidence/world-hud-responsive-20261105/editmode-tests.xml)、[小窗口常驻布局](VerificationEvidence/world-hud-responsive-20261105/00-closed_800x600.png)、[小窗口仙友分类](VerificationEvidence/world-hud-responsive-20261105/03-social_800x600.png)。完整 66 张截图保存在上述 `.codex-artifacts` 目录，可由原生验证器重建。

可运行副本为 `Builds/responsive-hud-20261105/mmorpg.exe`，基于 r4 的资源更新 Unity Development 播放器编译产出的运行时脚本程序集。完整资源重打包因内存压力停止，官方 scripts-only 打包缺少旧包的数据布局缓存，因此此副本作为脚本更新包交付，来源与程序集 SHA256 记录在包内 `ui-update-manifest.json`。启动检查成功进入原生 uGUI 选服流程，无托管异常；本地 `127.0.0.1:8081` 网关未连接，未做联机主城验收。

本版原生验收覆盖 2560×1080、1920×1080、1600×900、1280×720，共 20 张截图、32 组检查通过；正式 Root 的画布排序和射线层级测试 1/1 通过。

- [本版原生验收报告](VerificationEvidence/world-hud-20261022/verification.json)
- [本版窗口层级测试](VerificationEvidence/world-hud-20261022/hud-layering-tests.xml)
- [常驻入口](VerificationEvidence/world-hud-20261022/00-closed_2560x1080.png)、[修行](VerificationEvidence/world-hud-20261022/01-cultivation_2560x1080.png)、[历练](VerificationEvidence/world-hud-20261022/02-adventure_2560x1080.png)、[仙友](VerificationEvidence/world-hud-20261022/03-social_2560x1080.png)、[交易](VerificationEvidence/world-hud-20261022/04-trade_2560x1080.png)

Unity 编辑器入口：`MMORPG/UI/Capture categorized world HUD`。本版结果输出到 `.codex-artifacts/world-hud-20261022/`，覆盖四个分辨率及收起／四分类状态。运行 `MmorpgClient.Tests.EditMode.Battle.HudCanvasLayeringTests` 检查正式 Root 的排序与射线层级。

本版已留存[原生验证结果](VerificationEvidence/world-hud-20261022/verification.json)：32 项检查通过，包含四个分辨率下的 20 张原生截图。可查看[收起布局](VerificationEvidence/world-hud-20261022/00-closed_2560x1080.png)和[修行分类](VerificationEvidence/world-hud-20261022/01-cultivation_2560x1080.png)。验证范围为隔离的编辑器原生 UI 样例，无网络或在线账号。

`image/designs/main-hud-20261022-v2/preview.html` 是使用相同资源和几何的离线交互预览；入口反馈仅为设计演示，不发送游戏请求。

上一版的原生截图、包核对与在线入场记录保存在 [2026-10-21 验证目录](VerificationEvidence/world-hud-20261021/verification.json)，仅对应上一版。此前在线入场在主城 HUD 出现前被服务端以 `tip=3023` 拒绝，本版通过隔离的编辑器样例验证布局与交互，在线效果须在可进入主城的会话中另行验收。
