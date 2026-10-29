using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.UI.Ugui.Battle;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>独立于可隐藏挂载点的宿主；页签和折叠不影响订阅与任务同步。</summary>
    public sealed class QuestHudRoot : MonoBehaviour
    {
        public static QuestHudRoot Instance { get; private set; }
        public const float RefreshIntervalSeconds = 10;
        private PlayerFeaturesClient _client;
        private RectTransform _mount;
        private QuestHudContent _content;
        private ulong _playerId;
        private object _connectionIdentity;
        private float _nextRefresh;
        private bool _wasInBattle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var host = new GameObject("[QuestHud]");
            DontDestroyOnLoad(host); host.AddComponent<QuestHudRoot>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }
        private void Update()
        {
            EnsureMounted();
            var game = AppBootstrap.Instance?.GameClient;
            bool ready = game != null && game.InGame && game.IsGateReady && game.PlayerId != 0;
            TickSession(ready ? game.Features : null, ready ? game.PlayerId : 0,
                BattleUiRoot.Instance?.IsBattleLayerVisible ?? false, Time.unscaledTime, ready ? game.GateConnectionIdentity : null);
        }
        /// <summary>幂等消费07当前挂载点；壳被销毁或重建时释放旧内容并重新挂载。</summary>
        public void EnsureMounted()
        {
            var mount = WorldSideHudRoot.Instance?.QuestMount;
            if (_mount != mount || (_content != null && _content.Root == null))
            {
                _content?.Dispose(); _content = null; _mount = mount;
                if (mount != null)
                {
                    _content = new QuestHudContent(mount);
                    _content.DetailsRequested += (scope, id) => GameplayUiRoot.Instance?.OpenMission(scope, id);
                    _content.ListRequested += () => GameplayUiRoot.Instance?.OpenMissions();
                    _content.RefreshRequested += Refresh;
                }
                Changed();
            }
        }
        /// <summary>主线程会话/时钟接缝；隐藏页与战斗仍同步，测试无需建立真实账号。</summary>
        public void TickSession(PlayerFeaturesClient client, ulong playerId, bool inBattle, float now, object connectionIdentity = null)
        {
            if (playerId == 0) client = null;
            bool changedSession = !ReferenceEquals(_client, client) || _playerId != playerId || !ReferenceEquals(_connectionIdentity, connectionIdentity);
            if (changedSession)
            {
                if (_client != null) _client.OnChanged -= Changed;
                _client = client; _playerId = playerId; _connectionIdentity = connectionIdentity; _nextRefresh = 0;
                _content?.SetData(null, false, null, false, null);
                if (_client != null)
                {
                    _client.OnChanged += Changed;
                    // 即使旧角色读取仍在途，也让Client先核对主体并清除旧快照。
                    RefreshAt(now);
                }
                Changed();
            }
            if (_wasInBattle && !inBattle) _nextRefresh = 0;
            _wasInBattle = inBattle;
            if (_client != null && now >= _nextRefresh && !_client.MissionsLoading && !_client.BusyMissionAction)
                RefreshAt(now);
        }
        private void Refresh()
            => RefreshAt(Time.unscaledTime);
        private void RefreshAt(float now)
        {
            if (_client == null) return;
            _nextRefresh = now + RefreshIntervalSeconds;
            _client.RequestMissions();
        }
        private void Changed()
        {
            _content?.SetData(_client?.Missions, _client?.MissionsLoading ?? false, _client?.MissionsError,
                _client?.BusyMissionAction ?? false, _client?.MissionTracking);
        }
        private void OnDestroy()
        {
            if (_client != null) _client.OnChanged -= Changed;
            _content?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
