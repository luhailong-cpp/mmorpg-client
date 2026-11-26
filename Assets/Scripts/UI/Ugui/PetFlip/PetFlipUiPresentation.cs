using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.PetFlip;

namespace MmorpgClient.UI.Ugui.PetFlip
{
    /// <summary>灵宠翻牌主界面当前应呈现的子页面。</summary>
    public enum PetFlipUiPage
    {
        /// <summary>牌面资源正在载入，禁止重复开始。</summary>
        Loading = 0,
        /// <summary>任一牌面/名称缺失或复用同一占位图，阻断开局并允许重试。</summary>
        LoadFailed = 1,
        /// <summary>无进行中内存盘时的入口说明页。</summary>
        Entry = 2,
        /// <summary>12 张牌对局页（含首翻、配中、失配停留与断线冻结态）。</summary>
        Board = 3,
        /// <summary>配齐 6 对后的完成结果页。</summary>
        Complete = 4
    }

    /// <summary>UI 资源加载与完整性校验状态。</summary>
    public enum PetFlipAssetLoadState
    {
        NotLoaded = 0,
        Loading = 1,
        Ready = 2,
        Failed = 3
    }

    /// <summary>单张灵宠牌面身份绑定（纯数据，不依赖 UnityEngine，便于离线单测）。</summary>
    public readonly struct PetFlipFaceDescriptor
    {
        public SpiritPetCardPattern Pattern { get; }
        public string PetCatalogId { get; }
        public string DisplayName { get; }
        public object PortraitIdentity { get; }

        public PetFlipFaceDescriptor(
            SpiritPetCardPattern pattern,
            string petCatalogId,
            string displayName,
            object portraitIdentity)
        {
            Pattern = pattern;
            PetCatalogId = petCatalogId;
            DisplayName = displayName;
            PortraitIdentity = portraitIdentity;
        }
    }

    /// <summary>牌位像素矩形（对应横屏 1922×818 或竖屏 1024×1536 设计坐标，左上原点 y 向下）。</summary>
    public readonly struct PetFlipPixelRect
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public PetFlipPixelRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// 灵宠翻牌纯 C# 展示推导层（零 UnityEngine 依赖，脱离场景可高速单测）。
    /// 所有文案、页面路由、6 种灵宠映射校验与横竖屏 12 槽几何均以此类为唯一真相点。
    /// </summary>
    public static class PetFlipUiPresentation
    {
        public const float LandscapeWidth = 1922f;
        public const float LandscapeHeight = 818f;
        public const float PortraitWidth = 1024f;
        public const float PortraitHeight = 1536f;

        public const string TitleText = "灵宠翻牌";
        public const string EntryButtonText = "灵宠翻牌";
        public const string EntryPurposeText = "休闲翻牌";
        public const string EntrySubtitleText = "轻松翻一翻";
        public const string EntryDescriptionText = "每次翻两张，找出六对相同灵宠。翻错也可以继续。";
        public const string EntryHelpText = "本机最佳仅保存在此设备，清除本地数据后会重置。";
        public const string EntryCountRuleText = "翻完两张算一次";
        public const string NoRecordText = "暂无纪录";
        public const string StartButtonText = "开始翻牌";
        public const string BackButtonText = "返回历练";
        public const string PlayAgainButtonText = "再玩一局";
        public const string RetryButtonText = "重试";
        public const string BoardNoLimitText = "本局不限时";
        public const string MismatchHintText = "点任一未配对的牌，继续翻。";
        public const string BoardDefaultHintText = "记住翻开的牌面";
        public const string PortraitDefaultHintText = "翻开两张相同灵宠即可配对";
        public const string DisconnectedHintText = "连接中断，等待会话恢复…";
        public const string LoadingText = "正在载入灵宠牌面…";
        public const string LoadFailedTitleText = "灵宠牌面未能载入";
        public const string LoadFailedSubtext = "请重试，或先返回历练";
        public const string UnsavedRecordNoticeText = "本机纪录未保存";
        public const string PerfectRunBadgeText = "一气呵成";
        public const string SwitchConfigButtonText = "切换难度 / 玩法";
        public const string SpiritEyeArmedHintText = "灵目就绪：点一张背面暗牌免费窥看。";
        public const string SpiritEyePeekingHintText = "灵目已窥：点任一未配对牌盖回并开一手。";

        private static readonly string[] PetMatchFlavorQuotes =
        {
            "烛翎振羽：灯火照处，灵犀相通！",
            "霜团貂甩尾：毛茸茸的一对，找得好准！",
            "桂灯童提灯：桂香引路，再接再厉！",
            "竹风狸轻跃：穿竹过风，眼明手快！",
            "玄潮龟点头：稳中求胜，水到渠成！",
            "霞角鹿踏云：霞光映双影，好记性！",
            "绛铃清响：金铃一振，双影同心！",
            "岚铎仙拂袖：山岚散尽，灵眸如炬！"
        };

        /// <summary>返回指定灵宠配对成功时的一句道家 Q 版趣味小语。</summary>
        public static string GetPetMatchFlavorText(SpiritPetCardPattern pattern)
        {
            int index = (int)pattern;
            return index >= 0 && index < PetMatchFlavorQuotes.Length
                ? PetMatchFlavorQuotes[index]
                : string.Empty;
        }

        /// <summary>返回连续配对 2 次及以上的称赞短语；不足 2 连返回空串。</summary>
        public static string FormatStreakBadge(int currentStreak)
        {
            if (currentStreak < 2)
                return string.Empty;

            return currentStreak switch
            {
                2 => "二连妙手！",
                3 => "三连贯珠！",
                4 => "四连灵犀！",
                5 => "五连天成！",
                6 => "六连一气呵成！",
                7 => "七连登峰！",
                _ => "八连一气呵成！"
            };
        }

        /// <summary>
        /// 逻辑枚举 <see cref="SpiritPetCardPattern"/> (0..7) 与客户端 <c>QdaoPetCatalog</c> 身份的固定映射，
        /// A..F 对齐 <c>designs/pet-memory-ui-v1/README.md</c>，G..H 为进阶 16 张模式追加的两只现役灵宠；
        /// 不关联玩家真实养成宝宝实例。
        /// </summary>
        public static readonly string[] PatternToPetCatalogId =
        {
            "01-zhuling",      // A -> 烛翎
            "03-shuangtuan",   // B -> 霜团貂
            "04-guideng",      // C -> 桂灯童
            "08-zhufengli",    // D -> 竹风狸
            "10-xuanchaogui",  // E -> 玄潮龟
            "11-xiajiaolu",    // F -> 霞角鹿
            "02-jiangling",    // G -> 绛铃（进阶 16 张）
            "15-landuoxian"    // H -> 岚铎仙（进阶 16 张）
        };

        public static string GetPetCatalogId(SpiritPetCardPattern pattern)
        {
            int index = (int)pattern;
            return index >= 0 && index < PatternToPetCatalogId.Length
                ? PatternToPetCatalogId[index]
                : string.Empty;
        }

        public static string FormatDifficultyLabel(PetFlipDifficulty difficulty)
            => difficulty == PetFlipDifficulty.Advanced16 ? "进阶 · 16张" : "轻松 · 12张";

        public static string FormatDifficultyPill(PetFlipDifficulty difficulty)
            => difficulty == PetFlipDifficulty.Advanced16 ? "八对灵宠" : "六对灵宠";

        public static string FormatModeLabel(PetFlipGameMode mode)
        {
            return mode switch
            {
                PetFlipGameMode.DailyChallenge => "今日灵签",
                PetFlipGameMode.SpiritEye => "灵目趣味",
                _ => "经典自由"
            };
        }

        public static string FormatDailyDateLabel(int dateKey)
        {
            if (dateKey < 19700101)
                return "今日灵签";

            int month = (dateKey / 100) % 100;
            int day = dateKey % 100;
            if (month < 1 || month > 12 || day < 1 || day > 31)
                return "今日灵签";

            return $"{month.ToString(CultureInfo.InvariantCulture)}月{day.ToString(CultureInfo.InvariantCulture)}日灵签";
        }

        public static string FormatBestTitle(PetFlipGameMode mode)
            => mode == PetFlipGameMode.DailyChallenge ? "今日最佳" : "本机最佳";

        public static string FormatEntryDescription(PetFlipDifficulty difficulty, PetFlipGameMode mode)
        {
            string pairWord = difficulty == PetFlipDifficulty.Advanced16 ? "八对" : "六对";
            switch (mode)
            {
                case PetFlipGameMode.DailyChallenge:
                    return $"今日全员同盘，找出{pairWord}相同灵宠，可反复刷新今日最佳。";
                case PetFlipGameMode.SpiritEye:
                    return $"每局赠送 1 次灵目窥牌（看暗牌不计次），找出{pairWord}相同灵宠。";
                default:
                    return difficulty == PetFlipDifficulty.Relaxed12
                        ? EntryDescriptionText
                        : "每次翻两张，找出八对相同灵宠。翻错也可以继续。";
            }
        }

        /// <summary>
        /// 校验 6 种或 8 种灵宠牌面及必备框体资源是否齐全且无重复占位图。
        /// 缺任一头像、缺名称或两个图案复用同一贴图/精灵对象时返回 false，阻止正式开局。
        /// </summary>
        public static bool ValidateAssets(
            bool hasCoreUiSprites,
            IReadOnlyList<PetFlipFaceDescriptor> faces,
            out string failureReason)
        {
            int expectedCount = faces != null && faces.Count == PetFlipConstants.MaxPairCount
                ? PetFlipConstants.MaxPairCount
                : PetFlipConstants.PairCount;
            return ValidateAssets(hasCoreUiSprites, faces, expectedCount, out failureReason);
        }

        /// <summary>按指定最少对数（6 或 8）严格校验灵宠头像、名称与防同图占位。</summary>
        public static bool ValidateAssets(
            bool hasCoreUiSprites,
            IReadOnlyList<PetFlipFaceDescriptor> faces,
            int expectedPairCount,
            out string failureReason)
        {
            if (!hasCoreUiSprites)
            {
                failureReason = "missing_core_ui_sprites";
                return false;
            }

            if (expectedPairCount != PetFlipConstants.PairCount && expectedPairCount != PetFlipConstants.MaxPairCount)
            {
                failureReason = "invalid_face_count";
                return false;
            }

            if (faces == null || faces.Count != expectedPairCount)
            {
                failureReason = "invalid_face_count";
                return false;
            }

            var seenPatterns = new HashSet<SpiritPetCardPattern>();
            var seenPortraits = new HashSet<object>();

            for (int i = 0; i < faces.Count; i++)
            {
                PetFlipFaceDescriptor face = faces[i];
                int patternIdx = (int)face.Pattern;
                if (patternIdx < 0 || patternIdx >= expectedPairCount || !seenPatterns.Add(face.Pattern))
                {
                    failureReason = "duplicate_pattern";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(face.PetCatalogId) || string.IsNullOrWhiteSpace(face.DisplayName))
                {
                    failureReason = "missing_pet_name";
                    return false;
                }

                if (face.PortraitIdentity == null)
                {
                    failureReason = "missing_pet_portrait";
                    return false;
                }

                if (!seenPortraits.Add(face.PortraitIdentity))
                {
                    failureReason = "duplicate_placeholder_portrait";
                    return false;
                }
            }

            failureReason = null;
            return true;
        }

        /// <summary>
        /// 按资源状态与逻辑快照决定当前页面。
        /// 已有未完成或已完成内存盘时直接跳过说明页继续呈现盘面或终局，不强迫玩家重看说明。
        /// </summary>
        public static PetFlipUiPage ResolvePage(PetFlipSnapshot snapshot, PetFlipAssetLoadState assetState)
        {
            if (assetState == PetFlipAssetLoadState.Loading || assetState == PetFlipAssetLoadState.NotLoaded)
                return PetFlipUiPage.Loading;

            if (assetState == PetFlipAssetLoadState.Failed)
                return PetFlipUiPage.LoadFailed;

            if (snapshot == null || !snapshot.HasBoard)
                return PetFlipUiPage.Entry;

            return snapshot.Phase == PetFlipRoundPhase.Completed
                ? PetFlipUiPage.Complete
                : PetFlipUiPage.Board;
        }

        public static bool IsPortraitOrientation(float width, float height)
            => height > width && width > 0f && height > 0f;

        /// <summary>
        /// 返回槽位 <paramref name="slotIndex"/> 在横屏或窄竖屏下的设计像素矩形：
        /// - 轻松 12 张：横屏 4列×3行，竖屏 3列×4行（保持 V1 精调坐标）；
        /// - 进阶 16 张：横竖屏均为 4列×4行。
        /// 改变屏幕方向只重排几何位置，保持槽位编号与对应牌面身份不变。
        /// </summary>
        public static PetFlipPixelRect GetCardSlotRect(
            int slotIndex,
            bool isPortrait,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12)
        {
            int cardCount = PetFlipConstants.GetCardCount(difficulty);
            if (slotIndex < 0 || slotIndex >= cardCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, $"槽位索引必须在 0..{cardCount - 1} 之间。");

            if (difficulty == PetFlipDifficulty.Advanced16)
            {
                int col = slotIndex % 4;
                int row = slotIndex / 4;
                return isPortrait
                    ? new PetFlipPixelRect(157f + col * 186f, 314f + row * 222f, 152f, 198f)
                    : new PetFlipPixelRect(671f + col * 164f, 217f + row * 115f, 88f, 108f);
            }

            if (isPortrait)
            {
                int col = slotIndex % 3;
                int row = slotIndex / 3;
                return new PetFlipPixelRect(186f + col * 222f, 320f + row * 226f, 160f, 210f);
            }
            else
            {
                int col = slotIndex % 4;
                int row = slotIndex / 4;
                return new PetFlipPixelRect(620f + col * 179f, 223f + row * 151f, 112f, 144f);
            }
        }

        /// <summary>完成页灵宠圆形头像展示位（轻松 6 只：横 6×1 / 竖 3×2；进阶 8 只：横 8×1 / 竖 4×2）。</summary>
        public static PetFlipPixelRect GetCompletionMedallionRect(
            int patternIndex,
            bool isPortrait,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12)
        {
            int pairCount = PetFlipConstants.GetPairCount(difficulty);
            if (patternIndex < 0 || patternIndex >= pairCount)
                throw new ArgumentOutOfRangeException(nameof(patternIndex));

            if (difficulty == PetFlipDifficulty.Advanced16)
            {
                if (isPortrait)
                {
                    int col = patternIndex % 4;
                    int row = patternIndex / 4;
                    return new PetFlipPixelRect(157f + col * 190f, 532f + row * 224f, 140f, 140f);
                }

                return new PetFlipPixelRect(377f + patternIndex * 152f, 368f, 104f, 104f);
            }

            if (isPortrait)
            {
                int col = patternIndex % 3;
                int row = patternIndex / 3;
                return new PetFlipPixelRect(198f + col * 234f, 540f + row * 220f, 156f, 156f);
            }

            return new PetFlipPixelRect(478f + patternIndex * 154f, 365f, 112f, 112f);
        }

        /// <summary>完成页“一气呵成”完美通关徽章位置（对齐 pet-memory-ui-v2/manifest.json）。</summary>
        public static PetFlipPixelRect GetPerfectStampRect(bool isPortrait)
            => isPortrait
                ? new PetFlipPixelRect(448f, 298f, 128f, 128f)
                : new PetFlipPixelRect(745f, 222f, 128f, 128f);

        public static string FormatBestText(PetFlipLocalRecordSnapshot record)
        {
            if (record == null || !record.HasBestRecord || !record.BestFlipCount.HasValue)
                return NoRecordText;

            return record.BestFlipCount.Value.ToString(CultureInfo.InvariantCulture) + " 次";
        }

        public static string FormatPairCounter(int matchedPairCount, int totalPairCount = PetFlipConstants.PairCount)
            => $"已配 {matchedPairCount.ToString(CultureInfo.InvariantCulture)}/{totalPairCount.ToString(CultureInfo.InvariantCulture)} 对";

        public static string FormatFlipCounter(int flipCount)
            => "翻牌次数 " + flipCount.ToString(CultureInfo.InvariantCulture);

        public static string FormatBoardHint(PetFlipSnapshot snapshot, bool isPortrait, bool spiritEyeArmed = false)
        {
            if (snapshot == null)
                return isPortrait ? PortraitDefaultHintText : BoardDefaultHintText;

            if (snapshot.IsDisconnected)
                return DisconnectedHintText;

            if (snapshot.HandState == PetFlipHandState.MismatchShowing)
                return isPortrait ? MismatchHintText : "点任一未配对的牌\n继续翻";

            if (snapshot.HandState == PetFlipHandState.SpiritEyePeeking)
                return isPortrait ? SpiritEyePeekingHintText : "灵目已窥\n点任一未配对牌继续";

            if (spiritEyeArmed && snapshot.CanUseSpiritEye)
                return isPortrait ? SpiritEyeArmedHintText : "灵目就绪\n点一张暗牌免费窥看";

            if (snapshot.HandState == PetFlipHandState.WaitingFirstCard && snapshot.LastMatchedPattern.HasValue)
            {
                string quote = GetPetMatchFlavorText(snapshot.LastMatchedPattern.Value);
                string streak = FormatStreakBadge(snapshot.CurrentStreak);
                if (!string.IsNullOrEmpty(streak))
                    return isPortrait ? streak + " " + quote : streak + "\n" + quote;
                if (!string.IsNullOrEmpty(quote))
                    return quote;
            }

            return isPortrait ? PortraitDefaultHintText : BoardDefaultHintText;
        }

        public static string FormatCompletionHeadline(PetFlipCompletionResult result)
        {
            if (result == null)
                return "六对灵宠，全部配齐！";

            string pairWord = result.MatchedPairCount >= PetFlipConstants.AdvancedPairCount ? "八对" : "六对";
            string bestLabel = FormatBestTitle(result.Mode);
            string flips = result.FlipCount.ToString(CultureInfo.InvariantCulture);
            string best = result.BestFlipCount.ToString(CultureInfo.InvariantCulture);

            switch (result.Comparison)
            {
                case PetFlipRecordComparison.FirstRecord:
                    return $"{pairWord}齐了！首次纪录{flips}次。";
                case PetFlipRecordComparison.NewBest:
                {
                    int delta = result.PreviousBestFlipCount.HasValue
                        ? Math.Max(1, result.PreviousBestFlipCount.Value - result.FlipCount)
                        : 1;
                    return $"刷新{bestLabel}！比上次少{delta.ToString(CultureInfo.InvariantCulture)}次。";
                }
                case PetFlipRecordComparison.TiedBest:
                    return $"追平{bestLabel}，{flips}次。";
                default:
                    return $"{pairWord}齐了！本局{flips}次，{bestLabel}{best}次。";
            }
        }

        public static string FormatCompletionSummary(PetFlipCompletionResult result)
        {
            if (result == null)
                return string.Empty;

            string bestLabel = FormatBestTitle(result.Mode);
            string baseText = $"本局 {result.FlipCount.ToString(CultureInfo.InvariantCulture)} 次  ·  {bestLabel} {result.BestFlipCount.ToString(CultureInfo.InvariantCulture)} 次";
            if (result.IsPerfectRun)
                return baseText + "  ·  " + PerfectRunBadgeText;
            if (result.MaxStreak >= 2)
                return baseText + $"  ·  最高 {result.MaxStreak.ToString(CultureInfo.InvariantCulture)} 连配";
            return baseText;
        }

        public static bool ShouldShowUnsavedNotice(PetFlipSnapshot snapshot)
        {
            if (snapshot == null)
                return false;

            return (snapshot.LocalRecord != null && snapshot.LocalRecord.IsUnsaved)
                || (snapshot.CompletionResult != null && snapshot.CompletionResult.IsRecordUnsaved);
        }

        /// <summary>构造不依赖色相的可访问卡牌对象名称，已配对牌必含“已配”，灵目窥看含“灵目”。</summary>
        public static string FormatCardAccessibleName(
            PetFlipCardSnapshot card,
            string petDisplayName,
            bool isSpiritEyePeek = false)
        {
            string slot = card.SlotIndex.ToString(CultureInfo.InvariantCulture);
            string name = string.IsNullOrWhiteSpace(petDisplayName) ? "灵宠" : petDisplayName;
            switch (card.State)
            {
                case PetFlipCardState.Matched:
                    return $"PetFlipCard_{slot}_{name}_已配";
                case PetFlipCardState.FaceUp:
                    return isSpiritEyePeek
                        ? $"PetFlipCard_{slot}_{name}_灵目"
                        : $"PetFlipCard_{slot}_{name}_翻开";
                default:
                    return $"PetFlipCard_{slot}_背面";
            }
        }
    }
}
