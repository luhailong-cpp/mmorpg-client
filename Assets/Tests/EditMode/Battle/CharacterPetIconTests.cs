using System.Globalization;
using MmorpgClient.Game.Skills;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class CharacterPetIconTests
    {
        [TestCase(64)]
        [TestCase(128)]
        [TestCase(256)]
        public void EveryDeliveredPrototypeResolvesToItsOwnNativeSizeSprite(int size)
        {
            var sprites = Resources.LoadAll<Sprite>("UI/Ugui/CharacterPet/Icons/" + size);
            Assert.That(sprites, Has.Length.EqualTo(69), "导入清单的 69 个原型必须全部能由 Unity Resources 解码");
            foreach (var sprite in sprites)
            {
                uint id = TableId(sprite.name);
                Assert.That(sprite.rect.width, Is.EqualTo(size), sprite.name);
                Assert.That(sprite.rect.height, Is.EqualTo(size), sprite.name);
                Assert.That(CharacterPetArt.Skill(id, size), Is.SameAs(sprite), sprite.name + "不能错用其他原型画芯");
                Assert.That(CharacterPetArt.SkillPath(id, size), Does.EndWith("/" + size + "/" + sprite.name));
            }
        }

        [Test]
        public void CharacterAndPetBooksUseTheSameDeliveredIconForASharedSkill()
        {
            var character = new ListSkillsResponse();
            character.Catalog.Add(new SkillCatalogEntry { SkillTableId = 10010, Name = "青莲润生", IconKey = "UI/Skills/10010" });
            var pet = character.Clone(); pet.PetId = 900001;
            string characterKey = SkillBookMapper.ToSnapshot(character, _ => null).Find(10010).Definition.IconResource;
            string petKey = SkillBookMapper.ToSnapshot(pet, _ => null).Find(10010).Definition.IconResource;
            Assert.That(characterKey, Is.EqualTo(CharacterPetArt.SkillPath(10010)));
            Assert.That(petKey, Is.EqualTo(characterKey));
            Assert.That(Resources.Load<Sprite>(characterKey), Is.SameAs(CharacterPetArt.Skill(10010)));
            Assert.That(CharacterPetArt.Skill(999999), Is.Null, "未知技能不能借用其他正式原型图标");
        }

        private static uint TableId(string prototype)
        {
            Assert.That(prototype.Length, Is.GreaterThan(1));
            uint prefix = prototype[0] switch { 'A' => 10000u, 'B' => 11000u, 'P' => 12000u, 'T' => 13000u, 'O' => 14000u, _ => 0u };
            Assert.That(prefix, Is.Not.Zero, prototype);
            return prefix + uint.Parse(prototype.Substring(1), CultureInfo.InvariantCulture);
        }
    }
}
