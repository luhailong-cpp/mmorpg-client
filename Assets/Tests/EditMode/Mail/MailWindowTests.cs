using System.Linq;
using MmorpgClient.Game.Mail;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Mail;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Mail
{
    public sealed class MailWindowTests
    {
        private GameObject _canvas,_events;
        private MailWindow _window;
        private MailUiState _state;
        private const long Now=1800000000;
        [SetUp] public void SetUp()
        {
            _events=new GameObject("MailTestEvents",typeof(EventSystem));
            _canvas=new GameObject("MailTestCanvas",typeof(RectTransform),typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var design=QdaoUguiFactory.CreateCenteredRect("Design",_canvas.transform,2560,1080);
            _state=new MailUiState(()=>Now);_window=new MailWindow(design,_state);
        }
        [TearDown] public void TearDown()
        {
            _window?.Dispose();if(_canvas!=null)Object.DestroyImmediate(_canvas);if(_events!=null)Object.DestroyImmediate(_events);
        }
        private Button Button(string name)=>_canvas.GetComponentsInChildren<Button>(true).Single(x=>x.name==name);
        private TMP_Text Text(string name)=>_canvas.GetComponentsInChildren<TMP_Text>(true).Single(x=>x.name==name);
        private void Demo(){_state.LoadDemo();_window.Show();Canvas.ForceUpdateCanvases();}
        [Test] public void UnavailableWindowHasNoClaimableDemoData()
        {
            _window.Show();Assert.That(_state.Messages,Is.Empty);Assert.That(Button("MailClaimAll").interactable,Is.False);
            Assert.That(Text("MailUnavailableTitle").text,Is.EqualTo("邮件暂未开放"));
            Assert.That(Button("MailRefresh").interactable,Is.False);
        }
        [Test] public void ListBodyAndRewardsHaveIndependentMasksAndFixedActionsAreOutsideThem()
        {
            Demo();Assert.That(_window.ListScroll,Is.Not.SameAs(_window.BodyScroll));
            foreach(var scroll in new[]{_window.ListScroll,_window.BodyScroll,_window.RewardScroll})
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(),Is.Not.Null);
            foreach(var name in new[]{"MailClaimSelected","MailClaimAll","MailDelete","MailClearRead"})
            {
                var rect=(RectTransform)Button(name).transform;
                Assert.That(rect.parent,Is.EqualTo(_window.Frame));Assert.That(rect.GetComponentInParent<ScrollRect>(),Is.Null);
                Assert.That(-rect.anchoredPosition.y+rect.rect.height,Is.LessThanOrEqualTo(_window.Frame.rect.height));
            }
            Assert.That(_window.BodyScroll.content.rect.height,Is.GreaterThan(_window.BodyScroll.viewport.rect.height));
        }
        [Test] public void OpeningReadsButClaimButtonAloneClaimsAndDisables()
        {
            Demo();Assert.That(_state.Selected.IsRead,Is.True);Assert.That(_state.Selected.Claimed,Is.False);
            Button("MailClaimSelected").onClick.Invoke();
            Assert.That(_state.Selected.Claimed,Is.True);Assert.That(Button("MailClaimSelected").interactable,Is.False);
        }
        [Test] public void ExpiredAndNotificationStatesHaveAppropriateActions()
        {
            Demo();_state.Select("expired");Assert.That(Button("MailClaimSelected").interactable,Is.False);
            Assert.That(Button("MailClaimSelected").GetComponentInChildren<TMP_Text>().text,Is.EqualTo("已过期"));
            _state.Select("notice");Assert.That(Button("MailClaimSelected").gameObject.activeSelf,Is.False);
            Assert.That(Text("MailNoAttachments").text,Does.Contain("没有附件"));
        }
        [Test] public void DeletionProtectsPendingRewardsAndConfirmationHasSafeCancelFocus()
        {
            Demo();Button("MailDelete").onClick.Invoke();Assert.That(_window.DialogIsOpen,Is.False);
            Assert.That(_state.Messages.Count,Is.EqualTo(6));_state.Select("claimed");Button("MailDelete").onClick.Invoke();
            Assert.That(_window.DialogIsOpen,Is.True);Assert.That(EventSystem.current.currentSelectedGameObject.name,Is.EqualTo("MailDialogCancel"));
            Assert.That(_window.Frame.GetComponent<CanvasGroup>().interactable,Is.False);
            _window.Back();Assert.That(_window.DialogIsOpen,Is.False);Assert.That(_state.Messages.Count,Is.EqualTo(6));
            Button("MailDelete").onClick.Invoke();Button("MailDialogConfirm").onClick.Invoke();
            Assert.That(_state.Messages.Any(x=>x.Id=="claimed"),Is.False);
        }
        [Test] public void ActivityDialogIsExplicitlyOfflineAndEscapeClosesOnlyDialog()
        {
            Demo();_window.OpenActivity();Assert.That(_window.DialogIsOpen,Is.True);
            Assert.That(Text("MailActivityPreviewNotice").text,Does.Contain("非真实"));
            _window.Back();Assert.That(_window.IsVisible,Is.True);Assert.That(_window.DialogIsOpen,Is.False);
            _window.Back();Assert.That(_window.IsVisible,Is.False);
        }
        [Test] public void ManyAttachmentsAreAllRetainedInsideHorizontalScrollableViewport()
        {
            var message=new MailMessage{Id="many",Title="附件完整性",IsRead=true,Rewards=Enumerable.Range(1,12).Select(i=>new MailReward{Name="灵玉"+i,IconKey="round_badge_lotus",Count=i}).ToList()};
            _state.SetSnapshot(new[]{message});_window.Show();Canvas.ForceUpdateCanvases();
            Assert.That(_window.RewardScroll.horizontal,Is.True);Assert.That(_window.RewardScroll.vertical,Is.False);
            Assert.That(_window.RewardScroll.content.rect.width,Is.GreaterThan(_window.RewardScroll.viewport.rect.width));
            Assert.That(_window.RewardScroll.content.GetComponentsInChildren<TMP_Text>().Count(t=>t.name.StartsWith("RewardName_")),Is.EqualTo(12));
            Assert.That(_window.RewardScroll.viewport.rect.width,Is.EqualTo(830));
            Assert.That(Button("MailDelete").transform.IsChildOf(_window.RewardScroll.transform),Is.False);
        }
        [Test] public void OtherEventBannerHasNoHardcodedMidautumnTitleAndUserTextIsLiteral()
        {
            _state.SetSnapshot(new[]{new MailMessage{Id="spring",Title="<b>春日来信</b>",Body="<size=999>道友</size>",IsRead=true,BannerKey="event-midautumn",ActivityId="spring"}});
            _window.Show();Assert.That(Text("MailDetailTitle").text,Is.EqualTo("<b>春日来信</b>"));
            Assert.That(Text("MailDetailTitle").richText,Is.False);Assert.That(Text("MailBodyText").richText,Is.False);
            Assert.That(_canvas.GetComponentsInChildren<TMP_Text>().Any(t=>t.name=="ActivityKicker"),Is.False);
        }
        [Test] public void HideRestoresFocusAndBalancesGameplayInputBlocker()
        {
            bool blocked=GameplayInputGate.IsKeyboardBlocked;var opener=Button("CloseMail").gameObject;
            EventSystem.current.SetSelectedGameObject(_canvas);_window.Show();
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(opener));
            _window.Hide();Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(_canvas));
            Assert.That(GameplayInputGate.IsKeyboardBlocked,Is.EqualTo(blocked));
        }
        [Test] public void SessionResetClearsSelectionDataAndDialog()
        {
            Demo();_window.OpenActivity();_window.ResetSession();
            Assert.That(_window.IsVisible,Is.False);Assert.That(_window.DialogIsOpen,Is.False);
            Assert.That(_state.IsDemo,Is.False);Assert.That(_state.Messages,Is.Empty);
        }
    }
}
