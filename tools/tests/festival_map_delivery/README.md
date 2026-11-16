# 正式节庆地图的生产解析与寻路验证

此程序直接编译链接生产 `MapDeliveryContract`、`CurrentMapCatalog`（含 `CurrentMapData`）、`AuthoredFestivalAssets`、`AuthoredMapRegistry`、`CurrentMapTilesManifest`、`CityTileManifest`、`TianyongNavigationGrid`、`IMapNavigation` 和 `TianyongMapDefinition`。矩形、向量、数学运算来自实际 Unity `CoreModule` 程序集，手绘资源类型引用实际 `AIModule`。

仅原生引擎入口使用本地适配器：`JsonUtility` 由 `System.Text.Json` 按公开字段解析；`Resources` 只读本地正式文件；`Texture2D` 只读取 PNG 头尺寸。便携进程明确设为非 Editor、非 Development，手绘 NavMesh 本机模式不启用，也不声称已验证其原生运行。没有复制导航或清单算法，也没有运行 Unity、渲染图片或发送任何服务器请求。

在客户端根运行（路径按实际 Unity 安装传入）：

```powershell
dotnet run --project tools/tests/festival_map_delivery/FestivalMapDelivery.csproj --configuration Release -p:UnityManagedPath=D:/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Managed/UnityEngine -p:ArtifactsPath=D:/work/mmorpg-client/.codex-artifacts/festival-maps-20261106/managed-checks -- D:/work/mmorpg-client D:/work/mmorpg-client/.codex-artifacts/festival-maps-20261106/managed-checks/result.json
```

默认检查早期 3 图、5 外观、36 张运行图；验证当前 9 图、15 外观、90 张运行图时，在结果路径后传入 `9 15 90`。检查清单解析和 SHA/尺寸，全部导航格与真实运行采样器一致；生产 A* 和平滑路径不得穿过阻挡，云海三个平台之间不得生出路线。同地图春节/中秋共享完全相同导航，客户端独立地图保持禁止联机传送。还验证坏场景身份、缺导航和缺块清单被生产代码拒绝。

逐图资源检查以 2026-11-06/07 九图的本机联调白名单为范围。程序加载、校验完整已安装地图目录，其余批次应运行各自的接入验收。

线上定义选择回归使用显式标注的合成注册表输入，不生成或冒充八景岛素材：待发布的场景 2 外观可由离线查找取得，`FindForOnline` 始终保留旧岛；没有旧联机定义的 client-only 地图返回空；正式发布后才选用新定义。这些解析检查不代表实际服务器入场、传送窗口或引擎渲染已通过。

这是脱离 Unity 原生进程的生产代码验证，不等同于实际引擎画面、角色碰撞或服务端导航验收。
