using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;
using MmorpgClient.Game.Battle.Presentation;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 回合演出驱动(turn-battle-presentation.md §3 BattlePresenter):
    /// TurnPlan 的拍序列由 <see cref="BattleSequencer"/> 计时,本类把每一拍翻译成
    /// BattleUnitView 动作 + BattleFxPlayer 特效 + DamageNumberPool 数字 + 震屏/顿帧,并产出战斗记录行。
    ///
    ///  - Attack 拍:出手者冲到目标前 → 命中帧:目标 PlayHit + 伤害数字 + 命中特效(slash_arc/hit_star)→ 回位;
    ///  - Cast 拍:出手者 PlayCast → 释放:技能特效(BattleArtCatalog.FxForSkill)覆盖所有目标(并行),
    ///    特效命中帧全部目标同一拍飙血;Heal → heal_ring;BuffAdd → buff_rise + 图标入场;Miss → 闪避 + "闪";
    ///  - Death → PlayDeath(death_dissolve);Mana → 蓝条缓动;Defend → 护盾闪金;Flee → 冲出/缩回;
    ///  - 暴击:数字放大 1.3(DamageNumberLayout.CritScale)+ 顿帧 0.08s(BattleHitStop)+ 震屏 6px(BattleCameraFx);
    ///  - BuffTick:无 buff 表可查,按 target_health_after 与单位当前 HP 比较决定回血(heal_ring + 绿 "+N")还是掉血;
    ///  - SpeedScale 同步写入 <see cref="BattleTempo"/>:拍时长、单位动作、多段错拍、特效帧率、数字寿命同倍率缩放;
    ///  - Skip():剩余拍只落终态(HP/MP/死亡),供 BattleScreen 在演出预算不足(PlaybackBudget)时调用;
    ///    Abort():复位所有 tween/飘字/特效/残影/舞台位移(观战抢占/断线)。
    /// 播完 OnFinished(由 BattleScreen 转给 BattleUiRoot → AckTurnPlayed);本类不碰 BattleClient。
    /// </summary>
    public sealed class BattlePresenter
    {
        /// <summary>自动战斗时的基础播放倍率(spec §3:1.5-2.0;预算不足时由 PlaybackBudget 再往上压)。</summary>
        public const float AutoBattleSpeed = 1.5f;
        public const float SingleTargetFxSize = 300f;
        public const float MultiTargetFxSize = 380f;
        /// <summary>多段攻击每段错开的秒数。</summary>
        public const float MultiHitStagger = 0.12f;

        public event Action OnFinished;
        public event Action OnAborted;
        /// <summary>(beat, index) 拍开始(HUD 高亮行动预告)。</summary>
        public event Action<Beat, int> OnBeatStarted;
        /// <summary>战斗记录行。</summary>
        public event Action<string> OnLog;
        /// <summary>(actorId, hp) 播放中 HP 变化(右上角色卡同步)。</summary>
        public event Action<ulong, ulong> OnHealthChanged;
        /// <summary>(actorId, mp) 播放中 MP 变化。</summary>
        public event Action<ulong, ulong> OnManaChanged;

        public BattleFxPlayer Fx { get; }
        public DamageNumberPool Numbers { get; }
        /// <summary>残影池(舞台层,单位注入后冲刺/逃跑用)。</summary>
        public BattleAfterimagePool Ghosts { get; }
        public BattleCameraFx Camera { get; }
        public bool IsPlaying => _sequencer.IsRunning;
        public Beat CurrentBeat => _sequencer.CurrentBeat;

        /// <summary>播放倍率:拍序列与全部表现(BattleTempo)同步缩放。</summary>
        public float SpeedScale
        {
            get => _sequencer.SpeedScale;
            set
            {
                _sequencer.SpeedScale = value;
                BattleTempo.Speed = _sequencer.SpeedScale;
            }
        }

        private readonly MonoBehaviour _runner;
        private readonly RectTransform _overlayLayer;
        private readonly Func<ulong, BattleUnitView> _resolveView;
        private readonly Func<IEnumerable<BattleUnitView>> _allViews;
        private readonly Func<ulong, string> _resolveName;
        private readonly BattleSequencer _sequencer = new BattleSequencer();
        private readonly List<GameObject> _transient = new List<GameObject>();
        private readonly object _token = new object();
        private bool _fastForward;

        public BattlePresenter(MonoBehaviour runner, RectTransform stageRoot, RectTransform fxLayer,
            RectTransform numberLayer, RectTransform overlayLayer,
            Func<ulong, BattleUnitView> resolveView, Func<IEnumerable<BattleUnitView>> allViews, Func<ulong, string> resolveName,
            RectTransform shakeRoot = null)
        {
            _runner = runner;
            _overlayLayer = overlayLayer;
            _resolveView = resolveView;
            _allViews = allViews;
            _resolveName = resolveName;
            Fx = new BattleFxPlayer(fxLayer);
            Numbers = new DamageNumberPool(numberLayer);
            Ghosts = new BattleAfterimagePool(stageRoot);
            // 震屏作用在 shakeRoot(舞台+特效+名牌+数字的共同父节点),没给就只震舞台层
            Camera = new BattleCameraFx(shakeRoot != null ? shakeRoot : stageRoot);

            _sequencer.OnBeatStart += HandleBeatStart;
            _sequencer.OnBeatEnd += HandleBeatEnd;
            _sequencer.OnFinished += () => OnFinished?.Invoke();
            _sequencer.OnAborted += () => OnAborted?.Invoke();
        }

        // ── 播放控制 ─────────────────────────────────────────

        /// <summary>开始播放一回合(空计划立即 OnFinished)。</summary>
        public void Play(TurnPlan plan)
        {
            _fastForward = false;
            _sequencer.Run(plan);
        }

        /// <summary>跳过剩余表现:剩余拍只落终态。</summary>
        public void Skip()
        {
            if (!_sequencer.IsRunning) return;
            _fastForward = true;
            ClearTransient();
            _sequencer.Skip();
        }

        /// <summary>静默终止并复位一切表现(观战抢占/断线/关屏)。</summary>
        public void Abort()
        {
            _fastForward = false;
            _sequencer.Abort();
            ClearTransient();
            ResetViews();
        }

        public void Dispose()
        {
            Abort();
            Fx.Dispose();
            Numbers.Dispose();
            Ghosts.Dispose();
        }

        /// <summary>只清演出残留(特效/数字/残影/一次性覆盖层/震屏),不动单位状态。</summary>
        public void ClearTransient()
        {
            GTween.Kill(_token);
            Fx.Clear();
            Numbers.Clear();
            Ghosts.Clear();
            Camera.Reset();
            foreach (var go in _transient)
            {
                if (go == null) continue;
                GTween.Kill(go);
                UnityEngine.Object.Destroy(go);
            }
            _transient.Clear();
        }

        private void ResetViews()
        {
            var views = _allViews?.Invoke();
            if (views == null) return;
            foreach (var view in views) view?.ResetVisual();
        }

        // ── 开场 / 收尾 ──────────────────────────────────────

        /// <summary>开场:入场云层扫过 + 每个单位脚下出生光环展开。</summary>
        public void PlayEntrance(IEnumerable<BattleUnitView> views)
        {
            if (_overlayLayer == null) return;
            var clouds = BattleArtCatalog.LoadEntryClouds();
            if (clouds != null)
            {
                float w = QdaoUguiTheme.DesignWidth, h = QdaoUguiTheme.DesignHeight;
                var image = QdaoUguiFactory.CreateImage("EntryClouds", _overlayLayer, w, 0f, w * 1.2f, h, clouds);
                image.preserveAspect = false;
                image.raycastTarget = false;
                var rect = image.rectTransform;
                var go = image.gameObject;
                _transient.Add(go);
                GTween.To(w, -w * 1.2f, 1.3f).SetEase(EaseType.QuadInOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                    .OnUpdate((GTweenCallback1)(t =>
                    {
                        if (go == null) { GTween.Kill(go); return; }
                        rect.anchoredPosition = new Vector2(t.value.x, 0f);
                        float p = 1f - Mathf.Abs(t.value.x + w * 0.1f) / (w * 1.1f);
                        image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(p * 1.6f));
                    }))
                    .OnComplete((GTweenCallback)(() => DestroyTransient(go)));
            }

            if (views == null) return;
            int index = 0;
            foreach (var view in views)
            {
                if (view == null) continue;
                var ring = BattleArtCatalog.LoadSpawnRing(view.TeamIsMine);
                if (ring == null) continue;
                float size = 300f * Mathf.Max(0.6f, view.Scale);
                var foot = view.FootPosition;
                var image = QdaoUguiFactory.CreateImage("SpawnRing", _overlayLayer, foot.x - size * 0.5f, foot.y - size * 0.25f, size, size * 0.5f, ring);
                image.preserveAspect = false;
                image.raycastTarget = false;
                var rect = image.rectTransform;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition += new Vector2(size * 0.5f, -size * 0.25f);
                rect.localScale = Vector3.zero;
                var go = image.gameObject;
                _transient.Add(go);
                float delay = 0.25f + 0.05f * index++;
                GTween.To(0f, 1f, 0.45f).SetDelay(delay).SetEase(EaseType.BackOut).SetIgnoreEngineTimeScale(true).SetTarget(go)
                    .OnUpdate((GTweenCallback1)(t =>
                    {
                        if (go == null) { GTween.Kill(go); return; }
                        rect.localScale = new Vector3(t.value.x, t.value.x, 1f);
                    }))
                    .OnComplete((GTweenCallback)(() =>
                    {
                        if (go == null) return;
                        GTween.To(1f, 0f, 0.5f).SetDelay(0.35f).SetEase(EaseType.QuadIn).SetIgnoreEngineTimeScale(true).SetTarget(go)
                            .OnUpdate((GTweenCallback1)(t2 =>
                            {
                                if (go == null) { GTween.Kill(go); return; }
                                image.color = new Color(1f, 1f, 1f, t2.value.x);
                            }))
                            .OnComplete((GTweenCallback)(() => DestroyTransient(go)));
                    }));
            }
        }

        /// <summary>胜利方存活单位做胜利姿势。</summary>
        public void PlayVictory(IEnumerable<BattleUnitView> views, bool winnersAreMine)
        {
            if (views == null) return;
            foreach (var view in views)
            {
                if (view == null || view.TeamIsMine != winnersAreMine || view.IsDead || view.Fled) continue;
                view.PlayWin();
            }
        }

        // ── 拍处理 ──────────────────────────────────────────

        private void HandleBeatStart(Beat beat, int index)
        {
            OnBeatStarted?.Invoke(beat, index);
            if (_fastForward) return;

            var actor = beat.ActorId != 0 ? _resolveView(beat.ActorId) : null;
            switch (beat.Kind)
            {
                case BeatKind.Attack:
                    PlayAttack(beat, actor);
                    break;
                case BeatKind.Cast:
                    PlayCast(beat, actor, $"施放 技能{beat.SkillId}");
                    break;
                case BeatKind.Item:
                    PlayCast(beat, actor, $"使用 道具{beat.ItemId}");
                    break;
                case BeatKind.Death:
                    Log($"{Name(beat.ActorId)} 阵亡");
                    if (actor != null) actor.PlayDeath();
                    break;
                case BeatKind.Defend:
                    Log($"{Name(beat.ActorId)} 进入防御");
                    actor?.PlayDefend();
                    break;
                case BeatKind.Flee:
                    Log($"{Name(beat.ActorId)} {(beat.Success ? "逃跑成功" : "逃跑失败")}");
                    actor?.PlayFlee(beat.Success);
                    break;
                case BeatKind.BuffTick:
                    Log($"{Name(beat.ActorId)} 的 buff{beat.BuffId} 生效");
                    ApplyOutcomes(beat);
                    break;
                default:
                    // 孤立 Hit/Miss/Heal/BuffAdd/BuffRemove/Mana:直接结算
                    ApplyOutcomes(beat);
                    break;
            }

            if (beat.HasActorManaAfter && actor != null)
            {
                actor.SetManaDuringPlayback(beat.ActorManaAfter);
                OnManaChanged?.Invoke(beat.ActorId, beat.ActorManaAfter);
            }
        }

        /// <summary>拍结束:无论表现是否播完(Skip),把本拍终态落下,保证与权威状态衔接。</summary>
        private void HandleBeatEnd(Beat beat, int index)
        {
            foreach (var target in beat.Targets)
            {
                var view = _resolveView(target.ActorId);
                if (view == null) continue;
                if (target.HasHealthAfter)
                {
                    view.SetHealthDuringPlayback(target.HealthAfter);
                    OnHealthChanged?.Invoke(target.ActorId, target.HealthAfter);
                }
                if (target.HasManaAfter)
                {
                    view.SetManaDuringPlayback(target.ManaAfter);
                    OnManaChanged?.Invoke(target.ActorId, target.ManaAfter);
                }
            }
            if (_fastForward)
            {
                if (beat.Kind == BeatKind.Death) _resolveView(beat.ActorId)?.ShowDeadMark();
                if (beat.HasActorManaAfter) _resolveView(beat.ActorId)?.SetManaDuringPlayback(beat.ActorManaAfter);
            }
        }

        private void PlayAttack(Beat beat, BattleUnitView actor)
        {
            var first = FirstTargetView(beat);
            Log(first != null ? $"{Name(beat.ActorId)} 攻击 {Name(first.ActorId)}" : $"{Name(beat.ActorId)} 攻击");
            if (actor == null)
            {
                ApplyOutcomes(beat);
                return;
            }
            string fxId = BattleArtCatalog.FxForSkill(beat.SkillId, true);
            var targetFoot = first != null ? first.FootPosition : actor.FootPosition;
            bool mirror = !actor.FacingEast;
            actor.PlayAttackLunge(targetFoot, () =>
            {
                foreach (var view in DistinctTargetViews(beat))
                    Fx.Play(fxId, view.ChestPosition, SingleTargetFxSize * Mathf.Max(0.6f, view.Scale), 0f, null, BattleFxPlayer.DefaultHitFrame, mirror);
                ApplyOutcomes(beat);
            });
        }

        private void PlayCast(Beat beat, BattleUnitView actor, string verb)
        {
            Log($"{Name(beat.ActorId)} {verb}");
            if (actor == null)
            {
                ApplyOutcomes(beat);
                return;
            }
            var pres = BattleArtCatalog.ResolveSkillFx(beat.SkillId, false);
            bool mirror = !actor.FacingEast;
            actor.PlayCast(() =>
            {
                var targets = DistinctTargetViews(beat);
                if (targets.Count == 0)
                {
                    ApplyOutcomes(beat);
                    return;
                }
                bool applied = false;
                Action applyOnce = () =>
                {
                    if (applied) return;
                    applied = true;
                    ApplyOutcomes(beat);
                };
                float size = targets.Count > 1 ? MultiTargetFxSize : SingleTargetFxSize;
                bool anyDamage = false;
                foreach (var t in beat.Targets)
                {
                    if (t.Effect == TargetEffect.Damage || t.Effect == TargetEffect.Miss || t.Effect == TargetEffect.Block) { anyDamage = true; break; }
                }
                if (!anyDamage)
                {
                    // 纯治疗/上 buff 技能:目标特效由 ApplyOutcomes 各自铺(heal_ring/buff_rise),不再叠攻击特效
                    applyOnce();
                    return;
                }
                foreach (var view in targets)
                {
                    Fx.Play(pres.FxId, view.ChestPosition, size * Mathf.Max(0.6f, view.Scale), 0f, applyOnce, pres.HitFrame, mirror);
                }
            });
        }

        /// <summary>
        /// 本拍所有目标**同一拍**结算表现(spec §1:群攻所有目标同一拍飙血,数字按目标错位不重叠);
        /// 只有同一目标被打多段(同 target 多条、hit_index 递增)才按段错拍 <see cref="MultiHitStagger"/>。
        /// hit_index 在不同目标之间递增(按目标序编号)时不当作多段,否则 5 目标群攻会拖成 0.5s 的连珠
        /// (2026-09-04 帧验收:c-aoe5 命中拍只见 2 串数字)。
        /// </summary>
        private void ApplyOutcomes(Beat beat)
        {
            bool anyCrit = false;
            var perTargetSeen = new Dictionary<ulong, int>();
            var targetOrdinal = new Dictionary<ulong, int>();
            foreach (var target in beat.Targets)
            {
                var view = _resolveView(target.ActorId);
                // 同一目标第 n 次出现 → 第 n 段;不同目标各自从 0 起
                perTargetSeen.TryGetValue(target.ActorId, out int segment);
                perTargetSeen[target.ActorId] = segment + 1;
                float delay = segment * MultiHitStagger;
                // 目标序号(按首次出现),用于同拍数字的水平错位
                if (!targetOrdinal.TryGetValue(target.ActorId, out int ordinal))
                {
                    ordinal = targetOrdinal.Count;
                    targetOrdinal[target.ActorId] = ordinal;
                }
                float groupX = beat.Targets.Count > 1 ? DamageNumberLayout.GroupOffsetX(ordinal) : 0f;
                switch (target.Effect)
                {
                    case TargetEffect.Damage:
                        if (view != null)
                        {
                            if (delay > 0f)
                            {
                                var v = view;
                                bool crit = target.IsCrit;
                                Delay(delay, () => v.PlayHit(crit));
                            }
                            else view.PlayHit(target.IsCrit);
                            view.ShowNumber(target.Value, target.IsCrit ? NumberKind.Crit : NumberKind.Normal, groupX, delay);
                        }
                        Log($"{Name(target.ActorId)} 受到 {target.Value} 伤害{(target.IsCrit ? "(暴击)" : string.Empty)}");
                        if (target.IsCrit) anyCrit = true;
                        break;
                    case TargetEffect.Tick:
                    {
                        // BUFF_TICK:回血(HealthRegeneration 等)与 dot 伤害同事件同字段,按结算后 HP 与当前 HP 判
                        if (view != null && TurnPlan.TickIsHeal(target, view.CurrentHealth))
                        {
                            long healed = target.Value != 0 ? Math.Abs(target.Value) : (long)(target.HealthAfter - view.CurrentHealth);
                            view.SpawnFx("heal_ring", true, 320f);
                            view.ShowNumber(healed, NumberKind.Heal, groupX, delay);
                            Log($"{Name(target.ActorId)} 恢复 {healed} 生命(buff{target.BuffId})");
                        }
                        else
                        {
                            view?.PlayHit(false);
                            view?.ShowNumber(target.Value, NumberKind.Normal, groupX, delay);
                            Log($"{Name(target.ActorId)} 受到 {target.Value} 伤害(buff{target.BuffId})");
                        }
                        break;
                    }
                    case TargetEffect.Block:
                        view?.PlayHit(false);
                        view?.ShowNumber(target.Value, NumberKind.Normal, groupX, delay);
                        Log($"{Name(target.ActorId)} 格挡,受到 {target.Value} 伤害");
                        break;
                    case TargetEffect.Miss:
                        view?.PlayDodge();
                        view?.ShowNumber(0, NumberKind.Miss, groupX, delay);
                        Log($"{Name(target.ActorId)} 闪避");
                        break;
                    case TargetEffect.Heal:
                        view?.SpawnFx("heal_ring", true, 320f);
                        view?.ShowNumber(target.Value, NumberKind.Heal, groupX, delay);
                        Log($"{Name(target.ActorId)} 恢复 {target.Value} 生命");
                        break;
                    case TargetEffect.BuffAdd:
                        if (target.Success || target.BuffId != 0) view?.PlayBuffGain(target.BuffId);
                        Log($"{Name(target.ActorId)} 获得 buff{target.BuffId}");
                        break;
                    case TargetEffect.BuffRemove:
                        view?.PlayBuffLose(target.BuffId);
                        Log($"{Name(target.ActorId)} 失去 buff{target.BuffId}");
                        break;
                    case TargetEffect.Mana:
                        Log($"{Name(target.ActorId)} 法力 {(target.Value >= 0 ? "+" : string.Empty)}{target.Value}");
                        break;
                }

                if (view != null)
                {
                    if (target.HasHealthAfter)
                    {
                        if (delay > 0f)
                        {
                            var v = view;
                            ulong hp = target.HealthAfter;
                            ulong id = target.ActorId;
                            Delay(delay, () => { v.SetHealthDuringPlayback(hp); OnHealthChanged?.Invoke(id, hp); });
                        }
                        else
                        {
                            view.SetHealthDuringPlayback(target.HealthAfter);
                            OnHealthChanged?.Invoke(target.ActorId, target.HealthAfter);
                        }
                    }
                    if (target.HasManaAfter)
                    {
                        view.SetManaDuringPlayback(target.ManaAfter);
                        OnManaChanged?.Invoke(target.ActorId, target.ManaAfter);
                    }
                }
            }

            if (anyCrit)
            {
                Camera.Shake(BattleCameraFx.CritShakePixels, BattleCameraFx.CritShakeSeconds);
                BattleHitStop.Freeze(_runner, BattleHitStop.CritFreezeSeconds);
            }
        }

        // ── 小工具 ──────────────────────────────────────────

        private BattleUnitView FirstTargetView(Beat beat)
        {
            foreach (var target in beat.Targets)
            {
                if (target.Effect == TargetEffect.Mana) continue;
                var view = _resolveView(target.ActorId);
                if (view != null) return view;
            }
            return null;
        }

        private List<BattleUnitView> DistinctTargetViews(Beat beat)
        {
            var list = new List<BattleUnitView>();
            var seen = new HashSet<ulong>();
            foreach (var target in beat.Targets)
            {
                if (target.Effect == TargetEffect.Mana) continue;
                if (!seen.Add(target.ActorId)) continue;
                var view = _resolveView(target.ActorId);
                if (view != null) list.Add(view);
            }
            return list;
        }

        private string Name(ulong actorId)
        {
            string name = _resolveName?.Invoke(actorId);
            return string.IsNullOrEmpty(name) ? $"单位{actorId}" : name;
        }

        private void Log(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            try { OnLog?.Invoke(line); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>按 BattleTempo 倍率缩放的延时(多段攻击错拍等)。</summary>
        private void Delay(float seconds, Action action)
        {
            if (action == null) return;
            GTween.DelayedCall(BattleTempo.Scale(seconds)).SetIgnoreEngineTimeScale(true).SetTarget(_token)
                .OnComplete((GTweenCallback)(() =>
                {
                    try { action(); }
                    catch (Exception e) { Debug.LogException(e); }
                }));
        }

        private void DestroyTransient(GameObject go)
        {
            if (go == null) return;
            _transient.Remove(go);
            UnityEngine.Object.Destroy(go);
        }
    }
}
