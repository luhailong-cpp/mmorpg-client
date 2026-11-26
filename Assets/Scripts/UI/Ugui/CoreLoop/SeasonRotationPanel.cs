using System.Collections.Generic;
using Match;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui.Team;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>首季娱乐轮换；免费人物、灵宠预设只在本页出现（含 V2 4 技能预览与 5V5 排队条）。</summary>
    public sealed class SeasonRotationPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;
        private GameClient _game;
        private uint _characterPresetId;
        private uint _petPresetId;
        private uint _inspectedPresetId;
        private bool _skillDetailOpen;
        private bool _queuePromptDismissed;
        private uint _lastQueuedSeconds;
        private float _nextQueueRefreshAt;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _core = core;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show()
        {
            if (IsVisible) return;
            gameObject.SetActive(true);
            if (_game?.Ranked?.Profile == null) _game?.Ranked?.RequestProfile();
            Refresh();
        }

        public void Hide()
        {
            _skillDetailOpen = false;
            gameObject.SetActive(false);
        }

        public void Tick()
        {
            if (!IsVisible || _game?.Battle == null || Time.unscaledTime < _nextQueueRefreshAt) return;
            _nextQueueRefreshAt = Time.unscaledTime + 1f;
            uint seconds = _game.Battle.LastQueueStatus?.QueuedSeconds ?? 0u;
            if (seconds != _lastQueuedSeconds) Refresh();
        }

        public void Refresh()
        {
            if (_root == null || _core == null || _game == null) return;
            bool hasAuthoritativeWeek = CoreGameplayLoopModels.TryResolveAuthoritativeSeasonWeek(
                _game.Season?.Snapshot, out uint authoritativeWeek);
            var rotations = _core.BuildRotations(hasAuthoritativeWeek ? authoritativeWeek : 1u);
            var profile = _game.Ranked?.Profile;
            ValidatePresetSelection(profile);
            CoreLoopUiArt.Clear(transform);
            CoreLoopUiArt.Frame(transform, "首季轮换 · 娱乐场", _root.OpenHub);
            SeasonEconomyUiArt.DrawSeasonLobbyTabs(transform, 1,
                _root.OpenBossTrials, _root.OpenRotations, _root.OpenFormalPvp);
            CoreLoopUiArt.Text("Intro", transform, 290, 267, 1940, 55,
                hasAuthoritativeWeek
                    ? $"本季第 {authoritativeWeek} 周 · 免费人宠仅用于本页娱乐场，不带入正式排位；两种玩法均不计正式评分。"
                    : "免费人宠只用于本页娱乐场，不带入正式排位；两种玩法均不计正式评分。", 27,
                CoreLoopUiArt.Muted, false, true);

            if (rotations != null)
            {
                for (int i = 0; i < rotations.Count && i < 2; ++i)
                {
                    var card = rotations[i];
                    if (card == null) continue;
                    float x = 285 + i * 1010;
                    bool highlightActive = hasAuthoritativeWeek && card.ActiveNow;
                    CoreLoopUiArt.Card(transform, "RotationCard" + i, x, 338, 980, 245, highlightActive);
                    float titleX = x + 30f;
                    if (!card.IsPvp)
                    {
                        var guardBadge = QdaoUguiFactory.CreateImage("RotationModeIcon" + i, transform, x + 26, 346, 52, 52,
                            SeasonEconomyUiArt.LoadIcon("badge_protect_guard"), false);
                        guardBadge.preserveAspect = true;
                        if (guardBadge.sprite != null) titleX = x + 86f;
                        else guardBadge.gameObject.SetActive(false);
                    }
                    else
                    {
                        var physIcon = QdaoUguiFactory.CreateImage("RotationPhysIcon" + i, transform, x + 26, 350, 42, 42,
                            SeasonEconomyUiArt.LoadIcon("badge_polarity_phys"), false);
                        physIcon.preserveAspect = true;
                        var magicIcon = QdaoUguiFactory.CreateImage("RotationMagicIcon" + i, transform, x + 72, 350, 42, 42,
                            SeasonEconomyUiArt.LoadIcon("badge_polarity_magic"), false);
                        magicIcon.preserveAspect = true;
                        if (physIcon.sprite != null || magicIcon.sprite != null) titleX = x + 122f;
                        physIcon.gameObject.SetActive(physIcon.sprite != null);
                        magicIcon.gameObject.SetActive(magicIcon.sprite != null);
                    }
                    CoreLoopUiArt.Text("RotationTitle" + i, transform, titleX, 350, (x + 710f) - titleX, 50,
                        card.Title, 34, CoreLoopUiArt.DeepJade, true);
                    CoreLoopUiArt.Badge(transform, "RotationWindow" + i, x + 715, 353, 225,
                        card.IsPvp ? $"仅 {card.FixedTeamSize}V{card.FixedTeamSize}" : "PVE · 1–5 人",
                        CoreLoopUiArt.Jade);
                    string availabilityText = hasAuthoritativeWeek
                        ? (card.ActiveNow ? "当前周开放" : "非当前轮换周")
                        : "首测开放以服务端准入为准";
                    CoreLoopUiArt.Text("RotationAvailability" + i, transform, x + 600, 523, 330, 34,
                        availabilityText, 20, highlightActive ? CoreLoopUiArt.Jade : CoreLoopUiArt.Warning,
                        highlightActive, false, TextAlignmentOptions.Right);
                    CoreLoopUiArt.Text("RotationDates" + i, transform, x + 30, 407, 900, 34,
                        card.WindowLabel, 23, CoreLoopUiArt.Gold, true);
                    CoreLoopUiArt.Text("RotationRule" + i, transform, x + 30, 448, 900, 62,
                        card.RulesSummary, 23, CoreLoopUiArt.Ink, false, true);
                    var tradableIcon = QdaoUguiFactory.CreateImage("RotationTradableIcon" + i, transform, x + 28, 522, 36, 36,
                        SeasonEconomyUiArt.LoadIcon("badge_tradable_tag"), false);
                    tradableIcon.preserveAspect = true;
                    tradableIcon.gameObject.SetActive(tradableIcon.sprite != null);
                    CoreLoopUiArt.Text("RotationReward" + i, transform, tradableIcon.sprite != null ? x + 70 : x + 30, 520, 520, 45,
                        card.RewardSummary, 21, CoreLoopUiArt.Muted, false, true);
                }
            }

            var phase = _game.Battle?.Phase ?? BattlePhase.None;
            if (phase != BattlePhase.Queued) _queuePromptDismissed = false;
            CoreLoopUiArt.Text("PresetHeading", transform, 295, 598, 920, 42,
                "免费 3×3 人宠预设（V2 四技能满槽） · 仅本娱乐局生效", 28, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Button("ToggleSkillSheet", transform, 1930, 595, 325, 48,
                _skillDetailOpen ? "收起招式明细" : "查看 6 套招式详情",
                () => { _skillDetailOpen = !_skillDetailOpen; Refresh(); }, profile != null, false, 21);
            bool hasPresets = _characterPresetId != 0 && _petPresetId != 0;
            bool inQueue = phase == BattlePhase.Queued;
            bool cancelling = _core.FallbackSwitchState == QueueFallbackSwitchState.Cancelling
                || (_game.Battle?.IsCancellingQueue ?? false);
            DrawPresets(profile, !inQueue && !cancelling && phase == BattlePhase.None);
            DrawSelectedSkillsStrip(profile);
            if (inQueue || cancelling)
            {
                DrawRotationQueueBar(inQueue, cancelling);
            }
            else
            {
                CoreLoopUiArt.Button("EnterRescue", transform, 524, 882, 650, 72,
                    "护卫救援 · 前往试炼", () => _core.EnterGuardRescue(_characterPresetId, _petPresetId),
                    hasPresets && phase == BattlePhase.None, true, 28, "btn_primary_gold");
                CoreLoopUiArt.Button("EnterPolarity", transform, 1370, 882, 650, 72,
                    "物法易势 5V5 · 前往匹配", () => _core.EnterPolarity5v5(_characterPresetId, _petPresetId),
                    hasPresets && phase == BattlePhase.None, true, 28, "btn_primary_gold");
            }
            if (_skillDetailOpen && profile != null)
                DrawAllPresetSkillsOverlay(profile);
        }

        private void ValidatePresetSelection(RankedProfileSnapshot profile)
        {
            if (profile == null) { _characterPresetId = _petPresetId = 0u; return; }
            bool characterFound = false;
            bool petFound = false;
            if (profile.CharacterPresets != null)
                for (int i = 0; i < profile.CharacterPresets.Count; ++i)
                    if (profile.CharacterPresets[i].PresetId == _characterPresetId) characterFound = true;
            if (profile.PetPresets != null)
                for (int i = 0; i < profile.PetPresets.Count; ++i)
                    if (profile.PetPresets[i].PresetId == _petPresetId) petFound = true;
            if (!characterFound) _characterPresetId = profile.CharacterPresets != null && profile.CharacterPresets.Count > 0
                ? profile.CharacterPresets[0].PresetId : 0u;
            if (!petFound) _petPresetId = profile.PetPresets != null && profile.PetPresets.Count > 0
                ? profile.PetPresets[0].PresetId : 0u;
        }

        private void DrawPresets(RankedProfileSnapshot profile, bool interactable)
        {
            if (profile?.CharacterPresets != null)
                for (int i = 0; i < profile.CharacterPresets.Count && i < 3; ++i)
                {
                    var preset = profile.CharacterPresets[i];
                    if (preset == null) continue;
                    float x = 295 + i * 325;
                    DrawPreset("Character" + i, x, 668, preset, preset.PresetId == _characterPresetId,
                        interactable, () => { _characterPresetId = preset.PresetId; Refresh(); });
                }
            if (profile?.PetPresets != null)
                for (int i = 0; i < profile.PetPresets.Count && i < 3; ++i)
                {
                    var preset = profile.PetPresets[i];
                    if (preset == null) continue;
                    float x = 1305 + i * 325;
                    DrawPreset("Pet" + i, x, 668, preset, preset.PresetId == _petPresetId,
                        interactable, () => { _petPresetId = preset.PresetId; Refresh(); });
                }
            CoreLoopUiArt.Text("CharacterKind", transform, 295, 642, 930, 26,
                "人物 · 三选一", 21, CoreLoopUiArt.Muted, true);
            CoreLoopUiArt.Text("PetKind", transform, 1305, 642, 930, 26,
                "宝宝 · 三选一", 21, CoreLoopUiArt.Muted, true);
        }

        private void DrawPreset(string key, float x, float y, RankedPresetView preset,
            bool selected, bool interactable, System.Action select)
        {
            CoreLoopUiArt.Card(transform, key + "Card", x, y, 300, 128, selected);
            CoreLoopUiArt.Text(key + "Name", transform, x + 14, y + 8, 272, 36,
                preset.Name, 25, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text(key + "Role", transform, x + 14, y + 46, 272, 32,
                preset.RoleTag, 19, CoreLoopUiArt.Muted);
            CoreLoopUiArt.Badge(transform, key + "Badge", x + 14, y + 82, 168,
                selected ? "已选" : "可选", selected ? CoreLoopUiArt.Jade : CoreLoopUiArt.Muted);
            var hit = QdaoUguiFactory.CreateHitButton(key + "Select", transform, x, y, 300, 128);
            hit.interactable = interactable;
            hit.onClick.AddListener(() => select());
        }

        private void DrawSelectedSkillsStrip(RankedProfileSnapshot profile)
        {
            var charPreset = FindPreset(profile?.CharacterPresets, _characterPresetId);
            var petPreset = FindPreset(profile?.PetPresets, _petPresetId);
            CoreLoopUiArt.Image("SelectedSkillsStrip", transform, 295, 806, 1960, 64,
                "hud-strip", new Color(.94f, .92f, .84f, .95f));
            if (charPreset == null && petPreset == null)
            {
                CoreLoopUiArt.Text("PresetPending", transform, 320, 818, 1900, 40,
                    "正在同步免费预设，完成后可查看 4 技能明细并入场。", 21, CoreLoopUiArt.Warning);
                return;
            }
            string charLine = charPreset != null
                ? $"人物【{charPreset.Name}】招式：{FormatSkillList(charPreset.SkillIds)}"
                : "人物招式待同步";
            string petLine = petPreset != null
                ? $"灵宠【{petPreset.Name}】招式：{FormatSkillList(petPreset.SkillIds)}"
                : "灵宠招式待同步";
            var leftTxt = CoreLoopUiArt.Text("CharSkillsSummary", transform, 316, 814, 940, 48,
                charLine, 19, CoreLoopUiArt.Ink, false, true);
            leftTxt.enableAutoSizing = true; leftTxt.fontSizeMin = 14; leftTxt.fontSizeMax = 19;
            var rightTxt = CoreLoopUiArt.Text("PetSkillsSummary", transform, 1284, 814, 950, 48,
                petLine, 19, CoreLoopUiArt.Ink, false, true);
            rightTxt.enableAutoSizing = true; rightTxt.fontSizeMin = 14; rightTxt.fontSizeMax = 19;
        }

        private void DrawRotationQueueBar(bool inQueue, bool cancelling)
        {
            var queue = _game.Battle?.LastQueueStatus;
            _lastQueuedSeconds = queue?.QueuedSeconds ?? 0u;
            uint waitPrompt = queue != null && queue.WaitPromptSeconds != 0u
                ? queue.WaitPromptSeconds
                : (_game.Ranked?.Profile?.WaitPromptSeconds ?? RankedFormatting.DefaultWaitPromptSeconds);
            string spread = queue != null && queue.CurrentRatingSpread > 0u
                ? $" · 当前分差 ±{queue.CurrentRatingSpread}" : string.Empty;
            string headline = cancelling
                ? "正在取消物法易势 5V5 匹配，请稍候…"
                : $"物法易势 5V5 匹配中 · 已等 {RankedFormatting.FormatElapsedClock(_lastQueuedSeconds)} / {RankedFormatting.FormatElapsedClock(waitPrompt)}{spread}";
            CoreLoopUiArt.Card(transform, "RotationQueueBar", 295, 878, 1960, 82, true);
            CoreLoopUiArt.Text("RotationQueueText", transform, 320, 894, 1080, 48,
                headline, 22, CoreLoopUiArt.DeepJade, true, true);
            bool timeoutReached = !cancelling && inQueue && _lastQueuedSeconds >= waitPrompt && !_queuePromptDismissed;
            if (timeoutReached)
            {
                CoreLoopUiArt.Button("RotationContinueWait", transform, 1418, 892, 180, 54,
                    "继续等待", () => { _queuePromptDismissed = true; Refresh(); }, true, false, 20);
            }
            CoreLoopUiArt.Button("RotationSwitchBoss", transform, 1612, 892, 340, 54,
                cancelling ? "正在取消…" : "取消并去单人试炼",
                () => _core.CancelQueueAndSwitchToBossTrial(), !cancelling, timeoutReached, 20);
            CoreLoopUiArt.Button("RotationCancelQueue", transform, 1968, 892, 260, 54,
                cancelling ? "正在取消…" : "取消匹配", CancelQueue, !cancelling, false, 21);
        }

        private void DrawAllPresetSkillsOverlay(RankedProfileSnapshot profile)
        {
            var dim = QdaoUguiFactory.CreateImage("SkillSheetDim", transform, 0f, 0f, 2560f, 1080f, null, true);
            dim.color = new Color(0.03f, 0.06f, 0.05f, 0.58f);
            // 布局对齐 layout-contract.json rotation.detailModal [574, 291, 1414, 568]（向上扩展顶栏放 6 套预设切换键，避开胶囊 x<=2200）
            CoreLoopUiArt.Card(transform, "SkillSheetCard", 460f, 180f, 1640f, 740f, true);
            CoreLoopUiArt.Text("SkillSheetTitle", transform, 496f, 200f, 1320f, 48f,
                "首季娱乐场 · 6 套免费人宠预设 4 技能全览", 32f, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Button("SkillSheetClose", transform, 1900f, 198f, 164f, 52f,
                "关闭", () => { _skillDetailOpen = false; Refresh(); }, true, false, 22f);

            var allPresets = new List<RankedPresetView>(6);
            if (profile.CharacterPresets != null) allPresets.AddRange(profile.CharacterPresets);
            if (profile.PetPresets != null) allPresets.AddRange(profile.PetPresets);
            RankedPresetView active = FindPreset(allPresets, _inspectedPresetId)
                ?? FindPreset(allPresets, _characterPresetId)
                ?? (allPresets.Count > 0 ? allPresets[0] : null);
            if (active != null) _inspectedPresetId = active.PresetId;

            for (int i = 0; i < allPresets.Count && i < 6; ++i)
            {
                var p = allPresets[i];
                if (p == null) continue;
                bool sel = active != null && p.PresetId == active.PresetId;
                uint capturedId = p.PresetId;
                float tx = 496f + i * 262f;
                var tabSprite = SeasonEconomyUiArt.LoadBattleArenaAsset(sel ? "guild_tab_selected" : "guild_tab_normal");
                if (tabSprite != null)
                {
                    var btn = QdaoUguiFactory.CreateArtButton("PresetTab_" + i, transform, tx, 260f, 250f, 56f, tabSprite, out var img);
                    img.type = UnityEngine.UI.Image.Type.Simple;
                    img.color = Color.white;
                    btn.onClick.AddListener(() => { _inspectedPresetId = capturedId; Refresh(); });
                    var lbl = CoreLoopUiArt.Text("PresetTabLbl_" + i, btn.transform, 10f, 0f, 230f, 56f,
                        p.Name, 20f, sel ? SeasonEconomyUiArt.GoldText : SeasonEconomyUiArt.BattleArenaInk,
                        true, false, TextAlignmentOptions.Center);
                    lbl.enableAutoSizing = true; lbl.fontSizeMin = 14f; lbl.fontSizeMax = 20f;
                }
                else
                {
                    CoreLoopUiArt.Button("PresetTab_" + i, transform, tx, 260f, 250f, 56f,
                        p.Name, () => { _inspectedPresetId = capturedId; Refresh(); }, true, sel, 20f);
                }
            }

            if (active == null)
            {
                CoreLoopUiArt.Text("SkillSheetEmpty", transform, 520f, 380f, 1520f, 60f,
                    "预设招式资料同步中…", 24f, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.Center);
                return;
            }

            CoreLoopUiArt.Text("InspectedPresetMeta", transform, 496f, 326f, 1568f, 34f,
                $"{active.Name}（{active.RoleTag}） · 固定 4 招配置（仅本娱乐局生效，正式会武使用本人养成）", 22f, CoreLoopUiArt.Gold, true);

            // 接入 ranked-play-v2-ui-20261125 four-skill-detail-card-base.png（760:400 原比例）四技能表板
            var detailBase = SeasonEconomyUiArt.LoadRankedPlayV2Control("four-skill-detail-card-base");
            var skillIds = active.SkillIds;
            int count = skillIds != null ? Mathf.Min(4, skillIds.Count) : 0;
            if (detailBase != null)
            {
                const float cardX = 524f;
                const float cardY = 366f;
                const float cardW = 1512f;
                const float cardH = 526f;
                var board = QdaoUguiFactory.CreateImage("FourSkillDetailCardBase", transform, cardX, cardY, cardW, cardH, detailBase, false);
                board.type = UnityEngine.UI.Image.Type.Simple;
                board.color = Color.white;

                string roleKindLabel = active.RoleKind == 2 ? "灵宠预设" : "人物预设";
                CoreLoopUiArt.Text("DetailBoardHeaderLeft", board.transform, 28f, 12f, 920f, 44f,
                    $"{active.Name} · 四技能明细（{active.RoleTag}）", 25f, SeasonEconomyUiArt.BattleArenaJade, true);
                CoreLoopUiArt.Text("DetailBoardHeaderRight", board.transform, 1020f, 12f, 464f, 44f,
                    $"{roleKindLabel} · 护卫救援与物法易势通用", 20f, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.MidlineRight);

                for (int i = 0; i < count; ++i)
                {
                    float rowY = 76f + i * 110f;
                    DrawSkillDetailRow(board.transform, i, 20f, rowY, cardW - 40f, 104f, skillIds[i]);
                }
            }
            else
            {
                // 缺图兜底：2 x 2 技能卷轴卡
                for (int i = 0; i < count; ++i)
                {
                    int col = i % 2;
                    int row = i / 2;
                    float cx = 496f + col * 794f;
                    float cy = 382f + row * 256f;
                    DrawSkillScrollCard(i, cx, cy, 774f, 236f, skillIds[i]);
                }
            }
        }

        private void DrawSkillDetailRow(UnityEngine.Transform parent, int index, float x, float y, float w, float h, uint skillId)
        {
            var def = Skills.SkillUiRoot.FindPresentation(skillId);
            var row = SkillTableManager.Instance != null ? SkillTableManager.Instance.FindById(skillId) : null;
            ResolveSkillCategory(def, out string catKey, out string catLabel, out Color catColor);

            CoreLoopUiArt.Text("SkillOrdinal_" + index, parent, x + 8f, y + 26f, 44f, 44f,
                $"0{index + 1}", 22f, CoreLoopUiArt.Gold, true, false, TextAlignmentOptions.Center);

            var iconSprite = CharacterPetArt.Skill(skillId, 64)
                ?? (def != null ? QdaoUguiTheme.LoadSprite(def.IconResource) : null)
                ?? SeasonEconomyUiArt.LoadBattleArenaSkillCategoryIcon(catKey);
            if (iconSprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage("SkillRowIcon_" + index, parent, x + 60f, y + 18f, 68f, 68f, iconSprite, false);
                icon.preserveAspect = true;
            }
            else
            {
                var fallback = QdaoUguiFactory.CreateImage("SkillRowPlaque_" + index, parent, x + 60f, y + 18f, 68f, 68f, null, false);
                fallback.color = SeasonEconomyUiArt.BattleArenaJade;
                CoreLoopUiArt.Text("SkillRowGlyph_" + index, fallback.transform, 0f, 0f, 68f, 68f,
                    "灵", 28f, SeasonEconomyUiArt.BattleArenaPaper, true, false, TextAlignmentOptions.Center);
            }

            string skillName = !string.IsNullOrEmpty(def?.name) ? def.name
                : !string.IsNullOrEmpty(row?.Name) ? row.Name
                : $"技能 #{skillId}";
            CoreLoopUiArt.Text("SkillRowName_" + index, parent, x + 144f, y + 10f, 340f, 34f,
                skillName, 24f, SeasonEconomyUiArt.BattleArenaInk, true);

            var tagPlate = QdaoUguiFactory.CreateImage("SkillRowCatPlate_" + index, parent, x + 494f, y + 12f, 112f, 30f, null, false);
            tagPlate.color = catColor;
            CoreLoopUiArt.Text("SkillRowCatLbl_" + index, tagPlate.transform, 0f, 0f, 112f, 30f,
                catLabel, 17f, SeasonEconomyUiArt.GoldText, true, false, TextAlignmentOptions.Center);

            string metaLine = def != null
                ? $"目标：{def.TargetLabel} · 冷却 {(def.cooldownRounds > 0u ? def.cooldownRounds + " 回合" : "无")} · 法力 {def.manaPercent * 100d:0.#}%"
                : $"技能 ID：{skillId}";
            var metaLbl = CoreLoopUiArt.Text("SkillRowMeta_" + index, parent, x + 622f, y + 12f, w - 638f, 30f,
                metaLine, 18f, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.MidlineRight);
            metaLbl.enableAutoSizing = true; metaLbl.fontSizeMin = 14f; metaLbl.fontSizeMax = 18f;

            string desc = !string.IsNullOrEmpty(def?.description) ? def.description : "招式资料暂缺";
            var descLbl = CoreLoopUiArt.Text("SkillRowDesc_" + index, parent, x + 144f, y + 48f, w - 164f, 50f,
                desc, 19f, SeasonEconomyUiArt.BattleArenaInk, false, true);
            descLbl.enableAutoSizing = true; descLbl.fontSizeMin = 14f; descLbl.fontSizeMax = 19f;
        }

        private void DrawSkillScrollCard(int index, float x, float y, float w, float h, uint skillId)
        {
            CoreLoopUiArt.Card(transform, "SkillScroll_" + index, x, y, w, h, false);
            var def = Skills.SkillUiRoot.FindPresentation(skillId);
            var row = SkillTableManager.Instance != null ? SkillTableManager.Instance.FindById(skillId) : null;
            ResolveSkillCategory(def, out string catKey, out string catLabel, out Color catColor);

            var iconSprite = CharacterPetArt.Skill(skillId, 64)
                ?? (def != null ? QdaoUguiTheme.LoadSprite(def.IconResource) : null)
                ?? SeasonEconomyUiArt.LoadBattleArenaSkillCategoryIcon(catKey);
            if (iconSprite != null)
            {
                var icon = QdaoUguiFactory.CreateImage("SkillIcon_" + index, transform, x + 22f, y + 26f, 84f, 84f, iconSprite, false);
                icon.preserveAspect = true;
            }
            else
            {
                var fallback = QdaoUguiFactory.CreateImage("SkillIconPlaque_" + index, transform, x + 22f, y + 26f, 84f, 84f, null, false);
                fallback.color = SeasonEconomyUiArt.BattleArenaJade;
                CoreLoopUiArt.Text("SkillIconGlyph_" + index, fallback.transform, 0f, 0f, 84f, 84f,
                    "灵", 34f, SeasonEconomyUiArt.BattleArenaPaper, true, false, TextAlignmentOptions.Center);
            }

            string skillName = !string.IsNullOrEmpty(def?.name) ? def.name
                : !string.IsNullOrEmpty(row?.Name) ? row.Name
                : $"技能 #{skillId}";
            CoreLoopUiArt.Text("SkillName_" + index, transform, x + 124f, y + 22f, 440f, 40f,
                skillName, 26f, SeasonEconomyUiArt.BattleArenaInk, true);

            // 分类采用文字 + 低饱和色标，不以颜色单独表达
            var tagPlate = QdaoUguiFactory.CreateImage("SkillCatPlate_" + index, transform, x + w - 160f, y + 24f, 136f, 36f, null, false);
            tagPlate.color = catColor;
            CoreLoopUiArt.Text("SkillCatLbl_" + index, tagPlate.transform, 0f, 0f, 136f, 36f,
                catLabel, 19f, SeasonEconomyUiArt.GoldText, true, false, TextAlignmentOptions.Center);

            string metaLine = def != null
                ? $"目标：{def.TargetLabel} · 冷却 {(def.cooldownRounds > 0u ? def.cooldownRounds + " 回合" : "无")} · 法力 {def.manaPercent * 100d:0.#}%"
                : $"技能 ID：{skillId}";
            CoreLoopUiArt.Text("SkillMeta_" + index, transform, x + 124f, y + 66f, w - 150f, 30f,
                metaLine, 19f, CoreLoopUiArt.Muted);

            string desc = !string.IsNullOrEmpty(def?.description) ? def.description : "招式资料暂缺";
            CoreLoopUiArt.Text("SkillDesc_" + index, transform, x + 24f, y + 122f, w - 48f, 96f,
                desc, 20f, SeasonEconomyUiArt.BattleArenaInk, false, true);
        }

        private static void ResolveSkillCategory(Skills.SkillDefinition def, out string key, out string label, out Color color)
        {
            if (def == null)
            {
                key = string.Empty;
                label = "招式";
                color = SeasonEconomyUiArt.BattleArenaDisabled;
                return;
            }
            if (def.IsObstacle)
            {
                key = "control";
                label = "控场";
                color = SeasonEconomyUiArt.SkillControlColor;
                return;
            }
            if (def.IsSupport)
            {
                key = "support";
                label = "辅助";
                color = SeasonEconomyUiArt.SkillSupportColor;
                return;
            }
            if ((def.actionTags & 2u) != 0u)
            {
                key = "magic";
                label = "法术";
                color = SeasonEconomyUiArt.SkillMagicColor;
                return;
            }
            key = "physical";
            label = "物理";
            color = SeasonEconomyUiArt.SkillPhysicalColor;
        }

        private static RankedPresetView FindPreset(IReadOnlyList<RankedPresetView> list, uint id)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; ++i)
                if (list[i] != null && list[i].PresetId == id) return list[i];
            return null;
        }

        private static string FormatSkillList(IList<uint> skillIds)
        {
            if (skillIds == null || skillIds.Count == 0) return "暂无招式配置";
            var names = new List<string>(skillIds.Count);
            var table = SkillTableManager.Instance;
            for (int i = 0; i < skillIds.Count; ++i)
            {
                uint sid = skillIds[i];
                var row = table != null ? table.FindById(sid) : null;
                names.Add(row != null && !string.IsNullOrEmpty(row.Name) ? row.Name : $"技能{sid}");
            }
            return string.Join(" / ", names);
        }

        private void CancelQueue()
        {
            var entry = TeamUiRoot.Instance?.MatchEntry;
            if (entry != null) entry.Cancel();
            else _game?.Battle?.CancelQueue();
            Refresh();
        }
    }
}
