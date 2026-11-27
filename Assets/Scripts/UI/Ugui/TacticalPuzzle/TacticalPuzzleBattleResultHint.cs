using System;
using MmorpgClient.Game;
using MmorpgClient.Game.TacticalPuzzle;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.TacticalPuzzle.TacticalPuzzleUiArt;

namespace MmorpgClient.UI.Ugui.TacticalPuzzle
{
    /// <summary>
    /// 战斗结算页嵌入式「万象残局坊结算条」（仅在 BattleSettlementData.TacticalPuzzleContext != null 时展示）。
    /// 提供「再试一次 / 看标准解 / 点赞 / 分享 / 返回残局坊」快捷操作，全部经 BattleUiRoot 先安全确认收屏。
    /// </summary>
    public sealed class TacticalPuzzleBattleResultHint
    {
        public const float BarWidth = 820f;
        public const float BarHeight = 116f;

        private readonly RectTransform _root;
        private readonly TMP_Text _headline;
        private readonly TMP_Text _subtitle;
        private readonly Button _retryButton;
        private readonly Button _solutionButton;
        private readonly Button _likeButton;
        private readonly Button _shareButton;
        private readonly Button _hallButton;

        private global::TacticalPuzzleBattleContext _context;
        private uint _totalRounds;
        private bool _won;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public TacticalPuzzleBattleResultHint(UnityEngine.Transform parent, float x, float y)
        {
            var plate = SlicedOrTint("TacticalPuzzleResultBar", parent, x, y, BarWidth, BarHeight,
                LoadSlice("puzzle_chat_card_bg"), Paper, raycast: true);
            _root = (RectTransform)plate.transform;

            FixedIcon("PuzzleGlyph", _root, 16f, 12f, 44f, 44f, LoadSlice("hud_entry_puzzle"));
            _headline = Label("Headline", _root, 68f, 10f, 736f, 28f, "", 20f, Jade, bold: true);
            _subtitle = Label("Subtitle", _root, 68f, 38f, 736f, 22f, "", 15f, Sub);

            float btnY = 64f;
            _retryButton = ActionButton("RetryPuzzle", _root, 18f, btnY, 146f, 44f, "再试一次", 17f,
                primary: true, interactable: true, HandleRetry);
            _solutionButton = ActionButton("WatchPuzzleSolution", _root, 174f, btnY, 152f, 44f, "看标准解", 17f,
                primary: false, interactable: true, HandleWatchSolution, LoadSlice("icon_puzzle_solution"));
            _likeButton = ActionButton("LikePuzzle", _root, 336f, btnY, 134f, 44f, "点赞好题", 17f,
                primary: false, interactable: true, HandleLike, LoadSlice("icon_puzzle_like"));
            _shareButton = ActionButton("SharePuzzle", _root, 480f, btnY, 144f, 44f, "分享残局", 17f,
                primary: false, interactable: true, HandleShare);
            _hallButton = ActionButton("OpenPuzzleHall", _root, 634f, btnY, 168f, 44f, "返回残局坊", 17f,
                primary: false, interactable: true, HandleOpenHall);

            Hide();
        }

        public bool ShowForLive(global::BattleEndS2C end)
        {
            var settlement = end?.Settlement;
            var ctx = settlement?.TacticalPuzzleContext;
            if (ctx == null || ctx.PuzzleId == 0ul)
            {
                Hide();
                return false;
            }
            _context = ctx.Clone();
            _totalRounds = settlement.TotalRounds;
            _won = !settlement.Fled
                && ((settlement.Outcome == eBattleOutcome.BattleOutcomeSideAWin && settlement.PlayerTeamIndex == 0u)
                    || (settlement.Outcome == eBattleOutcome.BattleOutcomeSideBWin && settlement.PlayerTeamIndex == 1u));

            string title = string.IsNullOrWhiteSpace(ctx.PuzzleTitle) ? $"残局#{ctx.PuzzleId}" : ctx.PuzzleTitle;
            ulong myId = AppBootstrap.Instance?.GameClient?.PlayerId ?? 0ul;
            bool isAuthor = ctx.AuthorPlayerId != 0ul && ctx.AuthorPlayerId == myId;

            if (ctx.IsAuthorVerify)
            {
                _headline.text = _won ? $"「{title}」自证通关，已自动发布全服！" : $"「{title}」自证未通过";
                _subtitle.text = _won
                    ? $"作者标准解法已留档（{_totalRounds}回合），全服道友现在可以挑战并观摩你的解法。"
                    : "可在回合上限内再试一次，或回残局坊调整盘面参数。";
            }
            else
            {
                _headline.text = _won ? $"「{title}」破局成功（{_totalRounds}回合）！" : $"「{title}」暂未破局";
                _subtitle.text = _won
                    ? "公平模板残局 · 零消耗零战力通胀 · 喜欢这道奇局记得点赞或分享给仙友！"
                    : "解不开时可点击「看标准解」观摩作者自证录像学习套路。";
            }

            _solutionButton.interactable = !ctx.IsAuthorVerify;
            _likeButton.interactable = !ctx.IsAuthorVerify && !isAuthor && _won;
            _shareButton.interactable = !ctx.IsAuthorVerify || _won;

            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            return true;
        }

        public void SetVerticalPosition(float y)
        {
            if (_root != null) _root.anchoredPosition = new Vector2(_root.anchoredPosition.x, -y);
        }

        public void Hide()
        {
            _context = null;
            _totalRounds = 0u;
            _won = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void HandleRetry()
        {
            if (_context == null || _context.PuzzleId == 0ul) return;
            ulong pid = _context.PuzzleId;
            bool verify = _context.IsAuthorVerify;
            BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
            TacticalPuzzleUiRoot.EnsureSpawned();
            TacticalPuzzleUiRoot.Instance?.StartPuzzleChallenge(pid, verify);
        }

        private void HandleWatchSolution()
        {
            if (_context == null || _context.PuzzleId == 0ul) return;
            ulong pid = _context.PuzzleId;
            uint rev = _context.SpecRevision;
            BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
            TacticalPuzzleUiRoot.EnsureSpawned();
            TacticalPuzzleUiRoot.Instance?.WatchSolutionById(pid, rev);
        }

        private void HandleLike()
        {
            if (_context == null || _context.PuzzleId == 0ul) return;
            _likeButton.interactable = false;
            TacticalPuzzleUiRoot.EnsureSpawned();
            TacticalPuzzleUiRoot.Instance?.LikePuzzleById(_context.PuzzleId);
        }

        private void HandleShare()
        {
            if (_context == null || _context.PuzzleId == 0ul) return;
            string draft = TacticalPuzzleModels.FormatChatShareDraftFromContext(_context, _won ? _totalRounds : 0u);
            BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
            TacticalPuzzleUiRoot.EnsureSpawned();
            TacticalPuzzleUiRoot.Instance?.ShareDraftToWorldChat(draft);
        }

        private void HandleOpenHall()
        {
            ulong pid = _context?.PuzzleId ?? 0ul;
            bool verify = _context?.IsAuthorVerify ?? false;
            BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
            TacticalPuzzleUiRoot.EnsureSpawned();
            TacticalPuzzleUiRoot.Instance?.Open(pid,
                verify ? Match.TacticalPuzzleListTab.MyCreated : (Match.TacticalPuzzleListTab?)null);
        }
    }
}
