using System.Reflection;
using System.Linq;
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

        [Test]
        public void ProductionPrefab_UsesStaticArtworkAndSemanticControls()
        {
            AssertRequiredSprite(QdaoUguiTheme.ScreenArtSpritePath);
            AssertRequiredSprite(QdaoUguiTheme.StatusDotSpritePath);
            AssertRequiredSprite(QdaoUguiTheme.CredentialSpritePath);
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Resources/UI/Ugui/Native/screen_base_headband.png"), Is.Null,
                "The retired screen_base alias must not return beside the explicit production ScreenArtwork asset.");

            var prefab = Resources.Load<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing Resources/{PrefabPath}.prefab");

            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<QdaoServerSelectView>();
                Assert.That(view, Is.Not.Null);
                AssertSerializedPrefabReferences(view);
                Assert.That(instance.transform.Find("ReferenceArtwork"), Is.Null,
                    "The retired reference-only object must not return to the runtime prefab.");
                Assert.That(instance.transform.Find("ScreenBaseHeadband"), Is.Null,
                    "Editor-only reference objects must not return to the production hierarchy.");
                AssertVisibleSprite(instance, "ScreenArtwork");
                var backdrop = RequiredTransform(instance, "LetterboxBackdrop").GetComponent<Image>();
                Assert.That(backdrop, Is.Not.Null);
                Assert.That(backdrop.sprite, Is.Null);
                Assert.That(backdrop.raycastTarget, Is.False);
                Assert.That(backdrop.color, Is.EqualTo(QdaoUguiTheme.Letterbox));
                AssertHitButton(instance, "TopTab_0");
                AssertHitButton(instance, "CategoryArt_0");
                AssertHitButton(instance, "ServerCardArt_0");
                AssertHitButton(instance, "BackButton");
                AssertHitButton(instance, "RefreshButton");
                AssertHitButton(instance, "EnterButton");
                AssertHitButton(instance, "CredentialCancelButton");
                AssertHitButton(instance, "CredentialSubmitButton");
                Assert.That(RequiredTransform(instance, "CredentialBlocker")
                    .GetComponent<Image>().raycastTarget, Is.True);
                var allButtons = instance.GetComponentsInChildren<Button>(true);
                Assert.That(allButtons, Has.Length.EqualTo(24));
                Assert.That(allButtons.All(button => button.targetGraphic is Image image &&
                                                     image.raycastTarget), Is.True,
                    "Every semantic Button must own a raycastable target Image.");
                var fullScreenSprites = instance.GetComponentsInChildren<Image>(true)
                    .Where(image => image.sprite != null &&
                                    Mathf.RoundToInt(image.sprite.rect.width) == 2560 &&
                                    Mathf.RoundToInt(image.sprite.rect.height) == 1080)
                    .ToArray();
                Assert.That(fullScreenSprites.Select(image => image.name),
                    Is.EquivalentTo(new[] { "ScreenArtwork" }));
                var screenArtwork = RequiredTransform(instance, "ScreenArtwork").GetComponent<Image>();
                Assert.That(screenArtwork.raycastTarget, Is.False,
                    "Static artwork must never replace semantic uGUI hit targets.");

                AssertCenteredDesignRoot(instance);
                AssertStretchRect(instance, "LetterboxBackdrop");
                AssertRect(instance, "ScreenArtwork", 0f, 0f, 2560f, 1080f);
                AssertRect(instance, "SearchInput", 692f, 307f, 188f, 44f);
                AssertRect(instance, "TopTabText_0", 709f, 201f, 260f, 58f);
                AssertRect(instance, "CategoryText_0", 704f, 379f, 126f, 30f);
                AssertRect(instance, "ServerName_0", 1031f, 326f, 246f, 30f);
                AssertRect(instance, "ServerDot_0", 990f, 345f, 24f, 24f);
                AssertRect(instance, "EnterText", 1732f, 869f, 168f, 36f);
                AssertRect(instance, "EnterButton", 1728f, 850f, 176f, 82f);
                Assert.That(RequiredTransform(instance, "RefreshText").GetComponent<TMP_Text>().text, Is.EqualTo("刷新"));
                Assert.That(RequiredTransform(instance, "ServerStatus_0").GetComponent<TMP_Text>().text, Is.Empty);

                var childCount = instance.transform.childCount;
                view.PrepareForPreview();
                Assert.That(instance.transform.childCount, Is.EqualTo(childCount));
                Assert.That(RequiredTransform(instance, "ScreenArtwork"), Is.Not.Null,
                    "A freshly baked prefab must not be discarded during sprite rebinding.");

                var account = RequiredInput(instance, "AccountInput");
                Assert.That(account.gameObject.activeInHierarchy, Is.False);
                Assert.That(account.characterLimit, Is.EqualTo(191));

                var password = RequiredInput(instance, "PasswordInput");
                Assert.That(password.gameObject.activeInHierarchy, Is.False);
                Assert.That(password.characterLimit, Is.EqualTo(1024));
                Assert.That(password.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));

                var dots = instance.GetComponentsInChildren<Image>(true)
                    .Where(image => image.name.StartsWith("ServerDot_"))
                    .ToArray();
                Assert.That(dots, Has.Length.EqualTo(8));
                Assert.That(dots.All(image => image.sprite != null), Is.True,
                    "Every server status light must receive its runtime circle sprite.");

                var busyField = typeof(QdaoServerSelectView).GetField(
                    "_busy", BindingFlags.Instance | BindingFlags.NonPublic);
                var refreshMethod = typeof(QdaoServerSelectView).GetMethod(
                    "RefreshVisualState", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(busyField, Is.Not.Null);
                Assert.That(refreshMethod, Is.Not.Null);
                busyField.SetValue(view, true);
                refreshMethod.Invoke(view, null);
                Assert.That(instance.GetComponentsInChildren<Button>(true)
                    .Where(button => button.name.StartsWith("TopTab_") ||
                                     button.name.StartsWith("CategoryArt_") ||
                                     button.name.StartsWith("ServerCardArt_") ||
                                     button.name is "PrevPage" or "NextPage" or
                                                    "BackButton" or "RefreshButton" or "EnterButton" or
                                                    "CredentialCancelButton" or "CredentialSubmitButton")
                    .All(button => !button.interactable), Is.True,
                    "An in-flight enter pipeline must freeze every state-changing control.");
                Assert.That(RequiredInput(instance, "SearchInput").interactable, Is.False);
                Assert.That(account.interactable, Is.False);
                Assert.That(password.interactable, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static TMP_InputField RequiredInput(GameObject root, string name)
        {
            var input = root.GetComponentsInChildren<TMP_InputField>(true)
                .SingleOrDefault(candidate => candidate.name == name);
            Assert.That(input, Is.Not.Null, $"Missing TMP_InputField {name}");
            return input;
        }

        private static void AssertRequiredSprite(string resourcePath)
            => Assert.That(Resources.Load<Sprite>(resourcePath), Is.Not.Null,
                $"Missing Resources/{resourcePath}.png");

        private static void AssertVisibleSprite(GameObject root, string name)
        {
            var image = RequiredTransform(root, name).GetComponent<Image>();
            Assert.That(image, Is.Not.Null, $"{name} has no Image");
            Assert.That(image.sprite, Is.Not.Null, $"{name} has no Sprite");
            Assert.That(image.enabled, Is.True, $"{name} is disabled");
            Assert.That(image.color.a, Is.GreaterThan(0.01f), $"{name} is transparent");
        }

        private static void AssertVisibleButton(GameObject root, string name)
        {
            var target = RequiredTransform(root, name);
            var image = target.GetComponent<Image>();
            var button = target.GetComponent<Button>();
            Assert.That(image, Is.Not.Null, $"{name} has no visible Image");
            Assert.That(image.sprite, Is.Not.Null, $"{name} has no Sprite");
            Assert.That(button, Is.Not.Null, $"{name} has no Button");
            Assert.That(button.targetGraphic, Is.SameAs(image),
                $"{name} must use its visible art as targetGraphic");
        }

        private static void AssertHitButton(GameObject root, string name)
        {
            var target = RequiredTransform(root, name);
            var image = target.GetComponent<Image>();
            var button = target.GetComponent<Button>();
            Assert.That(image, Is.Not.Null, $"{name} has no raycast Image");
            Assert.That(image.raycastTarget, Is.True, $"{name} is not raycastable");
            Assert.That(button, Is.Not.Null, $"{name} has no Button");
            Assert.That(button.targetGraphic, Is.SameAs(image));
        }

        private static void AssertCenteredDesignRoot(GameObject root)
        {
            var rootRect = root.GetComponent<RectTransform>();
            Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(rootRect.sizeDelta, Is.EqualTo(Vector2.zero));

            var content = RequiredTransform(root, "ContentRoot").GetComponent<RectTransform>();
            Assert.That(content.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(content.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(content.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(content.sizeDelta, Is.EqualTo(new Vector2(2560f, 1080f)));
        }

        private static void AssertSerializedPrefabReferences(QdaoServerSelectView view)
        {
            var serialized = new SerializedObject(view);
            foreach (var propertyName in new[]
                     {
                         "_screenArtSprite", "_statusDotSprite", "_credentialSprite",
                         "_contentRoot", "_backdropImage", "_screenArtImage",
                         "_credentialPanel", "_credentialBlockerImage", "_credentialImage",
                         "_credentialCancelButton", "_credentialSubmitButton",
                         "_credentialCancelText", "_credentialSubmitText", "_accountInput",
                         "_passwordInput", "_searchInput", "_prevPageButton", "_nextPageButton",
                         "_prevPageText", "_nextPageText", "_pageText",
                         "_lastLoginText", "_selectedText", "_refreshText", "_enterText", "_statusText",
                         "_backButton", "_refreshButton", "_enterButton",
                         "_topDefaultDimmer", "_categoryDefaultDimmer",
                     })
            {
                var property = serialized.FindProperty(propertyName);
                Assert.That(property, Is.Not.Null, $"Missing serialized property {propertyName}");
                Assert.That(property.objectReferenceValue, Is.Not.Null,
                    $"Prefab did not serialize {propertyName}; runtime repair is forbidden.");
            }

            foreach (var propertyName in new[]
                     {
                         "_topButtons", "_topImages", "_topSelectionMarks", "_topLabels",
                         "_categoryButtons", "_categoryImages", "_categorySelectionMarks",
                         "_categoryTexts", "_serverButtons", "_serverCardImages", "_serverNames",
                         "_serverSubtitles", "_serverDots", "_serverEmptyCovers",
                     })
            {
                var property = serialized.FindProperty(propertyName);
                Assert.That(property, Is.Not.Null);
                Assert.That(property.isArray, Is.True);
                Assert.That(property.arraySize, Is.GreaterThan(0));
                for (var i = 0; i < property.arraySize; i++)
                    Assert.That(property.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null,
                        $"Prefab did not serialize {propertyName}[{i}].");
            }
        }

        private static void AssertRect(
            GameObject root,
            string childName,
            float x,
            float y,
            float width,
            float height)
        {
            var child = RequiredTransform(root, childName);
            var rect = child.GetComponent<RectTransform>();
            Assert.That(rect, Is.Not.Null, $"{childName} is not a RectTransform");
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(x).Within(0.01f));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-y).Within(0.01f));
            Assert.That(rect.sizeDelta.x, Is.EqualTo(width).Within(0.01f));
            Assert.That(rect.sizeDelta.y, Is.EqualTo(height).Within(0.01f));
        }

        private static void AssertStretchRect(GameObject root, string childName)
        {
            var rect = RequiredTransform(root, childName).GetComponent<RectTransform>();
            Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(rect.offsetMin, Is.EqualTo(Vector2.zero));
            Assert.That(rect.offsetMax, Is.EqualTo(Vector2.zero));
        }

        private static Transform RequiredTransform(GameObject root, string childName)
        {
            var child = root.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(candidate => candidate.name == childName);
            Assert.That(child, Is.Not.Null, $"Missing {childName}");
            return child;
        }
    }
}
