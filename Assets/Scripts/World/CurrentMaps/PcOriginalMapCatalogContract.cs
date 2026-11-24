using System;
using System.Collections.Generic;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>
    /// Pure C# contract and viewport streaming planner for PC original static background previews.
    /// Decouples logical mapId identity from temporary original asset roots so each map can be
    /// swapped cleanly to Q-Dao redrawn art by updating catalog metadata rather than C# paths.
    /// </summary>
    public static class PcOriginalMapContract
    {
        public const int SchemaVersion = 1;
        public const int DefaultTilePixels = 1024;
        public const int DefaultOverviewMaxEdge = 2048;
        public const float PixelsPerUnit = 1024f;
        public const int DefaultMaxResidentTiles = 24;
        public const int DefaultMaxConcurrentLoads = 4;
        public const string DefaultCatalogResourcePath = "World/PcOriginalPreview20261123/catalog";
        public const string ExpectedCatalogKind = "CLIENT_PC_ORIGINAL_STATIC_BACK_PREVIEW_CATALOG";
        public const string ArtStagePcOriginal = "pc_original_static_back";
        public const string ArtStageQdaoRedrawn = "qdao_redrawn_static_back";
        public const string ArtStageFormalDelivery = "formal_art_delivery";

        public static bool IsValidMapId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 5) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            return true;
        }

        public static bool IsValidSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        public static bool IsValidArtStage(string value)
            => value == ArtStagePcOriginal || value == ArtStageQdaoRedrawn || value == ArtStageFormalDelivery;

        public static bool IsValidAppearanceId(string value)
            => value == "normal" || value == "spring" || value == "lantern" ||
               value == "midautumn" || value == "mid-autumn";

        public static string NormalizeAppearanceId(string value)
            => value == "mid-autumn" ? "midautumn" : value;

        public static string AppearanceDisplayLabel(string appearanceId, string explicitLabel = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitLabel)) return explicitLabel;
            string norm = NormalizeAppearanceId(appearanceId);
            return norm == "spring" ? "春节" :
                   norm == "midautumn" ? "中秋" :
                   norm == "lantern" ? "元宵" :
                   norm == "normal" ? "日常" :
                   string.IsNullOrEmpty(appearanceId) ? "默认" : appearanceId;
        }

        public static bool IsSafeResourcePath(string value, bool expectTrailingSlash = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.StartsWith("/", StringComparison.Ordinal) || value.Contains("..") ||
                value.Contains("\\") || value.Contains("//")) return false;
            if (value.EndsWith("/", StringComparison.Ordinal) != expectTrailingSlash) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '/' || c == '-' || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        public static string ArtStageDisplayLabel(string artStage)
            => artStage == ArtStagePcOriginal ? "端游原图(临时可替换)" :
               artStage == ArtStageQdaoRedrawn ? "道家Q版改绘(静态底图)" :
               artStage == ArtStageFormalDelivery ? "正式美术交付" :
               string.IsNullOrEmpty(artStage) ? "未知阶段" : artStage;
    }

    [Serializable]
    public sealed class PcOriginalSourceContainer
    {
        public string path;
        public long bytes;
        public string sha256;
    }

    [Serializable]
    public sealed class PcOriginalMapAppearanceEntry
    {
        public string id;
        public string displayName;
        public string artStage;
        public bool isTemporaryOriginal;
        public string revision;
        public bool hasAlpha;
        public string tileFormat;
        public bool resampled;
        public string resourceRoot;
        public string overviewResourcePath;
        public string overviewExtension;
        public int overviewWidth;
        public int overviewHeight;
        public string overviewSha256;
        public long overviewBytes;
        public string tilesManifestResourcePath;
        public string tilesManifestSha256;
        public string sourceManifestResourcePath;
        public string sourceNativeSha256;
        public long sourceNativeBytes;
        public string sourceEvidenceSha256;

        public string DisplayLabel => PcOriginalMapContract.AppearanceDisplayLabel(id, displayName);

        public void Validate(string mapId, string pcOriginalSourceNativeSha256)
        {
            string label = $"{mapId}/{id}";
            if (!PcOriginalMapContract.IsValidAppearanceId(id))
                throw new InvalidOperationException($"{mapId}: unsupported appearance id '{id}'.");
            if (!PcOriginalMapContract.IsValidArtStage(artStage))
                throw new InvalidOperationException($"{label}: unsupported artStage '{artStage}'.");
            if (artStage == PcOriginalMapContract.ArtStageQdaoRedrawn && isTemporaryOriginal)
                throw new InvalidOperationException($"{label}: qdao_redrawn_static_back must have isTemporaryOriginal=false.");
            if (string.IsNullOrWhiteSpace(revision))
                throw new InvalidOperationException($"{label}: missing revision.");
            if (resampled)
                throw new InvalidOperationException($"{label}: native preview tiles must not be resampled.");
            if (!PcOriginalMapContract.IsSafeResourcePath(resourceRoot, expectTrailingSlash: true))
                throw new InvalidOperationException($"{label}: invalid resourceRoot '{resourceRoot}'.");
            if (!PcOriginalMapContract.IsSafeResourcePath(overviewResourcePath) ||
                !overviewResourcePath.StartsWith(resourceRoot, StringComparison.Ordinal))
                throw new InvalidOperationException($"{label}: invalid overviewResourcePath '{overviewResourcePath}'.");
            if (hasAlpha && !string.Equals(overviewExtension, ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{label}: RGBA appearance must use .png overview.");
            if (!PcOriginalMapContract.IsSafeResourcePath(tilesManifestResourcePath) ||
                !tilesManifestResourcePath.StartsWith(resourceRoot, StringComparison.Ordinal))
                throw new InvalidOperationException($"{label}: invalid tilesManifestResourcePath '{tilesManifestResourcePath}'.");
            if (overviewWidth <= 0 || overviewHeight <= 0 ||
                overviewWidth > PcOriginalMapContract.DefaultOverviewMaxEdge ||
                overviewHeight > PcOriginalMapContract.DefaultOverviewMaxEdge)
                throw new InvalidOperationException($"{label}: invalid overview size {overviewWidth}x{overviewHeight}.");
            if (!PcOriginalMapContract.IsValidSha256(sourceNativeSha256) || sourceNativeBytes <= 0)
                throw new InvalidOperationException($"{label}: invalid sourceNativeSha256 or byte size.");
            if (artStage == PcOriginalMapContract.ArtStageQdaoRedrawn &&
                !string.IsNullOrEmpty(pcOriginalSourceNativeSha256) &&
                string.Equals(sourceNativeSha256, pcOriginalSourceNativeSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{label}: Q-Dao appearance cannot reuse old PC original sourceNativeSha256.");
            if (!PcOriginalMapContract.IsValidSha256(tilesManifestSha256) ||
                !PcOriginalMapContract.IsValidSha256(overviewSha256) || overviewBytes <= 0)
                throw new InvalidOperationException($"{label}: invalid derived manifest/overview SHA.");
        }
    }

    [Serializable]
    public sealed class PcOriginalMapEntry
    {
        public string mapId;
        public uint numericMapId;
        public string name;
        public string artStage;
        public bool isTemporaryOriginal;
        public bool replaceableByQdaoArt;
        public string revision;
        public string sourceBranch;
        public string sourceStatus;
        public int nativeWidth;
        public int nativeHeight;
        public double aspectRatio;
        public int tileSize;
        public int columns;
        public int rows;
        public int tileCount;
        public bool hasAlpha;
        public string tileFormat;
        public bool resampled;
        public string resourceRoot;
        public string overviewResourcePath;
        public string overviewExtension;
        public int overviewWidth;
        public int overviewHeight;
        public string overviewSha256;
        public long overviewBytes;
        public string tilesManifestResourcePath;
        public string tilesManifestSha256;
        public string sourceManifestResourcePath;
        public string sourceNativeSha256;
        public long sourceNativeBytes;
        public string sourceEvidenceSha256;
        public string pcOriginalSourceNativeSha256;
        public long pcOriginalSourceNativeBytes;
        public string defaultAppearance;
        public PcOriginalMapAppearanceEntry[] appearances;
        public string serverRegistrationStatus;

        public float WorldWidth => nativeWidth / PcOriginalMapContract.PixelsPerUnit;
        public float WorldHeight => nativeHeight / PcOriginalMapContract.PixelsPerUnit;

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? "地图 " + mapId : name;

        public bool HasAppearances => appearances != null && appearances.Length > 0;
        public bool HasMultipleAppearances => appearances != null && appearances.Length > 1;
        public string EffectiveDefaultAppearance
            => !string.IsNullOrWhiteSpace(defaultAppearance)
                ? defaultAppearance
                : (HasAppearances ? appearances[0].id : "");

        public PcOriginalMapAppearanceEntry FindAppearance(string appearanceId)
        {
            if (!HasAppearances) return null;
            string query = string.IsNullOrWhiteSpace(appearanceId) ? EffectiveDefaultAppearance : appearanceId;
            if (string.IsNullOrWhiteSpace(query)) return null;
            for (int i = 0; i < appearances.Length; i++)
                if (appearances[i] != null && string.Equals(appearances[i].id, query, StringComparison.Ordinal))
                    return appearances[i];
            string normalizedQuery = PcOriginalMapContract.NormalizeAppearanceId(query);
            for (int i = 0; i < appearances.Length; i++)
                if (appearances[i] != null &&
                    string.Equals(PcOriginalMapContract.NormalizeAppearanceId(appearances[i].id), normalizedQuery, StringComparison.Ordinal))
                    return appearances[i];
            return null;
        }

        public PcOriginalMapEntry ResolveAppearance(string appearanceId = null)
        {
            if (!HasAppearances)
                return string.IsNullOrWhiteSpace(appearanceId) ? this : null;

            var app = FindAppearance(appearanceId);
            if (app == null) return null;

            return new PcOriginalMapEntry
            {
                mapId = mapId,
                numericMapId = numericMapId,
                name = name,
                artStage = app.artStage,
                isTemporaryOriginal = app.isTemporaryOriginal,
                replaceableByQdaoArt = replaceableByQdaoArt,
                revision = app.revision,
                sourceBranch = sourceBranch,
                sourceStatus = sourceStatus,
                nativeWidth = nativeWidth,
                nativeHeight = nativeHeight,
                aspectRatio = aspectRatio,
                tileSize = tileSize,
                columns = columns,
                rows = rows,
                tileCount = tileCount,
                hasAlpha = app.hasAlpha,
                tileFormat = app.tileFormat,
                resampled = app.resampled,
                resourceRoot = app.resourceRoot,
                overviewResourcePath = app.overviewResourcePath,
                overviewExtension = app.overviewExtension,
                overviewWidth = app.overviewWidth,
                overviewHeight = app.overviewHeight,
                overviewSha256 = app.overviewSha256,
                overviewBytes = app.overviewBytes,
                tilesManifestResourcePath = app.tilesManifestResourcePath,
                tilesManifestSha256 = app.tilesManifestSha256,
                sourceManifestResourcePath = app.sourceManifestResourcePath,
                sourceNativeSha256 = app.sourceNativeSha256,
                sourceNativeBytes = app.sourceNativeBytes,
                sourceEvidenceSha256 = app.sourceEvidenceSha256,
                pcOriginalSourceNativeSha256 = pcOriginalSourceNativeSha256,
                pcOriginalSourceNativeBytes = pcOriginalSourceNativeBytes,
                defaultAppearance = app.id,
                appearances = appearances,
                serverRegistrationStatus = serverRegistrationStatus
            };
        }

        public void Validate()
        {
            if (!PcOriginalMapContract.IsValidMapId(mapId))
                throw new InvalidOperationException($"Invalid 5-digit mapId: '{mapId}'.");
            if (numericMapId != uint.Parse(mapId))
                throw new InvalidOperationException($"{mapId}: numericMapId ({numericMapId}) does not match mapId.");
            if (!PcOriginalMapContract.IsValidArtStage(artStage))
                throw new InvalidOperationException($"{mapId}: unsupported artStage '{artStage}'.");
            if (artStage == PcOriginalMapContract.ArtStageQdaoRedrawn && isTemporaryOriginal)
                throw new InvalidOperationException($"{mapId}: qdao_redrawn_static_back must have isTemporaryOriginal=false.");
            if (string.IsNullOrWhiteSpace(revision))
                throw new InvalidOperationException($"{mapId}: missing revision.");
            if (nativeWidth <= 0 || nativeHeight <= 0)
                throw new InvalidOperationException($"{mapId}: invalid native pixel dimensions {nativeWidth}x{nativeHeight}.");
            if (tileSize <= 0 || tileSize > 2048)
                throw new InvalidOperationException($"{mapId}: invalid tileSize {tileSize}.");
            int expectedColumns = (nativeWidth + tileSize - 1) / tileSize;
            int expectedRows = (nativeHeight + tileSize - 1) / tileSize;
            if (columns != expectedColumns || rows != expectedRows || tileCount != columns * rows)
                throw new InvalidOperationException(
                    $"{mapId}: grid {columns}x{rows} (count {tileCount}) does not match {expectedColumns}x{expectedRows}.");
            double expectedRatio = nativeWidth / (double)nativeHeight;
            if (Math.Abs(aspectRatio - expectedRatio) > 1e-5)
                throw new InvalidOperationException($"{mapId}: aspectRatio {aspectRatio} deviates from {expectedRatio}.");
            if (resampled)
                throw new InvalidOperationException($"{mapId}: native preview tiles must not be resampled.");
            if (!PcOriginalMapContract.IsSafeResourcePath(resourceRoot, expectTrailingSlash: true))
                throw new InvalidOperationException($"{mapId}: invalid resourceRoot '{resourceRoot}'.");
            if (!PcOriginalMapContract.IsSafeResourcePath(overviewResourcePath) ||
                !overviewResourcePath.StartsWith(resourceRoot, StringComparison.Ordinal))
                throw new InvalidOperationException($"{mapId}: invalid overviewResourcePath '{overviewResourcePath}'.");
            if (hasAlpha && !string.Equals(overviewExtension, ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{mapId}: RGBA map must use .png overview.");
            if (!PcOriginalMapContract.IsSafeResourcePath(tilesManifestResourcePath) ||
                !tilesManifestResourcePath.StartsWith(resourceRoot, StringComparison.Ordinal))
                throw new InvalidOperationException($"{mapId}: invalid tilesManifestResourcePath '{tilesManifestResourcePath}'.");
            if (overviewWidth <= 0 || overviewHeight <= 0 ||
                overviewWidth > PcOriginalMapContract.DefaultOverviewMaxEdge ||
                overviewHeight > PcOriginalMapContract.DefaultOverviewMaxEdge)
                throw new InvalidOperationException($"{mapId}: invalid overview size {overviewWidth}x{overviewHeight}.");
            if (!PcOriginalMapContract.IsValidSha256(sourceNativeSha256) || sourceNativeBytes <= 0)
                throw new InvalidOperationException($"{mapId}: invalid sourceNativeSha256 or byte size.");
            if (!string.IsNullOrEmpty(pcOriginalSourceNativeSha256))
            {
                if (!PcOriginalMapContract.IsValidSha256(pcOriginalSourceNativeSha256) || pcOriginalSourceNativeBytes <= 0)
                    throw new InvalidOperationException($"{mapId}: invalid pcOriginalSourceNativeSha256 or byte size.");
                if (artStage == PcOriginalMapContract.ArtStageQdaoRedrawn &&
                    string.Equals(sourceNativeSha256, pcOriginalSourceNativeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{mapId}: Q-Dao map cannot reuse old PC original sourceNativeSha256.");
            }
            if (!PcOriginalMapContract.IsValidSha256(tilesManifestSha256) ||
                !PcOriginalMapContract.IsValidSha256(overviewSha256) || overviewBytes <= 0)
                throw new InvalidOperationException($"{mapId}: invalid derived manifest/overview SHA.");

            if (HasAppearances)
            {
                if (string.IsNullOrWhiteSpace(defaultAppearance))
                    throw new InvalidOperationException($"{mapId}: appearances[] requires defaultAppearance.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var roots = new HashSet<string>(StringComparer.Ordinal);
                PcOriginalMapAppearanceEntry defaultObj = null;
                for (int i = 0; i < appearances.Length; i++)
                {
                    var app = appearances[i];
                    if (app == null) throw new InvalidOperationException($"{mapId}: null appearance at index {i}.");
                    app.Validate(mapId, pcOriginalSourceNativeSha256);
                    string normId = PcOriginalMapContract.NormalizeAppearanceId(app.id);
                    if (!ids.Add(normId))
                        throw new InvalidOperationException($"{mapId}: duplicate appearance id '{app.id}'.");
                    if (!roots.Add(app.resourceRoot))
                        throw new InvalidOperationException($"{mapId}: duplicate appearance resourceRoot '{app.resourceRoot}'.");
                    if (string.Equals(app.id, defaultAppearance, StringComparison.Ordinal))
                        defaultObj = app;
                }
                if (defaultObj == null)
                    throw new InvalidOperationException($"{mapId}: defaultAppearance '{defaultAppearance}' not found in appearances[].");
                if (artStage != defaultObj.artStage || isTemporaryOriginal != defaultObj.isTemporaryOriginal ||
                    revision != defaultObj.revision || hasAlpha != defaultObj.hasAlpha ||
                    tileFormat != defaultObj.tileFormat || resourceRoot != defaultObj.resourceRoot ||
                    overviewResourcePath != defaultObj.overviewResourcePath ||
                    !string.Equals(overviewSha256, defaultObj.overviewSha256, StringComparison.OrdinalIgnoreCase) ||
                    overviewBytes != defaultObj.overviewBytes ||
                    tilesManifestResourcePath != defaultObj.tilesManifestResourcePath ||
                    !string.Equals(tilesManifestSha256, defaultObj.tilesManifestSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(sourceNativeSha256, defaultObj.sourceNativeSha256, StringComparison.OrdinalIgnoreCase) ||
                    sourceNativeBytes != defaultObj.sourceNativeBytes)
                    throw new InvalidOperationException($"{mapId}: top-level metadata disagrees with defaultAppearance '{defaultAppearance}'.");
            }
        }
    }

    [Serializable]
    public sealed class PcOriginalGapEntry
    {
        public string mapId;
        public uint numericMapId;
        public string status;
        public string reasonCode;
        public int infoHeaderCount;
        public string detail;
        public bool integrated;

        public void Validate()
        {
            if (!PcOriginalMapContract.IsValidMapId(mapId) || numericMapId != uint.Parse(mapId))
                throw new InvalidOperationException($"Invalid unavailable mapId '{mapId}'.");
            if (integrated)
                throw new InvalidOperationException($"Unavailable gap {mapId} must not be marked integrated.");
            if (string.IsNullOrWhiteSpace(reasonCode))
                throw new InvalidOperationException($"Unavailable gap {mapId} requires reasonCode.");
        }
    }

    [Serializable]
    public sealed class PcOriginalSpecialCandidateEntry
    {
        public string mapId;
        public uint numericMapId;
        public string branch;
        public string sourceStatus;
        public int nativeWidth;
        public int nativeHeight;
        public string sourceNativeSha256;
        public long sourceNativeBytes;
        public bool defaultBranchUnconfirmed;
        public bool gameMAPViewportConfirmed;
        public string reason;
        public bool integrated;

        public void Validate()
        {
            if (!PcOriginalMapContract.IsValidMapId(mapId) || numericMapId != uint.Parse(mapId))
                throw new InvalidOperationException($"Invalid special candidate mapId '{mapId}'.");
            if (integrated)
                throw new InvalidOperationException($"Special HOLD candidate {mapId} ({branch}) must not be marked integrated.");
            if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(sourceStatus))
                throw new InvalidOperationException($"Special candidate {mapId} requires branch and sourceStatus.");
            if (nativeWidth <= 0 || nativeHeight <= 0 || !PcOriginalMapContract.IsValidSha256(sourceNativeSha256))
                throw new InvalidOperationException($"Special candidate {mapId} has invalid native metadata.");
        }
    }

    [Serializable]
    public sealed class PcOriginalMapCatalog
    {
        public int schemaVersion;
        public string kind;
        public string catalogRevision;
        public string artStageDefault;
        public bool staticBackOnly;
        public bool completeGameplaySceneClaimed;
        public bool productionTravelEnabled;
        public PcOriginalSourceContainer sourceContainer;
        public string[] requestedMapIds;
        public int readyCount;
        public int unavailableCount;
        public int specialHoldCount;
        public PcOriginalMapEntry[] maps;
        public PcOriginalGapEntry[] unavailableMaps;
        public PcOriginalSpecialCandidateEntry[] specialHoldCandidates;

        public PcOriginalMapEntry FindByMapId(string mapId)
        {
            if (string.IsNullOrEmpty(mapId) || maps == null) return null;
            for (int i = 0; i < maps.Length; i++)
                if (string.Equals(maps[i].mapId, mapId, StringComparison.Ordinal)) return maps[i];
            return null;
        }

        public PcOriginalMapEntry FindByNumericId(uint numericMapId)
        {
            if (numericMapId == 0 || maps == null) return null;
            for (int i = 0; i < maps.Length; i++)
                if (maps[i].numericMapId == numericMapId) return maps[i];
            return null;
        }

        public void Validate()
        {
            if (schemaVersion != PcOriginalMapContract.SchemaVersion)
                throw new InvalidOperationException($"Unsupported catalog schemaVersion {schemaVersion}.");
            if (!string.Equals(kind, PcOriginalMapContract.ExpectedCatalogKind, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected catalog kind '{kind}'.");
            if (!staticBackOnly || completeGameplaySceneClaimed || productionTravelEnabled)
                throw new InvalidOperationException("PC original static-back catalog cannot claim gameplay scenes or online travel.");
            if (sourceContainer == null || !PcOriginalMapContract.IsValidSha256(sourceContainer.sha256))
                throw new InvalidOperationException("Missing or invalid sourceContainer SHA-256.");
            if (maps == null || maps.Length == 0)
                throw new InvalidOperationException("Catalog maps[] is empty.");
            if (unavailableMaps == null || specialHoldCandidates == null || requestedMapIds == null)
                throw new InvalidOperationException("Catalog must record requestedMapIds, unavailableMaps, and specialHoldCandidates.");
            if (readyCount != maps.Length || unavailableCount != unavailableMaps.Length ||
                specialHoldCount != specialHoldCandidates.Length)
                throw new InvalidOperationException("Catalog summary counts do not match array lengths.");

            var readyIds = new HashSet<string>(StringComparer.Ordinal);
            var readyNumeric = new HashSet<uint>();
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null) throw new InvalidOperationException($"Null map entry at index {i}.");
                maps[i].Validate();
                if (!readyIds.Add(maps[i].mapId) || !readyNumeric.Add(maps[i].numericMapId))
                    throw new InvalidOperationException($"Duplicate ready mapId '{maps[i].mapId}'.");
            }

            var unavailableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < unavailableMaps.Length; i++)
            {
                if (unavailableMaps[i] == null) throw new InvalidOperationException($"Null unavailable entry at index {i}.");
                unavailableMaps[i].Validate();
                if (!unavailableIds.Add(unavailableMaps[i].mapId))
                    throw new InvalidOperationException($"Duplicate unavailable mapId '{unavailableMaps[i].mapId}'.");
                if (readyIds.Contains(unavailableMaps[i].mapId))
                    throw new InvalidOperationException($"Unavailable mapId '{unavailableMaps[i].mapId}' cannot appear in maps[].");
            }

            for (int i = 0; i < specialHoldCandidates.Length; i++)
            {
                if (specialHoldCandidates[i] == null) throw new InvalidOperationException($"Null special candidate at index {i}.");
                specialHoldCandidates[i].Validate();
            }

            var requestedSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < requestedMapIds.Length; i++)
                if (!requestedSet.Add(requestedMapIds[i]))
                    throw new InvalidOperationException($"Duplicate requestedMapId '{requestedMapIds[i]}'.");
            var union = new HashSet<string>(readyIds, StringComparer.Ordinal);
            union.UnionWith(unavailableIds);
            if (!union.SetEquals(requestedSet))
                throw new InvalidOperationException("readyIds union unavailableIds must exactly equal requestedMapIds.");
        }
    }

    [Serializable]
    public sealed class PcOriginalTileEntry
    {
        public int row;
        public int column;
        public int x;
        public int y;
        public int width;
        public int height;
        public string resourcePath;
        public string fileExtension;
        public long bytes;
        public string sha256;
    }

    [Serializable]
    public sealed class PcOriginalTileManifest
    {
        public int schemaVersion;
        public string mapId;
        public uint numericMapId;
        public string revision;
        public string artStage;
        public string appearance;
        public int nativeWidth;
        public int nativeHeight;
        public int tileSize;
        public int columns;
        public int rows;
        public bool hasAlpha;
        public string tileFormat;
        public bool resampled;
        public string resourceRoot;
        public string sourceNativeSha256;
        public PcOriginalTileEntry[] tiles;

        public void Validate(PcOriginalMapEntry expectedEntry = null)
        {
            if (schemaVersion != PcOriginalMapContract.SchemaVersion)
                throw new InvalidOperationException($"Unsupported tile manifest schemaVersion {schemaVersion}.");
            if (!PcOriginalMapContract.IsValidMapId(mapId) || numericMapId != uint.Parse(mapId))
                throw new InvalidOperationException($"Invalid tile manifest mapId '{mapId}'.");
            if (nativeWidth <= 0 || nativeHeight <= 0 || tileSize <= 0 || tileSize > 2048 || resampled)
                throw new InvalidOperationException($"{mapId}: invalid tile manifest dimensions or resampled flag.");
            int expectedColumns = (nativeWidth + tileSize - 1) / tileSize;
            int expectedRows = (nativeHeight + tileSize - 1) / tileSize;
            if (columns != expectedColumns || rows != expectedRows || tiles == null || tiles.Length != columns * rows)
                throw new InvalidOperationException($"{mapId}: tile manifest count mismatch.");
            if (!PcOriginalMapContract.IsSafeResourcePath(resourceRoot, expectTrailingSlash: true))
                throw new InvalidOperationException($"{mapId}: invalid tile manifest resourceRoot.");
            if (expectedEntry != null)
            {
                if (expectedEntry.mapId != mapId || expectedEntry.revision != revision ||
                    expectedEntry.artStage != artStage || expectedEntry.hasAlpha != hasAlpha ||
                    expectedEntry.nativeWidth != nativeWidth || expectedEntry.nativeHeight != nativeHeight ||
                    expectedEntry.tileSize != tileSize || expectedEntry.columns != columns ||
                    expectedEntry.rows != rows || expectedEntry.resourceRoot != resourceRoot ||
                    !string.Equals(expectedEntry.sourceNativeSha256, sourceNativeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{mapId}: tile manifest does not match catalog entry.");
            }

            var occupied = new HashSet<int>();
            long area = 0;
            for (int i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i];
                if (tile == null || tile.row < 1 || tile.row > rows || tile.column < 1 || tile.column > columns)
                    throw new InvalidOperationException($"{mapId}: tile coordinate out of range at index {i}.");
                int key = (tile.row - 1) * columns + (tile.column - 1);
                if (!occupied.Add(key))
                    throw new InvalidOperationException($"{mapId}: duplicate tile ({tile.row},{tile.column}).");

                int expX = (tile.column - 1) * tileSize;
                int expY = (tile.row - 1) * tileSize;
                int expW = Math.Min(tileSize, nativeWidth - expX);
                int expH = Math.Min(tileSize, nativeHeight - expY);
                if (tile.x != expX || tile.y != expY || tile.width != expW || tile.height != expH ||
                    tile.width > tileSize || tile.height > tileSize)
                    throw new InvalidOperationException($"{mapId}: tile ({tile.row},{tile.column}) pixel rect mismatch.");
                if (!PcOriginalMapContract.IsSafeResourcePath(tile.resourcePath) ||
                    !tile.resourcePath.StartsWith(resourceRoot + "tiles/", StringComparison.Ordinal))
                    throw new InvalidOperationException($"{mapId}: invalid tile resourcePath '{tile.resourcePath}'.");
                if (hasAlpha && !string.Equals(tile.fileExtension, ".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{mapId}: RGBA tile ({tile.row},{tile.column}) must use .png.");
                if (!PcOriginalMapContract.IsValidSha256(tile.sha256) || tile.bytes <= 0)
                    throw new InvalidOperationException($"{mapId}: invalid tile SHA or bytes at ({tile.row},{tile.column}).");
                area += (long)tile.width * tile.height;
            }
            if (area != (long)nativeWidth * nativeHeight)
                throw new InvalidOperationException($"{mapId}: tile coverage area {area} != {(long)nativeWidth * nativeHeight}.");
        }
    }

    /// <summary>Axis-aligned world rectangle (bottom-left origin, Y-up, in PixelsPerUnit space).</summary>
    public readonly struct PcOriginalWorldRect
    {
        public readonly float XMin;
        public readonly float YMin;
        public readonly float Width;
        public readonly float Height;

        public PcOriginalWorldRect(float xMin, float yMin, float width, float height)
        {
            XMin = xMin;
            YMin = yMin;
            Width = width;
            Height = height;
        }

        public float XMax => XMin + Width;
        public float YMax => YMin + Height;
        public float CenterX => XMin + Width * 0.5f;
        public float CenterY => YMin + Height * 0.5f;

        public bool Overlaps(PcOriginalWorldRect other)
            => XMin < other.XMax && XMax > other.XMin && YMin < other.YMax && YMax > other.YMin;
    }

    /// <summary>Pure C# planner converting native top-left pixels to Unity Y-up world rects and selecting visible tiles.</summary>
    public static class PcOriginalViewportPlanner
    {
        public static PcOriginalWorldRect MapWorldBounds(int nativeWidth, int nativeHeight)
            => new PcOriginalWorldRect(0f, 0f,
                nativeWidth / PcOriginalMapContract.PixelsPerUnit,
                nativeHeight / PcOriginalMapContract.PixelsPerUnit);

        public static PcOriginalWorldRect TileWorldRect(int nativeHeight, PcOriginalTileEntry tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            float ppu = PcOriginalMapContract.PixelsPerUnit;
            float xMin = tile.x / ppu;
            // Source image Y=0 is at the top (north); Unity 2D world Y=0 is at the bottom (south).
            float yMin = (nativeHeight - (tile.y + tile.height)) / ppu;
            return new PcOriginalWorldRect(xMin, yMin, tile.width / ppu, tile.height / ppu);
        }

        /// <summary>
        /// Selects up to <paramref name="maxResidentTiles"/> visible tile indices overlapping
        /// <paramref name="viewRect"/>, ranked closest to viewport center first.
        /// </summary>
        public static int SelectVisibleTiles(
            PcOriginalTileManifest manifest,
            PcOriginalWorldRect viewRect,
            int maxResidentTiles,
            List<int> selectedIndices,
            out int totalOverlappingCount)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (selectedIndices == null) throw new ArgumentNullException(nameof(selectedIndices));
            selectedIndices.Clear();
            totalOverlappingCount = 0;
            if (manifest.tiles == null || manifest.tiles.Length == 0 || maxResidentTiles <= 0) return 0;

            var scored = new List<(int index, float sqrDist)>();
            float cx = viewRect.CenterX;
            float cy = viewRect.CenterY;

            for (int i = 0; i < manifest.tiles.Length; i++)
            {
                var rect = TileWorldRect(manifest.nativeHeight, manifest.tiles[i]);
                if (!rect.Overlaps(viewRect)) continue;
                totalOverlappingCount++;
                float dx = rect.CenterX - cx;
                float dy = rect.CenterY - cy;
                scored.Add((i, dx * dx + dy * dy));
            }

            scored.Sort((a, b) =>
            {
                int cmp = a.sqrDist.CompareTo(b.sqrDist);
                return cmp != 0 ? cmp : a.index.CompareTo(b.index);
            });

            int limit = Math.Min(maxResidentTiles, scored.Count);
            for (int i = 0; i < limit; i++)
                selectedIndices.Add(scored[i].index);
            return limit;
        }
    }
}
