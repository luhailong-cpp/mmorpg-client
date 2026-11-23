using System;
using UnityEngine;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>
    /// 基于 Unity <see cref="PlayerPrefs"/> 的设备本机最佳纪录存储。
    /// 键属于当前设备当前安装，不关联账号/角色/区服；仅同步读写单条版本化次数文本，
    /// 不保存整副牌与任何敏感凭据，兼容 WebGL2 单线程与 IL2CPP。
    /// </summary>
    public sealed class PlayerPrefsPetFlipBestRecordStore : IPetFlipBestRecordStore
    {
        public const string StorageKey = "mmorpg.minigame.pet_flip.best_flips.v1";

        private readonly string _storageKey;

        public PlayerPrefsPetFlipBestRecordStore(string storageKey = StorageKey)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
                throw new ArgumentException("存储键不能为空。", nameof(storageKey));

            _storageKey = storageKey;
        }

        public bool TryReadRaw(out string rawValue)
        {
            try
            {
                rawValue = PlayerPrefs.GetString(_storageKey, string.Empty);
                return true;
            }
            catch (Exception)
            {
                rawValue = null;
                return false;
            }
        }

        public bool TryWriteRaw(string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue))
                return false;

            try
            {
                PlayerPrefs.SetString(_storageKey, rawValue);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
