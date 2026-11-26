using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Skills;
using MmorpgClient.World;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    public enum PetCultivationPage { Attributes, FourStats, Skills, EntryPoints }

    /// <summary>
    /// 50fdfb 美术交接的宝宝培养页。数值只取 PetClient / ISkillBookSource 的权威快照；
    /// 四维草稿只记录新增分配；条目草稿为独立可减目标，目录与收益来自服务器。
    /// </summary>
    public sealed partial class PetCultivationPanel : IDisposable
    {
        private static readonly Color Ink = new Color32(42, 35, 27, 255);
        private static readonly Color Muted = new Color32(91, 74, 53, 255);
        private static readonly Color Green = new Color32(14, 64, 43, 255);
        private static readonly Color Cream = new Color32(255, 241, 205, 255);
        private static readonly Color Blue = new Color32(33, 92, 148, 255);
        private readonly Func<PetClient> _getClient;
        private readonly Func<ISkillBookSource> _getSkills;
        private readonly Func<bool> _battleLocked;
        private readonly Action _openMore;
        private readonly RectTransform _root;
        private RectTransform _body;
        private readonly Dictionary<uint, uint> _draft = new();
        private readonly Dictionary<uint, uint> _baseline = new();
        private readonly List<StatRow> _statRows = new();
        private PetClient _client;
        private ISkillBookSource _skills;
        private PetListInfo _list;
        private PetCultivationPage _page = PetCultivationPage.EntryPoints;
        private ulong _selectedPetId;
        private uint _selectedSkillId;
        private int _skillCategory, _entryCategory;
        private bool _allocationPending;
        private int _allocationAttempt;
        private PetListInfo _deferredList;
        private bool _disposed;
        private float _width = 2560f;
        private string _message;
        private Text _status, _remaining, _budget;
        private Button _save, _discard, _more, _ascendEntry;
        private RectTransform _leaveDialog;
        private Action _afterDiscard;
        private Func<bool> _canDiscard;

        private sealed class StatRow
        {
            public PetDimensionInfo Dimension;
            public Text Current, Increment, After;
            public Button Minus, Plus;
        }

        public bool IsVisible => _root.gameObject.activeSelf;
        public PetCultivationPage Page => _page;
        public ulong SelectedPetId => _selectedPetId;
        public bool HasDraft => _draft.Count != 0 || HasEntryDraft;
        public RectTransform Root => _root;
        private PetInfo Selected => _list?.Pets.FirstOrDefault(p => p.PetId == _selectedPetId);
        private bool BattleLocked => _battleLocked() || _client?.CultivationWritesBlocked == true;
        private bool CanWrite => _client?.IsReady == true && _client.HasList && !_client.Busy && !_allocationPending && !BattleLocked;
        private ulong DraftCount => _draft.Values.Aggregate<uint, ulong>(0, (sum, value) => sum + value);
        private uint Remaining => Selected == null ? 0 : (uint)Math.Max(0L, (long)Selected.RemainingPoints - (long)DraftCount);

        public PetCultivationPanel(UnityEngine.Transform parent, Func<PetClient> client,
            Func<ISkillBookSource> skills, Func<bool> battleLocked, Action openMore = null)
        {
            _getClient = client;
            _getSkills = skills;
            _battleLocked = battleLocked ?? (() => false);
            _openMore = openMore;
            _root = QdaoUguiFactory.CreateCenteredRect("PetCultivationWindow", parent, _width, 1080);
            BindServices();
            Render();
            _root.gameObject.SetActive(false);
        }

        public void Show(PetCultivationPage page = PetCultivationPage.EntryPoints)
        {
            BindServices();
            if (IsVisible && page != _page)
            {
                if (BattleLocked) SelectPage(page); else Navigate(() => SelectPage(page));
                return;
            }
            _page = page;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _message = null;
            Render();
            if (_client?.HasList == true) ApplyList(_client.Pets);
            _client?.RequestList();
            if (_page == PetCultivationPage.Skills) RefreshSkills();
        }

        public void RequestClose()
        {
            if (BattleLocked) Hide(); else Navigate(Hide);
        }
        // External battle/window transitions may hide the view, but never silently discard its draft.
        public void Hide()
        {
            _leaveDialog?.gameObject.SetActive(false);
            _entryTagDialog?.gameObject.SetActive(false);
            _afterDiscard = null;
            _canDiscard = null;
            _ascendVisible = false;
            _ascendError = null;
            _ascendDialog?.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        public void Tick()
        {
            BindServices();
            // PetClient clears its authority on disconnect; an idle disconnect need not change Busy.
            if (_list != null && _client?.HasList != true) ApplyList(null);
            if (!IsVisible) return;
            var safeArea = Screen.safeArea;
#if UNITY_WEBGL && !UNITY_EDITOR
            float capsuleTop = Screen.height - 80f * Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            safeArea.yMax = Mathf.Min(safeArea.yMax, capsuleTop);
#endif
            SetViewport(Screen.width, Screen.height, safeArea);
            RefreshFields();
        }

        public void SetViewport(float width, float height)
            => SetViewport(width, height, new UnityEngine.Rect(0, 0, width, height));

        private void SetViewport(float width, float height, UnityEngine.Rect safeArea)
        {
            if (width <= 0 || height <= 0) return;
            if (safeArea.width <= 0 || safeArea.height <= 0) safeArea = new UnityEngine.Rect(0, 0, width, height);
            float logicalWidth = Mathf.Clamp(1080 * safeArea.width / safeArea.height, 1920, 2560);
            var canvas = _root.GetComponentInParent<Canvas>();
            float canvasScale = canvas == null ? 1 : Mathf.Max(.001f, canvas.rootCanvas.scaleFactor);
            float desiredScale = Mathf.Min(safeArea.width / logicalWidth, safeArea.height / 1080);
            _root.anchoredPosition = (safeArea.center - new Vector2(width / 2, height / 2)) / canvasScale;
            _root.localScale = UnityEngine.Vector3.one * desiredScale / canvasScale;
            if (Mathf.Abs(_width - logicalWidth) < .5f) return;
            _width = logicalWidth;
            _root.sizeDelta = new Vector2(_width, 1080);
            Render();
        }

        private void BindServices()
        {
            var client = _getClient?.Invoke();
            if (!ReferenceEquals(client, _client))
            {
                if (_client != null) { _client.OnList -= ApplyList; _client.OnBusyChanged -= BusyChanged; _client.OnError -= Error; }
                _client = client;
                _list = client?.Pets;
                _draft.Clear(); _baseline.Clear(); _allocationPending = false; ClearEntryDraft(); _entrySaving = false;
                _ascendPending = false; _ascendVisible = false; _ascendError = null;
                ++_allocationAttempt; _deferredList = null;
                if (_client != null) { _client.OnList += ApplyList; _client.OnBusyChanged += BusyChanged; _client.OnError += Error; }
            }
            var skills = _getSkills?.Invoke();
            if (!ReferenceEquals(skills, _skills))
            {
                if (_skills != null) _skills.Changed -= SkillsChanged;
                _skills = skills;
                if (_skills != null) _skills.Changed += SkillsChanged;
            }
        }

        private void ApplyList(PetListInfo list)
        {
            if (list == null)
            {
                ++_allocationAttempt; _allocationPending = false; _deferredList = null; _entrySaving = false; _ascendPending = false;
            }
            else if (_allocationPending)
            {
                // Any list may be a notification or a concurrent read. Only the write callback
                // can acknowledge this draft; retain the latest list until that result arrives.
                _deferredList = list;
                return;
            }
            ulong previous = _selectedPetId;
            _list = list;
            if (Selected == null)
                _selectedPetId = list?.Pets.FirstOrDefault(p => p.PetId == list.ActivePetId)?.PetId ?? list?.Pets.FirstOrDefault()?.PetId ?? 0;
            if (list == null || previous != _selectedPetId) ClearEntryDraft();
            bool changed = Selected == null || previous != _selectedPetId ||
                (_baseline.Count != 0 && (Selected.Dimensions.Count != _baseline.Count ||
                    Selected.Dimensions.Any(d => !_baseline.TryGetValue(d.DimensionId, out var value) || value != d.Allocated))) ||
                (Selected != null && (DraftCount > Selected.RemainingPoints || Selected.Dimensions.Any(d =>
                    _draft.TryGetValue(d.DimensionId, out var increment) && d.Cap != 0 && (ulong)d.Allocated + increment > d.Cap)));
            if (changed && _draft.Count != 0) _message = "宝宝属性已更新，本次草稿已撤回，请按最新属性重新分配。";
            if (changed) _draft.Clear();
            RememberBaseline();
            if (list == null) { _draft.Clear(); _message = "连接已断开，宝宝信息未同步。"; }
            Render();
            if (_page == PetCultivationPage.Skills && previous != _selectedPetId) RefreshSkills();
        }

        private void RememberBaseline()
        {
            _baseline.Clear();
            if (Selected != null) foreach (var d in Selected.Dimensions) _baseline[d.DimensionId] = d.Allocated;
        }
        private void BusyChanged(bool busy) { if (_client?.HasList != true) ApplyList(null); else RefreshFields(); }
        private void Error(string message) { _message = message; RefreshFields(); }
        private void SkillsChanged() { if (_page == PetCultivationPage.Skills) Render(); }
        private SkillBookContext Context => Selected == null ? null : SkillBookContext.Pet(Selected.PetId, Selected.Name);
        private SkillBookViewState SkillState => Context?.Matches(_skills?.State?.Context) == true ? _skills.State : null;
        private void RefreshSkills() { if (Context != null) _skills?.Refresh(Context); else Render(); }
        private void SelectPage(PetCultivationPage page)
        {
            _page = page; _message = null; Render();
            if (page == PetCultivationPage.Skills) RefreshSkills();
        }
        private void SelectPet(ulong id)
        {
            if (_selectedPetId == id) return;
            if (BattleLocked && HasDraft)
            {
                _message = "已保留当前宝宝的加点草稿；战斗结束后再切换宝宝。";
                RefreshFields();
                return;
            }
            Navigate(() => { _selectedPetId = id; ClearEntryDraft(); _selectedSkillId = 0; _message = null; RememberBaseline(); Render();
                if (_page == PetCultivationPage.Skills) RefreshSkills(); });
        }

        private void Navigate(Action action, Func<bool> canDiscard = null)
        {
            if (_client?.Busy == true || SkillState?.IsBusy == true) { _message = "正在等待服务器确认，请稍候。"; RefreshFields(); return; }
            if (!HasDraft) { action(); return; }
            _afterDiscard = action;
            _canDiscard = canDiscard;
            BuildLeaveDialog();
        }

        private void BuildLeaveDialog()
        {
            if (_leaveDialog != null) DestroyUi(_leaveDialog.gameObject);
            _leaveDialog = Rect("PetDraftDialog", _root, 0, 0, _width, 1080);
            var blocker = _leaveDialog.gameObject.AddComponent<Image>(); blocker.color = new Color(0, 0, 0, .3f);
            var box = Art("PetDraftBox", _leaveDialog, (_width - 680) / 2, 360, 680, 300, "window_frame");
            Label("DraftQuestion", box.rectTransform, 55, 40, 570, 95, "尚有未保存的加点\n返回继续分配，或放弃本次草稿。", 28, Ink, TextAnchor.MiddleCenter);
            ActionButton("KeepDraft", box.rectTransform, 55, 180, 250, 65, "继续分配", true,
                () => { _leaveDialog.gameObject.SetActive(false); _afterDiscard = null; _canDiscard = null; });
            ActionButton("DiscardAndLeave", box.rectTransform, 350, 180, 275, 65, "放弃并离开", false,
                () =>
                {
                    _leaveDialog.gameObject.SetActive(false);
                    var next = _afterDiscard;
                    bool canDiscard = _canDiscard?.Invoke() != false;
                    _afterDiscard = null;
                    _canDiscard = null;
                    if (next == null || !canDiscard) return;
                    _draft.Clear();
                    ClearEntryDraft();
                    next.Invoke();
                });
        }

        private void Render()
        {
            if (_disposed) return;
            for (int i = _root.childCount - 1; i >= 0; --i) DestroyUi(_root.GetChild(i).gameObject);
            _statRows.Clear(); _leaveDialog = null; _remaining = _budget = null; _save = _discard = _more = _ascendEntry = null;
            _afterDiscard = null; _canDiscard = null;
            _entryClearAll = _entryTagButton = null; _entryPresetButtons.Clear(); _entryTagDialog = _entryDetailDialog = null;
            _ascendConfirm = null; _ascendHint = null; _ascendDialog = _badgeDialog = null;
            _skillUpgrade = _skillUpgradeTen = null;
            float t = Mathf.InverseLerp(1920, 2560, _width);
            float margin = Mathf.Lerp(10, 72, t);
            float shellW = _width - 2 * margin, scaleX = shellW / 1932f;
            float leftX = margin + 162 * scaleX, leftW = 584 * scaleX;
            float contentX = margin + 763 * scaleX, contentW = 1004 * scaleX;
            Art("CultivationFrame", _root, margin, 18, shellW, 986, "guild_empty_shell");
            Art("CultivationFrameInput", _root, margin + 125 * scaleX, 88, 1660 * scaleX, 883, "shell-close-hit", true);
            Label("CultivationTitle", _root, (_width - 520) / 2, 58, 520, 63, "宝宝培养", 47, Cream, TextAnchor.MiddleCenter);
            ActionButton("CultivationClose", _root, margin + 1780 * scaleX, 43, 86 * scaleX, 99, "", false, RequestClose, "shell-close-hit");
            _body = Rect("CultivationBody", _root, 0, 24, _width, 1000);
            Label("PetRosterHeadingText", _root, leftX + 26, 214, leftW - 52, 43, "我的宝宝", 33, Green);
            GuildUiArt.Line(_root, "PetRosterHeadingRule", leftX + 179, 239, leftW - 205);
            BuildRoster(leftX, leftW);
            string[] tabs = { "属性", "加点", "技能", "条目点" };
            for (int i = 0; i < tabs.Length; ++i)
            {
                var page = (PetCultivationPage)i;
                ActionButton("Tab" + tabs[i], _root, margin + (187 + i * 290) * scaleX, 136, 278 * scaleX, 53, tabs[i], _page == page,
                    () => { if (BattleLocked) SelectPage(page); else Navigate(() => SelectPage(page)); },
                    _page == page ? "mail_tab_selected" : "mail_tab_normal", 31);
            }
            if (_openMore != null)
            {
                float btnW = Mathf.Floor((leftW - 28f) / 3f);
                _more = ActionButton("PetMore", _root, leftX + 6, 902, btnW, 54, "更多培养", false,
                    () => { if (!BattleLocked) Navigate(() => { Hide(); _openMore(); }); }, null, 22);
                ActionButton("PetBadgeExplain", _root, leftX + 14 + btnW, 902, btnW, 54, "五阶徽章", false,
                    () => { _badgesVisible = true; BuildBadges(); }, null, 22);
                _ascendEntry = ActionButton("PetAscendTier", _root, leftX + 22 + btnW * 2f, 902, btnW, 54,
                    Selected?.CultivationTier >= 5 ? "已达巅峰" : "灵阶突破", HasNextTier(Selected), OpenAscendDialog, null, 22);
            }
            else
            {
                float btnW = Mathf.Floor((leftW - 24f) * 0.5f);
                ActionButton("PetBadgeExplain", _root, leftX + 8, 902, btnW, 54, "徽章说明", false,
                    () => { _badgesVisible = true; BuildBadges(); }, null, 24);
                _ascendEntry = ActionButton("PetAscendTier", _root, leftX + 16 + btnW, 902, btnW, 54,
                    Selected?.CultivationTier >= 5 ? "已达巅峰" : "灵阶突破", HasNextTier(Selected), OpenAscendDialog, null, 24);
            }
            _status = Label("PetCultivationStatus", _root, contentX + 24, 870, contentW - 48, 28, "", 22, Muted);
            switch (_page)
            {
                case PetCultivationPage.EntryPoints: BuildEntryPoints(contentX, contentW); break;
                case PetCultivationPage.FourStats: BuildFourStats(contentX, contentW); break;
                case PetCultivationPage.Skills: BuildSkills(contentX, contentW); break;
                default: BuildAttributes(contentX, contentW); break;
            }
            RefreshFields();
            if (_badgesVisible) BuildBadges();
            if (_ascendVisible) BuildAscendDialog();
        }

        private void BuildRoster(float x, float width)
        {
            Scroll("PetRoster", _root, x + 15, 264, width - 30, 612, out var content);
            int count = _list?.Pets.Count ?? 0;
            content.sizeDelta = new Vector2(width - 30, Mathf.Max(612, count * 121));
            if (count == 0)
                Label("PetRosterEmpty", content, 20, 170, width - 70, 150,
                    _client?.ListRequestPending == true ? "正在同步宝宝信息…" : "暂无宝宝\n获得宝宝后可在此培养", 26, Muted, TextAnchor.MiddleCenter);
            for (int i = 0; i < count; ++i)
            {
                var pet = _list.Pets[i]; bool selected = pet.PetId == _selectedPetId;
                var button = ActionButton("PetItem" + i, content, 0, i * 121, width - 30, 120, "", selected,
                    () => SelectPet(pet.PetId), selected ? "pet_card_selected" : "mail_row_normal");
                var parent = (RectTransform)button.transform;
                if (selected) Art("PetSelectedMark" + i, parent, 12, 47, 25, 25, "guild_self_mark").preserveAspect = true;
                var portrait = Art("PetPortrait" + i, parent, 49, 16, 88, 88, "round_badge_taiji");
                portrait.sprite = Portrait(pet); portrait.preserveAspect = true;
                Art("PetPortraitRing" + i, parent, 44, 11, 98, 98, string.IsNullOrEmpty(pet.CultivationArtKey) ? "portrait_frame" : "ring-" + pet.CultivationArtKey);
                Label("PetName" + i, parent, 159, 15, width - 206, 48, pet.Name, 34, selected ? Green : Ink);
                Label("PetLevel" + i, parent, 159, 66, width - 278, 37, $"{pet.Level}级 · {(string.IsNullOrEmpty(pet.CultivationTierName) ? "—" : pet.CultivationTierName)}", 23, Ink);
                Label("PetState" + i, parent, width - 138, 73, 90, 34, pet.IsActive ? "出战" : "休息", 20,
                    pet.IsActive ? Green : Muted, TextAnchor.MiddleRight);
                GuildUiArt.Line(parent, "PetRosterRule" + i, 3, 119, width - 36);
            }
        }

        private void BuildFourStats(float x, float width)
        {
            Label("FourStatsTitle", _body, x + 30, 196, 220, 48, "四维加点", 31, Ink);
            _remaining = Label("FourStatsRemaining", _body, x + 258, 196, 360, 48, "剩余属性点 —", 29, Green);
            _budget = Label("FourStatsBudget", _body, x + width - 460, 205, 428, 34, "", 21, Muted, TextAnchor.MiddleRight);
            float rowX = x + 22, rowW = width - 44, current = rowW * .35f, increment = rowW * .59f, after = rowW * .85f;
            Label("FourStatsNameHeader", _body, rowX + 55, 275, 180, 36, "属性", 23, Muted);
            Label("FourStatsCurrentHeader", _body, rowX + current - 90, 275, 180, 36, "当前投入", 23, Muted, TextAnchor.MiddleCenter);
            Label("FourStatsIncrementHeader", _body, rowX + increment - 90, 275, 180, 36, "本次新增", 23, Muted, TextAnchor.MiddleCenter);
            Label("FourStatsAfterHeader", _body, rowX + after - 90, 275, 180, 36, "调整后", 23, Muted, TextAnchor.MiddleCenter);
            var viewport = Scroll("FourStats", _body, rowX, 325, rowW, 465, out var content);
            int count = Selected?.Dimensions.Count ?? 0;
            content.sizeDelta = new Vector2(rowW, Mathf.Max(465, count * 110));
            for (int i = 0; i < count; ++i)
            {
                var d = Selected.Dimensions[i];
                var row = new StatRow { Dimension = d }; _statRows.Add(row);
                var plate = Art("FourStatRow" + i, content, 0, i * 110, rowW, 96, "mail_row_normal").rectTransform;
                Label("FourStatName" + i, plate, 55, 25, current - 150, 48, d.Name, 30, Ink);
                row.Current = Label("FourStatCurrent" + i, plate, current - 65, 27, 130, 44, "", 27, Ink, TextAnchor.MiddleCenter);
                row.Minus = ActionButton("Row" + i + "Minus", plate, increment - 111, 21, 54, 54, "", false,
                    () => Step(d.DimensionId, -1), "step-minus-normal");
                Art("FourStatIncrementField" + i, plate, increment - 44, 25, 88, 46, "stat_field");
                row.Increment = Label("FourStatIncrement" + i, plate, increment - 44, 25, 88, 46, "", 26, Blue, TextAnchor.MiddleCenter);
                row.Plus = ActionButton("Row" + i + "Plus", plate, increment + 57, 21, 54, 54, "", false,
                    () => Step(d.DimensionId, 1), "step-plus-normal");
                Art("FourStatAfterField" + i, plate, after - 48, 25, 96, 46, "stat_field");
                row.After = Label("FourStatAfter" + i, plate, after - 48, 25, 96, 46, "", 26, Ink, TextAnchor.MiddleCenter);
            }
            if (count == 0) Label("FourStatsEmpty", content, 20, 150, rowW - 40, 90, Selected == null ? "请先选择宝宝" : "暂无可分配维度", 28, Muted, TextAnchor.MiddleCenter);
            Label("FourStatsHelp", _body, x + 24, 806, width - 48, 42, "减号仅撤回本次尚未保存的新增属性点。", 21, Muted);
            _save = ActionButton("Confirm", _body, x + width - 479, 878, 215, 54, "保存加点", true, Save);
            _discard = ActionButton("Discard", _body, x + width - 246, 878, 224, 54, "恢复当前分配", false,
                () => { if (!CanWrite) return; _draft.Clear(); _message = "已恢复当前分配，未发送保存请求。"; RefreshFields(); }, null, 26);
        }

        private void Step(uint dimensionId, int delta)
        {
            if (!CanWrite || _page != PetCultivationPage.FourStats || Selected == null) return;
            var dimension = Selected.Dimensions.FirstOrDefault(d => d.DimensionId == dimensionId);
            if (dimension == null) return;
            _draft.TryGetValue(dimensionId, out var current);
            if (delta > 0 && Remaining > 0 && current < uint.MaxValue - dimension.Allocated &&
                (dimension.Cap == 0 || dimension.Allocated + current < dimension.Cap)) _draft[dimensionId] = current + 1;
            if (delta < 0 && current > 0) { if (current == 1) _draft.Remove(dimensionId); else _draft[dimensionId] = current - 1; }
            _message = null; RefreshFields();
        }

        private void Save()
        {
            if (!CanWrite || !HasDraft || Selected == null || _page != PetCultivationPage.FourStats) return;
            var target = new Dictionary<uint, uint>();
            foreach (var d in Selected.Dimensions)
                if (_draft.TryGetValue(d.DimensionId, out var increment)) target[d.DimensionId] = d.Allocated + increment;
            _allocationPending = true; _message = "正在保存，等待服务器确认…";
            int attempt = ++_allocationAttempt;
            ulong petId = _selectedPetId;
            _client.Allocate(petId, target, (success, response) =>
            {
                if (_disposed || attempt != _allocationAttempt) return;
                _allocationPending = false;
                var returnedPet = response?.Pets.FirstOrDefault(p => p.PetId == petId);
                bool reached = success && returnedPet != null && target.All(pair =>
                    returnedPet.Dimensions.Any(d => d.DimensionId == pair.Key && d.Allocated >= pair.Value));
                var authoritative = response ?? _deferredList ?? _client.Pets;
                _deferredList = null;
                if (reached) { _draft.Clear(); _message = "加点已保存，以服务器返回的属性为准。"; }
                else if (success) _message = "服务器尚未确认目标分配，草稿已保留，可重试。";
                if (authoritative != null) ApplyList(authoritative);
                RefreshFields();
            });
            RefreshFields();
        }

        private void RefreshFields()
        {
            if (_remaining != null) _remaining.text = Selected == null ? "剩余属性点 —" : "剩余属性点 " + Remaining;
            if (_budget != null) _budget.text = Selected == null ? "原有可分配 — · 本次已分配 —" : $"原有可分配 {Selected.RemainingPoints} · 本次已分配 {DraftCount}";
            foreach (var row in _statRows)
            {
                _draft.TryGetValue(row.Dimension.DimensionId, out var increment);
                ulong after = (ulong)row.Dimension.Allocated + increment;
                row.Current.text = row.Dimension.Allocated.ToString(); row.Increment.text = increment == 0 ? "0" : "+" + increment; row.After.text = after.ToString();
                row.Minus.interactable = CanWrite && increment > 0;
                row.Plus.interactable = CanWrite && Remaining > 0 && after < uint.MaxValue && (row.Dimension.Cap == 0 || after < row.Dimension.Cap);
            }
            if (_save != null) _save.interactable = _page == PetCultivationPage.FourStats && CanWrite && _draft.Count != 0;
            if (_discard != null) _discard.interactable = _page == PetCultivationPage.FourStats && CanWrite && _draft.Count != 0;
            if (_more != null) _more.interactable = !BattleLocked;
            if (_ascendEntry != null) _ascendEntry.interactable = HasNextTier(Selected);
            RefreshAscendDialogButtons();
            var skillEntry = SkillState?.Snapshot?.Find(_selectedSkillId);
            bool canLearn = !BattleLocked && SkillBookViewState.CanUpgrade(SkillState, skillEntry);
            if (_skillUpgrade != null) _skillUpgrade.interactable = canLearn;
            if (_skillUpgradeTen != null) _skillUpgradeTen.interactable = canLearn;
            if (_status != null) _status.text = BattleLocked ? (HasDraft ?
                "战斗中只读；当前宝宝的加点草稿已保留，关闭可返回战斗。" : "战斗中只读查看；不能修改培养，关闭可返回战斗。") :
                _client?.IsReady != true ? "连接未就绪，无法保存。" : _message ??
                (_page == PetCultivationPage.EntryPoints && EntryState == null ? "条目点尚未同步，请刷新宝宝列表。" : "");
            RefreshEntryFields();
        }

        private void BuildUnavailableEntries(float x, float width)
        {
            Label("EntryPointsTitle", _body, x + 30, 197, 120, 45, "条目点", 28, Ink);
            Label("EntryPointsBudget", _body, x + 155, 198, width - 190, 45, "总点数 —       已用 —       剩余 —", 26, Ink);
            string[] categories = { "全部", "输出", "障碍", "辅助" };
            float catW = (width - 98) / 4;
            for (int i = 0; i < categories.Length; i++)
            {
                int index = i;
                ActionButton("EntryFilter" + i, _body, x + 24 + i * (catW + 16), 253, catW, 47, categories[i], i == _entryCategory,
                    () => { _entryCategory = index; Render(); }, i == _entryCategory ? "mail_tab_selected" : "mail_tab_normal", 24);
            }
            float rowX = x + 22, rowW = width - 44, current = rowW * .41f, adjust = rowW * .64f, detail = rowW * .91f;
            Label("EntryDirectionHeader", _body, rowX + 79, 318, 250, 36, "分配方向", 22, Muted);
            Label("EntryCurrentHeader", _body, rowX + current - 80, 318, 160, 36, "当前投入", 22, Muted, TextAnchor.MiddleCenter);
            Label("EntryDraftHeader", _body, rowX + adjust - 80, 318, 160, 36, "调整后", 22, Muted, TextAnchor.MiddleCenter);
            string[] names = { "物理伤害", "法术伤害", "封物命中", "混乱命中", "治疗效果", "护盾容量" };
            string[] icons = { "physical", "magic", "seal", "confusion", "healing", "shield" };
            int visible = 0;
            for (int i = 0; i < names.Length; ++i)
            {
                if (_entryCategory != 0 && i / 2 + 1 != _entryCategory) continue;
                var row = Art("EntryRow" + i, _body, rowX, 359 + visible++ * 81, rowW, 72, "mail_row_normal").rectTransform;
                Art("EntryIcon" + i, row, 19, 13, 46, 46, "row_" + icons[i]);
                Label("EntryName" + i, row, 79, 14, current - 150, 44, names[i], 28, Ink);
                Label("EntryCurrent" + i, row, current - 45, 15, 90, 44, "—", 24, Ink, TextAnchor.MiddleCenter);
                ActionButton("EntryMinus" + i, row, adjust - 105, 10, 52, 51, "", false, null, "step-minus-disabled").interactable = false;
                Art("EntryField" + i, row, adjust - 41, 14, 82, 44, "stat_field");
                Label("EntryValue" + i, row, adjust - 41, 14, 82, 44, "—", 23, Ink, TextAnchor.MiddleCenter);
                ActionButton("EntryPlus" + i, row, adjust + 53, 10, 52, 51, "", false, null, "step-plus-disabled").interactable = false;
                ActionButton("EntryDetail" + i, row, detail - 49, 13, 98, 46, "详情", false,
                    () => { _message = "条目点尚未同步；请刷新宝宝列表获取服务器总点数、收益和上限。"; RefreshFields(); }, null, 21);
            }
            Label("EntryHelp", _body, x + 24, 839, width - 48, 40, "各方向共用条目点；未投入也可使用已学技能。", 20, Muted);
            _save = ActionButton("EntrySave", _body, x + width - 479, 878, 215, 54, "保存分配", true, null); _save.interactable = false;
            _discard = ActionButton("EntryRestore", _body, x + width - 246, 878, 224, 54, "恢复当前分配", false, null, null, 26); _discard.interactable = false;
        }

        private void BuildAttributes(float x, float width)
        {
            var pet = Selected;
            Label("PetOverviewTitle", _body, x + 30, 193, width - 60, 48, pet?.Name ?? "宝宝属性", 36, Green);
            GuildUiArt.Line(_body, "PetOverviewHeadingRule", x + 208, 222, width - 249);
            var d = pet?.Derived;
            string[] names = { "气血", "法力", "物理攻击", "法术攻击", "防御", "速度" };
            string[] values = d == null ? Enumerable.Repeat("—", 6).ToArray() : new[] {
                $"{d.Health}/{d.MaxHealth}", $"{d.Mana}/{d.MaxMana}", d.PhysicalAttack.ToString(), d.MagicAttack.ToString(), d.Defense.ToString(), d.Speed.ToString() };
            for (int i = 0; i < names.Length; i++)
            {
                var row = Art("PetOverview" + i, _body, x + 24, 270 + i * 71, width - 48, 71, "mail_row_normal").rectTransform;
                Label("PetOverviewLabel" + i, row, 40, 12, 260, 44, names[i], 28, Ink);
                Label("PetOverviewValue" + i, row, width - 450, 12, 350, 44, values[i], 28, Ink, TextAnchor.MiddleRight);
                GuildUiArt.Line(row, "PetOverviewRule" + i, 4, 70, width - 56);
            }
            bool isMaxTier = pet?.CultivationTier >= 5;
            float buttonWidth = isMaxTier ? 370 : 216;
            Label("PetOverviewGrowth", _body, x + 30, 734, width - 72 - buttonWidth, 44,
                pet == null ? "等级 —   成长率 —" : $"等级 {pet.Level}   成长率 {pet.Growth / 100f:0.##}%   灵阶：{(string.IsNullOrEmpty(pet.CultivationTierName) ? "—" : pet.CultivationTierName)}",
                25, Ink);
            if (pet != null)
            {
                bool hasNextTier = HasNextTier(pet);
                string tierHint = hasNextTier
                    ? $"下阶：{pet.NextTier.NextTierName}（需 {pet.NextTier.MinPetLevel} 级）"
                    : isMaxTier ? "" : "灵阶突破条件尚未同步。";
                Label("PetOverviewTierHint", _body, x + 30, 779, width - 290, 36, tierHint, 23, Green);
                var btn = ActionButton("PetOverviewAscend", _body, x + width - 30 - buttonWidth, 746, buttonWidth, 60,
                    isMaxTier ? MaxTierLabel : "灵阶突破", pet.NextTier?.CanAscend == true, OpenAscendDialog, null, 24);
                btn.interactable = hasNextTier;
            }
        }

        private static Sprite Portrait(PetInfo pet)
        {
            if (pet != null && !string.IsNullOrEmpty(pet.CosmeticArtKey))
            {
                var cosmetic = QdaoPetCatalog.LoadPortrait(pet.CosmeticArtKey);
                if (cosmetic != null) return cosmetic;
            }
            var entry = QdaoPetCatalog.Resolve(pet.PetTableId, pet.ModelId);
            if (entry?.id == "legacy-ling-yue") return CharacterPetArt.Get("portrait_lingyue");
            if (entry?.id == "legacy-yun-jiu-jiu") return CharacterPetArt.Get("portrait_yunjiujiu");
            return QdaoPetCatalog.LoadPortrait(entry?.id) ?? CharacterPetArt.Get("round_badge_taiji");
        }

        private static RectTransform Rect(string name, UnityEngine.Transform parent, float x, float y, float w, float h)
            => QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
        private static Image Art(string name, UnityEngine.Transform parent, float x, float y, float w, float h, string key, bool raycast = false)
        {
            string guildKey = key switch
            {
                "button_primary" => "guild_button_primary",
                "button_secondary" => "guild_button_secondary",
                "mail_tab_selected" => "guild_tab_selected",
                "mail_tab_normal" => "guild_tab_normal",
                "mail_row_normal" => "guild_row_normal",
                "pet_card_selected" => "guild_row_self",
                "close-jade" => "guild_close",
                _ when key.StartsWith("guild_", StringComparison.Ordinal) => key,
                _ => null,
            };
            var sprite = guildKey == null ? null : Resources.Load<Sprite>(GuildUiArt.FidelityRoot + guildKey);
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite ?? CharacterPetArt.Get(key), raycast);
            if (key == "shell-close-hit") image.color = Color.clear;
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            // render.ps1 scales the slice borders separately from the stretchable centers.
            float borderScale = key == "application_card" ? .85f : key == "mail_row_normal" ? (h >= 120 && h <= 125 ? 1 : .85f) :
                key == "mail_tab_normal" || key == "mail_tab_selected" ? (h <= 49 ? .78f : .9f) :
                key == "button_primary" || key == "button_secondary" ? (h <= 46 ? .67f : .9f) : 1;
            image.pixelsPerUnitMultiplier = 1 / borderScale;
            if (key == "close-jade") image.preserveAspect = true;
            return image;
        }
        private static Text Label(string name, UnityEngine.Transform parent, float x, float y, float w, float h, string value,
            int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = CharacterPetArt.Font; text.fontSize = size; text.color = color; text.alignment = alignment;
            text.text = value ?? ""; text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private static Button ActionButton(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string text, bool primary, Action clicked, string key = null, int fontSize = 28)
        {
            var art = Art(name + "Plate", parent, x, y, w, h, key ?? (primary ? "button_primary" : "button_secondary"), true);
            var button = art.gameObject.AddComponent<Button>(); button.targetGraphic = art;
            var colors = button.colors; colors.highlightedColor = new Color(1, .98f, .86f); colors.pressedColor = new Color(.8f, .9f, .82f);
            colors.disabledColor = new Color(.7f, .7f, .7f, .8f); button.colors = colors;
            if (key == "step-plus-normal" || key == "step-minus-normal")
            {
                string stem = key.Substring(0, key.Length - "normal".Length);
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState {
                    highlightedSprite = CharacterPetArt.Get(stem + "focus"), selectedSprite = CharacterPetArt.Get(stem + "focus"),
                    pressedSprite = CharacterPetArt.Get(stem + "pressed"), disabledSprite = CharacterPetArt.Get(stem + "disabled") };
            }
            if (clicked != null) button.onClick.AddListener(() => clicked());
            if (!string.IsNullOrEmpty(text)) Label(name + "Text", art.rectTransform, 0, 0, w, h, text, fontSize,
                primary ? Cream : Ink, TextAnchor.MiddleCenter);
            return button;
        }
        private static ScrollRect Scroll(string name, UnityEngine.Transform parent, float x, float y, float w, float h, out RectTransform content)
        {
            var viewport = Rect(name + "Viewport", parent, x, y, w, h);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect(name + "Content", viewport, 0, 0, w, h);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 45;
            return scroll;
        }
        private static void DestroyUi(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go);
        }
        public void Dispose()
        {
            _disposed = true;
            if (_client != null) { _client.OnList -= ApplyList; _client.OnBusyChanged -= BusyChanged; _client.OnError -= Error; }
            if (_skills != null) _skills.Changed -= SkillsChanged;
            DestroyUi(_root.gameObject);
        }
    }
}
