# 易容形象界面换肤与人物背景故事阅读设计方案

> 对应美术交付源：`D:\work\image\designs\appearance-ui-v1\`（4 张效果图参考 + 2 枚图标 + 8 张无字透明控件 + `ui/CONTROL-USAGE.md`）。
> 范围：仅客户端资源导入与表现/交互增量，不改动服务端、协议、定价、存档或已有业务规则。

## 1. 需求分析

1. **接入已交付的道家 Q 版易容美术资源**：
   - 道具图标 `item_appearance_card.png` 导入 `Assets/Resources/UI/Icons/item_appearance_card.png`（对接 Item `3402` 的 `icon_key`）。
   - 大世界「修行」入口图标 `hud_icon_appearance.png` 导入 `Assets/Resources/UI/Ugui/WorldHud20261022/appearance.png`（对接 `WorldHudEntry.Appearance`）。
   - 8 张无字透明控件导入 `Assets/Resources/UI/Ugui/AppearanceV1/`，按 `CONTROL-USAGE.md` 区分 `fixed_size_only` 与 `horizontal_only_verified` 边距；通用外窗框、主/次按钮和关闭图标复用既有 `GameplayV1` / `MailV1` 皮肤。
   - 补齐 4 个视觉状态的原生控件组合：常规态（`01`）、试用进行中倒计时态（`02`）、消耗卡双头像二次确认弹窗（`03`）、背包 0 张缺卡跳转商城引导弹窗（`04`）。
2. **仅新增「人物背景与故事阅读」能力**：
   - 在人物详情增加「人物背景」摘要与次级按钮「读故事」，不挤压试用、购买、切换按钮；无档案人物自动收起新增区块，不留上一人的残留文案。
   - 点击「读故事」在当前窗口内弹出可纵向滚动的故事阅读层，包含顶部「返回 / 人物名 / 故事篇名 / 独立跳过」、中部可滚动正文与底部「读完返回」。
   - 首版 4 名候选人物按原始稳定 `appearance_id` 精确匹配（`00_reference_topright_boy` 陆照禾/阿禾、`10_crimson_spear_girl` 祝停云、`17_ghost_script_calligrapher_boy` 闻砚生、`09_bamboo_archer_girl` 桑听雨），档案名不覆盖玩家昵称，也不对历史视觉替代 ID 生效。
   - 公开可读、零副作用（不扣卡、不占试用位、不切形象、不领奖、不新增职业锁或属性加成）。

## 2. 设计分析

按客户端 SRP 三层分离原则拆分：
- **纯 C# 数据与交互状态机层**（`Assets/Scripts/Game/Appearance/AppearanceBiography.cs`，零 `UnityEngine` 依赖）：
  - `AppearanceBiography` / `AppearanceBiographyCatalog`：独立存放 4 名候选人物文案，仅按 `StringComparer.Ordinal` 匹配原始 `appearance_id`，便于后续独立改文案或迁表。
  - `AppearancePanelState`：管理当前选中 `appearance_id`、当前顶层弹层（`None` / `SwitchConfirm` / `NoCardGuide` / `StoryReader`）、未提交扣卡确认态、列表滚动位置快照与断线/失效条目收口。
- **美术加载与拉伸适配层**（`Assets/Scripts/UI/Ugui/Gameplay/AppearanceUiArt.cs`、`AppearanceWindowFit`）：
  - 统一加载 `AppearanceV1` / `Icons` / `GameplayV1` / `MailV1` 精灵，严格按 `CONTROL-USAGE.md` 对 6 张横条应用 `Image.Type.Sliced` 与等比 `pixelsPerUnitMultiplier`，对立绘台和卡槽保持 `fixed_size_only` 原比例。
  - `AppearanceWindowFit` 将窗口等比缩放至 `Screen.safeArea` 与父容器交集内，并在小游戏 WebGL 环境下额外预留右上角原生胶囊安全留白，兼顾 2560×1080、1920×1080 及窄横屏/刘海屏/手势条避让。
- **原生 uGUI 表现层**（`Assets/Scripts/UI/Ugui/Gameplay/AppearanceUiRoot.cs`）：
  - 行项按 ID 列表变更复用并保留滚动偏移，不因打开/关闭阅读层销毁列表或重载角色立绘/动作。
  - 弹层开启时通过 `_mainPanelGroup`（`interactable = false`、`blocksRaycasts = false`）+ 全屏阻断底幕 + 键盘焦点限域实现完全输入隔离。

## 3. 思想分析

- **单一真相与最小侵入**：所有网络状态、3402 形象卡持有数、10 元定价、1 小时试用位图倒计时与幂等提交继续唯一归属 `AppearanceClient`，UI 层绝不伪造资产或消费状态。
- **防误操作优先**：将“读故事”与“永久切换扣卡”在状态机和 uGUI 焦点两层彻底隔离；任何进入故事阅读或返回详情的路径都强制清空尚未提交的扣卡确认，确保返回后首击切换必然重新确认。
- **候选文案可独立维护**：传记文案集中在 `AppearanceBiographyCatalog`，后续策划调字或接配表只需替换该目录提供源，不影响界面与网络代码。

## 4. 漏洞分析与防线

| 潜在漏洞 / 边界风险 | 防护措施 |
|---|---|
| 历史视觉替代 ID（如 `QdaoHeadbandBoy`、`18_desert_sun_monk_girl`、`26_osmanthus_healer`）误命中四人传记 | `AppearanceBiographyCatalog` 仅精确查原始 `appearance_id`，严禁先调用 `QdaoCharacterCatalog.ResolveVisualId` |
| 先点一次旧“切换”进入待确认 → 打开故事 → 返回 → 再点“切换”误扣卡 | 切换永久形象改为弹窗确认且 `OpenStory` 与 `CloseStory` 均清空全部待确认与旧武装态；返回后焦点回到“读故事”按钮而非“切换” |
| 阅读层期间点击正文/空白、按回车或连按返回穿透到底层消费按钮 | 顶层全屏 `Image(raycastTarget=true)` + 底层 `_mainPanelGroup.interactable=false / blocksRaycasts=false` + 弹层键盘焦点圈定 + 单帧单次 Escape 消费 |
| 切换至无档案角色残留上一人的标题/正文，或换人未同步关闭阅读层 | `SelectAppearance` 同步清空未匹配文案并关闭旧阅读层；列表刷新发现正在阅读的原始 ID 失效时立刻关闭阅读层 |
| 角色缺立绘时借用其他角色图或阻断阅读 | 精确 ID 校验 + `sprite == null` 显示“暂无立绘”占位，文字背景与“读故事”照常可用 |
| 短故事一屏放得下但字体放大后截断 | 故事正文 `ScrollRect` 依据 `TMP_Text` 实时 `preferredHeight` 计算内容高度并保留底部留白，强制开启纵向滚动 |
| 打开/关闭故事重刷列表导致滚回顶部或反复重载角色立绘 | 列表按 ID 签名增量刷新并显式保存/恢复 `verticalNormalizedPosition`；立绘缓存最近一次 `(appearanceId, sprite)` 不重复加载 |
| 断线、切号或关闭窗口时异步回调污染新会话 | 复用 `AppearanceClient` 连接代次隔离，断线/解绑/关闭统一调 `ResetForCloseOrDisconnect` 收起阅读与确认弹层 |

## 5. 步骤分析与执行步骤

1. 校验 `D:\work\image\designs\appearance-ui-v1\` 全部 10 张运行资产哈希与透明格式。
2. 导入 2 枚图标与 8 张无字控件到 `Assets/Resources/` 并生成符合 `CONTROL-USAGE.md` 边距的 Unity `.meta`。
3. 编写纯 C# `AppearanceBiography.cs`（4 人物候选文案 + 交互状态机）与本设计文档。
4. 编写 `AppearanceUiArt.cs` 并升级 `AppearanceUiRoot.cs`：换肤、双弹窗（确认/缺卡）、人物背景摘要、可滚动故事阅读层、输入隔离与安全区适配。
5. 扩展 `AppearanceClientTests.cs` 覆盖 8 项验收用例（4 人物精确匹配、视觉替代防串、跳过重读、滚动位置保持、无档案无残留、失效条目关闭、先点切换再读故事返回不误扣卡、阅读零网络请求），执行 Roslyn 编译与 NUnit 实测。
6. 新增 Editor 离线截图验证入口 `Assets/Editor/AppearanceUiVerification.cs`，在 Unity 6000.6 批处理模式下实拍常规横屏与窄横屏截图，目视验收后汇总交付。
