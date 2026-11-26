using System;
using MmorpgClient.Game.Guild;
using MmorpgClient.World;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Guild
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;
    /// <summary>
    /// 野外三图（八景岛=2 / 碧渊水宫=5 / 月汐湾=3）3D 灵脉战旗实景交互实体：
    /// - 优先加载 V2.5 独立切片 banner_leyline_world_flag，未落盘前自动回退到 banner_guild_flag；
    /// - 挂载 BoxCollider 供野外点击射线命中后唤起底栏灵脉旗台动作条；
    /// - 复用项目既有 WorldNameplate + WorldLabelBillboard 与 WorldSortingOrder 深度排序，无线程/阻塞依赖。
    /// </summary>
    public sealed class GuildWarLeylineWorldBanner
    {
        private const float BannerWorldHeight = 6.5f;
        private static readonly Vector3 DefaultOffsetFromArrival = new Vector3(5.5f, 0f, 4.5f);
        private static readonly Color EnemyNameColor = new Color(1f, 0.72f, 0.62f);

        public uint ActiveSceneConfigId => _activeSceneConfigId;
        public uint ActiveLeylineId => _activeLeylineId;
        public GameObject RootObject => _root;
        public bool IsActive => _root != null && _root.activeSelf && _activeLeylineId != 0;

        private GameObject _root;
        private Transform _spriteTransform;
        private SpriteRenderer _spriteRenderer;
        private SpriteRenderer _goldBadgeRenderer;
        private TMP_Text _nameplate;
        private uint _activeSceneConfigId;
        private uint _activeLeylineId;
        private bool _anchoredFromPlayer;
        private string _lastLabelText;

        public static uint LeylineIdForScene(uint sceneConfigId) => sceneConfigId switch
        {
            2u => 1u, // 八景岛
            5u => 2u, // 碧渊水宫
            3u => 3u, // 月汐湾
            _ => 0u,
        };

        public void Sync(
            uint sceneConfigId,
            GuildWarLeylineSnapshot leyline,
            ulong myGuildId,
            ulong serverNowMs,
            bool isGold,
            Vector3? arrivalAnchorHint)
        {
            uint leylineId = LeylineIdForScene(sceneConfigId);
            if (leylineId == 0)
            {
                Hide();
                return;
            }

            bool sceneChanged = _activeSceneConfigId != sceneConfigId || _activeLeylineId != leylineId;
            if (_root == null)
            {
                BuildVisual();
                sceneChanged = true;
            }

            if (sceneChanged)
            {
                _activeSceneConfigId = sceneConfigId;
                _activeLeylineId = leylineId;
                _anchoredFromPlayer = arrivalAnchorHint.HasValue;
                _root.transform.position = (arrivalAnchorHint ?? Vector3.zero) + DefaultOffsetFromArrival;
            }
            else if (!_anchoredFromPlayer && arrivalAnchorHint.HasValue)
            {
                _anchoredFromPlayer = true;
                _root.transform.position = arrivalAnchorHint.Value + DefaultOffsetFromArrival;
            }

            if (!_root.activeSelf)
                _root.SetActive(true);

            if (_goldBadgeRenderer != null)
                _goldBadgeRenderer.gameObject.SetActive(isGold);

            string label = BuildNameplateText(leylineId, leyline, serverNowMs, isGold);
            if (_nameplate != null)
            {
                if (!string.Equals(_lastLabelText, label, StringComparison.Ordinal))
                {
                    _nameplate.text = label;
                    _lastLabelText = label;
                }
                _nameplate.color = ResolveNameplateColor(leyline, myGuildId);
            }
        }

        public void Tick(float unscaledTimeSec)
        {
            if (!IsActive || _spriteTransform == null) return;
            var cam = Camera.main;
            float swayDeg = Mathf.Sin(unscaledTimeSec * 2.2f) * 2.5f;
            if (cam != null)
            {
                _spriteTransform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, swayDeg);
                int order = QdaoBoySpriteAnimator.WorldSortingOrder(_root.transform.position, cam);
                if (_spriteRenderer != null) _spriteRenderer.sortingOrder = order;
                if (_goldBadgeRenderer != null)
                {
                    _goldBadgeRenderer.transform.rotation = cam.transform.rotation;
                    _goldBadgeRenderer.sortingOrder = order + 1;
                }
            }
            else
            {
                _spriteTransform.localRotation = Quaternion.Euler(0f, 0f, swayDeg);
            }
        }

        public bool MatchesHit(Transform hitTransform)
        {
            if (!IsActive || hitTransform == null) return false;
            return hitTransform == _root.transform || hitTransform.IsChildOf(_root.transform);
        }

        public float DistanceTo(Vector3 worldPos) =>
            IsActive ? Vector3.Distance(_root.transform.position, worldPos) : -1f;

        public void Hide()
        {
            _activeSceneConfigId = 0u;
            _activeLeylineId = 0u;
            _anchoredFromPlayer = false;
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
        }

        public void Dispose()
        {
            _activeSceneConfigId = 0u;
            _activeLeylineId = 0u;
            _anchoredFromPlayer = false;
            _lastLabelText = null;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
            _root = null;
            _spriteTransform = null;
            _spriteRenderer = null;
            _goldBadgeRenderer = null;
            _nameplate = null;
        }

        private void BuildVisual()
        {
            _root = new GameObject("GuildWarLeylineWorldBanner");

            var spriteGo = new GameObject("sprite");
            spriteGo.transform.SetParent(_root.transform, false);
            _spriteTransform = spriteGo.transform;
            _spriteRenderer = spriteGo.AddComponent<SpriteRenderer>();
            var flagSprite = GuildWarUiArt.LoadOrFallback("banner_leyline_world_flag", "banner_guild_flag");
            _spriteRenderer.sprite = flagSprite;
            float spriteH = flagSprite != null && flagSprite.bounds.size.y > 0.01f ? flagSprite.bounds.size.y : 1f;
            float uniformScale = BannerWorldHeight / spriteH;
            _spriteTransform.localScale = new Vector3(uniformScale, uniformScale, 1f);
            _spriteTransform.localPosition = new Vector3(0f, BannerWorldHeight * 0.5f, 0f);

            var collider = _root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, BannerWorldHeight * 0.5f, 0f);
            collider.size = new Vector3(4.5f, BannerWorldHeight, 4.5f);

            var goldGo = new GameObject("gold_badge");
            goldGo.transform.SetParent(_root.transform, false);
            _goldBadgeRenderer = goldGo.AddComponent<SpriteRenderer>();
            var goldSprite = GuildWarUiArt.Load("badge_gold_leyline");
            _goldBadgeRenderer.sprite = goldSprite;
            float goldH = goldSprite != null && goldSprite.bounds.size.y > 0.01f ? goldSprite.bounds.size.y : 1f;
            float goldScale = 1.6f / goldH;
            goldGo.transform.localScale = new Vector3(goldScale, goldScale, 1f);
            goldGo.transform.localPosition = new Vector3(0f, BannerWorldHeight + 0.9f, 0f);
            goldGo.SetActive(false);

            _nameplate = WorldNameplate.Create(_root.transform, string.Empty, WorldNameplate.NpcColor);
            if (_nameplate != null)
                WorldLabelBillboard.Attach(_nameplate.gameObject);
        }

        private static string BuildNameplateText(
            uint leylineId,
            GuildWarLeylineSnapshot leyline,
            ulong serverNowMs,
            bool isGold)
        {
            string title = leyline != null && !string.IsNullOrEmpty(leyline.Name)
                ? leyline.Name
                : GuildWarText.LeylineName(leylineId);
            if (leyline == null)
                return title + (isGold ? " · 1.5x旺脉" : " · 点击旗台开战");

            string owner = leyline.IsUnclaimed ? "无主灵脉" : "[" + leyline.OwnerGuildName + "]";
            string hp = leyline.ShieldUntilMs > serverNowMs
                ? "护盾 " + GuildWarText.FormatCountdown(leyline.ShieldUntilMs, serverNowMs)
                : $"旗势 {leyline.BannerHp}/{leyline.BannerMaxHp}";
            string tag = isGold ? " · 1.5x旺脉" : leyline.UnderSiege ? " · 正被踢馆!" : string.Empty;
            return $"{title} · {owner} · {hp}{tag}";
        }

        private static Color ResolveNameplateColor(GuildWarLeylineSnapshot leyline, ulong myGuildId)
        {
            if (leyline == null || leyline.IsUnclaimed) return WorldNameplate.NpcColor;
            if (myGuildId != 0 && leyline.OwnerGuildId == myGuildId) return WorldNameplate.LocalPlayerColor;
            return EnemyNameColor;
        }
    }
}
