# 创角页头像窗口：测速与优化（2026-11-11～11-12）

只改客户端，服务端和协议不变，Java 版不适用。11-11 的阶段 0、1 已随客户端 `0ef7a0a6` 提交，编辑器构建器部分随 `8e3687e1` 提交；11-12 的其余改动在 main 工作区。全部未编译、未在 Unity 里运行，待 Codex 验证。

## 1. 背景与审查结论

点“＋ 创建角色”卡住的问题，前两轮已经处理：
- 创角解析改走索引；
- 窗口只持有当前人物和左右邻居三张头像。

11-11 复查结论：

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

负责人 11-11 要求：查出的问题现在就改，不等测速数据，按标准做法定。下面第 2 节就是按这个要求做完的全部改动。

## 2. 改动

### 2.1 测量手段

- **埋点**：`Assets/Scripts/World/CreationPortraitProfiling.cs`（GUID `8ce22786f9264a5fa688c138d99ca8bb`）定义创角页的 `ProfilerMarker`，见下表。名字是测速驱动的读取契约。`RoleFlowUi`、`QdaoCharacterCatalog` 在对应位置挂上这些埋点。
- **测速驱动**：`Assets/Scripts/App/DevRoleCreatePerfProbe.cs`（GUID `30931a78df074737ab410804fe6cb816`）。
  - 只编进编辑器和开发版。
  - 不登录：等登录期预解析跑完，把假角色列表交给真实的 `GameClient.PlayerChooser`，再调用真实按钮。
  - 结束时点“返回”取消，不会创建角色。
- **编辑器日志**：构建器每次运行都打一行英文耗时日志：
  - 域重载时：`Domain reload restored N proofs and verified M folders in X ms`；
  - 全量重验时：`RebuildAll verified N folders in X ms`。

| 埋点 | 覆盖范围 |
|---|---|
| `RoleCreate.Open` | 进入创角页 |
| `RoleCreate.RefreshChoices` | 重新解析全部候选 |
| `RoleCreate.RefreshSelection` | 刷新当前选择，含当前头像的同步加载 |
| `RoleCreate.ResolveAppearance` | 单个人物的创角解析 |
| `RoleCreate.LoadPortraitTexture` | 一次头像纹理同步加载 |
| `RoleCreate.PreloadNeighbour` | 发起邻居异步读取、读完收进窗口 |
| `RoleCreate.ReleasePortrait` | 归还一份租约 |

### 2.2 创角页

1. **邻居改异步预取**（`RoleFlowUi.PreloadCreationPortraits`、`QdaoCharacterCatalog.PrefetchCreationPortraitTexture`）。
   - 当前人物仍同步加载，保证点下去的那一帧画面里就有图，创建按钮的门控也不变。
   - 点击那一帧不碰邻居。下一帧起，对缺的邻居发出 `Resources.LoadAsync`，按翻页方向排序：往后翻先读下一位，往前翻先读上一位；开页、切职业或性别时先上后下。
   - 读完后在主线程同步收进窗口。这时纹理已在内存里，不再读盘。
   - 只做预取，不发租约。租约仍由原来的同步接口发，所有权规则一处不变。
   - 读完时如果不再需要（选择已变、页面已关、组件已销毁），窗口拒收；预取方检查到纹理没被任何租约、旧接口或缓存持有，就立即卸载。
   - 同一身份同时只发一个读取。还在读的时候用户点到它，同步加载会等这次读取完成后拿到同一个纹理。
   - 做法沿用仓库里 `CityTileStreaming` 的模式：异步读取，读完时没人要就回收。
2. **登录期预解析候选**（`RoleFlowUi.PrewarmCreationChoices`）。
   - 客户端启动、挂上选角界面后，每帧解析一位可新建人物的元数据：读清单、核对索引，不加载纹理。
   - 第一次进创角页时，候选刷新只剩命中缓存的读取。
   - 进了创角页就停，剩下的由开页自己解析。
3. **只对“可新建但资源没就绪”定时重试**（`RoleFlowUi._creationAwaitingResources`）。
   - 玄霄的职业默认外观男女都不可新建，这是有意的，有测试守着。
   - 原来停在这个槽位，每秒会把 11 人全部重新解析一遍。现在只在资源版本变化时刷新。
   - 候选名单为空时，名单本身还在准备，仍定时重试。
4. **索引查找改字典**（`QdaoOriginalHdResourceIndex.FindEntry`）。
   - 原来每次查找都扫 137 项，首次开页约 10 万次字符串比较。
   - 重复路径仍以先出现的为准，与逐项扫描结果一致。
   - `entries` 整个数组被替换时，查找表跟着重建。
5. **服务端退回创建后不再连刷两遍候选**（`RoleFlowUi.RestoreRejectedCreation`）。回填只记下身份，位置由紧接着的开页按同一个 id 定位。

### 2.3 同一类问题的其他入口

1. **`LoadPortrait` 不再为一张头像加载整套动画帧**。
   - 原版人物的完整解析 `ResolveAppearance` 对 V13 也只认逐个文件校验过的清单：`TextureMatches` 走索引，并且要求 `IsComplete` 通过。
   - 原来选角卡片、队伍、邀请、战斗头像遇到 V13 人物（00–03），会为一张头像加载 137 张 512² 帧（约 140 MiB），一直留到下次切场景，播放器里也一样。
   - 真正加载动画帧的世界角色和战斗仍逐张核对，缺了就回退。
   - 同身份 V13 的回退（`ResolveFallbackAppearance`）同样要求清单完整。
   - 参数名 `validHdIndex` 改为 `validIndex`，两个版本都适用。
2. **世界角色不再为检查尺寸加载头像**（`QdaoBoySpriteAnimator.LoadFrameSetWithResources`）。
   - 原版人物的头像在选外观时已按清单核对过，动画本身不用头像，所以不再为检查去加载 4 MiB。
   - 旧名单（非原版 V11/V12/V13）没有清单，仍按实图核对。
3. **守卫测试** `CharacterPortraitTexturesAreLoadedOnlyThroughTheCatalog`。
   - `Assets/Scripts` 里出现 `".../portrait"` 路径的文件只能是 Catalog 和三个有理由的例外：宠物、怪物、旧名单完整性检查。
   - 原因：创角窗口和预取会卸载没人持有的头像纹理，只有经 Catalog 取到的引用才在它的持有者名单里。

### 2.4 编辑器与构建

1. **域重载不再整套重算约 1.2 GB 的 PNG 摘要**（`QdaoOriginalHdResourceIndexBuilder.RestoreOrRebuildAll`）。
   - 每个文件夹完整校验通过后，把指纹存进 `SessionState`。指纹由三部分组成：规则版本、三份小文档的内容摘要、运行时索引与 137 张图在 AssetDatabase 里的依赖哈希。
   - 每次重编译、每次进 Play，指纹没变的文件夹直接恢复证明，变了的才全量重算。
   - 编辑器重启后会话状态清空，第一次仍全量校验。
   - 依赖哈希随源文件内容、`.meta` 和导入设置变化。Unity 没察觉的源文件改动也进不了运行时用的导入结果，构建前仍逐个文件重算。
2. **构建器写回索引后不再触发第二次重算**。
   - 写之前登记“写的是什么”。导入回调看到同一路径、同样内容就跳过。
   - 内容不同说明是外部改动（例如 git 检出），照常重验。
3. **构建时资源损坏让构建失败**（`OnPreprocessBuild`）。
   - 已登记人物有一个校验不通过，就抛 `BuildFailedException`，列出文件夹。原来只删索引，发行包会悄悄少一个人物。
   - 编辑器里（域重载、导入）只告警，这些人物在运行时显示为不可用。
4. **完整解析在编辑器里复用证明**（`QdaoOriginalHdResourceIndex.IsComplete`）。
   - 构建器对同一版本已经逐个文件算过摘要时，只核对结构和 `validation.json`，不再每次资源版本变化后把整套 PNG 重算一遍。
   - 没有证明时照旧逐个文件核对。

### 2.5 刻意不改的

- **没有采用“按 `ResourceRevision` 缓存候选快照”**。`CreationPreviewKeepsVerifiedV13UntilOwnHdIndexIsReady` 等测试要求：HD 索引就绪或清单变化时，即使资源版本号没推进，也必须被识别。登录期预解析已经把首次开页的冷开销挪走了。
- **头像不改压缩**：UI 要求逐像素一致。
- **旧接口长期持有头像**：选角卡片、队伍、战斗的长期持有上限是“本进程显示过的人物数 × 4 MiB”，有界。改成显式租约要动五六个界面，收益只是这几十 MiB 能否提前释放，本轮不做。
- **全局资源版本号**：播放器里恒定，编辑器导入时窗口最多重载 3 张，代价可接受。

### 2.6 测试

新增或修改的测试都在现有测试文件里，没有新增 `.meta`：

| 测试 | 文件 | 说明 |
|---|---|---|
| `CreationWindow_ShowsCurrentImmediatelyAndPrefetchesNeighboursAfterTheClickFrame` | `Assets/Tests/PlayMode/QdaoCreationPortraitWindowPlayModeTests.cs` | 改名改断言：原来要求“每帧补一位”，现在要求点击帧不发读取、下一帧按序发两个读取、读完才收 |
| `CreationWindow_PrefetchCompletingAfterSelectionMovedOnIsNotAdopted` | 同上 | 新增：读取挂起期间换了人物，迟到的读取不收、同一身份不重复读 |
| `CreationWindow_TravelDirectionLoadsMissingNeighbourOnFirstLaterFrame` | 同上 | 11-11 新增 |
| `CreationResources_UnreleasedProfessionDefaultDoesNotRescanRosterPeriodically` | `Assets/Tests/PlayMode/QdaoRoleIdentityPlayModeTests.cs` | 11-11 新增 |
| `CreationPortraitPrefetchReleasesUnclaimedTextureAndKeepsAdoptedOne` | `Assets/Tests/EditMode/Tianyong/QdaoOriginalHdAppearanceTests.cs` | 新增：没人收的预取立即卸载，收进租约的保留到租约归还，已缓存或不可新建的不发读取 |
| `OriginalV13NeedsItsOwnVerifiedInventoryBeforeAnyTextureCheck` | 同上 | 新增：V13 清单没校验过就不选、也不逐张核对纹理 |
| `CharacterPortraitTexturesAreLoadedOnlyThroughTheCatalog` | 同上 | 新增守卫 |
| `IndexEntryLookupKeepsFirstRowAndFollowsReplacedInventory` | 同上 | 11-11 新增 |
| `MissingHdIndexDoesNotProbeTexturePixelsAndImportCompletionUpgradesFallback` | 同上 | 改：索引判断现在对 V13 也生效，测试里的索引函数对 V13 文件夹返回 true |

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

参考机：Codex 跑测试的开发机。机器信息以 `summary.json` 里记录的为准；定下最低配置后，在那台机器上按同一口径复测一次。

正确性（必须）：
- 编译通过；指定的 EditMode / PlayMode 集合全部通过，包括 §2.6 的新测试。
- 构建开发版时，日志里有 `RebuildAll verified 14 folders`，并且构建没有因 `BuildFailedException` 失败。
- 测速 `summary.json` 里 `passed=true`，即：
  - 任何一帧窗口都不超过 3 张；
  - 每次退出后持有数为 0；
  - 只以取消结束，没有提交创建。

性能（达标线）：

| 指标 | 来源 | 达标线 |
|---|---|---|
| 首次开页 | `open.callbackMs`，10 次新进程 | p95 ≤ 50 ms，且 `open.endOfFrameMs` 那一帧画面里已有当前人物 |
| 再次开页 | `reopen.callbackMs` | p95 ≤ 16 ms |
| 切到已预取的人物 | `switch.hit.callbackMs` | p95 ≤ 2 ms |
| 预取帧的额外开销 | `frame.preload.mainThreadMs` p95 − `frame.baseline.mainThreadMs` p50 | ≤ 4 ms（读盘在后台线程，主线程只剩发起读取和收进窗口） |
| 反复开关 | `closed.textureMemoryDriftMb`，50 次开关 | ≤ 4 MiB |
| 编辑器域重载 | 日志 `Domain reload restored ... in X ms`，同一会话第 2 次起 | ≤ 500 ms，并且 14 个文件夹全部是 restored |

不要拿历史数字当这一版的基线。历史数字跑的是旧副本，样本也有问题。

## 5. 仍要做的

- 本轮全部改动的编译、测试、开发版构建和测速，见服务端 `docs/PROGRESS.md` 2026-11-12 的创角页条目。
- 如果测出某项不达标，先看 `frames.tsv` 里是哪个埋点，再改对应实现。达标线不随结果下调。
