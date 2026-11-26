using System;
using System.Collections.Generic;
using MmorpgClient.Game.PetFlip;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 灵宠翻牌小游戏纯逻辑与生命周期单元测试。
    /// 覆盖固定验收盘、随机洗牌、重复点击、迟到回调、面板重建、暂停续盘、临时断线、
    /// 登出/换角色/切服/销毁清盘、成绩比较、损坏记录兜底与保存失败降级。
    /// </summary>
    public sealed class PetFlipClientTests
    {
        private const ulong DefaultPlayerId = 10001;
        private const uint DefaultZoneId = 1;

        /// <summary>固定验收盘：位置 0..11 依次为 A, B, C, D, E, F, A, B, C, D, E, F（仅测试使用）。</summary>
        private static SpiritPetCardPattern[] CreateAcceptanceDeck() => new[]
        {
            SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
            SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F,
            SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
            SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F
        };

        private static PetFlipClient CreateFixedClient(
            IPetFlipBestRecordStore store = null,
            bool logIn = true)
        {
            var client = PetFlipClient.CreateForTesting(store ?? new InMemoryPetFlipBestRecordStore(), CreateAcceptanceDeck);
            if (logIn)
                client.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);
            return client;
        }

        private static PetFlipActionResult CompleteAcceptanceDeckInSixFlips(PetFlipClient client, ulong? viewToken = null)
        {
            int[] sequence = { 0, 6, 1, 7, 2, 8, 3, 9, 4, 10, 5, 11 };
            PetFlipActionResult last = null;
            ulong roundId = client.GetSnapshot().RoundId;
            for (int i = 0; i < sequence.Length; i++)
                last = client.FlipCard(sequence[i], expectedRoundId: roundId, viewToken: viewToken);
            return last;
        }

        private static PetFlipActionResult CompleteAcceptanceDeckWithExtraMismatches(
            PetFlipClient client,
            int extraMismatches)
        {
            ulong roundId = client.GetSnapshot().RoundId;
            for (int m = 0; m < extraMismatches; m++)
            {
                Assert.That(client.FlipCard(0, roundId).Accepted, Is.True);
                Assert.That(client.FlipCard(1, roundId).Outcome, Is.EqualTo(PetFlipActionOutcome.PairMismatched));
            }

            return CompleteAcceptanceDeckInSixFlips(client);
        }

        // ── 固定验收样例 ──────────────────────────────────────────────────────

        [Test]
        public void Acceptance_MismatchThenCoverThenMatch_MatchesExactSpecification()
        {
            using var client = CreateFixedClient();
            PetFlipActionResult start = client.StartOrResume();
            Assert.That(start.Accepted, Is.True);
            Assert.That(start.Outcome, Is.EqualTo(PetFlipActionOutcome.StartedNewRound));
            Assert.That(start.Snapshot.Cards.Count, Is.EqualTo(12));
            for (int i = 0; i < 12; i++)
            {
                Assert.That(start.Snapshot.Cards[i].State, Is.EqualTo(PetFlipCardState.FaceDown));
                Assert.That(start.Snapshot.Cards[i].VisiblePattern, Is.Null);
                Assert.That(start.Snapshot.Cards[i].IsClickable, Is.True);
            }

            // 依次点击 0、1：失配，次数 1，两张明牌。
            PetFlipActionResult flip0 = client.FlipCard(0);
            Assert.That(flip0.Outcome, Is.EqualTo(PetFlipActionOutcome.FirstCardFlipped));
            Assert.That(flip0.Snapshot.FlipCount, Is.EqualTo(0));
            Assert.That(flip0.Snapshot.Cards[0].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(flip0.Snapshot.Cards[0].VisiblePattern, Is.EqualTo(SpiritPetCardPattern.A));

            PetFlipActionResult flip1 = client.FlipCard(1);
            Assert.That(flip1.Outcome, Is.EqualTo(PetFlipActionOutcome.PairMismatched));
            Assert.That(flip1.Snapshot.FlipCount, Is.EqualTo(1));
            Assert.That(flip1.Snapshot.HandState, Is.EqualTo(PetFlipHandState.MismatchShowing));
            Assert.That(flip1.Snapshot.Cards[0].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(flip1.Snapshot.Cards[1].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(flip1.Snapshot.Cards[0].VisiblePattern, Is.EqualTo(SpiritPetCardPattern.A));
            Assert.That(flip1.Snapshot.Cards[1].VisiblePattern, Is.EqualTo(SpiritPetCardPattern.B));

            // 接着点击 6：原 0 和 1 盖回，仅 6 亮起，次数仍为 1。
            PetFlipActionResult flip6 = client.FlipCard(6);
            Assert.That(flip6.Outcome, Is.EqualTo(PetFlipActionOutcome.MismatchClearedAndFirstCardFlipped));
            Assert.That(flip6.CoveredFirstSlotIndex, Is.EqualTo(0));
            Assert.That(flip6.CoveredSecondSlotIndex, Is.EqualTo(1));
            Assert.That(flip6.FlippedSlotIndex, Is.EqualTo(6));
            Assert.That(flip6.Snapshot.FlipCount, Is.EqualTo(1));
            Assert.That(flip6.Snapshot.HandState, Is.EqualTo(PetFlipHandState.WaitingSecondCard));
            Assert.That(flip6.Snapshot.Cards[0].State, Is.EqualTo(PetFlipCardState.FaceDown));
            Assert.That(flip6.Snapshot.Cards[0].VisiblePattern, Is.Null);
            Assert.That(flip6.Snapshot.Cards[1].State, Is.EqualTo(PetFlipCardState.FaceDown));
            Assert.That(flip6.Snapshot.Cards[1].VisiblePattern, Is.Null);
            Assert.That(flip6.Snapshot.Cards[6].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(flip6.Snapshot.Cards[6].VisiblePattern, Is.EqualTo(SpiritPetCardPattern.A));

            // 再点击 0：0 和 6 配成一对，次数 2，已配 1 对。
            PetFlipActionResult match0 = client.FlipCard(0);
            Assert.That(match0.Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            Assert.That(match0.MatchedPattern, Is.EqualTo(SpiritPetCardPattern.A));
            Assert.That(match0.Snapshot.FlipCount, Is.EqualTo(2));
            Assert.That(match0.Snapshot.MatchedPairCount, Is.EqualTo(1));
            Assert.That(match0.Snapshot.RemainingPairCount, Is.EqualTo(5));
            Assert.That(match0.Snapshot.HandState, Is.EqualTo(PetFlipHandState.WaitingFirstCard));
            Assert.That(match0.Snapshot.Cards[0].State, Is.EqualTo(PetFlipCardState.Matched));
            Assert.That(match0.Snapshot.Cards[6].State, Is.EqualTo(PetFlipCardState.Matched));
            Assert.That(match0.Snapshot.Cards[0].IsClickable, Is.False);
            Assert.That(match0.Snapshot.Cards[6].IsClickable, Is.False);
        }

        [Test]
        public void Acceptance_PerfectSixPairRun_CompletesExactlyOnce()
        {
            using var client = CreateFixedClient();
            int completedEvents = 0;
            PetFlipCompletionResult lastCompleted = null;
            client.OnRoundCompleted += result =>
            {
                completedEvents++;
                lastCompleted = result;
            };

            client.StartOrResume();
            PetFlipActionResult finalStep = CompleteAcceptanceDeckInSixFlips(client);

            Assert.That(finalStep.Outcome, Is.EqualTo(PetFlipActionOutcome.RoundCompleted));
            Assert.That(finalStep.Snapshot.Phase, Is.EqualTo(PetFlipRoundPhase.Completed));
            Assert.That(finalStep.Snapshot.FlipCount, Is.EqualTo(6));
            Assert.That(finalStep.Snapshot.MatchedPairCount, Is.EqualTo(6));
            Assert.That(finalStep.Snapshot.RemainingPairCount, Is.EqualTo(0));
            Assert.That(finalStep.Snapshot.CanStartNewRound, Is.True);
            Assert.That(completedEvents, Is.EqualTo(1));
            Assert.That(lastCompleted, Is.Not.Null);
            Assert.That(lastCompleted.FlipCount, Is.EqualTo(6));
            Assert.That(lastCompleted.MatchedPairCount, Is.EqualTo(6));
            Assert.That(lastCompleted.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));

            // 完成后继续点牌，不增次、不二次完成。
            PetFlipActionResult extraClick = client.FlipCard(0);
            Assert.That(extraClick.Accepted, Is.False);
            Assert.That(extraClick.RejectReason, Is.EqualTo(PetFlipRejectReason.RoundAlreadyCompleted));
            Assert.That(extraClick.Snapshot.FlipCount, Is.EqualTo(6));
            Assert.That(completedEvents, Is.EqualTo(1));
        }

        [Test]
        public void Mismatch_ClickingOneOfMismatchedCards_CoversBothThenOpensClickedAsFirst()
        {
            using var client = CreateFixedClient();
            client.StartOrResume();
            client.FlipCard(0);
            client.FlipCard(1);

            // 点击刚才失配的两张中的位置 0：先盖回 0 和 1，再翻开 0 作为新一手第一张。
            PetFlipActionResult repick0 = client.FlipCard(0);
            Assert.That(repick0.Outcome, Is.EqualTo(PetFlipActionOutcome.MismatchClearedAndFirstCardFlipped));
            Assert.That(repick0.CoveredFirstSlotIndex, Is.EqualTo(0));
            Assert.That(repick0.CoveredSecondSlotIndex, Is.EqualTo(1));
            Assert.That(repick0.FlippedSlotIndex, Is.EqualTo(0));
            Assert.That(repick0.Snapshot.FlipCount, Is.EqualTo(1));
            Assert.That(repick0.Snapshot.HandState, Is.EqualTo(PetFlipHandState.WaitingSecondCard));
            Assert.That(repick0.Snapshot.Cards[0].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(repick0.Snapshot.Cards[1].State, Is.EqualTo(PetFlipCardState.FaceDown));

            PetFlipActionResult match6 = client.FlipCard(6);
            Assert.That(match6.Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            Assert.That(match6.Snapshot.FlipCount, Is.EqualTo(2));
            Assert.That(match6.Snapshot.MatchedPairCount, Is.EqualTo(1));
        }

        // ── 重复点击、非法位置与禁止盘中洗牌 ──────────────────────────────────

        [Test]
        public void DuplicateAndInvalidClicks_DoNotChangeBoardOrFlipCount()
        {
            using var client = CreateFixedClient();
            PetFlipActionResult firstStart = client.StartOrResume();
            ulong roundId = firstStart.Snapshot.RoundId;

            // 重复调用 StartOrResume 不开出第二盘。
            PetFlipActionResult secondStart = client.StartOrResume();
            Assert.That(secondStart.Outcome, Is.EqualTo(PetFlipActionOutcome.ResumedRound));
            Assert.That(secondStart.Snapshot.RoundId, Is.EqualTo(roundId));

            // 盘中调用 StartNewRound 被拒绝（无重新洗牌按钮）。
            PetFlipActionResult midRestart = client.StartNewRound();
            Assert.That(midRestart.Accepted, Is.False);
            Assert.That(midRestart.RejectReason, Is.EqualTo(PetFlipRejectReason.RoundNotCompleted));

            // 非法位置不改变次数和盘面。
            Assert.That(client.FlipCard(-1).RejectReason, Is.EqualTo(PetFlipRejectReason.InvalidSlotIndex));
            Assert.That(client.FlipCard(12).RejectReason, Is.EqualTo(PetFlipRejectReason.InvalidSlotIndex));

            // 点击同一手第一张两次不改变次数和盘面。
            Assert.That(client.FlipCard(0).Outcome, Is.EqualTo(PetFlipActionOutcome.FirstCardFlipped));
            PetFlipActionResult repeatFirst = client.FlipCard(0);
            Assert.That(repeatFirst.Accepted, Is.False);
            Assert.That(repeatFirst.RejectReason, Is.EqualTo(PetFlipRejectReason.AlreadySelectedInHand));
            Assert.That(repeatFirst.Snapshot.FlipCount, Is.EqualTo(0));

            // 配对 0 和 6 后重复点击已配对牌不改变次数和盘面。
            Assert.That(client.FlipCard(6).Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            PetFlipActionResult repeatMatched = client.FlipCard(0);
            Assert.That(repeatMatched.Accepted, Is.False);
            Assert.That(repeatMatched.RejectReason, Is.EqualTo(PetFlipRejectReason.CardAlreadyMatched));
            Assert.That(repeatMatched.Snapshot.FlipCount, Is.EqualTo(1));

            // 失配停留期间误点已配对牌或非法位置，不应把失配两张盖回。
            client.FlipCard(1);
            client.FlipCard(2);
            Assert.That(client.FlipCard(0).RejectReason, Is.EqualTo(PetFlipRejectReason.CardAlreadyMatched));
            Assert.That(client.FlipCard(99).RejectReason, Is.EqualTo(PetFlipRejectReason.InvalidSlotIndex));
            PetFlipSnapshot stillMismatch = client.GetSnapshot();
            Assert.That(stillMismatch.HandState, Is.EqualTo(PetFlipHandState.MismatchShowing));
            Assert.That(stillMismatch.Cards[1].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(stillMismatch.Cards[2].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(stillMismatch.FlipCount, Is.EqualTo(2));
        }

        // ── 迟到回调与面板重建 ────────────────────────────────────────────────

        [Test]
        public void LateCallbacksAndPanelRebuild_PreventStaleActionsAndDoubleCompletion()
        {
            using var client = CreateFixedClient();
            int completedEvents = 0;
            client.OnRoundCompleted += _ => completedEvents++;

            var view1Snapshots = new List<PetFlipSnapshot>();
            var view2Snapshots = new List<PetFlipSnapshot>();

            ulong token1 = client.AttachView(s => view1Snapshots.Add(s));
            ulong round1 = client.StartOrResume(token1).Snapshot.RoundId;
            client.FlipCard(0, round1, token1);

            // 乱序面板重建：新面板先挂载，旧面板后销毁。
            ulong token2 = client.AttachView(s => view2Snapshots.Add(s));
            Assert.That(client.IsActiveView(token1), Is.False);
            Assert.That(client.IsActiveView(token2), Is.True);

            // 旧面板迟到的 DetachView 不能关闭或暂停新面板。
            client.DetachView(token1, pausePanel: true);
            Assert.That(client.GetSnapshot().Phase, Is.EqualTo(PetFlipRoundPhase.Playing));

            // 旧面板迟到的点击回调被拒绝，不触发计次。
            PetFlipActionResult staleViewClick = client.FlipCard(1, round1, token1);
            Assert.That(staleViewClick.Accepted, Is.False);
            Assert.That(staleViewClick.RejectReason, Is.EqualTo(PetFlipRejectReason.StaleView));
            Assert.That(client.GetSnapshot().FlipCount, Is.EqualTo(0));

            // 用新面板完成第 1 局（0 已翻开，先配 6 再配其余 5 对）。
            Assert.That(client.FlipCard(6, round1, token2).Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            int[] rest = { 1, 7, 2, 8, 3, 9, 4, 10, 5, 11 };
            for (int i = 0; i < rest.Length; i++)
                client.FlipCard(rest[i], round1, token2);

            Assert.That(completedEvents, Is.EqualTo(1));
            Assert.That(client.GetSnapshot().CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));

            // 终局态面板销毁并重建后重新打开：保留原终局结果（仍是首次纪录，不被误算成追平），不自动开新盘。
            client.DetachView(token2, pausePanel: true);
            ulong token3 = client.AttachView(_ => { });
            PetFlipActionResult resumeCompleted = client.StartOrResume(token3);
            Assert.That(resumeCompleted.Outcome, Is.EqualTo(PetFlipActionOutcome.ResumedRound));
            Assert.That(resumeCompleted.Snapshot.RoundId, Is.EqualTo(round1));
            Assert.That(resumeCompleted.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));
            Assert.That(completedEvents, Is.EqualTo(1));

            // 点击“再玩一局”进入第 2 局；连点第二次被拒，且第 1 局的迟到点击回调被 StaleRound 拒绝。
            PetFlipActionResult round2Start = client.StartNewRound(round1, token3);
            Assert.That(round2Start.Outcome, Is.EqualTo(PetFlipActionOutcome.StartedNewRound));
            Assert.That(round2Start.Snapshot.RoundId, Is.GreaterThan(round1));

            PetFlipActionResult doubleNewRound = client.StartNewRound(round2Start.Snapshot.RoundId, token3);
            Assert.That(doubleNewRound.Accepted, Is.False);
            Assert.That(doubleNewRound.RejectReason, Is.EqualTo(PetFlipRejectReason.RoundNotCompleted));

            PetFlipActionResult staleRoundClick = client.FlipCard(0, expectedRoundId: round1, viewToken: token3);
            Assert.That(staleRoundClick.Accepted, Is.False);
            Assert.That(staleRoundClick.RejectReason, Is.EqualTo(PetFlipRejectReason.StaleRound));
            Assert.That(client.GetSnapshot().Cards[0].State, Is.EqualTo(PetFlipCardState.FaceDown));
        }

        // ── 暂停、高优先中断与断线续盘 ────────────────────────────────────────

        [Test]
        public void PauseAndPriorityInterrupts_PreserveBoardAndCollapseUntilCleared()
        {
            using var client = CreateFixedClient();
            client.StartOrResume();
            client.FlipCard(0);
            client.FlipCard(1); // 失配停留，次数 1

            // 暂关面板：暂停收起但保留盘面，操作被拒。
            client.Pause(PetFlipInterruptFlags.PanelClosed);
            PetFlipSnapshot closedSnap = client.GetSnapshot();
            Assert.That(closedSnap.Phase, Is.EqualTo(PetFlipRoundPhase.Paused));
            Assert.That(closedSnap.ShouldCollapsePanel, Is.True);
            Assert.That(closedSnap.CanInteract, Is.False);
            Assert.That(client.FlipCard(6).RejectReason, Is.EqualTo(PetFlipRejectReason.GamePaused));

            // 重新打开面板恢复续盘。
            PetFlipActionResult reopened = client.StartOrResume();
            Assert.That(reopened.Outcome, Is.EqualTo(PetFlipActionOutcome.ResumedRound));
            Assert.That(reopened.Snapshot.Phase, Is.EqualTo(PetFlipRoundPhase.Playing));
            Assert.That(reopened.Snapshot.HandState, Is.EqualTo(PetFlipHandState.MismatchShowing));
            Assert.That(reopened.Snapshot.FlipCount, Is.EqualTo(1));

            // 切后台 + 战斗叠加：仅恢复前台时仍被战斗阻塞收起，战斗结束后才可交互。
            client.NotifyApplicationPause(true);
            client.SetInterrupt(PetFlipInterruptFlags.Battle, true);
            Assert.That(client.GetSnapshot().ShouldCollapsePanel, Is.True);
            Assert.That(client.StartOrResume().RejectReason, Is.EqualTo(PetFlipRejectReason.BlockedByInterrupt));

            client.NotifyApplicationPause(false);
            Assert.That(client.GetSnapshot().Phase, Is.EqualTo(PetFlipRoundPhase.Paused));
            Assert.That(client.GetSnapshot().ShouldCollapsePanel, Is.True);
            Assert.That(client.FlipCard(6).RejectReason, Is.EqualTo(PetFlipRejectReason.BlockedByInterrupt));

            client.SetInterrupt(PetFlipInterruptFlags.Battle, false);
            Assert.That(client.GetSnapshot().Phase, Is.EqualTo(PetFlipRoundPhase.Playing));
            Assert.That(client.GetSnapshot().ShouldCollapsePanel, Is.False);

            // 观战、加载、强制弹窗均优先暂停收起，且宿主 SyncHostState 不覆盖 UI 设置的 ForcedModal。
            foreach (var priority in new[]
            {
                PetFlipInterruptFlags.Spectate,
                PetFlipInterruptFlags.Loading,
                PetFlipInterruptFlags.ForcedModal
            })
            {
                client.SetInterrupt(priority, true);
                client.SyncHostState(DefaultPlayerId, DefaultZoneId, true, false, false, false, false);
                Assert.That(client.GetSnapshot().ShouldCollapsePanel, Is.True, $"中断 {priority} 应触发收起");
                Assert.That(client.FlipCard(6).RejectReason, Is.EqualTo(PetFlipRejectReason.BlockedByInterrupt));
                client.SetInterrupt(priority, false);
            }

            // 全部中断清除后，盘面完整保留并可继续。
            Assert.That(client.FlipCard(6).Outcome, Is.EqualTo(PetFlipActionOutcome.MismatchClearedAndFirstCardFlipped));
            Assert.That(client.FlipCard(0).Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            Assert.That(client.GetSnapshot().FlipCount, Is.EqualTo(2));
        }

        [Test]
        public void TemporaryDisconnect_StopsInteractionAndResumesWhenSameRoleReconnects()
        {
            using var client = CreateFixedClient();
            ulong roundId = client.StartOrResume().Snapshot.RoundId;
            client.FlipCard(0);
            client.FlipCard(6); // 已配 1 对，次数 1

            // 临时断线重连中：停止操作但保留盘面。
            client.SyncHostState(0, 0, isInGameConnected: false, isReconnecting: true,
                isInBattle: false, isSpectating: false, isLoading: false);
            PetFlipSnapshot disconnected = client.GetSnapshot();
            Assert.That(disconnected.IsDisconnected, Is.True);
            Assert.That(disconnected.Phase, Is.EqualTo(PetFlipRoundPhase.Paused));
            Assert.That(disconnected.HasBoard, Is.True);
            Assert.That(disconnected.CanInteract, Is.False);
            Assert.That(client.FlipCard(1).RejectReason, Is.EqualTo(PetFlipRejectReason.BlockedByInterrupt));
            Assert.That(client.StartOrResume().RejectReason, Is.EqualTo(PetFlipRejectReason.BlockedByInterrupt));

            // 同角色同区恢复：继续原盘。
            client.SyncHostState(DefaultPlayerId, DefaultZoneId, isInGameConnected: true, isReconnecting: false,
                isInBattle: false, isSpectating: false, isLoading: false);
            PetFlipSnapshot restored = client.GetSnapshot();
            Assert.That(restored.IsDisconnected, Is.False);
            Assert.That(restored.Phase, Is.EqualTo(PetFlipRoundPhase.Playing));
            Assert.That(restored.RoundId, Is.EqualTo(roundId));
            Assert.That(restored.MatchedPairCount, Is.EqualTo(1));
            Assert.That(restored.FlipCount, Is.EqualTo(1));
            Assert.That(client.FlipCard(1).Outcome, Is.EqualTo(PetFlipActionOutcome.FirstCardFlipped));
        }

        // ── 登出、换角色、切服、销毁清盘与未登录拦截 ──────────────────────────

        [Test]
        public void LogoutRoleSwitchZoneSwitchAndDispose_DiscardUnfinishedBoardButKeepLocalBest()
        {
            var store = new InMemoryPetFlipBestRecordStore();
            var client = CreateFixedClient(store);

            // 先完成一局，落盘本机最佳 6 次。
            client.StartOrResume();
            CompleteAcceptanceDeckInSixFlips(client);
            Assert.That(store.RawValue, Is.EqualTo("1|6"));

            // 1. 明确登出：丢弃新开的未完成盘，且未登录不能再开局；本机最佳保留。
            client.StartNewRound();
            client.FlipCard(0);
            client.NotifyLoggedOut();
            Assert.That(client.GetSnapshot().HasBoard, Is.False);
            Assert.That(client.GetSnapshot().Phase, Is.EqualTo(PetFlipRoundPhase.Idle));
            Assert.That(client.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(6));
            Assert.That(client.StartOrResume().RejectReason, Is.EqualTo(PetFlipRejectReason.NoActiveSession));

            // 2. 换角色：丢弃旧角色未完成盘，设备本机最佳仍为 6。
            client.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);
            client.StartOrResume();
            client.FlipCard(0);
            client.NotifySessionRestored(20002, DefaultZoneId);
            Assert.That(client.GetSnapshot().HasBoard, Is.False);
            Assert.That(client.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(6));

            // 3. 切服（含重定向加载态落到新区）：丢弃未完成盘；若重定向落回同区同角色则保留。
            client.StartOrResume();
            client.FlipCard(0);
            client.SyncHostState(0, 0, isInGameConnected: false, isReconnecting: false,
                isInBattle: false, isSpectating: false, isLoading: true);
            Assert.That(client.GetSnapshot().HasBoard, Is.True);
            Assert.That(client.GetSnapshot().ShouldCollapsePanel, Is.True);
            client.SyncHostState(20002, DefaultZoneId, isInGameConnected: true, isReconnecting: false,
                isInBattle: false, isSpectating: false, isLoading: false);
            Assert.That(client.GetSnapshot().HasBoard, Is.True, "同区同角色迁移应保留盘面");

            client.SyncHostState(0, 0, isInGameConnected: false, isReconnecting: false,
                isInBattle: false, isSpectating: false, isLoading: true);
            client.SyncHostState(20002, 2, isInGameConnected: true, isReconnecting: false,
                isInBattle: false, isSpectating: false, isLoading: false);
            Assert.That(client.GetSnapshot().HasBoard, Is.False, "切服到 2 区应丢弃未完成盘");
            Assert.That(client.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(6));

            // 4. 进程/控制器销毁：清内存盘面、拒绝后续调用，保留已持久化本机最佳。
            client.StartOrResume();
            client.FlipCard(0);
            client.Dispose();
            Assert.That(client.GetSnapshot().Phase, Is.EqualTo(PetFlipRoundPhase.Disposed));
            Assert.That(client.GetSnapshot().HasBoard, Is.False);
            Assert.That(client.FlipCard(1).RejectReason, Is.EqualTo(PetFlipRejectReason.Disposed));
            Assert.That(store.RawValue, Is.EqualTo("1|6"));
        }

        // ── 成绩比较、损坏记录兜底与保存失败 ──────────────────────────────────

        [Test]
        public void ScoreComparison_CoversFirstNewBestTiedAndNormalCompletion()
        {
            var store = new InMemoryPetFlipBestRecordStore();
            using var client = CreateFixedClient(store);

            // 1. 无旧纪录：8 次完成 -> 首次纪录，写入存储。
            client.StartOrResume();
            PetFlipActionResult r1 = CompleteAcceptanceDeckWithExtraMismatches(client, extraMismatches: 2);
            Assert.That(r1.Snapshot.CompletionResult.FlipCount, Is.EqualTo(8));
            Assert.That(r1.Snapshot.CompletionResult.PreviousBestFlipCount, Is.Null);
            Assert.That(r1.Snapshot.CompletionResult.BestFlipCount, Is.EqualTo(8));
            Assert.That(r1.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));
            Assert.That(store.WriteCallCount, Is.EqualTo(1));
            Assert.That(store.RawValue, Is.EqualTo("1|8"));

            // 2. 等于旧纪录：8 次完成 -> 追平纪录，不重写磁盘。
            client.StartNewRound();
            PetFlipActionResult r2 = CompleteAcceptanceDeckWithExtraMismatches(client, extraMismatches: 2);
            Assert.That(r2.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.TiedBest));
            Assert.That(r2.Snapshot.CompletionResult.PreviousBestFlipCount, Is.EqualTo(8));
            Assert.That(r2.Snapshot.CompletionResult.BestFlipCount, Is.EqualTo(8));
            Assert.That(store.WriteCallCount, Is.EqualTo(1));

            // 3. 多于旧纪录：9 次完成 -> 普通完成，旧纪录不变。
            client.StartNewRound();
            PetFlipActionResult r3 = CompleteAcceptanceDeckWithExtraMismatches(client, extraMismatches: 3);
            Assert.That(r3.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.NormalCompletion));
            Assert.That(r3.Snapshot.CompletionResult.PreviousBestFlipCount, Is.EqualTo(8));
            Assert.That(r3.Snapshot.CompletionResult.BestFlipCount, Is.EqualTo(8));
            Assert.That(store.WriteCallCount, Is.EqualTo(1));

            // 4. 少于旧纪录：6 次完成 -> 刷新纪录，写入新最佳。
            client.StartNewRound();
            PetFlipActionResult r4 = CompleteAcceptanceDeckInSixFlips(client);
            Assert.That(r4.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.NewBest));
            Assert.That(r4.Snapshot.CompletionResult.PreviousBestFlipCount, Is.EqualTo(8));
            Assert.That(r4.Snapshot.CompletionResult.BestFlipCount, Is.EqualTo(6));
            Assert.That(store.WriteCallCount, Is.EqualTo(2));
            Assert.That(store.RawValue, Is.EqualTo("1|6"));

            // 5. 重启新进程读取同一本机存储：读回最佳 6 次且已保存，不残留整副牌。
            using var restarted = CreateFixedClient(store);
            Assert.That(restarted.GetSnapshot().HasBoard, Is.False);
            Assert.That(restarted.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(6));
            Assert.That(restarted.GetSnapshot().LocalRecord.PersistenceStatus, Is.EqualTo(PetFlipRecordPersistenceStatus.Saved));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("corrupted")]
        [TestCase("2|6")]
        [TestCase("1|5")]
        [TestCase("1|0")]
        [TestCase("1|-6")]
        [TestCase("1|abc")]
        [TestCase("1|6|extra")]
        [TestCase("1| 6")]
        public void MissingOrCorruptedRecord_TreatedAsNoRecord(string rawPayload)
        {
            var store = new InMemoryPetFlipBestRecordStore(rawPayload);
            using var client = CreateFixedClient(store);

            PetFlipLocalRecordSnapshot record = client.GetSnapshot().LocalRecord;
            Assert.That(record.HasBestRecord, Is.False);
            Assert.That(record.BestFlipCount, Is.Null);
            Assert.That(record.PersistedBestFlipCount, Is.Null);
            Assert.That(record.PersistenceStatus, Is.EqualTo(PetFlipRecordPersistenceStatus.NoRecord));
        }

        [Test]
        public void SaveFailure_ReportsUnsavedStatusAndDoesNotBlockContinuedPlay()
        {
            var store = new InMemoryPetFlipBestRecordStore { FailWrites = true };
            using var client = CreateFixedClient(store);

            // 首次纪录写入失败：正常完成、标记 Unsaved、允许继续再开一局。
            client.StartOrResume();
            PetFlipActionResult r1 = CompleteAcceptanceDeckWithExtraMismatches(client, extraMismatches: 1);
            Assert.That(r1.Outcome, Is.EqualTo(PetFlipActionOutcome.RoundCompleted));
            Assert.That(r1.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));
            Assert.That(r1.Snapshot.CompletionResult.BestFlipCount, Is.EqualTo(7));
            Assert.That(r1.Snapshot.CompletionResult.RecordPersistenceStatus, Is.EqualTo(PetFlipRecordPersistenceStatus.Unsaved));
            Assert.That(r1.Snapshot.CompletionResult.IsRecordUnsaved, Is.True);
            Assert.That(r1.Snapshot.LocalRecord.PersistedBestFlipCount, Is.Null);
            Assert.That(r1.Snapshot.CanStartNewRound, Is.True);

            // 不阻止继续玩下一局：打出更差成绩 8 次，不把内存最佳 7 覆写掉，保持 Unsaved。
            PetFlipActionResult r2Start = client.StartNewRound();
            Assert.That(r2Start.Accepted, Is.True);
            PetFlipActionResult r2 = CompleteAcceptanceDeckWithExtraMismatches(client, extraMismatches: 2);
            Assert.That(r2.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.NormalCompletion));
            Assert.That(r2.Snapshot.LocalRecord.BestFlipCount, Is.EqualTo(7));
            Assert.That(r2.Snapshot.LocalRecord.IsUnsaved, Is.True);

            // 存储恢复后刷新纪录为 6 次：写入成功，状态恢复 Saved。
            store.FailWrites = false;
            Assert.That(client.StartNewRound().Accepted, Is.True);
            PetFlipActionResult r3 = CompleteAcceptanceDeckInSixFlips(client);
            Assert.That(r3.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.NewBest));
            Assert.That(r3.Snapshot.LocalRecord.BestFlipCount, Is.EqualTo(6));
            Assert.That(r3.Snapshot.LocalRecord.PersistedBestFlipCount, Is.EqualTo(6));
            Assert.That(r3.Snapshot.LocalRecord.PersistenceStatus, Is.EqualTo(PetFlipRecordPersistenceStatus.Saved));
            Assert.That(store.RawValue, Is.EqualTo("1|6"));
        }

        [Test]
        public void ThrowingStore_IsHandledGracefullyOnBothReadAndWrite()
        {
            var store = new ThrowingBestRecordStore(initialPersisted: "1|9");
            using var client = CreateFixedClient(store);
            Assert.That(client.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(9));

            store.ThrowOnWrite = true;
            client.StartOrResume();
            PetFlipActionResult completed = CompleteAcceptanceDeckInSixFlips(client);
            Assert.That(completed.Outcome, Is.EqualTo(PetFlipActionOutcome.RoundCompleted));
            Assert.That(completed.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.NewBest));
            Assert.That(completed.Snapshot.LocalRecord.BestFlipCount, Is.EqualTo(6));
            Assert.That(completed.Snapshot.LocalRecord.PersistedBestFlipCount, Is.EqualTo(9));
            Assert.That(completed.Snapshot.LocalRecord.IsUnsaved, Is.True);
            Assert.That(completed.Snapshot.CanStartNewRound, Is.True);

            store.ThrowOnRead = true;
            using var failedReadClient = CreateFixedClient(store);
            Assert.That(failedReadClient.GetSnapshot().LocalRecord.PersistenceStatus, Is.EqualTo(PetFlipRecordPersistenceStatus.NoRecord));
        }

        // ── 正式开局随机洗牌不变量 ────────────────────────────────────────────

        [Test]
        public void ProductionClient_DealsValidRandomShuffledBoards()
        {
            var store = new InMemoryPetFlipBestRecordStore();
            using var client = new PetFlipClient(store, new Random(20261123));
            client.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);

            var observedSignatures = new HashSet<string>();
            for (int round = 0; round < 5; round++)
            {
                if (round == 0)
                    Assert.That(client.StartOrResume().Outcome, Is.EqualTo(PetFlipActionOutcome.StartedNewRound));
                else
                    Assert.That(client.StartNewRound().Outcome, Is.EqualTo(PetFlipActionOutcome.StartedNewRound));

                var revealed = new SpiritPetCardPattern[PetFlipConstants.BoardCardCount];
                // 第 1 步：成对探测 0..11 全部 12 张牌以获取真实牌面。
                for (int i = 0; i < PetFlipConstants.BoardCardCount; i += 2)
                {
                    PetFlipActionResult first = client.FlipCard(i);
                    Assert.That(first.Accepted, Is.True);
                    revealed[i] = first.FlippedPattern.Value;

                    PetFlipActionResult second = client.FlipCard(i + 1);
                    Assert.That(second.Accepted, Is.True);
                    revealed[i + 1] = second.FlippedPattern.Value;
                }

                // 第 2 步：按图案配对剩余未配对牌，直至整盘完成。
                for (int p = 0; p < PetFlipConstants.PairCount; p++)
                {
                    var slots = new List<int>();
                    PetFlipSnapshot snap = client.GetSnapshot();
                    for (int i = 0; i < PetFlipConstants.BoardCardCount; i++)
                    {
                        if (!snap.Cards[i].IsMatched && revealed[i] == (SpiritPetCardPattern)p)
                            slots.Add(i);
                    }

                    if (slots.Count == 2)
                    {
                        Assert.That(client.FlipCard(slots[0]).Accepted, Is.True);
                        PetFlipActionResult matched = client.FlipCard(slots[1]);
                        Assert.That(matched.Outcome,
                            Is.EqualTo(PetFlipActionOutcome.PairMatched).Or.EqualTo(PetFlipActionOutcome.RoundCompleted));
                    }
                }

                PetFlipSnapshot done = client.GetSnapshot();
                Assert.That(done.Phase, Is.EqualTo(PetFlipRoundPhase.Completed));
                for (int p = 0; p < PetFlipConstants.PairCount; p++)
                {
                    int actualCount = 0;
                    for (int i = 0; i < PetFlipConstants.BoardCardCount; i++)
                    {
                        if (done.Cards[i].VisiblePattern == (SpiritPetCardPattern)p)
                            actualCount++;
                    }
                    Assert.That(actualCount, Is.EqualTo(2), $"第 {round + 1} 局图案 {(SpiritPetCardPattern)p} 应恰好 2 张");
                }

                observedSignatures.Add(string.Join(",", revealed));
            }

            Assert.That(observedSignatures.Count, Is.GreaterThan(1), "正式开局应随机洗牌，不能每局相同");
        }

        [Test]
        public void CodecAndDeckValidation_EnforceInvariants()
        {
            Assert.That(PetFlipBestRecordCodec.Encode(6), Is.EqualTo("1|6"));
            Assert.That(PetFlipBestRecordCodec.TryDecode("1|12", out int decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(12));
            Assert.Throws<ArgumentOutOfRangeException>(() => PetFlipBestRecordCodec.Encode(5));

            Assert.Throws<ArgumentException>(() => PetFlipDeckFactory.ValidateDeck(new[] { SpiritPetCardPattern.A }));
            Assert.Throws<ArgumentException>(() => PetFlipDeckFactory.ValidateDeck(new[]
            {
                SpiritPetCardPattern.A, SpiritPetCardPattern.A, SpiritPetCardPattern.A,
                SpiritPetCardPattern.B, SpiritPetCardPattern.C, SpiritPetCardPattern.D,
                SpiritPetCardPattern.E, SpiritPetCardPattern.F, SpiritPetCardPattern.B,
                SpiritPetCardPattern.C, SpiritPetCardPattern.D, SpiritPetCardPattern.E
            }));
        }

        [Test]
        public void StreakAndPerfectRun_TrackConsecutiveMatchesResetOnMismatchAndPreserveAcrossPause()
        {
            using var client = CreateFixedClient();
            client.StartOrResume();

            // 连续配对 2 对：CurrentStreak=2, MaxStreak=2
            client.FlipCard(0);
            PetFlipActionResult m1 = client.FlipCard(6);
            Assert.That(m1.CurrentStreak, Is.EqualTo(1));
            Assert.That(m1.Snapshot.CurrentStreak, Is.EqualTo(1));
            Assert.That(m1.Snapshot.LastMatchedPattern, Is.EqualTo(SpiritPetCardPattern.A));

            // 翻下一手第一张清掉 LastMatchedPattern，但保留当前连击
            PetFlipActionResult f2 = client.FlipCard(1);
            Assert.That(f2.Snapshot.CurrentStreak, Is.EqualTo(1));
            Assert.That(f2.Snapshot.LastMatchedPattern, Is.Null);

            PetFlipActionResult m2 = client.FlipCard(7);
            Assert.That(m2.CurrentStreak, Is.EqualTo(2));
            Assert.That(m2.Snapshot.MaxStreak, Is.EqualTo(2));
            Assert.That(m2.Snapshot.LastMatchedPattern, Is.EqualTo(SpiritPetCardPattern.B));

            // 暂停续盘不丢连击与最近配对图案
            client.Pause(PetFlipInterruptFlags.PanelClosed);
            PetFlipSnapshot resumed = client.StartOrResume().Snapshot;
            Assert.That(resumed.CurrentStreak, Is.EqualTo(2));
            Assert.That(resumed.MaxStreak, Is.EqualTo(2));
            Assert.That(resumed.LastMatchedPattern, Is.EqualTo(SpiritPetCardPattern.B));

            // 失配清零 CurrentStreak，但保留 MaxStreak=2
            client.FlipCard(2);
            PetFlipActionResult miss = client.FlipCard(3);
            Assert.That(miss.Outcome, Is.EqualTo(PetFlipActionOutcome.PairMismatched));
            Assert.That(miss.CurrentStreak, Is.EqualTo(0));
            Assert.That(miss.Snapshot.CurrentStreak, Is.EqualTo(0));
            Assert.That(miss.Snapshot.MaxStreak, Is.EqualTo(2));
            Assert.That(miss.Snapshot.LastMatchedPattern, Is.Null);

            // 收尾剩余 4 对（最后连续 4 连，总 MaxStreak 升至 4，因有失配故非完美局）
            int[] rest = { 2, 8, 3, 9, 4, 10, 5, 11 };
            PetFlipActionResult end = null;
            for (int i = 0; i < rest.Length; i++) end = client.FlipCard(rest[i]);
            Assert.That(end.Outcome, Is.EqualTo(PetFlipActionOutcome.RoundCompleted));
            Assert.That(end.Snapshot.CompletionResult.MaxStreak, Is.EqualTo(4));
            Assert.That(end.Snapshot.CompletionResult.IsPerfectRun, Is.False);

            // 再开一局 6 次零失误完成：IsPerfectRun=true, MaxStreak=6
            client.StartNewRound();
            PetFlipActionResult perfect = CompleteAcceptanceDeckInSixFlips(client);
            Assert.That(perfect.Snapshot.CompletionResult.IsPerfectRun, Is.True);
            Assert.That(perfect.Snapshot.CompletionResult.MaxStreak, Is.EqualTo(6));
        }

        [Test]
        public void DailySeededDeck_IsDeterministicPerDateAndVariesAcrossDates()
        {
            SpiritPetCardPattern[] dayA1 = PetFlipDeckFactory.CreateDailySeededDeck(2026, 11, 25);
            SpiritPetCardPattern[] dayA2 = PetFlipDeckFactory.CreateDailySeededDeck(2026, 11, 25);
            SpiritPetCardPattern[] dayB = PetFlipDeckFactory.CreateDailySeededDeck(2026, 11, 26);
            SpiritPetCardPattern[] dayA16 = PetFlipDeckFactory.CreateDailySeededDeck(2026, 11, 25, PetFlipDifficulty.Advanced16);

            PetFlipDeckFactory.ValidateDeck(dayA1, PetFlipDifficulty.Relaxed12);
            PetFlipDeckFactory.ValidateDeck(dayB, PetFlipDifficulty.Relaxed12);
            PetFlipDeckFactory.ValidateDeck(dayA16, PetFlipDifficulty.Advanced16);
            Assert.That(dayA1, Is.EqualTo(dayA2));
            Assert.That(string.Join(",", dayA1), Is.Not.EqualTo(string.Join(",", dayB)));
        }

        // ── V2：进阶 16 张、今日灵签、灵目趣味与多桶本机最佳 ──────────────────

        private static SpiritPetCardPattern[] CreateAcceptanceDeck16() => new[]
        {
            SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C, SpiritPetCardPattern.D,
            SpiritPetCardPattern.E, SpiritPetCardPattern.F, SpiritPetCardPattern.G, SpiritPetCardPattern.H,
            SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C, SpiritPetCardPattern.D,
            SpiritPetCardPattern.E, SpiritPetCardPattern.F, SpiritPetCardPattern.G, SpiritPetCardPattern.H
        };

        [Test]
        public void Advanced16Difficulty_FullRunPerfectScoreIndependentRecordAndMidRoundConfigLock()
        {
            var store = new InMemoryPetFlipBestRecordStore("1|7");
            using var client = PetFlipClient.CreateForTesting(
                store,
                diff => diff == PetFlipDifficulty.Advanced16 ? CreateAcceptanceDeck16() : CreateAcceptanceDeck());
            client.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);

            // 旧 V1 轻松经典纪录 7 次自动兼容加载。
            Assert.That(client.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(7));

            // 切至进阶 16 张经典：独立分桶当前暂无纪录。
            PetFlipActionResult cfg = client.SelectConfiguration(PetFlipDifficulty.Advanced16, PetFlipGameMode.Classic);
            Assert.That(cfg.Outcome, Is.EqualTo(PetFlipActionOutcome.ConfigurationChanged));
            Assert.That(cfg.Snapshot.Difficulty, Is.EqualTo(PetFlipDifficulty.Advanced16));
            Assert.That(cfg.Snapshot.TotalPairCount, Is.EqualTo(8));
            Assert.That(cfg.Snapshot.LocalRecord.HasBestRecord, Is.False);

            PetFlipActionResult started = client.StartOrResume();
            Assert.That(started.Outcome, Is.EqualTo(PetFlipActionOutcome.StartedNewRound));
            Assert.That(started.Snapshot.Cards.Count, Is.EqualTo(16));
            Assert.That(client.FlipCard(16).RejectReason, Is.EqualTo(PetFlipRejectReason.InvalidSlotIndex));

            // 盘中严禁换档或回入口洗牌。
            Assert.That(client.SelectConfiguration(PetFlipDifficulty.Relaxed12, PetFlipGameMode.Classic).RejectReason,
                Is.EqualTo(PetFlipRejectReason.RoundNotCompleted));
            Assert.That(client.ReturnToEntryAfterCompletion().RejectReason,
                Is.EqualTo(PetFlipRejectReason.RoundNotCompleted));

            // 8 次零失误配齐全部 8 对。
            PetFlipActionResult finalStep = null;
            for (int p = 0; p < 8; p++)
            {
                Assert.That(client.FlipCard(p).Accepted, Is.True);
                finalStep = client.FlipCard(p + 8);
            }

            Assert.That(finalStep.Outcome, Is.EqualTo(PetFlipActionOutcome.RoundCompleted));
            Assert.That(finalStep.Snapshot.CompletionResult.FlipCount, Is.EqualTo(8));
            Assert.That(finalStep.Snapshot.CompletionResult.MatchedPairCount, Is.EqualTo(8));
            Assert.That(finalStep.Snapshot.CompletionResult.IsPerfectRun, Is.True);
            Assert.That(finalStep.Snapshot.CompletionResult.MaxStreak, Is.EqualTo(8));
            Assert.That(finalStep.Snapshot.CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));
            Assert.That(store.RawValue, Is.EqualTo("2|0:0=7,1:0=8"));

            // 终局后切换回轻松经典：清掉已完成盘回入口，并读出轻松经典原纪录 7 次。
            PetFlipActionResult back = client.SelectConfiguration(PetFlipDifficulty.Relaxed12, PetFlipGameMode.Classic);
            Assert.That(back.Outcome, Is.EqualTo(PetFlipActionOutcome.ConfigurationChanged));
            Assert.That(back.Snapshot.HasBoard, Is.False);
            Assert.That(back.Snapshot.LocalRecord.BestFlipCount, Is.EqualTo(7));

            // 重启新实例从 V2 存档完整恢复双桶纪录。
            using var reloaded = PetFlipClient.CreateForTesting(store, CreateAcceptanceDeck);
            reloaded.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);
            Assert.That(reloaded.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(7));
            reloaded.SelectConfiguration(PetFlipDifficulty.Advanced16, PetFlipGameMode.Classic);
            Assert.That(reloaded.GetSnapshot().LocalRecord.BestFlipCount, Is.EqualTo(8));
        }

        [Test]
        public void DailyChallengeMode_DealsSameDeckSameDayResetsBestNextDayAndIsolatesBuckets()
        {
            var store = new InMemoryPetFlipBestRecordStore();
            DateTime fakeToday = new DateTime(2026, 11, 25);
            using var client = PetFlipClient.CreateForTesting(store, () => fakeToday, new Random(42));
            client.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);

            client.SelectConfiguration(PetFlipDifficulty.Relaxed12, PetFlipGameMode.DailyChallenge);
            Assert.That(client.GetSnapshot().DailyDateKey, Is.EqualTo(20261125));
            Assert.That(client.GetSnapshot().LocalRecord.HasBestRecord, Is.False);

            // 同日开第一局并探测完整牌序后通关。
            client.StartOrResume();
            SpiritPetCardPattern[] day1Round1 = ProbeAndCompleteCurrentBoard(client);
            Assert.That(client.GetSnapshot().CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));
            Assert.That(store.RawValue, Does.Contain("0:1=").And.Contain("@20261125"));

            // 同日再玩一局：牌序必须完全一致。
            client.StartNewRound();
            SpiritPetCardPattern[] day1Round2 = ProbeAndCompleteCurrentBoard(client);
            Assert.That(day1Round2, Is.EqualTo(day1Round1));

            // 跨日后回到入口：今日最佳自动重置为暂无纪录，新开盘为新日期牌序。
            fakeToday = new DateTime(2026, 11, 26);
            client.ReturnToEntryAfterCompletion();
            PetFlipSnapshot nextDayEntry = client.GetSnapshot();
            Assert.That(nextDayEntry.DailyDateKey, Is.EqualTo(20261126));
            Assert.That(nextDayEntry.LocalRecord.HasBestRecord, Is.False);
            Assert.That(nextDayEntry.LocalRecord.PersistenceStatus, Is.EqualTo(PetFlipRecordPersistenceStatus.NoRecord));

            client.StartOrResume();
            SpiritPetCardPattern[] day2Round1 = ProbeAndCompleteCurrentBoard(client);
            Assert.That(string.Join(",", day2Round1), Is.Not.EqualTo(string.Join(",", day1Round1)));
            Assert.That(client.GetSnapshot().CompletionResult.Comparison, Is.EqualTo(PetFlipRecordComparison.FirstRecord));
            Assert.That(store.RawValue, Does.Contain("@20261126"));
        }

        [Test]
        public void SpiritEyeMode_FreePeekZeroFlipsPreservesStreakCoversOnNextClickAndRejectsInvalidStates()
        {
            var store = new InMemoryPetFlipBestRecordStore();
            using var client = PetFlipClient.CreateForTesting(store, _ => CreateAcceptanceDeck());
            client.NotifySessionRestored(DefaultPlayerId, DefaultZoneId);

            // 经典模式拒绝灵目窥牌。
            client.StartOrResume();
            Assert.That(client.UseSpiritEyePeek(0).RejectReason, Is.EqualTo(PetFlipRejectReason.SpiritEyeUnavailable));
            CompleteAcceptanceDeckInSixFlips(client);

            // 切至灵目趣味模式开局。
            client.SelectConfiguration(PetFlipDifficulty.Relaxed12, PetFlipGameMode.SpiritEye);
            PetFlipActionResult start = client.StartOrResume();
            Assert.That(start.Snapshot.RemainingSpiritEyeCharges, Is.EqualTo(1));
            Assert.That(start.Snapshot.CanUseSpiritEye, Is.True);

            // 先配成 1 对建立连击，再验证本手已翻第一张时拒绝灵目。
            client.FlipCard(0);
            Assert.That(client.UseSpiritEyePeek(1).RejectReason, Is.EqualTo(PetFlipRejectReason.SpiritEyeUnavailable));
            Assert.That(client.FlipCard(6).Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            Assert.That(client.GetSnapshot().CurrentStreak, Is.EqualTo(1));

            // 触发免费灵目窥看位置 1：不计次、不断连击、停留亮起不自动盖回。
            PetFlipActionResult peek = client.UseSpiritEyePeek(1);
            Assert.That(peek.Outcome, Is.EqualTo(PetFlipActionOutcome.SpiritEyePeeked));
            Assert.That(peek.FlippedPattern, Is.EqualTo(SpiritPetCardPattern.B));
            Assert.That(peek.Snapshot.FlipCount, Is.EqualTo(1));
            Assert.That(peek.Snapshot.CurrentStreak, Is.EqualTo(1));
            Assert.That(peek.Snapshot.RemainingSpiritEyeCharges, Is.EqualTo(0));
            Assert.That(peek.Snapshot.HandState, Is.EqualTo(PetFlipHandState.SpiritEyePeeking));
            Assert.That(peek.Snapshot.Cards[1].State, Is.EqualTo(PetFlipCardState.FaceUp));
            Assert.That(peek.Snapshot.Cards[1].VisiblePattern, Is.EqualTo(SpiritPetCardPattern.B));

            // 次数耗尽再次触发灵目被拒。
            Assert.That(client.UseSpiritEyePeek(2).RejectReason, Is.EqualTo(PetFlipRejectReason.SpiritEyeUnavailable));

            // 暂关面板再续盘：灵目亮起态完整保留。
            client.Pause(PetFlipInterruptFlags.PanelClosed);
            PetFlipSnapshot resumed = client.StartOrResume().Snapshot;
            Assert.That(resumed.HandState, Is.EqualTo(PetFlipHandState.SpiritEyePeeking));
            Assert.That(resumed.Cards[1].State, Is.EqualTo(PetFlipCardState.FaceUp));

            // 下一次直接点击刚窥看的位置 1：先盖回再作为新一手第 1 张翻开，次数仍为 1。
            PetFlipActionResult nextFirst = client.FlipCard(1);
            Assert.That(nextFirst.Outcome, Is.EqualTo(PetFlipActionOutcome.SpiritEyeClearedAndFirstCardFlipped));
            Assert.That(nextFirst.CoveredFirstSlotIndex, Is.EqualTo(1));
            Assert.That(nextFirst.FlippedSlotIndex, Is.EqualTo(1));
            Assert.That(nextFirst.Snapshot.FlipCount, Is.EqualTo(1));
            Assert.That(nextFirst.Snapshot.HandState, Is.EqualTo(PetFlipHandState.WaitingSecondCard));

            // 配中位置 7：连击延续到 2，翻牌次数为 2。
            PetFlipActionResult match2 = client.FlipCard(7);
            Assert.That(match2.Outcome, Is.EqualTo(PetFlipActionOutcome.PairMatched));
            Assert.That(match2.Snapshot.FlipCount, Is.EqualTo(2));
            Assert.That(match2.Snapshot.CurrentStreak, Is.EqualTo(2));
        }

        [TestCase("2|")]
        [TestCase("2|9:0=6")]
        [TestCase("2|0:9=6")]
        [TestCase("2|1:0=7")]
        [TestCase("2|0:0=6@20261125")]
        [TestCase("2|0:1=6@2026")]
        [TestCase("2|0:0=6,0:0=7")]
        public void V2RecordCodec_RejectsCorruptedPayloads(string badPayload)
        {
            Assert.That(PetFlipBestRecordCodec.TryDecodeTable(badPayload, out _), Is.False);
        }

        private static SpiritPetCardPattern[] ProbeAndCompleteCurrentBoard(PetFlipClient client)
        {
            PetFlipSnapshot init = client.GetSnapshot();
            int cardCount = init.Cards.Count;
            int pairCount = init.TotalPairCount;
            var revealed = new SpiritPetCardPattern[cardCount];

            for (int i = 0; i < cardCount; i += 2)
            {
                PetFlipActionResult a = client.FlipCard(i);
                Assert.That(a.Accepted, Is.True);
                revealed[i] = a.FlippedPattern.Value;

                PetFlipActionResult b = client.FlipCard(i + 1);
                Assert.That(b.Accepted, Is.True);
                revealed[i + 1] = b.FlippedPattern.Value;
            }

            for (int p = 0; p < pairCount; p++)
            {
                var slots = new List<int>(2);
                PetFlipSnapshot snap = client.GetSnapshot();
                for (int i = 0; i < cardCount; i++)
                {
                    if (!snap.Cards[i].IsMatched && revealed[i] == (SpiritPetCardPattern)p)
                        slots.Add(i);
                }

                if (slots.Count == 2)
                {
                    Assert.That(client.FlipCard(slots[0]).Accepted, Is.True);
                    PetFlipActionResult r = client.FlipCard(slots[1]);
                    Assert.That(r.Outcome,
                        Is.EqualTo(PetFlipActionOutcome.PairMatched).Or.EqualTo(PetFlipActionOutcome.RoundCompleted));
                }
            }

            Assert.That(client.GetSnapshot().Phase, Is.EqualTo(PetFlipRoundPhase.Completed));
            return revealed;
        }

        private sealed class ThrowingBestRecordStore : IPetFlipBestRecordStore
        {
            private readonly string _persisted;
            public bool ThrowOnRead { get; set; }
            public bool ThrowOnWrite { get; set; }

            public ThrowingBestRecordStore(string initialPersisted)
            {
                _persisted = initialPersisted;
            }

            public bool TryReadRaw(out string rawValue)
            {
                if (ThrowOnRead)
                    throw new InvalidOperationException("simulated read error");
                rawValue = _persisted;
                return true;
            }

            public bool TryWriteRaw(string rawValue)
            {
                if (ThrowOnWrite)
                    throw new InvalidOperationException("simulated write error");
                return true;
            }
        }
    }
}
