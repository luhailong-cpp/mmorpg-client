using System;
using System.Collections.Generic;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using Trade;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 《五行奇谈》经济防骗、卖家信用、近 7 日 p50 与官方 7:3 直售商城的道家 Q 版 uGUI 子视图。
    /// 全部切片取自 Assets/Resources/UI/Ugui/EconomyTradeV1/（无烘焙动态文字，不依赖 Hover）。
    /// </summary>
    internal static class EconomyTradeUiViews
    {
        private static readonly Color IvoryScrim = new Color(1.00f, 0.96f, 0.87f, 0.92f);
        private static readonly Color JadeTint = new Color(0.14f, 0.30f, 0.24f, 1f);
        private static readonly Color AmberAlertTint = new Color(0.99f, 0.93f, 0.78f, 1f);
        private static readonly Color CinnabarAlertTint = new Color(0.99f, 0.84f, 0.80f, 1f);
        private static readonly Color CinnabarText = QdaoUguiTheme.Html("#8E2B1D");
        private static readonly Color AmberText = QdaoUguiTheme.Html("#6B460B");

        /// <summary>在容器背景安全叠加固定比例无字面板艺术图（不拉伸变形），优先 V1 切片并兼容 EconomyTradeV2 切片。</summary>
        public static void DrawPanelBackdrop(RectTransform parent, string sliceName, float alpha = 0.26f)
        {
            var sprite = SeasonEconomyUiArt.LoadTradeSlice(sliceName) ?? SeasonEconomyUiArt.LoadTradeV2Slice(sliceName);
            if (sprite == null) return;
            var img = QdaoUguiFactory.CreateImage("Backdrop_" + sliceName, parent, 0f, 0f, parent.rect.width > 10f ? parent.rect.width : 2120f,
                parent.rect.height > 10f ? parent.rect.height : 660f, sprite, false);
            img.preserveAspect = true;
            img.type = Image.Type.Simple;
            img.color = new Color(1f, 1f, 1f, alpha);
        }

        private static RectTransform CreateCard(Transform parent, string name, float x, float y, float w, float h, Color? bg = null)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var img = rect.gameObject.AddComponent<Image>();
            img.color = bg ?? IvoryScrim;
            img.raycastTarget = false;
            return rect;
        }

        /// <summary>绘制横向三段拉伸警示横幅（diff_alert_banner.png）。</summary>
        public static RectTransform DrawAlertBanner(
            Transform parent,
            string name,
            float x,
            float y,
            float w,
            float h,
            string text,
            bool highDanger)
        {
            var holder = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var bannerSprite = SeasonEconomyUiArt.LoadTradeSlice("diff_alert_banner");
            var img = SeasonEconomyUiArt.CreateSlicedOrSimple(holder, "BannerArt", 0f, 0f, w, h,
                bannerSprite, highDanger ? CinnabarAlertTint : AmberAlertTint);
            if (bannerSprite != null)
            {
                img.color = highDanger ? new Color(1f, 0.88f, 0.86f, 1f) : Color.white;
            }
            var lbl = GameplayUiArt.Text(holder, text, 42f, 6f, w - 84f, h - 12f, 23f,
                highDanger ? CinnabarText : AmberText, wrap: true);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            return holder;
        }

        /// <summary>官方 7:3 小额直售商城与「仙途轻享卡」四页签视图（严禁第三方扫码/微信/支付宝图标）。</summary>
        public static void RenderOfficialShop(
            RectTransform body,
            int activeSubTab,
            IReadOnlyList<OfficialSkuView> skus,
            Action<int> onSelectSubTab,
            Action<OfficialSkuView> onBuySku,
            Action onOpenLightCard,
            Action<Button> registerWriteButton)
        {
            DrawPanelBackdrop(body, "panel_07_official_shop", 0.22f);

            // 顶部四页签：仙途轻享卡 / 永久衣柜 / 匠心配套 / 限量追赶包
            for (int i = 0; i < OfficialShopCatalogModel.TabTitles.Length; i++)
            {
                int idx = i;
                bool selected = idx == activeSubTab;
                GameplayUiArt.Button(body, OfficialShopCatalogModel.TabTitles[idx],
                    24f + idx * 330f, 8f, 314f, 52f, () => onSelectSubTab(idx),
                    primary: selected, fontSize: 23f);
            }
            GameplayUiArt.Text(body, "官方 7:3 小额直售 · " + OfficialShopCatalogModel.PlatformPurchaseComplianceNote(PaymentClient.Platform),
                1360f, 8f, 740f, 54f, 19f, GameplayUiArt.Muted, wrap: true);

            // 左侧主推卡：仙途轻享卡（联动实时 AfkPassClient）
            var left = CreateCard(body, "LightCardHero", 24f, 74f, 700f, 504f);
            GameplayUiArt.Text(left, "仙途轻享卡  ·  ¥18 / 30 天", 24f, 18f, 650f, 44f, 29f, JadeTint);
            var afk = AfkPassUiRoot.Instance?.Client;
            string liveState = afk == null
                ? "状态：进入主城后可实时查询月卡剩余时长"
                : (afk.Active ? "当前已开通：" + afk.RemainingText : "当前未开通（点击下方按钮可查看或开通）");
            GameplayUiArt.Text(left, liveState, 24f, 70f, 650f, 40f, 22f, GameplayUiArt.Gold);
            GameplayUiArt.Text(left,
                "• 零氪同样 100% 免费 +10 毕业，不售数值毕业资格\n"
                + "• 月卡解锁市场可交易产出与轻度离线便利\n"
                + "• 官方小额直售仅提供外观、匠心配套与限量绑定追赶\n"
                + "• 严守平台合规：商城不展示扫码或第三方支付图标",
                24f, 116f, 650f, 184f, 21f, GameplayUiArt.Ink, wrap: true);

            var talisman = SeasonEconomyUiArt.LoadTradeSlice("icon_unseal_talisman");
            var inheritTalisman = SeasonEconomyUiArt.LoadTradeV2Slice("icon_inherit_talisman");
            if (talisman != null)
            {
                var icon = QdaoUguiFactory.CreateImage("ShopTalismanPreview", left, 24f, 308f, 88f, 88f, talisman, false);
                icon.preserveAspect = true;
            }
            if (inheritTalisman != null)
            {
                var icon2 = QdaoUguiFactory.CreateImage("ShopInheritPreview", left, 120f, 308f, 88f, 88f, inheritTalisman, false);
                icon2.preserveAspect = true;
            }
            GameplayUiArt.Text(left, "匠心配套亮点：解封剥离符 / 流派副套继承符\n解封仅恢复纯 T 原胚不洗白 B 料；继承无损转移高强至副套并转绑定。",
                (talisman != null || inheritTalisman != null) ? 218f : 24f, 312f,
                (talisman != null || inheritTalisman != null) ? 458f : 650f, 92f, 19f, GameplayUiArt.Muted, wrap: true);
            GameplayUiArt.Button(left, "管理 / 购买仙途轻享卡", 24f, 434f, 380f, 48f, onOpenLightCard, primary: true, fontSize: 22f);

            // 右侧官方在售商品列表（按子页签筛选；若该分栏暂无专属 SKU 则展示全部在售并标注所属分栏，绝不隐藏真实服务端商品）
            var right = CreateCard(body, "OfficialSkuPanel", 748f, 74f, 1348f, 504f);
            var filtered = new List<OfficialSkuView>();
            if (skus != null)
            {
                for (int i = 0; i < skus.Count; i++)
                {
                    if ((int)OfficialShopCatalogModel.ClassifySku(skus[i]) == activeSubTab)
                        filtered.Add(skus[i]);
                }
            }
            bool showingAllFallback = filtered.Count == 0 && skus != null && skus.Count > 0;
            var displayList = showingAllFallback ? skus : filtered;
            string heading = showingAllFallback
                ? $"当前分栏「{OfficialShopCatalogModel.TabTitles[activeSubTab]}」暂无独立上架项，为您展示全部官方直售商品："
                : $"官方直售 · {OfficialShopCatalogModel.TabTitles[activeSubTab]}";
            GameplayUiArt.Text(right, heading, 24f, 14f, 1300f, 40f, 22f, JadeTint);

            if (displayList == null || displayList.Count == 0)
            {
                GameplayUiArt.Text(right, "当前没有可售官方商品。请稍后点击下方「刷新」向服务端重新同步。", 24f, 72f, 1280f, 70f, 24f, GameplayUiArt.Muted, wrap: true);
                return;
            }

            int maxRows = Math.Min(displayList.Count, 6);
            for (int i = 0; i < maxRows; i++)
            {
                var sku = displayList[i].Clone();
                float y = 62f + i * 70f;
                string catName = OfficialShopCatalogModel.TabTitles[(int)OfficialShopCatalogModel.ClassifySku(sku)];
                string line = $"[{catName}] {sku.Title} · {EconomyFormat.FormatMoney(sku.PriceMinor, sku.Currency)} · 可售 {EconomyFormat.FormatInteger(sku.Available)}";
                GameplayUiArt.Text(right, line, 24f, y, 960f, 54f, 23f);
                var buyBtn = GameplayUiArt.Button(right, "在游戏内购买", 1010f, y, 310f, 52f, () => onBuySku(sku), primary: true, fontSize: 22f);
                registerWriteButton?.Invoke(buyBtn);
            }
        }

        /// <summary>卖家店铺信用档案与高危状态单显（官方过户换人 vs 异地陌生新设备清 0）。</summary>
        public static void RenderSellerCreditProfile(
            RectTransform body,
            SellerCreditView profile,
            Action onRefresh,
            Action onBack)
        {
            var model = SellerCreditDisplayModel.FromProto(profile);
            DrawPanelBackdrop(body, model.AlertMode != SellerCreditAlertMode.None ? "panel_04_credit_alerts" : "panel_03_credit_review", 0.25f);

            var card = CreateCard(body, "SellerCreditCard", 24f, 12f, 2072f, 560f);
            GameplayUiArt.Text(card, $"卖家信用档案 · 服务端角色编号 #{model.SellerPlayerId}", 28f, 18f, 1200f, 48f, 30f, JadeTint);

            // 信用档位玉牌徽标
            var tierSprite = SeasonEconomyUiArt.LoadTradeSlice(model.TierBadgeKey);
            if (tierSprite != null)
            {
                var badge = QdaoUguiFactory.CreateImage("TierBadge", card, 28f, 82f, 136f, 136f, tierSprite, false);
                badge.preserveAspect = true;
            }

            // 5 枚暖金玉星
            for (int i = 0; i < model.StarSliceKeys.Length; i++)
            {
                var starSprite = SeasonEconomyUiArt.LoadTradeSlice(model.StarSliceKeys[i]);
                if (starSprite != null)
                {
                    var starImg = QdaoUguiFactory.CreateImage("CreditStar_" + i, card, 188f + i * 62f, 92f, 54f, 54f, starSprite, false);
                    starImg.preserveAspect = true;
                }
            }
            GameplayUiArt.Text(card, $"{model.StarText}  /  {model.ScoreText}  ·  {model.TierLabel}",
                512f, 94f, 920f, 50f, 28f, model.AlertMode == SellerCreditAlertMode.RemoteNewDevice ? CinnabarText : JadeTint);

            ulong completed = profile?.CompletedOrders ?? 0UL;
            ulong violations = profile?.ViolationCount ?? 0UL;
            GameplayUiArt.Text(card,
                $"官方托管成交数：{EconomyFormat.FormatInteger(completed)} 笔    好评率：{model.PositiveRateText}\n"
                + $"好评 / 中评 / 差评：{model.ReviewCountsText}    核实违约次数：{EconomyFormat.FormatInteger(violations)} 次\n"
                + $"当前持有者起算日：{model.HolderSinceText}（过户修订号 #{profile?.OwnerRevision ?? 0}）",
                188f, 156f, 1820f, 124f, 24f, GameplayUiArt.Ink, wrap: true);

            // 运行时按实际状态单显高危警示横幅（绝不同时并排展示伪状态）
            if (model.AlertMode != SellerCreditAlertMode.None)
            {
                bool remoteClear = model.AlertMode == SellerCreditAlertMode.RemoteNewDevice;
                DrawAlertBanner(card, "SellerCreditRiskBanner", 28f, 294f, 2016f, 82f, model.AlertBannerText, highDanger: remoteClear);
                GameplayUiArt.Text(card, model.AlertDetailText, 36f, 388f, 1996f, 108f, 22f,
                    remoteClear ? CinnabarText : AmberText, wrap: true);
            }
            else
            {
                GameplayUiArt.Text(card,
                    "当前店铺无高危换人或异地换机清零记录。\n"
                    + "说明：官方过户换人会清空历史信用重算；私下卖号/外地借号/盗号异地陌生新设备登录会直接清 0 正面信用并永久保留差评/违约黑历史。\n"
                    + "坐高铁或切换 5G 仅换 IP 不扣分；换新机建议先连接常用 Wi-Fi 后再登录。",
                    28f, 304f, 2000f, 160f, 22f, GameplayUiArt.Muted, wrap: true);
            }

            GameplayUiArt.Text(card, model.AntiWashNote, 28f, 508f, 1480f, 40f, 20f, GameplayUiArt.Muted);
            GameplayUiArt.Button(body, "刷新信用", 24f, 592f, 260f, 54f, onRefresh, fontSize: 24f);
            GameplayUiArt.Button(body, "返回", 310f, 592f, 240f, 54f, onBack, fontSize: 24f);
        }

        /// <summary>官方托管成交后 7 日内评价卖家弹窗（好评/中评/差评 + 防刷分说明）。</summary>
        public static void RenderSellerReviewDialog(
            RectTransform body,
            CommerceOrderView order,
            SellerReviewRating selectedRating,
            string currentComment,
            Action<SellerReviewRating> onSelectRating,
            Action<SellerReviewRating, string> onSubmit,
            Action onBack,
            Action<Button> registerWriteButton)
        {
            DrawPanelBackdrop(body, "panel_03_credit_review", 0.24f);
            var card = CreateCard(body, "SellerReviewCard", 24f, 14f, 2072f, 560f);

            GameplayUiArt.Text(card, $"托管成交评价 · 订单 {order.OrderId}", 28f, 18f, 1600f, 46f, 30f, JadeTint);
            GameplayUiArt.Text(card,
                $"卖家角色编号 #{order.SellerPlayerId} · 成交金额 {EconomyFormat.FormatMoney(order.Amount, order.Currency)} · 官方托管成交后 7 日内可评价",
                28f, 70f, 1980f, 42f, 23f, GameplayUiArt.Muted);

            GameplayUiArt.Button(card, selectedRating == SellerReviewRating.Positive ? "✓ 好评（推荐）" : "好评",
                28f, 134f, 320f, 64f, () => onSelectRating(SellerReviewRating.Positive),
                primary: selectedRating == SellerReviewRating.Positive, fontSize: 25f);
            GameplayUiArt.Button(card, selectedRating == SellerReviewRating.Neutral ? "✓ 中评" : "中评",
                378f, 134f, 300f, 64f, () => onSelectRating(SellerReviewRating.Neutral),
                primary: selectedRating == SellerReviewRating.Neutral, fontSize: 25f);
            GameplayUiArt.Button(card, selectedRating == SellerReviewRating.Negative ? "✓ 差评" : "差评",
                708f, 134f, 300f, 64f, () => onSelectRating(SellerReviewRating.Negative),
                primary: selectedRating == SellerReviewRating.Negative, fontSize: 25f);

            GameplayUiArt.Text(card, "短评（选填，最多 200 字，描述本次真实交易体验）：", 28f, 226f, 1200f, 38f, 22f);
            var commentInput = QdaoUguiFactory.CreateInputField("ReviewComment", card, 28f, 270f, 1400f, 68f, "填写短评…", 200);
            commentInput.SetTextWithoutNotify(currentComment ?? string.Empty);
            commentInput.textComponent.richText = false;

            GameplayUiArt.Text(card,
                "防刷分规则：同账号 / 同网络 / 同设备及极小额交易不计入卖家信用分；同买家对同卖家 7 日内仅计 1 次正面评分。",
                28f, 360f, 1980f, 56f, 21f, GameplayUiArt.Muted, wrap: true);

            var submitBtn = GameplayUiArt.Button(card, "提交评价", 28f, 444f, 360f, 62f,
                () => onSubmit(selectedRating, commentInput.text ?? string.Empty), primary: true, fontSize: 25f);
            registerWriteButton?.Invoke(submitBtn);
            GameplayUiArt.Button(card, "返回订单", 420f, 444f, 280f, 62f, onBack, fontSize: 24f);
        }

        /// <summary>材料市场近 7 日真实成交 p50 中位参考价条（不足 10 笔高亮隐藏）。</summary>
        public static void RenderMarketP50Strip(
            RectTransform body,
            GetMarketReferencePriceResponse p50,
            Action onRefreshP50)
        {
            var model = MarketP50DisplayModel.FromProto(p50);
            if (model.HideReferenceCurve)
            {
                DrawAlertBanner(body, "MarketP50GuardBanner", 24f, 4f, 1740f, 76f,
                    $"{model.Headline}（{model.SampleText}）· {model.SubNotice}", highDanger: false);
            }
            else
            {
                var card = CreateCard(body, "MarketP50Card", 24f, 4f, 1740f, 76f);
                GameplayUiArt.Text(card, $"{model.Headline}    |    {model.SampleText}", 20f, 6f, 1690f, 34f, 23f, JadeTint);
                GameplayUiArt.Text(card, model.SubNotice, 20f, 40f, 1690f, 30f, 19f, GameplayUiArt.Muted);
            }
            GameplayUiArt.Button(body, "刷新 p50", 1784f, 14f, 290f, 54f, onRefreshP50, fontSize: 22f);
        }

        /// <summary>集市心愿单与到价飞鸽提醒条（使用 EconomyTradeV2 icon_wishlist_dove，仅在 p50 样本充足且 ≤ 心愿价时高亮提醒）。</summary>
        public static void RenderMarketWishlistStrip(
            RectTransform body,
            string itemTitle,
            uint currencyType,
            MarketWishlistAlert alert,
            Action<ulong> onSetTarget,
            Action onClearTarget)
        {
            var cardBg = alert.BelowOrAtTarget ? new Color(0.84f, 0.91f, 0.85f, 0.94f) : IvoryScrim;
            var card = CreateCard(body, "MarketWishlistCard", 24f, 344f, 2050f, 68f, cardBg);
            var doveSprite = SeasonEconomyUiArt.LoadTradeV2Slice("icon_wishlist_dove");
            float leftX = 16f;
            if (doveSprite != null)
            {
                var doveImg = QdaoUguiFactory.CreateImage("WishlistDoveIcon", card, 14f, 6f, 56f, 56f, doveSprite, false);
                doveImg.preserveAspect = true;
                leftX = 80f;
            }
            string statusLine;
            if (alert.BelowOrAtTarget)
                statusLine = $"【已跌破心愿价】{itemTitle} · 心愿目标价 {EconomyFormat.FormatCurrency(alert.TargetPrice, currencyType)} · 当前 p50 {EconomyFormat.FormatCurrency(alert.CurrentP50, currencyType)}";
            else if (alert.HasTarget)
                statusLine = $"集市心愿单：{itemTitle} 目标价 {EconomyFormat.FormatCurrency(alert.TargetPrice, currencyType)}"
                    + (alert.HasValidP50 ? $"（当前 p50 {EconomyFormat.FormatCurrency(alert.CurrentP50, currencyType)}）" : "（样本满 10 笔后自动比对到价）");
            else
                statusLine = "集市心愿单与到价飞鸽提醒：设置心愿目标价，当服务端近 7 日 p50 跌破或等于目标价时立即提醒。";

            GameplayUiArt.Text(card, statusLine, leftX, 14f, 1180f - leftX, 42f, 21f,
                alert.BelowOrAtTarget ? JadeTint : GameplayUiArt.Ink);

            var field = QdaoUguiFactory.CreateInputField("WishlistTargetInput", card, 1210f, 10f, 330f, 48f, "心愿目标价(银两)", 16);
            field.textComponent.richText = false;
            if (alert.HasTarget) field.SetTextWithoutNotify(alert.TargetPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
            GameplayUiArt.Button(card, "设心愿价", 1560f, 10f, 230f, 48f, () =>
            {
                if (ulong.TryParse((field.text ?? string.Empty).Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong price) && price > 0UL)
                    onSetTarget?.Invoke(price);
            }, primary: true, fontSize: 21f);
            var clearBtn = GameplayUiArt.Button(card, "清除", 1810f, 10f, 210f, 48f, () => onClearTarget?.Invoke(), fontSize: 21f);
            clearBtn.interactable = alert.HasTarget;
        }

        /// <summary>游戏内当面交易防骗主窗 + 条款变更强确认态（左右双列「我付出 / 我得到」+ 额度条 + 差异横幅 + 两步锁定确认）。</summary>
        public static void RenderFaceToFaceOrder(
            RectTransform body,
            CommerceOrderResponse response,
            ulong viewerPlayerId,
            GetPlayerTradeQuotaResponse quota,
            FaceToFaceTradeDiffResult diff,
            bool localTermsLocked,
            bool serverAllowsConfirm,
            Action onLockTerms,
            Action onFinalConfirm,
            Action onRefresh,
            Action onCancel,
            Action onViewCounterpartyCredit,
            Action onBackToOrders,
            Action<Button> registerWriteButton,
            out Button cancelButton)
        {
            var order = response.Order;
            bool isSeller = viewerPlayerId != 0UL && order.SellerPlayerId == viewerPlayerId;
            ulong counterpartyId = isSeller ? order.BuyerPlayerId : order.SellerPlayerId;

            DrawPanelBackdrop(body, diff.HasChanges ? "panel_02_diff_confirm" : "panel_01_trade_guard", 0.22f);

            // 顶部信息栏：对方服务端权威角色编号 + 好友不代表官方担保 + 今日剩余额度
            var header = CreateCard(body, "TradeGuardHeader", 24f, 4f, 2072f, 88f);
            GameplayUiArt.Text(header,
                $"当面交易 · 订单 {order.OrderId}（v{order.Version}）  |  对方服务端角色编号 #{counterpartyId}（好友不代表官方担保）",
                20f, 8f, 1700f, 36f, 23f, JadeTint);
            GameplayUiArt.Text(header, FaceToFaceTradeDiffModel.FormatQuotaSummary(quota),
                20f, 46f, 1700f, 34f, 21f, GameplayUiArt.Muted);
            if (counterpartyId != 0UL)
                GameplayUiArt.Button(header, "查对方信用", 1760f, 18f, 290f, 52f, onViewCounterpartyCredit, fontSize: 22f);

            float columnsTop = 104f;
            if (diff.HasChanges)
            {
                string diffSummary = diff.BannerText + "\n" + string.Join("；", diff.DiffLines);
                DrawAlertBanner(body, "FaceToFaceDiffBanner", 24f, 100f, 2072f, 96f, diffSummary, highDanger: true);
                columnsTop = 206f;
            }
            else
            {
                GameplayUiArt.Text(body, "防骗提示：任一方修改条款会清空双方锁定；请核对千分位银两、强化等级与唯一实例号后再锁定并确认。",
                    24f, 98f, 2072f, 36f, 20f, GameplayUiArt.Muted);
                columnsTop = 140f;
            }

            // 左右双列：「我付出」vs「我得到」
            var myAssets = isSeller ? response.SellerAssets : response.BuyerAssets;
            var myItems = isSeller ? response.SellerItemInstances : response.BuyerItemInstances;
            var myPet = isSeller ? response.SellerPet : response.BuyerPet;
            var theirAssets = isSeller ? response.BuyerAssets : response.SellerAssets;
            var theirItems = isSeller ? response.BuyerItemInstances : response.SellerItemInstances;
            var theirPet = isSeller ? response.BuyerPet : response.SellerPet;
            bool myServerConfirmed = isSeller ? order.SellerConfirmed : order.BuyerConfirmed;
            bool theirServerConfirmed = isSeller ? order.BuyerConfirmed : order.SellerConfirmed;

            float colH = 442f - columnsTop;
            RenderTradeSideCard(body, "TradeGiveCard", 24f, columnsTop, 1020f, colH,
                "我付出", myAssets, myItems, myPet, myServerConfirmed, localTermsLocked);
            RenderTradeSideCard(body, "TradeReceiveCard", 1076f, columnsTop, 1020f, colH,
                "我得到", theirAssets, theirItems, theirPet, theirServerConfirmed, theirServerConfirmed);

            // 底部两步确认区：「锁定条款」与「最终确认」清晰分离
            var lockBtn = GameplayUiArt.Button(body,
                localTermsLocked ? "✓ 条款已核对锁定（点击重核）" : "① 锁定条款（核对无误）",
                24f, 456f, 450f, 56f, onLockTerms, primary: !localTermsLocked, enabled: serverAllowsConfirm, fontSize: 23f);
            if (!serverAllowsConfirm) lockBtn.interactable = false;

            var finalBtn = GameplayUiArt.Button(body,
                localTermsLocked ? "② 最终确认交易" : "② 请先锁定条款",
                500f, 456f, 400f, 56f, onFinalConfirm, primary: localTermsLocked, enabled: serverAllowsConfirm && localTermsLocked, fontSize: 23f);
            if (serverAllowsConfirm && localTermsLocked) registerWriteButton?.Invoke(finalBtn);

            GameplayUiArt.Button(body, "刷新核对", 930f, 456f, 260f, 56f, onRefresh, fontSize: 23f);
            cancelButton = null;
            if (order.State == CommerceOrderState.WaitingConfirmation || order.State == CommerceOrderState.AwaitingPayment)
            {
                cancelButton = GameplayUiArt.Button(body, "取消交易", 1216f, 456f, 260f, 56f, onCancel, fontSize: 23f);
                registerWriteButton?.Invoke(cancelButton);
            }
            GameplayUiArt.Button(body, "我的订单", 24f, 592f, 260f, 54f, onBackToOrders, fontSize: 24f);
        }

        private static void RenderTradeSideCard(
            RectTransform parent,
            string name,
            float x,
            float y,
            float w,
            float h,
            string title,
            TradeAssetSelection assets,
            IEnumerable<TradeOfferInstance> instances,
            TradeOfferInstance pet,
            bool serverConfirmed,
            bool localLocked)
        {
            var card = CreateCard(parent, name, x, y, w, h);
            string lockTag = serverConfirmed ? "服务端已确认" : (localLocked ? "已锁定条款" : "未锁定");
            GameplayUiArt.Text(card, $"{title}  ·  {lockTag}", 20f, 12f, w - 110f, 38f, 26f, JadeTint);

            var badgeT = SeasonEconomyUiArt.LoadTradeSlice("badge_item_tradable_t");
            if (badgeT != null)
            {
                var img = QdaoUguiFactory.CreateImage(name + "_BadgeT", card, w - 68f, 10f, 48f, 48f, badgeT, false);
                img.preserveAspect = true;
            }

            ulong silver = assets?.CurrencyAmount ?? 0UL;
            uint currType = assets?.CurrencyType ?? 0u;
            GameplayUiArt.Text(card, "货币金额：" + EconomyFormat.FormatCurrency(silver, currType),
                20f, 56f, w - 40f, 38f, 24f, silver > 0UL ? GameplayUiArt.Gold : GameplayUiArt.Muted);
            GameplayUiArt.Text(card, "物品与唯一实例：\n" + FaceToFaceTradeDiffModel.DescribeInstances(assets, instances, pet),
                20f, 100f, w - 40f, Math.Max(80f, h - 112f), 22f, GameplayUiArt.Ink, wrap: true);
        }

        private static readonly Color SelectedRowTint = new Color(0.84f, 0.91f, 0.85f, 0.92f);
        private static readonly Color TableHeaderTint = new Color(0.92f, 0.88f, 0.78f, 0.94f);

        /// <summary>珍宝阁(RMB)五分栏页签条（与 EconomyTradeV2 预览 01/02 对齐）。</summary>
        public static void RenderRmbSubTabs(
            RectTransform body,
            RmbConsignmentSubTab activeSubTab,
            Action<RmbConsignmentSubTab> onSelectSubTab)
        {
            float[] widths = { 360f, 340f, 400f, 380f, 440f };
            float x = 24f;
            for (int i = 0; i < RmbConsignmentViewModel.SubTabTitles.Length; i++)
            {
                var tab = (RmbConsignmentSubTab)i;
                GameplayUiArt.Button(body, RmbConsignmentViewModel.SubTabTitles[i],
                    x, 4f, widths[i], 50f, () => onSelectSubTab?.Invoke(tab),
                    primary: tab == activeSubTab, fontSize: 22f);
                x += widths[i] + 24f;
            }
        }

        /// <summary>珍宝阁(RMB)寄售浏览页（对齐 EconomyTradeV2 预览 01：panel_08 + 三种无字底签 + 卖家信用 + 选中行底部购买条）。</summary>
        public static void RenderRmbListings(
            RectTransform body,
            IReadOnlyList<RmbConsignmentView> listings,
            RmbConsignmentSubTab activeSubTab,
            ulong selectedListingId,
            ulong myPlayerId,
            IReadOnlyDictionary<ulong, SellerCreditView> sellerCredits,
            Action<RmbConsignmentSubTab> onSelectSubTab,
            Action<ulong> onSelectListing,
            Action<ulong> onViewSellerCredit,
            Action<RmbConsignmentView> onBuy,
            Action<RmbConsignmentView> onCancel,
            Action<Button> registerWriteButton)
        {
            DrawPanelBackdrop(body, "panel_08_zhenbaoge_rmb", 0.34f);
            RenderRmbSubTabs(body, activeSubTab, onSelectSubTab);

            var filtered = RmbConsignmentViewModel.FilterListings(listings, activeSubTab);
            var header = CreateCard(body, "RmbTableHeader", 24f, 64f, 2056f, 44f, TableHeaderTint);
            GameplayUiArt.Text(header, "寄售物品", 24f, 4f, 520f, 36f, 22f, JadeTint);
            GameplayUiArt.Text(header, "寄售价格", 560f, 4f, 240f, 36f, 22f, JadeTint);
            GameplayUiArt.Text(header, "公示规则", 820f, 4f, 520f, 36f, 22f, JadeTint);
            GameplayUiArt.Text(header, "卖家信用", 1380f, 4f, 640f, 36f, 22f, JadeTint);

            if (filtered.Count == 0)
            {
                string emptyHint = activeSubTab == RmbConsignmentSubTab.DesignatedOnly
                    ? "当前暂无指定买家专属的人民币托管寄售单。"
                    : (activeSubTab == RmbConsignmentSubTab.PublicNotice
                        ? "当前暂无处于公示期的人民币托管寄售单。"
                        : "当前暂无人民币官方托管寄售单；点击上方「上架寄售」可按当前所选资产上架。");
                GameplayUiArt.Text(body, emptyHint, 24f, 134f, 1980f, 60f, 24f, GameplayUiArt.Muted);
                return;
            }

            RmbConsignmentView selectedItem = null;
            int maxRows = Math.Min(filtered.Count, 5);
            for (int i = 0; i < maxRows; i++)
            {
                if (filtered[i].ListingId == selectedListingId) { selectedItem = filtered[i].Clone(); break; }
            }
            if (selectedItem == null) selectedItem = filtered[0].Clone();

            for (int i = 0; i < maxRows; i++)
            {
                var item = filtered[i].Clone();
                bool isSelected = item.ListingId == selectedItem.ListingId;
                float y = 114f + i * 74f;
                var row = CreateCard(body, $"RmbRow_{i}", 24f, y, 2056f, 68f, isSelected ? SelectedRowTint : IvoryScrim);
                var rowSelectBtn = row.gameObject.AddComponent<Button>();
                rowSelectBtn.transition = Selectable.Transition.None;
                row.GetComponent<Image>().raycastTarget = true;
                ulong lid = item.ListingId;
                rowSelectBtn.onClick.AddListener(() => onSelectListing?.Invoke(lid));

                string kindLabel = string.Equals(item.AssetKind, "PET", StringComparison.OrdinalIgnoreCase)
                    ? "宠物" : (string.Equals(item.AssetKind, "CHARACTER", StringComparison.OrdinalIgnoreCase) ? "角色" : "装备/道具");
                GameplayUiArt.Text(row, $"{(isSelected ? "✓ " : "")}{item.Title}", 20f, 6f, 520f, 32f, 23f, isSelected ? JadeTint : GameplayUiArt.Ink);
                GameplayUiArt.Text(row, $"#{item.ListingId} · {kindLabel} · {item.Phase}/{item.State}", 20f, 38f, 520f, 26f, 17f, GameplayUiArt.Muted);

                GameplayUiArt.Text(row, EconomyFormat.FormatMoney(item.PriceFen, "CNY"), 560f, 14f, 240f, 40f, 25f, JadeTint);

                bool uniqueValuable = RmbConsignmentViewModel.IsUniqueValuable(item);
                var noticeBadge = SeasonEconomyUiArt.LoadTradeV2Slice(uniqueValuable ? "badge_notice_48h" : "badge_notice_fast");
                if (noticeBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage($"RmbNoticeBadge_{i}", row, 820f, item.DesignatedOnly ? 2f : 12f, 240f, 38f, noticeBadge, false);
                    badgeImg.preserveAspect = true;
                    var badgeLbl = GameplayUiArt.Text(badgeImg.transform, uniqueValuable ? "48小时全服公示" : "0–2小时快速公示",
                        8f, 0f, 224f, 38f, 16f, uniqueValuable ? GameplayUiArt.Ink : GameplayUiArt.Cream);
                    badgeLbl.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    GameplayUiArt.Text(row, MarketP50DisplayModel.DescribeNoticeTier(uniqueValuable, item.PriceFen), 820f, 16f, 320f, 36f, 19f);
                }

                if (item.DesignatedOnly)
                {
                    var desigBadge = SeasonEconomyUiArt.LoadTradeV2Slice("badge_designated_only");
                    if (desigBadge != null)
                    {
                        var desigImg = QdaoUguiFactory.CreateImage($"RmbDesignatedBadge_{i}", row, 840f, 36f, 200f, 30f, desigBadge, false);
                        desigImg.preserveAspect = true;
                        var desigLbl = GameplayUiArt.Text(desigImg.transform, "指定买家专属", 6f, 0f, 188f, 30f, 15f, GameplayUiArt.Cream);
                        desigLbl.alignment = TextAlignmentOptions.Center;
                    }
                    else
                    {
                        GameplayUiArt.Text(row, "【指定买家专属】", 1070f, 16f, 240f, 36f, 19f, CinnabarText);
                    }
                }

                float creditX = 1380f;
                SellerCreditView cachedCredit = null;
                if (item.SellerPlayerId != 0UL && sellerCredits != null)
                    sellerCredits.TryGetValue(item.SellerPlayerId, out cachedCredit);
                if (cachedCredit != null)
                {
                    var cModel = SellerCreditDisplayModel.FromProto(cachedCredit);
                    var tierSprite = SeasonEconomyUiArt.LoadTradeSlice(cModel.TierBadgeKey);
                    if (tierSprite != null)
                    {
                        var tImg = QdaoUguiFactory.CreateImage($"RmbSellerTier_{i}", row, creditX, 12f, 44f, 44f, tierSprite, false);
                        tImg.preserveAspect = true;
                        creditX += 52f;
                    }
                    GameplayUiArt.Text(row, $"{cModel.StarText} · {cModel.TierLabel}", creditX, 16f, 340f, 36f, 21f, JadeTint);
                }
                else
                {
                    GameplayUiArt.Text(row, $"卖家 #{item.SellerPlayerId}", creditX, 16f, 340f, 36f, 21f, GameplayUiArt.Ink);
                }
                if (item.SellerPlayerId != 0UL)
                {
                    ulong sid = item.SellerPlayerId;
                    GameplayUiArt.Button(row, "查信用", 1830f, 10f, 200f, 48f, () => onViewSellerCredit?.Invoke(sid), fontSize: 20f);
                }
            }

            // 底部选中行确认购买 / 撤回条（与预览 01 底部操作条对齐）
            var footer = CreateCard(body, "RmbFooterBar", 24f, 492f, 2056f, 76f, TableHeaderTint);
            GameplayUiArt.Text(footer,
                $"已选：{selectedItem.Title}（#{selectedItem.ListingId}）    {EconomyFormat.FormatMoney(selectedItem.PriceFen, "CNY")}    · 资金与公示期由官方托管担保",
                24f, 16f, 1540f, 44f, 23f, JadeTint);
            if (selectedItem.SellerPlayerId == myPlayerId)
            {
                var cancelBtn = GameplayUiArt.Button(footer, "撤回上架", 1640f, 12f, 380f, 52f, () => onCancel?.Invoke(selectedItem), fontSize: 23f);
                registerWriteButton?.Invoke(cancelBtn);
            }
            else
            {
                var buyBtn = GameplayUiArt.Button(footer, "官方担保购买", 1640f, 12f, 380f, 52f, () => onBuy?.Invoke(selectedItem), primary: true, fontSize: 23f);
                registerWriteButton?.Invoke(buyBtn);
            }
        }

        /// <summary>卖家 RMB 托管收益与提现页（对齐 EconomyTradeV2 预览 02：三列汇总 + 朱砂争议冻结横幅 + 订单明细表 + 提现栏）。</summary>
        public static void RenderRmbSellerIncomes(
            RectTransform body,
            IReadOnlyList<RmbSellerIncome> incomes,
            Action<RmbConsignmentSubTab> onSelectSubTab,
            Action<ulong> onWithdrawFen,
            Action<Button> registerWriteButton)
        {
            DrawPanelBackdrop(body, "panel_08_zhenbaoge_rmb", 0.30f);
            RenderRmbSubTabs(body, RmbConsignmentSubTab.Income, onSelectSubTab);

            var summary = RmbConsignmentViewModel.SummarizeIncomes(incomes);
            var sumCard = CreateCard(body, "RmbIncomeSummary", 24f, 64f, 2056f, 84f, IvoryScrim);
            var col0 = CreateCard(sumCard, "RmbSumWithdrawable", 0f, 0f, 685f, 84f, SelectedRowTint);
            GameplayUiArt.Text(col0, "可提现余额", 24f, 8f, 637f, 28f, 20f, JadeTint).alignment = TextAlignmentOptions.Center;
            GameplayUiArt.Text(col0, EconomyFormat.FormatMoney(summary.WithdrawableFen, "CNY"), 24f, 38f, 637f, 40f, 28f, JadeTint).alignment = TextAlignmentOptions.Center;

            GameplayUiArt.Text(sumCard, "结算期预留", 695f, 8f, 665f, 28f, 20f, GameplayUiArt.Ink).alignment = TextAlignmentOptions.Center;
            GameplayUiArt.Text(sumCard, EconomyFormat.FormatMoney(summary.ReservedFen, "CNY"), 695f, 38f, 665f, 40f, 28f, GameplayUiArt.Ink).alignment = TextAlignmentOptions.Center;

            GameplayUiArt.Text(sumCard, "争议冻结", 1370f, 8f, 665f, 28f, 20f, summary.DisputeFrozenFen > 0UL ? CinnabarText : GameplayUiArt.Muted).alignment = TextAlignmentOptions.Center;
            GameplayUiArt.Text(sumCard, EconomyFormat.FormatMoney(summary.DisputeFrozenFen, "CNY"), 1370f, 38f, 665f, 40f, 28f, summary.DisputeFrozenFen > 0UL ? CinnabarText : GameplayUiArt.Muted).alignment = TextAlignmentOptions.Center;

            float tableTop = 158f;
            if (summary.DisputeFrozenFen > 0UL)
            {
                string casePart = string.IsNullOrEmpty(summary.FirstDisputeCaseId) ? "" : $"（工单 #{summary.FirstDisputeCaseId}）";
                DrawAlertBanner(body, "RmbDisputeFrozenBanner", 24f, 156f, 2056f, 66f,
                    $"存在核查争议冻结金额：{EconomyFormat.FormatMoney(summary.DisputeFrozenFen, "CNY")}{casePart}，争议结案前该部分资金自动锁止提现",
                    highDanger: true);
                tableTop = 230f;
            }

            var hdr = CreateCard(body, "RmbIncomeHeader", 24f, tableTop, 2056f, 40f, TableHeaderTint);
            GameplayUiArt.Text(hdr, "订单编号", 24f, 4f, 520f, 32f, 20f, JadeTint);
            GameplayUiArt.Text(hdr, "净收益", 580f, 4f, 340f, 32f, 20f, JadeTint);
            GameplayUiArt.Text(hdr, "已提现 / 预留 / 冻结", 940f, 4f, 680f, 32f, 20f, JadeTint);
            GameplayUiArt.Text(hdr, "结算状态", 1680f, 4f, 340f, 32f, 20f, JadeTint);

            float rowsTop = tableTop + 44f;
            if (incomes == null || incomes.Count == 0)
            {
                GameplayUiArt.Text(body, "暂无 RMB 托管收益记录。", 24f, rowsTop + 12f, 1600f, 46f, 23f, GameplayUiArt.Muted);
            }
            else
            {
                int maxRows = Math.Min(incomes.Count, summary.DisputeFrozenFen > 0UL ? 3 : 4);
                for (int i = 0; i < maxRows; i++)
                {
                    var inc = incomes[i];
                    var row = CreateCard(body, $"RmbIncomeRow_{i}", 24f, rowsTop + i * 52f, 2056f, 48f, IvoryScrim);
                    string statusText = RmbConsignmentViewModel.FormatIncomeRowStatus(inc);
                    Color statusColor = inc.DisputeFrozenFen > 0UL ? CinnabarText : (statusText == "可提现" ? JadeTint : GameplayUiArt.Ink);
                    GameplayUiArt.Text(row, inc.OrderId, 24f, 8f, 520f, 34f, 21f, GameplayUiArt.Ink);
                    GameplayUiArt.Text(row, EconomyFormat.FormatMoney(inc.NetFen, "CNY"), 580f, 8f, 340f, 34f, 21f, JadeTint);
                    string detail = $"已提 {EconomyFormat.FormatMoney(inc.PaidFen, "CNY")} · 预留 {EconomyFormat.FormatMoney(inc.ReservedFen, "CNY")}"
                        + (inc.DisputeFrozenFen > 0UL ? $" · 冻结 {EconomyFormat.FormatMoney(inc.DisputeFrozenFen, "CNY")}({inc.DisputeCaseId})" : "");
                    GameplayUiArt.Text(row, detail, 940f, 8f, 700f, 34f, 20f, inc.DisputeFrozenFen > 0UL ? CinnabarText : GameplayUiArt.Muted);
                    GameplayUiArt.Text(row, statusText, 1680f, 8f, 340f, 34f, 21f, statusColor);
                }
            }

            var wdBar = CreateCard(body, "RmbWithdrawBar", 24f, 498f, 2056f, 74f, TableHeaderTint);
            GameplayUiArt.Text(wdBar, "提现申请 · 提现金额（分，100分=1元）：", 24f, 16f, 640f, 40f, 22f, JadeTint);
            var amtField = QdaoUguiFactory.CreateInputField("WithdrawFen", wdBar, 680f, 10f, 560f, 54f, "请输入提现金额（分）", 20);
            amtField.textComponent.richText = false;
            var wdBtn = GameplayUiArt.Button(wdBar, "申请官方提现", 1640f, 10f, 380f, 54f, () =>
            {
                if (ulong.TryParse((amtField.text ?? "").Trim(), out ulong fen) && fen > 0UL)
                    onWithdrawFen?.Invoke(fen);
            }, primary: true, fontSize: 23f);
            registerWriteButton?.Invoke(wdBtn);
        }
    }
}
