// Narrow dependencies for executing the real TianyongMapRuntime control flow.
// These are not Unity renderer, physics, JsonUtility, network or device tests.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class Object { public static void Destroy(Object value) { } }
    public sealed class Transform { public Vector3 position; }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool inactive) where T : Component => null;
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component => Array.Empty<T>();
        public static T[] FindObjectsByType<T>() where T : Component => Array.Empty<T>();
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Component> _components = new();
        public readonly Transform transform = new();
        public bool activeSelf = true;
        public void SetActive(bool active) => activeSelf = active;
        public T GetComponent<T>() where T : Component => _components.TryGetValue(typeof(T),out var value) ? (T)value : null;
        public T AddComponent<T>() where T : Component, new()
        { var value = new T { gameObject = this }; _components[typeof(T)] = value; return value; }
    }
    public sealed class Camera : Component { public static Camera main; }
    public enum LightType { Directional }
    public sealed class Light : Component { public LightType type; }
    public struct Rect { public float x,y,width,height; public Rect(float x,float y,float width,float height) { this.x=x;this.y=y;this.width=width;this.height=height; } }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new(0,0,0);
        public override string ToString() => $"({x},{y},{z})";
    }
    public static class Time { public static float deltaTime; }
    public static class Debug
    {
        public static readonly List<string> Errors = new();
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
        public static void LogError(object value) => Errors.Add(value.ToString());
    }
}
namespace MmorpgClient.Game
{
    public sealed class ActorView { public UnityEngine.GameObject Go; public bool HasTarget; public UnityEngine.Vector3 Velocity; }
    public sealed class SceneInfoComp { public uint SceneConfigId; }
    public sealed class ActorWorld
    {
        public event Action<ActorView> OnLocalPlayerChanged;
        public bool HasLocalPlayer;
        public int LocalEntity;
        public bool TryGetActor(int key,out ActorView actor) { actor=null;return false; }
    }
    public sealed class GameClient
    {
        public bool InGame;
        public uint CurrentSceneConfigId;
        public readonly ActorWorld World = new();
        public event Action<SceneInfoComp> OnSceneEntered;
        public event Action OnDisconnected;
    }
    public static class GameplayInputGate { public static bool IsPointerBlocked; }
}
namespace MmorpgClient.World.CurrentMaps
{
    public static class MapDeliveryContract
    { public static bool IsAppearance(string value) => value is "normal" or "spring" or "lantern" or "mid-autumn"; }
    public sealed class CurrentMapDefinition
    {
        public string Key="test",ContentIdentity="test:v001:normal:aaaa",Appearance="normal";
        public uint SceneConfigId=21;
        public UnityEngine.Vector3 Spawn = new(10,0,10);
        public UnityEngine.Rect EffectiveWorldRect = new(0,0,100,100);
        public bool SameNavigation=true;
        public Exception BuildFailure;
        public Action BeforeBuild;
        public Tianyong.TianyongMapInstance Built;
        public readonly Dictionary<string,CurrentMapDefinition> Appearances = new();
        public CurrentMapDefinition ForAppearance(string appearance) => appearance == Appearance ? this : Appearances.GetValueOrDefault(appearance);
        public bool SharesNavigationWith(CurrentMapDefinition other) => other.SameNavigation;
    }
    public static class CurrentMapCatalog
    {
        public static CurrentMapDefinition Selected;
        public static Action Reload;
        public static CurrentMapDefinition Find(uint sceneId) => Selected?.SceneConfigId == sceneId ? Selected : null;
        public static void ReloadDeliveries() => Reload?.Invoke();
    }
    public static class CurrentMapBuilder
    {
        public static Tianyong.TianyongMapInstance Build(UnityEngine.Transform parent,CurrentMapDefinition definition)
        {
            definition.BeforeBuild?.Invoke();
            if (definition.BuildFailure != null) throw definition.BuildFailure;
            return definition.Built ??= new Tianyong.TianyongMapInstance();
        }
    }
}
namespace MmorpgClient.World.Tianyong
{
    using UnityEngine;
    public enum TianyongTheme { City }
    public sealed class TianyongMapConfig
    {
        public TianyongTheme InitialTheme;
        public float CameraZoomMin=24,CameraZoomMax=55,CameraZoomDefault=35;
        public int VisibleChunkRadius=3;
        public uint SceneConfigId=1;
        public static TianyongMapConfig LoadDefault() => new();
    }
    public static class TianyongMapDefinition { public const uint DefaultSceneConfigId=1; }
    public sealed class TianyongNavigationGrid
    {
        public bool Walkable=true,RecoveryValid=true;
        public bool IsWalkable(Vector3 point) => Walkable;
        public bool TryFindNearestWalkable(Vector3 point,out Vector3 result) { result=point;return Walkable; }
    }
    public sealed class TianyongMapInstance : IDisposable
    {
        public readonly GameObject Root=new();
        public readonly TianyongNavigationGrid Navigation=new();
        public int DisposeCalls,UpdateCalls;
        public Action DuringUpdate;
        public void Dispose() => DisposeCalls++;
        public void UpdateVisibleChunks(Vector3 focus,int radius) { UpdateCalls++;DuringUpdate?.Invoke(); }
        public void UpdateCityTiles(Camera camera,Vector3 focus) { }
    }
    public sealed class TianyongCameraController
    {
        public Transform Target;
        public Rect Bounds;
        public int Changes;
        public Action DuringSetBounds;
        public TianyongCameraController(Camera camera,float min,float max,float zoom) { }
        public static void ValidateWorldBounds(Rect bounds)
        { if (bounds.width<=0 || bounds.height<=0) throw new InvalidOperationException("invalid candidate camera bounds"); }
        public Action CaptureMapBinding()
        { var target=Target;var bounds=Bounds;int changes=Changes;return () => { Target=target;Bounds=bounds;Changes=changes; }; }
        public void SetWorldBounds(Rect bounds) { Bounds=bounds;Changes++;DuringSetBounds?.Invoke(); }
        public void SetTarget(Transform target) { Target=target;Changes++; }
        public void SetTheme(TianyongTheme theme,bool painted) => Changes++;
        public void Tick(float delta,bool input) { }
    }
    public sealed class TianyongPlayerController : MonoBehaviour
    {
        public object Motor=new();
        public TianyongNavigationGrid Navigation;
        public Vector3 FeetPosition => transform.position;
        public int Changes,Reports;
        public Action DuringApply;
        public static Vector3 ResolveFallbackSpawn(TianyongNavigationGrid nav,Vector3 feet,Vector3? requested)
        { if (!nav.RecoveryValid) throw new InvalidOperationException("No walkable recovery spawn");return requested??feet; }
        public Action CaptureMapBinding()
        { var nav=Navigation;int changes=Changes;var position=transform.position;bool active=enabled;
          return () => { Navigation=nav;Changes=changes;transform.position=position;enabled=active; }; }
        public void ApplyPreparedMapNavigation(TianyongNavigationGrid nav,Vector3 fallback,Vector3 feet)
        { Navigation=nav;Changes++;WarpTo(feet);DuringApply?.Invoke(); }
        public void Initialize(MmorpgClient.Game.GameClient client,TianyongNavigationGrid nav,Camera camera,TianyongMapConfig config,Vector3 fallback)
        { Navigation=nav;Changes++; }
        public void WarpTo(Vector3 feet) => transform.position=feet;
        public void ReportPositionToServer() => Reports++;
    }
    public static class TianyongLighting { public static void Apply(TianyongTheme theme,Light sun) { } }
}
