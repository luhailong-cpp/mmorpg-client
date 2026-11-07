using System;
using System.Reflection;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Rank;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class UiShutdownTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _fixture, _view;
        private MonoBehaviour _owner;

        [TearDown]
        public void TearDown()
        {
            if (_owner != null) Invoke("OnDestroy");
            if (_fixture != null) Object.DestroyImmediate(_fixture);
        }

        [TestCase(typeof(AttributeUiRoot))]
        [TestCase(typeof(GuildUiRoot))]
        [TestCase(typeof(RankUiRoot))]
        public void MissingManagedViewWithRetainedSessionFlagsDoesNotThrow(Type type)
        {
            CreateOwner(type);
            if (type == typeof(AttributeUiRoot))
            {
                Set("_app", _fixture.AddComponent<AppBootstrap>());
                Set("_canvasGo", _view);
                Set("_clientBound", true);
                Set("_playerId", 123UL);
            }
            else Set("_player", 123UL);
            Assert.DoesNotThrow(() => Invoke("Update"));
            Assert.DoesNotThrow(() => Invoke("OnDisable"));
            Assert.DoesNotThrow(() => Invoke("OnDestroy"));
            Assert.DoesNotThrow(() => Invoke("OnDestroy"));
            Assert.DoesNotThrow(() => Invoke("Update"));
            if (type == typeof(AttributeUiRoot))
                Assert.That(Get("_clientBound"), Is.False, "客户端丢失时也必须清掉陈旧的绑定标志");
        }

        [TestCase(typeof(AttributeUiRoot))]
        [TestCase(typeof(GuildUiRoot))]
        [TestCase(typeof(RankUiRoot))]
        public void NativeViewDestroyedBeforeOwnerShutdownIgnoresLateUpdates(Type type)
        {
            CreateOwner(type);
            if (type == typeof(AttributeUiRoot))
            {
                Set("_app", _fixture.AddComponent<AppBootstrap>());
                Set("_canvasGo", _view);
                Set("_panel", new AttributePanel((AttributeUiRoot)_owner, _view.transform));
            }
            else if (type == typeof(GuildUiRoot)) Set("_window", new GuildWindow(_view.transform));
            else Set("_window", new RankWindow(_view.transform));
            Object.DestroyImmediate(_view);
            Assert.DoesNotThrow(() => Invoke("Update"));
            Assert.DoesNotThrow(() => Invoke("OnDisable"));
            Assert.DoesNotThrow(() => Invoke("OnDestroy"));
            Assert.DoesNotThrow(() => Invoke("Update"));
        }

        private void CreateOwner(Type type)
        {
            _fixture = new GameObject("UiShutdownTests");
            _fixture.SetActive(false);
            _owner = (MonoBehaviour)_fixture.AddComponent(type);
            _view = new GameObject("OwnedView", typeof(RectTransform), typeof(Canvas));
            _view.transform.SetParent(_fixture.transform, false);
            var hud = new GameObject("HudRoot", typeof(RectTransform));
            hud.transform.SetParent(_view.transform, false);
            Set(type == typeof(AttributeUiRoot) ? "_hudRoot" : "_hud", (RectTransform)hud.transform);
        }

        private void Set(string name, object value) => _owner.GetType().GetField(name, Fields).SetValue(_owner, value);
        private object Get(string name) => _owner.GetType().GetField(name, Fields).GetValue(_owner);
        private void Invoke(string method) => _owner.GetType().GetMethod(method, Fields).Invoke(_owner, null);
    }
}
