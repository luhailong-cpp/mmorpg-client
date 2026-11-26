using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Guild;
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
        private Text _ascendHint;
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
            _badgeDialog = Rect("PetBadgeGallery", _root, 0, 0, _width, 1080);
            float margin = Mathf.Lerp(10, 72, Mathf.InverseLerp(1920, 2560, _width));
            float shellW = _width - margin * 2, scaleX = shellW / 1932f;
            Art("PetBadgeFrame", _badgeDialog, margin, 18, shellW, 986, "guild_empty_shell");
            Art("PetBadgeFrameInput", _badgeDialog, margin + 125 * scaleX, 88, 1660 * scaleX, 883, "shell-close-hit", true);
            float start = margin + 157 * scaleX, galleryWidth = 1614 * scaleX;
            Art("PetBadgePaper", _badgeDialog, start, 209, galleryWidth, 725, "guild_announcement");
            Label("PetBadgeTitle", _badgeDialog, (_width - 650) / 2, 58, 650, 63, "五阶徽章与头像框", 45, Cream, TextAnchor.MiddleCenter);
            ActionButton("PetBadgeClose", _badgeDialog, margin + 1780 * scaleX, 43, 86 * scaleX, 99, "", false,
                () => { _badgesVisible = false; _badgeDialog.gameObject.SetActive(false); }, "shell-close-hit");
            string[] ids = { "normal", "mutant", "spirit", "divine", "sacred" };
            string[] names = { "普通", "变异", "灵兽", "神兽", "圣兽" };
            float columnW = (galleryWidth - 44) / 5;
            for (int i = 0; i < ids.Length; i++)
            {
                int index = i; float x = start + 22 + i * columnW;
                var panel = Rect("BadgeColumn" + i, _badgeDialog, x, 238, columnW, 637);
                if (i > 0) GuildUiArt.Line(panel, "BadgeColumnRule" + i, 0, 82, 1.5f, 508);
                ActionButton("PreviewBadge" + i, panel, 27, 19, columnW - 54, 53, names[i], _previewBadge == i,
                    () => { _previewBadge = index; BuildBadges(); }, _previewBadge == i ? "mail_tab_selected" : "mail_tab_normal", 29);
                float badge = Mathf.Min(180, columnW - 70), ring = Mathf.Min(220, columnW - 50);
                Art("BadgeArt" + i, panel, (columnW - badge) / 2, 105, badge, badge, "badge-" + ids[i]).preserveAspect = true;
                Label("BadgeCaption" + i, panel, 20, 285, columnW - 40, 42, "徽章", 25, Ink, TextAnchor.MiddleCenter);
                GuildUiArt.Line(panel, "BadgeArtRule" + i, 34, 333, columnW - 68);
                var portrait = Art("BadgePetPortrait" + i, panel, (columnW - ring * .72f) / 2,
                    360 + ring * .14f, ring * .72f, ring * .72f, "round_badge_taiji");
                portrait.sprite = Selected == null ? CharacterPetArt.Get("round_badge_taiji") : Portrait(Selected);
                portrait.preserveAspect = true;
                Art("BadgeRing" + i, panel, (columnW - ring) / 2, 360, ring, ring, "ring-" + ids[i]).preserveAspect = true;
                Label("BadgeRingCaption" + i, panel, 20, 587, columnW - 40, 35, "头像框", 25, Ink, TextAnchor.MiddleCenter);
            }
            string quality = Selected == null ? "—" : Selected.Quality.ToString();
            Label("PetBadgeCurrent", _badgeDialog, start + 28, 883, galleryWidth - 447, 38,
                "当前阶：" + (string.IsNullOrEmpty(Selected?.CultivationTierName) ? "—" : Selected.CultivationTierName) + "    品质：" + quality + "    当前预览：" + names[_previewBadge], 25, Ink, TextAnchor.MiddleLeft);
            Label("PetBadgeExplanation", _badgeDialog, start + 28, 920, galleryWidth - 447, 35,
                "当前阶由服务器独立身份确定；样式预览不改变宝宝身份、点数或成长。", 23, Muted, TextAnchor.MiddleLeft);
            bool isMaxTier = Selected?.CultivationTier >= 5;
            float buttonWidth = isMaxTier ? 370 : 215;
            var openAscend = ActionButton("PetBadgeOpenAscend", _badgeDialog, start + galleryWidth - 28 - buttonWidth, 902, buttonWidth, 55,
                isMaxTier ? MaxTierLabel : "灵阶突破",
                Selected?.NextTier?.CanAscend == true,
                () =>
                {
                    if (!HasNextTier(Selected)) return;
                    _badgesVisible = false;
                    _badgeDialog.gameObject.SetActive(false);
                    OpenAscendDialog();
                }, null, 24);
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
            _ascendDialog = Rect("PetAscendDialog", _root, 0, 0, _width, 1080);
            _ascendDialog.gameObject.AddComponent<Image>().color = Color.clear;
            const float boxW = 900f, boxH = 620f;
            var panel = Art("PetAscendFrame", _ascendDialog, (_width - boxW) * 0.5f, 220, boxW, boxH, "guild_announcement", true).rectTransform;
            Art("PetAscendTitlePlate", panel, 223, 27, 454, 62, "guild_tab_selected");
            Label("PetAscendTitle", panel, 233, 27, 434, 62, $"{pet.Name} · 灵阶突破", 34, Cream, TextAnchor.MiddleCenter);
            ActionButton("PetAscendClose", panel, boxW - 76, 18, 58, 58, "", false,
                () => { _ascendVisible = false; _ascendDialog.gameObject.SetActive(false); }, "close-jade");

            var next = pet.NextTier;
            string curKey = string.IsNullOrEmpty(pet.CultivationArtKey) ? TierArtKey(pet.CultivationTier) : pet.CultivationArtKey;
            string curName = string.IsNullOrEmpty(pet.CultivationTierName) ? "普通" : pet.CultivationTierName;

            Art("PetAscendCurBadge", panel, 180, 118, 136, 136, "badge-" + curKey).preserveAspect = true;
            Label("PetAscendCurName", panel, 148, 256, 200, 36, $"当前：{curName}", 24, Ink, TextAnchor.MiddleCenter);
            Label("PetAscendArrow", panel, 390, 165, 120, 48, "──▸", 34, Green, TextAnchor.MiddleCenter);
            string nextKey = string.IsNullOrEmpty(next.NextArtKey) ? TierArtKey(next.NextTier) : next.NextArtKey;
            Art("PetAscendNextBadge", panel, boxW - 316, 118, 136, 136, "badge-" + nextKey).preserveAspect = true;
            Label("PetAscendNextName", panel, boxW - 348, 256, 200, 36, $"突破至：{next.NextTierName}", 24, Green, TextAnchor.MiddleCenter);

            var infoPlate = Art("PetAscendInfoCard", panel, 72, 302, boxW - 144, 207, "mail_row_normal").rectTransform;
            string curTotal = EntryState?.TotalPoints.ToString() ?? "—";
            Art("PetAscendRequirementsHeader", infoPlate, 0, 0, boxW - 144, 36, "guild_table_header");
            Label("PetAscendRequirementsTitle", infoPlate, 28, 0, boxW - 200, 36, "突破条件", 24, Muted);
            Label("PetAscendReqLevel", infoPlate, 28, 40, boxW - 200, 36,
                $"宝宝等级要求：{pet.Level} / {next.MinPetLevel} 级", 24, pet.Level >= next.MinPetLevel ? Ink : new Color32(158, 56, 40, 255));
            GuildUiArt.Line(infoPlate, "PetAscendLevelRule", 19, 77, boxW - 182);
            Label("PetAscendReqItem", infoPlate, 28, 78, boxW - 200, 36,
                $"悟道残页：{next.OwnedItemCount} / {next.CostItemCount}", 24, next.OwnedItemCount >= next.CostItemCount ? Ink : new Color32(158, 56, 40, 255));
            GuildUiArt.Line(infoPlate, "PetAscendMaterialRule", 19, 116, boxW - 182);
            Label("PetAscendReqGold", infoPlate, 28, 117, boxW - 200, 37,
                $"消耗银两：{next.CostGold:N0}    ·    条目总预算：{curTotal} → {20UL + next.BonusEntryPoints} 点", 24, Ink);
            _ascendHint = Label("PetAscendBlockHint", infoPlate, 28, 158, boxW - 200, 42, "", 21, Muted);

            bool pendingRetry = _client?.HasPendingTierAscension(pet.PetId) == true;
            _ascendConfirm = ActionButton("PetAscendConfirm", panel, 205, 520, 230, 62,
                pendingRetry ? "重试突破" : "确认突破", true, ExecuteAscendTier, null, 25);
            ActionButton("PetAscendCancel", panel, 465, 520, 230, 62, "取消", false,
                () => { _ascendVisible = false; _ascendDialog.gameObject.SetActive(false); }, null, 25);
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
