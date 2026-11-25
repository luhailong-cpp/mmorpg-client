using System;
using System.Collections.Generic;
using MmorpgClient.Table;

namespace MmorpgClient.Game.Appearance
{
    /// <summary>单个形象的候选人物档案与短篇故事（纯只读数据，不覆盖玩家昵称，可独立改稿）。</summary>
    public sealed class AppearanceBiography
    {
        public string RawAppearanceId { get; }
        public string ArchiveName { get; }
        public string Nickname { get; }
        public string OriginalTitle { get; }
        public string BackgroundSummary { get; }
        public string StoryTitle { get; }
        public string StoryBody { get; }

        public string DisplayArchiveName => string.IsNullOrEmpty(Nickname)
            ? ArchiveName
            : $"{ArchiveName}（{Nickname}）";

        public AppearanceBiography(
            string rawAppearanceId,
            string archiveName,
            string nickname,
            string originalTitle,
            string backgroundSummary,
            string storyTitle,
            string storyBody)
        {
            RawAppearanceId = rawAppearanceId ?? "";
            ArchiveName = archiveName ?? "";
            Nickname = nickname ?? "";
            OriginalTitle = originalTitle ?? "";
            BackgroundSummary = backgroundSummary ?? "";
            StoryTitle = storyTitle ?? "";
            StoryBody = storyBody ?? "";
        }
    }

    /// <summary>
    /// 人物背景与故事目录。
    /// 权威真相来自 <see cref="CharacterAppearanceTableManager"/>（热更后每次现查，绝不缓存行引用）；
    /// 仅在表尚未加载的极简离线单测上下文中回退至同源 9 人只读快照。
    /// 严格按条目原始稳定 <c>appearance_id</c> 精确匹配，绝不使用列表下标或历史视觉替代 ID。
    /// </summary>
    public static class AppearanceBiographyCatalog
    {
        private static readonly AppearanceBiography[] FallbackEntries =
        {
            new AppearanceBiography(
                rawAppearanceId: "00_reference_topright_boy",
                archiveName: "陆照禾",
                nickname: "阿禾",
                originalTitle: "金发带Q道童",
                backgroundSummary:
                    "母亲在归元城开面铺，祖母膝盖疼，把留灯院的钥匙交给阿禾。金葫芦里装的是井水。他记得谁吃面不放葱，却老忘了自己也能开口求人。",
                storyTitle: "第四只碗",
                storyBody:
                    "屋顶又漏了。阿禾一手举着碗，一手还端着给巷口送的面。\n\n" +
                    "停云扶住门框：“你还要走？”\n\n" +
                    "他迟疑了一下：“能替我看一会儿院子吗？”\n\n" +
                    "她把第四只碗往左挪了半寸：“成。回来带葱。”"),

            new AppearanceBiography(
                rawAppearanceId: "01_ice_sword_girl",
                archiveName: "霜见雪",
                nickname: "",
                originalTitle: "冰剑少女",
                backgroundSummary:
                    "出身北境玄霜剑阁，剑鞘上系着半截冻裂的旧流苏。性子清冷少言，练剑从不避风雪，却会悄悄把演武台边结冰的石阶扫净，免得旁人滑倒。",
                storyTitle: "霜阶扫雪",
                storyBody:
                    "卯时的演武台结着一层薄霜，霜见雪收剑归鞘，顺手抄起廊角那把秃头竹帚。\n\n" +
                    "许炽揉着眼走上石阶：“你每日练完剑，还要替杂役扫地？”\n\n" +
                    "她头也不抬，把最后一道暗冰扫进排水沟：“昨日你踩滑，险些撞翻沈澄的茶壶。”\n\n" +
                    "许炽噎了一下，抓起火符烘暖石面：“那明日我早来半刻，烤干了省得你扫。”她停了停帚：“别把青石烤裂。”"),

            new AppearanceBiography(
                rawAppearanceId: "02_fire_talisman_boy",
                archiveName: "许炽",
                nickname: "小炽",
                originalTitle: "火符少年",
                backgroundSummary:
                    "归元城南朱砂坊的学徒，衣袖总沾着洗不净的红墨。画符讲究一气呵成，常因性子太急烧焦符角，偏偏烤栗子与暖炉火候拿捏得极准。",
                storyTitle: "焦边朱砂",
                storyBody:
                    "案头摞着半尺画废的赤符，边缘全卷着焦黑的边。许炽盯着最后一笔叹气：“又收不住火。”\n\n" +
                    "沈澄递过一碗放凉的井水：“你下笔只想着破邪，没留半寸回旋的余地。”\n\n" +
                    "许炽蘸了点墨，重新压稳腕力，这一回朱砂顺着黄纸一气贯通，符尾只腾起一缕温热的细烟。\n\n" +
                    "他咧嘴笑起来，转身把废符拢进小炉：“不白费，正好留着给你们烤一兜糖栗子。”"),

            new AppearanceBiography(
                rawAppearanceId: "03_lotus_healer_girl",
                archiveName: "苏青莲",
                nickname: "",
                originalTitle: "莲花医者",
                backgroundSummary:
                    "归元城西药庐的掌勺医者，案头常年养着一钵清露青莲。诊脉细心、记性极好，谁怕苦药、谁爱忘按时换敷，她都在药包绳结上打了暗记。",
                storyTitle: "药包双结",
                storyBody:
                    "暮色沉下来，药庐最后一位扭伤脚踝的脚夫刚被送出门。苏青莲解开围裙，顺手往桌上多放了一颗话梅。\n\n" +
                    "林知音抱着瑶琴凑过来：“这副安神散怎么系了两道活结？”\n\n" +
                    "“那位阿婆眼神不好，摸见双结便知是睡前那一包。”苏青莲吹熄药炉余火，“单结的才是晨起喝的。”\n\n" +
                    "林知音替她拢起晒干的莲心：“难怪街坊都说，你开的药还没入口，心就先安了一半。”"),

            new AppearanceBiography(
                rawAppearanceId: "05_celestial_musician_girl",
                archiveName: "林知音",
                nickname: "",
                originalTitle: "天音少女",
                backgroundSummary:
                    "游历九州的抚琴弟子，随身一床七弦松木瑶琴。不爱在酒楼高台炫技，更常坐到城西药庐的竹廊下试弦，说是檐风与药香最衬清越泛音。",
                storyTitle: "廊下试弦",
                storyBody:
                    "夏雨初歇，琴弦受潮略松。林知音坐在药庐长廊下，一寸一寸调着徽位，拨出三声散音。\n\n" +
                    "里屋正熬药的苏青莲探出头：“方才那段慢了一拍，是换了新弦？”\n\n" +
                    "“不是新弦。”林知音指尖轻按七徽，看向檐下避雨的一排灰雀，“雨声太急，我放慢些，免得把它们惊飞。”\n\n" +
                    "廊外水珠顺着瓦当落进青莲钵，正好接上她下一记清脆的泛音。"),

            new AppearanceBiography(
                rawAppearanceId: "09_bamboo_archer_girl",
                archiveName: "桑听雨",
                nickname: "",
                originalTitle: "竹弓少女",
                backgroundSummary:
                    "月汐湾竹器人家的女儿，想去八景岛画潮汐鸟谱。家里早备好行李，她却把船票让给了别人。竹筒里还留着旧票角，和一张没落过笔的纸。",
                storyTitle: "没有画的那一页",
                storyBody:
                    "阿禾调好了青色：“岛上的鸟脚也是青的吗？”\n\n" +
                    "听雨看看他摊开的画纸：“岸边这只青。岛上的……替我空一页吧。”\n\n" +
                    "饭后她独自走到渡口，问船家，一个人带行李该坐哪一边。"),

            new AppearanceBiography(
                rawAppearanceId: "10_crimson_spear_girl",
                archiveName: "祝停云",
                nickname: "",
                originalTitle: "赤枪少女",
                backgroundSummary:
                    "月汐湾摆渡人家的女儿。父亲掌舵，母亲修船，她把旧船绳编进枪穗，想挣来修船的钱，也想堂堂正正赢一场。败局册里记给自己的问题，字总是特别小。",
                storyTitle: "纸也赢过一回",
                storyBody:
                    "她连输三场回院，枪穗往砚台里滴水。\n\n" +
                    "沈澄写“惜败”，她说：“去掉惜字。”\n\n" +
                    "自己把“对手拖”划成“我又急了”。\n\n" +
                    "吃到半碗面，她拿筷子比了两步：“什么时候该等？”\n\n" +
                    "他抬眼：“昨天谁说我纸上谈兵？”\n\n" +
                    "“今天纸赢了。你讲。”"),

            new AppearanceBiography(
                rawAppearanceId: "15_water_dragon_scholar_boy",
                archiveName: "沈澄",
                nickname: "",
                originalTitle: "水龙书生",
                backgroundSummary:
                    "常驻八仙岛与归元城之间的沧澜院书生，一柄素面折扇能引水势也能记战局。待人温和有礼，爱把各家招式的破绽抄进袖珍册页，却最怕别人在茶凉前催他下棋。",
                storyTitle: "半盏未凉",
                storyBody:
                    "八仙岛石亭风大，沈澄拿镇纸压住刚画好的阵势图，顺手替对面续了半盏清茶。\n\n" +
                    "霜见雪瞄了一眼图上密密麻麻的批注：“你连许炽收势慢半息都记下来了？”\n\n" +
                    "“好记性不如烂笔头。”沈澄合扇轻敲掌心，“昨日在归元城留灯院，祝姑娘拿筷子比步法，倒让我又看清两处变招。”\n\n" +
                    "亭外水汽漫过栏杆，他端起茶盏浅啜一口：“趁热喝吧，茶凉了，棋局就不好看了。”"),

            new AppearanceBiography(
                rawAppearanceId: "20_star_formation_master_girl",
                archiveName: "星璇",
                nickname: "",
                originalTitle: "星阵宗师",
                backgroundSummary:
                    "观星台守阵人，袖中星盘刻满五行生克轨迹。不以重金易道，唯认破阵历练之实；凡在五行试炼志积满十八枚战法印者，方得她亲授观星同游之约。",
                storyTitle: "十八星印",
                storyBody:
                    "观星台夜露微凉，星璇俯身拨正铜盘上最后一枚星子，抬眸望向石阶尽头。\n\n" +
                    "“破阵、御障、回春、易势——十八枚战法印，倒真叫你一枚不落地集齐了。”\n\n" +
                    "她合上星图卷轴，并未赠出半件神兵，只将一枚素净的玉星挂回腰间：“阵法在心，不在器物。这一路你走过的每一局，自己已是最好的凭据。”\n\n" +
                    "星盘轻转半圈，檐外星河倒映在台面水镜中：“既已过关，今夜归元城的风，便由你换副模样去看看。”"),
        };

        private static readonly Dictionary<string, AppearanceBiography> FallbackByRawId = BuildFallbackIndex();

        public static IReadOnlyList<AppearanceBiography> All
        {
            get
            {
                var rows = CharacterAppearanceTableManager.Instance.FindAll();
                if (rows.Count == 0) return FallbackEntries;
                var list = new List<AppearanceBiography>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (TryFromTableRow(rows[i], out var bio))
                        list.Add(bio);
                }
                return list;
            }
        }

        /// <summary>
        /// 仅按原始稳定 <paramref name="rawAppearanceId"/> 精确查找，绝不经视觉别名映射。
        /// 权威配表已加载时完全以当前快照为准（支持热更且不缓存行引用）。
        /// </summary>
        public static bool TryGetByRawAppearanceId(string rawAppearanceId, out AppearanceBiography biography)
        {
            if (string.IsNullOrEmpty(rawAppearanceId))
            {
                biography = null;
                return false;
            }
            var rows = CharacterAppearanceTableManager.Instance.FindAll();
            if (rows.Count > 0)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (string.Equals(row.AppearanceId, rawAppearanceId, StringComparison.Ordinal))
                        return TryFromTableRow(row, out biography);
                }
                biography = null;
                return false;
            }
            return FallbackByRawId.TryGetValue(rawAppearanceId, out biography);
        }

        private static bool TryFromTableRow(CharacterAppearanceTable row, out AppearanceBiography biography)
        {
            if (row == null ||
                string.IsNullOrWhiteSpace(row.ArchiveName) ||
                string.IsNullOrWhiteSpace(row.BiographySummary) ||
                string.IsNullOrWhiteSpace(row.StoryTitle) ||
                string.IsNullOrWhiteSpace(row.StoryBody))
            {
                biography = null;
                return false;
            }
            biography = new AppearanceBiography(
                row.AppearanceId,
                row.ArchiveName,
                row.Nickname,
                row.Name,
                row.BiographySummary,
                row.StoryTitle,
                row.StoryBody);
            return true;
        }

        private static Dictionary<string, AppearanceBiography> BuildFallbackIndex()
        {
            var map = new Dictionary<string, AppearanceBiography>(FallbackEntries.Length, StringComparer.Ordinal);
            for (int i = 0; i < FallbackEntries.Length; i++)
                map[FallbackEntries[i].RawAppearanceId] = FallbackEntries[i];
            return map;
        }
    }

    public enum AppearanceOverlayKind
    {
        None = 0,
        SwitchConfirm = 1,
        NoCardGuide = 2,
        StoryReader = 3,
        Biography = 4,
        PortraitPreview = 5,
        SeriesEpilogue = 6,
    }

    public enum AppearanceSwitchIntentOutcome
    {
        Ignored = 0,
        OpenedNoCardGuide = 1,
        OpenedSwitchConfirm = 2,
        ReadyForFreeSwitch = 3,
        LockedExclusive = 4,
    }

    public enum AppearanceStoryCloseReason
    {
        Back = 0,
        Skip = 1,
        Finished = 2,
        Escape = 3,
    }

    /// <summary>
    /// 易容面板的纯 C# 视图交互状态机（零 Unity 依赖）：
    /// 负责选中态、列表滚动位置保留、故事阅读层生命周期与防误扣卡确认清理。
    /// </summary>
    public sealed class AppearancePanelState
    {
        public string SelectedAppearanceId { get; private set; } = "";
        public uint SelectedSeriesId { get; private set; }
        public AppearanceOverlayKind ActiveOverlay { get; private set; } = AppearanceOverlayKind.None;
        public string PendingSwitchTargetId { get; private set; } = "";
        public string ReadingAppearanceId { get; private set; } = "";
        public uint ReadingSeriesEpilogueId { get; private set; }
        public float SavedListScrollNormalized { get; private set; } = 1f;
        public int StoryOpenCount { get; private set; }
        public float SavedStoryScrollNormalized { get; private set; } = 1f;
        private bool _storyReturnsToBiography;
        private AppearanceOverlayKind _previewReturnOverlay;

        // 保留显式武装态字段，确保任何历史或外部调用点在进入阅读层后都能彻底清零。
        public string ArmedSwitchAppearanceId { get; private set; } = "";
        public float ArmedSwitchAtSeconds { get; private set; } = float.NegativeInfinity;

        public bool HasOverlay => ActiveOverlay != AppearanceOverlayKind.None;
        public bool IsStoryOpen => ActiveOverlay == AppearanceOverlayKind.StoryReader;
        public bool IsSwitchConfirmOpen => ActiveOverlay == AppearanceOverlayKind.SwitchConfirm;
        public bool IsNoCardGuideOpen => ActiveOverlay == AppearanceOverlayKind.NoCardGuide;
        public bool IsBiographyOpen => ActiveOverlay == AppearanceOverlayKind.Biography;
        public bool IsPortraitPreviewOpen => ActiveOverlay == AppearanceOverlayKind.PortraitPreview;
        public bool IsSeriesEpilogueOpen => ActiveOverlay == AppearanceOverlayKind.SeriesEpilogue;
        public bool IsReadOnlyOverlay => IsStoryOpen || IsBiographyOpen || IsPortraitPreviewOpen || IsSeriesEpilogueOpen;
        public bool HasPendingSwitchConfirmation =>
            ActiveOverlay == AppearanceOverlayKind.SwitchConfirm || !string.IsNullOrEmpty(ArmedSwitchAppearanceId);

        public bool TryGetSelectedBiography(out AppearanceBiography biography)
            => AppearanceBiographyCatalog.TryGetByRawAppearanceId(SelectedAppearanceId, out biography);

        public bool TryGetReadingBiography(out AppearanceBiography biography)
        {
            if (!IsStoryOpen)
            {
                biography = null;
                return false;
            }
            return AppearanceBiographyCatalog.TryGetByRawAppearanceId(ReadingAppearanceId, out biography);
        }

        public void SyncAvailableAppearances(IReadOnlyList<string> validAppearanceIds, string fallbackActiveId,
            string permanentAppearanceId)
        {
            if (validAppearanceIds == null || validAppearanceIds.Count == 0)
            {
                SelectedAppearanceId = "";
                CloseAllOverlaysAndClearSwitchConfirmation();
                return;
            }

            if (!ContainsId(validAppearanceIds, SelectedAppearanceId))
            {
                SelectedAppearanceId = ContainsId(validAppearanceIds, fallbackActiveId)
                    ? fallbackActiveId
                    : validAppearanceIds[0];
                ClearPendingSwitchConfirmation();
                SavedStoryScrollNormalized = 1f;
            }

            // 若正在阅读的原始条目失效（被移除或无档案），立刻收起阅读层，不残留旧正文。
            if (IsReadOnlyOverlay && !IsSeriesEpilogueOpen && (!ContainsId(validAppearanceIds, ReadingAppearanceId) ||
                (!IsPortraitPreviewOpen && !AppearanceBiographyCatalog.TryGetByRawAppearanceId(ReadingAppearanceId, out _))))
            {
                ReadingAppearanceId = "";
                ActiveOverlay = AppearanceOverlayKind.None;
            }

            if (IsSwitchConfirmOpen && (!ContainsId(validAppearanceIds, PendingSwitchTargetId) ||
                string.Equals(PendingSwitchTargetId, permanentAppearanceId, StringComparison.Ordinal)))
            {
                PendingSwitchTargetId = "";
                ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public void SelectAppearance(string rawAppearanceId, float currentListScrollNormalized)
        {
            if (string.IsNullOrEmpty(rawAppearanceId)) return;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            bool changed = !string.Equals(SelectedAppearanceId, rawAppearanceId, StringComparison.Ordinal);
            SelectedAppearanceId = rawAppearanceId;
            ClearPendingSwitchConfirmation();
            if (changed)
            {
                SavedStoryScrollNormalized = 1f;
                _storyReturnsToBiography = false;
                _previewReturnOverlay = AppearanceOverlayKind.None;
                ReadingAppearanceId = "";
                if (IsReadOnlyOverlay) ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public void ArmLegacySwitchConfirmation(string targetAppearanceId, float nowSeconds)
        {
            if (string.IsNullOrEmpty(targetAppearanceId)) return;
            ArmedSwitchAppearanceId = targetAppearanceId;
            ArmedSwitchAtSeconds = nowSeconds;
        }

        public AppearanceSwitchIntentOutcome RequestSwitch(
            string targetAppearanceId,
            string permanentAppearanceId,
            ulong ownedCardCount,
            uint requiredCardCount,
            bool targetOwned = false,
            bool targetCardUnlockable = true)
        {
            if (IsReadOnlyOverlay) return AppearanceSwitchIntentOutcome.Ignored;
            if (string.IsNullOrEmpty(targetAppearanceId) ||
                string.Equals(targetAppearanceId, permanentAppearanceId, StringComparison.Ordinal))
            {
                ClearPendingSwitchConfirmation();
                return AppearanceSwitchIntentOutcome.Ignored;
            }

            ArmedSwitchAppearanceId = "";
            ArmedSwitchAtSeconds = float.NegativeInfinity;

            if (targetOwned)
            {
                ClearPendingSwitchConfirmation();
                return AppearanceSwitchIntentOutcome.ReadyForFreeSwitch;
            }

            if (!targetCardUnlockable)
            {
                ClearPendingSwitchConfirmation();
                return AppearanceSwitchIntentOutcome.LockedExclusive;
            }

            uint needed = requiredCardCount > 0 ? requiredCardCount : 1u;
            if (ownedCardCount < needed)
            {
                PendingSwitchTargetId = targetAppearanceId;
                ActiveOverlay = AppearanceOverlayKind.NoCardGuide;
                return AppearanceSwitchIntentOutcome.OpenedNoCardGuide;
            }

            PendingSwitchTargetId = targetAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.SwitchConfirm;
            return AppearanceSwitchIntentOutcome.OpenedSwitchConfirm;
        }

        public bool TryConsumePendingSwitchConfirm(out string targetAppearanceId)
        {
            if (ActiveOverlay != AppearanceOverlayKind.SwitchConfirm || string.IsNullOrEmpty(PendingSwitchTargetId))
            {
                targetAppearanceId = "";
                return false;
            }

            targetAppearanceId = PendingSwitchTargetId;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public bool OpenStory(float currentListScrollNormalized)
        {
            bool fromBiography = IsBiographyOpen;
            // 进入阅读层时，无论是否成功打开，一律清除尚未提交的旧扣卡确认。
            ClearPendingSwitchConfirmation();
            if (!AppearanceBiographyCatalog.TryGetByRawAppearanceId(SelectedAppearanceId, out _))
                return false;

            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            ReadingAppearanceId = SelectedAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.StoryReader;
            _storyReturnsToBiography = fromBiography;
            StoryOpenCount++;
            return true;
        }

        public bool CloseStory(AppearanceStoryCloseReason reason)
        {
            if (!IsStoryOpen) return false;
            bool returnToBiography = _storyReturnsToBiography &&
                (reason == AppearanceStoryCloseReason.Back || reason == AppearanceStoryCloseReason.Escape);
            ReadingAppearanceId = returnToBiography ? SelectedAppearanceId : "";
            ActiveOverlay = returnToBiography ? AppearanceOverlayKind.Biography : AppearanceOverlayKind.None;
            // 返回后清空所有旧确认态，保证首次点击“切换”必须重新进入确认。
            ClearPendingSwitchConfirmation();
            return true;
        }

        public bool DismissActiveOverlay()
        {
            if (IsPortraitPreviewOpen) return ClosePortraitPreview();
            if (IsSeriesEpilogueOpen) return CloseSeriesEpilogue();
            if (IsBiographyOpen) return CloseBiography();
            if (IsStoryOpen) return CloseStory(AppearanceStoryCloseReason.Escape);
            if (IsSwitchConfirmOpen || IsNoCardGuideOpen)
            {
                ClearPendingSwitchConfirmation();
                return true;
            }
            return false;
        }

        public void SelectSeries(uint seriesId, float currentListScrollNormalized)
        {
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            if (SelectedSeriesId != seriesId)
            {
                SelectedSeriesId = seriesId;
                if (IsReadOnlyOverlay) CloseAllOverlaysAndClearSwitchConfirmation();
            }
            ClearPendingSwitchConfirmation();
        }

        public void SyncSeriesSelection(IReadOnlyList<uint> validSeriesIds, Func<uint, bool> isCompleted = null)
        {
            if (SelectedSeriesId != 0 && !ContainsSeriesId(validSeriesIds, SelectedSeriesId))
            {
                SelectedSeriesId = 0;
            }
            if (IsSeriesEpilogueOpen && (!ContainsSeriesId(validSeriesIds, ReadingSeriesEpilogueId) ||
                (isCompleted != null && !isCompleted(ReadingSeriesEpilogueId))))
            {
                ReadingSeriesEpilogueId = 0;
                ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public bool OpenSeriesEpilogue(uint seriesId, bool seriesCompleted, string epilogueBody, float currentListScrollNormalized)
        {
            // 进入合篇阅读层时一律清除尚未提交的旧扣卡确认。
            ClearPendingSwitchConfirmation();
            if (seriesId == 0 || !seriesCompleted || string.IsNullOrWhiteSpace(epilogueBody))
                return false;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            ReadingAppearanceId = "";
            ReadingSeriesEpilogueId = seriesId;
            ActiveOverlay = AppearanceOverlayKind.SeriesEpilogue;
            return true;
        }

        public bool CloseSeriesEpilogue()
        {
            if (!IsSeriesEpilogueOpen) return false;
            ReadingSeriesEpilogueId = 0;
            ActiveOverlay = AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public void ClearPendingSwitchConfirmation()
        {
            PendingSwitchTargetId = "";
            ArmedSwitchAppearanceId = "";
            ArmedSwitchAtSeconds = float.NegativeInfinity;
            if (ActiveOverlay == AppearanceOverlayKind.SwitchConfirm ||
                ActiveOverlay == AppearanceOverlayKind.NoCardGuide)
            {
                ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public void ResetForCloseOrDisconnect()
        {
            CloseAllOverlaysAndClearSwitchConfirmation();
            SavedStoryScrollNormalized = 1f;
        }

        public void ResetForRoleChange()
        {
            ResetForCloseOrDisconnect();
            SelectedAppearanceId = "";
            SelectedSeriesId = 0;
            SavedListScrollNormalized = 1f;
        }

        public bool OpenBiography(float currentListScrollNormalized)
        {
            ClearPendingSwitchConfirmation();
            if (!TryGetSelectedBiography(out _)) return false;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            ReadingAppearanceId = SelectedAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.Biography;
            return true;
        }

        public bool CloseBiography()
        {
            if (!IsBiographyOpen) return false;
            ReadingAppearanceId = "";
            ActiveOverlay = AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public bool OpenPortraitPreview(float currentListScrollNormalized)
        {
            var returnOverlay = IsReadOnlyOverlay ? ActiveOverlay : AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            if (string.IsNullOrEmpty(SelectedAppearanceId)) return false;
            if (IsPortraitPreviewOpen) return true;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            _previewReturnOverlay = returnOverlay;
            ReadingAppearanceId = SelectedAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.PortraitPreview;
            return true;
        }

        public bool ClosePortraitPreview()
        {
            if (!IsPortraitPreviewOpen) return false;
            ActiveOverlay = _previewReturnOverlay;
            if (ActiveOverlay == AppearanceOverlayKind.None) ReadingAppearanceId = "";
            _previewReturnOverlay = AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public void SaveStoryPosition(float normalizedPosition)
            => SavedStoryScrollNormalized = Clamp01(normalizedPosition);

        private void CloseAllOverlaysAndClearSwitchConfirmation()
        {
            ReadingAppearanceId = "";
            ReadingSeriesEpilogueId = 0;
            PendingSwitchTargetId = "";
            ArmedSwitchAppearanceId = "";
            ArmedSwitchAtSeconds = float.NegativeInfinity;
            ActiveOverlay = AppearanceOverlayKind.None;
            _storyReturnsToBiography = false;
            _previewReturnOverlay = AppearanceOverlayKind.None;
        }

        private static bool ContainsSeriesId(IReadOnlyList<uint> ids, uint target)
        {
            if (ids == null || target == 0) return false;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == target) return true;
            return false;
        }

        private static bool ContainsId(IReadOnlyList<string> ids, string target)
        {
            if (ids == null || string.IsNullOrEmpty(target)) return false;
            for (int i = 0; i < ids.Count; i++)
                if (string.Equals(ids[i], target, StringComparison.Ordinal)) return true;
            return false;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
