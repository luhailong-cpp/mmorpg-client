using MmorpgClient.Game.CityNpc;
using MmorpgClient.World.Occlusion;
using TMPro;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 归元城 8 位 100% 原创核心 NPC 的大世界表现实体（纯显示、无碰撞体、绝不挡寻路）。
    /// 优先加载 Codex 交付的 1024×1024 透明立绘（pivot = (0.5, 0.04) 脚底贴地）与 npc_bubble_bg 气泡底板；
    /// 缺图时无缝回退项目自有原创形象目录（QdaoBoySpriteAnimator）与文字气泡。
    /// </summary>
    public sealed class GuiyuanCityNpcActor : MonoBehaviour
    {
        public const float HitWorldHeight = 9.4f;
        public const float HitWidthPerHeight = 0.52f;
        public const string BubbleBgResourceKey = "UI/Ugui/GuiyuanNpcV1/npc_bubble_bg";
        private const float DedicatedVisibleHeight = 8.4f;

        private CityNpcDefinition _definition;
        private Transform _dedicatedVisual;
        private SpriteRenderer _dedicatedRenderer;
        private Material _dedicatedDefaultMaterial;
        private float _dedicatedScreenUpOffset;
        private GameObject _bubbleRoot;
        private SpriteRenderer _bubbleBgRenderer;
        private TextMeshPro _bubbleText;
        private float _bubbleHideAt;

        public CityNpcDefinition Definition => _definition;
        public string NpcId => _definition?.Id ?? "";
        public Vector3 FeetPosition => transform.position;

        public static GuiyuanCityNpcActor Create(Transform parent, CityNpcDefinition definition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "[GuiyuanNpc:" + definition.Id + "]";
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(definition.WorldX, 0f, definition.WorldZ);

            var label = WorldNameplate.Create(go.transform, $"{definition.Name} · {definition.RoleTitle}", WorldNameplate.NpcColor);
            WorldLabelBillboard.Attach(label.gameObject);

            var actor = go.AddComponent<GuiyuanCityNpcActor>();
            actor._definition = definition;
            actor.CreateBubbleLabel();

            if (Application.isPlaying)
            {
                var dedicated = Resources.Load<Sprite>(definition.DedicatedSpriteKey);
                if (dedicated != null)
                {
                    actor.AttachDedicatedSprite(go, dedicated);
                }
                else
                {
                    QdaoBoySpriteAnimator.TryAttach(go, definition.FallbackAppearanceId);
                }
            }
            return actor;
        }

        private void AttachDedicatedSprite(GameObject host, Sprite sprite)
        {
            var meshRenderer = host.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = false;
            var visual = new GameObject("DedicatedNpcSprite");
            visual.transform.SetParent(host.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            float spriteHeight = sprite.bounds.size.y > 0.01f ? sprite.bounds.size.y : 1f;
            float scale = DedicatedVisibleHeight / spriteHeight;
            visual.transform.localScale = new Vector3(scale, scale, 1f);

            // Codex 交付的 8 位 NPC 均为底部居中锚点 (0.5, 0.04)，脚底直接贴合 y=0；
            // 若未来替换为居中锚点图，则自动补齐半高偏移。
            // 立绘走角色同级 WorldSortingOrder 与顶视深度带，不挂 WorldLabelBillboard（避免错加 +8000 名字牌层级压在前景与玩家之上）。
            float pivotNormY = sprite.rect.height > 1f ? sprite.pivot.y / sprite.rect.height : 0.5f;
            _dedicatedVisual = visual.transform;
            _dedicatedRenderer = sr;
            _dedicatedScreenUpOffset = pivotNormY < 0.25f ? 0f : DedicatedVisibleHeight * 0.5f;
            _dedicatedVisual.localPosition = new Vector3(0f, 0.1f, 0f);
            RefreshDedicatedVisual(Camera.main);
        }

        private void CreateBubbleLabel()
        {
            _bubbleRoot = new GameObject("SpeechBubble");
            _bubbleRoot.transform.SetParent(transform, false);
            _bubbleRoot.transform.localPosition = new Vector3(0f, HitWorldHeight + 1.55f, 0f);
            WorldLabelBillboard.Attach(_bubbleRoot);

            var bgSprite = Resources.Load<Sprite>(BubbleBgResourceKey);
            if (bgSprite != null)
            {
                var bgGo = new GameObject("BubbleBg");
                bgGo.transform.SetParent(_bubbleRoot.transform, false);
                bgGo.transform.localPosition = Vector3.zero;
                _bubbleBgRenderer = bgGo.AddComponent<SpriteRenderer>();
                _bubbleBgRenderer.sprite = bgSprite;
                _bubbleBgRenderer.drawMode = bgSprite.border.sqrMagnitude > 0f
                    ? SpriteDrawMode.Sliced
                    : SpriteDrawMode.Simple;
                _bubbleBgRenderer.size = new Vector2(8.8f, 2.2f);
                _bubbleBgRenderer.sortingOrder = 10;
            }

            var textGo = new GameObject("BubbleText");
            textGo.transform.SetParent(_bubbleRoot.transform, false);
            textGo.transform.localPosition = new Vector3(0f, bgSprite != null ? 0.14f : 0f, -0.02f);
            _bubbleText = textGo.AddComponent<TextMeshPro>();
            _bubbleText.fontSize = 2.6f;
            _bubbleText.alignment = TextAlignmentOptions.Center;
            _bubbleText.textWrappingMode = TextWrappingModes.Normal;
            _bubbleText.rectTransform.sizeDelta = new Vector2(7.6f, 1.55f);
            _bubbleText.sortingOrder = 11;
            _bubbleText.richText = false;
            if (bgSprite != null)
            {
                _bubbleText.color = new Color(0.15f, 0.23f, 0.19f, 1f);
                _bubbleText.outlineWidth = 0f;
            }
            else
            {
                _bubbleText.color = new Color(1f, 0.96f, 0.82f, 1f);
                _bubbleText.outlineWidth = 0.2f;
                _bubbleText.outlineColor = new Color(0.11f, 0.19f, 0.16f, 0.95f);
            }
            _bubbleText.text = "";
            _bubbleRoot.SetActive(false);
        }

        public void ShowBubble(string text, float durationSeconds = 3.2f)
        {
            if (_bubbleRoot == null || _bubbleText == null || string.IsNullOrWhiteSpace(text)) return;
            string clipped = text.Length > 34 ? text.Substring(0, 34) + "…" : text;
            _bubbleText.text = clipped;
            if (_bubbleBgRenderer != null && _bubbleBgRenderer.drawMode == SpriteDrawMode.Sliced)
            {
                float width = Mathf.Clamp(2.8f + clipped.Length * 0.28f, 5.4f, 9.6f);
                float height = clipped.Length > 18 ? 2.45f : 1.95f;
                _bubbleBgRenderer.size = new Vector2(width, height);
                _bubbleText.rectTransform.sizeDelta = new Vector2(width - 1.1f, height - 0.55f);
            }
            _bubbleRoot.SetActive(true);
            _bubbleHideAt = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
        }

        private void Update()
        {
            if (_bubbleRoot != null && _bubbleRoot.activeSelf && Time.unscaledTime >= _bubbleHideAt)
            {
                _bubbleRoot.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_dedicatedVisual != null && _dedicatedRenderer != null)
                RefreshDedicatedVisual(Camera.main);
        }

        public void RefreshDedicatedVisual(Camera worldCamera)
        {
            if (_dedicatedVisual == null || _dedicatedRenderer == null) return;
            var feet = transform.position;
            var rotation = worldCamera != null ? worldCamera.transform.rotation : Quaternion.Euler(90f, 0f, 0f);
            var screenUp = worldCamera != null ? worldCamera.transform.up : Vector3.forward;
            bool hasDepthOcclusion = MapOcclusionRenderer.TryResolveTopDownDepthY(feet, worldCamera, out float depthY);
            MapOcclusionRenderer.SyncSpriteMaterial(_dedicatedRenderer, worldCamera, ref _dedicatedDefaultMaterial);

            _dedicatedVisual.rotation = rotation;
            var positioned = feet + screenUp * _dedicatedScreenUpOffset;
            _dedicatedVisual.position = hasDepthOcclusion
                ? new Vector3(positioned.x, depthY, positioned.z)
                : new Vector3(positioned.x, feet.y + 0.1f, positioned.z);
            _dedicatedRenderer.sortingOrder = QdaoBoySpriteAnimator.WorldSortingOrder(feet, worldCamera);
        }

        public bool TryGetScreenHitBox(Camera worldCamera, out Vector2 feetScreen, out Vector2 size)
        {
            feetScreen = default;
            size = default;
            if (worldCamera == null) return false;
            var feet = worldCamera.WorldToScreenPoint(FeetPosition);
            var head = worldCamera.WorldToScreenPoint(FeetPosition + worldCamera.transform.up * HitWorldHeight);
            if (feet.z <= 0f || head.z <= 0f) return false;
            float height = Mathf.Abs(head.y - feet.y);
            if (height < 1f) return false;
            feetScreen = new Vector2(feet.x, Mathf.Min(feet.y, head.y));
            size = new Vector2(height * HitWidthPerHeight, height);
            return true;
        }
    }
}
