using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Tournament;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Tournament
{
    /// <summary>
    /// 《群仙问道赛》V2 模块 C · 实时观战底部喝彩条与顶部赛况/灵脉条：
    /// - 仅在 BattleUiRoot.IsSpectating 且 BattleStateS2C.TournamentContext.MatchId != 0 时显示；
    /// - 底部喝彩条严格遵循 UNITY-HANDOFF：单行横向布局、逻辑高度 <= 76px、底部安全区锚定、避让右侧「退出观战」；
    /// - phrase-01..06 为包含胶囊底色与毛笔字的完整按钮图，绝不叠字；c-cheer-title 与 cheer-float 同理绝不叠字；
    /// - 人气对推条严格绑定服务端 CountA/CountB（0:0 中性均衡，绝不在本地伪造累计）；
    /// - 仅在可见时按 2.5s 周期增量拉取 GetTournamentSpectateCheer，退出/切号/销毁时对称退订并清理动效。
    /// </summary>
    internal sealed class TournamentSpectateCheerOverlay
    {
        private const float DesignW = TournamentArtCatalog.DesignWidth;   // 1920
        private const float DesignH = TournamentArtCatalog.DesignHeight;  // 1080
        private const float BarMaxWidth = 1520f;
        private const float BarHeight = 72f; // 严格 <= 76px
        private const float PollIntervalSec = 2.5f;
        private const float FloatDurationSec = 2.4f;
        private const int MaxActiveFloats = 3;

        private sealed class ActiveFloatItem
        {
            public GameObject Go;
            public RectTransform Rect;
            public CanvasGroup Group;
            public float StartY;
            public float CreatedAt;
        }

        private readonly Canvas _canvas;
        private readonly RectTransform _safeRoot;
        private readonly RectTransform _topBanner;
        private readonly TMP_Text _topScoreLabel;
        private readonly Image _topEmblemA;
        private readonly Image _topEmblemB;
        private readonly RectTransform _topWarmupBadge;
        private readonly Image _topLeylineIcon;
        private readonly Button _topLeylineBtn;
        private readonly RectTransform _leylineTooltip;
        private readonly TMP_Text _leylineTooltipText;

        private readonly RectTransform _barRoot;
        private readonly Button _sideToggleBtn;
        private readonly Image _sideToggleBg;
        private readonly TMP_Text _sideToggleText;
        private readonly Button[] _phraseButtons = new Button[6];
        private readonly Image[] _phraseImages = new Image[6];
        private readonly Image[] _phraseSelectionRings = new Image[6];
        private readonly Image _jadeFill;
        private readonly Image _coralFill;
        private readonly TMP_Text _popularityLabel;
        private readonly TMP_Text _statusHintLabel;
        private readonly Button _floatToggleBtn;
        private readonly TMP_Text _floatToggleText;
        private readonly RectTransform _floatLayer;

        private readonly List<ActiveFloatItem> _floats = new List<ActiveFloatItem>(MaxActiveFloats);

        private TournamentClient _client;
        private ulong _activeBattleId;
        private ulong _lastRenderedFloatSeq;
        private uint _selectedSide; // 0=A队(玉绿), 1=B队(朱砂)
        private uint _lastClickedPhraseId;
        private bool _floatsEnabled = true;
        private bool _leylineTooltipOpen;
        private float _nextPollAt;
        private float _nextUiRefreshAt;
        private Rect _lastSafeArea;
        private TournamentBattleContext _lastContext;

        public bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

        public TournamentSpectateCheerOverlay(UnityEngine.Transform hostParent)
        {
            var canvasGo = new GameObject("TournamentSpectateCheerCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(hostParent, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 195;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _safeRoot = QdaoUguiFactory.CreateStretch("SafeRoot", canvasGo.transform, Vector4.zero);

            // ── 顶部紧凑赛况与五行灵脉横幅（居中偏上，避开左回合数、中央计时环与右上 140px 小游戏胶囊） ──
            _topBanner = TournamentArtCatalog.BorderedCard("TopTournamentBanner", _safeRoot,
                (DesignW - 720f) * 0.5f, 118f, 720f, 44f,
                new Color(0.97f, 0.94f, 0.87f, 0.94f), TournamentArtCatalog.Gold, 1.5f);
            _topBanner.anchorMin = new Vector2(0.5f, 1f);
            _topBanner.anchorMax = new Vector2(0.5f, 1f);
            _topBanner.pivot = new Vector2(0.5f, 1f);
            _topBanner.anchoredPosition = new Vector2(0f, -118f);
            _topBanner.sizeDelta = new Vector2(720f, 44f);

            _topEmblemA = QdaoUguiFactory.CreateImage("TopEmblemA", _topBanner, 10f, 6f, 32f, 32f, null, false);
            _topEmblemA.preserveAspect = true;
            _topEmblemA.gameObject.SetActive(false);

            _topScoreLabel = TournamentArtCatalog.Label("TopScore", _topBanner, 48f, 4f, 412f, 36f,
                string.Empty, 17f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
            _topScoreLabel.enableAutoSizing = true;
            _topScoreLabel.fontSizeMin = 12f;
            _topScoreLabel.fontSizeMax = 17f;

            _topEmblemB = QdaoUguiFactory.CreateImage("TopEmblemB", _topBanner, 464f, 6f, 32f, 32f, null, false);
            _topEmblemB.preserveAspect = true;
            _topEmblemB.gameObject.SetActive(false);

            var warmBadgeSprite = TournamentArtCatalog.Load("v3-d-warmup-badge");
            if (warmBadgeSprite != null)
            {
                var wb = QdaoUguiFactory.CreateImage("TopWarmupBadge", _topBanner, 502f, 6f, 114f, 32f, warmBadgeSprite, false);
                wb.preserveAspect = false;
                var wbTxt = TournamentArtCatalog.Label("Txt", wb.transform, 12f, 0f, 90f, 32f, "零积分演练", 12f,
                    TournamentArtCatalog.PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
                wbTxt.enableAutoSizing = true;
                wbTxt.fontSizeMin = 9.5f;
                wbTxt.fontSizeMax = 12f;
                _topWarmupBadge = wb.rectTransform;
            }
            else
            {
                _topWarmupBadge = TournamentArtCatalog.BorderedCard("TopWarmupBadge", _topBanner, 502f, 6f, 114f, 32f,
                    TournamentArtCatalog.JadeSoft, TournamentArtCatalog.Jade, 1.2f);
                TournamentArtCatalog.Label("Txt", _topWarmupBadge, 4f, 0f, 106f, 32f, "零积分演练", 12f,
                    TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
            }
            _topWarmupBadge.gameObject.SetActive(false);

            var leyBtnGo = QdaoUguiFactory.CreateRect("TopLeylineBtn", _topBanner, 622f, 4f, 92f, 36f);
            var leyBtnImg = leyBtnGo.gameObject.AddComponent<Image>();
            leyBtnImg.color = TournamentArtCatalog.GoldTagBg;
            leyBtnImg.raycastTarget = true;
            _topLeylineBtn = leyBtnGo.gameObject.AddComponent<Button>();
            _topLeylineBtn.targetGraphic = leyBtnImg;
            _topLeylineBtn.onClick.AddListener(ToggleLeylineTooltip);
            _topLeylineIcon = QdaoUguiFactory.CreateImage("Icon", leyBtnGo, 4f, 4f, 28f, 28f, null, false);
            _topLeylineIcon.preserveAspect = true;
            TournamentArtCatalog.Label("Lbl", leyBtnGo, 34f, 0f, 54f, 36f, "灵脉", 14f, TournamentArtCatalog.GoldTagText,
                TextAlignmentOptions.Center, bold: true);

            _leylineTooltip = TournamentArtCatalog.BorderedCard("LeylineTooltip", _topBanner, 120f, 48f, 480f, 46f,
                TournamentArtCatalog.Ivory, TournamentArtCatalog.Gold, 1.5f);
            _leylineTooltipText = TournamentArtCatalog.Label("Txt", _leylineTooltip, 12f, 4f, 456f, 38f,
                string.Empty, 15f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
            _leylineTooltip.gameObject.SetActive(false);

            // ── 飘字浮层（位于底条上方安全带，避开战斗单位与血条） ──
            var floatGo = new GameObject("CheerFloatLayer", typeof(RectTransform));
            floatGo.transform.SetParent(_safeRoot, false);
            _floatLayer = floatGo.GetComponent<RectTransform>();
            _floatLayer.anchorMin = new Vector2(0.5f, 0f);
            _floatLayer.anchorMax = new Vector2(0.5f, 0f);
            _floatLayer.pivot = new Vector2(0.5f, 0f);
            _floatLayer.anchoredPosition = new Vector2(-80f, BarHeight + 18f);
            _floatLayer.sizeDelta = new Vector2(760f, 150f);

            // ── 底部单行喝彩条（严格 <= 76px，左偏避让右下「退出观战」按钮） ──
            var barGo = new GameObject("CheerBarRoot", typeof(RectTransform));
            barGo.transform.SetParent(_safeRoot, false);
            _barRoot = barGo.GetComponent<RectTransform>();
            _barRoot.anchorMin = new Vector2(0.5f, 0f);
            _barRoot.anchorMax = new Vector2(0.5f, 0f);
            _barRoot.pivot = new Vector2(0.5f, 0f);
            _barRoot.anchoredPosition = new Vector2(-70f, 10f);
            _barRoot.sizeDelta = new Vector2(BarMaxWidth, BarHeight);

            var barBg = barGo.AddComponent<Image>();
            barBg.sprite = TournamentArtCatalog.Load("v2-c-cheer-bar");
            barBg.type = Image.Type.Simple;
            barBg.raycastTarget = true;
            if (barBg.sprite == null) barBg.color = new Color(0.95f, 0.91f, 0.82f, 0.96f);

            // 1. 左侧标题原生毛笔字稿（300×48 等比缩放，严禁叠字）
            var titleImg = TournamentArtCatalog.SpriteBox("CheerTitle", _barRoot, 16f, 18f, 150f, 24f,
                "v2-title-cheer", preserveAspect: true);
            if (titleImg.sprite == null)
                TournamentArtCatalog.Label("CheerTitleFallback", _barRoot, 16f, 18f, 120f, 26f, "观战喝彩", 17f, TournamentArtCatalog.JadeDeep, bold: true);

            // 2. 支持阵营切换胶囊（玉绿 A 队 / 朱砂 B 队）
            var sideOuter = TournamentArtCatalog.Box("SideToggle", _barRoot, 174f, 16f, 146f, 40f, TournamentArtCatalog.Gold, true);
            var sideInner = QdaoUguiFactory.CreateStretch("Bg", sideOuter.rectTransform, new Vector4(1.5f, 1.5f, 1.5f, 1.5f));
            _sideToggleBg = sideInner.gameObject.AddComponent<Image>();
            _sideToggleBg.color = TournamentArtCatalog.Jade;
            _sideToggleBg.raycastTarget = false;
            _sideToggleBtn = sideOuter.gameObject.AddComponent<Button>();
            _sideToggleBtn.targetGraphic = sideOuter;
            _sideToggleBtn.onClick.AddListener(ToggleSide);
            _sideToggleText = TournamentArtCatalog.Label("Label", sideOuter.rectTransform, 6f, 0f, 134f, 40f,
                "支持 A队", 15f, TournamentArtCatalog.PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            _sideToggleText.enableAutoSizing = true;
            _sideToggleText.fontSizeMin = 12f;
            _sideToggleText.fontSizeMax = 15f;

            // 3. 中部六枚完整固定短语按钮（单行水平滚区，phrase-01..06 含底色与毛笔字，严禁叠字）
            TournamentArtCatalog.CreateScrollView("PhraseScroll", _barRoot, 330f, 16f, 804f, 40f,
                horizontal: true, vertical: false, out var phraseContent);
            const float pillW = 126f;
            const float pillH = 24f;
            const float pillGap = 8f;
            for (int i = 0; i < TournamentFormatting.SpectateCheerPresets.Length; i++)
            {
                var preset = TournamentFormatting.SpectateCheerPresets[i];
                float px = i * (pillW + pillGap);

                // 外圈选中环（默认透明，选中时显暖金边）
                var ringImg = TournamentArtCatalog.Box($"PhraseRing_{i}", phraseContent, px - 2f, 6f, pillW + 4f, pillH + 4f,
                    new Color(0.85f, 0.75f, 0.52f, 0f), false);
                _phraseSelectionRings[i] = ringImg;

                var sprite = TournamentArtCatalog.Load(preset.SpriteKey);
                var btn = QdaoUguiFactory.CreateArtButton($"PhraseBtn_{preset.PhraseId}", phraseContent,
                    px, 8f, pillW, pillH, sprite, out var pImg);
                pImg.preserveAspect = true;
                if (sprite == null)
                {
                    pImg.color = TournamentArtCatalog.JadeSoft;
                    TournamentArtCatalog.Label("Fallback", btn.transform, 4f, 0f, pillW - 8f, pillH,
                        preset.Label, 13f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);
                }
                uint phraseId = preset.PhraseId;
                btn.onClick.AddListener(() => OnPhraseClicked(phraseId));
                _phraseButtons[i] = btn;
                _phraseImages[i] = pImg;
            }
            phraseContent.sizeDelta = new Vector2(6f * (pillW + pillGap), 40f);

            // 4. 右侧人气对推条（popularity-track 440×8 + jade/coral Filled 双向真实比例，0:0 中性均衡）
            var popRoot = QdaoUguiFactory.CreateRect("PopularityBox", _barRoot, 1146f, 10f, 250f, 52f);
            _popularityLabel = TournamentArtCatalog.Label("PopCounts", popRoot, 0f, 0f, 250f, 22f,
                "人气 0 : 0", 14f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            var track = TournamentArtCatalog.SpriteBox("Track", popRoot, 8f, 26f, 234f, 10f, "v2-c-popularity-track");
            if (track.sprite == null) track.color = TournamentArtCatalog.CardAltBg;

            _jadeFill = QdaoUguiFactory.CreateImage("JadeFill", track.transform, 0f, 0f, 234f, 10f,
                TournamentArtCatalog.Load("v2-c-popularity-jade"), false);
            _jadeFill.type = Image.Type.Filled;
            _jadeFill.fillMethod = Image.FillMethod.Horizontal;
            _jadeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _jadeFill.fillAmount = 0.5f;
            if (_jadeFill.sprite == null) _jadeFill.color = TournamentArtCatalog.Jade;

            _coralFill = QdaoUguiFactory.CreateImage("CoralFill", track.transform, 0f, 0f, 234f, 10f,
                TournamentArtCatalog.Load("v2-c-popularity-coral"), false);
            _coralFill.type = Image.Type.Filled;
            _coralFill.fillMethod = Image.FillMethod.Horizontal;
            _coralFill.fillOrigin = (int)Image.OriginHorizontal.Right;
            _coralFill.fillAmount = 0.5f;
            if (_coralFill.sprite == null) _coralFill.color = TournamentArtCatalog.WarnRed;

            _statusHintLabel = TournamentArtCatalog.Label("CheerHint", popRoot, 0f, 38f, 250f, 16f,
                "预设喝彩 · 5 秒冷却", 12f, TournamentArtCatalog.Muted, TextAlignmentOptions.Center);

            // 5. 最右：飘字开关（兼容降低动效需求）
            _floatToggleBtn = TournamentArtCatalog.FlatButton("FloatToggle", _barRoot, 1408f, 18f, 96f, 36f,
                "飘字 开", TournamentArtCatalog.Ivory, TournamentArtCatalog.Line, TournamentArtCatalog.JadeDeep,
                ToggleFloats, interactable: true, fontSize: 14f);
            _floatToggleText = _floatToggleBtn.GetComponentInChildren<TMP_Text>(true);

            _canvas.gameObject.SetActive(false);
        }

        public void BindClient(TournamentClient next)
        {
            if (ReferenceEquals(_client, next)) return;
            UnbindClient();
            _client = next;
            if (_client != null)
                _client.SpectateCheerChanged += HandleCheerChanged;
        }

        public void UnbindClient()
        {
            if (_client != null)
            {
                _client.SpectateCheerChanged -= HandleCheerChanged;
                _client = null;
            }
        }

        public void ResetAndHide()
        {
            _activeBattleId = 0UL;
            _lastRenderedFloatSeq = 0UL;
            _lastClickedPhraseId = 0u;
            _leylineTooltipOpen = false;
            _lastContext = null;
            if (_leylineTooltip != null) _leylineTooltip.gameObject.SetActive(false);
            ClearFloats();
            _client?.ClearSpectateCheer();
            if (_canvas != null && _canvas.gameObject.activeSelf)
                _canvas.gameObject.SetActive(false);
        }

        public void Tick(float nowRealtimeSeconds, bool inGame)
        {
            if (!inGame || _client == null)
            {
                if (IsVisible) ResetAndHide();
                return;
            }

            var battleUi = BattleUiRoot.Instance;
            var spectate = SpectateClient.Instance;
            var state = spectate?.State;
            var ctx = state?.TournamentContext;
            bool shouldShow = battleUi != null && battleUi.IsSpectating
                && spectate != null && spectate.Phase == SpectatePhase.Watching
                && ctx != null && ctx.MatchId != 0UL && state.BattleId != 0UL;

            if (!shouldShow)
            {
                if (IsVisible) ResetAndHide();
                return;
            }

            ApplySafeArea();

            if (!IsVisible || _activeBattleId != state.BattleId)
            {
                _activeBattleId = state.BattleId;
                _lastRenderedFloatSeq = 0UL;
                _lastClickedPhraseId = 0u;
                _leylineTooltipOpen = false;
                if (_leylineTooltip != null) _leylineTooltip.gameObject.SetActive(false);
                ClearFloats();
                _client.ClearSpectateCheer();
                _canvas.gameObject.SetActive(true);
                _nextPollAt = 0f;
            }

            _lastContext = ctx;
            RefreshTopBanner(ctx);
            RefreshSideToggleText(ctx);

            if (nowRealtimeSeconds >= _nextPollAt)
            {
                _nextPollAt = nowRealtimeSeconds + PollIntervalSec;
                ulong afterSeq = _client.SpectateCheer != null && _client.SpectateCheer.BattleId == _activeBattleId
                    ? _client.SpectateCheer.LatestSeq : 0UL;
                _client.RequestSpectateCheer(_activeBattleId, afterSeq);
            }

            if (nowRealtimeSeconds >= _nextUiRefreshAt)
            {
                _nextUiRefreshAt = nowRealtimeSeconds + 0.25f;
                RefreshCheerBarState(nowRealtimeSeconds);
            }

            AnimateFloats(nowRealtimeSeconds);
        }

        private void HandleCheerChanged()
        {
            if (!IsVisible || _client == null) return;
            float now = Time.realtimeSinceStartup;
            RefreshCheerBarState(now);
            SpawnNewFloats(now);
        }

        private void OnPhraseClicked(uint phraseId)
        {
            if (_client == null || _activeBattleId == 0UL) return;
            _lastClickedPhraseId = phraseId;
            _client.SendSpectateCheer(_activeBattleId, phraseId, _selectedSide);
            RefreshCheerBarState(Time.realtimeSinceStartup);
        }

        private void ToggleSide()
        {
            _selectedSide = _selectedSide == 0u ? 1u : 0u;
            RefreshSideToggleText(_lastContext);
        }

        private void ToggleFloats()
        {
            _floatsEnabled = !_floatsEnabled;
            if (!_floatsEnabled) ClearFloats();
            if (_floatToggleText != null) _floatToggleText.text = _floatsEnabled ? "飘字 开" : "飘字 关";
        }

        private void ToggleLeylineTooltip()
        {
            _leylineTooltipOpen = !_leylineTooltipOpen;
            if (_leylineTooltip != null) _leylineTooltip.gameObject.SetActive(_leylineTooltipOpen);
        }

        private void RefreshTopBanner(TournamentBattleContext ctx)
        {
            if (ctx == null) return;
            string a = !string.IsNullOrEmpty(ctx.SquadAName) ? ctx.SquadAName : "甲队";
            string b = !string.IsNullOrEmpty(ctx.SquadBName) ? ctx.SquadBName : "乙队";
            string boTag = ctx.IsWarmup
                ? "热身演练"
                : (ctx.IsThirdPlace
                    ? (ctx.BestOf >= 3 ? $"季军战 BO{ctx.BestOf} {ctx.WinsA}:{ctx.WinsB}" : "季军争夺战")
                    : (ctx.BestOf >= 3
                        ? $"BO{ctx.BestOf} {ctx.WinsA}:{ctx.WinsB} · 第 {Math.Max(1u, ctx.GameIndex)} 局"
                        : "淘汰赛"));
            _topScoreLabel.text = $"{a}  [{boTag}]  {b}";

            var embA = ctx.SquadAEmblemId > 0u ? TournamentArtCatalog.Load(TournamentFormatting.GetEmblemInfo(ctx.SquadAEmblemId).SpriteKey) : null;
            _topEmblemA.sprite = embA;
            _topEmblemA.gameObject.SetActive(embA != null);
            var embB = ctx.SquadBEmblemId > 0u ? TournamentArtCatalog.Load(TournamentFormatting.GetEmblemInfo(ctx.SquadBEmblemId).SpriteKey) : null;
            _topEmblemB.sprite = embB;
            _topEmblemB.gameObject.SetActive(embB != null);
            if (_topWarmupBadge != null) _topWarmupBadge.gameObject.SetActive(ctx.IsWarmup);

            var info = TournamentFormatting.GetLeylineInfo(ctx.LeylineElement);
            bool hasLeyline = ctx.LeylineElement != TournamentLeylineElement.TournamentLeylineUnspecified;
            _topLeylineBtn.gameObject.SetActive(hasLeyline);
            if (hasLeyline)
            {
                _topLeylineIcon.sprite = TournamentArtCatalog.Load(info.SpriteKey);
                _leylineTooltipText.text = $"{info.FullName}：{info.EffectSummary}";
            }
            else if (_leylineTooltip != null)
            {
                _leylineTooltip.gameObject.SetActive(false);
            }
        }

        private void RefreshSideToggleText(TournamentBattleContext ctx)
        {
            bool sideA = _selectedSide == 0u;
            _sideToggleBg.color = sideA ? TournamentArtCatalog.Jade : TournamentArtCatalog.WarnRed;
            string teamName = sideA
                ? (!string.IsNullOrEmpty(ctx?.SquadAName) ? ctx.SquadAName : "A队")
                : (!string.IsNullOrEmpty(ctx?.SquadBName) ? ctx.SquadBName : "B队");
            if (teamName.Length > 5) teamName = teamName.Substring(0, 5) + "…";
            _sideToggleText.text = $"挺 {teamName}";
        }

        private void RefreshCheerBarState(float nowRealtimeSeconds)
        {
            var snap = _client?.SpectateCheer;
            bool matchSnap = snap != null && snap.BattleId == _activeBattleId;
            ulong countA = matchSnap ? snap.CountA : 0UL;
            ulong countB = matchSnap ? snap.CountB : 0UL;
            var (ratioA, ratioB, _) = TournamentFormatting.ComputePopularitySplit(countA, countB);
            _jadeFill.fillAmount = ratioA;
            _coralFill.fillAmount = ratioB;
            _popularityLabel.text = $"人气  {countA} : {countB}";

            ulong cdMs = matchSnap ? snap.CooldownRemainingMs(nowRealtimeSeconds) : 0UL;
            bool busy = _client?.IsSpectateCheerSending == true;
            bool disabled = busy || cdMs > 0UL;

            for (int i = 0; i < _phraseButtons.Length; i++)
            {
                uint pid = (uint)(i + 1);
                _phraseButtons[i].interactable = !disabled;
                _phraseImages[i].color = disabled ? new Color(1f, 1f, 1f, 0.52f) : Color.white;
                _phraseSelectionRings[i].color = pid == _lastClickedPhraseId
                    ? TournamentArtCatalog.Gold : new Color(0.85f, 0.75f, 0.52f, 0f);
            }

            if (busy)
                _statusHintLabel.text = "喝彩送出中…";
            else if (cdMs > 0UL)
                _statusHintLabel.text = $"喝彩冷却 {Mathf.CeilToInt(cdMs / 1000f)}s";
            else if (!string.IsNullOrEmpty(_client?.LastError))
                _statusHintLabel.text = _client.LastError;
            else
                _statusHintLabel.text = "点击预设短语为本场加油";
        }

        private void SpawnNewFloats(float nowRealtimeSeconds)
        {
            var snap = _client?.SpectateCheer;
            if (snap == null || snap.BattleId != _activeBattleId || snap.RecentCheers.Count == 0) return;

            if (_lastRenderedFloatSeq == 0UL)
            {
                // 首次进入只对齐水位，不把历史积压一次全弹出来
                _lastRenderedFloatSeq = snap.LatestSeq;
                return;
            }

            for (int i = 0; i < snap.RecentCheers.Count; i++)
            {
                var item = snap.RecentCheers[i];
                if (item == null || item.Seq <= _lastRenderedFloatSeq) continue;
                _lastRenderedFloatSeq = item.Seq;
                if (_floatsEnabled) SpawnSingleFloat(item, nowRealtimeSeconds);
            }
        }

        private void SpawnSingleFloat(TournamentSpectateCheerItem item, float nowRealtimeSeconds)
        {
            if (!TournamentFormatting.TryGetCheerPreset(item.PhraseId, out var preset)) return;
            while (_floats.Count >= MaxActiveFloats)
            {
                var oldest = _floats[0];
                _floats.RemoveAt(0);
                if (oldest.Go != null) UnityEngine.Object.Destroy(oldest.Go);
            }

            var go = new GameObject($"CheerFloat_{item.Seq}", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_floatLayer, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            float offsetX = ((item.Seq % 3UL) - 1f) * 210f;
            float startY = 8f + (_floats.Count * 28f);
            rect.anchoredPosition = new Vector2(offsetX, startY);

            var floatSprite = preset.HasNativeFloatArt ? TournamentArtCatalog.Load("v2-c-cheer-float") : null;
            if (floatSprite != null)
            {
                // cheer-float.png (280×44) 已含原生轻金「妙法天成」字稿，严禁重复叠字
                rect.sizeDelta = new Vector2(224f, 35.2f);
                var img = go.AddComponent<Image>();
                img.sprite = floatSprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                rect.sizeDelta = new Vector2(240f, 32f);
                var bg = go.AddComponent<Image>();
                bg.color = item.Side == 0u
                    ? new Color(0.13f, 0.36f, 0.27f, 0.88f)
                    : new Color(0.62f, 0.26f, 0.20f, 0.88f);
                bg.raycastTarget = false;
                string prefix = !string.IsNullOrEmpty(item.SenderName) ? $"{item.SenderName}：" : string.Empty;
                TournamentArtCatalog.Label("Txt", rect, 8f, 0f, 224f, 32f,
                    prefix + preset.Label, 15f, TournamentArtCatalog.PrimaryBtnText, TextAlignmentOptions.Center, bold: true);
            }

            _floats.Add(new ActiveFloatItem
            {
                Go = go,
                Rect = rect,
                Group = go.GetComponent<CanvasGroup>(),
                StartY = startY,
                CreatedAt = nowRealtimeSeconds,
            });
        }

        private void AnimateFloats(float nowRealtimeSeconds)
        {
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                float t = (nowRealtimeSeconds - f.CreatedAt) / FloatDurationSec;
                if (t >= 1f || f.Go == null)
                {
                    if (f.Go != null) UnityEngine.Object.Destroy(f.Go);
                    _floats.RemoveAt(i);
                    continue;
                }
                var pos = f.Rect.anchoredPosition;
                pos.y = f.StartY + t * 44f; // 低幅度平稳上移，绝不遮挡战斗单位
                f.Rect.anchoredPosition = pos;
                f.Group.alpha = t < 0.7f ? 1f : Mathf.Clamp01(1f - (t - 0.7f) / 0.3f);
            }
        }

        private void ClearFloats()
        {
            for (int i = 0; i < _floats.Count; i++)
            {
                if (_floats[i].Go != null) UnityEngine.Object.Destroy(_floats[i].Go);
            }
            _floats.Clear();
        }

        private void ApplySafeArea()
        {
            var safe = Screen.safeArea;
            if (safe == _lastSafeArea || Screen.width <= 0 || Screen.height <= 0) return;
            _lastSafeArea = safe;
            _safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            _safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            _safeRoot.offsetMin = Vector2.zero;
            _safeRoot.offsetMax = Vector2.zero;
        }
    }
}
