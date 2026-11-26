using MmorpgClient.Game;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 主城总览 HUD（对齐 01-main-city-hub.png 留空中部构图）：
    /// 左上三项权威进度（避开顶部定位与右上导航/胶囊）、右侧四项紧凑入口条、右下四个非数值预览位；
    /// 屏幕中央完全透明留空不挡主角与大世界，且支持一键收起/展开。
    /// </summary>
    public sealed class CoreMainCityHubPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private CoreGameplayLoopClient _core;
        private GameClient _game;
        private bool _afkStateKnown;
        private bool _afkActive;
        private bool _collapsed;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _core = core;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show() { if (IsVisible) return; gameObject.SetActive(true); Refresh(); }
        public void Hide() => gameObject.SetActive(false);

        public void Tick()
        {
            var client = AfkPassUiRoot.Instance?.Client;
            bool known = client != null && client.Ready && client.Info != null;
            bool active = known && client.Active;
            if (known == _afkStateKnown && active == _afkActive) return;
            Refresh();
        }

        public void Refresh()
        {
            if (_root == null || _core == null) return;
            CoreLoopUiArt.Clear(transform);
            var afkClient = AfkPassUiRoot.Instance?.Client;
            _afkStateKnown = afkClient != null && afkClient.Ready && afkClient.Info != null;
            _afkActive = _afkStateKnown && afkClient.Active;
            var view = _core.BuildMainCityHub(hasAfkPass: _afkActive);
            bool materialReady = _game?.Features?.EquipmentMaterialReward != null;
            var rankedProfile = _game?.Ranked?.Profile;
            var seasonState = _game?.Season?.Snapshot;
            var journalState = _game?.TrialJournal?.Snapshot;
            bool seasonJournalReady = seasonState?.Available == true && journalState?.Available == true;

            // 顶部铭牌与收起键：落在 y=138..188（避开 WorldHudLayout.Location y=32..128 与右上导航 x>=1420）
            CoreLoopUiArt.Image("HubBrandPlate", transform, 40, 138, 360, 48,
                null, new Color(0.11f, 0.34f, 0.27f, 0.94f));
            CoreLoopUiArt.Image("HubBrandIcon", transform, 48, 142, 40, 40, "badge", Color.clear);
            CoreLoopUiArt.Text("HubBrandTitle", transform, 94, 142, 294, 40,
                "五行奇谈 · 主城总览", 24, CoreLoopUiArt.Ivory, true);
            CoreLoopUiArt.Button("HubCollapseToggle", transform, 412, 138, 118, 48,
                _collapsed ? "展开" : "收起", () => { _collapsed = !_collapsed; Refresh(); },
                true, false, 20);

            if (_collapsed) return;

            // 左上三项权威进度（x=40..1360, y=194..312；留空中央与右侧）
            for (int i = 0; i < 3; ++i)
                CoreLoopUiArt.Image("ProgressStrip" + i, transform, 40 + i * 444, 194, 430, 118,
                    "hud-strip", new Color(.94f, .92f, .85f, .95f));
            DrawProgress("Materials", 56,
                materialReady ? view.MaterialProgressLabel : "材料奖励进度",
                materialReady ? view.MaterialProgressValue : "同步中");
            DrawProgress("FormalPvp", 500, view.FormalPvpLabel,
                rankedProfile == null ? "同步中" : rankedProfile.CurrentProgress == null
                    ? "暂无赛季进度" : view.FormalPvpValue);
            DrawProgress("SeasonJournal", 944, view.SeasonAndJournalLabel,
                seasonJournalReady ? view.SeasonAndJournalValue
                    : seasonState == null || journalState == null ? "同步中" : "资料暂不可用");
            if (view.ClaimableMaterialUnits > 0)
                CoreLoopUiArt.Button("ClaimMaterials", transform, 314, 266, 144, 38,
                    "去领取", _root.OpenMaterialClaim, true, false, 18);
            SeasonV1UiArt.CreateFixedIcon(transform, "SeasonSealHubIcon", 1152f, 265f, 40f, 40f,
                SeasonV1UiArt.LoadHudSealIcon(), Color.clear);
            CoreLoopUiArt.Button("SeasonJournalOpen", transform, 1196, 266, 160, 38,
                "查看赛季印", _root.OpenSeasonSeal, true, false, 18);

            // 右侧四项紧凑核心入口（x=2168..2528, y=210..636；位于 WorldSideHud 1704..2144 右侧，中部完全透空）
            DrawEntry("BossTrial", 210, "首领试炼", "1–5 人 · 看懂机制再出手", "icon_hub_boss", _root.OpenBossTrials);
            DrawEntry("FormalPvp", 318, "人宠会武", "本人真实养成 · 正式排位", "icon_hub_pvp", _root.OpenFormalPvp);
            DrawEntry("SeasonRotation", 426, "首季轮换", "免费人宠 · 轻松换玩法", "icon_hub_rotation", _root.OpenRotations);
            DrawEntry("EquipmentPets", 534, "装备／灵宠", "养成与配装 · 随时继续", "icon_hub_equip_pet", _root.OpenEquipment);
            CoreLoopUiArt.Button("OpenPets", transform, 2434, 588, 82, 34,
                "灵宠", _root.OpenPets, true, false, 18);
            DrawEntry("WuxingTower", 642, "五行幻境", "十层肉鸽符印 · 免费阵法", "icon_hub_rotation", _root.OpenWuxingTower);
            CoreLoopUiArt.Button("OpenWuxingFormations", transform, 2178, 744, 170, 38,
                "五行阵法", _root.OpenWuxingFormations, true, false, 18);
            CoreLoopUiArt.Button("OpenCustomDuelWide", transform, 2358, 744, 170, 38,
                "道友切磋坊", () => _root.OpenCustomDuel(), true, false, 18);

            // 右下四个低打扰零卖数值预留位（x=1100..2528, y=884..1038；位于 WorldSideHud 底边 848 之下）
            CoreLoopUiArt.Badge(transform, "SpendHeaderBadge", 2216, 836, 312,
                "首测预览 · 未开直充", CoreLoopUiArt.Warning);
            var slots = view.SmallSpendSlots;
            if (slots == null) return;
            for (int i = 0; i < slots.Count && i < 4; ++i)
            {
                var slot = slots[i];
                if (slot == null) continue;
                float x = 1100 + i * 360;
                CoreLoopUiArt.Card(transform, "SpendSlot" + i, x, 884, 348, 152);
                CoreLoopUiArt.Text("SpendTitle" + i, transform, x + 16, 894, 316, 34,
                    slot.Title, 22, CoreLoopUiArt.Ink, true);
                string subtitle = slot.SlotKey == "convenience_loadouts"
                    ? (_afkStateKnown
                        ? (_afkActive ? "省心托管已生效；槽位 3→6 预览"
                            : "离线托管便利与槽位 3→6 预览")
                        : "省心卡待同步；槽位 3→6 预览")
                    : slot.Subtitle;
                CoreLoopUiArt.Text("SpendSubtitle" + i, transform, x + 16, 930, 316, 54,
                    subtitle, 17, CoreLoopUiArt.Muted, false, true);
                var slotKey = slot.SlotKey;
                CoreLoopUiArt.Button("SpendOpen" + i, transform, x + 216, 990, 118, 36,
                    "查看", () => OpenSpend(slotKey), true, false, 18);
                CoreLoopUiArt.Text("SpendZeroStat" + i, transform, x + 16, 994, 192, 28,
                    slot.PaymentEnabled ? slot.StatusBadge : "零战力售卖", 16, CoreLoopUiArt.Warning, true);
            }
        }

        private void DrawProgress(string key, float x, string label, string value)
        {
            CoreLoopUiArt.Text(key + "Label", transform, x, 204, 398, 30,
                label, 20, CoreLoopUiArt.Muted, true);
            CoreLoopUiArt.Text(key + "Value", transform, x, 236, 398, 42,
                value, 24, CoreLoopUiArt.DeepJade, true, true);
        }

        private void DrawEntry(string key, float y, string title, string subtitle, string iconKey, System.Action open)
        {
            const float x = 2168f;
            const float width = 360f;
            const float height = 96f;
            CoreLoopUiArt.Card(transform, key + "Card", x, y, width, height);
            var icon = CoreLoopUiArt.LoadV2(iconKey);
            if (icon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, key + "Icon", x + 15, y + 20, 56, 56, icon, Color.clear);
            else
                CoreLoopUiArt.Image(key + "Icon", transform, x + 14, y + 18, 58, 58,
                    "badge", new Color(.82f, .9f, .82f, .8f));
            CoreLoopUiArt.Text(key + "Title", transform, x + 82, y + 12, 184, 38,
                title, 26, CoreLoopUiArt.DeepJade, true);
            CoreLoopUiArt.Text(key + "Summary", transform, x + 82, y + 50, 184, 36,
                subtitle, 16, CoreLoopUiArt.Muted, false, true);
            CoreLoopUiArt.Button(key + "Open", transform, x + 266, y + 12, 82, 38,
                "进入", open, true, true, 20);
        }

        private void OpenSpend(string slotKey)
        {
            switch (slotKey)
            {
                case "wardrobe": _root.OpenSpendPreview(slotKey); break;
                case "season_journal": _root.OpenTrialJournal(); break;
                case "convenience_loadouts": _root.OpenSpendPreview(slotKey); break;
                case "material_market": _root.OpenSpendPreview(slotKey); break;
            }
        }
    }
}
