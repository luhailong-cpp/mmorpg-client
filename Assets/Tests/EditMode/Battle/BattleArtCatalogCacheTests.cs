using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class BattleArtCatalogCacheTests
    {
        private Texture2D _temporaryTexture;
        private Sprite _temporarySprite;

        [SetUp]
        public void SetUp() => BattleArtCatalog.ResetCaches();

        [TearDown]
        public void TearDown()
        {
            BattleArtCatalog.ResetCaches();
            if (_temporarySprite != null) Object.DestroyImmediate(_temporarySprite);
            if (_temporaryTexture != null) Object.DestroyImmediate(_temporaryTexture);
        }

        [Test]
        public void CapturableMonsterUsesTheAwardedSpeciesArtAndPortrait()
        {
            var wild = new BattleActorState { ActorType = eBattleActorType.BattleActorTypeMonster,
                MonsterTableId = 1, CapturePetTableId = 2 };
            var pet = new BattleActorState { ActorType = eBattleActorType.BattleActorTypePet, PetTableId = 2 };
            Assert.That(BattleArtCatalog.CharacterIdFor(wild), Is.EqualTo(BattleArtCatalog.CharacterIdFor(pet)));
            Assert.That(BattleArtCatalog.LoadPlayerPortrait(wild, null), Is.SameAs(BattleArtCatalog.LoadPlayerPortrait(pet, null)));
            wild.CapturePetTableId = 0;
            Assert.That(BattleArtCatalog.VisualPetTableId(wild), Is.Zero, "普通副本怪物仍使用怪物美术。");
        }

        [Test]
        public void SpawnRingReloadsAfterCachedSpriteIsDestroyed()
        {
            CreateTemporarySprite();
            Cache<Sprite>("s_sprites")[BattleArtCatalog.SpawnRingFriendlyPath] = _temporarySprite;
            Object.DestroyImmediate(_temporarySprite);

            var reloaded = BattleArtCatalog.LoadSpawnRing(true);

            Assert.That(reloaded != null, Is.True, "已销毁的光环缓存必须重新读取，不能退化为纯色矩形。");
            Assert.That(reloaded.texture != null, Is.True);
            Assert.That(ReferenceEquals(reloaded, _temporarySprite), Is.False);
        }

        [Test]
        public void PortraitReloadsAfterCachedTextureIsDestroyed()
        {
            const ulong actorId = 1;
            var appearance = QdaoCharacterCatalog.Find(QdaoCharacterCatalog.DefaultId).ResolveAppearance();
            Assert.That(appearance, Is.Not.Null);
            CreateTemporarySprite();
            var portraits = (Dictionary<string, Sprite>)typeof(QdaoCharacterCatalog)
                .GetField("Portraits", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            portraits[appearance.CacheKey] = _temporarySprite;
            Object.DestroyImmediate(_temporaryTexture);

            var reloaded = BattleArtCatalog.LoadPlayerPortrait(actorId);

            Assert.That(reloaded != null, Is.True, "当前人物头像缓存失效后必须重建，不保留已销毁的贴图句柄。");
            Assert.That(reloaded.texture != null, Is.True);
            Assert.That(ReferenceEquals(reloaded, _temporarySprite), Is.False);
            Assert.That(reloaded, Is.SameAs(portraits[appearance.CacheKey]));
        }

        [Test]
        public void ReleaseTransientBattleCachesEvictsMonstersAndFxWhilePreservingHud()
        {
            CreateTemporarySprite();
            string hudKey = $"{BattleArtCatalog.UiRoot}/test_hud";
            string fxKey = $"{BattleArtCatalog.FxRoot}/test_fx_strip";
            Cache<Sprite>("s_sprites")[hudKey] = _temporarySprite;
            Cache<Sprite>("s_sprites")[BattleArtCatalog.ArenaBackgroundPath] = _temporarySprite;
            Cache<Texture2D>("s_textures")[fxKey] = null;
            var strips = (Dictionary<string, StripAnim>)typeof(BattleArtCatalog)
                .GetField("s_strips", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            strips[$"{fxKey}|0|0.5,0.5"] = null;

            var ring = BattleArtCatalog.RingSprite;
            var spawnRing = BattleArtCatalog.LoadSpawnRing(true);
            var silhouette = BattleArtCatalog.GetMonsterSilhouette(777);
            var silhouetteTexture = silhouette.texture;
            Assert.That(spawnRing != null, Is.True);
            Assert.That(silhouette != null && silhouetteTexture != null, Is.True);

            BattleArtCatalog.ReleaseTransientBattleCaches();

            Assert.That(strips, Is.Empty, "瞬态战斗帧条缓存必须随战斗结束释放");
            Assert.That(Cache<Texture2D>("s_textures").ContainsKey(fxKey), Is.False, "特效贴图缓存必须移除");
            Assert.That(Cache<Sprite>("s_sprites").ContainsKey(BattleArtCatalog.SpawnRingFriendlyPath), Is.False, "瞬态光环缓存必须移除");
            Assert.That(silhouette == null, Is.True, "程序化怪物剪影 Sprite 必须销毁");
            Assert.That(silhouetteTexture == null, Is.True, "程序化怪物剪影 Texture2D 必须销毁");

            // 常驻 HUD 缓存不得被误清或误毁。
            Assert.That(Cache<Sprite>("s_sprites")[hudKey], Is.SameAs(_temporarySprite));
            Assert.That(Cache<Sprite>("s_sprites")[BattleArtCatalog.ArenaBackgroundPath], Is.SameAs(_temporarySprite));
            Assert.That(BattleArtCatalog.RingSprite, Is.SameAs(ring));
            Assert.That(_temporarySprite != null && _temporaryTexture != null, Is.True);

            // 下一局战斗按需重新加载应正常工作。
            var reloadedRing = BattleArtCatalog.LoadSpawnRing(true);
            var reloadedSilhouette = BattleArtCatalog.GetMonsterSilhouette(777);
            Assert.That(reloadedRing != null && reloadedRing.texture != null, Is.True);
            Assert.That(reloadedSilhouette != null && reloadedSilhouette.texture != null, Is.True);
            BattleArtCatalog.ReleaseTransientBattleCaches();
        }

        private void CreateTemporarySprite()
        {
            _temporaryTexture = new Texture2D(4, 4);
            _temporarySprite = Sprite.Create(_temporaryTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f);
        }

        private static Dictionary<string, T> Cache<T>(string field) where T : Object
            => (Dictionary<string, T>)typeof(BattleArtCatalog)
                .GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    }
}
