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
        private const float SeriesEpilogueViewportWidth = 944f;
        private const float SeriesEpilogueViewportHeight = 432f;
        private const float SeriesTabHeight = 44f;
        private const float SeriesBadgeHeight = 36f;

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
            // 左栏顶右侧：已集齐系列计数条（文案避开两端卷云装饰）
            var summaryPlate = AppearanceUiArt.HorizontalBar(mainRoot, "AppearanceSeriesSummary",
                AppearanceUiArt.LoadSeries("series_badge_dim") ?? AppearanceUiArt.LoadTeam("badge_leader"),
                416f, 148f, 264f, 52f, AppearanceUiArt.SeriesDimBg);
            _seriesSummaryLabel = QdaoUguiFactory.CreateText("SeriesSummaryLabel", summaryPlate.transform,
                42f, 0f, 180f, 52f, "集齐系列 0/0", 21f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _seriesSummaryLabel.richText = false;
            _seriesSummaryLabel.fontStyle = FontStyles.Bold;
            _seriesSummaryLabel.enableAutoSizing = true;
            _seriesSummaryLabel.fontSizeMin = 15f;
            _seriesSummaryLabel.fontSizeMax = 21f;
            _seriesSummaryRoot = summaryPlate.gameObject;
            _seriesSummaryRoot.SetActive(false);

            // 左栏系列筛选横条（支持横向滑动，便于未来持续扩充节日/门派系列）
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

            // 左栏选中系列时的精简系列信息条（612x120 原比例卡片：左侧圆环印章槽 cx=83 cy=60、x=151 金色竖线、右侧正文区 x=166..586）
            var bannerCard = AppearanceUiArt.SeriesCard(mainRoot, "AppearanceSeriesBanner", "series_banner_card",
                68f, 258f, 612f, 120f, AppearanceUiArt.ParchmentCard);
            var sealBox = QdaoUguiFactory.CreateRect("SeriesSealSlot", bannerCard.transform, 49f, 26f, 68f, 68f);
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
                166f, 12f, 224f, 30f, "", 20f, AppearanceUiArt.DeepJade, TextAlignmentOptions.MidlineLeft);
            _seriesBannerTitle.richText = false;
            _seriesBannerTitle.fontStyle = FontStyles.Bold;
            _seriesBannerTitle.enableAutoSizing = true;
            _seriesBannerTitle.fontSizeMin = 15f;
            _seriesBannerTitle.fontSizeMax = 20f;

            _seriesBannerBadgeBg = AppearanceUiArt.HorizontalBar(bannerCard.transform, "SeriesBannerBadge",
                null, 166f, 44f, 224f, SeriesBadgeHeight, AppearanceUiArt.SeriesDimBg);
            _seriesBannerBadgeLabel = QdaoUguiFactory.CreateText("SeriesBannerBadgeLabel", _seriesBannerBadgeBg.transform,
                30f, 0f, 164f, SeriesBadgeHeight, "", 16f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _seriesBannerBadgeLabel.richText = false;
            _seriesBannerBadgeLabel.enableAutoSizing = true;
            _seriesBannerBadgeLabel.fontSizeMin = 12f;
            _seriesBannerBadgeLabel.fontSizeMax = 16f;

            _seriesBannerDesc = QdaoUguiFactory.CreateText("SeriesBannerDesc", bannerCard.transform,
                166f, 84f, 414f, 24f, "", 16f, AppearanceUiArt.MutedInk, TextAlignmentOptions.MidlineLeft);
            _seriesBannerDesc.richText = false;
            _seriesBannerDesc.overflowMode = TextOverflowModes.Ellipsis;

            _seriesEpilogueBtn = AppearanceUiArt.ActionButton(bannerCard.transform, "AppearanceReadSeriesEpilogue",
                "集齐解锁合篇", 398f, 18f, 188f, 54f, HandleReadSeriesEpilogueClicked, primary: false, fontSize: 18f);
            _seriesEpilogueBtnLabel = _seriesEpilogueBtn.GetComponentInChildren<TMP_Text>(true);
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
                "返回", 76f, 46f, 160f, 60f, HandleCloseSeriesEpilogue, primary: false, fontSize: 25f);

            var sealHolder = QdaoUguiFactory.CreateRect("SeriesEpilogueSealSlot", box, 256f, 34f, 76f, 76f);
            var epilogueBgRect = QdaoUguiFactory.CreateStretch("SeriesEpilogueSealFallbackBg", sealHolder, Vector4.zero);
            _seriesEpilogueSealFallbackBg = epilogueBgRect.gameObject.AddComponent<Image>();
            _seriesEpilogueSealFallbackBg.color = AppearanceUiArt.SeriesLitBg;
            _seriesEpilogueSealFallbackLabel = QdaoUguiFactory.CreateText("SeriesEpilogueSealFallbackLabel", sealHolder,
                0f, 0f, 76f, 76f, "印", 32f, AppearanceUiArt.WarmCream, TextAlignmentOptions.Center);
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
                348f, 30f, 664f, 52f, "", 38f, AppearanceUiArt.DeepJade, TextAlignmentOptions.Center);
            _seriesEpilogueTitleText.richText = false;
            _seriesEpilogueTitleText.fontStyle = FontStyles.Bold;
            _seriesEpilogueTitleText.enableAutoSizing = true;
            _seriesEpilogueTitleText.fontSizeMin = 24f;
            _seriesEpilogueTitleText.fontSizeMax = 38f;

            _seriesEpilogueMetaText = QdaoUguiFactory.CreateText("SeriesEpilogueMeta", box,
                348f, 86f, 664f, 34f, "", 22f, AppearanceUiArt.MutedInk, TextAlignmentOptions.Center);
            _seriesEpilogueMetaText.richText = false;
            _seriesEpilogueMetaText.enableAutoSizing = true;
            _seriesEpilogueMetaText.fontSizeMin = 16f;
            _seriesEpilogueMetaText.fontSizeMax = 22f;

            _seriesEpilogueCloseBtn = AppearanceUiArt.CloseButton(box, "AppearanceSeriesEpilogueClose",
                w - 116f, 38f, 72f, HidePanel);

            AppearanceUiArt.SeriesCard(box, "SeriesEpiloguePaperCard", "series_epilogue_card",
                84f, 154f, 1192f, 528f, AppearanceUiArt.ParchmentInner);

            // 正文与滚动条严格落在 series_epilogue_card 四角云纹与上下金线安全区（卡内 x=112..1056, y=48..480）
            var viewport = QdaoUguiFactory.CreateRect("SeriesEpilogueViewport", box,
                196f, 202f, SeriesEpilogueViewportWidth, SeriesEpilogueViewportHeight);
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
                1152f, 250f, 14f, 336f);

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
                "读完返回", (w - 320f) * 0.5f, 706f, 320f, 68f, HandleCloseSeriesEpilogue, primary: true, fontSize: 27f);

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
            const float gap = 8f;

            AddSeriesTab(AppearanceSeriesModels.AllSeriesId, "全部", 112f, ref x, gap);
            for (int i = 0; i < info.SeriesProgress.Count; i++)
            {
                var s = info.SeriesProgress[i];
                float tabWidth = Mathf.Clamp(104f + (s.Name?.Length ?? 4) * 20f + 56f, 196f, 260f);
                AddSeriesTab(s.SeriesId, s.Name ?? "", tabWidth, ref x, gap);
            }
            _seriesTabContent.sizeDelta = new Vector2(Mathf.Max(612f, x), 48f);
        }

        private void AddSeriesTab(uint seriesId, string initialLabel, float width, ref float x, float gap)
        {
            string objName = seriesId == AppearanceSeriesModels.AllSeriesId
                ? "AppearanceSeriesTab_All"
                : "AppearanceSeriesTab_" + seriesId;
            var btn = QdaoUguiFactory.CreateArtButton(objName, _seriesTabContent, x, 2f, width, SeriesTabHeight,
                null, out var bg);
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            btn.onClick.AddListener(() => HandleSelectSeriesClicked(seriesId));

            // 预留两端卷云安全边距（240x64 @ h=44 云纹约占左右 30px）
            var label = QdaoUguiFactory.CreateText("Label", btn.transform, 32f, 0f, width - 64f, SeriesTabHeight,
                initialLabel, 19f, AppearanceUiArt.JadeInk, TextAlignmentOptions.Center);
            label.richText = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 19f;

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
                if (sprite != null)
                {
                    btnImg.sprite = sprite;
                    btnImg.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                    btnImg.color = Color.white;
                }
                else
                {
                    btnImg.color = epilogueUnlocked ? AppearanceUiArt.DeepJade : AppearanceUiArt.SeriesDimBg;
                }
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
                    MoveReadingElement(main, "AppearanceSeriesSummary", 532f, 124f, 584f, 64f);
                    var summaryImg = _seriesSummaryRoot != null ? _seriesSummaryRoot.GetComponent<Image>() : null;
                    if (summaryImg != null)
                        AppearanceUiArt.ApplyHorizontalSlice(summaryImg, summaryImg.sprite, 64f, AppearanceUiArt.SeriesDimBg);
                    if (_seriesSummaryLabel != null)
                        PlaceTopLeft(_seriesSummaryLabel.rectTransform, 52f, 0f, 480f, 64f);
                    MoveReadingElement(main, "AppearanceSeriesTabBar", 56f, 194f, 1060f, 52f);
                    if (showBanner)
                    {
                        // 窄横屏横卡比例 1060x92 与 612x120 差异大，切回九宫格纸卡避免烘焙圆环被压扁
                        ApplySeriesCardSprite(_seriesBannerRoot != null ? _seriesBannerRoot.GetComponent<Image>() : null,
                            AppearanceUiArt.LoadTeam("application_card"), AppearanceUiArt.ParchmentCard);
                        MoveReadingElement(main, "AppearanceSeriesBanner", 56f, 252f, 1060f, 92f);
                        var banner = _seriesBannerRoot.transform;
                        MoveReadingElement(banner, "SeriesSealSlot", 14f, 12f, 68f, 68f);
                        MoveReadingElement(banner, "SeriesBannerTitle", 94f, 8f, 360f, 34f);
                        MoveReadingElement(banner, "SeriesBannerBadge", 464f, 8f, 260f, SeriesBadgeHeight);
                        if (_seriesBannerBadgeLabel != null)
                            PlaceTopLeft(_seriesBannerBadgeLabel.rectTransform, 30f, 0f, 200f, SeriesBadgeHeight);
                        MoveReadingElement(banner, "SeriesBannerDesc", 94f, 52f, 630f, 30f);
                        MoveReadingElement(banner, "AppearanceReadSeriesEpilogue", 742f, 18f, 300f, 56f);
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
                    MoveReadingElement(main, "AppearanceSeriesSummary", 416f, 148f, 264f, 52f);
                    var summaryImg = _seriesSummaryRoot != null ? _seriesSummaryRoot.GetComponent<Image>() : null;
                    if (summaryImg != null)
                        AppearanceUiArt.ApplyHorizontalSlice(summaryImg, summaryImg.sprite, 52f, AppearanceUiArt.SeriesDimBg);
                    if (_seriesSummaryLabel != null)
                        PlaceTopLeft(_seriesSummaryLabel.rectTransform, 42f, 0f, 180f, 52f);
                    MoveReadingElement(main, "AppearanceSeriesTabBar", 68f, 206f, 612f, 48f);
                    if (showBanner)
                    {
                        ApplySeriesCardSprite(_seriesBannerRoot != null ? _seriesBannerRoot.GetComponent<Image>() : null,
                            AppearanceUiArt.LoadSeries("series_banner_card") ?? AppearanceUiArt.LoadTeam("application_card"),
                            AppearanceUiArt.ParchmentCard);
                        MoveReadingElement(main, "AppearanceSeriesBanner", 68f, 258f, 612f, 120f);
                        var banner = _seriesBannerRoot.transform;
                        MoveReadingElement(banner, "SeriesSealSlot", 49f, 26f, 68f, 68f);
                        MoveReadingElement(banner, "SeriesBannerTitle", 166f, 12f, 224f, 30f);
                        MoveReadingElement(banner, "SeriesBannerBadge", 166f, 44f, 224f, SeriesBadgeHeight);
                        if (_seriesBannerBadgeLabel != null)
                            PlaceTopLeft(_seriesBannerBadgeLabel.rectTransform, 30f, 0f, 164f, SeriesBadgeHeight);
                        MoveReadingElement(banner, "SeriesBannerDesc", 166f, 84f, 414f, 24f);
                        MoveReadingElement(banner, "AppearanceReadSeriesEpilogue", 398f, 18f, 188f, 54f);
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
            MoveReadingElement(frame, "AppearanceSeriesEpilogueBack", compact ? 36f : 76f, compact ? 28f : 46f, 160f, compact ? 76f : 60f);
            MoveReadingElement(frame, "SeriesEpilogueSealSlot", compact ? 212f : 256f, compact ? 30f : 34f, compact ? 64f : 76f, compact ? 64f : 76f);
            MoveReadingElement(frame, "SeriesEpilogueTitle", compact ? 288f : 348f, 30f, compact ? 680f : 664f, 52f);
            MoveReadingElement(frame, "SeriesEpilogueMeta", compact ? 288f : 348f, 86f, compact ? 680f : 664f, 34f);
            MoveReadingElement(frame, "AppearanceSeriesEpilogueClose", compact ? 1030f : 1244f, compact ? 28f : 38f, 72f, 72f);
            var paperCardTr = frame.Find("SeriesEpiloguePaperCard");
            ApplySeriesCardSprite(paperCardTr != null ? paperCardTr.GetComponent<Image>() : null,
                compact ? AppearanceUiArt.LoadTeam("application_card")
                    : (AppearanceUiArt.LoadSeries("series_epilogue_card") ?? AppearanceUiArt.LoadTeam("application_card")),
                AppearanceUiArt.ParchmentInner);
            MoveReadingElement(frame, "SeriesEpiloguePaperCard", compact ? 70f : 84f, 154f, compact ? 1000f : 1192f, compact ? 320f : 528f);
            MoveReadingElement(frame, "SeriesEpilogueViewport", compact ? 84f : 196f, compact ? 168f : 202f,
                compact ? 954f : SeriesEpilogueViewportWidth, compact ? 292f : SeriesEpilogueViewportHeight);
            MoveReadingElement(frame, "SeriesEpilogueScrollbar", compact ? 1050f : 1152f, compact ? 168f : 250f,
                14f, compact ? 292f : 336f);
            MoveReadingElement(frame, "AppearanceSeriesEpilogueFinish", compact ? 410f : 520f, compact ? 506f : 706f, 320f, compact ? 76f : 68f);
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
            var sb = new StringBuilder(info.SeriesProgress.Count * 16);
            for (int i = 0; i < info.SeriesProgress.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(info.SeriesProgress[i].SeriesId);
            }
            return sb.ToString();
        }
    }
}
