using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MmorpgClient.Game.TacticalPuzzle
{
    /// <summary>
    /// 万象残局坊 V1 纯 C# 规则、展示文案与聊天卡片协议模型（零 UnityEngine 依赖，EditMode 可直接单测）。
    /// 所有数值上限与白名单严格对齐服务端 C++ tactical_puzzle_rules.h / Go internal/puzzle/spec.go / Java TacticalPuzzleRules。
    /// </summary>
    public static class TacticalPuzzleModels
    {
        public const uint DungeonConfigId = 314u;
        public const uint PlayModeId = 475u;
        public const uint BossTrialKind = 10u;

        public const int MaxPuzzlesPerAuthor = 10;
        public const int MinTitleRunes = 4;
        public const int MaxTitleRunes = 20;
        public const int MaxHintRunes = 40;
        public const uint ValidTagMask = 0x3Fu; // 6 bits
        public const int MaxTagsSelected = 2;

        public const uint MinRoundLimit = 1u;
        public const uint MaxRoundLimit = 6u;
        public const uint MinPlayerHpPct = 15u;
        public const uint MaxPlayerHpPct = 100u;
        public const uint MinPlayerMpPct = 20u;
        public const uint MaxPlayerMpPct = 100u;
        public const uint MaxPlayerShieldPct = 50u;
        public const uint MaxPlayerDebuffKind = 3u;

        public const int MinEnemies = 1;
        public const int MaxEnemies = 3;
        public const uint MinEnemyHpPct = 10u;
        public const uint MaxEnemyHpPct = 150u;
        public const uint MaxEnemyShieldPct = 60u;
        public const int MinEnemySpeedDeltaPct = -30;
        public const int MaxEnemySpeedDeltaPct = 30;
        public const uint MaxEnemyBuffKind = 4u;

        public const string ChatCardPrefix = "#PUZ:";
        public const string ChatCardSuffix = "#";

        public static readonly uint[] CharacterPresetIds = { 101u, 102u, 103u };
        public static readonly uint[] PetPresetIds = { 0u, 201u, 202u, 203u };
        public static readonly uint[] FormationIds = { 0u, 1u, 2u, 3u, 4u, 5u };
        public static readonly uint[] MonsterIds = { 311u, 312u, 313u, 314u, 315u, 316u };

        public static readonly string[] TagNames =
        {
            "破盾",
            "控场",
            "驱散",
            "反杀",
            "极限回合",
            "残血翻盘",
        };

        /// <summary>生成一套默认可玩且合法的残局盘面草稿。</summary>
        public static global::TacticalPuzzleSpec CreateDefaultSpec()
        {
            var spec = new global::TacticalPuzzleSpec
            {
                RoundLimit = 3u,
                CharacterPresetId = 101u,
                PetPresetId = 201u,
                FormationId = 1u,
                PlayerInitialHpPct = 65u,
                PlayerInitialMpPct = 80u,
                PlayerInitialShieldPct = 0u,
                PlayerInitialDebuffKind = 0u,
            };
            spec.Enemies.Add(new global::TacticalPuzzleEnemySpec
            {
                MonsterTableId = 311u,
                InitialHpPct = 80u,
                InitialShieldPct = 25u,
                SpeedDeltaPct = 0,
                InitialBuffKind = 1u,
            });
            return spec;
        }

        public static bool IsAllowedCharacterPreset(uint id) => id == 101u || id == 102u || id == 103u;
        public static bool IsAllowedPetPreset(uint id) => id == 0u || id == 201u || id == 202u || id == 203u;
        public static bool IsAllowedFormation(uint id) => id <= 5u;
        public static bool IsAllowedMonster(uint id) => id >= 311u && id <= 316u;

        public static int CountTextElements(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }
                count++;
            }
            return count;
        }

        public static bool HasForbiddenControlChar(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < 0x20 || c == 0x7F) return true;
            }
            return false;
        }

        public static int PopCountTags(uint tagMask)
        {
            uint m = tagMask & ValidTagMask;
            int n = 0;
            while (m != 0u)
            {
                n += (int)(m & 1u);
                m >>= 1;
            }
            return n;
        }

        /// <summary>客户端草稿发包前本地快速校验（与服务端权威校验口径完全一致）。</summary>
        public static bool ValidateDraft(
            string title,
            string hintText,
            uint tagMask,
            global::TacticalPuzzleSpec spec,
            out string error)
        {
            string trimmedTitle = (title ?? "").Trim();
            int titleLen = CountTextElements(trimmedTitle);
            if (titleLen < MinTitleRunes || titleLen > MaxTitleRunes)
            {
                error = $"题名须为 {MinTitleRunes}～{MaxTitleRunes} 字。";
                return false;
            }
            if (HasForbiddenControlChar(trimmedTitle))
            {
                error = "题名不能包含换行或控制字符。";
                return false;
            }

            string trimmedHint = (hintText ?? "").Trim();
            if (CountTextElements(trimmedHint) > MaxHintRunes)
            {
                error = $"提示语最多 {MaxHintRunes} 字。";
                return false;
            }
            if (HasForbiddenControlChar(trimmedHint))
            {
                error = "提示语不能包含换行或控制字符。";
                return false;
            }

            if ((tagMask & ~ValidTagMask) != 0u)
            {
                error = "存在无效的标签选项。";
                return false;
            }
            if (PopCountTags(tagMask) > MaxTagsSelected)
            {
                error = $"每道残局最多选择 {MaxTagsSelected} 个标签。";
                return false;
            }

            return ValidateSpec(spec, out error);
        }

        public static bool ValidateSpec(global::TacticalPuzzleSpec spec, out string error)
        {
            if (spec == null)
            {
                error = "残局盘面不能为空。";
                return false;
            }
            if (spec.RoundLimit < MinRoundLimit || spec.RoundLimit > MaxRoundLimit)
            {
                error = $"回合上限须在 {MinRoundLimit}～{MaxRoundLimit} 回合之间。";
                return false;
            }
            if (!IsAllowedCharacterPreset(spec.CharacterPresetId))
            {
                error = "请选择有效的我方人物预设（破阵 / 控场 / 守心）。";
                return false;
            }
            if (!IsAllowedPetPreset(spec.PetPresetId))
            {
                error = "请选择有效的我方灵宠预设。";
                return false;
            }
            if (!IsAllowedFormation(spec.FormationId))
            {
                error = "请选择有效的阵法。";
                return false;
            }
            if (spec.PlayerInitialHpPct < MinPlayerHpPct || spec.PlayerInitialHpPct > MaxPlayerHpPct)
            {
                error = $"我方初始气血须在 {MinPlayerHpPct}%～{MaxPlayerHpPct}% 之间。";
                return false;
            }
            if (spec.PlayerInitialMpPct < MinPlayerMpPct || spec.PlayerInitialMpPct > MaxPlayerMpPct)
            {
                error = $"我方初始法力须在 {MinPlayerMpPct}%～{MaxPlayerMpPct}% 之间。";
                return false;
            }
            if (spec.PlayerInitialShieldPct > MaxPlayerShieldPct)
            {
                error = $"我方初始护盾不能超过 {MaxPlayerShieldPct}%。";
                return false;
            }
            if (spec.PlayerInitialDebuffKind > MaxPlayerDebuffKind)
            {
                error = "我方开场不利状态无效。";
                return false;
            }
            if (spec.Enemies == null || spec.Enemies.Count < MinEnemies || spec.Enemies.Count > MaxEnemies)
            {
                error = $"敌方人数须在 {MinEnemies}～{MaxEnemies} 名之间。";
                return false;
            }
            for (int i = 0; i < spec.Enemies.Count; i++)
            {
                var enemy = spec.Enemies[i];
                if (enemy == null || !IsAllowedMonster(enemy.MonsterTableId))
                {
                    error = $"第 {i + 1} 名敌方模板无效。";
                    return false;
                }
                if (enemy.InitialHpPct < MinEnemyHpPct || enemy.InitialHpPct > MaxEnemyHpPct)
                {
                    error = $"第 {i + 1} 名敌方血量须在 {MinEnemyHpPct}%～{MaxEnemyHpPct}% 之间。";
                    return false;
                }
                if (enemy.InitialShieldPct > MaxEnemyShieldPct)
                {
                    error = $"第 {i + 1} 名敌方护盾不能超过 {MaxEnemyShieldPct}%。";
                    return false;
                }
                if (enemy.SpeedDeltaPct < MinEnemySpeedDeltaPct || enemy.SpeedDeltaPct > MaxEnemySpeedDeltaPct)
                {
                    error = $"第 {i + 1} 名敌方速度修正须在 {MinEnemySpeedDeltaPct}%～+{MaxEnemySpeedDeltaPct}% 之间。";
                    return false;
                }
                if (enemy.InitialBuffKind > MaxEnemyBuffKind)
                {
                    error = $"第 {i + 1} 名敌方开场增益无效。";
                    return false;
                }
            }
            error = "";
            return true;
        }

        // ── 展示名称与格式化 ──────────────────────────────────────

        public static string TabName(Match.TacticalPuzzleListTab tab)
        {
            return tab switch
            {
                Match.TacticalPuzzleListTab.Hot => "精选热门",
                Match.TacticalPuzzleListTab.Newest => "最新奇局",
                Match.TacticalPuzzleListTab.Hardest => "烧脑难关",
                Match.TacticalPuzzleListTab.MyCreated => "我的创作",
                Match.TacticalPuzzleListTab.MyCleared => "我已破局",
                _ => "精选热门",
            };
        }

        public static string CharacterPresetName(uint presetId)
        {
            return presetId switch
            {
                101u => "破阵金锋",
                102u => "控场青木",
                103u => "守心玄水",
                _ => $"人物#{presetId}",
            };
        }

        public static string PetPresetName(uint presetId)
        {
            return presetId switch
            {
                0u => "单人无宠",
                201u => "破甲灵兽",
                202u => "灵咒玄狐",
                203u => "护主瑞龟",
                _ => $"灵宠#{presetId}",
            };
        }

        public static string FormationName(uint formationId)
        {
            return formationId switch
            {
                0u => "无阵",
                1u => "天覆阵",
                2u => "地载阵",
                3u => "风扬阵",
                4u => "云垂阵",
                5u => "虎翼阵",
                _ => $"阵法#{formationId}",
            };
        }

        public static string PlayerDebuffName(uint kind)
        {
            return kind switch
            {
                0u => "无不利状态",
                1u => "卸力（物攻降）",
                2u => "破甲（防御降）",
                3u => "玄毒（回合损血）",
                _ => $"状态#{kind}",
            };
        }

        public static string MonsterName(uint monsterId)
        {
            return monsterId switch
            {
                311u => "残局演武卫",
                312u => "破阵甲士",
                313u => "玄盾守将",
                314u => "炽炎术傀",
                315u => "玄水灵傀",
                316u => "疾风剑傀",
                _ => $"演武敌#{monsterId}",
            };
        }

        public static string EnemyBuffName(uint kind)
        {
            return kind switch
            {
                0u => "无增益",
                1u => "固守（双防升）",
                2u => "疾风（先手加速）",
                3u => "狂怒（攻伤升）",
                4u => "反震（物理反伤）",
                _ => $"增益#{kind}",
            };
        }

        public static string FormatTagSummary(uint tagMask, string emptyFallback = "常规残局")
        {
            uint m = tagMask & ValidTagMask;
            if (m == 0u) return emptyFallback;
            var sb = new StringBuilder();
            for (int i = 0; i < TagNames.Length; i++)
            {
                if ((m & (1u << i)) == 0u) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(TagNames[i]);
            }
            return sb.Length > 0 ? sb.ToString() : emptyFallback;
        }

        public static string FormatClearRate(uint attempts, uint clears)
        {
            if (attempts == 0u) return "待首破";
            uint clampedClears = Math.Min(clears, attempts);
            int pct = (int)Math.Round(clampedClears * 100.0 / attempts);
            return $"破局率 {pct}%（{clampedClears}/{attempts}）";
        }

        public static string DeriveHonorTitle(uint tier, string serverTitle = null)
        {
            if (!string.IsNullOrWhiteSpace(serverTitle)) return serverTitle.Trim();
            return tier switch
            {
                >= 3u => "万象棋圣",
                2u => "奇局宗师",
                1u => "破局国手",
                _ => "残局初窥",
            };
        }

        public static string DescribeSpecBrief(global::TacticalPuzzleSpec spec)
        {
            if (spec == null) return "标准公平模板";
            string pet = spec.PetPresetId == 0u ? "无宠" : PetPresetName(spec.PetPresetId);
            int enemies = spec.Enemies?.Count ?? 0;
            return $"{spec.RoundLimit}回合 · {CharacterPresetName(spec.CharacterPresetId)}/{pet} · {FormationName(spec.FormationId)} · {enemies}敌";
        }

        // ── 世界聊天残局约战卡 #PUZ:<puzzle_id># ─────────────────

        public static string FormatChatShareDraft(Match.TacticalPuzzleSummaryView puzzle)
        {
            if (puzzle == null || puzzle.PuzzleId == 0ul) return "";
            string title = string.IsNullOrWhiteSpace(puzzle.Title) ? $"残局#{puzzle.PuzzleId}" : puzzle.Title.Trim();
            uint rounds = puzzle.Spec?.RoundLimit ?? 0u;
            string roundPart = rounds > 0u ? $"{rounds}回合内破局" : "公平残局";
            string bestPart = puzzle.GlobalBestRounds > 0u ? $" · 最快{puzzle.GlobalBestRounds}回合" : "";
            return $"【万象残局】「{title}」（{roundPart}{bestPart}），来试试能破否？{ChatCardPrefix}{puzzle.PuzzleId}{ChatCardSuffix}";
        }

        public static string FormatChatShareDraftFromContext(global::TacticalPuzzleBattleContext ctx, uint clearedRounds)
        {
            if (ctx == null || ctx.PuzzleId == 0ul) return "";
            string title = string.IsNullOrWhiteSpace(ctx.PuzzleTitle) ? $"残局#{ctx.PuzzleId}" : ctx.PuzzleTitle.Trim();
            string roundPart = clearedRounds > 0u ? $"我用{clearedRounds}回合破局" : "公平残局";
            return $"【万象残局】「{title}」{roundPart}，道友敢来破阵吗？{ChatCardPrefix}{ctx.PuzzleId}{ChatCardSuffix}";
        }

        public static bool TryParsePuzzleCardId(string messageText, out ulong puzzleId)
        {
            puzzleId = 0ul;
            if (string.IsNullOrEmpty(messageText)) return false;
            int start = messageText.IndexOf(ChatCardPrefix, StringComparison.Ordinal);
            if (start < 0) return false;
            int numStart = start + ChatCardPrefix.Length;
            int end = messageText.IndexOf('#', numStart);
            if (end <= numStart) return false;
            string digits = messageText.Substring(numStart, end - numStart);
            return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out puzzleId) && puzzleId != 0ul;
        }

        public static string StripPuzzleCardTag(string messageText)
        {
            if (string.IsNullOrEmpty(messageText)) return "";
            int start = messageText.IndexOf(ChatCardPrefix, StringComparison.Ordinal);
            if (start < 0) return messageText.Trim();
            int end = messageText.IndexOf('#', start + ChatCardPrefix.Length);
            if (end < 0) return messageText.Trim();
            string before = messageText.Substring(0, start).TrimEnd();
            string after = end + 1 < messageText.Length ? messageText.Substring(end + 1).TrimStart() : "";
            if (string.IsNullOrEmpty(before)) return after;
            if (string.IsNullOrEmpty(after)) return before;
            return before + " " + after;
        }

        // ── Tip 错误码映射（16064..16072）────────────────────────

        public static string DescribeTip(uint tipId)
        {
            return tipId switch
            {
                16064u => "该残局不存在或已被作者删除。",
                16065u => "仅出题作者可编辑或删除该残局。",
                16066u => $"每位道友最多保存 {MaxPuzzlesPerAuthor} 道残局，请先删去旧题。",
                16067u => "残局盘面配置超出允许范围，请检查后重试。",
                16068u => "题名或提示语未通过内容安全审核，请修改后重试。",
                16069u => "该残局尚未完成作者自证发布，暂不可公开挑战。",
                16070u => "不能给自己的残局点赞。",
                16071u => "请先亲手破局通关后，再为这道好题点赞。",
                16072u => "该残局的标准解法录像暂不可用。",
                16004u => "服务繁忙，请稍后重试。",
                1008u => "操作太频繁，请稍候再试。",
                _ => $"操作未能完成（错误码 {tipId}）。",
            };
        }
    }
}
