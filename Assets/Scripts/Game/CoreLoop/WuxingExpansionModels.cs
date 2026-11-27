using System;
using System.Collections.Generic;
using System.Text;

namespace MmorpgClient.Game.CoreLoop
{
    /// <summary>免费道家五行阵法静态说明（与服务端 wuxing_expansion_rules.h 逐项一致，零数值付费）。</summary>
    public readonly struct WuxingFormationDef
    {
        public uint Id { get; }
        public string Name { get; }
        public uint Element { get; }
        public uint CountersId { get; }
        public string BonusSummary { get; }
        public string IconKey { get; }

        public WuxingFormationDef(uint id, string name, uint element, uint countersId, string bonusSummary, string iconKey)
        {
            Id = id;
            Name = name;
            Element = element;
            CountersId = countersId;
            BonusSummary = bonusSummary;
            IconKey = iconKey;
        }
    }

    /// <summary>五行幻境 15 枚局内五行符印静态说明（出塔清空，仅作展示与构筑预览）。</summary>
    public readonly struct WuxingTowerSigilDef
    {
        public uint Id { get; }
        public string Name { get; }
        public uint Element { get; }
        public uint Rarity { get; } // 1=凡品, 2=灵品, 3=仙品
        public string Description { get; }

        public WuxingTowerSigilDef(uint id, string name, uint element, uint rarity, string description)
        {
            Id = id;
            Name = name;
            Element = element;
            Rarity = rarity;
            Description = description;
        }
    }

    /// <summary>
    /// 五行阵法 × 人宠五行相生 × 五行幻境 10 层爬塔 × 道友切磋坊 × 战报配装摘要的纯 C# 规则视图（零 UnityEngine 依赖）。
    /// </summary>
    public static class WuxingExpansionModels
    {
        public const uint ElementNone = 0u;
        public const uint ElementMetal = 1u;
        public const uint ElementWood = 2u;
        public const uint ElementWater = 3u;
        public const uint ElementFire = 4u;
        public const uint ElementEarth = 5u;

        public const uint MaxFormationId = 5u;
        public const uint TowerMaxFloors = 10u;
        public const uint RealmV2Version = 2u;
        public const uint RealmV2MaxNodes = 6u;
        public const uint RealmV2ResonanceThreshold = 3u;
        public const int MaxSavedBuildBookmarks = 12;

        public const uint RealmNodeNeedBranch = 0u;
        public const uint RealmNodeNormal = 1u;
        public const uint RealmNodeElite = 2u;
        public const uint RealmNodeBoss = 3u;
        public const uint RealmNodeEvent = 4u;
        public const uint RealmNodeRest = 5u;

        public const uint RealmEventNone = 0u;
        public const uint RealmEventEncounter = 1u;
        public const uint RealmEventRest = 2u;

        public const uint RealmBranchElite = 1u;
        public const uint RealmBranchSpecial = 2u;

        public const uint DefaultFairCharPresetId = 101u;
        public const uint DefaultFairPetPresetId = 201u;

        /// <summary>五行幻境 V2 15 枚机制改造型五行奇策（号段 601..643，与服务端 wuxing_expansion_rules.h 逐项一致）。</summary>
        public static readonly WuxingTowerSigilDef[] RealmBoons =
        {
            new WuxingTowerSigilDef(601u, "破盾返元", ElementMetal, 1u, "击碎敌方护盾返还 50% 法力并追加 60% 斩击"),
            new WuxingTowerSigilDef(602u, "绝锋连斩", ElementMetal, 2u, "暴击向另 1 名存活敌人溅射 40% 实际伤害"),
            new WuxingTowerSigilDef(603u, "庚金斩煞", ElementMetal, 3u, "每回合己方首次直击无视 35% 防御穿透增伤"),

            new WuxingTowerSigilDef(611u, "玄毒蔓延", ElementWood,  1u, "敌方持续伤害跳字向其余存活敌方传染 50%"),
            new WuxingTowerSigilDef(612u, "生机反哺", ElementWood,  2u, "获得治疗或护盾后下 1 次直击伤害 +20%"),
            new WuxingTowerSigilDef(613u, "灵宠协战", ElementWood,  3u, "主角攻击后出战灵宠免费追击 70% 普攻（每回合1次）"),

            new WuxingTowerSigilDef(621u, "润脉免耗", ElementWater, 1u, "每回合己方首招主动技能法力消耗降至 0"),
            new WuxingTowerSigilDef(622u, "寒渊延控", ElementWater, 2u, "硬控/封印成功持续 +1 回合；免疫则附 2 回合 18% 易伤"),
            new WuxingTowerSigilDef(623u, "镜花水月", ElementWater, 3u, "主角每第 3 次主动进攻技能以 55% 威力连发 1 次"),

            new WuxingTowerSigilDef(631u, "焚盾爆炎", ElementFire,  1u, "场上任意护盾碎裂对敌全体造成护盾 30% 真伤（封顶1800）"),
            new WuxingTowerSigilDef(632u, "孤注焚天", ElementFire,  2u, "气血低于 60% 按已损气血获至高 +35% 伤害与 +20% 暴击"),
            new WuxingTowerSigilDef(633u, "离火燃魂", ElementFire,  3u, "回合末对最高血敌人叠灼伤（至多3层，每层4%封顶1500）"),

            new WuxingTowerSigilDef(641u, "镇岳倍反", ElementEarth, 1u, "反震伤害翻倍（×200%）并附 2 回合破甲（降防15%）"),
            new WuxingTowerSigilDef(642u, "绝境磐石", ElementEarth, 2u, "每战首次气血跌破 35% 自动获得 25% 最大气血护盾"),
            new WuxingTowerSigilDef(643u, "坤元借力", ElementEarth, 3u, "开局获 18% 护盾，持盾攻击附当前护盾 25% 伤害（封顶1200）"),
        };

        public static readonly WuxingFormationDef[] Formations =
        {
            new WuxingFormationDef(1u, "天覆阵", ElementMetal, 2u, "直伤 +5% · 速度 +2%（克地载阵）", "formation_1_tianfu"),
            new WuxingFormationDef(2u, "地载阵", ElementEarth, 3u, "受击减伤 +6% · 开局 4% 护盾（克风扬阵）", "formation_2_dizai"),
            new WuxingFormationDef(3u, "风扬阵", ElementWood,  4u, "速度 +6% · 治疗与护盾 +4%（克云垂阵）", "formation_3_fengyang"),
            new WuxingFormationDef(4u, "云垂阵", ElementWater, 5u, "控制抗性 +8% · 回合初 +3% 法力（克虎翼阵）", "formation_4_yunchui"),
            new WuxingFormationDef(5u, "虎翼阵", ElementFire,  1u, "暴击率 +4% · 出战灵宠伤害 +8%（克天覆阵）", "formation_5_huyi"),
        };

        public static readonly WuxingTowerSigilDef[] TowerSigils =
        {
            new WuxingTowerSigilDef(101u, "金·破锋", ElementMetal, 1u, "全队直接伤害 +8%"),
            new WuxingTowerSigilDef(102u, "金·锐芒", ElementMetal, 2u, "暴击率 +12%，对带盾目标额外 +25% 伤害"),
            new WuxingTowerSigilDef(103u, "金·太白", ElementMetal, 3u, "暴击伤害 +20%，直接伤害 +14%"),

            new WuxingTowerSigilDef(201u, "木·生机", ElementWood,  1u, "治疗与护盾效果 +12%"),
            new WuxingTowerSigilDef(202u, "木·灵犀", ElementWood,  2u, "出战灵宠伤害 +18%"),
            new WuxingTowerSigilDef(203u, "木·青帝", ElementWood,  3u, "每回合初回复 5% 最大气血，灵宠伤害 +22%"),

            new WuxingTowerSigilDef(301u, "水·润泽", ElementWater, 1u, "每回合初回复 8% 最大法力"),
            new WuxingTowerSigilDef(302u, "水·归元", ElementWater, 2u, "受击回蓝 4%，控制抗性 +15%"),
            new WuxingTowerSigilDef(303u, "水·玄冥", ElementWater, 3u, "每回合初 +10% 法力、控制抗性 +20%、减伤 8%"),

            new WuxingTowerSigilDef(401u, "火·烈焰", ElementFire,  1u, "前 3 回合全队伤害 +14%"),
            new WuxingTowerSigilDef(402u, "火·焚心", ElementFire,  2u, "单体直伤技能额外 +12%"),
            new WuxingTowerSigilDef(403u, "火·业火", ElementFire,  3u, "伤害 +16%，暴击附带目标 3% 气血灼烧（上限 1500）"),

            new WuxingTowerSigilDef(501u, "土·厚土", ElementEarth, 1u, "开局获得 12% 最大气血护盾"),
            new WuxingTowerSigilDef(502u, "土·镇岳", ElementEarth, 2u, "受击反弹 12% 实扣伤害"),
            new WuxingTowerSigilDef(503u, "土·昆仑", ElementEarth, 3u, "开局 20% 护盾、受击减伤 12% 并反弹 15%"),
        };

        public static string ElementName(uint element) => element switch
        {
            ElementMetal => "金",
            ElementWood => "木",
            ElementWater => "水",
            ElementFire => "火",
            ElementEarth => "土",
            _ => "无",
        };

        public static string ElementIconKey(uint element) => element switch
        {
            ElementMetal => "element_metal",
            ElementWood => "element_wood",
            ElementWater => "element_water",
            ElementFire => "element_fire",
            ElementEarth => "element_earth",
            _ => string.Empty,
        };

        public static string RarityLabel(uint rarity) => rarity switch
        {
            1u => "凡品",
            2u => "灵品",
            3u => "仙品",
            _ => "符印",
        };

        public static uint ElementOfClass(uint classId) => classId == 0u ? ElementNone : ((classId - 1u) % 5u) + 1u;

        public static uint ElementOfPet(uint petTableId) => petTableId == 0u ? ElementNone : ((petTableId - 1u) % 5u) + 1u;

        public static bool ElementGenerates(uint producer, uint receiver) =>
            (producer == ElementMetal && receiver == ElementWater)
            || (producer == ElementWater && receiver == ElementWood)
            || (producer == ElementWood && receiver == ElementFire)
            || (producer == ElementFire && receiver == ElementEarth)
            || (producer == ElementEarth && receiver == ElementMetal);

        public static bool AreElementsMutuallyGenerating(uint first, uint second) =>
            ElementGenerates(first, second) || ElementGenerates(second, first);

        public static string DescribeSynergy(uint classId, uint petTableId)
        {
            uint ownerElem = ElementOfClass(classId);
            uint petElem = ElementOfPet(petTableId);
            if (ownerElem == ElementNone || petElem == ElementNone)
                return "出战灵宠后，若人宠五行相生则每 3 回合自动触发合击（伤害/治疗/护盾 +10%）";
            if (ElementGenerates(ownerElem, petElem))
                return $"人宠相生已就绪：{ElementName(ownerElem)}生{ElementName(petElem)} · 第 3/6/9 回合伤害/治疗/护盾 +10%";
            if (ElementGenerates(petElem, ownerElem))
                return $"人宠相生已就绪：{ElementName(petElem)}生{ElementName(ownerElem)} · 第 3/6/9 回合伤害/治疗/护盾 +10%";
            return $"当前主角[{ElementName(ownerElem)}] × 灵宠[{ElementName(petElem)}]未相生（金→水→木→火→土→金）";
        }

        public static bool TryFindFormation(uint formationId, out WuxingFormationDef def)
        {
            if (formationId >= 1u && formationId <= Formations.Length)
            {
                def = Formations[formationId - 1];
                return true;
            }
            def = default;
            return false;
        }

        public static string FormationName(uint formationId) =>
            TryFindFormation(formationId, out var def) ? def.Name : "未布阵";

        public static string FormationCounterLabel(uint myFormationId, uint enemyFormationId)
        {
            if (!TryFindFormation(myFormationId, out var mine) || !TryFindFormation(enemyFormationId, out var enemy))
                return string.Empty;
            if (mine.CountersId == enemy.Id) return "克制敌阵 · 直伤 +5%";
            if (enemy.CountersId == mine.Id) return "被敌阵克制";
            return string.Empty;
        }

        public static bool TryFindRealmBoon(uint boonId, out WuxingTowerSigilDef def)
        {
            for (int i = 0; i < RealmBoons.Length; ++i)
            {
                if (RealmBoons[i].Id == boonId)
                {
                    def = RealmBoons[i];
                    return true;
                }
            }
            def = default;
            return false;
        }

        public static bool IsRealmBoonId(uint boonId) => TryFindRealmBoon(boonId, out _);

        public static bool TryFindSigil(uint sigilId, out WuxingTowerSigilDef def)
        {
            if (TryFindRealmBoon(sigilId, out def)) return true;
            for (int i = 0; i < TowerSigils.Length; ++i)
            {
                if (TowerSigils[i].Id == sigilId)
                {
                    def = TowerSigils[i];
                    return true;
                }
            }
            def = default;
            return false;
        }

        public static uint ComputeActiveResonanceElement(IEnumerable<uint> boonIds)
        {
            if (boonIds == null) return ElementNone;
            var counts = new uint[6];
            foreach (uint id in boonIds)
            {
                if (TryFindRealmBoon(id, out var def) && def.Element >= 1u && def.Element <= 5u)
                    counts[def.Element] += 1u;
            }
            for (uint elem = 1u; elem <= 5u; ++elem)
            {
                if (counts[elem] >= RealmV2ResonanceThreshold) return elem;
            }
            return ElementNone;
        }

        public static string ResonanceName(uint element) => element switch
        {
            ElementMetal => "破军锋芒",
            ElementWood => "生生不息",
            ElementWater => "流云千变",
            ElementFire => "焚城烈焰",
            ElementEarth => "不动如山",
            _ => string.Empty,
        };

        public static string ResonanceDescription(uint element) => element switch
        {
            ElementMetal => "对气血 ≤ 20% 非首领（首领 ≤ 12%）直接斩杀",
            ElementWood => "回合初敌方持续伤害额外结算 50%，己方回复 6% 气血",
            ElementWater => "己方主动技能冷却 -1 回合（最低1），法力回复 +30%",
            ElementFire => "己方直击附带 15% 溅射灼伤扩散至相邻敌方",
            ElementEarth => "护盾吸收伤害同样触发反震，开局护盾不自然衰减",
            _ => string.Empty,
        };

        public static string FormatResonanceProgressHint(IEnumerable<uint> boonIds)
        {
            if (boonIds == null) return "集齐 3 枚同五行奇策自动激活五行奇策共鸣";
            var counts = new uint[6];
            foreach (uint id in boonIds)
            {
                if (TryFindRealmBoon(id, out var def) && def.Element >= 1u && def.Element <= 5u)
                    counts[def.Element] += 1u;
            }
            for (uint elem = 1u; elem <= 5u; ++elem)
            {
                if (counts[elem] >= RealmV2ResonanceThreshold)
                    return $"已激活 {ElementName(elem)}系共鸣「{ResonanceName(elem)}」：{ResonanceDescription(elem)}";
            }
            uint bestElem = ElementNone;
            uint bestCount = 0u;
            for (uint elem = 1u; elem <= 5u; ++elem)
            {
                if (counts[elem] > bestCount)
                {
                    bestCount = counts[elem];
                    bestElem = elem;
                }
            }
            if (bestElem == ElementNone) return "集齐 3 枚同五行奇策自动激活五行奇策共鸣";
            uint need = RealmV2ResonanceThreshold - bestCount;
            return $"再选 {need} 枚{ElementName(bestElem)}系奇策即可激活「{ResonanceName(bestElem)}」共鸣";
        }

        public static bool CanUpgradeBoon(uint boonId, IEnumerable<uint> owned)
        {
            if (!TryFindRealmBoon(boonId, out var cur) || cur.Rarity >= 3u) return false;
            var ownedSet = owned != null ? new HashSet<uint>(owned) : new HashSet<uint>();
            for (int i = 0; i < RealmBoons.Length; ++i)
            {
                var candidate = RealmBoons[i];
                if (candidate.Element == cur.Element && candidate.Rarity > cur.Rarity && !ownedSet.Contains(candidate.Id))
                    return true;
            }
            return false;
        }

        public static string RealmNodeKindLabel(uint nodeKind) => nodeKind switch
        {
            RealmNodeNeedBranch => "待选分岔",
            RealmNodeNormal => "普通战",
            RealmNodeElite => "精英强敌",
            RealmNodeBoss => "终局首领",
            RealmNodeEvent => "奇遇事件",
            RealmNodeRest => "秘境休整",
            _ => "试炼节点",
        };

        public static string FairCharacterPresetName(uint presetId) => presetId switch
        {
            101u => "剑修·力",
            102u => "法宗·灵",
            103u => "道医·体",
            _ => "剑修·力",
        };

        public static string FairPetPresetName(uint presetId) => presetId switch
        {
            201u => "猛攻灵宠",
            202u => "法爆灵宠",
            203u => "护主血宠",
            _ => "猛攻灵宠",
        };

        public static int CountClearedNodes(uint weeklyClearedMask, uint maxNodes = RealmV2MaxNodes)
        {
            int limit = (int)Math.Min(maxNodes > 0u ? maxNodes : RealmV2MaxNodes, 31u);
            int count = 0;
            for (int bit = 0; bit < limit; ++bit)
            {
                if ((weeklyClearedMask & (1u << bit)) != 0u) ++count;
            }
            return count;
        }

        public static bool IsNodeCleared(uint weeklyClearedMask, uint nodeIndex, uint maxNodes = RealmV2MaxNodes) =>
            nodeIndex >= 1u && nodeIndex <= maxNodes && (weeklyClearedMask & (1u << (int)(nodeIndex - 1u))) != 0u;

        public static int CountClearedFloors(uint weeklyClearedMask)
        {
            int count = 0;
            for (int bit = 0; bit < TowerMaxFloors; ++bit)
            {
                if ((weeklyClearedMask & (1u << bit)) != 0u) ++count;
            }
            return count;
        }

        public static bool IsFloorCleared(uint weeklyClearedMask, uint floor) =>
            floor >= 1u && floor <= TowerMaxFloors && (weeklyClearedMask & (1u << (int)(floor - 1u))) != 0u;

        public static string FormatCustomDuelRulesSummary(global::CustomDuelRules rules)
        {
            if (rules == null) return "标准切磋 · 真实养成";
            var tags = new List<string>(5)
            {
                rules.UseFreePreset ? "公平 3×3 模板" : "真实养成",
            };
            if (rules.EnablePolarityRotation) tags.Add("物法易势");
            if (rules.RoundLimitOverride > 0u) tags.Add($"限 {rules.RoundLimitOverride} 回合");
            if (rules.BanControlSkills) tags.Add("禁控直伤");
            if (rules.SkillBanSlotsPerTeam > 0u) tags.Add($"每方 {rules.SkillBanSlotsPerTeam} 禁招·1 护招");
            if (rules.AllowPublicSpectate) tags.Add("公开观战");
            return string.Join(" · ", tags);
        }

        public static string FormatBuildSummary(global::BattleActorBuildSummary build)
        {
            if (build == null) return string.Empty;
            var sb = new StringBuilder();
            string name = string.IsNullOrWhiteSpace(build.Name) ? $"玩家{build.PlayerId}" : build.Name;
            sb.Append(name);
            sb.Append($"（{ElementName(build.WuxingElement)}行 · {FormationName(build.FormationId)}");
            if (build.SeasonSealId > 0u) sb.Append($" · 印#{build.SeasonSealId}");
            if (build.ActivePetTableId > 0u)
            {
                string petName = string.IsNullOrWhiteSpace(build.ActivePetName) ? $"灵宠#{build.ActivePetTableId}" : build.ActivePetName;
                sb.Append($" · 宠:{petName}[{ElementName(build.ActivePetElement)}]");
                if (AreElementsMutuallyGenerating(build.WuxingElement, build.ActivePetElement))
                    sb.Append("★相生");
            }
            if (build.UsedFreePreset) sb.Append(" · 公平3×3");
            sb.Append("）");
            return sb.ToString();
        }
    }
}
