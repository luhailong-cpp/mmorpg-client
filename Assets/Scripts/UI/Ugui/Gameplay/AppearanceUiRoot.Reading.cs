using System;
using System.Collections.Generic;
using MmorpgClient.Game.Appearance;
using MmorpgClient.UI.Ugui.Mail;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class AppearanceUiRoot
    {
        private GameObject _biographyOverlay, _portraitOverlay;
        private TMP_Text _biographyPageName, _biographyPageBody, _biographyStoryTitle;
        private TMP_Text _portraitPageName, _portraitPageMissing;
        private ScrollRect _biographyPageScroll;
        private Image _portraitPageImage;
        private Button _previewButton, _readOnlyRetryButton;
        private Button _storyCloseButton;
        private Button _readBeforeConfirm;
        private Selectable[] _biographyControls, _portraitControls;
        private readonly Dictionary<string, QdaoCharacterCatalog.CreationPortraitLease> _portraitLeases = new(StringComparer.Ordinal);
        private object _readingConnection;
        private ulong _readingPlayerId;

        private void BuildReadingOverlays(RectTransform parent)
        {
            var main = _mainPanelGroup.transform;
            _previewButton = AppearanceUiArt.ActionButton(main, "AppearanceViewPortrait", "查看形象",
                856, 808, 320, 64, HandleViewPortraitClicked, fontSize: 26);
            var portraitHit = _previewPortrait.gameObject.AddComponent<Button>();
            _previewPortrait.raycastTarget = true;
            portraitHit.targetGraphic = _previewPortrait;
            portraitHit.onClick.AddListener(HandleViewPortraitClicked);
            _readOnlyRetryButton = AppearanceUiArt.ActionButton(main, "AppearanceRetryInfo", "重新读取形象",
                1340, 292, 420, 70, () => _client?.RequestInfo(force: true), fontSize: 26);
            _readOnlyRetryButton.gameObject.SetActive(false);
            if (_readStoryBtn != null)
                _readStoryBtn.GetComponentInChildren<TMP_Text>(true).text = "读人物志";

            var root = CreateOverlayBackdrop(parent, "AppearanceBiographyOverlay");
            var box = QdaoUguiFactory.CreateCenteredRect("BiographyFrame", root, 1360, 800);
            box.anchoredPosition = new Vector2(0f, -48f);
            AppearanceUiArt.Frame(box, "BiographyFrameBg", 0, 0, 1360, 800);
            AppearanceUiArt.TitlePlate(box, "BiographyTitlePlate", 400, -24, 560, 114);
            var back = AppearanceUiArt.ActionButton(box, "BiographyBack", "返回", 70, 40, 170, 66,
                HandleDismissOverlay, fontSize: 27);
            var close = AppearanceUiArt.CloseButton(box, "BiographyClose", 1250, 30, 68, HidePanel);
            _biographyPageName = ReadingLabel(box, "BiographyName", "", 440, -6, 480, 62, 38, true);
            _biographyPageName.color = AppearanceUiArt.WarmCream;
            _biographyPageName.fontStyle = FontStyles.Bold;
            ReadingLabel(box, "BiographyHeading", "人物小传", 110, 134, 1000, 52, 34);
            _biographyPageScroll = MailUiArt.Scroll(box, "BiographyScroll", 110, 202, 1138, 290, out var content);
            _biographyPageBody = ReadingLabel(content, "BiographyBody", "", 0, 0, 1080, 290, 34);
            ReadingLabel(box, "BiographyStoryHeading", "一段往事", 110, 526, 1000, 50, 34);
            var story = AppearanceUiArt.ActionButton(box, "BiographyReadStory", "", 110, 598, 780, 86,
                OpenStoryFromBiography, true, 32);
            _biographyStoryTitle = story.GetComponentInChildren<TMP_Text>(true);
            var preview = AppearanceUiArt.ActionButton(box, "BiographyViewPortrait", "查看形象", 926, 598, 320, 86,
                HandleViewPortraitClicked, fontSize: 28);
            ReadingLabel(box, "BiographyCandidateNote", "候选人物设定 · 小传与故事公开阅读", 130, 712, 1100, 40, 25);
            _biographyControls = new Selectable[] { back, story, preview, close };
            _biographyOverlay = root.gameObject;
            _biographyOverlay.SetActive(false);

            root = CreateOverlayBackdrop(parent, "AppearancePortraitOverlay");
            box = QdaoUguiFactory.CreateCenteredRect("PortraitFrame", root, 1360, 800);
            box.anchoredPosition = new Vector2(0f, -48f);
            AppearanceUiArt.Frame(box, "PortraitFrameBg", 0, 0, 1360, 800);
            AppearanceUiArt.TitlePlate(box, "PortraitTitlePlate", 400, -24, 560, 114);
            back = AppearanceUiArt.ActionButton(box, "PortraitBack", "返回", 70, 40, 170, 66,
                HandleDismissOverlay, fontSize: 27);
            close = AppearanceUiArt.CloseButton(box, "PortraitClose", 1250, 30, 68, HidePanel);
            _portraitPageName = ReadingLabel(box, "PortraitName", "", 440, -6, 480, 62, 38, true);
            _portraitPageName.color = AppearanceUiArt.WarmCream;
            _portraitPageName.fontStyle = FontStyles.Bold;
            _portraitPageImage = QdaoUguiFactory.CreateImage("FullPortrait", box, 300, 118, 760, 564, null);
            _portraitPageImage.preserveAspect = true;
            _portraitPageMissing = ReadingLabel(box, "PortraitMissing", "", 200, 320, 960, 150, 36, true);
            var finish = AppearanceUiArt.ActionButton(box, "PortraitReturn", "返回刚才的位置", 470, 698, 420, 70,
                HandleDismissOverlay, true, 29);
            _portraitControls = new Selectable[] { back, finish, close };
            _portraitOverlay = root.gameObject;
            _portraitOverlay.SetActive(false);
            var storyFrame = _storyOverlay.transform.Find("StoryReaderFrame");
            _storyCloseButton = AppearanceUiArt.CloseButton(storyFrame, "StoryCloseWindow", 1246, 698, 76, HidePanel);
            _storyControls = new Selectable[] { _storyBackBtn, _storyFinishBtn, _storySkipBtn, _storyCloseButton };
            var confirmFrame = _confirmOverlay.transform.Find("ConfirmDialogFrame");
            _readBeforeConfirm = AppearanceUiArt.ActionButton(confirmFrame, "ReadBeforeSwitch", "先读人物志",
                350, 204, 280, 64, HandleReadStoryClicked, fontSize: 27);
            _confirmControls = new Selectable[] { _confirmCancelBtn, _confirmSubmitBtn, _readBeforeConfirm, _confirmCloseBtn };
            ConfigureModalNavigation();
            CaptureReadingLayout();
        }

        private void OpenStoryFromBiography()
        {
            float listPosition = _state.SavedListScrollNormalized;
            if (!_state.OpenStory(listPosition)) return;
            Render();
            if (_storyScroll != null)
            {
                _storyScroll.velocity = Vector2.zero;
                _storyScroll.verticalNormalizedPosition = _state.SavedStoryScrollNormalized;
            }
            FocusFirstControlOfActiveOverlay();
        }

        private void HandleViewPortraitClicked()
        {
            if (FindEntry(_client?.Info, _state.SelectedAppearanceId) == null) return;
            SaveReadingPosition();
            float position = _state.HasOverlay ? _state.SavedListScrollNormalized : CurrentListPosition();
            if (!_state.OpenPortraitPreview(position)) return;
            Render();
            FocusFirstControlOfActiveOverlay();
        }

        private float CurrentListPosition() => _listScroll != null ? _listScroll.verticalNormalizedPosition : 1f;

        private void SaveReadingPosition()
        {
            if (_state.IsStoryOpen && _storyScroll != null)
                _state.SaveStoryPosition(_storyScroll.verticalNormalizedPosition);
        }

        private void RenderReadingOverlays()
        {
            if (_biographyOverlay == null) return;
            if (_readBeforeConfirm != null) _readBeforeConfirm.gameObject.SetActive(_state.TryGetSelectedBiography(out _));
            if (_state.IsBiographyOpen && _state.TryGetSelectedBiography(out var biography))
            {
                _biographyPageName.text = biography.DisplayArchiveName;
                _biographyPageBody.text = biography.BackgroundSummary;
                _biographyStoryTitle.text = biography.StoryTitle + " · 阅读";
                LayoutBiographyBody();
            }
            if (_state.IsPortraitPreviewOpen)
            {
                string name = _state.TryGetSelectedBiography(out var bio)
                    ? bio.DisplayArchiveName : ResolveEntryName(_client?.Info, _state.SelectedAppearanceId);
                _portraitPageName.text = name;
                _portraitPageImage.sprite = LoadExactAppearancePortrait(_state.SelectedAppearanceId);
                _portraitPageImage.enabled = _portraitPageImage.sprite != null;
                _portraitPageMissing.text = _portraitPageImage.sprite == null ? name + "\n形象资源暂不可用" : "";
            }
            _biographyOverlay.SetActive(_state.IsBiographyOpen);
            _portraitOverlay.SetActive(_state.IsPortraitPreviewOpen);
        }

        private void LayoutBiographyBody()
        {
            if (_biographyPageBody == null || _biographyPageScroll == null) return;
            var scrollRect = (RectTransform)_biographyPageScroll.transform;
            float scrollW = scrollRect.sizeDelta.x > 1f ? scrollRect.sizeDelta.x : _biographyPageScroll.viewport.rect.width + 18f;
            float scrollH = scrollRect.sizeDelta.y > 1f ? scrollRect.sizeDelta.y : _biographyPageScroll.viewport.rect.height;
            float viewW = Mathf.Max(100f, scrollW - 18f);
            float width = Mathf.Max(100f, viewW - 24f);
            float height = Mathf.Max(scrollH,
                _biographyPageBody.GetPreferredValues(_biographyPageBody.text, width, 100000).y + 28);
            _biographyPageBody.rectTransform.sizeDelta = new Vector2(width, height);
            _biographyPageScroll.content.sizeDelta = new Vector2(viewW, height);
        }

        private void ConfigureModalNavigation()
        {
            foreach (var controls in new[] { _confirmControls, _noCardControls, _storyControls, _biographyControls, _portraitControls, _seriesEpilogueControls })
            {
                if (controls == null || controls.Length == 0) continue;
                var enabled = new List<Selectable>();
                foreach (var item in controls)
                    if (item != null && item.gameObject.activeSelf && item.interactable) enabled.Add(item);
                for (int i = 0; i < enabled.Count; i++)
                {
                    var control = enabled[i];
                    control.navigation = new Navigation
                    {
                        mode = Navigation.Mode.Explicit,
                        selectOnDown = enabled[(i + 1) % enabled.Count],
                        selectOnRight = enabled[(i + 1) % enabled.Count],
                        selectOnUp = enabled[(i + enabled.Count - 1) % enabled.Count],
                        selectOnLeft = enabled[(i + enabled.Count - 1) % enabled.Count]
                    };
                }
            }
        }

        private void ReleaseReadingPortraits()
        {
            ClearPreviewPortrait();
            if (_portraitPageImage != null) { _portraitPageImage.sprite = null; _portraitPageImage.enabled = false; }
            if (_confirmOldPortrait != null) _confirmOldPortrait.sprite = null;
            if (_confirmNewPortrait != null) _confirmNewPortrait.sprite = null;
            foreach (var row in _rows)
            {
                row.LoadedAvatarId = "";
                if (row.AvatarImage != null) { row.AvatarImage.sprite = null; row.AvatarImage.enabled = false; }
            }
            foreach (var lease in _portraitLeases.Values) lease?.Dispose();
            _portraitLeases.Clear();
        }

        private bool ObserveReadingSession()
        {
            if (_externalVerificationClient) return true;
            var connection = _game?.GateConnectionIdentity;
            ulong player = _game?.PlayerId ?? 0;
            if (ReferenceEquals(connection, _readingConnection) && player == _readingPlayerId) return true;
            _readingConnection = connection;
            _readingPlayerId = player;
            HidePanel();
            _state.ResetForRoleChange();
            return false;
        }

        private static TMP_Text ReadingLabel(UnityEngine.Transform parent, string name, string text,
            float x, float y, float width, float height, float fontSize, bool center = false)
        {
            var label = MailUiArt.Label(parent, name, text, x, y, width, height, fontSize,
                AppearanceUiArt.JadeInk, true,
                align: center ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft);
            return label;
        }
    }
}
