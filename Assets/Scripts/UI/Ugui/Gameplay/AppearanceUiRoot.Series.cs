using System;
using System.Collections.Generic;
using System.Text;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Table;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class AppearanceUiRoot
    {
        private const float SeriesEpilogueViewportWidth = 952f;
        private const float SeriesEpilogueViewportHeight = 444f;
        private const float SeriesTabHeight = 46f;
        private const float SeriesBadgeHeight = 34f;

        private sealed class SeriesTabView
        {
            public uint SeriesId;
            public GameObject Root;
            public Button Button;
            public Image Background;
            public TMP_Text Label;
        }

        private readonly List<SeriesTabView> _seriesTabs = new();
        private readonly List<uint> _validSeriesIdsBuffer = new();
        private readonly List<string> _filteredAppearanceIdsBuffer = new();
        private string _seriesTabSignature = "";
        private Func<uint, string> _seriesEpilogueBodyLookupOverride;

        // 左侧头部系列集齐统计 + 系列筛选栏 + 系列信息条
        private GameObject _seriesSummaryRoot;
        private TMP_Text _seriesSummaryLabel;
        private GameObject _seriesTabBarRoot;
        private ScrollRect _seriesTabScroll;
        private RectTransform _seriesTabContent;
        private GameObject _seriesBannerRoot;
        private Image _seriesSealIcon;
        private Image _seriesSealFallbackBg;
        private TMP_Text _seriesSealFallbackLabel;
        private TMP_Text _seriesBannerTitle;
        private Image _seriesBannerBadgeBg;
        private TMP_Text _seriesBannerBadgeLabel;
        private TMP_Text _seriesBannerDesc;
        private Button _seriesEpilogueBtn;
        private TMP_Text _seriesEpilogueBtnLabel;

        // 弹层 4：系列合篇故事阅读层（仅在服务端权威 Completed == true 时解锁）
        private GameObject _seriesEpilogueOverlay;
        private Button _seriesEpilogueBackBtn;
        private Button _seriesEpilogueCloseBtn;
        private Button _seriesEpilogueFinishBtn;
        private Image _seriesEpilogueSealIcon;
        private Image _seriesEpilogueSealFallbackBg;
        private TMP_Text _seriesEpilogueSealFallbackLabel;
        private TMP_Text _seriesEpilogueTitleText;
        private TMP_Text _seriesEpilogueMetaText;
        private ScrollRect _seriesEpilogueScroll;
        private RectTransform _seriesEpilogueContent;
        private TMP_Text _seriesEpilogueDescLabel;
        private RectTransform _seriesEpilogueDivider;
        private TMP_Text _seriesEpilogueBodyText;
        private Selectable[] _seriesEpilogueControls;

        private float _lastEpilogueBodyFontSize;
        private float _lastEpilogueViewportWidth = -1f, _lastEpilogueViewportHeight = -1f;
        private string _lastEpilogueDesc, _lastEpilogueBody;

#if UNITY_EDITOR
        public GameObject SeriesTabBarRoot => _seriesTabBarRoot;
        public RectTransform SeriesTabContent => _seriesTabContent;
        public GameObject SeriesBannerRoot => _seriesBannerRoot;
        public GameObject SeriesEpilogueOverlay => _seriesEpilogueOverlay;
        public ScrollRect SeriesEpilogueScroll => _seriesEpilogueScroll;
        public TMP_Text SeriesEpilogueBodyText => _seriesEpilogueBodyText;

        public void SetSeriesEpilogueBodyLookupForVerification(Func<uint, string> lookup)
        {
            _seriesEpilogueBodyLookupOverride = lookup;
        }

        public void RefreshSeriesEpilogueLayoutForVerification()
        {
            RefreshSeriesEpilogueScrollLayout();
        }
#endif

        private void BuildSeriesUi(RectTransform mainRoot, RectTransform fittedRoot)
        {
            // 左栏顶右侧：已集齐系列计数铭牌（220x44 原生高 44px 横向三段拉伸，文字避开两端 35px 云纹）
            var summaryPlate = AppearanceUiArt.HorizontalBar(mainRoot, "AppearanceSeriesSummary",
                AppearanceUiArt.LoadSeries("series_badge_dim") ?? AppearanceUiArt.LoadTeam("badge_leader"),
                436f, 152f, 244f, 44f, AppearanceUiArt.SeriesDimBg);
            _seriesSummaryLabel = QdaoUguiFactory.CreateText("SeriesSummaryLabel", summaryPlate.transform,
                36f, 0f, 172f, 44f, "集齐系列 0/0", 20f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _seriesSummaryLabel.richText = false;
            _seriesSummaryLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _seriesSummaryLabel.fontStyle = FontStyles.Bold;
            _seriesSummaryLabel.enableAutoSizing = true;
            _seriesSummaryLabel.fontSizeMin = 15f;
            _seriesSummaryLabel.fontSizeMax = 20f;
            _seriesSummaryRoot = summaryPlate.gameObject;
            _seriesSummaryRoot.SetActive(false);

            // 左栏系列筛选横条（现有 4 项同排单行展示，后续扩展更多系列时自动启用横向滑动）
            var tabBar = QdaoUguiFactory.CreateRect("AppearanceSeriesTabBar", mainRoot, 68f, 206f, 612f, 48f);
            var tabHit = tabBar.gameObject.AddComponent<Image>();
            tabHit.color = new Color(1f, 1f, 1f, 0.001f);
            tabHit.raycastTarget = true;
            tabBar.gameObject.AddComponent<RectMask2D>();
            _seriesTabContent = QdaoUguiFactory.CreateRect("SeriesTabContent", tabBar, 0f, 0f, 612f, 48f);
            _seriesTabScroll = tabBar.gameObject.AddComponent<ScrollRect>();
            _seriesTabScroll.viewport = tabBar;
            _seriesTabScroll.content = _seriesTabContent;
            _seriesTabScroll.horizontal = true;
            _seriesTabScroll.vertical = false;
            _seriesTabScroll.movementType = ScrollRect.MovementType.Clamped;
            _seriesTabScroll.scrollSensitivity = 36f;
            _seriesTabBarRoot = tabBar.gameObject;
            _seriesTabBarRoot.SetActive(false);

            // 左栏选中系列时的精简系列信息条（612x120 九宫格底板：左 68x68 印章，中右标题/铭牌/简介与合篇按钮）
            var bannerCard = AppearanceUiArt.SeriesCard(mainRoot, "AppearanceSeriesBanner", "series_banner_card",
                68f, 258f, 612f, 120f, AppearanceUiArt.ParchmentCard);
            var sealBox = QdaoUguiFactory.CreateRect("SeriesSealSlot", bannerCard.transform, 18f, 26f, 68f, 68f);
            _seriesSealFallbackBg = QdaoUguiFactory.CreateImage("SeriesSealFallbackBg", sealBox, 0f, 0f, 68f, 68f, null);
            _seriesSealFallbackBg.color = AppearanceUiArt.SeriesDimBg;
            _seriesSealFallbackLabel = QdaoUguiFactory.CreateText("SeriesSealFallbackLabel", sealBox,
                4f, 4f, 60f, 60f, "未", 28f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _seriesSealFallbackLabel.richText = false;
            _seriesSealFallbackLabel.fontStyle = FontStyles.Bold;
            _seriesSealIcon = QdaoUguiFactory.CreateImage("SeriesSealIcon", sealBox, 0f, 0f, 68f, 68f, null);
            _seriesSealIcon.preserveAspect = true;
            _seriesSealIcon.enabled = false;

            _seriesBannerTitle = QdaoUguiFactory.CreateText("SeriesBannerTitle", bannerCard.transform,
                102f, 14f, 250f, 30f, "", 20f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            _seriesBannerTitle.richText = false;
            _seriesBannerTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _seriesBannerTitle.fontStyle = FontStyles.Bold;
            _seriesBannerTitle.enableAutoSizing = true;
            _seriesBannerTitle.fontSizeMin = 15f;
            _seriesBannerTitle.fontSizeMax = 20f;

            _seriesBannerBadgeBg = AppearanceUiArt.HorizontalBar(bannerCard.transform, "SeriesBannerBadge",
                null, 102f, 46f, 186f, SeriesBadgeHeight, AppearanceUiArt.SeriesDimBg);
            _seriesBannerBadgeLabel = QdaoUguiFactory.CreateText("SeriesBannerBadgeLabel", _seriesBannerBadgeBg.transform,
                28f, 0f, 130f, SeriesBadgeHeight, "", 18f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _seriesBannerBadgeLabel.richText = false;
            _seriesBannerBadgeLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _seriesBannerBadgeLabel.enableAutoSizing = true;
            _seriesBannerBadgeLabel.fontSizeMin = 13f;
            _seriesBannerBadgeLabel.fontSizeMax = 18f;

            _seriesBannerDesc = QdaoUguiFactory.CreateText("SeriesBannerDesc", bannerCard.transform,
                102f, 84f, 490f, 24f, "", 16f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _seriesBannerDesc.richText = false;
            _seriesBannerDesc.textWrappingMode = TextWrappingModes.NoWrap;
            _seriesBannerDesc.overflowMode = TextOverflowModes.Ellipsis;

            _seriesEpilogueBtn = AppearanceUiArt.ActionButton(bannerCard.transform, "AppearanceReadSeriesEpilogue",
                "集齐解锁合篇", 360f, 24f, 232f, 50f, HandleReadSeriesEpilogueClicked, primary: false, fontSize: 18f);
            _seriesEpilogueBtnLabel = _seriesEpilogueBtn.GetComponentInChildren<TMP_Text>(true);
            if (_seriesEpilogueBtnLabel != null)
            {
                // 预留按钮两端金尖装饰留白，防止较长篇名紧贴左右边框
                _seriesEpilogueBtnLabel.rectTransform.offsetMin = new Vector2(26f, 0f);
                _seriesEpilogueBtnLabel.rectTransform.offsetMax = new Vector2(-26f, 0f);
                _seriesEpilogueBtnLabel.textWrappingMode = TextWrappingModes.NoWrap;
                _seriesEpilogueBtnLabel.fontSizeMin = 13f;
                _seriesEpilogueBtnLabel.fontSizeMax = 18f;
            }
            _seriesBannerRoot = bannerCard.gameObject;
            _seriesBannerRoot.SetActive(false);

            BuildSeriesEpilogueOverlay(fittedRoot);
        }

        private void BuildSeriesEpilogueOverlay(RectTransform parent)
        {
            var root = CreateOverlayBackdrop(parent, "AppearanceSeriesEpilogueOverlay");

            const float w = 1360f, h = 820f;
            var box = QdaoUguiFactory.CreateCenteredRect("SeriesEpilogueFrame", root, w, h);
            AppearanceUiArt.Frame(box, "SeriesEpilogueFrameBg", 0f, 0f, w, h);

            _seriesEpilogueBackBtn = AppearanceUiArt.ActionButton(box, "AppearanceSeriesEpilogueBack",
                "返回", 64f, 36f, 160f, 56f, HandleCloseSeriesEpilogue, primary: false, fontSize: 25f);

            // 顶栏居中点亮印章（84x84，与合篇效果图保持一致）
            var sealHolder = QdaoUguiFactory.CreateRect("SeriesEpilogueSealSlot", box, (w - 84f) * 0.5f, 14f, 84f, 84f);
            var epilogueBgRect = QdaoUguiFactory.CreateStretch("SeriesEpilogueSealFallbackBg", sealHolder, Vector4.zero);
            _seriesEpilogueSealFallbackBg = epilogueBgRect.gameObject.AddComponent<Image>();
            _seriesEpilogueSealFallbackBg.color = AppearanceUiArt.SeriesLitBg;
            _seriesEpilogueSealFallbackLabel = QdaoUguiFactory.CreateText("SeriesEpilogueSealFallbackLabel", sealHolder,
                0f, 0f, 84f, 84f, "印", 32f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
            _seriesEpilogueSealFallbackLabel.rectTransform.anchorMin = Vector2.zero;
            _seriesEpilogueSealFallbackLabel.rectTransform.anchorMax = Vector2.one;
            _seriesEpilogueSealFallbackLabel.rectTransform.offsetMin = Vector2.zero;
            _seriesEpilogueSealFallbackLabel.rectTransform.offsetMax = Vector2.zero;
            _seriesEpilogueSealFallbackLabel.richText = false;
            _seriesEpilogueSealFallbackLabel.fontStyle = FontStyles.Bold;
            var epilogueIconRect = QdaoUguiFactory.CreateStretch("SeriesEpilogueSealIcon", sealHolder, Vector4.zero);
            _seriesEpilogueSealIcon = epilogueIconRect.gameObject.AddComponent<Image>();
            _seriesEpilogueSealIcon.preserveAspect = true;
            _seriesEpilogueSealIcon.enabled = false;

            _seriesEpilogueTitleText = QdaoUguiFactory.CreateText("SeriesEpilogueTitle", box,
                240f, 100f, 880f, 48f, "", 38f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _seriesEpilogueTitleText.richText = false;
            _seriesEpilogueTitleText.textWrappingMode = TextWrappingModes.NoWrap;
            _seriesEpilogueTitleText.fontStyle = FontStyles.Bold;
            _seriesEpilogueTitleText.enableAutoSizing = true;
            _seriesEpilogueTitleText.fontSizeMin = 24f;
            _seriesEpilogueTitleText.fontSizeMax = 38f;

            _seriesEpilogueMetaText = QdaoUguiFactory.CreateText("SeriesEpilogueMeta", box,
                240f, 150f, 880f, 30f, "", 22f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _seriesEpilogueMetaText.richText = false;
            _seriesEpilogueMetaText.textWrappingMode = TextWrappingModes.NoWrap;
            _seriesEpilogueMetaText.enableAutoSizing = true;
            _seriesEpilogueMetaText.fontSizeMin = 16f;
            _seriesEpilogueMetaText.fontSizeMax = 22f;

            _seriesEpilogueCloseBtn = AppearanceUiArt.CloseButton(box, "AppearanceSeriesEpilogueClose",
                w - 112f, 30f, 68f, HidePanel);

            AppearanceUiArt.SeriesCard(box, "SeriesEpiloguePaperCard", "series_epilogue_card",
                84f, 186f, 1192f, 528f, AppearanceUiArt.ParchmentInner);

            // 正文严格落在 CONTROL-USAGE.md 声明的 series_epilogue_card 安全阅读区（卡内 x=120..1071, y=45..489）
            var viewport = QdaoUguiFactory.CreateRect("SeriesEpilogueViewport", box,
                204f, 231f, SeriesEpilogueViewportWidth, SeriesEpilogueViewportHeight);
            var viewHit = viewport.gameObject.AddComponent<Image>();
            viewHit.color = new Color(1f, 1f, 1f, 0.001f);
            viewHit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            _seriesEpilogueContent = QdaoUguiFactory.CreateRect("SeriesEpilogueContent", viewport,
                0f, 0f, SeriesEpilogueViewportWidth, SeriesEpilogueViewportHeight + 24f);
            _seriesEpilogueScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _seriesEpilogueScroll.viewport = viewport;
            _seriesEpilogueScroll.content = _seriesEpilogueContent;
            _seriesEpilogueScroll.horizontal = false;
            _seriesEpilogueScroll.vertical = true;
            _seriesEpilogueScroll.movementType = ScrollRect.MovementType.Clamped;
            _seriesEpilogueScroll.scrollSensitivity = 48f;
            AppearanceUiArt.AddVerticalScrollbar(_seriesEpilogueScroll, box, "SeriesEpilogueScrollbar",
                1180f, 240f, 14f, 426f);

            _seriesEpilogueDescLabel = QdaoUguiFactory.CreateText("SeriesEpilogueDesc", _seriesEpilogueContent,
                12f, 12f, SeriesEpilogueViewportWidth - 24f, 84f, "", 23f,
                AppearanceUiArt.MutedInk, TextAlignmentOptions.TopLeft);
            _seriesEpilogueDescLabel.richText = false;
            _seriesEpilogueDescLabel.textWrappingMode = TextWrappingModes.Normal;
            _seriesEpilogueDescLabel.overflowMode = TextOverflowModes.Overflow;
            _seriesEpilogueDescLabel.lineSpacing = 8f;

            var dividerImg = QdaoUguiFactory.CreateImage("SeriesEpilogueDivider", _seriesEpilogueContent,
                12f, 108f, SeriesEpilogueViewportWidth - 24f, 2f, null);
            dividerImg.color = new Color(AppearanceUiArt.ParchmentBorder.r, AppearanceUiArt.ParchmentBorder.g,
                AppearanceUiArt.ParchmentBorder.b, 0.6f);
            _seriesEpilogueDivider = dividerImg.rectTransform;

            _seriesEpilogueBodyText = QdaoUguiFactory.CreateText("SeriesEpilogueBody", _seriesEpilogueContent,
                12f, 132f, SeriesEpilogueViewportWidth - 24f, 340f, "", 30f,
                AppearanceUiArt.JadeInk, TextAlignmentOptions.TopLeft);
            _seriesEpilogueBodyText.richText = false;
            _seriesEpilogueBodyText.textWrappingMode = TextWrappingModes.Normal;
            _seriesEpilogueBodyText.overflowMode = TextOverflowModes.Overflow;
            _seriesEpilogueBodyText.lineSpacing = 14f;
            _seriesEpilogueBodyText.paragraphSpacing = 18f;
            _lastEpilogueBodyFontSize = _seriesEpilogueBodyText.fontSize;

            _seriesEpilogueFinishBtn = AppearanceUiArt.ActionButton(box, "AppearanceSeriesEpilogueFinish",
                "读完返回", (w - 300f) * 0.5f, 728f, 300f, 64f, HandleCloseSeriesEpilogue, primary: true, fontSize: 27f);

            _seriesEpilogueControls = new Selectable[]
            {
                _seriesEpilogueBackBtn, _seriesEpilogueFinishBtn, _seriesEpilogueCloseBtn
            };
            _seriesEpilogueOverlay = root.gameObject;
            _seriesEpilogueOverlay.SetActive(false);
        }

        private IReadOnlyList<CharacterAppearanceEntry> SyncSeriesStateAndFilter(PlayerAppearanceInfo info)
        {
            _validSeriesIdsBuffer.Clear();
            if (info != null && info.SeriesProgress != null)
            {
                for (int i = 0; i < info.SeriesProgress.Count; i++)
                    _validSeriesIdsBuffer.Add(info.SeriesProgress[i].SeriesId);
            }
            _state.SyncSeriesSelection(_validSeriesIdsBuffer, seriesId =>
            {
                var found = AppearanceSeriesModels.FindSeries(info, seriesId);
                return found != null && found.Completed && found.TotalCount > 0;
            });

            var visible = AppearanceSeriesModels.FilterBySeries(info, _state.SelectedSeriesId);
            if (info != null && _state.SelectedSeriesId != AppearanceSeriesModels.AllSeriesId && visible.Count > 0)
            {
                _filteredAppearanceIdsBuffer.Clear();
                for (int i = 0; i < visible.Count; i++)
                    _filteredAppearanceIdsBuffer.Add(visible[i].AppearanceId);
                _state.SyncAvailableAppearances(_filteredAppearanceIdsBuffer, info.ActiveAppearanceId, info.PermanentAppearanceId);
            }
            return visible;
        }

        private void RenderSeriesUi(PlayerAppearanceInfo info)
        {
            bool hasSeries = info != null && info.SeriesProgress != null && info.SeriesProgress.Count > 0;
            if (!hasSeries)
            {
                ClearSeriesTabs();
                if (_seriesSummaryRoot != null) _seriesSummaryRoot.SetActive(false);
                if (_seriesTabBarRoot != null) _seriesTabBarRoot.SetActive(false);
                if (_seriesBannerRoot != null) _seriesBannerRoot.SetActive(false);
                RenderSeriesEpilogueOverlay(null);
                return;
            }

            int completedCount = AppearanceSeriesModels.CountCompletedSeries(info);
            if (_seriesSummaryLabel != null)
                _seriesSummaryLabel.text = $"集齐系列 {completedCount}/{info.SeriesProgress.Count}";

            SyncSeriesTabButtons(info);
            RenderSelectedSeriesBanner(info);
            RenderSeriesEpilogueOverlay(info);
        }

        private void SyncSeriesTabButtons(PlayerAppearanceInfo info)
        {
            if (_seriesTabContent == null) return;
            string signature = BuildSeriesTabSignature(info);
            if (signature != _seriesTabSignature)
            {
                RebuildSeriesTabs(info);
                _seriesTabSignature = signature;
            }

            for (int i = 0; i < _seriesTabs.Count; i++)
            {
                var tab = _seriesTabs[i];
                bool selected = tab.SeriesId == _state.SelectedSeriesId;
                if (tab.SeriesId == AppearanceSeriesModels.AllSeriesId)
                {
                    tab.Label.text = "全部";
                    AppearanceUiArt.ApplySeriesTab(tab.Background, tab.Label, selected, completed: false, SeriesTabHeight);
                }
                else
                {
                    var series = AppearanceSeriesModels.FindSeries(info, tab.SeriesId);
                    bool completed = series != null && series.Completed && series.TotalCount > 0;
                    string label = series != null
                        ? (completed ? $"{series.Name} {series.OwnedCount}/{series.TotalCount} 印"
                            : $"{series.Name} {series.OwnedCount}/{series.TotalCount}")
                        : "";
                    tab.Label.text = label;
                    AppearanceUiArt.ApplySeriesTab(tab.Background, tab.Label, selected, completed, SeriesTabHeight);
                }
            }
        }

        private void RebuildSeriesTabs(PlayerAppearanceInfo info)
        {
            ClearSeriesTabs();
            float x = 0f;
            const float gap = AppearanceSeriesModels.DefaultTabGap;
            float[] widths = AppearanceSeriesModels.ComputeSeriesTabWidths(
                info.SeriesProgress, AppearanceSeriesModels.DefaultTabViewportWidth, gap);

            AddSeriesTab(AppearanceSeriesModels.AllSeriesId, "全部", widths[0], ref x, gap);
            for (int i = 0; i < info.SeriesProgress.Count; i++)
            {
                var s = info.SeriesProgress[i];
                AddSeriesTab(s.SeriesId, s.Name ?? "", widths[i + 1], ref x, gap);
            }
            float contentWidth = Mathf.Max(AppearanceSeriesModels.DefaultTabViewportWidth, x > 0f ? x - gap : 0f);
            _seriesTabContent.sizeDelta = new Vector2(contentWidth, 48f);
        }

        private void AddSeriesTab(uint seriesId, string initialLabel, float width, ref float x, float gap)
        {
            string objName = seriesId == AppearanceSeriesModels.AllSeriesId
                ? "AppearanceSeriesTab_All"
                : "AppearanceSeriesTab_" + seriesId;
            var btn = QdaoUguiFactory.CreateArtButton(objName, _seriesTabContent, x, 1f, width, SeriesTabHeight,
                null, out var bg);
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            btn.onClick.AddListener(() => HandleSelectSeriesClicked(seriesId));

            // 新版 series_tab_* 窄金角框预留左右 10~12px 安全边距，确保 4 项单行同屏完整可读
            float padX = seriesId == AppearanceSeriesModels.AllSeriesId ? 10f : 12f;
            var label = QdaoUguiFactory.CreateText("Label", btn.transform, padX, 0f, width - padX * 2f, SeriesTabHeight,
                initialLabel, 18f, AppearanceUiArt.JadeInk, TextAlignmentOptions.Center);
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 13f;
            label.fontSizeMax = 18f;

            _seriesTabs.Add(new SeriesTabView
            {
                SeriesId = seriesId,
                Root = btn.gameObject,
                Button = btn,
                Background = bg,
                Label = label,
            });
            x += width + gap;
        }

        private void ClearSeriesTabs()
        {
            for (int i = 0; i < _seriesTabs.Count; i++)
            {
                if (_seriesTabs[i].Root == null) continue;
                if (Application.isPlaying) Destroy(_seriesTabs[i].Root);
                else DestroyImmediate(_seriesTabs[i].Root);
            }
            _seriesTabs.Clear();
            _seriesTabSignature = "";
        }

        private void RenderSelectedSeriesBanner(PlayerAppearanceInfo info)
        {
            if (_seriesBannerRoot == null) return;
            var series = AppearanceSeriesModels.FindSeries(info, _state.SelectedSeriesId);
            if (series == null)
            {
                _seriesBannerRoot.SetActive(false);
                return;
            }

            bool completed = series.Completed && series.TotalCount > 0;
            ApplySealVisual(_seriesSealIcon, _seriesSealFallbackBg, _seriesSealFallbackLabel, series.SeriesKey, completed);

            _seriesBannerTitle.text = $"{series.Name} · 已收集 {series.OwnedCount}/{series.TotalCount}";
            AppearanceUiArt.ApplySeriesBadge(_seriesBannerBadgeBg, _seriesBannerBadgeLabel,
                series.CompletionBadgeName, completed, SeriesBadgeHeight);

            string desc = LookupSeriesDescription(series.SeriesId);
            if (string.IsNullOrWhiteSpace(desc))
                desc = completed
                    ? "已点亮本系列纯外观印章，可随时重读合篇故事"
                    : "集齐本系列可点亮印章并解锁合篇故事（纯外观收藏）";
            _seriesBannerDesc.text = desc;

            bool epilogueUnlocked = AppearanceSeriesModels.TryGetUnlockedEpilogue(
                info, series.SeriesId, LookupSeriesEpilogueBody, out var epilogue);
            _seriesEpilogueBtn.interactable = epilogueUnlocked && !_state.HasOverlay;
            var btnImg = _seriesEpilogueBtn.GetComponent<Image>();
            if (btnImg != null)
            {
                var sprite = AppearanceUiArt.LoadGameplay(epilogueUnlocked ? "button_primary" : "button_secondary");
                float targetHeight = _compactLayout ? 56f : 50f;
                AppearanceUiArt.ApplyHorizontalSlice(btnImg, sprite, targetHeight,
                    epilogueUnlocked ? AppearanceUiArt.DeepJade : AppearanceUiArt.SeriesDimBg);
            }
            string titleHint = !string.IsNullOrWhiteSpace(series.EpilogueTitle)
                ? $"《{series.EpilogueTitle}》" : "合篇";
            _seriesEpilogueBtnLabel.text = epilogueUnlocked
                ? $"读合篇 · {epilogue.EpilogueTitle}"
                : $"集齐解锁{titleHint}";
            _seriesEpilogueBtnLabel.color = epilogueUnlocked ? AppearanceUiArt.WarmCream : AppearanceUiArt.MutedInk;
            _seriesEpilogueBtnLabel.fontStyle = epilogueUnlocked ? FontStyles.Bold : FontStyles.Normal;
        }

        private void RenderSeriesEpilogueOverlay(PlayerAppearanceInfo info)
        {
            if (_seriesEpilogueOverlay == null) return;
            if (_state.IsSeriesEpilogueOpen && info != null &&
                AppearanceSeriesModels.TryGetUnlockedEpilogue(
                    info, _state.ReadingSeriesEpilogueId, LookupSeriesEpilogueBody, out var epilogue))
            {
                ApplySealVisual(_seriesEpilogueSealIcon, _seriesEpilogueSealFallbackBg, _seriesEpilogueSealFallbackLabel,
                    epilogue.SeriesKey, completed: true);
                _seriesEpilogueTitleText.text = $"《{epilogue.EpilogueTitle}》";
                string badgePart = string.IsNullOrWhiteSpace(epilogue.BadgeName)
                    ? "" : $" · 集齐印章「{epilogue.BadgeName}」";
                _seriesEpilogueMetaText.text = $"{epilogue.SeriesName}{badgePart} · 纯外观叙事收藏";
                string desc = LookupSeriesDescription(epilogue.SeriesId);
                _seriesEpilogueDescLabel.text = string.IsNullOrWhiteSpace(desc)
                    ? $"【{epilogue.SeriesName}】已集齐本系列全部角色。"
                    : $"【系列简介】{desc}";
                _seriesEpilogueBodyText.text = epilogue.EpilogueBody;
                RefreshSeriesEpilogueScrollLayout();
            }
            else if (_seriesEpilogueTitleText != null)
            {
                _seriesEpilogueTitleText.text = "";
                _seriesEpilogueMetaText.text = "";
                _seriesEpilogueDescLabel.text = "";
                _seriesEpilogueBodyText.text = "";
            }
        }

        private void RefreshSeriesEpilogueScrollLayout()
        {
            if (_seriesEpilogueContent == null || _seriesEpilogueDescLabel == null || _seriesEpilogueBodyText == null) return;
            float viewportWidth = _seriesEpilogueScroll != null && _seriesEpilogueScroll.viewport.rect.width > 1f
                ? _seriesEpilogueScroll.viewport.rect.width : SeriesEpilogueViewportWidth;
            float viewportHeight = _seriesEpilogueScroll != null && _seriesEpilogueScroll.viewport.rect.height > 1f
                ? _seriesEpilogueScroll.viewport.rect.height : SeriesEpilogueViewportHeight;
            if (Mathf.Approximately(viewportWidth, _lastEpilogueViewportWidth) &&
                Mathf.Approximately(viewportHeight, _lastEpilogueViewportHeight) &&
                Mathf.Approximately(_seriesEpilogueBodyText.fontSize, _lastEpilogueBodyFontSize) &&
                _lastEpilogueDesc == _seriesEpilogueDescLabel.text &&
                _lastEpilogueBody == _seriesEpilogueBodyText.text) return;

            _lastEpilogueViewportWidth = viewportWidth;
            _lastEpilogueViewportHeight = viewportHeight;
            _lastEpilogueBodyFontSize = _seriesEpilogueBodyText.fontSize;
            _lastEpilogueDesc = _seriesEpilogueDescLabel.text;
            _lastEpilogueBody = _seriesEpilogueBodyText.text;

            float textWidth = Mathf.Max(100f, viewportWidth - 24f);
            _seriesEpilogueDescLabel.ForceMeshUpdate(true, true);
            float descH = Mathf.Max(40f, Mathf.Ceil(_seriesEpilogueDescLabel.GetPreferredValues(_seriesEpilogueDescLabel.text, textWidth, 0f).y));
            PlaceTopLeft(_seriesEpilogueDescLabel.rectTransform, 12f, 12f, textWidth, descH);

            float dividerY = 12f + descH + 16f;
            if (_seriesEpilogueDivider != null) PlaceTopLeft(_seriesEpilogueDivider, 12f, dividerY, textWidth, 2f);

            float bodyY = dividerY + 20f;
            _seriesEpilogueBodyText.ForceMeshUpdate(true, true);
            float bodyH = Mathf.Max(220f, Mathf.Ceil(_seriesEpilogueBodyText.GetPreferredValues(_seriesEpilogueBodyText.text, textWidth, 0f).y));
            PlaceTopLeft(_seriesEpilogueBodyText.rectTransform, 12f, bodyY, textWidth, bodyH);

            float contentHeight = Mathf.Max(viewportHeight + 24f, bodyY + bodyH + 48f);
            _seriesEpilogueContent.sizeDelta = new Vector2(viewportWidth, contentHeight);
        }

        private void HandleSelectSeriesClicked(uint seriesId)
        {
            if (_state.HasOverlay) return;
            _state.SelectSeries(seriesId, CurrentListPosition());
            _compactDetail = false;
            Render();
            if (_listScroll != null)
            {
                _listScroll.velocity = Vector2.zero;
                _listScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void HandleReadSeriesEpilogueClicked()
        {
            if (_state.HasOverlay || _client?.Info == null || _state.SelectedSeriesId == AppearanceSeriesModels.AllSeriesId)
                return;
            if (!AppearanceSeriesModels.TryGetUnlockedEpilogue(
                    _client.Info, _state.SelectedSeriesId, LookupSeriesEpilogueBody, out var epilogue))
                return;
            if (!_state.OpenSeriesEpilogue(epilogue.SeriesId, seriesCompleted: true, epilogue.EpilogueBody, CurrentListPosition()))
                return;
            Render();
            if (_seriesEpilogueScroll != null)
            {
                _seriesEpilogueScroll.velocity = Vector2.zero;
                _seriesEpilogueScroll.verticalNormalizedPosition = 1f;
            }
            FocusFirstControlOfActiveOverlay();
        }

        private void HandleCloseSeriesEpilogue()
        {
            if (!_state.CloseSeriesEpilogue()) return;
            Render();
            RestoreListScrollPosition();
            if (EventSystem.current != null)
            {
                if (_seriesEpilogueBtn != null && _seriesEpilogueBtn.gameObject.activeInHierarchy && _seriesEpilogueBtn.interactable)
                    EventSystem.current.SetSelectedGameObject(_seriesEpilogueBtn.gameObject);
                else
                    EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void ApplySeriesLayout(bool compact, bool showList)
        {
            var main = _mainPanelGroup != null ? _mainPanelGroup.transform : null;
            if (main == null) return;

            var info = _client?.Info;
            bool hasSeries = showList && info != null && info.SeriesProgress != null && info.SeriesProgress.Count > 0;
            bool showBanner = hasSeries && AppearanceSeriesModels.FindSeries(info, _state.SelectedSeriesId) != null;

            if (_seriesSummaryRoot != null) _seriesSummaryRoot.SetActive(hasSeries);
            if (_seriesTabBarRoot != null) _seriesTabBarRoot.SetActive(hasSeries);
            if (_seriesBannerRoot != null) _seriesBannerRoot.SetActive(showBanner);

            if (compact)
            {
                if (hasSeries)
                {
                    MoveReadingElement(main, "AppearanceSeriesSummary", 532f, 132f, 584f, 48f);
                    var summaryImg = _seriesSummaryRoot != null ? _seriesSummaryRoot.GetComponent<Image>() : null;
                    if (summaryImg != null)
                        AppearanceUiArt.ApplyHorizontalSlice(summaryImg, summaryImg.sprite, 48f, AppearanceUiArt.SeriesDimBg);
                    if (_seriesSummaryLabel != null)
                        PlaceTopLeft(_seriesSummaryLabel.rectTransform, 44f, 0f, 496f, 48f);
                    MoveReadingElement(main, "AppearanceSeriesTabBar", 56f, 194f, 1060f, 52f);
                    if (showBanner)
                    {
                        ApplySeriesCardSprite(_seriesBannerRoot != null ? _seriesBannerRoot.GetComponent<Image>() : null,
                            AppearanceUiArt.LoadSeries("series_banner_card") ?? AppearanceUiArt.LoadTeam("application_card"),
                            AppearanceUiArt.ParchmentCard);
                        MoveReadingElement(main, "AppearanceSeriesBanner", 56f, 252f, 1060f, 92f);
                        var banner = _seriesBannerRoot.transform;
                        MoveReadingElement(banner, "SeriesSealSlot", 18f, 12f, 68f, 68f);
                        MoveReadingElement(banner, "SeriesBannerTitle", 100f, 10f, 360f, 34f);
                        MoveReadingElement(banner, "SeriesBannerBadge", 472f, 10f, 220f, SeriesBadgeHeight);
                        if (_seriesBannerBadgeLabel != null)
                            PlaceTopLeft(_seriesBannerBadgeLabel.rectTransform, 28f, 0f, 164f, SeriesBadgeHeight);
                        MoveReadingElement(banner, "SeriesBannerDesc", 100f, 52f, 610f, 28f);
                        MoveReadingElement(banner, "AppearanceReadSeriesEpilogue", 736f, 18f, 304f, 56f);
                        var epilogueBtnImg = _seriesEpilogueBtn != null ? _seriesEpilogueBtn.GetComponent<Image>() : null;
                        if (epilogueBtnImg != null && epilogueBtnImg.sprite != null)
                            AppearanceUiArt.ApplyHorizontalSlice(epilogueBtnImg, epilogueBtnImg.sprite, 56f, AppearanceUiArt.SeriesDimBg);
                        MoveReadingElement(main, "AppearanceListViewport", 56f, 350f, 1036f, 222f);
                        MoveReadingElement(main, "AppearanceListScrollbar", 1104f, 350f, 12f, 222f);
                    }
                    else
                    {
                        MoveReadingElement(main, "AppearanceListViewport", 56f, 254f, 1036f, 318f);
                        MoveReadingElement(main, "AppearanceListScrollbar", 1104f, 254f, 12f, 318f);
                    }
                }
            }
            else
            {
                if (hasSeries)
                {
                    MoveReadingElement(main, "AppearanceSeriesSummary", 436f, 152f, 244f, 44f);
                    var summaryImg = _seriesSummaryRoot != null ? _seriesSummaryRoot.GetComponent<Image>() : null;
                    if (summaryImg != null)
                        AppearanceUiArt.ApplyHorizontalSlice(summaryImg, summaryImg.sprite, 44f, AppearanceUiArt.SeriesDimBg);
                    if (_seriesSummaryLabel != null)
                        PlaceTopLeft(_seriesSummaryLabel.rectTransform, 36f, 0f, 172f, 44f);
                    MoveReadingElement(main, "AppearanceSeriesTabBar", 68f, 206f, 612f, 48f);
                    if (showBanner)
                    {
                        ApplySeriesCardSprite(_seriesBannerRoot != null ? _seriesBannerRoot.GetComponent<Image>() : null,
                            AppearanceUiArt.LoadSeries("series_banner_card") ?? AppearanceUiArt.LoadTeam("application_card"),
                            AppearanceUiArt.ParchmentCard);
                        MoveReadingElement(main, "AppearanceSeriesBanner", 68f, 258f, 612f, 120f);
                        var banner = _seriesBannerRoot.transform;
                        MoveReadingElement(banner, "SeriesSealSlot", 18f, 26f, 68f, 68f);
                        MoveReadingElement(banner, "SeriesBannerTitle", 102f, 14f, 250f, 30f);
                        MoveReadingElement(banner, "SeriesBannerBadge", 102f, 46f, 186f, SeriesBadgeHeight);
                        if (_seriesBannerBadgeLabel != null)
                            PlaceTopLeft(_seriesBannerBadgeLabel.rectTransform, 28f, 0f, 130f, SeriesBadgeHeight);
                        MoveReadingElement(banner, "SeriesBannerDesc", 102f, 84f, 490f, 24f);
                        MoveReadingElement(banner, "AppearanceReadSeriesEpilogue", 360f, 24f, 232f, 50f);
                        var epilogueBtnImg = _seriesEpilogueBtn != null ? _seriesEpilogueBtn.GetComponent<Image>() : null;
                        if (epilogueBtnImg != null && epilogueBtnImg.sprite != null)
                            AppearanceUiArt.ApplyHorizontalSlice(epilogueBtnImg, epilogueBtnImg.sprite, 50f, AppearanceUiArt.SeriesDimBg);
                        MoveReadingElement(main, "AppearanceListViewport", 68f, 384f, 594f, 486f);
                        MoveReadingElement(main, "AppearanceListScrollbar", 668f, 384f, 12f, 486f);
                    }
                    else
                    {
                        MoveReadingElement(main, "AppearanceListViewport", 68f, 260f, 594f, 610f);
                        MoveReadingElement(main, "AppearanceListScrollbar", 668f, 260f, 12f, 610f);
                    }
                }
                else
                {
                    MoveReadingElement(main, "AppearanceListViewport", 68f, 210f, 594f, 660f);
                    MoveReadingElement(main, "AppearanceListScrollbar", 668f, 210f, 12f, 660f);
                }
            }
        }

        private void ApplySeriesModalLayout(bool compact)
        {
            var frame = _seriesEpilogueOverlay != null
                ? _seriesEpilogueOverlay.transform.Find("SeriesEpilogueFrame") as RectTransform : null;
            if (frame == null) return;
            float epilogueW = compact ? 1140f : 1360f;
            float epilogueH = compact ? 620f : 820f;
            frame.sizeDelta = new Vector2(epilogueW, epilogueH);
            MoveReadingElement(frame, "SeriesEpilogueFrameBg", 0f, 0f, epilogueW, epilogueH);
            RefreshFrameScale(frame, "SeriesEpilogueFrameBg", epilogueW, epilogueH);
            MoveReadingElement(frame, "AppearanceSeriesEpilogueBack", compact ? 36f : 64f, compact ? 28f : 36f, 160f, compact ? 70f : 56f);
            MoveReadingElement(frame, "SeriesEpilogueSealSlot", compact ? 212f : (epilogueW - 84f) * 0.5f,
                compact ? 32f : 14f, compact ? 64f : 84f, compact ? 64f : 84f);
            MoveReadingElement(frame, "SeriesEpilogueTitle", compact ? 288f : 240f, compact ? 28f : 100f,
                compact ? 680f : 880f, compact ? 50f : 48f);
            MoveReadingElement(frame, "SeriesEpilogueMeta", compact ? 288f : 240f, compact ? 82f : 150f,
                compact ? 680f : 880f, compact ? 32f : 30f);
            MoveReadingElement(frame, "AppearanceSeriesEpilogueClose", compact ? 1030f : epilogueW - 112f,
                compact ? 28f : 30f, compact ? 72f : 68f, compact ? 72f : 68f);
            var paperCardTr = frame.Find("SeriesEpiloguePaperCard");
            ApplySeriesCardSprite(paperCardTr != null ? paperCardTr.GetComponent<Image>() : null,
                AppearanceUiArt.LoadSeries("series_epilogue_card") ?? AppearanceUiArt.LoadTeam("application_card"),
                AppearanceUiArt.ParchmentInner);
            MoveReadingElement(frame, "SeriesEpiloguePaperCard", compact ? 70f : 84f, compact ? 136f : 186f,
                compact ? 1000f : 1192f, compact ? 350f : 528f);
            MoveReadingElement(frame, "SeriesEpilogueViewport", compact ? 96f : 204f, compact ? 156f : 231f,
                compact ? 924f : SeriesEpilogueViewportWidth, compact ? 310f : SeriesEpilogueViewportHeight);
            MoveReadingElement(frame, "SeriesEpilogueScrollbar", compact ? 1036f : 1180f, compact ? 156f : 240f,
                14f, compact ? 310f : 426f);
            MoveReadingElement(frame, "AppearanceSeriesEpilogueFinish", compact ? 410f : (epilogueW - 300f) * 0.5f,
                compact ? 506f : 728f, compact ? 320f : 300f, compact ? 76f : 64f);
            RefreshSeriesEpilogueScrollLayout();
        }

        private static void ApplySeriesCardSprite(Image image, Sprite sprite, Color fallback)
        {
            if (image == null) return;
            image.sprite = sprite;
            if (sprite != null)
            {
                image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = 1f;
                image.color = Color.white;
            }
            else
            {
                image.type = Image.Type.Simple;
                image.color = fallback;
            }
        }

        private static void ApplySealVisual(Image icon, Image fallbackBg, TMP_Text fallbackLabel, string seriesKey, bool completed)
        {
            var sprite = AppearanceUiArt.LoadSeriesSeal(seriesKey, completed);
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.color = Color.white;
            }
            bool showFallback = sprite == null;
            if (fallbackBg != null)
            {
                fallbackBg.gameObject.SetActive(showFallback);
                fallbackBg.color = completed ? AppearanceUiArt.SeriesLitBg : AppearanceUiArt.SeriesDimBg;
            }
            if (fallbackLabel != null)
            {
                fallbackLabel.gameObject.SetActive(showFallback);
                fallbackLabel.text = completed ? "印" : "未";
                fallbackLabel.color = completed ? AppearanceUiArt.WarmCream : AppearanceUiArt.MutedInk;
            }
        }

        private static string LookupSeriesDescription(uint seriesId)
        {
            if (seriesId == 0) return "";
            var row = AppearanceSeriesTableManager.Instance.FindById(seriesId);
            return row?.Desc ?? "";
        }

        private string LookupSeriesEpilogueBody(uint seriesId)
        {
            if (seriesId == 0) return "";
            if (_seriesEpilogueBodyLookupOverride != null)
                return _seriesEpilogueBodyLookupOverride(seriesId) ?? "";
            var row = AppearanceSeriesTableManager.Instance.FindById(seriesId);
            return row?.EpilogueBody ?? "";
        }

        private static string BuildSeriesTabSignature(PlayerAppearanceInfo info)
        {
            if (info == null || info.SeriesProgress == null) return "";
            var sb = new StringBuilder(info.SeriesProgress.Count * 24);
            for (int i = 0; i < info.SeriesProgress.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(info.SeriesProgress[i].SeriesId).Append(':').Append(info.SeriesProgress[i].Name);
            }
            return sb.ToString();
        }
    }
}
