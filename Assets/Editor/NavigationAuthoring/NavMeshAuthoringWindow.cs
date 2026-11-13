using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.World.NavigationAuthoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MmorpgClient.Editor.NavigationAuthoring
{
    public sealed class NavMeshAuthoringWindow : EditorWindow
    {
        private enum Tool { WalkPolygon, BlockPolygon, WalkRect, BlockRect, Vertices, Spawn, Route }
        private static readonly string[] ToolLabels = { "圈可走区域", "圈障碍区域", "矩形可走", "矩形障碍", "调整顶点", "放出生点", "试寻路" };
        [SerializeField] private NavMeshAuthoringDraft _draft;
        [SerializeField] private Tool _tool;
        [SerializeField] private float _zoom = 1, _opacity = .3f, _snap;
        [SerializeField] private Vector2 _pan;
        private Vector2 _scroll;
        [SerializeField] private List<Vector2> _pending = new List<Vector2>();
        [SerializeField] private bool _pendingBlocked;
        private Rect _canvas, _imageRect;
        [SerializeField] private bool _fit = true;
        private bool _rectDragging, _vertexDragging;
        private Vector2 _rectStart, _rectEnd;
        [SerializeField] private int _selected = -1, _vertex = -1;
        [SerializeField] private string _status = "从整图、分块目录或旧地图标注创建草稿。";
        private readonly Dictionary<NavMeshAreaPolygon, PolygonDiagnostic> _areaDiagnostics = new Dictionary<NavMeshAreaPolygon, PolygonDiagnostic>();
        private PolygonDiagnostic _pendingDiagnostic;
        private NavMeshDataInstance _queryData;
        private static readonly UnityEngine.Vector3 QueryOffset = new UnityEngine.Vector3(0, 10000, 0);
        private UnityEngine.Vector3[] _bakedTriangles = Array.Empty<UnityEngine.Vector3>();
        private readonly List<UnityEngine.Vector3> _route = new List<UnityEngine.Vector3>();
        private Vector2? _routeStart;
        private bool _defaultAttempted;

        private sealed class PolygonDiagnostic
        {
            public string message;
            public int firstEdge = -1, secondEdge = -1;
            public bool valid => message == null;
        }

        [MenuItem("MMORPG/Maps/NavMesh 导航编辑器")]
        public static void Open()
        {
            var window = GetWindow<NavMeshAuthoringWindow>("NavMesh 导航编辑器");
            window.TrySelectDefault(); window.Show();
        }

        public static void OpenDraft(NavMeshAuthoringDraft draft)
        {
            var window = GetWindow<NavMeshAuthoringWindow>("NavMesh 导航编辑器");
            window.SelectDraft(draft); window._defaultAttempted = true; window.Show();
        }

        public NavMeshAuthoringDraft CurrentDraft => _draft;

        private void TrySelectDefault()
        {
            if (_draft != null || _defaultAttempted) return;
            var draft = FestivalNavMeshDraftPresets.FirstReady();
            if (draft != null) { SelectDraft(draft); _defaultAttempted = true; }
        }

        private void OnEnable()
        {
            if (_pending == null) _pending = new List<Vector2>();
            _areaDiagnostics.Clear();
            minSize = new Vector2(900, 580); Undo.undoRedoPerformed += OnUndo;
            EditorApplication.playModeStateChanged += OnPlayState;
        }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndo; EditorApplication.playModeStateChanged -= OnPlayState; ReleaseQuery(); }
        private void OnUndo() { _areaDiagnostics.Clear(); _pendingDiagnostic = null; InvalidateView(); Repaint(); }
        private void OnPlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) ReleaseQuery(); }

        private void OnGUI()
        {
            TrySelectDefault();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(285)))
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawSidebar();
                    EditorGUILayout.EndScrollView();
                }
                _canvas = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            DrawCanvas();
            EditorGUILayout.HelpBox(_status, MessageType.None);
            HandleKeys();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("从整图新建", EditorStyles.toolbarButton)) Guard(NewImage);
                if (GUILayout.Button("从分块新建", EditorStyles.toolbarButton)) Guard(NewTiles);
                if (GUILayout.Button("导入旧标注", EditorStyles.toolbarButton)) Guard(ImportOld);
                GUILayout.Space(10);
                var chosen = (NavMeshAuthoringDraft)EditorGUILayout.ObjectField(_draft, typeof(NavMeshAuthoringDraft), false, GUILayout.MinWidth(160));
                if (chosen != _draft) SelectDraft(chosen);
                using (new EditorGUI.DisabledScope(_draft == null))
                {
                    if (GUILayout.Button("保存当前绘制", EditorStyles.toolbarButton)) Guard(Save);
                    if (GUILayout.Button("适合窗口", EditorStyles.toolbarButton)) { _fit = true; Repaint(); }
                    if (GUILayout.Button("1:1 清晰查看", EditorStyles.toolbarButton)) NativePixelView();
                }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("节庆底图", GUILayout.Width(62));
                for (int i = 0; i < FestivalNavMeshDraftPresets.Keys.Length; i++)
                {
                    var preset = FestivalNavMeshDraftPresets.Load(i);
                    using (new EditorGUI.DisabledScope(preset == null))
                        if (GUILayout.Toggle(preset != null && _draft == preset, FestivalNavMeshDraftPresets.Labels[i], EditorStyles.toolbarButton) && _draft != preset)
                        { SelectDraft(preset); _defaultAttempted = true; }
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawSidebar()
        {
            EditorGUILayout.LabelField("图片地图 · 官方 Unity NavMesh", EditorStyles.boldLabel);
            if (_draft == null)
            {
                EditorGUILayout.HelpBox("选择一张PNG整图，或含r01_c01.png等完整分块的目录。后续新图可单独新建草稿。", MessageType.Info);
                return;
            }
            EditorGUI.BeginChangeCheck();
            string title = EditorGUILayout.TextField("地图名称", _draft.displayName);
            string key = EditorGUILayout.TextField("地图标识", _draft.mapKey);
            if (EditorGUI.EndChangeCheck()) Edit("修改地图名称", () => { _draft.displayName = title; _draft.mapKey = key; });
            EditorGUILayout.LabelField($"底图 {_draft.imageWidth} × {_draft.imageHeight} · {_draft.backgroundTiles.Length}块");
            EditorGUILayout.LabelField($"画布显示比例 {_zoom * EditorGUIUtility.pixelsPerPoint * 100:0.#}%（滚轮放大局部）");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("世界坐标（X/Z）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector2 origin = EditorGUILayout.Vector2Field("西南原点 X/Z", _draft.worldRect.position);
            float width = EditorGUILayout.FloatField("世界宽度", _draft.worldRect.width);
            float height = width * _draft.imageHeight / Mathf.Max(1, _draft.imageWidth);
            EditorGUILayout.LabelField("高度（保持原比例）", height.ToString("0.###"));
            bool confirmed = EditorGUILayout.Toggle("已核实世界范围", _draft.boundsConfirmed);
            if (EditorGUI.EndChangeCheck()) Edit("修改地图范围", () => {
                _draft.worldRect = new Rect(origin, new Vector2(width, height)); _draft.boundsConfirmed = confirmed; });
            if (!_draft.boundsConfirmed) EditorGUILayout.HelpBox("当前是本地作者设定的比例与范围。导出会保留未确认状态。", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("描绘工具", EditorStyles.boldLabel);
            var nextTool = (Tool)GUILayout.SelectionGrid((int)_tool, ToolLabels, 2);
            if (nextTool != _tool) Guard(() => { if (_pending.Count > 0) Save(); _tool = nextTool; _rectDragging = false; EditorUtility.SetDirty(this); });
            _snap = Mathf.Max(0, EditorGUILayout.FloatField("吸附像素（0为关）", _snap));
            _opacity = EditorGUILayout.Slider("烘焙区域透明度", _opacity, .05f, .8f);
            EditorGUILayout.HelpBox("多边形：逐点单击，Enter完成，退格撤最后点。保存当前绘制也会保存未完成轮廓；自交或点数不足会暂存为待修复区域。顶点：拖动，Shift单击边插点，Delete删点。中键平移，滚轮缩放。", MessageType.None);
            if (_pending.Count > 0 && GUILayout.Button("完成当前多边形")) Guard(CompletePolygon);
            if (_pending.Count > 0 && GUILayout.Button("暂存当前轮廓并调整顶点")) Guard(() => { Save(); _tool = Tool.Vertices; EditorUtility.SetDirty(this); });
            if (_pending.Count > 0 && GUILayout.Button("取消当前绘制")) { _pending.Clear(); PendingChanged(); _status = "已取消未完成轮廓；已保存区域保持不变。"; }
            DrawAreas();
            EditorGUILayout.Space();
            DrawAgentSettings();
            if (GUILayout.Button("烘焙 Unity NavMesh", GUILayout.Height(29))) Guard(() => {
                if (_pending.Count > 0) Save();
                ReleaseQuery(); NavMeshAuthoringAssetIO.Bake(_draft); BuildOverlay(); _status = "烘焙完成，绿色填充为实际NavMesh。设置出生点后可试走。"; });
            bool current = NavMeshAuthoringAssetIO.IsBakeCurrent(_draft);
            EditorGUILayout.LabelField(current ? "烘焙与当前标注一致" : "当前修改尚未烘焙", current ? EditorStyles.boldLabel : EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("出生点", _draft.spawnSet ? $"X {_draft.spawn.x:0.###} / Z {_draft.spawn.y:0.###}" : "未设置");
            using (new EditorGUI.DisabledScope(!current || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Play 离线试走")) Guard(StartPreview);
                if (GUILayout.Button("导出同源几何给服务器")) Guard(Export);
            }
            if (GUILayout.Button("打开官方 Navigation 设置")) EditorApplication.ExecuteMenuItem("Window/AI/Navigation");
        }

        private void DrawAreas()
        {
            EditorGUILayout.LabelField($"区域（{_draft.polygons.Count}）", EditorStyles.boldLabel);
            for (int i = 0; i < _draft.polygons.Count; i++)
            {
                var p = _draft.polygons[i];
                var diagnostic = AreaDiagnostic(p);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = GUILayout.Toggle(_selected == i, $"{i + 1}. {(p.blocked ? "障碍" : "可走")} · {p.points.Count}点{(diagnostic.valid ? "" : " · 待修复")}", "Button");
                    if (selected) _selected = i;
                    if (GUILayout.Button("×", GUILayout.Width(24))) { int index = i; Edit("删除区域", () => _draft.polygons.RemoveAt(index)); _selected = -1; break; }
                }
            }
            if (_selected >= 0 && _selected < _draft.polygons.Count)
            {
                var p = _draft.polygons[_selected];
                var diagnostic = AreaDiagnostic(p);
                if (!diagnostic.valid) EditorGUILayout.HelpBox("待修复：" + DiagnosticText(diagnostic, p.points.Count) + "。用“调整顶点”拖动或Shift单击边插点，修正后才可烘焙。", MessageType.Warning);
                EditorGUI.BeginChangeCheck(); string label = EditorGUILayout.TextField("区域名称", p.name);
                bool blocked = EditorGUILayout.Toggle("此区域是障碍", p.blocked);
                if (EditorGUI.EndChangeCheck()) Edit("修改区域", () => { p.name = label; p.blocked = blocked; });
            }
        }

        private void DrawAgentSettings()
        {
            EditorGUILayout.LabelField("角色与烘焙参数（世界单位）", EditorStyles.boldLabel);
            var ids = new List<int>(); var names = new List<string>();
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++) { int id = NavMesh.GetSettingsByIndex(i).agentTypeID; ids.Add(id); names.Add(NavMesh.GetSettingsNameFromID(id)); }
            EditorGUI.BeginChangeCheck();
            int selection = EditorGUILayout.Popup("角色类型", Mathf.Max(0, ids.IndexOf(_draft.agentTypeId)), names.ToArray());
            float radius = EditorGUILayout.FloatField("角色半径", _draft.agentRadius);
            float height = EditorGUILayout.FloatField("角色高度", _draft.agentHeight);
            float climb = EditorGUILayout.FloatField("最大台阶", _draft.agentClimb);
            float slope = EditorGUILayout.Slider("最大坡度", _draft.agentSlope, 0, 60);
            float voxel = EditorGUILayout.FloatField("体素精度", _draft.voxelSize);
            if (EditorGUI.EndChangeCheck()) Edit("修改烘焙参数", () => {
                if (selection >= 0 && selection < ids.Count) _draft.agentTypeId = ids[selection];
                _draft.agentRadius = radius; _draft.agentHeight = height; _draft.agentClimb = climb; _draft.agentSlope = slope; _draft.voxelSize = voxel; });
        }

        private void DrawCanvas()
        {
            if (_canvas.width < 1 || _canvas.height < 1) return;
            EditorGUI.DrawRect(_canvas, new Color(.11f, .12f, .13f));
            if (_draft == null || _draft.backgroundTiles == null || _draft.backgroundTiles.Length == 0) return;
            if (_fit)
            {
                _zoom = Mathf.Min((_canvas.width - 30) / Mathf.Max(1, _draft.imageWidth), (_canvas.height - 30) / Mathf.Max(1, _draft.imageHeight));
                _pan = new Vector2((_canvas.width - _draft.imageWidth * _zoom) * .5f, (_canvas.height - _draft.imageHeight * _zoom) * .5f); _fit = false;
            }
            _imageRect = new Rect(_canvas.position + _pan, new Vector2(_draft.imageWidth, _draft.imageHeight) * _zoom);
            GUI.BeginClip(_canvas);
            var offset = _canvas.position;
            DrawBackground(offset);
            Handles.BeginGUI();
            if (_bakedTriangles.Length == 0 && !EditorApplication.isPlayingOrWillChangePlaymode && NavMeshAuthoringAssetIO.IsBakeCurrent(_draft)) Guard(BuildOverlay);
            Handles.color = new Color(.1f, .95f, .55f, _opacity);
            for (int i = 0; i + 2 < _bakedTriangles.Length; i += 3)
                Handles.DrawAAConvexPolygon(WorldScreen(_bakedTriangles[i], offset), WorldScreen(_bakedTriangles[i + 1], offset), WorldScreen(_bakedTriangles[i + 2], offset));
            for (int i = 0; i < _draft.polygons.Count; i++)
            {
                var p = _draft.polygons[i]; Handles.color = p.blocked ? new Color(1, .3f, .25f) : new Color(.1f, .9f, .55f);
                DrawPolygon(p.points, offset, true, i == _selected ? 3 : 1.5f);
                if (i == _selected && _tool == Tool.Vertices)
                    foreach (var point in p.points) { var s = PixelScreen(point) - offset; EditorGUI.DrawRect(new Rect(s.x - 4, s.y - 4, 8, 8), Handles.color); }
                if (i == _selected) DrawDiagnostic(p.points, AreaDiagnostic(p), offset);
            }
            Handles.color = _pending.Count > 0 ? (_pendingBlocked ? Color.red : Color.green) :
                ((_tool == Tool.BlockPolygon || _tool == Tool.BlockRect) ? Color.red : Color.green);
            DrawPolygon(_pending, offset, false, 2);
            if (_pending.Count >= 3)
            {
                // 提前显示完成时会加入的闭合边，避免只在回车后才发现交叉。
                Handles.color = new Color(1f, .85f, .2f, .8f);
                Handles.DrawDottedLine(PixelScreen(_pending[_pending.Count - 1]) - offset, PixelScreen(_pending[0]) - offset, 4);
            }
            DrawDiagnostic(_pending, _pendingDiagnostic, offset);
            if (_rectDragging) DrawPolygon(Rectangle(_rectStart, _rectEnd), offset, true, 2);
            if (_draft.spawnSet)
            {
                Vector2 s = PixelScreen(_draft.WorldToPixel(_draft.spawn)) - offset;
                Handles.color = Color.yellow; Handles.DrawLine(s + Vector2.left * 8, s + Vector2.right * 8); Handles.DrawLine(s + Vector2.up * 8, s + Vector2.down * 8);
                GUI.Label(new Rect(s.x + 8, s.y - 12, 70, 22), "出生点");
            }
            Handles.color = Color.yellow;
            if (_route.Count > 1) Handles.DrawAAPolyLine(3, _route.Select(v => WorldScreen(v, offset)).ToArray());
            Handles.EndGUI(); GUI.EndClip();
            HandleCanvasInput();
        }

        private void DrawBackground(Vector2 offset)
        {
            int columns = _draft.backgroundTiles.Max(t => t.column), rows = _draft.backgroundTiles.Max(t => t.row);
            var widths = new int[columns]; var heights = new int[rows];
            foreach (var t in _draft.backgroundTiles) { widths[t.column - 1] = t.pixelWidth; heights[t.row - 1] = t.pixelHeight; }
            foreach (var t in _draft.backgroundTiles)
            {
                if (t.texture == null) continue;
                int x = 0, y = 0; for (int i = 0; i < t.column - 1; i++) x += widths[i]; for (int i = 0; i < t.row - 1; i++) y += heights[i];
                GUI.DrawTexture(new Rect(PixelScreen(new Vector2(x, y)) - offset, new Vector2(t.pixelWidth, t.pixelHeight) * _zoom), t.texture, ScaleMode.StretchToFill);
            }
        }

        private void HandleCanvasInput()
        {
            var e = Event.current;
            if (!_canvas.Contains(e.mousePosition) && !_rectDragging && !_vertexDragging) return;
            if (e.type == EventType.ScrollWheel)
            {
                Vector2 before = (e.mousePosition - _imageRect.position) / _zoom;
                _zoom = Mathf.Clamp(_zoom * Mathf.Exp(-e.delta.y * .08f), .005f, 12);
                _pan = e.mousePosition - _canvas.position - before * _zoom; e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseDrag && (e.button == 2 || (e.alt && e.button == 0))) { _pan += e.delta; e.Use(); Repaint(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (e.alt || e.button != 0) return;
            Vector2 pixel = ScreenPixel(e.mousePosition);
            bool inside = _imageRect.Contains(e.mousePosition);
            if (e.type == EventType.MouseDown && inside)
            {
                GUI.FocusControl(null);
                switch (_tool)
                {
                    case Tool.WalkPolygon: case Tool.BlockPolygon:
                        if (e.clickCount == 2 && _pending.Count >= 3) Guard(CompletePolygon);
                        else if (_pending.Count == 0 || Vector2.Distance(_pending[_pending.Count - 1], pixel) * _zoom > 3)
                        {
                            if (_pending.Count == 0) _pendingBlocked = _tool == Tool.BlockPolygon;
                            _pending.Add(pixel); PendingChanged();
                        }
                        break;
                    case Tool.WalkRect: case Tool.BlockRect: _rectStart = _rectEnd = pixel; _rectDragging = true; break;
                    case Tool.Vertices: BeginVertex(e.mousePosition, pixel, e.shift); break;
                    case Tool.Spawn: Edit("设置出生点", () => { _draft.spawn = _draft.PixelToWorld(pixel); _draft.spawnSet = true; }); break;
                    case Tool.Route: Guard(() => RouteClick(_draft.PixelToWorld(pixel))); break;
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_rectDragging) _rectEnd = pixel;
                if (_vertexDragging && _selected >= 0 && _vertex >= 0) { _draft.polygons[_selected].points[_vertex] = pixel; Changed(); }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp)
            {
                if (_rectDragging) { _rectDragging = false; Guard(() => AddPolygon(Rectangle(_rectStart, pixel), _tool == Tool.BlockRect)); }
                if (_vertexDragging) { _vertexDragging = false; Guard(() => NavigationPolygonGeometry.ValidatePolygon(_draft.polygons[_selected].points)); }
                e.Use(); Repaint();
            }
        }

        private void BeginVertex(Vector2 screen, Vector2 pixel, bool insert)
        {
            float best = 12; int polygonIndex = -1, pointIndex = -1;
            for (int i = 0; i < _draft.polygons.Count; i++)
            for (int j = 0; j < _draft.polygons[i].points.Count; j++)
            {
                var p = _draft.polygons[i].points;
                Vector2 a = PixelScreen(p[j]), b = PixelScreen(p[(j + 1) % p.Count]);
                // 单点暂存区也允许Shift插点；重合端点不能交给零长度线段距离计算。
                float distance = insert && (a - b).sqrMagnitude > .000001f ?
                    HandleUtility.DistancePointToLineSegment(screen, a, b) : Vector2.Distance(screen, a);
                if (distance < best) { best = distance; polygonIndex = i; pointIndex = j; }
            }
            if (polygonIndex < 0) return;
            _selected = polygonIndex; Undo.RegisterCompleteObjectUndo(_draft, insert ? "插入区域顶点" : "移动区域顶点");
            if (insert) { _draft.polygons[_selected].points.Insert(pointIndex + 1, pixel); _vertex = pointIndex + 1; Changed(); }
            else _vertex = pointIndex;
            _vertexDragging = true;
        }

        private void CompletePolygon()
        {
            _pendingDiagnostic = InspectPolygon(_pending);
            if (!_pendingDiagnostic.valid)
            {
                _status = "轮廓未完成：" + DiagnosticText(_pendingDiagnostic, _pending.Count) +
                    "。全部顶点已保留；可退格撤点，或点“保存当前绘制”暂存后用“调整顶点”修正。";
                Repaint();
                return;
            }
            AddPolygon(new List<Vector2>(_pending), _pendingBlocked);
            _pending.Clear(); PendingChanged();
            _status = "多边形已完成；点击“保存当前绘制”保存草稿。";
        }
        private void AddPolygon(List<Vector2> points, bool blocked)
        {
            NavigationPolygonGeometry.ValidatePolygon(points);
            Edit("添加导航区域", () => _draft.polygons.Add(new NavMeshAreaPolygon { name = (blocked ? "障碍" : "地面") + (_draft.polygons.Count + 1), blocked = blocked, points = points }));
            _selected = _draft.polygons.Count - 1;
        }
        private static List<Vector2> Rectangle(Vector2 a, Vector2 b) => new List<Vector2> { new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)),
            new Vector2(Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y)), new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), new Vector2(Mathf.Min(a.x, b.x), Mathf.Max(a.y, b.y)) };

        private static PolygonDiagnostic InspectPolygon(IReadOnlyList<Vector2> points)
        {
            var result = new PolygonDiagnostic();
            try { NavigationPolygonGeometry.ValidatePolygon(points); }
            catch (Exception error)
            {
                result.message = error.Message;
                NavigationPolygonGeometry.TryGetSelfIntersection(points, out result.firstEdge, out result.secondEdge);
            }
            return result;
        }

        private PolygonDiagnostic AreaDiagnostic(NavMeshAreaPolygon polygon)
        {
            if (!_areaDiagnostics.TryGetValue(polygon, out var result))
            {
                result = InspectPolygon(polygon.points); _areaDiagnostics[polygon] = result;
            }
            return result;
        }

        private static string DiagnosticText(PolygonDiagnostic diagnostic, int count)
        {
            if (diagnostic.firstEdge < 0 || diagnostic.secondEdge < 0 || count == 0) return diagnostic.message;
            string Edge(int index) => $"{index + 1}→{(index + 1) % count + 1}";
            return diagnostic.message + $"（边 {Edge(diagnostic.firstEdge)} 与边 {Edge(diagnostic.secondEdge)}）";
        }

        private void DrawDiagnostic(IReadOnlyList<Vector2> points, PolygonDiagnostic diagnostic, Vector2 offset)
        {
            if (diagnostic == null || diagnostic.valid || points.Count < 2) return;
            foreach (int index in new[] { diagnostic.firstEdge, diagnostic.secondEdge })
            {
                if (index < 0 || index >= points.Count) continue;
                Vector2 a = PixelScreen(points[index]) - offset, b = PixelScreen(points[(index + 1) % points.Count]) - offset;
                Handles.color = Color.yellow; Handles.DrawAAPolyLine(5, a, b);
                Vector2 center = (a + b) * .5f;
                GUI.Label(new Rect(center.x + 5, center.y - 20, 170, 22), $"冲突边 {index + 1}→{(index + 1) % points.Count + 1}");
            }
        }

        private void PendingChanged()
        {
            _pendingDiagnostic = null;
            EditorUtility.SetDirty(this); Repaint();
        }
        private Vector2 PixelScreen(Vector2 p) => _imageRect.position + p * _zoom;
        private void NativePixelView()
        {
            if (_draft == null) return;
            Vector2 focus = _imageRect.width > 0 && _zoom > 0 ? (_canvas.center - _imageRect.position) / _zoom : new Vector2(_draft.imageWidth, _draft.imageHeight) * .5f;
            _zoom = 1f / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
            _pan = _canvas.size * .5f - focus * _zoom;
            _fit = false; _status = "1:1原像素查看：中键拖动查看其他位置；适合窗口可返回整图。"; Repaint();
        }
        private void DrawPolygon(IReadOnlyList<Vector2> points, Vector2 offset, bool closed, float width)
        {
            if (points.Count < 2) return;
            var line = new UnityEngine.Vector3[points.Count + (closed ? 1 : 0)];
            for (int i = 0; i < points.Count; i++) line[i] = PixelScreen(points[i]) - offset;
            if (closed) line[line.Length - 1] = line[0];
            Handles.DrawAAPolyLine(width, line);
        }
        private UnityEngine.Vector3 WorldScreen(UnityEngine.Vector3 p, Vector2 offset) => PixelScreen(_draft.WorldToPixel(new Vector2(p.x, p.z))) - offset;
        private Vector2 ScreenPixel(Vector2 screen)
        {
            Vector2 p = (screen - _imageRect.position) / _zoom;
            if (_snap > 0) { p.x = Mathf.Round(p.x / _snap) * _snap; p.y = Mathf.Round(p.y / _snap) * _snap; }
            return new Vector2(Mathf.Clamp(p.x, 0, _draft.imageWidth), Mathf.Clamp(p.y, 0, _draft.imageHeight));
        }

        private void HandleKeys()
        {
            var e = Event.current; if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (e.keyCode == KeyCode.Escape) { _pending.Clear(); _rectDragging = false; PendingChanged(); _status = "已取消未完成轮廓；已保存区域保持不变。"; e.Use(); }
            else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _pending.Count > 0) { Guard(CompletePolygon); e.Use(); }
            else if (e.keyCode == KeyCode.Backspace && _pending.Count > 0) { _pending.RemoveAt(_pending.Count - 1); PendingChanged(); e.Use(); }
            else if (e.keyCode == KeyCode.Delete && _tool == Tool.Vertices && _selected >= 0 && _vertex >= 0 && _draft.polygons[_selected].points.Count > 3)
            { Edit("删除区域顶点", () => _draft.polygons[_selected].points.RemoveAt(_vertex)); _vertex = -1; e.Use(); }
        }

        private void BuildOverlay()
        {
            ReleaseQuery();
            if (!NavMeshAuthoringAssetIO.IsBakeCurrent(_draft)) return;
            _queryData = NavMesh.AddNavMeshData(_draft.bakedData, QueryOffset, Quaternion.identity);
            if (!_queryData.valid) throw new InvalidOperationException("无法加载已烘焙NavMesh。");
            var mesh = NavMesh.CalculateTriangulation(); var vertices = new List<UnityEngine.Vector3>();
            for (int i = 0; i < mesh.indices.Length; i += 3)
            {
                var a = mesh.vertices[mesh.indices[i]];
                if (Mathf.Abs(a.y - QueryOffset.y) > _draft.agentHeight + 1) continue;
                for (int j = 0; j < 3; j++) vertices.Add(mesh.vertices[mesh.indices[i + j]] - QueryOffset);
            }
            _bakedTriangles = vertices.ToArray();
        }

        private void RouteClick(Vector2 world)
        {
            if (!NavMeshAuthoringAssetIO.IsBakeCurrent(_draft)) throw new InvalidOperationException("请先烘焙当前标注。");
            if (!_queryData.valid) BuildOverlay();
            var filter = new NavMeshQueryFilter { agentTypeID = _draft.agentTypeId, areaMask = NavMesh.AllAreas };
            var position = new UnityEngine.Vector3(world.x, QueryOffset.y, world.y);
            if (!NavMesh.SamplePosition(position, out var hit, Mathf.Max(_draft.agentHeight, _draft.voxelSize * 2), filter) ||
                !NavMeshAuthoringDraft.MatchesGroundPoint(position, hit.position))
                throw new InvalidOperationException("此点不在实际NavMesh上。");
            if (!_routeStart.HasValue) { _routeStart = new Vector2(hit.position.x, hit.position.z); _route.Clear(); _status = "已选起点，再点终点。"; return; }
            var start = new UnityEngine.Vector3(_routeStart.Value.x, QueryOffset.y, _routeStart.Value.y); _routeStart = null;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, hit.position, filter, path) || path.status != NavMeshPathStatus.PathComplete)
            { _route.Clear(); _status = "起终点不连通，未生成可走路线。"; return; }
            _route.Clear(); _route.AddRange(path.corners.Select(p => p - QueryOffset)); _status = $"完整路线：{_route.Count}个拐点。";
        }

        private void StartPreview()
        {
            if (_pending.Count > 0) Save();
            NavMeshAuthoringAssetIO.CheckSpawn(_draft);
            if (!NavMeshAuthoringAssetIO.IsBakeCurrent(_draft)) throw new InvalidOperationException("请先重新烘焙当前标注。");
            Save(); ReleaseQuery();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_draft)).Replace('\\', '/');
            string path = directory + "/" + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(_draft)) + ".preview.unity";
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var go = new GameObject("离线 NavMesh 试走"); SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<NavMeshDraftPreview>().draft = _draft;
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (active.IsValid()) SceneManager.SetActiveScene(active); }
            NavMeshAuthoringPlaySession.Begin(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
        }

        private void Export()
        {
            if (_pending.Count > 0) Save();
            string folder = EditorUtility.SaveFolderPanel("导出NavMesh同源几何（草稿）", Path.GetFullPath(".codex-artifacts"), _draft.mapKey);
            if (string.IsNullOrEmpty(folder)) return;
            NavMeshAuthoringAssetIO.Export(_draft, folder); _status = "已导出OBJ、多边形标注、来源SHA及服务端烘焙命令：" + folder;
        }
        private string NewPath()
        {
            Directory.CreateDirectory(NavMeshAuthoringAssetIO.DraftRoot); AssetDatabase.Refresh();
            return EditorUtility.SaveFilePanelInProject("保存新导航草稿", "new-map", "asset", "草稿可持续保存和编辑。", NavMeshAuthoringAssetIO.DraftRoot);
        }
        private void NewImage() { string file = EditorUtility.OpenFilePanel("选择新地图PNG整图", "", "png"); if (file.Length == 0) return; string path = NewPath(); if (path.Length > 0) SelectDraft(NavMeshAuthoringAssetIO.CreateFromImage(file, path)); }
        private void NewTiles() { string folder = EditorUtility.OpenFolderPanel("选择完整分块目录（r01_c01.png）", "", ""); if (folder.Length == 0) return; string path = NewPath(); if (path.Length > 0) SelectDraft(NavMeshAuthoringAssetIO.CreateFromTileFolder(folder, path)); }
        private void ImportOld() { string folder = EditorUtility.OpenFolderPanel("选择含layout/source/navigation.json的旧图目录", "Assets/Resources/World/CurrentMaps20261009", ""); if (folder.Length == 0) return; string path = NewPath(); if (path.Length > 0) SelectDraft(NavMeshAuthoringAssetIO.ImportExistingMap(folder, path)); }
        private void Save()
        {
            if (_draft == null)
            {
                if (_pending.Count > 0) throw new InvalidOperationException("未完成轮廓尚未关联草稿，不能丢弃；请先恢复原草稿。");
                return;
            }
            string savedPendingStatus = null;
            if (_pending.Count > 0)
            {
                var diagnostic = InspectPolygon(_pending);
                // 暂存保留全部原始顶点；烘焙仍由几何校验拒绝无效轮廓。
                Undo.RecordObjects(new UnityEngine.Object[] { _draft, this }, "暂存当前导航轮廓");
                _draft.polygons.Add(new NavMeshAreaPolygon {
                    name = (_pendingBlocked ? "障碍" : "地面") + (_draft.polygons.Count + 1),
                    blocked = _pendingBlocked, points = new List<Vector2>(_pending) });
                _selected = _draft.polygons.Count - 1; _vertex = -1;
                if (!diagnostic.valid) _tool = Tool.Vertices;
                savedPendingStatus = diagnostic.valid ? "当前轮廓已保存为区域。" :
                    "当前轮廓已原样保存为待修复区域：" + DiagnosticText(diagnostic, _pending.Count) + "。请用“调整顶点”修正后再烘焙。";
                _pending.Clear(); PendingChanged(); Changed();
            }
            EditorUtility.SetDirty(_draft); AssetDatabase.SaveAssetIfDirty(_draft);
            _status = savedPendingStatus ?? "草稿已保存；已有区域及其顶点保持不变。";
        }

        private void SelectDraft(NavMeshAuthoringDraft draft)
        {
            // 重开同一草稿只激活窗口，不清除当前尚未完成的绘制。
            if (draft == _draft) { Repaint(); return; }
            if (_pending.Count > 0)
            {
                try { Save(); }
                catch (Exception error)
                {
                    _status = "未切换草稿，当前轮廓已保留：" + error.Message;
                    Debug.LogWarning("[NavMeshAuthoring] " + _status); Repaint(); return;
                }
            }
            ReleaseQuery(); _draft = draft; _selected = _vertex = -1; _fit = true;
            _areaDiagnostics.Clear(); _pendingDiagnostic = null;
            _status = draft == null ? "请选择草稿。" : "已打开草稿，选择描绘工具开始。";
            EditorUtility.SetDirty(this); Repaint();
        }
        private void Edit(string label, Action change) { Undo.RecordObject(_draft, label); change(); Changed(); }
        private void Changed() { _areaDiagnostics.Clear(); EditorUtility.SetDirty(_draft); InvalidateView(); Repaint(); }
        private void InvalidateView() { ReleaseQuery(); _route.Clear(); _routeStart = null; }
        private void ReleaseQuery() { if (_queryData.valid) _queryData.Remove(); _queryData = default; _bakedTriangles = Array.Empty<UnityEngine.Vector3>(); }
        private void Guard(Action action) { try { action(); } catch (Exception error) { _status = error.Message; Debug.LogWarning("[NavMeshAuthoring] " + error.Message); } Repaint(); }
    }

    [InitializeOnLoad]
    internal static class NavMeshAuthoringPlaySession
    {
        private const string Key = "NavMeshAuthoring.PreviousPlayScene";
        static NavMeshAuthoringPlaySession() { EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".active", false))
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key, ""));
                SessionState.EraseBool(Key + ".active"); SessionState.EraseString(Key);
            }
        }; }
        internal static void Begin(SceneAsset scene)
        {
            SessionState.SetString(Key, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + ".active", true); EditorSceneManager.playModeStartScene = scene; EditorApplication.EnterPlaymode();
        }
    }
}
