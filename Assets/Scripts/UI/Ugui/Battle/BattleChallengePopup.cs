using MmorpgClient.Game.CoreLoop;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 切磋挑战弹窗:OnChallengeInvite 时弹出,显示挑战者名 + 自定义规则摘要(公平3x3/易势/回合上限/禁控/公开观战)
    /// + 公平模板下的应战者预设选择 + 到 expires_at_ms 的倒计时 + 应战/拒绝按钮;超时自动收起。
    /// </summary>
    public sealed class BattleChallengePopup
    {
        private const float PanelW = 760f;
        private const float PanelH = 500f;

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;
        private readonly TMP_Text _bodyText;
        private readonly TMP_Text _rulesText;
        private readonly TMP_Text _countdownText;
        private readonly GameObject _presetRowRoot;
        private readonly UiTextButton[] _charButtons = new UiTextButton[3];
        private readonly UiTextButton[] _petButtons = new UiTextButton[3];
        private readonly UiTextButton _acceptButton;
        private readonly UiTextButton _declineButton;

        private ulong _challengeId;
        private ulong _expiresAtMs;
        private bool _useFreePreset;
        private uint _responderCharPreset = 101u;
        private uint _responderPetPreset = 203u;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public BattleChallengePopup(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;

            float x = (QdaoUguiTheme.DesignWidth - PanelW) * 0.5f;
            float y = 260f;
            var bg = BattleUiWidgets.CreatePanel("BattleChallengePopup", parent,
                x, y, PanelW, PanelH, BattleUiStyle.PanelBgLight);
            _root = (RectTransform)bg.transform;

            QdaoUguiFactory.CreateText("Title", _root, 0f, 24f, PanelW, 46f,
                "道友切磋挑战", 34f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _bodyText = QdaoUguiFactory.CreateText("Body", _root, 36f, 84f, PanelW - 72f, 42f,
                string.Empty, 25f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);
            _rulesText = QdaoUguiFactory.CreateText("Rules", _root, 36f, 134f, PanelW - 72f, 44f,
                string.Empty, 20f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _countdownText = QdaoUguiFactory.CreateText("Countdown", _root, 36f, 184f, PanelW - 72f, 36f,
                string.Empty, 21f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);

            var presetRect = QdaoUguiFactory.CreateRect("PresetRow", _root, 36f, 228f, PanelW - 72f, 132f);
            _presetRowRoot = presetRect.gameObject;
            QdaoUguiFactory.CreateText("PresetHint", presetRect, 0f, 0f, PanelW - 72f, 28f,
                "公平 3×3 切磋：请选择应战人物与灵宠模板", 18f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            string[] charLabels = { "剑修·力", "法宗·灵", "道医·体" };
            uint[] charIds = { 101u, 102u, 103u };
            for (int i = 0; i < 3; ++i)
            {
                uint cid = charIds[i];
                _charButtons[i] = BattleUiWidgets.CreateTextButton("CharPreset_" + cid, presetRect,
                    24f + i * 218f, 34f, 200f, 42f, charLabels[i], 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                _charButtons[i].Button.onClick.AddListener(() => { _responderCharPreset = cid; RefreshPresetHighlight(); });
            }
            string[] petLabels = { "猛攻灵宠", "法爆灵宠", "护主血宠" };
            uint[] petIds = { 201u, 202u, 203u };
            for (int i = 0; i < 3; ++i)
            {
                uint pid = petIds[i];
                _petButtons[i] = BattleUiWidgets.CreateTextButton("PetPreset_" + pid, presetRect,
                    24f + i * 218f, 84f, 200f, 42f, petLabels[i], 18f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                _petButtons[i].Button.onClick.AddListener(() => { _responderPetPreset = pid; RefreshPresetHighlight(); });
            }

            _acceptButton = BattleUiWidgets.CreateTextButton("Accept", _root, 100f, 392f, 250f, 76f,
                "应战", 28f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _acceptButton.Button.onClick.AddListener(() => Respond(true));

            _declineButton = BattleUiWidgets.CreateTextButton("Decline", _root, 410f, 392f, 250f, 76f,
                "拒绝", 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _declineButton.Button.onClick.AddListener(() => Respond(false));

            HideSilently();
        }

        public void Show(Match.ChallengeInviteS2C invite)
        {
            if (invite == null || _root == null) return;
            _challengeId = invite.ChallengeId;
            _expiresAtMs = invite.ExpiresAtMs;
            _useFreePreset = invite.CustomRules != null && invite.CustomRules.UseFreePreset;
            _responderCharPreset = 101u;
            _responderPetPreset = 203u;
            string name = string.IsNullOrEmpty(invite.ChallengerName)
                ? $"玩家{invite.ChallengerId}"
                : invite.ChallengerName;
            _bodyText.text = $"{name} 向你发起切磋挑战！";
            _rulesText.text = "约战规则：" + WuxingExpansionModels.FormatCustomDuelRulesSummary(invite.CustomRules);
            _countdownText.text = string.Empty;
            if (_presetRowRoot != null) _presetRowRoot.SetActive(_useFreePreset);
            RefreshPresetHighlight();
            _root.gameObject.SetActive(true);
            _owner?.RefreshModalDim();
        }

        private void RefreshPresetHighlight()
        {
            uint[] charIds = { 101u, 102u, 103u };
            uint[] petIds = { 201u, 202u, 203u };
            for (int i = 0; i < 3; ++i)
            {
                if (_charButtons[i]?.Plate != null) _charButtons[i].Plate.color = _responderCharPreset == charIds[i] ? BattleUiStyle.ButtonPlateAccent : BattleUiStyle.ButtonPlate;
                if (_petButtons[i]?.Plate != null) _petButtons[i].Plate.color = _responderPetPreset == petIds[i] ? BattleUiStyle.ButtonPlateAccent : BattleUiStyle.ButtonPlate;
            }
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
            uint charPreset = _useFreePreset ? _responderCharPreset : 0u;
            uint petPreset = _useFreePreset ? _responderPetPreset : 0u;
            HideSilently();
            if (client == null || challengeId == 0) return;
            client.RespondChallenge(challengeId, accept, charPreset, petPreset);
            _owner?.ShowToast(accept ? "已应战,等待开局…" : "已拒绝切磋");
        }
    }
}
