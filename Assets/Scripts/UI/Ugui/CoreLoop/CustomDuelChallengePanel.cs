using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 道友切磋坊 · 自定义规则约战配置面板：
    /// 支持输入切磋目标角色 ID，勾选公平 3×3 免费模板、物法易势轮换、回合上限（默认/8/12/20）、禁控直伤与全服公开观战。
    /// 零排位分、零材料刷取、零道具消耗。
    /// </summary>
    public sealed class CustomDuelChallengePanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private GameClient _game;
        private TMP_InputField _targetInput;

        private string _targetIdDraft = string.Empty;
        private bool _useFreePreset = true;
        private bool _enablePolarity;
        private uint _roundLimitOverride = 12u; // 0 / 8 / 12 / 20
        private bool _banControlSkills;
        private bool _enableSkillBan;
        private bool _allowPublicSpectate = true;
        private uint _challengerCharPreset = 101u;
        private uint _challengerPetPreset = 203u;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, GameClient game)
        {
            _root = root;
            _game = game;
            Hide();
        }

        public void Show(ulong presetTargetPlayerId = 0ul)
        {
            if (presetTargetPlayerId != 0ul)
                _targetIdDraft = presetTargetPlayerId.ToString();
            gameObject.SetActive(true);
            PreBattleLoadoutQuickBar.EnsureRequested(_game?.Features);
            Refresh();
        }

        public void Hide() => gameObject.SetActive(false);

        public void Refresh()
        {
            if (_root == null || _game == null) return;
            if (_targetInput != null) _targetIdDraft = _targetInput.text;
            CoreLoopUiArt.Clear(transform);
            CoreLoopUiArt.Frame(transform, "道友切磋坊 · 自定义规则约战", _root.CloseCurrent);

            CoreLoopUiArt.Card(transform, "DuelCard", 430f, 248f, 1700f, 676f);
            CoreLoopUiArt.Text("DuelSubtitle", transform, 470f, 266f, 1620f, 38f,
                "零排位分 · 零药耗 · 满血蓝开战，可自由定制公平模板、物法易势、禁控与公开观战",
                22f, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.Center);

            // 目标玩家 ID 输入
            CoreLoopUiArt.Text("TargetLabel", transform, 480f, 330f, 240f, 44f,
                "目标道友 ID：", 24f, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Image("TargetInputBg", transform, 730f, 326f, 460f, 52f, null,
                new Color(0.98f, 0.96f, 0.90f, 0.96f), raycast: false);
            _targetInput = QdaoUguiFactory.CreateInputField("TargetIdInput", transform, 730f, 326f, 460f, 52f,
                "输入在线道友数字角色 ID", 24);
            _targetInput.text = _targetIdDraft;
            _targetInput.onValueChanged.AddListener(v => _targetIdDraft = v ?? string.Empty);

            // 规则开关行
            DrawRuleToggle("FreePreset", 480f, 408f, "公平 3×3 免费模板", _useFreePreset, null,
                () =>
                {
                    _useFreePreset = !_useFreePreset;
                    if (_useFreePreset) _enableSkillBan = false;
                    Refresh();
                });
            DrawRuleToggle("Polarity", 1040f, 408f, "物法易势（每 2 回合交替）", _enablePolarity, "polarity",
                () => { _enablePolarity = !_enablePolarity; Refresh(); });
            DrawRuleToggle("BanCc", 480f, 482f, "禁控对决（纯拼直伤续航）", _banControlSkills, "ban_cc",
                () => { _banControlSkills = !_banControlSkills; Refresh(); });
            DrawRuleToggle("Spectate", 1040f, 482f, "允许全服公开观战", _allowPublicSpectate, "spectate",
                () => { _allowPublicSpectate = !_allowPublicSpectate; Refresh(); });
            DrawRuleToggle("SkillBan", 1600f, 408f,
                _useFreePreset ? "战前 3 禁 1 保（需关闭公平模板）" : "战前 3 禁 1 保（25 秒暗选）",
                !_useFreePreset && _enableSkillBan, null,
                () =>
                {
                    if (_useFreePreset)
                    {
                        _root.ShowToast("请先关闭「公平 3×3 免费模板」后再开启战前 3 禁 1 保");
                        return;
                    }
                    _enableSkillBan = !_enableSkillBan;
                    Refresh();
                });

            // 回合上限白名单：默认 / 8 / 12 / 20（配 duel_rule_blitz 沙漏小图标）
            var blitzIcon = WuxingExpansionsUiArt.LoadDuelRuleIcon("blitz");
            if (blitzIcon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, "RoundLimitIcon", 480f, 566f, 40f, 40f, blitzIcon, Color.clear);
            CoreLoopUiArt.Text("RoundLimitLbl", transform, blitzIcon != null ? 528f : 480f, 566f, 176f, 42f,
                "回合上限：", 23f, CoreLoopUiArt.DeepJade, true);
            uint[] limits = { 0u, 8u, 12u, 20u };
            string[] limitLabels = { "默认", "8 回合速决", "12 回合", "20 回合" };
            for (int i = 0; i < limits.Length; ++i)
            {
                uint cap = limits[i];
                bool selected = _roundLimitOverride == cap;
                CoreLoopUiArt.Button("LimitBtn" + cap, transform, 710f + i * 240f, 560f, 220f, 52f,
                    limitLabels[i], () => { _roundLimitOverride = cap; Refresh(); }, true, selected, 20f);
            }

            // 公平 3×3 时允许发起者选择人物/灵宠预设
            if (_useFreePreset)
            {
                CoreLoopUiArt.Text("CharPresetLbl", transform, 480f, 640f, 220f, 40f,
                    "我方人物模板：", 21f, CoreLoopUiArt.Ink, true);
                uint[] chars = { 101u, 102u, 103u };
                string[] charNames = { "剑修·力", "法宗·灵", "道医·体" };
                for (int i = 0; i < chars.Length; ++i)
                {
                    uint cid = chars[i];
                    CoreLoopUiArt.Button("CharPreset" + cid, transform, 710f + i * 240f, 634f, 220f, 48f,
                        charNames[i], () => { _challengerCharPreset = cid; Refresh(); }, true, _challengerCharPreset == cid, 20f);
                }

                CoreLoopUiArt.Text("PetPresetLbl", transform, 480f, 704f, 220f, 40f,
                    "我方灵宠模板：", 21f, CoreLoopUiArt.Ink, true);
                uint[] pets = { 201u, 202u, 203u };
                string[] petNames = { "猛攻灵宠", "法爆灵宠", "护主血宠" };
                for (int i = 0; i < pets.Length; ++i)
                {
                    uint pid = pets[i];
                    CoreLoopUiArt.Button("PetPreset" + pid, transform, 710f + i * 240f, 698f, 220f, 48f,
                        petNames[i], () => { _challengerPetPreset = pid; Refresh(); }, true, _challengerPetPreset == pid, 20f);
                }
            }
            else
            {
                PreBattleLoadoutQuickBar.RenderForFeatures(
                    transform, "CustomDuelLoadoutQuickBar", 480f, 644f, 1600f, 76f,
                    _game?.Features, _game?.Battle?.Phase ?? BattlePhase.None, _root.ShowToast);
            }

            // 规则预览 + 发起按钮
            var previewRules = BuildRules();
            CoreLoopUiArt.Text("RulesPreview", transform, 480f, 774f, 1600f, 40f,
                "当前约战规则：" + WuxingExpansionModels.FormatCustomDuelRulesSummary(previewRules),
                22f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.Center);

            CoreLoopUiArt.Button("SendCustomDuel", transform, 1020f, 830f, 520f, 68f,
                "发起自定义切磋邀请", SubmitChallenge, true, true, 26f);
        }

        private void DrawRuleToggle(string key, float x, float y, string label, bool enabled, string iconKey, System.Action onClick)
        {
            var icon = string.IsNullOrEmpty(iconKey) ? null : WuxingExpansionsUiArt.LoadDuelRuleIcon(iconKey);
            if (icon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, key + "Icon", x, y + 6f, 40f, 40f, icon, Color.clear);
            float bx = icon != null ? x + 48f : x;
            CoreLoopUiArt.Button(key + "Toggle", transform, bx, y, 490f, 54f,
                (enabled ? "[已开启] " : "[未开启] ") + label, onClick, true, enabled, 21f);
        }

        private global::CustomDuelRules BuildRules() => new global::CustomDuelRules
        {
            UseFreePreset = _useFreePreset,
            EnablePolarityRotation = _enablePolarity,
            RoundLimitOverride = _roundLimitOverride,
            BanControlSkills = _banControlSkills,
            AllowPublicSpectate = _allowPublicSpectate,
            ChallengerCharacterPresetId = _useFreePreset ? _challengerCharPreset : 0u,
            ChallengerPetPresetId = _useFreePreset ? _challengerPetPreset : 0u,
            SkillBanSlotsPerTeam = (!_useFreePreset && _enableSkillBan) ? 3u : 0u,
        };

        private void SubmitChallenge()
        {
            string raw = (_targetInput != null ? _targetInput.text : _targetIdDraft)?.Trim() ?? string.Empty;
            if (!ulong.TryParse(raw, out ulong targetId) || targetId == 0ul)
            {
                _root.ShowToast("请输入有效的在线道友数字角色 ID");
                return;
            }
            if (targetId == _game.PlayerId)
            {
                _root.ShowToast("不能向自己发起切磋邀请");
                return;
            }
            _game.Battle?.ChallengePlayer(targetId, BuildRules());
            _root.ShowToast($"已向道友 {targetId} 发送自定义切磋邀请");
            _root.CloseCurrent();
        }
    }
}
