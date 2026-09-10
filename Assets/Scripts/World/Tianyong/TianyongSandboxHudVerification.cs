using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Attribute;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.World.Tianyong
{
    /// <summary>
    /// Visual acceptance of the production HUD in the offline sandbox. Activated
    /// only by -sandboxDrive. Invokes the actual roots' existing canvas builders;
    /// no copied buttons and no fabricated AppBootstrap, InGame or network client.
    /// Online visibility and request/response behaviour are outside this preview.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongSandboxHudVerification : MonoBehaviour
    {
        private const string Tag = "[SandboxHud]";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<UiTextButton> _entries = new();
        private readonly List<(MonoBehaviour Root, bool Enabled, GameObject Canvas)> _roots = new();
        private readonly Dictionary<Canvas, (RenderMode Mode, Camera Camera, float Distance, int Order)> _captureCanvasState = new();
        private TianyongSandboxBootstrap _sandbox;
        public bool Ready { get; private set; }
        public bool NameplateVerified { get; private set; }
        public string Failure { get; private set; }

        public static TianyongSandboxHudVerification TryAttach(TianyongSandboxBootstrap sandbox)
        {
            if (sandbox == null || !TianyongSandboxAutoDrive.IsActive) return null;
            var existing = sandbox.GetComponent<TianyongSandboxHudVerification>();
            if (existing != null) return existing;
            var preview = sandbox.gameObject.AddComponent<TianyongSandboxHudVerification>();
            preview._sandbox = sandbox;
            preview.Build();
            return preview;
        }

        private void Build()
        {
            try
            {
                // A sandbox preview must never take over a live app's UI state.
                if (FindAnyObjectByType<AppBootstrap>() != null)
                    throw new InvalidOperationException("HUD preview requires the offline sandbox without AppBootstrap.");
                BattleUiRoot.EnsureSpawned();
                AttributeUiRoot.EnsureSpawned();
                BuildRoot(BattleUiRoot.Instance, "_entryButton", "_spectateEntryButton");
                BuildRoot(AttributeUiRoot.Instance, "_entryButton");
                Ready = _entries.Count == 3;
                Debug.Log($"{Tag} ready={Ready} mode=production_canvas_offline_preview entries={_entries.Count} " +
                          "roots=BattleUiRoot,AttributeUiRoot network_validation=false");
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : ex;
                Failure = cause.Message;
                Debug.LogError($"{Tag} RESULT=FAIL reason={Failure}");
            }
        }

        private void BuildRoot(MonoBehaviour root, params string[] entryFields)
        {
            var type = root.GetType();
            var canvasField = type.GetField("_canvasGo", PrivateInstance)
                ?? throw new MissingFieldException(type.Name, "_canvasGo");
            if ((GameObject)canvasField.GetValue(root) != null)
                throw new InvalidOperationException($"{type.Name} already has a canvas; refusing to replace it.");
            var build = type.GetMethod("BuildCanvas", PrivateInstance)
                ?? throw new MissingMethodException(type.Name, "BuildCanvas");
            bool wasEnabled = root.enabled;
            root.enabled = false; // Offline client state must not hide the preview each Update.
            _roots.Add((root, wasEnabled, null));
            try
            {
                build.Invoke(root, null);
            }
            finally
            {
                // Retain partial construction too, so a failed preview restores
                // the root and removes its canvas when the sandbox is destroyed.
                _roots[_roots.Count - 1] = (root, wasEnabled, (GameObject)canvasField.GetValue(root));
            }
            var canvas = (GameObject)canvasField.GetValue(root);
            if (canvas == null) throw new InvalidOperationException($"{type.Name} built no canvas.");
            // Prevent incidental mouse input opening panels during the camera drive.
            // This changes hit testing only, retaining the actual enabled button look.
            foreach (var raycaster in canvas.GetComponentsInChildren<GraphicRaycaster>(true))
                raycaster.enabled = false;
            foreach (var fieldName in entryFields)
            {
                var field = type.GetField(fieldName, PrivateInstance)
                    ?? throw new MissingFieldException(type.Name, fieldName);
                var entry = field.GetValue(root) as UiTextButton;
                if (entry == null || entry.Rect == null || entry.Label == null)
                    throw new InvalidOperationException($"{type.Name}.{fieldName} is incomplete.");
                entry.SetVisible(true);
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// Call after the first rendered frame. Checks the actual canvas geometry
        /// at the running player's resolution; screenshot review checks its art.
        /// </summary>
        public bool ValidateLayout(out string detail)
        {
            if (!Ready)
            {
                detail = Failure ?? "HUD not ready";
                return false;
            }
            Canvas.ForceUpdateCanvases();
            var descriptions = new List<string>();
            var rectangles = new List<Rect>();
            var corners = new UnityEngine.Vector3[4];
            foreach (var entry in _entries)
            {
                entry.Rect.GetWorldCorners(corners);
                var canvas = entry.Rect.GetComponentInParent<Canvas>();
                var canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
                var min = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
                var max = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]);
                var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                if (!entry.Rect.gameObject.activeInHierarchy || !entry.Button.IsInteractable()
                    || rect.width <= 0f || rect.height <= 0f || rect.xMin < 0f || rect.yMin < 0f
                    || rect.xMax > Screen.width + 0.5f || rect.yMax > Screen.height + 0.5f)
                {
                    detail = $"entry clipped, hidden or disabled: {entry.Rect.name} rect={rect}";
                    return false;
                }
                foreach (var previous in rectangles)
                {
                    if (!previous.Overlaps(rect)) continue;
                    detail = $"entry overlap: {entry.Rect.name} rect={rect}";
                    return false;
                }
                rectangles.Add(rect);
                descriptions.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}:({1:F1},{2:F1},{3:F1},{4:F1})", entry.Rect.name, rect.x, rect.y, rect.width, rect.height));
            }
            detail = string.Join(";", descriptions);
            Debug.Log($"{Tag} layout=PASS screen={Screen.width}x{Screen.height} {detail}");
            return true;
        }

        /// <summary>
        /// Route the existing production canvases through the acceptance camera's
        /// RenderTexture. Hidden desktop windows need explicit Camera.Render;
        /// ScreenSpaceOverlay is not included in that render. Passing null restores
        /// each canvas's original mode, camera and plane distance.
        /// </summary>
        public bool SetCaptureCamera(Camera camera)
        {
            if (camera == null)
            {
                foreach (var entry in _captureCanvasState)
                {
                    if (entry.Key == null) continue;
                    entry.Key.renderMode = entry.Value.Mode;
                    entry.Key.worldCamera = entry.Value.Camera;
                    entry.Key.planeDistance = entry.Value.Distance;
                    entry.Key.sortingOrder = entry.Value.Order;
                }
                _captureCanvasState.Clear();
                return true;
            }
            if (!Ready || camera.targetTexture == null || camera.farClipPlane <= camera.nearClipPlane)
            {
                Failure = "HUD capture requires a ready preview and camera RenderTexture with valid clipping planes.";
                return false;
            }
            foreach (var state in _roots)
            {
                var canvas = state.Canvas != null ? state.Canvas.GetComponent<Canvas>() : null;
                if (canvas == null || (camera.cullingMask & (1 << canvas.gameObject.layer)) == 0)
                {
                    Failure = "HUD canvas is missing or excluded by the capture camera culling mask.";
                    return false;
                }
                if (!_captureCanvasState.ContainsKey(canvas))
                    _captureCanvasState.Add(canvas, (canvas.renderMode, canvas.worldCamera, canvas.planeDistance, canvas.sortingOrder));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                // Overlay canvases originally draw after all world renderers.
                // Keep that relationship in a shared-camera render, including
                // world labels' 8000 sorting boost, while retaining HUD order.
                canvas.sortingOrder = 30000 + _captureCanvasState[canvas].Order;
                canvas.planeDistance = Mathf.Min(camera.nearClipPlane + 1f,
                    (camera.nearClipPlane + camera.farClipPlane) * 0.5f);
            }
            Canvas.ForceUpdateCanvases();
            Debug.Log($"{Tag} capture=offscreen_actual_canvas camera={camera.name} " +
                      $"target={camera.targetTexture.width}x{camera.targetTexture.height}");
            return true;
        }

        /// <summary>
        /// Exercises the real actor's label and backdrop across rendered frames.
        /// AutoDrive supplies its existing screenshot method, keeping one ordered
        /// evidence stream. On completion the actor's original name is restored.
        /// </summary>
        public IEnumerator VerifyNameplate(Func<string, IEnumerator> capture)
        {
            NameplateVerified = false;
            var labelTransform = _sandbox != null && _sandbox.Player != null
                ? _sandbox.Player.transform.Find(WorldNameplate.ObjectName) : null;
            var label = labelTransform != null ? labelTransform.GetComponent<TextMeshPro>() : null;
            if (label == null)
            {
                NameplateFailure("real sandbox player nameplate missing");
                yield break;
            }
            var originalName = label.text;
            var originallyEnabled = label.enabled;
            float shortWidth = 0f;
            try
            {
                var cases = new[]
                {
                    (Name: "short", Text: "云行客", Enabled: true, Visible: true),
                    (Name: "long_cjk", Text: "天墉城的远行旅人", Enabled: true, Visible: true),
                    (Name: "empty", Text: "", Enabled: true, Visible: false),
                    (Name: "disabled", Text: "云行客", Enabled: false, Visible: false),
                    (Name: "restored", Text: originalName, Enabled: originallyEnabled,
                        Visible: originallyEnabled && !string.IsNullOrWhiteSpace(originalName)),
                };
                foreach (var sample in cases)
                {
                    label.text = sample.Text;
                    label.enabled = sample.Enabled;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    if (!ValidateNameplate(label, sample.Visible, out var width, out var detail))
                    {
                        NameplateFailure($"case={sample.Name} {detail}");
                        yield break;
                    }
                    if (sample.Name == "short") shortWidth = width;
                    if (sample.Name == "long_cjk" && width <= shortWidth + 0.1f)
                    {
                        NameplateFailure($"long CJK name did not grow: short={shortWidth} long={width}");
                        yield break;
                    }
                    Debug.Log($"{Tag} nameplate_case={sample.Name} RESULT=PASS {detail}");
                    if (capture != null) yield return capture($"00_nameplate_{sample.Name}");
                }
                NameplateVerified = true;
                Debug.Log($"{Tag} nameplate=PASS cases=short,long_cjk,empty,disabled,restored");
            }
            finally
            {
                if (label != null)
                {
                    label.text = originalName;
                    label.enabled = originallyEnabled;
                }
            }
        }

        private static bool ValidateNameplate(TextMeshPro label, bool expectedVisible,
            out float width, out string detail)
        {
            width = 0f;
            var backdropTransform = label.transform.Find("NameplateBackdrop");
            var backdrop = backdropTransform != null ? backdropTransform.GetComponent<SpriteRenderer>() : null;
            if (backdrop == null || backdrop.sprite == null)
            {
                detail = "backdrop renderer or sprite missing";
                return false;
            }
            if (backdrop.enabled != expectedVisible)
            {
                detail = $"backdrop enabled={backdrop.enabled} expected={expectedVisible}";
                return false;
            }
            if (!expectedVisible)
            {
                detail = "backdrop_hidden=true";
                return true;
            }
            var bounds = label.textBounds;
            var spriteBounds = backdrop.sprite.bounds;
            width = spriteBounds.size.x * backdrop.transform.localScale.x;
            float height = spriteBounds.size.y * backdrop.transform.localScale.y;
            float requiredWidth = bounds.size.x + WorldNameplate.BackdropPaddingX * 2f;
            float requiredHeight = Mathf.Max(bounds.size.y, WorldNameplate.WorldEmHeight)
                + WorldNameplate.BackdropPaddingY * 2f;
            var textRenderer = label.GetComponent<MeshRenderer>();
            var material = backdrop.sharedMaterial;
            bool valid = Mathf.Abs(width - requiredWidth) < 0.01f
                && Mathf.Abs(height - requiredHeight) < 0.01f
                && Mathf.Abs(backdrop.transform.localPosition.x - bounds.center.x) < 0.01f
                && Mathf.Abs(backdrop.transform.localPosition.y - bounds.center.y) < 0.01f
                && textRenderer != null && backdrop.sortingLayerID == textRenderer.sortingLayerID
                && backdrop.sortingOrder == label.sortingOrder - 1
                && backdrop.color.a > 0f && backdrop.color.a < 1f
                && material != null && material.shader != null && material.shader.isSupported;
            detail = string.Format(CultureInfo.InvariantCulture,
                "width={0:F3} required_width={1:F3} height={2:F3} required_height={3:F3} " +
                "text_order={4} backdrop_order={5} alpha={6:F2} shader={7}",
                width, requiredWidth, height, requiredHeight, label.sortingOrder, backdrop.sortingOrder,
                backdrop.color.a, material != null && material.shader != null ? material.shader.name : "MISSING");
            return valid;
        }

        private void NameplateFailure(string reason)
        {
            Failure = reason;
            Debug.LogError($"{Tag} nameplate=FAIL reason={reason}");
        }

        private void OnDestroy()
        {
            SetCaptureCamera(null);
            foreach (var state in _roots)
            {
                if (state.Canvas != null) Destroy(state.Canvas);
                if (state.Root != null) state.Root.enabled = state.Enabled;
            }
            Ready = false;
        }
    }
}
