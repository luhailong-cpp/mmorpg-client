# 保留人物正式身份接入与运行验收（2026-09-21）

本次范围为原 ID 00–10、14、15、17、20，不重排、不恢复已删除人物。本页记录本机接入与验证；历史 Unity XML、隔离工程截图和模型提示词不能替代本次证据。

## 当前素材边界

- 正式可用原人物为 00–03 的完整 V13，512×512、52 PPU。
- 正式 V14 目录尚无完整已批准人物，不能生成发布索引或宣称存在已验收的实际 1024 行走截图。
- 04 保留 104 个 V13 walk，新增有效候选 14 个，缺 10 个；05 为 52+12，缺 64 个；06 为 16+5，缺 107 个。
- 07、08、09、10、14、15、17、20 各缺 128 walk 和 8 idle。候选不计为批准，原肖像的存在不代表运行时包已完整。
- [逐人物状态表](../../image/qdao_original_roster_v14_hd/mixed-preparation/client-integration-20260921/RETAINED_ROSTER_STATUS.md)列出全部 15 个保留 ID；[逐槽清单](../../image/qdao_original_roster_v14_hd/mixed-preparation/client-integration-20260921/retained-roster-inventory-detailed.json)包含各方向缺帧和肖像/JSON 状态。

## 正式身份链

新增独立 `string appearance_id`，与 `class_id/gender` 分离。创角请求和账号角色摘要保存同一稳定字符串；首次入场沿用既有 CAS 存盘流程补入角色 profile。AOI、队伍成员和战斗快照继续传递该身份，角色列表、主城实体、队伍头像和战斗表现均优先读取显式 ID。

实际存储是 Redis `account:<account>` 中的账号目录与 MySQL `player_database.profile_component` 中的完整 protobuf 字节。新增嵌套字段不另加 SQL 列，但当前真库是否已有 `profile_component` 基础列尚未验证。五条账号写入路径已取消原 12 小时 TTL，两个登录入口原子 PERSIST 旧账号且不重序列化原字节；48 小时后重登相同身份的进程内回归已通过。其他会话、token、锁和 PlayerAllData 缓存 TTL 不变。

无 TTL 修复只消除自动过期丢失角色目录：现有 Redis `allkeys-lfu` 仍可淘汰账号与反向索引，尚未登录的旧 key 仍需迁移，旧二进制也可能重新加 TTL。实际 AOF、持久卷及崩溃恢复未验证；仓内没有可据此恢复全账号目录的 MySQL 冷读链。部署前步骤及已过期目录不能凭空恢复的边界见 [外观身份存储设计](../../mmorpg/docs/design/character-appearance-identity.md)。

空 ID 的旧存档继续按原职业/性别解析 23–30。新选择只枚举保留且具有完整批准资源的原人物；显式 ID 缺资源时不改写存档、不借另一人物外观。同 ID 完整 V14 优先、完整 V13 回退，沿用逐方向加载和引用计数。

世界移动速度仍为 9，参考 30 ms/帧、480 ms/周期，按位移推进动画。512/52 与 1024/104 使用各帧几何及相同 pivot=(0.5,0.08)。混合 04/05/06 仍按 `mixed-preserved-v1` 保留旧图；本次没有把旧 512 帧放大成高清。

战斗冲刺与返回使用同身份 E/W walk，帧进度由实际位移计算，保留现有位移时间。攻击、施法没有新增动作帧时仍使用已有降级表现。角色切换、动作中断、销毁和残影各自释放其资源引用。

## 运行入口与证据规则

正式入口仍为 `Assets/Scenes/Bootstrap.unity`，选角 UI `RoleFlowUi` 有独立外观选择。`DevAutoPilot -appearanceId <原ID>` 复用正式创角/选角回调及网络流程，检查回包、ActorWorld 和实际 animator 的身份；`-requireAppearanceRole` 在重登列表缺失时直接失败，禁止创建替代角色。

`tools/run_move_test.ps1 -AppearanceId <原ID> -AppearanceUi` 连跑两次独立播放器并核对同 player_id、appearance_id 和重登位置。可选的 `-AppearanceUi` 保留真实 `RoleFlowUi.Choose`，依据网络角色列表向真实按钮发送 Unity 指针事件；driver 不填写 `PlayerChoice`。实际头像、选择结果及落盘截图核对成功后才记录 `appearance_ui`。登录请求仍由网络自动驾驶发起，不等于人工点按登录界面。

`tools/run_crosszone_pair.ps1 -AppearanceA <原ID> -AppearanceB <原ID> -AppearanceUi` 复用双播放器战斗入口；默认仍要求不同 gate，显式 `-SameZoneAppearanceCheck` 才仅验收同区外观对局。两边职业/性别均为 1，外观单独传递。正式 BattleScreen 的同身份头像、待机及至少两个有实际位移的走帧、逐帧几何全部通过，且截图保存后，才记录 `appearance_battle_view`。攻击/施法仍为既有降级动作。

两脚本启动前检查全部日志、结果和截图目录，拒绝覆盖历史证据；双实例启动中途失败只停止本脚本已启动的进程。外观战斗截图必须存在并与本次角色/身份/目录及实际日志绑定。

真实 Unity 验收复用素材仓库 `qdao_original_roster_v13/tools/run_unity_tests.ps1`，必须显式指定 `-Editor`、`-Project`、新 `-Run` 和已保存 `-InputSnapshot`。本机 Editor 为 `E:/Unity/6000.6.0f1/Editor/Unity.exe`；隔离迁移目录为 `D:/luyuan/wuxingqitan/tmp/qdao-original-live-candidate-20260921`。

走帧捕获从实际完整资源合同的每个含 native-HD 帧的方向取首帧；实际控制器必须处于 Run、有正位移且渲染器确实引用该 PNG。正常/最近镜头同 simulationFrame，记录输入 SHA、资源 SHA、尺寸、PPU、pivot、投影和裁切；至少两个方向。`verify_mixed_walk_captures.py` 的绑定校验与逐图目视审批是不同门禁。

## 本次运行结果

正式 Unity 工程的本轮离线运行测试已完成；正式联网验收仍未完成。当前结果：

- [run2 EditMode XML](../../image/qdao_original_roster_v13/runtime-validation/formal-character-20260921-run2/editmode.xml)：405/405 通过；[PlayMode XML](../../image/qdao_original_roster_v13/runtime-validation/formal-character-20260921-run2/playmode.xml)：49/49 通过，均零失败、零跳过，Unity 实际退出码 0。覆盖 Original/HD/Mixed、Animator、资源生命周期、RosterSandbox、14 项身份 EditMode、6 项身份 PlayMode 及 28 项相机 EditMode 用例。HD 合同夹具不代表缺失的真实高清美术已通过。
- 00–03 使用正式 V13 512 资源完成实际控制器移动、切换和正常/最近镜头捕获。16 张主城截图含旧职业映射 23–30 的兼容检查，不表示恢复了被删除的原人物。最近镜头完整头身脚和名牌的自动投影检查及逐图目视检查通过；原 512 材质近看仍有分辨率限制，不计为新 1024 行走证据。
- 本轮实际控制器每名角色只采样 N 方向 16 个走帧，位移约 4.8 世界单位；八方向资源读取及动画用例通过不等于八方向真实连走的逐图目视批准。主城截图为停步 idle；八方向转向/循环动态目视、04–06 原生高清与混合体型检查仍是缺口。
- 首次 run1 最近镜头存在头顶裁切，已保留原图；修复相机近景取景中心及名牌布局后 run2 通过。没有更改角色 PPU、pivot、世界体型或移动速度。截图、输入绑定和目视范围见 [本轮截图记录](../../tmp/qdao-integration-20260921/RUNTIME_SCREENSHOTS.md)。
- 发布工具原全套 147/147 通过，UI 补充回归 73/73 通过，相机与发布门禁最终定向回归 76/76 通过。当前绑定 88 文件；历史基线未改。见 [发布工具报告](../../image/qdao_original_roster_v14_hd/mixed-preparation/client-integration-20260921/PIPELINE_REPORT.md)。
- 正式 Windows Development 播放器已构建至 `tmp/qdao-formal-player/mmorpg.exe`，实际启用场景为 Bootstrap 和 TianyongSandbox；BuildReport 成功、0 错误、499 警告、约 2 分 57 秒。全部 283 个文件共 4,121,485,069 字节，需保留完整目录运行；[产物 SHA 清单](../../image/qdao_original_roster_v13/runtime-validation/formal-player-20260921-build1/player-artifacts.json)和原始警告日志已保存。播放器尚未启动，构建不是联网验收。
- 最新 Go 登录/管理端 161 个测试及子用例通过（160+1），先前独立队伍 142 个通过，均零失败/跳过；属于进程内回归，不能替代 AOF/数据库恢复证明。
- 两个玩家验收脚本的 35 项门禁验证通过，覆盖参数引用、证据保护、截图绑定与异常子进程清理；没有用模拟脚本替代真实玩家结果。

编译、服务端和工具证据位于工作区 `tmp/qdao-integration-20260921`。不能将编译成功或断网 UI 夹具截图称作正式联网验收通过。

首次沙盒账号启动 Unity 因无授权退出（198）；切换本机用户上下文后许可证可用。正式编译首先暴露公会夹具旧 `Contribution` 字段和 Tianyong 测试程序集缺 Protobuf 引用；按当前协议字段及既有 Battle 测试程序集配置作最小修复，保留原失败日志。

Unity 导入会将部分仅含 GUID 的旧 `.meta` 展开为当前导入设置。本次使用固定接手提交 `fc0a8dc` 比较作者文件和 GUID，并另记历史安全基线 SHA：角色 PNG 无改动，20 个 JSON 仅为工作树换行差异，1835 个 `.meta` 有导入设置变化，GUID 变化为 0。见 [导入审计](../../tmp/qdao-integration-20260921/formal-resource-import-audit.json)。该审计不构成替换旧基线或批准元数据迁移。旧发布门禁在无法证明旧资源保护时继续拒绝发布。

run2 EditMode 的原始输入前后有一处 `ProjectSettings.asset` 差异：Sentis 包移除 `;SENTIS_ANALYTICS_ENABLED`；PlayMode 又写入动态字体字形缓存。正式 Build 随后把该 define 恢复，仍有独立原始前后快照。测试结束只按接手前精确 SHA 恢复本轮字体/设置副作用，观察文件另存备份，见 [清理记录](../../tmp/qdao-integration-20260921/restored-test-side-effects-20260921-135113-366988/record.json)。清理没有改写原始观测，也不能把本次改称“输入完全未变”的发布验证。全量快照一致性门禁仍未满足，本轮未 stage、未 publish，没有从隔离工程拷回 GUID 或 runtime-index。

本机隔离工程准备已同步当前正式工程四类输入（Assets、Packages、ProjectSettings、Library/PackageCache），19,462 个文件全部 SHA 一致、无多余文件，源在复制前后无漂移，见 [迁移对应记录](../../image/qdao_original_roster_v13/runtime-validation/formal-seed-sync-20260921-run1/input-correspondence.json)。仅正式工程向隔离 seed 复制，未运行隔离 Unity，也不代表已有可发布候选完成隔离验证。

## 正式联网阻塞

正式登录、主城、队伍、战斗和重登尚未完成。Docker Desktop 后端因 `dockerInference` 残留端点无法访问而启动失败，所需基础设施及业务端口未运行；当前 scene/gate/battle 可执行文件仍是旧版，新构建被既有裸指针与生成头解析检查阻断。没有关闭门禁、启动旧节点冒充新协议、清库或重置 Docker。

已重建 7 个 Go 核心二进制及 `battle.lib`，但它们不能替代最新节点与真实服务。具体失败、版本/SHA和下一步入口见 [服务端运行报告](../../tmp/qdao-integration-20260921/SERVER_RUNTIME_REPORT.md)。待修复这两项阻塞并正常构建/启动开发服后，使用上述正式播放器入口重跑，逐角色确认相同 player_id 和 appearance_id。

本任务未调用图像生成工具，未新绘或放大旧人物帧。历史新增高清候选仅有内置生成入口记录，具体 GPT Image 型号未核验。

用户后来明确要求重画 04 山岳守卫、05 天音少女、06 雷法少年这批新增候选。该重画及多余候选清理尚未执行；当前内置工具不能选择或核验具体 GPT Image 型号，不能以本报告的 Unity 截图代替指定型号的新出图。
