# 仙笺邮件原生 UI

已新增 native uGUI 邮件模块，复用道家 Q 版手绘皮肤，文字独立渲染。设计坐标为 2560 × 1080，CanvasScaler 使用 Expand；编辑器验收同时覆盖 2560 × 1080 与 1920 × 1080。

## 正式入口与服务边界

`MailUiRoot` 在进入游戏、网关就绪且非战斗界面时显示主城“仙笺 [N]”入口，设计坐标 (68, 808)、模态 Canvas 排序 189。正式入口始终从 Unavailable 开始，不加载任何样例。当前没有接入邮件在线 RPC；点击正式入口显示“邮件暂未开放”。本模块不会发放背包道具、编造服务端成功或持久化账号数据。

显示模态窗口时使用现有 `GameplayInputBlocker` 隔离主城输入。关闭窗口/对象销毁时释放；Escape 优先关闭确认框，再关闭邮件。打开邮件会关闭已有 Team/Guild/Jubaozhai/Social 等主城窗口。切换账号、网关连接或退出登录会关闭并清空邮件状态。

## 独立离线预览

`MailPreviewHost` 只在显式预览宿主中使用，不自动注入正式会话。六封离线样例分别为带活动图的待领邮件、维护补偿、无附件通知、试炼奖励、已领取、已过期。打开信件标记已读，领取另行操作；所有变更仅在该预览内存中。

Unity 菜单：

- `MMORPG/UI/Preview mail UI (offline)`：临时离线窗口，不保存当前场景。
- `MMORPG/UI/Close mail preview`：关闭临时预览。
- `MMORPG/UI/Capture mail screens (offline)`：原生窗口八态 × 两种分辨率，输出 `.codex-artifacts/mail-ui-v1/`。截图在独立 PreviewScene 中完成，不切换当前活动场景；截图前检查底部操作按钮处于窗口和画面边界内，且不属于滚动内容。
- `MMORPG/UI/Create mail offline preview prefab and scene`：显式创建 `Assets/Prefabs/UI/MailOfflinePreview.prefab` 与 `Assets/Scenes/MailOfflinePreview.unity`。若已存在则拒绝覆盖。不会写入构建列表或覆盖用户正在编辑的场景。

活动详情弹窗明确标注“离线设计样例 · 非真实开放活动”。正式活动跳转只有接入 `MailWindow.ActivityRequested` 后才启用，宿主负责已验证的活动路由。

## 状态与权威适配接口

`MailUiState` 与 Unity 分离，可注入 Unix 秒时钟。调用 `SetSnapshot` 接收权威邮件快照；返回的 Messages/Selected/Rewards 都是深复制。模型会过滤空 ID、重复 ID 和非正数附件。到期规则为 ExpiresAt > 0 且 ExpiresAt <= Now；ExpiresAt=0 表示无期限。

适配器订阅 `ActionRequested(MailRequest)`，按 Operation 与 MessageIds 发请求。请求包含 UI Generation；完成时调用 `Complete(generation, authoritativeMessages, status)`，明确失败用 `Fail(generation, message)`。读取、领取、批量领取、删除、清理和刷新都是独立请求。未收到匹配代次的完整权威快照前不会乐观领取或删除；账户/快照变更后旧代次回包被拒绝。Generation 仅用于 UI 回包关联，未来服务端仍须提供事务与幂等保障。

变更请求超过 10 秒后，结果可能已在服务器执行，状态进入 RequiresRefresh，禁止再次变更，仅允许刷新。刷新本身再次超时仍保持锁；收到权威快照后才恢复。适配器对“传输断开、结果未知”应保留该保护，不能把未知结果当作明确业务失败。

- `LoadDemo`：显式重置为六封内存样例，仅离线宿主使用。
- `Reset(playerId)`：失效旧代次并清空账号状态。
- `Select/SetCategory/MarkSelectedRead`：阅读动作，不领取。
- `ClaimSelected/ClaimAll`：仅有效、未领取且存在正数附件的邮件；一键领取跨分类。
- `DeleteSelected`：保护有效待领附件。
- `ClearRead`：仅清理已读且无有效待领附件的邮件，保留未读通知。
- `Refresh`：请求新权威快照；结果未明时是唯一允许的请求。
- `Tick`：更新请求超时与到期状态。

`MailWindow` 提供 State、Show/Hide/Back/Dispose、Root/Frame、ListScroll/BodyScroll/RewardScroll。列表与正文独立滚动；附件固定区域可横向滚动，承接任意数量附件，底部按钮不会被长正文或附件遮挡。删除/清理二次确认会复核目标状态；确认框默认焦点是取消。

## 资源

资源路径：`Resources/UI/Ugui/MailV1/`，沿用获准手绘 PNG 原名。活动横幅使用 `event-midautumn`；仅 `preview-midautumn` 活动在图上叠中秋文字，其他邮件不会自动附加中秋文案。所有动态文字 richText=false，正文 Noto Sans SC，标题现有 SimKai。

按 Unity Vector4 (left, bottom, right, top)：
| 素材 | Sprite border |
|---|---|
| window_frame | (97, 74, 97, 99) |
| button_primary | (66, 30, 66, 30) |
| button_secondary | (30, 27, 30, 27) |
| stat_field | (12, 12, 12, 12) |
| tab_horizontal | (27, 24, 27, 24) |
| 其他图 | (0, 0, 0, 0) |

正式纹理导入由本轮统一资源导入器负责，本模块没有重复导入器。

## 验证

测试程序集：`MmorpgClient.Tests.EditMode.Mail`。当前提供 13 项纯状态测试和 10 项原生窗口测试，覆盖正式不可用、阅读领取分离、附件与删除规则、精确到期、快照深复制、权威回包、账号隔离、超时刷新锁、固定按钮和滚动遮罩、12 附件、富文本防解释、模态焦点和输入隔离。

本文件记录实现与验证入口；实际 Unity Test Runner 结果、编译和截图由统一客户端验收执行后记录，未把静态检查当作测试通过。
