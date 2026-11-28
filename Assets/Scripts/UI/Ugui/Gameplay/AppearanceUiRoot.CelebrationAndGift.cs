using System;
using System.Globalization;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.RewardedAd;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 批次 B2 主城「全城同乐」浮条/快捷施放条 + 好友纯外观/游历手记赠礼浮层（挂载于同一 AppearanceUiRoot）。
    /// </summary>
    public sealed partial class AppearanceUiRoot
    {
        private RectTransform _cityCelebrationHud;
        private Image _cityCelebrationPropIcon;
        private TMP_Text _cityCelebrationTitle;
        private TMP_Text _cityCelebrationSub;
        private Button _cityCelebrationClaimBtn;
        private TMP_Text _cityCelebrationClaimLabel;
        private ulong _shownCelebrationId;

        private RectTransform _showcaseCelebrationCastBar;
        private Button _charGiftBtn;
        private Button _cosmeticGiftBtn;

        // 好友赠礼确认浮层（仅真实灵石，满 7 天好友，每周 5 笔，绑定自用）
        private GameObject _giftOverlay;
        private TMP_Text _giftTitleText;
        private TMP_Text _giftDetailText;
        private TMP_InputField _giftTargetInput;
        private TMP_Text _giftStatusText;
        private Button _giftConfirmBtn;
        private global::CosmeticGiftTargetKind _giftTargetKind;
        private uint _giftTargetIdU32;
        private string _giftTargetIdStr = "";
        private ulong _giftExpectedPriceDiamond;

        private void BuildCelebrationHud(RectTransform hudRoot)
        {
            if (hudRoot == null) return;
            var bar = QdaoUguiFactory.CreateImage("CityCelebrationHud", hudRoot,
                840f, 118f, 880f, 104f, AppearanceUiArt.LoadGameplay("section_header"));
            bar.type = bar.sprite != null && bar.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            bar.color = bar.sprite != null ? Color.white : new Color(0.14f, 0.34f, 0.27f, 0.94f);
            _cityCelebrationHud = bar.rectTransform;

            _cityCelebrationPropIcon = QdaoUguiFactory.CreateImage("CityCelebrationPropIcon", _cityCelebrationHud,
                16f, 12f, 80f, 80f, AppearanceUiArt.LoadCelebrationWorldProp(3418u));
            _cityCelebrationPropIcon.preserveAspect = true;
            _cityCelebrationPropIcon.raycastTarget = false;

            _cityCelebrationTitle = QdaoUguiFactory.CreateText("CityCelebrationTitle", _cityCelebrationHud,
                110f, 14f, 520f, 36f, "全城同乐", 24f, AppearanceUiArt.WarmCream, TextAlignmentOptions.MidlineLeft);
            _cityCelebrationTitle.richText = false;
            _cityCelebrationTitle.fontStyle = FontStyles.Bold;

            _cityCelebrationSub = QdaoUguiFactory.CreateText("CityCelebrationSub", _cityCelebrationHud,
                110f, 54f, 520f, 34f, "点击领取绑定银两红包（本周上限 3000）", 19f, AppearanceUiArt.WarmCream, TextAlignmentOptions.MidlineLeft);
            _cityCelebrationSub.richText = false;

            _cityCelebrationClaimBtn = AppearanceUiArt.ActionButton(_cityCelebrationHud, "CityCelebrationClaimBtn",
                "领同乐绑银", 648f, 20f, 212f, 64f, HandleClaimCityCelebrationClicked, primary: true, fontSize: 22f);
            _cityCelebrationClaimLabel = _cityCelebrationClaimBtn.GetComponentInChildren<TMP_Text>(true);
            _cityCelebrationHud.gameObject.SetActive(false);
        }

        private void BuildCelebrationAndGiftPanelControls(RectTransform mainRoot, RectTransform fittedRoot)
        {
            // 1) 人物详情页「赠予好友」图标按钮（受 M-8 GiftingEnabled 门控）
            _charGiftBtn = AppearanceUiArt.ActionButton(mainRoot, "AppearanceCharGiftBtn",
                "赠礼", 1978f, 594f, 114f, 56f, HandleGiftSelectedCharacterClicked, primary: false, fontSize: 20f);
            AttachGiftIcon(_charGiftBtn);

            // 2) 排面页签底部「全城同乐」快捷施放条（天墉灵灯 3418 / 庆功烟花 3419 / 仙茶同乐席 3420）与「赠予好友」按钮
            if (_cosmeticsRoot != null)
            {
                _showcaseCelebrationCastBar = QdaoUguiFactory.CreateRect("ShowcaseCelebrationCastBar", _cosmeticsRoot,
                    64f, 804f, 1210f, 68f);
                var castTitle = QdaoUguiFactory.CreateText("CastTitle", _showcaseCelebrationCastBar,
                    0f, 12f, 200f, 44f, "主城全城同乐：", 22f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
                castTitle.fontStyle = FontStyles.Bold;
                castTitle.richText = false;
                BuildCelebrationCastButton(_showcaseCelebrationCastBar, "CastLanternBtn", 204f, 3418u, "放天墉灵灯");
                BuildCelebrationCastButton(_showcaseCelebrationCastBar, "CastFireworkBtn", 520f, 3419u, "燃庆功烟花");
                BuildCelebrationCastButton(_showcaseCelebrationCastBar, "CastTeaTableBtn", 836f, 3420u, "摆仙茶同乐席");
                _showcaseCelebrationCastBar.gameObject.SetActive(false);

                if (_cosmeticDetailPanel != null)
                {
                    _cosmeticGiftBtn = AppearanceUiArt.ActionButton(_cosmeticDetailPanel, "CosmeticGiftBtn",
                        "赠好友", 548f, 10f, 152f, 52f, HandleGiftSelectedCosmeticClicked, primary: false, fontSize: 20f);
                    AttachGiftIcon(_cosmeticGiftBtn);
                }
            }

            // 3) 好友赠礼二次确认浮层（挂于 _window 根下，保证游历手记打开赠礼时也可独立可见）
            var overlayRoot = CreateOverlayBackdrop(fittedRoot, "AppearanceCosmeticGiftOverlay");
            var card = AppearanceUiArt.Frame(overlayRoot, "GiftModalFrame", 590f, 220f, 980f, 560f);
            _giftTitleText = QdaoUguiFactory.CreateText("GiftTitle", card.transform,
                40f, 30f, 900f, 48f, "赠予好友", 34f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _giftTitleText.fontStyle = FontStyles.Bold;
            _giftTitleText.richText = false;

            _giftDetailText = QdaoUguiFactory.CreateText("GiftDetail", card.transform,
                56f, 92f, 868f, 128f, "", 22f, AppearanceUiArt.JadeInk, TextAlignmentOptions.TopLeft);
            _giftDetailText.richText = false;
            _giftDetailText.textWrappingMode = TextWrappingModes.Normal;

            var idCaption = QdaoUguiFactory.CreateText("GiftTargetCaption", card.transform,
                56f, 236f, 240f, 44f, "受赠好友编号：", 24f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            idCaption.richText = false;

            _giftTargetInput = QdaoUguiFactory.CreateInputField("GiftTargetPlayerIdInput", card.transform,
                300f, 232f, 624f, 54f, "请输入满 7 天好友的角色 ID（数字）", 24, null);

            _giftStatusText = QdaoUguiFactory.CreateText("GiftStatus", card.transform,
                56f, 304f, 868f, 92f, "", 20f, AppearanceUiArt.MutedInk, TextAlignmentOptions.TopLeft);
            _giftStatusText.richText = false;
            _giftStatusText.textWrappingMode = TextWrappingModes.Normal;

            AppearanceUiArt.ActionButton(card.transform, "GiftCancelBtn", "取消",
                210f, 438f, 240f, 74f, CloseCosmeticGiftModal, primary: false, fontSize: 26f);
            _giftConfirmBtn = AppearanceUiArt.ActionButton(card.transform, "GiftConfirmBtn", "确认赠礼",
                520f, 438f, 260f, 74f, HandleConfirmCosmeticGiftClicked, primary: true, fontSize: 26f);

            _giftOverlay = overlayRoot.gameObject;
            _giftOverlay.SetActive(false);
        }

        private static void AttachGiftIcon(Button btn)
        {
            if (btn == null) return;
            var sprite = AppearanceUiArt.LoadFriendGiftIcon();
            if (sprite == null) return;
            var icon = QdaoUguiFactory.CreateImage("GiftIcon", btn.transform, 10f, 10f, 32f, 32f, sprite);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        private void BuildCelebrationCastButton(RectTransform parent, string name, float x, uint itemConfigId, string label)
        {
            var btn = AppearanceUiArt.ActionButton(parent, name, label, x, 4f, 296f, 60f,
                () => _client?.CastCityCelebration(itemConfigId), primary: false, fontSize: 21f);
            var sprite = AppearanceUiArt.LoadCelebrationIcon(itemConfigId);
            if (sprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage("Icon", btn.transform, 12f, 10f, 40f, 40f, sprite);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
        }

        private void RefreshCityCelebrationHud()
        {
            if (_cityCelebrationHud == null) return;
            var list = _client?.ActiveCityCelebrations;
            if (!_available || list == null || list.Count == 0)
            {
                _cityCelebrationHud.gameObject.SetActive(false);
                _shownCelebrationId = 0ul;
                return;
            }
            var active = list[list.Count - 1];
            _shownCelebrationId = active.CelebrationId;
            if (_cityCelebrationPropIcon != null)
                _cityCelebrationPropIcon.sprite = AppearanceUiArt.LoadCelebrationWorldProp(active.ItemConfigId);
            string caster = string.IsNullOrWhiteSpace(active.CasterName) ? $"道友{active.CasterPlayerId}" : active.CasterName;
            string itemLabel = active.ItemConfigId switch
            {
                3418u => "天墉灵灯",
                3419u => "庆功烟花",
                3420u => "仙茶同乐席",
                _ => "全城同乐",
            };
            _cityCelebrationTitle.text = $"{caster} 在城中施放「{itemLabel}」";
            uint weeklyClaimed = _client?.Info?.CelebrationBindGoldClaimed ?? 0u;
            uint weeklyCap = _client?.Info?.CelebrationBindGoldWeeklyCap ?? 3000u;
            if (weeklyCap == 0u) weeklyCap = 3000u;
            _cityCelebrationSub.text = active.ClaimedByMe
                ? $"已领取本场绑定银两（本周 {weeklyClaimed}/{weeklyCap}）"
                : $"单场可领 {active.BindGoldReward} 绑定银两（本周 {weeklyClaimed}/{weeklyCap}）";
            if (_cityCelebrationClaimBtn != null)
            {
                _cityCelebrationClaimBtn.interactable = !active.ClaimedByMe && !(_client?.ActionPending ?? false);
                if (_cityCelebrationClaimLabel != null)
                    _cityCelebrationClaimLabel.text = active.ClaimedByMe ? "本场已领" : "领同乐绑银";
            }
            _cityCelebrationHud.gameObject.SetActive(true);
        }

        private void HandleClaimCityCelebrationClicked()
        {
            if (_shownCelebrationId == 0ul) return;
            _client?.ClaimCityCelebrationGift(_shownCelebrationId);
        }

        private void RefreshCelebrationAndGiftPanelState()
        {
            bool allowGift = ClientDistributionPolicy.Current.GiftingEnabled && (_client?.IsGiftSupported ?? false);
            if (_charGiftBtn != null)
            {
                bool showCharGift = allowGift && _cosmeticState.ActiveCategory == WardrobeCategoryTab.Character
                    && !string.IsNullOrWhiteSpace(_state.SelectedAppearanceId);
                _charGiftBtn.gameObject.SetActive(showCharGift);
            }
            if (_showcaseCelebrationCastBar != null)
            {
                bool showCast = _cosmeticState.ActiveCategory == WardrobeCategoryTab.Showcase
                    && (_client?.IsCelebrationSupported ?? false);
                _showcaseCelebrationCastBar.gameObject.SetActive(showCast);
            }
            if (_cosmeticGiftBtn != null)
            {
                var sel = _cosmeticState.SyncSelectedRow(_cosmeticState.ActiveCategory, _currentCosmeticRowViews);
                bool showCosmeticGift = allowGift && _cosmeticState.ActiveCategory != WardrobeCategoryTab.Character
                    && sel != null && !sel.IsBaseDefault && sel.SpiritShopGoodsId != 0u;
                _cosmeticGiftBtn.gameObject.SetActive(showCosmeticGift);
            }
        }

        /// <summary>打开好友纯外观 / 游历手记赠礼确认弹窗（供衣柜与游历手记复用）。</summary>
        public void OpenCosmeticGiftModal(
            global::CosmeticGiftTargetKind targetKind,
            uint targetIdU32,
            string targetIdStr,
            string displayName,
            ulong expectedPriceDiamond)
        {
            if (!ClientDistributionPolicy.Current.GiftingEnabled || _giftOverlay == null) return;
            if (!IsPanelVisible && _window != null) ShowPanel();
            _giftTargetKind = targetKind;
            _giftTargetIdU32 = targetIdU32;
            _giftTargetIdStr = targetIdStr ?? string.Empty;
            _giftExpectedPriceDiamond = expectedPriceDiamond;
            _giftTitleText.text = $"赠予好友 · {displayName}";
            _giftDetailText.text =
                $"消耗真实充值灵石：{expectedPriceDiamond.ToString(CultureInfo.InvariantCulture)} 灵石（绝不混扣绑定灵石）\n" +
                "规则：仅可赠予互加好友满 7 天且双向未拉黑的道友；每周送出上限 5 笔；\n" +
                "受赠方将获得绑定永久外观或手记典藏轨，若好友已拥有将自动原路退回灵石。";
            _giftStatusText.text = string.Empty;
            if (_giftTargetInput != null) _giftTargetInput.text = string.Empty;
            _giftOverlay.SetActive(true);
        }

        private void CloseCosmeticGiftModal()
        {
            if (_giftOverlay != null) _giftOverlay.SetActive(false);
        }

        private void HandleGiftSelectedCharacterClicked()
        {
            string selId = _state.SelectedAppearanceId;
            if (string.IsNullOrWhiteSpace(selId)) return;
            string title = selId;
            if (_client?.Info != null)
            {
                foreach (var item in _client.Info.Appearances)
                {
                    if (item.AppearanceId == selId && !string.IsNullOrWhiteSpace(item.Name))
                    {
                        title = item.Name;
                        break;
                    }
                }
            }
            ulong price = 100ul;
            OpenCosmeticGiftModal(global::CosmeticGiftTargetKind.CosmeticGiftTargetCharacter, 0u, selId, title, price);
        }

        private void HandleGiftSelectedCosmeticClicked()
        {
            var sel = _cosmeticState.SyncSelectedRow(_cosmeticState.ActiveCategory, _currentCosmeticRowViews);
            if (sel == null || sel.IsBaseDefault) return;
            var kind = _cosmeticState.ActiveCategory switch
            {
                WardrobeCategoryTab.Pet => global::CosmeticGiftTargetKind.CosmeticGiftTargetPetCosmetic,
                WardrobeCategoryTab.SkillEffect => global::CosmeticGiftTargetKind.CosmeticGiftTargetSkillVfx,
                WardrobeCategoryTab.Showcase => global::CosmeticGiftTargetKind.CosmeticGiftTargetShowcase,
                _ => global::CosmeticGiftTargetKind.CosmeticGiftTargetUnspecified,
            };
            if (kind == global::CosmeticGiftTargetKind.CosmeticGiftTargetUnspecified) return;
            ulong price = _cosmeticState.ActiveCategory == WardrobeCategoryTab.SkillEffect ? 180ul
                : (_cosmeticState.ActiveCategory == WardrobeCategoryTab.Pet ? 120ul : 80ul);
            OpenCosmeticGiftModal(kind, sel.ConfigId, string.Empty, sel.Name, price);
        }

        private void HandleConfirmCosmeticGiftClicked()
        {
            string raw = _giftTargetInput != null ? _giftTargetInput.text.Trim() : string.Empty;
            if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong targetPlayerId) || targetPlayerId == 0ul)
            {
                if (_giftStatusText != null) _giftStatusText.text = "请输入有效的好友角色 ID（正整数）。";
                return;
            }
            if (_client == null) return;
            if (_client.SendCosmeticGift(targetPlayerId, _giftTargetKind, _giftTargetIdU32, _giftTargetIdStr, _giftExpectedPriceDiamond))
                CloseCosmeticGiftModal();
            else if (_giftStatusText != null)
                _giftStatusText.text = _client.Status;
        }
    }
}
