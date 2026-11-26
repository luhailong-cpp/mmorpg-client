using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Skills;
using MmorpgClient.UI.Ugui.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Skills
{
    public enum SkillCraftPage { Scrolls, ProxyStalls }

    /// <summary>
    /// 典籍制书（分卷宗常规/稀世双轨拓印 + 我的未学书架）与主城西市名匠代打摊位窗口。
    /// 独立于已锁死原图像素几何的 SkillPanel，切图可从 UI/Ugui/CraftQualitySkill/ 无缝替换，缺图时回退既有道家 Q 版控件。
    /// </summary>
    public sealed class SkillCraftWindow : IDisposable
    {
        private readonly RectTransform _root, _frame, _body, _modalLayer;
        private readonly CanvasGroup _bodyGroup;
        private SkillCraftClient _client;
        private SkillCraftPage _page;
        private uint _selectedScrollId;
        private uint _selectedSkillId;
        private ulong _selectedProxyMasterId;
        private ulong _dismantleConfirmBookId;
        private ulong _learnPetId;
        private ulong _draftForgeFee = 80;
        private ulong _draftScrollFee = 80;
        private bool _draftFeesInitialized;
        private bool _rendering, _renderAgain, _disposed;

        public event Action Closed;
        public event Action<ulong> RequestProxyEquipmentPolish;
        public bool IsVisible => !_disposed && _root != null && _root.gameObject.activeSelf;

        public SkillCraftWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("SkillCraftWindow", parent, Vector4.zero);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .08f, .065f, .76f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>();
            _frame = QdaoUguiFactory.CreateCenteredRect("SkillCraftComposition", _root, Width, Height);
            _frame.gameObject.AddComponent<EquipmentUiFit>();
            var surface = QdaoUguiFactory.CreateStretch("SkillCraftSurface", _frame, Vector4.zero);
            _bodyGroup = surface.gameObject.AddComponent<CanvasGroup>();
            Panel(surface, "SkillCraftPaper", 0, 0, Width, Height, true);
            _body = QdaoUguiFactory.CreateStretch("SkillCraftContent", surface, Vector4.zero);
            _modalLayer = QdaoUguiFactory.CreateStretch("SkillCraftModal", _frame, Vector4.zero);
            _root.gameObject.SetActive(false);
        }

        public void Bind(SkillCraftClient client)
        {
            if (_disposed || ReferenceEquals(_client, client)) return;
            if (_client != null) _client.Changed -= Render;
            _client = client;
            _selectedScrollId = 0;
            _selectedSkillId = 0;
            _selectedProxyMasterId = 0;
            _dismantleConfirmBookId = 0;
            _draftFeesInitialized = false;
            if (_client != null) _client.Changed += Render;
            if (IsVisible) Render();
        }

        public void Show(SkillCraftPage page = SkillCraftPage.Scrolls, uint focusScrollId = 0)
        {
            if (_disposed) return;
            _page = page;
            if (focusScrollId != 0) _selectedScrollId = focusScrollId;
            _dismantleConfirmBookId = 0;
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            _client?.RefreshState();
            _client?.RefreshProxyStalls();
            Render();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            _dismantleConfirmBookId = 0;
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Render()
        {
            if (!IsVisible) return;
            if (_rendering) { _renderAgain = true; return; }
            _rendering = true;
            try
            {
                do
                {
                    _renderAgain = false;
                    Clear(_body);
                    Clear(_modalLayer);

                    Button(_body, "SkillCraftTab_Scrolls", "典籍制书", 180, 140, 380,
                        () => { _page = SkillCraftPage.Scrolls; _dismantleConfirmBookId = 0; Render(); },
                        _page == SkillCraftPage.Scrolls);
                    Button(_body, "SkillCraftTab_Proxy", "名匠代打", 590, 140, 380,
                        () => { _page = SkillCraftPage.ProxyStalls; _dismantleConfirmBookId = 0; _client?.RefreshProxyStalls(); Render(); },
                        _page == SkillCraftPage.ProxyStalls);
                    Label(_body, "SkillCraftHeading", _page == SkillCraftPage.Scrolls ? "典籍制书" : "名匠代打",
                        1560, 65, 960, 130, 68, color: Jade);
                    Button(_body, "SkillCraftRefresh", "刷新", 3040, 135, 355, () =>
                    {
                        _client?.RefreshState();
                        _client?.RefreshProxyStalls();
                    }, enabled: _client != null && !_client.IsLoading && !_client.IsBusy);
                    Button(_body, "SkillCraftClose", "关闭", 3420, 135, 355, Hide);

                    if (_page == SkillCraftPage.Scrolls) RenderScrollsPage();
                    else RenderProxyStallsPage();

                    string status = _client == null ? SkillCraftClient.NotInGameMessage :
                        !string.IsNullOrEmpty(_client.Error) ? _client.Error : _client.Message;
                    Label(_body, "SkillCraftStatus", status, 180, 1480, 2900, 75, 36,
                        color: !string.IsNullOrEmpty(_client?.Error) ? Danger : Jade);
                    if (_client?.HasPendingWrite == true)
                    {
                        Button(_body, "SkillCraftRetryPending", "重试原请求", 3120, 1365, 650,
                            () => _client.RetryPendingWrite(), true, !_client.IsBusy);
                    }

                    bool hasModal = _dismantleConfirmBookId != 0;
                    _bodyGroup.interactable = !hasModal;
                    _bodyGroup.blocksRaycasts = !hasModal;
                    _modalLayer.gameObject.SetActive(hasModal);
                    if (hasModal) RenderDismantleConfirmModal();
                } while (_renderAgain);
            }
            finally { _rendering = false; }
        }

        private void RenderScrollsPage()
        {
            var state = _client?.State;
            var scrollMasterySprite = SeasonEconomyUiArt.LoadCraftControl(
                SeasonEconomyUiArt.MasteryBadgeKey(state?.ScrollMastery?.Tier ?? 1u));
            float scrollMasteryX = 180f;
            if (scrollMasterySprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage("ScrollMasteryIcon", _body, 172, 276, 92, 92, scrollMasterySprite, false);
                icon.preserveAspect = true;
                scrollMasteryX = 278f;
            }
            Label(_body, "ScrollMasteryBar", SkillCraftModels.FormatMastery(state?.ScrollMastery, true),
                scrollMasteryX, 290, 1830f - scrollMasteryX, 70, 38, color: Jade);
            if (state == null)
            {
                Label(_body, "ScrollMaterialBar", "材料余额未同步", 1850, 290, 1950, 70, 38, color: Ink);
            }
            else
            {
                DrawWalletItem("WalletPage", 1850, "item_enlightenment_page", $"悟道残页 {state.FragmentOwned}", 440);
                DrawWalletItem("WalletRareScroll", 2310, "item_rare_scroll_fragment", $"绝学残卷 {state.RareScrollOwned}", 440);
                DrawWalletItem("WalletInk", 2770, "item_celestial_ink", $"天工秘墨 {state.RareInkOwned}", 440);
                Label(_body, "ScrollMaterialBar", $"银两 {state.GoldBalance}", 3230, 290, 560, 70, 38, color: Ink);
            }

            // 左栏：分卷宗列表（老卷宗永不稀释，新主题卷宗自动出现）
            Panel(_body, "ScrollListPaper", 130, 390, 720, 1020);
            Label(_body, "ScrollListTitle", "典籍卷宗", 200, 440, 560, 70, 48, color: Jade);
            var scrolls = state?.Scrolls.ToList() ?? new List<SkillScrollCatalogInfo>();
            if (_selectedScrollId == 0 || !scrolls.Any(s => s.ScrollId == _selectedScrollId))
                _selectedScrollId = scrolls.FirstOrDefault()?.ScrollId ?? 0;
            var scrollList = Scroll(_body, "ScrollListViewport", 185, 530, 610, 820, Math.Max(820, scrolls.Count * 180));
            var tabNormalSprite = SeasonEconomyUiArt.LoadCraftControl("scroll_tab_normal");
            var tabSelectedSprite = SeasonEconomyUiArt.LoadCraftControl("scroll_tab_selected");
            for (int i = 0; i < scrolls.Count; ++i)
            {
                var scroll = scrolls[i];
                bool selected = scroll.ScrollId == _selectedScrollId;
                string tag = scroll.RarityTier == 1 ? "稀世卷" : "常规卷";
                // scroll_tab_normal/selected 为 420x140 固定比例切片，1:1 放置不拉伸
                bool hasTabArt = (selected ? tabSelectedSprite : tabNormalSprite) != null;
                var tabBtn = Button(scrollList, "ScrollTab_" + scroll.ScrollId, $"{scroll.Name}\n{tag} · {scroll.SkillTableIds.Count} 技",
                    hasTabArt ? 80 : 10, i * 175, hasTabArt ? 420 : 560, () =>
                    {
                        _selectedScrollId = scroll.ScrollId;
                        _selectedSkillId = 0;
                        Render();
                    }, selected);
                if (hasTabArt)
                {
                    var img = tabBtn.GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = selected ? tabSelectedSprite : tabNormalSprite;
                        img.type = Image.Type.Simple;
                        img.preserveAspect = true;
                        img.color = Color.white;
                    }
                }
            }

            // 中栏：拓印案台
            Panel(_body, "ScrollDeskPaper", 890, 390, 1620, 1020);
            var activeScroll = scrolls.FirstOrDefault(s => s.ScrollId == _selectedScrollId);
            Label(_body, "ScrollDeskTitle", activeScroll == null ? "拓印案台" : $"{activeScroll.Name}（{(activeScroll.RarityTier == 1 ? "稀世绝学" : "常规卷宗")}）",
                960, 440, 1240, 70, 46, color: Jade);
            if (activeScroll != null && activeScroll.RarityTier == 1)
            {
                var rareBadge = SeasonEconomyUiArt.LoadCraftControl("badge_rare_skill");
                if (rareBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("ScrollRareBadge", _body, 2220, 439, 240, 72, rareBadge, false);
                    badgeImg.preserveAspect = true;
                    var rareLbl = Label(_body, "ScrollRareBadgeText", "稀世绝学", 2245, 445, 190, 60, 32, false, SeasonEconomyUiArt.GoldText);
                    rareLbl.alignment = TMPro.TextAlignmentOptions.Center;
                }
            }

            if (activeScroll != null)
            {
                var unlocked = new HashSet<uint>(state.BookUnlockedSkillIds);
                var ids = activeScroll.SkillTableIds;
                if (_selectedSkillId == 0 || !ids.Contains(_selectedSkillId))
                    _selectedSkillId = ids.FirstOrDefault();
                var skillList = Scroll(_body, "ScrollSkillsViewport", 950, 525, 1500, 430, Math.Max(430, ((ids.Count + 2) / 3) * 155));
                for (int i = 0; i < ids.Count; ++i)
                {
                    uint skillId = ids[i];
                    string name = ResolveSkillName(skillId);
                    string mark = unlocked.Contains(skillId) ? " [已解锁]" : string.Empty;
                    bool sel = skillId == _selectedSkillId;
                    var skillBtn = Button(skillList, "ScrollSkill_" + skillId, name + mark,
                        (i % 3) * 490 + 5, (i / 3) * 155, 470, () =>
                        {
                            _selectedSkillId = skillId;
                            Render();
                        }, sel);
                    var skillIcon = ResolveSkillIcon(skillId);
                    if (skillIcon != null)
                    {
                        var iconImg = QdaoUguiFactory.CreateImage("ScrollSkillIcon_" + skillId, skillBtn.transform,
                            24, 34, 72, 72, skillIcon, false);
                        iconImg.preserveAspect = true;
                        var btnLabel = skillBtn.transform.Find("Label") as RectTransform;
                        if (btnLabel != null)
                        {
                            btnLabel.anchoredPosition = new Vector2(104f, 0f);
                            btnLabel.sizeDelta = new Vector2(336f, 140f);
                        }
                    }
                }

                ulong proxyFee = SelectedScrollProxyFee();
                if (_selectedProxyMasterId != 0)
                {
                    Button(_body, "ScrollProxyClear", $"当前名匠代制（手工费 {proxyFee} 银两）· 点此改回亲自制书",
                        950, 975, 1480, () => { _selectedProxyMasterId = 0; Render(); });
                }
                else
                {
                    Button(_body, "ScrollProxyPick", "亲自制书 · 点此查看主城名匠代制（5% 官府托管）",
                        950, 975, 1480, () =>
                        {
                            _page = SkillCraftPage.ProxyStalls;
                            _client?.RefreshProxyStalls();
                            Render();
                        });
                }

                bool locked = _client == null || _client.IsBusy || _client.HasPendingWrite;
                if (activeScroll.RarityTier == 0)
                {
                    bool randAllowed = SkillCraftModels.CanCraftRegularRandom(state, activeScroll, proxyFee, out _);
                    bool targetAllowed = SkillCraftModels.CanCraftRegularTargeted(state, activeScroll, _selectedSkillId, proxyFee, out string targetReason);
                    bool canRand = !locked && randAllowed;
                    bool canTarget = !locked && targetAllowed;
                    Label(_body, "ScrollRegularHint",
                        $"随机拓印：{activeScroll.RandomFragmentCount} 残页 + {activeScroll.RandomGoldCost + proxyFee} 银两（产物可交易）\n" +
                        $"定向拓印（{ResolveSkillName(_selectedSkillId)}）：{activeScroll.TargetedFragmentCount} 残页 + {activeScroll.TargetedGoldCost + proxyFee} 银两",
                        960, 1125, 1480, 110, 34, true, Ink);
                    Button(_body, "CraftRandomRegularBtn", "随机拓印", 960, 1250, 680,
                        () => _client?.CraftRegularRandom(activeScroll.ScrollId, _selectedProxyMasterId), true, canRand);
                    Button(_body, "CraftTargetedRegularBtn", "定向拓印", 1740, 1250, 680,
                        () => _client?.CraftRegularTargeted(activeScroll.ScrollId, _selectedSkillId, _selectedProxyMasterId), true, canTarget);
                    if (!canTarget && !string.IsNullOrEmpty(targetReason))
                        Label(_body, "ScrollCraftReason", targetReason, 960, 1395, 1480, 50, 30, color: Danger);
                }
                else
                {
                    bool rareAllowed = SkillCraftModels.CanCraftRareTargeted(state, activeScroll, _selectedSkillId, proxyFee, out string rareReason);
                    bool canRare = !locked && rareAllowed;
                    bool inkShort = state.RareInkOwned < activeScroll.RareInkCount;
                    Label(_body, "ScrollRareHint",
                        $"定向制书（{ResolveSkillName(_selectedSkillId)}）：{activeScroll.RareScrollCount} 绝学残卷 + {activeScroll.RareInkCount} 绑定天工秘墨 + {activeScroll.RareGoldCost + proxyFee} 银两\n" +
                        "100% 必出指定稀世绝学（不抽卡不歪书）；消耗绑定秘墨合出的稀世绝学书永久仅自用。",
                        960, 1110, 1480, 130, 34, true, Ink);
                    if (inkShort)
                    {
                        Button(_body, "CraftTargetedRareBtn", "定向制书（100%必出）", 960, 1250, 820,
                            () => _client?.CraftRareTargeted(activeScroll.ScrollId, _selectedSkillId, _selectedProxyMasterId), true, canRare);
                        Button(_body, "RareMallShortcutBtn", "百宝阁补秘墨", 1810, 1250, 630, () =>
                        {
                            Hide();
                            var spiritRoot = MmorpgClient.UI.Ugui.SpiritShop.SpiritShopUiRoot.Instance;
                            if (spiritRoot?.Client?.IsSupported == true)
                                spiritRoot.OpenTab(
                                    MmorpgClient.Game.SpiritShop.SpiritShopModels.TabAdvancement,
                                    MmorpgClient.Game.SpiritShop.SpiritShopModels.GoodsCelestialInk);
                            else
                                Jubaozhai.JubaozhaiUiRoot.Instance?.OpenOfficialMall();
                        }, false, true);
                    }
                    else
                    {
                        Button(_body, "CraftTargetedRareBtn", "定向制书（100%必出）", 1260, 1250, 880,
                            () => _client?.CraftRareTargeted(activeScroll.ScrollId, _selectedSkillId, _selectedProxyMasterId), true, canRare);
                    }
                    if (!canRare && !string.IsNullOrEmpty(rareReason))
                        Label(_body, "ScrollCraftReason", rareReason, 960, 1395, 1480, 50, 30, color: Danger);
                }
            }

            // 右栏：我的未学书架
            Panel(_body, "BookshelfPaper", 2550, 390, 1280, 1020);
            Label(_body, "BookshelfTitle", "我的未学书架", 2620, 440, 660, 70, 48, color: Jade);
            string learnTargetText = _learnPetId == 0 ? "研习对象：人物（点切换灵宠）" : $"研习对象：灵宠 #{_learnPetId}";
            Button(_body, "BookshelfTargetCycle", learnTargetText, 2610, 520, 1150, CycleLearnTarget);

            var books = state?.SkillBooks.ToList() ?? new List<CraftedSkillBookEntry>();
            var bookList = Scroll(_body, "BookshelfViewport", 2610, 680, 1160, 690, Math.Max(690, books.Count * 225));
            bool bookLocked = _client == null || _client.IsBusy || _client.HasPendingWrite;
            for (int i = 0; i < books.Count; ++i)
            {
                var book = books[i];
                float y = i * 225;
                Label(bookList, "BookDesc_" + book.BookId, SkillCraftModels.DescribeBook(book, state, ResolveSkillName),
                    10, y, 1120, 70, 34, true, Ink);
                Button(bookList, "BookLearn_" + book.BookId, "研习", 10, y + 75, 480,
                    () => _client?.LearnBook(book.BookId, _learnPetId), true, !bookLocked);
                if (SkillCraftModels.CanDismantleBook(book))
                {
                    uint refund = SkillCraftModels.DismantleRefundOf(state, book);
                    Button(bookList, "BookDismantle_" + book.BookId, $"拆页（返{refund}残页）", 520, y + 75, 580,
                        () => { _dismantleConfirmBookId = book.BookId; Render(); }, false, !bookLocked);
                }
            }
            if (books.Count == 0)
                Label(_body, "BookshelfEmpty", "书架暂无未学技能书，可在左侧拓印制书。", 2630, 790, 1100, 160, 40, true, Muted);
        }

        private void RenderProxyStallsPage()
        {
            var state = _client?.State;
            var escrowBanner = SeasonEconomyUiArt.LoadCraftControl("banner_safe_escrow");
            if (escrowBanner != null)
            {
                SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "ProxySafetyBannerBg", 150, 284, 3660, 84,
                    escrowBanner, new Color(0.93f, 0.91f, 0.82f, 0.96f));
                Label(_body, "ProxySafetyBanner",
                    "官府托管 · 零风险代工：装备与材料全程留在本人背包，宗师不接触原件，成品直入本人背包（成交自动扣 5% 摆摊银两税）。",
                    260, 294, 3440, 64, 36, false, Jade);
            }
            else
            {
                Label(_body, "ProxySafetyBanner",
                    "官府托管 · 零风险代工：装备与材料全程留在本人背包，宗师不接触原件，成品直入本人背包（成交自动扣 5% 摆摊银两税）。",
                    180, 290, 3600, 80, 38, true, Jade);
            }

            Panel(_body, "MyProxyStallPaper", 130, 390, 3700, 310);
            bool isMaster = SkillCraftModels.CanOpenProxyStall(state);
            if (state != null && !_draftFeesInitialized)
            {
                _draftForgeFee = SkillCraftModels.IsValidProxyFee(state.ProxyForgeFeeGold) ? state.ProxyForgeFeeGold : 80;
                _draftScrollFee = SkillCraftModels.IsValidProxyFee(state.ProxyScrollFeeGold) ? state.ProxyScrollFeeGold : 80;
                _draftFeesInitialized = true;
            }
            string myStatus = !isMaster
                ? "我的代打摊位：铸器或制卷达到【宗师】后自动解锁开摊资格。"
                : $"我的代打摊位：{(state.ProxyStallOpen ? "营业中" : "未营业")}  |  装备代打费 {_draftForgeFee} 银两  |  技能代制费 {_draftScrollFee} 银两（范围 50–100 银两）";
            float myInfoX = 200f;
            if (isMaster)
            {
                var gmBadge = SeasonEconomyUiArt.LoadTradeV2Slice("icon_master_seal")
                    ?? SeasonEconomyUiArt.LoadCraftControl("mastery_badge_grandmaster");
                if (gmBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("MyProxyMasterBadge", _body, 190, 420, 96, 96, gmBadge, false);
                    badgeImg.preserveAspect = true;
                    myInfoX = 300f;
                }
            }
            Label(_body, "MyProxyStallInfo", myStatus, myInfoX, 430, 3750f - myInfoX, 80, 40, true, isMaster ? Jade : Muted);
            if (isMaster)
            {
                bool busy = _client.IsBusy;
                Button(_body, "ForgeFeeMinus", "装备费-10", 200, 535, 360, () => { _draftForgeFee = Math.Max(SkillCraftModels.ProxyFeeMinGold, _draftForgeFee - 10); Render(); }, enabled: !busy);
                Button(_body, "ForgeFeePlus", "装备费+10", 580, 535, 360, () => { _draftForgeFee = Math.Min(SkillCraftModels.ProxyFeeMaxGold, _draftForgeFee + 10); Render(); }, enabled: !busy);
                Button(_body, "ScrollFeeMinus", "制书费-10", 980, 535, 360, () => { _draftScrollFee = Math.Max(SkillCraftModels.ProxyFeeMinGold, _draftScrollFee - 10); Render(); }, enabled: !busy);
                Button(_body, "ScrollFeePlus", "制书费+10", 1360, 535, 360, () => { _draftScrollFee = Math.Min(SkillCraftModels.ProxyFeeMaxGold, _draftScrollFee + 10); Render(); }, enabled: !busy);
                Button(_body, "ProxyStallOpenBtn", state.ProxyStallOpen ? "保存并保持营业" : "开启代打摊位", 2420, 535, 650,
                    () => _client.SetProxyStall(true, _draftForgeFee, _draftScrollFee), true, !busy);
                if (state.ProxyStallOpen)
                    Button(_body, "ProxyStallCloseBtn", "收摊", 3100, 535, 450,
                        () => _client.SetProxyStall(false, 0, 0), false, !busy);
            }

            Panel(_body, "CityMastersPaper", 130, 730, 3700, 680);
            Label(_body, "CityMastersTitle", "全城在营业名匠（委托前核对代工费用与 5% 官方税）", 200, 770, 2200, 70, 46, color: Jade);
            var stalls = _client?.ProxyStalls ?? Array.Empty<CraftProxyStallInfo>();
            var list = Scroll(_body, "CityMastersViewport", 190, 855, 3580, 520, Math.Max(520, stalls.Count * 165));
            var gmRowBadge = SeasonEconomyUiArt.LoadTradeV2Slice("icon_master_seal")
                ?? SeasonEconomyUiArt.LoadCraftControl("mastery_badge_grandmaster");
            var sealRowBg = SeasonEconomyUiArt.LoadCraftControl("seal_master_signature");
            for (int i = 0; i < stalls.Count; ++i)
            {
                var stall = stalls[i];
                float y = i * 165;
                float lineX = 10f;
                if (gmRowBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("MasterBadge_" + stall.MasterPlayerId, list, 8, y + 26, 92, 92, gmRowBadge, false);
                    badgeImg.preserveAspect = true;
                    lineX = 112f;
                }
                if (sealRowBg != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(list, "MasterSeal_" + stall.MasterPlayerId,
                        lineX, y + 34, 2040f - lineX, 72, sealRowBg, new Color(0.95f, 0.91f, 0.80f, 0.92f));
                    Label(list, "MasterLine_" + stall.MasterPlayerId, SkillCraftModels.DescribeProxyStall(stall),
                        lineX + 68f, y + 36, 1960f - lineX, 68, 34, false, Ink);
                }
                else
                {
                    Label(list, "MasterLine_" + stall.MasterPlayerId, SkillCraftModels.DescribeProxyStall(stall),
                        lineX, y + 25, 2040f - lineX, 95, 38, true, Ink);
                }
                ulong masterId = stall.MasterPlayerId;
                Button(list, "ProxyCredit_" + masterId, "查信用", 2060, y + 5, 250, () =>
                {
                    Hide();
                    MmorpgClient.UI.Ugui.Jubaozhai.JubaozhaiUiRoot.Instance?.OpenSellerCredit(masterId, () =>
                    {
                        MmorpgClient.UI.Ugui.Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
                        SkillUiRoot.OpenProxyStalls();
                    });
                });
                if (stall.ForgeMasteryTier >= SkillCraftModels.MasterTier && stall.ProxyForgeFeeGold > 0)
                {
                    Button(list, "ProxyEquip_" + stall.MasterPlayerId, "找他代打装备", 2330, y + 5, 570, () =>
                    {
                        Hide();
                        RequestProxyEquipmentPolish?.Invoke(stall.MasterPlayerId);
                    }, true);
                }
                if (stall.ScrollMasteryTier >= SkillCraftModels.MasterTier && stall.ProxyScrollFeeGold > 0)
                {
                    Button(list, "ProxyScroll_" + stall.MasterPlayerId, "找他代制技能", 2925, y + 5, 570, () =>
                    {
                        _selectedProxyMasterId = stall.MasterPlayerId;
                        _page = SkillCraftPage.Scrolls;
                        Render();
                    }, true);
                }
            }
            if (stalls.Count == 0)
                Label(_body, "CityMastersEmpty", _client?.ProxyStallsLoading == true ? "正在读取在营业名匠…" : "当前暂无宗师开设代打摊位",
                    220, 940, 2600, 120, 44, true, Muted);
        }

        private void RenderDismantleConfirmModal()
        {
            var shade = _modalLayer.GetComponent<Image>() ?? _modalLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .07f, .055f, .85f);
            shade.raycastTarget = true;
            Panel(_modalLayer, "DismantleConfirmPaper", 730, 320, 2500, 920, true);
            var state = _client?.State;
            var book = state?.SkillBooks.FirstOrDefault(b => b.BookId == _dismantleConfirmBookId);
            uint refund = SkillCraftModels.DismantleRefundOf(state, book);
            Label(_modalLayer, "DismantleConfirmTitle", "确认拆解技能书", 900, 420, 2160, 90, 60, color: Jade);
            Label(_modalLayer, "DismantleConfirmBody",
                book == null ? "该技能书已不在书架中。" :
                $"确定拆解【{ResolveSkillName(book.SkillTableId)}】？\n将固定返还 {refund} 张通用悟道残页（新老常规卷宗通用）。",
                900, 560, 2160, 320, 44, true, Ink);
            Button(_modalLayer, "DismantleCancel", "取消", 1040, 1020, 680,
                () => { _dismantleConfirmBookId = 0; Render(); });
            Button(_modalLayer, "DismantleConfirm", "确认拆页", 2160, 1020, 680, () =>
            {
                ulong id = _dismantleConfirmBookId;
                _dismantleConfirmBookId = 0;
                if (id != 0) _client?.DismantleBook(id);
                Render();
            }, true, book != null && _client != null && !_client.IsBusy);
        }

        private void CycleLearnTarget()
        {
            var pets = PetClient.Instance?.Pets?.Pets;
            if (pets == null || pets.Count == 0) { _learnPetId = 0; Render(); return; }
            if (_learnPetId == 0) { _learnPetId = pets[0].PetId; Render(); return; }
            int idx = -1;
            for (int i = 0; i < pets.Count; ++i) if (pets[i].PetId == _learnPetId) { idx = i; break; }
            _learnPetId = idx < 0 || idx + 1 >= pets.Count ? 0 : pets[idx + 1].PetId;
            Render();
        }

        private ulong SelectedScrollProxyFee()
        {
            if (_selectedProxyMasterId == 0 || _client == null) return 0;
            var stall = _client.ProxyStalls.FirstOrDefault(s => s.MasterPlayerId == _selectedProxyMasterId);
            return stall?.ProxyScrollFeeGold ?? 0;
        }

        private void DrawWalletItem(string name, float x, string iconKey, string text, float width)
        {
            var icon = SeasonEconomyUiArt.LoadCraftIcon(iconKey);
            float textX = x;
            if (icon != null)
            {
                var img = QdaoUguiFactory.CreateImage(name + "Icon", _body, x, 294, 62, 62, icon, false);
                img.preserveAspect = true;
                textX = x + 68f;
            }
            Label(_body, name + "Text", text, textX, 290, width - (textX - x), 70, 36, color: Ink);
        }

        private static Sprite ResolveSkillIcon(uint skillId)
        {
            var growth = SeasonEconomyUiArt.LoadGrowthSkillIcon(skillId);
            if (growth != null) return growth;
            var def = SkillUiRoot.FindPresentation(skillId);
            return def != null ? QdaoUguiTheme.LoadSprite(def.IconResource) : null;
        }

        private static string ResolveSkillName(uint skillId)
        {
            var def = SkillUiRoot.FindPresentation(skillId);
            return !string.IsNullOrWhiteSpace(def?.name) ? def.name : $"技能 · {skillId}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            Hide();
            Bind(null);
            _disposed = true;
            Closed = null;
            RequestProxyEquipmentPolish = null;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }
    }
}
