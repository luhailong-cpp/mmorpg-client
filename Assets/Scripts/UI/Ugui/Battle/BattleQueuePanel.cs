using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 排队面板(场景内悬浮):
    ///   - 模式选择:PVE(1~5 人档) / PVP 1v1 / PVP 5v5
    ///     (battle_config_id 用 BattleUiStyle 可配常量)
    ///   - PVE 人数档:先点人数再点匹配。1 人即时开战;2 人起进该人数档的队列,凑满即开。
    ///     怪物数量随人数成倍增加:每人对应一整组怪(服务端按人数生成),单人打一组,N 人打 N 组。
    ///     已经组好队的由队长在队伍面板开战,不走这里。
    ///   - 「连续战斗」开关:纯客户端(BattleClient.ContinuousBattle),
    ///     结算面板收起(AckBattleEnd)后按上次参数自动重排(§11.2)
    ///   - 排队中状态:已等秒数 / 预计等待(来自 BattleClient.OnQueueStatus)
    ///   - 取消排队
    ///   - 切磋 PK 极简入口:当前场景没有 actor 点击选中交互(见 open_issues),
    ///     以手动输入目标玩家 ID 的方式发起 ChallengePlayer。
    /// </summary>
    public sealed class BattleQueuePanel
    {
        private const float PanelX = 60f;
        private const float PanelY = 90f;
        private const float PanelW = 520f;
        private const float PanelH = 920f;

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;
        private readonly UiTextButton _pveButton;
        private readonly UiTextButton[] _pveSizeButtons;
        private uint _pveTeamSize = 1; // 当前选中的 PVE 人数档(1..BattleUiStyle.PveTeamMaxSize)
        private readonly UiTextButton _pvpButton;
        private readonly UiTextButton _pvp5V5Button;
        private readonly UiTextButton _continuousButton;
        private readonly UiTextButton _cancelButton;
        private readonly UiTextButton _duelButton;
        private readonly UiTextButton _closeButton;
        private readonly TMP_Text _queueText;
        private readonly TMP_Text _statusText;
        private readonly TMP_InputField _targetInput;

        private bool _queueing;
        private float _queueStartRealtime;
        private uint _serverQueuedSeconds;
        private uint _serverEstimateSeconds;
        private float _serverStatusAt = -1f; // realtimeSinceStartup;<0 表示尚无服务器状态

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public BattleQueuePanel(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;

            var bg = BattleUiWidgets.CreatePanel("BattleQueuePanel", parent,
                PanelX, PanelY, PanelW, PanelH, BattleUiStyle.PanelBg);
            _root = (RectTransform)bg.transform;

            QdaoUguiFactory.CreateText("Title", _root, 24f, 16f, 320f, 42f,
                "回合战斗", 28f, QdaoUguiTheme.Cream);
            _closeButton = BattleUiWidgets.CreateTextButton("Close", _root, 456f, 12f, 48f, 48f,
                "×", 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _closeButton.Button.onClick.AddListener(Hide);

            // PVE 人数档:一排等宽小按钮(原「单人 / 组队5人」两个大按钮占的那块区域,下方布局不动)
            int sizeCount = (int)BattleUiStyle.PveTeamMaxSize;
            const float sizeRowWidth = 460f;
            const float sizeGap = 8f;
            float sizeWidth = (sizeRowWidth - sizeGap * (sizeCount - 1)) / sizeCount;
            _pveSizeButtons = new UiTextButton[sizeCount];
            for (int i = 0; i < sizeCount; i++)
            {
                uint size = (uint)(i + 1); // 闭包各自捕获
                _pveSizeButtons[i] = BattleUiWidgets.CreateTextButton($"PveSize{size}", _root,
                    30f + i * (sizeWidth + sizeGap), 76f, sizeWidth, 64f,
                    $"{size}人", 22f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                _pveSizeButtons[i].Button.onClick.AddListener(() => SelectPveTeamSize(size));
            }

            _pveButton = BattleUiWidgets.CreateTextButton("JoinPve", _root, 30f, 154f, 460f, 86f,
                string.Empty, 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pveButton.Button.onClick.AddListener(OnJoinPveClicked);
            RefreshPveSizeSelection();

            _pvpButton = BattleUiWidgets.CreateTextButton("JoinPvp", _root, 30f, 256f, 460f, 74f,
                "匹配 PVP(1v1)", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pvpButton.Button.onClick.AddListener(OnJoinPvpClicked);

            _pvp5V5Button = BattleUiWidgets.CreateTextButton("JoinPvp5V5", _root, 30f, 346f, 460f, 74f,
                "匹配 PVP(5v5)", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pvp5V5Button.Button.onClick.AddListener(OnJoinPvp5V5Clicked);

            _queueText = QdaoUguiFactory.CreateText("QueueText", _root, 30f, 436f, 460f, 40f,
                string.Empty, 20f, BattleUiStyle.WarnText);

            _cancelButton = BattleUiWidgets.CreateTextButton("CancelQueue", _root, 30f, 480f, 460f, 64f,
                "取消排队", 22f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _cancelButton.Button.onClick.AddListener(OnCancelClicked);

            _continuousButton = BattleUiWidgets.CreateTextButton("Continuous", _root, 30f, 556f, 460f, 56f,
                "连续战斗:关", 20f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _continuousButton.Button.onClick.AddListener(OnContinuousClicked);

            QdaoUguiFactory.CreateText("DuelDivider", _root, 30f, 636f, 460f, 30f,
                "── 切磋 PK ──", 20f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.Center);
            QdaoUguiFactory.CreateText("DuelHint", _root, 30f, 672f, 460f, 30f,
                "暂无场景点选目标,请输入对方玩家ID:", 18f, QdaoUguiTheme.StatusCream);

            var inputPlate = BattleUiWidgets.CreatePanel("DuelInputPlate", _root,
                30f, 710f, 296f, 56f, BattleUiStyle.PanelBgLight);
            _targetInput = QdaoUguiFactory.CreateInputField("DuelInput", inputPlate.transform,
                0f, 0f, 296f, 56f, "对方玩家ID", 20);
            _targetInput.contentType = TMP_InputField.ContentType.IntegerNumber;

            _duelButton = BattleUiWidgets.CreateTextButton("Duel", _root, 342f, 710f, 148f, 56f,
                "发起切磋", 20f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _duelButton.Button.onClick.AddListener(OnDuelClicked);

            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root, 30f, 784f, 460f, 110f,
                string.Empty, 18f, QdaoUguiTheme.StatusCream);

            RefreshButtons();
            RefreshContinuousButton();
            Hide();
        }

        public void Show()
        {
            if (_root != null) _root.gameObject.SetActive(true);
            RefreshContinuousButton(); // 文案与 BattleClient.ContinuousBattle 对齐(懒绑定时序)
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        /// <summary>由 Phase 变化驱动(Queued 进入/离开)。</summary>
        public void SetQueueing(bool value)
        {
            if (_queueing != value)
            {
                _queueing = value;
                if (value)
                {
                    _queueStartRealtime = Time.realtimeSinceStartup;
                    _serverStatusAt = -1f;
                }
                else
                {
                    if (_queueText != null) _queueText.text = string.Empty;
                }
            }
            RefreshButtons();
        }

        /// <summary>BattleClient.OnQueueStatus 转发进来的服务器排队状态。</summary>
        public void ApplyStatus(Match.GetQueueStatusResponse status)
        {
            if (status == null) return;
            if (status.State == Match.QueueState.Queued || status.State == Match.QueueState.Matched)
            {
                _serverQueuedSeconds = status.QueuedSeconds;
                _serverEstimateSeconds = status.EstimatedWaitSeconds;
                _serverStatusAt = Time.realtimeSinceStartup;
            }
        }

        public void SetStatus(string value)
        {
            if (_statusText != null) _statusText.text = value ?? string.Empty;
        }

        /// <summary>每帧刷新排队计时文案。</summary>
        public void Tick()
        {
            if (!IsVisible || !_queueing || _queueText == null) return;
            uint waited = _serverStatusAt >= 0f
                ? _serverQueuedSeconds + (uint)Mathf.Max(0f, Time.realtimeSinceStartup - _serverStatusAt)
                : (uint)Mathf.Max(0f, Time.realtimeSinceStartup - _queueStartRealtime);
            string estimate = (_serverStatusAt >= 0f && _serverEstimateSeconds > 0)
                ? $"{_serverEstimateSeconds} 秒"
                : "--";
            _queueText.text = $"排队中… 已等 {waited} 秒 / 预计 {estimate}";
        }

        // ── 交互 ─────────────────────────────────────────────

        private void SelectPveTeamSize(uint size)
        {
            if (_queueing) return; // 排队中不改档:已发出的请求按发出时的人数算
            _pveTeamSize = size;
            RefreshPveSizeSelection();
        }

        /// <summary>
        /// 选中的人数档换古铜色底并在文案前加「√」(两态都显式赋值,切档时旧的要还原)。
        /// 用「√」(U+221A)而不是「✓」(U+2713):本面板的字体 SimKai 里没有后者的字形,会显示成方框;
        /// 匹配按钮文案写明人数与怪物倍数(客户端不知道一组怪有几只,只写倍数)。
        /// </summary>
        private void RefreshPveSizeSelection()
        {
            if (_pveSizeButtons != null)
            {
                for (int i = 0; i < _pveSizeButtons.Length; i++)
                {
                    var sizeButton = _pveSizeButtons[i];
                    if (sizeButton == null) continue;
                    uint size = (uint)(i + 1);
                    bool selected = size == _pveTeamSize;
                    if (sizeButton.Plate != null)
                    {
                        sizeButton.Plate.color = selected
                            ? BattleUiStyle.PveSizeSelectedPlate
                            : BattleUiStyle.ButtonPlate;
                    }
                    sizeButton.SetText(selected ? $"√{size}人" : $"{size}人");
                }
            }
            _pveButton?.SetText(_pveTeamSize <= 1
                ? "匹配 PVE(1人 · 立即开战)"
                : $"匹配 PVE({_pveTeamSize}人 · 怪物×{_pveTeamSize})");
        }

        private void OnJoinPveClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            if (_pveTeamSize <= 1)
            {
                // 1 人档不需要等人:沿用 PVE_SOLO 入口,服务端即时开战
                client.JoinQueue(Match.MatchMode.PveSolo, BattleUiStyle.PveSoloBattleConfigId);
                SetStatus("已请求匹配 PVE(1 人),等待服务器…");
                return;
            }
            client.JoinQueue(Match.MatchMode.PveTeam, BattleUiStyle.PveTeamBattleConfigId, _pveTeamSize);
            SetStatus($"已请求匹配 PVE({_pveTeamSize} 人,怪物数量是单人的 {_pveTeamSize} 倍),等待凑满 {_pveTeamSize} 人…");
        }

        private void OnJoinPvpClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.JoinQueue(Match.MatchMode._1V1, BattleUiStyle.Pvp1V1BattleConfigId);
            SetStatus("已请求匹配 PVP(1v1),等待服务器…");
        }

        private void OnJoinPvp5V5Clicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.JoinQueue(Match.MatchMode._5V5, BattleUiStyle.Pvp5V5BattleConfigId);
            SetStatus("已请求匹配 PVP(5v5),等待凑满 10 人…");
        }

        private void OnContinuousClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.ContinuousBattle = !client.ContinuousBattle;
            RefreshContinuousButton();
        }

        private void RefreshContinuousButton()
        {
            bool on = _owner?.Client != null && _owner.Client.ContinuousBattle;
            _continuousButton?.SetText(on ? "连续战斗:开" : "连续战斗:关");
        }

        private void OnCancelClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.CancelQueue();
            SetStatus("已请求取消排队…");
        }

        private void OnDuelClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            string raw = (_targetInput != null ? _targetInput.text : string.Empty)?.Trim();
            if (string.IsNullOrEmpty(raw) || !ulong.TryParse(raw, out ulong target) || target == 0)
            {
                SetStatus("请输入有效的目标玩家ID");
                return;
            }
            if (target == client.MyPlayerId)
            {
                SetStatus("不能挑战自己");
                return;
            }
            client.ChallengePlayer(target);
            SetStatus($"已向玩家 {target} 发起切磋,等待对方应答…");
        }

        private void RefreshButtons()
        {
            bool idle = !_queueing;
            _pveButton?.SetInteractable(idle);
            if (_pveSizeButtons != null)
            {
                foreach (var sizeButton in _pveSizeButtons) sizeButton?.SetInteractable(idle);
            }
            _pvpButton?.SetInteractable(idle);
            _pvp5V5Button?.SetInteractable(idle);
            _duelButton?.SetInteractable(idle);
            _cancelButton?.SetVisible(_queueing);
            if (_queueText != null && !_queueing) _queueText.text = string.Empty;
        }
    }
}
