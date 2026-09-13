# 组队 UI

主城左侧、任务追踪下方增加「组队 [T]」入口。T 打开或关闭，Escape 关闭；窗口显示期间阻挡地图移动。进战斗、断线、退出或更换角色时关闭窗口，断线和换角同时清空组队资料。

## 页面

- 队伍成员：角色头像、名字、等级、门派、队长/队员、自己及在线状态；默认五席，容量更大时分页。
- 入队申请：每页四名角色，显示完整资料与同意/拒绝按钮。
- 已同意：展示已处理角色，当前仍在队伍中的角色显示「已加入当前队伍」。

名单由快照提供。同意按钮只发出意图；只有权威快照才能更新申请、成员与已同意列表。非队长、服务不可用、正在请求时禁止处理；满员时禁止同意，保留拒绝。请求十秒超时后允许重试，过期回包无效。

## 当前服务边界

目前服务端没有组队创建、申请、同意、拒绝或成员资料 RPC，也没有对应消息号。`TeamInfo` 只有队伍 ID、队长 ID 和成员 ID，不能据此推断陌生玩家的名字、等级、门派或头像。因此正式游戏显示「组队暂未开放」，不生成假成员或模拟线上入队成功。

`TeamRole` 使用已有 `QdaoCharacterCatalog` 加载角色头像；门派优先使用 `SchoolName`，缺省时沿用已知职业 1=破军、2=玄霄、3=丹心、4=逐风。外观资料不完整时显示「待同步」。名字与门派按纯文本渲染。

## 后续网络接入

运行时状态位于 `TeamUiRoot.State`，窗口可单独通过 `TeamWindow.SetState` 绑定。

1. 连接真实组队服务后，提供包含 `LocalPlayerId` 的完整 `TeamSnapshot`，调用 `SetSnapshot`。
2. 订阅 `TeamUiRoot.RefreshRequested(generation)` 与 `DecisionRequested(generation, playerId, approve)`，发送实际协议。
3. 成功时调用 `State.Complete(generation, snapshot, status)`；失败调用 `State.Fail(generation, status)`。
4. 服务主动推送可使用 `SetSnapshot`；网络适配器必须先核验连接世代和当前角色，丢弃旧会话推送。
5. 已同意列表必须由服务端提供，不能仅通过本地点击历史推断。不要混用战斗匹配队伍和主城组队。

## 验证

运行 `tools/client_compile_check.ps1` 检查全部运行时代码。Unity EditMode 过滤 `MmorpgClient.Tests.EditMode.Tianyong.Team` 可运行状态和窗口交互回归。

编辑器菜单 `MMORPG > UI > Capture team screens (offline)` 渲染两种分辨率的成员、申请、已同意、满员、空列表、服务不可用与处理中状态，输出到 `.codex-artifacts/team-ui`。示例角色只存在于 Editor 验证代码。

运行模式下可通过 `MMORPG > UI > Preview team UI (offline)` 打开离线交互预览，演示审批及名单更新；关闭预览会销毁临时窗口。

2026-09-13 既有运行记录：运行时代码编译通过；07:08 UTC 的 29 项 EditMode 与 07:09 UTC 的 3 项 PlayMode 测试全部通过；2560×1080、1920×1080 两种分辨率共 18 张截图生成成功，已检查成员、申请和已同意页布局。测试结果保存于 `.codex-artifacts/team-ui/tests.xml` 和 `playmode-tests.xml`。此前失败的输入阻挡用例 `OpeningSwitchingTabsAndClosing_ReleasesOnlyThisWindowsInputBlocker` 已移至 PlayMode，并于 07:09:21 UTC 通过。这些记录对应当时的文件版本；当前窗口的缺省职业名称随后有更新，本次文档核对未重新运行 Unity 全套测试。未进行联机组队验证（服务端尚无接口）。
