using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Team;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>由四组权威卡片渲染三只首领与普通、困难入口。</summary>
    public sealed class BossTrialPrepPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;
        private GameClient _game;
        private TeamClient _observedTeam;
        private ulong _observedTeamSerial;
        private bool _observedTeamFresh;
        private int _selectedIndex;
        private bool _selectedHard;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _core = core;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show() { if (IsVisible) return; gameObject.SetActive(true); Refresh(); }
        public void Hide() => gameObject.SetActive(false);

        private void Update()
        {
            if (!IsVisible) return;
            var team = CurrentTeam();
            ulong serial = team?.ViewSerial ?? 0ul;
            bool fresh = team?.HasFreshView == true && team.ServiceAvailable;
            if (object.ReferenceEquals(team, _observedTeam) && serial == _observedTeamSerial &&
                fresh == _observedTeamFresh) return;
            _observedTeam = team;
            _observedTeamSerial = serial;
            _observedTeamFresh = fresh;
            Refresh();
        }

        public void Refresh()
        {
            if (_root == null || _core == null) return;
            var cards = _core.BuildBossTrials();
            if (cards == null) return;
            if (_selectedIndex >= cards.Count) _selectedIndex = 0;
            if (cards.Count > 0 && _selectedHard && !cards[_selectedIndex].HardUnlocked) _selectedHard = false;

            CoreLoopUiArt.Clear(transform);
            CoreLoopUiArt.Frame(transform, "首领试炼 · 备战", _root.OpenHub);
            CoreLoopUiArt.Text("Intro", transform, 290, 275, 1410, 55,
                "先读机制，再选反制。单人可携出战灵宠，组队不必凑满五人。", 28,
                CoreLoopUiArt.Muted, false, true);
            int claimable = _game?.TrialJournal?.ClaimableMilestoneCount ?? 0;
            string journalLabel = claimable > 0 ? $"五行试炼志 · 可领 {claimable} 项" : "五行试炼志";
            CoreLoopUiArt.Button("OpenTrialJournal", transform, 1740, 268, 420, 56,
                journalLabel, _root.OpenTrialJournal, true, claimable > 0, 24);

            for (int i = 0; i < cards.Count; ++i)
            {
                var card = cards[i];
                if (card == null) continue;
                float x = 320 + i * 475;
                bool selected = i == _selectedIndex;
                var cardSprite = SeasonEconomyUiArt.LoadControl(selected ? "card_trial_selected" : "card_trial_normal");
                if (cardSprite != null)
                    SeasonEconomyUiArt.CreateSlicedOrSimple(transform, "BossCard" + i, x, 345, 400, 488, cardSprite, CoreLoopUiArt.Ivory);
                else
                    CoreLoopUiArt.Card(transform, "BossCard" + i, x, 345, 400, 488, selected);
                CoreLoopUiArt.Image("BossPortrait" + i, transform, x + 25, 374, 110, 110,
                    BossIllustrationKey(card.BossTrialKind), new Color(.82f, .9f, .82f, .9f));
                CoreLoopUiArt.Text("BossName" + i, transform, x + 145, 378, 230, 63,
                    card.BossName, 27, CoreLoopUiArt.DeepJade, true, true);
                CoreLoopUiArt.Badge(transform, "GuardTag" + i, x + 145, 451, 215,
                    card.GuardTag, CoreLoopUiArt.Ink);
                CoreLoopUiArt.Text("Mechanic" + i, transform, x + 25, 512, 350, 105,
                    card.CoreMechanicSummary, 21, CoreLoopUiArt.Ink, false, true);
                CoreLoopUiArt.Text("CounterLabel" + i, transform, x + 25, 620, 230, 30,
                    "反制提示", 22, CoreLoopUiArt.Gold, true);
                SeasonEconomyUiArt.BossCardIntentIcons(card.BossTrialKind, out string primaryIntent, out string secondaryIntent);
                var intent1 = QdaoUguiFactory.CreateImage("BossIntentPrimary" + i, transform, x + 282, 612, 42, 42,
                    SeasonEconomyUiArt.LoadIcon(primaryIntent), false);
                intent1.preserveAspect = true;
                intent1.gameObject.SetActive(intent1.sprite != null);
                var intent2 = QdaoUguiFactory.CreateImage("BossIntentSecondary" + i, transform, x + 330, 612, 42, 42,
                    SeasonEconomyUiArt.LoadIcon(secondaryIntent), false);
                intent2.preserveAspect = true;
                intent2.gameObject.SetActive(intent2.sprite != null);
                CoreLoopUiArt.Text("Counter" + i, transform, x + 25, 656, 350, 88,
                    card.CounterHint, 20, CoreLoopUiArt.Muted, false, true);

                int index = i;
                CoreLoopUiArt.Button("Normal" + i, transform, x + 18, 760, 174, 61,
                    "普通  +" + card.NormalRewardUnits,
                    () => Select(index, false), true, selected && !_selectedHard, 21);
                CoreLoopUiArt.Button("Hard" + i, transform, x + 207, 760, 174, 61,
                    card.HardUnlocked ? "困难  +" + card.HardRewardUnits : "困难 +" + card.HardRewardUnits + " · 锁",
                    () => Select(index, true), card.HardUnlocked, selected && _selectedHard, 21);
                if (!card.HardUnlocked)
                {
                    var hint = QdaoUguiFactory.CreateHitButton("HardHint" + i,
                        transform, x + 207, 760, 174, 61);
                    hint.onClick.AddListener(() => _root.ShowToast(card.HardLockHint));
                }
            }

            if (cards.Count == 0) return;
            var selectedCard = cards[_selectedIndex];
            uint selectedDungeon = _selectedHard ? selectedCard.HardDungeonId : selectedCard.NormalDungeonId;
            var team = CurrentTeam();
            bool teamReady = team?.HasFreshView == true && team.ServiceAvailable;
            bool hasTeam = teamReady && team.HasTeam;
            int partySize = hasTeam ? team.Snapshot.Members.Count : 1;
            bool canLead = teamReady && (!hasTeam || team.IsLeader);
            string enterLabel = !teamReady ? "队伍状态同步中"
                : hasTeam && !team.IsLeader ? "请队长发起试炼"
                : hasTeam ? $"按当前队伍 {partySize} 人立即开战"
                : "单人带出战灵宠立即开战";
            string teamHint = !teamReady ? "当前队伍信息待同步"
                : hasTeam ? $"当前队伍 {partySize}/{team.Snapshot.Capacity} 人 · 无需凑满 5 人 · {(team.IsLeader ? "你是队长" : "请队长发起")}"
                : "当前单人 · 可直接入场";
            var tradableIcon = QdaoUguiFactory.CreateImage("RewardTradableBadge", transform, 295, 844, 40, 40,
                SeasonEconomyUiArt.LoadIcon("badge_tradable_tag"), false);
            tradableIcon.preserveAspect = true;
            tradableIcon.gameObject.SetActive(tradableIcon.sprite != null);
            CoreLoopUiArt.Text("RewardRule", transform, tradableIcon.sprite != null ? 344 : 295, 842, 1265, 45,
                _selectedHard ? "困难胜局：基础 10 + 困难绑定加量 5（含至多 1 份可交易）；全队成员均须已解锁困难。"
                    : "普通胜局：基础材料奖励，含至多 1 份可交易。", 23, CoreLoopUiArt.Muted);
            CoreLoopUiArt.Button("EnterTrial", transform, 1740, 834, 420, 80,
                enterLabel, () => _core.EnterBossTrial(selectedDungeon), canLead, true, 30);
            CoreLoopUiArt.Text("PartyStatus", transform, 1740, 919, 420, 35,
                teamHint, 20, teamReady ? CoreLoopUiArt.Muted : CoreLoopUiArt.Warning,
                false, false, TextAlignmentOptions.Center);
        }

        private TeamClient CurrentTeam()
        {
            var team = TeamUiRoot.Instance?.Client;
            return _game != null && team != null && team.PlayerId == _game.PlayerId ? team : null;
        }

        private void Select(int index, bool hard)
        {
            var cards = _core.BuildBossTrials();
            if (cards == null || index < 0 || index >= cards.Count) return;
            if (hard && !cards[index].HardUnlocked)
            {
                _root.ShowToast(cards[index].HardLockHint);
                return;
            }
            _selectedIndex = index;
            _selectedHard = hard;
            Refresh();
        }

        private static string BossIllustrationKey(uint bossTrialKind)
        {
            // 本映射只选择交接中已确认的三张首领插画；机制、难度与入口 ID 均由 card 决定。
            switch (bossTrialKind)
            {
                case 1u:
                case 2u: return "boss-break";
                case 3u: return "boss-barrier";
                case 4u: return "boss-venom";
                default: return "badge";
            }
        }
    }
}
