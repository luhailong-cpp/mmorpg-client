using System.Collections;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Guild;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.UI.Ugui.Mail;
using MmorpgClient.UI.Ugui.Social;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>销毁后的 Unity 选中对象不能让各窗口的逐帧热键检查抛异常。</summary>
    public sealed class UiTypingFocusPlayModeTests
    {
        private static readonly System.Type[] Roots =
        {
            typeof(GuildUiRoot), typeof(JubaozhaiUiRoot), typeof(MailUiRoot), typeof(SocialUiRoot),
        };

        private GameObject _fixture;
        private EventSystem _events, _previousEvents;

        [SetUp]
        public void SetUp()
        {
            _previousEvents = EventSystem.current;
            _fixture = new GameObject("UiTypingFocusPlayModeTests", typeof(RectTransform), typeof(Canvas));
            _fixture.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var eventsObject = new GameObject("TypingFocusEvents", typeof(EventSystem));
            eventsObject.transform.SetParent(_fixture.transform, false);
            _events = eventsObject.GetComponent<EventSystem>();
            EventSystem.current = _events;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_events != null) _events.SetSelectedGameObject(null);
            if (_fixture != null)
            {
                _fixture.SetActive(false);
                Object.Destroy(_fixture);
            }
            if (_previousEvents != null) EventSystem.current = _previousEvents;
            _fixture = null;
            _events = _previousEvents = null;
            yield return null;
        }

        [Test]
        public void NoSelection_DoesNotBlockHotkeysOrThrow()
        {
            _events.SetSelectedGameObject(null);
            AssertAllTyping(false);
        }

        [Test]
        public void OrdinarySelectedObject_DoesNotBlockHotkeysOrThrow()
        {
            var selected = new GameObject("OrdinarySelection");
            selected.transform.SetParent(_fixture.transform, false);
            _events.SetSelectedGameObject(selected);
            AssertAllTyping(false);
        }

        [Test]
        public void DestroyedSelectedObject_DoesNotBlockHotkeysOrThrow()
        {
            // 普通对象没有 Selectable.OnDisable，原生对象销毁后 EventSystem 仍持有托管引用。
            var selected = new GameObject("DestroyedSelection");
            selected.transform.SetParent(_fixture.transform, false);
            _events.SetSelectedGameObject(selected);
            Object.DestroyImmediate(selected);

            var staleSelection = _events.currentSelectedGameObject;
            Assert.That(ReferenceEquals(staleSelection, null), Is.False, "必须覆盖仍存在的托管引用");
            Assert.That(staleSelection == null, Is.True, "Unity 原生选中对象已经销毁");
            AssertAllTyping(false);
        }

        [UnityTest]
        public IEnumerator FocusedTmpInput_BlocksHotkeysUntilFocusIsReleased()
        {
            var input = QdaoUguiFactory.CreateInputField("TypingInput", _fixture.transform,
                0, 0, 400, 60, "输入内容", 80);
            _events.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            yield return null;

            Assert.That(input.isFocused, Is.True, "正向用例必须使用真实聚焦的 TMP 输入框");
            AssertAllTyping(true);

            input.DeactivateInputField();
            _events.SetSelectedGameObject(null);
            Assert.That(input.isFocused, Is.False);
            AssertAllTyping(false);
        }

        private static void AssertAllTyping(bool expected)
        {
            foreach (var root in Roots)
            {
                // Guild 的检查是私有方法；所有根节点都调用真实生产检查，不复制其实现。
                var method = root.GetMethod("IsTyping", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null, root.Name + ".IsTyping");
                object actual = null;
                Assert.DoesNotThrow(() => actual = method.Invoke(null, null), root.Name + ".IsTyping");
                Assert.That(actual, Is.EqualTo(expected), root.Name + ".IsTyping");
            }
        }
    }
}
