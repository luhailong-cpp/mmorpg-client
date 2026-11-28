using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Skills;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    public enum PetCultivationPage { Attributes, FourStats, Skills, EntryPoints }

    /// <summary>
    /// 帮会画风宝宝培养主窗口（原生 1932×814 1:1 零拉伸底壳 + 高清 SimKai TMP 楷体）。
    /// 数值只取 PetClient / ISkillBookSource 权威快照；
    /// 四维草稿只记录新增分配；条目草稿为独立可减目标，目录与收益来自服务器。
    /// </summary>
    public sealed partial class PetCultivationPanel : IDisposable
    {
        private const float ShellNativeWidth = 1932f;
        private const float ShellNativeHeight = 814f;
        private const float ShellVisibleWidth = 1915f; // guild_empty_shell.png 右侧 17px 为全透明留白
        private const float ShellTopY = 133f;          // (1080 - 814) / 2，避让小游戏右上胶囊并保留底部玉兔花丛
        private const float LeftColX = 183f;           // 对齐帮会原图 approved-guild-reference.png 左栏
        private const float LeftColWidth = 544f;
        private const float RightColX = 795f;          // 对齐帮会原图右栏与 guild_table_header/guild_row 957 原宽
        private const float RightColWidth = 957f;
        private const float FooterSecondaryY = 702f;
        private const float FooterSecondaryHeight = 58f; // 对齐 guild_button_secondary 原高 58
        private const float FooterPrimaryY = 696f;
        private const float FooterPrimaryHeight = 68f;   // 舒展呈现 guild_button_primary 金云纹与厚绿玉芯

        // 从帮会首要标准原图 approved-guild-reference.png 实测提取的墨色与鎏金玉白
        private static readonly Color RichInk = new Color32(18, 13, 8, 255);      // 顶页签常态 / 次按钮 / 未选名
        private static readonly Color Ink = new Color32(31, 29, 27, 255);         // 列表正文与数值
        private static readonly Color Muted = new Color32(84, 68, 48, 255);       // 表头与辅助说明
        private static readonly Color Green = new Color32(0, 41, 19, 255);        // 栏标题 / 选中宝宝名 / 选中技能名（深玉墨绿）
        private static readonly Color SelectedInk = new Color32(6, 34, 17, 255);  // 选中行副文
        private static readonly Color TitleCream = new Color32(254, 250, 225, 255); // 顶部大标题玉白
        private static readonly Color Cream = new Color32(255, 252, 202, 255);      // 选中页签与绿底主钮鎏金米白
        private static readonly Color TabGoldRule = new Color32(227, 198, 129, 255);
        private static readonly Color Blue = new Color32(22, 82, 136, 255);
        private static readonly Color BackdropDim = new Color(0.02f, 0.065f, 0.05f, 0.56f);
        private static readonly Color ModalDim = new Color(0.015f, 0.05f, 0.04f, 0.62f);

        private readonly Func<PetClient> _getClient;
        private readonly Func<ISkillBookSource> _getSkills;
        private readonly Func<bool> _battleLocked;
        private readonly Action _openMore;
        private readonly RectTransform _root;
        private RectTransform _shell;
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
        private float _width = 1920f;
        private string _message;
        private TMP_Text _status, _remaining, _budget;
        private Button _save, _discard, _more, _ascendEntry;
        private RectTransform _leaveDialog;
        private Action _afterDiscard;
        private Func<bool> _canDiscard;

        private sealed class StatRow
        {
            public PetDimensionInfo Dimension;
            public TMP_Text Current, Increment, After;
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
        private float ShellX => Mathf.Round((_width - ShellVisibleWidth) * 0.5f);
        private float ShellScale => _width > 1940f ? Mathf.Min((_width - 80f) / ShellVisibleWidth, (1080f - 40f) / ShellNativeHeight) : 1f;

        private RectTransform CreateCenteredShell(string name, UnityEngine.Transform parent)
        {
            var shell = QdaoUguiFactory.CreateCenteredRect(name, parent, ShellNativeWidth, ShellNativeHeight);
            float scale = ShellScale;
            // 右侧 17px 为全透明留白，按视觉中心补偿 +8.5px 使灯笼与关闭钮左右完全对称
            shell.anchoredPosition = new Vector2(Mathf.Round(8.5f * scale), 0f);
            shell.localScale = new UnityEngine.Vector3(scale, scale, 1f);
            return shell;
        }

        public PetCultivationPanel(UnityEngine.Transform parent, Func<PetClient> client,
            Func<ISkillBookSource> skills, Func<bool> battleLocked, Action openMore = null)
        {
            _getClient = client;
            _getSkills = skills;
            _battleLocked = battleLocked ?? (() => false);
            _openMore = openMore;
            _root = QdaoUguiFactory.CreateCenteredRect("PetCultivationWindow", parent, _width, 1080f);
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
            float logicalWidth = Mathf.Clamp(Mathf.Round(1080f * safeArea.width / safeArea.height), 1920f, 2560f);
            var canvas = _root.GetComponentInParent<Canvas>();
            float canvasScale = canvas == null ? 1f : Mathf.Max(.001f, canvas.rootCanvas.scaleFactor);
            float desiredScale = Mathf.Min(safeArea.width / logicalWidth, safeArea.height / 1080f);
            _root.anchoredPosition = (safeArea.center - new Vector2(width * 0.5f, height * 0.5f)) / canvasScale;
            float ratio = desiredScale / canvasScale;
            if (Mathf.Abs(ratio - 1f) < 0.0005f) ratio = 1f;
            _root.localScale = new UnityEngine.Vector3(ratio, ratio, 1f);
            if (Mathf.Abs(_width - logicalWidth) < .5f) return;
            _width = logicalWidth;
            _root.sizeDelta = new Vector2(_width, 1080f);
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
            _leaveDialog = Rect("PetDraftDialog", _root, 0, 0, _width, 1080f);
            var blocker = _leaveDialog.gameObject.AddComponent<Image>();
            blocker.color = ModalDim;
            blocker.raycastTarget = true;
            var box = Art("PetDraftBox", _leaveDialog, Mathf.Round((_width - 680f) * 0.5f), 390f, 680f, 300f, "guild_announcement", true).rectTransform;
            Label("DraftQuestion", box, 55f, 44f, 570f, 96f, "尚有未保存的加点\n返回继续分配，或放弃本次草稿。",
                26f, Ink, TextAlignmentOptions.Center, wrap: true);
            ActionButton("KeepDraft", box, 68f, 184f, 250f, 58f, "继续分配", true,
                () => { _leaveDialog.gameObject.SetActive(false); _afterDiscard = null; _canDiscard = null; }, null, 24f);
            ActionButton("DiscardAndLeave", box, 362f, 184f, 250f, 58f, "放弃并离开", false,
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
                }, null, 24f);
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

            // 全屏柔和深玉暗幕，拦截点击穿透并衬托 1:1 原比例主底壳
            var backdrop = Rect("PetCultivationBackdrop", _root, 0, 0, _width, 1080f);
            var backdropImg = backdrop.gameObject.AddComponent<Image>();
            backdropImg.color = BackdropDim;
            backdropImg.raycastTarget = true;

            // 1932×814 原生零拉伸底壳容器（1920×1080 严格 1:1；2560×1080 同帮会窗等比放大铺满，灯笼/玉兔/关闭钮零变形）
            _shell = CreateCenteredShell("PetCultivationShell", _root);
            Art("CultivationFrame", _shell, 0f, 0f, ShellNativeWidth, ShellNativeHeight, "guild_empty_shell");
            Art("CultivationFrameInput", _shell, 125f, 70f, 1660f, 712f, "shell-close-hit", true);
            Label("CultivationTitle", _shell, 726f, 20f, 480f, 62f, "宝宝培养", 46f, TitleCream,
                TextAlignmentOptions.Center, role: QdaoUiTypographyRole.Title);
            ActionButton("CultivationClose", _shell, 1780f, 22f, 88f, 84f, "", false, RequestClose, "shell-close-hit");

            _body = Rect("CultivationBody", _shell, 0f, 0f, ShellNativeWidth, ShellNativeHeight);

            // 顶排 4 页签严格对齐帮会原图 guild_tab_normal 273×62 / guild_tab_selected 273×65 原生比例
            string[] tabs = { "属性", "加点", "技能", "条目点" };
            for (int i = 0; i < tabs.Length; ++i)
            {
                var page = (PetCultivationPage)i;
                bool active = _page == page;
                var tabBtn = ActionButton("Tab" + tabs[i], _shell, 188f + i * 283f, active ? 85f : 87f, 273f, active ? 65f : 62f,
                    tabs[i], active,
                    () => { if (BattleLocked) SelectPage(page); else Navigate(() => SelectPage(page)); },
                    active ? "mail_tab_selected" : "mail_tab_normal", 31f);
                if (active)
                {
                    var marker = GuildUiArt.Line(tabBtn.transform, "TabSelectedMarker", 112f, 56f, 49f, 2.5f);
                    marker.color = TabGoldRule;
                }
            }

            // 左栏：我的宝宝（严格落在帮会原图左栏 183..727 内）
            Label("PetRosterHeadingText", _shell, LeftColX + 10f, 172f, 160f, 40f, "我的宝宝", 32f, Green, role: QdaoUiTypographyRole.Heading);
            GuildUiArt.Line(_shell, "PetRosterHeadingRule", LeftColX + 162f, 192f, LeftColWidth - 168f);
            BuildRoster(LeftColX, LeftColWidth);

            if (_openMore != null)
            {
                const float btnGap = 10f;
                const float sideW = 152f;
                float ascendW = LeftColWidth - sideW * 2f - btnGap * 2f; // 220px，给主绿钮留足金云纹呼吸空间
                _more = ActionButton("PetMore", _shell, LeftColX, FooterSecondaryY, sideW, FooterSecondaryHeight, "更多培养", false,
                    () => { if (!BattleLocked) Navigate(() => { Hide(); _openMore(); }); }, null, 23f);
                ActionButton("PetBadgeExplain", _shell, LeftColX + sideW + btnGap, FooterSecondaryY, sideW, FooterSecondaryHeight, "五阶徽章", false,
                    () => { _badgesVisible = true; BuildBadges(); }, null, 23f);
                bool canAscend = HasNextTier(Selected);
                _ascendEntry = ActionButton("PetAscendTier", _shell, LeftColX + (sideW + btnGap) * 2f,
                    canAscend ? FooterPrimaryY : FooterSecondaryY, ascendW, canAscend ? FooterPrimaryHeight : FooterSecondaryHeight,
                    Selected?.CultivationTier >= 5 ? "已达巅峰" : "灵阶突破", canAscend, OpenAscendDialog, null, 25f);
            }
            else
            {
                const float btnGap = 14f;
                const float badgeW = 246f;
                float ascendW = LeftColWidth - badgeW - btnGap; // 284px
                ActionButton("PetBadgeExplain", _shell, LeftColX, FooterSecondaryY, badgeW, FooterSecondaryHeight, "徽章说明", false,
                    () => { _badgesVisible = true; BuildBadges(); }, null, 25f);
                bool canAscend = HasNextTier(Selected);
                _ascendEntry = ActionButton("PetAscendTier", _shell, LeftColX + badgeW + btnGap,
                    canAscend ? FooterPrimaryY : FooterSecondaryY, ascendW, canAscend ? FooterPrimaryHeight : FooterSecondaryHeight,
                    Selected?.CultivationTier >= 5 ? "已达巅峰" : "灵阶突破", canAscend, OpenAscendDialog, null, 26f);
            }

            _status = Label("PetCultivationStatus", _shell, RightColX + 4f, 673f, RightColWidth - 8f, 24f, "", 18f, Muted);

            switch (_page)
            {
                case PetCultivationPage.EntryPoints: BuildEntryPoints(RightColX, RightColWidth); break;
                case PetCultivationPage.FourStats: BuildFourStats(RightColX, RightColWidth); break;
                case PetCultivationPage.Skills: BuildSkills(RightColX, RightColWidth); break;
                default: BuildAttributes(RightColX, RightColWidth); break;
            }
            RefreshFields();
            if (_badgesVisible) BuildBadges();
            if (_ascendVisible) BuildAscendDialog();
        }

        private void BuildRoster(float x, float width)
        {
            const float rowHeight = 114f;
            const float rowStride = 118f;
            const float viewportHeight = 472f;
            Scroll("PetRoster", _shell, x, 218f, width, viewportHeight, out var content);
            int count = _list?.Pets.Count ?? 0;
            content.sizeDelta = new Vector2(width, Mathf.Max(viewportHeight, count == 0 ? viewportHeight : (count - 1) * rowStride + rowHeight));
            if (count == 0)
                Label("PetRosterEmpty", content, 20f, 150f, width - 40f, 140f,
                    _client?.ListRequestPending == true ? "正在同步宝宝信息…" : "暂无宝宝\n获得宝宝后可在此培养",
                    24f, Muted, TextAlignmentOptions.Center, wrap: true);
            for (int i = 0; i < count; ++i)
            {
                var pet = _list.Pets[i];
                bool selected = pet.PetId == _selectedPetId;
                var button = ActionButton("PetItem" + i, content, 0f, i * rowStride, width, rowHeight, "", selected,
                    () => SelectPet(pet.PetId), selected ? "pet_card_selected" : "mail_row_normal");
                var parent = (RectTransform)button.transform;
                if (selected) Art("PetSelectedMark" + i, parent, 12f, 45f, 24f, 24f, "guild_self_mark").preserveAspect = true;
                var portrait = Art("PetPortrait" + i, parent, 46f, 16f, 82f, 82f, "round_badge_taiji");
                portrait.sprite = Portrait(pet);
                portrait.preserveAspect = true;
                Art("PetPortraitRing" + i, parent, 40f, 10f, 94f, 94f,
                    string.IsNullOrEmpty(pet.CultivationArtKey) ? "portrait_frame" : "ring-" + pet.CultivationArtKey).preserveAspect = true;
                Label("PetName" + i, parent, 148f, 14f, width - 254f, 44f, pet.Name, 31f,
                    selected ? Green : RichInk, role: QdaoUiTypographyRole.Heading);
                Label("PetLevel" + i, parent, 148f, 62f, width - 260f, 34f,
                    $"{pet.Level}级 · {(string.IsNullOrEmpty(pet.CultivationTierName) ? "—" : pet.CultivationTierName)}", 21f,
                    selected ? SelectedInk : Ink);
                // 保持 18px 精确右内距，防止边框裁字并满足布局断言
                Label("PetState" + i, parent, width - 102f, 62f, 84f, 34f, pet.IsActive ? "出战" : "休息", 21f,
                    pet.IsActive ? Green : Muted, TextAlignmentOptions.MidlineRight,
                    role: pet.IsActive ? QdaoUiTypographyRole.Heading : QdaoUiTypographyRole.Body);
                GuildUiArt.Line(parent, "PetRosterRule" + i, 4f, rowHeight - 2f, width - 8f);
            }
        }

        private void BuildFourStats(float x, float width)
        {
            Label("FourStatsTitle", _body, x + 16f, 172f, 160f, 40f, "四维加点", 32f, Green, role: QdaoUiTypographyRole.Heading);
            _remaining = Label("FourStatsRemaining", _body, x + 186f, 174f, 280f, 38f, "剩余属性点 —", 25f, Green, role: QdaoUiTypographyRole.Heading);
            _budget = Label("FourStatsBudget", _body, x + width - 468f, 178f, 464f, 32f, "", 20f, Muted, TextAlignmentOptions.MidlineRight);

            float rowX = x, rowW = width, current = rowW * .36f, increment = rowW * .61f, after = rowW * .86f;
            Art("FourStatsHeaderBg", _body, rowX, 220f, rowW, 38f, "guild_table_header");
            Label("FourStatsNameHeader", _body, rowX + 42f, 220f, 180f, 38f, "属性", 21f, Muted);
            Label("FourStatsCurrentHeader", _body, rowX + current - 80f, 220f, 160f, 38f, "当前投入", 21f, Muted, TextAlignmentOptions.Center);
            Label("FourStatsIncrementHeader", _body, rowX + increment - 80f, 220f, 160f, 38f, "本次新增", 21f, Muted, TextAlignmentOptions.Center);
            Label("FourStatsAfterHeader", _body, rowX + after - 80f, 220f, 160f, 38f, "调整后", 21f, Muted, TextAlignmentOptions.Center);

            const float rowHeight = 84f;
            const float rowStride = 91f;
            const float viewportHeight = 374f;
            Scroll("FourStats", _body, rowX, 262f, rowW, viewportHeight, out var content);
            int count = Selected?.Dimensions.Count ?? 0;
            content.sizeDelta = new Vector2(rowW, Mathf.Max(viewportHeight, count == 0 ? viewportHeight : (count - 1) * rowStride + rowHeight));
            for (int i = 0; i < count; ++i)
            {
                var d = Selected.Dimensions[i];
                var row = new StatRow { Dimension = d };
                _statRows.Add(row);
                var plate = Art("FourStatRow" + i, content, 0f, i * rowStride, rowW, rowHeight, "mail_row_normal").rectTransform;
                Label("FourStatName" + i, plate, 42f, 20f, current - 120f, 44f, d.Name, 28f, RichInk, role: QdaoUiTypographyRole.Heading);
                row.Current = Label("FourStatCurrent" + i, plate, current - 60f, 21f, 120f, 42f, "", 25f, Ink, TextAlignmentOptions.Center);
                row.Minus = ActionButton("Row" + i + "Minus", plate, increment - 104f, 18f, 48f, 48f, "", false,
                    () => Step(d.DimensionId, -1), "step-minus-normal");
                Art("FourStatIncrementField" + i, plate, increment - 44f, 21f, 88f, 42f, "stat_field");
                row.Increment = Label("FourStatIncrement" + i, plate, increment - 44f, 21f, 88f, 42f, "", 24f, Blue, TextAlignmentOptions.Center, role: QdaoUiTypographyRole.Heading);
                row.Plus = ActionButton("Row" + i + "Plus", plate, increment + 56f, 18f, 48f, 48f, "", false,
                    () => Step(d.DimensionId, 1), "step-plus-normal");
                Art("FourStatAfterField" + i, plate, after - 46f, 21f, 92f, 42f, "stat_field");
                row.After = Label("FourStatAfter" + i, plate, after - 46f, 21f, 92f, 42f, "", 24f, RichInk, TextAlignmentOptions.Center, role: QdaoUiTypographyRole.Heading);
                GuildUiArt.Line(plate, "FourStatRule" + i, 4f, rowHeight - 2f, rowW - 8f);
            }
            if (count == 0)
                Label("FourStatsEmpty", content, 20f, 130f, rowW - 40f, 80f,
                    Selected == null ? "请先选择宝宝" : "暂无可分配维度", 26f, Muted, TextAlignmentOptions.Center);

            Label("FourStatsHelp", _body, x + 4f, 642f, width - 8f, 28f, "减号仅撤回本次尚未保存的新增属性点。", 19f, Muted);
            _save = ActionButton("Confirm", _body, x + width - 484f, FooterPrimaryY, 252f, FooterPrimaryHeight, "保存加点", true, Save, null, 27f);
            _discard = ActionButton("Discard", _body, x + width - 218f, FooterSecondaryY, 218f, FooterSecondaryHeight, "恢复当前分配", false,
                () => { if (!CanWrite) return; _draft.Clear(); _message = "已恢复当前分配，未发送保存请求。"; RefreshFields(); }, null, 24f);
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
            Label("EntryPointsTitle", _body, x + 16f, 172f, 130f, 40f, "条目点", 32f, Green, role: QdaoUiTypographyRole.Heading);
            GuildUiArt.Line(_body, "EntryHeadingRule", x + 144f, 192f, 112f);
            Label("EntryPointsBudget", _body, x + 260f, 176f, width - 264f, 34f, "总点数 —       已用 —       剩余 —",
                22f, RichInk, TextAlignmentOptions.MidlineRight, role: QdaoUiTypographyRole.Heading);

            string[] categories = { "全部", "输出", "障碍", "辅助" };
            float catW = (width - 36f) * 0.25f;
            for (int i = 0; i < categories.Length; i++)
            {
                int index = i;
                ActionButton("EntryFilter" + i, _body, x + i * (catW + 12f), 220f, catW, 42f, categories[i], i == _entryCategory,
                    () => { _entryCategory = index; Render(); }, i == _entryCategory ? "mail_tab_selected" : "button_secondary", 23f);
            }

            float rowW = width, current = rowW * .43f, adjust = rowW * .68f, detail = rowW * .90f;
            Art("EntryTableHeader", _body, x, 270f, rowW, 38f, "guild_table_header");
            Label("EntryDirectionHeader", _body, x + 64f, 270f, 240f, 38f, "分配方向", 21f, Muted);
            Label("EntryCurrentHeader", _body, x + current - 80f, 270f, 160f, 38f, "当前投入", 21f, Muted, TextAlignmentOptions.Center);
            Label("EntryDraftHeader", _body, x + adjust - 80f, 270f, 160f, 38f, "调整后", 21f, Muted, TextAlignmentOptions.Center);
            Label("EntryBenefitHeader", _body, x + detail - 70f, 270f, 140f, 38f, "操作", 21f, Muted, TextAlignmentOptions.Center);

            string[] names = { "物理伤害", "法术伤害", "封物命中", "混乱命中", "治疗效果", "护盾容量" };
            string[] icons = { "physical", "magic", "seal", "confusion", "healing", "shield" };
            int visible = 0;
            for (int i = 0; i < names.Length; ++i)
            {
                if (_entryCategory != 0 && i / 2 + 1 != _entryCategory) continue;
                var row = Art("EntryRow" + i, _body, x, 312f + visible++ * 56f, rowW, 54f, "mail_row_normal").rectTransform;
                Art("EntryIcon" + i, row, 14f, 8f, 38f, 38f, "row_" + icons[i]).preserveAspect = true;
                Label("EntryName" + i, row, 64f, 8f, current - 124f, 38f, names[i], 23f, RichInk);
                Label("EntryCurrent" + i, row, current - 44f, 8f, 88f, 38f, "—", 22f, Ink, TextAlignmentOptions.Center);
                ActionButton("EntryMinus" + i, row, adjust - 96f, 7f, 40f, 40f, "", false, null, "step-minus-disabled").interactable = false;
                Art("EntryField" + i, row, adjust - 48f, 9f, 74f, 36f, "stat_field");
                Label("EntryValue" + i, row, adjust - 48f, 9f, 74f, 36f, "—", 21f, Ink, TextAlignmentOptions.Center);
                ActionButton("EntryPlus" + i, row, adjust + 34f, 7f, 40f, 40f, "", false, null, "step-plus-disabled").interactable = false;
                ActionButton("EntryDetail" + i, row, rowW - 96f, 9f, 84f, 36f, "详情", false,
                    () => { _message = "条目点尚未同步；请刷新宝宝列表获取服务器总点数、收益和上限。"; RefreshFields(); }, null, 20f);
                GuildUiArt.Line(row, "EntryRowRule" + i, 4f, 52f, rowW - 8f);
            }

            Label("EntryHelp", _body, x + 4f, 714f, width - 500f, 34f, "各方向共用条目点；未投入也可使用已学技能。", 19f, Muted);
            _save = ActionButton("EntrySave", _body, x + width - 484f, FooterPrimaryY, 252f, FooterPrimaryHeight, "保存分配", true, null, null, 27f);
            _save.interactable = false;
            _discard = ActionButton("EntryRestore", _body, x + width - 218f, FooterSecondaryY, 218f, FooterSecondaryHeight, "恢复当前分配", false, null, null, 24f);
            _discard.interactable = false;
        }

        private void BuildAttributes(float x, float width)
        {
            var pet = Selected;
            Label("PetOverviewTitle", _body, x + 16f, 172f, 260f, 40f, pet?.Name ?? "宝宝属性", 32f, Green, role: QdaoUiTypographyRole.Heading);
            GuildUiArt.Line(_body, "PetOverviewHeadingRule", x + 206f, 192f, width - 212f);
            var d = pet?.Derived;
            string[] names = { "气血", "法力", "物理攻击", "法术攻击", "防御", "速度" };
            string[] values = d == null ? Enumerable.Repeat("—", 6).ToArray() : new[] {
                $"{d.Health}/{d.MaxHealth}", $"{d.Mana}/{d.MaxMana}", d.PhysicalAttack.ToString(), d.MagicAttack.ToString(), d.Defense.ToString(), d.Speed.ToString() };
            for (int i = 0; i < names.Length; i++)
            {
                var row = Art("PetOverview" + i, _body, x, 224f + i * 62f, width, 58f, "mail_row_normal").rectTransform;
                Label("PetOverviewLabel" + i, row, 32f, 10f, 240f, 38f, names[i], 25f, RichInk, role: QdaoUiTypographyRole.Heading);
                Label("PetOverviewValue" + i, row, width - 380f, 10f, 348f, 38f, values[i], 25f, Ink, TextAlignmentOptions.MidlineRight);
                GuildUiArt.Line(row, "PetOverviewRule" + i, 4f, 56f, width - 8f);
            }
            bool isMaxTier = pet?.CultivationTier >= 5;
            bool primaryAscend = pet?.NextTier?.CanAscend == true;
            float buttonWidth = isMaxTier ? 410f : 268f;
            Label("PetOverviewGrowth", _body, x + 8f, 608f, width - 16f, 32f,
                pet == null ? "等级 — · 成长率 —" : $"等级 {pet.Level} · 成长率 {pet.Growth / 100f:0.##}% · 灵阶：{(string.IsNullOrEmpty(pet.CultivationTierName) ? "—" : pet.CultivationTierName)}",
                22f, RichInk, role: QdaoUiTypographyRole.Heading);
            if (pet != null)
            {
                bool hasNextTier = HasNextTier(pet);
                string tierHint = hasNextTier
                    ? $"下阶：{pet.NextTier.NextTierName}（需 {pet.NextTier.MinPetLevel} 级）"
                    : isMaxTier ? "已达最高灵阶，享受全部条目点加成。" : "灵阶突破条件尚未同步。";
                Label("PetOverviewTierHint", _body, x + 8f, 642f, width - 16f, 28f, tierHint, 20f, Green);
                var btn = ActionButton("PetOverviewAscend", _body, x + width - buttonWidth,
                    primaryAscend ? FooterPrimaryY : FooterSecondaryY, buttonWidth, primaryAscend ? FooterPrimaryHeight : FooterSecondaryHeight,
                    isMaxTier ? MaxTierLabel : "灵阶突破", primaryAscend, OpenAscendDialog, null, 26f);
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
            image.pixelsPerUnitMultiplier = 1f;
            if (key == "close-jade" || key == "guild_close") image.preserveAspect = true;
            return image;
        }

        private static TMP_Text Label(string name, UnityEngine.Transform parent, float x, float y, float w, float h, string value,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            bool wrap = false, bool autoSize = false, float minSize = 15f,
            QdaoUiTypographyRole role = QdaoUiTypographyRole.Body)
        {
            var text = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, value ?? "", size, color, alignment);
            QdaoUguiTheme.ApplyTypography(text, role);
            text.richText = false;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            if (autoSize)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = Mathf.Min(minSize, size);
                text.fontSizeMax = size;
            }
            text.raycastTarget = false;
            return text;
        }

        private static Button ActionButton(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string text, bool primary, Action clicked, string key = null, float fontSize = 24f, bool autoSize = true)
        {
            var art = Art(name + "Plate", parent, x, y, w, h, key ?? (primary ? "button_primary" : "button_secondary"), true);
            var button = art.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .98f, .86f);
            colors.pressedColor = new Color(.86f, .93f, .88f);
            // 柔和玉雾禁用色，避免把深玉绿主钮和金框次钮压成脏灰黑块
            colors.disabledColor = primary ? new Color(0.82f, 0.88f, 0.84f, 0.88f) : new Color(0.90f, 0.89f, 0.85f, 0.78f);
            button.colors = colors;
            if (key == "step-plus-normal" || key == "step-minus-normal")
            {
                string stem = key.Substring(0, key.Length - "normal".Length);
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = CharacterPetArt.Get(stem + "focus"),
                    selectedSprite = CharacterPetArt.Get(stem + "focus"),
                    pressedSprite = CharacterPetArt.Get(stem + "pressed"),
                    disabledSprite = CharacterPetArt.Get(stem + "disabled"),
                };
            }
            if (clicked != null) button.onClick.AddListener(() => clicked());
            if (!string.IsNullOrEmpty(text))
            {
                bool ornatePrimary = primary && (key == null || key == "button_primary" || key == "guild_button_primary");
                float padX = ornatePrimary ? (w >= 210f ? 36f : 28f) : w >= 160f ? 18f : w >= 100f ? 12f : 6f;
                Label(name + "Text", art.rectTransform, padX, 2f, Mathf.Max(20f, w - padX * 2f), Mathf.Max(20f, h - 4f),
                    text, fontSize, primary ? Cream : RichInk, TextAlignmentOptions.Center,
                    wrap: false, autoSize: autoSize, minSize: Mathf.Max(14f, fontSize - 7f),
                    role: primary ? QdaoUiTypographyRole.Primary : QdaoUiTypographyRole.Heading);
            }
            return button;
        }

        private static ScrollRect Scroll(string name, UnityEngine.Transform parent, float x, float y, float w, float h, out RectTransform content)
        {
            var viewport = Rect(name + "Viewport", parent, x, y, w, h);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect(name + "Content", viewport, 0f, 0f, w, h);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            return scroll;
        }

        private static void DestroyUi(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
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
