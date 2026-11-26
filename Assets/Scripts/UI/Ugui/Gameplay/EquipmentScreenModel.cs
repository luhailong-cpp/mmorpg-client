using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 装备页的纯展示模型。数值和可用操作来自服务器，只整理文字、语义对比与实例数量，
    /// 不计算战斗结果或打造资格。返回纯文本，调用方须关闭富文本；所有方法均不修改快照。
    /// </summary>
    public static class EquipmentScreenModel
    {
        private const string IconRoot = "UI/Ugui/Equipment20261117/Icons/";
        private const string CraftIconRoot = "UI/Ugui/CraftQualitySkill/Icons/";

        public static IReadOnlyList<string> DetailLines(BagItemInfo item)
        {
            var lines = new List<string>();
            if (item == null) return lines;
            if (!string.IsNullOrWhiteSpace(item.Description)) lines.Add(item.Description);
            if (item.EquipKind != 0)
            {
                lines.Add(EquipBagModel.LevelRequirement(item));
                lines.Add(EquipBagModel.ClassRequirement(item));
            }

            // 旧装备的基础、随机属性及宝石继续使用既有格式，不能把旧整数百分数再除以100。
            foreach (var line in EquipDisplay.LinesOf(item)) lines.Add(EquipDisplay.FormatLine(line));
            foreach (var slot in item.FixedAffixSlots.OrderBy(entry => entry.Slot).ThenBy(entry => entry.Pool))
            {
                string label = $"{PoolName(slot.Pool)} {Number(slot.Slot)}";
                if (slot.Selected == null)
                {
                    lines.Add($"{label}：空位（未选择）");
                    continue;
                }
                var affix = slot.Selected;
                string cap = affix.SourceCapBps == 0 ? string.Empty :
                    $"（装备同类上限 {FixedAmount(affix.SourceCapBps, affix.Unit)}）";
                string quality = FormatAffixQualityTag(affix);
                lines.Add($"{label}：{AffixName(affix)} {AffixValue(affix)}{quality}{cap}");
                string pity = FormatPolishPity(affix);
                if (!string.IsNullOrEmpty(pity)) lines.Add(pity);
                if (!string.IsNullOrWhiteSpace(affix.Description)) lines.Add(affix.Description);
            }

            if (item.Artifact != null)
            {
                lines.Add($"法宝：{ArtifactName(item.Artifact)}");
                if (!string.IsNullOrWhiteSpace(item.Artifact.Description)) lines.Add(item.Artifact.Description);
                lines.Add(ArtifactLimits(item.Artifact));
            }
            if (item.EquipKind != 0) AddEnhanceLines(item, lines);
            foreach (var socket in EquipDisplay.SocketsOf(item)) lines.Add(EquipDisplay.FormatSocketLine(socket));
            if (item.GemAttr != null) lines.Add(EquipDisplay.FormatGemAttr(item.GemAttr));
            if (!string.IsNullOrWhiteSpace(item.CrafterName)) lines.Add($"宗师·{item.CrafterName} 亲手铸造");
            return lines;
        }

        /// <summary>
        /// 按属性身份与单位对齐；旧属性认 attr_id/percent，固定词条认 effect/effect_param/unit。
        /// 名称只用于展示，同名不同效果不合并。只比较这两件的贡献，不推算角色面板或全身封顶后的收益。
        /// </summary>
        public static IReadOnlyList<string> CompareLines(BagItemInfo current, BagItemInfo candidate)
        {
            var lines = new List<string>();
            if (current == null && candidate == null) return lines;
            var rows = new Dictionary<(uint Source, uint Id, uint Parameter, uint Unit), ComparisonRow>();
            AddComparisonValues(current, false, rows);
            AddComparisonValues(candidate, true, rows);
            foreach (var entry in rows.OrderBy(pair => pair.Key.Source).ThenBy(pair => pair.Key.Id)
                .ThenBy(pair => pair.Key.Parameter).ThenBy(pair => pair.Key.Unit))
            {
                var row = entry.Value;
                bool fixedValue = entry.Key.Source != 0;
                string before = ComparisonAmount(row.Current, fixedValue, entry.Key.Unit);
                string after = ComparisonAmount(row.Candidate, fixedValue, entry.Key.Unit);
                decimal difference = row.Candidate - row.Current;
                string delta = difference == 0 ? "不变" :
                    (difference > 0 ? "+" : string.Empty) + ComparisonAmount(difference, fixedValue, entry.Key.Unit);
                lines.Add($"{row.Name}：{before} → {after}（{delta}）");
            }

            var pools = new SortedSet<uint>();
            if (current != null) foreach (var slot in current.FixedAffixSlots) pools.Add(slot.Pool);
            if (candidate != null) foreach (var slot in candidate.FixedAffixSlots) pools.Add(slot.Pool);
            foreach (uint pool in pools)
            {
                int before = EmptySlots(current, pool), after = EmptySlots(candidate, pool);
                if (before != 0 || after != 0)
                    lines.Add($"{PoolName(pool)}空位：{Number(before)} → {Number(after)}");
            }

            if (current?.Artifact != null || candidate?.Artifact != null)
            {
                lines.Add(current?.Artifact == null ? "当前法宝：无" :
                    $"当前法宝：{ArtifactName(current.Artifact)}；{ArtifactLimits(current.Artifact)}");
                lines.Add(candidate?.Artifact == null ? "候选法宝：无" :
                    $"候选法宝：{ArtifactName(candidate.Artifact)}；{ArtifactLimits(candidate.Artifact)}");
            }
            if ((current?.EnhanceMaxLevel ?? 0) != 0 || (candidate?.EnhanceMaxLevel ?? 0) != 0 ||
                (current?.EnhanceLevel ?? 0) != 0 || (candidate?.EnhanceLevel ?? 0) != 0)
                lines.Add($"强化等级：+{Number(current?.EnhanceLevel ?? 0)} → +{Number(candidate?.EnhanceLevel ?? 0)}");
            return lines;
        }

        public static string PoolName(uint pool) => pool switch
        {
            1 => "方向词条",
            2 => "效能词条",
            3 => "通用词条",
            _ => $"词条池 · {Number(pool)}",
        };

        /// <summary>万分比转换为相对百分数或百分点；减耗显示负号，不把未知单位猜成百分数。</summary>
        public static string AffixValue(EquipFixedAffixInfo affix)
        {
            if (affix == null) return string.Empty;
            string sign = affix.Effect == 13 && affix.Unit == 1 ? "-" : "+";
            return sign + FixedAmount(affix.ValueBps, affix.Unit);
        }

        /// <summary>词条满值显示文本；无满值配置时回落现值。</summary>
        public static string AffixMaxValue(EquipFixedAffixInfo affix)
        {
            if (affix == null) return string.Empty;
            uint maxBps = affix.MaxValueBps != 0 ? affix.MaxValueBps : affix.ValueBps;
            string sign = affix.Effect == 13 && affix.Unit == 1 ? "-" : "+";
            return sign + FixedAmount(maxBps, affix.Unit);
        }

        public static string QualityTierName(uint tier, string serverName = null)
        {
            if (!string.IsNullOrWhiteSpace(serverName)) return serverName;
            return tier switch
            {
                1 => "凡品",
                2 => "良品",
                3 => "上品",
                4 => "珍品",
                5 => "极品",
                _ => string.Empty,
            };
        }

        /// <summary>词条品相附加标签；旧快照未下发品相字段时返回空串以保持既有文案不变。</summary>
        public static string FormatAffixQualityTag(EquipFixedAffixInfo affix)
        {
            if (affix == null) return string.Empty;
            if (affix.PolishExempt) return " [固定满值]";
            if (affix.QualityTier == 0) return string.Empty;
            string name = QualityTierName(affix.QualityTier, affix.QualityName);
            string maxPart = affix.MaxValueBps != 0 && affix.MaxValueBps != affix.ValueBps
                ? $" / 满值 {AffixMaxValue(affix)}" : string.Empty;
            return $" [{name}{maxPart}]";
        }

        /// <summary>单条词条的打磨保底进度提示；豁免或已满极品返回空串。</summary>
        public static string FormatPolishPity(EquipFixedAffixInfo affix)
        {
            if (affix == null || affix.PolishExempt || affix.QualityTier == 0 || affix.QualityTier >= 5 || affix.PolishPityLimit == 0)
                return string.Empty;
            if (affix.PolishPityCount + 1 >= affix.PolishPityLimit)
                return $"打磨保底：{Number(affix.PolishPityCount)} / {Number(affix.PolishPityLimit)}（本次打磨 100% 必出极品）";
            return $"打磨保底：{Number(affix.PolishPityCount)} / {Number(affix.PolishPityLimit)}（只升不降）";
        }

        /// <summary>合规概率公示行（全取服务端 quality_tiers，不本地硬编码权重）。</summary>
        public static IReadOnlyList<string> FormatQualityOddsLines(GetEquipmentCraftRulesResponse rules)
        {
            var lines = new List<string>();
            if (rules == null || rules.QualityTiers.Count == 0)
            {
                lines.Add("品相概率表暂未同步，请刷新。");
                return lines;
            }
            uint masteryTier = rules.ForgeMastery?.Tier ?? 1;
            foreach (var row in rules.QualityTiers.OrderBy(t => t.QualityTier))
            {
                uint activeRate = masteryTier >= 3 ? row.MasterRateBps : masteryTier == 2 ? row.ArtisanRateBps : row.ApprenticeRateBps;
                string name = QualityTierName(row.QualityTier, row.Name);
                lines.Add($"{name}（满值 {Number(row.RatioPct)}%）：当前 {EquipDisplay.FormatSuccessRate(activeRate)}（学徒 {EquipDisplay.FormatSuccessRate(row.ApprenticeRateBps)} / 巧匠 {EquipDisplay.FormatSuccessRate(row.ArtisanRateBps)} / 宗师 {EquipDisplay.FormatSuccessRate(row.MasterRateBps)}）");
            }
            lines.Add("规则：只升不降，累计第 8 次必出极品（100%）；速度词条固定满值，无需打磨；换词条 100% 保留当前品相与保底进度。");
            return lines;
        }

        /// <summary>铸器手艺轻徽章文案（无体力条、无日常红点）。</summary>
        public static string FormatForgeMastery(CraftMasteryInfo mastery)
        {
            if (mastery == null || mastery.Tier == 0) return "铸器手艺：未同步";
            string name = string.IsNullOrWhiteSpace(mastery.TierName) ? $"档位 {Number(mastery.Tier)}" : mastery.TierName;
            string progress = mastery.NextTierExp == 0 ? "已满宗师" : $"{Number(mastery.Exp)}/{Number(mastery.NextTierExp)}";
            return $"铸器手艺 · {name}（极品率 {EquipDisplay.FormatSuccessRate(mastery.TopQualityRateBps)} · {progress}）";
        }

        /// <summary>打磨 / 换词条预览成本汇总（含可选定星保底符、银两、名匠手工费与防染毒提示）。</summary>
        public static string FormatCraftPreviewSummary(EquipmentCraftPreview preview)
        {
            if (preview == null) return "选择词条后读取当前消耗与资格";
            var parts = new List<string>();
            if (preview.Material != null && preview.Material.Count != 0)
            {
                string mat = $"{preview.Material.Name} 需要 {Number(preview.Material.Count)} · 持有 {Number(preview.Material.Owned)} · 可用 {Number(preview.Material.Available)}";
                if (preview.Material.Available < preview.Material.Count)
                    mat += $"（缺 {Number(preview.Material.Count - preview.Material.Available)}）";
                parts.Add(mat);
            }
            if (preview.ExtraMaterial != null && preview.ExtraMaterial.Count != 0)
            {
                string extra = $"{preview.ExtraMaterial.Name} 需要 {Number(preview.ExtraMaterial.Count)} · 可用 {Number(preview.ExtraMaterial.Available)}";
                parts.Add(extra);
            }
            if (preview.GoldCost != 0 || preview.ProxyFeeGold != 0)
            {
                string gold = $"银两 {Number(preview.GoldCost)}";
                if (preview.ProxyFeeGold != 0)
                {
                    string master = string.IsNullOrWhiteSpace(preview.ProxyMasterName) ? "名匠" : preview.ProxyMasterName;
                    gold += $" + {master}手工费 {Number(preview.ProxyFeeGold)} 银两（零风险代打，装备不离身）";
                }
                parts.Add(gold);
            }
            if (preview.WillBindItem)
                parts.Add("注意：本次将消耗绑定材料，完成后装备永久转为仅自用");
            return string.Join("\n", parts);
        }

        /// <summary>
        /// 只展示单件效果。条件由法宝类型定义，数值、有效窗口与限次始终读取服务器字段，
        /// 不在目录中把两件同名法宝的次数或收益推导成一件。
        /// </summary>
        public static string ArtifactLimits(EquipArtifactInfo artifact)
        {
            if (artifact == null) return string.Empty;
            string percent = FixedAmount(artifact.BonusBps, 1);
            string points = FixedAmount(artifact.BonusBps, 2);
            var parts = new List<string>();
            switch (artifact.EffectId)
            {
                case 1:
                case 2:
                    string type = artifact.EffectId == 1 ? "物理" : "法术";
                    parts.Add($"单件：正常行动仅主目标首段{type}直接伤害，额外削减该段伤害 {percent} 的合格剩余护盾");
                    parts.Add("基数取分担后、护盾吸收前伤害；仅原有固定容量盾，最多扣剩余盾量，不溢出到气血");
                    parts.Add("不复制到副目标，同动作后续伤害段不能补触发；实际额外扣盾为0不计次");
                    break;
                case 3:
                    parts.Add($"单件：正常行动对主目标施加障碍被概率抵抗后，下一回合首次同类型主目标命中 +{points}");
                    parts.Add("仅封灵、封法、封物、混乱、昏睡、冰冻、中毒，限该技能的主障碍类型");
                    parts.Add("只在下一回合有效；每件最多保留一份补偿，不覆盖、不排队；补偿后的失败不再生成补偿");
                    parts.Add("免疫、无效目标或未进入概率判定不生成；死亡清除待用补偿，已用生成次数不退还");
                    break;
                case 4:
                    parts.Add($"单件：正常行动主目标治疗前气血不高于最大气血 {FixedAmount(artifact.HealthThresholdBps, 1)}，首段即时治疗的施放加成层 +{points}");
                    parts.Add("与装备治疗加成同层相加，按缺血封顶；不增加护盾，不作用持续恢复、复活、吸血或药品");
                    parts.Add("没有增加实际治疗时不计次");
                    break;
                case 5:
                    parts.Add($"单件：正常施放人物独立护盾技能，主目标新护盾容量加成层 +{points}");
                    parts.Add("与装备护盾加成同层相加，以完整新容量判断覆盖；实际创建或替换且增加容量才计次");
                    parts.Add("不修补旧盾、不延长时间、不增加目标，不由治疗、其他法宝或宝宝护罩触发");
                    break;
                case 6:
                    parts.Add($"单件：敌方直接伤害经其他防御、护盾和分担后，减少实际将扣气血伤害的 {percent}，每次最多减免入战最大气血的 {FixedAmount(artifact.CapBps, 1)}");
                    parts.Add("减免发生在扣血前，不是新护盾；不作用持续伤害、反弹、援护转伤或资源消耗，实际减免为0不计次");
                    break;
                case 7:
                    parts.Add($"单件：原指令为普通防御且实际执行后，回复入战最大法力的 {percent}，不超过当前法力缺口");
                    parts.Add("不受临时法力上限、治疗或护盾加成放大；被控跳过、技能附带防御或施法回蓝不触发，实际回复为0不计次");
                    break;
                case 8:
                    parts.Add($"单件：敌方正常行动首次对本人进行合法硬控制概率判定时，对应抵抗 +{points}，进入判定即计次");
                    parts.Add("仅封灵、封法、封物、混乱、昏睡、冰冻；副目标也可生效；免疫、无效目标或基础概率为0不计次");
                    parts.Add("不包括中毒，不解除已有控制、不缩短控制时间、不增加第二次抵抗判定；最终仍被控制也不退次数");
                    break;
                default:
                    parts.Add($"单件：效果类型 {Number(artifact.EffectId)}；强度万分比 {Number(artifact.BonusBps)}；上限万分比 {Number(artifact.CapBps)}；气血阈值万分比 {Number(artifact.HealthThresholdBps)}");
                    break;
            }
            if (artifact.PerRoundLimit != 0)
                parts.Add(artifact.EffectId == 3 ? $"每回合最多生成 {Number(artifact.PerRoundLimit)} 次补偿" :
                    $"每回合最多 {Number(artifact.PerRoundLimit)} 次");
            if (artifact.BattleLimit != 0)
                parts.Add(artifact.EffectId == 3 ? $"每场最多生成 {Number(artifact.BattleLimit)} 次补偿" :
                    $"每场最多 {Number(artifact.BattleLimit)} 次");
            if (artifact.ActiveRounds != 0)
                parts.Add($"仅开战前 {Number(artifact.ActiveRounds)} 回合有效，第 {Number(artifact.ActiveRounds)} 回合结束失效");
            if ((artifact.EffectId >= 1 && artifact.EffectId <= 5) || artifact.EffectId == 8)
                parts.Add("正常行动不含反击、追击、开场、反弹、连击追加或混乱强制友伤");
            parts.Add("死亡、复活和重连不刷新已用次数；不增加额外行动");
            return string.Join("；", parts) + "。";
        }

        /// <summary>仅列出服务器下发配方；同名不同模板保留，不根据已拥有数量隐藏再次打造。</summary>
        public static List<EquipmentCraftRecipe> RecipeList(GetEquipmentCraftRulesResponse rules, uint equipKind = 0)
        {
            if (rules == null || (rules.ErrorMessage != null && rules.ErrorMessage.Id != 0))
                return new List<EquipmentCraftRecipe>();
            return rules.Recipes.Where(recipe => recipe.Product != null && recipe.Product.ConfigId != 0 &&
                    recipe.Product.EquipKind != 0 && (equipKind == 0 || recipe.Product.EquipKind == equipKind))
                .OrderBy(recipe => recipe.Product.EquipKind).ThenBy(recipe => recipe.Product.EquipLevel)
                .ThenBy(recipe => recipe.Product.ConfigId).ToList();
        }

        /// <summary>仅 V1 基础四部位与首饰（低于 80 级）支持纯银两原地无损升档。</summary>
        public static bool CanUpgradeTier(BagItemInfo item) =>
            item != null && item.ItemId != 0 && item.Count != 0
            && (item.EquipProfile == 1u || item.EquipProfile == 2u)
            && item.EquipKind != 0 && item.EquipLevel < 80u;

        /// <summary>从服务端打造规则中筛出同类型、同部位且等级高于当前装备的可升档目标模板。</summary>
        public static List<BagItemInfo> FindUpgradeTierTargets(BagItemInfo item, GetEquipmentCraftRulesResponse rules)
        {
            if (!CanUpgradeTier(item) || rules == null || (rules.ErrorMessage != null && rules.ErrorMessage.Id != 0))
                return new List<BagItemInfo>();
            var seen = new HashSet<uint>();
            return rules.Recipes
                .Select(recipe => recipe?.Product)
                .Where(product => product != null && product.ConfigId != 0
                    && product.EquipProfile == item.EquipProfile
                    && product.EquipKind == item.EquipKind
                    && product.EquipLevel > item.EquipLevel
                    && seen.Add(product.ConfigId))
                .OrderBy(product => product.EquipLevel)
                .ThenBy(product => product.ConfigId)
                .ToList();
        }

        /// <summary>默认选中不超过角色当前等级的最高档位；若全部高于角色等级则选中下一档。</summary>
        public static BagItemInfo PickDefaultUpgradeTarget(IReadOnlyList<BagItemInfo> targets, uint playerLevel)
        {
            if (targets == null || targets.Count == 0) return null;
            BagItemInfo best = null;
            for (int i = 0; i < targets.Count; i++)
            {
                var candidate = targets[i];
                if (candidate == null) continue;
                if (playerLevel > 0 && candidate.EquipLevel <= playerLevel)
                    best = candidate;
            }
            return best ?? targets[0];
        }

        /// <summary>人物背包与装备栏的独立实例数；同 GUID 的两份快照只计一次，不把堆数量当装备件数。</summary>
        public static int OwnedCount(BagInfo bag, BagInfo worn, uint configId)
        {
            var ids = new HashSet<ulong>();
            CollectInstances(worn, configId, ids);
            CollectInstances(bag, configId, ids);
            return ids.Count;
        }

        /// <summary>装备容器中的独立实例数；布局的重复占位不产生第二件物品。</summary>
        public static int EquippedCount(BagInfo worn, uint configId)
        {
            var ids = new HashSet<ulong>();
            CollectInstances(worn, configId, ids);
            return ids.Count;
        }

        /// <summary>本批独立图标的 Resources 路径。未知或旧装备返回 null，由旧图标路径兜底。</summary>
        public static string IconKey(BagItemInfo item)
        {
            if (item == null) return null;
            if (item.EquipKind == 0)
            {
                if (item.ConfigId == 3001) return IconRoot + "material_crafting";
                if (item.ConfigId == 3002) return IconRoot + "material_enhancement";
                if (item.ConfigId == 3405) return CraftIconRoot + "item_star_guarantee_charm";
                if (item.ConfigId == 3406) return CraftIconRoot + "item_enlightenment_page";
                if (item.ConfigId == 3407) return CraftIconRoot + "item_rare_scroll_fragment";
                if (item.ConfigId == 3408) return CraftIconRoot + "item_celestial_ink";
                if (item.ConfigId == 3409) return SeasonEconomyUiArt.EconomyTradeRoot + "icon_unseal_talisman";
                if (item.ConfigId == 3410) return SeasonEconomyUiArt.EconomyTradeV2Root + "icon_inherit_talisman";
                return IsIcon(item.IconKey, "reward_choice") ? IconRoot + "reward_choice" : null;
            }
            if (item.EquipProfile == 1)
                return item.EquipKind switch
                {
                    11 => IconRoot + "equip_weapon",
                    12 => IconRoot + "equip_hat",
                    13 => IconRoot + "equip_robe",
                    14 => IconRoot + "equip_boots",
                    _ => null,
                };
            if (item.EquipProfile == 2 && item.EquipKind == 15)
                return IconRoot + (IsIcon(item.IconKey, "jewelry_jade_knot") ? "jewelry_jade_knot" : "jewelry_jade_pendant");
            if (item.EquipProfile != 3 || item.EquipKind != 16 || item.Artifact == null) return null;
            return item.Artifact.EffectId switch
            {
                1 => IconRoot + "artifact_pozhen",
                2 => IconRoot + "artifact_zhaoying",
                3 => IconRoot + "artifact_huixiang",
                4 => IconRoot + "artifact_jisheng",
                5 => IconRoot + "artifact_guyuan",
                6 => IconRoot + "artifact_huxin",
                7 => IconRoot + "artifact_guiyuan",
                8 => IconRoot + "artifact_qingxin",
                _ => null,
            };
        }

        private static void AddEnhanceLines(BagItemInfo item, List<string> lines)
        {
            if (item.EnhanceMaxLevel == 0)
            {
                lines.Add(item.EquipProfile == 2 || item.EquipProfile == 3 ? "强化：不适用" : "强化：未开放");
                // 不丢弃旧服务器可能带来的已有等级。
                if (item.EnhanceLevel != 0) lines.Add($"已有强化等级：+{Number(item.EnhanceLevel)}");
                return;
            }
            bool maximum = item.EnhanceLevel >= item.EnhanceMaxLevel;
            lines.Add($"强化：+{Number(item.EnhanceLevel)} / +{Number(item.EnhanceMaxLevel)}" + (maximum ? "（已满级）" : string.Empty));
            lines.Add($"可强化基础项加成：{FixedAmount(item.EnhanceBonusPermyriad, 1)}");
            var next = item.EnhanceNext;
            if (next == null)
            {
                if (!maximum) lines.Add("下一次强化暂不可用");
                return;
            }
            lines.Add($"下一级：+{Number(next.NextLevel)}，可强化基础项加成 {FixedAmount(next.BonusPermyriad, 1)}");
            foreach (string preview in EquipDisplay.EnhancePreviewLines(item)) lines.Add(preview);
            lines.Add($"成功率：{EquipDisplay.FormatSuccessRate(next.SuccessRate)}");
            lines.Add($"强化消耗：{EquipDisplay.FormatEnhanceCost(next)}");
            if (next.SuccessRate < 10000)
            {
                lines.Add(next.FailLevelLoss == 0 ? "强化失败不掉级" : $"强化失败降低 {Number(next.FailLevelLoss)} 级");
                lines.Add("强化成功或失败均消耗上述材料与银两");
            }
        }

        private sealed class ComparisonRow
        {
            public string Name;
            public decimal Current;
            public decimal Candidate;
        }

        private static void AddComparisonValues(BagItemInfo item, bool candidate,
            Dictionary<(uint Source, uint Id, uint Parameter, uint Unit), ComparisonRow> rows)
        {
            if (item == null) return;
            foreach (var attr in EquipDisplay.LinesOf(item))
                AddComparisonValue(rows, (0, attr.AttrId, 0, attr.Percent ? 1u : 0u), EquipDisplay.AttrName(attr), attr.Value, candidate);
            foreach (var socket in EquipDisplay.SocketsOf(item))
                if (socket.GemConfigId != 0 && socket.Attr != null)
                {
                    var attr = socket.Attr;
                    AddComparisonValue(rows, (0, attr.AttrId, 0, attr.Percent ? 1u : 0u), EquipDisplay.AttrName(attr), attr.Value, candidate);
                }
            foreach (var slot in item.FixedAffixSlots)
            {
                var affix = slot.Selected;
                if (affix == null) continue;
                // 未识别 effect=0 的旧/缺配条目只按 affix_id 对齐，不能把它们全并到“未知效果”。
                var key = (affix.Effect == 0 ? 2u : 1u, affix.Effect == 0 ? affix.AffixId : affix.Effect, affix.EffectParam, affix.Unit);
                decimal value = affix.ValueBps;
                if (affix.Effect == 13 && affix.Unit == 1) value = -value;
                AddComparisonValue(rows, key, AffixName(affix), value, candidate);
            }
        }

        private static void AddComparisonValue(Dictionary<(uint Source, uint Id, uint Parameter, uint Unit), ComparisonRow> rows,
            (uint Source, uint Id, uint Parameter, uint Unit) key, string name, decimal value, bool candidate)
        {
            if (!rows.TryGetValue(key, out var row)) rows.Add(key, row = new ComparisonRow { Name = name });
            if (candidate) row.Candidate += value;
            else row.Current += value;
        }

        private static string ComparisonAmount(decimal value, bool fixedValue, uint unit)
            => fixedValue ? FixedAmount(value, unit) : Number(value) + (unit == 1 ? "%" : string.Empty);

        private static string FixedAmount(decimal value, uint unit) => unit switch
        {
            1 => Number(value / 100m) + "%",
            2 => Number(value / 100m) + "个百分点",
            _ => $"{Number(value)}（万分比原值，单位 {Number(unit)}）",
        };

        private static int EmptySlots(BagItemInfo item, uint pool)
            => item == null ? 0 : item.FixedAffixSlots.Count(slot => slot.Pool == pool && slot.Selected == null);

        private static string AffixName(EquipFixedAffixInfo affix)
            => string.IsNullOrWhiteSpace(affix.Name) ? $"词条 · {Number(affix.AffixId)}" : affix.Name;

        private static string ArtifactName(EquipArtifactInfo artifact)
            => string.IsNullOrWhiteSpace(artifact.Name) ? $"法宝 · {Number(artifact.EffectId)}" : artifact.Name;

        private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static bool IsIcon(string key, string name)
            => string.Equals(key, name, StringComparison.Ordinal) || string.Equals(key, IconRoot + name, StringComparison.Ordinal);

        public enum TooltipLineTone : byte { Muted, Normal, Rare, Precious, Supreme }

        /// <summary>供普通背包悬浮卡复用的 V1 固定词条品相、保底、法宝与宗师署名行；旧装备返回空列表。</summary>
        public static IReadOnlyList<(string Text, TooltipLineTone Tone)> BagTooltipV1Lines(BagItemInfo item)
        {
            var lines = new List<(string Text, TooltipLineTone Tone)>();
            if (item == null) return lines;
            foreach (var slot in item.FixedAffixSlots.OrderBy(entry => entry.Slot).ThenBy(entry => entry.Pool))
            {
                string label = $"{PoolName(slot.Pool)} {Number(slot.Slot)}";
                if (slot.Selected == null)
                {
                    lines.Add(($"{label}：空位（未选择）", TooltipLineTone.Muted));
                    continue;
                }
                var affix = slot.Selected;
                var tone = affix.QualityTier >= 5 ? TooltipLineTone.Supreme
                    : affix.QualityTier == 4 ? TooltipLineTone.Precious
                    : TooltipLineTone.Rare;
                lines.Add(($"{label}：{AffixName(affix)} {AffixValue(affix)}{FormatAffixQualityTag(affix)}", tone));
                string pity = FormatPolishPity(affix);
                if (!string.IsNullOrEmpty(pity))
                    lines.Add((pity, TooltipLineTone.Muted));
            }
            if (item.Artifact != null)
            {
                lines.Add(($"法宝：{ArtifactName(item.Artifact)}", TooltipLineTone.Supreme));
                if (!string.IsNullOrWhiteSpace(item.Artifact.Description))
                    lines.Add((item.Artifact.Description, TooltipLineTone.Normal));
            }
            if (!string.IsNullOrWhiteSpace(item.CrafterName))
                lines.Add(($"宗师·{item.CrafterName} 亲手铸造", TooltipLineTone.Supreme));
            return lines;
        }

        private static void CollectInstances(BagInfo bag, uint configId, HashSet<ulong> ids)
        {
            if (bag == null || configId == 0) return;
            foreach (var item in bag.Items)
                if (item.ItemId != 0 && item.Count != 0 && item.ConfigId == configId) ids.Add(item.ItemId);
        }
    }
}
