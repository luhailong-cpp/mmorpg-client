using System.Collections.Generic;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="PetClient"/> 状态机用例(不经 Attach,避免污染单例)。
    ///
    /// 覆盖的是"客户端不能自作主张"这条契约:列表只由服务器给,写请求单飞,
    /// 断线作废,自动加点只回建议不改列表。与 <see cref="AttributeClientTests"/> 同构 ——
    /// 两边任何一边的口径改了,另一边的同名用例应该跟着改。
    /// </summary>
    public sealed class PetClientTests
    {
        private FakeBattleTransport _net;
        private PetClient _client;

        private const ulong PetA = 900001UL;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport();
            _client = new PetClient(_net);
        }

        private static PetListInfo MakeList(uint remaining = 5, uint allocated = 0, ulong activePetId = 0)
        {
            var list = new PetListInfo { ActivePetId = activePetId, MaxPets = 10 };
            var pet = new PetInfo
            {
                PetId = PetA,
                PetTableId = 1,
                Name = "灵狐",
                Level = 10,
                PoolId = 4,
                TotalPoints = remaining + allocated,
                RemainingPoints = remaining,
                Growth = 10500,
                IsActive = activePetId == PetA,
            };
            pet.Dimensions.Add(new PetDimensionInfo
            {
                DimensionId = 401, PoolId = 4, Name = "体质", Desc = "体质:提高宝宝气血上限和防御。",
                Allocated = allocated, Value = 10 + allocated, Aptitude = 11000,
            });
            pet.Derived = new PetDerivedInfo { MaxHealth = 400, Health = 400, Defense = 12 };
            list.Pets.Add(pet);
            return list;
        }

        [Test]
        public void RequestListAppliesServerList()
        {
            PetListInfo received = null;
            _client.OnList += l => received = l;

            _client.RequestList();
            var call = _net.CallsOf(MessageIds.GetPetList);
            Assert.That(call, Has.Count.EqualTo(1));

            call[0].Respond(new GetPetListResponse { Pets = MakeList() });

            Assert.That(received, Is.Not.Null);
            Assert.That(_client.HasList, Is.True);
            Assert.That(_client.Pets.Pets[0].RemainingPoints, Is.EqualTo(5u));
        }

        [Test]
        public void AllocateSendsTargetValuesAndAppliesReturnedList()
        {
            _client.Allocate(PetA, new Dictionary<uint, uint> { { 401, 3 } });

            var call = _net.CallsOf(MessageIds.AllocatePetPoints);
            Assert.That(call, Has.Count.EqualTo(1));
            var request = (AllocatePetPointsRequest)call[0].Request;
            Assert.That(request.PetId, Is.EqualTo(PetA));
            // 目标值语义:发的是"最终应为 3",不是"+3"
            Assert.That(request.Allocated[401], Is.EqualTo(3u));
            Assert.That(_client.Busy, Is.True);

            call[0].Respond(new AllocatePetPointsResponse { Pets = MakeList(2, 3) });

            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Pets.Pets[0].Dimensions[0].Allocated, Is.EqualTo(3u));
        }

        [Test]
        public void SecondWriteIsRejectedWhileFirstIsInFlight()
        {
            string error = null;
            _client.OnError += e => error = e;

            _client.Allocate(PetA, new Dictionary<uint, uint> { { 401, 1 } });
            _client.ResetPoints(PetA);

            Assert.That(_net.CallsOf(MessageIds.ResetPetPoints), Is.Empty);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void EmptyAllocationIsRejectedLocallyWithoutNetworkCall()
        {
            _client.Allocate(PetA, new Dictionary<uint, uint>());

            Assert.That(_net.CallsOf(MessageIds.AllocatePetPoints), Is.Empty);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void AutoAllocateReturnsSuggestionWithoutTouchingListOrBusy()
        {
            _client.RequestList();
            _net.CallsOf(MessageIds.GetPetList)[0].Respond(new GetPetListResponse { Pets = MakeList() });

            ulong suggestedPet = 0;
            IReadOnlyDictionary<uint, uint> suggestion = null;
            _client.OnAutoSuggestion += (petId, map) => { suggestedPet = petId; suggestion = map; };

            _client.AutoAllocate(PetA);
            Assert.That(_client.Busy, Is.False, "自动加点只算不落,不该占用写请求名额");

            var response = new AutoAllocatePetPointsResponse { PetId = PetA };
            response.Suggested[401] = 5;
            _net.CallsOf(MessageIds.AutoAllocatePetPoints)[0].Respond(response);

            Assert.That(suggestedPet, Is.EqualTo(PetA));
            Assert.That(suggestion[401], Is.EqualTo(5u));
            // 建议不是权威:列表仍是服务器上次给的那份
            Assert.That(_client.Pets.Pets[0].Dimensions[0].Allocated, Is.EqualTo(0u));
        }

        [Test]
        public void SummonAndRecallApplyReturnedList()
        {
            _client.Summon(PetA);
            var summon = _net.CallsOf(MessageIds.SummonPet);
            Assert.That(summon, Has.Count.EqualTo(1));
            Assert.That(((SummonPetRequest)summon[0].Request).PetId, Is.EqualTo(PetA));
            summon[0].Respond(new SummonPetResponse { Pets = MakeList(5, 0, PetA) });
            Assert.That(_client.Pets.ActivePetId, Is.EqualTo(PetA));
            Assert.That(_client.Pets.Pets[0].IsActive, Is.True);

            _client.Recall();
            var recall = _net.CallsOf(MessageIds.RecallPet);
            Assert.That(recall, Has.Count.EqualTo(1));
            recall[0].Respond(new RecallPetResponse { Pets = MakeList() });
            Assert.That(_client.Pets.ActivePetId, Is.EqualTo(0UL));
        }

        [Test]
        public void ServerPushReplacesList()
        {
            _client.RequestList();
            _net.CallsOf(MessageIds.GetPetList)[0].Respond(new GetPetListResponse { Pets = MakeList() });

            _net.PushNotify(MessageIds.NotifyPetListChanged,
                new PetListChangedS2C { Pets = MakeList(9, 1) });

            Assert.That(_client.Pets.Pets[0].RemainingPoints, Is.EqualTo(9u));
            Assert.That(_client.Pets.Pets[0].Dimensions[0].Allocated, Is.EqualTo(1u));
        }

        [Test]
        public void DisconnectDropsListAndInFlightWrite()
        {
            _client.RequestList();
            _net.CallsOf(MessageIds.GetPetList)[0].Respond(new GetPetListResponse { Pets = MakeList() });
            _client.Allocate(PetA, new Dictionary<uint, uint> { { 401, 1 } });
            Assert.That(_client.Busy, Is.True);

            _net.RaiseDisconnected();

            Assert.That(_client.HasList, Is.False);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void TipInResponseBodyIsRejectionKeepsListAndClearsBusy()
        {
            // 服务端拒绝码放在响应体 error_message(信封 error_message 为空 → 走 onResponse 而不是 onError)
            _client.RequestList();
            _net.CallsOf(MessageIds.GetPetList)[0].Respond(new GetPetListResponse { Pets = MakeList() });
            string error = null;
            _client.OnError += e => error = e;

            _client.Allocate(PetA, new Dictionary<uint, uint> { { 401, 3 } });
            _net.CallsOf(MessageIds.AllocatePetPoints)[0].Respond(
                new AllocatePetPointsResponse { ErrorMessage = new TipInfoMessage { Id = 26010 } });

            Assert.That(_client.Busy, Is.False);
            Assert.That(error, Does.Contain("不能减少"));
            // 旧列表保留,没有被 null 列表冲掉
            Assert.That(_client.HasList, Is.True);
            Assert.That(_client.Pets.Pets[0].Dimensions[0].Allocated, Is.EqualTo(0u));
        }

        [Test]
        public void SummonTipInBodySurfacesPetErrorText()
        {
            string error = null;
            _client.OnError += e => error = e;

            _client.Summon(PetA);
            _net.CallsOf(MessageIds.SummonPet)[0].Respond(
                new SummonPetResponse { ErrorMessage = new TipInfoMessage { Id = 26004 } });

            Assert.That(_client.Busy, Is.False);
            Assert.That(error, Does.Contain("已经在出战中"));
        }

        [Test]
        public void NotReadyTransportRejectsBeforeSending()
        {
            _net.IsReady = false;
            string error = null;
            _client.OnError += e => error = e;

            _client.RequestList();
            _client.Allocate(PetA, new Dictionary<uint, uint> { { 401, 1 } });

            Assert.That(_net.Calls, Is.Empty);
            Assert.That(error, Is.Not.Null);
        }
    }
}
