using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>可嵌入既有结算窗口的材料拆解块；全部数值取自核心循环只读视图。</summary>
    public sealed class CoreSettlementRewardSection : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _core = core;
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
            float scale = Mathf.Min(width / 820f, height / 180f);
            float offsetX = (width - 820f * scale) * .5f;
            float offsetY = (height - 180f * scale) * .5f;
            var body = CoreLoopUiArt.Rect("RewardBody", transform, offsetX, offsetY, 820, 180);
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
                ? $"周精进绑定 {view.EarnedThisWeek}/{view.WeeklyLimit}"
                : $"永久首套 {view.LifetimeEarned}/{view.LifetimeLimit} · 可交易 {view.TradableEarnedTotal}/{view.TradableLifetimeLimit}";
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
