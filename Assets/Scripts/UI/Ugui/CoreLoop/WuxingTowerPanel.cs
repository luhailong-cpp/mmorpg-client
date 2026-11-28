using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.Game.Season;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>
    /// 五行幻境 V2 · 五行奇策轻肉鸽秘境（6 关分岔探索 + 双模式 + 3 同系五行共鸣）：
    ///   - 顶部摘要条：模式标识、本周首通 6 关位图、双模式最速回合与随时中断/跨周保留说明；
    ///   - 上方路线板：从左到右 6 关分岔路线（第 2 关精英/奇遇、第 4 关精英/休整、第 6 关终局首领）；
    ///   - 下方构筑区：五行共鸣横幅、已铭刻五行奇策栏、开局双模式/公平 3×3 预设切换、分岔/战斗行动条；
    ///   - 独立弹层：奇遇事件/秘境休整二选一弹层、战后五行奇策三选一弹层；
    ///   - 右侧分榜：本人流派 Top 20 / 公平试炼 Top 20 物理隔离切页。
    /// </summary>
    public sealed class WuxingTowerPanel : MonoBehaviour
    {
        private CoreGameplayUiRoot _root;
        private GameClient _game;

        private bool _selectedFairPreset;
        private uint _selectedCharPresetId = WuxingExpansionModels.DefaultFairCharPresetId;
        private uint _selectedPetPresetId = WuxingExpansionModels.DefaultFairPetPresetId;
        private bool _showPresetLeaderboard;
        private uint _selectedUpgradeBoonId;

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
            CoreLoopUiArt.Frame(transform, "五行幻境 · 五行奇策轻肉鸽秘境", _root.CloseCurrent);

            if (tower == null)
            {
                CoreLoopUiArt.Text("TowerLoading", transform, 400f, 430f, 1760f, 100f,
                    season?.TowerStatePending == true ? "正在同步五行幻境秘境进度…" : "暂未读取到五行幻境状态",
                    32f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("TowerRetry", transform, 1070f, 560f, 420f, 66f,
                    "刷新幻境状态", () => season?.RequestWuxingTowerState(true), season?.TowerStatePending != true, true, 25f);
                return;
            }

            bool busy = season.ActionPending;
            uint maxNodes = tower.MaxFloors > 0u ? tower.MaxFloors : WuxingExpansionModels.RealmV2MaxNodes;
            uint curNode = tower.CurrentFloor >= 1u ? tower.CurrentFloor : 1u;
            int clearedNodes = WuxingExpansionModels.CountClearedNodes(tower.WeeklyClearedMask, maxNodes);
            string leylineName = WuxingExpansionModels.ElementName(tower.CurrentFloorLeylineElement);

            // 顶部摘要条（x=295..2265, y=232..306）
            CoreLoopUiArt.Card(transform, "TowerSummaryBar", 295f, 232f, 1970f, 74f);
            var leySprite = WuxingExpansionsUiArt.LoadElementIcon(tower.CurrentFloorLeylineElement);
            if (leySprite != null)
                SeasonV1UiArt.CreateFixedIcon(transform, "TowerLeylineIcon", 314f, 247f, 44f, 44f, leySprite, Color.clear);

            string modeTag = tower.RunActive
                ? (tower.UseFreePreset ? "公平 3×3 试炼中" : "本人流派探索中")
                : "随时免费启程";
            string bestSelf = tower.BestRoundsSelf > 0u ? $"{tower.BestRoundsSelf}回合" : "未全通";
            string bestPreset = tower.BestRoundsPreset > 0u ? $"{tower.BestRoundsPreset}回合" : "未全通";
            CoreLoopUiArt.Text("TowerSummaryLeft", transform, leySprite != null ? 366f : 322f, 244f, 1180f, 50f,
                $"本周首通 {clearedNodes}/{maxNodes} 关 · {modeTag} · 当前灵脉：[{leylineName}行] · 最速 本人:{bestSelf} / 公平:{bestPreset}",
                22f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineLeft);
            CoreLoopUiArt.Text("TowerZeroP2wNote", transform, 1550f, 248f, 692f, 42f,
                "随时中断自动保留 · 跨周不清空在途局 · 奇策出图清零",
                19f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.MidlineRight);

            // 上方路线板：6 关分岔探索路线图（x=295..1685, y=316..540）
            DrawRouteBoard(season, tower, curNode, maxNodes, busy);

            // 下方主体卡片：共鸣横幅 + 已选奇策 / 开局设置 + 行动按钮（x=295..1685, y=550..936）
            DrawBuildAndActionCard(season, tower, curNode, maxNodes, busy);

            // 右侧双模式分榜卡片（x=1702..2265, y=316..936）
            DrawLeaderboardCard(tower, maxNodes);

            // 奇遇事件 / 秘境休整二选一弹层
            if (tower.RunActive && tower.PendingOfferSigilIds.Count == 0 && tower.PendingEventKind != WuxingExpansionModels.RealmEventNone)
                DrawEventResolveOverlay(season, tower, busy);

            // 战后三选一五行奇策弹层（最高层级）
            if (tower.RunActive && tower.PendingOfferSigilIds.Count > 0)
                DrawBoonDraftOverlay(season, tower, busy);
        }

        private void DrawRouteBoard(SeasonClient season, global::WuxingTowerStateSnapshot tower, uint curNode, uint maxNodes, bool busy)
        {
            const float bx = 295f;
            const float by = 316f;
            const float bw = 1390f;
            const float bh = 224f;

            var boardBg = WuxingExpansionsUiArt.LoadRealmRouteBoardBg();
            if (boardBg != null)
            {
                var img = QdaoUguiFactory.CreateImage("RouteBoardBg", transform, bx, by, bw, bh, boardBg, false);
                img.type = boardBg.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                img.preserveAspect = false;
            }
            else
            {
                CoreLoopUiArt.Card(transform, "RouteBoardBg", bx, by, bw, bh);
            }

            string routeHeader = tower.RunActive
                ? $"六关分岔秘境 · 当前第 {curNode}/{maxNodes} 关（{WuxingExpansionModels.RealmNodeKindLabel(tower.CurrentNodeKind)}）· 本轮累计 {tower.RunTotalRounds} 回合"
                : "六关分岔路线预览（第 2、4 关二选一：高风险精英拿灵/仙奇策 vs 奇遇/休整稳健成型）";
            CoreLoopUiArt.Text("RouteHeader", transform, bx + 22f, by + 10f, bw - 44f, 30f,
                routeHeader, 20f, CoreLoopUiArt.DeepJade, true);

            // 六关横向排布连接线
            float[] xs = { 340f, 560f, 780f, 1000f, 1220f, 1440f };
            float midY = by + 114f;
            for (int i = 0; i < 5; ++i)
            {
                float lx = xs[i] + 150f;
                float lw = xs[i + 1] - lx + 8f;
                CoreLoopUiArt.Image("RouteLine" + i, transform, lx, midY + 20f, lw, 4f, null,
                    new Color(0.32f, 0.50f, 0.43f, 0.45f));
            }

            bool branch2Right = (tower.RouteChoiceMask & 1u) != 0u;
            bool branch4Right = (tower.RouteChoiceMask & 2u) != 0u;

            // Node 1: 初探普通战
            DrawSingleRouteNode("Node1", xs[0], midY - 12f, 1u, WuxingExpansionModels.RealmNodeNormal, "第1关", "初探普通",
                tower.RunActive && curNode == 1u,
                (tower.RunActive && curNode > 1u) || WuxingExpansionModels.IsNodeCleared(tower.WeeklyClearedMask, 1u, maxNodes));

            // Node 2: 分岔（上路 精英强敌 / 下路 奇遇事件）
            bool n2Picking = tower.RunActive && curNode == 2u && tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeNeedBranch;
            bool n2ActiveElite = tower.RunActive && curNode == 2u && tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeElite;
            bool n2ActiveEvent = tower.RunActive && curNode == 2u && tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeEvent;
            bool n2Passed = tower.RunActive && curNode > 2u;
            DrawBranchPairNode("Node2", xs[1], by + 48f, 2u,
                WuxingExpansionModels.RealmNodeElite, "2上·精英", n2ActiveElite, n2Passed && !branch2Right,
                WuxingExpansionModels.RealmNodeEvent, "2下·奇遇", n2ActiveEvent, n2Passed && branch2Right,
                n2Picking && !busy,
                () => season.ChooseWuxingRealmBranch(WuxingExpansionModels.RealmBranchElite),
                () => season.ChooseWuxingRealmBranch(WuxingExpansionModels.RealmBranchSpecial));

            // Node 3: 灵脉普通战
            DrawSingleRouteNode("Node3", xs[2], midY - 12f, 3u, WuxingExpansionModels.RealmNodeNormal, "第3关", "灵脉试炼",
                tower.RunActive && curNode == 3u,
                (tower.RunActive && curNode > 3u) || WuxingExpansionModels.IsNodeCleared(tower.WeeklyClearedMask, 3u, maxNodes));

            // Node 4: 分岔（上路 精英强敌 / 下路 秘境休整）
            bool n4Picking = tower.RunActive && curNode == 4u && tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeNeedBranch;
            bool n4ActiveElite = tower.RunActive && curNode == 4u && tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeElite;
            bool n4ActiveRest = tower.RunActive && curNode == 4u && tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeRest;
            bool n4Passed = tower.RunActive && curNode > 4u;
            DrawBranchPairNode("Node4", xs[3], by + 48f, 4u,
                WuxingExpansionModels.RealmNodeElite, "4上·精英", n4ActiveElite, n4Passed && !branch4Right,
                WuxingExpansionModels.RealmNodeRest, "4下·休整", n4ActiveRest, n4Passed && branch4Right,
                n4Picking && !busy,
                () => season.ChooseWuxingRealmBranch(WuxingExpansionModels.RealmBranchElite),
                () => season.ChooseWuxingRealmBranch(WuxingExpansionModels.RealmBranchSpecial));

            // Node 5: 险境精英战
            DrawSingleRouteNode("Node5", xs[4], midY - 12f, 5u, WuxingExpansionModels.RealmNodeElite, "第5关", "险境精英",
                tower.RunActive && curNode == 5u,
                (tower.RunActive && curNode > 5u) || WuxingExpansionModels.IsNodeCleared(tower.WeeklyClearedMask, 5u, maxNodes));

            // Node 6: 终局首领
            DrawSingleRouteNode("Node6", xs[5], midY - 12f, 6u, WuxingExpansionModels.RealmNodeBoss, "第6关", "终局首领",
                tower.RunActive && curNode == 6u,
                WuxingExpansionModels.IsNodeCleared(tower.WeeklyClearedMask, 6u, maxNodes));
        }

        private void DrawSingleRouteNode(string key, float x, float y, uint nodeIndex, uint nodeKind, string title, string subtitle,
            bool isCurrent, bool isCleared)
        {
            const float w = 158f;
            const float h = 84f;
            Color bg = isCurrent
                ? new Color(0.13f, 0.44f, 0.33f, 0.96f)
                : isCleared
                    ? new Color(0.84f, 0.93f, 0.86f, 0.95f)
                    : nodeKind == WuxingExpansionModels.RealmNodeBoss
                        ? new Color(0.97f, 0.90f, 0.76f, 0.95f)
                        : new Color(0.95f, 0.93f, 0.87f, 0.94f);
            CoreLoopUiArt.Image(key + "Bg", transform, x, y, w, h, null, bg);

            var icon = WuxingExpansionsUiArt.LoadRealmNodeIcon(nodeKind);
            if (icon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, key + "Icon", x + 8f, y + 16f, 48f, 48f, icon, Color.clear);

            float tx = icon != null ? x + 60f : x + 10f;
            float tw = icon != null ? w - 66f : w - 20f;
            CoreLoopUiArt.Text(key + "Title", transform, tx, y + 10f, tw, 28f,
                isCurrent ? $"→ {title}" : (isCleared ? $"√ {title}" : title),
                18f, isCurrent ? CoreLoopUiArt.Ivory : CoreLoopUiArt.Ink, true);
            CoreLoopUiArt.Text(key + "Sub", transform, tx, y + 42f, tw, 28f,
                subtitle, 16f, isCurrent ? SeasonV1UiArt.BadgeIvoryText : CoreLoopUiArt.DeepJade, true);
        }

        private void DrawBranchPairNode(string key, float x, float topY, uint nodeIndex,
            uint upperKind, string upperLabel, bool upperActive, bool upperCleared,
            uint lowerKind, string lowerLabel, bool lowerActive, bool lowerCleared,
            bool canPickNow, System.Action onPickUpper, System.Action onPickLower)
        {
            const float w = 166f;
            const float h = 72f;
            DrawBranchLane(key + "Up", x, topY, w, h, upperKind, upperLabel, upperActive, upperCleared, canPickNow, onPickUpper);
            DrawBranchLane(key + "Down", x, topY + 86f, w, h, lowerKind, lowerLabel, lowerActive, lowerCleared, canPickNow, onPickLower);
        }

        private void DrawBranchLane(string key, float x, float y, float w, float h, uint nodeKind, string label,
            bool isActive, bool isCleared, bool clickable, System.Action onClick)
        {
            if (clickable)
            {
                CoreLoopUiArt.Button(key + "PickBtn", transform, x, y, w, h,
                    $"选 {label}", onClick, true, nodeKind == WuxingExpansionModels.RealmNodeElite, 18f);
                return;
            }

            Color bg = isActive
                ? new Color(0.13f, 0.44f, 0.33f, 0.96f)
                : isCleared
                    ? new Color(0.84f, 0.93f, 0.86f, 0.95f)
                    : new Color(0.95f, 0.93f, 0.87f, 0.86f);
            CoreLoopUiArt.Image(key + "Bg", transform, x, y, w, h, null, bg);

            var icon = WuxingExpansionsUiArt.LoadRealmNodeIcon(nodeKind);
            if (icon != null)
                SeasonV1UiArt.CreateFixedIcon(transform, key + "Icon", x + 6f, y + 12f, 44f, 44f, icon, Color.clear);

            float tx = icon != null ? x + 54f : x + 10f;
            float tw = icon != null ? w - 60f : w - 20f;
            string prefix = isActive ? "→ " : (isCleared ? "√ " : "");
            CoreLoopUiArt.Text(key + "Lbl", transform, tx, y + 18f, tw, 34f,
                prefix + label, 17f, isActive ? CoreLoopUiArt.Ivory : CoreLoopUiArt.Ink, true);
        }

        private void DrawBuildAndActionCard(SeasonClient season, global::WuxingTowerStateSnapshot tower, uint curNode, uint maxNodes, bool busy)
        {
            const float cx = 295f;
            const float cy = 550f;
            const float cw = 1390f;
            const float ch = 386f;

            CoreLoopUiArt.Card(transform, "RunDetailCard", cx, cy, cw, ch);

            // 顶部：五行奇策共鸣横幅（x=318..1662, y=562..612）
            uint resElem = tower.ActiveResonanceElement != 0u
                ? tower.ActiveResonanceElement
                : WuxingExpansionModels.ComputeActiveResonanceElement(tower.ActiveSigilIds);
            var bannerSprite = WuxingExpansionsUiArt.LoadRealmResonanceBanner();
            if (bannerSprite != null)
            {
                var bannerImg = QdaoUguiFactory.CreateImage("ResonanceBannerBg", transform, cx + 20f, cy + 12f, cw - 40f, 48f, bannerSprite, false);
                bannerImg.type = bannerSprite.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                bannerImg.preserveAspect = false;
            }
            else
            {
                Color bannerColor = resElem != 0u
                    ? new Color(0.95f, 0.87f, 0.65f, 0.96f)
                    : new Color(0.90f, 0.94f, 0.90f, 0.92f);
                CoreLoopUiArt.Image("ResonanceBannerBg", transform, cx + 20f, cy + 12f, cw - 40f, 48f, null, bannerColor);
            }

            string resText = resElem != 0u
                ? $"★ {WuxingExpansionModels.ElementName(resElem)}系奇策共鸣「{WuxingExpansionModels.ResonanceName(resElem)}」已激活：{WuxingExpansionModels.ResonanceDescription(resElem)}"
                : WuxingExpansionModels.FormatResonanceProgressHint(tower.ActiveSigilIds);
            CoreLoopUiArt.Text("ResonanceBannerTxt", transform, cx + 38f, cy + 18f, cw - 76f, 36f,
                resText, 19f, resElem != 0u ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);

            if (!tower.RunActive)
            {
                // 未开局：双模式与公平 3×3 模板选择
                CoreLoopUiArt.Text("ModeSelectLabel", transform, cx + 26f, cy + 76f, 220f, 40f,
                    "本轮探索模式：", 21f, CoreLoopUiArt.Ink, true);
                CoreLoopUiArt.Button("ModeSelfBtn", transform, cx + 246f, cy + 72f, 340f, 48f,
                    "本人流派（真实养成）", () => { _selectedFairPreset = false; Refresh(); }, !busy, !_selectedFairPreset, 20f);
                CoreLoopUiArt.Button("ModePresetBtn", transform, cx + 604f, cy + 72f, 390f, 48f,
                    "公平 3×3 试炼（1 级平等）", () => { _selectedFairPreset = true; Refresh(); }, !busy, _selectedFairPreset, 20f);

                if (_selectedFairPreset)
                {
                    CoreLoopUiArt.Text("FairCharLbl", transform, cx + 26f, cy + 136f, 220f, 38f,
                        "人物公平模板：", 20f, CoreLoopUiArt.Ink, true);
                    uint[] chars = { 101u, 102u, 103u };
                    for (int i = 0; i < chars.Length; ++i)
                    {
                        uint cid = chars[i];
                        CoreLoopUiArt.Button("FairChar" + cid, transform, cx + 246f + i * 246f, cy + 132f, 228f, 46f,
                            WuxingExpansionModels.FairCharacterPresetName(cid),
                            () => { _selectedCharPresetId = cid; Refresh(); }, !busy, _selectedCharPresetId == cid, 19f);
                    }

                    CoreLoopUiArt.Text("FairPetLbl", transform, cx + 26f, cy + 196f, 220f, 38f,
                        "灵宠公平模板：", 20f, CoreLoopUiArt.Ink, true);
                    uint[] pets = { 201u, 202u, 203u };
                    for (int i = 0; i < pets.Length; ++i)
                    {
                        uint pid = pets[i];
                        CoreLoopUiArt.Button("FairPet" + pid, transform, cx + 246f + i * 246f, cy + 192f, 228f, 46f,
                            WuxingExpansionModels.FairPetPresetName(pid),
                            () => { _selectedPetPresetId = pid; Refresh(); }, !busy, _selectedPetPresetId == pid, 19f);
                    }
                }
                else
                {
                    CoreLoopUiArt.Text("SelfModeHint", transform, cx + 26f, cy + 134f, cw - 52f, 36f,
                        $"当前阵法：{WuxingExpansionModels.FormationName(season.EquippedFormationId)} · 开战自动满血满蓝，不消耗背包道具，随时中断自动保存。",
                        19f, CoreLoopUiArt.Muted, false);
                    PreBattleLoadoutQuickBar.RenderForFeatures(
                        transform, "TowerPreStartLoadoutBar", cx + 26f, cy + 178f, cw - 52f, 58f,
                        _game?.Features, _game?.Battle?.Phase ?? BattlePhase.None, _root.ShowToast);
                }

                string startLabel = _selectedFairPreset
                    ? $"免费开启公平试炼（{WuxingExpansionModels.FairCharacterPresetName(_selectedCharPresetId)} × {WuxingExpansionModels.FairPetPresetName(_selectedPetPresetId)}）"
                    : "免费开启新一轮五行奇策秘境（6 关短局）";
                CoreLoopUiArt.Button("StartTowerRun", transform, cx + 365f, cy + 298f, 660f, 66f,
                    startLabel, () => season.StartWuxingTowerRun(_selectedFairPreset, _selectedCharPresetId, _selectedPetPresetId),
                    !busy, true, 23f);
                return;
            }

            // 进行中：已铭刻五行奇策 3×2 网格（y=624..830）
            string runSub = tower.UseFreePreset
                ? $"本轮已铭刻奇策（{tower.ActiveSigilIds.Count} 枚）· 公平模板：{WuxingExpansionModels.FairCharacterPresetName(tower.CharacterPresetId)} × {WuxingExpansionModels.FairPetPresetName(tower.PetPresetId)}"
                : $"本轮已铭刻奇策（{tower.ActiveSigilIds.Count} 枚）· 阵法：{WuxingExpansionModels.FormationName(season.EquippedFormationId)}";
            CoreLoopUiArt.Text("ActiveBoonsHeader", transform, cx + 24f, cy + 68f, cw - 48f, 28f,
                runSub, 19f, CoreLoopUiArt.Muted, true);

            if (tower.ActiveSigilIds.Count == 0)
            {
                CoreLoopUiArt.Text("NoBoonsYet", transform, cx + 24f, cy + 126f, cw - 48f, 64f,
                    "通过第 1 关后即可三选一铭刻首批机制改造型「五行奇策」，集齐 3 枚同五行立即点亮强力共鸣！",
                    21f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
            }
            else
            {
                for (int i = 0; i < tower.ActiveSigilIds.Count && i < 6; ++i)
                {
                    uint boonId = tower.ActiveSigilIds[i];
                    WuxingExpansionModels.TryFindSigil(boonId, out var def);
                    int col = i % 3;
                    int row = i / 3;
                    float sx = cx + 22f + col * 452f;
                    float sy = cy + 102f + row * 94f;
                    CoreLoopUiArt.Card(transform, "BoonChip" + i, sx, sy, 440f, 86f, selected: def.Rarity >= 3u);
                    var elemSprite = WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                    if (elemSprite != null)
                        SeasonV1UiArt.CreateFixedIcon(transform, "BoonElem" + i, sx + 10f, sy + 18f, 44f, 44f, elemSprite, Color.clear);
                    float tx = elemSprite != null ? sx + 60f : sx + 14f;
                    float tw = elemSprite != null ? 368f : 412f;
                    CoreLoopUiArt.Text("BoonName" + i, transform, tx, sy + 8f, tw, 26f,
                        $"{def.Name} · {WuxingExpansionModels.ElementName(def.Element)}行{WuxingExpansionModels.RarityLabel(def.Rarity)}",
                        17f, def.Rarity >= 3u ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade, true);
                    CoreLoopUiArt.Text("BoonDesc" + i, transform, tx, sy + 36f, tw, 44f,
                        def.Description, 14f, CoreLoopUiArt.Ink, false, true);
                }
            }

            // 底部行动条（y=854..922）
            float btnY = cy + 304f;
            if (tower.PendingOfferSigilIds.Count > 0)
            {
                CoreLoopUiArt.Text("PendingPickHint", transform, cx + 24f, btnY + 12f, cw - 48f, 42f,
                    "请在弹出的「五行奇策三选一」中择一入局，即可继续推进！",
                    21f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.Center);
            }
            else if (tower.PendingEventKind != WuxingExpansionModels.RealmEventNone)
            {
                CoreLoopUiArt.Text("PendingEventHint", transform, cx + 24f, btnY + 12f, cw - 48f, 42f,
                    "请在弹出的秘境事件窗口中做出抉择！",
                    21f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.Center);
            }
            else if (tower.CurrentNodeKind == WuxingExpansionModels.RealmNodeNeedBranch)
            {
                string rightLabel = curNode == 2u ? "右路：进入奇遇事件（免战寻策）" : "右路：进入秘境休整（淬炼升品/同源寻策）";
                CoreLoopUiArt.Button("BranchLeftElite", transform, cx + 26f, btnY, 440f, 64f,
                    "左路：挑战精英强敌（保底灵/仙奇策）",
                    () => season.ChooseWuxingRealmBranch(WuxingExpansionModels.RealmBranchElite), !busy, true, 20f);
                CoreLoopUiArt.Button("BranchRightSpecial", transform, cx + 486f, btnY, 500f, 64f,
                    rightLabel,
                    () => season.ChooseWuxingRealmBranch(WuxingExpansionModels.RealmBranchSpecial), !busy, false, 20f);
                CoreLoopUiArt.Button("AbandonTowerRun", transform, cx + 1008f, btnY, 356f, 64f,
                    "放弃本轮（留首通）", () => season.AbandonWuxingTowerRun(), !busy, false, 20f);
            }
            else
            {
                uint dungeonId = tower.CurrentFloorDungeonId > 0u ? tower.CurrentFloorDungeonId : 101u;
                string nodeLabel = WuxingExpansionModels.RealmNodeKindLabel(tower.CurrentNodeKind);
                CoreLoopUiArt.Button("EnterTowerFloor", transform, cx + 26f, btnY, 540f, 64f,
                    $"挑战第 {curNode} 关 · {nodeLabel}（满血满蓝入场）", () =>
                    {
                        _root.CloseCurrent();
                        _game.Battle?.JoinQueue(Match.MatchMode.PveSolo, dungeonId, 0u, 0u, 0u, 0u, isWuxingTower: true);
                    }, !busy, true, 22f);
                CoreLoopUiArt.Button("AbandonTowerRun", transform, cx + 1008f, btnY, 356f, 64f,
                    "放弃本轮（留首通）", () => season.AbandonWuxingTowerRun(), !busy, false, 20f);
            }
        }

        private void DrawLeaderboardCard(global::WuxingTowerStateSnapshot tower, uint maxNodes)
        {
            const float lx = 1702f;
            const float ly = 316f;
            const float lw = 563f;
            const float lh = 620f;

            CoreLoopUiArt.Card(transform, "TowerLeaderboardCard", lx, ly, lw, lh);
            CoreLoopUiArt.Text("LeaderboardTitle", transform, lx + 20f, ly + 12f, lw - 40f, 34f,
                "本周最速流派榜（双模式隔离）", 22f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);

            // 双榜切换标签
            CoreLoopUiArt.Button("LbTabSelf", transform, lx + 24f, ly + 52f, 250f, 40f,
                "本人流派 Top 20", () => { _showPresetLeaderboard = false; Refresh(); }, true, !_showPresetLeaderboard, 18f);
            CoreLoopUiArt.Button("LbTabPreset", transform, lx + 288f, ly + 52f, 250f, 40f,
                "公平试炼 Top 20", () => { _showPresetLeaderboard = true; Refresh(); }, true, _showPresetLeaderboard, 18f);

            IReadOnlyList<global::WuxingTowerLeaderboardEntry> list = _showPresetLeaderboard
                ? (IReadOnlyList<global::WuxingTowerLeaderboardEntry>)tower.WeeklyLeaderboardPreset
                : tower.WeeklyLeaderboard;

            if (list == null || list.Count == 0)
            {
                CoreLoopUiArt.Text("LeaderboardEmpty", transform, lx + 24f, ly + 150f, lw - 48f, 90f,
                    _showPresetLeaderboard
                        ? "本周尚无公平试炼登榜道友，任选 3×3 模板通过第 1 关即刻入榜！"
                        : "本周尚无本人流派登榜道友，通过第 1 关即刻入榜！",
                    19f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
                return;
            }

            int count = Mathf.Min(list.Count, 10);
            for (int i = 0; i < count; ++i)
            {
                var row = list[i];
                float ry = ly + 104f + i * 50f;
                CoreLoopUiArt.Image("LbRow" + i, transform, lx + 18f, ry, lw - 36f, 44f, null,
                    i < 3 ? new Color(0.97f, 0.92f, 0.78f, 0.94f) : new Color(0.95f, 0.93f, 0.87f, 0.90f));
                string pname = string.IsNullOrWhiteSpace(row.PlayerName) ? $"道友{row.PlayerId}" : row.PlayerName;
                string score = row.BestFloor >= maxNodes && row.BestRounds > 0u
                    ? $"全通·{row.BestRounds}回合"
                    : $"第{row.BestFloor}/{maxNodes}关";
                string resSuffix = row.ResonanceElement != 0u
                    ? $" · ★{WuxingExpansionModels.ElementName(row.ResonanceElement)}"
                    : string.Empty;
                CoreLoopUiArt.Text("LbName" + i, transform, lx + 30f, ry + 7f, 232f, 30f,
                    $"#{row.Rank} {pname}", 18f, CoreLoopUiArt.Ink, i < 3);
                CoreLoopUiArt.Text("LbScore" + i, transform, lx + 264f, ry + 7f, 268f, 30f,
                    $"{score}{resSuffix}", 16f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.MidlineRight);
            }
        }

        private void DrawEventResolveOverlay(SeasonClient season, global::WuxingTowerStateSnapshot tower, bool busy)
        {
            var dim = QdaoUguiFactory.CreateImage("RealmEventDim", transform, 0f, 0f, 2560f, 1080f, null);
            dim.color = new Color(0.04f, 0.11f, 0.09f, 0.76f);
            dim.raycastTarget = true;

            bool isEncounter = tower.PendingEventKind == WuxingExpansionModels.RealmEventEncounter;
            CoreLoopUiArt.Card(transform, "RealmEventModal", 560f, 210f, 1440f, 640f);
            CoreLoopUiArt.Text("RealmEventTitle", transform, 600f, 238f, 1360f, 48f,
                isEncounter ? "第 2 关 · 秘境奇遇（二选一免战收获）" : "第 4 关 · 秘境休整（二选一强化构筑）",
                30f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);

            if (isEncounter)
            {
                // 奇遇选项 1：道缘寻策
                CoreLoopUiArt.Card(transform, "EncOpt1Card", 630f, 326f, 610f, 440f);
                CoreLoopUiArt.Text("EncOpt1Title", transform, 660f, 360f, 550f, 42f,
                    "道缘寻策 · 直获灵品奇策", 26f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
                CoreLoopUiArt.Text("EncOpt1Desc", transform, 660f, 430f, 550f, 160f,
                    "立即获得 1 枚尚未持有的灵品五行奇策，并直接推进至第 3 关。\n\n适合稳健提速、快速拉高单卡强度。",
                    21f, CoreLoopUiArt.Ink, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("EncOpt1Btn", transform, 735f, 672f, 400f, 64f,
                    "选择「道缘寻策」", () => season.ResolveWuxingRealmEvent(1u), !busy, true, 23f);

                // 奇遇选项 2：五行悟道
                CoreLoopUiArt.Card(transform, "EncOpt2Card", 1320f, 326f, 610f, 440f);
                CoreLoopUiArt.Text("EncOpt2Title", transform, 1350f, 360f, 550f, 42f,
                    "五行悟道 · 免费奇策三选一", 26f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
                CoreLoopUiArt.Text("EncOpt2Desc", transform, 1350f, 430f, 550f, 160f,
                    "展开 3 枚未持有的五行奇策自由挑选 1 枚入局，选定后推进至第 3 关。\n\n适合定向寻找关键流派组件与同系共鸣。",
                    21f, CoreLoopUiArt.Ink, false, true, TextAlignmentOptions.Center);
                CoreLoopUiArt.Button("EncOpt2Btn", transform, 1425f, 672f, 400f, 64f,
                    "选择「五行悟道」", () => season.ResolveWuxingRealmEvent(2u), !busy, false, 23f);
                return;
            }

            // 第 4 关休整：收集当前可升品的已持有凡/灵奇策
            var upgradables = new List<uint>(6);
            for (int i = 0; i < tower.ActiveSigilIds.Count; ++i)
            {
                uint id = tower.ActiveSigilIds[i];
                if (WuxingExpansionModels.CanUpgradeBoon(id, tower.ActiveSigilIds))
                    upgradables.Add(id);
            }
            if (upgradables.Count > 0 && !upgradables.Contains(_selectedUpgradeBoonId))
                _selectedUpgradeBoonId = upgradables[0];

            // 休整选项 1：奇策淬炼升品
            CoreLoopUiArt.Card(transform, "RestOpt1Card", 630f, 326f, 610f, 460f);
            CoreLoopUiArt.Text("RestOpt1Title", transform, 660f, 352f, 550f, 40f,
                "奇策淬炼 · 同行高品重铸", 25f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Text("RestOpt1Desc", transform, 660f, 402f, 550f, 84f,
                "将 1 枚已持有的凡品或灵品奇策淬炼升级为同五行更高一档未持有奇策，并直接推进至第 5 关。",
                19f, CoreLoopUiArt.Ink, false, true, TextAlignmentOptions.Center);

            if (upgradables.Count == 0)
            {
                CoreLoopUiArt.Text("RestNoUpgrade", transform, 660f, 520f, 550f, 60f,
                    "当前暂无可升品的凡/灵品奇策，建议选择右侧「同源寻策」。",
                    19f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);
            }
            else
            {
                for (int i = 0; i < upgradables.Count && i < 4; ++i)
                {
                    uint bid = upgradables[i];
                    WuxingExpansionModels.TryFindSigil(bid, out var def);
                    float py = 498f + i * 44f;
                    CoreLoopUiArt.Button("RestUpgPick" + bid, transform, 690f, py, 490f, 38f,
                        $"{def.Name}（{WuxingExpansionModels.ElementName(def.Element)}行·{WuxingExpansionModels.RarityLabel(def.Rarity)}）",
                        () => { _selectedUpgradeBoonId = bid; Refresh(); }, !busy, _selectedUpgradeBoonId == bid, 18f);
                }
            }
            CoreLoopUiArt.Button("RestOpt1Btn", transform, 735f, 700f, 400f, 62f,
                "确认淬炼升品", () => season.ResolveWuxingRealmEvent(1u, _selectedUpgradeBoonId),
                !busy && upgradables.Count > 0, true, 22f);

            // 休整选项 2：同源寻策
            CoreLoopUiArt.Card(transform, "RestOpt2Card", 1320f, 326f, 610f, 460f);
            CoreLoopUiArt.Text("RestOpt2Title", transform, 1350f, 352f, 550f, 40f,
                "同源寻策 · 补齐五行共鸣", 25f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Text("RestOpt2Desc", transform, 1350f, 412f, 550f, 160f,
                "按你当前持有最多的五行定向展开 3 选 1 奇策候选（保底塞入至多 2 枚同五行未持有奇策），助你临战凑齐 3 同系共鸣！\n\n"
                + WuxingExpansionModels.FormatResonanceProgressHint(tower.ActiveSigilIds),
                19f, CoreLoopUiArt.Ink, false, true, TextAlignmentOptions.Center);
            CoreLoopUiArt.Button("RestOpt2Btn", transform, 1425f, 700f, 400f, 62f,
                "展开「同源寻策」", () => season.ResolveWuxingRealmEvent(2u), !busy, false, 22f);
        }

        private void DrawBoonDraftOverlay(SeasonClient season, global::WuxingTowerStateSnapshot tower, bool busy)
        {
            var dim = QdaoUguiFactory.CreateImage("SigilPickDim", transform, 0f, 0f, 2560f, 1080f, null);
            dim.color = new Color(0.04f, 0.11f, 0.09f, 0.78f);
            dim.raycastTarget = true;

            CoreLoopUiArt.Card(transform, "SigilPickModal", 500f, 124f, 1560f, 832f);
            CoreLoopUiArt.Text("SigilPickTitle", transform, 540f, 148f, 1480f, 46f,
                $"第 {tower.CurrentFloor} 关获胜 · 请择 1 枚五行奇策入局（出图清零）",
                30f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            CoreLoopUiArt.Text("SigilPickResHint", transform, 540f, 196f, 1480f, 32f,
                WuxingExpansionModels.FormatResonanceProgressHint(tower.ActiveSigilIds),
                20f, CoreLoopUiArt.Warning, true, false, TextAlignmentOptions.Center);

            const float cardW = 350f;
            const float cardH = 500f;
            const float cy = 246f;
            int count = Mathf.Min(tower.PendingOfferSigilIds.Count, 3);
            float totalW = count * cardW + Mathf.Max(0, count - 1) * 90f;
            float startX = (2560f - totalW) * 0.5f;
            var cardBg = WuxingExpansionsUiArt.LoadRealmBoonCardBg();

            for (int i = 0; i < count; ++i)
            {
                uint boonId = tower.PendingOfferSigilIds[i];
                WuxingExpansionModels.TryFindSigil(boonId, out var def);
                float cx = startX + i * (cardW + 90f);

                if (cardBg != null)
                {
                    var bgImg = QdaoUguiFactory.CreateImage("OfferCardBg" + i, transform, cx, cy, cardW, cardH, cardBg, false);
                    bgImg.type = cardBg.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                    bgImg.preserveAspect = false;
                }
                else
                {
                    CoreLoopUiArt.Card(transform, "OfferCardBg" + i, cx, cy, cardW, cardH);
                }

                var frame = WuxingExpansionsUiArt.LoadSigilCardFrame(def.Rarity);
                if (frame != null)
                {
                    var frameImg = QdaoUguiFactory.CreateImage("OfferCard" + i, transform, cx, cy, cardW, cardH, frame, false);
                    frameImg.type = frame.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                    frameImg.preserveAspect = false;
                }

                CoreLoopUiArt.Badge(transform, "OfferRarity" + i, cx + 86f, cy + 110f, 178f,
                    $"{WuxingExpansionModels.ElementName(def.Element)}行 · {WuxingExpansionModels.RarityLabel(def.Rarity)}",
                    def.Rarity >= 3u ? CoreLoopUiArt.Warning : CoreLoopUiArt.DeepJade);

                var elemIcon = WuxingExpansionsUiArt.LoadElementIcon(def.Element);
                if (elemIcon != null)
                    SeasonV1UiArt.CreateFixedIcon(transform, "OfferElem" + i, cx + 117f, cy + 158f, 116f, 116f, elemIcon, Color.clear);

                CoreLoopUiArt.Text("OfferName" + i, transform, cx + 44f, cy + 282f, 262f, 36f,
                    def.Name, 25f, CoreLoopUiArt.Ink, true, false, TextAlignmentOptions.Center);
                CoreLoopUiArt.Text("OfferDesc" + i, transform, cx + 46f, cy + 324f, 258f, 108f,
                    def.Description, 17f, CoreLoopUiArt.Muted, false, true, TextAlignmentOptions.Center);

                uint capturedId = boonId;
                CoreLoopUiArt.Button("PickSigilBtn" + i, transform, cx + 35f, cy + cardH + 18f, 280f, 58f,
                    "选择此策", () => season.PickWuxingTowerSigil(capturedId), !busy, true, 23f);
            }

            var adRoot = RewardedAdUiRoot.Instance;
            if (!busy && tower.RunId != 0ul && adRoot != null
                && adRoot.CanShowSlot(MmorpgClient.Game.RewardedAd.RewardedAdModel.SlotIdTowerBoonReroll, out _))
            {
                uint runIdLow = (uint)tower.RunId;
                var rerollBtn = CoreLoopUiArt.Button("AdRerollBoonBtn", transform, 1050f, 824f, 460f, 54f,
                    MmorpgClient.Game.RewardedAd.RewardedAdModel.TowerRerollEntryText,
                    () => adRoot.OpenConfirmForSlot(
                        MmorpgClient.Game.RewardedAd.RewardedAdModel.SlotIdTowerBoonReroll,
                        runIdLow, string.Empty,
                        ok => { if (ok) season.RequestWuxingTowerState(force: true); }),
                    true, false, 20f);
                RewardedAdUiRoot.AttachVideoBadge(rerollBtn.transform, 26f);
            }

            CoreLoopUiArt.Text("SigilPickFooter", transform, 540f, 890f, 1480f, 34f,
                "所有五行奇策仅在本轮秘境生效，随时离开游戏自动保留当前进度。",
                18f, CoreLoopUiArt.Muted, false, false, TextAlignmentOptions.Center);
        }
    }
}
