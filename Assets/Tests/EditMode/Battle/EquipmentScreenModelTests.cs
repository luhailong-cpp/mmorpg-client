using System.Globalization;
using System.Linq;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>只测试服务器快照到装备页文字的契约，不加载 Unity 或配置表。</summary>
    public sealed class EquipmentScreenModelTests
    {
        private const string Icons = "UI/Ugui/Equipment20261117/Icons/";

        private static EquipFixedAffixInfo Affix(uint id, uint effect, uint parameter, uint unit, uint value, string name)
            => new EquipFixedAffixInfo
            {
                AffixId = id, Effect = effect, EffectParam = parameter, Unit = unit, ValueBps = value, Name = name,
            };

        private static void Select(BagItemInfo item, uint slot, uint pool, EquipFixedAffixInfo affix)
            => item.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo { Slot = slot, Pool = pool, Selected = affix });

        private static BagItemInfo Item(ulong id, uint config = 3301)
            => new BagItemInfo { ItemId = id, ConfigId = config, Count = 1, EquipKind = 16, EquipProfile = 3, Name = "同名法宝" };

        [Test]
        public void MissingSnapshotsDoNotInventDisplayOrOwnership()
        {
            Assert.That(EquipmentScreenModel.DetailLines(null), Is.Empty);
            Assert.That(EquipmentScreenModel.CompareLines(null, null), Is.Empty);
            Assert.That(EquipmentScreenModel.RecipeList(null), Is.Empty);
            Assert.That(EquipmentScreenModel.AffixValue(null), Is.Empty);
            Assert.That(EquipmentScreenModel.ArtifactLimits(null), Is.Empty);
            Assert.That(EquipmentScreenModel.OwnedCount(null, null, 3301), Is.Zero);
            Assert.That(EquipmentScreenModel.EquippedCount(null, 3301), Is.Zero);
            Assert.That(EquipmentScreenModel.IconKey(null), Is.Null);
        }

        [Test]
        public void LegacyDetailsKeepRequirementsRandomCapsAndBothGemStates()
        {
            var item = new BagItemInfo { EquipKind = 11, Description = "旧装备说明", EquipLevel = 30, EquipClass = 7 };
            item.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 46, EnhanceBonus = 6 });
            item.Affixes.Add(new EquipAttrLineInfo { AttrId = 2, Name = "物理必杀率", Tier = 1, Value = 8, Cap = 10, Percent = true });
            item.GemSockets.Add(new EquipGemSocketInfo { Socket = 1, Name = "防御孔", GemConfigId = 90, GemName = "旧宝石" });
            item.GemSockets.Add(new EquipGemSocketInfo
            {
                Socket = 0, Name = "攻击孔", GemConfigId = 91, GemName = "攻击石",
                Attr = new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 },
            });

            var lines = EquipmentScreenModel.DetailLines(item);

            Assert.That(lines, Does.Contain("旧装备说明"));
            Assert.That(lines, Does.Contain("角色要求：等级 30"));
            Assert.That(lines, Does.Contain("职业要求：职业 · 7"));
            Assert.That(lines, Does.Contain("伤害：46（+6）"));
            Assert.That(lines, Does.Contain("物理必杀率 8%/10%"));
            Assert.That(lines, Does.Contain("攻击孔：攻击石（伤害 +30）"));
            Assert.That(lines, Does.Contain("防御孔：旧宝石（未生效）"));
        }

        [Test]
        public void FixedSlotsRemainExplicitAndSortByTheirSlotInsteadOfRepeatedOrder()
        {
            var item = new BagItemInfo { EquipKind = 11, EquipProfile = 1 };
            Select(item, 2, 2, null);
            var selected = Affix(101, 1, 0, 1, 800, "物理攻击");
            selected.SourceCapBps = 3200;
            selected.Description = "仅增加物理攻击";
            Select(item, 1, 1, selected);
            var original = item.Clone();

            var lines = EquipmentScreenModel.DetailLines(item);

            Assert.That(lines.Where(line => line.StartsWith("方向词条") || line.StartsWith("效能词条")), Is.EqualTo(new[]
            {
                "方向词条 1：物理攻击 +8%（装备同类上限 32%）", "效能词条 2：空位（未选择）",
            }));
            Assert.That(lines, Does.Contain("仅增加物理攻击"));
            Assert.That(item, Is.EqualTo(original));
        }

        [TestCase(1u, 1u, 800u, "+8%")]
        [TestCase(11u, 2u, 425u, "+4.25个百分点")]
        [TestCase(7u, 2u, 1u, "+0.01个百分点")]
        [TestCase(13u, 1u, 300u, "-3%")]
        [TestCase(1u, 1u, 0u, "+0%")]
        [TestCase(1u, 1u, uint.MaxValue, "+42949672.95%")]
        public void AffixUnitsPreserveRelativePercentAndProbabilityPoints(uint effect, uint unit, uint value, string expected)
            => Assert.That(EquipmentScreenModel.AffixValue(Affix(1, effect, 0, unit, value, "词条")), Is.EqualTo(expected));

        [Test]
        public void UnknownPoolAndUnitKeepTheirIdentityWithoutGuessingAnEffect()
        {
            Assert.That(EquipmentScreenModel.PoolName(9), Is.EqualTo("词条池 · 9"));
            Assert.That(EquipmentScreenModel.AffixValue(Affix(45, 99, 0, 7, 850, "")),
                Is.EqualTo("+850（万分比原值，单位 7）"));
            Assert.That(EquipmentScreenModel.ArtifactLimits(new EquipArtifactInfo
            {
                EffectId = 99, BonusBps = 27, CapBps = 41, HealthThresholdBps = 63,
            }), Does.Contain("效果类型 99；强度万分比 27；上限万分比 41；气血阈值万分比 63"));
        }

        [Test]
        public void FormattingDoesNotDependOnMachineDecimalSeparator()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(EquipmentScreenModel.AffixValue(Affix(1, 11, 6, 2, 125, "冰冻命中")), Is.EqualTo("+1.25个百分点"));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Test]
        public void EnhancementShowsAuthoritativePreviewAndUnchangedShoeSpeed()
        {
            var item = new BagItemInfo
            {
                EquipKind = 14, EquipProfile = 1, EnhanceLevel = 9, EnhanceMaxLevel = 10, EnhanceBonusPermyriad = 1800,
                EnhanceNext = new EquipEnhancePreview
                {
                    NextLevel = 10, SuccessRate = 10000, BonusPermyriad = 2000,
                    MaterialConfigId = 3002, MaterialName = "百炼石", MaterialCount = 15,
                },
            };
            item.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 3, Name = "防御", Value = 573, EnhanceBonus = 87, EnhanceNextValue = 583 });
            item.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 4, Name = "速度", Value = 328, Seq = 1, EnhanceNextValue = 328 });

            var lines = EquipmentScreenModel.DetailLines(item);

            Assert.That(lines, Does.Contain("防御：573 → 583"));
            Assert.That(lines, Does.Contain("速度：328 → 328"));
            Assert.That(lines, Does.Contain("可强化基础项加成：18%"));
            Assert.That(lines, Does.Contain("下一级：+10，可强化基础项加成 20%"));
            Assert.That(lines, Does.Contain("成功率：100%"));
            Assert.That(lines, Does.Contain("强化消耗：银两 0、百炼石 ×15"));
        }

        [Test]
        public void MissingEnhancePreviewDoesNotMeanMaximumOrZeroNextAttributes()
        {
            var item = new BagItemInfo { EquipKind = 11, EquipProfile = 1, EnhanceLevel = 3, EnhanceMaxLevel = 10 };
            item.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 1, Name = "物理攻击", Value = 100 });
            var lines = EquipmentScreenModel.DetailLines(item);
            Assert.That(lines, Does.Contain("下一次强化暂不可用"));
            Assert.That(lines.Any(line => line.Contains("已满级") || line.Contains("→ 0")), Is.False);
            item.EnhanceLevel = 10;
            Assert.That(EquipmentScreenModel.DetailLines(item), Does.Contain("强化：+10 / +10（已满级）"));
        }

        [Test]
        public void LegacyEnhancementKeepsFailureLossAndChargedOnFailureText()
        {
            var item = new BagItemInfo
            {
                EquipKind = 11, EnhanceLevel = 4, EnhanceMaxLevel = 12,
                EnhanceNext = new EquipEnhancePreview { NextLevel = 5, SuccessRate = 8750, FailLevelLoss = 2, CostGold = 500 },
            };
            var lines = EquipmentScreenModel.DetailLines(item);
            Assert.That(lines, Does.Contain("成功率：87.5%"));
            Assert.That(lines, Does.Contain("强化失败降低 2 级"));
            Assert.That(lines, Does.Contain("强化成功或失败均消耗上述材料与银两"));
        }

        [TestCase(1u, "物理直接伤害", "不溢出到气血")]
        [TestCase(2u, "法术直接伤害", "不复制到副目标")]
        [TestCase(3u, "下一回合", "补偿后的失败不再生成补偿")]
        [TestCase(4u, "首段即时治疗", "不作用持续恢复、复活、吸血或药品")]
        [TestCase(5u, "主目标新护盾容量", "实际创建或替换且增加容量才计次")]
        [TestCase(6u, "不是新护盾", "实际减免为0不计次")]
        [TestCase(7u, "原指令为普通防御", "实际回复为0不计次")]
        [TestCase(8u, "进入判定即计次", "不包括中毒")]
        public void EveryArtifactShowsItsSingleItemTriggerAndExclusions(uint effect, string trigger, string exclusion)
        {
            var artifact = new EquipArtifactInfo { EffectId = effect, BonusBps = 625, PerRoundLimit = 1, BattleLimit = 2 };
            string text = EquipmentScreenModel.ArtifactLimits(artifact);
            Assert.That(text, Does.StartWith("单件："));
            Assert.That(text, Does.Contain(trigger));
            Assert.That(text, Does.Contain(exclusion));
            Assert.That(text, Does.Contain(effect == 3 ? "每回合最多生成 1 次补偿" : "每回合最多 1 次"));
            Assert.That(text, Does.Contain("死亡、复活和重连不刷新已用次数"));
        }

        [Test]
        public void ArtifactValuesLimitsThresholdAndWindowComeFromSnapshot()
        {
            string defense = EquipmentScreenModel.ArtifactLimits(new EquipArtifactInfo
            {
                EffectId = 6, BonusBps = 1750, CapBps = 725, BattleLimit = 1, ActiveRounds = 3,
            });
            Assert.That(defense, Does.Contain("伤害的 17.5%"));
            Assert.That(defense, Does.Contain("入战最大气血的 7.25%"));
            Assert.That(defense, Does.Contain("每场最多 1 次"));
            Assert.That(defense, Does.Contain("仅开战前 3 回合有效，第 3 回合结束失效"));
            string healing = EquipmentScreenModel.ArtifactLimits(new EquipArtifactInfo
            {
                EffectId = 4, BonusBps = 1250, HealthThresholdBps = 2750,
            });
            Assert.That(healing, Does.Contain("最大气血 27.5%"));
            Assert.That(healing, Does.Contain("施放加成层 +12.5个百分点"));
            Assert.That(healing, Does.Not.Contain("每回合最多 0 次"));
        }

        [Test]
        public void JewelryComparisonReportsSpeedLossAndSpecificResistanceGain()
        {
            var current = new BagItemInfo { ItemId = 101, Name = "相同玉饰", EquipKind = 15, EquipProfile = 2 };
            var candidate = new BagItemInfo { ItemId = 102, Name = "相同玉饰", EquipKind = 15, EquipProfile = 2 };
            Select(current, 1, 3, Affix(301, 3, 0, 1, 400, "气血上限"));
            Select(current, 2, 3, Affix(302, 4, 0, 1, 100, "速度"));
            Select(candidate, 1, 3, Affix(301, 3, 0, 1, 400, "气血上限"));
            Select(candidate, 2, 3, Affix(310, 12, 6, 2, 800, "抗冰冻"));

            var lines = EquipmentScreenModel.CompareLines(current, candidate);

            Assert.That(lines, Does.Contain("气血上限：4% → 4%（不变）"));
            Assert.That(lines, Does.Contain("速度：1% → 0%（-1%）"));
            Assert.That(lines, Does.Contain("抗冰冻：0个百分点 → 8个百分点（+8个百分点）"));
        }

        [Test]
        public void ComparisonAlignsByIdentityAndUnitInsteadOfLabels()
        {
            var current = new BagItemInfo();
            var candidate = new BagItemInfo();
            current.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 8, Name = "速度", Value = 328 });
            candidate.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 8, Name = "改过显示名的速度", Value = 330 });
            candidate.Affixes.Add(new EquipAttrLineInfo { AttrId = 8, Name = "速度", Value = 2, Percent = true });
            Select(current, 1, 1, Affix(100, 11, 5, 2, 400, "同名障碍"));
            Select(candidate, 1, 1, Affix(101, 11, 6, 2, 400, "同名障碍"));
            Select(candidate, 2, 2, Affix(102, 11, 5, 1, 400, "同名障碍"));

            var lines = EquipmentScreenModel.CompareLines(current, candidate);

            Assert.That(lines, Does.Contain("速度：328 → 330（+2）"));
            Assert.That(lines, Does.Contain("速度：0% → 2%（+2%）"));
            Assert.That(lines.Count(line => line.StartsWith("同名障碍：")), Is.EqualTo(3));
            Assert.That(lines, Does.Contain("同名障碍：4个百分点 → 0个百分点（-4个百分点）"));
            Assert.That(lines, Does.Contain("同名障碍：0个百分点 → 4个百分点（+4个百分点）"));
            Assert.That(lines, Does.Contain("同名障碍：0% → 4%（+4%）"));
        }

        [Test]
        public void SameFixedEffectWithDifferentAffixIdsStillAlignsAndManaReductionUsesNegativeCost()
        {
            var current = new BagItemInfo();
            var candidate = new BagItemInfo();
            Select(current, 2, 2, Affix(201, 13, 3, 1, 300, "治疗减耗"));
            Select(candidate, 2, 2, Affix(999, 13, 3, 1, 600, "治疗费用降低"));
            Assert.That(EquipmentScreenModel.CompareLines(current, candidate),
                Is.EqualTo(new[] { "治疗减耗：-3% → -6%（-3%）" }));
        }

        [Test]
        public void ComparisonIncludesLegacyAffixesAndActiveGemsWithoutUnsignedUnderflow()
        {
            var current = new BagItemInfo();
            current.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 46 });
            current.Affixes.Add(new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Tier = 1, Value = 4 });
            current.GemSockets.Add(new EquipGemSocketInfo
            {
                GemConfigId = 91, Attr = new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 },
            });
            current.GemSockets.Add(new EquipGemSocketInfo { Socket = 1, GemConfigId = 92 });
            current.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 2, Name = "大数", Value = ulong.MaxValue });
            var candidate = new BagItemInfo();
            candidate.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 70 });

            var lines = EquipmentScreenModel.CompareLines(current, candidate);

            Assert.That(lines, Does.Contain("伤害：80 → 70（-10）"));
            Assert.That(lines, Does.Contain("大数：18446744073709551615 → 0（-18446744073709551615）"));
        }

        [Test]
        public void CompareKeepsEmptyPoolsAndTwoIdenticallyNamedArtifactsSeparate()
        {
            var current = Item(10);
            var candidate = Item(11);
            current.Artifact = new EquipArtifactInfo { EffectId = 1, Name = "破阵刃", BonusBps = 2000, PerRoundLimit = 1 };
            candidate.Artifact = current.Artifact.Clone();
            Select(current, 1, 1, null);
            var before = current.Clone();

            var lines = EquipmentScreenModel.CompareLines(current, candidate);

            Assert.That(lines.Count(line => line.Contains("法宝：破阵刃")), Is.EqualTo(2));
            Assert.That(lines.Where(line => line.Contains("法宝：破阵刃")).All(line => line.Contains("20%") && !line.Contains("40%")), Is.True);
            Assert.That(lines, Does.Contain("方向词条空位：1 → 0"));
            Assert.That(current, Is.EqualTo(before));
        }

        [Test]
        public void RecipesUseServerKindsAndLevelOrderAndKeepSameNamedProducts()
        {
            var rules = new GetEquipmentCraftRulesResponse();
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3181, EquipKind = 11, EquipLevel = 80, Name = "同名" } });
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3301, EquipKind = 16, Name = "法宝" } });
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3101, EquipKind = 11, EquipLevel = 1, Name = "同名" } });
            rules.Recipes.Add(new EquipmentCraftRecipe());
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3001, EquipKind = 0 } });
            var original = rules.Clone();

            Assert.That(EquipmentScreenModel.RecipeList(rules).Select(recipe => recipe.Product.ConfigId), Is.EqualTo(new uint[] { 3101, 3181, 3301 }));
            Assert.That(EquipmentScreenModel.RecipeList(rules, 11).Select(recipe => recipe.Product.ConfigId), Is.EqualTo(new uint[] { 3101, 3181 }));
            Assert.That(EquipmentScreenModel.RecipeList(rules, 15), Is.Empty);
            Assert.That(rules, Is.EqualTo(original));
            rules.ErrorMessage = new TipInfoMessage { Id = 1 };
            Assert.That(EquipmentScreenModel.RecipeList(rules), Is.Empty);
        }

        [Test]
        public void OwnershipCountsIndependentInstancesIncludingSameNamedDuplicateEquipment()
        {
            var bag = new BagInfo();
            var worn = new BagInfo();
            bag.Items.Add(Item(10));
            bag.Items.Add(Item(11));
            bag.Items.Add(Item(11));
            bag.Items.Add(Item(0));
            bag.Items.Add(new BagItemInfo { ItemId = 20, ConfigId = 3301, Count = 0 });
            bag.Items.Add(Item(21, 3302));
            worn.Items.Add(Item(10));
            worn.Items.Add(Item(12));
            worn.Items[1].Count = 999;
            worn.Layout = new BagLayoutInfo();
            worn.Layout.Slots.Add(new BagSlotInfo { Slot = 6, ItemId = 12 });
            worn.Layout.Slots.Add(new BagSlotInfo { Slot = 7, ItemId = 12 });

            Assert.That(EquipmentScreenModel.OwnedCount(bag, worn, 3301), Is.EqualTo(3));
            Assert.That(EquipmentScreenModel.EquippedCount(worn, 3301), Is.EqualTo(2));
            Assert.That(EquipmentScreenModel.OwnedCount(bag, worn, 0), Is.Zero);
        }

        [TestCase(11u, "equip_weapon")]
        [TestCase(12u, "equip_hat")]
        [TestCase(13u, "equip_robe")]
        [TestCase(14u, "equip_boots")]
        public void BasicIconsUseProfileAndKind(uint kind, string expected)
            => Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { EquipProfile = 1, EquipKind = kind }), Is.EqualTo(Icons + expected));

        [TestCase(1u, "artifact_pozhen")]
        [TestCase(2u, "artifact_zhaoying")]
        [TestCase(3u, "artifact_huixiang")]
        [TestCase(4u, "artifact_jisheng")]
        [TestCase(5u, "artifact_guyuan")]
        [TestCase(6u, "artifact_huxin")]
        [TestCase(7u, "artifact_guiyuan")]
        [TestCase(8u, "artifact_qingxin")]
        public void ArtifactIconsUseEffectIdentityEvenWhenNamesAreEqual(uint effect, string expected)
        {
            var item = Item(1);
            item.Artifact = new EquipArtifactInfo { EffectId = effect, Name = "同名" };
            Assert.That(EquipmentScreenModel.IconKey(item), Is.EqualTo(Icons + expected));
        }

        [Test]
        public void JewelryAppearanceDoesNotAlternateByInstanceOrSlotAndUnknownItemsKeepFallback()
        {
            var first = new BagItemInfo { ItemId = 1, EquipProfile = 2, EquipKind = 15 };
            var second = first.Clone();
            second.ItemId = 2;
            Assert.That(EquipmentScreenModel.IconKey(first), Is.EqualTo(Icons + "jewelry_jade_pendant"));
            Assert.That(EquipmentScreenModel.IconKey(second), Is.EqualTo(EquipmentScreenModel.IconKey(first)));
            second.IconKey = "jewelry_jade_knot";
            Assert.That(EquipmentScreenModel.IconKey(second), Is.EqualTo(Icons + "jewelry_jade_knot"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { EquipKind = 11, EquipProfile = 0 }), Is.Null);
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { EquipKind = 11, EquipProfile = 99 }), Is.Null);
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { EquipKind = 16, EquipProfile = 3, Artifact = new EquipArtifactInfo { EffectId = 99 } }), Is.Null);
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3001 }), Is.EqualTo(Icons + "material_crafting"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3002 }), Is.EqualTo(Icons + "material_enhancement"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { IconKey = "reward_choice" }), Is.EqualTo(Icons + "reward_choice"));
        }

        [Test]
        public void QualityTierPitySpeedExemptCrafterSignatureAndOddsFormatFromServerFields()
        {
            var item = new BagItemInfo { EquipKind = 15, EquipProfile = 2, CrafterName = "清风道人" };
            var hp = Affix(301, 5, 0, 1, 380, "气血上限");
            hp.QualityTier = 4; hp.QualityName = "珍品"; hp.MaxValueBps = 400; hp.PolishPityCount = 7; hp.PolishPityLimit = 8;
            var speed = Affix(302, 6, 0, 1, 100, "速度");
            speed.PolishExempt = true; speed.QualityTier = 5; speed.MaxValueBps = 100;
            Select(item, 1, 3, hp);
            Select(item, 2, 3, speed);

            var lines = EquipmentScreenModel.DetailLines(item);
            Assert.That(lines, Does.Contain("通用词条 1：气血上限 +3.8% [珍品 / 满值 +4%]"));
            Assert.That(lines, Does.Contain("打磨保底：7 / 8（本次打磨 100% 必出极品）"));
            Assert.That(lines, Does.Contain("通用词条 2：速度 +1% [固定满值]"));
            Assert.That(lines, Does.Contain("宗师·清风道人 亲手铸造"));

            var rules = new GetEquipmentCraftRulesResponse
            {
                ForgeMastery = new CraftMasteryInfo { Tier = 2, TierName = "巧匠", Exp = 25, NextTierExp = 50, TopQualityRateBps = 700 },
            };
            rules.QualityTiers.Add(new EquipAffixQualityInfo
            {
                QualityTier = 5, Name = "极品", RatioPct = 100,
                ApprenticeRateBps = 500, ArtisanRateBps = 700, MasterRateBps = 1000,
            });
            var odds = EquipmentScreenModel.FormatQualityOddsLines(rules);
            Assert.That(odds[0], Does.Contain("极品（满值 100%）：当前 7%"));
            Assert.That(EquipmentScreenModel.FormatForgeMastery(rules.ForgeMastery), Does.Contain("巧匠").And.Contain("7%"));

            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3405 }), Does.EndWith("item_star_guarantee_charm"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3406 }), Does.EndWith("item_enlightenment_page"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3407 }), Does.EndWith("item_rare_scroll_fragment"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3408 }), Does.EndWith("item_celestial_ink"));
        }

        [Test]
        public void EquipTooltipBuildData_ShowsV1QualityPityAndMasterSignatureWithoutBreakingLegacyItems()
        {
            var v1Item = new BagItemInfo
            {
                ItemId = 9001,
                ConfigId = 3101,
                Name = "青云灵剑",
                EquipKind = 11,
                EquipLevel = 80,
                EnhanceLevel = 5,
                CrafterName = "欧冶子",
            };
            v1Item.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo
            {
                Slot = 1,
                Pool = 1,
                Selected = new EquipFixedAffixInfo
                {
                    AffixId = 101,
                    Name = "物理攻击",
                    ValueBps = 760,
                    MaxValueBps = 800,
                    Unit = 1,
                    QualityTier = 4,
                    QualityName = "珍品",
                    PolishPityCount = 5,
                    PolishPityLimit = 8,
                },
            });

            var lines = EquipmentScreenModel.BagTooltipV1Lines(v1Item);
            Assert.That(lines.Select(l => l.Text), Has.Some.Contains("方向词条 1：物理攻击 +7.6%").And.Some.Contains("珍品"));
            Assert.That(lines.Select(l => l.Text), Has.Some.Contains("5 / 8"));
            Assert.That(lines.Select(l => l.Text), Has.Member("宗师·欧冶子 亲手铸造"));
            Assert.That(lines[0].Tone, Is.EqualTo(EquipmentScreenModel.TooltipLineTone.Precious));
            Assert.That(lines[lines.Count - 1].Tone, Is.EqualTo(EquipmentScreenModel.TooltipLineTone.Supreme));

            var legacyItem = new BagItemInfo { ItemId = 9002, ConfigId = 1001, Name = "玄铁剑", EquipKind = 11, EquipLevel = 60 };
            Assert.That(EquipmentScreenModel.BagTooltipV1Lines(legacyItem), Is.Empty);
        }

        [Test]
        public void UpgradeTierTargetsFilterByProfileKindHigherLevelAndValidCraftChoiceAcceptsPureSilverUpgrade()
        {
            var lv1Weapon = new BagItemInfo { ItemId = 101, ConfigId = 3101, Count = 1, EquipProfile = 1, EquipKind = 11, EquipLevel = 1 };
            var lv80Weapon = new BagItemInfo { ItemId = 102, ConfigId = 3181, Count = 1, EquipProfile = 1, EquipKind = 11, EquipLevel = 80 };
            var artifact = new BagItemInfo { ItemId = 103, ConfigId = 3301, Count = 1, EquipProfile = 3, EquipKind = 16, EquipLevel = 1 };

            Assert.That(EquipmentScreenModel.CanUpgradeTier(lv1Weapon), Is.True);
            Assert.That(EquipmentScreenModel.CanUpgradeTier(lv80Weapon), Is.False);
            Assert.That(EquipmentScreenModel.CanUpgradeTier(artifact), Is.False);

            var rules = new GetEquipmentCraftRulesResponse();
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3101, EquipProfile = 1, EquipKind = 11, EquipLevel = 1 } });
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3161, EquipProfile = 1, EquipKind = 11, EquipLevel = 60 } });
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3131, EquipProfile = 1, EquipKind = 11, EquipLevel = 30 } });
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3132, EquipProfile = 1, EquipKind = 12, EquipLevel = 30 } });
            rules.Recipes.Add(new EquipmentCraftRecipe { Product = new BagItemInfo { ConfigId = 3231, EquipProfile = 2, EquipKind = 15, EquipLevel = 30 } });

            var targets = EquipmentScreenModel.FindUpgradeTierTargets(lv1Weapon, rules);
            Assert.That(targets.Select(t => t.ConfigId), Is.EqualTo(new uint[] { 3131, 3161 }));
            Assert.That(EquipmentScreenModel.PickDefaultUpgradeTarget(targets, 45)?.ConfigId, Is.EqualTo(3131u));
            Assert.That(EquipmentScreenModel.PickDefaultUpgradeTarget(targets, 70)?.ConfigId, Is.EqualTo(3161u));
            Assert.That(EquipmentScreenModel.PickDefaultUpgradeTarget(targets, 15)?.ConfigId, Is.EqualTo(3131u));

            Assert.That(MmorpgClient.Game.PlayerFeatures.PlayerFeaturesClient.ValidCraftChoice(new EquipmentCraftChoice
            {
                Operation = EquipmentCraftChoice.Types.Operation.UpgradeTier,
                ItemId = 101,
                ConfigId = 3131,
            }), Is.True);
            Assert.That(MmorpgClient.Game.PlayerFeatures.PlayerFeaturesClient.ValidCraftChoice(new EquipmentCraftChoice
            {
                Operation = EquipmentCraftChoice.Types.Operation.UpgradeTier,
                ItemId = 101,
                ConfigId = 0,
            }), Is.False);
            Assert.That(MmorpgClient.Game.PlayerFeatures.PlayerFeaturesClient.ValidCraftChoice(new EquipmentCraftChoice
            {
                Operation = EquipmentCraftChoice.Types.Operation.UpgradeTier,
                ItemId = 101,
                ConfigId = 3131,
                SourceItemId = 202,
            }), Is.False);

            string summary = EquipmentScreenModel.FormatCraftPreviewSummary(new EquipmentCraftPreview
            {
                Available = true,
                GoldCost = 300,
                Choice = new EquipmentCraftChoice { Operation = EquipmentCraftChoice.Types.Operation.UpgradeTier, ItemId = 101, ConfigId = 3131 },
            });
            Assert.That(summary, Is.EqualTo("银两 300"));
        }

        [Test]
        public void IconKeyMapsReforgeSandAndBoundArtifactVoucherToFormalIcons()
        {
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3403 }), Is.EqualTo("UI/Icons/item_reforge_sand"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3404 }), Is.EqualTo("UI/Icons/item_bound_artifact_voucher"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3405 }), Is.EqualTo("UI/Ugui/CraftQualitySkill/Icons/item_star_guarantee_charm"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3410 }), Is.EqualTo("UI/Ugui/EconomyTradeV2/icon_inherit_talisman"));
            Assert.That(EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3414 }), Is.EqualTo("UI/Icons/item_sealed_skill_book"));

            var sealedBook = new BagItemInfo
            {
                ItemId = 9001,
                ConfigId = 3414,
                Count = 1,
                Bound = false,
                SealedSkillBook = new SealedSkillBookInfo
                {
                    SkillTableId = 12020,
                    SkillName = "愈战愈勇",
                    ScrollId = 3,
                    ScrollName = "兵法成长卷",
                    CrafterName = "欧冶子",
                },
            };
            var tooltipLines = EquipmentScreenModel.BagTooltipV1Lines(sealedBook);
            Assert.That(tooltipLines.Any(l => l.Text.Contains("愈战愈勇") && l.Text.Contains("兵法成长卷")), Is.True);
            Assert.That(tooltipLines.Any(l => l.Text.Contains("宗师·欧冶子 亲手撰")), Is.True);
        }

        [Test]
        public void V2ToV5QualityAuditBatchPolishSummaryAndFormattedTitleWork()
        {
            // V2 雅号署名不重复拼接「宗师·」
            Assert.That(EquipmentScreenModel.FormatCrafterLine("双绝天工·欧冶子", false), Is.EqualTo("双绝天工·欧冶子 亲手铸造"));
            Assert.That(EquipmentScreenModel.FormatCrafterLine("欧冶子", true), Is.EqualTo("宗师·欧冶子 亲手撰"));

            // V3 全身品相工坊健检：1 极品 + 1 凡品(保底2/8 -> 最坏剩6砂) + 1 速度豁免
            var gear = new BagItemInfo { ItemId = 501, ConfigId = 3101 };
            gear.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo
            {
                Slot = 1, Pool = 1,
                Selected = new EquipFixedAffixInfo { AffixId = 101, QualityTier = 5, PolishPityLimit = 8 },
            });
            gear.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo
            {
                Slot = 2, Pool = 2,
                Selected = new EquipFixedAffixInfo { AffixId = 201, QualityTier = 1, PolishPityCount = 2, PolishPityLimit = 8 },
            });
            var acc = new BagItemInfo { ItemId = 502, ConfigId = 3201 };
            acc.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo
            {
                Slot = 1, Pool = 3,
                Selected = new EquipFixedAffixInfo { AffixId = 302, QualityTier = 5, PolishExempt = true },
            });
            var audit = EquipmentScreenModel.BuildQualityAudit(new[] { gear, acc });
            Assert.That(audit.PolishableSlots, Is.EqualTo(2));
            Assert.That(audit.SupremeSlots, Is.EqualTo(1));
            Assert.That(audit.ExemptSpeedSlots, Is.EqualTo(1));
            Assert.That(audit.WorstCaseSandNeeded, Is.EqualTo(6u));
            Assert.That(audit.WorstCaseGoldNeeded, Is.EqualTo(120UL));
            Assert.That(audit.LowestItemId, Is.EqualTo(501UL));
            Assert.That(audit.LowestSlot, Is.EqualTo(2u));

            // V3 智能连磨 + V5 同队情义 0 费代打预览与回执摘要
            string batchSummary = EquipmentScreenModel.FormatCraftPreviewSummary(new EquipmentCraftPreview
            {
                Available = true,
                GoldCost = 20,
                ProxyFeeGold = 0,
                ProxyMasterName = "双绝天工·欧冶子",
                TeamFreeProxyApplied = true,
                MaxPolishAttempts = 6,
                MaxPolishSandCost = 6,
                MaxPolishGoldCost = 120,
                Choice = new EquipmentCraftChoice { Operation = EquipmentCraftChoice.Types.Operation.PolishAffix, TargetQualityTier = 5, MaxPolishAttempts = 8 },
            });
            Assert.That(batchSummary, Does.Contain("同队情义代打（0 银两免税"));
            Assert.That(batchSummary, Does.Contain("智能定档连磨（目标 极品）"));

            string recap = EquipmentScreenModel.FormatPolishReceiptSummary(new CommitEquipmentCraftResponse
            {
                Outcome = CommitEquipmentCraftResponse.Types.Outcome.Applied,
                ResultQualityTier = 4,
                PolishAttemptsUsed = 2,
                ActualGoldSpent = 40,
                PolishSteps =
                {
                    new PolishStepTrace { AttemptIndex = 1, TierBefore = 1, TierAfter = 3 },
                    new PolishStepTrace { AttemptIndex = 2, TierBefore = 3, TierAfter = 4 },
                },
            });
            Assert.That(recap, Does.Contain("实耗 2 手 · 40 银两 → 珍品"));
            Assert.That(recap, Does.Contain("上品 → 珍品"));
        }

        [Test]
        public void SupportAndControlEnhancedAffixesShowEnhancementBonusTagAndBenefitLinesWithoutScalingSpeedOrAttack()
        {
            var weapon = new BagItemInfo
            {
                ItemId = 9901,
                ConfigId = 3181,
                EquipKind = 11,
                EquipProfile = 1,
                EnhanceLevel = 10,
                EnhanceMaxLevel = 11,
                EnhanceBonusPermyriad = 2000,
                EnhanceNext = new EquipEnhancePreview { NextLevel = 11, SuccessRate = 1000, BonusPermyriad = 2400 },
            };
            var heal = Affix(103, 5, 0, 1, 880, "施放治疗");
            heal.MaxValueBps = 800; heal.SourceCapBps = 3520; heal.QualityTier = 5; heal.QualityName = "极品";
            var ctrl = Affix(105, 11, 1, 2, 420, "封灵命中");
            ctrl.MaxValueBps = 400; ctrl.SourceCapBps = 1680; ctrl.QualityTier = 5; ctrl.QualityName = "极品";
            Select(weapon, 1, 1, heal);
            Select(weapon, 2, 2, ctrl);

            Assert.That(EquipmentScreenModel.FormatAffixQualityTag(heal), Is.EqualTo(" [极品 · 强化+0.8%]"));
            Assert.That(EquipmentScreenModel.FormatAffixQualityTag(ctrl), Is.EqualTo(" [极品 · 强化+0.2个百分点]"));

            var enhancedTier4 = Affix(103, 5, 0, 1, 851, "施放治疗");
            enhancedTier4.MaxValueBps = 800; enhancedTier4.QualityTier = 4; enhancedTier4.QualityName = "珍品";
            Assert.That(EquipmentScreenModel.FormatAffixQualityTag(enhancedTier4), Is.EqualTo(" [珍品 · 含强化 / 基础满值 +8%]"));

            var benefitLines = EquipmentScreenModel.EnhanceAffixBenefitLines(weapon);
            Assert.That(benefitLines, Has.Count.EqualTo(2));
            Assert.That(benefitLines[0], Does.Contain("施放治疗").And.Contain("50%").And.Contain("+8.8%").And.Contain("+11 词条 +12%"));
            Assert.That(benefitLines[1], Does.Contain("封灵命中").And.Contain("25%").And.Contain("+4.2个百分点").And.Contain("+11 词条 +6%"));

            // +11 满级（无 EnhanceNext）也展示已生效的控辅词条强化幅度；输出攻%与速度不生成词条强化受益行。
            weapon.EnhanceLevel = 11;
            weapon.EnhanceBonusPermyriad = 2400;
            weapon.EnhanceNext = null;
            heal.ValueBps = 896;
            var maxBenefits = EquipmentScreenModel.EnhanceAffixBenefitLines(weapon);
            Assert.That(maxBenefits[0], Does.Contain("+8.96%").And.Contain("已享 50% 幅度 · +12%"));

            var dps = new BagItemInfo { EquipKind = 11, EquipProfile = 1, EnhanceLevel = 11, EnhanceMaxLevel = 11, EnhanceBonusPermyriad = 2400 };
            Select(dps, 1, 1, Affix(101, 1, 0, 1, 800, "物理攻击"));
            Select(dps, 2, 3, Affix(302, 4, 0, 1, 100, "速度"));
            Assert.That(EquipmentScreenModel.EnhanceAffixBenefitLines(dps), Is.Empty);
        }
    }
}
