using System;
using Match;
using MmorpgClient.Game.Tournament;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Tournament
{
    /// <summary>
    /// 《群仙问道赛》V2 模块 B · 天墉城冠军玉座锚点与观道致敬入口：
    /// - 严格遵循 UNITY-HANDOFF：不改绘天墉城地图，按现有场景锚点动态挂载独立玉座（champion-pedestal）、
    ///   牌匾（champion-plaque）与 80×80 可点气泡（salute-bubble）；
    /// - 仅当当前位于天墉城主城且服务端下发已诞生总冠军时展示；无冠军、进战斗或离城时自动隐藏清理；
    /// - 点击气泡直接弹出「观道致敬」轻弹窗，绝不在本地伪造已致敬状态。
    /// </summary>
    internal sealed class TournamentChampionStageController
    {
        private const string StageRootName = "[TournamentChampionPedestal]";
        // 位于天墉城中庭开阔广场（PaintingWorldRect 内）
        private static readonly UnityEngine.Vector3 AnchorOffsetFromSpawn = new UnityEngine.Vector3(7.5f, 0f, 5.2f);

        private readonly Action<uint> _onSaluteBubbleClicked;
        private readonly RectTransform _bubbleButtonRect;
        private readonly Button _bubbleButton;
        private readonly TMP_Text _bubbleBadgeText;

        private GameObject _worldRoot;
        private UnityEngine.Transform _boundMapRoot;
        private UnityEngine.Transform _bubbleAnchor;
        private TextMeshPro _plaqueText;
        private uint _boundSeasonId;
        private ulong _boundChampionSquadId;

        public TournamentChampionStageController(UnityEngine.Transform hudParent, Action<uint> onSaluteBubbleClicked)
        {
            _onSaluteBubbleClicked = onSaluteBubbleClicked;

            var sprite = TournamentArtCatalog.Load("v2-b-salute-bubble");
            _bubbleButton = QdaoUguiFactory.CreateArtButton("TianyongSaluteBubble", hudParent,
                0f, 0f, 80f, 80f, sprite, out var img);
            img.preserveAspect = true;
            if (sprite == null) img.color = TournamentArtCatalog.Jade;
            _bubbleButtonRect = (RectTransform)_bubbleButton.transform;
            _bubbleButtonRect.anchorMin = new Vector2(0.5f, 0.5f);
            _bubbleButtonRect.anchorMax = new Vector2(0.5f, 0.5f);
            _bubbleButtonRect.pivot = new Vector2(0.5f, 0.5f);
            _bubbleButton.onClick.AddListener(() => _onSaluteBubbleClicked?.Invoke(_boundSeasonId));

            var badge = TournamentArtCatalog.BorderedCard("SaluteStatePill", _bubbleButtonRect,
                -18f, 76f, 116f, 24f, TournamentArtCatalog.Ivory, TournamentArtCatalog.Gold, 1.2f);
            _bubbleBadgeText = TournamentArtCatalog.Label("Txt", badge, 4f, 0f, 108f, 24f,
                "观道致敬", 13f, TournamentArtCatalog.JadeDeep, TextAlignmentOptions.Center, bold: true);

            _bubbleButtonRect.gameObject.SetActive(false);
        }

        public void Tick(TournamentOverviewSnapshot overview, bool cityHudAvailable)
        {
            var champSeason = overview?.FindLatestChampionSeason();
            if (!cityHudAvailable || champSeason == null || champSeason.ChampionSquadId == 0UL)
            {
                SetVisible(false);
                return;
            }

            var mapRuntime = UnityEngine.Object.FindFirstObjectByType<TianyongMapRuntime>();
            if (mapRuntime == null || mapRuntime.Map?.Root == null || !mapRuntime.Map.Root.activeInHierarchy)
            {
                SetVisible(false);
                return;
            }

            var mapRoot = mapRuntime.Map.Root.transform;
            if (_worldRoot == null || _boundMapRoot != mapRoot)
            {
                DestroyWorldProps();
                BuildWorldProps(mapRoot, mapRuntime.CurrentSpawn + AnchorOffsetFromSpawn);
                _boundMapRoot = mapRoot;
            }

            if (_boundSeasonId != champSeason.SeasonId || _boundChampionSquadId != champSeason.ChampionSquadId)
            {
                _boundSeasonId = champSeason.SeasonId;
                _boundChampionSquadId = champSeason.ChampionSquadId;
                if (_plaqueText != null)
                {
                    string sqName = !string.IsNullOrEmpty(champSeason.ChampionSquadName)
                        ? champSeason.ChampionSquadName : $"战队 #{champSeason.ChampionSquadId}";
                    _plaqueText.text = $"S{champSeason.SeasonId}总冠军·{sqName}";
                }
            }

            if (_worldRoot != null && !_worldRoot.activeSelf)
                _worldRoot.SetActive(true);

            if (_bubbleBadgeText != null)
                _bubbleBadgeText.text = champSeason.SalutedToday ? "今日已观道" : "观道致敬";

            UpdateBillboardAndBubble(mapRuntime.WorldCamera ?? Camera.main);
        }

        public void Dispose()
        {
            DestroyWorldProps();
            if (_bubbleButtonRect != null)
                _bubbleButtonRect.gameObject.SetActive(false);
        }

        private void BuildWorldProps(UnityEngine.Transform mapRoot, UnityEngine.Vector3 worldPos)
        {
            _worldRoot = new GameObject(StageRootName);
            _worldRoot.transform.SetParent(mapRoot, false);
            _worldRoot.transform.position = worldPos;

            var pedSprite = TournamentArtCatalog.Load("v2-b-champion-pedestal");
            if (pedSprite != null)
            {
                var pedGo = new GameObject("ChampionPedestal");
                pedGo.transform.SetParent(_worldRoot.transform, false);
                pedGo.transform.localPosition = new UnityEngine.Vector3(0f, 1.35f, 0f);
                pedGo.transform.localScale = new UnityEngine.Vector3(1.1f, 1.1f, 1.1f);
                var sr = pedGo.AddComponent<SpriteRenderer>();
                sr.sprite = pedSprite;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                sr.receiveShadows = false;
                sr.sortingOrder = 10;
            }

            var plaqueSprite = TournamentArtCatalog.Load("v2-b-champion-plaque");
            var plaqueGo = new GameObject("ChampionPlaque");
            plaqueGo.transform.SetParent(_worldRoot.transform, false);
            plaqueGo.transform.localPosition = new UnityEngine.Vector3(0f, 3.15f, 0f);
            if (plaqueSprite != null)
            {
                plaqueGo.transform.localScale = new UnityEngine.Vector3(0.95f, 0.95f, 0.95f);
                var sr = plaqueGo.AddComponent<SpriteRenderer>();
                sr.sprite = plaqueSprite;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                sr.receiveShadows = false;
                sr.sortingOrder = 11;
            }

            var txtGo = new GameObject("PlaqueText");
            txtGo.transform.SetParent(plaqueGo.transform, false);
            txtGo.transform.localPosition = new UnityEngine.Vector3(0f, 0f, -0.05f);
            _plaqueText = txtGo.AddComponent<TextMeshPro>();
            _plaqueText.fontSize = 2.6f;
            _plaqueText.alignment = TextAlignmentOptions.Center;
            _plaqueText.color = TournamentArtCatalog.Ink;
            _plaqueText.sortingOrder = 12;

            var anchorGo = new GameObject("SaluteBubbleAnchor");
            anchorGo.transform.SetParent(_worldRoot.transform, false);
            anchorGo.transform.localPosition = new UnityEngine.Vector3(0f, 4.35f, 0f);
            _bubbleAnchor = anchorGo.transform;
        }

        private void UpdateBillboardAndBubble(Camera cam)
        {
            if (cam == null || _worldRoot == null || _bubbleAnchor == null)
            {
                if (_bubbleButtonRect != null) _bubbleButtonRect.gameObject.SetActive(false);
                return;
            }

            _worldRoot.transform.rotation = cam.transform.rotation;

            UnityEngine.Vector3 screenPos = cam.WorldToScreenPoint(_bubbleAnchor.position);
            bool inFront = screenPos.z > 0.1f
                && screenPos.x >= 80f && screenPos.x <= Screen.width - 160f
                && screenPos.y >= 100f && screenPos.y <= Screen.height - 120f;
            if (!inFront)
            {
                if (_bubbleButtonRect.gameObject.activeSelf) _bubbleButtonRect.gameObject.SetActive(false);
                return;
            }

            var parentRect = _bubbleButtonRect.parent as RectTransform;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPos, null, out Vector2 localPt))
            {
                _bubbleButtonRect.anchoredPosition = localPt;
                if (!_bubbleButtonRect.gameObject.activeSelf) _bubbleButtonRect.gameObject.SetActive(true);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_worldRoot != null && _worldRoot.activeSelf != visible)
                _worldRoot.SetActive(visible);
            if (_bubbleButtonRect != null && _bubbleButtonRect.gameObject.activeSelf != visible)
                _bubbleButtonRect.gameObject.SetActive(visible);
        }

        private void DestroyWorldProps()
        {
            if (_worldRoot != null)
            {
                UnityEngine.Object.Destroy(_worldRoot);
                _worldRoot = null;
            }
            _boundMapRoot = null;
            _bubbleAnchor = null;
            _plaqueText = null;
            _boundSeasonId = 0u;
            _boundChampionSquadId = 0UL;
        }
    }
}
