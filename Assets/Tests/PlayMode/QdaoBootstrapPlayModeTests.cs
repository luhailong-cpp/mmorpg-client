using System.Collections;
using System.Linq;
using MmorpgClient.Net;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoBootstrapPlayModeTests
    {
        [UnityTest]
        public IEnumerator AppBootstrap_StartsNativeLoginAndServerSelectView()
        {
            // AppBootstrap.AutoSpawn runs AfterSceneLoad. Give the empty test
            // scene a frame to complete the same lifecycle used by a real build.
            yield return null;

            var app = Object.FindAnyObjectByType<AppBootstrap>();
            Assert.That(app, Is.Not.Null, "AppBootstrap did not auto-spawn.");
            Assert.That(app.GameClient, Is.Not.Null);
            Assert.That(app.Gateway, Is.Not.Null);
            Assert.That(app.WorldMap, Is.Not.Null);
            Assert.That(app.Ugui, Is.Not.Null,
                "Native uGUI failed to initialize and fell back to FairyGUI.");
            Assert.That(app.Router, Is.Null,
                "The native startup path must return before creating the legacy router.");
            app.StopAllCoroutines();

            var view = Object.FindAnyObjectByType<QdaoServerSelectView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.isActiveAndEnabled, Is.True);

            // The production root also owns a battle overlay Canvas at order
            // 200. Assert against the Canvas that actually contains this view
            // instead of whichever Canvas FindAny happens to return.
            var canvas = view.GetComponentInParent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.sortingOrder, Is.EqualTo(100));
            Assert.That(canvas.pixelPerfect, Is.True);
            var scaler = canvas.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(2560f, 1080f)));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.Expand));
            Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Not.Null);
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);

            Assert.That(FindTransform(view.gameObject, "ScreenBaseHeadband"), Is.Null);
            Assert.That(FindTransform(view.gameObject, "ReferenceArtwork"), Is.Null);
            var backdrop = FindTransform(view.gameObject, "LetterboxBackdrop")?.GetComponent<Image>();
            Assert.That(backdrop, Is.Not.Null);
            Assert.That(backdrop.sprite, Is.Null);
            Assert.That(backdrop.raycastTarget, Is.False);
            var screenArtwork = FindTransform(view.gameObject, "ScreenArtwork")?.GetComponent<Image>();
            Assert.That(screenArtwork?.sprite, Is.Not.Null);
            Assert.That(screenArtwork?.raycastTarget, Is.False);
            var firstCardTransform = FindTransform(view.gameObject, "ServerCardArt_0");
            Assert.That(firstCardTransform?.GetComponent<Button>()?.targetGraphic,
                Is.SameAs(firstCardTransform?.GetComponent<Image>()));

            var inputs = view.GetComponentsInChildren<TMP_InputField>(true);
            var account = inputs.Single(input => input.name == "AccountInput");
            var password = inputs.Single(input => input.name == "PasswordInput");
            Assert.That(account.gameObject.activeInHierarchy, Is.False);
            Assert.That(password.gameObject.activeInHierarchy, Is.False);
            Assert.That(account.characterLimit, Is.EqualTo(191));
            Assert.That(password.characterLimit, Is.EqualTo(1024));
            Assert.That(password.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));

            var dots = view.GetComponentsInChildren<Image>(true)
                .Where(image => image.name.StartsWith("ServerDot_"))
                .ToArray();
            Assert.That(dots, Has.Length.EqualTo(8));
            Assert.That(dots.All(image => image.sprite != null), Is.True);

            var topButtons = view.GetComponentsInChildren<Button>(true);
            app.Session.Zones.Clear();
            for (var i = 1; i <= 17; i++)
            {
                app.Session.Zones.Add(new ServerListZone
                {
                    zone_id = (uint)i,
                    name = $"区服{i}",
                    status = i == 6 ? "MAINTENANCE" : "OPEN",
                    load_level = i % 4 == 0 ? "BUSY" : "SMOOTH",
                    recommended = i <= 2,
                    is_new = i >= 15,
                });
            }
            topButtons.Single(button => button.name == "TopTab_2").onClick.Invoke();
            yield return null;

            var labels = view.GetComponentsInChildren<TMP_Text>(true);
            var recentTop = labels.Single(label => label.name == "TopTabText_0");
            var allTop = labels.Single(label => label.name == "TopTabText_2");
            var recentCategory = labels.Single(label => label.name == "CategoryText_0");
            var allCategory = labels.Single(label => label.name == "CategoryText_2");
            Assert.That(recentTop.color, Is.EqualTo(QdaoUguiTheme.Brown));
            Assert.That(allTop.color, Is.EqualTo(QdaoUguiTheme.SelectedRed));
            Assert.That(recentCategory.color, Is.EqualTo(QdaoUguiTheme.Brown));
            Assert.That(allCategory.color, Is.EqualTo(QdaoUguiTheme.SelectedRed));
            Assert.That(FindTransform(view.gameObject, "TopDefaultDimmer").GetComponent<Image>().enabled, Is.True);
            Assert.That(FindTransform(view.gameObject, "TopTabSelection_2").GetComponent<Image>().enabled, Is.True);
            Assert.That(FindTransform(view.gameObject, "CategoryDefaultDimmer").GetComponent<Image>().enabled, Is.True);
            Assert.That(FindTransform(view.gameObject, "CategorySelection_2").GetComponent<Image>().enabled, Is.True);

            var firstServer = topButtons.Single(button => button.name == "ServerCardArt_0");
            firstServer.onClick.Invoke();
            Assert.That(app.Session.SelectedZoneId, Is.EqualTo(1));
            Assert.That(app.Session.SelectedZoneIndex, Is.EqualTo(0));

            var pageText = labels.Single(label => label.name == "PageText");
            var prevPageText = labels.Single(label => label.name == "PrevPageText");
            var nextPageText = labels.Single(label => label.name == "NextPageText");
            var next = topButtons.Single(button => button.name == "NextPage");
            Assert.That(pageText.text, Is.EqualTo("1/3"));
            Assert.That(prevPageText.gameObject.activeInHierarchy, Is.True);
            Assert.That(nextPageText.gameObject.activeInHierarchy, Is.True);
            next.onClick.Invoke();
            Assert.That(pageText.text, Is.EqualTo("2/3"));
            next.onClick.Invoke();
            Assert.That(pageText.text, Is.EqualTo("3/3"));
            Assert.That(next.interactable, Is.False);

            var search = inputs.Single(input => input.name == "SearchInput");
            search.onValueChanged.Invoke("17");
            yield return null;
            Assert.That(pageText.text, Is.Empty, "Searching must reset paging to the first page.");
            search.onValueChanged.Invoke("__no_matching_zone__");
            yield return null;
            Assert.That(dots.All(image => !image.enabled), Is.True,
                "Empty native card slots must hide their independent status lights.");
            Assert.That(topButtons.Where(button => button.name.StartsWith("ServerCardArt_"))
                .All(button => !button.interactable), Is.True);

            var backButton = view.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "BackButton");
            backButton.onClick.Invoke();
            yield return null;
            Assert.That(account.gameObject.activeInHierarchy, Is.True);
            Assert.That(password.gameObject.activeInHierarchy, Is.True);
            Assert.That(FindTransform(view.gameObject, "CredentialBlocker")
                .GetComponent<Image>().raycastTarget, Is.True);
            var cancel = view.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "CredentialCancelButton");
            cancel.onClick.Invoke();
            yield return null;
            Assert.That(account.gameObject.activeInHierarchy, Is.False);
            Assert.That(password.gameObject.activeInHierarchy, Is.False);

            Object.Destroy(app.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReferenceResolution_UsesUnitScaleAndCenteredDesignRoot()
        {
            var cameraObject = new GameObject("QdaoScalerTestCamera", typeof(Camera));
            var canvasObject = new GameObject(
                "QdaoScalerTestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var target = new RenderTexture(2560, 1080, 0);
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.targetTexture = target;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(2560f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(scaler.scaleFactor, Is.EqualTo(1f).Within(0.001f));

                var content = new GameObject("ContentRoot", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                content.SetParent(canvasObject.transform, false);
                content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
                content.sizeDelta = new Vector2(2560f, 1080f);
                Assert.That(content.rect.size, Is.EqualTo(new Vector2(2560f, 1080f)));
                Assert.That(content.localScale, Is.EqualTo(Vector3.one));
            }
            finally
            {
                Object.Destroy(canvasObject);
                Object.Destroy(cameraObject);
                target.Release();
                Object.Destroy(target);
            }
        }

        [UnityTest]
        public IEnumerator SixteenByNine_UsesSolidLetterboxAndKeepsArtworkAtAuthoredAspect()
        {
            // CanvasScaler.Expand maps a 1920x1080 viewport to 2560x1440
            // logical units. RenderTexture does not override Screen size in a
            // headless test process, so exercise that resulting viewport
            // directly instead of asserting an environment-owned scaleFactor.
            var viewportObject = new GameObject("Qdao16By9LogicalViewport", typeof(RectTransform));
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
            viewport.sizeDelta = new Vector2(2560f, 1440f);
            GameObject instance = null;
            try
            {
                var prefab = Resources.Load<GameObject>("UI/Ugui/Prefabs/QdaoServerSelect");
                Assert.That(prefab, Is.Not.Null);
                instance = Object.Instantiate(prefab, viewport, false);

                yield return null;
                Canvas.ForceUpdateCanvases();

                var rootRect = instance.GetComponent<RectTransform>();
                Assert.That(rootRect.rect.size.x, Is.EqualTo(2560f).Within(0.1f));
                Assert.That(rootRect.rect.size.y, Is.EqualTo(1440f).Within(0.1f));

                var content = FindTransform(instance, "ContentRoot").GetComponent<RectTransform>();
                Assert.That(content.rect.size, Is.EqualTo(new Vector2(2560f, 1080f)));
                Assert.That(content.anchoredPosition, Is.EqualTo(Vector2.zero));

                var backdrop = FindTransform(instance, "LetterboxBackdrop").GetComponent<RectTransform>();
                Assert.That(backdrop.rect.size, Is.EqualTo(rootRect.rect.size));
                var artwork = FindTransform(instance, "ScreenArtwork").GetComponent<RectTransform>();
                Assert.That(artwork.rect.size, Is.EqualTo(new Vector2(2560f, 1080f)));
            }
            finally
            {
                if (instance != null) Object.Destroy(instance);
                Object.Destroy(viewportObject);
            }
        }

        private static Transform FindTransform(GameObject root, string name)
            => root.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(candidate => candidate.name == name);
    }
}
