using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>直接覆盖正式 UI 使用的纯显示判定，可在无 Unity 引擎进程时执行。</summary>
    public sealed class BattleSkillTargetRangeTests
    {
        // 1001 是本人，1002 是队友，1003 是己方宠物；界面不能按单位种类排除友方宠物。
        [TestCase("enemy", 1001UL, 7u, false)]
        [TestCase("enemy", 1002UL, 7u, false)]
        [TestCase("enemy", 1003UL, 7u, false)]
        [TestCase("enemy", 2001UL, 9u, true)]
        [TestCase("ally", 1001UL, 7u, true)]
        [TestCase("ally", 1002UL, 7u, true)]
        [TestCase("ally", 1003UL, 7u, true)]
        [TestCase("ally", 2001UL, 9u, false)]
        [TestCase("self", 1001UL, 7u, true)]
        [TestCase("self", 1002UL, 7u, false)]
        [TestCase("self", 1003UL, 7u, false)]
        [TestCase("self", 2001UL, 9u, false)]
        [TestCase(null, 1001UL, 7u, true)]
        [TestCase(null, 2001UL, 9u, true)]
        [TestCase("", 1003UL, 7u, true)]
        [TestCase("legacy", 2001UL, 9u, true)]
        [TestCase("ALLY", 1003UL, 7u, true)]
        public void MetadataControlsOnlyTheVisibleTargetRange(string targetTeam, ulong targetId, uint team, bool expected)
        {
            Assert.That(BattleTargetPresentation.AllowsTarget(targetTeam, 1001, 7, targetId, team, false, false),
                Is.EqualTo(expected));
        }

        [TestCase("enemy", 2001UL, 9u)]
        [TestCase("ally", 1003UL, 7u)]
        [TestCase("self", 1001UL, 7u)]
        [TestCase(null, 2001UL, 9u)]
        public void DeadOrFledUnitsAreNeverHighlighted(string targetTeam, ulong targetId, uint team)
        {
            Assert.That(BattleTargetPresentation.AllowsTarget(targetTeam, 1001, 7, targetId, team, true, false), Is.False);
            Assert.That(BattleTargetPresentation.AllowsTarget(targetTeam, 1001, 7, targetId, team, false, true), Is.False);
        }

        [Test]
        public void UnknownActingIdentityCannotSelectSelf()
        {
            Assert.That(BattleTargetPresentation.AllowsTarget("self", 0, 7, 0, 7, false, false), Is.False);
        }
    }

    /// <summary>真实 BattleScreen 的按钮、单位点击、快照和确认链路；只用假传输记录出站请求。</summary>
    public sealed class BattleSkillTargetSelectionTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ulong MyId = 1001, AllyId = 1002, PetId = 1003, EnemyId = 2001, DeadId = 2002, FledId = 2003;
        private const uint EnemySkill = 1101, SelfSkill = 1103, AllySkill = 1301, UnknownSkill = 999999;
        private GameObject _canvas;
        private BattleScreen _screen;
        private FakeBattleTransport _transport;
        private FakeBattleChannel _channel;
        private BattleStateS2C _state;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("BattleSkillTargetSelectionTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_canvas.transform).sizeDelta = new Vector2(2560, 1080);
            var ownerObject = new GameObject("InactiveBattleOwner");
            ownerObject.SetActive(false);
            ownerObject.transform.SetParent(_canvas.transform, false);
            var owner = ownerObject.AddComponent<BattleUiRoot>();
            owner.enabled = false;
            _transport = new FakeBattleTransport { PlayerId = MyId };
            _channel = new FakeBattleChannel { ReadyBattleId = 77 };
            var client = new BattleClient(_transport, _channel);
            typeof(BattleUiRoot).GetField("_client", Hidden).SetValue(owner, client);
            _state = CreateState();
            _transport.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = 77, State = _state });
            _screen = new BattleScreen(owner, _canvas.transform);
            _screen.Open(_state, MyId);
            _screen.OnPhaseChanged(BattlePhase.WaitingAction);
            _screen.AbortPlayback();
            _transport.Calls.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode 不调用面向运行场景的 BattleUnitView.Destroy；先清理 tween 再立即销毁测试画布。
            if (_screen != null)
            {
                _screen.Presenter.Dispose();
                foreach (var view in Views.Values)
                {
                    view.StopAction();
                    RealtimeTween.Kill(typeof(BattleUnitView).GetField("_idleToken", Hidden).GetValue(view));
                }
            }
            if (_canvas != null) Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void ActionTimer_ShowsServerDeadline_AndStaysVisibleAfterSubmitting()
        {
            _state.ActionDeadlineMs = (ulong)(BattleUiWidgets.NowUnixMs() + 25000);
            _screen.ApplyState(_state);
            _screen.Tick();
            var timer = _canvas.transform.Find("BattleScreen/Hud/ActionTimer");
            var seconds = timer.Find("TimerText").GetComponent<TMPro.TMP_Text>();
            Assert.That(timer.gameObject.activeSelf, Is.True);
            Assert.That(seconds.text, Is.EqualTo("25"));
            Assert.That(timer.Find("SecondsUnit").GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("秒"));

            // 自己提交后仍在等待其他玩家,倒计时继续显示同一个服务端截止时间。
            _screen.OnPhaseChanged(BattlePhase.Resolving);
            _screen.Tick();
            Assert.That(timer.gameObject.activeSelf, Is.True);
            Assert.That(seconds.text, Is.EqualTo("25"));

            _state.ActionDeadlineMs = (ulong)(BattleUiWidgets.NowUnixMs() - 1);
            _screen.ApplyState(_state);
            _screen.Tick();
            Assert.That(timer.gameObject.activeSelf, Is.True);
            Assert.That(seconds.text, Is.EqualTo("0"));

            _screen.OnPhaseChanged(BattlePhase.Ended);
            _screen.Tick();
            Assert.That(timer.gameObject.activeSelf, Is.False, "终局不留下空计时框");
        }

        [Test]
        public void ActionTimer_HidesWithoutDeadline_AndUsesPulledDeadlineOnReconnect()
        {
            var timer = _canvas.transform.Find("BattleScreen/Hud/ActionTimer");
            _state.ActionDeadlineMs = 0;
            _screen.ApplyState(_state);
            _screen.Tick();
            Assert.That(timer.gameObject.activeSelf, Is.False);

            // 补拉只显示剩余时间,不能重新从 25 开始。
            _state.ActionDeadlineMs = (ulong)(BattleUiWidgets.NowUnixMs() + 4000);
            _screen.ApplyState(_state);
            _screen.Tick();
            Assert.That(timer.gameObject.activeSelf, Is.True);
            Assert.That(timer.Find("TimerText").GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("4"));
        }

        [Test]
        public void EnemySkillUsesTheRealSkillButtonAndRejectsFriendlyClicks()
        {
            Click("Cmd_Spell");
            Click("Skill_" + EnemySkill);
            AssertHighlight(SlotHighlight.Targetable, EnemyId);
            AssertHighlight(SlotHighlight.None, MyId, AllyId, PetId, DeadId, FledId);
            ClickUnit(PetId);
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            ClickUnit(EnemyId);
            Click("ConfirmAction");
            AssertSubmitted(EnemySkill, EnemyId);
        }

        [Test]
        public void AllySkillIncludesSelfTeammateAndPetAndRejectsEnemyClicks()
        {
            Pick(AllySkill);
            AssertHighlight(SlotHighlight.Targetable, MyId, AllyId, PetId);
            AssertHighlight(SlotHighlight.None, EnemyId, DeadId, FledId);
            ClickUnit(EnemyId);
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            ClickUnit(PetId);
            Click("ConfirmAction");
            AssertSubmitted(AllySkill, PetId);
        }

        [Test]
        public void SelfSkillPreselectsOnlySelfAndWaitsForExplicitConfirmation()
        {
            Pick(SelfSkill);
            AssertHighlight(SlotHighlight.Selected, MyId);
            AssertHighlight(SlotHighlight.None, AllyId, PetId, EnemyId, DeadId, FledId);
            Assert.That(IsVisible("ConfirmAction"), Is.True);
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
            ClickUnit(AllyId);
            AssertHighlight(SlotHighlight.Selected, MyId);
            Click("ConfirmAction");
            AssertSubmitted(SelfSkill, MyId);
        }

        [Test]
        public void UnknownSkillMetadataKeepsLivingUnitsSelectable()
        {
            Pick(UnknownSkill);
            AssertHighlight(SlotHighlight.Targetable, MyId, AllyId, PetId, EnemyId);
            AssertHighlight(SlotHighlight.None, DeadId, FledId);
            ClickUnit(AllyId);
            Click("ConfirmAction");
            AssertSubmitted(UnknownSkill, AllyId);
        }

        [Test]
        public void CancelClearsTheSelectionAndLateUnitClicksDoNotRestoreIt()
        {
            Pick(SelfSkill);
            Click("CancelAction");
            AssertHighlight(SlotHighlight.None, MyId, AllyId, PetId, EnemyId);
            ClickUnit(MyId);
            Button("ConfirmAction").onClick.Invoke();
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            Assert.That(IsVisible("CancelAction"), Is.False);
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
        }

        [Test]
        public void OpeningSkillListClearsSelfConfirmationBeforePickingAnotherSkill()
        {
            Pick(SelfSkill);
            Click("Cmd_Spell");
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            AssertHighlight(SlotHighlight.None, MyId, AllyId, PetId, EnemyId);
            Click("Skill_" + EnemySkill);
            AssertHighlight(SlotHighlight.Targetable, EnemyId);
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            ClickUnit(MyId);
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            ClickUnit(EnemyId);
            Click("ConfirmAction");
            AssertSubmitted(EnemySkill, EnemyId);
        }

        [Test]
        public void SwitchingFromSelfSkillToAttackClearsTheSelectedSelf()
        {
            Pick(SelfSkill);
            Click("Cmd_Attack");
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            AssertHighlight(SlotHighlight.None, MyId, PetId);
            AssertHighlight(SlotHighlight.Targetable, EnemyId);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameRoundSnapshotInvalidatesDeadOrFledSelection(bool fled)
        {
            Pick(EnemySkill);
            ClickUnit(EnemyId);
            var next = _state.Clone();
            var target = next.Actors.Single(actor => actor.ActorId == EnemyId);
            target.IsDead = !fled;
            target.Fled = fled;
            _screen.ApplyState(next);
            AssertHighlight(SlotHighlight.None, EnemyId);
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            ClickUnit(EnemyId);
            Button("ConfirmAction").onClick.Invoke();
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
        }

        [Test]
        public void SameRoundSnapshotRestoresTheStillValidSelectionHighlight()
        {
            Pick(AllySkill);
            ClickUnit(PetId);
            _screen.ApplyState(_state.Clone());
            AssertHighlight(SlotHighlight.Selected, PetId);
            Assert.That(IsVisible("ConfirmAction"), Is.True);
        }

        [Test]
        public void PhaseChangeBlocksStaleConfirmationAndUnitClicks()
        {
            Pick(EnemySkill);
            ClickUnit(EnemyId);
            _screen.OnPhaseChanged(BattlePhase.Resolving);
            AssertSelectionHidden();
            ClickUnit(EnemyId);
            Button("ConfirmAction").onClick.Invoke();
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
            _screen.OnPhaseChanged(BattlePhase.WaitingAction);
            AssertSelectionHidden();
        }

        [TestCase("dead")]
        [TestCase("fled")]
        [TestCase("submitted")]
        public void SameRoundActorCannotActClearsSelection(string reason)
        {
            Pick(EnemySkill);
            ClickUnit(EnemyId);
            var next = _state.Clone();
            var me = next.Actors.Single(actor => actor.ActorId == MyId);
            if (reason == "dead") me.IsDead = true;
            else if (reason == "fled") me.Fled = true;
            else
            {
                next.PendingActorIds.Clear();
                next.PendingActorIds.Add(AllyId);
            }
            _screen.ApplyState(next);
            AssertSelectionHidden();
            Button("ConfirmAction").onClick.Invoke();
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
        }

        [Test]
        public void TemporaryChannelLossHidesControlsButRestoresTheSameSelectionWhenReady()
        {
            Pick(EnemySkill);
            ClickUnit(EnemyId);
            _channel.ReadyBattleId = 0;
            RefreshChannel();
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            AssertHighlight(SlotHighlight.None, MyId, AllyId, PetId, EnemyId);
            Button("ConfirmAction").onClick.Invoke();
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
            _channel.ReadyBattleId = 77;
            RefreshChannel();
            AssertHighlight(SlotHighlight.Selected, EnemyId);
            Assert.That(IsVisible("ConfirmAction"), Is.True);
            Assert.That(_transport.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);
            Click("ConfirmAction");
            AssertSubmitted(EnemySkill, EnemyId);
        }

        private void AssertSelectionHidden()
        {
            AssertHighlight(SlotHighlight.None, MyId, AllyId, PetId, EnemyId);
            Assert.That(IsVisible("ConfirmAction"), Is.False);
            Assert.That(IsVisible("CancelAction"), Is.False);
        }

        private Dictionary<ulong, BattleUnitView> Views =>
            (Dictionary<ulong, BattleUnitView>)typeof(BattleScreen).GetField("_viewById", Hidden).GetValue(_screen);
        private Button Button(string name) => _canvas.GetComponentsInChildren<Button>(true).Single(button => button.name == name + "Plate");
        private bool IsVisible(string name) => Button(name).gameObject.activeInHierarchy;
        private void Click(string name)
        {
            var button = Button(name);
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
        private void ClickUnit(ulong id) => Views[id].Root.Find("Hit").GetComponent<Button>().onClick.Invoke();
        // 个别用例直接调用技能卡绑定的回调，避免为无关的资源列表反复重建对象。
        private void Pick(uint id) => typeof(BattleScreen).GetMethod("OnSkillPicked", Hidden).Invoke(_screen, new object[] { id });
        private void RefreshChannel() => typeof(BattleScreen).GetMethod("RefreshChannelState", Hidden).Invoke(_screen, null);
        private void AssertHighlight(SlotHighlight expected, params ulong[] ids)
        {
            foreach (var id in ids) Assert.That(Views[id].Highlight, Is.EqualTo(expected), "actor " + id);
        }
        private void AssertSubmitted(uint skillId, ulong targetId)
        {
            var calls = _transport.CallsOf(MessageIds.SubmitBattleAction);
            Assert.That(calls.Count, Is.EqualTo(1));
            var action = ((SubmitBattleActionRequest)calls[0].Request).Action;
            Assert.That(action.ActionType, Is.EqualTo(eBattleActionType.BattleActionSkill));
            Assert.That(action.SkillTableId, Is.EqualTo(skillId));
            Assert.That(action.TargetId, Is.EqualTo(targetId));
        }
        private static BattleStateS2C CreateState()
        {
            var state = new BattleStateS2C { BattleId = 77, RoundIndex = 1 };
            state.PendingActorIds.Add(MyId);
            state.Actors.Add(Actor(MyId, 0, eBattleActorType.BattleActorTypePlayer));
            state.Actors.Add(Actor(AllyId, 0, eBattleActorType.BattleActorTypePlayer));
            state.Actors.Add(Actor(PetId, 0, eBattleActorType.BattleActorTypePet));
            state.Actors.Add(Actor(EnemyId, 1, eBattleActorType.BattleActorTypeMonster));
            state.Actors.Add(Actor(DeadId, 1, eBattleActorType.BattleActorTypeMonster));
            state.Actors.Add(Actor(FledId, 1, eBattleActorType.BattleActorTypeMonster));
            state.Actors[0].SkillTableIds.Add(new[] { EnemySkill, SelfSkill, AllySkill, UnknownSkill });
            state.Actors[4].IsDead = true;
            state.Actors[5].Fled = true;
            return state;
        }
        private static BattleActorState Actor(ulong id, uint team, eBattleActorType type) => new BattleActorState
        {
            ActorId = id, TeamIndex = team, ActorType = type, Name = "目标" + id,
            Attributes = new BaseAttributesComp { Health = 100, Mana = 100 }, MaxHealth = 100, MaxMana = 100,
        };
    }
}
