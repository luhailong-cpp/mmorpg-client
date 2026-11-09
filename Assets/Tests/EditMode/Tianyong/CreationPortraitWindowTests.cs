using System.Collections.Generic;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class CreationPortraitWindowTests
    {
        [Test]
        public void SlidingWindowReusesOverlapReleasesOutgoingBeforeAcquireAndRejectsStalePreload()
        {
            var sprites = new Dictionary<string, Sprite>();
            var acquired = new List<string>();
            var released = new List<string>();
            var live = new HashSet<string>();
            QdaoCharacterCatalog.CreationPortraitLease Acquire(string id)
            {
                acquired.Add(id);
                Assert.That(live.Add(id), Is.True);
                Assert.That(live.Count, Is.LessThanOrEqualTo(3));
                if (!sprites.TryGetValue(id, out var sprite))
                {
                    var texture = new Texture2D(4, 4);
                    sprites[id] = sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
                }
                return QdaoCharacterCatalog.CreationPortraitLease.Borrow(sprite, release: () =>
                {
                    Assert.That(live.Remove(id), Is.True);
                    released.Add(id);
                });
            }
            using var window = new CreationPortraitWindow(Acquire, () => 1);
            try
            {
                window.SetWindow("B", "A", "C");
                window.GetOrLoad("B"); window.GetOrLoad("A"); window.GetOrLoad("C");
                var current = window.GetOrLoad("C");
                window.SetWindow("C", "B", "D");
                Assert.That(released, Is.EqualTo(new[] { "A" }));
                Assert.That(window.GetOrLoad("C"), Is.SameAs(current));
                Assert.That(window.GetOrLoad("A"), Is.Null, "An old coroutine cannot reinsert a portrait outside the latest window.");
                window.GetOrLoad("D");
                Assert.That(acquired, Is.EqualTo(new[] { "B", "A", "C", "D" }));
                Assert.That(window.CachedIds, Is.EquivalentTo(new[] { "B", "C", "D" }));
                window.SetWindow("C", "C", "B");
                Assert.That(window.Count, Is.EqualTo(2));
                window.GetOrLoad("C");
                Assert.That(acquired.Count, Is.EqualTo(4), "Two selector positions for one identity share one lease.");
                window.Clear(); window.Dispose(); window.Dispose();
                Assert.That(live, Is.Empty);
                Assert.That(released, Is.EquivalentTo(new[] { "A", "B", "C", "D" }));
            }
            finally
            {
                window.Dispose();
                foreach (var sprite in sprites.Values)
                {
                    Object.DestroyImmediate(sprite.texture);
                    Object.DestroyImmediate(sprite);
                }
            }
        }

        [Test]
        public void SlidingWindowRevisionAndInvalidPortraitReleaseCachedLeaseBeforeRetry()
        {
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            int revision = 1, acquisitions = 0, releases = 0;
            bool valid = true;
            QdaoCharacterCatalog.CreationPortraitLease Acquire(string _)
            {
                acquisitions++;
                return valid ? QdaoCharacterCatalog.CreationPortraitLease.Borrow(sprite, () => valid, () => releases++) : null;
            }
            using var window = new CreationPortraitWindow(Acquire, () => revision);
            try
            {
                window.SetWindow("A", "A", "A");
                Assert.That(window.GetOrLoad("A"), Is.SameAs(sprite));
                valid = false;
                Assert.That(window.GetOrLoad("A"), Is.Null);
                Assert.That(window.Count, Is.Zero);
                Assert.That(releases, Is.EqualTo(1));
                valid = true;
                Assert.That(window.GetOrLoad("A"), Is.SameAs(sprite));
                revision++;
                Assert.That(window.GetOrLoad("A"), Is.SameAs(sprite));
                Assert.That(releases, Is.EqualTo(2));
                Assert.That(acquisitions, Is.EqualTo(4));
                window.Clear();
                Assert.That(releases, Is.EqualTo(3));
            }
            finally { window.Dispose(); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }
    }
}
