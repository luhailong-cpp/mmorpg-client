using System;
using System.Collections.Generic;
using Match;
using MmorpgClient.Game.Ranked;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Ranked.RankedArtCatalog;

namespace MmorpgClient.UI.Ugui.Ranked
{
    using Transform = UnityEngine.Transform;
    /// <summary>
    /// 论剑单局个人结算面板（1920x1080 坐标系）：
    /// - 严格分离两条权威来源：match RPC 451（积分/段位/有效场次/展示权益）与 scene 终局/补查（日常材料自选额度）；
    /// - 积分未确认（PENDING）期间绝不展示本地推测分值或段位，明确显示「积分待确认」并提供刷新重试；
    /// - 作废局（VOID）不展示分值变化，不计季场次，无道具/材料变动；
    /// - 材料额度卡明确提示「已入材料账，前往领取后入包」，日额度满时展示「+0 · 赛季照记」；
    /// - 底部「返回」与「同规模再排」严格按 GEMINI_HANDOFF.md 坐标摆位。
    /// </summary>
    public sealed class RankedSettlementPanel
    {
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public RectTransform Root => _root;

        public event Action BackRequested;
        public event Action<uint> RequeueSameSizeRequested;
        public event Action RefreshRatingRequested;
        public event Action OpenMaterialClaimRequested;

        private readonly RectTransform _root;
        private readonly TMP_Text _outcomeTitle, _metaLine, _confirmBadge;
        private readonly Image _tierBadge;
        private readonly TMP_Text _ratingMainText, _tierTransitionText;
        private readonly Button _refreshRatingBtn;

        // 底部三卡（panel-paper：150 / 705 / 1260, y=570, 510x259）
        private readonly TMP_Text _card1Main, _card1Sub;
        private readonly TMP_Text _card2Main, _card2Sub;
        private readonly TMP_Text _card3Main, _card3Sub;
        private readonly RectTransform[] _legacyRewardCards = new RectTransform[3];
        private readonly Button _claimMaterialBtn;

        private readonly Button _backBtn, _requeueBtn;
        private readonly TMP_Text _requeueLabel;

        private uint _lastTeamSize = RankedFormatting.DefaultTeamSize;

        public RankedSettlementPanel(Transform designParent)
        {
            _root = QdaoUguiFactory.CreateRect("RankedSettlementPanel", designParent, 0f, 0f, DesignWidth, DesignHeight);

            CreateArt("SettlementBase", _root, 0f, 0f, DesignWidth, DesignHeight, "ui-window-clean", preserveAspect: true, raycastTarget: true);

            _outcomeTitle = CreateLabel("OutcomeTitle", _root, 560f, 74f, 800f, 72f,
                "论剑结算", 54f, GoldCream, TextAlignmentOptions.Center, bold: true);
            _metaLine = CreateLabel("OutcomeMeta", _root, 460f, 178f, 1000f, 38f,
                "", 23f, InkMuted, TextAlignmentOptions.Center);
            _confirmBadge = CreateLabel("ConfirmBadge", _root, 660f, 224f, 600f, 34f,
                "积分待确认", 21f, BronzeHint, TextAlignmentOptions.Center, bold: true);

            // 中部权威段位与分值变化区
            var rankPanel = CreateArt("SettlementRankPanel", _root, 490f, 272f, 940f, 268f, "panel-rank");
            _tierBadge = CreateArt("SettlementTierBadge", rankPanel.transform, 72f, 24f, 220f, 220f, "rank-1", preserveAspect: true);
            _tierTransitionText = CreateLabel("TierTransition", rankPanel.transform, 318f, 44f, 580f, 58f,
                "等待权威落账…", 38f, GoldCream, bold: true);
            _ratingMainText = CreateLabel("RatingMain", rankPanel.transform, 318f, 114f, 580f, 96f,
                "积分待确认，正在等待服务端权威结算结果", 25f, GoldSoft, wrap: true);

            _refreshRatingBtn = CreateArtButton("RefreshRatingBtn", rankPanel.transform, 738f, 204f, 170f, 46f,
                "drawer-collapse", "刷新确认", 19f, JadeDark, out _, out _);
            _refreshRatingBtn.onClick.AddListener(() => RefreshRatingRequested?.Invoke());

            // 底部 3 张说明卡：x = 150 / 705 / 1260, y = 570, w = 510, h = 259
            _legacyRewardCards[0] = (RectTransform)BuildCard(150f, "本季有效场次", out _card1Main, out _card1Sub);
            _legacyRewardCards[1] = (RectTransform)BuildCard(705f, "赛季展示权益（仅展示）", out _card2Main, out _card2Sub);
            var card3 = BuildCard(1260f, "材料自选额度", out _card3Main, out _card3Sub);
            _legacyRewardCards[2] = (RectTransform)card3;

            _claimMaterialBtn = CreateArtButton("ClaimMaterialBtn", card3, 155f, 194f, 200f, 46f,
                "appearance-action-idle", "前往领取材料", 19f, JadeDark, out _, out _);
            _claimMaterialBtn.onClick.AddListener(() => OpenMaterialClaimRequested?.Invoke());

            // 底部双按钮：返回 [534, 869, 310, 83]，同规模再排 [899, 861, 490, 96]
            _backBtn = CreateArtButton("SettlementBackBtn", _root, 534f, 869f, 310f, 83f,
                "button-secondary", "返回", 28f, JadeDark, out _, out _);
            _backBtn.onClick.AddListener(() => BackRequested?.Invoke());

            _requeueBtn = CreateArtButton("SettlementRequeueBtn", _root, 899f, 861f, 490f, 96f,
                "button-primary", "同规模再排", 31f, ButtonGoldText, out _, out _requeueLabel, preserveAspect: true);
            _requeueBtn.onClick.AddListener(() => RequeueSameSizeRequested?.Invoke(_lastTeamSize));

            _root.gameObject.SetActive(false);
        }

        public void Show(RankedBattleSettlementState state)
        {
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            Render(state);
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>核心玩法现场结算使用权威材料拆解块替换旧三卡，评分区仍由本面板负责。</summary>
        public void SetCoreSettlementVisible(bool visible)
        {
            foreach (var card in _legacyRewardCards)
                if (card != null) card.gameObject.SetActive(!visible);
        }

        public void Render(RankedBattleSettlementState state)
        {
            if (state == null) return;
            _lastTeamSize = state.EffectiveTeamSize;
            _requeueLabel.text = $"{_lastTeamSize}V{_lastTeamSize} 同规模再排";

            _outcomeTitle.text = RankedFormatting.OutcomeBannerText(state.EffectiveOutcome, state.PlayerTeamIndex, state.IsVoid);
            string recTag = state.FromRecovery ? " · 重登补弹" : "";
            _metaLine.text = $"论剑 {_lastTeamSize}V{_lastTeamSize} · 共 {state.TotalRounds} 回合 · 对局 #{state.BattleId}{recTag}";

            // 中部：权威积分与段位
            var view = state.AuthoritativeView;
            if (state.IsVoid)
            {
                _confirmBadge.text = "本局作废（无扣分、无消耗、不计场次）";
                _confirmBadge.color = WarnRed;
                _tierBadge.sprite = RankBadge(RankedTier.Unspecified);
                _tierTransitionText.text = "本局作废";
                _ratingMainText.text = "节点故障或对局异常中止，本局已作废。\n所有角色积分、有效场次、永久宠物与背包资产均无任何变动。";
                _refreshRatingBtn.gameObject.SetActive(false);
            }
            else if (state.IsZeroRound)
            {
                _confirmBadge.text = "零回合结束（不计分、不计场次）";
                _confirmBadge.color = BronzeHint;
                _tierBadge.sprite = RankBadge(view?.TierAfter ?? RankedTier.Unspecified);
                _tierTransitionText.text = "0 回合不计分";
                _ratingMainText.text = "本局未发生有效回合，不产生积分变化与赛季场次进度。";
                _refreshRatingBtn.gameObject.SetActive(false);
            }
            else if (state.IsRatingConfirmed && view != null)
            {
                _confirmBadge.text = "权威积分已确认";
                _confirmBadge.color = JadeAccent;
                _tierBadge.sprite = RankBadge(view.TierAfter);

                string beforeTier = RankedFormatting.TierDisplayName(view.TierBefore, !view.NewlyQualified && view.TierBefore != RankedTier.Unspecified);
                string afterTier = RankedFormatting.TierDisplayName(view.TierAfter, view.TierAfter != RankedTier.Unspecified);
                _tierTransitionText.text = view.TierBefore != view.TierAfter
                    ? $"{beforeTier}  →  {afterTier}"
                    : afterTier;

                string beforeScore = RankedFormatting.FormatRating(view.RatingBeforeCenti);
                string afterScore = RankedFormatting.FormatRating(view.RatingAfterCenti);
                string deltaStr = RankedFormatting.FormatRatingDelta(view.RatingDeltaCenti);
                string qualNote = view.NewlyQualified ? " · 本季首次亮段上榜！" : "";
                string partNote = view.Fled ? "\n（逃离对局：照常计分，不计有效参与场次）"
                    : !view.Participated ? "\n（未满足有效参与条件：不计赛季有效场次）" : "";
                _ratingMainText.text = $"积分：{beforeScore}  →  {afterScore}（{deltaStr}）{qualNote}{partNote}";
                _refreshRatingBtn.gameObject.SetActive(false);
            }
            else
            {
                _confirmBadge.text = state.RatingLoading ? "积分待确认（查询权威落账中…）" : "积分待确认";
                _confirmBadge.color = BronzeHint;
                _tierBadge.sprite = RankBadge(RankedTier.Unspecified);
                _tierTransitionText.text = "积分待确认";
                _ratingMainText.text = string.IsNullOrEmpty(state.RatingError)
                    ? "战斗已结束，正在等待权威排位服确认本次积分、段位与赛季场次。"
                    : $"权威积分尚未确认（{state.RatingError}）。可点击刷新重试，或返回大厅稍后查看。";
                _refreshRatingBtn.gameObject.SetActive(true);
                _refreshRatingBtn.interactable = !state.RatingLoading;
            }

            // 卡 1：本季有效场次
            if (state.IsVoid)
            {
                _card1Main.text = "作废不计";
                _card1Sub.text = "作废对局不计入 4 / 8 / 12 场赛季进度。";
            }
            else if (state.IsRatingConfirmed && view != null)
            {
                _card1Main.text = $"{view.SeasonValidMatchesAfter} / 12 场";
                _card1Sub.text = view.Participated
                    ? $"本局已计入有效场次 · {RankedFormatting.NextMilestoneHint(view.SeasonValidMatchesAfter)}"
                    : "本局未计入有效场次（未达有效参与标准或逃离）。";
            }
            else
            {
                _card1Main.text = "场次待确认";
                _card1Sub.text = "等待排位服确认本局有效参与与累计场次。";
            }

            // 卡 2：赛季展示权益
            if (state.IsVoid)
            {
                _card2Main.text = "无权益变动";
                _card2Sub.text = "永久展示外观仅由有效对局累计 4 / 8 / 12 场解锁。";
            }
            else if (state.IsRatingConfirmed && view != null)
            {
                if (view.NewlyUnlockedRewards.Count > 0)
                {
                    var names = new List<string>(view.NewlyUnlockedRewards.Count);
                    foreach (var r in view.NewlyUnlockedRewards) names.Add(RankedFormatting.RewardKindName(r.Kind));
                    _card2Main.text = "新取得：" + string.Join("、", names);
                    _card2Sub.text = "已永久发放至「已获外观」，可随时佩戴或隐藏（非打造材料）。";
                }
                else
                {
                    _card2Main.text = "暂无新解锁";
                    _card2Sub.text = RankedFormatting.NextMilestoneHint(view.SeasonValidMatchesAfter) + "（永久展示外观）。";
                }
            }
            else
            {
                _card2Main.text = "权益待确认";
                _card2Sub.text = "确认场次后自动发放对应永久展示外观。";
            }

            // 卡 3：日常材料自选额度（来自 scene 权威终局/补查，与材料领取入包分离）
            if (state.IsVoid)
            {
                _card3Main.text = "+0 份（作废）";
                _card3Sub.text = "本局作废，不增减每日材料自选额度。";
                _claimMaterialBtn.gameObject.SetActive(false);
            }
            else if (!state.MaterialCreditConfirmed)
            {
                _card3Main.text = "额度待确认";
                _card3Sub.text = "请返回主城打开「材料自选」查看最新可领取份额。";
                _claimMaterialBtn.gameObject.SetActive(true);
            }
            else if (state.MaterialCreditUnitsAdded > 0)
            {
                _card3Main.text = state.MaterialTradableUnitsAdded > 0
                    ? $"+{state.MaterialCreditUnitsAdded} 份（含 {state.MaterialTradableUnitsAdded} 可交易）"
                    : $"+{state.MaterialCreditUnitsAdded} 份自选额度";
                _card3Sub.text = "已计入材料总账（未自动入包），请走既有「材料自选」领取入包。";
                _claimMaterialBtn.gameObject.SetActive(true);
            }
            else if (state.MaterialPvpLossIneligible)
            {
                _card3Main.text = "+0 份（未达有效对抗）";
                _card3Sub.text = "本局未达有效对抗门槛，不扣永久 660 额度；论剑积分与赛季场次照常记录。";
                _claimMaterialBtn.gameObject.SetActive(true);
            }
            else
            {
                _card3Main.text = "+0 份（阶段额度已满）";
                _card3Sub.text = "当前阶段材料总账已满（首套 660 / 周精进 100），本局积分与赛季场次照常记录。";
                _claimMaterialBtn.gameObject.SetActive(true);
            }
        }

        private Transform BuildCard(float x, string header, out TMP_Text mainText, out TMP_Text subText)
        {
            var card = CreateArt("SettlementCard_" + x, _root, x, 570f, 510f, 259f, "panel-paper");
            CreateLabel("CardHeader", card.transform, 28f, 18f, 454f, 34f, header, 22f, JadeHeading, bold: true);
            mainText = CreateLabel("CardMain", card.transform, 28f, 62f, 454f, 48f, "", 28f, JadeAccent, bold: true);
            subText = CreateLabel("CardSub", card.transform, 28f, 114f, 454f, 82f, "", 19f, InkMuted, wrap: true);
            return card.transform;
        }
    }
}
