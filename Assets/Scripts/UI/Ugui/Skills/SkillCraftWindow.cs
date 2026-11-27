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
    public enum SkillCraftPage { Scrolls, ProxyStalls, WantedBoard }

    /// <summary>
    /// 典籍制书（分卷宗常规/稀世双轨拓印 + 藏经阁里程碑 + 批量拆页 + 流派体检 + 我的未学书架）、
    /// 主城西市名匠代打摊位（口碑/雅号/招牌/同队情义免税）与西市封卷悬赏求书板窗口。
    /// 独立于已锁死原图像素几何的 SkillPanel，切图可从 UI/Ugui/CraftQualitySkill/ 无缝替换，缺图时回退既有道家 Q 版控件。
    /// </summary>
    public sealed class SkillCraftWindow : IDisposable
    {
        private enum CraftModal { None = 0, DismantleSingle = 1, BatchDismantle = 2, BuildAdvisor = 3, ProxyDigest = 4 }

        private readonly RectTransform _root, _frame, _body, _modalLayer;
        private readonly CanvasGroup _bodyGroup;
        private SkillCraftClient _client;
        private SkillCraftPage _page;
        private CraftModal _modal;
        private bool _autoShownDigestThisOpen;
        private uint _selectedScrollId;
        private uint _selectedSkillId;
        private ulong _selectedProxyMasterId;
        private ulong _dismantleConfirmBookId;
        private List<ulong> _batchDismantleIds = new();
        private ulong _learnPetId;
        private ulong _draftForgeFee = 80;
        private ulong _draftScrollFee = 80;
        private uint _draftShowcaseTag;
        private uint _draftCraftTitleId;
        private bool _draftTeamFreeProxy;
        private bool _draftFeesInitialized;
        private ProxyStallSortMode _stallSortMode = ProxyStallSortMode.HotRank;
        private ProxyStallFilterMode _stallFilterMode = ProxyStallFilterMode.All;
        private uint _wantedSkillId;
        private ulong _wantedRewardGold = 500;
        private bool _wantedRequireMasterSig;
        private bool _rendering, _renderAgain, _disposed;
        private PlayerFeaturesClient _bagSource;

        public event Action Closed;
        public event Action<ulong> RequestProxyEquipmentPolish;
        public event Action RequestOpenMail;
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
            _batchDismantleIds.Clear();
            _modal = CraftModal.None;
            _autoShownDigestThisOpen = false;
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            BindBagSource(PlayerFeaturesClient.Instance);
            if (_bagSource != null && _bagSource.CharacterBag == null)
                _bagSource.RequestBag(PlayerFeaturesClient.CharacterBagType);
            _client?.RefreshState();
            _client?.RefreshProxyStalls();
            if (_page == SkillCraftPage.WantedBoard) _client?.RefreshWantedOrders();
            Render();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            BindBagSource(null);
            _dismantleConfirmBookId = 0;
            _batchDismantleIds.Clear();
            _modal = CraftModal.None;
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void BindBagSource(PlayerFeaturesClient source)
        {
            if (ReferenceEquals(_bagSource, source)) return;
            if (_bagSource != null) _bagSource.OnChanged -= Render;
            _bagSource = source;
            if (_bagSource != null) _bagSource.OnChanged += Render;
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

                    Button(_body, "SkillCraftTab_Scrolls", "典籍制书", 180, 140, 340,
                        () => { _page = SkillCraftPage.Scrolls; _modal = CraftModal.None; _dismantleConfirmBookId = 0; Render(); },
                        _page == SkillCraftPage.Scrolls);
                    Button(_body, "SkillCraftTab_Proxy", "名匠代打", 545, 140, 340,
                        () => { _page = SkillCraftPage.ProxyStalls; _modal = CraftModal.None; _dismantleConfirmBookId = 0; _client?.RefreshProxyStalls(); Render(); },
                        _page == SkillCraftPage.ProxyStalls);
                    var wantedTabBtn = Button(_body, "SkillCraftTab_Wanted", "悬赏求书", 910, 140, 340,
                        () => { _page = SkillCraftPage.WantedBoard; _modal = CraftModal.None; _dismantleConfirmBookId = 0; _client?.RefreshWantedOrders(); Render(); },
                        _page == SkillCraftPage.WantedBoard);
                    var wantedIcon = SeasonEconomyUiArt.LoadCraftIcon("icon_wanted_board");
                    if (wantedIcon != null)
                    {
                        var wImg = QdaoUguiFactory.CreateImage("WantedTabIcon", wantedTabBtn.transform, 18, 38, 64, 64, wantedIcon, false);
                        wImg.preserveAspect = true;
                        if (wantedTabBtn.transform.Find("Label") is RectTransform wLbl)
                        {
                            wLbl.anchoredPosition = new Vector2(86f, 0f);
                            wLbl.sizeDelta = new Vector2(244f, 140f);
                        }
                    }
                    string heading = _page switch
                    {
                        SkillCraftPage.ProxyStalls => "名匠代打",
                        SkillCraftPage.WantedBoard => "西市悬赏求书",
                        _ => "典籍制书",
                    };
                    Label(_body, "SkillCraftHeading", heading, 1560, 65, 960, 130, 68, color: Jade);
                    Button(_body, "SkillCraftRefresh", "刷新", 3040, 135, 355, () =>
                    {
                        _client?.RefreshState();
                        _client?.RefreshProxyStalls();
                        if (_page == SkillCraftPage.WantedBoard) _client?.RefreshWantedOrders();
                    }, enabled: _client != null && !_client.IsLoading && !_client.IsBusy);
                    Button(_body, "SkillCraftClose", "关闭", 3420, 135, 355, Hide);

                    if (_page == SkillCraftPage.Scrolls) RenderScrollsPage();
                    else if (_page == SkillCraftPage.ProxyStalls) RenderProxyStallsPage();
                    else RenderWantedBoardPage();

                    string status = _client == null ? SkillCraftClient.NotInGameMessage :
                        !string.IsNullOrEmpty(_client.Error) ? _client.Error : _client.Message;
                    Label(_body, "SkillCraftStatus", status, 180, 1480, 2900, 75, 36,
                        color: !string.IsNullOrEmpty(_client?.Error) ? Danger : Jade);
                    if (_client?.HasPendingWrite == true)
                    {
                        Button(_body, "SkillCraftRetryPending", "重试原请求", 3120, 1365, 650,
                            () => _client.RetryPendingWrite(), true, !_client.IsBusy);
                    }

                    if (_dismantleConfirmBookId != 0 && _modal == CraftModal.None) _modal = CraftModal.DismantleSingle;
                    if (!_autoShownDigestThisOpen && _modal == CraftModal.None && _client?.HasUnreadProxyDigest == true)
                    {
                        _autoShownDigestThisOpen = true;
                        _modal = CraftModal.ProxyDigest;
                    }
                    bool hasModal = _modal != CraftModal.None;
                    _bodyGroup.interactable = !hasModal;
                    _bodyGroup.blocksRaycasts = !hasModal;
                    _modalLayer.gameObject.SetActive(hasModal);
                    if (_modal == CraftModal.DismantleSingle) RenderDismantleConfirmModal();
                    else if (_modal == CraftModal.BatchDismantle) RenderBatchDismantleModal();
                    else if (_modal == CraftModal.BuildAdvisor) RenderBuildAdvisorModal();
                    else if (_modal == CraftModal.ProxyDigest) RenderProxyDigestModal();
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
            Label(_body, "ScrollListTitle", "典籍卷宗", 190, 440, 290, 70, 44, color: Jade);
            var advisorBtn = Button(_body, "BuildBookAdvisorBtn", "流派体检", 485, 428, 320, () =>
            {
                _modal = CraftModal.BuildAdvisor;
                Render();
            }, false, state != null);
            var archiveIcon = SeasonEconomyUiArt.LoadCraftIcon("icon_scroll_archive");
            if (archiveIcon != null)
            {
                var aImg = QdaoUguiFactory.CreateImage("ScrollArchiveIcon", advisorBtn.transform, 16, 42, 56, 56, archiveIcon, false);
                aImg.preserveAspect = true;
                if (advisorBtn.transform.Find("Label") is RectTransform aLbl)
                {
                    aLbl.anchoredPosition = new Vector2(76f, 0f);
                    aLbl.sizeDelta = new Vector2(254f, 140f);
                }
            }
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
                var scrollIcon = SeasonEconomyUiArt.LoadSkillScrollIcon(scroll.ScrollId);
                if (scrollIcon != null)
                {
                    var iconImg = QdaoUguiFactory.CreateImage("ScrollTabIcon_" + scroll.ScrollId, tabBtn.transform,
                        24, 38, 64, 64, scrollIcon, false);
                    iconImg.preserveAspect = true;
                    var btnLabel = tabBtn.transform.Find("Label") as RectTransform;
                    if (btnLabel != null)
                    {
                        float tabWidth = hasTabArt ? 420f : 560f;
                        btnLabel.anchoredPosition = new Vector2(96f, 0f);
                        btnLabel.sizeDelta = new Vector2(tabWidth - 116f, 140f);
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
                var skillList = Scroll(_body, "ScrollSkillsViewport", 950, 525, 1500, 335, Math.Max(335, ((ids.Count + 2) / 3) * 155));
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

                string selectedSkillSummary = ResolveSkillSummary(_selectedSkillId);
                if (!string.IsNullOrEmpty(selectedSkillSummary))
                {
                    var descLbl = Label(_body, "ScrollSelectedSkillDesc", selectedSkillSummary,
                        960, 868, 1480, 96, 28, true, Ink);
                    descLbl.enableAutoSizing = true;
                    descLbl.fontSizeMin = 20f;
                    descLbl.fontSizeMax = 28f;
                }

                ulong proxyFee = SelectedScrollProxyFee();
                bool proxyFreeForMe = SelectedScrollProxyIsTeamFree();
                var milestone = SkillCraftModels.GetScrollMilestoneStatus(state, activeScroll);
                bool locked = _client == null || _client.IsBusy || _client.HasPendingWrite;

                // V4 藏经阁本卷收集进度与小成/圆满残页里程碑领取栏
                Label(_body, "ScrollArchiveProgress",
                    $"藏经阁进度：{milestone.UnlockedCount}/{milestone.TotalCount} 已解锁",
                    955, 976, 560, 58, 30, false, Jade);
                string halfLabel = milestone.HalfClaimed ? "小成已领" : $"领小成(20页·{milestone.HalfThreshold}技)";
                Button(_body, "ScrollMilestoneHalfBtn", halfLabel, 1525, 962, 460,
                    () => _client?.ClaimScrollMilestone(activeScroll.ScrollId, 1u),
                    milestone.HalfClaimable, !locked && milestone.HalfClaimable);
                string fullLabel = milestone.FullClaimed ? "圆满已领" : "领圆满(40页·全解锁)";
                Button(_body, "ScrollMilestoneFullBtn", fullLabel, 2000, 962, 460,
                    () => _client?.ClaimScrollMilestone(activeScroll.ScrollId, 2u),
                    milestone.FullClaimable, !locked && milestone.FullClaimable);

                if (_selectedProxyMasterId != 0)
                {
                    string proxyFeeLabel = proxyFreeForMe ? "同队情义 0 银两免税" : $"手工费 {proxyFee} 银两";
                    Button(_body, "ScrollProxyClear", $"当前名匠代制（{proxyFeeLabel}）· 点此改回亲自制书",
                        950, 1044, 1510, () => { _selectedProxyMasterId = 0; Render(); });
                }
                else
                {
                    Button(_body, "ScrollProxyPick", "亲自制书 · 点此查看全区名匠24小时代制（跨线/离线可接单）",
                        950, 1044, 1510, () =>
                        {
                            _page = SkillCraftPage.ProxyStalls;
                            _client?.RefreshProxyStalls();
                            Render();
                        });
                }

                if (activeScroll.RarityTier == 0)
                {
                    bool randAllowed = SkillCraftModels.CanCraftRegularRandom(state, activeScroll, proxyFee, out _);
                    bool targetAllowed = SkillCraftModels.CanCraftRegularTargeted(state, activeScroll, _selectedSkillId, proxyFee, out string targetReason);
                    bool canRand = !locked && randAllowed;
                    bool canTarget = !locked && targetAllowed;
                    Label(_body, "ScrollRegularHint",
                        $"随机拓印：{activeScroll.RandomFragmentCount} 残页 + {activeScroll.RandomGoldCost + proxyFee} 银两（产物可交易）\n" +
                        $"定向拓印（{ResolveSkillName(_selectedSkillId)}）：{activeScroll.TargetedFragmentCount} 残页 + {activeScroll.TargetedGoldCost + proxyFee} 银两",
                        960, 1140, 1480, 100, 32, true, Ink);
                    Button(_body, "CraftRandomRegularBtn", "随机拓印", 960, 1255, 680,
                        () => _client?.CraftRegularRandom(activeScroll.ScrollId, _selectedProxyMasterId), true, canRand);
                    Button(_body, "CraftTargetedRegularBtn", "定向拓印", 1740, 1255, 680,
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
                        960, 1132, 1480, 114, 30, true, Ink);
                    if (inkShort)
                    {
                        Button(_body, "CraftTargetedRareBtn", "定向制书（100%必出）", 960, 1255, 820,
                            () => _client?.CraftRareTargeted(activeScroll.ScrollId, _selectedSkillId, _selectedProxyMasterId), true, canRare);
                        Button(_body, "RareMallShortcutBtn", "百宝阁补秘墨", 1810, 1255, 630, () =>
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
                        Button(_body, "CraftTargetedRareBtn", "定向制书（100%必出）", 1260, 1255, 880,
                            () => _client?.CraftRareTargeted(activeScroll.ScrollId, _selectedSkillId, _selectedProxyMasterId), true, canRare);
                    }
                    if (!canRare && !string.IsNullOrEmpty(rareReason))
                        Label(_body, "ScrollCraftReason", rareReason, 960, 1395, 1480, 50, 30, color: Danger);
                }
            }

            // 右栏：我的未学书架（含 V4 安全批量一键拆多余常规书）
            Panel(_body, "BookshelfPaper", 2550, 390, 1280, 1020);
            Label(_body, "BookshelfTitle", "我的未学书架", 2620, 440, 520, 70, 46, color: Jade);
            var safeBatchIds = SkillCraftModels.SelectSafeBatchDismantleBookIds(state);
            bool shelfBusy = _client == null || _client.IsBusy || _client.HasPendingWrite;
            Button(_body, "BookshelfBatchDismantleBtn",
                safeBatchIds.Count > 0 ? $"一键拆多余({safeBatchIds.Count}本)" : "无多余常规书",
                3160, 428, 600, () =>
                {
                    _batchDismantleIds = safeBatchIds;
                    _modal = CraftModal.BatchDismantle;
                    Render();
                }, safeBatchIds.Count > 0, !shelfBusy && safeBatchIds.Count > 0);
            string learnTargetText = _learnPetId == 0 ? "研习对象：人物（点切换灵宠）" : $"研习对象：灵宠 #{_learnPetId}";
            Button(_body, "BookshelfTargetCycle", learnTargetText, 2610, 520, 1150, CycleLearnTarget);

            var books = state?.SkillBooks.ToList() ?? new List<CraftedSkillBookEntry>();
            var sealedBagBooks = (_bagSource?.CharacterBag?.Items ?? (IReadOnlyList<BagItemInfo>)Array.Empty<BagItemInfo>())
                .Where(it => it != null && it.ItemId != 0 && it.Count > 0 &&
                    it.ConfigId == SkillCraftModels.SealedSkillBookConfigId && it.SealedSkillBook != null)
                .ToList();
            int totalShelfRows = books.Count + sealedBagBooks.Count;
            var bookList = Scroll(_body, "BookshelfViewport", 2610, 680, 1160, 690, Math.Max(690, totalShelfRows * 225));
            bool bookLocked = _client == null || _client.IsBusy || _client.HasPendingWrite;
            for (int i = 0; i < books.Count; ++i)
            {
                var book = books[i];
                float y = i * 225;
                Label(bookList, "BookDesc_" + book.BookId, SkillCraftModels.DescribeBook(book, state, ResolveSkillName),
                    10, y, 1120, 70, 34, true, Ink);
                bool canDismantle = SkillCraftModels.CanDismantleBook(book);
                bool canSeal = SkillCraftModels.CanSealBook(book);
                if (canSeal && canDismantle)
                {
                    uint refund = SkillCraftModels.DismantleRefundOf(state, book);
                    Button(bookList, "BookLearn_" + book.BookId, "研习", 10, y + 75, 320,
                        () => _client?.LearnBook(book.BookId, _learnPetId), true, !bookLocked);
                    Button(bookList, "BookSeal_" + book.BookId, "封卷入包", 345, y + 75, 360,
                        () => _client?.SealBook(book.BookId), false, !bookLocked);
                    Button(bookList, "BookDismantle_" + book.BookId, $"拆页(返{refund})", 720, y + 75, 400,
                        () => { _dismantleConfirmBookId = book.BookId; Render(); }, false, !bookLocked);
                }
                else
                {
                    Button(bookList, "BookLearn_" + book.BookId, "研习", 10, y + 75, 480,
                        () => _client?.LearnBook(book.BookId, _learnPetId), true, !bookLocked);
                    if (canDismantle)
                    {
                        uint refund = SkillCraftModels.DismantleRefundOf(state, book);
                        Button(bookList, "BookDismantle_" + book.BookId, $"拆页（返{refund}残页）", 520, y + 75, 580,
                            () => { _dismantleConfirmBookId = book.BookId; Render(); }, false, !bookLocked);
                    }
                }
            }
            for (int i = 0; i < sealedBagBooks.Count; ++i)
            {
                var sealedItem = sealedBagBooks[i];
                float y = (books.Count + i) * 225;
                Label(bookList, "SealedBookDesc_" + sealedItem.ItemId,
                    SkillCraftModels.DescribeSealedBook(sealedItem, ResolveSkillName),
                    10, y, 1120, 70, 34, true, Jade);
                ulong sealedItemId = sealedItem.ItemId;
                Button(bookList, "SealedBookUnseal_" + sealedItemId, "开卷入书架", 10, y + 75, 560,
                    () => _client?.UnsealBook(sealedItemId), true, !bookLocked);
            }
            if (totalShelfRows == 0)
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
                    "官府托管 · 全区24小时离线代工：装备与材料全程不离身，宗师跨线/离线照常接单，95% 净银两经邮件自动入账（扣 5% 税）。",
                    260, 294, 3440, 64, 34, false, Jade);
            }
            else
            {
                Label(_body, "ProxySafetyBanner",
                    "官府托管 · 全区24小时离线代工：装备与材料全程不离身，宗师跨线/离线照常接单，95% 净银两经邮件自动入账（扣 5% 税）。",
                    180, 290, 3600, 80, 36, true, Jade);
            }

            Panel(_body, "MyProxyStallPaper", 130, 374, 3700, 308);
            bool isMaster = SkillCraftModels.CanOpenProxyStall(state);
            if (state != null && !_draftFeesInitialized)
            {
                _draftForgeFee = SkillCraftModels.IsValidProxyFee(state.ProxyForgeFeeGold) ? state.ProxyForgeFeeGold : 80;
                _draftScrollFee = SkillCraftModels.IsValidProxyFee(state.ProxyScrollFeeGold) ? state.ProxyScrollFeeGold : 80;
                _draftShowcaseTag = state.MasterShowcaseTag;
                _draftCraftTitleId = state.EquippedCraftTitleId;
                _draftTeamFreeProxy = state.TeamFreeProxy;
                _draftFeesInitialized = true;
            }
            string myStatus = !isMaster
                ? "我的代打摊位：铸器或制卷达到【宗师】后自动解锁 24 小时跨线/离线开摊、名匠雅号与同队情义免税。"
                : $"我的名匠摊位：{(state.ProxyStallOpen ? "24小时营业中（离线照常接单 · 收益走邮件）" : "未营业")} | 口碑 {state.ProxyOrdersCompleted} 单 · 极品 {state.SupremeForgedCount} · 稀世 {state.RareBooksCraftedCount}\n" +
                  $"装备代打费 {_draftForgeFee} 银两 · 技能代制费 {_draftScrollFee} 银两 | 招牌：{SkillCraftModels.ShowcaseTagName(_draftShowcaseTag)} | 雅号：{SkillCraftModels.CraftTitleName(_draftCraftTitleId)}";
            float myInfoX = 200f;
            if (isMaster)
            {
                var gmBadge = SeasonEconomyUiArt.LoadCraftIcon("icon_master_honor")
                    ?? SeasonEconomyUiArt.LoadTradeV2Slice("icon_master_seal")
                    ?? SeasonEconomyUiArt.LoadCraftControl("mastery_badge_grandmaster");
                if (gmBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("MyProxyMasterBadge", _body, 190, 400, 96, 96, gmBadge, false);
                    badgeImg.preserveAspect = true;
                    myInfoX = 300f;
                }
            }
            bool hasUnreadDigest = _client?.HasUnreadProxyDigest == true;
            float myInfoRight = hasUnreadDigest ? 3340f : 3450f;
            Label(_body, "MyProxyStallInfo", myStatus, myInfoX, 396, myInfoRight - myInfoX, 104, 34, true, isMaster ? Jade : Muted);
            if (hasUnreadDigest)
            {
                var digestBtn = GameplayUiArt.Button(_body, "营业战报", 3370, 404, 400, 86, () =>
                {
                    _modal = CraftModal.ProxyDigest;
                    Render();
                }, primary: true, enabled: true, fontSize: 30);
                digestBtn.name = "OpenProxyDigestBtn";
                var scrollIcon = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("icon_digest_scroll");
                if (scrollIcon != null)
                {
                    var dImg = QdaoUguiFactory.CreateImage("OpenProxyDigestIcon", digestBtn.transform, 64, 14, 58, 58, scrollIcon, false);
                    dImg.preserveAspect = true;
                    if (digestBtn.transform.Find("Label") is RectTransform dLbl)
                    {
                        dLbl.anchoredPosition = new Vector2(126f, 0f);
                        dLbl.sizeDelta = new Vector2(216f, 86f);
                    }
                }
            }
            if (isMaster)
            {
                bool busy = _client.IsBusy;
                Button(_body, "ForgeFeeMinus", "装备费-10", 190, 512, 290, () => { _draftForgeFee = Math.Max(SkillCraftModels.ProxyFeeMinGold, _draftForgeFee - 10); Render(); }, enabled: !busy);
                Button(_body, "ForgeFeePlus", "装备费+10", 495, 512, 290, () => { _draftForgeFee = Math.Min(SkillCraftModels.ProxyFeeMaxGold, _draftForgeFee + 10); Render(); }, enabled: !busy);
                Button(_body, "ScrollFeeMinus", "制书费-10", 815, 512, 290, () => { _draftScrollFee = Math.Max(SkillCraftModels.ProxyFeeMinGold, _draftScrollFee - 10); Render(); }, enabled: !busy);
                Button(_body, "ScrollFeePlus", "制书费+10", 1120, 512, 290, () => { _draftScrollFee = Math.Min(SkillCraftModels.ProxyFeeMaxGold, _draftScrollFee + 10); Render(); }, enabled: !busy);
                Button(_body, "ShowcaseTagCycle", $"招牌:{SkillCraftModels.ShowcaseTagName(_draftShowcaseTag)}", 1440, 512, 490, () =>
                {
                    _draftShowcaseTag = (_draftShowcaseTag + 1u) % (SkillCraftModels.MaxShowcaseTag + 1u);
                    Render();
                }, enabled: !busy);
                Button(_body, "CraftTitleCycle", $"雅号:{SkillCraftModels.CraftTitleName(_draftCraftTitleId)}", 1945, 512, 450, () =>
                {
                    CycleUnlockedCraftTitle(state);
                    Render();
                }, _draftCraftTitleId != 0, !busy);
                Button(_body, "TeamFreeProxyToggle", _draftTeamFreeProxy ? "✓ 同队情义免税" : "同队免税:关", 2410, 512, 420, () =>
                {
                    _draftTeamFreeProxy = !_draftTeamFreeProxy;
                    Render();
                }, _draftTeamFreeProxy, !busy);
                Button(_body, "ProxyStallOpenBtn", state.ProxyStallOpen ? "保存并保持营业" : "开启代打摊位", 2850, 512, 520,
                    () => _client.SetProxyStall(true, _draftForgeFee, _draftScrollFee, _draftShowcaseTag, _draftCraftTitleId, _draftTeamFreeProxy), true, !busy);
                if (state.ProxyStallOpen)
                    Button(_body, "ProxyStallCloseBtn", "收摊", 3390, 512, 380,
                        () => _client.SetProxyStall(false, 0, 0, _draftShowcaseTag, _draftCraftTitleId, _draftTeamFreeProxy), false, !busy);
                else
                    Button(_body, "ProxyProfileSaveBtn", "仅存雅号", 3390, 512, 380,
                        () => _client.SetProxyStall(false, 0, 0, _draftShowcaseTag, _draftCraftTitleId, _draftTeamFreeProxy), false, !busy);
            }

            Panel(_body, "CityMastersPaper", 130, 696, 3700, 754);
            var topHot = SkillCraftModels.TopHotStalls(_client?.ProxyStalls);
            float toolbarY = 720f;
            float listTopY = 804f;
            float listHeight = 624f;
            if (topHot.Count > 0)
            {
                RenderTopHotMastersStrip(topHot);
                toolbarY = 1004f;
                listTopY = 1084f;
                listHeight = 344f;
            }

            Label(_body, "CityMastersTitle", "全区24小时营业名匠", 190, toolbarY + 2f, 440, 64, 36, color: Jade);
            // 极品热榜与 V2 排序、筛选紧凑按钮组（使用可调高九宫格按钮，避免横向重叠）
            CreateCompactPillButton(_body, "SortHotRank", "热榜优先", 650, toolbarY, 260, 68, () => { _stallSortMode = ProxyStallSortMode.HotRank; Render(); }, _stallSortMode == ProxyStallSortMode.HotRank);
            CreateCompactPillButton(_body, "SortReputation", "口碑优先", 924, toolbarY, 260, 68, () => { _stallSortMode = ProxyStallSortMode.Reputation; Render(); }, _stallSortMode == ProxyStallSortMode.Reputation);
            CreateCompactPillButton(_body, "SortSupreme", "极品最多", 1198, toolbarY, 260, 68, () => { _stallSortMode = ProxyStallSortMode.SupremeCount; Render(); }, _stallSortMode == ProxyStallSortMode.SupremeCount);
            CreateCompactPillButton(_body, "SortLowestFee", "手工费低", 1472, toolbarY, 260, 68, () => { _stallSortMode = ProxyStallSortMode.LowestFee; Render(); }, _stallSortMode == ProxyStallSortMode.LowestFee);
            CreateCompactPillButton(_body, "FilterAll", "全部", 1780, toolbarY, 210, 68, () => { _stallFilterMode = ProxyStallFilterMode.All; Render(); }, _stallFilterMode == ProxyStallFilterMode.All);
            CreateCompactPillButton(_body, "FilterForge", "铸器宗师", 2004, toolbarY, 270, 68, () => { _stallFilterMode = ProxyStallFilterMode.ForgeMaster; Render(); }, _stallFilterMode == ProxyStallFilterMode.ForgeMaster);
            CreateCompactPillButton(_body, "FilterScroll", "制卷宗师", 2288, toolbarY, 270, 68, () => { _stallFilterMode = ProxyStallFilterMode.ScrollMaster; Render(); }, _stallFilterMode == ProxyStallFilterMode.ScrollMaster);
            CreateCompactPillButton(_body, "FilterTeamFree", "同队免税", 2572, toolbarY, 270, 68, () => { _stallFilterMode = ProxyStallFilterMode.TeamFree; Render(); }, _stallFilterMode == ProxyStallFilterMode.TeamFree);

            var stalls = SkillCraftModels.SortAndFilterProxyStalls(_client?.ProxyStalls, _stallSortMode, _stallFilterMode);
            var list = Scroll(_body, "CityMastersViewport", 190, listTopY, 3580, listHeight, Math.Max(listHeight, stalls.Count * 165));
            var gmRowBadge = SeasonEconomyUiArt.LoadCraftIcon("icon_master_honor")
                ?? SeasonEconomyUiArt.LoadTradeV2Slice("icon_master_seal")
                ?? SeasonEconomyUiArt.LoadCraftControl("mastery_badge_grandmaster");
            var sealRowBg = SeasonEconomyUiArt.LoadCraftControl("seal_master_signature");
            var freeProxyBadge = SeasonEconomyUiArt.LoadCraftControl("badge_team_free_proxy");
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
                        lineX, y + 24, 2040f - lineX, 92, sealRowBg, new Color(0.95f, 0.91f, 0.80f, 0.92f));
                    Label(list, "MasterLine_" + stall.MasterPlayerId, SkillCraftModels.DescribeProxyStall(stall),
                        lineX + 48f, y + 24, 1980f - lineX, 92, 28, true, Ink);
                }
                else
                {
                    Label(list, "MasterLine_" + stall.MasterPlayerId, SkillCraftModels.DescribeProxyStall(stall),
                        lineX, y + 20, 2040f - lineX, 105, 30, true, Ink);
                }
                if (stall.ViewerGetsFreeProxy && freeProxyBadge != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(list, "MasterFreeBadge_" + stall.MasterPlayerId,
                        1820, y + 2, 220, 48, freeProxyBadge, Color.white);
                    var freeLbl = Label(list, "MasterFreeBadgeText_" + stall.MasterPlayerId,
                        "同队免税", 1844, y + 2, 172, 48, 28, false, SeasonEconomyUiArt.GoldText);
                    freeLbl.fontStyle = TMPro.FontStyles.Bold;
                    freeLbl.alignment = TMPro.TextAlignmentOptions.Center;
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
                    220, listTopY + 60, 2600, 120, 44, true, Muted);
        }

        private static UnityEngine.UI.Button CreateCompactPillButton(
            UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Action click, bool primary = false, bool enabled = true, float fontSize = 26f)
        {
            var btn = GameplayUiArt.Button(parent, label, x, y, w, h, click, primary: primary, enabled: enabled, fontSize: fontSize);
            btn.name = name;
            return btn;
        }

        private void RenderTopHotMastersStrip(List<CraftProxyStallInfo> topHot)
        {
            // banner_hot_masters 为 1400x96 浅宣纸金框九宫格 [36,28,36,28]（无字），深玉绿叠字保证高对比可读。
            var bannerBg = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("banner_hot_masters");
            SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "HotMastersBannerBg", 963, 716, 2040, 78,
                bannerBg, Jade);
            var stripTitle = Label(_body, "HotMastersStripTitle", "西市极品热榜 Top 3 · 近24小时全区热度排行",
                1090, 716, 1786, 78, 36, false, bannerBg != null ? Jade : Cream);
            stripTitle.fontStyle = TMPro.FontStyles.Bold;
            stripTitle.alignment = TMPro.TextAlignmentOptions.Center;

            // 3 张 card_hot_master（520x136，九宫格 [36,28,36,28]）横向并排铺满 190..3770。
            var cardBg = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("card_hot_master");
            const float startX = 190f;
            const float cardW = 1174f;
            const float cardGap = 29f;
            const float cardY = 804f;
            const float cardH = 186f;
            for (int i = 0; i < topHot.Count && i < 3; ++i)
            {
                var stall = topHot[i];
                float cx = startX + i * (cardW + cardGap);
                if (cardBg != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "HotMasterCard_" + stall.HotRank, cx, cardY, cardW, cardH,
                        cardBg, Cream);
                }
                else
                {
                    Panel(_body, "HotMasterCard_" + stall.HotRank, cx, cardY, cardW, cardH);
                }

                var rankBadge = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("badge_hot_rank_" + stall.HotRank);
                string rankTag = SkillCraftModels.HotRankLabel(stall.HotRank);
                if (rankBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("HotRankBadge_" + stall.HotRank, _body,
                        cx + 24f, cardY + 25f, 136f, 136f, rankBadge, false);
                    badgeImg.preserveAspect = true;
                }
                else
                {
                    var rankLbl = Label(_body, "HotRankText_" + stall.HotRank, rankTag,
                        cx + 24f, cardY + 56f, 136f, 72f, 34, false, SeasonEconomyUiArt.GoldHighlight);
                    rankLbl.alignment = TMPro.TextAlignmentOptions.Center;
                }

                string mName = string.IsNullOrWhiteSpace(stall.MasterName) ? $"宗师#{stall.MasterPlayerId}" : stall.MasterName;
                string titlePrefix = stall.EquippedCraftTitleId != 0 ? SkillCraftModels.CraftTitleName(stall.EquippedCraftTitleId) + "·" : string.Empty;
                string rankPrefix = !string.IsNullOrEmpty(rankTag) ? $"{rankTag} · " : string.Empty;
                var nameLbl = Label(_body, "HotMasterName_" + stall.HotRank, $"{rankPrefix}{titlePrefix}{mName}",
                    cx + 174f, cardY + 28f, 640f, 52f, 36, false, Jade);
                nameLbl.fontStyle = TMPro.FontStyles.Bold;

                ulong masterId = stall.MasterPlayerId;
                bool canForge = stall.ForgeMasteryTier >= SkillCraftModels.MasterTier && stall.ProxyForgeFeeGold > 0;
                bool canScroll = stall.ScrollMasteryTier >= SkillCraftModels.MasterTier && stall.ProxyScrollFeeGold > 0;
                string feeNote = stall.ViewerGetsFreeProxy
                    ? "同队免税"
                    : canForge ? $"装备 {stall.ProxyForgeFeeGold} 银"
                    : canScroll ? $"制书 {stall.ProxyScrollFeeGold} 银" : string.Empty;
                string cardDesc = string.IsNullOrEmpty(feeNote)
                    ? $"近24h {stall.Recent24HOrders}单 · 极品 {stall.Recent24HSupreme}件\n累计极品 {stall.SupremeForgedCount}件 · 口碑 {stall.ProxyOrdersCompleted}单"
                    : $"近24h {stall.Recent24HOrders}单 · 极品 {stall.Recent24HSupreme}件\n累计极品 {stall.SupremeForgedCount}件 · {feeNote}";
                Label(_body, "HotMasterInfo_" + stall.HotRank, cardDesc, cx + 174f, cardY + 84f, 640f, 82f, 26, true, Ink);

                if (canForge)
                {
                    CreateCompactPillButton(_body, "HotMasterGo_" + stall.HotRank, "快捷代打",
                        cx + 826f, cardY + 52f, 312f, 82f, () =>
                        {
                            Hide();
                            RequestProxyEquipmentPolish?.Invoke(masterId);
                        }, primary: true, enabled: true, fontSize: 30f);
                }
                else if (canScroll)
                {
                    CreateCompactPillButton(_body, "HotMasterGo_" + stall.HotRank, "快捷代制",
                        cx + 826f, cardY + 52f, 312f, 82f, () =>
                        {
                            _selectedProxyMasterId = masterId;
                            _page = SkillCraftPage.Scrolls;
                            Render();
                        }, primary: true, enabled: true, fontSize: 30f);
                }
            }
        }

        private void RenderProxyDigestModal()
        {
            var shade = _modalLayer.GetComponent<Image>() ?? _modalLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .07f, .055f, .85f);
            shade.raycastTarget = true;

            // panel_proxy_digest 为 1300x540 九宫格 [120,80,120,80] 无字弹窗底板，居中放置。
            var panelBg = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("panel_proxy_digest");
            if (panelBg != null)
            {
                SeasonEconomyUiArt.CreateSlicedOrSimple(_modalLayer, "ProxyDigestPaper", 983, 273, 2000, 1040,
                    panelBg, Cream);
            }
            else
            {
                Panel(_modalLayer, "ProxyDigestPaper", 983, 273, 2000, 1040, true);
            }

            // 顶部浅宣纸金框标题条 + 112x112 营业账册等比图标（深玉绿叠字确保高对比可读）
            var headerPlaque = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("banner_hot_masters");
            SeasonEconomyUiArt.CreateSlicedOrSimple(_modalLayer, "ProxyDigestHeaderBar", 1383, 330, 1200, 96,
                headerPlaque, Jade);
            Color headerTitleColor = headerPlaque != null ? Jade : Cream;
            var scrollIcon = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("icon_digest_scroll");
            if (scrollIcon != null)
            {
                var iconImg = QdaoUguiFactory.CreateImage("ProxyDigestIcon", _modalLayer, 1652, 322, 112, 112, scrollIcon, false);
                iconImg.preserveAspect = true;
                var titleLbl = Label(_modalLayer, "ProxyDigestTitle", "名匠离线营业战报", 1778, 330, 540, 96, 48, false, headerTitleColor);
                titleLbl.fontStyle = TMPro.FontStyles.Bold;
            }
            else
            {
                var titleLbl = Label(_modalLayer, "ProxyDigestTitle", "名匠离线营业战报", 1483, 330, 1000, 96, 48, false, headerTitleColor);
                titleLbl.fontStyle = TMPro.FontStyles.Bold;
                titleLbl.alignment = TMPro.TextAlignmentOptions.Center;
            }

            var subLbl = Label(_modalLayer, "ProxyDigestSubtitle", "离线期间，匠心未歇", 1103, 434, 1760, 44, 34, false, Jade);
            subLbl.alignment = TMPro.TextAlignmentOptions.Center;

            // 中部四项战报数据并排展示卡（复用 card_hot_master 九宫格 [36,28,36,28] 细金边底卡）
            var digest = _client?.State?.UnreadProxyDigest;
            var statsFrame = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("card_hot_master");
            SeasonEconomyUiArt.CreateSlicedOrSimple(_modalLayer, "ProxyDigestStatsCard", 1093, 486, 1780, 280,
                statsFrame, new Color(0.97f, 0.94f, 0.86f, 0.95f));

            uint orders = digest?.OrdersCompleted ?? 0u;
            uint supreme = digest?.SupremeForgedCount ?? 0u;
            uint rare = digest?.RareBooksCraftedCount ?? 0u;
            ulong netGold = digest?.NetGoldEarned ?? 0UL;
            DrawDigestMetricColumn("DigestColOrders", 1125, "累计成交", $"{orders} 单", null);
            DrawDigestMetricColumn("DigestColSupreme", 1554, "极品产出", $"{supreme} 件", null);
            DrawDigestMetricColumn("DigestColRare", 1983, "稀世撰出", $"{rare} 件", null);
            DrawDigestMetricColumn("DigestColGold", 2412, "税后净银两", $"{netGold:N0}", "已寄往邮箱");

            var dividerColor = new Color(0.72f, 0.58f, 0.32f, 0.45f);
            for (int d = 1; d <= 3; ++d)
            {
                var div = QdaoUguiFactory.CreateImage("ProxyDigestDivider_" + d, _modalLayer, 1125f + d * 429f, 540f, 2f, 172f, null, false);
                div.color = dividerColor;
            }

            string buyerLine = !string.IsNullOrWhiteSpace(digest?.LastBuyerName)
                ? $"最近下单侠士：{digest.LastBuyerName}"
                : "全部代打净收益（已扣 5% 税）均已安全寄往邮箱，请随时查收。";
            var buyerLbl = Label(_modalLayer, "ProxyDigestBuyer", buyerLine, 1103, 790, 1760, 54, 36, false, Ink);
            buyerLbl.alignment = TMPro.TextAlignmentOptions.Center;

            // badge_title_unlocked 为 128x128 等比喜报徽标（展示 92x92），左侧等比叠放于细金边雅号行，严禁横向拉伸。
            var unlockedTitles = digest != null
                ? SkillCraftModels.DecodeUnlockedTitleNames(digest.NewlyUnlockedTitleMask)
                : new List<string>();
            if (unlockedTitles.Count > 0)
            {
                SeasonEconomyUiArt.CreateSlicedOrSimple(_modalLayer, "ProxyDigestTitleRowBg", 1223, 872, 1520, 112,
                    statsFrame, new Color(0.97f, 0.91f, 0.77f, 0.98f));
                var titleBadge = SeasonEconomyUiArt.LoadCraftProxyDigestSlice("badge_title_unlocked");
                float textX = 1283f;
                float textW = 1400f;
                if (titleBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("ProxyDigestTitleBadge", _modalLayer, 1264, 882, 92, 92, titleBadge, false);
                    badgeImg.preserveAspect = true;
                    textX = 1376f;
                    textW = 1326f;
                }
                var unlockedLbl = Label(_modalLayer, "ProxyDigestUnlockedBanner",
                    $"新解锁零属性雅号：{string.Join("、", unlockedTitles)}",
                    textX, 872, textW, 112, 38, false, Jade);
                unlockedLbl.fontStyle = TMPro.FontStyles.Bold;
                unlockedLbl.alignment = titleBadge != null ? TMPro.TextAlignmentOptions.MidlineLeft : TMPro.TextAlignmentOptions.Center;
            }
            else
            {
                var hintLbl = Label(_modalLayer, "ProxyDigestHint",
                    "保持 24 小时跨线营业，累计口碑与极品数将解锁专属零属性名匠雅号。",
                    1103, 896, 1760, 64, 32, false, Muted);
                hintLbl.alignment = TMPro.TextAlignmentOptions.Center;
            }

            bool busy = _client == null || _client.IsBusy;
            Button(_modalLayer, "ProxyDigestAckBtn", "收下战报", 1233, 1054, 640, () =>
            {
                _modal = CraftModal.None;
                _client?.AckProxyDigest();
                Render();
            }, false, !busy);
            Button(_modalLayer, "ProxyDigestAckMailBtn", "直达邮箱领银两", 1973, 1054, 760, () =>
            {
                _modal = CraftModal.None;
                _client?.AckProxyDigest();
                Hide();
                if (RequestOpenMail != null) RequestOpenMail.Invoke();
                else MmorpgClient.UI.Ugui.Mail.MailUiRoot.Instance?.Toggle();
            }, true, !busy);
        }

        private void DrawDigestMetricColumn(string prefix, float x, string heading, string value, string footnote)
        {
            var headLbl = Label(_modalLayer, prefix + "_Head", heading, x + 12f, 520f, 405f, 48f, 32, false, Ink);
            headLbl.alignment = TMPro.TextAlignmentOptions.Center;
            var valLbl = Label(_modalLayer, prefix + "_Val", value, x + 12f, 582f, 405f, 84f, 56, false, Jade);
            valLbl.fontStyle = TMPro.FontStyles.Bold;
            valLbl.alignment = TMPro.TextAlignmentOptions.Center;
            if (!string.IsNullOrEmpty(footnote))
            {
                var footLbl = Label(_modalLayer, prefix + "_Foot", footnote, x + 12f, 676f, 405f, 42f, 26, false, Muted);
                footLbl.alignment = TMPro.TextAlignmentOptions.Center;
            }
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
                () => { _dismantleConfirmBookId = 0; _modal = CraftModal.None; Render(); });
            Button(_modalLayer, "DismantleConfirm", "确认拆页", 2160, 1020, 680, () =>
            {
                ulong id = _dismantleConfirmBookId;
                _dismantleConfirmBookId = 0;
                _modal = CraftModal.None;
                if (id != 0) _client?.DismantleBook(id);
                Render();
            }, true, book != null && _client != null && !_client.IsBusy);
        }

        private void RenderBatchDismantleModal()
        {
            var shade = _modalLayer.GetComponent<Image>() ?? _modalLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .07f, .055f, .85f);
            shade.raycastTarget = true;
            Panel(_modalLayer, "BatchDismantlePaper", 730, 300, 2500, 960, true);
            var state = _client?.State;
            uint totalRefund = 0;
            var names = new List<string>();
            if (state != null)
            {
                foreach (ulong bid in _batchDismantleIds)
                {
                    var b = state.SkillBooks.FirstOrDefault(x => x.BookId == bid);
                    if (b == null || b.RarityTier != 0) continue;
                    totalRefund += SkillCraftModels.DismantleRefundOf(state, b);
                    if (names.Count < 8) names.Add(ResolveSkillName(b.SkillTableId));
                }
            }
            string previewNames = names.Count > 0 ? string.Join("、", names) + (_batchDismantleIds.Count > names.Count ? " 等" : string.Empty) : "无";
            Label(_modalLayer, "BatchDismantleTitle", "批量拆解多余常规技能书", 900, 390, 2160, 90, 56, color: Jade);
            Label(_modalLayer, "BatchDismantleBody",
                $"已安全筛选 {_batchDismantleIds.Count} 本多余常规书（仅含已习得或书架重复书，绝不误拆孤本未学书与稀世绝学）：\n{previewNames}\n\n" +
                $"确认拆解后将一次性返还 {totalRefund} 张绑定通用悟道残页。",
                900, 520, 2160, 420, 38, true, Ink);
            Button(_modalLayer, "BatchDismantleCancel", "取消", 1040, 1050, 680,
                () => { _batchDismantleIds.Clear(); _modal = CraftModal.None; Render(); });
            Button(_modalLayer, "BatchDismantleConfirm", "确认一键拆页", 2160, 1050, 680, () =>
            {
                var ids = _batchDismantleIds.ToList();
                _batchDismantleIds.Clear();
                _modal = CraftModal.None;
                if (ids.Count > 0) _client?.BatchDismantleBooks(ids);
                Render();
            }, true, _batchDismantleIds.Count > 0 && _client != null && !_client.IsBusy);
        }

        private void RenderBuildAdvisorModal()
        {
            var shade = _modalLayer.GetComponent<Image>() ?? _modalLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .07f, .055f, .85f);
            shade.raycastTarget = true;
            Panel(_modalLayer, "BuildAdvisorPaper", 360, 160, 3240, 1260, true);
            Label(_modalLayer, "BuildAdvisorTitle", "典籍藏经阁 · 六大流派缺书体检（点击缺书直跳定向拓印）",
                460, 220, 2400, 80, 46, color: Jade);
            Button(_modalLayer, "BuildAdvisorClose", "关闭", 3120, 210, 380,
                () => { _modal = CraftModal.None; Render(); });

            var state = _client?.State;
            var advices = SkillCraftModels.EvaluateBuildBookAdvisor(state);
            var list = Scroll(_modalLayer, "BuildAdvisorViewport", 440, 325, 3080, 1020, Math.Max(1020, advices.Count * 225));
            for (int i = 0; i < advices.Count; ++i)
            {
                var adv = advices[i];
                float y = i * 225;
                string head = $"{adv.Archetype.Name}（{adv.UnlockedCount}/{adv.Archetype.SkillIds.Length} 已解锁） — {adv.Archetype.Summary}";
                Label(list, "BuildArchHead_" + i, head, 15, y + 6, 3000, 56, 34, false,
                    adv.MissingSkills.Count == 0 ? SeasonEconomyUiArt.GoldHighlight : Jade);
                if (adv.MissingSkills.Count == 0)
                {
                    Label(list, "BuildArchComplete_" + i, "✓ 该流派核心技能已全部解锁！", 15, y + 72, 1600, 64, 32, false, Jade);
                }
                else
                {
                    for (int m = 0; m < adv.MissingSkills.Count && m < 5; ++m)
                    {
                        var miss = adv.MissingSkills[m];
                        string scrollName = SkillCraftModels.ScrollNameOf(state, miss.ScrollId);
                        uint targetScroll = miss.ScrollId;
                        uint targetSkill = miss.SkillId;
                        Button(list, $"BuildMiss_{i}_{targetSkill}", $"缺:{ResolveSkillName(targetSkill)}({scrollName})",
                            15 + m * 600, y + 74, 580, () =>
                            {
                                _modal = CraftModal.None;
                                _page = SkillCraftPage.Scrolls;
                                if (targetScroll != 0) _selectedScrollId = targetScroll;
                                _selectedSkillId = targetSkill;
                                Render();
                            }, false, targetScroll != 0);
                    }
                }
            }
        }

        private void RenderWantedBoardPage()
        {
            var state = _client?.State;
            var escrowBanner = SeasonEconomyUiArt.LoadCraftControl("banner_safe_escrow");
            if (escrowBanner != null)
            {
                SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "WantedSafetyBannerBg", 150, 284, 3660, 84,
                    escrowBanner, new Color(0.93f, 0.91f, 0.82f, 0.96f));
                Label(_body, "WantedSafetyBanner",
                    "西市封卷悬赏求书板（全服可见）：发榜托管流通银两，托管中与挂榜中可撤单全额退回；揭榜交背包【3414 封卷道籍】或书架未绑常规书（自动先封卷），先书后款，成交扣 5% 银两税，发榜人收到背包里的封卷道籍。",
                    220, 294, 3520, 64, 30, false, Jade);
            }
            else
            {
                Label(_body, "WantedSafetyBanner",
                    "西市封卷悬赏求书板（全服可见）：发榜托管流通银两，挂榜中可撤单全额退回；揭榜交背包封卷或书架未绑常规书，先书后款（扣 5% 税）。",
                    180, 290, 3600, 76, 34, true, Jade);
            }

            // 上半区左：发布求书悬赏；上半区右：我的在榜悬赏（最多 3 条）
            var regularSkills = new List<uint>();
            if (state != null)
            {
                foreach (var sc in state.Scrolls)
                {
                    if (sc == null || sc.RarityTier != 0) continue;
                    foreach (uint sid in sc.SkillTableIds) regularSkills.Add(sid);
                }
            }
            if (_wantedSkillId == 0 || !regularSkills.Contains(_wantedSkillId))
                _wantedSkillId = regularSkills.FirstOrDefault();

            Panel(_body, "PostWantedPaper", 130, 385, 1980, 360);
            Label(_body, "PostWantedTitle",
                $"发布常规技能求书单（持有流通银两：{state?.GoldBalance ?? 0} · 范围 100–50000）",
                190, 412, 1860, 62, 34, color: Jade);
            // 发榜或揭榜之后等服务端结论期间,悬赏页的写按钮一并锁住。
            bool writeLocked = _client == null || _client.IsBusy || _client.HasPendingWrite || state == null
                || _client.WantedFollowUpActive;
            var myOrders = _client?.MyWantedOrders.ToList() ?? new List<global::Trade.WantedSkillBookOrderView>();
            Button(_body, "WantedSkillPrev", "←换技", 190, 488, 250, () =>
            {
                if (regularSkills.Count == 0) return;
                int idx = regularSkills.IndexOf(_wantedSkillId);
                _wantedSkillId = regularSkills[(idx <= 0 ? regularSkills.Count : idx) - 1];
                Render();
            }, enabled: regularSkills.Count > 1 && !writeLocked);
            Label(_body, "WantedSkillCurrent",
                _wantedSkillId != 0 ? $"求购：{ResolveSkillName(_wantedSkillId)}" : "暂无常规技能可选",
                455, 502, 580, 66, 34, false, Ink);
            Button(_body, "WantedSkillNext", "换技→", 1045, 488, 250, () =>
            {
                if (regularSkills.Count == 0) return;
                int idx = regularSkills.IndexOf(_wantedSkillId);
                _wantedSkillId = regularSkills[(idx + 1) % regularSkills.Count];
                Render();
            }, enabled: regularSkills.Count > 1 && !writeLocked);
            Button(_body, "WantedSigToggle", _wantedRequireMasterSig ? "✓ 仅收宗师署名" : "不限宗师署名",
                1320, 488, 730, () => { _wantedRequireMasterSig = !_wantedRequireMasterSig; Render(); },
                _wantedRequireMasterSig, !writeLocked);

            Button(_body, "WantedGoldMinus", "悬赏-100", 190, 610, 310, () =>
            {
                _wantedRewardGold = _wantedRewardGold > SkillCraftModels.WantedOrderMinGold + 100
                    ? _wantedRewardGold - 100 : SkillCraftModels.WantedOrderMinGold;
                Render();
            }, enabled: !writeLocked);
            Label(_body, "WantedGoldValue", $"悬赏 {_wantedRewardGold} 银两", 515, 626, 440, 66, 34, false, Jade);
            Button(_body, "WantedGoldPlus", "悬赏+100", 965, 610, 310, () =>
            {
                _wantedRewardGold = Math.Min(SkillCraftModels.WantedOrderMaxGold, _wantedRewardGold + 100);
                Render();
            }, enabled: !writeLocked);
            bool canPost = !writeLocked && _wantedSkillId != 0 && SkillCraftModels.IsValidWantedRewardGold(_wantedRewardGold)
                && state.GoldBalance >= _wantedRewardGold && myOrders.Count < SkillCraftModels.MaxActiveWantedOrders;
            Button(_body, "PostWantedSubmitBtn", "发榜托管求书", 1320, 610, 730,
                () => _client?.PostWantedOrder(_wantedSkillId, _wantedRewardGold, _wantedRequireMasterSig), true, canPost);

            Panel(_body, "MyWantedPaper", 2140, 385, 1690, 360);
            Label(_body, "MyWantedTitle", $"我的悬赏（{myOrders.Count}/{SkillCraftModels.MaxActiveWantedOrders}）",
                2200, 412, 1560, 62, 34, color: Jade);
            for (int i = 0; i < myOrders.Count && i < 3; ++i)
            {
                var ord = myOrders[i];
                float y = 480 + i * 86;
                Label(_body, "MyWantedLine_" + ord.OrderId,
                    $"[{SkillCraftModels.DescribeWantedPhase(ord.Phase)}] 求【{SkillCraftModels.ResolveWantedSkillName(ord, ResolveSkillName)}】{(ord.RequireMasterSignature ? "[须署名]" : "")} · {ord.RewardGold} 银",
                    2200, y + 10, 1120, 62, 28, false, Ink);
                string oid = ord.OrderId;
                Button(_body, "MyWantedCancel_" + oid, "撤单全退", 3340, y, 420,
                    () => _client?.CancelWantedOrder(oid), false, !writeLocked && SkillCraftModels.CanCancelWantedOrder(ord));
            }
            if (myOrders.Count == 0)
                Label(_body, "MyWantedEmpty", "暂无挂单，可在左侧发榜求购心仪常规书。", 2200, 540, 1520, 80, 32, true, Muted);

            // 下半区：全城悬赏求书板（支持背包 3414 封卷或书架未绑常规书一键揭榜）
            Panel(_body, "CityWantedPaper", 130, 765, 3700, 645);
            Label(_body, "CityWantedTitle", "全城悬赏求书榜（有匹配的未绑封卷或书架常规书即可一键揭榜领银两）",
                190, 795, 2900, 66, 38, color: Jade);
            Button(_body, "CityWantedRefreshBtn", "刷新求书榜", 3220, 785, 520,
                () => _client?.RefreshWantedOrders(), false, _client != null && !_client.WantedOrdersLoading);

            var allOrders = _client?.WantedOrders ?? Array.Empty<global::Trade.WantedSkillBookOrderView>();
            bool hasMoreOrders = _client != null && _client.HasMoreWantedOrders;
            var wantedList = Scroll(_body, "CityWantedViewport", 190, 880, 3580, 500,
                Math.Max(500, (allOrders.Count + (hasMoreOrders ? 1 : 0)) * 145));
            for (int i = 0; i < allOrders.Count; ++i)
            {
                var order = allOrders[i];
                float y = i * 145;
                Label(wantedList, "WantedOrderLine_" + order.OrderId,
                    SkillCraftModels.DescribeWantedOrder(order, ResolveSkillName),
                    15, y + 18, 2680, 96, 32, true, Ink);
                bool isMine = order.IsMine;
                var match = isMine ? default : SkillCraftModels.FindMatchingFulfillSource(order, state, _bagSource?.CharacterBag);
                string btnLabel = isMine ? "本人悬赏单" :
                    match.SealedItemId != 0 ? "交背包封卷揭榜" :
                    match.BookshelfBookId != 0 ? "交书架书揭榜" : "无匹配未绑书";
                string orderId = order.OrderId;
                Button(wantedList, $"WantedFulfill_{orderId}", btnLabel, 2730, y + 10, 790, () =>
                {
                    if (match.SealedItemId != 0) _client?.FulfillWantedOrderWithSealedItem(orderId, match.SealedItemId);
                    else if (match.BookshelfBookId != 0) _client?.FulfillWantedOrderWithBookshelfBook(orderId, match.BookshelfBookId);
                }, match.Found && !isMine, !writeLocked && !isMine && match.Found);
            }
            // 公开榜按游标分页(悬赏额从高到低),列表末尾一行续取下一页。
            if (hasMoreOrders)
                Button(wantedList, "CityWantedMoreBtn", "加载更多悬赏", 2730, allOrders.Count * 145 + 10, 790,
                    () => _client?.RefreshWantedOrders(nextPage: true), false, !_client.WantedOrdersLoading);
            if (allOrders.Count == 0)
                Label(_body, "CityWantedEmpty", _client?.WantedOrdersLoading == true ? "正在读取西市悬赏求书榜…" : "当前暂无悬赏求书单，可率先发榜求购心仪技能书。",
                    220, 960, 2800, 120, 40, true, Muted);
        }

        private void CycleUnlockedCraftTitle(SkillCraftingStateSnapshot state)
        {
            var unlocked = new List<uint> { 0u };
            if (state != null)
            {
                foreach (uint id in state.UnlockedCraftTitleIds)
                    if (id != 0 && !unlocked.Contains(id)) unlocked.Add(id);
            }
            int idx = unlocked.IndexOf(_draftCraftTitleId);
            _draftCraftTitleId = unlocked[(idx + 1) % unlocked.Count];
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
            if (stall == null) return 0;
            return stall.ViewerGetsFreeProxy ? 0UL : stall.ProxyScrollFeeGold;
        }

        private bool SelectedScrollProxyIsTeamFree()
        {
            if (_selectedProxyMasterId == 0 || _client == null) return false;
            var stall = _client.ProxyStalls.FirstOrDefault(s => s.MasterPlayerId == _selectedProxyMasterId);
            return stall != null && stall.ViewerGetsFreeProxy;
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
            // 优先取 UI/Skills/{id} 正式 512x512 图标（含卷 2/3/4/5/6），缺省再回退本地图鉴原型画芯。
            var direct = skillId != 0u ? QdaoUguiTheme.LoadSprite("UI/Skills/" + skillId) : null;
            if (direct != null) return direct;
            var def = SkillUiRoot.FindPresentation(skillId);
            return def != null ? QdaoUguiTheme.LoadSprite(def.IconResource) : null;
        }

        private static string ResolveSkillName(uint skillId)
        {
            var def = SkillUiRoot.FindPresentation(skillId);
            return !string.IsNullOrWhiteSpace(def?.name) ? def.name : $"技能 · {skillId}";
        }

        private static string ResolveSkillSummary(uint skillId)
        {
            if (skillId == 0u) return string.Empty;
            var row = MmorpgClient.Table.SkillTableManager.Instance.FindById(skillId);
            string desc = !string.IsNullOrWhiteSpace(row?.Description)
                ? row.Description
                : SkillUiRoot.FindPresentation(skillId)?.description;
            if (string.IsNullOrWhiteSpace(desc)) return string.Empty;
            string scope = row == null ? string.Empty : row.CasterMask switch
            {
                1u => "【人物专属】",
                2u => "【灵宠专属】",
                3u => "【人宠共用】",
                _ => string.Empty,
            };
            return scope + desc;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Hide();
            BindBagSource(null);
            Bind(null);
            _disposed = true;
            Closed = null;
            RequestProxyEquipmentPolish = null;
            RequestOpenMail = null;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }
    }
}
