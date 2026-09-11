# 人物与宝宝属性 UI v10 接入验收

2026-09-11 通过官方 Unity relay MCP（Unity 6000.6.0f1）完成 31 张新版切图正式发布和客户端导入。资源路径仍为 `Assets/Resources/UI/Ugui/AttributesPaintedV2/`，原 GUID 保持不变，未改变运行时资源键。

暂存区、美术正式目录、客户端的 PNG 和 manifest 三方 SHA-256 一致；Sprite Single / Full Rect / Clamp / Bilinear / 无压缩 / 无 mipmap 及九宫格边距均经过引擎检查。

`CharacterAttributeVisuals.PointRow` 修正了滑杆手柄在纵向拉伸锚点下实际点击区域变为 120 高的问题，使手柄真实尺寸保持 60×60，扩展列表最后一行的控件完整位于滚动视口内。人物和宝宝标题的纵向位置也已适配新版绿底标题牌。

验收运行真实 `AttributePanel` 和 `PetPanel`，在临时附加场景中渲染；使用无网络连接的样例数据，不修改已有场景或服务器状态。两种分辨率（2560×1080、1920×1080）各覆盖人物常规、人物八行滚到底、宝宝常规、宝宝空列表、宝宝十项滚到底，共 10 张截图。资源加载、九宫格、移除相性页签、本地宝宝加点/撤销、末项可见范围均通过。相关 EditMode 测试 **26 通过、0 失败**。

完整报告和截图：`../.codex-artifacts/attribute-ui-v10-20260911/`。可在 Unity 菜单 `MMORPG/UI/Verify attribute and pet v10 screens` 重新生成截图，`MMORPG/UI/Test attribute and pet panels` 运行交互测试。

宝宝模型 1001、1004 使用已确认头像；1002 石灵、1003 金猊尚无明确对应头像资源，保留中性徽记。葫团团、符小虎的切片已入库，未将不同角色误绑定到现有服务器模型。

本次验收范围为资源、Unity 中的实际面板和离线交互；不包含 Windows 成品包重建或在线服务器发布。
