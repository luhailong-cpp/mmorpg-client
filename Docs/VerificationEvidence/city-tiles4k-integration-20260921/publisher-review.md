# 独立发布工具审阅与故障复验

审阅范围：生产导入验证器的通过语义、精确256路径/坐标合同、资源释放边界；发布工具的失败恢复、版本绑定及保留并行改动。只读审阅其他agent源码，没有修改美术、正式资源或其实现。

## 导入验证器

- 无生产manifest时，七套均为 `not_delivered`，发布套数为0，`allPublishedImportsPassed=false`；批处理返回2，不能误报通过。
- `gameplayAcceptancePassed` 固定为false；导入成功状态明确为 `imports_passed_gameplay_pending`。
- 正式校验限制七个city/variant、16×16、固定世界矩形、256个逐行路径；天墉要求前景兼容标志。
- 每张纹理逐项检查实际4096、RGB24、导入设置及平台覆盖，在finally卸载。批处理保护要求关闭Play模式/独立batch编辑器。没有观察到把全部256张作为常驻纹理加载的代码。
- 本节是源码边界审阅；引擎实测结果归主任务validation证据，不能用本节替代。

## 已复现并修复：manifest临时文件阻断恢复

初版 `write_json(destination / "manifest.json")` 会先在正式Resources目录写 `.writing` 文件。若最终替换异常，恢复inventory检查把该工具临时文件当成外部修改，拒绝恢复旧目录。

独立复现：使用系统临时目录，构造一张小字节fixture和旧目录，在正式manifest的 `os.replace` 注入OSError；PNG解码、release/pre-existing验证使用mock，仅保留真实目录复制、文件写入、rename、inventory和恢复代码。

修复前实际结果：

```json
{"pendingJournalStillExists":true,"oldRestoredInProduction":false,"productionFilesAfterFailure":["manifest.json.<uuid>.writing","r01_c01.png"],"previousSnapshotExists":true}
```

当前实现改为在事务目录完整生成 `manifest.ready.json`，最后一次 `os.replace` 才将其放入正式目录。同一故障fixture复验实际结果：

```json
{"pendingJournalStillExists":false,"oldRestoredInProduction":true,"productionFilesAfterFailure":["old-preserved.txt"],"oldBytesIdentical":true}
```

抛出的错误恢复为原始注入OSError，不再被恢复流程自身的inventory错误覆盖。发布测试源码另包含首次发布和已有版本的manifest替换失败回归；本独立审阅没有重复运行整套测试，完整测试结果归publisher测试日志。

## 已修复并独立验证：准备期间旧资源变化

原实现先采集旧目录inventory，再耗时复制/验证新图，切换前缺少对旧目录复比。若其他工具此时修改正式资源，旧快照与journal记录会不一致。

当前实现切换前重新比较当前目录inventory与初始快照。独立fixture在真实copytree返回后修改旧目录文件，随后执行原install_release，实际结果：

```json
{"pendingJournalExists":false,"concurrentActiveBytesPreserved":true,"newProductionTileAbsent":true,"oldDirectoryWasNotMoved":true}
```

抛出 `Production resources changed while preparing publication; nothing was replaced`。说明已在正式切换前拒绝操作，并行修改继续保留在当前目录。

## 来源与前景绑定

已审阅修复：验收JSON/截图、assembly、原生来源记录与源图在耗时校验结束后按首次记录SHA再次核对；stage复制证据时继续使用首次SHA。天墉前景验收绑定3个实现源码、36张旧PNG及其meta共75个客户端文件；发布前检查当前客户端仍与验收记录匹配。整体目录切换保留旧目录，manifest最后安装；没有发现新旧图块按文件交错替换的路径。

## 验证边界

以上两个独立故障fixture均在系统临时目录运行并清理，使用微小占位数据和PNG/release验证mock，仅证明事务分支、字节保留和恢复行为。**没有执行真实256张4K完整包发布，没有新增任何生产manifest，没有证明实际游戏入口、视觉、导航、前景美术或设备性能通过。** 当前真实美术仍为24个局部候选、0套完整正式包，详见delivery-audit.md/json。

记录时间：2026-09-21T11:56:02.230989+00:00

审阅时源码SHA-256（后续修改应另核）：

- `Assets/Editor/Tianyong/CityTileProductionVerification.cs`: `091ab6f22b321f3e6824ed5bad79532c921133449f4e999241376f0bb4854947`
- `tools/publish_city_tiles.py`: `67b13e083c38638d5bfae31ece085ba22fd4b2b1ab75d01408843f5ee3cd8953`
- `tools/tests/test_publish_city_tiles.py`: `e18ddbe8a63bde9f9643d3ab23e7af5d9307a639dfe06da9dc08552baa32764f`
