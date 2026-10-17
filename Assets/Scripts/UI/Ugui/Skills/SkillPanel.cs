using System;
using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>角色和宠物共用的原生技能簿；只展示快照并转发操作意图。</summary>
    public sealed partial class SkillPanel
    {
        private sealed class Row
        {
            public uint Id;
            public UiTextButton Button;
            public Image Icon;
            public TMP_Text Name, Rank, Kind, Mana, Target;
        }
        private readonly RectTransform _root, _content, _descriptionContent;
        private readonly ScrollRect _scroll, _descriptionScroll;
        private readonly TMP_Text _title, _classLabel, _classInfo, _empty, _description, _condition,
            _balance, _cost, _status, _selectedName;
        private readonly Image _selectedIcon;
        private readonly UiTextButton _upgrade, _upgradeTen, _refresh;
        private readonly UiTextButton[] _filters = new UiTextButton[5];
        private readonly UiTextButton[] _classes = new UiTextButton[4];
        private readonly List<Row> _rows = new();
        private readonly List<SkillEntryState> _visible = new();
        private readonly Action _refreshAction;
        private readonly Action<uint, uint, uint> _upgradeAction;
        private SkillBookViewState _state;
        private bool _hadContext, _lastIsPet;
        private ulong _lastPetId;
        private uint _lastClassId;
        private uint _selectedId;
        private int _filter;
        private static readonly string[] FilterNames = { "全部", "主动", "被动", "障碍", "辅助" };
        private const float ListHeight = 500f, RowHeight = 82f, RowStep = 86f;
        public bool IsVisible => _root.gameObject.activeSelf;

        public SkillPanel(SkillUiRoot owner, UnityEngine.Transform parent) : this(parent,
            owner.RequestRefresh, owner.RequestUpgrade, owner.SelectPreviewClass, owner.HidePanel,
            allocation =>
            {
                owner.HidePanel();
                if (owner.Context.IsPet) Pet.PetUiRoot.Instance?.OpenPanel(allocation);
                else AttributeUiRoot.Instance?.OpenPanel(allocation);
            }) { }

        /// <summary>无单例构造供离线预览使用；回调与生产网络完全分离。</summary>
        public SkillPanel(UnityEngine.Transform parent, Action refresh, Action<uint, uint, uint> upgrade,
            Action<uint> previewClass = null, Action close = null, Action<bool> navigate = null)
        {
            _refreshAction = refresh;
            _upgradeAction = upgrade;
            var frame = BattleUiWidgets.CreatePanel("CharacterSkillWindow", parent,
                CharacterAttributeVisuals.WindowX, CharacterAttributeVisuals.WindowY,
                CharacterAttributeVisuals.WindowW, CharacterAttributeVisuals.WindowH, Color.white);
            CharacterAttributeVisuals.Skin(frame, "window_frame");
            _root = frame.rectTransform;
            _root.gameObject.AddComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>();
            CharacterAttributeVisuals.Panel("TitlePlate", _root, 598f, -64f, 820f, 138f, "title_plate");
            _title = Text("Title", _root, 678f, -22f, 660f, 65f, "角色技能", 44f,
                QdaoRefreshArt.Ivory, TextAlignmentOptions.Center);
            var closeButton = CharacterAttributeVisuals.Button("Close", _root, 1980f, -24f, 88f, 88f, "关闭");
            CharacterAttributeVisuals.Skin(closeButton.Plate, "close_button");
            closeButton.Label.gameObject.SetActive(false);
            closeButton.Button.onClick.AddListener(() => { Hide(); close?.Invoke(); });
            CharacterAttributeVisuals.Panel("CloseTassel", _root, 2052f, 24f, 32f, 80f, "close_tassel");

            _classLabel = Text("Class", _root, 105f, 68f, 1740f, 50f, "技能信息未同步", 32f);
            _classInfo = Text("ClassInfo", _root, 105f, 125f, 810f, 50f, string.Empty, 27f);
            string[] classNames = { "破军", "玄霄", "丹心", "逐风" };
            for (int i = 0; i < _classes.Length; i++)
            {
                uint classId = (uint)i + 1;
                var button = CharacterAttributeVisuals.Button("PreviewClass" + classId, _root,
                    105f + i * 168f, 124f, 154f, 54f, classNames[i], false, 28f);
                button.Button.onClick.AddListener(() => previewClass?.Invoke(classId));
                button.SetInteractable(previewClass != null);
                _classes[i] = button;
            }
            for (int i = 0; i < _filters.Length; i++)
            {
                int captured = i;
                _filters[i] = CharacterAttributeVisuals.Button("Filter" + i, _root,
                    985f + i * 184f, 120f, 172f, 60f, FilterNames[i], i == 0, 29f);
                _filters[i].Button.onClick.AddListener(() =>
                {
                    _filter = captured;
                    _scroll.verticalNormalizedPosition = 1f;
                    Rebuild();
                });
            }
            Text("NameHeader", _root, 238f, 183f, 400f, 42f, "技能名称", 27f);
            Text("RankHeader", _root, 693f, 183f, 180f, 42f, "等级", 27f);
            Text("KindHeader", _root, 938f, 183f, 180f, 42f, "类型", 27f);
            Text("ManaHeader", _root, 1198f, 183f, 360f, 42f, "法力 / 冷却", 27f);
            Text("TargetHeader", _root, 1663f, 183f, 230f, 42f, "作用目标", 27f);

            var viewport = QdaoUguiFactory.CreateRect("SkillListViewport", _root, 100f, 228f, 1800f, ListHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;
            _content = QdaoUguiFactory.CreateRect("SkillListContent", viewport, 0f, 0f, 1800f, ListHeight);
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 38f;
            AddScrollbar(_root, _scroll, "SkillListScrollbar", 1907f, 228f, ListHeight);
            _empty = Text("Empty", viewport, 110f, 132f, 1580f, 126f,
                "技能信息未同步", 31f, QdaoRefreshArt.Ink, TextAlignmentOptions.Center);
            _empty.textWrappingMode = TextWrappingModes.Normal;

            _selectedIcon = QdaoUguiFactory.CreateImage("SelectedIcon", _root, 105f, 687f, 112f, 112f, null);
            _selectedIcon.preserveAspect = true;
            _selectedName = Text("SelectedName", _root, 242f, 678f, 442f, 50f, "选择一个技能", 32f);
            _condition = Text("Condition", _root, 242f, 731f, 450f, 76f, string.Empty, 25f);
            _condition.textWrappingMode = TextWrappingModes.Normal;
            var detailViewport = QdaoUguiFactory.CreateRect("DescriptionViewport", _root, 720f, 678f, 1160f, 126f);
            detailViewport.gameObject.AddComponent<RectMask2D>();
            var detailHit = detailViewport.gameObject.AddComponent<Image>();
            detailHit.color = new Color(1f, 1f, 1f, .001f);
            _descriptionContent = QdaoUguiFactory.CreateRect("DescriptionContent", detailViewport, 0f, 0f, 1150f, 126f);
            _description = Text("Description", _descriptionContent, 0f, 0f, 1140f, 126f, string.Empty, 28f);
            _description.textWrappingMode = TextWrappingModes.Normal;
            _description.overflowMode = TextOverflowModes.Overflow;
            _description.alignment = TextAlignmentOptions.TopLeft;
            _descriptionScroll = detailViewport.gameObject.AddComponent<ScrollRect>();
            _descriptionScroll.viewport = detailViewport;
            _descriptionScroll.content = _descriptionContent;
            _descriptionScroll.horizontal = false;
            _descriptionScroll.movementType = ScrollRect.MovementType.Clamped;
            _descriptionScroll.scrollSensitivity = 30f;
            AddScrollbar(_root, _descriptionScroll, "DescriptionScrollbar", 1890f, 678f, 126f);

            _status = Text("Status", _root, 105f, 810f, 1790f, 34f, string.Empty, 24f);
            _balance = Text("Balance", _root, 105f, 852f, 520f, 46f, "金币：—", 27f);
            _cost = Text("Cost", _root, 620f, 852f, 460f, 46f, "下一级消耗：—", 26f);
            _refresh = CharacterAttributeVisuals.Button("Refresh", _root, 1080f, 846f, 220f, 65f, "刷新");
            _refresh.Button.onClick.AddListener(() => _refreshAction?.Invoke());
            _upgrade = CharacterAttributeVisuals.Button("Upgrade", _root, 1320f, 846f, 260f, 65f, "学习", true);
            _upgrade.Button.onClick.AddListener(() => SubmitUpgrade(1));
            _upgradeTen = CharacterAttributeVisuals.Button("UpgradeTen", _root, 1600f, 846f, 290f, 65f, "升级10次", true);
            _upgradeTen.Button.onClick.AddListener(() => SubmitUpgrade(10));
            var attribute = CharacterAttributeVisuals.SideTab("AttributeTab", _root, 116f, "属\n性", false);
            attribute.Button.onClick.AddListener(() => navigate?.Invoke(false));
            attribute.SetInteractable(navigate != null);
            var allocation = CharacterAttributeVisuals.SideTab("AllocationTab", _root, 302f, "加\n点", false);
            allocation.Button.onClick.AddListener(() => navigate?.Invoke(true));
            allocation.SetInteractable(navigate != null);
            CharacterAttributeVisuals.SideTab("SkillTab", _root, 488f, "技\n能", true).SetInteractable(false);
            Hide();
            Apply(null);
        }

        public void Show() => _root.gameObject.SetActive(true);
        public void Hide() => _root.gameObject.SetActive(false);
        public void Apply(SkillBookViewState state)
        {
            // 缓存身份值而非旧对象引用，兼容适配器复用展示对象的情况。
            var context = state?.Context;
            uint classId = state?.Snapshot?.ClassId ?? 0;
            ulong petId = context?.IsPet == true ? context.PetId : 0;
            bool changedContext = !_hadContext || context == null || _lastIsPet != context.IsPet ||
                _lastPetId != petId || _lastClassId != classId;
            _hadContext = context != null;
            _lastIsPet = context?.IsPet == true;
            _lastPetId = petId;
            _lastClassId = classId;
            _state = state;
            if (changedContext)
            {
                _selectedId = 0;
                _filter = 0;
                _scroll.verticalNormalizedPosition = 1f;
                _descriptionScroll.verticalNormalizedPosition = 1f;
            }
            Rebuild();
        }
        public void SetStatus(string message, bool error = false)
        {
            _status.text = message ?? string.Empty;
            _status.color = error ? AttributeUiStyle.WarnText : QdaoRefreshArt.Ink;
        }
        public static bool CanUpgrade(SkillBookViewState state, SkillEntryState entry) =>
            SkillBookViewState.CanUpgrade(state, entry);

        private void SubmitUpgrade(uint count)
        {
            var entry = _state?.Snapshot?.Find(_selectedId);
            if (CanUpgrade(_state, entry)) _upgradeAction?.Invoke(entry.Id, entry.Rank, count);
        }
        private void BuildRow(int index)
        {
            var row = new Row();
            row.Button = CharacterAttributeVisuals.Button("SkillRow" + index, _content,
                0f, index * RowStep, 1800f, RowHeight, string.Empty);
            row.Button.Label.gameObject.SetActive(false);
            row.Icon = QdaoUguiFactory.CreateImage("Icon", row.Button.Rect, 8f, 4f, 74f, 74f, null);
            row.Icon.preserveAspect = true;
            row.Name = Text("Name", row.Button.Rect, 138f, 2f, 440f, 78f, string.Empty, 29f);
            row.Rank = Text("Rank", row.Button.Rect, 593f, 2f, 224f, 78f, string.Empty, 27f);
            row.Kind = Text("Kind", row.Button.Rect, 838f, 2f, 215f, 78f, string.Empty, 27f);
            row.Mana = Text("Mana", row.Button.Rect, 1098f, 2f, 435f, 78f, string.Empty, 26f);
            row.Target = Text("Target", row.Button.Rect, 1563f, 2f, 225f, 78f, string.Empty, 26f);
            row.Button.Button.onClick.AddListener(() =>
            {
                _selectedId = row.Id;
                _descriptionScroll.verticalNormalizedPosition = 1f;
                Rebuild();
            });
            // 键盘焦点移到列表外的行时，把对应行滚入可视区域。
            var focus = row.Button.Rect.gameObject.AddComponent<SkillRowFocusRelay>();
            focus.Selected = () => EnsureRowVisible(row.Button.Rect);
            _rows.Add(row);
        }
        private void EnsureRowVisible(RectTransform row)
        {
            float top = -row.anchoredPosition.y;
            float position = _content.anchoredPosition.y;
            if (top < position) position = top;
            else if (top + RowHeight > position + ListHeight) position = top + RowHeight - ListHeight;
            _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(position, 0f,
                Mathf.Max(0f, _content.sizeDelta.y - ListHeight)));
        }
        private bool MatchesFilter(SkillDefinition definition) => _filter switch
        {
            1 => !definition.IsPassive,
            2 => definition.IsPassive,
            3 => definition.IsObstacle,
            4 => definition.IsSupport,
            _ => true,
        };
        private void Rebuild()
        {
            var snapshot = _state?.Snapshot;
            bool readOnly = _state == null || _state.IsReadOnly;
            bool pet = _state?.Context?.IsPet == true;
            bool hasObstacle = snapshot?.Entries.Exists(entry => entry?.Definition?.IsObstacle == true) == true;
            bool hasSupport = snapshot?.Entries.Exists(entry => entry?.Definition?.IsSupport == true) == true;
            if ((_filter == 3 && !hasObstacle) || (_filter == 4 && !hasSupport)) _filter = 0;
            int visibleFilter = 0;
            for (int i = 0; i < _filters.Length; i++)
            {
                bool visible = i < 3 || (i == 3 ? hasObstacle : hasSupport);
                _filters[i].SetVisible(visible);
                if (visible) _filters[i].Rect.anchoredPosition = new Vector2(985f + visibleFilter++ * 184f, -120f);
                bool activeFilter = _filter == i;
                _filters[i].SetText((activeFilter ? "✓ " : string.Empty) + FilterNames[i]);
                CharacterAttributeVisuals.Skin(_filters[i].Plate, activeFilter ? "button_primary" : "button_secondary");
                _filters[i].Label.color = activeFilter ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink;
            }
            _visible.Clear();
            if (snapshot != null)
                foreach (var entry in snapshot.Entries)
                    if (entry?.Definition != null && MatchesFilter(entry.Definition)) _visible.Add(entry);
            if (!_visible.Exists(entry => entry.Id == _selectedId))
                _selectedId = _visible.Count > 0 ? _visible[0].Id : 0;
            while (_rows.Count < _visible.Count) BuildRow(_rows.Count);
            _content.sizeDelta = new Vector2(1800f, Mathf.Max(ListHeight, _visible.Count * RowStep - 4f));
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.Button.SetVisible(i < _visible.Count);
                if (i >= _visible.Count) continue;
                var entry = _visible[i];
                var definition = entry.Definition;
                row.Id = entry.Id;
                row.Icon.sprite = LoadIcon(definition);
                row.Icon.enabled = row.Icon.sprite != null;
                row.Icon.color = !readOnly && entry.Rank == 0 ? new Color(.72f, .72f, .72f, 1f) : Color.white;
                row.Name.text = (entry.Id == _selectedId ? "✓ " : string.Empty) + definition.name;
                row.Rank.text = readOnly ? "未同步" : entry.Rank == 0 ? "未学习" :
                    definition.maxRank > 0 ? $"{entry.Rank}/{definition.maxRank}" : entry.Rank.ToString();
                row.Kind.text = definition.KindLabel;
                row.Mana.text = definition.IsPassive ? "常驻 · 无消耗" :
                    $"{definition.manaPercent * 100:0.#}% / {definition.cooldownRounds}回合";
                row.Target.text = definition.TargetLabel +
                    (definition.IsPassive || definition.targetTeam == "self" || definition.maxTargets == 0 ? string.Empty : " " + definition.maxTargets);
                row.Button.Plate.color = entry.Id == _selectedId ? new Color(.82f, .94f, .85f) : Color.white;
            }
            _empty.gameObject.SetActive(_visible.Count == 0);
            _empty.text = _state?.IsLoading == true ? "正在同步技能…" : pet && (readOnly || snapshot == null) ?
                "宠物技能信息未同步\n同步后可查看已拥有的技能。" : snapshot == null ?
                "技能信息未同步\n请刷新后重试。" : snapshot.Entries.Count == 0 ?
                "暂无可显示的技能" : "此分类暂无技能";

            for (int i = 0; i < _classes.Length; i++)
            {
                _classes[i].SetVisible(readOnly && !pet);
                CharacterAttributeVisuals.Skin(_classes[i].Plate,
                    snapshot?.ClassId == i + 1 ? "button_primary" : "button_secondary");
                _classes[i].Label.color = snapshot?.ClassId == i + 1 ? QdaoRefreshArt.Ivory : QdaoRefreshArt.Ink;
            }
            _classInfo.gameObject.SetActive(!readOnly || pet);
            _classInfo.text = pet ? "宠物技能 · 按实际技能分类展示" :
                snapshot == null ? "等待技能信息" : $"当前角色等级：{snapshot.PlayerLevel} · {snapshot.Entries.Count}项技能";
            _title.text = pet ? "宠物技能" : "角色技能";
            var selected = snapshot?.Find(_selectedId);
            var skill = selected?.Definition;
            string className = !string.IsNullOrEmpty(snapshot?.Title) ? snapshot.Title : pet ?
                _state.Context.DisplayName : skill?.className ?? "职业技能";
            _classLabel.text = readOnly ? $"{className} · 技能信息未同步" : $"{className} · 技能修习";
            _selectedIcon.sprite = skill == null ? null : LoadIcon(skill);
            _selectedIcon.enabled = _selectedIcon.sprite != null;
            _selectedName.text = skill?.name ?? "选择一个技能";
            _description.text = skill?.description ?? (pet ? "同步宠物技能后可查看说明。" : "选择列表中的技能查看说明。");
            float descriptionHeight = Mathf.Max(126f, _description.GetPreferredValues(_description.text, 1140f, 0f).y + 4f);
            _description.rectTransform.sizeDelta = new Vector2(1140f, descriptionHeight);
            _descriptionContent.sizeDelta = new Vector2(1150f, descriptionHeight);
            _condition.text = Condition(skill, selected, readOnly);
            _balance.text = readOnly || snapshot == null ? "金币：—" : $"金币：{snapshot.CurrencyBalance:N0}";
            _cost.text = readOnly || selected == null || selected.Rank >= selected.RankLimit ?
                "下一级消耗：—" : $"下一级消耗：{selected.NextRankCost:N0}";
            bool maxed = !readOnly && selected != null && skill?.maxRank > 0 && selected.Rank >= skill.maxRank;
            _upgrade.SetText(maxed ? "已满级" : selected?.Rank > 0 ? "升级" : "学习");
            bool canUpgrade = CanUpgrade(_state, selected) && _upgradeAction != null;
            _upgrade.SetInteractable(canUpgrade);
            _upgradeTen.SetInteractable(canUpgrade);
            _refresh.SetInteractable(_refreshAction != null && _state?.IsLoading != true && _state?.IsBusy != true && !readOnly);
            if (!string.IsNullOrEmpty(_state?.Error)) SetStatus(_state.Error, true);
            else if (_state?.IsLoading == true) SetStatus("正在同步技能…");
            else if (_state?.IsBusy == true) SetStatus("正在提交修习，请稍候…");
            else if (readOnly) SetStatus(_state?.Message ?? "技能信息未同步 · 仅浏览职业图鉴");
            else if (_state?.IsConnected != true) SetStatus("连接已断开 · 技能修习暂不可用", true);
            else SetStatus(_state?.Message ?? string.Empty);
            ApplyFidelityLayout();
        }
        private string Condition(SkillDefinition skill, SkillEntryState entry, bool readOnly)
        {
            if (skill == null) return string.Empty;
            if (readOnly) return "学习状态未同步\n等级与金币以实际数据为准";
            if (!string.IsNullOrEmpty(entry.LockedReason)) return entry.LockedReason;
            if (skill.maxRank > 0 && entry.Rank >= skill.maxRank) return "已修至圆满";
            if (entry.Rank >= entry.RankLimit) return "已达当前可修习上限";
            if (entry.NextRankCost > _state.Snapshot.CurrencyBalance) return "金币不足，暂不可修习";
            return $"{skill.unlockLevel}级可学\n当前可修至{entry.RankLimit}级";
        }
        private static void AddScrollbar(UnityEngine.Transform parent, ScrollRect scroll, string name,
            float x, float y, float height)
        {
            var track = QdaoUguiFactory.CreateImage(name, parent, x, y, 14f, height, null, true);
            track.color = new Color(.49f, .39f, .22f, .12f);
            var handle = QdaoUguiFactory.CreateImage("Handle", track.transform, 0f, 0f, 14f, height, null, true);
            // Scrollbar 会按可见比例驱动锚点，不能再叠加固定尺寸，否则滑块会超出轨道。
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = Vector2.one;
            handle.rectTransform.offsetMin = Vector2.zero;
            handle.rectTransform.offsetMax = Vector2.zero;
            handle.color = new Color(.32f, .59f, .49f, .8f);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        private static TMP_Text Text(string name, UnityEngine.Transform parent, float x, float y,
            float width, float height, string value, float size, Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = QdaoUguiFactory.CreateText(name, parent, x, y, width, height, value,
                size, color ?? QdaoRefreshArt.Ink, alignment);
            text.richText = false;
            return text;
        }
        private static Sprite LoadIcon(SkillDefinition definition) =>
            QdaoUguiTheme.LoadSprite(definition.IconResource) ??
            QdaoUguiTheme.LoadSprite("UI/Ugui/GameplayV1/icon_scroll");
    }
}
