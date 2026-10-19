# 装备栏、装备 tooltip 与战斗属性 UI

> 历史实现记录（2026-10-07）。2026-10-15 工作区审计时保留本文与装备战斗分拍回归测试；主干已继续增加强化、镶嵌、分享等功能，以下布局与验证状态只描述当时版本。

2026-10-07。背包页多了一列装备栏和一张装备 tooltip 卡片,可以穿上 / 卸下装备;属性窗的一级属性会标出装备加成,并多了一个「战斗」页签。全部是原生 uGUI,没有新开窗口,也没有动 HUD 入口。

服务端契约见同级服务器仓 `docs/design/equipment-attributes.md`(§3.2 协议、§6 客户端、§2.7 tip 码)。

**当前状态:代码已落盘,离线编译通过,用例在命令行里跑过(窗口级的是用 Unity 替身跑的);还没有在 Unity 里跑用例、出截图。** 详见「如何验证」。

## 界面结构

### 背包页(`GameplayWindow` 的背包分支)

页面内容区宽 2040,从左到右四栏:

| 栏 | x 范围 | 内容 |
|---|---|---|
| 分类栏 | 0 – 338 | 物品分类、随身资产、刷新 / 整理(没动) |
| **装备栏** | 352 – 488 | 一列槽位,新加 |
| 物品格 | 502 – 1586 | 7 列 × 4 行,每页 28 格(列距从 158 收到 156,格子大小没变) |
| 详情栏 | 1600 – 2040 | 宽度从 522 收到 440;选中装备时被 tooltip 卡片盖住 |

**装备栏**

- 服务器下发几个槽位就画几个格子,按槽号升序;空槽写部位名,部位名为空时写「栏位 N」。
- 穿着装备的格子画图标,点它就选中这件装备。
- 格子高度按槽位数均分栏高:4 个槽时每格约 102 高,7 个槽时约 71 高。
- 超过 8 个槽才出现「上 / 下」翻页。
- 装备栏还没读到时,栏里写「读取中… / 未同步 / 未读取」。

**tooltip 卡片**

选中一件装备(物品格里的或装备栏里的)时出现,盖在详情栏上。自上而下:

1. 图标、名称、「已穿戴 / 未穿戴」。
2. 「角色要求：等级 N」「职业要求：职业名」。不限职业显示「无」。已知角色等级且不够时,等级这一行变红 `#FF5540`。
3. 基础属性,一行一条,「名称：值」。
4. 随机属性,一行一条,「名称 当前值/上限值」,百分比两边都带 `%`。颜色:蓝 `#5AA7FF`、粉 `#FF7AD9`、黄 `#FFD84A`、绿 `#52D96A`(预留)。
5. 物品说明(服务器下发的 `description`,为空就不画这一行),自动换行,写不下的部分以省略号截断。
6. 底部按钮:物品格里的装备是「装备」,装备栏里的是「卸下」。

其他行为:

- 卡片比详情栏四周各收一圈(宽 396),露出详情栏的描金边。高度 598 – 692:属性行不超过 8 条(带说明时 7 条)按正常行距排;再多就压缩行距和字号,不丢行,也不会压到说明和按钮。说明至少留 64 高(约两行),卡片没长到上限时用掉属性行与按钮之间的全部空白。
- 穿脱或整理在途时按钮置灰;失败原因显示在物品格下方的状态行(例如「装备失败:等级不足,无法装备」)。超时、断线这类传输层失败显示成「装备失败:网络异常,请稍后重试」,不把内部的英文错误串放上屏。
- 不是装备的物品仍然用原来的详情栏,不出卡片。
- 切页、关窗、换角色、选中的装备不见了,卡片都会收起。

**选中项怎么跟着装备走**

窗口只记「玩家点过的那件物品的实例 id」,不记它在哪个包。每次重画时现查:

1. 这件在装备栏里 → 选中它,按钮是「卸下」。
2. 否则在当前可见的物品格里 → 选中它,按钮是「装备」。
3. 两边都没有 → 退回第一件可见物品;一件都没有就不选中。

所以穿上之后卡片自动变成「已穿戴 / 卸下」,卸下之后又变回来,不会指向一件已经不存在的东西。

**穿脱失败与刷新(`PlayerFeaturesClient`)**

人物背包和装备栏是两份快照,穿上 / 卸下成功时回包把两份一起换掉。失败时按下面的规则处理:

- **穿脱在途时,之前发出的读取不作废。** 穿脱成功,回包覆盖两份快照,那些更旧的读取才作废;穿脱被拒,它们照常落地。例如第一次打开背包页、行囊还没读回来就点了「卸下」并被拒,行囊随后照样显示。
- **穿脱在途时又要求刷新**(点了顶部「背包」页签、帮会兑换触发的重拉):不发包、不报错,记下来,等穿脱有了结果再补发一次。
- **失败后要不要自动重拉两份快照**,看失败的种类:
  - 不重拉:规则类拒绝(不是装备、等级不足、职业不符、没有栏位、背包已满、战斗中、冻结中)和 gate 限流。服务器状态没变;每次拒绝都补两个读取的话,连点会把读取放大到触发限流。
  - 重拉:超时 / 断线 / 解析失败(服务器可能已经执行)、回包没带快照(已执行但没带回来)、「物品不存在 / 未穿戴」(本地快照已过期)、内部故障和其他域透传过来的码(状态未知)。正在看仓库 / 临时格时只重拉装备栏。
- **失败原因不会被重拉冲掉**:它一直留在状态行,直到下一次穿脱开始、穿脱成功,或玩家主动刷新 / 重新打开背包页。重拉期间状态行先显示「正在同步…」。
- 装备栏读取失败时,背包页其余部分照常可用:装备栏写「未同步」,状态行显示「读取装备栏失败…」,点「刷新」重试。

### 属性窗(`AttributePanel`)

- **一级属性的装备加成**:加点页的数值在有外部加成时显示成 `170(+15)`。170 已经包含那 15 点,括号只是说明其中多少来自装备;加成为 0 时不显示括号。有未确认的加点时显示成 `170+2(+15)`。数值栏的位置和宽度没变,放不下时自动缩小字号。
- **「战斗」页签**:右缘第四个侧页签,接在「技能」下面。点开后按服务器下发的顺序列出全部战斗属性(必杀率、连击率、各种抗性等,目前 15 项),两列排布,「名称 + 数值」,百分比带 `%`;超过 16 项可以滚动。服务器没有下发这份列表时(老服务器),页签不出现,原有三个页签和各页内容的位置都不变。

没有把战斗属性塞进左栏六项二级属性下面:那里只剩 88 像素高,放不下 15 行。

## 数据来源

全部来自服务器,客户端零配表:

| 界面上的东西 | 来自 |
|---|---|
| 有哪些装备槽位、部位名 | 装备栏快照(`GetBag(bag_type = 2)`)的 `layout.equip_slots` |
| 哪个槽穿着哪件 | 同一份快照的 `layout.slots` + `items` |
| 装备名称、图标、等级 / 职业要求 | `BagItemInfo` 的 `name / icon_key / equip_level / equip_class / equip_class_name` |
| 属性行的名称、数值、上限、是否百分比、颜色档、顺序 | `BagItemInfo.base_attrs / affixes` 里每条 `EquipAttrLineInfo` 的 `name / value / cap / percent / tier / seq` |
| 一级属性的装备加成 | `AttributeDimensionInfo.bonus` |
| 战斗属性列表 | `AttributePanelInfo.combat`(每项带 `name / value / percent / sort`) |
| 角色等级(只用来标红) | 属性面板的 `level`;面板还没拉到时不标红 |

换表不用发客户端。服务器下发的字符串一律按纯文本显示:一行一个 Text,`richText` 关闭,不拼富文本。

## 与服务端契约的对应

| 契约(equipment-attributes.md) | 客户端 |
|---|---|
| §3.2 `EquipItem / UnequipItem`,回包带人物背包与装备栏两份全量 | `PlayerFeaturesClient.EquipItem / UnequipItem`;窗口的 `EquipRequested / UnequipRequested` 事件由 `GameplayUiRoot` 接过去 |
| §3.2 `BagLayoutInfo.equip_slots`(含空槽,按 slot 升序) | `EquipBagModel.Slots`;乱序、缺定义、重复定义都能画 |
| §3.2 `EquipAttrLineInfo`,显示顺序 = (tier, seq) | `EquipDisplay.LinesOf / FormatLine`(数据层)+ `EquipTooltip`(摆放与上色) |
| §6 颜色、卡片挂在窗口根 | `EquipUiStyle`;卡片是页面内容层的兄弟节点 |
| §2.7 tip 文案 | `PlayerFeaturesClient.DescribeEquipTip`(数据层),窗口原样显示;信封上的码(gate / 路由层)走同一套文案 |
| 服务端 `player_bag_handler.cpp`:穿脱已生效但快照建不出来时回 tip,客户端重新 `GetBag` | `PlayerFeaturesClient` 对「状态未知」的失败自动重拉两份快照(见上文「穿脱失败与刷新」) |
| §3.2 `AttributeDimensionInfo.bonus`、`AttributePanelInfo.combat` | `UiPointRow` 的括号标注、`AttributePanel` 的「战斗」页签 |

涉及的文件:

- `Assets/Scripts/UI/Ugui/Gameplay/`:`GameplayWindow.cs`、`GameplayUiRoot.cs`,新增 `EquipUiStyle.cs`(颜色与尺寸)、`EquipTooltip.cs`(卡片)、`EquipBagModel.cs`(槽位列表、选中项跟随、要求文案,纯函数)。
- `Assets/Scripts/UI/Ugui/Attribute/`:`AttributePanel.cs`、`AttributeUiWidgets.cs`。
- 数据层:`Assets/Scripts/Game/PlayerFeatures/PlayerFeaturesClient.cs`、新增 `EquipDisplay.cs`(属性行排版与排序)。
- 测试:`Assets/Tests/EditMode/Battle/` 下的 `EquipClientTests.cs`、`EquipBagModelTests.cs`、`EquipWindowTests.cs`、`AttributePanelDisplayTests.cs`。
- 截图夹具:`Assets/Editor/GameplayUiVerification.cs`、`AttributePanelV10Verification.cs`。

## 战斗演出兼容性

2026-10-08 核对。结论:**战斗表现代码不用改**。服务端装备属性带来的四种新事件(连击、反震、反击、抵抗),现有的分拍(`TurnPlan`)和演出(`BattlePresenter`)都能消化:不报错、不卡拍,伤害数字和血条都落在正确的单位上。本次没有改任何战斗表现代码,只加了一组分拍用例。

| 服务端发来的 | 现在怎么演 |
|---|---|
| 连击:同一组里对同一目标的第二条伤害 | 还是出手者那一拍,只冲一次;目标身上先后飘两段数字(间隔 0.12 秒),血条降两次 |
| 反震:出手者那一组里,受击者打回出手者的一条伤害 | 还是出手者那一拍;命中时出手者自己头上飘反震数字,血条按出手者的血量走;被弹死的话,这一拍之后接它的阵亡拍 |
| 反击:整次普攻之后另起一组的「攻击 + 伤害」 | 独立的一拍,受击者冲向原出手者,和普通攻击同一套演出;打死了接阵亡拍 |
| 抵抗(事件类型 15) | 被忽略:不成拍,也不影响同一招里别的伤害和 buff |

一回合的拍数可以多于出手人数(每次反击多一拍)。客户端没有任何地方按「第几拍 = 出手序第几个」取出手者,顶部预告条按单位 id 找瓦片。自动战斗、观战走同一条播放路径;重连补拉只拿状态,不重播事件。

**用例**:`Assets/Tests/EditMode/Battle/EquipCombatEventsPlanTests.cs`(16 条,纯逻辑)。事件流照服务端引擎实际产出的字段构造,用真 proto 序列化再解析后喂给 `TurnPlan.Build`。覆盖:连击、连击致死、反震、连击加反震、出手者被反震弹死(首段 / 连击段)、反击、反击致死、三种同回合、抵抗(夹在群攻中间 / 整招只有抵抗 / 单独一条)、`hit_kind` 与未知枚举值过线格式、连击段与反击段落空、反击拍计入演出预算。

已经做过的:离线编译后在命令行里实跑 16/16,回归 `TurnPlanTests` 27/27、`PlaybackBudgetTests` 8/8;对 `TurnPlan.cs` 的临时副本做了 6 处改坏(例如反震不并入出手者那一拍、未知事件打断当前拍、反击不另起拍),全部被这些用例抓到。

还没做的:**演出层(`BattlePresenter` / `BattleUnitView`)的结论来自读代码,没有在 Unity 里实跑。** 在完整工程里跑 `-testFilter "EquipCombatEventsPlanTests;TurnPlanTests;BattleArchivedActionPlaybackTests"`,再把反震率 / 反击率调高目视一遍。

不够好看的地方(数值都对,本次没改):

- **没有专门的字样**:连击、反击、反震、抵抗都没有飘字;战斗记录里只是普通的「攻击 / 受到 N 伤害」,反震那一行不写是谁弹的。`hit_kind` 目前没有带进分拍数据(`TurnEventInput.From`),要做字样时从那里加起。
- **反震时出手者会瞬间回到原位**:它在命中那一帧被自己的受击动作打断,不走回位那几步;出手者原位还会多播一份普攻的命中特效。有连击时,第二段数字仍然飘在目标身上,看起来像隔空打的。
- **抵抗完全没有表现**:一招只有异常状态又全被抵抗时,玩家只看到施法动作和扣蓝,目标毫无反应。
- **反击会让预告条的高亮跳一下**:高亮跳到反击者的瓦片,下一拍再跳回正常顺序。
- **反击会把回合演出拉长**:每次反击多 0.9 秒(打死人再多 0.6 秒)。全员自动时每回合只有约 1.7 秒可用、最多压到 6 倍速,也就是最多塞 10.2 秒的演出:5v5 全普攻(9.0 秒)刚好能播,再多两次反击(10.8 秒)这一回合就跳过演出、只落最终血量。手动模式能塞约 21 秒,基本不受影响。

## 如何验证

**前置:消息号。** `MessageIds.EquipItem / UnequipItem` 要等服务端 proto-gen 把号写进 `proto/message_id.txt`,再跑 `tools/gen_messageids.ps1 -ProtoRoot <服务器仓>` 才会生成。在那之前工程里有两个预期的编译错误(`PlayerFeaturesClient.cs` 引用了还不存在的常量),界面用例也编不过。

之后依次做:

1. **离线编译**:`pwsh -NoProfile -File tools/client_compile_check.ps1 -ShowErrors 40`。
2. **EditMode 用例**(Unity Test Runner;菜单 `MMORPG/UI/Test attribute and pet panels` 只跑属性 / 宝宝那一组,已把 `AttributePanelDisplayTests` 加进去):
   - `EquipClientTests`:两份快照分开缓存、穿脱单飞与整体覆盖、在途读取何时作废、在途期间的刷新补发、哪些失败会自动重拉、失败文案(含传输层串不上屏)、断线与换角色、属性行排版。
   - `EquipBagModelTests`:槽位排序、空名兜底、缺定义的槽、选中项跟随。
   - `EquipWindowTests`:装备栏渲染与翻页、tooltip 各行文本与颜色、物品说明的位置、点「装备 / 卸下」发出的请求、喂穿上 / 卸下回包后的界面、两份快照先后到达时选中不丢、被拒 / 超时时状态行的文案、装备栏读取失败时背包页仍可用、卡片何时收起、换角色清场。
   - `AttributePanelDisplayTests`:括号标注、战斗页签的出现 / 隐藏与排序。
   - 回归:`GameplayWindowTests`、`PlayerFeaturesClientTests`。
   - 命令行:Unity 加 `-batchmode -projectPath <工程> -runTests -testPlatform EditMode -testFilter "EquipClientTests;EquipBagModelTests;EquipWindowTests;AttributePanelDisplayTests;GameplayWindowTests;PlayerFeaturesClientTests" -testResults <结果.xml> -logFile <日志>`(带 `-runTests` 时不要加 `-quit`)。
   - `GameplayUiTestRunner.Run` 的名单里还没有这四个新类,走那个入口的话要先补上。
   - 这些用例都在 Battle 测试程序集里。同一个程序集里的 `PetAppearanceIntegrationTests.cs` 目前有 4 个与本功能无关的编译错误(`Vector3` 别名冲突),不先修掉的话整个程序集在 Unity 里编不过,上面的用例一条也跑不了。
3. **截图**(编辑器夹具,不连服务器):
   - `MMORPG/UI/Capture gameplay screens`:新增 `14-bag-equip-weapon-tooltip`(背包 + 装备栏 + 惊雷剑卡片)、`15-equipment-boots-tooltip`(装备栏里的惊雷靴,带「卸下」)、`16-bag-item-detail`(普通物品仍走详情栏)。原有的背包截图现在也带装备栏。
   - `MMORPG/UI/Verify attribute and pet v10 screens`:新增 `06-character-equip-bonus`、`07-character-combat-attributes`。
4. **联机**:服务端 dev 环境用 GM 发一件样例装备,走一遍穿上、替换、卸下、背包满时卸下被拒。

本次已经做过的:

- 用一份临时的占位消息号做离线编译:运行时程序集、Battle 测试程序集、改到的三个 Editor 脚本都是 0 个新增错误(运行时只剩基线自带的 `QdaoPetCompanion.cs` 5 个)。
- 不依赖 Unity 的用例在命令行里实跑:`EquipClientTests` 70/70、`EquipBagModelTests` 13/13,回归 `PlayerFeaturesClientTests` 31/31、`AttributeClientTests` 11/11。
- 窗口级用例用一份「Unity 替身」在命令行里跑过一遍:`GameplayWindowTests` 16/16、`EquipWindowTests` 22/22、`AttributePanelDisplayTests` 4/4。替身只模拟对象层级、名字、显隐、按钮可用、文本、颜色、位置尺寸的数值和点击事件,窗口、控件工厂、数据层都是仓内真实源码。另外对真实源码做了两轮小改坏(第一轮 34 处,例如穿上后不跟随选中、粉黄颜色对调;第二轮 25 处,针对穿脱失败与刷新,例如发包时就作废读取、失败后不重拉、规则类拒绝也重拉、重拉成功冲掉失败原因),全部被这些用例抓到。

还没做的:

- 上面那一遍**不等于在 Unity 里跑过**。替身不排版、不绘制、不加载真实资源和字体,所以文字放不放得下、切图拉伸好不好看、资源路径对不对,都要等 Unity 里的用例和截图。
- `EquipWindowTests`、`AttributePanelDisplayTests`、`GameplayWindowTests` 还没有在 Unity Test Runner 里跑;截图一张都没出;没有联机验证。

**2026-10-08 尝试在真实 Unity 里跑:没跑成,卡在 Unity 许可**

- **做法**:不在仓库工作树里开 Unity,另建副本工程 `E:/work/tmp/equip-verify-project`。副本工程 = 旧分支美术底座(`E:/work/mmorpg-client` 的 Assets / Packages / ProjectSettings / Library)+ 本工作树脚本覆盖(`Assets/Scripts`、`Assets/Tests`、`Assets/Editor` 下的脚本,以及还没入库的 `Assets/Resources/UI/Ugui/EquipmentV1` 切图)+ 三处仅副本内的基线补丁。三处补丁是:带占位消息号的 `MessageIds.cs`;`QdaoPetCompanion.cs` 的 `Vector3` 别名;删掉编不过的 `PetAppearanceIntegrationTests.cs` 与 `QdaoArchivedActionsPlayModeTests.cs`。它们都没有回写任何仓库。
- **结果**:Unity 6000.6.0f1 启动 4 秒就退出(退出码 198,日志 `No valid Unity Editor license found`)。本机个人版许可的离线有效期在 2026-10-07 07:20(UTC)到期,Unity Hub 是未登录状态,续期需要本人打开 Unity Hub 登录一次。**所以 EditMode 用例在 Unity 里一条没跑,截图一张没出**,上面「还没做的」两条原样成立。
- **在副本工程的源码上做了的**(覆盖时刻为本机时间 07:25 与 07:34,两次内容相同,含当时工作树里并行加入的 `EquipDetailCard` / `EquipUiSkin` / `EquipWish*`):
  - 用 Unity 自带的编译器离线编译全部程序集,均 0 错误:运行时 385 个文件、Battle 测试 29 个、Editor 37 个,以及其余 7 个 EditMode 测试程序集、PlayMode、Guild.Editor。
  - 不依赖 Unity 的用例实跑 181/181:`EquipClientTests` 70、`PlayerFeaturesClientTests` 31、`AttributeClientTests` 11、`EquipCombatEventsPlanTests` 16、`TurnPlanTests` 27、`PlaybackBudgetTests` 8、`EquipBagModelTests` 13、`EquipWishSelectionTests` 5。
  - 窗口级用例仍然只在 Unity 替身里跑:`GameplayWindowTests` 16/16、`EquipWindowTests` 22/22、`AttributePanelDisplayTests` 4/4。这一遍是卡片改由 `EquipDetailCard` 绘制之后重跑的。
  - 截图夹具和背包窗口用到的切图、图标,在底座美术里按文件名都找得到。这只是文件存在,不是加载结果。
- **许可续期后要补的**:上面第 2 步的 EditMode 用例(类名再加上 `AttributeClientTests;EquipCombatEventsPlanTests`)和第 3 步的两组截图(背包看 `01 / 14 / 15 / 16`,属性窗看 `01 / 06 / 07`,2560×1080 与 1920×1080 各一份)。副本工程保留着,可以直接用。

## 已知缺口

- **战斗表现没改**:连击、反击、反震沿用现有的攻击 / 受击演出,「抵抗」事件被忽略。数值和血条都对,哪里不够好看见「战斗演出兼容性」。
- **没有拖拽、没有双击穿戴**:只能选中后点卡片底部的按钮。
- **同部位多槽不能指定换哪只**:都占着时由服务器替换槽号最小的那件,协议里还没有「指定槽位」的字段。
- **职业不符不标红**:客户端拿不到角色职业,只有等级要求会标红;穿不穿得上以服务器回包为准。
- **等级标红依赖属性面板**:本次登录后没打开过属性窗、服务器也没推过面板时,客户端不知道角色等级,不标红。
- **老物品的卡片很空**:物品表里原有的装备行没有填等级和属性,卡片会显示「角色要求：等级 0」、没有属性行,只剩物品说明(表里有才有)。
- **装备的卡片不显示持有数量 / 叠放上限**:装备不可堆叠(恒为 1),改用卡片后这两行不再出现;物品说明已经搬到卡片上。
- **传输层失败的原始错误串看不到了**:界面只显示「网络异常,请稍后重试」;`PlayerFeaturesClient` 不依赖 Unity、自己不写日志,超时 / 解析失败的原始串目前没有落到任何日志里。
- **服务器持续报内部故障时,连点会多发读取**:每次「状态未知」的失败都会重拉两份快照(两个 `GetBag`)。正常情况下这类失败很少;如果服务器对每一次穿脱都回内部故障而玩家又在连点,读取量是穿脱的两倍,可能撞上 `GetBag` 的限流(10 次/秒)。
- **整理背包、任务页的失败文案没有跟着改**:它们仍然把传输层的原始串(例如 `rpc timeout`)交给界面;背包页只显示通用的「暂时未能同步」,任务页会原样显示。这是原有行为,不在本次范围。
- **装备栏里穿着的格子不写部位名**:只有图标,部位要看空槽时的文字或卡片上的名称。
- **属性行超过 8 条字会变小**:当前一件装备最多 8 条(基础 3 + 蓝 3 + 粉 1 + 黄 1),不带说明时刚好够;带说明的满 8 条装备字号从 28 缩到约 25.6。以后加绿色套装属性时要回来看。
