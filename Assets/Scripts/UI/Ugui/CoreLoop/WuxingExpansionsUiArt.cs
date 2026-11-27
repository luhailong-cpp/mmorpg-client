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
        private const string RealmV2Root = "UI/Ugui/WuxingRealmV2/";
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(48);

        public static Sprite LoadIcon(string key) => Load(Root, "icons/" + key);
        public static Sprite LoadControl(string key) => Load(Root, "ui/" + key);

        // 五行幻境 V2（8 张 Codex 透明切片预接线，素材入库前返回 null 自动回退）
        public static Sprite LoadRealmEntryIcon() => Load(RealmV2Root, "hud_entry_roguelike_realm");
        public static Sprite LoadRealmRouteBoardBg() => Load(RealmV2Root, "realm_route_board_bg");
        public static Sprite LoadRealmBoonCardBg() => Load(RealmV2Root, "realm_boon_card_bg");
        public static Sprite LoadRealmResonanceBanner() => Load(RealmV2Root, "realm_resonance_banner");

        public static Sprite LoadRealmNodeIcon(uint nodeKind) => nodeKind switch
        {
            1u => Load(RealmV2Root, "realm_node_normal"),
            2u => Load(RealmV2Root, "realm_node_elite"),
            3u => Load(RealmV2Root, "realm_node_boss"),
            4u or 5u => Load(RealmV2Root, "realm_node_event"),
            _ => null,
        };

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

        private static Sprite Load(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            string fullKey = root + relativePath;
            if (Cache.TryGetValue(fullKey, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>(fullKey);
            if (sprite != null) Cache[fullKey] = sprite;
            return sprite;
        }
    }
}
