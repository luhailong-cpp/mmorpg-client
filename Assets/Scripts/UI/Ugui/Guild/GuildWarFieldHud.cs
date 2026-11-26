using System;
using System.Collections.Generic;
using MmorpgClient.Game.Guild;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Guild.GuildWarUiArt;

namespace MmorpgClient.UI.Ugui.Guild
{
    /// <summary>
    /// 野外三图（八仙岛/碧渊水宫/月汐湾）与天墉城安全区的常驻轻量化帮战现场 HUD：
    /// - 右上角安全区下方展示当前战区、战意/和平免战一键切换与「帮战」直达；
    /// - 野外三图同屏自动列出附近异帮玩家（最多 3 人快捷直打/追杀/观战）；
    /// - 玩家在 3D 野外地图点击任意异帮角色后，于底部弹出选中目标动作条（直接开战 / 执行追杀 / 实时观战）。
    /// </summary>
    public sealed class GuildWarFieldHud
    {
        public event Action OpenWarWindowRequested;
        public event Action<ulong> WatchBattleRequested;

        public RectTransform Root => _root;
        public ulong SelectedTargetPlayerId => _selectedPlayerId;

        private readonly RectTransform _root;
        private readonly RectTransform _capsuleRoot;
        private readonly RectTransform _nearbyRoot;
        private readonly RectTransform _targetBarRoot;

        private GuildWarClient _client;
        private uint _sceneConfigId;
        private ulong _myGuildId;
        private ulong _selectedPlayerId;

        public GuildWarFieldHud(UnityEngine.Transform hudParent)
        {
            _root = QdaoUguiFactory.CreateStretch("GuildWarFieldHud", hudParent, Vector4.zero);
            _root.gameObject.SetActive(false);

            // 预留右上角微信/抖音小游戏胶囊安全区（y=126 起）
            _capsuleRoot = QdaoUguiFactory.CreateRect("GuildWarFieldCapsule", _root, 2046f, 126f, 470f, 76f);
            _nearbyRoot = QdaoUguiFactory.CreateRect("GuildWarNearbyEnemies", _root, 2046f, 212f, 470f, 236f);
            _targetBarRoot = QdaoUguiFactory.CreateRect("GuildWarPickedTargetBar", _root, 860f, 922f, 840f, 108f);
            _targetBarRoot.gameObject.SetActive(false);
        }

        public static bool IsWildWarScene(uint sceneConfigId) =>
            sceneConfigId == 2u || sceneConfigId == 3u || sceneConfigId == 5u;

        public void SetContext(GuildWarClient client, ulong myGuildId, uint sceneConfigId)
        {
            if (_client != null) _client.Changed -= Render;
            _client = client;
            _myGuildId = myGuildId;
            _sceneConfigId = sceneConfigId;
            if (_client != null) _client.Changed += Render;
            if (!IsWildWarScene(sceneConfigId)) _selectedPlayerId = 0;
            Render();
        }

        public void UpdateScene(uint sceneConfigId, ulong myGuildId)
        {
            if (_sceneConfigId == sceneConfigId && _myGuildId == myGuildId) return;
            _sceneConfigId = sceneConfigId;
            _myGuildId = myGuildId;
            if (!IsWildWarScene(sceneConfigId)) _selectedPlayerId = 0;
            Render();
        }

        public void SelectFieldTarget(ulong playerId)
        {
            if (!IsWildWarScene(_sceneConfigId) || playerId == 0)
            {
                ClearSelection();
                return;
            }
            _selectedPlayerId = playerId;
            Render();
        }

        public void ClearSelection()
        {
            if (_selectedPlayerId == 0) return;
            _selectedPlayerId = 0;
            Render();
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Changed -= Render;
                _client = null;
            }
            _selectedPlayerId = 0;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Render()
        {
            if (_root == null) return;
            bool visible = _client != null && _myGuildId != 0 && _sceneConfigId != 0;
            _root.gameObject.SetActive(visible);
            if (!visible) return;

            GuildUiArt.Clear(_capsuleRoot);
            GuildUiArt.Clear(_nearbyRoot);
            GuildUiArt.Clear(_targetBarRoot);

            bool wild = IsWildWarScene(_sceneConfigId);
            var stance = _client.Overview?.MyStance;
            bool peace = stance?.PeaceMode ?? false;
            bool busy = _client.ActionInFlight;

            Card(_capsuleRoot, "FieldCapsuleBg", 0f, 0f, 470f, 76f, PaperSoft);
            Art(_capsuleRoot, "FieldCapsuleStanceIcon", peace ? "badge_stance_peace" : "badge_stance_war", 10f, 10f, 56f, 56f, true);
            string mapName = GuildWarText.SceneName(_sceneConfigId);
            string zoneTag = wild ? (peace ? mapName + " · 免战" : mapName + " · 帮战区") : mapName + " · 安全区";
            Label(_capsuleRoot, "FieldCapsuleZone", zoneTag, 74f, 8f, 198f, 30f, 19f, wild ? CrimsonText : TitleJade);
            Label(_capsuleRoot, "FieldCapsuleSub", wild ? "点异帮玩家可直打" : "安全区内不可开战", 74f, 39f, 198f, 26f, 16f, LabelBrown);

            ActionButton(_capsuleRoot, "FieldCapsuleToggleStance", peace ? "切战意" : "切免战",
                274f, 15f, 94f, 46f, () => _client.SetPeaceMode(!peace),
                primary: peace, enabled: !busy, fontSize: 17f);
            ActionButton(_capsuleRoot, "FieldCapsuleOpenWar", "帮战",
                374f, 15f, 86f, 46f, () => OpenWarWindowRequested?.Invoke(),
                danger: true, fontSize: 18f);

            var enemies = FilterEnemyTargets(_client.FieldTargets, _myGuildId);
            _nearbyRoot.gameObject.SetActive(wild && enemies.Count > 0);
            if (wild && enemies.Count > 0)
            {
                int shown = Math.Min(3, enemies.Count);
                for (int i = 0; i < shown; i++)
                {
                    var t = enemies[i];
                    float ry = i * 76f;
                    Card(_nearbyRoot, "NearbyEnemy_" + i, 0f, ry, 470f, 68f, PaperSoft);
                    if (t.ActiveBountyId != 0) Art(_nearbyRoot, "NearbyMark_" + i, "badge_bounty_target", 8f, ry + 10f, 48f, 48f, true);
                    else if (t.InBattle) Art(_nearbyRoot, "NearbyMark_" + i, "badge_spectate_swords", 8f, ry + 10f, 48f, 48f, true);
                    else if (t.Streak >= 3) Art(_nearbyRoot, "NearbyMark_" + i, StreakBadgeKey(t.Streak), 8f, ry + 10f, 48f, 48f, true);

                    string prefix = string.IsNullOrEmpty(t.GuildName) ? "[异帮]" : $"[{t.GuildName}]";
                    string line = $"{prefix} {t.PlayerName}" + (t.IsRivalGuild ? "·宿敌" : string.Empty);
                    Label(_nearbyRoot, "NearbyName_" + i, line, 62f, ry + 8f, 248f, 28f, 18f, BodyInk);
                    string sub = t.ActiveBountyId != 0 ? $"追杀赏金 {t.ActiveBountyRewardSilver}"
                        : t.InBattle ? "激战中（可观战）"
                        : t.PeaceMode ? "和平免战" : "可点击直接开战";
                    Label(_nearbyRoot, "NearbySub_" + i, sub, 62f, ry + 36f, 248f, 24f, 15f, LabelBrown);

                    if (t.InBattle && t.BattleId != 0)
                    {
                        ulong bid = t.BattleId;
                        ActionButton(_nearbyRoot, "NearbyWatch_" + i, "观战", 322f, ry + 12f, 136f, 44f,
                            () => WatchBattleRequested?.Invoke(bid), primary: true, fontSize: 18f);
                    }
                    else
                    {
                        ulong pid = t.PlayerId;
                        ulong bid = t.ActiveBountyId;
                        ActionButton(_nearbyRoot, "NearbyAttack_" + i, bid != 0 ? "追杀" : "开战", 322f, ry + 12f, 136f, 44f,
                            () => _client.AttackFieldPlayer(pid, bid), danger: true, enabled: !busy && t.CanAttackNow, fontSize: 18f);
                    }
                }
            }

            GuildWarFieldTargetSnapshot picked = FindTarget(enemies, _selectedPlayerId);
            _targetBarRoot.gameObject.SetActive(wild && picked != null);
            if (wild && picked != null)
            {
                Card(_targetBarRoot, "PickedTargetBg", 0f, 0f, 840f, 108f, PaperFill);
                if (picked.ActiveBountyId != 0) Art(_targetBarRoot, "PickedBadge", "badge_bounty_target", 18f, 18f, 72f, 72f, true);
                else if (picked.InBattle) Art(_targetBarRoot, "PickedBadge", "badge_spectate_swords", 18f, 18f, 72f, 72f, true);
                else if (picked.Streak >= 3) Art(_targetBarRoot, "PickedBadge", StreakBadgeKey(picked.Streak), 18f, 18f, 72f, 72f, true);
                else Art(_targetBarRoot, "PickedBadge", picked.PeaceMode ? "badge_stance_peace" : "badge_stance_war", 18f, 18f, 72f, 72f, true);

                Label(_targetBarRoot, "PickedTargetTitle",
                    $"[{picked.GuildName}] {picked.PlayerName}" + (picked.IsRivalGuild ? " · 本周宿敌 (+20%)" : string.Empty),
                    104f, 14f, 412f, 38f, 24f, TitleJade);
                string pickedSub = picked.ActiveBountyId != 0
                    ? $"江湖追杀令目标 · 悬赏 {picked.ActiveBountyRewardSilver} 银两"
                    : picked.InBattle ? "正在进行帮战对局，可实时观战"
                    : picked.PeaceMode ? "对方已开启和平免战"
                    : "野外任意异帮直打 · 0 秒进战零战损";
                Label(_targetBarRoot, "PickedTargetSub", pickedSub, 104f, 56f, 412f, 34f, 19f, LabelBrown);

                if (picked.InBattle && picked.BattleId != 0)
                {
                    ulong bid = picked.BattleId;
                    ActionButton(_targetBarRoot, "PickedWatchBtn", "实时观战", 528f, 24f, 186f, 60f,
                        () => WatchBattleRequested?.Invoke(bid), primary: true, fontSize: 22f);
                }
                else
                {
                    ulong pid = picked.PlayerId;
                    ulong bountyId = picked.ActiveBountyId;
                    ActionButton(_targetBarRoot, "PickedAttackBtn", bountyId != 0 ? "执行追杀" : "直接开战", 528f, 24f, 186f, 60f,
                        () => { ClearSelection(); _client.AttackFieldPlayer(pid, bountyId); },
                        danger: true, enabled: !busy && picked.CanAttackNow, fontSize: 22f);
                }
                ActionButton(_targetBarRoot, "PickedCloseBtn", "取消", 726f, 24f, 96f, 60f, ClearSelection, fontSize: 20f);
            }
        }

        private static List<GuildWarFieldTargetSnapshot> FilterEnemyTargets(IReadOnlyList<GuildWarFieldTargetSnapshot> all, ulong myGuildId)
        {
            var result = new List<GuildWarFieldTargetSnapshot>();
            if (all == null) return result;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.GuildId == 0 || (myGuildId != 0 && t.GuildId == myGuildId)) continue;
                result.Add(t);
            }
            return result;
        }

        private static GuildWarFieldTargetSnapshot FindTarget(List<GuildWarFieldTargetSnapshot> list, ulong playerId)
        {
            if (playerId == 0) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].PlayerId == playerId) return list[i];
            return null;
        }
    }
}
