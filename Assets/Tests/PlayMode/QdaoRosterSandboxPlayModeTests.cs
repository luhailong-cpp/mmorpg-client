using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoRosterSandboxPlayModeTests
    {
        [UnityTest]
        public IEnumerator RealCitySandbox_SwitchesAllAvailableAppearancesWithoutReplacingThePlayer_AndWalksWithTheRealMotor()
        {
            var observations = new RuntimeObservedAppearances();
            var previousCapture = Time.captureFramerate;
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientLight;
            Time.captureFramerate = 60;
            var root = new GameObject("RosterCitySandboxTest");
            var cameraObject = new GameObject("RosterCityCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("RosterCitySun");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.AddComponent<Light>().type = LightType.Directional;
            var sandbox = root.AddComponent<TianyongSandboxBootstrap>();
            try
            {
                sandbox.SetHelpVisible(false);
                sandbox.BuildSandbox();
                var player = sandbox.Player;
                Assert.That(player, Is.Not.Null);
                var map = sandbox.Map;
                var controller = player.GetComponent<TianyongPlayerController>();
                var animator = player.GetComponent<QdaoBoySpriteAnimator>();
                var motor = controller.Motor;
                controller.SetScriptedInputOwner(true);
                Assert.That(motor, Is.Not.Null);
                Assert.That(motor.enabled, Is.True);
                var height = motor.height;
                var radius = motor.radius;
                var centre = motor.center;
                yield return null;
                yield return null;
                var routeOrigin = controller.FeetPosition;
                var availableAppearances = QdaoCharacterCatalog.AvailableAll;

                foreach (var definition in availableAppearances)
                {
                    var position = player.transform.position;
                    Assert.That(sandbox.SelectCharacter(definition.Id), Is.True);
                    Assert.That(player.transform.position, Is.EqualTo(position), "Changing art must not warp the player.");
                    yield return null;
                    Assert.That(sandbox.Player, Is.SameAs(player));
                    Assert.That(sandbox.Map, Is.SameAs(map), "Appearance switching must not rebuild map/navigation.");
                    Assert.That(player.GetComponent<TianyongPlayerController>(), Is.SameAs(controller));
                    Assert.That(controller.Motor, Is.SameAs(motor));
                    Assert.That(motor.enabled, Is.True);
                    Assert.That(motor.height, Is.EqualTo(height));
                    Assert.That(motor.radius, Is.EqualTo(radius));
                    Assert.That(motor.center, Is.EqualTo(centre));
                    Assert.That(animator.CharacterId, Is.EqualTo(definition.Id));
                    Assert.That(animator.FrameCount, Is.EqualTo(definition.FrameCount));
                    Assert.That(animator.ArtworkVersion, Is.EqualTo(definition.Version));
                    Assert.That(player.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo(definition.Name));
                    observations.selectedAppearanceCount++;
                    CaptureIfRequested(sandbox, definition.Id, observations);
                    if (!definition.IsOriginalRoster) continue;
                    Assert.That(animator.FrameCount, Is.EqualTo(16));
                    Assert.That(animator.ArtworkVersion, Is.EqualTo(definition.Version));
                    Assert.That(animator.ArtworkVersion, Is.EqualTo(13).Or.EqualTo(14));
                    yield return WalkWithRealMotor(sandbox, definition.Id, routeOrigin, observations);
                    observations.testedOriginalCount++;
                    if (animator.ArtworkVersion == 14)
                    {
                        if (definition.ResolveAppearance()?.IsMixedResolution == true) observations.testedMixedOriginalCount++;
                        else observations.testedHdOriginalCount++;
                    }
                    WriteObservationsIfRequested(observations);
                }
                foreach (var original in QdaoCharacterCatalog.OriginalAll)
                {
                    var available = original.ResolveAppearance() != null;
                    var listed = false;
                    foreach (var entry in sandbox.AvailableCharacters) if (entry.Id == original.Id) listed = true;
                    Assert.That(listed, Is.EqualTo(available), "F5 must list exactly the approved original identities.");
                    if (available) continue; // Selection and actual movement were tested above.
                    var previousIdentity = animator.CharacterId;
                    Assert.That(sandbox.SelectCharacter(original.Id), Is.False);
                    Assert.That(animator.CharacterId, Is.EqualTo(previousIdentity));
                }
                Assert.That(sandbox.SelectCharacter("24_crane_hermit"), Is.False);

                // Keep the existing V12 real-motor baseline even when no Original has passed acceptance.
                Assert.That(sandbox.SelectCharacter("24_lu_dongbin"), Is.True);
                yield return WalkWithRealMotor(sandbox, "24_lu_dongbin", routeOrigin, observations);
                Assert.That(controller.Motor, Is.SameAs(motor));
                observations.behaviorAssertionsCompleted = true;
                WriteObservationsIfRequested(observations);
                Debug.Log($"[QdaoRosterCity] {observations.selectedAppearanceCount} appearances switched; {observations.testedOriginalCount} original appearances walked with the real motor; Lu baseline retained. Offline sandbox, no online login asserted.");
            }
            finally
            {
                Time.captureFramerate = previousCapture;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.Destroy(root);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        private static IEnumerator WalkWithRealMotor(TianyongSandboxBootstrap sandbox, string characterId,
            Vector3 routeOrigin, RuntimeObservedAppearances observations)
        {
            var player = sandbox.Player;
            var controller = player.GetComponent<TianyongPlayerController>();
            var animator = player.GetComponent<QdaoBoySpriteAnimator>();
            var motor = controller.Motor;
            Assert.That(animator.CharacterId, Is.EqualTo(characterId));
            controller.SetDebugDirection(Vector3.zero);
            // Each character starts at the same open city route. This setup warp is
            // deliberately outside the recorded interval and is never counted as walking.
            controller.WarpTo(routeOrigin);
            yield return null;
            yield return null;
            Assert.That(motor.enabled, Is.True);
            var start = controller.FeetPosition;
            var direction = FindOpenDirection(sandbox.Map.Navigation, start);
            Assert.That(direction, Is.Not.EqualTo(Vector3.zero), "The city spawn must expose a short walkable test route.");
            var renderer = player.transform.Find("sprite").GetComponent<SpriteRenderer>();
            var poses = new HashSet<Sprite>();
            var lastFeet = start;
            var pathDistance = 0f;
            var movementStartTime = Time.time;
            controller.SetDebugDirection(direction);
            for (var frame = 0; frame < 32; frame++)
            {
                yield return null;
                if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Run) poses.Add(renderer.sprite);
                var step = controller.FeetPosition - lastFeet;
                step.y = 0f;
                pathDistance += step.magnitude;
                lastFeet = controller.FeetPosition;
            }
            var movementSeconds = Time.time - movementStartTime;
            controller.SetDebugDirection(Vector3.zero);
            var end = controller.FeetPosition;
            var travel = end - start;
            travel.y = 0f;
            Assert.That(travel.magnitude, Is.GreaterThan(3f), "The real CharacterController must travel across the city pavement.");
            Assert.That(poses.Count, Is.EqualTo(QdaoCharacterCatalog.Find(characterId).FrameCount),
                characterId + ": real controller movement must animate every separately imported pose.");
            var stationaryFrames = 0;
            for (var frame = 0; frame < 48; frame++)
            {
                yield return null;
                stationaryFrames++;
                if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Idle) break;
            }
            Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
            Assert.That(controller.Motor, Is.SameAs(motor));
            Assert.That(animator.CharacterId, Is.EqualTo(characterId));
            RecordMovementIfRequested(sandbox, observations, travel.magnitude, pathDistance,
                movementSeconds, poses, stationaryFrames, start, end);
        }

        private static Vector3 FindOpenDirection(TianyongNavigationGrid navigation, Vector3 start)
        {
            for (var sector = 0; sector < 8; sector++)
            {
                var radians = sector * 45f * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                var side = new Vector3(direction.z, 0f, -direction.x) * 0.5f;
                var clear = true;
                for (var step = 1; step <= 24; step++)
                {
                    var sample = start + direction * (step * 0.25f);
                    if (navigation.IsWalkable(sample) && navigation.IsWalkable(sample + side) && navigation.IsWalkable(sample - side)) continue;
                    clear = false;
                    break;
                }
                if (clear) return direction;
            }
            return Vector3.zero;
        }

        [System.Serializable]
        private sealed class RuntimeObservedAppearances
        {
            public int schemaVersion = 2;
            public string scope = "Observed real offline Tianyong sandbox and real movement controller; this report does not approve artwork or assert online login.";
            public string generatedUtc;
            public string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            public string inputSnapshotPath;
            public string inputSnapshotSha256;
            public string unityVersion = Application.unityVersion;
            public int deviceMaxTextureSize = SystemInfo.maxTextureSize;
            public bool behaviorAssertionsCompleted;
            public int selectedAppearanceCount;
            public int testedOriginalCount;
            public int testedHdOriginalCount;
            public int testedMixedOriginalCount;
            public List<ObservedAppearance> appearances = new();
        }

        [System.Serializable]
        private sealed class ObservedAppearance
        {
            public string requestedCharacterId;
            public string actualCharacterId;
            public int actualArtworkVersion;
            public int actualFrameCount;
            public bool actualIsOriginalRoster;
            public bool actualIsHd;
            public bool actualIsMixedResolution;
            public bool v14MixedContractObserved;
            public List<ObservedFrameGeometry> actualFrameGeometry = new();
            public int actualFrameWidth;
            public int actualFrameHeight;
            public float actualPixelsPerUnit;
            public Vector2 actualNormalizedPivot;
            public float actualFrameWorldHeight;
            public Vector3 actualBillboardScale;
            public DirectionFrameCounts actualTextureWidthsPerDirection = new();
            public DirectionFrameCounts actualTextureHeightsPerDirection = new();
            public int maxResidentHdDirectionsObserved;
            public bool actualHasDedicatedIdle;
            public float actualAnimationFramesPerSecond;
            public float actualFramesPerUnit;
            public float actualCycleDurationMs;
            public float actualCycleWorldDistance;
            public float controllerMoveSpeed;
            public string frameInventoryScope = "Loaded FrameSet.Walk arrays, distinct from direction poses sampled during actual movement.";
            public DirectionFrameCounts actualFramesPerDirection = new();
            public DirectionFrameCounts actualUniqueFrameSpritesPerDirection = new();
            public DirectionFrameCounts actualUniqueFrameTexturesPerDirection = new();
            public bool actualFramesMatchResources;
            public bool actualIdleMatchResources;
            public DirectionFrameCounts actualDedicatedIdleDirections = new();
            public int catalogVersion;
            public float catalogFramesPerSecond;
            public int catalogFrameDurationMs;
            public string resourceFolder;
            public string activationResourcePath;
            public bool activationPresent;
            public string activationSha256;
            public string activationManifestSha256;
            public string activationQcSha256;
            public string activationValidationSha256;
            public string manifestResourcePath;
            public bool manifestPresent;
            public string manifestSha256;
            public string manifestHashInput = "Resources.Load<TextAsset>(manifestResourcePath).bytes";
            public int activationAlignmentVersion;
            public int activationContactFrame;
            public string activationHashInput = "Resources.Load<TextAsset>(activationResourcePath).bytes";
            public string spriteName;
            public string spriteEntityId;
            public string textureName;
            public string textureEntityId;
            public int textureWidth;
            public int textureHeight;
            public float spriteRectWidth;
            public float spriteRectHeight;
            public string expectedIdleResourcePath;
            public bool spriteMatchesDedicatedIdle;
            public bool v13SixteenFrameContractObserved;
            public bool v14HdContractObserved;
            public CameraCaptureObservation normalView;
            public CameraCaptureObservation nearestView;
            public bool movementObserved;
            public float actualTravelDistance;
            public float actualPathDistance;
            public float movementSeconds;
            public Vector3 measuredRouteStart;
            public Vector3 measuredRouteEnd;
            public string routePreparation;
            public int observedWalkPoseCount;
            public List<string> observedWalkSpriteNames = new();
            public DirectionFrameCounts sampledPosesPerDirection = new();
            public int stationaryFramesUntilObservation;
            public bool stoppedIdle;
            public bool realMotorEnabled;
        }

        [System.Serializable]
        private sealed class CameraCaptureObservation
        {
            public string imagePath;
            public string imageSha256;
            public string scope = "Actual unchanged gameplay camera framing; projected full sprite rectangle, not opaque-alpha height.";
            public int renderWidth, renderHeight;
            public float requestedZoom, actualOrthographicSize;
            public float configuredZoomMin, configuredZoomDefault;
            public Vector3 actorFeetScreenPixels;
            public float frameLeftPixels, frameRightPixels, frameBottomPixels, frameTopPixels;
            public float projectedFrameHeightPixels;
            public float screenPixelsPerTexturePixel;
            public bool fullFrameInsideCapture;
        }

        [System.Serializable]
        private sealed class DirectionFrameCounts
        {
            public int N, NE, E, SE, S, SW, W, NW;
            public void Set(string direction, int count) => GetType().GetField(direction).SetValue(this, count);
        }

        [System.Serializable]
        private sealed class ObservedActivation
        {
            public int alignmentVersion;
            public int contactFrame;
            public int frameWidth;
            public int frameHeight;
            public float pixelsPerUnit;
            public float pivotX;
            public float pivotY;
            public string manifest_sha256;
            public string qc_sha256;
            public string validation_sha256;
        }

        [System.Serializable]
        private sealed class ObservedFrameGeometry
        {
            public string resourcePath;
            public int width, height;
            public float pixelsPerUnit, worldHeight;
            public Vector2 pivot;
        }
        private static readonly string[] ObservationDirections = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private static object ActualFrameSet(QdaoBoySpriteAnimator animator)
            => typeof(QdaoBoySpriteAnimator).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);

        private static void ObserveLoadedFrames(QdaoBoySpriteAnimator animator,
            QdaoCharacterCatalog.Appearance appearance, ObservedAppearance observed)
        {
            var frameSet = ActualFrameSet(animator);
            Assert.That(frameSet, Is.Not.Null, "Runtime evidence must inspect an actually loaded animator FrameSet.");
            var type = frameSet.GetType();
            var hd = appearance?.IsHd == true;
            observed.actualHasDedicatedIdle = (bool)type.GetField("DedicatedIdle").GetValue(frameSet);
            observed.actualAnimationFramesPerSecond = (float)type.GetField("Fps").GetValue(frameSet);
            observed.actualFramesPerUnit = (float)type.GetProperty("FramesPerUnit").GetValue(frameSet);
            observed.actualCycleDurationMs = observed.actualFrameCount / observed.actualAnimationFramesPerSecond * 1000f;
            observed.actualCycleWorldDistance = observed.actualFrameCount / observed.actualFramesPerUnit;
            observed.frameInventoryScope = hd
                ? "Eight HD directions loaded and inspected sequentially; only current rendered direction plus one observation direction are leased. Not simultaneous eight-direction residency."
                : "Loaded FrameSet.Walk arrays, distinct from direction poses sampled during actual movement.";
            var rendered = animator.transform.Find("sprite").GetComponent<SpriteRenderer>();
            var activeSprite = rendered.sprite;
            var activeDirection = animator.Direction;
            observed.actualFramesMatchResources = appearance != null;
            observed.actualIdleMatchResources = observed.actualHasDedicatedIdle;
            observed.actualFrameGeometry.Clear();
            void ObserveGeometry(string path, Sprite sprite)
            {
                observed.actualFrameGeometry.Add(new ObservedFrameGeometry { resourcePath = path,
                    width = sprite.texture.width, height = sprite.texture.height, pixelsPerUnit = sprite.pixelsPerUnit,
                    worldHeight = sprite.rect.height / sprite.pixelsPerUnit,
                    pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height) });
            }
            try
            {
                for (var direction = 0; direction < ObservationDirections.Length; direction++)
                {
                    if (hd)
                    {
                        Assert.That(animator.EnsureDirectionFrames(direction), Is.True, "A complete approved HD direction must load without fallback.");
                        Assert.That(ActualFrameSet(animator), Is.SameAs(frameSet));
                        Assert.That(animator.Direction, Is.EqualTo(activeDirection));
                        Assert.That(rendered.sprite, Is.SameAs(activeSprite), "Inspection must never evict the displayed sprite.");
                        Assert.That(activeSprite.texture, Is.Not.Null);
                        observed.maxResidentHdDirectionsObserved = Mathf.Max(observed.maxResidentHdDirectionsObserved, animator.ResidentHdDirections);
                        Assert.That(animator.ResidentHdDirections, Is.LessThanOrEqualTo(2));
                    }
                    var walk = (Sprite[][])type.GetField("Walk").GetValue(frameSet);
                    var idle = (Sprite[])type.GetField("Idle").GetValue(frameSet);
                    var poses = direction < walk.Length ? walk[direction] : null;
                    var sprites = new HashSet<Sprite>();
                    var textures = new HashSet<Texture2D>();
                    var validCount = 0;
                    var width = 0; var height = 0;
                    if (poses != null)
                    for (var frame = 0; frame < poses.Length; frame++)
                    {
                        var sprite = poses[frame];
                        var texture = sprite != null ? sprite.texture : null;
                        var path = appearance?.FrameResourcePath(ObservationDirections[direction], frame);
                        if (texture == null || appearance == null || !appearance.GeometryForResource(path).Matches(sprite))
                        {
                            observed.actualFramesMatchResources = false;
                            continue;
                        }
                        // A zero summary means multiple dimensions; every measured frame is listed below.
                        width = validCount == 0 ? texture.width : width == texture.width ? width : 0;
                        height = validCount == 0 ? texture.height : height == texture.height ? height : 0;
                        validCount++;
                        sprites.Add(sprite);
                        textures.Add(texture);
                        ObserveGeometry(path, sprite);
                        if (texture != Resources.Load<Texture2D>(path))
                            observed.actualFramesMatchResources = false;
                    }
                    var standing = idle[direction];
                    var idleMatches = standing != null && appearance != null && standing.texture != null &&
                        appearance.GeometryForResource(appearance.IdleResourcePath(ObservationDirections[direction])).Matches(standing) &&
                        standing.texture == Resources.Load<Texture2D>(appearance.IdleResourcePath(ObservationDirections[direction]));
                    if (idleMatches) ObserveGeometry(appearance.IdleResourcePath(ObservationDirections[direction]), standing);
                    observed.actualIdleMatchResources &= idleMatches;
                    observed.actualDedicatedIdleDirections.Set(ObservationDirections[direction], idleMatches && observed.actualHasDedicatedIdle ? 1 : 0);
                    observed.actualTextureWidthsPerDirection.Set(ObservationDirections[direction], width);
                    observed.actualTextureHeightsPerDirection.Set(ObservationDirections[direction], height);
                    observed.actualFramesPerDirection.Set(ObservationDirections[direction], validCount);
                    observed.actualUniqueFrameSpritesPerDirection.Set(ObservationDirections[direction], sprites.Count);
                    observed.actualUniqueFrameTexturesPerDirection.Set(ObservationDirections[direction], textures.Count);
                    if (hd)
                    {
                        Assert.That(validCount, Is.EqualTo(16));
                        Assert.That(sprites.Count, Is.EqualTo(16));
                        Assert.That(textures.Count, Is.EqualTo(16));
                        Assert.That(idleMatches, Is.True);
                    }
                }
                if (hd) Assert.That(observed.actualFramesMatchResources && observed.actualIdleMatchResources, Is.True);
            }
            finally { if (hd) animator.ReleaseObservedDirection(); }
        }

        private static ObservedAppearance ObserveAppearance(TianyongSandboxBootstrap sandbox, string requestedId,
            RuntimeObservedAppearances observations)
        {
            ObservedAppearance observed = null;
            foreach (var entry in observations.appearances)
                if (entry.requestedCharacterId == requestedId) { observed = entry; break; }
            if (observed == null)
            {
                observed = new ObservedAppearance { requestedCharacterId = requestedId };
                observations.appearances.Add(observed);
            }
            var player = sandbox.Player;
            var animator = player.GetComponent<QdaoBoySpriteAnimator>();
            var appearance = QdaoCharacterCatalog.Find(animator.CharacterId)?.ResolveAppearance();
            var sprite = player.transform.Find("sprite").GetComponent<SpriteRenderer>().sprite;
            var texture = sprite != null ? sprite.texture : null;
            observed.actualCharacterId = animator.CharacterId;
            observed.actualArtworkVersion = animator.ArtworkVersion;
            observed.actualFrameCount = animator.FrameCount;
            observed.actualIsOriginalRoster = appearance?.IsOriginalRoster == true;
            ObserveLoadedFrames(animator, appearance, observed);
            observed.controllerMoveSpeed = (float)typeof(TianyongPlayerController).GetField(
                "_moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player.GetComponent<TianyongPlayerController>());
            observed.catalogVersion = appearance?.Version ?? 0;
            observed.catalogFramesPerSecond = appearance?.FramesPerSecond ?? 0f;
            observed.catalogFrameDurationMs = appearance?.FrameDurationMs ?? 0;
            observed.resourceFolder = appearance?.ResourceFolder;
            observed.activationResourcePath = appearance == null ? null : appearance.ResourceFolder + "/appearance";
            var activation = observed.activationResourcePath == null ? null : Resources.Load<TextAsset>(observed.activationResourcePath);
            observed.activationPresent = activation != null;
            using (var sha = SHA256.Create())
                observed.activationSha256 = activation == null ? null :
                    System.BitConverter.ToString(sha.ComputeHash(activation.bytes)).Replace("-", "").ToLowerInvariant();
            var activationFields = activation != null ? JsonUtility.FromJson<ObservedActivation>(activation.text) : null;
            observed.activationAlignmentVersion = activationFields?.alignmentVersion ?? 0;
            observed.activationContactFrame = activationFields?.contactFrame ?? -1;
            observed.activationManifestSha256 = activationFields?.manifest_sha256;
            observed.activationQcSha256 = activationFields?.qc_sha256;
            observed.activationValidationSha256 = activationFields?.validation_sha256;
            observed.manifestResourcePath = appearance == null ? null : appearance.ResourceFolder + "/manifest";
            var manifest = observed.manifestResourcePath == null ? null : Resources.Load<TextAsset>(observed.manifestResourcePath);
            observed.manifestPresent = manifest != null;
            using (var sha = SHA256.Create())
                observed.manifestSha256 = manifest == null ? null :
                    System.BitConverter.ToString(sha.ComputeHash(manifest.bytes)).Replace("-", "").ToLowerInvariant();
            if (observed.actualIsOriginalRoster)
            {
                Assert.That(observed.manifestPresent, Is.True, "Actual Original appearance must retain its accepted manifest resource.");
                Assert.That(observed.manifestSha256, Is.EqualTo(observed.activationManifestSha256).IgnoreCase,
                    "Actual Original manifest bytes must match the activation SHA during observation.");
            }
            observed.spriteName = sprite != null ? sprite.name : null;
            observed.spriteEntityId = sprite != null ? sprite.GetEntityId().ToString() : null;
            observed.textureName = texture != null ? texture.name : null;
            observed.textureEntityId = texture != null ? texture.GetEntityId().ToString() : null;
            observed.textureWidth = texture != null ? texture.width : 0;
            observed.textureHeight = texture != null ? texture.height : 0;
            observed.spriteRectWidth = sprite != null ? sprite.rect.width : 0f;
            observed.spriteRectHeight = sprite != null ? sprite.rect.height : 0f;
            observed.actualFrameWidth = sprite != null ? Mathf.RoundToInt(sprite.rect.width) : 0;
            observed.actualFrameHeight = sprite != null ? Mathf.RoundToInt(sprite.rect.height) : 0;
            observed.actualPixelsPerUnit = sprite != null ? sprite.pixelsPerUnit : 0f;
            observed.actualNormalizedPivot = sprite != null && sprite.rect.width > 0 && sprite.rect.height > 0
                ? new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height) : Vector2.zero;
            observed.actualBillboardScale = player.transform.Find("sprite").lossyScale;
            observed.actualFrameWorldHeight = sprite != null ? sprite.rect.height / sprite.pixelsPerUnit * observed.actualBillboardScale.y : 0f;
            observed.actualIsMixedResolution = animator.ArtworkVersion == 14 && appearance?.IsMixedResolution == true;
            observed.actualIsHd = animator.ArtworkVersion == 14 && appearance?.IsHd == true && !observed.actualIsMixedResolution;
            observed.expectedIdleResourcePath = appearance?.IdleResourcePath(ObservationDirections[animator.Direction]);
            observed.spriteMatchesDedicatedIdle = appearance != null && appearance.HasDedicatedIdle &&
                texture != null && texture == Resources.Load<Texture2D>(observed.expectedIdleResourcePath);
            observed.v13SixteenFrameContractObserved = animator.ArtworkVersion == 13 && animator.FrameCount == 16 &&
                appearance != null && appearance.Version == 13 && appearance.FrameCount == 16 &&
                appearance.FrameDurationMs == 30 && observed.activationPresent;
            observed.v14HdContractObserved = observed.actualIsHd && animator.FrameCount == 16 &&
                appearance.FrameDurationMs == 30 && observed.activationPresent && observed.textureWidth == 1024 &&
                observed.textureHeight == 1024 && observed.actualFrameWidth == 1024 && observed.actualFrameHeight == 1024 &&
                observed.actualPixelsPerUnit == 104f && activationFields.frameWidth == 1024 && activationFields.frameHeight == 1024 &&
                activationFields.pixelsPerUnit == 104f && Mathf.Abs(observed.actualNormalizedPivot.x - .5f) < .0001f &&
                Mathf.Abs(observed.actualNormalizedPivot.y - .08f) < .0001f &&
                Mathf.Abs(observed.actualFrameWorldHeight - 512f / 52f) < .0001f;
            observed.v14MixedContractObserved = observed.actualIsMixedResolution && animator.FrameCount == 16 &&
                appearance.FrameDurationMs == 30 && observed.activationPresent && observed.actualFrameGeometry.Count == 136 &&
                observed.actualFramesMatchResources && observed.actualIdleMatchResources &&
                Mathf.Abs(observed.actualNormalizedPivot.x - .5f) < .0001f && Mathf.Abs(observed.actualNormalizedPivot.y - .08f) < .0001f &&
                Mathf.Abs(observed.actualFrameWorldHeight - 512f / 52f) < .0001f;
            observed.realMotorEnabled = player.GetComponent<TianyongPlayerController>().Motor.enabled;
            return observed;
        }

        private static void RecordMovementIfRequested(TianyongSandboxBootstrap sandbox, RuntimeObservedAppearances observations,
            float travel, float pathDistance, float movementSeconds, HashSet<Sprite> poses, int stationaryFrames,
            Vector3 routeStart, Vector3 routeEnd)
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("QDAO_ROSTER_CAPTURE_DIR"))) return;
            var animator = sandbox.Player.GetComponent<QdaoBoySpriteAnimator>();
            // Save measured route evidence before sequential HD inventory checks can evict inactive directions.
            var poseNames = new List<string>();
            foreach (var pose in poses) poseNames.Add(pose != null ? pose.name : "<missing>");
            poseNames.Sort(System.StringComparer.Ordinal);
            var sampledCounts = new DirectionFrameCounts();
            var frameSet = ActualFrameSet(animator);
            var walk = (Sprite[][])frameSet.GetType().GetField("Walk").GetValue(frameSet);
            for (var direction = 0; direction < ObservationDirections.Length; direction++)
            {
                var sampled = 0;
                if (walk[direction] != null)
                    foreach (var pose in walk[direction]) if (poses.Contains(pose)) sampled++;
                sampledCounts.Set(ObservationDirections[direction], sampled);
            }
            var observed = ObserveAppearance(sandbox, animator.CharacterId, observations);
            observed.movementObserved = true;
            observed.actualTravelDistance = travel;
            observed.actualPathDistance = pathDistance;
            observed.movementSeconds = movementSeconds;
            observed.measuredRouteStart = routeStart;
            observed.measuredRouteEnd = routeEnd;
            observed.routePreparation = "WarpTo common open route before measurement; only subsequent real CharacterController travel is included.";
            observed.observedWalkPoseCount = poses.Count;
            observed.observedWalkSpriteNames = poseNames;
            observed.sampledPosesPerDirection = sampledCounts;
            observed.stationaryFramesUntilObservation = stationaryFrames;
            observed.stoppedIdle = animator.State == QdaoBoySpriteAnimator.LocomotionState.Idle;
            WriteObservationsIfRequested(observations);
        }

        private static void WriteObservationsIfRequested(RuntimeObservedAppearances observations)
        {
            var outputDirectory = System.Environment.GetEnvironmentVariable("QDAO_ROSTER_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outputDirectory)) return;
            Directory.CreateDirectory(outputDirectory);
            var snapshot = System.Environment.GetEnvironmentVariable("QDAO_ROSTER_INPUT_SNAPSHOT");
            if (!string.IsNullOrEmpty(snapshot))
            {
                var absoluteSnapshot = Path.GetFullPath(snapshot);
                Assert.That(File.Exists(absoluteSnapshot), Is.True, "QDAO_ROSTER_INPUT_SNAPSHOT must name the existing saved input snapshot.");
                using var sha = SHA256.Create();
                var hash = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(absoluteSnapshot))).Replace("-", "").ToLowerInvariant();
                if (!string.IsNullOrEmpty(observations.inputSnapshotSha256))
                    Assert.That(hash, Is.EqualTo(observations.inputSnapshotSha256), "The saved input snapshot changed during runtime observation.");
                observations.inputSnapshotPath = absoluteSnapshot;
                observations.inputSnapshotSha256 = hash;
            }
            observations.generatedUtc = System.DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(outputDirectory, "runtime-observed-appearances.json"), JsonUtility.ToJson(observations, true));
        }

        private static CameraCaptureObservation ObserveCameraProjection(TianyongSandboxBootstrap sandbox, int width, int height)
        {
            var camera = sandbox.WorldCamera;
            var renderer = sandbox.Player.transform.Find("sprite").GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.Not.Null);
            var bounds = renderer.sprite.bounds;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var x in new[] { bounds.min.x, bounds.max.x })
            foreach (var y in new[] { bounds.min.y, bounds.max.y })
            {
                var screen = camera.WorldToScreenPoint(renderer.transform.TransformPoint(new Vector3(x, y, 0f)));
                min = Vector2.Min(min, new Vector2(screen.x, screen.y));
                max = Vector2.Max(max, new Vector2(screen.x, screen.y));
            }
            return new CameraCaptureObservation
            {
                renderWidth = width, renderHeight = height,
                requestedZoom = sandbox.CameraRig.RequestedZoom, actualOrthographicSize = camera.orthographicSize,
                configuredZoomMin = TianyongMapConfig.LoadDefault().CameraZoomMin,
                configuredZoomDefault = TianyongMapConfig.LoadDefault().CameraZoomDefault,
                actorFeetScreenPixels = camera.WorldToScreenPoint(sandbox.Player.GetComponent<TianyongPlayerController>().FeetPosition),
                frameLeftPixels = min.x, frameRightPixels = max.x, frameBottomPixels = min.y, frameTopPixels = max.y,
                projectedFrameHeightPixels = max.y - min.y,
                screenPixelsPerTexturePixel = (max.y - min.y) / renderer.sprite.texture.height,
                fullFrameInsideCapture = min.x >= 0f && min.y >= 0f && max.x <= width && max.y <= height
            };
        }

        private static void CaptureIfRequested(TianyongSandboxBootstrap sandbox, string characterId,
            RuntimeObservedAppearances observations)
        {
            var outputDirectory = System.Environment.GetEnvironmentVariable("QDAO_ROSTER_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outputDirectory)) return;
            var observed = ObserveAppearance(sandbox, characterId, observations);
            WriteObservationsIfRequested(observations);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Directory.CreateDirectory(outputDirectory);
            var camera = sandbox.WorldCamera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousZoom = sandbox.CameraRig.RequestedZoom;
            var target = new RenderTexture(1920, 1080, 24);
            var capture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                sandbox.CameraRig.Snap();
                CameraCaptureObservation SaveView(string suffix)
                {
                    camera.Render();
                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    capture.Apply();
                    var path = Path.Combine(outputDirectory, "tianyong-" + characterId + suffix + ".png");
                    File.WriteAllBytes(path, capture.EncodeToPNG());
                    Assert.That(new FileInfo(path).Length, Is.GreaterThan(100000), "The real map capture should contain visible city artwork.");
                    var view = ObserveCameraProjection(sandbox, target.width, target.height);
                    view.imagePath = path;
                    using (var sha = SHA256.Create())
                        view.imageSha256 = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                    Debug.Log("[QdaoRosterCityCapture] " + path + " (offline Tianyong sandbox, actual zoom " + camera.orthographicSize + ")");
                    return view;
                }
                observed.normalView = SaveView("");
                if (observed.actualIsHd || observed.actualIsMixedResolution)
                {
                    sandbox.CameraRig.SetZoom(TianyongMapConfig.LoadDefault().CameraZoomMin);
                    sandbox.CameraRig.Snap();
                    observed.nearestView = SaveView("-nearest-zoom");
                }
                WriteObservationsIfRequested(observations);
            }
            finally
            {
                sandbox.CameraRig.SetZoom(previousZoom);
                sandbox.CameraRig.Snap();
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(capture);
            }
        }
    }
}
