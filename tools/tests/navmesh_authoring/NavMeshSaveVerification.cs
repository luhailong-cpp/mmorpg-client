using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MmorpgClient.Editor.NavigationAuthoring;
using MmorpgClient.World.NavigationAuthoring;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class NavMeshSaveVerification
{
    private const string Root = "Assets/SaveTests";
    private static readonly JArray Checks = new JArray();
    private static readonly List<NavMeshAuthoringWindow> Windows = new List<NavMeshAuthoringWindow>();
    private static int _caseId;

    public static void Run()
    {
        var report = new JObject { ["result"] = "RUNNING", ["unityVersion"] = Application.unityVersion, ["checks"] = Checks };
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", "save-verification.json"));
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            int sourceIndex = Array.IndexOf(arguments, "-navmeshSourceProject");
            Require(sourceIndex >= 0 && sourceIndex + 1 < arguments.Length &&
                !string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), Path.GetFullPath(arguments[sourceIndex + 1]), StringComparison.OrdinalIgnoreCase) &&
                new DirectoryInfo(Path.Combine(Application.dataPath, "..")).Name == "verify-project",
                "Refusing to operate outside the isolated verification project");
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            Directory.CreateDirectory(Root);
            var texture = new Texture2D(200, 120, TextureFormat.RGB24, false);
            texture.SetPixels(Enumerable.Repeat(new Color(.2f, .3f, .4f), 200 * 120).ToArray()); texture.Apply();
            File.WriteAllBytes(Root + "/map.png", texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(Root + "/map.png", ImportAssetOptions.ForceSynchronousImport);
            Check("invalid completion retains points and identifies conflicting original edges", TestInvalidCompletion);
            Check("Save preserves existing 88/113-point outlines exactly across asset reload", TestHighPointSave);
            Check("Save persists invalid outline, invalidates old bake and permits rebake after repair", TestSaveAndBake);
            Check("one-point and two-point outlines survive asset reload", TestShortOutlines);
            Check("Return and KeypadEnter complete polygons through actual window events", TestKeys);
            Check("serialized window restores pending type, draft, selection and view", TestWindowSerialization);
            Check("reopening same draft preserves pending and switching drafts saves it", TestDraftSwitch);
            Check("Undo and Redo restore both pending window state and stored polygon", TestUndoRedo);
            report["result"] = "PASS";
        }
        catch (Exception error) { report["result"] = "FAIL"; report["error"] = error.ToString(); Debug.LogException(error); }
        finally
        {
            foreach (var window in Windows.ToArray())
            {
                try { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
                catch (Exception error)
                {
                    report["result"] = "FAIL";
                    if (report["cleanupErrors"] == null) report["cleanupErrors"] = new JArray();
                    ((JArray)report["cleanupErrors"]).Add(error.ToString());
                }
            }
            if ((string)report["result"] == "RUNNING") report["result"] = "FAIL";
            try { File.WriteAllText(output, report.ToString()); Debug.Log("[NavMeshSaveVerification] " + report["result"] + ": " + output); }
            catch (Exception error) { report["result"] = "FAIL"; Debug.LogException(error); }
            finally { EditorApplication.Exit((string)report["result"] == "PASS" ? 0 : 1); }
        }
    }

    private static void TestInvalidCompletion()
    {
        var draft = Draft(); var window = Window(draft); var points = Bowtie(); Pending(window, points, true);
        Call(window, "CompletePolygon");
        Require(Pending(window).SequenceEqual(points) && draft.polygons.Count == 1, "Rejected completion changed or discarded authored points");
        object diagnostic = Field(window, "_pendingDiagnostic");
        int first = (int)diagnostic.GetType().GetField("firstEdge").GetValue(diagnostic);
        int second = (int)diagnostic.GetType().GetField("secondEdge").GetValue(diagnostic);
        Require(first == 0 && second == 2, "Conflict diagnostics do not identify the two crossing original edges");
        Require(((string)Field(window, "_status")).Contains("保存当前绘制"), "Invalid completion does not explain how to preserve and repair the outline");
    }

    private static void TestHighPointSave()
    {
        var draft = Draft(false);
        draft.polygons.Add(Ring(false, 88, 100, 60, 90, 50)); draft.polygons.Add(Ring(true, 113, 100, 60, 10, 10));
        var walk = draft.polygons[0].points.ToArray(); var obstacle = draft.polygons[1].points.ToArray();
        var window = Window(draft); var points = Bowtie(); Pending(window, points, true); Call(window, "Save");
        Require(Pending(window).Count == 0 && draft.polygons.Count == 3, "Save did not transfer pending outline into the draft");
        Require((int)Field(window, "_selected") == 2 && Field(window, "_tool").ToString() == "Vertices", "Saved invalid outline is not selected for repair");
        Close(window); draft = Reload(draft);
        Require(draft.polygons[0].points.SequenceEqual(walk) && draft.polygons[1].points.SequenceEqual(obstacle), "Save rewrote existing 88/113-point regions");
        Require(draft.polygons[2].blocked && draft.polygons[2].points.SequenceEqual(points), "Asset reload changed pending points or obstacle type");
    }

    private static void TestSaveAndBake()
    {
        var draft = Draft(); var original = draft.polygons[0].points.ToArray();
        NavMeshAuthoringAssetIO.Bake(draft); string previousHash = draft.bakedGeometrySha256;
        var window = Window(draft); var points = Bowtie(); Pending(window, points, true); Call(window, "Save");
        Require(Pending(window).Count == 0 && draft.polygons.Count == 2, "Save did not retain the pending outline alongside the existing floor");
        Close(window); draft = Reload(draft);
        Require(draft.polygons[0].points.SequenceEqual(original) && draft.polygons[1].blocked && draft.polygons[1].points.SequenceEqual(points), "Saved invalid outline or existing floor changed during reload");
        Require(draft.bakedGeometrySha256 == previousHash && !NavMeshAuthoringAssetIO.IsBakeCurrent(draft), "Invalid saved outline left the old bake current");
        Throws(() => NavMeshAuthoringAssetIO.Bake(draft), "Invalid stored outline was baked");
        Throws(() => NavMeshAuthoringAssetIO.Export(draft, Path.GetFullPath(Root + "/InvalidExport")), "Stale invalid outline was exported");
        draft.polygons[1].points = new List<Vector2> { new Vector2(70,40), new Vector2(90,40), new Vector2(90,80), new Vector2(70,80) };
        NavMeshAuthoringAssetIO.Bake(draft);
        Require(NavMeshAuthoringAssetIO.IsBakeCurrent(draft) && draft.bakedGeometrySha256 != previousHash, "Repaired outline cannot be baked as a new current state");
    }

    private static void TestShortOutlines()
    {
        var draft = Draft(); var window = Window(draft);
        var one = new List<Vector2> { new Vector2(20,20) }; Pending(window, one, true); Call(window, "Save");
        var two = new List<Vector2> { new Vector2(30,30), new Vector2(40,40) }; Pending(window, two, false); Call(window, "Save");
        Close(window); draft = Reload(draft);
        Require(draft.polygons.Count == 3 && draft.polygons[1].points.SequenceEqual(one) && draft.polygons[2].points.SequenceEqual(two), "Short outlines were discarded or changed");
        Require(draft.polygons[1].blocked && !draft.polygons[2].blocked, "Short outline type changed");
        Throws(() => NavMeshAuthoringAssetIO.Bake(draft), "A one/two-point region was silently ignored during bake");
    }

    private static void TestKeys()
    {
        var draft = Draft(); var window = Window(draft); window.Show(); window.Focus();
        window.SendEvent(new Event { type = EventType.Layout }); window.SendEvent(new Event { type = EventType.Repaint });
        foreach (var key in new[] { KeyCode.Return, KeyCode.KeypadEnter })
        {
            int before = draft.polygons.Count;
            Pending(window, new List<Vector2> { new Vector2(30,30), new Vector2(50,30), new Vector2(40,50) }, true);
            EditorGUIUtility.editingTextField = false;
            window.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
            Require(Pending(window).Count == 0 && draft.polygons.Count == before + 1 && draft.polygons.Last().blocked, key + " did not complete the actual pending obstacle");
        }
    }

    private static void TestWindowSerialization()
    {
        var draft = Draft(); var window = Window(draft); var points = Bowtie(); Pending(window, points, true);
        Set(window, "_zoom", 2.5f); Set(window, "_pan", new Vector2(17,33)); Set(window, "_fit", false); Set(window, "_selected", 0); Set(window, "_vertex", 2);
        string json = EditorJsonUtility.ToJson(window);
        var restored = ScriptableObject.CreateInstance<NavMeshAuthoringWindow>(); Windows.Add(restored); EditorJsonUtility.FromJsonOverwrite(json, restored);
        Require(Pending(restored).SequenceEqual(points) && (bool)Field(restored, "_pendingBlocked") && restored.CurrentDraft == draft, "Window serialization lost pending points, type or draft association");
        Require((float)Field(restored, "_zoom") == 2.5f && (Vector2)Field(restored, "_pan") == new Vector2(17,33) && !(bool)Field(restored, "_fit"), "Window serialization lost the view");
        Require((int)Field(restored, "_selected") == 0 && (int)Field(restored, "_vertex") == 2, "Window serialization lost selection");
    }

    private static void TestDraftSwitch()
    {
        var first = Draft(); var second = Draft(); var window = Window(first); var points = Bowtie(); Pending(window, points, true);
        Call(window, "SelectDraft", first);
        Require(Pending(window).SequenceEqual(points) && first.polygons.Count == 1, "Reopening the same draft discarded or prematurely committed pending points");
        Call(window, "SelectDraft", second);
        Require(window.CurrentDraft == second && Pending(window).Count == 0, "Draft switch did not finish preserving the previous pending outline");
        first = Reload(first);
        Require(first.polygons.Count == 2 && first.polygons[1].points.SequenceEqual(points) && first.polygons[1].blocked, "Draft switch did not persist the previous raw outline");
    }

    private static void TestUndoRedo()
    {
        var draft = Draft(); var window = Window(draft); var points = Bowtie(); Pending(window, points, true);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Call(window, "Save"); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
        Undo.PerformUndo();
        Require(draft.polygons.Count == 1 && Pending(window).SequenceEqual(points) && (bool)Field(window, "_pendingBlocked"), "Undo lost the pending outline instead of restoring it");
        Undo.PerformRedo();
        Require(draft.polygons.Count == 2 && Pending(window).Count == 0 && draft.polygons[1].points.SequenceEqual(points), "Redo did not restore the saved outline");
    }

    private static NavMeshAuthoringDraft Draft(bool floor = true)
    {
        var draft = NavMeshAuthoringAssetIO.CreateFromImage(Path.GetFullPath(Root + "/map.png"), Root + "/Case" + (++_caseId) + ".asset");
        draft.worldRect = new Rect(0,0,20,12); draft.spawnSet = true; draft.spawn = new Vector2(3,6);
        if (floor) draft.polygons.Add(new NavMeshAreaPolygon { name = "existing floor", points = new List<Vector2> { new Vector2(10,10),new Vector2(190,10),new Vector2(190,110),new Vector2(10,110) } });
        EditorUtility.SetDirty(draft); AssetDatabase.SaveAssetIfDirty(draft); return draft;
    }

    private static NavMeshAuthoringWindow Window(NavMeshAuthoringDraft draft)
    {
        var window = ScriptableObject.CreateInstance<NavMeshAuthoringWindow>(); Windows.Add(window); Call(window, "SelectDraft", draft); return window;
    }

    private static void Close(NavMeshAuthoringWindow window) { Windows.Remove(window); if (window != null) UnityEngine.Object.DestroyImmediate(window); }
    private static NavMeshAuthoringDraft Reload(NavMeshAuthoringDraft draft)
    {
        string path = AssetDatabase.GetAssetPath(draft); Resources.UnloadAsset(draft);
        return AssetDatabase.LoadAssetAtPath<NavMeshAuthoringDraft>(path);
    }
    private static List<Vector2> Bowtie() => new List<Vector2> { new Vector2(20,20),new Vector2(180,100),new Vector2(20,100),new Vector2(160,20) };
    private static NavMeshAreaPolygon Ring(bool blocked, int count, float x, float y, float rx, float ry)
        => new NavMeshAreaPolygon { name = "existing " + count, blocked = blocked,
            points = Enumerable.Range(0,count).Select(i => new Vector2(x + rx * Mathf.Cos(i * Mathf.PI * 2 / count), y + ry * Mathf.Sin(i * Mathf.PI * 2 / count))).ToList() };
    private static List<Vector2> Pending(NavMeshAuthoringWindow window) => (List<Vector2>)Field(window, "_pending");
    private static void Pending(NavMeshAuthoringWindow window, List<Vector2> points, bool blocked)
    {
        Pending(window).Clear(); Pending(window).AddRange(points); Set(window, "_pendingBlocked", blocked);
        Set(window, "_pendingDiagnostic", null); Set(window, "_tool", Enum.Parse(typeof(NavMeshAuthoringWindow).GetField("_tool", BindingFlags.Instance | BindingFlags.NonPublic).FieldType, blocked ? "BlockPolygon" : "WalkPolygon"));
    }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target,args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static void Check(string name, Action test)
    {
        try { test(); Checks.Add(new JObject { ["name"] = name, ["result"] = "PASS" }); }
        catch (Exception error) { Checks.Add(new JObject { ["name"] = name, ["result"] = "FAIL", ["error"] = error.Message }); throw; }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws(Action action, string message) { bool threw = false; try { action(); } catch { threw = true; } Require(threw,message); }
}
