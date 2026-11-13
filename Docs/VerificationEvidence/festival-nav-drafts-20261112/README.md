# Three prepared NavMesh authoring canvases

Open **MMORPG → Maps → NavMesh 导航编辑器**. The window defaults to the first prepared draft when no draft is already selected and offers three shortcut buttons.

| Draft in Assets/MapNavigationDrafts/Festival20261111 | Source tiles | Canvas pixels |
|---|---|---|
| 26000-mid-autumn.asset | 5 columns × 4 rows, 20 PNGs | 5120×4096 |
| 26008-spring.asset | 4 columns × 3 rows, 12 PNGs | 4096×3072 |
| 26008-mid-autumn.asset | 4 columns × 3 rows, 12 PNGs | 4096×3072 |

Each draft references the existing PNGs in `Assets/Resources/World/FestivalPreview20261111`. All 44 PNG SHA-256 values and decoded dimensions match the previous transfer verification pinned to image commit `06ba7d8169f8d93173973a55b615998f4075564b`. No replacement overview image is used.

Unity 6000.6.0f1 actual import checks preserve the main PNG metadata/GUIDs and show all textures at 1024×1024, RGB24, one mip level, Bilinear. Displaying the whole canvas in a smaller viewport reduces visible detail. The saved GameView layout also records a 0.5706888 view scale, but this is not a measurement of the current live GameView. The editor now offers **1:1 清晰查看**, checked with `zoom * pixelsPerPoint == 1`, plus middle-button pan and Fit.

Drafts were created and reloaded with the actual `MmorpgClient` runtime assembly name, the main draft script GUID, and all 44 original texture GUIDs. Per-draft validation, `OpenDraft`/`CurrentDraft`, window layout/repaint, and native-pixel zoom checks passed. Installed file hashes match the verified serialized assets. No pre-existing draft was overwritten; the final assembly identifier normalization changed only the identifier line and preserved all fields and GUIDs.

These are blank authoring canvases: zero polygons, no spawn, no baked NavMesh. World width 300 and proportional height are local authoring defaults; formal world mapping and effective boundaries remain unconfirmed. The user can trace pixel-space polygons now and confirm world scale before final baking/integration.

JSON receipts in this directory preserve source identity, GPU import results, metadata identity, installation hashes, display analysis, and the zero-error meta/GUID check. Temporary Unity verification logs remain in `.codex-artifacts/festival-nav-drafts-20261112`.
