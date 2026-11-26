using System;
using System.Collections.Generic;
using Guildpb;
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
        public uint SelectedLeylineId => _selectedLeylineId;

        private readonly RectTransform _root;
        private readonly RectTransform _capsuleRoot;
        private readonly RectTransform _leylineStripRoot;
        private readonly RectTransform _nearbyRoot;
        private readonly RectTransform _targetBarRoot;

        private GuildWarClient _client;
        private uint _sceneConfigId;
        private ulong _myGuildId;
        private ulong _selectedPlayerId;
        private uint _selectedLeylineId;
        private int _bannerDistanceRounded = -1;

        public GuildWarFieldHud(UnityEngine.Transform hudParent)
        {
            _root = QdaoUguiFactory.CreateStretch("GuildWarFieldHud", hudParent, Vector4.zero);
            _root.gameObject.SetActive(false);

            // 预留右上角微信/抖音小游戏胶囊安全区（y=126 起）
            _capsuleRoot = QdaoUguiFactory.CreateRect("GuildWarFieldCapsule", _root, 2046f, 126f, 470f, 76f);
            _leylineStripRoot = QdaoUguiFactory.CreateRect("GuildWarFieldLeylineStrip", _root, 2046f, 208f, 470f, 44f);
            _leylineStripRoot.gameObject.SetActive(false);
            _nearbyRoot = QdaoUguiFactory.CreateRect("GuildWarNearbyEnemies", _root, 2046f, 258f, 470f, 236f);
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
            if (!IsWildWarScene(sceneConfigId))
            {
                _selectedPlayerId = 0;
                _selectedLeylineId = 0;
                _bannerDistanceRounded = -1;
            }
            Render();
        }

        public void UpdateScene(uint sceneConfigId, ulong myGuildId)
        {
            if (_sceneConfigId == sceneConfigId && _myGuildId == myGuildId) return;
            _sceneConfigId = sceneConfigId;
            _myGuildId = myGuildId;
            if (!IsWildWarScene(sceneConfigId))
            {
                _selectedPlayerId = 0;
                _selectedLeylineId = 0;
                _bannerDistanceRounded = -1;
            }
            Render();
        }

        public void UpdateBannerDistance(float distanceMeters)
        {
            int rounded = distanceMeters < 0f ? -1 : Mathf.RoundToInt(distanceMeters);
            if (_bannerDistanceRounded == rounded) return;
            _bannerDistanceRounded = rounded;
            if (IsWildWarScene(_sceneConfigId)) Render();
        }

        public void SelectFieldTarget(ulong playerId)
        {
            if (!IsWildWarScene(_sceneConfigId) || playerId == 0)
            {
                ClearSelection();
                return;
            }
            _selectedPlayerId = playerId;
            _selectedLeylineId = 0;
            Render();
        }

        public void SelectLeylineBanner(uint leylineId)
        {
            if (!IsWildWarScene(_sceneConfigId) || leylineId == 0)
            {
                ClearSelection();
                return;
            }
            _selectedPlayerId = 0;
            _selectedLeylineId = leylineId;
            Render();
        }

        public void ClearSelection()
        {
            if (_selectedPlayerId == 0 && _selectedLeylineId == 0) return;
            _selectedPlayerId = 0;
            _selectedLeylineId = 0;
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
            _selectedLeylineId = 0;
            _bannerDistanceRounded = -1;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Render()
        {
            if (_root == null) return;
            bool visible = _client != null && _myGuildId != 0 && _sceneConfigId != 0;
            _root.gameObject.SetActive(visible);
            if (!visible) return;

            GuildUiArt.Clear(_capsuleRoot);
            GuildUiArt.Clear(_leylineStripRoot);
            GuildUiArt.Clear(_nearbyRoot);
            GuildUiArt.Clear(_targetBarRoot);

            bool wild = IsWildWarScene(_sceneConfigId);
            var ov = _client.Overview;
            ulong nowMs = _client.EstimatedServerNowMs;
            var stance = ov?.MyStance;
            bool peace = stance?.PeaceMode ?? false;
            bool busy = _client.ActionInFlight;

            Card(_capsuleRoot, "FieldCapsuleBg", 0f, 0f, 470f, 76f, PaperSoft);
            Art(_capsuleRoot, "FieldCapsuleStanceIcon", peace ? "badge_stance_peace" : "badge_stance_war", 10f, 10f, 56f, 56f, true);
            string mapName = GuildWarText.SceneName(_sceneConfigId);
            string zoneTag = wild ? (peace ? mapName + " · 免战" : mapName + " · 帮战区") : mapName + " · 安全区";
            Label(_capsuleRoot, "FieldCapsuleZone", zoneTag, 74f, 8f, 198f, 30f, 19f, wild ? CrimsonText : TitleJade);
            Label(_capsuleRoot, "FieldCapsuleSub", wild ? "点异帮玩家/战旗直打" : "安全区内不可开战", 74f, 39f, 198f, 26f, 16f, LabelBrown);

            ActionButton(_capsuleRoot, "FieldCapsuleToggleStance", peace ? "切战意" : "切免战",
                274f, 15f, 94f, 46f, () => _client.SetPeaceMode(!peace),
                primary: peace, enabled: !busy, fontSize: 17f);
            ActionButton(_capsuleRoot, "FieldCapsuleOpenWar", "帮战",
                374f, 15f, 86f, 46f, () => OpenWarWindowRequested?.Invoke(),
                danger: true, fontSize: 18f);

            uint sceneLeylineId = GuildWarLeylineWorldBanner.LeylineIdForScene(_sceneConfigId);
            GuildWarLeylineSnapshot sceneLeyline = FindLeyline(ov, sceneLeylineId);
            var feud = ov?.ActiveFeud;
            _leylineStripRoot.gameObject.SetActive(wild && sceneLeylineId != 0);
            if (wild && sceneLeylineId != 0)
            {
                Card(_leylineStripRoot, "FieldLeylineStripBg", 0f, 0f, 470f, 44f, PaperSoft);
                bool showFeud = feud != null && feud.Status == GuildWarFeudStatus.Active;
                if (showFeud)
                {
                    ArtOrFallback(_leylineStripRoot, "FieldFeudBadge", "badge_feud_edict", "badge_rival_guild", 8f, 6f, 32f, 32f, true);
                    string feudText = $"约战 {feud.IssuerWins}:{feud.TargetWins} · 剩 {GuildWarText.FormatCountdown(feud.EndsAtMs, nowMs)}";
                    Label(_leylineStripRoot, "FieldLeylineStripText", feudText, 46f, 8f, 306f, 28f, 16f, CrimsonText);
                }
                else
                {
                    bool isGold = sceneLeyline != null && (sceneLeyline.IsGoldLeyline || (ov != null && ov.GoldLeylineId == sceneLeylineId));
                    if (isGold)
                        Art(_leylineStripRoot, "FieldLeylineGoldIcon", "badge_gold_leyline", 8f, 6f, 32f, 32f, true);
                    else
                        ArtOrFallback(_leylineStripRoot, "FieldLeylineFlagIcon", "banner_leyline_world_flag", "banner_guild_flag", 10f, 4f, 26f, 36f, true);
                    string distText = _bannerDistanceRounded >= 0 ? $" · {_bannerDistanceRounded}m" : string.Empty;
                    string leyStatus = sceneLeyline == null
                        ? $"{GuildWarText.LeylineName(sceneLeylineId)}{distText}"
                        : sceneLeyline.IsUnclaimed
                            ? $"无主灵脉{distText}"
                            : $"[{sceneLeyline.OwnerGuildName}] {sceneLeyline.BannerHp}/{sceneLeyline.BannerMaxHp}{distText}";
                    Label(_leylineStripRoot, "FieldLeylineStripText", leyStatus, 46f, 8f, 306f, 28f, 16f, isGold ? CrimsonText : TitleJade);
                }
                uint lid = sceneLeylineId;
                ActionButton(_leylineStripRoot, "FieldLeylineFlagBtn", "灵脉旗", 358f, 5f, 104f, 34f,
                    () => SelectLeylineBanner(lid), primary: _selectedLeylineId == lid, fontSize: 16f);
            }

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
                    float textW = t.IsRivalGuild ? 214f : 248f;
                    Label(_nearbyRoot, "NearbyName_" + i, line, 62f, ry + 8f, textW, 28f, 18f, BodyInk);
                    string sub = t.ActiveBountyId != 0 ? $"追杀赏金 {t.ActiveBountyRewardSilver}"
                        : t.InBattle ? "激战中（可观战）"
                        : t.PeaceMode ? "和平免战" : "可点击直接开战";
                    Label(_nearbyRoot, "NearbySub_" + i, sub, 62f, ry + 36f, textW, 24f, 15f, LabelBrown);
                    if (t.IsRivalGuild)
                        Art(_nearbyRoot, "NearbyRivalBadge_" + i, "badge_rival_guild", 282f, ry + 18f, 32f, 32f, true);

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
            bool showLeylineBar = wild && picked == null && _selectedLeylineId != 0;
            _targetBarRoot.gameObject.SetActive((wild && picked != null) || showLeylineBar);
            if (wild && picked != null)
            {
                Card(_targetBarRoot, "PickedTargetBg", 0f, 0f, 840f, 108f, PaperFill);
                if (picked.ActiveBountyId != 0) Art(_targetBarRoot, "PickedBadge", "badge_bounty_target", 18f, 18f, 72f, 72f, true);
                else if (picked.InBattle) Art(_targetBarRoot, "PickedBadge", "badge_spectate_swords", 18f, 18f, 72f, 72f, true);
                else if (picked.Streak >= 3) Art(_targetBarRoot, "PickedBadge", StreakBadgeKey(picked.Streak), 18f, 18f, 72f, 72f, true);
                else Art(_targetBarRoot, "PickedBadge", picked.PeaceMode ? "badge_stance_peace" : "badge_stance_war", 18f, 18f, 72f, 72f, true);

                float pickedTextW = picked.IsRivalGuild ? 360f : 412f;
                Label(_targetBarRoot, "PickedTargetTitle",
                    $"[{picked.GuildName}] {picked.PlayerName}" + (picked.IsRivalGuild ? " · 本周宿敌 (+20%)" : string.Empty),
                    104f, 14f, pickedTextW, 38f, 24f, TitleJade);
                string pickedSub = picked.ActiveBountyId != 0
                    ? $"江湖追杀令目标 · 悬赏 {picked.ActiveBountyRewardSilver} 银两"
                    : picked.InBattle ? "正在进行帮战对局，可实时观战"
                    : picked.PeaceMode ? "对方已开启和平免战"
                    : "野外任意异帮直打 · 0 秒进战零战损";
                Label(_targetBarRoot, "PickedTargetSub", pickedSub, 104f, 56f, pickedTextW, 34f, 19f, LabelBrown);
                if (picked.IsRivalGuild)
                    Art(_targetBarRoot, "PickedRivalBadge", "badge_rival_guild", 470f, 30f, 48f, 48f, true);

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
            else if (showLeylineBar)
            {
                var ley = FindLeyline(ov, _selectedLeylineId);
                bool ally = ley != null && _myGuildId != 0 && ley.OwnerGuildId == _myGuildId;
                bool isGold = ley != null && (ley.IsGoldLeyline || (ov != null && ov.GoldLeylineId == _selectedLeylineId));
                Card(_targetBarRoot, "PickedLeylineBg", 0f, 0f, 840f, 108f, PaperFill);
                ArtOrFallback(_targetBarRoot, "PickedLeylineIcon", "banner_leyline_world_flag", "banner_guild_flag", 18f, 12f, 56f, 84f, true);
                if (isGold)
                    Art(_targetBarRoot, "PickedLeylineGoldBadge", "badge_gold_leyline", 412f, 30f, 48f, 48f, true);

                string leyTitle = (ley != null && !string.IsNullOrEmpty(ley.Name) ? ley.Name : GuildWarText.LeylineName(_selectedLeylineId))
                    + (isGold ? " · 1.5x旺脉" : string.Empty);
                string leySub = ley == null
                    ? "点击立即切入该灵脉争夺战（0 秒直打）"
                    : ley.IsUnclaimed
                        ? "无主灵脉 · 击败守卫即可插旗占领"
                        : ley.ShieldUntilMs > nowMs
                            ? $"占领帮：[{ley.OwnerGuildName}] · 护盾 {GuildWarText.FormatCountdown(ley.ShieldUntilMs, nowMs)}"
                            : $"占领帮：[{ley.OwnerGuildName}] · 旗势 {ley.BannerHp}/{ley.BannerMaxHp} · 守旗 {ley.GarrisonCount}/5";
                float leyTextW = isGold ? 314f : 368f;
                Label(_targetBarRoot, "PickedLeylineTitle", leyTitle, 88f, 14f, leyTextW, 38f, 23f, TitleJade);
                Label(_targetBarRoot, "PickedLeylineSub", leySub, 88f, 56f, leyTextW, 34f, 18f, LabelBrown);

                uint lid = _selectedLeylineId;
                ActionButton(_targetBarRoot, "PickedLeylineSiegeBtn", ally ? "驰援守旗" : "攻打灵脉", 472f, 24f, 150f, 60f,
                    () => { ClearSelection(); _client.StartLeylineSiege(lid); },
                    danger: !ally, primary: ally, enabled: !busy, fontSize: 21f);
                ulong watchId = ley?.FeaturedWatchBattleId ?? 0UL;
                ActionButton(_targetBarRoot, "PickedLeylineWatchBtn", watchId != 0 ? "观战" : "总览", 630f, 24f, 96f, 60f,
                    () => { ClearSelection(); if (watchId != 0) WatchBattleRequested?.Invoke(watchId); else OpenWarWindowRequested?.Invoke(); },
                    primary: watchId != 0, fontSize: 20f);
                ActionButton(_targetBarRoot, "PickedLeylineCloseBtn", "取消", 734f, 24f, 88f, 60f, ClearSelection, fontSize: 20f);
            }
        }

        private static GuildWarLeylineSnapshot FindLeyline(GuildWarOverviewSnapshot ov, uint leylineId)
        {
            if (ov == null || leylineId == 0) return null;
            for (int i = 0; i < ov.Leylines.Count; i++)
                if (ov.Leylines[i].LeylineId == leylineId) return ov.Leylines[i];
            return null;
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
