using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Guild;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    public sealed partial class PetCultivationPanel
    {
        private bool _badgesVisible;
        private RectTransform _badgeDialog;
        private int _previewBadge;
        private bool _ascendVisible;
        private bool _ascendPending;
        private RectTransform _ascendDialog;
        private Button _ascendConfirm;
        private TMP_Text _ascendHint;
        private string _ascendError;
        private const string MaxTierLabel = "已达巅峰 · 圣兽（条目预算 +8）";

        private static bool HasNextTier(PetInfo pet) => pet?.NextTier?.NextTier > 0 && pet.CultivationTier < 5;
        private bool CanConfirmAscend(PetInfo pet) => CanWrite && !HasDraft && !_ascendPending && HasNextTier(pet) &&
            (pet.NextTier.CanAscend || _client.HasPendingTierAscension(pet.PetId));

        private void OpenAscendDialog()
        {
            if (!HasNextTier(Selected)) return;
            _ascendError = null;
            _ascendVisible = true;
            BuildAscendDialog();
        }

        // 五阶只使用服务端独立 CultivationTier；Quality 与获取类别不参与映射。
        private void BuildBadges()
        {
            if (_badgeDialog != null) DestroyUi(_badgeDialog.gameObject);
            _badgeDialog = Rect("PetBadgeGallery", _root, 0f, 0f, _width, 1080f);
            var dim = _badgeDialog.gameObject.AddComponent<Image>();
            dim.color = ModalDim;
            dim.raycastTarget = true;

            var shell = Rect("PetBadgeShell", _badgeDialog, ShellX, ShellTopY, ShellNativeWidth, ShellNativeHeight);
            Art("PetBadgeFrame", shell, 0f, 0f, ShellNativeWidth, ShellNativeHeight, "guild_empty_shell");
            Art("PetBadgeFrameInput", shell, 125f, 70f, 1660f, 712f, "shell-close-hit", true);
            Label("PetBadgeTitle", shell, 696f, 22f, 540f, 60f, "五阶徽章与头像框", 40f, Cream,
                TextAlignmentOptions.Center, emphasized: true);
            ActionButton("PetBadgeClose", shell, 1780f, 22f, 88f, 84f, "", false,
                () => { _badgesVisible = false; _badgeDialog.gameObject.SetActive(false); }, "shell-close-hit");

            // 整块玉纸底板覆盖双栏壳内中缝，并留足底缘安全距避开左下玉兔灯花
            const float startX = 184f;
            const float galleryW = 1564f;
            Art("PetBadgePaper", shell, startX, 114f, galleryW, 646f, "guild_announcement");

            string[] ids = { "normal", "mutant", "spirit", "divine", "sacred" };
            string[] names = { "普通", "变异", "灵兽", "神兽", "圣兽" };
            float columnW = (galleryW - 40f) / 5f;
            for (int i = 0; i < ids.Length; i++)
            {
                int index = i;
                float colX = startX + 20f + i * columnW;
                var panel = Rect("BadgeColumn" + i, shell, colX, 134f, columnW, 506f);
                if (i > 0) GuildUiArt.Line(panel, "BadgeColumnRule" + i, 0f, 70f, 1.5f, 418f);
                ActionButton("PreviewBadge" + i, panel, 28f, 12f, columnW - 56f, 48f, names[i], _previewBadge == i,
                    () => { _previewBadge = index; BuildBadges(); }, _previewBadge == i ? "mail_tab_selected" : "button_secondary", 25f);
                const float badgeSize = 148f;
                const float ringSize = 180f;
                Art("BadgeArt" + i, panel, (columnW - badgeSize) * 0.5f, 78f, badgeSize, badgeSize, "badge-" + ids[i]).preserveAspect = true;
                Label("BadgeCaption" + i, panel, 16f, 232f, columnW - 32f, 30f, "徽章", 22f, Ink, TextAlignmentOptions.Center);
                GuildUiArt.Line(panel, "BadgeArtRule" + i, 34f, 268f, columnW - 68f);
                float portraitSize = ringSize * .74f;
                var portrait = Art("BadgePetPortrait" + i, panel, (columnW - portraitSize) * 0.5f,
                    282f + (ringSize - portraitSize) * 0.5f, portraitSize, portraitSize, "round_badge_taiji");
                portrait.sprite = Selected == null ? CharacterPetArt.Get("round_badge_taiji") : Portrait(Selected);
                portrait.preserveAspect = true;
                Art("BadgeRing" + i, panel, (columnW - ringSize) * 0.5f, 282f, ringSize, ringSize, "ring-" + ids[i]).preserveAspect = true;
                Label("BadgeRingCaption" + i, panel, 16f, 468f, columnW - 32f, 32f, "头像框", 22f, Ink, TextAlignmentOptions.Center);
            }

            GuildUiArt.Line(shell, "PetBadgeFooterRule", startX + 28f, 654f, galleryW - 56f);
            string quality = Selected == null ? "—" : Selected.Quality.ToString();
            bool isMaxTier = Selected?.CultivationTier >= 5;
            float buttonWidth = isMaxTier ? 380f : 220f;
            Label("PetBadgeCurrent", shell, startX + 34f, 672f, galleryW - buttonWidth - 82f, 32f,
                "当前阶：" + (string.IsNullOrEmpty(Selected?.CultivationTierName) ? "—" : Selected.CultivationTierName) +
                "  ·  品质：" + quality + "  ·  当前预览：" + names[_previewBadge],
                22f, Ink);
            Label("PetBadgeExplanation", shell, startX + 34f, 706f, galleryW - buttonWidth - 82f, 28f,
                "当前阶由服务器独立身份确定；样式预览不改变宝宝身份、点数或成长。", 19f, Muted);

            var openAscend = ActionButton("PetBadgeOpenAscend", shell, startX + galleryW - buttonWidth - 28f, 676f, buttonWidth, FooterBtnHeight,
                isMaxTier ? MaxTierLabel : "灵阶突破",
                Selected?.NextTier?.CanAscend == true,
                () =>
                {
                    if (!HasNextTier(Selected)) return;
                    _badgesVisible = false;
                    _badgeDialog.gameObject.SetActive(false);
                    OpenAscendDialog();
                }, null, 23f);
            openAscend.interactable = HasNextTier(Selected);
        }

        private static string TierArtKey(uint tier) => tier switch
        {
            2 => "mutant",
            3 => "spirit",
            4 => "divine",
            >= 5 => "sacred",
            _ => "normal",
        };

        private void BuildAscendDialog()
        {
            if (_ascendDialog != null) DestroyUi(_ascendDialog.gameObject);
            _ascendConfirm = null;
            _ascendHint = null;
            var pet = Selected;
            if (!HasNextTier(pet)) { _ascendVisible = false; return; }

            _ascendDialog = Rect("PetAscendDialog", _root, 0f, 0f, _width, 1080f);
            var dim = _ascendDialog.gameObject.AddComponent<Image>();
            dim.color = ModalDim;
            dim.raycastTarget = true;

            const float boxW = 900f, boxH = 600f;
            var panel = Art("PetAscendFrame", _ascendDialog, Mathf.Round((_width - boxW) * 0.5f), 240f, boxW, boxH, "guild_announcement", true).rectTransform;
            Art("PetAscendTitlePlate", panel, 223f, 24f, 454f, 60f, "guild_tab_selected");
            Label("PetAscendTitle", panel, 238f, 24f, 424f, 60f, $"{pet.Name} · 灵阶突破", 32f, Cream,
                TextAlignmentOptions.Center, autoSize: true, minSize: 22f, emphasized: true);
            ActionButton("PetAscendClose", panel, boxW - 76f, 16f, 58f, 58f, "", false,
                () => { _ascendVisible = false; _ascendDialog.gameObject.SetActive(false); }, "guild_close");

            var next = pet.NextTier;
            string curKey = string.IsNullOrEmpty(pet.CultivationArtKey) ? TierArtKey(pet.CultivationTier) : pet.CultivationArtKey;
            string curName = string.IsNullOrEmpty(pet.CultivationTierName) ? "普通" : pet.CultivationTierName;

            Art("PetAscendCurBadge", panel, 182f, 106f, 136f, 136f, "badge-" + curKey).preserveAspect = true;
            Label("PetAscendCurName", panel, 150f, 246f, 200f, 34f, $"当前：{curName}", 23f, Ink, TextAlignmentOptions.Center);
            // SimKai 原生支持 ─ (U+2500) 与 → (U+2192)，杜绝缺失字形豆腐块
            Label("PetAscendArrow", panel, 386f, 152f, 128f, 46f, "──→", 32f, Green, TextAlignmentOptions.Center);
            string nextKey = string.IsNullOrEmpty(next.NextArtKey) ? TierArtKey(next.NextTier) : next.NextArtKey;
            Art("PetAscendNextBadge", panel, boxW - 318f, 106f, 136f, 136f, "badge-" + nextKey).preserveAspect = true;
            Label("PetAscendNextName", panel, boxW - 350f, 246f, 200f, 34f, $"突破至：{next.NextTierName}", 23f, Green, TextAlignmentOptions.Center);

            var infoPlate = Art("PetAscendInfoCard", panel, 68f, 294f, boxW - 136f, 204f, "mail_row_normal").rectTransform;
            string curTotal = EntryState?.TotalPoints.ToString() ?? "—";
            Art("PetAscendRequirementsHeader", infoPlate, 0f, 0f, boxW - 136f, 36f, "guild_table_header");
            Label("PetAscendRequirementsTitle", infoPlate, 28f, 0f, boxW - 192f, 36f, "突破条件", 22f, Muted);
            Label("PetAscendReqLevel", infoPlate, 28f, 40f, boxW - 192f, 34f,
                $"宝宝等级要求：{pet.Level} / {next.MinPetLevel} 级", 22f, pet.Level >= next.MinPetLevel ? Ink : new Color32(158, 56, 40, 255));
            GuildUiArt.Line(infoPlate, "PetAscendLevelRule", 18f, 76f, boxW - 172f);
            Label("PetAscendReqItem", infoPlate, 28f, 78f, boxW - 192f, 34f,
                $"悟道残页：{next.OwnedItemCount} / {next.CostItemCount}", 22f, next.OwnedItemCount >= next.CostItemCount ? Ink : new Color32(158, 56, 40, 255));
            GuildUiArt.Line(infoPlate, "PetAscendMaterialRule", 18f, 114f, boxW - 172f);
            Label("PetAscendReqGold", infoPlate, 28f, 116f, boxW - 192f, 34f,
                $"消耗银两：{next.CostGold:N0} · 条目总预算：{curTotal} → {20UL + next.BonusEntryPoints} 点", 22f, Ink);
            _ascendHint = Label("PetAscendBlockHint", infoPlate, 28f, 156f, boxW - 192f, 38f, "", 20f, Muted);

            bool pendingRetry = _client?.HasPendingTierAscension(pet.PetId) == true;
            _ascendConfirm = ActionButton("PetAscendConfirm", panel, 205f, 516f, 230f, 58f,
                pendingRetry ? "重试突破" : "确认突破", true, ExecuteAscendTier, null, 24f);
            ActionButton("PetAscendCancel", panel, 465f, 516f, 230f, 58f, "取消", false,
                () => { _ascendVisible = false; _ascendDialog.gameObject.SetActive(false); }, null, 24f);
            RefreshAscendDialogButtons();
        }

        private void RefreshAscendDialogButtons()
        {
            if (_ascendConfirm == null) return;
            var pet = Selected;
            var next = pet?.NextTier;
            bool retry = pet != null && _client?.HasPendingTierAscension(pet.PetId) == true;
            _ascendConfirm.interactable = CanConfirmAscend(pet);
            if (_ascendHint == null || next == null) return;
            _ascendHint.text = BattleLocked ? "战斗中不可进行灵阶突破。"
                : HasDraft ? "请先保存或放弃培养草稿，再进行灵阶突破。"
                : _ascendPending ? "正在提交灵阶突破，等待服务器确认…"
                : _ascendError != null ? _ascendError
                : retry ? "上次突破结果尚未确认，可重试原请求。"
                : !next.CanAscend && next.BlockTipId != 0 ? PetClient.DescribeTip("", new TipInfoMessage { Id = next.BlockTipId })
                : !next.CanAscend ? "尚未满足突破条件。"
                : !CanWrite ? "当前无法进行灵阶突破。"
                : "确认后将消耗上述材料和银两。";
            _ascendHint.color = _ascendConfirm.interactable && _ascendError == null ? Muted : new Color32(158, 56, 40, 255);
        }

        private void ExecuteAscendTier()
        {
            var pet = Selected;
            if (!CanConfirmAscend(pet)) return;
            var client = _client;
            ulong petId = pet.PetId;
            _ascendError = null;
            _ascendPending = true;
            _message = "正在提交灵阶突破，等待服务器确认…";
            RefreshFields();
            client.AscendTier(petId, pet.CultivationTier, (success, newTier, _, error) =>
            {
                if (_disposed || !ReferenceEquals(client, _client)) return;
                _ascendPending = false;
                if (petId != _selectedPetId) return;
                if (success)
                {
                    _ascendVisible = false;
                    _message = $"灵阶突破成功！已升至 {Selected?.CultivationTierName ?? $"第{newTier}阶"}。";
                }
                else
                {
                    _message = _ascendError = error ?? "灵阶突破未成功，请重试。";
                }
                Render();
            });
        }
    }
}
