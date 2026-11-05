using MmorpgClient.UI.Ugui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class WorldHudScrollRelayTests
    {
        private GameObject _fixture, _source;
        private EventSystem _events;
        private ScrollRect _scroll;

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("CrossCanvasScrollFixture");
            _events = _fixture.AddComponent<EventSystem>();
            var navigationCanvas = Canvas("NavigationCanvas");
            var viewport = QdaoUguiFactory.CreateRect("CategoryViewport", navigationCanvas.transform,
                0, 0, 100, 100);
            var content = QdaoUguiFactory.CreateRect("CategoryContent", viewport, 0, 0, 100, 400);
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 36;

            // Production entries remain under their feature's own canvas, outside
            // the proxy ScrollRect hierarchy that receives the forwarded gesture.
            var featureCanvas = Canvas("FeatureCanvas");
            var container = QdaoUguiFactory.CreateRect("FeatureOwnedSlot", featureCanvas.transform,
                0, 0, 100, 100);
            container.gameObject.AddComponent<WorldHudScrollRelay>().Target = _scroll;
            _source = QdaoUguiFactory.CreateHitButton("FeatureEntry", container, 0, 0, 100, 100).gameObject;
            UnityEngine.Canvas.ForceUpdateCanvases();
            _scroll.Rebuild(CanvasUpdate.PostLayout);
            _scroll.verticalNormalizedPosition = 1;
        }

        [TearDown]
        public void TearDown()
        {
            if (_fixture != null) Object.DestroyImmediate(_fixture);
        }

        [Test]
        public void WheelAndDragFromFeatureButton_MoveTheSeparateCategoryScrollContent()
        {
            Assert.That(_source.transform.IsChildOf(_scroll.transform), Is.False);
            var wheel = new PointerEventData(_events) { scrollDelta = new Vector2(0, -1) };
            ExecuteEvents.ExecuteHierarchy(_source, wheel, ExecuteEvents.scrollHandler);
            Assert.That(_scroll.content.anchoredPosition.y, Is.GreaterThan(0),
                "滚轮从功能按钮冒泡后必须滚动独立的分类内容");
            Assert.That(_scroll.verticalNormalizedPosition, Is.LessThan(1));

            _scroll.verticalNormalizedPosition = 1;
            var drag = new PointerEventData(_events)
            {
                button = PointerEventData.InputButton.Left,
                position = Vector2.zero
            };
            ExecuteEvents.ExecuteHierarchy(_source, drag, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.ExecuteHierarchy(_source, drag, ExecuteEvents.beginDragHandler);
            drag.position = new Vector2(0, 64);
            ExecuteEvents.ExecuteHierarchy(_source, drag, ExecuteEvents.dragHandler);
            ExecuteEvents.ExecuteHierarchy(_source, drag, ExecuteEvents.endDragHandler);
            Assert.That(_scroll.content.anchoredPosition.y, Is.GreaterThan(0),
                "从功能按钮开始的拖动必须滚动独立的分类内容");
        }

        [Test]
        public void DisabledCategoryScroll_DoesNotMoveWhenFeatureEntryReceivesWheel()
        {
            _scroll.enabled = false;
            var before = _scroll.content.anchoredPosition;
            var wheel = new PointerEventData(_events) { scrollDelta = new Vector2(0, -1) };
            ExecuteEvents.ExecuteHierarchy(_source, wheel, ExecuteEvents.scrollHandler);
            Assert.That(_scroll.content.anchoredPosition, Is.EqualTo(before));
        }

        private GameObject Canvas(string name)
        {
            var canvas = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.Canvas));
            canvas.transform.SetParent(_fixture.transform, false);
            canvas.GetComponent<UnityEngine.Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }
    }
}
