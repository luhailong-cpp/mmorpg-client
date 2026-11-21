# 客户端协作约定

- 核心关联 Git 仓库（按 GitHub 地址定位，本地克隆路径因机器而异，不写死盘符路径）：
  - Unity 客户端（本仓库）：`https://github.com/luhailong-cpp/mmorpg-client`
  - C++/Go 服务端：`https://github.com/luyuan-cpp/xuanming-server-mmo`
  - Java 服务端：`https://github.com/luyuan-java/xuanming-server-mmo`
  - Python 服务端：`https://github.com/luhailong-python/xuanming-server-mmo-python`
  - 协议转 MySQL 工具库：`https://github.com/luyuan-cpp/proto2mysql`
- 构建、日志、缓存与临时验证产物放在所属项目的 Git 忽略目录（如 `Builds/`、`Logs/`、`.codex-artifacts/`）或系统临时目录，不在工作区顶层新建额外杂项目录。
- 确需工作树时优先使用托管工作树，或放在工作区之外。代码合并、验证并确认无独有未保存内容后清理工作树。
- 保留并提交有效源码、资源和需要长期留存的验证证据；不得因清理目录丢弃未合并的提交或未提交改动。
- 生成协议与调用桩必须来自当前权威服务端源和正式生成器，不手改生成代码。合并须保留双方已有功能，并运行与变更对应的编译和测试。

## 开发分支与通用工程原则

- **只在 `main` 分支开发**：所有改动直接在 `main` 分支进行，禁止创建临时分支；如确需工作树清理，须先确认无未保存改动。
- **未上线阶段前提**：游戏尚未上线，玩家数据可完全清空；按最标准方案实现与重构，不堆砌历史兼容包袱。
- **架构与质量要求**：
  - 保持客户端与服务端状态/数据一致性，严格遵守**单一职责（SRP）**等开发原则，代码清晰易懂；适合使用经典设计模式解耦的模块主动采用并说明。
  - 发现已知问题或设计缺陷当场修复，不拖延到“以后再修”。
- **分工约定**：只负责客户端业务代码时，协议生成、编译检查与冒烟验证可交由 Codex 执行。

## 门禁命令与禁止手改目录

```powershell
# 1. 协议与消息号生成（来自权威服务端源）
pwsh tools/gen_proto.ps1
pwsh tools/gen_messageids.ps1

# 2. 编译检查与客户端构建
pwsh tools/client_compile_check.ps1
pwsh tools/build_client.ps1

# 3. 相关自动化测试（按改动模块选择）
pwsh tools/run_activity_tests.ps1
pwsh tools/run_quest_tests.ps1
pwsh tools/run_move_test.ps1
```

- **禁止手改目录**：自动生成的协议与消息号文件、服务端同步契约产物（需改权威源后通过 `tools/gen_proto.ps1` / `tools/gen_messageids.ps1` 重新生成）。

## UI 原图还原验收（用户确认于 2026-11-06）

- UI 以用户指定的 `D:/work/image/designs` 对应效果图为基准，要求完全一致；主验收分辨率是每张效果图自身的原始像素尺寸，不统一改成 1920×1080 或 2560×1080。
- 逐界面锁定原图文件、SHA-256、尺寸、界面状态与样例数据。相同内容和状态下，对完整客户端截图逐像素比较；零差异才可声称该状态画面一致。不得缩放、自动对齐、裁掉差异、使用容差或遮罩后将局部通过当成整屏通过。
- 保留真实业务交互及动态数据。不得用整张效果图遮盖运行界面、截图专用控件或硬编码样例文字来获得通过；固定装饰和固定题字可使用原图素材。
- 原图没有覆盖的按钮、弹窗及状态须有对应设计依据，不能擅自重排后宣称与原图一致。缺少精确字体、分层底图或状态设计时，明确列为未解决项，不以近似素材代替并标记完成。
- 切片哈希一致、离线拼接、编译通过、预览场景截图及历史截图均不能单独证明当前发布客户端画面一致。记录截图来源与代码/资源版本；功能验证与发布客户端视觉验收分别完成。

## 小游戏(WebGL)兼容约束(负责人确认于 2026-11-20)

微信 / 抖音小游戏是 Unity 的 Web 构建(WebAssembly + WebGL2)跑在宿主里。负责人定:转包放到上线前再做(届时打算用 Unity 7),所以平时写的代码与资源就要守住下面这些限制,否则转包时是大返工。依据是 Unity 6.6 手册的 Web 平台「Technical limitations」「.NET API support on the Web platform」「Introduction to Web multithreading」「WebGL2」四页;服务端一侧见服务端仓 `docs/design/minigame-websocket-gate.md`。

- **单线程**:C# 托管线程不可用。`new Thread` 的委托不执行,`ThreadPool.QueueUserWorkItem` 的回调永远不跑,`System.Threading.Timer` / `System.Timers.Timer` 不触发,`CancellationTokenSource` 的超时不生效;`lock`、`Monitor`、`Mutex`、`Semaphore` 能编译但不起作用,`System.Collections.Concurrent` 的容器没有真正的线程安全。
- **会让页面卡死、无法恢复的调用**:`Task.Run`、`Task.Delay`、`Task.Factory.StartNew`、`Parallel.*`,以及任何经线程池调度的 API,包括 `ConfigureAwait(false)` 之后的续体、`FileStream` / `File.*Async` 的异步读写、`HttpClient`、`WebClient`、`HttpWebRequest`。主线程上也不许阻塞等待(`.Wait()`、未完成任务的 `.Result`、`Thread.Sleep`):只有这一个线程,等不到结果。
  - 替代:等待与超时用 `Awaitable`、协程,或在 `Update` / `Poll` 里按单调时钟判。并行计算只有 Burst Job,还要开 Native C/C++ Multithreading、服务器发 COOP / COEP 响应头;小游戏宿主支不支持没有核实,按不可用算。
- **网络**:没有原始套接字(`TcpClient`、`UdpClient`、`Socket`、`SslStream`、`UnityEngine.Ping` 都不行),`ClientWebSocket` 也不行。
  - HTTP 一律走 `UnityWebRequest`。
  - 长连接一律经 `ClientTransport` / `IFramedConnection`(小游戏下是 `WebSocketFramedConnection` 加 `Assets/Plugins/WebGL/MmorpgMiniGame.jslib`)。新加的连接代码不许直接 new `GateTcpClient`,也不许用 `TcpClient` 探测;会阻塞的 `Connect` 交给 `ClientTransport.ConnectRunner`,不要默认丢进线程池。
  - 小游戏只能连后台登记过的 https / wss 域名,不能连 IP。服务端下发的 `*_ws_url` 就是给小游戏连的地址。
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

## 模型档位规定(负责人确认于 2026-11-20)

- **修改代码必须使用各软件当时的最高档模型**，不得在低档模型上改代码：Claude Code 用 `Fable 5.2 Max`，Gemini 用 `Gemini 4 Neon Extra`，Codex 用 `GPT 6.2 Astral Ultra`。
- **编译、协议生成、跑测试与冒烟使用低档模型**：Gemini 用 `Carbon Medium`，Codex 用 `6.1 Sol Ultra`，Claude Code 用 `Opus 5 High`；遇到需要改代码的报错先保留原文停下，切回最高档模型再改。
