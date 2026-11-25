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

## 6. 批次二增量（2026-11-23）：永久多人物收藏衣柜与建角收紧

- **创角范围收紧与衣柜立绘隔离**：`QdaoCharacterCatalog.IsCreatableAppearance` / `CreationOriginalAll` 收紧至 2 名免费初始人物（`00_reference_topright_boy`、`01_ice_sword_girl`），新增 `IsReleasedAppearance` 保留 8 名现役人物在易容衣柜中的轻量立绘租约加载（`AcquireCreationPortrait` / `PrefetchCreationPortraitTexture`），既有职业/性别老号回退路径不变。
- **永久衣柜状态与防误扣卡**：
  - `AppearanceClient.SwitchAppearance(targetId, expectFreeSwitch)` 对齐服务端 `SwitchAppearanceRequest.expect_free_switch`，补齐 `KAppearanceNotCardUnlockable`（33007）、`KAppearanceAlreadyOwned`（33008）、`KAppearanceNotOwned`（33009）中文文案并在拒绝时强制刷新快照。
  - `AppearancePanelState.RequestSwitch` 新增 `targetOwned` / `targetCardUnlockable` 参数与 `ReadyForFreeSwitch` / `LockedExclusive` 结论：已拥有形象直接发起 `expectFreeSwitch: true` 免费切换（免弹窗、0 扣卡）；未拥有且 `card_unlockable=false` 阻断扣卡流程；未拥有且支持卡兑换沿用缺卡引导 / 二次确认弹窗，并明确告知原形象保留在衣柜中。
  - `AppearanceUiRoot` 与 `AppearanceUiArt` 升级列表标题为「角色衣柜 · 已收集 X/Y」，区分「使用中 / 试用中 / 已获得 / 可试用1小时 / 未获得 / 玩法解锁」角标，已拥有目标禁试用按钮并切换主按钮文案为「免费切换」。

## 7. 批次二·续增量（2026-11-23）：人物收藏系列（纯外观与叙事，零属性）

- **纯 C# 数据与门禁层**：新增 `AppearanceSeriesModels.cs`（`FilterBySeries`、`FindSeries`、`CountCompletedSeries`、`TryGetUnlockedEpilogue`），并在 `AppearanceBiography.cs` 扩展 `SelectedSeriesId`、`AppearanceOverlayKind.SeriesEpilogue`、`SelectSeries`、`SyncSeriesSelection`、`OpenSeriesEpilogue`、`CloseSeriesEpilogue`，严格按服务端权威 `SeriesProgress.Completed == true` 放行合篇故事。
- **原生 uGUI 与响应式布局**：新增 `AppearanceUiRoot.Series.cs` 并接入 `AppearanceUiRoot.cs` / `AppearanceUiRoot.Layout.cs` / `AppearanceUiRoot.Reading.cs`，在易容左侧补齐「集齐系列 M/N」、横向可滚「系列筛选栏」、选中系列展开的「系列信息条（印章/铭牌/简介/读合篇）」及「系列合篇阅读层」（支持宽屏 2160×1000 与窄屏 1180×650、字号放大滚动与完整输入隔离）。
- **美术资源槽位与平滑回退**：`AppearanceUiArt` 预留 `Assets/Resources/UI/Ugui/AppearanceSeriesV1/`（`series_tab_normal/selected`、`series_banner_card`、`series_epilogue_card`、`series_badge_dim/lit`、`series_seal[_<series_key>]_dim/lit`），未导入新图前自动回退到现有皮肤与配色，待 Codex 交付去底透明 PNG 后放入即自动生效。

## 8. 批次二·终章（2026-11-24）：全员 9 人人物志配表化与端到端收口

- **需求与设计分析**：将人物志文案从 `AppearanceBiographyCatalog` 硬编码迁至权威 `CharacterAppearance` 配置表（字段 `10..14`：`archive_name`/`nickname`/`biography_summary`/`story_title`/`story_body`），补齐全部 9 位现役角色档案与往事（陆照禾、霜见雪、许炽、苏青莲、林知音、桑听雨、祝停云、沈澄、星璇），并将祝停云往事中已删角色「闻砚生」替换为现役角色「沈澄」。
- **思想与防线**：严格遵守「不长期缓存 `*TableManager` 行引用」的热更契约，`AppearanceBiographyCatalog.TryGetByRawAppearanceId` 每次现查 `CharacterAppearanceTableManager.Instance.FindAll()` 并按原始 `appearance_id`（`StringComparer.Ordinal`）精确匹配，绝不经 `ResolveVisualId`；四项核心字段必须同时非空才展示人物志，任意未配置人物志的条目仍自动收起背景区；仅在表尚未加载（`Count == 0`）的隔离测试环境回退至同源内置只读目录。
- **执行步骤**：同步最新 `characterappearance.pb` 与生成类 → 改造 `AppearanceBiography.cs` → 更新 `AppearanceClientTests.cs` / `tools/tests/appearance_chronicle/` / `AppearanceUiVerification.cs` → 执行 `client_compile_check.ps1`、Unity EditMode 单测、Unity 原生截图验收与离线模型检查；并输出 `AppearanceSeriesV1` 完整切图规格供 ChatGPT 生成后直接落盘自动生效。

---

## 9. 人物收藏系列美术素材规格与接入记录（`AppearanceSeriesV1`）

客户端代码 `AppearanceUiArt.cs` 与 `AppearanceUiRoot.Series.cs` 已完成 **11 张正式透明 PNG** 接入，来源仓库 `image`（`designs/appearance-series-v1/`，提交 `a1517134644b34a69dc6cfcc16c227da3089460d`，程序矢量栅格化，无 AI 模型调用），落盘目录：

- `Assets/Resources/UI/Ugui/AppearanceSeriesV1/`（逐图 SHA-256 与 `validation.json` 一致，零内嵌文字，轮廓外 100% 透明 Alpha）。

### 9.1 视觉基调与色板（严格同源 `AppearanceChronicleV2` / `team-ui-v2`）

- 风格：国风轻仙侠青玉暖金卷云＋宣纸手账风，干净扁平微质感插画，无写实噪点、无厚重发光、无整屏底景。
- 严格红线：**全部 PNG 严禁烘焙任何文字、数字、印章汉字、水印**（所有文案均由 TMP 实时绘制）；**控件轮廓外必须为 100% 纯透明 Alpha=0**。
- 配色锚点（来自 `AppearanceUiArt.cs`）：
  - 主青玉深色 `#205B46`（DeepJade）、高亮青玉 `#2B6A50`（SeriesLitBg）、墨青正文字 `#304736`（JadeInk）
  - 宣纸底 `#F7F0E1`（ParchmentCard）、内纸面 `#FBF6EA`（ParchmentInner）、暖金边 `#C9AB70`（ParchmentBorder）、烫金暗色 `#8E651F`（SeriesGoldInk）
  - 未点亮灰纸 `#E6DEC9`（SeriesDimBg）、未点亮暖灰 `#70634E`（MutedInk）、素石玉灰 `#D8CFBC`、反白暖米字 `#FFF3D6`（WarmCream）

### 9.2 已接入切图与 Unity Border（11 张正式素材）

| 文件名 | 尺寸 (px) | UGUI Image 类型 | Unity `spriteBorder` `(L, B, R, T)` | 布局与安全区对齐 |
|---|---|---|---|---|
| `series_tab_normal.png` | `240×64` | `Sliced`（横向中段 50%） | `60, 0, 60, 0` | 未选中系列标签，文字留左右 `32px` 云纹安全边距 |
| `series_tab_selected.png` | `240×64` | `Sliced`（横向中段 50%） | `60, 0, 60, 0` | 选中系列标签，深青玉双金线描边 |
| `series_badge_dim.png` | `220×44` | `Sliced`（横向中段 50%） | `55, 0, 55, 0` | 未点亮印章条 / 顶栏统计条，文字避让两端卷云（宽屏 `42px` / 信息条 `30px` / 窄屏 `52px`） |
| `series_badge_lit.png` | `220×44` | `Sliced`（横向中段 50%） | `55, 0, 55, 0` | 已点亮印章名条，翠青玉暖金边 |
| `series_banner_card.png` | `612×120` | `Simple`（宽屏原比例 1:1） | `0, 0, 0, 0` | 宽屏 `612×120` 原比例显示：印章槽 `49,26,68,68` 精准居中于圆环 `(83,60)`，正文与按钮严格落在金竖线 `x=151` 右侧 `x=166..586`；窄横屏 `1060×92` 切回九宫格 `application_card` 防圆环拉扁 |
| `series_epilogue_card.png` | `1192×528` | `Simple`（宽屏原比例 1:1） | `0, 0, 0, 0` | 宽屏 `1192×528` 原比例显示：正文视口落在卡内 `x=112..1056, y=48..480`、滚动条 `x=1068, y=96..432`，完全避让四角云回纹；窄横屏 `1000×320` 切回九宫格 `application_card` |
| `series_seal_dim.png` | `160×160` | `Simple`（1:1 保持比例） | `0, 0, 0, 0` | 通用未点亮印章八角素石玉佩 |
| `series_seal_lit.png` | `160×160` | `Simple`（1:1 保持比例） | `0, 0, 0, 0` | 通用已点亮五瓣云星玉印 |
| `series_seal_lantern_courtyard_lit.png` | `160×160` | `Simple`（1:1 保持比例） | `0, 0, 0, 0` | 系列 1「留灯院旧友」专属点亮印章（纸灯笼＋竹叶） |
| `series_seal_elemental_blades_lit.png` | `160×160` | `Simple`（1:1 保持比例） | `0, 0, 0, 0` | 系列 2「水火冰锋」专属点亮印章（冰剑＋火纹＋水波） |
| `series_seal_lotus_melody_lit.png` | `160×160` | `Simple`（1:1 保持比例） | `0, 0, 0, 0` | 系列 3「清音济世」专属点亮印章（青莲＋七弦琴＋音纹） |

### 9.3 Unity 导入参数（所有 PNG 统一）

- `Texture Type`: `Sprite (2D and UI)`，`Sprite Mode`: `Single`，`Mesh Type`: `Full Rect`（`spriteMeshType: 0`），`Pixels Per Unit`: `100`
- `Alpha Is Transparency`: 勾选，`Generate Mip Maps`: 关闭，`Filter Mode`: `Bilinear`，`Compression`: `None`（与 `AppearanceChronicleV2` 一致）
