# 人物八方向动作核查与站姿修复（2026-09-13）

用户反馈"方向的动作不对"，并新出了一批序列图（`E:\work\image\qdao_chibi_roster_v12`）。本文记录核查结论、本轮修复、验证结果和仍需素材的部分。证据目录：`VerificationEvidence/qdao-roster-20260913/`。

## 当前状态补记（2026-09-13）

2026-09-13 当前状态：用户已明确指出道家造型、走路方向与姿势、脸型与大小三项均不符合预期。后续批量制作与发布暂停，目前仅制作吕洞宾自然走路样板，待用户复核后再决定是否扩展。内部数值/视觉记录或运行测试通过，不等于用户美术验收通过；25狮鼓护卫虽内部标记passed，仍未接入，也未获得本轮风格认可。

游戏当前实际接入V12的仅有24吕洞宾、26桂香药婆、29何仙姑、30韩湘子；23灯穗小使、25狮鼓护卫、27墨鸢游侠、28月兔机关师仍使用同ID的V11。已接入的四位也在本次造型、步态和比例复核范围内，不能把已接入状态表述为用户已认可。

停步修复的当前定向证据为`Docs/ArtEvidence/v12-stop-fix/playmode.xml`：2026-09-13 15:04:04–15:04:12 UTC，8/8 PlayMode Passed、0失败，包含`V12_StoppingAtDifferentPhases_ShowsDirectionIdleOnTheNextFrame`。这验证停下后的下一帧切至对应方向独立站姿，不能证明美术风格、自然步态、脸型大小已符合用户要求。

以下第1–7节保留当时V11方向/步态核查和早期站姿补丁的历史记录，包括当时测试数量、03:00素材清单及待办；不代表当前V12全部朝向、步态或原画通过验收。三位及四位V12时期的121/121 EditMode、7/7 PlayMode亦属于各自历史范围，见`qdao-roster-v12-integration.md`，不是当前停步修复后的新全量测试。当前版本与工作范围以上述补记为准。

## 1. 结论：方向没错，错的是步态和站姿

分三层核对，三层都对：

| 层 | 核对方式 | 结果 |
| --- | --- | --- |
| 代码映射 | 行走向量 → `QdaoBoySpriteAnimator.DirectionNames` 条带（N=走远背面，S=走近正面，E=右侧面，W=左侧面） | 正确 |
| 美术朝向 | 8 个 V11 角色 × 8 方向第 01 帧对照表 [v11-facing-frame01.jpg](VerificationEvidence/qdao-roster-20260913/v11-facing-frame01.jpg)，8 名复核员逐帧目视，8 名反方复核员复核 | 64 组朝向都在正确的那一侧，同方向 4 帧无转身/镜像。唯一保留意见：韩湘子 SW 的转角接近纯侧面 W（约 65°），不是 SE（约 15°）的镜像，左右两侧转角不对称 |

反方复核员另外发现一类原复核漏掉的问题：**帧间整图横向平移**。同一方向相邻两帧之间整个人在画布里左右跳 40～64 px（灯穗小使 W 向头部质心 293→248→300→236），播放时每 120 ms 来回抽一次。灯穗小使 E/W/NE/NW/SE、狮鼓护卫 E/W/SW、桂香药婆 SE/E/W/NW、何仙姑 E/W 都有；15～27 px 以内的可接受。这同样只能靠 V12 重做时用共同缩放和脚点平移对齐解决，V12 流程本身已经这样设计。
| 实机 | 沙盒八方向走位截图 + 日志里动画器选的精灵名 [runtime-directions-before.jpg](VerificationEvidence/qdao-roster-20260913/runtime-directions-before.jpg) | 走 N 用 `run_N`，走 E 用 `run_E`，8 向全部对应 |

看起来"不对"的是两件事：

**问题一：走路不换腿。** V11 每方向只有 4 帧，两个触地帧（01 和 03）大多是同一条腿在前，循环播放像单腿蹦。客观判定（最低 40 行脚部按左右分两团，触到最低行的是前脚）：正面、背面和斜向共 33 个可判定方向里，**25 个 01/03 前脚同侧**。E/W 纯侧面分不清近远腿，肉眼不明显。逐方向数据见 [v11-gait-metrics.csv](VerificationEvidence/qdao-roster-20260913/v11-gait-metrics.csv)。这是素材问题，代码修不了；只能靠 V12 的八帧真步态。

**问题二：停下来定格在劈腿。** V11 没有独立站立图，`Appearance.ContactFrame = 0`，停步就显示走路第 01 帧。64 个方向里 **47 个第 01 帧是大跨步**（脚跨度是该方向最窄帧的 1.5 倍以上；吕洞宾 E 向 197 px 对 59 px）。这一条本轮修了。

## 2. 本轮修改

**用 V12 站立图给 V11 角色补独立站姿。** 用户的 V12 `idle.png` 是洋红底 4×2 原画，格序 N NE E SE / S SW W NW。处理：去洋红并去溢色，逐格取最大连通体，按该角色 V11 走路 32 帧的平均身高做一个公共缩放（不逐帧适配），脚点对齐到 (256, 471)，输出 512×512 RGBA 到 `Assets/Resources/World/Characters/QdaoRosterV11/<id>/idle/<DIR>.png`，.meta 复制走路帧的导入设置。

只接了 **吕洞宾、何仙姑、韩湘子** 三人：他们的 V12 站立图和 V11 走路图是同一套衣服（对照 [idle-vs-walk01-24_lu_dongbin.jpg](VerificationEvidence/qdao-roster-20260913/idle-vs-walk01-24_lu_dongbin.jpg) 等三张）。灯穗小使、狮鼓护卫、桂香药婆的 V12 是"道家服饰修正"换装版，上衣颜色、肩甲、围裙都不同，混进 V11 走路会在停步瞬间换衣服，见 [idle-vs-walk01-others-costume-drift.jpg](VerificationEvidence/qdao-roster-20260913/idle-vs-walk01-others-costume-drift.jpg)，等 V12 整套一起上。

代码改动两处，都是加法：

- `QdaoCharacterCatalog.Appearance.HasDedicatedIdle`：V12 恒真；V11 在 8 张 `idle/<DIR>` 都存在且为 512×512 时为真，首次访问探测一次，缺任何一张都按没有处理。`IdleResourcePath` 因此自动指向站立图，战斗侧 `BattleArtCatalog` 同样受益。
- `QdaoBoySpriteAnimator.LoadFrameSet`：帧集缓存键追加 `:idle`，避免同一角色在有无站立图两种状态间串缓存。

对应测试改了两处断言：V11 基线的 `HasDedicatedIdle` 改为"等于其 8 张站立图是否齐全"；拒绝升级记录后的回退仍等于基线值。

**验收脚本顺带修一处误报。** 节庆版大殿占 z 196.5..241，最近可走点只搜 8 格（16 u），原来点屋顶 (200, 215) 距殿南沿 18.5 u，搜不到就报整轮失败。改点 (200, 207)。

## 3. 验证

| 检查 | 结果 |
| --- | --- |
| 离线编译 | 268 个文件，0 错误 |
| 出包 + 自动走位 | 136/136 张，RESULT=PASS，0 失败 |
| 停步精灵 | 8 向停步 0.7 s 后全部为 `<id>_idle_<DIR>_00`，见 [stop-pose-before-after.jpg](VerificationEvidence/qdao-roster-20260913/stop-pose-before-after.jpg)、[runtime-directions-after.jpg](VerificationEvidence/qdao-roster-20260913/runtime-directions-after.jpg) |
| EditMode | 525/526；唯一失败是并行会话未提交的组队界面用例 `TeamWindowTests.OpeningSwitchingTabsAndClosing_ReleasesOnlyThisWindowsInputBlocker`，与本轮无关 |
| PlayMode | 18/18 |
| 目录/外观/战斗外观用例 | `QdaoCharacterCatalogTests`、`QdaoAppearanceVersionTests`、`BattleRosterAppearanceTests` 全部通过 |

出包用的是排除了并行会话编译错误文件 `Assets/Editor/TeamUiVerification.cs` 的工作区快照，其余文件与工作区一致。

## 4. 逐角色步态复核

8 名复核员逐帧看图，另 8 名反方复核员专挑漏判误判；每份反方结论至少亲自看了 4 个方向的全部原帧。逐条原文见 [v11-review-workflow-result.json](VerificationEvidence/qdao-roster-20260913/v11-review-workflow-result.json)，汇总 [v11-review-verdicts.csv](VerificationEvidence/qdao-roster-20260913/v11-review-verdicts.csv)。

### 附表：8 名复核员 + 8 名反方复核员的净结论

| 角色 | 朝向 | 4 帧同向 | 步态不交替的方向 | 反方推翻/补充 | 最差方向 | 反方净结论 |
| --- | --- | --- | --- | --- | --- | --- |
| 23_lantern_courier 灯穗小使 | 对 | 是 | E S SW W NW | 推翻 3 / 补充 6 | W | 朝向本身没有问题:8 个方向 ×4 帧我全部亲自看过,N/NE/NW 背面(NE 头转右、NW 头转左),E 右侧面、W 左侧面,S/SE/SW 正面(SE 眼神偏右、SW 偏左),每个方向 4 帧朝向一致,没有镜像或转身;32 帧最低不透明行确实全在第 471 行。真正的问题在步态和帧间位置两类。(1)最严重是 W … |
| 24_lu_dongbin 吕洞宾 | 对 | 是 | SW W | 推翻 5 / 补充 5 | NE | 有实际问题,但分布和原结论不同。朝向:8 向第 01 帧都符合标签,各向 4 帧内无转身/镜像/正背互换——同意。真正的步态问题落在 5 个方向:(1) SW:01/03 同一站位(角色右脚在前)、02/04 同一只脚(角色左脚)后抬,角色左脚从不在前、右脚从不离地,只有半个周期;(2) NE:01/02/03 三帧右… |
| 25_lion_drum_guard 狮鼓护卫 | 对 | 是 | N NE E SE S SW W NW | 推翻 2 / 补充 6 | E | 朝向本身没问题:八个方向 4 帧头部朝向都符合标签(N/NE/NW 背面、E 右侧脸朝右、W 左侧脸朝左、S/SE/SW 正面),脚底最低点全部在第 469-470 行,没有浮空或沉底。实际问题全在步态和帧间一致性,而且比原结论更糟一点:(1) 同一只脚反复抬——N 01/02/03 都抬屏幕右脚只有 04 换脚;S … |
| 26_osmanthus_healer 桂香药婆 | 对 | 是 | NE E SE S SW W NW | 推翻 3 / 补充 4 | SE | 有实际问题。朝向没有问题:8 个方向 32 帧的头部转向、背篓位置都与标签一致,32 帧最低不透明行全部是第 471 行(SW/03 为 470,差 1px)。问题全部落在动作和一致性上,按方向和帧号列出:(1) SE:01≈03、02≈04 只有 2 个姿势;帽心 222/271/204/266 帧间横跳最大 67p… |
| 27_ink_kite_ranger 墨鸢游侠 | 对 | 是 | E SE SW W | 推翻 4 / 补充 4 | SE | 朝向没有问题:八个方向 32 帧全部与标签一致,NE 四帧脸都在画面右、马尾向左,NW 四帧脸都在画面左、马尾向右,没有任何一帧转身或镜像;脚底最低行全部在 469-471。真正的动作问题按严重度:(1) SE——02/04 顺序反了:SE01 右脚在前 → SE02 抬右膝(前抬、鞋底朝观众) → SE03 右脚后蹬… |
| 28_moon_rabbit_artificer 月兔机关师 | 对 | 是 | NE E SE S SW W NW | 推翻 6 / 补充 5 | SW | 朝向本身没有问题:8 个方向的 01 帧都符合约定,每个方向 4 帧内没有转身/镜像/正背互换,脚底都落在第 470 行附近(唯 S 四帧书包偏露在画面右侧、身体略偏 SW,轻微)。真正的动作问题有两类。第一类是单腿步态,肉眼可见:SW 最差(01/03 逐像素同姿、画面左脚永远支撑,02 和 04 抬的都是画面右脚,… |
| 29_he_xiangu 何仙姑 | 对 | 是 | E SW W | 推翻 4 / 补充 4 | SW | 有实际问题。朝向 8 个方向全部符合标签、方向内 4 帧朝向一致,32 帧最低不透明像素全在 471 行(红线),这两项原结论没错,我复核也一致。动作层面按严重度:(1) SW 步态不交替——SW02 抬屏幕左腿(膝在 x≈225)、SW04 也抬屏幕左腿(膝在 x≈215),SW01/SW03 都是屏幕左脚在前踩线、… |
| 30_han_xiangzi 韩湘子 | 对 | 是 | NE E SE SW W NW | 推翻 2 / 补充 4 | SW | 有实际问题,且不止步态。  朝向:N/NE/E/SE/S/W/NW 七个方向第 01 帧朝向符合标签,四帧内也没有转身或镜像帧,这点同意原结论。但 SW 01-04 是单眼近侧面(约离正面 65°),不是 SE 那种双眼近正面(约 15°)的镜像——SW 与 W 几乎同角度,SE 与 S 几乎同角度,八方向的左右两侧转… |

### V12 站立图（用户原画）方向顺序核对

| 角色 | 8 格朝向符合 N NE E SE / S SW W NW | 都是站姿 | 同一人 | 问题 |
| --- | --- | --- | --- | --- |
| 23_lantern_courier | 7/8 | 是 | 是 | 第 6 格(SW)朝向错误:实际是正面转右(与第 4 格 SE 重复),缺少正面转左的帧；第 2 格(NE)灯笼拿在右手,其余各格(N/S/NW 等)都是左手,道具手别不一致 |
| 24_lu_dongbin | 8/8 | 是 | 是 | 第 6 格(SW)背上的剑柄完全看不到(按右肩挂剑应在观众左肩上方露出),其余 7 格都有剑柄;小瑕疵,不影响朝向判定 |
| 25_lion_drum_guard | 7/8 | 是 | 是 | 第 6 格(SW)朝向错误:实际是正面转右(与第 4 格 SE 重复),缺少正面转左的帧 |
| 29_he_xiangu | 8/8 | 是 | 是 | 莲花发簪和手持莲苞在各格之间换边/换手:N 格发簪在角色右侧、S 格在角色左侧;莲苞 N/SE/W/NW 在右手、NE/E/S/SW 在左手,始终画在“可见的那 |


## 5. 用户新出的 V12 序列图清单

V12 每人需要：8 方向 × 8 帧走路（8 张 `walk-<DIR>` 或 8 张相位转面）+ 1 张 8 向站立。截至 2026-09-13 03:00：

| 角色 | 站立 | 走路原画 | 状态 |
| --- | --- | --- | --- |
| 24 吕洞宾 | 有 | 相位 01–07，缺 08；另有 walk-E/walk-S | 差 1 张相位 |
| 29 何仙姑 | 有（两版） | 相位 01–08 齐，已转置出 8 方向，s_e 已进处理 | 并行会话正在处理 |
| 30 韩湘子 | 有 | 相位 01–08 齐；walk-E/S | 待转置处理 |
| 23 灯穗小使 | 有（换装版） | 无 | 只有站立和新肖像 |
| 25 狮鼓护卫 | 有（换装版） | 无 | 只有站立和新肖像 |
| 26 桂香药婆 | 无 | 无 | 只有新肖像 |
| 27 墨鸢游侠、28 月兔机关师 | 无 | 无 | 未开始 |

"多了几个动作"指的就是站立：V12 只定义了走路和站立两种动作，没有攻击、施法等其他动作。V12 处理流程归并行会话，本轮没有碰 `E:\work\image` 和 `QdaoRosterV12` 目录。

## 6. 仍需素材或后续处理

1. **走路不换腿**只能靠 V12 八帧真步态；建议优先重做前脚同侧最严重的方向（附表）。
2. 灯穗小使、狮鼓护卫、桂香药婆的站姿要等 V12 整套（走路 + 站立 + 肖像）一起发布，否则停步会换衣服。
3. 复核员另指出道具跨方向换边（吕洞宾 NE/SW 剑柄换肩、狮鼓护卫 W 向腰鼓换边），转向时会闪一下，属素材一致性问题。
4. **V12 站立原画要改两张**：灯穗小使和狮鼓护卫的 `idle.png` 第 6 格（应为 SW，正面转左）实际画成了正面转右，和第 4 格 SE 重复；灯穗小使第 2 格灯笼换到了右手。这两人本轮本就没接入，但进 V12 前要重出。
5. 已接入的三人站立图有小瑕疵，不影响使用：吕洞宾 SW 格看不到剑柄；何仙姑莲花发簪与莲苞在各方向换边换手。韩湘子的站立图是我自己目视核对的（复核员启动时它还没生成），8 格朝向与其 V11 走路图逐格一致。

## 7. 本轮文件范围

`Assets/Scripts/World/QdaoCharacterCatalog.cs`、`Assets/Scripts/World/QdaoBoySpriteAnimator.cs`、`Assets/Scripts/World/Tianyong/TianyongSandboxAutoDrive.cs`、`Assets/Tests/EditMode/Tianyong/QdaoCharacterCatalogTests.cs`、`Assets/Tests/EditMode/Tianyong/QdaoAppearanceVersionTests.cs`、`Assets/Resources/World/Characters/QdaoRosterV11/{24_lu_dongbin,29_he_xiangu,30_han_xiangzi}/idle/`（各 8 张 PNG 及 .meta）、本文与 `Docs/VerificationEvidence/qdao-roster-20260913/`。未提交。
