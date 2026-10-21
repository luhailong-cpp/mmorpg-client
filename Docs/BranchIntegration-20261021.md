# 分支与归档工作树整合记录（2026-10-21）

本次先从 `ad5a5cdc` 快进到远端 `ce984904`，保留新字体、角色运行时索引、包锁定与登录诊断工具，再逐一检查全部本地分支和 `origin` 功能分支。`origin/HEAD` 只是默认分支的符号引用。

| 分支 | 审计时末端 | 处理 |
| --- | --- | --- |
| `codex/guild-team-roster-v12` | `0eadd618` | 已是主干祖先 |
| `codex/npc-showroom-20261016` | `fc2ea1bf` | 展示室代码、场景、23 张原图、导入工具及证据已由 `29a04be6` 等快照带入主干；本次合并 `81d3f9f0` 补齐分支祖先关系 |
| `feat/class-skills` | `2b484d23` | 已是主干祖先 |
| `feat/equip-enhance-inlay` | `359a803d` | 已是主干祖先 |
| `feat/equip-forge-ui-logic` | `d250c38a` | 已是主干祖先 |
| `feat/minigame-feasibility` | `6de7eb92` | 已是主干祖先；另保全 7 个未提交原型文件，见下文 |
| `feat/mystery-merchant` | `746a3e19` | 已是主干祖先 |
| `feat/team-backfill` | `750bddf6` | 已是主干祖先 |
| `fix/qdao-pet-companion-vector3-alias` | `8280cb13` | 已是主干祖先 |
| `origin/codex/guild-team-roster-v12` | `795f72e4` | 已是主干祖先 |
| `origin/snapshot/all-changes-20261016` | `c4764ade` | 已是主干祖先 |
| `origin/snapshot/all-changes-20261018` | `c9cc7cc3` | 已是主干祖先 |

NPC 合并仅有 23 个 `.png.meta` 的 add/add 冲突。主干已保存 Unity 展开的完整导入配置，分支是相同 GUID 和 2048 上限的简写配置；全部保留主干版本，合并前后文件树完全相同。

归档工作树的未提交内容也逐文件比对：

- `channel-team-client-src-20261010`：没有独有业务源码；大量换行和 Unity 导入元数据，旧字体缓存与已更名图片的孤立 `.meta`。保留主干较新的字体、NPC 导入和角色索引，不恢复旧删除。
- `mmorpg-client-equip`：装备运行时、属性加成、战斗属性和测试已在主干，主干还包含锻造、镶嵌和后续界面更新。旧截图夹具已由新的装备验证入口及更完整的属性/装备测试覆盖，不覆盖现有界面。
- `mmorpg-client-forge`：99 个文件与主干一致，110 个缺失文件是已由类型化 RPC 替代的旧 `HandlerRegistry`/逐方法 Handler；另两处是旧表生成结果。生成源码保持当前权威协议输出。
- `mmorpg-client-forgeui`、`mmorpg-client-wt-main-sync`、`mmorpg-client-wt-merchant`、`mmorpg-client-wt-teamfill-fix`：工作树干净。
- `mmorpg-client-petvec3`：唯一未提交文件与主干逐字节相同。
- `mmorpg-client-wt-teamfill` 与 `mmorpg-client-wt-teamfill-rb`：补位同步已在主干；旧稿与主干差异来自后续队列时序修复、协议兼容和测试加强。`TeamBackfillQueueSync.cs` 本体与主干相同。
- `wx-minigame-spike/client-wt`：7 个确实未跟踪过的 WebSocket/开发桥源码，已逐字节保全到 `tools/experimental/minigame`，附来源 SHA-256 和用途边界。正式客户端继续使用当前 TCP 流程。

验证：运行时 405 个 C# 文件编译通过（0 错误）；NPC 两个编辑器入口编译通过，原图导入回归 7/7 通过；小游戏开发桥回归 24/24 通过，原型 C# 分别按 Standalone / WebGL 条件独立编译通过。详细本地审计和日志位于被 Git 忽略的 `.codex-artifacts/branch-audit-20261021/`。

本记录不删除分支或归档工作树。清理目录时仍需保存旧工作树中的历史原始状态；业务功能不应由旧生成代码或旧资源快照覆盖主干。
