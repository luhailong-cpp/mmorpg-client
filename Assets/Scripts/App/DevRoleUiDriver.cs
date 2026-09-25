using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MmorpgClient.Game;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.App
{
    /// <summary>Opt-in programmatic Unity button input. Login itself remains the network autopilot.</summary>
    public sealed class DevRoleUiDriver
    {
        private readonly MonoBehaviour _host;
        private readonly RoleFlowUi _ui;
        private readonly Func<uint, IReadOnlyList<AccountSimplePlayer>, GameClient.PlayerChoice, IEnumerator> _original;
        private readonly DevAutoPilot.Options _options;
        private readonly Action<string> _log, _error;
        private readonly Func<bool> _cancelled;
        private readonly string _scope;
        private readonly Func<Texture2D> _disconnectedCapture;
        private readonly List<string> _buttons = new();
        private bool _failed;

        [Serializable] private sealed class RoleRecord
        {
            public string playerId, appearanceId;
            public uint classId, gender;
        }
        [Serializable] private sealed class CaptureRecord
        {
            public string scope, inputMethod, captureMethod, action, appearanceId, playerId, screenshot, screenshotSha256, utc;
            public int frame, width, height;
            public uint classId, gender, zoneId;
            public string[] buttons;
            public RoleRecord[] serverRoles;
            public bool uiChoiceVerified;
        }

        public DevRoleUiDriver(MonoBehaviour host, RoleFlowUi ui,
            Func<uint, IReadOnlyList<AccountSimplePlayer>, GameClient.PlayerChoice, IEnumerator> original,
            DevAutoPilot.Options options, Action<string> log, Action<string> error,
            Func<bool> cancelled = null, string scope = "network_login_response_roles", Func<Texture2D> disconnectedCapture = null)
        {
            _host = host; _ui = ui; _original = original; _options = options;
            _log = log; _error = error; _cancelled = cancelled; _scope = scope; _disconnectedCapture = disconnectedCapture;
        }

        // Only the real RoleFlowUi callbacks write choice. This wrapper observes their result.
        public IEnumerator Choose(uint zone, IReadOnlyList<AccountSimplePlayer> roles, GameClient.PlayerChoice choice)
        {
            _failed = false;
            _buttons.Clear();
            var definition = QdaoCharacterCatalog.Find(_options.AppearanceId);
            if (_host == null || _ui == null || _original?.Target != _ui ||
                definition?.ResolveAppearance() == null || !QdaoCharacterCatalog.IsRetainedOriginal(_options.AppearanceId) ||
                string.IsNullOrWhiteSpace(_options.ShotDir) || _options.RoleClass < 1 || _options.RoleClass > 4 ||
                _options.RoleGender < 1 || _options.RoleGender > 2 || roles == null ||
                _disconnectedCapture != null && _scope != "disconnected_ui_fixture")
            { Reject("正式角色UI/原Chooser/齐套外观/截图目录/职业性别参数不完整"); yield break; }

            var matches = roles.Where(role => role != null && role.AppearanceId == _options.AppearanceId).ToArray();
            if (matches.Length > 1 || matches.Length == 0 && _options.RequireAppearanceRole)
            { Reject("服务器角色列表缺少唯一指定外观，禁止重登创建替代角色"); yield break; }
            var selected = matches.SingleOrDefault();
            if (selected != null && (selected.PlayerId == 0 || selected.ClassId != _options.RoleClass || selected.Gender != _options.RoleGender))
            { Reject("服务器角色职业/性别与验收目标不一致"); yield break; }
            if (selected == null && roles.Count >= 5)
            { Reject("正式角色位已满，不能自动创建或删除已有角色"); yield break; }
            foreach (var role in roles.Where(role => role != null))
                _log?.Invoke($"appearance_ui_server_role player_id={role.PlayerId} appearance_id={role.AppearanceId} class_id={role.ClassId} gender={role.Gender} zone={zone}");

            bool resolved = false;
            var routine = _host.StartCoroutine(ObserveOriginal(zone, roles, choice, () => resolved = true));
            CaptureRecord capture = null;
            try
            {
                yield return null; // Let the actual chooser display and lay out its canvas.
                if (Stopped) yield break;
                if (selected == null)
                {
                    if (roles.Count > 0) { yield return Click("CreateNewRole"); if (Stopped) yield break; }
                    yield return Click("Class_" + _options.RoleClass); if (Stopped) yield break;
                    yield return Click(_options.RoleGender == 1 ? "GenderMale" : "GenderFemale"); if (Stopped) yield break;
                    int count = QdaoCharacterCatalog.RetainedOriginalAll.Count(entry => entry.ResolveAppearance() != null) + 1;
                    // The label and portrait are both read from the displayed UI, not its private selection fields.
                    while (count-- > 0 && !PreviewMatches())
                    { yield return Click("NextAppearance"); if (Stopped) yield break; }
                }
                else { yield return Click("Role_" + selected.PlayerId); if (Stopped) yield break; }

                if (!PreviewMatches()) { Reject("真实角色预览头像/外观名称与目标不一致"); yield break; }
                // Batch-mode Editor tests cannot wait for a player EndOfFrame. Their explicit
                // disconnected camera capture is recorded separately and never used online.
                if (_disconnectedCapture == null) yield return new WaitForEndOfFrame();
                else yield return null;
                if (Stopped) yield break;
                try { capture = Capture(zone, roles, selected); }
                catch (Exception error) { Reject("角色UI截图失败: " + error.Message); }
                if (Stopped) yield break;
                yield return Click(selected == null ? "ConfirmCreate" : "EnterSanctuary");
                if (Stopped) yield break;
                float until = Time.realtimeSinceStartup + 5f;
                while (!resolved && !Stopped && Time.realtimeSinceStartup < until) yield return null;
                if (Stopped) yield break;
                bool choiceMatches = resolved && !choice.Cancelled && (selected == null
                    ? choice.CreateNew && choice.AppearanceId == _options.AppearanceId && choice.ClassId == _options.RoleClass && choice.Gender == _options.RoleGender
                    : !choice.CreateNew && choice.SelectedPlayerId == selected.PlayerId);
                if (!choiceMatches) { Reject("实际按钮未完成原Chooser或返回了错误角色身份"); yield break; }
                try
                {
                    capture.uiChoiceVerified = true;
                    capture.buttons = _buttons.ToArray();
                    WriteNew(capture.screenshot + ".json", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(capture, true)));
                    if (!File.Exists(capture.screenshot) || new FileInfo(capture.screenshot).Length == 0)
                        throw new IOException("截图文件未保存");
                }
                catch (Exception error) { Reject("角色UI证据保存失败: " + error.Message); }
                if (Stopped) yield break;
                _log?.Invoke($"appearance_ui action={capture.action} appearance_id={_options.AppearanceId} player_id={selected?.PlayerId ?? 0} class_id={_options.RoleClass} gender={_options.RoleGender} input=programmatic_unity_buttons scope={_scope} screenshot={capture.screenshot} sha256={capture.screenshotSha256}");
            }
            finally
            {
                if (!resolved && routine != null) _host.StopCoroutine(routine);
            }
        }

        private IEnumerator ObserveOriginal(uint zone, IReadOnlyList<AccountSimplePlayer> roles,
            GameClient.PlayerChoice choice, Action complete)
        {
            IEnumerator original = null;
            try { original = _original(zone, roles, choice); }
            catch (Exception error) { Reject("原Chooser启动失败: " + error.Message); }
            if (original == null) yield break;
            try
            {
                while (!Stopped)
                {
                    bool next = false;
                    try { next = original.MoveNext(); }
                    catch (Exception error) { Reject("原Chooser执行失败: " + error.Message); }
                    if (Stopped) yield break;
                    if (!next) { complete(); yield break; }
                    yield return original.Current;
                }
            }
            finally { (original as IDisposable)?.Dispose(); }
        }

        private bool Stopped => _failed || (_cancelled?.Invoke() ?? false);
        private void Reject(string message)
        {
            if (_failed) return;
            _failed = true;
            _error?.Invoke(message);
        }

        private bool PreviewMatches()
        {
            var hero = _ui.GetComponentsInChildren<Image>(false).SingleOrDefault(image => image.name == "CharacterArtwork");
            var title = _ui.GetComponentsInChildren<TMPro.TMP_Text>(false).SingleOrDefault(text => text.name == "PreviewTitle");
            return hero != null && hero.enabled && hero.sprite != null &&
                hero.sprite == QdaoCharacterCatalog.LoadPortrait(_options.AppearanceId) &&
                title != null && title.text == QdaoCharacterCatalog.Find(_options.AppearanceId).Name;
        }

        private IEnumerator Click(string name)
        {
            if (Stopped) yield break;
            var buttons = _ui.GetComponentsInChildren<Button>(false).Where(button => button.name == name).ToArray();
            if (buttons.Length != 1 || !buttons[0].isActiveAndEnabled || !buttons[0].IsInteractable() || EventSystem.current == null)
            { Reject("真实按钮不可用: " + name); yield break; }
            var button = buttons[0];
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button)
            { Reject("真实按钮被遮挡或未命中: " + name); yield break; }
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return null;
            if (Stopped) yield break;
            if (!button.isActiveAndEnabled || !button.IsInteractable()) { Reject("点击前按钮状态变化: " + name); yield break; }
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            _buttons.Add(name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            yield return null;
        }

        private CaptureRecord Capture(uint zone, IReadOnlyList<AccountSimplePlayer> roles, AccountSimplePlayer selected)
        {
            Texture2D texture = null;
            try
            {
                Directory.CreateDirectory(_options.ShotDir);
                var path = Path.GetFullPath(Path.Combine(_options.ShotDir,
                    "role-ui-" + _options.AppearanceId + "-" + Guid.NewGuid().ToString("N") + ".png"));
                texture = _disconnectedCapture == null ? ScreenCapture.CaptureScreenshotAsTexture() : _disconnectedCapture();
                if (texture == null || texture.width <= 0 || texture.height <= 0) throw new IOException("没有实际屏幕帧");
                var png = texture.EncodeToPNG();
                WriteNew(path, png);
                using var hash = SHA256.Create();
                return new CaptureRecord
                {
                    scope = _scope, inputMethod = "programmatic_unity_pointer_events", action = selected == null ? "create" : "select",
                    captureMethod = _disconnectedCapture == null ? "standalone_screen_end_of_frame" : "disconnected_canvas_camera_render",
                    appearanceId = _options.AppearanceId, playerId = (selected?.PlayerId ?? 0).ToString(),
                    classId = _options.RoleClass, gender = _options.RoleGender, zoneId = zone,
                    screenshot = path, screenshotSha256 = BitConverter.ToString(hash.ComputeHash(png)).Replace("-", "").ToLowerInvariant(),
                    utc = DateTime.UtcNow.ToString("O"), frame = Time.frameCount, width = texture.width, height = texture.height,
                    serverRoles = roles.Where(role => role != null).Select(role => new RoleRecord
                    { playerId = role.PlayerId.ToString(), appearanceId = role.AppearanceId, classId = role.ClassId, gender = role.Gender }).ToArray()
                };
            }
            finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(bytes, 0, bytes.Length);
            file.Flush(true);
        }
    }
}
