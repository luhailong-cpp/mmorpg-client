# 四图游历与六种景色验收 · 2026-09-13

本目录保存最终通过轮次run4的原始联机报告、日志与7张1920×1080截图。使用新测试角色、真实EnterScene传送、正式角色控制器和服务端导航。验证顺序为蓬莱岛→东海渔村→揽仙镇→天墉城，三地依次检查日景与节庆。

- [联机报告](festival-map-verification.json)：PASS，4次入场，7张截图；所有出生误差/导航差异/回拉为0。
- [原始玩家日志](player.log)、[运行参数记录（不含密码）](run-metadata.json)。普通AutoPilot登录PASS不参与本次结果判定。
- [客户端部署清单](client-deployment.json)：289文件SHA256，含实际游戏程序集和资源文件，原构建与日常启动目录逐文件匹配，记录旧客户端备份位置。
- [服务端部署清单](server-deployment.json)、[导航注册与四图入场原始行](server-navigation-registration.log)：配置1–4导航、配表、节点二进制与进程证据。
- [EditMode 61条](editmode-results.xml)、[PlayMode输入门禁1条](playmode-results.xml)、[gate路由26条](gate-routing-results.xml)均通过；[美术导航检查](navigation-review.json)31条完整路线/37个阻挡探针通过。

| 地图 | 日景 | 节庆 |
|---|---|---|
| 蓬莱岛 | [日景](scene-2-day.png) | [中秋月夜](scene-2-festival.png) |
| 东海渔村 | [日景](scene-3-day.png) | [元宵灯会](scene-3-festival.png) |
| 揽仙镇 | [日景](scene-4-day.png) | [春节迎新](scene-4-festival.png) |
| 天墉城返程 | [主城](scene-1-day.png) | — |

人工查看7张真实游戏截图：地图、角色、正常游戏HUD和节庆贴图均正常；图中的新三地以原生1254×1254贴图铺设，近景清晰度受原图限制。截图来自D3D12的可见实际游戏窗口；不使用离屏重画替代实机截图。

第一次联机暴露gate只处理首次登录的旧缺口，已修复并补7条路由回归。后两轮截图失败源于完全隐藏的游戏窗口没有最终帧，保留非空验收并改用Unity最终帧API及可见游戏预览后run4通过；失败日志仍保存在E:\work\tmp\festival_live_verify_20260913_run1/2/3，未用失败轮次截图充数。