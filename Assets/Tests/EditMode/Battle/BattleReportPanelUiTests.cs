using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 战报列表面板与近期战果「看回放」按钮的 uGUI 契约测试:
    ///   - BattleReportPanel:空列表提示、100 条按服务端顺序排列、has_replay=false 置灰「无回放」;
    ///   - BattleResultPanel:近期 PVP 战果显示「看回放」并回传 battle_id,PVE/作废/现场结算隐藏该按钮。
    /// </summary>
    public sealed class BattleReportPanelUiTests
    {
        private static Match.BattleReportSummary MakeSummary(ulong battleId, bool hasReplay)
        {
            var summary = new Match.BattleReportSummary
            {
                BattleId = battleId,
                Mode = Match.MatchMode._1V1,
                FinishedAtMs = 1700000000000UL,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                TotalRounds = 7,
                HasReplay = hasReplay,
            };
            summary.Members.Add(new Match.BattleReportMember { PlayerId = 1001, TeamIndex = 0, Name = "甲", Level = 50, ClassId = 1 });
            summary.Members.Add(new Match.BattleReportMember { PlayerId = 2002, TeamIndex = 1, Name = "乙", Level = 52 });
            return summary;
        }

        [Test]
        public void ReportPanel_RendersServerOrderAndDisablesButtonWhenHasReplayIsFalse()
        {
            var fixture = new GameObject("ReportPanelTest", typeof(RectTransform), typeof(Canvas));
            fixture.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            try
            {
                var panel = new BattleReportPanel(null, fixture.transform);
                var root = fixture.transform.Find("BattleReportPanel");
                var content = root.Find("Viewport/Content");
                var empty = root.Find("Empty");

                // 空列表
                panel.ApplyList(new List<Match.BattleReportSummary>(), 1001);
                Assert.IsTrue(empty.gameObject.activeSelf);
                Assert.AreEqual(0, content.childCount);

                // 保持服务端顺序(battle_id 非单调)且区分 has_replay
                var list = new List<Match.BattleReportSummary>
                {
                    MakeSummary(500, hasReplay: false),
                    MakeSummary(12, hasReplay: true),
                    MakeSummary(9999, hasReplay: true),
                };
                panel.ApplyList(list, 1001);
                Assert.IsFalse(empty.gameObject.activeSelf);
                Assert.AreEqual(3, content.childCount);
                Assert.AreEqual("Report_500", content.GetChild(0).name);
                Assert.AreEqual("Report_12", content.GetChild(1).name);
                Assert.AreEqual("Report_9999", content.GetChild(2).name);

                var noReplayBtn = content.GetChild(0).Find("WatchReplayPlate").GetComponent<Button>();
                var noReplayLabel = content.GetChild(0).Find("WatchReplayPlate").GetComponentInChildren<TMP_Text>();
                Assert.IsFalse(noReplayBtn.interactable);
                Assert.AreEqual("无回放", noReplayLabel.text);

                var replayBtn = content.GetChild(1).Find("WatchReplayPlate").GetComponent<Button>();
                var replayLabel = content.GetChild(1).Find("WatchReplayPlate").GetComponentInChildren<TMP_Text>();
                Assert.IsTrue(replayBtn.interactable);
                Assert.AreEqual("看回放", replayLabel.text);

                StringAssert.Contains("甲(破军 Lv.50)", BattleReportPanel.FormatMembers(list[0]));
                StringAssert.Contains("乙(Lv.52)", BattleReportPanel.FormatMembers(list[0]));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        [Test]
        public void ResultPanel_ShowsWatchReplayOnlyForNonVoidRecoveredPvp()
        {
            var fixture = new GameObject("ResultPanelReplayButtonTest", typeof(RectTransform), typeof(Canvas));
            fixture.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            BattleResultPanel pvpPanel = null, pvePanel = null, voidPanel = null, livePanel = null;
            try
            {
                ulong requestedReplay = 0;

                var pvpParent = new GameObject("Pvp", typeof(RectTransform)).transform;
                pvpParent.SetParent(fixture.transform, false);
                pvpPanel = new BattleResultPanel(pvpParent, null, id => requestedReplay = id);
                pvpPanel.ShowRecovered(new PlayerBattleResult
                {
                    BattleId = 7788,
                    MatchMode = (uint)Match.MatchMode._1V1,
                    Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin,
                    TeamIndex = 0,
                });
                var pvpWatch = pvpParent.Find("BattleResultPanel/WatchReplayPlate");
                Assert.NotNull(pvpWatch);
                Assert.IsTrue(pvpWatch.gameObject.activeSelf);
                pvpWatch.GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(7788UL, requestedReplay);

                var pveParent = new GameObject("Pve", typeof(RectTransform)).transform;
                pveParent.SetParent(fixture.transform, false);
                pvePanel = new BattleResultPanel(pveParent, null, _ => { });
                pvePanel.ShowRecovered(new PlayerBattleResult
                {
                    BattleId = 7789,
                    MatchMode = (uint)Match.MatchMode.PveSolo,
                    Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin,
                });
                Assert.IsFalse(pveParent.Find("BattleResultPanel/WatchReplayPlate").gameObject.activeSelf);

                var voidParent = new GameObject("Void", typeof(RectTransform)).transform;
                voidParent.SetParent(fixture.transform, false);
                voidPanel = new BattleResultPanel(voidParent, null, _ => { });
                voidPanel.ShowRecovered(new PlayerBattleResult
                {
                    BattleId = 7790,
                    MatchMode = (uint)Match.MatchMode._1V1,
                    Outcome = (uint)eBattleOutcome.BattleOutcomeVoid,
                });
                Assert.IsFalse(voidParent.Find("BattleResultPanel/WatchReplayPlate").gameObject.activeSelf);

                var liveParent = new GameObject("Live", typeof(RectTransform)).transform;
                liveParent.SetParent(fixture.transform, false);
                livePanel = new BattleResultPanel(liveParent, null, _ => { });
                livePanel.Show(new BattleEndS2C { BattleId = 7791, Settlement = new BattleSettlementData() }, 0);
                Assert.IsFalse(liveParent.Find("BattleResultPanel/WatchReplayPlate").gameObject.activeSelf);
            }
            finally
            {
                pvpPanel?.Hide();
                pvePanel?.Hide();
                voidPanel?.Hide();
                livePanel?.Hide();
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }
    }
}
