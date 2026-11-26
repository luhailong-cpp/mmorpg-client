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
    /// 归元城 8 位原创核心 NPC 交互对话、演武木桩/残局/奇谈、顺路捎物、五行签文与江湖名帖窗口。
    /// 自动优先加载 Codex 切图目录（UI/Ugui/GuiyuanNpcV1/），缺图时无缝回退至项目通用底板与按钮。
    /// </summary>
    public sealed class CityNpcWindow
    {
        public const string DedicatedArtRoot = "UI/Ugui/GuiyuanNpcV1/";

        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
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
        private readonly Image _portrait;
        private readonly TMP_Text _npcName;
        private readonly TMP_Text _npcRole;
        private readonly TMP_Text _characterTag;
        private readonly TMP_Text _greetingBody;
        private readonly TMP_Text _statusText;
        private readonly RectTransform _npcRosterContent;
        private readonly RectTransform _shortcutRow;
        private readonly RectTransform _featureBody;
        private readonly List<Button> _rosterButtons = new();

        private CityNpcDefinition _currentNpc;
        private bool _useFreePresetForOptional = true;
        private uint _expandedStoryDungeonId;

        public CityNpcWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("CityNpcWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(0.03f, 0.08f, 0.07f, 0.72f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();

            var frame = QdaoUguiFactory.CreateCenteredRect("CityNpcFrame", _root, 1840, 880);
            var panelBg = CreateOptionalArt(frame, "npc_dialog_panel", "panel_main", 0, 0, 1840, 880);
            panelBg.raycastTarget = true;

            Text(frame, "归元城 · 街坊见闻与人物玩法", 48, 22, 680, 48, 34, Ink);
            Button(frame, "关闭", 1680, 20, 120, 52, Hide, false, true, fontSize: 26);

            // 左侧：归元城 8 位原创 NPC 快速名录（可一键切换查看或寻路前往）
            var rosterPanel = CreateOptionalArt(frame, "npc_roster_bg", "panel_sub", 36, 92, 290, 744);
            rosterPanel.color = new Color(1f, 1f, 1f, 0.92f);
            Text(rosterPanel.transform, "归元八友", 18, 12, 250, 36, 25, Gold, alignment: TextAlignmentOptions.Center);
            _npcRosterContent = QdaoUguiFactory.CreateRect("NpcRosterList", rosterPanel.transform, 14, 56, 262, 676);

            // 中左：当前 NPC 立绘 + 姓名称谓 + 寻路按钮
            var portraitPanel = CreateOptionalArt(frame, "npc_portrait_bg", "panel_sub", 344, 92, 360, 744);
            portraitPanel.color = new Color(1f, 1f, 1f, 0.94f);
            _portrait = QdaoUguiFactory.CreateImage("NpcPortrait", portraitPanel.transform, 20, 24, 320, 440, null);
            _portrait.preserveAspect = true;
            _portrait.raycastTarget = false;
            CreateOptionalArt(portraitPanel.transform, "npc_name_ribbon", "tab_selected", 24, 480, 312, 58);
            _npcName = Text(portraitPanel.transform, "", 32, 480, 296, 58, 30, Cream, alignment: TextAlignmentOptions.Center);
            _npcRole = Text(portraitPanel.transform, "", 24, 548, 312, 64, 22, Muted, true, TextAlignmentOptions.Top);
            Button(portraitPanel.transform, "自动寻路前往", 44, 642, 272, 62, () =>
            {
                if (_currentNpc != null) WalkToNpcRequested?.Invoke(_currentNpc);
            }, false, true, fontSize: 25);

            // 右侧上方：看人下菜碟专属对话 + 快捷系统直达
            var dialogPanel = CreateOptionalArt(frame, "npc_dialog_sub", "panel_sub", 722, 92, 1082, 264);
            _characterTag = Text(dialogPanel.transform, "", 26, 14, 1030, 34, 22, Gold);
            _greetingBody = Text(dialogPanel.transform, "", 26, 54, 1030, 118, 25, Ink, true);
            _shortcutRow = QdaoUguiFactory.CreateRect("NpcShortcutRow", dialogPanel.transform, 24, 182, 1034, 68);

            // 右侧下方：专属玩法交互区（演武木桩/残局/奇谈/顺路捎物/签文/江湖名帖）
            var featurePanel = CreateOptionalArt(frame, "npc_feature_sub", "panel_sub", 722, 372, 1082, 412);
            _featureBody = QdaoUguiFactory.CreateRect("NpcFeatureBody", featurePanel.transform, 20, 14, 1042, 384);

            // 底栏状态提示
            _statusText = Text(frame, "", 724, 796, 1076, 44, 22, Muted);
            BuildRosterButtons();
        }

        private void BuildRosterButtons()
        {
            Clear(_npcRosterContent);
            _rosterButtons.Clear();
            float y = 0f;
            for (int i = 0; i < CityNpcCatalog.Npcs.Count; ++i)
            {
                var npc = CityNpcCatalog.Npcs[i];
                var btn = Button(_npcRosterContent, $"{npc.Name} · {npc.RoleTitle}", 0f, y, 262f, 74f,
                    () => SelectNpcRequested?.Invoke(npc), false, true, fontSize: 21);
                _rosterButtons.Add(btn);
                y += 82f;
            }
        }

        public void Show(CityNpcDefinition npc, string activeAppearanceId, string activeCharacterArchiveName,
            CityNpcState state, CharacterCallingCardView callingCard, uint activeTideSceneId, string statusNotice = "")
        {
            _currentNpc = npc ?? CityNpcCatalog.Npcs[0];
            _root.gameObject.SetActive(true);
            Refresh(activeAppearanceId, activeCharacterArchiveName, state, callingCard, activeTideSceneId, statusNotice);
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Refresh(string activeAppearanceId, string activeCharacterArchiveName,
            CityNpcState state, CharacterCallingCardView callingCard, uint activeTideSceneId, string statusNotice = "")
        {
            if (_currentNpc == null || !IsVisible) return;

            // 高亮左侧名录
            for (int i = 0; i < _rosterButtons.Count && i < CityNpcCatalog.Npcs.Count; ++i)
            {
                bool selected = string.Equals(CityNpcCatalog.Npcs[i].Id, _currentNpc.Id, StringComparison.Ordinal);
                var img = _rosterButtons[i].image;
                if (img != null) img.sprite = Load(selected ? "tab_selected" : "button_secondary");
                var lbl = _rosterButtons[i].GetComponentInChildren<TMP_Text>();
                if (lbl != null) lbl.color = selected ? Cream : Ink;
            }

            // 立绘（优先 Codex 专属立绘，缺图回退项目自有原创立绘）
            var sprite = Resources.Load<Sprite>(_currentNpc.DedicatedSpriteKey)
                         ?? QdaoCharacterCatalog.LoadPortrait(_currentNpc.FallbackAppearanceId);
            _portrait.sprite = sprite;
            _portrait.enabled = sprite != null;
            _npcName.text = _currentNpc.Name;
            _npcRole.text = $"{_currentNpc.RoleTitle}\n常驻：归元城 · {_currentNpc.LandmarkLabel}";

            // 看人下菜碟台词
            bool matchedChar = _currentNpc.TryGetCharacterGreeting(activeAppearanceId, out string greeting);
            string charTitle = string.IsNullOrWhiteSpace(activeCharacterArchiveName) ? "当前人物" : activeCharacterArchiveName;
            _characterTag.text = matchedChar
                ? $"★ 人物专属互动（当前身披：{charTitle}）"
                : $"归元城见闻（换上不同人物或免费试用可触发不同专属对白）";
            _greetingBody.text = greeting;

            // 快捷系统直达按钮
            Clear(_shortcutRow);
            float sx = 0f;
            for (int i = 0; i < _currentNpc.Shortcuts.Count; ++i)
            {
                var sc = _currentNpc.Shortcuts[i];
                Button(_shortcutRow, sc.Label, sx, 4f, 326f, 58f,
                    () => ShortcutRequested?.Invoke(sc.Kind), false, true, fontSize: 22);
                sx += 344f;
            }

            // 专属玩法交互体
            RenderFeatureBody(state, callingCard, activeTideSceneId);
            _statusText.text = string.IsNullOrEmpty(statusNotice) ? (state?.LastBannerNotice ?? "") : statusNotice;
        }

        private void RenderFeatureBody(CityNpcState state, CharacterCallingCardView callingCard, uint activeTideSceneId)
        {
            Clear(_featureBody);
            switch (_currentNpc.FeatureKind)
            {
                case CityNpcFeatureKind.SparringAndPuzzles:
                    RenderSparringAndPuzzles(state);
                    break;
                case CityNpcFeatureKind.CharacterTale:
                    RenderCharacterTales(state, _currentNpc.Id);
                    break;
                case CityNpcFeatureKind.LightErrands:
                    RenderErrandsAndFerryTale(state);
                    break;
                case CityNpcFeatureKind.FortuneAndCallingCard:
                    RenderFortuneAndCallingCard(state, callingCard, activeTideSceneId);
                    break;
                default:
                    RenderAmbientGuidance();
                    break;
            }
        }

        private void RenderAmbientGuidance()
        {
            Text(_featureBody, $"【{_currentNpc.Name}的街坊指点】", 12, 12, 980, 38, 26, Gold);
            Text(_featureBody,
                "归元城内全系统仍保留快捷栏一键打开，逛街找街坊聊天可触发 9 位原创主角专属彩蛋。\n\n" +
                "• 找【齐问锋】可打零消耗「演武试招木桩」验加点/战法与「回合残局 3 关」；\n" +
                "• 找【顾青崖 / 温半夏 / 周行舟】可体验 3 篇不限时「归元人物奇谈」与「顺路捎物」；\n" +
                "• 找【莫听风】可求「灵潮五行签文」、生成「人物江湖名帖」与设定纯外观「同行之约」。",
                12, 60, 1010, 290, 23, Ink, true);
        }

        private void RenderSparringAndPuzzles(CityNpcState state)
        {
            Text(_featureBody, "演武试招木桩（强制本人真实养成 · 0 消耗 0 刷取）", 8, 4, 640, 34, 24, Gold);
            var presetToggleLabel = _useFreePresetForOptional ? "残局模式：借用免费 3×3 预设（点此切换本人）" : "残局模式：使用本人真实养成（点此切换预设）";
            Button(_featureBody, presetToggleLabel, 632, 0, 404, 42, () =>
            {
                _useFreePresetForOptional = !_useFreePresetForOptional;
                if (_currentNpc != null) SelectNpcRequested?.Invoke(_currentNpc);
            }, false, true, fontSize: 18);

            var list = CityNpcCatalog.FindEncountersForNpc("guiyuan_sparring");
            float y = 50f;
            for (int i = 0; i < list.Count; ++i)
            {
                var enc = list[i];
                bool isDummy = enc.Category == CityNpcEncounterCategory.PracticeDummy;
                bool cleared = !isDummy && (state?.IsPuzzleCleared(enc.DungeonConfigId) ?? false);
                string badge = isDummy ? "[无损验招]" : (cleared ? $"[已破 · {enc.ClearBadgeName}]" : "[未破阵]");
                Text(_featureBody, $"{enc.Title}  {badge}", 10, y, 700, 30, 22, cleared ? Gold : Ink);
                Text(_featureBody, cleared && !string.IsNullOrEmpty(enc.HiddenEpilogueBody) ? enc.HiddenEpilogueBody : enc.StoryIntro,
                    10, y + 30f, 760, 32, 18, Muted);
                string btnLabel = isDummy ? "本人试招" : (_useFreePresetForOptional ? "预设破局" : "本人破局");
                Button(_featureBody, btnLabel, 800, y + 6f, 226, 52f,
                    () => StartEncounterRequested?.Invoke(enc, !isDummy && _useFreePresetForOptional),
                    isDummy || !cleared, true, fontSize: 21);
                y += 66f;
            }
        }

        private void RenderCharacterTales(CityNpcState state, string npcId)
        {
            var tales = CityNpcCatalog.FindEncountersForNpc(npcId);
            if (tales.Count == 0) { RenderAmbientGuidance(); return; }
            var tale = tales[0];
            bool cleared = state?.IsTaleCleared(tale.DungeonConfigId) ?? false;
            Text(_featureBody, $"{tale.Title}  {(cleared ? $"【已达 · {tale.ClearBadgeName}】" : "【不限时奇谈 · 可跳过对白】")}",
                10, 8, 1020, 38, 26, Gold);
            Text(_featureBody, tale.Subtitle, 10, 48, 1020, 30, 20, Muted);

            bool showEpilogue = cleared && _expandedStoryDungeonId == tale.DungeonConfigId;
            string bodyText = showEpilogue
                ? $"【{tale.HiddenEpilogueTitle}】\n{tale.HiddenEpilogueBody}"
                : tale.StoryIntro;
            Text(_featureBody, bodyText, 10, 86, 1020, 206, 23, Ink, true);

            Button(_featureBody, "本人阵容开战", 10, 310, 240, 58,
                () => StartEncounterRequested?.Invoke(tale, false), !cleared, true, fontSize: 22);
            Button(_featureBody, "免费预设试演（跳过对白）", 268, 310, 330, 58,
                () => StartEncounterRequested?.Invoke(tale, true), true, true, fontSize: 21);
            if (cleared)
            {
                Button(_featureBody, showEpilogue ? "看奇谈引子" : "读隐藏续篇", 616, 310, 220, 58, () =>
                {
                    _expandedStoryDungeonId = showEpilogue ? 0u : tale.DungeonConfigId;
                    if (_currentNpc != null) SelectNpcRequested?.Invoke(_currentNpc);
                }, false, true, fontSize: 21);
            }
        }

        private void RenderErrandsAndFerryTale(CityNpcState state)
        {
            Text(_featureBody, "不限时「顺路捎物」（去野外抓宠/逛灵潮自动送达，绝不催打卡）", 8, 4, 1020, 32, 23, Gold);
            float y = 42f;
            for (int i = 0; i < CityNpcCatalog.Errands.Count; ++i)
            {
                var err = CityNpcCatalog.Errands[i];
                var st = state?.GetErrandStatus(err.Id) ?? CityNpcErrandStatus.Available;
                string tag = st switch
                {
                    CityNpcErrandStatus.Active => $"[进行中 · 前往{err.TargetSceneName}自动送达]",
                    CityNpcErrandStatus.ReadyToArchive => "[已送达 · 可归档]",
                    CityNpcErrandStatus.Archived => "[已归档见闻]",
                    _ => "[可接取]"
                };
                Text(_featureBody, $"{err.Title}  {tag}", 10, y, 750, 28, 21, st == CityNpcErrandStatus.ReadyToArchive ? Gold : Ink);
                Text(_featureBody, st == CityNpcErrandStatus.Archived ? err.CompletionNote : err.ParcelDesc, 10, y + 28f, 760, 28, 18, Muted);
                if (st == CityNpcErrandStatus.Available)
                {
                    Button(_featureBody, "顺路接单", 804, y + 4f, 220, 48f,
                        () => AcceptErrandRequested?.Invoke(err.Id), false, true, fontSize: 20);
                }
                else if (st == CityNpcErrandStatus.ReadyToArchive)
                {
                    Button(_featureBody, "归档见闻", 804, y + 4f, 220, 48f,
                        () => ArchiveErrandRequested?.Invoke(err.Id), true, true, fontSize: 20);
                }
                else if (st == CityNpcErrandStatus.Active)
                {
                    Button(_featureBody, "打开灵潮图", 804, y + 4f, 220, 48f,
                        () => ShortcutRequested?.Invoke(CityNpcShortcutKind.WorldMap), false, true, fontSize: 20);
                }
                y += 66f;
            }

            // 底部挂接周行舟专属奇谈《月汐渡口》
            var ferryTales = CityNpcCatalog.FindEncountersForNpc("guiyuan_ferryman");
            if (ferryTales.Count > 0)
            {
                var tale = ferryTales[0];
                bool cleared = state?.IsTaleCleared(tale.DungeonConfigId) ?? false;
                Text(_featureBody, $"{tale.Title} {(cleared ? "[已达 · 已解锁续篇]" : "[3 分钟渡口奇谈]")}",
                    10, 252, 1020, 30, 22, Gold);
                Text(_featureBody, cleared ? $"【{tale.HiddenEpilogueTitle}】{tale.HiddenEpilogueBody}" : tale.StoryIntro,
                    10, 282, 650, 96, 18, Ink, true);
                Button(_featureBody, "本人开战", 680, 294, 166, 56,
                    () => StartEncounterRequested?.Invoke(tale, false), false, true, fontSize: 20);
                Button(_featureBody, "预设试演", 858, 294, 166, 56,
                    () => StartEncounterRequested?.Invoke(tale, true), true, true, fontSize: 20);
            }
        }

        private void RenderFortuneAndCallingCard(CityNpcState state, CharacterCallingCardView card, uint activeTideSceneId)
        {
            // 左半区：灵潮五行签文
            var fortunePanel = CreateOptionalArt(_featureBody, "fortune_slip_card", "panel_sub", 4, 4, 466, 372);
            Text(fortunePanel.transform, "灵潮五行签文（纯趣味零战力）", 16, 12, 434, 34, 23, Gold);
            if (state?.LastFortune is WuxingFortuneResult f)
            {
                Text(fortunePanel.transform, $"{f.ElementName} · {f.SignGrade}", 16, 56, 434, 38, 26, Ink);
                Text(fortunePanel.transform, $"{f.Verse}\n\n{f.TravelHint}\n\n头顶吉签（30 分钟纯视觉）：{f.OverheadTag}",
                    16, 102, 434, 190, 20, Muted, true);
            }
            else
            {
                string tideTip = activeTideSceneId > 1u
                    ? $"当期灵潮正涌「{CityNpcCatalog.ResolveMapDisplayName(activeTideSceneId)}」，点击下方求取今日五行吉签。"
                    : "求取今日五行吉签，附带 30 分钟纯视觉头顶吉签气泡（零数值、不打卡不亏）。";
                Text(fortunePanel.transform, tideTip, 16, 72, 434, 210, 21, Ink, true);
            }
            Button(fortunePanel.transform, "求取今日五行签", 88, 304, 290, 54,
                () => DrawFortuneRequested?.Invoke(), true, true, fontSize: 22);

            // 右半区：人物「江湖名帖」与纯外观「同行之约」
            var cardPanel = CreateOptionalArt(_featureBody, "card_calling_panel", "panel_sub", 486, 4, 550, 372);
            Text(cardPanel.transform, "人物江湖名帖与同行之约", 18, 12, 514, 34, 23, Gold);
            if (card != null)
            {
                string seriesText = card.CompletedSeriesBadges.Count > 0 ? string.Join("、", card.CompletedSeriesBadges) : "尚未集齐系列";
                string honorText = card.SeasonHonorTitles.Count > 0 ? string.Join("、", card.SeasonHonorTitles) : "待解赛季铭牌";
                string detail =
                    $"{card.RoleName}（Lv.{card.Level}）｜身披：{card.CharacterArchiveName}\n" +
                    $"流派气质：{card.BuildStyleTag}｜得意灵宠：{card.ActivePetName}\n" +
                    $"衣柜收藏：{card.OwnedCharacterCount}/{Math.Max(1, card.TotalCharacterCount)}｜试炼战法印：{card.TrialSealCount} 枚\n" +
                    $"系列印章：{seriesText}\n" +
                    $"赛季铭牌：{honorText}\n" +
                    $"残局：{card.ClearedPuzzleCount}/3｜奇谈：{card.ClearedTaleCount}/3｜顺路见闻：{card.ArchivedErrandCount}/3\n" +
                    $"同行之约：「{card.CompanionOathPhrase}」";
                var cardText = Text(cardPanel.transform, detail, 18, 52, 514, 240, 19, Ink, true);
                cardText.richText = false;
            }
            Button(cardPanel.transform, "换一句同行之约", 18, 304, 246, 54,
                () => CycleCompanionOathRequested?.Invoke(), false, true, fontSize: 20);
            Button(cardPanel.transform, "复制江湖名帖", 282, 304, 246, 54,
                () => CopyCallingCardRequested?.Invoke(), true, true, fontSize: 20);
        }

        private static Image CreateOptionalArt(UnityEngine.Transform parent, string dedicatedKey, string fallbackKey,
            float x, float y, float w, float h)
        {
            var dedicated = Resources.Load<Sprite>(DedicatedArtRoot + dedicatedKey);
            if (dedicated != null)
            {
                var img = QdaoUguiFactory.CreateImage(dedicatedKey, parent, x, y, w, h, dedicated);
                img.type = dedicated.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                return img;
            }
            return Art(parent, fallbackKey, x, y, w, h);
        }
    }
}
