using System.Linq;
using System.Text;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Social;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Social
{
    public sealed class EquipShareTextTests
    {
        private static BagItemInfo Equipment(string name="青锋剑") => new BagItemInfo
        { ItemId=42,ConfigId=1102,Count=1,EquipKind=11,Name=name,EquipLevel=20,EquipClass=2,EquipClassName="玄霄" };

        [Test]
        public void DraftUsesAuthoritativeRequirementsAndSortedAttributes()
        {
            var item=Equipment();
            item.Affixes.Add(new EquipAttrLineInfo{Name="物理必杀率",Tier=2,Value=8,Cap=10,Percent=true});
            item.BaseAttrs.Add(new EquipAttrLineInfo{Name="伤害",Value=420});
            item.Affixes.Add(new EquipAttrLineInfo{Name="力量",Tier=1,Value=3,Cap=5});
            Assert.That(EquipShareText.Build(item),Is.EqualTo("【青锋剑】 20级 · 玄霄；伤害：420；力量 3/5；物理必杀率 8%/10%"));
        }

        [Test]
        public void LongChineseTextFitsTheActualChatContract()
        {
            string result=EquipShareText.Build(Equipment(new string('剑',300)));
            Assert.That(SocialState.TextLength(result),Is.EqualTo(SocialState.MessageLimit));
            Assert.That(result,Does.EndWith("…"));
            Assert.That(SocialState.ValidMessage(result),Is.True);
        }

        [Test]
        public void EmojiAtTruncationBoundaryKeepsItsSurrogatePair()
        {
            string result=EquipShareText.Build(Equipment(string.Concat(Enumerable.Repeat("🐉",200))));
            Assert.That(SocialState.ValidMessage(result),Is.True);
            Assert.That(result,Does.EndWith("…"));
            Assert.That(new UTF8Encoding(false,true).GetBytes(result).Length,Is.LessThanOrEqualTo(512));
            Assert.That(result.Substring(1,result.Length-2),Is.EqualTo(string.Concat(Enumerable.Repeat("🐉",118))));
        }

        [Test]
        public void CombiningMarksAreNotSeparatedFromTheirLetter()
        {
            string result=EquipShareText.Build(Equipment(string.Concat(Enumerable.Repeat("e\u0301",100))));
            Assert.That(result,Does.EndWith("e\u0301…"));
            Assert.That(SocialState.ValidMessage(result),Is.True);
        }

        [TestCase("尚未发送的话")]
        [TestCase(" \n ")]
        [TestCase("🐉 原草稿")]
        public void ExistingDraftIsPreservedExactly(string original)
        {
            Assert.That(EquipShareText.TryCreateDraft(Equipment(),original,out var result),Is.False);
            Assert.That(result,Is.EqualTo(original));
        }

        [Test]
        public void EmptyDraftReceivesTextWithoutChangingEquipment()
        {
            var item=Equipment();var snapshot=item.Clone();
            Assert.That(EquipShareText.TryCreateDraft(item,null,out var result),Is.True);
            Assert.That(result,Is.EqualTo(EquipShareText.Build(item)));
            Assert.That(item,Is.EqualTo(snapshot));
        }

        [Test]
        public void MissingNamesUseIdentifiersWithoutInventingMetadata()
        {
            var item=Equipment("");item.EquipClassName="";
            Assert.That(EquipShareText.Build(item),Is.EqualTo("【装备 #1102】 20级 · 职业 #2"));
        }

        [Test]
        public void InvalidOrNonEquipmentItemsDoNotCreateDrafts()
        {
            Assert.That(EquipShareText.TryCreateDraft(null,"",out var result),Is.False);
            Assert.That(result,Is.Empty);
            var item=Equipment();item.EquipKind=0;
            Assert.That(EquipShareText.TryCreateDraft(item,"",out result),Is.False);
            item.EquipKind=11;item.ItemId=0;
            Assert.That(EquipShareText.Build(item),Is.Empty);
        }
    }
}
