using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Rank;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Rank
{
    /// <summary>排行榜热键在界面重建留下失效选中对象时仍应正常检查输入焦点。</summary>
    public sealed class RankTypingFocusTests
    {
        private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private List<EventSystem> _registeredEvents;
        private EventSystem[] _previousEvents;
        private GameObject _fixture;
        private EventSystem _events;

        [SetUp]
        public void SetUp()
        {
            // EditMode 中显式隔离并恢复注册表，避免测试受编辑器当前场景的 EventSystem 影响。
            var registry = typeof(EventSystem).GetField("m_EventSystems", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(registry, Is.Not.Null);
            _registeredEvents = (List<EventSystem>)registry.GetValue(null);
            _previousEvents = _registeredEvents.ToArray();
            _registeredEvents.Clear();
            _fixture = new GameObject("RankTypingFocusTests", typeof(RectTransform), typeof(Canvas));
            _fixture.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            if (_events != null) _events.SetSelectedGameObject(null);
            if (_fixture != null) Object.DestroyImmediate(_fixture);
            if (_registeredEvents != null)
            {
                _registeredEvents.Clear();
                if (_previousEvents != null)
                    foreach (var previous in _previousEvents)
                        if (previous != null) _registeredEvents.Add(previous);
            }
        }

        [Test]
        public void MissingEventSystem_DoesNotBlockHotkeysOrThrow()
        {
            Assert.That(EventSystem.current, Is.Null);
            AssertTyping(false);
        }

        [Test]
        public void NoSelection_DoesNotBlockHotkeysOrThrow()
        {
            CreateEventSystem();
            Assert.That(_events.currentSelectedGameObject, Is.Null);
            AssertTyping(false);
        }

        [Test]
        public void DestroyedSelection_DoesNotBlockHotkeysOrThrow()
        {
            CreateEventSystem();
            // 无 Selectable.OnDisable 的普通对象会在 EventSystem 中留下原生对象已销毁的托管引用。
            var selected = new GameObject("DestroyedSelection");
            selected.transform.SetParent(_fixture.transform, false);
            _events.SetSelectedGameObject(selected);
            Object.DestroyImmediate(selected);

            Assert.That(ReferenceEquals(_events.currentSelectedGameObject, null), Is.False,
                "回归用例必须保留托管壳，普通 CLR null 无法复现运行时异常");
            Assert.That(_events.currentSelectedGameObject == null, Is.True);
            AssertTyping(false);
        }

        [Test]
        public void SelectedButton_DoesNotBlockHotkeysOrThrow()
        {
            CreateEventSystem();
            var selected = new GameObject("SelectedButton", typeof(RectTransform), typeof(Button));
            selected.transform.SetParent(_fixture.transform, false);
            _events.SetSelectedGameObject(selected);
            AssertTyping(false);
        }

        [Test]
        public void FocusedTmpInput_BlocksHotkeysUntilFocusIsReleased()
        {
            CreateEventSystem();
            var input = QdaoUguiFactory.CreateInputField("TypingInput", _fixture.transform,
                0, 0, 400, 60, "输入内容", 80);
            input.caretBlinkRate = 0;
            _events.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            // EditMode 不推进 MonoBehaviour 的逐帧生命周期；运行 TMP 自己的激活步骤，不伪造 isFocused。
            typeof(TMP_InputField).GetMethod("LateUpdate", HiddenInstance).Invoke(input, null);

            Assert.That(input.isFocused, Is.True, "使用真实聚焦的 TMP 输入框验证热键抑制");
            AssertTyping(true);

            input.DeactivateInputField();
            _events.SetSelectedGameObject(null);
            Assert.That(input.isFocused, Is.False);
            AssertTyping(false);
        }

        private void CreateEventSystem()
        {
            var go = new GameObject("RankTypingEvents", typeof(EventSystem));
            go.transform.SetParent(_fixture.transform, false);
            _events = go.GetComponent<EventSystem>();
            // EventSystem 没有 ExecuteAlways，EditMode 创建后显式注册其生产生命周期。
            if (!_registeredEvents.Contains(_events))
                typeof(EventSystem).GetMethod("OnEnable", HiddenInstance).Invoke(_events, null);
            EventSystem.current = _events;
            Assert.That(EventSystem.current, Is.SameAs(_events));
        }

        private static void AssertTyping(bool expected)
        {
            var method = typeof(RankUiRoot).GetMethod("IsTyping", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object actual = null;
            Assert.DoesNotThrow(() => actual = method.Invoke(null, null));
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
