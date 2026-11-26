using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 五行阵法 / 五行幻境爬塔 / 道友切磋坊美术资源预接线加载器。
    /// 对应 Codex 交付目录 UI/Ugui/WuxingExpansionsV1；素材尚未导入前安全返回 null，面板自动回退高可读道家青玉底色。
    /// </summary>
    public static class WuxingExpansionsUiArt
    {
        private const string Root = "UI/Ugui/WuxingExpansionsV1/";
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(32);

        public static Sprite LoadIcon(string key) => Load("icons/" + key);
        public static Sprite LoadControl(string key) => Load("ui/" + key);

        public static Sprite LoadElementIcon(uint element) => element switch
        {
            1u => LoadIcon("element_metal"),
            2u => LoadIcon("element_wood"),
            3u => LoadIcon("element_water"),
            4u => LoadIcon("element_fire"),
            5u => LoadIcon("element_earth"),
            _ => null,
        };

        public static Sprite LoadFormationIcon(uint formationId) => formationId switch
        {
            1u => LoadIcon("formation_1_tianfu"),
            2u => LoadIcon("formation_2_dizai"),
            3u => LoadIcon("formation_3_fengyang"),
            4u => LoadIcon("formation_4_yunchui"),
            5u => LoadIcon("formation_5_huyi"),
            _ => null,
        };

        public static Sprite LoadSigilCardFrame(uint rarity) => rarity switch
        {
            1u => LoadControl("sigil_card_common"),
            2u => LoadControl("sigil_card_spirit"),
            3u => LoadControl("sigil_card_immortal"),
            _ => null,
        };

        public static Sprite LoadFormationCompassBg() => LoadControl("formation_compass_bg");

        public static Sprite LoadDuelRuleIcon(string ruleKey) => ruleKey switch
        {
            "polarity" => LoadIcon("duel_rule_polarity"),
            "blitz" => LoadIcon("duel_rule_blitz"),
            "ban_cc" => LoadIcon("duel_rule_ban_cc"),
            "spectate" => LoadIcon("duel_rule_spectate"),
            _ => null,
        };

        private static Sprite Load(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            if (Cache.TryGetValue(relativePath, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>(Root + relativePath);
            if (sprite != null) Cache[relativePath] = sprite;
            return sprite;
        }
    }
}
