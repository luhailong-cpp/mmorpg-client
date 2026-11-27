using MmorpgClient.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>
    /// 首次启动的《用户协议》《隐私政策》弹窗(服务端 docs/design/official-passport-registration.md §3.9)。
    ///
    /// 没同意当前版本(<see cref="PrivacyConsent"/>)就盖在登录界面最上层:选服、登录、注册都点不了,Esc 也关不掉。
    /// 「阅读」按钮用系统浏览器打开构建时写进包里的协议地址(<see cref="ClientEndpoints"/>);「不同意」第一次只说明后果,
    /// 再点才退出游戏,不反复弹窗。与通行证等待窗相同,运行期挂载、复用面板与按钮的现有美术,不改烘焙的 Prefab。
    /// </summary>
    public sealed partial class QdaoServerSelectView
    {
        private const string PrivacyConsentBody =
            "欢迎来到本游戏！我们依据《用户协议》和《隐私政策》为你提供服务，并说明我们如何收集、使用、存储和保护你的个人信息，" +
            "以及你享有的查阅、更正、删除个人信息和注销账号等权利。请先阅读完整内容。\n" +
            "点击「同意并继续」即表示你已阅读并同意上述协议；不同意将无法使用游戏服务。";
        private const string PrivacyDeclineBody =
            "需要同意《用户协议》和《隐私政策》才能注册、登录和进行游戏。你可以先阅读完整内容再决定；仍不同意请点击「退出游戏」。";

        private RectTransform _privacyDialog;
        private TMP_Text _privacyBody, _privacyDeclineText;
        private Button _privacyTermsButton, _privacyPolicyButton, _privacyDeclineButton, _privacyAcceptButton;
        private bool _privacyDeclinedOnce;
        private bool _privacyEventsBound;

        private bool PrivacyConsentOpen => _privacyDialog != null && _privacyDialog.gameObject.activeSelf;

        private void EnsurePrivacyConsentUi()
        {
            if (_privacyDialog != null || _contentRoot == null) return;
            _privacyDialog = UtilityPanel("PrivacyConsentDialog", "用户协议与隐私政策");
            _privacyBody = Label("PrivacyConsentBody", _privacyDialog, 825, 370, 910, 250, PrivacyConsentBody, 28);
            _privacyBody.alignment = TextAlignmentOptions.TopLeft;
            _privacyBody.textWrappingMode = TextWrappingModes.Normal;
            _privacyBody.enableAutoSizing = true;
            _privacyBody.fontSizeMin = 20;
            _privacyBody.fontSizeMax = 28;
            _privacyTermsButton = ArtButton("PrivacyConsentTerms", _privacyDialog, 930, 640, 310, 80,
                "阅读《用户协议》", "clean_tab", out _);
            _privacyPolicyButton = ArtButton("PrivacyConsentPolicy", _privacyDialog, 1320, 640, 310, 80,
                "阅读《隐私政策》", "clean_tab", out _);
            _privacyDeclineButton = ArtButton("PrivacyConsentDecline", _privacyDialog, 930, 767, 310, 80,
                "不同意", "clean_tab", out _privacyDeclineText);
            _privacyAcceptButton = ArtButton("PrivacyConsentAccept", _privacyDialog, 1320, 767, 310, 80,
                "同意并继续", "clean_primary", out var acceptText);
            acceptText.color = QdaoCoreFidelityArt.Ivory;
            _privacyDialog.gameObject.SetActive(false);
        }

        /// <summary>没同意当前版本就弹出;已同意的什么都不做。</summary>
        private void ShowPrivacyConsentIfNeeded()
        {
            if (PrivacyConsent.Accepted) return;
            EnsurePrivacyConsentUi();
            if (_privacyDialog == null) return;
            _privacyDeclinedOnce = false;
            _privacyBody.text = PrivacyConsentBody;
            _privacyDeclineText.text = "不同意";
            _privacyDialog.SetAsLastSibling();
            _privacyDialog.gameObject.SetActive(true);
            RefreshVisualState();
            FocusUtility(_privacyAcceptButton);
        }

        private void BindPrivacyEvents()
        {
            if (_privacyEventsBound || _privacyDialog == null) return;
            _privacyTermsButton.onClick.AddListener(OnPrivacyTermsClicked);
            _privacyPolicyButton.onClick.AddListener(OnPrivacyPolicyClicked);
            _privacyDeclineButton.onClick.AddListener(OnPrivacyDeclineClicked);
            _privacyAcceptButton.onClick.AddListener(OnPrivacyAcceptClicked);
            _privacyEventsBound = true;
        }

        private void UnbindPrivacyEvents()
        {
            if (!_privacyEventsBound) return;
            _privacyTermsButton.onClick.RemoveListener(OnPrivacyTermsClicked);
            _privacyPolicyButton.onClick.RemoveListener(OnPrivacyPolicyClicked);
            _privacyDeclineButton.onClick.RemoveListener(OnPrivacyDeclineClicked);
            _privacyAcceptButton.onClick.RemoveListener(OnPrivacyAcceptClicked);
            _privacyEventsBound = false;
        }

        private void OnPrivacyTermsClicked() => OpenPolicy(ClientEndpoints.Current.termsUrl, "用户协议");

        private void OnPrivacyPolicyClicked() => OpenPolicy(ClientEndpoints.Current.privacyUrl, "隐私政策");

        private void OpenPolicy(string url, string name)
        {
            if (!PrivacyConsentOpen) return;
            if (string.IsNullOrEmpty(url))
            {
                // 只有开发包会走到这里:正式包没有协议地址出不了包(ClientEndpointsBuildGuard)。
                SetStatus($"这个包没有配置《{name}》的地址");
                return;
            }
            Application.OpenURL(url);
        }

        private void OnPrivacyDeclineClicked()
        {
            if (!PrivacyConsentOpen) return;
            if (!_privacyDeclinedOnce)
            {
                _privacyDeclinedOnce = true;
                _privacyBody.text = PrivacyDeclineBody;
                _privacyDeclineText.text = "退出游戏";
                return;
            }
            Debug.Log("[privacy] consent declined, quitting");
            Application.Quit();
        }

        private void OnPrivacyAcceptClicked()
        {
            if (!PrivacyConsentOpen) return;
            PrivacyConsent.Accept();
            _privacyDialog.gameObject.SetActive(false);
            RefreshVisualState();
        }
    }
}
