using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.UI.Ugui.Battle;
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
            int index = BattleHudLogic.PortraitIndexFor(actorId, BattleArtCatalog.PortraitFiles.Length);
            string path = BattleArtCatalog.PortraitsRoot + "/" + BattleArtCatalog.PortraitFiles[index];
            CreateTemporarySprite();
            Cache<Texture2D>("s_textures")[path] = _temporaryTexture;
            Cache<Sprite>("s_sprites")[path + "#head"] = _temporarySprite;
            Object.DestroyImmediate(_temporaryTexture);

            var reloaded = BattleArtCatalog.LoadPlayerPortrait(actorId);

            Assert.That(reloaded != null, Is.True, "头像贴图失效后必须重新裁头，不保留已销毁的贴图句柄。");
            Assert.That(reloaded.texture != null, Is.True);
            Assert.That(ReferenceEquals(reloaded, _temporarySprite), Is.False);
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