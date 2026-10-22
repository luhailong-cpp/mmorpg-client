using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>Runs the production presenter and pools; no network or alternate damage implementation.</summary>
    public sealed class BattleCombatFxIntegrationTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo Tick = typeof(RealtimeTween).GetMethod("UpdateAll", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly Dictionary<ulong, BattleUnitView> _views = new Dictionary<ulong, BattleUnitView>();
        private readonly List<int> _damageTicks = new List<int>();
        private GameObject _host;
        private BattlePresenter _presenter;
        private int _tick;

        [SetUp]
        public void SetUp()
        {
            BattleTempo.Speed = 1f;
            _host = new GameObject("CombatFxIntegration", typeof(RectTransform), typeof(Canvas));
            var stage = Layer("Stage");
            var fx = Layer("Fx");
            var plates = Layer("Plates");
            var numbers = Layer("Numbers");
            var overlay = Layer("Overlay");
            _presenter = new BattlePresenter(null, stage, fx, numbers, overlay,
                id => _views.TryGetValue(id, out var view) ? view : null, () => _views.Values, id => id.ToString());
            for (ulong id = 1; id <= 4; id++)
            {
                var view = new BattleUnitView(null, stage, id, id == 1, id == 1, (int)id - 1, null, plates);
                view.Apply(new BattleActorState { ActorId = id, TeamIndex = id == 1 ? 0u : 1u,
                    Attributes = new BaseAttributesComp { Health = 1000, Mana = 1000 }, MaxHealth = 1000, MaxMana = 1000,
                    ActorType = eBattleActorType.BattleActorTypePlayer, Name = "FX " + id, Level = 12 });
                view.SetPlacement(new Vector2(id == 1 ? 1600 : 600 + 270 * (id - 2), id == 1 ? 750 : 550), 1f);
                view.Fx = _presenter.Fx;
                view.Numbers = _presenter.Numbers;
                view.Ghosts = _presenter.Ghosts;
                _views.Add(id, view);
            }
            _presenter.OnLog += line => { if (line.Contains("受到") && line.Contains("伤害")) _damageTicks.Add(_tick); };
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Abort();
            // These tests own the complete hierarchy; avoid deferred Object.Destroy in EditMode.
            foreach (var view in _views.Values)
            {
                view.StopAction();
                RealtimeTween.Kill(typeof(BattleUnitView).GetField("_idleToken", Hidden).GetValue(view));
            }
            Object.DestroyImmediate(_host);
            _views.Clear();
            _damageTicks.Clear();
            Advance(.01f);
            BattleTempo.Speed = 1f;
        }

        [Test]
        public void DedicatedMappingsAndImportedStripsMatchTheAcceptedContracts()
        {
            var single = BattleArtCatalog.ResolveSkillFx(1201, false);
            Assert.That(single.FxId, Is.EqualTo("xuanfu_luoyao_impact"));
            Assert.That(single.ProjectileFxId, Is.EqualTo("xuanfu_luoyao_projectile"));
            Assert.That(single.FlightSeconds, Is.EqualTo(.5f));
            Assert.That(single.HitFrame, Is.EqualTo(3));
            Assert.That(single.TargetAtFeet, Is.False);
            var area = BattleArtCatalog.ResolveSkillFx(1202, false);
            Assert.That(area.FxId, Is.EqualTo("zixiao_leizhen_strike"));
            Assert.That(area.TelegraphFxId, Is.EqualTo("zixiao_leizhen_telegraph"));
            Assert.That(area.TelegraphSeconds, Is.EqualTo(.5f));
            Assert.That(area.HitFrame, Is.EqualTo(3));
            Assert.That(area.TargetAtFeet, Is.True);
            foreach (string id in new[] { "hit_star", "cast_charge", "xuanfu_luoyao_projectile", "xuanfu_luoyao_impact", "zixiao_leizhen_telegraph", "zixiao_leizhen_strike" })
            {
                var strip = BattleArtCatalog.LoadFx(id);
                Assert.That(strip?.Count, Is.EqualTo(8), id);
                Assert.That(strip.Fps, Is.EqualTo(id == "cast_charge" ? 10f : 16f), id);
                Assert.That(strip.Frames[0].rect.size, Is.EqualTo(new Vector2(384f, 384f)), id);
                Assert.That(strip.Pivot, Is.EqualTo(id == "cast_charge" || id == "zixiao_leizhen_strike" ? new Vector2(.5f, .1f) : new Vector2(.5f, .5f)), id);
                var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Battle/Fx/" + id + "_strip.png");
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), id);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), id);
                Assert.That(importer.mipmapEnabled, Is.False, id);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), id);
                Assert.That(importer.maxTextureSize, Is.EqualTo(4096), id);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProjectileMovesAndLoopsThenArrivesOnlyOnce(bool mirror)
        {
            int arrived = 0;
            var from = new Vector2(300f, 400f);
            var to = new Vector2(1100f, 500f);
            _presenter.Fx.PlayTravel("xuanfu_luoyao_projectile", from, to, 200f, .8f, () => arrived++, mirror);
            Advance(.3f);
            var image = ActiveFx.Single();
            Assert.That(Vector2.Distance(VisualAnchor(image, new Vector2(.5f, .5f)), Vector2.Lerp(from, to, .375f)), Is.LessThan(.05f));
            Assert.That(image.rectTransform.localScale.x, Is.EqualTo(mirror ? -1f : 1f));
            Assert.That(arrived, Is.Zero);
            Advance(.3f);
            Assert.That(arrived, Is.Zero);
            Assert.That(image.sprite, Is.SameAs(BattleArtCatalog.LoadFx("xuanfu_luoyao_projectile").Frames[1]), "A long flight must cycle the strip instead of freezing its last frame.");
            Advance(.21f);
            Assert.That(arrived, Is.EqualTo(1));
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(_presenter.Fx.PooledCount, Is.EqualTo(1));
            Advance(1f);
            Assert.That(arrived, Is.EqualTo(1));
        }

        [Test]
        public void ClearingAFlightCancelsItsArrivalAndRecyclesTheImage()
        {
            int arrived = 0;
            _presenter.Fx.PlayTravel("xuanfu_luoyao_projectile", Vector2.zero, Vector2.one * 500f, 200f, .5f, () => arrived++);
            Advance(.2f);
            _presenter.Fx.Clear();
            Advance(1f);
            Assert.That(arrived, Is.Zero);
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(_presenter.Fx.PooledCount, Is.EqualTo(1));
        }

        [Test]
        public void XuanfuFlightCannotApplyDamageOrNumbersBeforeItsImpactFrame()
        {
            var plan = Plan(1201, 1);
            float release = _views[1].ActionEventDelaySeconds("cast");
            _presenter.Play(plan);
            Assert.That(ActiveFx.Any(image => image.name.StartsWith("Fx_cast_charge_")), Is.True);
            Advance(release + .3f);
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(1000));
            Assert.That(_damageTicks, Is.Empty);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
            Assert.That(ActiveFx.Any(image => image.name.Contains("xuanfu_luoyao_projectile")), Is.True);
            Advance(.42f);
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(900));
            Assert.That(_damageTicks.Count, Is.EqualTo(1));
            Assert.That(_presenter.Numbers.ActiveCount, Is.EqualTo(1));
            Advance(plan.TotalSeconds + 1.5f);
            Assert.That(_damageTicks.Count, Is.EqualTo(1));
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(3)]
        public void LeizhenTargetsHitOnOneClockAtFeetAndEachReceiveOneResult(int targetCount)
        {
            var plan = Plan(1202, targetCount);
            float release = _views[1].ActionEventDelaySeconds("cast");
            _presenter.Play(plan);
            Advance(release + .55f);
            Assert.That(_damageTicks, Is.Empty);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
            var strikes = ActiveFx.Where(image => image.name.Contains("zixiao_leizhen_strike")).ToArray();
            Assert.That(strikes.Length, Is.EqualTo(targetCount));
            Assert.That(strikes.Select(image => image.sprite).Distinct().Count(), Is.EqualTo(1), "All target strips use the same frame clock.");
            foreach (var view in _views.Values.Where(view => view.ActorId > 1 && view.ActorId <= (ulong)targetCount + 1))
                Assert.That(strikes.Any(image => Vector2.Distance(VisualAnchor(image, new Vector2(.5f, .1f)), view.FootPosition) < .02f), Is.True, "Strike must be registered to the feet.");
            Advance(.18f);
            Assert.That(_damageTicks.Count, Is.EqualTo(targetCount));
            Assert.That(_damageTicks.Distinct().Count(), Is.EqualTo(1));
            Assert.That(_presenter.Numbers.ActiveCount, Is.EqualTo(targetCount));
            for (ulong id = 2; id <= (ulong)targetCount + 1; id++) Assert.That(_views[id].CurrentHealth, Is.EqualTo(900));
            Advance(plan.TotalSeconds + 1.5f);
            Assert.That(_damageTicks.Count, Is.EqualTo(targetCount));
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
        }

        [TestCase(1201u)]
        [TestCase(1202u)]
        public void BudgetIncludesTheLeadInAndTheCompleteImpactStrip(uint skill)
        {
            var plan = Plan(skill, 1);
            _presenter.PreparePlan(plan);
            var presentation = BattleArtCatalog.ResolveSkillFx(skill, false);
            float lead = presentation.FlightSeconds + presentation.TelegraphSeconds;
            float release = _views[1].ActionEventDelaySeconds("cast");
            var fx = BattleArtCatalog.LoadFx(presentation.FxId);
            Assert.That(plan.TotalSeconds, Is.GreaterThanOrEqualTo(release + lead + fx.DurationSeconds));
            Assert.That(plan.TotalSeconds, Is.GreaterThanOrEqualTo(release + lead + 3f / 16f + _views[2].ActionDurationSeconds("hit")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AbortAndSkipDoNotDeliverLateImpactCallbacks(bool skip)
        {
            var plan = Plan(1201, 1);
            _presenter.Play(plan);
            Advance(_views[1].ActionEventDelaySeconds("cast") + .2f);
            Assert.That(_damageTicks, Is.Empty);
            if (skip) _presenter.Skip(); else _presenter.Abort();
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(skip ? 900 : 1000));
            Advance(3f);
            Assert.That(_damageTicks, Is.Empty, "Skip lands the terminal HP without spawning a second hit or number.");
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
        }

        private RectTransform Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_host.transform, false);
            return go.GetComponent<RectTransform>();
        }

        private static TurnPlan Plan(uint skill, int targets)
        {
            var plan = new TurnPlan();
            var beat = new Beat { ActorId = 1, SkillId = skill, Kind = BeatKind.Cast, DurationSeconds = TurnPlan.CastSeconds };
            for (ulong id = 2; id <= (ulong)targets + 1; id++)
                beat.Targets.Add(new BeatTarget { ActorId = id, Effect = TargetEffect.Damage, Value = 100, HasHealthAfter = true, HealthAfter = 900 });
            plan.Beats.Add(beat);
            return plan;
        }

        private List<Image> ActiveFx => (List<Image>)typeof(BattleFxPlayer).GetField("_active", Hidden).GetValue(_presenter.Fx);

        private static Vector2 VisualAnchor(Image image, Vector2 pivot)
        {
            var rt = image.rectTransform;
            float left = rt.anchoredPosition.x - (rt.localScale.x < 0f ? rt.sizeDelta.x : 0f);
            return new Vector2(left + rt.sizeDelta.x * pivot.x, -rt.anchoredPosition.y + rt.sizeDelta.y * (1f - pivot.y));
        }

        private void Advance(float seconds)
        {
            while (seconds > .000001f)
            {
                float step = Mathf.Min(seconds, .002f);
                _tick++;
                Tick.Invoke(null, new object[] { step, step });
                seconds -= step;
            }
        }
    }
}
