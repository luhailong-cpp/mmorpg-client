# 小游戏 WebSocket 可行性原型

2026-10-21 从 `feat/minigame-feasibility` 归档工作树中保全的 7 个未提交源码文件。该分支的已提交部分原本已在 `main`，这些文件此前不在 Git 中。原目录结构和文件字节保留，`source-sha256.json` 记录迁移时的 SHA-256。

这里提供浏览器 / 小游戏 WebSocket 通道、承载 muduo 帧的连接，以及只依赖 Python 标准库的本机 WebSocket↔TCP 联调桥。目录位于 Unity `Assets` 之外，不参与正式客户端构建。

当前 `GameClient` 的登录、重定向和连接生命周期仍依赖同步 TCP；这份原型没有接入正式登录流程，也没有完成微信 / 抖音真机验证。后续接入需要统一异步连接状态、连接工厂、握手超时和重定向处理，并补充 C# 通道的边界与资源上限测试。桥只供本机联调，默认监听和目标白名单均限定本机。

从客户端项目根目录运行桥接回归：

```powershell
python -I -m unittest discover -s tools/experimental/minigame/tools/ws-bridge -v
```

查看开发桥参数：

```powershell
python tools/experimental/minigame/tools/ws-bridge/ws_tcp_bridge.py --help
```

本次迁移的 24 个 Python 回归全部通过，覆盖帧分片、粘包、上下行限长、关闭、白名单、HTTP 代理、日志与静态文件路径；4 个 C# 源码分别以 Standalone 和 WebGL 条件引用当前主干程序集编译通过。该结果不代表小游戏已完成可发布接入。
