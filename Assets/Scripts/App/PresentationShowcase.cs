using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Battle;
using UnityEngine;

namespace MmorpgClient.App
{
    /// <summary>
    /// 确定性演出验收台(离线,不连服务端):合成一场 5v5 回合战斗,按脚本把
    /// BattleStartS2C / TurnResultS2C×5 / BattleEndS2C 灌进既有 UI 链路,并逐帧截图,
    /// 供 turn-battle-presentation.md §1/§4 的观感项目(对角斜带 5v5 阵型、群攻全屏特效 +
    /// 全目标同拍飙血、暴击、MISS、治疗、buff 图标、耗蓝、死亡渐隐、命令环、结算)离线验收。
    ///
    /// 只有命令行带 -showcase 才激活;不激活时不挂组件、不改任何既有行为。
    ///   mmorpg.exe -screen-width 2560 -screen-height 1080 -showcase
    ///              -shotDir E:/work/tmp/showcase_shots [-shotInterval 0.25] [-shotMax 400]
    /// 编辑器调试:环境变量 MMORPG_SHOWCASE_ARGS 里放同格式的一整行参数。
    ///
    /// 注入点(不改任何既有契约):
    ///   - 自带 <see cref="ShowcaseTransport"/>(IBattleTransport 假实现,做法同
    ///     Assets/Tests/EditMode/Battle/FakeBattleTransport.cs),用
    ///     <see cref="BattleClient.Attach"/> 换掉 BattleClient.Instance,再用
    ///     RegisterNotify 注册的 S2C 通道推消息 —— BattleClient 的状态机契约原样跑;
    ///   - BattleUiRoot 只在 EnsureBound 里解析一次 BattleClient.Instance,故先销毁旧的
    ///     BattleUiRoot 再 EnsureSpawned(),让它重新绑到本台的假客户端上。
    ///
    /// 节奏:每回合推出去之后等 BattleClient 相位回到 WaitingAction(= BattleUiRoot
    /// 播完整段表现并 AckTurnPlayed)才推下一条,保证截图覆盖完整演出。
    /// 播完 BattleEnd 再多截几秒结算面板,然后 Application.Quit(0)。
    ///
    /// 截图自带一份轻量实现(不复用 DevAutoPilot):ReadPixels + EncodeToPNG 同步写盘,
    /// 文件名 show_&lt;seq4&gt;_&lt;marker&gt;.png。marker 直接编码本帧覆盖的验收点,便于对账。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresentationShowcase : MonoBehaviour
    {
        /// <summary>编辑器调试用:整行参数,格式与命令行一致。</summary>
        public const string ArgsEnvVar = "MMORPG_SHOWCASE_ARGS";

        private const string Tag = "[Showcase]";
        private const ulong ShowcaseBattleId = 880001UL;

        // 我方(team 0),ActorId = 90xx;9001 是"本人"(命令环/角色卡以它为准)
        private const ulong MeId = 9001UL;

        public sealed class Options
        {
            public bool Active;
            public string ShotDir;
            public float ShotInterval = 0.25f;
            public int ShotMax = 400;
            /// <summary>每回合最多等多少秒播完(兜底,防止卡死不出帧)。</summary>
            public float TurnTimeout = 40f;
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

        private Options _opt;
        private ShowcaseTransport _net;
        private BattleClient _client;

        // 合成战斗的可变模型:actorId → 权威状态(每回合克隆进 BattleStateS2C)
        private readonly List<BattleActorState> _actors = new List<BattleActorState>();
        private readonly Dictionary<ulong, BattleActorState> _byId = new Dictionary<ulong, BattleActorState>();
        private uint _round;

        // 截图状态
        private bool _shotRunning;
        private int _shotSeq;
        private string _marker;

        // ── 激活与参数 ────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (!IsActive) return;
            var go = new GameObject("[PresentationShowcase]");
            DontDestroyOnLoad(go);
            go.AddComponent<PresentationShowcase>();
        }

        private static Options ResolveOptions()
        {
            var opt = Parse(Environment.GetCommandLineArgs(), skipFirst: true);
            if (opt.Active) return opt;
            string line = null;
            try { line = Environment.GetEnvironmentVariable(ArgsEnvVar); } catch { }
            if (string.IsNullOrWhiteSpace(line)) return opt;
            return Parse(Tokenize(line), skipFirst: false);
        }

        /// <summary>解析 argv:支持 "-key value" 与 "-key=value";未知参数忽略。</summary>
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
                    case "showcase":     opt.Active = true; break;
                    case "shotdir":      opt.ShotDir = NextValue(); break;
                    case "shotinterval": opt.ShotInterval = ParseFloat(NextValue(), opt.ShotInterval); break;
                    case "shotmax":      opt.ShotMax = (int)ParseUInt(NextValue(), (uint)opt.ShotMax); break;
                    case "turntimeout":  opt.TurnTimeout = ParseFloat(NextValue(), opt.TurnTimeout); break;
                }
            }
            return opt;
        }

        private static uint ParseUInt(string s, uint fallback)
            => uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;

        private static float ParseFloat(string s, float fallback)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0f ? v : fallback;

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

        // ── 生命周期 ──────────────────────────────────────

        private void Start()
        {
            _opt = Current;
            if (_opt == null || !_opt.Active) { enabled = false; return; }
            Application.runInBackground = true;
            Application.targetFrameRate = 60;   // 演出按时长播,不需要跑飞
            StartCoroutine(CoRun());
        }

        private IEnumerator CoRun()
        {
            Debug.Log($"{Tag} begin shotDir={_opt.ShotDir ?? "-"} interval={_opt.ShotInterval}s max={_opt.ShotMax} " +
                      $"screen={Screen.width}x{Screen.height}");

            // 1) 等 AppBootstrap 起来(播放器里它在场景里,两帧足够)
            for (int i = 0; i < 3 && AppBootstrap.Instance == null; i++) yield return null;
            var app = AppBootstrap.Instance;
            if (app == null)
            {
                Debug.LogError($"{Tag} AppBootstrap 未就绪,放弃");
                Quit(1);
                yield break;
            }
            // 登录/选区屏收起,免得挡住战斗层(战斗 Canvas sortingOrder 200 本已在其上,双保险)
            app.Ugui?.SetServerSelectVisible(false);

            BeginShots();
            Mark("boot");
            yield return null;

            // 2) 把 BattleClient.Instance 换成本台的假客户端,并让 BattleUiRoot 重新绑定
            //    (BattleUiRoot 只在 EnsureBound 解析一次单例,故先销毁旧的再 EnsureSpawned)
            BuildActors();
            _net = new ShowcaseTransport
            {
                PlayerId = MeId,
                ResponseFor = ResolveFakeResponse,
            };
            var oldUi = BattleUiRoot.Instance;
            _client = BattleClient.Attach(_net);
            _client.AutoBattleLatched = false;   // 保持手动:命令环可见(录像里的核心 UI)
            if (oldUi != null)
            {
                Destroy(oldUi.gameObject);
                yield return null;
            }
            BattleUiRoot.EnsureSpawned();
            yield return null;   // BattleUiRoot.Awake
            yield return null;   // BattleUiRoot.Update → EnsureBound 绑到假客户端
            if (BattleUiRoot.Instance == null || BattleUiRoot.Instance.Client != _client)
            {
                Debug.LogWarning($"{Tag} BattleUiRoot 未绑到演出客户端(client={(BattleUiRoot.Instance?.Client == null ? "null" : "other")})");
            }

            // 3) 开局
            Mark("start_5v5");
            _net.Push(MessageIds.NotifyBattleStart, new BattleStartS2C
            {
                BattleId = ShowcaseBattleId,
                State = Snapshot(eBattleOutcome.BattleOutcomeOngoing),
            });
            Debug.Log($"{Tag} BattleStart actors={_actors.Count} phase={_client.Phase}");
            yield return WaitSeconds(1.6f);      // 入场演出(BattleScreen.PlayEntrance)

            // 4) 五个回合(覆盖点见各 marker;完整对照表在 CoverageLegend)
            foreach (var line in CoverageLegend) Debug.Log($"{Tag} cover {line}");

            yield return PlayTurn("r1_a-attack_b-crit_d-miss_i-defend", BuildRound1);
            yield return PlayTurn("r2_c-aoe5_g-mana_h-death", BuildRound2);
            yield return PlayTurn("r3_e-heal_f-buffadd_i-item", BuildRound3);
            yield return PlayTurn("r4_f-tick-remove_c-aoe_h-death", BuildRound4);
            yield return PlayTurn("r5_c-aoe-crit_h-wipe", BuildRound5);

            // 5) 终局 + 结算
            Mark("end_result");
            _net.Push(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = ShowcaseBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                Settlement = new BattleSettlementData
                {
                    BattleId = ShowcaseBattleId,
                    PlayerId = MeId,
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    PlayerTeamIndex = 0,
                    Health = HealthOf(MeId),
                    Mana = ManaOf(MeId),
                    ExpGain = 12800,
                    GoldGain = 3460,
                    TotalRounds = _round,
                    ItemsGained =
                    {
                        new BattleItemEntry { ItemTableId = 2001, Count = 3 },
                        new BattleItemEntry { ItemTableId = 2007, Count = 1 },
                        new BattleItemEntry { ItemTableId = 3105, Count = 2 },
                    },
                    ItemsConsumed = { new BattleItemEntry { ItemTableId = 2001, Count = 1 } },
                },
            });
            Debug.Log($"{Tag} BattleEnd outcome=SideAWin rounds={_round}");
            yield return WaitSeconds(2.5f);
            Mark("settle");
            yield return WaitSeconds(1.5f);

            _shotRunning = false;
            yield return null;
            Debug.Log($"{Tag} done frames={_shotSeq} dir={_opt.ShotDir ?? "-"}");
            Quit(0);
        }

        /// <summary>验收点对照(a–j;写进日志便于报告对账)。</summary>
        private static readonly string[] CoverageLegend =
        {
            "a=单体普攻(ATTACK+DAMAGE 同 group) → r1",
            "b=暴击(is_critical) → r1 / r5",
            "c=群攻(SKILL + 同 group 多 DAMAGE,hit_index 递增,5 目标) → r2 / r4 / r5",
            "d=MISS → r1",
            "e=HEAL → r3",
            "f=BUFF_ADD / BUFF_TICK(回血 tick) / BUFF_REMOVE → r3(add) r4(tick+remove)",
            "g=MANA(耗蓝,source==target) → r2 / r3",
            "h=DEATH(群攻中首个目标死亡,不拆拍) → r2 / r4 / r5",
            "i=DEFEND / ITEM → r1(defend) r3(item)",
            "j=多回合(5 回合)与 action_order 变化 → r1..r5",
        };

        private void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }

        // ── 回合推进 ──────────────────────────────────────

        /// <summary>推一个回合并等 BattleUiRoot 播完(相位 Resolving → WaitingAction)。</summary>
        private IEnumerator PlayTurn(string marker, Func<List<BattleEventItem>> build)
        {
            _round++;
            var events = build();
            bool ended = AllDead(1) || AllDead(0);
            var outcome = ended
                ? (AllDead(1) ? eBattleOutcome.BattleOutcomeSideAWin : eBattleOutcome.BattleOutcomeSideBWin)
                : eBattleOutcome.BattleOutcomeOngoing;

            var turn = new TurnResultS2C
            {
                BattleId = ShowcaseBattleId,
                RoundIndex = _round,
                State = Snapshot(outcome),
            };
            turn.Events.AddRange(events);
            turn.ActionOrder.AddRange(ActionOrderFor(_round));   // j:每回合出手序不同

            Mark(marker);
            _net.Push(MessageIds.NotifyTurnResult, turn);
            Debug.Log($"{Tag} turn round={_round} events={events.Count} order={string.Join(",", turn.ActionOrder)} " +
                      $"outcome={outcome} phase={_client.Phase}");

            float deadline = Time.realtimeSinceStartup + _opt.TurnTimeout;
            // 先等它真的进入 Resolving(同帧就会进),再等播完回到 WaitingAction
            while (_client.Phase == BattlePhase.Resolving && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (_client.Phase == BattlePhase.Resolving)
                Debug.LogWarning($"{Tag} round={_round} 播放超时({_opt.TurnTimeout}s),继续推下一回合");
            yield return WaitSeconds(0.6f);   // 落终态后留几帧给截图
        }

        /// <summary>按 realtime 等待(演出用 unscaled 时间,Time.timeScale 不参与)。</summary>
        private static IEnumerator WaitSeconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        // ── 合成战斗数据 ──────────────────────────────────

        private void BuildActors()
        {
            _actors.Clear();
            _byId.Clear();
            // team 0(我方,下方右下斜带):slot 0..4 前排 + 5..9 后排混用,覆盖前后两排
            AddActor(MeId,   0, "凌霄客",   62, 1, 8600, 3200, 980, 420, 610);
            AddActor(9002UL, 0, "白芷仙子", 60, 2, 6400, 5200, 380, 1180, 420);
            AddActor(9003UL, 0, "雷破天",   63, 3, 9200, 2600, 1120, 300, 700);
            AddActor(9004UL, 0, "素心娘",   59, 5, 5800, 6100, 300, 960, 380);
            AddActor(9005UL, 0, "玄石道人", 61, 7, 10400, 3000, 640, 720, 880);
            // team 1(敌方,左上斜带)
            AddActor(9101UL, 1, "血罗刹",   61, 1, 5200, 2400, 1040, 260, 520);
            AddActor(9102UL, 1, "蚀骨僧",   62, 2, 7400, 4800, 420, 1240, 560);
            AddActor(9103UL, 1, "黑风童子", 58, 3, 6100, 2200, 880, 340, 460);
            AddActor(9104UL, 1, "幽泉女",   60, 6, 5600, 5400, 320, 1080, 400);
            AddActor(9105UL, 1, "骨甲卫",   64, 8, 11200, 1800, 760, 200, 1040);
        }

        private void AddActor(ulong id, uint team, string name, uint level, uint slot,
            ulong hp, ulong mp, ulong physical, ulong magic, ulong defense)
        {
            var actor = new BattleActorState
            {
                ActorId = id,
                ActorType = eBattleActorType.BattleActorTypePlayer,
                TeamIndex = team,
                Name = name,
                Level = level,
                FormationSlot = slot,
                MaxHealth = hp,
                MaxMana = mp,
                PhysicalAttack = physical,
                MagicAttack = magic,
                Defense = defense,
                IsAuto = false,
                Attributes = new BaseAttributesComp
                {
                    Health = hp,
                    Mana = mp,
                    Strength = 120 + id % 40,
                    Stamina = 110 + id % 30,
                    Critchance = 850 + id % 150,
                    Armor = defense,
                    Resistance = defense / 2,
                    Speed = 300 + (ulong)(slot * 17) + id % 23,
                },
                SkillTableIds = { 1101u, 1207u, 1305u },
            };
            _actors.Add(actor);
            _byId[id] = actor;
        }

        /// <summary>当前模型的权威快照(深拷贝,避免 UI 持有的旧 state 被后续回合改写)。</summary>
        private BattleStateS2C Snapshot(eBattleOutcome outcome)
        {
            var state = new BattleStateS2C
            {
                BattleId = ShowcaseBattleId,
                RoundIndex = _round,
                Outcome = outcome,
                ActionDeadlineMs = 0,   // 0 = 无窗口 → PlaybackBudget 判 Unbounded,原速播全套演出
            };
            foreach (var actor in _actors) state.Actors.Add(actor.Clone());
            if (outcome == eBattleOutcome.BattleOutcomeOngoing)
            {
                // 本人未提交行动 → BattleClient 判 WaitingAction → 命令环亮起
                foreach (var actor in _actors)
                {
                    if (!actor.IsDead && actor.TeamIndex == 0) state.PendingActorIds.Add(actor.ActorId);
                }
            }
            return state;
        }

        private BattleActorState Actor(ulong id) => _byId.TryGetValue(id, out var a) ? a : null;
        private ulong HealthOf(ulong id) => Actor(id)?.Attributes?.Health ?? 0UL;
        private ulong ManaOf(ulong id) => Actor(id)?.Attributes?.Mana ?? 0UL;

        private bool AllDead(uint team)
        {
            foreach (var actor in _actors)
            {
                if (actor.TeamIndex == team && !actor.IsDead) return false;
            }
            return true;
        }

        /// <summary>出手序(D3):每回合轮转,让预告条可见地变化。</summary>
        private List<ulong> ActionOrderFor(uint round)
        {
            var alive = new List<ulong>();
            foreach (var actor in _actors)
            {
                if (!actor.IsDead) alive.Add(actor.ActorId);
            }
            if (alive.Count == 0) return alive;
            int shift = (int)(round % (uint)alive.Count);
            var order = new List<ulong>(alive.Count);
            for (int i = 0; i < alive.Count; i++) order.Add(alive[(i + shift) % alive.Count]);
            return order;
        }

        // ── 事件构造工具(顺带把模型改掉,保证 health_after 与快照自洽) ──

        /// <summary>扣血;返回结算后 HP。HP 归零时标记死亡。</summary>
        private ulong Hurt(ulong id, ulong amount)
        {
            var actor = Actor(id);
            if (actor == null) return 0UL;
            ulong hp = actor.Attributes.Health;
            hp = amount >= hp ? 0UL : hp - amount;
            actor.Attributes.Health = hp;
            if (hp == 0UL) actor.IsDead = true;
            return hp;
        }

        private ulong Heal(ulong id, ulong amount)
        {
            var actor = Actor(id);
            if (actor == null) return 0UL;
            ulong hp = Math.Min(actor.MaxHealth, actor.Attributes.Health + amount);
            actor.Attributes.Health = hp;
            return hp;
        }

        private ulong SpendMana(ulong id, ulong amount)
        {
            var actor = Actor(id);
            if (actor == null) return 0UL;
            ulong mp = amount >= actor.Attributes.Mana ? 0UL : actor.Attributes.Mana - amount;
            actor.Attributes.Mana = mp;
            return mp;
        }

        private static BattleEventItem Ev(eBattleEventType type, ulong source, ulong target, uint group)
            => new BattleEventItem { EventType = type, SourceId = source, TargetId = target, GroupId = group };

        /// <summary>ATTACK/SKILL 出手事件。</summary>
        private static BattleEventItem Cast(ulong source, uint group, uint skillId, bool isSkill)
        {
            var ev = Ev(isSkill ? eBattleEventType.BattleEventSkill : eBattleEventType.BattleEventAttack,
                source, 0UL, group);
            ev.SkillTableId = skillId;
            return ev;
        }

        private BattleEventItem Damage(ulong source, ulong target, uint group, ulong value, bool crit, uint hitIndex)
        {
            var ev = Ev(eBattleEventType.BattleEventDamage, source, target, group);
            ev.Value = value;
            ev.IsCritical = crit;
            ev.HitIndex = hitIndex;
            ev.TargetHealthAfter = Hurt(target, value);
            return ev;
        }

        private BattleEventItem HealEv(ulong source, ulong target, uint group, ulong value)
        {
            var ev = Ev(eBattleEventType.BattleEventHeal, source, target, group);
            ev.Value = value;
            ev.TargetHealthAfter = Heal(target, value);
            return ev;
        }

        /// <summary>MANA:source==target = 施法者耗蓝(TurnPlan.MergeMana 按此判负号)。</summary>
        private BattleEventItem ManaCost(ulong caster, uint group, ulong cost)
        {
            var ev = Ev(eBattleEventType.BattleEventMana, caster, caster, group);
            ev.Value = cost;
            ev.TargetManaAfter = SpendMana(caster, cost);
            return ev;
        }

        /// <summary>致命一击:伤害值取目标剩余 HP,飘字数值与"打空血条"自洽。</summary>
        private BattleEventItem Lethal(ulong source, ulong target, uint group, bool crit, uint hitIndex)
            => Damage(source, target, group, Math.Max(1UL, HealthOf(target)), crit, hitIndex);

        private static BattleEventItem Death(ulong victim, uint group)
            => Ev(eBattleEventType.BattleEventDeath, victim, victim, group);

        private BattleEventItem BuffAdd(ulong source, ulong target, uint group, uint buffTableId, uint rounds)
        {
            var ev = Ev(eBattleEventType.BattleEventBuffAdd, source, target, group);
            ev.BuffTableId = buffTableId;
            ev.Success = true;
            var actor = Actor(target);
            if (actor != null)
            {
                actor.Buffs.Add(new BattleBuffEntry
                {
                    BuffId = (ulong)buffTableId * 1000UL + target % 1000UL,
                    BuffTableId = buffTableId,
                    Layer = 1,
                    RemainRounds = rounds,
                    CasterId = source,
                });
            }
            return ev;
        }

        private BattleEventItem BuffRemove(ulong target, uint buffTableId)
        {
            var ev = Ev(eBattleEventType.BattleEventBuffRemove, target, target, 0u);
            ev.BuffTableId = buffTableId;
            var actor = Actor(target);
            if (actor != null)
            {
                for (int i = actor.Buffs.Count - 1; i >= 0; i--)
                {
                    if (actor.Buffs[i].BuffTableId == buffTableId) actor.Buffs.RemoveAt(i);
                }
            }
            return ev;
        }

        /// <summary>BUFF_TICK:heal=true 时 health_after &gt; 当前 HP(表现层据此按回血演出)。</summary>
        private BattleEventItem BuffTick(ulong target, uint buffTableId, ulong value, bool heal)
        {
            var ev = Ev(eBattleEventType.BattleEventBuffTick, target, target, 0u);
            ev.BuffTableId = buffTableId;
            ev.Value = value;
            ev.TargetHealthAfter = heal ? Heal(target, value) : Hurt(target, value);
            return ev;
        }

        // ── 五个回合的脚本 ────────────────────────────────

        /// <summary>a 单体普攻 / b 暴击 / d MISS / i DEFEND。</summary>
        private List<BattleEventItem> BuildRound1()
        {
            var e = new List<BattleEventItem>
            {
                // a:ATTACK + 同 group 的 DAMAGE
                Cast(MeId, 1, 0, false),
                Damage(MeId, 9101UL, 1, 860, false, 0),
                // d:敌方普攻打空
                Cast(9101UL, 2, 0, false),
                Ev(eBattleEventType.BattleEventMiss, 9101UL, MeId, 2),
                // b:暴击
                Cast(9003UL, 3, 0, false),
                Damage(9003UL, 9102UL, 3, 2140, true, 0),
                // i:防御
                Ev(eBattleEventType.BattleEventDefend, 9005UL, 9005UL, 4),
            };
            var defender = Actor(9005UL);
            if (defender != null) defender.IsDefending = true;
            return e;
        }

        /// <summary>c 群攻(5 目标,hit_index 递增)/ g 耗蓝 / h 群攻内首目标阵亡(不拆拍)。</summary>
        private List<BattleEventItem> BuildRound2()
        {
            var e = new List<BattleEventItem>
            {
                Cast(9002UL, 10, 1207u, true),
                ManaCost(9002UL, 10, 620),                      // g
                Lethal(9002UL, 9101UL, 10, false, 0),           // 首目标被打死
                Death(9101UL, 10),                              // h:挂到整拍之后,不拆拍
                Damage(9002UL, 9102UL, 10, 1180, false, 1),
                Damage(9002UL, 9103UL, 10, 1240, true, 2),
                Damage(9002UL, 9104UL, 10, 1090, false, 3),
                Damage(9002UL, 9105UL, 10, 960, false, 4),
            };
            var defender = Actor(9005UL);
            if (defender != null) defender.IsDefending = false;
            return e;
        }

        /// <summary>e HEAL / f BUFF_ADD / g 耗蓝 / i ITEM。</summary>
        private List<BattleEventItem> BuildRound3()
        {
            return new List<BattleEventItem>
            {
                // e:群体治疗技能(同 group 多 HEAL)
                Cast(9004UL, 20, 1305u, true),
                ManaCost(9004UL, 20, 480),
                HealEv(9004UL, MeId, 20, 1320),
                HealEv(9004UL, 9003UL, 20, 980),
                // f:上 buff(同 group 多 BUFF_ADD)
                Cast(9005UL, 21, 1101u, true),
                ManaCost(9005UL, 21, 260),
                BuffAdd(9005UL, MeId, 21, 3u, 3u),
                BuffAdd(9005UL, 9002UL, 21, 5u, 3u),
                BuffAdd(9005UL, 9003UL, 21, 7u, 3u),
                // 敌方也挂个 dot,供下回合 BUFF_TICK 用
                Cast(9104UL, 22, 1207u, true),
                BuffAdd(9104UL, MeId, 22, 11u, 2u),
                // i:使用道具(ITEM + 同 group 的 HEAL)
                ItemUse(9002UL, 23, 2001u),
                HealEv(9002UL, 9002UL, 23, 1500),
            };
        }

        private static BattleEventItem ItemUse(ulong source, uint group, uint itemTableId)
        {
            var ev = Ev(eBattleEventType.BattleEventItem, source, source, group);
            ev.ItemTableId = itemTableId;
            return ev;
        }

        /// <summary>f BUFF_TICK(回血 + dot)/ BUFF_REMOVE / c 敌方群攻 / h 阵亡。</summary>
        private List<BattleEventItem> BuildRound4()
        {
            var e = new List<BattleEventItem>
            {
                // c:敌方群攻,4 目标同拍(hit_index 全 0 = 全目标同拍飙血)
                Cast(9102UL, 30, 1207u, true),
                ManaCost(9102UL, 30, 700),
                Lethal(9102UL, 9005UL, 30, true, 0),         // h:群攻内阵亡
                Death(9005UL, 30),
                Damage(9102UL, 9004UL, 30, 1260, false, 0),
                Damage(9102UL, 9003UL, 30, 1340, false, 0),
                Damage(9102UL, MeId, 30, 1180, false, 0),
                // f:回合末周期效果 —— 回血 tick(HealthAfter > 当前 HP)与 dot tick
                BuffTick(MeId, 3u, 900, true),
                BuffTick(9103UL, 11u, 640, false),
                // f:buff 掉落
                BuffRemove(MeId, 11u),
            };
            return e;
        }

        /// <summary>c 暴击群攻收尾 / b 暴击 / h 敌方全灭 → 终局。</summary>
        private List<BattleEventItem> BuildRound5()
        {
            var e = new List<BattleEventItem>
            {
                Cast(9003UL, 40, 1101u, true),
                ManaCost(9003UL, 40, 900),
            };
            ulong[] victims = { 9102UL, 9103UL, 9104UL, 9105UL };
            foreach (ulong id in victims)
            {
                if (Actor(id) == null || Actor(id).IsDead) continue;
                e.Add(Lethal(9003UL, id, 40, true, 0));
                e.Add(Death(id, 40));
            }
            return e;
        }

        // ── 假响应(BattleClient 出站调用的兜底) ────────

        /// <summary>
        /// BattleClient 会发 SetAutoBattle / SubmitBattleAction / GetBattleState 等请求。
        /// 这里给出无错响应;GetBattleState 回当前权威快照,免得兜底补拉把状态打空。
        /// </summary>
        private IMessage ResolveFakeResponse(uint messageId)
        {
            if (messageId == MessageIds.GetBattleState) return Snapshot(eBattleOutcome.BattleOutcomeOngoing);
            return null;   // 其余用空响应(error_message 为空 = 成功)
        }

        // ── 截图 ─────────────────────────────────────────

        private bool ShotsEnabled => _opt != null && !string.IsNullOrEmpty(_opt.ShotDir);

        /// <summary>给下一帧的截图打标签(截完即清)。</summary>
        private void Mark(string marker)
        {
            if (!ShotsEnabled) return;
            _marker = marker;
        }

        private void BeginShots()
        {
            if (!ShotsEnabled || _shotRunning) return;
            try
            {
                Directory.CreateDirectory(_opt.ShotDir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} 截图目录创建失败,关闭截图: {_opt.ShotDir} {e.Message}");
                _opt.ShotDir = null;
                return;
            }
            _shotRunning = true;
            StartCoroutine(ShotLoop());
        }

        private IEnumerator ShotLoop()
        {
            var wait = new WaitForEndOfFrame();
            float next = 0f;
            while (_shotRunning && _shotSeq < _opt.ShotMax)
            {
                yield return wait;
                bool marked = !string.IsNullOrEmpty(_marker);
                if (!marked && Time.realtimeSinceStartup < next) continue;
                string marker = marked ? _marker : "tick";
                _marker = null;
                Capture(marker);
                next = Time.realtimeSinceStartup + Mathf.Max(0.05f, _opt.ShotInterval);
            }
        }

        /// <summary>
        /// 同步截屏写盘(必须在 WaitForEndOfFrame 之后调用)。
        /// 不用 ScreenCapture.CaptureScreenshot:那条路是异步的,batchmode/退出竞态下经常一张都写不出来。
        /// </summary>
        private void Capture(string marker)
        {
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return;
            string name = $"show_{_shotSeq:0000}_{marker}.png";
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                File.WriteAllBytes(Path.Combine(_opt.ShotDir, name), tex.EncodeToPNG());
                _shotSeq++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} 截图失败 {name}: {e.Message}");
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
        }

        // ── 假传输层 ─────────────────────────────────────

        /// <summary>
        /// <see cref="IBattleTransport"/> 的演出台实现:出站请求就地成功返回(可由
        /// <see cref="ResponseFor"/> 定制),入站推送由 <see cref="Push"/> 走 RegisterNotify
        /// 注册的真实解析路径(payload 序列化 → MessageContent → Parser.ParseFrom),
        /// 与生产链路同构。做法同 Assets/Tests/EditMode/Battle/FakeBattleTransport.cs。
        /// </summary>
        private sealed class ShowcaseTransport : IBattleTransport
        {
            private readonly Dictionary<uint, Action<MessageContent>> _notify = new Dictionary<uint, Action<MessageContent>>();

            public ulong PlayerId { get; set; } = MeId;
            public bool IsReady => true;

            /// <summary>按 message_id 给出响应体;返回 null 表示用空响应。</summary>
            public Func<uint, IMessage> ResponseFor;

#pragma warning disable 0067 // 演出台永不断线,事件仅为满足接口
            public event Action Disconnected;
#pragma warning restore 0067

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _notify[messageId] = handler;

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                                    Action<TResp> onResponse, Action<string> onError)
                where TResp : IMessage<TResp>
            {
                IMessage payload = null;
                try { payload = ResponseFor?.Invoke(messageId); }
                catch (Exception e) { Debug.LogWarning($"{Tag} 假响应构造失败 id={messageId}: {e.Message}"); }
                try
                {
                    onResponse(parser.ParseFrom(payload != null ? payload.ToByteString() : ByteString.Empty));
                }
                catch (Exception e)
                {
                    onError?.Invoke(e.Message);
                }
            }

            public void SendOneWay(uint messageId, IMessage request) { }

            /// <summary>模拟服务端 S2C 推送。</summary>
            public void Push(uint messageId, IMessage payload)
            {
                if (!_notify.TryGetValue(messageId, out var handler))
                {
                    Debug.LogError($"{Tag} 没有注册 message_id={messageId} 的推送处理器");
                    return;
                }
                handler(new MessageContent
                {
                    MessageId = messageId,
                    SerializedMessage = payload.ToByteString(),
                });
            }
        }
    }
}
