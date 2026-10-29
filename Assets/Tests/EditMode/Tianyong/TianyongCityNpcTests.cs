using MmorpgClient.World.CurrentMaps;
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
                Assert.That(entry.nativeWidth, Is.GreaterThan(0), entry.id + ": source width is missing");
                Assert.That(entry.nativeHeight, Is.GreaterThan(0), entry.id + ": source height is missing");
                Assert.That(texture.width, Is.EqualTo(entry.nativeWidth), entry.id + ": importer resized width");
                Assert.That(texture.height, Is.EqualTo(entry.nativeHeight), entry.id + ": importer resized height");
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
        public void CurrentMainBuild_OwnsItsVisuals_AndDisposeRebuildDoesNotLeak()
        {
            var parent = new GameObject("CurrentMapLifecycleTestRoot");
            TianyongMapInstance map = null;
            try
            {
                var definition = CurrentMapCatalog.Find("main");
                map = CurrentMapBuilder.Build(parent.transform, definition);
                var firstRoot = map.Root;
                var ground = firstRoot.transform.Find(CurrentMapBuilder.GroundName);
                Assert.That(ground, Is.Not.Null);
                var firstMaterial = ground.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(firstMaterial.mainTexture, Is.SameAs(definition.LoadTexture()));
                Assert.That(firstRoot.GetComponentsInChildren<CurrentMapTileStreaming>(true).Length, Is.EqualTo(1));
                Assert.That(firstRoot.GetComponentsInChildren<TianyongCityNpcs>(true), Is.Empty,
                    "当前布局不能注入与其导航不匹配的旧城NPC摆放");
                Assert.That(map.Navigation.IsWalkable(definition.Spawn), Is.True);
                map.Dispose();
                map = null;
                Assert.That(firstRoot == null, Is.True);
                Assert.That(firstMaterial == null, Is.True, "地图拥有的运行时材质必须释放");
                Assert.That(parent.transform.childCount, Is.Zero);
                map = CurrentMapBuilder.Build(parent.transform, definition);
                Assert.That(parent.transform.childCount, Is.EqualTo(1));
                Assert.That(parent.GetComponentsInChildren<CurrentMapTileStreaming>(true).Length, Is.EqualTo(1));
                Assert.That(parent.GetComponentsInChildren<TianyongCityNpcs>(true), Is.Empty);
            }
            finally { map?.Dispose(); Object.DestroyImmediate(parent); }
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
