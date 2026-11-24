# 客户端 UI 去底透明美术资源接入记录（2026-11-23）

日期：2026-11-23。依据 `D:/work/image/designs/background-removal-20261123/current-manifest.json`（已通过 `verify-delivery.py --require-complete`：`passed`、`accepted=118`、`pending=0`）与 `D:/work/image/designs/ui-art-delivery-20261123/verification.json`，将全部已完成主体外去底的正式 RGBA 透明 UI 运行底板与独立控件字节原样接入客户端正式运行路径，并补齐因底板去底（清除旧烘焙文字）所需的最小原生文案与测试更新。

完整逐文件哈希、尺寸、GUID、Alpha 与映射清单见机读收据：[resource-mapping.json](../VerificationEvidence/ui-background-removal-integration-20261123/resource-mapping.json)。

## 1. 需求分析

- **接入范围**：将美术仓库 `118/118` 清单中对应客户端正式运行资源的 `80` 张透明 PNG（`11` 张运行底板 + `69` 个独立控件）逐字节替换为透明成品；核验易容与装备 `50` 张运行资源及 `14` 处九宫格 `spriteBorder` 与交付清单一致；复用易容 `4` 张与装备 `11` 张透明整屏预览作为对照，不以整屏效果图覆盖运行界面。
- **硬性约束**：
  - 不修改、不重编码任何美术 PNG 像素；保留原有尺寸、比例、锚点与全部 `.meta` 文件和 GUID。
  - 不触碰 `D:/work/image` 的共享 Git HEAD / index / refs，不接管美术仓库推送。
  - 保留本仓库内其他作者正在进行的未提交改动（`AppearanceBiography` / `AppearanceClient` / `AppearanceUiArt` / `AppearanceUiRoot*` / `AppearanceChronicleV2` / `Assets/StreamingAssets/Tables/*.pb.meta` 等）。
  - 保留真实业务交互与网络接线；邮件在无正式邮件 RPC 时保持真实“邮件暂未开放”态，不伪造服务器收发成功。
  - 保留地图 HOLD、未验收技能图标候选及其他作者在制稿状态，不误接入正式资源。

## 2. 设计分析与资源映射

客户端无 `.spriteatlas` 资源，全部原生 uGUI 通过 `Resources.Load<Sprite>("UI/Ugui/...")` 直接加载切片。对 `118` 项去底成品与 `65` 项易容/装备交付清单建立一一映射：

| 客户端资源目录 | 替换/核验数量 | 美术来源目录（`D:/work/image/`） | 使用界面与入口 | 备注 |
| --- | ---: | --- | --- | --- |
| `Assets/Resources/UI/Ugui/GameplayFidelity20261016/` | 26 张替换（3 底板 + 23 控件） | `designs/gameplay-ui/fidelity-20261016/{staged,visual-fixes-20261017/staged,final-polish-20261017/staged}/...` | `GameplayWindow`（任务 / 背包 / 活动） | 按 `source_sha256` 精确匹配最新版本，旧 `staged` 中已被覆盖的 6 份早期副本不误用 |
| `Assets/Resources/UI/Ugui/GuildFidelity20261016/` | 9 张替换（1 底板 + 8 控件） | `designs/guild-ui-v2/fidelity-20261016/staged/...` | `GuildWindow`（帮会） | `GuildWindow` 主框由不透明 `GuildExact20261017/guild-skin` 切换为透明 `guild_empty_shell`，徽记由 `GuildExact20261017/crest` 切换为透明 `guild_crest` |
| `Assets/Resources/UI/Ugui/JubaozhaiFidelity20261016/` | 10 张替换（1 底板 + 9 控件） | `designs/jubaozhai-ui-v1/fidelity-20261016/staged/...` | `JubaozhaiWindow`（聚宝斋） | 保持既有加载入口，翻页箭头等边缘更平滑 |
| `Assets/Resources/UI/Ugui/MailV1Fidelity/` | 16 张替换（1 底板 + 15 控件） | `designs/mail-ui-v1/fidelity-20261016/staged/...` | `MailWindow`（邮件） | 去底后 `mail_empty_frame` 标题牌为空底，恢复 `MailTitle="仙  笺"` / `MailSubtitle="邮件"` 原生 TMP 渲染 |
| `Assets/Resources/UI/Ugui/SceneSelectV1/Fidelity20261016/` | 5 张替换（1 底板 + 4 控件） | `designs/scene-select-ui-v1/fidelity-20261016/staged/...` | `SceneChannelWindow`（选线） | 去底后 `scene_skin` 底栏按钮为空底，`CloseButton` / `ConfirmButton` 改用透明 `button_secondary`（“取消”）与 `button_primary`（“确认”）；新 `row_selected` 同时消除旧资源残留裁切字迹 |
| `Assets/Resources/UI/Ugui/SocialV1/Fidelity20261016/` | 12 张替换（3 底板 + 9 控件） | `designs/social-ui-v1/fidelity-20261016/staged/...` | `SocialWindow`（社交：世界 / 传闻 / 群组） | 保持既有加载入口 |
| `Assets/Resources/UI/Ugui/TeamV2/Fidelity20261016/` | 1 张替换（1 底板） | `designs/team-ui-v2/fidelity-20261016/staged/...` | `TeamWindow`（组队） | `TeamWindow` 主框由不透明 `TeamExact20261017/team-skin` 切换为透明 `team_skin`，摘要与空余席位改由原生 TMP 渲染 |
| `Assets/Resources/UI/Ugui/WorldMapOriginal/Fidelity20261016/` | 1 张替换（1 控件） | `designs/world-map-original-20261013/fidelity-20261016/staged/...` | `CityTravelWindow`（主城传送） | 保持既有加载入口 |
| `Assets/Resources/UI/Ugui/AppearanceV1/`、`Icons/item_appearance_card.png`、`WorldHud20261022/appearance.png`、`Equipment20261117/{Controls,Icons}/` | 50 张已核验字节一致 | `designs/appearance-ui-v1/`、`designs/equipment-ui-20261117/` | `AppearanceUiRoot`、`EquipmentWindow` | 已与交付成品完全一致，14 处 `CONTROL-USAGE.md` 九宫格边框全部吻合 |
| `Assets/Resources/UI/Ugui/AttributesPaintedV2/` | 30/31 张已含真实透明 | `designs/attribute-panels/v2-painted/` | `AttributeFidelityLayout`、`PetPanel` | 仅 `paper_tile.png` 为平铺纸面底纹；清单中 2 张整屏效果图仅作视觉对照 |

清单其余 `38/118` 项均已登记在 `resource-mapping.json` 中：`20` 张整屏参考效果图（不覆盖运行界面）、`12` 张美术过程母图（`art/` 与 `assets/`）、`6` 份被后续 `visual-fixes` / `final-polish` 替代的早期 `staged` 副本。

## 3. 架构与边缘情况分析

1. **去底底板与旧烘焙文案的兼容**：
   - `mail_empty_frame.png`、`scene_skin.png`、`team_skin.png` 在去底成品中清理了旧版不透明底板上烘焙的固定文字；若不恢复原生 TMP 渲染，运行界面将出现空标题牌、空底部按钮或摘要信息缺失。
   - 修改严格限制在 4 个既有窗口类中，不新增抽象层或重复入口：
     - [MailWindow.cs](../../Assets/Scripts/UI/Ugui/Mail/MailWindow.cs)：在 `MailTitle` / `MailSubtitle` 恢复 `"仙  笺"` / `"邮件"`。
     - [SceneChannelWindow.cs](../../Assets/Scripts/UI/Ugui/Gameplay/SceneChannelWindow.cs)：底栏 `CloseButtonName` / `ConfirmButtonName` 改用 `FidelityButton` 绑定透明 `button_secondary`（`"取消"`）与 `button_primary`（`"确认"`），保留禁用态暗化反馈与点击二次校验。
     - [GuildWindow.cs](../../Assets/Scripts/UI/Ugui/Guild/GuildWindow.cs)：主框切换为 `GuildFidelity20261016/guild_empty_shell`（`GuildShell`），徽记切换为 `GuildFidelity20261016/guild_crest`（`207×188`）。
     - [TeamWindow.cs](../../Assets/Scripts/UI/Ugui/Team/TeamWindow.cs)：主框切换为 `TeamV2/Fidelity20261016/team_skin`（`TeamSkin`），开启 `_summary`（自适应字号 14–20）与 `_memberCount`（`"空余 N 席"`），关闭 `_summaryFallbackPlate` 并清空原重叠数字标签，避免重影。
2. **`.meta` 与 GUID 保护**：全部 80 张替换 PNG 的 `.meta` 文件零改动；已逐个核验 `guid`、`textureType: 8`、`alphaIsTransparency: 1`、`alphaUsage: 1`、`enableMipMap: 0` 全部完好。
3. **WebGL2 / App 跨端安全**：4 处 C# 改动仅涉及既有 uGUI `Image` / `Button` / `TMP_Text` 属性，未引入线程、阻塞 I/O、原生插件或动态反射，完全兼容 WebGL2 与 IL2CPP。
4. **UPM 依赖核查**：`Packages/manifest.json` 中旧的 `file:../tools/unity-packages/com.unity.ai.assistant-2.17.0-pre.1.tgz` 引用已由历史提交 `1bd0778a` 移除；当前仅保留 `file:../tools/unity-packages/com.unity.ai.navigation-2.0.16.tgz`（文件实存 `8,891,255` 字节），无缺失本地依赖阻塞。

## 4. 风险与安全分析

- **多会话并行修改风险**：工作区存在其他作者的未提交改动（易容人物志、`Assets/StreamingAssets/Tables/*.pb.meta`、`CoreFidelity20261016/portraits/04_mountain_guardian_boy.png*`），提交时必须按文件精确暂存，严禁 `git add -A`。
- **已知视觉差异 / 未解决项**：
  1. `GuildWindow` 顶部“帮 会”手写书法仍复用原不透明贴片 `GuildExact20261017/title.png`（`183×55`）叠加在透明 `guild_empty_shell` 标题牌上，局部放大可见约 15 RGB 的细微边缘色差；因本次禁止改动任何图片且该书法无独立去底切片，保留原贴片并如实记录。
  2. `MailWindow` 底板尺寸为 `1932×814`，而参考效果图 `01-mail-event.png` / `02-mail-system.png` 为 `1931×814`（客户端既有 `MailUiVerification.cs` 已标注该 1px 差异）。
  3. 本机 Unity 6000.6.0f1 缺少有效编辑器授权（`Unity.Licensing.Client.exe --showEntitlements` 返回 `No licenses were found.`，`-batchmode` 退出码 `1`，交互式日志报错 `No valid Unity Editor license found`），且系统未安装 PowerShell 7（`pwsh`）。因此**本机未能完成 Unity 资源导入刷新、EditMode/PlayMode 运行、Windows 播放器构建（`Builds/windows-x64-*/mmorpg.exe`）与实机整屏原尺寸逐像素零差异验收**；当前仅声明离线三程序集零错误编译、字节/GUID/Alpha 校验与静态四背景透明边缘检查通过，不虚报实机零像素差异。

## 5. 执行步骤与验证结果

1. 运行 `D:/work/image` 官方验收脚本：`verify-delivery.py --require-complete` → `status: passed`，`targets: 118`，`accepted: 118`，`pending: 0`。
2. 拉全稀疏检出中的 UI 资源与 `Assets/Editor/` 目录；逐字节替换 `80` 张去底 PNG 并生成 `resource-mapping.json`。
3. 生成并人工目视核查黑 / 白 / 灰 / 实机背景（`sanctuary_background.png`）四套透明边缘拼图（位于 `.codex-artifacts/ui-background-removal-20261123/`）：全部 11 张底板与 69 个控件边缘清晰，无白边、黑边、棋盘格或旧场景残留，所有 `alpha == 0` 像素隐藏 RGB 均为 `0`。
4. 更新 4 个运行时窗口脚本及对应 4 个 EditMode 测试文件（[GuildUiTests.cs](../../Assets/Tests/EditMode/Guild/GuildUiTests.cs)、[MailWindowTests.cs](../../Assets/Tests/EditMode/Mail/MailWindowTests.cs)、[TeamWindowTests.cs](../../Assets/Tests/EditMode/Tianyong/TeamWindowTests.cs)、[SceneChannelWindowTests.cs](../../Assets/Tests/EditMode/Tianyong/SceneChannelWindowTests.cs)）。
5. 自动化校验结果：
   - Python 自检（80 张 PNG 字节哈希与 `current-manifest.json` 一致、118 项全覆盖、8 目标目录 `.meta` 变更数为 0、易容/装备 50 张运行图一致、`D:/work/image` 成品零漂移）：`passed: true`（退出码 `0`）。
   - `git diff --check`（8 个改动 C# 文件）：退出码 `0`。
   - 离线 Roslyn 编译（基于 Unity `6000.6.0f1` 管理与模板程序集）：
     - Runtime（`Assets/Scripts`）：`584` 文件，`0` 错误。
     - Editor（`Assets/Editor`）：`84` 文件，`0` 错误。
     - EditMode Tests（`Assets/Tests/EditMode`）：`177` 文件，`0` 错误。
   - Unity batchmode 授权探针（`2026-11-23T15:38:56Z`）：确认 `Found 0 entitlement groups and 0 free entitlements`，退出码 `1`，实机构建与截屏验收阻塞。

## 6. 2026-11-24 Unity 实跑、出包与视觉复核补充

本机 Unity `6000.6.0f1` Personal 授权激活后，已补齐稀疏检出剩余运行期 UI 目录并完成以下真实引擎验收：

1. **实机渲染发现并修复 1 处残余不透明贴片缺陷**：
   - 渲染 `SocialWindow` 时发现顶部仍由 `SocialWindow.Fidelity.cs` 叠加旧不透明蓝天裁切图 `SocialV1/Fidelity20261016/exact_title.png`（400×88），导致“仙友”标题牌外框露出蓝天矩形。
   - 已改为直接在透明皮肤自带的太极标题牌上渲染原生中文 `SocialTitle="仙  友"`，并同步更新 [SocialFidelityVerification.cs](../../Assets/Editor/Social/SocialFidelityVerification.cs) 与 [SocialTests.cs](../../Assets/Tests/EditMode/Social/SocialTests.cs) 回归断言；重截世界/群组/谣言三页确认蓝天矩形完全消除。
   - 同时为 [GameplayUiVerification.cs](../../Assets/Editor/GameplayUiVerification.cs)、[JubaozhaiUiVerification.cs](../../Assets/Editor/Jubaozhai/JubaozhaiUiVerification.cs)、[CityTravelUiVerification.cs](../../Assets/Editor/CityTravelUiVerification.cs) 补齐批处理打开 `Bootstrap.unity` 保护，避免无标题场景叠加报错。
2. **Unity EditMode 实跑结果**：
   - 全量 UI 过滤套件：`233` 项运行、`222` 项通过（`MailWindowTests`、`MailUiStateTests`、`GuildUiTests`、`SceneChannelWindowTests`、`GameplayWindowTests`、`JubaozhaiFidelityWindowTests`、`SocialTests`、`CityTravelWindowTests`、`AttributePanelDisplayTests`、`PetPanelInteractionTests`、`EquipmentScreenModelTests`、`AppearanceClientTests` 及 `TeamWindowTests.TransparentTeamSkin_RendersNativeSummaryAndVacancy` 全绿）；`SocialTests` 修复后单独复跑 `85/85` 全绿。
   - 仅存 `11` 项 `TeamWindowTests` 头像断言失败，经溯源为既有提交 `37c0b9d2` 删除退场角色资产时移除了 `24_lu_dongbin` / `27_ink_kite_ranger` 在 `QdaoCharacterCatalog.ResolveVisualId` 中的映射所致，与本次 UI 资源接入无关。
3. **正式 Windows x64 播放器构建与启动冒烟**（基线提交 `43692363`，早于上述 `SocialWindow.Fidelity.cs` 一行标题修复）：
   - 按 `tools/build_client.ps1` 等价参数调用 `MmorpgClient.Editor.ClientPlayerBuild.Build` 构建正式包（场景 `Assets/Scenes/Bootstrap.unity`、`Assets/Scenes/World/TianyongSandbox.unity`），退出码 `0`、`errors: 0`、`result: Succeeded`，产物 `Builds/windows-x64-release-20261124-040653-659-29480/mmorpg.exe`（SHA-256 `a4c71b1bfc3e42d02f2f129fe04adef3ebf2343a662884e4b99598e9dfc90e7d`），日志 `Logs/build-client-20261124-040653-659-29480.log`。
   - 启动 `mmorpg.exe`（1280×720 窗口化，Direct3D 11），`AppBootstrap` 原生 uGUI 正常初始化并持续存活，零 C# 异常（仅预期的本地登录服 `127.0.0.1:8081` 未启动连接失败及稀疏检出未含 `StreamingAssets/Tables` 提示）。
4. **全界面 Unity 截图目视验收与严格零差异逐像素比对**：
   - 已在 Unity 中完成邮件（`1931×814`）、组队（`1931×814`）、社交（群组/世界/谣言修复后）、帮会（26 屏 `missingGlyphLabels: 0`）、选线、背包、任务、活动、聚宝斋（20 屏交互校验通过）、主城传送全部 10 类界面截图并逐屏目视检查，透明外框、圆角装饰、中文文案与按钮状态均清晰无露底。
   - 严格原尺寸整屏逐像素比对（无缩放、无对齐、无裁剪、无容差）：
     - `designs/mail-ui-v1/01-mail-event.png` vs `01-activity-mail_1931x814.png`：`zero_diff_parity=false`（整帧差异像素 `99.71%`，不透明前景均值差 `22.16`）
     - `designs/mail-ui-v1/02-mail-system.png` vs `02-plain-pending_1931x814.png`：`zero_diff_parity=false`（整帧差异像素 `98.07%`，不透明前景均值差 `20.08`）
     - `designs/team-ui-v2/team-ui-v2.png` vs `01-approved-preview_1931x814.png`：`zero_diff_parity=false`（整帧差异像素 `86.72%`，不透明前景均值差 `9.32`）
   - 差异原因属实记录：运行界面在透明底板外渲染真实游戏底图与离线预览角标、使用运行期 TMP 字体而非手绘效果图字形、包含正式业务新增交互按钮（如组队底栏“开始 PVE / 邀请道友 / 解散队伍 / 离开队伍”）、且邮件底板与参考图存在既有 `1932×814` vs `1931×814` 宽差，因此不声明零像素一致。

## 7. 后续构建与复核命令

```powershell
# 如需在包含 SocialWindow 标题修复的最新提交上重新出 Windows 正式包（需安装 PowerShell 7 或直接调用 Unity -executeMethod）：
pwsh -File tools/build_client.ps1 -UnityExe "D:\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
```
