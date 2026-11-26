using MmorpgClient.Game.Pet;
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

        // 五阶只使用服务端独立 CultivationTier；Quality 与获取类别不参与映射。
        private void BuildBadges()
        {
            if (_badgeDialog != null) DestroyUi(_badgeDialog.gameObject);
            _badgeDialog = Rect("PetBadgeGallery", _root, 0, 0, _width, 1080);
            float margin = Mathf.Lerp(76, 133, Mathf.InverseLerp(1920, 2560, _width));
            Art("PetBadgeFrame", _badgeDialog, margin, 94, _width - 2 * margin, 921, "window_frame", true);
            Art("PetBadgeTitlePlate", _badgeDialog, (_width - 650) / 2, 54, 650, 110, "title_plate");
            Art("PetBadgeTaiji", _badgeDialog, (_width - 56) / 2, 23, 56, 56, "round_badge_taiji");
            Label("PetBadgeTitle", _badgeDialog, (_width - 650) / 2, 76, 650, 60, "五阶徽章与头像框", 42, Cream, TextAnchor.MiddleCenter);
            ActionButton("PetBadgeClose", _badgeDialog, _width - margin - 77, 106, 61, 72, "", false,
                () => { _badgesVisible = false; _badgeDialog.gameObject.SetActive(false); }, "close-jade");
            string[] ids = { "normal", "mutant", "spirit", "divine", "sacred" };
            string[] names = { "普通", "变异", "灵兽", "神兽", "圣兽" };
            float start = margin + 45, columnW = (_width - 2 * start - 4 * 28) / 5;
            for (int i = 0; i < ids.Length; i++)
            {
                int index = i; float x = start + i * (columnW + 28);
                var panel = Art("BadgeColumn" + i, _badgeDialog, x, 198, columnW, 684, "application_card").rectTransform;
                ActionButton("PreviewBadge" + i, panel, 38, 19, columnW - 76, 49, names[i], _previewBadge == i,
                    () => { _previewBadge = index; BuildBadges(); }, _previewBadge == i ? "mail_tab_selected" : "mail_tab_normal", 29);
                float badge = Mathf.Min(180, columnW - 70), ring = Mathf.Min(220, columnW - 50);
                Art("BadgeArt" + i, panel, (columnW - badge) / 2, 105, badge, badge, "badge-" + ids[i]).preserveAspect = true;
                Label("BadgeCaption" + i, panel, 20, 285, columnW - 40, 42, "徽章", 24, Muted, TextAnchor.MiddleCenter);
                Art("BadgePortraitPlate" + i, panel, 30, 338, columnW - 60, 284, "mail_row_normal");
                var portrait = Art("BadgePetPortrait" + i, panel, (columnW - ring * .72f) / 2,
                    360 + ring * .14f, ring * .72f, ring * .72f, "round_badge_taiji");
                portrait.sprite = Selected == null ? CharacterPetArt.Get("round_badge_taiji") : Portrait(Selected);
                portrait.preserveAspect = true;
                Art("BadgeRing" + i, panel, (columnW - ring) / 2, 360, ring, ring, "ring-" + ids[i]).preserveAspect = true;
                Label("BadgeRingCaption" + i, panel, 20, 587, columnW - 40, 35, "头像框", 24, Muted, TextAnchor.MiddleCenter);
            }
            string quality = Selected == null ? "—" : Selected.Quality.ToString();
            Label("PetBadgeCurrent", _badgeDialog, margin + 50, 898, _width - 2 * margin - 320, 38,
                "当前阶：" + (string.IsNullOrEmpty(Selected?.CultivationTierName) ? "—" : Selected.CultivationTierName) + "    品质：" + quality + "    当前预览：" + names[_previewBadge], 25, Ink, TextAnchor.MiddleLeft);
            Label("PetBadgeExplanation", _badgeDialog, margin + 50, 945, _width - 2 * margin - 320, 38,
                "当前阶由服务器独立身份确定；样式预览不改变宝宝身份、点数或成长。", 23, Muted, TextAnchor.MiddleLeft);
            var openAscend = ActionButton("PetBadgeOpenAscend", _badgeDialog, _width - margin - 260, 912, 215, 62,
                Selected?.NextTier == null || Selected.NextTier.NextTier == 0 ? "查看灵阶" : "灵阶突破",
                Selected?.NextTier?.CanAscend == true,
                () => { _badgesVisible = false; _badgeDialog.gameObject.SetActive(false); _ascendVisible = true; BuildAscendDialog(); }, null, 24);
            openAscend.interactable = Selected != null;
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
            var pet = Selected;
            if (pet == null) { _ascendVisible = false; return; }
            _ascendDialog = Rect("PetAscendDialog", _root, 0, 0, _width, 1080);
            _ascendDialog.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .38f);
            const float boxW = 900f, boxH = 620f;
            var panel = Art("PetAscendFrame", _ascendDialog, (_width - boxW) * 0.5f, 220, boxW, boxH, "window_frame", true).rectTransform;
            Label("PetAscendTitle", panel, 60, 34, boxW - 120, 56, $"{pet.Name} · 灵阶突破", 34, Ink, TextAnchor.MiddleCenter);
            ActionButton("PetAscendClose", panel, boxW - 76, 18, 58, 58, "", false,
                () => { _ascendVisible = false; _ascendDialog.gameObject.SetActive(false); }, "close-jade");

            var next = pet.NextTier;
            bool maxed = next == null || next.NextTier == 0;
            string curKey = string.IsNullOrEmpty(pet.CultivationArtKey) ? TierArtKey(pet.CultivationTier) : pet.CultivationArtKey;
            string curName = string.IsNullOrEmpty(pet.CultivationTierName) ? "普通" : pet.CultivationTierName;

            if (maxed)
            {
                Art("PetAscendMaxBadge", panel, (boxW - 170) * 0.5f, 130, 170, 170, "badge-sacred").preserveAspect = true;
                Label("PetAscendMaxText", panel, 60, 326, boxW - 120, 56, "已达巅峰 · 圣兽（+8 条目预算）", 30, Green, TextAnchor.MiddleCenter);
                Label("PetAscendMaxSub", panel, 60, 392, boxW - 120, 48,
                    $"当前条目总预算：{pet.Entries?.TotalPoints ?? 28} 点 · 无需继续突破", 23, Muted, TextAnchor.MiddleCenter);
                ActionButton("PetAscendMaxOk", panel, (boxW - 240) * 0.5f, 515, 240, 62, "关闭", true,
                    () => { _ascendVisible = false; _ascendDialog.gameObject.SetActive(false); }, null, 25);
                return;
            }

            Art("PetAscendCurBadge", panel, 180, 118, 136, 136, "badge-" + curKey).preserveAspect = true;
            Label("PetAscendCurName", panel, 148, 256, 200, 36, $"当前：{curName}", 24, Ink, TextAnchor.MiddleCenter);
            Label("PetAscendArrow", panel, 390, 165, 120, 48, "──▸", 34, Green, TextAnchor.MiddleCenter);
            Art("PetAscendNextBadge", panel, boxW - 316, 118, 136, 136, "badge-" + TierArtKey(next.NextTier)).preserveAspect = true;
            Label("PetAscendNextName", panel, boxW - 348, 256, 200, 36, $"突破至：{next.NextTierName}", 24, Green, TextAnchor.MiddleCenter);

            var infoPlate = Art("PetAscendInfoCard", panel, 72, 306, boxW - 144, 186, "mail_row_normal").rectTransform;
            ulong curTotal = pet.Entries?.TotalPoints ?? 20UL;
            Label("PetAscendReqLevel", infoPlate, 28, 16, boxW - 200, 36,
                $"宝宝等级要求：{pet.Level} / {next.MinPetLevel} 级", 23, pet.Level >= next.MinPetLevel ? Green : new Color32(158, 56, 40, 255));
            Label("PetAscendReqItem", infoPlate, 28, 56, boxW - 200, 36,
                $"悟道残页：{next.OwnedItemCount} / {next.CostItemCount}", 23, next.OwnedItemCount >= next.CostItemCount ? Green : new Color32(158, 56, 40, 255));
            Label("PetAscendReqGold", infoPlate, 28, 96, boxW - 200, 36,
                $"消耗银两：{next.CostGold:N0}    ·    突破后条目预算加成：+{next.BonusEntryPoints} 点（当前总计 {curTotal} 点）", 22, Ink);

            string blockMsg = BattleLocked ? "战斗中不可进行灵阶突破"
                : !next.CanAscend && next.BlockTipId != 0
                    ? PetClient.DescribeTip("", new TipInfoMessage { Id = next.BlockTipId })
                    : !next.CanAscend ? "尚未满足突破条件" : "条件已满足，确认后立即扣材扣银并提升条目总预算。";
            Label("PetAscendBlockHint", infoPlate, 28, 140, boxW - 200, 36, blockMsg, 20,
                next.CanAscend && !BattleLocked ? Muted : new Color32(158, 56, 40, 255));

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
            _ascendConfirm.interactable = pet != null && CanWrite && !_ascendPending && next != null && next.NextTier > 0 && (next.CanAscend || retry);
        }

        private void ExecuteAscendTier()
        {
            var pet = Selected;
            if (pet == null || !CanWrite || _ascendPending) return;
            ulong petId = pet.PetId;
            _ascendPending = true;
            _message = "正在提交灵阶突破，等待服务器确认…";
            RefreshFields();
            _client.AscendTier(petId, pet.CultivationTier, (success, newTier, list, error) =>
            {
                if (_disposed) return;
                _ascendPending = false;
                if (success)
                {
                    _ascendVisible = false;
                    _message = $"灵阶突破成功！已升至 {Selected?.CultivationTierName ?? $"第{newTier}阶"}。";
                }
                else
                {
                    _message = error ?? "灵阶突破未成功，请重试。";
                }
                Render();
            });
        }
    }
}
