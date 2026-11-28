using System;
using System.Collections.Generic;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>
    /// 防盗号「保持登录(系统安全存储 RefreshToken)」与「最近账号历史记录(仅存脱敏元数据)」运行期 UI 扩展:
    ///   1. 在 _credentialPanel 内挂接「保持登录（公用电脑请勿勾选）」开关与「退出登录」按钮;
    ///   2. 在 _credentialPanel 右侧挂接「最近登录账号」列表(最多 5 条、支持单条 × 删除、不存密码);
    ///   3. 在 _settingsPanel 的「注销账号」旁挂接「退出登录」入口;
    ///   4. 不改动烘焙 QdaoServerSelect.prefab 与 _landingRoot 原图布局,确保原图验收与 Prefab 契约测试零破坏。
    /// </summary>
    public sealed partial class QdaoServerSelectView
    {
        private Toggle _keepSignedInToggle;
        private Button _credentialSignOutButton;
        private Button _settingsSignOutButton;
        private RectTransform _accountHistoryPanel;
        private Button[] _historySelectButtons;
        private TMP_Text[] _historySelectTexts;
        private Button[] _historyDeleteButtons;
        private readonly List<AccountHistoryEntry> _renderedHistory = new();
        private bool _accountSessionEventsBound;

        private string _pendingAuthType;
        private string _pendingAuthRawLabel;
        private RememberedSessionInfo _pendingRememberedInfo;
        private Action<string> _refreshTokenUpdatedHandler;
        private Action _sessionInvalidatedHandler;

        private void EnsureAccountSessionUi()
        {
            if (_credentialPanel != null && _keepSignedInToggle == null)
            {
                var toggleRoot = QdaoUguiFactory.CreateRect("KeepSignedInToggle", _credentialPanel, 990f, 604f, 700f, 46f);
                var box = QdaoCoreFidelityArt.Panel("KeepSignedInBox", toggleRoot, 0f, 1f, 44f, 44f, "clean_tab");
                box.raycastTarget = true;
                var check = QdaoUguiFactory.CreateImage("KeepSignedInCheck", toggleRoot, 10f, 11f, 24f, 24f,
                    QdaoCoreFidelityArt.Load("check"));
                check.raycastTarget = false;
                var label = Label("KeepSignedInLabel", toggleRoot, 58f, 0f, 640f, 46f,
                    "保持登录（公用电脑请勿勾选）", 24f);
                label.raycastTarget = false;
                _keepSignedInToggle = toggleRoot.gameObject.AddComponent<Toggle>();
                _keepSignedInToggle.targetGraphic = box;
                _keepSignedInToggle.graphic = check;
                _keepSignedInToggle.SetIsOnWithoutNotify(ClientAuthStorage.RememberedSession.KeepSignedIn);
            }

            if (_credentialPanel != null && _credentialSignOutButton == null)
            {
                _credentialSignOutButton = ArtButton("CredentialSignOutButton", _credentialPanel,
                    1625f, 294f, 195f, 56f, "退出登录", "clean_tab", out var text);
                text.fontSize = 26f;
                _credentialSignOutButton.gameObject.SetActive(false);
            }

            if (_credentialPanel != null && _accountHistoryPanel == null)
            {
                BuildAccountHistoryPanel();
            }

            if (_settingsPanel != null && _settingsSignOutButton == null)
            {
                _settingsSignOutButton = ArtButton("SettingsSignOutButton", _settingsPanel,
                    1375f, 650f, 260f, 70f, "退出登录", "clean_tab", out var text);
                text.fontSize = 30f;
                _settingsSignOutButton.gameObject.SetActive(false);
            }
        }

        private void BuildAccountHistoryPanel()
        {
            _accountHistoryPanel = QdaoUguiFactory.CreateRect("AccountHistoryPanel", _credentialPanel,
                1895f, 235f, 560f, 650f);
            var frame = QdaoCoreFidelityArt.Panel("AccountHistoryFrame", _accountHistoryPanel,
                0f, 0f, 560f, 650f, "main_frame");
            frame.raycastTarget = true;

            var title = Label("AccountHistoryTitle", _accountHistoryPanel,
                30f, 42f, 500f, 58f, "最近登录账号", 34f, false, TextAlignmentOptions.Center);
            QdaoCoreFidelityArt.ApplyText(title);

            _historySelectButtons = new Button[AccountHistoryStore.MaxEntries];
            _historySelectTexts = new TMP_Text[AccountHistoryStore.MaxEntries];
            _historyDeleteButtons = new Button[AccountHistoryStore.MaxEntries];
            for (int i = 0; i < AccountHistoryStore.MaxEntries; i++)
            {
                float y = 117f + i * 96f;
                _historySelectButtons[i] = ArtButton("AccountHistoryRow_" + i, _accountHistoryPanel,
                    30f, y, 420f, 80f, string.Empty, "clean_tab", out var rowText);
                rowText.fontSize = 23f;
                rowText.alignment = TextAlignmentOptions.MidlineLeft;
                rowText.overflowMode = TextOverflowModes.Ellipsis;
                rowText.rectTransform.anchoredPosition = new Vector2(22f, 0f);
                rowText.rectTransform.sizeDelta = new Vector2(376f, 80f);
                _historySelectTexts[i] = rowText;

                _historyDeleteButtons[i] = ArtButton("AccountHistoryDelete_" + i, _accountHistoryPanel,
                    460f, y, 70f, 80f, "×", "clean_tab", out var delText);
                delText.fontSize = 30f;
                delText.rectTransform.anchoredPosition = Vector2.zero;
                delText.rectTransform.sizeDelta = new Vector2(70f, 80f);

                _historySelectButtons[i].gameObject.SetActive(false);
                _historyDeleteButtons[i].gameObject.SetActive(false);
            }

            var hint = Label("AccountHistoryHint", _accountHistoryPanel,
                30f, 594f, 500f, 34f, "仅存脱敏展示记录，绝不保存密码", 20f, false, TextAlignmentOptions.Center);
            hint.raycastTarget = false;
            _accountHistoryPanel.gameObject.SetActive(false);
        }

        private void BindAccountSessionEvents()
        {
            if (_accountSessionEventsBound) UnbindAccountSessionEvents();
            if (_keepSignedInToggle != null)
                _keepSignedInToggle.onValueChanged.AddListener(OnKeepSignedInChanged);
            if (_credentialSignOutButton != null)
                _credentialSignOutButton.onClick.AddListener(OnSignOutClicked);
            if (_settingsSignOutButton != null)
                _settingsSignOutButton.onClick.AddListener(OnSignOutClicked);
            if (_historySelectButtons != null)
            {
                for (int i = 0; i < _historySelectButtons.Length; i++)
                {
                    int captured = i;
                    _historySelectButtons[i].onClick.AddListener(() => OnHistoryEntryClicked(captured));
                    _historyDeleteButtons[i].onClick.AddListener(() => OnHistoryEntryDeleteClicked(captured));
                }
            }
            _accountSessionEventsBound = true;
        }

        private void UnbindAccountSessionEvents()
        {
            if (!_accountSessionEventsBound) return;
            if (_keepSignedInToggle != null)
                _keepSignedInToggle.onValueChanged.RemoveListener(OnKeepSignedInChanged);
            if (_credentialSignOutButton != null)
                _credentialSignOutButton.onClick.RemoveListener(OnSignOutClicked);
            if (_settingsSignOutButton != null)
                _settingsSignOutButton.onClick.RemoveListener(OnSignOutClicked);
            if (_historySelectButtons != null)
            {
                for (int i = 0; i < _historySelectButtons.Length; i++)
                {
                    _historySelectButtons[i]?.onClick.RemoveAllListeners();
                    _historyDeleteButtons[i]?.onClick.RemoveAllListeners();
                }
            }
            _accountSessionEventsBound = false;
        }

        private void BindAccountSessionClientEvents(GameClient client)
        {
            if (client == null) return;
            _refreshTokenUpdatedHandler = token =>
            {
                // 服务端每次签发/轮换 RefreshToken 立刻覆盖系统安全存储,避免旧令牌残留触发重用吊销。
                ClientAuthStorage.RememberedSession.UpdateRotatedRefreshToken(token, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            };
            _sessionInvalidatedHandler = () =>
            {
                ClientAuthStorage.RememberedSession.Clear();
                if (_app?.Session != null)
                {
                    _app.Session.AccessToken = null;
                    _app.Session.RefreshToken = null;
                }
                if (this != null) RefreshVisualState();
            };
            client.OnRefreshTokenUpdated += _refreshTokenUpdatedHandler;
            client.OnSessionInvalidated += _sessionInvalidatedHandler;
        }

        private void UnbindAccountSessionClientEvents(GameClient client)
        {
            if (client == null) return;
            if (_refreshTokenUpdatedHandler != null) client.OnRefreshTokenUpdated -= _refreshTokenUpdatedHandler;
            if (_sessionInvalidatedHandler != null) client.OnSessionInvalidated -= _sessionInvalidatedHandler;
            _refreshTokenUpdatedHandler = null;
            _sessionInvalidatedHandler = null;
        }

        private void RefreshAccountSessionUiState(bool canInteract)
        {
            bool normalLoginPanel = !_credentialForRealName && !_credentialForDeletion && MiniGameLogin.AuthType == null;
            bool secureAvailable = ClientAuthStorage.RememberedSession.IsSecureStorageAvailable;
            bool hasSaved = ClientAuthStorage.RememberedSession.TryGetSavedMetadata(out var saved);

            if (_keepSignedInToggle != null)
            {
                bool showToggle = normalLoginPanel && secureAvailable;
                _keepSignedInToggle.gameObject.SetActive(showToggle);
                _keepSignedInToggle.interactable = canInteract && showToggle;
                _keepSignedInToggle.SetIsOnWithoutNotify(ClientAuthStorage.RememberedSession.KeepSignedIn);
            }

            if (_credentialSignOutButton != null)
            {
                bool showSignOut = normalLoginPanel && hasSaved;
                _credentialSignOutButton.gameObject.SetActive(showSignOut);
                _credentialSignOutButton.interactable = canInteract && showSignOut;
            }

            if (_settingsSignOutButton != null)
            {
                bool hasAnyToken = hasSaved || !string.IsNullOrEmpty(_app?.GameClient?.RefreshToken);
                bool showSettingsSignOut = MiniGameLogin.AuthType == null && hasAnyToken;
                _settingsSignOutButton.gameObject.SetActive(showSettingsSignOut);
                _settingsSignOutButton.interactable = !_busy && showSettingsSignOut;
            }

            RefreshAccountHistoryPanel(canInteract, normalLoginPanel, hasSaved ? saved : default);
        }

        private void RefreshAccountHistoryPanel(bool canInteract, bool normalLoginPanel, RememberedSessionInfo saved)
        {
            if (_accountHistoryPanel == null) return;
            _renderedHistory.Clear();
            if (normalLoginPanel)
                _renderedHistory.AddRange(ClientAuthStorage.History.GetEntries());

            bool showPanel = normalLoginPanel && _renderedHistory.Count > 0;
            _accountHistoryPanel.gameObject.SetActive(showPanel);
            if (!showPanel) return;

            for (int i = 0; i < _historySelectButtons.Length; i++)
            {
                bool present = i < _renderedHistory.Count;
                _historySelectButtons[i].gameObject.SetActive(present);
                _historyDeleteButtons[i].gameObject.SetActive(present);
                _historySelectButtons[i].interactable = canInteract && present;
                _historyDeleteButtons[i].interactable = canInteract && present;
                if (!present) continue;

                var entry = _renderedHistory[i];
                bool isSaved = saved.IsValid && string.Equals(saved.EntryKey, entry.EntryKey, StringComparison.Ordinal);
                string prefix = AccountHistoryStore.AuthTypeDisplayName(entry.AuthType);
                string suffix = isSaved ? "（已保持登录）" : string.Empty;
                _historySelectTexts[i].text = $"{prefix} · {entry.MaskedLabel}{suffix}";
            }
        }

        private string FormatSavedSessionIdleStatus()
        {
            if (MiniGameLogin.AuthType != null) return string.Empty;
            if (!ClientAuthStorage.RememberedSession.TryGetSavedMetadata(out var saved)) return string.Empty;
            string kind = AccountHistoryStore.AuthTypeDisplayName(saved.AuthType);
            return $"已保持登录：{kind} · {saved.MaskedLabel}（点击进入游戏直接登录）";
        }

        private void OnKeepSignedInChanged(bool enabled)
        {
            if (_busy) return;
            ClientAuthStorage.RememberedSession.KeepSignedIn = enabled;
            RefreshVisualState();
            if (!enabled)
                SetStatus("已关闭保持登录，本机免登凭据已清除");
        }

        private void OnSignOutClicked()
        {
            if (_busy) return;
            SignOutRememberedSession();
            if (_settingsPanel != null && _settingsPanel.gameObject.activeSelf)
                CloseLoginUtilities();
            RefreshVisualState();
            SetStatus("已退出登录，本机免登凭据已清除");
        }

        private void SignOutRememberedSession()
        {
            string prevMethod = ClientSettings.LastLoginMethod;
            ClientAuthStorage.RememberedSession.Clear();
            var session = _app?.Session;
            if (session != null)
            {
                session.Password = string.Empty;
                session.AccessToken = null;
                session.RefreshToken = null;
            }
            if (_passwordInput != null)
                _passwordInput.SetTextWithoutNotify(string.Empty);
            _app?.GameClient?.SetTokens(null, null, 0);
            if (prevMethod == LoginAuthTypes.Google)
                PlatformSignIn.ClearGoogleCredentialState();
        }

        private void OnHistoryEntryClicked(int index)
        {
            if (_busy || index < 0 || index >= _renderedHistory.Count) return;
            var entry = _renderedHistory[index];
            if (ClientAuthStorage.RememberedSession.TryGetSavedSession(out var saved, out string refreshToken) &&
                string.Equals(saved.EntryKey, entry.EntryKey, StringComparison.Ordinal))
            {
                var zone = ResolveEnterableZone();
                if (zone == null) return;
                ShowCredentialPanel(false);
                RunEnterZoneWithRememberedSession(zone, saved, refreshToken);
                return;
            }

            if (entry.AuthType == LoginAuthTypes.Passport ||
                entry.AuthType == LoginAuthTypes.Apple ||
                entry.AuthType == LoginAuthTypes.Google)
            {
                BeginChannelSignIn(entry.AuthType);
                return;
            }

            // 口令账号只回填账号名,绝不回填或持久化密码。
            if (_accountInput != null) _accountInput.SetTextWithoutNotify(entry.PrefillAccount);
            if (_passwordInput != null)
            {
                _passwordInput.SetTextWithoutNotify(string.Empty);
                _passwordInput.Select();
                _passwordInput.ActivateInputField();
            }
            SetStatus("已填入账号，请输入密码登录");
        }

        private void OnHistoryEntryDeleteClicked(int index)
        {
            if (_busy || index < 0 || index >= _renderedHistory.Count) return;
            var entry = _renderedHistory[index];
            ClientAuthStorage.History.Remove(entry.EntryKey);
            if (ClientAuthStorage.RememberedSession.TryGetSavedMetadata(out var saved) &&
                string.Equals(saved.EntryKey, entry.EntryKey, StringComparison.Ordinal))
            {
                ClientAuthStorage.RememberedSession.Clear();
                _app?.GameClient?.SetTokens(null, null, 0);
            }
            RefreshVisualState();
            SetStatus("已删除该账号记录");
        }

        private void RecordSuccessfulLoginSession(string authType, string rawLabel, uint zoneId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_pendingRememberedInfo.IsValid)
            {
                var saved = _pendingRememberedInfo;
                ClientAuthStorage.History.TouchZone(saved.EntryKey, zoneId, now);
                string tokenAfterReauth = _app?.GameClient?.RefreshToken;
                if (MiniGameLogin.AuthType == null && !string.IsNullOrEmpty(tokenAfterReauth))
                {
                    ClientAuthStorage.RememberedSession.Save(
                        tokenAfterReauth, saved.EntryKey, saved.AuthType, saved.MaskedLabel, zoneId, now);
                }
                return;
            }
            var entry = ClientAuthStorage.History.Record(authType, rawLabel, zoneId, now);
            string refreshToken = _app?.GameClient?.RefreshToken;
            if (MiniGameLogin.AuthType == null && !string.IsNullOrEmpty(refreshToken))
            {
                ClientAuthStorage.RememberedSession.Save(
                    refreshToken, entry.EntryKey, entry.AuthType, entry.MaskedLabel, zoneId, now);
            }
            else if (!ClientAuthStorage.RememberedSession.KeepSignedIn)
            {
                ClientAuthStorage.RememberedSession.Clear();
            }
        }

        private void RunEnterZoneWithRememberedSession(ServerListZone zone, RememberedSessionInfo saved, string refreshToken)
        {
            var session = _app.Session;
            ClientSettings.LastLoginMethod = saved.AuthType;
            _pendingAuthType = saved.AuthType;
            _pendingAuthRawLabel = string.Empty;
            _pendingRememberedInfo = saved;
            _busy = true;
            int runId = ++_enterRunId;
            RefreshVisualState();
            _app.Run(_app.GameClient.EnterZoneWithRefreshToken(
                zone.zone_id, refreshToken, session.DeviceId,
                onSuccess: () =>
                {
                    if (runId != _enterRunId) return;
                    _busy = false;
                    session.SelectedZoneId = zone.zone_id;
                    session.SelectedZoneIndex = session.Zones.FindIndex(c => c.zone_id == zone.zone_id);
                    session.SetTokensFromClient(_app.GameClient);
                    session.RecordZoneEntered(zone.zone_id);
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    ClientAuthStorage.History.TouchZone(saved.EntryKey, zone.zone_id, now);
                    string latestRefresh = _app.GameClient.RefreshToken;
                    if (!string.IsNullOrEmpty(latestRefresh))
                    {
                        ClientAuthStorage.RememberedSession.Save(
                            latestRefresh, saved.EntryKey, saved.AuthType, saved.MaskedLabel, zone.zone_id, now);
                    }
                    _pendingRememberedInfo = default;
                    _pendingAuthType = null;
                    _pendingAuthRawLabel = null;
                    SetStatus(string.Empty);
                    gameObject.SetActive(false);
                },
                onError: err =>
                {
                    if (runId != _enterRunId) return;
                    _busy = false;
                    RefreshVisualState();
                    SetStatus(err);
                    if (_app.GameClient.AntiAddiction.real_name_required)
                        BeginRealName(zone, LoginCredential.ForAccessToken(_app.GameClient.AccessToken));
                },
                onSessionExpired: () =>
                {
                    if (runId != _enterRunId) return;
                    _pendingRememberedInfo = default;
                    ClientAuthStorage.RememberedSession.Clear();
                    ShowCredentialPanel(true);
                },
                zoneCreateRoleClosed: zone.create_role_closed,
                onAccountDeletionPending: (state, executeAfter) =>
                {
                    if (runId != _enterRunId) return;
                    _busy = false;
                    _pendingRememberedInfo = default;
                    SetStatus(string.Empty);
                    ShowDeletionPending(zone.zone_id, state, executeAfter);
                },
                previousAccountDeletedNotice: deletedAt => ShowPreviousDeletionNotice(runId, deletedAt)));
        }
    }
}
