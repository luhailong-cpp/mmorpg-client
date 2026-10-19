# 正式主城地图（2026-10-18）

正式联机主城已接入 `Assets/Resources/World/CurrentMaps20261009/main/layout.png`，与八仙岛、东海渔村共同使用 `CurrentMapCatalog` 和 `CurrentMapBuilder`。不再使用下文历史主城分块和 F1–F4 外观；旧沙盒和美术工具仍保留用于历史调试。

当前主城出生点是 Unity `(199, 0, 133)` / 服务端 `(133, 199, 0)`；原始布局图为 1254×1254，导航为 150×150、每格 2 单位，世界矩形 X=50..350、Z=0..300。高清分块仍待完成。贴图、导航及来源记录全部随客户端 Git 交付。

完整资源契约、三图传送、服务端同步和当前验收限制见 [正式联机地图接入](FestivalRegions.md)。当前本机仅完成编译和独立导航校验，尚未完成 Unity 实机联机验收。

## 历史实现说明（以下不代表当前正式地图）

# Historical Tianyong City world map

The production scene now presents a dedicated painted main-city backdrop when
the player enters Tianyong. The existing world-space map remains active behind
the artwork for navigation, networking and collision compatibility, while the
login and turn-based battle canvases continue to render above it. The offline
sandbox still exposes the procedural geometry for movement validation.

It is built for **Unity 6000.5.8f1** and uses Unity's native uGUI stack.

- Scene config: `1` (`0` is accepted only as the current first-enter fallback)
- World size: `400 x 300` Unity units
- Streaming: `8 x 6` chunks, each `50 x 50` units
- Navigation: deterministic `100 x 75` A* grid, `4` units per cell
- Spawn: `(200, 0, 180)` on the central avenue; the runtime positions the
  prefab root at protocol feet coordinates, with the capsule centre `0.9`
  units above it
- Themes: City, Market, Snow and Lantern; press `F1` through `F4`
- Movement: camera-relative `WASD` or left-click pathing
- Main-city artwork: the 6×6 tile set under
  `Assets/Resources/World/Tianyong/SceneTiles6x6/Tiles` (6144×6144 px, 20.48 px
  per unit), laid flat on world `X 50..350 / Z 0..300` (square painting centred
  on the 400×300 play area) and viewed by a straight-down orthographic camera.
  The 64:27 single image is only a fallback when the tiles are missing.
- Painted-city entry zoom: orthographic size `27`. At 1080p this exposes about
  1106 source texels over 1080 screen pixels, avoiding the 2.4× magnification
  and blur caused by the former size `11`. Players can still zoom closer with
  the mouse wheel when they prefer character scale over map sharpness.
- Painted-city navigation: a 150×150 walk mask (2 units per cell) embedded in
  `TianyongPaintedCity.WalkMaskBase64`, generated from the master image by
  classifying beige pavement (bright, low saturation, warm), keeping cells with
  ≥55 % pavement, clipping the outer wall and despeckling twice (overlay:
  `Assets/Art/World/Tianyong/SceneTiles6x6/Previews/tianyong_walkmask_overlay_2048.png`). Procedural
  building footprints only drive navigation for the 3D themes. Every collider
  above `y = 0` is disabled in painted mode; only the chunk ground stays.
- Actors: the qdao 8-direction run sprites (4096×512 HD strips, eight 512 px
  frames per direction at 16 fps, 64 ppu → ~8 visual units tall, feet pivot at
  normalized Y `0.08`, imported uncompressed) face the camera (so they lie flat
  under the top-down view), sort by Z (south in front), names sit under the
  feet (`WorldLabelBillboard`), and a procedural blue ring
  (`TianyongClickMarker`) marks each click-to-move destination. The camera
  follows in `LateUpdate`, after actor movement, while painted-map tiles,
  buildings, foliage and other occluders remain world-static and never inherit
  the actor's run motion.

## Rebuild the standard Unity content

After Unity finishes compiling scripts, run:

`MMORPG > World > Tianyong > Rebuild standard test content`

The command is idempotent and creates or updates all of the following:

- `Assets/Prefabs/App/AppRoot.prefab` — production `AppBootstrap`, world runtime,
  camera, directional light and EventSystem;
- `Assets/Prefabs/World/Tianyong/TianyongDebugPlayer.prefab` — a
  `CharacterController` root with a collider-free capsule visual;
- `Assets/Resources/World/Tianyong/TianyongMapConfig.asset`;
- six referenced materials in `Assets/Art/World/Tianyong/Materials`;
- `Assets/Scenes/Bootstrap.unity` at Build Settings index `0`;
- `Assets/Scenes/World/TianyongSandbox.unity` at Build Settings index `1`.

It also normalizes all five Tianyong textures as repeatable world textures:
Default/2D texture shape, Repeat wrap, Trilinear filtering, mipmaps, anisotropic
level 4, NPOT ToNearest, 2048 maximum size and high-quality compression.

For an unattended rebuild after the project already compiles:

```powershell
$unityExe = "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe"
$projectPath = (Resolve-Path ".").Path
& $unityExe -batchmode -nographics -quit `
  -projectPath $projectPath `
  -executeMethod MmorpgClient.Editor.Tianyong.TianyongProjectSetup.BuildAll `
  -logFile "$projectPath\Logs\tianyong-setup.log"
```

## Enter Play Mode

- Production flow: open `Assets/Scenes/Bootstrap.unity` and press Play.
- Offline map validation: use `MMORPG > World > Tianyong > Open sandbox`, then
  press Play. This scene creates the map and debug player without a server login.
- Sandbox controls: `WASD`, left-click pathing, mouse-wheel zoom, and `F1`–`F4`
  for City, Market, Snow and Lantern themes.

The PlayMode suite physically moves a `CharacterController` over each of the
three collider-backed bridge decks. It also rebuilds every theme, verifies that
the old theme root is disposed, and limits Lantern mode to at most 32 real-time
point lights; the remaining lanterns use emissive materials.

## Runtime hierarchy

`TianyongMapRuntime` loads the world after `GameClient.OnSceneEntered` and builds:

1. the painted main-city backdrop below the game HUD;
2. terrain chunks;
3. stone roads and central plaza;
4. canal and three walkable bridges;
5. city wall and four gates;
6. collider-backed landmark buildings and martial arena;
7. non-blocking market, tree and festival-lantern decoration.

Map geometry and decoration are world-static. In particular, foreground
occluders never move with the player; only the camera follows the local actor,
and that follow step runs in `LateUpdate` so it observes the completed movement
for the current frame.

Textures live in `Assets/Resources/World/Tianyong/Textures`. Their importer forces
repeat wrap, Trilinear filtering, NPOT ToNearest, mipmaps, anisotropic filtering
and high-quality compression.

## Server coordinate contract

The authored client map uses positive Unity `X/Z` coordinates in the ranges
`0..400` and `0..300`. The current server's first-enter message omits
`scene_conf_id`, so the client falls back to config `1` for that value.

Since 2026-09-05 the server owns the spawn: on scene entry it validates the
saved Transform against a navmesh baked from this project's
`TianyongPaintedCity.WalkMaskBase64` (`xuanming-server-mmo/tools/navmesh_baker
--painted-city`) and relocates illegal saves (including the `(0,0,0)` proto
default of a new character) to server `(180, 200, 0)`, which is Unity
`(200, 0, 180)` under `WorldCoordinateConverter` (`server.x = unity.z`,
`server.y = unity.x`, `server.z = unity.y`). The client no longer needs to
relocate silently; `TianyongMapRuntime` keeps a nearest-walkable fallback for
scenes without server navdata and reports it back with a `MoveStop`, and a
`MoveAck` snap that lands off the mask recovers the same way
(`TianyongPlayerController.WarpFromServer`). Movement acceptance runs through
`DevAutoPilot -moveTest` / `tools/run_move_test.ps1`; see
`xuanming-server-mmo/docs/design/nav-spawn-fix-2026-09-05.md`.
