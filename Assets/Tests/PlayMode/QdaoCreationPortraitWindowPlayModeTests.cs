using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    public sealed partial class QdaoRoleIdentityPlayModeTests
    {
        private const string WindowPreferred = "08_alchemy_prodigy_boy";
        private const string WindowDefault = "04_mountain_guardian_boy";
        private const string WindowThird = "03_lotus_healer_girl";

        [UnityTest]
        public IEnumerator CreationWindow_ShowsCurrentImmediatelyAndPrefetchesNeighboursAfterTheClickFrame()
        {
            using var fixture = new CreationWindowFixture();
            int openingFrame = Time.frameCount;
            fixture.Open();
            Assert.That(fixture.Hero.sprite, Is.Not.Null);
            Assert.That(fixture.VisibleId, Is.EqualTo(WindowPreferred));
            Assert.That(fixture.Loads.Select(row => row.Id), Is.EqualTo(new[] { WindowPreferred }),
                "The current portrait must be visible before either neighbour is loaded.");
            Assert.That(fixture.Prefetches, Is.Empty, "The click frame must not start reading a neighbour.");
            for (int i = 0; i < 4; i++) yield return null;
            fixture.AssertResident(WindowPreferred, WindowDefault, First);
            // 邻居纹理走异步读取：开页后的第一帧按“先上后下”发出两个读取，读完才收进窗口。
            Assert.That(fixture.Prefetches, Is.EqualTo(new[] { (WindowDefault, openingFrame + 1), (First, openingFrame + 1) }));
            Assert.That(fixture.Loads.Skip(1).Select(row => row.Id), Is.EqualTo(new[] { WindowDefault, First }));
            Assert.That(fixture.Loads.Skip(1).All(row => row.Frame > openingFrame), Is.True);

            var cachedNext = fixture.Window.GetOrLoad(First);
            int priorLoads = fixture.Loads.Count;
            Click(fixture.Host, "NextAppearance");
            Assert.That(fixture.Hero.sprite, Is.SameAs(cachedNext));
            Assert.That(fixture.Loads.Count, Is.EqualTo(priorLoads), "Switching to a warm neighbour must reuse its lease immediately.");
            for (int i = 0; i < 3; i++) yield return null;
            fixture.AssertResident(WindowPreferred, First, Second);
            Assert.That(fixture.Released, Does.Contain(WindowDefault));
            Assert.That(fixture.Loads.Skip(priorLoads).Select(row => row.Id), Is.EqualTo(new[] { Second }));
        }

        [UnityTest]
        public IEnumerator CreationWindow_PrefetchCompletingAfterSelectionMovedOnIsNotAdopted()
        {
            using var fixture = new CreationWindowFixture { DeferPrefetch = true };
            fixture.Open();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(fixture.Prefetches.Select(row => row.Id), Is.EqualTo(new[] { WindowDefault, First }));
            Assert.That(fixture.Window.Count, Is.EqualTo(1), "A neighbour joins the window only after its read completes.");

            // 读取还没完成就往后翻：当前人物同步取，缺的下一位再发一个读取，已离开窗口的那位读完也不收。
            Click(fixture.Host, "NextAppearance");
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(fixture.Prefetches.Select(row => row.Id), Is.EqualTo(new[] { WindowDefault, First, Second }),
                "A portrait already being read is not requested twice.");
            fixture.CompletePrefetches();
            yield return null;
            fixture.AssertResident(First, WindowPreferred, Second);
            Assert.That(fixture.Loads.Select(row => row.Id), Is.EqualTo(new[] { WindowPreferred, First, Second }),
                "A read for a portrait that left the window must not be adopted.");
        }

        [UnityTest]
        public IEnumerator CreationWindow_ForwardBackwardAndWrapStayWithinThreeAndDeduplicateDefaultIdentity()
        {
            using var fixture = new CreationWindowFixture();
            fixture.Open();
            for (int i = 0; i < 3; i++) yield return null;
            string[] ring = { WindowDefault, WindowPreferred, First, Second, WindowThird, WindowDefault };
            int slot = 1;
            foreach (int direction in new[] { 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1 })
            {
                Click(fixture.Host, direction > 0 ? "NextAppearance" : "PreviousAppearance");
                slot = (slot + direction + ring.Length) % ring.Length;
                Assert.That(fixture.VisibleId, Is.EqualTo(ring[slot]));
                for (int i = 0; i < 3; i++) yield return null;
                fixture.AssertResident(ring[slot], ring[(slot + ring.Length - 1) % ring.Length], ring[(slot + 1) % ring.Length]);
            }
            Assert.That(fixture.MaxResident, Is.LessThanOrEqualTo(3), "Eviction must happen before loading a fourth identity.");
            Click(fixture.Host, "PreviousAppearance"); // Slot zero: explicit profession default, same identity as the final slot.
            for (int i = 0; i < 3; i++) yield return null;
            fixture.AssertResident(WindowDefault, WindowPreferred);
            Assert.That(ReadText(fixture.Host, "AppearanceName"), Does.Contain("职业默认"));
            var defaultSprite = fixture.Hero.sprite;
            int loads = fixture.Loads.Count;
            Click(fixture.Host, "PreviousAppearance"); // Wrap to the explicit appearance with the same ID.
            Assert.That(fixture.Hero.sprite, Is.SameAs(defaultSprite));
            Assert.That(fixture.Loads.Count, Is.EqualTo(loads));
            for (int i = 0; i < 3; i++) yield return null;
            fixture.AssertResident(WindowDefault, WindowThird);
        }

        [UnityTest]
        public IEnumerator CreationWindow_RapidSwitchCancelsOutdatedNeighbourPreloads()
        {
            using var fixture = new CreationWindowFixture();
            fixture.Open();
            Click(fixture.Host, "NextAppearance");
            Click(fixture.Host, "NextAppearance");
            int afterClicks = fixture.Loads.Count;
            Assert.That(fixture.VisibleId, Is.EqualTo(Second));
            for (int i = 0; i < 4; i++) yield return null;
            fixture.AssertResident(First, Second, WindowThird);
            Assert.That(fixture.Loads.Skip(afterClicks).Select(row => row.Id), Is.EqualTo(new[] { WindowThird }),
                "Only the latest selection's missing neighbour may preload after rapid clicks.");
            Assert.That(fixture.Loads.Any(row => row.Id == WindowDefault), Is.False,
                "The initial selection's obsolete previous neighbour must never start loading.");
        }

        [UnityTest]
        public IEnumerator CreationWindow_TravelDirectionLoadsMissingNeighbourOnFirstLaterFrame()
        {
            using var fixture = new CreationWindowFixture();
            fixture.Open();
            for (int i = 0; i < 3; i++) yield return null;
            fixture.AssertResident(WindowPreferred, WindowDefault, First);

            // 往后翻：上一位就是刚才的当前人物，早已在窗口里，不能让它白占一帧。
            int clickFrame = Time.frameCount;
            int loads = fixture.Loads.Count;
            Click(fixture.Host, "NextAppearance");
            for (int i = 0; i < 3; i++) yield return null;
            fixture.AssertResident(First, WindowPreferred, Second);
            Assert.That(fixture.Loads.Skip(loads), Is.EqualTo(new[] { (Second, clickFrame + 1) }),
                "Forward travel must load the missing next portrait on the first frame after the click.");

            // 往前翻：缺的上一位同样在点击后的第一帧补上。
            clickFrame = Time.frameCount;
            loads = fixture.Loads.Count;
            Click(fixture.Host, "PreviousAppearance");
            for (int i = 0; i < 3; i++) yield return null;
            fixture.AssertResident(WindowPreferred, WindowDefault, First);
            Assert.That(fixture.Loads.Skip(loads), Is.EqualTo(new[] { (WindowDefault, clickFrame + 1) }),
                "Backward travel must load the missing previous portrait on the first frame after the click.");
        }

        [UnityTest]
        public IEnumerator CreationWindow_LeavingForSelectionCancellingDisablingOrDestroyingReleasesEveryLease()
        {
            foreach (string exit in new[] { "selection", "cancel", "disable", "destroy" })
            {
                using var fixture = new CreationWindowFixture();
                fixture.Open(exit == "selection");
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(fixture.Live.Count, Is.EqualTo(3), exit);
                int loads = fixture.Loads.Count;
                switch (exit)
                {
                    case "selection": Click(fixture.Host, "BackFromCreate"); break;
                    case "cancel":
                        Click(fixture.Host, "BackFromCreate");
                        Assert.That(fixture.Choosing.MoveNext(), Is.False);
                        break;
                    case "disable": fixture.Host.SetActive(false); break;
                    default: UnityEngine.Object.Destroy(fixture.Host); break;
                }
                yield return null;
                Assert.That(fixture.Live, Is.Empty, exit + " must release all creation textures.");
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(fixture.Loads.Count, Is.EqualTo(loads), exit + " must also stop deferred preloads.");
            }
        }

        [UnityTest]
        public IEnumerator CreationWindow_ImportRevisionDropsOldLeasesPreservesNameAndRejectsMissingCurrent()
        {
            using var fixture = new CreationWindowFixture();
            fixture.Open();
            for (int i = 0; i < 3; i++) yield return null;
            var input = fixture.Host.GetComponentsInChildren<TMPro.TMP_InputField>(true).Single();
            input.text = "云行客";
            var oldTextures = fixture.Live.Keys.ToArray();
            fixture.Available.Remove(WindowPreferred);
            QdaoCharacterCatalog.RefreshAppearances();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(oldTextures.All(texture => !fixture.Live.ContainsKey(texture)), Is.True);
            Assert.That(input.text, Is.EqualTo("云行客"));
            Assert.That(ReadText(fixture.Host, "AppearanceName"), Does.Contain("炼丹童子"));
            Assert.That(fixture.Hero.enabled, Is.False);
            fixture.Button("ConfirmCreate").onClick.Invoke();
            Assert.That(fixture.Choice.CreateNew, Is.False);

            fixture.Available[WindowPreferred] = QdaoHdResourceLifetimePlayModeTests.Hd(WindowPreferred);
            QdaoCharacterCatalog.RefreshAppearances();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(fixture.VisibleId, Is.EqualTo(WindowPreferred));
            Assert.That(fixture.Button("ConfirmCreate").IsInteractable(), Is.True);
            Assert.That(input.text, Is.EqualTo("云行客"));
            Assert.That(fixture.MaxResident, Is.LessThanOrEqualTo(3));
        }

        private sealed class CreationWindowFixture : IDisposable
        {
            public readonly GameObject Host = new("CreationPortraitSlidingWindowFixture");
            public readonly RoleFlowUi Ui;
            public readonly GameClient.PlayerChoice Choice = new();
            public readonly Dictionary<string, QdaoCharacterCatalog.Appearance> Available = new();
            public readonly List<(string Id, int Frame)> Loads = new();
            public readonly List<(string Id, int Frame)> Prefetches = new();
            public readonly List<string> Released = new();
            public readonly Dictionary<Texture2D, string> Live = new();
            private readonly List<Action> _pendingPrefetches = new();
            public int MaxResident;
            // false = 读取当场完成；true = 读取挂起，直到测试调用 CompletePrefetches。
            public bool DeferPrefetch;
            public IEnumerator Choosing;
            public CreationPortraitWindow Window => (CreationPortraitWindow)typeof(RoleFlowUi)
                .GetField("_creationPortraitWindow", Hidden).GetValue(Ui);
            public Image Hero => FindImage(Host, "CharacterArtwork");
            public string VisibleId => Hero.sprite != null && Live.TryGetValue(Hero.sprite.texture, out var id) ? id : null;

            public CreationWindowFixture()
            {
                foreach (string id in new[] { WindowPreferred, First, Second, WindowThird, WindowDefault })
                    Available[id] = QdaoHdResourceLifetimePlayModeTests.Hd(id);
                Ui = Host.AddComponent<RoleFlowUi>();
                QdaoCharacterCatalog.Appearance Resolve(QdaoCharacterCatalog.Definition definition)
                    => definition != null && Available.TryGetValue(definition.Id, out var value) ? value : null;
                Func<QdaoCharacterCatalog.Definition, QdaoCharacterCatalog.Appearance> appearance = Resolve;
                Func<string, QdaoCharacterCatalog.CreationPortraitLease> acquire =
                    id => QdaoCharacterCatalog.AcquireCreationPortrait(id, Resolve, Load, Release);
                Action<string, Action> prefetch = (id, completed) =>
                {
                    Prefetches.Add((id, Time.frameCount));
                    if (DeferPrefetch) _pendingPrefetches.Add(completed);
                    else completed();
                };
                typeof(RoleFlowUi).GetField("_appearanceResolver", Hidden).SetValue(Ui, appearance);
                typeof(RoleFlowUi).GetField("_creationPortraitAcquire", Hidden).SetValue(Ui, acquire);
                typeof(RoleFlowUi).GetField("_creationPortraitPrefetch", Hidden).SetValue(Ui, prefetch);
                typeof(RoleFlowUi).GetMethod("BuildCanvas", Hidden).Invoke(Ui, null);
            }

            public void CompletePrefetches()
            {
                var pending = _pendingPrefetches.ToArray();
                _pendingPrefetches.Clear();
                foreach (var completed in pending) completed();
            }

            public void Open(bool withExistingRole = false)
            {
                IReadOnlyList<AccountSimplePlayer> players = withExistingRole
                    ? new[] { new AccountSimplePlayer { PlayerId = 314, ClassId = 1, Gender = 1, AppearanceId = First, Name = "旧道友" } }
                    : Array.Empty<AccountSimplePlayer>();
                Choosing = Choose(Ui, players, Choice);
                Assert.That(Choosing.MoveNext(), Is.True);
                if (withExistingRole) Click(Host, "CreateNewRole");
            }

            private Texture2D Load(string path)
            {
                Assert.That(path, Does.EndWith("/portrait"), "Creation preloads cannot load animation frames.");
                string id = path.Split('/')[3];
                Loads.Add((id, Time.frameCount));
                var texture = new Texture2D(1024, 1024) { name = id + "-window-fixture" };
                Live.Add(texture, id);
                MaxResident = Math.Max(MaxResident, Live.Count);
                Assert.That(Live.Count, Is.LessThanOrEqualTo(3), "A fourth texture was allocated before the farthest one was released.");
                return texture;
            }

            private void Release(Texture2D texture)
            {
                Assert.That(Live.TryGetValue(texture, out var id), Is.True, "Each acquired texture must release exactly once.");
                Live.Remove(texture);
                Released.Add(id);
                UnityEngine.Object.Destroy(texture);
            }

            public Button Button(string name) => Host.GetComponentsInChildren<Button>(true).Single(button => button.name == name);

            public void AssertResident(params string[] identities)
            {
                var expected = identities.Distinct().ToArray();
                Assert.That(Window.CachedIds, Is.EquivalentTo(expected));
                Assert.That(Window.Count, Is.EqualTo(expected.Length));
                Assert.That(Live.Values, Is.EquivalentTo(expected));
                Assert.That(MaxResident, Is.LessThanOrEqualTo(3));
            }

            public void Dispose()
            {
                _pendingPrefetches.Clear();
                if (Host != null) { Host.SetActive(false); UnityEngine.Object.Destroy(Host); }
                foreach (var texture in Live.Keys.ToArray()) UnityEngine.Object.Destroy(texture);
                Live.Clear();
                QdaoCharacterCatalog.RefreshAppearances();
            }
        }
    }
}
