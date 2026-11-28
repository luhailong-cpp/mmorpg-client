# 客户端防盗号「保持登录」与「账号历史记录」设计及异机作图交接

## 1. 需求分析

- **核心诉求**：玩家完成官方通行证或账号登录后，下次打开游戏无需重复跳转浏览器或重输密码即可一键进入游戏；同时在切换账号弹窗中可查看最近登录过的账号历史、快速选择或单条删除。
- **核心边界**：
  1. 绝不落盘明文密码，绝不把可续期凭据写入明文配置；
  2. 同一设备最多只保持 1 个活跃账号的免登状态，提供显式「保持登录（公用电脑请勿勾选）」开关与「退出登录」入口；
  3. 账号历史仅记录脱敏展示名与登录方式，支持单条删除，不跨账号囤积免登凭据；
  4. 登录首页保持原图一致，不新增破坏首页视觉的控件；本次新增 UI 仅位于「切换账号」登录弹窗内/右侧与「游戏设置」弹窗内。

## 2. 设计分析

- **安全存储分层**：`ISecureStringStore` 抽象隔离平台差异——Windows 使用 Win32 DPAPI（`CurrentUser` + 按键熵值 + 原子写文件），iOS 使用 Keychain（`AfterFirstUnlockThisDeviceOnly` 不进 iCloud 备份），Android 使用 `AndroidKeyStore` AES-256-GCM + AAD；WebGL 小游戏与未适配平台一律 `fail-closed` 返回不可用，绝不回落明文。
- **纯数据模型分层**：`AccountHistoryStore` 与 `RememberedSessionStore` 脱离引擎层，支持全量纯 C# 单元测试；隐私同意（`PrivacyConsent.Accepted`）未完成前禁止落盘。
- **UI 视觉替换策略**：程序侧已完成功能占位运行期挂载；正式视觉切图交由异机 Codex 产出后，再将图仓 `designs/account-session-history-ui-v1/slices/` 成品导入客户端并替换占位图。

## 3. 思想分析

- 以「可吊销短期/轮换令牌 + 系统级硬件/用户加密」替代「永久明文密码」，把单点泄露半径收敛到单台设备、单次会话家族，并复用服务端既有的令牌轮换重用熔断与通行证改密秒踢能力。
- 历史记录与免登状态解耦：历史记录服务于「快速识别与发起对应登录方式」，免登状态仅绑定当前 1 个活跃账号，防止共用设备串号。

## 4. 漏洞分析

| 潜在漏洞 | 缓解措施 |
|---|---|
| 明文存密码导致通行证被改密/改绑或撞库 | 彻底禁止落盘密码；仅存 30 天可吊销 `RefreshToken`，敏感注销仍强制二次认证。 |
| 本机明文配置被读取或文件拷往他机复用 | 仅写 OS 加密凭据库（DPAPI `CurrentUser` / Keychain `ThisDeviceOnly` / `AndroidKeyStore`），跨机/跨用户/篡改均解密失败。 |
| 令牌轮换后本地仍留旧值触发家族重用误吊销 | 订阅 `GameClient.OnRefreshTokenUpdated`，所有入口（登录、排队续期、断线重连、切角、局内刷新）轮换后即刻覆写密文；写失败则清空。 |
| 改密/封禁/注销后本机死循环重试失效凭据 | HTTP 401 拒刷新及改密/注销踢线触发 `OnSessionInvalidated`，立刻清空本地密文并拉起登录窗；网络/500 故障不误清。 |
| 历史记录泄露邮箱/手机号或二次脱敏破坏去重 | 第三方账号只存脱敏展示名 + SHA-256 键；续期仅调 `TouchZone` 更新区服与时间。 |

## 5. 步骤分析

1. 客户端底层实现 `SecureStore`（Windows / iOS / Android / WebGL）与纯 C# 模型 `AccountHistoryStore`、`RememberedSessionStore`。
2. `GameClient` 接入 `EnterZoneWithRefreshToken`、`OnRefreshTokenUpdated` 与 `OnSessionInvalidated`。
3. 登录界面以运行期挂载方式先完成逻辑闭环与编译/单测验真。
4. 交付纯功能作图清单给异机 Codex 产出正式预览图与切图。
5. 正式切图验收后导入客户端替换占位资源并做实机视觉复核。

## 6. 执行步骤

- 步骤 1–3 已完成并通过编译及 17/17 单元测试（含真实 Windows DPAPI 往返、篡改与跨键互换验证）。
- 步骤 4：按下方独立正文交接给异机 Codex 出图。
- 步骤 5：收到图仓 `designs/account-session-history-ui-v1/` 成品后，按文末「本机研发导入备忘」完成换图与验收。

---

## 可直接复制给 Codex / ChatGPT 的纯功能作图交接正文

```text
请制作「保持登录与最近登录账号历史」这套登录弹窗增量 UI 的预览图与切图，严格遵守以下要求：

一、开工前必须先核实并逐张展示标准参照实图
开工前请先在图仓核实以下 7 张长期标准参照图与 1 张同功能已确认成图，并在回复中逐张展示实图、名称与准确相对路径（帮会界面为首要标准；若缺失请先如实报告，不要用文字或其他图片代替）：
1. 邮件界面：designs/mail-ui-v1/01-mail-event.png
2. 组队界面：designs/team-ui-v2/team-ui-v2.png
3. 帮会界面（首要标准）：designs/ranked-play-v2-ui-20261125/assets/approved-guild-reference.png
4. 天墉生死擂 · 下达战书：designs/ui-standard-references/jianghu-duel/01-challenge-approved.png
5. 江湖恩怨录：designs/ui-standard-references/jianghu-duel/02-grudges-approved.png
6. 帮派恩怨墙：designs/ui-standard-references/jianghu-duel/03-guild-wall-approved.png
7. 江湖战书 · 三种弹窗：designs/ui-standard-references/jianghu-duel/04-popups-approved.png
同功能已确认成图：
8. 账号登录弹窗（含官方通行证入口）：designs/official-passport-ui-v1/previews/01_account_login_with_passport.png

二、图仓输出目录
- 新建独立专题目录：D:\work\image\designs\account-session-history-ui-v1\
- 换电脑按图仓相对路径定位：designs/account-session-history-ui-v1/
- 目录内分两个子目录：
  - previews/（整屏界面布局预览图）
  - slices/（透明底正式切图）

三、功能、布局与状态说明
本次为既有「账号登录弹窗」与「游戏设置弹窗」的增量功能界面，不改动登录首页底图，不改动主登录弹窗的宽高（1180×650，位于 2560×1080 画布 x=690, y=235）及下方「官方账号登录 / 注册」按钮位置（x=980, y=900, 600×90）：
1. 账号登录弹窗内部增量控件：
   - 「保持登录（公用电脑请勿勾选）」复选框：位于密码输入框下方、「取消 / 登录」按钮上方（x=990, y=604，复选框图形 44×44，右侧为动态文字区域），包含未勾选、已勾选两种状态。
   - 「退出登录」次级按钮：位于登录弹窗标题右侧右上角（x=1625, y=294，200×56），仅在当前已保持登录时显示，包含常态、悬停/按下态。
2. 「最近登录账号」侧栏面板：
   - 紧贴主账号登录弹窗右侧独立展示（x=1895, y=235，尺寸 560×650，与主登录弹窗等高对齐）。
   - 顶部标题区域展示「最近登录账号」，中间纵向排列最多 5 条历史账号记录（每行高 80，行间距 16），底部留一行提示文案区域（「仅存脱敏展示记录，绝不保存密码」）。
   - 每条历史账号记录由左右两部分组成：
     - 左侧主选择条目（420×80）：显示登录方式标签 + 脱敏账号名（如「通行证 · lh***p」）；若该条正是当前本机已保持登录的账号，采用高亮激活态，并在条目右侧叠加「已保持登录」状态小徽标（120×34）；非当前保持登录条目为常态，鼠标移入/按下为悬停按下态。
     - 右侧单条删除按钮（70×80）：居中带清晰删除叉号图标，点击删除该条历史记录，包含常态与悬停/按下态。
3. 游戏设置弹窗增量入口：
   - 在现有「账号管理」一行中，「注销账号」按钮右侧（x=1375, y=650，尺寸 260×70）并排放置「退出登录」次级按钮（可复用上述退出登录按钮切图拉伸适配）。

四、交付图片清单（全部切图均为 PNG-32 透明底，严禁白底或残留杂边）
（A）整屏预览图（previews/，用于审核布局与视觉协调）：
1. previews/01_login_with_history_and_keep_signin.png（2560×1080）：账号登录弹窗完整态，同时展示「保持登录」勾选框、右上角「退出登录」按钮、下方「官方账号登录 / 注册」按钮，以及右侧「最近登录账号」侧栏（含 1 条已保持登录高亮项 + 2 条普通历史项与右侧删除按钮）。
2. previews/02_settings_with_signout.png（2560×1080）：游戏设置弹窗，展示「账号管理」行中「注销账号」与右侧「退出登录」并排布局。

（B）正式切图（slices/，全部 PNG-32 透明底）：
1. slices/account_history_panel_bg.png（560×650）：右侧「最近登录账号」侧栏面板底板；面板内全留空无任何文字；四边预留外框安全区，中间内容区留空，支持按外框安全区拉伸。
2. slices/account_history_row_normal.png（420×80）：历史账号条目常态底板；完全无文字留空；左右两端保留安全区，中间平直区域支持水平拉伸。
3. slices/account_history_row_active.png（420×80）：历史账号条目「当前已保持登录」高亮态底板；完全无文字留空；与常态有清晰主次区分；中间区域支持水平拉伸。
4. slices/account_history_row_hover.png（420×80）：历史账号条目悬停/按下态底板；完全无文字留空；中间区域支持水平拉伸。
5. slices/account_history_delete_normal.png（70×80）：历史账号单条删除按钮常态；居中含清晰删除叉号图形，无任何文字；固定比例不可拉伸。
6. slices/account_history_delete_hover.png（70×80）：历史账号单条删除按钮悬停/按下态；居中含清晰删除叉号图形，无任何文字；固定比例不可拉伸。
7. slices/keep_signin_checkbox_off.png（44×44）：保持登录复选框未勾选态；无文字；固定尺寸不可拉伸。
8. slices/keep_signin_checkbox_on.png（44×44）：保持登录复选框已勾选态；居中含清晰勾选标记，无文字；固定尺寸不可拉伸。
9. slices/signout_button_normal.png（200×56）：退出登录次级按钮常态；按钮内完全无文字留空（程序动态叠加「退出登录」）；中间区域支持水平拉伸（需兼容 200×56 与 260×70 两种显示尺寸）。
10. slices/signout_button_hover.png（200×56）：退出登录次级按钮悬停/按下态；完全无文字留空；中间区域支持水平拉伸。
11. slices/account_saved_badge.png（120×34）：历史条目右侧「已保持登录」状态小徽标底板；内部无文字留空（程序动态叠加短标签）；中间区域支持水平拉伸。

五、验收要求
- 所有 slices/ 切图必须为严格 PNG-32 透明底，边缘干净无锯齿无背景残留，且除删除叉号与勾选标记外不得烘焙任何文字或样例账号名。
- 完成后在专题目录下附一份 MANIFEST.md，列出每张图的相对路径、像素尺寸与状态说明。
```

---

## 本机研发导入备忘（严禁混入上方作图交接正文）

- 图仓成品就绪后，将 `D:\work\image\designs\account-session-history-ui-v1\slices\*.png` 导入至 `Assets/Resources/UI/Ugui/RefreshV8/`，统一设置 `textureType: 8`（`Sprite`）及对应 9-slice `spriteBorder`，并同步 `.meta`。
- 替换 `Assets/Scripts/UI/Ugui/QdaoServerSelectView.AccountSession.cs` 中当前临时复用的 `main_frame` / `clean_tab` / `check` 占位资源名为上述 11 张正式切图名，并补充高亮态 `account_history_row_active` 与徽标 `account_saved_badge` 的状态切换。
- 完成后执行 `pwsh -NoProfile -File tools/client_compile_check.ps1` 与实机 PlayMode 视觉复核。
