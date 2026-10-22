# 技能逻辑接入交接：Claude Code

更新日期：2026-10-22。分工已经确定：**Codex 负责技能 UI；Claude Code 负责服务端逻辑、业务客户端、协议、持久化和 UI 数据适配。不要覆盖正在修改的 UI 和图标。**

## 当前接入状态（2026-10-22）

- 当前事实源是正式客户端的 `Assets/Scripts/Game/Skills/SkillBookClient.cs`、`SkillBookMapper.cs` 与 `Assets/Scripts/UI/Ugui/Skills/SkillBookModels.cs`、`SkillUiRoot.cs`。角色适配器已经实现 `ISkillBookSource`，由 `GameClient.Skills` 持有、`AppBootstrap` 实际绑定；列表和升级使用生成的类型化 RPC，`UpgradeSkill = 317`。无需重新创建适配器或复制旧 staging。
- 角色列表、学习/升级请求、整份快照替换、错误和会话隔离已接入。战斗 UI 的 `enemy/ally/self` 高亮、点击与确认过滤也已完成，自身技能仍须玩家确认。已完成接线不等于角色升级持久化或 32 个技能效果已联机验收。
- `Builds/play-20261021-ready` 已成功构建；其 `assembly-source-verification.json` 和实际 DLL/PDB 一致，`BattleScreen.cs` 编译源 SHA-256 为 `6637AA5D1B63585716A72E94E021C08D7635351DFB16409FACA0EB0A0636E7D9`。`verification/target-ui-fixed-results.xml` 记录真实 Unity EditMode 目标 UI 回归 **15/15 通过**，使用假传输；后续工作区 UI/图标改动不因此自动进入该包。10/22 又实际执行当前技能面板与目标选择 **30/30** 原生 UI 测试通过，见 [本轮结果](VerificationEvidence/skills-ui-20261022/native-ui-summary.json)；409 份运行源码在测试期间未变化。新包状态与当前登录前提以 [测试说明](SKILL_UI_TESTING.md) 为准，原生 UI 测试不替代联网验收。
- 待做：§5.1 的在途升级切换上下文边界；§6 的宠物技能协议、持久化和实际施法路径；五阶毒、法术吸收、给友方普攻二连击；角色学习升级与战斗的两端联网全流程验收。
- 10/22 新增独立测试包 `Builds/play-20261022-skills-ui/`，构建零错误且源码稳定；14 份关键运行源已通过 [Player 程序集核验](../Builds/play-20261022-skills-ui/verification/skills-assembly-source-verification.json)。该包带当前 UI 调整，§5.1 已知业务缺口仍存在；未修业务不能因再次编译而标记完成。
- 第 1 节现状表是 **10/09 历史基线**，第 8 节是 **10/19 分支交接记录**，第 9 节记录 **10/20 合并验证**。保留它们便于追溯；旧的“未合入/待接线/未执行”陈述不能覆盖本摘要及后续更新，也不应据此重复移植旧草稿。

## 1. 目标、现状与工作边界

目标是让正式游戏中的角色技能与宠物技能可查询、可按规则学习升级、可在战斗中产生权威效果，并保持重登、断线、结算及两版服务器的一致性。已有图标、界面和旧草稿不构成功能验收。

| 内容 | 2026-10-09 核对结果 |
|---|---|
| 正式服务端 | `E:/work/xuanming-server-mmo`。旧技能表仅 ID 1–13；正式 `ListSkillsResponse` 仍只有错误与 `skill_list`，未接入四职业目录/升级接口。 |
| 正式客户端 | `E:/work/mmorpg-client`。本轮技能 UI 已提供角色入口、按宠物实例打开的入口、独立数据接口；未绑定业务源时显示只读图鉴或未同步提示。 |
| 原创四职业内容 | `E:/work/image/designs/class-skills-20260928/catalog.json`，4 个职业、32 个技能；最大等级 10，原创名称。 |
| 旧服务端草稿 | `E:/work/image/designs/class-skills-20260928/integration/server`，有表、协议、学习升级及引擎修改草稿，尚未安全合入正式仓库。 |
| 旧客户端草稿 | 同目录下 `integration/client`，有 `Game/Skills`、生成协议及战斗法力表现改稿，必须逐段对照正式代码接入。 |
| 截图效果 | 五阶毒、法术吸收、给友方普攻二连击，**未包含在已有 32 技能的目录中**。不能拿降物伤、加防、装备连击当作已完成。 |
| Java 版 | 服务端规范记录 `D:/luyuan/wuxingqitan/xuanming-server-mmo-java`，本次核对该路径不存在。接入前定位真实仓库；找不到时明确记录待做原因，不编造已同步结果，也不另建替代仓库。 |

Claude 可修改：服务端权威 schema/表/proto/逻辑/测试；客户端 `Assets/Scripts/Game/Skills` 业务、网络生命周期、生成协议、战斗模型及目标合法性逻辑；一个实现 `ISkillBookSource` 的业务适配器。网络接线应增量合入当前 `GameClient`，不要复制旧完整文件。

Codex 所有的表现层：`Assets/Scripts/UI/Ugui/Skills/`、`Assets/Resources/UI/Skills/`、技能入口/窗口互斥/战斗选技的本轮外观，以及相关 UI 验证文件。需要新增呈现字段或交互时，先列清接口需求交回 Codex；**不要借逻辑接入重做或覆盖 UI**。`Resources/Skills/catalog.json` 是展示目录，可在权威内容定稿后同步元数据，不能据它授予技能。

## 2. 开始前必须处理的并发与协议冲突

1. 读正式服务端 `AGENTS.md`、`cpp/AGENTS.md`、`data/AGENTS.md`、`docs/PROGRESS.md`、`.github/copilot-instructions.md` 及 `docs/design/turn-based-battle-server.md`、宠物/属性相关设计。检查客户端当前本地规则。先看两仓 `git status`、近期提交与当前 PR/Issue，记录本任务涉及文件的基线。
2. 当前两仓存在其他任务未提交改动，包括宠物资源、属性、装备、`GameClient`、`BattleClient`、UI 和协议。不要 reset、整文件回退、移动他人文件；每次落补丁前重读目标内容。
3. **不要执行 `integration/apply_server.ps1`。** 该脚本对 staging 下所有文件 `Copy-Item -Force`，会覆盖较新的装备、连击、协议及其他修复。它不是合并器。只把已经审核的业务差异逐段移植。
4. **不要发布旧 `integration/client-publish-manifest.json` 的 107 文件清单。** 它的生成时间为 2026-09-28，基线与本轮 UI/协议不匹配；不要批量复制旧客户端树或旧 `.meta`。只重新生成受影响的协议产物，业务文件用最小补丁，保留现有资源 GUID。
5. 以下字段号已经发生真实冲突，旧草稿不能直接使用：

| message | staging 的旧提案 | 现正式已占用 | 接入要求 |
|---|---|---|---|
| `BattlePlayerSnapshot` | `skill_ranks = 20` | `CombatAttributes combat = 20` | 保留 combat，新增空闲字段；本次候选 21，接入时重新核对。 |
| `BattleActorState` | `skill_ranks = 27` | `CombatAttributes combat = 27` | 保留 combat；本次候选 28，接入时重新核对。 |
| `BattleEventItem` | `optional sint64 mana_delta = 14` | `eBattleHitKind hit_kind = 14` | 保留 hit_kind；本次候选 15，接入时重新核对。 |

旧草稿 `proto/message_id.txt` 中的 `243=SceneSkillClientPlayerUpgradeSkill` 也不能使用：正式消息表的 243 已是 `GuildServiceRespondGuildTrialInvite`。按当前权威消息表和生成流程分配新号、核对唯一性。新增 pet RPC 同理。字段号不可复用，生成代码不可手改，tip 码从 `data/tip/Tip.xlsx` 发号。

## 3. 四职业 32 个技能与内容基线

保留原创 ID 和名称；以下各行按 ID 递增，前 5 项主动，后 3 项被动。完整效果、解锁等级、倍率和资源键读取 `catalog.json`，不要从图标或截图猜数值。

| 职业 | 主动技能 | 被动技能 |
|---|---|---|
| 1 破军 | 1101 破阵惊锋；1102 横戈裂岳；1103 赤甲镇关；1104 戈鸣断势；1105 破军决胜 | 1106 铁骨铸魂；1107 砺刃藏锋；1108 百战归元 |
| 2 玄霄 | 1201 玄符落曜；1202 紫霄雷阵；1203 寒星凝魄；1204 太虚澄心；1205 九霄归墟 | 1206 灵台凝元；1207 玄脉通真；1208 云篆护心 |
| 3 丹心 | 1301 丹华续脉；1302 青莲润生；1303 玉露涤尘；1304 灵鼎护元；1305 丹心返照 | 1306 悬壶济世；1307 碧落养元；1308 清心抱朴 |
| 4 逐风 | 1401 逐影穿云；1402 回岚掠阵；1403 青羽催行；1404 缚岚止步；1405 长风先至 | 1406 听风识隙；1407 流云轻身；1408 乘风蓄势 |

旧内容规则需要在服务端单一事实源实现并测试：

- 最大技能等级 10；未学习为 0。达到 `unlockLevel` 可学，等级上限为 `min(10, 1 + (角色等级 - unlockLevel) / 2)`；未达解锁等级上限为 0。各职业第 1 个主动和第 1 个被动为初始学习候选，仍需校验职业和解锁条件。
- 下一重费用 `100 * nextRank * nextRank + unlockLevel * 10` 金币；主动伤害、治疗、辅助强度每重增加基值的 2%，被动每重增加基值的 5%。按目录的比例编码，`0.20` 表示 20%。
- 耗蓝 `ceil(maxMana * manaPercent)`，比例正数时最低 1；恢复法力按实际恢复量封顶，不显示超出上限的数值。
- 选中合法主目标优先；其余敌方按阵位/actor_id 稳定排序，治疗补目标按血量比例后阵位/actor_id；目标数量受表控制。
- 增益持续 2 个完整回合，施加当回合的结尾不应先吃掉一回合；同方向同属性取最强并刷新持续时间。速度改变从下一回合排序生效；先手优先级在速度之前。
- 被动只进入可重算派生属性；不能在 scene 计算完又在 battle 重复叠加。治疗加成等战斗专用被动也要明确唯一消费者。
- 目录写明不支持降级，当前 UI 没有降级操作；不要为模仿参考截图添加本地降级。

这些是原稿规则，不是验证通过声明。特别检查旧服务端 `StatMultiplier` 只读静态 buff 幅度、`PassiveBonus` 与 scene 属性重算重叠等位置，补上技能等级增幅与不重复计算的实际实现，不只复制说明文字。

## 4. 建议实施顺序

### A. 先落协议与权威数据

1. 在当前 `data/schema/skill_table.proto` 追加目录、职业、等级、目标、耗蓝、冷却、治疗、辅助及被动字段，审核 staging 的字段 26–46 是否仍未占用。同步 `data/Skill.xlsx`、必要的 `Buff.xlsx`；保留正式已有行及装备相关表。
2. 当前 `PlayerSkillComp` 追加持久化等级。旧通用技能没有 rank 时的兼容与新职业 rank=0 未学习必须区分；对角色转职、未知 ID、重复记录、超限等级制定清理/迁移策略，不能把所有旧技能静默删掉。
3. 角色列表/升级协议可沿用 staging 的语义：`ListSkillsResponse` 携带完整目录、角色等级、职业、金币余额；目录项含 `skill_table_id/name/description/icon_key/category/passive/target_team/max_targets/unlock_level/max_rank/rank/rank_limit/mana_percent/cooldown_rounds/next_rank_cost/upgrade_block_reason`。
4. `UpgradeSkillRequest` 明确 `skill_table_id/expected_rank/count`，count 仅 1 或 10；响应带错误、最新完整状态、实际升级次数、提前停止原因。请求发起者从服务端会话确定，不能信客户端传角色身份。
5. 为 battle 快照/状态追加已学技能等级、buff 施加强度与施加回合等确实需要的字段，避开第 2 节冲突；不要手写与 proto 重复的数据结构。把新表字段纳入 `battle_table_fingerprint`，保证 scene/battle 配置指纹可发现不一致。

### B. 学习升级、保存与属性

主要接入点：`cpp/libs/services/scene/player/system/player_skill.{h,cpp}`、`player_database_loader.cpp`、`player_attribute.cpp`、`cpp/nodes/scene/handler/rpc/player/player_skill_handler.cpp`。handler 保持薄包装。

- 列表由服务端按当前职业生成；未学也可出现在可学习目录中，已学状态只来自持久化组件。避免正式旧 `RegisterPlayer` 遍历所有职业种子的行为延续到新系统。
- 拒绝错误职业、未知技能、非法次数、旧 `expected_rank`、等级不足、余额不足，以及战斗/结算未收尾/跨区冻结/传送交接窗口。所有拒绝不得扣款或改等级。
- 一次 10 次升级允许在等级上限或余额处提前停，响应必须准确给出 `upgraded_count`、总扣费后的余额和原因。相同旧请求重放不能二次扣费；网络超时不能由客户端猜成功或补扣。
- 将扣费和等级修改放在同一权威执行上下文，沿用项目持久化与失败处理；验证 Redis/落库/重登/离线结算/跨区交接。仅调用 `SavePlayerToRedis` 不等于持久化完成，应遵守现有成功语义与写入屏障。
- 升级影响派生属性时立刻重算并推属性面板/技能面板。旧 staging 的 `UpgradeSkill` 末尾只保存并回列表，不能据此认为属性已即时更新。
- HP/MP 上限变化沿用现有重算原因规则，明确满血/残血/死亡时结果；不能靠重复升级、重登或重复重算刷血刷蓝。

### C. 战斗逻辑与结算

主要接入点：scene 的 `battle/system/player_battle.cpp` 与 battle 的 `system/turn_battle_engine.{h,cpp}`、`constants/turn_battle_constants.h`、`data/battle_table_fingerprint.{h,cpp}`。

- scene 构造参战快照：带当前已学且可用的主动技能 ID、必要等级，以及已经算好的派生属性。未学、错误职业、被动、旧已删除技能不能被提交施放。
- battle 在提交与实际执行时均校验技能归属、存活/逃离、MP、冷却、行动限制及目标阵营。友方/自身技能不能错误地降级到随机敌方；群疗只选活着的友方，丹心返照不能复活。
- 多目标法术一次扣费、一次冷却；单个目标死亡/消失的重选、选不到合法目标的结果要明确并有测试。高血量整数、倍率、封顶及扣费计算避免溢出。
- 应用技能等级的伤害/治疗/辅助幅度；保存 buff 当次施加强度，不能效果说明升级了而引擎仍用静态值。
- 保留正式装备的暴击、连击、反击、反震、抗性与事件 `hit_kind`；合入旧引擎文件会丢失这些逻辑，必须逐段合并。
- 技能效果只改变局内派生状态；scene 仍是玩家/宠物持久化的唯一写入者。结算按 battle_id 幂等，角色与宠物 HP/MP 正确回写；临时增益不能永久烙入基础属性。离线结算、重连、逃跑、战斗作废和重复结算都要覆盖。

### D. 法力事件必须贯通服务端到演出

staging `battle_data.proto` 有 `optional sint64 mana_delta`，但本次未在旧引擎找到 `set_mana_delta` 调用。不能只重生成客户端后宣称耗蓝/回蓝正确。

1. 以未占用字段号新增可区分存在性的 `mana_delta`：负数为实际消耗，正数为实际恢复，0 为实际零变化；`target_mana_after` 保持权威终值。
2. 服务端所有产生法力变化的路径填符号正确的实际 delta，包括施法、回蓝技能、道具和后续状态效果；明确无变化是否发事件。
3. 客户端完整透传 `ManaDelta/HasManaDelta`：协议解码 → 战斗事件模型 → `Presentation/TurnPlan.cs` → 播放帧。保留旧事件的兼容分支，不可把正的回蓝误判为扣蓝；同一出手内多个法力事件应按事件次序/实际累计处理，不能只保留最后一次。
4. 重放、观战、重连状态同步均以权威终值校正；动画与状态不能分别扣费两次。测试同一技能耗蓝又回蓝、零消耗、自身回蓝、他人回蓝、满蓝封顶及旧事件无 delta。

## 5. 现有 UI 的精确契约与接线

事实源：`Assets/Scripts/UI/Ugui/Skills/SkillBookModels.cs` 与 `SkillUiRoot.cs`；命名空间 `MmorpgClient.UI.Ugui.Skills`。它们不引用 `Game/Skills` 或 protobuf。

```csharp
public interface ISkillBookSource
{
    event System.Action Changed;
    SkillBookViewState State { get; }
    void Refresh(SkillBookContext context);
    void Upgrade(SkillBookContext context, uint skillId, uint expectedRank, uint count);
}
```

- `SkillBookContext.Character()` 表示当前登录角色；`SkillBookContext.Pet(ulong id, string name)` 表示宠物唯一实例。`Matches` 按 `IsPet + PetId` 比较，不按显示名称。切换角色会话要由业务 generation 隔离，不能只靠 Character context 区分两次登录。
- `SkillBookViewState`：`Context`、`Snapshot`、`IsLoading`、`IsBusy`、`IsConnected`、`IsReadOnly`、`Message`、`Error`。默认断线；业务必须在真正可用时显式 `IsConnected=true`。当前 UI 将 `IsReadOnly=true` 用作未同步/未开放图鉴：等级显示“未同步”，余额和费用显示“—”，升级与刷新均禁用。它不能直接表示“权威技能已同步，但这一项不可升级”；这种情况应提供条目的 `LockedReason`，见 §6。
- `SkillBookSnapshot`：`ClassId`、`PlayerLevel`、`CurrencyBalance`、`Title`、`Entries`、`Find(uint)`。每次响应整体替换快照，不在 UI 中增等级或减金币。
- `SkillEntryState`：`Id`、`Definition`、`Rank`、`RankLimit`、`NextRankCost`、`LockedReason`。
- `SkillDefinition` 使用小写元数据字段：`id/classId/className/name/type/category/originLabel/unlockLevel/maxRank/targetTeam/maxTargets/manaPercent/cooldownRounds/durationRounds/description/iconPath/iconResource`。`type="passive"` 为被动；目标用 `enemy/ally/self`。`originLabel` 可显示“天生技能”，不要把宠物技能谎称职业技能。默认图片资源键为 `UI/Skills/{id}`。
- `Changed` 必须在 Unity 主线程触发。异步回包需校验 context、会话 generation 和请求有效性；旧角色、旧宠物、断线前请求不能覆盖现状态。
- Root 会防止当前 context 重复 loading/busy，但 adapter 仍须有正确的请求生命周期、错误、超时与取消处理。失败后清 busy/loading；断线清或标记过期权威快照并禁升级；重新登录重新拉取。`Error` 非空时 UI 禁止使用旧快照升级，仍允许刷新；成功响应应清除旧错误，不要一直留下错误导致按钮不可用。
- `Upgrade` 必须发送 UI 提交的 `expectedRank`，不能悄悄替换成回包后更新的值；`count` 只允许 1 或 10。正式 `SkillBookClient` 已实现此签名和透传，不能退回旧 staging 的 `SkillClient.Upgrade(id,count)`。

现有业务接入点示意（`source` 已是 `GameClient.Skills`；`AppBootstrap` 已执行绑定和销毁时解绑，不要重复注册）：

```csharp
using MmorpgClient.UI.Ugui.Skills;

// 当前角色业务/网络会话就绪：绑定一次。
SkillUiRoot.EnsureSpawned();
SkillUiRoot.Instance.Bind(source);

// 已有角色与宠物页入口已经调用这些方法，无需重复造按钮。
SkillUiRoot.Open();
SkillUiRoot.OpenPet(petId, petName);

// 退出当前会话：先解绑，再由业务注销网络回调并销毁 source。
SkillUiRoot.Instance?.Unbind(source);
```

角色协议到展示模型直接维护正式 `Assets/Scripts/Game/Skills/SkillBookMapper.cs` 的 `ToSnapshot`，不在此复制第二份实现，也不将转换放进 UI。旧示例的 `UpgradeBlockReason` 字段不存在：当前使用 `UpgradeBlockTipId`，非零时经 `DescribeTip` 转成 `LockedReason`。`TargetTeamName` 将 1/2/3 映射为 `enemy/ally/self`，未知值为空串，不能默认成敌方。`NormalizeIconKey` 统一反斜杠、移除 `Assets/Resources/` 前缀及扩展名，空键回落到 `UI/Skills/{id}`。等级、上限、费用和余额均取权威响应；本地图鉴只补展示字段，不能代替服务端目录或已学数据。该 mapper 只保留当前角色职业的行，未来宠物适配不能照搬这一筛选。

当前 HUD“技能”、角色属性窗口“技能”和宠物窗口“技能”已接 `Open` / `OpenPet`，返回入口和窗口互斥已由 Codex 实现。战斗列表仅遍历 `BattleActorState.SkillTableIds`，按 `SkillUiRoot.FindPresentation(id)` 展示名称、图标、耗蓝比例和服务端冷却，并按元数据隐藏被动。正式 `BattleScreen` 已将同一目标显示判定用于高亮、点击和确认：`enemy` 只选敌方，`ally` 包含自己、队友和同队宠物，`self` 预选本人 `actor_id` 并等待确认；死亡/逃跑目标不可选，未知元数据保留任意存活目标的兼容范围。

取消或换技会清空旧选择；本人失去本回合行动能力时收起选择，临时断线只隐藏高亮与确认并保留重试意图，连接恢复不自动发送。**技能归属、MP、冷却、实际目标合法性、效果和宠物指令授权仍由业务与服务端校验**；UI 的显示范围不能授予施放资格。后续若需要新呈现字段或交互，由 Claude 列出窄接口需求交回 Codex，不重做这段 UI。

`FindPresentation` 当前先读已绑定 source 快照，再查本地角色图鉴；打开宠物技能时不能丢掉角色/其他宠物的表现目录。业务可维护独立权威元数据索引，把需要的窄查询接口交 Codex 接入；这个索引仍不授予施放资格。

### 5.1 待 Claude 修复：升级在途时切宠物后提前返回角色

2026-10-22 已使用交付 Player 的真实 `MmorpgClient.dll` 和现有 `SkillFakeTransport` 离线复现，未修改业务。DLL/PDB 匹配，编译时 `SkillBookClient` 源哈希与当前一致。时序为：角色技能已同步 → `Upgrade` 请求在途 → `Refresh(Pet(...))` 发布宠物只读空状态 → 升级回包到达前 `Refresh(Character())`。角色刷新因 `_activeWrite != 0` 直接返回，`State.Context` 仍是宠物；随后 `CompleteUpgrade` 清掉在途标记，却因上下文不匹配而丢弃角色结果且不触发 `Changed`。实际请求保持 `[77,317]`，宠物 88 的只读空状态没有自动恢复；再次手动刷新才发送第三次 `77`，响应后恢复等级 2、余额 99500。见 [离线复现结果](VerificationEvidence/skills-ui-20261022/context-race-repro.json)：`desiredAutomaticRecoveryPassed=false`，不能作为功能通过。现有 `UpgradeReplyAfterSwitchingToPetDoesNotOverwriteThePetView` 只覆盖“收到回包后再返回角色”，未覆盖提前返回。

本地复跑：`pwsh -NoProfile -File E:/work/mmorpg-client/.codex-artifacts/skill-ui-review-20261022/race-repro/run.ps1`。该脚本退出码 0 表示成功复现缺陷并确认手动刷新恢复；它使用 .NET 和假传输，不执行 Unity 或真实服务端。正式回归测试应由 Claude 随修复补入业务测试。

修复归 `Assets/Scripts/Game/Skills/SkillBookClient.cs` 及其业务测试；由 Claude 明确区分当前用户请求显示的上下文与最后发布快照的上下文，或排队执行返回角色后的刷新。必须保留一次读/写在途、请求会话隔离与宠物页不被角色回包覆盖的约束，不通过重发升级或本地猜测等级解决。

验收步骤：

1. 用现有假 RPC 载入角色技能书，发出一次升级并暂不回包；切宠物，确认没有宠物 RPC 或伪造技能。
2. 在升级回包前切回角色，再送成功/部分成功/拒绝但附完整状态的响应。预期自动呈现当前角色最新权威快照，或只发一次后续列表刷新；不得重复升级、假扣款或永久保留上下文错误。
3. 分别改成未发送、超时/断线和缺少完整状态的响应：清理 busy/loading，显示与当前角色上下文对应的错误并允许刷新；不得拿旧快照继续升级。若用户仍停在宠物页，角色结果不得覆盖该页。
4. 在上述过程中切账号或换连接，旧响应必须丢弃；新会话能正常重新拉取。测试同时断言 RPC 数量、`State.Context`、快照内容、错误恢复和 `Changed` 通知。

## 6. 宠物技能与截图机制：尚需定义并实现

宠物面板入口已经传入真实 `PetId`，**宠物技能查询/升级业务协议尚未定义**，不要伪造“已接好”的 `ListPetSkills/UpgradePetSkill` RPC。先查看当前 `proto/scene/player_pet.proto`、PetSystem 与宠物存档/快照契约，再明确以下内容并落设计：

- 按 pet 实例查询目录、已学/天生技能、来源、等级、目标类型和可升级性；服务端验证宠物归属。宠物不能沿用角色 class_id 筛选而显示错误目录。
- 当前未开放的宠物上下文使用 `IsReadOnly=true` 和空目录。未来权威数据已同步、但天生技能不可升级时，应设置该项 `LockedReason` 并保留真实等级和刷新能力；不要仅因不可升级就设置整页 `IsReadOnly=true`，否则当前 UI 会把它显示为未同步。若需要整页已同步只读语义，先交 Codex 明确新的状态/交互契约。可升级技能还需定义余额、消耗、expected_rank、防重放及保存规则，不能复用角色升级而遗漏 pet_id。
- 当前 battle 宠物为主人同队独立单位，现协议注释为默认行动代打。落实本任务要求的宠物施法入口：定义技能 AI 或主人下指令的正式协议/授权/回合归属，避免 UI 有技能却永远只普攻。
- 宠物真实 `pet_id` 与局内 `actor_id` 不同，列表/持久化按 pet_id，战斗目标按 actor_id，不能混用；战后回写归属到正确主人和宠物。

截图名称只用于效果对照，最终仍用原创命名与新分配的配置 ID，不能挤占上述 32 个 ID。五阶毒不等于把一个 DOT 复制五行；先形成一份可测试的技能表方案，明确五阶解锁、技能等级、目标数曲线、耗蓝、命中/道行/抗性、持续回合、降攻防幅度和标签。截图只展示部分等级样本，不能据此宣称完整公式已知。

| 效果 | 必须完成的可观察验收 |
|---|---|
| 五阶毒（截图：狼猛蜂毒、蛇口蜂针、鹤顶红粉、蝎尾蛇涎、万蚁噬心） | 对敌方按阶段/等级选择正确数量目标；命中后中毒、持续扣血、降低攻击和防御；按规则移除非木系辅助，保留木系辅助；中毒时禁止普通物理攻击及对应物理强攻。失手/抗性/免疫给清晰事件；死亡、驱散、到期恢复所有限制/属性；同一随机种子可复现。三阶截图有“技能 120 级目标增至 4”的参考，和四职业现定最大 10 级不一致，必须单独决定毒系等级方案，不静默套用。 |
| 法术吸收（截图：如意圈） | 宠物可给合法友方施加效果；敌方法术命中时按最终定义吸收，HP/吸收剩余额/事件一致；普攻不被误吸收；次数或额度耗尽、到期、驱散有终止事件。吸收是否转化回血、额度/次数/持续回合及穿透规则需要在技能设计中明确，截图没有给足数值。 |
| 友方普攻二连（截图：死亡缠绵） | 宠物给合法友方施加增益后，该友方普通攻击产生两个有序命中段；首击击杀后不会攻击已死亡目标或凭空额外攻击；主动法术不触发；持续/消耗/驱散规则生效；事件分组/hit_index 可让表现播两击。与现有装备概率连击的叠加或互斥规则必须明确，不能直接设 combo_rate=100 后留永久状态；第二击倍率也不能未经定义默认沿用装备的 50%。 |

补充回归：群体法术遇不同友方吸收状态；毒限制下自动战斗/技能降级普攻不得绕过限制；二连与反击/反震/暴击/吸收/死亡的顺序；重复加同 buff、较弱覆盖较强、读档与断线补状态。禁止仅凭 tooltip 文案或动画演示判定通过。

## 7. 生成、测试与交付

本轮只交接逻辑，Codex 未据此修改服务端代码/表/proto，也未执行服务端构建。客户端 UI 的独立编译与状态检查结果见 `Docs/SKILL_UI_TESTING.md`，不能代替逻辑验收。按服务端 `AGENTS.md §10.1`：Claude 修改实现、测试和文档后，把准确命令交给 Codex 执行；不要声称未经执行的编译或测试通过。

生成顺序：权威 schema/xlsx 与 proto 定稿 → 服务端导表/消息协议生成 → 各语言产物同步 → 客户端协议与 MessageIds 生成 → 受影响 C++/Go/Java/Unity 编译与测试。使用正式仓库现有工具；例如客户端命令必须显式指向真实服务器目录，不能沿用 README 的旧同级目录默认值：

```powershell
Set-Location E:/work/mmorpg-client
pwsh -File tools/gen_proto.ps1 -ProtoRoot E:/work/xuanming-server-mmo
pwsh -File tools/gen_messageids.ps1 -ProtoRoot E:/work/xuanming-server-mmo
```

以上是接入完成后交执行者的命令，不是本次已经运行的记录。服务端导表入口为 `dev.bat gen`，Go/proto 生成遵循当前仓库规范；C++ MSBuild 串行 `/m:1`。补充准确工作目录、目标工程、先后依赖、预期产物和失败日志位置。

最低测试清单：

1. 目录 4×8 项、ID/职业/解锁/图标键一致；无重复字段号/消息号，旧装备事件 hit_kind 未丢；不同服务配表指纹不一致时拒绝开局。
2. 每职业初始学习、未学→学习、单次/10 次升级、部分成功、满级、等级不足、金币不足、错误职业/宠物主人、非法 count、重复请求、旧 expected_rank、乱序回包、断线/切宠物。
3. 升级后属性即时更新；连续重算/重登不叠加被动；保存后重登、旧档兼容、战斗/传送/交接窗口拒绝写；扣费与等级不会一边成功一边丢失。
4. 32 技能逐项覆盖目标、实际 HP/MP/攻防/速度、倍率等级成长、持续时间、最强叠加、驱散、冷却、被动不可主动施放；scene/battle 不重复加成。
5. 截图新增机制按第 6 节覆盖；新宠物技能能通过真实宠物行动路径执行，不是引擎内部手工塞 buff。
6. ManaDelta 全链、群体技能只扣一次、回放/观战/重连与权威终值一致；伤害/状态事件组保持兼容。
7. 两端联网验收：进游戏打开角色/宠物技能页 → 刷新 → 学习/升级 → 进战斗施放 → 出战斗 → 重登复查。UI 按真实响应显示，不能用只读预览或假数据冒充联网成功。

已知旧测试证据：本次运行正式现有 `build/cpp/tests/turn_battle_engine_test.exe`，`PoisonBuffTicksEachRoundAndExpiresAfterTwoRounds` 1 项通过；它使用假配置只证明旧引擎毒 DOT。该 exe 为 2026-09-28，当前源码为 2026-10-09；新连击测试未包含其中。**必须重编译并确认实际匹配测试数量，不能把 gtest filter 未匹配也返回 0 当作通过。** 它更不能替代新增技能及 staging 的测试。

交付时列出：实际改动文件、当前协议/配置版本、逐功能验证结果、未执行项及真实失败；说明 C++/Go 与 Java 两版状态。定位到 Java 仓库后按其规范维护 `PARITY.md`；找不到则明确待做。不得声称“所有技能完成”而遗漏宠物、真实联网、持久化或另一版。

最后边界：**Claude 负责逻辑与适配；保留 Codex 已完成的技能 UI、入口、图片、布局和窗口交互。不要运行旧批量发布脚本，不要用 staging 整文件覆盖正式 UI。**

## 8. 2026-10-19 接入记录(Claude Code)

本节只追加,上面 §1–§7 原文不动。结论:**角色技能的 A(协议与权威数据)、B(学习升级、保存与属性)、C(快照带等级 + 引擎施放规则)、D 的服务端填值与客户端演出消费都已落码;宠物技能(§6)与截图新机制没做。**服务端代码未编译、未跑测试(按服务端 AGENTS §10.1 交 Codex);客户端已离线编译通过、新增用例已离线实跑通过,但没有在 Unity 里跑、没有联机。

### 8.1 分支与位置

| 仓 | 分支 / 工作树 | 内容 |
|---|---|---|
| 服务端 | `feat/class-skills`,`E:/work/xuanming-server-mmo-wt-skills`,基于 origin/main `c63b22dca2` | 配表 + proto + 导表产物 + scene / 引擎逻辑 + 测试 + 设计文档 `docs/design/class-skills.md`;**proto-gen 未跑**(生成器源码比仓内 exe 新,要 Codex 先 `proto-gen-build`),命令清单 `docs/handoff/class-skills-handoff-20261019.md` |
| 客户端 | `feat/class-skills`,`E:/work/mmorpg-client-wt-skills`(稀疏检出,只含 `Assets/Scripts`、`Assets/Tests`、`Assets/Plugins`、`Docs`、`tools`;`Library` 是指向主工程的目录联接) | 本节下列文件 |

客户端改动文件:
- 生成协议(用服务端分支的 proto 现生成,全量 `gen_proto.ps1` 对比确认只有这四份变化):`Assets/Scripts/Proto/Generated/{PlayerSkill,PlayerSkillComp,BattleData}.cs`、新增 `SkillErrorTip.cs`;
  `Assets/Scripts/Net/MessageIds.cs` 新增 `UpgradeSkill = 317`;导表产物 `Assets/Scripts/Table/Generated/SkillTable.cs`。
- 生成脚本:`tools/gen_proto.ps1` 清单加 `generated/code/proto/tip/skill_error_tip.proto`;`tools/gen_messageids.ps1` 白名单加 `SceneSkillClientPlayerUpgradeSkill`。
- 业务:新增 `Assets/Scripts/Game/Skills/SkillBookClient.cs`(实现 `ISkillBookSource`)、`SkillBookMapper.cs`(协议 → UI 模型、tip 文案);
  `GameClient.cs` 挂 `Skills`;`UI/AppBootstrap.cs` 启动时 `SkillUiRoot.EnsureSpawned()` + `Bind(GameClient.Skills)`,销毁时先 `Unbind` 再 `Dispose`;
  `Game/Battle/Presentation/TurnPlan.cs` 读 `mana_delta`(有就以它的符号为准,同一出手内多条按顺序累计)。
- 测试:`Assets/Tests/EditMode/Skills/`(新程序集 `MmorpgClient.Tests.EditMode.Skills`:`SkillBookClientTests` 18 条、`SkillBookMapperTests` 6 个方法 / 10 例)、
  `Assets/Tests/EditMode/Battle/ManaDeltaPlanTests.cs`(5 条)。
- **没碰** `Assets/Scripts/UI/Ugui/Skills/`、`Assets/Resources/UI/Skills/`、战斗选技 UI。

### 8.2 契约要点(给 Codex 做 UI 时参考)

- `ListSkillsResponse.catalog`:当前职业完整目录,未学 `rank = 0`;`upgrade_block_tip_id` 非 0 时适配器把它译成 `SkillEntryState.LockedReason`(UI 的 CanUpgrade 已据此禁用)。
- 修习结果:成功 / 部分成功 / 被拒但带了新技能书 → 写 `State.Message`(例:「【横戈裂岳】修习成功,提升 2 重(已停止:角色等级不足,暂不能继续修习)」),快照整份替换,不挡下一次修习;
  超时 / 网络失败 → 写 `State.Error`「修习结果未确认…请刷新」,UI 按既有规则禁止修习、只留刷新。现有 `SkillPanel` 在无 Error / 非加载 / 非提交时显示 `Message`,不需要改 UI。
- 宠物上下文:`IsReadOnly = true`、空目录、`Message = 宠物技能服务端尚未开放,暂不能查看与修习`,不发任何请求。
- 战斗选技目标:服务端按技能阵营校验,`target_id = 0` 时可由服务端按规则选(友方治疗选血量比例最低的存活友方)。10/19 交接时 UI 尚未按技能阵营过滤；**10/21 已完成** §5 的 `enemy/ally/self` 高亮、点击与确认接线。当前自身技能预选本人 `actor_id`，玩家确认后才提交，不自动发送，也不依赖 `TargetId = 0`。原“交 Codex 待接线”项已关闭。
- 战斗内被动不显示、不能施放(服务端快照不下发被动的施放 id);职业技能冷却在 `BattleActorState.skill_cooldown_rounds` 里按技能 id 给剩余回合。

### 8.3 生成与验证命令

服务端分支合入 main 并由 Codex 跑完 proto-gen 之后,客户端在**合入后的服务端主目录**上复核一次(只应得到与本分支相同的四份协议与 MessageIds):

```powershell
Set-Location E:/work/mmorpg-client
pwsh -File tools/gen_proto.ps1 -ProtoRoot E:/work/xuanming-server-mmo -Protoc E:/work/xuanming-server-mmo/third_party/grpc/install_vs2026_dbg/bin/protoc.exe
pwsh -File tools/gen_messageids.ps1 -ProtoRoot E:/work/xuanming-server-mmo
pwsh -File tools/client_compile_check.ps1
```

已实际执行(Claude,2026-10-19,客户端工作树):
- `tools/client_compile_check.ps1 -ProjectPath <工作树> -Csproj <主工程>/MmorpgClient.csproj`:改动前 524 个文件 0 错误;改动后 527 个文件 0 错误。
- 离线 NUnit 跑具(Unity 自带 `nunit.framework.dll` + .NET 10,反射执行 `[Test]` / `[TestCase]`;替身只有 `UnityEngine.Debug`、`SkillCatalog`(读 Resources)与 `MessageContent`):
  技能书 28 / 28 通过;`ManaDeltaPlanTests` + 既有 `TurnPlanTests` + `EquipCombatEventsPlanTests` 共 48 / 48 通过(TurnPlan 改动无回归)。
- **没做**:Unity EditMode 真跑(新程序集 asmdef 要 Unity 生成工程后才进 Test Runner)、截图、联机。

### 8.4 §7 最低测试清单逐项状态

| # | 项 | 状态 |
|---|---|---|
| 1 | 目录 4×8、ID / 职业 / 解锁 / 图标键一致;无重复字段号 / 消息号;hit_kind 未丢;指纹不一致拒绝开局 | 服务端真表契约用例**已写未跑**;字段号已人工核对(21 / 28 / 15 / 6 / 7 均空闲,combat 20 / 27、hit_kind 14 保留);消息号 317 无冲突;指纹整表序列化,新列自动纳入 |
| 2 | 初始学习、未学 → 学习、单次 / 10 次、部分成功、满级、等级不足、金币不足、错误职业 / 宠物主人、非法 count、重复请求、旧 expected_rank、乱序回包、断线 / 切宠物 | 服务端 13 条**已写未跑**;客户端乱序 / 换号 / 换连接 / 断线 / 切宠物 / 超时 **已实跑**(离线);宠物主人校验**未做**(无宠物技能协议) |
| 3 | 属性即时更新、重算 / 重登不叠加、保存后重登、旧档兼容、战斗 / 传送 / 交接窗口拒写、扣费与等级不丢一边 | 逻辑已落(被动只在 scene 重算、`kSkillChanged` 按比例保持 HP/MP、窗口拒写、同回调扣费改级);窗口拒写 / 不扣费**已写未跑**;即时重算与重登不叠加**未写专门用例**,留给联机冒烟看属性面板 |
| 4 | 32 技能逐项效果、最强叠加、驱散、冷却、被动不可施放、scene / battle 不重复加成 | 引擎按类别 17 条**已写未跑**(内存表行,形状同真表);32 个技能逐个数值核对**未做** |
| 5 | 截图新机制、宠物技能真实行动路径 | **未做** |
| 6 | ManaDelta 全链、群体只扣一次、回放 / 观战 / 重连一致 | 服务端填值与"群体一次扣蓝"**已写未跑**;客户端演出消费**已实跑**(离线 5 条 + 回归);观战 / 重连**未测** |
| 7 | 两端联网全流程 | **未做**(前置:服务端 proto-gen + 编译 + 测试) |

### 8.5 遗留

- 宠物技能(§6)整块、截图三种机制、Java 版(本机没有 `D:/luyuan/xuanming-server-mmo`,`PARITY.md` 待登记)。
- 玩家挂机 / 超时仍只普攻;没有职业技能 AI。
- 老 robot / 旧客户端仍能用 `skill_list`;robot 已改为剔除被动(服务端分支内)。
- 客户端两分支都没合入 main:服务端要先由负责人把 origin/main 合进本地 main(当前落后 19 个提交),再合 `feat/class-skills`;客户端 `feat/class-skills` 基于客户端 main `d6338af1`,可直接合。

## 9. 2026-10-20 主干整合与验证记录（Codex）

- 客户端 `feat/class-skills` 合入 `main`，保留最新主干的按请求号关联与类型化 RPC。技能书读写调用生成的 `SceneSkillClientPlayerRpc`，明确区分本地未发送与结果未确认，不再解析字符串错误。
- 从合并后的 `E:/work/xuanming-server-mmo` 权威源运行 `tools/gen_proto.ps1`、`tools/gen_messageids.ps1`；服务端正式生成器默认配置重生客户端 RPC 调用桩。`UpgradeSkill = 317`，主干推送方向注解保留。`SkillTable.cs` 也从当前服务端导表产生的 `generated/code/proto/skill_table.proto` 经 protoc 重生，结果与技能分支一致。
- 实际验证：`tools/client_compile_check.ps1` 编译当前 405 份运行时源码，0 错误；改动的跨区出包和装备核验 Editor 入口离线编译通过。PowerShell 出包、跨区与移动三份脚本语法检查通过。
- 使用刚编译的真实客户端程序集、现有 Google.Protobuf/NUnit 和 .NET 10 离线执行：`SkillBookClientTests` 20/20、`SkillBookMapperTests` 10/10、`ManaDeltaPlanTests` 5/5、`TurnPlanTests` 27/27、`EquipCombatEventsPlanTests` 16/16，共 **78/78 通过**。新增两例验证类型化 RPC 的本地未发送和信封提示码；未替换业务实现。
- 本次未执行 Unity Test Runner、界面截图和两端联网技能全流程；离线通过不代表这些项目已验收。独立 Java 服务器的规范路径 `D:/luyuan/xuanming-server-mmo` 本机不存在，四职业移植与 `PARITY.md` 仍待处理；本次不扩展实现。
- 工作目录约定见根目录 `AGENTS.md`：构建默认到客户端 `Builds/crosszone_player`，运行日志默认到客户端 `Logs`，命令行仍可覆盖。旧装备核验的原始日志与截图保留在 `Docs/Verification/forge-ui-qa-20261016`（本地忽略），避免在 `E:/work` 顶层继续生成临时目录。
