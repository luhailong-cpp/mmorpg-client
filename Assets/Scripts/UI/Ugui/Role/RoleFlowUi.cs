using System.Collections;
using System.Collections.Generic;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Role
{
    /// <summary>
    /// 选角/建角界面。挂接 <see cref="GameClient.PlayerChooser"/>:EnterZone 管线在
    /// TCP Login 拿到角色列表后暂停,把「选中区过滤后的角色列表」交给本界面;
    /// 玩家选择已有角色、或选职业+性别创建新角色后,管线继续 EnterGame。
    ///
    /// 与 BattleUiRoot 同一路子:纯代码构建、自建 Canvas(sortingOrder 150,
    /// 压在选服屏 100 之上、战斗层 200 之下),不依赖烘焙 prefab。
    /// </summary>
    public sealed class RoleFlowUi : MonoBehaviour
    {
        public static RoleFlowUi Instance { get; private set; }

        // 客户端展示用职业清单;id 必须存在于服务端 Class 配表(data/Class.xlsx,id 1-9),
        // 服务端 createplayerlogic 会按配表校验,不合法直接拒绝。
        private static readonly (uint id, string name, string desc)[] Classes =
        {
            (1u, "剑修", "近战爆发"),
            (2u, "法修", "远程法术"),
            (3u, "丹修", "治疗辅助"),
            (4u, "体修", "坚韧防御"),
        };

        private const int MaxRows = 5; // 与服务端 Account.MaxPlayersPerAccount 默认值对齐

        private GameObject _canvasGo;
        private RectTransform _designRoot;
        private RectTransform _panel;
        private TMP_Text _title;

        private RectTransform _selectRoot;
        private RectTransform _rowContainer;
        private UiTextButton _selectBackButton;
        private UiTextButton _gotoCreateButton;

        private RectTransform _createRoot;
        private readonly List<Image> _classPlates = new();
        private Image _malePlate;
        private Image _femalePlate;
        private UiTextButton _createBackButton;
        private UiTextButton _confirmCreateButton;

        private static readonly Color PlateNormal = new(0.35f, 0.25f, 0.14f, 0.95f);   // Brown 系
        private static readonly Color PlateSelected = new(0.61f, 0.17f, 0.10f, 0.98f); // SelectedRed 系

        // 一次 Choose 的会话状态
        private uint _zoneId;
        private IReadOnlyList<AccountSimplePlayer> _players;
        private bool _resolved;
        private GameClient.PlayerChoice _result;
        private uint _pickedClassId = 1;
        private uint _pickedGender = 1;

        /// <summary>由 AppBootstrap 在 GameClient 创建后调用,建界面并挂接选角钩子。</summary>
        public static RoleFlowUi Attach(AppBootstrap app)
        {
            if (Instance == null)
            {
                var go = new GameObject("[RoleUi]");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<RoleFlowUi>();
                Instance.BuildCanvas();
            }
            if (app != null && app.GameClient != null)
                app.GameClient.PlayerChooser = Instance.Choose;
            return Instance;
        }

        // ── GameClient.PlayerChooser 入口 ──────────────────────────────

        private IEnumerator Choose(uint zoneId, IReadOnlyList<AccountSimplePlayer> players,
                                   GameClient.PlayerChoice choice)
        {
            _zoneId = zoneId;
            _players = players ?? new List<AccountSimplePlayer>();
            _resolved = false;
            _result = choice;
            _pickedClassId = Classes[0].id;
            _pickedGender = 1;

            _canvasGo.SetActive(true);
            if (_players.Count > 0) ShowSelectMode();
            else ShowCreateMode();

            while (!_resolved) yield return null;

            _canvasGo.SetActive(false);
        }

        private void ResolveSelect(ulong playerId)
        {
            if (_resolved) return;
            _result.SelectedPlayerId = playerId;
            _result.CreateNew = false;
            _result.Cancelled = false;
            _resolved = true;
        }

        private void ResolveCreate()
        {
            if (_resolved) return;
            _result.CreateNew = true;
            _result.ClassId = _pickedClassId;
            _result.Gender = _pickedGender;
            _result.Cancelled = false;
            _resolved = true;
        }

        private void ResolveCancel()
        {
            if (_resolved) return;
            _result.Cancelled = true;
            _resolved = true;
        }

        // ── 两个模式 ───────────────────────────────────────────────

        private void ShowSelectMode()
        {
            _title.text = $"选择角色 · {_zoneId} 区";
            _createRoot.gameObject.SetActive(false);
            _selectRoot.gameObject.SetActive(true);
            RebuildRows();
            _gotoCreateButton.SetVisible(_players.Count < MaxRows);
        }

        private void ShowCreateMode()
        {
            _title.text = $"创建角色 · {_zoneId} 区";
            _selectRoot.gameObject.SetActive(false);
            _createRoot.gameObject.SetActive(true);
            // 返回键:该区已有角色 → 回到选角;没有 → 取消回选服
            _createBackButton.SetText(_players.Count > 0 ? "返回选角" : "返回选服");
            RefreshCreateHighlights();
        }

        private void RebuildRows()
        {
            for (int i = _rowContainer.childCount - 1; i >= 0; i--)
                Destroy(_rowContainer.GetChild(i).gameObject);

            ulong lastPlayed = ClientSettings.GetLastPlayer(_zoneId);
            int count = Mathf.Min(_players.Count, MaxRows);
            for (int i = 0; i < count; i++)
            {
                var p = _players[i];
                float y = i * 104f;
                var row = BattleUiWidgets.CreateTextButton($"Role{i}", _rowContainer,
                    0f, y, 860f, 92f, DescribePlayer(p, lastPlayed == p.PlayerId), 30f,
                    PlateNormal, new Color(1f, 0.96f, 0.83f));
                ulong capturedId = p.PlayerId;
                row.Button.onClick.AddListener(() => ResolveSelect(capturedId));
            }
        }

        private static string DescribePlayer(AccountSimplePlayer p, bool isLast)
        {
            string cls = "无名散修";
            foreach (var c in Classes)
                if (c.id == p.ClassId) { cls = c.name; break; }
            string gender = p.Gender == 2 ? "女" : p.Gender == 1 ? "男" : "-";
            string tail = p.PlayerId.ToString();
            if (tail.Length > 6) tail = tail.Substring(tail.Length - 6);
            string label = $"{cls} · {gender} · ID …{tail}";
            if (isLast) label += "   [上次]";
            return label;
        }

        private void RefreshCreateHighlights()
        {
            for (int i = 0; i < _classPlates.Count; i++)
                _classPlates[i].color = Classes[i].id == _pickedClassId ? PlateSelected : PlateNormal;
            _malePlate.color = _pickedGender == 1 ? PlateSelected : PlateNormal;
            _femalePlate.color = _pickedGender == 2 ? PlateSelected : PlateNormal;
        }

        // ── Canvas 构建(参数与 QdaoUguiRuntime / BattleUiRoot 保持一致) ──

        private void BuildCanvas()
        {
            EnsureEventSystem();

            _canvasGo = new GameObject(
                "[RoleUgui]",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);

            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150; // 选服屏 100 < 本层 < 战斗层 200
            canvas.pixelPerfect = true;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            BattleUiWidgets.CreateStretchPanel("Dim", _canvasGo.transform,
                new Color(0f, 0f, 0f, 0.62f));

            _designRoot = CreateDesignRoot("DesignRoot", _canvasGo.transform);

            // 主面板:居中 1100x800
            _panel = QdaoUguiFactory.CreateCenteredRect("Panel", _designRoot, 1100f, 800f);
            var panelBg = _panel.gameObject.AddComponent<Image>();
            panelBg.color = new Color(0.945f, 0.902f, 0.847f, 0.98f); // PanelPaper 系
            panelBg.raycastTarget = true;

            _title = QdaoUguiFactory.CreateText("Title", _panel, 0f, 36f, 1100f, 64f,
                string.Empty, 44f, new Color(0.24f, 0.16f, 0.08f), TextAlignmentOptions.Center);

            BuildSelectRoot();
            BuildCreateRoot();

            _canvasGo.SetActive(false);
        }

        private void BuildSelectRoot()
        {
            _selectRoot = QdaoUguiFactory.CreateRect("SelectRoot", _panel, 0f, 0f, 1100f, 800f);
            _rowContainer = QdaoUguiFactory.CreateRect("Rows", _selectRoot, 120f, 140f, 860f, 520f);

            _selectBackButton = BattleUiWidgets.CreateTextButton("Back", _selectRoot,
                120f, 692f, 240f, 72f, "返回选服", 28f, PlateNormal, new Color(1f, 0.96f, 0.83f));
            _selectBackButton.Button.onClick.AddListener(ResolveCancel);

            _gotoCreateButton = BattleUiWidgets.CreateTextButton("GotoCreate", _selectRoot,
                740f, 692f, 240f, 72f, "创建新角色", 28f, PlateSelected, new Color(1f, 0.96f, 0.83f));
            _gotoCreateButton.Button.onClick.AddListener(ShowCreateMode);
        }

        private void BuildCreateRoot()
        {
            _createRoot = QdaoUguiFactory.CreateRect("CreateRoot", _panel, 0f, 0f, 1100f, 800f);

            QdaoUguiFactory.CreateText("ClassLabel", _createRoot, 120f, 128f, 400f, 44f,
                "选择职业", 32f, new Color(0.24f, 0.16f, 0.08f));

            _classPlates.Clear();
            for (int i = 0; i < Classes.Length; i++)
            {
                var def = Classes[i];
                float x = 120f + i * 230f;
                var plate = BattleUiWidgets.CreatePanel($"Class{def.id}", _createRoot,
                    x, 190f, 200f, 230f, PlateNormal);
                var button = plate.gameObject.AddComponent<Button>();
                BattleUiWidgets.ConfigureButtonVisual(button, plate);
                QdaoUguiFactory.CreateText("Name", plate.transform, 0f, 46f, 200f, 56f,
                    def.name, 42f, new Color(1f, 0.96f, 0.83f), TextAlignmentOptions.Center);
                QdaoUguiFactory.CreateText("Desc", plate.transform, 0f, 128f, 200f, 40f,
                    def.desc, 24f, new Color(1f, 0.96f, 0.83f, 0.85f), TextAlignmentOptions.Center);
                uint capturedId = def.id;
                button.onClick.AddListener(() =>
                {
                    _pickedClassId = capturedId;
                    RefreshCreateHighlights();
                });
                _classPlates.Add(plate);
            }

            QdaoUguiFactory.CreateText("GenderLabel", _createRoot, 120f, 470f, 400f, 44f,
                "选择性别", 32f, new Color(0.24f, 0.16f, 0.08f));

            _malePlate = BattleUiWidgets.CreatePanel("GenderMale", _createRoot,
                120f, 530f, 160f, 72f, PlateNormal);
            var maleButton = _malePlate.gameObject.AddComponent<Button>();
            BattleUiWidgets.ConfigureButtonVisual(maleButton, _malePlate);
            QdaoUguiFactory.CreateText("Text", _malePlate.transform, 0f, 0f, 160f, 72f,
                "男", 32f, new Color(1f, 0.96f, 0.83f), TextAlignmentOptions.Center);
            maleButton.onClick.AddListener(() => { _pickedGender = 1; RefreshCreateHighlights(); });

            _femalePlate = BattleUiWidgets.CreatePanel("GenderFemale", _createRoot,
                310f, 530f, 160f, 72f, PlateNormal);
            var femaleButton = _femalePlate.gameObject.AddComponent<Button>();
            BattleUiWidgets.ConfigureButtonVisual(femaleButton, _femalePlate);
            QdaoUguiFactory.CreateText("Text", _femalePlate.transform, 0f, 0f, 160f, 72f,
                "女", 32f, new Color(1f, 0.96f, 0.83f), TextAlignmentOptions.Center);
            femaleButton.onClick.AddListener(() => { _pickedGender = 2; RefreshCreateHighlights(); });

            _createBackButton = BattleUiWidgets.CreateTextButton("Back", _createRoot,
                120f, 692f, 240f, 72f, "返回", 28f, PlateNormal, new Color(1f, 0.96f, 0.83f));
            _createBackButton.Button.onClick.AddListener(() =>
            {
                if (_players != null && _players.Count > 0) ShowSelectMode();
                else ResolveCancel();
            });

            _confirmCreateButton = BattleUiWidgets.CreateTextButton("Confirm", _createRoot,
                700f, 692f, 280f, 72f, "创建并进入", 30f, PlateSelected, new Color(1f, 0.96f, 0.83f));
            _confirmCreateButton.Button.onClick.AddListener(ResolveCreate);
        }

        private static RectTransform CreateDesignRoot(string name, UnityEngine.Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            return rect;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("[EventSystem]", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }
    }
}
