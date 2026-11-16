# 八景岛本体双节客户端接入

八景岛本体沿用客户端身份 `island`、场景 `2`，资源修订为 `bajingbody20261114`。Unity 菜单 `MMORPG > Maps > 八景岛双节本体预览` 打开离线走图场景。点 Play 后可用 WASD 或鼠标点击移动，用滚轮缩放，在场景面板选择春节或中秋。

双节各使用 88 张正式交付的 1024×1024 运行图块，格网为 11 列 × 8 行。总览底图 4096×2979，正式运行图块保留原始字节。导入逐块核对了坐标、实际 PNG 尺寸、字节数、SHA-256 及正式采用文件。来源是美术仓 `8eb27570b2c514c6a6f8abb46afbd62aa1222e3b` 的 `designs/bajing-body-final-20261115/PACKAGE_MANIFEST.json`。导入校验与来源见 [接入校验记录](VerificationEvidence/bajing-body-20261114/client-install-verification.json)。

两套外观共用交付的 587×427 导航掩码，单元为 1 世界单位，24,016 个可走格。出生点为 Unity `(205, 0, 70)`，有效世界范围为 420×420。注册保留 `clientOnly=true`。服务端场景 2 的联机进入仍由既有地图和导航处理。新导航发布前需完成服务端烘焙、坐标与双端验证。 [导航交接](VerificationEvidence/bajing-body-20261114/server-handoff/handoff.json)

Unity `6000.6.0f1` EditMode 的当前地图与传送测试通过 54 项。八景岛专项检查覆盖实际导图尺寸、双节共用导航、出生点到太极广场的寻路、海面阻挡以及旧场景 2 联机保留。当前发布客户端的整屏逐像素验收仍待完成。 [美术验收](VerificationEvidence/bajing-body-20261114/FINAL_ART_ACCEPTANCE.json)
