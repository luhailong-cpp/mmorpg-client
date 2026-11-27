using System;
using System.Text;
using MmorpgClient.Game.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 实时战斗与观战共用的「局外人观战交流」可折叠抽屉（零新切图，复用既有战斗 9-slice 底板与控件）：
    /// - 离线回放：彻底隐藏，不发请求；
    /// - 本局关闭观战交流（SpectateChatEnabled == false）：
    ///     * 参战端：彻底隐藏，零打扰；
    ///     * 观战端：左侧显示温和状态牌「静默观战·禁交流」（点击提示「本局已关闭观战交流，仍可正常观战」），无输入框、零轮询；
    /// - 本局开启观战交流（SpectateChatEnabled == true）：
    ///     * 显示「观战交流」折叠按钮（带未读角标），展开后查看全房公开流（标明【参战】/【观战】）、发言（支持回车与冷却倒计时）与一键收起。
    /// </summary>
    public sealed class BattleSpectateChatPanel
    {
        public static readonly Rect PanelRect = new Rect(40f, 176f, 680f, 470f);
        private const int VisibleLineLimit = 40;

        private readonly UiTextButton _toggleButton;
        private readonly RectTransform _panel;
        private readonly TMP_Text _titleText;
        private readonly TMP_Text _messagesText;
        private readonly ScrollRect _scroll;
        private readonly RectTransform _scrollContent;
        private readonly TMP_Text _statusText;
        private readonly TMP_InputField _input;
        private readonly UiTextButton _sendButton;
        private readonly Action<string> _showToast;

        private BattleSpectateChatClient _boundClient;
        private ulong _battleId;
        private bool _chatEnabled;
        private bool _isSpectating;
        private bool _isReplay;
        private ulong _lastReadSeq;
        private float _lastCooldownRefreshAt;

        public bool IsDrawerVisible => _panel != null && _panel.gameObject.activeSelf;
        public RectTransform ButtonRoot => _toggleButton.Rect;

        public BattleSpectateChatPanel(UnityEngine.Transform parent, Action<string> showToast = null)
        {
            _showToast = showToast;
            _toggleButton = BattleUiWidgets.CreateTextButton("BattleSpectateChatBtn", parent,
                BattleScreen.SpectateChatButtonRect.x, BattleScreen.SpectateChatButtonRect.y,
                BattleScreen.SpectateChatButtonRect.width, BattleScreen.SpectateChatButtonRect.height,
                "观战交流", 21f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_toggleButton.Plate, "button_9slice");
            _toggleButton.Button.onClick.AddListener(OnToggleButtonClicked);
            _toggleButton.SetVisible(false);

            var bg = BattleUiWidgets.CreatePanel("BattleSpectateChatPanel", parent,
                PanelRect.x, PanelRect.y, PanelRect.width, PanelRect.height, BattleUiStyle.PanelBg);
            BattleRoundCounter.ApplyNineSlice(bg, "panel_9slice");
            _panel = bg.rectTransform;

            _titleText = QdaoUguiFactory.CreateText("Title", _panel, 20f, 12f, 540f, 34f,
                "观战交流（全房公开）", 22f, BattleUiStyle.WarnText);
            var close = BattleUiWidgets.CreateTextButton("Close", _panel, PanelRect.width - 56f, 10f, 44f, 40f,
                "×", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            close.Button.onClick.AddListener(() => SetDrawerVisible(false));

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(_panel, false);
            var vpRect = (RectTransform)viewportGo.transform;
            vpRect.anchorMin = new Vector2(0f, 1f);
            vpRect.anchorMax = new Vector2(0f, 1f);
            vpRect.pivot = new Vector2(0f, 1f);
            vpRect.anchoredPosition = new Vector2(18f, -54f);
            vpRect.sizeDelta = new Vector2(PanelRect.width - 36f, 300f);
            var vpImg = viewportGo.GetComponent<Image>();
            vpImg.color = new Color(0.06f, 0.08f, 0.09f, 0.55f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = true;

            _scrollContent = QdaoUguiFactory.CreateRect("Content", vpRect, 0f, 0f, PanelRect.width - 36f, 300f);
            _messagesText = BattleUiWidgets.CreateWrappedText("Messages", _scrollContent, 12f, 8f,
                PanelRect.width - 60f, 284f, string.Empty, 19f, QdaoUguiTheme.StatusCream);
            _messagesText.richText = false;

            _scroll = viewportGo.AddComponent<ScrollRect>();
            _scroll.viewport = vpRect;
            _scroll.content = _scrollContent;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            _statusText = QdaoUguiFactory.CreateText("Status", _panel, 20f, 360f, PanelRect.width - 40f, 28f,
                string.Empty, 16f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.MidlineLeft);
            _statusText.overflowMode = TextOverflowModes.Ellipsis;

            var inputPlate = BattleUiWidgets.CreatePanel("InputPlate", _panel,
                18f, 394f, PanelRect.width - 170f, 56f, BattleUiStyle.PanelBgLight);
            _input = QdaoUguiFactory.CreateInputField("ChatInput", inputPlate.transform,
                0f, 0f, PanelRect.width - 170f, 56f, "发句话和大家聊聊（≤60字）…", 19);
            _input.characterLimit = 60;
            _input.onSubmit.AddListener(_ => OnSendClicked());

            _sendButton = BattleUiWidgets.CreateTextButton("Send", _panel,
                PanelRect.width - 142f, 394f, 124f, 56f,
                "发送", 21f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            BattleRoundCounter.ApplyNineSlice(_sendButton.Plate, "button_9slice");
            _sendButton.Button.onClick.AddListener(OnSendClicked);

            _panel.gameObject.SetActive(false);
        }

        /// <summary>由 BattleScreen.Open / ApplyState 调用，同步权威战斗与观战交流开关。</summary>
        public void Sync(BattleStateS2C state, bool isSpectating, bool isReplay)
        {
            EnsureBound();
            _isSpectating = isSpectating;
            _isReplay = isReplay;
            ulong nextBattleId = (!isReplay && state != null) ? state.BattleId : 0UL;
            bool nextEnabled = !isReplay && state != null && state.SpectateChatEnabled;
            bool battleSwitched = nextBattleId != _battleId;
            _battleId = nextBattleId;
            _chatEnabled = nextEnabled;

            if (battleSwitched)
            {
                _lastReadSeq = 0UL;
                SetDrawerVisible(false);
                if (_input != null) _input.text = string.Empty;
            }

            if (_isReplay || _battleId == 0UL)
            {
                _boundClient?.ClearBattleContext();
                _toggleButton.SetVisible(false);
                SetDrawerVisible(false);
                return;
            }

            _boundClient?.SyncBattleContext(_battleId, _chatEnabled, pollingActive: _chatEnabled);

            if (!_chatEnabled)
            {
                SetDrawerVisible(false);
                // 关闭交流时：参战端彻底隐藏零打扰；观战端展示温和只读提示牌（仍可正常观战）
                _toggleButton.SetVisible(_isSpectating);
                if (_isSpectating) _toggleButton.SetText("静默观战·禁交流");
                return;
            }

            _toggleButton.SetVisible(true);
            RefreshUi();
        }

        public void Tick()
        {
            if (_isReplay || _battleId == 0UL || !_chatEnabled) return;
            EnsureBound();
            if (Time.unscaledTime - _lastCooldownRefreshAt >= 0.25f)
            {
                _lastCooldownRefreshAt = Time.unscaledTime;
                RefreshSendAvailability();
            }
        }

        public void ResetAndHide()
        {
            _battleId = 0UL;
            _chatEnabled = false;
            _isSpectating = false;
            _isReplay = false;
            _lastReadSeq = 0UL;
            _boundClient?.ClearBattleContext();
            if (_input != null) _input.text = string.Empty;
            SetDrawerVisible(false);
            _toggleButton.SetVisible(false);
        }

        public void Dispose()
        {
            UnbindClient();
        }

        // ── 内部 ─────────────────────────────────────────────

        private void EnsureBound()
        {
            var current = BattleSpectateChatClient.Instance;
            if (ReferenceEquals(_boundClient, current)) return;
            UnbindClient();
            _boundClient = current;
            if (_boundClient != null)
            {
                _boundClient.ChatChanged += RefreshUi;
            }
        }

        private void UnbindClient()
        {
            if (_boundClient != null)
            {
                _boundClient.ChatChanged -= RefreshUi;
                _boundClient = null;
            }
        }

        private void OnToggleButtonClicked()
        {
            if (_isReplay || _battleId == 0UL) return;
            if (!_chatEnabled)
            {
                _showToast?.Invoke("本场战斗已关闭观战交流，仍可正常观战");
                return;
            }
            SetDrawerVisible(!IsDrawerVisible);
        }

        private void SetDrawerVisible(bool visible)
        {
            if (_panel == null) return;
            if (visible && (!_chatEnabled || _isReplay || _battleId == 0UL)) visible = false;
            if (_panel.gameObject.activeSelf != visible)
            {
                _panel.gameObject.SetActive(visible);
                if (visible)
                {
                    _panel.SetAsLastSibling();
                    if (_boundClient != null)
                    {
                        _lastReadSeq = _boundClient.LatestSeq;
                        _boundClient.Pull(_battleId);
                    }
                    RefreshUi();
                }
            }
        }

        private void OnSendClicked()
        {
            EnsureBound();
            if (_boundClient == null || _battleId == 0UL || !_chatEnabled || _input == null) return;
            string text = (_input.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (_boundClient.Send(_battleId, text))
            {
                _input.text = string.Empty;
            }
        }

        private void RefreshUi()
        {
            if (_panel == null || _toggleButton.Rect == null) return;
            if (_isReplay || _battleId == 0UL)
            {
                _toggleButton.SetVisible(false);
                SetDrawerVisible(false);
                return;
            }

            if (_boundClient != null && _boundClient.ActiveBattleId == _battleId && !_boundClient.ChatEnabled)
            {
                _chatEnabled = false;
                SetDrawerVisible(false);
                _toggleButton.SetVisible(_isSpectating);
                if (_isSpectating) _toggleButton.SetText("静默观战·禁交流");
                return;
            }

            if (!_chatEnabled) return;

            if (IsDrawerVisible && _boundClient != null)
            {
                _lastReadSeq = _boundClient.LatestSeq;
            }

            ulong unread = 0UL;
            if (_boundClient != null && !IsDrawerVisible && _boundClient.LatestSeq > _lastReadSeq)
            {
                unread = _boundClient.LatestSeq - _lastReadSeq;
            }
            _toggleButton.SetText(unread > 0UL ? $"观战交流 ({Math.Min(unread, 99UL)})" : "观战交流");

            if (_titleText != null)
            {
                _titleText.text = _boundClient != null && _boundClient.BattleEnded
                    ? "观战交流（本局已结束）"
                    : "观战交流（全房公开 · 不含队内私密）";
            }

            RefreshMessagesText();
            RefreshSendAvailability();
        }

        private void RefreshMessagesText()
        {
            if (_messagesText == null) return;
            var msgs = _boundClient?.Messages;
            if (msgs == null || msgs.Count == 0)
            {
                _messagesText.text = "本局已开放局外人观战交流（全房公开，队内战术频道完全隔离），发句话聊聊吧。";
                if (_scrollContent != null) _scrollContent.sizeDelta = new Vector2(_scrollContent.sizeDelta.x, 300f);
                return;
            }
            var sb = new StringBuilder();
            int start = Math.Max(0, msgs.Count - VisibleLineLimit);
            for (int i = start; i < msgs.Count; i++)
            {
                var m = msgs[i];
                if (sb.Length > 0) sb.Append('\n');
                sb.Append('[').Append(m.RoleTag).Append("] ")
                  .Append(m.SenderName).Append("：")
                  .Append(m.Content);
            }
            _messagesText.text = sb.ToString();
            float preferredH = Math.Max(284f, _messagesText.GetPreferredValues(_messagesText.text, PanelRect.width - 60f, float.PositiveInfinity).y + 18f);
            _messagesText.rectTransform.sizeDelta = new Vector2(PanelRect.width - 60f, preferredH);
            if (_scrollContent != null)
            {
                _scrollContent.sizeDelta = new Vector2(_scrollContent.sizeDelta.x, Math.Max(300f, preferredH + 16f));
                if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
            }
        }

        private void RefreshSendAvailability()
        {
            if (_boundClient == null) return;
            bool ended = _boundClient.BattleEnded;
            double cd = _boundClient.CooldownRemainingSeconds();
            bool canSend = !ended && !_boundClient.Sending && cd <= 0.05;

            if (_input != null) _input.interactable = !ended;
            if (_sendButton.Button != null) _sendButton.SetInteractable(canSend);

            if (_statusText != null)
            {
                if (ended)
                    _statusText.text = "本场战斗已结束，观战交流已关闭";
                else if (!string.IsNullOrEmpty(_boundClient.LastError))
                    _statusText.text = _boundClient.LastError;
                else if (_boundClient.Sending)
                    _statusText.text = "发送中…";
                else if (cd > 0.05)
                    _statusText.text = $"发言冷却 {Mathf.CeilToInt((float)cd)} 秒";
                else
                    _statusText.text = "全房公开交流 · 双方与观众同步可见";
            }
        }
    }
}
