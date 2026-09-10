# 五行奇谈客户端 UI 接入 · 2026-09-09

本轮将 `E:/work/image` 已确认的效果图和独立素材组装到 Unity 客户端。登录、选服、选角、主城 HUD、人物属性及宝宝属性使用原生 uGUI 控件；动态名称、数值、输入和按钮事件继续连接现有客户端数据与协议。

本文记录实现范围、复核入口及实际验证结果；服务端全量构建与真实联网验收单独列明。

## 已完成的界面

| 页面 | 本轮实现 |
|---|---|
| 登录 | 使用仙境净背景、独立标题、道童和灵狐组装页面；保留选择服务器、进入游戏及现有账号操作流程。 |
| 选服 | 用无字框板、分类、搜索和服务器卡片重排界面；服名与状态由原生文字显示，选择和继续操作沿用现有逻辑。 |
| 选角 | 左侧角色槽位、中间独立角色/灵宠、右侧角色信息和进入入口按效果图重排；保留现有选角、创建角色及进入流程。 |
| 主城 HUD | 在天墉城游戏场景上统一战斗、观战、角色、宝宝入口的皮肤和排列；保留现有入口绑定。 |
| 人物属性 | 按最终 `01-character-ui-no-affinity.png` 组装宽纸窗、方案选择、六项属性、属性点、四行滑条、自动/重置/确认和右侧页签；属性/加点切页保留草稿。 |
| 宝宝属性 | 按 `02-pet-ui.png` 组装滚动头像列表、六项属性两列、数字加点两列和操作区；保留出战/收回、自动建议、确认加点、洗点，并提供本地重置和真实改名入口。 |

人物页面不显示相性点页签。服务器属性池、维度名称、上限、余点、方案成本和实际数值仍由协议提供；页面不会用效果图中的演示数值覆盖服务器数据。四行布局保留效果图的间距，最多八行通过视口滚动显示；鼠标滚轮可在行间空白处使用，键盘选中的行会滚入可见区域。

## 美术资源与装配规则

| 客户端 Resources 根目录 | 用途与来源 |
|---|---|
| `Assets/Resources/UI/Ugui/RefreshV8/` | 登录、选服、选角及通用 UI 的背景、独立角色/宠物、标题和无字组件。来源及合同见 `E:/work/image/client_ui_refresh_20260908/README.md`、`refresh_components.json`。 |
| `Assets/Resources/UI/Ugui/AttributesPaintedV2/` | 人物/宝宝效果稿的精确切图，包括窗框、标题装饰、固定书法标题、字段、按钮、页签、分隔线、关闭图标、步进按钮与滑条。源目录为 `E:/work/image/designs/attribute-panels/v2-painted/unity-slices/`。 |

运行时 `Resources.Load` 路径分别从 `UI/Ugui/RefreshV8/` 和 `UI/Ugui/AttributesPaintedV2/` 开始，不带扩展名。

`AttributesPaintedV2/manifest.json` 自述由官方 Unity MCP 的 `Unity_RunCommand` 精确裁切；该批资源在本轮接入期间到位，原件及其 `.meta` 保留。接入代码复用其中的切图与 `spriteBorder`，没有重新生成图片。关键资源已核对为 Sprite Single、PPU 100、Alpha 透明；窗框边界为 left/bottom/right/top = `97/74/97/99`，字段为 `12/12/12/12`，滑条轨道/填充为 `7/7/7/7`。

固定标题、关闭及加减图标可直接使用对应切图；动态文案、名称、数值、输入始终使用原生文字。人物原生加减字在采用固定图标后隐藏，避免重复叠字。九宫格边框按素材原尺寸与显示高度换算，圆形图标和固定字图保持比例。整屏效果图仅用于对照，不充当带字的交互面板背景。

## 数据与交互边界

- 人物继续使用 `AttributeClient` 和 `UiPointRow`：本地只保存待提交增量，确认后提交目标已分配值，服务器返回权威面板后刷新。自动加点只提供建议；方案切换和新建保留未确认加点拦截。
- 宝宝继续使用 `PetClient`：确认提交目标分配值；“重置”撤销本次本地预分配，“一键洗点”调用真实洗点接口并显示协议提供的成本。改名、出战/收回之前会检查未确认加点，避免全量列表响应无提示清空草稿。
- 客户端只绑定已有协议。人物技能，以及宝宝技能、抗性、坐骑、图鉴尚无对应可接入的页面协议/操作，保持禁用。
- 宝宝头像按已核对的模型 ID 映射：`1001` 使用灵狐图，`1004` 使用云鹤图。`1002` 石灵与 `1003` 金猊没有对应的新图，显示太极缺省图；不按可修改的名字或列表顺序绑定其他宠物肖像。

## 宠物协议编号补齐

按用户本轮授权补齐 9 个宠物协议编号 `180–188`，保留原有 180 条协议的名称、编号及既有定义不变。客户端常量入口为 `Assets/Scripts/Net/MessageIds.cs`。

| 编号 | 客户端常量 | 用途 |
|---:|---|---|
| 180 | `NotifyPetListChanged` | 宝宝列表变化通知 |
| 181 | `RecallPet` | 收回出战宝宝 |
| 182 | `ResetPetPoints` | 洗点 |
| 183 | `GetPetList` | 获取宝宝列表 |
| 184 | `AllocatePetPoints` | 确认分配目标值 |
| 185 | `RenamePet` | 改名 |
| 186 | `GmGrantPet` | 既有开发调试获取入口 |
| 187 | `SummonPet` | 出战 |
| 188 | `AutoAllocatePetPoints` | 自动分配建议 |

补齐编号和客户端可编译，不等于服务端全量协议生成、所有服务端目标构建或真实网络链路已经验收。上述结果须分别以主任务的实际日志为准。

## 验证入口

在 Unity 编辑模式中，通过当前工程连接的官方 relay MCP 执行编辑器命令。先等待资源刷新和脚本编译结束，再调用以下现有入口：

```csharp
ClientUiRefreshVerification.CaptureAll();
ClientUiQaCapture.CaptureCityAttribute();
```

`ClientUiRefreshVerification` 位于 `Assets/Editor/ClientUiRefreshVerification.cs`，也可通过 Unity 菜单 **MMORPG → UI → Capture refreshed screens** 调用。它用生产控件在独立临时场景中生成 `2560×1080`、`1920×1080` 两种尺寸的页面截图，覆盖登录、选服、账号、选角/创建、人物、宝宝及空列表。

`ClientUiQaCapture` 位于 `Assets/Editor/ClientUiQaCapture.cs`，用于天墉城真实场景与 HUD、人物四行和八行属性布局的编辑器截图。两个工具统一输出到客户端 `.codex-artifacts/ui-refresh-20260909/`。

这些工具的示例属性与宝宝数据只在编辑器验证路径构造，不注入真实联网会话。截图用于检查布局和原生控件渲染，不能单独证明登录、服务器状态或加点写入已通过线上验证。

静态检查命令：

```powershell
git -C E:/work/mmorpg-client diff --check
```

主任务还应记录官方 relay MCP 的实际编译/控制台检查、相关 EditMode/PlayMode 测试结果，以及产物构建结果。测试名称、通过数量、输出路径和失败原因以实际运行日志填写。

## 最终验证与产物记录

| 项目 | 最终结果 / 证据 |
|---|---|
| Unity 资源刷新与脚本编译 | 官方 MCP 刷新后确认运行时与测试程序集已重编译加载；49 项测试能执行。 |
| 登录/选服/选角截图及检查 | CaptureAll 已输出 2560×1080、1920×1080 两种尺寸；已查看最终截图。 |
| 主城 HUD、人物四行/八行、宝宝截图及交互 | 实际主城离屏截图、宝宝四项/空列表两种尺寸已检查；HUD 不再遮住人物页签，宝宝底部按钮留在框内。 |
| 相关 EditMode / PlayMode 测试 | EditMode 49/49 通过，0 失败，0 跳过；见 `.codex-artifacts/ui-refresh-20260909/tests.xml`。本轮未跑 PlayMode 测试。 |
| 原有 180 条协议保持不变的核对 | 已核对 180/180 名称和编号未变；见 `E:/work/tmp/pet-registry-ui-20260909/validation.json`。 |
| 新增 180–188 编号与客户端绑定核对 | 用户明确授权后同步正式生成结果和客户端常量；见同目录 `sync-result.json`。 |
| 客户端构建与可交付产物路径 | Windows64 Development 构建成功，0 错误，511 条警告，899006510 字节；产物 `E:/work/tmp/ui_refresh_player/mmorpg.exe`。见 `player-build.txt`。 |
| 服务端生成/构建与真实联网验收 | 本轮只执行协议描述/注册编号生成及客户端常量同步；未执行服务端全量构建，未以真实账号验证联网加点/改名。 |


## 后续背包、任务、活动

用户正在补充这三页效果图，将在素材到齐后继续按效果图接入。截至本轮接口审查，image 下尚未出现这三页独立效果图或切片，因此不列入已完成页面。

- 背包：服务端已有 BagService、ItemEntry 和 BagAllData，但普通玩家列表、变更通知及操作 RPC 尚缺；客户端无对应入口。
- 任务：MissionSystem 已有接取、放弃、进度和奖励逻辑，但缺玩家查询/操作 RPC 及客户端生成类型。
- 活动：尚未发现玩家活动服务、时间状态和领奖 RPC。

新界面可以复用本轮的 HUD 分层、窗口生命周期和美术加载方式；真实交互需接上各自玩家协议，不能将服务端持久化对象当作现成网络接口。
构建警告集中在 Unity AI Inference/Sentis 的着色器变体、整数运算等；MCP 因记录到警告返回了 partial 警告状态，但 Unity BuildReport 和实际产物确认构建成功，未重复出包。警告摘要保存为 `build-warnings.txt`。

已用此独立客户端进行启动检查，日志确认 `QdaoUguiRuntime.Create` 创建原生 UI；没有捕获到 C# 启动异常。本机 `127.0.0.1:8081` 网关未运行，因此服务器列表和公告请求连接失败，尚未验证真实登录或联网加点。启动检查日志为 `player-startup.log`，检查进程已关闭。
已将成功产物同步到原一键启动入口使用的 E:/work/tmp/showcase_player/。逐文件 SHA256 比对 289 个文件，0 个不一致，证据为 player-copy-audit.json。继续双击 E:/work/启动游戏.cmd 即会打开本轮新 UI 客户端。
## 2026-09-10 续接记录

本次接入的是已经发布的 v8 曝光修正版，共替换 73 张 PNG：`RefreshV8` 44 张、`AttributesPaintedV2` 29 张。其余 8 张 PNG 无变化，两资源目录全部 81 个 `.meta` 的 SHA256 均保持不变，现有 Sprite 引用、导入设置及九宫格边界得以保留。本次没有生成新图，也不属于 v10 重制交付；背包、任务、活动三组效果图和成套资源仍未交付，三页不计为完成。

同步前逐张核对客户端 PNG 等于发布合同中的 `original_sha256`、源 PNG 等于 `output_sha256`；全部通过后，先备份全部 73 张原 PNG，再复制最终资源。源合同为 `E:/work/image/qdao_exposure_refinement_v8/processing.json`（`published`）及同目录 `validation.json`（`passed`）。资源映射还依据 `E:/work/image/client_ui_refresh_20260908/refresh_components.json` 与 `E:/work/image/designs/attribute-panels/v2-painted/unity-slices/manifest.json`。

原 PNG 备份保留相对资源路径，位于 `E:/work/mmorpg-client/.codex-artifacts/ui-refresh-20260910/pre-refinement-assets/UI/Ugui/`。逐文件源、目标、备份和 `.meta` 哈希证据见 `E:/work/mmorpg-client/.codex-artifacts/ui-refresh-20260910/refinement-asset-sync-audit.json`：`status=passed`、`copied_count=73`、`unchanged_count=8`、`meta_files_checked=81`、`all_meta_preserved=true`，无错误。

| 项目 | 2026-09-10 已验证结果 |
|---|---|
| 最终资源上的测试 | EditMode 49/49 通过。 |
| 官方 MCP 截图 | 双尺寸 `2560×1080`、`1920×1080` 页面截图，以及主城、战斗截图已输出到 `E:/work/mmorpg-client/.codex-artifacts/ui-refresh-20260910/`。 |
| 实际视觉检查 | 主任务已查看登录、选服、选角、宝宝 16:9，以及主城中的人物面板截图。 |
| 客户端构建 | Unity `BuildReport` 为 `Succeeded`，`errors=0`、`warnings=497`、`bytes=899618013`。 |
| 当前构建产物 | `E:/work/tmp/ui_refresh_player_20260910/mmorpg.exe`。 |
| 服务端依赖与通信入口 | 服务端通信端点启动器已修复；已实证 Docker 与数据库依赖就绪。完整联网仍在验证，暂不对真实登录、人物/宝宝操作及服务器数据写入作验收结论。 |

以上构建记录对应 2026-09-10 的最终资源。新包已同步至默认 E:/work/tmp/showcase_player/，逐文件 SHA256 核验 289 个文件，0 个不一致，见本日 player-copy-audit.json。独立客户端启动日志确认 AppBootstrap 的原生 uGUI 已创建；验证进程已关闭。仍使用 E:/work/启动游戏.cmd。完整联网结果在后续记录中单独说明。

### 本机联网验证实际结果

通信端点修复后 Docker 与数据库依赖启动成功。原 scene_manager/player_locator/login 因 AttributePool.owner_type 与旧二进制不兼容退出，现已从已有源码隔离构建并替换，原程序在 E:/work/tmp/ui-network-retry-20260910/previous/ 备份；3 个服务已就绪，网关 health=UP。随后 gate/scene 因两个 g1 控制主题分区 1 与契约 256 不符，在注册前退出，区服仍显示 MAINTENANCE。未删除/扩容主题或修改代次，未完成真实账号登录与人物/宝宝读取。下一步修复方案见本日 network-next-step.md；账号自动登录准备被自动审批拒绝，等待明确授权。
