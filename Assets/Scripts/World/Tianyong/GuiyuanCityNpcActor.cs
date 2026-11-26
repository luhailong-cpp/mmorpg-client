using MmorpgClient.Game.CityNpc;
using TMPro;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 归元城 8 位 100% 原创核心 NPC 的大世界表现实体（纯显示、无碰撞体、绝不挡寻路）。
    /// 优先加载 Codex 交付的专属无字立绘（Resources/UI/Ugui/GuiyuanNpcV1/Npcs/npc_{id}）；
    /// 专属立绘未落盘前无缝复用项目自有原创形象目录（QdaoBoySpriteAnimator），零第三方版权素材风险。
    /// </summary>
    public sealed class GuiyuanCityNpcActor : MonoBehaviour
    {
        public const float HitWorldHeight = 9.4f;
        public const float HitWidthPerHeight = 0.52f;
        private const float DedicatedVisibleHeight = 8.4f;

        private CityNpcDefinition _definition;
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
            visual.transform.localPosition = new Vector3(0f, DedicatedVisibleHeight * 0.5f, 0f);
            WorldLabelBillboard.Attach(visual);
        }

        private void CreateBubbleLabel()
        {
            var bubbleGo = new GameObject("SpeechBubble");
            bubbleGo.transform.SetParent(transform, false);
            bubbleGo.transform.localPosition = new Vector3(0f, HitWorldHeight + 1.35f, 0f);
            _bubbleText = bubbleGo.AddComponent<TextMeshPro>();
            _bubbleText.fontSize = 3.2f;
            _bubbleText.alignment = TextAlignmentOptions.Center;
            _bubbleText.color = new Color(1f, 0.96f, 0.82f, 1f);
            _bubbleText.outlineWidth = 0.2f;
            _bubbleText.outlineColor = new Color(0.11f, 0.19f, 0.16f, 0.95f);
            _bubbleText.richText = false;
            _bubbleText.text = "";
            WorldLabelBillboard.Attach(bubbleGo);
            bubbleGo.SetActive(false);
        }

        public void ShowBubble(string text, float durationSeconds = 3.2f)
        {
            if (_bubbleText == null || string.IsNullOrWhiteSpace(text)) return;
            _bubbleText.text = text.Length > 34 ? text.Substring(0, 34) + "…" : text;
            _bubbleText.gameObject.SetActive(true);
            _bubbleHideAt = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
        }

        private void Update()
        {
            if (_bubbleText != null && _bubbleText.gameObject.activeSelf && Time.unscaledTime >= _bubbleHideAt)
            {
                _bubbleText.gameObject.SetActive(false);
            }
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
