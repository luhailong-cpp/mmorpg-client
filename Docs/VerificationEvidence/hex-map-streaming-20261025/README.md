# 六边形地图分片加载接入（2026-10-25）

当前地图图片加载使用服务器同源的六边形空间查询。原作者为 Amit Patel / Red Blob Games，采用其 [C# 实现](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.cs) 中的 Point、Hex、FractionalHex、Orientation、Layout，保留 CC0 许可及完整上游文件 SHA-256，详见 [NOTICE](../../../Assets/Scripts/World/CurrentMaps/RedBlobHexGrid.NOTICE.txt)。

服务器 `GridSystem` 使用平顶六边形、`size=(20,20)`、`origin=(0,0)`。客户端通过真实 `WorldCoordinateConverter` 转换坐标，服务器 X 对应 Unity Z，服务器 Y 对应 Unity X。半值舍入显式使用 `AwayFromZero`，匹配服务器 C++ `round`。

正式地图与沙盒均传入玩家脚下世界坐标。加载候选来自玩家当前格与六邻格，共七格，再合并相机视野覆盖的六边形。原有方形 PNG 不重切；每个分片矩形登记到所有相交六边形，避免分片跨格或宽视野导致遗漏。初始化只处理分片元数据，不请求高清 PNG。

空间候选与纹理预算独立：共享缓存最多六张，包含不可取消的在途请求，最多两个并发加载。可见分片优先；空余名额预取移动方向。无压力时离开两秒后释放，满额时已不需要的独占完成块立即为新可见块腾位。超出预算或缺片的位置保留同版布局底图。手机纹理格式、尺寸、地图下载方式与安装包配置未在本次更改。

## 验证

- 413 份实际运行时源码的 Editor／Release 编译均为零错误；两份相关测试源码编译通过。
- 引用实际生产 DLL 的独立数学程序实际通过 32,018 条断言，覆盖服务器坐标金标准、正负半值舍入、轴转换、六邻格顺序、大矩形跨格、相机与玩家中心分离、宽视野、14／16 分片网格、平移后索引及可见候选完整性。
- 以上源码哈希、编译和数学运行结果见 [hex-verification.json](hex-verification.json)。源码哈希按 CRLF 统一为 LF 后计算；上游原文件与 DLL 使用原始字节哈希。
- 隔离 Unity 测试入口为 `tools/run_city_tiles_tests.ps1`，复制真实运行时源码和依赖，不以地图生命周期替身替代真实 `CurrentMapBuilder`／`TianyongMapInstance`。`-PrepareOnly` 只准备工程，不启动 Unity；准备和编译证据见 [isolated-runner-verification.json](isolated-runner-verification.json)。
- 该入口的 `-PrepareOnly` 已成功执行；精确复制的 413 份运行时、3 份 Editor、5 份测试源码均编译通过，430 对源文件与副本哈希相符。隔离工程仅含一张现有布局图，不复制高清分片或分片清单；这些编译结果不代表 Unity 测试已执行。

本机 Unity 探测缺少 `com.unity.editor.headless` entitlement，真实引擎导入、Resources 异步生命周期、游戏画面及真机内存尚未验证。较早的独立 streaming 程序通过过 24 条断言，但添加压力释放断言后的程序被 Windows 应用控制 `0x800711C7` 拒绝加载；不能把后者列为执行通过。六边形数学验证不替代上述引擎测试。

本记录只确认六边形图片加载代码的接入与上述验证，不声明新增地图资源已经导入或上传。
