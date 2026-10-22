# 客户端协议分支兼容

2026-10-19 的客户端同步同时保留本地恢复逻辑与远端神秘商人、归队、组队补位界面。服务端两套 Git 分支的新增协议尚未统一；本次没有改动服务端，没有猜测或分配新的消息号、字段号。

事实来源固定为两个服务端版本：

- `xuanming-server-mmo-cpp` 的 `origin/main`，提交 `fea7f5e9be55017597752bf7b4ec433bd3e12850`：`proto/team/team.proto`、`proto/scene/player_mystery_merchant.proto`、`proto/message_id.txt`。这是远端补位与神秘商人的协议来源，不代表已确认线上部署。
- `xuanming-server-mmo-cpp` 的本地提交 `85a457a55f6116f2715364bcf955fdd33137ae59`：`proto/team/team.proto`、`proto/mail/mail.proto`、`proto/rank/rank.proto`、`proto/message_id.txt`。组队开局恢复、邮件、排行榜、探针与交易采用这一套契约；Java 仓 `xuanming-server-mmo` 的 `game-proto/src/main/proto/proto/team/team.proto` 与 `game-proto/src/main/resources/contract/message_id.txt` 也使用这套恢复契约。

## 组队：同号、不同 wire type 的过渡桥接

| 消息字段 | 本地 CPP/Java 恢复契约 | CPP 远端补位契约 |
|---|---|---|
| `GetMyTeamRequest` 字段 2 | `string match_request_id`，wire type 2 | `bool regroup`，wire type 0 |
| `StartTeamMatchRequest` 字段 3 | `string request_id`，wire type 2 | `uint32 target_team_size`，wire type 0 |
| `TeamResponse` 字段 3 | `TeamMatchIntent match_intent` | 没有此字段 |
| `TeamView` 字段 13–16 | 没有这些字段 | 目标人数、入队时间、副本、补位能力 |

`.proto` 不能声明同号双字段。客户端继续使用本地 CPP/Java 恢复契约生成的 `Assets/Scripts/Proto/Generated/Team.cs`，由 `Assets/Scripts/Proto/TeamBackfillProtocol.cs` 的 partial 类型访问远端已有的 varint 字段。桥接读取和修改 `UnknownFieldSet`，只替换对应字段号、wire type 0 的值，保留同号的恢复 string、其他 wire type 和全部其他未知字段。原生成的序列化、解析、克隆和合并仍承担全部消息编码工作。补位枚举以 `TeamBackfillProtocol` 常量表示远端已有数值 2、15、16，未修改生成文件或发明新值。

服务端只解释自己认识的 tag，另一分支的不同 wire type 仍可作为未知字段保留。客户端只有收到 `match_fill_supported = true` 才允许发起补位；UI 与直接调用 API 都执行这一检查。开局请求同时携带原请求 UUID 和目标人数，持久化和重试保持同一份请求。已有正式 `MatchIntent` 时始终按其权威状态恢复，包括补位请求；没有 `MatchIntent` 的旧补位响应只在明确成功或权威队列核对队伍、目标人数、副本一致后解除本地记录。补位超时之后不会对忽略 UUID 的旧分支盲目重发。

`TeamProtocolCompatibilityTests` 覆盖双 wire type 的两个输入顺序、克隆、合并、清零、同号其他 wire type、未知字段保留和官方样例字节。同步时另外将上述两个真实 proto 分别用 protoc 生成隔离的验证类型，交叉解析并核对字节；这些验证别名类型只存在仓库外，不属于生产协议。

这是过渡桥接，不是新的服务端统一协议。正式统一 `.proto` 后，应先核对全部字段号和 wire type，再移除对应 partial 成员、恢复生成枚举引用并重生 `Team.cs`。若正式 schema 已包含这些属性而 partial 还存在，应立即停止生成，避免重复成员或误删恢复 API。生成器 preflight 必须检查所选 proto 仍包含字段 2/3 的两个恢复 string、`TeamResponse.match_intent = 3`，且尚未直接声明桥接的六个 varint 属性。

## 神秘商人：保留功能，默认未开放

CPP 远端契约的查询、购买、公告消息号为 264、265、266；本地 CPP/Java 契约的同号分别是邮件推送、网络探针提交、聚宝斋市场订单列表。两套消息号不能在同一连接上同时生效。

本次默认按本地 CPP/Java 契约生成：`GetMysteryMerchantInfo`、`BuyMysteryMerchantGoods`、`NotifyMysteryMerchant` 保留公开常量，未分配时值为 0。`MysteryMerchantClient` 只有在三项消息号全部非 0 且互不重复时才注册公告、发送查询或购买；默认角标显示「神秘商人 未开放」，窗口说明「当前服务器未开放神秘商人」。全部远端模型、窗口、资源、协议类型和原行为测试保留；测试显式注入独立假号，绝不把假号作为生产映射。

两套服务端分支统一并分配无冲突消息号后，从正式事实源重跑 `tools/gen_proto.ps1` 和 `tools/gen_messageids.ps1` 即可启用现有功能。生成清单和 whitelist 继续保留三个方法；当前源仓完全没有神秘商人 proto 时保留已入库类型，部分文件缺失时拒绝生成。

## 邮件和排行榜

采用本地 CPP/Java 的现行 schema 与消息号，保留客户端双方的行为实现。远端 `GetRankPage = 275` 已与本地探针方案冲突，必须映射到当前正式 `ClientPlayerRankGetRankPage` 的消息号；不能继续沿用旧生成值。邮件附件/清理等业务字段同样以本地现行 proto 为准，界面和回调适配该事实源。

## 组队写请求恢复（2026-10-21）

普通组队写请求在发送前保存 UUID 和完整正文。故障回包、未知传输结局或重建后加载的原件继续保留，只能按同一请求恢复；其他写不能覆盖它。只有此前没有未知尝试、且首次发送被 Gate 按原请求明确限流时，才清理本次未执行意图并允许新的写。旧未知请求重试遇到限流，只能证明这一次没有执行，不能抹掉之前的未知结果。清理本机原件失败时保留保护状态。
