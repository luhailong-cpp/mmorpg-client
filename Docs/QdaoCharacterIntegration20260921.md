# 保留人物正式身份接入与运行验收（2026-09-21）

本次范围为原 ID 00–10、14、15、17、20，不重排、不恢复已删除人物。本页记录本机接入与验证；历史 Unity XML、隔离工程截图和模型提示词不能替代本次证据。

## 当前素材边界

- 正式可用原人物为 00–03 的完整 V13，512×512、52 PPU。
- 正式 V14 目录尚无完整已批准人物，不能生成发布索引或宣称存在已验收的实际 1024 行走截图。
- 04 保留 104 个 V13 walk，新增有效候选 14 个，缺 10 个；05 为 52+12，缺 64 个；06 为 16+5，缺 107 个。
- 07、08、09、10、14、15、17、20 各缺 128 walk 和 8 idle。候选不计为批准，原肖像的存在不代表运行时包已完整。
- 逐人物、逐方向缺帧和肖像/JSON 状态见素材仓库 `qdao_original_roster_v14_hd/mixed-preparation/client-integration-20260921/retained-roster-inventory-detailed.json`。

## 正式身份链

新增独立 `string appearance_id`，与 `class_id/gender` 分离。创角请求和账号角色摘要保存同一稳定字符串；首次入场沿用既有 CAS 存盘流程补入角色 profile。AOI、队伍成员和战斗快照继续传递该身份，角色列表、主城实体、队伍头像和战斗表现均优先读取显式 ID。

空 ID 的旧存档继续按原职业/性别解析 23–30。新选择只枚举保留且具有完整批准资源的原人物；显式 ID 缺资源时不改写存档、不借另一人物外观。同 ID 完整 V14 优先、完整 V13 回退，沿用逐方向加载和引用计数。

战斗冲刺与返回使用同身份 E/W walk，帧进度由实际位移计算，保留现有位移时间。攻击、施法没有新增动作帧时仍使用已有降级表现。角色切换、动作中断、销毁和残影各自释放其资源引用。

## 运行入口与证据规则

正式入口仍为 `Assets/Scenes/Bootstrap.unity`，选角 UI `RoleFlowUi` 有独立外观选择。`DevAutoPilot -appearanceId <原ID>` 复用正式创角/选角回调及网络流程，检查回包、ActorWorld 和实际 animator 的身份；`-requireAppearanceRole` 在重登列表缺失时直接失败，禁止创建替代角色。

`tools/run_move_test.ps1 -AppearanceId <原ID>` 连跑两次独立播放器并核对同 player_id、appearance_id 和重登位置。该自动选择流程不等于人工点按 UI 验收，也不单独证明战斗完成。

真实 Unity 验收复用素材仓库 `qdao_original_roster_v13/tools/run_unity_tests.ps1`，必须显式指定 `-Editor`、`-Project`、新 `-Run` 和已保存 `-InputSnapshot`。本机 Editor 为 `E:/Unity/6000.6.0f1/Editor/Unity.exe`；隔离迁移目录为 `D:/luyuan/wuxingqitan/tmp/qdao-original-live-candidate-20260921`。

走帧捕获从实际完整资源合同的每个含 native-HD 帧的方向取首帧；实际控制器必须处于 Run、有正位移且渲染器确实引用该 PNG。正常/最近镜头同 simulationFrame，记录输入 SHA、资源 SHA、尺寸、PPU、pivot、投影和裁切；至少两个方向。`verify_mixed_walk_captures.py` 的绑定校验与逐图目视审批是不同门禁。

## 本次运行结果

执行中；最终结果将在实际 Unity、服务端和正式流程结束后补齐。现有编译日志位于工作区 `tmp/qdao-integration-20260921`，不能将编译成功或断网 UI 夹具截图称作正式联网验收通过。

首次沙盒账号启动 Unity 因无授权退出（198）；切换本机用户上下文后许可证可用。正式编译首先暴露公会夹具旧 `Contribution` 字段和 Tianyong 测试程序集缺 Protobuf 引用；按当前协议字段及既有 Battle 测试程序集配置作最小修复，保留原失败日志。

Unity 导入会将部分仅含 GUID 的旧 `.meta` 展开为当前导入设置。本次使用固定接手提交 `fc0a8dc` 比较作者文件和 GUID，并另记历史安全基线 SHA；该审计不构成替换旧基线或批准元数据迁移。旧发布门禁在无法证明旧资源保护时继续拒绝发布。

本任务未调用图像生成工具，未新绘或放大旧人物帧。历史新增高清候选仅有内置生成入口记录，具体 GPT Image 型号未核验。
