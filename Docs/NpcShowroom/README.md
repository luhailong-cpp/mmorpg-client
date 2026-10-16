# 独立 NPC 展示

当前为 23 位主城 NPC 的静态素材展示，等待新主城底图。可直接检查清晰度、整体比例和透明边缘。

## 本机打开

双击 `E:/work/image/support/npc-client-20261016/Start-NpcShowroom.cmd`。

程序位于同目录的 `player/NpcArtShowroom.exe`。复制给其他电脑时必须携带整个 `player` 文件夹，不能只复制 exe。

Unity 入口为 `Assets/Scenes/World/NpcArtShowroom.unity`，直接打开后进入 Play。场景不需要主城底图或服务端。

## 操作

- 点击左侧名录、上一位／下一位，或使用左右方向键切换人物。
- 使用滚轮或滑块缩放，鼠标拖动查看局部。
- “原图 1:1”按纹理原生像素查看；“重置视图”恢复适配窗口。
- 浅底、深底和棋盘用于检查透明边缘。窗口显示每张图片真实尺寸及当前显示比例。

## 构建和验证

构建入口为 `MmorpgClient.Editor.Tianyong.NpcArtShowroomBuild.Build`，参数 `-npcShowroomOut <输出目录>`。它创建独立场景并仅选择该场景构建，不修改工程的全局 Build Settings；Unity 仍会打包工程内 Resources 内容。

本机交付使用独立最小工程构建，只包含展示源码、NPC 素材、导入器和项目现有的 Noto 中文字体。对应复现文件放在 `E:/work/image/support/npc-client-20261016/`。

可见窗口验证命令：

```powershell
& 'E:/work/image/support/npc-client-20261016/player/NpcArtShowroom.exe' -screen-fullscreen 0 -screen-width 1440 -screen-height 900 -npcShowroomVerify 'E:/work/image/support/npc-client-20261016/verification/manual'
```

验证需要显示窗口；不要使用隐藏窗口、`-batchmode` 或 `-nographics`。本机隐藏窗口时不会产生 GUI 绘制，已用可见窗口复核。程序等待首次 GUI 绘制，再逐图检查，生成报告和四张截图后自动退出。验证期间输入被禁用，普通启动不加验证参数即可交互查看。

[本次验证](verification.json) 为 30/30 通过、0 运行时错误。这只证明独立静态展示的资源加载和绘制，不代表主城地图、遮挡、对话、动画或输入点击自动化通过。

实际播放器截图：[浅底](01-light.png)、[深底](14-dark.png)、[棋盘](23-checker.png)、[放大细节](23-high-zoom.png)。
