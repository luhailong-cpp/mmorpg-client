# 2026-09-28：四种组队邀请入口

当前代码已接入真实组队服务；下文 2026-09-13/14 的“服务未开放”说明属于历史交付边界。

按 T 打开「结伴同游」，创建队伍后，队长点「邀请道友」进入邀请窗口。窗口复用 TeamV2 的玉绿标题、米白纸面、暖金细边和人物头像，提供好友邀请、周围玩家、在线玩家、聊天邀请四个来源。列表支持姓名或编号筛选、刷新和分页，编号邀请入口继续保留。聊天玩家资料也能进入组队邀请。

四个来源分别读取真实好友 RPC、当前场景 AOI、服务端在线目录、已同步聊天参与者；不会用演示角色填充正式列表。在线目录使用 RecommendFriends RPC 的显式 online_only 分支，并要求回包 online_directory=true；旧服未实现时显示未开放，避免把普通随机好友推荐误当在线名单。在线查询分页且限定同归属区服，邀请提交仍由原 InviteToTeam 权威校验。

所有入口共用 TeamClient.Invite。队长权限、队伍满员、在途请求、已发邀请与已在队伍等状态在发送前复核；客户端不提前把目标加入成员。断线、换角或连接更换后，清空目录并忽略旧请求回包。玩家文字以纯文本显示。

本轮验证结果以 E:/work/image/designs/team-invites-20260928/README.md 与 qa 中实际运行记录为准。界面截图使用明确标注的离线样例，不代表双账号在线验收。

---
# 组队 UI

2026-09-13 v2：实际客户端已改为「队伍成员 + 申请列表」双栏同屏，移除「已同意」页面和入口。设计来源为素材仓库 `E:/work/image/designs/team-ui-v2`；运行时使用独立 uGUI 控件与原生 TMP 文字。

## 入口和布局

主城左侧任务追踪下方的「组队 [T]」入口沿用。T 打开或关闭，Escape 关闭；窗口显示期间阻挡地图移动。进战斗、断线、退出或更换角色时关闭窗口，断线和换角同时清空组队资料。

- 左侧成员使用五条横行：清晰头肩头像、姓名、等级、门派、队长／队员、自己和在线状态。空席降低强调；容量大于五时独立分页。
- 右侧申请列表与成员始终同时可见：每页最多四人，显示头像、姓名、等级、门派、拒绝／同意按钮。一到两名申请使用宽松卡片，三到四名使用紧凑行。
- 两份名单的页码分别维护，翻动一侧不会改变另一侧。单页隐藏翻页控件；名单收缩时各自纠正页码。
- 标题保留国风字形，正文与操作按钮使用随包的 Noto Sans SC；该字体仅应用于组队窗口。玉绿、米白、暖金控件、灯笼和短流苏继续使用项目正式资源。
- 头像沿用 `QdaoCharacterCatalog` 的当前角色来源，通过运行时圆形遮罩显示头肩区域，没有重画或替换角色身份。

`Show(TeamPage.Members/Applications)` 保留为打开入口，两栏都会显示。旧 `Show(TeamPage.Approved)` 调用映射到成员，历史数据不会显示。`TeamSnapshot.Approved` 暂留数据兼容；新界面不读取或维护它。

## 行为约束

名单由权威快照提供。点击同意／拒绝只发出意图，不在实际客户端乐观修改名单；服务成功回包之后，申请从右侧移除，加入者出现在左侧。非队长、服务不可用、正在请求时禁止处理；满员时禁止同意、保留拒绝。刷新在请求期间禁用。请求十秒超时后允许重试，过期回包无效。

名字和门派按纯文本渲染，长文本省略显示，不能被解释为 TMP 富文本。分页后、申请行删除后和窗口关闭后恢复到有效控件焦点。

门派优先使用 `SchoolName`，缺省时沿用已知职业 1=破军、2=玄霄、3=丹心、4=逐风。资料不完整时显示待同步。

## 当前服务边界

服务端仍未提供主城组队创建、申请、同意、拒绝或成员资料 RPC。`TeamInfo` 只有队伍 ID、队长 ID 和成员 ID，不能推断陌生玩家的名字、等级、门派或头像。因此正式游戏继续明确显示「组队暂未开放」，不会生成假成员或模拟线上入队成功。本轮完成界面接入与离线验证，不扩展服务器组队系统。

后续连接真实服务时，向 `TeamUiRoot.State` 提供包含 `LocalPlayerId` 的完整快照；订阅 `RefreshRequested(generation)` 和 `DecisionRequested(generation, playerId, approve)`，并使用 `Complete`／`Fail` 结束请求。服务推送应核验连接世代与当前角色，不能将战斗匹配队伍当作主城组队。

## 验证与预览

- 运行 `tools/client_compile_check.ps1` 检查全部运行时代码。
- Unity EditMode 过滤 `MmorpgClient.Tests.EditMode.Tianyong.Team`，覆盖窗口、快照与请求状态。
- Unity PlayMode 过滤 `MmorpgClient.Tests.PlayMode.TeamWindowPlayModeTests`，验证实际生命周期中的输入阻挡与释放，以及真实 EventSystem 的开启／关闭、快照重建、申请删除和分页收缩焦点。
- 编辑器菜单 `MMORPG > UI > Capture team screens (offline)` 输出至 `.codex-artifacts/team-ui-v2`。两分辨率各十张，覆盖双栏、申请翻页、满员、成员翻页、独立页码、申请空态、服务不可用和处理中。
- 运行模式下使用 `MMORPG > UI > Preview team UI (offline)` 打开离线交互预览。界面标明示例角色，同意后仅演示移入成员；关闭时清理临时窗口。

旧 `.codex-artifacts/team-ui` 图片及测试属于之前三页版本，不能用于 v2 验收。当前结果记录在 v2 目录和素材仓库交付说明中。

字体：`Assets/Resources/Fonts/TeamNotoSansSC.otf`，来自本机已有 Noto Sans SC 字体，按 SIL Open Font License 随包保留 `TeamNotoSansSC-LICENSE.txt`；项目来源 [Noto CJK](https://github.com/notofonts/noto-cjk)。

### 2026-09-13 最终结果

Unity 6000.6.0f1：EditMode 27/27、PlayMode 6/6，均为 Passed；CaptureAll 成功生成两种分辨率共 20 张截图。截图逐一断言所有可见非空文字包含可见字形，避免字体行高让整个名字被省略；手工复核双申请、紧凑列表、满员、空态、未连接、处理中和独立分页。

实际截图、两份原始测试 XML、源码 SHA-256 与全部截图清单位于 `E:/work/image/designs/team-ui-v2`，汇总记录为 `qa/client-integration-report.json`。此次验收只覆盖客户端 UI 与离线快照，服务器边界见上文。

### 2026-09-14 官方 MCP 原稿切图接入

从已完成的 `team-ui-v2.png`（1931×814 原生）通过 Unity 官方 MCP 切出 14 张专用 PNG，导入 `Assets/Resources/UI/Ugui/TeamV2/`。`TeamUiArt` 负责独立资源加载；主框、标题牌、行底、申请卡、按钮和徽章使用九宫格，圆形资源使用透明 Alpha。原稿 baked 资料及动作文字清除，姓名、等级、门派、身份、数量和按钮文案均为原生 TMP。

正式 `TeamWindow` 已换成原稿的云头玉牌、细金纸面、横向成员行和申请卡；现有状态机和主城入口继续沿用。共享 GameplayV1 仅用于既有灯笼与短流苏。实际截图及回归运行在独立验证工程，保留主编辑器当前未保存场景。

切片、来源/九宫格清单、官方 MCP 成功响应和本轮验收证据位于 `E:/work/image/designs/team-ui-v2/unity-slices/`。原有服务边界不变，离线 UI 验证不代表主城组队 RPC 已实现。

# 2026-10-09:组队跟随(走路时跟着队长)

问道式一路纵队:队长点地走,队员在身后按入队先后排成一列,沿队长走过的路一个跟一个;队长停,队员走到自己的位置也停。

## 怎么实现的

**每个客户端只驱动自己的角色。** 队员的客户端把本地角色走到「沿队长轨迹往回数 槽位 × 1.4」的点上,照常发 `MoveStart / MoveSync / MoveStop`,服务端照常裁决;别的队员在本机上的位置来自他们各自的移动广播(`ActorMoveS2C`)。

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Game/Team/TeamFollowTrail.cs` | 队长轨迹(折线)。纯几何,不碰 Unity 运行时 |
| `Assets/Scripts/Game/Team/TeamFollowPlanner.cs` | 槽位(`SlotOf`)与每帧指令(`Plan` / `PlanToPoint`:`Hold` / `Steer` / `Route`)。纯规则 |
| `Assets/Scripts/Game/Team/TeamFollowDriver.cs` | 每帧:算槽位 → 在 `ActorWorld` 里按 `PlayerId` 找队长 → 记轨迹 → 规划 → 驱动本地控制器 |
| `Assets/Scripts/World/Tianyong/TianyongPlayerController.cs` | 跟随接管:`SetFollowControlled` / `FollowSteer` / `FollowRoute` / `FollowHold` |
| `Assets/Scripts/UI/Ugui/Team/TeamFollowUiRoot.cs` | 宿主(自动生成的常驻对象):持有驱动并每帧驱动它、开关按钮与提示。队伍状态只读 `TeamUiRoot.Instance.Client`,`TeamUiRoot.cs` 本身没有改 |
| `Assets/Scripts/World/ActorWorld.cs` | 远端角色的插值、外推上限、碰撞体(见下「顺带修正」) |

关键数值都在 `TeamFollowPlanner` / `TeamFollowDriver` / `TeamFollowTrail` 顶部的常量里:间距 1.4、最多 4 个跟随槽位、前视 0.8、停步容差 0.15 / 起步容差 0.6(滞回,避免反复起停刷移动包)、偏离轨迹 2 以上改寻路、落后 0.3 以上移速 ×1.1(9.9,不超过服务端的客户端速度信任上限 10)、轨迹保留 80、停步回拉最多收回 1.5。

## 行为

- **槽位**:非队长成员按 `JoinSeq` 升序,同号按玩家编号;1 紧跟队长。转让队长后原队长按自己的 `JoinSeq` 排进队列。
- **接管期间**玩家自己点地面 / 按方向键不会移动角色,只弹一条提示(3 秒内最多一次);界面占着键盘(聊天打字、弹窗)时照样跟着走。脚本化方向(`SetDebugDirection`,验收用)优先级仍然最高。
- **开关按钮**(组队入口右侧,`TeamFollowUiRoot` 自己的画布上,名字 `TeamFollowToggle`):跟随中显示「暂离」;暂离中显示「归队」;在队里但看不到队长时显示「寻找队长」(不可点,此时本来就能自己走)。没有队伍或自己是队长时不显示。点完会取消选中,免得紧接着按空格 / 回车又触发一次。
- **暂离是纯本地状态**,服务端不知道:换图照样被队长带走,也可能被服务端瞬移回队长身边(例如单人打完一场战斗),只是到了之后不排队。确认换队 / 离队时复位;断线重连期间队伍视图暂时拿不到不算换队。
- **看不到队长就不跟**:队长的角色不在本机 `ActorWorld` 里时恢复自由移动,再看到时重新接管。
- **队长掉头**:队长带着速度往回走时轨迹折成发夹原样留着,队员继续沿去程走,直到回程的队长走到身边才转身跟上(2026-10-13 起由游标保证,见文末),不会被赶到队长前面。只有队长站定时才把轨迹末端收回一小段(停步后显示位置被拉回停点,最多 1.5)。
- **刚看到队长、或暂离 / 战斗回来而队长已经挪了位置时**,在他背后(按他的朝向)铺一段直线尾巴重起轨迹;尾巴碰到不可走的格子就停,后排的人先叠在最后一个可走点上,队长一走出去就拉开。新进视野的远端角色朝向为 0(`ActorCreateS2C` 不带朝向),所以站着不动的队长背后一律是 −Z 方向。
- **槽位落在不可走的格子里**时目标统一换成最近的可走点(到了就站住,没到就寻路)。
- **直线跟随被行走掩码挡住**(轨迹按远端角色的显示位置记,拐角处可能擦到不可走格)时,接下来 0.75 秒改用寻路去槽位;寻路找不到路则 2 秒后再试。
- 战斗画面亮着、未进游戏、组队快照还没有权威视图时一律不接管。

## 顺带修正(影响所有远端角色,不只是队友)

这些路径此前从未真正运行过(服务端不发 `ActorMoveS2C`),跟随是它们的第一个使用者:

- **`ActorWorld.Tick` 的插值**:旧实现收到移动样本后先在 0.15 秒内朝「上报时的位置」插值,过了窗口才按速度外推,所以行走中的远端角色恒定落后 速度 × 0.15 = 1.35(一个跟随槽位)。现在样本一到就按它的速度外推,插值的两端一起前进,被收掉的只是收到样本那一刻的显示误差:本来就走在正确位置上的角色保持匀速,不会每收一个样本就顿一下。`Clock` 字段与 `Tick(float)` 重载是为了让测试能确定性地驱动它。
- **外推上限**:超过 1 秒(`ActorWorld.MaxExtrapolationSeconds`)没有新样本就停止外推。走着的人掉线、或被服务端冻结而「停步」没发出来时,不会在别人屏幕上一直直线跑下去。
- **角色不做物理障碍**:`ActorWorld.SpawnActor` 禁用图元自带的碰撞体(本地角色的控制器接管时本来就会删掉它)。可走性只看行走掩码,其它角色不挡路。
- **转向立即上报**:`TianyongPlayerController` 在行进方向相对上一次上报转过约 20° 以上时立刻补发一条 `MoveSync`(两次之间至少 0.12 秒,约 8 条 / 秒封顶;gate 对 `MoveSync` 的额度是每窗口 20 条)。远端按最后一次上报的速度外推,不上报拐弯的话别人会看到你先直着冲出最多 2.25 再被拉回。

## 依赖服务端的部分

以下三件事在服务端分支 `feat/team-follow`(`xuanming-server-mmo`,设计文档 `docs/design/team-system.md` 文末「组队跟随 v1.1」),**未编译**。旧的 `scene.exe` 上本功能不会生效:

1. 移动广播 `ActorMoveS2C`(133):此前服务端从不发送,别的玩家在客户端上根本不会动。
2. 入队后把队员带到队长所在的线 / 场景。
3. 同一场景内把队员瞬移到队长脚下(`TeleportS2C`,reason = 5),并重建双方可见性。

## 验证

- 离线 Roslyn 编译:运行时程序集(397 个文件)与 `MmorpgClient.Tests.EditMode.Tianyong`(41 个文件)均 0 error。
- 纯逻辑测试 `TeamFollowTrailTests`(16)+ `TeamFollowPlannerTests`(20)在离线运行器里 36/36 通过。其中三条是逐帧模拟:队长走 L 形后队员停在槽位上且不反复起步;掉队 6 米后在行进中追回且不走走停停;队长原路掉头时队员不会跑到他前面。八个故意改坏的版本(间距、回退收点、起停滞回、自交取近、无条件收回、收回不设上限、追赶门槛过低、定点容差写反)各被对应用例抓到。
- 这些模拟是规则层面的:步进照抄控制器 `FollowStep` 的截短规则,「是否在走」用上一帧的指令代替控制器的 `IsMoving`。控制器自己的分支(被掩码挡住、寻路、服务器吸附)不在其中。
- **没有在 Unity 里跑过**:`TeamFollowDriver`、控制器的跟随分支与转向上报、`TeamFollowUiRoot`(宿主与按钮)、`ActorWorldFoundationTests` 里新增的五条(`RemoteActor_*` 四条、`SpawnedActors_AreNotPhysicalObstacles`,需要 GameObject,离线运行器跑不了)只过了编译。Unity 内的 EditMode 测试命令:`Unity.exe -batchmode -nographics -projectPath <工程> -runTests -testPlatform EditMode -testFilter "TeamFollow;ActorWorldFoundation" -testResults <xml> -logFile <log>`(不带 `-quit`)。
- 联机验收步骤见服务端设计文档 V.6 第 3 步。
- 注意:自动化驱动(`DevAutoPilot` 的 `SetDestination` 路径测试等)所用账号若是某支队伍的非队长成员,点击寻路会被跟随接管覆盖;跑这类验收前先离队或点暂离。

# 2026-10-13:跟随游标(修复队长折返时队员原地踱步)

## 现象与根因

真实客户端联机验收(服务端 robot `team-follow-smoke` 的 `client_follower` 形态:机器人当队长按客户端节拍走路,真实播放器当队员)抓到:
队长向北走一段、原路走回、再向东拐,队员约一半的概率在原地一步向北一步向南,再也跟不上(连跑 6 遍 3 遍)。
机器人记下的队员上报是速度 `-9.9` / `+9.9` 交替。

根因在 `TeamFollowTrail.DistanceBehindHead`:它每帧在**整条**轨迹上找离自己最近的一段。队长来回走过同一条线时,去程与回程在几何上重叠,
而远端角色的显示位置带着厘米级的摆动,两趟不会严丝合缝 —— 「最近的一段」于是由摆动决定,在两趟之间来回跳;两趟的行进方向相反,
转向点就一帧在前一帧在后。此前的单测用的是严丝合缝的直线,测不出来。

## 改法

队员记着**自己走到轨迹的哪儿了**(游标),每帧只在它附近找:

- `TeamFollowTrail` 给每个记录点记**里程**(队长沿这条轨迹累计走过的弧长,`double`),新增 `HeadOdometer`、`Epoch`(清空 / 重起时加一)、
  `TryProject(position, fromOdometer, toOdometer, preferOdometer, tieTolerance, …)`(只在里程窗口内投影;几处差不多近时按里程偏好挑)。
- `TeamFollowPlanner.Plan(…, ref TeamFollowCursor cursor)`(逐帧驱动必须用这个重载):
  1. 有游标:只在上一帧里程的 `[-CursorBack(1), +CursorAhead(2)]` 内找自己;
  2. 例外:里程在窗口之后的某一段离自己不超过 `JumpLateral`(0.5)时跳过去 —— 队长折返后从身边走过、或绕一圈回来,队员不必把那一圈走完。
     **只往前跳**,跳过之后旧的那一趟不再是候选,所以不会来回;
  3. 没有游标、轨迹重起过、或窗口里已经找不到自己(偏离超过 2):在整条轨迹上重新找,几段相差不超过 `AcquireTieTolerance`(0.25)时取离队首最近的。
- `TeamFollowDriver` 持有游标;交还控制权(暂离、战斗、看不到队长)、换队长、槽位不可走改走定点时丢掉它。
- 不带游标的 `Plan` 重载与 `DistanceBehindHead` 原样保留(单次查询、既有用例在用),**不要**再拿它逐帧驱动角色。

行为上的变化只有一处说法要更正:队长掉头后,队员**继续沿去程走,直到回程的队长走到身边(0.5 以内)才转身跟上**,不是一定走到折返点。
队长绕一大圈回来穿过自己走过的路,同理。

## 验证

- 离线 Roslyn 编译:主工程(`ba60401f` + 本改动)运行时 429 个文件、Tianyong 测试 41 个文件,均 0 error;出包用的快照工程(`750bddf6` + 本改动)418 / 41,0 error。
- 纯逻辑测试 46 / 46(原 38 + 新 8):`TeamFollowTrailTests` 里程与开窗投影 4 条;`TeamFollowPlannerTests` 游标 3 条 +
  逐帧模拟 `Simulation_LeaderDoublesBackAlongAWobblyLineThenTurns_FollowerDoesNotPaceBackAndForth`(去程 / 回程各带 3 厘米、相位相反的摆动,重现联机现场)。
  三条既有的逐帧模拟改为带游标(与驱动一致)。
- 变异检查:把带游标的 `Plan` 退回「每帧全局找最近一段」,新模拟用例掉头 156 次(上限 2)、三条游标用例同时失败。
- 离线运行器的 NUnit 替身补了 `[TestCase]` 支持(此前的 38 条里有 2 条参数化用例跑不了)。
- 真实客户端复验(新包 + 同一条 robot 验收)结果记在服务端 `docs/PROGRESS.md` 2026-10-13 的条目里。
- **没有在 Unity 编辑器里跑 EditMode 测试**(命令同上一节「验证」);`TeamFollowDriver` 的改动只有 5 行,靠出包后的联机验收覆盖。

## 已知、待定

- **间距观感**:当前镜头下 1 个世界单位约 12 像素,人物贴图约 40 × 75 像素;间距 1.4 只有 17 像素,队员与队长的贴图叠住一半以上。
  拉开到一个身位(约 3.3)会让 4 名队员的队尾离队长 13.2,超过服务端视野半径 10 与归队阈值 8,需要和服务端一起改,待定。
- 机器人队长走的是服务端导航网格上的直线,不过客户端行走掩码;真实队长走不到的地方(掩码外)没有覆盖。
