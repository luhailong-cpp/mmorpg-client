# 天墉城 6×6 场景图集

这套资源由 36 张无缝衔接的正方形场景图组成，用于构建国风道门回合制游戏的主城大地图。

## 规格

- 网格：6 列 × 6 行
- 单图：1024 × 1024，RGB PNG
- 拼接后：6144 × 6144
- 原点：左上角
- 顺序：从左到右、从上到下
- 文件名：`tianyong_r行号_c列号.png`
- 拼接：相邻图片直接贴边，不需要重叠、裁边或缩放

## 目录

- `Assets/Resources/World/Tianyong/SceneTiles6x6/Tiles/`：36 张正式图块
- `Previews/tianyong_city_master_6144.png`：完整无网格母图
- `Previews/tianyong_city_master_preview_2048.png`：便于快速查看的完整预览
- `Previews/tianyong_city_tiles_contact_sheet.png`：带行列标记的缩略预览
- `tile_manifest.json`：行列、像素坐标和相邻图块关系

## Unity 导入建议

- Texture Type：Sprite (2D and UI)，Single
- sRGB：开启
- Wrap Mode：Clamp
- Filter Mode：Bilinear
- Generate Mip Maps：关闭
- Max Size：2048 或更高
- Compression：None（优先画质）
- Pixels Per Unit：100；每张 1024 像素图块在世界空间中宽、高均为 10.24 单位，相邻图块中心应按 10.24 单位的整数倍放置
- 清单使用左上角原点且行号向下递增；映射到 Unity 常见的 Y 轴向上世界坐标时，行方向通常使用负 Y（例如第 `r` 行的 Y 偏移为 `-(r-1)×10.24`）
- 36 张无压缩图块同时驻留约占 108–144 MiB 纹理内存（取决于 RGB24/RGBA32）；运行时优先按可见图块流送或使用 Addressables，并按目标平台评估 BC7/ASTC 等压缩格式

## 美术约束

整套图使用固定 45° 正交俯视、统一建筑比例和明亮上午光照。青绿琉璃瓦、朱木、米白石材、翡翠水系与克制金色共同形成主色板；正式图中不含人物、文字、界面、标志或水印。

地图按多人主城玩法重新留白：南北、东西主轴均为超宽通路，中央广场和各功能区都有大面积连续铺装站立区，桥面、台阶和四门入口也支持多名角色并行。树木、摊位、雕塑等装饰集中在通行面边缘，不占用主要集会和移动空间。

每个逻辑图块生成时额外包含四周 256 像素邻区；36 个 1536×1536 精修块先沿最低误差路径拼成一张连续母图，再统一反切为正式图块。正式图可以按清单直接贴边拼接，不需要在运行时再次羽化。
