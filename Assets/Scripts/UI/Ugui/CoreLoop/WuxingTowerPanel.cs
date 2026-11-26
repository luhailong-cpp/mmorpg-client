using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 五行幻境 · 10 层轻肉鸽周常爬塔面板：
    ///   - 左侧 10 层进度阶梯（本周首通位图 + 当前待挑战层）；
    ///   - 中部当前层环境灵脉、已选局内五行符印条、出战/放弃操作；
    ///   - 通关一层后弹出三选一局内五行符印卡片（凡品 / 灵品 / 仙品）；
    ///   - 右侧本周最速流派榜（零付费数值，所有符印仅限本轮爬塔生效）。
    /// </summary>
    public sealed class WuxingTowerPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private GameClient _game;

        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(CoreGameplayUiRoot root, CoreGameplayLoopClient core, GameClient game)
        {
            _root = root;
            _game = game;
            Refresh();
            Hide();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _game?.Season?.RequestWuxingTowerState(force: true);
            PreBattleLoadoutQuickBar.EnsureRequested(_game?.Features);
            Refresh();
        }

        public void Hide() => gameObject.SetActive(false);

        public void Refresh()
        {
            if (_root == null || _game == null) return;
            var season = _game.Season;
            var tower = season?.TowerSnapshot;
            CoreLoopUiArt.Clear(transform);
            CoreLoopUiArt.Frame(transform, "五行幻境 · 十层轻肉鸽周常爬塔", _root.CloseCurrent);

            if (tower == null)
            {
                CoreLoopUiArt.Text("TowerLoading", transform, 400f, 430f, 1760f, 100f,
                    season?.TowerStatePending == true ? "正在同步五行幻境周常进度…" : "暂未读取到五行幻境状态",
                    32f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("TowerRetry", transform, 1070f, 560f, 420f, 66f,
                    "刷新幻境状态", () => season?.RequestWuxingTowerState(true), season?.TowerStatePending != true, true, 25f);
                return;
            }

            bool busy = season.ActionPending;
            int cleared = WuxingExpansionModels.CountClearedFloors(tower.WeeklyClearedMask);
            uint floor = tower.CurrentFloor >= 1u ? tower.CurrentFloor : 1u;
            string leylineName = WuxingExpansionModels.ElementName(tower.CurrentFloorLeylineElement);

            // 顶部摘要条
            CoreLoopUiArt.Card(transform, "TowerSummaryBar", 295f, 240f, 1970f, 76f);
            string bestText = tower.WeeklyBestFloor >= 10u && tower.WeeklyBestRounds > 0u
                ? $"本周全通最速：{tower.WeeklyBestRounds} 回合"
                : $"本周最高：第 {tower.WeeklyBestFloor} 层";
            var leySprite = WuxingExpansionsUiArt.LoadElementIcon(tower.CurrentFloorLeylineElement);
            if (leySprite != null)
                SeasonV1UiArt.CreateFixedIcon(transform, "TowerLeylineIcon", 316f, 256f, 44f, 44f, leySprite, Color.clear);
            CoreLoopUiArt.Text("TowerSummaryLeft", transform, leySprite != null ? 368f : 325f, 252f, 1140f, 50f,
                $"本周首通 {cleared} / {tower.MaxFloors} 层  ·  {bestText}  ·  当前层灵脉：[{leylineName}行]",
                25f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineLeft);
            CoreLoopUiArt.Text("TowerZeroP2wNote", transform, 1520f, 256f, 715f, 42f,
                "局内符印免费三选一 · 出塔自动清空 · 零战力售卖",
                20f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.MidlineRight);

            // 左侧：10 层阶梯（x=295..765）
            CoreLoopUiArt.Card(transform, "FloorLadderCard", 295f, 330f, 470f, 600f);
            CoreLoopUiArt.Text("FloorLadderTitle", transform, 315f, 342f, 430f, 38f,
                "十层试炼阶梯", 24f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            for (uint f = 1u; f <= 10u; ++f)
            {
                float fy = 386f + (f - 1u) * 53f;
                bool isCleared = WuxingExpansionModels.IsFloorCleared(tower.WeeklyClearedMask, f);
                bool isCurrent = tower.RunActive && floor == f;
                var rowColor = isCurrent
                    ? new Color(0.13f, 0.42f, 0.31f, 0.94f)
                    : isCleared
                        ? new Color(0.86f, 0.93f, 0.87f, 0.94f)
                        : new Color(0.95f, 0.93f, 0.86f, 0.92f);
                CoreLoopUiArt.Image("FloorRow" + f, transform, 315f, fy, 430f, 46f, null, rowColor);
                string tag = isCurrent ? "当前挑战" : (isCleared ? "本周已通" : "未首通");
                CoreLoopUiArt.Text("FloorLbl" + f, transform, 332f, fy + 6f, 230f, 34f,
                    $"第 {f} 层", 22f, isCurrent ? CoreLoopUiArt.Ivory : CoreLoopUiArt.Ink, true);
                CoreLoopUiArt.Text("FloorTag" + f, transform, 560f, fy + 8f, 168f, 30f,
                    tag, 19f, isCurrent ? SeasonV1UiArt.BadgeIvoryText : (isCleared ? CoreLoopUiArt.Jade : CoreLoopUiArt.Muted),
                    true, false, TextAlignmentOptions.MidlineRight);
            }

            // 中部：本轮构筑与操作（x=782..1685）
            CoreLoopUiArt.Card(transform, "RunDetailCard", 782f, 330f, 903f, 600f);
            string runHeadline = tower.RunActive
                ? $"进行中 · 第 {floor} 层 · 累计 {tower.RunTotalRounds} 回合 · 阵法：{WuxingExpansionModels.FormationName(season.EquippedFormationId)}"
                : "尚未开启本轮幻境 · 点击下方免费开轮";
            CoreLoopUiArt.Text("RunHeadline", transform, 808f, 346f, 850f, 42f,
                runHeadline, 23f, CoreLoopUiArt.DeepJade, true);

            // 本轮已选局内符印列表
            CoreLoopUiArt.Text("ActiveSigilsHeader", transform, 808f, 400f, 850f, 32f,
                $"本轮已铭刻符印（{tower.ActiveSigilIds.Count} 枚，出塔自动清空）：", 20f, CoreLoopUiArt.Muted, true);
            if (tower.ActiveSigilIds.Count == 0)
            {
                CoreLoopUiArt.Text("NoSigils", transform, 808f, 444f, 850f, 60f,
                    "通关任意一层后，即可从 3 枚随机五行符印中任选 1 枚构筑本轮流派。",
                    21f, CoreLoopUiArt.Muted, false, true);
            }
            else
            {
                for (int i = 0; i < tower.ActiveSigilIds.Count && i < 9; ++i)
                {
                    uint sigilId = tower.ActiveSigilIds[i];
                    WuxingExpansionModels.TryFindSigil(sigilId, out var def);
                    int col = i % 3;
                    int row = i / 3;
                    float sx = 808f + col * 288f;
                    float sy = 440f + row * 106f;
                    // 已持有符印条用紧凑圆角底卡 + 五行图标（立式 420×600 符印卡框仅按原比例用于三选一浮层，不横向压扁）
                    CoreLoopUiArt.Card(transform, "SigilChip" + i, sx, sy, 276f, 96f, selected: def.Rarity >= 3u);
                    var elemSprite = WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                    if (elemSprite != null)
                        SeasonV1UiArt.CreateFixedIcon(transform, "SigilElem" + i, sx + 12f, sy + 14f, 44f, 44f, elemSprite, Color.clear);
                    CoreLoopUiArt.Text("SigilName" + i, transform, sx + (elemSprite != null ? 62f : 14f), sy + 10f, 202f, 30f,
                        $"{def.Name}（{WuxingExpansionModels.RarityLabel(def.Rarity)}）", 17f, CoreLoopUiArt.DeepJade, true);
                    CoreLoopUiArt.Text("SigilDesc" + i, transform, sx + (elemSprite != null ? 62f : 14f), sy + 44f, 202f, 44f,
                        def.Description, 14f, CoreLoopUiArt.Ink, false, true);
                }
            }

            // 战前准备期 1..5 一键换流派快捷条（看到当前层五行灵脉后直接切换抗性/控场/输出整套方案）
            PreBattleLoadoutQuickBar.RenderForFeatures(
                transform, "TowerLoadoutQuickBar", 808f, 760f, 852f, 58f,
                _game?.Features, _game?.Battle?.Phase ?? BattlePhase.None, _root.ShowToast);

            // 底部行动按钮区
            if (!tower.RunActive)
            {
                CoreLoopUiArt.Button("StartTowerRun", transform, 990f, 830f, 480f, 68f,
                    "免费开启新一轮五行幻境", () => season.StartWuxingTowerRun(), !busy, true, 26f);
            }
            else if (tower.PendingOfferSigilIds.Count > 0)
            {
                CoreLoopUiArt.Text("PendingPickHint", transform, 808f, 840f, 850f, 48f,
                    "已通关本层！请在弹出的三选一符印卡中铭刻 1 枚继续登层。", 22f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.Center);
            }
            else
            {
                uint dungeonId = tower.CurrentFloorDungeonId > 0u ? tower.CurrentFloorDungeonId : 101u;
                CoreLoopUiArt.Button("EnterTowerFloor", transform, 830f, 830f, 440f, 68f,
                    $"挑战第 {floor} 层（满血蓝入场）", () =>
                    {
                        _root.CloseCurrent();
                        _game.Battle?.JoinQueue(Match.MatchMode.PveSolo, dungeonId, 0u, 0u, 0u, 0u, isWuxingTower: true);
                    }, !busy, true, 24f);
                CoreLoopUiArt.Button("AbandonTowerRun", transform, 1300f, 830f, 355f, 68f,
                    "放弃本轮（留首通）", () => season.AbandonWuxingTowerRun(), !busy, false, 22f);
            }

            // 右侧：本周最速流派榜（x=1702..2265）
            CoreLoopUiArt.Card(transform, "TowerLeaderboardCard", 1702f, 330f, 563f, 600f);
            CoreLoopUiArt.Text("LeaderboardTitle", transform, 1722f, 344f, 523f, 38f,
                "本周最速流派榜（Top 20）", 24f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            if (tower.WeeklyLeaderboard.Count == 0)
            {
                CoreLoopUiArt.Text("LeaderboardEmpty", transform, 1726f, 420f, 515f, 80f,
                    "本周尚无通关登榜道友，通关第 1 层起即刻入榜！", 20f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
            }
            else
            {
                int count = Mathf.Min(tower.WeeklyLeaderboard.Count, 10);
                for (int i = 0; i < count; ++i)
                {
                    var row = tower.WeeklyLeaderboard[i];
                    float ry = 392f + i * 52f;
                    CoreLoopUiArt.Image("LbRow" + i, transform, 1720f, ry, 527f, 46f, null,
                        i < 3 ? new Color(0.97f, 0.92f, 0.78f, 0.94f) : new Color(0.95f, 0.93f, 0.87f, 0.90f));
                    string pname = string.IsNullOrWhiteSpace(row.PlayerName) ? $"道友{row.PlayerId}" : row.PlayerName;
                    string score = row.BestFloor >= 10u && row.BestRounds > 0u
                        ? $"10层·{row.BestRounds}回合"
                        : $"第{row.BestFloor}层";
                    CoreLoopUiArt.Text("LbName" + i, transform, 1732f, ry + 8f, 250f, 30f,
                        $"#{row.Rank} {pname}", 19f, CoreLoopUiArt.Ink, i < 3);
                    CoreLoopUiArt.Text("LbScore" + i, transform, 1986f, ry + 8f, 248f, 30f,
                        $"{score} · {WuxingExpansionModels.FormationName(row.FormationId)}", 17f, CoreLoopUiArt.DeepJade,
                        true, false, TextAlignmentOptions.MidlineRight);
                }
            }

            // 三选一局内五行符印弹窗（pending_offer_sigil_ids 非空时置顶展示）
            if (tower.RunActive && tower.PendingOfferSigilIds.Count > 0)
                DrawSigilPickOverlay(season, tower, busy);
        }

        private void DrawSigilPickOverlay(MmorpgClient.Game.Season.SeasonClient season, global::WuxingTowerStateSnapshot tower, bool busy)
        {
            var dim = QdaoUguiFactory.CreateImage("SigilPickDim", transform, 0f, 0f, 2560f, 1080f, null);
            dim.color = new Color(0.04f, 0.11f, 0.09f, 0.76f);
            dim.raycastTarget = true;

            CoreLoopUiArt.Card(transform, "SigilPickModal", 520f, 148f, 1520f, 784f);
            CoreLoopUiArt.Text("SigilPickTitle", transform, 560f, 174f, 1440f, 48f,
                $"通关第 {tower.CurrentFloor} 层！请选择 1 枚局内五行符印（出塔清空）",
                30f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);

            // 严格遵守 CONTROL-USAGE.md：三张立式符印卡按 420:600 等比缩放（350×500），内容落在安全区内（左右 >=47px、上 >=120px、下 >=70px）
            const float cardW = 350f;
            const float cardH = 500f;
            const float cy = 244f;
            int count = Mathf.Min(tower.PendingOfferSigilIds.Count, 3);
            float totalW = count * cardW + Mathf.Max(0, count - 1) * 90f;
            float startX = (2560f - totalW) * 0.5f;
            for (int i = 0; i < count; ++i)
            {
                uint sigilId = tower.PendingOfferSigilIds[i];
                WuxingExpansionModels.TryFindSigil(sigilId, out var def);
                float cx = startX + i * (cardW + 90f);
                var frame = WuxingExpansionsUiArt.LoadSigilCardFrame(def.Rarity);
                if (frame != null)
                {
                    var cardImg = QdaoUguiFactory.CreateImage("OfferCard" + i, transform, cx, cy, cardW, cardH, frame, false);
                    cardImg.type = frame.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                    cardImg.preserveAspect = false;
                }
                else
                    CoreLoopUiArt.Card(transform, "OfferCard" + i, cx, cy, cardW, cardH);

                CoreLoopUiArt.Badge(transform, "OfferRarity" + i, cx + 88f, cy + 114f, 174f,
                    $"{WuxingExpansionModels.ElementName(def.Element)}行 · {WuxingExpansionModels.RarityLabel(def.Rarity)}",
                    def.Rarity >= 3u ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade);

                var elemIcon = WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                if (elemIcon != null)
                    SeasonV1UiArt.CreateFixedIcon(transform, "OfferElem" + i, cx + 117f, cy + 162f, 116f, 116f, elemIcon, Color.clear);

                CoreLoopUiArt.Text("OfferName" + i, transform, cx + 46f, cy + 286f, 258f, 38f,
                    def.Name, 25f, CoreLoopUiArt.Ink, true, false, TextAlignmentOptions.Center);
                CoreLoopUiArt.Text("OfferDesc" + i, transform, cx + 48f, cy + 330f, 254f, 94f,
                    def.Description, 18f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);

                uint capturedId = sigilId;
                CoreLoopUiArt.Button("PickSigilBtn" + i, transform, cx + 35f, cy + cardH + 18f, 280f, 58f,
                    "铭刻此符印", () => season.PickWuxingTowerSigil(capturedId), !busy, true, 23f);
            }
        }
    }
}
