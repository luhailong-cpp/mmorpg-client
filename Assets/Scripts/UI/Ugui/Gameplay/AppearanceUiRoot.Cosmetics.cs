using System;
using System.Collections.Generic;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.Skills;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 衣柜「人物 / 灵宠 / 特效」三类别页签与灵宠幻化、技能特效预览/穿戴视图（挂载于同一 AppearanceUiRoot 窗口）。
    /// 切片来自 Assets/Resources/UI/Ugui/WardrobeCosmetics/（按原比例 Simple 绘制，动态文案由 TMP 叠加）。
    /// </summary>
    public sealed partial class AppearanceUiRoot
    {
        private sealed class CosmeticRowView
        {
            public uint ConfigId;
            public GameObject Root;
            public Button Button;
            public Image Background;
            public Image IconImage;
            public TMP_Text IconMissingText;
            public TMP_Text NameText;
            public TMP_Text StateText;
        }

        private readonly WardrobeCosmeticTabState _cosmeticState = new();
        private readonly List<CosmeticRowView> _cosmeticRows = new();
        private readonly List<WardrobePetTargetOption> _petTargetBuffer = new();
        private readonly List<WardrobeSkillTargetOption> _skillTargetBuffer = new();
        private List<WardrobeCosmeticRowView> _currentCosmeticRowViews = new();
        private string _cosmeticListSignature = "";

        private TMP_Text _windowMainTitleText;
        private TMP_Text _windowSubTitleText;

        private RectTransform _categoryTabBar;
        private readonly Button[] _categoryTabButtons = new Button[3];
        private readonly Image[] _categoryTabImages = new Image[3];
        private readonly TMP_Text[] _categoryTabLabels = new TMP_Text[3];

        private RectTransform _cosmeticsRoot;
        private Button _cosmeticCompactBackBtn;
        private Image _cosmeticSectionHeader;
        private TMP_Text _cosmeticSectionTitle;
        private RectTransform _cosmeticListViewport;
        private ScrollRect _cosmeticListScroll;
        private RectTransform _cosmeticListContent;
        private GameObject _cosmeticListScrollbarObj;
        private TMP_Text _cosmeticEmptyLabel;
        private Button _cosmeticRetryBtn;

        private RectTransform _cosmeticPreviewPanel;
        private RectTransform _cosmeticPreviewClip;
        private Image _cosmeticPreviewImage;
        private TMP_Text _cosmeticPreviewBadge;
        private TMP_Text _cosmeticPreviewMissing;

        private RectTransform _cosmeticDetailPanel;
        private TMP_Text _cosmeticDetailTitle;
        private TMP_Text _cosmeticDetailState;
        private RectTransform _cosmeticDetailDivider;
        private TMP_Text _cosmeticTargetCaption;
        private TMP_Text _cosmeticTargetValue;
        private Button _cosmeticTargetChangeBtn;
        private TMP_Text _cosmeticTargetChangeLabel;
        private TMP_Text _cosmeticScopeCaption;
        private TMP_Text _cosmeticScopeValue;
        private TMP_Text _cosmeticDescText;
        private Button _cosmeticSecondaryBtn;
        private TMP_Text _cosmeticSecondaryLabel;
        private Button _cosmeticPrimaryBtn;
        private TMP_Text _cosmeticPrimaryLabel;
        private TMP_Text _cosmeticStatusText;

        // 技能特效窗口内预览播放态（纯 Update 驱动，WebGL 单线程安全，切项/离页/断线立即停播）
        private StripAnim _activeVfxStrip;
        private string _activeVfxPreviewKey = "";
        private float _activeVfxElapsed;
        private bool _activeVfxPlaying;
        private Color _activeVfxTint = Color.white;
        private Func<ulong, bool> _sellableMallSkuOverride;

        public WardrobeCategoryTab ActiveWardrobeCategory => _cosmeticState.ActiveCategory;

#if UNITY_EDITOR
        public WardrobeCosmeticTabState CosmeticTabState => _cosmeticState;
        public RectTransform CosmeticsRoot => _cosmeticsRoot;
        public void SetSellableMallSkuOverrideForVerification(Func<ulong, bool> lookup) => _sellableMallSkuOverride = lookup;
#endif

        /// <summary>从灵宠界面上下文打开衣柜并定位到「灵宠」页与指定灵宠实例。</summary>
        public void OpenPetCosmetics(ulong petId = 0ul)
        {
            if (!_available || _window == null) return;
            _cosmeticState.OpenContextual(WardrobeCategoryTab.Pet, targetPetId: petId);
            ShowPanelCore(previewOnly: false, preserveCategory: true);
        }

        /// <summary>从技能界面上下文打开衣柜并定位到「特效」页与指定技能。</summary>
        public void OpenSkillEffectCosmetics(uint skillTableId = 0u)
        {
            if (!_available || _window == null) return;
            _cosmeticState.OpenContextual(WardrobeCategoryTab.SkillEffect, targetSkillTableId: skillTableId);
            ShowPanelCore(previewOnly: false, preserveCategory: true);
        }

        private void PrepareWardrobeCosmeticsOnShow(bool preserveCategory)
        {
            if (!preserveCategory) _cosmeticState.OpenContextual(WardrobeCategoryTab.Character);
            if (_externalVerificationClient || _cosmeticState.ActiveCategory == WardrobeCategoryTab.Character) return;
            JubaozhaiUiRoot.Instance?.RequestOfficialSkusRefresh();
            if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.Pet) PetClient.Instance?.RequestList();
        }

        private void OnWardrobeCosmeticsHide(bool wasVisible)
        {
            if (wasVisible && _cosmeticState.ActiveCategory != WardrobeCategoryTab.Character &&
                _cosmeticListScroll != null && _cosmeticListScroll.gameObject.activeInHierarchy)
            {
                _cosmeticState.SaveScroll(_cosmeticState.ActiveCategory, _cosmeticListScroll.verticalNormalizedPosition);
            }
            StopVfxPreview();
            _cosmeticState.ResetForClose();
        }

        private bool TryHandleCosmeticsEscape()
        {
            if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.Character) return false;
            if (_compactLayout && _cosmeticState.CompactDetailOpen)
            {
                _cosmeticState.ExitCompactDetail();
                StopVfxPreview();
                Render();
                return true;
            }
            return false;
        }

        private void BuildWardrobeCosmeticsUi(RectTransform mainRoot)
        {
            // 顶部三类别页签：人物 / 灵宠 / 特效
            _categoryTabBar = QdaoUguiFactory.CreateRect("WardrobeCategoryTabBar", mainRoot, 68f, 24f, 640f, 56f);
            string[] tabLabels = { "人物", "灵宠", "特效" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var sprite = AppearanceUiArt.LoadWardrobeCosmetic(i == 0 ? "tab-selected" : "tab-default");
                var btn = QdaoUguiFactory.CreateArtButton("WardrobeTab_" + idx, _categoryTabBar,
                    i * 211f, 0f, 198f, 56f, sprite, out var img);
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = sprite != null ? Color.white : (i == 0 ? AppearanceUiArt.DeepJade : AppearanceUiArt.ParchmentInner);
                btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                btn.onClick.AddListener(() => SelectWardrobeCategory((WardrobeCategoryTab)idx));
                var lbl = QdaoUguiFactory.CreateText("Label", btn.transform, 10f, 0f, 178f, 56f,
                    tabLabels[i], 24f, i == 0 ? AppearanceUiArt.WarmCream : AppearanceUiArt.JadeInk,
                    TextAlignmentOptions.Center);
                lbl.rectTransform.anchorMin = Vector2.zero;
                lbl.rectTransform.anchorMax = Vector2.one;
                lbl.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                lbl.rectTransform.offsetMin = new Vector2(8f, 0f);
                lbl.rectTransform.offsetMax = new Vector2(-8f, 0f);
                lbl.richText = false;
                lbl.textWrappingMode = TextWrappingModes.NoWrap;
                lbl.fontStyle = i == 0 ? FontStyles.Bold : FontStyles.Normal;
                _categoryTabButtons[i] = btn;
                _categoryTabImages[i] = img;
                _categoryTabLabels[i] = lbl;
            }

            // 灵宠 / 特效共用内容层（人物页时整层隐藏，零侵入原人物衣柜）
            _cosmeticsRoot = QdaoUguiFactory.CreateRect("WardrobeCosmeticsRoot", mainRoot,
                0f, 0f, AppearanceUiArt.FrameWidth, AppearanceUiArt.MainBodyHeight);

            _cosmeticCompactBackBtn = CreateCosmeticTextButton(_cosmeticsRoot, "CosmeticBackToList", "< 返回列表",
                847f, 68f, 190f, 52f, () =>
                {
                    _cosmeticState.ExitCompactDetail();
                    StopVfxPreview();
                    Render();
                }, primary: false, fontSize: 22f);
            _cosmeticCompactBackBtn.gameObject.SetActive(false);

            // 左栏：分类标题条 + 可滚动外观列表
            var sectionSprite = AppearanceUiArt.LoadWardrobeCosmetic("section");
            _cosmeticSectionHeader = AppearanceUiArt.FixedArt(_cosmeticsRoot, "CosmeticSectionHeader",
                sectionSprite, 104f, 140f, 480f, 64f, AppearanceUiArt.DeepJade, preserveAspect: true);
            _cosmeticSectionTitle = QdaoUguiFactory.CreateText("Title", _cosmeticSectionHeader.transform,
                40f, 0f, 400f, 64f, "幻化外观", 26f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            _cosmeticSectionTitle.richText = false;
            _cosmeticSectionTitle.fontStyle = FontStyles.Bold;

            _cosmeticListViewport = QdaoUguiFactory.CreateRect("CosmeticListViewport", _cosmeticsRoot, 104f, 224f, 510f, 610f);
            var hit = _cosmeticListViewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;
            _cosmeticListViewport.gameObject.AddComponent<RectMask2D>();
            _cosmeticListContent = QdaoUguiFactory.CreateRect("CosmeticListContent", _cosmeticListViewport, 0f, 0f, 510f, 610f);
            _cosmeticListScroll = _cosmeticListViewport.gameObject.AddComponent<ScrollRect>();
            _cosmeticListScroll.viewport = _cosmeticListViewport;
            _cosmeticListScroll.content = _cosmeticListContent;
            _cosmeticListScroll.horizontal = false;
            _cosmeticListScroll.vertical = true;
            _cosmeticListScroll.movementType = ScrollRect.MovementType.Clamped;
            _cosmeticListScroll.scrollSensitivity = 48f;
            AppearanceUiArt.AddVerticalScrollbar(_cosmeticListScroll, _cosmeticsRoot,
                "CosmeticListScrollbar", 622f, 224f, 12f, 610f);
            _cosmeticListScrollbarObj = _cosmeticsRoot.Find("CosmeticListScrollbar")?.gameObject;

            _cosmeticEmptyLabel = QdaoUguiFactory.CreateText("CosmeticEmptyLabel", _cosmeticsRoot,
                114f, 340f, 490f, 120f, "", 24f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _cosmeticEmptyLabel.richText = false;
            _cosmeticEmptyLabel.textWrappingMode = TextWrappingModes.Normal;
            _cosmeticEmptyLabel.gameObject.SetActive(false);

            _cosmeticRetryBtn = CreateCosmeticTextButton(_cosmeticsRoot, "CosmeticRetryBtn", "重新同步",
                220f, 470f, 260f, 60f, () => _client?.RequestInfo(force: true), primary: false, fontSize: 22f);
            _cosmeticRetryBtn.gameObject.SetActive(false);

            // 中栏：带 RectMask2D 边界裁剪的预览台
            var previewSprite = AppearanceUiArt.LoadWardrobeCosmetic("pet-preview-panel");
            var previewImg = AppearanceUiArt.FixedArt(_cosmeticsRoot, "CosmeticPreviewPanel",
                previewSprite, 690f, 134f, 560f, 650f, AppearanceUiArt.ParchmentCard, preserveAspect: true);
            _cosmeticPreviewPanel = previewImg.rectTransform;

            _cosmeticPreviewClip = QdaoUguiFactory.CreateRect("CosmeticPreviewClip", _cosmeticPreviewPanel,
                26f, 28f, 508f, 594f);
            _cosmeticPreviewClip.gameObject.AddComponent<RectMask2D>();
            _cosmeticPreviewImage = QdaoUguiFactory.CreateImage("CosmeticPreviewImage", _cosmeticPreviewClip,
                24f, 24f, 460f, 500f, null);
            _cosmeticPreviewImage.preserveAspect = true;
            _cosmeticPreviewImage.enabled = false;

            _cosmeticPreviewBadge = QdaoUguiFactory.CreateText("CosmeticPreviewBadge", _cosmeticPreviewClip,
                24f, 14f, 240f, 36f, "", 20f, AppearanceUiArt.CrimsonInk, TextAlignmentOptions.MidlineLeft);
            _cosmeticPreviewBadge.richText = false;
            _cosmeticPreviewBadge.fontStyle = FontStyles.Bold;
            _cosmeticPreviewBadge.gameObject.SetActive(false);

            _cosmeticPreviewMissing = QdaoUguiFactory.CreateText("CosmeticPreviewMissing", _cosmeticPreviewClip,
                40f, 220f, 428f, 120f, "暂无预览资源", 24f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _cosmeticPreviewMissing.richText = false;
            _cosmeticPreviewMissing.textWrappingMode = TextWrappingModes.Normal;
            _cosmeticPreviewMissing.gameObject.SetActive(false);

            // 右栏：名称、状态、目标灵宠/适用技能、生效范围、说明与双操作按钮
            _cosmeticDetailPanel = QdaoUguiFactory.CreateRect("CosmeticDetailPanel", _cosmeticsRoot,
                1310f, 140f, 740f, 650f);

            _cosmeticDetailTitle = QdaoUguiFactory.CreateText("DetailTitle", _cosmeticDetailPanel,
                0f, 12f, 720f, 62f, "", 46f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            _cosmeticDetailTitle.richText = false;
            _cosmeticDetailTitle.fontStyle = FontStyles.Bold;
            _cosmeticDetailTitle.enableAutoSizing = true;
            _cosmeticDetailTitle.fontSizeMin = 26f;
            _cosmeticDetailTitle.fontSizeMax = 46f;

            _cosmeticDetailState = QdaoUguiFactory.CreateText("DetailState", _cosmeticDetailPanel,
                0f, 82f, 720f, 38f, "", 24f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _cosmeticDetailState.richText = false;

            var divImg = QdaoUguiFactory.CreateImage("DetailDivider", _cosmeticDetailPanel, 0f, 134f, 700f, 2f, null);
            divImg.color = new Color(AppearanceUiArt.ParchmentBorder.r, AppearanceUiArt.ParchmentBorder.g,
                AppearanceUiArt.ParchmentBorder.b, 0.7f);
            _cosmeticDetailDivider = divImg.rectTransform;

            _cosmeticTargetCaption = QdaoUguiFactory.CreateText("TargetCaption", _cosmeticDetailPanel,
                0f, 176f, 180f, 44f, "当前灵宠", 26f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _cosmeticTargetCaption.richText = false;

            _cosmeticTargetValue = QdaoUguiFactory.CreateText("TargetValue", _cosmeticDetailPanel,
                190f, 176f, 320f, 44f, "—", 28f, AppearanceUiArt.JadeInk, TextAlignmentOptions.MidlineLeft);
            _cosmeticTargetValue.richText = false;

            _cosmeticTargetChangeBtn = QdaoUguiFactory.CreateHitButton("TargetChangeBtn", _cosmeticDetailPanel,
                516f, 168f, 190f, 60f);
            _cosmeticTargetChangeBtn.onClick.AddListener(HandleCosmeticTargetChangeClicked);
            _cosmeticTargetChangeLabel = QdaoUguiFactory.CreateText("Label", _cosmeticTargetChangeBtn.transform,
                0f, 0f, 190f, 60f, "更换灵宠 >", 22f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineRight);
            _cosmeticTargetChangeLabel.richText = false;

            _cosmeticScopeCaption = QdaoUguiFactory.CreateText("ScopeCaption", _cosmeticDetailPanel,
                0f, 248f, 180f, 44f, "生效范围", 26f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _cosmeticScopeCaption.richText = false;

            _cosmeticScopeValue = QdaoUguiFactory.CreateText("ScopeValue", _cosmeticDetailPanel,
                190f, 248f, 520f, 44f, "仅应用于当前灵宠", 26f, AppearanceUiArt.JadeInk, TextAlignmentOptions.MidlineLeft);
            _cosmeticScopeValue.richText = false;

            _cosmeticDescText = QdaoUguiFactory.CreateText("DetailDesc", _cosmeticDetailPanel,
                0f, 330f, 700f, 128f, "", 23f, AppearanceUiArt.MutedInk, TextAlignmentOptions.TopLeft);
            _cosmeticDescText.richText = false;
            _cosmeticDescText.textWrappingMode = TextWrappingModes.Normal;
            _cosmeticDescText.lineSpacing = 6f;

            _cosmeticSecondaryBtn = CreateCosmeticTextButton(_cosmeticDetailPanel, "CosmeticSecondaryBtn", "对比原形",
                0f, 504f, 260f, 76f, HandleCosmeticSecondaryClicked, primary: false, fontSize: 26f);
            _cosmeticSecondaryLabel = _cosmeticSecondaryBtn.GetComponentInChildren<TMP_Text>(true);

            _cosmeticPrimaryBtn = CreateCosmeticTextButton(_cosmeticDetailPanel, "CosmeticPrimaryBtn", "穿戴幻化",
                286f, 498f, 354f, 86f, HandleCosmeticPrimaryClicked, primary: true, fontSize: 28f);
            _cosmeticPrimaryLabel = _cosmeticPrimaryBtn.GetComponentInChildren<TMP_Text>(true);

            _cosmeticStatusText = QdaoUguiFactory.CreateText("CosmeticStatus", _cosmeticDetailPanel,
                0f, 598f, 700f, 56f, "", 21f, AppearanceUiArt.MutedInk, TextAlignmentOptions.TopLeft);
            _cosmeticStatusText.richText = false;
            _cosmeticStatusText.textWrappingMode = TextWrappingModes.Normal;

            _cosmeticsRoot.gameObject.SetActive(false);
        }

        private static Button CreateCosmeticTextButton(
            UnityEngine.Transform parent,
            string name,
            string label,
            float x,
            float y,
            float w,
            float h,
            Action onClick,
            bool primary,
            float fontSize)
        {
            var sprite = AppearanceUiArt.LoadWardrobeCosmetic(primary ? "button-primary" : "button-secondary")
                ?? AppearanceUiArt.LoadGameplay(primary ? "button_primary" : "button_secondary");
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, sprite, out var img);
            if (sprite != null)
            {
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = Color.white;
            }
            else
            {
                img.color = primary ? AppearanceUiArt.DeepJade : AppearanceUiArt.ParchmentInner;
            }
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var txt = QdaoUguiFactory.CreateText("Label", btn.transform, 14f, 0f, w - 28f, h,
                label, fontSize, primary ? AppearanceUiArt.WarmCream : AppearanceUiArt.JadeInk,
                TextAlignmentOptions.Center);
            txt.richText = false;
            txt.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
            txt.enableAutoSizing = true;
            txt.fontSizeMin = Mathf.Max(15f, fontSize - 8f);
            txt.fontSizeMax = fontSize;
            return btn;
        }

        public void SelectWardrobeCategory(WardrobeCategoryTab category)
        {
            if (_state.HasOverlay) return;
            float scrollPos = _cosmeticListScroll != null && _cosmeticListScroll.gameObject.activeInHierarchy
                ? _cosmeticListScroll.verticalNormalizedPosition : 1f;
            _cosmeticState.SelectCategory(category, scrollPos);
            if (category != WardrobeCategoryTab.SkillEffect) StopVfxPreview();
            if (category == WardrobeCategoryTab.Pet)
                PetClient.Instance?.RequestList();
            if (category != WardrobeCategoryTab.Character && !_externalVerificationClient)
                JubaozhaiUiRoot.Instance?.RequestOfficialSkusRefresh();
            MarkReadingLayoutDirty();
            Render();
        }

        private void RenderWardrobeCategoryShell()
        {
            var cat = _cosmeticState.ActiveCategory;
            for (int i = 0; i < 3; i++)
            {
                bool active = (int)cat == i;
                if (_categoryTabImages[i] != null)
                {
                    var sprite = AppearanceUiArt.LoadWardrobeCosmetic(active ? "tab-selected" : "tab-default");
                    _categoryTabImages[i].sprite = sprite;
                    _categoryTabImages[i].color = sprite != null
                        ? Color.white
                        : (active ? AppearanceUiArt.DeepJade : AppearanceUiArt.ParchmentInner);
                }
                if (_categoryTabLabels[i] != null)
                {
                    _categoryTabLabels[i].color = active ? AppearanceUiArt.WarmCream : AppearanceUiArt.JadeInk;
                    _categoryTabLabels[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                }
            }

            if (_windowMainTitleText != null)
                _windowMainTitleText.text = cat == WardrobeCategoryTab.Character ? "易 容" : "衣 柜";
            if (_windowSubTitleText != null)
            {
                _windowSubTitleText.text = cat switch
                {
                    WardrobeCategoryTab.Pet => "灵宠幻化",
                    WardrobeCategoryTab.SkillEffect => "技能特效",
                    _ => "形象与人物志",
                };
            }

            if (_cosmeticsRoot != null)
                _cosmeticsRoot.gameObject.SetActive(cat != WardrobeCategoryTab.Character);
        }

        private void RenderCosmeticTab(PlayerAppearanceInfo info)
        {
            var cat = _cosmeticState.ActiveCategory;
            if (cat == WardrobeCategoryTab.Character || _cosmeticsRoot == null) return;

            bool isPet = cat == WardrobeCategoryTab.Pet;
            _cosmeticSectionTitle.text = isPet ? "幻化外观" : "技能特效";

            Func<ulong, bool> isSellableSku = skuId =>
                _sellableMallSkuOverride != null
                    ? _sellableMallSkuOverride(skuId)
                    : (JubaozhaiUiRoot.Instance?.HasSellableOfficialSku(skuId) == true);

            if (info == null)
            {
                ClearCosmeticRows();
                _currentCosmeticRowViews = new List<WardrobeCosmeticRowView>();
                _cosmeticEmptyLabel.text = _client?.InfoPending == true ? "正在同步衣柜外观…" : "衣柜外观同步失败";
                _cosmeticEmptyLabel.gameObject.SetActive(true);
                _cosmeticRetryBtn.gameObject.SetActive(_client?.InfoPending != true);
                _cosmeticRetryBtn.interactable = _client?.Ready == true;
                _cosmeticDetailTitle.text = isPet ? "灵宠幻化" : "技能特效";
                _cosmeticDetailState.text = "";
                _cosmeticDescText.text = _client?.Status ?? "";
                _cosmeticPreviewImage.enabled = false;
                _cosmeticPreviewMissing.gameObject.SetActive(true);
                _cosmeticPreviewMissing.text = "等待同步外观数据";
                _cosmeticPrimaryBtn.interactable = false;
                _cosmeticSecondaryBtn.interactable = false;
                _cosmeticStatusText.text = _client?.Status ?? "";
                StopVfxPreview();
                return;
            }

            WardrobePetTargetOption selectedPet = null;
            WardrobeSkillTargetOption selectedSkill = null;
            if (isPet)
            {
                RebuildPetTargets(info);
                selectedPet = _cosmeticState.SyncPetTarget(_petTargetBuffer, PetClient.Instance?.Pets?.ActivePetId ?? 0ul);
                _currentCosmeticRowViews = WardrobeCosmeticModels.BuildPetRows(info, selectedPet, isSellableSku);
            }
            else
            {
                RebuildSkillTargets(info);
                _currentCosmeticRowViews = WardrobeCosmeticModels.BuildSkillEffectRows(info, _cosmeticState.FilterSkillTableId, isSellableSku);
            }

            var selectedRow = _cosmeticState.SyncSelectedRow(cat, _currentCosmeticRowViews);
            if (!isPet && selectedRow != null)
            {
                selectedSkill = FindSkillTarget(selectedRow.SkillTableId);
            }

            bool emptyCatalog = _currentCosmeticRowViews.Count == 0;
            _cosmeticEmptyLabel.gameObject.SetActive(emptyCatalog);
            _cosmeticRetryBtn.gameObject.SetActive(emptyCatalog);
            if (emptyCatalog)
            {
                _cosmeticEmptyLabel.text = isPet ? "当前暂无可展示灵宠幻化" : "当前暂无可展示技能特效";
            }

            SyncCosmeticListRows(_currentCosmeticRowViews, selectedRow?.ConfigId ?? 0u);

            if (selectedRow == null)
            {
                _cosmeticDetailTitle.text = isPet ? "暂无灵宠幻化" : "暂无技能特效";
                _cosmeticDetailState.text = "";
                _cosmeticDescText.text = "";
                _cosmeticPreviewImage.enabled = false;
                _cosmeticPreviewMissing.gameObject.SetActive(true);
                _cosmeticPreviewMissing.text = "暂无预览项";
                _cosmeticPrimaryBtn.interactable = false;
                _cosmeticSecondaryBtn.interactable = false;
                StopVfxPreview();
            }
            else if (isPet)
            {
                StopVfxPreview();
                RenderPetCosmeticDetails(selectedRow, selectedPet);
            }
            else
            {
                RenderSkillVfxDetails(selectedRow, selectedSkill);
            }

            if (_cosmeticStatusText != null)
            {
                _cosmeticStatusText.text = _client?.Status ?? "";
                _cosmeticStatusText.color = (_client?.StatusIsError == true)
                    ? AppearanceUiArt.CrimsonInk : AppearanceUiArt.MutedInk;
            }
        }

        private void RenderPetCosmeticDetails(WardrobeCosmeticRowView selected, WardrobePetTargetOption targetPet)
        {
            _cosmeticDetailTitle.text = selected.Name;
            _cosmeticDetailState.text = selected.StateLabel;
            _cosmeticDetailState.color = selected.State == WardrobeCosmeticRowState.Equipped
                ? AppearanceUiArt.TrialGreen
                : (selected.IsOwned ? AppearanceUiArt.JadeInk : AppearanceUiArt.CrimsonInk);

            _cosmeticTargetCaption.text = "当前灵宠";
            _cosmeticTargetValue.text = targetPet != null ? targetPet.Name : "暂无灵宠";
            bool canSwitchPet = _petTargetBuffer.Count > 1;
            _cosmeticTargetChangeBtn.gameObject.SetActive(canSwitchPet);
            _cosmeticTargetChangeLabel.text = "更换灵宠 >";

            _cosmeticScopeCaption.text = "生效范围";
            _cosmeticScopeValue.text = selected.TargetPetTableId == 0u ? "仅应用于当前灵宠" : "仅应用于指定种类当前灵宠";

            bool canSubmit = _client != null && _client.Ready && !_client.RequiresReconnect && _client.IsCosmeticsSupported;
            bool busy = _client != null && _client.ActionPending;
            var primary = WardrobeCosmeticModels.ResolvePetPrimaryAction(selected, targetPet, canSubmit, busy, _corePreviewOnly);

            string desc = selected.Description;
            if (!string.IsNullOrEmpty(primary.DetailHint) && !desc.Contains(primary.DetailHint))
                desc = string.IsNullOrEmpty(desc) ? primary.DetailHint : desc + "\n" + primary.DetailHint;
            _cosmeticDescText.text = desc;

            bool showOriginal = _cosmeticState.CompareOriginalPet && !selected.IsBaseDefault && targetPet != null;
            Sprite previewSprite = showOriginal
                ? LoadPetArtPortrait(targetPet.BaseArtKey, targetPet.PetTableId, targetPet.ModelId)
                : LoadPetArtPortrait(selected.VisualArtKey, selected.VisualPetTableId, 0u);
            _cosmeticPreviewImage.sprite = previewSprite;
            _cosmeticPreviewImage.color = Color.white;
            _cosmeticPreviewImage.enabled = previewSprite != null;
            _cosmeticPreviewMissing.gameObject.SetActive(previewSprite == null);
            if (previewSprite == null) _cosmeticPreviewMissing.text = $"{selected.Name}\n灵宠外观预览资源暂不可用";
            _cosmeticPreviewBadge.gameObject.SetActive(showOriginal);
            _cosmeticPreviewBadge.text = showOriginal ? "原形对比中" : "";

            bool canCompare = targetPet != null && !selected.IsBaseDefault;
            _cosmeticSecondaryBtn.interactable = canCompare;
            _cosmeticSecondaryLabel.text = showOriginal ? "查看幻化" : "对比原形";

            _cosmeticPrimaryBtn.interactable = primary.Interactable;
            _cosmeticPrimaryLabel.text = primary.Label;
        }

        private void RenderSkillVfxDetails(WardrobeCosmeticRowView selected, WardrobeSkillTargetOption targetSkill)
        {
            _cosmeticDetailTitle.text = selected.Name;
            _cosmeticDetailState.text = selected.StateLabel;
            _cosmeticDetailState.color = selected.State == WardrobeCosmeticRowState.Equipped
                ? AppearanceUiArt.TrialGreen
                : (selected.IsOwned ? AppearanceUiArt.JadeInk : AppearanceUiArt.CrimsonInk);

            _cosmeticTargetCaption.text = "适用技能";
            _cosmeticTargetValue.text = targetSkill != null ? targetSkill.Name : ResolveSkillDisplayName(selected.SkillTableId);
            bool canSwitchSkill = _skillTargetBuffer.Count > 1;
            _cosmeticTargetChangeBtn.gameObject.SetActive(canSwitchSkill);
            _cosmeticTargetChangeLabel.text = _cosmeticState.FilterSkillTableId == 0u ? "筛选技能 >" : "全部技能 >";

            _cosmeticScopeCaption.text = "生效范围";
            _cosmeticScopeValue.text = "仅应用于对应技能";

            bool canSubmit = _client != null && _client.Ready && !_client.RequiresReconnect && _client.IsCosmeticsSupported;
            bool busy = _client != null && _client.ActionPending;
            var primary = WardrobeCosmeticModels.ResolveSkillPrimaryAction(selected, targetSkill, canSubmit, busy, _corePreviewOnly);

            string desc = selected.Description;
            if (!string.IsNullOrEmpty(primary.DetailHint) && !desc.Contains(primary.DetailHint))
                desc = string.IsNullOrEmpty(desc) ? primary.DetailHint : desc + "\n" + primary.DetailHint;
            _cosmeticDescText.text = desc;

            _cosmeticPreviewBadge.gameObject.SetActive(false);

            string fxId = !string.IsNullOrEmpty(selected.FxId)
                ? selected.FxId
                : BattleArtCatalog.FxForSkill(selected.SkillTableId, isAttack: false);
            string previewKey = $"{selected.ConfigId}:{fxId}:{selected.TintHex}";
            if (_activeVfxPreviewKey != previewKey)
            {
                StartVfxPreview(previewKey, fxId, selected.TintHex);
            }

            _cosmeticSecondaryBtn.interactable = _activeVfxStrip != null && _activeVfxStrip.Count > 0;
            _cosmeticSecondaryLabel.text = "重播特效";

            _cosmeticPrimaryBtn.interactable = primary.Interactable;
            _cosmeticPrimaryLabel.text = primary.Label;
        }

        private void StartVfxPreview(string previewKey, string fxId, string tintHex)
        {
            StopVfxPreview();
            _activeVfxPreviewKey = previewKey;
            _activeVfxStrip = BattleArtCatalog.LoadFx(fxId);
            _activeVfxTint = WardrobeCosmeticModels.TryParseTintHex(tintHex, out float r, out float g, out float b)
                ? new Color(r, g, b, 1f) : Color.white;
            if (_activeVfxStrip == null || _activeVfxStrip.Count == 0)
            {
                _cosmeticPreviewImage.sprite = null;
                _cosmeticPreviewImage.enabled = false;
                _cosmeticPreviewMissing.gameObject.SetActive(true);
                _cosmeticPreviewMissing.text = "技能特效预览帧暂不可用";
                return;
            }
            _activeVfxElapsed = 0f;
            _activeVfxPlaying = true;
            _cosmeticPreviewMissing.gameObject.SetActive(false);
            _cosmeticPreviewImage.sprite = _activeVfxStrip.Frames[0];
            _cosmeticPreviewImage.color = _activeVfxTint;
            _cosmeticPreviewImage.enabled = true;
        }

        private void ReplayVfxPreview()
        {
            if (_activeVfxStrip == null || _activeVfxStrip.Count == 0) return;
            _activeVfxElapsed = 0f;
            _activeVfxPlaying = true;
            _cosmeticPreviewImage.sprite = _activeVfxStrip.Frames[0];
            _cosmeticPreviewImage.color = _activeVfxTint;
            _cosmeticPreviewImage.enabled = true;
        }

        private void StopVfxPreview()
        {
            _activeVfxPlaying = false;
            _activeVfxElapsed = 0f;
            _activeVfxStrip = null;
            _activeVfxPreviewKey = "";
            _activeVfxTint = Color.white;
        }

        private void TickWardrobeVfxPreview()
        {
            if (!_activeVfxPlaying || _activeVfxStrip == null || _activeVfxStrip.Count == 0 || _cosmeticPreviewImage == null)
                return;
            if (!IsPanelVisible || _cosmeticState.ActiveCategory != WardrobeCategoryTab.SkillEffect)
            {
                StopVfxPreview();
                return;
            }
            float fps = _activeVfxStrip.Fps > 0f ? _activeVfxStrip.Fps : BattleFxPlayer.DefaultFps;
            float duration = _activeVfxStrip.Count / fps;
            _activeVfxElapsed += Time.unscaledDeltaTime;
            if (_activeVfxElapsed >= duration)
            {
                _activeVfxPlaying = false;
                _cosmeticPreviewImage.sprite = _activeVfxStrip.Frames[_activeVfxStrip.Count - 1];
                return;
            }
            int frame = Mathf.Clamp(Mathf.FloorToInt(_activeVfxElapsed * fps), 0, _activeVfxStrip.Count - 1);
            _cosmeticPreviewImage.sprite = _activeVfxStrip.Frames[frame];
            _cosmeticPreviewImage.color = _activeVfxTint;
        }

        private void SyncCosmeticListRows(IReadOnlyList<WardrobeCosmeticRowView> rows, uint selectedConfigId)
        {
            if (_cosmeticListContent == null) return;
            var sigSb = new System.Text.StringBuilder(rows.Count * 16);
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sigSb.Append('|');
                sigSb.Append((int)rows[i].Category).Append(':').Append(rows[i].ConfigId).Append(':').Append(rows[i].Name);
            }
            string sig = sigSb.ToString();
            if (sig != _cosmeticListSignature)
            {
                RebuildCosmeticRowObjects(rows);
                _cosmeticListSignature = sig;
                if (_cosmeticListScroll != null)
                {
                    _cosmeticListScroll.velocity = Vector2.zero;
                    _cosmeticListScroll.verticalNormalizedPosition = _cosmeticState.SavedScroll(_cosmeticState.ActiveCategory);
                }
            }

            var normalSprite = AppearanceUiArt.LoadWardrobeCosmetic("row-default");
            var selectedSprite = AppearanceUiArt.LoadWardrobeCosmetic("row-selected");
            for (int i = 0; i < _cosmeticRows.Count && i < rows.Count; i++)
            {
                var view = _cosmeticRows[i];
                var item = rows[i];
                bool selected = item.ConfigId == selectedConfigId;
                if (view.Background != null)
                {
                    var sp = selected ? selectedSprite : normalSprite;
                    view.Background.sprite = sp;
                    view.Background.color = sp != null
                        ? Color.white
                        : (selected ? QdaoUguiTheme.Html("#D4E8CF") : AppearanceUiArt.ParchmentCard);
                }
                view.NameText.text = item.Name;
                view.NameText.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                view.StateText.text = item.StateLabel;
                view.StateText.color = item.State == WardrobeCosmeticRowState.Equipped
                    ? AppearanceUiArt.TrialGreen
                    : (item.IsOwned ? AppearanceUiArt.MutedInk : AppearanceUiArt.CrimsonInk);

                Sprite icon = item.Category == WardrobeCategoryTab.Pet
                    ? LoadPetArtPortrait(item.VisualArtKey, item.VisualPetTableId, 0u)
                    : LoadSkillCosmeticIcon(item);
                view.IconImage.sprite = icon;
                view.IconImage.enabled = icon != null;
                view.IconMissingText.gameObject.SetActive(icon == null);
            }
        }

        private void RebuildCosmeticRowObjects(IReadOnlyList<WardrobeCosmeticRowView> rows)
        {
            ClearCosmeticRows();
            var ringSprite = AppearanceUiArt.LoadWardrobeCosmetic("portrait-ring");
            var rowSprite = AppearanceUiArt.LoadWardrobeCosmetic("row-default");
            const float rowW = 496f;
            const float rowH = 96f;
            const float gap = 12f;
            float y = 6f;

            for (int i = 0; i < rows.Count; i++)
            {
                var item = rows[i];
                uint capturedId = item.ConfigId;
                var capturedCategory = item.Category;

                var btn = QdaoUguiFactory.CreateArtButton($"CosmeticRow_{capturedCategory}_{capturedId}",
                    _cosmeticListContent, 4f, y, rowW, rowH, rowSprite, out var bg);
                bg.type = Image.Type.Simple;
                bg.preserveAspect = false;
                btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                btn.onClick.AddListener(() =>
                {
                    if (_state.HasOverlay) return;
                    float scroll = _cosmeticListScroll != null ? _cosmeticListScroll.verticalNormalizedPosition : 1f;
                    _cosmeticState.SelectRow(capturedCategory, capturedId, scroll, openCompactDetail: _compactLayout);
                    Render();
                });

                var ring = AppearanceUiArt.FixedArt(btn.transform, "PortraitRing", ringSprite,
                    16f, 11f, 74f, 74f, AppearanceUiArt.PortraitPlaceholderBg, preserveAspect: true);
                var icon = QdaoUguiFactory.CreateImage("Icon", ring.transform, 7f, 7f, 60f, 60f, null);
                icon.preserveAspect = true;
                icon.enabled = false;
                var miss = QdaoUguiFactory.CreateText("Missing", ring.transform, 4f, 20f, 66f, 34f,
                    "缺图", 16f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
                miss.richText = false;

                var nameLbl = QdaoUguiFactory.CreateText("Name", btn.transform,
                    108f, 0f, 236f, rowH, item.Name, 26f, AppearanceUiArt.JadeInk, TextAlignmentOptions.MidlineLeft);
                nameLbl.richText = false;

                var stateLbl = QdaoUguiFactory.CreateText("State", btn.transform,
                    348f, 0f, 132f, rowH, item.StateLabel, 21f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineRight);
                stateLbl.richText = false;

                _cosmeticRows.Add(new CosmeticRowView
                {
                    ConfigId = capturedId,
                    Root = btn.gameObject,
                    Button = btn,
                    Background = bg,
                    IconImage = icon,
                    IconMissingText = miss,
                    NameText = nameLbl,
                    StateText = stateLbl,
                });
                y += rowH + gap;
            }
            _cosmeticListContent.sizeDelta = new Vector2(510f, Mathf.Max(610f, y + 12f));
        }

        private void ClearCosmeticRows()
        {
            for (int i = 0; i < _cosmeticRows.Count; i++)
            {
                if (_cosmeticRows[i].Root == null) continue;
                if (Application.isPlaying) Destroy(_cosmeticRows[i].Root);
                else DestroyImmediate(_cosmeticRows[i].Root);
            }
            _cosmeticRows.Clear();
            _cosmeticListSignature = "";
        }

        private void HandleCosmeticTargetChangeClicked()
        {
            if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.Pet)
            {
                _cosmeticState.CyclePetTarget(_petTargetBuffer);
                Render();
            }
            else if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.SkillEffect)
            {
                if (_cosmeticState.FilterSkillTableId != 0u)
                {
                    _cosmeticState.SetFilterSkillTableId(0u);
                }
                else if (_skillTargetBuffer.Count > 0)
                {
                    var selected = FindCurrentSelectedCosmeticRow();
                    uint nextSkill = selected != null ? selected.SkillTableId : _skillTargetBuffer[0].SkillTableId;
                    _cosmeticState.SetFilterSkillTableId(nextSkill);
                }
                Render();
            }
        }

        private void HandleCosmeticSecondaryClicked()
        {
            if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.Pet)
            {
                _cosmeticState.ToggleCompareOriginalPet();
                Render();
            }
            else if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.SkillEffect)
            {
                ReplayVfxPreview();
            }
        }

        private void HandleCosmeticPrimaryClicked()
        {
            if (_corePreviewOnly || _client == null || _client.Info == null) return;
            var selected = FindCurrentSelectedCosmeticRow();
            if (selected == null) return;

            if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.Pet)
            {
                var targetPet = _cosmeticState.SyncPetTarget(_petTargetBuffer, PetClient.Instance?.Pets?.ActivePetId ?? 0ul);
                bool canSubmit = _client.Ready && !_client.RequiresReconnect && _client.IsCosmeticsSupported;
                var action = WardrobeCosmeticModels.ResolvePetPrimaryAction(selected, targetPet, canSubmit, _client.ActionPending, _corePreviewOnly);
                if (!action.Interactable) return;
                if (action.Kind == WardrobePrimaryActionKind.OpenPermanentWardrobeMall)
                {
                    HidePanel();
                    JubaozhaiUiRoot.Instance?.OpenOfficialMallSubTab(OfficialShopTab.PermanentWardrobe);
                }
                else if (action.Kind == WardrobePrimaryActionKind.RestoreDefault && targetPet != null)
                {
                    _client.EquipPetCosmetic(targetPet.PetId, 0u);
                }
                else if (action.Kind == WardrobePrimaryActionKind.EquipCosmetic && targetPet != null)
                {
                    _client.EquipPetCosmetic(targetPet.PetId, selected.ConfigId);
                }
            }
            else if (_cosmeticState.ActiveCategory == WardrobeCategoryTab.SkillEffect)
            {
                var targetSkill = FindSkillTarget(selected.SkillTableId);
                bool canSubmit = _client.Ready && !_client.RequiresReconnect && _client.IsCosmeticsSupported;
                var action = WardrobeCosmeticModels.ResolveSkillPrimaryAction(selected, targetSkill, canSubmit, _client.ActionPending, _corePreviewOnly);
                if (!action.Interactable) return;
                if (action.Kind == WardrobePrimaryActionKind.OpenPermanentWardrobeMall)
                {
                    HidePanel();
                    JubaozhaiUiRoot.Instance?.OpenOfficialMallSubTab(OfficialShopTab.PermanentWardrobe);
                }
                else if (action.Kind == WardrobePrimaryActionKind.RestoreDefault)
                {
                    _client.EquipSkillVfx(selected.SkillTableId, 0u);
                }
                else if (action.Kind == WardrobePrimaryActionKind.EquipCosmetic)
                {
                    _client.EquipSkillVfx(selected.SkillTableId, selected.ConfigId);
                }
            }
        }

        private WardrobeCosmeticRowView FindCurrentSelectedCosmeticRow()
        {
            uint targetId = _cosmeticState.ActiveCategory == WardrobeCategoryTab.Pet
                ? _cosmeticState.SelectedPetCosmeticId : _cosmeticState.SelectedSkillVfxId;
            for (int i = 0; i < _currentCosmeticRowViews.Count; i++)
                if (_currentCosmeticRowViews[i].ConfigId == targetId) return _currentCosmeticRowViews[i];
            return null;
        }

        private void RebuildPetTargets(PlayerAppearanceInfo info)
        {
            _petTargetBuffer.Clear();
            var petList = PetClient.Instance?.Pets;
            if (petList != null && petList.Pets != null && petList.Pets.Count > 0)
            {
                for (int i = 0; i < petList.Pets.Count; i++)
                {
                    var p = petList.Pets[i];
                    if (p == null) continue;
                    string baseArt = QdaoPetCatalog.Resolve(p.PetTableId, p.ModelId)?.id ?? "";
                    uint equipped = WardrobeCosmeticModels.FindEquippedPetCosmeticId(info, p.PetId);
                    if (equipped == 0u) equipped = p.CosmeticId;
                    _petTargetBuffer.Add(new WardrobePetTargetOption(p.PetId, p.PetTableId, p.ModelId, p.Name, baseArt, equipped));
                }
                return;
            }
            // 若灵宠列表尚未拉取但衣柜快照已含灵宠穿戴映射，保留最小目标项避免页面空白。
            if (info?.EquippedPetCosmetics != null)
            {
                for (int i = 0; i < info.EquippedPetCosmetics.Count; i++)
                {
                    var eq = info.EquippedPetCosmetics[i];
                    if (eq == null || eq.PetId == 0ul) continue;
                    _petTargetBuffer.Add(new WardrobePetTargetOption(eq.PetId, 0u, 0u, $"灵宠 #{eq.PetId}", "", eq.CosmeticId));
                }
            }
        }

        private void RebuildSkillTargets(PlayerAppearanceInfo info)
        {
            _skillTargetBuffer.Clear();
            var seen = new HashSet<uint>();
            var skillSnap = SkillBookClient.Instance?.State?.Snapshot;
            if (info?.SkillVfxCosmetics != null)
            {
                for (int i = 0; i < info.SkillVfxCosmetics.Count; i++)
                {
                    uint skillId = info.SkillVfxCosmetics[i].SkillTableId;
                    if (skillId == 0u || !seen.Add(skillId)) continue;
                    string name = ResolveSkillDisplayName(skillId);
                    string iconKey = $"UI/Skills/{skillId}";
                    bool usable = true;
                    if (skillSnap != null && skillSnap.Entries != null)
                    {
                        usable = false;
                        for (int j = 0; j < skillSnap.Entries.Count; j++)
                        {
                            var e = skillSnap.Entries[j];
                            if (e != null && e.Id == skillId)
                            {
                                usable = e.Rank > 0u || e.SeasonId != 0u;
                                break;
                            }
                        }
                    }
                    uint eq = WardrobeCosmeticModels.FindEquippedSkillVfxId(info, skillId);
                    _skillTargetBuffer.Add(new WardrobeSkillTargetOption(skillId, name, iconKey, usable, eq));
                }
            }
        }

        private WardrobeSkillTargetOption FindSkillTarget(uint skillTableId)
        {
            for (int i = 0; i < _skillTargetBuffer.Count; i++)
                if (_skillTargetBuffer[i].SkillTableId == skillTableId) return _skillTargetBuffer[i];
            return null;
        }

        private static string ResolveSkillDisplayName(uint skillTableId)
        {
            if (skillTableId == 0u) return "—";
            var row = SkillTableManager.Instance.FindById(skillTableId);
            if (row != null && !string.IsNullOrEmpty(row.Name)) return row.Name;
            return $"技能 #{skillTableId}";
        }

        private static Sprite LoadPetArtPortrait(string artKey, uint petTableId, uint modelId)
        {
            if (!string.IsNullOrEmpty(artKey))
            {
                var byKey = QdaoPetCatalog.LoadPortrait(artKey);
                if (byKey != null) return byKey;
            }
            if (petTableId != 0u || modelId != 0u)
            {
                string resolved = QdaoPetCatalog.Resolve(petTableId, modelId)?.id;
                if (!string.IsNullOrEmpty(resolved)) return QdaoPetCatalog.LoadPortrait(resolved);
            }
            return null;
        }

        private static Sprite LoadSkillCosmeticIcon(WardrobeCosmeticRowView item)
        {
            if (item == null) return null;
            if (!string.IsNullOrEmpty(item.FxId))
            {
                var strip = BattleArtCatalog.LoadFx(item.FxId);
                if (strip != null && strip.Count > 0) return strip.Frames[Mathf.Min(3, strip.Count - 1)];
            }
            if (item.SkillTableId != 0u)
            {
                var petArtSkill = CharacterPetArt.Skill(item.SkillTableId);
                if (petArtSkill != null) return petArtSkill;
                var skillIcon = Resources.Load<Sprite>($"UI/Skills/{item.SkillTableId}");
                if (skillIcon != null) return skillIcon;
            }
            return null;
        }

        private void ApplyWardrobeCosmeticsLayout(bool compact)
        {
            if (_categoryTabBar == null) return;
            var cat = _cosmeticState.ActiveCategory;
            bool inCosmetic = cat != WardrobeCategoryTab.Character;

            // 顶部三类别页签定位：宽屏避开居中标题牌；窄屏人物详情子页收起页签避免与返回列表/角色名重叠，列表页置于卡片上方
            bool showCategoryTabs = !compact || inCosmetic || !_compactDetail;
            _categoryTabBar.gameObject.SetActive(showCategoryTabs);
            if (compact)
            {
                float tabBarX = inCosmetic ? 66f : 56f;
                float tabBarY = inCosmetic ? 74f : 68f;
                float tabBarH = inCosmetic ? 47f : 40f;
                PlaceTopLeft(_categoryTabBar, tabBarX, tabBarY, 470f, tabBarH);
                for (int i = 0; i < 3; i++)
                {
                    if (_categoryTabButtons[i] != null)
                        PlaceTopLeft((RectTransform)_categoryTabButtons[i].transform, i * 158f, 0f, 145f, tabBarH);
                    if (_categoryTabLabels[i] != null) _categoryTabLabels[i].fontSize = 20f;
                }
            }
            else
            {
                PlaceTopLeft(_categoryTabBar, 68f, 24f, 640f, 56f);
                for (int i = 0; i < 3; i++)
                {
                    if (_categoryTabButtons[i] != null)
                        PlaceTopLeft((RectTransform)_categoryTabButtons[i].transform, i * 211f, 0f, 198f, 56f);
                    if (_categoryTabLabels[i] != null) _categoryTabLabels[i].fontSize = 24f;
                }
            }

            if (!inCosmetic)
            {
                if (_cosmeticsRoot != null) _cosmeticsRoot.gameObject.SetActive(false);
                return;
            }

            // 隐藏所有人物页元素，避免与灵宠/特效页重叠
            var main = _mainPanelGroup.transform;
            foreach (string name in _listElementNames) SetReadingElementVisible(main, name, false);
            foreach (string name in _detailElementNames) SetReadingElementVisible(main, name, false);
            SetReadingElementVisible(main, "AppearanceBackToList", false);
            SetReadingElementVisible(main, "AppearanceRuleBanner", false);
            if (_biographySection != null) _biographySection.SetActive(false);
            if (_previewMissingBox != null) _previewMissingBox.SetActive(false);
            if (_trialBannerRoot != null) _trialBannerRoot.SetActive(false);
            if (_cancelTrialBtn != null) _cancelTrialBtn.gameObject.SetActive(false);
            if (_compactListStatus != null) _compactListStatus.gameObject.SetActive(false);
            if (_compactListRetry != null) _compactListRetry.gameObject.SetActive(false);
            if (_seriesSummaryRoot != null) _seriesSummaryRoot.SetActive(false);
            if (_seriesTabBarRoot != null) _seriesTabBarRoot.SetActive(false);
            if (_seriesBannerRoot != null) _seriesBannerRoot.SetActive(false);

            if (_cosmeticsRoot == null) return;
            _cosmeticsRoot.gameObject.SetActive(true);

            bool showList = !compact || !_cosmeticState.CompactDetailOpen;
            bool showDetail = !compact || _cosmeticState.CompactDetailOpen;
            _cosmeticCompactBackBtn.gameObject.SetActive(compact && _cosmeticState.CompactDetailOpen);
            _cosmeticSectionHeader.gameObject.SetActive(showList);
            _cosmeticListViewport.gameObject.SetActive(showList);
            if (_cosmeticListScrollbarObj != null) _cosmeticListScrollbarObj.SetActive(showList);
            _cosmeticPreviewPanel.gameObject.SetActive(showDetail);
            _cosmeticDetailPanel.gameObject.SetActive(showDetail);

            if (compact)
            {
                PlaceTopLeft(_cosmeticsRoot, 0f, 0f, 1180f, 596f);
                PlaceTopLeft((RectTransform)_cosmeticCompactBackBtn.transform, 847f, 68f, 190f, 52f);
                PlaceTopLeft(_cosmeticSectionHeader.rectTransform, 66f, 134f, 420f, 52f);
                PlaceTopLeft(_cosmeticListViewport, 66f, 196f, 1020f, 368f);
                if (_cosmeticListScrollbarObj != null)
                    PlaceTopLeft((RectTransform)_cosmeticListScrollbarObj.transform, 1096f, 196f, 12f, 368f);
                PlaceTopLeft(_cosmeticPreviewPanel, 63f, 155f, 344f, 354f);
                PlaceTopLeft(_cosmeticPreviewClip, 16f, 16f, 312f, 322f);
                PlaceTopLeft(_cosmeticPreviewImage.rectTransform, 12f, 12f, 288f, 298f);
                PlaceTopLeft(_cosmeticDetailPanel, 449f, 156f, 584f, 380f);
                PlaceTopLeft(_cosmeticDetailTitle.rectTransform, 0f, 4f, 570f, 46f);
                PlaceTopLeft(_cosmeticDetailState.rectTransform, 0f, 54f, 570f, 32f);
                PlaceTopLeft(_cosmeticDetailDivider, 0f, 92f, 570f, 2f);
                PlaceTopLeft(_cosmeticTargetCaption.rectTransform, 0f, 112f, 140f, 36f);
                PlaceTopLeft(_cosmeticTargetValue.rectTransform, 148f, 112f, 240f, 36f);
                PlaceTopLeft((RectTransform)_cosmeticTargetChangeBtn.transform, 404f, 102f, 166f, 54f);
                PlaceTopLeft(_cosmeticScopeCaption.rectTransform, 0f, 160f, 140f, 36f);
                PlaceTopLeft(_cosmeticScopeValue.rectTransform, 148f, 160f, 420f, 36f);
                PlaceTopLeft(_cosmeticDescText.rectTransform, 0f, 206f, 570f, 76f);
                PlaceTopLeft((RectTransform)_cosmeticSecondaryBtn.transform, 0f, 296f, 220f, 56f);
                PlaceTopLeft((RectTransform)_cosmeticPrimaryBtn.transform, 240f, 291f, 275f, 66f);
                PlaceTopLeft(_cosmeticStatusText.rectTransform, 0f, 360f, 570f, 36f);
            }
            else
            {
                PlaceTopLeft(_cosmeticsRoot, 0f, 0f, AppearanceUiArt.FrameWidth, AppearanceUiArt.MainBodyHeight);
                PlaceTopLeft(_cosmeticSectionHeader.rectTransform, 104f, 140f, 480f, 64f);
                PlaceTopLeft(_cosmeticListViewport, 104f, 224f, 510f, 610f);
                if (_cosmeticListScrollbarObj != null)
                    PlaceTopLeft((RectTransform)_cosmeticListScrollbarObj.transform, 622f, 224f, 12f, 610f);
                PlaceTopLeft(_cosmeticPreviewPanel, 690f, 134f, 560f, 650f);
                PlaceTopLeft(_cosmeticPreviewClip, 26f, 28f, 508f, 594f);
                PlaceTopLeft(_cosmeticPreviewImage.rectTransform, 24f, 24f, 460f, 500f);
                PlaceTopLeft(_cosmeticDetailPanel, 1310f, 140f, 740f, 650f);
                PlaceTopLeft(_cosmeticDetailTitle.rectTransform, 0f, 12f, 720f, 62f);
                PlaceTopLeft(_cosmeticDetailState.rectTransform, 0f, 82f, 720f, 38f);
                PlaceTopLeft(_cosmeticDetailDivider, 0f, 134f, 700f, 2f);
                PlaceTopLeft(_cosmeticTargetCaption.rectTransform, 0f, 176f, 180f, 44f);
                PlaceTopLeft(_cosmeticTargetValue.rectTransform, 190f, 176f, 320f, 44f);
                PlaceTopLeft((RectTransform)_cosmeticTargetChangeBtn.transform, 516f, 168f, 190f, 60f);
                PlaceTopLeft(_cosmeticScopeCaption.rectTransform, 0f, 248f, 180f, 44f);
                PlaceTopLeft(_cosmeticScopeValue.rectTransform, 190f, 248f, 520f, 44f);
                PlaceTopLeft(_cosmeticDescText.rectTransform, 0f, 330f, 700f, 128f);
                PlaceTopLeft((RectTransform)_cosmeticSecondaryBtn.transform, 0f, 504f, 260f, 76f);
                PlaceTopLeft((RectTransform)_cosmeticPrimaryBtn.transform, 286f, 498f, 354f, 86f);
                PlaceTopLeft(_cosmeticStatusText.rectTransform, 0f, 598f, 700f, 56f);
            }
            ApplyCosmeticRowLayout(compact);
        }

        private void ApplyCosmeticRowLayout(bool compact)
        {
            float rowW = compact ? 1004f : 496f;
            float contentW = compact ? 1020f : 510f;
            float nameW = compact ? 540f : 236f;
            float stateX = compact ? 660f : 348f;
            float stateW = compact ? 320f : 132f;
            for (int i = 0; i < _cosmeticRows.Count; i++)
            {
                var row = _cosmeticRows[i];
                if (row.Root == null) continue;
                var rt = (RectTransform)row.Root.transform;
                rt.sizeDelta = new Vector2(rowW, 96f);
                if (row.Background != null) row.Background.rectTransform.sizeDelta = rt.sizeDelta;
                if (row.NameText != null) PlaceTopLeft(row.NameText.rectTransform, 108f, 0f, nameW, 96f);
                if (row.StateText != null) PlaceTopLeft(row.StateText.rectTransform, stateX, 0f, stateW, 96f);
            }
            if (_cosmeticListContent != null)
                _cosmeticListContent.sizeDelta = new Vector2(contentW, _cosmeticListContent.sizeDelta.y);
        }
    }
}
