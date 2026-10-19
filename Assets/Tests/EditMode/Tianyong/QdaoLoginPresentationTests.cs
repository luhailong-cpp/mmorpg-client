using System.Linq;
using System.Reflection;
using MmorpgClient.Net;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Ugui
{
    public sealed class QdaoLoginPresentationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root, _appHost;
        private QdaoServerSelectView _view;
        private SessionModel _session;

        [SetUp]
        public void SetUp()
        {
            _root = Object.Instantiate(Resources.Load<GameObject>("UI/Ugui/Prefabs/QdaoServerSelect"));
            _view = _root.GetComponent<QdaoServerSelectView>();
            _view.PrepareForPreview();
            _view.ShowLanding(true);
            _appHost = new GameObject("LoginPresentationFixture");
            _appHost.SetActive(false);
            var app = _appHost.AddComponent<AppBootstrap>();
            _session = new SessionModel();
            typeof(AppBootstrap).GetField("<Session>k__BackingField", Private).SetValue(app, _session);
            typeof(QdaoServerSelectView).GetField("_app", Private).SetValue(_view, app);
            typeof(QdaoServerSelectView).GetMethod("BindEvents", Private).Invoke(_view, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_appHost != null) Object.DestroyImmediate(_appHost);
        }

        [Test]
        public void Landing_PreservesOriginalFixedLetteringAndBothWorkingUtilityEntrypoints()
        {
            var art = Required<Image>("ScreenArtwork");
            Assert.That(art.sprite.rect.width, Is.EqualTo(2560));
            Assert.That(art.sprite.rect.height, Is.EqualTo(1080));
            Assert.That(Required<Image>("EnterGame").sprite.rect.size, Is.EqualTo(new Vector2(733, 167)));
            Assert.That(Required<TMP_Text>("EnterGameText").gameObject.activeInHierarchy, Is.False,
                "The original painted action lettering must not be overprinted by an approximate native font.");
            Assert.That(Required<TMP_Text>("SwitchAccountText").gameObject.activeInHierarchy, Is.False);
            foreach (var name in new[] { "LandingAnnouncement", "LandingSettings" })
            {
                var button = Required<Button>(name);
                Assert.That(button.interactable, Is.True, name);
                Assert.That(button.targetGraphic, Is.TypeOf<Image>(), name);
                Assert.That(button.targetGraphic.raycastTarget, Is.True, name);
                Assert.That(((Image)button.targetGraphic).sprite, Is.Not.Null, name);
            }
        }

        [Test]
        public void Announcements_ShowActualSessionContentAndAnHonestEmptyState()
        {
            _session.Announcements.Add(new AnnouncementItem
                { title = "真实公告标题", content = "真实公告正文 <b>原样显示</b>" });
            Click("LandingAnnouncement");
            Assert.That(Required<RectTransform>("AnnouncementPanel").gameObject.activeSelf, Is.True);
            var body = Required<TMP_Text>("AnnouncementBody");
            Assert.That(body.text, Does.Contain("真实公告标题").And.Contain("真实公告正文"));
            Assert.That(body.richText, Is.False, "Remote announcement content must remain plain text.");
            Click("AnnouncementClose");
            Assert.That(Required<RectTransform>("AnnouncementPanel").gameObject.activeSelf, Is.False);
            _session.Announcements.Clear();
            Click("LandingAnnouncement");
            Assert.That(body.text, Does.Contain("暂无公告"));
            Assert.That(body.text, Does.Not.Contain("真实公告正文"));
        }

        [Test]
        public void UtilityModals_CloseBackToTheUsableLandingPage()
        {
            Click("LandingSettings");
            Assert.That(Required<RectTransform>("SettingsPanel").gameObject.activeSelf, Is.True);
            Assert.That(Required<Button>("SelectServer").interactable, Is.False,
                "A modal must also suspend keyboard navigation to the page behind its pointer blocker.");
            Assert.That(Required<Button>("SwitchAccount").interactable, Is.False);
            Assert.That(Required<Slider>("SettingsVolumeSlider").interactable, Is.True);
            Assert.That(Required<Toggle>("SettingsFullscreen").interactable, Is.True);
            Click("SettingsClose");
            Assert.That(Required<RectTransform>("SettingsPanel").gameObject.activeSelf, Is.False);
            Assert.That(Required<RectTransform>("LoginPage").gameObject.activeSelf, Is.True);
            Assert.That(Required<Button>("SelectServer").interactable, Is.True);
            Assert.That(Required<Button>("SwitchAccount").interactable, Is.True);
            Assert.That(Required<Button>("LandingAnnouncement").interactable, Is.True);
        }

        [Test]
        public void VolumeControl_ChangesActualOutputAndRestoresItsSavedValueWhenReopened()
        {
            const string preference = "Qdao.MasterVolume";
            bool hadPreference = PlayerPrefs.HasKey(preference);
            float saved = PlayerPrefs.GetFloat(preference), output = AudioListener.volume;
            try
            {
                Click("LandingSettings");
                Required<Slider>("SettingsVolumeSlider").value = .37f;
                Assert.That(AudioListener.volume, Is.EqualTo(.37f).Within(.001f));
                var slider = Required<Slider>("SettingsVolumeSlider");
                slider.fillRect.ForceUpdateRectTransforms();
                Assert.That(slider.fillRect.rect.width, Is.EqualTo(470f * .37f).Within(1f),
                    "Slider fill must represent volume without overflowing its track.");
                slider.handleRect.ForceUpdateRectTransforms();
                Assert.That(slider.handleRect.rect.size, Is.EqualTo(new Vector2(42, 42)),
                    "Slider's perpendicular stretch must preserve a round handle.");
                Click("SettingsClose"); Click("LandingSettings");
                Assert.That(Required<Slider>("SettingsVolumeSlider").value, Is.EqualTo(.37f).Within(.001f));
                Assert.That(Required<TMP_Text>("SettingsVolumeValue").text, Is.EqualTo("37%"));
            }
            finally
            {
                AudioListener.volume = output;
                if (hadPreference) PlayerPrefs.SetFloat(preference, saved); else PlayerPrefs.DeleteKey(preference);
                PlayerPrefs.Save();
            }
        }

        private T Required<T>(string name) where T : Component
        {
            var result = _root.GetComponentsInChildren<T>(true).SingleOrDefault(x => x.name == name);
            Assert.That(result, Is.Not.Null, name);
            return result;
        }
        private void Click(string name) => Required<Button>(name).onClick.Invoke();
    }
}
