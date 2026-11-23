# 易容与人物志：组队同源 UI 接入交接

日期：2026-11-23。当前客户端基线来自 `luhailong-cpp/mmorpg-client`；本轮基于主树已有正式 `Gameplay/AppearanceUiRoot`、`AppearanceClient`、生成协议和配表接续。仅修改客户端界面、只读人物文案、会话隔离和对应测试；未修改服务端、协议、价格或奖励规则，未提交或推送。

用户已明确在另一台电脑进行 Unity 接入。本机不启动 Unity，不把浏览器图、离线编译或资源清单当作游戏内验收。

## 正式入口与运行数据

唯一易容入口仍为 `AppearanceUiRoot`：世界 HUD 注册的“易容形象”及人物面板已有入口打开同一窗口。它沿用现有 `GameClientBattleTransport` 和 `AppearanceClient`，从服务端 `PlayerAppearanceInfo` 读取目录、原始 `appearance_id`、永久形象、当前生效形象、试用、卡数量和商城参数。服务端返回的清单及顺序保留，不创建第二套人物系统或永久拥有数据库。

现有 `GameClient.Appearance.cs` / `SceneAppearanceClientPlayerRpc` 继续承担正式外观通知；本次未改写生成的消息号、RPC、protobuf、表 schema 或存档。代码中的候选姓名和故事只用于人物档案，不覆盖玩家昵称，也不添加职业锁、战力、属性星级或武器专属技能。

四位档案仅按以下原始稳定 ID 精确关联：

| 原始 appearance_id | 候选人物 | 一段往事 |
| --- | --- | --- |
| `00_reference_topright_boy` | 陆照禾（阿禾） | 第四只碗 |
| `10_crimson_spear_girl` | 祝停云 | 纸也赢过一回 |
| `17_ghost_script_calligrapher_boy` | 闻砚生 | 借还栏 |
| `09_bamboo_archer_girl` | 桑听雨 | 没有画的那一页 |

完整短档案与各段故事保存在 `AppearanceBiography.cs`，可独立替换。`ResolveVisualId` 仅允许用于历史图像替代，不能让别名获得替代人物的传记。没有档案的条目保留原有功能、显示“暂无人物志”、收起人物志入口；形象缺失仍显示本人姓名并可阅读。

## 已接入页面与交互

易容列表、选中详情、试用态、禁用操作、扣卡确认、形象卡不足、人物小传、故事和大图预览共用 `AppearanceUiArt` 的同源皮肤。真实姓名、状态、正文、价格和倒计时仍由 TMP 控件绘制。窗口外为真实透明；透明的全屏射线层仍拦截背后输入。

从“读人物志”进入人物小传，再点击篇名阅读故事。正文支持段落和滚动；普通返回逐层返回，跳过和读完回详情；点击立绘或“查看形象”打开大图，并返回刚才的层级。选中项和列表位置保留，同人的故事阅读位置保留；换人重置篇章，关闭重开回列表/详情，退出角色清除本次阅读状态。

进入小传、故事或预览会清除未提交的扣卡确认。“确认易容”中也提供“先读人物志”，返回后必须重新点击原切换入口并再次确认。已提交的请求继续归原客户端管理。阅读和预览本身不购买、不扣卡、不申请试用、不切换、不发奖、不改属性。

所有模态层隔离背层 CanvasGroup、指针和键盘确认焦点，导航使用当前模态控件范围；窗口更新在 EventSystem 前处理焦点。原试用/取消/切换 handler 保留模态检查，商城只接受有效的原快照参数。价格未同步时不补出一个新价格。角色或连接身份变化、断线和关闭都释放阅读资源并清理旧 UI 会话；`OnDisable` 对称解绑，重新启用后由既有绑定流程接回。

`AppearanceClient` 新增回调前对连接身份与完整 `ulong PlayerId` 的核对，避免换角色后、下一次 Tick 前旧回包填入新角色。断线后的迟到推送也不能恢复旧快照。原重试次数、请求幂等键和资产处理流程保持原实现。

## 尺寸、滚动与素材

常规横屏沿正式窗口 2160×1000 设计空间和既有消费区；主验收目标包含 2560×1080、1920×1080。小屏阈值为视口宽小于 1200 或高小于 600，改用 1180×650 的实际重排：先列表，再单页详情；底部消费按钮高 76，距框底留独立空间。摘要是独立可滚动文字区，不裁掉原文；正常帧不会把滚动位置拉回顶部。小传、故事和大图弹层另排为 1140×620，故事依据当前 viewport 宽高计算文本，不沿用宽屏定值。

896×414 下，扣除通用边距后的 76 逻辑高按钮约为 44 屏幕像素，30 号阅读正文约为 17 像素；这是布局数学值，仍需在目标 Unity Game View 和真机安全区、胶囊条件下核实。当前安全区组件保留刘海、手势区及小游戏胶囊避让，不引入线程、阻塞等待、新网络连接或大型阅读依赖。

皮肤资源位于 `Assets/Resources/UI/Ugui/AppearanceChronicleV2/`，主框、完整云纹标题牌、纸面列表/卡片、按钮、选中描边和关闭控件承接已确认组队图。原固定立绘台、形象卡槽和形象卡图标以本机实际源另存到同一新资源目录，原生成来源不改写；不存在的旧 Mail/Appearance/Icons 目录不从 Git 恢复。详细 PNG、Alpha、尺寸、哈希和 `.meta` 清单见本轮包清单与美术交付记录。

角色图像通过现有 `QdaoCharacterCatalog.AcquireCreationPortrait` 轻量入口持有租约；头像、详情、确认和大图共用同 ID 已加载的图像，翻页不加载整套动作。换窗、换角色和断线对称 Dispose。借入的验收 Sprite 不由界面擅自销毁。

运行依赖包需要现有完整角色交付：`appearance.json`、`manifest.json`、`validation.json` 与相应原图/动作库存证明不能只带 `portrait.png`。本轮各角色真实依赖及完整 137 PNG 库存由角色依赖包清单锁定；V13 的运行证明按现有 Unity Builder 在目标机核实原图后生成，不伪造验收结论。

## 本次源码与数据

| 路径 | 作用 |
| --- | --- |
| `Assets/Scripts/UI/Ugui/Gameplay/AppearanceUiRoot.cs` | 唯一正式入口、现有各页接线、焦点与资产操作边界 |
| `Assets/Scripts/UI/Ugui/Gameplay/AppearanceUiRoot.Reading.cs` + `.meta` | 小传、故事衔接、大图、共享立绘租约 |
| `Assets/Scripts/UI/Ugui/Gameplay/AppearanceUiRoot.Layout.cs` + `.meta` | 宽屏/窄横屏重排、可滚动摘要及视口适配 |
| `Assets/Scripts/UI/Ugui/Gameplay/AppearanceUiArt.cs` | 全部页面的组队同源皮肤与控件装配 |
| `Assets/Scripts/Game/Appearance/AppearanceBiography.cs` | 四份候选文案、原始 ID 映射、页面与阅读位置状态 |
| `Assets/Scripts/Game/Appearance/AppearanceClient.cs` | 保留原通信业务，补连接/角色代次与迟到回调隔离 |
| `Assets/Tests/EditMode/Role/AppearanceClientTests.cs` | 相应客户端回调生命周期回归 |
| `tools/tests/appearance_chronicle/AppearanceChronicle.proj`、`Program.cs` | 链接当前真实模型、客户端和生成类型的离线检查 |
| `Assets/Resources/UI/Ugui/AppearanceChronicleV2/` | 本次正式 UI 资源及每件 `.meta` |

既有 `GameClient.Appearance.cs`、`Net/Generated/Rpc/SceneAppearanceClientPlayerRpc.cs`、`Proto/Generated/PlayerAppearance.cs`、`AppearanceErrorTip.cs`、`Table/Generated/*Appearance*`、`MessageIds.cs` 属完整客户端基线依赖，不手改或从旧临时项目搬回。本轮原本缺少的 `Assets/StreamingAssets/Tables`，通过既有 `sync_tables.ps1` 从当前权威服务器 `generated/client_tables` 的 58 份已裁剪产物补齐；来源、目标哈希及数据 `.meta` 以终版清单为准，不读取未裁剪的服务端表。

`AGENTS.md` 引用的 `Docs/rules/` 分章在本机缺失，本次未从旧 Git 或快照恢复。当前根 AGENTS 已列明双平台、生成边界和生命周期要求，继续执行。

## 另一台电脑接入

1. 在完整 `mmorpg-client` checkout 上应用本轮功能文件和依赖包，保留实际路径与所有 `.meta`；按包清单校验 SHA-256。功能包不是整个 Unity 工程，不能代替基线。
2. 使用 `ProjectSettings/ProjectVersion.txt` 指定的 Unity **6000.6.0f1**。当前包清单包含 uGUI 2.6.0、Purchasing 5.0.4、Test Framework 1.8.0 和 AI Navigation 2.0.16 的既有本地包引用，按实际 `Packages/manifest.json`、锁文件和 `tools/unity-packages` 基线解析，不安装本轮不需要的 AI Assistant 包。
3. 核查全部 UI 资源、11 个当前形象的完整角色依赖、客户端表、protobuf、RPC、表管理器和消息号属于目标服务端的同一批契约；图片文件不能是 LFS 指针。
4. 打开当前正式 Bootstrap/游戏场景，沿已有登录、选角和世界流程进入“易容形象”。服务端未提供有效清单时显示加载/错误及只读重试；不注入演示玩家或伪造永久/试用快照。
5. 若目标基线确需重生客户端产物，使用当前权威服务端已生成的输出执行下述正式工具；不要为了 UI 导入去修改协议、猜消息号、恢复旧临时项目或运行服务端生成。

```powershell
# 在客户端仓库根运行；参数使用目标机真实的同级权威服务端路径。
pwsh -File tools/sync_tables.ps1 -ServerRoot <权威服务端仓库>
pwsh -File tools/gen_proto.ps1 -ProtoRoot <权威服务端仓库> -Protoc <现有protoc.exe>
pwsh -File tools/gen_messageids.ps1 -ProtoRoot <权威服务端仓库>
pwsh -File tools/client_compile_check.ps1 -UnityRoot <Unity安装根目录> -ArtifactDir .codex-artifacts/appearance-ui-check
```

没有同批权威输出时先核清基线，不用缺失文件或旧版本数据生成空契约。本机本轮不重生协议或改动服务端。

## 验证与目标机验收

最终完整运行时离线编译、当前模型/真实客户端检查及受影响 Role 测试程序集的证据统一收在 [本轮验证目录](VerificationEvidence/appearance-chronicle-team-20261123/)。本机编译引用使用实际 Unity、uGUI、TMP 和 AIModule DLL，保留原始响应文件、警告、结果和源码清单；它们不代表 Unity 已运行。

目标机逐项点击核查四位候选姓名、立绘、小传、篇名和段落，再核查无档案条目、缺图、加载失败/重试、空列表、永久与试用为不同人、快切、断线、关闭重开。完整走通“人物志→故事→滚动→返回/跳过→大图→返回”，确认选择和列表位置不丢失；先唤起扣卡确认，再“先读人物志”，返回后必须重新确认。对只读路径记录调用，购买、扣卡、试用、切换和发奖请求应为零。最后在 2560×1080、1920×1080、896×414 以及实际小游戏/App 安全区检查点击范围与文字。

本机未执行 Unity 编辑器渲染/点击、PlayMode、客户端出包、真实服务器资产回归或微信/抖音/iOS/Android 真机测试；这些由目标机接入流程验收。浏览器同源预览与素材截图仅供美术和布局核对，不作为正式客户端运行证据。
