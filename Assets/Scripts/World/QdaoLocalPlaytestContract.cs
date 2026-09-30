using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>本次用户指定候选的开发版入口；不改变正式美术批准合同。</summary>
    public static class QdaoLocalPlaytestContract
    {
        public const string Mode = "local-playtest-20260929-v1";
        public const string PendingReview = "pending_dynamic_review";

        public static bool Allows(string id, int version, string activationJson, byte[] manifestBytes)
        {
#if UNITY_EDITOR
            const bool developmentBuild = true;
#else
            var developmentBuild = Debug.isDebugBuild;
#endif
            return MatchesPinnedDelivery(id, version, activationJson, manifestBytes, developmentBuild);
        }

        // 明确传入构建类型便于覆盖发行版拒收；运行选择只能通过上面的实际构建类型入口。
        public static bool MatchesPinnedDelivery(string id, int version, string activationJson,
            byte[] manifestBytes, bool developmentBuild)
        {
            if (!developmentBuild || version != 14 || string.IsNullOrEmpty(activationJson) || manifestBytes == null)
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
