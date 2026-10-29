using System;
using System.Security.Cryptography;
using System.Text;

namespace MmorpgClient.World
{
    /// <summary>保留历史交付名称；用户指定的两份完整候选可用于所有构建，仍不声明美术验收。</summary>
    public static class QdaoLocalPlaytestContract
    {
        public const string Mode = "local-playtest-20260929-v1";
        public const string PendingReview = "pending_dynamic_review";

        public static bool Allows(string id, int version, string activationJson, byte[] manifestBytes)
            => MatchesPinnedDelivery(id, version, activationJson, manifestBytes);

        // 发行版与编辑器使用同一锁定交付，不能因为构建类型不同让已保存的07/15人物消失。
        public static bool MatchesPinnedDelivery(string id, int version, string activationJson,
            byte[] manifestBytes)
        {
            if (version != 14 || string.IsNullOrEmpty(activationJson) || manifestBytes == null)
                return false;
            string manifestHash;
            string activationHash;
            switch (id)
            {
                case "07_moon_shadow_assassin_girl":
                    manifestHash = "8fe0405ab2d91a05bb7de9b47ce70148782245ad5b77b34d901d9f3695e40ac4";
                    activationHash = "d36b6d673d3643e7050dcc71ab59c3d6c95d2b0a87f1a245e61b3e48b3f13b86";
                    break;
                case "15_water_dragon_scholar_boy":
                    manifestHash = "d4e604aa0d9f65e2b21415a315559d255b1eeef12e4b175a1a2f232f8760762b";
                    activationHash = "880c7b68549b18faa9b980d8de660ff4c10dfc9f8e0a9c8c33dea66911ebeb60";
                    break;
                default:
                    return false;
            }
            // 这两份固定文件包含当前来源审查/交付清单SHA和pending状态；任何替换都需重新核验。
            return Hash(manifestBytes) == manifestHash && Hash(Encoding.UTF8.GetBytes(activationJson)) == activationHash;
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
