using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Skills;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Skills
{
    /// <summary>
    /// SkillBookClient(技能书业务适配器)经假传输层的行为,服务端契约见 docs/design/class-skills.md §4。
    /// 断言只用生成的消息号与 tip 枚举,不写数字。
    /// </summary>
    public sealed class SkillBookClientTests
    {
        private SkillFakeTransport _net;
        private object _connection;
        private SkillBookClient _client;
        private int _changes;

        [SetUp]
        public void SetUp()
        {
            _net = new SkillFakeTransport();
            _connection = new object();
            _client = new SkillBookClient(_net, _net, () => _connection, _ => null);
            _client.Changed += () => ++_changes;
        }

        [TearDown] public void TearDown() => _client.Dispose();

        private static SkillCatalogEntry Row(uint id, uint rank, uint limit, ulong cost, uint blockTip = 0,
            bool passive = false, uint team = 1)
            => new SkillCatalogEntry
            {
                SkillTableId = id, Name = "技能" + id, Description = "说明", IconKey = "UI/Skills/" + id, ClassId = 1,
                Category = passive ? "被动" : "主动", Passive = passive, TargetTeam = team, MaxTargets = 1,
                UnlockLevel = 1, MaxRank = 10, Rank = rank, RankLimit = limit, ManaPercent = 0.03,
                CooldownRounds = 0, NextRankCost = cost, UpgradeBlockTipId = blockTip,
            };

        private static ListSkillsResponse Book(ulong balance, params SkillCatalogEntry[] rows)
        {
            var book = new ListSkillsResponse { SkillList = new PlayerSkillListComp(), ClassId = 1, PlayerLevel = 30, CurrencyBalance = balance };
            book.Catalog.AddRange(rows);
            return book;
        }

        private void LoadBook(ListSkillsResponse book)
        {
            _client.Refresh(SkillBookContext.Character());
            _net.Reply(book);
        }

        [Test]
        public void StartsDisconnectedUntilRefreshedInGame()
        {
            Assert.That(_client.State.IsConnected, Is.False, "默认断线:没拿到权威技能书之前不能修习");
            Assert.That(_client.State.Snapshot, Is.Null);
        }

        [Test]
        public void RefreshSendsListSkillsAndReplacesTheWholeSnapshot()
        {
            _client.Refresh(SkillBookContext.Character());
            Assert.That(_net.Calls, Is.EqualTo(new[] { MessageIds.ListSkills }));
            Assert.That(_client.State.IsLoading, Is.True);

            _net.Reply(Book(5000, Row(1102, 0, 10, 200), Row(1101, 3, 10, 1610)));
            var state = _client.State;
            Assert.That(state.IsLoading, Is.False);
            Assert.That(state.IsConnected, Is.True);
            Assert.That(state.Error, Is.Null.Or.Empty);
            Assert.That(state.Snapshot.CurrencyBalance, Is.EqualTo(5000UL));
            Assert.That(state.Snapshot.PlayerLevel, Is.EqualTo(30u));
            Assert.That(state.Snapshot.Entries.ConvertAll(e => e.Id), Is.EqualTo(new List<uint> { 1101, 1102 }), "按 id 升序");
            var entry = state.Snapshot.Find(1101);
            Assert.That(entry.Rank, Is.EqualTo(3u));
            Assert.That(entry.NextRankCost, Is.EqualTo(1610UL));
            Assert.That(entry.Definition.IconResource, Is.EqualTo("UI/Skills/1101"));
            Assert.That(SkillBookViewState.CanUpgrade(state, entry), Is.True);
        }

        [Test]
        public void ServerBlockReasonBecomesLockedReasonText()
        {
            LoadBook(Book(10, Row(1102, 0, 10, 200, (uint)skill_error.KSkillGoldNotEnough)));
            var entry = _client.State.Snapshot.Find(1102);
            Assert.That(entry.LockedReason, Is.EqualTo(SkillBookMapper.DescribeTip((uint)skill_error.KSkillGoldNotEnough)));
            Assert.That(SkillBookViewState.CanUpgrade(_client.State, entry), Is.False);
        }

        [Test]
        public void UpgradeSendsTheExpectedRankTheUiSaw()
        {
            LoadBook(Book(100000, Row(1102, 2, 10, 1000)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 2, 10);
            Assert.That(_net.Calls[^1], Is.EqualTo(MessageIds.UpgradeSkill));
            var request = (UpgradeSkillRequest)_net.LastRequest;
            Assert.That(request.SkillTableId, Is.EqualTo(1102u));
            Assert.That(request.ExpectedRank, Is.EqualTo(2u));
            Assert.That(request.Count, Is.EqualTo(10u));
            Assert.That(_client.State.IsBusy, Is.True);
        }

        [Test]
        public void SuccessfulUpgradeAdoptsTheServerBookAndReportsPartialStop()
        {
            LoadBook(Book(100000, Row(1102, 0, 2, 200)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 0, 10);
            _net.Reply(new UpgradeSkillResponse
            {
                State = Book(99300, Row(1102, 2, 2, 1000, (uint)skill_error.KSkillLevelNotEnough)),
                UpgradedCount = 2,
                StopTipId = (uint)skill_error.KSkillLevelNotEnough,
            });
            var state = _client.State;
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.Error, Is.Null.Or.Empty);
            Assert.That(state.Snapshot.Find(1102).Rank, Is.EqualTo(2u), "等级来自回包,不在本地推算");
            Assert.That(state.Snapshot.CurrencyBalance, Is.EqualTo(99300UL));
            Assert.That(state.Message, Does.Contain("提升 2 重"));
            Assert.That(state.Message, Does.Contain(SkillBookMapper.DescribeTip((uint)skill_error.KSkillLevelNotEnough)));
        }

        [Test]
        public void RejectedUpgradeWithFreshBookKeepsUpgradingPossible()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            _net.Reply(new UpgradeSkillResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)skill_error.KSkillRankChanged },
                State = Book(99000, Row(1102, 3, 10, 1000)),
            });
            var state = _client.State;
            Assert.That(state.Error, Is.Null.Or.Empty, "拒绝也带了权威技能书:原因放 Message,不挡下一次修习");
            Assert.That(state.Message, Does.Contain(SkillBookMapper.DescribeTip((uint)skill_error.KSkillRankChanged)));
            Assert.That(state.Snapshot.Find(1102).Rank, Is.EqualTo(3u));
            Assert.That(SkillBookViewState.CanUpgrade(state, state.Snapshot.Find(1102)), Is.True);
        }

        [Test]
        public void TimeoutLeavesTheResultUnconfirmedUntilRefresh()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            _net.Fail("请求超时");
            var state = _client.State;
            Assert.That(state.IsBusy, Is.False, "失败后要清 busy");
            Assert.That(state.Error, Does.Contain("未确认"));
            Assert.That(state.Snapshot.Find(1102).Rank, Is.EqualTo(1u), "不猜成功,不补扣");
            Assert.That(SkillBookViewState.CanUpgrade(state, state.Snapshot.Find(1102)), Is.False, "Error 非空禁止修习");

            _client.Refresh(SkillBookContext.Character());
            _net.Reply(Book(99500, Row(1102, 2, 10, 1000)));
            Assert.That(_client.State.Error, Is.Null.Or.Empty, "成功刷新清掉旧错误");
            Assert.That(_client.State.Snapshot.Find(1102).Rank, Is.EqualTo(2u));
        }

        [Test]
        public void NotSentClearsBusyWithoutClaimingTheRequestReachedTheServer()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            _net.Fail(RpcStatus.NotSent("not ready"));
            Assert.That(_client.State.IsBusy, Is.False);
            Assert.That(_client.State.Error, Does.Contain("未发送"));
            Assert.That(_client.State.Snapshot.CurrencyBalance, Is.EqualTo(100000UL));
            Assert.That(_client.State.Snapshot.Find(1102).Rank, Is.EqualTo(1u));
        }

        [Test]
        public void EnvelopeTipIsReadFromTheStructuredRpcStatus()
        {
            _client.Refresh(SkillBookContext.Character());
            _net.Fail(RpcStatus.ServerTip((uint)skill_error.KSkillUpgradeBusy));
            Assert.That(_client.State.IsLoading, Is.False);
            Assert.That(_client.State.Error, Does.Contain(SkillBookMapper.DescribeTip((uint)skill_error.KSkillUpgradeBusy)));
        }

        [Test]
        public void OnlyOneRequestInFlight()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            _client.Refresh(SkillBookContext.Character());
            Assert.That(_net.Calls.FindAll(id => id == MessageIds.UpgradeSkill).Count, Is.EqualTo(1));
            Assert.That(_net.Calls.FindAll(id => id == MessageIds.ListSkills).Count, Is.EqualTo(1), "只有最开始那次拉取");
        }

        [Test]
        public void InvalidCountIsNotSent()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 3);
            Assert.That(_net.Calls.FindAll(id => id == MessageIds.UpgradeSkill).Count, Is.Zero);
            Assert.That(_client.State.Message, Is.EqualTo(SkillBookClient.InvalidCountMessage));
        }

        [Test]
        public void ReplyFromAPreviousConnectionIsDropped()
        {
            _client.Refresh(SkillBookContext.Character());
            _connection = new object();  // 连接被替换(重定向 / 重连)
            _net.Reply(Book(5000, Row(1101, 1, 10, 410)));
            Assert.That(_client.State.Snapshot, Is.Null, "旧连接上的回包不能覆盖新会话");
        }

        [Test]
        public void ReplyAfterSwitchingPlayerIsDropped()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            _net.PlayerId = 2;  // 换号
            _net.Reply(new UpgradeSkillResponse { UpgradedCount = 1, State = Book(99500, Row(1102, 2, 10, 1000)) });
            Assert.That(_client.State.Snapshot, Is.Null, "上一个号的修习回包不能落到新号上");
            Assert.That(_client.State.IsBusy, Is.False);
        }

        [Test]
        public void DisconnectClearsTheAuthoritativeSnapshot()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _net.Disconnect();
            Assert.That(_client.State.Snapshot, Is.Null);
            Assert.That(_client.State.IsConnected, Is.False);
            Assert.That(_client.State.Message, Is.EqualTo(SkillBookClient.DisconnectedMessage));
        }

        [Test]
        public void RefreshWhileNotInGameDoesNotSend()
        {
            _net.IsReady = false;
            _client.Refresh(SkillBookContext.Character());
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.State.Message, Is.EqualTo(SkillBookClient.NotInGameMessage));
        }

        [Test]
        public void PetContextIsReadOnlyAndNeverFakesSkills()
        {
            var pet = SkillBookContext.Pet(88, "小白");
            _client.Refresh(pet);
            Assert.That(_net.Calls, Is.Empty, "没有宠物技能协议,不发任何请求");
            var state = _client.State;
            Assert.That(pet.Matches(state.Context), Is.True);
            Assert.That(state.IsReadOnly, Is.True);
            Assert.That(state.Snapshot.Entries, Is.Empty);
            Assert.That(state.Message, Is.EqualTo(SkillBookClient.PetUnsupportedMessage));

            _client.Upgrade(pet, 1101, 0, 1);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void UpgradeReplyAfterSwitchingToPetDoesNotOverwriteThePetView()
        {
            LoadBook(Book(100000, Row(1102, 1, 10, 500)));
            _client.Upgrade(SkillBookContext.Character(), 1102, 1, 1);
            var pet = SkillBookContext.Pet(88, "小白");
            _client.Refresh(pet);
            _net.Reply(new UpgradeSkillResponse { UpgradedCount = 1, State = Book(99500, Row(1102, 2, 10, 1000)) });
            Assert.That(pet.Matches(_client.State.Context), Is.True);

            _client.Refresh(SkillBookContext.Character());  // 回到角色页重新拉取
            Assert.That(_net.Calls[^1], Is.EqualTo(MessageIds.ListSkills));
        }

        [Test]
        public void ServerTipOnRefreshIsAnErrorThatStillAllowsRefresh()
        {
            _client.Refresh(SkillBookContext.Character());
            _net.Reply(new ListSkillsResponse { ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable } });
            Assert.That(_client.State.Error, Does.Contain(SkillBookMapper.DescribeTip((uint)common_error.KServiceUnavailable)));
            Assert.That(_client.State.IsLoading, Is.False);
            _client.Refresh(SkillBookContext.Character());
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void EmptyCatalogIsShownAsSuch()
        {
            LoadBook(Book(0));
            Assert.That(_client.State.Snapshot.Entries, Is.Empty);
            Assert.That(_client.State.Message, Is.EqualTo(SkillBookClient.EmptyCatalogMessage));
        }

        [Test]
        public void ChangedFiresForEveryStateTransition()
        {
            int before = _changes;
            LoadBook(Book(5000, Row(1101, 1, 10, 410)));
            Assert.That(_changes - before, Is.GreaterThanOrEqualTo(2), "loading 与回包各通知一次");
        }
    }

    internal sealed class SkillFakeTransport : IBattleTransport, IRpcChannel
    {
        public ulong PlayerId { get; set; } = 1;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public List<uint> Calls { get; } = new();
        public IMessage LastRequest;
        private Action<IMessage> _pending;
        private Action<RpcStatus> _error;
        public void Reply(IMessage response) => _pending(response);
        public void Fail(string error) => Fail(RpcStatus.Timeout(error));
        public void Fail(RpcStatus status) => _error(status);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void RegisterNotify(uint id, Action<MessageContent> action) { }
        public void SendOneWay(uint id, IMessage request) { }
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T>
            => throw new InvalidOperationException("技能书必须走类型化 RPC");
        public void CallMethod<T>(uint id, IMessage request, MessageParser<T> parser, RpcDone<T> done)
            where T : IMessage<T>
        {
            Calls.Add(id);
            LastRequest = request;
            _pending = message => done(RpcStatus.Ok, (T)message);
            _error = status => done(status, default);
        }
    }
}
