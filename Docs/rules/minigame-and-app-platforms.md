# 抖音/微信小游戏与 iOS/Android App 双形态强制规范

> 对应根索引 [AGENTS.md 小游戏与 App 游戏双形态兼容与合规约束](../../AGENTS.md#小游戏与-app-游戏双形态兼容与合规约束负责人确认于-2026-11-20)。
> 本客户端**同时面向「微信小游戏 + 抖音小游戏（WebGL2 / WebAssembly）」与「iOS + Android 原生 App（IL2CPP / AOT）」首发交付**。平时编写任何客户端代码、接入渠道能力或制作 UI/资源时，**必须同时遵守小游戏与原生 App 的技术边界、平台审核规范与法规红线**，严禁写出“只在 PC 编辑器能跑，转小游戏或提审 App Store / Google Play / 国内渠道即返工”的实现。

权威跨端设计事实源（同级服务端仓 `docs/design/` 与 `docs/ops/`）：
- 网络与小游戏网关：`minigame-websocket-gate.md`（W1–W11）
- 登录与第三方认证：`auth-provider-framework.md`、`third-party-login-end-to-end-design.md`（§11–12）
- 真钱充值与渠道隔离：`payment-recharge.md`（§22 及附录 A）
- 实名认证与未成年人防沉迷：`real-name-anti-addiction.md`
- 账号注销与 Apple/Google 接入：`account-deletion.md`
- 改名与 UGC 内容安全：`rename-and-content-safety.md`
- 上线前置手续：`docs/ops/release-checklist.md`（§7）

---

## 1. 运行时、单线程与 IL2CPP / AOT 硬约束

### 1.1 抖音 / 微信小游戏（WebGL2 + WebAssembly）单线程禁令
- **托管多线程不可用**：`new Thread` 委托不执行，`ThreadPool.QueueUserWorkItem` 回调不触发，`System.Threading.Timer` / `System.Timers.Timer` 不触发，`CancellationTokenSource` 超时不生效；`lock` / `Monitor` / `Mutex` / `Semaphore` 与 `System.Collections.Concurrent` 无真实并发安全语义。
- **严禁导致宿主页面永久卡死的调用**：
  - ❌ `Task.Run`、`Task.Delay`、`Task.Factory.StartNew`、`Parallel.*` 及任何经线程池调度的 API（含 `ConfigureAwait(false)` 续体、`FileStream` / `File.*Async` 异步读写、`HttpClient` / `WebClient` / `HttpWebRequest`）；
  - ❌ 主线程阻塞等待（`.Wait()`、未完成任务的 `.Result`、`Thread.Sleep`）。
  - ✅ **唯一合法替代**：等待、重试与超时统一用 `Awaitable`、协程，或在 `Update` / `Poll` 中基于不受 `Time.timeScale` 影响的单调时钟推进。

### 1.2 小游戏 WebGL 与 iOS/Android App（IL2CPP）共用的 AOT 与裁剪红线
- **禁止动态代码生成**：两个形态均不支持 `System.Reflection.Emit`（禁止引入依赖运行时 IL 生成的序列化、ORM 或动态代理库）。
- **防 Managed Stripping / 混淆误裁**：通过反射、JNI（`AndroidJavaClass`）、Objective-C `UnitySendMessage` 或 `.jslib` 回调调用的 C# / Java 方法与 DTO（如 `AndroidPayBridges`、`PlatformSignIn`），必须保持显式引用或配套 `[Preserve]`、`link.xml`、Android ProGuard `-keep` 规则，杜绝 Release 包运行期报 `MissingMethodException` / `ClassNotFoundException`。
- **平台专属代码隔离**：
  - 小游戏专属 C#：`#if UNITY_WEBGL && !UNITY_EDITOR`，JS 桥放 `Assets/Plugins/WebGL/*.jslib` 直接调宿主 `wx.*` / `tt.*`，回调用显式声明的委托类型加 `[MonoPInvokeCallback]`；**检测运行宿主时必须先判 `tt` 再判 `wx`**（抖音小游戏宿主可能同时暴露 `wx` 兼容对象，先判 `wx` 会误识别为微信，见 `MmorpgMiniGame.jslib` / `MinigamePay.jslib`）；
  - iOS 原生插件：`Assets/Plugins/iOS/*.mm`（`#if UNITY_IOS && !UNITY_EDITOR`，`[DllImport("__Internal")]`），Xcode 能力经 `Assets/Editor/*BuildPostProcess.cs` 自动注入；
  - Android 原生插件：`Assets/Plugins/Android/*.androidlib`（`#if UNITY_ANDROID && !UNITY_EDITOR`），各自独立维护 `build.gradle` 与 `AndroidManifest.xml`，**不硬编码最终游戏包名**（用 `${applicationId}` 占位，Android 11+ 拉起外部 App 必须在 `<queries>` 中声明目标包名，如微信 `com.tencent.mm`、支付宝 `com.eg.android.AlipayGphone`）；
  - 本地开发调试 / GM 入口：必须严格包在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内，正式包零残留。

---

## 2. 网络传输、域名合规与前后台生命周期

- **统一传输层（禁裸 Socket / 禁私建连接）**：
  - HTTP 请求一律走 `UnityWebRequest`；
  - 长连接（大厅 `GameClient`、战斗直连 `BattleDirectLink`、观战 `SpectateClient`、地域测速 `RegionProbeClient`）一律经 `ClientTransport` / `IFramedConnection` 创建：原生 App 默认走 TCP 直连 `client_endpoint`，抖音/微信小游戏统一走 `WebSocketFramedConnection` + `MmorpgMiniGame.jslib`（抖音调 `tt.connectSocket`，微信调 `wx.connectSocket`，**不走微信限制繁多的 `TCPSocket`**）。
- **并发连接数上限（微信硬限制）**：微信小游戏 `wx.connectSocket` 同时最多允许 **5 条**连接；本客户端严格控制在 **≤ 3 条**（大厅 1 条 + 战斗/观战/地域测速），测速或换连接结束后必须立即关闭释放。
- **HTTPS / WSS 合法域名与证书红线**：
  - 微信与抖音小游戏正式包**只能连接完成 ICP 备案并在两家后台登记为 `request` / `socket` 合法子域名的 `https://` 与 `wss://`（443 端口）地址，严禁连接裸 IP、父域名通配或自签证书**；正式构建若收到空 `ws_url` 必须直接按连接失败处理，严禁回落明文 `ws://<ip>:<port>`；
  - iOS / Android App 正式包同样要求网关与支付回调全程走受信 CA 的 HTTPS（符合 iOS ATS 与 Android Network Security Config，不全局开放明文 HTTP）。
- **重定向与前后台恢复（App 挂起 + 小游戏切后台）**：
  - WebSocket 模式下收到 Gate 重定向（`124`）不做 TCP 探测，直接建新 WebSocket 并在真正打开后再关闭旧连接；
  - 手机锁屏、切后台、接电话或小游戏最小化时，进程挂起或 JS 定时器暂停会导致心跳中断并被服务端 30 秒空闲断开。宿主统一经 `OnApplicationPause` 驱动恢复：回前台先调 `CloseForResume` 清理死连接、处理已收到的踢线/重定向，再走 20 秒预算的令牌短重连与战斗票据补签；**主动登出、顶号、账号注销踢线、防沉迷到点踢线严禁自动重连抢回会话**。

---

## 3. 多端登录与品牌提审红线

| 运行形态 | `auth_type` | 客户端凭证获取 | 关键约束与提审红线 |
|---|---|---|---|
| **微信小游戏** | `wechat_minigame` | `wx.login()` 一次性 `code` | 与同微信开放平台主体下的微信 App 归一为同一 `wx_<unionid>` 账号；严禁与 App `wechat` 混传 `auth_type`（会浪费一次性 `code`） |
| **抖音小游戏** | `douyin_minigame` | `tt.login({force: true})` 的 `code` | 必须强制登录拿 `code` 换 `dy_<unionid>`；**严禁使用 `anonymousCode` 匿名登录**（换机丢号且违反实名规定） |
| **iOS App** | `apple`（海外/必配）等 | `ASAuthorizationAppleIDProvider` `identityToken` | **App Store 4.8 硬规**：只要有第三方登录就必须提供 Sign in with Apple；不请求多余姓名/邮箱 scope；按钮必须严格符合 Apple HIG（官方标志不拉伸不改色、黑/白/白底黑描边三选一、圆角、高度 ≥ 44pt） |
| **Android 海外 App** | `google` | Credential Manager `GoogleIdTokenCredential` | `serverClientId` 必须填服务端 Web 类型 OAuth Client ID（填 Android Client ID 会因 `aud` 不符被拒）；按钮须符合 Google Sign-In 品牌规范 |
| **国内 App** | `wechat` / `qq` / `password` | 微信 OAuth `code` / QQ `access_token` | 密码与第三方一次性凭证只驻内存，重连只用服务端签发的 `access_token` / `refresh_token` |

- **凭证安全与一次性消费**：`code` 与 Apple/Google `id_token` 在服务端均为一次性消费；网关回 `401` 或 Tip `2000`（凭证无效）时客户端必须重新拉起平台 SDK 获取新凭证，不得重发旧令牌；`session_key` 不进客户端持久态或日志，**Refresh Token 严禁明文存入 `PlayerPrefs`**。

---

## 4. 真钱支付与商店渠道隔离红线（提审一票否决项）

- **按平台严格隔离支付渠道，严禁串渠道**：
  - **iOS App**：买钻石等虚拟商品**只允许走 App Store IAP（`StorePayBridge` / StoreKit 2）**。严格遵守 **App Store 审核指南 3.1.1**，iOS 包内**严禁**暴露微信支付、支付宝、第三方网页充值链接或任何切支付引导文案；提审须兼容苹果沙盒交易核验；
  - **Google Play App**：只走 Google Play Billing（一次性消耗品须等服务端核验回 `finish_transaction` 后再确认消耗，避免 3 天自动退款）；
  - **国内 Android App**：走微信 OpenSDK（`WechatAppPayBridge`）与支付宝 SDK（`AlipayAppPayBridge`）；
  - **微信小游戏**：下单前先调 `wx.login()` 取一次性 `code` 交服务端现取现弃 `session_key` 算签名，再走虚拟支付道具直购 `wx.requestMidasPaymentGameItem`（`MinigamePay.jslib`）；
  - **抖音小游戏**：Android 走 `tt.requestGamePayment`（`MinigamePay.jslib` 已接），iOS 侧按抖音规范走 `tt.openAwemeCustomerService` 路径（待真机/转包阶段补齐）；
  - **`devpay`**：仅限本地 `dev` / `test` 模式联调，生产环境严禁展示或启用。
- **客户端绝不采信本地 SDK 支付结果**：
  - 任何 SDK 回调（含 Android `WXPayEntryActivity`）只做通知收口，**绝不作为发货或界面显示“已到账”的依据**；
  - 商店渠道下单前必须绑定服务端下发的订单标识（Apple `appAccountToken` / Google obfuscated account），拿到凭证后调 `SubmitPurchase`，并在每次启动/建连时自动补交未 `finish` 的挂起交易（防付款后杀进程丢单）；二段式渠道统一轮询服务端 `GetPayIntent` 确认终态。

---

## 5. 法规与商店合规三大件（实名防沉迷 · 账号注销 · 内容安全）

### 5.1 实名认证与未成年人防沉迷（国新出发〔2021〕14 号 / 〔2019〕34 号）
- **零未实名游客**：全形态不得向未实名用户提供任何形式的游戏体验（含游客试玩）。
- **微信 / 抖音小游戏形态**：实名认证、未成年人可玩时段（周五/六/日及法定节假日 20:00–21:00）由宿主平台容器统一拦截执行，**小游戏客户端不得自建弹窗收集玩家姓名与身份证号**；若该账号同时在 App 侧完成过实名（有 `pi`），服务端支付闸会自动跨端合并累计未成年人充值限额。
- **国内 App 形态**：
  - `/api/login` 返回 `anti_addiction.real_name_required = true` 时，选服进游戏前必须弹出实名认证框提交 `POST /api/real-name`；**玩家输入的姓名与身份证号严禁落本地缓存、`PlayerPrefs` 或任何日志**；
  - 正确拦截并提示 Gate `kLoginRealNameRequired`、`kLoginMinorNotInPlayTime` 以及支付侧未成年人年龄/单笔/月度限额拒绝 Tip；
  - 未成年人在线期间按 `play_deadline_ms` 在剩余 10、5、1 分钟弹出友好提醒；收到服务端到点踢线原因码时弹窗说明并退回登录页，不自动重连。

### 5.2 账号注销闭环（App Store 5.1.1(v) / Google Play / 国内应用市场强制）
- **App 与小游戏内必须有真注销入口**（「游戏设置 → 注销账号」）：清晰展示删除内容、依法去标识保留项及 **7 天冷静期**说明，玩家确认后必须完成**当场重新认证**（口令账号重输口令；微信/抖音小游戏重新调 `wx.login` / `tt.login` 取新 `code`；Google 重新拉起登录并在提交成功后调 `clearCredentialStateAsync`；**Apple 账号必须重新拉起 Apple 授权并一并上传 `authorization_code` 供服务端调 `/auth/revoke` 撤销令牌**，同时界面提示用户亦可前往系统 Apple ID 设置管理授权）。
- **状态闭环**：申请被 `blockers` 拦截时逐项清晰提示玩家处理（如帮主先转让、下架寄售角色、退队/结清交易）；冷静期内登录只弹剩余天数与「撤销注销 / 退出」双按钮，不进选角；账号在游戏内触发注销踢线时不自动重连；注销彻底完成后同渠道再次建新号登录时，按 `previous_account_deleted_at` 弹出一次原账号已完成注销确认。

### 5.3 UGC 文本内容安全（小游戏平台接口 + App 审核红线）
- 玩家可输入的**全部**文本（建角名、角色改名、建帮名、帮会改名、帮会公告、宠物改名、属性方案名、各频道聊天）一律由服务端 `content_guard`（本地归一化词库 + 微信小游戏 `msgSecCheck` / 抖音小游戏 `antidirt` / App 商业审核接口）先审后落库或转发。
- 客户端新增任何玩家可自由输入的 `string` 请求字段时，必须确认服务端已将其纳入内容安全契约检查（漏登记会导致服务端契约测试判红）；UI 必须正确处理审核不通过 Tip、聊天打码文本（`masked_text`）替换展示，以及角色/帮会改名的冷却期、改名卡占用与异步结果推送。

---

## 6. 包体、图形、字体、文件与异形屏适配

- **图形能力基线**：小游戏宿主按 WebGL2（OpenGL ES 3.0）计，移动 App 兼顾中低端机型——**不使用 Compute Shader、几何/曲面细分着色器与实时 GI**（仅用烘焙非方向性光照贴图）；自写 GLSL 必须声明精度限定符；严格控制 Shader 变体数，禁止把多变体着色器塞进 `Always Included Shaders`。
- **首包与内存预算**：小游戏有严格首包体积上限且 WebAssembly 内存远紧于原生，App 也有商店蜂窝下载提示线与低端机 OOM 红线。大体积地图瓦片、角色序列帧/模型、音频等走 CDN 按需分包加载（Addressables / AssetBundle），不在首包或 `Resources` 堆放巨型未压缩资源。
- **字体、文件与音视频**：两类平台均无法读取开发机系统字体，所有用到的中文字体（含回退字符集、粗斜体）必须打进工程资源；`System.IO` 仅允许小体量同步读写（禁异步读写，禁用 `DriveInfo` 与 `UnityEngine.Caching`；小游戏 `persistentDataPath` 跨会话留存不作强依赖，本地幂等原件丢失由服务端幂等键兜底）；视频在小游戏下统一通过 `VideoPlayer` URL 方式流式播放；物理计算仅作表现，不得用于权威逻辑判定。
- **触控交互与安全区避让**：所有交互不得依赖鼠标 `Hover` 悬停作为唯一信息入口（兼顾手指点击/长按与移动端软键盘弹起）；HUD 顶栏与四角控件必须兼容手机刘海屏 / 灵动岛 / 底部 Home 条（`Screen.safeArea`）以及**微信/抖音小游戏右上角原生胶囊按钮区域避让**；在锁定效果图验收分辨率下严格保持 [AGENTS.md UI 原图还原验收](../../AGENTS.md#ui-原图还原验收用户确认于-2026-11-06) 零差异标准。
