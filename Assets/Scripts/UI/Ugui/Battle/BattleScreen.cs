using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 全屏回合战斗屏(纯 UGUI,不做 3D):
    ///   - 上敌方 / 下我方两排单位槽
    ///   - 行动条:攻击 / 技能 / 防御 / 道具 / 逃跑
    ///   - 技能子面板列 skill_table_ids(技能名先显示 id,表数据接入见 open_issues)
    ///   - 目标选择:点单位槽高亮,确认后 SubmitAction
    ///   - 回合倒计时(action_deadline_ms),已提交时显示「等待其他玩家…」
    ///   - OnTurnResult 事件流按序播放表现,播完全量应用 state(Ack 由 BattleUiRoot 调)
    ///   - 行动条末位「自动」开关:权威态取 BattleClient.IsMyActorAuto(点击只表达意愿,
    ///     is_auto 随下一帧权威状态回来),开自动时其余行动按钮置灰
    ///   - 观战只读模式(OpenSpectate):隐藏全部行动输入,顶部显示观众数,
    ///     「退出观战」走 SpectateClient.StopWatch;回合播放复用 CoPlayTurn
    ///     (观战流无 AckTurnPlayed 契约,Ack 与否由 BattleUiRoot 按模式区分)
    /// </summary>
    public sealed class BattleScreen
    {
        private enum PendingKind { None, Attack, Skill, Item }

        private const float EnemyRowY = 140f;
        private const float AllyRowY = 560f;
        private const float RowGap = 26f;

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;

        // 顶部信息
        private readonly TMP_Text _roundText;
        private readonly TMP_Text _phaseText;
        private readonly TMP_Text _pendingText;

        // 行动条
        private readonly UiTextButton _attackButton;
        private readonly UiTextButton _skillButton;
        private readonly UiTextButton _defendButton;
        private readonly UiTextButton _itemButton;
        private readonly UiTextButton _fleeButton;
        private readonly UiTextButton _autoButton;

        // 观战只读模式
        private readonly TMP_Text _spectateText;
        private readonly UiTextButton _stopWatchButton;

        // 目标选择 / 确认
        private readonly TMP_Text _targetHintText;
        private readonly UiTextButton _confirmButton;
        private readonly UiTextButton _cancelButton;

        // 技能子面板
        private readonly RectTransform _skillPanel;
        private readonly TMP_Text _skillEmptyText;
        private readonly List<UiTextButton> _skillButtons = new();

        // 道具子面板(道具列表未随 BattleStateS2C 下发,先用输入 id 的极简方式,见 open_issues)
        private readonly RectTransform _itemPanel;
        private readonly TMP_InputField _itemInput;

        // 底部提示
        private readonly TMP_Text _hintText;

        // 单位槽
        private readonly List<BattleUnitSlot> _slots = new();
        private readonly Dictionary<ulong, BattleUnitSlot> _slotById = new();

        private BattleStateS2C _state;
        private ulong _myId;
        private uint _myTeam;
        private uint _lastRound;
        private ulong _deadlineMs;
        private BattlePhase _phase = BattlePhase.None;
        private PendingKind _pending = PendingKind.None;
        private uint _pendingSkillId;
        private uint _pendingItemId;
        private ulong _selectedTargetId;
        private bool _submitted;
        private bool _playing;
        private bool _spectate;
        private uint _observerCount;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public uint MyTeamIndex => _myTeam;

        public BattleScreen(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;
            _root = QdaoUguiFactory.CreateRect("BattleScreen", parent,
                0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            _roundText = QdaoUguiFactory.CreateText("Round", _root, 1030f, 16f, 500f, 54f,
                string.Empty, 34f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _phaseText = QdaoUguiFactory.CreateText("Phase", _root, 1030f, 74f, 500f, 44f,
                string.Empty, 26f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _pendingText = QdaoUguiFactory.CreateText("Pending", _root, 680f, 122f, 1200f, 34f,
                string.Empty, 20f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.Center);

            // ── 目标选择 / 确认条 ──
            _targetHintText = QdaoUguiFactory.CreateText("TargetHint", _root, 724f, 788f, 560f, 56f,
                string.Empty, 22f, BattleUiStyle.WarnText);
            _confirmButton = BattleUiWidgets.CreateTextButton("ConfirmAction", _root, 1310f, 784f, 240f, 64f,
                "确认行动", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
            _cancelButton = BattleUiWidgets.CreateTextButton("CancelAction", _root, 1570f, 784f, 240f, 64f,
                "取消", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _cancelButton.Button.onClick.AddListener(ExitTargetMode);

            // ── 行动条 ──
            _attackButton = CreateActionButton(0, "攻击", OnAttackClicked);
            _skillButton  = CreateActionButton(1, "技能", OnSkillMenuClicked);
            _defendButton = CreateActionButton(2, "防御", OnDefendClicked);
            _itemButton   = CreateActionButton(3, "道具", OnItemMenuClicked);
            _fleeButton   = CreateActionButton(4, "逃跑", OnFleeClicked);
            // 「自动」挂在行动五键右侧(index 5):文案跟权威 is_auto,不跟本地意愿
            _autoButton   = CreateActionButton(5, "自动:关", OnAutoClicked);

            // ── 技能子面板 ──
            var skillBg = BattleUiWidgets.CreatePanel("SkillPanel", _root,
                830f, 470f, 900f, 300f, BattleUiStyle.PanelBgLight);
            _skillPanel = (RectTransform)skillBg.transform;
            QdaoUguiFactory.CreateText("SkillTitle", _skillPanel, 24f, 12f, 400f, 40f,
                "选择技能", 24f, QdaoUguiTheme.Cream);
            var skillClose = BattleUiWidgets.CreateTextButton("SkillClose", _skillPanel, 846f, 10f, 44f, 44f,
                "×", 26f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            skillClose.Button.onClick.AddListener(() => _skillPanel.gameObject.SetActive(false));
            _skillEmptyText = QdaoUguiFactory.CreateText("SkillEmpty", _skillPanel, 24f, 70f, 852f, 40f,
                "无可用技能", 22f, QdaoUguiTheme.MutedBrown);

            // ── 道具子面板 ──
            var itemBg = BattleUiWidgets.CreatePanel("ItemPanel", _root,
                830f, 500f, 900f, 220f, BattleUiStyle.PanelBgLight);
            _itemPanel = (RectTransform)itemBg.transform;
            QdaoUguiFactory.CreateText("ItemTitle", _itemPanel, 24f, 12f, 400f, 40f,
                "使用道具", 24f, QdaoUguiTheme.Cream);
            var itemClose = BattleUiWidgets.CreateTextButton("ItemClose", _itemPanel, 846f, 10f, 44f, 44f,
                "×", 26f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            itemClose.Button.onClick.AddListener(() => _itemPanel.gameObject.SetActive(false));
            QdaoUguiFactory.CreateText("ItemHint", _itemPanel, 24f, 62f, 852f, 34f,
                "战斗道具列表暂未接入,请输入道具表ID:", 20f, QdaoUguiTheme.StatusCream);
            var itemInputPlate = BattleUiWidgets.CreatePanel("ItemInputPlate", _itemPanel,
                24f, 106f, 300f, 56f, BattleUiStyle.PanelBg);
            _itemInput = QdaoUguiFactory.CreateInputField("ItemInput", itemInputPlate.transform,
                0f, 0f, 300f, 56f, "道具表ID", 10);
            _itemInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            var itemUse = BattleUiWidgets.CreateTextButton("ItemUse", _itemPanel, 340f, 106f, 160f, 56f,
                "选定", 22f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            itemUse.Button.onClick.AddListener(OnItemPicked);

            _hintText = QdaoUguiFactory.CreateText("Hint", _root, 680f, 952f, 1200f, 40f,
                string.Empty, 22f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);

            // ── 观战头部(仅 spectate 模式可见) ──
            _spectateText = QdaoUguiFactory.CreateText("SpectateInfo", _root, 40f, 16f, 620f, 54f,
                string.Empty, 30f, QdaoUguiTheme.Cream);
            _stopWatchButton = BattleUiWidgets.CreateTextButton("StopWatch", _root, 2330f, 16f, 190f, 64f,
                "退出观战", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _stopWatchButton.Button.onClick.AddListener(() => _owner?.RequestStopSpectate());
            _spectateText.gameObject.SetActive(false);
            _stopWatchButton.SetVisible(false);

            _skillPanel.gameObject.SetActive(false);
            _itemPanel.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        private UiTextButton CreateActionButton(int index, string label, UnityEngine.Events.UnityAction onClick)
        {
            const float width = 200f;
            const float gap = 28f;
            float x0 = (QdaoUguiTheme.DesignWidth - (5f * width + 4f * gap)) * 0.5f;
            var button = BattleUiWidgets.CreateTextButton($"Action_{label}", _root,
                x0 + index * (width + gap), 856f, width, 84f,
                label, 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            button.Button.onClick.AddListener(onClick);
            return button;
        }

        // ── 开关 ─────────────────────────────────────────────

        public void Open(BattleStateS2C state, ulong myPlayerId)
            => OpenInternal(state, myPlayerId, spectate: false, observerCount: 0);

        /// <summary>
        /// 观战只读模式开屏(§10 D8 观众零写权):无本人 actor(myId=0,
        /// FindMyActor 恒空),行动输入整体隐藏;_myTeam 保持 0,即 team 0
        /// 固定显示在下排。回合播放复用 CoPlayTurn,Ack 与否由 BattleUiRoot 区分。
        /// </summary>
        public void OpenSpectate(BattleStateS2C state, uint observerCount)
            => OpenInternal(state, 0, spectate: true, observerCount: observerCount);

        private void OpenInternal(BattleStateS2C state, ulong myPlayerId, bool spectate, uint observerCount)
        {
            _spectate = spectate;
            _observerCount = observerCount;
            // 观战不走 OnPhaseChanged(BattleClient 相位与观战无关),留在 None;
            // _myTeam 归零保证 team 0 固定在下排(上一场参战残留值会翻转排布)
            if (spectate)
            {
                _phase = BattlePhase.None;
                _myTeam = 0;
            }
            _myId = myPlayerId;
            _submitted = false;
            _playing = false;
            _lastRound = 0;
            _pending = PendingKind.None;
            _selectedTargetId = 0;
            _root.gameObject.SetActive(true);
            ApplyState(state);
            ExitTargetMode();
            RefreshSpectateHeader();
        }

        public void Close()
        {
            AbortPlayback();
            foreach (var slot in _slots) slot.Destroy();
            _slots.Clear();
            _slotById.Clear();
            _state = null;
            _spectate = false;
            _observerCount = 0;
            RefreshSpectateHeader();
            _root.gameObject.SetActive(false);
        }

        /// <summary>观战补帧(NotifySpectateState 可重发)刷新观众数。</summary>
        public void SetObserverCount(uint count)
        {
            _observerCount = count;
            RefreshSpectateHeader();
        }

        public void OnPhaseChanged(BattlePhase phase)
        {
            _phase = phase;
            RefreshActionBar();
        }

        /// <summary>战斗屏被强制关闭(断线等)时中止播放态。</summary>
        public void AbortPlayback()
        {
            _playing = false;
        }

        // ── 权威状态应用 ─────────────────────────────────────

        public void ApplyState(BattleStateS2C state)
        {
            if (state == null) return;
            _state = state;
            _deadlineMs = state.ActionDeadlineMs;

            // 我方 team:按本人 actor 定位;找不到时保持上次值
            foreach (var actor in state.Actors)
            {
                if (actor.ActorId == _myId) { _myTeam = actor.TeamIndex; break; }
            }

            bool structural = _slots.Count != state.Actors.Count;
            if (!structural)
            {
                foreach (var actor in state.Actors)
                {
                    if (!_slotById.ContainsKey(actor.ActorId)) { structural = true; break; }
                }
            }
            if (structural) RebuildSlots(state);
            else
            {
                foreach (var actor in state.Actors)
                {
                    if (_slotById.TryGetValue(actor.ActorId, out var slot)) slot.Apply(actor);
                }
            }

            _roundText.text = $"第 {state.RoundIndex} 回合";

            // 新回合开始:重置本地已提交标记
            if (state.RoundIndex != _lastRound)
            {
                _lastRound = state.RoundIndex;
                _submitted = false;
            }
            // 服务器 pending 列表是权威:不含本人即已提交
            if (state.PendingActorIds.Count > 0)
                _submitted = !state.PendingActorIds.Contains(_myId);

            RefreshPendingText();
            RefreshActionBar();
        }

        private void RebuildSlots(BattleStateS2C state)
        {
            foreach (var slot in _slots) slot.Destroy();
            _slots.Clear();
            _slotById.Clear();

            var enemies = new List<BattleActorState>();
            var allies = new List<BattleActorState>();
            foreach (var actor in state.Actors)
            {
                if (actor.TeamIndex == _myTeam) allies.Add(actor);
                else enemies.Add(actor);
            }
            BuildRow(enemies, EnemyRowY);
            BuildRow(allies, AllyRowY);

            // 子面板压在单位槽之上(后建的槽会盖住先建的面板)
            _skillPanel.SetAsLastSibling();
            _itemPanel.SetAsLastSibling();
        }

        private void BuildRow(List<BattleActorState> actors, float y)
        {
            float total = actors.Count * BattleUnitSlot.Width + Mathf.Max(0, actors.Count - 1) * RowGap;
            float x0 = (QdaoUguiTheme.DesignWidth - total) * 0.5f;
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                var slot = new BattleUnitSlot(_owner, _root,
                    x0 + i * (BattleUnitSlot.Width + RowGap), y,
                    actor.ActorId, actor.ActorId == _myId, OnSlotClicked);
                slot.Apply(actor);
                _slots.Add(slot);
                _slotById[actor.ActorId] = slot;
            }
        }

        // ── 每帧驱动(倒计时) ────────────────────────────────

        public void Tick()
        {
            if (!IsOpen) return;
            if (_playing)
            {
                _phaseText.text = "回合播放中…";
                return;
            }
            if (_spectate)
            {
                // 观众没有本地相位,顶部信息完全由权威状态推导
                if (_state != null && _state.Outcome == eBattleOutcome.BattleOutcomeOngoing)
                {
                    long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                    _phaseText.text = remainMs > 0
                        ? $"等待玩家行动 {(int)(remainMs / 1000)} 秒"
                        : "等待回合结算…";
                }
                else
                {
                    _phaseText.text = "战斗已结束";
                }
                return;
            }
            switch (_phase)
            {
                case BattlePhase.WaitingAction:
                    if (_submitted)
                    {
                        _phaseText.text = "等待其他玩家…";
                    }
                    else
                    {
                        long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                        _phaseText.text = $"行动倒计时 {Mathf.Max(0, (int)(remainMs / 1000))} 秒";
                    }
                    break;
                case BattlePhase.Resolving:
                    _phaseText.text = "回合结算中…";
                    break;
                case BattlePhase.Ended:
                    _phaseText.text = "战斗结束";
                    break;
                default:
                    _phaseText.text = string.Empty;
                    break;
            }
        }

        // ── 行动条交互 ──────────────────────────────────────

        private void OnAttackClicked()
        {
            if (!CanAct()) return;
            CloseSubPanels();
            _pending = PendingKind.Attack;
            EnterTargetMode("请选择攻击目标(敌方)");
        }

        private void OnSkillMenuClicked()
        {
            if (!CanAct()) return;
            if (_skillPanel.gameObject.activeSelf) { _skillPanel.gameObject.SetActive(false); return; }
            CloseSubPanels();
            PopulateSkillPanel();
            _skillPanel.gameObject.SetActive(true);
        }

        private void OnSkillPicked(uint skillTableId)
        {
            if (!CanAct()) return;
            _skillPanel.gameObject.SetActive(false);
            _pending = PendingKind.Skill;
            _pendingSkillId = skillTableId;
            EnterTargetMode($"请为 技能{skillTableId} 选择目标");
        }

        private void OnDefendClicked()
        {
            if (!CanAct()) return;
            CloseSubPanels();
            Submit(new BattleAction { ActionType = eBattleActionType.BattleActionDefend });
        }

        private void OnItemMenuClicked()
        {
            if (!CanAct()) return;
            if (_itemPanel.gameObject.activeSelf) { _itemPanel.gameObject.SetActive(false); return; }
            CloseSubPanels();
            _itemPanel.gameObject.SetActive(true);
        }

        private void OnItemPicked()
        {
            if (!CanAct()) return;
            string raw = (_itemInput != null ? _itemInput.text : string.Empty)?.Trim();
            if (string.IsNullOrEmpty(raw) || !uint.TryParse(raw, out uint itemId) || itemId == 0)
            {
                SetHint("请输入有效的道具表ID");
                return;
            }
            _itemPanel.gameObject.SetActive(false);
            _pending = PendingKind.Item;
            _pendingItemId = itemId;
            EnterTargetMode($"请为 道具{itemId} 选择目标");
        }

        private void OnFleeClicked()
        {
            if (!CanAct()) return;
            CloseSubPanels();
            Submit(new BattleAction { ActionType = eBattleActionType.BattleActionFlee });
        }

        private void OnAutoClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetHint("战斗模块未就绪"); return; }
            // 切换基准取本地意愿(AutoBattleLatched)而非权威值:服务器未回执前
            // 再点一次也能撤销;按钮文案只跟 is_auto 权威回执翻转(§11.2)
            bool enable = !client.AutoBattleLatched;
            client.SetAutoBattle(enable);
            SetHint(enable ? "已请求开启自动战斗…" : "已请求关闭自动战斗…");
        }

        /// <summary>BattleClient.OnAutoStateChanged(is_auto 权威变化)驱动:刷按钮文案与行动条置灰。</summary>
        public void OnAutoStateChanged(bool isAuto)
        {
            RefreshActionBar();
        }

        // ── 目标选择 ────────────────────────────────────────

        private void OnSlotClicked(ulong actorId)
        {
            if (_pending == PendingKind.None) return;
            if (!_slotById.TryGetValue(actorId, out var slot)) return;
            if (!IsTargetable(slot)) return;

            _selectedTargetId = actorId;
            foreach (var s in _slots)
                s.SetHighlight(s.ActorId == actorId
                    ? SlotHighlight.Selected
                    : (IsTargetable(s) ? SlotHighlight.Targetable : SlotHighlight.None));
            _confirmButton.SetVisible(true);
        }

        private bool IsTargetable(BattleUnitSlot slot)
        {
            if (slot.IsDead || slot.Fled) return false;
            // 普攻只允许打敌方;技能/道具的敌我规则依赖表数据,先放开任意存活目标
            if (_pending == PendingKind.Attack) return slot.TeamIndex != _myTeam;
            return true;
        }

        private void EnterTargetMode(string hint)
        {
            _selectedTargetId = 0;
            _targetHintText.text = hint ?? string.Empty;
            foreach (var slot in _slots)
                slot.SetHighlight(IsTargetable(slot) ? SlotHighlight.Targetable : SlotHighlight.None);
            _confirmButton.SetVisible(false);
            _cancelButton.SetVisible(true);
        }

        private void ExitTargetMode()
        {
            _pending = PendingKind.None;
            _pendingSkillId = 0;
            _pendingItemId = 0;
            _selectedTargetId = 0;
            _targetHintText.text = string.Empty;
            foreach (var slot in _slots) slot.SetHighlight(SlotHighlight.None);
            _confirmButton.SetVisible(false);
            _cancelButton.SetVisible(false);
            CloseSubPanels();
        }

        private void OnConfirmClicked()
        {
            if (_pending == PendingKind.None || _selectedTargetId == 0) return;
            BattleAction action;
            switch (_pending)
            {
                case PendingKind.Attack:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionAttack,
                        TargetId = _selectedTargetId,
                    };
                    break;
                case PendingKind.Skill:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionSkill,
                        SkillTableId = _pendingSkillId,
                        TargetId = _selectedTargetId,
                    };
                    break;
                case PendingKind.Item:
                    action = new BattleAction
                    {
                        ActionType = eBattleActionType.BattleActionItem,
                        ItemTableId = _pendingItemId,
                        TargetId = _selectedTargetId,
                    };
                    break;
                default:
                    return;
            }
            Submit(action);
        }

        private void Submit(BattleAction action)
        {
            var client = _owner?.Client;
            if (client == null) { SetHint("战斗模块未就绪"); return; }
            client.SubmitAction(action);
            _submitted = true;
            ExitTargetMode();
            RefreshActionBar();
            SetHint("已提交行动,等待其他玩家…");
        }

        private bool CanAct()
        {
            if (_spectate) return false;
            // 自动战斗中禁止手动出招(is_auto 单位不进 pending,提交会被服务端拒)
            if (_owner?.Client != null && _owner.Client.IsMyActorAuto) return false;
            var me = FindMyActor();
            return _phase == BattlePhase.WaitingAction && !_playing && !_submitted
                   && me != null && !me.IsDead && !me.Fled;
        }

        // ── 回合播放 ────────────────────────────────────────

        /// <summary>按事件顺序播放回合表现,播完全量应用 state。由 BattleUiRoot 以协程驱动。</summary>
        public IEnumerator CoPlayTurn(TurnResultS2C result)
        {
            if (result == null) yield break;
            _playing = true;
            ExitTargetMode();
            RefreshActionBar();
            _roundText.text = $"第 {result.RoundIndex} 回合 · 结算";

            foreach (var ev in result.Events)
            {
                PlayEvent(ev);
                yield return new WaitForSecondsRealtime(BattleUiStyle.EventStepSeconds);
            }

            if (result.State != null) ApplyState(result.State);
            _playing = false;
            RefreshActionBar();
        }

        private void PlayEvent(BattleEventItem ev)
        {
            if (ev == null) return;
            _slotById.TryGetValue(ev.SourceId, out var src);
            _slotById.TryGetValue(ev.TargetId, out var dst);

            switch (ev.EventType)
            {
                case eBattleEventType.BattleEventAttack:
                    src?.PlayShake();
                    src?.SpawnFloatText("攻击", BattleUiStyle.SysText, false);
                    break;
                case eBattleEventType.BattleEventSkill:
                    src?.PlayFlash(BattleUiStyle.SkillFlash);
                    src?.SpawnFloatText($"技能{ev.SkillTableId}", BattleUiStyle.SkillText, false);
                    break;
                case eBattleEventType.BattleEventDamage:
                    if (dst != null)
                    {
                        dst.PlayShake();
                        dst.PlayFlash(BattleUiStyle.DamageFlash);
                        dst.SpawnFloatText(ev.IsCritical ? $"暴击 -{ev.Value}" : $"-{ev.Value}",
                            BattleUiStyle.DamageText, ev.IsCritical);
                        dst.SetHealthDuringPlayback(ev.TargetHealthAfter);
                    }
                    break;
                case eBattleEventType.BattleEventHeal:
                    if (dst != null)
                    {
                        dst.PlayFlash(BattleUiStyle.HealFlash);
                        dst.SpawnFloatText($"+{ev.Value}", BattleUiStyle.HealText, ev.IsCritical);
                        dst.SetHealthDuringPlayback(ev.TargetHealthAfter);
                    }
                    break;
                case eBattleEventType.BattleEventBuffAdd:
                    dst?.SpawnFloatText($"+B{ev.BuffTableId}", BattleUiStyle.BuffAddText, false);
                    break;
                case eBattleEventType.BattleEventBuffRemove:
                    dst?.SpawnFloatText($"-B{ev.BuffTableId}", BattleUiStyle.BuffCutText, false);
                    break;
                case eBattleEventType.BattleEventBuffTick:
                    if (dst != null)
                    {
                        dst.SpawnFloatText($"B{ev.BuffTableId} -{ev.Value}", BattleUiStyle.DamageText, false);
                        dst.SetHealthDuringPlayback(ev.TargetHealthAfter);
                    }
                    break;
                case eBattleEventType.BattleEventDeath:
                    if (dst != null)
                    {
                        dst.PlayFlash(BattleUiStyle.DeathFlash);
                        dst.SpawnFloatText("阵亡", BattleUiStyle.DamageText, true);
                        dst.ShowDeadMark();
                    }
                    break;
                case eBattleEventType.BattleEventDefend:
                    (dst ?? src)?.SpawnFloatText("防御", BattleUiStyle.SysText, false);
                    break;
                case eBattleEventType.BattleEventItem:
                    src?.SpawnFloatText($"道具{ev.ItemTableId}", BattleUiStyle.SkillText, false);
                    break;
                case eBattleEventType.BattleEventFlee:
                    src?.SpawnFloatText(ev.Success ? "逃跑成功" : "逃跑失败",
                        ev.Success ? BattleUiStyle.HealText : BattleUiStyle.BuffCutText, false);
                    break;
            }
        }

        // ── 内部刷新 ────────────────────────────────────────

        private void PopulateSkillPanel()
        {
            foreach (var button in _skillButtons)
            {
                if (button.Rect != null)
                    UnityEngine.Object.Destroy(button.Rect.gameObject);
            }
            _skillButtons.Clear();

            var me = FindMyActor();
            int index = 0;
            if (me != null)
            {
                foreach (var skillId in me.SkillTableIds)
                {
                    me.SkillCooldownRounds.TryGetValue(skillId, out uint cooldown);
                    int row = index / 4;
                    int col = index % 4;
                    uint captured = skillId;
                    var button = BattleUiWidgets.CreateTextButton($"Skill_{skillId}", _skillPanel,
                        24f + col * 216f, 70f + row * 74f, 200f, 60f,
                        cooldown > 0 ? $"技能{skillId}(冷却{cooldown})" : $"技能{skillId}",
                        20f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                    button.SetInteractable(cooldown == 0);
                    button.Button.onClick.AddListener(() => OnSkillPicked(captured));
                    _skillButtons.Add(button);
                    index++;
                }
            }
            _skillEmptyText.gameObject.SetActive(index == 0);
        }

        private void CloseSubPanels()
        {
            _skillPanel.gameObject.SetActive(false);
            _itemPanel.gameObject.SetActive(false);
        }

        private void RefreshPendingText()
        {
            // 观战没有本地相位,只要权威 pending 列表非空就展示
            bool show = _state != null && _state.PendingActorIds.Count > 0
                        && (_spectate || _phase == BattlePhase.WaitingAction);
            if (!show)
            {
                _pendingText.text = string.Empty;
                return;
            }
            var names = new List<string>();
            foreach (var actorId in _state.PendingActorIds)
            {
                string name = $"单位{actorId}";
                foreach (var actor in _state.Actors)
                {
                    if (actor.ActorId == actorId)
                    {
                        name = string.IsNullOrEmpty(actor.Name) ? name : actor.Name;
                        break;
                    }
                }
                names.Add(name);
            }
            _pendingText.text = $"未行动:{string.Join("、", names)}";
        }

        private void RefreshActionBar()
        {
            // 观战只读:行动输入整体隐藏(不是置灰,观众根本没有行动语义)
            bool showActions = !_spectate;
            _attackButton.SetVisible(showActions);
            _skillButton.SetVisible(showActions);
            _defendButton.SetVisible(showActions);
            _itemButton.SetVisible(showActions);
            _fleeButton.SetVisible(showActions);
            _autoButton.SetVisible(showActions);
            if (_spectate)
            {
                SetHint(string.Empty);
                return;
            }

            var me = FindMyActor();
            bool myAuto = _owner?.Client != null && _owner.Client.IsMyActorAuto;
            bool alive = me != null && !me.IsDead && !me.Fled;
            bool canAct = _phase == BattlePhase.WaitingAction && !_playing && !_submitted
                          && alive && !myAuto;

            _attackButton.SetInteractable(canAct);
            _skillButton.SetInteractable(canAct);
            _defendButton.SetInteractable(canAct);
            _itemButton.SetInteractable(canAct);
            // 一期规则:PVP 不可逃跑(客户端以「敌方含玩家」近似判定,见 open_issues)
            bool canFlee = canAct && IsPveBattle();
            _fleeButton.SetInteractable(canFlee);
            // 「自动」不受已提交/播放中限制:战斗内活着随时可切(Resolving 切换下回合生效)
            bool inRound = _phase == BattlePhase.WaitingAction || _phase == BattlePhase.Resolving;
            _autoButton.SetInteractable(alive && inRound);
            _autoButton.SetText(myAuto ? "自动:开" : "自动:关");

            if (me == null) SetHint(string.Empty);
            else if (me.IsDead) SetHint("你已阵亡,等待战斗结束…");
            else if (me.Fled) SetHint("你已逃离战斗…");
            else if (myAuto) SetHint("自动战斗中,点「自动:开」可关闭");
            else if (_playing) SetHint("回合播放中…");
            else if (_submitted && _phase == BattlePhase.WaitingAction) SetHint("已提交行动,等待其他玩家…");
            else if (_phase == BattlePhase.WaitingAction) SetHint(canFlee ? string.Empty : "PVP 战斗不可逃跑");
            else SetHint(string.Empty);
        }

        private void RefreshSpectateHeader()
        {
            if (_spectateText == null) return;
            _spectateText.gameObject.SetActive(_spectate);
            _stopWatchButton.SetVisible(_spectate);
            if (_spectate) _spectateText.text = $"观战中 · 观众 {_observerCount} 人";
        }

        private bool IsPveBattle()
        {
            if (_state == null) return true;
            foreach (var actor in _state.Actors)
            {
                if (actor.TeamIndex != _myTeam &&
                    actor.ActorType == eBattleActorType.BattleActorTypeMonster)
                    return true;
            }
            return false;
        }

        private BattleActorState FindMyActor()
        {
            if (_state == null) return null;
            foreach (var actor in _state.Actors)
            {
                if (actor.ActorId == _myId) return actor;
            }
            return null;
        }

        private void SetHint(string value)
        {
            if (_hintText != null) _hintText.text = value ?? string.Empty;
        }
    }
}
