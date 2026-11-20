using TMPro;
using MmorpgClient.Game;
using MmorpgClient.Net;
using UnityEngine;

namespace MmorpgClient.UI.Ugui
{
    // 实名窗(服务端 docs/design/real-name-anti-addiction.md §5.7):复用账号面板的两个输入框,账号框填姓名、
    // 密码框改成明文填身份证号;提交按钮在这个用途下调 /api/real-name。提交成功后继续进区:口令登录用原口令,
    // 渠道登录的凭证一次性、已被用掉,改用这次签发的 access token。姓名和身份证号提交后立刻清空,不进 SessionModel、不记日志。
    public sealed partial class QdaoServerSelectView
    {
        private bool _credentialForRealName;
        private ServerListZone _realNameZone;
        private LoginCredential _realNameCredential;
        private string _accountPlaceholder;
        private string _passwordPlaceholder;
        private int _accountLimit;
        private int _passwordLimit;
        private TMP_InputField.CharacterValidation _accountValidation;
        private TMP_InputField.CharacterValidation _passwordValidation;

        private void BeginRealName(ServerListZone zone, LoginCredential credential)
        {
            if (_credentialForRealName) return;
            _credentialForRealName = true;
            _realNameZone = zone;
            _realNameCredential = credential;
            _accountPlaceholder = PlaceholderText(_accountInput);
            _passwordPlaceholder = PlaceholderText(_passwordInput);
            _accountLimit = _accountInput.characterLimit;
            _passwordLimit = _passwordInput.characterLimit;
            _accountValidation = _accountInput.characterValidation;
            _passwordValidation = _passwordInput.characterValidation;

            _accountInput.text = string.Empty;
            _passwordInput.text = string.Empty;
            // 姓名要能输汉字和「·」,身份证号最后一位可能是 X:两个框都先放开字符限制。
            _accountInput.characterValidation = TMP_InputField.CharacterValidation.None;
            _passwordInput.characterValidation = TMP_InputField.CharacterValidation.None;
            _accountInput.characterLimit = 32;
            _passwordInput.characterLimit = 18;
            _passwordInput.contentType = TMP_InputField.ContentType.Standard;
            _passwordInput.ForceLabelUpdate();
            SetPlaceholder(_accountInput, "真实姓名");
            SetPlaceholder(_passwordInput, "身份证号");
            ShowCredentialPanel(true, focusAccount: true);
            SetStatus(GameClient.RealNameRequiredMessage + "（信息仅用于国家网络游戏防沉迷实名认证）");
        }

        private void EndRealName()
        {
            if (!_credentialForRealName) return;
            _credentialForRealName = false;
            _accountInput.text = string.Empty;
            _passwordInput.text = string.Empty;
            _accountInput.characterLimit = _accountLimit;
            _passwordInput.characterLimit = _passwordLimit;
            _accountInput.characterValidation = _accountValidation;
            _passwordInput.characterValidation = _passwordValidation;
            _passwordInput.contentType = TMP_InputField.ContentType.Password;
            _passwordInput.ForceLabelUpdate();
            SetPlaceholder(_accountInput, _accountPlaceholder);
            SetPlaceholder(_passwordInput, _passwordPlaceholder);
            // 口令登录把账号放回去,玩家看到的还是自己的账号。
            if (_realNameCredential.IsPassword) _accountInput.text = _realNameCredential.Account;
        }

        private void SubmitRealNameFromPanel()
        {
            var name = (_accountInput.text ?? string.Empty).Trim();
            var idNumber = (_passwordInput.text ?? string.Empty).Trim();
            if (name.Length == 0) { SetStatus("请输入真实姓名"); return; }
            if (idNumber.Length != 18) { SetStatus("请输入18位身份证号"); return; }
            if (_app?.GameClient == null || _realNameZone == null) return;

            _busy = true;
            int runId = ++_enterRunId;
            RefreshVisualState();
            SetStatus("正在实名认证…");
            _passwordInput.text = string.Empty; // 身份证号不留在界面上
            _app.Run(_app.GameClient.SubmitRealName(_realNameZone.zone_id, name, idNumber,
                r =>
                {
                    if (this == null || runId != _enterRunId) return;
                    _busy = false;
                    var text = GameClient.DescribeRealNameResult(r);
                    if (text != null)
                    {
                        RefreshVisualState();
                        SetStatus(text);
                        return;
                    }
                    var zone = _realNameZone;
                    var credential = _realNameCredential.IsPassword
                        ? _realNameCredential
                        : LoginCredential.ForAccessToken(_app.GameClient.AccessToken);
                    EndRealName();
                    ShowCredentialPanel(false);
                    RunEnterZone(zone, credential, null);
                },
                err =>
                {
                    if (this == null || runId != _enterRunId) return;
                    _busy = false;
                    RefreshVisualState();
                    Debug.LogWarning($"[QdaoUgui] real-name submit: {err}");
                    SetStatus("实名认证服务繁忙，请稍后重试");
                }));
        }

        private static string PlaceholderText(TMP_InputField input) =>
            input.placeholder is TMP_Text text ? text.text : string.Empty;

        private static void SetPlaceholder(TMP_InputField input, string value)
        {
            if (input.placeholder is TMP_Text text) text.text = value;
        }
    }
}
