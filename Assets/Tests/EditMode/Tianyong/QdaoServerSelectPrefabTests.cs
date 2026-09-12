using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Net;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Ugui
{
    using Transform = UnityEngine.Transform;

    public sealed class QdaoServerSelectPrefabTests
    {
        private const string PrefabPath = "UI/Ugui/Prefabs/QdaoServerSelect";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _instance;
        private GameObject _appHost;
        private QdaoServerSelectView _view;
        private SessionModel _session;

        [SetUp]
        public void SetUp()
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing Resources/{PrefabPath}.prefab");
            _instance = Object.Instantiate(prefab);
            _view = _instance.GetComponent<QdaoServerSelectView>();
            Assert.That(_view, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
            if (_appHost != null) Object.DestroyImmediate(_appHost);
        }

        [Test]
        public void ProductionPrefab_HasNativeLoginServerAndCredentialControlsWithIndependentArt()
        {
            AssertSerializedPrefabReferences();
            Assert.That(Invoke("ReferencesValid"), Is.True);
            Assert.That(_instance.transform.Find("ReferenceArtwork"), Is.Null);
            Assert.That(_instance.transform.Find("ScreenBaseHeadband"), Is.Null);

            foreach (var name in new[] { "ScreenArtwork", "Hero", "ServerFrame", "CredentialArt" })
            {
                var image = Required<Image>(name);
                Assert.That(image.sprite, Is.Not.Null, name);
                Assert.That(image.enabled && image.color.a > 0.01f, Is.True, name);
                StringAssert.StartsWith("Assets/Resources/UI/Ugui/RefreshV8/", AssetDatabase.GetAssetPath(image.sprite), name);
            }
            foreach (var name in new[] { "SelectServer", "EnterGame", "SwitchAccount", "TopTab_0", "CategoryArt_0",
                         "ServerCardArt_0", "BackButton", "RefreshButton", "EnterButton",
                         "CredentialCancelButton", "CredentialSubmitButton" })
            {
                var button = Required<Button>(name);
                var image = button.GetComponent<Image>();
                Assert.That(image.sprite, Is.Not.Null, name);
                Assert.That(button.targetGraphic, Is.SameAs(image), name);
                Assert.That(image.raycastTarget, Is.True, name);
            }
            Assert.That(_instance.GetComponentsInChildren<Button>(true)
                .All(button => button.targetGraphic is Image image && image.raycastTarget), Is.True,
                "Each semantic action must retain a raycastable Image target.");
            Assert.That(Required<Image>("ScreenArtwork").raycastTarget, Is.False);
            Assert.That(Required<Image>("CredentialBlocker").raycastTarget, Is.True);
            var backdrop = Required<Image>("LetterboxBackdrop");
            Assert.That(backdrop.sprite, Is.Null);
            Assert.That(backdrop.raycastTarget, Is.False);
            Assert.That(backdrop.color, Is.EqualTo(QdaoUguiTheme.Letterbox));
            Assert.That(backdrop.rectTransform.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(backdrop.rectTransform.anchorMax, Is.EqualTo(Vector2.one));

            var rootRect = _instance.GetComponent<RectTransform>();
            Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one));
            var content = Required<RectTransform>("ContentRoot");
            Assert.That(content.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(content.anchorMax, Is.EqualTo(content.anchorMin));
            Assert.That(content.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(content.sizeDelta, Is.EqualTo(new Vector2(2560f, 1080f)));

            var originalChildren = _instance.GetComponentsInChildren<Transform>(true);
            _view.PrepareForPreview();
            Assert.That(_instance.GetComponentsInChildren<Transform>(true), Is.EquivalentTo(originalChildren),
                "Preparing a serialized prefab must not replace its native hierarchy.");
            Assert.That(Required<TMP_Text>("ServerStatus_0").text, Does.Contain("流畅"),
                "Availability must be live readable text, not baked into server art.");
            Assert.That(Required<TMP_InputField>("AccountInput").characterLimit, Is.EqualTo(191));
            var password = Required<TMP_InputField>("PasswordInput");
            Assert.That(password.characterLimit, Is.EqualTo(1024));
            Assert.That(password.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
            Assert.That(password.gameObject.activeInHierarchy, Is.False);
            foreach (var dot in _instance.GetComponentsInChildren<Image>(true).Where(image => image.name.StartsWith("ServerDot_")))
                Assert.That(dot.sprite, Is.Not.Null, dot.name);
        }

        [Test]
        public void LoginNavigation_OpensServerPageAndAccountModalAndReturnsWithoutEntering()
        {
            PrepareDisconnectedSession();
            _view.ShowLanding(true);
            Click("SelectServer");
            Assert.That(Required<RectTransform>("ServerPage").gameObject.activeSelf, Is.True);
            Assert.That(Required<RectTransform>("LoginPage").gameObject.activeSelf, Is.False);
            Click("BackButton");
            Assert.That(Required<RectTransform>("LoginPage").gameObject.activeSelf, Is.True);
            Assert.That(Required<RectTransform>("ServerPage").gameObject.activeSelf, Is.False);
            Assert.That(Required<RectTransform>("CredentialPanel").gameObject.activeSelf, Is.False);
            Click("SwitchAccount");
            Assert.That(Required<TMP_InputField>("AccountInput").gameObject.activeInHierarchy, Is.True);
            Click("CredentialCancelButton");
            AssertNoEnterOrModal();
            Assert.That(Required<RectTransform>("LoginPage").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void EmptySearch_WithPreviouslySelectedSessionZone_DoesNotEnterOrAskForCredentials()
        {
            PrepareDisconnectedSession();
            Required<TMP_InputField>("SearchInput").text = "__no_matching_zone__";
            Assert.That(_session.SelectedZone, Is.Not.Null, "Reproduce a selection retained in the session after filtering.");
            Assert.That(Get<List<ServerListZone>>("_filtered"), Is.Empty);
            Assert.That(Required<Button>("EnterButton").interactable, Is.False);
            Assert.That(Required<TMP_Text>("EmptyServers").gameObject.activeInHierarchy, Is.True);
            Assert.That(_instance.GetComponentsInChildren<Button>(true)
                .Where(button => button.name.StartsWith("ServerCardArt_"))
                .All(button => !button.gameObject.activeSelf), Is.True);

            // Invoke the bound handler even though the button is disabled: stale input must be guarded too.
            Click("EnterButton");
            AssertNoEnterOrModal();
            _view.ShowLanding(true);
            Click("EnterGame");
            AssertNoEnterOrModal();
            Assert.That(Required<RectTransform>("ServerPage").gameObject.activeSelf, Is.True,
                "The landing action should direct an empty selection back to the server picker.");
            Assert.That(Required<TMP_Text>("StatusText").text, Does.Contain("选择区服"));
        }

        [Test]
        public void MaintenanceSelection_DisablesBothEntryButtonsAndRejectsStaleClicks()
        {
            PrepareDisconnectedSession();
            Click("ServerCardArt_1");
            Assert.That(_session.SelectedZoneId, Is.EqualTo(2u));
            Assert.That(Required<TMP_Text>("ServerStatus_1").text, Does.Contain("维护"));
            foreach (var name in new[] { "EnterButton", "EnterGame" })
            {
                Assert.That(Required<Button>(name).interactable, Is.False, name);
                Click(name);
                AssertNoEnterOrModal();
            }
            Assert.That(Required<TMP_Text>("StatusText").text, Does.Contain("维护"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Loading_EnterHandlerDoesNotOpenCredentialsOrStartEntry(bool completeCredentials)
        {
            PrepareDisconnectedSession();
            if (completeCredentials)
            {
                Required<TMP_InputField>("AccountInput").SetTextWithoutNotify("edited-account");
                Required<TMP_InputField>("PasswordInput").SetTextWithoutNotify("edited-password");
            }
            Set("_serverListLoading", true);
            Invoke("RefreshVisualState");
            Assert.That(Required<Button>("EnterButton").interactable, Is.False);
            Assert.That(Required<Button>("EnterGame").interactable, Is.False);
            Invoke("OnEnterClicked");
            AssertNoEnterOrModal();
            Assert.That(Required<TMP_Text>("StatusText").text, Does.Contain("获取区服列表"));
            Click("EnterGame");
            AssertNoEnterOrModal();
        }

        [Test]
        public void BusyEntry_FreezesLoginServerAndCredentialActions()
        {
            PrepareDisconnectedSession();
            Set("_busy", true);
            Invoke("RefreshVisualState");
            Assert.That(_instance.GetComponentsInChildren<Button>(true).All(button => !button.interactable), Is.True);
            Assert.That(_instance.GetComponentsInChildren<TMP_InputField>(true).All(input => !input.interactable), Is.True);
        }

        [TestCase("_landingRoot")]
        [TestCase("_serverRoot")]
        [TestCase("_landingServers")]
        [TestCase("_landingEnter")]
        [TestCase("_landingAccount")]
        [TestCase("_landingServerText")]
        [TestCase("_landingStatusText")]
        [TestCase("_emptyListText")]
        [TestCase("_serverBadges")]
        public void IncompleteLandingPrefab_IsRejectedBeforeRuntimeUse(string field)
        {
            Assert.That(Invoke("ReferencesValid"), Is.True, "The original prefab must be complete before fault injection.");
            Set(field, null);
            Assert.That(Invoke("ReferencesValid"), Is.False, $"An old prefab missing {field} must require rebuilding.");
        }

        private void PrepareDisconnectedSession()
        {
            _appHost = new GameObject("[QdaoServerSelectTests Offline Session]");
            // Never activate this host or call Initialize: the real bootstrap must not create clients or run coroutines.
            _appHost.SetActive(false);
            var app = _appHost.AddComponent<AppBootstrap>();
            _session = new SessionModel { Account = "original-account", Password = "original-password", SelectedZoneId = 1, SelectedZoneIndex = 0 };
            _session.RecentZoneIds.Clear();
            _session.Zones.Add(new ServerListZone { zone_id = 1, name = "青云一服", status = "OPEN", load_level = "SMOOTH" });
            _session.Zones.Add(new ServerListZone { zone_id = 2, name = "昆仑二服", status = "MAINTENANCE", maintenance_msg = "停服维护中" });
            typeof(AppBootstrap).GetProperty(nameof(AppBootstrap.Session)).GetSetMethod(true).Invoke(app, new object[] { _session });
            Set("_app", app);
            Set("_selectedZoneId", 1u);
            Invoke("BindEvents");
            _view.PrepareForPreview();
            Required<TMP_InputField>("AccountInput").SetTextWithoutNotify(string.Empty);
            Required<TMP_InputField>("PasswordInput").SetTextWithoutNotify(string.Empty);
            Assert.That(app.GameClient, Is.Null, "These regression tests must remain disconnected.");
        }

        private void AssertNoEnterOrModal()
        {
            Assert.That(Required<RectTransform>("CredentialPanel").gameObject.activeSelf, Is.False);
            Assert.That(Get<bool>("_busy"), Is.False);
            Assert.That(Get<int>("_enterRunId"), Is.Zero, "A rejected entry must not start the enter pipeline.");
            Assert.That(_instance.activeSelf, Is.True);
            Assert.That(_session.Account, Is.EqualTo("original-account"));
            Assert.That(_session.Password, Is.EqualTo("original-password"));
            Assert.That(_session.RecentZoneIds, Is.Empty);
        }

        private void AssertSerializedPrefabReferences()
        {
            var serialized = new SerializedObject(_view);
            foreach (var field in typeof(QdaoServerSelectView).GetFields(PrivateInstance)
                         .Where(field => field.IsDefined(typeof(SerializeField), false)))
            {
                var property = serialized.FindProperty(field.Name);
                Assert.That(property, Is.Not.Null, field.Name);
                if (property.isArray)
                {
                    Assert.That(property.arraySize, Is.GreaterThan(0), field.Name);
                    for (var i = 0; i < property.arraySize; i++)
                        Assert.That(property.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"{field.Name}[{i}]");
                }
                else
                    Assert.That(property.objectReferenceValue, Is.Not.Null, $"Prefab did not serialize {field.Name}.");
            }
        }

        private T Required<T>(string name) where T : Component
        {
            var component = _instance.GetComponentsInChildren<T>(true).SingleOrDefault(value => value.name == name);
            Assert.That(component, Is.Not.Null, $"Missing {typeof(T).Name} {name}");
            return component;
        }

        private void Click(string name) => Required<Button>(name).onClick.Invoke();
        private T Get<T>(string field) => (T)RequiredField(field).GetValue(_view);
        private void Set(string field, object value) => RequiredField(field).SetValue(_view, value);
        private static FieldInfo RequiredField(string field)
        {
            var info = typeof(QdaoServerSelectView).GetField(field, PrivateInstance);
            Assert.That(info, Is.Not.Null, field);
            return info;
        }
        private object Invoke(string method)
        {
            var info = typeof(QdaoServerSelectView).GetMethod(method, PrivateInstance);
            Assert.That(info, Is.Not.Null, method);
            return info.Invoke(_view, null);
        }
    }
}
