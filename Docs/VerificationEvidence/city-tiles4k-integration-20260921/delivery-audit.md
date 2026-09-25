# 最新主城交付只读核对（2026-09-21）

核对时间：2026-09-21T11:44:43.076827+00:00。美术最新总账更新时间：2026-09-21T07:41:53.917888+00:00。

**当前没有可发布的完整主城包：完整 0/7 套，正式验收 0/1792 块。** 当前只有 24 个独立坐标的局部候选；7 张1254²布局参考、预览图及历史重复版本均不算正式图块。未生成正式 manifest。

| 美术外观 | 客户端映射 | 已选局部候选坐标 | 候选数 | 尚无候选坐标 | 正式验收 |
| --- | --- | --- | ---: | ---: | ---: |
| tianyong_festival | tianyong/festival | r09_c07, r09_c08, r10_c07, r10_c08, r10_c09, r10_c10 | 6 | 250 | 0/256 |
| penglai_day | penglai/day | r09_c10, r09_c11, r09_c12 | 3 | 253 | 0/256 |
| penglai_mid_autumn | penglai/festival | r09_c10, r09_c11, r09_c12 | 3 | 253 | 0/256 |
| donghai_day | donghai/day | r08_c08, r08_c09, r08_c10 | 3 | 253 | 0/256 |
| donghai_lantern | donghai/festival | r08_c08, r08_c09, r08_c10 | 3 | 253 | 0/256 |
| lanxian_day | lanxian/day | r08_c06, r08_c07, r08_c08 | 3 | 253 | 0/256 |
| lanxian_spring | lanxian/festival | r08_c06, r08_c07, r08_c08 | 3 | 253 | 0/256 |

尚无候选坐标共 1768 个；这不是“尚未开工”数量，可能存在未入账的制作中原图。正式验收尚缺全部 1792 块。完整坐标列表与每块来源路径/SHA 见 [delivery-audit.json](delivery-audit.json)。

## 本次实际验证

- 对选定 24 张 PNG 逐文件核对 SHA-256、PNG 校验和与完整像素解码、4096×4096尺寸、16×16整图坐标及世界矩形；错误 0 项。
- 每个候选的 QA、assembly 原始字节 SHA 均与最新总账比较；所有7套 catalog 与 current-batch 逐条比较。
- 502 条保留原生来源（392基础+110返修）核对原图SHA、来源记录SHA与尺寸；源图只验证哈希/图像头，未将其称为本轮完整解码。24个最终候选执行了完整解码。
- 扫描制作目录，含严格 r01_c01…r16_c16 命名且齐256坐标的单目录为 0 个；文件名 manifest.json 仅见局部 QA 裁切清单。
- 读取过程中输入快照SHA稳定；每个输入的完整SHA和所有候选/原生来源SHA均已保存。

## 交付版本与限制

- 2026-09-19交接中的19块已被2026-09-21最新账本24块取代。天墉选 output_v5 共6块；蓬莱日景选 output_v2_20260918，中秋选 output_v2_20260920；东海日景选 output_v3，元宵选 output_v2；揽仙日景和春景各选 triple output_v2。
- 当前 QA 均限定局部连续性候选，正式/整城/运行时标志均未通过。东海日景旧QA中的元宵c10缺源图说法已被新元宵QA与当前账本取代。
- 每套仍缺完整重拼、全部相邻边与四块交点、外围邻块、同城外观几何、导航/出生点及独立前景验收。天墉旧前景的剪影、遮挡、脚点、光照和近景清晰度未齐备，不能声明 legacyForegroundCompatible=true。
- 实际游戏入口及设备内存/显存/帧时间不在本只读审计的验证范围；历史19块Editor记录不能替代这些验证。
- 未修改美术仓，保留来源记录内历史E:/work路径；本机解析使用批次根相对路径。

## 输入快照SHA-256

- status: `ed43932aa8cdab5f9c74bf1de7f5eaf6ce3aa4ca7986db80a38c2d6bdce539ce` — `D:\luyuan\wuxingqitan\image\qdao_city_tiles_4k_20260916\status.json`
- catalog: `ab81387e5874f98e8e48f1eeb2596fce5af0e8c5fdadad5c068ec4e06e8b6d3a` — `D:\luyuan\wuxingqitan\image\qdao_city_tiles_4k_20260916\production_catalog.json`
- batch: `4178e85b37d9c531e22cfbd26e7f9e90192089e13f9e5b99c1d1dae1dbfd3454` — `D:\luyuan\wuxingqitan\image\qdao_city_tiles_4k_20260916\builtin_q64_production\current-batch.json`
- readme: `660b52794108de776da3a361151e7eb1988e3a4be6cfa1f60c03342f19c39689` — `D:\luyuan\wuxingqitan\image\qdao_city_tiles_4k_20260916\README.md`
- artSpecification: `2e7fde56298729d012419d3f7815c4f469c5dbeee29f6785166670e35828c12a` — `D:\luyuan\wuxingqitan\image\主城地图切图规范.md`
- clientContract: `80cc98c7b133522af31cd59fe26127eb07d593c930f58450b63608cfbb9cc8f6` — `D:\luyuan\wuxingqitan\mmorpg-client\Docs\CityTiles4K.md`
