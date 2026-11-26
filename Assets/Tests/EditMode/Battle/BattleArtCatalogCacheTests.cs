using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.UI.Ugui;
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
        public void BossTrialMonstersReuseDeliveredPetArtWithoutEnablingCapture()
        {
            for (uint monsterId = 101; monsterId <= 107; monsterId++)
            {
                uint mappedPetId = BattleArtCatalog.ResolveMonsterVisualPetTableId(monsterId);
                Assert.That(mappedPetId, Is.GreaterThan(0u), $"试炼怪物 {monsterId} 必须映射到已交付宝宝图");
                var petEntry = QdaoPetCatalog.Resolve(mappedPetId);
                Assert.That(petEntry, Is.Not.Null, $"试炼怪物 {monsterId} 映射的宝宝 {mappedPetId} 不在 QdaoPetCatalog");

                var actor = new BattleActorState
                {
                    ActorType = eBattleActorType.BattleActorTypeMonster,
                    MonsterTableId = monsterId,
                    CapturePetTableId = 0,
                };
                Assert.That(BattleArtCatalog.VisualPetTableId(actor), Is.EqualTo(mappedPetId));
                Assert.That(BattleArtCatalog.CharacterIdFor(actor), Is.EqualTo(petEntry.id));
                Assert.That(actor.CapturePetTableId, Is.Zero, "复用宝宝外观不得开启试炼怪捕捉资格");
                Assert.That(QdaoPetCatalog.HasAction(petEntry.id, "idle", "E"), Is.True, $"{petEntry.id} 缺少 idle/E");
                Assert.That(QdaoPetCatalog.HasAction(petEntry.id, "attack", "E"), Is.True, $"{petEntry.id} 缺少 attack/E");
                Assert.That(BattleArtCatalog.LoadMonsterPortrait(monsterId), Is.SameAs(QdaoPetCatalog.LoadPortrait(petEntry.id)));
                Assert.That(BattleArtCatalog.ResolveMonsterDisplayName(monsterId), Is.Not.Null.And.Not.Empty);
            }
            for (uint ordinary = 1; ordinary <= 6; ordinary++)
            {
                Assert.That(BattleArtCatalog.ResolveMonsterVisualPetTableId(ordinary), Is.Zero,
                    $"已有独立怪物贴图的普通怪 {ordinary} 不应被改写为宝宝图");
            }
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

        [Test]
        public void BossTrialV2DeliveredAssets_LoadMonsters109And110_ThreeFxStrips_And_AllEightUiSprites()
        {
            // 1. 策应卫 109/110 使用专属 Battle/Monsters/{109,110} 动作条与立绘，不再借用宝宝外观
            var expectedActions = new (string Action, float Fps)[]
            {
                ("idle", 6f), ("attack", 12f), ("hit", 12f), ("die", 8f),
            };
            foreach (uint monsterId in new[] { 109u, 110u })
            {
                Assert.That(BattleArtCatalog.ResolveMonsterVisualPetTableId(monsterId), Is.Zero,
                    $"已接入专属帧条的策应卫 {monsterId} 不得再路由到宝宝视觉");
                var actor = new BattleActorState
                {
                    ActorType = eBattleActorType.BattleActorTypeMonster,
                    MonsterTableId = monsterId,
                    CapturePetTableId = 0,
                };
                Assert.That(BattleArtCatalog.VisualPetTableId(actor), Is.Zero);
                Assert.That(BattleArtCatalog.ResolveMonsterDisplayName(monsterId), Is.Not.Null.And.Not.Empty);

                var portrait = BattleArtCatalog.LoadMonsterPortrait(monsterId);
                Assert.That(portrait != null && portrait.texture != null, Is.True, $"缺少 Monster/{monsterId}/portrait");
                Assert.That(portrait.texture.width, Is.EqualTo(256), $"Monster/{monsterId}/portrait 宽应为 256");
                Assert.That(portrait.texture.height, Is.EqualTo(256), $"Monster/{monsterId}/portrait 高应为 256");

                foreach (var (action, fps) in expectedActions)
                {
                    var east = BattleArtCatalog.LoadMonsterAction(monsterId, action, facingEast: true);
                    Assert.That(east, Is.Not.Null, $"缺少 Monster/{monsterId}/{action}_E_strip");
                    Assert.That(east.Count, Is.EqualTo(8), $"Monster/{monsterId}/{action} 应为 8 帧");
                    Assert.That(east.Fps, Is.EqualTo(fps).Within(0.01f));
                    Assert.That(east.Mirrored, Is.False);
                    Assert.That(east.Frames[0].rect.width, Is.EqualTo(256f));
                    Assert.That(east.Frames[0].rect.height, Is.EqualTo(256f));
                    Assert.That(east.Frames[0].texture.width, Is.EqualTo(2048));
                    Assert.That(east.Frames[0].texture.height, Is.EqualTo(256));

                    var west = BattleArtCatalog.LoadMonsterAction(monsterId, action, facingEast: false);
                    Assert.That(west, Is.Not.Null);
                    Assert.That(west.Count, Is.EqualTo(8));
                    Assert.That(west.Mirrored, Is.True, "缺 W 向时应自动水平镜像 E 向");
                }
            }

            // 2. 三条首领机制特效条（boss-trial-v2-runtime 统一 8 帧规格：2048x256 / 3072x384）
            var expectedFx = new (string Id, int Frames, int Cell, int Width)[]
            {
                ("boss_exposed_crack", 8, 256, 2048),
                ("boss_stance_break", 8, 384, 3072),
                ("boss_phase2_enrage", 8, 384, 3072),
            };
            foreach (var (fxId, frames, cell, width) in expectedFx)
            {
                var strip = BattleArtCatalog.LoadFx(fxId);
                Assert.That(strip, Is.Not.Null, $"缺少 Battle/Fx/{fxId}_strip");
                Assert.That(strip.Count, Is.EqualTo(frames), $"{fxId} 帧数不匹配（检查是否被缩至 4096）");
                Assert.That(strip.Fps, Is.EqualTo(12f).Within(0.01f));
                Assert.That(strip.Pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(strip.Frames[0].texture.width, Is.EqualTo(width), $"{fxId} 导入贴图宽被篡改");
                Assert.That(strip.Frames[0].texture.height, Is.EqualTo(cell), $"{fxId} 导入贴图高被篡改");
                Assert.That(strip.Frames[0].rect.width, Is.EqualTo(cell));
                Assert.That(strip.Frames[0].rect.height, Is.EqualTo(cell));
            }

            // 3. 8 张 UI 图标 / 徽章 / 双卫头像均按 Simple 固定比例无失真导入
            foreach (string icon in new[] { "icon_exposed", "icon_momentum", "icon_sunder", "icon_dishearten", "icon_focus", "icon_splash" })
            {
                var sprite = SeasonEconomyUiArt.LoadBossTrialV2Icon(icon);
                Assert.That(sprite != null && sprite.texture != null, Is.True, $"缺少 BossTrialV2/{icon}");
                Assert.That(sprite.texture.width, Is.EqualTo(64), icon);
                Assert.That(sprite.texture.height, Is.EqualTo(64), icon);
                Assert.That(sprite.border, Is.EqualTo(Vector4.zero), icon);
            }

            foreach (string badgeKey in new[]
            {
                "badge_trial_dual_guard",
                "badge_season_s1_trial",
                "badge_trial_grandmaster",
                "badge_trial_master",
                "badge_trial_sword",
                "badge_trial_three_bosses",
            })
            {
                var badge = SeasonEconomyUiArt.LoadTrialJournalBadge(badgeKey);
                Assert.That(badge != null && badge.texture != null, Is.True, "缺少 " + badgeKey);
                Assert.That(badge.texture.width, Is.EqualTo(128), badgeKey);
                Assert.That(badge.texture.height, Is.EqualTo(128), badgeKey);
                Assert.That(badge.border, Is.EqualTo(Vector4.zero), badgeKey);
            }

            var dualAvatar = SeasonEconomyUiArt.LoadIcon(SeasonEconomyUiArt.BossAvatarIconKey(8u));
            Assert.That(dualAvatar != null && dualAvatar.texture != null, Is.True, "缺少 boss_avatar_dual_guard");
            Assert.That(dualAvatar.texture.width, Is.EqualTo(256));
            Assert.That(dualAvatar.texture.height, Is.EqualTo(256));
            Assert.That(dualAvatar.border, Is.EqualTo(Vector4.zero));

            // 4. 5120 宽 FX 帧条导入上限规则校验（不破坏原有 Battle/strip.png 4096 约束）
            var importerType = System.Type.GetType(
                "MmorpgClient.Editor.Tianyong.QdaoCharacterSpriteImporter, Assembly-CSharp-Editor");
            Assert.That(importerType, Is.Not.Null);
            var requiredMax = importerType.GetMethod("RequiredMaxTextureSize",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(int), typeof(int) }, null);
            Assert.That(requiredMax, Is.Not.Null);
            Assert.That((int)requiredMax.Invoke(null,
                new object[] { "Assets/Resources/Battle/Fx/boss_stance_break_strip.png", 5120, 512 }), Is.EqualTo(8192));
            Assert.That((int)requiredMax.Invoke(null,
                new object[] { "Assets/Resources/Battle/Fx/boss_phase2_enrage_strip.png", 5120, 512 }), Is.EqualTo(8192));
            Assert.That((int)requiredMax.Invoke(null,
                new object[] { "Assets/Resources/Battle/Fx/boss_exposed_crack_strip.png", 2048, 256 }), Is.EqualTo(4096));
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
