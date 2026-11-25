using Match;
using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Ranked;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>首季娱乐轮换；免费人物、灵宠预设只在本页出现。</summary>
    public sealed class SeasonRotationPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;
        private GameClient _game;
        private uint _characterPresetId;
        private uint _petPresetId;

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

        public void Hide() => gameObject.SetActive(false);

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

            CoreLoopUiArt.Text("PresetHeading", transform, 295, 615, 1100, 48,
                "免费 3×3 人宠预设 · 仅本娱乐局生效", 30, CoreLoopUiArt.DeepJade, true);
            DrawPresets(profile);
            bool hasPresets = _characterPresetId != 0 && _petPresetId != 0;
            CoreLoopUiArt.Button("EnterRescue", transform, 524, 874, 650, 76,
                "护卫救援 · 前往试炼", () => _core.EnterGuardRescue(_characterPresetId, _petPresetId),
                hasPresets, true, 29);
            CoreLoopUiArt.Button("EnterPolarity", transform, 1370, 874, 650, 76,
                "物法易势 5V5 · 前往匹配", () => _core.EnterPolarity5v5(_characterPresetId, _petPresetId),
                hasPresets, true, 29);
            if (!hasPresets)
                CoreLoopUiArt.Text("PresetPending", transform, 295, 835, 1800, 36,
                    "正在同步免费预设，完成后可选人物与宝宝。", 22, CoreLoopUiArt.Warning);
            else
                CoreLoopUiArt.Text("RotationAdmission", transform, 295, 835, 1800, 36,
                    "当前开放轮换由服务端准入决定；若未开放，请以返回提示为准。", 22, CoreLoopUiArt.Muted);
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

        private void DrawPresets(RankedProfileSnapshot profile)
        {
            if (profile?.CharacterPresets != null)
                for (int i = 0; i < profile.CharacterPresets.Count && i < 3; ++i)
                {
                    var preset = profile.CharacterPresets[i];
                    if (preset == null) continue;
                    float x = 295 + i * 325;
                    DrawPreset("Character" + i, x, 680, preset, preset.PresetId == _characterPresetId,
                        () => { _characterPresetId = preset.PresetId; Refresh(); });
                }
            if (profile?.PetPresets != null)
                for (int i = 0; i < profile.PetPresets.Count && i < 3; ++i)
                {
                    var preset = profile.PetPresets[i];
                    if (preset == null) continue;
                    float x = 1305 + i * 325;
                    DrawPreset("Pet" + i, x, 680, preset, preset.PresetId == _petPresetId,
                        () => { _petPresetId = preset.PresetId; Refresh(); });
                }
            CoreLoopUiArt.Text("CharacterKind", transform, 295, 655, 930, 30,
                "人物 · 三选一", 23, CoreLoopUiArt.Muted, true);
            CoreLoopUiArt.Text("PetKind", transform, 1305, 655, 930, 30,
                "宝宝 · 三选一", 23, CoreLoopUiArt.Muted, true);
        }

        private void DrawPreset(string key, float x, float y, RankedPresetView preset,
            bool selected, System.Action select)
        {
            CoreLoopUiArt.Card(transform, key + "Card", x, y, 300, 142, selected);
            CoreLoopUiArt.Text(key + "Name", transform, x + 15, y + 10, 270, 40,
                preset.Name, 26, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text(key + "Role", transform, x + 15, y + 51, 270, 36,
                preset.RoleTag, 20, CoreLoopUiArt.Muted);
            CoreLoopUiArt.Badge(transform, key + "Badge", x + 16, y + 92, 180,
                selected ? "已选" : "可选", selected ? CoreLoopUiArt.Jade : CoreLoopUiArt.Muted);
            var hit = QdaoUguiFactory.CreateHitButton(key + "Select", transform, x, y, 300, 142);
            hit.onClick.AddListener(() => select());
        }
    }
}
