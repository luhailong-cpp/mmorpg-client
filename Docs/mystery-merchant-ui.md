# 神秘商人(客户端)

> 当前契约(2026-10-19)：CPP 远端分支已有神秘商人，本地 CPP/Java 契约与它尚未统一。客户端完整保留功能、模型、界面、资源与协议类型；
> 生成器为未分配的三个方法生成 0，生产映射因此不注册推送、不发送查询或购买，角标显示「神秘商人 未开放」，
> 窗口显示「当前服务器未开放神秘商人」。旧分支 264/265/266 已分别被邮件推送、网络探针与聚宝斋占用，不能复用。
> 两套服务端分支统一并分配无冲突消息号后，重跑生成脚本即可自动恢复完整功能。测试通过显式注入独立假号覆盖已实现的行为。

> 状态(2026-10-17):**已在真 Unity 6000.6.0f1 里跑过** EditMode 测试与截图验收(主工程 batchmode)。
> EditMode `-testFilter MysteryMerchant` 94/94 通过(数据层 36、规则 34、窗口 24);截图 2560×1080 / 1920×1080
> 各 12 张,脚本内断言全过并目视检查过。还没做的是两个客户端联机,见文末「还没验证的」。
> 服务端的编译、单测与机器人冒烟(三遍 `MERCHANT_SMOKE_OK`)见服务端仓设计文档 §9.1。

服务端设计与协议:服务端仓 `docs/design/mystery-merchant.md`。一句话:每个整点商人随机落到天墉城的某一条分线上,
停留 30 分钟,卖一批打 6 / 5 / 4 折的东西,每人每件限购。

## 玩家看到什么

| 时刻 | 界面 |
|---|---|
| 商人出现(新的一轮,或换了线) | 屏幕上方飘字:「神秘商人出现在3线,停留 30 分钟。」(就在本线时说「本线」) |
| 平时 | 右上角线路角标左边多一个角标:在本线「神秘商人 本线 12:34」/ 在别的线「神秘商人 3线 12:34」/ 不在场「神秘商人 45:10 后出现」/ 到点了新一轮还没排出来「神秘商人 即将出现」;服务端还没有任何一轮时不显示 |
| 商人就在本线 | 主城里能看到他(名牌「神秘商人」)。点他,或点角标:离得远会先自动走过去,走到了打开商店 |
| 商店 | 三列两行的货架:名称、折扣、每份数量、原价、折后价、限购 n/N。「购买」要点两次(第一次变成「确认购买」);买满的置灰,**买不起的不置灰**(点了由服务端说「货币不足」) |
| 商人在别的线 | 点角标打开的窗口里只有「正在 3线」和按钮「前往 3线」(走的是线路面板同一套切线);那条线进不去时按钮置灰,底部说明原因(已满 / 冷却中 / 不在同一张地图) |
| 商人不在场 | 窗口里是下一次出现的倒计时 |

## 文件

| 层 | 文件 | 说明 |
|---|---|---|
| 规则与文案 | `Assets/Scripts/Game/MysteryMerchant/MysteryMerchantModels.cs` | 在不在本线、倒计时、折扣角标、能不能买、提示码文案。纯函数 |
| 数据层 | `Assets/Scripts/Game/MysteryMerchant/MysteryMerchantClient.cs` | 查询 / 购买 / 公告;不引用 UnityEngine |
| 商店窗口 | `Assets/Scripts/UI/Ugui/Gameplay/MysteryMerchantWindow.cs` | 只读视图进、意图事件出 |
| 宿主 | `Assets/Scripts/UI/Ugui/Gameplay/MysteryMerchantUiRoot.cs` | 自启动;角标、飘字、摆商人、接点击、转发意图 |
| 世界形象 | `Assets/Scripts/World/Tianyong/MysteryMerchantNpc.cs` | 纯显示,没有碰撞体 |
| 协议 | `Assets/Scripts/Proto/Generated/PlayerMysteryMerchant.cs`、`MysteryMerchantErrorTip.cs`、`Assets/Scripts/Net/MessageIds.cs` | 生成物,见下 |
| 测试 | `Assets/Tests/EditMode/Tianyong/MysteryMerchant{Models,Client,Window}Tests.cs` | |
| 截图验收 | `Assets/Editor/MysteryMerchantUiVerification.cs` | 菜单 MMORPG/UI/Capture mystery merchant screens (offline) |

`GameClient.cs`、`TianyongPlayerController.cs`、`TianyongMapBuilder.cs` 都没有改。

## 几个设计决定

- **服务端没有神秘商人的实体**,它只下发「哪条线、哪个坐标」。所以商人的形象是客户端自己摆的,
  只在「商人在本线 + 我就在那张地图」时出现;切线 / 换图 / 断线时撤掉,进场后重新问。
- **在不在本线只按 `scene_id` 比**,不看应答里的 `here`:公告是对所有人发的同一份,`here` 恒为 false。
- **点击商人不用物理拾取**。主城的点地寻路本来就不用物理射线;这里在 HUD 层放一块每帧跟着商人屏幕位置的
  透明热区。指针落在它上面时 `EventSystem.IsPointerOverGameObject()` 为 true,点地寻路自然不会同时触发。
- **走到再开店**。服务端按它记录的玩家坐标判购买距离(表里是 8),而坐标上报有延迟,所以客户端走到 6 以内才开店。
- **购买要点两次**。折扣商品一点就扣钱;不弹模态确认框,但也不让一次误触成交。两次点击挨得太近
  (`MysteryMerchantWindow.ConfirmMinIntervalSeconds` = 0.4 秒,多半是双击)不算确认。
- **报价防呆**。第一次点击时把看到的那份报价拍下来(`MysteryMerchantQuote`:轮次、单价、货币、物品、每份数量),
  第二次点击发出去的就是它;两次点击之间货架变了,确认作废。服务端发现任何一项对不上(货架换了 / 表热更了)就拒绝并让客户端刷新。
- **本地余额不拦购买**。客户端手里的余额是某次快照(开背包窗、上一次购买),scene 不推送余额变化;
  按它置灰的话,玩家攒够了钱也点不了,而且没有办法自己解开。余额只用于显示:优先用较新的那一份
  (`MysteryMerchantUiRoot.DisplayCurrency`),每次开店重拉一次人物背包。
- **购买超时之后要等重连**。购买请求可能已经在服务端成交，超时本身不能证明失败；核实前重复购买可能重复扣款。
  因此重连并重新取得权威背包与货架之前不再发购买，做法与帮会商店相同。提示会一直留到重连，不会被后续查询盖掉。
- **查询失败自己重试**(5 / 10 / 20 / 40 / 60 秒退避);查询在途时又有人要查,回来之后再问一次。
- **换了场景,上一个场景里问出来的应答整包丢弃并重问**:它说的「在不在本线」与货架属于已经离开的那条线。
  新旧结论的先后比较(`server_time_ms`)只在同一个场景内做 —— 跨节点之后两个节点的时钟不可比。
- **到点自动查询**。到了「下一次出现」的时刻而公告没来:12 秒、45 秒、120 秒后各问一次(各加按角色 id 算的 0..19 秒偏移,
  全服不在同一秒一起问),同一个时刻最多 3 次。服务端在整点后有 30 秒宽限期,第二次落在宽限期之后。
- **出现提示去重**。同一次落位(同一轮、同一条线)只弹一次:跨节点换场景时两个节点各发过一条公告。
- **走近的停靠距离**取 `min(3, 开店距离 − 0.5)`(`MysteryMerchantNpc.StandOff`):购买距离配小之后不会停在开店距离之外。
- **形象是顶替的**。主干里没有专属的商人立绘,暂用角色名册里的「灯穗小使」(`MysteryMerchantNpc.AppearanceId`)。
  换美术时只改这一个常量。

## 重新生成协议

服务端导表器与 proto-gen 跑完之后(顺序见服务端设计文档 §9):

```
pwsh -File tools/gen_proto.ps1 -ProtoRoot E:\work\xuanming-server-mmo
pwsh -File tools/gen_messageids.ps1 -ProtoRoot E:\work\xuanming-server-mmo
```

当前入库的两份 C# 是用同一个 protoc(35.1)对服务端工作树窄面生成的(基线核对:`BaseComp.cs` 逐字节一致);
购买请求在评审后加了三个字段(`expected_currency_type / item_config_id / item_count`),`PlayerMysteryMerchant.cs` 已随之重新生成;
历史整合曾按尚未合并的神秘商人分支生成查询 / 购买 / 公告 **264 / 265 / 266**；
当前服务端主干的这三个号已分别属于邮件推送、网络探针与聚宝斋，旧分支的 250 / 251 / 252 也已被装备 RPC 占用。
这些历史消息号不能用于本次默认的本地 CPP/Java 契约。CPP 远端 `origin/main` 的神秘商人仍正式使用它们；
双方契约尚未统一，客户端不能同时把这些号分配给两个功能。当前生成器按本地 CPP/Java 事实源对缺失方法明确输出 0，
并由完整消息映射校验禁用网络出口；其余已入库的功能与类型继续保留。待服务端分支统一后重跑两条命令生成兼容的正式契约。
提示码 `MysteryMerchantErrorTip.cs` 是 **30000–30011**:服务端最初定的 29000 段已被玩家收件箱(`player_inbox_error`)占用,
接到新 main 时改用 30000 段;这份 C# 按服务端沙盒导表产出的 tip proto 窄面重新生成过(2026-10-15)。
服务端正式导表之后再跑一次第一条命令,应当无差异。

客户端主干已包含装备属性对应的 `PlayerBag.cs`;神秘商人继续复用其中的 `BagInfo`。
此次整合还重新生成了 `PlayerScene.cs` 并补齐摘要消息号,使已在主干的落地恢复摘要逻辑具备匹配的生成协议。

## 已验证(2026-10-17,真 Unity)

1. EditMode:`Unity.exe -batchmode -projectPath <客户端> -runTests -testPlatform EditMode -testFilter MysteryMerchant`
   → 94/94。第一次跑挂了 1 条 `OpeningSkillBook_ClosesMerchantInsteadOfLeavingItAboveTheSkillPanel`(整合时新加的):
   测试用 `SendMessage("Awake")` 手动初始化,EditMode 下 Unity 会打出 `ShouldRunBehaviour()` 断言日志、被判为未处理的错误日志;
   改成与 `TeamInvitationWindowTests` 相同的反射调私有 `Awake` 后通过,断言本身(打开技能面板会收起商店)成立。
2. 截图:`-executeMethod MysteryMerchantUiVerification.CaptureAll -quit`(不加 `-nographics`)→ 两种分辨率各 12 张,
   脚本内断言全过(字形 / 截断 / 置灰 / 双击不成交 / 「前往」置灰写原因 / 角标文案)。目视:折后价、已购满置灰、
   比余额贵的商品不置灰、角标宽度都正常;「确认购买」四个字在按钮里偏满,但没有截断。
3. 副作用提醒:在主工程跑 batchmode 时,编辑器会把 `QdaoOriginalRosterV14/*/runtime-index.asset` 里的
   `sourceWriteUtcTicks`(源图文件时间)刷新掉,内容哈希不变;跑完 `git checkout --` 还原即可,别提交。

## 还没验证的

1. 联机:本地把 `go/scene_manager/etc/scene_manager_service.yaml` 里 `MysteryMerchant` 那几行的注释去掉
   (每两分钟一轮、一直在场),两个客户端分别在商人所在线与别的线,核对:飘字、角标、商人形象的位置、
   点击走过去开店、购买后余额与背包、别的线上「前往」、限购、换轮后货架刷新;
   再核对两件评审后才有的行为:余额不够时按钮仍可点、点了提示「货币不足」;双击「购买」不会成交。
2. 商人形象:热区大小是按「角色帧高 9.8、人物占一半宽」估的,要在真实镜头下点一点。
3. 角标与线路角标并排占了右上角约 870 设计像素,其它分辨率下会不会挡到别的 HUD 没看过。
