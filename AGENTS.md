# 客户端协作约定与 AI 规范总入口

> 本文件是本仓库对 AI Agent（Claude Code / Codex / Gemini / Antigravity / Cursor / Copilot 等）和人类开发者的**唯一规范总入口**。
> `CLAUDE.md`、`GEMINI.md` 与 `.github/copilot-instructions.md` 只通过 `@AGENTS.md` 单行导入本文件，不在多处重复维护规则；规则变更只编辑本文件或 `Docs/rules/` 下对应的分章文档。

## 会话启动门禁（每次必做）

**AI 没有可靠的跨会话记忆。** 每次新会话动手前必须按序确认：

0. **新电脑 / 新 AI Agent 环境必先安装必装 Skills**（`mattpocock/skills`、`DietrichGebert/ponytail` 及精选 ≥2 万 Star 代码工程 Skills），清单与安装命令见 → [Docs/rules/required-skills.md](Docs/rules/required-skills.md)
1. 阅读 `AGENTS.md`（已通过导入文件自动加载的无需重复读取）及本次任务涉及的 `Docs/rules/*.md` 分章细则；**任何功能、网络、SDK、UI 与资源改动都必须同时满足「抖音/微信小游戏 + iOS/Android App 游戏」双形态硬约束**（见 [Docs/rules/minigame-and-app-platforms.md](Docs/rules/minigame-and-app-platforms.md)）
2. 阅读 [Docs/ClientProtocolCompatibility.md](Docs/ClientProtocolCompatibility.md) 相关章节与 `Docs/` 下对应界面/系统文档；涉及跨端契约时同步核对同级服务端仓 `docs/design/<模块>.md`
3. 执行 `git status -sb` 与 `git log -20 --oneline` —— 核对既有未提交改动与近期演进，严禁覆盖或回退他人工作

先确认任务范围、既有改动、跨端契约与双形态平台约束；没读懂或缺少设计/协议事实源就不盲目动手。

## 规范分章索引（`Docs/rules/`）

| 领域 | 核心约束摘要 | 完整规范文档 |
|---|---|---|
| 必装 Skills | 新电脑 / 新 Agent 必装 `mattpocock/skills`、`ponytail` 等 8 个仓库共 618 个 Skills（含一键复原脚本与完整明细） | [Docs/rules/required-skills.md](Docs/rules/required-skills.md) |
| **抖音/微信小游戏 + App 双形态（强制）** | **WebGL2 单线程禁阻塞 + App IL2CPP 防裁剪、HTTPS/WSS 合法域名（并发 ≤3 条）、前后台恢复、多端登录与官方按钮、支付渠道隔离（iOS 仅 IAP 禁外链）、实名防沉迷 / 7 天账号注销 / 内容安全、首包分包与异形屏/胶囊避让** | [Docs/rules/minigame-and-app-platforms.md](Docs/rules/minigame-and-app-platforms.md) |
| 协议、配表与跨端不变量 | 生成物边界、3 步同步、`double` 坐标与 `uint64` 位宽、永久 ID 禁排禁拆、Tip 单轴、`*TableManager` 只存 `id` 禁存行引用、查表判空与位图容错、`RpcGuard` 限流熔断与幂等键 | [Docs/rules/proto-and-tables.md](Docs/rules/proto-and-tables.md) |
| 命名与权限边界 | 见名知意+带单位、分层命名（`*Client` / `*Model` / `*Window`）、中文协作+英文诊断日志、`.meta` 守护与硬禁令 | [Docs/rules/naming-and-boundaries.md](Docs/rules/naming-and-boundaries.md) |
| 工程设计与生命周期 | SRP 三层解耦（纯 C# `*Model` 脱离 `UnityEngine`）、**系统/模块间 Actor/事件消息通信（不可变消息、禁过度拆内部函数）**、KISS/YAGNI、**实例与布局分层**、**连接唯一所有权+对称退订+回调先验存活与会话代次** | [Docs/rules/engineering-principles.md](Docs/rules/engineering-principles.md) |
| AI 协作与模型档位 | Claude 改代码 vs Codex 跑编译/生成/测试、无运行证据禁称已验证、草稿 `git merge-file` 安全写回、强制高低档模型切换、设计方案强制落盘 `.md` | [Docs/rules/ai-workflow.md](Docs/rules/ai-workflow.md) |

---

## 仓库定位与工作区约束

- 核心关联 Git 仓库（按 GitHub 地址定位，本地克隆路径因机器而异，不写死盘符路径）：
  - Unity 客户端（本仓库）：`https://github.com/luhailong-cpp/mmorpg-client`
  - C++/Go 服务端：`https://github.com/luyuan-cpp/xuanming-server-mmo`
  - Java 服务端：`https://github.com/luyuan-java/xuanming-server-mmo`
  - Python 服务端：`https://github.com/luhailong-python/xuanming-server-mmo-python`
  - 协议转 MySQL 工具库：`https://github.com/luyuan-cpp/proto2mysql`
- **双服务端契约同步**：服务端 C++/Go 版与 Java 版并行演进，共享同一套客户端契约（帧格式、客户端可见 proto、tip 码、配置表数据）。
- **语言约定**：AI 协作回复、注释、Commit Message 与文档统一用**中文**；运行期诊断日志保持结构化**英文**。
- 构建、日志、缓存与临时验证产物放在所属项目的 Git 忽略目录（如 `Builds/`、`Logs/`、`.codex-artifacts/`）或系统临时目录，不在工作区顶层新建额外杂项目录。
- 确需工作树时优先使用托管工作树，或放在工作区之外。代码合并、验证并确认无独有未保存内容后清理工作树。
- 保留并提交有效源码（`Assets/` 下增删改移必须同步 `.meta`）、资源和需要长期留存的验证证据；不得因清理目录丢弃未合并的提交或未提交改动。
- 生成协议、配置表与调用桩必须来自当前权威服务端源和正式生成器，不手改生成代码。合并须保留双方已有功能，并运行与变更对应的编译和测试。

## 开发分支与通用工程原则

- **只在 `main` 分支开发**：所有改动直接在 `main` 分支进行，禁止创建临时分支；如确需工作树清理，须先确认无未保存改动。
- **未上线阶段前提**：游戏尚未上线，玩家数据可完全清空；按最标准方案实现与重构，不堆砌历史兼容包袱。
- **架构与质量要求**（完整细则见 → [Docs/rules/engineering-principles.md](Docs/rules/engineering-principles.md)）：
  - 优先级：`正确性 / 安全 / 客户端与服务端状态一致性 / 向后兼容 > 简单 / 可读 / 与仓库一致 > 可维护 / 可测试 > 有数据支撑的性能 > 假想扩展`。
  - 严格遵守**单一职责（SRP）**与高内聚低耦合：通信协调（`*Client`）、纯 C# 推导模型（`*Model`，不依赖 `UnityEngine` 以便单测）、uGUI/场景表现（`MonoBehaviour`）分层清晰，拒绝无意义透传层与深层 `MonoBehaviour` 继承树。
  - **系统 / 模块间消息通信（Actor / 事件隔离，强制）**：各业务模块 / UI 系统独占状态，跨系统交互一律通过**不可变消息 / 只读快照 / 事件 / RPC** 在主线程有序通信，禁止传递可变对象引用或借全局单例绕过消息边界；仅限跨模块边界使用，单个模块内部保持正常直接方法调用，严禁把内部普通函数过度拆成消息。
  - **经典设计模式与开源库选型**：适合用 **GoF 23 种设计模式**解耦或消除巨型分支时主动采用并向程序说明原因；某功能有 **GitHub ≥ 2 万 Star** 成熟开源库/插件可用时（安全与 WebGL2/IL2CPP 兼容优先于 Star 数）交由程序决定是否引入并说明对比与代价，不擅自默默引入新依赖。
  - **实例与布局分层**：「是什么」（`guid` / `config_id` / `equip_kind`）与「摆在哪」（槽位表 `EquipSlot` / 格子位置 / 排序）严格分离，不靠 proto `repeated` 下标承载槽位语义，UI 临时态不混入权威快照。
  - **连接与回调生命周期**：连接所有权只归 `GameClient` / `BattleDirectLink`；UI 订阅事件必须在 `OnDisable` / `OnDestroy` 对称退订；异步回调先判 `UnityEngine.Object` 存活与 `(PlayerId, 连接代次)` 一致再改状态。
  - **命名第一原则**：所有命名清晰准确并标明单位（`Ms` / `Sec` / `Bytes`），拒绝模糊缩写与泛化名（详见 → [Docs/rules/naming-and-boundaries.md](Docs/rules/naming-and-boundaries.md)）。
  - 发现已知问题或设计缺陷当场修复，不拖延到“以后再修”。
- **分工约定**：只负责客户端业务代码时，协议生成、编译检查与冒烟验证可交由 Codex 执行；无实际运行证据不得声称“编译通过 / 测试绿”。

## 门禁命令与禁止手改目录

服务端改动客户端可见 `.proto`、`Tip.xlsx` 或配置表后，**必须按序完成以下 3 步**才算同步闭环（仅跑服务端 `proto-gen` 会导致 `*Rpc.cs` 与 `Proto/Generated/*.cs` 错位、Unity 进入 Safe Mode）：

1. **先在权威 C++/Go 服务端仓生成**（或直接在本仓运行 `.\gen.bat` 一键串联步骤 1–3）：运行 `dev.bat proto`（若改动了 `Tip.xlsx` 或配置表，运行 `dev.bat gen`），更新服务端绑定、`proto/message_id.txt`、`generated/tables/*.pb`（服务端全量）、`generated/client_tables/*.pb`（已按 `owner` 剔除 `server` 私有列）与本仓 `Assets/Scripts/Net/Generated/Rpc/*Rpc.cs`、`Assets/Scripts/Table/Generated/`。
2. **再在本仓同步客户端表数据、protobuf 与消息号**：`tools/sync_tables.ps1` 仅从服务端裁剪产物 `generated/client_tables/*.pb` 同步到 `Assets/StreamingAssets/Tables/*.pb`（缺目录即失败，绝不回落拷贝服务端未裁剪表）并补齐 `Table/Generated`、`Net/Generated/Rpc` 的 `.meta`；`tools/gen_proto.ps1` 自动扫描 `Rpc/*.cs`、递归 `import`、全部 `generated/code/proto/tip/*_tip.proto` 与底层信封种子并同步 `.meta`；三份脚本均自动探测同级权威服务端仓与仓库 `protoc`。
3. **最后过编译门禁**：执行 `tools/client_compile_check.ps1`；若改动涉及公共签名或测试文件，一并确认受影响的 EditMode/PlayMode 测试程序集编译通过（任一程序集报错都会令 Unity 触发 Safe Mode）。

```powershell
# 1. 一键完成「表 + 协议」全链路生成与编译体检（默认含服务端导表 + proto-gen + 客户端全同步）
.\gen.bat           # 全量：服务端导表 & proto-gen → 客户端表数据 / 协议 / 消息号 / 编译体检
.\gen.bat proto     # 仅改 .proto 时：跳过导表
.\gen.bat client    # 服务端已跑过 dev.bat gen 时：仅同步客户端侧

# （等价分步命令）
pwsh -File tools/sync_tables.ps1
pwsh -File tools/gen_proto.ps1
pwsh -File tools/gen_messageids.ps1
pwsh -File tools/client_compile_check.ps1

# 2. 客户端构建
pwsh -File tools/build_client.ps1

# 3. 相关自动化测试（按改动模块选择）
pwsh -File tools/run_activity_tests.ps1
pwsh -File tools/run_quest_tests.ps1
pwsh -File tools/run_move_test.ps1
```

- **禁止手改目录**：自动生成的协议、消息号与配表文件（`Assets/Scripts/Proto/Generated/`、`Assets/Scripts/Net/Generated/`、`Assets/Scripts/Net/MessageIds.cs`、`Assets/Scripts/Table/Generated/`、`Assets/StreamingAssets/Tables/`）及服务端同步契约产物（需改权威源后按上述步骤重新生成）。
- **新增客户端 `.proto` 无需手维护 `gen_proto.ps1` 清单**：只需保证服务端 `proto-gen` 已生成对应 `*Rpc.cs` 或导表器已产出 `*_tip.proto`；仅新增不挂在任何 `service` 下的底层传输信封 `.proto` 时，才补进 `gen_proto.ps1` 种子；若业务通过 `MessageIds.*`（而非 `*Rpc.*MessageId`）引用新 RPC，再补进 `tools/gen_messageids.ps1` 的 `$whitelist`。
- **跨端契约硬约束**（完整细则见 → [Docs/rules/proto-and-tables.md](Docs/rules/proto-and-tables.md)）：
  - 大世界坐标/速度协议为 `double`，64 位 ID / 基础属性 / 货币 / 时间戳必须全程 `ulong`/`long` 严禁窄化；
  - 发号器永久 ID（`player_id` / `guild_id` / 物品 `guid` / `battle_id` / `team_id` 等）无位布局且不单调，**严禁拆位解析、严禁按 ID 大小排先后、严禁转 `float`/`double`**；
  - Tip 码与故障判定统一走生成的 `*ErrorTip` 与 `TipFaults.IsFault(tipId)`，禁手写数字或私有故障表；
  - 配置表 `*TableManager` **只存 `uint id`、用时现查 `FindById`，严禁长期缓存表行引用**（否则热更失效），且必须处理 `FindById == null` 与删表/位图容错。

## UI 原图还原验收（用户确认于 2026-11-06）

- UI 以用户指定的 `D:/work/image/designs` 对应效果图为基准，要求完全一致；主验收分辨率是每张效果图自身的原始像素尺寸，不统一改成 1920×1080 或 2560×1080。
- 逐界面锁定原图文件、SHA-256、尺寸、界面状态与样例数据。相同内容和状态下，对完整客户端截图逐像素比较；零差异才可声称该状态画面一致。不得缩放、自动对齐、裁掉差异、使用容差或遮罩后将局部通过当成整屏通过。
- 保留真实业务交互及动态数据。不得用整张效果图遮盖运行界面、截图专用控件或硬编码样例文字来获得通过；固定装饰和固定题字可使用原图素材。
- 原图没有覆盖的按钮、弹窗及状态须有对应设计依据，不能擅自重排后宣称与原图一致。缺少精确字体、分层底图或状态设计时，明确列为未解决项，不以近似素材代替并标记完成。
- 切片哈希一致、离线拼接、编译通过、预览场景截图及历史截图均不能单独证明当前发布客户端画面一致。记录截图来源与代码/资源版本；功能验证与发布客户端视觉验收分别完成。

## 小游戏与 App 游戏双形态兼容与合规约束（负责人确认于 2026-11-20）

本客户端**同时面向「微信 / 抖音小游戏（WebAssembly + WebGL2）」与「iOS / Android 原生 App（IL2CPP / AOT）」首发**。平时写的每一处代码、SDK 桥接与资源都必须**同时守住小游戏与 App 两端的技术限制、商店提审规则与合规红线**，严禁写出“只在 PC 编辑器能跑、转小游戏或提审商店就大返工”的实现（完整细则见 → [Docs/rules/minigame-and-app-platforms.md](Docs/rules/minigame-and-app-platforms.md)）。

### 1. 抖音 / 微信小游戏（WebGL2）技术硬约束

微信 / 抖音小游戏是 Unity 的 Web 构建(WebAssembly + WebGL2)跑在宿主里。负责人定:转包放到上线前再做(届时打算用 Unity 7),所以平时写的代码与资源就要守住下面这些限制,否则转包时是大返工。依据是 Unity 6.6 手册的 Web 平台「Technical limitations」「.NET API support on the Web platform」「Introduction to Web multithreading」「WebGL2」四页;服务端一侧见服务端仓 `docs/design/minigame-websocket-gate.md`。

- **单线程**:C# 托管线程不可用。`new Thread` 的委托不执行,`ThreadPool.QueueUserWorkItem` 的回调永远不跑,`System.Threading.Timer` / `System.Timers.Timer` 不触发,`CancellationTokenSource` 的超时不生效;`lock`、`Monitor`、`Mutex`、`Semaphore` 能编译但不起作用,`System.Collections.Concurrent` 的容器没有真正的线程安全。
- **会让页面卡死、无法恢复的调用**:`Task.Run`、`Task.Delay`、`Task.Factory.StartNew`、`Parallel.*`,以及任何经线程池调度的 API,包括 `ConfigureAwait(false)` 之后的续体、`FileStream` / `File.*Async` 的异步读写、`HttpClient`、`WebClient`、`HttpWebRequest`。主线程上也不许阻塞等待(`.Wait()`、未完成任务的 `.Result`、`Thread.Sleep`):只有这一个线程,等不到结果。
  - 替代:等待与超时用 `Awaitable`、协程,或在 `Update` / `Poll` 里按单调时钟判。并行计算只有 Burst Job,还要开 Native C/C++ Multithreading、服务器发 COOP / COEP 响应头;小游戏宿主支不支持没有核实,按不可用算。
- **网络**:没有原始套接字(`TcpClient`、`UdpClient`、`Socket`、`SslStream`、`UnityEngine.Ping` 都不行),`ClientWebSocket` 也不行。
  - HTTP 一律走 `UnityWebRequest`。
  - 长连接一律经 `ClientTransport` / `IFramedConnection`(小游戏下是 `WebSocketFramedConnection` 加 `Assets/Plugins/WebGL/MmorpgMiniGame.jslib`)。新加的连接代码不许直接 new `GateTcpClient`,也不许用 `TcpClient` 探测;会阻塞的 `Connect` 交给 `ClientTransport.ConnectRunner`,不要默认丢进线程池。
  - 小游戏只能连后台登记过的 https / wss 域名,不能连 IP（且微信 `wx.connectSocket` 并发上限 5 条，本客户端同时严格控制在 ≤ 3 条）。服务端下发的 `*_ws_url` 就是给小游戏连的地址，正式包收到空 `ws_url` 直接按连接失败处理。
- **文件**:同步的 `System.IO` 读写在内存文件系统(MEMFS)上能用;异步读写会卡死;`DriveInfo` 不支持;Unity 的 `Caching` API 不支持。小游戏里写进 `persistentDataPath` 的文件能不能跨会话保留没有核实,转包时定。现有的本机待确认记录丢了只是少一次自动恢复,服务端按幂等键去重。
- **图形**:WebGL2 只有 OpenGL ES 3.0 的能力。
  - 没有 Compute Shader(WebGPU 才有,小游戏宿主按 WebGL2 算),也没有几何 / 曲面细分着色器。
  - 不支持实时 GI,只有烘焙 GI,光照贴图只能是非方向性的。
  - 自写 GLSL 要写精度限定符;着色器变体要裁剪(内存紧),不要把变体很多的着色器放进 Always-included Shaders。
  - 视频只能用 VideoPlayer 的 URL 方式从 StreamingAssets 流式播放。
- **字体**:读不到系统字体,用到的字体(含中文回退字体、粗体 / 斜体)都要打进包里。
- **内存与包体**:内存比原生紧得多,首包也有上限;大资源走 CDN 分包加载(Addressables / AssetBundle),不要塞进首包。具体数值转包时以两家平台文档为准。
- **其它**:音频只有基础功能(Web Audio);物理结果与其它平台不保证逐位一致,权威结算在服务端,客户端不得拿物理结果做判定;没有 `System.Reflection.Emit`(与 iOS 的 IL2CPP 相同)。
- **平台专属代码的写法**:只在小游戏里跑的 C# 放进 `#if UNITY_WEBGL && !UNITY_EDITOR`;JS 桥放 `Assets/Plugins/WebGL/*.jslib`,直接调宿主的 `wx` / `tt`(与 `MinigamePay.jslib` 同一写法);回调用专门声明的委托类型加 `[MonoPInvokeCallback]`。这些分支编辑器的编译检查编不到(本机也没装 WebGL Build Support),转包时要先完整编一次 Web 包。
- 2026-11-20 核对过:登录、gate、战斗直连、地域测速这条小游戏会走到的路径上没有上面这些用法。`GateTcpClient`、Android 支付桥、特效验证工具里的线程只在原生、编辑器或命令行验证时运行。
- 上线前要提前办、需要几周的手续(域名 ICP 备案、两家开发者账号、合法域名登记)见服务端仓 `docs/ops/release-checklist.md` §7。

### 2. iOS / Android App 游戏与小游戏跨端提审及合规红线（必守）

- **IL2CPP / AOT 与原生插件隔离**：iOS / Android IL2CPP 同样禁用 `System.Reflection.Emit`；反射 / JNI / ObjC / `.jslib` 回调目标必须防裁剪（`[Preserve]` / `link.xml` / ProGuard `-keep`）。iOS 插件放 `Assets/Plugins/iOS/*.mm`，Android 插件用独立 `Assets/Plugins/Android/*.androidlib`（用 `${applicationId}` 占位不硬编码包名，Android 11+ 拉起微信/支付宝必须在 `<queries>` 声明包名）；调试/GM 功能严封在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内。
- **前后台挂起恢复与触控/异形屏适配**：App 锁屏/切后台与小游戏最小化都会触发 30 秒心跳空闲断连，统一经 `OnApplicationPause` 走 `CloseForResume` + 20 秒令牌短重连与战斗补签（登出/顶号/注销/防沉迷踢线绝不自动重连）。交互不依赖鼠标 `Hover` 悬停，HUD 兼容手机 `Screen.safeArea`（刘海/灵动岛）与微信/抖音右上角胶囊避让。
- **多端登录与品牌规范**：严格区分 `wechat_minigame`（`wx.login`）、`douyin_minigame`（`tt.login({force:true})`，**禁匿名 `anonymousCode`**）、`apple`（iOS 有第三方登录时按 App Store 4.8 必配，按钮须严守 Apple HIG 官方标志/配色/≥44pt 高度）、`google`（`serverClientId` 填 Web OAuth Client ID，遵守 Google 品牌规范）与国内 `wechat` / `qq`。一次性凭证与密码不落盘，Refresh Token 禁明文存 `PlayerPrefs`。
- **真钱支付渠道严格隔离（提审生死线）**：**iOS App 虚拟商品只允许走 App Store IAP（严守 3.1.1，iOS 包严禁出现微信/支付宝/外链切支付）**；Google Play 只走 Play Billing（服务端回 `finish_transaction` 后再消耗，启动时自动补交未结挂起单）；国内 Android 走微信/支付宝 SDK；微信/抖音小游戏走各自虚拟支付桥（`MinigamePay.jslib`）；`devpay` 仅限本地 `dev/test`。**客户端绝不采信本地 SDK 返回值，到账只认服务端。**
- **实名防沉迷、账号注销与内容安全三大件**：
  1. **实名防沉迷**：禁一切未实名游客模式；微信/抖音小游戏由宿主平台拦截时段（小游戏端不弹窗收身份证）；国内 App 按 `/api/login` 的 `real_name_required` 弹实名窗（**严禁缓存或打日志记录姓名与身份证号**），处理时段拦截、剩余 10/5/1 分钟提醒、到点踢线与未成年人充值限额。
  2. **账号注销（App Store 5.1.1(v) / Play / 国内硬规）**：游戏内必须提供真注销闭环（披露删除/保留范围 + 7 天冷静期 + 当场重新认证；**iOS Apple 账号注销必须重新授权上传 `authorization_code` 供服务端调 `/auth/revoke` 撤销令牌**），完整处理 `blockers` 拦截、冷静期撤销弹窗与注销完成首次告知。
  3. **UGC 内容安全**：所有玩家可填文本（角色/帮会/宠物/方案名、帮会公告、聊天）均须经服务端 `content_guard`（本地词库 + 微信 `msgSecCheck` / 抖音 `antidirt` / App 商业审核）通过后生效，新增客户端可输入 `string` 字段必须同步接入服务端审核契约。

## AI 协作与模型档位规定（负责人确认于 2026-11-20）

- **写/修改代码，以及需求分析、设计分析、思想分析、漏洞分析、步骤分析、执行步骤，必须使用各软件当时的最高档模型**，不得在低档模型上执行：Claude Code 用 `Fable 5.2 Max`，Gemini 用 `Gemini 4 Neon Extra`，Codex 用 `GPT 6.2 Astral Ultra`。
- **设计方案强制落盘 `.md`（2026-11-21 定）**：以后编写设计方案时，Claude Code（及其他 AI Agent）写好设计后必须将**需求分析、设计分析、思想分析、漏洞分析、步骤分析、执行步骤**及完整方案保存到 `Docs/design/<主题>.md`（或 `Docs/` 下对应模块设计文档），不得只留在对话中。
- **仅编译、协议生成、跑测试与冒烟使用低档模型**：Gemini 用 `Carbon Medium`，Codex 用 `6.1 Sol Ultra`，Claude Code 用 `Opus 5 High`；遇到需要改代码或需做上述分析/步骤设计的报错先保留原文停下，切回最高档模型再处理。
- 完整协作分工、草稿副本 `git merge-file` 安全写回与交付门禁见 → [Docs/rules/ai-workflow.md](Docs/rules/ai-workflow.md)。
