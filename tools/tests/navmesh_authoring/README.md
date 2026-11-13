# NavMesh authoring verification

This checks the actual editor and runtime authoring sources in an isolated Unity project. It leaves the main client editor and scene open. It uses the client's pinned Unity version and the official AI Navigation 2.0.16 package from `tools/unity-packages`, plus the already restored Newtonsoft 3.2.2 package.

From a PowerShell terminal:

```powershell
pwsh -File tools/tests/navmesh_authoring/run_verification.ps1 -UnityPath 'D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe'
```

The script can discover Unity in the sibling `tools/Unity/<project version>` installation or the standard Unity Hub location when `-UnityPath` is omitted. `-ProjectPath` selects another checkout. `-OutputDirectory` selects a different ignored or temporary artifact location; the default is `.codex-artifacts/navmesh-authoring-verification` inside the client.

The runner copies the current `NavMeshAuthoringDraft`, `NavigationPolygonGeometry`, `NavMeshDraftPreview`, `NavMeshAuthoringAssetIO`, `NavMeshAuthoringWindow`, and `FestivalNavMeshDraftPresets` sources into its isolated project's `Assets`. The runtime sources compile in a minimal assembly named `MmorpgClient`, matching the actual client's assembly name. It embeds local copies of the official packages and launches Unity with `-batchmode -executeMethod NavigationAuthoringVerification.Run`. It deliberately keeps a real graphics device available for the Play camera render. No desktop automation is used.

The checks cover coordinate direction; concave ground, overlapping ground, and obstacle-hole subtraction; triangle winding; invalid polygons; native Unity NavMesh baking and complete/partial paths; disconnected islands; agent-radius clearance; whole-image and row/column tile import; legacy island polygon import; polygon undo/redo and a vertex gesture across the mouse-frame boundary; stale bake rejection; blocked spawn rejection; authoring/OBJ file hashes; rejection of a changed source PNG at Play entry followed by successful exact-byte restoration; native bake identity after an older authoring state is restored; and actual Play-mode NavMeshAgent movement around the obstacle. The runtime checks also confirm that Play uses a private authoring snapshot and rejects a destination in the obstacle.

The image created by these tests is a synthetic checkerboard. Its screenshots verify the tool's rendering and movement only. They do not validate the appearance, navigation, or acceptance of any production map. The imported old island is checked for source identity and authoring data import only.

Outputs include `verification.json`, `source-snapshot.json`, `unity-verification.log`, synthetic Play screenshots, and a `test-export` folder containing the same source geometry used in the Unity bake. A server baker can consume that exported OBJ in a separate paired verification. Passing this Unity check alone does not prove the server's binary navigation format, axis adaptation, deployment, or production-map behavior.

## 手绘平面高度和服务器配对验证

回归包含原始地面脚点（3档水平体素）、实际Play角色高度、共享参数及旧烘焙失效检查。Unity显式开启HeightMesh后，`SamplePosition`和实际角色保留源平面Y=0；原始路径拐点和`CalculateTriangulation`仍可保留Unity近似网格的上浮高度，不能把这些拐点当作应写回角色的高度。

同次运行还输出`paired-unity-queries.json`和`paired-exports/`，覆盖洞、断岛、过窄连接、可通连接、小岛和跨tile地图，并检查洞边、地图外边缘及抬高0.2的负例。每份导出包含实际`server-bake-args.json`及其SHA，服务器验证直接消费此数组，不重复手填角色参数。服务器使用当前源码构建的UE5 double ABI `navmesh_baker`和`navmesh_query`：

```powershell
python tools/tests/navmesh_authoring/verify_server_parity.py `
  --unity-report '.codex-artifacts/navmesh-authoring-verification/paired-unity-queries.json' `
  --baker '../xuanming-server-mmo-cpp/tools/navmesh_baker/build/Release/navmesh_baker.exe' `
  --query '../xuanming-server-mmo-cpp/tools/navmesh_baker/build/Release/navmesh_query.exe' `
  --output '.codex-artifacts/navmesh-authoring-verification/paired'
```

脚本校验导出的OBJ、标注和参数SHA，用原始`X,0,Z`出生点烘焙，再重新加载落盘MSET，逐项比较可走判定、可走点坐标、完整路径和直线阻挡。输出`paired-verification.json`、各例烘焙日志及服务器查询JSON，并记录所用可执行文件SHA。所有产物留在指定项目的忽略目录。它验证合成几何的工具链，不代替用户完成绘图后的正式地图、场景mask/范围及联机移动验收，也不要求两种引擎生成逐位相同的三角网。

For a manual rerun of the already prepared isolated project:

```powershell
& 'D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe' -batchmode -projectPath '<output>/verify-project' -executeMethod NavigationAuthoringVerification.Run -navmeshSourceProject '<client checkout>' -logFile '<output>/unity-verification.log'
```

The C# entry point writes its report and exits Unity with zero only after all checks and the actual Play test pass. The PowerShell runner also verifies that the client's source hashes did not change during the run.

保存未完成轮廓的专门回归可使用同一运行器：

```powershell
pwsh -File tools/tests/navmesh_authoring/run_verification.ps1 `
  -VerificationMethod NavMeshSaveVerification.Run `
  -OutputDirectory '.codex-artifacts/navmesh-save-verification'
```

该入口只操作隔离项目中的合成草稿，拒绝在实际源工程中执行；覆盖自交轮廓完成失败不丢点、原始顶点保存和实际资产重载、单点/双点暂存、待修复轮廓阻止旧烘焙继续使用、修复后重烘、Return/KeypadEnter事件、窗口序列化、切图保存及撤销重做。结果写入`save-verification.json`。常规回归还验证两个重叠障碍区按并集扣洞，没有把独立区域之间的重叠误判成同一轮廓自交。

`PrepareFestivalDrafts.cs` is a separate fixture-preparation helper for the three 2026-11-11 festival tile sets. It is not called by the regression runner. Before running `PrepareFestivalDrafts.Run` in the isolated project, copy the actual client PNG files with their original `.png.meta` files to the same relative `Assets/Resources/World/FestivalPreview20261111` paths, and copy the client's `NavMeshAuthoringDraft.cs.meta` beside the isolated runtime script. Keep the runtime assembly name `MmorpgClient`, as prepared by the runner, so both the script GUID and serialized editor class identifier match the main client. This preserves the script and texture references when the resulting `.asset` files are moved back to the client. Pass `-navmeshSourceProject '<client checkout>' -festivalDraftOut '<receipt directory>'` to the Unity invocation.

The helper creates or verifies only blank drafts: no polygons, no spawn, no baked navigation, and unconfirmed local world bounds. It checks source SHA/GUID identity, serialized reload, each window's selected draft, native-pixel view scaling, and actual imported texture dimensions/format/mip/filter settings. It writes `draft-preparation-verification.json`. Existing nonblank drafts are rejected. Installation must copy only previously absent `.asset` and `.meta` files to the matching client paths; existing user-authored drafts must remain untouched.
