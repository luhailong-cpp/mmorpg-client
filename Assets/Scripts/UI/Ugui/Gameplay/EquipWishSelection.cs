using System;
using System.Collections.Generic;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed class EquipWishOption
    {
        public uint Id { get; }
        public string Label { get; }
        public EquipWishOption(uint id, string label) { Id = id; Label = label ?? ""; }
    }

    /// <summary>Local draft only. Options must be supplied by the caller; current item affixes are not a pool.</summary>
    public sealed class EquipWishSelection
    {
        private readonly List<EquipWishOption> _options = new List<EquipWishOption>();
        private readonly HashSet<uint> _allowed = new HashSet<uint>();
        private readonly HashSet<uint> _selected = new HashSet<uint>();
        public IReadOnlyList<EquipWishOption> Options => _options.AsReadOnly();
        public int Count => _selected.Count;
        public bool CanConfirm => _options.Count > 0;

        public void Reset(IEnumerable<EquipWishOption> options, IEnumerable<uint> selected = null)
        {
            _options.Clear(); _allowed.Clear(); _selected.Clear();
            if (options != null)
                foreach (var option in options)
                    if (option != null && !string.IsNullOrWhiteSpace(option.Label) && _allowed.Add(option.Id))
                        _options.Add(option);
            if (selected != null)
                foreach (uint id in selected)
                    if (_allowed.Contains(id)) _selected.Add(id);
        }

        public bool IsSelected(uint id) => _selected.Contains(id);
        public bool Toggle(uint id)
        {
            if (!_allowed.Contains(id)) return false;
            if (!_selected.Add(id)) _selected.Remove(id);
            return true;
        }
        public void Clear() => _selected.Clear();

        /// <summary>Copy in source order, independent from both the original input and future edits.</summary>
        public uint[] Snapshot()
        {
            var values = new List<uint>();
            foreach (var option in _options) if (_selected.Contains(option.Id)) values.Add(option.Id);
            return values.ToArray();
        }
    }
}
