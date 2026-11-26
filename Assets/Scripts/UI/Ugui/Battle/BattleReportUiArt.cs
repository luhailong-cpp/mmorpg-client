using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 战报复盘 V2 专用切图加载器（目录 Resources/UI/Ugui/BattleReportV2/）。
    /// 未放入新切图前静默返回 null，界面自动回落到现有宣纸/玄玉色块，零报错、放图即刻热生效。
    /// </summary>
    public static class BattleReportUiArt
    {
        public const string Root = "UI/Ugui/BattleReportV2/";

        public const string StampWin = "stamp_win";
        public const string StampLose = "stamp_lose";
        public const string StampDraw = "stamp_draw";
        public const string BadgeMvp = "badge_mvp";
        public const string IconHighlightKill = "icon_highlight_kill";
        public const string IconHighlightBurst = "icon_highlight_burst";
        public const string IconHighlightHeal = "icon_highlight_heal";
        public const string IconStatsChart = "icon_stats_chart";
        public const string BarDamageFill = "bar_damage_fill";
        public const string BarHealFill = "bar_heal_fill";

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite TryLoad(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>(Root + key);
            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(Root + key);
                if (tex != null)
                {
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = key;
                }
            }
            if (sprite != null) Cache[key] = sprite;
            return sprite;
        }

        public static void ApplyOptional(Image image, string key, Color fallbackColor, bool slicedIfBordered = false)
        {
            if (image == null) return;
            var sprite = TryLoad(key);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.type = slicedIfBordered && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                image.preserveAspect = image.type == Image.Type.Simple;
                if (image.type == Image.Type.Sliced)
                    image.pixelsPerUnitMultiplier = sprite.rect.height / Mathf.Max(1f, image.rectTransform.rect.height);
            }
            else
            {
                image.color = fallbackColor;
            }
        }
    }
}
