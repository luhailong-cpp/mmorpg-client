# 聚宝斋原生 UGUI
本模块将 `image/designs/jubaozhai-ui` 的七张最终设计稿拆为独立皮肤、头像和商品图，用原生 UGUI 与 TextMeshPro 绘制可变化内容。统一玉绿、米白、暖金风格与四门派：破军、玄霄、逐风、丹心。

## 使用
- 游戏内进入角色后，左栏“聚宝斋 [U]”打开；U 可切换，Esc 先关详情/说明弹窗，再关主窗口。输入搜索文字时不触发 U 快捷键。
- 独立离线演示：打开 `Assets/Scenes/JubaozhaiPreview.unity` 并运行。该场景显式装载样例商品；正式入口默认没有样例数据。
- 可复用入口预制：`Assets/Prefabs/UI/JubaozhaiOfflinePreview.prefab`，仅用于离线查看。
- 重新裁图：Unity 菜单中的 Jubaozhai 构建入口或通过官方 MCP 执行 `JubaozhaiAssetBuilder.Build()`。
- 窗口验收：`JubaozhaiUiVerification.CaptureAll()`。在隔离场景绘制实际窗口并验证按钮，无网络请求；原场景不被保存覆盖。

## 内容和交互
角色、宠物、武器、防具、套装、法宝、首饰、召唤令及对应子类；原生名称/编号搜索、排序、分页、唯一选中、收藏筛选、详情和空状态。公示、拍卖、货架保留可访问入口；规则与估价以未开放提示呈现。

正式模式只接入浏览、详情、收藏与货架查询（聚宝斋 P1），不执行下单、支付或卖家消息；收藏由服务端按账号保存，离线演示中的收藏仅为当前界面会话状态。样例价格、编号与期限不代表线上商品。

## 数据接线
正式入口走 `JubaozhaiClient` + 服务端分页。`JubaozhaiUiRoot` 拿到 `GameClient` 时创建 `MmorpgClient.Game.Jubaozhai.JubaozhaiClient`，与窗口共用同一个 `JubaozhaiState`；打开窗口（`Toggle`）调用 `JubaozhaiClient.Open()`，State 进入服务端分页模式。协议为服务端 `proto/trade/jubaozhai.proto` 的 `ClientPlayerJubaozhai`（C# 命名空间 `Trade`，消息号 `MessageIds.Jubaozhai*`）。

- 浏览：筛选、搜索、排序、翻页只改 State 并触发 `QueryChanged`，由 client 发 `BrowseListings`；「货架」分区走 `GetMyShelf`。同一时刻只发一个请求，在途期间的查询变化合并为回包后补发一次最新查询；回包按发送时的 `QueryVersion` 注入，过期回包丢弃。服务端模式下 `GetPage` 原样返回注入的那一页，本地不再过滤、排序或改页码。
- 编码：类目 = 客户端 `JubaozhaiCategory` 值 + 1（服务端 9 = 游戏币，客户端暂无页签，收到直接跳过）；子类 0 = 全部，k = `JubaozhaiCatalog.SubcategoriesFor(类目)` 的第 k 个标签（角色类目即门派）；价格 = `price_fen / 100`；剩余时间按回包 `server_now_ms` 校正后的服务端时钟计算。请求不带 player_id，身份只取 gate 会话。
- 收藏与详情：收藏按钮只发 `SetFavorite`，服务端确认后才改显示（不做乐观更新），在途时提示“请稍候再试”；打开详情时补拉 `GetListingDetail` 的描述，同一页每条只拉一次。
- 错误：业务结果读回包体 `error_message`，`trade_error` / `common_error` 映射为中文文案，未收录编号给出带编号的兜底文案。只有 `GameClient.Call` 的 `rpc timeout` 会隔离请求，直到真实断线或换 Gate；其余传输错误只提示文案。
- `State.SetListings(listings, serviceAvailable: true)` 仅用于离线或整份快照注入（调试、验收），`LoadDemo` 仅用于离线演示；两者与 `Reset` 都会退出服务端分页模式。离开角色、换角色或断线会清理会话；战斗层显示时只清窗口，不重置 client。

未开放项（入口保留，点击只提示或显示空页）：购买、联系卖家、估价、规则说明；拍卖分区（服务端回 `TradeFeatureDisabled`，显示空页与“拍卖尚未开放”）；上架、下架、支付与资产移动；按区筛选（`zone_filter` 仅全服市场生效，客户端暂不提供）；游戏币类目页签。详情打开不代表成交。

协议与消息号由脚本生成（Codex 执行）：`pwsh -File tools/gen_proto.ps1 -ProtoRoot E:\work\xuanming-server-mmo`、`pwsh -File tools/gen_messageids.ps1 -ProtoRoot E:\work\xuanming-server-mmo`。前置：服务端已导表生成 `generated/code/proto/tip/trade_error_tip.proto`，且 `proto/message_id.txt` 已分配 `ClientPlayerJubaozhai*` 编号。

## 资源与验证
- 独立 PNG/Sprite：`Assets/Resources/UI/Ugui/JubaozhaiV1/`。
- 来源、裁切坐标和九宫格参数由构建脚本输出清单记录；源原图不改。
- 编辑器结果：`.codex-artifacts/jubaozhai-ui/`。
- 官方 MCP 执行记录：`E:/work/output/jubaozhai/qa/`。

2026-09-14 官方 MCP 离线验收通过：36 张 Sprite、6 项模型断言、20 张两种分辨率的原生 UI 截图与交互检查，280 个运行时文件编译无错误。该轮未覆盖 PlayMode 联网全链路；当时的未命名场景保留于 `Assets/Scenes/Recovery/JubaozhaiOriginalUntitled_20260914.unity`。

2026-09-17 聚宝斋 P1 客户端真实联网验收通过（本地测试栈、`Market.Scope=zone`）：

- 离线最终回归 **102/102**：聚宝斋客户端及分页 50 例、模型 6 例、TCP 生命周期 8 例、`BattleDirectLink` 会话 38 例。主仓运行时 **307 个源码文件编译通过，0 错误**。真实重登曾暴露 `GateTcpClient` 主动关闭异常逃逸、关闭后 `Connected` 空引用及线程退出唤醒问题；已修复并同步主仓。本地回环 TCP 回归保留修复前 3 通过/5 失败及修复后 8/8 通过的证据，没有忽略异常或放宽断言。
- 真实 PlayMode **1/1**、退出码 **0**，日志标记 `TRADE_CLIENT_LIVE_OK`。专用账号 `robot_9411`、角色 `1014` 经 `127.0.0.1:8081` 登录一区 Gate `127.0.0.1:10000`，实际浏览和查看商品 `301`、等待服务端确认收藏、主动断线清空窗口状态，再经新的真实 TCP 连接登录同一角色；“仅看收藏”仍返回该商品。拍卖页实际返回未开放提示。商品由正式 `TradeAdmin` 在本地造种，夹具不注入商品、不模拟业务回包，不执行下单、支付或上架。
- 保存了浏览、详情、收藏、重登后收藏、拍卖未开放 **5 张 1920×1080 真实窗口截图**。截图将当前联网生产 Canvas 临时交给 RenderTexture 相机同步渲染并还原，没有重建窗口或装载演示数据。336 项生产输入文件在运行前后哈希无漂移；另存截图 SHA256 和真实响应写回客户端状态的 JSON 哈希，后者不是原始 TCP 抓包。
- 验收运行于独立快照 `E:/work/tmp/trade-client-verify-20260916`，未控制主 Unity。生产 `JubaozhaiWindow` 和网络代码与主仓一致；其他任务正在修改的 `QdaoUguiFactory`/`QdaoUguiTheme` 字体样式未同步，因此不宣称与当前主 Unity 逐像素同版。快照使用 Legacy 输入，夹具点击生产 HUD 按钮，走与 U 键相同的 `JubaozhaiUiRoot.Toggle`；**尚未人工按 U 或注入虚拟 U，实际快捷键仍需手动确认**。

证据根目录：`E:/work/xuanming-server-mmo/run/verify-trade-p1-20260917/client-live/`。总览为 `final-summary.json`，本地修复与 102 例结果在 `tcp-dispose/`，完整联网 XML、日志、阶段 JSON 与截图在 `live-04/`；`live-01` 至 `live-03` 失败证据原样保留。客户端仅修改 `GateTcpClient.cs`、新增 `Assets/Tests/EditMode/Net/` 回归及本验证段；未提交代码。

