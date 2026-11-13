using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.BattleScripts;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.BattleScripts
{
    /// <summary>
    /// BattleScriptClient 经假传输层的行为(服务端 docs/design/battle-script.md §7)。
    /// 断言只用生成的消息号与 tip 枚举,不写数字。
    /// </summary>
    public sealed class BattleScriptClientTests
    {
        private BattleScriptFakeTransport _net;
        private object _connection;
        private BattleScriptClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new BattleScriptFakeTransport();
            _connection = new object();
            _client = new BattleScriptClient(_net, () => _connection);
        }

        [TearDown] public void TearDown() => _client.Dispose();

        private static global::BattleScript Script(string name) => new()
        {
            SchemaVersion = 1,
            Name = name,
            Roles =
            {
                new BattleScriptRole
                {
                    RoleId = 1, Order = 10, Name = "输出",
                    Rules = { new BattleScriptRule { Priority = 10, Action = eBattleActionType.BattleActionAttack,
                        Target = new BattleScriptTarget { Side = eBattleScriptTargetSide.BattleScriptTargetEnginePick } } },
                },
            },
        };

        private static ListBattleScriptsResponse List(uint maxBytes = 900)
        {
            var response = new ListBattleScriptsResponse { MaxScripts = 10, MaxScriptBytes = maxBytes };
            response.OfficialScripts.Add(new global::BattleScript { SchemaVersion = 1, ScriptId = 1, Name = "四职业·集火控速" });
            response.MyScripts.Add(new PlayerBattleScriptView
            {
                ScriptId = 7001, Revision = 2, Content = Script("我的打法"),
                ReviewState = BattleScriptReviewState.BattleScriptReviewPending, ApprovedRevision = 1,
            });
            response.Loadout.Add(new BattleScriptLoadoutEntry
            {
                MatchMode = (uint)MatchMode.PveTeam, Origin = eBattleScriptOrigin.BattleScriptOriginPlayer, PlayerScriptId = 7001,
            });
            return response;
        }

        private void Loaded(uint maxBytes = 900)
        {
            _client.Refresh();
            _net.Reply(List(maxBytes));
        }

        [Test]
        public void RefreshSendsListWithoutAnyPlayerId()
        {
            _client.Refresh();
            Assert.That(_net.Calls, Is.EqualTo(new[] { MatchServiceRpc.ListBattleScriptsMessageId }));
            Assert.That(_client.Busy, Is.True);
            // 身份只取 gate 会话:请求协议里根本没有 player_id 字段
            Assert.That(ListBattleScriptsRequest.Descriptor.Fields.InDeclarationOrder().Any(f => f.Name.Contains("player")), Is.False);
            _net.Reply(List());
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.List.MyScripts.Single().ScriptId, Is.EqualTo(7001));
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void SubmitSuccessRefreshesTheList()
        {
            Loaded();
            _client.Submit(0, 0, Script("新打法"));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MatchServiceRpc.SubmitBattleScriptMessageId));
            var request = (SubmitBattleScriptRequest)_net.LastRequest;
            Assert.That(request.ScriptId, Is.EqualTo(0));
            Assert.That(request.Script.Name, Is.EqualTo("新打法"));
            _net.Reply(new SubmitBattleScriptResponse { Script = new PlayerBattleScriptView { ScriptId = 7002, Revision = 1 } });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MatchServiceRpc.ListBattleScriptsMessageId), "a successful write refreshes the list");
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.SubmittedMessage));
            _net.Reply(List());
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.SubmittedMessage), "the write result stays visible after the refresh");
        }

        [Test]
        public void InvalidScriptShowsTheServerDetailWithoutRefreshing()
        {
            Loaded();
            int calls = _net.Calls.Count;
            _client.Submit(0, 0, Script("坏打法"));
            _net.Reply(new SubmitBattleScriptResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchBattleScriptInvalid },
                InvalidDetail = "script 0 role 1 rule 10: unknown skill 4242",
            });
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.DescribeTip((uint)match_error.KMatchBattleScriptInvalid)));
            Assert.That(_client.InvalidDetail, Does.Contain("unknown skill"));
        }

        [Test]
        public void WriteTimeoutBlocksFurtherWritesUntilARefreshSucceeds()
        {
            Loaded();
            _client.Delete(7001, 2);
            _net.Fail(BattleScriptClient.TimeoutError);
            Assert.That(_client.RequiresRefresh, Is.True);
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.RecoveryMessage));

            int calls = _net.Calls.Count;
            _client.ChooseOfficial((uint)MatchMode.PveSolo, 0, 1);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "no write while the previous outcome is unknown");

            _client.Refresh();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MatchServiceRpc.ListBattleScriptsMessageId));
            _net.Reply(List());
            Assert.That(_client.RequiresRefresh, Is.False);
        }

        [Test]
        public void ReadTimeoutDoesNotBlockWrites()
        {
            _client.Refresh();
            _net.Fail(BattleScriptClient.TimeoutError);
            Assert.That(_client.RequiresRefresh, Is.False);
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.TransportFailureMessage));
        }

        [Test]
        public void BusyClientRejectsASecondRequest()
        {
            _client.Refresh();
            _client.Delete(7001, 2);
            Assert.That(_net.Calls, Is.EqualTo(new[] { MatchServiceRpc.ListBattleScriptsMessageId }));
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.LoadingMessage));
        }

        [Test]
        public void CallbacksFromAnOldConnectionAreIgnored()
        {
            _client.Refresh();
            _connection = new object();
            _client.ObserveConnection();
            _net.Reply(List());
            Assert.That(_client.List, Is.Null);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void TooLargeScriptIsRejectedBeforeSending()
        {
            Loaded(maxBytes: 8);
            int calls = _net.Calls.Count;
            _client.Submit(0, 0, Script("这一份明显超过八个字节"));
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.DescribeTip((uint)match_error.KMatchBattleScriptTooLarge)));
        }

        [Test]
        public void LoadoutReplyReplacesTheChoices()
        {
            Loaded();
            Assert.That(_client.ChoiceFor((uint)MatchMode.PveTeam, 0).PlayerScriptId, Is.EqualTo(7001));
            _client.ChooseOfficial((uint)MatchMode.PveSolo, 0, 1);
            var request = (SetBattleScriptLoadoutRequest)_net.LastRequest;
            Assert.That(request.Entry.Origin, Is.EqualTo(eBattleScriptOrigin.BattleScriptOriginOfficial));
            var reply = new SetBattleScriptLoadoutResponse();
            reply.Loadout.Add(new BattleScriptLoadoutEntry
            {
                MatchMode = (uint)MatchMode.PveSolo, Origin = eBattleScriptOrigin.BattleScriptOriginOfficial, OfficialScriptId = 1,
            });
            _net.Reply(reply);
            Assert.That(_client.ChoiceFor((uint)MatchMode.PveSolo, 0).OfficialScriptId, Is.EqualTo(1));
            Assert.That(_client.ChoiceFor((uint)MatchMode.PveTeam, 0), Is.Null, "the server's list is authoritative");
            Assert.That(_client.Status, Is.EqualTo(BattleScriptClient.LoadoutSavedMessage));
        }

        [Test]
        public void ClearChoiceSendsAnUnspecifiedOrigin()
        {
            Loaded();
            _client.ClearChoice((uint)MatchMode.PveTeam, 0);
            var request = (SetBattleScriptLoadoutRequest)_net.LastRequest;
            Assert.That(request.Entry.Origin, Is.EqualTo(eBattleScriptOrigin.BattleScriptOriginUnspecified));
            Assert.That(request.Entry.MatchMode, Is.EqualTo((uint)MatchMode.PveTeam));
        }

        [Test]
        public void ImportParsesProtobufJsonAndRejectsUnknownFields()
        {
            const string json = "{\"schema_version\":1,\"name\":\"导入\",\"roles\":[{\"role_id\":1,\"order\":10," +
                "\"rules\":[{\"priority\":10,\"action\":\"BATTLE_ACTION_ATTACK\",\"target\":{\"side\":\"BATTLE_SCRIPT_TARGET_ENGINE_PICK\"}}]}]}";
            Assert.That(BattleScriptClient.TryParseScript(json, out var script, out var error), Is.True, error);
            Assert.That(script.Name, Is.EqualTo("导入"));
            Assert.That(script.Roles.Single().Rules.Single().Action, Is.EqualTo(eBattleActionType.BattleActionAttack));

            Assert.That(BattleScriptClient.TryParseScript("{\"nmae\":\"typo\"}", out _, out error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(BattleScriptClient.TryParseScript("", out _, out _), Is.False);
        }

        [Test]
        public void ReviewTextShowsStateAndTheRevisionInUse()
        {
            var view = new PlayerBattleScriptView { Revision = 3, ApprovedRevision = 2, ReviewState = BattleScriptReviewState.BattleScriptReviewRejected, RejectReason = "规则矛盾" };
            Assert.That(BattleScriptClient.DescribeReview(view), Is.EqualTo("已驳回：规则矛盾 · 生效第 2 版"));
            view = new PlayerBattleScriptView { Revision = 1, ApprovedRevision = 1, ReviewState = BattleScriptReviewState.BattleScriptReviewApproved };
            Assert.That(BattleScriptClient.DescribeReview(view), Is.EqualTo("已通过"));
        }
    }

    internal sealed class BattleScriptFakeTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = 1;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public List<uint> Calls { get; } = new();
        public IMessage LastRequest;
        private Action<IMessage> _pending;
        private Action<string> _error;
        public void Reply(IMessage response) => _pending(response);
        public void Fail(string error) => _error(error);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void RegisterNotify(uint id, Action<MessageContent> action) { }
        public void SendOneWay(uint id, IMessage request) { }
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T>
        {
            Calls.Add(id);
            LastRequest = request;
            _pending = message => success((T)message);
            _error = error;
        }
    }
}
