using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>
    /// City team entry and session lifetime. TeamClient owns every team request; State only
    /// mirrors it for the window and the entry badge.
    /// </summary>
    public sealed class TeamUiRoot : MonoBehaviour
    {
        public static TeamUiRoot Instance { get; private set; }
        public TeamWindow Window => _window;
        public TeamClient Client => _client;
        public TeamInvitationWindow InvitationWindow => _invitations;
        public TeamInvitationDirectory InvitationDirectory => _directory;
        public TeamUiState State { get; } = new(() => Time.realtimeSinceStartup);
        public const float EntryX = 68;
        public const float EntryY = 496;

        private GameClient _game;
        private TeamClient _client;
        private RectTransform _hud;
        private Button _entry;
        private TMP_Text _entryLabel;
        private TeamWindow _window;
        private TeamInvitationWindow _invitations;
        private TeamInvitationDirectory _directory;
        private ulong _playerId;
        private bool _available;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[TeamUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TeamUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("TeamCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 186;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            // The existing right column already fills the screen; use the free space below tracked quests.
            _entry = GameplayUiArt.Button(_hud, "组队 [T]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            _entry.name = "TeamEntry";
            _entryLabel = _entry.GetComponentInChildren<TMP_Text>();
            _window = new TeamWindow(design);
            _invitations = new TeamInvitationWindow(design);
            _window.RefreshRequested += () => { if (_available) _client?.Refresh(); };
            _window.DecisionRequested += (id, ok) => { if (_available) _client?.HandleApplication(id, ok); };
            _window.CreateRequested += () => { if (_available) _client?.Create(); };
            _window.ApplyRequested += id => { if (_available) _client?.ApplyJoin(id); };
            _window.InviteRequested += id => { if (_available) _client?.Invite(id); };
            _window.InviteBrowseRequested += () => OpenInvitations(TeamInvitationSource.Friends);
            _window.Closed += () => _invitations.Hide();
            _invitations.RefreshRequested += (source, query) => { if (_available) _directory?.Refresh(source, query); };
            _invitations.LoadMoreRequested += () => { if (_available) _directory?.LoadMore(); };
            _invitations.InviteRequested += id => { if (_available) _client?.Invite(id); };
            _invitations.CreateRequested += () => { if (_available) _client?.Create(); };
            _invitations.Closed += () => _window.SetCovered(false);
            _window.InviteResponseRequested += (teamId, ok) => { if (_available) _client?.RespondInvite(teamId, ok); };
            _window.LeaveRequested += () => { if (_available) _client?.Leave(); };
            _window.KickRequested += id => { if (_available) _client?.Kick(id); };
            _window.TransferRequested += id => { if (_available) _client?.TransferLeader(id); };
            _window.DisbandRequested += () => { if (_available) _client?.Disband(); };
            // v1 has one team dungeon; it matches the server's only PveTeamSizeByConfigId entry.
            _window.StartMatchRequested += () => { if (_available) _client?.StartMatch(BattleUiStyle.PveTeamBattleConfigId); };
            State.Changed += RenderState;
            State.Reset();
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                DisposeClient();
                _game = game;
                if (game != null)
                {
                    _client = new TeamClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                        handler => game.OnTeamSnapshot += handler, handler => game.OnTeamSnapshot -= handler,
                        () => Time.realtimeSinceStartup);
                    _client.Changed += SyncState;
                    _directory = new TeamInvitationDirectory(game, () => Social.SocialUiRoot.Instance?.State);
                    _directory.Changed += SyncDirectory;
                }
                _playerId = 0;
                _window.ResetSession();
                _invitations.ResetSession();
                SyncState();
            }
            _client?.ObserveConnection();
            _directory?.ObserveConnection();
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            ulong playerId = inGame ? _game.PlayerId : 0;
            if (_playerId != playerId)
            {
                _playerId = playerId;
                _window.ResetSession();
                _invitations.ResetSession();
                _directory?.Reset();
                if (_client != null) _client.Reset();
                else State.Reset(playerId);
            }
            _available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            // The live path mirrors the client and holds no token, so this is a no-op kept for consistency.
            State.Tick(Time.realtimeSinceStartup);
            if (!_available) { HidePanel(); return; }
            // Probe, invites, conflict re-pulls and the 30s panel refresh all go out here, one per frame.
            _client?.DrainQueued(_window.IsVisible || _invitations.IsVisible);
            // A completed background read can still occupy the shared send interval. Re-enable
            // controls when that interval (or their own per-message quota) expires.
            if (_client != null && (State.InvitationCoolingDown != _client.InvitationCoolingDown ||
                State.CreationCoolingDown != _client.CreationCoolingDown)) SyncState();
            bool typing = IsTyping();
            if (!_window.IsVisible && !_invitations.IsVisible && GameplayInputGate.IsKeyboardBlocked) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) { if (_invitations.IsVisible) _invitations.Hide(); else _window.Back(); }
            else if (!typing && keys.tKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_invitations.IsVisible) _invitations.Hide(); else _window.Back(); }
            else if (!typing && Input.GetKeyDown(KeyCode.T)) Toggle();
#endif
        }

        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available) return;
            Guild.GuildUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            Mail.MailUiRoot.Instance?.HidePanel();
            Social.SocialUiRoot.Instance?.HidePanel();
            RenderState();
            _window.Show();
            // Opening the panel asks for GetMyTeam; the client queues it when busy or too soon.
            _client?.Refresh();
        }

        public void HidePanel()
        {
            _invitations?.Hide();
            _window?.Hide();
        }

        public void OpenInviteForPlayer(ulong playerId)
        {
            if (!_available || playerId == 0 || playerId == _playerId) return;
            _directory?.RememberChatPlayer(playerId);
            OpenInvitations(TeamInvitationSource.Chat, playerId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void OpenInvitations(TeamInvitationSource source, string query = "")
        {
            if (!_available) return;
            if (!_window.IsVisible) Toggle();
            Social.SocialUiRoot.Instance?.HidePanel();
            _invitations.SetState(State);
            _window.SetCovered(true);
            _invitations.Show(source, query);
        }

        private void SyncDirectory()
        {
            if (_directory == null) return;
            _invitations?.SetCandidates(_directory.Source, _directory.Candidates, _directory.IsLoading,
                _directory.Status, _directory.HasMore);
        }

        private void SyncState()
        {
            if (_client == null) { State.Reset(_playerId); return; }
            // A probe answered without a view (server read failure) leaves a placeholder "no team"
            // snapshot; the window must show "not synced" rather than offer Create / Apply to a member.
            State.Sync(_client.Snapshot, _client.Invites, _client.HasLoaded && _client.HasView, _client.ServiceAvailable,
                _client.PendingAction, _client.PendingTarget, _client.HighlightPlayerId, _client.Status,
                _client.InvitationCoolingDown, _client.CreationCoolingDown);
        }

        private void RenderState()
        {
            _window?.SetState(State);
            _invitations?.SetState(State);
            if (_entryLabel == null) return;
            int applications = State.Snapshot.Applications.Count;
            int invites = State.Invites.Count;
            _entryLabel.text = State.IsLeader && applications > 0 ? $"组队 · {applications}条申请"
                : State.HasLoaded && !State.HasTeam && invites > 0 ? $"组队 · {invites}条邀请"
                : "组队 [T]";
        }

        private void DisposeClient()
        {
            if (_directory != null)
            {
                _directory.Changed -= SyncDirectory;
                _directory.Dispose();
                _directory = null;
            }
            if (_client == null) return;
            _client.Changed -= SyncState;
            _client.Dispose();
            _client = null;
        }

        private static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponentInParent<TMP_InputField>()?.isFocused == true ||
                                        selected.GetComponentInParent<InputField>()?.isFocused == true);
        }

        private void OnDisable() => HidePanel();

        private void OnDestroy()
        {
            DisposeClient();
            State.Changed -= RenderState;
            HidePanel();
            if (Instance == this) Instance = null;
        }
    }
}
