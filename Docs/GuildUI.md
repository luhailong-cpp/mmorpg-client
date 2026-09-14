# 帮会 UI 接入

2026-09-14 更新：[官方 MCP 切图与原生布局接入](GuildUI-MCP-20260914.md)，27 张 Sprite、28 项测试通过、20 张本次原生截图。下文保留 9 月 13 日业务接入记录。

2026-09-14 服务端接入（未编译、未联调）：帮会服务改为客户端可达并**按区服隔离**——建帮落在角色归属区服，只能浏览、加入本区帮会，排行为本区排行；身份与区服由服务端判定，请求里的 `PlayerId` / `ZoneId` 不再作为依据。帮名仍全服唯一。新增错误提示：帮名无效、帮名已被使用、公告过长、角色归属区服未确认。公告上限改为 200 字（服务端按 600 字节校验：Gate 单包上限 1KB，500 个汉字的包会在 Gate 被丢弃）。排行标题改为「本区帮会排行」。**必须先运行 `tools/gen_proto.ps1 -ProtoRoot E:\work\xuanming-server-mmo` 生成新错误码枚举，否则 `GuildClient.cs` 无法编译。** 服务端决策与验证清单见 `xuanming-server-mmo/docs/design/guild-zone-client-access.md`。

2026-09-13。正式入口位于主城左侧「帮会 [G]」（组队下方），也可按 G 打开；Escape 先关闭弹窗，再关闭帮会。进入战斗时关闭窗口，离线和换角时关闭窗口并清理会话状态。

界面使用原生 UGUI 与 TMP 文字，复用现有玉绿、米白、暖金手绘九宫格；新目录 `Assets/Resources/UI/Ugui/GuildV2/` 的 9 张独立图来自素材库已验收资产，原字节复制，不使用整屏静态图作为运行 UI。

## 已实现

- 六个页面：帮会总览、成员、排行、捐献、活动、商店。成员支持角色编号搜索、仅在线筛选和分页。
- 总览读取当前帮会及公告；成员可查看公告全文，服务端确认的帮主或长老可编辑公告。
- 未入帮状态可浏览排行榜、加入已有帮会或创建帮会。加入、退出与解散均有二次确认；确认默认选择取消，弹窗隔离后层键盘操作。
- 真实请求复用 `GameClientBattleTransport` 的 gate 连接。所有写操作等服务端回包后刷新；无本地假成功。重复提交、断线、换角和过期回包有状态隔离。

## 协议与边界

源契约为 `E:/work/xuanming-server-mmo/proto/guild/guild.proto`。按客户端原有生成流程增加 Guild 协议与错误码枚举，编号从服务端 `proto/message_id.txt` 生成。9 月 13 日接入时未改服务端；9 月 14 日服务端改动见本文开头。

| 客户端动作 | 真实 RPC |
|---|---|
| 读取我的帮会、公告和成员 | GetPlayerGuild |
| 分页浏览帮会 | GetGuildRank |
| 创建 / 加入 | CreateGuild / JoinGuild |
| 保存公告 | SetAnnouncement |
| 退出 / 解散 | LeaveGuild / DisbandGuild |

协议目前只返回成员编号、身份、在线状态、贡献、入帮和最近活跃时间，不返回成员姓名、等级或头像。原生成员列表因此使用真实编号，不将预览角色硬套到真实玩家。

公告权限严格对齐服务端 `UpdateAnnouncementAuthorized`：角色 1（长老）及 3（帮主），不放行尚未实现写权限的角色 2。服务端仍是最终权限判断方。

捐献、帮会活动和商店目前缺少对应业务契约，其页面明确「暂未开放」，按钮禁用；不扣除银两或灵石，不模拟入账、库存或领奖。客户端不公开 `UpdateGuildScore` 这种服务端评分写操作。

当前 Gate 的普通 gRPC 回复不回显请求编号，客户端原有兼容路径按消息类型先后匹配。因此帮会请求发生传输错误/超时，或在请求未完成时重置角色状态，会隔离此连接上的后续帮会请求，显示「请重新登录角色后再试」；只读浏览、成员筛选/本地分页和关闭仍可用。仅普通 Reset 或同一连接换角色不会解除隔离，防止迟到回包套到新操作。

`GameClient.GateConnectionIdentity` 只读暴露真实 Gate 对象身份，不修改全局消息路由。帮会在每帧和发请求前观察身份变更；真实断开或 Gate 被替换才重置隔离，兼容 EnterZone/RedirectFlow 的静默重连。回调同时校验角色、请求代次与当前 Gate 身份，避免下一次 UI 更新前的旧回包污染状态。

联机服务器验证尚未执行；离线样例验收不能代表服务器可用。成员加入等外部变动可用刷新按钮重新读取。以上 7 个业务 RPC 已接到原生窗口，捐献、活动、商店仍明确未开放。

## 源文件与复现

- `Assets/Scripts/Game/Guild/GuildClient.cs`：请求、权威快照、会话隔离和业务错误。
- `Assets/Scripts/UI/Ugui/Guild/GuildUiRoot.cs`：HUD、快捷键、会话和面板互斥。
- `Assets/Scripts/UI/Ugui/Guild/GuildWindow.cs`：正式窗口、表单、分页及确认弹窗。
- `Assets/Editor/Guild/GuildUiVerification.cs`：独立离线验收，不保存或改写用户场景。
- `Assets/Tests/EditMode/Guild/GuildUiTests.cs`：请求及界面测试；独立测试程序集显式引用 Protobuf。
- `tools/gen_proto.ps1` 与 `tools/gen_messageids.ps1`：可重复生成 Guild 类型、错误码与编号；生成产物不可手改。

菜单 `MMORPG > UI > Preview guild UI (offline)` 提供带明确样例标识的浏览预览；`Capture guild screens (offline)` 生成正式窗口的 2560×1080 与 1920×1080 截图。离线预览不执行生产写操作。

验证输出位于 `.codex-artifacts/guild-ui-v2/`：

- `resource-sync.json`：9 张资源来源、目标与相同 SHA-256。
- `editmode-results.xml`：帮会定向测试结果。
- `capture.json` 与 20 张 PNG：六页、公告编辑、未入帮、24 字长帮名确认、需重新连接状态，两种分辨率。

最终使用 Unity 6000.6.0f1 验证：2026-09-13 13:22:19–20 UTC，帮会定向 EditMode 测试 **27/27 通过，0 失败、0 跳过**。覆盖真实连接对象静默替换、过期回包、未确认请求隔离、角色权限、重复提交、成员分页、原生资源导入、确认框焦点与背景隔离。测试使用本地可控回包，不启动服务器或创建网络连接。

20 张原生截图已经渲染，并对总览、成员、排行、三种暂未开放页、公告、长帮名确认和重连提示进行实际画面核对。最后空态内距调整仅重拍 `08-empty` 与 `09-long-name-confirmation` 的两种尺寸，其余页面保留已经验收的截图。`capture.log` 是全量渲染记录，`capture-empty-final.log` 是最后四张重拍记录；`validation.json` 保存最终截图尺寸、SHA-256、日志扫描与测试摘要。

复跑测试：Unity 加 `-batchmode -projectPath E:/work/mmorpg-client -runTests -testPlatform EditMode -testFilter MmorpgClient.Tests.EditMode.Tianyong.Guild -testResults E:/work/mmorpg-client/.codex-artifacts/guild-ui-v2/editmode-results.xml -logFile E:/work/mmorpg-client/.codex-artifacts/guild-ui-v2/editmode.log`。完整截图加 `-executeMethod GuildUiVerification.CaptureAll -quit`，定向空态加 `-executeMethod GuildUiVerification.CaptureEmptyStates -quit`。每次必须等待占用此工程的 Unity 进程退出。
