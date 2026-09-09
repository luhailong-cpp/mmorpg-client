using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

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
            public float TimeoutSeconds = 180f;
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

            if (!string.IsNullOrEmpty(_opt.ShotDir))
            {
                try { Directory.CreateDirectory(_opt.ShotDir); }
                catch (Exception e)
                {
                    Debug.LogWarning($"{Tag} shot dir unusable, screenshots disabled: {_opt.ShotDir} {e.Message}");
                    _opt.ShotDir = null;
                }
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

            // 4. 点击不可达位置(大殿屋顶):标记必须落在实际采用的目标上(殿前台阶下的主道)
            Warp(new Vector3(200f, 0f, 176f));
            yield return new WaitForSeconds(0.9f);
            Click(new Vector3(200f, 0f, 215f), "click_roof");
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

            // 6. 灯柱:出生点西侧的小灯柱在掩码里占 x≈187..190 / z≈179..193 的两列格子(整根剪影
            //    不可走),能站的最近位置是它西侧 (186,183)——脚点已在柱基之北,身体右缘与柱身重叠,
            //    这是"人应被柱子遮住一部分"的前后关系验证点;再绕到柱子南侧与东侧对照。
            Warp(new Vector3(184f, 0f, 177f));
            yield return new WaitForSeconds(0.8f);
            Click(new Vector3(185.8f, 0f, 183f), "occluder_west_behind");
            yield return WaitStop(6f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("07_occluder_west_behind");
            Click(new Vector3(188f, 0f, 176.5f), "occluder_south_front");
            yield return WaitStop(6f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("07_occluder_south_front");
            Click(new Vector3(191.5f, 0f, 183f), "occluder_east");
            yield return WaitStop(6f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("07_occluder_east");
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

            // 8. 贴边缩放:ClampFocus 只有在 orthographicSize 还在缓动的那几帧
            //    才可能露出底图边缘,而前面 55 帧全程 size=27,这一条一直没有
            //    实拍证据。贴到西边缘后拉远再拉近,逐帧看画面里有没有底图之外
            //    的颜色,以及内容有没有单帧跳变。
            var rig = _sandbox.CameraRig;
            if (rig != null)
            {
                Warp(new Vector3(64f, 0f, 190f));
                yield return new WaitForSeconds(1.0f);
                yield return Shot("09_zoom_base");
                rig.SetZoom(30f);
                yield return null;
                yield return Shot("09_zoom_out_f1");
                yield return new WaitForSeconds(0.06f);
                yield return Shot("09_zoom_out_f2");
                yield return new WaitForSeconds(0.5f);
                yield return Shot("09_zoom_out_settled");
                rig.SetZoom(12f);
                yield return null;
                yield return Shot("09_zoom_in_f1");
                yield return new WaitForSeconds(0.06f);
                yield return Shot("09_zoom_in_f2");
                yield return new WaitForSeconds(0.6f);
                yield return Shot("09_zoom_in_settled");
                rig.SetZoom(27f);
                yield return new WaitForSeconds(0.6f);
                yield return Shot("09_zoom_restored");
            }
            else
            {
                Log("zoom section skipped: sandbox exposes no camera rig");
            }

            Log($"RESULT=PASS shots={_shotSeq} dir={_opt.ShotDir ?? "-"}");
            Finish(0);
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
            Log($"click {why} at={F(groundPoint)} routed={ok} " +
                $"adopted={(adopted.HasValue ? F(adopted.Value) : "-")} feet={F(_ctrl.FeetPosition)}");
        }

        /// <summary>
        /// 等角色真正停下:脚点连续 0.5s 位移小于 5cm 且不在移动。IsMoving 在寻路被掩码
        /// 挡住的帧会短暂翻成 false 再继续,所以以位置稳定为准而不是只看标志位。
        /// </summary>
        private IEnumerator WaitStop(float maxSeconds)
        {
            var start = Time.realtimeSinceStartup;
            var deadline = start + maxSeconds;
            var last = _ctrl.FeetPosition;
            var stableSince = -1f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var now = Time.realtimeSinceStartup;
                var feet = _ctrl.FeetPosition;
                var moved = (feet - last).sqrMagnitude > 0.05f * 0.05f;
                last = feet;
                if (moved || _ctrl.IsMoving) stableSince = -1f;
                else if (stableSince < 0f) stableSince = now;
                else if (now - stableSince > 0.5f && now - start > 0.3f) yield break;
                yield return null;
            }
        }

        // ── 截图 ─────────────────────────────────────────

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var sprite = CurrentSpriteName();
            var file = "-";
            if (!string.IsNullOrEmpty(_opt.ShotDir) && Screen.width > 0 && Screen.height > 0)
            {
                file = $"{_shotSeq:00}_{name}.png";
                Texture2D tex = null;
                try
                {
                    tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                    tex.Apply(false);
                    File.WriteAllBytes(Path.Combine(_opt.ShotDir, file), tex.EncodeToPNG());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{Tag} shot failed {file}: {e.Message}");
                    file = "FAILED";
                }
                finally
                {
                    if (tex != null) Destroy(tex);
                }
            }
            _shotSeq++;
            _shots.Add(name);
            var cameraPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Log($"shot={name} file={file} feet={F(_ctrl.FeetPosition)} moving={_ctrl.IsMoving} sprite={sprite} " +
                $"markers={CountMarkers()} camera={F(cameraPosition)}");
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
            if (_ctrl != null)
            {
                _ctrl.SetDebugDirection(Vector3.zero);
                _ctrl.SetScriptedInputOwner(false);
            }
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
