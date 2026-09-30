# V14 全角色本地试玩接入（2026-09-29）

用户指定美术源为 `../image/qdao_original_roster_v14_hd`，要求包含该目录内所有其他角色。
当前目录实际具有完整动作库存的身份为 04、05、06、07、08、09、10、14、15、17、20，共 11 名。
每名有 8 个方向、每方向 16 张行走图及 1 张独立待机图，即 136 张动作 PNG；运行包再加入同身份肖像，共 137 PNG。
客户端原有 00–03 保持 V13 的 8 方向、16 帧，合计 15 名原人物可供本地开发版选择。
历史索引中其余身份在指定目录没有完整动作包，本次没有生成缺失图片或伪造库存。

| 身份 | 名称 | 当前动作来源（相对 recovery-20260921） | 接入形式 |
| --- | --- | --- | --- |
| 04 | 山岳守卫 | 04-delivery-preview/revisions/review-set-v3/runtime | V14 混合：112 张旧 512，24 张原生 1024 |
| 05 | 天音少女 | 05-delivery-preview/final/runtime | V14 混合：60 张旧 512，76 张原生 1024 |
| 06 | 雷法少年 | 06-final/runtime | V14 全 1024 |
| 07 | 月影少女 | 07-tools/candidate/07_moon_shadow_assassin_girl | V14 开发版试玩候选 |
| 08 | 炼丹童子 | 08-delivery-preview/revisions/final-v1/runtime | V14 全 1024 |
| 09 | 竹弓少女 | 09-delivery-preview/final/runtime | V14 全 1024 |
| 10 | 赤枪少女 | 10-delivery-preview/current | V14 全 1024 |
| 14 | 唤雪少女 | 14-delivery-preview/assets | V14 全 1024 |
| 15 | 水龙书生 | 15-delivery-preview/runtime | V14 开发版试玩候选 |
| 17 | 灵篆书生 | 17-delivery-preview/revisions/final-20260923/runtime | V14 全 1024 |
| 20 | 星阵少女 | 20-final | V14 全 1024 |

## 资源与审核语义

先前八名角色的 1088 张动作图已与最新美术源逐文件 SHA256 核对，相同且无缺失。
本次补齐 04、07、15，共 408 张动作图；动作图原字节复制，不缩放旧动作冒充高清。
混合包逐帧保持 512/52 PPU 或 1024/104 PPU，世界尺寸和脚底锚点一致。

04 的后续离线复核已覆盖八方向，接入绑定该真实复核及旧图冻结快照；没有改写旧 manifest 的历史 pending。
07、15 的当前全部动作已落盘，但源审查尚未完成实时动态美术审核。
两者保留 `pending_dynamic_review`，仅由 `QdaoLocalPlaytestContract` 在 Editor / Development Build 中
按精确 manifest、activation SHA 启用。发行版继续拒绝候选；原正式资源合同仍要求视觉审核通过。
结构测试、运行测试和本地试玩不替代正式美术批准。

## 选角与进入场景

新建角色默认展示炼丹童子，完整 V14 优先排列，提供“上一人物 / 下一人物”按钮和方向、帧数说明。
已有角色身份不变；用户主动选择职业默认以及名字被服务器退回后的选择均保留。

本机登录后的 3023 原因是 `zone_1_db.player_database` 缺少 profile_component、asset_op_ledger、settlement_ledger。
已在本地备份该表并按 proto 15、16、17 顺序补齐，未删除账号和原角色。
官方只读迁移计划已无待执行语句。真实服务器已接受独立试玩角色 202，进入地图并呈现炼丹童子 V14。
额外移动测试四向行走正常，但故意越墙阶段出现边缘纠偏失败，不能写成完整移动验收通过。

验证与构建证据位于 `../../tmp/qdao-v14-player-rolefix-20260929-evidence/`。
全名单 Unity EditMode 15/15、PlayMode 11/11 均通过，包括确切 15 名名单（11 V14、4 V13）、
真实角色创建/回退，以及全部可用人物的八方向逐帧播放和独立待机。
新增三名的 879 个资源/metadata/index 文件已从验证候选同步至正式目录，GUID 冲突 0、SHA 差异 0。
全角色 Windows Development 播放器构建成功：errors=0、warnings=499，输出
`../../tmp/qdao-v14-player-all-roster-20260929/mmorpg.exe`。499 条构建警告仍保留在完整构建日志中。
最终实际联网试玩结果见证据目录下 `all-roster-live-*` 的玩家日志与截图。

04、07、15 已分别使用独立本地试玩账号，通过真实 RoleFlowUi 创建、服务器保存身份、进入地图，
玩家 203、204、205 的 ActorWorld / Animator 均报告对应身份且 version=14，三次均 `RESULT=PASS stage=in_game`。
已查看三名实际进场截图，未把仅有编辑器结果当作 Player 运行证据。
