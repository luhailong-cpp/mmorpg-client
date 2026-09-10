# 天墉城移动体验：核查与补交（2026-09-09）

本报告替代上一轮交付说明。原先列出的八方向待机、名牌底板、右侧三按钮画面和障碍物后方截图已补齐；另外修复了核查中新发现的南北跑步重复姿势、西侧灯柱遮挡及验收脚本误报风险。

最终轮次 **audit_20260909d**：构建成功、0 错误；**EditMode 249/249、PlayMode 13/13**；**134 张有效画面、3,528 次连续离屏渲染边界检查通过**。结论针对下文列出的配置、站位与运行副本。

[完整运行画面](VerificationEvidence/tianyong-20260909/00_00_runtime_hud.jpg) · [八方向待机](VerificationEvidence/tianyong-20260909/idle-directions-review.jpg) · [灯柱前后对照](VerificationEvidence/tianyong-20260909/foreground-before-after.jpg) · [点击反馈](VerificationEvidence/tianyong-20260909/45_04_click_a.jpg)

## 1. 旧交付结论的更正

| 上轮说法 | 本次核查 |
| --- | --- |
| Pet MessageIds 尚未生成，无法编译 | 已过时。接手时 Pet 常量已存在；最终离线编译 141 个运行时 C# 文件、0 错误，Unity 出包成功。没有把并行 Pet 工作归为本轮修复。 |
| 九张缩放图证明四边和全程“零露底” | 旧 final4 为 72 张 PNG，缩放图实际只有 8 张，且集中在西边。13 张边缘/缩放图的边框最多单色占比约 0.1502%；这不能证明未取样时刻，也不能作为严格露底检测器。现以每次实际渲染时的视口和真实地砖边界作几何核验。 |
| 整个绘制剪影禁行，所以无需前景层 | 不成立。新画面复现了西侧小灯柱屋檐被人物鞋尖覆盖。已按原图轮廓加入该灯柱的前景网格，并用同站位开关对照验证。 |
| 旧测试结果可代表最后版本 | 旧测试早于末次修改；本次在当前副本重跑全部测试。新增前景后，布局测试仍严格核对 36 块地砖，另核对前景；桥梁测试先激活当地分块，再核对 Collider 并完成过桥。 |
| 截图脚本打印 PASS 即表示画面有效 | 本轮初次隐藏窗口曾产生灰图，已整轮判废。改为明确调用真实 Camera.Render 后读回 RenderTexture，并交叉核对截图、日志、帧序和用例完整性。旧失败轮不作为最终证据。 |

旧说明原文保留在[历史记录](VerificationEvidence/tianyong-20260909/previous-delivery.md)，其中的已撤回结论不再作为验收依据。

## 2. 实际补齐和修复

**八方向独立待机。** 新增 idle_N/NE/E/SE/S/SW/W/NW，各一张 512×512 透明中性站姿。停止后先完成收势，再显示最后朝向的独立待机。保持 PPU=52、统一脚点，未用运行时缩放掩盖尺寸差异。这是八方向静止站姿，不是多帧呼吸循环。八方向运行时纹理引用与停止行为均已测试。[素材 QA 与生成来源](tianyong-idle-assets-2026-09.md)。

**南北跑步条。** 旧 N/S 各八帧只有五种上身图，导致既有 EditMode 用例失败。现已重新生成并接入两组八阶段跑步条，保留原 .meta / GUID、分辨率和动画推进方式；其余六个方向跑步条不变。两组八帧上身均为 8/8 唯一，alpha>8 的脚点距下缘均为 41px，原测试阈值未放宽。[前后资源、循环预览与 QA](tianyong-run-ns-2026-09.md)。

**名牌半透明底板。** 根据实际字形宽高调整深色底板，无需等待单独美术贴图。空名和禁用时同时隐藏，恢复后正常显示。缩窄过厚描边并加亮字芯，使楷体细笔画在亮石砖上可读。短中文名、八字长名、空名、禁用、恢复均做运行断言与截图。[长名画面](VerificationEvidence/tianyong-20260909/02_00_nameplate_long_cjk.jpg)。

**右侧三个实际 HUD 按钮。** 离线验收加载现有 BattleUiRoot 和 AttributeUiRoot 的真实组件，验证战斗、观战、角色按钮可见、位于屏内且互不重叠，并取得画面。验证模式关闭在线更新/请求；此项覆盖实际组件的布局和绘制，不替代联网按钮交互测试。[HUD 画面](VerificationEvidence/tianyong-20260909/00_00_runtime_hud.jpg)。

**缩放边缘与跟随。** TianyongCameraController 先更新实际缩放，再按当帧窗口限制目标与最终相机位置；Snap 同样重算，窗口过宽时限制实际景别。实际运行在四边四角完成 12→30→12，共八组，另有恢复景别画面。每组包含首次渲染、等待约 0.06 秒后的取样及收敛取样；PNG 编码会推迟后续帧，文件名里的 `006` 不表示精确在 0.060 秒发生，以日志真实时间为准。连续 CSV 同时检查实际景别推进和收敛，不只检查目标值。[西边首次拉远](VerificationEvidence/tianyong-20260909/72_09_zoom_w_out_f1.jpg)。

**西侧灯柱遮挡。** 直接使用原 r03_c03 地图纹理的屋檐/顶饰轮廓，添加按地面基点深度排序的透明网格。原底图、通行掩码及人物碰撞逻辑不变。同机位、同人物脚点的前景关闭/开启图可见屋檐正确覆盖鞋尖；变化集中在灯柱与人物重叠区域。补拍六处障碍物后方及南侧前方观察点。六处站位图本身不是六个有效身体交叠证明；此次新增前景只覆盖已复现问题的西侧灯柱。[同站位前后对照](VerificationEvidence/tianyong-20260909/foreground-before-after.jpg) · [南侧前方](VerificationEvidence/tianyong-20260909/62_07_occluder_south_front.jpg)。

**可重跑验收。** 构建失败不再继续运行陈旧 exe；超时、运行失败、灰图、缺图、重复用例、CSV 断帧、日志不完整均不能生成成功摘要。副本与输出路径限制在预期工作目录，拒绝指向源仓库、相互覆盖或重解析路径的清理操作。原工程编辑器未被验证流程关闭；本轮没有执行 Git 提交。

## 3. 同版本验证与证据范围

| 检查 | 最终结果 |
| --- | --- |
| 版本基点 | 9f80aa028e780230115ec97f9b3a89f0b0f6b961，加当时工作区改动；不是声称已提交的版本 |
| Unity / 图形设备 | Unity 6000.6.0f1，Windows Direct3D12 |
| 运行配置 | 1920×1080，16:9，实际生产地图、角色、材质与 HUD Canvas 的显式离屏渲染 |
| 离线类型编译 / Player 构建 | 141 个运行时 C# 文件，0 错误；Player Succeeded，0 errors、503 warnings |
| 完整 EditMode | 249/249，0 失败、0 跳过；2026-09-09 17:57 UTC |
| 完整 PlayMode | 13/13，0 失败、0 跳过；2026-09-09 17:54 UTC |
| 实际运行 | 退出码 0，RESULT=PASS，134/134 PNG，8/8 缩放位置，6/6 后方站位 |
| 连续帧区间 | Unity frame 4–3531，共 3,528 次显式 Camera.Render，与 CSV 一一对应 |
| 地图边界 | 36 个实际地砖 Renderer，逐帧视口四角与其边界比较；超过 0.001 世界单位容差的帧为 0 |
| 最大几何溢出值 | 0.0000305175781 世界单位，低于容差；不把浮点误差写成绝对零 |
| 构建文件一致性 | 1,144 个源代码、测试、运行资源、配置文件哈希与验证副本一致；清单记录范围与采集时间 |

503 条构建警告仍存在，主要来自已有图形/着色器代码；没有将“构建成功”写成“零警告”。完整日志随证据保留。

相机 EditMode 还覆盖 9:16、16:9、21:9、6:1 四种宽高比以及 30/60/144 FPS 的步长，遍历四边四角和多次缩放；另有 Snap 与 10:1 极宽窗口测试。这些是控制器几何测试，实际截图配置仍只有本表的 1920×1080。

PNG 用于画面复核，灰图检查只排除无效图；它不是逐像素露底识别器。逐帧几何证据针对这次连续显式离屏渲染，不涵盖操作系统窗口呈现、任意未录制配置或全图任意站位。当前保守通行掩码仍然保留；扩大可走范围时，应同时补充前景轮廓和有效交叠用例。

- [验收摘要](VerificationEvidence/tianyong-20260909/coverage-summary.json)、[相机逐帧 CSV](VerificationEvidence/tianyong-20260909/camera-frames.csv)、[播放器日志](VerificationEvidence/tianyong-20260909/player.log)。
- [EditMode XML](VerificationEvidence/tianyong-20260909/EditMode.xml)、[PlayMode XML](VerificationEvidence/tianyong-20260909/PlayMode.xml)、[构建日志](VerificationEvidence/tianyong-20260909/build.log)。
- [构建源文件哈希](VerificationEvidence/tianyong-20260909/built-source-manifest.json)、[134 张原始 PNG 的哈希](VerificationEvidence/tianyong-20260909/original-screenshots-sha256.json)。

## 4. 交付与复跑

本文件及相邻 ArtEvidence / VerificationEvidence 中的报告、关键 JPEG、QA、日志和测试结果一起提供。关键整屏 JPEG 约 260–310 KB，替代上一轮未能传出的两张大图；这里提供本地可携带文件，不声称已经上传到手机或网页。

134 张无损 1920×1080 原图保留在 `E:\work\tmp\citymove_shots\audit_20260909d`；完整 Player 位于 `E:\work\tmp\citymove_player\audit_20260909d`。轻量交付包不包含大型 Player 和全套原始截图，原图哈希清单可供核对。包内保留这份报告的相对链接结构。

仓库仍混有 Pet、界面刷新等并行改动。仅按[本轮文件范围](VerificationEvidence/tianyong-20260909/delivery-file-scope.txt)审阅本次交付；该清单不是整仓提交指令。地图原图、通行掩码、其他六方向跑步条和并行工作文件未被本轮覆盖。

从仓库目录复跑（使用新的轮次名；默认在隔离副本构建）：

```powershell
pwsh -NoProfile -File tools/run_city_move_verify.ps1 -Tag review_20260909 -RunTimeout 300
python tools/summarize_city_move_verify.py --shots E:/work/tmp/citymove_shots/review_20260909 --log E:/work/tmp/citymove_player_review_20260909.log --output E:/work/output/citymove-review-20260909
```
