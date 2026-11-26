using MmorpgClient.Game.Skills;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Skills
{
    /// <summary>技能书协议 → UI 模型的纯映射(服务端 docs/design/class-skills.md §4)。</summary>
    public sealed class SkillBookMapperTests
    {
        private static ListSkillsResponse OneRow(SkillCatalogEntry row, uint classId = 3)
        {
            var book = new ListSkillsResponse { ClassId = classId, PlayerLevel = 12, CurrencyBalance = 777 };
            book.Catalog.Add(row);
            return book;
        }

        [Test]
        public void MapsEveryDisplayFieldFromTheServer()
        {
            var row = new SkillCatalogEntry
            {
                SkillTableId = 1304, Name = "灵鼎护元", Description = "提升防御", IconKey = "UI/Skills/1304", ClassId = 3,
                Category = "主动", Passive = false, TargetTeam = 2, MaxTargets = 3, UnlockLevel = 30, MaxRank = 10,
                Rank = 0, RankLimit = 0, ManaPercent = 0.06, CooldownRounds = 3, NextRankCost = 400,
                UpgradeBlockTipId = (uint)skill_error.KSkillLevelNotEnough, DurationRounds = 2,
            };
            var snapshot = SkillBookMapper.ToSnapshot(OneRow(row),
                id => new SkillDefinition { id = id, className = "丹心", iconPath = "icons/1304.png" });

            Assert.That(snapshot.ClassId, Is.EqualTo(3u));
            Assert.That(snapshot.PlayerLevel, Is.EqualTo(12u));
            Assert.That(snapshot.CurrencyBalance, Is.EqualTo(777UL));
            Assert.That(snapshot.Title, Is.EqualTo(SkillBookMapper.CharacterTitle));
            var entry = snapshot.Find(1304);
            Assert.That(entry.Rank, Is.Zero);
            Assert.That(entry.RankLimit, Is.Zero);
            Assert.That(entry.NextRankCost, Is.EqualTo(400UL));
            Assert.That(entry.LockedReason, Is.EqualTo(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLevelNotEnough)));
            var definition = entry.Definition;
            Assert.That(definition.name, Is.EqualTo("灵鼎护元"));
            Assert.That(definition.className, Is.EqualTo("丹心"), "职业名只是展示元数据,取本地图鉴");
            Assert.That(definition.targetTeam, Is.EqualTo("ally"));
            Assert.That(definition.TargetLabel, Is.EqualTo("友方"));
            Assert.That(definition.maxTargets, Is.EqualTo(3u));
            Assert.That(definition.manaPercent, Is.EqualTo(0.06));
            Assert.That(definition.cooldownRounds, Is.EqualTo(3u));
            Assert.That(definition.durationRounds, Is.EqualTo(2u));
            Assert.That(definition.IsPassive, Is.False);
            Assert.That(definition.IconResource, Is.EqualTo("UI/Skills/1304"));
        }

        [Test]
        public void PassiveRowsAreMarkedPassive()
        {
            var snapshot = SkillBookMapper.ToSnapshot(OneRow(new SkillCatalogEntry
                { SkillTableId = 1306, ClassId = 3, Passive = true, TargetTeam = 3, Category = "被动" }), _ => null);
            Assert.That(snapshot.Find(1306).Definition.IsPassive, Is.True);
            Assert.That(snapshot.Find(1306).Definition.targetTeam, Is.EqualTo("self"));
        }

        [Test]
        public void FreeCatalogDoesNotApplyAClientClassRestriction()
        {
            var book = OneRow(new SkillCatalogEntry { SkillTableId = 2101, ClassId = 0, AllowDeadTarget = true }, classId: 3);
            var entry = SkillBookMapper.ToSnapshot(book, _ => null).Find(2101);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Definition.allowDeadTarget, Is.True);
        }

        [TestCase("UI/Skills/1101", 1101u, "UI/Skills/1101")]
        [TestCase("Assets/Resources/UI/Skills/1101.png", 1101u, "UI/Skills/1101")]
        [TestCase(@"UI\Skills\1102.png", 1102u, "UI/Skills/1102")]
        [TestCase("", 1201u, "UI/Skills/1201")]
        [TestCase(null, 1202u, "UI/Skills/1202")]
        public void IconKeysAreResourceRelativeWithoutExtension(string key, uint id, string expected)
        {
            Assert.That(SkillBookMapper.NormalizeIconKey(key, id), Is.EqualTo(expected));
        }

        [Test]
        public void GenericActiveCategoryUsesServerActionTagsForHealingSupportAndObstacleTabs()
        {
            foreach (var item in new[] { (10010u, 4u, true, false), (10040u, 8u, true, false),
                (10020u, 16u, false, true), (10019u, 18u, false, false) })
            {
                var book = OneRow(new SkillCatalogEntry { SkillTableId = item.Item1, Category = "主动", ActionTags = item.Item2 });
                var definition = SkillBookMapper.ToSnapshot(book, _ => null).Find(item.Item1).Definition;
                Assert.That(definition.IsSupport, Is.EqualTo(item.Item3));
                Assert.That(definition.IsObstacle, Is.EqualTo(item.Item4));
            }
        }

        [Test]
        public void CompleteSkillManifestAlsoAppliesToDirectDefinitionsWithoutAnOpenedSkillBook()
        {
            foreach (var entry in new[] { (10010u, "A10"), (11005u, "B05"), (12019u, "P19"), (13006u, "T06"), (14003u, "O03") })
            {
                string expected = "UI/Ugui/CharacterPet/Icons/128/" + entry.Item2;
                Assert.That(SkillBookMapper.NormalizeIconKey("UI/Skills/obsolete", entry.Item1), Is.EqualTo(expected));
                Assert.That(new SkillDefinition { id = entry.Item1, iconResource = "UI/Skills/obsolete" }.IconResource, Is.EqualTo(expected));
            }
            Assert.That(new SkillDefinition { id = 1101, iconResource = "UI/Skills/legacy" }.IconResource, Is.EqualTo("UI/Skills/legacy"));
        }

        [Test]
        public void TipTextsUseGeneratedEnumsAndFallBackToNumber()
        {
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillGoldNotEnough), Is.EqualTo("银两不足"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillUpgradeBusy), Does.Contain("战斗"));
            Assert.That(SkillBookMapper.DescribeTip(424242u), Is.EqualTo("tip=424242"));
        }

        [Test]
        public void UpgradeSummaryMentionsStopReasonOnlyWhenStoppedEarly()
        {
            Assert.That(SkillBookMapper.DescribeUpgrade("破阵惊锋", 10, 0), Is.EqualTo("【破阵惊锋】修习成功,提升 10 重"));
            Assert.That(SkillBookMapper.DescribeUpgrade("破阵惊锋", 2, (uint)skill_error.KSkillGoldNotEnough),
                Does.EndWith("(已停止:银两不足)"));
        }

        [Test]
        public void MapsScrollRarityBookUnlockFieldsAndNewCraftTips()
        {
            var row = new SkillCatalogEntry
            {
                SkillTableId = 12020, Name = "愈战愈勇", Passive = true, TargetTeam = 3,
                MaxRank = 10, Rank = 0, RankLimit = 10, NextRankCost = 100,
                ScrollId = 3, RarityTier = 0, CraftUnlockRequired = true, UnlockedByBook = false,
            };
            var rare = new SkillCatalogEntry
            {
                SkillTableId = 10041, Name = "玄镜映法", Passive = false, TargetTeam = 1,
                MaxRank = 10, Rank = 0, RankLimit = 10, NextRankCost = 100,
                ScrollId = 2, RarityTier = 1, CraftUnlockRequired = true, UnlockedByBook = true,
            };
            var book = new ListSkillsResponse { ClassId = 1, PlayerLevel = 60, CurrencyBalance = 5000 };
            book.Catalog.Add(row);
            book.Catalog.Add(rare);
            var snapshot = SkillBookMapper.ToSnapshot(book, _ => null);

            var locked = snapshot.Find(12020);
            Assert.That(locked.ScrollId, Is.EqualTo(3u));
            Assert.That(locked.NeedsCraftUnlock, Is.True);
            var view = new SkillBookViewState { IsConnected = true, Snapshot = snapshot };
            Assert.That(SkillBookViewState.CanUpgrade(view, locked), Is.False, "未研习技能书解锁时不可直接修习");

            var unlockedRare = snapshot.Find(10041);
            Assert.That(unlockedRare.IsRare, Is.True);
            Assert.That(unlockedRare.Definition.IsRare, Is.True);
            Assert.That(unlockedRare.NeedsCraftUnlock, Is.False);
            Assert.That(SkillBookViewState.CanUpgrade(view, unlockedRare), Is.True);

            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillBookNotDismantlable), Does.Contain("稀世"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillCraftBookRequired), Does.Contain("典籍制书"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillProxyFeeInvalid), Does.Contain("50"));
            Assert.That(SkillBookMapper.DescribeTip((uint)equip_error.KEquipProxyMasterUnavailable), Does.Contain("代打摊位"));
        }

        [Test]
        public void LoadoutSnapshot_MapsLightCardUnlockedSchemesAndPreservesExpiredSchemeFour()
        {
            var activeResp = new ListSkillsResponse
            {
                ClassId = 1,
                PlayerLevel = 60,
                Loadout = new SkillLoadoutSnapshot
                {
                    CustomEnabled = true,
                    ActiveSchemeId = 4,
                    StateVersion = 2,
                    UnlockedSchemes = 6,
                    MaxSchemes = 6,
                },
            };
            activeResp.Loadout.Schemes.Add(new SkillLoadoutSchemeInfo { SchemeId = 4, RoleTag = 1 });
            var activeSnap = SkillBookMapper.ToSnapshot(activeResp);
            Assert.That(activeSnap.Loadout.UnlockedSchemes, Is.EqualTo(6u));
            Assert.That(activeSnap.Loadout.MaxSchemes, Is.EqualTo(6u));
            Assert.That(activeSnap.Loadout.ActiveSchemeId, Is.EqualTo(4u));
            Assert.That(activeSnap.Loadout.FindScheme(4)?.RoleTag, Is.EqualTo(1u));

            var expiredResp = new ListSkillsResponse
            {
                ClassId = 1,
                PlayerLevel = 60,
                Loadout = new SkillLoadoutSnapshot
                {
                    CustomEnabled = true,
                    ActiveSchemeId = 1,
                    StateVersion = 3,
                    UnlockedSchemes = 3,
                    MaxSchemes = 6,
                },
            };
            expiredResp.Loadout.Schemes.Add(new SkillLoadoutSchemeInfo { SchemeId = 1, RoleTag = 0 });
            expiredResp.Loadout.Schemes.Add(new SkillLoadoutSchemeInfo { SchemeId = 4, RoleTag = 2 });
            var expiredSnap = SkillBookMapper.ToSnapshot(expiredResp);
            Assert.That(expiredSnap.Loadout.UnlockedSchemes, Is.EqualTo(3u));
            Assert.That(expiredSnap.Loadout.MaxSchemes, Is.EqualTo(6u));
            Assert.That(expiredSnap.Loadout.FindScheme(4)?.RoleTag, Is.EqualTo(2u), "到期后仍须保留第 4 套已存方案供只读展示");
        }

        [Test]
        public void LoadoutSnapshot_MapsSevenSlotRentalMetadataCustomNamesAttributesAndNewTips()
        {
            var scheme5 = new SkillLoadoutSchemeInfo
            {
                SchemeId = 5,
                Name = "高速封系",
                RoleTag = 2,
                AttributeRemaining = 10,
                AttributeTotalPoints = 60,
            };
            scheme5.AttributeAllocated[104] = 40;
            scheme5.AttributeAllocated[101] = 10;

            var resp = new ListSkillsResponse
            {
                ClassId = 1,
                PlayerLevel = 60,
                Loadout = new SkillLoadoutSnapshot
                {
                    CustomEnabled = true,
                    ActiveSchemeId = 5,
                    StateVersion = 9,
                    UnlockedSchemes = 6,
                    MaxSchemes = 7,
                    FreeSchemes = 2,
                    LightCardBonusSchemes = 2,
                    RentedExtraSlots = 2,
                    RentExpiresAtSec = 200000UL,
                    ServerNowSec = 100000UL,
                    RentUnitPriceDiamond = 30UL,
                    RentPeriodDays = 30u,
                    RentMaxRemainingDays = 180u,
                    DiamondBalance = 95UL,
                    AttributeResetCostGold = 12000UL,
                    AttributeSwitchCooldownUntil = 100045UL,
                    ActiveAttributeSchemeId = 5u,
                },
            };
            resp.Loadout.Schemes.Add(scheme5);

            var snap = SkillBookMapper.ToSnapshot(resp);
            Assert.That(snap.Loadout.MaxSchemes, Is.EqualTo(7u));
            Assert.That(snap.Loadout.FreeSchemes, Is.EqualTo(2u));
            Assert.That(snap.Loadout.LightCardActive, Is.True);
            Assert.That(snap.Loadout.RentedExtraSlots, Is.EqualTo(2u));
            Assert.That(snap.Loadout.IsRentActive, Is.True);
            Assert.That(snap.Loadout.RemainingRentSeconds, Is.EqualTo(100000UL));
            Assert.That(snap.Loadout.DiamondBalance, Is.EqualTo(95UL));
            Assert.That(snap.Loadout.AttributeResetCostGold, Is.EqualTo(12000UL));
            Assert.That(snap.Loadout.ActiveAttributeSchemeId, Is.EqualTo(5u));

            var mapped = snap.Loadout.FindScheme(5);
            Assert.That(mapped, Is.Not.Null);
            Assert.That(mapped.Name, Is.EqualTo("高速封系"));
            Assert.That(mapped.AttributeTotalPoints, Is.EqualTo(60u));
            Assert.That(mapped.AttributeRemaining, Is.EqualTo(10u));
            Assert.That(mapped.GetAllocated(104), Is.EqualTo(40u));
            Assert.That(mapped.GetAllocated(101), Is.EqualTo(10u));

            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutNameInvalid), Does.Contain("方案名称"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutRentPriceChanged), Does.Contain("价格"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutRentDiamondNotEnough), Does.Contain("灵石不足"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutRentStackLimit), Does.Contain("上限"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutRentSlotsInvalid), Does.Contain("缩减"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutAttributeInvalid), Does.Contain("加点"));
            Assert.That(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLoadoutAttributeResetRequired), Does.Contain("洗点"));
            Assert.That(SkillBookMapper.DescribeTip((uint)attribute_error.KAttributeGoldNotEnough), Does.Contain("银两不足"));
        }
    }
}
