# 装备强化 / 镶嵌：数据层与界面接线说明

2026-10-09 / 10 落码。**这一批只交数据层、排版函数和纯逻辑模型，没有改任何界面文件**；界面入口由维护装备界面的一方（Codex）按本文接。
**尚未在 Unity 里运行过**：做过的只有离线编译和离线 NUnit（见「验证状态」），不是 Unity EditMode 的结果，更没有联机验证。

服务端契约见服务器仓 `docs/design/equipment-enhance-inlay.md`（协议 §3.3、客户端 §6、tip 码 §2.5）。一期（随机属性 + 穿脱）见同目录 `equipment-attributes.md`。

> 这批代码在隔离工作树 `mmorpg-client-forge`（分支 `feat/equip-enhance-inlay`）里。**生成物到位之前不要合进 main**：缺 3 个消息号常量和 11 个 tip 枚举名，运行时程序集有预期的 CS0117（见「预期的编译错误」）。

## 文件

| 文件 | 改了什么 |
|---|---|
| `Assets/Scripts/Game/PlayerFeatures/PlayerFeaturesClient.cs` | 三个新入口 `EnhanceEquip` / `InlayGem` / `RemoveGem`；`CharacterBag`、`LastEnhanceOutcome`、`OnEnhanceResult`、`GoldCurrencyIndex`、`ForgeMinIntervalMs`、`NowMs`；新类型 `EquipEnhanceOutcome`。穿上 / 卸下的行为与文案不变 |
| `Assets/Scripts/Game/PlayerFeatures/EquipDisplay.cs` | 强化 / 孔位 / 宝石各行的排版函数（纯文本）。`LinesOf` 的签名与返回不变 |
| `Assets/Scripts/UI/Ugui/Gameplay/EquipBagModel.cs` | 孔位视图、可嵌宝石、数量汇总、强化前置检查；新类型 `EquipGemSocketView` / `EquipGemChoice` / `EquipEnhanceCheck` |
| `Assets/Scripts/Proto/Generated/PlayerBag.cs` | 重生成：`BagItemInfo` 字段 14–20、`EquipAttrLineInfo` 字段 8 / 9、`EquipGemSocketInfo`、`EquipEnhancePreview`、三对请求响应 |
| `tools/gen_messageids.ps1` | 白名单加三行 |
| `Assets/Tests/EditMode/Battle/EquipForgeClientTests.cs`、`EquipForgeModelTests.cs` | 新用例 |

没有动：`GameplayWindow.cs`、`GameplayUiRoot.cs`、`EquipTooltip.cs`、`EquipDetailCard.cs`、`EquipActionsDialog.cs`、`GameClient.cs`、`MessageIds.cs`、`EquipErrorTip.cs`。

## 三个入口

都在 `PlayerFeaturesClient` 上（`GameClient.Features`）。目标装备可以在人物背包里，也可以穿在身上；仓库 / 临时格里的不行。

```csharp
features.EnhanceEquip(itemId);                    // 强化
features.InlayGem(itemId, socket, gemConfigId);   // 镶嵌：socket 从 0 起（0 是合法值）；gemConfigId 是宝石的 ConfigId，不是某一堆的 ItemId
features.RemoveGem(itemId, socket);               // 取下：宝石退回人物背包
```

共同的行为：

- **与穿上 / 卸下共用同一个在途标志 `BusyEquip`**。五种操作同一时刻只有一个；在途期间再点任何一个都静默丢弃（不发包、不报错）。按钮按 `BusyEquip` 禁用即可，不用另加标志。
- **发包间隔**：强化 / 镶嵌 / 取下两次发包至少隔 `ForgeMinIntervalMs`（700 毫秒，三者共用，从上一次**发包**算起）。间隔内的点击同样静默丢弃——所以「点了没反应」在回包后的大半秒内是正常的，界面不要因此补发。数据层不提供「冷却中」状态；想给反馈的话，界面可以在点击后按 `PlayerFeaturesClient.ForgeMinIntervalMs` 自己做按钮冷却。穿脱不受这个间隔约束。
- **回包整体覆盖人物背包与装备栏两份快照**，然后触发一次 `OnChanged`。界面照常在 `OnChanged` 里重画，不要自己扣金币 / 材料 / 宝石。
- **失败走 `OnError`，原因留在 `EquipmentError`**（与穿脱同一个属性）。文案格式 `{操作名}:{原因}`，操作名分别是 `无法强化` / `镶嵌失败` / `取下失败`。
- **传输失败（超时 / 断线）绝不自动重发**——强化不是幂等操作，再发一次就是再扣一次。数据层只重拉两份快照；界面也不要替玩家重试。
- 与整理背包、任务操作互斥：对方在途时报 `背包正在整理，请稍候` / `任务正在处理中，请稍候`。

### 发包前的本地拒绝

不发包、不重拉，文案走 `OnError`。两份快照有一份还没拉到时不做本地拒绝（空 id 除外），直接发包让服务器裁决。

| 情形 | 文案 |
|---|---|
| `itemId == 0` | `请先选择要强化的装备` / `请先选择要镶嵌的装备` / `请先选择要取下宝石的装备` |
| 镶嵌时 `gemConfigId == 0` | `请先选择要镶嵌的宝石` |
| 目标不在人物背包与装备栏里 | `{操作名}:物品不存在` |
| 强化：`EnhanceMaxLevel == 0`（未开放 / 不是装备） | `无法强化:该装备无法强化` |
| 强化：`EnhanceLevel >= EnhanceMaxLevel` | `无法强化:已强化到最高等级` |
| 镶嵌：装备没有这个孔 | `镶嵌失败:该装备没有这个宝石孔` |
| 镶嵌：孔里已有宝石（`GemConfigId != 0`） | `镶嵌失败:该孔位已镶有宝石,请先取下` |
| 镶嵌：人物背包里这种宝石总数为 0 | `镶嵌失败:背包里没有这颗宝石` |
| 取下：快照里列着这个孔，而且是空的 | `取下失败:该孔位没有宝石` |

**不在本地拦的**：金币 / 材料不足（快照可能偏旧，拦了会把人锁死，只给界面置灰用）；宝石类型与孔位不符（服务器裁决）；取下一个快照里没列出来的孔（策划删掉孔位定义后宝石仍可按孔位号取下）。

### 服务器拒绝之后要不要重拉

界面不用管，数据层自己处理；列在这里是为了看懂「失败之后为什么又多了两个读包」。

| 不重拉 | 重拉人物背包与装备栏 |
|---|---|
| 宝石类型不符、背包放不下取下的宝石、不是装备、战斗中、跨区冻结中、gate 限流 | 没有这颗宝石、孔位已占用 / 为空 / 不存在、不是宝石、已满级、物品不存在、内部故障、别的域的码、传输失败、回包缺快照 |
| 强化材料不足 / 金币不足，且发包时本地快照的判断（`EquipBagModel.CheckEnhance`）与服务器说的一致 | 同两个码，但本地以为够（或以为缺的是另一样）——快照已过期 |
| 无法强化，且本地快照本来就没有下一级预览（`CheckEnhance` 得 `Unavailable`：实例未初始化，或下一级是必败的行） | 无法强化，但本地快照带着下一级预览、以为能强化——快照已过期 |

失败原因不会被重拉成功冲掉；玩家主动刷新（`RequestEquipment`）时才收起。

## 强化结果

强化的「掷骰没过」**不是错误**：服务器回的是成功的响应（没有 tip），金币与材料已扣，两份快照照常覆盖。成败看结果，不看 `OnError`。

```csharp
public readonly struct EquipEnhanceOutcome { ulong ItemId; bool Success; uint Level; }   // Level = 操作后的强化等级

features.OnEnhanceResult += outcome => { /* 弹「强化成功 +N」/「强化失败」 */ };
EquipEnhanceOutcome? last = features.LastEnhanceOutcome;   // 最近一次的结果，没有为 null
```

- 顺序固定：覆盖两份快照 → 写 `LastEnhanceOutcome` → `OnChanged`（一次）→ `OnEnhanceResult`（一次）→ 补发在途期间被挡下的刷新。所以 `OnChanged` 里已经读得到结果，`OnEnhanceResult` 里快照已经是新的。
- 只有被服务器受理的强化才有结果。被拒绝、超时、回包缺快照都**不**触发 `OnEnhanceResult`、不写 `LastEnhanceOutcome`。
- `LastEnhanceOutcome` 的清空时机：任何一次装备类操作开始（穿上 / 卸下 / 强化 / 镶嵌 / 取下，包括随后被本地拒绝或被整理 / 任务挡下的那一次；被当作连点丢弃的不算）、断线、换角色。所以它不会和装备类操作的失败原因同时存在：状态行可以写成「有 `EquipmentError` 显示错误，否则有 `LastEnhanceOutcome` 显示结果」。
- 失败时等级怎么变由服务器的表决定（目前全填「不掉级」），客户端不推算，直接用 `outcome.Level`。
- 如果 `OnChanged` 的监听者在回调里换了角色，或又发起了一次装备类操作，这一次的 `OnEnhanceResult` 不再触发。

## 模型函数（`EquipBagModel`）

凡是要看「背包里有什么」的函数都显式接收**人物背包**快照：传 `features.CharacterBag`（bag_type = 0），不要传 `features.Bag`——后者是「当前查看的那一类」，可能是仓库。

| 函数 | 用途 |
|---|---|
| `GemSockets(item)` → `List<EquipGemSocketView>` | 一件装备的孔位，按孔位号升序，含空孔。`view.Socket`（请求里填的孔位号）、`view.Name`（已退回「孔位 N」）、`view.Occupied`、`view.Active`、`view.Info`（原始 `EquipGemSocketInfo`） |
| `FindGemSocket(item, socket)` | 按孔位号找孔，没有返回 null |
| `GemChoices(socketInfo, characterBag)` → `List<EquipGemChoice>` | 这个孔能嵌哪些宝石：人物背包里类型相符的宝石，**按 ConfigId 汇总数量**（同一种可能分几堆），ConfigId 升序。`choice.ConfigId`（传给 `InlayGem`）、`choice.Count`、`choice.Item`（名称 / 图标 / `GemAttr`，取先出现的那一堆）。只管类型，不看孔是否已占用 |
| `CountOf(bag, configId)` | 某种物品的总数（各堆之和），`ulong` |
| `CheckEnhance(item, characterBag)` → `EquipEnhanceCheck` | 强化前置检查，见下 |

「占用」与「生效」是两件事：

- `Occupied`：孔里有宝石（只看 `GemConfigId != 0`）。占着的孔显示「取下」，不能再嵌。
- `Active`：这颗宝石此刻计入属性（带 `Attr`）。占着但不生效的（宝石行被删、属性不可用）显示成 `（未生效）`，**仍然要给取下入口**。

### 置灰判据

| 控件 | 判据 |
|---|---|
| 所有装备类按钮 | `features.BusyEquip` 时禁用 |
| 「强化」 | `EquipBagModel.CheckEnhance(item, features.CharacterBag)`：`None` / `Unknown` 可点；其余置灰并显示原因 |
| 「镶嵌」（某个孔） | `!view.Occupied`，且 `GemChoices(view.Info, features.CharacterBag)` 非空 |
| 「取下」（某个孔） | `view.Occupied` |

`EquipEnhanceCheck` 的取值：

| 值 | 含义 | 建议文案 |
|---|---|---|
| `None` | 按本地快照看可以强化 | — |
| `Unavailable` | 强化未开放（`EnhanceMaxLevel == 0`，非装备也在这里），或服务器没给下一级预览 | 该装备无法强化 |
| `MaxLevel` | `EnhanceLevel >= EnhanceMaxLevel` | 已强化到最高等级 |
| `MaterialNotEnough` | 人物背包里材料总数 < `EnhanceNext.MaterialCount` | 强化材料不足 |
| `GoldNotEnough` | `CharacterBag.Currency.Values[GoldCurrencyIndex]` < `EnhanceNext.CostGold` | 金币不足 |
| `Unknown` | 人物背包或其中的货币还没拉到，本地无从判断 | 不置灰，让服务器裁决 |

检查顺序与服务器一致：未开放 → 满级 → 没有预览 → 材料 → 金币（两样都不够时报材料）。这只是提示，能不能强化由服务器裁决——置灰的按钮即使被点到，数据层也不会因为金币 / 材料不够而拒发。

是不是宝石：`BagItemInfo.GemType != 0`（宝石的 `EquipKind` 是 0，和普通物品一样，不能靠它分）。

## 逐字格式表（`EquipDisplay`）

全是纯文本，服务器下发的名称原样拼入，UI 一行一个 Text 且关掉 richText。冒号 `：` 与括号 `（）` 是全角；`percent` 为 true 的行数值后带 `%`。这里只排版，不拿加成万分比去算任何属性值——强化增量、下一级预览值、宝石属性都是服务器算好下发的。

| 场景 | 函数 | 格式 | 例 |
|---|---|---|---|
| 基础属性行，未强化 | `FormatLine(line)` | `{名称}：{值}`（与一期相同） | `伤害：40` |
| 基础属性行，已强化 | `FormatLine(line)` | `{名称}：{值}（+{增量}）`（值已含增量） | `伤害：46（+6）` |
| 下一级预览（一行） | `FormatEnhancePreviewLine(line)` | `{名称}：{值} → {下一级的值}` | `伤害：46 → 48` |
| 下一级预览（整件） | `EnhancePreviewLines(item)` | 每条基础属性一行，seq 升序；装备不带 `EnhanceNext` 时为空 | |
| 名称后缀 | `NameWithEnhance(name, level)` | `{名称} +{N}`；0 级不加 | `青铜剑 +3` |
| 成功率 | `FormatSuccessRate(permyriad)` | 万分比换成百分数，至多两位小数，去掉末尾的 0 | `90%`、`12.5%`、`0.01%` |
| 强化消耗 | `FormatEnhanceCost(next)` | `金币 {cost}`；要材料时再加 `、{材料名} ×{count}` | `金币 5000、强化石 ×2` |
| 孔位名 | `SocketName(socket)` | `Name` 非空用它；空则 `孔位 {socket + 1}` | `孔位 1` |
| 空孔 | `FormatSocketLine(socket)` | `{孔名}：未镶嵌` | `攻击孔：未镶嵌` |
| 已镶且生效 | `FormatSocketLine(socket)` | `{孔名}：{宝石名}（{属性名} +{值}）` | `攻击孔：攻击宝石·一级（伤害 +30）` |
| 已镶但不生效 | `FormatSocketLine(socket)` | `{孔名}：{宝石名}（未生效）` | `攻击孔：攻击宝石·一级（未生效）` |
| 宝石物品说明行 | `FormatGemAttr(item.GemAttr)` | `{属性名} +{值}`；不带 `GemAttr` 时为空串 | `伤害 +30` |
| 孔位列表 | `SocketsOf(item)` | 按孔位号升序，同号只留先出现的一条 | |
| 物品名占位 | `ItemNameOr(name, configId)` | 名称为空时 `物品 · {configId}` | `物品 · 2001` |

名称为空的占位（材料、宝石）用 `物品 · {configId}`，与背包格子上 `GameplayUiArt.ItemName` 的写法逐字相同；属性名为空仍是一期的 `属性 · {id}`。

强化相关的原始字段（都在 `BagItemInfo` 上）：`EnhanceLevel`、`EnhanceMaxLevel`、`EnhanceBonusPermyriad`（当前总加成，万分比）、`EnhanceNext`（`NextLevel` / `SuccessRate` / `CostGold` / `MaterialConfigId` / `MaterialName` / `MaterialIconKey` / `MaterialCount` / `BonusPermyriad` / `FailLevelLoss`）。满级 / 未开放 / 实例未初始化时不带 `EnhanceNext`。

## 接线建议

以下文件都不在这一批的改动范围内，写法只是示意。

1. **`EquipTooltip.Show`（卡片内容）**：名称改用 `EquipDisplay.NameWithEnhance(ItemName(item), item.EnhanceLevel)`；属性行照旧 `LinesOf` + `FormatLine`（已强化的基础属性行自动带 `（+N）`）；其后追加孔位行：

   ```csharp
   foreach (var view in EquipBagModel.GemSockets(item))
       data.Lines.Add(new EquipDetailLine(EquipDisplay.FormatSocketLine(view.Info), /* 生效 / 不生效 / 空孔 各一种颜色 */));
   ```

   宝石物品（`item.GemType != 0`）的卡片加一行 `EquipDisplay.FormatGemAttr(item.GemAttr)`（为空串时不加）。
2. **`EquipActionsDialog`**（现在写着「强化、洗练、镶嵌尚未开放」的地方）：
   - 强化区：`item.EnhanceNext != null` 时显示 `FormatSuccessRate(next.SuccessRate)`、`FormatEnhanceCost(next)`、`EnhancePreviewLines(item)`；按钮按上面的置灰判据。
   - 孔位区：遍历 `EquipBagModel.GemSockets(item)`；空孔列出 `GemChoices(view.Info, features.CharacterBag)` 供选择，选中后 `InlayGem(item.ItemId, view.Socket, choice.ConfigId)`；占着的孔给「取下」→ `RemoveGem(item.ItemId, view.Socket)`。
3. **`GameplayUiRoot`**：照 `EquipRequested` / `UnequipRequested` 的接法，把窗口的三个意图事件接到 `_client.EnhanceEquip` / `InlayGem` / `RemoveGem`；订阅 `_client.OnEnhanceResult` 弹结果提示。`Dispose` 会清掉监听。
4. **状态行**：`GameplayWindow.BagStateLine` 里 `BusyEquip` 对应的「正在更换装备…」对强化 / 镶嵌不贴切，建议改成中性的说法。数据层里另有三处「装备正在更换，请稍候」（`BusyEquip` 时切背包类型、整理、任务操作被挡的提示）同样偏向穿脱，这一批没有改（那是既有文案，有用例与界面依赖）。

打开强化 / 镶嵌界面前确保两份快照都在：`features.RequestBag(0)` 与 `features.RequestEquipment()`。

## 预期的编译错误与消除步骤

生成物到位之前，运行时程序集有 **19 处 CS0117，全部在 `PlayerFeaturesClient.cs`**：

| 位置 | 处数 | 缺的符号 |
|---|---:|---|
| `EnhanceEquip` / `InlayGem` / `RemoveGem` 里的 `_net.Call(MessageIds.…)` | 3 | `MessageIds.EnhanceEquip`、`MessageIds.InlayGem`、`MessageIds.RemoveGem` |
| `DescribeEquipTip` 的 switch | 11 | `equip_error.KEquipEnhanceUnavailable`、`KEquipEnhanceMaxLevel`、`KEquipEnhanceMaterialNotEnough`、`KEquipEnhanceGoldNotEnough`、`KEquipGemNotGem`、`KEquipGemNotOwned`、`KEquipGemSocketInvalid`、`KEquipGemSocketOccupied`、`KEquipGemSocketEmpty`、`KEquipGemTypeMismatch`、`KEquipGemBagFull` |
| `IsRuleRejection` 的 switch | 5 | 上面 11 个里的 `KEquipGemTypeMismatch`、`KEquipGemBagFull`、`KEquipEnhanceMaterialNotEnough`、`KEquipEnhanceGoldNotEnough`、`KEquipEnhanceUnavailable` |

测试程序集里只有 `EquipForgeClientTests.cs` 引用这些符号（31 行：消息号常量 9 处、tip 枚举名 24 处）；`EquipForgeModelTests.cs`、`EquipDisplay.cs`、`EquipBagModel.cs` 一处都没有。

消除步骤（都由服务器侧的生成触发，**不要手写数字**）：

1. 服务器正式导表（`data/tip/Tip.xlsx` 的 `//equip_error` 组多出 11 个码）→ 客户端重跑
   `pwsh -File tools/gen_proto.ps1 -ProtoRoot <服务器仓>`，`Assets/Scripts/Proto/Generated/EquipErrorTip.cs` 里出现 11 个新枚举名。
2. 服务器全量 proto-gen 给三个新方法发号（写进 `proto/message_id.txt`）→ 客户端重跑
   `pwsh -File tools/gen_messageids.ps1 -ProtoRoot <服务器仓>`（白名单已加好），`Assets/Scripts/Net/MessageIds.cs` 里出现 3 个新常量。脚本若报 `missing in message_id.txt: SceneBagClientPlayerEnhanceEquip …`，说明服务器还没发号。
3. 两步都做完，19 处 CS0117 应当同时消失；之后跑 EditMode（`MmorpgClient.Tests.EditMode.Battle`）。

`PlayerBag.cs` 已经在这个工作树里重生成过（只依赖 proto 源，不依赖发号），第 1 步重跑 `gen_proto.ps1` 时它不应再有差异。

服务器限流表补上这三个方法之前，它们吃默认档（相邻两秒合计 3 个，约 1.5 次 / 秒；超限计非法包，累计 50 次被 gate 断线）；数据层的 700 毫秒间隔就是按这个默认档留的（持续连点要 ≥ 667 毫秒才不超限）。这只是兜底：同号的别的来源（脚本、多开）不受它约束，限流补丁仍然要执行。

## 验证状态

**没有在 Unity 里跑过任何东西**（没有启动编辑器，没有 EditMode 结果，没有截图，没有联机）。做过的只有离线验证，脚本与产物都在会话临时目录，不在仓库里：

| 项 | 做法 | 结果 |
|---|---|---|
| 运行时程序集，不加占位 | Roslyn `csc` 直接编 `Assets/Scripts/**/*.cs`（393 个文件），引用取主工程 `MmorpgClient.csproj` | 19 个错误，全部是上表的 CS0117，全部在 `PlayerFeaturesClient.cs`；没有别的错误 |
| 运行时程序集，加占位 | 用临时目录里的两份占位文件顶替 `MessageIds.cs` / `EquipErrorTip.cs` 再编。枚举占位是把服务器沙盒导表产出的 `equip_error_tip.proto` 过了一遍 `protoc`，消息号占位是随手取的值 | 0 错误 |
| Battle 测试程序集，只编译 | `Assets/Tests/EditMode/Battle/*.cs`（30 个文件，含窗口级用例）对占位版运行时程序集编译，引用取主工程的测试 csproj | 0 错误 |
| 纯逻辑用例 | 把 `FakeBattleTransport` + 5 份用例编成可执行程序，用 Unity 自带的 `nunit.framework.dll`（真实 NUnit，不是替身框架）在 .NET 上跑 | 242 通过 / 0 失败：`EquipForgeClientTests` 93、`EquipForgeModelTests` 35、既有的 `EquipClientTests` 70、`EquipBagModelTests` 13、`PlayerFeaturesClientTests` 31 |
| 用例有效性 | 把被测的三份源码各改坏一处（共 44 种改法，只改临时副本）再跑同一批用例 | 44 种全部被至少一条用例抓到，没有存活的 |
| `PlayerBag.cs` | 新旧对比（忽略换行符） | 3165 行新增 / 60 行替换：新消息与新字段、描述符、消息下标顺移，外加两处随服务器 proto 注释更新的文档注释 |

离线 NUnit 与 Unity EditMode 的差别：运行时是 .NET 而不是 Unity 的 Mono，没有 Unity 的程序集加载与域重载；这些用例不碰 `UnityEngine`，所以差别预期很小，但它**不能替代**在编辑器里跑一遍。窗口级用例（`EquipWindowTests` 等）没有跑。

## 已知限制

- 正在看仓库 / 临时格时，装备类操作失败后的自动重拉只拉装备栏，不拉人物背包（与穿脱的既有行为一致：重拉的是「当前查看的那一类」）。这时 `CharacterBag` 可能偏旧，切回行囊页（`RequestBag(0)`）才刷新。
- 强化超时后玩家手动再点一次，可能再扣一次（服务器不做幂等）。数据层保证不自动重发，并在超时后重拉快照，让界面尽快看到真实状态。
- 时钟 `NowMs` 是 public 的可写属性（契约里写的是 internal）：测试程序集没有 `InternalsVisibleTo`，而构造函数签名不能动。它只给用例用，生产代码不要赋值。
- `PlayerFeaturesClient` 通过别名引用了 `UI.Ugui.Gameplay` 命名空间里的 `EquipBagModel` / `EquipEnhanceCheck`（纯逻辑，不依赖 `UnityEngine`），为的是「本地拒绝 / 重拉判定」与「界面置灰」只有一份判据。两者目前在同一个程序集里；以后若拆程序集，这两个类型要跟着数据层走。
- `KEquipInBattle` / `KEquipFrozen` 的文案是「…无法更换装备」，强化 / 镶嵌复用时措辞不完全贴切（服务器表里的既有文案）。
