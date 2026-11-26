using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Season;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>可嵌入既有结算窗口的材料拆解与赛季印进度反馈块；全部数值取自权威结算只读视图。</summary>
    public sealed class CoreSettlementRewardSection : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;
        private GameClient _game;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _core = core;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show() { if (IsVisible) return; gameObject.SetActive(true); Refresh(); }
        public void Hide() => gameObject.SetActive(false);

        public void Refresh()
        {
            if (_root == null || _core == null) return;
            var view = _core.BuildLastSettlementReward();
            if (view == null) return;
            CoreLoopUiArt.Clear(transform);
            var rect = transform as RectTransform;
            float width = rect != null && rect.rect.width > 0 ? rect.rect.width : 820;
            float height = rect != null && rect.rect.height > 0 ? rect.rect.height : 180;
            var highlights = _core.BuildLastBossTrialHighlights();
            bool hasHighlights = highlights != null && highlights.Count > 0;
            var review = _core.BuildLastTacticalReview();
            bool hasReview = review != null && review.HasContent;
            var seasonProgress = SeasonModels.BuildSettlementProgressView(_game?.Battle?.LastBattleEnd, _game?.Season?.Snapshot);
            bool hasSeason = seasonProgress != null;
            float seasonH = hasSeason ? (seasonProgress.HonorNewlyUnlocked ? 58f : 42f) : 0f;
            var progression = _core.BuildLastSettlementProgression();
            bool hasProgression = progression != null && progression.HasVisibleRewards;
            float progH = hasProgression ? (progression.LeveledUp ? 54f : 34f) : 0f;
            float baseH = 180f + (hasProgression ? progH + 4f : 0f) + (hasSeason ? seasonH + 4f : 0f) + (hasHighlights ? 40f : 0f) + (hasReview ? 56f : 0f);
            float scale = Mathf.Min(width / 820f, height / baseH);
            float offsetX = (width - 820f * scale) * .5f;
            float offsetY = (height - baseH * scale) * .5f;
            var body = CoreLoopUiArt.Rect("RewardBody", transform, offsetX, offsetY, 820, baseH);
            body.localScale = new UnityEngine.Vector3(scale, scale, 1);

            CoreLoopUiArt.Image("RewardBackground", body, 0, 0, 820, 180,
                "card", view.PvpLossIneligible ? new Color(.98f, .91f, .76f, .98f) : CoreLoopUiArt.Ivory);
            if (view.PvpLossIneligible)
            {
                CoreLoopUiArt.Image("IneligibleAccent", body, 0, 0, 10, 180, null, CoreLoopUiArt.Warning);
                var softBanner = SeasonEconomyUiArt.LoadControl("notice_banner_soft");
                if (softBanner != null)
                    SeasonEconomyUiArt.CreateSlicedOrSimple(body, "IneligibleNoticeBanner", 18, 45, 784, 52,
                        softBanner, new Color(.97f, .93f, .83f, .96f));
            }
            CoreLoopUiArt.Text("Headline", body, 20, 8, view.TradableUnitsAdded > 0u ? 580 : 780, 40,
                view.Headline, 27, view.PvpLossIneligible ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade,
                true, false);
            if (view.TradableUnitsAdded > 0u)
            {
                var tradableSprite = SeasonEconomyUiArt.LoadIcon("badge_tradable_tag");
                if (tradableSprite != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("TradableHeadlineIcon", body, 608, 10, 36, 36, tradableSprite, false);
                    badgeImg.preserveAspect = true;
                }
                CoreLoopUiArt.Text("TradableHeadlineTag", body, 648, 12, 154, 34,
                    $"含 {view.TradableUnitsAdded} 可交易", 19, SeasonEconomyUiArt.GoldHighlight, true, false, TextAlignmentOptions.MidlineLeft);
            }
            CoreLoopUiArt.Text("Detail", body, view.PvpLossIneligible ? 48 : 20, 49, view.PvpLossIneligible ? 724 : 780, 44,
                view.DetailNote, 18, view.PvpLossIneligible ? SeasonEconomyUiArt.SoftWarnText : CoreLoopUiArt.Ink,
                view.PvpLossIneligible, true);

            DrawAmount(body, "Base", 22, "基础", view.BaseUnitsAdded);
            DrawAmount(body, "Bonus", 202, "加量", view.BonusUnitsAdded);
            DrawAmount(body, "Tradable", 382, "可交易", view.TradableUnitsAdded,
                view.TradableUnitsAdded > 0u ? SeasonEconomyUiArt.GoldHighlight : (Color?)null,
                view.TradableUnitsAdded > 0u ? SeasonEconomyUiArt.LoadIcon("badge_tradable_tag") : null);
            DrawAmount(body, "Bound", 562, "绑定", view.BoundUnitsAdded);

            string progress = view.WeeklyPhaseActive
                ? $"周精进材料 {view.EarnedThisWeek}/{view.WeeklyLimit}"
                : $"永久首套 {view.LifetimeEarned}/{view.LifetimeLimit} · 账号可交易 {view.TradableEarnedTotal}/{view.TradableLifetimeLimit}";
            CoreLoopUiArt.Text("Progress", body, 22, 141, 575, 22,
                progress, 17, CoreLoopUiArt.Muted);
            CoreLoopUiArt.Image("ProgressTrack", body, 22, 166, 575, 7,
                null, new Color(.76f, .8f, .7f, .9f));
            uint earned = view.WeeklyPhaseActive ? view.EarnedThisWeek : view.LifetimeEarned;
            uint limit = view.WeeklyPhaseActive ? view.WeeklyLimit : view.LifetimeLimit;
            float progressWidth = limit == 0u ? 0f : 575f * Mathf.Clamp01((float)earned / limit);
            if (progressWidth > 0f)
                CoreLoopUiArt.Image("ProgressFill", body, 22, 166, progressWidth, 7,
                    null, CoreLoopUiArt.Jade);
            CoreLoopUiArt.Button("Claim", body, 631, 137, 168, 38,
                "自选领取 " + view.ClaimableUnits, _root.OpenMaterialClaim,
                view.ClaimableUnits > 0, false, 18);

            float cursorY = 184f;
            if (hasProgression)
            {
                Color progBg = progression.LeveledUp
                    ? new Color(.98f, .93f, .76f, .98f)
                    : new Color(.92f, .95f, .90f, .96f);
                CoreLoopUiArt.Image("ProgressionSettlementBack", body, 0, cursorY, 820, progH, "hud-strip", progBg);
                CoreLoopUiArt.Image("ProgressionSettlementAccent", body, 0, cursorY, 6, progH, null,
                    progression.LeveledUp ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Jade);
                if (progression.LeveledUp)
                {
                    var lvLbl = CoreLoopUiArt.Text("ProgressionLevelUpLine", body, 16, cursorY + 4f, 610, 24,
                        progression.LevelUpBannerLine, 19, SeasonEconomyUiArt.GoldHighlight, true, false, TextAlignmentOptions.MidlineLeft);
                    lvLbl.enableAutoSizing = true; lvLbl.fontSizeMin = 13f; lvLbl.fontSizeMax = 19f;
                    var gainLbl = CoreLoopUiArt.Text("ProgressionGainLine", body, 16, cursorY + 29f, 610, 21,
                        progression.RewardSummaryLine, 16, CoreLoopUiArt.Ink, false, false, TextAlignmentOptions.MidlineLeft);
                    gainLbl.enableAutoSizing = true; gainLbl.fontSizeMin = 12f; gainLbl.fontSizeMax = 16f;
                    CoreLoopUiArt.Button("OpenAttributeAllocFromSettlement", body, 636, cursorY + (progH - 34f) * 0.5f, 168, 34,
                        "前往加点", _root.OpenAttributeAllocation, true, true, 17);
                }
                else
                {
                    var gainLbl = CoreLoopUiArt.Text("ProgressionGainLine", body, 16, cursorY + 4f, 788, 26,
                        progression.RewardSummaryLine, 17, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineLeft);
                    gainLbl.enableAutoSizing = true; gainLbl.fontSizeMin = 12f; gainLbl.fontSizeMax = 17f;
                }
                cursorY += progH + 4f;
            }
            if (hasSeason)
            {
                bool gained = seasonProgress.PointsAdded > 0u;
                Color bg = seasonProgress.HonorNewlyUnlocked
                    ? new Color(.97f, .92f, .76f, .98f)
                    : new Color(.90f, .96f, .92f, .96f);
                CoreLoopUiArt.Image("SeasonSealSettlementBack", body, 0, cursorY, 820, seasonH, "hud-strip", bg);
                CoreLoopUiArt.Image("SeasonSealSettlementAccent", body, 0, cursorY, 6, seasonH, null,
                    seasonProgress.HonorNewlyUnlocked ? SeasonEconomyUiArt.GoldHighlight : CoreLoopUiArt.Jade);
                float sealTextX = 16f;
                float sealTextW = 610f;
                float sealTrackW = 600f;
                if (seasonProgress.HonorNewlyUnlocked)
                {
                    SeasonV1UiArt.CreateFixedIcon(body, "SeasonHonorUnlockBadge", 14f, cursorY + 5f, 48f, 48f,
                        SeasonV1UiArt.LoadHonorBadgeIcon(seasonProgress.SeasonId, seasonProgress.HonorBadgeIconKey),
                        SeasonEconomyUiArt.GoldHighlight);
                    sealTextX = 68f;
                    sealTextW = 558f;
                    sealTrackW = 548f;
                }
                string sealLine = gained
                    ? $"{seasonProgress.SeasonName} · 赛季印进度 +{seasonProgress.PointsAdded}（{seasonProgress.ProgressAfter}/{seasonProgress.MaxPoints}）"
                    : $"{seasonProgress.SeasonName} · 本季赛季印已满（{seasonProgress.ProgressAfter}/{seasonProgress.MaxPoints}），可随时免费切印";
                var sealLbl = CoreLoopUiArt.Text("SeasonSealSettlementLine", body, sealTextX, cursorY + 3f, sealTextW, 22,
                    sealLine, 17, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineLeft);
                sealLbl.enableAutoSizing = true; sealLbl.fontSizeMin = 12f; sealLbl.fontSizeMax = 17f;

                float sealRatio = SeasonEconomyUiArt.ComputeProgressRatio(seasonProgress.ProgressAfter, seasonProgress.MaxPoints);
                CoreLoopUiArt.Image("SeasonSealTrack", body, sealTextX, cursorY + 28f, sealTrackW, 7, null, new Color(.74f, .80f, .72f, .92f));
                if (sealRatio > 0f)
                    CoreLoopUiArt.Image("SeasonSealFill", body, sealTextX, cursorY + 28f, sealTrackW * sealRatio, 7, null, CoreLoopUiArt.Jade);

                if (seasonProgress.HonorNewlyUnlocked)
                {
                    string titleTag = string.IsNullOrWhiteSpace(seasonProgress.HonorTitleName)
                        ? "已永久解锁本季零数值纪念徽章与称谓"
                        : $"已永久解锁零数值纪念称谓「{seasonProgress.HonorTitleName}」";
                    CoreLoopUiArt.Text("SeasonHonorUnlockLine", body, sealTextX, cursorY + 37f, sealTextW, 19,
                        titleTag, 15, SeasonEconomyUiArt.GoldHighlight, true, false, TextAlignmentOptions.MidlineLeft);
                }

                CoreLoopUiArt.Button("OpenSeasonSealFromSettlement", body, 636, cursorY + (seasonH - 32f) * 0.5f, 168, 32,
                    "查看赛季印", _root.OpenSeasonSeal, true, false, 16);
                cursorY += seasonH + 4f;
            }

            if (hasHighlights)
            {
                var sb = new System.Text.StringBuilder("本场战法高光：");
                int count = Mathf.Min(5, highlights.Count);
                for (int i = 0; i < count; ++i)
                {
                    if (i > 0) sb.Append(" · ");
                    sb.Append(highlights[i].Title);
                }
                var highlightSprite = CoreLoopUiArt.LoadV2("highlight_strip_gold");
                var highlightFallback = new Color(.98f, .93f, .76f, .95f);
                if (highlightSprite != null)
                    SeasonEconomyUiArt.CreateSlicedOrSimple(body, "BossTrialHighlightBack", 0, cursorY, 820, 36,
                        highlightSprite, highlightFallback);
                else
                    CoreLoopUiArt.Image("BossTrialHighlightBack", body, 0, cursorY, 820, 36, "hud-strip", highlightFallback);
                float highlightPad = highlightSprite != null ? 38f : 14f;
                Color highlightInk = highlightSprite != null ? CoreLoopUiArt.Ink : SeasonEconomyUiArt.GoldHighlight;
                var hlText = CoreLoopUiArt.Text("BossTrialHighlightText", body, highlightPad, cursorY + 2f, 820f - highlightPad * 2f, 32,
                    sb.ToString(), 17, highlightInk, true, false, TextAlignmentOptions.MidlineLeft);
                hlText.enableAutoSizing = true;
                hlText.fontSizeMin = 13f;
                hlText.fontSizeMax = 17f;
                cursorY += 40f;
            }

            if (hasReview)
            {
                float reviewY = cursorY;
                Color reviewBg = review.IsVictory
                    ? new Color(.91f, .96f, .92f, .96f)
                    : new Color(.97f, .93f, .86f, .96f);
                Color reviewAccent = review.IsVictory ? CoreLoopUiArt.DeepJade : CoreLoopUiArt.Warning;
                var reviewSprite = CoreLoopUiArt.LoadV2(review.IsVictory ? "review_strip_win" : "review_strip_loss");
                if (reviewSprite != null)
                    SeasonEconomyUiArt.CreateSlicedOrSimple(body, "TacticalReviewBack", 0, reviewY, 820, 52, reviewSprite, reviewBg);
                else
                {
                    CoreLoopUiArt.Image("TacticalReviewBack", body, 0, reviewY, 820, 52, "hud-strip", reviewBg);
                    CoreLoopUiArt.Image("TacticalReviewAccent", body, 0, reviewY, 6, 52, null, reviewAccent);
                }

                // 避让九宫格左右金色云纹端帽（约 30px）与上下金框（约 6px）
                float reviewTextX = reviewSprite != null ? 34f : 14f;
                var reviewIconSprite = CoreLoopUiArt.LoadV2("icon_tactical_review");
                if (reviewIconSprite != null)
                {
                    float iconX = reviewSprite != null ? 32f : 14f;
                    var reviewIcon = QdaoUguiFactory.CreateImage("TacticalReviewIcon", body, iconX, reviewY + 10, 32, 32,
                        reviewIconSprite, false);
                    reviewIcon.preserveAspect = true;
                    reviewTextX = iconX + 38f;
                }
                float reviewTextW = (reviewSprite != null ? 786f : 810f) - reviewTextX;
                string reasonLine = string.IsNullOrEmpty(review.ModeTag)
                    ? $"胜负复盘：{review.ReasonHeadline}"
                    : $"[{review.ModeTag}] 复盘：{review.ReasonHeadline}";
                var reasonText = CoreLoopUiArt.Text("TacticalReviewReason", body, reviewTextX, reviewY + 6, reviewTextW, 20,
                    reasonLine, 16, reviewAccent, true, false, TextAlignmentOptions.MidlineLeft);
                reasonText.enableAutoSizing = true;
                reasonText.fontSizeMin = 12f;
                reasonText.fontSizeMax = 16f;
                var nextText = CoreLoopUiArt.Text("TacticalReviewNext", body, reviewTextX, reviewY + 26, reviewTextW, 20,
                    "下一手建议：" + review.NextMoveTip, 15, CoreLoopUiArt.Ink, false, false, TextAlignmentOptions.MidlineLeft);
                nextText.enableAutoSizing = true;
                nextText.fontSizeMin = 11f;
                nextText.fontSizeMax = 15f;
            }
        }

        private static void DrawAmount(UnityEngine.Transform body, string key, float x, string title, uint amount,
            Color? textColor = null, Sprite iconOverride = null)
        {
            if (iconOverride != null)
            {
                CoreLoopUiArt.Image(key + "Plate", body, x, 98, 164, 42, null, new Color(0.95f, 0.90f, 0.74f, 0.92f));
                var icon = QdaoUguiFactory.CreateImage(key + "Icon", body, x + 4, 100, 38, 38, iconOverride, false);
                icon.preserveAspect = true;
                CoreLoopUiArt.Text(key, body, x + 46, 101, 114, 36, title + " +" + amount, 20,
                    textColor ?? CoreLoopUiArt.Jade, true, false, TextAlignmentOptions.Center);
                return;
            }
            CoreLoopUiArt.Badge(body, key, x, 98, 164,
                title + " +" + amount, textColor ?? CoreLoopUiArt.Jade);
        }
    }
}
