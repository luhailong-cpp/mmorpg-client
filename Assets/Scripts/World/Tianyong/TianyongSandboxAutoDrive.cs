using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 主城移动观感离线验收驱动(沙盒场景专用,不依赖服务器)。
    /// 播放器带 <c>-sandboxDrive -shotDir &lt;dir&gt;</c> 启动时由 <see cref="TianyongSandboxBootstrap"/>
    /// 挂上:按固定脚本驱动真实的 <see cref="TianyongPlayerController"/>(原地站立 → 八方向 +
    /// 突然反向 → 连续短点击 / 不可达点击 → 停步再起步 → 灯柱与殿前附近 → 地图边缘),在每个观察点用
    /// ReadPixels 同步截屏写盘,并把脚点 / 是否移动 / 当前精灵帧写进日志(<c>[SandboxDrive] shot=…</c>)。
    /// 修改前后用同一分辨率跑同一脚本即可逐张对比(tools/run_city_move_verify.ps1)。
    /// 不带 <c>-sandboxDrive</c> 时本类不挂组件、不改任何既有行为。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongSandboxAutoDrive : MonoBehaviour
    {
        /// <summary>环境变量:整行参数,格式与命令行一致(编辑器调试用)。</summary>
        public const string ArgsEnvVar = "MMORPG_SANDBOX_DRIVE_ARGS";
        private const string Tag = "[SandboxDrive]";

        public sealed class Options
        {
            public bool Active;
            public string ShotDir;
            public bool QuitWhenDone = true;
            public float TimeoutSeconds = 240f;
        }

        private static Options _current;
        private static bool _parsed;

        public static Options Current
        {
            get
            {
                if (!_parsed)
                {
                    _parsed = true;
                    _current = ResolveOptions();
                }
                return _current;
            }
        }

        public static bool IsActive => Current != null && Current.Active;

        private TianyongSandboxBootstrap _sandbox;
        private TianyongPlayerController _ctrl;
        private Options _opt;
        private int _shotSeq;
        private bool _done;
        private readonly List<string> _shots = new();
        private readonly HashSet<string> _requiredShots = new();
        private readonly List<Renderer> _paintingRenderers = new();
        private StreamWriter _cameraTrace;
        private int _failureCount;
        private int _cameraSamples;
        private int _lastCameraFrame = -1;
        private int _cameraFailures;
        private int _completedZoomCases;
        private int _completedBehindCases;
        private int _expectedPaintingRenderers;
        private string _phase = "startup";
        private const float BoundsTolerance = 0.001f;
        private RenderTexture _captureTexture;
        private RenderTexture _previousTargetTexture;
        private int _offscreenRenderCount;
        private int _previousVSyncCount;
        private int _previousTargetFrameRate;
        private Vector3? _lastClickDestination;
        private string _lastClickName;


        /// <summary>沙盒 BuildSandbox 之后调用;未激活时不挂组件,返回 null。</summary>
        public static TianyongSandboxAutoDrive TryAttach(TianyongSandboxBootstrap sandbox)
        {
            if (sandbox == null || !IsActive) return null;
            var existing = sandbox.GetComponent<TianyongSandboxAutoDrive>();
            if (existing != null) return existing;
            var drive = sandbox.gameObject.AddComponent<TianyongSandboxAutoDrive>();
            drive._sandbox = sandbox;
            drive._opt = Current;
            return drive;
        }

        // ── 参数 ─────────────────────────────────────────

        private static Options ResolveOptions()
        {
            var opt = Parse(Environment.GetCommandLineArgs(), skipFirst: true);
            if (opt.Active) return opt;
            string line = null;
            try { line = Environment.GetEnvironmentVariable(ArgsEnvVar); } catch { }
            if (string.IsNullOrWhiteSpace(line)) return opt;
            return Parse(Tokenize(line), skipFirst: false);
        }

        public static Options Parse(IReadOnlyList<string> args, bool skipFirst)
        {
            var opt = new Options();
            if (args == null) return opt;
            for (var i = skipFirst ? 1 : 0; i < args.Count; i++)
            {
                var raw = args[i];
                if (string.IsNullOrEmpty(raw) || raw[0] != '-') continue;
                var key = raw.TrimStart('-');
                string inlineValue = null;
                var eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    inlineValue = key.Substring(eq + 1);
                    key = key.Substring(0, eq);
                }

                string NextValue()
                {
                    if (inlineValue != null) return inlineValue;
                    if (i + 1 < args.Count && !(args[i + 1].StartsWith("-") && args[i + 1].Length > 1
                                                && !char.IsDigit(args[i + 1][1])))
                    {
                        i++;
                        return args[i];
                    }
                    return null;
                }

                switch (key.ToLowerInvariant())
                {
                    case "sandboxdrive": opt.Active = true; break;
                    case "shotdir": opt.ShotDir = NextValue(); break;
                    case "drivenoquit": opt.QuitWhenDone = false; break;
                    case "drivetimeout":
                        if (float.TryParse(NextValue(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0f)
                            opt.TimeoutSeconds = t;
                        break;
                }
            }
            return opt;
        }

        private static List<string> Tokenize(string line)
        {
            var result = new List<string>();
            var cur = new System.Text.StringBuilder();
            var quoted = false;
            foreach (var c in line)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (cur.Length > 0) { result.Add(cur.ToString()); cur.Clear(); }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) result.Add(cur.ToString());
            return result;
        }

        // ── 生命周期 ─────────────────────────────────────

        private void Start()
        {
            if (_opt == null) _opt = Current;
            if (_sandbox == null) _sandbox = GetComponent<TianyongSandboxBootstrap>();
            if (_opt == null || !_opt.Active || _sandbox == null)
            {
                enabled = false;
                return;
            }
            Application.runInBackground = true;
            _previousVSyncCount = QualitySettings.vSyncCount;
            _previousTargetFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            StartCoroutine(CoRun());
            StartCoroutine(CoWatchdog());
        }

        private IEnumerator CoWatchdog()
        {
            yield return new WaitForSecondsRealtime(_opt.TimeoutSeconds);
            if (_done) yield break;
            Debug.LogError($"{Tag} RESULT=FAIL reason=timeout after {_opt.TimeoutSeconds}s shots={_shotSeq}");
            Finish(1);
        }

        private static readonly (string Name, Vector3 Dir)[] Directions =
        {
            ("N", Vector3.forward),
            ("NE", new Vector3(1f, 0f, 1f).normalized),
            ("E", Vector3.right),
            ("SE", new Vector3(1f, 0f, -1f).normalized),
            ("S", Vector3.back),
            ("SW", new Vector3(-1f, 0f, -1f).normalized),
            ("W", Vector3.left),
            ("NW", new Vector3(-1f, 0f, 1f).normalized),
        };

        private static Vector3 Dir(string name)
        {
            foreach (var (n, d) in Directions)
                if (n == name) return d;
            return Vector3.zero;
        }

        private IEnumerator CoRun()
        {
            yield return null;
            yield return null;
            _sandbox.SetHelpVisible(false);

            var player = _sandbox.Player;
            _ctrl = player != null ? player.GetComponent<TianyongPlayerController>() : null;
            if (_ctrl == null || _ctrl.Motor == null || _ctrl.Navigation == null)
            {
                Debug.LogError($"{Tag} RESULT=FAIL reason=sandbox player/controller missing");
                Finish(1);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(_opt.ShotDir))
            {
                Fail("shotDir is required for verifiable screenshots and camera CSV");
                Finish(1);
                yield break;
            }
            try { Directory.CreateDirectory(_opt.ShotDir); }
            catch (Exception e)
            {
                Fail($"shot directory unusable: {e.Message}");
                Finish(1);
                yield break;
            }

            var hud = _sandbox.GetComponent<TianyongSandboxHudVerification>();
            var hudDeadline = Time.realtimeSinceStartup + 10f;
            while (hud != null && !hud.Ready && string.IsNullOrEmpty(hud.Failure)
                   && Time.realtimeSinceStartup < hudDeadline)
                yield return null;
            if (hud == null || !hud.Ready)
            {
                Fail($"runtime HUD unavailable: {hud?.Failure ?? "component missing or timed out"}");
                Finish(1);
                yield break;
            }
            if (!BeginCameraVerification())
            {
                Finish(1);
                yield break;
            }
            if (!hud.SetCaptureCamera(_sandbox.CameraRig.RenderCamera))
            {
                Fail($"offscreen HUD setup failed: {hud.Failure}");
                Finish(1);
                yield break;
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (!hud.ValidateLayout(out var hudDetail))
            {
                Fail($"runtime HUD layout invalid: {hudDetail}");
                Finish(1);
                yield break;
            }
            _ctrl.SetScriptedInputOwner(true);
            StartCoroutine(CoMonitorCamera());
            yield return Shot("00_runtime_hud");
            yield return hud.VerifyNameplate(Shot);
            if (!hud.NameplateVerified)
            {
                Fail($"runtime nameplate verification failed: {hud.Failure}");
                Finish(1);
                yield break;
            }
            // A stray click OR keystroke while the player window takes focus
            // would walk the actor off an observation point and make the
            // captured frames non-reproducible; the drive owns movement from
            // here. The keyboard half matters most: its axes outrank path
            // following, so one held arrow key turns the click-to-move section
            // into a free-walk section without any error being logged.
            _ctrl.SetScriptedInputOwner(true);
            Log($"begin screen={Screen.width}x{Screen.height} shotDir={_opt.ShotDir ?? "-"} spawn={F(_ctrl.FeetPosition)}");

            // 1. 原地站立 3 秒。落点在广场南侧主道(走路掩码里 x 182..214 / z 76..108 是最大的
            //    32u 开阔方块),八方向各 0.9s(约 8u)都不会撞到不可走区;广场本体的装饰环带在
            //    掩码里是不可走的,不能用作起点。
            Warp(new Vector3(198f, 0f, 92f));
            yield return new WaitForSeconds(1.0f); // 相机跟随收敛
            yield return Shot("01_idle_a");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("01_idle_b");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("01_idle_c");

            // 2. 八方向:先向 A 跑 0.9s,不停步直接反向(突然反向),再跑 0.9s,然后停步观察 0.8s
            string[][] pairs = { new[] { "N", "S" }, new[] { "E", "W" }, new[] { "NE", "SW" }, new[] { "SE", "NW" } };
            foreach (var pair in pairs)
            {
                var a = pair[0];
                var b = pair[1];
                _ctrl.SetDebugDirection(Dir(a));
                yield return new WaitForSeconds(0.45f);
                yield return Shot($"02_run_{a}_a");
                yield return new WaitForSeconds(0.45f);
                yield return Shot($"02_run_{a}_b");

                // a 腿也停一次。原来只在 b 腿之后停步,于是 N/E/NE/SE 四个方向
                // 永远没有停步样本,"八方向都自然站定"这条验收只覆盖了一半。
                _ctrl.SetDebugDirection(Vector3.zero);
                yield return new WaitForSeconds(0.12f);
                yield return Shot($"03_stop_{a}_a");
                yield return new WaitForSeconds(0.7f);
                yield return Shot($"03_stop_{a}_b");

                // 站定之后重新起步,再做突然反向:这样"起步"与"反向"各测一次
                _ctrl.SetDebugDirection(Dir(a));
                yield return new WaitForSeconds(0.45f);

                _ctrl.SetDebugDirection(Dir(b)); // 突然反向,不经过停步
                yield return new WaitForSeconds(0.08f);
                yield return Shot($"02_reverse_{b}_a");
                yield return new WaitForSeconds(0.3f);
                yield return Shot($"02_reverse_{b}_b");
                yield return new WaitForSeconds(0.52f);
                yield return Shot($"02_run_{b}_b");

                _ctrl.SetDebugDirection(Vector3.zero);
                yield return new WaitForSeconds(0.12f);
                yield return Shot($"03_stop_{b}_a");
                yield return new WaitForSeconds(0.7f);
                yield return Shot($"03_stop_{b}_b");
            }

            // 3. 连续短距离点击 + 反向 + 密集点击(残留数量)
            var feet = _ctrl.FeetPosition;
            Click(feet + new Vector3(5f, 0f, 0f), "click_short");
            yield return new WaitForSeconds(0.03f);
            yield return Shot("04_click_a");
            yield return new WaitForSeconds(0.2f);
            yield return Shot("04_click_b");
            yield return new WaitForSeconds(0.25f);
            yield return Shot("04_click_c");
            feet = _ctrl.FeetPosition;
            Click(feet + new Vector3(-6f, 0f, 0f), "click_reverse");
            yield return new WaitForSeconds(0.08f);
            yield return Shot("04_click_reverse_a");
            yield return new WaitForSeconds(0.3f);
            yield return Shot("04_click_reverse_b");
            yield return WaitStop(4f);
            yield return new WaitForSeconds(0.1f);
            yield return Shot("04_click_arrive_a");
            yield return new WaitForSeconds(0.8f);
            yield return Shot("04_click_arrive_b");

            feet = _ctrl.FeetPosition;
            Vector3[] rapid =
            {
                new(3f, 0f, 2f), new(-3f, 0f, 2f), new(3f, 0f, -2f), new(-3f, 0f, -2f),
                new(4f, 0f, 0f), new(0f, 0f, 4f), new(-4f, 0f, 0f), new(0f, 0f, -4f),
            };
            foreach (var off in rapid)
            {
                Click(feet + off, "click_rapid");
                yield return new WaitForSeconds(0.1f);
            }
            yield return Shot("04_click_rapid_a");
            yield return new WaitForSeconds(0.35f);
            yield return Shot("04_click_rapid_b");
            yield return WaitStop(4f);

            // 4. 点击不可达位置(大殿屋顶):标记必须落在实际采用的目标上(殿前台阶下的主道)。
            //    节庆版底图的大殿占 z 196.5..241,最近可走点搜索只找 8 格(16u);原来点
            //    (200,215) 离殿南沿 18.5u,已经搜不到、routed=False,整轮误报失败。
            //    改点 z=207,仍在屋顶上,距南沿 10.5u。
            Warp(new Vector3(200f, 0f, 176f));
            yield return new WaitForSeconds(0.9f);
            Click(new Vector3(200f, 0f, 207f), "click_roof");
            yield return new WaitForSeconds(0.05f);
            yield return Shot("05_click_roof_a");
            yield return new WaitForSeconds(0.25f);
            yield return Shot("05_click_roof_b");
            yield return WaitStop(10f);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("05_click_roof_arrive");

            // 5. 停步再起步
            feet = _ctrl.FeetPosition;
            Click(feet + new Vector3(0f, 0f, -12f), "restart_leg1");
            yield return WaitStop(5f);
            yield return new WaitForSeconds(0.9f);
            yield return Shot("06_restart_stopped");
            feet = _ctrl.FeetPosition;
            Click(feet + new Vector3(8f, 0f, 0f), "restart_leg2");
            yield return new WaitForSeconds(0.1f);
            yield return Shot("06_restart_go_a");
            yield return new WaitForSeconds(0.35f);
            yield return Shot("06_restart_go_b");
            yield return WaitStop(5f);

            // 6. 新版灯柱:西灯落地点约 (167.04,185.30)，东灯 (232.71,185.30)。
            //    站在西灯西侧合法道路 (163,189)，脚点在落地线以北，身体右缘与屋檐
            //    重叠。沿真实道路绕到南侧，再到东侧；整柱跨 tile 的两片同步开关。
            Warp(new Vector3(163f, 0f, 182f));
            yield return new WaitForSeconds(0.8f);
            Click(new Vector3(163f, 0f, 189f), "occluder_west_behind");
            yield return WaitStop(6f);
            yield return new WaitForSeconds(0.3f);
            // Same actor, camera and position: expose the original overlap,
            // then enable the base-sorted foreground and capture the correction.
            if (!TianyongPaintedForeground.SetWestLampVisible(_sandbox.transform, false))
                Fail("west lamp foreground missing for before/after verification");
            yield return Shot("07_occluder_west_before_foreground");
            if (!TianyongPaintedForeground.SetWestLampVisible(_sandbox.transform, true))
                Fail("west lamp foreground could not be restored");
            yield return Shot("07_occluder_west_behind");
            Click(new Vector3(167f, 0f, 182f), "occluder_south_front");
            yield return WaitStop(6f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("07_occluder_south_front");
            Click(new Vector3(173f, 0f, 189f), "occluder_east");
            yield return WaitStop(6f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("07_occluder_east");
            // 东灯西側 (227,189) 对照同样的遮挡关系。先等相机完全停稳，确保
            // 同站位 A/B 图的区别只来自整柱前景开关。
            Warp(new Vector3(227f, 0f, 189f));
            yield return new WaitForSeconds(0.8f);
            yield return WaitCameraSettled(3f);
            var eastPairCamera = _sandbox.CameraRig.RenderCamera.transform.position;
            if (!TianyongPaintedForeground.SetVisible(_sandbox.transform, TianyongPaintedForeground.EastLampObjectName, false))
                Fail("east lamp foreground missing for before/after verification");
            yield return Shot("07_occluder_east_lamp_before_foreground");
            if (!TianyongPaintedForeground.SetVisible(_sandbox.transform, TianyongPaintedForeground.EastLampObjectName, true))
                Fail("east lamp foreground could not be restored");
            yield return Shot("07_occluder_east_lamp_behind");
            if ((_sandbox.CameraRig.RenderCamera.transform.position - eastPairCamera).sqrMagnitude > 1e-6f)
                Fail("camera moved between the east lamp before/after shots; the A/B pair is not comparable");
            // 明亮地砖与高柱/建筑阴影交界:大殿台阶西侧的高柱脚下,以及殿前
            Warp(new Vector3(165f, 0f, 183f));
            yield return new WaitForSeconds(0.9f);
            yield return Shot("07_near_pillar");
            Warp(new Vector3(197f, 0f, 197f));
            yield return new WaitForSeconds(0.9f);
            yield return Shot("07_temple_front");

            // 7. 地图边缘:掩码可走范围 x 60..340 / z 16..280;西门、东门、南门口各看一次
            Warp(new Vector3(64f, 0f, 190f));
            yield return new WaitForSeconds(1.0f);
            yield return Shot("08_edge_w");
            Click(new Vector3(61f, 0f, 186f), "edge_w");
            yield return WaitStop(8f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("08_edge_w_arrive");
            Warp(new Vector3(337f, 0f, 183f));
            yield return new WaitForSeconds(1.0f);
            yield return Shot("08_edge_e");
            Warp(new Vector3(185f, 0f, 20f));
            yield return new WaitForSeconds(1.0f);
            yield return Shot("08_edge_s");
            Warp(new Vector3(200f, 0f, 285f));
            yield return new WaitForSeconds(1.0f);
            yield return Shot("08_edge_n");

            // Exercise every rendered edge and corner with an exact camera target.
            // Navigation may move a requested corner to a distant walkable cell, so
            // the camera probe is independent of Warp's nearest-walkable fallback.
            var rig = _sandbox.CameraRig;
            var previousTarget = rig.Target;
            var probe = new GameObject("[CameraVerificationTarget]");
            probe.transform.SetParent(_sandbox.transform, false);
            var cameraCases = new (string Name, float X, float Z)[]
            {
                ("w", 0f, 0.5f), ("e", 1f, 0.5f),
                ("s", 0.5f, 0f), ("n", 0.5f, 1f),
                ("sw", 0f, 0f), ("se", 1f, 0f),
                ("nw", 0f, 1f), ("ne", 1f, 1f),
            };
            foreach (var cameraCase in cameraCases)
            {
                var bounds = RenderedPaintingBounds();
                probe.transform.position = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, cameraCase.X), 0f,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, cameraCase.Z));
                rig.SetZoom(12f);
                rig.SetTarget(probe.transform);
                _phase = $"zoom_{cameraCase.Name}_base";
                yield return new WaitForSeconds(0.1f);
                yield return Shot($"09_zoom_{cameraCase.Name}_base");

                _phase = $"zoom_{cameraCase.Name}_out";
                rig.SetZoom(30f);
                yield return null;
                yield return Shot($"09_zoom_{cameraCase.Name}_out_f1");
                yield return new WaitForSeconds(0.06f);
                yield return Shot($"09_zoom_{cameraCase.Name}_out_006");
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"09_zoom_{cameraCase.Name}_out_settled");

                _phase = $"zoom_{cameraCase.Name}_in";
                rig.SetZoom(12f);
                yield return null;
                yield return Shot($"09_zoom_{cameraCase.Name}_in_f1");
                yield return new WaitForSeconds(0.06f);
                yield return Shot($"09_zoom_{cameraCase.Name}_in_006");
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"09_zoom_{cameraCase.Name}_in_settled");
                _completedZoomCases++;
            }
            rig.SetZoom(27f);
            rig.SetTarget(previousTarget);
            Destroy(probe);
            _phase = "zoom_restored";
            yield return new WaitForSeconds(0.6f);
            yield return Shot("09_zoom_restored");

            // 9. 遮挡穿帮定点:从走路掩码里挑出四周可走的独立小障碍(灯柱/石柱/花台),
            //    站到它正北方第一个可走格 —— 透视上人物比它更远,底图画的柱身应当
            //    挡住人物下半身。底图是单张 Unlit quad、永远画在所有精灵之下,所以
            //    这里必然穿帮;这一组就是把穿帮拍下来当作补前景层的依据。
            Vector3[] behindProps =
            {
                new(229f, 0f, 231f), new(239f, 0f, 179f), new(187f, 0f, 131f),
                new(159f, 0f, 179f), new(185f, 0f, 153f), new(203f, 0f, 195f),
            };
            for (var i = 0; i < behindProps.Length; i++)
            {
                Warp(behindProps[i]);
                yield return new WaitForSeconds(0.8f);
                yield return Shot($"10_behind_prop_{i}");
                _completedBehindCases++;
            }

            if (_cameraSamples == 0) Fail("no end-of-frame camera samples recorded");
            if (_completedZoomCases != 8) Fail($"incomplete edge/corner zoom cases: {_completedZoomCases}/8");
            if (_completedBehindCases != behindProps.Length)
                Fail($"incomplete behind-prop cases: {_completedBehindCases}/{behindProps.Length}");
            foreach (var requiredShot in _requiredShots)
                if (!_shots.Contains(requiredShot)) Fail($"required screenshot missing: {requiredShot}");
            Finish(_failureCount == 0 ? 0 : 1);
        }


        private bool BeginCameraVerification()
        {
            var rig = _sandbox.CameraRig;
            if (rig == null || !rig.IsTopDown || rig.RenderCamera == null)
            {
                Fail("camera verification requires the painted-city top-down camera");
                return false;
            }
            Transform painting = null;
            foreach (var child in _sandbox.GetComponentsInChildren<Transform>(true))
                if (child.name == TianyongPaintedCity.RootName) { painting = child; break; }
            if (painting == null)
            {
                Fail("rendered painting root missing");
                return false;
            }
            foreach (var renderer in painting.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "Ground" || renderer.name.StartsWith("Tile_r", StringComparison.Ordinal))
                    _paintingRenderers.Add(renderer);
            _expectedPaintingRenderers = _paintingRenderers.Count;
            if (_expectedPaintingRenderers != 1 &&
                _expectedPaintingRenderers != TianyongPaintedCity.TileRows * TianyongPaintedCity.TileColumns)
            {
                Fail($"painting renderer count invalid: {_expectedPaintingRenderers}");
                return false;
            }
            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("a real graphics device is required for offscreen capture");
                _captureTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "TianyongAcceptanceCapture",
                    antiAliasing = 1,
                };
                if (!_captureTexture.Create()) throw new InvalidOperationException("RenderTexture creation failed");
                _previousTargetTexture = rig.RenderCamera.targetTexture;
                rig.RenderCamera.targetTexture = _captureTexture;
                rig.RenderCamera.aspect = (float)_captureTexture.width / _captureTexture.height;
                _cameraTrace = new StreamWriter(Path.Combine(_opt.ShotDir, "camera-frames.csv"), false);
                _cameraTrace.WriteLine("frame,rendered_frame,offscreen_render,mode,realtime,phase,actual_size,requested_size,aspect,focus_x,focus_z,camera_x,camera_y,camera_z,map_x_min,map_z_min,map_x_max,map_z_max,view_x_min,view_z_min,view_x_max,view_z_max,overflow,pass");
            }
            catch (Exception e)
            {
                Fail($"camera CSV unavailable: {e.Message}");
                return false;
            }
            var camera = rig.RenderCamera;
            Log($"camera verification mode=offscreen_render size={_captureTexture.width}x{_captureTexture.height} "
                + $"graphics={SystemInfo.graphicsDeviceType} vsync={QualitySettings.vSyncCount} targetFPS={Application.targetFrameRate} "
                + $"clearFlags={camera.clearFlags} background={camera.backgroundColor} "
                + $"hdr={camera.allowHDR} renderers={_expectedPaintingRenderers} geometryTolerance={BoundsTolerance}");
            return true;
        }

        private Bounds RenderedPaintingBounds()
        {
            var bounds = _paintingRenderers[0].bounds;
            for (var i = 1; i < _paintingRenderers.Count; i++)
                bounds.Encapsulate(_paintingRenderers[i].bounds);
            return bounds;
        }

        private IEnumerator CoMonitorCamera()
        {
            while (!_done)
            {
                yield return new WaitForEndOfFrame();
                if (!_done) VerifyCameraFrame();
            }
        }

        private void VerifyCameraFrame()
        {
            try { RecordCameraFrame(); }
            catch (Exception e)
            {
                Fail($"camera verification interrupted: {e.Message}");
                Finish(1);
            }
        }

        private void RecordCameraFrame()
        {
            if (_lastCameraFrame == Time.frameCount) return;
            if (_lastCameraFrame >= 0 && Time.frameCount != _lastCameraFrame + 1)
                Fail($"camera frame gap: {_lastCameraFrame} -> {Time.frameCount}");
            _lastCameraFrame = Time.frameCount;
            var rig = _sandbox.CameraRig;
            var camera = rig.RenderCamera;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            _offscreenRenderCount++;
            var bounds = RenderedPaintingBounds();
            var valid = camera != null && camera.enabled && rig.IsTopDown && camera.orthographic;
            foreach (var renderer in _paintingRenderers)
                valid &= renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy
                         && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0
                         && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null
                         && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.isSupported;
            var plane = new Plane(Vector3.up, new Vector3(0f, bounds.center.y, 0f));
            var min = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, 0f, float.NegativeInfinity);
            for (var corner = 0; corner < 4; corner++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(corner & 1, (corner >> 1) & 1, 0f));
                if (!plane.Raycast(ray, out var distance))
                {
                    valid = false;
                    continue;
                }
                var point = ray.GetPoint(distance);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                var depth = camera.transform.InverseTransformPoint(point).z;
                valid &= depth >= camera.nearClipPlane && depth <= camera.farClipPlane;
            }
            var overflow = Mathf.Max(0f, bounds.min.x - min.x, max.x - bounds.max.x,
                bounds.min.z - min.z, max.z - bounds.max.z);
            valid &= !float.IsNaN(overflow) && !float.IsInfinity(overflow) && overflow <= BoundsTolerance;
            _cameraSamples++;
            if (!valid)
            {
                _cameraFailures++;
                if (_cameraFailures == 1)
                    Fail($"camera coverage failed at frame={Time.frameCount} phase={_phase} overflow={overflow}");
            }
            var focus = rig.Focus;
            var position = camera.transform.position;
            try
            {
                _cameraTrace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},offscreen_render,{3:R},{4},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20:R},{21:R},{22}",
                    Time.frameCount, Time.renderedFrameCount, _offscreenRenderCount,
                    Time.realtimeSinceStartup, _phase, camera.orthographicSize,
                    rig.RequestedZoom, camera.aspect, focus.x, focus.z, position.x, position.y, position.z,
                    bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z,
                    min.x, min.z, max.x, max.z, overflow, valid ? 1 : 0));
                if (_cameraSamples % 60 == 0) _cameraTrace.Flush();
            }
            catch (Exception e)
            {
                Fail($"camera CSV write failed: {e.Message}");
                Finish(1);
            }
        }

        private void Fail(string reason)
        {
            _failureCount++;
            Debug.LogError($"{Tag} verification failure: {reason}");
        }

        // ── 驱动原语 ─────────────────────────────────────

        private void Warp(Vector3 want)
        {
            if (!_ctrl.Navigation.TryFindNearestWalkable(want, out var point))
                point = TianyongMapDefinition.DefaultSpawn;
            _ctrl.SetDebugDirection(Vector3.zero);
            _ctrl.WarpTo(point);
            Log($"warp want={F(want)} feet={F(_ctrl.FeetPosition)}");
        }

        private void Click(Vector3 groundPoint, string why)
        {
            var ok = _ctrl.ClickAt(groundPoint);
            // adopted = where the ring was dropped and where the actor is now
            // headed. For a click on an unwalkable spot (a roof, the canal)
            // this is the nearest legal cell, and it must match the ring.
            var adopted = _ctrl.PathDestination;
            _lastClickDestination = adopted;
            _lastClickName = why;
            Log($"click {why} at={F(groundPoint)} routed={ok} " +
                $"adopted={(adopted.HasValue ? F(adopted.Value) : "-")} feet={F(_ctrl.FeetPosition)}");
        }

        /// <summary>
        /// 等相机停稳:连续 0.3s 每帧位移都小于 0.0005u。同站位前后对照(关/开前景)的两张图
        /// 必须相机完全同位,否则亚像素平移会让整屏重采样都变,差异就不再只来自前景。
        /// </summary>
        private IEnumerator WaitCameraSettled(float maxSeconds)
        {
            var camera = _sandbox.CameraRig.RenderCamera;
            var deadline = Time.realtimeSinceStartup + maxSeconds;
            var last = camera.transform.position;
            var stableSince = -1f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var now = Time.realtimeSinceStartup;
                var position = camera.transform.position;
                if ((position - last).sqrMagnitude > 0.0005f * 0.0005f) stableSince = -1f;
                else if (stableSince < 0f) stableSince = now;
                else if (now - stableSince >= 0.3f) yield break;
                last = position;
            }
            Fail($"camera did not settle within {maxSeconds:F1}s");
        }

        /// <summary>
        /// 等角色真正停下:脚点连续 0.5s 位移小于 5cm 且不在移动。IsMoving 在寻路被掩码
        /// 挡住的帧会短暂翻成 false 再继续,所以以位置稳定为准而不是只看标志位。
        /// </summary>
        private IEnumerator WaitStop(float maxSeconds)
        {
            var start = Time.realtimeSinceStartup;
            var arrivalDeadline = start + maxSeconds;
            // The movement budget and the mandatory stationary observation are
            // separate. Only an actor already arrived before the deadline can
            // use this final half-second; a blocked path cannot gain extra time.
            var settleDeadline = arrivalDeadline + 0.55f;
            var anchor = _ctrl.FeetPosition;
            var stableSince = -1f;
            var arrivalTolerance = Mathf.Max(0.25f, _ctrl.Motor.radius * 0.9f) + 0.005f;
            while (Time.realtimeSinceStartup < arrivalDeadline ||
                   (stableSince >= 0f && Time.realtimeSinceStartup < settleDeadline))
            {
                var now = Time.realtimeSinceStartup;
                var feet = _ctrl.FeetPosition;
                var destinationDelta = _lastClickDestination.HasValue
                    ? _lastClickDestination.Value - feet : new Vector3(float.PositiveInfinity, 0f, 0f);
                destinationDelta.y = 0f;
                var arrived = !_ctrl.IsMoving && !_ctrl.PathDestination.HasValue
                              && destinationDelta.sqrMagnitude <= arrivalTolerance * arrivalTolerance;
                if (!arrived)
                    stableSince = -1f;
                else if (stableSince < 0f)
                {
                    if (now >= arrivalDeadline) break;
                    stableSince = now;
                    anchor = feet;
                }
                else if ((feet - anchor).sqrMagnitude > 0.05f * 0.05f)
                {
                    stableSince = -1f;
                }
                else if (now - stableSince >= 0.5f)
                {
                    Log($"stop verified click={_lastClickName} elapsed={now - start:F3}s "
                        + $"distance={destinationDelta.magnitude:F4} tolerance={arrivalTolerance:F4}");
                    yield break;
                }
                yield return null;
            }
            Fail($"WaitStop timed out: click={_lastClickName} movementBudget={maxSeconds}s "
                + $"feet={F(_ctrl.FeetPosition)} moving={_ctrl.IsMoving} "
                + $"pathActive={_ctrl.PathDestination.HasValue} "
                + $"destination={(_lastClickDestination.HasValue ? F(_lastClickDestination.Value) : "-")}");
        }

        // ── 截图 ─────────────────────────────────────────

        private IEnumerator Shot(string name)
        {
            if (!_requiredShots.Add(name)) Fail($"duplicate screenshot name: {name}");
            yield return new WaitForEndOfFrame();
            if (_done) yield break;
            VerifyCameraFrame();
            if (_done) yield break;
            var sprite = CurrentSpriteName();
            var file = $"{_shotSeq:00}_{name}.png";
            Texture2D tex = null;
            try
            {
                if (string.IsNullOrEmpty(_opt.ShotDir) || Screen.width <= 0 || Screen.height <= 0)
                    throw new InvalidOperationException("screenshot output or screen size unavailable");
                if (_captureTexture == null || !_captureTexture.IsCreated() || _offscreenRenderCount == 0)
                    throw new InvalidOperationException("no completed offscreen render is available");
                tex = new Texture2D(_captureTexture.width, _captureTexture.height, TextureFormat.RGB24, false);
                var previousActive = RenderTexture.active;
                try
                {
                    RenderTexture.active = _captureTexture;
                    tex.ReadPixels(new Rect(0, 0, _captureTexture.width, _captureTexture.height), 0, 0, false);
                    tex.Apply(false);
                }
                finally { RenderTexture.active = previousActive; }
                var path = Path.Combine(_opt.ShotDir, file);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                if (!File.Exists(path) || new FileInfo(path).Length <= 8)
                    throw new IOException("PNG was not written");
                AssertCapturedWorldHasTexture(tex);
                _shots.Add(name);
            }
            catch (Exception e)
            {
                Fail($"screenshot failed: {file}: {e.Message}");
                file = "FAILED";
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
            _shotSeq++;
            var camera = _sandbox.CameraRig.RenderCamera;
            Log($"shot={name} file={file} frame={Time.frameCount} renderedFrame={Time.renderedFrameCount} "
                + $"offscreenRender={_offscreenRenderCount} capture=offscreen_render realtime={Time.realtimeSinceStartup:F4} "
                + $"feet={F(_ctrl.FeetPosition)} moving={_ctrl.IsMoving} sprite={sprite} "
                + $"markers={CountMarkers()} camera={F(camera.transform.position)} "
                + $"actualSize={camera.orthographicSize:F6} requestedSize={_sandbox.CameraRig.RequestedZoom:F6}");
        }


        private static void AssertCapturedWorldHasTexture(Texture2D texture)
        {
            // This rejects an undrawn, nearly uniform framebuffer. It does not
            // replace the viewport geometry checks or visual review of the art.
            var pixels = texture.GetPixels32();
            var counts = new Dictionary<int, int>();
            var total = 0;
            var mostCommon = 0;
            double sum = 0, squares = 0;
            var stepX = Mathf.Max(1, texture.width / 64);
            var stepY = Mathf.Max(1, texture.height / 64);
            for (var y = texture.height / 5; y < texture.height * 4 / 5; y += stepY)
            for (var x = texture.width / 5; x < texture.width * 4 / 5; x += stepX)
            {
                var pixel = pixels[y * texture.width + x];
                var key = (pixel.r << 16) | (pixel.g << 8) | pixel.b;
                counts.TryGetValue(key, out var count);
                counts[key] = ++count;
                mostCommon = Mathf.Max(mostCommon, count);
                var luminance = (pixel.r + pixel.g + pixel.b) / 3.0;
                sum += luminance;
                squares += luminance * luminance;
                total++;
            }
            var variance = total > 0 ? squares / total - Math.Pow(sum / total, 2) : 0;
            if (total == 0 || counts.Count < 32 || variance < 1.0 || mostCommon > total * 0.98)
                throw new InvalidOperationException(
                    $"captured world is blank or nearly uniform: colors={counts.Count}, variance={variance:F3}, dominant={mostCommon}/{total}");
        }

        private string CurrentSpriteName()
        {
            var player = _sandbox != null ? _sandbox.Player : null;
            if (player == null) return "-";
            foreach (var renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.gameObject.name == "sprite")
                    return renderer.sprite != null ? renderer.sprite.name : "(null)";
            }
            return "-";
        }

        private static int CountMarkers()
#if UNITY_6000_6_OR_NEWER
            => FindObjectsByType<TianyongClickMarker>().Length;
#else
            => FindObjectsByType<TianyongClickMarker>(FindObjectsSortMode.None).Length;
#endif

        private void Finish(int code)
        {
            if (_done) return;
            _done = true;
            StopAllCoroutines();
            try
            {
                _cameraTrace?.Flush();
                _cameraTrace?.Dispose();
            }
            catch (Exception e) { Fail($"camera CSV close failed: {e.Message}"); }
            _cameraTrace = null;
            try
            {
                var hud = _sandbox != null ? _sandbox.GetComponent<TianyongSandboxHudVerification>() : null;
                if (hud != null && !hud.SetCaptureCamera(null))
                    Fail($"restore HUD camera failed: {hud.Failure}");
                var camera = _sandbox != null ? _sandbox.CameraRig?.RenderCamera : null;
                if (camera != null) camera.targetTexture = _previousTargetTexture;
                if (_captureTexture != null)
                {
                    _captureTexture.Release();
                    Destroy(_captureTexture);
                    _captureTexture = null;
                }
            }
            catch (Exception e) { Fail($"capture cleanup failed: {e.Message}"); }
            QualitySettings.vSyncCount = _previousVSyncCount;
            Application.targetFrameRate = _previousTargetFrameRate;
            if (_failureCount > 0) code = 1;
            if (_ctrl != null)
            {
                _ctrl.SetDebugDirection(Vector3.zero);
                _ctrl.SetScriptedInputOwner(false);
            }
            Log($"RESULT={(code == 0 ? "PASS" : "FAIL")} shots={_shots.Count}/{_shotSeq} "
                + $"cameraSamples={_cameraSamples} cameraFailures={_cameraFailures} failures={_failureCount} "
                + $"offscreenRenders={_offscreenRenderCount} capture=offscreen_render "
                + $"zoomCases={_completedZoomCases}/8 behindCases={_completedBehindCases}/6 dir={_opt.ShotDir ?? "-"}");
            Log($"quit exit_code={code} shots={string.Join(",", _shots)}");
            if (!_opt.QuitWhenDone) return;
            if (Application.isEditor) return;
            Application.Quit(code);
        }

        private static string F(Vector3 v)
            => string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);

        private static void Log(string message) => Debug.Log($"{Tag} {message}");
    }
}
