using System;
using System.Text;
using MmorpgClient.Game.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 实时战斗与观战共用的「局外人观战交流」可折叠抽屉（已接入 BattleSpectateChatV1 4 枚专属透明 PNG-32 切片，缺失时优雅回落战斗 9-slice 底板）：
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
        public const string DrawerBgResourcePath = "UI/Ugui/BattleSpectateChatV1/spectate_chat_drawer_bg";
        public const string EntryOpenBgResourcePath = "UI/Ugui/BattleSpectateChatV1/spectate_chat_entry_open_bg";
        public const string EntrySilentBgResourcePath = "UI/Ugui/BattleSpectateChatV1/spectate_chat_entry_silent_bg";
        public const string SendBtnBgResourcePath = "UI/Ugui/BattleSpectateChatV1/spectate_chat_send_btn_bg";
        private const int VisibleLineLimit = 40;
        private const float ViewportHeight = 248f;
        private static readonly string[] QuickPhrases = { "好预判！", "保一手宝宝", "极限翻盘！", "稳住别浪" };
        private static readonly Color ParchmentInputBg = new Color(0.90f, 0.85f, 0.75f, 0.90f);
        private static readonly Color ParchmentChipBg = new Color(0.86f, 0.81f, 0.71f, 0.92f);
        private static readonly Color ParchmentSeparatorColor = new Color(0.75f, 0.65f, 0.50f, 0.55f);
        private static readonly Color SendCooldownTint = new Color(0.72f, 0.72f, 0.70f, 0.85f);

        private readonly UiTextButton _toggleButton;
        private readonly RectTransform _panel;
        private readonly TMP_Text _titleText;
        private readonly UiTextButton _muteButton;
        private readonly TMP_Text _messagesText;
        private readonly ScrollRect _scroll;
        private readonly RectTransform _scrollContent;
        private readonly UiTextButton[] _quickButtons;
        private readonly TMP_Text _statusText;
        private readonly TMP_InputField _input;
        private readonly UiTextButton _sendButton;
        private readonly Action<string> _showToast;
        private readonly Sprite _entryOpenSprite;
        private readonly Sprite _entrySilentSprite;
        private readonly bool _hasCustomSendSprite;

        private BattleSpectateChatClient _boundClient;
        private ulong _battleId;
        private bool _chatEnabled;
        private bool _isSpectating;
        private bool _isReplay;
        private bool _localMuted;
        private ulong _lastReadSeq;
        private float _lastCooldownRefreshAt;

        public bool IsDrawerVisible => _panel != null && _panel.gameObject.activeSelf;
        public RectTransform ButtonRoot => _toggleButton.Rect;

        public BattleSpectateChatPanel(UnityEngine.Transform parent, Action<string> showToast = null)
        {
            _showToast = showToast;
            var drawerSprite = Resources.Load<Sprite>(DrawerBgResourcePath);
            _entryOpenSprite = Resources.Load<Sprite>(EntryOpenBgResourcePath);
            _entrySilentSprite = Resources.Load<Sprite>(EntrySilentBgResourcePath);
            var sendSprite = Resources.Load<Sprite>(SendBtnBgResourcePath);
            bool hasCustomDrawer = drawerSprite != null;
            _hasCustomSendSprite = sendSprite != null;

            _toggleButton = BattleUiWidgets.CreateTextButton("BattleSpectateChatBtn", parent,
                BattleScreen.SpectateChatButtonRect.x, BattleScreen.SpectateChatButtonRect.y,
                BattleScreen.SpectateChatButtonRect.width, BattleScreen.SpectateChatButtonRect.height,
                "观战交流", 20f, BattleUiStyle.ButtonPlate, QdaoUguiTheme.Cream);
            ApplyToggleVisual(open: true);
            _toggleButton.Button.onClick.AddListener(OnToggleButtonClicked);
            _toggleButton.SetVisible(false);

            var bg = BattleUiWidgets.CreatePanel("BattleSpectateChatPanel", parent,
                PanelRect.x, PanelRect.y, PanelRect.width, PanelRect.height,
                hasCustomDrawer ? Color.white : BattleUiStyle.PanelBg);
            if (hasCustomDrawer)
            {
                bg.sprite = drawerSprite;
                bg.type = Image.Type.Sliced;
            }
            else
            {
                BattleRoundCounter.ApplyNineSlice(bg, "panel_9slice");
            }
            _panel = bg.rectTransform;

            _titleText = QdaoUguiFactory.CreateText("Title", _panel, 24f, 8f, 456f, 38f,
                "观战交流（全房公开）", 21f, hasCustomDrawer ? QdaoUguiTheme.Cream : BattleUiStyle.WarnText);
            _muteButton = BattleUiWidgets.CreateTextButton("MuteBattle", _panel, PanelRect.width - 152f, 9f, 88f, 36f,
                "静音本局", 16f, hasCustomDrawer ? new Color(0f, 0f, 0f, 0.18f) : BattleUiStyle.ButtonPlate, QdaoUguiTheme.Cream);
            _muteButton.Button.onClick.AddListener(OnMuteClicked);
            var close = BattleUiWidgets.CreateTextButton("Close", _panel, PanelRect.width - 58f, 7f, 44f, 40f,
                "×", 24f, hasCustomDrawer ? new Color(0f, 0f, 0f, 0f) : BattleUiStyle.ButtonPlate, QdaoUguiTheme.Cream);
            close.Button.onClick.AddListener(() => SetDrawerVisible(false));

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(_panel, false);
            var vpRect = (RectTransform)viewportGo.transform;
            vpRect.anchorMin = new Vector2(0f, 1f);
            vpRect.anchorMax = new Vector2(0f, 1f);
            vpRect.pivot = new Vector2(0f, 1f);
            vpRect.anchoredPosition = new Vector2(22f, -60f);
            vpRect.sizeDelta = new Vector2(PanelRect.width - 44f, ViewportHeight);
            if (!hasCustomDrawer)
            {
                var vpImg = viewportGo.AddComponent<Image>();
                vpImg.color = new Color(0.06f, 0.08f, 0.09f, 0.55f);
            }

            _scrollContent = QdaoUguiFactory.CreateRect("Content", vpRect, 0f, 0f, PanelRect.width - 44f, ViewportHeight);
            _messagesText = BattleUiWidgets.CreateWrappedText("Messages", _scrollContent, 8f, 6f,
                PanelRect.width - 60f, ViewportHeight - 12f, string.Empty, 19f,
                hasCustomDrawer ? QdaoUguiTheme.DarkBrown : QdaoUguiTheme.StatusCream);
            _messagesText.richText = false;

            _scroll = viewportGo.AddComponent<ScrollRect>();
            _scroll.viewport = vpRect;
            _scroll.content = _scrollContent;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            if (hasCustomDrawer)
            {
                var sep = BattleUiWidgets.CreatePanel("Separator", _panel,
                    24f, 312f, PanelRect.width - 48f, 2f, ParchmentSeparatorColor);
                sep.raycastTarget = false;
            }

            // 4 枚回合战况快捷短语（直走现有 587 公开流 + ContentGuard + 冷却，免 25 秒出手窗内手打）
            _quickButtons = new UiTextButton[QuickPhrases.Length];
            const float chipGap = 8f;
            float chipW = (PanelRect.width - 44f - chipGap * (QuickPhrases.Length - 1)) / QuickPhrases.Length;
            for (int i = 0; i < QuickPhrases.Length; i++)
            {
                string phrase = QuickPhrases[i];
                _quickButtons[i] = BattleUiWidgets.CreateTextButton($"Quick_{i}", _panel,
                    22f + i * (chipW + chipGap), 318f, chipW, 34f, phrase, 16f,
                    hasCustomDrawer ? ParchmentChipBg : BattleUiStyle.PanelBgLight,
                    hasCustomDrawer ? QdaoUguiTheme.Brown : QdaoUguiTheme.Cream);
                _quickButtons[i].Button.onClick.AddListener(() => OnQuickPhraseClicked(phrase));
            }

            _statusText = QdaoUguiFactory.CreateText("Status", _panel, 24f, 358f, PanelRect.width - 48f, 28f,
                string.Empty, 16f, QdaoUguiTheme.MutedBrown, TextAlignmentOptions.MidlineLeft);
            _statusText.overflowMode = TextOverflowModes.Ellipsis;

            var inputPlate = BattleUiWidgets.CreatePanel("InputPlate", _panel,
                22f, 392f, PanelRect.width - 178f, 56f,
                hasCustomDrawer ? ParchmentInputBg : BattleUiStyle.PanelBgLight);
            _input = QdaoUguiFactory.CreateInputField("ChatInput", inputPlate.transform,
                0f, 0f, PanelRect.width - 178f, 56f, "发句话和大家聊聊（≤60字）…", 19);
            _input.characterLimit = 60;
            if (hasCustomDrawer)
            {
                if (_input.textComponent != null) _input.textComponent.color = QdaoUguiTheme.DarkBrown;
                if (_input.placeholder is TMP_Text ph) ph.color = QdaoUguiTheme.MutedBrown;
            }
            _input.onSubmit.AddListener(_ => OnSendClicked());

            _sendButton = BattleUiWidgets.CreateTextButton("Send", _panel,
                PanelRect.width - 146f, 392f, 124f, 56f,
                "发送", 21f, _hasCustomSendSprite ? Color.white : BattleUiStyle.ButtonPlateAccent, QdaoUguiTheme.Cream);
            if (_hasCustomSendSprite)
            {
                _sendButton.Plate.sprite = sendSprite;
                _sendButton.Plate.type = Image.Type.Sliced;
            }
            else
            {
                BattleRoundCounter.ApplyNineSlice(_sendButton.Plate, "button_9slice");
            }
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
                _localMuted = false;
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

            _boundClient?.SyncBattleContext(_battleId, _chatEnabled, pollingActive: _chatEnabled && !_localMuted);

            if (!_chatEnabled)
            {
                SetDrawerVisible(false);
                // 关闭交流时：参战端彻底隐藏零打扰；观战端展示温和只读提示牌（仍可正常观战）
                _toggleButton.SetVisible(_isSpectating);
                if (_isSpectating)
                {
                    ApplyToggleVisual(open: false);
                    _toggleButton.SetText("静默观战·禁交流");
                }
                return;
            }

            ApplyToggleVisual(open: true);
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
            _localMuted = false;
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

        private void ApplyToggleVisual(bool open)
        {
            if (_toggleButton?.Plate == null) return;
            Sprite sprite = open ? _entryOpenSprite : _entrySilentSprite;
            if (sprite != null)
            {
                _toggleButton.Plate.sprite = sprite;
                _toggleButton.Plate.type = Image.Type.Sliced;
                _toggleButton.Plate.color = Color.white;
                if (_toggleButton.Label != null)
                {
                    _toggleButton.Label.color = open ? QdaoUguiTheme.Cream : QdaoUguiTheme.Brown;
                }
            }
            else
            {
                BattleRoundCounter.ApplyNineSlice(_toggleButton.Plate, "button_9slice");
                _toggleButton.Plate.color = open ? BattleUiStyle.ButtonPlateAccent : BattleUiStyle.ButtonPlate;
                if (_toggleButton.Label != null) _toggleButton.Label.color = BattleUiStyle.ButtonText;
            }
        }

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

        private void OnQuickPhraseClicked(string phrase)
        {
            EnsureBound();
            if (_boundClient == null || _battleId == 0UL || !_chatEnabled || string.IsNullOrEmpty(phrase)) return;
            _boundClient.Send(_battleId, phrase);
        }

        private void OnMuteClicked()
        {
            if (_isReplay || _battleId == 0UL || !_chatEnabled) return;
            _localMuted = !_localMuted;
            EnsureBound();
            _boundClient?.SetPollingActive(!_localMuted);
            if (_localMuted)
            {
                if (_boundClient != null) _lastReadSeq = _boundClient.LatestSeq;
                SetDrawerVisible(false);
                _showToast?.Invoke("已在本局本地静音观战交流（不影响其他玩家，可随时重新打开）");
            }
            else
            {
                _boundClient?.Pull(_battleId);
            }
            RefreshUi();
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
                if (_isSpectating)
                {
                    ApplyToggleVisual(open: false);
                    _toggleButton.SetText("静默观战·禁交流");
                }
                return;
            }

            if (!_chatEnabled) return;
            ApplyToggleVisual(open: true);

            if (IsDrawerVisible && _boundClient != null)
            {
                _lastReadSeq = _boundClient.LatestSeq;
            }

            ulong unread = 0UL;
            if (_boundClient != null && !IsDrawerVisible && !_localMuted && _boundClient.LatestSeq > _lastReadSeq)
            {
                unread = _boundClient.LatestSeq - _lastReadSeq;
            }
            _toggleButton.SetText(_localMuted
                ? "观战交流·静"
                : (unread > 0UL ? $"观战交流 ({Math.Min(unread, 99UL)})" : "观战交流"));
            _muteButton?.SetText(_localMuted ? "取消静音" : "静音本局");

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
                _messagesText.text = "本局已开放局外人观战交流（全房公开，队内战术频道完全隔离），点下方短语或输入内容聊聊吧。";
                if (_scrollContent != null) _scrollContent.sizeDelta = new Vector2(_scrollContent.sizeDelta.x, ViewportHeight);
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
            float preferredH = Math.Max(ViewportHeight - 12f, _messagesText.GetPreferredValues(_messagesText.text, PanelRect.width - 60f, float.PositiveInfinity).y + 18f);
            _messagesText.rectTransform.sizeDelta = new Vector2(PanelRect.width - 60f, preferredH);
            if (_scrollContent != null)
            {
                _scrollContent.sizeDelta = new Vector2(_scrollContent.sizeDelta.x, Math.Max(ViewportHeight, preferredH + 16f));
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
            if (_quickButtons != null)
            {
                for (int i = 0; i < _quickButtons.Length; i++)
                {
                    _quickButtons[i]?.SetInteractable(canSend);
                }
            }
            if (_hasCustomSendSprite && _sendButton.Plate != null)
            {
                _sendButton.Plate.color = canSend ? Color.white : SendCooldownTint;
            }

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
                else if (_localMuted)
                    _statusText.text = "本局已本地静音 · 点右上角「取消静音」恢复自动刷新";
                else
                    _statusText.text = "全房公开交流 · 双方与观众同步可见";
            }
        }
    }
}
