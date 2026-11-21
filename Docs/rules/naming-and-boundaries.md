# 命名规范与权限边界

> 对应根索引 [AGENTS.md](../../AGENTS.md)，移植自服务端仓 `AGENTS.md §2 / §8 / §9`（`docs/rules/naming-and-boundaries.md`）并对齐客户端目录结构。

## 1. 命名规范与语言约定

### 第一原则（强制）
所有命名（C# 类、接口、方法、属性、局部变量、事件、配置表列引用、测试名等）**必须清晰易懂且语义完全准确**，做到见名知意、零歧义：
- **带准业务语义与物理单位**：耗时、间隔、截止时间、字节数、数量须在命名中体现单位（如 `TimeoutMs`、`ExpireAtMs`、`RetryDelaySeconds`、`PayloadBytes`、`MaxRequests`），拒绝无上下文的 `data`、`info`、`flag`、`temp`、`val`、`Handle`、`Process`、`DoWork` 等泛化名或与当前行为不符的历史遗留名。
- **布尔值语义明确**：使用 `Is*`、`Has*`、`Can*`、`Should*` 明确表达真值含义，不制造双重否定或正反含糊的布尔名。
- **分层概念不混名**：严格区分「是什么」与「摆在哪」（如物品 `ConfigId` / `EquipKind` vs 槽位 `SlotId`，任务 `MissionId` vs 奖励内容引用 `RewardId`；详见 [engineering-principles.md §4](engineering-principles.md#4-实例与布局分层数据层--显示层强制)）。
- **术语对齐服务端现行架构**：统一使用 `Scene` / `Gate` / `Battle`（如 `SceneNode`、`BattleDirectLink`），不使用已废弃的 `GameNode` / `Centre` 等旧称呼。

### 客户端模块分层命名约定
- `Assets/Scripts/Net/`：底层帧编解码（`MuduoCodec`）、传输通道（`IFramedConnection`、`ClientTransport`）、请求认领（`OutstandingCalls`）、统一防护（`RpcGuard`、`GuardedRpcChannel`）与幂等键（`ClientRequestKeys`）。
- `Assets/Scripts/Game/**/*Client.cs`：按业务域划分的客户端门面/状态协调器（如 `BattleClient`、`TeamClient`、`GuildClient`、`RankClient`），收口本域 RPC 调用与权威数据同步。
- `Assets/Scripts/UI/**/*Model.cs`：不依赖 `UnityEngine` 组件生命周期的纯 C# 视图/业务推导模型（如 `EquipBagModel`、`TemporaryBagModel`），便于脱离场景直接单测。
- `Assets/Scripts/UI/**/*Window.cs` / `*Panel.cs` / `*Dialog.cs` / `*Root.cs`：uGUI 界面组件，遵守 C# 标准命名（公开类型/方法/属性 `PascalCase`，接口 `I*`，私有字段 `_camelCase`）。

### 中文协作与英文日志约定
- **协作产出全中文**：AI 回复、代码注释、设计/交接文档、Commit Message 统一使用中文。
- **运行期诊断日志用英文**：写入控制台与日志文件的诊断/网络/协议日志（如 `[rpc] late reply dropped`、`[client-request] key reused`）保持结构化英文，便于跨端与服务端英文日志对照检索；面向玩家界面展示的提示文案使用中文（或走 `Tip` 表文案）。

## 2. 禁止事项与权限边界

### 关联 Git 仓库与工作区约束
- 核心关联仓库（按 GitHub 地址定位，本地克隆路径因机器而异，代码与脚本不写死个人盘符）：
  - Unity 客户端（本仓库）：`https://github.com/luhailong-cpp/mmorpg-client`
  - C++/Go 服务端：`https://github.com/luyuan-cpp/xuanming-server-mmo`
  - Java 服务端：`https://github.com/luyuan-java/xuanming-server-mmo`
  - Python 服务端：`https://github.com/luhailong-python/xuanming-server-mmo-python`
  - 协议转 MySQL 工具库：`https://github.com/luyuan-cpp/proto2mysql`
- **只在 `main` 分支开发**：所有改动直接在 `main` 分支进行，禁止创建临时分支；确需工作树时使用托管工作树或放在工作区根目录之外，合并验证无未保存内容后清理。
- **工作区整洁**：构建产物、日志、临时验证证据统一放在已忽略目录（`Builds/`、`Logs/`、`.codex-artifacts/`）或系统 `%TEMP%`，不在仓库顶层新建杂项目录。

### 硬禁令
- ❌ **严禁手改生成目录与文件**：`Assets/Scripts/Proto/Generated/`、`Assets/Scripts/Net/Generated/`（含 `Rpc/*Rpc.cs` 与 `TipFaults.cs`）、`Assets/Scripts/Net/MessageIds.cs`、`Assets/Scripts/Table/Generated/`（含 `*Table.cs`、`*TableManager.cs`、`TableLoader.cs`）。
- ❌ **严禁遗漏或破坏 Unity `.meta`**：`Assets/` 下增删改移文件必须同步维护 `.meta`，不手造冲突 GUID。
- ❌ **严禁在小游戏可达路径开启线程或阻塞主线程**：禁止 `new Thread`、`Task.Run`、`Task.Delay`、`ThreadPool`、`HttpClient`、原始 `TcpClient` / `Socket`、异步 `File.*Async` 以及 `.Wait()` / `.Result` / `Thread.Sleep`（详见 [minigame-and-app-platforms.md](minigame-and-app-platforms.md)）。
- ❌ **严禁绕过质量与视觉门禁**：禁止用 `--no-verify`、跳过/删除失败测试、放宽 UI 像素比对容差、裁切遮罩截图或硬编码假样例文字冒充原图验收通过。
- ❌ **严禁未授权 Git 远端与破坏性操作**：禁止执行 `git push` / `git tag`；`git commit` 仅在用户明确要求时执行；禁止用 `git reset --hard` / `git checkout --` 销毁工作区既有改动。
- ❌ **严禁泄露凭据与越权访问**：禁止将密码、Token、签名密钥、License 写入 Git 跟踪文件、`PlayerPrefs`（如 Refresh Token）或日志；禁止擅自登录远端账号或触碰生产环境。
- ❌ **不要随意新建零散 Markdown**：除用户明确要求或正式模块文档外，不为普通日常小改动在 `Docs/` 下到处新建流水账 `.md`；跨端协议过渡与行为变更统一归口记录在 [ClientProtocolCompatibility.md](../ClientProtocolCompatibility.md)。
