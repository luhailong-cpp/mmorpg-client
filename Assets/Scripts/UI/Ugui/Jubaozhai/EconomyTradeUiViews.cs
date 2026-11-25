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

        /// <summary>在容器背景安全叠加固定比例无字面板艺术图（不拉伸变形），再由前景卡片承载高对比文字。</summary>
        public static void DrawPanelBackdrop(RectTransform parent, string sliceName, float alpha = 0.26f)
        {
            var sprite = SeasonEconomyUiArt.LoadTradeSlice(sliceName);
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
                24f, 122f, 650f, 240f, 22f, GameplayUiArt.Ink, wrap: true);

            var talisman = SeasonEconomyUiArt.LoadTradeSlice("icon_unseal_talisman");
            if (talisman != null)
            {
                var icon = QdaoUguiFactory.CreateImage("ShopTalismanPreview", left, 24f, 368f, 96f, 96f, talisman, false);
                icon.preserveAspect = true;
                GameplayUiArt.Text(left, "匠心配套亮点：解封剥离符\n清空绑定养成，仅恢复纯 T 原胚，绝不洗白 B 材料。",
                    132f, 372f, 540f, 92f, 20f, GameplayUiArt.Muted, wrap: true);
            }
            GameplayUiArt.Button(left, "管理 / 购买仙途轻享卡", 24f, 446f, 380f, 48f, onOpenLightCard, primary: true, fontSize: 22f);

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
    }
}
