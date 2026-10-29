using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MmorpgClient.World;

// Runs without Unity. Only the production file verifier is linked into this executable.
// Usage: dotnet run --project tools/tests/character_availability --configuration Release -- <repo-root>
internal static class Program
{
    private static readonly List<string> Checks = new();
    private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    private static readonly string[] ExpectedIds = { "04", "05", "06", "07", "08", "09", "10", "14", "15", "17", "20" };
    private const string Roster = "World/Characters/QdaoOriginalRosterV14";

    private static int Main(string[] args)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            Require(args.Length <= 1, "Expected one optional repository-root argument.");
            var project = FindProject(args.Length == 1 ? args[0] : Directory.GetCurrentDirectory());
            var evidence = Path.Combine(project, ".codex-artifacts", "character-creation-20261027");
            var scratch = Path.Combine(evidence, "availability-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            CheckFileIdentity(scratch);
            var realResources = CheckRealIndexes(project);
            var report = new
            {
                ok = true,
                observedAtUtc = DateTime.UtcNow,
                productionHelper = "MmorpgClient.World.QdaoHdSourceFileVerifier.Matches",
                checks = Checks,
                realResources,
                pixelDecodingPerformed = false,
                sourceResourcesModified = false,
                elapsedMilliseconds = watch.ElapsedMilliseconds
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(evidence, "availability-runner-result.json"), json + Environment.NewLine);
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Character availability regression failed: " + error);
            return 1;
        }
    }

    private static string FindProject(string start)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(start)); current != null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Assets", "Scripts", "World", "QdaoHdSourceFileVerifier.cs")))
                return current.FullName;
        throw new InvalidOperationException("Could not locate the client repository from " + start);
    }

    private static void CheckFileIdentity(string scratch)
    {
        // A valid, synthetic 1x1 PNG written as a real file. Never a delivered character asset.
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        var hash = Sha256(bytes);
        var original = Path.Combine(scratch, "original.png");
        File.WriteAllBytes(original, bytes);
        File.SetLastWriteTimeUtc(original, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Check(QdaoHdSourceFileVerifier.Matches(original, bytes.Length, 1, 1, hash), "original PNG identity");

        var checkout = Path.Combine(scratch, "fresh-checkout.png");
        File.Copy(original, checkout);
        File.SetLastWriteTimeUtc(checkout, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Require(File.GetLastWriteTimeUtc(checkout) != File.GetLastWriteTimeUtc(original), "Fixture must have a different checkout timestamp.");
        Check(QdaoHdSourceFileVerifier.Matches(checkout, bytes.Length, 1, 1, hash), "fresh checkout with different mtime remains available");
        File.SetLastWriteTimeUtc(checkout, new DateTime(2015, 6, 7, 0, 0, 0, DateTimeKind.Utc));
        Check(QdaoHdSourceFileVerifier.Matches(checkout, bytes.Length, 1, 1, hash), "mtime-only changes remain available on repeated checks");
        Check(QdaoHdSourceFileVerifier.Matches(checkout, bytes.Length, 1, 1, hash.ToUpperInvariant()), "SHA256 hexadecimal case is portable");

        var matchingTime = File.GetLastWriteTimeUtc(checkout);
        var tampered = (byte[])bytes.Clone();
        tampered[45] ^= 1; // Preserve PNG header and dimensions, length and then the original timestamp.
        File.WriteAllBytes(checkout, tampered);
        File.SetLastWriteTimeUtc(checkout, matchingTime);
        Require(new FileInfo(checkout).Length == bytes.Length && File.GetLastWriteTimeUtc(checkout) == matchingTime,
            "Tamper regression must preserve both byte count and mtime.");
        Check(!QdaoHdSourceFileVerifier.Matches(checkout, bytes.Length, 1, 1, hash), "equal-length content tampering with forged mtime is rejected after a prior success");
        File.WriteAllBytes(checkout, bytes);
        File.SetLastWriteTimeUtc(checkout, matchingTime);
        Check(QdaoHdSourceFileVerifier.Matches(checkout, bytes.Length, 1, 1, hash), "restored source identity is available again");

        Check(!QdaoHdSourceFileVerifier.Matches(original, bytes.Length, 2, 1, hash), "wrong expected width is rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(original, bytes.Length, 1, 2, hash), "wrong expected height is rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(original, bytes.Length + 1, 1, 1, hash), "wrong expected byte count is rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(original, bytes.Length, 1, 1, new string('0', 64)), "wrong source hash is rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(Path.Combine(scratch, "missing.png"), bytes.Length, 1, 1, hash), "missing source is rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(scratch, bytes.Length, 1, 1, hash), "directory instead of source is rejected");

        var malformed = Path.Combine(scratch, "malformed.png");
        var badHeader = (byte[])bytes.Clone();
        badHeader[0] = 0;
        File.WriteAllBytes(malformed, badHeader);
        Check(!QdaoHdSourceFileVerifier.Matches(malformed, badHeader.Length, 1, 1, Sha256(badHeader)), "invalid PNG signature is rejected even when its hash matches");
        badHeader = (byte[])bytes.Clone();
        badHeader[12] = 0;
        File.WriteAllBytes(malformed, badHeader);
        Check(!QdaoHdSourceFileVerifier.Matches(malformed, badHeader.Length, 1, 1, Sha256(badHeader)), "invalid IHDR identity is rejected even when its hash matches");
        File.WriteAllBytes(malformed, bytes.Take(20).ToArray());
        Check(!QdaoHdSourceFileVerifier.Matches(malformed, 20, 1, 1, hash), "truncated source is rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(original, bytes.Length, 0, 1, hash), "nonpositive dimensions are rejected");
        Check(!QdaoHdSourceFileVerifier.Matches(original, bytes.Length, 1, 1, "short"), "invalid expected hash is rejected");
    }

    private static object CheckRealIndexes(string project)
    {
        var resources = Path.Combine(project, "Assets", "Resources");
        var roster = Path.Combine(resources, Roster.Replace('/', Path.DirectorySeparatorChar));
        var indexes = Directory.GetFiles(roster, "runtime-index.asset", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Require(indexes.Length == ExpectedIds.Length, $"Expected 11 HD runtime indexes, found {indexes.Length}.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var guids = new HashSet<string>(StringComparer.Ordinal);
        var required = RequiredPaths();
        var results = new List<object>();
        var totalEntries = 0;
        long totalBytes = 0;
        var timestampDifferences = 0;
        foreach (var indexPath in indexes)
        {
            var text = File.ReadAllText(indexPath);
            var folder = Path.GetDirectoryName(indexPath)!;
            var name = Path.GetFileName(folder);
            Require(name.Length > 3 && name[2] == '_' && ids.Add(name.Substring(0, 2)), "Unexpected or duplicate character identity: " + name);
            var resourceFolder = Scalar(text, "resourceFolder");
            Require(resourceFolder == Roster + "/" + name, "Runtime index folder mismatch: " + name);
            foreach (var contract in new[] { ("manifestSha256", "manifest.json"), ("activationSha256", "appearance.json"), ("validationSha256", "validation.json") })
            {
                var expected = Scalar(text, contract.Item1);
                Require(IsHex(expected, 64) && HashFile(Path.Combine(folder, contract.Item2)) == expected,
                    $"{name}: stale {contract.Item1}");
            }
            var entries = ParseEntries(text);
            Require(entries.Count == 137 && entries.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal).SetEquals(required),
                name + ": index must contain portrait, 8 idle directions and 128 walk frames exactly once.");
            var differingTimes = 0;
            long bytes = 0;
            foreach (var entry in entries)
            {
                Require(IsHex(entry.Guid, 32) && guids.Add(entry.Guid) && IsHex(entry.Sha256, 64), name + "/" + entry.Path + ": invalid or duplicate identity.");
                Require(entry.Width > 0 && entry.Height > 0 && entry.Bytes > 24, name + "/" + entry.Path + ": invalid dimensions or byte count.");
                var file = Path.Combine(folder, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Require(Regex.IsMatch(File.ReadAllText(file + ".meta"), "(?m)^guid: " + Regex.Escape(entry.Guid) + "\\r?$"),
                    name + "/" + entry.Path + ": asset GUID mismatch.");
                Require(QdaoHdSourceFileVerifier.Matches(file, entry.Bytes, entry.Width, entry.Height, entry.Sha256),
                    name + "/" + entry.Path + ": production helper rejected tracked source identity.");
                if (File.GetLastWriteTimeUtc(file).Ticks != entry.LegacyWriteUtcTicks) differingTimes++;
                bytes += entry.Bytes;
            }
            timestampDifferences += differingTimes;
            totalEntries += entries.Count;
            totalBytes += bytes;
            results.Add(new { character = name, entries = entries.Count, verifiedImageBytes = bytes, legacyTimestampDifferences = differingTimes });
            Console.WriteLine($"Verified {name}: {entries.Count} source PNGs; {differingTimes} legacy timestamp differences accepted.");
        }
        Require(ids.SetEquals(ExpectedIds), "HD runtime index character roster changed unexpectedly.");
        Require(totalEntries == 1507, "Expected 11 x 137 production PNG identities.");
        Checks.Add("all 11 runtime indexes and 1507 real PNG identities verified with the production helper");
        return new { indexCount = indexes.Length, verifiedPngCount = totalEntries, verifiedImageBytes = totalBytes, legacyTimestampDifferences = timestampDifferences, characters = results };
    }

    // Unity's generated runtime-index assets use this fixed scalar/entry YAML shape; no YAML dependency is needed.
    private static List<SourceEntry> ParseEntries(string text)
    {
        var result = new List<SourceEntry>();
        Dictionary<string, string>? current = null;
        foreach (var line in text.Split('\n'))
        {
            var clean = line.TrimEnd('\r');
            if (clean.StartsWith("  - path: ", StringComparison.Ordinal))
            {
                if (current != null) result.Add(ToEntry(current));
                current = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = clean.Substring(10) };
            }
            else if (current != null && clean.StartsWith("    ", StringComparison.Ordinal))
            {
                var separator = clean.IndexOf(':', 4);
                Require(separator > 4, "Malformed runtime-index entry.");
                Require(current.TryAdd(clean.Substring(4, separator - 4), clean.Substring(separator + 1).Trim()), "Duplicate runtime-index entry field.");
            }
        }
        if (current != null) result.Add(ToEntry(current));
        return result;
    }

    private static SourceEntry ToEntry(Dictionary<string, string> entry) => new(
        entry["path"], entry["assetGuid"], entry["sha256"],
        int.Parse(entry["width"], CultureInfo.InvariantCulture), int.Parse(entry["height"], CultureInfo.InvariantCulture),
        long.Parse(entry["sourceBytes"], CultureInfo.InvariantCulture), long.Parse(entry["sourceWriteUtcTicks"], CultureInfo.InvariantCulture));

    private static HashSet<string> RequiredPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal) { "portrait.png" };
        foreach (var direction in Directions)
        {
            paths.Add("idle/" + direction + ".png");
            for (var frame = 1; frame <= 16; frame++) paths.Add($"walk/{direction}/{frame:00}.png");
        }
        return paths;
    }

    private static string Scalar(string text, string key)
    {
        var matches = Regex.Matches(text, "(?m)^  " + Regex.Escape(key) + ": *([^\\r\\n]*)\\r?$");
        Require(matches.Count == 1, "Missing or duplicate runtime-index field: " + key);
        return matches[0].Groups[1].Value.Trim();
    }

    private static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    private static void Check(bool condition, string name) { Require(condition, name); Checks.Add(name); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record SourceEntry(string Path, string Guid, string Sha256, int Width, int Height, long Bytes, long LegacyWriteUtcTicks);
}
