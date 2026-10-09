using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>Production-builder placement audit and real Unity camera evidence. Run without -nographics.</summary>
    public static class TianyongNpcVerification
    {
        private const int ExpectedCount = 23;
        private const string DefaultOutput = "E:/work/tmp/city-npc-placement-20261009/verification";

        [Serializable] public sealed class NpcProof
        {
            public string id, displayName, location, resourcePath, textureFormat;
            public float x, z, visibleHeight, runtimeVisibleHeight, pivotX, pivotY;
            public float importedTransparentFraction, importedOpaqueFraction;
            public int textureWidth, textureHeight, routeWaypoints, sortingOrder;
            public bool resourceLoaded, alphaImportValid, alphaPixelsPreserved, independentFeetRoot;
            public bool feetPivotValid, nameplateValid, walkable, reachableFromSpawn, passed;
            public string error;
        }

        [Serializable] public sealed class ExclusionProof
        {
            public string appearance;
            public int npcLayerCount;
            public bool passed;
        }

        [Serializable] public sealed class ScreenshotProof
        {
            public string file, focusNpcId, playerCharacter;
            public float cameraX, cameraZ, orthographicSize, playerX, playerZ;
            public int width, height;
            public string[] visibleNpcIds;
        }

        [Serializable] public sealed class Report
        {
            public string verifiedAtUtc, unityVersion, graphicsDevice, projectPath;
            public string manifestResource = TianyongCityNpcs.PlacementsResourcePath;
            public int expectedNpcCount = ExpectedCount, actualNpcCount;
            public bool passed, separateLayer, noNpcColliders, repeatedBuildDoesNotDuplicate;
            public bool disposeRemovesLayer, rebuildCreatesExactlyOneLayer, proceduralCityHasNoNpcs;
            public bool screenshotsRendered, gameplayMovementExecuted, serverInteractionsVerified;
            public List<NpcProof> npcs = new();
            public List<ExclusionProof> exclusions = new();
            public List<ScreenshotProof> screenshots = new();
            public List<string> errors = new();
            public string[] limitations =
            {
                "Editor evidence from the actual production map builder and Camera.Render, not a connected game session.",
                "Reachability exercises the production A* navigation; it does not drive player locomotion or server NPC interactions.",
                "NPCs are static independent sprites; the delivered map artwork is not modified."
            };
        }

        /// <summary>Unity -batchmode -executeMethod MmorpgClient.Editor.Tianyong.TianyongNpcVerification.VerifyBatch</summary>
        public static void VerifyBatch()
        {
            var output = Argument("-npcEvidence") ?? DefaultOutput;
            var report = new Report
            {
                verifiedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            };
            Directory.CreateDirectory(output);
            try
            {
                Require(Application.isBatchMode, "Run in a separate batch editor with the project closed.");
                Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                    "Camera evidence needs a graphics device: omit -nographics.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Verification requires Edit Mode.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Verify(report, output);
                report.passed = report.errors.Count == 0;
            }
            catch (Exception error)
            {
                report.errors.Add(error.ToString());
                Debug.LogException(error);
            }
            File.WriteAllText(Path.Combine(output, "npc-placement-verification.json"), JsonUtility.ToJson(report, true));
            Debug.Log($"TIANYONG_NPC_VERIFICATION passed={report.passed} count={report.actualNpcCount} evidence={Path.GetFullPath(output)}");
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void Verify(Report report, string output)
        {
            var source = Resources.Load<TextAsset>(TianyongCityNpcs.PlacementsResourcePath);
            Require(source != null, "NPC placements resource is missing.");
            var manifest = JsonUtility.FromJson<TianyongNpcPlacementManifest>(source.text);
            Require(manifest != null, "NPC placements JSON is empty.");
            Require(manifest.Validate(out var manifestError), manifestError);
            Require(manifest.entries.Length == ExpectedCount, "Expected exactly 23 authored NPCs.");

            var stage = new GameObject("NpcVerificationStage");
            var camera = new GameObject("NpcVerificationCamera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1000f;
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .13f, .11f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.transparencySortMode = TransparencySortMode.Orthographic;
            camera.cullingMask = 1 << 31;
            TianyongMapInstance map = null;
            try
            {
                map = TianyongMapBuilder.Build(stage.transform, TianyongTheme.City);
                var layer = map.Root.GetComponentInChildren<TianyongCityNpcs>(true);
                Require(layer != null, "Production painted-city builder did not install the NPC layer.");
                Require(map.Root.transform.Find(TianyongPaintedCity.RootName) != null, "The painted city was not built.");
                report.actualNpcCount = layer.Count;
                Check(report, report.actualNpcCount == ExpectedCount, "Built NPC count differs from 23.");
                report.separateLayer = layer.name == TianyongCityNpcs.RootName && layer.transform.parent == map.Root.transform;
                Check(report, report.separateLayer, "NPC layer is not a separate child of the map root.");
                report.noNpcColliders = layer.GetComponentsInChildren<Collider>(true).Length == 0;
                Check(report, report.noNpcColliders, "Static NPC presentation unexpectedly introduces colliders.");
                layer.RefreshForCamera(camera);
                foreach (var entry in manifest.entries) VerifyNpc(report, map, layer, camera, entry);

                report.repeatedBuildDoesNotDuplicate = ReferenceEquals(layer, TianyongCityNpcs.Build(map)) &&
                    map.Root.GetComponentsInChildren<TianyongCityNpcs>(true).Length == 1 && layer.Count == ExpectedCount;
                Check(report, report.repeatedBuildDoesNotDuplicate, "Building twice duplicates the NPC layer.");
                CaptureEvidence(report, output, stage, map, layer, camera, manifest);
                var oldLayer = layer;
                map.Dispose();
                map = null;
                report.disposeRemovesLayer = oldLayer == null && stage.GetComponentsInChildren<TianyongCityNpcs>(true).Length == 0;
                Check(report, report.disposeRemovesLayer, "Disposing the city left NPC objects behind.");

                map = TianyongMapBuilder.Build(stage.transform, TianyongTheme.City);
                var rebuilt = map.Root.GetComponentsInChildren<TianyongCityNpcs>(true);
                report.rebuildCreatesExactlyOneLayer = rebuilt.Length == 1 && rebuilt[0].Count == ExpectedCount;
                Check(report, report.rebuildCreatesExactlyOneLayer, "Rebuilding the city did not create exactly one 23-NPC layer.");
                map.Dispose();
                map = null;

                foreach (TianyongTheme theme in Enum.GetValues(typeof(TianyongTheme)))
                {
                    if (theme == TianyongTheme.City) continue;
                    map = TianyongMapBuilder.Build(stage.transform, theme);
                    VerifyExcluded(report, map, "Tianyong/" + theme);
                    map.Dispose();
                    map = null;
                }

                // An explicit procedural City config must not receive painted-map placements.
                var proceduralConfig = ScriptableObject.CreateInstance<TianyongMapConfig>();
                try
                {
                    var serialized = new SerializedObject(proceduralConfig);
                    serialized.FindProperty("paintedCityGround").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    map = TianyongMapBuilder.Build(stage.transform, TianyongTheme.City, proceduralConfig);
                    report.proceduralCityHasNoNpcs = VerifyExcluded(report, map, "Tianyong/CityProcedural");
                    map.Dispose();
                    map = null;
                }
                finally { UnityEngine.Object.DestroyImmediate(proceduralConfig); }

                foreach (var region in FestivalRegionMap.Regions)
                foreach (bool festival in new[] { false, true })
                {
                    map = FestivalRegionMap.Build(stage.transform, region, festival);
                    VerifyExcluded(report, map, region.Key + (festival ? "/festival" : "/day"));
                    map.Dispose();
                    map = null;
                }
            }
            finally
            {
                map?.Dispose();
                UnityEngine.Object.DestroyImmediate(stage);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        private static void VerifyNpc(Report report, TianyongMapInstance map, TianyongCityNpcs layer,
            Camera camera, TianyongNpcPlacement entry)
        {
            var proof = new NpcProof
            {
                id = entry.id, displayName = entry.displayName, location = entry.location,
                resourcePath = entry.spriteResource, x = entry.x, z = entry.z,
                visibleHeight = entry.visibleHeight, pivotX = entry.pivotX, pivotY = entry.pivotY,
            };
            report.npcs.Add(proof);
            try
            {
                var texture = Resources.Load<Texture2D>(entry.spriteResource);
                proof.resourceLoaded = texture != null;
                Require(proof.resourceLoaded, "Texture resource is missing.");
                proof.textureWidth = texture.width;
                proof.textureHeight = texture.height;
                proof.textureFormat = texture.format.ToString();
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                proof.alphaImportValid = importer != null && importer.alphaSource == TextureImporterAlphaSource.FromInput &&
                    importer.alphaIsTransparency && !importer.mipmapEnabled &&
                    importer.wrapMode == TextureWrapMode.Clamp;
                Require(proof.alphaImportValid, "Expected input alpha, transparency enabled, no mipmaps, and Clamp import.");
                ReadAlpha(texture, out proof.importedTransparentFraction, out proof.importedOpaqueFraction);
                proof.alphaPixelsPreserved = proof.importedTransparentFraction > .01f && proof.importedOpaqueFraction > .01f;
                Require(proof.alphaPixelsPreserved, "Imported texture has lost its transparent background or visible body.");

                var feet = layer.transform.Find(entry.id);
                proof.independentFeetRoot = feet != null && feet.parent == layer.transform &&
                    Vector3.Distance(feet.localPosition, entry.FeetPosition) < .001f;
                Require(proof.independentFeetRoot, "Independent NPC feet root is missing or misplaced.");
                var body = feet.Find("sprite")?.GetComponent<SpriteRenderer>();
                Require(body != null && body.sprite != null, "NPC body SpriteRenderer is missing.");
                var sprite = body.sprite;
                proof.runtimeVisibleHeight = sprite.bounds.size.y * entry.visibleHeightFraction;
                proof.feetPivotValid = Mathf.Abs(sprite.pivot.x / sprite.rect.width - entry.pivotX) < .001f &&
                    Mathf.Abs(sprite.pivot.y / sprite.rect.height - entry.pivotY) < .001f &&
                    Mathf.Abs(proof.runtimeVisibleHeight - entry.visibleHeight) < .001f &&
                    Mathf.Abs(body.transform.position.x - feet.position.x) < .001f &&
                    Mathf.Abs(body.transform.position.z - feet.position.z) < .001f;
                Require(proof.feetPivotValid, "Runtime sprite feet pivot or visible height differs from its manifest.");
                proof.sortingOrder = body.sortingOrder;
                Require(body.sortingOrder == QdaoBoySpriteAnimator.WorldSortingOrder(feet.position, camera),
                    "NPC depth sorting differs from the player convention.");
                var label = feet.Find(WorldNameplate.ObjectName)?.GetComponent<TMPro.TMP_Text>();
                proof.nameplateValid = label != null && label.text == entry.displayName;
                Require(proof.nameplateValid, "NPC nameplate does not match the authored name.");

                proof.walkable = map.Navigation.IsWalkable(entry.FeetPosition);
                Require(proof.walkable, "NPC feet are not on the production walk mask.");
                var path = map.Navigation.FindPath(TianyongMapDefinition.DefaultSpawn, entry.FeetPosition);
                proof.routeWaypoints = path.Count;
                proof.reachableFromSpawn = path.Count > 0 &&
                    Vector3.Distance(path[path.Count - 1], entry.FeetPosition) < .01f;
                for (int i = 1; proof.reachableFromSpawn && i < path.Count; i++)
                {
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(path[i - 1], path[i]) / .5f));
                    for (int sample = 0; sample <= samples; sample++)
                        if (!map.Navigation.IsWalkable(Vector3.Lerp(path[i - 1], path[i], sample / (float)samples)))
                        { proof.reachableFromSpawn = false; break; }
                }
                Require(proof.reachableFromSpawn, "No walkable production route reaches the exact NPC feet from DefaultSpawn.");
                proof.passed = true;
            }
            catch (Exception error)
            {
                proof.error = error.Message;
                report.errors.Add(entry.id + ": " + error.Message);
            }
        }

        private static void ReadAlpha(Texture2D texture, out float transparentFraction, out float opaqueFraction)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                copy.Apply(false, false);
                var pixels = copy.GetPixels32();
                int transparent = 0, opaque = 0;
                foreach (var pixel in pixels)
                {
                    if (pixel.a <= 2) transparent++;
                    if (pixel.a >= 250) opaque++;
                }
                transparentFraction = transparent / (float)pixels.Length;
                opaqueFraction = opaque / (float)pixels.Length;
            }
            finally
            {
                RenderTexture.active = previous;
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static bool VerifyExcluded(Report report, TianyongMapInstance map, string appearance)
        {
            int count = map.Root.GetComponentsInChildren<TianyongCityNpcs>(true).Length;
            var proof = new ExclusionProof { appearance = appearance, npcLayerCount = count, passed = count == 0 };
            report.exclusions.Add(proof);
            Check(report, proof.passed, "Main-city NPCs leaked into " + appearance + ".");
            return proof.passed;
        }

        private static void CaptureEvidence(Report report, string output, GameObject stage, TianyongMapInstance map,
            TianyongCityNpcs layer, Camera camera, TianyongNpcPlacementManifest manifest)
        {
            var player = new GameObject("NpcVerificationPlayer");
            player.transform.SetParent(stage.transform, false);
            player.transform.position = TianyongMapDefinition.DefaultSpawn;
            Require(QdaoBoySpriteAnimator.TryAttach(player, QdaoCharacterCatalog.DefaultId), "Production player sprite could not be loaded.");
            var animator = player.GetComponent<QdaoBoySpriteAnimator>();
            var nameplate = WorldNameplate.Create(player.transform, "云行客", WorldNameplate.LocalPlayerColor);
            WorldLabelBillboard.Attach(nameplate.gameObject);
            SetLayer(stage.transform, 31);
            map.UpdateVisibleChunks(TianyongMapDefinition.DefaultSpawn, 20);
            camera.transform.position = new Vector3(200f, 500f, 150f);
            camera.orthographicSize = 150f;
            Capture(report, output, "city-npcs-overview.png", null, map, layer, camera, player, animator, manifest, 2048, 2048);

            // Include the crowded west market and three edge placements, then cover remaining districts.
            var priorityIds = new[] { "02", "23", "21", "19" };
            var anchors = new List<TianyongNpcPlacement>();
            for (int view = 0; view < Mathf.Min(6, manifest.entries.Length); view++)
            {
                TianyongNpcPlacement chosen = null;
                float best = float.NegativeInfinity;
                foreach (var candidate in manifest.entries)
                {
                    if (anchors.Contains(candidate)) continue;
                    float score = -Vector3.Distance(candidate.FeetPosition, TianyongMapDefinition.DefaultSpawn);
                    if (anchors.Count > 0)
                    {
                        score = float.PositiveInfinity;
                        foreach (var anchor in anchors)
                            score = Mathf.Min(score, Vector3.Distance(candidate.FeetPosition, anchor.FeetPosition));
                    }
                    if (score <= best) continue;
                    best = score;
                    chosen = candidate;
                }
                if (view < priorityIds.Length)
                    foreach (var candidate in manifest.entries)
                        if (candidate.id == priorityIds[view]) { chosen = candidate; break; }
                anchors.Add(chosen);
                player.transform.position = PlayerReferencePoint(map.Navigation, chosen.FeetPosition);
                var focus = (chosen.FeetPosition + player.transform.position) * .5f;
                camera.transform.position = new Vector3(focus.x, 500f, focus.z + 3f);
                camera.orthographicSize = 18f;
                Capture(report, output, $"city-npcs-near-{view + 1:00}-{chosen.id}.png", chosen.id,
                    map, layer, camera, player, animator, manifest, 1600, 1000);
            }
            report.screenshotsRendered = report.screenshots.Count == 7;
            Require(report.screenshotsRendered, "Expected one overview and six real-engine close views.");
            UnityEngine.Object.DestroyImmediate(player);
        }

        private static Vector3 PlayerReferencePoint(TianyongNavigationGrid navigation, Vector3 npc)
        {
            foreach (var offset in new[] { new Vector3(7f, 0f, 0f), new Vector3(-7f, 0f, 0f),
                         new Vector3(6f, 0f, -3f), new Vector3(-6f, 0f, -3f), new Vector3(0f, 0f, -8f) })
            {
                var point = npc + offset;
                if (!navigation.IsWalkable(point)) continue;
                if (navigation.FindPath(TianyongMapDefinition.DefaultSpawn, point).Count > 0) return point;
            }
            Require(navigation.TryFindNearestWalkable(npc + new Vector3(7f, 0f, 0f), out var nearest),
                "Could not place the reference player beside the NPC.");
            return nearest;
        }

        private static void Capture(Report report, string output, string filename, string focusId,
            TianyongMapInstance map, TianyongCityNpcs layer, Camera camera, GameObject player,
            QdaoBoySpriteAnimator animator, TianyongNpcPlacementManifest manifest, int width, int height)
        {
            RenderTexture target = null;
            Texture2D pixels = null;
            var previous = RenderTexture.active;
            try
            {
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.Create();
                camera.targetTexture = target;
                camera.aspect = width / (float)height;
                layer.RefreshForCamera(camera);
                animator.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
                animator.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
                foreach (var label in map.Root.GetComponentsInChildren<WorldLabelBillboard>(true))
                    label.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
                foreach (var label in player.GetComponentsInChildren<WorldLabelBillboard>(true))
                    label.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
                foreach (var label in map.Root.GetComponentsInChildren<TMPro.TMP_Text>(true)) label.ForceMeshUpdate();
                foreach (var label in player.GetComponentsInChildren<TMPro.TMP_Text>(true)) label.ForceMeshUpdate();
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(output, filename), pixels.EncodeToPNG());
                var visibleIds = new List<string>();
                foreach (var entry in manifest.entries)
                {
                    var screen = camera.WorldToViewportPoint(entry.FeetPosition);
                    if (screen.x > 0f && screen.x < 1f && screen.y > 0f && screen.y < 1f) visibleIds.Add(entry.id);
                }
                report.screenshots.Add(new ScreenshotProof
                {
                    file = filename, focusNpcId = focusId, playerCharacter = animator.CharacterId,
                    cameraX = camera.transform.position.x, cameraZ = camera.transform.position.z,
                    orthographicSize = camera.orthographicSize, playerX = player.transform.position.x,
                    playerZ = player.transform.position.z, width = width, height = height,
                    visibleNpcIds = visibleIds.ToArray(),
                });
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
        }

        private static void SetLayer(Transform root, int layer)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }

        private static void Check(Report report, bool condition, string error)
        { if (!condition) report.errors.Add(error); }

        private static void Require(bool condition, string error)
        { if (!condition) throw new InvalidOperationException(error); }

        private static string Argument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++) if (arguments[i] == name) return arguments[i + 1];
            return null;
        }
    }
}
