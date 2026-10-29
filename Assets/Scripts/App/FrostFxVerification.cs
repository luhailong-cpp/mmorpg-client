using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.App
{
    /// <summary>
    /// Separate native-player acceptance for skill 1203: -frostFxVerify -frostFxOut &lt;absolute directory&gt;.
    /// Synthetic 100-damage outcomes use the production presenter; no server or slow-debuff rules are simulated.
    /// The frost hit is a brief visual for a slow skill, not a freeze or immobilization mechanic.
    /// </summary>
    public sealed class FrostFxVerification : MonoBehaviour
    {
        private const string Tag = "[FrostFxVerification]";
        private const uint Skill = 1203;
        private const string Projectile = "hanxing_ningpo_projectile";
        private const string Impact = "hanxing_ningpo_impact";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int ExpectedCases = 12;

        [Serializable] public sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable] public sealed class CaseReceipt
        {
            public string name;
            public uint skillId = Skill;
            public string scope = "Offline synthetic 100 damage; skill 1203 slows, not freezes. Server damage and persistent slow/debuff rules are not validated here.";
            public Vector2 casterFoot;
            public Vector2 targetFoot;
            public Vector2 flightFrom;
            public Vector2 flightTo;
            public float expectedAngleDegrees;
            public float speed;
            public int targetFramesPerSecond;
            public int observedFrames;
            public float minimumFrameSeconds;
            public float maximumFrameSeconds;
            public float releaseSeconds;
            public float plannedSeconds;
            public float firstDamageSeconds = -1f;
            public float finishedSeconds = -1f;
            public int damageEvents;
            public int finishedEvents;
            public int abortedEvents;
            public int distinctDamageFrames;
            public int peakNumbers;
            public int peakProjectiles;
            public int peakImpacts;
            public int flightSamples;
            public float minimumFlightProgress = 1f;
            public float maximumFlightProgress;
            public float maximumPathError;
            public float maximumAngleError;
            public bool sawCastCharge;
            public bool sawProjectile;
            public bool sawImpact;
            public bool sawHitStar;
            public bool sawTargetHitAction;
            public bool resultBeforeImpact;
            public bool correctProjectilePivot = true;
            public bool correctProjectileScale = true;
            public bool flightProgressMonotonic = true;
            public bool correctFixedAnchors = true;
            public bool correctFixedRotation = true;
            public bool impactActiveAtFinish;
            public int activeFxBeforeCleanup;
            public int activeFxAfterCleanup;
            public string[] captures;
            public Check[] checks;
        }

        [Serializable] public sealed class Receipt
        {
            public string scope = "Native Unity player, separate 1203 batch; production BattlePresenter/BattleUnitView/BattleFxPlayer/DamageNumberPool with synthetic 100 damage. This does not validate server settlement, next-round ordering, or persistent slow buffs. Frost is not freeze.";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public int screenWidth;
            public int screenHeight;
            public bool passed;
            public Check[] contracts;
            public CaseReceipt[] cases;
            public string[] errors;
        }

        private readonly Dictionary<ulong, BattleUnitView> _views = new Dictionary<ulong, BattleUnitView>();
        private readonly List<CaseReceipt> _cases = new List<CaseReceipt>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<int> _damageFrames = new List<int>();
        private readonly List<string> _captures = new List<string>();
        private readonly HashSet<string> _capturedPhases = new HashSet<string>();
        private readonly List<Task> _captureWrites = new List<Task>();
        private string _out;
        private GameObject _canvas;
        private RectTransform _caseRoot;
        private RectTransform _fxLayer;
        private BattlePresenter _presenter;
        private TextMeshProUGUI _label;
        private CaseReceipt _current;
        private Vector2 _targetChest;
        private float _caseStart;
        private float _lastFlightProgress;
        private int _lastObservedFrame = -1;
        private int _lastFlightFrame = -1;
        private int _shots;
        private bool _observeCase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-frostFxVerify", StringComparison.OrdinalIgnoreCase))) return;
            if (FindFirstObjectByType<FrostFxVerification>() != null) return;
            var go = new GameObject("[FrostFxVerification]");
            DontDestroyOnLoad(go);
            go.AddComponent<FrostFxVerification>();
        }

        private void Start()
        {
            _out = Argument("-frostFxOut");
            if (string.IsNullOrWhiteSpace(_out) || !Path.IsPathRooted(_out))
            {
                Debug.LogError(Tag + " requires an absolute -frostFxOut directory");
                Application.Quit(2);
                return;
            }
            Directory.CreateDirectory(_out);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.logMessageReceived += RecordError;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var receipt = new Receipt { startedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                screenWidth = Screen.width, screenHeight = Screen.height };
            yield return null;
            CreateCanvas();
            receipt.contracts = CheckContracts();
            yield return Case("1203-east", new Vector2(600f, 720f), new Vector2(1750f, 720f));
            yield return Case("1203-west", new Vector2(1850f, 720f), new Vector2(700f, 720f));
            yield return Case("1203-northeast", new Vector2(650f, 850f), new Vector2(1700f, 520f));
            yield return Case("1203-southeast", new Vector2(650f, 520f), new Vector2(1700f, 850f));
            yield return Case("1203-northwest", new Vector2(1850f, 850f), new Vector2(800f, 520f));
            yield return Case("1203-southwest", new Vector2(1850f, 520f), new Vector2(800f, 850f));
            yield return Case("1203-speed6-fps15", new Vector2(1850f, 850f), new Vector2(800f, 520f), 6f, 15);
            yield return Case("1203-speed1.5-fps30", new Vector2(650f, 520f), new Vector2(1700f, 850f), 1.5f, 30);
            yield return InterruptedCase("1203-abort-flight", false, false);
            yield return InterruptedCase("1203-skip-flight", true, false);
            yield return InterruptedCase("1203-abort-expired-budget", false, true);
            yield return InterruptedCase("1203-skip-expired-budget", true, true);

            // Pixel reads happen on the main thread. Finish all background PNG writes before the receipt and exit.
            while (_captureWrites.Any(write => !write.IsCompleted)) yield return null;
            foreach (var write in _captureWrites)
                if (write.IsFaulted) _errors.Add("Screenshot write failed: " + write.Exception);
            receipt.finishedUtc = DateTime.UtcNow.ToString("o");
            receipt.cases = _cases.ToArray();
            receipt.errors = _errors.ToArray();
            receipt.passed = _errors.Count == 0 && _cases.Count == ExpectedCases &&
                receipt.contracts.All(check => check.passed) && _cases.All(item => item.checks.All(check => check.passed));
            File.WriteAllText(Path.Combine(_out, "frost-fx-verification.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log(Tag + " passed=" + receipt.passed + " cases=" + _cases.Count + " out=" + _out);
            CleanupCase();
            yield return null;
            Application.Quit(receipt.passed ? 0 : 1);
        }

        private static Check[] CheckContracts()
        {
            var presentation = BattleArtCatalog.ResolveSkillFx(Skill, false);
            var projectile = BattleArtCatalog.LoadFx(Projectile);
            var impact = BattleArtCatalog.LoadFx(Impact);
            return new[] {
                CheckFor("dedicated 1203 projectile and impact mapping", presentation.ProjectileFxId == Projectile && presentation.FxId == Impact && presentation.IsCast),
                CheckFor("one chest hit on frame 3 after a half-second flight", presentation.HitFrame == 3 && !presentation.TargetAtFeet && Mathf.Abs(presentation.FlightSeconds - .5f) < .0001f),
                CheckFor("projectile strip contract", ValidStrip(projectile)),
                CheckFor("impact strip contract", ValidStrip(impact))
            };
        }

        private static bool ValidStrip(StripAnim strip)
            => strip != null && strip.Count == 8 && Mathf.Abs(strip.Fps - 16f) < .0001f &&
                Vector2.Distance(strip.Pivot, new Vector2(.5f, .5f)) < .0001f;

        private void CreateCanvas()
        {
            _canvas = new GameObject("FrostFxAcceptanceCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(_canvas);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            var background = QdaoUguiFactory.CreateImage("Arena", _canvas.transform, 0f, 0f, 2560f, 1080f,
                BattleArtCatalog.LoadSprite(BattleArtCatalog.ArenaBackgroundPath));
            background.raycastTarget = false;
            if (background.sprite == null) background.color = new Color(.05f, .1f, .16f, 1f);
            _label = QdaoUguiFactory.CreateText("FrostOfflineFixtureLabel", _canvas.transform, 40f, 25f, 2480f, 150f,
                "", 28f, Color.white, TextAlignmentOptions.TopLeft);
            _label.raycastTarget = false;
        }

        private void SetupCase(string name, Vector2 casterFoot, Vector2 targetFoot, float speed, int framesPerSecond)
        {
            CleanupCase();
            _current = new CaseReceipt { name = name, casterFoot = casterFoot, targetFoot = targetFoot,
                speed = speed, targetFramesPerSecond = framesPerSecond };
            _damageFrames.Clear();
            _captures.Clear();
            _capturedPhases.Clear();
            _caseRoot = QdaoUguiFactory.CreateRect(name, _canvas.transform, 0f, 0f, 2560f, 1080f);
            var stage = Layer("Units");
            _fxLayer = Layer("Effects");
            var plates = Layer("Plates");
            var numbers = Layer("Numbers");
            var overlay = Layer("Overlay");
            _presenter = new BattlePresenter(this, stage, _fxLayer, numbers, overlay,
                id => _views.TryGetValue(id, out var view) ? view : null, () => _views.Values, id => "Frost fixture " + id);
            _presenter.SpeedScale = speed;
            bool east = targetFoot.x > casterFoot.x;
            for (ulong id = 1; id <= 2; id++)
            {
                bool caster = id == 1;
                var view = new BattleUnitView(null, stage, id, caster, caster ? !east : east, (int)id - 1, null, plates);
                view.Apply(new BattleActorState { ActorId = id, TeamIndex = caster ? 0u : 1u,
                    ActorType = eBattleActorType.BattleActorTypePlayer, Name = caster ? "玄霄 · 寒星凝魄测试" : "单体目标 · 减速非冻结",
                    Level = 20, Attributes = new BaseAttributesComp { Health = 1000, Mana = 1000 }, MaxHealth = 1000, MaxMana = 1000 });
                view.SetPlacement(caster ? casterFoot : targetFoot, 1f);
                view.Fx = _presenter.Fx;
                view.Numbers = _presenter.Numbers;
                view.Ghosts = _presenter.Ghosts;
                _views.Add(id, view);
            }
            _targetChest = _views[2].ChestPosition;
            Vector2 casterChest = _views[1].ChestPosition;
            _current.flightFrom = casterChest + (_targetChest - casterChest).normalized * 70f * Mathf.Max(.6f, _views[1].Scale);
            _current.flightTo = _targetChest;
            Vector2 direction = _current.flightTo - _current.flightFrom;
            _current.expectedAngleDegrees = Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg;
            _current.releaseSeconds = _views[1].ActionEventDelaySeconds("cast") / speed;
            _presenter.OnLog += OnPresentationLog;
            _presenter.OnHealthChanged += OnHealthChanged;
            _presenter.OnFinished += OnPresentationFinished;
            _presenter.OnAborted += OnPresentationAborted;
            _label.transform.SetAsLastSibling();
            _lastObservedFrame = -1;
            _lastFlightFrame = -1;
            _lastFlightProgress = -1f;
            _caseStart = Time.unscaledTime;
            _observeCase = true;
        }

        private IEnumerator Case(string name, Vector2 casterFoot, Vector2 targetFoot, float speed = 1f, int framesPerSecond = 60)
        {
            Application.targetFrameRate = framesPerSecond;
            yield return null;
            yield return null;
            SetupCase(name, casterFoot, targetFoot, speed, framesPerSecond);
            var plan = Plan();
            _presenter.PreparePlan(plan);
            _current.plannedSeconds = plan.TotalSeconds / speed;
            _presenter.Play(plan);
            float deadline = Time.unscaledTime + _current.plannedSeconds + 4f;
            while (_current.finishedEvents == 0 && Time.unscaledTime < deadline)
            {
                InspectFrame();
                yield return CaptureNewPhase();
                yield return null;
            }
            InspectFrame();
            yield return CaptureNewPhase();
            yield return new WaitForSecondsRealtime(1.35f);
            _current.activeFxBeforeCleanup = _presenter.Fx.ActiveCount;
            _observeCase = false;
            _presenter.ClearTransient();
            _current.activeFxAfterCleanup = _presenter.Fx.ActiveCount;
            CompleteCounts();
            var presentation = BattleArtCatalog.ResolveSkillFx(Skill, false);
            float earliestHit = _current.releaseSeconds + (presentation.FlightSeconds + 3f / 16f) / speed;
            _current.checks = new[] {
                CheckFor("production presenter finished exactly once", _current.finishedEvents == 1 && _current.abortedEvents == 0),
                CheckFor("general cast charge and hit-star visible", _current.sawCastCharge && _current.sawHitStar),
                CheckFor("existing target hit action visible", _current.sawTargetHitAction),
                CheckFor("dedicated flight and impact visible", _current.sawProjectile && _current.sawImpact && _current.peakProjectiles == 1 && _current.peakImpacts == 1),
                CheckFor("runtime projectile moves along the actual chest trajectory", _current.flightSamples >= 2 && _current.maximumFlightProgress - _current.minimumFlightProgress > .1f &&
                    _current.maximumPathError < .1f && _current.minimumFlightProgress >= -.001f && _current.maximumFlightProgress <= 1.001f && _current.flightProgressMonotonic,
                    "samples=" + _current.flightSamples + " progress=" + _current.minimumFlightProgress + ".." + _current.maximumFlightProgress + " pathError=" + _current.maximumPathError),
                CheckFor("projectile rotates to the trajectory around its sprite pivot", _current.correctProjectilePivot && _current.correctProjectileScale && _current.maximumAngleError < .1f,
                    "expectedAngle=" + _current.expectedAngleDegrees + " maxError=" + _current.maximumAngleError),
                CheckFor("chest impact and feet cast charge stay registered", _current.correctFixedAnchors && _current.correctFixedRotation),
                CheckFor("no result or number before impact frame 3", !_current.resultBeforeImpact),
                CheckFor("release and travel precede the hit", _current.firstDamageSeconds >= earliestHit - .002f,
                    "actual=" + _current.firstDamageSeconds + " expected>=" + earliestHit),
                CheckFor("one 100-damage result and number", _current.damageEvents == 1 && _current.distinctDamageFrames == 1 && _current.peakNumbers == 1 && _views[2].CurrentHealth == 900),
                CheckFor("complete impact tail before presenter finishes", _current.finishedEvents == 1 && !_current.impactActiveAtFinish),
                CheckFor("all transient effects recycled", _current.activeFxBeforeCleanup == 0 && _current.activeFxAfterCleanup == 0)
            };
            _cases.Add(_current);
            Debug.Log(Tag + " case=" + name + " passed=" + _current.checks.All(check => check.passed));
        }

        private IEnumerator InterruptedCase(string name, bool skip, bool expiredBudget)
        {
            float speed = expiredBudget ? 6f : 1f;
            int framesPerSecond = expiredBudget ? 15 : 60;
            Application.targetFrameRate = framesPerSecond;
            yield return null;
            yield return null;
            SetupCase(name, new Vector2(1850f, 850f), new Vector2(800f, 520f), speed, framesPerSecond);
            var plan = Plan();
            _presenter.PreparePlan(plan);
            _current.plannedSeconds = plan.TotalSeconds / speed;
            _presenter.Play(plan);
            float deadline = Time.unscaledTime + 4f;
            bool ready = false;
            while (Time.unscaledTime < deadline && _damageFrames.Count == 0)
            {
                InspectFrame();
                bool leadActive = ActiveFx.Any(image => image.name.Contains(Projectile));
                bool impactActive = ActiveFx.Any(image => image.name.Contains(Impact));
                ready = expiredBudget
                    ? Time.unscaledTime - _caseStart >= _current.plannedSeconds && _presenter.IsPlaying && (leadActive || impactActive)
                    : leadActive && _lastFlightProgress >= .25f;
                if (ready) break;
                yield return null;
            }
            var sequencer = (BattleSequencer)typeof(BattlePresenter).GetField("_sequencer", Hidden).GetValue(_presenter);
            bool waitingForCompletion = _presenter.IsPlaying && RealtimeTween.IsTweening(sequencer);
            bool noEarlyResult = _damageFrames.Count == 0 && !_current.resultBeforeImpact && _views[2].CurrentHealth == 1000;
            yield return Capture(expiredBudget ? "expired-budget-before-cancel" : "flight-before-cancel");
            _observeCase = false;
            if (skip) _presenter.Skip(); else _presenter.Abort();
            bool immediateCleanup = !_presenter.IsPlaying && !RealtimeTween.IsTweening(sequencer) &&
                _presenter.Fx.ActiveCount == 0 && _presenter.Numbers.ActiveCount == 0;
            yield return new WaitForSecondsRealtime(2f);
            _label.text = "OFFLINE 1203 — " + name + "\n寒星凝魄：减速，非冻结 / slow, not freeze\nNo late impact, retry or duplicate result after interruption";
            yield return Capture("interrupted");
            _current.activeFxAfterCleanup = _presenter.Fx.ActiveCount;
            CompleteCounts();
            _current.checks = new[] {
                CheckFor(expiredBudget ? "cancel while an expired beat waits for presentation" : "cancel during visible runtime flight", ready && waitingForCompletion),
                CheckFor("no result before interruption", noEarlyResult),
                CheckFor("immediate effect and completion-retry cleanup", immediateCleanup),
                CheckFor("no late result, number or retry", _damageFrames.Count == 0 && !_presenter.IsPlaying && !RealtimeTween.IsTweening(sequencer) &&
                    _presenter.Fx.ActiveCount == 0 && _presenter.Numbers.ActiveCount == 0),
                CheckFor("one cancellation notification without a duplicate finish", _current.finishedEvents == (skip ? 1 : 0) && _current.abortedEvents == (skip ? 0 : 1)),
                CheckFor("correct terminal health", _views[2].CurrentHealth == (skip ? 900UL : 1000UL))
            };
            _cases.Add(_current);
            Debug.Log(Tag + " case=" + name + " passed=" + _current.checks.All(check => check.passed));
        }

        private void InspectFrame()
        {
            if (_lastObservedFrame != Time.frameCount)
            {
                _lastObservedFrame = Time.frameCount;
                _current.observedFrames++;
                float seconds = Time.unscaledDeltaTime;
                if (_current.minimumFrameSeconds <= 0f || seconds < _current.minimumFrameSeconds) _current.minimumFrameSeconds = seconds;
                _current.maximumFrameSeconds = Mathf.Max(_current.maximumFrameSeconds, seconds);
            }
            var active = ActiveFx;
            var projectiles = active.Where(image => image.name.Contains(Projectile)).ToArray();
            var impacts = active.Where(image => image.name.Contains(Impact)).ToArray();
            _current.sawCastCharge |= active.Any(image => image.name.StartsWith("Fx_cast_charge_"));
            _current.sawHitStar |= active.Any(image => image.name.StartsWith("Fx_hit_star_"));
            _current.sawProjectile |= projectiles.Length > 0;
            _current.sawImpact |= impacts.Length > 0;
            _current.peakProjectiles = Math.Max(_current.peakProjectiles, projectiles.Length);
            _current.peakImpacts = Math.Max(_current.peakImpacts, impacts.Length);
            _current.peakNumbers = Math.Max(_current.peakNumbers, _presenter.Numbers.ActiveCount);
            var body = _views[2].BodyImage;
            _current.sawTargetHitAction |= body != null && body.sprite != null && body.sprite.name.Contains("_hit_");
            if (_damageFrames.Count == 0 && (_presenter.Numbers.ActiveCount != 0 || _views[2].CurrentHealth != 1000))
                _current.resultBeforeImpact = true;
            bool pending = projectiles.Length > 0 || (impacts.Length > 0 && impacts.All(image => FrameIndex(image, Impact) < 3));
            if (pending && (_damageFrames.Count != 0 || _presenter.Numbers.ActiveCount != 0 || _views[2].CurrentHealth != 1000))
                _current.resultBeforeImpact = true;

            foreach (var image in projectiles)
            {
                var strip = BattleArtCatalog.LoadFx(Projectile);
                var rt = image.rectTransform;
                var anchor = ImageAnchor(image, strip.Pivot);
                Vector2 path = _current.flightTo - _current.flightFrom;
                float progress = Vector2.Dot(anchor - _current.flightFrom, path) / Mathf.Max(.0001f, path.sqrMagnitude);
                float error = Vector2.Distance(anchor, _current.flightFrom + path * progress);
                _current.maximumPathError = Mathf.Max(_current.maximumPathError, error);
                _current.maximumAngleError = Mathf.Max(_current.maximumAngleError, Mathf.Abs(Mathf.DeltaAngle(_current.expectedAngleDegrees, rt.localEulerAngles.z)));
                _current.correctProjectilePivot &= Vector2.Distance(rt.pivot, strip.Pivot) < .0001f;
                _current.correctProjectileScale &= UnityEngine.Vector3.Distance(rt.localScale, UnityEngine.Vector3.one) < .0001f;
                if (_lastFlightFrame != Time.frameCount)
                {
                    _lastFlightFrame = Time.frameCount;
                    _current.flightSamples++;
                    _current.minimumFlightProgress = Mathf.Min(_current.minimumFlightProgress, progress);
                    _current.maximumFlightProgress = Mathf.Max(_current.maximumFlightProgress, progress);
                    _current.flightProgressMonotonic &= _lastFlightProgress < 0f || progress + .001f >= _lastFlightProgress;
                    _lastFlightProgress = progress;
                }
            }
            foreach (var image in active.Where(image => image.name.StartsWith("Fx_cast_charge_")))
                CheckFixedAnchor(image, new Vector2(.5f, .1f), _views[1].FootPosition);
            foreach (var image in impacts)
                CheckFixedAnchor(image, new Vector2(.5f, .5f), _targetChest);
            _label.text = "OFFLINE 1203 — " + _current.name + "\n寒星凝魄：减速，非冻结 / slow, not freeze; synthetic 100 damage\n" +
                "speed=" + _current.speed + "x  target FPS=" + _current.targetFramesPerSecond + "  t=" + (Time.unscaledTime - _caseStart).ToString("F3") +
                "  damage events=" + _damageFrames.Count + "  numbers=" + _presenter.Numbers.ActiveCount + "  FX=" + _presenter.Fx.ActiveCount;
        }

        private void CheckFixedAnchor(Image image, Vector2 pivot, Vector2 expected)
        {
            _current.correctFixedAnchors &= Vector2.Distance(ImageAnchor(image, pivot), expected) < .1f;
            _current.correctFixedRotation &= Quaternion.Angle(image.rectTransform.localRotation, Quaternion.identity) < .01f;
        }

        // Transform the sprite anchor through the real RectTransform. This remains valid for rotated, pivot-centred flight images.
        private Vector2 ImageAnchor(Image image, Vector2 spritePivot)
        {
            var rt = image.rectTransform;
            var local = new UnityEngine.Vector3((spritePivot.x - rt.pivot.x) * rt.rect.width,
                (spritePivot.y - rt.pivot.y) * rt.rect.height, 0f);
            var point = _fxLayer.InverseTransformPoint(rt.TransformPoint(local));
            return new Vector2(point.x, -point.y);
        }

        private void LateUpdate()
        {
            if (_observeCase && _presenter != null && _current != null) InspectFrame();
        }

        private IEnumerator CaptureNewPhase()
        {
            if (ActiveFx.Any(image => image.name.StartsWith("Fx_cast_charge_")) && !_capturedPhases.Contains("cast-charge"))
                yield return Capture("cast-charge");
            if (ActiveFx.Any(image => image.name.Contains(Projectile)) && !_capturedPhases.Contains("flight-start"))
                yield return Capture("flight-start");
            if (ActiveFx.Any(image => image.name.Contains(Projectile)) && _lastFlightProgress >= .2f && !_capturedPhases.Contains("flight-mid"))
                yield return Capture("flight-mid");
            if (ActiveFx.Any(image => image.name.Contains(Impact) && FrameIndex(image, Impact) < 3) && !_capturedPhases.Contains("pre-hit"))
                yield return Capture("pre-hit");
            if (_damageFrames.Count > 0 && !_capturedPhases.Contains("hit")) yield return Capture("hit");
            if (_damageFrames.Count > 0 && !_capturedPhases.Contains("reaction"))
            {
                yield return new WaitForSecondsRealtime(.08f / _current.speed);
                yield return Capture("reaction");
            }
        }

        private IEnumerator Capture(string phase)
        {
            _capturedPhases.Add(phase);
            yield return new WaitForEndOfFrame();
            string file = (++_shots).ToString("D3") + "-" + _current.name + "-" + phase + ".png";
            int width = Screen.width, height = Screen.height;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply(false, false);
            var pixels = texture.GetRawTextureData<byte>().ToArray();
            var format = texture.graphicsFormat;
            Destroy(texture);
            _captures.Add(file);
            string path = Path.Combine(_out, file);
            // EncodeArrayToPNG is thread safe; only managed pixel data and immutable capture values cross threads.
            _captureWrites.Add(Task.Run(() => File.WriteAllBytes(path,
                ImageConversion.EncodeArrayToPNG(pixels, format, (uint)width, (uint)height))));
        }

        private void OnPresentationLog(string line)
        {
            if (!line.Contains("受到") || !line.Contains("伤害")) return;
            if (!ActiveFx.Any(image => image.name.Contains(Impact) && FrameIndex(image, Impact) >= 3))
                _current.resultBeforeImpact = true;
            _damageFrames.Add(Time.frameCount);
            if (_current.firstDamageSeconds < 0f) _current.firstDamageSeconds = Time.unscaledTime - _caseStart;
        }

        private void OnHealthChanged(ulong actorId, ulong health)
        {
            if (_observeCase && actorId == 2 && health != 1000 && _damageFrames.Count == 0) _current.resultBeforeImpact = true;
        }

        private void OnPresentationFinished()
        {
            _current.finishedEvents++;
            _current.finishedSeconds = Time.unscaledTime - _caseStart;
            _current.impactActiveAtFinish |= ActiveFx.Any(image => image.name.Contains(Impact));
        }

        private void OnPresentationAborted() => _current.abortedEvents++;

        private void CompleteCounts()
        {
            _current.damageEvents = _damageFrames.Count;
            _current.distinctDamageFrames = _damageFrames.Distinct().Count();
            _current.captures = _captures.ToArray();
        }

        private void CleanupCase()
        {
            _observeCase = false;
            if (_presenter != null)
            {
                _presenter.OnLog -= OnPresentationLog;
                _presenter.OnHealthChanged -= OnHealthChanged;
                _presenter.OnFinished -= OnPresentationFinished;
                _presenter.OnAborted -= OnPresentationAborted;
                _presenter.Dispose();
                _presenter = null;
            }
            foreach (var view in _views.Values) view.Destroy();
            _views.Clear();
            if (_caseRoot != null) Destroy(_caseRoot.gameObject);
        }

        private void RecordError(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) _errors.Add(message + "\n" + stack);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= RecordError;
            CleanupCase();
            if (_canvas != null) Destroy(_canvas);
        }

        private RectTransform Layer(string name) => QdaoUguiFactory.CreateRect(name, _caseRoot, 0f, 0f, 2560f, 1080f);
        private List<Image> ActiveFx => (List<Image>)typeof(BattleFxPlayer).GetField("_active", Hidden).GetValue(_presenter.Fx);
        private static int FrameIndex(Image image, string id)
        {
            var strip = BattleArtCatalog.LoadFx(id);
            return strip == null ? -1 : Array.IndexOf(strip.Frames, image.sprite);
        }
        private static Check CheckFor(string name, bool passed, string detail = "") => new Check { name = name, passed = passed, detail = detail };
        private static string Argument(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
        private static TurnPlan Plan()
        {
            var plan = new TurnPlan();
            var beat = new Beat { ActorId = 1, Kind = BeatKind.Cast, SkillId = Skill, DurationSeconds = TurnPlan.CastSeconds };
            beat.Targets.Add(new BeatTarget { ActorId = 2, Effect = TargetEffect.Damage, Value = 100, HasHealthAfter = true, HealthAfter = 900 });
            plan.Beats.Add(beat);
            return plan;
        }
    }
}
