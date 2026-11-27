using System;
using System.Collections.Generic;
using MmorpgClient.Game.CityNpc;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 归元城 8 位原创核心 NPC 交互对话、演武木桩/残局/奇谈、顺路捎物、灵潮五行签牌与江湖名帖窗口。
    /// 全量接入 Codex 交付的 8 张 1024×1024 独立立绘与 13 张无字 PNG-32 切图（UI/Ugui/GuiyuanNpcV1/），
    /// 缺图时安全回退通用底板与自有原创角色图；兼容 2560×1080、1920×1080、1280×480 与安全区/小游戏胶囊避让。
    /// </summary>
    public sealed class CityNpcWindow
    {
        public const string DedicatedArtRoot = "UI/Ugui/GuiyuanNpcV1/";
        public const float DesignWidth = 1560f;
        public const float DesignHeight = 896f;

        /// <summary>Codex 交付的 13 张正式 UI 控件切图（全量运行时接线，无闲置资源）。</summary>
        public static readonly string[] AllDeliveredUiKeys =
        {
            "npc_dialog_panel",
            "npc_name_ribbon",
            "npc_bubble_bg",
            "npc_action_btn_primary",
            "npc_action_btn_secondary",
            "card_calling_panel",
            "fortune_slip_card",
            "icon_sparring_dummy",
            "icon_tactical_puzzle",
            "icon_character_tale",
            "icon_errand_parcel",
            "icon_fortune_tube",
            "icon_calling_card"
        };

        /// <summary>Codex 交付的 8 张 1024×1024 独立全身 NPC 立绘。</summary>
        public static readonly string[] AllDeliveredNpcKeys =
        {
            "Npcs/npc_guiyuan_smith",
            "Npcs/npc_guiyuan_merchant",
            "Npcs/npc_guiyuan_sparring",
            "Npcs/npc_guiyuan_elder",
            "Npcs/npc_guiyuan_constable",
            "Npcs/npc_guiyuan_herbalist",
            "Npcs/npc_guiyuan_ferryman",
            "Npcs/npc_guiyuan_storyteller"
        };

        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public bool HasOpenSubModal =>
            (_fortuneModal != null && _fortuneModal.gameObject.activeSelf) ||
            (_callingCardModal != null && _callingCardModal.gameObject.activeSelf);
        public CityNpcDefinition CurrentNpc => _currentNpc;

        public event Action<CityNpcDefinition> SelectNpcRequested;
        public event Action<CityNpcDefinition> WalkToNpcRequested;
        public event Action<CityNpcShortcutKind> ShortcutRequested;
        public event Action<CityNpcEncounterDefinition, bool> StartEncounterRequested;
        public event Action<string> AcceptErrandRequested;
        public event Action<string> ArchiveErrandRequested;
        public event Action DrawFortuneRequested;
        public event Action CycleCompanionOathRequested;
        public event Action CopyCallingCardRequested;

        private readonly RectTransform _root;
        private readonly RectTransform _fittedRoot;
        private readonly Button _closeButton;
        private readonly Image _portrait;
        private readonly TMP_Text _plaqueTitle;
        private readonly TMP_Text _npcNameRibbonText;
        private readonly TMP_Text _npcLandmarkText;
        private readonly TMP_Text _characterTag;
        private readonly TMP_Text _greetingBody;
        private readonly RectTransform _featureBody;
        private readonly RectTransform _actionDeckRow;
        private readonly RectTransform _shortcutRow;
        private readonly TMP_Text _statusText;
        private readonly RectTransform _rosterBar;
        private readonly List<Button> _rosterButtons = new();

        // 两个独立原比例二级弹层：480×680 五行签牌 + 1240×720 江湖名帖
        private readonly RectTransform _fortuneModal;
        private readonly RectTransform _fortuneCardBody;
        private readonly RectTransform _callingCardModal;
        private readonly RectTransform _callingCardBody;

        private CityNpcDefinition _currentNpc;
        private bool _useFreePresetForOptional = true;
        private bool _sparringShowPuzzles;
        private bool _ferryShowTale;
        private uint _expandedStoryDungeonId;
        private bool _compactCloseOnLeft;

        // 缓存最近一次渲染上下文，供子页签切换与打开签牌/名帖弹层即时重绘
        private string _lastAppearanceId = "";
        private string _lastArchiveName = "";
        private CityNpcState _lastState;
        private CharacterCallingCardView _lastCallingCard;
        private uint _lastActiveTideSceneId;

        public CityNpcWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("CityNpcWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(0.03f, 0.08f, 0.07f, 0.72f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();

            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("CityNpcFittedRoot", _root, DesignWidth, DesignHeight);
            var fit = _fittedRoot.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(DesignWidth, DesignHeight);

            // 主对话面板：严格按 Codex npc_dialog_panel.png 1560×820 内部几何锚点排布
            var panelBg = CreateOptionalArt(_fittedRoot, "npc_dialog_panel", "panel_main", 0, 0, 1560, 820, true);
            panelBg.raycastTarget = true;

            // 顶部玉匾（x=603, y=14, w=354, h=94）
            _plaqueTitle = Text(_fittedRoot, "归元城 · 街坊见闻", 612, 26, 336, 70, 29, Cream, alignment: TextAlignmentOptions.Center);

            // 关闭按钮（常规右上角；窄横屏/小游戏胶囊避让时切到左上角）
            _closeButton = DedicatedButton(_fittedRoot, "关闭", 1408, 42, 112, 50, Hide, primary: false, fontSize: 22);

            // 左侧立绘龛（x=69, y=114, w=498, h=616）：8 位 1024×1024 独立全身立绘 + npc_name_ribbon + 寻路按钮
            _portrait = QdaoUguiFactory.CreateImage("NpcPortrait", _fittedRoot, 88, 126, 460, 444, null);
            _portrait.preserveAspect = true;
            _portrait.raycastTarget = false;

            CreateOptionalArt(_fittedRoot, "npc_name_ribbon", "tab_selected", 108, 574, 420, 68, true);
            _npcNameRibbonText = Text(_fittedRoot, "", 138, 574, 360, 68, 25, Cream, alignment: TextAlignmentOptions.Center);
            _npcLandmarkText = Text(_fittedRoot, "", 96, 646, 444, 28, 18, Muted, alignment: TextAlignmentOptions.Center);
            DedicatedButton(_fittedRoot, "自动寻路前往", 178, 676, 280, 48, () =>
            {
                if (_currentNpc != null) WalkToNpcRequested?.Invoke(_currentNpc);
            }, primary: false, fontSize: 20);

            // 右上米白宣纸内框（x=601, y=125, w=872, h=365）：专属身份标签 + 9×8 互动台词 + 专属玩法区
            _characterTag = Text(_fittedRoot, "", 624, 136, 826, 30, 19, Gold);
            _greetingBody = Text(_fittedRoot, "", 624, 168, 826, 80, 21, Ink, true);
            _featureBody = QdaoUguiFactory.CreateRect("NpcFeatureBody", _fittedRoot, 616, 254, 842, 228);

            // 右下玉珠分隔线下方（y=528..748）：主玩法操作按钮行 + 既有系统直达行 + 状态提示行
            _actionDeckRow = QdaoUguiFactory.CreateRect("NpcActionDeckRow", _fittedRoot, 608, 532, 864, 66);
            _shortcutRow = QdaoUguiFactory.CreateRect("NpcShortcutRow", _fittedRoot, 608, 608, 864, 60);
            _statusText = Text(_fittedRoot, "", 612, 678, 856, 56, 19, Muted, true);
            _statusText.richText = false;

            // 底栏：归元城 8 位原创 NPC 快速切换横条（8 × 188 + 7 × 8 = 1560）
            _rosterBar = QdaoUguiFactory.CreateRect("NpcRosterBar", _fittedRoot, 0, 834, 1560, 60);
            BuildRosterButtons();

            // 二级弹层 1：480×680 灵潮五行签牌（严格等比不拉伸）
            _fortuneModal = BuildModalLayer("FortuneSlipModal", out _fortuneCardBody, 520f, 770f);

            // 二级弹层 2：1240×720 人物江湖名帖（原比例展示）
            _callingCardModal = BuildModalLayer("CallingCardModal", out _callingCardBody, 1240f, 792f);
        }

        private RectTransform BuildModalLayer(string name, out RectTransform body, float refW, float refH)
        {
            var overlay = QdaoUguiFactory.CreateStretch(name, _root, Vector4.zero);
            overlay.gameObject.SetActive(false);
            var bg = overlay.gameObject.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.06f, 0.05f, 0.78f);
            bg.raycastTarget = true;

            body = QdaoUguiFactory.CreateCenteredRect(name + "Body", overlay, refW, refH);
            var fit = body.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(refW, refH);
            return overlay;
        }

        private void BuildRosterButtons()
        {
            Clear(_rosterBar);
            _rosterButtons.Clear();
            float x = 0f;
            for (int i = 0; i < CityNpcCatalog.Npcs.Count; ++i)
            {
                var npc = CityNpcCatalog.Npcs[i];
                var btn = DedicatedButton(_rosterBar, npc.Name, x, 0f, 188f, 58f,
                    () => SelectNpcRequested?.Invoke(npc), primary: false, fontSize: 21);
                _rosterButtons.Add(btn);
                x += 196f;
            }
        }

        public void SetCompactCloseOnLeft(bool compactOnLeft)
        {
            if (_compactCloseOnLeft == compactOnLeft || _closeButton == null) return;
            _compactCloseOnLeft = compactOnLeft;
            var rect = (RectTransform)_closeButton.transform;
            rect.anchoredPosition = compactOnLeft ? new Vector2(40f, -42f) : new Vector2(1408f, -42f);
        }

        public bool DismissModalIfOpen()
        {
            if (_fortuneModal != null && _fortuneModal.gameObject.activeSelf)
            {
                _fortuneModal.gameObject.SetActive(false);
                return true;
            }
            if (_callingCardModal != null && _callingCardModal.gameObject.activeSelf)
            {
                _callingCardModal.gameObject.SetActive(false);
                return true;
            }
            return false;
        }

        public void Show(CityNpcDefinition npc, string activeAppearanceId, string activeCharacterArchiveName,
            CityNpcState state, CharacterCallingCardView callingCard, uint activeTideSceneId, string statusNotice = "")
        {
            _currentNpc = npc ?? CityNpcCatalog.Npcs[0];
            DismissModalIfOpen();
            _root.gameObject.SetActive(true);
            Refresh(activeAppearanceId, activeCharacterArchiveName, state, callingCard, activeTideSceneId, statusNotice);
        }

        public void Hide()
        {
            DismissModalIfOpen();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void OpenFortuneModal()
        {
            if (!IsVisible) return;
            if (_callingCardModal != null) _callingCardModal.gameObject.SetActive(false);
            RenderFortuneModal();
            if (_fortuneModal != null) _fortuneModal.gameObject.SetActive(true);
        }

        public void OpenCallingCardModal()
        {
            if (!IsVisible) return;
            if (_fortuneModal != null) _fortuneModal.gameObject.SetActive(false);
            RenderCallingCardModal();
            if (_callingCardModal != null) _callingCardModal.gameObject.SetActive(true);
        }

        public void Refresh(string activeAppearanceId, string activeCharacterArchiveName,
            CityNpcState state, CharacterCallingCardView callingCard, uint activeTideSceneId, string statusNotice = "")
        {
            if (_currentNpc == null || !IsVisible) return;
            _lastAppearanceId = activeAppearanceId ?? "";
            _lastArchiveName = activeCharacterArchiveName ?? "";
            _lastState = state;
            _lastCallingCard = callingCard;
            _lastActiveTideSceneId = activeTideSceneId;

            // 高亮底栏当前选中的归元城 NPC
            var primarySprite = LoadDedicatedSprite("npc_action_btn_primary") ?? Load("tab_selected");
            var secondarySprite = LoadDedicatedSprite("npc_action_btn_secondary") ?? Load("button_secondary");
            for (int i = 0; i < _rosterButtons.Count && i < CityNpcCatalog.Npcs.Count; ++i)
            {
                bool selected = string.Equals(CityNpcCatalog.Npcs[i].Id, _currentNpc.Id, StringComparison.Ordinal);
                var img = _rosterButtons[i].image;
                if (img != null)
                {
                    img.sprite = selected ? primarySprite : secondarySprite;
                    img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                }
                var lbl = _rosterButtons[i].GetComponentInChildren<TMP_Text>();
                if (lbl != null) lbl.color = selected ? Cream : Ink;
            }

            // 左侧立绘与称谓缎带（100% 优先加载 Codex 专属透明全身立绘）
            var sprite = Resources.Load<Sprite>(_currentNpc.DedicatedSpriteKey)
                         ?? QdaoCharacterCatalog.LoadPortrait(_currentNpc.FallbackAppearanceId);
            _portrait.sprite = sprite;
            _portrait.enabled = sprite != null;
            _plaqueTitle.text = $"归元城 · {_currentNpc.Name}";
            _npcNameRibbonText.text = $"{_currentNpc.Name} · {_currentNpc.RoleTitle}";
            _npcLandmarkText.text = $"常驻：归元城 · {_currentNpc.LandmarkLabel}";

            // 9×8 看人下菜碟台词
            bool matchedChar = _currentNpc.TryGetCharacterGreeting(_lastAppearanceId, out string greeting);
            string charTitle = string.IsNullOrWhiteSpace(_lastArchiveName) ? "当前人物" : _lastArchiveName;
            _characterTag.text = matchedChar
                ? $"★ 人物专属互动（当前身披：{charTitle}）"
                : "归元城见闻（换上不同人物或免费试用可触发不同专属对白）";
            _greetingBody.text = greeting;

            // 快捷系统直达按钮行（不强迫跑腿，HUD 原入口照常可用）
            Clear(_shortcutRow);
            int scCount = _currentNpc.Shortcuts.Count;
            if (scCount > 0)
            {
                float btnW = scCount >= 3 ? 276f : 310f;
                float gap = 14f;
                float sx = 0f;
                for (int i = 0; i < scCount; ++i)
                {
                    var sc = _currentNpc.Shortcuts[i];
                    DedicatedButton(_shortcutRow, sc.Label, sx, 2f, btnW, 54f,
                        () => ShortcutRequested?.Invoke(sc.Kind), primary: false, fontSize: 20);
                    sx += btnW + gap;
                }
            }

            RenderFeatureBody();
            _statusText.text = string.IsNullOrEmpty(statusNotice) ? (_lastState?.LastBannerNotice ?? "") : statusNotice;

            if (_fortuneModal != null && _fortuneModal.gameObject.activeSelf) RenderFortuneModal();
            if (_callingCardModal != null && _callingCardModal.gameObject.activeSelf) RenderCallingCardModal();
        }

        private void RenderFeatureBody()
        {
            Clear(_featureBody);
            Clear(_actionDeckRow);
            switch (_currentNpc.FeatureKind)
            {
                case CityNpcFeatureKind.SparringAndPuzzles:
                    RenderSparringAndPuzzles();
                    break;
                case CityNpcFeatureKind.CharacterTale:
                    RenderCharacterTales(_currentNpc.Id);
                    break;
                case CityNpcFeatureKind.LightErrands:
                    RenderErrandsAndFerryTale();
                    break;
                case CityNpcFeatureKind.FortuneAndCallingCard:
                    RenderFortuneAndCallingCard();
                    break;
                default:
                    RenderAmbientGuidance();
                    break;
            }
        }

        private void RenderAmbientGuidance()
        {
            CreateDedicatedIcon(_featureBody, "icon_sparring_dummy", 4f, 8f, 44f);
            Text(_featureBody, "【齐问锋 · 演武场】零消耗无损试招木桩（2 档）+ 回合残局考题（3 关，支持免费 3×3 预设）。", 58f, 12f, 774f, 40f, 19, Ink, true);

            CreateDedicatedIcon(_featureBody, "icon_character_tale", 4f, 64f, 44f);
            Text(_featureBody, "【顾青崖 / 温半夏 / 周行舟】3 篇不限时「归元人物奇谈」与 3 条野外「顺路捎物」，零日常打卡。", 58f, 68f, 774f, 40f, 19, Ink, true);

            CreateDedicatedIcon(_featureBody, "icon_fortune_tube", 4f, 120f, 44f);
            Text(_featureBody, "【莫听风 · 桥畔茶亭】求取灵潮五行签文（纯视觉吉签）、生成人物「江湖名帖」与设定纯外观「同行之约」。", 58f, 124f, 774f, 44f, 19, Ink, true);

            Text(_actionDeckRow, "提示：点击下方任意归元城街坊可直达互动，或通过下方快捷按钮打开既有系统。", 4f, 16f, 850f, 38f, 19, Muted);
        }

        private void RenderSparringAndPuzzles()
        {
            // 上方功能切换：演武木桩 / 回合残局 / 万象残局坊（玩家自创工坊）
            DedicatedButtonWithIcon(_actionDeckRow, "icon_sparring_dummy", "演武木桩 (2)", 0f, 4f, 196f, 56f, () =>
            {
                _sparringShowPuzzles = false;
                RenderFeatureBody();
            }, primary: !_sparringShowPuzzles, fontSize: 19);

            DedicatedButtonWithIcon(_actionDeckRow, "icon_tactical_puzzle", "官方残局 (3)", 204f, 4f, 196f, 56f, () =>
            {
                _sparringShowPuzzles = true;
                RenderFeatureBody();
            }, primary: _sparringShowPuzzles, fontSize: 19);

            var workshopBtn = DedicatedButton(_actionDeckRow, "万象残局坊", 408f, 4f, 206f, 56f, () =>
            {
                Hide();
                MmorpgClient.UI.Ugui.TacticalPuzzle.TacticalPuzzleUiRoot.EnsureSpawned();
                MmorpgClient.UI.Ugui.TacticalPuzzle.TacticalPuzzleUiRoot.Instance?.Open();
            }, primary: true, fontSize: 19);
            var workshopIcon = Resources.Load<Sprite>("UI/Ugui/TacticalPuzzleV1/hud_entry_puzzle");
            if (workshopIcon != null)
            {
                var iconImg = QdaoUguiFactory.CreateImage("BtnIcon_Workshop", workshopBtn.transform, 10f, 10f, 36f, 36f, workshopIcon);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                var txt = workshopBtn.GetComponentInChildren<TMP_Text>();
                if (txt != null)
                {
                    txt.rectTransform.anchoredPosition = new Vector2(48f, 0f);
                    txt.rectTransform.sizeDelta = new Vector2(148f, 56f);
                }
            }

            if (_sparringShowPuzzles)
            {
                string toggleLabel = _useFreePresetForOptional ? "残局：借免费预设" : "残局：本人养成";
                DedicatedButton(_actionDeckRow, toggleLabel, 622f, 4f, 238f, 56f, () =>
                {
                    _useFreePresetForOptional = !_useFreePresetForOptional;
                    RenderFeatureBody();
                }, primary: false, fontSize: 18);
            }
            else
            {
                Text(_actionDeckRow, "木桩校验真实加点；残局坊支持玩家自创公平残局。", 624f, 8f, 238f, 48f, 16, Muted, true);
            }

            var all = CityNpcCatalog.FindEncountersForNpc("guiyuan_sparring");
            float y = 6f;
            for (int i = 0; i < all.Count; ++i)
            {
                var enc = all[i];
                bool isDummy = enc.Category == CityNpcEncounterCategory.PracticeDummy;
                if (_sparringShowPuzzles == isDummy) continue;

                bool cleared = !isDummy && (_lastState?.IsPuzzleCleared(enc.DungeonConfigId) ?? false);
                string iconKey = isDummy ? "icon_sparring_dummy" : "icon_tactical_puzzle";
                CreateDedicatedIcon(_featureBody, iconKey, 4f, y + 6f, 46f);

                string badge = isDummy ? "[无损验招]" : (cleared ? $"[已破 · {enc.ClearBadgeName}]" : "[未破阵]");
                Text(_featureBody, $"{enc.Title}  {badge}", 60f, y, 580f, 28f, 20, cleared ? Gold : Ink);
                string desc;
                if (isDummy && _lastState != null && _lastState.TryGetDummyRecord(enc.DungeonConfigId, out var dummyRec))
                {
                    desc = dummyRec.FormatCompactSummary();
                }
                else
                {
                    desc = cleared && !string.IsNullOrEmpty(enc.HiddenEpilogueBody) ? enc.HiddenEpilogueBody : enc.StoryIntro;
                }
                Text(_featureBody, desc, 60f, y + 28f, 584f, isDummy ? 56f : 38f, 16,
                    isDummy && _lastState != null && _lastState.TryGetDummyRecord(enc.DungeonConfigId, out _) ? Ink : Muted, true);

                string btnLabel = isDummy ? "本人试招" : (_useFreePresetForOptional ? "预设破局" : "本人破局");
                DedicatedButton(_featureBody, btnLabel, 658f, y + 8f, 178f, 48f,
                    () => StartEncounterRequested?.Invoke(enc, !isDummy && _useFreePresetForOptional),
                    primary: isDummy || !cleared, fontSize: 19);

                y += isDummy ? 96f : 72f;
            }
        }

        private void RenderCharacterTales(string npcId)
        {
            var tales = CityNpcCatalog.FindEncountersForNpc(npcId);
            if (tales.Count == 0) { RenderAmbientGuidance(); return; }
            var tale = tales[0];
            bool cleared = _lastState?.IsTaleCleared(tale.DungeonConfigId) ?? false;

            CreateDedicatedIcon(_featureBody, "icon_character_tale", 4f, 4f, 50f);
            Text(_featureBody, $"{tale.Title}  {(cleared ? $"【已达 · {tale.ClearBadgeName}】" : "【不限时奇谈】")}",
                64f, 4f, 770f, 30f, 21, Gold);
            Text(_featureBody, tale.Subtitle, 64f, 34f, 770f, 24f, 17, Muted);

            bool showEpilogue = cleared && _expandedStoryDungeonId == tale.DungeonConfigId;
            string bodyText = showEpilogue
                ? $"【{tale.HiddenEpilogueTitle}】{tale.HiddenEpilogueBody}"
                : tale.StoryIntro;
            Text(_featureBody, bodyText, 8f, 66f, 824f, 152f, 19, Ink, true);

            DedicatedButton(_actionDeckRow, "本人阵容开战", 0f, 4f, 240f, 56f,
                () => StartEncounterRequested?.Invoke(tale, false), primary: !cleared, fontSize: 20);
            DedicatedButton(_actionDeckRow, "免费预设试演（跳过对白）", 252f, 4f, 340f, 56f,
                () => StartEncounterRequested?.Invoke(tale, true), primary: true, fontSize: 19);
            if (cleared)
            {
                DedicatedButton(_actionDeckRow, showEpilogue ? "看奇谈引子" : "读隐藏续篇", 604f, 4f, 240f, 56f, () =>
                {
                    _expandedStoryDungeonId = showEpilogue ? 0u : tale.DungeonConfigId;
                    RenderFeatureBody();
                }, primary: false, fontSize: 20);
            }
        }

        private void RenderErrandsAndFerryTale()
        {
            DedicatedButtonWithIcon(_actionDeckRow, "icon_errand_parcel", "顺路捎物 (3)", 0f, 4f, 244f, 56f, () =>
            {
                _ferryShowTale = false;
                RenderFeatureBody();
            }, primary: !_ferryShowTale, fontSize: 20);

            DedicatedButtonWithIcon(_actionDeckRow, "icon_character_tale", "渡口奇谈《月汐渡口》", 256f, 4f, 324f, 56f, () =>
            {
                _ferryShowTale = true;
                RenderFeatureBody();
            }, primary: _ferryShowTale, fontSize: 20);

            if (_ferryShowTale)
            {
                var tales = CityNpcCatalog.FindEncountersForNpc("guiyuan_ferryman");
                if (tales.Count > 0)
                {
                    var tale = tales[0];
                    bool cleared = _lastState?.IsTaleCleared(tale.DungeonConfigId) ?? false;
                    CreateDedicatedIcon(_featureBody, "icon_character_tale", 4f, 4f, 48f);
                    Text(_featureBody, $"{tale.Title} {(cleared ? $"【已达 · {tale.ClearBadgeName}】" : "【3 分钟渡口奇谈】")}",
                        62f, 6f, 770f, 28f, 20, Gold);
                    string body = cleared ? $"【{tale.HiddenEpilogueTitle}】{tale.HiddenEpilogueBody}" : tale.StoryIntro;
                    Text(_featureBody, body, 8f, 58f, 824f, 104f, 18, Ink, true);
                    DedicatedButton(_featureBody, "本人开战", 8f, 170f, 210f, 50f,
                        () => StartEncounterRequested?.Invoke(tale, false), primary: false, fontSize: 19);
                    DedicatedButton(_featureBody, "免费预设试演", 232f, 170f, 240f, 50f,
                        () => StartEncounterRequested?.Invoke(tale, true), primary: true, fontSize: 19);
                }
                return;
            }

            Text(_actionDeckRow, "永不过期，去对应野外图寻宠或逛灵潮自动送达。", 592f, 14f, 270f, 42f, 17, Muted, true);
            float y = 4f;
            for (int i = 0; i < CityNpcCatalog.Errands.Count; ++i)
            {
                var err = CityNpcCatalog.Errands[i];
                var st = _lastState?.GetErrandStatus(err.Id) ?? CityNpcErrandStatus.Available;
                CreateDedicatedIcon(_featureBody, "icon_errand_parcel", 4f, y + 6f, 44f);
                string tag = st switch
                {
                    CityNpcErrandStatus.Active => $"[前往{err.TargetSceneName}自动送达]",
                    CityNpcErrandStatus.ReadyToArchive => "[已送达 · 可归档]",
                    CityNpcErrandStatus.Archived => "[已归档见闻]",
                    _ => "[可接取]"
                };
                Text(_featureBody, $"{err.Title}  {tag}", 58f, y, 596f, 26f, 19, st == CityNpcErrandStatus.ReadyToArchive ? Gold : Ink);
                Text(_featureBody, st == CityNpcErrandStatus.Archived ? err.CompletionNote : err.ParcelDesc,
                    58f, y + 26f, 596f, 40f, 16, Muted, true);

                if (st == CityNpcErrandStatus.Available)
                {
                    DedicatedButton(_featureBody, "顺路接单", 666f, y + 8f, 170f, 46f,
                        () => AcceptErrandRequested?.Invoke(err.Id), primary: false, fontSize: 18);
                }
                else if (st == CityNpcErrandStatus.ReadyToArchive)
                {
                    DedicatedButton(_featureBody, "归档见闻", 666f, y + 8f, 170f, 46f,
                        () => ArchiveErrandRequested?.Invoke(err.Id), primary: true, fontSize: 18);
                }
                else if (st == CityNpcErrandStatus.Active)
                {
                    DedicatedButton(_featureBody, "打开灵潮图", 666f, y + 8f, 170f, 46f,
                        () => ShortcutRequested?.Invoke(CityNpcShortcutKind.WorldMap), primary: false, fontSize: 18);
                }
                y += 74f;
            }
        }

        private void RenderFortuneAndCallingCard()
        {
            // 上方预览区展示「灵潮五行签文」与「江湖名帖」摘要，下方主按钮一键展开原比例签牌/名帖弹层
            CreateDedicatedIcon(_featureBody, "icon_fortune_tube", 4f, 8f, 52f);
            if (_lastState?.LastFortune is WuxingFortuneResult f)
            {
                Text(_featureBody, $"今日五行签：{f.ElementName} · {f.SignGrade}（纯视觉零战力）", 66f, 6f, 766f, 28f, 20, Gold);
                Text(_featureBody, $"{f.Verse}  {f.OverheadTag}", 66f, 36f, 766f, 46f, 18, Ink, true);
            }
            else
            {
                string tideTip = _lastActiveTideSceneId > 1u
                    ? $"当期灵潮正涌「{CityNpcCatalog.ResolveMapDisplayName(_lastActiveTideSceneId)}」，点击下方求取今日五行吉签。"
                    : "求取今日五行吉签，附带 30 分钟纯视觉头顶吉签气泡（零数值、不打卡不亏）。";
                Text(_featureBody, "灵潮五行签文（纯趣味零战力）", 66f, 6f, 766f, 28f, 20, Gold);
                Text(_featureBody, tideTip, 66f, 36f, 766f, 46f, 18, Muted, true);
            }

            CreateDedicatedIcon(_featureBody, "icon_calling_card", 4f, 110f, 52f);
            Text(_featureBody, "人物江湖名帖与纯外观「同行之约」", 66f, 108f, 766f, 28f, 20, Gold);
            if (_lastCallingCard != null)
            {
                string summary =
                    $"{_lastCallingCard.RoleName}（Lv.{_lastCallingCard.Level}）｜流派：{_lastCallingCard.BuildStyleTag}｜" +
                    $"衣柜：{_lastCallingCard.OwnedCharacterCount}/{Math.Max(1, _lastCallingCard.TotalCharacterCount)}｜" +
                    $"战法印：{_lastCallingCard.TrialSealCount} 枚\n同行之约：「{_lastCallingCard.CompanionOathPhrase}」";
                var sumText = Text(_featureBody, summary, 66f, 138f, 766f, 74f, 18, Ink, true);
                sumText.richText = false;
            }

            DedicatedButtonWithIcon(_actionDeckRow, "icon_fortune_tube", "求取五行签牌", 0f, 4f, 276f, 56f, () =>
            {
                DrawFortuneRequested?.Invoke();
                OpenFortuneModal();
            }, primary: true, fontSize: 20);

            DedicatedButtonWithIcon(_actionDeckRow, "icon_calling_card", "展开江湖名帖", 290f, 4f, 276f, 56f,
                OpenCallingCardModal, primary: true, fontSize: 20);

            DedicatedButton(_actionDeckRow, "换一句同行之约", 580f, 4f, 276f, 56f,
                () => CycleCompanionOathRequested?.Invoke(), primary: false, fontSize: 19);
        }

        private void RenderFortuneModal()
        {
            Clear(_fortuneCardBody);
            // 严格 480×680 等比原尺寸展示，不做九宫格拉伸（遵循 ui/README.md）
            var cardImg = CreateOptionalArt(_fortuneCardBody, "fortune_slip_card", "panel_main", 20f, 0f, 480f, 680f, allowSlice: false);
            cardImg.preserveAspect = true;
            cardImg.raycastTarget = true;

            if (_lastState?.LastFortune is WuxingFortuneResult f)
            {
                // 顶部圆形玉徽（cx=240, cy=102, r=43）：显示五行主字（金/木/水/火/土）
                string elemGlyph = string.IsNullOrEmpty(f.ElementName) ? "吉" : f.ElementName.Substring(0, 1);
                Text(_fortuneCardBody, elemGlyph, 20f + 190f, 52f, 100f, 100f, 46, Cream, alignment: TextAlignmentOptions.Center);

                // 中间宣纸签辞区（x=114..366, y=184..549）
                Text(_fortuneCardBody, f.SignGrade, 20f + 114f, 194f, 252f, 44f, 32, Gold, alignment: TextAlignmentOptions.Center);
                Text(_fortuneCardBody, f.ElementName, 20f + 114f, 244f, 252f, 34f, 23, Ink, alignment: TextAlignmentOptions.Center);
                Text(_fortuneCardBody, f.Verse, 20f + 118f, 294f, 244f, 110f, 21, Ink, true, TextAlignmentOptions.Center);
                Text(_fortuneCardBody, f.TravelHint, 20f + 118f, 412f, 244f, 84f, 18, Muted, true, TextAlignmentOptions.Center);
                Text(_fortuneCardBody, $"头顶吉签（30 分钟纯视觉）\n{f.OverheadTag}",
                    20f + 118f, 500f, 244f, 54f, 17, Gold, true, TextAlignmentOptions.Center);
            }

            DedicatedButton(_fortuneCardBody, "重抽今日签", 34f, 696f, 216f, 58f, () =>
            {
                DrawFortuneRequested?.Invoke();
                RenderFortuneModal();
            }, primary: true, fontSize: 21);

            DedicatedButton(_fortuneCardBody, "收起签牌", 270f, 696f, 216f, 58f,
                () => _fortuneModal.gameObject.SetActive(false), primary: false, fontSize: 21);
        }

        private void RenderCallingCardModal()
        {
            Clear(_callingCardBody);
            // 严格按 card_calling_panel.png 1240×720 原布局定位
            var cardImg = CreateOptionalArt(_callingCardBody, "card_calling_panel", "panel_main", 0f, 0f, 1240f, 720f, allowSlice: true);
            cardImg.raycastTarget = true;

            // 顶部玉匾（x=384, y=12, w=472, h=84）
            Text(_callingCardBody, "五行奇谈 · 人物江湖名帖", 392f, 22f, 456f, 64f, 30, Cream, alignment: TextAlignmentOptions.Center);

            var card = _lastCallingCard;
            if (card != null)
            {
                // 左侧角色立绘龛（x=77, y=120, w=322, h=510）
                var charPortrait = QdaoCharacterCatalog.LoadPortrait(card.AppearanceId);
                var portraitImg = QdaoUguiFactory.CreateImage("CallingCardPortrait", _callingCardBody, 94f, 136f, 288f, 400f, charPortrait);
                portraitImg.preserveAspect = true;
                portraitImg.enabled = charPortrait != null;
                portraitImg.raycastTarget = false;

                CreateOptionalArt(_callingCardBody, "npc_name_ribbon", "tab_selected", 92f, 548f, 292f, 56f, allowSlice: true);
                var roleNameText = Text(_callingCardBody, $"{card.RoleName}  Lv.{card.Level}", 112f, 548f, 252f, 56f, 21, Cream, alignment: TextAlignmentOptions.Center);
                roleNameText.richText = false;

                // 右侧三道横线分栏（paperInset x=453, y=120, w=714, h=502；分隔线 y=236 / 368 / 500）
                var row1 = Text(_callingCardBody,
                    $"身披人物：{card.CharacterArchiveName}\n流派气质：{card.BuildStyleTag}   ｜   得意灵宠：{card.ActivePetName}",
                    482f, 138f, 656f, 86f, 22, Ink, true);
                row1.richText = false;

                string seriesText = card.CompletedSeriesBadges.Count > 0 ? string.Join("、", card.CompletedSeriesBadges) : "尚未集齐收藏系列";
                Text(_callingCardBody,
                    $"衣柜收藏：{card.OwnedCharacterCount}/{Math.Max(1, card.TotalCharacterCount)}   ｜   试炼战法印：{card.TrialSealCount} 枚\n系列印章：{seriesText}",
                    482f, 254f, 656f, 96f, 21, Ink, true);

                string honorText = card.SeasonHonorTitles.Count > 0 ? string.Join("、", card.SeasonHonorTitles) : "待解锁赛季永久铭牌";
                Text(_callingCardBody,
                    $"赛季铭牌：{honorText}\n演武残局：{card.ClearedPuzzleCount}/3   ｜   归元奇谈：{card.ClearedTaleCount}/3   ｜   顺路见闻：{card.ArchivedErrandCount}/3",
                    482f, 386f, 656f, 98f, 21, Ink, true);

                Text(_callingCardBody, $"同行之约：「{card.CompanionOathPhrase}」",
                    482f, 522f, 656f, 74f, 24, Gold, true, TextAlignmentOptions.Center);
            }

            DedicatedButton(_callingCardBody, "换一句同行之约", 210f, 732f, 260f, 56f, () =>
            {
                CycleCompanionOathRequested?.Invoke();
                RenderCallingCardModal();
            }, primary: false, fontSize: 20);

            DedicatedButton(_callingCardBody, "复制江湖名帖", 490f, 732f, 260f, 56f,
                () => CopyCallingCardRequested?.Invoke(), primary: true, fontSize: 21);

            DedicatedButton(_callingCardBody, "关闭名帖", 770f, 732f, 260f, 56f,
                () => _callingCardModal.gameObject.SetActive(false), primary: false, fontSize: 20);
        }

        private static Sprite LoadDedicatedSprite(string key) =>
            string.IsNullOrEmpty(key) ? null : Resources.Load<Sprite>(DedicatedArtRoot + key);

        private static Image CreateOptionalArt(UnityEngine.Transform parent, string dedicatedKey, string fallbackKey,
            float x, float y, float w, float h, bool allowSlice)
        {
            var dedicated = LoadDedicatedSprite(dedicatedKey);
            if (dedicated != null)
            {
                var img = QdaoUguiFactory.CreateImage(dedicatedKey, parent, x, y, w, h, dedicated);
                img.type = allowSlice && dedicated.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                return img;
            }
            return Art(parent, fallbackKey, x, y, w, h);
        }

        private static void CreateDedicatedIcon(UnityEngine.Transform parent, string iconKey, float x, float y, float size)
        {
            var sprite = LoadDedicatedSprite(iconKey);
            if (sprite == null) return;
            var img = QdaoUguiFactory.CreateImage(iconKey, parent, x, y, size, size, sprite);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private static Button DedicatedButton(UnityEngine.Transform parent, string label, float x, float y, float w, float h,
            Action onClick, bool primary, int fontSize = 20)
        {
            var btn = Button(parent, label, x, y, w, h, onClick, primary, true, fontSize: fontSize);
            var dedicated = LoadDedicatedSprite(primary ? "npc_action_btn_primary" : "npc_action_btn_secondary");
            if (dedicated != null && btn.image != null)
            {
                btn.image.sprite = dedicated;
                btn.image.type = dedicated.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                var txt = btn.GetComponentInChildren<TMP_Text>();
                if (txt != null) txt.color = primary ? Cream : Ink;
            }
            return btn;
        }

        private static Button DedicatedButtonWithIcon(UnityEngine.Transform parent, string iconKey, string label,
            float x, float y, float w, float h, Action onClick, bool primary, int fontSize = 20)
        {
            var btn = DedicatedButton(parent, label, x, y, w, h, onClick, primary, fontSize);
            var iconSprite = LoadDedicatedSprite(iconKey);
            if (iconSprite != null)
            {
                float iconSize = Mathf.Min(36f, h - 14f);
                var iconImg = QdaoUguiFactory.CreateImage("BtnIcon_" + iconKey, btn.transform, 14f, (h - iconSize) * 0.5f, iconSize, iconSize, iconSprite);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                var txt = btn.GetComponentInChildren<TMP_Text>();
                if (txt != null)
                {
                    var rt = txt.rectTransform;
                    rt.anchoredPosition = new Vector2(iconSize + 20f, 0f);
                    rt.sizeDelta = new Vector2(Mathf.Max(40f, w - iconSize - 32f), h);
                }
            }
            return btn;
        }
    }
}
