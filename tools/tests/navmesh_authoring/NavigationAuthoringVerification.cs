using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MmorpgClient.World.NavigationAuthoring;
using MmorpgClient.Editor.NavigationAuthoring;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class NavigationAuthoringVerification
{
    [Serializable] public sealed class Check { public string name, result, detail; }
    [Serializable] public sealed class Report
    {
        public string result = "RUNNING", unityVersion, scope = "Isolated actual-source NavMesh authoring checks; synthetic test art only; not production-map acceptance.";
        public List<Check> checks = new List<Check>();
        public string playScreenshot, routeScreenshot, error;
    }

    private const string StateKey = "NavigationAuthoringVerification.State";
    private const string ReportKey = "NavigationAuthoringVerification.Report";
    private static Report _report;
    private static double _playStart;
    private static int _frames;
    private static bool _destinationSet;
    private static bool _routeCaptured;
    private static Vector2 _start;
    private static Vector2 _target = new Vector2(17, 6);
    private static string _caseRoot;
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    private static string ImagePath => "Assets/TestArt/background.png";
    private static string SourceProject
    {
        get
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-navmeshSourceProject")return Path.GetFullPath(args[i+1]);
            return Path.GetFullPath(Path.Combine(Application.dataPath,"../../../.."));
        }
    }

    static NavigationAuthoringVerification()
    {
        if (SessionState.GetString(StateKey, "") == "PLAY")
        {
            _report = JsonUtility.FromJson<Report>(SessionState.GetString(ReportKey, "{}"));
            _playStart = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdatePlay;
        }
    }

    public static void Run()
    {
        _report = new Report { unityVersion = Application.unityVersion };
        _caseRoot = "Assets/TestCases/Run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        try
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "TestArt"));
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "TestCases"));
            Directory.CreateDirectory(_caseRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            WriteSyntheticImage(ImagePath, 200, 120);
            RunCheck("pixel/world north-west direction roundtrip", TestCoordinates);
            RunCheck("geometry concave floor and positive Y winding", TestConcave);
            RunCheck("geometry union overlap avoids duplicate floor", TestUnion);
            RunCheck("geometry obstacle holes subtract correct area", TestHole);
            RunCheck("overlapping independent obstacles form one blocked union", TestOverlappingObstacles);
            RunCheck("invalid/self-intersecting polygons rejected", TestInvalidGeometry);
            RunCheck("flat ground queries preserve original foot height", TestGroundHeight);
            RunCheck("shared bake settings do not depend on global agent defaults", TestSharedSettings);
            RunCheck("production spawn and editor route reject 3cm edge snapping", TestEdgeTolerance);
            RunCheck("official NavMesh bake routes around obstacle hole", TestNavMeshRoute);
            RunCheck("official NavMesh keeps disconnected islands disconnected", TestIslands);
            RunCheck("agent clearance closes narrow connection", TestClearance);
            RunCheck("invalid draft geometry and parameters rejected", TestInvalidDraft);
            RunCheck("editor imports whole image with source SHA", TestImageImport);
            RunCheck("editor imports actual legacy island polygon source", TestExistingIslandImport);
            RunCheck("editor imports complete row/column tile set", TestTileImport);
            RunCheck("editor rejects missing tile coordinate", TestMissingTile);
            RunCheck("editor polygon edit undo/redo and canvas coordinate mapping", TestWindowEditing);
            RunCheck("editor native bake and export preserve same geometry", TestBakeAndExport);
            RunCheck("Play entry rejects changed source PNG and accepts exact restoration", TestChangedImageAtPlayEntry);
            RunCheck("native bake identity stays safe after restoring older authoring state", TestNativeAssetIdentity);
            RunCheck("paired fixtures export actual Unity queries and shared OBJ inputs", TestPairedFixtures);
            if (_report.checks.Any(c => c.result == "FAIL")) { Finish(false); return; }

            var draft = MakeDraft();
            draft.polygons.Add(Polygon(false, 10, 10, 190, 10, 190, 110, 10, 110));
            draft.polygons.Add(Polygon(true, 80, 30, 120, 30, 120, 90, 80, 90));
            draft.spawnSet = true;
            draft.spawn = new Vector2(3, 6);
            AssetDatabase.CreateAsset(draft, _caseRoot + "/PlayDraft.asset");
            InvokeIO("Bake", draft);
            Assert(draft.bakedData != null, "Play draft has no actual baked NavMeshData");
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Synthetic NavMesh Play Verification");
            var preview = go.AddComponent<NavMeshDraftPreview>();
            preview.draft = draft;
            EditorSceneManager.SaveScene(go.scene, _caseRoot + "/Play.unity");
            SessionState.SetString(ReportKey, JsonUtility.ToJson(_report));
            SessionState.SetString(StateKey, "PLAY");
            _playStart = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdatePlay;
            EditorApplication.isPlaying = true;
        }
        catch (Exception error) { _report.error = error.ToString(); Finish(false); }
    }

    private static void TestCoordinates()
    {
        var d = MakeDraft();
        AssertNear(d.PixelToWorld(new Vector2(0, 0)), new Vector2(0, 12), "north west");
        AssertNear(d.PixelToWorld(new Vector2(200, 120)), new Vector2(20, 0), "south east");
        var p = new Vector2(123.5f, 77.25f);
        AssertNear(d.WorldToPixel(d.PixelToWorld(p)), p, "roundtrip");
        UnityEngine.Object.DestroyImmediate(d);
    }

    private static void TestConcave()
    {
        var d = MakeDraft();
        d.polygons.Add(Polygon(false, 10,10, 190,10, 190,40, 60,40, 60,110, 10,110));
        var mesh = NavigationPolygonGeometry.Build(d);
        AssertArea(mesh, 89);
        Assert(mesh.triangles.Length >= 12, "Concave floor missing triangles");
        Destroy(mesh, d);
    }

    private static void TestUnion()
    {
        var d = MakeDraft();
        d.polygons.Add(Polygon(false, 10,10, 120,10, 120,110, 10,110));
        d.polygons.Add(Polygon(false, 80,10, 190,10, 190,110, 80,110));
        var mesh = NavigationPolygonGeometry.Build(d);
        AssertArea(mesh, 180);
        Destroy(mesh, d);
    }

    private static void TestHole()
    {
        var d = MakeDraft();
        d.polygons.Add(Polygon(false, 10,10, 190,10, 190,110, 10,110));
        d.polygons.Add(Polygon(true, 80,30, 120,30, 120,90, 80,90));
        var mesh = NavigationPolygonGeometry.Build(d);
        AssertArea(mesh, 156);
        var vs = mesh.vertices;
        for (int i=0; i<mesh.triangles.Length; i+=3)
        {
            var c = (vs[mesh.triangles[i]] + vs[mesh.triangles[i+1]] + vs[mesh.triangles[i+2]]) / 3;
            Assert(!(c.x>8.001f && c.x<11.999f && c.z>3.001f && c.z<8.999f), "Triangle centroid inside blocked hole");
        }
        Destroy(mesh, d);
    }

    private static void TestInvalidGeometry()
    {
        Throws(() => NavigationPolygonGeometry.ValidatePolygon(new [] { new Vector2(0,0),new Vector2(10,10),new Vector2(0,10),new Vector2(10,0) }), "bow tie accepted");
        Throws(() => NavigationPolygonGeometry.ValidatePolygon(new [] { new Vector2(0,0),new Vector2(1,1),new Vector2(2,2) }), "zero area accepted");
    }

    private static void TestOverlappingObstacles()
    {
        var draft=MakeDraft();
        draft.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
        draft.polygons.Add(Polygon(true,60,30,100,30,100,90,60,90));
        draft.polygons.Add(Polygon(true,80,30,120,30,120,90,80,90));
        var mesh=NavigationPolygonGeometry.Build(draft);
        AssertArea(mesh,144);
        Destroy(mesh);
        WithNative(draft,()=>{
            foreach(float x in new[]{7f,9f,11f})Assert(!NavMesh.SamplePosition(new Vector3(x,0,6),out _,.25f,NavMesh.AllAreas),"Overlapping obstacle union has a walkable gap");
            Assert(NavMesh.SamplePosition(new Vector3(3,0,6),out _,.25f,NavMesh.AllAreas),"Obstacle union removed unrelated floor");
        });
        Destroy(draft);
    }

    private static void TestNavMeshRoute()
    {
        var d = MakeDraft();
        d.polygons.Add(Polygon(false, 10,10, 190,10, 190,110, 10,110));
        d.polygons.Add(Polygon(true, 80,30, 120,30, 120,90, 80,90));
        WithNative(d, () => {
            var path = new NavMeshPath();
            Assert(NavMesh.CalculatePath(new Vector3(3,0,6), new Vector3(17,0,6), NavMesh.AllAreas, path), "Native CalculatePath returned false");
            Assert(path.status == NavMeshPathStatus.PathComplete, "Route did not complete around hole");
            Assert(path.corners.Length >= 4, "Route appears to cross blocked hole directly");
            Assert(!NavMesh.SamplePosition(new Vector3(10,0,6), out _, .25f, NavMesh.AllAreas), "Blocked hole has navigable surface");
            Assert(NavMesh.Raycast(new Vector3(3,0,6), new Vector3(17,0,6), out _, NavMesh.AllAreas), "Direct ray should hit hole boundary");
            foreach(var p in path.corners) Assert(!(p.x>8 && p.x<12 && p.z>3 && p.z<9), "Route enters hole");
        });
        UnityEngine.Object.DestroyImmediate(d);
    }

    private static void TestGroundHeight()
    {
        foreach(float voxel in new[]{.05f,.1f,.2f})
        {
            var d=MakeDraft(); d.voxelSize=voxel;
            d.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
            WithNative(d,()=>{
                Assert(NavMesh.SamplePosition(new Vector3(3,0,6),out var hit,1,NavMesh.AllAreas),"Original ground point missing");
                Assert(Mathf.Abs(hit.position.y)<=.02f,"Ground foot height changed: voxel="+voxel+" y="+hit.position.y);
            });
            UnityEngine.Object.DestroyImmediate(d);
        }
    }

    private static void TestSharedSettings()
    {
        var d=MakeDraft(); d.agentRadius=.6f; d.agentHeight=2.1f;d.agentClimb=.4f;d.agentSlope=37;
        var globalBefore=NavMesh.GetSettingsByID(d.agentTypeId);
        var settings=d.BuildSettings();
        Assert(settings.agentRadius==d.agentRadius && settings.agentHeight==d.agentHeight &&
            settings.agentClimb==d.agentClimb && settings.agentSlope==d.agentSlope,"Bake ignores author agent settings");
        Assert(settings.overrideVoxelSize && settings.voxelSize==d.voxelSize,"Bake ignores author voxel size");
        Assert(settings.overrideTileSize && settings.tileSize==256 && settings.minRegionArea==0,"Tile/region contract inherited from unrelated global settings");
        Assert(settings.buildHeightMesh,"Flat source height is not retained for native queries");
        Assert(settings.ledgeDropHeight==0 && settings.maxJumpAcrossDistance==0,"Unrequested links inherited from global agent settings");
        var globalAfter=NavMesh.GetSettingsByID(d.agentTypeId);
        Assert(globalBefore.agentRadius==globalAfter.agentRadius && globalBefore.agentHeight==globalAfter.agentHeight &&
            globalBefore.agentClimb==globalAfter.agentClimb && globalBefore.agentSlope==globalAfter.agentSlope,"Local bake changed project-wide agent settings");
        Destroy(d);
    }

    private static void TestEdgeTolerance()
    {
        var d=MakeDraft(); d.spawnSet=true;d.spawn=new Vector2(3,6);
        d.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
        AssetDatabase.CreateAsset(d,_caseRoot+"/EdgeTolerance.asset");InvokeIO("Bake",d);
        NavMesh.RemoveAllNavMeshData();var instance=NavMesh.AddNavMeshData(d.bakedData);
        Vector2 outside;
        try
        {
            Assert(NavMesh.SamplePosition(new Vector3(0,0,6),out var edge,3,NavMesh.AllAreas),"Cannot locate native edge");
            outside=new Vector2(edge.position.x-.03f,edge.position.z);
        }
        finally{instance.Remove();}
        d.spawn=outside;
        Throws(()=>InvokeIO("CheckSpawn",d),"Production spawn accepts 3cm horizontal snap rejected by server");
        Throws(()=>InvokeIO("Export",d,Path.Combine(Output,"edge-snap-must-reject")),"Export accepts a server-rejected original spawn");
        var window=EditorWindow.GetWindow<NavMeshAuthoringWindow>();
        try
        {
            typeof(NavMeshAuthoringWindow).GetMethod("SelectDraft",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{d});
            Throws(()=>typeof(NavMeshAuthoringWindow).GetMethod("RouteClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{outside}),"Editor route accepts 3cm horizontal snap rejected by server");
        }
        finally{window.Close();}
    }

    private static void TestIslands()
    {
        var d = MakeDraft();
        d.polygons.Add(Polygon(false, 10,10, 70,10, 70,110, 10,110));
        d.polygons.Add(Polygon(false, 130,10, 190,10, 190,110, 130,110));
        WithNative(d, () => {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(new Vector3(3,0,6), new Vector3(17,0,6), NavMesh.AllAreas, path);
            Assert(path.status != NavMeshPathStatus.PathComplete, "Disconnected islands gained invisible link");
            Assert(NavMesh.SamplePosition(new Vector3(17,0,6), out _, .25f, NavMesh.AllAreas), "Target island itself was lost");
        });
        UnityEngine.Object.DestroyImmediate(d);
    }

    private static void TestClearance()
    {
        var d = MakeDraft();
        d.agentRadius = .6f;
        d.polygons.Add(Polygon(false, 10,10, 80,10, 80,110, 10,110));
        d.polygons.Add(Polygon(false, 120,10, 190,10, 190,110, 120,110));
        d.polygons.Add(Polygon(false, 70,56, 130,56, 130,64, 70,64));
        WithNative(d, () => {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(new Vector3(3,0,6),new Vector3(17,0,6),NavMesh.AllAreas,path);
            Assert(path.status != NavMeshPathStatus.PathComplete, "0.8-unit corridor admits diameter1.2 agent");
        });
        UnityEngine.Object.DestroyImmediate(d);
    }

    private static void TestInvalidDraft()
    {
        var d = MakeDraft();
        d.worldRect = new Rect(0,0,20,20); Throws(() => d.Validate(), "Wrong aspect accepted");
        d.worldRect = new Rect(0,0,20,12); d.agentRadius = 0; Throws(() => d.Validate(), "Zero radius accepted");
        d.agentRadius = .4f; d.spawnSet = true; d.spawn = new Vector2(21,6); Throws(() => d.Validate(true), "Outside spawn accepted");
        d.spawnSet = false; Throws(() => d.Validate(true), "Unset spawn accepted");
        UnityEngine.Object.DestroyImmediate(d);
    }

    private static void TestImageImport()
    {
        var d = (NavMeshAuthoringDraft)InvokeIO("CreateFromImage", Path.GetFullPath(ImagePath), _caseRoot + "/ImportedImage.asset");
        Assert(d != null && d.imageWidth == 200 && d.imageHeight == 120 && d.backgroundTiles.Length == 1, "Whole-image import dimensions differ");
        Assert(d.backgroundTiles[0].sha256 == Sha(File.ReadAllBytes(ImagePath)), "Whole-image source SHA differs");
    }

    private static void TestTileImport()
    {
        Directory.CreateDirectory("Assets/TestArt/Tiles");
        for(int row=1;row<=2;row++) for(int col=1;col<=2;col++) WriteSyntheticImage($"Assets/TestArt/Tiles/r{row:00}_c{col:00}.png", 64, 32);
        var d = (NavMeshAuthoringDraft)InvokeIO("CreateFromTileFolder", Path.GetFullPath("Assets/TestArt/Tiles"), _caseRoot + "/ImportedTiles.asset");
        Assert(d.backgroundTiles.Length == 4 && d.imageWidth == 128 && d.imageHeight == 64, "2x2 tiles wrong geometry");
        Assert(d.backgroundTiles.Select(t=>t.row+":"+t.column).Distinct().Count()==4, "Tile coordinate duplicated");
    }

    private static void TestExistingIslandImport()
    {
        var folder = Path.Combine(SourceProject,"Assets/Resources/World/CurrentMaps20261009/island");
        var d = (NavMeshAuthoringDraft)InvokeIO("ImportExistingMap", folder, _caseRoot + "/ImportedIsland.asset");
        Assert(d.mapKey == "island" && d.imageWidth == 1254 && d.imageHeight == 1254, "Legacy island identity/dimensions differ");
        Assert(d.polygons.Any(p=>!p.blocked) && d.spawnSet, "Legacy island lost polygon authoring/spawn");
        Assert(d.backgroundTiles[0].sha256 == Sha(File.ReadAllBytes(Path.Combine(folder,"layout.png"))), "Legacy import changed source art identity");
    }

    private static void TestMissingTile()
    {
        Directory.CreateDirectory("Assets/TestArt/MissingTiles");
        WriteSyntheticImage("Assets/TestArt/MissingTiles/r01_c01.png",64,32);
        WriteSyntheticImage("Assets/TestArt/MissingTiles/r01_c03.png",64,32);
        Throws(()=>InvokeIO("CreateFromTileFolder",Path.GetFullPath("Assets/TestArt/MissingTiles"),_caseRoot + "/ShouldReject.asset"), "Missing c02 accepted");
    }

    private static void TestBakeAndExport()
    {
        var d = MakeDraft();
        d.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
        d.polygons.Add(Polygon(true,80,30,120,30,120,90,80,90));
        d.spawnSet=true; d.spawn=new Vector2(3,6);
        AssetDatabase.CreateAsset(d,_caseRoot + "/ExportDraft.asset");
        InvokeIO("Bake", d);
        Assert(d.bakedData != null && !string.IsNullOrEmpty(d.bakedGeometrySha256),"Bake did not persist data and geometry identity");
        d.displayName = "renamed synthetic source";
        d.spawn = new Vector2(3,5);
        Assert((bool)InvokeIO("IsBakeCurrent",d), "Renaming/spawn placement incorrectly invalidates geometry bake");
        d.spawn = new Vector2(10,6);
        Throws(()=>InvokeIO("Export",d,Path.Combine(Output,"blocked-spawn-export")), "Blocked-hole spawn exported");
        d.spawn = new Vector2(3,6);
        var prior = d.polygons[0].points[0];
        d.polygons[0].points[0] = new Vector2(11,10);
        Assert(!(bool)InvokeIO("IsBakeCurrent",d), "Changed floor polygon still treated as current bake");
        Throws(()=>InvokeIO("Export",d,Path.Combine(Output,"stale-bake-export")), "Stale baked source exported");
        d.polygons[0].points[0] = prior;
        Assert((bool)InvokeIO("IsBakeCurrent",d), "Restored exact source fails geometry identity");
        var folder=Path.Combine(Output,"test-export");
        InvokeIO("Export",d,folder);
        foreach(var file in new[]{"navigation.json","world-y-up.obj","source.json","server-bake-command.txt","server-bake-args.json"}) Assert(File.Exists(Path.Combine(folder,file)),"Missing "+file);
        var obj=File.ReadAllText(Path.Combine(folder,"world-y-up.obj"));
        Assert(obj.Contains("\nv ") && obj.Contains("\nf "), "OBJ lacks geometry");
        var source=File.ReadAllText(Path.Combine(folder,"source.json"));
        Assert(source.Contains(Sha(File.ReadAllBytes(Path.Combine(folder,"world-y-up.obj")))), "Export source does not bind OBJ SHA");
        var nav = File.ReadAllText(Path.Combine(folder,"navigation.json"));
        Assert(nav.Contains(Sha(File.ReadAllBytes(ImagePath))), "Export authoring document lost source art SHA");
        Assert(source.Contains(Sha(File.ReadAllBytes(Path.Combine(folder,"navigation.json")))), "Export source does not bind actual authoring file bytes");
        Assert(source.Contains(Sha(File.ReadAllBytes(Path.Combine(folder,"server-bake-args.json")))),"Export source does not bind exact baker arguments");
        var arguments=JArray.Parse(File.ReadAllText(Path.Combine(folder,"server-bake-args.json"))).Values<string>().ToArray();
        string Argument(string key){int i=Array.IndexOf(arguments,key);Assert(i>=0 && i+1<arguments.Length,"Missing shared baker argument "+key);return arguments[i+1];}
        Assert(Argument("--profile")=="authored-flat" && Argument("--agent-slope")=="45" && Argument("--tile-size")=="256" && Argument("--min-region-area")=="0","Server command does not use shared contract");
        Assert(Argument("--probe")=="3,0,6","Server command changed the original ground foot point");
        string currentHash=d.bakedGeometrySha256;
        foreach(Action mutate in new Action[]{()=>d.agentRadius+=.1f,()=>d.agentHeight+=.1f,()=>d.agentClimb+=.01f,()=>d.agentSlope+=1,()=>d.voxelSize+=.01f})
        {
            string before=JsonUtility.ToJson(d);mutate();
            Assert(!(bool)InvokeIO("IsBakeCurrent",d),"Changed shared agent setting did not invalidate bake");
            JsonUtility.FromJsonOverwrite(before,d);
            Assert(d.bakedGeometrySha256==currentHash && (bool)InvokeIO("IsBakeCurrent",d),"Restored settings lost valid bake identity");
        }
    }

    private static void TestPairedFixtures()
    {
        var cases=new JArray();
        foreach(string name in new[]{"hole","islands","narrow-closed","wide-open","small-island","multi-tile"})
        {
            var d=MakeDraft();
            if(name=="hole" || name=="multi-tile")
            {
                d.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
                d.polygons.Add(Polygon(true,80,30,120,30,120,90,80,90));
            }
            else
            {
                d.polygons.Add(Polygon(false,10,10,70,10,70,110,10,110));
                d.polygons.Add(name=="small-island"?Polygon(false,150,54,156,54,156,60,150,60):Polygon(false,130,10,190,10,190,110,130,110));
                if(name=="narrow-closed") {d.agentRadius=.6f;d.polygons.Add(Polygon(false,60,56,140,56,140,64,60,64));}
                if(name=="wide-open") d.polygons.Add(Polygon(false,60,45,140,45,140,75,60,75));
                if(name=="small-island"){d.agentRadius=.1f;d.voxelSize=.05f;}
            }
            float scale=name=="multi-tile"?4:1;
            if(scale>1)d.worldRect=new Rect(0,0,20*scale,12*scale);
            d.agentSlope=37;d.agentHeight=1.9f;d.agentClimb=.4f;
            d.spawnSet=true;d.spawn=new Vector2(3*scale,6*scale);
            AssetDatabase.CreateAsset(d,_caseRoot+"/Paired-"+name+".asset");
            InvokeIO("Bake",d);
            string directory=Path.Combine(Output,"paired-exports",name);
            InvokeIO("Export",d,directory);
            NavMesh.RemoveAllNavMeshData();var instance=NavMesh.AddNavMeshData(d.bakedData);
            try
            {
                var points=new JArray();var routes=new JArray();
                var target=name=="small-island"?new Vector3(15.3f,0,6.3f):new Vector3(17*scale,0,6*scale);
                var start=new Vector3(d.spawn.x,0,d.spawn.y);
                var inputs=new List<Vector3>{start,target,new Vector3(10*scale,0,6*scale),new Vector3(0,0,6*scale),start+Vector3.up*.2f};
                if(name=="hole")inputs.AddRange(new[]{new Vector3(7.4f,0,6),new Vector3(7.8f,0,6),new Vector3(12.6f,0,6),new Vector3(12.2f,0,6),new Vector3(1.3f,0,6),new Vector3(1.7f,0,6)});
                foreach(var input in inputs)
                {
                    bool found=NavMesh.SamplePosition(input,out var hit,2,NavMesh.AllAreas);
                    bool onMesh=found && Mathf.Abs(hit.position.x-input.x)<=.02f && Mathf.Abs(hit.position.y-input.y)<=.02f && Mathf.Abs(hit.position.z-input.z)<=.02f;
                    points.Add(new JObject{["input"]=Position(input),["onMesh"]=onMesh,["nearest"]=found?Position(hit.position):null});
                }
                Assert((bool)points[0]["onMesh"] && (bool)points[1]["onMesh"],"Fixture lost a walkable endpoint: "+name);
                Assert(!(bool)points[3]["onMesh"],"Fixture includes outside point: "+name);
                Assert(!(bool)points[4]["onMesh"],"Raised diagnostic point replaced original foot point: "+name);
                if(name=="hole")for(int i=5;i<points.Count;i++)Assert((bool)points[i]["onMesh"]==(i==5 || i==7 || i==10),"Obstacle/outer clearance fixture differs at "+points[i]["input"]);
                var path=new NavMeshPath();NavMesh.CalculatePath(start,target,NavMesh.AllAreas,path);
                bool complete=path.status==NavMeshPathStatus.PathComplete;
                bool expectedComplete=name=="hole" || name=="multi-tile" || name=="wide-open";
                Assert(complete==expectedComplete,"Fixture Unity connectivity differs: "+name);
                bool rayBlocked=NavMesh.Raycast(start,target,out _,NavMesh.AllAreas);
                routes.Add(new JObject{["from"]=Position(start),["to"]=Position(target),["complete"]=complete,["rayBlocked"]=rayBlocked,
                    ["corners"]=new JArray(path.corners.Select(p=>Position(p)))});
                cases.Add(new JObject{["name"]=name,["exportDirectory"]=directory,["points"]=points,["routes"]=routes});
            }
            finally {instance.Remove();}
        }
        File.WriteAllText(Path.Combine(Output,"paired-unity-queries.json"),new JObject{["cases"]=cases,["scope"]="Synthetic same-source planar query comparison; not production-map acceptance"}.ToString());
    }

    private static JArray Position(Vector3 p)=>new JArray(p.x,p.y,p.z);

    private static void TestWindowEditing()
    {
        var type = typeof(NavMeshAuthoringWindow);
        var window = EditorWindow.GetWindow(type,false,"Synthetic NavMesh Tool Verification",true);
        window.position=new Rect(10,10,1100,760);
        var draft=MakeDraft();
        try
        {
            type.GetMethod("SelectDraft",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{draft});
            var pending=(List<Vector2>)type.GetField("_pending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            pending.AddRange(new[]{new Vector2(10,10),new Vector2(190,10),new Vector2(190,110),new Vector2(10,110)});
            Undo.IncrementCurrentGroup();
            type.GetMethod("CompletePolygon",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
            Undo.FlushUndoRecordObjects();
            Assert(draft.polygons.Count==1,"Editor handler did not add polygon");
            Undo.PerformUndo();
            Assert(draft.polygons.Count==0,"Editor polygon undo failed");
            Undo.PerformRedo();
            Assert(draft.polygons.Count==1,"Editor polygon redo failed");
            type.GetField("_imageRect",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,new Rect(100,50,400,240));
            type.GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,2f);
            var screen=(Vector2)type.GetMethod("PixelScreen",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{new Vector2(123,77)});
            var pixel=(Vector2)type.GetMethod("ScreenPixel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{screen});
            AssertNear(pixel,new Vector2(123,77),"Editor canvas roundtrip");
            Undo.IncrementCurrentGroup();
            var original=draft.polygons[0].points[0];
            var vertexScreen=(Vector2)type.GetMethod("PixelScreen",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{original});
            type.GetMethod("BeginVertex",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{vertexScreen,original,false});
            // The MouseDown frame ends before a later MouseDrag mutates the point.
            // Explicit flush reproduces that boundary without desktop input.
            Undo.FlushUndoRecordObjects();
            draft.polygons[0].points[0]=new Vector2(20,20);
            EditorUtility.SetDirty(draft);
            Undo.PerformUndo();
            Assert(draft.polygons.Count==1 && draft.polygons[0].points[0]==original,"Vertex drag snapshot was lost between mouse frames");
            window.Show();window.Repaint();
            window.SendEvent(new Event{type=EventType.Layout});
            window.SendEvent(new Event{type=EventType.Repaint});
        }
        finally{window.Close();UnityEngine.Object.DestroyImmediate(draft);}
    }

    private static void TestNativeAssetIdentity()
    {
        var d=MakeDraft();
        d.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
        d.spawnSet=true;d.spawn=new Vector2(3,6);
        AssetDatabase.CreateAsset(d,_caseRoot+"/BakeIdentity.asset");
        InvokeIO("Bake",d);
        string olderPath=AssetDatabase.GetAssetPath(d.bakedData),olderHash=d.bakedGeometrySha256;
        d.polygons.Add(Polygon(true,80,30,120,30,120,90,80,90));
        InvokeIO("Bake",d);
        // Unity undo restores an asset reference by GUID, so emulate restoration
        // after a second bake replaced or versioned that original native asset.
        d.polygons.RemoveAt(1);d.bakedGeometrySha256=olderHash;
        d.bakedData=AssetDatabase.LoadAssetAtPath<NavMeshData>(olderPath);
        if((bool)InvokeIO("IsBakeCurrent",d))
        {
            NavMesh.RemoveAllNavMeshData();
            var instance=NavMesh.AddNavMeshData(d.bakedData);
            try{Assert(NavMesh.SamplePosition(new Vector3(10,0,6),out _, .25f,NavMesh.AllAreas),"Older state appears current but native asset contains newer blocked hole");}
            finally{instance.Remove();}
        }
    }

    private static void TestChangedImageAtPlayEntry()
    {
        var d=MakeDraft();
        d.polygons.Add(Polygon(false,10,10,190,10,190,110,10,110));
        d.spawnSet=true;d.spawn=new Vector2(3,6);
        AssetDatabase.CreateAsset(d,_caseRoot+"/ChangedImage.asset");
        InvokeIO("Bake",d);
        var original=File.ReadAllBytes(ImagePath);
        string recordedSha=d.backgroundTiles[0].sha256;
        try
        {
            var changed=new Texture2D(200,120,TextureFormat.RGB24,false);
            changed.SetPixel(0,0,Color.red);changed.Apply();
            File.WriteAllBytes(ImagePath,changed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(changed);
            Assert(Sha(File.ReadAllBytes(ImagePath))!=recordedSha,"Test did not actually change the source PNG");
            Assert((bool)InvokeIO("IsBakeCurrent",d),"Recorded authoring identity unexpectedly changed in this test");
            Throws(()=>InvokeIO("CheckSpawn",d),"Play entry accepted a PNG whose actual bytes differ from recorded SHA");
        }
        finally
        {
            File.WriteAllBytes(ImagePath,original);
            AssetDatabase.ImportAsset(ImagePath,ImportAssetOptions.ForceSynchronousImport);
        }
        Assert(d.backgroundTiles[0].sha256==recordedSha,"Test rewrote the recorded source SHA");
        InvokeIO("CheckSpawn",d);
    }

    private static void WithNative(NavMeshAuthoringDraft d, Action check)
    {
        NavMesh.RemoveAllNavMeshData();
        var mesh=NavigationPolygonGeometry.Build(d);
        var sources=new List<NavMeshBuildSource>{new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Mesh,sourceObject=mesh,transform=Matrix4x4.identity,area=0}};
        var bounds=new Bounds(new Vector3(d.worldRect.center.x,0,d.worldRect.center.y),new Vector3(d.worldRect.width,10,d.worldRect.height));
        var data=NavMeshBuilder.BuildNavMeshData(d.BuildSettings(),sources,bounds,Vector3.zero,Quaternion.identity);
        Assert(data!=null,"Native bake returned null");
        var instance=NavMesh.AddNavMeshData(data);
        try{Assert(NavMesh.CalculateTriangulation().indices.Length>0,"Native bake empty");check();}
        finally{instance.Remove();Destroy(mesh,data);}
    }

    private static NavMeshAuthoringDraft MakeDraft()
    {
        var draft=ScriptableObject.CreateInstance<NavMeshAuthoringDraft>();
        draft.mapKey="synthetic-verification"; draft.displayName="合成色块验证";
        draft.imageWidth=200; draft.imageHeight=120; draft.worldRect=new Rect(0,0,20,12); draft.boundsConfirmed=true;
        draft.agentTypeId=NavMesh.GetSettingsByIndex(0).agentTypeID;
        draft.agentRadius=.4f;draft.agentHeight=1.8f;draft.agentClimb=.3f;draft.voxelSize=.1f;
        draft.backgroundTiles=new[]{new NavMeshBackgroundTile{texture=AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath),row=1,column=1,pixelWidth=200,pixelHeight=120,assetPath=ImagePath,sha256=Sha(File.ReadAllBytes(ImagePath))}};
        return draft;
    }

    private static NavMeshAreaPolygon Polygon(bool blocked, params float[] xy)
    {
        var p=new NavMeshAreaPolygon{blocked=blocked,name=blocked?"synthetic obstacle":"synthetic floor"};
        for(int i=0;i<xy.Length;i+=2)p.points.Add(new Vector2(xy[i],xy[i+1]));
        return p;
    }

    private static object InvokeIO(string method, params object[] args)
    {
        var type=typeof(NavMeshAuthoringAssetIO);
        Assert(type!=null,"Actual editor IO class not compiled");
        var info=type.GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name==method && m.GetParameters().Length==args.Length);
        Assert(info!=null,"IO API missing: "+method);
        try{return info.Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException??e;}
    }

    private static void UpdatePlay()
    {
        try
        {
            if(!EditorApplication.isPlaying){if(EditorApplication.timeSinceStartup-_playStart>45)throw new Exception("Play failed to enter");return;}
            _frames++;
            var preview=UnityEngine.Object.FindAnyObjectByType<NavMeshDraftPreview>();
            if(preview==null || !preview.IsReady){if(EditorApplication.timeSinceStartup-_playStart>45)throw new Exception("Play preview not ready: "+preview?.Status);return;}
            if(!_destinationSet)
            {
                _start=preview.PlayerWorld;
                var snapshot=(NavMeshAuthoringDraft)typeof(NavMeshDraftPreview).GetField("_runtimeDraft",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(preview);
                Assert(snapshot!=null && snapshot!=preview.draft && !ReferenceEquals(snapshot.polygons,preview.draft.polygons) && !ReferenceEquals(snapshot.polygons[0].points,preview.draft.polygons[0].points),"Play authoring state is not privately snapshotted");
                Assert(NavMesh.SamplePosition(new Vector3(0,0,6),out var edge,3,NavMesh.AllAreas),"Play cannot locate native edge");
                Assert(!preview.TrySetDestination(new Vector2(edge.position.x-.03f,edge.position.z)),"Actual preview accepts 3cm horizontal snap rejected by server");
                Assert(!preview.TrySetDestination(new Vector2(10,6)),"Actual preview accepts target inside blocked hole");
                Assert(preview.TrySetDestination(_target),"Actual preview rejected valid target: "+preview.Status);
                _destinationSet=true;
            }
            var current=preview.PlayerWorld;
            var actualAgent=UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None).FirstOrDefault();
            Assert(actualAgent!=null && Mathf.Abs(actualAgent.nextPosition.y)<=.02f,"Actual Play agent left original ground height");
            Assert(!(current.x>8 && current.x<12 && current.y>3 && current.y<9),"Actual NavMeshAgent entered blocked hole");
            if(_frames<30)return;
            if(!_routeCaptured && Vector2.Distance(preview.PlayerWorld,_start)>2)
            {
                _report.routeScreenshot=Capture(preview.PreviewCamera,"navmesh-play-route-synthetic-smoke.png");
                _routeCaptured=true;
            }
            if(Vector2.Distance(preview.PlayerWorld,_target)<.3f)
            {
                Assert(Vector2.Distance(preview.PlayerWorld,_start)>5,"Agent did not actually move");
                Assert(preview.PreviewCamera!=null,"Actual preview camera missing");
                _report.playScreenshot=Capture(preview.PreviewCamera,"navmesh-play-synthetic-smoke.png");
                _report.checks.Add(new Check{name="actual Play NavMeshAgent moves around obstacle",result="PASS",detail="Play frames="+_frames+"; final="+preview.PlayerWorld});
                Finish(true);
            }
            else if(EditorApplication.timeSinceStartup-_playStart>45)throw new Exception("Agent failed to arrive. pos="+preview.PlayerWorld+" status="+preview.Status);
        }
        catch(Exception error){_report.error=error.ToString();Finish(false);}
    }

    private static string Capture(Camera camera,string filename)
    {
        Assert(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"No real graphics device");
        var rt=new RenderTexture(960,720,24);var texture=new Texture2D(960,720,TextureFormat.RGB24,false);
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,960,720),0,0);texture.Apply();
            var pixels=texture.GetPixels32();Assert(pixels.Any(p=>p.r!=pixels[0].r||p.g!=pixels[0].g||p.b!=pixels[0].b),"Screenshot uniform/empty");
            string path=Path.Combine(Output,filename);File.WriteAllBytes(path,texture.EncodeToPNG());return path;}
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);}
    }

    private static void WriteSyntheticImage(string path,int width,int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);var colors=new Color32[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)colors[y*width+x]=((x/10+y/10)%2==0)?new Color32(107,145,128,255):new Color32(158,173,146,255);
        image.SetPixels32(colors);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
    }

    private static void AssertArea(Mesh mesh,float expected)
    {
        double area=0;var v=mesh.vertices;var t=mesh.triangles;
        for(int i=0;i<t.Length;i+=3){var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);Assert(cross.y>0,"Triangle winding faces downward/degenerate");area+=cross.magnitude*.5;}
        Assert(Math.Abs(area-expected)<.001,"Area "+area+" expected "+expected);
    }
    private static void AssertNear(Vector2 a,Vector2 b,string message){Assert(Vector2.Distance(a,b)<.0001f,message+": "+a+" != "+b);}
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Throws(Action action,string message){try{action();}catch{return;}throw new Exception(message);}
    private static void Destroy(params UnityEngine.Object[] objects){foreach(var value in objects)UnityEngine.Object.DestroyImmediate(value);}
    private static string Sha(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    private static void RunCheck(string name,Action test){try{test();_report.checks.Add(new Check{name=name,result="PASS"});}catch(Exception e){_report.checks.Add(new Check{name=name,result="FAIL",detail=e.ToString()});}}
    private static void Finish(bool success)
    {
        EditorApplication.update-=UpdatePlay;SessionState.EraseString(StateKey);SessionState.EraseString(ReportKey);
        _report.result=success?"PASS":"FAIL";
        Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"verification.json"),JsonUtility.ToJson(_report,true));
        Debug.Log("[NavigationAuthoringVerification] "+_report.result+" "+JsonUtility.ToJson(_report));
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
    }
}
