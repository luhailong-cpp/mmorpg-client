using System;
using Ui = MmorpgClient.UI.Ugui.Skills;

namespace MmorpgClient.Game.Skills
{
    /// <summary>
    /// 技能书协议 → 技能 UI 展示模型的纯映射(服务端 docs/design/class-skills.md §4)。
    ///
    /// 只做翻译,不推算:等级、上限、价格、能否修习、余额全部原样取服务端 ListSkillsResponse。
    /// 本地图鉴(<see cref="Ui.SkillCatalog"/>)只补服务端不下发的纯展示字段(职业名、图鉴图片路径),
    /// 不授予技能、不改任何数值。不引用 UnityEngine 之外的运行时状态,EditMode 可直接喂协议对象测。
    /// </summary>
    public static class SkillBookMapper
    {
        public const string CharacterTitle = "角色技能";

        /// <summary>服务端 target_team(1 敌方 / 2 友方 / 3 自身)→ UI 元数据写法;认不出的给空串(UI 显示"—")。</summary>
        public static string TargetTeamName(uint targetTeam) => targetTeam switch
        {
            1 => "enemy",
            2 => "ally",
            3 => "self",
            _ => string.Empty,
        };

        /// <summary>
        /// icon_key 统一成 Unity Resources 相对键:去掉 Assets/Resources/ 前缀与扩展名、反斜杠换正斜杠;
        /// 服务端没给就用约定的 UI/Skills/{id}。
        /// </summary>
        public static string NormalizeIconKey(string iconKey, uint skillId)
        {
            if (string.IsNullOrWhiteSpace(iconKey)) return "UI/Skills/" + skillId;
            string key = iconKey.Trim().Replace('\\', '/');
            const string resourcesPrefix = "Assets/Resources/";
            if (key.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase)) key = key.Substring(resourcesPrefix.Length);
            int slash = key.LastIndexOf('/');
            int dot = key.LastIndexOf('.');
            if (dot > slash) key = key.Substring(0, dot);
            return key;
        }

        /// <summary>
        /// 整份技能书 → 一次性替换用的快照。只收当前职业的目录行(服务端本来就只发当前职业),按 id 升序。
        /// presentation 取本地图鉴的展示元数据;传 null 用 <see cref="Ui.SkillCatalog.Find"/>。
        /// </summary>
        public static Ui.SkillBookSnapshot ToSnapshot(ListSkillsResponse response,
            Func<uint, Ui.SkillDefinition> presentation = null)
        {
            if (response == null) return null;
            presentation ??= id => Ui.SkillCatalog.Find(id);
            var snapshot = new Ui.SkillBookSnapshot
            {
                ClassId = response.ClassId,
                PlayerLevel = response.PlayerLevel,
                CurrencyBalance = response.CurrencyBalance,
                Title = CharacterTitle,
            };
            foreach (var row in response.Catalog)
            {
                if (row == null || row.SkillTableId == 0 || row.ClassId != response.ClassId) continue;
                var local = presentation(row.SkillTableId);
                snapshot.Entries.Add(new Ui.SkillEntryState
                {
                    Id = row.SkillTableId,
                    Rank = row.Rank,
                    RankLimit = row.RankLimit,
                    NextRankCost = row.NextRankCost,
                    LockedReason = row.UpgradeBlockTipId == 0 ? null : DescribeTip(row.UpgradeBlockTipId),
                    Definition = new Ui.SkillDefinition
                    {
                        id = row.SkillTableId,
                        classId = row.ClassId,
                        className = local?.className,
                        name = row.Name,
                        type = row.Passive ? "passive" : "active",
                        category = row.Category,
                        unlockLevel = row.UnlockLevel,
                        maxRank = row.MaxRank,
                        targetTeam = TargetTeamName(row.TargetTeam),
                        maxTargets = row.MaxTargets,
                        manaPercent = row.ManaPercent,
                        cooldownRounds = row.CooldownRounds,
                        durationRounds = row.DurationRounds,
                        description = row.Description,
                        iconPath = local?.iconPath,
                        iconResource = NormalizeIconKey(row.IconKey, row.SkillTableId),
                    },
                });
            }
            snapshot.Entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            return snapshot;
        }

        /// <summary>
        /// 技能书 / 修习的 tip 文案。按生成的 skill_error / common_error 枚举映射(文案逐字镜像 data/tip/Tip.xlsx,
        /// 表改了要同步这里),不写数字;未收录的退回全仓既有口径 "tip=N"。
        /// </summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)skill_error.KSkillNotInClass => "该技能不属于当前职业",
            (uint)skill_error.KSkillRankChanged => "技能等级已变化,请刷新后再试",
            (uint)skill_error.KSkillUpgradeCountInvalid => "修习次数只能是 1 次或 10 次",
            (uint)skill_error.KSkillLevelNotEnough => "角色等级不足,暂不能继续修习",
            (uint)skill_error.KSkillMaxRankReached => "技能已修至最高重",
            (uint)skill_error.KSkillGoldNotEnough => "银两不足",
            (uint)skill_error.KSkillUpgradeBusy => "战斗中或角色迁移中不能修习技能",
            (uint)common_error.KInvalidTableId => "技能不存在",
            (uint)common_error.KServiceUnavailable => "技能服务暂不可用,请稍后再试",
            (uint)common_error.KEntityIsNull => "角色尚未就绪",
            // 扣银两失败透传的资产段码(冻结 / 余额被并发花掉)客户端没有生成枚举,按全仓口径显示编号,不手抄数字
            _ => $"tip={tipId}",
        };

        public static string DescribeTip(string what, TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return what;
            return $"{what}:{DescribeTip(tip.Id)}";
        }

        /// <summary>修习结果的一句话:实际修成几重;少于请求次数时带上停止原因。</summary>
        public static string DescribeUpgrade(string skillName, uint upgradedCount, uint stopTipId)
        {
            string name = string.IsNullOrEmpty(skillName) ? "技能" : $"【{skillName}】";
            string done = $"{name}修习成功,提升 {upgradedCount} 重";
            return stopTipId == 0 ? done : $"{done}(已停止:{DescribeTip(stopTipId)})";
        }
    }
}
