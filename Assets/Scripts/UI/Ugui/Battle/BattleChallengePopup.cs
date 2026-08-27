using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 切磋挑战弹窗:OnChallengeInvite 时弹出,显示挑战者名 + 到 expires_at_ms
    /// 的倒计时 + 应战/拒绝按钮;超时自动收起。
    /// </summary>
    public sealed class BattleChallengePopup
    {
        private const float PanelW = 720f;
        private const float PanelH = 420f;

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;
        private readonly TMP_Text _bodyText;
        private readonly TMP_Text _countdownText;
        private readonly UiTextButton _acceptButton;
        private readonly UiTextButton _declineButton;

        private ulong _challengeId;
        private ulong _expiresAtMs;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public BattleChallengePopup(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;

            float x = (QdaoUguiTheme.DesignWidth - PanelW) * 0.5f;
            float y = 300f;
            var bg = BattleUiWidgets.CreatePanel("BattleChallengePopup", parent,
                x, y, PanelW, PanelH, BattleUiStyle.PanelBgLight);
            _root = (RectTransform)bg.transform;

            QdaoUguiFactory.CreateText("Title", _root, 0f, 26f, PanelW, 48f,
                "切磋挑战", 34f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _bodyText = QdaoUguiFactory.CreateText("Body", _root, 40f, 116f, PanelW - 80f, 46f,
                string.Empty, 26f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);
            _countdownText = QdaoUguiFactory.CreateText("Countdown", _root, 40f, 182f, PanelW - 80f, 40f,
                string.Empty, 24f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);

            _acceptButton = BattleUiWidgets.CreateTextButton("Accept", _root, 90f, 292f, 250f, 76f,
                "应战", 28f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _acceptButton.Button.onClick.AddListener(() => Respond(true));

            _declineButton = BattleUiWidgets.CreateTextButton("Decline", _root, 380f, 292f, 250f, 76f,
                "拒绝", 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _declineButton.Button.onClick.AddListener(() => Respond(false));

            HideSilently();
        }

        public void Show(Match.ChallengeInviteS2C invite)
        {
            if (invite == null || _root == null) return;
            _challengeId = invite.ChallengeId;
            _expiresAtMs = invite.ExpiresAtMs;
            string name = string.IsNullOrEmpty(invite.ChallengerName)
                ? $"玩家{invite.ChallengerId}"
                : invite.ChallengerName;
            _bodyText.text = $"{name} 向你发起切磋挑战!";
            _countdownText.text = string.Empty;
            _root.gameObject.SetActive(true);
            _owner?.RefreshModalDim();
        }

        public void HideSilently()
        {
            _challengeId = 0;
            if (_root != null) _root.gameObject.SetActive(false);
            _owner?.RefreshModalDim();
        }

        /// <summary>每帧刷新倒计时;到 expires_at_ms 自动收起。</summary>
        public void Tick()
        {
            if (!IsVisible) return;
            long remainMs = (long)_expiresAtMs - BattleUiWidgets.NowUnixMs();
            if (remainMs <= 0)
            {
                HideSilently();
                _owner?.ShowToast("切磋邀请已超时");
                return;
            }
            _countdownText.text = $"剩余应答时间:{(remainMs + 999) / 1000} 秒";
        }

        private void Respond(bool accept)
        {
            var client = _owner?.Client;
            ulong challengeId = _challengeId;
            HideSilently();
            if (client == null || challengeId == 0) return;
            client.RespondChallenge(challengeId, accept);
            _owner?.ShowToast(accept ? "已应战,等待开局…" : "已拒绝切磋");
        }
    }
}
