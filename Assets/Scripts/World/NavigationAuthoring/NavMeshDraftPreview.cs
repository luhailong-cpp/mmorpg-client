using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UVector3 = UnityEngine.Vector3;

namespace MmorpgClient.World.NavigationAuthoring
{
    /// <summary>Runs a user-authored Unity NavMesh in an isolated offline preview scene.</summary>
    [DisallowMultipleComponent]
    public sealed class NavMeshDraftPreview : MonoBehaviour
    {
        public NavMeshAuthoringDraft draft;
        private NavMeshAuthoringDraft _runtimeDraft;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<UVector3> _path = new List<UVector3>();
        private GameObject _root;
        private Camera _camera;
        private NavMeshAgent _agent;
        private LineRenderer _line;
        private NavMeshDataInstance _ownedData;
        private NavMeshQueryFilter _filter;
        private Vector2 _focus;
        private float _zoom;
        private float _cameraHeight;
        private float _speed;
        private float _markerRadius;
        private string _status = "等待导航草稿";

        public bool IsReady { get; private set; }
        public string Status => _status;
        public Vector2 PlayerWorld => _agent != null ? new Vector2(_agent.nextPosition.x, _agent.nextPosition.z) : default;
        public Camera PreviewCamera => _camera;
        public IReadOnlyList<UVector3> CurrentPath => _path;

        private void Start()
        {
            try
            {
                if (draft == null) throw new InvalidOperationException("请先选择导航草稿。");
                // Keep authoring edits made during Play from mutating the geometry,
                // bounds, or agent settings of this running preview.
                _runtimeDraft = Instantiate(draft);
                _runtimeDraft.name = draft.name + " (runtime snapshot)";
                _runtimeDraft.Validate(true);
                if (_runtimeDraft.bakedData == null) throw new InvalidOperationException("请先烘焙此草稿的 Unity NavMesh。");
                // Unity NavMesh queries span all loaded instances. A clean preview
                // scene prevents an unrelated NavMesh from satisfying these checks.
                var existing = NavMesh.CalculateTriangulation();
                if (existing.vertices != null && existing.vertices.Length != 0)
                    throw new InvalidOperationException("请在不含其他 NavMesh 的独立预览场景中运行。");
                _filter = new NavMeshQueryFilter { agentTypeID = _runtimeDraft.agentTypeId, areaMask = NavMesh.AllAreas };
                _ownedData = NavMesh.AddNavMeshData(_runtimeDraft.bakedData);
                if (!_ownedData.valid) throw new InvalidOperationException("无法加载草稿的 NavMesh 数据。");
                Build();
                IsReady = true;
                _status = "离线 NavMesh 预览：WASD 移动，左键寻路，方向键平移，滚轮缩放";
            }
            catch (Exception error)
            {
                IsReady = false;
                _status = "无法预览：" + error.Message;
                Debug.LogWarning("[NavMeshDraftPreview] " + _status);
                ReleasePreview();
            }
        }

        private void Build()
        {
            _root = new GameObject("[NavMeshDraftPreview]");
            BuildBackground();
            BuildAgent();
            BuildCamera();
            BuildPathLine();
        }

        private void BuildBackground()
        {
            var tiles = _runtimeDraft.backgroundTiles;
            if (tiles == null || tiles.Length == 0 || _runtimeDraft.imageWidth <= 0 || _runtimeDraft.imageHeight <= 0)
                throw new InvalidOperationException("缺少地图图块或图像尺寸。");
            int maxColumn = 0, maxRow = 0;
            foreach (var tile in tiles)
            {
                if (tile == null || tile.texture == null || tile.row < 1 || tile.column < 1 ||
                    tile.pixelWidth <= 0 || tile.pixelHeight <= 0)
                    throw new InvalidOperationException("图块记录或纹理尺寸无效。");
                maxColumn = Mathf.Max(maxColumn, tile.column);
                maxRow = Mathf.Max(maxRow, tile.row);
            }
            if ((long)maxColumn * maxRow != tiles.Length)
                throw new InvalidOperationException("地图图块不是完整矩形网格。");
            var widths = new int[maxColumn];
            var heights = new int[maxRow];
            var seen = new bool[maxRow, maxColumn];
            foreach (var tile in tiles)
            {
                int column = tile.column - 1, row = tile.row - 1;
                if (seen[row, column]) throw new InvalidOperationException("地图图块坐标重复。");
                seen[row, column] = true;
                if ((widths[column] != 0 && widths[column] != tile.pixelWidth) ||
                    (heights[row] != 0 && heights[row] != tile.pixelHeight))
                    throw new InvalidOperationException("同列图块宽度或同行图块高度不一致。");
                widths[column] = tile.pixelWidth;
                heights[row] = tile.pixelHeight;
            }
            var xs = new int[maxColumn + 1];
            var ys = new int[maxRow + 1];
            for (int i = 0; i < maxColumn; i++) xs[i + 1] = checked(xs[i] + widths[i]);
            for (int i = 0; i < maxRow; i++) ys[i + 1] = checked(ys[i] + heights[i]);
            if (xs[maxColumn] != _runtimeDraft.imageWidth || ys[maxRow] != _runtimeDraft.imageHeight)
                throw new InvalidOperationException("图块像素总尺寸与地图尺寸不一致。");
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("缺少 Unlit/Texture 着色器。");
            Rect world = _runtimeDraft.worldRect;
            foreach (var tile in tiles)
            {
                int column = tile.column - 1, row = tile.row - 1;
                float west = world.xMin + world.width * xs[column] / _runtimeDraft.imageWidth;
                float east = world.xMin + world.width * xs[column + 1] / _runtimeDraft.imageWidth;
                float north = world.yMax - world.height * ys[row] / _runtimeDraft.imageHeight;
                float south = world.yMax - world.height * ys[row + 1] / _runtimeDraft.imageHeight;
                var mesh = new Mesh { name = $"NavDraftTile_{tile.row:00}_{tile.column:00}" };
                _meshes.Add(mesh);
                mesh.vertices = new[] { new UVector3(west, 0, south), new UVector3(west, 0, north),
                    new UVector3(east, 0, north), new UVector3(east, 0, south) };
                mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var material = new Material(shader) { name = mesh.name + "_material", mainTexture = tile.texture };
                _materials.Add(material);
                var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(_root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private void BuildAgent()
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("缺少 Unlit/Color 着色器。");
            float tolerance = SnapTolerance();
            if (!TrySample(_runtimeDraft.spawn, out UVector3 spawnPosition))
                throw new InvalidOperationException("出生点不在该 NavMesh 上。");

            var agentObject = new GameObject("[NavMeshDraftAgent]");
            // Configure the intended agent type before Unity can bind this
            // component to a potentially different default NavMesh type.
            agentObject.SetActive(false);
            agentObject.transform.SetParent(_root.transform, false);
            agentObject.transform.position = spawnPosition;
            _agent = agentObject.AddComponent<NavMeshAgent>();
            _agent.enabled = false;
            _agent.agentTypeID = _runtimeDraft.agentTypeId;
            _agent.radius = _runtimeDraft.agentRadius;
            _agent.height = _runtimeDraft.agentHeight;
            _agent.baseOffset = 0f;
            _agent.updateRotation = false;
            _agent.autoBraking = true;
            _speed = Mathf.Max(_runtimeDraft.agentRadius * 4f, Mathf.Min(_runtimeDraft.worldRect.width, _runtimeDraft.worldRect.height) * .15f);
            _agent.speed = _speed;
            _agent.acceleration = _speed * 8f;
            _agent.stoppingDistance = Mathf.Max(.01f, tolerance);
            _agent.enabled = true;
            agentObject.SetActive(true);
            if (!_agent.Warp(spawnPosition) || !_agent.isOnNavMesh)
                throw new InvalidOperationException("角色无法绑定到该 NavMesh；请核对代理类型与出生点。");

            var material = new Material(shader) { name = "NavDraftPlayer", color = new Color(1f, .24f, .08f, 1f) };
            _materials.Add(material);
            _markerRadius = Mathf.Max(_runtimeDraft.agentRadius, Mathf.Min(_runtimeDraft.worldRect.width, _runtimeDraft.worldRect.height) * .004f);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "[NavMeshAgentMarker]";
            marker.transform.SetParent(agentObject.transform, false);
            marker.transform.localPosition = UVector3.up * (_markerRadius + .02f);
            marker.transform.localScale = UVector3.one * (_markerRadius * 2f);
            var collider = marker.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Destroy(collider); }
            marker.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void BuildCamera()
        {
            var go = new GameObject("[NavMeshDraftCamera]", typeof(Camera));
            go.transform.SetParent(_root.transform, false);
            _camera = go.GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(.12f, .14f, .14f);
            _camera.nearClipPlane = .01f;
            _cameraHeight = Mathf.Max(_runtimeDraft.worldRect.width, _runtimeDraft.worldRect.height) * 2f + 10f;
            _camera.farClipPlane = _cameraHeight * 2f + 100f;
            _camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            FitView();
        }

        private void BuildPathLine()
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("缺少路径线着色器。");
            var material = new Material(shader) { name = "NavDraftPath", color = new Color(1f, .55f, .04f, 1f) };
            _materials.Add(material);
            var go = new GameObject("[NavMeshDraftPath]", typeof(LineRenderer));
            go.transform.SetParent(_root.transform, false);
            _line = go.GetComponent<LineRenderer>();
            _line.sharedMaterial = material;
            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.View;
            _line.startWidth = _line.endWidth = Mathf.Max(_runtimeDraft.agentRadius * .2f, _markerRadius * .18f);
            _line.positionCount = 0;
        }

        private void Update()
        {
            if (!IsReady || _agent == null || !_agent.isOnNavMesh) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            Vector2 movement = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            if (movement.sqrMagnitude > 0) TryMove(movement.normalized * (_speed * dt));
            if (Input.GetMouseButtonDown(0) && !MouseOverHelp())
            {
                var ray = _camera.ScreenPointToRay(Input.mousePosition);
                if (new Plane(UVector3.up, UVector3.zero).Raycast(ray, out float distance))
                {
                    var point = ray.GetPoint(distance);
                    TrySetDestination(new Vector2(point.x, point.z));
                }
            }
            var pan = new Vector2(
                (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
            if (pan.sqrMagnitude > 0) _focus += pan.normalized * (_zoom * dt);
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .001f) _zoom *= Mathf.Pow(.85f, scroll);
            if (Input.GetKeyDown(KeyCode.Home)) FitView();
            ApplyCamera();
            RefreshLine();
        }

        /// <summary>Requests an exact point only when the Unity NavMesh reports a complete path.</summary>
        public bool TrySetDestination(Vector2 target)
        {
            if (!IsReady || _agent == null || !_agent.isOnNavMesh || !TrySample(target, out UVector3 point))
            { ClearPath(); _status = "目标不在 NavMesh 上"; return false; }
            var candidate = new NavMeshPath();
            if (!_agent.CalculatePath(point, candidate) || candidate.status != NavMeshPathStatus.PathComplete ||
                candidate.corners == null || candidate.corners.Length == 0 || !_agent.SetPath(candidate))
            { ClearPath(); _status = "目标没有完整可达路径"; return false; }
            _path.Clear();
            _path.AddRange(candidate.corners);
            _status = "路径由 Unity NavMesh 计算";
            RefreshLine();
            return true;
        }

        /// <summary>Moves along the NavMesh only; blocked segments are not traversed.</summary>
        public bool TryMove(Vector2 delta)
        {
            if (!IsReady || _agent == null || !_agent.isOnNavMesh || !Finite(delta.x) || !Finite(delta.y)) return false;
            if (delta.sqrMagnitude < .00000001f) return true;
            UVector3 from = _agent.nextPosition;
            Vector2 requested = new Vector2(from.x + delta.x, from.z + delta.y);
            if (!TrySample(requested, out UVector3 target) || NavMesh.Raycast(from, target, out _, _filter))
            { ClearPath(); _status = "NavMesh 边界或障碍阻挡了移动"; return false; }
            ClearPath();
            _agent.Move(target - from);
            _status = "离线 NavMesh 手动移动";
            return true;
        }

        private bool TrySample(Vector2 target, out UVector3 point)
        {
            point = default;
            if (!Finite(target.x) || !Finite(target.y)) return false;
            var position = new UVector3(target.x, 0, target.y);
            // 手绘平面的 HeightMesh 保留原始脚点高度；搜索范围不代表验收容差。
            // 与服务端探针一样，只接受原地面上下 2cm 内的结果。
            float sampleRadius = Mathf.Max(_runtimeDraft.agentHeight, _runtimeDraft.voxelSize * 2f);
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, sampleRadius, _filter) ||
                !NavMeshAuthoringDraft.MatchesGroundPoint(position, hit.position)) return false;
            point = hit.position;
            return true;
        }

        private float SnapTolerance() => NavMeshAuthoringDraft.GroundPointTolerance;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void RefreshLine()
        {
            if (_line == null || _agent == null || !_agent.isOnNavMesh) return;
            if (!_agent.hasPath || _agent.pathPending) { _line.positionCount = 0; return; }
            UVector3[] corners = _agent.path.corners;
            _line.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
                _line.SetPosition(i, corners[i] + UVector3.up * Mathf.Max(.02f, _markerRadius * .1f));
        }

        private void ClearPath()
        {
            _path.Clear();
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
            if (_line != null) _line.positionCount = 0;
        }

        private void FitView()
        {
            if (_camera == null || _runtimeDraft == null) return;
            _focus = _runtimeDraft.worldRect.center;
            _zoom = Mathf.Max(_runtimeDraft.worldRect.height * .5f, _runtimeDraft.worldRect.width * .5f / Mathf.Max(.01f, _camera.aspect));
            ApplyCamera();
        }

        private void ApplyCamera()
        {
            if (_camera == null || _runtimeDraft == null) return;
            Rect bounds = _runtimeDraft.worldRect;
            float maxZoom = Mathf.Max(bounds.height, bounds.width / Mathf.Max(.01f, _camera.aspect)) * .75f;
            _zoom = Mathf.Clamp(_zoom, Mathf.Max(.1f, _runtimeDraft.agentRadius), Mathf.Max(.1f, maxZoom));
            float hx = _zoom * Mathf.Max(.01f, _camera.aspect);
            _focus.x = hx * 2 >= bounds.width ? bounds.center.x : Mathf.Clamp(_focus.x, bounds.xMin + hx, bounds.xMax - hx);
            _focus.y = _zoom * 2 >= bounds.height ? bounds.center.y : Mathf.Clamp(_focus.y, bounds.yMin + _zoom, bounds.yMax - _zoom);
            _camera.orthographicSize = _zoom;
            _camera.transform.position = new UVector3(_focus.x, _cameraHeight, _focus.y);
        }

        private static bool MouseOverHelp() => Input.mousePosition.x < 700 && Input.mousePosition.y > Screen.height - 95;

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 690, 85), "导航草稿：本地 NavMesh 预览，不影响服务器或正式地图");
            GUI.Label(new Rect(22, 35, 665, 22), _status);
            if (IsReady)
            {
                Vector2 position = PlayerWorld;
                GUI.Label(new Rect(22, 58, 665, 22), $"脚点 ({position.x:F2}, {position.y:F2})");
            }
        }

        private void ReleasePreview()
        {
            IsReady = false;
            if (_agent != null) _agent.enabled = false;
            if (_ownedData.valid) _ownedData.Remove();
            if (_root != null) Destroy(_root);
            _root = null; _camera = null; _agent = null; _line = null;
            foreach (var mesh in _meshes) if (mesh != null) Destroy(mesh);
            foreach (var material in _materials) if (material != null) Destroy(material);
            _meshes.Clear(); _materials.Clear(); _path.Clear();
            if (_runtimeDraft != null) Destroy(_runtimeDraft);
            _runtimeDraft = null;
        }

        private void OnDestroy() => ReleasePreview();
    }
}
