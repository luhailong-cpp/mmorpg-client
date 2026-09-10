using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class HudCanvasLayeringTests
    {
        private GameObject _fixture;
        private RenderTexture _renderTarget;

        [TearDown]
        public void TearDown()
        {
            if (_fixture != null) UnityEngine.Object.DestroyImmediate(_fixture);
            if (_renderTarget != null)
            {
                _renderTarget.Release();
                UnityEngine.Object.DestroyImmediate(_renderTarget);
            }
        }

        [UnityTest]
        public IEnumerator NativeHudCanvasesStayBelowWindowsAndKeepRaycastTargets()
        {
            _fixture = new GameObject("HudCanvasLayeringTests");
            var eventSystemGo = new GameObject("TestEventSystem", typeof(EventSystem));
            eventSystemGo.transform.SetParent(_fixture.transform, false);
            var eventSystem = eventSystemGo.GetComponent<EventSystem>();
            var cameraGo = new GameObject("HudRaycastCamera", typeof(Camera));
            cameraGo.transform.SetParent(_fixture.transform, false);
            var camera = cameraGo.GetComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.targetDisplay = Display.activeEditorGameViewTarget;
            _renderTarget = new RenderTexture(1280, 540, 24, RenderTextureFormat.ARGB32);
            _renderTarget.Create();
            camera.targetTexture = _renderTarget;
            var types = new[] { typeof(BattleUiRoot), typeof(AttributeUiRoot), typeof(PetUiRoot) };
            int[] windowOrders = { 200, 160, 170 };
            var probes = new List<(Canvas windowCanvas, Canvas hudCanvas, Image window, Image hud)>();

            for (int i = 0; i < types.Length; i++)
            {
                // 与生产相同，在激活的父层级内构建嵌套 Canvas；只禁用 Update 防止网络绑定。
                // 不能在非激活层级设好 overrideSorting 后再重挂 Canvas：激活会重算根 Canvas。
                var ownerGo = new GameObject(types[i].Name);
                ownerGo.SetActive(false);
                ownerGo.transform.SetParent(_fixture.transform, false);
                var owner = (Behaviour)ownerGo.AddComponent(types[i]);
                owner.enabled = false;
                ownerGo.SetActive(true);
                types[i].GetMethod("BuildCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(owner, null);
                var canvasGo = (GameObject)types[i].GetField("_canvasGo",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                var canvas = canvasGo.GetComponent<Canvas>();
                var hudRoot = (RectTransform)types[i].GetField("_hudRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                var hudCanvas = hudRoot.GetComponent<Canvas>();
                var windowRoot = (RectTransform)canvasGo.transform.Find("WindowRoot");

                Assert.That(canvas.sortingOrder, Is.EqualTo(windowOrders[i]));
                Assert.That(hudCanvas, Is.Not.Null, types[i].Name + "必须给 HUD 独立 Canvas");
                Assert.That(hudCanvas.overrideSorting, Is.True, types[i].Name + "的 HUD 必须独立排序");
                Assert.That(hudCanvas.sortingOrder, Is.EqualTo(140));
                Assert.That(hudCanvas.sortingOrder, Is.LessThan(canvas.sortingOrder));
                Assert.That(hudRoot.GetComponent<GraphicRaycaster>(), Is.Not.Null);
                Assert.That(hudRoot.gameObject.activeSelf, Is.True, "降层不应隐藏 HUD 触发自动关窗");
                Assert.That(windowRoot, Is.Not.Null, "正式窗口保留在主 Canvas 下");
                probes.Add((canvas, hudCanvas,
                    AddProbe(windowRoot, "WindowProbe" + i), AddProbe(hudRoot, "HudProbe" + i)));
            }

            // EditMode 不保证 GameView 正在绘制；GraphicRaycaster 明确跳过 depth == -1。
            // 沿用项目截图验收的离屏绘制方式，为同一棵真实 UI 树建立原生绘制深度。
            foreach (var transform in _fixture.GetComponentsInChildren<UnityEngine.Transform>(true))
                transform.gameObject.layer = 31;
            foreach (var canvas in _fixture.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 2f;
                canvas.targetDisplay = camera.targetDisplay;
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            var previousTarget = RenderTexture.active;
            try { camera.Render(); }
            finally { RenderTexture.active = previousTarget; }

            foreach (var probe in probes)
            {
                Assert.That(probe.hud.depth, Is.GreaterThanOrEqualTo(0), "HUD 探针必须已被原生 Canvas 绘制");
                Assert.That(probe.window.depth, Is.GreaterThanOrEqualTo(0), "窗口探针必须已被原生 Canvas 绘制");
                Assert.That(probe.hudCanvas.overrideSorting, Is.True);
                Assert.That(probe.hudCanvas.sortingOrder, Is.EqualTo(140));
                var hudRaycaster = probe.hudCanvas.GetComponent<GraphicRaycaster>();
                var hudPointer = new PointerEventData(eventSystem)
                {
                    position = RectTransformUtility.WorldToScreenPoint(
                        hudRaycaster.eventCamera, probe.hud.rectTransform.position),
                    displayIndex = camera.targetDisplay
                };
                var hudHits = new List<RaycastResult>();
                hudRaycaster.Raycast(hudPointer, hudHits);
                var hudHit = hudHits.Find(hit => hit.gameObject == probe.hud.gameObject);
                Assert.That(hudHit.gameObject, Is.EqualTo(probe.hud.gameObject), "HUD 分层后仍可射线命中");
                Assert.That(hudHit.sortingOrder, Is.EqualTo(140));

                var windowRaycaster = probe.windowCanvas.GetComponent<GraphicRaycaster>();
                var windowPointer = new PointerEventData(eventSystem)
                {
                    position = RectTransformUtility.WorldToScreenPoint(
                        windowRaycaster.eventCamera, probe.window.rectTransform.position),
                    displayIndex = camera.targetDisplay
                };
                var windowHits = new List<RaycastResult>();
                windowRaycaster.Raycast(windowPointer, windowHits);
                var windowHit = windowHits.Find(hit => hit.gameObject == probe.window.gameObject);
                Assert.That(windowHit.gameObject, Is.EqualTo(probe.window.gameObject), "正式窗口仍可射线命中");
                Assert.That(windowHit.sortingOrder, Is.GreaterThan(hudHit.sortingOrder));
            }
        }

        private static Image AddProbe(RectTransform parent, string name)
        {
            var rect = QdaoUguiFactory.CreateCenteredRect(name, parent, 100f, 100f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;
            return image;
        }
    }
}
