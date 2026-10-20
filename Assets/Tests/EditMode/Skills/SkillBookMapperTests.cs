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
        public void RowsOfAnotherClassAreIgnored()
        {
            var book = OneRow(new SkillCatalogEntry { SkillTableId = 1101, ClassId = 1 }, classId: 3);
            Assert.That(SkillBookMapper.ToSnapshot(book, _ => null).Entries, Is.Empty);
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
    }
}
