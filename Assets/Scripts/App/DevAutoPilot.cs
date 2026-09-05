using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI;
using UnityEngine;

namespace MmorpgClient.App
{
    /// <summary>
    /// 无人值守自动驾驶(开发/验证用):按命令行参数绕过选区 UI 直接
    /// EnterZone → 进场景 → JoinQueue → 自动战斗 → 打完退出。用途是双实例
    /// 跨区匹配验证(tools/run_crosszone_pair.ps1),对齐服务端
    /// robot/battle_smoke_cross_zone_scenario.go 的流程与断言。
    ///
    /// 命令行(播放器):
    ///   -gateway http://127.0.0.1:8081 -zone 1 -account robot_9101 -password 123456
    ///   -autoQueue 1v1 -battleConfig 1 -autoBattle -quitOnBattleEnd -logTag A
    ///   可选超时覆盖:-loginTimeout 30 -queueTimeout 60 -battleTimeout 180(秒)
    /// 编辑器调试:命令行没带 -zone 时,依次从环境变量 MMORPG_AUTOPILOT_ARGS、
    ///   PlayerPrefs "mmorpg.devautopilot.args" 读同格式的一整行参数。
    ///
    /// 只有带 -zone 才激活;不激活时本类不挂组件、不改任何既有行为。
    /// 激活后 gateway/account 以命令行为准(AppBootstrap 构造 Session 时套用),
    /// 且 GameClient.PlayerChooser 置 null:EnterZone 管线在该区无角色时按
    /// 服务端默认职业静默建号,有角色则进第一个(GameClient.EnterZone 既有分支)。
    ///
    /// 日志前缀 [AutoPilot][tag],关键行(run_crosszone_pair.ps1 按此解析):
    ///   stage=in_game … gate=ip:port(落区证据:两实例 gate 相同即不是跨区)/
    ///   BattleStart battle_id=N / BattleEnd battle_id=N outcome=X turns=N /
    ///   RESULT=PASS … 或 RESULT=FAIL stage=… reason=…
    /// 失败判定对齐服务端 robot:进场时残留战斗/排队相位、JoinQueue 被拒、开局即终局
    /// (turns=0,既有缺陷"上一局阵亡带 0 血入队开局判负")都直接 FAIL,不空等超时。
    /// 退出码:0 = 打完;1 = 任一阶段超时/失败(仅 -quitOnBattleEnd 时退出进程)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevAutoPilot : MonoBehaviour
    {
        /// <summary>编辑器调试用 PlayerPrefs 键:整行参数,格式与命令行一致。</summary>
        public const string EditorArgsPrefKey = "mmorpg.devautopilot.args";
        /// <summary>环境变量:整行参数,格式与命令行一致(编辑器/播放器都认)。</summary>
        public const string ArgsEnvVar = "MMORPG_AUTOPILOT_ARGS";

        public sealed class Options
        {
            public string Gateway;          // null = 不覆盖 ClientSettings.GatewayBaseUrl
            public uint Zone;               // 0 = 未激活
            public string Account;
            public string Password;
            public string AutoQueue;        // "1v1" / null
            public uint? BattleConfig;      // null = 用 BattleUiStyle 默认
            public bool AutoBattle;
            public bool QuitOnBattleEnd;
            public string LogTag;
            public float LoginTimeout = 30f;
            public float QueueTimeout = 60f;
            public float BattleTimeout = 180f;

            // ── 演出截图(视觉验收用;-shotDir 为空 = 完全关闭,零开销)──
            public string ShotDir;              // 截图输出目录;为空则不截图
            public float ShotInterval = 0.4f;   // 定时截图间隔(realtime 秒)
            public int ShotSuperSize = 1;       // ScreenCapture 超采样倍数(1 = 原分辨率)
            public int ShotMax = 400;           // 单实例最多截多少张(防跑飞把磁盘写满)
            public bool ShotAll;                // true = 从登录就开始截;默认只截战斗段

            public bool Active => Zone != 0;
        }

        private enum Stage { Login, Queue, Battle, Done }

        private static Options _current;
        private static bool _parsed;

        /// <summary>解析结果(懒解析一次)。未激活时 Active=false。</summary>
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

        private AppBootstrap _app;
        private GameClient _client;
        private BattleClient _battle;
        private Options _opt;
        private string _prefix;

        private Stage _stage = Stage.Login;
        private float _deadline;              // realtimeSinceStartup;0 = 无超时
        private bool _finished;
        private bool _queuedSeen;
        private string _lastBattleError;
        private ulong _battleId;
        private int _turns;
        private uint _lastRound;

        // 截图状态:_shotMarker 是"下一张截图的标签",由战斗事件打点,截完即清
        private bool _shotRunning;
        private int _shotSeq;
        private string _shotMarker = "boot";

        // ── 参数解析 ──────────────────────────────────────

        private static Options ResolveOptions()
        {
            var opt = Parse(Environment.GetCommandLineArgs(), skipFirst: true);
            if (opt.Active) return opt;

            // 无命令行(编辑器/手动起播放器)时的调试入口:环境变量 → PlayerPrefs
            string line = null;
            try { line = Environment.GetEnvironmentVariable(ArgsEnvVar); } catch { }
            if (string.IsNullOrWhiteSpace(line))
            {
                try { line = PlayerPrefs.GetString(EditorArgsPrefKey, string.Empty); } catch { }
            }
            if (string.IsNullOrWhiteSpace(line)) return opt;
            return Parse(Tokenize(line), skipFirst: false);
        }

        /// <summary>解析 argv:支持 "-key value" 与 "-key=value";未知参数(Unity 自身的)忽略。</summary>
        public static Options Parse(IReadOnlyList<string> args, bool skipFirst)
        {
            var opt = new Options();
            if (args == null) return opt;
            for (int i = skipFirst ? 1 : 0; i < args.Count; i++)
            {
                string raw = args[i];
                if (string.IsNullOrEmpty(raw) || raw[0] != '-') continue;
                string key = raw.TrimStart('-');
                string inlineValue = null;
                int eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    inlineValue = key.Substring(eq + 1);
                    key = key.Substring(0, eq);
                }

                string NextValue()
                {
                    if (inlineValue != null) return inlineValue;
                    if (i + 1 < args.Count && !(args[i + 1].StartsWith("-") && args[i + 1].Length > 1 && !char.IsDigit(args[i + 1][1])))
                    {
                        i++;
                        return args[i];
                    }
                    return null;
                }

                switch (key.ToLowerInvariant())
                {
                    case "gateway":         opt.Gateway = NextValue(); break;
                    case "zone":            opt.Zone = ParseUInt(NextValue()); break;
                    case "account":         opt.Account = NextValue(); break;
                    case "password":        opt.Password = NextValue(); break;
                    case "autoqueue":       opt.AutoQueue = NextValue()?.ToLowerInvariant(); break;
                    case "battleconfig":    opt.BattleConfig = ParseUInt(NextValue()); break;
                    case "autobattle":      opt.AutoBattle = true; break;
                    case "quitonbattleend": opt.QuitOnBattleEnd = true; break;
                    case "logtag":          opt.LogTag = NextValue(); break;
                    case "logintimeout":    opt.LoginTimeout = ParseFloat(NextValue(), opt.LoginTimeout); break;
                    case "queuetimeout":    opt.QueueTimeout = ParseFloat(NextValue(), opt.QueueTimeout); break;
                    case "battletimeout":   opt.BattleTimeout = ParseFloat(NextValue(), opt.BattleTimeout); break;
                    case "shotdir":         opt.ShotDir = NextValue(); break;
                    case "shotinterval":    opt.ShotInterval = ParseFloat(NextValue(), opt.ShotInterval); break;
                    case "shotsupersize":   opt.ShotSuperSize = (int)ParseUInt(NextValue()); break;
                    case "shotmax":         opt.ShotMax = (int)ParseUInt(NextValue()); break;
                    case "shotall":         opt.ShotAll = true; break;
                }
            }
            if (string.IsNullOrEmpty(opt.LogTag))
                opt.LogTag = string.IsNullOrEmpty(opt.Account) ? "auto" : opt.Account;
            return opt;
        }

        private static uint ParseUInt(string s)
            => uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0u;

        private static float ParseFloat(string s, float fallback)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0f ? v : fallback;

        /// <summary>把一整行参数切成 argv(支持双引号包住带空格的值)。</summary>
        private static List<string> Tokenize(string line)
        {
            var result = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in line)
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

        // ── 挂接 ─────────────────────────────────────────

        /// <summary>
        /// AppBootstrap 构造 SessionModel 后调用:自动模式下 gateway/account
        /// 以命令行为准,覆盖 PlayerPrefs 里的默认值(不回写 PlayerPrefs,
        /// 双实例共用同一份 PlayerPrefs,不能互相污染)。
        /// </summary>
        public static void ApplyToSession(SessionModel session)
        {
            var opt = Current;
            if (session == null || opt == null || !opt.Active) return;
            if (!string.IsNullOrEmpty(opt.Gateway)) session.GatewayBaseUrl = opt.Gateway;
            if (!string.IsNullOrEmpty(opt.Account)) session.Account = opt.Account;
            if (opt.Password != null) session.Password = opt.Password;
        }

        /// <summary>AppBootstrap 在 GameClient 创建后调用;未激活时不挂组件,返回 null。</summary>
        public static DevAutoPilot Attach(AppBootstrap app)
        {
            if (app == null || !IsActive) return null;
            var existing = app.GetComponent<DevAutoPilot>();
            if (existing != null) return existing;
            var pilot = app.gameObject.AddComponent<DevAutoPilot>();
            pilot._app = app;
            pilot._opt = Current;
            pilot._prefix = $"[AutoPilot][{pilot._opt.LogTag}]";
            return pilot;
        }

        // ── 生命周期 ──────────────────────────────────────

        private void Start()
        {
            if (_app == null) _app = GetComponent<AppBootstrap>() ?? AppBootstrap.Instance;
            if (_opt == null) _opt = Current;
            if (_prefix == null) _prefix = $"[AutoPilot][{_opt?.LogTag}]";
            if (_app == null || _app.GameClient == null || _opt == null || !_opt.Active)
            {
                Fail("boot", "AppBootstrap/GameClient 未就绪或参数未激活");
                return;
            }

            // 双实例前提:失焦的实例也要跑 Update,否则收不到网络消息
            // (ProjectSettings.runInBackground 也已置 1,这里是双保险)
            Application.runInBackground = true;

            // -shotAll:从登录就开始截(默认只截战斗段,见 HandleBattleStart)
            if (_opt.ShotAll) BeginShots();

            _client = _app.GameClient;
            _battle = _client.Battle;

            // 静默选角:无角色按默认职业建号,有角色进第一个(见 GameClient.PlayerChooser 注释)
            _client.PlayerChooser = null;

            _client.OnDisconnected += HandleDisconnected;
            if (_battle != null)
            {
                _battle.OnPhaseChanged += HandlePhaseChanged;
                _battle.OnBattleStart += HandleBattleStart;
                _battle.OnTurnResult += HandleTurnResult;
                _battle.OnBattleEnd += HandleBattleEnd;
                _battle.OnError += HandleBattleError;
                // 自动战斗记忆:BattleStart 时 BattleClient 自动补发 SetAutoBattle(true)
                if (_opt.AutoBattle) _battle.AutoBattleLatched = true;
            }

            Log($"stage=login zone={_opt.Zone} account={_opt.Account} gateway={_app.Session?.GatewayBaseUrl} " +
                $"autoQueue={_opt.AutoQueue ?? "-"} battleConfig={(_opt.BattleConfig.HasValue ? _opt.BattleConfig.Value.ToString() : "default")} " +
                $"autoBattle={_opt.AutoBattle} quitOnBattleEnd={_opt.QuitOnBattleEnd} " +
                $"timeouts(login/queue/battle)={_opt.LoginTimeout}/{_opt.QueueTimeout}/{_opt.BattleTimeout}s");

            if (string.IsNullOrEmpty(_opt.Account) || _opt.Password == null)
            {
                Fail("login", "缺少 -account / -password");
                return;
            }

            _stage = Stage.Login;
            _deadline = Time.realtimeSinceStartup + _opt.LoginTimeout;
            // deviceId 用账号:双实例同机 SystemInfo.deviceUniqueIdentifier 相同,按任务约定区分
            _app.Run(_client.EnterZone(_opt.Zone, _opt.Account, _opt.Password, _opt.Account,
                HandleEnterSuccess, err => Fail("login", err)));
        }

        private void Update()
        {
            if (_finished || _deadline <= 0f) return;
            if (Time.realtimeSinceStartup < _deadline) return;
            switch (_stage)
            {
                case Stage.Login:  Fail("login", $"登录/进场景超时({_opt.LoginTimeout}s)"); break;
                case Stage.Queue:  Fail("queue", $"排队等 BattleStart 超时({_opt.QueueTimeout}s) phase={_battle?.Phase}"); break;
                case Stage.Battle: Fail("battle", $"战斗超时({_opt.BattleTimeout}s) battle_id={_battleId} turns={_turns} phase={_battle?.Phase}"); break;
                default: _deadline = 0f; break;
            }
        }

        private void OnDestroy()
        {
            if (_client != null) _client.OnDisconnected -= HandleDisconnected;
            if (_battle != null)
            {
                _battle.OnPhaseChanged -= HandlePhaseChanged;
                _battle.OnBattleStart -= HandleBattleStart;
                _battle.OnTurnResult -= HandleTurnResult;
                _battle.OnBattleEnd -= HandleBattleEnd;
                _battle.OnError -= HandleBattleError;
            }
        }

        // ── 阶段推进 ──────────────────────────────────────

        private void HandleEnterSuccess()
        {
            if (_finished) return;
            // gate 地址是"真落在哪个 zone"的运行时证据(脚本据此断言 A/B 不同 gate)
            Log($"stage=in_game player_id={_client.PlayerId} scene_id={_client.CurrentSceneId} " +
                $"scene_config={_client.CurrentSceneConfigId} gate={_client.AssignedGate ?? "-"}");
            _app.Ugui?.SetServerSelectVisible(false); // 选区屏不再遮挡战斗层

            if (string.IsNullOrEmpty(_opt.AutoQueue))
            {
                _stage = Stage.Done;
                _deadline = 0f;
                Log("RESULT=PASS stage=in_game (no -autoQueue, staying in scene)");
                return;
            }
            if (_battle == null)
            {
                Fail("queue", "BattleClient 未初始化");
                return;
            }

            Match.MatchMode mode;
            switch (_opt.AutoQueue)
            {
                case "1v1": mode = Match.MatchMode._1V1; break;
                default:
                    Fail("queue", $"不支持的 -autoQueue={_opt.AutoQueue}(当前只支持 1v1)");
                    return;
            }
            // 队列按 (mode, battle_config_id) 分 key:必须与服务端 robot 跨区冒烟
            // (crossZoneBattleConfigId=1)一致才能凑到一起,默认取 BattleUiStyle 常量
            uint config = _opt.BattleConfig ?? UI.Ugui.Battle.BattleUiStyle.Pvp1V1BattleConfigId;

            // 上一轮被脚本强杀(不发 LeaveGame)时 scene 仍持 InBattleComp,登录后会推
            // BattleReconnectS2C 把相位拉到 WaitingAction/Resolving;此时 JoinQueue 会被
            // BattleClient 前置拒绝且不改相位,_queuedSeen 永远为 false,只能空等 60s。
            // 直接判失败并把真实原因写进 RESULT,不让它伪装成"匹配服务没响应"。
            if (_battle.Phase != BattlePhase.None)
            {
                Fail("queue", $"进场时战斗相位={_battle.Phase}(上次残留战斗/排队,需等 scene 侧 battle:lock 解冻后再跑)");
                return;
            }

            _stage = Stage.Queue;
            _queuedSeen = false;
            _lastBattleError = null;
            _deadline = Time.realtimeSinceStartup + _opt.QueueTimeout;
            Log($"stage=queue mode={_opt.AutoQueue} battle_config={config} player_id={_battle.MyPlayerId}");
            _battle.JoinQueue(mode, config);
        }

        private void HandlePhaseChanged(BattlePhase phase)
        {
            if (_finished) return;
            Log($"phase={phase}");
            if (_stage != Stage.Queue) return;
            if (phase == BattlePhase.Queued || phase == BattlePhase.Preparing) _queuedSeen = true;
            // 排队/准备阶段回落 None = 入队被拒 / 取消 / Preparing 15s 超时(BattleClient 已抛 OnError)
            if (phase == BattlePhase.None && _queuedSeen)
                Fail("queue", _lastBattleError ?? "排队相位回落 None");
        }

        private void HandleBattleStart(BattleStartS2C ev)
        {
            if (_finished || ev == null) return;
            _battleId = ev.BattleId != 0 ? ev.BattleId : ev.State?.BattleId ?? 0;
            _turns = 0;
            _lastRound = ev.State?.RoundIndex ?? 0;
            _stage = Stage.Battle;
            _deadline = Time.realtimeSinceStartup + _opt.BattleTimeout;
            if (_opt.AutoBattle && _battle != null) _battle.AutoBattleLatched = true; // 幂等,Start 已置
            MarkShot("start");
            BeginShots();   // 默认从开局开始截(已在跑则幂等)
            Log($"BattleStart battle_id={_battleId} round={_lastRound} actors={ev.State?.Actors.Count ?? 0} autoBattle={_opt.AutoBattle}");
        }

        private void HandleTurnResult(TurnResultS2C ev)
        {
            if (_finished || ev == null) return;
            if (_battleId != 0 && ev.BattleId != 0 && ev.BattleId != _battleId) return;
            _turns++;
            _lastRound = ev.RoundIndex;
            MarkShot($"turn{_turns}");
            Log($"Turn battle_id={_battleId} round={ev.RoundIndex} events={ev.Events.Count} turns={_turns}");
        }

        private void HandleBattleEnd(BattleEndS2C ev)
        {
            if (_finished || ev == null) return;
            // 登录时 scene 可能补推离线期间结束的上一局 BattleEnd(BattleClient 在
            // 无战斗上下文时已丢弃);这里再按 battle_id 过滤一次,只认本局
            if (_battleId == 0 || (ev.BattleId != 0 && ev.BattleId != _battleId))
            {
                Log($"ignore stale BattleEnd battle_id={ev.BattleId} (current={_battleId})");
                return;
            }
            MarkShot("end");
            Log($"BattleEnd battle_id={_battleId} outcome={ev.Outcome} turns={_turns} last_round={_lastRound}");
            // 对齐 robot 的 turn-count 断言:开局即终局(一回合没打)不算打完。
            // 已知会命中的既有缺陷:上一局阵亡玩家带 0 血再入队,引擎开局即判负。
            if (_turns < 1)
            {
                Fail("battle", $"zero_turns battle_id={_battleId} outcome={ev.Outcome}(开局即终局,期望 turns ≥ 1)");
                return;
            }
            Finish(true, $"RESULT=PASS battle_id={_battleId} outcome={ev.Outcome} turns={_turns} gate={_client?.AssignedGate ?? "-"}");
        }

        private void HandleBattleError(string message)
        {
            if (_finished) return;
            _lastBattleError = message;
            Debug.LogWarning($"{_prefix} battle error: {message}");
            // 排队阶段的错误若没伴随相位回落(JoinQueue 前置拒绝 / 迟到的 ErrInBattle 响应,
            // 相位仍是重连恢复的 WaitingAction 等),HandlePhaseChanged 永远等不到 None,
            // 这里直接判失败;正常的入队失败(Queued → None)仍由 HandlePhaseChanged 收口。
            if (_stage == Stage.Queue && _battle != null
                && _battle.Phase != BattlePhase.Queued && _battle.Phase != BattlePhase.Preparing)
            {
                Fail("queue", $"{message} phase={_battle.Phase}");
            }
        }

        private void HandleDisconnected()
        {
            if (_finished) return;
            Fail(_stage.ToString().ToLowerInvariant(), "与服务器断开连接");
        }

        // ── 收尾 ─────────────────────────────────────────

        private void Fail(string stage, string reason)
        {
            if (_finished) return;
            Finish(false, $"RESULT=FAIL stage={stage} reason={reason}");
        }

        private void Finish(bool ok, string line)
        {
            if (_finished) return;
            _finished = true;
            _stage = Stage.Done;
            _deadline = 0f;
            if (ok) Log(line); else Debug.LogError($"{_prefix} {line}");

            if (!_opt.QuitOnBattleEnd)
            {
                _shotRunning = false;   // 不退出也停截图,避免一直写盘
                return;
            }
            int code = ok ? 0 : 1;
            // 截图模式下先把结算演出多截几帧再退(退早了最后几张写不出来)
            if (_shotRunning && isActiveAndEnabled)
            {
                StartCoroutine(FinishWithShots(code, line));
                return;
            }
            QuitNow(code, line);
        }

        private void Log(string message) => Debug.Log($"{_prefix} {message}");

        // ── 演出截图 ──────────────────────────────────────
        // 目的:让"战斗表现"可被离线验收 —— 播放器跑一局真实战斗,按固定间隔 + 关键事件
        // (开局/每回合/终局)截帧到磁盘,再拿这些帧与参考录像逐项比对(阵型、伤害数字、
        // 特效、命令环)。默认关闭;只有传 -shotDir 才有任何开销。

        /// <summary>标记下一张截图的事件标签(截完即清回 tick)。</summary>
        private void MarkShot(string marker)
        {
            if (!ShotsEnabled) return;
            _shotMarker = marker;
        }

        private bool ShotsEnabled => _opt != null && !string.IsNullOrEmpty(_opt.ShotDir);

        private void BeginShots()
        {
            if (!ShotsEnabled || _shotRunning) return;
            try
            {
                System.IO.Directory.CreateDirectory(_opt.ShotDir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{_prefix} 截图目录创建失败,关闭截图: {_opt.ShotDir} {e.Message}");
                _opt.ShotDir = null;
                return;
            }
            _shotRunning = true;
            Log($"shots begin dir={_opt.ShotDir} interval={_opt.ShotInterval}s superSize={_opt.ShotSuperSize} max={_opt.ShotMax}");
            StartCoroutine(ShotLoop());
        }

        private System.Collections.IEnumerator ShotLoop()
        {
            var wait = new WaitForEndOfFrame();
            float next = 0f;
            while (_shotRunning && _shotSeq < _opt.ShotMax)
            {
                yield return wait;
                // 事件打点的帧立刻截,其余按间隔截
                bool marked = _shotMarker != null && _shotMarker != "tick";
                if (!marked && Time.realtimeSinceStartup < next) continue;
                CaptureNow(marked ? _shotMarker : "tick");
                next = Time.realtimeSinceStartup + Mathf.Max(0.05f, _opt.ShotInterval);
                _shotMarker = "tick";
            }
        }

        /// <summary>当帧结束时把屏幕写盘;必须在 WaitForEndOfFrame 之后调用。</summary>
        private void CaptureNow(string marker)
        {
            string safeTag = string.IsNullOrEmpty(_opt.LogTag) ? "auto" : _opt.LogTag;
            string name = $"{safeTag}_{_shotSeq:0000}_{marker}.png";
            try
            {
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_opt.ShotDir, name),
                    Mathf.Max(1, _opt.ShotSuperSize));
                _shotSeq++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{_prefix} 截图失败 {name}: {e.Message}");
            }
        }

        /// <summary>终局后再多截几帧(结算面板要时间弹出),然后才允许退出。</summary>
        private System.Collections.IEnumerator FinishWithShots(int code, string line)
        {
            var wait = new WaitForEndOfFrame();
            for (int i = 0; i < 6 && _shotSeq < _opt.ShotMax; i++)
            {
                yield return wait;
                CaptureNow(i == 0 ? "final" : "settle");
                // 结算演出是逐条飞入的,隔几帧再截下一张
                for (int f = 0; f < 12; f++) yield return null;
            }
            _shotRunning = false;
            Log($"shots done count={_shotSeq} dir={_opt.ShotDir}");
            QuitNow(code, line);
        }

        private void QuitNow(int code, string line)
        {
            Log($"quit exit_code={code}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
