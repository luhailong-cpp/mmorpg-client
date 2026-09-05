using System.Collections.Generic;
using MmorpgClient.Game.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>
    /// 属性加点面板(问道式两栏窗):
    ///   左栏 = 方案选择 + 六项二级属性(气血/法力/物伤/法伤/速度/防御)+ 开启新方案;
    ///   右栏 = 三个点数池页签(属性点/相性点/仙魔点)+ 剩余点 + 自动加点 + 加点行 + 重置/确认。
    ///
    /// 数据契约:面板内容 **全部** 来自服务器 <see cref="AttributePanelInfo"/> —— 维度名、
    /// 说明 tooltip、单项上限、剩余点、二级属性都不在客户端配表,改表不用改客户端
    /// (设计文档 player-attribute-allocation.md §4)。
    ///
    /// 本地只保留"待提交增量":滑条/± 改的是 <see cref="UiPointRow.Pending"/>,
    /// 「确认」才把整池目标值发给服务器;服务器回全量面板后整体覆盖。
    /// 未提交时切页签/切方案会被拦下(避免玩家以为已生效)。
    /// </summary>
    public sealed class AttributePanel
    {
        private const float RowHeight = 68f;
        private const float RowGap = 14f;
        private const int MaxRows = 8;

        private readonly AttributeUiRoot _owner;
        private readonly RectTransform _root;

        // 左栏
        private UiTextButton _schemeButton;
        private RectTransform _schemeDropdown;
        private readonly List<UiTextButton> _schemeOptions = new();
        private readonly TMP_Text[] _statValues = new TMP_Text[6];
        private UiTextButton _createSchemeButton;

        // 右栏
        private TMP_Text _titleText;
        private readonly List<UiTextButton> _tabs = new();
        private readonly List<uint> _tabPoolIds = new();
        private TMP_Text _remainText;
        private UiTextButton _autoButton;
        private readonly UiPointRow[] _rows = new UiPointRow[MaxRows];
        private UiTextButton _resetButton;
        private UiTextButton _confirmButton;
        private TMP_Text _statusText;

        // tooltip(悬浮说明,对应截图里的"体质:提高气血上限和防御。")
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
                AttributeUiStyle.WindowX, AttributeUiStyle.WindowY,
                AttributeUiStyle.WindowW, AttributeUiStyle.WindowH, AttributeUiStyle.WindowPaper);
            _root = (RectTransform)window.transform;

            // ── 标题栏 ──
            BattleUiWidgets.CreatePanel("TitlePlate", _root, 340f, -22f, 560f, 74f, AttributeUiStyle.TitlePlate);
            _titleText = QdaoUguiFactory.CreateText("Title", _root, 340f, -22f, 560f, 74f,
                "属 性 加 点", 34f, AttributeUiStyle.TitleText, TextAlignmentOptions.Center);

            var close = BattleUiWidgets.CreateTextButton("Close", _root,
                AttributeUiStyle.WindowW - 76f, -18f, 62f, 62f, "×", 34f,
                AttributeUiStyle.ClosePlate, AttributeUiStyle.CloseText);
            close.Button.onClick.AddListener(Hide);

            BuildLeftColumn();
            BuildRightColumn();

            // tooltip 最后建,保证兄弟序在最上层
            _tooltip = QdaoUguiFactory.CreateRect("Tooltip", _root, 0f, 0f, 460f, 56f);
            var tooltipPlate = _tooltip.gameObject.AddComponent<Image>();
            tooltipPlate.color = AttributeUiStyle.TooltipPlate;
            tooltipPlate.raycastTarget = false;
            _tooltipText = QdaoUguiFactory.CreateText("TooltipText", _tooltip, 16f, 0f, 428f, 56f,
                string.Empty, 22f, AttributeUiStyle.TooltipText);
            _tooltip.gameObject.SetActive(false);

            Hide();
        }

        // ── 构建 ────────────────────────────────────────────

        private void BuildLeftColumn()
        {
            float x = AttributeUiStyle.LeftX;
            float w = AttributeUiStyle.LeftW;

            _schemeButton = BattleUiWidgets.CreateTextButton("SchemeButton", _root, x, 76f, w, 66f,
                "方案一  ▾", 26f, AttributeUiStyle.FieldPlate, AttributeUiStyle.FieldLabel);
            _schemeButton.Button.onClick.AddListener(ToggleSchemeList);

            // 下拉列表(点方案按钮展开;选项按面板 schemes 动态重建)
            _schemeDropdown = QdaoUguiFactory.CreateRect("SchemeDropdown", _root, x, 144f, w, 0f);
            _schemeDropdown.gameObject.SetActive(false);

            string[] labels = { "气 血", "法 力", "物 伤", "法 伤", "速 度", "防 御" };
            for (int i = 0; i < labels.Length; i++)
            {
                _statValues[i] = AttributeUiWidgets.CreateStatField($"Stat{i}", _root,
                    x, 176f + i * 104f, w, 72f, labels[i]);
            }

            _createSchemeButton = BattleUiWidgets.CreateTextButton("CreateScheme", _root,
                x, AttributeUiStyle.WindowH - 116f, w - 60f, 74f,
                "开启新方案", 26f, AttributeUiStyle.ConfirmPlate, AttributeUiStyle.ActionText);
            _createSchemeButton.Button.onClick.AddListener(OnCreateSchemeClicked);
        }

        private void BuildRightColumn()
        {
            float x = AttributeUiStyle.RightX;
            float w = AttributeUiStyle.RightW;

            // 三个池页签由面板数据驱动建(池名来自服务器),这里先占位建满 3 个
            const int tabCount = 3;
            float tabW = (w - 2 * 16f) / tabCount;
            for (int i = 0; i < tabCount; i++)
            {
                int index = i;
                var tab = BattleUiWidgets.CreateTextButton($"Tab{i}", _root,
                    x + i * (tabW + 16f), 76f, tabW, 66f, string.Empty, 26f,
                    AttributeUiStyle.TabIdle, AttributeUiStyle.TabText);
                tab.Button.onClick.AddListener(() => OnTabClicked(index));
                tab.SetVisible(false);
                _tabs.Add(tab);
                _tabPoolIds.Add(0);
            }

            _remainText = QdaoUguiFactory.CreateText("Remain", _root, x, 168f, w - 220f, 52f,
                string.Empty, 26f, AttributeUiStyle.RemainText);

            _autoButton = BattleUiWidgets.CreateTextButton("Auto", _root, x + w - 200f, 164f, 200f, 60f,
                "自动加点", 24f, AttributeUiStyle.ActionPlate, AttributeUiStyle.ActionText);
            _autoButton.Button.onClick.AddListener(OnAutoClicked);

            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = AttributeUiWidgets.CreatePointRow($"Row{i}", _root,
                    x, 240f + i * (RowHeight + RowGap), w, RowHeight);
                _rows[i].Bind(OnRowChanged);
                AttachRowTooltip(_rows[i]);
                _rows[i].Rect.gameObject.SetActive(false);
            }

            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root, x, AttributeUiStyle.WindowH - 190f,
                w, 60f, string.Empty, 22f, AttributeUiStyle.HintText);

            _resetButton = BattleUiWidgets.CreateTextButton("Reset", _root,
                x + 40f, AttributeUiStyle.WindowH - 116f, 240f, 74f,
                "重 置", 28f, AttributeUiStyle.ActionPlate, AttributeUiStyle.ActionText);
            _resetButton.Button.onClick.AddListener(OnResetClicked);

            _confirmButton = BattleUiWidgets.CreateTextButton("Confirm", _root,
                x + w - 280f, AttributeUiStyle.WindowH - 116f, 240f, 74f,
                "确 认", 28f, AttributeUiStyle.ConfirmPlate, AttributeUiStyle.ActionText);
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
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
                _activePoolId = panel.Pools.Count > 0 ? panel.Pools[0].PoolId : 0;
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
            var name = "方案一";
            foreach (var scheme in _panel.Schemes)
            {
                if (scheme.SchemeId == _panel.ActiveSchemeId)
                {
                    name = scheme.Name;
                    break;
                }
            }
            _schemeButton?.SetText($"{name}  ▾");
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
            }
        }

        private void RefreshTabs()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (i < _panel.Pools.Count)
                {
                    var pool = _panel.Pools[i];
                    _tabPoolIds[i] = pool.PoolId;
                    _tabs[i].SetVisible(true);
                    _tabs[i].SetText(pool.Unlocked ? pool.Name : $"{pool.Name}({pool.UnlockLevel}级)");
                    bool active = pool.PoolId == _activePoolId;
                    if (_tabs[i].Plate != null)
                    {
                        _tabs[i].Plate.color = active ? AttributeUiStyle.TabActive : AttributeUiStyle.TabIdle;
                    }
                    if (_tabs[i].Label != null)
                    {
                        _tabs[i].Label.color = pool.Unlocked ? AttributeUiStyle.TabText : AttributeUiStyle.TabLockedText;
                    }
                }
                else
                {
                    _tabPoolIds[i] = 0;
                    _tabs[i].SetVisible(false);
                }
            }

            var activePool = FindPool(_activePoolId);
            _titleText.text = activePool != null && activePool.PoolId != 0
                ? SpacedTitle(activePool.Name)
                : "属 性 加 点";
        }

        /// <summary>标题按截图风格分字("属性点" → "属 性 加 点")。</summary>
        private static string SpacedTitle(string poolName)
        {
            string trimmed = poolName != null && poolName.EndsWith("点") && poolName.Length > 1
                ? poolName.Substring(0, poolName.Length - 1)
                : poolName;
            var chars = (trimmed ?? "属性") + "加点";
            return string.Join(" ", chars.ToCharArray());
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
            if (pool == null || _remainText == null) return;
            uint left = RemainingAfterPending();
            string poolName = string.IsNullOrEmpty(pool.Name) ? "点数" : pool.Name;
            if (!pool.Unlocked)
            {
                _remainText.text = $"! {poolName} 需 {pool.UnlockLevel} 级解锁";
                return;
            }
            uint delta = PendingDelta();
            _remainText.text = delta > 0
                ? $"! 剩余{poolName} {left}(待分配 {delta})"
                : $"! 剩余{poolName} {left}";
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

            float w = AttributeUiStyle.LeftW;
            for (int i = 0; i < _panel.Schemes.Count; i++)
            {
                var scheme = _panel.Schemes[i];
                uint schemeId = scheme.SchemeId;
                var option = BattleUiWidgets.CreateTextButton($"SchemeOption{i}", _schemeDropdown,
                    0f, i * 62f, w, 58f, scheme.Name, 24f,
                    schemeId == _panel.ActiveSchemeId ? AttributeUiStyle.TabActive : AttributeUiStyle.FieldPlate,
                    AttributeUiStyle.FieldLabel);
                option.Button.onClick.AddListener(() => OnSchemeChosen(schemeId));
                _schemeOptions.Add(option);
            }
            _schemeDropdown.sizeDelta = new Vector2(w, _panel.Schemes.Count * 62f);
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
            var anchored = row.Rect.anchoredPosition;
            _tooltip.anchoredPosition = new Vector2(anchored.x + 120f, anchored.y - RowHeight + 4f);
            _tooltip.SetAsLastSibling();
            _tooltip.gameObject.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.gameObject.SetActive(false);
        }
    }
}
