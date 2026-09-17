using System;
using System.Linq;
using MmorpgClient.Game.Mail;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Mail
{
    public sealed class MailUiStateTests
    {
        private long _now;
        private MailUiState _state;
        [SetUp] public void SetUp(){_now=1800000000;_state=new MailUiState(()=>_now);}
        private MailMessage Message(string id="a",long expires=0,bool read=true,int count=2)=>new()
        { Id=id,Title=id,Category=MailCategory.Reward,IsRead=read,ExpiresAt=expires,
          Rewards=new(){new MailReward{Name="灵玉",IconKey="round_badge_lotus",Count=count}} };

        [Test] public void ProductionStartsUnavailableAndNeverLoadsDemo()
        {
            Assert.That(_state.IsDemo,Is.False);Assert.That(_state.ServiceAvailable,Is.False);
            Assert.That(_state.Messages,Is.Empty);Assert.That(_state.CanAct,Is.False);
            Assert.That(_state.ClaimAll(),Is.Zero);Assert.That(_state.Status,Is.EqualTo(MailUiState.UnavailableMessage));
        }
        [Test] public void ReadingNeverClaimsAndClaimingDoesNotReadOtherMessages()
        {
            _state.LoadDemo();_state.Select("midautumn");
            Assert.That(_state.Selected.IsRead,Is.True);Assert.That(_state.Selected.Claimed,Is.False);
            _state.ClaimAll();
            Assert.That(_state.Messages.Single(m=>m.Id=="maintenance").Claimed,Is.True);
            Assert.That(_state.Messages.Single(m=>m.Id=="maintenance").IsRead,Is.False);
        }
        [Test] public void BulkClaimIncludesOtherCategoriesButSkipsExpiredClaimedAndNotice()
        {
            _state.LoadDemo();_state.SetCategory(MailCategory.System);_state.ClaimAll();
            Assert.That(_state.ClaimableCount,Is.Zero);
            Assert.That(_state.Messages.Count(m=>m.Claimed),Is.EqualTo(4));
            Assert.That(_state.Messages.Single(m=>m.Id=="expired").Claimed,Is.False);
            Assert.That(_state.Messages.Single(m=>m.Id=="notice").Claimed,Is.False);
            Assert.That(_state.Status,Is.EqualTo("已领取 3 封邮件，共 8 项附件。"));
        }
        [Test] public void DeleteProtectsValidAttachmentsButAllowsExpired()
        {
            _state.LoadDemo();_state.Select("midautumn");Assert.That(_state.DeleteSelected(),Is.Zero);
            Assert.That(_state.Messages.Count,Is.EqualTo(6));
            _state.Select("expired");Assert.That(_state.DeleteSelected(),Is.EqualTo(1));
            Assert.That(_state.Messages.Any(m=>m.Id=="expired"),Is.False);
        }
        [Test] public void ClearReadKeepsUnreadNotificationsAndUnreadOrReadPendingRewards()
        {
            _state.LoadDemo();_state.Select("midautumn");_state.ClearRead();
            CollectionAssert.AreEquivalent(new[]{"midautumn","maintenance","notice","adventure"},_state.Messages.Select(m=>m.Id));
            _state.Select("notice");_state.ClearRead();
            CollectionAssert.AreEquivalent(new[]{"midautumn","maintenance","adventure"},_state.Messages.Select(m=>m.Id));
        }
        [Test] public void ExpiryBoundaryIsInclusiveAndZeroHasNoExpiry()
        {
            Assert.That(_state.HasValidAttachments(Message(expires:_now)),Is.False);
            Assert.That(_state.HasValidAttachments(Message(expires:_now+1)),Is.True);
            Assert.That(_state.HasValidAttachments(Message(expires:0)),Is.True);
            _state.SetSnapshot(new[]{Message(expires:_now+1)});int changes=0;_state.Changed+=()=>changes++;
            _now++;_state.Tick();Assert.That(_state.CanClaimSelected,Is.False);Assert.That(changes,Is.EqualTo(1));
        }
        [Test] public void SnapshotAndAllReadModelsAreDeepCopies()
        {
            var source=Message();_state.SetSnapshot(new[]{source});
            source.Title="outside";source.Rewards[0].Count=999;
            var selected=_state.Selected;selected.Title="selected";selected.Rewards[0].Count=888;
            var messages=_state.Messages;messages[0].Rewards.Clear();
            Assert.That(_state.Selected.Title,Is.EqualTo("a"));
            Assert.That(_state.Selected.Rewards.Single().Count,Is.EqualTo(2));
        }
        [Test] public void InvalidIdsDuplicateMessagesAndNonPositiveRewardsAreNormalized()
        {
            var source=Message(count:0);source.Rewards.Add(null);source.Rewards.Add(new MailReward{Count=-4});
            _state.SetSnapshot(new[]{null,new MailMessage{Id=""},source,Message()});
            Assert.That(_state.Messages.Count,Is.EqualTo(1));Assert.That(_state.Selected.Rewards,Is.Empty);
            Assert.That(_state.ClaimableCount,Is.Zero);
        }
        [Test] public void ProductionClaimWaitsForMatchingAuthoritativeSnapshot()
        {
            _state.SetSnapshot(new[]{Message()});MailRequest request=null;_state.ActionRequested+=r=>request=r;
            int generation=_state.ClaimSelected();Assert.That(request.Operation,Is.EqualTo(MailOperation.Claim));
            Assert.That(_state.IsBusy,Is.True);Assert.That(_state.Selected.Claimed,Is.False);
            Assert.That(_state.Complete(generation+1,new[]{Message()}),Is.False);
            var authority=Message();authority.Claimed=true;
            Assert.That(_state.Complete(generation,new[]{authority}),Is.True);
            Assert.That(_state.Selected.Claimed,Is.True);Assert.That(_state.IsBusy,Is.False);
        }
        [Test] public void AccountResetAndReplacementSnapshotRejectLateCallbacks()
        {
            _state.SetSnapshot(new[]{Message()});_state.ActionRequested+=_=>{};
            int generation=_state.ClaimSelected();_state.Reset(77);_state.SetSnapshot(new[]{Message("b")});
            Assert.That(_state.Complete(generation,new[]{Message()}),Is.False);
            Assert.That(_state.Fail(generation,"old failure"),Is.False);
            Assert.That(_state.PlayerId,Is.EqualTo(77));Assert.That(_state.Selected.Id,Is.EqualTo("b"));
            int newer=_state.ClaimSelected();_state.SetSnapshot(new[]{Message("c")});
            Assert.That(_state.Complete(newer,new[]{Message()}),Is.False);Assert.That(_state.Selected.Id,Is.EqualTo("c"));
        }
        [Test] public void UncertainMutationTimeoutLocksUntilSuccessfulRefreshEvenIfRefreshTimesOut()
        {
            _state.SetSnapshot(new[]{Message()});_state.ActionRequested+=_=>{};
            int generation=_state.ClaimSelected();_now+=10;_state.Tick();
            Assert.That(_state.RequiresRefresh,Is.True);Assert.That(_state.CanAct,Is.False);Assert.That(_state.CanRefresh,Is.True);
            Assert.That(_state.ClaimSelected(),Is.Zero);Assert.That(_state.Complete(generation,new[]{Message()}),Is.False);
            int refresh=_state.Refresh();Assert.That(refresh,Is.Not.Zero);_now+=10;_state.Tick();
            Assert.That(_state.RequiresRefresh,Is.True);Assert.That(_state.CanAct,Is.False);
            refresh=_state.Refresh();Assert.That(_state.Complete(refresh,new[]{Message()}),Is.True);
            Assert.That(_state.RequiresRefresh,Is.False);Assert.That(_state.CanAct,Is.True);
        }
        [Test] public void MissingAdapterCannotPretendProductionSuccess()
        {
            _state.SetSnapshot(new[]{Message()});Assert.That(_state.ClaimSelected(),Is.Zero);
            Assert.That(_state.ServiceAvailable,Is.False);Assert.That(_state.Selected.Claimed,Is.False);
        }
        [Test] public void PendingBulkRequestContainsOnlyValidPositiveAttachments()
        {
            _state.SetSnapshot(new[]{Message("valid"),Message("zero",count:0),Message("expired",expires:_now),new MailMessage{Id="notice",IsRead=true}});
            MailRequest request=null;_state.ActionRequested+=r=>request=r;_state.ClaimAll();
            CollectionAssert.AreEqual(new[]{"valid"},request.MessageIds);Assert.That(_state.Selected.Claimed,Is.False);
        }
    }
}
