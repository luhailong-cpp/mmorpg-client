using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Battle.Presentation;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 全屏回合战斗屏(问道式,纯 UGUI,turn-battle-presentation.md §3/§4):
    ///   - 舞台:BattleStage 槽位(上敌下我、两排交错、近大远小)+ BattleUnitView 图片单位;
    ///   - 演出:TurnResult 到达 → TurnPlan.Build → BattlePresenter/BattleSequencer 逐拍播放
    ///     (冲刺/施法/受击/飘字/特效/暴击顿帧震屏/死亡),播完全量应用 state(Ack 由 BattleUiRoot 调);
    ///   - HUD:左上回合数(翻数)+ 战斗记录;顶部行动预告条;右上自己与队友卡片;
    ///     右下命令环(攻击/法术/防御/道具/召唤(灰)/逃跑/自动;自动战斗中换成 角色/宠物/取消自动);
    ///   - 目标选择:点单位(BattleUnitView 点击区)高亮,确认后 SubmitAction;
    ///   - 回合倒计时:环形 + 数字(action_deadline_ms);已提交显示「等待其他玩家…」;
    ///   - 观战只读模式(OpenSpectate):隐藏命令环,顶部显示观众数;回合播放复用 CoPlayTurn
    ///     (观战流无 AckTurnPlayed 契约,Ack 与否由 BattleUiRoot 按模式区分);
    ///   - 观战抢占/断线:AbortPlayback → BattlePresenter.Abort 复位所有 tween/飘字/特效。
    /// 对 BattleUiRoot 的公开接口与旧版保持一致。
    /// </summary>
    public sealed class BattleScreen
    {
        private enum PendingKind { None, Attack, Skill, Item }

        // ── HUD 固定控件矩形(设计坐标,y 向下;BattleUiLayout 用它们断言任何分辨率下都在屏内) ──
        public const float RoundCounterX = 40f;
        public const float RoundCounterY = 20f;
        public static readonly Rect LogButtonRect = new Rect(356f, 28f, 170f, 54f);
        public static readonly Rect TimerRect = new Rect(2080f, 24f, 90f, 90f);
        public static readonly Rect StopWatchRect = new Rect(2300f, 980f, 220f, 64f);
        /// <summary>目标提示/确认/取消条:贴在 BattleStage.HudBottomBand 之下,不压我方后排。</summary>
        public static readonly Rect TargetHintRect = new Rect(724f, BattleStage.HudBottomBand + 4f, 560f, 56f);
        public static readonly Rect ConfirmRect = new Rect(1310f, BattleStage.HudBottomBand, 240f, 64f);
        public static readonly Rect CancelRect = new Rect(1570f, BattleStage.HudBottomBand, 240f, 64f);

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;

        // 层(兄弟顺序即绘制顺序)
        private readonly RectTransform _world;        // 舞台/特效/名牌/数字的共同父节点(震屏整体位移)
        private readonly RectTransform _stageRoot;    // 单位
        private readonly RectTransform _fxLayer;      // 特效
        private readonly RectTransform _plateLayer;   // 名牌(名字/血蓝条/buff 行):立绘与特效之上,不被遮
        private readonly RectTransform _numberLayer;  // 伤害数字
        private readonly RectTransform _overlayLayer; // 入场云层 / 出生光环
        private readonly RectTransform _hudRoot;      // HUD

        // HUD
        private readonly BattleRoundCounter _roundCounter;
        private readonly BattleLogPanel _logPanel;
        private readonly BattleActionOrderBar _orderBar;
        private readonly BattlePartyCards _partyCards;
        private readonly BattleCommandRing _commandRing;
        private readonly TMP_Text _phaseText;
        private readonly TMP_Text _pendingText;
        private readonly Image _timerRing;
        private readonly TMP_Text _timerText;

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

        // 单位
        private readonly List<BattleUnitView> _views = new();
        private readonly Dictionary<ulong, BattleUnitView> _viewById = new();
        private readonly BattlePresenter _presenter;

        private BattleStateS2C _state;
        private ulong _myId;
        private uint _myTeam;
        private uint _lastRound;
        private ulong _deadlineMs;
        private long _deadlineStartMs;
        private BattlePhase _phase = BattlePhase.None;
        private PendingKind _pending = PendingKind.None;
        private uint _pendingSkillId;
        private uint _pendingItemId;
        private ulong _selectedTargetId;
        private bool _submitted;
        private bool _playing;
        private bool _playDone = true;
        private bool _spectate;
        private uint _observerCount;
        private eBattleOutcome _shownOutcome = eBattleOutcome.BattleOutcomeOngoing;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public uint MyTeamIndex => _myTeam;
        /// <summary>演出层(BattleUiRoot 结算/测试可用)。</summary>
        public BattlePresenter Presenter => _presenter;

        public BattleScreen(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;
            _root = QdaoUguiFactory.CreateRect("BattleScreen", parent,
                0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            // 舞台/特效/数字层各自嵌套 Canvas:单位呼吸、冲刺、飘字只重建各自子画布,HUD 静态批次不动;
            // 舞台层自带 GraphicRaycaster(嵌套 Canvas 的图形只由本画布的 raycaster 命中,单位点击区在此)
            _world = CreateLayer("World", nestedCanvas: false, raycaster: false);
            _stageRoot = CreateLayer("Stage", nestedCanvas: true, raycaster: true, parent: _world);
            _fxLayer = CreateLayer("Fx", nestedCanvas: true, raycaster: false, parent: _world);
            _plateLayer = CreateLayer("Plates", nestedCanvas: true, raycaster: false, parent: _world);
            _numberLayer = CreateLayer("Numbers", nestedCanvas: true, raycaster: false, parent: _world);
            _overlayLayer = CreateLayer("Overlay", nestedCanvas: false, raycaster: false);
            _hudRoot = CreateLayer("Hud", nestedCanvas: false, raycaster: false);

            _presenter = new BattlePresenter(owner, _stageRoot, _fxLayer, _numberLayer, _overlayLayer,
                id => _viewById.TryGetValue(id, out var v) ? v : null, () => _views, ResolveName, _world);
            _presenter.OnFinished += () => _playDone = true;
            _presenter.OnAborted += () => _playDone = true;
            _presenter.OnBeatStarted += (beat, index) => _orderBar.Highlight(beat.ActorId);
            _presenter.OnLog += line => _logPanel.Append(line);
            _presenter.OnHealthChanged += (id, hp) => _partyCards.SetHealth(id, hp);
            _presenter.OnManaChanged += (id, mp) => _partyCards.SetMana(id, mp);

            // ── 左上:回合 + 战斗记录 + 相位/未行动(左列,避开顶部预告条与敌方后排的头顶条) ──
            _roundCounter = new BattleRoundCounter(_hudRoot, RoundCounterX, RoundCounterY);
            _logPanel = new BattleLogPanel(_hudRoot, LogButtonRect.x, LogButtonRect.y);
            _phaseText = QdaoUguiFactory.CreateText("Phase", _hudRoot, 40f, 98f, 620f, 36f,
                string.Empty, 24f, BattleUiStyle.WarnText, TextAlignmentOptions.MidlineLeft);
            _pendingText = QdaoUguiFactory.CreateText("Pending", _hudRoot, 40f, 136f, 700f, 30f,
                string.Empty, 18f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.MidlineLeft);
            _pendingText.overflowMode = TextOverflowModes.Ellipsis;

            // ── 顶部:行动预告条(x 542..2018,底边 ≈132 = BattleStage.HudTopBand 之上) ──
            _orderBar = new BattleActionOrderBar(_hudRoot);

            // ── 回合计时环(行动预告条右侧) ──
            var timerBg = QdaoUguiFactory.CreateImage("TimerBg", _hudRoot, TimerRect.x, TimerRect.y, TimerRect.width, TimerRect.height, BattleArtCatalog.RingSprite);
            timerBg.color = new Color(0f, 0f, 0f, 0.45f);
            _timerRing = QdaoUguiFactory.CreateImage("TimerRing", _hudRoot, TimerRect.x, TimerRect.y, TimerRect.width, TimerRect.height, BattleArtCatalog.RingSprite);
            _timerRing.type = Image.Type.Filled;
            _timerRing.fillMethod = Image.FillMethod.Radial360;
            _timerRing.fillOrigin = (int)Image.Origin360.Top;
            _timerRing.fillClockwise = false;
            _timerRing.color = BattleUiStyle.WarnText;
            _timerText = QdaoUguiFactory.CreateText("TimerText", _hudRoot, TimerRect.x, TimerRect.y, TimerRect.width, TimerRect.height,
                string.Empty, 30f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            _timerText.fontStyle = FontStyles.Bold;

            // ── 右上:角色卡 ──
            _partyCards = new BattlePartyCards(_hudRoot);

            // ── 右下:命令环 ──
            _commandRing = new BattleCommandRing(_hudRoot, OnCommand, OnAutoCommand);

            // ── 目标选择 / 确认条(HudBottomBand 之下,我方后排脚下名字之外) ──
            _targetHintText = QdaoUguiFactory.CreateText("TargetHint", _hudRoot, TargetHintRect.x, TargetHintRect.y, TargetHintRect.width, TargetHintRect.height,
                string.Empty, 22f, BattleUiStyle.WarnText);
            _confirmButton = BattleUiWidgets.CreateTextButton("ConfirmAction", _hudRoot, ConfirmRect.x, ConfirmRect.y, ConfirmRect.width, ConfirmRect.height,
                "确认行动", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_confirmButton.Plate, "button_9slice");
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
            _cancelButton = BattleUiWidgets.CreateTextButton("CancelAction", _hudRoot, CancelRect.x, CancelRect.y, CancelRect.width, CancelRect.height,
                "取消", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_cancelButton.Plate, "button_9slice");
            _cancelButton.Button.onClick.AddListener(ExitTargetMode);

            // ── 技能子面板 ──
            var skillBg = BattleUiWidgets.CreatePanel("SkillPanel", _hudRoot,
                830f, 470f, 900f, 300f, BattleUiStyle.PanelBgLight);
            BattleRoundCounter.ApplyNineSlice(skillBg, "panel_9slice");
            _skillPanel = (RectTransform)skillBg.transform;
            QdaoUguiFactory.CreateText("SkillTitle", _skillPanel, 24f, 12f, 400f, 40f,
                "选择法术", 24f, QdaoUguiTheme.Cream);
            var skillClose = BattleUiWidgets.CreateTextButton("SkillClose", _skillPanel, 846f, 10f, 44f, 44f,
                "×", 26f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            skillClose.Button.onClick.AddListener(() => _skillPanel.gameObject.SetActive(false));
            _skillEmptyText = QdaoUguiFactory.CreateText("SkillEmpty", _skillPanel, 24f, 70f, 852f, 40f,
                "无可用法术", 22f, QdaoUguiTheme.MutedBrown);

            // ── 道具子面板 ──
            var itemBg = BattleUiWidgets.CreatePanel("ItemPanel", _hudRoot,
                830f, 500f, 900f, 220f, BattleUiStyle.PanelBgLight);
            BattleRoundCounter.ApplyNineSlice(itemBg, "panel_9slice");
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

            _hintText = QdaoUguiFactory.CreateText("Hint", _hudRoot, 680f, 1000f, 1200f, 40f,
                string.Empty, 22f, QdaoUguiTheme.StatusCream, TextAlignmentOptions.Center);

            // ── 观战头部(仅 spectate 模式可见;左列第三行) ──
            _spectateText = QdaoUguiFactory.CreateText("SpectateInfo", _hudRoot, 40f, 170f, 620f, 40f,
                string.Empty, 24f, QdaoUguiTheme.Cream);
            _stopWatchButton = BattleUiWidgets.CreateTextButton("StopWatch", _hudRoot, StopWatchRect.x, StopWatchRect.y, StopWatchRect.width, StopWatchRect.height,
                "退出观战", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_stopWatchButton.Plate, "button_9slice");
            _stopWatchButton.Button.onClick.AddListener(() => _owner?.RequestStopSpectate());
            _spectateText.gameObject.SetActive(false);
            _stopWatchButton.SetVisible(false);

            _skillPanel.gameObject.SetActive(false);
            _itemPanel.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 建一层(兄弟顺序即绘制顺序)。nestedCanvas=true 时加嵌套 Canvas(不覆盖排序,随父画布层级绘制),
        /// 该层内任何 transform 变化只触发本子画布 BuildBatch;raycaster=true 再加 GraphicRaycaster
        /// (嵌套 Canvas 下的 Graphic 只登记在自己的画布上,父画布的 raycaster 命中不到)。
        /// </summary>
        private RectTransform CreateLayer(string name, bool nestedCanvas, bool raycaster, RectTransform parent = null)
        {
            var layer = QdaoUguiFactory.CreateRect(name, parent != null ? parent : _root, 0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            if (nestedCanvas)
            {
                var canvas = layer.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = false;
                canvas.pixelPerfect = false;
                if (raycaster) layer.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            return layer;
        }

        // ── 开关 ─────────────────────────────────────────────

        public void Open(BattleStateS2C state, ulong myPlayerId)
            => OpenInternal(state, myPlayerId, spectate: false, observerCount: 0);

        /// <summary>
        /// 观战只读模式开屏(§10 D8 观众零写权):无本人 actor(myId=0,
        /// FindMyActor 恒空),命令环整体隐藏;_myTeam 保持 0,即 team 0
        /// 固定显示在下方(我方阵位)。回合播放复用 CoPlayTurn,Ack 与否由 BattleUiRoot 区分。
        /// </summary>
        public void OpenSpectate(BattleStateS2C state, uint observerCount)
            => OpenInternal(state, 0, spectate: true, observerCount: observerCount);

        private void OpenInternal(BattleStateS2C state, ulong myPlayerId, bool spectate, uint observerCount)
        {
            _spectate = spectate;
            _observerCount = observerCount;
            if (spectate)
            {
                _phase = BattlePhase.None;
                _myTeam = 0;
            }
            _myId = myPlayerId;
            _submitted = false;
            _playing = false;
            _playDone = true;
            _lastRound = 0;
            _deadlineStartMs = 0;
            _pending = PendingKind.None;
            _selectedTargetId = 0;
            _shownOutcome = eBattleOutcome.BattleOutcomeOngoing;
            _root.gameObject.SetActive(true);
            _logPanel.Clear();
            _logPanel.SetVisible(false);
            _orderBar.Clear();
            ApplyState(state);
            _roundCounter.SetRound(state?.RoundIndex ?? 0, false);
            ExitTargetMode();
            RefreshSpectateHeader();
            _presenter.PlayEntrance(_views);
            _logPanel.Append("战斗开始");
        }

        public void Close()
        {
            AbortPlayback();
            _presenter.ClearTransient();
            foreach (var view in _views) view.Destroy();
            _views.Clear();
            _viewById.Clear();
            _orderBar.Clear();
            _partyCards.Clear();
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

        /// <summary>战斗屏被强制关闭(断线)/观战抢占时中止播放态:复位所有 tween、飘字、特效。</summary>
        public void AbortPlayback()
        {
            _presenter.Abort();
            _playing = false;
            _playDone = true;
            _orderBar.ResetHighlight();
        }

        // ── 权威状态应用 ─────────────────────────────────────

        public void ApplyState(BattleStateS2C state)
        {
            if (state == null) return;
            _state = state;
            if (state.ActionDeadlineMs != _deadlineMs)
            {
                _deadlineMs = state.ActionDeadlineMs;
                _deadlineStartMs = BattleUiWidgets.NowUnixMs();
            }

            // 我方 team:按本人 actor 定位;找不到时保持上次值
            foreach (var actor in state.Actors)
            {
                if (actor.ActorId == _myId) { _myTeam = actor.TeamIndex; break; }
            }

            bool structural = _views.Count != state.Actors.Count;
            if (!structural)
            {
                foreach (var actor in state.Actors)
                {
                    if (!_viewById.ContainsKey(actor.ActorId)) { structural = true; break; }
                }
            }
            if (structural) RebuildUnits(state);
            else
            {
                foreach (var actor in state.Actors)
                {
                    if (_viewById.TryGetValue(actor.ActorId, out var view)) view.Apply(actor);
                }
            }

            _roundCounter.SetRound(state.RoundIndex, state.RoundIndex != _roundCounter.Round && _roundCounter.Round != 0);
            _partyCards.Refresh(BattleHudLogic.PartyCardOrder(state.Actors, _myId, _myTeam, BattlePartyCards.MaxCards), ResolveTile);

            // 新回合开始:重置本地已提交标记
            if (state.RoundIndex != _lastRound)
            {
                _lastRound = state.RoundIndex;
                _submitted = false;
            }
            // 服务器 pending 列表是权威:不含本人即已提交
            if (state.PendingActorIds.Count > 0)
                _submitted = !state.PendingActorIds.Contains(_myId);

            // 终局:胜方存活单位做胜利姿势(一次)
            if (state.Outcome != eBattleOutcome.BattleOutcomeOngoing && state.Outcome != _shownOutcome)
            {
                _shownOutcome = state.Outcome;
                if (state.Outcome != eBattleOutcome.BattleOutcomeDraw)
                {
                    uint winnerTeam = state.Outcome == eBattleOutcome.BattleOutcomeSideAWin ? 0u : 1u;
                    _presenter.PlayVictory(_views, winnerTeam == _myTeam);
                }
            }

            RefreshPendingText();
            RefreshActionBar();
        }

        private void RebuildUnits(BattleStateS2C state)
        {
            foreach (var view in _views) view.Destroy();
            _views.Clear();
            _viewById.Clear();

            var placement = BattleStage.AssignAll(state.Actors, _myTeam);
            foreach (var actor in state.Actors)
            {
                if (actor == null) continue;
                bool mine = actor.TeamIndex == _myTeam;
                int slot = 0;
                if (placement.TryGetValue(actor.ActorId, out var p))
                {
                    mine = p.teamIsMine;
                    slot = p.slot;
                }
                var view = new BattleUnitView(_owner, _stageRoot, actor.ActorId, actor.ActorId == _myId && _myId != 0, mine, slot, OnUnitClicked, _plateLayer)
                {
                    Fx = _presenter.Fx,
                    Numbers = _presenter.Numbers,
                    Ghosts = _presenter.Ghosts,
                };
                view.SetPlacement(BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, slot), slot);
                view.Apply(actor);
                _views.Add(view);
                _viewById[actor.ActorId] = view;
            }

            // 绘制顺序:脚底 y 升序(下方的后画,盖住上方的)
            _views.Sort((a, b) => BattleStage.CompareDepth(a.FootPosition, b.FootPosition));
            for (int i = 0; i < _views.Count; i++) _views[i].SetSiblingIndex(i);
        }

        // ── 每帧驱动(倒计时) ────────────────────────────────

        public void Tick()
        {
            if (!IsOpen) return;
            if (_playing)
            {
                // 播放中行动窗口已在倒数(服务端广播前就起算):计时环照常显示,玩家能看到留给自己的时间
                _phaseText.text = "回合播放中…";
                bool ongoing = _state == null || _state.Outcome == eBattleOutcome.BattleOutcomeOngoing;
                long remainMs = _deadlineMs != 0 && ongoing ? (long)_deadlineMs - BattleUiWidgets.NowUnixMs() : -1;
                SetTimer(remainMs, TimerRatio(remainMs));
                return;
            }
            if (_spectate)
            {
                // 观众没有本地相位,顶部信息完全由权威状态推导
                if (_state != null && _state.Outcome == eBattleOutcome.BattleOutcomeOngoing)
                {
                    long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                    _phaseText.text = remainMs > 0
                        ? "等待玩家行动"
                        : "等待回合结算…";
                    SetTimer(remainMs, TimerRatio(remainMs));
                }
                else
                {
                    _phaseText.text = "战斗已结束";
                    SetTimer(-1, 0f);
                }
                return;
            }
            switch (_phase)
            {
                case BattlePhase.WaitingAction:
                {
                    long remainMs = (long)_deadlineMs - BattleUiWidgets.NowUnixMs();
                    // 窗口已过(服务端正按默认行动结算,回合结果在路上):不再提示「请选择行动」
                    _phaseText.text = remainMs < 0 && _deadlineMs != 0 ? "等待回合结算…"
                        : _submitted ? "等待其他玩家…" : "请选择行动";
                    SetTimer(remainMs, TimerRatio(remainMs));
                    break;
                }
                case BattlePhase.Resolving:
                    _phaseText.text = "回合结算中…";
                    SetTimer(-1, 0f);
                    break;
                case BattlePhase.Ended:
                    _phaseText.text = "战斗结束";
                    SetTimer(-1, 0f);
                    break;
                default:
                    _phaseText.text = string.Empty;
                    SetTimer(-1, 0f);
                    break;
            }
        }

        private float TimerRatio(long remainMs)
        {
            long total = (long)_deadlineMs - _deadlineStartMs;
            if (total <= 0 || remainMs <= 0) return 0f;
            return Mathf.Clamp01((float)remainMs / total);
        }

        private void SetTimer(long remainMs, float ratio)
        {
            bool show = remainMs >= 0;
            if (_timerRing != null)
            {
                _timerRing.gameObject.SetActive(show);
                _timerRing.fillAmount = ratio;
                _timerRing.color = ratio < 0.25f ? BattleUiStyle.DamageText : BattleUiStyle.WarnText;
            }
            if (_timerText != null)
            {
                _timerText.gameObject.SetActive(show);
                if (show) _timerText.text = Mathf.Max(0, (int)(remainMs / 1000)).ToString();
            }
        }

        // ── 命令环交互 ──────────────────────────────────────

        private void OnCommand(BattleCommand command)
        {
            switch (command)
            {
                case BattleCommand.Attack:
                    if (!CanAct()) return;
                    CloseSubPanels();
                    _pending = PendingKind.Attack;
                    EnterTargetMode("请选择攻击目标(敌方)");
                    break;
                case BattleCommand.Spell:
                    if (!CanAct()) return;
                    if (_skillPanel.gameObject.activeSelf) { _skillPanel.gameObject.SetActive(false); return; }
                    CloseSubPanels();
                    PopulateSkillPanel();
                    _skillPanel.gameObject.SetActive(true);
                    _skillPanel.SetAsLastSibling();
                    break;
                case BattleCommand.Defend:
                    if (!CanAct()) return;
                    CloseSubPanels();
                    Submit(new BattleAction { ActionType = eBattleActionType.BattleActionDefend });
                    break;
                case BattleCommand.Item:
                    if (!CanAct()) return;
                    if (_itemPanel.gameObject.activeSelf) { _itemPanel.gameObject.SetActive(false); return; }
                    CloseSubPanels();
                    _itemPanel.gameObject.SetActive(true);
                    _itemPanel.SetAsLastSibling();
                    break;
                case BattleCommand.Summon:
                    SetHint("召唤(宠物)功能尚未开放");
                    break;
                case BattleCommand.Flee:
                    if (!CanAct()) return;
                    if (!IsPveBattle()) { SetHint("PVP 战斗不可逃跑"); return; }
                    CloseSubPanels();
                    Submit(new BattleAction { ActionType = eBattleActionType.BattleActionFlee });
                    break;
                case BattleCommand.Auto:
                    OnAutoClicked();
                    break;
            }
        }

        private void OnAutoCommand(AutoBattleCommand command)
        {
            switch (command)
            {
                case AutoBattleCommand.Character:
                    SetHint("角色面板暂未接入");
                    break;
                case AutoBattleCommand.Pet:
                    SetHint("宠物系统暂未接入");
                    break;
                case AutoBattleCommand.CancelAuto:
                {
                    var client = _owner?.Client;
                    if (client == null) { SetHint("战斗模块未就绪"); return; }
                    client.SetAutoBattle(false);
                    SetHint("已请求关闭自动战斗…");
                    break;
                }
            }
        }

        private void OnSkillPicked(uint skillTableId)
        {
            if (!CanAct()) return;
            _skillPanel.gameObject.SetActive(false);
            _pending = PendingKind.Skill;
            _pendingSkillId = skillTableId;
            EnterTargetMode($"请为 法术{skillTableId} 选择目标");
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

        /// <summary>BattleClient.OnAutoStateChanged(is_auto 权威变化)驱动:命令环换模式与置灰。</summary>
        public void OnAutoStateChanged(bool isAuto)
        {
            RefreshActionBar();
        }

        // ── 目标选择 ────────────────────────────────────────

        private void OnUnitClicked(ulong actorId)
        {
            if (_pending == PendingKind.None) return;
            if (!_viewById.TryGetValue(actorId, out var view)) return;
            if (!IsTargetable(view)) return;

            _selectedTargetId = actorId;
            foreach (var v in _views)
                v.SetHighlight(v.ActorId == actorId
                    ? SlotHighlight.Selected
                    : (IsTargetable(v) ? SlotHighlight.Targetable : SlotHighlight.None));
            _confirmButton.SetVisible(true);
        }

        private bool IsTargetable(BattleUnitView view)
        {
            if (view.IsDead || view.Fled) return false;
            // 普攻只允许打敌方;技能/道具的敌我规则依赖表数据,先放开任意存活目标
            if (_pending == PendingKind.Attack) return view.TeamIndex != _myTeam;
            return true;
        }

        private void EnterTargetMode(string hint)
        {
            _selectedTargetId = 0;
            _targetHintText.text = hint ?? string.Empty;
            foreach (var view in _views)
                view.SetHighlight(IsTargetable(view) ? SlotHighlight.Targetable : SlotHighlight.None);
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
            foreach (var view in _views) view.SetHighlight(SlotHighlight.None);
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

        /// <summary>
        /// 按 TurnPlan 播放回合表现(BattlePresenter 驱动),播完全量应用 state。
        /// 由 BattleUiRoot 以协程驱动;协程被 StopCoroutine 抢占后需再调 AbortPlayback 复位表现。
        ///
        /// 演出预算:服务端在广播本回合结果前已把下一回合 action_deadline_ms 挂上(手动 6s / 全员自动 2s),
        /// 不等客户端 Ack。按 <see cref="PlaybackBudget"/> 用 deadline − now − 输入余量 决定倍率:
        /// 够就原速(自动 1.5x),不够就压缩到上限 6x,仍塞不下则 Skip 只落终态 —— 保证命令环在
        /// 窗口内至少留出 2.5s 给玩家手动出招,自动/观战也不再每回合被下一回合抢占。
        /// </summary>
        public IEnumerator CoPlayTurn(TurnResultS2C result)
        {
            if (result == null) yield break;
            // 抢占:上一回合若还在播,先复位
            if (_presenter.IsPlaying) _presenter.Abort();

            _playing = true;
            _playDone = false;
            ExitTargetMode();
            RefreshActionBar();
            _roundCounter.SetRound(result.RoundIndex, true, " · 结算");

            // 下一回合的截止时间随本回合结果一起到达:计时环从现在起算,而不是等播完 ApplyState 才起算
            if (result.State != null && result.State.ActionDeadlineMs != _deadlineMs)
            {
                _deadlineMs = result.State.ActionDeadlineMs;
                _deadlineStartMs = BattleUiWidgets.NowUnixMs();
            }

            var plan = TurnPlan.Build(result, _myTeam);
            var order = BattleHudLogic.ResolveActionOrder(_state?.Actors, plan.ActionOrder);
            _orderBar.SetOrder(order, ResolveTile);
            _logPanel.Append($"—— 第 {result.RoundIndex} 回合 ——");

            bool myAuto = !_spectate && _owner?.Client != null && _owner.Client.IsMyActorAuto;
            bool ended = result.State != null && result.State.Outcome != eBattleOutcome.BattleOutcomeOngoing;
            var budget = PlaybackBudget.Decide(plan.TotalSeconds, result.State?.ActionDeadlineMs ?? 0UL,
                BattleUiWidgets.NowUnixMs(), passive: _spectate || myAuto,
                baseSpeed: myAuto ? BattlePresenter.AutoBattleSpeed : 1f, unbounded: ended);
            _presenter.SpeedScale = budget.Speed;
            if (!budget.Unbounded && budget.Speed > (myAuto ? BattlePresenter.AutoBattleSpeed : 1f) + 0.01f)
                _logPanel.Append($"(行动窗口剩 {budget.BudgetSeconds:0.0}s,演出 {plan.TotalSeconds:0.0}s → {budget.Speed:0.0}x)");
            _presenter.Play(plan);
            if (budget.Skip && _presenter.IsPlaying)
            {
                _logPanel.Append("(行动窗口不足,跳过本回合演出,直接落终态)");
                _presenter.Skip();
            }

            while (!_playDone) yield return null;

            _orderBar.ResetHighlight();
            if (result.State != null) ApplyState(result.State);
            _roundCounter.SetRound(result.State?.RoundIndex ?? result.RoundIndex, false);
            _playing = false;
            RefreshActionBar();
        }

        // ── 内部刷新 ────────────────────────────────────────

        private BattleTileInfo ResolveTile(ulong actorId)
        {
            var info = new BattleTileInfo { Name = ResolveName(actorId), IsSelf = actorId == _myId && _myId != 0 };
            var actor = FindActor(actorId);
            if (actor != null)
            {
                info.IsMine = actor.TeamIndex == _myTeam;
                bool monster = actor.ActorType == eBattleActorType.BattleActorTypeMonster;
                info.Portrait = monster
                    ? BattleArtCatalog.LoadMonsterPortrait(actor.MonsterTableId)
                    : BattleArtCatalog.LoadPlayerPortrait(actorId);
            }
            else
            {
                info.Portrait = BattleArtCatalog.LoadPlayerPortrait(actorId);
            }
            return info;
        }

        private string ResolveName(ulong actorId)
        {
            var actor = FindActor(actorId);
            if (actor == null) return $"单位{actorId}";
            if (!string.IsNullOrEmpty(actor.Name)) return actor.Name;
            return actor.ActorType == eBattleActorType.BattleActorTypeMonster
                ? $"怪物{actor.MonsterTableId}"
                : $"玩家{actor.ActorId}";
        }

        private BattleActorState FindActor(ulong actorId)
        {
            if (_state == null) return null;
            foreach (var actor in _state.Actors)
            {
                if (actor.ActorId == actorId) return actor;
            }
            return null;
        }

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
                        cooldown > 0 ? $"法术{skillId}(冷却{cooldown})" : $"法术{skillId}",
                        20f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                    BattleRoundCounter.ApplyNineSlice(button.Plate, "button_9slice");
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
            foreach (var actorId in _state.PendingActorIds) names.Add(ResolveName(actorId));
            _pendingText.text = $"未行动:{string.Join("、", names)}";
        }

        private void RefreshActionBar()
        {
            // 观战只读:命令环整体隐藏(不是置灰,观众根本没有行动语义)
            _commandRing.SetVisible(!_spectate);
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
            // 一期规则:PVP 不可逃跑(客户端以「敌方含玩家」近似判定,见 open_issues)
            bool canFlee = IsPveBattle();
            // 「自动」不受已提交/播放中限制:战斗内活着随时可切(Resolving 切换下回合生效)
            bool inRound = _phase == BattlePhase.WaitingAction || _phase == BattlePhase.Resolving;
            bool autoInteractable = alive && inRound;

            _commandRing.SetAutoMode(myAuto);
            _commandRing.SetState(canAct, canFlee, autoInteractable, myAuto);
            _commandRing.SetAutoKeysInteractable(true);

            if (me == null) SetHint(string.Empty);
            else if (me.IsDead) SetHint("你已阵亡,等待战斗结束…");
            else if (me.Fled) SetHint("你已逃离战斗…");
            else if (myAuto) SetHint("自动战斗中,点「取消自动」可关闭");
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
            return !BattleHudLogic.IsPvp(_state.Actors, _myTeam);
        }

        private BattleActorState FindMyActor()
        {
            if (_state == null || _myId == 0) return null;
            return FindActor(_myId);
        }

        private void SetHint(string value)
        {
            if (_hintText != null) _hintText.text = value ?? string.Empty;
        }
    }
}
