using System;
using System.Collections.Generic;
using System.Reflection;
using Google.Protobuf;
using MmorpgClient.App;
using Loginpb;
using MmorpgClient.Game;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using NUnit.Framework;
using Teampb;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class PersistedAppearanceIdentityTests
    {
        private const string First = "04_mountain_guardian_boy";
        private const string Second = "05_celestial_musician_girl";
        private GameClient _client;

        [SetUp]
        public void SetUp() => _client = new GameClient("http://127.0.0.1:1");

        [TearDown]
        public void TearDown()
        {
            _client.Disconnect();
            if (_client.World.Root != null) UnityEngine.Object.DestroyImmediate(_client.World.Root.gameObject);
        }

        [Test]
        public void SameProfessionAndGender_PreserveDistinctSavedAppearancesAcrossRelogAndEntityChanges()
        {
            var first = new AccountSimplePlayer { PlayerId = 101, ClassId = 1, Gender = 1, AppearanceId = First };
            var second = new AccountSimplePlayer { PlayerId = 102, ClassId = 1, Gender = 1, AppearanceId = Second };
            // This round-trip is the client wire contract. The server persistence test owns DB proof.
            first = AccountSimplePlayer.Parser.ParseFrom(first.ToByteArray());
            second = AccountSimplePlayer.Parser.ParseFrom(second.ToByteArray());
            Cache(first, second);
            Spawn(7, 101);
            Spawn(8, 102);
            Assert.That(_client.World.Actors[7].CharacterId, Is.EqualTo(First));
            Assert.That(_client.World.Actors[8].CharacterId, Is.EqualTo(Second));
            _client.Disconnect();
            Assert.That(_client.ResolveCharacterId(101), Is.Null);
            Cache(first, second);
            Spawn(90, 101);
            Spawn(91, 102);
            Assert.That(_client.World.Actors[90].CharacterId, Is.EqualTo(First));
            Assert.That(_client.World.Actors[91].CharacterId, Is.EqualTo(Second));
        }

        [Test]
        public void CreateRequest_SeparatesPersistedAppearanceFromGameplayFields()
        {
            var request = CreatePlayerRequest.Parser.ParseFrom(new CreatePlayerRequest
                { ClassId = 4, Gender = 2, AppearanceId = First }.ToByteArray());
            Assert.That(request.AppearanceId, Is.EqualTo(First));
            Assert.That(request.ClassId, Is.EqualTo(4u));
            Assert.That(request.Gender, Is.EqualTo(2u));
            Assert.That(QdaoCharacterCatalog.ResolveRole(4, 2, request.AppearanceId), Is.EqualTo(First));
            Assert.That(QdaoCharacterCatalog.ResolveRole(4, 2, ""), Is.EqualTo("26_osmanthus_healer"));
        }

        [Test]
        public void RemoteAoiIdentity_DoesNotNeedTheLocalAccountRoleList_AndClearsOnDisconnect()
        {
            Spawn(501, 991, First);
            Spawn(502, 992, Second);
            Assert.That(_client.ResolveCharacterId(991), Is.EqualTo(First));
            Assert.That(_client.World.Actors[502].CharacterId, Is.EqualTo(Second));
            _client.Disconnect();
            Assert.That(_client.ResolveCharacterId(991), Is.Null);
        }

        [Test]
        public void RemoteLegacyAoi_UsesTransmittedProfessionAndGender()
        {
            Invoke("SpawnActorView", new ActorCreateS2C
                { Entity = 50, Guid = 990, ActorType = ActorType.Player, ClassId = 3, Gender = 2 });
            Assert.That(_client.World.Actors[50].CharacterId, Is.EqualTo("29_he_xiangu"));
        }

        [Test]
        public void AoiRestoredIdentity_OverridesTheEmptyRoleListReturnedBeforeEnterGameSelfHeal()
        {
            Cache(new AccountSimplePlayer { PlayerId = 101, ClassId = 1, Gender = 1 });
            Assert.That(_client.ResolveCharacterId(101), Is.EqualTo("24_lu_dongbin"));
            Spawn(50, 101, First);
            Assert.That(_client.ResolveCharacterId(101), Is.EqualTo(First));
            Assert.That(_client.World.Actors[50].CharacterId, Is.EqualTo(First));
        }

        [Test]
        public void MissingResources_DoNotChangeThePersistedIdentity()
        {
            const string unsupported = "future_unavailable_identity";
            Cache(new AccountSimplePlayer { PlayerId = 101, ClassId = 1, Gender = 1, AppearanceId = unsupported });
            Spawn(7, 101);
            Assert.That(_client.World.Actors[7].CharacterId, Is.EqualTo(unsupported));
            Assert.That(QdaoBoySpriteAnimator.TryAttach(_client.World.Actors[7].Go, unsupported), Is.False);
            Assert.That(_client.World.Actors[7].Go.GetComponent<QdaoBoySpriteAnimator>(), Is.Null);
        }

        [Test]
        public void BattleSnapshotIdentity_WinsOverLocalCache_WithoutUsingPetOwnerIdentity()
        {
            var player = BattleActorState.Parser.ParseFrom(new BattleActorState
                { ActorId = 991, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = Second }.ToByteArray());
            Assert.That(BattleArtCatalog.CharacterIdFor(player, _ => First), Is.EqualTo(Second));
            Assert.That(BattleArtCatalog.CharacterIdFor(player), Is.EqualTo(Second));
            player.AppearanceId = "";
            player.ClassId = 3;
            player.Gender = 2;
            Assert.That(BattleArtCatalog.CharacterIdFor(player, _ => First), Is.EqualTo("29_he_xiangu"));
            var pet = new BattleActorState { ActorId = 992, ActorType = eBattleActorType.BattleActorTypePet, OwnerPlayerId = 991 };
            Assert.That(BattleArtCatalog.CharacterIdFor(pet, _ => throw new Exception("Pet must not resolve its owner")),
                Is.EqualTo(BattleArtCatalog.CharacterIdFor(pet)));
        }

        [Test]
        public void TeamSnapshotAndApplications_UseTheSameIdentityAsWorldAndBattle()
        {
            var view = new TeamView { TeamId = 8, LeaderId = 101, Capacity = 5 };
            view.Members.Add(new TeamMemberView { PlayerId = 101, ClassId = 1, Gender = 1, AppearanceId = First });
            view.Applications.Add(new TeamApplicationView
                { Player = new TeamMemberView { PlayerId = 102, ClassId = 1, Gender = 1, AppearanceId = Second } });
            var snapshot = TeamAppearanceTransport.ToSnapshot(TeamView.Parser.ParseFrom(view.ToByteArray()), 101);
            Assert.That(snapshot.Members[0].CharacterId, Is.EqualTo(First));
            Assert.That(snapshot.Applications[0].CharacterId, Is.EqualTo(Second));
            Assert.That(snapshot.LocalPlayerId, Is.EqualTo(101UL));
            var state = new TeamUiState();
            state.SetSnapshot(snapshot);
            snapshot.Members[0].CharacterId = Second;
            Assert.That(state.Snapshot.Members[0].CharacterId, Is.EqualTo(First));
        }

        [Test]
        public void LateTeamReplies_CannotRestoreThePreviousMembershipOrAppearance()
        {
            var current = new TeamView { MembershipEpoch = 7, Version = 3, ServerTimeMs = 100 };
            Assert.That(TeamAppearanceTransport.IsAtLeastAsRecent(
                new TeamView { MembershipEpoch = 6, Version = 99, ServerTimeMs = 101 }, current), Is.False);
            Assert.That(TeamAppearanceTransport.IsAtLeastAsRecent(
                new TeamView { MembershipEpoch = 7, Version = 2, ServerTimeMs = 101 }, current), Is.False);
            Assert.That(TeamAppearanceTransport.IsAtLeastAsRecent(
                new TeamView { MembershipEpoch = 7, Version = 3, ServerTimeMs = 99 }, current), Is.False);
            Assert.That(TeamAppearanceTransport.IsAtLeastAsRecent(
                new TeamView { MembershipEpoch = 8, Version = 1, ServerTimeMs = 101 }, current), Is.True);
        }

        [Test]
        public void AvailableOriginals_NeverReintroduceDeletedIds_EvenIfHistoricalResourcesExist()
        {
            var visited = new HashSet<string>();
            var result = QdaoCharacterCatalog.SelectAvailableAppearances(entry => { visited.Add(entry.Id); return null; });
            Assert.That(result.Count, Is.EqualTo(8), "Incomplete originals cannot appear in selection.");
            Assert.That(visited.Count, Is.EqualTo(15));
            Assert.That(QdaoCharacterCatalog.RetainedOriginalAll.Count, Is.EqualTo(15));
            foreach (var id in visited) Assert.That(QdaoCharacterCatalog.IsRetainedOriginal(id), Is.True);
            Assert.That(visited, Does.Not.Contain("11_jade_fist_flat_top_boy"));
            Assert.That(visited, Does.Contain("20_star_formation_master_girl"));
        }

        [Test]
        public void AutoPilotOptions_ParseAppearanceAndStrictRelogWithoutChangingDefaults()
        {
            var parsed = DevAutoPilot.Parse(new[] { "-appearanceId=" + First, "-roleClass", "4",
                "-roleGender=2", "-requireAppearanceRole", "-appearanceUi" }, false);
            Assert.That(parsed.AppearanceId, Is.EqualTo(First));
            Assert.That(parsed.RoleClass, Is.EqualTo(4u));
            Assert.That(parsed.RoleGender, Is.EqualTo(2u));
            Assert.That(parsed.RequireAppearanceRole, Is.True);
            Assert.That(parsed.AppearanceUi, Is.True);
            Assert.That(DevAutoPilot.Parse(Array.Empty<string>(), false).AppearanceUi, Is.False);
            Assert.That(DevAutoPilot.Parse(Array.Empty<string>(), false).AppearanceId, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StrictRelog_OnlySelectsThePersistedAppearance_AndNeverCreatesAReplacement(bool existing)
        {
            const string accepted = "00_reference_topright_boy";
            Assert.That(QdaoCharacterCatalog.Find(accepted).ResolveAppearance(), Is.Not.Null,
                "This integration test requires the actual accepted resource package.");
            var host = new GameObject("StrictAppearanceChooserTest");
            host.SetActive(false); // Do not run the automatic network startup.
            try
            {
                var pilot = host.AddComponent<DevAutoPilot>();
                typeof(DevAutoPilot).GetField("_opt", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(pilot,
                    new DevAutoPilot.Options { AppearanceId = accepted, RequireAppearanceRole = true });
                var roles = new List<AccountSimplePlayer>
                {
                    new() { PlayerId = 101, ClassId = 1, Gender = 1, AppearanceId = existing ? accepted : First },
                };
                var choice = new GameClient.PlayerChoice();
                var routine = (System.Collections.IEnumerator)typeof(DevAutoPilot)
                    .GetMethod("ChooseAppearance", BindingFlags.NonPublic | BindingFlags.Instance)?
                    .Invoke(pilot, new object[] { 1u, roles, choice });
                if (!existing) LogAssert.Expect(LogType.Error, new Regex("RESULT=FAIL stage=appearance_select.*禁止自动创建替代角色"));
                Assert.That(routine, Is.Not.Null);
                Assert.That(routine.MoveNext(), Is.False);
                Assert.That(choice.CreateNew, Is.False);
                Assert.That(choice.Cancelled, Is.EqualTo(!existing));
                Assert.That(choice.SelectedPlayerId, Is.EqualTo(existing ? 101UL : 0UL));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void BattleWalking_UsesDistanceAndTheWorldReferenceCadence_IndependentOfDisplayScale()
        {
            float fps = 1000f / 30f;
            float distanceForCycle = QdaoBoySpriteAnimator.ReferenceRunSpeed * .48f;
            float pixels = distanceForCycle / QdaoBoySpriteAnimator.FrameWorldHeight * BattleUnitView.PlayerHeight;
            Assert.That(BattleUnitView.WalkFrameAdvance(pixels, 1f, fps), Is.EqualTo(16f).Within(.001f));
            Assert.That(BattleUnitView.WalkFrameAdvance(pixels * .75f, .75f, fps), Is.EqualTo(16f).Within(.001f));
            Assert.That(BattleUnitView.WalkFrameAdvance(0f, 1f, fps), Is.Zero);
        }

        private void Cache(params AccountSimplePlayer[] roles)
        {
            var wrapped = new AccountSimplePlayerWrapper[roles.Length];
            for (int i = 0; i < roles.Length; i++) wrapped[i] = new AccountSimplePlayerWrapper { Player = roles[i] };
            Invoke("CacheRoleMetadata", wrapped);
        }

        private void Spawn(ulong entity, ulong playerId, string appearance = "")
            => Invoke("SpawnActorView", new ActorCreateS2C
                { Entity = entity, Guid = playerId, ActorType = ActorType.Player, AppearanceId = appearance });

        private void Invoke(string method, object argument)
            => typeof(GameClient).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_client, new[] { argument });
    }
}
