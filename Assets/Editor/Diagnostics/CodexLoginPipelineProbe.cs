using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using MmorpgClient.Game;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Role;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.EditorDev
{
    // 临时、仅 Editor 使用的真实登录 UI 诊断。没有 Library 开关时完全不注册更新。
    // 口令只在内存中读取，不进入状态文件、结果、日志或异常文本。
    // 首份快照发生在诊断源码导入/域重载之后；beforeActions 仅指本工具的 Play 操作之前，
    // 不能将该时刻的运行时对象缺失直接解释成用户原先卡住的原因。
    [InitializeOnLoad]
    internal static class CodexLoginPipelineProbe
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string AllowedOutputRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CodexLoginProbe"));
        private const string AllowedEnvironment = "D:/luyuan/xuanming-server-mmo-cpp/build/docker-debug-current/.env";
        private const string PasswordKey = "LOGIN_DEV_PASSWORD_SHARED_SECRET=";
        private static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
        private static readonly string ControlDirectory = Path.Combine(ProjectRoot, "Library/CodexLoginProbe");
        private static readonly string RequestPath = Path.Combine(ControlDirectory, "request.json");
        private static readonly string StatePath = Path.Combine(ControlDirectory, "state.json");
        private static Request _request;
        private static State _state;
        private static bool _finished;
        private static double _nextPoll;
        private static DateTime _expiresAtUtc;

        [Serializable]
        private sealed class Request
        {
            public string runId;
            public string outputDirectory;
            public string environmentFile;
            public string expiresUtc;
        }

        [Serializable]
        private sealed class State
        {
            public string runId;
            public string stage;
            public int editorProcessId;
            public double startedAt;
            public double stageStartedAt;
            public bool initialRecorded;
            public bool wrongPasswordPassed;
            public bool roleSubmitted;
            public bool journalCreated;
            public string outputDirectory;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public string stage;
            public string eventKind;
            public string utc;
            public double elapsedSeconds;
            public bool initialBeforeActions;
            public bool initialAfterProbeLoad;
            public bool isPlaying;
            public bool isPaused;
            public bool isCompiling;
            public bool isUpdating;
            public bool applicationFocused;
            public bool runInBackground;
            public int frameCount;
            public bool viewPresent;
            public bool appPresent;
            public bool gatewayPresent;
            public bool sessionPresent;
            public bool clientPresent;
            public bool busy;
            public bool serverListLoading;
            public bool serverListFailed;
            public int serverCount;
            public int enterRunGeneration;
            public int serverListGeneration;
            public int pipelineGeneration;
            public string flow;
            public bool inGame;
            public bool playerPresent;
            public bool scenePresent;
            public bool wrongPasswordPassed;
            public bool success;
            public string exceptionType;
        }

        static CodexLoginPipelineProbe()
        {
            if (!File.Exists(RequestPath)) return;
            try
            {
                _request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
                ValidateRequest();
                // 未及时导入的旧开关不得在以后无关的编辑会话中自动操作 Play。
                if (DateTime.UtcNow >= _expiresAtUtc)
                {
                    File.Delete(RequestPath);
                    return;
                }
                if (File.Exists(StatePath))
                {
                    var previous = JsonUtility.FromJson<State>(File.ReadAllText(StatePath));
                    if (previous != null && previous.runId == _request.runId) _state = previous;
                }
                if (_state == null)
                {
                    Directory.CreateDirectory(_request.outputDirectory);
                    // 不覆盖另一轮证据。后续只追加本轮独占创建的 journal。
                    using (new FileStream(JournalPath, FileMode.CreateNew, FileAccess.Write)) { }
                    if (File.Exists(ResultPath)) throw new IOException();
                    _state = new State
                    {
                        runId = _request.runId, stage = "initial", journalCreated = true,
                        editorProcessId = Process.GetCurrentProcess().Id,
                        startedAt = EditorApplication.timeSinceStartup,
                        stageStartedAt = EditorApplication.timeSinceStartup,
                        outputDirectory = _request.outputDirectory
                    };
                    SaveState();
                }
                if (!string.Equals(_state.outputDirectory, _request.outputDirectory, StringComparison.OrdinalIgnoreCase)
                    || !_state.journalCreated || !File.Exists(JournalPath)) throw new InvalidDataException();
                EditorApplication.update += Tick;
            }
            catch (Exception ex)
            {
                // 请求或目标路径不可信时禁止向其写任何文件；仅留下固定类型的本地诊断。
                UnityEngine.Debug.LogWarning("[CodexLoginProbe] initialization_failed: " + ex.GetType().Name);
            }
        }

        private static string JournalPath => Path.Combine(_request.outputDirectory, "journal.jsonl");
        private static string ResultPath => Path.Combine(_request.outputDirectory, "result.json");

        private static void ValidateRequest()
        {
            if (_request == null || !Guid.TryParse(_request.runId, out _)) throw new InvalidDataException();
            if (!DateTime.TryParse(_request.expiresUtc, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out _expiresAtUtc))
                throw new InvalidDataException();
            _expiresAtUtc = _expiresAtUtc.ToUniversalTime();
            var root = Path.GetFullPath(AllowedOutputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var output = Path.GetFullPath(_request.outputDirectory ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException();
            // 不接受目录联接/符号链接把受控输出转到仓库外。
            for (var directory = new DirectoryInfo(output); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException();
            if (!string.Equals(Path.GetFullPath(_request.environmentFile ?? string.Empty),
                    Path.GetFullPath(AllowedEnvironment), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException();
            _request.outputDirectory = output;
        }

        private static void Tick()
        {
            if (_finished || EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.1;
            try
            {
                if (!OwnRequestExists()) { StopUpdates(); return; }
                if (DateTime.UtcNow >= _expiresAtUtc) { Finish(false, "request_expired"); return; }
                if (!_state.initialRecorded)
                {
                    AppendSnapshot("initial_before_actions", true);
                    _state.initialRecorded = true;
                    SaveState();
                }
                if (_state.editorProcessId != Process.GetCurrentProcess().Id
                    || EditorApplication.timeSinceStartup < _state.startedAt)
                { Finish(false, "editor_restarted"); return; }
                if (EditorApplication.isPaused) { Finish(false, "editor_paused"); return; }
                if (Elapsed >= 90) { Finish(false, EditorApplication.isCompiling ? "editor_compiling_timeout" : EditorApplication.isUpdating ? "editor_updating_timeout" : "total_timeout"); return; }
                if (EditorApplication.isCompiling) return;
                if (EditorApplication.isUpdating) return;

                switch (_state.stage)
                {
                    case "finished": StopUpdates(); RemoveOwnRequest(); return;
                    case "initial":
                        if (File.Exists(Path.Combine(ProjectRoot, "auto_enter_game.local.json")))
                        { Finish(false, "competing_automation"); return; }
                        // 动作前持久化阶段；domain reload 不会第二次发出停止或进入请求。
                        if (EditorApplication.isPlayingOrWillChangePlaymode)
                        {
                            SetStage("stopping_play");
                            EditorApplication.ExitPlaymode();
                        }
                        else
                        {
                            SetStage("starting_play");
                            EditorApplication.EnterPlaymode();
                        }
                        return;
                    case "stopping_play":
                        if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        {
                            SetStage("starting_play");
                            EditorApplication.EnterPlaymode();
                            return;
                        }
                        if (StageElapsed >= 15) { Finish(false, "stop_play_timeout"); return; }
                        return;
                    case "starting_play":
                        if (EditorApplication.isPlaying)
                        {
                            SetStage("waiting_ui");
                            return;
                        }
                        if (StageElapsed >= 20) { Finish(false, "start_play_timeout"); return; }
                        return;
                    case "waiting_ui":
                        if (StageElapsed >= 25) { Finish(false, "ui_or_server_list_timeout"); return; }
                        if (!TryReadyView(out var view)) return;
                        SetStage("wrong_submitted");
                        Submit(view, Guid.NewGuid().ToString("N") + "-invalid");
                        AppendSnapshot("wrong_submission_returned");
                        return;
                    case "wrong_submitted":
                        view = FindLiveView();
                        if (!RequireLiveClient(view)) return;
                        if (!Field<bool>(view, "_busy") && StatusClass(view) == "auth_rejected")
                        {
                            _state.wrongPasswordPassed = true;
                            AppendSnapshot("wrong_password_passed");
                            SetStage("correct_ready");
                        }
                        else if (StageElapsed >= 8) Finish(false, "wrong_password_timeout_or_status");
                        return;
                    case "correct_ready":
                        if (!TryReadyView(out view))
                        { Finish(false, "correct_ui_not_ready"); return; }
                        var password = ReadPassword();
                        SetStage("correct_submitted");
                        Submit(view, password);
                        AppendSnapshot("correct_submission_returned");
                        return;
                    case "correct_submitted":
                        view = FindLiveView();
                        if (!RequireLiveClient(view)) return;
                        var app = Field<AppBootstrap>(view, "_app");
                        if (app.GameClient.InGame && app.GameClient.PlayerId != 0 && app.GameClient.CurrentSceneId != 0)
                        { Finish(true, "entered_game"); return; }
                        if (StageElapsed >= 45) { Finish(false, "correct_login_timeout"); return; }
                        if (!_state.roleSubmitted) ResolveRoleOnce();
                        if (StageElapsed > 1 && !Field<bool>(view, "_busy"))
                            Finish(false, "correct_flow_ended_before_game");
                        return;
                    default: Finish(false, "invalid_stage"); return;
                }
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : ex;
                Finish(false, "probe_exception", cause.GetType().Name);
            }
        }

        private static double Elapsed => EditorApplication.timeSinceStartup - _state.startedAt;
        private static double StageElapsed => EditorApplication.timeSinceStartup - _state.stageStartedAt;

        private static QdaoServerSelectView FindLiveView()
        {
            var views = UnityEngine.Object.FindObjectsByType<QdaoServerSelectView>(
#if UNITY_6000_6_OR_NEWER
                FindObjectsInactive.Include);
#else
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            foreach (var view in views)
                if (Field<AppBootstrap>(view, "_app") != null) return view;
            return null;
        }

        private static bool TryReadyView(out QdaoServerSelectView view)
        {
            view = FindLiveView();
            if (!EditorApplication.isPlaying || view == null || !view.isActiveAndEnabled) return false;
            var app = Field<AppBootstrap>(view, "_app");
            if (app == null || app.Gateway == null || app.Session == null || app.GameClient == null) return false;
            if (Field<bool>(view, "_busy") || Field<bool>(view, "_serverListLoading")) return false;
            if (Field<bool>(view, "_serverListFailed")) { Finish(false, "server_list_failed"); return false; }
            var zone = Field<uint>(view, "_selectedZoneId");
            return app.Session.Zones.Count > 0 && Field<int>(view, "_serverListRequestGen") > 0 && zone != 0
                && Invoke(view, "FindZone", zone) != null && Invoke(view, "FindRenderedZone", zone) != null;
        }

        private static bool RequireLiveClient(QdaoServerSelectView view)
        {
            var app = view != null ? Field<AppBootstrap>(view, "_app") : null;
            if (EditorApplication.isPlaying && app != null && app.Gateway != null && app.Session != null && app.GameClient != null)
                return true;
            Finish(false, "play_or_runtime_state_lost");
            return false;
        }

        private static void Submit(QdaoServerSelectView view, string password)
        {
            Field<TMP_InputField>(view, "_accountInput").SetTextWithoutNotify("dev_demo");
            Field<TMP_InputField>(view, "_passwordInput").SetTextWithoutNotify(password);
            Invoke(view, "OnEnterClicked");
        }

        private static string ReadPassword()
        {
            string password = null;
            foreach (var line in File.ReadLines(_request.environmentFile))
            {
                if (!line.StartsWith(PasswordKey, StringComparison.Ordinal)) continue;
                if (password != null) throw new InvalidDataException();
                password = line.Substring(PasswordKey.Length).Trim();
            }
            // 本地生成器的密钥契约为 64 位十六进制；不接受 shell 展开或其他环境值。
            if (password == null || password.Length != 64) throw new InvalidDataException();
            foreach (var c in password)
                if (!Uri.IsHexDigit(c)) throw new InvalidDataException();
            return password;
        }

        private static void ResolveRoleOnce()
        {
            var role = RoleFlowUi.Instance;
            if (role == null) return;
            var canvas = Field<GameObject>(role, "_canvasGo");
            if (canvas == null || !canvas.activeSelf || Field<bool>(role, "_resolved")
                || Field<GameClient.PlayerChoice>(role, "_result") == null) return;
            object first = null;
            var players = Field<object>(role, "_players") as IEnumerable;
            if (players != null)
                foreach (var player in players) { first = player; break; }
            _state.roleSubmitted = true;
            SaveState();
            if (first == null) Invoke(role, "ResolveCreate");
            else
            {
                var playerId = Convert.ToUInt64(first.GetType().GetProperty("PlayerId").GetValue(first));
                if (playerId == 0) throw new InvalidDataException();
                Invoke(role, "ResolveSelect", playerId);
            }
            AppendSnapshot(first == null ? "role_create_submitted" : "role_select_submitted");
        }

        private static T Field<T>(object target, string name)
        {
            return (T)(target.GetType().GetField(name, PrivateInstance)
                ?? throw new MissingFieldException()).GetValue(target);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            return (target.GetType().GetMethod(name, PrivateInstance)
                ?? throw new MissingMethodException()).Invoke(target, arguments);
        }

        private static string StatusClass(QdaoServerSelectView view)
        {
            var label = Field<TMP_Text>(view, "_statusText");
            var status = label != null ? label.text : null;
            switch (status)
            {
                case null: case "": return "empty";
                case "账号或密码错误": return "auth_rejected";
                case "正在登录账号…": return "authenticating";
                case "正在分配服务器…": return "assigning_gate";
                case "正在连接服务器…": return "connecting_gate";
                case "正在验证会话…": return "verifying_session";
                case "正在创建角色…": return "creating_role";
                case "正在进入游戏…": return "entering_game";
                case "进入游戏成功": return "entered_game";
                default: return "other";
            }
        }

        private static Snapshot Capture(string eventKind, bool initial = false)
        {
            var view = FindLiveView();
            var app = view != null ? Field<AppBootstrap>(view, "_app") : AppBootstrap.Instance;
            var client = app != null ? app.GameClient : null;
            return new Snapshot
            {
                stage = _state.stage, eventKind = eventKind, utc = DateTime.UtcNow.ToString("O"),
                elapsedSeconds = Math.Round(Elapsed, 3), initialBeforeActions = initial,
                initialAfterProbeLoad = initial,
                isPlaying = EditorApplication.isPlaying, isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling, isUpdating = EditorApplication.isUpdating,
                applicationFocused = Application.isFocused, runInBackground = Application.runInBackground,
                frameCount = Time.frameCount,
                viewPresent = view != null, appPresent = app != null,
                gatewayPresent = app != null && app.Gateway != null,
                sessionPresent = app != null && app.Session != null, clientPresent = client != null,
                busy = view != null && Field<bool>(view, "_busy"),
                serverListLoading = view != null && Field<bool>(view, "_serverListLoading"),
                serverListFailed = view != null && Field<bool>(view, "_serverListFailed"),
                serverCount = app != null && app.Session != null ? app.Session.Zones.Count : 0,
                enterRunGeneration = view != null ? Field<int>(view, "_enterRunId") : 0,
                serverListGeneration = view != null ? Field<int>(view, "_serverListRequestGen") : 0,
                pipelineGeneration = client != null ? Field<int>(client, "_pipelineGen") : 0,
                flow = view != null ? StatusClass(view) : "view_missing",
                inGame = client != null && client.InGame,
                playerPresent = client != null && client.PlayerId != 0,
                scenePresent = client != null && client.CurrentSceneId != 0,
                wrongPasswordPassed = _state.wrongPasswordPassed
            };
        }

        private static void AppendSnapshot(string eventKind, bool initial = false)
        {
            File.AppendAllText(JournalPath, JsonUtility.ToJson(Capture(eventKind, initial)) + "\n", new UTF8Encoding(false));
        }

        private static void SetStage(string stage)
        {
            _state.stage = stage;
            _state.stageStartedAt = EditorApplication.timeSinceStartup;
            SaveState();
            AppendSnapshot("stage_changed");
        }

        private static void SaveState()
        {
            var temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(_state), new UTF8Encoding(false));
            if (File.Exists(StatePath)) File.Replace(temporary, StatePath, null);
            else File.Move(temporary, StatePath);
        }

        private static bool OwnRequestExists()
        {
            if (!File.Exists(RequestPath)) return false;
            var current = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            return current != null && current.runId == _request.runId;
        }

        private static void RemoveOwnRequest()
        {
            if (OwnRequestExists()) File.Delete(RequestPath);
        }

        private static void StopUpdates()
        {
            _finished = true;
            EditorApplication.update -= Tick;
        }

        private static void Finish(bool success, string classification, string exceptionType = null)
        {
            if (_finished) return;
            StopUpdates();
            try
            {
                var result = Capture(classification);
                result.success = success;
                result.exceptionType = exceptionType;
                using (var writer = new StreamWriter(new FileStream(ResultPath, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(result, true));
                AppendSnapshot("finished");
                _state.stage = "finished";
                SaveState();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[CodexLoginProbe] result_write_failed: " + ex.GetType().Name);
            }
            finally
            {
                try { RemoveOwnRequest(); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning("[CodexLoginProbe] cleanup_failed: " + ex.GetType().Name); }
            }
        }
    }
}

// 本轮受控登录诊断导入：20261021T021113.064469Z
