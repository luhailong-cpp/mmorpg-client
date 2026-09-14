// Reproducible, editor-only slicing of the approved Jubaozhai artwork.
// Run through the official Unity MCP: JubaozhaiAssetBuilder.Build();
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class JubaozhaiAssetBuilder
{
    public static string SourceDirectory;
    public const string OutputDirectory = "Assets/Resources/UI/Ugui/JubaozhaiV1";
    static readonly string[] Pages = { "01-empty", "02-characters", "03-pets", "04-weapons", "05-armor", "06-summoning", "07-sets" };
    static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, Texture2D> Results = new Dictionary<string, Texture2D>();
    static Manifest manifest;
    static string sourceRoot;

    [Serializable] public class Source { public string path, sha256; public int width, height; }
    [Serializable] public class Patch { public int[] sourceRectTopLeft, destinationRectTopLeft; }
    [Serializable] public class Slice {
        public string name, source, sourceSha256, output, outputSha256, processing;
        public int width, height;
        public int[] borderLeftBottomRightTop;
        public float[] alphaMaskPolygonReference;
        public bool containsDynamicText, containsStaticTitle;
        public List<Patch> patches = new List<Patch>();
    }
    [Serializable] public class Manifest {
        public string version = "jubaozhai-v1-approved-art-slices";
        public string execution = "Unity Editor Texture2D pixel operations and TextureImporter; invoke with official Unity MCP";
        public string coordinateSystem = "Source/destination rectangles: native pixels, top-left x,y,width,height. Authoring coordinates: 1440x607; converted independently using each original width/height.";
        public string alphaPolicy = "Manual source-space masks preserve approved art; no generative edits. Occluded portrait badges are cut transparent and replaced by runtime labels.";
        public string limitations = "Original screenshots are flattened. The shared v10 transparent window_frame is reused for main_frame because the Jubaozhai window outline is occluded by scenery, tabs and close button. Avatar pixels originally occluded by badges cannot be recovered; badge regions are transparent. The static Chinese title is retained intentionally.";
        public string importer = "Sprite Single, 100 PPU, FullRect, explicit border, Clamp, Bilinear, no mipmap, no compression, RGBA32, sRGB";
        public string contactSheet = "contact-sheet.png";
        public string buildScriptSha256;
        public List<Source> sources = new List<Source>();
        public List<Slice> sprites = new List<Slice>();
    }

    [MenuItem("Tools/Jubaozhai/Build approved art slices")]
    public static void BuildMenu() { Debug.Log(Build()); }

    public static string Build()
    {
        sourceRoot = string.IsNullOrEmpty(SourceDirectory)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../image/designs/jubaozhai-ui"))
            : Path.GetFullPath(SourceDirectory);
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException(sourceRoot);
        manifest = new Manifest();
        manifest.buildScriptSha256 = Hash(File.ReadAllBytes("Assets/Editor/Jubaozhai/JubaozhaiAssetBuilder.cs"));
        Directory.CreateDirectory(OutputDirectory);
        try
        {
            foreach (string page in Pages) LoadSource(page, Path.Combine(sourceRoot, page + ".png"));
            const string shared = "Assets/Resources/UI/Ugui/AttributesPaintedV2/window_frame.png";
            LoadSource("v10-window", shared);
            var main = CopyTexture(Textures["v10-window"]);
            var mainRecord = Record("main_frame", "v10-window", main, new Vector4(97, 74, 97, 99),
                "Exact reuse of certified text-free v10 window_frame; original source qdao_ui_style_recut_v10/source/02-main-window.png. Existing transparent ornament alpha and four slice borders retained.");
            mainRecord.patches.Add(PatchOf(new RectInt(0, 0, main.width, main.height), new RectInt(0, 0, main.width, main.height)));

            // Nine source patches are assembled from corners, clean edges and blank center samples.
            // No strip passes over a title, name, level, price, icon, chevron or row portrait.
            Nine("panel", "01-empty", R(375,149,909,35), 9, 7, R(430,158,80,12), 800, 158, 256, 64);
            Nine("row_normal", "02-characters", R(375,269,908,80), 14, 10, R(664,288,110,40), 700, 287, 320, 110);
            Nine("row_selected", "02-characters", R(375,183,908,82), 15, 10, R(663,202,110,38), 700, 201, 320, 110);
            Nine("button_primary", "02-characters", R(1100,523,185,52), 27, 10, R(1130,539,25,21), 1134, 540, 240, 70);
            Nine("button_secondary", "02-characters", R(966,525,127,49), 17, 10, R(973,539,15,21), 980, 541, 200, 68);
            Nine("tab_normal", "01-empty", R(149,225,196,39), 13, 8, R(245,236,55,15), 257, 236, 240, 58);
            Nine("tab_selected", "01-empty", R(148,184,197,40), 13, 8, R(244,195,54,15), 257, 195, 240, 58);
            Nine("input", "02-characters", R(376,531,239,43), 12, 9, R(541,543,41,17), 555, 543, 320, 66);

            var titleRect = Native("01-empty", R(526,0,387,106));
            var title = Crop(Textures["01-empty"], titleRect);
            Vector2[] outline = {
                new Vector2(537,52),new Vector2(549,47),new Vector2(550,35),new Vector2(563,28),
                new Vector2(579,31),new Vector2(586,29),new Vector2(595,33),new Vector2(650,31),
                new Vector2(659,22),new Vector2(678,24),new Vector2(681,9),new Vector2(695,3),
                new Vector2(705,0),new Vector2(727,0),new Vector2(739,9),new Vector2(743,20),
                new Vector2(756,22),new Vector2(764,30),new Vector2(829,31),new Vector2(840,29),
                new Vector2(851,34),new Vector2(863,29),new Vector2(875,32),new Vector2(883,42),
                new Vector2(885,52),new Vector2(899,54),new Vector2(905,63),new Vector2(911,72),
                new Vector2(907,82),new Vector2(908,102),new Vector2(895,104),new Vector2(898,82),
                new Vector2(885,83),new Vector2(876,92),new Vector2(862,97),new Vector2(581,97),
                new Vector2(568,92),new Vector2(557,83),new Vector2(544,82),new Vector2(545,103),
                new Vector2(530,104),new Vector2(533,84),new Vector2(527,77),new Vector2(529,65)
            };
            PolygonMask(title, "01-empty", titleRect, outline);
            var titleRecord = Record("title_plate", "01-empty", title, Vector4.zero,
                "Exact static-title crop; polygon alpha outline in authoring coordinates excludes scenery. Contains only approved static 聚宝斋 title, jade/gold plaque, taiji and tassels.", true);
            titleRecord.alphaMaskPolygonReference = Flatten(outline);
            titleRecord.patches.Add(PatchOf(titleRect, new RectInt(0,0,title.width,title.height)));

            var closeRect = Native("01-empty", R(1295,45,49,48));
            var close = Crop(Textures["01-empty"], closeRect);
            CircleMask(close, "01-empty", closeRect, 1319, 68, 21.5f, false);
            var closeRecord = Record("close", "01-empty", close, Vector4.zero, "Circular alpha mask isolates gold/jade close button including its static cross glyph.");
            closeRecord.patches.Add(PatchOf(closeRect, new RectInt(0,0,close.width,close.height)));

            var guideRect = Native("01-empty", R(513,224,305,254));
            var guide = Crop(Textures["01-empty"], guideRect);
            Vector2[] guideShape = {
                new Vector2(513,395),new Vector2(536,368),new Vector2(525,320),new Vector2(535,265),
                new Vector2(563,246),new Vector2(598,249),new Vector2(633,236),new Vector2(655,225),
                new Vector2(689,225),new Vector2(712,246),new Vector2(723,278),new Vector2(716,311),
                new Vector2(722,351),new Vector2(744,361),new Vector2(768,370),new Vector2(770,396),
                new Vector2(790,408),new Vector2(790,435),new Vector2(817,445),new Vector2(816,466),
                new Vector2(723,475),new Vector2(582,478),new Vector2(566,463),new Vector2(524,455)
            };
            PolygonMask(guide,"01-empty",guideRect,guideShape);
            RemoveEdgePaper(guide);
            var guideRecord = Record("empty_guide", "01-empty", guide, Vector4.zero,
                "Girl-only source crop. Polygon excludes the speech bubble and its text; edge-connected warm-paper pixels (RGB distance <= 43 to local blank paper) removed, preserving enclosed highlights.");
            guideRecord.alphaMaskPolygonReference = Flatten(guideShape);
            guideRecord.patches.Add(PatchOf(guideRect,new RectInt(0,0,guide.width,guide.height)));

            float[] rows = {183,268,353,439};
            for (int i = 0; i < 4; i++)
            {
                Avatar("character_" + (i+1).ToString("00"), "02-characters", rows[i], true, false, false);
                Avatar("pet_" + (i+1).ToString("00"), "03-pets", rows[i], true, true, false);
                Avatar("weapon_" + (i+1).ToString("00"), "04-weapons", rows[i], false, true, false);
                Avatar("armor_" + (i+1).ToString("00"), "05-armor", rows[i], false, true, false);
                Avatar("summoning_" + (i+1).ToString("00"), "06-summoning", rows[i], false, false, true);
                Avatar("set_" + (i+1).ToString("00"), "07-sets", rows[i], false, true, false);
            }
            SaveAll();
            string auditDirectory = Path.Combine(sourceRoot, "unity-slices");
            Directory.CreateDirectory(auditDirectory);
            WriteContactSheet(Path.Combine(auditDirectory, "contact-sheet.png"));
            string json = JsonUtility.ToJson(manifest,true);
            File.WriteAllText(Path.Combine(auditDirectory,"manifest.json"), json + Environment.NewLine);
            File.WriteAllText(Path.Combine(OutputDirectory,"manifest.json"), json + Environment.NewLine);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string message = "Jubaozhai: " + manifest.sprites.Count + " independently imported sprites. Manifest: " + Path.Combine(auditDirectory,"manifest.json");
            Debug.Log(message);
            return message;
        }
        finally
        {
            foreach (var texture in Textures.Values) UnityEngine.Object.DestroyImmediate(texture);
            foreach (var texture in Results.Values) UnityEngine.Object.DestroyImmediate(texture);
            Textures.Clear(); Results.Clear();
        }
    }

    static Rect R(float x,float y,float w,float h) { return new Rect(x,y,w,h); }
    static RectInt Native(string source, Rect r)
    {
        Texture2D t = Textures[source];
        int x = Mathf.RoundToInt(r.x * t.width / 1440f), y = Mathf.RoundToInt(r.y * t.height / 607f);
        return new RectInt(x,y,Mathf.RoundToInt((r.x+r.width)*t.width/1440f)-x,Mathf.RoundToInt((r.y+r.height)*t.height/607f)-y);
    }
    static void LoadSource(string key, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Approved artwork missing",path);
        var t = new Texture2D(2,2,TextureFormat.RGBA32,false,false);
        byte[] bytes = File.ReadAllBytes(path);
        if (!ImageConversion.LoadImage(t,bytes,false)) throw new InvalidDataException(path);
        t.name = key; Textures.Add(key,t);
        manifest.sources.Add(new Source {path=Path.GetFullPath(path).Replace('\\','/'),sha256=Hash(bytes),width=t.width,height=t.height});
    }
    static Texture2D NewTexture(int width,int height)
    {
        var t = new Texture2D(width,height,TextureFormat.RGBA32,false,false);
        t.SetPixels32(new Color32[width*height]); return t;
    }
    static Texture2D CopyTexture(Texture2D t) { var n=NewTexture(t.width,t.height);n.SetPixels32(t.GetPixels32());n.Apply(false,false);return n; }
    static Texture2D Crop(Texture2D t, RectInt rect)
    {
        if(rect.x<0||rect.y<0||rect.xMax>t.width||rect.yMax>t.height) throw new ArgumentOutOfRangeException("crop",rect.ToString());
        var n=NewTexture(rect.width,rect.height);
        n.SetPixels(t.GetPixels(rect.x,t.height-rect.yMax,rect.width,rect.height)); n.Apply(false,false); return n;
    }
    static Patch PatchOf(RectInt from,RectInt to) { return new Patch {sourceRectTopLeft=new[]{from.x,from.y,from.width,from.height},destinationRectTopLeft=new[]{to.x,to.y,to.width,to.height}}; }
    static Slice Record(string name,string key,Texture2D result,Vector4 border,string rule,bool staticTitle=false)
    {
        Source src = manifest.sources.Find(s=>s.sha256==Hash(File.ReadAllBytes(key=="v10-window"?"Assets/Resources/UI/Ugui/AttributesPaintedV2/window_frame.png":Path.Combine(sourceRoot,key+".png"))));
        var s=new Slice {name=name,source=src.path,sourceSha256=src.sha256,output=OutputDirectory+"/"+name+".png",width=result.width,height=result.height,
            borderLeftBottomRightTop=new[]{(int)border.x,(int)border.y,(int)border.z,(int)border.w},containsDynamicText=false,containsStaticTitle=staticTitle,processing=rule};
        Results.Add(name,result);manifest.sprites.Add(s);return s;
    }
    static void Blit(Texture2D source,RectInt from,Texture2D dest,RectInt to)
    {
        var data=source.GetPixels32();var target=dest.GetPixels32();
        for(int y=0;y<to.height;y++) for(int x=0;x<to.width;x++)
        {
            // Nearest-neighbour deterministic patch transfer; no invented or AI-generated pixels.
            int sx=from.x+Mathf.Min(from.width-1,Mathf.FloorToInt((x+.5f)*from.width/to.width));
            int sy=from.y+Mathf.Min(from.height-1,Mathf.FloorToInt((y+.5f)*from.height/to.height));
            target[(dest.height-1-to.y-y)*dest.width+to.x+x]=data[(source.height-1-sy)*source.width+sx];
        }
        dest.SetPixels32(target);
    }
    static void Nine(string name,string source,Rect outer,float bx,float by,Rect blank,float edgeX,float edgeY,int w,int h)
    {
        Rect[] regions={R(outer.x,outer.y,bx,by),R(edgeX,outer.y,blank.width,by),R(outer.xMax-bx,outer.y,bx,by),
            R(outer.x,edgeY,bx,blank.height),blank,R(outer.xMax-bx,edgeY,bx,blank.height),
            R(outer.x,outer.yMax-by,bx,by),R(edgeX,outer.yMax-by,blank.width,by),R(outer.xMax-bx,outer.yMax-by,bx,by)};
        int left=Mathf.RoundToInt(bx*Textures[source].width/1440f),top=Mathf.RoundToInt(by*Textures[source].height/607f);
        var t=NewTexture(w,h);var s=Record(name,source,t,new Vector4(left,top,left,top),
            "Nine-part deterministic reassembly from approved screenshot corners, text-free edge strips and a blank paper/jade sample. Exact native source/destination patches follow. Central screenshot labels/icons are excluded.");
        int[] xs={0,left,w-left,w}; int[] ys={0,top,h-top,h};
        for(int iy=0;iy<3;iy++)for(int ix=0;ix<3;ix++)
        {
            RectInt from=Native(source,regions[iy*3+ix]);var to=new RectInt(xs[ix],ys[iy],xs[ix+1]-xs[ix],ys[iy+1]-ys[iy]);
            Blit(Textures[source],from,t,to);s.patches.Add(PatchOf(from,to));
        }
        t.Apply(false,false);
    }
    static void Avatar(string name,string key,float row,bool round,bool bothBadges,bool noBadges)
    {
        RectInt rect=Native(key,R(402,row-3,104,104));var t=Crop(Textures[key],rect);
        if(round) CircleMask(t,key,rect,450,row+40,43,false);
        else RoundedRectMask(t,key,rect,R(412,row+4,92,77),5);
        if(!noBadges)
        {
            if(round)
            {
                CircleMask(t,key,rect,490,row+64,19.5f,true);
                if(bothBadges)CircleMask(t,key,rect,414,row+64,16.5f,true);
            }
            else
            {
                CircleMask(t,key,rect,417,row+64,18.5f,true);
                CircleMask(t,key,rect,500,row+64,19.5f,true);
            }
        }
        var record=Record(name,key,t,Vector4.zero,round
            ? "Portrait source crop and circular alpha silhouette; original numeric badge removed with an explicit circular alpha cutout. Pet element badge also excluded. Runtime renders fresh level text over the badge cutout."
            : (noBadges?"Independent source row icon; rounded alpha rectangle preserves the approved summoning token. No label text in crop.":"Independent source row icon; rounded alpha rectangle and two lower badge circle cutouts remove all baked level/element text. Runtime renders fresh badge text; occluded art is not invented."));
        record.patches.Add(PatchOf(rect,new RectInt(0,0,t.width,t.height)));
        record.processing += " Mask coordinates at 1440x607 reference: rowTop="+row+"; circle center=(450,rowTop+40), radius=43; portrait level=(490,rowTop+64), radius=19.5; pet element=(414,rowTop+64), radius=16.5; item rect=(412,rowTop+4,92,77), radius=5; item badges=(417,rowTop+64,r18.5),(500,rowTop+64,r19.5).";
    }
    static Vector2 AuthorPoint(string key,RectInt rect,int x,int y)
    {var t=Textures[key];return new Vector2((rect.x+x+.5f)*1440f/t.width,(rect.y+y+.5f)*607f/t.height);}
    static void CircleMask(Texture2D t,string key,RectInt rect,float cx,float cy,float radius,bool remove)
    {
        Color32[] pixels=t.GetPixels32();
        for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)
        {
            Vector2 p=AuthorPoint(key,rect,x,y);float distance=Vector2.Distance(p,new Vector2(cx,cy));
            float a=remove?Mathf.Clamp01(distance-radius):Mathf.Clamp01(radius-distance);
            int i=(t.height-1-y)*t.width+x;Color32 c=pixels[i];c.a=(byte)Mathf.RoundToInt(c.a*a);pixels[i]=c;
        }
        t.SetPixels32(pixels);t.Apply(false,false);
    }
    static void RoundedRectMask(Texture2D t,string key,RectInt source,Rect bounds,float radius)
    {
        Color32[] pixels=t.GetPixels32();
        for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)
        {
            Vector2 p=AuthorPoint(key,source,x,y),center=new Vector2(Mathf.Clamp(p.x,bounds.x+radius,bounds.xMax-radius),Mathf.Clamp(p.y,bounds.y+radius,bounds.yMax-radius));
            float a=Mathf.Clamp01(radius-Vector2.Distance(p,center));int i=(t.height-1-y)*t.width+x;Color32 c=pixels[i];c.a=(byte)Mathf.RoundToInt(c.a*a);pixels[i]=c;
        }
        t.SetPixels32(pixels);t.Apply(false,false);
    }
    static float[] Flatten(Vector2[] points) { var data=new float[points.Length*2]; for(int i=0;i<points.Length;i++){data[i*2]=points[i].x;data[i*2+1]=points[i].y;} return data; }
    static bool Inside(Vector2 p,Vector2[] poly)
    {
        bool inside=false;for(int i=0,j=poly.Length-1;i<poly.Length;j=i++)
            if(((poly[i].y>p.y)!=(poly[j].y>p.y))&&(p.x<(poly[j].x-poly[i].x)*(p.y-poly[i].y)/(poly[j].y-poly[i].y)+poly[i].x)) inside=!inside;
        return inside;
    }
    static void PolygonMask(Texture2D t,string key,RectInt rect,Vector2[] polygon)
    {
        var data=t.GetPixels32();for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)
            if(!Inside(AuthorPoint(key,rect,x,y),polygon))data[(t.height-1-y)*t.width+x]=new Color32(0,0,0,0);
        t.SetPixels32(data);t.Apply(false,false);
    }
    static void RemoveEdgePaper(Texture2D t)
    {
        // Flood is edge-connected so highlights surrounded by the approved silhouette remain intact.
        Color32[] data=t.GetPixels32();bool[] seen=new bool[data.Length];var queue=new Queue<int>();
        for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++) {int i=y*t.width+x;if(data[i].a==0||x==0||y==0||x==t.width-1||y==t.height-1){seen[i]=true;queue.Enqueue(i);}}
        Func<Color32,bool> paper=c=> {int dr=c.r-237,dg=c.g-225,db=c.b-204;return dr*dr+dg*dg+db*db<=43*43;};
        while(queue.Count>0)
        {
            int i=queue.Dequeue();if(data[i].a!=0&&!paper(data[i]))continue;
            data[i]=new Color32(0,0,0,0);int x=i%t.width,y=i/t.width;
            int[] next={x>0?i-1:-1,x<t.width-1?i+1:-1,y>0?i-t.width:-1,y<t.height-1?i+t.width:-1};
            foreach(int n in next)if(n>=0&&!seen[n]){seen[n]=true;queue.Enqueue(n);}
        }
        t.SetPixels32(data);t.Apply(false,false);
    }
    static void SaveAll()
    {
        foreach(Slice s in manifest.sprites)
        {
            byte[] bytes=Results[s.name].EncodeToPNG();File.WriteAllBytes(s.output,bytes);s.outputSha256=Hash(bytes);
            AssetDatabase.ImportAsset(s.output,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(s.output);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=100;importer.spritePivot=new Vector2(.5f,.5f);
            int[] b=s.borderLeftBottomRightTop;importer.spriteBorder=new Vector4(b[0],b[1],b[2],b[3]);
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.sRGBTexture=true;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.crunchedCompression=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;importer.isReadable=false;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteMeshType=SpriteMeshType.FullRect;settings.spriteGenerateFallbackPhysicsShape=false;importer.SetTextureSettings(settings);
            var platform=importer.GetDefaultPlatformTextureSettings();platform.format=TextureImporterFormat.RGBA32;platform.textureCompression=TextureImporterCompression.Uncompressed;platform.maxTextureSize=4096;importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            Sprite sprite=AssetDatabase.LoadAssetAtPath<Sprite>(s.output);
            if(sprite==null||sprite.border!=new Vector4(b[0],b[1],b[2],b[3]))throw new InvalidOperationException("Sprite import validation failed: "+s.output);
        }
    }
    static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    static void WriteContactSheet(string path)
    {
        const int columns=6,cell=230,gap=12;int rows=Mathf.CeilToInt(manifest.sprites.Count/(float)columns);
        var sheet=NewTexture(columns*cell,rows*cell);var bg=new Color32[sheet.width*sheet.height];
        for(int y=0;y<sheet.height;y++)for(int x=0;x<sheet.width;x++){byte shade=(byte)(((x/16+y/16)%2==0)?75:91);bg[y*sheet.width+x]=new Color32(shade,shade,shade,255);}sheet.SetPixels32(bg);
        for(int i=0;i<manifest.sprites.Count;i++)
        {
            Texture2D tile=Results[manifest.sprites[i].name];float scale=Mathf.Min((cell-2*gap)/(float)tile.width,(cell-2*gap)/(float)tile.height);
            int w=Mathf.RoundToInt(tile.width*scale),h=Mathf.RoundToInt(tile.height*scale);
            AlphaBlit(tile,sheet,new RectInt(i%columns*cell+(cell-w)/2,i/columns*cell+(cell-h)/2,w,h));
        }
        sheet.Apply(false,false);File.WriteAllBytes(path,sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
    }
    static void AlphaBlit(Texture2D source,Texture2D target,RectInt dest)
    {
        var output=target.GetPixels32();var input=source.GetPixels32();
        for(int y=0;y<dest.height;y++)for(int x=0;x<dest.width;x++)
        {
            int sx=Mathf.Min(source.width-1,x*source.width/dest.width),sy=Mathf.Min(source.height-1,y*source.height/dest.height);
            Color32 c=input[(source.height-1-sy)*source.width+sx];int i=(target.height-1-dest.y-y)*target.width+dest.x+x;Color32 b=output[i];float a=c.a/255f;
            output[i]=new Color32((byte)(c.r*a+b.r*(1-a)),(byte)(c.g*a+b.g*(1-a)),(byte)(c.b*a+b.b*(1-a)),255);
        }
        target.SetPixels32(output);
    }
}