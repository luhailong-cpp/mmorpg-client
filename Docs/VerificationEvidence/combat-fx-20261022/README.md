# 六组战斗特效接入验收

2026-10-23完成本批客户端接入与离线原生演出验收。接续时六组资源、专属映射和飞行播放器已落盘，本次沿用现有实现，补修高倍率、低帧率下拍计时提前落HP的问题，并完成构建、回归与画面检查。未重复覆盖素材或meta GUID，未修改服务端技能规则、数值或来源记录。

## 实际接入

统一使用3072×384 RGBA高清横条，每组8帧，直通Alpha。导入为Bilinear、Clamp、无mipmap、无压缩、最大边4096；逐帧像素、帧序及SHA256与美术交付一致。

| 特效 | fps | 锚点 / pivot | 行为 |
|---|---:|---|---|
| `hit_star` | 16 | 目标胸口 / (0.5,0.5) | 配合现有受击动作、闪白与后仰 |
| `cast_charge` | 10 | 施法者脚底 / (0.5,0.1) | 聚气，人物动作事件仍决定释放时刻 |
| `xuanfu_luoyao_projectile` | 16 | 胸口轨迹 / (0.5,0.5) | 1201释放后飞行0.5秒，向左镜像，可循环，无结果应用 |
| `xuanfu_luoyao_impact` | 16 | 目标胸口 / (0.5,0.5) | 1201到达后播放，零基3帧触发唯一结果批次 |
| `zixiao_leizhen_telegraph` | 16 | 目标脚底 / (0.5,0.5) | 1202实际1–3目标同拍预示0.5秒 |
| `zixiao_leizhen_strike` | 16 | 目标脚底 / (0.5,0.1) | 同拍雷击，零基3帧整批结算，各目标一次伤害与飘字 |

实际时序由人物释放事件和`BattleTempo`控制，不硬套素材预览的绝对秒数。`BattleSequencer.CanEndBeat`与`BattlePresenter`的施法完成状态让正常拍结束等待命中尾段，避免6x/15fps等粗帧更新下提前落HP或进入下一拍；Abort/Skip仍立即取消、回收，无迟发结果。持续Buff任务尚未改写本批公共入口，本次未改其在制功能。

## 验证结果与证据

| 验证 | 结果 | 记录 |
|---|---|---|
| 六组素材、来源、导入合同 | 通过，实际model/quality保持null | [asset-validation.json](asset-validation.json) |
| Unity专项回归 | 131/131，其中特效集成26/26 | [focused-results.xml](focused-results.xml) |
| 当前共享目录离线编译 | 424个运行时文件，0错误 | [编译记录](main-runtime-compile-20261023.txt) |
| Unity Windows构建 | 成功，0错误、497条警告 | [build-summary.json](build-summary.json) |
| 最终原生演出 | 12/12，退出0、运行错误0、47张PNG写完 | [原生回执](combat-fx-verification.json) |
| 画面人工检查 | 通过：镜像、脚底/胸口、透明、雷击展开与数字层级 | [visual-review.json](visual-review.json) |
| 战斗程序集全量回归 | **965项：920通过、45失败，尚未全绿** | [原始XML](broad-results.xml)、[失败归因](broad-suite-failure-triage.json) |

12个原生场景覆盖1201双向飞行、1202单/三目标、1.5x目标30fps、6x目标15fps，以及飞行/预示阶段的Abort与Skip。6x场景实际首次命中约0.33秒；正常热身场景约1.16–1.17秒。冷启动与GPU读回仍有帧尖峰，不能把截图当作恒定帧率性能基准；15/30fps的确定性时序检查见专项测试。

代表截图：[向左飞行](screenshots/003-1201-west-flight-mid.png)、[向右飞行与聚气](screenshots/009-1201-east-flight-mid.png)、[单目标受击](screenshots/017-1202-one-reaction.png)、[三目标预示](screenshots/019-1202-three-lead-in.png)、[三目标雷击](screenshots/021-1202-three-hit.png)、[三目标伤害数字](screenshots/022-1202-three-reaction.png)。`flight-mid`只是阶段名，不代表精确轨迹中点。

## 验收边界

这是原生Unity播放器中直接调用正式`BattlePresenter`、`BattleUnitView`、`BattleFxPlayer`和飘字池的离线验收，使用每目标100伤害的合成结果，**没有宣称服务器实战结算联调通过**。

隔离工程以`da77a149`为基线，保留本批精确源码与资源，独立缓存，仅移除不参与运行时演出的Editor AI Assistant包。主目录的活动、任务、队伍等并行改动未覆盖、未纳入该隔离整合包；当前共享主目录另外通过了离线编译，但本次没有完整主目录Unity出包。16项本批输入在主目录和隔离工程哈希相同，详见[最终输入](verified-inputs.json)。

131项专项运行后，仅原生验收驱动调整了取图、后台PNG压缩与探针；生产代码和测试未再变化。最终驱动已由本轮构建与12场景验证，未将旧测试冒充最终整包重测。两版记录见[测试输入](tests-inputs.json)与[最终输入](verified-inputs.json)。

45项全量失败集中于角色混合分辨率断言和宠物资源/预览生命周期，未在本批扩修；没有当前改动前的对照重跑，不能声称已经证明它们全部与本批无关。

验收播放器：`E:/work/mmorpg-client/Builds/combat-fx-20261022/client/mmorpg.exe`。原始构建日志、完整47张截图及复跑脚本位于`Logs/combat-fx-dedicated-20261022/`。完整机器可读汇总见[verification-summary.json](verification-summary.json)。本对话未自行stage、提交、切分支或推送。
