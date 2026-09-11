using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Jade, ivory and fine gold artwork shared by the three player windows.</summary>
    public static class GameplayUiArt
    {
        public const string Root = "UI/Ugui/GameplayV1/";
        public static readonly Color Ink = QdaoUguiTheme.Html("#294F41");
        public static readonly Color Muted = QdaoUguiTheme.Html("#77664D");
        public static readonly Color Cream = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color Gold = QdaoUguiTheme.Html("#AA803E");
        public static Sprite Load(string key) => QdaoUguiTheme.RequireSprite(Root + key);

        public static Image Art(UnityEngine.Transform parent, string key, float x, float y, float w, float h, bool aspect = false)
        {
            var image = QdaoUguiFactory.CreateImage(key, parent, x, y, w, h, Load(key));
            image.type = !aspect && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = aspect;
            image.pixelsPerUnitMultiplier = 1;
            return image;
        }

        public static TextMeshProUGUI Text(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            float size = 30, Color? color = null, bool wrap = false, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = QdaoUguiFactory.CreateText("Label", parent, x, y, w, h, value, size, color ?? Ink, alignment);
            label.richText = false; // Server names and descriptions are plain text, never TMP markup.
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (wrap) label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        public static Button Button(UnityEngine.Transform parent, string label, float x, float y, float w, float h,
            Action click, bool primary = false, bool enabled = true, string key = null, float fontSize = 30)
        {
            key ??= primary ? "button_primary" : "button_secondary";
            var b = QdaoUguiFactory.CreateArtButton(label, parent, x, y, w, h, Load(key), out var image);
            image.type = image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = b.colors;
            colors.highlightedColor = new Color(1, .95f, .80f);
            colors.selectedColor = new Color(1, .94f, .77f);
            b.colors = colors;
            b.interactable = enabled;
            if (click != null) b.onClick.AddListener(() => click());
            Text(b.transform, label, 20, 0, w - 40, h, fontSize,
                primary || key == "tab_selected" || key == "pet_card_selected" ? Cream : Ink,
                alignment: TextAlignmentOptions.Center);
            return b;
        }

        public static void Clear(UnityEngine.Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        public static ulong MissionKey(PlayerMissionInfo mission) => ((ulong)mission.Scope << 32) | mission.MissionId;
        public static string MissionName(PlayerMissionInfo mission) =>
            string.IsNullOrWhiteSpace(mission.Name) ? $"任务 · {mission.MissionId}" : mission.Name;
        public static string ActivityName(PlayerActivityInfo activity) =>
            string.IsNullOrWhiteSpace(activity.Name) ? $"活动 · {activity.ActivityId}" : activity.Name;
        public static string ItemName(BagItemInfo item) =>
            string.IsNullOrWhiteSpace(item.Name) ? $"物品 · {item.ConfigId}" : item.Name;
        public static string MissionStatus(PlayerMissionStatus status) => (int)status switch
        {
            1 => "进行中", 2 => "已完成", 3 => "目标已达成", 4 => "未完成", _ => "待开启"
        };
        public static string ActivityStatus(PlayerActivityStatus status) => (int)status switch
        {
            1 => "即将开启", 2 => "进行中", 3 => "已结束", _ => "待开放"
        };

        // Icon paths are optional presentation metadata, scoped to game UI resources.
        public static void ItemIcon(UnityEngine.Transform parent, string key, float x, float y, float size)
        {
            Sprite icon = null;
            if (!string.IsNullOrWhiteSpace(key) && !key.Contains("..") && !key.Contains(":"))
                icon = Battle.BattleArtCatalog.LoadSprite(key.StartsWith("UI/") ? key : Root + key);
            if (icon == null) icon = Load("icon_scroll");
            var image = QdaoUguiFactory.CreateImage("ItemIcon", parent, x, y, size, size, icon);
            image.preserveAspect = true;
        }
    }
}
