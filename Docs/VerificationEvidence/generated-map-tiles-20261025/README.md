# 2026-10-25 当前地图分片与六边形加载接入记录

本次将已生成的完整原生分片接入当前三张联机地图，接缝修复尚未完成不再阻止交付。内部 QA 失败、未知及正式验收状态如实保留；原图完整性、布局身份、图格、4096 尺寸和哈希均按冻结清单验证。

## 资源冻结状态

已冻结 **519 张 4096×4096 原始 PNG**，共 **13,108,398,493 字节**。冻结审计 SHA-256 为 `1a9f42665079bd458eb408b5db50a9753562c5bbfff0ec779ef9c0f912b4c99f`；布局身份、逐图源文件哈希、数量与验证摘要见 [resources.json](resources.json)。

| 当前地图 | 已交付候选数 | 原图字节数 | 网格 |
| --- | ---: | ---: | --- |
| 主城 | 178 | 4,231,320,686 | 16×16 |
| 渔村 | 165 | 4,290,662,985 | 14×14 |
| 八仙岛 | 176 | 4,586,414,822 | 14×14 |

相对上次 151 张交付，本轮新增 **368 个图格**、更新 **22 个图格**，**129 张** PNG 字节不变；旧图格全部保留。新增与更新文件共 **9,840,931,414 字节**。尚无完整分片的 129 个图格继续显示同版布局底图。

519 张源 PNG 均完成全量解码、所有 PNG 块 CRC、尺寸、SHA-256 和 Git blob SHA-1 检查；冻结前再次核对源文件大小、修改时间和独立来源证据的文档哈希。518 张为 RGB，1 张为全不透明 RGBA（整张 alpha 范围 255–255）。原始 PNG 不降采样、不重编码、不改源图，也不要求 Git LFS。

内部 QA 记录为 **131 张通过、50 张失败、338 张未确认**，其中 164 张没有可绑定的独立 QA 文档；交付正式验收数为零，整图未验收。原来源的局部验收状态及其邻居约束保留在逐图证据中。用户已允许接入接缝或美术 QA 未通过的完整候选；合成测试图、验证夹具以及用布局图填充缺失 CORE 的不完整预览仍不接入。冻结审计中的 `rejectedCandidates` 是历史筛选记录，最终集合以 `selected` 为准。

**客户端资源导入及随后自动执行的全量仓库校验已通过，进程退出码为 0。** [归一后的原始结果](import-and-verify.json) 为 `ok: true`，核对 519 张 PNG、13,108,398,493 字节及三份清单的冻结审计哈希。日志采用 UTF-8、LF 换行，SHA-256 为 `84d1ec37ca6dc25b90c17a4753414d363837b3b7deb79e4d7cd655cfb228ca47`；源日志最后写入时间为 `2026-10-25T09:36:37.334735+00:00`，完成结果观察时间为 `2026-10-25T09:42:46.505193+00:00`，后者不表示精确进程结束时刻。

本次执行命令的可移植等效形式（在客户端仓库根目录运行）为：

```text
python tools/import_completed_map_tiles.py --audit .codex-artifacts/map-all-generated-20261025/all-generated-map-tiles-20261025-audit.json --art-root ../image
```

导入命令会自动完成全量校验。普通客户端完整检出可独立复验，不需要本机美术目录或冻结审计文件：

```text
python tools/import_completed_map_tiles.py --verify --project .
python tools/import_current_maps.py --verify --project .
```

导入后再次运行当前地图导航校验，退出码为 0。主城、渔村和八仙岛的 `layout.png`、`navigation.json`、`source.json`、`walkmask.txt` 共 12 个文件均与本批之前的 main 基线 `7595fe6e2b4f8e5672eab4efb8c07102a7ecc1a4` 一致；逐文件 Git blob SHA-1、出生点和步行掩码哈希保存在 [resources.json](resources.json)。这是仓库资源的一致性证明，不表示已经检查线上服务器运行状态。 本机 C++／Java 两套服务端的三图合同、行走掩码、场景出生点及 Git 跟踪状态也已核对一致，见 [服务端导航对齐记录](server-navigation-alignment.json)；未 fetch、未启动服务，不能据此认定线上已加载。

资源完整性、代码和数学检查均不能代替真实 Unity 引擎导入、游戏画面、接缝或整图美术验收；`wholeMapAccepted` 仍为 false，未做实机画面验证。

## 已接入的空间查询

客户端采用 Amit Patel / Red Blob Games 的[官方 C# 源码](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.cs)，许可为 CC0。保留的 `Point`、`Hex`、`FractionalHex`、`Orientation`、`Layout` 位于 [RedBlobHexGrid.cs](../../../Assets/Scripts/World/CurrentMaps/RedBlobHexGrid.cs)，原始完整文件 SHA 与改动记录见 [NOTICE](../../../Assets/Scripts/World/CurrentMaps/RedBlobHexGrid.NOTICE.txt) 和 [hex-verification.json](hex-verification.json)。

适配对象是 C++ 服务端 `GridSystem`：平顶六边形、`size=(20,20)`、`origin=(0,0)`，服务器使用 XY 平面。客户端沿用 `WorldCoordinateConverter` 的轴交换（服务器 X＝Unity Z，服务器 Y＝Unity X），半值舍入显式采用 `MidpointRounding.AwayFromZero`，与 C++ `round` 一致。

正式地图和沙盒均传入玩家脚下世界坐标，查询本格与六邻格，共七格；相机实际视野覆盖的其他格子另外并入。方形 PNG 的矩形与所有相交六边形建立关联，避免仅用分片中心归属导致边缘漏片。初始化仅读取矩形元数据，不加载 PNG，不改变原有 16×16／14×14 分片，更不把整张地图均分成九块。

七格周边查询是空间候选规则；共享纹理缓存最多六张是独立内存预算，包含不可取消的在途请求，加载并发最多两个。可见分片优先，无压力时保留两秒释放滞后；预算满时，已不需要且独占的完成块可立即让位于新可见块。超过预算或未交付区域继续显示同版布局底图。288 MiB 仅为六张 RGB24 原始像素估算，未测量整客户端内存或 GPU 驻留。

## 已完成验证

- 413 个实际运行时代码文件在 Editor 与 Release 配置下均编译通过，错误数为零。
- [CurrentMapHexGridTests.cs](../../../Assets/Tests/EditMode/Tianyong/CurrentMapHexGridTests.cs) 与 [CurrentMapTileStreamingTests.cs](../../../Assets/Tests/EditMode/Tianyong/CurrentMapTileStreamingTests.cs) 两份测试源码编译通过；这不表示 Unity EditMode 用例已执行。
- 引用实际生产 DLL 的独立纯数学程序通过 **32,018 条断言**，包括服务器坐标金标准、正负半值舍入、Unity／服务器坐标往返、六邻格顺序、大矩形跨多个六边形、宽视野边缘、玩家与相机中心分离、14／16 网格、平移后的索引、候选去重及实际可见矩形不遗漏。
- 运行时确实通过六边形索引取得加载候选；新增测试覆盖原一圈方格预取范围之外的六边形邻域分片。完整异步加载测试源码另覆盖预算满时新可见分片立即发起请求、双并发、共享引用和不可取消请求的预算计数。

可移植证据见 [hex-verification.json](hex-verification.json)。源码哈希以 UTF-8 内容统一 CRLF 为 LF 后计算，避免 Git 检出换行设置影响复核；上游源码和生产 DLL 则记录原始字节 SHA-256。

## 尚未执行的验证

本轮 Unity 启动探测受许可证阻塞，日志缺少 `com.unity.editor.headless` entitlement，未执行真实引擎导入、Resources 异步生命周期、游戏画面或 GPU 内存测试。已停止重复启动探测。

较早的 streaming 纯托管程序曾通过 24 条断言；增加预算压力释放断言后的旧程序被 Windows 应用控制以 `0x800711C7` 拒绝加载，因此新增压力断言未执行，也未绕过系统策略。后续独立六边形数学程序的 32,018 条通过结果，不替代该压力测试或 Unity 引擎验证。

具备许可证时，应在完整客户端 Unity 项目中运行上述 EditMode 测试，并从正式登录入口检查跨格移动、缩放、宽屏、接缝、实际纹理导入与内存峰值。当前结果不能表述为完整地图或游戏画面验收通过。
