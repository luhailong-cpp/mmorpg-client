# 9 张节庆地图的本机联调

该入口使用现有 `GameClient.EnterScene` 请求、真实服务器入场通知、角色与移动协议。它只出现在 Unity Editor / Development Build，并须显式传 `-festivalMapLocalTest`；每次请求核对实际连接使用的 HTTP 网关和分配的 gate 都是回环地址。正式地图窗口、`CanTravelOnline` 和 9 图的 `clientOnly=true` 均保持原样。

当前隔离服务器已更新并加载、注册 9 图导航，网关 `http://127.0.0.1:18185` 的 1 区已开放。本次客户端完成编译与 47 项针对性测试，但用户选择自行手动验收，自动流程已停止；尚未完成双端实机场景验收，自动进入 Play 也未实机确认。

## 启动

保持本次隔离测试服运行。在 PowerShell 7 执行以下命令，以隔离服测试账号登录，然后手动切图：

```powershell
pwsh -NoProfile -File D:\luyuan\mmorpg-client\tools\run_festival_maps_local.ps1 -Editor -LocalServerEnvFile D:\luyuan\xuanming-server-mmo-cpp\build\festival-maps-20261109\stack\.env
```

脚本打开主仓的正式 `Bootstrap.unity`；若没有进入运行状态，直接点 Unity 顶部 **Play**。进入后使用 `dev_festival_manual` 自动登录，左上角展开“9 图本机联调”。脚本拒绝复用已打开的主工程，不关闭其他 Unity 副本，也不覆盖未保存场景。没有服务器时面板保持等待；请求被服务器拒绝/超时会复用既有失败提示与恢复机制。编译通过或面板出现不代表双端验收通过。

按本轮要求只通过 Unity Editor 联调，不打包客户端。脚本不启停服务器、不清库、不改变凭据；完整的 9 图资源与已有 PNG 保持不变。

左上角面板列出 `01001`、`01002`、`05008`、`10000`、`19004`、`21003`、`23006`、`28007`、`35004`。只启用已交付的 15 套外观；`19004` / `23006` 没有春节，`28007` 没有中秋。点击外观请求同区入场；同图切景保留同一场景实例、实体、坐标及导航。面板显示服务端场景模板、运行实例和当前外观。收起面板测试 WASD / 点击行走，再展开选择下一图或返回主城。传送未结束时保持角色输入门禁。

## 必须逐图确认

- 进场由服务器通知确认，出生与恢复位置位于已交付安全导航内；正常移动有服务器响应，障碍、边界、填充区与断开平台不能穿越。
- `19004` 仅 48 个安全格；`10000` 两平台与 `35004` 三平台不连通。`10000` 的实际画面接缝仍为待修，不能记作视觉验收通过。
- 返回主城、拒绝请求、超时、掉线重连不留下在途锁或错误旧图导航。跨区传送另按正式测试环境验证；本面板只提供同区联调。
- 日志在 `Logs/festival-local-player-*.log`；`[FestivalMapLocalTest] REQUEST` 仅证明请求已发出。实际验收另记服务器、客户端版本、导航与配置 SHA、逐图结果及未解决项，不以该日志充当通过回执。

本批冻结美术和交接证据不改写；只有服务器权威导航和双端验证均通过后，才按成对发布清单另行评审正式开放。

## 隔离测试账号与备用自动联调

本次隔离服务器若按配套步骤生成了 `.env`，可以通过子进程环境变量自动登录，口令不会进入命令行或日志：

```powershell
pwsh -File tools/run_festival_maps_local.ps1 -Editor -LocalServerEnvFile ../xuanming-server-mmo-cpp/build/festival-maps-20261109/stack/.env
# 备用自动检查入口；本次已停止，下面不是已通过的验收记录：
pwsh -File tools/run_festival_maps_local.ps1 -Editor -LocalServerEnvFile ../xuanming-server-mmo-cpp/build/festival-maps-20261109/stack/.env -Account dev_festival_verify -AutoVerify
```

实际 `.env` 路径以服务器本次产物为准。账号默认为 `dev_festival_manual`，仅使用隔离服随机生成的开发口令。备用 `-AutoVerify` 设计为在真实渲染窗口运行，完成后关闭本次启动的编辑器，输出 `festival-local-verification.json`、16 张截图（15 外观 + 主城）及日志；本次没有产出该通过报告。它要求真实服务端入场通知，同图换景核对场景实例、实体、坐标、导航保持一致。每套节庆外观必须有匹配的运行分片清单、实际视域内分片全部加载且零在途请求；报告记录总块数、可见/已加载块数和资源路径。底图回退或 `[CurrentMapTiles]` 加载错误不算通过。视域外分片未遍历的事实与全 90 块文件 SHA 校验分开记录。

合法移动部分先记录客户端行走与零纠偏：`NotifyMoveAck` 是纠偏消息，合法移动没有此消息是正常情况，不能据此声称服务器已接受每次移动。随后的真实 137 会记录发送越界消息前脚底、服务器裁决点、两者水平距离及裁决点距出生点的距离；仅在拒绝检查通过且裁决点距此前脚底不超过 1.5m 时标记 `authoritativeLegalEndpointConfirmed=true`。C++ strict mask 会拒绝整段越界输入并保留此前权威位置，本轮须逐图核对此字段；Java 导航可沿途推进到障碍前，因此该字段不作为两个后端共用的整体 PASS 条件。

每张新图随后通过正式 `SendMoveStop` 发送一个有限的有效范围外点，不直接修改角色坐标，最多等待 5 秒匹配本次 `input_seq` 的真实 137 裁决。报告记录 `illegalMoveRejected`、输入序号、上报点、服务器裁决点和客户端最终位置，要求同一连接/场景中服务器返回合法位置且客户端保持在该位置附近。这证明网络越界报告被拒绝；由于该输入可能同时命中距离或速度限制，不能单独把结果归因为精确导航 mask，后者由服务端 native 查询测试另证。缺坏导航、跨区、超时故障注入和重连仍未覆盖，不能据此解除正式门禁。
