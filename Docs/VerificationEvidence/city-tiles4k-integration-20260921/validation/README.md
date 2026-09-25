# Client validation: 2026-09-21

This evidence verifies the current loader and production import gate. It does **not** approve delivered artwork or claim actual-game city acceptance. No production manifest or city texture was added by this validation.

## Results

| Check | Result | Evidence |
| --- | --- | --- |
| Actual client runtime Roslyn compile | 356 source files, 0 errors, exit 0 at 07:52:56 EDT | `runtime-compile.txt`, `runtime-compile-summary.json`, `runtime-source-hashes.json` |
| Unity 6000.6.0f1 isolated EditMode | 29 passed / 0 failed / 0 skipped, exit 0 at 07:53:34 EDT | `summary.json`, `editmode-results.xml`, `unity-editmode.log` |
| Exact source copies | All seven copied sources/assets match SHA-256; unchanged during the final run | `source-hashes.json`, `invocation.json` |
| Actual 4096 importer regression | RGB24, sRGB, Clamp, Bilinear, unreadable, one mip level; four 2048 overrides cleared | `actual-import-settings.json` |
| Empty production batch gate | Expected exit 2; 0 published appearances, import acceptance false, gameplay acceptance false | `production-gate-summary.json`, `empty-production-gate/production-imports.json`, `production-gate.log` |

The 29 tests comprise the 21 existing `CityTileStreamingTests` and eight new cases: complete 16 x 16 coordinate/viewport contract, real 4K importer reimport, one valid production manifest, four invalid manifest cases (order, city, grid, foreground approval), and the empty-production reporting gate. Existing tests include atomic appearance commit, camera expansion before commit, missing manifest/tile behavior, cancellation/disposal, shared asynchronous requests and last-owner release.

The importer test first imports a generated 4096 x 4096 texture using the exact production `CityTile4KImporter`. It then explicitly sets **Standalone, Android, iPhone and WebGL** to `overridden=true` and `maxTextureSize=2048`, confirms those settings were applied, and also poisons global readability/mipmap/sRGB/wrap/filter settings. A real `SaveAndReimport()` runs the postprocessor again. Assertions inspect the resulting Texture2D and importer and invoke the exact production `ValidateTexture` gate. This proves importer behavior for a synthetic texture; it does not prove the visual quality or import status of any real city tile. No GPU memory or frame-time claim is derived from headless EditMode.

## Isolation and provenance

Unity installation is `E:\Unity\6000.6.0f1\Editor\Unity.exe`, resolved from the **current local csproj**, not a hard-coded old `E:\work` workspace. The test runner is `tools/run_city_tiles_tests.ps1`; the reusable extra test source is `tools/tests/CityTileImportVerificationTests.cs`. The local `.codex-artifacts/city-tiles4k-validation/20260921-075206-524e6a` project is retained for inspection and is ignored by Git.

The main Unity project was never opened by this runner. It copied the current Manifest, Streaming, Importer, existing tests, production verifier, extra tests and one unchanged legacy PNG. The only scene dependency substitute is `TianyongPaintedCity.PaintingWorldRect = (50, 0, 300, 300)`. The actual source derives that rectangle from `TianyongMapDefinition.Width=400` and `Depth=300`; both production source hashes are also in `runtime-source-hashes.json`. There are no real game bootstrap, network, player, navigation, lighting or foreground systems in the isolated project. Minimal assembly definitions and locally cached Unity Test Framework/NUnit packages replace the main project configuration. The copied legacy PNG is used only for the existing Resources lifetime tests.

The fresh batch `VerifyBatch` run used the same isolated project after all tests completed. The synthetic `__import_test` fixture has no manifest and is not one of the seven supported appearances. The empty-production result intentionally remains false; exit 2 is the expected refusal to certify an absent delivery, not a successful real production import audit.

## Reproduce

```powershell
pwsh -NoProfile -File tools/client_compile_check.ps1
pwsh -NoProfile -File tools/run_city_tiles_tests.ps1 -EvidencePath 'D:\luyuan\wuxingqitan\mmorpg-client\Docs\VerificationEvidence\city-tiles4k-integration-20260921\validation'
```

Pass `-UnityExe` if the local csproj and standard Unity Hub path cannot locate an editor. The runner creates a new isolated project each run, archives its prior named output files under `previous-runs/`, and checks copied source hashes again after Unity exits. Existing result XML cannot be reused accidentally by a later failed or incomplete run.

Two setup attempts are retained separately: sandbox licensing returned exit 198 before compilation; after a controlled approved unsandboxed run restored access to the installed Unity license, the first minimal package fixture omitted built-in Physics and failed compile. Adding Physics to the **isolated fixture only** resolved that issue. The successful pre-cleanup run is under `previous-runs/`; the top-level records represent the final reusable runner.

## Still required after complete artwork delivery

Actual production entry, nearest camera, movement across tile seams/four-way intersections, zoom/widescreen/map edges, navigation/spawn points, foreground silhouettes/occlusion/feet/lighting, switching between complete appearances, and target-device texture residency/peak RAM/VRAM/frame time are not covered here. No screenshots from actual gameplay are claimed.
