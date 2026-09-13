# Q 版八位角色素材接入

已验收图片来源为 `../image/qdao_chibi_roster_v11` 的根 `manifest.json` 与 `validation.json`。立绘与方向帧条逐字节复制；独立动作图按权威帧条重新分切并验证 RGBA 像素完全一致。导入目录为 `Assets/Resources/World/Characters/QdaoRosterV11/<slug>/`。

| Slug | 角色 |
| --- | --- |
| `23_lantern_courier` | 灯穗小使 |
| `24_lu_dongbin` | 吕洞宾 |
| `25_lion_drum_guard` | 狮鼓护卫 |
| `26_osmanthus_healer` | 桂香药婆 |
| `27_ink_kite_ranger` | 墨鸢游侠 |
| `28_moon_rabbit_artificer` | 月兔机关师 |
| `29_he_xiangu` | 何仙姑 |
| `30_han_xiangzi` | 韩湘子 |

每个角色目录有 `portrait.png`（1024×1024 RGBA），以及 `walk/<DIR>/01.png`、`02.png`、`03.png`、`04.png` 四张独立动作图（每张 512×512 RGBA）。`<DIR>` 为 `N、NE、E、SE、S、SW、W、NW`，每位角色共 32 张独立移动图，全包共 256 张。世界人物动画直接按四张图片循环加载，不把四张动作再当成八格切分。

重新分切采用对应权威 `strip.png` 的横向固定边界 `x=0、512、1024、1536`，保留完整 512×512 画布、透明边和脚底位置。每张输出都与已验收独立源帧比较完整 RGBA 像素；每个方向都必须有四个不同的像素 SHA256。原 `walk_N.png` 等 64 张 2048×512 帧条继续保留供素材核对与旧工具使用；新人物的世界移动和战斗入口均读取独立动作图。导入总量为 8 张立绘 + 256 张独立动作图 + 64 张兼容帧条 = 328 张 PNG。源动图参考速度为每帧 120ms；移动动画跟随实际位移。

脚底锚点使用图像左上角坐标 `(256, 471)`。新包未制作独立待机图，静止时使用对应方向步行动作的落脚帧。不得把四帧重复成八帧、误用旧人物的待机图片，或把立绘当成步行动画。被用户否决的 `24_crane_hermit` 不在导入白名单中。

`QdaoCharacterSpriteImporter` 已覆盖该目录：Texture2D、保留 alpha、无 mipmap、无压缩、Bilinear、Clamp、最大尺寸 4096；世界和战斗动画将每张独立图片完整创建为 Sprite，核对帧条不参与新人物的运行时切片。首次导入的 `.meta` 使用路径生成稳定 GUID 并沿用现有角色导入设置；已有 `.meta` 保持不变。

同步及独立复查（在项目目录执行）：

```powershell
python -X utf8 -B tools/sync_qdao_roster_v11.py
python -X utf8 -B tools/sync_qdao_roster_v11.py --verify-only
python -X utf8 -B tools/sync_qdao_roster_v11.py --review-dir E:/work/tmp/qdao-four-frame-review
```

脚本需要 Pillow，可用 `--source` 指定其他素材包位置。同步前先检查八人白名单、根清单、验收 SHA256、RGBA/尺寸/透明度；同步后核对 72 张立绘/帧条与来源逐字节一致、256 张重新分切独立帧与源帧逐像素一致、全部 328 张输出 SHA256，以及 409 个资源及文件夹 GUID 和导入设置。`--review-dir` 生成八张技术核对图，每张四列八行，明确标注方向与动作序号，不改动游戏图片。同步结果记录在 `Docs/ArtEvidence/qdao-roster-v11-import.json`。该记录证明素材复制与导入约定，不代替 Unity 编译或游戏运行验证。

战斗场景中，已知账号角色通过真实玩家 `ActorId` 复用同一外观和立绘头像；`OwnerPlayerId` 仅表示宠物所属，不用于给宠物套玩家形象。新角色战斗待机使用对应 E/W 方向的第一张落脚帧，不镜像已绘制方向。攻击、施法、受击、死亡和胜利继续使用现有的程序动画及特效；本次资源没有独立绘制这些战斗动作。未知远程角色保留既有外观回退。
