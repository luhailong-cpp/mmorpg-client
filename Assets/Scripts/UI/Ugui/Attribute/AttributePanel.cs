using System.Collections.Generic;
using MmorpgClient.Game.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>
    /// Native character window matching the approved affinity-free painted UI.
    /// All values, pool limits, schemes and allocation actions remain server-authoritative.
    /// Only pending allocation deltas live locally until the player confirms.
    /// </summary>
    public sealed class AttributePanel
    {
        private const float RowHeight = 76f;
        private const float RowGap = 32f;
        private const int MaxRows = 8;

        private readonly AttributeUiRoot _owner;
        private readonly RectTransform _root;
        private UiTextButton _schemeButton;
        private RectTransform _schemeDropdown;
        private readonly List<UiTextButton> _schemeOptions = new();
        private readonly TMP_Text[] _statValues = new TMP_Text[6];
        private readonly TMP_Text[] _overviewValues = new TMP_Text[6];
        private UiTextButton _createSchemeButton;
        private readonly List<UiTextButton> _tabs = new();
        private readonly List<uint> _tabPoolIds = new();
        private TMP_Text _remainText;
        private UiTextButton _autoButton;
        private readonly UiPointRow[] _rows = new UiPointRow[MaxRows];
        private UiTextButton _resetButton;
        private UiTextButton _confirmButton;
        private TMP_Text _statusText;
        private RectTransform _allocationRoot;
        private RectTransform _overviewRoot;
        private RectTransform _rowsRoot;
        private UiTextButton _attributeNavigation;
        private UiTextButton _allocationNavigation;

        private readonly RectTransform _tooltip;
        private readonly TMP_Text _tooltipText;
        private AttributePanelInfo _panel;
        private uint _activePoolId;
        private bool _schemeListOpen;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public AttributePanel(AttributeUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;
            var window = BattleUiWidgets.CreatePanel("AttributeWindow", parent,
                CharacterAttributeVisuals.WindowX, CharacterAttributeVisuals.WindowY,
                CharacterAttributeVisuals.WindowW, CharacterAttributeVisuals.WindowH, AttributeUiStyle.WindowPaper);
            CharacterAttributeVisuals.Skin(window, "window_frame");
            _root = (RectTransform)window.transform;

            float titleX = (CharacterAttributeVisuals.WindowW - 820f) * 0.5f;
            CharacterAttributeVisuals.Panel("TitlePlate", _root, titleX, -64f, 820f, 138f, "title_plate");
            CharacterAttributeVisuals.Panel("Title", _root,
                (CharacterAttributeVisuals.WindowW - 288f) * 0.5f, -2f, 288f, 62f, "title_character");
            CharacterAttributeVisuals.Panel("CloseTassel", _root,
                CharacterAttributeVisuals.WindowW + 36f, 24f, 32f, 80f, "close_tassel");
            var close = BattleUiWidgets.CreateTextButton("Close", _root,
                CharacterAttributeVisuals.WindowW - 36f, -24f, 88f, 88f, string.Empty, 48f,
                AttributeUiStyle.ClosePlate, AttributeUiStyle.CloseText);
            CharacterAttributeVisuals.Skin(close.Plate, "close_button");
            close.Label.gameObject.SetActive(false);
            close.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            close.Button.onClick.AddListener(Hide);

            BuildLeftColumn();
            BuildRightColumn();
            BuildSideNavigation();

            _tooltip = QdaoUguiFactory.CreateRect("Tooltip", _root, 0f, 0f, 680f, 76f);
            var tooltipPlate = _tooltip.gameObject.AddComponent<Image>();
            tooltipPlate.color = AttributeUiStyle.TooltipPlate;
            tooltipPlate.raycastTarget = false;
            _tooltipText = QdaoUguiFactory.CreateText("TooltipText", _tooltip, 20f, 0f, 640f, 76f,
                string.Empty, 28f, AttributeUiStyle.TooltipText);
            _tooltip.gameObject.SetActive(false);
            Hide();
        }

        private void BuildLeftColumn()
        {
            float x = CharacterAttributeVisuals.LeftX;
            float w = CharacterAttributeVisuals.LeftW;
            _schemeButton = CharacterAttributeVisuals.Button("SchemeButton", _root, x, 76f, w, 88f,
                "方案一");
            CharacterAttributeVisuals.Skin(_schemeButton.Plate, "button_scheme");
            CharacterAttributeVisuals.Panel("SchemeArrow", _schemeButton.Rect,
                w - 66f, 32f, 36f, 27f, "dropdown_arrow");
            _schemeButton.Button.onClick.AddListener(ToggleSchemeList);
            _schemeDropdown = QdaoUguiFactory.CreateRect("SchemeDropdown", _root, x, 166f, w, 0f);
            _schemeDropdown.gameObject.SetActive(false);

            string[] labels = { "气 血", "法 力", "物 伤", "法 伤", "速 度", "防 御" };
            for (int i = 0; i < labels.Length; i++)
                _statValues[i] = CharacterAttributeVisuals.Stat($"Stat{i}", _root,
                    x, 194f + i * 88f, w, labels[i]);

            _createSchemeButton = CharacterAttributeVisuals.Button("CreateScheme", _root,
                x, 792f, w, 96f, "开启新方案", true);
            _createSchemeButton.Button.onClick.AddListener(OnCreateSchemeClicked);
            _createSchemeButton.SetInteractable(false);

            CharacterAttributeVisuals.Panel("ColumnDivider", _root, 643f, 92f, 40f, 776f, "divider");
        }

        private void BuildRightColumn()
        {
            float x = CharacterAttributeVisuals.RightX;
            float w = CharacterAttributeVisuals.RightW;
            _allocationRoot = QdaoUguiFactory.CreateRect("AllocationPage", _root, 0f, 0f,
                CharacterAttributeVisuals.WindowW, CharacterAttributeVisuals.WindowH);

            // The latest accepted image has one attribute-points tab; no affinity page.
            var tab = CharacterAttributeVisuals.Button("AttributePoolTab", _allocationRoot,
                x, 98f, 356f, 80f, "属性点", true);
            CharacterAttributeVisuals.Skin(tab.Plate, "tab_horizontal");
            tab.Button.onClick.AddListener(() => OnTabClicked(0));
            _tabs.Add(tab);
            _tabPoolIds.Add(0);

            CharacterAttributeVisuals.Panel("RemainNotice", _allocationRoot, x, 221f, 47f, 47f, "notice_icon");
            _remainText = QdaoUguiFactory.CreateText("Remain", _allocationRoot, x + 64f, 210f, w - 364f, 68f,
                "正在读取属性…", 34f, AttributeUiStyle.RemainText);
            _autoButton = CharacterAttributeVisuals.Button("Auto", _allocationRoot,
                x + w - 276f, 204f, 276f, 80f, "自动加点", false, 30f);
            _autoButton.Button.onClick.AddListener(OnAutoClicked);

            // Extra server-defined dimensions scroll within the same four-row visual space.
            var viewport = QdaoUguiFactory.CreateRect("PointsViewport", _allocationRoot, x, 304f, w, 418f);
            var viewportHit = viewport.gameObject.AddComponent<Image>();
            viewportHit.color = new Color(1f, 1f, 1f, 0.001f);
            viewportHit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            _rowsRoot = QdaoUguiFactory.CreateRect("PointsRows", viewport, 0f, 0f, w, 418f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _rowsRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = CharacterAttributeVisuals.PointRow($"Row{i}", _rowsRoot,
                    0f, i * (RowHeight + RowGap), w, RowHeight);
                _rows[i].Bind(OnRowChanged);
                AttachRowTooltip(_rows[i]);
                CharacterAttributeScrollFocus.Bind(_rows[i].Minus, scroll, _rows[i].Rect);
                CharacterAttributeScrollFocus.Bind(_rows[i].Slider, scroll, _rows[i].Rect);
                CharacterAttributeScrollFocus.Bind(_rows[i].Plus, scroll, _rows[i].Rect);
                _rows[i].Rect.gameObject.SetActive(false);
            }

            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root, x, 726f,
                w, 60f, string.Empty, 26f, AttributeUiStyle.HintText);
            _resetButton = CharacterAttributeVisuals.Button("Reset", _allocationRoot,
                x + 44f, 792f, 356f, 96f, "重 置");
            _resetButton.Button.onClick.AddListener(OnResetClicked);
            _confirmButton = CharacterAttributeVisuals.Button("Confirm", _allocationRoot,
                x + w - 590f, 792f, 488f, 96f, "确认加点", true, 38f);
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
            _autoButton.SetInteractable(false);
            _resetButton.SetInteractable(false);
            _confirmButton.SetInteractable(false);

            _overviewRoot = QdaoUguiFactory.CreateRect("OverviewPage", _root, x, 104f, w, 602f);
            QdaoUguiFactory.CreateText("OverviewHeading", _overviewRoot, 0f, 0f, w, 88f,
                "属 性 总 览", 40f, QdaoRefreshArt.Ink);
            string[] labels = { "气 血", "法 力", "物 伤", "法 伤", "速 度", "防 御" };
            for (int i = 0; i < labels.Length; i++)
                _overviewValues[i] = CharacterAttributeVisuals.Stat($"OverviewStat{i}", _overviewRoot,
                    (i % 2) * 602f, 152f + (i / 2) * 126f, 570f, labels[i]);
            _overviewRoot.gameObject.SetActive(false);
        }

        private void BuildSideNavigation()
        {
            _attributeNavigation = CharacterAttributeVisuals.SideTab("AttributeTab", _root, 116f, "属\n性", false);
            _allocationNavigation = CharacterAttributeVisuals.SideTab("AllocationTab", _root, 302f, "加\n点", true);
            var skill = CharacterAttributeVisuals.SideTab("SkillTab", _root, 488f, "技\n能", false);
            // The attribute protocol has no skills-page response or action to bind.
            skill.SetInteractable(false);
            _attributeNavigation.Button.onClick.AddListener(() => ShowAllocationPage(false));
            _allocationNavigation.Button.onClick.AddListener(() => ShowAllocationPage(true));
        }

        private void ShowAllocationPage(bool allocation)
        {
            _allocationRoot.gameObject.SetActive(allocation);
            _overviewRoot.gameObject.SetActive(!allocation);
            CharacterAttributeVisuals.SelectSideTab(_attributeNavigation, !allocation);
            CharacterAttributeVisuals.SelectSideTab(_allocationNavigation, allocation);
            HideTooltip();
        }

        /// <summary>给一行挂 hover 事件:显示服务器下发的维度说明(截图里的黑底提示条)。</summary>
        private void AttachRowTooltip(UiPointRow row)
        {
            if (row.Hover == null) return;
            row.Hover.OnEnter = () => ShowTooltipFor(row);
            row.Hover.OnExit = HideTooltip;
        }

        // ── 显隐 ────────────────────────────────────────────

        public void Show()
        {
            // 宝宝窗与本窗同位置同尺寸,必须互斥(见 AttributeUiRoot.HidePanel)
            Pet.PetUiRoot.Instance?.HidePanel();
            if (_root != null) _root.gameObject.SetActive(true);
            CloseSchemeList();
            HideTooltip();
            SetStatus(string.Empty);
            _owner.Client?.RequestPanel();
        }

        public void Hide()
        {
            CloseSchemeList();
            HideTooltip();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        public void SetStatus(string value, bool warn = false)
        {
            if (_statusText == null) return;
            _statusText.text = value ?? string.Empty;
            _statusText.color = warn ? AttributeUiStyle.WarnText : AttributeUiStyle.HintText;
        }

        // ── 数据应用 ────────────────────────────────────────

        /// <summary>服务器面板到达:整体重建界面状态,丢弃所有未提交增量。</summary>
        public void ApplyPanel(AttributePanelInfo panel)
        {
            _panel = panel;
            if (panel == null) return;

            if (_activePoolId == 0 || FindPool(_activePoolId) == null)
            {
                _activePoolId = FindAttributePool()?.PoolId ?? 0;
            }

            RefreshSchemeButton();
            RefreshDerived();
            RefreshTabs();
            RebuildRows();
            RefreshActionButtons();
        }

        /// <summary>在途状态变化:按钮与加点行一起进/出 Busy(行控件只锁在 Busy 期间,失败回包后必须解锁)。</summary>
        public void ApplyBusy(bool busy)
        {
            RefreshRowRanges();
            RefreshActionButtons();
        }

        /// <summary>自动加点建议:只填本地待提交值,玩家点「确认」才落。</summary>
        public void ApplyAutoSuggestion(uint poolId, IReadOnlyDictionary<uint, uint> suggested)
        {
            if (poolId != _activePoolId)
            {
                return;
            }
            // 推荐是按服务器权威状态算的:先把本地未提交增量全部回落(否则各行滑条还留着上一次
            // 互相挤占后的旧上界,推荐值会被夹成错的),再从 committed 起写入
            RebuildRows();
            foreach (var row in ActiveRows())
            {
                if (suggested.TryGetValue(row.DimensionId, out uint target))
                {
                    row.SetPending(target);
                }
            }
            RefreshRowRanges();
            RefreshRemainText();
            RefreshActionButtons();
            SetStatus("已按推荐方案预分配,点「确认」生效。");
        }

        private void RefreshSchemeButton()
        {
            if (_panel == null) return;
            var name = "方案一";
            foreach (var scheme in _panel.Schemes)
            {
                if (scheme.SchemeId == _panel.ActiveSchemeId)
                {
                    name = scheme.Name;
                    break;
                }
            }
            _schemeButton?.SetText(name);
            _schemeButton?.SetInteractable(!(_owner.Client?.Busy ?? false));
            bool canCreate = _panel.Schemes.Count < _panel.MaxSchemes;
            _createSchemeButton?.SetInteractable(canCreate && !(_owner.Client?.Busy ?? false));
            _createSchemeButton?.SetText(canCreate && _panel.CreateSchemeCostGold > 0
                ? $"开启新方案({_panel.CreateSchemeCostGold} 金)"
                : "开启新方案");
        }

        private void RefreshDerived()
        {
            var derived = _panel.Derived;
            if (derived == null) return;
            // 气血/法力显示「当前/上限」,其余显示单值(与截图一致的六项)
            SetStat(0, $"{derived.Health}/{derived.MaxHealth}");
            SetStat(1, $"{derived.Mana}/{derived.MaxMana}");
            SetStat(2, derived.PhysicalAttack.ToString());
            SetStat(3, derived.MagicAttack.ToString());
            SetStat(4, derived.Speed.ToString());
            SetStat(5, derived.Defense.ToString());
        }

        private void SetStat(int index, string value)
        {
            if (index >= 0 && index < _statValues.Length && _statValues[index] != null)
            {
                _statValues[index].text = value;
                _overviewValues[index].text = value;
            }
        }

        private AttributePoolInfo FindAttributePool()
        {
            if (_panel == null) return null;
            foreach (var pool in _panel.Pools)
                if (!string.IsNullOrEmpty(pool.Name) && pool.Name.Contains("属性")) return pool;
            foreach (var pool in _panel.Pools)
                if (string.IsNullOrEmpty(pool.Name) ||
                    (!pool.Name.Contains("相性") && !pool.Name.Contains("仙魔"))) return pool;
            return null;
        }

        private void RefreshTabs()
        {
            var pool = FindPool(_activePoolId);
            _tabPoolIds[0] = pool?.PoolId ?? 0;
            _tabs[0].SetText(pool == null ? "属性点" :
                pool.Unlocked ? pool.Name : $"{pool.Name}({pool.UnlockLevel}级)");
            _tabs[0].SetInteractable(pool != null && pool.Unlocked);
            CharacterAttributeVisuals.Skin(_tabs[0].Plate, "tab_horizontal");
            _tabs[0].Label.color = pool != null && !pool.Unlocked ? AttributeUiStyle.TabLockedText : QdaoRefreshArt.Ivory;
        }

        private void RebuildRows()
        {
            var pool = FindPool(_activePoolId);
            bool interactable = pool != null && pool.Unlocked && !(_owner.Client?.Busy ?? false);
            int index = 0;
            foreach (var dimension in _panel.Dimensions)
            {
                if (dimension.PoolId != _activePoolId) continue;
                if (index >= MaxRows) break;
                var row = _rows[index];
                row.Rect.gameObject.SetActive(true);
                row.SetFromServer(dimension.DimensionId, dimension.Name, dimension.Value,
                    dimension.Allocated, dimension.Cap, pool?.Remaining ?? 0, interactable);
                row.Rect.name = $"Row_{dimension.Name}";
                index++;
            }
            for (int i = index; i < MaxRows; i++)
            {
                _rows[i].Rect.gameObject.SetActive(false);
            }
            _rowsRoot.sizeDelta = new Vector2(CharacterAttributeVisuals.RightW,
                Mathf.Max(418f, index * (RowHeight + RowGap) - RowGap));
            _rowsRoot.anchoredPosition = Vector2.zero;
            RefreshRemainText();
        }

        private IEnumerable<UiPointRow> ActiveRows()
        {
            foreach (var row in _rows)
            {
                if (row.Rect != null && row.Rect.gameObject.activeSelf) yield return row;
            }
        }

        private uint PendingDelta()
        {
            uint delta = 0;
            foreach (var row in ActiveRows())
            {
                delta += row.Pending - row.Committed;
            }
            return delta;
        }

        private uint RemainingAfterPending()
        {
            var pool = FindPool(_activePoolId);
            if (pool == null) return 0;
            uint delta = PendingDelta();
            return pool.Remaining > delta ? pool.Remaining - delta : 0;
        }

        private void RefreshRowRanges()
        {
            var pool = FindPool(_activePoolId);
            if (pool == null) return;
            uint left = RemainingAfterPending();
            bool interactable = pool.Unlocked && !(_owner.Client?.Busy ?? false);
            foreach (var row in ActiveRows())
            {
                row.ApplyRange(FindCap(row.DimensionId), left, interactable);
            }
        }

        private void RefreshRemainText()
        {
            var pool = FindPool(_activePoolId);
            if (_remainText == null) return;
            if (pool == null) { _remainText.text = "暂无可分配属性点"; return; }
            uint left = RemainingAfterPending();
            string poolName = string.IsNullOrEmpty(pool.Name) ? "点数" : pool.Name;
            if (!pool.Unlocked)
            {
                _remainText.text = $"{poolName} 需 {pool.UnlockLevel} 级解锁";
                return;
            }
            uint delta = PendingDelta();
            _remainText.text = delta > 0
                ? $"剩余{poolName} {left}(待分配 {delta})"
                : $"剩余{poolName} {left}";
        }

        private void RefreshActionButtons()
        {
            var pool = FindPool(_activePoolId);
            bool busy = _owner.Client?.Busy ?? false;
            bool unlocked = pool != null && pool.Unlocked;
            uint delta = PendingDelta();

            _confirmButton?.SetInteractable(!busy && unlocked && delta > 0);
            _autoButton?.SetInteractable(!busy && unlocked && (pool?.Remaining ?? 0) > 0);
            _resetButton?.SetInteractable(!busy && unlocked);
            _resetButton?.SetText(pool != null && pool.ResetCostGold > 0 ? $"重置({pool.ResetCostGold} 金)" : "重 置");
            RefreshSchemeButton();
        }

        private AttributePoolInfo FindPool(uint poolId)
        {
            if (_panel == null) return null;
            foreach (var pool in _panel.Pools)
            {
                if (pool.PoolId == poolId) return pool;
            }
            return null;
        }

        private uint FindCap(uint dimensionId)
        {
            if (_panel == null) return 0;
            foreach (var dimension in _panel.Dimensions)
            {
                if (dimension.DimensionId == dimensionId) return dimension.Cap;
            }
            return 0;
        }

        private string FindDesc(uint dimensionId)
        {
            if (_panel == null) return string.Empty;
            foreach (var dimension in _panel.Dimensions)
            {
                if (dimension.DimensionId == dimensionId) return dimension.Desc;
            }
            return string.Empty;
        }

        // ── 交互 ────────────────────────────────────────────

        private void OnRowChanged(UiPointRow row)
        {
            RefreshRowRanges();
            RefreshRemainText();
            RefreshActionButtons();
        }

        private void OnTabClicked(int index)
        {
            if (index < 0 || index >= _tabPoolIds.Count) return;
            uint poolId = _tabPoolIds[index];
            if (poolId == 0 || poolId == _activePoolId) return;
            if (PendingDelta() > 0)
            {
                SetStatus("有未确认的加点,请先「确认」或「重置」。", true);
                return;
            }
            _activePoolId = poolId;
            SetStatus(string.Empty);
            RefreshTabs();
            RebuildRows();
            RefreshActionButtons();
        }

        private void OnConfirmClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("属性模块未就绪", true); return; }
            var target = new Dictionary<uint, uint>();
            foreach (var row in ActiveRows())
            {
                if (row.Pending != row.Committed)
                {
                    target[row.DimensionId] = row.Pending;
                }
            }
            if (target.Count == 0)
            {
                SetStatus("没有需要提交的加点。", true);
                return;
            }
            client.Allocate(_activePoolId, target);
            SetStatus("已提交,等待服务器确认…");
        }

        private void OnResetClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("属性模块未就绪", true); return; }
            if (PendingDelta() > 0)
            {
                // 先撤未提交的本地增量,不必花钱洗点
                RebuildRows();
                RefreshActionButtons();
                SetStatus("已撤销未确认的加点。");
                return;
            }
            client.ResetPool(_activePoolId);
            SetStatus("已请求重置,等待服务器确认…");
        }

        private void OnAutoClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("属性模块未就绪", true); return; }
            client.AutoAllocate(_activePoolId);
            SetStatus("正在计算推荐方案…");
        }

        private void OnCreateSchemeClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("属性模块未就绪", true); return; }
            if (PendingDelta() > 0)
            {
                SetStatus("有未确认的加点,请先「确认」或「重置」。", true);
                return;
            }
            client.CreateScheme(string.Empty); // 空名 = 服务器按「方案N」命名
            SetStatus("正在开启新方案…");
        }

        private void ToggleSchemeList()
        {
            if (_schemeListOpen) { CloseSchemeList(); return; }
            if (_panel == null || _panel.Schemes.Count == 0) return;

            foreach (var option in _schemeOptions)
            {
                Object.Destroy(option.Rect.gameObject);
            }
            _schemeOptions.Clear();

            float w = CharacterAttributeVisuals.LeftW;
            for (int i = 0; i < _panel.Schemes.Count; i++)
            {
                var scheme = _panel.Schemes[i];
                uint schemeId = scheme.SchemeId;
                var option = CharacterAttributeVisuals.Button($"SchemeOption{i}", _schemeDropdown,
                    0f, i * 84f, w, 80f, scheme.Name, schemeId == _panel.ActiveSchemeId, 30f);
                option.Button.onClick.AddListener(() => OnSchemeChosen(schemeId));
                _schemeOptions.Add(option);
            }
            _schemeDropdown.sizeDelta = new Vector2(w, _panel.Schemes.Count * 84f);
            // 下拉建在六项属性栏之前(兄弟序更早),展开时置顶,否则被不透明的 Stat 底板盖住
            _schemeDropdown.SetAsLastSibling();
            _schemeDropdown.gameObject.SetActive(true);
            _schemeListOpen = true;
        }

        private void CloseSchemeList()
        {
            _schemeListOpen = false;
            if (_schemeDropdown != null) _schemeDropdown.gameObject.SetActive(false);
        }

        private void OnSchemeChosen(uint schemeId)
        {
            CloseSchemeList();
            var client = _owner.Client;
            if (client == null) { SetStatus("属性模块未就绪", true); return; }
            if (_panel != null && schemeId == _panel.ActiveSchemeId) return;
            if (PendingDelta() > 0)
            {
                SetStatus("有未确认的加点,请先「确认」或「重置」。", true);
                return;
            }
            client.SwitchScheme(schemeId);
            SetStatus("正在切换方案…");
        }

        private void ShowTooltipFor(UiPointRow row)
        {
            string desc = FindDesc(row.DimensionId);
            if (string.IsNullOrEmpty(desc) || _tooltip == null) return;
            _tooltipText.text = desc;
            // 贴在该行下方偏左(截图里提示条压在下一行上方)
            UnityEngine.Vector3 local = _root.InverseTransformPoint(row.Rect.TransformPoint(UnityEngine.Vector3.zero));
            _tooltip.anchoredPosition = new Vector2(
                Mathf.Min(CharacterAttributeVisuals.WindowW - 700f, local.x + 100f), local.y - RowHeight);
            _tooltip.SetAsLastSibling();
            _tooltip.gameObject.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.gameObject.SetActive(false);
        }
    }
}
