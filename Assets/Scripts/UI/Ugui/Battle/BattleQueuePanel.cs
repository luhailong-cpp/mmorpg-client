using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 排队面板(场景内悬浮):
    ///   - 模式选择:PVE 单人 / PVE 组队(5人) / PVP 1v1 / PVP 5v5
    ///     (battle_config_id 用 BattleUiStyle 可配常量)
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
        private readonly UiTextButton _pveTeamButton;
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

            _pveButton = BattleUiWidgets.CreateTextButton("JoinPve", _root, 30f, 76f, 460f, 74f,
                "匹配 PVE(单人)", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pveButton.Button.onClick.AddListener(OnJoinPveClicked);

            _pveTeamButton = BattleUiWidgets.CreateTextButton("JoinPveTeam", _root, 30f, 166f, 460f, 74f,
                "匹配 PVE(组队5人)", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pveTeamButton.Button.onClick.AddListener(OnJoinPveTeamClicked);

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

        private void OnJoinPveClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.JoinQueue(Match.MatchMode.PveSolo, BattleUiStyle.PveSoloBattleConfigId);
            SetStatus("已请求匹配 PVE(单人),等待服务器…");
        }

        private void OnJoinPvpClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.JoinQueue(Match.MatchMode._1V1, BattleUiStyle.Pvp1V1BattleConfigId);
            SetStatus("已请求匹配 PVP(1v1),等待服务器…");
        }

        private void OnJoinPveTeamClicked()
        {
            var client = _owner?.Client;
            if (client == null) { SetStatus("战斗模块未就绪"); return; }
            client.JoinQueue(Match.MatchMode.PveTeam, BattleUiStyle.PveTeamBattleConfigId);
            SetStatus("已请求匹配 PVE(组队 5 人),等待凑满队伍…");
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
            _pveTeamButton?.SetInteractable(idle);
            _pvpButton?.SetInteractable(idle);
            _pvp5V5Button?.SetInteractable(idle);
            _duelButton?.SetInteractable(idle);
            _cancelButton?.SetVisible(_queueing);
            if (_queueText != null && !_queueing) _queueText.text = string.Empty;
        }
    }
}
