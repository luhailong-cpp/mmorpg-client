using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using MmorpgClient.Game;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEngine;

internal static class Program
{
    private static readonly List<string> Passed=new();
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Assertion failed"); }
    private static void Set(object instance,string field,object value) => instance.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(instance,value);
    private static T Get<T>(object instance,string field) => (T)instance.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);
    private static void Check(string name,Action action) { action();Passed.Add(name); }
    private sealed class Fixture
    {
        public readonly TianyongMapRuntime Runtime=new GameObject().AddComponent<TianyongMapRuntime>();
        public readonly TianyongMapInstance Old=new();
        public readonly CurrentMapDefinition Before=new();
        public readonly CurrentMapDefinition Next=new() { ContentIdentity="test:v002:normal:bbbb" };
        public readonly ActorView Actor=new() { Go=new GameObject(),HasTarget=true,Velocity=new Vector3(4,0,3) };
        public readonly TianyongPlayerController Player;
        public readonly TianyongCameraController Camera=new(null,24,55,35);
        public Fixture()
        {
            Actor.Go.transform.position=new Vector3(20,0,30);
            Player=Actor.Go.AddComponent<TianyongPlayerController>();Player.Navigation=Old.Navigation;
            Camera.Target=Actor.Go.transform;Camera.Bounds=new Rect(7,9,50,60);
            Set(Runtime,"_map",Old);Set(Runtime,"_currentMap",Before);Set(Runtime,"_activeSceneConfigId",21u);
            Set(Runtime,"_localPlayer",Actor);Set(Runtime,"_cameraController",Camera);
            CurrentMapCatalog.Selected=Before;CurrentMapCatalog.Reload=() => CurrentMapCatalog.Selected=Next;
            Next.BeforeBuild=() => Require(ReferenceEquals(Runtime.Map,Old) && Old.DisposeCalls==0 &&
                ReferenceEquals(Player.Navigation,Old.Navigation) && ReferenceEquals(Camera.Target,Actor.Go.transform));
        }
        public void Retained()
        {
            Require(ReferenceEquals(Runtime.Map,Old) && Old.DisposeCalls==0 && Old.Root.activeSelf);
            Require(ReferenceEquals(Get<CurrentMapDefinition>(Runtime,"_currentMap"),Before) && Runtime.ActiveSceneConfigId==21 && Runtime.Appearance=="normal");
            Require(ReferenceEquals(Get<ActorView>(Runtime,"_localPlayer"),Actor) && Player.enabled &&
                ReferenceEquals(Player.Navigation,Old.Navigation) && Player.Changes==0);
            Require(Actor.Go.transform.position.x==20 && Actor.Go.transform.position.z==30 && Actor.HasTarget && Actor.Velocity.x==4);
            Require(ReferenceEquals(Camera.Target,Actor.Go.transform) && Camera.Bounds.x==7 && Camera.Bounds.y==9 && Camera.Changes==0);
        }
    }
    public static int Main()
    {
        try
        {
            foreach (var missing in new[] { "source","layout","tiles" })
                Check("Refresh failed "+missing+" preserves real runtime map/nav/player/camera bindings",() =>
                { var f=new Fixture();f.Next.BuildFailure=new InvalidOperationException("missing "+missing);f.Runtime.RefreshCurrentDelivery();f.Retained(); });
            Check("Refresh missing catalog entry preserves current scene",() =>
            { var f=new Fixture();CurrentMapCatalog.Reload=() => CurrentMapCatalog.Selected=null;f.Runtime.RefreshCurrentDelivery();f.Retained(); });
            Check("Refresh candidate chunk preparation failure retains old map and disposes candidate",() =>
            { var f=new Fixture();f.Next.Built=new();f.Next.Built.DuringUpdate=() => throw new InvalidOperationException("candidate preparation");
              f.Runtime.RefreshCurrentDelivery();f.Retained();Require(f.Next.Built.DisposeCalls==1 && !f.Next.Built.Root.activeSelf); });
            Check("Refresh recovery spawn preparation failure leaves old player untouched",() =>
            { var f=new Fixture();f.Next.Built=new();f.Next.Built.Navigation.RecoveryValid=false;f.Runtime.RefreshCurrentDelivery();f.Retained();Require(f.Next.Built.DisposeCalls==1); });
            Check("Refresh binding exception rolls back camera and player while old root survives",() =>
            { var f=new Fixture();f.Player.DuringApply=() => throw new InvalidOperationException("actor binding failure");
              f.Runtime.RefreshCurrentDelivery();f.Retained();Require(f.Next.Built.DisposeCalls==1); });
            Check("Refresh success commits selected revision only after runtime preparation",() =>
            {
                var f=new Fixture();f.Next.Built=new();int checks=0;
                f.Next.Built.DuringUpdate=() => { checks++;Require(f.Old.DisposeCalls==0 && f.Old.Root.activeSelf); };
                f.Runtime.RefreshCurrentDelivery();
                Require(checks>=2 && ReferenceEquals(f.Runtime.Map,f.Next.Built) && f.Old.DisposeCalls==1 && !f.Old.Root.activeSelf);
                Require(Get<CurrentMapDefinition>(f.Runtime,"_currentMap").ContentIdentity==f.Next.ContentIdentity &&
                    ReferenceEquals(f.Player.Navigation,f.Next.Built.Navigation) && f.Player.enabled && f.Player.Changes==1);
                Require(ReferenceEquals(f.Camera.Target,f.Actor.Go.transform) && f.Camera.Bounds.width==100);
            });
            Check("Appearance recovery preparation failure retains complete old runtime",() =>
            {
                var f=new Fixture();var spring=new CurrentMapDefinition { Appearance="spring",ContentIdentity="test:v001:spring:cccc",Built=new() };
                spring.Built.Navigation.RecoveryValid=false;f.Before.Appearances["spring"]=spring;
                Require(!f.Runtime.TrySetAppearance("spring"));f.Retained();Require(spring.Built.DisposeCalls==1);
            });
            Check("Appearance late binding failure restores old camera and controller",() =>
            {
                var f=new Fixture();var spring=new CurrentMapDefinition { Appearance="spring",ContentIdentity="test:v001:spring:cccc",Built=new() };
                f.Before.Appearances["spring"]=spring;f.Player.DuringApply=() => throw new InvalidOperationException("appearance apply");
                Require(!f.Runtime.TrySetAppearance("spring"));f.Retained();Require(spring.Built.DisposeCalls==1);
            });
            Check("Appearance success uses independent source and retires old map last",() =>
            {
                var f=new Fixture();var spring=new CurrentMapDefinition { Appearance="spring",ContentIdentity="test:v001:spring:cccc",Built=new() };
                f.Before.Appearances["spring"]=spring;spring.Built.DuringUpdate=() => Require(f.Old.DisposeCalls==0);
                Require(f.Runtime.TrySetAppearance("spring") && f.Runtime.Appearance=="spring" && f.Old.DisposeCalls==1 &&
                    ReferenceEquals(f.Runtime.Map,spring.Built) && ReferenceEquals(f.Player.Navigation,spring.Built.Navigation));
            });
            Check("Different server scene load failure still unloads old navigation",() =>
            {
                var f=new Fixture();f.Next.SceneConfigId=22;f.Next.BuildFailure=new InvalidOperationException("new server scene missing");
                CurrentMapCatalog.Selected=f.Next;
                try { typeof(TianyongMapRuntime).GetMethod("EnterScene",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f.Runtime,new object[] { 22u });
                    throw new Exception("Expected scene failure"); } catch (TargetInvocationException e) when(e.InnerException is InvalidOperationException) { }
                Require(f.Runtime.Map==null && f.Old.DisposeCalls==1 && !f.Player.enabled && f.Camera.Target==null);
            });
            Check("Cold same-ID load failure still clears incomplete player and camera bindings",() =>
            {
                var f=new Fixture();Set(f.Runtime,"_map",null);f.Next.BeforeBuild=null;
                f.Next.BuildFailure=new InvalidOperationException("cold load missing");
                try { f.Runtime.RefreshCurrentDelivery();throw new Exception("Expected cold load failure"); }
                catch(InvalidOperationException) { }
                Require(f.Runtime.Map==null && f.Runtime.ActiveSceneConfigId==0 && !f.Player.enabled &&
                    f.Camera.Target==null && Get<ActorView>(f.Runtime,"_localPlayer")==null);
            });
            Console.WriteLine(JsonSerializer.Serialize(new { passed=Passed.Count,failed=0,
                scope="Actual TianyongMapRuntime source execution with lightweight dependency doubles; no Unity native renderer/physics, device or network acceptance.",tests=Passed }));
            return 0;
        }
        catch(Exception error)
        { Console.WriteLine(JsonSerializer.Serialize(new { passed=Passed.Count,failed=1,error=error.ToString(),tests=Passed }));return 1; }
    }
}
