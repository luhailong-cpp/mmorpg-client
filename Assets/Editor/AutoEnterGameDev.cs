using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.EditorDev
{
    /// <summary>
    /// Dev-only automation: when the sentinel file exists at the project root,
    /// enter Play mode, fill the login password and click "进入游戏" on the
    /// qdao server-select UI. Deletes the sentinel when done so a normal
    /// editor session is never affected. Safe to leave in the project.
    ///
    /// Sentinel: &lt;project&gt;/auto_enter_game.local.json  { "password": "..." }
    /// Progress log: &lt;project&gt;/auto_enter_game.local.log
    /// </summary>
    [InitializeOnLoad]
    internal static class AutoEnterGameDev
    {
        private static readonly string ProjectRoot =
            Directory.GetParent(Application.dataPath).FullName;
        private static readonly string SentinelPath =
            Path.Combine(ProjectRoot, "auto_enter_game.local.json");
        private static readonly string LogPath =
            Path.Combine(ProjectRoot, "auto_enter_game.local.log");

        private static bool _playRequested;
        private static bool _entered;
        private static double _nextPollAt;
        private static double _nextRefreshAt;

        static AutoEnterGameDev()
        {
            if (!File.Exists(SentinelPath)) return;
            Log("armed (sentinel present). isPlaying=" + EditorApplication.isPlaying);
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (_entered) return;
            if (EditorApplication.timeSinceStartup < _nextPollAt) return;
            _nextPollAt = EditorApplication.timeSinceStartup + 0.5;

            if (!File.Exists(SentinelPath))
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                if (_playRequested) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                _playRequested = true;
                Log("entering play mode");
                EditorApplication.EnterPlaymode();
                return;
            }

            try
            {
                DriveLogin();
            }
            catch (Exception ex)
            {
                Log("ERROR " + ex);
                Finish();
            }
        }

        private static void DriveLogin()
        {
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            if (TryResolveRoleChooser(f)) { Finish(); return; }
            var viewType = typeof(MmorpgClient.UI.Ugui.QdaoServerSelectView);
            var appField = viewType.GetField("_app", f);

            // A baked scene copy of the prefab can coexist with the live
            // runtime instance — only the initialized one has _app wired.
            var views = UnityEngine.Object.FindObjectsByType<
                MmorpgClient.UI.Ugui.QdaoServerSelectView>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            MmorpgClient.UI.Ugui.QdaoServerSelectView comp = null;
            foreach (var v in views)
                if (appField.GetValue(v) != null) { comp = v; break; }
            if (comp == null)
            {
                if (EditorApplication.timeSinceStartup >= _nextRefreshAt)
                {
                    _nextRefreshAt = EditorApplication.timeSinceStartup + 5.0;
                    Log("views=" + views.Length + ", none initialized (_app null); waiting");
                }
                return;
            }

            // Hot reload during play wipes AppBootstrap's non-serialized state
            // (Gateway/Session become null) and the client zombies. Detect it
            // and restart play mode cleanly.
            var app = (MmorpgClient.UI.AppBootstrap)appField.GetValue(comp);
            if (app.Gateway == null || app.Session == null)
            {
                Log("zombie AppBootstrap (Gateway/Session null) - restarting play mode");
                _playRequested = false;
                _nextPollAt = EditorApplication.timeSinceStartup + 2.0;
                EditorApplication.ExitPlaymode();
                return;
            }

            var t = comp.GetType();

            var busy = (bool)t.GetField("_busy", f).GetValue(comp);
            if (busy) return;

            var zoneId = (uint)t.GetField("_selectedZoneId", f).GetValue(comp);
            if (zoneId == 0)
            {
                // Server list not loaded (initial fetch may have raced the
                // gateway coming up). Nudge the refresh flow, throttled.
                var loading = (bool)t.GetField("_serverListLoading", f).GetValue(comp);
                var failed = (bool)t.GetField("_serverListFailed", f).GetValue(comp);
                if (!loading && EditorApplication.timeSinceStartup >= _nextRefreshAt)
                {
                    _nextRefreshAt = EditorApplication.timeSinceStartup + 5.0;
                    Log("list empty (failed=" + failed + ", active=" + comp.isActiveAndEnabled
                        + "); invoking OnRefreshClicked");
                    t.GetMethod("OnRefreshClicked", f)?.Invoke(comp, null);
                }
                return;
            }

            var findZone = t.GetMethod("FindZone", f);
            if (findZone != null && findZone.Invoke(comp, new object[] { zoneId }) == null)
            { Log("zone " + zoneId + " not in list yet..."); return; }

            string password = ReadPassword();
            if (string.IsNullOrEmpty(password)) { Log("sentinel has no password"); Finish(); return; }

            var accountInput = (TMPro.TMP_InputField)t.GetField("_accountInput", f).GetValue(comp);
            var passwordInput = (TMPro.TMP_InputField)t.GetField("_passwordInput", f).GetValue(comp);
            if (accountInput == null || passwordInput == null) return;
            if (string.IsNullOrWhiteSpace(accountInput.text))
                accountInput.SetTextWithoutNotify("dev_demo");
            passwordInput.SetTextWithoutNotify(password);

            Log("clicking enter: account=" + accountInput.text + " zone=" + zoneId);
            t.GetMethod("OnEnterClicked", f).Invoke(comp, null);
            Finish();
        }

        /// <summary>
        /// If the role select/create dialog is up and waiting, resolve it:
        /// pick the first existing character, or create one with defaults.
        /// Returns true when the choice was submitted.
        /// </summary>
        private static bool TryResolveRoleChooser(BindingFlags f)
        {
            var role = UnityEngine.Object.FindAnyObjectByType<
                MmorpgClient.UI.Ugui.Role.RoleFlowUi>(FindObjectsInactive.Include);
            if (role == null) return false;

            var rt = role.GetType();
            var canvasGo = (GameObject)rt.GetField("_canvasGo", f).GetValue(role);
            if (canvasGo == null || !canvasGo.activeSelf) return false;
            if ((bool)rt.GetField("_resolved", f).GetValue(role)) return false;
            // A hot reload wipes the non-serialized choice object while the
            // canvas stays active; fall through to the zombie restart instead.
            if (rt.GetField("_result", f).GetValue(role) == null) return false;

            var players = rt.GetField("_players", f).GetValue(role)
                as System.Collections.IList;
            if (players != null && players.Count > 0)
            {
                var p0 = players[0];
                ulong pid = Convert.ToUInt64(
                    p0.GetType().GetProperty("PlayerId").GetValue(p0));
                Log("role chooser: selecting existing player " + pid);
                rt.GetMethod("ResolveSelect", f).Invoke(role, new object[] { pid });
            }
            else
            {
                Log("role chooser: creating new character (defaults)");
                rt.GetMethod("ResolveCreate", f).Invoke(role, null);
            }
            return true;
        }

        private static string ReadPassword()
        {
            try
            {
                var json = File.ReadAllText(SentinelPath);
                var m = System.Text.RegularExpressions.Regex.Match(
                    json, "\"password\"\\s*:\\s*\"([^\"]*)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        private static void Finish()
        {
            _entered = true;
            EditorApplication.update -= Tick;
            try { File.Delete(SentinelPath); } catch { }
            Log("done (sentinel removed); RoleFlowUi takes over from here");
        }

        private static void Log(string msg)
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [AutoEnterGameDev] " + msg;
            Debug.Log(line);
            try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
        }
    }
}
