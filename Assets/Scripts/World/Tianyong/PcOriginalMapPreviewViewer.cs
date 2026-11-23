#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using MmorpgClient.World.CurrentMaps;
using UnityEngine;
// BaseComp.cs 的 proto 生成物在全局命名空间声明了 Vector3 消息类，会遮蔽 UnityEngine.Vector3；
// 本文件统一使用全限定名 UnityEngine.Vector3。

namespace MmorpgClient.World.Tianyong
{
    /// <summary>
    /// Local viewer and switcher for the 36 audited PC original static `back` backgrounds.
    /// Streams native <=1024px tiles asynchronously over an aspect-preserving overview texture
    /// within a strict VRAM budget. Strictly local/offline viewing only; does not alter formal
    /// online travel or hardcode temporary original paths into gameplay systems.
    /// </summary>
    public sealed class PcOriginalMapPreviewViewer : MonoBehaviour
    {
        private const int PreviewLayer = 30;
        private const float TileReleaseDelaySec = 1.5f;
        private const float TileRetryDelaySec = 8.0f;

        [SerializeField] private bool standalone = true;
        [SerializeField] private string catalogResourcePath = PcOriginalMapContract.DefaultCatalogResourcePath;
        [SerializeField] private string initialMapId = "05000";
        [SerializeField] private int maxResidentTiles = PcOriginalMapContract.DefaultMaxResidentTiles;
        [SerializeField] private int maxConcurrentLoads = PcOriginalMapContract.DefaultMaxConcurrentLoads;

        private sealed class TileRuntime
        {
            public PcOriginalTileEntry Entry;
            public PcOriginalWorldRect WorldRect;
            public ResourceRequest PendingRequest;
            public Texture2D Texture;
            public Sprite Sprite;
            public GameObject TileObject;
            public bool Wanted;
            public float LastWantedTime;
            public float RetryAfterTime;
        }

        private TianyongSandboxBootstrap _sandbox;
        private bool _sandboxWasEnabled;
        private TianyongPlayerController _previousPlayerController;
        private bool _playerControllerWasEnabled;
        private readonly List<Camera> _disabledCameras = new List<Camera>();

        private PcOriginalMapCatalog _catalog;
        private PcOriginalMapEntry _currentMap;
        private PcOriginalTileManifest _currentManifest;
        private TileRuntime[] _tiles = Array.Empty<TileRuntime>();
        private readonly List<int> _visibleSelected = new List<int>();
        private readonly List<int> _evictionCandidates = new List<int>();

        private Camera _camera;
        private GameObject _visualRoot;
        private Texture2D _overviewTexture;
        private Sprite _overviewSprite;
        private GameObject _overviewObject;

        private Rect _bounds;
        private Vector2 _focus;
        private float _zoom = 2f;
        private bool _isDragging;
        private Vector2 _lastPointer;
        private int _mapGeneration;
        private int _activePendingLoads;
        private int _lastOverlappingTiles;
        private string _status = "";
        private string _searchFilter = "";
        private Vector2 _listScroll;
        private Vector2 _gapsScroll;
        private bool _sidebarVisible = true;
        private bool _showGapsModal;
        private bool _showControls = true;
        private Rect _sidebarGuiRect;
        private Rect _gapsGuiRect;

        public bool IsStandalone => standalone;
        public PcOriginalMapCatalog Catalog => _catalog;
        public PcOriginalMapEntry CurrentMap => _currentMap;
        public PcOriginalTileManifest CurrentTileManifest => _currentManifest;
        public Camera PreviewCamera => _camera;
        public GameObject VisualRoot => _visualRoot;
        public Rect VisualBounds => _bounds;
        public Vector2 Focus => _focus;
        public float Zoom => _zoom;
        public string LastStatus => _status;
        public bool OverviewLoaded => _overviewTexture != null;
        public int TotalTileCount => _tiles.Length;
        public int OverlappingTileCount => _lastOverlappingTiles;
        public int PendingNativeTileCount => _activePendingLoads;

        public int LoadedNativeTileCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _tiles.Length; i++)
                    if (_tiles[i].TileObject != null && _tiles[i].Texture != null) count++;
                return count;
            }
        }

        public static PcOriginalMapPreviewViewer Open(TianyongSandboxBootstrap sandbox, string mapId = "05000")
        {
            if (sandbox == null) throw new ArgumentNullException(nameof(sandbox));
            var existing = sandbox.GetComponent<PcOriginalMapPreviewViewer>();
            bool created = existing == null;
            if (created)
            {
                existing = sandbox.gameObject.AddComponent<PcOriginalMapPreviewViewer>();
                existing.standalone = false;
            }
            try
            {
                existing.Activate(sandbox, mapId);
                return existing;
            }
            catch
            {
                if (created && existing != null) Destroy(existing);
                throw;
            }
        }

        private void Start()
        {
            if (!standalone) return;
            try { Activate(null, initialMapId); }
            catch (Exception error)
            {
                _status = "初始化失败：" + error.Message;
                Debug.LogException(error, this);
            }
        }

        private void Activate(TianyongSandboxBootstrap sandbox, string mapId)
        {
            if (_camera != null)
            {
                if (!SelectByMapId(mapId)) throw new InvalidOperationException(_status);
                return;
            }

            string path = string.IsNullOrWhiteSpace(catalogResourcePath)
                ? PcOriginalMapContract.DefaultCatalogResourcePath
                : catalogResourcePath;
            var catalogAsset = Resources.Load<TextAsset>(path);
            if (catalogAsset == null)
                throw new InvalidOperationException("端游原图目录清单缺失：" + path);

            var parsed = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
            if (parsed == null)
                throw new InvalidOperationException("端游原图目录清单解析失败：" + path);
            parsed.Validate();
            _catalog = parsed;

            _sandbox = sandbox;
            _sandboxWasEnabled = sandbox != null && sandbox.enabled;
            _previousPlayerController = sandbox != null && sandbox.Player != null
                ? sandbox.Player.GetComponent<TianyongPlayerController>()
                : null;
            _playerControllerWasEnabled = _previousPlayerController != null && _previousPlayerController.enabled;

            try
            {
                if (_sandbox != null) _sandbox.enabled = false;
                if (_previousPlayerController != null) _previousPlayerController.enabled = false;
                foreach (var cam in Camera.allCameras)
                {
                    if (cam != null && cam.enabled)
                    {
                        cam.enabled = false;
                        _disabledCameras.Add(cam);
                    }
                }

                var cameraObject = new GameObject("[PcOriginalPreviewCamera]");
                cameraObject.transform.SetParent(transform, false);
                _camera = cameraObject.AddComponent<Camera>();
                _camera.cullingMask = 1 << PreviewLayer;
                _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.11f, 0.12f, 0.13f, 1f);
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = 50f;
                _camera.transform.SetPositionAndRotation(new UnityEngine.Vector3(0f, 0f, -10f), Quaternion.identity);

                string targetId = !string.IsNullOrEmpty(mapId) ? mapId : _catalog.maps[0].mapId;
                if (_catalog.FindByMapId(targetId) == null) targetId = _catalog.maps[0].mapId;
                if (!SelectByMapId(targetId))
                    throw new InvalidOperationException(_status);
            }
            catch
            {
                RestoreState();
                throw;
            }
        }

        public bool SelectByMapId(string mapId)
        {
            if (_catalog == null)
            {
                _status = "目录尚未初始化";
                return false;
            }
            var entry = _catalog.FindByMapId(mapId);
            if (entry == null)
            {
                _status = $"未找到已就绪地图 {mapId}（缺口或 HOLD 项不可直接切换）";
                return false;
            }
            return Select(entry);
        }

        public bool SelectByIndex(int index)
        {
            if (_catalog == null || _catalog.maps == null || index < 0 || index >= _catalog.maps.Length)
                return false;
            return Select(_catalog.maps[index]);
        }

        public bool SelectRelative(int delta)
        {
            if (_catalog == null || _catalog.maps == null || _catalog.maps.Length == 0) return false;
            int current = 0;
            if (_currentMap != null)
            {
                for (int i = 0; i < _catalog.maps.Length; i++)
                    if (_catalog.maps[i].mapId == _currentMap.mapId) { current = i; break; }
            }
            int next = (current + delta) % _catalog.maps.Length;
            if (next < 0) next += _catalog.maps.Length;
            return Select(_catalog.maps[next]);
        }

        public void SetView(Vector2 focus, float zoom)
        {
            _focus = focus;
            _zoom = zoom;
            ApplyCamera();
            UpdateTileStreaming(Time.unscaledTime);
        }

        public void FitFullMap()
        {
            if (_camera == null || _currentMap == null) return;
            _focus = _bounds.center;
            float aspect = Mathf.Max(0.01f, _camera.aspect);
            _zoom = Mathf.Max(_bounds.height * 0.5f, _bounds.width * 0.5f / aspect);
            ApplyCamera();
        }

        public void SetNativeOneToOneZoom()
        {
            if (_camera == null || _currentMap == null) return;
            float pixelHeight = Mathf.Max(1f, _camera.pixelHeight > 0 ? _camera.pixelHeight : Screen.height);
            _zoom = pixelHeight / (2f * PcOriginalMapContract.PixelsPerUnit);
            ApplyCamera();
        }

        public void SetControlsVisible(bool visible) => _showControls = visible;

        private bool Select(PcOriginalMapEntry selected)
        {
            if (_camera == null || selected == null) return false;

            GameObject candidateRoot = null;
            Texture2D candidateOverview = null;
            Sprite candidateOverviewSprite = null;
            try
            {
                selected.Validate();
                var tilesAsset = Resources.Load<TextAsset>(selected.tilesManifestResourcePath);
                if (tilesAsset == null)
                    throw new InvalidOperationException("分片清单缺失：" + selected.tilesManifestResourcePath);

                var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(tilesAsset.text);
                if (manifest == null)
                    throw new InvalidOperationException("分片清单解析失败：" + selected.tilesManifestResourcePath);
                manifest.Validate(selected);

                candidateOverview = Resources.Load<Texture2D>(selected.overviewResourcePath);
                if (candidateOverview == null)
                    throw new InvalidOperationException("总览底图缺失：" + selected.overviewResourcePath);

                candidateRoot = new GameObject($"[PcOriginalMap:{selected.mapId}]");
                candidateRoot.transform.SetParent(transform, false);

                float worldW = selected.WorldWidth;
                float worldH = selected.WorldHeight;
                float ovPpuX = candidateOverview.width / worldW;
                candidateOverviewSprite = Sprite.Create(
                    candidateOverview,
                    new Rect(0f, 0f, candidateOverview.width, candidateOverview.height),
                    new Vector2(0.5f, 0.5f),
                    ovPpuX,
                    0u,
                    SpriteMeshType.FullRect);

                var ovGo = new GameObject("OverviewBase");
                ovGo.layer = PreviewLayer;
                ovGo.transform.SetParent(candidateRoot.transform, false);
                ovGo.transform.localPosition = new UnityEngine.Vector3(worldW * 0.5f, worldH * 0.5f, 0.1f);
                // Keep exact native aspect ratio even when overview rounding differs by 0.5px.
                float spriteH = candidateOverview.height / ovPpuX;
                float scaleY = spriteH > 0.0001f ? worldH / spriteH : 1f;
                ovGo.transform.localScale = new UnityEngine.Vector3(1f, scaleY, 1f);
                var ovRenderer = ovGo.AddComponent<SpriteRenderer>();
                ovRenderer.sprite = candidateOverviewSprite;
                ovRenderer.sortingOrder = 0;

                var tileRuntimes = new TileRuntime[manifest.tiles.Length];
                for (int i = 0; i < manifest.tiles.Length; i++)
                {
                    tileRuntimes[i] = new TileRuntime
                    {
                        Entry = manifest.tiles[i],
                        WorldRect = PcOriginalViewportPlanner.TileWorldRect(manifest.nativeHeight, manifest.tiles[i])
                    };
                }

                // Commit switch cleanly after all validation & base texture checks succeed.
                ReleaseCurrentMap();
                _mapGeneration++;
                _currentMap = selected;
                _currentManifest = manifest;
                _visualRoot = candidateRoot;
                _overviewTexture = candidateOverview;
                _overviewSprite = candidateOverviewSprite;
                _overviewObject = ovGo;
                _tiles = tileRuntimes;
                _bounds = new Rect(0f, 0f, worldW, worldH);
                _focus = _bounds.center;
                FitFullMap();
                UpdateTileStreaming(Time.unscaledTime);

                _status = $"{selected.mapId} {selected.DisplayName} · {selected.nativeWidth}×{selected.nativeHeight} " +
                          $"({selected.aspectRatio:F3}:1) · {selected.columns}×{selected.rows}={selected.tileCount} 块 · " +
                          $"{PcOriginalMapContract.ArtStageDisplayLabel(selected.artStage)}";
                Debug.Log($"[PcOriginalMapPreview] selected={selected.mapId} pixels={selected.nativeWidth}x{selected.nativeHeight} " +
                          $"grid={selected.columns}x{selected.rows} stage={selected.artStage} root={selected.resourceRoot}");
                return true;
            }
            catch (Exception error)
            {
                if (candidateRoot != null) DestroyOwned(candidateRoot);
                if (candidateOverviewSprite != null) DestroyOwned(candidateOverviewSprite);
                if (candidateOverview != null) Resources.UnloadAsset(candidateOverview);
                _status = "切图失败：" + error.Message;
                Debug.LogError("[PcOriginalMapPreview] " + _status);
                return false;
            }
        }

        private void ApplyCamera()
        {
            if (_camera == null || _currentMap == null) return;
            float aspect = Mathf.Max(0.01f, _camera.aspect);
            float maxZoom = Mathf.Max(_bounds.height * 0.65f, _bounds.width * 0.65f / aspect);
            _zoom = Mathf.Clamp(_zoom, 0.12f, Mathf.Max(0.5f, maxZoom));
            float hx = _zoom * aspect;
            float hy = _zoom;
            _focus.x = hx * 2f >= _bounds.width
                ? _bounds.center.x
                : Mathf.Clamp(_focus.x, _bounds.xMin + hx, _bounds.xMax - hx);
            _focus.y = hy * 2f >= _bounds.height
                ? _bounds.center.y
                : Mathf.Clamp(_focus.y, _bounds.yMin + hy, _bounds.yMax - hy);
            _camera.orthographicSize = _zoom;
            _camera.transform.position = new UnityEngine.Vector3(_focus.x, _focus.y, -10f);
        }

        private void Update()
        {
            if (_currentMap == null || _camera == null) return;

            bool overGui = IsPointerOverUi();
            if (!overGui)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.001f)
                    _zoom *= Mathf.Pow(0.85f, wheel);
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2))
            {
                if (!overGui)
                {
                    _isDragging = true;
                    _lastPointer = Input.mousePosition;
                }
            }
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(2))
                _isDragging = false;

            if (_isDragging)
            {
                Vector2 pointer = Input.mousePosition;
                float pixelHeight = Mathf.Max(1f, _camera.pixelHeight > 0 ? _camera.pixelHeight : Screen.height);
                _focus -= (pointer - _lastPointer) * (2f * _zoom / pixelHeight);
                _lastPointer = pointer;
            }

            // Skip keyboard panning while typing in the search field.
            if (string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
            {
                var motion = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                if (motion.sqrMagnitude > 0.0001f)
                    _focus += motion * (_zoom * 1.25f * Time.unscaledDeltaTime);

                if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.F)) FitFullMap();
                else if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetNativeOneToOneZoom();
                else if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.LeftBracket)) SelectRelative(-1);
                else if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.RightBracket)) SelectRelative(1);
                else if (Input.GetKeyDown(KeyCode.Tab)) _sidebarVisible = !_sidebarVisible;
            }

            ApplyCamera();
            UpdateTileStreaming(Time.unscaledTime);
        }

        private bool IsPointerOverUi()
        {
            if (!_showControls) return false;
            Vector2 guiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_sidebarVisible && _sidebarGuiRect.Contains(guiPos)) return true;
            if (_showGapsModal && _gapsGuiRect.Contains(guiPos)) return true;
            return false;
        }

        private void UpdateTileStreaming(float now)
        {
            if (_currentManifest == null || _camera == null || _visualRoot == null) return;

            float aspect = Mathf.Max(0.01f, _camera.aspect);
            float hy = _camera.orthographicSize;
            float hx = hy * aspect;
            // Small margin so tiles entering from screen edges warm smoothly.
            float margin = 0.25f;
            var viewRect = new PcOriginalWorldRect(
                _focus.x - hx - margin,
                _focus.y - hy - margin,
                (hx + margin) * 2f,
                (hy + margin) * 2f);

            int budget = Mathf.Max(1, maxResidentTiles);
            int maxPending = Mathf.Max(1, maxConcurrentLoads);

            PcOriginalViewportPlanner.SelectVisibleTiles(
                _currentManifest, viewRect, budget, _visibleSelected, out _lastOverlappingTiles);

            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i].Wanted = false;

            for (int i = 0; i < _visibleSelected.Count; i++)
            {
                var tile = _tiles[_visibleSelected[i]];
                tile.Wanted = true;
                tile.LastWantedTime = now;
            }

            // Release tiles that have left the viewport past the grace window.
            for (int i = 0; i < _tiles.Length; i++)
            {
                var tile = _tiles[i];
                if (!tile.Wanted && tile.TileObject != null && now - tile.LastWantedTime >= TileReleaseDelaySec)
                    ReleaseTileVisual(tile);
            }

            // Enforce hard resident ceiling before issuing new loads.
            int residentCount = CountResidentAndPending();
            if (residentCount >= budget)
            {
                _evictionCandidates.Clear();
                for (int i = 0; i < _tiles.Length; i++)
                    if (!_tiles[i].Wanted && _tiles[i].TileObject != null) _evictionCandidates.Add(i);

                _evictionCandidates.Sort((a, b) => _tiles[a].LastWantedTime.CompareTo(_tiles[b].LastWantedTime));
                for (int i = 0; i < _evictionCandidates.Count && residentCount >= budget; i++)
                {
                    ReleaseTileVisual(_tiles[_evictionCandidates[i]]);
                    residentCount--;
                }
            }

            // Issue async main-thread Resources loads for needed visible tiles.
            for (int i = 0; i < _visibleSelected.Count; i++)
            {
                if (_activePendingLoads >= maxPending || residentCount >= budget) break;
                var tile = _tiles[_visibleSelected[i]];
                if (tile.TileObject != null || tile.PendingRequest != null || tile.RetryAfterTime > now) continue;

                int capturedGen = _mapGeneration;
                var req = Resources.LoadAsync<Texture2D>(tile.Entry.resourcePath);
                tile.PendingRequest = req;
                _activePendingLoads++;
                residentCount++;
                req.completed += _ => HandleTileLoadCompleted(capturedGen, tile, req);
            }
        }

        private int CountResidentAndPending()
        {
            int count = 0;
            for (int i = 0; i < _tiles.Length; i++)
                if (_tiles[i].TileObject != null || _tiles[i].PendingRequest != null) count++;
            return count;
        }

        private void HandleTileLoadCompleted(int generation, TileRuntime tile, ResourceRequest req)
        {
            var tex = req.asset as Texture2D;
            if (tile.PendingRequest == req)
                tile.PendingRequest = null;

            if (generation == _mapGeneration && _activePendingLoads > 0)
                _activePendingLoads--;

            // Map was switched, viewer was destroyed, or tile scrolled out of view while loading.
            if (this == null || generation != _mapGeneration || _visualRoot == null || !tile.Wanted)
            {
                if (tex != null) Resources.UnloadAsset(tex);
                return;
            }

            if (tex == null || tex.width != tile.Entry.width || tex.height != tile.Entry.height)
            {
                Debug.LogError(
                    $"[PcOriginalMapPreview] tile load mismatch: {tile.Entry.resourcePath} " +
                    $"expected={tile.Entry.width}x{tile.Entry.height} actual={(tex != null ? $"{tex.width}x{tex.height}" : "null")}");
                if (tex != null) Resources.UnloadAsset(tex);
                tile.RetryAfterTime = Time.unscaledTime + TileRetryDelaySec;
                return;
            }

            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                PcOriginalMapContract.PixelsPerUnit,
                0u,
                SpriteMeshType.FullRect);

            var go = new GameObject($"Tile_r{tile.Entry.row:00}_c{tile.Entry.column:00}");
            go.layer = PreviewLayer;
            go.transform.SetParent(_visualRoot.transform, false);
            go.transform.localPosition = new UnityEngine.Vector3(tile.WorldRect.CenterX, tile.WorldRect.CenterY, 0f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 1;

            tile.Texture = tex;
            tile.Sprite = sprite;
            tile.TileObject = go;
        }

        private static void ReleaseTileVisual(TileRuntime tile)
        {
            if (tile.TileObject != null)
            {
                tile.TileObject.SetActive(false);
                DestroyOwned(tile.TileObject);
                tile.TileObject = null;
            }
            if (tile.Sprite != null)
            {
                DestroyOwned(tile.Sprite);
                tile.Sprite = null;
            }
            if (tile.Texture != null)
            {
                Resources.UnloadAsset(tile.Texture);
                tile.Texture = null;
            }
        }

        private void ReleaseCurrentMap()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].Wanted = false;
                ReleaseTileVisual(_tiles[i]);
                _tiles[i].PendingRequest = null;
            }
            _tiles = Array.Empty<TileRuntime>();
            _activePendingLoads = 0;
            _lastOverlappingTiles = 0;

            if (_visualRoot != null)
            {
                _visualRoot.SetActive(false);
                DestroyOwned(_visualRoot);
                _visualRoot = null;
            }
            _overviewObject = null;
            if (_overviewSprite != null)
            {
                DestroyOwned(_overviewSprite);
                _overviewSprite = null;
            }
            if (_overviewTexture != null)
            {
                Resources.UnloadAsset(_overviewTexture);
                _overviewTexture = null;
            }
            _currentMap = null;
            _currentManifest = null;
        }

        private void OnGUI()
        {
            if (!_showControls || _catalog == null) return;

            if (!_sidebarVisible)
            {
                _sidebarGuiRect = new Rect(12f, 12f, 220f, 36f);
                if (GUI.Button(_sidebarGuiRect, "展开端游原图面板 (Tab)"))
                    _sidebarVisible = true;
                return;
            }

            float panelWidth = Mathf.Min(360f, Screen.width - 24f);
            float panelHeight = Mathf.Max(320f, Screen.height - 24f);
            _sidebarGuiRect = new Rect(12f, 12f, panelWidth, panelHeight);

            GUI.Box(_sidebarGuiRect, $"端游原图本地预览 ({_catalog.readyCount} 张就绪 / {_catalog.unavailableCount} 缺口)");

            float x = 22f;
            float w = panelWidth - 20f;
            float y = 38f;

            // Current map header
            if (_currentMap != null)
            {
                GUI.Label(new Rect(x, y, w, 22f),
                    $"当前：{_currentMap.mapId} {_currentMap.DisplayName}（{_currentMap.nativeWidth}×{_currentMap.nativeHeight}）");
                y += 22f;
                GUI.Label(new Rect(x, y, w, 20f),
                    $"阶段：{PcOriginalMapContract.ArtStageDisplayLabel(_currentMap.artStage)} | 分片：{LoadedNativeTileCount}/{_lastOverlappingTiles}（上限{maxResidentTiles}）");
                y += 20f;
                GUI.Label(new Rect(x, y, w, 20f),
                    $"服务端：未登记（仅本地静态back，无碰撞/导航）");
                y += 22f;
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 24f), _status);
                y += 26f;
            }

            // Action row 1: Prev / Next / Fit / 1:1
            float btnW = (w - 18f) * 0.25f;
            if (GUI.Button(new Rect(x, y, btnW, 28f), "上一张 [")) SelectRelative(-1);
            if (GUI.Button(new Rect(x + btnW + 6f, y, btnW, 28f), "下一张 ]")) SelectRelative(1);
            if (GUI.Button(new Rect(x + (btnW + 6f) * 2f, y, btnW, 28f), "全图 F")) FitFullMap();
            if (GUI.Button(new Rect(x + (btnW + 6f) * 3f, y, btnW, 28f), "1:1 像素")) SetNativeOneToOneZoom();
            y += 34f;

            // Action row 2: Gaps/HOLD info, Collapse, Close
            float halfW = (w - 6f) * 0.5f;
            if (GUI.Button(new Rect(x, y, halfW, 26f),
                _showGapsModal ? "隐藏缺口/HOLD" : $"缺口/HOLD ({_catalog.unavailableCount}+{_catalog.specialHoldCount})"))
                _showGapsModal = !_showGapsModal;
            if (standalone)
            {
                if (GUI.Button(new Rect(x + halfW + 6f, y, halfW, 26f), "收起面板 (Tab)"))
                    _sidebarVisible = false;
            }
            else
            {
                if (GUI.Button(new Rect(x + halfW + 6f, y, halfW, 26f), "返回节庆预览"))
                {
                    Close();
                    return;
                }
            }
            y += 32f;

            // Search box
            GUI.Label(new Rect(x, y + 2f, 44f, 22f), "筛选：");
            GUI.SetNextControlName("PcOriginalMapSearch");
            _searchFilter = GUI.TextField(new Rect(x + 44f, y, w - 98f, 24f), _searchFilter ?? "");
            if (GUI.Button(new Rect(x + w - 48f, y, 48f, 24f), "清空"))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            y += 30f;

            // Scrollable list of the 36 ready maps
            float footerHeight = 46f;
            float listHeight = Mathf.Max(80f, panelHeight - (y - 12f) - footerHeight);
            string filter = (_searchFilter ?? "").Trim();

            var filtered = new List<PcOriginalMapEntry>(_catalog.maps.Length);
            for (int i = 0; i < _catalog.maps.Length; i++)
            {
                var m = _catalog.maps[i];
                if (string.IsNullOrEmpty(filter) ||
                    m.mapId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(m);
            }

            float itemHeight = 32f;
            var viewArea = new Rect(x, y, w, listHeight);
            var contentRect = new Rect(0f, 0f, w - 18f, Mathf.Max(listHeight, filtered.Count * (itemHeight + 4f)));
            _listScroll = GUI.BeginScrollView(viewArea, _listScroll, contentRect);
            for (int i = 0; i < filtered.Count; i++)
            {
                var m = filtered[i];
                bool isCurrent = _currentMap != null && m.mapId == _currentMap.mapId;
                string tag = m.artStage == PcOriginalMapContract.ArtStagePcOriginal ? "原图" : "Q版";
                string alphaTag = m.hasAlpha ? " RGBA" : "";
                string prefix = isCurrent ? "▶ " : "";
                string label = $"{prefix}{m.mapId} {m.DisplayName}  [{m.nativeWidth}×{m.nativeHeight} {tag}{alphaTag}]";
                if (GUI.Button(new Rect(0f, i * (itemHeight + 4f), contentRect.width, itemHeight), label))
                {
                    GUI.FocusControl(null);
                    Select(m);
                }
            }
            GUI.EndScrollView();

            float footerY = _sidebarGuiRect.yMax - footerHeight + 4f;
            GUI.Label(new Rect(x, footerY, w, 40f),
                "左/中键拖拽或WASD平移 · 滚轮缩放 · 1键1:1像素\n本视图仅加载静态back底图，后续可按mapId替换为道家Q版");

            if (_showGapsModal) DrawGapsAndHoldPanel(panelWidth + 24f);
        }

        private void DrawGapsAndHoldPanel(float left)
        {
            float width = Mathf.Min(540f, Screen.width - left - 12f);
            if (width < 260f) { left = 12f; width = Screen.width - 24f; }
            float height = Mathf.Min(460f, Screen.height - 24f);
            _gapsGuiRect = new Rect(left, 12f, width, height);
            GUI.Box(_gapsGuiRect, "未接入缺口 ID（4）与特殊 HOLD 备选（2）— 只读说明");

            float innerX = left + 12f;
            float innerW = width - 24f;
            var outer = new Rect(innerX, 40f, innerW, height - 52f);
            float contentH = 60f + (_catalog.unavailableMaps.Length * 58f) + (_catalog.specialHoldCandidates.Length * 76f);
            _gapsScroll = GUI.BeginScrollView(outer, _gapsScroll, new Rect(0f, 0f, innerW - 18f, contentH));

            float y = 4f;
            GUI.Label(new Rect(0f, y, innerW - 20f, 22f), "【4 个缺口 ID（不可切换，未标记为已完成接入）】");
            y += 24f;
            for (int i = 0; i < _catalog.unavailableMaps.Length; i++)
            {
                var g = _catalog.unavailableMaps[i];
                GUI.Box(new Rect(0f, y, innerW - 22f, 52f), "");
                GUI.Label(new Rect(8f, y + 4f, innerW - 36f, 20f), $"{g.mapId}  |  状态：{g.reasonCode}");
                GUI.Label(new Rect(8f, y + 24f, innerW - 36f, 24f), g.detail);
                y += 58f;
            }

            y += 8f;
            GUI.Label(new Rect(0f, y, innerW - 20f, 22f), "【2 个特殊 HOLD 备选（暂不作为默认运行版）】");
            y += 24f;
            for (int i = 0; i < _catalog.specialHoldCandidates.Length; i++)
            {
                var c = _catalog.specialHoldCandidates[i];
                GUI.Box(new Rect(0f, y, innerW - 22f, 70f), "");
                GUI.Label(new Rect(8f, y + 4f, innerW - 36f, 20f),
                    $"{c.mapId} ({c.branch})  {c.nativeWidth}×{c.nativeHeight}  |  {c.sourceStatus}");
                GUI.Label(new Rect(8f, y + 24f, innerW - 36f, 42f), c.reason);
                y += 76f;
            }

            GUI.EndScrollView();
        }

        public void Close()
        {
            RestoreState();
            Destroy(this);
        }

        private void RestoreState()
        {
            _mapGeneration++;
            ReleaseCurrentMap();
            if (_camera != null) DestroyOwned(_camera.gameObject);
            _camera = null;
            for (int i = 0; i < _disabledCameras.Count; i++)
                if (_disabledCameras[i] != null) _disabledCameras[i].enabled = true;
            _disabledCameras.Clear();
            if (_previousPlayerController != null)
                _previousPlayerController.enabled = _playerControllerWasEnabled;
            _previousPlayerController = null;
            if (_sandbox != null)
                _sandbox.enabled = _sandboxWasEnabled;
            _sandbox = null;
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void OnDestroy() => RestoreState();
    }
}
#endif
