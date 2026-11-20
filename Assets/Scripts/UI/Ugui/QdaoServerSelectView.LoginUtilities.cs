using System;
using System.Text;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    public sealed partial class QdaoServerSelectView
    {
        private const string MasterVolumePreference = "Qdao.MasterVolume";
        private const string FullscreenPreference = "Qdao.Fullscreen";
        [SerializeField] private Button _landingAnnouncement, _landingSettings;
        [SerializeField] private RectTransform _announcementPanel, _settingsPanel;
        [SerializeField] private TMP_Text _announcementBody, _settingsVolumeValue;
        [SerializeField] private ScrollRect _announcementScroll;
        [SerializeField] private Button _announcementClose, _settingsClose;
        [SerializeField] private Slider _settingsVolumeSlider;
        [SerializeField] private Toggle _settingsFullscreen;
        // 账号注销(服务端 docs/design/account-deletion.md §4.2~§4.4):设置面板里的入口,与注销 / 冷静期 / 提示共用的一个账号弹窗。
        [SerializeField] private Button _settingsDeleteAccount;
        [SerializeField] private RectTransform _accountDialog;
        [SerializeField] private TMP_Text _accountDialogTitle, _accountDialogBody, _accountDialogPrimaryText, _accountDialogSecondaryText;
        [SerializeField] private Toggle _accountDialogAcknowledge;
        [SerializeField] private Button _accountDialogPrimary, _accountDialogSecondary;
        private Action _accountDialogPrimaryAction, _accountDialogSecondaryAction;
        // 账号面板正用作注销前的重新认证:提交与渠道按钮都改为申请注销,不进区。
        private bool _credentialForDeletion;
        // 进区管线停在「之前的账号已注销」提示上:_busy 仍为 true,只放开弹窗自己的按钮。
        private bool _entryNoticeOpen;

        // 冷静期按负责人定案的 7 天写(§9);服务端改 AccountDirectory.DeletionCoolingOff 时这里同步。完成时刻以应答为准。
        private const string AccountDeletionTerms =
            "注销后将删除：账号、名下全部角色及其存档、好友关系、帮会成员身份、邮件等游戏数据。\n" +
            "将保留：支付订单与防欺诈所需的交易记录（按法规与安全要求留存）；聊天消息在 30 天内随有效期自动清除。\n" +
            "提交后有 7 天冷静期，期满自动完成注销，完成后无法恢复；冷静期内重新登录即可撤销注销。";
        private const string AppleRevokeHint = "\n如需同时解除 Apple 授权，可在 设置 > Apple ID > 使用 Apple 登录 中移除本游戏。";

        private bool LoginUtilityOpen => (_announcementPanel != null && _announcementPanel.gameObject.activeSelf)
            || (_settingsPanel != null && _settingsPanel.gameObject.activeSelf) || AccountDialogOpen;

        private bool AccountDialogOpen => _accountDialog != null && _accountDialog.gameObject.activeSelf;

        private RectTransform UtilityPanel(string name, string title)
        {
            var root = QdaoUguiFactory.CreateRect(name, _contentRoot, 0, 0, 2560, 1080);
            var blocker = QdaoUguiFactory.CreateImage(name + "Blocker", root, 0, 0, 2560, 1080, null, true);
            blocker.color = new Color(.025f, .07f, .06f, .72f);
            var frame = QdaoCoreFidelityArt.Panel(name + "Frame", root, 690, 235, 1180, 650, "main_frame");
            frame.raycastTarget = true;
            Label(name + "Title", root, 820, 282, 920, 78, title, 48, false, TextAlignmentOptions.Center);
            return root;
        }

        private void BuildLoginUtilities()
        {
            _announcementPanel = UtilityPanel("AnnouncementPanel", "游戏公告");
            var viewport = QdaoUguiFactory.CreateRect("AnnouncementViewport", _announcementPanel, 825, 391, 910, 328);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            _announcementBody = Label("AnnouncementBody", viewport, 0, 0, 910, 328, "", 31);
            _announcementBody.alignment = TextAlignmentOptions.TopLeft;
            _announcementBody.textWrappingMode = TextWrappingModes.Normal;
            _announcementBody.overflowMode = TextOverflowModes.Overflow;
            _announcementScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _announcementScroll.viewport = viewport;
            _announcementScroll.content = _announcementBody.rectTransform;
            _announcementScroll.horizontal = false;
            _announcementScroll.vertical = true;
            _announcementScroll.movementType = ScrollRect.MovementType.Clamped;
            _announcementClose = ArtButton("AnnouncementClose", _announcementPanel, 1120, 767, 320, 80,
                "关闭", "clean_tab", out _);
            _announcementPanel.gameObject.SetActive(false);

            _settingsPanel = UtilityPanel("SettingsPanel", "游戏设置");
            Label("SettingsVolumeLabel", _settingsPanel, 845, 421, 180, 65, "声音音量", 34);
            var sliderRoot = QdaoUguiFactory.CreateRect("SettingsVolumeSlider", _settingsPanel, 1050, 430, 520, 50);
            var track = QdaoCoreFidelityArt.Panel("VolumeTrack", sliderRoot, 0, 0, 520, 50, "clean_search");
            track.raycastTarget = true;
            var fillArea = QdaoUguiFactory.CreateRect("VolumeFillArea", sliderRoot, 25, 17, 470, 16);
            var fill = QdaoUguiFactory.CreateImage("VolumeFill", fillArea, 0, 0, 470, 16, null);
            fill.color = QdaoUguiTheme.Html("#419B76");
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = QdaoUguiFactory.CreateRect("VolumeHandleArea", sliderRoot, 25, 4, 470, 42);
            var handle = QdaoUguiFactory.CreateImage("VolumeHandle", handleArea, 0, 4, 42, 42, _statusDotSprite, true);
            handle.color = QdaoUguiTheme.Html("#E7C96E");
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0, .5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            // Slider stretches the perpendicular anchors on every refresh.
            // The area's height supplies the handle height; keep its extra height zero.
            handle.rectTransform.sizeDelta = new Vector2(42, 0);
            _settingsVolumeSlider = sliderRoot.gameObject.AddComponent<Slider>();
            _settingsVolumeSlider.fillRect = fill.rectTransform;
            _settingsVolumeSlider.handleRect = handle.rectTransform;
            _settingsVolumeSlider.targetGraphic = handle;
            _settingsVolumeSlider.minValue = 0; _settingsVolumeSlider.maxValue = 1;
            _settingsVolumeValue = Label("SettingsVolumeValue", _settingsPanel, 1610, 420, 130, 65, "", 30);

            var toggleRoot = QdaoUguiFactory.CreateRect("SettingsFullscreen", _settingsPanel, 1050, 548, 480, 64);
            var box = QdaoCoreFidelityArt.Panel("FullscreenBox", toggleRoot, 0, 0, 64, 64, "clean_tab");
            box.raycastTarget = true;
            var check = QdaoUguiFactory.CreateImage("FullscreenCheck", toggleRoot, 18, 16, 30, 30,
                QdaoCoreFidelityArt.Load("check"));
            Label("FullscreenLabel", toggleRoot, 92, 0, 310, 64, "全屏显示", 34);
            _settingsFullscreen = toggleRoot.gameObject.AddComponent<Toggle>();
            _settingsFullscreen.targetGraphic = box; _settingsFullscreen.graphic = check;
            Label("SettingsAccountLabel", _settingsPanel, 845, 652, 180, 65, "账号管理", 34);
            _settingsDeleteAccount = ArtButton("SettingsDeleteAccount", _settingsPanel, 1050, 650, 300, 70,
                "注销账号", "clean_tab", out _);
            _settingsClose = ArtButton("SettingsClose", _settingsPanel, 1120, 767, 320, 80,
                "完成", "clean_tab", out _);
            _settingsPanel.gameObject.SetActive(false);

            // 标题、正文、按钮文字都在运行时按用途填(ShowAccountDialog)。
            _accountDialog = UtilityPanel("AccountDialog", "注销账号");
            _accountDialogTitle = _accountDialog.Find("AccountDialogTitle").GetComponent<TMP_Text>();
            _accountDialogBody = Label("AccountDialogBody", _accountDialog, 825, 380, 910, 290, "", 28);
            _accountDialogBody.alignment = TextAlignmentOptions.TopLeft;
            _accountDialogBody.textWrappingMode = TextWrappingModes.Normal;
            _accountDialogBody.enableAutoSizing = true; // 注销说明最长,字号在 20~28 之间自适应,不出框
            _accountDialogBody.fontSizeMin = 20;
            _accountDialogBody.fontSizeMax = 28;
            var ackRoot = QdaoUguiFactory.CreateRect("AccountDialogAcknowledge", _accountDialog, 845, 688, 900, 56);
            var ackBox = QdaoCoreFidelityArt.Panel("AccountDialogAcknowledgeBox", ackRoot, 0, 0, 56, 56, "clean_tab");
            ackBox.raycastTarget = true;
            var ackCheck = QdaoUguiFactory.CreateImage("AccountDialogAcknowledgeCheck", ackRoot, 14, 13, 28, 28,
                QdaoCoreFidelityArt.Load("check"));
            Label("AccountDialogAcknowledgeLabel", ackRoot, 76, 0, 800, 56, "我已阅读并了解以上说明", 28);
            _accountDialogAcknowledge = ackRoot.gameObject.AddComponent<Toggle>();
            _accountDialogAcknowledge.targetGraphic = ackBox; _accountDialogAcknowledge.graphic = ackCheck;
            _accountDialogAcknowledge.isOn = false;
            _accountDialogSecondary = ArtButton("AccountDialogSecondary", _accountDialog, 930, 767, 310, 80,
                "取消", "clean_tab", out _accountDialogSecondaryText);
            _accountDialogPrimary = ArtButton("AccountDialogPrimary", _accountDialog, 1320, 767, 310, 80,
                "确认注销", "clean_primary", out _accountDialogPrimaryText);
            _accountDialogPrimaryText.color = QdaoCoreFidelityArt.Ivory;
            _accountDialog.gameObject.SetActive(false);
        }

        private void BindLoginUtilityEvents()
        {
            _landingAnnouncement.onClick.AddListener(OnLandingAnnouncement);
            _landingSettings.onClick.AddListener(OnLandingSettings);
            _announcementClose.onClick.AddListener(CloseLoginUtilities);
            _settingsClose.onClick.AddListener(CloseLoginUtilities);
            _settingsVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            _settingsFullscreen.onValueChanged.AddListener(OnFullscreenChanged);
            _settingsDeleteAccount.onClick.AddListener(OnSettingsDeleteAccount);
            _accountDialogPrimary.onClick.AddListener(OnAccountDialogPrimary);
            _accountDialogSecondary.onClick.AddListener(OnAccountDialogSecondary);
            _accountDialogAcknowledge.onValueChanged.AddListener(OnAccountDialogAcknowledged);
            if (Application.isPlaying)
            {
                AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePreference, AudioListener.volume));
                if (PlayerPrefs.HasKey(FullscreenPreference))
                    Screen.fullScreen = PlayerPrefs.GetInt(FullscreenPreference) != 0;
            }
        }

        private void UnbindLoginUtilityEvents()
        {
            _landingAnnouncement.onClick.RemoveListener(OnLandingAnnouncement);
            _landingSettings.onClick.RemoveListener(OnLandingSettings);
            _announcementClose.onClick.RemoveListener(CloseLoginUtilities);
            _settingsClose.onClick.RemoveListener(CloseLoginUtilities);
            _settingsVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            _settingsFullscreen.onValueChanged.RemoveListener(OnFullscreenChanged);
            _settingsDeleteAccount.onClick.RemoveListener(OnSettingsDeleteAccount);
            _accountDialogPrimary.onClick.RemoveListener(OnAccountDialogPrimary);
            _accountDialogSecondary.onClick.RemoveListener(OnAccountDialogSecondary);
            _accountDialogAcknowledge.onValueChanged.RemoveListener(OnAccountDialogAcknowledged);
        }

        private void OnLandingAnnouncement()
        {
            if (_busy || LoginUtilityOpen || (_credentialPanel != null && _credentialPanel.gameObject.activeSelf)) return;
            var text = new StringBuilder();
            var announcements = _app?.Session?.Announcements;
            if (announcements != null)
                foreach (var item in announcements)
                {
                    if (item == null) continue;
                    if (text.Length > 0) text.Append("\n\n");
                    if (!string.IsNullOrWhiteSpace(item.title)) text.AppendLine(item.title);
                    text.Append(item.content ?? string.Empty);
                }
            _announcementBody.text = text.Length == 0 ? "暂无公告" : text.ToString();
            _announcementBody.richText = false;
            float height = _announcementBody.GetPreferredValues(_announcementBody.text, 910, Mathf.Infinity).y;
            _announcementBody.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(328, height + 8));
            _announcementBody.rectTransform.anchoredPosition = Vector2.zero;
            _announcementPanel.gameObject.SetActive(true);
            _announcementScroll.verticalNormalizedPosition = 1;
            RefreshVisualState(); FocusUtility(_announcementClose);
        }

        private void OnLandingSettings()
        {
            if (_busy || LoginUtilityOpen || (_credentialPanel != null && _credentialPanel.gameObject.activeSelf)) return;
            float volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePreference, AudioListener.volume));
            _settingsVolumeSlider.SetValueWithoutNotify(volume);
            _settingsVolumeValue.text = Mathf.RoundToInt(volume * 100) + "%";
            _settingsFullscreen.SetIsOnWithoutNotify(Application.isPlaying ? Screen.fullScreen
                : PlayerPrefs.GetInt(FullscreenPreference, Screen.fullScreen ? 1 : 0) != 0);
            _settingsPanel.gameObject.SetActive(true);
            RefreshVisualState(); FocusUtility(_settingsVolumeSlider);
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (_busy) return;
            value = Mathf.Clamp01(value);
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(MasterVolumePreference, value);
            _settingsVolumeValue.text = Mathf.RoundToInt(value * 100) + "%";
        }

        private void OnFullscreenChanged(bool value)
        {
            if (_busy) return;
            PlayerPrefs.SetInt(FullscreenPreference, value ? 1 : 0);
            if (Application.isPlaying) Screen.fullScreen = value;
        }

        private void CloseLoginUtilities()
        {
            if (_busy) return;
            // 账号弹窗按它的次要按钮收起(Esc 走这里):冷静期弹窗即「退出」,其余即取消 / 关闭。
            if (AccountDialogOpen) { OnAccountDialogSecondary(); return; }
            bool settings = _settingsPanel.gameObject.activeSelf;
            if (settings) PlayerPrefs.Save();
            _announcementPanel.gameObject.SetActive(false);
            _settingsPanel.gameObject.SetActive(false);
            RefreshVisualState(); FocusUtility(settings ? _landingSettings : _landingAnnouncement);
        }

        private static void FocusUtility(Selectable target)
        {
            if (EventSystem.current != null && target != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void RefreshLoginUtilityState()
        {
            bool blocked = LoginUtilityOpen || (_credentialPanel != null && _credentialPanel.gameObject.activeSelf);
            if (blocked)
            {
                _landingServers.interactable = false;
                _landingEnter.interactable = false;
                _landingAccount.interactable = false;
            }
            _landingAnnouncement.interactable = !_busy && !blocked;
            _landingSettings.interactable = !_busy && !blocked;
            _announcementClose.interactable = !_busy;
            _settingsClose.interactable = !_busy;
            _settingsVolumeSlider.interactable = !_busy;
            _settingsFullscreen.interactable = !_busy;
            _settingsDeleteAccount.interactable = !_busy;
            bool dialogLive = !_busy || _entryNoticeOpen;
            _accountDialogSecondary.interactable = dialogLive;
            _accountDialogAcknowledge.interactable = dialogLive;
            _accountDialogPrimary.interactable = dialogLive &&
                (!_accountDialogAcknowledge.gameObject.activeSelf || _accountDialogAcknowledge.isOn);
        }

        // ── 账号弹窗 ─────────────────────────────────────────────

        /// <summary>
        /// 通用账号弹窗:标题、正文、一或两个按钮。不传的按钮动作即关闭弹窗;不传 secondary 就只有居中的主按钮;
        /// acknowledge = 主按钮要先勾选「已阅读」才可点(注销说明,§4.2 第 1 步的勾选确认)。
        /// </summary>
        private void ShowAccountDialog(string title, string body, string primary, Action onPrimary,
            string secondary = null, Action onSecondary = null, bool acknowledge = false)
        {
            _accountDialogTitle.text = title;
            _accountDialogBody.text = body;
            _accountDialogPrimaryText.text = primary;
            _accountDialogPrimaryAction = onPrimary ?? CloseAccountDialog;
            _accountDialogSecondaryAction = onSecondary ?? CloseAccountDialog;
            bool twoButtons = secondary != null;
            _accountDialogSecondary.gameObject.SetActive(twoButtons);
            if (twoButtons) _accountDialogSecondaryText.text = secondary;
            // 两个按钮时与面板的「取消 / 登录」同排;一个时居中(框宽 1180 从 690 起)
            ((RectTransform)_accountDialogPrimary.transform).anchoredPosition = new Vector2(twoButtons ? 1320f : 1125f, -767f);
            _accountDialogAcknowledge.gameObject.SetActive(acknowledge);
            _accountDialogAcknowledge.SetIsOnWithoutNotify(false);
            _accountDialog.gameObject.SetActive(true);
            RefreshVisualState();
            FocusUtility(_accountDialogPrimary);
        }

        private void ShowAccountMessage(string title, string body) => ShowAccountDialog(title, body, "确定", null);

        private void CloseAccountDialog()
        {
            _accountDialog.gameObject.SetActive(false);
            RefreshVisualState();
        }

        private void OnAccountDialogPrimary()
        {
            if ((_busy && !_entryNoticeOpen) || !AccountDialogOpen) return;
            if (_accountDialogAcknowledge.gameObject.activeSelf && !_accountDialogAcknowledge.isOn) return;
            _accountDialogPrimaryAction?.Invoke();
        }

        private void OnAccountDialogSecondary()
        {
            if ((_busy && !_entryNoticeOpen) || !AccountDialogOpen) return;
            _accountDialogSecondaryAction?.Invoke();
        }

        private void OnAccountDialogAcknowledged(bool _) => RefreshLoginUtilityState();

        // ── 账号注销(服务端 docs/design/account-deletion.md §4.2) ─────────

        private void OnSettingsDeleteAccount()
        {
            if (_busy || !_settingsPanel.gameObject.activeSelf || _app?.GameClient == null) return;
            CloseLoginUtilities();
            bool apple = ClientSettings.LastLoginMethod == LoginAuthTypes.Apple;
            ShowAccountDialog("注销账号", apple ? AccountDeletionTerms + AppleRevokeHint : AccountDeletionTerms,
                "确认注销", BeginDeletionReauth, "取消", null, acknowledge: true);
        }

        /// <summary>
        /// 确认之后重新认证(重新认证就是二次确认):Apple / Google 重新拉起渠道,小游戏向宿主重新要一次登录 code,其余重输账号口令。
        /// 小游戏账号没有口令,落到口令面板就永远注销不了。
        /// </summary>
        private void BeginDeletionReauth()
        {
            CloseAccountDialog();
            string method = ClientSettings.LastLoginMethod;
            if (method == LoginAuthTypes.Apple || method == LoginAuthTypes.Google ||
                method == LoginAuthTypes.WeChatMiniGame || method == LoginAuthTypes.DouyinMiniGame)
            {
                RequestChannelDeletion(method);
                return;
            }
            OpenDeletionCredentialPanel("请重新输入账号和密码以确认注销");
        }

        // 账号面板切到注销用途:口令要重输,不用内存里上次登录的那份。面板上的渠道按钮也照样可用。
        private void OpenDeletionCredentialPanel(string status)
        {
            ShowCredentialPanel(true, true);
            SetCredentialForDeletion(true);
            _passwordInput.SetTextWithoutNotify(string.Empty);
            SetStatus(status);
        }

        private void SetCredentialForDeletion(bool deletion)
        {
            _credentialForDeletion = deletion;
            _credentialTitleText.text = deletion ? "验证账号以注销" : CredentialLoginTitle;
            _credentialSubmitText.text = deletion ? "确认注销" : CredentialLoginSubmit;
        }

        private void SubmitPasswordDeletion()
        {
            var account = (_accountInput.text ?? string.Empty).Trim();
            var password = _passwordInput.text ?? string.Empty;
            if (account.Length == 0 || password.Length == 0)
            {
                SetStatus(account.Length == 0 ? "请输入账号" : "请输入密码");
                return;
            }
            ShowCredentialPanel(false);
            SubmitDeletion(LoginCredential.ForPassword(account, password), null);
        }

        /// <summary>渠道账号重新拉起 SDK 取新凭据;Apple 连同这次授权的 authorization_code 一起送,login 换票后立刻撤销(§8,定案 B)。</summary>
        private void RequestChannelDeletion(string authType)
        {
            if (_busy || _app?.GameClient == null) return;
            ShowCredentialPanel(false);
            _busy = true;
            int runId = ++_enterRunId;
            RefreshVisualState();
            SetStatus("正在等待授权…");
            RequestChannel(authType, result =>
            {
                if (this == null || runId != _enterRunId) return;
                if (!result.Ok)
                {
                    _busy = false;
                    RefreshVisualState();
                    SetStatus(PlatformSignIn.DescribeFailure(result.Error));
                    return;
                }
                SubmitDeletion(LoginCredential.ForThirdParty(authType, result.IdentityToken),
                    authType == LoginAuthTypes.Apple ? result.AuthorizationCode : null);
            });
        }

        private void SubmitDeletion(LoginCredential credential, string authorizationCode)
        {
            _busy = true;
            int runId = ++_enterRunId;
            RefreshVisualState();
            SetStatus("正在提交注销申请…");
            _app.Run(_app.GameClient.RequestAccountDeletion(DeletionZoneId(), credential, authorizationCode,
                r =>
                {
                    if (this == null || runId != _enterRunId) return;
                    _busy = false;
                    SetStatus(string.Empty);
                    OnDeletionResult(credential.AuthType, r);
                },
                err =>
                {
                    if (this == null || runId != _enterRunId) return;
                    _busy = false;
                    SetStatus(string.Empty);
                    Debug.LogWarning($"[QdaoUgui] account deletion request: {err}");
                    ShowAccountMessage("注销账号", "服务暂时不可用，请稍后重试。");
                }));
        }

        private void OnDeletionResult(string authType, GatewayAccountDeletionResult r)
        {
            var step = GameClient.ClassifyAccountDeletion(r);
            if (step == GameClient.AccountDeletionStep.Scheduled)
            {
                ClearLocalAccountState(authType);
                ShowLanding(true);
                ShowAccountMessage("注销申请已提交",
                    $"已申请注销，将于{GameClient.FormatAccountDeletionTime(r.execute_after)}完成。冷静期内重新登录可以撤销。");
                return;
            }
            if (step == GameClient.AccountDeletionStep.Blocked)
            {
                ShowAccountMessage("暂时无法注销", "请先处理以下事项再申请：\n" + GameClient.DescribeAccountDeletionBlockers(r.blockers));
                return;
            }
            if (step == GameClient.AccountDeletionStep.Reauthorize || step == GameClient.AccountDeletionStep.CredentialRejected)
            {
                if (authType != LoginAuthTypes.Apple && authType != LoginAuthTypes.Google)
                {
                    OpenDeletionCredentialPanel("账号或密码错误");
                    return;
                }
                // 渠道令牌一次性:重新拉起 SDK 取新的,不重发用过的(§3.4)。
                ShowAccountDialog("注销账号",
                    step == GameClient.AccountDeletionStep.Reauthorize ? "授权已失效，请重新授权。" : "登录凭证已失效，请重新验证。",
                    "重新授权", () => { CloseAccountDialog(); RequestChannelDeletion(authType); }, "取消");
                return;
            }
            if (step == GameClient.AccountDeletionStep.AlreadyExecuting) ClearLocalAccountState(authType);
            ShowAccountMessage("注销账号", step switch
            {
                GameClient.AccountDeletionStep.AlreadyExecuting => "账号注销已在执行中。",
                GameClient.AccountDeletionStep.RateLimited => "操作过于频繁，请稍后重试。",
                _ => "服务暂时不可用，请稍后重试。",
            });
        }

        /// <summary>注销已受理:清掉本地令牌与内存里的口令,Google 再清凭据状态;冷静期内重新登录走完整流程,可以撤销。</summary>
        private void ClearLocalAccountState(string authType)
        {
            var session = _app.Session;
            session.Password = string.Empty;
            session.AccessToken = null;
            session.RefreshToken = null;
            _passwordInput.SetTextWithoutNotify(string.Empty);
            _app.GameClient.SetTokens(null, null, 0);
            ClientSettings.LastLoginMethod = string.Empty;
            if (authType == LoginAuthTypes.Google) PlatformSignIn.ClearGoogleCredentialState();
        }

        // zone_id 只用来选路(各区 login 共用同一地域的目录,§5.2):当前选中的区,没有就用最近进过的区。
        private uint DeletionZoneId() => _selectedZoneId != 0 ? _selectedZoneId : ClientSettings.ZoneId;
    }
}
