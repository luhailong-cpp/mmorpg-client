#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.World;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MmorpgClient.App
{
    /// <summary>
    /// 创角页测速驱动，只编进编辑器和开发版。不登录：把假角色列表直接交给真实的
    /// <see cref="GameClient.PlayerChooser"/>（即 RoleFlowUi.Choose），再调用真实按钮，记录点击回调耗时、
    /// 逐帧主线程耗时、创角埋点（<see cref="CreationPortraitProfiling"/>）、窗口持有数和纹理内存。
    /// 结束时点“返回”取消，不会创建角色。驱动只报告数字和不变量，不判断性能是否达标。
    ///
    /// 启动：命令行 -roleCreatePerf &lt;输出目录&gt;，或环境变量 MMORPG_ROLE_CREATE_PERF=&lt;输出目录&gt;（编辑器里用，测完要清掉）。
    /// 可选：-roleCreatePerfRoles 1:1,3:2  账号已有角色，职业:性别[:外观id]，逗号分隔，最多 4 个；none = 没有角色，直接进创角页
    ///       -roleCreatePerfCycles 50      退出再进创角页的次数（没有角色时不做）
    ///       -roleCreatePerfQuit           结束后退出：播放器退出码 0 = 不变量全部成立、1 = 有失败；编辑器停止播放
    /// 每次运行在输出目录下新建 run-&lt;UTC 时间&gt;-&lt;进程号&gt;/，写 samples.tsv、frames.tsv、summary.json。
    /// 没有渲染帧时（-nographics）“到帧结束”一列留空。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevRoleCreatePerfProbe : MonoBehaviour
    {
        public const string OutputArgument = "-roleCreatePerf";
        public const string OutputEnvironmentVariable = "MMORPG_ROLE_CREATE_PERF";
        private const string RolesArgument = "-roleCreatePerfRoles";
        private const string CyclesArgument = "-roleCreatePerfCycles";
        private const string QuitArgument = "-roleCreatePerfQuit";
        private const string DefaultRoles = "1:1,3:2";
        private const string PortraitSuffix = "_portrait";
        private const string ProfessionDefaultLabel = "职业默认";
        private const uint ZoneId = 1;
        private const int MaxRoles = 4;
        private const int BootFrameLimit = 600;
        private const int ExitFrameLimit = 120;
        private const int BaselineFrames = 60;
        private const int SettleFrames = 6;
        private const int MaxWindowPortraits = 3;

        private static readonly string[] MarkerNames =
        {
            CreationPortraitProfiling.OpenName, CreationPortraitProfiling.RefreshChoicesName,
            CreationPortraitProfiling.RefreshSelectionName, CreationPortraitProfiling.ResolveName,
            CreationPortraitProfiling.LoadTextureName, CreationPortraitProfiling.PreloadName,
            CreationPortraitProfiling.ReleaseName,
        };

        private static readonly string[] RequiredButtons =
        {
            "NextAppearance", "PreviousAppearance", "BackFromCreate", "BackToServers", "ConfirmCreate",
            "Class_1", "Class_2", "Class_3", "Class_4", "GenderMale", "GenderFemale",
        };

        private sealed class Options
        {
            public string OutputRoot;
            public List<AccountSimplePlayer> Roles = new();
            public int Cycles = 50;
            public bool Quit;
            public string Error;

            public static Options Resolve(IReadOnlyList<string> args)
            {
                var output = ClientLaunchArguments.GetValue(args, OutputArgument);
                if (string.IsNullOrWhiteSpace(output)) output = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(output)) return null;
                var options = new Options { OutputRoot = output, Quit = ClientLaunchArguments.HasFlag(args, QuitArgument) };
                options.Error = ParseRoles(ClientLaunchArguments.GetValue(args, RolesArgument) ?? DefaultRoles, options.Roles);
                var cycles = ClientLaunchArguments.GetValue(args, CyclesArgument);
                if (cycles != null && (!int.TryParse(cycles, NumberStyles.Integer, CultureInfo.InvariantCulture, out options.Cycles) ||
                    options.Cycles < 0))
                    options.Error ??= $"invalid {CyclesArgument} value: {cycles}";
                return options;
            }

            private static string ParseRoles(string spec, List<AccountSimplePlayer> roles)
            {
                if (string.Equals(spec.Trim(), "none", StringComparison.OrdinalIgnoreCase)) return null;
                foreach (var item in spec.Split(','))
                {
                    var parts = item.Trim().Split(':');
                    if (parts.Length < 2 || parts.Length > 3 ||
                        !uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var classId) ||
                        classId < 1 || classId > 4 ||
                        !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gender) ||
                        gender < 1 || gender > 2)
                        return $"invalid {RolesArgument} item: {item}";
                    roles.Add(new AccountSimplePlayer
                    {
                        PlayerId = 10001UL + (ulong)roles.Count, ClassId = classId, Gender = gender,
                        AppearanceId = parts.Length == 3 ? parts[2].Trim() : string.Empty,
                        Name = "测速角色" + (roles.Count + 1),
                    });
                }
                // 角色位满了“创建角色”按钮会隐藏，测不了。
                return roles.Count > MaxRoles ? $"{RolesArgument} allows at most {MaxRoles} roles" : null;
            }
        }

        /// <summary>一次点击或一个检查点。耗时用 NaN 表示没测。</summary>
        private sealed class SampleRow
        {
            public string Stage;
            public int Index;
            public int Frame;
            public double CallbackMs = double.NaN;
            public double EndOfFrameMs = double.NaN;
            public string HeroBefore;
            public string HeroAfter;
            public bool Hit;
            public int WindowCount;
            public string WindowIds;
            public bool ConfirmEnabled;
            public int ResidentPortraits = -1;
            public long TextureMemoryBytes = -1;
        }

        /// <summary>一帧的主线程耗时和各埋点耗时（毫秒），以及采样时创角窗口持有的头像数。</summary>
        private sealed class FrameRow
        {
            public int Frame;
            public string Phase;
            public double MainThreadMs;
            public double[] MarkerMs;
            public long TextureMemoryBytes;
            public int WindowCount;
        }

        [Serializable] private sealed class Stat { public string name; public int count; public double p50, p95, max; }

        [Serializable] private sealed class Summary
        {
            public bool passed;
            public string[] failures;
            public string utc, outputDirectory, unityVersion, platform, deviceModel, processorType, graphicsDeviceName, graphicsDeviceType;
            public bool isEditor, isDevelopmentBuild, endOfFrameMeasured;
            public int processorCount, systemMemoryMb, graphicsMemoryMb, cycles, frameCount;
            public string[] roles, creatableIdentities;
            public Stat[] stats;
        }

        private Options _options;
        private RoleFlowUi _ui;
        private Image _hero;
        private TMP_Text _appearanceLabel;
        private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousButtons = new(StringComparer.Ordinal);
        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _textureMemory;
        private readonly ProfilerRecorder[] _markers = new ProfilerRecorder[MarkerNames.Length];
        private readonly List<SampleRow> _samples = new();
        private readonly List<FrameRow> _frames = new();
        private readonly List<string> _failures = new();
        private string _phase = "boot";
        private bool _canWaitForEndOfFrame;
        private bool _chooserFinished;
        private bool _completed;
        private bool _finished;
        private bool _windowViolationReported;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var options = Options.Resolve(Environment.GetCommandLineArgs());
            if (options == null) return;
            var host = new GameObject("[RoleCreatePerfProbe]");
            DontDestroyOnLoad(host);
            host.AddComponent<DevRoleCreatePerfProbe>()._options = options;
        }

        private IEnumerator Start()
        {
            if (_options == null) yield break; // 只接受 Boot 按启动参数挂上的实例
            try
            {
                if (_options.Error != null) { Fail("options", _options.Error); yield break; }
                // AppBootstrap 与本驱动都在场景加载后启动，先后不定：等它挂好真实的选角回调。
                for (int frame = 0; frame < BootFrameLimit && !AppReady(); frame++) yield return null;
                if (!AppReady()) { Fail("boot", "AppBootstrap, GameClient.PlayerChooser or RoleFlowUi is not ready"); yield break; }
                if (DevAutoPilot.IsActive) { Fail("boot", "DevAutoPilot is active; run the probe without -zone"); yield break; }
                _ui = RoleFlowUi.Instance;
                // 正式流程里登录、选服要好几秒，候选预解析早已跑完；测速也从这个状态开始，不把它算进开页。
                for (int frame = 0; frame < BootFrameLimit && !_ui.CreationChoicesPrewarmed; frame++) yield return null;
                if (!_ui.CreationChoicesPrewarmed) { Fail("boot", "creation choice prewarm did not finish"); yield break; }
                if (!BindUi()) yield break;
                _canWaitForEndOfFrame = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
                StartRecorders();
                StartCoroutine(SampleFrames());
                yield return Guarded("run", Measure(), () => _completed = true);
            }
            finally { Finish(); }
        }

        /// <summary>
        /// 按 Unity 的规则逐层推进嵌套协程：IEnumerator 就地展开，其他让出值交给 Unity。任何一层抛出的异常都记成失败，
        /// 保证结果文件照常写出、带 -roleCreatePerfQuit 时进程照常退出；正常走完才调用 completed。
        /// </summary>
        private IEnumerator Guarded(string stage, IEnumerator routine, Action completed)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool advanced;
                Exception error = null;
                try { advanced = top.MoveNext(); }
                catch (Exception caught)
                {
                    error = caught;
                    advanced = false;
                }
                if (error != null)
                {
                    Fail(stage, $"{error.GetType().Name}: {error.Message}");
                    Debug.LogException(error);
                    yield break;
                }
                if (!advanced)
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return top.Current;
            }
            completed?.Invoke();
        }

        private void OnDestroy() => DisposeRecorders();

        private static bool AppReady()
        {
            var client = AppBootstrap.Instance != null ? AppBootstrap.Instance.GameClient : null;
            var ui = RoleFlowUi.Instance;
            return client?.PlayerChooser != null && ui != null && ReferenceEquals(client.PlayerChooser.Target, ui);
        }

        private IEnumerator Measure()
        {
            bool hasRoles = _options.Roles.Count > 0;
            var choice = new GameClient.PlayerChoice();
            var routine = AppBootstrap.Instance.GameClient.PlayerChooser(ZoneId, _options.Roles, choice);

            // 没有角色时 Choose 的第一步就是进创角页，这一步就是冷开页；有角色时第一步是选角页。
            _phase = hasRoles ? "select-open" : "open";
            var opened = new SampleRow { Stage = _phase, Frame = Time.frameCount, HeroBefore = null };
            var watch = Stopwatch.StartNew();
            bool running = routine.MoveNext();
            opened.CallbackMs = watch.Elapsed.TotalMilliseconds;
            FillState(opened);
            _samples.Add(opened);
            if (!running) { Fail(_phase, "the chooser finished on its first step"); yield break; }
            StartCoroutine(Guarded("chooser", ContinueChooser(routine), () => _chooserFinished = true));
            if (_canWaitForEndOfFrame)
            {
                yield return new WaitForEndOfFrame();
                opened.EndOfFrameMs = watch.Elapsed.TotalMilliseconds;
            }

            _phase = "settle";
            yield return Frames(30);
            _phase = "baseline";
            yield return Frames(BaselineFrames);
            Checkpoint("baseline", 0);

            if (hasRoles)
            {
                _phase = "open";
                yield return Click("open", 0, "CreateNewRole");
                if (Stopped) yield break;
                yield return Frames(SettleFrames);
            }

            // 整环往后、往前各走一圈（含职业默认槽）；每次点击后留几帧给邻居补载。
            int ring = QdaoCharacterCatalog.CreationOriginalAll.Count + 1;
            foreach (var (stage, button) in new[] { ("next", "NextAppearance"), ("previous", "PreviousAppearance") })
            {
                _phase = stage;
                for (int i = 1; i <= ring; i++)
                {
                    yield return Click(stage, i, button);
                    if (Stopped) yield break;
                    yield return Frames(SettleFrames);
                }
            }

            // 同一帧连点两次：目标通常还没预载，测现场同步加载。
            _phase = "rapid";
            yield return Click("rapid-next", 2, "NextAppearance", "NextAppearance");
            if (Stopped) yield break;
            yield return Frames(SettleFrames);
            yield return Click("rapid-previous", 2, "PreviousAppearance", "PreviousAppearance");
            if (Stopped) yield break;
            yield return Frames(SettleFrames);

            // 停在职业默认槽切职业和性别：当前人物跟着变，两侧邻居基本不变。
            _phase = "profession";
            for (int i = 0; i < ring && !AppearanceLabel().Contains(ProfessionDefaultLabel); i++)
            {
                yield return Click("to-default", i + 1, "PreviousAppearance");
                if (Stopped) yield break;
                yield return Frames(SettleFrames);
            }
            if (!AppearanceLabel().Contains(ProfessionDefaultLabel)) { Fail("profession", "profession-default slot was not reached"); yield break; }
            int professionIndex = 0;
            for (int classId = 1; classId <= 4; classId++)
                foreach (var button in new[] { "Class_" + classId, "GenderMale", "GenderFemale" })
                {
                    yield return Click("profession", ++professionIndex, button);
                    if (Stopped) yield break;
                    yield return Frames(SettleFrames);
                }
            // 回到破军男、第一位明确人物，后面的开关循环从常见状态开始。
            foreach (var button in new[] { "Class_1", "GenderMale", "NextAppearance" })
            {
                yield return Click("restore", 0, button);
                if (Stopped) yield break;
            }
            yield return Frames(SettleFrames);

            if (hasRoles)
            {
                for (int cycle = 1; cycle <= _options.Cycles; cycle++)
                {
                    _phase = "close";
                    yield return Click("close", cycle, "BackFromCreate");
                    if (Stopped) yield break;
                    yield return Frames(3);
                    Checkpoint("closed", cycle);
                    if (_ui.CachedCreationPortraitCount != 0)
                        Fail("close", $"cycle {cycle} kept {_ui.CachedCreationPortraitCount} creation portraits after leaving");
                    _phase = "reopen";
                    yield return Click("reopen", cycle, "CreateNewRole");
                    if (Stopped) yield break;
                    yield return Frames(SettleFrames);
                }
            }

            // 退出：有角色时先回选角页再返回选服，没有角色时“返回”直接取消；两条路都不提交创建。
            _phase = "exit";
            yield return Click("exit", 1, "BackFromCreate");
            if (Stopped) yield break;
            if (hasRoles)
            {
                yield return Click("exit", 2, "BackToServers");
                if (Stopped) yield break;
            }
            for (int frame = 0; frame < ExitFrameLimit && !_chooserFinished; frame++) yield return null;
            if (!_chooserFinished) Fail("exit", "the chooser did not finish after cancelling");
            if (choice.CreateNew || !choice.Cancelled) Fail("exit", "the probe must end by cancelling, never by creating");
            Checkpoint("exit", 0);
            if (_ui.CachedCreationPortraitCount != 0)
                Fail("exit", $"{_ui.CachedCreationPortraitCount} creation portraits are still held after the chooser finished");
        }

        private static IEnumerator ContinueChooser(IEnumerator routine)
        {
            // 第一步已在 Measure 里同步执行并计时；这里按 RoleFlowUi.Choose 自己的节奏继续推进。
            yield return routine.Current;
            while (routine.MoveNext()) yield return routine.Current;
        }

        private bool Stopped => _failures.Count > 0;

        private IEnumerator Click(string stage, int index, params string[] names)
        {
            var targets = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                targets[i] = UsableButton(names[i]);
                if (targets[i] == null) { Fail(stage, "button is missing, hidden or not interactable: " + names[i]); yield break; }
            }
            var row = new SampleRow { Stage = stage, Index = index, Frame = Time.frameCount, HeroBefore = HeroId() };
            var residentBefore = new HashSet<string>(_ui.CachedCreationPortraitIds, StringComparer.Ordinal);
            var watch = Stopwatch.StartNew();
            foreach (var button in targets) button.onClick.Invoke();
            row.CallbackMs = watch.Elapsed.TotalMilliseconds;
            FillState(row);
            row.Hit = row.HeroAfter != null && residentBefore.Contains(row.HeroAfter);
            _samples.Add(row);
            if (!_canWaitForEndOfFrame) yield break;
            yield return new WaitForEndOfFrame();
            row.EndOfFrameMs = watch.Elapsed.TotalMilliseconds;
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>检查点会遍历全部已加载纹理，开销不小，只在点击之间做。</summary>
        private void Checkpoint(string stage, int index)
        {
            var row = new SampleRow { Stage = stage, Index = index, Frame = Time.frameCount };
            FillState(row);
            row.ResidentPortraits = Resources.FindObjectsOfTypeAll<Texture2D>().Count(texture => texture.name == "portrait");
            row.TextureMemoryBytes = _textureMemory.Valid ? _textureMemory.LastValue : -1;
            _samples.Add(row);
        }

        private void FillState(SampleRow row)
        {
            row.HeroAfter = HeroId();
            row.WindowCount = _ui.CachedCreationPortraitCount;
            row.WindowIds = string.Join("|", _ui.CachedCreationPortraitIds.OrderBy(id => id, StringComparer.Ordinal));
            row.ConfirmEnabled = _buttons.TryGetValue("ConfirmCreate", out var confirm) && confirm.isActiveAndEnabled && confirm.IsInteractable();
        }

        private IEnumerator SampleFrames()
        {
            string phaseOfPreviousFrame = _phase;
            while (!_finished)
            {
                yield return null;
                // 这里读到的 LastValue 属于上一帧。
                var row = new FrameRow
                {
                    Frame = Time.frameCount - 1,
                    Phase = phaseOfPreviousFrame,
                    MainThreadMs = Milliseconds(_mainThread),
                    MarkerMs = _markers.Select(Milliseconds).ToArray(),
                    TextureMemoryBytes = _textureMemory.Valid ? _textureMemory.LastValue : -1,
                    WindowCount = _ui.CachedCreationPortraitCount,
                };
                _frames.Add(row);
                if (row.WindowCount > MaxWindowPortraits && !_windowViolationReported)
                {
                    _windowViolationReported = true;
                    Fail("window", $"frame {row.Frame} holds {row.WindowCount} creation portraits");
                }
                phaseOfPreviousFrame = _phase;
            }
        }

        private bool BindUi()
        {
            foreach (var button in _ui.GetComponentsInChildren<Button>(true))
            {
                if (button.name.StartsWith("Role_", StringComparison.Ordinal)) continue; // 角色卡每次进选角页都重建
                if (!_buttons.TryAdd(button.name, button)) _ambiguousButtons.Add(button.name);
            }
            _hero = _ui.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.name == "CharacterArtwork");
            _appearanceLabel = _ui.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.name == "AppearanceName");
            var required = _options.Roles.Count > 0 ? RequiredButtons.Append("CreateNewRole") : RequiredButtons;
            var missing = required.Where(name => !_buttons.ContainsKey(name) || _ambiguousButtons.Contains(name)).ToArray();
            if (_hero == null || _appearanceLabel == null || missing.Length > 0)
            {
                Fail("boot", "role UI is missing CharacterArtwork, AppearanceName or buttons: " + string.Join(",", missing));
                return false;
            }
            return true;
        }

        private Button UsableButton(string name)
            => _buttons.TryGetValue(name, out var button) && !_ambiguousButtons.Contains(name) &&
               button.isActiveAndEnabled && button.IsInteractable() ? button : null;

        private string HeroId()
        {
            if (_hero == null || !_hero.isActiveAndEnabled || _hero.sprite == null) return null;
            var name = _hero.sprite.name;
            return name.EndsWith(PortraitSuffix, StringComparison.Ordinal) ? name.Substring(0, name.Length - PortraitSuffix.Length) : name;
        }

        private string AppearanceLabel() => _appearanceLabel != null ? _appearanceLabel.text ?? string.Empty : string.Empty;

        private void StartRecorders()
        {
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            _textureMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
            for (int i = 0; i < MarkerNames.Length; i++)
                _markers[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerNames[i]);
        }

        private void DisposeRecorders()
        {
            _mainThread.Dispose();
            _textureMemory.Dispose();
            for (int i = 0; i < _markers.Length; i++) _markers[i].Dispose();
        }

        private static double Milliseconds(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue / 1e6 : -1;

        private void Fail(string stage, string reason)
        {
            _failures.Add(stage + ": " + reason);
            Debug.LogError($"[RoleCreatePerfProbe] FAIL stage={stage} reason={reason}");
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (!_completed && _failures.Count == 0) Fail("run", "measurement stopped before completion; see the log");
            string directory = null;
            try { directory = WriteResults(); }
            catch (Exception error)
            {
                Fail("output", error.Message);
                Debug.LogException(error);
            }
            bool passed = _failures.Count == 0;
            Debug.Log($"[RoleCreatePerfProbe] RESULT={(passed ? "PASS" : "FAIL")} output={directory} failures={string.Join(" | ", _failures)}");
            DisposeRecorders();
            if (!_options.Quit) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(passed ? 0 : 1);
#endif
        }

        private string WriteResults()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess().Id;
            var directory = Path.GetFullPath(Path.Combine(_options.OutputRoot,
                $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{process}"));
            if (Directory.Exists(directory)) throw new IOException("output directory already exists: " + directory);
            Directory.CreateDirectory(directory);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(directory, "samples.tsv"), SamplesTsv(), utf8);
            File.WriteAllText(Path.Combine(directory, "frames.tsv"), FramesTsv(), utf8);
            // 汇总最后写，写之前的失败也要进汇总。
            File.WriteAllText(Path.Combine(directory, "summary.json"), JsonUtility.ToJson(BuildSummary(directory), true), utf8);
            return directory;
        }

        private string SamplesTsv()
        {
            var text = new StringBuilder("stage\tindex\tframe\tcallbackMs\tendOfFrameMs\theroBefore\theroAfter\thit\twindowCount\twindowIds\tconfirmEnabled\tresidentPortraits\ttextureMemoryBytes\n");
            foreach (var row in _samples)
                text.Append(row.Stage).Append('\t').Append(row.Index).Append('\t').Append(row.Frame).Append('\t')
                    .Append(Format(row.CallbackMs)).Append('\t').Append(Format(row.EndOfFrameMs)).Append('\t')
                    .Append(row.HeroBefore).Append('\t').Append(row.HeroAfter).Append('\t').Append(row.Hit).Append('\t')
                    .Append(row.WindowCount).Append('\t').Append(row.WindowIds).Append('\t').Append(row.ConfirmEnabled).Append('\t')
                    .Append(row.ResidentPortraits).Append('\t').Append(row.TextureMemoryBytes).Append('\n');
            return text.ToString();
        }

        private string FramesTsv()
        {
            var text = new StringBuilder("frame\tphase\tmainThreadMs");
            foreach (var name in MarkerNames) text.Append('\t').Append(name).Append("Ms");
            text.Append("\ttextureMemoryBytes\twindowCount\n");
            foreach (var row in _frames)
            {
                text.Append(row.Frame).Append('\t').Append(row.Phase).Append('\t').Append(Format(row.MainThreadMs));
                foreach (var value in row.MarkerMs) text.Append('\t').Append(Format(value));
                text.Append('\t').Append(row.TextureMemoryBytes).Append('\t').Append(row.WindowCount).Append('\n');
            }
            return text.ToString();
        }

        private Summary BuildSummary(string directory)
        {
            int preload = Array.IndexOf(MarkerNames, CreationPortraitProfiling.PreloadName);
            int loadTexture = Array.IndexOf(MarkerNames, CreationPortraitProfiling.LoadTextureName);
            IEnumerable<SampleRow> Stage(params string[] stages) => _samples.Where(row => stages.Contains(row.Stage));
            var switches = Stage("next", "previous").ToArray();
            var closed = Stage("closed").Where(row => row.TextureMemoryBytes >= 0).ToArray();
            var stats = new List<Stat>
            {
                Describe("open.callbackMs", Stage("open").Select(row => row.CallbackMs)),
                Describe("open.endOfFrameMs", Stage("open").Select(row => row.EndOfFrameMs)),
                Describe("reopen.callbackMs", Stage("reopen").Select(row => row.CallbackMs)),
                Describe("reopen.endOfFrameMs", Stage("reopen").Select(row => row.EndOfFrameMs)),
                Describe("switch.hit.callbackMs", switches.Where(row => row.Hit).Select(row => row.CallbackMs)),
                Describe("switch.miss.callbackMs", switches.Where(row => !row.Hit).Select(row => row.CallbackMs)),
                Describe("rapid.callbackMs", Stage("rapid-next", "rapid-previous").Select(row => row.CallbackMs)),
                Describe("profession.callbackMs", Stage("profession").Select(row => row.CallbackMs)),
                // 基线帧：不在任何创角埋点里的平常帧。
                Describe("frame.baseline.mainThreadMs", _frames.Where(row => row.Phase == "baseline" && row.MarkerMs.All(ms => ms <= 0))
                    .Select(row => row.MainThreadMs)),
                Describe("frame.preload.mainThreadMs", _frames.Where(row => row.MarkerMs[preload] > 0).Select(row => row.MainThreadMs)),
                Describe("frame.preload.markerMs", _frames.Where(row => row.MarkerMs[preload] > 0).Select(row => row.MarkerMs[preload])),
                Describe("frame.loadTexture.markerMs", _frames.Where(row => row.MarkerMs[loadTexture] > 0).Select(row => row.MarkerMs[loadTexture])),
                Describe("closed.residentPortraits", closed.Select(row => (double)row.ResidentPortraits)),
                Describe("closed.textureMemoryMb", closed.Select(row => row.TextureMemoryBytes / 1048576.0)),
                Describe("closed.textureMemoryDriftMb", closed.Length < 2 ? Array.Empty<double>()
                    : new[] { (closed[closed.Length - 1].TextureMemoryBytes - closed[0].TextureMemoryBytes) / 1048576.0 }),
            };
            return new Summary
            {
                passed = _failures.Count == 0,
                failures = _failures.ToArray(),
                utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                outputDirectory = directory,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                isEditor = Application.isEditor,
                isDevelopmentBuild = Debug.isDebugBuild,
                endOfFrameMeasured = _canWaitForEndOfFrame,
                deviceModel = SystemInfo.deviceModel,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemoryMb = SystemInfo.systemMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsMemoryMb = SystemInfo.graphicsMemorySize,
                cycles = _options.Roles.Count > 0 ? _options.Cycles : 0,
                frameCount = _frames.Count,
                roles = _options.Roles.Select(role => $"{role.ClassId}:{role.Gender}:{role.AppearanceId}").ToArray(),
                creatableIdentities = QdaoCharacterCatalog.CreationOriginalAll.Select(entry => entry.Id).ToArray(),
                stats = stats.ToArray(),
            };
        }

        /// <summary>按最近秩取分位数；没有数据时 count 为 0、其余为 0。</summary>
        private static Stat Describe(string name, IEnumerable<double> values)
        {
            var sorted = values.Where(value => !double.IsNaN(value)).OrderBy(value => value).ToArray();
            var stat = new Stat { name = name, count = sorted.Length };
            if (sorted.Length == 0) return stat;
            stat.p50 = sorted[Math.Max(0, (int)Math.Ceiling(.50 * sorted.Length) - 1)];
            stat.p95 = sorted[Math.Max(0, (int)Math.Ceiling(.95 * sorted.Length) - 1)];
            stat.max = sorted[sorted.Length - 1];
            return stat;
        }

        private static string Format(double value)
            => double.IsNaN(value) ? string.Empty : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
#endif
