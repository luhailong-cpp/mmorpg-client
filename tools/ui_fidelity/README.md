# UI 原图尺寸验证

主验收分辨率采用每张批准效果图的原始尺寸。组队及两张邮件原图经解码核实均为 1931×814。任何一个截图状态尚未通过完整像素比较与业务验证，都不能标记为完成还原。

## 完整像素比较

```powershell
python verify_pixels.py reference.png current.png result.json --diff difference.png
python -m unittest discover -s . -p 'test_*.py' -v
```

需要 Pillow。解码后逐像素比较完整 RGBA，不调整尺寸、对齐、裁边、忽略动态区或设置误差阈值。退出码 0 表示像素相同，1 表示存在差异，2 表示输入或比较过程无效。PNG 编码不同但解码像素相同可通过。

像素相同只证明这两个输入文件相同，不能证明截图来自发布客户端，也不能证明功能可用。完整验收还必须记录代码与资源版本、截图来源、相同界面状态和数据，并实际验证交互。禁止用整张原图覆盖真实界面获得通过。

## 不影响当前登录的邮件捕获

```powershell
./capture_mail_isolated.ps1 -ProjectPath 'D:/luyuan/mmorpg-client' -EvidencePath 'D:/luyuan/mmorpg-client/.codex-artifacts/ui-fidelity-native/run-unique'
```

此脚本离线编译正式运行源码，复制邮件相关资源到独立 Unity 工程，调用正式 `MailWindow` 与明确的离线样例。不会打开主工程、启动游戏网络或操作当前登录。`EvidencePath` 必须为新路径，不会覆盖已有工程。脚本返回后台 Unity PID；应读取该次日志、截图和报告确认后续完成，启动成功本身不表示捕获成功。

增加 `-WaitForCompletion` 可等待该次进程退出，并核对报告与八个独立截图文件；结果写入 `completion.json`，只表示离线捕获完成，视觉验收仍为 `false`。

`-OverlayRoot` 可指定包含 `Assets/Scripts` 和可选 `Assets/Resources` 的候选目录，用于验证候选源码及素材。只有独立工程使用覆盖内容。

启动及完成记录包含 `sourceProfile`（`baseline` 或 `overlay`）与 `overlayRoot`。候选运行仅在隔离复制的捕获器中将 `geometryNote` 改为中性说明；主捕获器里的原有几何说明不能证明候选尺寸，候选几何仍须独立检查。

这些输出是隔离 PreviewScene 的诊断截图，保留样例数据和离线标识；不是当前账号截图，样例数据也并不完全等同原图。它们可定位字体、布局与资源问题，但不能替代发布客户端的整屏验收。截图报告须保持 `pixelParityVerified: false`，直到对相同状态与数据的正式截图完成单独验证。
