# Character and pet runtime acceptance

This harness runs the production Bootstrap scene, `GameClient.EnterZone`, live scene RPCs,
and the production character/pet windows. It is compiled only in Editor or Development
builds and attaches only when `-characterPetQaPlan` is supplied. It creates no replacement
scene or UI and never loads mockup images for capture.

1. Confirm no user Unity Editor or game client is running, and coordinate one build owner.
2. Build using `tools/build_client.ps1 -Development -OutDir <new directory>`.
3. Set process environment `MMORPG_UI_QA_PASSWORD` for an isolated `dev_ui_50fdfb_*` local QA
   account (the active local login uses existing DevPasswordAuth with the `dev_` prefix).
   Do not put passwords in plans, logs, shell command arguments, or committed files.
4. Run `tools/run_character_pet_runtime_qa.ps1 -PlayerExe <new mmorpg.exe> -Run
   -PrepareDedicatedAccount`. Omit `-Run` to prepare/review the plan without starting Unity.
   The optional setup uses existing developer GM RPCs for this account's level, pet and
   silver, then learns A10/A40 through normal skill-book RPCs. No database edits are used.
5. Inspect `runtime-report.json`, authority snapshots, `player.log`, PNGs and their JSON
   sidecars. Every PNG records actual resolution, frame, SHA-256, scene, connected player
   ID, visible text and active buttons. Incorrect resolution, ambiguous button selection,
   disabled-button invocation, failed login/GM/skill requests fail the run.

When full Resources packaging is constrained, `-EditorPlay -Run` launches an exclusive
graphical Unity session, opens the real saved Bootstrap scene and enters Play mode. It
uses the same network runner and fixes the actual Game View render size; it creates no
preview scene or capture camera. Reports mark `isEditor=true`. This evidence is real game
execution, but does not substitute for a completed standalone build. `-IncludeBattle`
additionally enters a normal solo PVE battle and verifies the production read-only HUD
view buttons and disabled pet allocation controls before finishing via normal auto battle.

Run `tools/review_character_pet_runtime_qa.ps1 -EvidenceDirectory <run directory>` to
verify capture coverage, file hashes, live-session identity, runtime errors and authoritative
one-point saves. `-ReadOnly` validates runs that omit allocation writes. Requested battle runs
also check actual live battle phases and disabled control metadata at both resolutions.
Full native screenshot visual review remains separate.

基础计划覆盖 2560×1080 与 1920×1080 的六个页面、来源弹窗及宝宝四维加点。四维保存须核对
`before-save-*` 与 `after-save-*` 权威快照。战斗只读状态从真实 PVE 的「人物资料」「宝宝资料」入口检查。
宝宝条目点已接入服务端预览、保存与列表刷新。验收时要分别对照投入前、保存后和撤点后的同一宝宝快照，
核对条目 ID、投入值、剩余点数和状态版本。下文 11 月 17、18 日的缺失数据记录对应当时的旧测试服。

The launcher records build and source provenance, but a green run means functional capture,
not pixel identity. Review full screenshots at native size against the approved art and
report differences. Do not resize/crop/mask differences and then claim an exact match.
If the deployed server lacks the new schema, retain the failed live run; any subsequent
controlled Play-mode test must be labeled separately with its data source and limitations.

For an already provisioned QA account, omit setup switches to avoid repeated GM writes.
`-PrepareBaseOnly` prepares level/pet/silver without attempting modern skill learning, for
checking supported screens and honest missing-data states against an older deployed server.
It does not make that server support the new skill catalog or pet skill ownership contract.

## Verified runtime: 2026-11-17 local time

`D:/luyuan/mmorpg-client/.codex-artifacts/character-pet-ui-50fdfb/live-final-08/`
contains the completed real-network Editor Play run: 49 native PNGs, no Unity runtime
errors, and 102/102 functional evidence checks. The Editor exited normally afterward.
The entry is the saved production `Assets/Scenes/Bootstrap.unity`; the server reports
scene ID `90294489033408512`, config `1`, and the map runtime reports `scene=1 map=main`.
These are full game frames, not preview scenes, composites, or a standalone release build.

The isolated QA player is `5007339388956450590`; pet `4683427316218225503` is a real
level-60 pet. This run performed no GM setup. It saved one constitution point at each
resolution, refreshed the list through the live service and checked the authoritative
change: remaining points `298 → 297 → 296`, allocated total `2 → 3 → 4`.
The extra `+1` shown during battle was an unsaved draft; the final server snapshot remains
allocated total `4`. No other account was used or modified.

| Evidence | Files in the run directory |
| --- | --- |
| Six pages, both resolutions | `01-pet-entry-points-*`, `02-pet-four-stats-*`, `03-pet-skills-*`, `04-character-attributes-*`, `05-healing-detail-*`, `06-shield-detail-*` |
| Draft, save, confirmation dialog | `02-pet-allocation-draft-*`, `02-pet-allocation-saved-*`, `02-pet-unsaved-dialog-*`, authoritative `before-save-*` / `after-save-*` JSON |
| Source overlays and five badge previews | `04/05/06-*-sources-*`, `07-pet-badge-*` |
| Live battle read-only at both resolutions | `08-pet-battle-read-only-*`, `08-character-battle-read-only-*`; recorded phase `WaitingAction` |
| Actual victory and return to scene | `00-after-battle-result-0-1920x1080.png`, `08-returned-to-scene-1920x1080.png` |
| Audit | `runtime-report.json`, `functional-review.json`, PNG JSON sidecars, `provenance.json`, `player.log` |

All scripted clicks in this final run used Unity EventSystem raycasts and pointer-click
events; a covered or disabled button fails verification. The pet battle save, plus, minus
and restore controls were disabled with the draft preserved. Character allocation was
disabled while source-view buttons remained enabled. Actual review confirmed the repaired
1920 pet font rendering and state-label inset, opaque panel interiors, transparent outer
edges, visible close buttons and unobscured content. No exact full-frame art match is claimed.

The deployed service still has the legacy 46 Skill / 26 Buff table set. Consequently the
pet skill page correctly refuses its missing ownership context, and A10/A40 and unsynchronized
source totals show unavailable values. Modern skill selection/learning cannot be certified
from this live run. Pet entry-point allocation has no backend contract and its save remains
disabled. Independent table-driven UI supplements must be labeled separately.

The victory screenshot contains a transient service-level “战斗已结束” toast above the
result panel. The two-round victory, result confirmation and return succeeded; the toast
was absent from the final return frame. This is recorded separately from the zero Unity
exception/error count and is not hidden by cropping. Earlier incomplete runs remain in
their original directories; `live-final-07` was rejected because a pending result modal
covered the pages, and is not the final evidence set.

## Final passive-category increment: 2026-11-18 local time

After the conditional passive-category fix, `pet-skills-live-09/` records a separate normal
Bootstrap live-server smoke run using the existing runtime runner and a read-only plan.
It contains six skill-page captures (attack/obstacle/support at both native resolutions)
plus the initial world frame. All 45 checks passed with zero runtime errors. The active
legacy service still shows exactly three category buttons; their positions and dimensions
match `live-final-08`. All three were reached through real EventSystem pointer clicks.
The initial and final authoritative pet snapshots are identical; no GM, allocation save,
learning or new battle was requested. The dedicated Editor exited normally.

This increment confirms the final patch preserves the deployed legacy page. It does not
certify modern live skill data. The separately labeled table-driven passive-page supplement
and native EditMode results cover the modern conditional fourth category. The original
49-frame/102-check live evidence set remains unchanged.

## 2026-11-22 本机新测试服联网验收

Windows 11、RTX 3070 Ti、D3D11、Unity 6000.6.0f1 Editor Play。独立项目副本打开正式
`Assets/Scenes/Bootstrap.unity`，专用新角色通过本机网关登录。场景与战斗节点运行同一镜像
`sha256:609330be46f03…`，完整摘要见 `server-environment.json`。执行了 207 项计划动作，
保存 63 张原生 PNG，2560×1080 共 34 张，1920×1080 共 29 张。仓库标准检查 116/116，
专项检查 91/91，Unity 记录的错误及异常为 0。

| 验收项 | 两种分辨率的结果 |
| --- | --- |
| 人物治疗与护盾 | 服务端标记两项来源完整。被动「悬壶济世」贡献 15%，治疗显示 1380（基础 1200）。当前护盾加成为 0%，容量显示 960（基础 960），持续两回合。两项装备同类来源上限均显示 32%。 |
| 宝宝条目和四维 | 物理伤害条目 501 投入 1 点，显示 +0.5%。每次保存后重新请求宝宝列表，剩余点数先从 20 变为 19，撤点后回到 20，状态版本递增。四维两次各保存 1 点，剩余 300 → 299 → 298。 |
| 五阶归属 | 服务端归属为「普通」。五个预览依次切换至普通、变异、灵兽、神兽、圣兽，界面持续显示当前阶「普通」。 |
| 实战 | 人物施放 A40，宝宝施放 B05「如意灵环」。首回合结算的 HUD 新增对应 Buff 100400 和 110050。战斗中人物可查看来源，宝宝四维和条目保存控件锁定。两回合获胜，点击正式战果面板返回场景。 |

证据保留在客户端仓库 `.codex-artifacts/character-pet-ui-50fdfb/live-isolated-1122-r3/`。
`integrated-review.json` 列出逐张截图的 SHA-256、原生尺寸校验及保存前后的服务端快照。
`functional-review.json` 记录标准检查。可对照 `04-healing-sources-2560x1080.png`、
`01-pet-entry-saved-1920x1080.png` 和 `08-character-battle-read-only-1920x1080.png` 查看对应窗口。
本次结果覆盖 Windows 编辑器联网运行。新专用角色未写入退役技能，旧存档由服务端专项回归覆盖。
