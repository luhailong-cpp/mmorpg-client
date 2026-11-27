# Apple / Google 渠道登录官方品牌素材来源说明

> 对应《账号注销与 Apple / Google 登录交接单》（`xuanming-server-mmo-cpp/docs/handoff/account-deletion-handoff-20261120.md` §3 第 8 步）与 `AGENTS.md` 零拒审红线（App Review 4.8 / Apple HIG / Google Sign-In Branding Guidelines）。
>
> **替换说明（2026-11-26 核查）**：2026-11-20 提交的初版 4 张 PNG 为本地脚本自绘（96 DPI 同时间戳、双按钮像素分布逐项相等、Apple 标志轮廓偏扁），不符合官方原件要求，已于 2026-11-26 全部替换为以下官方来源素材；品牌标志不改色、不变形、不自绘，不交由 Codex 作图。

## 1. 官方原件来源与校验摘要（2026-11-26 获取）

| 平台 | 官方下载地址 / 入口 | 原始文件 SHA-256 |
|---|---|---|
| Google Sign-In 品牌资源包 | `https://developers.google.com/identity/images/signin-assets.zip`（官方规范页：`https://developers.google.com/identity/branding-guidelines`） | `ba884069e12093b06bcfd776915081254a7c95094b80d50be2c5dc6bac1c1da1` |
| Apple Sign in with Apple 官方 JS 矢量标志 | `https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/zh_CN/appleid.auth.js`（官方 HIG：`https://developer.apple.com/design/human-interface-guidelines/sign-in-with-apple`） | `e95cfbdf1680ef1612df06c9acbe17b8a8d1ef1b6bd2d9e307ecc2fd8a7b6272` |
| Apple Sign in with Apple 官方按钮 PNG 端点 | `https://appleid.cdn-apple.com/appleid/button?height=64&width=375&color=black&border=true&border_radius=10&type=sign-in&locale=zh_CN` | `d254258700ce6ee5cad8f5e0ade12337b1523a15497ce43a5ad50d2ec09cdb33` |

## 2. 本目录四张 PNG 与合规参数

| 文件 | 尺寸 | 处理方式 | SHA-256 |
|---|---|---|---|
| `google_logo.png` | 96×96 RGBA | 直接取自官方 `signin-assets.zip` 中 `Android + Web/PNG @4x/{Light,Dark}/Theme=*, Show text=No, Shape=Square, Platform=Android+Web@4x.png` 中心 `[32:128, 32:128]` 1:1 像素，按白/深两张官方原图解析透明通道（与官方 `Neutral@4x.png` 交叉验证平均误差 0.0384/255），零缩放、零改色、零变形 | `67787c5a90a347d321ce47ff197f34a82e0dba31ceb326202fc46de95ad1ad47` |
| `google_signin_button.png` | 600×90 RGBA | 直接取自上述官方 `Light@4x.png` 外框（`#FFFFFF` 底、`#747775` 描边、4dp/40dp 圆角比例），清空中心标志位后按 90px 高度等比缩放外框并延展水平中段（配合 `.meta` 九宫格 `spriteBorder: {24, 24, 24, 24}`） | `94ec37d7d79906869d5516c16853d001e758d625440fa29c797b5f6607c787e8` |
| `apple_logo.png` | 96×96 RGBA | 直接由官方 `appleid.auth.js` 内嵌未修改 Apple 标志 SVG `<path>` 渲染（`#FFFFFF` 透明底，严格保持原始 15:19 宽高比，标志高度 80px 对齐 `google_logo.png`） | `084a10cde12292ffd0d5512c7a9e99008d03f50ad1e0ee58b87f8752b49b2fc5` |
| `apple_signin_button.png` | 600×90 RGBA | 直接取自 Apple 官方 PNG 端点生成的黑色带边按钮外框（`#000000` 底），清空中心图文区后按 90px 高度等比缩放外框并延展水平中段（配合 `.meta` 九宫格 `spriteBorder: {24, 24, 24, 24}`） | `9c373d57c2a7d274a94e3e0f7590e91ce2a28ae5f8585ac6e5953e8761a3b438` |

## 3. 控件文案与尺寸合规核对

- **Apple 按钮**：文案「通过 Apple 登录」（Apple 官方简体中文标准文案）、纯黑底白标白字、按钮尺寸 `600×90`（2x 基准 45pt ≥ Apple HIG 最低高度 44pt）。
- **Google 按钮**：文案「使用 Google 账号登录」（Google Identity 官方简体中文标准文案）、白底 `#747775` 边 `#1F1F1F` 深灰字与官方彩色 G 标、按钮尺寸 `600×90`。
- **对等性（App Review 4.8）**：两按钮位于账号面板下方同一位置、同尺寸（`600×90`）、同字号（`34`）、同标志位（`44×44`）且同等醒目。
