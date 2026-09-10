using System.Collections.Generic;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>
    /// 宝宝面板(问道式三栏窗):
    ///   左栏 = 宝宝列表(出战中的高亮)+ 出战 / 收回;
    ///   中栏 = 选中宝宝的六项二级属性(气血 / 法力 / 物伤 / 法伤 / 速度 / 防御)+ 等级 / 成长率;
    ///   右栏 = 剩余属性点 + 自动加点 + 四行加点(体质 / 灵力 / 力量 / 敏捷)+ 洗点 / 确认。
    ///
    /// 数据契约:面板内容 **全部** 来自服务器 <see cref="PetListInfo"/> —— 维度名、说明 tooltip、
    /// 单项上限、剩余点、资质、成长率、二级属性都不在客户端配表,改表不用改客户端
    /// (设计文档 player-pet.md §4)。
    ///
    /// 本地只保留"待提交增量":滑条 / ± 改的是 <see cref="UiPointRow.Pending"/>,「确认」才把
    /// 整只宝宝的目标值发给服务器;服务器回全量列表后整体覆盖。未提交时切换选中宝宝会被拦下
    /// (避免玩家以为已生效)。控件与配色复用属性窗的 <see cref="AttributeUiWidgets"/> /
    /// <see cref="AttributeUiStyle"/>,不另造一套。
    /// </summary>
    public sealed class PetPanel
    {
        private const float RowHeight = 54f;
        private const float RowGap = 12f;
        private const int MaxRows = 6;

        private readonly PetUiRoot _owner;
        private readonly RectTransform _root;

        // 左栏
        private readonly List<UiTextButton> _petItems = new();
        private readonly List<ulong> _petItemIds = new();
        private UiTextButton _summonButton;

        // 中栏
        private readonly TMP_Text[] _statValues = new TMP_Text[6];
        private TMP_Text _levelValue;
        private TMP_Text _growthValue;

        // 右栏
        private TMP_Text _titleText;
        private TMP_Text _remainText;
        private UiTextButton _autoButton;
        private readonly UiPointRow[] _rows = new UiPointRow[MaxRows];
        private UiTextButton _resetButton;
        private UiTextButton _confirmButton;
        private TMP_Text _statusText;

        // tooltip(悬浮说明,如"体质:提高宝宝气血上限和防御。")
        private readonly RectTransform _tooltip;
        private readonly TMP_Text _tooltipText;

        private PetListInfo _list;
        private ulong _selectedPetId;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public PetPanel(PetUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;

            var window = BattleUiWidgets.CreatePanel("PetWindow", parent,
                PetUiStyle.WindowX, PetUiStyle.WindowY,
                PetUiStyle.WindowW, PetUiStyle.WindowH, PetUiStyle.WindowPaper);
            QdaoRefreshArt.Skin(window, "main_frame");
            window.pixelsPerUnitMultiplier = 4f;
            _root = (RectTransform)window.transform;

            // ── 标题栏 ──
            float titleX = (PetUiStyle.WindowW - 560f) * 0.5f;
            QdaoRefreshArt.Panel("TitlePlate", _root, titleX, -22f, 560f, 74f, "primary_button_normal");
            _titleText = QdaoUguiFactory.CreateText("Title", _root, titleX, -22f, 560f, 74f,
                "宝 宝", 34f, PetUiStyle.TitleText, TextAlignmentOptions.Center);

            var close = BattleUiWidgets.CreateTextButton("Close", _root,
                PetUiStyle.WindowW - 76f, -18f, 62f, 62f, "×", 34f,
                PetUiStyle.ClosePlate, PetUiStyle.CloseText);
            close.Button.onClick.AddListener(Hide);

            BuildLeftColumn();
            BuildMidColumn();
            BuildRightColumn();

            // tooltip 最后建,保证兄弟序在最上层
            _tooltip = QdaoUguiFactory.CreateRect("Tooltip", _root, 0f, 0f, 460f, 56f);
            var tooltipPlate = _tooltip.gameObject.AddComponent<Image>();
            tooltipPlate.color = PetUiStyle.TooltipPlate;
            tooltipPlate.raycastTarget = false;
            _tooltipText = QdaoUguiFactory.CreateText("TooltipText", _tooltip, 16f, 0f, 428f, 56f,
                string.Empty, 22f, PetUiStyle.TooltipText);
            _tooltip.gameObject.SetActive(false);

            Hide();
        }

        // ── 构建 ────────────────────────────────────────────

        private void BuildLeftColumn()
        {
            float x = PetUiStyle.LeftX;
            float w = PetUiStyle.LeftW;

            QdaoUguiFactory.CreateText("PetListLabel", _root, x, 84f, w, 48f,
                "我的宝宝", 26f, PetUiStyle.FieldLabel);

            for (int i = 0; i < PetUiStyle.MaxPetItems; i++)
            {
                int index = i;
                var item = BattleUiWidgets.CreateTextButton($"PetItem{i}", _root,
                    x, 140f + i * (PetUiStyle.PetItemHeight + PetUiStyle.PetItemGap), w,
                    PetUiStyle.PetItemHeight, string.Empty, 24f,
                    PetUiStyle.FieldPlate, PetUiStyle.FieldLabel);
                item.Button.onClick.AddListener(() => OnPetItemClicked(index));
                item.SetVisible(false);
                _petItems.Add(item);
                _petItemIds.Add(0);
            }

            _summonButton = BattleUiWidgets.CreateTextButton("Summon", _root,
                x, PetUiStyle.WindowH - 116f, w, 74f,
                "出 战", 28f, PetUiStyle.ConfirmPlate, PetUiStyle.ActionText);
            QdaoRefreshArt.Skin(_summonButton.Plate, "primary_button_normal");
            _summonButton.Label.color = QdaoRefreshArt.Ivory;
            _summonButton.Button.onClick.AddListener(OnSummonClicked);
        }

        private void BuildMidColumn()
        {
            float x = PetUiStyle.MidX;
            float w = PetUiStyle.MidW;

            _levelValue = AttributeUiWidgets.CreateStatField("PetLevel", _root, x, 84f, w, 72f, "等 级");
            _growthValue = AttributeUiWidgets.CreateStatField("PetGrowth", _root, x, 168f, w, 72f, "成长率");

            string[] labels = { "气 血", "法 力", "物 伤", "法 伤", "速 度", "防 御" };
            for (int i = 0; i < labels.Length; i++)
            {
                _statValues[i] = AttributeUiWidgets.CreateStatField($"Stat{i}", _root,
                    x, 268f + i * 92f, w, 72f, labels[i]);
            }
        }

        private void BuildRightColumn()
        {
            float x = PetUiStyle.RightX;
            float w = PetUiStyle.RightW;

            _remainText = QdaoUguiFactory.CreateText("Remain", _root, x, 90f, w - 200f, 52f,
                string.Empty, 26f, PetUiStyle.RemainText);

            _autoButton = BattleUiWidgets.CreateTextButton("Auto", _root, x + w - 190f, 86f, 190f, 60f,
                "自动加点", 24f, PetUiStyle.ActionPlate, PetUiStyle.ActionText);
            _autoButton.Button.onClick.AddListener(OnAutoClicked);

            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = AttributeUiWidgets.CreatePointRow($"Row{i}", _root,
                    x, 176f + i * (RowHeight + RowGap), w, RowHeight);
                _rows[i].Bind(OnRowChanged);
                AttachRowTooltip(_rows[i]);
                _rows[i].Rect.gameObject.SetActive(false);
            }

            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root, x, PetUiStyle.WindowH - 190f,
                w, 60f, string.Empty, 22f, PetUiStyle.HintText);

            _resetButton = BattleUiWidgets.CreateTextButton("Reset", _root,
                x, PetUiStyle.WindowH - 116f, 240f, 74f,
                "洗 点", 28f, PetUiStyle.ActionPlate, PetUiStyle.ActionText);
            _resetButton.Button.onClick.AddListener(OnResetClicked);

            _confirmButton = BattleUiWidgets.CreateTextButton("Confirm", _root,
                x + w - 240f, PetUiStyle.WindowH - 116f, 240f, 74f,
                "确 认", 28f, PetUiStyle.ConfirmPlate, PetUiStyle.ActionText);
            QdaoRefreshArt.Skin(_confirmButton.Plate, "primary_button_normal");
            _confirmButton.Label.color = QdaoRefreshArt.Ivory;
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
        }

        /// <summary>给一行挂 hover 事件:显示服务器下发的维度说明。</summary>
        private void AttachRowTooltip(UiPointRow row)
        {
            if (row.Hover == null) return;
            row.Hover.OnEnter = () => ShowTooltipFor(row);
            row.Hover.OnExit = HideTooltip;
        }

        // ── 显隐 ────────────────────────────────────────────

        public void Show()
        {
            // 属性窗与本窗同位置同尺寸,必须互斥(见 AttributeUiRoot.HidePanel)
            AttributeUiRoot.Instance?.HidePanel();
            if (_root != null) _root.gameObject.SetActive(true);
            HideTooltip();
            SetStatus(string.Empty);
            _owner.Client?.RequestList();
        }

        public void Hide()
        {
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
            _statusText.color = warn ? PetUiStyle.WarnText : PetUiStyle.HintText;
        }

        // ── 数据应用 ────────────────────────────────────────

        /// <summary>服务器列表到达:整体重建界面状态,丢弃所有未提交增量。</summary>
        public void ApplyList(PetListInfo list)
        {
            _list = list;
            if (list == null) return;

            // 选中项失效(第一次打开 / 宝宝被换掉)时优先选出战中的那只,否则选第一只
            if (_selectedPetId == 0 || FindPet(_selectedPetId) == null)
            {
                _selectedPetId = list.ActivePetId != 0 && FindPet(list.ActivePetId) != null
                    ? list.ActivePetId
                    : (list.Pets.Count > 0 ? list.Pets[0].PetId : 0);
            }

            RefreshPetItems();
            RefreshDerived();
            RebuildRows();
            RefreshActionButtons();
        }

        /// <summary>在途状态变化:按钮与加点行一起进 / 出 Busy(失败回包后必须解锁)。</summary>
        public void ApplyBusy(bool busy)
        {
            RefreshRowRanges();
            RefreshActionButtons();
        }

        /// <summary>自动加点建议:只填本地待提交值,玩家点「确认」才落。</summary>
        public void ApplyAutoSuggestion(ulong petId, IReadOnlyDictionary<uint, uint> suggested)
        {
            if (petId != _selectedPetId) return;
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

        private void RefreshPetItems()
        {
            for (int i = 0; i < _petItems.Count; i++)
            {
                if (i < _list.Pets.Count)
                {
                    var pet = _list.Pets[i];
                    _petItemIds[i] = pet.PetId;
                    _petItems[i].SetVisible(true);
                    string mark = pet.IsActive ? "【战】" : string.Empty;
                    _petItems[i].SetText($"{mark}{pet.Name}  Lv.{pet.Level}");
                    bool selected = pet.PetId == _selectedPetId;
                    if (_petItems[i].Plate != null)
                    {
                        QdaoRefreshArt.Skin(_petItems[i].Plate, selected ? "tab_selected" : "list_row_normal");
                    }
                    if (_petItems[i].Label != null)
                    {
                        _petItems[i].Label.color = selected ? QdaoRefreshArt.Ivory : PetUiStyle.FieldLabel;
                    }
                }
                else
                {
                    _petItemIds[i] = 0;
                    _petItems[i].SetVisible(false);
                }
            }

            var current = FindPet(_selectedPetId);
            _titleText.text = current != null ? $"宝 宝 · {current.Name}" : "宝 宝";
        }

        private void RefreshDerived()
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null)
            {
                _levelValue.text = "-";
                _growthValue.text = "-";
                for (int i = 0; i < _statValues.Length; i++) SetStat(i, "-");
                return;
            }
            _levelValue.text = pet.Level.ToString();
            // 成长率是万分比(10000 = 100%),服务器算好给,这里只做展示换算
            _growthValue.text = $"{pet.Growth / 100f:0.##}%";

            var derived = pet.Derived;
            if (derived == null) return;
            // 气血/法力显示「当前/上限」,其余单值(与角色面板同排布)
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

        private void RebuildRows()
        {
            var pet = FindPet(_selectedPetId);
            bool interactable = pet != null && !(_owner.Client?.Busy ?? false);
            int index = 0;
            if (pet != null)
            {
                foreach (var dimension in pet.Dimensions)
                {
                    if (index >= MaxRows) break;
                    var row = _rows[index];
                    row.Rect.gameObject.SetActive(true);
                    row.SetFromServer(dimension.DimensionId, dimension.Name, dimension.Value,
                        dimension.Allocated, dimension.Cap, pet.RemainingPoints, interactable);
                    row.Rect.name = $"Row_{dimension.Name}";
                    index++;
                }
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
            var pet = FindPet(_selectedPetId);
            if (pet == null) return 0;
            uint delta = PendingDelta();
            return pet.RemainingPoints > delta ? pet.RemainingPoints - delta : 0;
        }

        private void RefreshRowRanges()
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return;
            uint left = RemainingAfterPending();
            bool interactable = !(_owner.Client?.Busy ?? false);
            foreach (var row in ActiveRows())
            {
                row.ApplyRange(FindCap(row.DimensionId), left, interactable);
            }
        }

        private void RefreshRemainText()
        {
            if (_remainText == null) return;
            var pet = FindPet(_selectedPetId);
            if (pet == null)
            {
                _remainText.text = "! 还没有宝宝";
                return;
            }
            uint left = RemainingAfterPending();
            uint delta = PendingDelta();
            _remainText.text = delta > 0
                ? $"! 剩余属性点 {left}(待分配 {delta})"
                : $"! 剩余属性点 {left}";
        }

        private void RefreshActionButtons()
        {
            var pet = FindPet(_selectedPetId);
            bool busy = _owner.Client?.Busy ?? false;
            bool hasPet = pet != null;
            uint delta = PendingDelta();

            _confirmButton?.SetInteractable(!busy && hasPet && delta > 0);
            _autoButton?.SetInteractable(!busy && hasPet && (pet?.RemainingPoints ?? 0) > 0);
            _resetButton?.SetInteractable(!busy && hasPet);
            _resetButton?.SetText(pet != null && pet.ResetCostGold > 0 ? $"洗点({pet.ResetCostGold} 金)" : "洗 点");

            // 出战按钮在"出战 / 收回"之间切:选中的就是出战中那只时按钮变收回
            bool selectedIsActive = pet != null && pet.IsActive;
            _summonButton?.SetText(selectedIsActive ? "收 回" : "出 战");
            _summonButton?.SetInteractable(!busy && hasPet);
        }

        private PetInfo FindPet(ulong petId)
        {
            if (_list == null) return null;
            foreach (var pet in _list.Pets)
            {
                if (pet.PetId == petId) return pet;
            }
            return null;
        }

        private uint FindCap(uint dimensionId)
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return 0;
            foreach (var dimension in pet.Dimensions)
            {
                if (dimension.DimensionId == dimensionId) return dimension.Cap;
            }
            return 0;
        }

        private string FindDesc(uint dimensionId)
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return string.Empty;
            foreach (var dimension in pet.Dimensions)
            {
                if (dimension.DimensionId == dimensionId)
                {
                    // 资质是宝宝独有的一条信息,直接并进 tooltip:玩家最想知道的就是
                    // "这一点加下去,我这只宝宝比别人多长多少"
                    return dimension.Aptitude > 0
                        ? $"{dimension.Desc}(资质 {dimension.Aptitude / 100f:0.##}%)"
                        : dimension.Desc;
                }
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

        private void OnPetItemClicked(int index)
        {
            if (index < 0 || index >= _petItemIds.Count) return;
            ulong petId = _petItemIds[index];
            if (petId == 0 || petId == _selectedPetId) return;
            if (PendingDelta() > 0)
            {
                SetStatus("有未确认的加点,请先「确认」或「洗点」。", true);
                return;
            }
            _selectedPetId = petId;
            SetStatus(string.Empty);
            RefreshPetItems();
            RefreshDerived();
            RebuildRows();
            RefreshActionButtons();
        }

        private void OnSummonClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            var pet = FindPet(_selectedPetId);
            if (pet == null) { SetStatus("请先选中一只宝宝。", true); return; }
            if (pet.IsActive)
            {
                client.Recall();
                SetStatus("正在收回…");
                return;
            }
            client.Summon(_selectedPetId);
            SetStatus("正在出战…");
        }

        private void OnConfirmClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
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
            client.Allocate(_selectedPetId, target);
            SetStatus("已提交,等待服务器确认…");
        }

        private void OnResetClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            if (PendingDelta() > 0)
            {
                // 先撤未提交的本地增量,不必花钱洗点
                RebuildRows();
                RefreshActionButtons();
                SetStatus("已撤销未确认的加点。");
                return;
            }
            client.ResetPoints(_selectedPetId);
            SetStatus("已请求洗点,等待服务器确认…");
        }

        private void OnAutoClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            if (_selectedPetId == 0) { SetStatus("请先选中一只宝宝。", true); return; }
            client.AutoAllocate(_selectedPetId);
            SetStatus("正在计算推荐方案…");
        }

        private void ShowTooltipFor(UiPointRow row)
        {
            string desc = FindDesc(row.DimensionId);
            if (string.IsNullOrEmpty(desc) || _tooltip == null) return;
            _tooltipText.text = desc;
            // 贴在该行下方偏左
            var anchored = row.Rect.anchoredPosition;
            _tooltip.anchoredPosition = new Vector2(anchored.x + 60f, anchored.y - RowHeight + 4f);
            _tooltip.SetAsLastSibling();
            _tooltip.gameObject.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.gameObject.SetActive(false);
        }
    }
}
