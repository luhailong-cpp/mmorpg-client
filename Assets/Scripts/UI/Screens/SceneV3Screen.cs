using System;
using FairyGUI;
using MmorpgClient.UI.Ugui.Battle;
using UnityEngine;

namespace MmorpgClient.UI.Screens
{
    /// <summary>
    /// V3 in-game placeholder screen. Has a banner + "返回登录" button so the
    /// router flow round-trips end to end. Real HUD widgets land here once
    /// the gameplay protocol is wired in again.
    ///
    /// Layout contract with assets/qdao/SceneV3.xml:
    ///   banner (V3Banner), ornTL/ornTR (GImage)
    ///   btnBack (V3BtnAlt)
    ///   status  (GTextField)
    /// </summary>
    public sealed class SceneV3Screen : IScreen
    {
        private const string PkgComponent = "SceneV3";

        private AppBootstrap _app;
        private GComponent _root;
        private GTextField _status;

        public GComponent Build(AppBootstrap app)
        {
            _app = app;

            _root = Theme.TryCreateFromPackage(PkgComponent);
            if (_root == null) { _root = BuildMissingPackagePlaceholder(); return _root; }

            _status = Theme.Find<GTextField>(_root, "status");
            BindIfPresent("btnBack", () => _app.Router.Show<LoginV3Screen>());
            SetStatus("");
            return _root;
        }

        // 战斗入口:场景屏亮起时激活 UGUI 战斗悬浮层(排队面板入口按钮、
        // 挑战弹窗宿主、战斗屏切换都由 BattleUiRoot 承载,禁止在此加 FairyGUI UI)。
        public void OnEnter() { BattleUiRoot.NotifySceneScreenEntered(); }
        public void OnExit()  { BattleUiRoot.NotifySceneScreenExited(); }
        public void Tick(float _) { }

        private void BindIfPresent(string childName, Action handler)
        {
            var go = _root.GetChild(childName);
            if (go == null) return;
            go.onClick.Set(_ => handler?.Invoke());
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text ?? string.Empty;
        }

        private GComponent BuildMissingPackagePlaceholder()
        {
            var c = new GComponent();
            c.SetSize(Theme.Art.ReferenceWidth, Theme.Art.ReferenceHeight);
            var t = new GTextField { text = "qdao package missing — open client/fairygui/qdao/qdao.fairy and F8 publish." };
            t.SetSize(c.width, 80);
            t.SetXY(0, c.height * 0.5f - 40);
            t.textFormat = new TextFormat { font = Theme.BodyFontName, size = 32, align = AlignType.Center, color = new Color(0.85f, 0.7f, 0.4f) };
            c.AddChild(t);
            return c;
        }
    }
}
