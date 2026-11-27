# 宠物培养界面画质修复与 1:1 零失真重构方案（V1 样板）

> **日期**：2026-11-27  
> **目标模块**：`Assets/Scripts/UI/Ugui/Pet/PetCultivationPanel*.cs`、`ProjectSettings/ProjectSettings.asset`、`Assets/Editor/QdaoUiTextureQuality.cs`、`Assets/Resources/UI/Ugui/CharacterPet/Controls/`、`Assets/Scripts/UI/Ugui/QdaoUguiTheme.cs`  
> **对齐基准**：帮会画风「宝宝培养」（`guild_empty_shell.png` 原生 `1932×814` 1:1 比例 + `SimKai` 高清楷体 + 帮会标准控件）  
> **双形态兼容要求**：同时满足微信/抖音小游戏（WebGL2 单线程、内置打包字体、右上角 176×72 胶囊避让、显存可控）与 iOS/Android App（IL2CPP）

---

## 1. 需求分析

负责人反馈两类核心上线阻塞问题：
1. **拼装形态变样**：`D:\work\image\designs` 中的效果图进入 Unity 游戏后走样变形，要求达到上线级**完全一致**（而非“尽量一致”）；
2. **Unity 内画质劣化**：相同图片放进 Unity 后发灰、偏暗、缩图锯齿发渣、文字发虚糊边。

负责人已拍板先以**帮会画风「宝宝培养」界面**作为首个全链路样板打通验证，确认三项前置决策：
- **颜色空间**：由 `Linear` 切回 `Gamma`；
- **对齐基准**：帮会画风「宝宝培养」（横排 4 页签 + 福字灯笼/玉兔/朱砂圆关闭钮完整底壳），同步修掉底壳拉伸变形、字溢框与对比度缺陷；
- **字体策略**：统一采用与预览图一致的 `SimKai.ttf` 升级高清 `TextMeshPro` 动态 Multi-Atlas SDF，彻底淘汰 Legacy `UI.Text` 与黑体混用（商用上线前由负责人确认中易楷体授权或统一切换为免费可商用 OFL 开源楷体并重新定稿）。

---

## 2. 漏洞与根因分析（宠物培养面板实测证据）

| 编号 | 缺陷类别 | 现状实锤 | 视觉后果 |
|---|---|---|---|
| **B1** | 颜色空间失配 | `ProjectSettings.asset` 为 `m_ActiveColorSpace: 1 (Linear)`，全仓 2D UI 与 Sprite 均按 sRGB 制作 | 半透明阴影、淡墨山水、流苏羽化、文字抗锯齿全屏偏浅发灰，与外部看图软件永远不一致 |
| **B2** | 切片整张漏透明 + 尘点残留 | `CharacterPet/Controls/` 中 13 张核心贴图（5 枚 `badge-*`、5 枚 `ring-*`、2 张 `portrait_*`、`close-jade`）99.9%~100% 实心像素 `Alpha = 250~254`（主值 253），且含数万 `Alpha = 1~2` 隐形脏尘点 | 叠在深色遮罩/场景上层层透底发暗发脏 |
| **B3** | 超大图零 Mipmap 27× 缩图走样 | `badge-*` / `ring-*` / `portrait_*` / `row_*` 共 18 张为 `1254×1254`，但全部 `enableMipMap: 0` + `Bilinear`，实际在界面中缩至 `46×46`~`98×98`（缩小 12~27 倍） | 违反奈奎斯特采样定理，GPU 丢弃 >99% 像素，图标与徽章满屏狗牙噪点 |
| **B4** | 主底壳强行非等比拉伸变形 | `guild_empty_shell.png` 原生 `1932×814`（可见区 `0..1915 × 0..814`），`PetCultivationPanel.cs` 强行拉成 `1900×986`（纵拉 +21.1%）或 `2416×986`（横拉 +25%） | 灯笼、玉兔、圆红关闭钮、顶部太极圆全部被拉成鸡蛋/椭圆，全图双线性重采样变糊 |
| **B5** | Legacy `UI.Text` 糊字与溢框 | `PetCultivationPanel` 全页使用过时 `UnityEngine.UI.Text`；条目方案按钮设置 `HorizontalWrapMode.Overflow` 放任文字戳穿边框；技能选中行浅绿底配浅白 `Cream` 字对比度极低 | 文字锯齿发糊、方案字挤爆按钮框、选中技能几乎看不清字 |
| **B6** | 缺弹窗遮罩与自动化 1:1 截图门禁 | 灵阶突破/用途切换弹窗用 `Color.clear` 无遮罩导致前后两层文字粘连；无 1920×1080 1:1 离线渲染自动截屏与布局/像素门禁 | 每次改布局只能盲拼，回归问题无人拦截 |

---

## 3. 思想分析（为何能做到「完全一致」）

商业游戏做到“游戏画面 == 效果图”的唯一工程铁律是 **单一事实源（Single Source of Truth）**：
1. **像素 1:1 零重采样原则**：`guild_empty_shell.png` 可见像素为 `1915×814`，在 `1920×1080` 与 `2560×1080` 屏下均可严格以 `scale = 1.000` 整数坐标居中呈现，彻底消除非整数缩放；
2. **渲染管线与美术软件同构原则**：切回 `Gamma` + 清洗实心区域 `Alpha = 255` + 对高倍缩图启用 Kaiser Mipmap + Trilinear，让 GPU 合成结果与专业图像处理软件在数学上一致；
3. **Unity 渲染即验收基准原则**：将真实业务面板在 `1920×1080` 基准分辨率下通过 Unity BatchMode 离屏渲染输出全状态样板图，负责人验收通过后固化为基线图，并由自动化逐像素 Diff 与布局单测锁死，杜绝二次走样。

---

## 4. 设计分析

### 4.1 颜色空间与贴图画质底座
1. **`ProjectSettings.asset`**：`m_ActiveColorSpace: 0`（Gamma）。同步核验 `BattleUiAdditive.shader` 与常用暗幕透明度在 Gamma 空间的表现。
2. **PNG 像素清洗（仅目标漏透明资产）**：
   - 目标：`CharacterPet/Controls/` 中 `badge-{normal,mutant,spirit,divine,sacred}.png`、`ring-{normal,mutant,spirit,divine,sacred}.png`、`portrait_lingyue.png`、`portrait_yunjiujiu.png`、`close-jade.png`（严格跳过故意半透明灰态的 `step-*-disabled.png`）；
   - 规则：`alpha <= 3` 清为 `(0,0,0,0)`，`alpha >= 248` 锁死为 `255`，`4 <= alpha <= 247` 完整保留原始羽化抗锯齿；移除多余 `gAMA` chunk。
3. **高倍缩图 Kaiser Mipmap 策略（`QdaoUiTextureQuality.cs`）**：
   - 保持 1:1 底板/按钮/框体 `mipmapEnabled = false` 不变，不触发既有战斗/角色/场景 Mipmap 单测；
   - 对 `Assets/Resources/UI/Ugui/CharacterPet/Controls/` 中 18 张 `1254×1254` 高倍缩图（`badge-*` / `ring-*` / `portrait_*` / `row_*`）启用 `mipmapEnabled = true`、`mipmapFilter = KaiserFilter`、`filterMode = Trilinear`、`anisoLevel = 4`、`mipMapBias = -0.35f`。

### 4.2 字体与 TMP 渲染升级
1. `QdaoUguiTheme.ResolveFont()` 加载 `SimKai SDF` 后显式开启 `isMultiAtlasTexturesEnabled = true`，防止动态中文图集爆满丢字。
2. `PetCultivationPanel` 全部文字由 `UnityEngine.UI.Text` 升级为 `TextMeshProUGUI`（统一 `SimKai SDF`，`richText = false`）。
3. 为深玉绿标题匾与绿底主按钮/选中页签提供共享缓存的 TMP 柔和暗墨投影材质预设（`Underlay`），不污染正文默认材质，在销毁/重载时安全清理。

### 4.3 `PetCultivationPanel` 1:1 原生坐标系重构（基准 `1932×814`）
- **根容器**：`_root` 尺寸保持 `logicalWidth × 1080`（1920~2560），全屏柔和深玉暗幕 `Color(0.02f, 0.065f, 0.05f, 0.62f)`；
- **1:1 居中底壳 `ShellRoot`（`1932×814`）**：
  - `shellX = Mathf.Round((_width - 1915f) * 0.5f)`（1920 屏下为 `2px`，可见区 `[2..1917]` 零裁剪；2560 屏下为 `322px`）；
  - `shellY = 133f`（`(1080 - 814) / 2 = 133`，上方留足 133px 完整避让微信/抖音小游戏右上角 `176×72` 胶囊区）；
  - `guild_empty_shell` 按 `1932×814` 绝对 1:1 绘制，`localScale = Vector3.one`，零拉伸、零畸变。
- **内部严格对齐原图金线安全区**：
  - 标题匾「宝宝培养」：`(786, 24, 360, 58)`，40px 暖白楷书带微投影；
  - 右上关闭热区：`(1780, 22, 88, 84)`，精准重合壳体自带红圆关闭钮；
  - 顶排 4 页签（属性 / 加点 / 技能 / 条目点）：`y = 87, h = 62`（1:1 匹配 `guild_tab_normal.png` 原生高度），宽 `264px`、间距 `12px`，起点 `x = 188`；
  - 左栏「我的宝宝」（`x = 184..726`, `y = 176..756`）：列表高 `472px`（单行 `112px`，状态严格保留 18px 右内距），底按钮 `y = 702, h = 54`；
  - 右栏内容区（`x = 788..1750`, `y = 176..756`）：
    - **条目点页**：3 方案按钮宽 `254px`（带 `enableAutoSizing` 与左右内边距，严禁 `Overflow` 溢框）+ 用途钮 `170px`；4 分类钮 `230px`；6 条目每行 `52px`（一屏完整呈现 6 行，效果与详情左右齐平）；底部提示 + `一键清零 / 保存分配 / 恢复当前分配` 齐平于 `y = 702, h = 54`；
    - **属性页 / 加点页 / 技能页**：均按 `y = 176..756` 等距排布，底栏按钮统一齐平 `y = 702, h = 54`；技能选中行修正为深玉绿/墨色高对比文字；
    - **五阶徽章弹窗**：同以 1:1 `guild_empty_shell (1932×814)` + 暗幕呈现，5 列对称无变形；
    - **灵阶突破 / 条目详情 / 用途弹窗**：补齐柔和全屏暗幕遮罩与 `guild_close` 实体关闭钮。

---

## 5. 步骤分析与执行步骤

1. **步骤 1（颜色空间与贴图画质）**：修改 `ProjectSettings.asset` 切回 Gamma；脚本清洗 `CharacterPet/Controls/` 13 张漏透明 PNG；升级 `QdaoUiTextureQuality.cs` 及 18 张 `1254×1254` 贴图 `.meta` 为 Kaiser Mipmap + Trilinear；在 `QdaoUguiTheme.cs` 启用 `SimKai SDF` Multi-Atlas。
2. **步骤 2（宠物培养面板重构）**：改造 `PetCultivationPanel.cs`、`PetCultivationPanel.Entries.cs`、`PetCultivationPanel.Skills.cs`、`PetCultivationPanel.Badges.cs`，全量切换高清 `TextMeshProUGUI`、1:1 原生底壳坐标与防溢框排版；保留所有原有按钮/文本名称与权威业务协议交互不变。
3. **步骤 3（单测与自动化截屏门禁）**：更新 `PetCultivationInteractionTests.cs` 适配 `TMP_Text` 并新增 1:1 比例、零 Legacy Text、零溢框、贴图画质断言；新增 `Assets/Editor/PetCultivationFidelityCapture.cs` 离屏渲染 6 个核心状态的 1920×1080 1:1 截图并输出布局自检与像素报告。
4. **步骤 4（编译、测试与真机渲染验收）**：
   - 运行 `tools/client_compile_check.ps1`；
   - 运行 Unity BatchMode 重导资源、执行 EditMode 单测与 1920×1080 1:1 真实渲染截屏；
   - 逐张肉眼复核全部 6 张真实渲染截图，确认零变形、零发渣、零溢框、字体与帮会标准图一致后交付验收。

---

## 6. 风险与后续推广约束

- **切 Gamma 的全局回归检查**：切回 Gamma 后需在下轮实机游玩中顺便复核战斗受击闪白（`BattleUiAdditive.shader`）与大世界 2D 地图亮度；
- **商用字体合规**：上线公开发行前须确认 `SimKai.ttf` 已取得合法游戏嵌入商用授权，或统一替换为免费可商用 OFL 开源楷体并一键刷新基线截图；
- **不混改未提交工作**：严禁触碰当前工作区已有的 `BattleSpectateChat*` 等未提交文件。
