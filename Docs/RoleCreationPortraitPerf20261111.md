# 创角页头像窗口：测速与阶段 1 优化（2026-11-11）

本轮只改客户端，未编译、未在 Unity 里运行，待 Codex 验证。服务端和协议不变，Java 版不适用。

## 1. 背景与审查结论

点“＋ 创建角色”卡住的问题，前两轮已经处理：
- 创角解析改走索引；
- 窗口只持有当前人物和左右邻居三张头像。

本次复查结论：

- 三人物窗口的逻辑没有发现缺陷，以下几点核对无误：
  - 先释放离开范围的，再加载新的；
  - 过期的预载会作废；
  - 退出时全部释放；
  - 和选角卡片等界面共用 Sprite 时引用计数正确。
- 历史证据不能代表当前主仓：
  - `role-create-window-20261109/live-window.tsv` 跑的是 peer 副本里的旧 `RoleFlowUi`（12 人、没有职业默认槽）。
  - 第一轮“冷缓存开页中位数 20.8 ms”的三个冷样本里，有两个根本没加载头像；真正冷的只有一个，回调 47 ms、首帧 90 ms。
  - 历史回归用的 `QdaoCharacterCatalog` 还含雷法少年。
- 头像被导入设置强制为不压缩、无 mip，每张 1024² = 4 MiB。客户端 UI 要求与效果图逐像素一致，所以不能靠压缩提速。

## 2. 本轮改动

### 阶段 0：测量手段（不改行为）

- `Assets/Scripts/World/CreationPortraitProfiling.cs`（新文件，GUID `8ce22786f9264a5fa688c138d99ca8bb`）：创角页的 `ProfilerMarker`，见下表。名字是测速驱动的读取契约。
- `RoleFlowUi`、`QdaoCharacterCatalog` 在对应位置挂上这些埋点。
- `Assets/Scripts/App/DevRoleCreatePerfProbe.cs`（新文件，GUID `30931a78df074737ab410804fe6cb816`）：测速驱动。
  - 只编进编辑器和开发版。
  - 不登录：把假角色列表交给真实的 `GameClient.PlayerChooser`，再调用真实按钮。
  - 结束时点“返回”取消，不会创建角色。
- `QdaoOriginalHdResourceIndexBuilder.RebuildAll` 结束时打一行英文日志：`RebuildAll verified N folders in X ms`，用来量编辑器域重载（含进 Play）时的重建耗时。

| 埋点 | 覆盖范围 |
|---|---|
| `RoleCreate.Open` | 进入创角页 |
| `RoleCreate.RefreshChoices` | 重新解析全部候选 |
| `RoleCreate.RefreshSelection` | 刷新当前选择，含当前头像的同步加载 |
| `RoleCreate.ResolveAppearance` | 单个人物的创角解析 |
| `RoleCreate.LoadPortraitTexture` | 一次头像纹理加载 |
| `RoleCreate.PreloadNeighbour` | 补载一位邻居 |
| `RoleCreate.ReleasePortrait` | 归还一份租约 |

### 阶段 1：三处小改（不动 UI 布局、完整性校验和共享引用规则）

1. **邻居预载按翻页方向排序，已缓存的邻居不占帧**（`RoleFlowUi.ScheduleCreationPortraitPreload / PreloadCreationPortraits`）。
   - 原来固定“先上一位、后下一位”。往后翻时，上一位就是刚才的当前人物，白占一帧，新的下一位要到第 2 帧才加载。
   - 现在往后翻先补下一位，往前翻先补上一位；开页、切职业或性别时仍是先上后下。
   - 点击那一帧仍然不碰邻居；之后每帧最多真正加载一位。
2. **只对“可新建但资源没就绪”定时重试**（`RoleFlowUi._creationAwaitingResources`）。
   - 玄霄的职业默认外观男女都不可新建，这是有意的，有测试守着。
   - 原来停在这个槽位，每秒会把 11 人全部重新解析一遍，并对约 330 KB 清单做 SHA-256。
   - 不可新建是代码写死的，等多久都不会变，所以现在只在资源版本变化时刷新。
   - 候选名单为空时，名单本身还在准备，仍定时重试。
3. **索引查找改成字典**（`QdaoOriginalHdResourceIndex.FindEntry`，`TextureMatches` 改用它）。
   - 原来每次查找都扫 137 项，首次开页约 10 万次字符串比较。
   - 重复路径仍以先出现的为准，与逐项扫描结果一致。
   - `entries` 整个数组被替换时，查找表跟着重建。

没有采用“按 `ResourceRevision` 缓存候选快照”。`CreationPreviewKeepsVerifiedV13UntilOwnHdIndexIsReady` 等测试要求：HD 索引就绪或清单变化时，即使资源版本号没有推进，也必须被识别出来。按版本号缓存会破坏这条契约。

新增测试（都加在现有测试文件里，没有新增 .meta）：

| 测试 | 文件 | 在旧实现上的结果 |
|---|---|---|
| `CreationWindow_TravelDirectionLoadsMissingNeighbourOnFirstLaterFrame` | `Assets/Tests/PlayMode/QdaoCreationPortraitWindowPlayModeTests.cs` | 往后翻那半段会失败 |
| `CreationResources_UnreleasedProfessionDefaultDoesNotRescanRosterPeriodically` | `Assets/Tests/PlayMode/QdaoRoleIdentityPlayModeTests.cs` | 会失败 |
| `IndexEntryLookupKeepsFirstRowAndFollowsReplacedInventory` | `Assets/Tests/EditMode/Tianyong/QdaoOriginalHdAppearanceTests.cs` | — |

## 3. 测速驱动用法

开发版播放器或编辑器都能用，参数如下：

| 参数 | 作用 |
|---|---|
| `-roleCreatePerf <输出目录>` | 必填，用绝对路径。编辑器里也可以改用环境变量 `MMORPG_ROLE_CREATE_PERF=<输出目录>`，测完要清掉，否则每次进 Play 都会跑 |
| `-roleCreatePerfRoles 1:1,3:2` | 账号已有角色，格式“职业:性别[:外观id]”，最多 4 个。默认值同编辑器预览（破军男、丹心女）。`none` 表示没有角色、直接进创角页 |
| `-roleCreatePerfCycles 50` | 退出再进创角页的次数，没有角色时不做 |
| `-roleCreatePerfQuit` | 结束后退出：播放器退出码 0 表示不变量全部成立、1 表示有失败；编辑器里停止播放 |

每次运行会在输出目录下新建 `run-<UTC 时间>-<进程号>/`，写入三个文件：
- `samples.tsv`：每次点击和检查点。
- `frames.tsv`：逐帧的主线程耗时和各埋点耗时。
- `summary.json`：分位数、不变量和机器信息。

驱动依次执行以下场景：
- 冷开页；
- 整环往后、往前各一圈（含职业默认槽）；
- 同一帧连点两次；
- 停在职业默认槽切换职业和性别；
- 退出再进入 N 次；
- 取消退出。

驱动只报告数字和不变量，不判断性能是否达标。判断看第 4 节。

## 4. 验收口径

正确性（必须）：
- 编译通过；指定的 EditMode / PlayMode 集合全部通过，包括第 2 节的三条新测试。
- 测速 `summary.json` 里 `passed=true`，即：
  - 任何一帧窗口都不超过 3 张；
  - 每次退出后持有数为 0；
  - 只以取消结束，没有提交创建。

性能（建议值，参考机待负责人指定；先在开发机上测出基线）：

| 指标 | 来源 | 建议门槛 |
|---|---|---|
| 首次开页 | `open.callbackMs`，10 次新进程 | p95 ≤ 50 ms，且 `open.endOfFrameMs` 那一帧画面里已有当前人物 |
| 再次开页 | `reopen.callbackMs` | p95 ≤ 16 ms |
| 切到已预载的人物 | `switch.hit.callbackMs` | p95 ≤ 2 ms |
| 缺的邻居就位 | `frames.tsv` | 往后、往前翻时，`RoleCreate.PreloadNeighbourMs > 0` 都出现在点击后的下一帧 |
| 预载帧的额外开销 | `frame.preload.mainThreadMs` p95 − `frame.baseline.mainThreadMs` p50 | ≤ 8 ms，超了就做阶段 2a |
| 反复开关 | `closed.textureMemoryDriftMb`，50 次开关 | ≤ 4 MiB |

不要拿历史数字当这一版的基线。历史数字跑的是旧副本，样本也有问题。

## 5. 阶段 2（看数据再决定）

- **2a 邻居改异步预载**：只有预载帧的额外开销超过门槛才做。
  - 只改邻居，当前人物仍同步加载，保证首帧有图，创建按钮的门控也不用变。
  - 复用 `CityTileStreaming` 的模式：`Resources.LoadAsync` + 引用计数 + 加载完成时没人要就回收。
  - 加载中的请求占用 3 个名额；退出时还在加载的请求，完成后回收。
- **2b 首次开页预热**：只有首次开页在播放器里超过门槛才做。在选角页的空闲帧里逐个预先解析候选，只碰元数据，不加载纹理。

## 6. 范围外待办（单开任务）

- **编辑器重建太重**：
  - 每次域重载都在主线程同步重算 14 个文件夹（约 1.2 GB PNG）的摘要；进 Play 也会重载域。
  - 索引写回后，同一文件夹还会再算一遍。
  - 要不要改成“Unity 判定文件变了才重算”，属于完整性策略，需要负责人决定。
- **构建时资源损坏不报错**：现在只删索引，发行包会悄悄少一个人物。是否改成直接让构建失败，需要负责人决定。
- **`LoadPortrait` 走完整解析**：
  - 选角卡片、队伍、邀请、战斗头像都调用它。
  - V13 人物会为一张头像加载 137 张未压缩帧（约 140 MiB），一直留到下次切场景，播放器里也一样。
- **`QdaoBoySpriteAnimator` 的尺寸检查**：为了检查尺寸，加载了一整张 4 MiB 头像。
