# 组队 UI

2026-09-13 v2：实际客户端已改为「队伍成员 + 申请列表」双栏同屏，移除「已同意」页面和入口。设计来源为素材仓库 `E:/work/image/designs/team-ui-v2`；运行时使用独立 uGUI 控件与原生 TMP 文字。

## 入口和布局

主城左侧任务追踪下方的「组队 [T]」入口沿用。T 打开或关闭，Escape 关闭；窗口显示期间阻挡地图移动。进战斗、断线、退出或更换角色时关闭窗口，断线和换角同时清空组队资料。

- 左侧成员使用五条横行：清晰头肩头像、姓名、等级、门派、队长／队员、自己和在线状态。空席降低强调；容量大于五时独立分页。
- 右侧申请列表与成员始终同时可见：每页最多四人，显示头像、姓名、等级、门派、拒绝／同意按钮。一到两名申请使用宽松卡片，三到四名使用紧凑行。
- 两份名单的页码分别维护，翻动一侧不会改变另一侧。单页隐藏翻页控件；名单收缩时各自纠正页码。
- 标题保留国风字形，正文与操作按钮使用随包的 Noto Sans SC；该字体仅应用于组队窗口。玉绿、米白、暖金控件、灯笼和短流苏继续使用项目正式资源。
- 头像沿用 `QdaoCharacterCatalog` 的当前角色来源，通过运行时圆形遮罩显示头肩区域，没有重画或替换角色身份。

`Show(TeamPage.Members/Applications)` 保留为打开入口，两栏都会显示。旧 `Show(TeamPage.Approved)` 调用映射到成员，历史数据不会显示。`TeamSnapshot.Approved` 暂留数据兼容；新界面不读取或维护它。

## 行为约束

名单由权威快照提供。点击同意／拒绝只发出意图，不在实际客户端乐观修改名单；服务成功回包之后，申请从右侧移除，加入者出现在左侧。非队长、服务不可用、正在请求时禁止处理；满员时禁止同意、保留拒绝。刷新在请求期间禁用。请求十秒超时后允许重试，过期回包无效。

名字和门派按纯文本渲染，长文本省略显示，不能被解释为 TMP 富文本。分页后、申请行删除后和窗口关闭后恢复到有效控件焦点。

门派优先使用 `SchoolName`，缺省时沿用已知职业 1=破军、2=玄霄、3=丹心、4=逐风。资料不完整时显示待同步。

## 当前服务边界

服务端仍未提供主城组队创建、申请、同意、拒绝或成员资料 RPC。`TeamInfo` 只有队伍 ID、队长 ID 和成员 ID，不能推断陌生玩家的名字、等级、门派或头像。因此正式游戏继续明确显示「组队暂未开放」，不会生成假成员或模拟线上入队成功。本轮完成界面接入与离线验证，不扩展服务器组队系统。

后续连接真实服务时，向 `TeamUiRoot.State` 提供包含 `LocalPlayerId` 的完整快照；订阅 `RefreshRequested(generation)` 和 `DecisionRequested(generation, playerId, approve)`，并使用 `Complete`／`Fail` 结束请求。服务推送应核验连接世代与当前角色，不能将战斗匹配队伍当作主城组队。

## 验证与预览

- 运行 `tools/client_compile_check.ps1` 检查全部运行时代码。
- Unity EditMode 过滤 `MmorpgClient.Tests.EditMode.Tianyong.Team`，覆盖窗口、快照与请求状态。
- Unity PlayMode 过滤 `MmorpgClient.Tests.PlayMode.TeamWindowPlayModeTests`，验证实际生命周期中的输入阻挡与释放，以及真实 EventSystem 的开启／关闭、快照重建、申请删除和分页收缩焦点。
- 编辑器菜单 `MMORPG > UI > Capture team screens (offline)` 输出至 `.codex-artifacts/team-ui-v2`。两分辨率各十张，覆盖双栏、申请翻页、满员、成员翻页、独立页码、申请空态、服务不可用和处理中。
- 运行模式下使用 `MMORPG > UI > Preview team UI (offline)` 打开离线交互预览。界面标明示例角色，同意后仅演示移入成员；关闭时清理临时窗口。

旧 `.codex-artifacts/team-ui` 图片及测试属于之前三页版本，不能用于 v2 验收。当前结果记录在 v2 目录和素材仓库交付说明中。

字体：`Assets/Resources/Fonts/TeamNotoSansSC.otf`，来自本机已有 Noto Sans SC 字体，按 SIL Open Font License 随包保留 `TeamNotoSansSC-LICENSE.txt`；项目来源 [Noto CJK](https://github.com/notofonts/noto-cjk)。

### 2026-09-13 最终结果

Unity 6000.6.0f1：EditMode 27/27、PlayMode 6/6，均为 Passed；CaptureAll 成功生成两种分辨率共 20 张截图。截图逐一断言所有可见非空文字包含可见字形，避免字体行高让整个名字被省略；手工复核双申请、紧凑列表、满员、空态、未连接、处理中和独立分页。

实际截图、两份原始测试 XML、源码 SHA-256 与全部截图清单位于 `E:/work/image/designs/team-ui-v2`，汇总记录为 `qa/client-integration-report.json`。此次验收只覆盖客户端 UI 与离线快照，服务器边界见上文。
