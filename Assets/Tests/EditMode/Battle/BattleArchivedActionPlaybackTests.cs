using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class BattleArchivedActionPlaybackTests
    {
        private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] RetainedCharacters =
        {
            "00_reference_topright_boy", "01_ice_sword_girl", "02_fire_talisman_boy",
            "03_lotus_healer_girl", "04_mountain_guardian_boy", "05_celestial_musician_girl",
            "06_thunder_caster_boy", "07_moon_shadow_assassin_girl", "08_alchemy_prodigy_boy",
            "09_bamboo_archer_girl", "10_crimson_spear_girl", "14_short_hair_snow_summoner_girl",
            "15_water_dragon_scholar_boy", "17_ghost_script_calligrapher_boy", "20_star_formation_master_girl"
        };
        private static readonly MethodInfo Tick = typeof(RealtimeTween).GetMethod("UpdateAll", BindingFlags.Static | BindingFlags.NonPublic);
        private GameObject _layer;
        private BattleUnitView _view;
        private Texture2D _texture;
        private Sprite _idle;
        private Sprite _action;

        [SetUp]
        public void SetUp()
        {
            BattleTempo.Speed = 1f;
            _layer = new GameObject("ArchivedBattleTest", typeof(RectTransform), typeof(Canvas));
            _view = new BattleUnitView(null, _layer.transform, 17, true, true, 0, null);
            _texture = new Texture2D(16, 16);
            _idle = Sprite.Create(_texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .125f), 8f);
            _action = Sprite.Create(_texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .25f), 16f);
            Invoke("ApplyBodySprite", _idle, false);
            _view.SetPlacement(new Vector2(400f, 250f), 1f);
        }

        [TearDown]
        public void TearDown()
        {
            _view.StopAction();
            // EditMode uses immediate teardown; BattleUnitView.Destroy targets the playing scene.
            RealtimeTween.Kill(typeof(BattleUnitView).GetField("_idleToken", HiddenInstance).GetValue(_view));
            Object.DestroyImmediate(_layer);
            Object.DestroyImmediate(_idle);
            Object.DestroyImmediate(_action);
            Object.DestroyImmediate(_texture);
            Advance(.01f);
            BattleTempo.Speed = 1f;
        }

        [Test]
        public void ActionFramesKeepWorldScaleAndFeetThenRestoreIdleFacingAndGeometry()
        {
            var strip = new StripAnim { Frames = new[] { _action, _action }, Fps = 10f, Mirrored = true, UseWorldGeometry = true };
            Invoke("BeginAction");
            Invoke("PlayStrip", strip, .2f, null, (Action)(() => _view.PlayIdle()));
            var body = Body;
            Assert.That(body.sprite, Is.SameAs(_action));
            Assert.That(body.rectTransform.sizeDelta.y, Is.EqualTo(BattleUnitView.PlayerHeight).Within(.001f));
            Assert.That(body.rectTransform.anchoredPosition.y + .25f * body.rectTransform.sizeDelta.y,
                Is.EqualTo(-BattleUnitView.GroundY).Within(.001f));
            Assert.That(body.rectTransform.localScale.x, Is.EqualTo(-1f));
            Advance(.22f);
            Assert.That(body.sprite, Is.SameAs(_idle));
            Assert.That(body.rectTransform.localScale.x, Is.EqualTo(1f));
            Assert.That(body.rectTransform.anchoredPosition.y, Is.EqualTo(-BattleUnitView.GroundY - .125f * BattleUnitView.PlayerHeight).Within(.001f));
        }

        [Test]
        public void InterruptedActionNeverFiresItsRemainingFramesOrCompletion()
        {
            var strip = new StripAnim { Frames = new[] { _action, _action, _action }, Fps = 10f };
            int frames = 0, completed = 0;
            Invoke("BeginAction");
            Invoke("PlayStrip", strip, .3f, (Action<int>)(_ => frames++), (Action)(() => completed++));
            Assert.That(frames, Is.EqualTo(1), "The contact frame is visible immediately.");
            _view.StopAction();
            Advance(.5f);
            Assert.That(frames, Is.EqualTo(1));
            Assert.That(completed, Is.Zero);
            Assert.That(Body.sprite, Is.SameAs(_idle));
            Assert.That(_view.Root.anchoredPosition, Is.EqualTo(new Vector2(400f, -250f)));
        }

        [Test]
        public void BattleTempoScalesTheWholeAuthoredClipWithoutChangingItsFrameOrder()
        {
            BattleTempo.Speed = 2f;
            var strip = new StripAnim { Frames = new[] { _idle, _action }, Fps = 5f };
            bool complete = false;
            Invoke("BeginAction");
            Invoke("PlayStrip", strip, .4f, null, (Action)(() => complete = true));
            Advance(.11f);
            Assert.That(Body.sprite, Is.SameAs(_action));
            Assert.That(complete, Is.False);
            Advance(.1f);
            Assert.That(complete, Is.True);
        }

        [Test]
        public void ItemBeatBudgetIncludesItsCastAndAttackReturnWaitsForTheSwing()
        {
            var root = _layer.GetComponent<RectTransform>();
            var presenter = new BattlePresenter(null, root, root, root, root, _ => _view,
                () => new[] { _view }, _ => "test");
            try
            {
                var plan = TurnPlan.Build(new[] { new TurnEventInput { Type = BattleEventCodes.Item, SourceId = 17 } });
                presenter.PreparePlan(plan);
                Assert.That(plan.Beats[0].DurationSeconds, Is.GreaterThanOrEqualTo(BattleUnitView.CastActionSeconds));
                Assert.That(BattleUnitView.AttackReturnStart(.36f), Is.GreaterThanOrEqualTo(BattleUnitView.AttackApproachSeconds + .36f));
            }
            finally { presenter.Dispose(); }
        }

        [Test]
        public void BeatBudgetIncludesTheLastRepeatedTargetReaction()
        {
            var root = _layer.GetComponent<RectTransform>();
            var presenter = new BattlePresenter(null, root, root, root, root, _ => _view,
                () => new[] { _view }, _ => "test");
            try
            {
                var plan = new TurnPlan();
                var beat = new Beat { ActorId = 17, Kind = BeatKind.Item, DurationSeconds = TurnPlan.ItemSeconds };
                for (int i = 0; i < 8; i++) beat.Targets.Add(new BeatTarget { ActorId = 18, Effect = TargetEffect.Damage });
                plan.Beats.Add(beat);
                presenter.PreparePlan(plan);
                Assert.That(beat.DurationSeconds, Is.GreaterThanOrEqualTo(
                    BattleUnitView.CastReleaseDelaySeconds + 7 * BattlePresenter.MultiHitStagger + _view.ActionDurationSeconds("hit")));
            }
            finally { presenter.Dispose(); }
        }

        [TestCase("attack")]
        [TestCase("cast")]
        public void AuthoredImpactFiresExactlyOnceAtItsManifestFrame(string action)
        {
            const string character = "07_moon_shadow_assassin_girl";
            typeof(BattleUnitView).GetProperty("CharacterId").SetValue(_view, character);
            _view.Fx = new BattleFxPlayer(null);
            _view.Ghosts = new BattleAfterimagePool(null);
            float eventTime = QdaoActionResources.GetEventTimeSeconds(character, action, "W");
            Assert.That(eventTime, Is.GreaterThan(0f));
            int hits = 0;
            if (action == "attack")
            {
                eventTime += BattleUnitView.AttackApproachSeconds;
                _view.PlayAttackLunge(new Vector2(700f, 250f), () => hits++);
            }
            else _view.PlayCast(() => hits++);
            Advance(eventTime - .01f);
            Assert.That(hits, Is.Zero);
            Advance(.03f);
            Assert.That(hits, Is.EqualTo(1));
            Advance(_view.ActionDurationSeconds(action) + .1f);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(Body.sprite, Is.SameAs(_idle));
        }

        [Test]
        public void CancellingApproachReleasesThePendingAttackWithoutDeliveringItsImpact()
        {
            typeof(BattleUnitView).GetProperty("CharacterId").SetValue(_view, "00_reference_topright_boy");
            _view.Fx = new BattleFxPlayer(null);
            _view.Ghosts = new BattleAfterimagePool(null);
            int baseline = QdaoActionResources.ResidentClipCount;
            int hits = 0;
            _view.PlayAttackLunge(new Vector2(700f, 250f), () => hits++);
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline + 1));
            Advance(.1f);
            _view.StopAction();
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
            Advance(1f);
            Assert.That(hits, Is.Zero);
        }

        [TestCase(1f)]
        [TestCase(2f)]
        [TestCase(6f)]
        public void AcceleratedAttackFinishesItsReturnWithinItsBudgetAtThirtyFps(float speed)
        {
            BattleTempo.Speed = speed;
            typeof(BattleUnitView).GetProperty("CharacterId").SetValue(_view, "00_reference_topright_boy");
            _view.Fx = new BattleFxPlayer(null);
            _view.Ghosts = new BattleAfterimagePool(null);
            int hits = 0;
            _view.PlayAttackLunge(new Vector2(700f, 250f), () => hits++);
            Advance(_view.ActionDurationSeconds("attack") / speed + .001f, 1f / 30f);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(_view.Root.anchoredPosition, Is.EqualTo(new Vector2(400f, -250f)));
            Assert.That(Body.sprite, Is.SameAs(_idle));
        }

        [Test]
        public void AnImpactCallbackCanStartANewActionWithoutTheOldReturnCancellingIt()
        {
            typeof(BattleUnitView).GetProperty("CharacterId").SetValue(_view, "07_moon_shadow_assassin_girl");
            _view.Fx = new BattleFxPlayer(null);
            _view.Ghosts = new BattleAfterimagePool(null);
            _view.PlayAttackLunge(new Vector2(700f, 250f), () => _view.PlayCast());
            Advance(.4f);
            Assert.That(Body.sprite.name, Does.Contain("_archived_cast_"));
            Advance(.35f);
            Assert.That(Body.sprite.name, Does.Contain("_archived_cast_"));
            Advance(.5f);
            Assert.That(Body.sprite, Is.SameAs(_idle));
        }

        [Test]
        public void EndingAClipWithoutAnIdleClearsBothImageReferences()
        {
            typeof(BattleUnitView).GetField("_idleSprite", HiddenInstance).SetValue(_view, null);
            var strip = new StripAnim { Frames = new[] { _action }, Fps = 10f };
            Invoke("BeginAction");
            Invoke("PlayStrip", strip, .1f, null, null);
            Advance(.11f);
            Assert.That(Body.sprite, Is.Null);
            Assert.That(Body.transform.Find("Flash").GetComponent<Image>().sprite, Is.Null);
        }

        [Test]
        public void MissingAuthoredEventUsesTheExistingReleaseDelayInsteadOfFrameZero()
        {
            var strip = new StripAnim { Frames = new Sprite[16], Fps = 16f / .72f,
                UseWorldGeometry = true, EventFrame = -1 };
            int frame = (int)typeof(BattleUnitView).GetMethod("EventFrameFor", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { strip, 3, BattleUnitView.CastReleaseDelaySeconds });
            Assert.That(frame, Is.EqualTo(10));
            Assert.That(frame / strip.Fps, Is.EqualTo(BattleUnitView.CastReleaseDelaySeconds).Within(.001f));
        }

        [TestCase("00_reference_topright_boy")]
        [TestCase("01_ice_sword_girl")]
        public void CharactersWithoutAnEventMarkerDoNotReleaseAtTheFirstCastFrame(string character)
        {
            typeof(BattleUnitView).GetProperty("CharacterId").SetValue(_view, character);
            _view.Fx = new BattleFxPlayer(null);
            int releases = 0;
            _view.PlayCast(() => releases++);
            Advance(.05f);
            Assert.That(releases, Is.Zero);
            Advance(_view.ActionDurationSeconds("cast") + .1f);
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(Body.sprite, Is.SameAs(_idle));
        }

        [Test]
        public void AfterimageRetainsArchivedFramesAfterTheActorClipIsReleased()
        {
            var ghosts = new BattleAfterimagePool(_layer.GetComponent<RectTransform>());
            using var clip = BattleArtCatalog.LoadPlayerWalk("00_reference_topright_boy", true);
            Assert.That(clip, Is.Not.Null);
            var sprite = clip.Frames[0];
            ghosts.Spawn(sprite, Vector2.zero, new Vector2(230f, 230f), false, 0, 5f);
            clip.Dispose();
            Assert.That(sprite != null && sprite.texture != null, Is.True);
            ghosts.Clear();
            Assert.That(sprite == null, Is.True, "The last afterimage releases the archived runtime sprite.");
        }

        [Test]
        public void RetainedRosterRequiresAllFifteenDeliveredCombatIdentities()
        {
            Assert.That(QdaoCharacterCatalog.RetainedOriginalAll.Select(entry => entry.Id),
                Is.EquivalentTo(RetainedCharacters));
        }

        [TestCaseSource(nameof(RetainedCharacters))]
        public void DeliveredActionsUseOwnEAndWFramesAndAuthoredCadence(string character)
        {
            int baseline = QdaoActionResources.ResidentClipCount;
            foreach (bool east in new[] { true, false })
            foreach (var action in new[] { "attack", "hit", "cast" })
            {
                string direction = east ? "E" : "W";
                string label = character + "/" + action + "/" + direction;
                int count = action == "attack" ? 12 : action == "hit" ? 6 : 16;
                float frameSeconds = action == "attack" ? .03f : action == "hit" ? .04f : .045f;
                using var strip = BattleArtCatalog.LoadCharacterAction(character, action, east);
                using var clip = QdaoActionResources.Acquire(character, action, direction);
                using var opposite = QdaoActionResources.Acquire(character, action, east ? "W" : "E");
                Assert.That(strip, Is.Not.Null, label);
                Assert.That(clip?.IsValid, Is.True, label);
                Assert.That(opposite?.IsValid, Is.True, label + " opposite direction");
                Assert.That(strip.Count, Is.EqualTo(count), label);
                Assert.That(strip.Mirrored, Is.False, label);
                Assert.That(strip.UseWorldGeometry, Is.True, label + " must use authored frames, not a legacy strip");
                Assert.That(strip.DurationSeconds, Is.EqualTo(count * frameSeconds).Within(.0001f), label);
                Assert.That(strip.DurationSeconds, Is.EqualTo(QdaoActionResources.GetDurationSeconds(character, action, direction)).Within(.0001f), label);
                Assert.That(clip.FrameDurationsSeconds.Length, Is.EqualTo(count), label);
                Assert.That(strip.EventFrame, Is.InRange(-1, count - 1), label);
                Assert.That(strip.EventFrame, Is.EqualTo(clip.EventFrame), label);
                float expectedEvent = strip.EventFrame < 0 ? -1f : strip.EventFrame * frameSeconds;
                Assert.That(clip.EventTimeSeconds, Is.EqualTo(expectedEvent).Within(.0001f), label);
                Assert.That(QdaoActionResources.GetEventTimeSeconds(character, action, direction),
                    Is.EqualTo(expectedEvent).Within(.0001f), label);
                var uniqueTextures = new HashSet<Texture2D>();
                for (int i = 0; i < count; i++)
                {
                    Assert.That(strip.Frames[i] != null && strip.Frames[i].texture != null, Is.True, label + "/" + i);
                    Assert.That(uniqueTextures.Add(strip.Frames[i].texture), Is.True, label + " repeats a texture");
                    Assert.That(opposite.Frames.Any(frame => frame.texture == strip.Frames[i].texture),
                        Is.False, label + " borrows the other direction");
                    Assert.That(strip.Frames[i].name, Is.EqualTo(character + "_archived_" + action + "_" + direction + "_" + (i + 1).ToString("00")));
                    Assert.That(clip.FrameDurationsSeconds[i], Is.EqualTo(frameSeconds).Within(.000001f), label + "/" + i);
                    Assert.That(strip.FrameAt((i + .5f) / count), Is.SameAs(strip.Frames[i]), label);
                    Assert.That(clip.FrameAt((i + .5f) * frameSeconds, false), Is.SameAs(strip.Frames[i]), label);
                }
            }
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
        }

        private Image Body => _view.Root.Find("Body").GetComponent<Image>();
        private object Invoke(string method, params object[] args)
            => typeof(BattleUnitView).GetMethod(method, HiddenInstance).Invoke(_view, args);
        private static void Advance(float seconds, float frameSeconds = .005f)
        {
            while (seconds > 0f)
            {
                var step = Mathf.Min(seconds, frameSeconds);
                Tick.Invoke(null, new object[] { step, step });
                seconds -= step;
            }
        }
    }
}
