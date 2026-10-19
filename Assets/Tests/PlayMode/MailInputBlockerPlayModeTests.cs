using System.Collections;
using System.Linq;
using MmorpgClient.Game.Mail;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Mail;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    public sealed class MailInputBlockerPlayModeTests
    {
        private GameObject _canvas, _eventObject, _other;
        private EventSystem _events, _previousEvents;
        private MailWindow _window;
        private MailUiState _state;
        private bool _keyboardBefore, _pointerBefore;
        [UnitySetUp] public IEnumerator SetUp()
        {
            _previousEvents=EventSystem.current;
            _eventObject=new GameObject("MailInputTestEvents",typeof(EventSystem));
            _events=_eventObject.GetComponent<EventSystem>();EventSystem.current=_events;
            _keyboardBefore=GameplayInputGate.IsKeyboardBlocked;_pointerBefore=GameplayInputGate.IsPointerBlocked;
            Assert.That(_keyboardBefore,Is.False,"Requires an isolated test scene.");
            Assert.That(_pointerBefore,Is.False,"Requires an isolated test scene.");
            _canvas=new GameObject("MailInputTestCanvas",typeof(RectTransform),typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var design=QdaoUguiFactory.CreateCenteredRect("Design",_canvas.transform,2560,1080);
            _state=new MailUiState(()=>1800000000);_state.LoadDemo();_window=new MailWindow(design,_state);
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            _window?.Dispose();_window=null;
            if(_canvas!=null)Object.Destroy(_canvas);
            if(_other!=null)Object.Destroy(_other);
            if(_eventObject!=null){_eventObject.SetActive(false);Object.Destroy(_eventObject);}
            EventSystem.current=_previousEvents;yield return null;
        }
        [UnityTest] public IEnumerator ShowingAndHidingRestoresInputAndOpenerFocus()
        {
            _events.SetSelectedGameObject(_canvas);_window.Show();yield return null;
            Assert.That(_events.currentSelectedGameObject,Is.SameAs(Find("CloseMail").gameObject));
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.True);Assert.That(GameplayInputGate.IsPointerBlocked,Is.True);
            _window.Hide();yield return null;
            Assert.That(_events.currentSelectedGameObject,Is.SameAs(_canvas));
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.EqualTo(_keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked,Is.EqualTo(_pointerBefore));
        }
        [UnityTest] public IEnumerator ConfirmationFocusAndCancelKeepWindowBlocked()
        {
            _window.Show();_state.Select("claimed");Find("MailDelete").onClick.Invoke();yield return null;
            Assert.That(_window.DialogIsOpen,Is.True);
            Assert.That(_events.currentSelectedGameObject,Is.SameAs(Find("MailDialogCancel").gameObject));
            Assert.That(_window.Frame.GetComponent<CanvasGroup>().interactable,Is.False);
            _window.Back();yield return null;
            Assert.That(_window.DialogIsOpen,Is.False);Assert.That(_window.IsVisible,Is.True);
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.True);
            Assert.That(_window.Frame.GetComponent<CanvasGroup>().interactable,Is.True);
            _other=new GameObject("OtherMailTestBlocker",typeof(GameplayInputBlocker));yield return null;
            _window.Hide();yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.True);
            _other.SetActive(false);yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.EqualTo(_keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked,Is.EqualTo(_pointerBefore));
        }
        private Button Find(string name)=>_canvas.GetComponentsInChildren<Button>(false).Single(b=>b.name==name);
    }
}
