using System.Linq;
using System.Reflection;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Pet;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>通过原生按钮驱动加点草稿，验证会刷新服务器列表的操作不能静默丢弃草稿。</summary>
    public sealed class PetPanelInteractionTests
    {
        private const ulong PetId = 900001UL;
        private GameObject _uiRoot;
        private FakeBattleTransport _net;
        private PetClient _client;
        private PetPanel _panel;
        private PetListInfo _list;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport();
            _client = new PetClient(_net);
            _uiRoot = new GameObject("PetPanelInteractionTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_uiRoot.transform).sizeDelta = new Vector2(2560f, 1080f);

            // 非激活宿主不会执行单例 Awake/Update；仅注入已有客户端测试缝。
            var ownerGo = new GameObject("PetOwner");
            ownerGo.SetActive(false);
            ownerGo.transform.SetParent(_uiRoot.transform, false);
            var owner = ownerGo.AddComponent<PetUiRoot>();
            owner.enabled = false;
            typeof(PetUiRoot).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(owner, _client);
            _panel = new PetPanel(owner, _uiRoot.transform);
            _client.OnList += _panel.ApplyList;

            _list = MakeList();
            _panel.Show();
            _net.CallsOf(MessageIds.GetPetList).Single().Respond(new GetPetListResponse { Pets = _list });
            _net.Calls.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_client != null && _panel != null) _client.OnList -= _panel.ApplyList;
            if (_uiRoot != null) Object.DestroyImmediate(_uiRoot);
        }

        [Test]
        public void PendingAllocationBlocksRenameUntilLocalDraftIsReset()
        {
            Click("Row0Plus");
            AssertDraftPointIsVisible();
            Click("CultivationHelp");
            _uiRoot.GetComponentsInChildren<TMP_InputField>(true)
                .Single(input => input.name == "RenameInput").text = "新灵狐";

            Click("RenameConfirm");

            Assert.That(_net.Calls, Is.Empty, "未确认加点时不应发送改名，回包会覆盖草稿");
            Assert.That(_client.Busy, Is.False);
            Assert.That(Text("Status").text, Does.Contain("未确认的加点"));
            AssertDraftPointIsVisible();

            // 重置只回退本次草稿，之后同一个改名按钮仍能提交真实请求。
            Click("DetailsClose");
            Click("Discard");
            Click("CultivationHelp");
            _uiRoot.GetComponentsInChildren<TMP_InputField>(true)
                .Single(input => input.name == "RenameInput").text = "新灵狐";
            Click("RenameConfirm");
            var rename = _net.CallsOf(MessageIds.RenamePet).Single();
            Assert.That(((RenamePetRequest)rename.Request).PetId, Is.EqualTo(PetId));
            Assert.That(((RenamePetRequest)rename.Request).Name, Is.EqualTo("新灵狐"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingAllocationBlocksSummonOrRecallUntilLocalDraftIsReset(bool active)
        {
            _list.ActivePetId = active ? PetId : 0UL;
            _list.Pets[0].IsActive = active;
            _panel.ApplyList(_list);
            Click("Row0Plus");

            Click("Summon");

            Assert.That(_net.Calls, Is.Empty, "出战与召回回包都不能静默覆盖未确认草稿");
            Assert.That(_client.Busy, Is.False);
            Assert.That(Text("Status").text, Does.Contain("未确认的加点"));
            AssertDraftPointIsVisible();

            Click("Discard");
            Click("Summon");
            Assert.That(_net.Calls, Has.Count.EqualTo(1));
            Assert.That(_net.Calls[0].MessageId,
                Is.EqualTo(active ? MessageIds.RecallPet : MessageIds.SummonPet));
        }

        [Test]
        public void LocalResetRestoresCommittedValuesWithoutSendingAnyRequest()
        {
            Click("Row0Plus");
            Click("Row0Plus");
            Assert.That(Text("Remain").text, Does.Contain("3"));
            Assert.That(Button("Row0Minus").interactable, Is.True);
            Assert.That(Button("Confirm").interactable, Is.True);

            Click("Discard");

            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_net.OneWays, Is.Empty);
            Assert.That(_client.Busy, Is.False);
            Assert.That(Text("Row0Value").text, Is.EqualTo("10"));
            Assert.That(Text("Remain").text, Is.EqualTo("未分配　5"));
            Assert.That(Button("Row0Minus").interactable, Is.False);
            Assert.That(Button("Confirm").interactable, Is.False);
            Assert.That(Button("Discard").interactable, Is.False);
            Assert.That(_client.Pets.Pets[0].Dimensions[0].Allocated, Is.EqualTo(0u));
            Assert.That(_client.Pets.Pets[0].RemainingPoints, Is.EqualTo(5u));
        }

        private void AssertDraftPointIsVisible()
        {
            Assert.That(Text("Row0Value").text, Does.Contain("+1"));
            Assert.That(Text("Remain").text, Does.Contain("未分配　4"));
            Assert.That(Button("Confirm").interactable, Is.True);
        }

        private void Click(string name)
        {
            var button = Button(name);
            Assert.That(button.gameObject.activeInHierarchy, Is.True, name + "应可见");
            Assert.That(button.interactable, Is.True, name + "应可点击");
            button.onClick.Invoke();
        }

        private Button Button(string name)
            => _uiRoot.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == name + "Plate");

        private TMP_Text Text(string name)
            => _uiRoot.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == name);

        private static PetListInfo MakeList()
        {
            var pet = new PetInfo
            {
                PetId = PetId, PetTableId = 1, ModelId = 1001, Name = "灵狐", Level = 10,
                Growth = 10000, RemainingPoints = 5, TotalPoints = 5,
                Derived = new PetDerivedInfo { Health = 400, MaxHealth = 400, Defense = 12 }
            };
            pet.Dimensions.Add(new PetDimensionInfo
            {
                DimensionId = 401, Name = "体质", Value = 10, Allocated = 0, Cap = 100,
                Aptitude = 10000, Desc = "提高宝宝气血上限和防御。"
            });
            var list = new PetListInfo { MaxPets = 8 };
            list.Pets.Add(pet);
            return list;
        }
    }
}
