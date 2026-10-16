using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    /// <summary>Standalone inspection of delivered NPC textures; no world or network dependencies.</summary>
    public sealed class NpcArtShowroom : MonoBehaviour
    {
        private const string ManifestPath = "World/Tianyong/Npcs/placements";
        private const string SpriteRoot = "World/Tianyong/Npcs/Sprites/";
        private const int ExpectedCount = 23;
        [Serializable] private sealed class Manifest { public Entry[] entries; }
        [Serializable] private sealed class Entry
        {
            public string id, displayName, spriteResource;
            public int nativeWidth, nativeHeight;
        }
        [Serializable] private sealed class Check
        {
            public string name, id, detail, screenshot;
            public bool passed;
        }
        [Serializable] private sealed class VerificationReport
        {
            public string generatedAtUtc;
            public string scope = "Independent static NPC showroom; not main-city gameplay and not input-click automation.";
            public int expectedNpcCount = ExpectedCount, actualNpcCount, checkCount, passedCount, failedCount;
            public bool passed;
            public Check[] checks;
            public string[] runtimeErrors;
        }

        private static readonly Color Jade = new Color(0.10f, 0.27f, 0.24f);
        private static readonly Color Ivory = new Color(0.97f, 0.95f, 0.88f);
        private static readonly Color Gold = new Color(0.79f, 0.64f, 0.35f);
        private Entry[] _entries = Array.Empty<Entry>();
        private Texture2D _texture;
        private Font _font;
        private string _fatal, _selectionError, _verifyDirectory;
        private int _selected, _background, _lastDrawnFrame = -1;
        private string _lastDrawnId;
        private float _lastDrawnAspect, _zoom = 1f;
        private Vector2 _pan, _listScroll;
        private bool _dragging, _verifying;
        private GUIStyle _title, _body, _small, _button, _selectedButton, _error, _slider, _thumb;
        private readonly List<Texture2D> _styleTextures = new List<Texture2D>();
        private readonly List<Check> _checks = new List<Check>();
        private readonly List<string> _runtimeErrors = new List<string>();

        private void Awake()
        {
            Application.targetFrameRate = 60;
            _font = Resources.Load<Font>("Fonts/TeamNotoSansSC");
            try
            {
                var source = Resources.Load<TextAsset>(ManifestPath);
                if (source == null) throw new InvalidOperationException("缺少 NPC 名单，无法开始展示。");
                var manifest = JsonUtility.FromJson<Manifest>(source.text);
                if (manifest == null || manifest.entries == null || manifest.entries.Length == 0)
                    throw new InvalidOperationException("NPC 名单为空或格式不正确。");
                _entries = manifest.entries;
                Select(0);
            }
            catch (Exception exception) { _fatal = exception.Message; }
            if (_font == null) _fatal = "缺少中文字体，无法可靠显示姓名。";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-npcShowroomVerify", StringComparison.OrdinalIgnoreCase)) continue;
                _verifying = true;
                _verifyDirectory = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
                break;
            }
            if (_verifying)
            {
                Application.runInBackground = true;
                Application.logMessageReceived += RecordRuntimeError;
                StartCoroutine(Verify());
            }
        }

        private bool Select(int index)
        {
            _selectionError = null;
            if (_entries.Length == 0) return false;
            _selected = (index % _entries.Length + _entries.Length) % _entries.Length;
            _zoom = 1f;
            _pan = Vector2.zero;
            _lastDrawnId = null;
            _lastDrawnFrame = -1;
            _listScroll.y = Mathf.Max(0, _selected * 52f - (Screen.height - 200f) * 0.4f);
            var entry = _entries[_selected];
            if (_texture != null) Resources.UnloadAsset(_texture);
            _texture = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || string.IsNullOrWhiteSpace(entry.displayName) ||
                string.IsNullOrWhiteSpace(entry.spriteResource) || !entry.spriteResource.StartsWith(SpriteRoot, StringComparison.Ordinal) ||
                entry.spriteResource.Contains("..") || entry.spriteResource.Contains("\\") || entry.spriteResource.Contains("."))
            {
                _selectionError = "这位 NPC 的姓名或图片路径不完整。";
                return false;
            }
            _texture = Resources.Load<Texture2D>(entry.spriteResource);
            if (_texture == null) _selectionError = "未找到「" + entry.displayName + "」的图片。";
            return _texture != null;
        }

        private void Update()
        {
            if (_verifying || _entries.Length == 0) return;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Select(_selected - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) Select(_selected + 1);
        }

        private Rect PreviewRect(float side, float margin)
        {
            return new Rect(side + margin * 2f, 166f, Mathf.Max(180f, Screen.width - side - margin * 3f), Mathf.Max(100f, Screen.height - 324f));
        }

        private void OnGUI()
        {
            EnsureStyles();
            float margin = Mathf.Clamp(Screen.width * 0.016f, 12f, 24f);
            float side = Mathf.Clamp(Screen.width * 0.22f, 210f, 300f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), Ivory);
            Fill(new Rect(0, 0, Screen.width, 104f), Jade);
            Fill(new Rect(0, 103f, Screen.width, 2f), Gold);
            GUI.Label(new Rect(margin, 17f, Screen.width - margin * 2f, 38f), "主城 NPC 展示", _title);
            GUI.Label(new Rect(margin, 62f, Screen.width - margin * 2f, 26f),
                "静态 NPC 素材展示 · 无对话与动画 · 等待新主城接入", LightBody());
            GUI.Label(new Rect(margin, 119f, side, 32f), "人物名录  " + _entries.Length + " 位", _body);
            var listRect = new Rect(margin, 166f, side, Mathf.Max(100f, Screen.height - 190f));
            Panel(listRect);
            _listScroll = GUI.BeginScrollView(listRect, _listScroll, new Rect(0, 0, side - 22f, _entries.Length * 52f), false, true);
            for (int i = 0; i < _entries.Length; i++)
            {
                GUI.SetNextControlName("npc-" + i);
                string label = (i + 1).ToString("00") + "   " + (_entries[i]?.displayName ?? "名单缺失");
                if (GUI.Button(new Rect(5f, i * 52f + 4f, side - 32f, 44f), label,
                        i == _selected ? _selectedButton : _button) && !_verifying) Select(i);
            }
            GUI.EndScrollView();

            var viewport = PreviewRect(side, margin);
            var entry = _entries.Length > 0 ? _entries[_selected] : null;
            GUI.Label(new Rect(viewport.x, 119f, Mathf.Max(100f, viewport.width - 230f), 34f),
                entry == null ? "暂无人物" : entry.displayName, _body);
            if (NamedButton("previous", new Rect(viewport.xMax - 220f, 114f, 106f, 44f), "上一位")) Select(_selected - 1);
            if (NamedButton("next", new Rect(viewport.xMax - 106f, 114f, 106f, 44f), "下一位")) Select(_selected + 1);
            HandlePreviewInput(viewport);
            GUI.BeginGroup(viewport);
            DrawBackdrop(new Rect(0, 0, viewport.width, viewport.height));
            if (_texture != null)
            {
                float scale = FitScale(viewport) * _zoom;
                var size = new Vector2(_texture.width * scale, _texture.height * scale);
                var rect = new Rect((viewport.width - size.x) * 0.5f + _pan.x, (viewport.height - size.y) * 0.5f + _pan.y, size.x, size.y);
                GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill, true);
                if (Event.current.type == EventType.Repaint)
                {
                    _lastDrawnId = entry?.id;
                    _lastDrawnFrame = Time.frameCount;
                    _lastDrawnAspect = rect.width / rect.height;
                }
            }
            GUI.EndGroup();
            Border(viewport, Gold);
            string problem = _fatal ?? _selectionError;
            if (!string.IsNullOrEmpty(problem))
                GUI.Label(new Rect(viewport.x + 24f, viewport.y + 24f, viewport.width - 48f, 110f), problem, _error);

            float controlsY = viewport.yMax + 12f;
            float choiceWidth = Mathf.Min(106f, (viewport.width - 16f) / 3f);
            string[] backgrounds = { "浅底", "深底", "棋盘" };
            for (int i = 0; i < backgrounds.Length; i++)
                if (NamedButton("background-" + i, new Rect(viewport.x + i * (choiceWidth + 8f), controlsY, choiceWidth, 44f), backgrounds[i], _background == i)) _background = i;
            if (_texture != null && viewport.width >= 560f)
                GUI.Label(new Rect(viewport.x + 350f, controlsY + 3f, viewport.width - 350f, 40f),
                    "原图 " + _texture.width + " × " + _texture.height, _small);
            float secondY = controlsY + 52f;
            GUI.Label(new Rect(viewport.x, secondY + 6f, 72f, 30f), "缩放", _small);
            GUI.SetNextControlName("zoom");
            float newZoom = GUI.HorizontalSlider(new Rect(viewport.x + 60f, secondY + 7f, Mathf.Max(64f, viewport.width - 300f), 32f), _zoom, 0.25f, 8f, _slider, _thumb);
            if (!_verifying) _zoom = newZoom;
            if (NamedButton("native-size", new Rect(viewport.xMax - 224f, secondY, 106f, 44f), "原图 1:1") && _texture != null)
            { _zoom = Mathf.Clamp(1f / FitScale(viewport), 0.25f, 8f); _pan = Vector2.zero; }
            if (NamedButton("reset", new Rect(viewport.xMax - 110f, secondY, 110f, 44f), "重置视图"))
            { _zoom = 1f; _pan = Vector2.zero; }
            string pixels = _texture == null ? "" : "  ·  显示 " + (FitScale(viewport) * _zoom * 100f).ToString("0") + "%";
            GUI.Label(new Rect(viewport.x, secondY + 51f, viewport.width, 28f), "左右键切换 · 滚轮缩放 · 拖动查看细节" + pixels, _small);
        }

        private bool NamedButton(string name, Rect rect, string label, bool selected = false)
        {
            GUI.SetNextControlName(name);
            return GUI.Button(rect, label, selected ? _selectedButton : _button) && !_verifying;
        }

        private float FitScale(Rect viewport)
        {
            return _texture == null ? 1f : Mathf.Min((viewport.width - 36f) / _texture.width, (viewport.height - 28f) / _texture.height);
        }

        private void HandlePreviewInput(Rect viewport)
        {
            if (_verifying) return;
            var current = Event.current;
            if (current.type == EventType.ScrollWheel && viewport.Contains(current.mousePosition))
            {
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.12f, -current.delta.y), 0.25f, 8f);
                var mouse = current.mousePosition - viewport.center;
                _pan = mouse - (mouse - _pan) * (_zoom / oldZoom);
                current.Use();
            }
            if (current.type == EventType.MouseDown && current.button == 0 && viewport.Contains(current.mousePosition))
            { _dragging = true; GUI.FocusControl(null); current.Use(); }
            if (current.type == EventType.MouseDrag && _dragging)
            { _pan += current.delta; current.Use(); }
            if (current.type == EventType.MouseUp && _dragging)
            { _dragging = false; current.Use(); }
        }

        private void DrawBackdrop(Rect rect)
        {
            Fill(rect, _background == 1 ? new Color(0.12f, 0.18f, 0.17f) : new Color(0.99f, 0.98f, 0.94f));
            if (_background != 2) return;
            const float cell = 24f;
            for (int row = 0; row * cell < rect.height; row++)
                for (int col = 0; col * cell < rect.width; col++)
                    if ((row + col) % 2 == 0)
                        Fill(new Rect(col * cell, row * cell, cell, cell), new Color(0.77f, 0.80f, 0.75f));
        }

        private void EnsureStyles()
        {
            if (_body != null) return;
            _body = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 20, alignment = TextAnchor.MiddleLeft };
            _body.normal.textColor = Jade;
            _small = new GUIStyle(_body) { fontSize = 16 };
            _title = new GUIStyle(_body) { fontSize = 30, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Ivory;
            _error = new GUIStyle(_body) { wordWrap = true, fontSize = 18 };
            _error.normal.textColor = new Color(0.68f, 0.15f, 0.12f);
            _button = new GUIStyle(GUI.skin.button) { font = _font, fontSize = 17, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 4, 4) };
            SetState(_button.normal, new Color(0.91f, 0.92f, 0.85f), Jade);
            SetState(_button.hover, new Color(0.79f, 0.87f, 0.79f), Jade);
            SetState(_button.active, Jade, Ivory);
            SetState(_button.focused, Gold, Jade);
            SetState(_button.onNormal, Jade, Ivory);
            SetState(_button.onHover, Jade, Ivory);
            SetState(_button.onActive, Gold, Jade);
            SetState(_button.onFocused, Gold, Jade);
            _selectedButton = new GUIStyle(_button);
            SetState(_selectedButton.normal, Jade, Ivory);
            SetState(_selectedButton.hover, new Color(0.17f, 0.38f, 0.32f), Ivory);
            SetState(_selectedButton.focused, Gold, Jade);
            _slider = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 32f };
            _thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedWidth = 28f, fixedHeight = 32f };
            SetState(_thumb.focused, Gold, Jade);
        }

        private GUIStyle LightBody()
        {
            var style = new GUIStyle(_small);
            style.normal.textColor = Ivory;
            return style;
        }

        private void SetState(GUIStyleState state, Color background, Color foreground)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, background);
            texture.Apply();
            _styleTextures.Add(texture);
            state.background = texture;
            state.textColor = foreground;
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
        private static void Border(Rect rect, Color color)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1f), color);
            Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            Fill(new Rect(rect.x, rect.y, 1f, rect.height), color);
            Fill(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }
        private static void Panel(Rect rect) { Fill(rect, new Color(0.94f, 0.94f, 0.88f)); Border(rect, Gold); }

        private IEnumerator Verify()
        {
            if (string.IsNullOrWhiteSpace(_verifyDirectory))
            { Debug.LogError("[NpcShowroom] -npcShowroomVerify requires an output directory."); Application.Quit(1); yield break; }
            try { _verifyDirectory = Path.GetFullPath(_verifyDirectory); Directory.CreateDirectory(_verifyDirectory); }
            catch (Exception exception) { Debug.LogError("[NpcShowroom] Verification directory: " + exception.Message); Application.Quit(1); yield break; }
            AddCheck("manifest-count", null, _entries.Length == ExpectedCount && _fatal == null,
                "Expected 23, actual " + _entries.Length + (_fatal == null ? "" : "; " + _fatal));
            AddCheck("bundled-chinese-font", null, _font != null, "Fonts/TeamNotoSansSC");
            // Awake can run before the player finishes its splash screen and first GUI repaint.
            float startupDeadline = Time.realtimeSinceStartup + 20f;
            while (_fatal == null && _lastDrawnFrame < 0 && Time.realtimeSinceStartup < startupDeadline)
                yield return null;
            bool ready = _fatal == null && _lastDrawnFrame >= 0;
            AddCheck("first-gui-repaint", null, ready,
                "First rendered frame=" + _lastDrawnFrame + "; splash finished=" + UnityEngine.Rendering.SplashScreen.isFinished);
            if (!ready) { FinishVerification(); yield break; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i];
                bool selected = Select(i);
                yield return null;
                yield return new WaitForEndOfFrame();
                bool dimensions = entry != null && entry.nativeWidth > 0 && entry.nativeHeight > 0 && _texture != null &&
                    _texture.width == entry.nativeWidth && _texture.height == entry.nativeHeight;
                bool drawn = entry != null && _lastDrawnId == entry.id && _lastDrawnFrame >= Time.frameCount - 1 && _texture != null &&
                    Mathf.Abs(_lastDrawnAspect - (float)_texture.width / _texture.height) < 0.001f;
                bool unique = entry != null && ids.Add(entry.id ?? "");
                string actual = _texture == null ? "missing" : _texture.width + "x" + _texture.height;
                AddCheck("texture-selected-and-rendered", entry?.id, selected && dimensions && drawn && unique,
                    "Expected " + (entry?.nativeWidth ?? 0) + "x" + (entry?.nativeHeight ?? 0) + "; actual " + actual +
                    "; selected=" + selected + "; drawn=" + drawn + "; unique=" + unique + "; " + (_selectionError ?? ""));
            }
            yield return Capture(0, 0, 1f, "01-light.png");
            yield return Capture(13, 1, 1f, "14-dark.png");
            yield return Capture(22, 2, 1f, "23-checker.png");
            yield return Capture(22, 2, 3.5f, "23-high-zoom.png");
            FinishVerification();
        }

        private IEnumerator Capture(int index, int background, float zoom, string filename)
        {
            if (index >= _entries.Length || !Select(index))
            { AddCheck("screenshot", null, false, "Could not select NPC " + (index + 1), filename); yield break; }
            _background = background;
            _zoom = zoom;
            yield return null;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_verifyDirectory, filename);
            string captureError = null;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
            }
            catch (Exception exception) { captureError = exception.Message; }
            if (captureError != null)
            { AddCheck("screenshot", _entries[index].id, false, captureError, filename); yield break; }
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (File.Exists(path) && new FileInfo(path).Length > 24) break;
            }
            bool valid = false;
            string detail;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                int width = ReadBigEndian(bytes, 16), height = ReadBigEndian(bytes, 20);
                valid = bytes.Length > 24 && bytes[0] == 137 && bytes[1] == 80 && width == Screen.width && height == Screen.height &&
                    _lastDrawnId == _entries[index].id;
                detail = "ScreenCapture " + width + "x" + height + "; fit multiplier=" + zoom + "; background=" + background;
            }
            catch (Exception exception) { detail = exception.Message; }
            AddCheck("screenshot", _entries[index].id, valid, detail, filename);
        }

        private static int ReadBigEndian(byte[] bytes, int offset)
        {
            if (bytes.Length < offset + 4) return 0;
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
        private void AddCheck(string name, string id, bool passed, string detail, string screenshot = null)
        { _checks.Add(new Check { name = name, id = id, passed = passed, detail = detail, screenshot = screenshot }); }
        private void RecordRuntimeError(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _runtimeErrors.Add(message + "\n" + stackTrace);
        }
        private void FinishVerification()
        {
            int passed = 0;
            foreach (var check in _checks) if (check.passed) passed++;
            var report = new VerificationReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"), actualNpcCount = _entries.Length,
                checkCount = _checks.Count, passedCount = passed, failedCount = _checks.Count - passed,
                passed = passed == _checks.Count && _entries.Length == ExpectedCount && _runtimeErrors.Count == 0,
                checks = _checks.ToArray(), runtimeErrors = _runtimeErrors.ToArray()
            };
            try { File.WriteAllText(Path.Combine(_verifyDirectory, "verification.json"), JsonUtility.ToJson(report, true)); }
            catch (Exception exception) { Debug.LogError("[NpcShowroom] Cannot write report: " + exception.Message); Application.Quit(1); return; }
            Debug.Log("[NpcShowroom] Verification " + (report.passed ? "PASS" : "FAIL") + ": " + report.passedCount + "/" + report.checkCount +
                "; runtime errors=" + _runtimeErrors.Count + "; independent static showroom, not main-city gameplay or input-click automation.");
            Application.Quit(report.passed ? 0 : 1);
        }
        private void OnDestroy()
        {
            Application.logMessageReceived -= RecordRuntimeError;
            foreach (var texture in _styleTextures) if (texture != null) Destroy(texture);
        }
    }
}
