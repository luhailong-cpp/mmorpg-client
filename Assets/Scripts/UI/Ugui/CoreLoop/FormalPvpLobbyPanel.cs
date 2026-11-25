using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Season;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Team;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>正式会武只展示本人真实养成与服务端开放的五档规模，严禁出现免费 3×3 预设。</summary>
    public sealed class FormalPvpLobbyPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;
        private GameClient _game;
        private TeamClient _observedTeam;
        private ulong _observedTeamSerial;
        private uint _selectedSize;
        private bool _queuePromptDismissed;
        private uint _lastQueuedSeconds;
        private float _nextQueueRefreshAt;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _core = core;
            _game = game;
            _selectedSize = 0;
            Refresh();
            Hide();
        }

        public void Show() { if (IsVisible) return; gameObject.SetActive(true); Refresh(); }
        public void Hide() => gameObject.SetActive(false);

        public void Tick()
        {
            if (!IsVisible || _game?.Battle == null) return;
            var team = TeamUiRoot.Instance?.Client;
            ulong teamSerial = team?.ViewSerial ?? 0ul;
            if (!ReferenceEquals(team, _observedTeam) || teamSerial != _observedTeamSerial)
            {
                _observedTeam = team;
                _observedTeamSerial = teamSerial;
                Refresh();
                return;
            }
            if (Time.unscaledTime < _nextQueueRefreshAt) return;
            _nextQueueRefreshAt = Time.unscaledTime + 1f;
            var status = _game.Battle.LastQueueStatus;
            uint seconds = status?.QueuedSeconds ?? 0u;
            if (seconds != _lastQueuedSeconds) Refresh();
        }

        public void Refresh()
        {
            if (_root == null || _core == null || _game == null) return;
            var scales = _core.BuildFormalPvpScales();
            if (scales == null) return;
            bool selectedValid = false;
            for (int i = 0; i < scales.Count; ++i)
                if (scales[i].TeamSize == _selectedSize && scales[i].CanEnterWithCurrentParty) selectedValid = true;
            if (!selectedValid)
            {
                _selectedSize = 0;
                for (int i = 0; i < scales.Count; ++i)
                    if (scales[i].CanEnterWithCurrentParty) { _selectedSize = scales[i].TeamSize; break; }
            }

            var phase = _game.Battle?.Phase ?? BattlePhase.None;
            if (phase != BattlePhase.Queued) _queuePromptDismissed = false;
            CoreLoopUiArt.Clear(transform);
            CoreLoopUiArt.Frame(transform, "正式人宠会武", _root.OpenHub);
            SeasonEconomyUiArt.DrawSeasonLobbyTabs(transform, 2,
                _root.OpenBossTrials, _root.OpenRotations, _root.OpenFormalPvp);
            CoreLoopUiArt.Image("OwnProgressionBanner", transform, 280, 260, 2000, 96,
                "hud-strip", new Color(.83f, .91f, .84f, .95f));
            CoreLoopUiArt.Text("OwnProgression", transform, 315, 276, 1920, 62,
                "正式排位 · 使用本人真实装备、技能、赛季印与出战宝宝", 35,
                CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);

            DrawOwnProgressionSummary();
            CoreLoopUiArt.Text("ScaleHeading", transform, 290, 492, 1550, 44,
                "选择匹配规模 · 开放态以服务端为准", 28, CoreLoopUiArt.Ink, true);
            for (int i = 0; i < scales.Count; ++i)
            {
                var slot = scales[i];
                if (slot == null) continue;
                float x = 280 + i * 405;
                bool selected = slot.TeamSize == _selectedSize;
                CoreLoopUiArt.Card(transform, "ScaleCard" + slot.TeamSize, x, 548, 382, 202, selected);
                CoreLoopUiArt.Text("ScaleName" + slot.TeamSize, transform, x + 18, 560, 346, 58,
                    slot.Label, 38, slot.ServerOpen ? CoreLoopUiArt.DeepJade : CoreLoopUiArt.Muted, true, false, TextAlignmentOptions.Center);
                string pillKey = selected ? "size_pill_active" : (slot.CanEnterWithCurrentParty ? "size_pill_normal" : "size_pill_locked");
                var pillSprite = SeasonEconomyUiArt.LoadControl(pillKey);
                if (pillSprite != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(transform, "ScaleBadgePlate" + slot.TeamSize,
                        x + 27, 622, 328, 52, pillSprite, CoreLoopUiArt.Ivory);
                    var pillText = CoreLoopUiArt.Text("ScaleBadge" + slot.TeamSize, transform, x + 56, 626, 270, 44,
                        slot.BadgeText, 20, selected ? CoreLoopUiArt.Ivory : (slot.ServerOpen ? CoreLoopUiArt.DeepJade : CoreLoopUiArt.Muted),
                        true, false, TextAlignmentOptions.Center);
                    pillText.enableAutoSizing = true;
                    pillText.fontSizeMin = 15;
                    pillText.fontSizeMax = 20;
                }
                else
                {
                    CoreLoopUiArt.Badge(transform, "ScaleBadge" + slot.TeamSize, x + 27, 628, 328,
                        slot.BadgeText, slot.ServerOpen ? CoreLoopUiArt.Jade : CoreLoopUiArt.Muted);
                }
                CoreLoopUiArt.Text("ScaleReason" + slot.TeamSize, transform, x + 24, 682, 334, 58,
                    slot.BlockedReason, 20, CoreLoopUiArt.Warning, false, true,
                    TextAlignmentOptions.Center);
                uint targetSize = slot.TeamSize;
                var hit = QdaoUguiFactory.CreateHitButton("ChooseScale" + targetSize,
                    transform, x + 18, 552, 345, 194);
                hit.interactable = slot.CanEnterWithCurrentParty && phase == BattlePhase.None;
                hit.onClick.AddListener(() => SelectScale(targetSize));
            }

            DrawQueueStatusAndActions(phase);
        }

        private void DrawOwnProgressionSummary()
        {
            string equipSummary = "装备待同步";
            if (_game.Features?.Equipment?.Layout != null)
            {
                var wornSlots = EquipBagModel.Slots(_game.Features.Equipment);
                uint count = 0u;
                ulong totalEnhance = 0ul;
                for (int i = 0; i < wornSlots.Count; ++i)
                {
                    var item = wornSlots[i].Item;
                    if (item == null) continue;
                    ++count;
                    totalEnhance += item.EnhanceLevel;
                }
                equipSummary = count == 0 ? "本人装备 · 暂未穿戴"
                    : $"已穿戴 {count} 件 · 平均强化 +{(float)totalEnhance / count:0.0}";
            }
            string petSummary = "出战宝宝待同步";
            var pets = _game.Pets?.Pets;
            if (pets != null)
            {
                for (int i = 0; i < pets.Pets.Count; ++i)
                    if (pets.Pets[i].PetId == pets.ActivePetId)
                    { petSummary = "出战宝宝 · " + pets.Pets[i].Name; break; }
            }
            var seal = SeasonModels.FindEquippedSeal(_game.Season?.Snapshot);
            string sealSummary = seal != null ? "赛季印 · " + seal.Name : "赛季印待同步";
            DrawSummary("Equipment", 305, equipSummary, _root.OpenEquipment);
            DrawSummary("Pet", 970, petSummary, _root.OpenPets);
            DrawSummary("Seal", 1635, sealSummary, _root.OpenSeasonSeal);
        }

        private void DrawSummary(string key, float x, string value, System.Action open)
        {
            CoreLoopUiArt.Card(transform, key + "Summary", x, 376, 620, 90);
            CoreLoopUiArt.Text(key + "Text", transform, x + 22, 387, 450, 65,
                value, 25, CoreLoopUiArt.Ink, true, true);
            CoreLoopUiArt.Button(key + "Open", transform, x + 475, 395, 125, 52,
                "查看", open, true, false, 22);
        }

        private void DrawQueueStatusAndActions(BattlePhase phase)
        {
            var battle = _game.Battle;
            var queue = battle?.LastQueueStatus;
            _lastQueuedSeconds = queue?.QueuedSeconds ?? 0u;
            bool cancelling = _core.FallbackSwitchState == QueueFallbackSwitchState.Cancelling
                || (battle?.IsCancellingQueue ?? false);
            uint waitPrompt = queue != null && queue.WaitPromptSeconds != 0u
                ? queue.WaitPromptSeconds
                : (_game.Ranked?.Profile?.WaitPromptSeconds ?? RankedFormatting.DefaultWaitPromptSeconds);
            bool inQueue = phase == BattlePhase.Queued;

            // 匹配等待进度条（对齐 03-formal-pvp-lobby.png）
            CoreLoopUiArt.Card(transform, "QueuePanel", 280, 774, 1340, 186);
            string spread = queue != null && queue.CurrentRatingSpread > 0u
                ? $" · 当前分差 ±{queue.CurrentRatingSpread}" : string.Empty;
            string queueHeadline = cancelling
                ? "正在取消匹配，请稍候…"
                : inQueue
                    ? $"正在匹配 · 已等待 {RankedFormatting.FormatElapsedClock(_lastQueuedSeconds)} / {RankedFormatting.FormatElapsedClock(waitPrompt)}{spread}"
                    : "选择已开放的规模后开始匹配；等待满 120 秒可随时改打单人首领试炼（不补机器人）。";
            CoreLoopUiArt.Text("QueueHeading", transform, 310, 790, 1280, 42,
                queueHeadline, 23, cancelling || inQueue ? CoreLoopUiArt.DeepJade : CoreLoopUiArt.Ink, true, true);

            CoreLoopUiArt.Image("QueueTrack", transform, 310, 842, 1280, 18,
                null, new Color(.84f, .82f, .73f, .95f));
            float ratio = inQueue && waitPrompt > 0u ? Mathf.Clamp01((float)_lastQueuedSeconds / waitPrompt) : 0f;
            if (ratio > 0f)
                CoreLoopUiArt.Image("QueueFill", transform, 310, 842, 1280f * ratio, 18,
                    null, CoreLoopUiArt.Jade);

            bool showTimeoutChoices = cancelling || (inQueue && _lastQueuedSeconds >= waitPrompt && !_queuePromptDismissed);
            if (_core.FallbackSwitchState == QueueFallbackSwitchState.ReturnedToMatchedBattle)
                showTimeoutChoices = false;

            if (showTimeoutChoices)
            {
                CoreLoopUiArt.Text("QueueFallbackTip", transform, 310, 872, 620, 72,
                    "真人不足，可继续等待或取消并直接转入单人首领试炼。", 21, CoreLoopUiArt.Warning, false, true);
                CoreLoopUiArt.Button("ContinueWait", transform, 946, 882, 230, 56,
                    "继续等待", () => { _queuePromptDismissed = true; Refresh(); }, !cancelling, false, 22);
                CoreLoopUiArt.Button("SwitchToBoss", transform, 1192, 882, 396, 56,
                    cancelling ? "正在取消匹配…" : "取消并去单人试炼",
                    () => _core.CancelQueueAndSwitchToBossTrial(), !cancelling, true, 22);
            }
            else if (inQueue)
            {
                CoreLoopUiArt.Text("QueueNormalTip", transform, 310, 882, 860, 54,
                    "匹配中可随时取消排队，或点击右侧「取消并去单人试炼」。", 21, CoreLoopUiArt.Muted, false, true);
                CoreLoopUiArt.Button("SwitchToBossEarly", transform, 1192, 882, 396, 56,
                    "取消并去单人试炼", () => _core.CancelQueueAndSwitchToBossTrial(), !cancelling, false, 22);
            }
            else
            {
                CoreLoopUiArt.Text("QueueIdleTip", transform, 310, 882, 1260, 54,
                    "正式会武五档规模由服务端动态开放：主力 1V1/3V3/5V5，2V2/4V4 为特殊玩法；组队无需确认免费预设。",
                    20, CoreLoopUiArt.Muted, false, true);
            }

            // 右下主行动区：空闲时「开始匹配」，排队中切换为可点击的「取消匹配」
            CoreLoopUiArt.Button("Team", transform, 1656, 782, 624, 68,
                "调整队伍（支持单人或整队排位）", _root.OpenTeam, !inQueue && !cancelling, false, 24);
            if (inQueue)
            {
                CoreLoopUiArt.Button("CancelQueue", transform, 1656, 872, 624, 84,
                    cancelling ? "正在取消匹配…" : "取消匹配", CancelQueue, !cancelling, false, 30);
            }
            else
            {
                bool canQueue = _selectedSize != 0 && phase == BattlePhase.None;
                CoreLoopUiArt.Button("Queue", transform, 1656, 872, 624, 84,
                    "开始匹配", StartQueue, canQueue, true, 32, "btn_primary_gold");
            }
        }

        private void SelectScale(uint size)
        {
            var scales = _core.BuildFormalPvpScales();
            for (int i = 0; i < scales.Count; ++i)
            {
                if (scales[i].TeamSize != size) continue;
                if (!scales[i].CanEnterWithCurrentParty)
                { _root.ShowToast(scales[i].BlockedReason); return; }
                _selectedSize = size;
                Refresh();
                return;
            }
        }

        private void StartQueue()
        {
            if (_selectedSize != 0) _core.EnterFormalPvp(_selectedSize);
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
