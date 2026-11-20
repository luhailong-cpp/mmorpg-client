using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
 public sealed class PetEntriesClientTests
 {
  private FakeBattleTransport net;private PetClient client;private bool locked;
  private const ulong PetId=901;
  [SetUp] public void SetUp(){net=new FakeBattleTransport();locked=false;client=new PetClient(net,()=>locked);Publish(Info());}
  private static PetEntriesInfo Info(uint allocated=0,ulong version=1,string request=""){
   var i=new PetEntriesInfo{PetId=PetId,StateVersion=version,ConfigVersion=1,TotalPoints=20,RemainingPoints=20-allocated,Effective=true,LastRequestId=request};
   i.Entries.Add(new PetEntryInfo{Id=503,Name="治疗效果",Cap=20,Allocated=allocated,PerPointBps=50,EffectiveBps=50*allocated,Category=3});return i;}
  private void Publish(PetEntriesInfo info){var list=new PetListInfo();list.Pets.Add(new PetInfo{PetId=PetId,Entries=info});net.PushNotify(MessageIds.NotifyPetListChanged,new PetListChangedS2C{Pets=list});}
  [Test] public void PreviewNeverCommitsAndOnlyMatchingPetResponseIsAccepted(){
   PetEntriesInfo preview=null;client.PreviewEntries(PetId,1,1,new Dictionary<uint,uint>{{503,4}},(p,e)=>preview=p);
   net.Calls.Last().Respond(new PreviewPetEntriesResponse{Entries=Info(4)});
   Assert.That(preview.Entries[0].Allocated,Is.EqualTo(4));Assert.That(client.Pets.Pets[0].Entries.Entries[0].Allocated,Is.Zero);
  }
  [Test] public void TimeoutRetryRetainsExactRequestAndRejectsChangedDraftSubstitution(){
   client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},null);var first=(SavePetEntriesRequest)net.Calls.Last().Request;
   net.Calls.Last().FailWith("timeout");Assert.That(client.HasPendingEntries(PetId),Is.True);
   bool accepted=false;client.SaveEntries(PetId,99,99,new Dictionary<uint,uint>{{503,10}},(ok,p,e)=>accepted=ok);
   var retry=(SavePetEntriesRequest)net.Calls.Last().Request;Assert.That(retry,Is.EqualTo(first));
   net.Calls.Last().Respond(new SavePetEntriesResponse{RequestId=first.RequestId,Entries=Info(2,2,first.RequestId),Replayed=true});
   Assert.That(accepted,Is.True);Assert.That(client.HasPendingEntries(PetId),Is.False);Assert.That(client.Pets.Pets[0].Entries.Entries[0].Allocated,Is.EqualTo(2));
  }
  [Test] public void UnrelatedListsAndWrongReceiptCannotAcknowledgeSave(){
   bool accepted=false;client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},(ok,p,e)=>accepted=ok);var call=net.Calls.Last();
   Publish(Info(2,2,"other-request"));Assert.That(accepted,Is.False);Assert.That(client.HasPendingEntries(PetId),Is.True);
   call.Respond(new SavePetEntriesResponse{RequestId="other-request",Entries=Info(2,2,"other-request")});
   Assert.That(accepted,Is.False);Assert.That(client.HasPendingEntries(PetId),Is.True);
  }
  [Test] public void RejectionAllowsFreshExplicitRetryUsingLatestVersion(){
   client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},null);var first=(SavePetEntriesRequest)net.Calls.Last().Request;
   net.Calls.Last().Respond(new SavePetEntriesResponse{ErrorMessage=new TipInfoMessage{Id=1},Entries=Info(5,2),RequestId=first.RequestId});
   Assert.That(client.HasPendingEntries(PetId),Is.False);Assert.That(client.Pets.Pets[0].Entries.Entries[0].Allocated,Is.EqualTo(5));
   client.SaveEntries(PetId,2,1,new Dictionary<uint,uint>{{503,1}},null);var next=(SavePetEntriesRequest)net.Calls.Last().Request;
   Assert.That(next.RequestId,Is.Not.EqualTo(first.RequestId));Assert.That(next.StateVersion,Is.EqualTo(2));
  }
  [Test] public void BattleLockSendsNothingAndDoesNotCreateUnsentRetry(){
   locked=true;client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},null);
   Assert.That(net.Calls,Is.Empty);Assert.That(client.HasPendingEntries(PetId),Is.False);Assert.That(client.Busy,Is.False);
  }
  [Test] public void DisconnectInvalidatesLateCallbackAndRetainsOnlyExplicitRetry(){
   bool accepted=false;client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},(ok,p,e)=>accepted=ok);
   var old=net.Calls.Last();var r=(SavePetEntriesRequest)old.Request;net.RaiseDisconnected();
   old.Respond(new SavePetEntriesResponse{RequestId=r.RequestId,Entries=Info(2,2,r.RequestId)});
   Assert.That(accepted,Is.False);Assert.That(client.HasList,Is.False);Assert.That(client.Busy,Is.False);
   Publish(Info(2,2,r.RequestId));Assert.That(net.Calls.Count,Is.EqualTo(1));
   client.SaveEntries(PetId,2,1,new Dictionary<uint,uint>{{503,7}},null);
   Assert.That((SavePetEntriesRequest)net.Calls.Last().Request,Is.EqualTo(r));
  }
  [Test] public void StaleReadCannotRollbackConfirmedAllocation(){
   client.RequestEntries(PetId);var read=net.Calls.Last();client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},null);
   var r=(SavePetEntriesRequest)net.Calls.Last().Request;net.Calls.Last().Respond(new SavePetEntriesResponse{RequestId=r.RequestId,Entries=Info(2,2,r.RequestId)});
   read.Respond(new GetPetEntriesResponse{Entries=Info()});Assert.That(client.Pets.Pets[0].Entries.StateVersion,Is.EqualTo(2));
  }
  [Test] public void EmptyOrMissingTargetRowsCannotAcknowledgeSuccessfulReceipt(){
   bool accepted=false;client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},(ok,p,e)=>accepted=ok);
   var sent=(SavePetEntriesRequest)net.Calls.Last().Request;var incomplete=Info(2,2,sent.RequestId);incomplete.Entries.Clear();
   net.Calls.Last().Respond(new SavePetEntriesResponse{RequestId=sent.RequestId,Entries=incomplete});
   Assert.That(accepted,Is.False);Assert.That(client.HasPendingEntries(PetId),Is.True);
   client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},(ok,p,e)=>accepted=ok);
   incomplete.Entries.Add(new PetEntryInfo{Id=501,Allocated=0});
   net.Calls.Last().Respond(new SavePetEntriesResponse{RequestId=sent.RequestId,Entries=incomplete});
   Assert.That(accepted,Is.False);Assert.That(client.HasPendingEntries(PetId),Is.True);
  }
  [Test] public void LateSaveReceiptCannotRollbackNewerAuthoritativeList(){
   bool accepted=false;client.SaveEntries(PetId,1,1,new Dictionary<uint,uint>{{503,2}},(ok,p,e)=>accepted=ok);
   var call=net.Calls.Last();var sent=(SavePetEntriesRequest)call.Request;Publish(Info(7,3,"newer-request"));
   call.Respond(new SavePetEntriesResponse{RequestId=sent.RequestId,Entries=Info(2,2,sent.RequestId)});
   Assert.That(accepted,Is.True);Assert.That(client.HasPendingEntries(PetId),Is.False);
   Assert.That(client.Pets.Pets[0].Entries.StateVersion,Is.EqualTo(3));Assert.That(client.Pets.Pets[0].Entries.Entries[0].Allocated,Is.EqualTo(7));
  }
  [Test] public void ZeroEntriesAreOmittedOnWireAndAllZeroSaveCommitsCleanly(){
   var full=new Dictionary<uint,uint>();for(uint id=501;id<=521;id++) full[id]=id==503?1u:0u;
   client.PreviewEntries(PetId,1,1,full,null);
   var previewReq=(PreviewPetEntriesRequest)net.Calls.Last().Request;
   Assert.That(previewReq.Target.Count,Is.EqualTo(1));Assert.That(previewReq.Target[503],Is.EqualTo(1));
   full[503]=0;bool cleared=false;
   client.SaveEntries(PetId,1,1,full,(ok,p,e)=>cleared=ok);
   var saveReq=(SavePetEntriesRequest)net.Calls.Last().Request;
   Assert.That(saveReq.Target.Count,Is.Zero);
   net.Calls.Last().Respond(new SavePetEntriesResponse{RequestId=saveReq.RequestId,Entries=Info(0,2,saveReq.RequestId)});
   Assert.That(cleared,Is.True);Assert.That(client.HasPendingEntries(PetId),Is.False);
  }
 }
}
