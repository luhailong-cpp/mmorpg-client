using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 观战入口面板(场景内悬浮,构建/交互惯例照 BattleQueuePanel):
    ///   - 「随机观战」= SpectateClient.WatchRandom()(battle_id=0,由 match 挑场);
    ///   - 战斗列表:RefreshList 拉 BattleWatchSummary,条目点击进指定观战;
    ///   - 列表条目只在 ApplyList 时重建(摘要是开局时刻快照,无实时刷新语义);
    ///   - 进入观战成功后由 BattleUiRoot 收面板(Watching 相位),这里不管关屏。
    /// </summary>
    public sealed class SpectatePanel
    {
        private const float PanelX = 60f;
        private const float PanelY = 140f;
        private const float PanelW = 640f;
        private const float PanelH = 820f;

        // 列表区几何(条目高度 x 条数决定面板下沿,改条数需同步 PanelH)
        private const int MaxEntries = 6;
        private const float ListTop = 170f;
        private const float EntryH = 84f;
        private const float EntryGap = 8f;

        private readonly BattleUiRoot _owner;
        private readonly RectTransform _root;
        private readonly UiTextButton _randomButton;
        private readonly UiTextButton _refreshButton;
        private readonly UiTextButton _closeButton;
        private readonly TMP_Text _emptyText;
        private readonly TMP_Text _statusText;
        private readonly List<UiTextButton> _entries = new();

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public SpectatePanel(BattleUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;

            var bg = BattleUiWidgets.CreatePanel("SpectatePanel", parent,
                PanelX, PanelY, PanelW, PanelH, BattleUiStyle.PanelBg);
            _root = (RectTransform)bg.transform;

            QdaoUguiFactory.CreateText("Title", _root, 24f, 16f, 320f, 42f,
                "观战", 28f, QdaoUguiTheme.Cream);
            _closeButton = BattleUiWidgets.CreateTextButton("Close", _root, PanelW - 64f, 12f, 48f, 48f,
                "×", 28f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _closeButton.Button.onClick.AddListener(Hide);

            _randomButton = BattleUiWidgets.CreateTextButton("WatchRandom", _root, 30f, 76f, 380f, 74f,
                "随机观战", 24f, BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _randomButton.Button.onClick.AddListener(OnRandomClicked);

            _refreshButton = BattleUiWidgets.CreateTextButton("Refresh", _root, 430f, 76f, 180f, 74f,
                "刷新", 24f, BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _refreshButton.Button.onClick.AddListener(RefreshList);

            _emptyText = QdaoUguiFactory.CreateText("Empty", _root, 30f, ListTop, PanelW - 60f, 60f,
                "暂无可观战的战斗", 22f, QdaoUguiTheme.MutedBrown);

            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root,
                30f, ListTop + MaxEntries * (EntryH + EntryGap) + 12f, PanelW - 60f, 72f,
                string.Empty, 18f, QdaoUguiTheme.StatusCream);

            Hide();
        }

        public void Show()
        {
            if (_root != null) _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        public void SetStatus(string value)
        {
            if (_statusText != null) _statusText.text = value ?? string.Empty;
        }

        /// <summary>拉取可观战列表(条数按面板可容纳的 MaxEntries 收口)。</summary>
        public void RefreshList()
        {
            var spectate = _owner?.Spectate;
            if (spectate == null) { SetStatus("观战模块未就绪"); return; }
            spectate.RefreshList(MaxEntries);
            SetStatus("列表刷新中…");
        }

        /// <summary>SpectateClient.OnListUpdated 转发进来的列表结果(BattleUiRoot 接线)。</summary>
        public void ApplyList(Match.ListWatchableBattlesResponse resp)
        {
            foreach (var entry in _entries)
            {
                if (entry.Rect != null)
                    UnityEngine.Object.Destroy(entry.Rect.gameObject);
            }
            _entries.Clear();

            int count = 0;
            if (resp?.Battles != null)
            {
                foreach (var summary in resp.Battles)
                {
                    if (summary == null || summary.BattleId == 0) continue;
                    if (count >= MaxEntries) break;
                    BuildEntry(summary, count);
                    count++;
                }
            }
            _emptyText.gameObject.SetActive(count == 0);
            SetStatus(count == 0 ? string.Empty : $"共 {count} 场,点击条目进入观战");
        }

        // ── 内部 ─────────────────────────────────────────────

        private void BuildEntry(Match.BattleWatchSummary summary, int index)
        {
            float y = ListTop + index * (EntryH + EntryGap);
            var entry = BattleUiWidgets.CreateTextButton($"Watch_{summary.BattleId}", _root,
                30f, y, PanelW - 60f, EntryH, string.Empty, 20f,
                BattleUiStyle.PanelBgLight, BattleUiStyle.ButtonText);

            // 条目双行:上行 模式 + 开局相对时间,下行 玩家名单(超长省略号截断)
            QdaoUguiFactory.CreateText("Head", entry.Rect, 16f, 8f, PanelW - 92f, 32f,
                $"{ModeName(summary.Mode)} · {DescribeElapsed(summary.CreatedAtMs)}",
                20f, QdaoUguiTheme.Cream);
            QdaoUguiFactory.CreateText("Players", entry.Rect, 16f, 44f, PanelW - 92f, 30f,
                FormatPlayers(summary), 17f, QdaoUguiTheme.StatusCream);

            ulong battleId = summary.BattleId;
            entry.Button.onClick.AddListener(() => OnEntryClicked(battleId));
            _entries.Add(entry);
        }

        private void OnRandomClicked()
        {
            var spectate = _owner?.Spectate;
            if (spectate == null) { SetStatus("观战模块未就绪"); return; }
            spectate.WatchRandom();
        }

        private void OnEntryClicked(ulong battleId)
        {
            var spectate = _owner?.Spectate;
            if (spectate == null) { SetStatus("观战模块未就绪"); return; }
            spectate.WatchBattle(battleId);
        }

        private static string ModeName(Match.MatchMode mode)
        {
            switch (mode)
            {
                case Match.MatchMode.PveSolo: return "PVE 单人";
                case Match.MatchMode.PveTeam: return "PVE 组队";
                case Match.MatchMode._1V1: return "PVP 1v1";
                case Match.MatchMode._3V3: return "PVP 3v3";
                case Match.MatchMode._5V5: return "PVP 5v5";
                case Match.MatchMode.PvpChallenge: return "切磋 PK";
                default: return $"模式{(int)mode}";
            }
        }

        private static string DescribeElapsed(ulong createdAtMs)
        {
            long elapsedMs = BattleUiWidgets.NowUnixMs() - (long)createdAtMs;
            if (createdAtMs == 0 || elapsedMs < 0) return "刚开局";
            return elapsedMs < 60_000 ? "刚开局" : $"已打 {elapsedMs / 60_000} 分钟";
        }

        private static string FormatPlayers(Match.BattleWatchSummary summary)
        {
            if (summary.PlayerNames == null || summary.PlayerNames.Count == 0) return "(无玩家)";
            return string.Join("、", summary.PlayerNames);
        }
    }
}
