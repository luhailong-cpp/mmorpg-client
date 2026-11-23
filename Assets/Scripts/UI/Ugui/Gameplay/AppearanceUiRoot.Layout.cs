using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class AppearanceUiRoot
    {
        private bool _compactLayout, _compactDetail, _layoutCaptured;
        private readonly Dictionary<RectTransform, (Vector2 Position, Vector2 Size)> _wideRects = new();
        private TMP_Text _compactListStatus;
        private Button _compactListRetry;
        private RectTransform _compactBioViewport;
        private ScrollRect _compactBioScroll;
        private Sprite _wideCardSprite;
        private UnityEngine.Transform _wideCancelTrialParent;
        private string _compactBiographyId = "";
        private readonly string[] _listElementNames =
        { "AppearanceListPanel", "AppearanceListHeader", "AppearanceListViewport", "AppearanceListScrollbar" };
        private readonly string[] _detailElementNames =
        {
            "AppearancePreviewPedestal", "AppearancePreviewPortrait", "AppearancePortraitMissing", "AppearanceTrialBanner",
            "AppearancePreviewName", "AppearanceArchiveTag", "AppearancePreviewDesc", "AppearancePreviewStatus",
            "AppearanceBiographySection", "AppearanceCardSlot", "AppearanceStartTrial", "AppearanceConfirmSwitch",
            "AppearanceStatus", "AppearanceViewPortrait", "AppearanceRetryInfo"
        };

        private void CaptureReadingLayout()
        {
            if (_layoutCaptured || _mainPanelGroup == null) return;
            foreach (var rect in _mainPanelGroup.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == _listContent || (_listContent != null && rect.IsChildOf(_listContent))) continue;
                _wideRects[rect] = (rect.anchoredPosition, rect.sizeDelta);
            }
            var main = _mainPanelGroup.transform;
            _wideCardSprite = main.Find("AppearanceCardSlot")?.GetComponent<Image>()?.sprite;
            _wideCancelTrialParent = _cancelTrialBtn.transform.parent;
            var back = AppearanceUiArt.ActionButton(main, "AppearanceBackToList", "返回形象列表",
                40, 80, 300, 76, () =>
                {
                    _compactDetail = false;
                    ApplyReadingLayout();
                    Canvas.ForceUpdateCanvases();
                    RestoreListScrollPosition();
                }, fontSize: 28);
            back.gameObject.SetActive(false);
            _compactListStatus = ReadingLabel(main, "CompactListStatus", "", 90, 260, 1000, 140, 32, true);
            _compactListRetry = AppearanceUiArt.ActionButton(main, "CompactListRetry", "重新读取形象", 380, 420, 420, 76,
                () => _client?.RequestInfo(force: true), fontSize: 28);
            _compactListStatus.gameObject.SetActive(false); _compactListRetry.gameObject.SetActive(false);
            _compactBioViewport = QdaoUguiFactory.CreateRect("CompactBiographyViewport", _biographySection.transform, 22, 78, 676, 64);
            var hit = _compactBioViewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
            _compactBioViewport.gameObject.AddComponent<RectMask2D>();
            _compactBioScroll = _compactBioViewport.gameObject.AddComponent<ScrollRect>();
            _compactBioScroll.horizontal = false; _compactBioScroll.vertical = true;
            _compactBioScroll.viewport = _compactBioViewport;
            _compactBioScroll.content = _biographyBody.rectTransform;
            _compactBioScroll.movementType = ScrollRect.MovementType.Clamped;
            _compactBioViewport.gameObject.SetActive(false);
            _layoutCaptured = true;
        }

        private void ApplyReadingLayout()
        {
            if (!_layoutCaptured || _mainPanelGroup == null || _windowFit == null) return;
            var pixels = _canvas != null ? _canvas.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
            bool compact = pixels.width < 1200 || pixels.height < 600;
            var main = _mainPanelGroup.transform;
            if (compact != _compactLayout)
            {
                foreach (var pair in _wideRects)
                {
                    if (pair.Key == null) continue;
                    pair.Key.anchoredPosition = pair.Value.Position;
                    pair.Key.sizeDelta = pair.Value.Size;
                }
                _compactLayout = compact;
                _compactBiographyId = "";
                _biographyBody.rectTransform.SetParent(compact ? _compactBioViewport : _biographySection.transform, false);
                _cancelTrialBtn.transform.SetParent(compact ? main : _wideCancelTrialParent, false);
                _biographyBody.overflowMode = compact ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
                _compactBioViewport.gameObject.SetActive(compact);
                var card = main.Find("AppearanceCardSlot")?.GetComponent<Image>();
                if (card != null)
                {
                    card.sprite = compact ? AppearanceUiArt.LoadGameplay("pet_card_normal") : _wideCardSprite;
                    card.type = compact ? Image.Type.Sliced : Image.Type.Simple;
                    card.preserveAspect = !compact;
                }
            }
            bool detail = !compact || _compactDetail;
            foreach (string name in _listElementNames) SetReadingElementVisible(main, name, !compact || !_compactDetail);
            foreach (string name in _detailElementNames) SetReadingElementVisible(main, name, detail);
            SetReadingElementVisible(main, "AppearanceBackToList", compact && _compactDetail);
            SetReadingElementVisible(main, "AppearanceRuleBanner", !compact);
            SetReadingElementVisible(main, "AppearancePreviewPedestal", !compact);
            SetReadingElementVisible(main, "AppearancePreviewDesc", !compact);
            if (_biographySection != null) _biographySection.SetActive(detail && _state.TryGetSelectedBiography(out _));
            if (_previewMissingBox != null) _previewMissingBox.SetActive(detail && !string.IsNullOrEmpty(_state.SelectedAppearanceId) && _loadedPreviewPortraitSprite == null);
            bool activeTrial = _client?.HasActiveTrial == true;
            if (_trialBannerRoot != null) _trialBannerRoot.SetActive(!compact && detail && activeTrial);
            if (_cancelTrialBtn != null) _cancelTrialBtn.gameObject.SetActive(!compact || (detail && activeTrial));
            bool empty = _client?.Info == null || _client.Info.Appearances.Count == 0;
            if (_readOnlyRetryButton != null)
                _readOnlyRetryButton.gameObject.SetActive(detail && empty && _client?.InfoPending != true);
            bool listEmpty = compact && !_compactDetail && empty;
            _compactListStatus.gameObject.SetActive(listEmpty);
            _compactListRetry.gameObject.SetActive(listEmpty && _client?.InfoPending != true);
            _compactListRetry.interactable = _client?.Ready == true;
            if (listEmpty) _compactListStatus.text = _client?.InfoPending == true ? "正在读取形象" :
                _client?.Info != null ? "暂无可展示形象" : "读取形象失败\n" + (_client?.Status ?? "");
            _windowFit.referenceSize = compact ? new Vector2(1180, 650) : new Vector2(AppearanceUiArt.FrameWidth, AppearanceUiArt.FrameHeight);
            if (compact)
            {
                PlaceTopLeft((RectTransform)main, 0, 54, 1180, 596);
                MoveReadingElement(main, "AppearanceMainFrame", 0, 0, 1180, 596);
                MoveReadingElement(main, "AppearanceTitlePlate", 232, -60, 716, 145);
                MoveReadingElement(main, "TitleTaiji", 562, -52, 56, 56);
                MoveReadingElement(main, "AppearanceClose", 1090, 8, 76, 76);
                MoveReadingElement(main, "AppearanceListPanel", 40, 112, 1100, 474);
                MoveReadingElement(main, "AppearanceListHeader", 56, 124, 460, 64);
                MoveReadingElement(main, "AppearanceListViewport", 56, 202, 1036, 366);
                MoveReadingElement(main, "AppearanceListScrollbar", 1104, 202, 12, 366);
                _listCountLabel.rectTransform.sizeDelta = new Vector2(420, 64);
                MoveReadingElement(main, "AppearancePreviewPortrait", 40, 168, 330, 294);
                MoveReadingElement(main, "AppearancePortraitMissing", 40, 260, 330, 130);
                MoveReadingElement(main, "AppearanceViewPortrait", 40, 468, activeTrial ? 156 : 330, 76);
                MoveReadingElement(main, "AppearanceCancelTrial", 214, 468, 156, 76);
                MoveReadingElement(main, "AppearancePreviewName", 410, 102, 720, 48);
                MoveReadingElement(main, "AppearanceArchiveTag", 410, 154, 720, 30);
                MoveReadingElement(main, "AppearancePreviewStatus", 410, 188, 720, 34);
                MoveReadingElement(main, "AppearanceBiographySection", 410, 230, 720, 146);
                var bio = _biographySection.transform;
                MoveReadingElement(bio, "BiographyIdentity", 164, 18, 330, 28);
                MoveReadingElement(bio, "AppearanceReadStory", 512, 0, 186, 76);
                MoveReadingElement(bio, "BiographyDivider", 22, 76, 676, 2);
                MoveReadingElement(main, "AppearanceTrialBanner", 40, 94, 330, 64);
                MoveReadingElement(main, "AppearanceCardSlot", 410, 386, 720, 92);
                var card = main.Find("AppearanceCardSlot");
                MoveReadingElement(card, "AppearanceCardIcon", 16, 12, 68, 68);
                MoveReadingElement(card, "CardOwnedLine", 104, 10, 370, 34);
                MoveReadingElement(card, "CardRuleLine", 104, 48, 370, 32);
                MoveReadingElement(card, "AppearanceOpenMall", 490, 8, 208, 76);
                MoveReadingElement(main, "AppearanceStartTrial", 410, 488, 300, 76);
                MoveReadingElement(main, "AppearanceConfirmSwitch", 728, 488, 402, 76);
                MoveReadingElement(main, "AppearanceStatus", 40, 550, 330, 30);
                MoveReadingElement(main, "AppearanceRetryInfo", 410, 254, 500, 76);
                _previewArchiveTag.alignment = TextAlignmentOptions.MidlineLeft;
                _previewStatus.fontSize = 24;
                _previewStatus.textWrappingMode = TextWrappingModes.Normal;
                if (activeTrial && _client.Info.TrialAppearanceId == _state.SelectedAppearanceId)
                    _previewStatus.text = "试用中 · 剩余 " + MmorpgClient.Game.Appearance.AppearanceModels.FormatTrialCountdown(_client.TrialRemainingSeconds);
                _biographyBody.fontSize = 30;
                _biographyBody.lineSpacing = 0;
                float textHeight = Mathf.Max(64, _biographyBody.GetPreferredValues(_biographyBody.text, 676, 100000).y + 12);
                _biographyBody.rectTransform.sizeDelta = new Vector2(676, textHeight);
                if (_compactBiographyId != _state.SelectedAppearanceId)
                {
                    _compactBiographyId = _state.SelectedAppearanceId;
                    _compactBioScroll.velocity = Vector2.zero;
                    _biographyBody.rectTransform.anchoredPosition = Vector2.zero;
                }
            }
            else
            {
                _previewArchiveTag.alignment = TextAlignmentOptions.MidlineRight;
                _previewStatus.fontSize = 23;
                _biographyBody.fontSize = 22; _biographyBody.lineSpacing = 6;
            }
            SetReadingFonts(compact);
            ApplyReadingRowLayout(compact);
            ApplyModalReadingLayout(compact);
            _windowFit.Fit();
        }

        private void ApplyModalReadingLayout(bool compact)
        {
            foreach (var overlay in new[] { _confirmOverlay, _noCardOverlay, _storyOverlay, _biographyOverlay, _portraitOverlay })
            {
                if (overlay == null) continue;
                var rect = (RectTransform)overlay.transform;
                rect.sizeDelta = compact ? new Vector2(1180, 650) : new Vector2(AppearanceUiArt.FrameWidth, AppearanceUiArt.FrameHeight);
                var dim = rect.Find("WindowDim") as RectTransform;
                if (dim != null) PlaceTopLeft(dim, compact ? 12 : 16, compact ? 12 : AppearanceUiArt.MainBodyTopOffset + 12,
                    compact ? 1156 : AppearanceUiArt.FrameWidth - 32, compact ? 626 : AppearanceUiArt.MainBodyHeight - 24);
            }
            var story = _storyOverlay != null ? _storyOverlay.transform.Find("StoryReaderFrame") as RectTransform : null;
            var bio = _biographyOverlay != null ? _biographyOverlay.transform.Find("BiographyFrame") as RectTransform : null;
            var portrait = _portraitOverlay != null ? _portraitOverlay.transform.Find("PortraitFrame") as RectTransform : null;
            if (story != null)
            {
                story.sizeDelta = compact ? new Vector2(1140, 620) : new Vector2(1360, 820);
                MoveReadingElement(story, "StoryFrameBg", 0, 0, compact ? 1140 : 1360, compact ? 620 : 820);
                MoveReadingElement(story, "AppearanceStoryBack", compact ? 36 : 76, compact ? 28 : 46, 160, compact ? 76 : 60);
                MoveReadingElement(story, "StoryTitle", compact ? 240 : 256, 32, compact ? 660 : 848, 52);
                MoveReadingElement(story, "StoryCharacter", compact ? 240 : 256, 88, compact ? 660 : 848, 34);
                MoveReadingElement(story, "AppearanceStorySkip", compact ? 944 : 1124, compact ? 28 : 46, 160, compact ? 76 : 60);
                MoveReadingElement(story, "StoryPaperCard", compact ? 70 : 84, 154, compact ? 1000 : 1192, compact ? 320 : 528);
                MoveReadingElement(story, "StoryViewport", compact ? 84 : 98, 168, compact ? 954 : StoryViewportWidth, compact ? 292 : StoryViewportHeight);
                MoveReadingElement(story, "StoryScrollbar", compact ? 1050 : 1250, 168, 14, compact ? 292 : StoryViewportHeight);
                MoveReadingElement(story, "AppearanceStoryFinish", compact ? 410 : 520, compact ? 506 : 706, 320, compact ? 76 : 68);
                MoveReadingElement(story, "StoryCloseWindow", compact ? 1048 : 1268, compact ? 506 : 706, 76, 76);
                RefreshStoryScrollLayout();
            }
            if (bio != null)
            {
                bio.sizeDelta = compact ? new Vector2(1140, 620) : new Vector2(1360, 880);
                MoveReadingElement(bio, "BiographyFrameBg", 0, 0, compact ? 1140 : 1360, compact ? 620 : 880);
                MoveReadingElement(bio, "BiographyTitlePlate", compact ? 300 : 400, -20, 560, 114);
                MoveReadingElement(bio, "BiographyName", compact ? 300 : 400, 20, 560, 70);
                MoveReadingElement(bio, "BiographyBack", compact ? 34 : 70, compact ? 28 : 42, 170, compact ? 76 : 66);
                MoveReadingElement(bio, "BiographyClose", compact ? 1030 : 1250, 28, 76, 76);
                MoveReadingElement(bio, "BiographyHeading", compact ? 70 : 110, compact ? 126 : 150, 1000, 56);
                MoveReadingElement(bio, "BiographyScroll", compact ? 70 : 110, compact ? 188 : 228, compact ? 1000 : 1138, compact ? 204 : 314);
                MoveReadingElement(bio, "BiographyStoryHeading", compact ? 70 : 110, compact ? 410 : 584, 1000, 54);
                MoveReadingElement(bio, "BiographyReadStory", compact ? 70 : 110, compact ? 478 : 662, compact ? 650 : 780, compact ? 76 : 88);
                MoveReadingElement(bio, "BiographyViewPortrait", compact ? 750 : 926, compact ? 478 : 662, 320, compact ? 76 : 88);
                MoveReadingElement(bio, "BiographyCandidateNote", compact ? 70 : 110, compact ? 570 : 786, compact ? 1000 : 1138, 36);
                LayoutBiographyBody();
            }
            if (portrait != null)
            {
                portrait.sizeDelta = compact ? new Vector2(1140, 620) : new Vector2(1360, 900);
                MoveReadingElement(portrait, "PortraitFrameBg", 0, 0, compact ? 1140 : 1360, compact ? 620 : 900);
                MoveReadingElement(portrait, "PortraitTitlePlate", compact ? 300 : 400, -20, 560, 114);
                MoveReadingElement(portrait, "PortraitName", compact ? 300 : 400, 20, 560, 70);
                MoveReadingElement(portrait, "PortraitBack", compact ? 34 : 70, compact ? 28 : 40, 170, 76);
                MoveReadingElement(portrait, "PortraitClose", compact ? 1030 : 1250, 28, 76, 76);
                MoveReadingElement(portrait, "FullPortrait", compact ? 360 : 300, compact ? 120 : 132, compact ? 420 : 760, compact ? 380 : 650);
                MoveReadingElement(portrait, "PortraitMissing", compact ? 140 : 200, compact ? 260 : 360, compact ? 860 : 960, 150);
                MoveReadingElement(portrait, "PortraitReturn", compact ? 380 : 470, compact ? 526 : 800, compact ? 380 : 420, 76);
            }
        }

        private void SetReadingFonts(bool compact)
        {
            if (_storyRecapLabel != null) _storyRecapLabel.fontSize = compact ? 24 : 23;
            if (_biographyPageBody != null) _biographyPageBody.fontSize = compact ? 30 : 34;
            if (_listCountLabel != null) _listCountLabel.fontSize = compact ? 30 : 24;
            foreach (var button in new[] { _trialBtn, _switchBtn })
            {
                var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
                if (label == null) continue;
                label.textWrappingMode = compact ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                label.fontSizeMin = compact ? 26 : 18;
                label.fontSizeMax = compact ? 30 : 26;
            }
        }

        private void ApplyReadingRowLayout(bool compact)
        {
            foreach (var row in _rows)
            {
                if (row.Root == null) continue;
                var parent = row.Root.transform;
                var rect = (RectTransform)parent;
                rect.sizeDelta = new Vector2(compact ? 1020 : 580, compact ? 102 : 98);
                if (row.CardBackground != null) row.CardBackground.rectTransform.sizeDelta = rect.sizeDelta;
                MoveReadingElement(parent, "Name", compact ? 114 : 104, 14, compact ? 450 : 248, compact ? 44 : 38);
                MoveReadingElement(parent, "Sub", compact ? 114 : 104, 58, compact ? 450 : 248, 32);
                MoveReadingElement(parent, "Badge", compact ? 610 : 362, 23, compact ? 380 : 206, BadgeHeight);
                row.NameText.fontSize = compact ? 30 : 27;
                row.SubText.fontSize = compact ? 24 : 19;
                row.BadgeLabel.rectTransform.sizeDelta = new Vector2(compact ? 360 : 186, BadgeHeight);
                row.BadgeLabel.fontSize = compact ? 24 : 20;
            }
            if (_listContent != null) _listContent.sizeDelta = new Vector2(compact ? 1036 : 594, _listContent.sizeDelta.y);
        }

        private static void MoveReadingElement(UnityEngine.Transform parent, string name, float x, float y, float width, float height)
        {
            if (parent == null) return;
            var rect = parent.Find(name) as RectTransform;
            if (rect != null) PlaceTopLeft(rect, x, y, width, height);
        }
        private static void SetReadingElementVisible(UnityEngine.Transform parent, string name, bool visible)
        {
            var item = parent.Find(name);
            if (item != null) item.gameObject.SetActive(visible);
        }
    }
}
