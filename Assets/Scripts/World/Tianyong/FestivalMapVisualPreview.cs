using System;
using System.Collections.Generic;
using UnityEngine;
// BaseComp.cs 的 proto 生成物在全局命名空间声明了 Vector3 消息类，会遮蔽 UnityEngine.Vector3；
// 因此本文件的坐标构造统一写全限定名 UnityEngine.Vector3，不能用同名 using 别名（CS0576）。

namespace MmorpgClient.World.Tianyong
{
    /// <summary>Visual-only preview of externally verified runtime tiles. No navigation or online travel.</summary>
    public sealed class FestivalMapVisualPreview : MonoBehaviour
    {
        public const string CatalogPath = "World/FestivalPreview20261111/preview-catalog";
        private const int PreviewLayer = 30;

        [Serializable] private sealed class Catalog { public Map[] maps; }
        [Serializable] private sealed class PixelRect { public float x, y, width, height; }
        [Serializable] private sealed class Map
        {
            public uint mapId;
            public string appearance, resourceRoot;
            public int columns, rows, tilePixels;
            public bool validRectKnown;
            public PixelRect validRect;
        }

        private TianyongSandboxBootstrap _sandbox;
        private bool _sandboxWasEnabled;
        private TianyongPlayerController _previousPlayerController;
        private bool _playerControllerWasEnabled;
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<Sprite> _sprites = new List<Sprite>();
        private GameObject _visualRoot;
        private Camera _camera;
        private Map[] _maps;
        private Map _current;
        private Rect _bounds;
        private Vector2 _focus;
        private float _zoom;
        private string _status = "";
        public uint CurrentMapId => _current?.mapId ?? 0;
        public string CurrentAppearance => _current?.appearance;
        public int Columns => _current?.columns ?? 0;
        public int Rows => _current?.rows ?? 0;
        public int LoadedTileCount => _sprites.Count;
        public bool ValidBoundsKnown => _current != null && _current.validRectKnown;
        public Rect VisualBounds => _bounds;
        public Camera PreviewCamera => _camera;
        public GameObject VisualRoot => _visualRoot;
        public Vector2 Focus => _focus;
        public float Zoom => _zoom;
        public bool TryGetTilePosition(int row, int column, out Vector2 position)
        {
            position = default;
            if (_visualRoot == null || _current == null || row < 1 || row > _current.rows || column < 1 || column > _current.columns) return false;
            var tile = _visualRoot.transform.Find($"r{row:00}_c{column:00}");
            if (tile == null) return false;
            position = tile.localPosition;
            return true;
        }
        public void SetView(Vector2 focus, float zoom)
        {
            _focus = focus;
            _zoom = zoom;
            ApplyCamera();
        }

        public static void Open(TianyongSandboxBootstrap sandbox, uint mapId, string appearance)
        {
            if (sandbox == null) throw new ArgumentNullException(nameof(sandbox));
            var preview = sandbox.GetComponent<FestivalMapVisualPreview>();
            bool created = preview == null;
            if (created) preview = sandbox.gameObject.AddComponent<FestivalMapVisualPreview>();
            try { preview.Activate(sandbox, mapId, appearance); }
            catch
            {
                if (created && preview != null) Destroy(preview);
                throw;
            }
        }

        private void Activate(TianyongSandboxBootstrap sandbox, uint mapId, string appearance)
        {
            if (_sandbox != null)
            {
                if (!Select(mapId, appearance)) throw new InvalidOperationException(_status);
                return;
            }
            var json = Resources.Load<TextAsset>(CatalogPath);
            if (json == null) throw new InvalidOperationException("离线预览清单缺失：" + CatalogPath);
            _maps = JsonUtility.FromJson<Catalog>(json.text)?.maps;
            if (_maps == null || _maps.Length == 0) throw new InvalidOperationException("离线预览清单为空。");
            _sandbox = sandbox;
            _sandboxWasEnabled = sandbox.enabled;
            _previousPlayerController = sandbox.Player != null
                ? sandbox.Player.GetComponent<TianyongPlayerController>() : null;
            _playerControllerWasEnabled = _previousPlayerController != null && _previousPlayerController.enabled;
            try
            {
                _sandbox.enabled = false;
                if (_previousPlayerController != null) _previousPlayerController.enabled = false;
                foreach (var camera in Camera.allCameras)
                    if (camera != null && camera.enabled) { camera.enabled = false; _disabledCameras.Add(camera); }
                var cameraObject = new GameObject("[FestivalVisualPreviewCamera]");
                cameraObject.transform.SetParent(transform, false);
                _camera = cameraObject.AddComponent<Camera>();
                _camera.cullingMask = 1 << PreviewLayer;
                _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.16f, .17f, .17f);
                _camera.nearClipPlane = .1f;
                _camera.farClipPlane = 50f;
                _camera.transform.SetPositionAndRotation(new UnityEngine.Vector3(0, 0, -10), Quaternion.identity);
                if (!Select(mapId, appearance)) throw new InvalidOperationException(_status);
            }
            catch
            {
                RestoreState();
                throw;
            }
        }

        public bool Select(uint mapId, string appearance)
        {
            Map selected = null;
            foreach (var map in _maps)
                if (map.mapId == mapId && map.appearance == appearance) { selected = map; break; }
            if (selected == null) { _status = "未找到地图或外观"; return false; }
            if (selected.columns <= 0 || selected.rows <= 0 || selected.tilePixels != 1024 ||
                string.IsNullOrEmpty(selected.resourceRoot)) { _status = "预览元数据无效"; return false; }

            var candidate = new GameObject("[VisualMapCandidate]");
            candidate.transform.SetParent(transform, false);
            var sprites = new List<Sprite>();
            try
            {
                for (int row = 1; row <= selected.rows; row++)
                for (int col = 1; col <= selected.columns; col++)
                {
                    string path = selected.resourceRoot + $"r{row:00}_c{col:00}";
                    var texture = Resources.Load<Texture2D>(path);
                    if (texture == null || texture.width != 1024 || texture.height != 1024)
                        throw new InvalidOperationException("纹理缺失或尺寸错误：" + path);
                    var sprite = Sprite.Create(texture, new Rect(0, 0, 1024, 1024), new Vector2(.5f, .5f), 1024f);
                    sprites.Add(sprite);
                    var tile = new GameObject($"r{row:00}_c{col:00}");
                    tile.layer = PreviewLayer;
                    tile.transform.SetParent(candidate.transform, false);
                    tile.transform.localPosition = new UnityEngine.Vector3(col - .5f, selected.rows - row + .5f, 0);
                    tile.AddComponent<SpriteRenderer>().sprite = sprite;
                }
                Rect bounds = new Rect(0, 0, selected.columns, selected.rows);
                if (selected.validRectKnown)
                {
                    var r = selected.validRect;
                    if (r == null || r.width <= 0 || r.height <= 0 || r.x < 0 || r.y < 0 ||
                        r.x + r.width > selected.columns * 1024 || r.y + r.height > selected.rows * 1024)
                        throw new InvalidOperationException("已证实的内容边界无效。");
                    bounds = new Rect(r.x / 1024, selected.rows - (r.y + r.height) / 1024,
                        r.width / 1024, r.height / 1024);
                }
                ReleaseMap();
                _visualRoot = candidate;
                _sprites.AddRange(sprites);
                _current = selected;
                _bounds = bounds;
                _focus = bounds.center;
                Fit();
                _status = $"{mapId} {AppearanceName(appearance)} · {selected.columns}×{selected.rows} · {sprites.Count} 块" +
                    (selected.validRectKnown ? " · 已绑定有效边界" : " · 按方形图格显示；有效边界和原图比例映射待核");
                Debug.Log("[FestivalVisualPreview] " + _status);
                return true;
            }
            catch (Exception error)
            {
                Destroy(candidate);
                foreach (var sprite in sprites) if (sprite != null) Destroy(sprite);
                _status = "加载失败：" + error.Message;
                Debug.LogError("[FestivalVisualPreview] " + _status);
                return false;
            }
        }

        private void Fit()
        {
            if (_camera == null) return;
            _zoom = Mathf.Max(_bounds.height * .5f, _bounds.width * .5f / Mathf.Max(.01f, _camera.aspect));
            ApplyCamera();
        }

        private void ApplyCamera()
        {
            if (_camera == null) return;
            _zoom = Mathf.Max(.15f, _zoom);
            float hx = _zoom * Mathf.Max(.01f, _camera.aspect);
            float hy = _zoom;
            _focus.x = hx * 2 >= _bounds.width ? _bounds.center.x : Mathf.Clamp(_focus.x, _bounds.xMin + hx, _bounds.xMax - hx);
            _focus.y = hy * 2 >= _bounds.height ? _bounds.center.y : Mathf.Clamp(_focus.y, _bounds.yMin + hy, _bounds.yMax - hy);
            _camera.orthographicSize = _zoom;
            _camera.transform.position = new UnityEngine.Vector3(_focus.x, _focus.y, -10);
        }

        private void Update()
        {
            if (_current == null) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .001f) _zoom *= Mathf.Pow(.85f, wheel);
            var motion = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            _focus += motion * (_zoom * Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.Home)) { _focus = _bounds.center; Fit(); }
            ApplyCamera();
        }

        private void OnGUI()
        {
            if (_current == null) return;
            GUI.Box(new Rect(12, 12, 580, 160), "节庆地图 · 离线视觉预览（无导航与碰撞）");
            if (GUI.Button(new Rect(24, 40, 165, 32), "26000 中秋")) Select(26000, "mid-autumn");
            if (GUI.Button(new Rect(198, 40, 165, 32), "26008 春节")) Select(26008, "spring");
            if (GUI.Button(new Rect(372, 40, 165, 32), "26008 中秋")) Select(26008, "mid-autumn");
            GUI.Label(new Rect(24, 78, 550, 30), _status);
            GUI.Label(new Rect(24, 107, 450, 28), "方向键/WASD 平移 · 滚轮缩放 · Home 显示全图");
            if (GUI.Button(new Rect(450, 132, 120, 27), "返回旧地图")) Close();
        }

        private static string AppearanceName(string appearance) => appearance == "spring" ? "春节" : "中秋";

        private void ReleaseMap()
        {
            if (_visualRoot != null) Destroy(_visualRoot);
            _visualRoot = null;
            foreach (var sprite in _sprites) if (sprite != null) Destroy(sprite);
            _sprites.Clear();
        }

        public void Close()
        {
            RestoreState();
            Destroy(this);
        }

        private void RestoreState()
        {
            ReleaseMap();
            _current = null;
            if (_camera != null) Destroy(_camera.gameObject);
            _camera = null;
            foreach (var camera in _disabledCameras) if (camera != null) camera.enabled = true;
            _disabledCameras.Clear();
            if (_previousPlayerController != null) _previousPlayerController.enabled = _playerControllerWasEnabled;
            _previousPlayerController = null;
            if (_sandbox != null) _sandbox.enabled = _sandboxWasEnabled;
            _sandbox = null;
        }

        private void OnDestroy() => RestoreState();

    }
}
