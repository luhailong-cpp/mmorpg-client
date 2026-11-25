using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Season;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>赛季印只读快照与免费切换、服务端幂等追赶入口。</summary>
    public sealed class SeasonSealPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private GameClient _game;
        private uint _selectedSealId;
        private bool _showTrialJournal;
        private int _milestonePage;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show()
        {
            _showTrialJournal = false;
            gameObject.SetActive(true);
            _game?.Season?.RequestState();
            Refresh();
        }

        public void ShowTrialJournal()
        {
            _showTrialJournal = true;
            gameObject.SetActive(true);
            _game?.TrialJournal?.RequestSnapshot();
            Refresh();
        }

        public void Hide() => gameObject.SetActive(false);

        public void Refresh()
        {
            if (_root == null || _game == null) return;
            var season = _game.Season;
            var snapshot = season?.Snapshot;
            CoreLoopUiArt.Clear(transform);
            CoreLoopUiArt.Frame(transform, "赛季印 · 试炼志", _root.CloseCurrent);
            CoreLoopUiArt.Button("SeasonTab", transform, 300, 251, 245, 48,
                "赛季印", Show, true, !_showTrialJournal, 23);
            CoreLoopUiArt.Button("JournalTab", transform, 559, 251, 245, 48,
                "试炼志", () => _root.OpenTrialJournal(), true, _showTrialJournal, 23);
            if (_showTrialJournal) { DrawTrialJournal(); return; }
            if (snapshot == null || !snapshot.Available)
            {
                CoreLoopUiArt.Text("Unavailable", transform, 400, 400, 1710, 130,
                    snapshot?.UnavailableReason ?? "正在同步赛季印…", 35,
                    CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("Retry", transform, 1060, 575, 420, 73,
                    "刷新赛季资料", () => season?.RequestState(true), true, true, 28);
                return;
            }

            var equipped = SeasonModels.FindEquippedSeal(snapshot);
            bool selectedExists = false;
            for (int i = 0; i < snapshot.Seals.Count; ++i)
                if (snapshot.Seals[i].SealId == _selectedSealId) selectedExists = true;
            if (!selectedExists) _selectedSealId = equipped?.SealId ??
                (snapshot.Seals.Count > 0 ? snapshot.Seals[0].SealId : 0u);

            CoreLoopUiArt.Image("SeasonSummary", transform, 285, 312, 2000, 75,
                "hud-strip", new Color(.9f, .93f, .84f, .95f));
            CoreLoopUiArt.Text("SeasonName", transform, 315, 321, 1000, 58,
                snapshot.SeasonName, 37, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text("SeasonProgress", transform, 1325, 321, 930, 58,
                $"赛季印进度 {snapshot.ProgressPoints}/{snapshot.MaxPoints}", 30,
                CoreLoopUiArt.Ink, true, false, TextAlignmentOptions.Right);

            CoreLoopUiArt.Text("Equipped", transform, 310, 387, 1090, 49,
                "当前装配 · " + (equipped?.Name ?? "未装配"), 29, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text("Bonuses", transform, 1420, 387, 820, 49,
                "气血 +" + SeasonModels.FormatBpsPercent(snapshot.CurrentMaxHealthBps)
                    + "   防御 +" + SeasonModels.FormatBpsPercent(snapshot.CurrentDefenseBps),
                25, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.Right);

            CoreLoopUiArt.Text("SealHeading", transform, 310, 448, 600, 43,
                "可选赛季印 · 免费切换", 27, CoreLoopUiArt.Ink, true);
            int maxSealsVisible = Mathf.Min(snapshot.Seals.Count, 6);
            for (int i = 0; i < maxSealsVisible; ++i)
            {
                var seal = snapshot.Seals[i];
                if (seal == null) continue;
                float x = 310 + i * 322;
                float y = 510;
                bool selected = seal.SealId == _selectedSealId;
                CoreLoopUiArt.Card(transform, "Seal" + seal.SealId, x, y, 305, 92, selected);
                CoreLoopUiArt.Text("SealName" + seal.SealId, transform, x + 15, y + 8, 275, 38,
                    seal.Name, 24, CoreLoopUiArt.DeepJade, true);
                CoreLoopUiArt.Text("SealDescription" + seal.SealId, transform, x + 15, y + 48, 275, 34,
                    seal.Description, 18, CoreLoopUiArt.Muted, false, true);
                uint sealId = seal.SealId;
                var hit = QdaoUguiFactory.CreateHitButton("SelectSeal" + sealId, transform, x, y, 305, 92);
                hit.onClick.AddListener(() => { _selectedSealId = sealId; Refresh(); });
            }

            CoreLoopUiArt.Text("SkillHeading", transform, 310, 636, 900, 45,
                "当季免费赛季技能 · 随进度开放", 27, CoreLoopUiArt.Ink, true);
            for (int i = 0; i < snapshot.SeasonSkills.Count && i < 4; ++i)
            {
                var skill = snapshot.SeasonSkills[i];
                if (skill == null) continue;
                float x = 310 + i * 485;
                CoreLoopUiArt.Card(transform, "SkillCard" + i, x, 695, 465, 125);
                CoreLoopUiArt.Text("SkillName" + i, transform, x + 16, 704, 433, 39,
                    skill.Name, 24, CoreLoopUiArt.DeepJade, true);
                CoreLoopUiArt.Text("SkillDescription" + i, transform, x + 16, 745, 433, 64,
                    skill.Description, 19, CoreLoopUiArt.Muted, false, true);
            }

            bool busy = season.ActionPending || season.RequiresReconnect;
            bool canEquip = !busy && snapshot.CanSwitchSeal && _selectedSealId != 0
                && _selectedSealId != snapshot.EquippedSealId;
            CoreLoopUiArt.Button("EquipSeal", transform, 540, 848, 600, 72,
                "免费切换赛季印", () => season.EquipSeal(_selectedSealId), canEquip, true, 28);
            bool canCatchup = !busy && snapshot.CanCatchup;
            CoreLoopUiArt.Button("Catchup", transform, 1370, 848, 600, 72,
                "一键追赶至满进度", () => season.CatchupSeal(), canCatchup, true, 28);
            string currencyName = SeasonModels.CurrencyName(snapshot.CatchupCurrencyType);
            CoreLoopUiArt.Text("CatchupPrice", transform, 1390, 922, 760, 32,
                $"服务端报价 {snapshot.CatchupRemainingTotalPrice} {currencyName} · 当前余额 {snapshot.CurrencyBalance} {currencyName}",
                21, CoreLoopUiArt.Muted);
            if (!string.IsNullOrEmpty(season.Status))
                CoreLoopUiArt.Text("SeasonStatus", transform, 330, 924, 1040, 34,
                    season.Status, 21, season.StatusIsError ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade);
        }

        private void DrawTrialJournal()
        {
            var journal = _game.TrialJournal;
            var snapshot = journal?.Snapshot;
            if (snapshot == null || !snapshot.Available)
            {
                CoreLoopUiArt.Text("JournalUnavailable", transform, 350, 390, 1840, 100,
                    snapshot?.UnavailableReason ?? "正在同步试炼志…", 32,
                    CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("JournalRetry", transform, 1060, 555, 420, 70,
                    "刷新试炼志", () => journal?.RequestSnapshot(true), true, true, 27);
                return;
            }

            CoreLoopUiArt.Text("JournalProgress", transform, 315, 320, 1480, 60,
                $"永久点亮 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals} · 可领里程碑 {journal.ClaimableMilestoneCount}",
                30, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Button("OpenFullJournal", transform, 1825, 322, 420, 56,
                "打开完整三卷试炼志与法宝兑换", _root.OpenTrialJournal, true, false, 21);
            var milestones = snapshot.Milestones;
            int pageCount = Mathf.Max(1, (milestones.Count + 3) / 4);
            _milestonePage = Mathf.Clamp(_milestonePage, 0, pageCount - 1);
            for (int visible = 0; visible < 4; ++visible)
            {
                int index = _milestonePage * 4 + visible;
                if (index >= milestones.Count) break;
                var milestone = milestones[index];
                if (milestone == null) continue;
                float x = 310 + (visible % 2) * 980;
                float y = 405 + (visible / 2) * 224;
                CoreLoopUiArt.Card(transform, "Milestone" + index, x, y, 940, 202);
                CoreLoopUiArt.Text("MilestoneName" + index, transform, x + 22, y + 12, 720, 42,
                    milestone.Title, 27, CoreLoopUiArt.DeepJade, true);
                CoreLoopUiArt.Badge(transform, "MilestoneStatus" + index, x + 718, y + 13, 195,
                    milestone.Claimed ? "已领取" : milestone.Claimable ? "可领取" : "未达成",
                    milestone.Claimable ? CoreLoopUiArt.Jade : CoreLoopUiArt.Muted);
                CoreLoopUiArt.Text("MilestoneDesc" + index, transform, x + 22, y + 59, 880, 53,
                    milestone.Desc, 21, CoreLoopUiArt.Ink, false, true);
                CoreLoopUiArt.Text("MilestoneSeals" + index, transform, x + 22, y + 119, 510, 32,
                    $"战法印 {milestone.CurrentSeals}/{milestone.RequiredSeals} · 绑定砂 {milestone.BoundSandCount} · 拓印券 {milestone.BoundArtifactVoucherCount}",
                    20, CoreLoopUiArt.Muted);
                uint milestoneId = milestone.MilestoneId;
                CoreLoopUiArt.Button("ClaimMilestone" + index, transform, x + 707, y + 139, 208, 52,
                    milestone.Claimed ? "已领取" : "领取里程碑",
                    () => journal.ClaimMilestone(milestoneId),
                    milestone.Claimable && snapshot.CanOperate, true, 23);
            }
            CoreLoopUiArt.Button("PrevMilestonePage", transform, 835, 860, 210, 59,
                "上一页", () => { --_milestonePage; Refresh(); }, _milestonePage > 0, false, 23);
            CoreLoopUiArt.Text("MilestonePage", transform, 1075, 867, 270, 45,
                $"{_milestonePage + 1} / {pageCount}", 25, CoreLoopUiArt.Muted,
                false, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Button("NextMilestonePage", transform, 1370, 860, 210, 59,
                "下一页", () => { ++_milestonePage; Refresh(); }, _milestonePage + 1 < pageCount, false, 23);
            if (!string.IsNullOrEmpty(journal.Status))
                CoreLoopUiArt.Text("JournalStatus", transform, 330, 928, 1840, 34,
                    journal.Status, 21, journal.StatusIsError ? CoreLoopUiArt.Warning : CoreLoopUiArt.Jade);
        }
    }
}
