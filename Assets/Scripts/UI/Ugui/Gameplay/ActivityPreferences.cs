using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>仅保存本机角色的展示偏好；不保存活动进度、资格或领取状态。</summary>
    public sealed class ActivityPreferences
    {
        [Serializable]
        public sealed class Favorite
        {
            public uint activityId;
            public string definitionKey;
            public string displayName;
        }

        [Serializable]
        private sealed class Document
        {
            public int category = -1;
            public int reward;
            public List<Favorite> favorites = new();
        }

        private const int MaximumFavorites = 256;
        private readonly Func<string, string> _read;
        private readonly Action<string, string> _write;
        private Document _document = new();
        private string _storageKey;
        public ulong PlayerId { get; private set; }
        public bool CanSave => PlayerId != 0;
        public int Category => _document.category;
        public int Reward => _document.reward;
        public IReadOnlyList<Favorite> Favorites => _document.favorites;
        public string Error { get; private set; } = string.Empty;

        public ActivityPreferences(Func<string, string> read, Action<string, string> write)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        public static ActivityPreferences CreateLocal() => new(
            key => PlayerPrefs.GetString(key, string.Empty),
            (key, value) => { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); });

        public void SetPlayer(ulong playerId)
        {
            if (PlayerId == playerId) return;
            PlayerId = playerId;
            _document = new Document(); Error = string.Empty;
            _storageKey = playerId == 0 ? null : "wuxingqitan.activities.preferences.v1." + playerId.ToString(CultureInfo.InvariantCulture);
            if (_storageKey == null) return;
            try
            {
                string json = _read(_storageKey);
                if (string.IsNullOrEmpty(json)) return;
                var saved = JsonUtility.FromJson<Document>(json);
                if (saved == null) return;
                saved.category = Math.Max(-1, Math.Min(5, saved.category));
                saved.reward = Math.Max(0, Math.Min(4, saved.reward));
                saved.favorites = (saved.favorites ?? new List<Favorite>())
                    .Where(f => f != null && f.activityId != 0 && !string.IsNullOrWhiteSpace(f.definitionKey))
                    .GroupBy(f => (f.activityId, f.definitionKey), EqualityComparer<(uint, string)>.Default)
                    .Select(g => g.First()).Take(MaximumFavorites).ToList();
                _document = saved;
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            { Error = "收藏偏好读取失败，可重新收藏"; }
        }

        public bool Contains(uint activityId, string definitionKey) => !string.IsNullOrEmpty(definitionKey) &&
            _document.favorites.Any(f => f.activityId == activityId && f.definitionKey == definitionKey);

        public bool Toggle(uint activityId, string definitionKey, string displayName)
        {
            if (!CanSave || activityId == 0 || string.IsNullOrWhiteSpace(definitionKey)) return false;
            var existing = _document.favorites.FirstOrDefault(f => f.activityId == activityId && f.definitionKey == definitionKey);
            if (existing != null) _document.favorites.Remove(existing);
            else
            {
                if (_document.favorites.Count >= MaximumFavorites) { Error = "收藏已满，请先移除失效收藏"; return false; }
                _document.favorites.Add(new Favorite { activityId = activityId, definitionKey = definitionKey, displayName = displayName ?? string.Empty });
            }
            return Save();
        }

        public void SetFilters(int category, int reward)
        {
            _document.category = Math.Max(-1, Math.Min(5, category));
            _document.reward = Math.Max(0, Math.Min(4, reward));
            if (CanSave) Save();
        }

        private bool Save()
        {
            try { _write(_storageKey, JsonUtility.ToJson(_document)); Error = string.Empty; return true; }
            catch (Exception exception) when (IsStorageFailure(exception))
            { Error = "收藏偏好保存失败，本次更改仅在当前窗口有效"; return false; }
        }

        private static bool IsStorageFailure(Exception exception) => exception is ArgumentException ||
            exception is InvalidOperationException || exception is System.IO.IOException || exception is UnauthorizedAccessException ||
            exception is System.Security.SecurityException || exception is PlayerPrefsException;
    }
}
