using System;
using System.Collections.Generic;
using System.IO;
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
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Independent 1203 presentation acceptance through the production presenter and pools.
    /// The 100 damage and existing buff-icon ID are synthetic outcomes, not server combat validation.
    /// </summary>
    public sealed class BattleFrostFxIntegrationTests
    {
        private const string Projectile = "hanxing_ningpo_projectile";
        private const string Impact = "hanxing_ningpo_impact";
        private const uint FixtureBuffId = 7; // Existing generic icon; does not implement slowing or freezing.
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo Tick = typeof(RealtimeTween).GetMethod("UpdateAll", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly Dictionary<ulong, BattleUnitView> _views = new Dictionary<ulong, BattleUnitView>();
        private readonly List<int> _damageTicks = new List<int>();
        private readonly List<int> _buffTicks = new List<int>();
        private GameObject _host;
        private RectTransform _fxLayer;
        private BattlePresenter _presenter;
        private int _tick;

        [SetUp]
        public void SetUp()
        {
            BattleTempo.Reset();
            _tick = 0;
            _host = new GameObject("FrostFxIntegration", typeof(RectTransform), typeof(Canvas));
            var stage = Layer("Units");
            _fxLayer = Layer("Effects");
            var plates = Layer("Plates");
            _presenter = new BattlePresenter(null, stage, _fxLayer, Layer("Numbers"), Layer("Overlay"),
                id => _views.TryGetValue(id, out var view) ? view : null, () => _views.Values, id => id.ToString());
            for (ulong id = 1; id <= 2; id++)
            {
                var view = new BattleUnitView(null, stage, id, id == 1, id == 1, (int)id - 1, null, plates);
                view.Apply(new BattleActorState { ActorId = id, TeamIndex = id == 1 ? 0u : 1u,
                    ActorType = eBattleActorType.BattleActorTypePlayer, Name = "Frost fixture " + id, Level = 20,
                    Attributes = new BaseAttributesComp { Health = 1000, Mana = 1000 }, MaxHealth = 1000, MaxMana = 1000 });
                view.SetPlacement(new Vector2(id == 1 ? 1600f : 600f, id == 1 ? 780f : 500f), 1f);
                view.Fx = _presenter.Fx;
                view.Numbers = _presenter.Numbers;
                view.Ghosts = _presenter.Ghosts;
                _views.Add(id, view);
            }
            _presenter.OnLog += line =>
            {
                if (line.Contains("受到") && line.Contains("伤害")) _damageTicks.Add(_tick);
                if (line.Contains("获得 buff" + FixtureBuffId)) _buffTicks.Add(_tick);
            };
        }

        [TearDown]
        public void TearDown()
        {
            // These fixture-created icons need immediate destruction before the runtime reset path.
            foreach (var view in _views.Values)
            {
                var icons = (List<GameObject>)typeof(BattleUnitView).GetField("_buffIcons", Hidden).GetValue(view);
                foreach (var icon in icons) if (icon != null) Object.DestroyImmediate(icon);
                icons.Clear();
            }
            _presenter.Abort();
            foreach (var view in _views.Values)
            {
                view.StopAction();
                RealtimeTween.Kill(typeof(BattleUnitView).GetField("_idleToken", Hidden).GetValue(view));
            }
            // The fixture owns all objects; release sprite leases without deferred Destroy in EditMode.
            foreach (var image in _host.GetComponentsInChildren<Image>(true)) image.sprite = null;
            foreach (var owner in _host.GetComponentsInChildren<BattleActionSpriteLeaseOwner>(true)) owner.Clear();
            foreach (var owner in _host.GetComponentsInChildren<MmorpgClient.World.QdaoHdSpriteLeaseOwner>(true)) owner.Clear();
            Object.DestroyImmediate(_host);
            _views.Clear();
            _damageTicks.Clear();
            _buffTicks.Clear();
            Advance(.01f);
            BattleTempo.Reset();
        }

        [Test]
        public void Dedicated1203MappingOptsIntoPathAlignmentWithoutChanging1201()
        {
            var presentation = BattleArtCatalog.ResolveSkillFx(1203, false);
            Assert.That(presentation.FxId, Is.EqualTo(Impact));
            Assert.That(presentation.ProjectileFxId, Is.EqualTo(Projectile));
            Assert.That(presentation.FlightSeconds, Is.EqualTo(.5f));
            Assert.That(presentation.HitFrame, Is.EqualTo(3));
            Assert.That(presentation.TargetAtFeet, Is.False);
            Assert.That(presentation.AlignProjectileToPath, Is.True);
            Assert.That(presentation.IsCast, Is.True);
            Assert.That(BattleArtCatalog.ResolveSkillFx(1201, false).AlignProjectileToPath, Is.False,
                "The previous skill retains its accepted horizontal mirror contract.");
        }

        [TestCase(Projectile)]
        [TestCase(Impact)]
        public void FrostStripHasEightTransparentRgbaFramesAndTheAcceptedImporterContract(string id)
        {
            var strip = BattleArtCatalog.LoadFx(id);
            Assert.That(strip, Is.Not.Null);
            Assert.That(strip.Count, Is.EqualTo(8));
            Assert.That(strip.Fps, Is.EqualTo(16f));
            Assert.That(strip.Pivot, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(strip.DurationSeconds, Is.EqualTo(.5f));
            foreach (var sprite in strip.Frames) Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(384f, 384f)));
            string path = "Assets/Resources/Battle/Fx/" + id + "_strip.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.maxTextureSize, Is.EqualTo(4096));
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes[24], Is.EqualTo(8), "The source PNG uses 8-bit channels.");
            Assert.That(bytes[25], Is.EqualTo(6), "The source PNG carries explicit RGBA channels.");
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(decoded.LoadImage(bytes), Is.True);
                Assert.That(decoded.width, Is.EqualTo(3072));
                Assert.That(decoded.height, Is.EqualTo(384));
                var pixels = decoded.GetPixels32();
                Assert.That(pixels.Any(pixel => pixel.a == 0), Is.True);
                Assert.That(pixels.Any(pixel => pixel.a > 0), Is.True);
                Assert.That(pixels.Any(pixel => pixel.a > 0 && pixel.a < 255), Is.True, "Soft edges must retain partial alpha.");
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void ReadOnlyCatalogKeepsSpeedReductionForTwoRoundsRatherThanInventingFreeze()
        {
            var asset = Resources.Load<TextAsset>("Skills/catalog");
            Assert.That(asset, Is.Not.Null);
            var skill = JsonUtility.FromJson<CatalogFile>(asset.text).skills.Single(entry => entry.id == 1203);
            Assert.That(skill.name, Is.EqualTo("寒星凝魄"));
            Assert.That(skill.maxTargets, Is.EqualTo(1));
            Assert.That(skill.targetTeam, Is.EqualTo("enemy"));
            Assert.That(skill.buffKind, Is.EqualTo("speed"));
            Assert.That(skill.buffPercent, Is.EqualTo(-.2f).Within(.00001f));
            Assert.That(skill.durationRounds, Is.EqualTo(2));
            Assert.That(skill.description, Does.Contain("减速").And.Not.Contain("冻结"));
            Assert.That(skill.description, Does.Contain("下一回合排序生效"));
        }

        [TestCase(400f, 400f, 1200f, 400f)]
        [TestCase(1200f, 400f, 400f, 400f)]
        [TestCase(400f, 700f, 1200f, 300f)]
        [TestCase(1200f, 700f, 400f, 300f)]
        [TestCase(400f, 300f, 1200f, 700f)]
        [TestCase(1200f, 300f, 400f, 700f)]
        public void AlignedTravelKeepsItsCoreOnThePathAndPointsAlongTheUguiDirection(float fromX, float fromY, float toX, float toY)
        {
            var from = new Vector2(fromX, fromY);
            var to = new Vector2(toX, toY);
            int arrivals = 0;
            _presenter.Fx.PlayTravel(Projectile, from, to, 220f, .8f, () => arrivals++, alignToPath: true);
            Advance(.2f);
            var image = ActiveFx.Single();
            Assert.That(image.rectTransform.pivot, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(image.rectTransform.localScale, Is.EqualTo(Vector3.one), "Path rotation must not also mirror the right-pointing artwork.");
            AssertAnchor(image, Vector2.Lerp(from, to, .25f));
            AssertDirection(image, from, to);
            Assert.That(arrivals, Is.Zero);
            Advance(.4f);
            AssertAnchor(image, Vector2.Lerp(from, to, .75f));
            AssertDirection(image, from, to);
            Assert.That(image.sprite, Is.SameAs(BattleArtCatalog.LoadFx(Projectile).Frames[1]), "Long flights must cycle the eight frames.");
            Advance(.21f);
            Assert.That(arrivals, Is.EqualTo(1));
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(image.rectTransform.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(Quaternion.Angle(image.rectTransform.localRotation, Quaternion.identity), Is.LessThan(.001f));
            Advance(.5f);
            Assert.That(arrivals, Is.EqualTo(1));
        }

        [Test]
        public void ReturningAnAlignedImageResetsGeometryBeforeALegacyMirroredFlightReusesIt()
        {
            var from = new Vector2(350f, 650f);
            var to = new Vector2(1150f, 250f);
            _presenter.Fx.PlayTravel(Projectile, from, to, 200f, .5f, alignToPath: true);
            var image = ActiveFx.Single();
            Advance(.2f);
            _presenter.Fx.Clear();
            Assert.That(image.rectTransform.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(Quaternion.Angle(image.rectTransform.localRotation, Quaternion.identity), Is.LessThan(.001f));
            _presenter.Fx.PlayTravel("xuanfu_luoyao_projectile", to, from, 200f, .5f, mirror: true);
            Assert.That(ActiveFx.Single(), Is.SameAs(image));
            Assert.That(image.rectTransform.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(Quaternion.Angle(image.rectTransform.localRotation, Quaternion.identity), Is.LessThan(.001f));
            Assert.That(image.rectTransform.localScale.x, Is.EqualTo(-1f));
            Advance(.2f);
            AssertAnchor(image, Vector2.Lerp(to, from, .4f));
        }

        [Test]
        public void PresenterAlignsItsProjectileAndAppliesDamageAndExistingBuffOnlyAtImpactFrameThree()
        {
            var plan = Plan();
            _presenter.Play(plan);
            float untilFlight = 0f;
            while (!ActiveFx.Any(image => image.name.Contains(Projectile)) && untilFlight < 2f)
            {
                Advance(.002f);
                untilFlight += .002f;
                AssertNoResults();
            }
            var flight = ActiveFx.Single(image => image.name.Contains(Projectile));
            var from = _views[1].ChestPosition;
            var to = _views[2].ChestPosition;
            from += (to - from).normalized * 70f * Mathf.Max(.6f, _views[1].Scale);
            AssertAnchor(flight, from);
            AssertDirection(flight, from, to);
            Advance(.25f);
            AssertAnchor(flight, Vector2.Lerp(from, to, .5f));
            AssertNoResults();
            float untilImpact = 0f;
            while (!ActiveFx.Any(image => image.name.Contains(Impact)) && untilImpact < 1f)
            {
                Advance(.002f);
                untilImpact += .002f;
                AssertNoResults();
            }
            var impact = ActiveFx.Single(image => image.name.Contains(Impact));
            Assert.That(FrameIndex(impact, Impact), Is.EqualTo(0));
            AssertAnchor(impact, _views[2].ChestPosition);
            Advance(3f / 16f - .01f);
            Assert.That(FrameIndex(impact, Impact), Is.EqualTo(2));
            AssertNoResults();
            Advance(.02f);
            Assert.That(FrameIndex(impact, Impact), Is.GreaterThanOrEqualTo(3));
            AssertOneResultBatch();
            Assert.That(_presenter.Numbers.ActiveCount, Is.EqualTo(1));
            Assert.That(BuffIcons.Count, Is.EqualTo(1));
            Advance(plan.TotalSeconds + 2f);
            AssertOneResultBatch();
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
        }

        [TestCase(1.5f, 30)]
        [TestCase(1.5f, 15)]
        [TestCase(6f, 30)]
        [TestCase(6f, 15)]
        public void CoarseFramesCannotSettleHpEarlyAndWaitForTheFullImpactTail(float speed, int framesPerSecond)
        {
            _presenter.SpeedScale = speed;
            var plan = Plan();
            bool finished = false, impactAtFinish = false, prematureHealth = false;
            bool sawFlight = false, sawImpact = false;
            int peakNumbers = 0;
            float elapsed = 0f, firstDamage = -1f;
            float step = 1f / framesPerSecond;
            _presenter.OnHealthChanged += (id, health) => { if (id == 2 && health != 1000 && _damageTicks.Count == 0) prematureHealth = true; };
            _presenter.OnFinished += () => { finished = true; impactAtFinish = ActiveFx.Any(image => image.name.Contains(Impact)); };
            _presenter.Play(plan);
            while (!finished && elapsed < 5f)
            {
                Advance(step, step);
                elapsed += step;
                sawFlight |= ActiveFx.Any(image => image.name.Contains(Projectile));
                sawImpact |= ActiveFx.Any(image => image.name.Contains(Impact));
                peakNumbers = Math.Max(peakNumbers, _presenter.Numbers.ActiveCount);
                Assert.That(_damageTicks.Count, Is.InRange(0, 1));
                Assert.That(_buffTicks.Count, Is.EqualTo(_damageTicks.Count), "The appended BuffAdd shares the unique result batch.");
                if (_damageTicks.Count == 0) AssertNoResults();
                else
                {
                    if (firstDamage < 0f) firstDamage = elapsed;
                    AssertOneResultBatch();
                }
            }
            Assert.That(finished, Is.True);
            Assert.That(sawFlight && sawImpact, Is.True);
            Assert.That(prematureHealth, Is.False);
            Assert.That(firstDamage, Is.GreaterThanOrEqualTo((_views[1].ActionEventDelaySeconds("cast") + .5f + 3f / 16f) / speed - .001f));
            Assert.That(impactAtFinish, Is.False, "A timer deadline cannot acknowledge a still-playing impact tail.");
            Assert.That(peakNumbers, Is.EqualTo(1));
            AssertOneResultBatch();
            Advance(2f, step);
            AssertOneResultBatch();
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AbortOrSkipDuringFlightCancelsTheUnreachedImpactAndBuff(bool skip)
        {
            _presenter.Play(Plan());
            Advance(_views[1].ActionEventDelaySeconds("cast") + .2f);
            Assert.That(ActiveFx.Any(image => image.name.Contains(Projectile)), Is.True);
            AssertNoResults();
            AssertCancellation(skip);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AbortOrSkipAfterTheBudgetExpiresKillsThePendingImpactAndCompletionRetry(bool skip)
        {
            _presenter.SpeedScale = 6f;
            var plan = Plan();
            _presenter.Play(plan);
            float budget = plan.TotalSeconds / _presenter.SpeedScale;
            float elapsed = 0f;
            const float step = 1f / 15f;
            while (elapsed < budget) { Advance(step, step); elapsed += step; }
            var sequencer = (BattleSequencer)typeof(BattlePresenter).GetField("_sequencer", Hidden).GetValue(_presenter);
            Assert.That(_presenter.IsPlaying, Is.True);
            Assert.That(RealtimeTween.IsTweening(sequencer), Is.True);
            Assert.That(typeof(BattlePresenter).GetField("_pendingCast", Hidden).GetValue(_presenter), Is.Not.Null);
            Assert.That(ActiveFx.Any(image => image.name.Contains(Impact) && FrameIndex(image, Impact) < 3), Is.True);
            AssertNoResults();
            AssertCancellation(skip);
            Assert.That(RealtimeTween.IsTweening(sequencer), Is.False);
        }

        private void AssertCancellation(bool skip)
        {
            int finished = 0, aborted = 0;
            _presenter.OnFinished += () => finished++;
            _presenter.OnAborted += () => aborted++;
            if (skip) _presenter.Skip(); else _presenter.Abort();
            Assert.That(_presenter.IsPlaying, Is.False);
            Assert.That(typeof(BattlePresenter).GetField("_pendingCast", Hidden).GetValue(_presenter), Is.Null);
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(skip ? 900 : 1000));
            Advance(3f, 1f / 15f);
            Assert.That(_damageTicks, Is.Empty);
            Assert.That(_buffTicks, Is.Empty);
            Assert.That(BuffIcons, Is.Empty);
            Assert.That(_presenter.Fx.ActiveCount, Is.Zero);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(skip ? 900 : 1000));
            Assert.That(finished, Is.EqualTo(skip ? 1 : 0));
            Assert.That(aborted, Is.EqualTo(skip ? 0 : 1));
        }

        private void AssertNoResults()
        {
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(1000), "Flight and pre-hit frames do not apply terminal HP.");
            Assert.That(_damageTicks, Is.Empty);
            Assert.That(_buffTicks, Is.Empty);
            Assert.That(_presenter.Numbers.ActiveCount, Is.Zero);
            Assert.That(BuffIcons, Is.Empty);
        }

        private void AssertOneResultBatch()
        {
            Assert.That(_views[2].CurrentHealth, Is.EqualTo(900));
            Assert.That(_damageTicks.Count, Is.EqualTo(1));
            Assert.That(_buffTicks.Count, Is.EqualTo(1));
            Assert.That(_buffTicks[0], Is.EqualTo(_damageTicks[0]));
        }

        private void AssertAnchor(Image image, Vector2 anchor)
        {
            var rt = image.rectTransform;
            var artPivot = new Vector2(.5f, .5f);
            var localArtCore = new Vector3((artPivot.x - rt.pivot.x) * rt.rect.width, (artPivot.y - rt.pivot.y) * rt.rect.height, 0f);
            var actual = rt.TransformPoint(localArtCore);
            var expected = _fxLayer.TransformPoint(new Vector3(_fxLayer.rect.xMin + anchor.x, _fxLayer.rect.yMax - anchor.y, 0f));
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.05f), "Rotation/mirroring must keep the artwork core at its intended design anchor.");
        }

        private static void AssertDirection(Image image, Vector2 from, Vector2 to)
        {
            var rt = image.rectTransform;
            var expected = new Vector2(to.x - from.x, from.y - to.y).normalized;
            var actual = (Vector2)(rt.TransformPoint(Vector3.right) - rt.TransformPoint(Vector3.zero));
            Assert.That(Vector2.Angle(actual, expected), Is.LessThan(.02f), "The source artwork points right, so its positive X axis must follow the UGUI flight vector.");
            float degrees = Mathf.Atan2(expected.y, expected.x) * Mathf.Rad2Deg;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(rt.localEulerAngles.z, degrees)), Is.LessThan(.02f));
        }

        private static TurnPlan Plan()
        {
            var plan = new TurnPlan();
            var beat = new Beat { ActorId = 1, SkillId = 1203, Kind = BeatKind.Cast, DurationSeconds = TurnPlan.CastSeconds };
            beat.Targets.Add(new BeatTarget { ActorId = 2, Effect = TargetEffect.Damage, Value = 100, HasHealthAfter = true, HealthAfter = 900 });
            beat.Targets.Add(new BeatTarget { ActorId = 2, Effect = TargetEffect.BuffAdd, BuffId = FixtureBuffId, Success = true });
            plan.Beats.Add(beat);
            return plan;
        }

        private RectTransform Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_host.transform, false);
            return go.GetComponent<RectTransform>();
        }

        private List<Image> ActiveFx => (List<Image>)typeof(BattleFxPlayer).GetField("_active", Hidden).GetValue(_presenter.Fx);
        private List<GameObject> BuffIcons => (List<GameObject>)typeof(BattleUnitView).GetField("_buffIcons", Hidden).GetValue(_views[2]);
        private static int FrameIndex(Image image, string id) => Array.IndexOf(BattleArtCatalog.LoadFx(id).Frames, image.sprite);
        private void Advance(float seconds, float maxStep = .002f)
        {
            while (seconds > .000001f)
            {
                float step = Mathf.Min(seconds, maxStep);
                _tick++;
                Tick.Invoke(null, new object[] { step, step });
                seconds -= step;
            }
        }

        [Serializable] private sealed class CatalogFile { public CatalogSkill[] skills; }
        [Serializable] private sealed class CatalogSkill
        {
            public uint id;
            public string name;
            public int maxTargets;
            public string targetTeam;
            public string buffKind;
            public float buffPercent;
            public int durationRounds;
            public string description;
        }
    }
}
