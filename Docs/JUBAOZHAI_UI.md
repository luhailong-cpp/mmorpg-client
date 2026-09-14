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

正式模式没有聚宝斋交易协议，不执行下单、支付或卖家消息。收藏仅为当前界面会话状态。样例价格、编号与期限不代表线上商品。

## 数据接线
`JubaozhaiUiRoot.Window.State.SetListings(listings, serviceAvailable: true)` 是数据注入入口；模型是 `MmorpgClient.Game.Jubaozhai.JubaozhaiListing`。业务层在获得真实列表后注入，离开角色或断线会清理会话。后续交易接口需单独接入，不能将详情打开当作成交。

## 资源与验证
- 独立 PNG/Sprite：`Assets/Resources/UI/Ugui/JubaozhaiV1/`。
- 来源、裁切坐标和九宫格参数由构建脚本输出清单记录；源原图不改。
- 编辑器结果：`.codex-artifacts/jubaozhai-ui/`。
- 官方 MCP 执行记录：`E:/work/output/jubaozhai/qa/`。

2026-09-14 最终官方MCP验收通过：36张Sprite，6项模型断言全部通过，20张两种分辨率的原生UI截图与交互检查通过。编译280个运行时文件无错误。当前未进行线上交易或PlayMode全链路验证。当前未命名场景已保留于 Assets/Scenes/Recovery/JubaozhaiOriginalUntitled_20260914.unity。

