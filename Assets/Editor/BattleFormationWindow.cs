using System;
using System.IO;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using UnityEditor;
using UnityEngine;
using UnityVector3 = UnityEngine.Vector3;

namespace MmorpgClient.Editor
{
    public sealed class BattleFormationWindow : EditorWindow
    {
        private const string AssetPath = "Assets/Resources/" + BattleFormationLayout.ResourcePath + ".asset";
        private const string EditUndoName = "编辑战斗站位";
        private const float HandleRadius = 10f;
        private const float HitRadius = 14f;
        private const float DesignWidth = QdaoUguiTheme.DesignWidth;
        private const float DesignHeight = QdaoUguiTheme.DesignHeight;
        private const int SlotsPerRow = BattleStage.FrontRowCount;

        private static readonly Row[] Rows =
        {
            new Row(false, SlotsPerRow, "敌方角色", new Color32(240, 78, 72, 255)),
            new Row(false, 0, "敌方宠物", new Color32(174, 112, 228, 255)),
            new Row(true, 0, "我方宠物", new Color32(106, 198, 93, 255)),
            new Row(true, SlotsPerRow, "我方角色", new Color32(83, 169, 239, 255)),
        };

        private static readonly string[] SlotLabels = CreateSlotLabels();
        private readonly UnityVector3[] _rowPoints = new UnityVector3[SlotsPerRow];
        [SerializeField] private int _selectedIndex;
        private BattleFormationLayout _layout;
        private Texture2D _arenaTexture;
        private Texture2D _referenceTexture;
        private string _referenceLabel;
        private string _referenceDirectory;
        [SerializeField] private float _referenceOpacity = 0.55f;
        private GUIStyle _numberStyle;
        private GUIStyle _missingArtStyle;
        private int _dragControl;
        private Vector2 _dragOffset;
        private bool _dragChanged;

        [MenuItem("Tools/战斗/编辑战斗站位")]
        public static void Open()
        {
            GetWindow<BattleFormationWindow>().Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("战斗站位");
            minSize = new Vector2(680f, 390f);
            ReloadAssets();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            FinishDrag();
            ClearReference();
        }

        private void OnLostFocus() => FinishDrag();

        private void OnProjectChange()
        {
            ReloadAssets();
            Repaint();
        }

        private void ReloadAssets()
        {
            _layout = AssetDatabase.LoadAssetAtPath<BattleFormationLayout>(AssetPath);
            if (_arenaTexture == null)
                _arenaTexture = BattleUiStyle.ResolveArenaBackground()?.texture;
        }

        private void OnUndoRedo()
        {
            EditorApplication.delayCall -= SaveLayout;
            EditorApplication.delayCall += SaveLayout;
            Repaint();
        }

        private void OnGUI()
        {
            if (_layout == null)
            {
                EditorGUILayout.HelpBox("找不到战斗站位资源。\n" + AssetPath, MessageType.Error);
                return;
            }

            DrawControls();
            bool hasReference = _referenceTexture != null;
            if (hasReference) DrawReferenceControls();
            DrawLegend();

            float aspect = DesignWidth / DesignHeight;
            float controlsHeight = hasReference ? 117f : 95f;
            float height = Mathf.Max(100f, Mathf.Min((position.width - 12f) / aspect, position.height - controlsHeight));
            Rect available = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            float width = Mathf.Min(available.width, available.height * aspect);
            var preview = new Rect(available.center.x - width * 0.5f, available.y, width, width / aspect);
            int previewControl = GUIUtility.GetControlID(FocusType.Passive);
            if (preview.width > 1f)
            {
                HandlePreviewInput(preview, previewControl);
                if (Event.current.type == EventType.Repaint) DrawPreview(preview);
            }
            EditorGUILayout.LabelField("拖动圆点移动脚底位置。坐标原点在左上角，修改后自动保存，可用 Unity 撤销。",
                EditorStyles.miniLabel);
        }

        private void DrawControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _selectedIndex = EditorGUILayout.Popup(Mathf.Clamp(_selectedIndex, 0, SlotLabels.Length - 1),
                    SlotLabels, GUILayout.Width(132f));
                Row row = GetRow(_selectedIndex);
                int slot = row.FirstSlot + _selectedIndex % SlotsPerRow;
                Vector2 current = _layout.GetPosition(row.TeamIsMine, slot);
                GUILayout.Label("X", GUILayout.Width(13f));
                float x = EditorGUILayout.DelayedFloatField(current.x, GUILayout.Width(77f));
                GUILayout.Label("Y", GUILayout.Width(13f));
                float y = EditorGUILayout.DelayedFloatField(current.y, GUILayout.Width(77f));
                if (IsFinite(x) && IsFinite(y))
                {
                    var next = ClampToDesign(new Vector2(x, y));
                    if (next != current)
                    {
                        Undo.RegisterCompleteObjectUndo(_layout, EditUndoName);
                        WritePosition(row, slot, next);
                        SaveLayout();
                    }
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("载入参考图", GUILayout.ExpandWidth(false)))
                {
                    LoadReference();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("定位站位资源", GUILayout.ExpandWidth(false)))
                    EditorGUIUtility.PingObject(_layout);
                if (GUILayout.Button("恢复默认", GUILayout.ExpandWidth(false)))
                {
                    Undo.RegisterCompleteObjectUndo(_layout, "恢复默认战斗站位");
                    _layout.ResetToDefaults();
                    EditorUtility.SetDirty(_layout);
                    SaveLayout();
                }
            }
        }

        private void DrawReferenceControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(_referenceLabel, EditorStyles.miniLabel, GUILayout.MaxWidth(200f));
                GUILayout.Label("透明度", GUILayout.ExpandWidth(false));
                _referenceOpacity = GUILayout.HorizontalSlider(_referenceOpacity, 0f, 1f, GUILayout.Width(108f));
                GUILayout.Label($"{Mathf.RoundToInt(_referenceOpacity * 100f)}%", GUILayout.Width(38f));
                if (GUILayout.Button("移除参考图", GUILayout.ExpandWidth(false)))
                {
                    ClearReference();
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("等比缩放，按高度居中对齐", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            }
        }

        private void LoadReference()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("载入战斗参考图", _referenceDirectory ?? string.Empty,
                new[] { "PNG / JPG", "png,jpg,jpeg" });
            if (string.IsNullOrEmpty(path)) return;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), markNonReadable: true))
                    throw new IOException("无法解码该图片。");
                ClearReference();
                _referenceTexture = texture;
                _referenceLabel = Path.GetFileName(path);
                _referenceDirectory = Path.GetDirectoryName(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is UnityException)
            {
                DestroyImmediate(texture);
                EditorUtility.DisplayDialog("参考图载入失败", error.Message, "确定");
            }
        }

        private void ClearReference()
        {
            if (_referenceTexture != null) DestroyImmediate(_referenceTexture);
            _referenceTexture = null;
            _referenceLabel = null;
        }

        private static void DrawLegend()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (Row row in Rows)
                {
                    Rect swatch = GUILayoutUtility.GetRect(10f, 16f, GUILayout.Width(10f));
                    EditorGUI.DrawRect(new Rect(swatch.x, swatch.y + 4f, 9f, 9f), row.Color);
                    GUILayout.Label(row.Label, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    GUILayout.Space(12f);
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("2560 × 1080", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            }
        }

        private void DrawPreview(Rect preview)
        {
            GUI.BeginClip(preview);
            var local = new Rect(0f, 0f, preview.width, preview.height);
            EditorGUI.DrawRect(local, new Color(0.12f, 0.14f, 0.15f));
            if (_arenaTexture != null)
                GUI.DrawTexture(local, _arenaTexture, ScaleMode.StretchToFill);
            else
            {
                _missingArtStyle ??= new GUIStyle(EditorStyles.whiteLabel) { alignment = TextAnchor.UpperCenter };
                GUI.Label(local, "战斗背景资源未找到", _missingArtStyle);
            }

            if (_referenceTexture != null && _referenceOpacity > 0f)
            {
                float width = local.height * _referenceTexture.width / _referenceTexture.height;
                var rect = new Rect(local.center.x - width * 0.5f, 0f, width, local.height);
                Color originalTint = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, _referenceOpacity);
                GUI.DrawTexture(rect, _referenceTexture, ScaleMode.StretchToFill);
                GUI.color = originalTint;
            }

            _numberStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.06f, 0.07f, 0.08f) },
            };
            Handles.BeginGUI();
            Color originalColor = Handles.color;
            for (int rowIndex = 0; rowIndex < Rows.Length; rowIndex++)
            {
                Row row = Rows[rowIndex];
                for (int col = 0; col < SlotsPerRow; col++)
                    _rowPoints[col] = ToPreview(local, _layout.GetPosition(row.TeamIsMine, row.FirstSlot + col));
                Handles.color = new Color(row.Color.r, row.Color.g, row.Color.b, 0.8f);
                Handles.DrawAAPolyLine(2f, _rowPoints);
            }
            for (int index = 0; index < SlotLabels.Length; index++)
                if (index != _selectedIndex) DrawHandle(local, index, false);
            DrawHandle(local, _selectedIndex, true);
            Handles.color = originalColor;
            Handles.EndGUI();
            GUI.EndClip();
        }

        private void DrawHandle(Rect preview, int index, bool selected)
        {
            Row row = GetRow(index);
            int col = index % SlotsPerRow;
            Vector2 center = ToPreview(preview, _layout.GetPosition(row.TeamIsMine, row.FirstSlot + col));
            float radius = selected ? HandleRadius + 2f : HandleRadius;
            Handles.color = selected ? Color.white : new Color(0.08f, 0.09f, 0.1f, 0.95f);
            Handles.DrawSolidDisc(center, UnityVector3.forward, radius + 2f);
            Handles.color = row.Color;
            Handles.DrawSolidDisc(center, UnityVector3.forward, radius);
            GUI.Label(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f),
                (col + 1).ToString(), _numberStyle);
        }

        private void HandlePreviewInput(Rect preview, int control)
        {
            Event evt = Event.current;
            switch (evt.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !preview.Contains(evt.mousePosition)) return;
                    int nearest = ClosestHandle(preview, evt.mousePosition);
                    if (nearest < 0) return;
                    GUI.FocusControl(null);
                    _selectedIndex = nearest;
                    _dragControl = control;
                    GUIUtility.hotControl = control;
                    Row row = GetRow(nearest);
                    int slot = row.FirstSlot + nearest % SlotsPerRow;
                    _dragOffset = ToDesign(preview, evt.mousePosition) - _layout.GetPosition(row.TeamIsMine, slot);
                    evt.Use();
                    Repaint();
                    break;
                case EventType.MouseDrag:
                    if (_dragControl != control || GUIUtility.hotControl != control) return;
                    MoveSelectedHandle(preview, evt.mousePosition);
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (_dragControl != control) return;
                    FinishDrag();
                    evt.Use();
                    break;
            }
        }

        private int ClosestHandle(Rect preview, Vector2 mousePosition)
        {
            int best = -1;
            float bestDistance = HitRadius * HitRadius;
            for (int index = 0; index < SlotLabels.Length; index++)
            {
                Row row = GetRow(index);
                int slot = row.FirstSlot + index % SlotsPerRow;
                Vector2 center = ToPreview(preview, _layout.GetPosition(row.TeamIsMine, slot));
                float distance = (center - mousePosition).sqrMagnitude;
                if (distance < bestDistance || (distance == bestDistance && index == _selectedIndex))
                {
                    best = index;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void MoveSelectedHandle(Rect preview, Vector2 mousePosition)
        {
            Row row = GetRow(_selectedIndex);
            int slot = row.FirstSlot + _selectedIndex % SlotsPerRow;
            Vector2 next = ClampToDesign(ToDesign(preview, mousePosition) - _dragOffset);
            if (next == _layout.GetPosition(row.TeamIsMine, slot)) return;
            if (!_dragChanged)
            {
                Undo.RegisterCompleteObjectUndo(_layout, EditUndoName);
                _dragChanged = true;
            }
            WritePosition(row, slot, next);
            Repaint();
        }

        private void FinishDrag()
        {
            if (_dragControl == 0) return;
            if (GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
            _dragControl = 0;
            if (_dragChanged) SaveLayout();
            _dragChanged = false;
        }

        private void WritePosition(Row row, int slot, Vector2 point)
        {
            _layout.SetPosition(row.TeamIsMine, slot, point);
            EditorUtility.SetDirty(_layout);
        }

        private void SaveLayout()
        {
            if (_layout != null) AssetDatabase.SaveAssetIfDirty(_layout);
        }

        private static Row GetRow(int index) => Rows[index / SlotsPerRow];
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Vector2 ToPreview(Rect preview, Vector2 point) => new Vector2(
            preview.x + point.x * preview.width / DesignWidth,
            preview.y + point.y * preview.height / DesignHeight);

        private static Vector2 ToDesign(Rect preview, Vector2 point) => new Vector2(
            (point.x - preview.x) * DesignWidth / preview.width,
            (point.y - preview.y) * DesignHeight / preview.height);

        private static Vector2 ClampToDesign(Vector2 point) => new Vector2(
            Mathf.Clamp(point.x, 0f, DesignWidth), Mathf.Clamp(point.y, 0f, DesignHeight));

        private static string[] CreateSlotLabels()
        {
            var labels = new string[Rows.Length * SlotsPerRow];
            for (int index = 0; index < labels.Length; index++)
                labels[index] = GetRow(index).Label + " " + (index % SlotsPerRow + 1);
            return labels;
        }

        private readonly struct Row
        {
            public readonly bool TeamIsMine;
            public readonly int FirstSlot;
            public readonly string Label;
            public readonly Color Color;

            public Row(bool teamIsMine, int firstSlot, string label, Color color)
            {
                TeamIsMine = teamIsMine;
                FirstSlot = firstSlot;
                Label = label;
                Color = color;
            }
        }
    }
}
