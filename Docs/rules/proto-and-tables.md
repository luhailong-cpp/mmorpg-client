# 协议、配置表与跨端契约不变量（客户端必守）

> 对应根索引 [AGENTS.md 门禁命令与禁止手改目录](../../AGENTS.md#门禁命令与禁止手改目录)，整合移植自服务端仓 `docs/rules/proto-and-tables.md`、`docs/rules/invariants.md`、`docs/rules/bit-storage-and-table-ids.md` 与 `data/AGENTS.md` 中客户端必须共同遵守的跨端契约。C++/Go 版与 Java 版双服务端与本客户端共享同一套客户端契约（帧格式、客户端可见 proto、tip 错误码轴、配置表数据）。

## 1. 生成物边界与三步同步闭环

- **禁止手改任何生成产物**，也**禁止手写与 proto 消息重复的并行数据类**：
  - `Assets/Scripts/Proto/Generated/`（protobuf 消息与 `*_tip.proto` 枚举）
  - `Assets/Scripts/Net/Generated/`（`Rpc/*Rpc.cs` 服务调用桩、`TipFaults.cs` 故障判定表）
  - `Assets/Scripts/Net/MessageIds.cs`（消息号常量表）
  - `Assets/Scripts/Table/Generated/`（`*Table.cs`、`*TableManager.cs`、`*TableForeignKeys.cs`、`TableLoader.cs`）
  - `Assets/StreamingAssets/Tables/`（二进制 `*.pb` 配置表数据，由 `tools/sync_tables.ps1` 同步，启动时由 `ClientTableBootstrap` 加载）
- **服务端改动客户端可见 `.proto`、`Tip.xlsx` 或配置表后，必须按序跑完同步与编译闭环**：
  1. 直接在客户端仓根目录运行 `.\gen.bat`（一键串联：服务端导表 + `proto-gen` → 客户端 `tools/sync_tables.ps1` / `tools/gen_proto.ps1` / `tools/gen_messageids.ps1` / `tools/client_compile_check.ps1`）；仅改 `.proto` 可跑 `.\gen.bat proto`，服务端已跑过 `dev.bat gen` 可跑 `.\gen.bat client`；
  2. 若改动涉及公共签名或测试文件，一并确认受影响的 EditMode/PlayMode 测试程序集编译通过（避免 `*Rpc.cs` 与 `Proto/Generated/*.cs` 错位导致 Unity 进入 Safe Mode）。
- **字段号契约**：上线后 proto 与配置表 schema 字段号永不复用（废弃字段标 `reserved`）；客户端过渡期双分支协议兼容状态以 [ClientProtocolCompatibility.md](../ClientProtocolCompatibility.md) 为准。

## 2. Proto 字段类型与精度约束（严禁窄化截断）

客户端 C# 处理协议字段时必须保持与服务端一致的位宽与精度，**严禁强转窄化导致溢出或丢精度**：

- **大世界坐标与运动量用 `double`**：`Location`、`Rotation`、`Scale`、`Vector3`、`Velocity`、`Acceleration` 的 `x/y/z` 在协议上全部是 `double`。客户端在权威坐标缓存、距离判定与插值外推计算中应保持 `double` 精度，仅在最终赋值给 `UnityEngine.Transform` / `UnityEngine.Vector3` 渲染节点时再转为 `float`，且不得把截断后的 `float` 当作权威坐标回传或覆盖。
- **64 位核心数值必须全程用 `ulong` / `long`（严禁强转 `int` / `uint` / `float`）**：
  - **全部 64 位 ID**：`player_id`、`guild_id`、物品 `guid` / `item_id`、`scene_id`、`entity`、`buff_id`、`skill_id` 实例、`battle_id`、`team_id`、`tx_id`、`client_request_id` 等；
  - **基础战斗属性（`BaseAttributesComp`）**：`strength` / `stamina` / `health` / `mana` / `critchance` / `armor` / `resistance` / `speed`；
  - **货币与经验**：`CurrencyComp` 余额、交易金额、消耗/变动额、角色累计经验 `total_exp` / `exp_gain`；
  - **Unix 毫秒时间戳与单调代次**：`created_at` / `expires_at` / `expire_at_ms`、`session_incarnation` / `gate_call_seq`。
- **可安全按 `uint` / `int` / `float` 处理的字段**：配置表主键 `id`（`skill_table_id` 等 `uint32`）、`session_id`、`max_health` / `attack_power` / `defense_power`、`ViewRadius.radius`（`float`）。

## 3. 全局 ID 不变量（永久 ID vs SnowFlake，严禁混用）

全系统存在两类完全不同的 64 位 ID，客户端必须严格遵守以下红线：

1. **发号器永久 ID 无位布局、不单调、不连续**：`player_id`、`guild_id`、物品 `guid`、`battle_id`、`team_id`、`tx_id`、`snapshot_id` 等均由服务端发号器生成（63 位以内的打散计数器值）。
   - ❌ **严禁**尝试按 SnowFlake 位布局从这些 ID 中解析时间戳、区服号或节点号；
   - ❌ **严禁**靠比较两个永久 ID 的数值大小来判断创建先后或列表排序（列表排序与翻页**必须**使用协议显式下发的时间戳、`result_seq` 或序号字段）；
   - ❌ **严禁**把 64 位 ID 存进 `double` / `float` 或 JSON Number 走弱类型转换（超过 `2^53` 会静默丢失低位导致点错物品/角色）。
2. **`scene_id` 为服务端 `scene_manager` SnowFlake ID**：客户端同样只将其视为不透明 `ulong` 路由/场景标识原样存取与比对，不自行拆位。

## 4. Tip 错误码全仓单轴与故障判定

- **唯一事实源**：`TipInfoMessage.id` 由服务端 `data/tip/Tip.xlsx` 统一分配并导出为 `Assets/Scripts/Proto/Generated/*ErrorTip.cs` 枚举及 `Assets/Scripts/Net/Generated/TipFaults.cs`。
- **禁止手写错误码数字或私有故障表**：
  - 业务判断具体错误原因时，一律引用生成的 `*ErrorTip` 枚举常量（不写裸数字，防止不同模块号段理解偏差）；
  - 区分“服务端内部故障（计入熔断 / 结果未知）”与“正常业务拒绝（如余额不足、等级变化、限流 `1008`）”时，**统一使用生成的 `TipFaults.IsFault(tipId)`**（已集成在 `RpcGuard` 中），禁止在各业务 `*Client` 里手写第二份故障码集合。

## 5. 配置表（`*TableManager`）与位图 / 删表安全规则

配置表权威 schema 在服务端 `data/schema/<sheet>_table.proto`，导表器生成本仓 `Assets/Scripts/Table/Generated/`：

1. **热更快照契约：只存 `id`，用时现查，不存行引用（强制）**
   - `*TableManager.Load()` 每次热更会构建全新不可变快照并原子替换旧快照；
   - **客户端业务类、UI 项、容器和闭包里只允许保存 `uint configId`，用时调 `*TableManager.Instance.FindById(id)` 获取当前快照行**；长期缓存 `*Table` 对象引用会让界面在配置热更后永远读到旧数值。
2. **区分单值主键与可重复主键（`(cfg_multi) = true`）**
   - 单主键表暴露 `FindById(id)`（可能返回 `null`）；声明了 `(cfg_multi) = true` 的多行表（如掉落组）只生成 `FindAllById(id)`（返回冻结的 `IReadOnlyList<T>`，永不为 `null`），不要强转修改返回的只读集合。
3. **跨平台文件名大小写严格走生成常量**
   - JSON 表文件名保留表名大小写，二进制 `.pb` 文件名全小写；加载表文件时**必须使用 `*TableManager.FileName(useBinary)` / `JsonFileName` / `BinaryFileName` 或 `TableLoader`**，严禁手拼文件名字符串（Windows 不报错，Android / iOS / Linux / 小游戏包会直接 `FileNotFound`）。
4. **查表判空与位图 / 删表容错**
   - 任何时候调 `FindById(id)` **必须处理返回 `null` 的情况**（如策划下线/删除了旧配置行或客户端表尚未热更到位），安全跳过或显示占位说明，绝不抛空引用阻塞登录或界面渲染；
   - **领取状态归属业务表**：任务、成就、活动等领取状态归属各自业务表 ID，不绑公共 `Reward` 表 ID（`Reward` 表只表达发什么奖品）；
   - **完成数 / 进度百分比统计**：若服务端下发紧凑完成位图（`bytes`），客户端统计“当前已完成数量 / 总进度”时必须**按当前 `*TableManager` 里的有效表行集合过滤计数**，不得直接对原始位图做 `popcount`（已删除的历史废弃 ID 仍会在老玩家存档位图里保留 `1` 位）；且客户端不得截断或篡改未知高位 bit。

## 6. 客户端长连接、限流熔断与幂等契约

完整演进记录与各模块细则见 [ClientProtocolCompatibility.md](../ClientProtocolCompatibility.md)，核心红线如下：

- **请求认领与双连接分流**：请求-应答（`id != 0`）严格按 `id` 由 `OutstandingCalls` 认领且恰好回调一次；单向消息与推送（`id == 0`）按 `message_id` 派发。回合制战斗操作（`SubmitBattleAction` / `GetBattleState` / `StopWatchBattle` / `SetAutoBattle`）**只走 `BattleDirectLink` 战斗直连，未就绪时本地立即失败并置灰，绝不回落走大厅 Gate**。
- **统一防护（`RpcGuard` + `GuardedRpcChannel`）**：所有长连接业务 RPC 经 `IRpcChannel` 统一受滑动窗口限流（对齐 `MessageLimiterTableManager`）与**按服务域分桶**的三态熔断保护。**红线**：`1008` 限流与正常业务拒绝绝不计入熔断；`circuit open`（`NotSent`）先于限流返回，绝不消耗限流配额、绝不触发幂等重发，也不得把业务客户端打入需要重连的锁死态。
- **写操作幂等键（`ClientRequestKeys`）**：带幂等键的写操作在每次玩家主动点击且通过本地校验后生成一次非 0 随机 64 位键；拿到明确结果前，**仅在超时或 `1003` 且 `(PlayerId, 连接身份)` 未变时用同一键原样重发一次**；绝不把旧键复用给新的业务操作（否则服务端回 `1005`）。
