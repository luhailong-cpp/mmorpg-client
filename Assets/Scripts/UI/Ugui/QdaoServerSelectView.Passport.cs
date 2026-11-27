using MmorpgClient.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>
    /// 官方通行证(Casdoor OIDC)道家 Q 版界面扩展:
    ///   1. 在 _credentialPanel 账号弹窗下方挂接「官方账号登录 / 注册」玉绿金边主按钮与副文案(素材来自 RefreshV8/passport_*);
    ///   2. 浏览器授权期间显示「通行证授权中」等待窗(旋转道纹罗盘 + 重新打开页面 + 取消授权);
    ///   3. 取消 / 超时 / 校验失败 / 端口占用给出明确恢复提示。
    ///
    /// 采用 Initialize 运行期挂载(非 SerializeField,也不在 PrepareForPreview 中改层级),
    /// 以保持现有烘焙 QdaoServerSelect.prefab 与 Prefab 契约测试零破坏。
    /// </summary>
    public sealed partial class QdaoServerSelectView
    {
        private const string PassportButtonNormalAsset = "passport_button_normal";
        private const string PassportButtonHoverAsset = "passport_button_hover";
        private const string PassportButtonDisabledAsset = "passport_button_disabled";
        private const string PassportSealIconAsset = "passport_jade_seal";
        private const string PassportWaitingBadgeAsset = "passport_waiting_badge";
        private const float PassportBadgeRotateDegPerSec = 45f;

        private Button _passportSignInButton;
        private TMP_Text _passportHintText;
        private RectTransform _passportWaitingDialog;
        private RectTransform _passportWaitingBadge;
        private Button _passportWaitingReopenButton;
        private Button _passportWaitingCancelButton;
        private bool _passportWaitingOpen;
        private bool _passportEventsBound;

        private void EnsurePassportUi()
        {
            if (_passportSignInButton == null && _credentialPanel != null)
            {
                _passportSignInButton = BuildPassportSignInButton(_credentialPanel);
                _passportHintText = Label("PassportSignInHint", _credentialPanel,
                    980f, 996f, 600f, 36f, "支持邮箱注册、登录与找回密码", 24f, true, TextAlignmentOptions.Center);
                _passportSignInButton.gameObject.SetActive(false);
                _passportHintText.gameObject.SetActive(false);
            }

            if (_passportWaitingDialog == null && _contentRoot != null)
            {
                BuildPassportWaitingDialog();
            }
        }

        private Button BuildPassportSignInButton(UnityEngine.Transform parent)
        {
            var button = QdaoCoreFidelityArt.Button("PassportSignInButton", parent,
                980f, 900f, 600f, 90f, PassportButtonNormalAsset, out _);
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = QdaoCoreFidelityArt.Load(PassportButtonHoverAsset),
                selectedSprite = QdaoCoreFidelityArt.Load(PassportButtonHoverAsset),
                pressedSprite = QdaoCoreFidelityArt.Load(PassportButtonHoverAsset),
                disabledSprite = QdaoCoreFidelityArt.Load(PassportButtonDisabledAsset),
            };

            var icon = QdaoUguiFactory.CreateImage("PassportSignInButtonIcon", button.transform,
                84f, 23f, 44f, 44f, QdaoCoreFidelityArt.Load(PassportSealIconAsset));
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var text = Label("PassportSignInButtonText", button.transform,
                140f, 0f, 384f, 90f, "官方账号登录 / 注册", 34f, true, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            text.fontWeight = FontWeight.Bold;
            return button;
        }

        private void BuildPassportWaitingDialog()
        {
            _passportWaitingDialog = UtilityPanel("PassportWaitingDialog", "通行证授权中");
            var badgeImage = QdaoUguiFactory.CreateImage("PassportWaitingBadge", _passportWaitingDialog,
                1200f, 392f, 160f, 160f, QdaoCoreFidelityArt.Load(PassportWaitingBadgeAsset));
            badgeImage.preserveAspect = true;
            badgeImage.raycastTarget = false;
            _passportWaitingBadge = badgeImage.rectTransform;
            // 将枢轴置中以围绕画布中心 (80, 80) 平滑旋转,同时保持屏幕坐标不变。
            _passportWaitingBadge.pivot = new Vector2(0.5f, 0.5f);
            _passportWaitingBadge.anchoredPosition = new Vector2(1280f, -472f);

            var body = Label("PassportWaitingBody", _passportWaitingDialog,
                805f, 588f, 950f, 120f,
                "已在浏览器打开官方通行证，请在浏览器完成注册或登录后返回游戏。",
                30f, false, TextAlignmentOptions.Center);
            body.textWrappingMode = TextWrappingModes.Normal;

            _passportWaitingReopenButton = ArtButton("PassportWaitingReopenButton", _passportWaitingDialog,
                930f, 767f, 310f, 80f, "重新打开页面", "clean_tab", out _);
            _passportWaitingCancelButton = ArtButton("PassportWaitingCancelButton", _passportWaitingDialog,
                1320f, 767f, 310f, 80f, "取消授权", "clean_tab", out _);
            _passportWaitingDialog.gameObject.SetActive(false);
        }

        private void BindPassportEvents()
        {
            if (_passportEventsBound) UnbindPassportEvents();
            if (_passportSignInButton != null)
                _passportSignInButton.onClick.AddListener(OnPassportSignInClicked);
            if (_passportWaitingReopenButton != null)
                _passportWaitingReopenButton.onClick.AddListener(OnPassportWaitingReopenClicked);
            if (_passportWaitingCancelButton != null)
                _passportWaitingCancelButton.onClick.AddListener(OnPassportWaitingCancelClicked);
            _passportEventsBound = true;
        }

        private void UnbindPassportEvents()
        {
            if (_passportSignInButton != null)
                _passportSignInButton.onClick.RemoveListener(OnPassportSignInClicked);
            if (_passportWaitingReopenButton != null)
                _passportWaitingReopenButton.onClick.RemoveListener(OnPassportWaitingReopenClicked);
            if (_passportWaitingCancelButton != null)
                _passportWaitingCancelButton.onClick.RemoveListener(OnPassportWaitingCancelClicked);
            _passportEventsBound = false;
        }

        private void OnPassportSignInClicked() => BeginChannelSignIn(LoginAuthTypes.Passport);

        private void OnPassportWaitingReopenClicked()
        {
            if (!_passportWaitingOpen) return;
            PassportSignIn.ReopenBrowser();
        }

        private void OnPassportWaitingCancelClicked()
        {
            if (!_passportWaitingOpen) return;
            PassportSignIn.Cancel();
        }

        private void ShowPassportWaitingDialog(bool open)
        {
            if (open) EnsurePassportUi();
            _passportWaitingOpen = open;
            if (_passportWaitingDialog != null)
            {
                if (open && _passportWaitingBadge != null)
                    _passportWaitingBadge.localRotation = Quaternion.identity;
                _passportWaitingDialog.gameObject.SetActive(open);
            }
            RefreshVisualState();
            if (open && _passportWaitingCancelButton != null)
                FocusUtility(_passportWaitingCancelButton);
        }

        private void TickPassportWaiting(float deltaTime)
        {
            if (!_passportWaitingOpen) return;
            if (_passportWaitingBadge != null)
                _passportWaitingBadge.Rotate(0f, 0f, -PassportBadgeRotateDegPerSec * deltaTime);
            if (Input.GetKeyDown(KeyCode.Escape))
                OnPassportWaitingCancelClicked();
        }

        private void RefreshPassportUiState(bool canInteract)
        {
            bool showPassport = _passportSignInButton != null &&
                                PassportSignIn.Available &&
                                MiniGameLogin.AuthType == null &&
                                !_credentialForRealName;
            bool hasPlatformButton = (PlatformSignIn.AppleAvailable || PlatformSignIn.GoogleAvailable) &&
                                     !_credentialForRealName;

            if (_passportSignInButton != null)
            {
                _passportSignInButton.gameObject.SetActive(showPassport);
                _passportSignInButton.interactable = canInteract && showPassport;
            }
            if (_passportHintText != null)
                _passportHintText.gameObject.SetActive(showPassport);

            // 保持 90px 基准高度不纵向拉伸云饰:单按钮居中(600x90),与 Apple/Google 共存时左右并排(各 580x90)。
            if (showPassport && hasPlatformButton)
            {
                SetChannelRect(_passportSignInButton, 680f, 900f, 580f, 90f);
                SetRect(_passportHintText.rectTransform, 680f, 996f, 580f, 36f);
                SetChannelRect(_appleSignInButton, 1300f, 900f, 580f, 90f);
                SetChannelRect(_googleSignInButton, 1300f, 900f, 580f, 90f);
            }
            else
            {
                if (_passportSignInButton != null)
                {
                    SetChannelRect(_passportSignInButton, 980f, 900f, 600f, 90f);
                    SetRect(_passportHintText.rectTransform, 980f, 996f, 600f, 36f);
                }
                SetChannelRect(_appleSignInButton, 980f, 900f, 600f, 90f);
                SetChannelRect(_googleSignInButton, 980f, 900f, 600f, 90f);
            }

            if (_passportWaitingReopenButton != null)
                _passportWaitingReopenButton.interactable = _passportWaitingOpen && PassportSignIn.IsWaiting;
            if (_passportWaitingCancelButton != null)
                _passportWaitingCancelButton.interactable = _passportWaitingOpen;
        }

        private void HandlePassportSignInFailure(string error, bool forDeletion)
        {
            string message = PassportSignIn.DescribeFailure(error);
            SetStatus(message);
            if (error == PassportSignInErrors.Timeout ||
                error == PassportSignInErrors.StateMismatch ||
                error == PassportSignInErrors.PortInUse)
            {
                ShowAccountDialog("授权未完成", message,
                    "重试", () =>
                    {
                        CloseAccountDialog();
                        if (forDeletion) RequestChannelDeletion(LoginAuthTypes.Passport);
                        else BeginChannelSignIn(LoginAuthTypes.Passport);
                    },
                    "退出");
            }
        }

        private void CleanupPassportOnDestroy()
        {
            if (_passportWaitingOpen)
            {
                // 先作废 runId 并关标志:Cancel 会同步回调,销毁中的界面不能再走刷新与失败弹窗。
                _passportWaitingOpen = false;
                _enterRunId++;
                PassportSignIn.Cancel();
            }
        }

        private static void SetChannelRect(Button button, float x, float y, float w, float h)
        {
            if (button == null) return;
            SetRect((RectTransform)button.transform, x, y, w, h);
        }

        private static void SetRect(RectTransform rect, float x, float y, float w, float h)
        {
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
