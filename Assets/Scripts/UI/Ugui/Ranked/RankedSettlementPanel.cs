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
        public event Action<uint> OpenTrialJournalRequested;

        private readonly RectTransform _root;
        private readonly TMP_Text _outcomeTitle, _metaLine, _confirmBadge;
        private readonly Image _surrenderBadgePlate;
        private readonly TMP_Text _surrenderBadgeText;
        private readonly Image _tierBadge;
        private readonly TMP_Text _ratingMainText, _tierTransitionText;
        private readonly Button _refreshRatingBtn;

        // 底部三卡（panel-paper：150 / 705 / 1260, y=570, 510x259）
        private readonly TMP_Text _card1Main, _card1Sub;
        private readonly TMP_Text _card2Main, _card2Sub;
        private readonly TMP_Text _card3Main, _card3Sub;
        private readonly RectTransform[] _legacyRewardCards = new RectTransform[3];
        private readonly Button _claimMaterialBtn;

        private readonly Button _backBtn, _requeueBtn, _trialJournalBtn;
        private readonly TMP_Text _requeueLabel, _trialJournalLabel;

        private uint _lastTeamSize = RankedFormatting.DefaultTeamSize;
        private uint _trialJournalEntryId;

        public RankedSettlementPanel(Transform designParent)
        {
            _root = QdaoUguiFactory.CreateRect("RankedSettlementPanel", designParent, 0f, 0f, DesignWidth, DesignHeight);

            // 严禁复用已退场旧窗框 ui-window-clean；统一使用已验收帮会同源道家窗框（guild_empty_shell，1932x814）
            CreateGuildArt("SettlementBase", _root, 0f, 118f, DesignWidth, 844f, "guild_empty_shell", preserveAspect: true, raycastTarget: true);

            _outcomeTitle = CreateLabel("OutcomeTitle", _root, 560f, 132f, 800f, 62f,
                "论剑结算", 44f, GoldCream, TextAlignmentOptions.Center, bold: true);
            _metaLine = CreateLabel("OutcomeMeta", _root, 360f, 214f, 1200f, 36f,
                "", 22f, InkSecondary, TextAlignmentOptions.Center);
            _confirmBadge = CreateLabel("ConfirmBadge", _root, 640f, 250f, 640f, 32f,
                "积分待确认", 20f, BronzeHint, TextAlignmentOptions.Center, bold: true);

            // 体面认输温和玉色角标（非逃跑红警、不额外扣奖，仅在 state.Surrendered==true 时显示）
            _surrenderBadgePlate = QdaoUguiFactory.CreateImage("SurrenderDignityBadge", _root, 1290f, 246f, 340f, 36f, null, false);
            _surrenderBadgePlate.color = SeasonEconomyUiArt.BattleArenaJadeLight;
            _surrenderBadgeText = CreateLabel("SurrenderDignityText", _surrenderBadgePlate.transform, 10f, 0f, 320f, 36f,
                "体面认输（不计逃跑）", 18f, SeasonEconomyUiArt.BattleArenaJade, TextAlignmentOptions.Center, bold: true);
            _surrenderBadgePlate.gameObject.SetActive(false);

            // 中部权威段位与分值变化区（采用已验收帮会宣纸高亮底 + 暖金描边，低光污高可读）
            var rankBorder = QdaoUguiFactory.CreateImage("SettlementRankBorder", _root, 488f, 290f, 944f, 254f, null, false);
            rankBorder.color = SeasonEconomyUiArt.BattleArenaWarmGold;
            var rankPanel = CreateGuildArt("SettlementRankPanel", _root, 490f, 292f, 940f, 250f, "guild_row_self");
            _tierBadge = CreateArt("SettlementTierBadge", rankPanel.transform, 72f, 22f, 206f, 206f, "rank-1", preserveAspect: true);
            _tierTransitionText = CreateLabel("TierTransition", rankPanel.transform, 312f, 38f, 590f, 54f,
                "等待权威落账…", 36f, JadeDark, bold: true);
            _ratingMainText = CreateLabel("RatingMain", rankPanel.transform, 312f, 102f, 590f, 104f,
                "积分待确认，正在等待服务端权威结算结果", 23f, InkSecondary, wrap: true);

            _refreshRatingBtn = CreateGuildButton("RefreshRatingBtn", rankPanel.transform, 738f, 192f, 170f, 44f,
                "guild_button_secondary", "刷新确认", 19f, JadeDark, out _, out _);
            _refreshRatingBtn.onClick.AddListener(() => RefreshRatingRequested?.Invoke());

            // 底部 3 张说明卡：x = 150 / 705 / 1260, y = 570, w = 510, h = 259（与 CoreSettlementRewardSection 对齐）
            _legacyRewardCards[0] = (RectTransform)BuildCard(150f, "本季有效场次", out _card1Main, out _card1Sub, 122f);
            _legacyRewardCards[1] = (RectTransform)BuildCard(705f, "赛季展示权益（仅展示）", out _card2Main, out _card2Sub, 122f);
            var card3 = BuildCard(1260f, "材料自选额度", out _card3Main, out _card3Sub, 76f);
            _legacyRewardCards[2] = (RectTransform)card3;

            _claimMaterialBtn = CreateGuildButton("ClaimMaterialBtn", card3, 155f, 194f, 200f, 46f,
                "guild_button_secondary", "前往领取材料", 19f, JadeDark, out _, out _);
            _claimMaterialBtn.onClick.AddListener(() => OpenMaterialClaimRequested?.Invoke());

            // 底部双按钮：采用已验收帮会主/次按钮切片，置于窗框下沿内侧
            _backBtn = CreateGuildButton("SettlementBackBtn", _root, 560f, 846f, 290f, 68f,
                "guild_button_secondary", "返回", 26f, JadeDark, out _, out _);
            _backBtn.onClick.AddListener(() => BackRequested?.Invoke());

            _requeueBtn = CreateGuildButton("SettlementRequeueBtn", _root, 900f, 840f, 420f, 78f,
                "guild_button_primary", "同规模再排", 28f, ButtonGoldText, out _, out _requeueLabel);
            _requeueBtn.onClick.AddListener(() => RequeueSameSizeRequested?.Invoke(_lastTeamSize));

            // 卷三 303「人宠会武」试炼志结算直达入口（仅当 trialJournalEntryId != 0 时展示）
            _trialJournalBtn = CreateGuildButton("SettlementTrialJournalBtn", _root, 1360f, 846f, 240f, 68f,
                "guild_button_secondary", "看试炼志", 24f, JadeDark, out _, out _trialJournalLabel);
            _trialJournalBtn.onClick.AddListener(() =>
            {
                if (_trialJournalEntryId != 0u) OpenTrialJournalRequested?.Invoke(_trialJournalEntryId);
            });
            _trialJournalBtn.gameObject.SetActive(false);

            _root.gameObject.SetActive(false);
        }

        public void SetTrialJournalEntry(uint entryId, string label = "看试炼志")
        {
            _trialJournalEntryId = entryId;
            if (_trialJournalLabel != null)
                _trialJournalLabel.text = string.IsNullOrEmpty(label) ? "看试炼志" : label;
            if (_trialJournalBtn != null)
                _trialJournalBtn.gameObject.SetActive(entryId != 0u);
        }

        public void Show(RankedBattleSettlementState state, RankedProfileSnapshot profile = null)
        {
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            Render(state, profile);
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 现场结算：左侧保留本季有效场次与赛季展示权益两卡，右侧用核心权威材料拆解块替代旧卡 3；
        /// 重登补查结算：恢复原有等宽三卡。
        /// </summary>
        public void SetCoreSettlementVisible(bool visible)
        {
            if (_legacyRewardCards[0] != null)
            {
                _legacyRewardCards[0].gameObject.SetActive(true);
                LayoutInfoCard(_legacyRewardCards[0], 150f, visible ? 390f : 510f);
            }
            if (_legacyRewardCards[1] != null)
            {
                _legacyRewardCards[1].gameObject.SetActive(true);
                LayoutInfoCard(_legacyRewardCards[1], visible ? 556f : 705f, visible ? 390f : 510f);
            }
            if (_legacyRewardCards[2] != null)
                _legacyRewardCards[2].gameObject.SetActive(!visible);
        }

        private static void LayoutInfoCard(RectTransform card, float x, float width)
        {
            card.anchoredPosition = new Vector2(x, -570f);
            card.sizeDelta = new Vector2(width, 259f);
            float pad = width < 450f ? 22f : 28f;
            float innerWidth = width - pad * 2f;
            for (int i = 0; i < card.childCount; i++)
            {
                if (card.GetChild(i) is RectTransform child)
                {
                    if (child.name == "CardHeaderBg") continue;
                    child.anchoredPosition = new Vector2(pad, child.anchoredPosition.y);
                    child.sizeDelta = new Vector2(innerWidth, child.sizeDelta.y);
                }
            }
        }

        public void Render(RankedBattleSettlementState state, RankedProfileSnapshot profile = null)
        {
            if (state == null) return;
            _lastTeamSize = state.EffectiveTeamSize;
            _requeueLabel.text = $"{_lastTeamSize}V{_lastTeamSize} 同规模再排";

            _outcomeTitle.text = RankedFormatting.OutcomeBannerText(state.EffectiveOutcome, state.PlayerTeamIndex, state.IsVoid);
            string recTag = state.FromRecovery ? " · 重登补弹" : "";
            string surrenderTag = state.Surrendered ? " · 体面认输（不计逃跑）" : "";
            _metaLine.text = $"论剑 {_lastTeamSize}V{_lastTeamSize} · 共 {state.TotalRounds} 回合 · 对局 #{state.BattleId}{surrenderTag}{recTag}";
            if (_surrenderBadgePlate != null)
                _surrenderBadgePlate.gameObject.SetActive(state.Surrendered && !state.IsVoid);

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
                _confirmBadge.text = state.Surrendered ? "权威积分已确认 · 体面认输（不计逃跑）" : "权威积分已确认";
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
                    : state.Surrendered ? "\n（体面认输：按正常战败结算，不计逃跑惩罚）"
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

            // 卡 1：本季有效场次 + 6 胜「锋芒」进度
            if (state.IsVoid)
            {
                _card1Main.text = "作废不计";
                _card1Sub.text = "作废对局不计入 4 / 8 / 12 场赛季进度与 6 胜锋芒称号。";
            }
            else if (state.IsRatingConfirmed && view != null)
            {
                bool newlyWonTitle = false;
                foreach (var r in view.NewlyUnlockedRewards)
                    if (r.Kind == RankedRewardKind.RankedRewardWinMilestone) { newlyWonTitle = true; break; }
                string winLine = newlyWonTitle
                    ? "本局解锁「锋芒」永久称号！"
                    : profile != null ? profile.GetWinMilestoneHint() : string.Empty;
                _card1Main.text = $"{view.SeasonValidMatchesAfter} / 12 场";
                string baseLine = view.Participated
                    ? $"本局已计入 · {RankedFormatting.NextMilestoneHint(view.SeasonValidMatchesAfter)}"
                    : "本局未计入有效场次（未达有效参与标准或逃离）。";
                _card1Sub.text = string.IsNullOrEmpty(winLine) ? baseLine : $"{baseLine}\n{winLine}";
            }
            else
            {
                _card1Main.text = "场次待确认";
                _card1Sub.text = "等待排位服确认本局有效参与、累计场次与 6 胜进度。";
            }

            // 卡 2：赛季展示权益
            if (state.IsVoid)
            {
                _card2Main.text = "无权益变动";
                _card2Sub.text = "永久展示外观由 4 / 8 / 12 场与 6 胜锋芒里程碑解锁。";
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
                    _card2Sub.text = RankedFormatting.NextMilestoneHint(view.SeasonValidMatchesAfter) + "（含 6 胜「锋芒」永久称号）。";
                }
            }
            else
            {
                _card2Main.text = "权益待确认";
                _card2Sub.text = "确认场次后自动发放对应永久展示外观与称号。";
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
                _card3Sub.text = "本局未达有效对抗门槛，不扣永久 660 额度；论剑积分照常结算，赛季有效场次以排位服确认为准。";
                _claimMaterialBtn.gameObject.SetActive(true);
            }
            else
            {
                _card3Main.text = "+0 份（阶段额度已满）";
                _card3Sub.text = "当前阶段材料总账已满（首套 660 / 周精进池），本局积分与赛季场次照常记录。";
                _claimMaterialBtn.gameObject.SetActive(true);
            }
        }

        private Transform BuildCard(float x, string header, out TMP_Text mainText, out TMP_Text subText, float subHeight = 82f)
        {
            var card = CreateGuildArt("SettlementCard_" + x, _root, x, 570f, 510f, 259f, "guild_row_normal");
            var headerBar = CreateGuildArt("CardHeaderBg", card.transform, 0f, 0f, 510f, 46f, "guild_table_header");
            headerBar.rectTransform.anchorMin = new Vector2(0f, 1f);
            headerBar.rectTransform.anchorMax = new Vector2(1f, 1f);
            headerBar.rectTransform.offsetMin = new Vector2(0f, -46f);
            headerBar.rectTransform.offsetMax = Vector2.zero;
            CreateLabel("CardHeader", card.transform, 28f, 8f, 454f, 32f, header, 21f, JadeHeading, bold: true);
            mainText = CreateLabel("CardMain", card.transform, 28f, 60f, 454f, 46f, "", 27f, JadeAccent, bold: true);
            subText = CreateLabel("CardSub", card.transform, 28f, 112f, 454f, subHeight, "", 19f, InkMuted, wrap: true);
            return card.transform;
        }
    }
}
