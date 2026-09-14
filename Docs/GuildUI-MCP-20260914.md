# 帮会设计切图与 Unity 官方 MCP 接入

2026-09-14。以素材仓库 `designs/guild-ui-v2` 的已确认设计为输入，通过当前工程配置的 Unity 官方 relay MCP `Unity_RunCommand` 导入与验证原生 Sprite。原资源已是独立透明 PNG，本次使用这些已切好的原件，没有重绘或重采样整屏图。

## 资源

`Assets/Resources/UI/Ugui/GuildV2/` 共 27 张 Sprite，原 9 张路径与 GUID 保留，新增 18 张。来源、SHA-256、原尺寸、运行时资源键和 Unity 九宫格边距见目录内 `manifest.json`。

- PNG 与批准的源文件逐字节一致，27/27 SHA-256 相同。
- Sprite Single、100 PPU、Full Rect、Clamp、Bilinear、保留 Alpha、关闭 Mipmap、无有损压缩、无非二次幂缩放。
- 设计边距 L/T/R/B 转换为 Unity L/B/R/T。窗口为 `(97,74,97,99)`；主按钮 `(66,30,66,30)`；次按钮 `(30,27,30,27)`；输入/字段 `(12,12,12,12)`。
- 标题牌、关闭钮、红穗与圆徽章保持比例。文本由 TMP 生成。
- 头像与商品图片属于设计包资源；成员协议未提供头像或姓名，正式成员列表继续显示真实角色编号，不以演示头像表示生产玩家。

复现导入：将素材仓库与客户端并列放置，在 Unity 编辑模式调用菜单 `MMORPG > UI > Import approved guild sprites`。其他目录结构可调用 `GuildUiAssetImport.Import(sourceDirectory)`。导入前核对源清单和全部 PNG 哈希；导入后检查实际 Sprite 尺寸、边距与现有 GUID。

## 原生窗口

入口仍为主城「帮会 [G]」。`GuildUiArt` 独立读取帮会资源包；居中标题、横向页签、轻分隔、公告装饰和关闭红穗对齐已完成设计。总览指标只读取服务端快照；原有成员筛选、分页、排行、公告和确认弹窗继续使用既有业务事件。

基础帮会沿用已有 7 个 RPC。捐献、活动、商店仍显示「暂未开放」。设计稿中的签到、任务、演示交易没有生产服务契约，本次不模拟成功或修改服务端。尚未执行线上服务器验证。

## 官方 MCP 证据

本机调用与原始结果保存在 `E:/work/output/guild-mcp-20260914/`。导入调用实际返回 `isCompilationSuccessful=true`、`isExecutionSuccessful=true`。

正式工程完成官方 MCP 导入后，共享编辑器后续命令超时。为保留原编辑器未保存场景，本次验收使用独立物理副本 `E:/work/tmp/guild-mcp-verify-20260914`：7 个帮会源码文件与 27 张 Sprite 哈希均匹配正式工程。独立 Unity 进程完成 28/28 EditMode 测试（0 失败、0 跳过）及 20 张双分辨率截图，两步退出码均为 0。

验收输出归档于 [VerificationEvidence/guild-official-mcp-20260914](VerificationEvidence/guild-official-mcp-20260914/)。[完整记录](VerificationEvidence/guild-official-mcp-20260914/validation.json)包含工具执行、资产哈希、源码一致性、测试结果、截图索引和实际视觉检查范围。

[总览截图](VerificationEvidence/guild-official-mcp-20260914/01-overview_2560x1080.png) · [成员截图](VerificationEvidence/guild-official-mcp-20260914/02-members_1920x1080.png) · [切图索引](VerificationEvidence/guild-official-mcp-20260914/sprite-contact-sheet.jpg)。截图使用明确标记的离线样例；未验证在线服务器。许可服务的非阻塞查询诊断保存在截图日志中，不影响两步完成结果。