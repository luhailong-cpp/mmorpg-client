using System;

namespace MmorpgClient.Game
{
    /// <summary>跨模块只读 Toast 提示事件值（World / 业务层发向 UI 层，避免反向依赖具体 UiRoot）。</summary>
    public readonly struct ToastNoticeEvent
    {
        public string Message { get; }
        public bool IsError { get; }

        public ToastNoticeEvent(string message, bool isError = false)
        {
            Message = message ?? string.Empty;
            IsError = isError;
        }
    }

    /// <summary>
    /// 跨模块 UI 域事件总线（仅限单线程主循环，纯 C# 不引用 UnityEngine）。
    /// 用于消除 World -&gt; UI 反向强引用，以及替代挂在具体 UiRoot 上的 public static 可变委托字段。
    /// </summary>
    public static class UiDomainEvents
    {
        public static event Action<ToastNoticeEvent> ToastRequested;

        private static Func<BattleEndS2C, uint, bool> s_rankedBattleEndHandler;
        private static Func<PlayerBattleResult, bool> s_rankedRecoveredResultHandler;

        public static void PublishToast(string message, bool isError = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            ToastRequested?.Invoke(new ToastNoticeEvent(message, isError));
        }

        public static void RegisterRankedSettlementHandlers(
            Func<BattleEndS2C, uint, bool> onBattleEnd,
            Func<PlayerBattleResult, bool> onRecoveredResult)
        {
            s_rankedBattleEndHandler = onBattleEnd;
            s_rankedRecoveredResultHandler = onRecoveredResult;
        }

        public static void UnregisterRankedSettlementHandlers(
            Func<BattleEndS2C, uint, bool> onBattleEnd,
            Func<PlayerBattleResult, bool> onRecoveredResult)
        {
            if (s_rankedBattleEndHandler == onBattleEnd) s_rankedBattleEndHandler = null;
            if (s_rankedRecoveredResultHandler == onRecoveredResult) s_rankedRecoveredResultHandler = null;
        }

        public static bool TryInterceptRankedBattleEnd(BattleEndS2C end, uint localTeam)
            => end != null && s_rankedBattleEndHandler != null && s_rankedBattleEndHandler(end, localTeam);

        public static bool TryInterceptRankedRecoveredResult(PlayerBattleResult result)
            => result != null && s_rankedRecoveredResultHandler != null && s_rankedRecoveredResultHandler(result);

        /// <summary>仅限 EditMode/PlayMode 测试清理静态订阅，防止跨用例污染。</summary>
        public static void ResetForTests()
        {
            ToastRequested = null;
            s_rankedBattleEndHandler = null;
            s_rankedRecoveredResultHandler = null;
        }
    }
}
