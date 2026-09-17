# 仙友会原生 UI：群组、世界传音与谣言

本模块承接 `E:/work/image/designs/social-ui-v1` 的道家 Q 版设计。使用 `Resources/UI/Ugui/SocialV1/` 的独立 Sprite 与 TMP 原生文字：玉绿标题、象牙纸面、细暖金边、Q 版头像与少量灯笼短穗。主窗口按 2560×1080 画布制作，由 CanvasScaler 适配宽屏；不把整屏效果图当作客户端 UI。

## 正式入口与服务边界

`SocialUiRoot` 自注入主城，入口为「仙友会 [C]」，设计坐标 `(68,912)`，Canvas 排序 190，HUD 单独排序 140。O 已被活动使用，因此社交采用 C。退出、断线、换角、切换 Gate 与战斗遮罩会关闭相应窗口。聊天输入时 C 不作为快捷键；Esc 先关闭详情，再关闭主窗口。用户打开前的焦点会在关闭后恢复。

当前服务端聊天 v1 **支持 World 和 Private**，Team 与 System 尚未开放。本模块的正式 UI 仅接入 World，未接入私聊入口；界面保留当前、世界、帮派、队伍选择，其中只有世界可拉取与发送。群组和系统谣言显示清晰的未开放状态，不加载任何假道友、假群组或假传闻。

世界使用生成处理器常量：发送 RPC 61，历史 RPC 28。历史一次取最多 50 条，服务端倒序结果在界面中按时间升序显示。现有协议没有聊天推送、消息游标或稳定消息 ID，所以本模块提供明确刷新操作；不宣称即时推送已经上线。

发送流程为：输入校验 → 真实 RPC → 接受响应 → 拉取权威历史。发送请求使用 UUID 作为业务 `request_id`；它与传输层请求序号不同。只有发送响应没有错误才清除这次已确认的草稿，期间另写的草稿保留。未拿到历史之前不插入“已发送”的假消息。

服务端正文上限为 512 UTF-8 字节，界面进一步采用最多 120 个 Unicode 码点的文案范围。空白不可发送，中文输入法组合确认不应触发发送；Shift+Enter 换行。显示消息时 `richText=false`，玩家正文不能成为 TMP 富文本标记。

`SocialClient` 通过现有 `IBattleTransport` / `GameClientBattleTransport` 工作，不增加网络栈，不调用生成 `HandlerRegistry.Register`。通用 GameClient 已检查外层 ErrorMessage；适配器再检查发送/历史响应内层 ErrorMessage。明确的外层服务器拒绝允许用户稍后重试；超时等结果不明的请求进入待重连状态，不进行盲目重发。回调同时核对角色、连接对象与请求代次，旧角色或旧 Gate 回包不可进入新会话。

聊天协议只有玩家 ID、正文、频道与时间。正式消息不编造等级、称号、肖像、道具卡或坐标，未知角色显示道友编号与待同步资料。协议未提供结构化谣言字段，不能把任意 System 文本解释成妖兽倒计时。

## 显式离线预览

`SocialPreviewHost` 不会自注入，也不会被生产 Root 创建。它显式构造 `SocialState(preview:true)`，用于查看和验证完整设计。

- 群组：三栏群列表、消息、资料；搜索、创建、群主编辑、成员资料、邀请选择、置顶、消息提醒、退出二次确认与独立群草稿。
- 世界：当前/世界/帮派/队伍各有独立消息和草稿；原生输入、表情插入、道具与坐标分享卡、本地角色资料；收到示例消息保留正在阅读的像素位置，回到最新可清除提示。
- 谣言：明确提示“本频道为系统传闻，无法发言”，没有发言输入框；类别、关键词筛选、无结果恢复、详情、可控时钟倒计时与已结束状态。倒计时结束后正文不会继续说“三分钟后”。

所有离线操作只改变本地对象，不向其他玩家发消息。预览里的坐标只打开详情，不冒充实际寻路。`ResetPreview()` 恢复演示；`SimulateIncoming()` 添加一条本地消息。

## 主要 API

- `SocialState(bool preview=false, Func<long> clock=null)`：毫秒时钟可控，预览构造自动装入样例；正式状态初始为空。
- `SocialState.SelectPage / SelectChannel / SelectGroup / FilterRumors`：切换页面、频道、群组与传闻筛选。
- `SocialState.LoadDemo`：仅预览允许；正式状态调用会抛错。
- `SocialClient(IBattleTransport, SocialState, Func<object> connectionIdentity)`：世界聊天生产适配器。
- `SocialWindow(Transform, SocialState)`：纯原生窗口，公开 `Root / State / IsVisible / HasModal`。
- `Window.Show(SocialPage.Groups|World|Rumor)`、`Back / Hide / ResetSession / Tick / Dispose`。
- `RefreshRequested(SocialChannel)` 与 `SendRequested(SocialChannel,string)`：窗口意图，由 Root 接至真实服务。

## 验证入口

EditMode 测试程序集：`MmorpgClient.Tests.EditMode.Social`。测试覆盖正式/演示隔离、独立草稿、Unicode 长度、群权限/邀请去重、只读谣言与倒计时、协议字段、成功/失败、旧回包、Gate 与角色切换、明确拒绝及未知结果。

Editor 截图与显式预览资源生成由 `Assets/Editor/Social/SocialUiVerification.cs` 统一提供；与邮件一起由协调任务执行 Unity 原生控件点击、截图、编译与测试。本文记录已实现范围，实际通过数量与截图结果以执行报告为准，不把源文件存在等同于 Unity 验收通过。

- RPC 重试边界：外层 `server tip=1005/1006/1008/1010` 属于明确拒绝，可重试；`1003 ServiceUnavailable` 也可能由上游超时产生，发送结果视为未知，保留草稿并隔离至连接切换，避免用新业务 UUID 重复发送。
