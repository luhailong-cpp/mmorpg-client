# 技能界面测试入口

本入口用于验收客户端 UI。技能升级、扣款、学习、宠物技能效果、中毒、法术吸收和连击由后续逻辑适配器提供。本页的预览不连接账号、服务器或玩家存档。

## 本机验收状态（2026-10-09）

Unity 批处理启动在执行 `CaptureAll` 前被许可证检查中止：退出码 **198**，日志报告没有有效 Unity Editor 许可证、缺少 `com.unity.editor.headless`。因此本次 **没有执行原生截图，也没有执行 Unity EditMode 测试**，不能将代码或预览入口的存在视为原生验收通过。原始日志保留在 `Docs/VerificationEvidence/skills-ui-20261009/unity-capture.log`。

许可证可用的 Unity 编辑器中，可按照下面的菜单步骤自行检查。独立 .NET 的纯展示状态测试只覆盖状态与身份契约，不替代原生画面、按钮事件或场景生命周期验收。

运行时 400 个脚本、`SkillUiVerification` 编辑器预览工具与 `SkillPanelPresentationTests` 测试程序集已通过离线 Roslyn 编译。资源检查确认四职业各 8 项、共 32 个唯一技能 ID 与 32 张独立图标，Sprite 元文件齐全。报告位于同目录 `compile-report.json` 和 `resource-report.json`。

不启动 Unity 的编译复查命令：`pwsh -NoProfile -File tools/verify_skill_ui_compile.ps1`。这条命令只检查编译，不会执行原生 UI 测试。

本次独立 .NET 运行结果：**25 项通过，0 项失败，进程退出码 0**。直接编译当前正式 `SkillBookModels.cs`，检查学习/升级按钮的展示可用性、金币边界、等级限制、错误/加载/断线状态，以及角色与宠物身份匹配。报告记录源文件 SHA-256，见 [纯 UI 契约报告](VerificationEvidence/skills-ui-20261009/pure-ui-contract-tests.json) 和 [运行日志](VerificationEvidence/skills-ui-20261009/pure-ui-contract-tests.log)。

本机重复执行（不需要 Unity，不启动网络）：

```powershell
dotnet run --project 'E:\work\tmp\skill-ui-contract-20261009\SkillUiContractTests.csproj' --configuration Release -p:RestoreConfigFile='E:\work\tmp\skill-ui-contract-20261009\NuGet.Config' -- 'E:\work\mmorpg-client\Docs\VerificationEvidence\skills-ui-20261009\pure-ui-contract-tests.json'
```

## 打开可点击的原生界面

1. 用 Unity 打开 `E:\work\mmorpg-client`，等待脚本编译完成，保持 Edit Mode。
2. 选择菜单 **MMORPG → UI → 技能界面 → 打开交互预览**。
3. 在预览窗口顶部选择职业、障碍技能、宠物技能或异常状态，并切换 **1920 × 1080 / 2560 × 1080**。
4. 直接点击画面里的筛选、技能行、刷新、升级或升级 10 次按钮；鼠标放在列表或说明上可以滚轮滚动，也可拖动滚动条。升级只显示收到的 UI 请求参数，不会修改示例等级或金币。关闭面板后，用顶部“重置样例”重新打开。

预览使用正式 `SkillPanel` 和仅存在于 Editor 的 `ISkillBookSource` 样例。每张画面都标记“布局预览”。窗口关闭、程序集重载或进入 Play Mode 时会释放临时场景与纹理；不使用 `SkillUiRoot` 的生产单例，也不切换或保存当前游戏场景。

## 游戏中的入口

- 角色窗口右侧“技能”进入角色技能簿；没有绑定业务数据源时，显示四职业只读图鉴，等级与金币显示未同步，学习/升级禁用。
- 宝宝窗口先选中一只宝宝，再点击右侧“技能”；入口传入真实宠物实例 ID。无业务数据时为空态，不凭本地图鉴授予宠物技能。
- 技能页侧栏可返回原角色或宝宝的属性/加点；打开其他主城窗口、战斗或退出当前游戏会话时收起技能页。
- 战斗选技读取原有权威技能 ID 列表，新增中文名、图标、冷却提示与滚动。敌友目标、耗蓝校验、宠物出手和效果结算仍由后续逻辑接入实现。

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

独立批处理命令（避免与已打开的 Unity 抢占项目）：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'E:\work\mmorpg-client' -runTests -testPlatform EditMode -testFilter 'MmorpgClient.Tests.EditMode.Battle.SkillPanelPresentationTests' -testResults 'E:\work\mmorpg-client\Docs\VerificationEvidence\skills-ui-20261009\ui-tests.xml' -logFile 'E:\work\mmorpg-client\Docs\VerificationEvidence\skills-ui-20261009\ui-tests.log'
```

## 逻辑接入后的验收

业务方实现 `ISkillBookSource`，在 Unity 主线程发布完整 `SkillBookViewState` 并触发 `Changed`。`Refresh` 与 `Upgrade` 接收当前 `SkillBookContext`；`Upgrade` 同时接收技能 ID、界面展示的等级及次数。UI 不预测升级结果，不写玩家数据，不解释战斗效果。

业务接入后另外验证：真实角色与宠物隔离、服务端拒绝与重试、学习/升级/扣款后的权威快照、断线或切角色时旧回包处理，以及实际战斗中的中毒、吸收和连击。这些不在本 UI 预览的验收范围内。
