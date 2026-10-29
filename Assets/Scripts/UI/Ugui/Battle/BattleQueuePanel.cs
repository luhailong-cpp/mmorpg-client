using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 排队面板(场景内悬浮):
    ///   - 模式选择:PVE 按现有人数 / PVP 每方 1～5 人
    ///     (battle_config_id 用 BattleUiStyle 可配常量)
    ///   - PVE 单人或队伍立即开战；PVP 选择每方人数，等待真人，整队不拆。
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
        private readonly UiTextButton[] _sizeButtons;
        private uint _pvpTeamSize = 1; // PVP 每方目标人数；PVE 始终按权威队伍现有人数。
        private readonly UiTextButton _pvpButton;
        private readonly TMP_Text _matchHint;
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
        /// <summary>状态行当前的文字(最后一次 SetStatus 写入的内容)。</summary>
        public string StatusText => _statusText != null ? _statusText.text : string.Empty;

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

            // 复用原人数档位置，仅用于 PVP 每方目标人数。
            int sizeCount = (int)BattleUiStyle.PveTeamMaxSize;
            const float sizeRowWidth = 460f;
            const float sizeGap = 8f;
            float sizeWidth = (sizeRowWidth - sizeGap * (sizeCount - 1)) / sizeCount;
            _sizeButtons = new UiTextButton[sizeCount];
            for (int i = 0; i < sizeCount; i++)
            {
                uint size = (uint)(i + 1); // 闭包各自捕获
                _sizeButtons[i] = BattleUiWidgets.CreateTextButton($"PvpSize{size}", _root,
                    30f + i * (sizeWidth + sizeGap), 76f, sizeWidth, 64f,
                    $"{size}v{size}", 22f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
                _sizeButtons[i].Button.onClick.AddListener(() => SelectPvpTeamSize(size));
            }

            _pveButton = BattleUiWidgets.CreateTextButton("JoinPve", _root, 30f, 154f, 460f, 86f,
                string.Empty, 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pveButton.Button.onClick.AddListener(OnJoinPveClicked);
            RefreshMatchSelection();

            _pvpButton = BattleUiWidgets.CreateTextButton("JoinPvp", _root, 30f, 256f, 460f, 74f,
                "匹配 PVP(1v1)", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _pvpButton.Button.onClick.AddListener(OnJoinPvpClicked);

            _matchHint = BattleUiWidgets.CreateWrappedText("MatchHint", _root, 30f, 344f, 460f, 80f,
                "PVE 按现有人数立即开战；PVP 等待真人，已组队由队长发起。", 18f, QdaoUguiTheme.StatusCream);

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
            BattleFidelityArt.ApplyPanel(_root);
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
            if (IsVisible) RefreshMatchSelection();
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

        private void SelectPvpTeamSize(uint size)
        {
            if (_queueing) return; // 排队中不改档:已发出的请求按发出时的人数算
            _pvpTeamSize = size;
            RefreshMatchSelection();
        }

        /// <summary>
        /// 选中的人数档换古铜色底并在文案前加「√」(两态都显式赋值,切档时旧的要还原)。
        /// 用「√」(U+221A)而不是「✓」(U+2713):本面板的字体 SimKai 里没有后者的字形,会显示成方框;
        /// 匹配按钮文案写明人数与怪物倍数(客户端不知道一组怪有几只,只写倍数)。
        /// </summary>
        private void RefreshMatchSelection()
        {
            var entry = _owner?.MatchEntry;
            uint members = entry?.CurrentSize ?? 1;
            if (!_queueing && _pvpTeamSize < members) _pvpTeamSize = members;
            if (_sizeButtons != null)
                for (int i = 0; i < _sizeButtons.Length; i++)
                {
                    var button = _sizeButtons[i];
                    if (button == null) continue;
                    uint size = (uint)(i + 1);
                    bool selected = size == _pvpTeamSize;
                    if (button.Plate != null) button.Plate.color = selected ? BattleUiStyle.PveSizeSelectedPlate : BattleUiStyle.ButtonPlate;
                    button.SetText((selected ? "√" : "") + $"{size}v{size}");
                    button.SetInteractable(!_queueing && entry?.Pending != true && size >= members);
                }
            _pveButton?.SetText($"PVE（{members}人 · 立即开战）");
            _pvpButton?.SetText($"匹配 PVP（{_pvpTeamSize}v{_pvpTeamSize} · 真人）");
            bool ready = !_queueing && entry?.Pending != true && entry?.IsMember != true;
            _pveButton?.SetInteractable(ready);
            _pvpButton?.SetInteractable(ready);
            if (_matchHint != null) _matchHint.text = entry?.IsMember == true ? "你是队员，请由队长发起全队战斗。"
                : entry?.Pending == true ? "正在同步队伍，确认后发起战斗…"
                : "上方人数为 PVP 每方目标。PVE 按现有人数开战；PVP 等待真人，队伍一起入场。";
        }

        private void OnJoinPveClicked()
        {
            var entry = _owner?.MatchEntry;
            if (entry == null) { SetStatus("正在同步战斗与队伍模块，请稍候…"); return; }
            entry.StartPve();
            SetStatus(entry.Status);
        }

        private void OnJoinPvpClicked()
        {
            var entry = _owner?.MatchEntry;
            if (entry == null) { SetStatus("正在同步战斗与队伍模块，请稍候…"); return; }
            entry.StartPvp(_pvpTeamSize);
            SetStatus(entry.Status);
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
            SetStatus("已请求取消排队…");
            if (_owner?.MatchEntry != null) _owner.MatchEntry.Cancel();
            else client.CancelQueue();
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
            SetStatus($"已向玩家 {target} 发起切磋,等待对方应答…");
            client.ChallengePlayer(target);
        }

        private void RefreshButtons()
        {
            bool idle = !_queueing;
            _pveButton?.SetInteractable(idle);
            if (_sizeButtons != null)
            {
                foreach (var sizeButton in _sizeButtons) sizeButton?.SetInteractable(idle);
            }
            _pvpButton?.SetInteractable(idle);
            RefreshMatchSelection();
            _duelButton?.SetInteractable(idle);
            _cancelButton?.SetVisible(_queueing);
            if (_queueText != null && !_queueing) _queueText.text = string.Empty;
        }
    }
}
