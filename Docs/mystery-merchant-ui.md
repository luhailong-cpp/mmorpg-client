# 神秘商人(客户端)

> 状态(2026-10-09):代码已写完,**没有在 Unity 里运行过**(本机 Unity 许可证离线有效期已过)。
> 已有的证据:离线 Roslyn 编译三个程序集 0 错误;不依赖 Unity 的测试(规则 34 条 + 数据层 36 条)在离线
> NUnit 替身里 70/70 通过。窗口测试(23 条)、截图验收、联机购买都还没跑,见文末「还没验证的」。

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
- **购买超时之后要等重连**。`GameClient.Call` 按消息号先进先出匹配应答,迟到的旧应答会被当成下一次购买的结果;
  做法与帮会商店相同。这条提示会一直留到重连,不会被后续的查询盖掉。
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
主干整合时已按服务端神秘商人分支的 `proto/message_id.txt` 重新生成消息号:
查询 / 购买 / 公告分别为 **264 / 265 / 266**。旧分支的 250 / 251 / 252 已被装备 RPC 占用,不能继续使用。
服务端神秘商人分支尚需单独完成合并与发布;再次调整消息号后必须重跑第二条命令。

客户端主干已包含装备属性对应的 `PlayerBag.cs`;神秘商人继续复用其中的 `BagInfo`。
此次整合还重新生成了 `PlayerScene.cs` 并补齐摘要消息号,使已在主干的落地恢复摘要逻辑具备匹配的生成协议。

## 还没验证的

1. Unity 里的 EditMode 测试:`-runTests -testPlatform EditMode -testFilter MysteryMerchant`(三个测试类,共 93 条;
   其中窗口的 23 条依赖 UnityEngine,离线没跑过)。
2. 截图验收:`-executeMethod MysteryMerchantUiVerification.CaptureAll`(不能加 `-nographics`)。
   重点看角标宽度够不够、卡片上的文字有没有被截断(截图类里有断言);新增了「前往置灰 + 原因」「即将出现」两张。
3. 联机:本地把 `go/scene_manager/etc/scene_manager_service.yaml` 里 `MysteryMerchant` 那几行的注释去掉
   (每两分钟一轮、一直在场),两个客户端分别在商人所在线与别的线,核对:飘字、角标、商人形象的位置、
   点击走过去开店、购买后余额与背包、别的线上「前往」、限购、换轮后货架刷新;
   再核对两件评审后才有的行为:余额不够时按钮仍可点、点了提示「货币不足」;双击「购买」不会成交。
4. 商人形象:热区大小是按「角色帧高 9.8、人物占一半宽」估的,要在真实镜头下点一点。
5. 角标与线路角标并排占了右上角约 870 设计像素,其它分辨率下会不会挡到别的 HUD 没看过。
