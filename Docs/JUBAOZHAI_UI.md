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

2026-09-14 最终官方MCP验收通过：36张Sprite，6项模型断言全部通过，20张两种分辨率的原生UI截图与交互检查通过。编译280个运行时文件无错误。当前未进行线上交易或PlayMode全链路验证。当前未命名场景已保留于 Assets/Scenes/Recovery/JubaozhaiOriginalUntitled_20260914.unity。

