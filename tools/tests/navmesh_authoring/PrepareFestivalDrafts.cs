using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MmorpgClient.Editor.NavigationAuthoring;
using MmorpgClient.World.NavigationAuthoring;
using UnityEditor;
using UnityEngine;

/// <summary>Creates blank authoring canvases in an isolated project with the client's actual GUIDs.</summary>
public static class PrepareFestivalDrafts
{
    [Serializable] public sealed class TileReceipt
    {
        public int row, column, width, height, runtimeTextureWidth, runtimeTextureHeight, mipmapCount;
        public string assetPath, textureGuid, sha256, runtimeFormat, filterMode;
    }
    [Serializable] public sealed class DraftReceipt
    {
        public string assetPath, displayName, mapKey, assetGuid, scriptGuid, assetSha256, editorClassIdentifier;
        public int tiles, columns, rows, imageWidth, imageHeight;
        public float worldWidth, worldDepth;
        public bool boundsConfirmed, spawnSet, hasBakedData, nativePixelViewValidated;
        public int polygonCount;
        public List<TileReceipt> sourceTiles = new List<TileReceipt>();
    }
    [Serializable] public sealed class Receipt
    {
        public string result, generatedUtc, unityVersion, sourceProject, runtimeAssembly, error;
        public bool existingArtworkOnly = true, authoredNavigationProvided = false;
        public List<DraftReceipt> drafts = new List<DraftReceipt>();
    }
    private sealed class Spec
    {
        public string key, label, folder;
        public int columns, rows;
        public Spec(string k,string l,string f,int c,int r){key=k;label=l;folder=f;columns=c;rows=r;}
    }

    public static void Run()
    {
        string source = Argument("-navmeshSourceProject");
        string output = Argument("-festivalDraftOut");
        if(string.IsNullOrEmpty(source) || string.IsNullOrEmpty(output))throw new InvalidOperationException("Pass -navmeshSourceProject and -festivalDraftOut.");
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        var receipt=new Receipt{result="RUNNING",generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,sourceProject=source,runtimeAssembly=typeof(NavMeshAuthoringDraft).Assembly.GetName().Name};
        try
        {
            string scriptGuid=ReadGuid(Path.Combine(source,"Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs.meta"));
            var specs=new[]{
                new Spec("26000-mid-autumn","26000 中秋","26000/mid-autumn",5,4),
                new Spec("26008-spring","26008 春节","26008/spring",4,3),
                new Spec("26008-mid-autumn","26008 中秋","26008/mid-autumn",4,3)
            };
            foreach(var spec in specs)
            {
                string folder="Assets/Resources/World/FestivalPreview20261111/"+spec.folder;
                string path="Assets/MapNavigationDrafts/Festival20261111/"+spec.key+".asset";
                var draft=AssetDatabase.LoadAssetAtPath<NavMeshAuthoringDraft>(path);
                if(draft==null)
                {
                    if(File.Exists(path))throw new InvalidOperationException("Existing unrecognized draft: "+path);
                    draft=NavMeshAuthoringAssetIO.CreateFromTileFolder(Path.GetFullPath(folder),path);
                    draft.mapKey=spec.key;draft.displayName=spec.label;
                    EditorUtility.SetDirty(draft);AssetDatabase.SaveAssetIfDirty(draft);
                }
                draft.Validate();
                Assert(draft.mapKey==spec.key && draft.displayName==spec.label,"Draft identity differs: "+path);
                Assert(draft.backgroundTiles.Length==spec.columns*spec.rows,"Wrong tile count: "+path);
                Assert(draft.imageWidth==spec.columns*1024 && draft.imageHeight==spec.rows*1024,"Wrong source dimensions: "+path);
                Assert(draft.worldRect.x==0 && draft.worldRect.y==0 && Mathf.Abs(draft.worldRect.width-300)<.0001f && Mathf.Abs(draft.worldRect.height-(300f*spec.rows/spec.columns))<.0001f,"Wrong local authoring ratio: "+path);
                Assert(!draft.boundsConfirmed && !draft.spawnSet && draft.bakedData==null && draft.polygons.Count==0,"Draft must be an unauthored blank canvas: "+path);
                var item=new DraftReceipt{assetPath=path,displayName=draft.displayName,mapKey=draft.mapKey,
                    assetGuid=AssetDatabase.AssetPathToGUID(path),scriptGuid=scriptGuid,tiles=draft.backgroundTiles.Length,
                    columns=spec.columns,rows=spec.rows,imageWidth=draft.imageWidth,imageHeight=draft.imageHeight,
                    worldWidth=draft.worldRect.width,worldDepth=draft.worldRect.height,boundsConfirmed=draft.boundsConfirmed,
                    spawnSet=draft.spawnSet,hasBakedData=draft.bakedData!=null,polygonCount=draft.polygons.Count};
                var seen=new HashSet<string>();
                foreach(var tile in draft.backgroundTiles)
                {
                    string mainImage=Path.Combine(source,tile.assetPath);
                    string mainGuid=ReadGuid(mainImage+".meta");
                    Assert(tile.row>=1 && tile.row<=spec.rows && tile.column>=1 && tile.column<=spec.columns && seen.Add(tile.row+":"+tile.column),"Tile row/column missing or duplicated");
                    Assert(tile.texture!=null && tile.pixelWidth==1024 && tile.pixelHeight==1024,"Tile texture missing/dimensions wrong");
                    Assert(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tile.texture))==mainGuid,"Texture reference is not the main client's GUID: "+tile.assetPath);
                    Assert(tile.sha256==Sha(File.ReadAllBytes(mainImage)) && tile.sha256==Sha(File.ReadAllBytes(tile.assetPath)),"Actual source SHA differs: "+tile.assetPath);
                    item.sourceTiles.Add(new TileReceipt{row=tile.row,column=tile.column,width=tile.pixelWidth,height=tile.pixelHeight,assetPath=tile.assetPath,textureGuid=mainGuid,sha256=tile.sha256,
                        runtimeTextureWidth=tile.texture.width,runtimeTextureHeight=tile.texture.height,runtimeFormat=tile.texture.format.ToString(),mipmapCount=tile.texture.mipmapCount,filterMode=tile.texture.filterMode.ToString()});
                }
                AssetDatabase.ForceReserializeAssets(new[]{path},ForceReserializeAssetsOptions.ReserializeAssets);
                var serialized=File.ReadAllText(path);
                item.editorClassIdentifier=typeof(NavMeshAuthoringDraft).Assembly.GetName().Name+"::"+typeof(NavMeshAuthoringDraft).FullName;
                Assert(serialized.Contains("m_EditorClassIdentifier: "+item.editorClassIdentifier),"Draft editor class identifier differs from the actual runtime assembly");
                Assert(Regex.IsMatch(serialized,@"m_Script:\s*\{[^}]*guid:\s*"+scriptGuid+@"\b"),"Draft's serialized script GUID differs from main source");
                foreach(var t in item.sourceTiles)Assert(serialized.Contains("guid: "+t.textureGuid),"Serialized texture GUID missing");
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var loaded=AssetDatabase.LoadAssetAtPath<NavMeshAuthoringDraft>(path);
                loaded.Validate();Assert(loaded.backgroundTiles.Length==item.tiles && loaded.polygons.Count==0,"Serialized draft did not reload as a blank canvas");
                TestWindow(loaded);item.nativePixelViewValidated=true;
                item.assetSha256=Sha(File.ReadAllBytes(path));
                receipt.drafts.Add(item);
            }
            receipt.result="PASS";
        }
        catch(Exception error){receipt.result="FAIL";receipt.error=error.ToString();}
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"draft-preparation-verification.json"),JsonUtility.ToJson(receipt,true));
        Debug.Log("[PrepareFestivalDrafts] "+JsonUtility.ToJson(receipt));
        EditorApplication.Exit(receipt.result=="PASS"?0:1);
    }

    private static void TestWindow(NavMeshAuthoringDraft draft)
    {
        var window=EditorWindow.GetWindow<NavMeshAuthoringWindow>();
        try
        {
            window.position=new Rect(10,10,1100,760);
            var open=typeof(NavMeshAuthoringWindow).GetMethod("OpenDraft",BindingFlags.Static|BindingFlags.Public);
            if(open!=null)open.Invoke(null,new object[]{draft});
            else typeof(NavMeshAuthoringWindow).GetMethod("SelectDraft",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{draft});
            var current=typeof(NavMeshAuthoringWindow).GetProperty("CurrentDraft",BindingFlags.Public|BindingFlags.Instance);
            Assert(current!=null && ReferenceEquals(current.GetValue(window),draft),"OpenDraft did not associate the selected actual draft");
            window.Show();window.Repaint();
            window.SendEvent(new Event{type=EventType.Layout});window.SendEvent(new Event{type=EventType.Repaint});
            typeof(NavMeshAuthoringWindow).GetMethod("NativePixelView",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
            float zoom=(float)typeof(NavMeshAuthoringWindow).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            Assert(Mathf.Abs(zoom*EditorGUIUtility.pixelsPerPoint-1)<.0001f,"Native pixel view does not map a source pixel to one physical screen pixel");
            window.SendEvent(new Event{type=EventType.Layout});window.SendEvent(new Event{type=EventType.Repaint});
        }
        finally{window.Close();}
    }
    private static string Argument(string key){var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]==key)return args[i+1];return null;}
    private static string ReadGuid(string meta){var m=Regex.Match(File.ReadAllText(meta),@"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");if(!m.Success)throw new InvalidOperationException("GUID missing: "+meta);return m.Groups[1].Value.ToLowerInvariant();}
    private static string Sha(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    private static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
