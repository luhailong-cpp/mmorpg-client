using System;
using System.Collections.Generic;
using MmorpgClient.Game.PetFlip;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.PetFlip
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 灵宠翻牌道家 Q 版 uGUI 视图层。
    /// 严格遵循逻辑唯一真相：仅依据 <see cref="PetFlipSnapshot"/> 与 <see cref="PetFlipResolvedAssets"/> 渲染，
    /// 不自行洗牌、判定配对、计次或写纪录；横屏（1922×818，4×3）与窄竖屏（1024×1536，3×4）共享同一组 12 张牌位组件，
    /// 旋转方向只重排几何不改变牌的身份与进度。
    /// </summary>
    public sealed class PetFlipPanelView
    {
        private const float FlipAnimDurationSeconds = 0.12f;
        private const float MatchBounceDurationSeconds = 0.22f;

        private sealed class CardSlotView
        {
            public int SlotIndex;
            public RectTransform Root;
            public Button Button;
            public Image HitArea;
            public Image BackImage;
            public RectTransform FrontGroup;
            public Image FrontFrame;
            public RectTransform PortraitMaskRect;
            public Image PortraitImage;
            public RectTransform NamePlateRect;
            public TMP_Text NameText;
            public RectTransform MatchedMarkRect;
            public Image MatchedMarkImage;
        }

        private sealed class MedallionView
        {
            public RectTransform Root;
            public Image PaperFill;
            public RectTransform PortraitMaskRect;
            public Image PortraitImage;
            public Image RingImage;
            public TMP_Text NameText;
        }

        private sealed class PillButtonView
        {
            public RectTransform Root;
            public Button Button;
            public Image Background;
            public TMP_Text Label;
        }

        public event Action StartClicked;
        public event Action PlayAgainClicked;
        public event Action RetryLoadClicked;
        public event Action CloseClicked;
        public event Action<int> CardSlotClicked;
        public event Action<int> SpiritEyeSlotClicked;
        public event Action<PetFlipDifficulty, PetFlipGameMode> ConfigurationSelected;
        public event Action ReturnToEntryClicked;

        public RectTransform FrameRoot { get; }
        public bool IsPortrait { get; private set; }
        public PetFlipUiPage CurrentPage { get; private set; } = PetFlipUiPage.Loading;

        private readonly Image _frameImage;
        private readonly TMP_Text _titleText;
        private readonly Button _closeButton;
        private readonly TMP_Text _closeLabel;
        private readonly TMP_Text _leftPillText;
        private readonly TMP_Text _rightPillText;

        private readonly Button _primaryButton;
        private readonly Image _primaryHitImage;
        private readonly TMP_Text _primaryLabel;
        private readonly Image _primarySpiritEyeIcon;
        private readonly Button _secondaryButton;
        private readonly Image _secondaryHitImage;
        private readonly TMP_Text _secondaryLabel;

        // ── 子页面容器 ──
        private readonly GameObject _entryRoot;
        private readonly RectTransform _entryLeftNote;
        private readonly TMP_Text _entryLeftTitle;
        private readonly TMP_Text _entryLeftBody;
        private readonly Image _entryPreviewCardBack;
        private readonly RectTransform _entryRightNote;
        private readonly TMP_Text _entryBestTitle;
        private readonly TMP_Text _entryBestValue;
        private readonly TMP_Text _entryBestSub;
        private readonly TMP_Text _entryHelpText;
        private readonly PillButtonView[] _difficultyPills = new PillButtonView[2];
        private readonly PillButtonView[] _modePills = new PillButtonView[3];

        private readonly GameObject _boardRoot;
        private readonly RectTransform _boardLeftNote;
        private readonly TMP_Text _boardRulesFoot;
        private readonly RectTransform _boardRightNote;
        private readonly TMP_Text _boardBestTitle;
        private readonly TMP_Text _boardBestValue;
        private readonly TMP_Text _boardHintText;
        private readonly TMP_Text _boardUnsavedText;
        private readonly TMP_Text _portraitHintText;
        private readonly CardSlotView[] _cardSlots = new CardSlotView[PetFlipConstants.MaxBoardCardCount];

        private readonly GameObject _completeRoot;
        private readonly TMP_Text _completeHeadline;
        private readonly Image _completePerfectStamp;
        private readonly TMP_Text _completeSummary;
        private readonly TMP_Text _completeUnsavedText;
        private readonly PillButtonView _switchConfigButton;
        private readonly MedallionView[] _medallions = new MedallionView[PetFlipConstants.MaxPairCount];

        private readonly GameObject _statusRoot;
        private readonly RectTransform _statusNote;
        private readonly TMP_Text _statusIcon;
        private readonly TMP_Text _statusTitle;
        private readonly TMP_Text _statusSubtext;

        private readonly List<RealtimeTweener> _activeTweens = new List<RealtimeTweener>();
        private float _inputLockedUntilUnscaledTime;
        private PetFlipResolvedAssets _assets;
        private PetFlipDifficulty _layoutDifficulty = PetFlipDifficulty.Relaxed12;
        private PetFlipDifficulty _selectedDifficulty = PetFlipDifficulty.Relaxed12;
        private PetFlipGameMode _selectedMode = PetFlipGameMode.Classic;
        private ulong _lastRenderedRoundId;
        private bool _spiritEyeArmed;
        private PetFlipSnapshot _lastSnapshot;

        public PetFlipPanelView(RectTransform fittedParent)
        {
            FrameRoot = QdaoUguiFactory.CreateStretch("PetFlipFrame", fittedParent, Vector4.zero);
            _frameImage = FrameRoot.gameObject.AddComponent<Image>();
            _frameImage.type = Image.Type.Simple;
            _frameImage.color = Color.white;
            _frameImage.raycastTarget = true;

            _titleText = CreateCenteredLabel("Title", FrameRoot, PetFlipUiPresentation.TitleText, 48f, PetFlipArtCatalog.PaperText, bold: true);
            _leftPillText = CreateCenteredLabel("LeftPill", FrameRoot, string.Empty, 24f, PetFlipArtCatalog.PaperText, bold: true);
            _rightPillText = CreateCenteredLabel("RightPill", FrameRoot, string.Empty, 24f, PetFlipArtCatalog.JadeText, bold: true);

            _closeButton = CreateOverlayButton("PetFlipClose", FrameRoot, out _closeLabel, out _, "×", 38f, PetFlipArtCatalog.PaperText);
            _closeButton.onClick.AddListener(() => CloseClicked?.Invoke());

            _primaryButton = CreateOverlayButton("PetFlipPrimaryAction", FrameRoot, out _primaryLabel, out _primaryHitImage, PetFlipUiPresentation.StartButtonText, 29f, PetFlipArtCatalog.PaperText);
            _primaryButton.onClick.AddListener(HandlePrimaryClicked);
            var eyeIconRect = QdaoUguiFactory.CreateRect("SpiritEyeIcon", (RectTransform)_primaryButton.transform, 18f, 25f, 36f, 36f);
            _primarySpiritEyeIcon = eyeIconRect.gameObject.AddComponent<Image>();
            _primarySpiritEyeIcon.preserveAspect = true;
            _primarySpiritEyeIcon.raycastTarget = false;
            _primarySpiritEyeIcon.gameObject.SetActive(false);

            _secondaryButton = CreateOverlayButton("PetFlipSecondaryAction", FrameRoot, out _secondaryLabel, out _secondaryHitImage, PetFlipUiPresentation.BackButtonText, 28f, PetFlipArtCatalog.JadeText);
            _secondaryButton.onClick.AddListener(() => CloseClicked?.Invoke());

            // 1. 入口说明页
            _entryRoot = CreateLayer("EntryPage", FrameRoot);
            _entryLeftNote = CreateNoteCard("EntryRulesNote", _entryRoot.transform);
            _entryLeftTitle = CreateCenteredLabel("Title", _entryLeftNote, PetFlipUiPresentation.EntrySubtitleText, 33f, PetFlipArtCatalog.JadeText, bold: true);
            _entryLeftBody = CreateCenteredLabel("Body", _entryLeftNote, PetFlipUiPresentation.EntryDescriptionText, 22f, PetFlipArtCatalog.JadeText, bold: false, wrap: true);
            _entryPreviewCardBack = QdaoUguiFactory.CreateImage("EntryCardBackPreview", _entryRoot.transform, 854f, 231f, 214f, 287f, null);
            _entryPreviewCardBack.preserveAspect = true;
            _entryRightNote = CreateNoteCard("EntryBestNote", _entryRoot.transform);
            _entryBestTitle = CreateCenteredLabel("Title", _entryRightNote, "本机最佳", 32f, PetFlipArtCatalog.JadeText, bold: true);
            _entryBestValue = CreateCenteredLabel("Value", _entryRightNote, PetFlipUiPresentation.NoRecordText, 44f, PetFlipArtCatalog.GoldText, bold: true);
            _entryBestSub = CreateCenteredLabel("Sub", _entryRightNote, PetFlipUiPresentation.EntryCountRuleText, 21f, PetFlipArtCatalog.JadeText, bold: false);
            _entryHelpText = CreateCenteredLabel("EntryHelp", _entryRoot.transform, PetFlipUiPresentation.EntryHelpText, 24f, PetFlipArtCatalog.JadeText, bold: false, wrap: true);

            _difficultyPills[0] = CreatePillButton("DiffRelaxed12", _entryRoot.transform, PetFlipUiPresentation.FormatDifficultyLabel(PetFlipDifficulty.Relaxed12),
                () => ConfigurationSelected?.Invoke(PetFlipDifficulty.Relaxed12, _selectedMode));
            _difficultyPills[1] = CreatePillButton("DiffAdvanced16", _entryRoot.transform, PetFlipUiPresentation.FormatDifficultyLabel(PetFlipDifficulty.Advanced16),
                () => ConfigurationSelected?.Invoke(PetFlipDifficulty.Advanced16, _selectedMode));

            _modePills[0] = CreatePillButton("ModeClassic", _entryRoot.transform, PetFlipUiPresentation.FormatModeLabel(PetFlipGameMode.Classic),
                () => ConfigurationSelected?.Invoke(_selectedDifficulty, PetFlipGameMode.Classic));
            _modePills[1] = CreatePillButton("ModeDaily", _entryRoot.transform, PetFlipUiPresentation.FormatModeLabel(PetFlipGameMode.DailyChallenge),
                () => ConfigurationSelected?.Invoke(_selectedDifficulty, PetFlipGameMode.DailyChallenge));
            _modePills[2] = CreatePillButton("ModeSpiritEye", _entryRoot.transform, PetFlipUiPresentation.FormatModeLabel(PetFlipGameMode.SpiritEye),
                () => ConfigurationSelected?.Invoke(_selectedDifficulty, PetFlipGameMode.SpiritEye));

            // 2. 牌盘页
            _boardRoot = CreateLayer("BoardPage", FrameRoot);
            _boardLeftNote = CreateNoteCard("BoardRulesNote", _boardRoot.transform);
            var boardRulesTitle = CreateCenteredLabel("Title", _boardLeftNote, "玩法", 31f, PetFlipArtCatalog.JadeText, bold: true);
            Place(boardRulesTitle.rectTransform, 20f, 17f, 230f, 52f);
            var boardRulesBody = CreateCenteredLabel("Body", _boardLeftNote, "翻开两张相同灵宠\n就能配成一对", 23f, PetFlipArtCatalog.JadeText, bold: false, wrap: true);
            Place(boardRulesBody.rectTransform, 18f, 80f, 234f, 100f);
            _boardRulesFoot = CreateCenteredLabel("Foot", _boardLeftNote, PetFlipUiPresentation.BoardNoLimitText, 20f, PetFlipArtCatalog.GoldText, bold: false);
            Place(_boardRulesFoot.rectTransform, 20f, 191f, 230f, 43f);

            _boardRightNote = CreateNoteCard("BoardBestNote", _boardRoot.transform);
            _boardBestTitle = CreateCenteredLabel("Title", _boardRightNote, "本机最佳", 31f, PetFlipArtCatalog.JadeText, bold: true);
            Place(_boardBestTitle.rectTransform, 20f, 17f, 230f, 52f);
            _boardBestValue = CreateCenteredLabel("Value", _boardRightNote, PetFlipUiPresentation.NoRecordText, 42f, PetFlipArtCatalog.GoldText, bold: true);
            Place(_boardBestValue.rectTransform, 16f, 82f, 238f, 70f);
            _boardHintText = CreateCenteredLabel("Hint", _boardRightNote, PetFlipUiPresentation.BoardDefaultHintText, 20f, PetFlipArtCatalog.JadeText, bold: false, wrap: true);
            _boardHintText.enableAutoSizing = true;
            _boardHintText.fontSizeMin = 15f;
            _boardHintText.fontSizeMax = 20f;
            Place(_boardHintText.rectTransform, 10f, 152f, 250f, 70f);
            _boardUnsavedText = CreateCenteredLabel("UnsavedNotice", _boardRightNote, PetFlipUiPresentation.UnsavedRecordNoticeText, 18f, PetFlipArtCatalog.WarnText, bold: true);
            Place(_boardUnsavedText.rectTransform, 10f, 222f, 250f, 28f);
            _boardUnsavedText.gameObject.SetActive(false);

            _portraitHintText = CreateCenteredLabel("PortraitBoardHint", _boardRoot.transform, PetFlipUiPresentation.PortraitDefaultHintText, 25f, PetFlipArtCatalog.JadeText, bold: false, wrap: true);
            _portraitHintText.enableAutoSizing = true;
            _portraitHintText.fontSizeMin = 18f;
            _portraitHintText.fontSizeMax = 25f;

            for (int i = 0; i < PetFlipConstants.MaxBoardCardCount; i++)
                _cardSlots[i] = BuildCardSlot(_boardRoot.transform, i);

            // 3. 完成结果页
            _completeRoot = CreateLayer("CompletePage", FrameRoot);
            _completeHeadline = CreateCenteredLabel("Headline", _completeRoot.transform, string.Empty, 42f, PetFlipArtCatalog.JadeText, bold: true, wrap: true);
            var perfectStampRect = QdaoUguiFactory.CreateRect("PerfectStamp", _completeRoot.transform, 1440f, 212f, 112f, 112f);
            _completePerfectStamp = perfectStampRect.gameObject.AddComponent<Image>();
            _completePerfectStamp.preserveAspect = true;
            _completePerfectStamp.raycastTarget = false;
            _completePerfectStamp.gameObject.SetActive(false);
            _completeSummary = CreateCenteredLabel("Summary", _completeRoot.transform, string.Empty, 31f, PetFlipArtCatalog.GoldText, bold: true);
            _completeUnsavedText = CreateCenteredLabel("CompleteUnsaved", _completeRoot.transform, PetFlipUiPresentation.UnsavedRecordNoticeText, 24f, PetFlipArtCatalog.WarnText, bold: true);
            _completeUnsavedText.gameObject.SetActive(false);
            _switchConfigButton = CreatePillButton("SwitchConfigButton", _completeRoot.transform, PetFlipUiPresentation.SwitchConfigButtonText,
                () => ReturnToEntryClicked?.Invoke());

            for (int i = 0; i < PetFlipConstants.MaxPairCount; i++)
                _medallions[i] = BuildMedallion(_completeRoot.transform, i);

            // 4. 资源加载中 / 加载失败页
            _statusRoot = CreateLayer("StatusPage", FrameRoot);
            _statusNote = CreateNoteCard("StatusNote", _statusRoot.transform);
            _statusIcon = CreateCenteredLabel("Icon", _statusNote, "！", 75f, PetFlipArtCatalog.GoldText, bold: true);
            _statusTitle = CreateCenteredLabel("Title", _statusNote, PetFlipUiPresentation.LoadingText, 40f, PetFlipArtCatalog.JadeText, bold: true, wrap: true);
            _statusSubtext = CreateCenteredLabel("Subtext", _statusNote, string.Empty, 25f, PetFlipArtCatalog.JadeText, bold: false, wrap: true);

            ApplyOrientationLayout(isPortrait: false);
        }

        public void SetAssets(PetFlipResolvedAssets assets)
        {
            _assets = assets;
            if (assets == null)
                return;

            _frameImage.sprite = IsPortrait ? assets.WindowFramePortrait : assets.WindowFrameLandscape;
            _entryPreviewCardBack.sprite = assets.CardBack;
            _primarySpiritEyeIcon.sprite = assets.SpiritEyeIcon;
            _completePerfectStamp.sprite = assets.StampPerfect;

            for (int i = 0; i < _cardSlots.Length; i++)
            {
                _cardSlots[i].BackImage.sprite = assets.CardBack;
                _cardSlots[i].FrontFrame.sprite = assets.CardFront;
                _cardSlots[i].MatchedMarkImage.sprite = assets.MatchedMark;
            }

            for (int i = 0; i < _medallions.Length; i++)
            {
                PetFlipResolvedFace face = assets.GetFace((SpiritPetCardPattern)i);
                if (face == null) continue;
                _medallions[i].PortraitImage.sprite = face.Portrait;
                _medallions[i].NameText.text = face.DisplayName;
            }
        }

        /// <summary>切换横竖屏几何排版；只改变位置与尺寸，绝不改动牌位归属与逻辑状态。</summary>
        public void ApplyOrientationLayout(bool isPortrait)
        {
            IsPortrait = isPortrait;
            CancelAnimations();

            if (_assets != null)
                _frameImage.sprite = isPortrait ? _assets.WindowFramePortrait : _assets.WindowFrameLandscape;

            if (isPortrait)
            {
                Place(_titleText.rectTransform, 318f, 64f, 390f, 96f);
                _titleText.fontSize = 54f;
                Place((RectTransform)_closeButton.transform, 864f, 66f, 104f, 92f);
                _closeLabel.fontSize = 46f;
                Place(_leftPillText.rectTransform, 211f, 202f, 245f, 68f);
                _leftPillText.fontSize = 26f;
                Place(_rightPillText.rectTransform, 575f, 202f, 238f, 68f);
                _rightPillText.fontSize = 25f;

                Place((RectTransform)_primaryButton.transform, 186f, 1388f, 304f, 83f);
                _primaryLabel.fontSize = 30f;
                Place((RectTransform)_secondaryButton.transform, 538f, 1388f, 310f, 83f);
                _secondaryLabel.fontSize = 29f;

                // 入口页竖排（含 V2 难度/模式选择胶囊）
                Place(_entryPreviewCardBack.rectTransform, 424f, 292f, 176f, 236f);
                Place(_difficultyPills[0].Root, 152f, 546f, 340f, 54f);
                Place(_difficultyPills[1].Root, 532f, 546f, 340f, 54f);
                Place(_modePills[0].Root, 152f, 612f, 228f, 54f);
                Place(_modePills[1].Root, 398f, 612f, 228f, 54f);
                Place(_modePills[2].Root, 644f, 612f, 228f, 54f);
                Place(_entryLeftNote, 142f, 684f, 740f, 196f);
                Place(_entryLeftTitle.rectTransform, 24f, 18f, 692f, 48f);
                Place(_entryLeftBody.rectTransform, 32f, 74f, 676f, 108f);
                Place(_entryRightNote, 142f, 898f, 740f, 196f);
                Place(_entryBestTitle.rectTransform, 24f, 16f, 692f, 46f);
                Place(_entryBestValue.rectTransform, 24f, 66f, 692f, 72f);
                Place(_entryBestSub.rectTransform, 24f, 144f, 692f, 40f);
                Place(_entryHelpText.rectTransform, 112f, 1112f, 800f, 96f);

                // 牌盘页：隐藏横屏左右便签，显示底部竖屏提示条
                _boardLeftNote.gameObject.SetActive(false);
                _boardRightNote.gameObject.SetActive(false);
                _portraitHintText.gameObject.SetActive(true);
                Place(_portraitHintText.rectTransform, 112f, 1244f, 800f, 84f);

                // 完成页竖排
                Place(_completeHeadline.rectTransform, 102f, 320f, 820f, 150f);
                _completeHeadline.fontSize = 42f;
                Place(_completePerfectStamp.rectTransform, 796f, 294f, 112f, 112f);
                Place(_completeSummary.rectTransform, 112f, 1020f, 800f, 72f);
                _completeSummary.fontSize = 34f;
                Place(_completeUnsavedText.rectTransform, 112f, 1106f, 800f, 52f);
                Place(_switchConfigButton.Root, 352f, 1188f, 320f, 58f);

                // 加载/失败页竖排
                Place(_statusNote, 132f, 470f, 760f, 440f);
                Place(_statusIcon.rectTransform, 315f, 32f, 130f, 118f);
                Place(_statusTitle.rectTransform, 48f, 166f, 664f, 92f);
                Place(_statusSubtext.rectTransform, 48f, 276f, 664f, 84f);
            }
            else
            {
                Place(_titleText.rectTransform, 706f, 41f, 510f, 87f);
                _titleText.fontSize = 48f;
                Place((RectTransform)_closeButton.transform, 1697f, 39f, 88f, 88f);
                _closeLabel.fontSize = 38f;
                Place(_leftPillText.rectTransform, 660f, 160f, 200f, 54f);
                _leftPillText.fontSize = 24f;
                Place(_rightPillText.rectTransform, 1065f, 160f, 193f, 54f);
                _rightPillText.fontSize = 23f;

                Place((RectTransform)_primaryButton.transform, 648f, 688f, 300f, 90f);
                _primaryLabel.fontSize = 29f;
                Place((RectTransform)_secondaryButton.transform, 985f, 688f, 292f, 90f);
                _secondaryLabel.fontSize = 28f;

                // 入口页横排（居中预留 V2 难度/模式双排胶囊，与左右便签零重叠）
                Place(_entryLeftNote, 286f, 293f, 295f, 238f);
                Place(_entryLeftTitle.rectTransform, 20f, 19f, 255f, 56f);
                Place(_entryLeftBody.rectTransform, 18f, 82f, 259f, 134f);
                Place(_entryPreviewCardBack.rectTransform, 868f, 216f, 186f, 248f);
                Place(_difficultyPills[0].Root, 763f, 476f, 190f, 42f);
                Place(_difficultyPills[1].Root, 969f, 476f, 190f, 42f);
                Place(_modePills[0].Root, 677f, 526f, 180f, 42f);
                Place(_modePills[1].Root, 871f, 526f, 180f, 42f);
                Place(_modePills[2].Root, 1065f, 526f, 180f, 42f);
                Place(_entryRightNote, 1347f, 293f, 295f, 238f);
                Place(_entryBestTitle.rectTransform, 22f, 23f, 250f, 53f);
                Place(_entryBestValue.rectTransform, 22f, 87f, 250f, 75f);
                Place(_entryBestSub.rectTransform, 22f, 169f, 250f, 42f);
                Place(_entryHelpText.rectTransform, 480f, 580f, 962f, 52f);

                // 牌盘页：左右便签
                _boardLeftNote.gameObject.SetActive(true);
                _boardRightNote.gameObject.SetActive(true);
                Place(_boardLeftNote, 256f, 310f, 270f, 254f);
                Place(_boardRightNote, 1393f, 310f, 270f, 254f);
                _portraitHintText.gameObject.SetActive(false);

                // 完成页横排
                Place(_completeHeadline.rectTransform, 320f, 238f, 1282f, 78f);
                _completeHeadline.fontSize = 42f;
                Place(_completePerfectStamp.rectTransform, 1460f, 212f, 112f, 112f);
                Place(_completeSummary.rectTransform, 420f, 540f, 1082f, 58f);
                _completeSummary.fontSize = 31f;
                Place(_completeUnsavedText.rectTransform, 511f, 596f, 900f, 38f);
                Place(_switchConfigButton.Root, 831f, 636f, 260f, 42f);

                // 加载/失败页横排
                Place(_statusNote, 575f, 250f, 774f, 376f);
                Place(_statusIcon.rectTransform, 320f, 22f, 130f, 118f);
                Place(_statusTitle.rectTransform, 80f, 140f, 612f, 68f);
                Place(_statusSubtext.rectTransform, 85f, 224f, 602f, 55f);
            }

            float pillFont = isPortrait ? 24f : 19f;
            for (int i = 0; i < _difficultyPills.Length; i++) _difficultyPills[i].Label.fontSizeMax = pillFont;
            for (int i = 0; i < _modePills.Length; i++) _modePills[i].Label.fontSizeMax = pillFont;
            _switchConfigButton.Label.fontSizeMax = isPortrait ? 25f : 20f;

            ApplyBoardAndMedallionGeometry();
        }

        private void ApplyBoardAndMedallionGeometry()
        {
            int activeCards = PetFlipConstants.GetCardCount(_layoutDifficulty);
            for (int i = 0; i < _cardSlots.Length; i++)
            {
                if (i < activeCards)
                    ApplyCardSlotGeometry(_cardSlots[i], PetFlipUiPresentation.GetCardSlotRect(i, IsPortrait, _layoutDifficulty));
            }

            int activePairs = PetFlipConstants.GetPairCount(_layoutDifficulty);
            for (int i = 0; i < _medallions.Length; i++)
            {
                if (i < activePairs)
                    ApplyMedallionGeometry(_medallions[i], PetFlipUiPresentation.GetCompletionMedallionRect(i, IsPortrait, _layoutDifficulty));
            }
        }

        /// <summary>依据不可变规则快照与资源加载状态整体刷新页面，不修改任何业务逻辑。</summary>
        public void Render(PetFlipSnapshot snapshot, PetFlipAssetLoadState assetState)
        {
            _lastSnapshot = snapshot;
            if (snapshot != null)
            {
                _selectedDifficulty = snapshot.Difficulty;
                _selectedMode = snapshot.Mode;

                if (snapshot.Difficulty != _layoutDifficulty)
                {
                    _layoutDifficulty = snapshot.Difficulty;
                    ApplyBoardAndMedallionGeometry();
                }

                if (snapshot.RoundId != _lastRenderedRoundId || !snapshot.CanUseSpiritEye)
                {
                    _lastRenderedRoundId = snapshot.RoundId;
                    _spiritEyeArmed = false;
                }
            }

            CurrentPage = PetFlipUiPresentation.ResolvePage(snapshot, assetState);

            _entryRoot.SetActive(CurrentPage == PetFlipUiPage.Entry);
            _boardRoot.SetActive(CurrentPage == PetFlipUiPage.Board);
            _completeRoot.SetActive(CurrentPage == PetFlipUiPage.Complete);
            _statusRoot.SetActive(CurrentPage == PetFlipUiPage.Loading || CurrentPage == PetFlipUiPage.LoadFailed);

            _secondaryLabel.text = PetFlipUiPresentation.BackButtonText;
            _secondaryButton.interactable = true;
            _secondaryHitImage.raycastTarget = true;

            switch (CurrentPage)
            {
                case PetFlipUiPage.Loading:
                    _leftPillText.text = "资源载入";
                    _rightPillText.text = "请稍候";
                    _statusIcon.text = "…";
                    _statusTitle.text = PetFlipUiPresentation.LoadingText;
                    _statusSubtext.text = "请勿重复开始";
                    SetPrimaryAction("载入中…", interactive: false);
                    break;

                case PetFlipUiPage.LoadFailed:
                    _leftPillText.text = "资源载入";
                    _rightPillText.text = "尚未开始";
                    _statusIcon.text = "！";
                    _statusTitle.text = PetFlipUiPresentation.LoadFailedTitleText;
                    _statusSubtext.text = PetFlipUiPresentation.LoadFailedSubtext;
                    SetPrimaryAction(PetFlipUiPresentation.RetryButtonText, interactive: true);
                    break;

                case PetFlipUiPage.Entry:
                    RenderEntry(snapshot);
                    break;

                case PetFlipUiPage.Board:
                    RenderBoard(snapshot);
                    break;

                case PetFlipUiPage.Complete:
                    RenderComplete(snapshot);
                    break;
            }
        }

        private void RenderEntry(PetFlipSnapshot snapshot)
        {
            PetFlipDifficulty diff = snapshot != null ? snapshot.Difficulty : PetFlipDifficulty.Relaxed12;
            PetFlipGameMode mode = snapshot != null ? snapshot.Mode : PetFlipGameMode.Classic;
            bool canConfig = snapshot != null && snapshot.CanChangeConfiguration;

            _leftPillText.text = PetFlipUiPresentation.FormatDifficultyPill(diff);
            _rightPillText.text = mode == PetFlipGameMode.DailyChallenge
                ? PetFlipUiPresentation.FormatDailyDateLabel(snapshot.DailyDateKey)
                : PetFlipUiPresentation.FormatModeLabel(mode);
            _entryLeftBody.text = PetFlipUiPresentation.FormatEntryDescription(diff, mode);
            _entryBestTitle.text = PetFlipUiPresentation.FormatBestTitle(mode);
            _entryBestValue.text = PetFlipUiPresentation.FormatBestText(snapshot?.LocalRecord);

            UpdatePillState(_difficultyPills[0], PetFlipUiPresentation.FormatDifficultyLabel(PetFlipDifficulty.Relaxed12),
                diff == PetFlipDifficulty.Relaxed12, canConfig);
            UpdatePillState(_difficultyPills[1], PetFlipUiPresentation.FormatDifficultyLabel(PetFlipDifficulty.Advanced16),
                diff == PetFlipDifficulty.Advanced16, canConfig);

            UpdatePillState(_modePills[0], PetFlipUiPresentation.FormatModeLabel(PetFlipGameMode.Classic),
                mode == PetFlipGameMode.Classic, canConfig);
            UpdatePillState(_modePills[1], PetFlipUiPresentation.FormatModeLabel(PetFlipGameMode.DailyChallenge),
                mode == PetFlipGameMode.DailyChallenge, canConfig);
            UpdatePillState(_modePills[2], PetFlipUiPresentation.FormatModeLabel(PetFlipGameMode.SpiritEye),
                mode == PetFlipGameMode.SpiritEye, canConfig);

            bool canStart = snapshot != null && snapshot.HasActiveSession && !snapshot.ShouldCollapsePanel;
            SetPrimaryAction(PetFlipUiPresentation.StartButtonText, interactive: canStart);
        }

        /// <summary>
        /// 仅在逻辑已同步更新后播放轻巧视觉反馈；绝不在动画结束回调中计次、配对或保存纪录。
        /// 翻牌过渡期短暂拦截连点穿透，不缓存待执行点击队列。
        /// </summary>
        public void PlayVisualFeedback(PetFlipActionResult action)
        {
            if (action == null || !action.Accepted)
                return;

            switch (action.Outcome)
            {
                case PetFlipActionOutcome.FirstCardFlipped:
                case PetFlipActionOutcome.MismatchClearedAndFirstCardFlipped:
                case PetFlipActionOutcome.SpiritEyePeeked:
                case PetFlipActionOutcome.SpiritEyeClearedAndFirstCardFlipped:
                case PetFlipActionOutcome.PairMismatched:
                    if (action.FlippedSlotIndex >= 0 && action.FlippedSlotIndex < _cardSlots.Length)
                    {
                        _inputLockedUntilUnscaledTime = Time.unscaledTime + FlipAnimDurationSeconds;
                        AnimateCardReveal(_cardSlots[action.FlippedSlotIndex].Root);
                    }
                    break;

                case PetFlipActionOutcome.PairMatched:
                case PetFlipActionOutcome.RoundCompleted:
                    _inputLockedUntilUnscaledTime = Time.unscaledTime + FlipAnimDurationSeconds;
                    if (action.FirstHandSlotIndex >= 0 && action.FirstHandSlotIndex < _cardSlots.Length)
                        AnimateCardMatchBounce(_cardSlots[action.FirstHandSlotIndex].Root, action.CurrentStreak);
                    if (action.SecondHandSlotIndex >= 0 && action.SecondHandSlotIndex < _cardSlots.Length)
                        AnimateCardMatchBounce(_cardSlots[action.SecondHandSlotIndex].Root, action.CurrentStreak);
                    break;
            }
        }

        /// <summary>取消全部进行中动画、复位牌面缩放、退出灵目待选态并释放输入锁（关闭/暂停/销毁/方向切换时必调）。</summary>
        public void CancelAnimations()
        {
            for (int i = 0; i < _activeTweens.Count; i++)
                _activeTweens[i]?.Kill();
            _activeTweens.Clear();
            _inputLockedUntilUnscaledTime = 0f;
            _spiritEyeArmed = false;

            for (int i = 0; i < _cardSlots.Length; i++)
            {
                if (_cardSlots[i]?.Root != null)
                    _cardSlots[i].Root.localScale = Vector3.one;
            }
        }

        private void RenderBoard(PetFlipSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _leftPillText.text = PetFlipUiPresentation.FormatPairCounter(snapshot.MatchedPairCount, snapshot.TotalPairCount);
            _rightPillText.text = PetFlipUiPresentation.FormatFlipCounter(snapshot.FlipCount);
            _boardBestTitle.text = PetFlipUiPresentation.FormatBestTitle(snapshot.Mode);
            _boardBestValue.text = PetFlipUiPresentation.FormatBestText(snapshot.LocalRecord);
            _boardRulesFoot.text = snapshot.Mode == PetFlipGameMode.DailyChallenge
                ? PetFlipUiPresentation.FormatDailyDateLabel(snapshot.DailyDateKey)
                : (snapshot.Mode == PetFlipGameMode.SpiritEye ? "免费灵目 · 不限时" : PetFlipUiPresentation.BoardNoLimitText);

            bool showUnsaved = PetFlipUiPresentation.ShouldShowUnsavedNotice(snapshot);
            _boardHintText.text = PetFlipUiPresentation.FormatBoardHint(snapshot, isPortrait: false, spiritEyeArmed: _spiritEyeArmed);
            _boardUnsavedText.gameObject.SetActive(showUnsaved);

            string portraitHint = PetFlipUiPresentation.FormatBoardHint(snapshot, isPortrait: true, spiritEyeArmed: _spiritEyeArmed);
            _portraitHintText.text = showUnsaved
                ? portraitHint + "  ·  " + PetFlipUiPresentation.UnsavedRecordNoticeText
                : portraitHint;

            // 经典/今日灵签底部左牌严格只作“本局不限时”不可点击说明牌；灵目模式复用该牌为免费窥牌开关。
            if (snapshot.Mode == PetFlipGameMode.SpiritEye)
            {
                if (snapshot.CanUseSpiritEye)
                {
                    string spiritLabel = _spiritEyeArmed
                        ? "取消灵目·点暗牌"
                        : $"灵目窥牌 ({snapshot.RemainingSpiritEyeCharges})";
                    SetPrimaryAction(spiritLabel, interactive: true);
                }
                else if (snapshot.HandState == PetFlipHandState.SpiritEyePeeking)
                {
                    SetPrimaryAction("灵目窥看中", interactive: false);
                }
                else
                {
                    SetPrimaryAction("灵目已用 · 不限时", interactive: false);
                }
            }
            else
            {
                SetPrimaryAction(PetFlipUiPresentation.BoardNoLimitText, interactive: false);
            }

            for (int i = 0; i < _cardSlots.Length; i++)
            {
                CardSlotView view = _cardSlots[i];
                if (i >= snapshot.Cards.Count)
                {
                    view.Root.gameObject.SetActive(false);
                    continue;
                }

                view.Root.gameObject.SetActive(true);
                PetFlipCardSnapshot card = snapshot.Cards[i];
                bool isFaceUp = card.IsFaceUp && card.VisiblePattern.HasValue;
                bool isSpiritPeek = snapshot.HandState == PetFlipHandState.SpiritEyePeeking
                    && i == snapshot.FirstSelectedSlotIndex;
                PetFlipResolvedFace face = isFaceUp && _assets != null
                    ? _assets.GetFace(card.VisiblePattern.Value)
                    : null;

                view.BackImage.gameObject.SetActive(!isFaceUp);
                view.FrontGroup.gameObject.SetActive(isFaceUp);
                bool usePeekFrame = isSpiritPeek && _assets?.CardSpiritPeekFrame != null;
                if (_assets != null)
                    view.FrontFrame.sprite = usePeekFrame ? _assets.CardSpiritPeekFrame : _assets.CardFront;
                view.FrontFrame.color = isSpiritPeek && !usePeekFrame ? PetFlipArtCatalog.SpiritEyePeekTint : Color.white;
                if (isFaceUp && face != null)
                {
                    view.PortraitImage.sprite = face.Portrait;
                    view.NameText.text = face.DisplayName;
                }

                view.MatchedMarkImage.gameObject.SetActive(card.IsMatched);
                view.Button.interactable = card.IsClickable;
                view.Root.gameObject.name = PetFlipUiPresentation.FormatCardAccessibleName(card, face?.DisplayName, isSpiritPeek);
            }
        }

        private void RenderComplete(PetFlipSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            PetFlipCompletionResult result = snapshot.CompletionResult;
            int flips = result != null ? result.FlipCount : snapshot.FlipCount;
            int totalPairs = snapshot.TotalPairCount;
            _leftPillText.text = PetFlipUiPresentation.FormatPairCounter(totalPairs, totalPairs);
            _rightPillText.text = PetFlipUiPresentation.FormatFlipCounter(flips);

            _completeHeadline.text = PetFlipUiPresentation.FormatCompletionHeadline(result);
            _completePerfectStamp.gameObject.SetActive(result != null && result.IsPerfectRun && _completePerfectStamp.sprite != null);
            _completeSummary.text = PetFlipUiPresentation.FormatCompletionSummary(result);
            _completeUnsavedText.gameObject.SetActive(PetFlipUiPresentation.ShouldShowUnsavedNotice(snapshot));
            _switchConfigButton.Button.interactable = snapshot.CanChangeConfiguration;

            for (int i = 0; i < _medallions.Length; i++)
                _medallions[i].Root.gameObject.SetActive(i < totalPairs);

            SetPrimaryAction(PetFlipUiPresentation.PlayAgainButtonText, interactive: snapshot.CanStartNewRound);
        }

        private void SetPrimaryAction(string label, bool interactive)
        {
            _primaryLabel.text = label;
            _primaryButton.interactable = interactive;
            _primaryHitImage.raycastTarget = interactive;
            bool showSpiritIcon = CurrentPage == PetFlipUiPage.Board
                && _lastSnapshot != null
                && _lastSnapshot.Mode == PetFlipGameMode.SpiritEye
                && _primarySpiritEyeIcon.sprite != null;
            _primarySpiritEyeIcon.gameObject.SetActive(showSpiritIcon);
            _primaryLabel.rectTransform.offsetMin = new Vector2(showSpiritIcon ? 56f : 12f, 0f);
            _primaryLabel.color = interactive
                ? PetFlipArtCatalog.PaperText
                : new Color(PetFlipArtCatalog.PaperText.r, PetFlipArtCatalog.PaperText.g, PetFlipArtCatalog.PaperText.b, 0.88f);
        }

        private void HandlePrimaryClicked()
        {
            switch (CurrentPage)
            {
                case PetFlipUiPage.Entry:
                    StartClicked?.Invoke();
                    break;
                case PetFlipUiPage.Board:
                    if (_lastSnapshot != null && _lastSnapshot.CanUseSpiritEye)
                    {
                        _spiritEyeArmed = !_spiritEyeArmed;
                        RenderBoard(_lastSnapshot);
                    }
                    break;
                case PetFlipUiPage.Complete:
                    PlayAgainClicked?.Invoke();
                    break;
                case PetFlipUiPage.LoadFailed:
                    RetryLoadClicked?.Invoke();
                    break;
            }
        }

        private void HandleCardClicked(int slotIndex)
        {
            // 动画过渡窗内忽略连点穿透，不排队缓存。
            if (Time.unscaledTime < _inputLockedUntilUnscaledTime)
                return;

            if (_spiritEyeArmed && _lastSnapshot != null && _lastSnapshot.CanUseSpiritEye)
            {
                _spiritEyeArmed = false;
                SpiritEyeSlotClicked?.Invoke(slotIndex);
                return;
            }

            CardSlotClicked?.Invoke(slotIndex);
        }

        private void AnimateCardReveal(RectTransform target)
        {
            if (target == null) return;
            target.localScale = new Vector3(0.25f, 1f, 1f);
            var tween = RealtimeTween.To(0.25f, 1f, FlipAnimDurationSeconds)
                .SetTarget(target)
                .SetEase(RealtimeEase.QuadOut)
                .OnUpdate(tw =>
                {
                    if (target != null)
                        target.localScale = new Vector3(tw.Value.x, 1f, 1f);
                });
            _activeTweens.Add(tween);
        }

        private void AnimateCardMatchBounce(RectTransform target, int currentStreak)
        {
            if (target == null) return;
            target.localScale = Vector3.one;
            // 连击越高弹跳略轻快（0.08 -> 0.13 封顶），避免遮挡相邻牌面。
            float amplitude = Mathf.Min(0.13f, 0.08f + Mathf.Max(0, currentStreak - 1) * 0.012f);
            var tween = RealtimeTween.To(0f, 1f, MatchBounceDurationSeconds)
                .SetTarget(target)
                .SetEase(RealtimeEase.SineInOut)
                .OnUpdate(tw =>
                {
                    if (target == null) return;
                    float s = 1f + amplitude * Mathf.Sin(tw.Value.x * Mathf.PI);
                    target.localScale = new Vector3(s, s, 1f);
                });
            _activeTweens.Add(tween);
        }

        private CardSlotView BuildCardSlot(Transform parent, int slotIndex)
        {
            var root = QdaoUguiFactory.CreateRect($"PetFlipCard_{slotIndex}_背面", parent, 0f, 0f, 112f, 144f);
            root.pivot = new Vector2(0.5f, 0.5f);

            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            int capturedSlot = slotIndex;
            button.onClick.AddListener(() => HandleCardClicked(capturedSlot));

            var backGo = QdaoUguiFactory.CreateStretch("CardBack", root, Vector4.zero);
            var backImg = backGo.gameObject.AddComponent<Image>();
            backImg.type = Image.Type.Simple;
            backImg.raycastTarget = false;

            var frontGroup = QdaoUguiFactory.CreateStretch("CardFront", root, Vector4.zero);
            var frontFrame = frontGroup.gameObject.AddComponent<Image>();
            frontFrame.type = Image.Type.Simple;
            frontFrame.raycastTarget = false;

            // 椭圆头像裁切区（对齐 build.ps1 Draw-PetCard 比例：x+0.19w, y+0.31h, 0.62w, 0.43h）
            var maskRect = QdaoUguiFactory.CreateRect("PortraitMask", frontGroup, 0f, 0f, 68f, 62f);
            var maskImg = maskRect.gameObject.AddComponent<Image>();
            maskImg.sprite = PetFlipArtCatalog.GetCircleMaskSprite();
            maskImg.raycastTarget = false;
            var mask = maskRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var portraitRect = QdaoUguiFactory.CreateStretch("Portrait", maskRect, new Vector4(-4f, -4f, -4f, -4f));
            var portraitImg = portraitRect.gameObject.AddComponent<Image>();
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;

            // 底部名称铭牌（对齐 build.ps1：x+0.13w, y+0.795h, 0.74w, 0.16h）
            var namePlateRect = QdaoUguiFactory.CreateRect("NamePlate", frontGroup, 0f, 0f, 82f, 23f);
            var namePlateImg = namePlateRect.gameObject.AddComponent<Image>();
            namePlateImg.sprite = PetFlipArtCatalog.GetRoundedLabelSprite();
            namePlateImg.type = Image.Type.Sliced;
            namePlateImg.raycastTarget = false;

            var nameText = CreateCenteredLabel("PetName", namePlateRect, string.Empty, 14f, PetFlipArtCatalog.CardLabelText, bold: true);
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 10f;
            nameText.fontSizeMax = 20f;
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(3f, 0f);
            nameRect.offsetMax = new Vector2(-3f, 0f);

            // 右上角已配对勾形徽章（对齐 build.ps1：side=0.21h, x+w-side-3, y+6）
            var markRect = QdaoUguiFactory.CreateRect("MatchedMark", frontGroup, 0f, 0f, 30f, 30f);
            var markImg = markRect.gameObject.AddComponent<Image>();
            markImg.preserveAspect = true;
            markImg.raycastTarget = false;
            markImg.gameObject.SetActive(false);

            frontGroup.gameObject.SetActive(false);

            return new CardSlotView
            {
                SlotIndex = slotIndex,
                Root = root,
                Button = button,
                HitArea = hit,
                BackImage = backImg,
                FrontGroup = frontGroup,
                FrontFrame = frontFrame,
                PortraitMaskRect = maskRect,
                PortraitImage = portraitImg,
                NamePlateRect = namePlateRect,
                NameText = nameText,
                MatchedMarkRect = markRect,
                MatchedMarkImage = markImg
            };
        }

        private static void ApplyCardSlotGeometry(CardSlotView slot, PetFlipPixelRect rect)
        {
            // 卡牌使用中心 pivot 以便缩放弹跳居中，需按中心点换算左上坐标。
            slot.Root.anchorMin = new Vector2(0f, 1f);
            slot.Root.anchorMax = new Vector2(0f, 1f);
            slot.Root.pivot = new Vector2(0.5f, 0.5f);
            slot.Root.sizeDelta = new Vector2(rect.Width, rect.Height);
            slot.Root.anchoredPosition = new Vector2(rect.X + rect.Width * 0.5f, -(rect.Y + rect.Height * 0.5f));
            slot.Root.localScale = Vector3.one;

            Place(slot.PortraitMaskRect, rect.Width * 0.19f, rect.Height * 0.31f, rect.Width * 0.62f, rect.Height * 0.43f);
            Place(slot.NamePlateRect, rect.Width * 0.13f, rect.Height * 0.795f, rect.Width * 0.74f, rect.Height * 0.16f);
            slot.NameText.fontSizeMax = Mathf.Max(12f, rect.Height * 0.095f);

            float markSide = rect.Height * 0.21f;
            Place(slot.MatchedMarkRect, rect.Width - markSide - 3f, 6f, markSide, markSide);
        }

        private static MedallionView BuildMedallion(Transform parent, int index)
        {
            var root = QdaoUguiFactory.CreateRect($"CompletePet_{index}", parent, 0f, 0f, 112f, 152f);

            var paperRect = QdaoUguiFactory.CreateRect("PaperCircle", root, 0f, 0f, 112f, 112f);
            var paperFill = paperRect.gameObject.AddComponent<Image>();
            paperFill.sprite = PetFlipArtCatalog.GetCircleMaskSprite();
            paperFill.color = PetFlipArtCatalog.CirclePaperFill;
            paperFill.raycastTarget = false;

            var maskRect = QdaoUguiFactory.CreateRect("PortraitMask", root, 2f, 2f, 108f, 108f);
            var maskImg = maskRect.gameObject.AddComponent<Image>();
            maskImg.sprite = PetFlipArtCatalog.GetCircleMaskSprite();
            maskImg.raycastTarget = false;
            var mask = maskRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var portraitRect = QdaoUguiFactory.CreateStretch("Portrait", maskRect, Vector4.zero);
            var portraitImg = portraitRect.gameObject.AddComponent<Image>();
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;

            var ringRect = QdaoUguiFactory.CreateRect("GoldRing", root, 0f, 0f, 112f, 112f);
            var ringImg = ringRect.gameObject.AddComponent<Image>();
            ringImg.sprite = PetFlipArtCatalog.GetCircleRingSprite();
            ringImg.raycastTarget = false;

            var nameText = CreateCenteredLabel("Name", root, string.Empty, 19f, PetFlipArtCatalog.JadeText, bold: true);
            Place(nameText.rectTransform, -14f, 118f, 140f, 36f);

            return new MedallionView
            {
                Root = root,
                PaperFill = paperFill,
                PortraitMaskRect = maskRect,
                PortraitImage = portraitImg,
                RingImage = ringImg,
                NameText = nameText
            };
        }

        private static void ApplyMedallionGeometry(MedallionView view, PetFlipPixelRect rect)
        {
            float side = rect.Width;
            Place(view.Root, rect.X, rect.Y, side, side + 44f);
            Place(view.PaperFill.rectTransform, 0f, 0f, side, side);
            Place(view.PortraitMaskRect, 2f, 2f, side - 4f, side - 4f);
            Place(view.RingImage.rectTransform, 0f, 0f, side, side);
            Place(view.NameText.rectTransform, -14f, side + 6f, side + 28f, 38f);
            view.NameText.fontSize = side >= 140f ? 24f : 19f;
        }

        private static GameObject CreateLayer(string name, Transform parent)
        {
            var rect = QdaoUguiFactory.CreateStretch(name, parent, Vector4.zero);
            return rect.gameObject;
        }

        private static RectTransform CreateNoteCard(string name, Transform parent)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, 0f, 0f, 270f, 240f);
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = PetFlipArtCatalog.GetRoundedNoteSprite();
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return rect;
        }

        private static TextMeshProUGUI CreateCenteredLabel(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Color color,
            bool bold = false,
            bool wrap = false)
        {
            var label = QdaoUguiFactory.CreateText(name, parent, 0f, 0f, 100f, 40f, text, fontSize, color, TextAlignmentOptions.Center);
            label.richText = false;
            label.raycastTarget = false;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (bold)
            {
                label.fontStyle = FontStyles.Bold;
                label.fontWeight = FontWeight.Bold;
            }
            return label;
        }

        private static Button CreateOverlayButton(
            string name,
            Transform parent,
            out TMP_Text label,
            out Image hitImage,
            string text,
            float fontSize,
            Color textColor)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, 0f, 0f, 300f, 90f);
            hitImage = rect.gameObject.AddComponent<Image>();
            // 按钮底板已由窗体框架绘制；此处放轻透点击反馈层，按下时微暗即可。
            hitImage.color = new Color(1f, 1f, 1f, 0.004f);
            hitImage.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hitImage;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.004f);
            colors.highlightedColor = new Color(1f, 0.96f, 0.82f, 0.10f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.10f, 0.22f, 0.16f, 0.18f);
            colors.disabledColor = new Color(0f, 0f, 0f, 0f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var labelObj = CreateCenteredLabel("Label", rect, text, fontSize, textColor, bold: true);
            var lr = labelObj.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(12f, 0f);
            lr.offsetMax = new Vector2(-12f, 0f);
            label = labelObj;
            return button;
        }

        private static PillButtonView CreatePillButton(string name, Transform parent, string text, Action onClick)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, 0f, 0f, 180f, 42f);
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = PetFlipArtCatalog.GetNormalPillSprite();
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = bg.sprite != null && bg.sprite.rect.height >= 72f ? 2f : 1f;
            bg.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var label = CreateCenteredLabel("Label", rect, text, 19f, PetFlipArtCatalog.JadeText, bold: true);
            label.enableAutoSizing = true;
            label.fontSizeMin = 13f;
            label.fontSizeMax = 24f;
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 2f);
            lr.offsetMax = new Vector2(-8f, -2f);

            return new PillButtonView { Root = rect, Button = button, Background = bg, Label = label };
        }

        private static void UpdatePillState(PillButtonView pill, string baseLabel, bool selected, bool interactable)
        {
            if (pill == null) return;
            pill.Background.sprite = selected
                ? PetFlipArtCatalog.GetSelectedPillSprite()
                : PetFlipArtCatalog.GetNormalPillSprite();
            pill.Background.pixelsPerUnitMultiplier =
                pill.Background.sprite != null && pill.Background.sprite.rect.height >= 72f ? 2f : 1f;
            pill.Label.color = selected ? PetFlipArtCatalog.PaperText : PetFlipArtCatalog.JadeText;
            // 非色相勾选前缀，确保色觉障碍玩家同样清晰辨识当前选中档位/模式。
            pill.Label.text = selected ? "✓ " + baseLabel : baseLabel;
            pill.Button.interactable = interactable;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }
    }
}
