# NavMesh authoring tool verification — 2026-11-12

Usage: [NavMeshAuthoring.md](../../NavMeshAuthoring.md). Reproduce with `tools/tests/navmesh_authoring/run_verification.ps1`.

- Unity 6000.6.0f1 isolated actual-source tests: **18/18 PASS**, runner exit 0. Includes official native baking, obstacles and disconnected regions, radius clearance, whole/tiled image import, old island polygon import, editor layout and Undo, stale/art-changed draft refusal, export SHA, and actual Play-mode NavMeshAgent movement around a hole.
- `source-snapshot.json` pins the five source files tested. Runner checks these hashes against the working client again after Unity exits.
- Whole client runtime compile preflight: 457 sources, exit 0, zero errors, using installed Unity 6000.6 modules including AI.
- The already running main Unity project subsequently resolved the official AI package and completed its own script build (`Tundra build success`, 27.19 seconds, 494 items updated). `MmorpgClient.dll` and `Assembly-CSharp-Editor.dll` were written at 03:34:29 and 03:34:28 America/New_York, after the final editor source modification at 03:32:19. This confirms main-project integration compilation as well as the isolated tests.
- Meta/GUID check: 21,191 files, zero errors; six pre-existing unrelated class-name warnings.
- `navmesh-play-route-synthetic-smoke.png` is a **synthetic test scene**, not an acceptance screenshot of a delivered map. Production-map appearance and manually authored production navigation were not validated here.

Server compatibility remains partial: the existing C++ baker read the exported same-source OBJ, but the ground-height spawn probe `(3,0,6)` returned OFF MESH (exit 1). The diagnostic `(3,0.2,6)` returned ON MESH and produced an MSET. The original foot-point check remains failed; no server production files, spawn heights, scenes, or online travel gates were changed. The inspected executable has not been independently proven to match the currently inspected source. Reports preserve both outcomes.

`unity-verification.json` SHA-256: `4968e0e3852f044e296d719143b0b744fa2f631e95ae271660c2010ad077f43e`.

`source-snapshot.json` SHA-256: `a0b85d7239830a987adb2dbb292d0aeb0a7cc5379924fa1c7c9fc53127f2551d`.
