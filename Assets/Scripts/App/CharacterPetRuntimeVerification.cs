#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MmorpgClient.Game;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MmorpgClient.App
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;
    /// <summary>
    /// 显式命令行启用的真实运行验收：Bootstrap、正常登录管线、真实网络客户端与正式 UI。
    /// 不创建替代窗口、不绘制参考图、不伪造属性；截图只读实际屏幕的帧末输出。
    /// </summary>
    public sealed class CharacterPetRuntimeVerification : MonoBehaviour
    {
        [Serializable] public sealed class Plan
        {
            public string outputDirectory;
            public string account;
            public string passwordEnvironment = "MMORPG_UI_QA_PASSWORD";
            public uint zone = 1;
            public bool prepareDedicatedAccount;
            public uint level = 60;
            public uint petTableId = 1;
            public uint[] characterSkills = { 10010, 10040 };
            public uint[] petSkills = { 10010 };
            public int width = 2560, height = 1080;
            public Step[] steps;
        }

        [Serializable] public sealed class Step
        {
            public string operation, target, text, screenshot;
            public bool expected;
            public float seconds = .4f;
            public int width, height;
        }

        [Serializable] public sealed class Control
        {
            public string path, text, sprite;
            public bool interactable;
            public float x, y, width, height, alpha;
        }

        [Serializable] public sealed class Evidence
        {
            public string file, sha256, utc, scene, operation;
            public string dataSource = "local live server; dedicated QA account";
            public int width, height, frame;
            public ulong playerId;
            public bool inGame, gateReady;
            public string battlePhase;
            public Control[] buttons;
            public string[] visibleText;
        }

        [Serializable] public sealed class Report
        {
            public string result = "running", startedUtc, finishedUtc, unityVersion, scene, account, error;
            public string captureMethod = "ScreenCapture.CaptureScreenshotAsTexture at WaitForEndOfFrame";
            public string entry = "production Bootstrap scene -> GameClient.EnterZone -> production UI methods and Button events";
            public bool liveServerVerified, isEditor, visualReferenceMatchClaimed;
            public ulong playerId;
            public List<Evidence> screenshots = new();
            public List<string> actions = new();
            public List<string> runtimeErrors = new();
        }

        private Plan _plan;
        private Report _report;
        private GameClient _client;
        private bool _finished;
        private string _failure;
        private float _startedAt;
        private int _requestedWidth, _requestedHeight;
        private PetInfo _allocationBefore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            string planPath = Argument("-characterPetQaPlan");
            if (string.IsNullOrEmpty(planPath) || FindAnyObjectByType<CharacterPetRuntimeVerification>() != null) return;
            var host = new GameObject("[CharacterPetRuntimeVerification]");
            DontDestroyOnLoad(host);
            var runner = host.AddComponent<CharacterPetRuntimeVerification>();
            runner._plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
        }

        private IEnumerator Start()
        {
            _startedAt = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            if (_plan == null || string.IsNullOrWhiteSpace(_plan.outputDirectory) ||
                string.IsNullOrWhiteSpace(_plan.account) ||
                !(_plan.account.StartsWith("ui_50fdfb_", StringComparison.Ordinal) || _plan.account.StartsWith("dev_ui_50fdfb_", StringComparison.Ordinal)))
            { Debug.LogError("Character/pet QA requires an explicit dedicated ui_50fdfb_ or dev_ui_50fdfb_ account and output path."); Application.Quit(2); yield break; }
            _plan.outputDirectory = Path.GetFullPath(_plan.outputDirectory);
            Directory.CreateDirectory(_plan.outputDirectory);
            _report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                isEditor = Application.isEditor, account = _plan.account, scene = SceneManager.GetActiveScene().path };
            Application.logMessageReceived += OnLog;
            WriteReport();
            SetResolution(_plan.width, _plan.height);
            yield return WaitFor(() => AppBootstrap.Instance?.GameClient != null, 20, "Bootstrap did not create GameClient");
            if (StopIfFailed()) yield break;
            var app = AppBootstrap.Instance;
            _client = app.GameClient;
            // 正常用户登录前，角色选择会先预热本地资源；验收也等待同一正式准备状态，避免重资源加载占满 HTTP 的五秒预算。
            _report.actions.Add("Waiting for production role-resource prewarm before login");
            WriteReport();
            yield return WaitFor(() => MmorpgClient.UI.Ugui.Role.RoleFlowUi.Instance?.CreationChoicesPrewarmed == true,
                90, "Production role-resource prewarm did not complete");
            if (StopIfFailed()) yield break;
            string password = Environment.GetEnvironmentVariable(_plan.passwordEnvironment);
            if (string.IsNullOrEmpty(password)) { Finish("failed", "QA password environment variable is absent"); yield break; }
            bool entered = false;
            string loginError = null;
            _client.PlayerChooser = null; // 与既有 DevAutoPilot 相同的正常建角/选角管线，仅作用于专用账号。
            app.Run(_client.EnterZone(_plan.zone, _plan.account, password, _plan.account,
                () => entered = true, error => loginError = error));
            yield return WaitFor(() => entered || loginError != null, 90, "Login/EnterZone timed out");
            if (loginError != null) { _failure = "Login failed: " + loginError; Debug.LogWarning("[CharacterPetRuntimeVerification] " + _failure); }
            if (_failure != null) yield return Capture("00-login-failed", "login");
            if (StopIfFailed()) yield break;
            _report.liveServerVerified = _client.InGame && _client.IsGateReady;
            _report.playerId = _client.PlayerId;
            if (!_report.liveServerVerified || _client.PlayerId == 0) { Finish("failed", "Login did not reach a ready game session"); yield break; }
            app.Ugui.SetServerSelectVisible(false);
            _client.Attributes.OnError += OnClientError;
            _client.Pets.OnError += OnClientError;
            _client.Attributes.RequestPanel();
            _client.Pets.RequestList();
            yield return WaitFor(() => _client.Attributes.HasPanel && _client.Pets.HasList, 20, "Initial player/pet snapshots were not received");
            if (StopIfFailed()) yield break;
            yield return new WaitForSecondsRealtime(2);
            yield return ReturnToScene("before");
            if (StopIfFailed()) yield break;
            yield return Capture("00-live-scene", "login");
            if (_plan.prepareDedicatedAccount)
            {
                yield return PrepareAccount();
                if (StopIfFailed()) yield break;
            }
            WriteAuthoritySnapshot("initial");
            yield return new WaitForSecondsRealtime(2);
            foreach (var step in _plan.steps ?? Array.Empty<Step>())
            {
                if (_failure != null) break;
                _report.actions.Add(DateTime.UtcNow.ToString("O") + " " + step.operation + " " + step.target);
                bool ok = true;
                try { Execute(step); }
                catch (Exception error) { _failure = error.ToString(); ok = false; }
                if (!ok) break;
                if (step.operation == "wait-idle")
                    yield return WaitFor(() => !_client.Attributes.Busy && !_client.Pets.Busy && !_client.Pets.ListRequestPending &&
                        !_client.Skills.State.IsBusy && !_client.Skills.State.IsLoading, 20, "UI requests did not finish");
                if (step.operation == "wait-battle")
                    yield return WaitFor(() => BattleUiRoot.Instance?.IsBattleLayerVisible == true && _client.Battle.HasActiveBattle, 60, "Live PVE battle did not begin");
                if (step.operation == "wait-battle-end")
                    yield return WaitFor(() => !_client.Battle.HasActiveBattle, 90, "Live PVE battle did not finish");
                if (step.operation == "return-to-scene") yield return ReturnToScene("after");
                if (step.seconds > 0) yield return new WaitForSecondsRealtime(Mathf.Min(step.seconds, 20));
                if (!string.IsNullOrEmpty(step.screenshot)) yield return Capture(step.screenshot, step.operation);
                WriteReport();
            }
            if (_failure != null) yield return Capture("verification-failed", "failure");
            WriteAuthoritySnapshot("final");
            Finish(_failure == null ? "captured; visual review pending" : "failed", _failure);
        }

        private IEnumerator PrepareAccount()
        {
            // 只调用游戏既有开发服 GM / 正常技能修习；不直写数据库、不修改其它玩家。
            _client.Attributes.GmSetLevel(_plan.level);
            yield return WaitFor(() => !_client.Attributes.Busy, 20, "GM level request timed out");
            if (_failure != null) yield break;
            _client.Pets.RequestList();
            yield return WaitFor(() => !_client.Pets.ListRequestPending, 20, "Pet refresh timed out");
            if (_client.Pets.Pets.Pets.Count == 0)
            {
                _client.Pets.GmGrantPet(_plan.petTableId);
                yield return WaitFor(() => !_client.Pets.Busy, 20, "GM pet grant timed out");
            }
            if (_failure == null && _client.Pets.Pets.ActivePetId == 0 && _client.Pets.Pets.Pets.Count > 0)
            {
                _client.Pets.Summon(_client.Pets.Pets.Pets[0].PetId);
                yield return WaitFor(() => !_client.Pets.Busy, 20, "Pet summon timed out");
            }
            if (_failure != null) yield break;
            bool currencyDone = false;
            SceneCurrencyClientPlayerRpc.GmAddCurrency(_client,
                new GmAddCurrencyRequest { CurrencyType = 0, Amount = 100000 }, (status, response) =>
                {
                    currencyDone = true;
                    if (!status.IsOk || response == null || response.ErrorMessage?.Id > 0)
                        _failure = "QA GM currency failed: " + (response?.ErrorMessage?.Id.ToString() ?? status.Code.ToString());
                });
            yield return WaitFor(() => currencyDone, 20, "GM currency request timed out");
            if (_failure != null) yield break;
            yield return Learn(SkillBookContext.Character(), _plan.characterSkills);
            if (_failure == null && _client.Pets.Pets.Pets.Count > 0)
            {
                var pet = _client.Pets.Pets.Pets[0];
                yield return Learn(SkillBookContext.Pet(pet.PetId, pet.Name), _plan.petSkills);
            }
        }

        private IEnumerator ReturnToScene(string stage)
        {
            // 失败重跑可能正常恢复上一局或补弹战果；通过正式结果按钮确认，不把弹层下的页面算作截图。
            float deadline = Time.realtimeSinceStartup + 90, quietSince = -1;
            int resultCount = 0;
            bool autoRequested = false;
            while (_failure == null && Time.realtimeSinceStartup < deadline)
            {
                var results = Buttons().Where(button => Hierarchy(button.transform).EndsWith("/BattleResultPanel/ConfirmPlate", StringComparison.Ordinal)).ToArray();
                if (results.Length > 1) { _failure = "Multiple battle result panels are active"; yield break; }
                if (results.Length == 1 && results[0].IsInteractable())
                {
                    yield return Capture("00-" + stage + "-battle-result-" + resultCount++, "battle-result");
                    _report.actions.Add("Confirmed the production battle result via its visible return button");
                    Click(results[0]);
                }
                if (_client.Battle.HasActiveBattle && !autoRequested)
                {
                    _client.Battle.SetAutoBattle(true);
                    autoRequested = true;
                }
                bool busy = _client.Battle.HasActiveBattle || BattleUiRoot.Instance?.IsBattleLayerVisible == true ||
                    results.Length != 0 || _client.BattleResults.QueryInFlight;
                if (busy) quietSince = -1;
                else if (quietSince < 0) quietSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - quietSince >= 2)
                {
                    _client.Battle.SetAutoBattle(false);
                    yield break;
                }
                yield return null;
            }
            if (_failure == null) _failure = "The production battle result did not return to the normal scene";
        }

        private IEnumerator Learn(SkillBookContext context, uint[] ids)
        {
            _client.Skills.Refresh(context);
            yield return WaitFor(() => !_client.Skills.State.IsLoading, 20, "Skill catalog timed out");
            foreach (uint id in ids ?? Array.Empty<uint>())
            {
                var entry = _client.Skills.State.Snapshot?.Find(id);
                if (entry == null) { _failure = "The live server has no eligible skill " + id + " for " + context.DisplayName; yield break; }
                if (entry.Rank > 0) continue;
                _client.Skills.Upgrade(context, id, 0, 1);
                yield return WaitFor(() => !_client.Skills.State.IsBusy, 20, "Skill upgrade timed out");
                if ((_client.Skills.State.Snapshot?.Find(id)?.Rank ?? 0) == 0)
                { _failure = "Live skill learning failed: " + id + " " + _client.Skills.State.Error + " " + _client.Skills.State.Message; yield break; }
                yield return new WaitForSecondsRealtime(0.85f);
            }
        }

        private void Execute(Step step)
        {
            switch (step.operation)
            {
                case "resolution": SetResolution(step.width, step.height); break;
                case "open-character": AttributeUiRoot.Instance.OpenPanel(); break;
                case "open-pet": OpenPetPage(step.target); break;
                case "close-panels":
                    AttributeUiRoot.Instance?.HidePanel(); PetUiRoot.Instance?.HidePanel(); SkillUiRoot.Instance?.HidePanel(); break;
                case "click":
                    var button = FindButton(step.target);
                    Click(button);
                    break;
                case "click-battle-unit":
                    Click(FindBattleUnitButton(step.target));
                    break;
                case "focus": EventSystem.current.SetSelectedGameObject(FindButton(step.target).gameObject); break;
                case "assert-interactable":
                    if (FindButton(step.target).IsInteractable() != step.expected)
                        throw new InvalidOperationException("Button enabled state differs: " + step.target);
                    break;
                case "assert-text":
                    if (!VisibleText().Any(text => text.Contains(step.text ?? "")))
                        throw new InvalidOperationException("Expected visible text was not found: " + step.text);
                    break;
                case "snapshot": WriteAuthoritySnapshot(step.target); break;
                case "remember-pet-allocation":
                    _allocationBefore = _client.Pets.Pets.Pets.FirstOrDefault()?.Clone() ??
                        throw new InvalidOperationException("No live pet is available for save verification");
                    break;
                case "assert-pet-saved-one-point":
                    var saved = _client.Pets.Pets.Pets.FirstOrDefault(p => p.PetId == _allocationBefore?.PetId);
                    if (saved == null || saved.RemainingPoints + 1 != _allocationBefore.RemainingPoints ||
                        saved.Dimensions.Sum(d => (long)d.Allocated) != _allocationBefore.Dimensions.Sum(d => (long)d.Allocated) + 1)
                        throw new InvalidOperationException("Authoritative pet snapshot did not confirm exactly one saved allocation point");
                    break;
                case "refresh-pets": _client.Pets.RequestList(); break;
                case "start-pve": BattleUiRoot.Instance.MatchEntry.StartPve(); break;
                case "auto-battle": _client.Battle.SetAutoBattle(step.expected); break;
                case "assert-battle":
                    if (!_client.Battle.HasActiveBattle || BattleUiRoot.Instance?.IsBattleLayerVisible != true)
                        throw new InvalidOperationException("The expected live battle is not active");
                    break;
                case "capture": case "wait": case "wait-idle": case "wait-battle": case "wait-battle-end": case "return-to-scene": break;
                default: throw new InvalidOperationException("Unknown QA operation: " + step.operation);
            }
        }

        private static void OpenPetPage(string page)
        {
            var root = PetUiRoot.Instance ?? throw new InvalidOperationException("Pet UI is absent");
            root.OpenPage((PetCultivationPage)Enum.Parse(typeof(PetCultivationPage), page));
        }

        private IEnumerator Capture(string name, string operation)
        {
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { _failure = "Invalid capture name"; yield break; }
            Texture2D frame = null;
            try
            {
                if (Screen.width != _requestedWidth || Screen.height != _requestedHeight)
                    throw new InvalidOperationException("Actual render resolution differs: " + Screen.width + "x" + Screen.height + "; expected " + _requestedWidth + "x" + _requestedHeight);
                frame = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] bytes = frame.EncodeToPNG();
                string filename = name + "-" + frame.width + "x" + frame.height + ".png";
                string path = Path.Combine(_plan.outputDirectory, filename);
                if (File.Exists(path)) throw new IOException("Refusing to overwrite capture: " + path);
                File.WriteAllBytes(path, bytes);
                using var sha = SHA256.Create();
                var evidence = new Evidence { file = filename, sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                    utc = DateTime.UtcNow.ToString("O"), width = frame.width, height = frame.height, frame = Time.frameCount,
                    scene = SceneManager.GetActiveScene().path, operation = operation, playerId = _client?.PlayerId ?? 0,
                    inGame = _client?.InGame == true, gateReady = _client?.IsGateReady == true,
                    battlePhase = _client?.Battle?.Phase.ToString(), buttons = Buttons().Select(Describe).ToArray(),
                    visibleText = VisibleText().ToArray() };
                _report.screenshots.Add(evidence);
                File.WriteAllText(path + ".json", JsonUtility.ToJson(evidence, true));
                Debug.Log("[CharacterPetRuntimeVerification] captured " + filename);
            }
            catch (Exception error) { _failure = (_failure == null ? "" : _failure + "\nCapture evidence error: ") + error; }
            finally { if (frame != null) Destroy(frame); }
        }

        private void SetResolution(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            _requestedWidth = width; _requestedHeight = height;
#if UNITY_EDITOR
            if (Application.isEditor) { SetEditorGameViewResolution(width, height); return; }
#endif
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }

#if UNITY_EDITOR
        // 固定正式 Game View 的实际渲染尺寸；不替换相机、不构造离线画布。
        public static void SetEditorGameViewResolution(int width, int height)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var singleton = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
            object sizes = singleton.GetProperty("instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType", true);
            object group = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            var groupClass = group.GetType();
            int count = (int)groupClass.GetMethod("GetTotalCount", flags).Invoke(group, null);
            var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            int selected = -1;
            for (int i = 0; i < count; i++)
            {
                object size = groupClass.GetMethod("GetGameViewSize", flags).Invoke(group, new object[] { i });
                if ((int)sizeType.GetProperty("width", flags).GetValue(size) == width && (int)sizeType.GetProperty("height", flags).GetValue(size) == height)
                { selected = i; break; }
            }
            if (selected < 0)
            {
                var modeType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                object size = Activator.CreateInstance(sizeType, flags, null,
                    new[] { Enum.Parse(modeType, "FixedResolution"), (object)width, height, "CharacterPetQA_" + width + "x" + height }, null);
                groupClass.GetMethod("AddCustomSize", flags).Invoke(group, new[] { size });
                selected = count;
            }
            var viewType = assembly.GetType("UnityEditor.GameView", true);
            var view = UnityEditor.EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex", flags).SetValue(view, selected);
            view.Focus(); view.Repaint();
        }
#endif

        private static IEnumerable<Button> Buttons() => FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(button => button.gameObject.activeInHierarchy);
        private static IEnumerable<TMP_Text> Texts() => FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(text => text.gameObject.activeInHierarchy && text.enabled);
        private static IEnumerable<string> VisibleText() => Texts().Select(text => text.text)
            .Concat(FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(text => text.gameObject.activeInHierarchy && text.enabled).Select(text => text.text))
            .Where(text => !string.IsNullOrWhiteSpace(text));
        private Button FindBattleUnitButton(string role)
        {
            var actors = _client?.Battle?.State?.Actors ?? throw new InvalidOperationException("No live battle state");
            var myActor = actors.FirstOrDefault(a => a.ActorId == _client.PlayerId);
            var target = role switch
            {
                "self" => myActor,
                "pet" => actors.FirstOrDefault(a => a.ActorType == eBattleActorType.BattleActorTypePet && a.OwnerPlayerId == _client.PlayerId && !a.IsDead && !a.Fled),
                "enemy" => actors.FirstOrDefault(a => myActor != null && a.TeamIndex != myActor.TeamIndex && !a.IsDead && !a.Fled),
                _ => throw new InvalidOperationException("Unknown battle unit role: " + role),
            } ?? throw new InvalidOperationException("No live battle unit found for role: " + role);
            return FindButton("Unit_" + target.ActorId + "/Hit");
        }
        private static Button FindButton(string target)
        {
            var matches = Buttons().Where(button => button.name == target || Hierarchy(button.transform).EndsWith("/" + target, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one active button: " + target + "; found " + matches.Length);
            return matches[0];
        }
        private static void Click(Button button)
        {
            if (!button.IsInteractable()) throw new InvalidOperationException("Requested button is disabled: " + button.name);
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button)
                throw new InvalidOperationException("Requested button is covered by another UI element: " + Hierarchy(button.transform));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private static string Hierarchy(Transform transform) => transform.parent == null ? transform.name : Hierarchy(transform.parent) + "/" + transform.name;
        private static Control Describe(Button button)
        {
            var corners = new Vector3[4];
            var rect = button.transform as RectTransform;
            rect.GetWorldCorners(corners);
            var image = button.targetGraphic as Image;
            var sprite = image != null ? image.sprite : null;
            return new Control { path = Hierarchy(button.transform), text = button.GetComponentInChildren<TMP_Text>()?.text ?? button.GetComponentInChildren<Text>()?.text,
                interactable = button.IsInteractable(), x = corners[0].x, y = corners[0].y,
                width = corners[2].x - corners[0].x, height = corners[2].y - corners[0].y,
                alpha = image == null ? 0 : image.color.a, sprite = sprite != null ? sprite.name : null };
        }

        private IEnumerator WaitFor(Func<bool> predicate, float seconds, string failure)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!predicate() && _failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!predicate() && _failure == null) _failure = failure;
        }
        private bool StopIfFailed() { if (_failure == null) return false; Finish("failed", _failure); return true; }
        private void WriteAuthoritySnapshot(string label)
        {
            if (string.IsNullOrWhiteSpace(label) || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
            if (_client?.Attributes.Panel != null) File.WriteAllText(Path.Combine(_plan.outputDirectory, label + "-attributes.json"), _client.Attributes.Panel.ToString());
            if (_client?.Pets.Pets != null) File.WriteAllText(Path.Combine(_plan.outputDirectory, label + "-pets.json"), _client.Pets.Pets.ToString());
        }
        private void OnClientError(string error) { _failure = error; }
        private void OnLog(string condition, string stack, LogType type)
        {
            if (_report != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                _report.runtimeErrors.Add(condition + "\n" + stack);
        }
        private void Update()
        {
            if (!_finished && _report != null && Time.realtimeSinceStartup - _startedAt > 360)
                Finish("failed", "Runtime verification watchdog expired");
        }
        private void Finish(string result, string error)
        {
            if (_finished) return;
            _finished = true;
            _report.result = result; _report.error = error; _report.finishedUtc = DateTime.UtcNow.ToString("O");
            WriteReport();
            Application.logMessageReceived -= OnLog;
            Debug.Log("[CharacterPetRuntimeVerification] " + result + (error == null ? "" : ": " + error));
#if UNITY_EDITOR
            if (Application.isEditor) { UnityEditor.EditorApplication.isPlaying = false; return; }
#endif
            if (!Application.isEditor) Application.Quit(error == null ? 0 : 1);
        }
        private void WriteReport() => File.WriteAllText(Path.Combine(_plan.outputDirectory, "runtime-report.json"), JsonUtility.ToJson(_report, true));
        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
#endif
