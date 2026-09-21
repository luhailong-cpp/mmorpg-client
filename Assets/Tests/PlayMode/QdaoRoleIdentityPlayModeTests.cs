using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>Actual UI and resource rendering with disconnected role fixtures; not a network acceptance test.</summary>
    public sealed class QdaoRoleIdentityPlayModeTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string First = "00_reference_topright_boy";
        private const string Second = "01_ice_sword_girl";

        [UnityTest]
        public IEnumerator MissingChangedIdentity_ClearsPreviousWorldAndBattleBody_AndCanRecover()
        {
            string selected = First;
            var world = new ActorWorld("MissingIdentityWorld");
            world.AppearanceProvider = _ => selected;
            var host = new GameObject("MissingIdentityBattle", typeof(RectTransform), typeof(Canvas));
            var view = new BattleUnitView(null, host.transform, 101, true, true, 0, null);
            try
            {
                world.SpawnActor(10, ActorKind.Player, 0, UnityEngine.Vector3.zero, UnityEngine.Vector3.zero, 101);
                var actor = world.Actors[10];
                var animator = actor.Go.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = actor.Go.transform.Find("sprite").GetComponent<SpriteRenderer>();
                foreach (var missing in new[] { "04_mountain_guardian_boy", "future_unavailable_identity" })
                {
                    selected = First;
                    world.RefreshAppearances();
                    view.Apply(new BattleActorState { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = First });
                    var previousBody = FindImage(host, "Body").sprite;
                    Assert.That(renderer.sprite, Is.Not.Null);
                    selected = missing;
                    world.RefreshAppearances();
                    yield return null;
                    Assert.That(actor.CharacterId, Is.EqualTo(missing));
                    Assert.That(animator.CharacterId, Is.EqualTo(missing));
                    Assert.That(renderer.sprite, Is.Null, "A missing different identity must not keep the last actor's artwork.");
                    Assert.That(actor.Go.GetComponent<MeshRenderer>().enabled, Is.True);
                    view.Apply(new BattleActorState { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = missing });
                    var body = FindImage(host, "Body").sprite;
                    Assert.That(body, Is.Not.SameAs(previousBody));
                    // 04 retains its historical same-ID battle art; an unknown ID gets a placeholder.
                    if (missing == "future_unavailable_identity")
                    {
                        Assert.That(body, Is.Null);
                        Assert.That(BattleArtCatalog.LoadPlayerPortrait(new BattleActorState
                            { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = missing }, null), Is.Null);
                    }
                    else if (body != null) Assert.That(body.name, Does.Contain(missing));
                }
                selected = First;
                world.RefreshAppearances();
                yield return null;
                Assert.That(animator.CharacterId, Is.EqualTo(First));
                Assert.That(animator.enabled, Is.True);
                Assert.That(renderer.sprite, Is.Not.Null);
                Assert.That(actor.Go.GetComponent<MeshRenderer>().enabled, Is.False);
                Assert.That(actor.Go.GetComponentsInChildren<QdaoBoySpriteAnimator>().Length, Is.EqualTo(1));
                Assert.That(actor.Go.transform.Find("shadow").GetComponent<SpriteRenderer>().enabled, Is.True);
            }
            finally { view.Destroy(); world.Clear(); UnityEngine.Object.Destroy(world.Root.gameObject); UnityEngine.Object.Destroy(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreationSelectorAndAccountCards_KeepAppearanceIndependentOfProfessionAndGender()
        {
            Assert.That(QdaoCharacterCatalog.Find(First).ResolveAppearance(), Is.Not.Null);
            Assert.That(QdaoCharacterCatalog.Find(Second).ResolveAppearance(), Is.Not.Null);
            var host = new GameObject("RoleIdentityUiAcceptance");
            var ui = host.AddComponent<RoleFlowUi>();
            try
            {
                typeof(RoleFlowUi).GetMethod("BuildCanvas", Hidden)?.Invoke(ui, null);
                var choice = new GameClient.PlayerChoice();
                var choose = Choose(ui, Array.Empty<AccountSimplePlayer>(), choice);
                Assert.That(choose.MoveNext(), Is.True);
                yield return null;
                Click(host, "NextAppearance");
                Click(host, "Class_4");
                Click(host, "GenderFemale");
                yield return null;
                Assert.That(FindImage(host, "CharacterArtwork").sprite, Is.SameAs(QdaoCharacterCatalog.LoadPortrait(First)));
                Capture(host.GetComponentInChildren<Canvas>(), "create-00-disconnected-ui");
                Click(host, "ConfirmCreate");
                Assert.That(choose.MoveNext(), Is.False);
                Assert.That(choice.AppearanceId, Is.EqualTo(First));
                Assert.That(choice.ClassId, Is.EqualTo(4u));
                Assert.That(choice.Gender, Is.EqualTo(2u));
                Assert.That(choice.CreateNew, Is.True);

                var roles = new[]
                {
                    new AccountSimplePlayer { PlayerId = 101, ClassId = 4, Gender = 2, AppearanceId = First },
                    new AccountSimplePlayer { PlayerId = 102, ClassId = 4, Gender = 2, AppearanceId = Second },
                };
                choice = new GameClient.PlayerChoice();
                choose = Choose(ui, roles, choice);
                Assert.That(choose.MoveNext(), Is.True);
                yield return null;
                Click(host, "Role_102");
                yield return null;
                Assert.That(FindImage(host, "CharacterArtwork").sprite, Is.SameAs(QdaoCharacterCatalog.LoadPortrait(Second)));
                Capture(host.GetComponentInChildren<Canvas>(), "select-01-disconnected-ui");
                Click(host, "EnterSanctuary");
                Assert.That(choose.MoveNext(), Is.False);
                Assert.That(choice.SelectedPlayerId, Is.EqualTo(102UL));
                Assert.That(choice.CreateNew, Is.False);
            }
            finally { UnityEngine.Object.Destroy(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BattleLunge_RendersPublishedWalkGeometry_AndReleasesMovementOnInterruptionAndDestroy()
        {
            var appearance = QdaoCharacterCatalog.Find(First).ResolveAppearance();
            Assert.That(appearance, Is.Not.Null, "Requires a real accepted same-ID V13/V14 package.");
            var sourceFrames = new Dictionary<Texture2D, QdaoMixedResolutionContract.Geometry>();
            foreach (string direction in new[] { "E", "W" })
                for (int frame = 0; frame < appearance.FrameCount; frame++)
                {
                    string path = appearance.FrameResourcePath(direction, frame);
                    var texture = Resources.Load<Texture2D>(path);
                    Assert.That(texture, Is.Not.Null, path);
                    sourceFrames.Add(texture, appearance.GeometryForResource(path));
                }
            int baseline = QdaoHdResources.ResidentDirectionCount;
            var host = new GameObject("IdentityBattleWalkAcceptance", typeof(RectTransform), typeof(Canvas));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var view = new BattleUnitView(null, host.transform, 101, true, true, 0, null);
            try
            {
                view.SetPlacement(new Vector2(1450, 650), 1f);
                view.Apply(new BattleActorState
                    { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = First, Level = 1 });
                var idle = FindImage(host, "Body").sprite;
                view.PlayAttackLunge(new Vector2(700, 470));
                bool captured = false;
                var seen = new HashSet<Texture2D>();
                float deadline = Time.realtimeSinceStartup + 1.2f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    var sprite = FindImage(host, "Body").sprite;
                    if (sprite == null || !sourceFrames.TryGetValue(sprite.texture, out var geometry)) continue;
                    Assert.That(sprite.texture.width, Is.EqualTo(geometry.Width));
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(geometry.PixelsPerUnit));
                    seen.Add(sprite.texture);
                    if (!captured)
                    {
                        Capture(canvas, $"battle-00-v{appearance.Version}-{geometry.Width}-walk-disconnected-presenter");
                        captured = true;
                    }
                }
                Assert.That(captured, Is.True);
                Assert.That(seen.Count, Is.GreaterThanOrEqualTo(2), "Movement must advance beyond a static idle/first walk pose.");
                Assert.That(FindImage(host, "Body").sprite, Is.SameAs(idle));
                view.PlayAttackLunge(new Vector2(700, 470));
                yield return null;
                view.ResetVisual();
                Assert.That(typeof(BattleUnitView).GetField("_movementStrip", Hidden)?.GetValue(view), Is.Null);
                Assert.That(FindImage(host, "Body").sprite, Is.SameAs(idle));
                view.PlayAttackLunge(new Vector2(700, 470));
                yield return null;
            }
            finally { view.Destroy(); UnityEngine.Object.Destroy(host); }
            yield return null;
            yield return null;
            Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline));
        }

        [UnityTest]
        public IEnumerator SyntheticHdLunge_IdentitySwapAndDestroyKeepAfterimageLeaseUntilPoolRelease()
        {
            // Existing lifetime fixture supplies only synthetic textures: no captures, approvals or art evidence.
            using var resources = new QdaoHdResourceLifetimePlayModeTests.ResourcesFixture();
            var hd = QdaoHdResourceLifetimePlayModeTests.Hd(First);
            int baseline = QdaoHdResources.ResidentDirectionCount;
            var host = new GameObject("SyntheticIdentityWalkLeaseTest", typeof(RectTransform), typeof(Canvas));
            var view = new BattleUnitView(null, host.transform, 101, true, true, 0, null);
            var ghosts = new BattleAfterimagePool(host.GetComponent<RectTransform>());
            view.Ghosts = ghosts;
            try
            {
                view.SetPlacement(new Vector2(1450, 650), 1f);
                view.Apply(new BattleActorState
                    { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = First });
                using var idle = resources.Battle(hd, false, false);
                typeof(BattleUnitView).GetMethod("ApplyBodySprite", Hidden)?.Invoke(view, new object[] { idle.Frames[0], false });
                idle.Dispose();
                view.PlayAttackLunge(new Vector2(700, 470));
                typeof(BattleUnitView).GetMethod("StopMovementWalk", Hidden)?.Invoke(view, null);
                typeof(BattleUnitView).GetField("_movementStrip", Hidden)?.SetValue(view, resources.Battle(hd, true, false));
                yield return null;
                var moving = FindImage(host, "Body").sprite;
                Assert.That(moving.texture.name, Does.StartWith(QdaoCharacterCatalog.OriginalV14Root));
                ghosts.Spawn(moving, Vector2.zero, new Vector2(230, 230), false, 0, 30f);
                var ghost = host.GetComponentsInChildren<Image>().First(image => image.name == "Afterimage" && image.sprite == moving);
                view.Apply(new BattleActorState
                    { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = Second });
                Assert.That(view.CharacterId, Is.EqualTo(Second));
                Assert.That(typeof(BattleUnitView).GetField("_movementStrip", Hidden)?.GetValue(view), Is.Null);
                Assert.That(ghost.sprite, Is.SameAs(moving));
                Assert.That(resources.Live, Is.EqualTo(17));
                view.Destroy();
                yield return null;
                Assert.That(ghost.sprite.texture, Is.Not.Null);
                Assert.That(resources.Live, Is.EqualTo(17));
                ghosts.Clear();
                Assert.That(resources.Live, Is.Zero);
                Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline));
            }
            finally { ghosts.Dispose(); view.Destroy(); UnityEngine.Object.Destroy(host); }
            yield return null;
        }

        private static IEnumerator Choose(RoleFlowUi ui, IReadOnlyList<AccountSimplePlayer> roles, GameClient.PlayerChoice choice)
            => (IEnumerator)typeof(RoleFlowUi).GetMethod("Choose", Hidden)?.Invoke(ui, new object[] { 1u, roles, choice });

        private static Image FindImage(GameObject host, string name)
            => host.GetComponentsInChildren<Image>(true).First(image => image.name == name);

        private static void Click(GameObject host, string name)
        {
            var button = host.GetComponentsInChildren<Button>().First(candidate => candidate.name == name);
            Assert.That(button.IsInteractable(), Is.True);
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }

        private static void Capture(Canvas canvas, string label)
        {
            string folder = Environment.GetEnvironmentVariable("QDAO_IDENTITY_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Application.dataPath, "../Temp/QdaoIdentityCaptures");
            Directory.CreateDirectory(folder);
            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            var previousActive = RenderTexture.active;
            var cameraObject = new GameObject("RoleIdentityCaptureCamera");
            var target = new RenderTexture(1920, 810, 24);
            var image = new Texture2D(1920, 810, TextureFormat.RGB24, false);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .10f, .12f);
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                string path = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + label + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("[QdaoRoleIdentityPlayModeTests] disconnected UI/presenter capture: " + Path.GetFullPath(path));
            }
            finally
            {
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }
    }
}
