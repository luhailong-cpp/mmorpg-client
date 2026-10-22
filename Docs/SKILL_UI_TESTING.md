# 技能界面测试入口

本页区分原生 UI 预览与联网功能测试。角色技能查询、学习/升级已接入 `SkillBookClient`，正式启动由 `AppBootstrap` 绑定 `GameClient.Skills`；角色等级、金币与技能等级以服务端回包为准。宠物技能以及截图里的五阶毒、法术吸收和友方普攻二连尚未完成业务接入。Editor 预览始终不连接账号、服务器或玩家存档。

## 当前验收状态（2026-10-22 复核）

当前技能 UI 测试包为 `Builds/play-20261022-skills-ui/`，单开入口是 [Start-Client.cmd](../Builds/play-20261022-skills-ui/Start-Client.cmd)。10/22 02:08（EDT）构建成功，0 错误、504 警告，构建期间源码稳定；[实际程序集核验](../Builds/play-20261022-skills-ui/verification/skills-assembly-source-verification.json)确认 PE/PDB 匹配，14 份关键运行源与冻结输入及本轮原生测试源码一致，包括 `SkillPanel.Fidelity.cs` 的后续布局调整。后续尚未导入的美术或代码不自动进入此包。旧 `Builds/play-20261021-ready/` 已保留，打开旧窗口仍运行旧版本。

[实际资源核验](../Builds/play-20261022-skills-ui/verification/skill-resources-verification.json)也已通过：32/32 技能均有包内 Sprite 到 Texture2D 的有效引用，逐张解码后的像素与冻结 PNG 一致，包内目录 TextAsset 含准确的 32 个 ID。尚未启动此新 Player 实测画面或联网行为。

这是技能 UI 范围的测试包，不能标成全系统验收完成：下面记录的进场 `3023` 和业务切页恢复问题仍需修复；其他任务同日全界面回归 625 项中 612 项通过、13 项失败，已保存 [失败明细](../Builds/play-20261022-skills-ui/verification/other-ui-regression-reference.json)，涉及装备、活动、宠物、帮派、聚宝斋、分线和组队。

| 范围 | 已有证据 | 尚不能据此确认 |
|---|---|---|
| 原生技能面板与战斗选技布局 | 最近一次 [原生截图报告](VerificationEvidence/skills-ui-20261009/native-capture.json) 于 2026-10-21 更新：30 张截图、122 项断言通过，Unity 6000.6.0f1 | 使用 Editor 示例数据，不能代替 10/22 新 Player 的实屏或联网验收 |
| 目标选择 UI | [原生 Unity EditMode 结果](../Builds/play-20261021-ready/verification/target-ui-fixed-results.xml)：15/15 通过，覆盖真实按钮、宠物目标、自身确认、取消、死亡/逃跑、相位变化与断线恢复；此前纯目标范围 22/22 通过 | 原生测试使用假传输，不等于联网施放及效果验收 |
| 10/22 当前 UI 回归 | [原生测试结果](VerificationEvidence/skills-ui-20261022/native-ui-results.xml)：技能面板 15 项与目标选择 15 项共 30/30 通过；[摘要](VerificationEvidence/skills-ui-20261022/native-ui-summary.json)确认 409 份运行源码在测试期间未变；[离线编译](VerificationEvidence/skills-ui-20261022/runtime-compile-summary.json)零错误 | 测试不构建 Player、不连接真实服务器；不能据此认为旧包包含后续 UI 调整 |
| 10/22 新测试包 | [构建结果](../Builds/play-20261022-skills-ui/build-result.json)：`Succeeded`、`UnityExitCode=0`、`SourceChanged=false`；[程序集核验](../Builds/play-20261022-skills-ui/verification/skills-assembly-source-verification.json)确认本轮技能 UI 已编译入包 | 不代替新 Player 登录、真实学习升级或其他系统功能验收 |
| 固定包与角色技能业务 | [程序集与源码核验](../Builds/play-20261021-ready/assembly-source-verification.json)确认最终 `BattleScreen` 和 `SkillPanel` 已入包；另复查 `SkillBookClient`、`SkillBookMapper`、`GameClient`、`AppBootstrap` 的 PDB 源哈希与当前业务源码一致 | 真实学习/升级、扣款、保存、重登和战斗效果仍需使用测试账号联机验证 |
| 角色技能客户端回归 | [2026-10-20 主干整合记录](SKILL_LOGIC_HANDOFF.md#9-2026-10-20-主干整合与验证记录codex)：78/78 离线测试通过 | 不等于 Unity Test Runner、联网保存、重登或逐技能战斗效果通过 |
| 宠物与截图机制 | 面板入口、宠物实例上下文及 Editor 样例存在；正式适配器返回“宠物技能服务端尚未开放” | 如意圈、死亡缠绵和五阶毒不能按已完成功能验收 |

2026-10-21 较早的 [截图复核记录](VerificationEvidence/skills-ui-20261021/existing-captures-review.json) 保存当时 30 张图片的 SHA-256，并标明实际查看的 6 张；此后其他界面任务已重新生成同目录截图，因此旧哈希只对应当时版本。该历史记录不应覆盖后来生成结果。当前截图报告中的 `liveServerVerified=false`、`battleEffectsVerified=false` 仍然有效。左侧角色摘要的简短文案已核对进入固定包，新包游戏内技能页仍需联机实屏复验。

2026-10-09 的许可证失败是历史记录，后来已完成原生截图，不应继续用它描述当前阻塞。旧编译与 25 项纯 UI 契约检查分别保留在 [编译报告](VerificationEvidence/skills-ui-20261009/compile-report.json)、[资源报告](VerificationEvidence/skills-ui-20261009/resource-report.json) 和 [纯 UI 契约报告](VerificationEvidence/skills-ui-20261009/pure-ui-contract-tests.json)；这些报告仅证明当时的源文件。

## 新包的最短测试顺序

双击本页顶部 10/22 新包的 `Start-Client.cmd`，它连接本地网关 `http://127.0.0.1:8081`，不会自动登录或自动战斗。服务端必须已经运行；旧包的 `server-readiness-reference.json` 是 10 月 21 日的历史记录。

2026-10-22 01:20（EDT）已恢复角色技能测试所需本地服务，独立核验网关 `UP`、一区 `OPEN / SMOOTH`，并在实际 Player 刷新区服列表，看到 `zone-1` 流畅、已选中且“进入选角”可用。详见 [本次交付核验](../Builds/play-20261021-ready/delivery-verification-20261022.json)。客户端已打开，账号登录由测试者手动操作，尚未验证真实账号学习升级或战斗效果。完整服务栈未恢复：`chat` 端口绑定被 Windows 拒绝，`trade`、`friend`、`player_inbox`、`mail`、`rank` 也未恢复，本次不验收这些功能。此记录仅证明核验时状态，重启电脑后须重新启动服务端。

**同日后续发现：区服开放不等于角色可以进场。** 实际客户端日志 `Builds/play-20261021-ready/logs/manual-20261022-013124363-9d56163f/client-01.log` 已记录 `[enter] server reported enter failure tip=3023`。该进场问题正在「启动六个客户端」任务中处理；须先成功进入主场景，再执行下面的技能联网验收。不能把此前健康检查或原生 UI 测试作为登录、存档或技能效果已经通过的证据。

1. 手动登录一区，点击主界面 **修行 → 技能**（也可从 **角色 → 技能** 进入），点击刷新。应显示该角色职业的真实目录、等级、余额；不能停在“未同步”或“布局预览”。
2. 选择一项可学习/升级技能，先记下等级与余额，再点击 **升级**。等待回包后两者一起更新；处理中不能重复提交。再测 **升级 10 次**，遇到等级或余额限制时显示实际提升次数和停止原因。
3. 关闭再打开技能页，退出重登，确认等级和余额一致。余额不足、满级与等级不足应有明确原因，不能反复扣款。
4. 从 **历练 → 战斗 → 1人** 进入战斗，**先关闭自动战斗，再点击“法术”手动选技**。自动战斗和超时目前仍只普攻。按下文检查敌/友/自身目标、取消、切换技能和冷却提示；真实伤害、治疗与扣蓝还要核对服务端结果。
5. 选中一只宝宝，打开 **宝宝 → 技能**。当前应明确显示服务端尚未开放，且不能升级；这项用于验收空态与角色/宠物隔离，不用于验收吸收或二连效果。
6. 断线时不能继续修习。重连刷新后应恢复权威状态；请求结果未确认时先刷新，避免把未知结果当失败后反复点击。

已知业务边界：升级请求尚未返回时，切到宝宝再立即切回角色，角色页可能停在未同步提示；待请求结束后点击刷新可恢复。本次已用实际 Player 程序集及假传输复现，见 [证据](VerificationEvidence/skills-ui-20261022/context-race-repro.json) 与 [Claude 修复交接 §5.1](SKILL_LOGIC_HANDOFF.md#51-待-claude-修复升级在途时切宠物后提前返回角色)。此项尚未修复，验收时不要将手动恢复记作自动恢复通过。

记录包名、角色/职业、技能名、操作前后等级与余额、时间、实际提示和截图。客户端运行日志随测试包交付说明定位；联网问题同时保留对应服务端日志。

## 战斗目标交互检查

2026-10-21 按逻辑交接的 `targetTeam` 契约补充 UI 过滤。仅接收展示元数据，不在客户端重写施法规则。新包应符合：

| 选中的操作 | 预期交互 |
|---|---|
| 普攻或 `enemy` 技能 | 仅存活、未逃跑的敌方高亮并可选 |
| `ally` 技能 | 仅存活、未逃跑的己方高亮，包括自己、队友及同队宠物 |
| `self` 技能 | 自动预选自己并显示确认；选技能时不发送行动，仍需点击确认 |
| 目标阵亡、逃跑或从快照消失 | 清除失效选择，隐藏确认，不能提交旧目标 |
| 本人阵亡、逃跑、已提交，或进入回合结算 | 清空选择，收起高亮与确认，不能留下无响应按钮 |
| 战斗连接暂时断开后恢复 | 断线时隐藏高亮与确认；同一回合恢复后可继续确认仍有效的原目标，不自动发送行动 |
| 取消，或改选普攻、另一个技能、道具 | 清除上一项操作的目标和高亮，按新操作重新选择 |
| 旧技能或缺少目标元数据的道具 | 保留原有任意存活目标选择，由服务端最终校验 |

当前 32 项角色目录的 `targetTeam` / `maxTargets` 已与服务端 `generated/tables/skill.json` 逐项比对一致。映射为 `1 → enemy`、`2 → ally`、`3 → self`。战斗选择使用局内 `actor_id`；宠物技能书使用的持久化 `pet_id` 不能作为战斗目标 ID。

## 打开可点击的原生界面

1. 用 Unity 打开 `E:\work\mmorpg-client`，等待脚本编译完成，保持 Edit Mode。
2. 选择菜单 **MMORPG → UI → 技能界面 → 打开交互预览**。
3. 在预览窗口顶部选择职业、障碍技能、宠物技能或异常状态，并切换 **1920 × 1080 / 2560 × 1080**。
4. 直接点击画面里的筛选、技能行、刷新、升级或升级 10 次按钮；鼠标放在列表或说明上可以滚轮滚动，也可拖动滚动条。升级只显示收到的 UI 请求参数，不会修改示例等级或金币。关闭面板后，用顶部“重置样例”重新打开。

预览使用正式 `SkillPanel` 和仅存在于 Editor 的 `ISkillBookSource` 样例。每张画面都标记“布局预览”。窗口关闭、程序集重载或进入 Play Mode 时会释放临时场景与纹理；不使用 `SkillUiRoot` 的生产单例，也不切换或保存当前游戏场景。

## 游戏中的入口

- 角色窗口右侧“技能”进入角色技能簿，正式启动已绑定 `GameClient.Skills`。没有绑定业务数据源时才显示四职业只读图鉴，等级与金币显示未同步，学习/升级禁用。
- 宝宝窗口先选中一只宝宝，再点击右侧“技能”；入口传入真实宠物实例 ID。当前正式适配器返回只读空态和未开放说明，不发宠物技能 RPC，不凭本地图鉴授予宠物技能。
- 技能页侧栏可返回原角色或宝宝的属性/加点；打开其他主城窗口、战斗或退出当前游戏会话时收起技能页。
- 战斗选技读取权威技能 ID 列表，显示中文名、图标、冷却提示与滚动。施放资格、耗蓝和效果结算由服务端处理；UI 的目标高亮只依赖已提供的 `targetTeam`，不推演战斗规则。

## 验收样例

| 样例 | 检查内容 |
|---|---|
| 四个角色职业 | 图标、名称、等级、类型、法力、目标、详情，以及各分类筛选和选择状态 |
| 障碍五项 | 狼猛蜂毒、蛇口蜂针、鹤顶红粉、蝎尾蛇涎、万蚁噬心的列表与长说明布局 |
| 宠物两项 | 如意圈、死亡缠绵和“天生技能”标签，宠物身份不混入角色技能簿 |
| 加载、空列表、错误 | 清晰的状态文字、正确的按钮可用状态、刷新入口 |
| 金币不足、满级、处理中、未连接 | 升级操作不可用，当前信息仍能阅读 |

障碍和宠物的名称及说明是截图功能参考的 **Editor 样例**，采用已有图标占位，不代表新增正式技能配置或已实现战斗效果。四职业 32 项资源作为 UI 图鉴使用，也不代表玩家已经学会这些技能。

## 导出验收截图

选择菜单 **MMORPG → UI → 技能界面 → 导出验收截图**，或在交互预览顶部点击“导出全部截图”。

输出位于 `Docs/VerificationEvidence/skills-ui-20261009/`。每种样例分别导出 1920 × 1080 和 2560 × 1080 的 PNG，并包含战斗选技列表首屏和滚动后画面，完成后写入 `native-capture.json`。导出时会断言按钮状态、请求参数、不修改展示数值、被动筛选，以及战斗未知技能保留、被动隐藏、冷却禁用和超过九项时可以滚动；失败会抛出错误，不写成功报告。

报告仅证明原生 UI 渲染与这些界面断言完成；仍需查看截图确认字体、裁切、图标与视觉效果。报告明确记录 `liveServerVerified=false` 和 `battleEffectsVerified=false`。

没有其他 Unity 编辑器占用此项目时，可由集成负责人运行：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -batchmode -projectPath 'E:\work\mmorpg-client' -executeMethod SkillUiVerification.CaptureAll -quit -logFile 'E:\work\mmorpg-client\Docs\VerificationEvidence\skills-ui-20261009\capture.log'
```

截图需要图形设备；不要添加 `-nographics`。已有编辑器开着时直接用菜单，不再启动第二个实例。

## 自动化的 UI 接口检查

打开 **Window → General → Test Runner → EditMode**，运行 `MmorpgClient.Tests.EditMode.Battle.SkillPanelPresentationTests`。

该组测试检查：升级按钮传出选中的技能 ID、当前等级和次数；UI 不自行更改等级和金币；刷新使用当前宠物身份；加载、繁忙、断线、只读、空列表、金币不足及满级时升级不可点；快照更新后下一次操作使用新等级。测试没有调用网络、服务器或战斗实现。

新增 `BattleSkillTargetRangeTests` 和 `BattleSkillTargetSelectionTests` 位于 `Assets/Tests/EditMode/Battle/BattleSkillTargetSelectionTests.cs`。前者直接检查正式目标展示判定，已离线执行 22 项通过；后者走真实技能按钮、单位点击、取消、确认与快照更新，随后已在 Unity Test Runner 实际执行 15 项通过。首轮测试遇到 `BattlePartyCards.Clear` 在 EditMode 使用 `Destroy` 的问题，出包方修复 `BattleHud` 后复跑通过；以 `target-ui-fixed-results.xml` 为最终证据，保留首轮失败记录，不混用计数。

独立批处理命令（避免与已打开的 Unity 抢占项目）：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'E:\work\mmorpg-client' -runTests -testPlatform EditMode -testFilter 'MmorpgClient.Tests.EditMode.Battle.SkillPanelPresentationTests' -testResults 'E:\work\mmorpg-client\Docs\VerificationEvidence\skills-ui-20261009\ui-tests.xml' -logFile 'E:\work\mmorpg-client\Docs\VerificationEvidence\skills-ui-20261009\ui-tests.log'
```

## 业务接口与联网验收

现有 `Assets/Scripts/Game/Skills/SkillBookClient.cs` 已实现 `ISkillBookSource`，在 Unity 主线程发布完整 `SkillBookViewState` 并触发 `Changed`。`Refresh` 与 `Upgrade` 接收当前 `SkillBookContext`；`Upgrade` 同时接收技能 ID、界面展示的等级及次数。UI 不预测升级结果，不写玩家数据，不解释战斗效果。当前角色契约、宠物待办和测试记录见 [逻辑交接文档](SKILL_LOGIC_HANDOFF.md) 第 8、9 节。

联网另外验证：真实角色与宠物隔离、服务端拒绝与重试、学习/升级/扣款后的权威快照、断线或切角色时旧回包处理。中毒、吸收和连击须等对应业务完成后单独验收，不能用 Editor 样例替代。
