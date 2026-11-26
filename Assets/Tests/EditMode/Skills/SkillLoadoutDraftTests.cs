using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Skills
{
    public sealed class SkillLoadoutDraftTests
    {
        [Test]
        public void FullDestinationKeepsAnEquippedConflictInAnotherCategory()
        {
            var snapshot = new SkillBookSnapshot { Loadout = new SkillLoadoutView() };
            var scheme = new SkillLoadoutSchemeView { SchemeId = 1 };
            scheme.OpeningSkillIds.Add(10);
            scheme.PassiveSkillIds.Add(20);
            snapshot.Loadout.Schemes.Add(scheme);
            snapshot.Entries.Add(Entry(10, 4, 0));
            snapshot.Entries.Add(Entry(20, 2, 8));
            var next = Entry(30, 4, 8);
            snapshot.Entries.Add(next);
            var draft = new SkillLoadoutDraft();
            draft.Reset(snapshot.Loadout, 1);

            Assert.That(draft.GetConflict(snapshot, next), Is.EqualTo(20u));
            Assert.That(draft.Toggle(snapshot, next, out uint replaced), Is.EqualTo(SkillLoadoutDraft.Change.SlotsFull));
            Assert.That(replaced, Is.Zero);
            Assert.That(draft.Opening, Is.EqualTo(new uint[] { 10 }));
            Assert.That(draft.Passive, Is.EqualTo(new uint[] { 20 }));
            Assert.That(draft.IsDirty, Is.False);
        }

        [Test]
        public void SameCategoryConflictUsesTheExistingSlotEvenAtCapacity()
        {
            var snapshot = new SkillBookSnapshot { Loadout = new SkillLoadoutView() };
            var scheme = new SkillLoadoutSchemeView { SchemeId = 1 };
            scheme.PursuitSkillIds.Add(10);
            snapshot.Loadout.Schemes.Add(scheme);
            snapshot.Entries.Add(Entry(10, 3, 8));
            var next = Entry(20, 3, 8);
            snapshot.Entries.Add(next);
            var draft = new SkillLoadoutDraft();
            draft.Reset(snapshot.Loadout, 1);

            Assert.That(draft.Toggle(snapshot, next, out uint replaced), Is.EqualTo(SkillLoadoutDraft.Change.Replaced));
            Assert.That(replaced, Is.EqualTo(10u));
            Assert.That(draft.Pursuit, Is.EqualTo(new uint[] { 20 }));
            Assert.That(draft.IsDirty, Is.True);
        }

        [Test]
        public void BuildTemplatesDistributeAllPointsAndAssignRemainderToPrimaryStat()
        {
            var loadout = new SkillLoadoutView { UnlockedSchemes = 2 };
            var scheme = new SkillLoadoutSchemeView { SchemeId = 1, AttributeTotalPoints = 53, AttributeRemaining = 53 };
            loadout.Schemes.Add(scheme);
            var draft = new SkillLoadoutDraft();
            draft.Reset(loadout, 1);

            Assert.That(draft.ApplyTemplate(0), Is.True); // 4力1体: 53 -> 力43 体10
            Assert.That(draft.GetAttribute(SkillBuildTemplates.DimStr), Is.EqualTo(43u));
            Assert.That(draft.GetAttribute(SkillBuildTemplates.DimCon), Is.EqualTo(10u));
            Assert.That(draft.AttributeUsedPoints, Is.EqualTo(53u));
            Assert.That(draft.AttributeRemaining, Is.Zero);
            Assert.That(draft.RoleTag, Is.EqualTo(1u));
            Assert.That(draft.AttributesDirty, Is.True);

            for (int i = 0; i < SkillBuildTemplates.All.Length; i++)
            {
                Assert.That(draft.ApplyTemplate(i), Is.True);
                Assert.That(draft.AttributeUsedPoints, Is.EqualTo(53u), SkillBuildTemplates.All[i].Label);
            }
        }

        [Test]
        public void AdjustAttributeDetectsResetRequirementAndCopyWorksFromExpiredScheme()
        {
            var loadout = new SkillLoadoutView { UnlockedSchemes = 2 };
            var s1 = new SkillLoadoutSchemeView { SchemeId = 1, AttributeTotalPoints = 40, AttributeRemaining = 10 };
            s1.AttributeAllocated[SkillBuildTemplates.DimStr] = 30;
            loadout.Schemes.Add(s1);

            var expired5 = new SkillLoadoutSchemeView { SchemeId = 5, Name = "封系旧案", RoleTag = 2, AttributeTotalPoints = 40 };
            expired5.PassiveSkillIds.Add(12001u);
            expired5.AttributeAllocated[SkillBuildTemplates.DimAgi] = 35;
            expired5.AttributeAllocated[SkillBuildTemplates.DimCon] = 5;
            loadout.Schemes.Add(expired5);

            var draft = new SkillLoadoutDraft();
            draft.Reset(loadout, 1);
            Assert.That(draft.NeedsAttributeReset, Is.False);
            Assert.That(draft.AdjustAttribute(SkillBuildTemplates.DimStr, -10), Is.True);
            Assert.That(draft.NeedsAttributeReset, Is.True);
            Assert.That(draft.AdjustAttribute(SkillBuildTemplates.DimStr, 10), Is.True);
            Assert.That(draft.NeedsAttributeReset, Is.False);

            // 从已到期只读方案 5 一键复制到方案 1 草稿
            Assert.That(draft.CopyFromScheme(loadout, 5), Is.True);
            Assert.That(draft.RoleTag, Is.EqualTo(2u));
            Assert.That(draft.Passive, Is.EqualTo(new[] { 12001u }));
            Assert.That(draft.GetAttribute(SkillBuildTemplates.DimAgi), Is.EqualTo(35u));
            Assert.That(draft.GetAttribute(SkillBuildTemplates.DimCon), Is.EqualTo(5u));
            Assert.That(draft.NeedsAttributeReset, Is.True, "力量从 30 变为 0 必须识别为需洗点");
        }

        [Test]
        public void RentPlannerMatchesServerProrateCeilAntiExploitAntiOverbuyAndStackCap()
        {
            ulong day = SkillLoadoutRentPlanner.SecondsPerDay;
            var fresh = new SkillLoadoutView { ServerNowSec = 1000UL, LightCardBonusSchemes = 0 };
            var qNew = SkillLoadoutRentPlanner.Plan(fresh, 2u, 1u);
            Assert.That(qNew.Valid, Is.True);
            Assert.That(qNew.ProrateCost, Is.Zero);
            Assert.That(qNew.RenewCost, Is.EqualTo(60UL));
            Assert.That(qNew.TotalCost, Is.EqualTo(60UL));
            Assert.That(qNew.NewUnlockedSchemes, Is.EqualTo(4u));

            // 租期内剩 15 天时从 2 套扩容到 4 套（不续期）：补差价 ceil(2*30*15/30)=30
            var mid = new SkillLoadoutView
            {
                ServerNowSec = 1000UL,
                RentedExtraSlots = 2u,
                RentExpiresAtSec = 1000UL + 15UL * day,
            };
            var qExpand = SkillLoadoutRentPlanner.Plan(mid, 4u, 0u);
            Assert.That(qExpand.Valid, Is.True);
            Assert.That(qExpand.ProrateCost, Is.EqualTo(30UL));
            Assert.That(qExpand.RenewCost, Is.Zero);

            // 剩余 1 秒防薅保底 >= 1 灵钻
            var edge = new SkillLoadoutView { ServerNowSec = 1000UL, RentedExtraSlots = 1u, RentExpiresAtSec = 1001UL };
            var qEdge = SkillLoadoutRentPlanner.Plan(edge, 2u, 0u);
            Assert.That(qEdge.Valid, Is.True);
            Assert.That(qEdge.ProrateCost, Is.EqualTo(1UL));

            // 租期内不可缩减已租套数
            var qShrink = SkillLoadoutRentPlanner.Plan(mid, 1u, 1u);
            Assert.That(qShrink.Valid, Is.False);

            // 轻享卡激活时最多可租 3 套（防多买）；85 级满级免费 3 套时上限同步收紧至无卡 4 套 / 轻享卡 2 套
            var card = new SkillLoadoutView { ServerNowSec = 1000UL, LightCardBonusSchemes = 2u };
            Assert.That(SkillLoadoutRentPlanner.Plan(card, 4u, 1u).Valid, Is.False);
            Assert.That(SkillLoadoutRentPlanner.Plan(card, 3u, 1u).NewUnlockedSchemes, Is.EqualTo(7u));

            Assert.That(SkillLoadoutRentPlanner.FreeSchemesForLevel(84u), Is.EqualTo(2u));
            Assert.That(SkillLoadoutRentPlanner.FreeSchemesForLevel(85u), Is.EqualTo(3u));
            var lv85Free = new SkillLoadoutView { ServerNowSec = 1000UL, FreeSchemes = 3u };
            Assert.That(lv85Free.MaxRentableExtraSlots, Is.EqualTo(4u));
            Assert.That(SkillLoadoutRentPlanner.Plan(lv85Free, 5u, 1u).Valid, Is.False);
            Assert.That(SkillLoadoutRentPlanner.Plan(lv85Free, 4u, 1u).NewUnlockedSchemes, Is.EqualTo(7u));

            var lv85CardMidTerm = new SkillLoadoutView
            {
                ServerNowSec = 1000UL,
                FreeSchemes = 3u,
                LightCardBonusSchemes = 2u,
                RentedExtraSlots = 3u, // 84级轻享卡期间曾租3套，升85级后可按新上限2套续期不卡手
                RentExpiresAtSec = 1000UL + 10UL * day,
            };
            Assert.That(lv85CardMidTerm.MaxRentableExtraSlots, Is.EqualTo(2u));
            Assert.That(SkillLoadoutRentPlanner.Plan(lv85CardMidTerm, 3u, 1u).Valid, Is.False);
            var qRenewLv85 = SkillLoadoutRentPlanner.Plan(lv85CardMidTerm, 2u, 1u);
            Assert.That(qRenewLv85.Valid, Is.True);
            Assert.That(qRenewLv85.TotalCost, Is.EqualTo(60UL));
            Assert.That(qRenewLv85.NewUnlockedSchemes, Is.EqualTo(7u));

            // 剩余 151 天再续 30 天超过 180 天上限
            var nearCap = new SkillLoadoutView { ServerNowSec = 1000UL, RentedExtraSlots = 1u, RentExpiresAtSec = 1000UL + 151UL * day };
            var qCap = SkillLoadoutRentPlanner.Plan(nearCap, 1u, 1u);
            Assert.That(qCap.Valid, Is.False);
            Assert.That(qCap.ExceedsStackCap, Is.True);

            // 名称合规校验
            Assert.That(SkillLoadoutRentPlanner.ValidateDraftName("清怪爆发", out _), Is.True);
            Assert.That(SkillLoadoutRentPlanner.ValidateDraftName("一二三四五六七八九十甲乙丙", out _), Is.False);
            Assert.That(SkillLoadoutRentPlanner.ValidateDraftName("   ", out _), Is.False);
            Assert.That(SkillLoadoutRentPlanner.ValidateDraftName("<b>富文本</b>", out _), Is.False);
            Assert.That(SkillLoadoutRentPlanner.ValidateDraftName("官方GM", out _), Is.False);
        }

        private static SkillEntryState Entry(uint id, uint kind, uint group) =>
            new SkillEntryState { Id = id, Rank = 1, Definition = new SkillDefinition { id = id, abilityKind = kind, exclusiveGroup = group } };
    }
}
