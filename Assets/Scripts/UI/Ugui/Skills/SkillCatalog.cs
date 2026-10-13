using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>只读取 Resources 元数据，不授予技能，不引用网络、等级或钱包业务。</summary>
    public static class SkillCatalog
    {
        [Serializable] private sealed class CatalogFile { public SkillDefinition[] skills; }
        private static readonly List<SkillDefinition> Entries = new();
        private static readonly Dictionary<uint, SkillDefinition> Index = new();
        private static bool _loaded;
        public static IReadOnlyList<SkillDefinition> All { get { EnsureLoaded(); return Entries; } }
        public static SkillDefinition Find(uint id)
        {
            EnsureLoaded();
            return Index.TryGetValue(id, out var entry) ? entry : null;
        }
        public static string Name(uint id) => Find(id)?.name ?? $"技能{id}";
        public static IReadOnlyList<SkillDefinition> ForClass(uint classId)
        {
            EnsureLoaded();
            return Entries.FindAll(entry => entry.classId == classId);
        }
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var asset = Resources.Load<TextAsset>("Skills/catalog");
            if (asset == null) { Debug.LogWarning("技能图鉴目录尚未导入。"); return; }
            try
            {
                var data = JsonUtility.FromJson<CatalogFile>(asset.text);
                if (data?.skills == null) throw new FormatException("缺少技能目录");
                foreach (var entry in data.skills)
                {
                    if (entry == null || entry.id == 0 || string.IsNullOrWhiteSpace(entry.name) || Index.ContainsKey(entry.id))
                        continue;
                    Index.Add(entry.id, entry);
                    Entries.Add(entry);
                }
                Entries.Sort((left, right) => left.id.CompareTo(right.id));
            }
            catch (Exception error) { Debug.LogWarning("技能图鉴读取失败：" + error.Message); }
        }
    }
}
