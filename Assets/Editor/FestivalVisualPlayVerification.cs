#if UNITY_EDITOR
using System;
using System.IO;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>
    /// Drives the saved offline preview scene in real Editor Play mode. Invoke with
    /// -executeMethod; do not pass -quit or -nographics. This is not a navigation test.
    /// </summary>
    [InitializeOnLoad]
    public static class FestivalVisualPlayVerification
    {
        private const string ArmedKey = "FestivalVisualPlayVerification.Armed";
        private const string StartKey = "FestivalVisualPlayVerification.StartUtc";
        private const string KeepEditorKey = "FestivalVisualPlayVerification.KeepEditor";
        private const string OutputKey = "FestivalVisualPlayVerification.OutputDirectory";
        private const int Width = 1600;
        private const int Height = 900;
        private const float Tolerance = 0.002f;
        private static readonly uint[] MapIds = { 26000, 26008, 26008 };
        private static readonly string[] Appearances = { "mid-autumn", "spring", "mid-autumn" };
        private static readonly int[] ExpectedColumns = { 5, 4, 4 };
        private static readonly int[] ExpectedRows = { 4, 3, 3 };

        private static int _index;
        private static int _waitFrames;
        private static int _bootstrapWaitFrames;
        private static int _lastObservedFrame = -1;
        private static FestivalMapVisualPreview _preview;
        private static Texture _springTile;
        private static bool _started;
        private static readonly System.Collections.Generic.List<string> RuntimeErrors = new();

        [Serializable]
        private sealed class MapResult
        {
            public uint mapId;
            public string appearance;
            public int columns;
            public int rows;
            public int loadedTiles;
            public bool validBoundsKnown;
            public float[] visualBounds;
            public string overview;
            public string lowerLeft;
            public string upperRight;
            public bool coordinatesVerified;
            public bool edgeClampVerified;
            public bool zoomVerified;
        }

        [Serializable]
        private sealed class Report
        {
            public string result = "RUNNING";
            public string scope = "Unity Editor Play mode; offline visual preview only; no navigation or collision validation";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string scene;
            public string error;
            public bool menuOpened;
            public bool playModeEntered;
            public int playFramesObserved;
            public bool appearanceSwitchVerified;
            public string[] runtimeErrors;
            public MapResult[] maps = new MapResult[3];
        }

        private static Report _report;

        static FestivalVisualPlayVerification()
        {
            if (SessionState.GetBool(ArmedKey, false)) RegisterCallbacks();
        }

        [MenuItem("MMORPG/Maps/验证三套离线视觉预览")]
        public static void RunInteractive()
        {
            // 手动验收完成后保留编辑器，只退出本次 Play。
            SessionState.SetBool(KeepEditorKey, true);
            SessionState.SetString(OutputKey, Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Builds/festival-maps-offline-20261112/editor-play-verification-final")));
            Run();
        }

        [MenuItem("MMORPG/Maps/验证三套离线视觉预览", true)]
        private static bool CanRunInteractive() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !SessionState.GetBool(ArmedKey, false);

        public static void Run()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Verification requires a fresh Edit mode session.");
                foreach (var scene in Scenes())
                    if (scene.isDirty) throw new InvalidOperationException("Open scene has unsaved changes: " + scene.path);
                _index = 0;
                _waitFrames = 0;
                _bootstrapWaitFrames = 0;
                _lastObservedFrame = -1;
                _preview = null;
                _springTile = null;
                _started = false;
                _report = null;
                RuntimeErrors.Clear();
                Directory.CreateDirectory(OutputDirectory());
                SessionState.SetBool(ArmedKey, true);
                SessionState.SetString(StartKey, DateTime.UtcNow.ToString("O"));
                RegisterCallbacks();
                if (!EditorApplication.ExecuteMenuItem("MMORPG/Maps/节庆地图离线预览"))
                    throw new InvalidOperationException("Offline preview menu could not be invoked.");
                if (SceneManager.GetActiveScene().path != FestivalMapPreviewMenu.ScenePath)
                    throw new InvalidOperationException("The preview menu opened an unexpected scene.");
                EditorApplication.delayCall += EnterPlayMode;
            }
            catch (Exception error) { Finish(false, error); }
        }

        private static void RegisterCallbacks()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= HandleLog;
            Application.logMessageReceived += HandleLog;
        }

        private static void EnterPlayMode()
        {
            try
            {
                Debug.Log("[FestivalVisualPlayVerification] Requesting Editor Play mode");
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(false, error); }
        }

        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                RuntimeErrors.Add(type + ": " + condition + "\n" + stackTrace);
        }

        private static System.Collections.Generic.IEnumerable<Scene> Scenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) yield return SceneManager.GetSceneAt(i);
        }

        private static void Tick()
        {
            try
            {
                DateTime started = DateTime.Parse(SessionState.GetString(StartKey, ""), null,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                if ((DateTime.UtcNow - started.ToUniversalTime()).TotalSeconds > 300)
                    throw new TimeoutException("Timed out waiting for Play mode and visual maps.");
                if (!EditorApplication.isPlaying) return;
                // In batch mode Editor updates are not evidence that the player
                // loop advanced. Request a loop and wait for actual Play frames.
                EditorApplication.QueuePlayerLoopUpdate();
                int frame = Time.frameCount;
                if (frame == _lastObservedFrame) return;
                _lastObservedFrame = frame;
                if (_report == null)
                {
                    _report = new Report {
                        startedUtc = started.ToUniversalTime().ToString("O"),
                        unityVersion = Application.unityVersion,
                        scene = SceneManager.GetActiveScene().path,
                        menuOpened = SceneManager.GetActiveScene().path == FestivalMapPreviewMenu.ScenePath,
                        playModeEntered = true
                    };
                }
                _report.playFramesObserved++;
                var sandbox = UnityEngine.Object.FindFirstObjectByType<TianyongSandboxBootstrap>();
                if (sandbox == null) return;
                // Let Start complete, but do not require unrelated legacy map resources
                // to load before starting the separate visual-only preview.
                if (!_started && sandbox.Map == null && _bootstrapWaitFrames++ < 10) return;
                if (!_started)
                {
                    FestivalMapVisualPreview.Open(sandbox, MapIds[0], Appearances[0]);
                    _preview = sandbox.GetComponent<FestivalMapVisualPreview>();
                    if (_preview == null || _preview.PreviewCamera == null)
                        throw new InvalidOperationException("Visual preview could not be opened.");
                    _started = true;
                    _waitFrames = 3;
                    return;
                }
                if (_waitFrames-- > 0) return;
                VerifyAndCapture(_index);
                _index++;
                if (_index == MapIds.Length)
                {
                    if (!_report.appearanceSwitchVerified)
                        throw new InvalidOperationException("Spring and Mid-Autumn textures did not switch.");
                    if (RuntimeErrors.Count != 0)
                        throw new InvalidOperationException("Unity reported errors during verification; see runtimeErrors.");
                    Finish(true, null);
                    return;
                }
                if (!_preview.Select(MapIds[_index], Appearances[_index]))
                    throw new InvalidOperationException("Unable to select map " + MapIds[_index] + "/" + Appearances[_index]);
                _waitFrames = 3; // allow Unity's delayed Destroy to remove the previous visual root
            }
            catch (Exception error) { Finish(false, error); }
        }

        private static void VerifyAndCapture(int index)
        {
            if (_preview.CurrentMapId != MapIds[index] || _preview.CurrentAppearance != Appearances[index])
                throw new InvalidOperationException("Selected map/appearance does not match the requested case.");
            int columns = ExpectedColumns[index], rows = ExpectedRows[index];
            if (_preview.Columns != columns || _preview.Rows != rows || _preview.LoadedTileCount != columns * rows)
                throw new InvalidOperationException("Unexpected grid or loaded tile count.");
            var root = _preview.VisualRoot;
            if (root == null) throw new InvalidOperationException("Visual tile root is missing.");
            var sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites.Length != columns * rows) throw new InvalidOperationException("Unexpected tile renderer count.");
            for (int row = 1; row <= rows; row++)
            for (int column = 1; column <= columns; column++)
            {
                string name = $"r{row:00}_c{column:00}";
                UnityEngine.Transform tile = root.transform.Find(name);
                if (tile == null) throw new InvalidOperationException("Missing tile " + name);
                var renderer = tile.GetComponent<SpriteRenderer>();
                if (renderer == null || renderer.sprite == null || renderer.sprite.texture == null ||
                    renderer.sprite.texture.width != 1024 || renderer.sprite.texture.height != 1024)
                    throw new InvalidOperationException("Bad image dimensions or renderer: " + name);
                CheckNear(tile.localPosition.x, column - .5f, name + " x");
                CheckNear(tile.localPosition.y, rows - row + .5f, name + " y");
                CheckNear(renderer.sprite.bounds.size.x, 1, name + " width");
                CheckNear(renderer.sprite.bounds.size.y, 1, name + " height");
                if (row == 1 && column == 1 && index == 1) _springTile = renderer.sprite.texture;
                if (row == 1 && column == 1 && index == 2)
                    _report.appearanceSwitchVerified = _springTile != null && renderer.sprite.texture != _springTile;
            }

            Camera camera = _preview.PreviewCamera;
            if (camera == null || !camera.orthographic || !camera.enabled)
                throw new InvalidOperationException("The Play mode preview camera is not active.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || !SystemInfo.supportsRenderTextures)
                throw new InvalidOperationException("A graphics device and render textures are required; do not pass -nographics.");
            camera.aspect = Width / (float)Height;
            Rect bounds = _preview.VisualBounds;
            if (bounds.width <= 0 || bounds.height <= 0 || bounds.xMin < -Tolerance || bounds.yMin < -Tolerance ||
                bounds.xMax > columns + Tolerance || bounds.yMax > rows + Tolerance)
                throw new InvalidOperationException("Invalid visual bounds.");
            float fit = Mathf.Max(bounds.height * .5f, bounds.width * .5f / camera.aspect);
            _preview.SetView(bounds.center, fit);
            CheckNear(_preview.Zoom, fit, "fit zoom");
            string stem = MapIds[index] + "-" + Appearances[index];
            string overview = stem + "-overview.png";
            Capture(camera, Path.Combine(OutputDirectory(), overview));

            float zoom = Mathf.Min(bounds.height * .24f, bounds.width * .24f / camera.aspect);
            if (zoom < .15f) throw new InvalidOperationException("Bounds too small for edge and zoom verification.");
            _preview.SetView(new Vector2(bounds.xMin - 100, bounds.yMin - 100), zoom);
            CheckNear(_preview.Zoom, zoom, "near zoom");
            CheckNear(_preview.Focus.x - zoom * camera.aspect, bounds.xMin, "left boundary");
            CheckNear(_preview.Focus.y - zoom, bounds.yMin, "bottom boundary");
            string lowerLeft = stem + "-lower-left.png";
            Capture(camera, Path.Combine(OutputDirectory(), lowerLeft));
            _preview.SetView(new Vector2(bounds.xMax + 100, bounds.yMax + 100), zoom);
            CheckNear(_preview.Focus.x + zoom * camera.aspect, bounds.xMax, "right boundary");
            CheckNear(_preview.Focus.y + zoom, bounds.yMax, "top boundary");
            string upperRight = stem + "-upper-right.png";
            Capture(camera, Path.Combine(OutputDirectory(), upperRight));
            _preview.SetView(bounds.center, fit);

            _report.maps[index] = new MapResult {
                mapId = MapIds[index], appearance = Appearances[index], columns = columns, rows = rows,
                loadedTiles = sprites.Length, validBoundsKnown = _preview.ValidBoundsKnown,
                visualBounds = new[] { bounds.x, bounds.y, bounds.width, bounds.height },
                overview = overview, lowerLeft = lowerLeft, upperRight = upperRight,
                coordinatesVerified = true, edgeClampVerified = true, zoomVerified = true
            };
            Debug.Log($"[FestivalVisualPlayVerification] PASS {stem}: {columns}x{rows}, {sprites.Length} tiles, edge clamp and zoom");
        }

        private static void Capture(Camera camera, string path)
        {
            var oldActive = RenderTexture.active;
            GameObject captureObject = null;
            Camera captureCamera = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                // Match the active preview camera while keeping the manual rendering
                // camera disabled, as required by Camera.Render's documented pattern.
                captureObject = new GameObject("[FestivalVisualCaptureCamera]");
                captureCamera = captureObject.AddComponent<Camera>();
                captureCamera.CopyFrom(camera);
                captureCamera.enabled = false;
                captureCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                if (!target.Create()) throw new InvalidOperationException("Unable to create capture render target.");
                captureCamera.targetTexture = target;
                captureCamera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                pixels.Apply(false, false);
                var data = pixels.GetPixels32();
                var first = data[0];
                bool varied = false;
                for (int i = 1; i < data.Length; i += 97)
                    if (Math.Abs(data[i].r - first.r) + Math.Abs(data[i].g - first.g) + Math.Abs(data[i].b - first.b) > 20)
                    { varied = true; break; }
                if (!varied) throw new InvalidOperationException("Rendered screenshot is blank: " + path);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                if (captureCamera != null) captureCamera.targetTexture = null;
                RenderTexture.active = oldActive;
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (captureObject != null) UnityEngine.Object.DestroyImmediate(captureObject);
            }
        }

        private static void CheckNear(float actual, float expected, string what)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > Tolerance)
                throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
        }

        private static string OutputDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-festivalPreviewCaptureDir");
            if (index >= 0 && index + 1 < args.Length)
            {
                string output = Path.GetFullPath(args[index + 1]);
                SessionState.SetString(OutputKey, output);
                return output;
            }
            string interactiveOutput = SessionState.GetString(OutputKey, "");
            if (string.IsNullOrEmpty(interactiveOutput))
                throw new ArgumentException("Provide -festivalPreviewCaptureDir <absolute output directory>.");
            return interactiveOutput;
        }

        private static void Finish(bool success, Exception error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= HandleLog;
            SessionState.EraseBool(ArmedKey);
            if (_report == null) _report = new Report { unityVersion = Application.unityVersion };
            _report.result = success ? "PASS" : "FAIL";
            _report.finishedUtc = DateTime.UtcNow.ToString("O");
            _report.error = error?.ToString();
            _report.runtimeErrors = RuntimeErrors.ToArray();
            try
            {
                Directory.CreateDirectory(OutputDirectory());
                File.WriteAllText(Path.Combine(OutputDirectory(), "play-verification.json"), JsonUtility.ToJson(_report, true));
            }
            catch (Exception reportError) { Debug.LogException(reportError); success = false; }
            if (error != null) Debug.LogException(error);
            Debug.Log("[FestivalVisualPlayVerification] " + (success ? "PASS" : "FAIL"));
            bool keepEditor = SessionState.GetBool(KeepEditorKey, false);
            SessionState.EraseBool(KeepEditorKey);
            if (keepEditor)
                EditorApplication.isPlaying = false;
            else
                EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
