# 装备强化 / 镶嵌界面接入交接：Codex

更新日期：2026-10-15。**分工（用户 10-15 定）：Claude 负责强化 / 镶嵌的按钮逻辑，已经写好并有用例；Codex 负责客户端 UI 接入——入口、面板画面、布局、美术、窗口互斥、键盘、窗口级用例。** 按钮逻辑全部在 `EquipForgeUiState` 里，界面只读它的快照来画，点按钮时调它的方法；不要在界面里再写一份判断。

> **2026-10-16 接入进展：Codex 已完成强化 / 镶嵌入口和原生 uGUI 面板，Unity EditMode 相关用例 365/365 通过（新增窗口用例 12/12）。接入文件与验证说明见第 12 节。下文第 1–11 节保留原按钮逻辑交接背景及接入约束。**

服务端契约见服务器仓 `docs/design/equipment-enhance-inlay.md`；数据层（`PlayerFeaturesClient` 的三个入口、拒绝文案、重拉规则）见同目录 `EquipmentForge-20261009.md`。

## 1. 现状

| 部分 | 状态 |
|---|---|
| 服务器 | 10-13 已在 main，真实起栈冒烟通过：强化 0→1→2、2→3→4，镶嵌 / 取下，重新登录后强化等级与宝石都在（服务器仓 `docs/design/equipment-enhance-inlay.md` §11）。 |
| 客户端数据层 | 已在 main（`PlayerFeaturesClient.EnhanceEquip / InlayGem / RemoveGem`，正式消息号 250 / 251 / 252）。 |
| **按钮逻辑（本次）** | 分支 `feat/equip-forge-ui-logic`，见第 2 节。 |
| 入口与画面 | **没做**。现在进游戏仍然点不到强化 / 镶嵌：卡片「更多」打开的 `EquipActionsDialog` 里还是那句「强化、洗练、镶嵌尚未开放」。 |

## 2. 本次给你的东西

| 文件 | 内容 |
|---|---|
| `Assets/Scripts/UI/Ugui/Gameplay/EquipForgeUiState.cs`（新，含 `.meta`） | 面板逻辑类 `EquipForgeUiState` + 呈现快照 `EquipForgeView` 及其子类型。不引用 UnityEngine。 |
| `Assets/Scripts/UI/Ugui/Gameplay/GameplayUiRoot.cs` | 只加生命周期：建一个 `_forge`，公开为 `GameplayUiRoot.Forge`；`GameClient.Features` 换掉时 `Bind`；每帧 `Tick`；窗口 `Closed` 时 `Close`；强化结果的兜底提示——背包页正在屏幕上时写进它的状态行（下一次装备操作开始时收起），否则用 `BattleUiRoot.ShowToast` 飘字。 |
| `Assets/Scripts/Game/PlayerFeatures/PlayerFeaturesClient.cs` | 新只读属性 `ForgeCooldownRemainingMs`（剩余发包间隔）与 `EquipmentReadError`（装备栏读取本身的失败）；「金币不足,无法强化」改叫「银两不足,无法强化」。 |
| `Assets/Scripts/Game/PlayerFeatures/EquipDisplay.cs` | 强化消耗「金币 5000…」改成「银两 5000…」（契约 §0-1 把 kCurrencyGold 叫银两，背包页 / 属性 / 宠物都这么叫）。 |
| `Assets/Tests/EditMode/Battle/EquipForgeUiStateTests.cs`（新，含 `.meta`） | 63 条，纯逻辑，不碰 UnityEngine。 |
| `EquipForgeClientTests.cs` / `EquipForgeModelTests.cs` | 文案改成银两；新增 1 条冷却读数用例。 |

**没有动**：`GameplayWindow`、`EquipActionsDialog`、`EquipTooltip`、`EquipDetailCard`、`EquipWishDialog*`、`EquipUiStyle`、`EquipUiSkin`、`GameplayUiArt`、任何资源与 `EquipWindowTests`。

## 3. 你要做的事（建议顺序）

1. **把逻辑对象交给窗口。** `GameplayUiRoot.Awake` 里已经有 `_forge`（`GameplayUiRoot.cs` 的 `Forge` 属性）。建议给 `GameplayWindow` 的构造函数加一个可选参数 `EquipForgeUiState forge = null`，在 `_window = new GameplayWindow(design)` 处把 `_forge` 传进去。可选参数让 `EquipWindowTests` 现有的 `new GameplayWindow(...)` 不用改也能编译；窗口级用例需要面板时自己 new 一个 `EquipForgeUiState` 并 `Bind` 到测试里的 `PlayerFeaturesClient`。不建议在窗口里读 `GameplayUiRoot.Instance`，那样窗口级用例里拿不到它。
2. **入口。** `EquipActionsDialog`（卡片「更多」）里把占位文字「强化、洗练、镶嵌尚未开放」换成一个「强化 / 镶嵌」按钮。照「期望属性」的接法给 `EquipActionsDialog.Show` 多传一个回调 `() => OpenEquipmentForge(id)`（`GameplayWindow.OpenEquipmentActions(ulong id)` 里闭包带着 id），不必把 itemId 传进对话框。「查看人物属性」「期望属性」两个按钮保持不变，洗练仍未开放（文案可改成「洗练尚未开放」）。对话框的键盘导航写死了 3 个控件（`% 3`），加按钮要一起改。
3. **面板。** 建议新建 `EquipForgeDialog`，和 `EquipActionsDialog` / `EquipWishDialog` 一样挂在窗口 `_root` 下，是全屏弹窗。
   - `GameplayWindow.OpenEquipmentForge(ulong id)` 照 `OpenEquipmentWish` 写：先 `FindEquipment(id)` 判空，再 `_modalItemId = id; SetModalControls(true); forge.Open(id);`，然后显示面板。**`_modalItemId = id` 不能省**：`EquipActionsDialog` 的按钮都是先 `Close()` 再执行回调，它的 closed 回调会把 `_modalItemId` 清零并 `SetModalControls(false)`；不重新赋值的话，点「强化」后数据层的第一次 `OnChanged`（在途）会触发 `Render`，它的「物品消失就关弹窗」守卫（`FindEquipment(_modalItemId) == null`）会在发出强化的同一刻把面板关掉。
   - 订阅 `forge.Changed`，每次都按 `forge.View` **整份重画**。`Changed` 可能连着来几次，重画要幂等。
   - 关闭按钮 / Esc：调 `forge.Close()` 并隐藏。`forge.Close()` 也会由窗口关闭（`GameplayWindow.Closed`）触发，面板要能收到并跟着藏起来（在 `Changed` 里看到 `View.Stage == Closed` 就隐藏）。
   - `View.Stage == Missing`（装备不见了）时显示 `View.StageText`，然后关掉面板或只留一个关闭按钮。
   - 把它纳入窗口的弹窗管理：`HasEquipmentModal`、`CloseEquipmentModals`、`Render` 里「物品消失就关弹窗」那段、`SetModalControls`、`EquipmentModalOwnsKeyboard`。键盘可复用 `EquipWishDialogInput`。
4. **选宝石、确认降级两步**：见第 5 节。`EquipWishDialog` 是多选勾选框，不适合选宝石（这里是单选，还要显示图标、数量、属性）。
5. **背包页状态行**：`GameplayWindow.BagStateLine` 里 `_equipBusy` 那一支写死了「正在更换装备…」，强化 / 镶嵌在途时也会显示这句。建议改成中性的「正在处理装备…」（和面板里别处在途时的说法一致），同时改 `EquipWindowTests` 里 4 处断言（:315、:332、:407、:532）。
6. **（可选）卡片。** `EquipTooltip` 目前不显示强化等级与孔位。名称可以改用 `EquipDisplay.NameWithEnhance(ItemName(item), item.EnhanceLevel)`，孔位行可以用 `EquipDisplay.FormatSocketLine`，写法见 `EquipmentForge-20261009.md`「接线建议」第 1 条。

## 4. 呈现快照 `forge.View`（`EquipForgeView`）→ 控件

快照从不为 null，面板关着时是 `EquipForgeView.Closed`。**全部文本都是纯文本**，服务器下发的名称原样拼入，所以每个 Text 都要关掉 richText。数值不要自己算，成功率、消耗、属性值都在文本里。

| 字段 | 用途 | 例 |
|---|---|---|
| `Stage` | `Loading` / `Missing` 时画 `StageText`，「强化」与孔位区不画（`EnhanceButton` 已置灰、`Sockets` 为空）；「刷新」照常按 `RefreshButton.Enabled` 画（读取失败时可点，正在同步时置灰）；「关闭」始终可点。`Ready` 才画下面各块 | |
| `StageText` | 整块提示 | 「正在同步行囊与装备栏…」「行囊或装备栏暂未同步，请点击刷新重试」「这件装备已不在行囊或装备栏中」 |
| `Title`、`IconKey` | 头部，名称带强化等级 | 「青铜剑 +3」 |
| `Worn`、`LocationText` | 在哪 | 「已穿戴」/「行囊中」 |
| `EnhanceLevelText` | 强化等级 | 「强化等级 +3 / +12」；未开放时「该装备不可强化」 |
| `HasNextLevel` | true 时才画下面五项 | |
| `SuccessRateText` | | 「成功率 90%」 |
| `CostText` | | 「消耗 银两 5000、强化石 ×2」 |
| `OwnedText` | 只列这一级要消耗的东西 | 「持有 银两 12000、强化石 ×5」，余额未知时「持有 银两 —」 |
| `FailRuleText` | 已按当前等级封顶（服务器最多降到 +0，所以 +0 的装备总是「失败不降级」） | 「失败不降级」/「失败降 1 级」 |
| `PreviewLines` | 每行一个 Text | 「伤害：46 → 48」 |
| `EnhanceButton` | 「强化」按钮：`Label` / `Enabled` / `Hint` | 灰的原因如「银两不足」「已强化到最高等级」「操作太快，请稍候」 |
| `Sockets` | 孔位列表，按孔位号升序，含空孔；每行见下表 | |
| `SocketsEmptyText` | 没有孔时 | 「这件装备没有宝石孔」 |
| `Picker` | 正在给某个孔选宝石；null 表示不在这一步 | 见第 5 节 |
| `Confirm` | 正在确认一次会降级的强化；null 表示不在这一步 | 见第 5 节 |
| `StatusText`、`StatusTone` | 面板底部状态行；按 `Tone` 选颜色 | 见下 |
| `Busy`、`CoolingDown` | 只是信息，按钮的可点状态已经算进各个 `Button` 里 | |
| `RefreshButton` | 「刷新」，重新拉两份快照 | |

**孔位行 `EquipForgeSocketRow`**：

- `Text`：整行文本，例如「攻击孔：攻击宝石·一级（伤害 +30）」「攻击孔：未镶嵌」「…（未生效）」。
- `Kind`：`Empty` / `Active` / `Inactive`，用来选颜色。`Inactive` 的宝石仍然占着孔，也仍然可以取下。
- `GemIconKey`：镶着的宝石图标。
- `Button`：空孔是「镶嵌」，占着的孔是「取下」。`IsRemove` 区分这两种。
- `ChoiceCount`：空孔时能嵌进去的宝石种数。

**状态行 `StatusTone`**：

| 值 | 含义 | 建议颜色 |
|---|---|---|
| `Error` | 本面板为这件装备发出的操作被拒绝或网络失败（原因原样显示）；或者行囊 / 装备栏没同步上（「行囊同步失败，请点击刷新重试」/「读取装备栏失败:…」，此时置灰与数量还是旧快照算的） | 与背包页错误同色 `#9A442D` |
| `Busy` | 在途。本面板为这件装备发出的分别显示「正在强化…」「正在镶嵌…」「正在取下宝石…」；其余（背包页上的穿脱、为另一件装备发出还没回来的操作）显示「正在处理装备…」 | |
| `Success` | 「强化成功：青铜剑 +4」 | |
| `Failure` | 「强化未成功：青铜剑 仍为 +3，消耗已扣除」，掉级时是「降为 +2」 | 失败不是错误，别用错误色 |
| `Normal` | 空串 | |

状态行**只认领本面板为这件装备发出的操作**的失败原因和强化结果：背包页上穿脱的失败、之前为另一件装备发出的操作的失败都不会挂到这件头上。强化结果只挂到下一次装备类操作开始为止（不论谁发起的）。

## 5. 按钮 → 方法

| 控件 | 调用 | 什么时候不做任何事 |
|---|---|---|
| 「强化 / 镶嵌」入口 | `forge.Open(itemId)` | 没绑定数据层，或 id 为 0 |
| 关闭 | `forge.Close()` | |
| 刷新 | `forge.Refresh()` | `RefreshButton.Enabled` 为 false |
| 强化 | `forge.Enhance()` | 按钮灰着。**下一级失败会降级时不直接发出，而是进入确认这一步**：`View.Confirm` 变成非 null |
| 确认框「确认强化」 | `forge.ConfirmEnhance()` | `Confirm.CanConfirm` 为 false（在途 / 冷却时），确认框保持打开 |
| 确认框「取消」 | `forge.CancelConfirm()` | |
| 空孔「镶嵌」 | `forge.BeginInlay(row.Socket)` → `View.Picker` 变成非 null | 按钮灰着（例如「行囊里没有能嵌入这个孔的宝石」） |
| 选宝石列表点一项 | `forge.ChooseGem(option.ConfigId)` → 发出，`Picker` 收起 | `Picker.CanChoose` 为 false，或 config 不在列表里 |
| 选宝石「取消」 | `forge.CancelInlay()` | |
| 占孔「取下」 | `forge.RemoveGem(row.Socket)`，直接发出，不需要确认 | 按钮灰着 |

**两步弹层**（同一时刻最多一个非 null）：

- `Picker`：`Title`（「选择要镶入「攻击孔」的宝石」）、`Options`（按 config 升序，至少一项），每项有 `Name`、`IconKey`、`CountText`（「×5」）、`AttrText`（「伤害 +30」）、`ConfigId`。另有 `CanChoose` 与 `Hint`。孔被占了、宝石用完、装备不见时它会自己变回 null，面板照着重画即可。
- `Confirm`：`Title`「确认强化」，`Message` 是两行纯文本：

  ```
  成功率 75%，消耗 银两 1000、强化石 ×1。
  失败会降 1 级（+5 → +4），消耗不退还。
  ```

  按钮文字取 `ConfirmLabel` / `CancelLabel`；另有 `CanConfirm` 与 `Hint`。降级数按当前等级封顶（+1 遇到「降 3 级」写降 1 级，+0 不弹确认）。目前服务器表里所有等级都是「失败不降级」，所以实机暂时看不到确认框，用例里覆盖了这一步。

**结果提示**：`forge.Announced` 每次本面板发出的强化被服务器受理都会触发一次（成功或掷骰没过），面板已经关掉也照样触发，带一句 `notice.Text`。`GameplayUiRoot` 已经接好兜底：背包页正在屏幕上时写进它的状态行（`_window.SetEquipmentNotice`，下一次装备操作开始时自动收起）；窗口关着或停在别的页时改用 `BattleUiRoot.Instance?.ShowToast(notice.Text)` 飘字（那时状态行会在重开窗口时被 `Show` 清掉）。要改成别的提示方式就改 `GameplayUiRoot` 里那一段。**掷骰没过不是错误，不要传 `isError: true`**（那是伤害红）；想按成败换颜色，另选非错误色。

## 6. 必须守住的约束

- **不要在界面里直接调 `PlayerFeaturesClient.EnhanceEquip / InlayGem / RemoveGem`。** 绕过 `EquipForgeUiState` 会丢掉以下几件事：按同一份快照复核置灰、会降级时的确认、在途文案、结果提示的归属。
- 按钮的 `interactable` 只取 `Button.Enabled`，不要自己再拼 `BusyEquip` / 金币判断。置灰原因取 `Button.Hint`。
- **不要自动重发、排队或重试。** 强化不是幂等操作，再发一次就是再扣一次。也不要在 `Changed` 回调里调用意图方法。
- `Tick()` 已由 `GameplayUiRoot.Update` 每帧调用，界面不用再调。冷却结束时 `Changed` 会自己来。
- 每次 `Changed` 后重新读 `forge.View`，不要跨重画持有旧快照里的对象。
- 打开面板时，如果两份快照都在，`Open` 不发任何请求，所以 `EquipWindowTests` 里「打开弹窗不发请求」的断言（:626、:654、:668）不会被它破坏。行囊没同步上时，身上装备的卡片照样能点「更多」进来，这时 `Open` 会补拉行囊，面板先处在 `Loading`。

## 7. 现有测试和验证会受影响的地方

- `EquipWindowTests.cs:615-683`（`MoreAndUnavailableWishAreReachableWithoutMutationAndBackKeepsBagOpen` 等）：
  - 按名字找按钮：`EquipMore` / `EquipOpenWish` / `EquipViewAttributes`。
  - `GetComponentsInChildren<CanvasGroup>().Single()`：新面板如果自带一个激活的 CanvasGroup，这里会抛异常。
  - `HasEquipmentModal` 的真假会变。
- `EquipWindowTests.cs` 的 `FindButton(label)` 要求按钮文字唯一（:313、:333、:504 找的是「刷新」）。面板里的「刷新」激活时会和背包页撞文字，要么换个名字，要么让测试按 GameObject 名找。
- `EquipWindowTests.cs` :315 / :332 / :407 / :532 断言「正在更换装备…」（见第 3 节第 5 条）。
- `Assets/Editor/EquipmentIntegrationVerification.cs:131-146` 也按上面几个按钮名点击；`Assets/Editor/EquipmentUiVerification.cs:61` 有「炼化尚未开放」预览文案。

## 8. 建议补的窗口级用例（Unity EditMode）

1. 卡片「更多」→「强化 / 镶嵌」打开面板，画出标题、等级、成功率、消耗。点「强化」，出站的是 `EnhanceEquip(itemId)`；回包后状态行显示结果。按钮置灰时 `Hint` 也画了出来。
2. 空孔「镶嵌」打开列表，点一项，出站 `InlayGem(itemId, socket, config)`；占孔「取下」，出站 `RemoveGem(itemId, socket)`。
3. 会降级的强化（夹具里把 `EnhanceNext.FailLevelLoss` 设成 1）弹出确认框，取消不发，确认才发。
4. 装备消失时面板关闭；关闭背包窗口后 `forge.IsOpen` 为 false；Esc 只关面板、不关背包。
5. 面板按钮的名字和文字不与背包页冲突。

`FakeBattleTransport` 与正式 `PlayerFeaturesClient` 的用法照 `EquipForgeUiStateTests` 的 `SetUp`；物品夹具（带孔位、带下一级预览的武器，宝石堆）可以照抄那里的 `Weapon` / `WithGem` / `Gem`。

## 9. 验收

- 编译：`pwsh -File tools/client_compile_check.ps1`。
- Unity EditMode：程序集 `MmorpgClient.Tests.EditMode.Battle`（不带 `-quit`）。本批相关的用例类及条数：`EquipForgeUiStateTests` 63、`EquipForgeClientTests` 96、`EquipForgeModelTests` 35、`EquipClientTests` 70、`EquipBagModelTests` 13、`PlayerFeaturesClientTests` 31，另加你新写的窗口级用例。
- 实机（服务器 main 起栈后）：游戏里没有装备、宝石、强化石的产出，只能靠 GM 发，**客户端也没有发物 / 加银两的 GM 入口**。现成的账号是 `robot_9104` / `123456`（`robot_` 前缀走开发期密码认证），它被服务器仓 `robot\` 下的 `.\robot.exe -c etc/equip_forge_smoke.yaml` 跑过：冒烟每次强化前只把银两与强化石补到刚好够（缺多少补多少），所以跑完之后**强化石是 0**、银两最多剩下角色原本就有的那些；「新手木剑」穿在身上（跑一遍到 +2，跑两遍到 +4），攻击宝石 2101 在行囊里。**要实机点出一次真正的强化，先补货**：在服务器仓 `robot\` 下跑 `.\robot.exe -c etc/equip_forge_topup.yaml`（服务器 main `d7faa7fecd` 起有这个模式，主仓的 `robot.exe` 已按它重编）。它给 `robot_9104` 补 40 万银两、60 颗强化石、四种一级宝石各 3 颗——按强化表，够把一把装备从 +0 强化到满级——发完重新读背包核对到账，日志末行 `EQUIP_FORGE_TOPUP_OK …`。账号上还没有可强化的装备时，先跑一次 `etc/equip_forge_smoke.yaml`（它会发一把「新手木剑」并穿上）。不补货时这个号只能验：面板画面、孔位与镶嵌 / 取下、「强化材料不足」的置灰与「持有 … 强化石 ×0」。服务器表里 +1 到 +3 必成，装备到了 +3 之后，下一级的成功率才低于 100%（第 4 级 90%）。

## 10. 我做过的验证

| 项 | 结果 |
|---|---|
| 运行时程序集离线编译（Roslyn，引用取主工程 csproj，基于提交时的客户端 main，见提交说明） | 0 错误 |
| Battle 测试程序集离线编译 | 0 错误 |
| 变异测试 | 把 `EquipForgeUiState` 的 18 处关键判断逐个改坏（每次只改一行，只改临时副本），18 个全部被至少一条用例抓到 |
| **Unity EditMode（真 Unity 6000.6.0f1，副本工程 batchmode，客户端 main `db969ce4`）** | 跑 `MmorpgClient.Tests.EditMode.Battle` 全量 825 条：本批相关的八个用例类全过——`EquipForgeUiStateTests` 63/63、`EquipForgeClientTests` 96/96、`EquipForgeModelTests` 35/35、`EquipClientTests` 70/70、`EquipBagModelTests` 13/13、`PlayerFeaturesClientTests` 31/31、`EquipWindowTests` 29/29（真搭 uGUI 窗口）、`GameplayWindowTests` 16/16。另有 67 条失败，全在美术资源相关的四个用例类（`PetAppearanceIntegrationTests` 48、`BattleArchivedActionPlaybackTests` 12、`PetPanelInteractionTests` 4、`BattleRosterAppearanceTests` 3）；把本批改动退回合并前（`fccf6f34`）在同一副本重跑，失败的是同样 67 条，属既有问题，与本批无关 |
| 对抗评审 | 五个独立视角（状态机、契约、Unity 接线、用例、本文档）各自评审，每条发现再由独立代理尝试反驳；确认的 17 条（全是 minor / nit）已全部改完，本文是改完后的版本 |
| 纯逻辑用例（Unity 自带的 NUnit 在 .NET 上跑） | 308 / 308：`EquipForgeUiStateTests` 63、`EquipForgeClientTests` 96、`EquipForgeModelTests` 35、`EquipClientTests` 70、`EquipBagModelTests` 13、`PlayerFeaturesClientTests` 31 |

**没有做的**：

- 没有在游戏里实机点过（界面还没做）。
- `GameplayUiRoot` 里的那几行接线没有自动化用例（它是 MonoBehaviour）。
- 没有界面，所以也没有实机点过。

## 11. 已知限制

- `Open` 时如果人物背包快照不在，会调 `RequestBag(0)`。这会把数据层的「当前查看的背包类型」切回行囊。现在界面只用行囊（类型 0），不受影响；以后加仓库页时要留意。
- 冷却置灰最长 700 毫秒（`PlayerFeaturesClient.ForgeMinIntervalMs`）。回包很快时，按钮会在结果出来后再灰大半秒，这是限流档决定的。
- 面板开着时，强化结果会同时出现在面板状态行和背包页状态行（兜底提示）。换成飘字之后就只剩面板里那一处。
- 服务器 `Tip.xlsx` 里 `kEquipEnhanceGoldNotEnough` 的文案如果还写着「金币」，客户端已经按银两显示（做法与属性、宠物相同）；服务端表另行同步。

## 12. Codex 界面接入（2026-10-16）

入口已接通：背包或已穿戴装备的卡片「更多」→「强化 / 镶嵌」。人物属性与期望属性入口保留，未开放提示只保留洗练 / 炼化。

| 文件 | 接入内容 |
|---|---|
| `EquipForgeDialog.cs`（含 `.meta`） | 1480×900 玉色 / 纸张主面板，展示快照中的等级、成功率、消耗、持有、失败规则、属性预览、孔位、状态和置灰原因。单选宝石每页 6 项；会降级的强化先显示确认层。 |
| `EquipForgeSocketFocus.cs`（含 `.meta`） | 仅处理键盘选中，使孔位自动滚到可见范围；滚轮和拖动继续交给父 `ScrollRect`。 |
| `EquipActionsDialog.cs`、`GameplayWindow.cs`、`GameplayUiRoot.cs` | 注入既有 `EquipForgeUiState`，接入口和弹窗互斥；重新记录目标实例 id，防止发包同步通知误关窗口；关闭 / 换页 / 物品消失时同步关闭状态，销毁时解除面板订阅。 |
| `EquipTooltip.cs` | 装备名称显示强化等级，属性区补充宝石孔位；回包后使用新快照更新。 |
| `EquipForgeWindowTests.cs`（含 `.meta`） | 12 条窗口级用例，使用真实状态对象 / 数据层与假传输，从原生按钮验证请求、回包、冷却、置灰文案、两步确认、关闭、刷新、纯文本以及孔位滚轮 / 拖动 / 焦点。 |
| `EquipWindowTests.cs` | 原有 4 处忙状态文案断言更新为「正在处理装备…」。 |
| `Assets/Editor/EquipmentForgeUiVerification.cs`（含 `.meta`） | `EquipmentForgeUiVerification.CaptureAll()` 使用正式窗口和假传输渲染三种分辨率的原生 Unity 截图，同时断言镶嵌及确认强化的发包行为。 |

界面不直接调用强化 / 镶嵌 RPC，不计算数值，不自动重发。所有服务器文本关闭 richText；按钮状态和提示直接取快照。Esc 先取消宝石选择 / 降级确认，再关闭养成面板；关闭的同一帧不穿透关闭背包。孔位列表可滚动，较多的宝石和属性预览可翻页，键盘导航限定在当前层。

### 本次验证

- 运行时离线编译：`pwsh -File tools/client_compile_check.ps1`，0 错误。
- Unity 6000.6.0f1 EditMode：最终排版版本的 9 个相关用例类，**365/365 通过**。其中 `EquipForgeWindowTests` 12、`EquipWindowTests` 29、`GameplayWindowTests` 16，原 6 个数据 / 状态用例类共 308。结果：`E:\work\forge-ui-qa-20261016\editmode-results.xml`；日志：`E:\work\forge-ui-qa-20261016\editmode-final.log`。
- Unity 验证在交接中的副本 `E:\work-archive-20261015\tmp\forge-ui-verify-20261016` 完成；运行前同步当前客户端 `Assets/Scripts`、Battle 测试和新截图脚本，复用副本的资源缓存。
- 主仓与验证副本的 9 个相关源文件 SHA-256 完全一致，记录：`E:\work\forge-ui-qa-20261016\verified-source-hashes.json`。
- 原生截图：**18 张**（2560×1080、1920×1080、1280×720，各含可强化、选择宝石、镶嵌在途、降级确认、材料不足、同步中六种状态），脚本断言通过；已目视检查主面板、宝石选择、确认与提示，修正宝石题签与数量落入装饰边框的问题。输出：`E:\work\forge-ui-qa-20261016\screenshots`；记录：`E:\work\forge-ui-qa-20261016\verification.txt`。
- 本次没有连接游戏服务器或登录账号；真实强化 / 镶嵌联机验收仍按第 9 节补货后执行。第 10 节中「没有界面」的限制已由本次接入解除。
