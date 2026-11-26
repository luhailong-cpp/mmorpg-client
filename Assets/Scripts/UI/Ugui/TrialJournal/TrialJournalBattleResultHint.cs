using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.TrialJournal;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.CoreLoop;
using MmorpgClient.UI.Ugui.Social;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.TrialJournal
{
    /// <summary>
    /// 战斗结算页嵌入式「试炼志行内提示条」（严格还原 D:\work\image\designs\trial-journal-ui-v1\previews\12-battle-result-hint.png）。
    /// 仅在存在有效 trial_journal_entry_id 时显示，不弹窗打断；点击「看试炼志」经宿主回调安全关闭结算并定位到对应关卡格；
    /// V3 胜局同步展示「发帖约战」次要按钮，一键生成权威约战帖草稿。
    /// </summary>
    public sealed class TrialJournalBattleResultHint
    {
        public const float BarWidth = 820f;
        public const float BarHeight = 82f;

        private readonly RectTransform _root;
        private readonly TMP_Text _headline;
        private readonly TMP_Text _subtitle;
        private readonly Button _shareButton;
        private readonly Button _openButton;
        private uint _entryId;
        private ulong _shareBattleId;
        private readonly Action<uint> _onOpen;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public TrialJournalBattleResultHint(
            UnityEngine.Transform parent,
            float x,
            float y,
            Action<uint> onOpen)
        {
            _onOpen = onOpen;
            var plate = TrialJournalUiArt.SlicedOrTint("TrialJournalHintBar", parent,
                x, y, BarWidth, BarHeight, TrialJournalUiArt.LoadSkin("battle_result_hint_bar", "member_row"),
                TrialJournalUiArt.Paper, raycast: true);
            _root = (RectTransform)plate.transform;

            TrialJournalUiArt.SealMedallion("SealMedallion", _root,
                18f, 15f, 52f, lit: true, "印", 24f);

            _headline = TrialJournalUiArt.Label("Headline", _root, 88f, 14f, 512f, 30f,
                "", 20f, TrialJournalUiArt.Jade, bold: true);
            _subtitle = TrialJournalUiArt.Label("Subtitle", _root, 88f, 44f, 512f, 26f,
                "", 16f, TrialJournalUiArt.Sub);

            _shareButton = TrialJournalUiArt.SpriteButton("ShareTrialChallenge", _root,
                476f, 15f, 134f, 52f, "发帖约战", 18f,
                primary: true, interactable: true,
                onClick: HandleShareClicked);
            _shareButton.gameObject.SetActive(false);

            _openButton = TrialJournalUiArt.SpriteButton("OpenTrialJournal", _root,
                620f, 15f, 180f, 52f, "看试炼志", 19f,
                primary: false, interactable: true,
                onClick: () =>
                {
                    if (_entryId != 0u) _onOpen?.Invoke(_entryId);
                });
            Hide();
        }

        public bool ShowForRecovered(global::PlayerBattleResult result, global::TrialJournalSnapshot snapshot)
        {
            if (result == null || result.TrialJournalEntryId == 0u)
            {
                Hide();
                return false;
            }
            _entryId = result.TrialJournalEntryId;
            var entry = TrialJournalModels.FindEntry(snapshot, _entryId);
            string entryName = entry != null && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName
                : $"试炼格#{_entryId}";
            int newlyLit = result.TrialNewlyLitSealIds?.Count ?? 0;
            _headline.text = newlyLit > 0 ? $"{entryName} · 战法印 +{newlyLit}" : $"{entryName} · 试炼结算";

            string summary = TrialJournalModels.FormatBattleResultSummary(result, snapshot);
            if (string.IsNullOrEmpty(summary))
            {
                summary = snapshot != null && snapshot.Available
                    ? $"永久道印 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals} · 点击查看复盘与里程碑"
                    : "点击查看试炼志复盘与里程碑进度";
            }
            _subtitle.text = summary;

            ulong battleId = result.TrialBattleId != 0ul ? result.TrialBattleId : result.BattleId;
            bool canShare = battleId != 0ul
                && BattleReportClient.CanWatchReplayFromRecovered(result)
                && IsPlayerVictory((eBattleOutcome)result.Outcome, result.TeamIndex)
                && !result.Fled;
            ConfigureShareButton(canShare ? battleId : 0ul);

            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            return true;
        }

        public bool ShowForLive(global::BattleEndS2C end, global::TrialJournalSnapshot snapshot)
        {
            var settlement = end?.Settlement;
            uint entryId = settlement?.TrialFacts?.JournalEntryId ?? 0u;
            if (entryId == 0u)
            {
                Hide();
                return false;
            }
            _entryId = entryId;
            var entry = TrialJournalModels.FindEntry(snapshot, _entryId);
            string entryName = entry != null && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName
                : $"试炼格#{_entryId}";
            uint vowMask = settlement.TrialFacts.ChallengeVowMask & TrialJournalModels.VowMaskAll;
            _headline.text = vowMask != 0u
                ? $"{entryName} · {TrialJournalModels.CountVows(vowMask)}誓约记档"
                : $"{entryName} · 试炼志已记档";
            _subtitle.text = vowMask != 0u
                ? $"生效誓约：{TrialJournalModels.FormatVowSummary(vowMask)}"
                : (snapshot != null && snapshot.Available
                    ? $"永久道印 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals} · 战法印与纪录将在返回后同步"
                    : "战法印、配装复盘与里程碑已在结算同步");

            var mode = (Match.MatchMode)settlement.MatchMode;
            bool isPveTrial = mode == Match.MatchMode.PveSolo || mode == Match.MatchMode.PveTeam;
            bool canShare = settlement.BattleId != 0ul
                && isPveTrial
                && IsPlayerVictory(settlement.Outcome, settlement.PlayerTeamIndex)
                && !settlement.Fled;
            ConfigureShareButton(canShare ? settlement.BattleId : 0ul);

            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            return true;
        }

        public void SetVerticalPosition(float y)
        {
            if (_root != null) _root.anchoredPosition = new Vector2(_root.anchoredPosition.x, -y);
        }

        public void Hide()
        {
            _entryId = 0u;
            ConfigureShareButton(0ul);
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void ConfigureShareButton(ulong battleId)
        {
            _shareBattleId = battleId;
            bool showShare = battleId != 0ul;
            if (_shareButton != null) _shareButton.gameObject.SetActive(showShare);
            float textWidth = showShare ? 378f : 512f;
            if (_headline != null) _headline.rectTransform.sizeDelta = new Vector2(textWidth, 30f);
            if (_subtitle != null) _subtitle.rectTransform.sizeDelta = new Vector2(textWidth, 26f);
        }

        private void HandleShareClicked()
        {
            if (_shareBattleId == 0ul) return;
            var reports = BattleReportClient.Instance;
            if (reports == null)
            {
                CoreGameplayUiRoot.Instance?.ShowToast("约战分享模块尚未就绪，请稍候重试。");
                return;
            }
            reports.CreateTrialChallengeCard(_shareBattleId,
                card =>
                {
                    var snap = AppBootstrap.Instance?.GameClient?.TrialJournal?.Snapshot;
                    string draft = TrialJournalModels.FormatChallengeCardChatDraft(card, snap);
                    BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
                    if (SocialUiRoot.Instance == null || !SocialUiRoot.Instance.TryComposeTrialChallengeCard(draft))
                    {
                        CoreGameplayUiRoot.Instance?.ShowToast("约战帖已生成，请打开仙友会世界频道发送。");
                    }
                },
                err =>
                {
                    if (!string.IsNullOrEmpty(err))
                        CoreGameplayUiRoot.Instance?.ShowToast(err);
                });
        }

        private static bool IsPlayerVictory(eBattleOutcome outcome, uint teamIndex)
        {
            return (outcome == eBattleOutcome.BattleOutcomeSideAWin && teamIndex == 0u)
                || (outcome == eBattleOutcome.BattleOutcomeSideBWin && teamIndex == 1u);
        }
    }
}
