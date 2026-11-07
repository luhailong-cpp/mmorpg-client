using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UnityEngine
{
    // The production catalog/navigation and Unity's managed geometry remain real.
    // These facades replace only APIs which require a running Unity native process.
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static T FromJson<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
    }

    public sealed class TextAsset
    {
        public readonly string text;
        public TextAsset(string value) => text = value;
    }

    public sealed class Texture2D
    {
        public int width { get; }
        public int height { get; }
        public Texture2D(int w, int h) { width = w; height = h; }
    }

    public static class Resources
    {
        public static string Root;
        public static T Load<T>(string path) where T : class
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..") || path.Contains('\\'))
                throw new InvalidOperationException("Unsafe local test resource path.");
            var file = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            if (typeof(T) == typeof(TextAsset))
            {
                foreach (string extension in new[] { ".json", ".txt" })
                    if (File.Exists(file + extension)) return new TextAsset(File.ReadAllText(file + extension)) as T;
                return null;
            }
            if (typeof(T) == typeof(Texture2D))
            {
                if (!File.Exists(file + ".png")) return null;
                byte[] header = new byte[24];
                using var stream = File.OpenRead(file + ".png");
                stream.ReadExactly(header);
                if (!header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new InvalidOperationException("Resource is not a PNG.");
                int w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
                int h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
                return new Texture2D(w, h) as T;
            }
            throw new NotSupportedException(typeof(T).FullName);
        }
    }

    public static class Debug
    {
        public static readonly List<string> Errors = new();
        public static void LogError(object value) => Errors.Add(value?.ToString() ?? "");
    }
}
