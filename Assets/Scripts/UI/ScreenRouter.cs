using System;
using System.Collections.Generic;
using FairyGUI;

namespace MmorpgClient.UI
{
    public interface IScreen
    {
        GComponent Build(AppBootstrap app);
        void OnEnter();
        void OnExit();
        void Tick(float dt);
    }

    /// <summary>
    /// Single-instance screen stack on top of FairyGUI's GRoot. At most one
    /// screen lives in the host container; each screen is constructed lazily
    /// and reused.
    /// </summary>
    public sealed class ScreenRouter
    {
        private readonly AppBootstrap _app;
        private readonly GComponent _host;
        private readonly Dictionary<Type, IScreen> _cache = new();

        public IScreen Current { get; private set; }
        public GComponent Host => _host;

        /// <summary>
        /// 路由切屏通知(FairyGUI 兼容模式)。战斗 UI 是独立的 UGUI 悬浮层
        /// (UI/Ugui/Battle/BattleUiRoot),用静态事件感知「是否在场景屏」来
        /// 显示战斗入口/收起悬浮面板,避免 FairyGUI 层反向依赖战斗模块。
        /// </summary>
        public static event Action<IScreen> ScreenChanged;

        public ScreenRouter(AppBootstrap app, GComponent host)
        {
            _app = app;
            _host = host;
        }

        public T Show<T>() where T : IScreen, new()
        {
            if (Current is T existing) return existing;

            Current?.OnExit();
            // Remove (do not dispose) prior screen so it can be reused next Show<T>().
            for (int i = _host.numChildren - 1; i >= 0; i--)
                _host.RemoveChildAt(i, false);

            if (!_cache.TryGetValue(typeof(T), out var screen))
            {
                screen = new T();
                _cache[typeof(T)] = screen;
            }
            var root = screen.Build(_app);
            // Stretch to host (always design space: 2560x1080).
            root.SetSize(_host.width, _host.height);
            root.AddRelation(_host, RelationType.Size);
            _host.AddChild(root);

            Current = screen;
            screen.OnEnter();
            ScreenChanged?.Invoke(screen);
            return (T)screen;
        }

        public void Tick(float dt) => Current?.Tick(dt);

        /// <summary>Called when GRoot is resized. _host is the fixed 2560x1080
        /// design canvas, so nothing to resize here — AppBootstrap.FitRootToScreen
        /// handles the uniform scale + center.</summary>
        public void OnRootResize()
        {
        }
    }
}
