#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Friendpb;
using MmorpgClient.Game;
using MmorpgClient.Game.Social;
using MmorpgClient.Game.Team;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.App
{
    /// <summary>
    /// Opt-in, two-process development acceptance. Uses real GameClient connections and actual
    /// visible Unity buttons; shared files synchronize the test only, never supply team state.
    /// Login credentials remain in DevAutoPilot's per-process environment variable.
    /// </summary>
    public sealed class DevTeamInvitationDriver : MonoBehaviour
    {
        [Serializable] private sealed class Signal
        {
            public string run, mode, phase, playerId, peerId, teamId, sceneId, error, utc;
            public uint sceneConfigId;
            public int round;
        }
        [Serializable] private sealed class Evidence
        {
            public string name, source, utc, playerId, peerId, teamId, leaderId, version, membershipEpoch, screenshot;
            public string[] members, candidates, clickedButtons;
            public bool inGame, gateReady, directoryLoading, socialVisible, candidateOnline, onlineStatusKnown;
        }
        [Serializable] private sealed class Report
        {
            public string result, mode, run, error, utc, scope;
            public string[] checks;
            public Evidence[] evidence;
        }

        private string _mode, _run, _directory, _phase = "boot", _error;
        private int _round = -1;
        private ulong _peerId;
        private bool _done;
        private readonly List<string> _checks = new();
        private readonly List<string> _clicks = new();
        private readonly List<Evidence> _evidence = new();
        private GameClient Game => AppBootstrap.Instance?.GameClient;
        private TeamUiRoot Team => TeamUiRoot.Instance;
        private SocialUiRoot Social => SocialUiRoot.Instance;
        private string SignalPath(string mode) => Path.Combine(_directory, _run + "-" + mode + "-signal.json");
        private string OtherMode => _mode == "leader" ? "member" : "leader";
        private string ChatMarker => "本机组队验收 " + _run;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartOptIn()
        {
            string mode = Argument("teamVerifyMode");
            if (mode != "leader" && mode != "member") return;
            if (FindAnyObjectByType<DevTeamInvitationDriver>() != null) return;
            var go = new GameObject("[DevTeamInvitationDriver]");
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<DevTeamInvitationDriver>();
            driver._mode = mode;
            driver._run = Argument("teamVerifyRun");
            driver._directory = Argument("teamVerifyDir");
        }

        private static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-" + key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            if (string.IsNullOrEmpty(_run) || _run.Length > 40 || _run.Any(c => !char.IsLetterOrDigit(c) && c != '-') ||
                string.IsNullOrEmpty(_directory) || !Path.IsPathRooted(_directory))
            { Fail("Require a unique alphanumeric -teamVerifyRun and absolute -teamVerifyDir."); yield break; }
            Directory.CreateDirectory(_directory);
            if (File.Exists(SignalPath(_mode))) { Fail("Refusing to reuse an existing run/mode evidence file."); yield break; }
            Debug.Log("[TeamVerify][" + _mode + "] BEGIN run=" + _run + " input=programmatic_unity_pointer_events");
            yield return Wait(() => Game?.InGame == true && Game.IsGateReady && Team?.Client?.HasView == true &&
                Team.Client.ServiceAvailable && Social?.Client != null && Social.State.PlayerId == Game.PlayerId,
                "real login and team probe", 120);
            if (_done) yield break;
            if (DevAutoPilot.Current.Account != "robot_tui_" + _run + "_" + _mode)
            { Fail("This driver only accepts its unique robot_tui run account."); yield break; }
            if (Team.Client.HasTeam) { Fail("Fresh acceptance account unexpectedly already belongs to a team."); yield break; }
            Publish("ready");
            yield return Wait(() => TryPeer(out var signal) && ulong.TryParse(signal.playerId, out _peerId) &&
                _peerId != 0 && _peerId != Game.PlayerId, "second authenticated player", 120);
            if (_done) yield break;
            Check("Distinct real authenticated player IDs: " + Game.PlayerId + " / " + _peerId);
            if (_mode == "leader")
            {
                TryPeer(out var leader);
                if (!ulong.TryParse(leader.sceneId, out var destination) || destination == 0)
                { Fail("Peer has no authoritative scene ID."); yield break; }
                if (Game.CurrentSceneId != destination)
                {
                    bool accepted = false;
                    yield return Game.EnterScene(leader.sceneConfigId, destination, () => accepted = true, Fail);
                    if (_done) yield break;
                    yield return Wait(() => accepted && Game.CurrentSceneId == destination && Game.World.HasLocalPlayer,
                        "production EnterScene reaches peer line", 90);
                    if (_done) yield break;
                }
                Publish("aligned");
            }
            else
            {
                yield return Wait(() => PeerAt("aligned", -1), "leader joins peer line", 100);
                if (_done) yield break;
            }
            if (_mode == "leader") yield return Wait(() => Game.World.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.PlayerId == _peerId),
                "peer becomes visible through real AOI", 90);
            if (_done) yield break;
            yield return _mode == "leader" ? Leader() : Member();
        }

        private IEnumerator Leader()
        {
            bool friendAdded = false;
            yield return Game.Call(ClientPlayerFriendAddFriendHandler.MessageId,
                new AddFriendRequest { TargetPlayerId = _peerId }, AddFriendResponse.Parser,
                response => { if (TipOk(response.ErrorMessage, "add friend")) friendAdded = true; }, Fail);
            if (_done || !friendAdded) { if (!_done) Fail("Friend request did not acknowledge success."); yield break; }
            Publish("friend-requested");
            yield return Wait(() => PeerAt("chat-ready", -1), "friend accepted and real world chat sent", 60);
            if (_done) yield break;

            Social.Toggle();
            yield return Click("SocialTab_World");
            yield return Wait(() => !Social.Client.Busy && Social.State.Messages(SocialChannel.World)
                .Any(m => m.Sender == _peerId && m.Text == ChatMarker), "server world history with peer sender", 35);
            if (_done) yield break;
            Check("World history contains peer's server-acknowledged marker.");
            Team.Toggle();
            yield return TeamIdle();
            yield return Click("CreateTeam");
            yield return Wait(() => Team.Client.HasTeam && Team.Client.IsLeader && Team.Client.Snapshot.Members.Count == 1,
                "authoritative team creation");
            if (_done) yield break;
            ulong teamId = Team.Client.Snapshot.TeamId;
            Check("Created actual team " + teamId);

            var sources = new[] { TeamInvitationSource.Friends, TeamInvitationSource.Nearby, TeamInvitationSource.Online, TeamInvitationSource.Chat };
            for (int i = 0; i < sources.Length && !_done; ++i)
            {
                _round = i;
                if (sources[i] == TeamInvitationSource.Chat)
                {
                    Team.HidePanel(); Social.Toggle();
                    if (Social.State.Page != SocialPage.World) yield return Click("SocialTab_World");
                    yield return Wait(() => !Social.Client.Busy, "chat refresh complete");
                    if (_done) yield break;
                    var scroll = Social.GetComponentsInChildren<ScrollRect>().FirstOrDefault(s => s.name == "SocialChatScroll");
                    if (scroll != null) scroll.verticalNormalizedPosition = 0;
                    yield return null;
                    yield return Click("SocialPortrait_" + _peerId + "Button");
                    yield return Capture("chat-profile");
                    yield return Click("SocialInviteToTeam");
                    yield return Wait(() => Team.InvitationWindow.IsVisible && Team.InvitationWindow.Source == TeamInvitationSource.Chat &&
                        Team.InvitationWindow.Query == _peerId.ToString() && !Social.Window.IsVisible,
                        "chat profile routes the correct target and closes social");
                }
                else
                {
                    if (Team.InvitationWindow.IsVisible) Team.InvitationWindow.Hide();
                    yield return TeamIdle();
                    yield return Click("InviteToTeam");
                    if (sources[i] != TeamInvitationSource.Friends)
                    {
                        yield return DirectoryIdle();
                        yield return Click("TeamInvitationTab_" + sources[i]);
                    }
                    if (sources[i] == TeamInvitationSource.Online || sources[i] == TeamInvitationSource.Nearby)
                    {
                        yield return DirectoryIdle();
                        var input = Team.GetComponentsInChildren<TMP_InputField>().Single(p => p.name == "TeamInvitationSearch");
                        input.text = _peerId.ToString();
                        yield return Click("SearchTeamInvitations");
                    }
                }
                yield return DirectoryIdle();
                if (sources[i] == TeamInvitationSource.Online)
                {
                    int pages = 0;
                    while (!_done && !Team.InvitationDirectory.Candidates.Any(role => role.PlayerId == _peerId) &&
                        Team.InvitationDirectory.HasMore && pages++ < 64)
                    {
                        // The real Next control becomes Load More on the final (including empty) local page.
                        yield return Click("TeamInvitationNext");
                        yield return DirectoryIdle();
                        Debug.Log("[TeamVerify][leader] online_pages_loaded=" + (pages + 1));
                    }
                }
                yield return Wait(() => Team.InvitationDirectory.Candidates.Any(role => role.PlayerId == _peerId),
                    "peer in " + sources[i] + " real candidate source");
                if (_done) yield break;
                if (Team.InvitationDirectory.Candidates.Any(role => role.PlayerId == Game.PlayerId))
                { Fail("Candidate source includes the current player."); yield break; }
                if (sources[i] == TeamInvitationSource.Nearby && !Game.World.Actors.Values.Any(a =>
                    a.Kind == ActorKind.Player && a.PlayerId == _peerId))
                { Fail("Nearby candidate is not backed by the actual AOI player GUID."); yield break; }
                Check("Real source " + sources[i] + " contains peer " + _peerId + " and excludes self.");
                yield return TeamIdle();
                yield return Capture("source-" + sources[i]);
                yield return Click("InviteCandidate_" + _peerId);
                yield return Wait(() => Team.Client.Snapshot.PendingInvites.Any(role => role.PlayerId == _peerId),
                    "authoritative pending invitation " + sources[i]);
                if (_done) yield break;
                var invited = FindButton("InviteCandidate_" + _peerId);
                if (invited == null || invited.IsInteractable()) { Fail("Authoritative pending invite did not disable the candidate action."); yield break; }
                Publish("invite");
                yield return Wait(() => PeerAt("joined", i) && Team.Client.Snapshot.TeamId == teamId &&
                    Team.Client.Snapshot.Members.Count == 2 && Team.Client.Snapshot.Members.Any(r => r.PlayerId == _peerId),
                    "both clients observe accepted membership " + sources[i]);
                if (_done) yield break;
                yield return Capture("joined-" + sources[i]);
                Check(sources[i] + " invitation accepted; leader authoritative snapshot contains both players in team " + teamId);
                Publish("confirmed");
                if (i + 1 < sources.Length)
                {
                    yield return Wait(() => PeerAt("left", i) && Team.Client.Snapshot.TeamId == teamId &&
                        Team.Client.Snapshot.Members.Count == 1, "peer leaves before next source");
                    if (_done) yield break;
                }
            }
            if (_done) yield break;
            yield return Wait(() => PeerAt("all-rounds-complete", 3), "member completed all sources");
            if (_done) yield break;
            Publish("cleanup-leave");
            yield return Wait(() => PeerAt("cleanup-left", 3) && Team.Client.Snapshot.Members.Count == 1,
                "member leaves the acceptance team");
            if (_done) yield break;
            Team.InvitationWindow.Hide();
            yield return TeamIdle();
            yield return Click("DisbandTeam");
            yield return Click("TeamModalConfirm");
            yield return Wait(() => !Team.Client.HasTeam, "acceptance team disbanded");
            if (_done) yield break;
            bool removed = false;
            yield return Game.Call(ClientPlayerFriendRemoveFriendHandler.MessageId,
                new RemoveFriendRequest { TargetPlayerId = _peerId }, RemoveFriendResponse.Parser,
                response => { if (TipOk(response.ErrorMessage, "remove acceptance friend")) removed = true; }, Fail);
            if (_done || !removed) { if (!_done) Fail("Friend cleanup not acknowledged."); yield break; }
            yield return VerifyFriendRemoved();
            if (_done) yield break;
            Check("Acceptance team disbanded and only this run's peer friendship removed.");
            Publish("complete");
            yield return Wait(() => PeerAt("complete", 3), "member final cleanup acknowledgement");
            if (_done) yield break;
            yield return Finish();
        }

        private IEnumerator Member()
        {
            yield return Wait(() => PeerAt("friend-requested", -1), "leader sends real friend request", 60);
            if (_done) yield break;
            bool accepted = false;
            yield return Game.Call(ClientPlayerFriendAcceptFriendHandler.MessageId,
                new AcceptFriendRequest { FromPlayerId = _peerId }, AcceptFriendResponse.Parser,
                response => { if (TipOk(response.ErrorMessage, "accept friend")) accepted = true; }, Fail);
            if (_done || !accepted) { if (!_done) Fail("Friend acceptance not acknowledged."); yield break; }
            Check("Accepted a real friend request from the other acceptance account.");
            if (!Social.Client.Send(SocialChannel.World, ChatMarker)) { Fail("World chat send was not started."); yield break; }
            yield return Wait(() => !Social.Client.Busy && Social.State.Messages(SocialChannel.World)
                .Any(message => message.Sender == Game.PlayerId && message.Text == ChatMarker), "real acknowledged world chat", 35);
            if (_done) yield break;
            Publish("chat-ready");
            for (int i = 0; i < 4 && !_done; ++i)
            {
                _round = i;
                yield return Wait(() => PeerAt("invite", i), "invitation dispatched round " + i, 120);
                if (_done) yield break;
                TryPeer(out var signal);
                if (!ulong.TryParse(signal.teamId, out var teamId) || teamId == 0) { Fail("Leader has no authoritative team ID."); yield break; }
                if (!Team.Window.IsVisible) Team.Toggle();
                yield return Wait(() => Team.Client.Invites.Any(invite => invite.TeamId == teamId && invite.LeaderId == _peerId),
                    "real incoming invitation from the leader");
                yield return TeamIdle();
                if (_done) yield break;
                yield return Capture("received-" + i);
                yield return Click("AcceptInvite_" + teamId);
                yield return Wait(() => Team.Client.Snapshot.TeamId == teamId && Team.Client.Snapshot.Members.Count == 2 &&
                    Team.Client.Snapshot.Members.Any(r => r.PlayerId == _peerId) &&
                    Team.Client.Snapshot.Members.Any(r => r.PlayerId == Game.PlayerId), "member authoritative accepted team");
                if (_done) yield break;
                yield return Capture("accepted-" + i);
                Check("Round " + i + " accepted through real button; authoritative members agree for team " + teamId);
                Publish("joined");
                yield return Wait(() => PeerAt("confirmed", i), "leader confirms matching authoritative membership");
                if (_done) yield break;
                if (i < 3)
                {
                    yield return TeamIdle();
                    yield return Click("LeaveTeam");
                    yield return Click("TeamModalConfirm");
                    yield return Wait(() => !Team.Client.HasTeam, "authoritative leave before next invitation");
                    if (_done) yield break;
                    Publish("left");
                }
            }
            if (_done) yield break;
            Publish("all-rounds-complete");
            yield return Wait(() => PeerAt("cleanup-leave", 3), "leader requests isolated team cleanup");
            if (_done) yield break;
            yield return TeamIdle();
            yield return Click("LeaveTeam");
            yield return Click("TeamModalConfirm");
            yield return Wait(() => !Team.Client.HasTeam, "member leaves final acceptance team");
            if (_done) yield break;
            Publish("cleanup-left");
            yield return Wait(() => PeerAt("complete", 3), "leader final acknowledgement");
            if (_done) yield break;
            yield return VerifyFriendRemoved();
            if (_done) yield break;
            Check("No team membership and peer friendship absent after cleanup.");
            Publish("complete");
            yield return Finish();
        }

        private IEnumerator VerifyFriendRemoved()
        {
            bool verified = false;
            yield return Game.Call(ClientPlayerFriendGetFriendListHandler.MessageId, new GetFriendListRequest(),
                GetFriendListResponse.Parser, response =>
                {
                    if (!TipOk(response.ErrorMessage, "verify friend cleanup")) return;
                    if (response.Friends.Any(friend => friend.FriendPlayerId == _peerId))
                    { Fail("Acceptance friendship remains after cleanup."); return; }
                    verified = true;
                }, Fail);
            if (!_done && !verified) Fail("Friend cleanup read did not confirm removal.");
        }

        private IEnumerator TeamIdle()
        {
            yield return Wait(() => Team.Client.ServiceAvailable && !Team.Client.Busy &&
                !Team.Client.RefreshQueued && !Team.Client.InvitesQueued, "team request queue idle");
            if (!_done) yield return new WaitForSecondsRealtime(.55f);
        }
        private IEnumerator DirectoryIdle() => Wait(() => !Team.InvitationDirectory.IsLoading, "candidate directory read complete", 35);

        private IEnumerator Wait(Func<bool> ready, string step, float timeout = 35)
        {
            if (_done) yield break;
            float until = Time.realtimeSinceStartup + timeout;
            while (!_done && Time.realtimeSinceStartup < until)
            {
                if (TryPeer(out var peer) && peer.phase == "failed") { Fail("Peer failed: " + peer.error); yield break; }
                bool complete = false;
                try { complete = ready(); } catch (Exception ex) { Fail(step + ": " + ex.Message); }
                if (complete || _done) yield break;
                yield return null;
            }
            if (!_done) Fail("Timeout: " + step + "; team=" + Team?.Client?.Status + "; directory=" + Team?.InvitationDirectory?.Status);
        }

        private bool TipOk(TipInfoMessage tip, string action)
        {
            if (tip == null || tip.Id == 0) return true;
            Fail(action + " rejected, tip=" + tip.Id); return false;
        }
        private bool TryPeer(out Signal signal)
        {
            signal = null;
            if (string.IsNullOrEmpty(_directory) || string.IsNullOrEmpty(_run)) return false;
            try
            {
                var path = SignalPath(OtherMode);
                if (!File.Exists(path)) return false;
                signal = JsonUtility.FromJson<Signal>(File.ReadAllText(path));
                return signal != null && signal.run == _run && signal.mode == OtherMode;
            }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }
        private bool PeerAt(string phase, int round) => TryPeer(out var signal) && signal.phase == phase && signal.round == round;

        private void Publish(string phase)
        {
            _phase = phase;
            var signal = new Signal { run = _run, mode = _mode, phase = phase, round = _round,
                playerId = (Game?.PlayerId ?? 0).ToString(), peerId = _peerId.ToString(),
                sceneId = (Game?.CurrentSceneId ?? 0).ToString(), sceneConfigId = Game?.CurrentSceneConfigId ?? 0,
                teamId = (Team?.Client?.Snapshot.TeamId ?? 0).ToString(), error = _error, utc = DateTime.UtcNow.ToString("O") };
            WriteJson(SignalPath(_mode), JsonUtility.ToJson(signal, true));
            Debug.Log("[TeamVerify][" + _mode + "] phase=" + phase + " round=" + _round + " player_id=" + signal.playerId + " team_id=" + signal.teamId);
        }
        private static void WriteJson(string path, string text)
        {
            string staging = path + ".writing";
            File.WriteAllText(staging, text);
            File.Copy(staging, path, true);
            File.Delete(staging);
        }
        private void Check(string message) { _checks.Add(message); Debug.Log("[TeamVerify][" + _mode + "] CHECK " + message); }

        private Button FindButton(string name) => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude)
            .FirstOrDefault(button => button.name == name && button.isActiveAndEnabled);

        private IEnumerator Click(string name)
        {
            if (_done) yield break;
            // 新打开的 Canvas 必须先完成原生绘制；仅强制布局不会生成 Graphic.depth。
            yield return null;
            Button button = null;
            PointerEventData pointer = null;
            var hits = new List<RaycastResult>();
            float until = Time.realtimeSinceStartup + 5f;
            bool hittable = false;
            while (!_done && Time.realtimeSinceStartup < until)
            {
                button = FindButton(name);
                if (button != null && button.IsInteractable() && EventSystem.current != null)
                {
                    Canvas.ForceUpdateCanvases();
                    var rect = (RectTransform)button.transform;
                    var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                    pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
                    hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
                    hittable = hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button;
                    if (hittable) break;
                }
                yield return null;
            }
            if (_done) yield break;
            if (!hittable)
            { Fail("Real button not hittable: " + name + "; point=" + pointer?.position + "; hits=" +
                string.Join(",", hits.Take(6).Select(h => h.gameObject.name + "@" + h.sortingOrder))); yield break; }
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return null;
            if (_done) yield break;
            if (button == null || !button.IsInteractable()) { Fail("Button changed before click: " + name); yield break; }
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            _clicks.Add(name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            yield return null;
        }

        private IEnumerator Capture(string name)
        {
            if (_done) yield break;
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try
            {
                string path = Path.Combine(_directory, _run + "-" + _mode + "-" + name + ".png");
                texture = RenderLiveFrame();
                if (texture == null || texture.width < 100) throw new IOException("No real screen frame.");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                var state = Team.Client.Snapshot;
                var candidate = Team.InvitationDirectory.Candidates.FirstOrDefault(role => role.PlayerId == _peerId);
                var item = new Evidence { name = name, source = Team.InvitationDirectory.Source.ToString(), utc = DateTime.UtcNow.ToString("O"),
                    playerId = Game.PlayerId.ToString(), peerId = _peerId.ToString(), teamId = state.TeamId.ToString(),
                    leaderId = state.LeaderId.ToString(), version = state.Version.ToString(), membershipEpoch = state.MembershipEpoch.ToString(),
                    screenshot = path, members = state.Members.Select(role => role.PlayerId.ToString()).ToArray(),
                    candidates = Team.InvitationDirectory.Candidates.Select(role => role.PlayerId.ToString()).ToArray(),
                    clickedButtons = _clicks.ToArray(), inGame = Game.InGame, gateReady = Game.IsGateReady,
                    directoryLoading = Team.InvitationDirectory.IsLoading, socialVisible = Social.Window.IsVisible,
                    candidateOnline = candidate?.IsOnline ?? false, onlineStatusKnown = candidate?.OnlineStatusKnown ?? false };
                _evidence.Add(item);
                File.WriteAllText(path + ".json", JsonUtility.ToJson(item, true));
            }
            catch (Exception ex) { Fail("Evidence capture: " + ex.Message); }
            finally { if (texture != null) Destroy(texture); }
        }

        private IEnumerator Finish()
        {
            _done = true;
            Game.Disconnect();
            _checks.Add("Client session disconnected normally after cleanup.");
            WriteReport("PASS");
            Debug.Log("[TeamVerify][" + _mode + "] RESULT=PASS run=" + _run + " rounds=4");
            yield return new WaitForSecondsRealtime(1);
            Application.Quit(0);
        }
        private void Fail(string error)
        {
            if (_done) return;
            _error = error; _done = true;
            try
            {
                if (!string.IsNullOrEmpty(_directory) && Directory.Exists(_directory))
                {
                    var frame = RenderLiveFrame();
                    if (frame != null) { File.WriteAllBytes(Path.Combine(_directory, _run + "-" + _mode + "-failure.png"), frame.EncodeToPNG()); Destroy(frame); }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[TeamVerify] Failure frame unavailable: " + ex.Message); }
            Debug.LogError("[TeamVerify][" + _mode + "] RESULT=FAIL phase=" + _phase + " round=" + _round + " reason=" + error);
            try { if (!string.IsNullOrEmpty(_directory) && Directory.Exists(_directory)) { Publish("failed"); WriteReport("FAIL"); } }
            catch (Exception ex) { Debug.LogError("[TeamVerify] Cannot save failure report: " + ex.Message); }
            StartCoroutine(FinishFailureFrame());
        }
        private IEnumerator FinishFailureFrame()
        {
            yield return new WaitForEndOfFrame();
            Texture2D frame = null;
            try
            {
                frame = RenderLiveFrame();
                if (frame != null) File.WriteAllBytes(Path.Combine(_directory, _run + "-" + _mode + "-failure.png"), frame.EncodeToPNG());
            }
            catch (Exception ex) { Debug.LogWarning("[TeamVerify] Failure frame: " + ex.Message); }
            finally { if (frame != null) Destroy(frame); }
            Application.Quit(1);
        }
        // 隐藏的 Windows 播放器没有可读的屏幕后缓冲；用真实摄像机和当前 UI 树离屏绘制。
        private static Texture2D RenderLiveFrame()
        {
            var camera = Camera.main;
            if (camera == null) throw new IOException("Live scene camera unavailable.");
            int width = Math.Max(1920, Screen.width), height = Math.Max(1080, Screen.height);
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
                .Select(c => (canvas: c, mode: c.renderMode, camera: c.worldCamera, distance: c.planeDistance)).ToArray();
            try
            {
                camera.targetTexture = target;
                foreach (var state in canvases)
                    if (state.mode == RenderMode.ScreenSpaceOverlay)
                    { state.canvas.renderMode = RenderMode.ScreenSpaceCamera; state.canvas.worldCamera = camera; state.canvas.planeDistance = 2; }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
                frame.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                frame.Apply();
                return frame;
            }
            finally
            {
                foreach (var state in canvases)
                { state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.distance; }
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Canvas.ForceUpdateCanvases();
            }
        }
        private void WriteReport(string result)
        {
            var report = new Report { result = result, mode = _mode, run = _run, error = _error, utc = DateTime.UtcNow.ToString("O"),
                scope = "two_real_Unity_GameClient_accounts_production_UI_pointer_events_authoritative_server_snapshots",
                checks = _checks.ToArray(), evidence = _evidence.ToArray() };
            File.WriteAllText(Path.Combine(_directory, _run + "-" + _mode + "-report.json"), JsonUtility.ToJson(report, true));
        }
    }
}
#endif
