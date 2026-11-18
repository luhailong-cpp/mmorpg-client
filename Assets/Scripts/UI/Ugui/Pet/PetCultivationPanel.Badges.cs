using UnityEngine;

namespace MmorpgClient.UI.Ugui.Pet
{
    public sealed partial class PetCultivationPanel
    {
        private bool _badgesVisible;
        private RectTransform _badgeDialog;
        private int _previewBadge;

        // PetInfo.Quality is a quality value, and PetTable.AcquisitionTier is a separate acquisition
        // classification. Neither field is the proposed five-tier cultivation rank.
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
            Label("PetBadgeCurrent", _badgeDialog, margin + 50, 898, _width - 2 * margin - 100, 38,
                "当前阶：—    品质：" + quality + "    当前预览：" + names[_previewBadge], 25, Ink, TextAnchor.MiddleCenter);
            Label("PetBadgeExplanation", _badgeDialog, margin + 50, 945, _width - 2 * margin - 100, 38,
                "五阶名称与样式为候选方案；预览不改变宝宝品质、点数或成长。", 23, Muted, TextAnchor.MiddleCenter);
        }
    }
}
