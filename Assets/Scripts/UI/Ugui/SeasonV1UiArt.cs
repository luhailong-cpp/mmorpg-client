using System;
using System.Collections.Generic;
using MmorpgClient.Game.Season;
using MmorpgClient.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 「破阵之季 · 赛季印与赛季技能」专属美术资源加载器（来源 D:\work\image\designs\season-ui-v1）。
    /// 仅加载独立图标与无字透明切片（含精确横向三段拉伸），严禁在运行时整屏拉伸预览图。
    /// </summary>
    public static class SeasonV1UiArt
    {
        public const string ControlsRoot = "UI/Ugui/SeasonV1/";
        public const string SealIconsPrefix = "UI/SeasonSeals/";
        public const string SkillIconsPrefix = "UI/Skills/";
        public const string HonorIconsPrefix = "UI/SeasonHonors/";

        public static readonly Color BadgeIvoryText = new Color32(0xFF, 0xF5, 0xDC, 255);
        public static readonly Color JadeHighlight = new Color32(0x1D, 0x5B, 0x47, 255);
        public static readonly Color EquippedCardTint = new Color(0.86f, 0.94f, 0.87f, 0.98f);
        public static readonly Color NormalCardTint = new Color(0.97f, 0.94f, 0.87f, 0.96f);

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        public static Sprite LoadControl(string name)
            => string.IsNullOrEmpty(name) ? null : LoadCached(ControlsRoot + name);

        public static Sprite LoadHudSealIcon() => LoadControl("hud_icon_season_seal");

        public static Sprite LoadSealIcon(uint sealId, string iconKey = null)
        {
            if (string.IsNullOrWhiteSpace(iconKey) && sealId != 0u)
                iconKey = SeasonSealTableManager.Instance?.FindById(sealId)?.IconKey;
            string path = SeasonModels.NormalizeIconResource(iconKey, SealIconsPrefix, sealId);
            return LoadCached(path) ?? LoadHudSealIcon();
        }

        public static Sprite LoadSkillIcon(uint skillId, string iconKey = null)
        {
            if (string.IsNullOrWhiteSpace(iconKey) && skillId != 0u)
                iconKey = SkillTableManager.Instance?.FindById(skillId)?.IconKey;
            string path = SeasonModels.NormalizeIconResource(iconKey, SkillIconsPrefix, skillId);
            return LoadCached(path);
        }

        /// <summary>加载本季零数值永久纪念徽章图标（优先使用表/快照 iconKey，缺省按赛季号映射正式徽章，无图回落玉印）。</summary>
        public static Sprite LoadHonorBadgeIcon(uint seasonId, string iconKey = null)
        {
            string effectiveKey = !string.IsNullOrWhiteSpace(iconKey)
                ? iconKey
                : seasonId switch
                {
                    1u => HonorIconsPrefix + "honor_s1_pozhen",
                    2u => HonorIconsPrefix + "honor_s2_xuanmai",
                    3u => HonorIconsPrefix + "honor_s3_lihuo",
                    4u => HonorIconsPrefix + "honor_s4_qingmu",
                    5u => HonorIconsPrefix + "honor_s5_xuanshui",
                    6u => HonorIconsPrefix + "honor_s6_houtu",
                    7u => HonorIconsPrefix + "honor_s7_gengjin",
                    8u => HonorIconsPrefix + "honor_s8_taiyin",
                    9u => HonorIconsPrefix + "honor_s9_tianlei",
                    10u => HonorIconsPrefix + "honor_s10_hunyuan",
                    _ => null,
                };
            string path = SeasonModels.NormalizeIconResource(effectiveKey, HonorIconsPrefix, seasonId);
            return LoadCached(path) ?? LoadHudSealIcon();
        }

        /// <summary>创建支持横向三段拉伸的赛季专属切片图像（无图时回退纯色）。</summary>
        public static Image CreateHorizontalSlice(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            string controlName,
            Color fallback,
            bool raycast = false)
        {
            var sprite = LoadControl(controlName);
            return SeasonEconomyUiArt.CreateSlicedOrSimple(parent, name, x, y, width, height, sprite, fallback, raycast);
        }

        /// <summary>创建固定比例图标/槽框（保持原图纵横比）。</summary>
        public static Image CreateFixedIcon(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            Sprite sprite,
            Color fallback,
            bool raycast = false)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, sprite, raycast);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = sprite != null ? Color.white : fallback;
            return img;
        }

        /// <summary>在技能图标右上角叠放「季」小朱砂圆印角标（供技能书与战斗选招共用）。</summary>
        public static RectTransform CreateSeasonCornerBadge(
            Transform parent,
            string name,
            float x,
            float y,
            float size)
        {
            var bg = CreateFixedIcon(parent, name, x, y, size, size,
                LoadControl("season_badge_corner"), new Color(0.68f, 0.22f, 0.16f, 0.96f), false);
            var label = QdaoUguiFactory.CreateText(name + "Text", bg.transform,
                0f, 0f, size, size, "季", Mathf.Max(11f, size * 0.56f), BadgeIvoryText, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            return bg.rectTransform;
        }

        /// <summary>创建带无字徽章底板的水平三段标签（如「当前佩戴」「本季免费」）。</summary>
        public static RectTransform CreateBadgePill(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            string controlName,
            string text,
            float fontSize,
            Color fallbackBg,
            Color? textColor = null)
        {
            var bg = CreateHorizontalSlice(parent, name, x, y, width, height, controlName, fallbackBg, false);
            var label = QdaoUguiFactory.CreateText(name + "Label", bg.transform,
                8f, 0f, Mathf.Max(16f, width - 16f), height, text, fontSize,
                textColor ?? BadgeIvoryText, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(11f, fontSize * 0.65f);
            label.fontSizeMax = fontSize;
            label.raycastTarget = false;
            return bg.rectTransform;
        }

        private static Sprite LoadCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) Cache[path] = sprite;
            return sprite;
        }
    }
}
