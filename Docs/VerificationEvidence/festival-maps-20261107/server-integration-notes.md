# 九张客户端地图的服务器接入交接

只读审计时间：2026-11-07T09:38:42.595792+00:00。本次没有修改服务器、提交、fetch、构建或部署。

请求的`D:/work/xuanming-server-mmo`不存在；实际仓库为`D:/work/xuanming-server-mmo-cpp`。异机以自己的`<ServerRoot>`替换该绝对路径。

本地基线为`main 26ceb70ca5771c5ab055c2ec7144c1e55f32c528`；origin为`https://github.com/luyuan-cpp/xuanming-server-mmo`。未查询实时远端HEAD，不能把它称为已同步。

## 必须带走的已有工作树差异

- `SERVER_EXISTING_MAP_WORKTREE_DELTA_20261107-v001.patch`：SHA256 `471b980c793c5ce6ae73a9a9b6b451a65d42cef3b66132e768f200cea1159650`，只含`PROGRESS.md`与`cpp/libs/services/scene/spatial/constants/nav.h`相对上述HEAD的差异。
- `SERVER_EXISTING_MAP_WORLD_EXPANSION_SPEED2X_20261101-v001.json`：SHA256 `f07368f555caee97863f342acb41f91b4ef0914061d8af27c206c613ac266e76`；原字节对应`docs/design/world-expansion-speed2x-20261101.json`，复制字节相等。
- 这三项不在该HEAD：已提交速度上限仍为10，工作树为20以容纳客户端18/跟随19.8；1200世界边长/其他地图坐标倍数仅为旧规划，尚非本批9图的地图接入。
- 已逐段读取限定diff和JSON并检查无凭据/私密连接信息；没有读取.env。先核另一机HEAD与目标路径已有修改，再执行`git apply --check <Handoff>/SERVER_EXISTING_MAP_WORKTREE_DELTA_20261107-v001.patch`；不同HEAD人工整合，不能自动套补丁。未跟踪JSON先核冲突后放回原相对路径。

## 当前九图事实

客户端15节庆版/90运行块仍全部`clientOnly=true`。服务器`generated/tables/basescene.json`仅1..21，`world.json`仅1..16，九个目标都未注册。资源的前导零必须保留；数字只能作为拟接入的地图模板ID，绝不是Snowflake实例ID。

|资源ID|拟模板ID|外观|mask列×行/bytes|Unity出生(x,y,z)|服务端出生(x,y,z)|现有域数/可走格|
|---|---:|---|---|---|---|---|
|01001|1001|spring,mid-autumn|103×52/670|80,0,30.4|30.4,80,0|1/250|
|01002|1002|spring,mid-autumn|103×52/670|46,0,28.9|28.9,46,0|1/309|
|05008|5008|spring,mid-autumn|154×103/1983|91.2,0,64|64,91.2,0|1/2313|
|10000|10000|spring,mid-autumn|205×154/3947|116.8,0,82.6|82.6,116.8,0|2/7268|
|19004|19004|mid-autumn|154×103/1983|192,0,88.8|88.8,192,0|1/48|
|21003|21003|spring,mid-autumn|154×154/2965|153.6,0,107.2|107.2,153.6,0|1/3218|
|23006|23006|mid-autumn|103×103/1327|131.84,0,66.56|66.56,131.84,0|1/833|
|28007|28007|spring|154×103/1983|100.8,0,41.92|41.92,100.8,0|1/1481|
|35004|35004|spring,mid-autumn|154×103/1983|140.8,0,91.2|91.2,140.8,0|3/2566|

10000保留两域且龙身跨云通道未打开，画面硬色阶接缝仍视觉待验；35004保留三域；19004只有48个确认安全格，整个林间路线尚未开放。不得为达到连通或上线目标凭空连接、补路。

## 按源码执行的接入顺序

1. 在另一电脑按实际仓库AGENTS→PROGRESS→copilot→scene-navmesh-pipeline读取，核server/main基线与client提交；不要把本机路径或未fetch的origin当可用状态。
2. 先核并整合限定已有工作树patch与原字节规划JSON。保留原21个BaseScene/16个World与存量玩家数据；不要清库、重置角色或套用未落地1200世界尺度。
3. 确认9个五位字符串资源ID与数字服务器模板ID映射；客户端catalog.sceneId当前只是拟定模板数值，绝不是Snowflake实例ID。公开常驻图在BaseScene.xlsx和World.xlsx增相应行，实例图沿现有Dungeon/Mirror语义，不由美术外观推断类型。
4. 以客户端冻结source.json/navigation-plan.json/walkmask.txt的SHA为事实源。每ID春秋共享已验证mask；保留top-left行序、MSB位序、cell2、worldRect/imageRect及最后一行列裁剪。现--mask-base64固定150x150不能直接用。
5. 实现一个显式尺寸/原点/边界的通用烘焙输入并测试，或将同一mask精确生成Unity-Y-up米制平面OBJ走已有--obj。不得重采样/补路/连接云海。按现客户端裁剪公式生成几何、使用--filter-ledges0和既有不收缩语义，并与客户端格边逐点对拍。
6. 逐ID烘焙独立MSET文件，出生Unity探针、正向可达点对、阻挡点/边缘/不可达跨域都要验证。现有--probe只验证最近poly，不能代替路线或负探针。当前10000两域、35004三域、19004仅48安全格必须原样保持并明示覆盖限制。
7. 源BaseScene id/nav_bin_file/spawn_x,y,z填写9行。spawn将Unity(x,y,z)唯一转为server(z,x,y)，不要重复换轴；nav_bin_file按GetDataRootDir()+相对路径核实际运行目录。
8. 增加9目标必需导航就绪门禁：现NavigationSystem坏mesh只跳过注册，而缺nav查询/出生路径会fail-open，不能因此认定场景被拒绝。新目标须缺/坏/出生不符/错误SHA时拒绝开放或建场；保留兼容旧场景行为，补回归。
9. 源schema改动才增字段编号、由工具重生所有语言；只加表行通常不需改网络协议。运行dev.bat gen会写C++/Go/Java及兄弟client生成目录，先核输出，禁止手改generated或复制并行协议。
10. 构建baker/C++/相关Go服务，跑旧21场景与新增9ID回归。启动日志须逐ID真正nav registered且spawn精确正确，进行真实登录/EnterScene/换图/重登/移动纠偏与40s路线口径检查；端到端到达以NotifyEnterScene+ActorCreate/坐标为准，非发送回执。
11. 春秋是同scene_config_id的客户端外观选择，当前协议无节庆网络场景需求；不能给未交普通外观造默认。服务器验收完成前保持clientOnly/authoritativeServerRegistered等现有未接受字段，最终两端成对发布才更新。
12. Java完整服务器按AGENTS§12同步共享契约并登记PARITY；本机指定Java仓库缺失，当前标待做。此交接不部署、不推服务器，也没有服务器构建/实机PASS。

## 准确源入口

|用途|服务器相对路径与行|
|---|---|
|BaseScene id uint32/nav_bin_file/spawn_x,y,z double in server Z-up|`data/schema/basescene_table.proto:12`|
|World.scene_id foreign key to BaseScene.id|`data/schema/world_table.proto:12`|
|Edit source xlsx+schema, regenerate; no generated hand edits|`data/AGENTS.md:33`|
|Exporter sandbox/lint/generation/test commands|`data/AGENTS.md:100`|
|Generated JSON/binary/proto output directories|`tools/data_table_exporter/exporter_config.yaml:21`|
|Deploy outputs to C++/Go/Java and sibling client; verify paths first|`tools/data_table_exporter/exporter_config.yaml:44`|
|scene_config_id template vs uint64 scene_id runtime Snowflake|`proto/scene/scene_info.proto:4`|
|Existing client EnterScene request/notification|`proto/scene/player_scene.proto:13`|
|scene_id=0 allocates by scene_conf_id; zone/session/owner routing|`proto/scene_manager/scene_manager_service.proto:78`|
|Existing entry copies config ID and calls RequestSceneChange; do not bypass lifecycle|`cpp/nodes/scene/handler/rpc/player/player_scene_handler.cpp:151`|
|Changed map uses destination spawn; notify only after real bind/location|`cpp/libs/services/scene/player/system/player_scene.cpp:60`|
|World table enumerates main-world configs|`go/scene_manager/internal/logic/world_init.go:578`|
|Runtime instance IDs minted by Snowflake and room reserved|`go/scene_manager/internal/logic/world_init.go:187`|
|World classification and on-demand world scene creation|`go/scene_manager/internal/logic/createscenelogic.go:121`|
|Unity/Nav Y-up metres; server Z-up; conversion only inside NavQuery|`cpp/libs/services/scene/spatial/system/nav_query.h:13`|
|MSET v1 struct ABI; mask150x150/cell2/minX50/maxZ300 hardcoded|`tools/navmesh_baker/navmesh_baker.cpp:79`|
|--mask-base64 requires exactly2813 decoded bytes; not generic|`tools/navmesh_baker/navmesh_baker.cpp:225`|
|MSB first top-row geometry; fixed original bounds|`tools/navmesh_baker/navmesh_baker.cpp:253`|
|Spawn probe only nearest poly, not full route/block validation|`tools/navmesh_baker/navmesh_baker.cpp:678`|
|Existing --obj/--probe/--filter-ledges CLI|`tools/navmesh_baker/navmesh_baker.cpp:727`|
|Baker/runtime same ue5navmesh double and ABI; CMake3.22+ C++20|`tools/navmesh_baker/CMakeLists.txt:1`|
|Loads MSET; addTile return is not checked, so need full mesh validation|`cpp/libs/services/scene/spatial/system/recast.cpp:31`|
|After table load invokes ConfigSystem|`cpp/nodes/scene/main.cpp:46`|
|Startup invokes LoadNavBins|`cpp/libs/services/scene/core/config/config.cpp:5`|
|Per BaseScene load and scene spawn probe before nav registration|`cpp/libs/services/scene/spatial/system/navigation.cpp:92`|
|Configured nonzero spawn authority; missing/zero uses old Tianyong fallback|`cpp/libs/services/scene/spatial/system/scene_spawn.cpp:45`|
|No nav remains fail-open; cannot treat skipped nav registration as scene reject|`cpp/libs/services/scene/spatial/system/scene_spawn.cpp:129`|
|Runtime data root and table data dir/format|`cpp/libs/engine/config/config.cpp:318`|
|SceneManager loads tables from config|`go/scene_manager/internal/svc/servicecontext.go:58`|
|Default TableDir ../../generated/tables, UseBinary=false|`go/scene_manager/internal/config/config.go:88`|
|Existing legacy150x150 baker tests; add new rectangular fixtures|`tools/navmesh_baker/test_baker.py:8`|
|All configured scenes must have exact spawn/nav; old distinct-geometry assertions only2/3/4|`cpp/tests/bag_test/scene_spawn_test.cpp:51`|
|Tests must rebuild via-Build and MSBuild /m:1|`tools/scripts/run_cpp_tests.ps1:10`|
|bag/configuration/cross_zone test projects selectable with-Filter|`tools/scripts/run_cpp_tests.ps1:43`|
|dev.bat gen performs tables then proto generation|`dev.bat:569`|
|Java counterpart and PARITY registration required; not implemented here|`AGENTS.md:274`|
|Existing uncommitted speed20/build/dependency/Java limits; not deployed|`PROGRESS.md:6545`|

## 另一电脑构建与测试命令

以下均未在本审计执行；先恢复依赖并核工具链。当前壳PATH未找到cmake/msbuild/go/protoc/mvn/java，所查ue5navmesh和protobuf头文件均缺，baker.exe也不存在。这不等于断言整台电脑未安装工具。

工作目录：`<ServerRoot>`；用途：核基线；本次工作树补丁只绑定26ceb70ca，不表示最新远端已相同。

```text
git rev-parse HEAD
```

工作目录：`<ServerRoot>`；用途：仅预检；基线不同或路径已有修改先人工整合，不自动覆盖。

```text
git apply --check <Handoff>/SERVER_EXISTING_MAP_WORKTREE_DELTA_20261107-v001.patch
```

工作目录：`<ServerRoot>`；用途：导表器只读沙盒等价核验；表改后差异需逐项审，不把预期新增当旧表逐字节相同。

```text
python tools/data_table_exporter/tools/sandbox_export.py --out <outside-repo-temp> --compare
```

工作目录：`<ServerRoot>`；用途：schema语法/迁移等价门禁。

```text
python tools/data_table_exporter/tools/verify_schema_parity.py
```

工作目录：`<ServerRoot>`；用途：源表/schema完成后生成并部署多语言产物；事前核exporter/proto路径和protoc35.1门禁。

```text
dev.bat gen
```

工作目录：`<ServerRoot>`；用途：Windows构建烘焙器；需VS环境/ue5navmesh源码；Linux不传-A。

```text
cmake -S tools/navmesh_baker -B tools/navmesh_baker/build -A x64
```

工作目录：`<ServerRoot>`；用途：baker与runtime同ue5navmesh/ABI/dtReal=double。

```text
cmake --build tools/navmesh_baker/build --config Release --parallel 1
```

工作目录：`<ServerRoot>`；用途：既有150x150兼容/错误长度/坏探针不写文件回归；新增矩形位图另加测试。

```text
python tools/navmesh_baker/test_baker.py
```

工作目录：`<ServerRoot>`；用途：串行完整C++构建；不能用旧exe通过代替。

```text
msbuild game.sln /m:1 /p:Configuration=Debug /p:Platform=x64
```

工作目录：`<ServerRoot>`；用途：含SceneSpawnTest；扩展9目标、坏/缺mesh拒入、路线/阻挡对拍后执行。

```text
pwsh -File tools/scripts/run_cpp_tests.ps1 -Build -Filter bag_test
```

工作目录：`<ServerRoot>`；用途：配置加载回归。

```text
pwsh -File tools/scripts/run_cpp_tests.ps1 -Build -Filter configuration_table_test
```

工作目录：`<ServerRoot>/go/scene_manager`；用途：服务单测；世界路由改动还跑go test ./internal/logic/ -run TestIntegration_ -count=1。

```text
go test ./...
```

工作目录：`<ServerRoot>/go/scene_manager`；用途：编译读取新表的SceneManager；其他实际读取表服务也须按修改范围编译。

```text
go build ./...
```

工作目录：`<ServerRoot>/tools/data_table_exporter`；用途：导表器回归。

```text
python -m pytest -q
```

OBJ分支示例只有在精确转换器已生成对应地图几何后才可执行（当前并无该产物）：

```text
tools/navmesh_baker/build/Release/navmesh_baker.exe --obj <generated-map01001.obj> --filter-ledges 0 --agent-radius 0 --cs 0.25 --ch 0.2 --probe 80,0,30.4 --out data/scene_nav_bin/client01001.bin
```

不得将本批mask直接交给现有`--mask-base64`：它固定150×150、2m格、x起点50、maxZ300，只接受2813解码字节；本批形状/原点均不同。矩形位图转换须沿客户端`tools/festival_map_navigation.py`的最后一行/列裁剪公式，不能按图像高简单向下减2而错位。

## 启动与验收门槛

- 源表/schema经导表器生成，生成产物不手改；`DataRootDirectory + nav_bin_file`必须指向正确MSET。烘焙器与运行时同ue5navmesh/ABI/dtReal=double，不假设不同操作系统二进制布局天然兼容。
- 启动调用链为`cpp/nodes/scene/main.cpp:50` → `core/config/config.cpp:5` → `spatial/system/navigation.cpp:92`。逐ID检查`nav loaded`、`nav registered for scene`与精确spawn；只打印日志/加载成功不够。
- 当前坏mesh是跳过导航注册，玩家无导航会fail-open；九个新目标须新增明确就绪门禁并验证缺文件/坏SHA/空或部分mesh/出生不符均不能开放。`recast.cpp:90`未检查addTile返回，单个spawn成功不能证明其余tile、阻挡或跨域正确。
- 除正向出生/路径探针，必须核阻挡点、边缘不穿越、多域保持分离、原21场景兼容、同图重登保位、跨图采用目标spawn、真实EnterSceneS2C及ActorCreate。当前烘焙`--probe`只查最近poly，不包含负探针或路径对。
- 所有客户端共享配置/协议变更须与C++/Go/Java消费端同批生成和验证。Java完整服务器另库按AGENTS§12登记PARITY，本机声明路径缺失，仍待做。服务器未验收前不把clientOnly改成权威发布。

详细JSON包含逐外观资源/位图/导航SHA、当前生成表SHA、完整源码证据SHA、工作树补丁绑定、未验证范围。
