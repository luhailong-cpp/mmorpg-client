using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongCityNpcTests
    {
        [Test]
        public void AuthoredRoster_LoadsAll23Textures_AndEveryFeetPointIsReachable()
        {
            var source = Resources.Load<TextAsset>(TianyongCityNpcs.PlacementsResourcePath);
            Assert.That(source, Is.Not.Null);
            var manifest = JsonUtility.FromJson<TianyongNpcPlacementManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.Validate(out var error), Is.True, error);
            Assert.That(manifest.entries.Length, Is.EqualTo(23));
            var navigation = TianyongPaintedCity.CreateNavigation();
            foreach (var entry in manifest.entries)
            {
                var texture = Resources.Load<Texture2D>(entry.spriteResource);
                Assert.That(texture, Is.Not.Null, entry.id + ": missing texture");
                Assert.That(texture.mipmapCount, Is.EqualTo(1), entry.id + ": unwanted sprite mipmaps");
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), entry.id);
                Assert.That(navigation.IsWalkable(entry.FeetPosition), Is.True, entry.id + ": feet are not on pavement");
                var path = navigation.FindPath(TianyongMapDefinition.DefaultSpawn, entry.FeetPosition);
                Assert.That(path, Is.Not.Empty, entry.id + ": unreachable from spawn");
                Assert.That(Vector3.Distance(path[path.Count - 1], entry.FeetPosition), Is.LessThan(.01f),
                    entry.id + ": route must reach the exact feet point, not a substitute nearby cell");
            }
        }

        [Test]
        public void ProductionCityBuild_OwnsOneNpcLayer_AndDisposeRebuildDoesNotLeak()
        {
            var parent = new GameObject("NpcLifecycleTestRoot");
            TianyongMapInstance map = null;
            try
            {
                map = TianyongMapBuilder.Build(parent.transform, TianyongTheme.City, null);
                var layer = map.Root.GetComponentInChildren<TianyongCityNpcs>(true);
                Assert.That(layer, Is.Not.Null);
                Assert.That(layer.Count, Is.EqualTo(23));
                Assert.That(layer.transform.parent, Is.SameAs(map.Root.transform));
                Assert.That(layer.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "visual NPCs must not alter click-to-move or navigation collisions");
                Assert.That(TianyongCityNpcs.Build(map), Is.SameAs(layer));
                Assert.That(map.Root.GetComponentsInChildren<TianyongCityNpcs>(true).Length, Is.EqualTo(1));
                var firstSprite = layer.transform.GetChild(0).Find("sprite").GetComponent<SpriteRenderer>().sprite;
                map.Dispose();
                map = null;
                Assert.That(layer == null, Is.True, "map disposal must destroy its NPC layer");
                Assert.That(firstSprite == null, Is.True, "map disposal must destroy runtime-created NPC sprites");
                Assert.That(parent.GetComponentsInChildren<TianyongCityNpcs>(true), Is.Empty);

                map = TianyongMapBuilder.Build(parent.transform, TianyongTheme.City, null);
                var rebuilt = parent.GetComponentsInChildren<TianyongCityNpcs>(true);
                Assert.That(rebuilt.Length, Is.EqualTo(1));
                Assert.That(rebuilt[0].Count, Is.EqualTo(23));
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        [TestCase(TianyongTheme.Market)]
        [TestCase(TianyongTheme.Snow)]
        [TestCase(TianyongTheme.Lantern)]
        public void OtherThemes_DoNotReceivePaintedCityNpcs(TianyongTheme theme)
        {
            var parent = new GameObject("NpcThemeScopeTestRoot");
            TianyongMapInstance map = null;
            try
            {
                map = TianyongMapBuilder.Build(parent.transform, theme, null);
                Assert.That(map.Root.GetComponentsInChildren<TianyongCityNpcs>(true), Is.Empty);
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }
    }
}
