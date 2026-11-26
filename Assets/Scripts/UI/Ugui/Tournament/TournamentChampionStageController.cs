using MmorpgClient.Game.Tournament;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.UI.Ugui.Tournament
{
    /// <summary>
    /// 《群仙问道赛》V2 模块 B · 天墉城冠军玉座锚点（纯荣誉展示）：
    /// - 严格遵循 UNITY-HANDOFF：不改绘天墉城地图，按现有场景锚点动态挂载独立玉座（champion-pedestal）与牌匾（champion-plaque）；
    /// - 仅当当前位于天墉城主城且服务端下发已诞生总冠军时展示；无冠军、进战斗或离城时自动隐藏清理。
    /// </summary>
    internal sealed class TournamentChampionStageController
    {
        private const string StageRootName = "[TournamentChampionPedestal]";
        // 位于天墉城中庭开阔广场（PaintingWorldRect 内）
        private static readonly UnityEngine.Vector3 AnchorOffsetFromSpawn = new UnityEngine.Vector3(7.5f, 0f, 5.2f);

        private GameObject _worldRoot;
        private UnityEngine.Transform _boundMapRoot;
        private TextMeshPro _plaqueText;
        private uint _boundSeasonId;
        private ulong _boundChampionSquadId;

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
                    string embName = TournamentFormatting.GetEmblemInfo(champSeason.ChampionEmblemId).Name;
                    string mvpSuffix = champSeason.FinalMvpPlayerId != 0UL && !string.IsNullOrEmpty(champSeason.FinalMvpPlayerName)
                        ? $"\n问鼎之星 · {champSeason.FinalMvpPlayerName}"
                        : string.Empty;
                    _plaqueText.text = $"S{champSeason.SeasonId}总冠军·[{embName}]{sqName}{mvpSuffix}";
                }
            }

            if (_worldRoot != null && !_worldRoot.activeSelf)
                _worldRoot.SetActive(true);

            UpdateBillboard(mapRuntime.WorldCamera ?? Camera.main);
        }

        public void Dispose()
        {
            DestroyWorldProps();
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
        }

        private void UpdateBillboard(Camera cam)
        {
            if (cam == null || _worldRoot == null) return;
            _worldRoot.transform.rotation = cam.transform.rotation;
        }

        private void SetVisible(bool visible)
        {
            if (_worldRoot != null && _worldRoot.activeSelf != visible)
                _worldRoot.SetActive(visible);
        }

        private void DestroyWorldProps()
        {
            if (_worldRoot != null)
            {
                UnityEngine.Object.Destroy(_worldRoot);
                _worldRoot = null;
            }
            _boundMapRoot = null;
            _plaqueText = null;
            _boundSeasonId = 0u;
            _boundChampionSquadId = 0UL;
        }
    }
}
