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
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.App
{
    /// <summary>
    /// Explicit, offline acceptance run of the production presentation classes in a native player.
    /// -combatFxVerify -combatFxOut <absolute output directory>
    /// This does not enter an account or replace a network transport. Synthetic outcomes are labelled in the receipt.
    /// </summary>
    public sealed class CombatFxVerification : MonoBehaviour
    {
        private const string Tag = "[CombatFxVerification]";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable] public sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }
        [Serializable] public sealed class CaseReceipt
        {
            public string name;
            public uint skillId;
            public int targets;
            public bool casterFacesEast;
            public float speed;
            public int targetFramesPerSecond;
            public int observedFrames;
            public float minimumFrameSeconds;
            public float maximumFrameSeconds;
            public float releaseSeconds;
            public float plannedSeconds;
            public float finishedSeconds = -1f;
            public float firstDamageSeconds = -1f;
            public int damageEvents;
            public int distinctDamageFrames;
            public int peakNumbers;
            public int peakLeadInstances;
            public int peakImpactInstances;
            public bool sawCastCharge;
            public bool sawLeadIn;
            public bool sawImpact;
            public bool sawHitStar;
            public bool resultBeforeImpact;
            public bool targetFramesSynchronized = true;
            public bool correctAnchor = true;
            public bool correctProjectileMirror = true;
            public bool impactActiveAtFinish;
            public int activeFxBeforeCleanup;
            public int activeFxAfterCleanup;
            public string[] captures;
            public Check[] checks;
        }
        [Serializable] public sealed class Receipt
        {
            public string scope = "Native Unity player; offline synthetic outcomes through production BattlePresenter, BattleUnitView, BattleFxPlayer and DamageNumberPool. This is presentation acceptance, not server battle validation.";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public int screenWidth;
            public int screenHeight;
            public bool passed;
            public CaseReceipt[] cases;
            public string[] errors;
        }

        private readonly Dictionary<ulong, BattleUnitView> _views = new Dictionary<ulong, BattleUnitView>();
        private readonly List<CaseReceipt> _cases = new List<CaseReceipt>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<int> _damageFrames = new List<int>();
        private readonly List<string> _captures = new List<string>();
        private readonly HashSet<string> _capturedPhases = new HashSet<string>();
        private readonly Dictionary<ulong, Vector2> _targetChests = new Dictionary<ulong, Vector2>();
        private string _out;
        private GameObject _canvas;
        private RectTransform _caseRoot;
        private RectTransform _fxLayer;
        private BattlePresenter _presenter;
        private TextMeshProUGUI _label;
        private CaseReceipt _current;
        private float _caseStart;
        private int _shots;
        private bool _observeCase;
        private int _lastObservedFrame = -1;
        private readonly List<Task> _captureWrites = new List<Task>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Any(arg => string.Equals(arg, "-combatFxVerify", StringComparison.OrdinalIgnoreCase))) return;
            if (FindFirstObjectByType<CombatFxVerification>() != null) return;
            var go = new GameObject("[CombatFxVerification]");
            DontDestroyOnLoad(go);
            go.AddComponent<CombatFxVerification>();
        }

        private void Start()
        {
            _out = Argument("-combatFxOut");
            if (string.IsNullOrWhiteSpace(_out) || !Path.IsPathRooted(_out))
            {
                Debug.LogError(Tag + " requires an absolute -combatFxOut directory");
                Application.Quit(2);
                return;
            }
            Directory.CreateDirectory(_out);
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Application.logMessageReceived += RecordError;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var receipt = new Receipt { startedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                screenWidth = Screen.width, screenHeight = Screen.height };
            yield return null;
            CreateCanvas();
            yield return Case("1201-west", 1201, 1, false);
            yield return Case("1201-east", 1201, 1, true);
            yield return Case("1202-one", 1202, 1, false);
            yield return Case("1202-three", 1202, 3, false);
            yield return InterruptedCase("1201-abort-flight", false);
            yield return InterruptedCase("1201-skip-flight", true);
            yield return Case("1201-speed6-fps15", 1201, 1, false, 6f, 15);
            yield return Case("1202-speed6-fps15", 1202, 3, false, 6f, 15);
            yield return Case("1201-speed1.5-fps30", 1201, 1, true, 1.5f, 30);
            yield return Case("1202-speed1.5-fps30", 1202, 3, false, 1.5f, 30);
            yield return InterruptedCase("1202-abort-telegraph", false, 1202, 3);
            yield return InterruptedCase("1202-skip-telegraph", true, 1202, 3);
            // Keep PNG compression and disk writes off the presentation clock, but finish them before the receipt.
            while (_captureWrites.Any(write => !write.IsCompleted)) yield return null;
            foreach (var write in _captureWrites)
                if (write.IsFaulted) _errors.Add("Screenshot write failed: " + write.Exception);
            receipt.finishedUtc = DateTime.UtcNow.ToString("o");
            receipt.cases = _cases.ToArray();
            receipt.errors = _errors.ToArray();
            receipt.passed = _errors.Count == 0 && _cases.Count == 12 && _cases.All(item => item.checks.All(check => check.passed));
            File.WriteAllText(Path.Combine(_out, "combat-fx-verification.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log(Tag + " passed=" + receipt.passed + " cases=" + _cases.Count + " out=" + _out);
            CleanupCase();
            yield return null;
            Application.Quit(receipt.passed ? 0 : 1);
        }

        private void CreateCanvas()
        {
            _canvas = new GameObject("CombatFxAcceptanceCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(_canvas);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            var bg = QdaoUguiFactory.CreateImage("Arena", _canvas.transform, 0f, 0f, 2560f, 1080f,
                BattleArtCatalog.LoadSprite(BattleArtCatalog.ArenaBackgroundPath));
            bg.raycastTarget = false;
            if (bg.sprite == null) bg.color = new Color(.05f, .1f, .16f, 1f);
            _label = QdaoUguiFactory.CreateText("OfflineFixtureLabel", _canvas.transform, 40f, 25f, 2480f, 130f,
                "", 32f, Color.white, TextAlignmentOptions.TopLeft);
            _label.raycastTarget = false;
        }

        private void SetupCase(string name, uint skill, int targets, bool east, float speed = 1f, int framesPerSecond = 60)
        {
            CleanupCase();
            _current = new CaseReceipt { name = name, skillId = skill, targets = targets, casterFacesEast = east,
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
                id => _views.TryGetValue(id, out var view) ? view : null, () => _views.Values, id => "Fixture " + id);
            _presenter.SpeedScale = speed;
            for (ulong id = 1; id <= (ulong)targets + 1; id++)
            {
                bool caster = id == 1;
                var view = new BattleUnitView(null, stage, id, caster, caster ? !east : east, (int)id - 1, null, plates);
                view.Apply(new BattleActorState { ActorId = id, TeamIndex = caster ? 0u : 1u,
                    ActorType = eBattleActorType.BattleActorTypePlayer, Name = caster ? "玄霄 · 演出测试" : "目标 " + (id - 1),
                    Level = 12, Attributes = new BaseAttributesComp { Health = 1000, Mana = 1000 }, MaxHealth = 1000, MaxMana = 1000 });
                view.SetPlacement(new Vector2(caster ? (east ? 600f : 1850f) : (east ? 1650f : 650f) + 180f * (id - 2),
                    caster ? 720f : 630f + 130f * (id - 2)), 1f);
                view.Fx = _presenter.Fx;
                view.Numbers = _presenter.Numbers;
                view.Ghosts = _presenter.Ghosts;
                _views.Add(id, view);
                if (!caster) _targetChests.Add(id, view.ChestPosition);
            }
            _presenter.OnLog += OnPresentationLog;
            _presenter.OnHealthChanged += OnHealthChanged;
            _current.releaseSeconds = _views[1].ActionEventDelaySeconds("cast") / speed;
            _label.transform.SetAsLastSibling();
            _caseStart = Time.unscaledTime;
            _lastObservedFrame = -1;
            _observeCase = true;
        }

        private IEnumerator Case(string name, uint skill, int targets, bool east, float speed = 1f, int framesPerSecond = 60)
        {
            Application.targetFrameRate = framesPerSecond;
            // Let the native player's frame limiter settle before timing the production presenter.
            yield return null;
            yield return null;
            SetupCase(name, skill, targets, east, speed, framesPerSecond);
            var plan = Plan(skill, targets);
            _presenter.PreparePlan(plan);
            _current.plannedSeconds = plan.TotalSeconds / speed;
            bool finished = false;
            string impactId = BattleArtCatalog.ResolveSkillFx(skill, false).FxId;
            _presenter.OnFinished += () =>
            {
                finished = true;
                _current.finishedSeconds = Time.unscaledTime - _caseStart;
                _current.impactActiveAtFinish = ActiveFx.Any(image => image.name.Contains(impactId));
            };
            _presenter.Play(plan);
            float deadline = Time.unscaledTime + _current.plannedSeconds + 3f;
            while (!finished && Time.unscaledTime < deadline)
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
            float lead = skill == 1201 ? BattleArtCatalog.ResolveSkillFx(skill, false).FlightSeconds : BattleArtCatalog.ResolveSkillFx(skill, false).TelegraphSeconds;
            _current.damageEvents = _damageFrames.Count;
            _current.distinctDamageFrames = _damageFrames.Distinct().Count();
            _current.captures = _captures.ToArray();
            _current.checks = new[]
            {
                CheckFor("production presenter finished", finished),
                CheckFor("cast charge visible", _current.sawCastCharge),
                CheckFor("lead-in and impact visible", _current.sawLeadIn && _current.sawImpact),
                CheckFor("one synchronized effect instance per actual target", _current.peakLeadInstances == targets && _current.peakImpactInstances == targets && _current.targetFramesSynchronized),
                CheckFor("hit-star and target reaction visible", _current.sawHitStar),
                CheckFor("no result or number before hit frame", !_current.resultBeforeImpact),
                CheckFor("hit delayed through lead-in", _current.firstDamageSeconds >= _current.releaseSeconds + (lead + 3f / 16f) / speed - .002f,
                    "actual=" + _current.firstDamageSeconds + " expected>=" + (_current.releaseSeconds + (lead + 3f / 16f) / speed)),
                CheckFor("one result per actual target", _current.damageEvents == targets && _views.Values.Where(view => view.ActorId > 1).All(view => view.CurrentHealth == 900)),
                CheckFor("one shared impact frame", _current.distinctDamageFrames == 1 && _current.targetFramesSynchronized),
                CheckFor("one number per actual target", _current.peakNumbers == targets),
                CheckFor("feet/chest registration", _current.correctAnchor),
                CheckFor("projectile direction", _current.correctProjectileMirror),
                CheckFor("complete impact tail before presenter finishes", finished && !_current.impactActiveAtFinish),
                CheckFor("all transient effects recycled", _current.activeFxBeforeCleanup == 0 && _current.activeFxAfterCleanup == 0)
            };
            _cases.Add(_current);
            Debug.Log(Tag + " case=" + name + " passed=" + _current.checks.All(check => check.passed));
        }

        private IEnumerator InterruptedCase(string name, bool skip, uint skill = 1201, int targets = 1)
        {
            Application.targetFrameRate = 60;
            yield return null;
            yield return null;
            SetupCase(name, skill, targets, false);
            var plan = Plan(skill, targets);
            _presenter.Play(plan);
            float until = Time.unscaledTime + _current.releaseSeconds + .2f;
            while (Time.unscaledTime < until)
            {
                InspectFrame();
                yield return null;
            }
            string leadId = skill == 1201 ? "xuanfu_luoyao_projectile" : "zixiao_leizhen_telegraph";
            bool sawLead = ActiveFx.Count(image => image.name.Contains(leadId)) == targets;
            bool noEarlyResult = _damageFrames.Count == 0 && !_current.resultBeforeImpact;
            _observeCase = false;
            if (skip) _presenter.Skip(); else _presenter.Abort();
            int fxAfter = _presenter.Fx.ActiveCount;
            int numbersAfter = _presenter.Numbers.ActiveCount;
            yield return new WaitForSecondsRealtime(2f);
            _label.text = "OFFLINE PRESENTATION FIXTURE — " + name + "\nNo late impact or duplicate number after interruption";
            yield return Capture("interrupted");
            _current.activeFxAfterCleanup = _presenter.Fx.ActiveCount;
            _current.damageEvents = _damageFrames.Count;
            _current.captures = _captures.ToArray();
            _current.checks = new[] {
                CheckFor("interrupt during visible lead-in", sawLead),
                CheckFor("no result before interruption", noEarlyResult),
                CheckFor("immediate pool cleanup", fxAfter == 0 && numbersAfter == 0),
                CheckFor("no late outcome or number", _damageFrames.Count == 0 && _presenter.Numbers.ActiveCount == 0 && _presenter.Fx.ActiveCount == 0),
                CheckFor("correct terminal state", _views.Values.Where(view => view.ActorId > 1).All(view => view.CurrentHealth == (skip ? 900UL : 1000UL)))
            };
            _cases.Add(_current);
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
            _current.sawCastCharge |= active.Any(image => image.name.StartsWith("Fx_cast_charge_"));
            _current.sawHitStar |= active.Any(image => image.name.StartsWith("Fx_hit_star_"));
            _current.peakNumbers = Math.Max(_current.peakNumbers, _presenter.Numbers.ActiveCount);
            string leadId = _current.skillId == 1201 ? "xuanfu_luoyao_projectile" : "zixiao_leizhen_telegraph";
            string impactId = _current.skillId == 1201 ? "xuanfu_luoyao_impact" : "zixiao_leizhen_strike";
            var lead = active.Where(image => image.name.Contains(leadId)).ToArray();
            var impact = active.Where(image => image.name.Contains(impactId)).ToArray();
            _current.sawLeadIn |= lead.Length > 0;
            _current.sawImpact |= impact.Length > 0;
            _current.peakLeadInstances = Math.Max(_current.peakLeadInstances, lead.Length);
            _current.peakImpactInstances = Math.Max(_current.peakImpactInstances, impact.Length);
            if (_damageFrames.Count == 0 && (_presenter.Numbers.ActiveCount != 0 || _views.Values.Any(view => view.ActorId > 1 && view.CurrentHealth != 1000)))
                _current.resultBeforeImpact = true;
            bool pending = lead.Length > 0 || (impact.Length > 0 && impact.All(image => FrameIndex(image, impactId) < 3));
            if (pending && (_damageFrames.Count != 0 || _presenter.Numbers.ActiveCount != 0 || _views.Values.Any(view => view.ActorId > 1 && view.CurrentHealth != 1000)))
                _current.resultBeforeImpact = true;
            if (lead.Length > 0 && _current.skillId == 1201)
                _current.correctProjectileMirror &= lead.All(image => image.rectTransform.localScale.x < 0f == !_current.casterFacesEast);
            if (impact.Length > 1) _current.targetFramesSynchronized &= impact.Select(image => FrameIndex(image, impactId)).Distinct().Count() == 1;
            if (lead.Length > 1) _current.targetFramesSynchronized &= lead.Select(image => FrameIndex(image, leadId)).Distinct().Count() == 1;
            foreach (var charge in active.Where(image => image.name.StartsWith("Fx_cast_charge_")))
                _current.correctAnchor &= Vector2.Distance(VisualAnchor(charge, new Vector2(.5f, .1f)), _views[1].FootPosition) < .05f;
            if (_current.skillId == 1202)
                foreach (var image in lead)
                    _current.correctAnchor &= _views.Values.Where(view => view.ActorId > 1).Any(view => Vector2.Distance(VisualAnchor(image, new Vector2(.5f, .5f)), view.FootPosition) < .05f);
            foreach (var image in impact)
            {
                Vector2 pivot = BattleArtCatalog.LoadFx(impactId).Pivot;
                Vector2 anchor = VisualAnchor(image, pivot);
                // The impact was registered before the target's hit action changes its visible bounds.
                _current.correctAnchor &= _views.Values.Where(view => view.ActorId > 1).Any(view => Vector2.Distance(anchor, _current.skillId == 1201 ? _targetChests[view.ActorId] : view.FootPosition) < .05f);
            }
            _label.text = "OFFLINE PRESENTATION FIXTURE — " + _current.name + "\n" +
                "speed=" + _current.speed + "x  target FPS=" + _current.targetFramesPerSecond + "  t=" + (Time.unscaledTime - _caseStart).ToString("F3") + "  damage events=" + _damageFrames.Count +
                "  numbers=" + _presenter.Numbers.ActiveCount + "  FX=" + _presenter.Fx.ActiveCount;
        }

        private void LateUpdate()
        {
            // Screenshot coroutines may span several frames; acceptance probes still inspect every native update.
            if (_observeCase && _presenter != null && _current != null) InspectFrame();
        }

        private IEnumerator CaptureNewPhase()
        {
            string impactId = _current.skillId == 1201 ? "xuanfu_luoyao_impact" : "zixiao_leizhen_strike";
            if (_current.sawCastCharge && !_capturedPhases.Contains("cast-charge")) yield return Capture("cast-charge");
            if (_current.sawLeadIn && !_capturedPhases.Contains("lead-in")) yield return Capture("lead-in");
            if (_current.skillId == 1201 && !_capturedPhases.Contains("flight-mid") &&
                Time.unscaledTime - _caseStart >= _current.releaseSeconds + .2f / _current.speed &&
                ActiveFx.Any(image => image.name.Contains("xuanfu_luoyao_projectile")))
                yield return Capture("flight-mid");
            if (ActiveFx.Any(image => image.name.Contains(impactId) && FrameIndex(image, impactId) < 3) && !_capturedPhases.Contains("pre-hit")) yield return Capture("pre-hit");
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
            // EncodeArrayToPNG is thread safe; texture access and frame capture stay on Unity's main thread.
            _captureWrites.Add(Task.Run(() => File.WriteAllBytes(path,
                ImageConversion.EncodeArrayToPNG(pixels, format, (uint)width, (uint)height))));
        }

        private void OnPresentationLog(string line)
        {
            if (!line.Contains("受到") || !line.Contains("伤害")) return;
            string impactId = BattleArtCatalog.ResolveSkillFx(_current.skillId, false).FxId;
            if (!ActiveFx.Any(image => image.name.Contains(impactId) && FrameIndex(image, impactId) >= 3))
                _current.resultBeforeImpact = true;
            _damageFrames.Add(Time.frameCount);
            if (_current.firstDamageSeconds < 0f) _current.firstDamageSeconds = Time.unscaledTime - _caseStart;
        }

        private void OnHealthChanged(ulong actorId, ulong health)
        {
            // Detect a premature beat-end write even when the real hit follows later in the same frame.
            if (_observeCase && actorId > 1 && health != 1000 && _damageFrames.Count == 0) _current.resultBeforeImpact = true;
        }

        private void CleanupCase()
        {
            _observeCase = false;
            if (_presenter != null)
            {
                _presenter.OnLog -= OnPresentationLog;
                _presenter.OnHealthChanged -= OnHealthChanged;
                _presenter.Dispose();
                _presenter = null;
            }
            foreach (var view in _views.Values) view.Destroy();
            _views.Clear();
            _targetChests.Clear();
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
        private static int FrameIndex(Image image, string id) => Array.IndexOf(BattleArtCatalog.LoadFx(id).Frames, image.sprite);
        private static Check CheckFor(string name, bool passed, string detail = "") => new Check { name = name, passed = passed, detail = detail };
        private static string Argument(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
        private static Vector2 VisualAnchor(Image image, Vector2 pivot)
        {
            var rt = image.rectTransform;
            float left = rt.anchoredPosition.x - (rt.localScale.x < 0f ? rt.sizeDelta.x : 0f);
            return new Vector2(left + rt.sizeDelta.x * pivot.x, -rt.anchoredPosition.y + rt.sizeDelta.y * (1f - pivot.y));
        }
        private static TurnPlan Plan(uint skill, int targets)
        {
            var plan = new TurnPlan();
            var beat = new Beat { ActorId = 1, Kind = BeatKind.Cast, SkillId = skill, DurationSeconds = TurnPlan.CastSeconds };
            for (ulong id = 2; id <= (ulong)targets + 1; id++)
                beat.Targets.Add(new BeatTarget { ActorId = id, Effect = TargetEffect.Damage, Value = 100, HealthAfter = 900, HasHealthAfter = true });
            plan.Beats.Add(beat);
            return plan;
        }
    }
}
