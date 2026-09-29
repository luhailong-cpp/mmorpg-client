# 帮会 UI 接入

2026-09-14 更新：[官方 MCP 切图与原生布局接入](GuildUI-MCP-20260914.md)，27 张 Sprite、28 项测试通过、20 张本次原生截图。下文保留 9 月 13 日业务接入记录。

2026-09-14 Codex 本轮客户端验证：`tools/gen_proto.ps1 -ProtoRoot E:\work\xuanming-server-mmo` 已成功生成协议及新错误码；`tools/client_compile_check.ps1` 检查 **283 个源码文件，0 错误，退出码 0**。Unity 6000.6.0f1 在独立副本 `E:\work\tmp\guild-zone-verify-20260914` 运行帮会 EditMode 测试，**30/30 通过，0 失败、0 跳过，退出码 0**；新增公告边界及区服/重名提示两条测试均通过。本轮验证于 2026-09-14 13:11:47 UTC 完成，6 个帮会源码和协议文件的 SHA-256 与测试副本一致，正式 Unity 编辑器未关闭。

本轮未进行独立客户端打包或游戏内界面手测。服务端双区机器人冒烟已于 2026-09-14 09:46:32–38 EDT 通过并输出 `GUILD_SMOKE_OK`：1 区角色 A（602）与 B（601）、2 区角色 C（701），测试帮会编号 101；同一窗口确认 `UpdateGuildScore` 被会话白名单以 `PermissionDenied` 拒绝，清理后帮会、成员及三份榜单均为 0。细节见服务端 `docs/design/guild-zone-client-access.md` §7，运行证据位于 `run/logs/guild-verify-20260914/`。下文此前的截图、测试和初始交接记录保留为历史证据；本轮机器人联机验证不代替客户端游戏内手测。本轮证据集中在服务端仓库 `run/logs/guild-verify-20260914/`：`client-compile.log`、`client-guild-editmode-results.xml`、`client-guild-editmode.log`、`client-guild-editmode-completion.json`、`client-snapshot.json`、`client-final-check.json`。

2026-09-14 服务端接入初始交接（当时未编译、未联调）：帮会服务改为客户端可达并**按区服隔离**——建帮落在角色归属区服，只能浏览、加入本区帮会，排行为本区排行；身份与区服由服务端判定，请求里的 `PlayerId` / `ZoneId` 不再作为依据。帮名仍全服唯一。新增错误提示：帮名无效、帮名已被使用、公告过长、角色归属区服未确认。公告上限改为 200 字（服务端按 600 字节校验：Gate 单包上限 1KB，500 个汉字的包会在 Gate 被丢弃）。排行标题改为「本区帮会排行」。**必须先运行 `tools/gen_proto.ps1 -ProtoRoot E:\work\xuanming-server-mmo` 生成新错误码枚举，否则 `GuildClient.cs` 无法编译。** 服务端决策与验证清单见 `xuanming-server-mmo/docs/design/guild-zone-client-access.md`。

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
| 创建 / 申请入帮 / 撤回申请 | CreateGuild / ApplyJoinGuild / CancelGuildApplication |
| 管理(任免 / 踢出 / 转让) | SetGuildMemberRole / KickGuildMember / TransferGuildLeader |
| 审批(两种列表 / 通过拒绝) | ListGuildApplications / ListMyGuildApplications / ReviewGuildApplication |
| 服务端推送 | NotifyGuildChanged(下行,不加 MessageLimiter 行) |
| 保存公告 | SetAnnouncement |
| 退出 / 解散 | LeaveGuild / DisbandGuild |
| 捐献(读取 / 捐一次) | GetGuildDonateOptions / DonateToGuild(2026-09-28 B5c) |
| 升级帮会 | UpgradeGuild(B5c) |
| 帮会商店(读取 / 兑换) | GetGuildShop / BuyGuildShopGoods(B5c) |

协议返回成员编号、身份、在线状态、帮贡(累计 / 可用两列)、入帮和最近活跃时间;`GuildMember.name` 字段已于 2026-09-19(B2s)加进协议,但**服务端要到 B3b 才真正填值**,所以原生成员列表现在仍显示真实编号 + 「道友 · id」兜底,不把预览角色硬套到真实玩家。等级与头像不返回。

公告权限严格对齐服务端 `UpdateAnnouncementAuthorized`：角色 1（长老）及 3（帮主），不放行尚未实现写权限的角色 2。服务端仍是最终权限判断方。

捐献与商店自 2026-09-28(B5c)起接入真实协议,见下文「捐献、升级与商店」;帮会活动仍明确「暂未开放」,按钮禁用。客户端不模拟入账、库存或领奖,也不公开 `UpdateGuildScore` 这种服务端评分写操作。

当前 Gate 的普通 gRPC 回复不回显请求编号，客户端原有兼容路径按消息类型先后匹配。因此帮会请求发生传输错误/超时，或在请求未完成时重置角色状态，会隔离此连接上的后续帮会请求，显示「请重新登录角色后再试」；只读浏览、成员筛选/本地分页和关闭仍可用。仅普通 Reset 或同一连接换角色不会解除隔离，防止迟到回包套到新操作。

`GameClient.GateConnectionIdentity` 只读暴露真实 Gate 对象身份，不修改全局消息路由。帮会在每帧和发请求前观察身份变更；真实断开或 Gate 被替换才重置隔离，兼容 EnterZone/RedirectFlow 的静默重连。回调同时校验角色、请求代次与当前 Gate 身份，避免下一次 UI 更新前的旧回包污染状态。

联机服务器验证尚未执行；离线样例验收不能代表服务器可用。成员加入等外部变动可用刷新按钮重新读取。上表的业务 RPC 已接到原生窗口(2026-09-19 B2c 起含申请制与管理三件,2026-09-28 B5c 起含捐献、升级与商店)，帮会活动仍明确未开放。

## 入帮申请制（2026-09-19 B2c 落码，未编译未测试）

直接入帮取消。`JoinGuild` 从协议与客户端一并删除，改为「申请 → 帮主或长老审批」。

- 排行页按钮 `JoinGuild_{id}` 改名 `ApplyGuild_{id}`，文案三态：本帮显示「我的帮会」并禁用；已申请显示「撤回申请」；其余显示「申请」。两种可点状态都先弹确认框（`确认申请加入` / `确认撤回申请`），确认框里带帮会名，避免误点错行。
- 按钮文案由 `GuildClient.HasApplied(guildId)` 决定，数据来自 `ListMyGuildApplications`。`MyApplications == null` 表示「还没拉过」，**不等于**「没有申请」——此时按钮先显示「申请」，`Browse` 的回调已把本人申请排进队列，下一帧 `DrainQueued` 拉回来后按钮自己翻成「撤回申请」。入帮成功时服务端已删光本人全部申请，本地列表随 `Apply(GuildInfo)` 一起作废置 null；退帮 / 被踢 / 解散回到未入帮后由 `Refresh` 的 NotInGuild 分支重新排队拉取。
- 审批侧在成员页：顶栏右端 `GuildApplicationsToggle` 显示「入帮申请 {待审数}」，仅长老与帮主渲染；点开切到同一正文区的申请视图，5 行一页，与成员列表共用 `MembersPerPage`。角标数字来自 `GetPlayerGuild` 或写操作回带的快照，服务端对普通成员与外人一律回 0，不把帮会内部状态泄给任意查询者。
- 申请行的「同意 / 拒绝」**不加**确认框：拒绝后对方仍可再申请，同意也能再请离，误点可挽回；加确认框只会让批量审批变慢。行内「剩余 N 小时」按服务端 `expire_ms` 与本地时钟算差值，只用于展示，不足一小时按 1 小时显示；真正的过期判定在服务端。
- 申请有效期、每人同时进行中的申请上限、每帮待审队列上限全部由服务端裁定，客户端只负责把对应的 tip 文案显示出来。
- 身份被降为帮众后申请视图立刻失效，自动落回成员列表；换帮与换角（`SetClient` / `ResetSession`）都会清零申请视图与它的翻页。

## 成员操作（任免长老 / 转让帮主 / 请离）

职位表 `GuildRoles` 与服务端 `go/guild` 的 `constants.Rank` 同值：0 帮众、1 长老、3 帮主；**2 不启用**，与任何未知值一样落到 `RankNone`（谁也管不了、也不会被当成可请离的目标）。`RoleName` 同步删去了「副帮主」。

- 请离规则 `GuildRoles.CanKick(actor, target)`：操作者至少是长老，且**严格高于**目标一级。长老可请离帮众，请离不了长老与帮主；帮主可请离长老与帮众；谁都请离不了自己。
- 按钮可见性：任免与转让仅帮主可见且不对自己出现；「任长老」只对 role=0 的成员出现，「免长老」只对 role=1 的成员出现，两者共用同一个槽位；「请离」按 `CanKick(member)` 出现。「任长老」在长老满员时保留但不可点。
- 四个操作全部先弹确认框：`确认任命长老` 会把当前长老数与上限写进去，`确认转让帮主` 明确「转让后你将成为长老（长老已满则为帮众），此操作无法撤回」。
- 按钮不出现或点不动时，成员页下方 `GuildMemberActionHint` 给出唯一一条原因：长老已满 / 长老只能请离帮众 / 帮众只能看名册。三种情形互斥，不会同时出现两条。
- 客户端这些判断只为提前收起点不动的按钮，**服务端仍按 MySQL 里的职位复核**并可能拒绝。
- 写操作成功后直接应用响应里回带的权威快照，不再补发一次 `GetPlayerGuild`：服务端在提交后读快照回带，多发一次只会多一次等待与闪烁。公告保存同样改成了应用快照。

## 服务端推送 NotifyGuildChanged

推送只说「变了」，载荷里没有快照，所以客户端收到后**不改 `Info`**，只排队重拉——拉取结果才是真相。

- 三个排队标志：`RefreshQueued`（重拉 `GetPlayerGuild`）、`ApplicantsQueued`（重拉待审列表）、`MyApplicationsQueued`（重拉本人申请）。
- `GuildUiRoot.Update()` 在窗口可见时每帧调用 `GuildClient.DrainQueued(window.ShowingApplications)`；窗口关着不拉，打开时 `Toggle()` 里已有一次 `Refresh()`。
- `DrainQueued` 一次只发**一个**请求，且在 `Busy` / 连接隔离 / 传输未就绪时什么也不做——帮会只有一个在途请求位，连发会互相挤掉。发出后立刻清掉对应标志，避免每帧重复发送。
- 收到「有人申请入帮」时分两种走法：申请视图开着就重拉列表；关着则改拉 `GetPlayerGuild`，服务端重算待审数，成员页那颗角标随之更新。
- 「你被请离」「帮会被解散」这类提示先存进 `_pendingNotice`，等 `Refresh` 回包落到 `Status`，且只用一次——否则会被请求中的「正在读取帮会…」与未入帮默认文案盖掉。若回包显示帮会仍在，说明提示已过时，直接丢弃。
- 一个 message id 只有一个处理器（`GameClient.OnNotify` 覆盖写），帮会推送只在 `GuildClient` 构造函数里注册；`Dispose` 之后到来的推送由 `_disposed` 判断挡掉。坏包直接丢弃不抛异常，推送是至多一次的，抛出去会打断 gate 的收包循环。
- 服务端把 `NotifyGuildChanged` 排除在会话白名单之外：客户端发这个编号会被 `PermissionDenied` 拒绝，它只能收。

本轮（B2c）按硬性要求**未执行任何构建、生成或测试命令**，交付的是未编译源码。待 `tools/gen_messageids.ps1` 与 `tools/gen_proto.ps1` 生成 9 个新编号、新消息与 9 个新错误码后才能编译。期望的验收口径：`GuildClientTests` 与 `GuildWindowTests` 合计 **65 条**用例全部通过；离线截图新增第 11 屏 `11-applications`（申请审批视图），`CaptureAll` 输出随之变为 11 屏 × 2 分辨率。

## 捐献、升级与商店（2026-09-28 B5c 落码，未编译未测试）

服务端设计见 `xuanming-server-mmo/docs/design/guild-phase2/05-economy.md` §5.34–§5.37(效力顺序与落码偏差见同目录 `92-handoff.md` §13)。

- **「结算中 / 待发放」不是错误。** 捐献扣的是 scene 上的银两 / 灵石,兑换发的是背包物品,都经资产通道异步结算。服务端把结果放在视图的 `status` 里(结算中 / 已入账 / 未成功 / 已撤销 / 部分发放),`error_message` 只放真正的拒绝。客户端遇到任何非 0 tip 都会中断后续刷新,所以结算中的单绝不能按错误处理。
- **捐献页**沿用三面板:银两(小捐 / 大捐)、灵石(一档)、建设物资(暂未开放,按钮名沿用 `GuildUnavailable_Donate_2`)。选项、今日次数、解锁等级全部来自 `GetGuildDonateOptions`,客户端不加载配表。次数用尽或未解锁的档位按钮置灰;点击先弹确认框写清花费与收益。页脚按优先级显示:结算中笔数与第一笔的暂时原因 → 最近一笔结果 → 可用 / 累计帮贡与帮会资金。结算中那一档**不许诺「完成后自动入账」**(设计 §5.35.1 原句):结算中的单也可能以未成功 / 撤销收尾,余额不足而结果未落盘(PENDING + 27000)时多半如此,页脚与状态栏同写「N 笔捐献结算中：余额不足，正在确认结算结果」(`GuildClient.DonationPendingReasonText`)。
- **商店页**分三类(修行补给 / 帮会珍藏 / 节庆好礼),每页 6 张卡片;未解锁显示「帮会 Lv.N 解锁」,限购显示今日 / 本周用量。可兑换条件 = 已解锁 且 未兑满 且 可用帮贡够;本期每次兑换 1 份。页脚只有 800 宽(右侧 820 起是翻页键),30 号字放不下「最近一单：兑换失败，帮贡与限购已退回：」+ 原因与部分发放的整句(设计按 26 号字写,`GuildUiArt.Text` 把字号抬到 30),所以这两种页脚只写结论(`ShopFooterResultText`),**全文写在状态栏**:没有待发放时,`RefreshShop` 的默认文案是「最近一单：」+ 完整结果(没有最近一单才是「帮会商店已更新」),断线重连后进页自动拉取也看得到上一单(05 §5.32 W14)。`EconomyFootersFitWithoutEllipsis` 按字体度量逐个原因码核对两页页脚不超框宽。
- **隔离(请求超时,需重新登录)时**两页的「读取中」占位改写 `RecoveryMessage`:此时没有请求在途,与排行页、总览空态同一判断顺序(先隔离、再 Busy)。
- **总览**改为四列:成员 / 在线 / 帮会资金 / 可用与累计帮贡(`GuildMyContribution` 显示「可用 / 累计」)。帮主与长老多一个「升级帮会」按钮(满级显示「已满级」并置灰)。**资金够不够交给服务端判**:别的长老刚花过钱时本地资金可能是旧值,所以按钮只按职位与是否满级收起;资金不足时服务端回拒绝并带最新快照,资金显示随之刷新。升级请求带**确认框打开时看到的等级**(`UpgradeRequested` 带这个等级、`GuildClient.Upgrade(expectedLevel)` 用它作 `expected_level`),而不是点确认那一刻的等级:确认框开着时别的长老先升了级,推送会把本地快照刷成新等级,确认框却不会关;这时客户端不发请求,提示「帮会等级已变化，请重新确认升级。」,不会按下一级的花费再扣一次、连升两级。
- **进页自动拉取**:进入捐献页 / 商店页时,没有快照**或快照已过时**就自动拉一次(每次进入最多一次,失败后由玩家点刷新;停在本页时快照变过时也会拉一次:等级 / 帮贡变化伴随回包或推送,经 `Changed → Render` 触发;跨过日 / 周切点没有任何事件,由 `GuildUiRoot.Update` 在 `DrainQueued` 之后每帧调 `GuildWindow.Tick()` 补判,它只做时间比较、不重建界面,一份过时的快照最多自动拉一次)。"过时"指:同一个帮会里等级变了(本人升级,或别人升级的推送 → 重拉帮会快照;商店与捐献档的"解锁"是服务端按拉取那一刻的等级算的)、本人可用帮贡变了(捐献入账后的商店余额、兑换后捐献页页脚的余额),或本地时钟已过回包里的 `next_daily_reset_ms`(商店另看 `next_weekly_reset_ms`)。过时只作标记、不清快照,也不走排队重拉 —— 排队那一发不带文案,会把升级 / 捐献的结果盖成"…已更新"。结算推送已排队重拉本页时,进页不再自己拉,让 `DrainQueued` 按帮会快照在前、本页在后的顺序来。右下角「刷新」在这两页只刷本页数据。
- **「还有未结算的帮会操作」**(`kGuildAssetPending`,资产通道关闭或未决指令过多时回):捐献 / 兑换的回调里带着这句拒绝文案直接重拉本页,让待结算列表收敛,文案留在状态栏;不走 `DrainQueued`,否则待结算为空时状态栏会落成"捐献信息已更新 / 帮会商店已更新",像是成功了。
- **文字框高**:QdaoBody(Noto Sans SC)30 号一行要 (74.24+18.432)×30/64 ≈ 43.4 高;`CreateText` 默认 Ellipsis,框比一行矮时第一个字就判溢出,整行一个字都不出。捐献页选项两行、页脚与商店卡片的单行正文框高一律 ≥ 46。`EconomyPageBodyTextBoxesFitOneLine` 按字体度量核对这两页的单行正文;`CaptureAll` 截图时逐个核对可见文字是否真有字形,没有就把 `capture.json` 记为 `failed` 并在日志里列出。
- **结算完成的推送**:服务端只给结算的那个人推 `FUNDS_CHANGED`(捐献)/ `DELIVERY_DONE`(兑换)。客户端收到后触发 `AssetsChanged` 让背包重拉(scene 没有余额推送),并在对应页拉过时排队重拉本页;同时按 B2 规则排一次 `GetPlayerGuild`。`DrainQueued` 的顺序是帮会快照在前、本页在后,让「已入账 / 已发放」的文案最后落到状态栏。已离帮后才结算的那一笔同样会让背包重拉。
- **兑换成功后的帮贡**:兑换回包不带帮会快照,但带提交后的权威余额,客户端据此就地更新总览里那一格。
- **换帮 / 离帮 / 换角**时捐献与商店快照一律作废,不会把上一个帮会的次数与帮贡带进来。
- **货币改名**(契约 §0-1):背包面板的「金币 / 钻石 / 绑定钻石」改为「银两 / 灵石 / 绑定灵石」;加点与宠物的「金币不足」改为「银两不足」。服务端 `Tip.xlsx` 那两行的文案仍写「金币不足」,随下一个改 Tip.xlsx 的批次同步。加点面板「开启新方案(N 银两)」「重置(N 银两)」、宠物面板「改名 · N 银两」「洗点 · N 银两」四个按钮的花费单位,以及战斗结算面板的「银两  +N」(原「金钱  +N」;`GoldGain` 由 scene 按 `kCurrencyGold` 入账)也在 B5c 收尾时一并改了(按钮宽度已核:最长「开启新方案(1000 银两)」约 364px,按钮 516px);现有用例 `DamageNumberLayoutTests.cs:108` 只断言含「+30」,不受影响。
- **前置生成**(未执行):`tools/gen_messageids.ps1 -ProtoRoot E:\work\xuanming-server-mmo` 生成 5 个新编号(GetGuildDonateOptions / DonateToGuild / UpgradeGuild / GetGuildShop / BuyGuildShopGoods)后才能编译;经济协议与 10 个错误码的 C# 已在 `Assets/Scripts/Proto/Generated/` 里。注意 53 / 76 / 120 三个编号是从好友服务易主来的,旧 `MessageIds.cs` 若残留好友常量会指错。
- **期望的验收口径**:`GuildClientTests` 72 条 + `GuildWindowTests` 35 条 = **107 条**全部通过(本批新增 38 条、删去 `UnsupportedActionsAreClearlyDisabled` 的捐献 / 商店两例;B6a-cli 删最后一例时要删整个方法)。离线截图的 `04-donate` 与 `06-shop` 改为带样例数据的真实页面。

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

20 张原生截图已经渲染(**2026-09-13 的历史证据**;B2c 新增的申请页与成员操作按钮未重拍,`11-applications` 需下次 CaptureAll 时补)，并对总览、成员、排行、三种暂未开放页、公告、长帮名确认和重连提示进行实际画面核对。最后空态内距调整仅重拍 `08-empty` 与 `09-long-name-confirmation` 的两种尺寸，其余页面保留已经验收的截图。`capture.log` 是全量渲染记录，`capture-empty-final.log` 是最后四张重拍记录；`validation.json` 保存最终截图尺寸、SHA-256、日志扫描与测试摘要。

复跑测试：Unity 加 `-batchmode -projectPath E:/work/mmorpg-client -runTests -testPlatform EditMode -testFilter "GuildClientTests;GuildWindowTests" -testResults E:/work/mmorpg-client/.codex-artifacts/guild-ui-v2/editmode-results.xml -logFile E:/work/mmorpg-client/.codex-artifacts/guild-ui-v2/editmode.log`。完整截图加 `-executeMethod GuildUiVerification.CaptureAll -quit`，定向空态加 `-executeMethod GuildUiVerification.CaptureEmptyStates -quit`。每次必须等待占用此工程的 Unity 进程退出。
