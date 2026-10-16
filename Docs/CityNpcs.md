# 主城静态 NPC · 2026-10-16

23 位高清 NPC 已导入客户端；本次按用户选择提供独立展示场景，等待新的主城底图。已清理的旧主城底图没有恢复，当前不宣称主城内测试完成。

美术保持原有道家 Q 版、`image/designs` 已确认风格及春节／中秋喜庆服饰细节。使用 `image/designs/city-npcs-hd-20260924/native` 的最终透明 PNG，未重新生成、裁切或放大图片，本次接入未调用付费生图 API。

## 开始测试

在 Unity 打开 `Assets/Scenes/World/NpcArtShowroom.unity` 并进入 Play；独立 Windows 程序和操作说明见 [NPC 展示说明](NpcShowroom/README.md)。当前范围为静态图片、原图比例、透明边缘和细节查看，不包含对话、商店、任务、碰撞或动作动画。

## 素材和主城接入

- [placements.json](../Assets/Resources/World/Tianyong/Npcs/placements.json) 保存 23 位人物的姓名、历史世界坐标、显示高度、脚底锚点及原生尺寸。
- [逐图来源](CityNpcs.sources.json) 保存 SHA-256、来源记录、实际尺寸与模型证据。内置生图未披露数值模型版本／质量，不能把配置目标视为实际返回版本。
- 原生尺寸：杨镖头 941×1672，朱雀 1254×1254，其余 21 张 1122×1402。这些是原生文件，不是原生 4K。
- 导入器上限调整为 2048，使用 NPOT None、无压缩、无 mipmap，保留原始长宽。运行时对声明的原生尺寸做一致性检查，避免再次被 1024 上限缩小。
- `pivotX/pivotY` 是以图片左下角为原点的归一化脚底锚点；`visibleHeightFraction` 来自透明包围框。非方图按原始宽高比显示。
- [锚点记录](NpcShowroom/anchors.json) 的像素坐标以 PNG 左上角为原点。朱雀横向锚点经人工修订为 x=550，避免尾羽使中心偏移。
- 历史坐标基线和取回来源分别保存在 [placements-baseline.json](NpcShowroom/placements-baseline.json) 与 [baseline-provenance.json](NpcShowroom/baseline-provenance.json)。坐标没有按尚未交付的新地图重新验收。

`TianyongCityNpcs` 仍仅在绘制主城挂载成功后创建独立人物、姓名和阴影图层。新底图到位后需要复查道路位置、可达性、人物高度、脚底与前景遮挡；独立展示场景不验证这些事项。

## 验证结果

- [23 张源文件与客户端 SHA-256 检查](NpcShowroom/asset-sha256.json)：全部一致。
- Python 同步脚本 7 项集成测试通过，覆盖新旧清单、非方图、GUID 保留、锚点及无效输入不写入。
- 客户端 443 个运行时 C# 源文件离线编译，0 错误；不等同于执行生产主城 EditMode 测试。
- Unity 6000.6.0f1 Windows 独立播放器构建成功，0 错误。
- [真实播放器验证](NpcShowroom/verification.json)：30 项通过、0 运行时错误；包括首次 GUI 绘制、23 张纹理原生尺寸与等比绘制，以及浅底、深底、棋盘、高倍缩放四张实机截图。四张截图均已人工视觉检查。
- 尚未自动化鼠标／键盘点击，也未执行依赖新底图的主城实机测试。

## 后续同步

在客户端根目录，使用带 Pillow 的 Python：

```powershell
python tools/sync_city_npcs.py --source ../image/designs/city-npcs-hd-20260924 --project-root . --positions Docs/NpcShowroom/placements-baseline.json --anchors Docs/NpcShowroom/anchors.json --dry-run
python -m unittest discover -s tools -p test_sync_city_npcs.py -v
```

确认 dry-run 后移除该参数进行同步。脚本完整预检后才写入；不提供操作系统写入故障时的整批回滚。新地图完成坐标调整后应以实际 `placements.json` 作为 `--positions` 输入，避免覆盖新坐标。旧排布合成脚本已修正非方图比例，但仍需要主城底图，本次未运行。
