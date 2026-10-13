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
| 帮会活动(读取 / 点灯 / 领团圆礼) | GetGuildActivities / LightGuildLantern / ClaimGuildReunion(2026-10-01 B6a-cli) |
| 同道历练(建邀请房间 / 应答) | StartGuildTrial / RespondGuildTrialInvite(消息号 B6a 已占;2026-10-08 B6b-cli 起客户端调用) |

协议返回成员编号、身份、在线状态、帮贡(累计 / 可用两列)、入帮和最近活跃时间;`GuildMember.name` 字段已于 2026-09-19(B2s)加进协议,但**服务端要到 B3b 才真正填值**,所以原生成员列表现在仍显示真实编号 + 「道友 · id」兜底,不把预览角色硬套到真实玩家。等级与头像不返回。

公告权限严格对齐服务端 `UpdateAnnouncementAuthorized`：角色 1（长老）及 3（帮主），不放行尚未实现写权限的角色 2。服务端仍是最终权限判断方。

捐献与商店自 2026-09-28(B5c)起接入真实协议,见下文「捐献、升级与商店」;帮会活动自 2026-10-01(B6a-cli)起接入灯会与团圆,见下文「帮会活动」;同道历练的选人、邀请与应答自 2026-10-08(B6b-cli)起接入,见下文「同道历练」。客户端不模拟入账、库存或领奖,也不公开 `UpdateGuildScore` 这种服务端评分写操作。

当前 Gate 的普通 gRPC 回复不回显请求编号，客户端原有兼容路径按消息类型先后匹配。因此帮会请求发生传输错误/超时，或在请求未完成时重置角色状态，会隔离此连接上的后续帮会请求，显示「请重新登录角色后再试」；只读浏览、成员筛选/本地分页和关闭仍可用。仅普通 Reset 或同一连接换角色不会解除隔离，防止迟到回包套到新操作。

`GameClient.GateConnectionIdentity` 只读暴露真实 Gate 对象身份，不修改全局消息路由。帮会在每帧和发请求前观察身份变更；真实断开或 Gate 被替换才重置隔离，兼容 EnterZone/RedirectFlow 的静默重连。回调同时校验角色、请求代次与当前 Gate 身份，避免下一次 UI 更新前的旧回包污染状态。

联机服务器验证尚未执行；离线样例验收不能代表服务器可用。成员加入等外部变动可用刷新按钮重新读取。上表的业务 RPC 已接到原生窗口(2026-09-19 B2c 起含申请制与管理三件,2026-09-28 B5c 起含捐献、升级与商店,2026-10-01 B6a-cli 起含灯会与团圆,2026-10-08 B6b-cli 起含同道历练)。

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
- `GuildUiRoot.Update()` 在窗口可见时每帧调用 `GuildClient.DrainQueued(window.ShowingApplications, window.ShowingActivities)`（第二个参数 B6a-cli 加，见下文「帮会活动」）；窗口关着不拉，打开时 `Toggle()` 里已有一次 `Refresh()`。
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

## 帮会活动（2026-10-01 B6a-cli 落码，2026-10-08 审查补齐，未编译未测试）

服务端设计见 `xuanming-server-mmo/docs/design/guild-phase2/06-activities.md` §6.36–§6.39、§6.43(先读文件顶部的「决策覆盖」;跨节修正见同目录 `90-consistency.md` X-06 / X-07 / X-11 / Y-11)。本批只接**元宵灯会**与**中秋团圆**;同道历练的选人、邀请框、建房与应答留给 B6b-cli。

2026-10-01 的落码在收尾前被中断;2026-10-08 逐文件对照设计审查(括号配平、符号引用、测试与实现的签名逐条走查,**仍未编译**)后补了四处:`ActivityWrite` 加 `guildChanged` 参数(给 B6b 的历练建房 / 应答留好不排 `GetPlayerGuild` 的走法)、写操作结果文案沿用一次(`_activityResult`)、档期切点也算快照过时、历练卡片在服务端已开放时写「即将开放」。下面各条已按补齐后的行为写。

- **三张卡片全部由服务端视图驱动。** `GetGuildActivities` 每种类型至多回一条视图(配表行 + 帮会进度 + 本人状态),客户端不加载活动配表。第 i 张卡片固定对应类型 i+1(灯会 / 团圆 / 历练),按类型找视图而不是按下标;服务端少下发一种时,那张卡写「暂无活动」,后面的卡不错位。按钮名 `GuildActivityAction_{1|2|3}` 用的是类型号,不是 `activity_id`(换档期后 id 会变)。
- **能不能点只看 `blocked_tip_id`。** 0 = 本人现在可参与;否则是服务端按「未开放 > 帮会等级 > 入帮时长 > 今日次数 > 团圆人数」给出的第一个不满足项,按钮写成对应的原因(今日已点灯 / 今日已领取 / 未开放 / 尚未开启 / 已结束 / 帮会等级不足 / 入帮时间不足 / 人数未齐)并置灰。点灯与领团圆礼没有花费,**不加确认框**。服务端在事务里仍会完整复核。
- **历练卡片在本批只读,按钮一律不可点。**(这一条是 B6a-cli 当时的状态;B6b-cli 已接管这张卡片,「即将开放」不再出现,见下文「同道历练」。)名称、状态、进度、奖励与「今日 x / y」照常显示。服务端 B6a 把历练视图固定为「未开放」(`state = DISABLED`、`blocked_tip_id = kGuildActivityNotOpen`),按钮写「未开放」;连上已开放历练的服务端(`blocked_tip_id = 0`)时按钮写「即将开放」—— 此时状态行是「常开 / 至 … 结束」,再写「未开放」就自相矛盾,也不给一个点了没反应的按钮;其它不可参与的原因(今日次数已满、帮会等级不足……)照常显示。B6b-cli 从 `GuildWindow.RenderActivityCard` 的 `default` 分支接管。
- **读取只是排队。** 帮会只有一个在途请求位,打开窗口时那一发 `GetPlayerGuild` 多半还在路上;活动页的读取事件 `ActivitiesRequested` 接的是 `GuildClient.QueueActivities()`(只置 `ActivitiesQueued`),由 `DrainQueued` 在活动页可见且空闲的那一帧发出,所以 Busy 时进页也不会卡在「正在读取」。`DrainQueued` 的顺序:帮会快照 → 待审申请 → 本人申请 → **活动视图** → 捐献页 → 商店页。活动页不可见时标志留着,进页再发。
- **进页自动拉取**与捐献 / 商店页同一套规则(`MaybeAutoRequest` / `Tick`):没有快照或快照过时就自动排一次,每次进入最多一次,失败后由玩家点右下角「刷新」(在活动页只刷活动视图)。「过时」指同一个帮会里等级变了(`min_guild_level` 的判定随之变)、**服务端时钟**已过视图里的 `next_reset_ms`,或已过某个活动的档期切点(未开始的到了 `start_at_ms`、进行中的到了 `end_at_ms`:状态、按钮与「本期」进度都要换,这两个时刻没有任何推送;状态以服务端下发的为准,客户端只拿它与起止时刻比,不自己重算)。推送或刷新键已经排过队时,进页不再重复触发。
- **时间以服务端为准。** 视图带 `server_time_ms`;客户端记下收到它那一刻的单调时钟读数,之后按「server_time_ms + 流逝时间」估算服务端当前时刻(`GuildClient.ActivityServerNowMs`),不读本机墙钟 —— 玩家改系统时间不会让倒计时乱跳,也不会让自动重拉提前或落空。标题行右侧的 `GuildActivityReset` 显示「每日 05:00 重置 · 距下次重置 N 小时 M 分」,停在本页时由 `Tick` 每帧只改这一行文字。视图没带时刻(单测替身)就只写「每日 05:00 重置参与次数。」,不编倒计时。
- **写成功后的两步。** 点灯 / 领奖成功只回本活动的视图:客户端就地换掉这一条,提示存进 `_writeNotice` 并排队 `GetPlayerGuild`(帮贡、资金在帮会快照里)。**不在回调里连发第二个请求**;下一帧 `DrainQueued` 发出,回包后提示落回状态栏(「花灯已点亮，帮贡已到账。」/「团圆礼已领取，物品稍后到账。」),只用一次。`_writeNotice` 与 B2 的 `_pendingNotice`(被请离 / 解散)是两个字段:补拉回来发现已不在帮会时,优先说「被请离 / 已解散」,写操作的提示丢弃(X-06)。奖励带物品时触发 `AssetsChanged` 让背包重拉(成功却没带回视图时不知道有没有物品,按有处理)。这两步由 `ActivityWrite` 的 `guildChanged = true` 打开;B6b 的历练建房与应答只动邀请房间,传 `false`,不排 `GetPlayerGuild`、不重拉背包。
- **结果文案再沿用一次。** 写操作的结果(成功提示或拒绝原因)另记在 `_activityResult`。紧随其后的活动视图重拉 —— 别人让本期达了阈值的推送、跨日 / 档期 / 等级变化让窗口自动排的那一发 —— 发出时若状态栏还停在这条文案上,就沿用它一次,不被默认的「帮会活动已更新」盖掉;状态栏已经换成别的文案就不沿用。只一次:之后再点刷新看到的是「帮会活动已更新」。
- **被拒后的重拉带着拒绝文案。** 被拒(今日已参与、档期已过、人数不足……)多半说明本页已过时,回调里带着拒绝文案直接重拉一次活动视图 —— 走排队的话那一发不带文案,会把原因盖成「帮会活动已更新」(与捐献 / 兑换的 `kGuildAssetPending` 同一做法)。回「已不在帮会」的不重拉,只排 `GetPlayerGuild`。成功却没带回视图(服务端提交后重建视图失败,写已生效)时同样带着提示重拉本页。
- **物品待发放不是错误。** 背包满时奖励保持待发放、腾出空间后自动到账;「我的状态」写成「今日 1 / 1 · 背包已满，腾出空间后自动发放(1)」。发放完成的推送是 `DELIVERY_DONE`(与商店兑换同一种):活动页上挂着待发放时排队重拉本页。永久拒绝(`my_last_reward_reject_tip_id`)只在没有待发放时提示「上次物品发放失败」。
- **活动推送 `ACTIVITY_CHANGED`**(本档期首次达阈值时发给其他成员)只排活动视图的重拉,不排 `GetPlayerGuild`。定向给本人的那种是同道历练邀请(B6b):`TrialInvitePending` 已随本批加好并在离帮 / 换角时清掉,自动打开活动页的那段在 B6b-cli 接(已接,见下文「同道历练」)。
- **tip 文案只有一份映射。** `Accept` 里的 switch 抽成 `GuildClient.TipText(TipInfoMessage)`,B6b 的窗口要把邀请房间的结束原因写成人话时也用它。活动的 10 个码已加进去;带参数的(人数 `[在线, 阈值]`、等级 `[min_level]`、入帮时长 `[N]`、冷却 `[秒]`)按位次取参数,**参数缺失或不是数字时回落到不带数字的说法** —— 不带参数的码,服务端会塞一段英文说明当唯一参数,不能拼进文案。历练的 `TeamInvalid` / `InviteDeclined` 先给通用说法,B6b-cli 再换成带成员名的(已换:状态栏走带人名的 `DescribeTip`,静态的 `TipText` 仍是不带人名的说法)。
- **与设计稿的排版偏差**(设计按 24–27 号字排格子,`GuildUiArt.Text` 会把字号抬到至少 30,424 宽一行只有约 14 个汉字):
  1. 重置提示从标题下方挪到标题行右侧(右对齐),卡片上移到 y=64,腾出一行给奖励(三行,框高 138);「我的状态」两行(框高 94);单行正文框高一律 46;页脚 564..610。
  2. 开放状态行(324 宽)放不下「进行中 · 至 {end} · 需帮会 N 级」:帮会等级不够时写「进行中 / 常开 · 需帮会 N 级」,否则写「至 MM-dd HH:mm 结束」(UTC+8);未开始写「MM-dd HH:mm 开启」。时刻显示不了时写「--」而不抛异常:`DateTimeOffset` 只到 9999 年末,而显示前要加 8 小时,所以上限比 UTC 上限早 8 小时(`MaxDisplayableUnixMs` = 253402271999999)—— 配表填坏、或拿「9999-12-31 23:59:59 UTC」表示长期开放,都落在这一支。
  3. 「帮会资金已入库」不拼在进度行后面,改写进奖励:「达成后帮会资金 +500」→「帮会资金 +500 已入库」。
  4. 物品最多列两种(`#编号×数量`,视图不带名字),其余写「等 N 种」;待发放与「上次发放失败」同时存在时只写待发放。
  `ActivityPageTextBoxesFitAtBodySize` 取各格最长的现实文案,按字体度量核对单行的框高与字形总宽、多行的排版总高。
- **离线验收**:`FixtureTransport` 响应 `GetGuildActivities`,`05-activities` 截到的是三张带样例数据的卡片(灯会可点、团圆已领且物品待发放、历练未开放),不再是「暂未开放」占位;截图总数不变(11 屏 × 2 分辨率)。预览与截图流程没有每帧的 `DrainQueued`,替身又是同步回包,所以那里的 `ActivitiesRequested` 直接接 `RefreshActivities()`。
- **前置生成**(未执行,预期如此):服务端导表与 proto-gen 跑完之后,依次执行 `tools/gen_proto.ps1 -ProtoRoot E:\work\xuanming-server-mmo`(生成活动消息类型、三个枚举与 10 个活动错误码)与 `tools/gen_messageids.ps1 -ProtoRoot E:\work\xuanming-server-mmo`(白名单已加 5 个活动号)。在那之前 `Guild.cs` 里没有 `GuildActivityView` 等类型、`GuildErrorTip.cs` 里没有 `KGuildActivity*` / `KGuildTrial*`、`MessageIds.cs` 里没有 `GetGuildActivities` 等常量,**本批源码编不过是预期,不是缺陷**。
- **期望的验收口径**:`GuildClientTests` 102 条 + `GuildWindowTests` 47 条 = **149 条**全部通过(按 NUnit 用例计,一个 `[TestCase]` 算一条;这是按源码数出来的条数,**本批从未编译、从未运行**)。相对 B5c 的 107 条净增 42 条:`GuildClientTests` 新增 30 条(15 个 `[Test]` + `ActivityTipsMapToReadableStatus` 11 例 + `ActivityTipWithoutNumericParametersFallsBackToGenericText` 4 例);`GuildWindowTests` 新增 13 条、**删去整个 `UnsupportedActionsAreClearlyDisabled` 方法**(活动是它最后一例,NUnit 不许带参方法没有用例,90 清单 X-11)。设计 §6.43 的 1–6、9–14 号全部在内;7、8、15–17 号(历练)属 B6b-cli,其中 17 号 `TrialButtonStates` 落地时取代本批的 `TrialCardIsReadOnlyUntilTheTrialClientShips`。**B6b-cli 落码后的口径是 193 条,见下一节。**

## 同道历练（2026-10-08 B6b-cli 落码，未编译未测试）

服务端设计见 `xuanming-server-mmo/docs/design/guild-phase2/06-activities.md` §6.22–§6.27(邀请房间)、§6.37–§6.39、§6.43 的 7、8、15–17 号;文件顶部「决策覆盖」U2 = **阵亡也得奖**,只有逃跑者不得奖(客户端没有任何"阵亡不得奖"的文案)。本批改了 `GuildClient.cs`、`GuildWindow.cs`、`GuildUiRoot.cs`、`GuildUiTests.cs`,并同步了 `GuildUiVerification.cs` 的样例;**全程没有编译、没有运行任何测试**。

先说结论:历练是**邀请确认制**。发起人挑人 → 服务端建一个 30 秒有效的待确认房间并通知被选中的人 → 每个人自己点同意 → 全员同意的那一下才开战。任何成员都不能把别人直接拉进战斗。

- **卡片按钮五态**(按顺序取第一条,`GuildWindow.TrialButton`):① 本人有进行中的历练对局 →「历练进行中」(不可点);② 房间等待确认、本人是发起人 →「取消邀请」(直接发,不加确认框);③ 房间等待确认、本人被邀请还没应答 →「响应邀请」(打开邀请框);④ 本人已同意 →「等待同道」,全员已同意正在开战 →「正在开战」(都不可点);⑤ 其余 → 可参与时「组队历练」(打开选人框),否则写不可参与的原因。①–④ **先于** `blocked_tip_id`:被邀请的成员即使今日次数已满也能应答(随队,胜利不再得奖),发起人任何时候都能取消。
- **选人框**(`ShowTrialPicker`):候选是帮会快照里在线且不是自己的成员,按编号升序,每页 6 人、两列三行;自己必在队中,不出现在列表里,发出时排在名单首位,其余按点选顺序。可选人数 = 视图的 `team_size_min / max` 各减去自己(默认 2–5 人队 → 选 1–4 位);选满后再点别人不生效。已选的用主按钮底色标出(不用「✓」字符,字体里未必有这个字形)。成员按钮 478 宽(文字框 442),字号只在超宽时从 32 缩到正文下限 30;有名字的照旧加粗(12 个字约 411 宽),**取名失败的兜底名「道友 · 编号」用正文字重、不加粗** —— 按钮字是伪粗体,每个字另加 0.07em 字距,存量 snowflake 号(18–19 位)加粗后缩到 30 号仍超框,编号尾巴会被省略号截掉;不加粗时 19 位约 420、`ulong` 上限的 20 位约 436.5,都放得下。点「发出邀请」前按最新的成员快照再筛一遍:选中的人已下线或离帮就先重画给玩家看,不替他发一份没看过的名单。请求在途时按钮置灰,硬点也不关框、不丢已选的人。
- **邀请框**(`ShowTrialInvite`):写明谁邀请、队伍几人、还剩多少秒;今日次数已满的人在同意之前就看到「你今日次数已满，胜利不再得奖。」。进活动页时房间正等着本人应答就自动弹出,**同一个房间只自动弹一次**(按 Esc 关掉后不再反复弹,卡片上的「响应邀请」还能打开);别的弹窗开着时不弹,等它关掉由 `Tick` 补弹。自动弹出时不给默认焦点,手上正按着的回车 / 空格不会替玩家同意或婉拒。
- **应答带的是当时看到的房间。** 卡片按钮与邀请框的两个按钮,带的都是画出来那一刻的房间 id。弹窗开着时视图可能被推送刷新、甚至换成另一个房间,玩家同意的只能是他在框里看到的那一个(过期了由服务端回「邀请已失效」)。
- **邀请框跟着房间走。** 弹窗不随页面重画而重建,所以每次重画末尾对一遍(`SyncTrialModals`):视图里已经不是「这个房间等我应答」(过期、发起人取消、别人婉拒、已开战、换了房间)或帮会请求已被隔离 → 关掉,卡片与状态栏说明原因;还在等 → 两个按钮跟着「请求在途」置灰 / 恢复。帮会只有一个在途请求位,推送触发的重拉恰好在路上时点下去会被静默丢掉,所以那一瞬间按钮是灰的。
- **房间到期没有推送。** 服务端在读取时按截止时刻折算房间状态,到点不通知任何人。客户端把它当成又一种「切点」:等待确认的房间过了 `expire_at_ms`、「正在开战」的过了开战窗口(视图生成时刻 + 5 秒),活动快照就算过时,停在活动页时自动排一次重拉(`GuildClient.ActivitiesNeedReload` → `GuildWindow.Tick`)。时间仍按服务端时钟估算,不读本机墙钟;房间是否真的结束以重拉回来的状态为准,客户端不自己判过期。
- **两处文案分工。** 卡片的「我的状态」只有两行:第一行今日次数,第二行是在途状态的**短结论**(等待同道确认 2/3、收到邀请待你响应、正在开战…、历练已开启、邀请被婉拒 / 已取消 / 已过期、队伍有变未能开战、服务繁忙未能开战、发起人次数已满、活动已关闭),424 宽的一行写不下带人名的原因。**完整的一句写在底部状态栏**:活动视图每次重拉,只要有在途的历练,默认文案就是它的状态(`GuildClient.TrialStatusText` / `LobbyEndText`),例如「阿青 婉拒了同道历练邀请。」「历练未能开战：阿青 当前不在线。」。有在途状态时第二行不再写物品待发放的说明,房间记录过期后自己回来。
- **名单被拒会指名道姓。** `kGuildTrialTeamInvalid` 的参数是 `[reason, player_id]`:客户端把编号换成名字(是自己就说「你」),十种 reason 各有一句;参数缺失、不是数字、带符号、溢出、被塞了英文说明、或 reason 不认识,一律不抛异常,落到「有同道…」或「队伍信息无效，请刷新后重试。」。原因是「有人已离线 / 已不在帮会」时,说明本地成员快照过时(在线状态没有推送,活动页的刷新键也只刷活动视图),客户端排队补拉一次 `GetPlayerGuild`,拒绝原因活过那一发。
- **历练的成功提示不沿用。** 点灯 / 领奖的成功提示会被紧随其后的那一发重拉沿用一次;历练不这样做 —— 建房、同意之后的重拉都是别人的操作带来的,沿用就会让「邀请已发出」盖住「某某婉拒了邀请」。同意之后的提示看回包里的房间:还在等别人、全员到齐已开战,或房间其实已经解散(写解散原因)。**婉拒 / 取消同样看回包里的房间**(`GuildClient.DeclinedTrialText`):服务端对「本人早已同意、房间却已不在等待确认」的应答不看 `accept`,一律按成功回视图,而发起人建房即同意 —— 最后一票刚到、正在开战时(开战完成后才给发起人推送,这段时间他的按钮还是「取消邀请」),或别人先一步婉拒 / 邀请已过期时,他点「取消邀请」拿到的是成功回包,房间却不是被这一下取消的。所以只有回包里的房间确实记着「被本人婉拒 / 取消」时才写「已婉拒 / 已取消同道历练邀请。」;否则写房间的实际状态(「全员已同意，正在开战…」「同道历练进行中。」「阿青 婉拒了同道历练邀请。」等,与随后重拉的默认文案同一句);回包没带回这个房间时无从判断,用固定提示。建房与应答只动邀请房间,不补拉帮会快照、不重拉背包。
- **开战失败与其他成员看到同一句。** 最后一个人同意的那次调用负责开战;失败时服务端回 tip 并同时带回已解散的房间,客户端先应用视图,状态栏写「历练未能开战：…」,不再多拉一次本页。
- **收到邀请时自动打开活动页**(`GuildUiRoot.OpenPendingTrialInvite`)。邀请只有几十秒,所以主动打开,但不打断玩家手上的事:战斗中不弹;帮会窗口开着且有弹窗、或正在成员查找框里打字时等它结束;帮会窗口关着而别的全屏界面开着、或正在别处打字时也等。推迟超过 90 秒(服务端默认的 30 秒有效期 + 60 秒房间记录保留)标志自己失效,不会在几分钟后凭空弹出一个空页面。**帮会窗口这次登录还没开过时本地没有帮会快照**,这是被邀请人的常态:邀请照样记下,先补拉帮会快照,活动视图随后跟上。
- **与战斗的衔接。** 开战后参战者收到 `NotifyBattleStart`,战斗界面接管,帮会窗口连同邀请框 / 选人框随之关闭(沿用既有的「战斗层显示即关窗」)。帮会**不注册**战斗的任何推送(一个消息号只有一个处理器,注册就会顶掉战斗模块的)。战斗层收起的那一帧,`GuildUiRoot` 调 `GuildClient.NoteBattleEnded()`:快照里有在途的历练就标记过时,下次进活动页自动重拉 —— 结算推送至多一次、可能丢,结算也可能晚于战斗结束。没有在途历练的普通战斗不白拉。
- **名字兜底规则挪到了 Game 层。**「有名字显示名字,否则『道友 · 编号』」原来只在 `GuildWindow.MemberDisplayName`;状态栏的历练文案也要用它,所以规则本体放进 `GuildClient.DisplayName`,窗口的静态方法转调。规则仍只有一份,依赖方向是界面层 → Game 层(90 清单 Y-08 写的是反过来调,那样 Game 层就依赖了界面层)。
- **与设计稿的差异**(设计按 24–27 号字排,实际最小 30 号、按钮 32 号):卡片第二行只写短结论(见上);房间解散原因里有三个码换了说法 —— 通用文案是对「我这次操作」说的,而房间里次数用完的是发起人、没及时响应的是别的同道;选人框已选用底色而不是「✓ 」前缀;翻页键写「上页 / 下页」(130 宽的按钮放不下三个 32 号字);「正在开战」单列一个标签;邀请推送加了 90 秒时限与「没有帮会快照时也认」两条;请求里没有「难度」,副本由配表定。
- **离线验收**:`FixtureTransport` 的历练样例改为已开放,默认是「本人发起、已有一人同意」的邀请房间(`05-activities` 上按钮为「取消邀请」);新增两屏 `12-trial-picker`(选中两位)与 `13-trial-invite`(带倒计时),`CaptureAll` 变为 13 屏 × 2 分辨率 = 26 张,`capture.json` 的 `screenshots` 随之改为 26。
- **前置生成**(未执行):同上一节,`gen_proto.ps1` 与 `gen_messageids.ps1` 都要带 `-ProtoRoot`。本批新用到的生成物符号:`GuildTrialLobbyView`、`GuildTrialLobbyState.{Pending, Launching, Launched, Ended}`、`StartGuildTrialRequest/Response`、`RespondGuildTrialInviteRequest/Response`、`GuildActivityView.TrialLobby / MyTrialBattleId / TeamSizeMin / TeamSizeMax`、`MessageIds.StartGuildTrial / RespondGuildTrialInvite`。
- **期望的验收口径**:`GuildClientTests` 139 条 + `GuildWindowTests` 54 条 = **193 条**全部通过(按源码数出来的条数,**从未编译、从未运行**)。相对 B6a-cli 的 149 条净增 44 条:`GuildClientTests` +37(11 个 `[Test]` + `TrialTeamInvalidReasonsReadNaturally` 16 例 + `EndedLobbyExplainsItselfInTheStatusBar` 10 例);`GuildWindowTests` +8 −1(`TrialButtonStates` 取代 `TrialCardIsReadOnlyUntilTheTrialClientShips`)。2026-10-08 评审修复补的一条是 `CancellingATrialThatAlreadyMovedOnReportsTheLobbyInstead`(发起人取消时房间已开战 / 已被别人解散);另在两条既有用例里加了断言,条数不变:`ActivityStateAndBlockedLabelsCoverEveryCase`(9999 年末前 8 小时的档期时刻不抛异常,含边界两侧各一毫秒)、`TrialTextBoxesFitAtBodySize`(18 / 19 / 20 位编号的无名成员在选人框按钮上缩到 30 号仍放得下)。设计 §6.43 的对应:7 = `StartTrialValidatesLocally`,8 = `TrialTeamInvalidShowsMemberName`,15 = `TrialPickerLimitsSelectionAndRaisesEvent`,16 = `TrialInviteModalShownOnceAndResponds`,17 = `TrialButtonStates`。既有用例里 `ActivityTipsMapToReadableStatus` 改了两行期望(名单不合法、被婉拒现在带人名)与末尾一条断言(名单里有人离线时会补拉帮会快照)。

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
