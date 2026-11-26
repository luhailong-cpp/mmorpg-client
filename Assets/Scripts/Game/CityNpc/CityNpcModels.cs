using System;
using System.Collections.Generic;
using System.Text;

namespace MmorpgClient.Game.CityNpc
{
    /// <summary>归元城原创 NPC 可快捷拉起的既有系统入口（保留 HUD 快捷栏，绝不强迫跑腿）。</summary>
    public enum CityNpcShortcutKind
    {
        None = 0,
        EquipmentForge = 1,
        Bag = 2,
        MarketTrade = 3,
        SpiritShop = 4,
        TravelPass = 5,
        FormalPvp = 6,
        CustomDuel = 7,
        BossTrial = 8,
        WuxingTower = 9,
        TrialJournal = 10,
        GuildWar = 11,
        Pets = 12,
        Skills = 13,
        WorldMap = 14,
        Appearance = 15,
        Attributes = 16
    }

    public enum CityNpcFeatureKind
    {
        None = 0,
        SparringAndPuzzles = 1,
        CharacterTale = 2,
        LightErrands = 3,
        FortuneAndCallingCard = 4
    }

    public enum CityNpcEncounterCategory
    {
        PracticeDummy = 1,
        TacticalPuzzle = 2,
        CharacterTale = 3
    }

    public sealed class CityNpcShortcut
    {
        public string Label { get; }
        public CityNpcShortcutKind Kind { get; }

        public CityNpcShortcut(string label, CityNpcShortcutKind kind)
        {
            Label = label ?? "";
            Kind = kind;
        }
    }

    /// <summary>归元城单名 100% 原创核心 NPC 的不可变规格（坐标均取自已验单连通地标）。</summary>
    public sealed class CityNpcDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string RoleTitle { get; }
        public string LandmarkLabel { get; }
        public float WorldX { get; }
        public float WorldZ { get; }
        /// <summary>复用项目自有原创形象目录占位，待 Codex 交付专属立绘后自动优先加载 DedicatedSpriteKey。</summary>
        public string FallbackAppearanceId { get; }
        public string DedicatedSpriteKey => "UI/Ugui/GuiyuanNpcV1/Npcs/npc_" + Id;
        public string DefaultGreeting { get; }
        public string AmbientBubble { get; }
        public CityNpcFeatureKind FeatureKind { get; }
        public IReadOnlyList<CityNpcShortcut> Shortcuts { get; }
        public IReadOnlyDictionary<string, string> CharacterGreetings { get; }

        public CityNpcDefinition(
            string id,
            string name,
            string roleTitle,
            string landmarkLabel,
            float worldX,
            float worldZ,
            string fallbackAppearanceId,
            string defaultGreeting,
            string ambientBubble,
            CityNpcFeatureKind featureKind,
            IReadOnlyList<CityNpcShortcut> shortcuts,
            IReadOnlyDictionary<string, string> characterGreetings)
        {
            Id = id;
            Name = name;
            RoleTitle = roleTitle;
            LandmarkLabel = landmarkLabel;
            WorldX = worldX;
            WorldZ = worldZ;
            FallbackAppearanceId = fallbackAppearanceId;
            DefaultGreeting = defaultGreeting;
            AmbientBubble = ambientBubble;
            FeatureKind = featureKind;
            Shortcuts = shortcuts ?? Array.Empty<CityNpcShortcut>();
            CharacterGreetings = characterGreetings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public bool TryGetCharacterGreeting(string rawAppearanceId, out string greeting)
        {
            if (!string.IsNullOrEmpty(rawAppearanceId) &&
                CharacterGreetings.TryGetValue(rawAppearanceId, out greeting) &&
                !string.IsNullOrEmpty(greeting))
            {
                return true;
            }
            greeting = DefaultGreeting;
            return false;
        }
    }

    /// <summary>演武木桩 / 回合残局 / 人物奇谈短案的不可变副本配置描述。</summary>
    public sealed class CityNpcEncounterDefinition
    {
        public uint DungeonConfigId { get; }
        public CityNpcEncounterCategory Category { get; }
        public string HostNpcId { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string StoryIntro { get; }
        public string HiddenEpilogueTitle { get; }
        public string HiddenEpilogueBody { get; }
        public string ClearBadgeName { get; }
        public bool AllowsFreePreset { get; }
        public uint RecommendedCharacterPresetId { get; }
        public uint RecommendedPetPresetId { get; }
        public string LinkedAppearanceId { get; }

        public CityNpcEncounterDefinition(
            uint dungeonConfigId,
            CityNpcEncounterCategory category,
            string hostNpcId,
            string title,
            string subtitle,
            string storyIntro,
            string hiddenEpilogueTitle,
            string hiddenEpilogueBody,
            string clearBadgeName,
            bool allowsFreePreset,
            uint recommendedCharacterPresetId,
            uint recommendedPetPresetId,
            string linkedAppearanceId = "")
        {
            DungeonConfigId = dungeonConfigId;
            Category = category;
            HostNpcId = hostNpcId;
            Title = title;
            Subtitle = subtitle;
            StoryIntro = storyIntro;
            HiddenEpilogueTitle = hiddenEpilogueTitle;
            HiddenEpilogueBody = hiddenEpilogueBody;
            ClearBadgeName = clearBadgeName;
            AllowsFreePreset = allowsFreePreset;
            RecommendedCharacterPresetId = recommendedCharacterPresetId;
            RecommendedPetPresetId = recommendedPetPresetId;
            LinkedAppearanceId = linkedAppearanceId ?? "";
        }
    }

    /// <summary>周行舟发布的不限时「顺路捎物」轻委托（不催打卡、不占 660 核心料）。</summary>
    public sealed class CityNpcErrandDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public uint TargetSceneConfigId { get; }
        public string TargetSceneName { get; }
        public string ParcelDesc { get; }
        public string CompletionNote { get; }

        public CityNpcErrandDefinition(
            string id,
            string title,
            uint targetSceneConfigId,
            string targetSceneName,
            string parcelDesc,
            string completionNote)
        {
            Id = id;
            Title = title;
            TargetSceneConfigId = targetSceneConfigId;
            TargetSceneName = targetSceneName;
            ParcelDesc = parcelDesc;
            CompletionNote = completionNote;
        }
    }

    /// <summary>莫听风「灵潮五行签文」纯趣味结果（确定性派生、零战力数值）。</summary>
    public readonly struct WuxingFortuneResult
    {
        public string DateKeyUtc8 { get; }
        public string ElementName { get; }
        public string SignGrade { get; }
        public string Verse { get; }
        public string TravelHint { get; }
        public string OverheadTag { get; }

        public WuxingFortuneResult(string dateKeyUtc8, string elementName, string signGrade,
            string verse, string travelHint, string overheadTag)
        {
            DateKeyUtc8 = dateKeyUtc8;
            ElementName = elementName;
            SignGrade = signGrade;
            Verse = verse;
            TravelHint = travelHint;
            OverheadTag = overheadTag;
        }
    }

    /// <summary>人物「江湖名帖」只读快照（全部由服务端权威面板派生，零额外数值）。</summary>
    public sealed class CharacterCallingCardView
    {
        public string RoleName { get; }
        public uint Level { get; }
        public string CharacterArchiveName { get; }
        public string AppearanceId { get; }
        public string BuildStyleTag { get; }
        public string CompanionOathPhrase { get; }
        public int OwnedCharacterCount { get; }
        public int TotalCharacterCount { get; }
        public IReadOnlyList<string> CompletedSeriesBadges { get; }
        public IReadOnlyList<string> SeasonHonorTitles { get; }
        public int TrialSealCount { get; }
        public string ActivePetName { get; }
        public int ClearedPuzzleCount { get; }
        public int ClearedTaleCount { get; }
        public int ArchivedErrandCount { get; }

        public CharacterCallingCardView(
            string roleName,
            uint level,
            string characterArchiveName,
            string appearanceId,
            string buildStyleTag,
            string companionOathPhrase,
            int ownedCharacterCount,
            int totalCharacterCount,
            IReadOnlyList<string> completedSeriesBadges,
            IReadOnlyList<string> seasonHonorTitles,
            int trialSealCount,
            string activePetName,
            int clearedPuzzleCount,
            int clearedTaleCount,
            int archivedErrandCount)
        {
            RoleName = string.IsNullOrWhiteSpace(roleName) ? "归元行者" : roleName;
            Level = level;
            CharacterArchiveName = string.IsNullOrWhiteSpace(characterArchiveName) ? "归元游侠" : characterArchiveName;
            AppearanceId = appearanceId ?? "";
            BuildStyleTag = string.IsNullOrWhiteSpace(buildStyleTag) ? "五行均衡" : buildStyleTag;
            CompanionOathPhrase = companionOathPhrase ?? "";
            OwnedCharacterCount = ownedCharacterCount;
            TotalCharacterCount = totalCharacterCount;
            CompletedSeriesBadges = completedSeriesBadges ?? Array.Empty<string>();
            SeasonHonorTitles = seasonHonorTitles ?? Array.Empty<string>();
            TrialSealCount = trialSealCount;
            ActivePetName = string.IsNullOrWhiteSpace(activePetName) ? "尚未携宠" : activePetName;
            ClearedPuzzleCount = clearedPuzzleCount;
            ClearedTaleCount = clearedTaleCount;
            ArchivedErrandCount = archivedErrandCount;
        }

        public string FormatShareText()
        {
            var sb = new StringBuilder();
            sb.Append("【五行奇谈 · 江湖名帖】").Append(RoleName);
            if (Level > 0) sb.Append(" · Lv.").Append(Level);
            sb.Append("｜身披：").Append(CharacterArchiveName);
            sb.Append("｜流派：").Append(BuildStyleTag);
            sb.Append("｜得意灵宠：").Append(ActivePetName);
            sb.Append("｜衣柜收集：").Append(OwnedCharacterCount).Append('/').Append(Math.Max(1, TotalCharacterCount));
            if (CompletedSeriesBadges.Count > 0)
                sb.Append("｜系列印章：").Append(string.Join("、", CompletedSeriesBadges));
            if (SeasonHonorTitles.Count > 0)
                sb.Append("｜赛季铭牌：").Append(string.Join("、", SeasonHonorTitles));
            sb.Append("｜演武残局：").Append(ClearedPuzzleCount).Append("/3");
            sb.Append("｜归元奇谈：").Append(ClearedTaleCount).Append("/3");
            if (!string.IsNullOrWhiteSpace(CompanionOathPhrase))
                sb.Append("｜同行之约：「").Append(CompanionOathPhrase).Append('」');
            return sb.ToString();
        }
    }

    public readonly struct SeriesCoopBubbleLine
    {
        public string SeriesName { get; }
        public string SpeakerAppearanceId { get; }
        public string PartnerAppearanceId { get; }
        public string Line { get; }

        public SeriesCoopBubbleLine(string seriesName, string speakerAppearanceId, string partnerAppearanceId, string line)
        {
            SeriesName = seriesName;
            SpeakerAppearanceId = speakerAppearanceId;
            PartnerAppearanceId = partnerAppearanceId;
            Line = line;
        }
    }

    /// <summary>归元城 8 位原创核心 NPC 目录与纯规则推导（零 Unity 依赖，零第三方版权旧名）。</summary>
    public static class CityNpcCatalog
    {
        public const uint GuiyuanCitySceneConfigId = 1u;
        public const uint DummySingleDungeonId = 301u;
        public const uint DummyGroupDungeonId = 302u;
        public const float DefaultInteractRange = 14f;

        /// <summary>莫听风纯外观「同行之约」官方预设短语库（无自由输入 UGC 风险、零战力加成）。</summary>
        public static readonly IReadOnlyList<string> CompanionOathPresets = new[]
        {
            "清风伴剑，有空共游归元城",
            "不赶进度，十来分钟打一局正好",
            "残局切磋，胜负都记进败局册",
            "灵潮将起，结伴往八景岛寻宠",
            "茶凉莫催，先看这一手五行相生",
            "留灯一盏，专候夜归同行道友",
            "药香伴琴，后排疗盾包在我身上",
            "潮起月汐，顺路捎信一并出发"
        };

        /// <summary>主城已验单连通地标站位（全部取自 biyuan-body-20261123/navigation.json component=0，且与神秘商人 6 点零重合）。</summary>
        public static readonly IReadOnlyList<CityNpcDefinition> Npcs = new[]
        {
            new CityNpcDefinition(
                "guiyuan_smith", "钟百炼", "器坊铸师", "西廊炉坊口", 90.0f, 94.75f,
                "11_jade_fist_flat_top_boy",
                "打铁讲究火候，换流派讲究轻快。首套装备升档、打磨品相、拆技制书，在炉坊都能原件保底。",
                "炉火正旺，旧装原地升档绝不损强化！",
                CityNpcFeatureKind.None,
                new[]
                {
                    new CityNpcShortcut("装备打造与背包", CityNpcShortcutKind.EquipmentForge),
                    new CityNpcShortcut("整理行囊", CityNpcShortcutKind.Bag)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾来了？你那金葫芦的铜扣磨亮了不少，屋顶漏雨的瓦买齐没？",
                    ["01_ice_sword_girl"] = "霜姑娘这柄寒剑的刃口稳得很，天冷别总自己扫演武台暗冰。",
                    ["02_fire_talisman_boy"] = "小炽，画赤符别总把桌角点着！烤栗子的火候倒比我炉子还准。",
                    ["03_lotus_healer_girl"] = "苏大夫，上次帮炉坊学徒配的烫伤膏极好用，新打的药剪给您留着呢。",
                    ["05_celestial_musician_girl"] = "林姑娘路过炉坊怕是嫌铁锤太吵，回头我给您校一副铜琴轸。",
                    ["09_bamboo_archer_girl"] = "听雨姑娘的青竹弓韧性好，箭簇要轻不要重，海风里才不偏。",
                    ["10_crimson_spear_girl"] = "停云姑娘！枪缨上那股旧船绳我都替你绕稳了，论剑场上放心出枪。",
                    ["15_water_dragon_scholar_boy"] = "沈书生又来记各家兵刃破绽了？茶凉了别怪炉火太旺。",
                    ["20_star_formation_master_girl"] = "星璇宗师大驾光临，这炉坊凡铁倒显得拙朴，十八星印果然气象非凡。"
                }),
            new CityNpcDefinition(
                "guiyuan_merchant", "宋满仓", "牙行掌柜", "南中廊市口", 118.75f, 105.0625f,
                "21_lidazui_hair_cook_boy",
                "归元城做买卖讲究明码实价、双边托管。万象阁流通可交易货，百宝阁直售永久收藏与轻便利，绝不设套路。",
                "万象阁明码托管，游历手记零打卡随时补领！",
                CityNpcFeatureKind.None,
                new[]
                {
                    new CityNpcShortcut("打开万象阁集市", CityNpcShortcutKind.MarketTrade),
                    new CityNpcShortcut("灵石百宝阁", CityNpcShortcutKind.SpiritShop),
                    new CityNpcShortcut("青云游历手记", CityNpcShortcutKind.TravelPass)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾，你们家面铺的葱姜账本我替你对清了，这回可不许再顺手画个碗蒙混过去！",
                    ["01_ice_sword_girl"] = "霜姑娘挑东西最干脆，从不讨价还价，今日可要看看衣柜里新到的剑穗特效？",
                    ["02_fire_talisman_boy"] = "小炽，朱砂坊的新墨刚入库，别又拿废符在柜台边烘板栗。",
                    ["03_lotus_healer_girl"] = "苏大夫，药庐订的细棉绳全送到啦，单结双结够系一整季。",
                    ["05_celestial_musician_girl"] = "林姑娘来了，铺子里刚收了两轴松木琴弦，您抽空试个音。",
                    ["09_bamboo_archer_girl"] = "听雨姑娘，去八景岛的渡船时刻我贴在铺门外了，这回可别再把船票让出去。",
                    ["10_crimson_spear_girl"] = "停云姑娘，听说你正攒银两修渡船？把闲置可交易装挂万象阁，省心又防骗。",
                    ["15_water_dragon_scholar_boy"] = "沈公子，上回托我留的湖笔和空白册页都包好了，管够你记满百场战局。",
                    ["20_star_formation_master_girl"] = "星璇宗师不图重金只认破阵实绩，今日来市口可是替新人挑件顺手法宝？"
                }),
            new CityNpcDefinition(
                "guiyuan_sparring", "齐问锋", "演武执事", "中庭北阶", 121.25f, 161.0f,
                "10_crimson_spear_girl",
                "换完加点、战法和灵宠，别急着硬闯排位——先在我这儿敲两轮铁甲木桩验验出手序和伤害，或者破三道演武残局热热手。",
                "演武木桩无损试招，三道回合残局随时破阵！",
                CityNpcFeatureKind.SparringAndPuzzles,
                new[]
                {
                    new CityNpcShortcut("人宠会武 · 论剑", CityNpcShortcutKind.FormalPvp),
                    new CityNpcShortcut("道友切磋坊", CityNpcShortcutKind.CustomDuel),
                    new CityNpcShortcut("属性加点方案", CityNpcShortcutKind.Attributes)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾，别光顾着替旁人挡风，今天先试一把保护宝宝的残局，记牢何时该求援。",
                    ["01_ice_sword_girl"] = "霜姑娘剑意极寒，正好拿高甲铁木桩测测穿防词条和先手冰控的节奏。",
                    ["02_fire_talisman_boy"] = "小炽，三才群桩站好了，来试试你这套火符群攻加追击能打几段爆发！",
                    ["03_lotus_healer_girl"] = "苏大夫来演武场正好，三才桩能实时验你治疗与太清灵盾的叠加实效。",
                    ["05_celestial_musician_girl"] = "林姑娘的清音辅控最吃出手顺序，拿木桩测一眼速度差最稳妥。",
                    ["09_bamboo_archer_girl"] = "听雨，单点破核讲究耐住性子等破绽，你最擅长等那一箭的时机。",
                    ["10_crimson_spear_girl"] = "停云！败局册又添新页了？别急着抢攻，去把残局三关的断疗步法走一遍。",
                    ["15_water_dragon_scholar_boy"] = "沈书生别光在台下画脚印，上台拿真实配装测两回合人宠五行相生！",
                    ["20_star_formation_master_girl"] = "星璇宗师莅临演武阶，不妨替后辈试试这三道残局火候够不够。"
                }),
            new CityNpcDefinition(
                "guiyuan_elder", "玄衡子", "观星长老", "钟鼓阶前", 241.25f, 172.25f,
                "20_star_formation_master_girl",
                "五行生克循环不息，战法装配讲求克制与协防。首领试炼、五行幻境与试炼志皆在此处接引，无日清枷锁，有空便来求道。",
                "观星推演五行相生，首领试炼与幻境随时启阵。",
                CityNpcFeatureKind.None,
                new[]
                {
                    new CityNpcShortcut("首领试炼", CityNpcShortcutKind.BossTrial),
                    new CityNpcShortcut("五行幻境爬塔", CityNpcShortcutKind.WuxingTower),
                    new CityNpcShortcut("五行试炼志", CityNpcShortcutKind.TrialJournal)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "陆家小子心性淳厚，打首领时切记先护灵宠、再伺慢重劈破绽。",
                    ["01_ice_sword_girl"] = "玄霜剑气与五行金水最合拍，去幻境十层挑几枚仙品符印试试剑势吧。",
                    ["02_fire_talisman_boy"] = "许炽小友火性刚烈，记住遇水克之时切不可硬顶，换阵法方是妙策。",
                    ["03_lotus_healer_girl"] = "青莲济世，破阵首领二阶段最缺你这般稳健驱散与回春。",
                    ["05_celestial_musician_girl"] = "知音一曲能定神清障，玄毒首领的封控阵法正该由你化解。",
                    ["09_bamboo_archer_girl"] = "听雨静如岸鸟，破阵先断回春卫，正合你的百步穿杨。",
                    ["10_crimson_spear_girl"] = "祝家姑娘锐气可嘉，试炼志十八战法印讲究攻守巧兼修，不可偏废。",
                    ["15_water_dragon_scholar_boy"] = "沈澄又带着阵图来了？今日周常五行词缀轮转，你且对照星盘一观。",
                    ["20_star_formation_master_girl"] = "星璇归位！观星台铜盘犹温，归元城的风，今夜随你细赏。"
                }),
            new CityNpcDefinition(
                "guiyuan_constable", "顾青崖", "巡城执令", "中央廊道", 193.75f, 169.4375f,
                "07_moon_shadow_assassin_girl",
                "归元城内永为和平安全区，城外三图恩怨帮战各凭本事。近来巷口又添了几桩奇谈怪事，若你有十分钟闲暇，可愿替我走一趟？",
                "归元城永久免战；城中奇谈短案随时可查！",
                CityNpcFeatureKind.CharacterTale,
                new[]
                {
                    new CityNpcShortcut("帮会与全天帮战", CityNpcShortcutKind.GuildWar),
                    new CityNpcShortcut("查看战法技能", CityNpcShortcutKind.Skills)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾正好！留灯院巷口昨夜有纸人搬书，街坊都说你最熟那条巷子，随我去瞧一眼。",
                    ["01_ice_sword_girl"] = "霜姑娘夜巡时可曾见过黑影掠过钟鼓亭？有你在，城北安稳多了。",
                    ["02_fire_talisman_boy"] = "小炽，城里巡夜禁放爆裂赤符，想捉怪去奇谈案卷里放开手脚！",
                    ["03_lotus_healer_girl"] = "苏大夫，昨晚追捕匪徒擦伤的两名巡卫刚从你药庐出来，多谢照应。",
                    ["05_celestial_musician_girl"] = "林姑娘耳力过人，巷尾那阵怪异风铃声，怕只有你能辨出虚实。",
                    ["09_bamboo_archer_girl"] = "听雨姑娘辨声识踪的本事，比司署半数探子都强。",
                    ["10_crimson_spear_girl"] = "停云姑娘若在野外遇异帮挑衅，记住和平冷却与反杀撕单规矩，别吃暗亏。",
                    ["15_water_dragon_scholar_boy"] = "沈书生，你那本《奇谈夹页》记的巷口纸人案，今日终于能去亲手查个水落石出了。",
                    ["20_star_formation_master_girl"] = "有星璇宗师坐镇中庭，连最棘手的悬赏逃犯也不敢靠近归元城半步。"
                }),
            new CityNpcDefinition(
                "guiyuan_herbalist", "温半夏", "百草堂主", "东廊药铺口", 255.3125f, 144.75f,
                "03_lotus_healer_girl",
                "灵宠随主同修、手动出招才见灵性。药庐备齐了灵兽养护与技能条目指引，另有一桩寒霜试药奇谈，闲时不妨来搭把手。",
                "灵宠五阶同档封顶，药庐奇谈随时可试演。",
                CityNpcFeatureKind.CharacterTale,
                new[]
                {
                    new CityNpcShortcut("灵宠养成与幻化", CityNpcShortcutKind.Pets),
                    new CityNpcShortcut("战法技能书", CityNpcShortcutKind.Skills)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾，你祖母膝盖贴的温经膏我又多备了两副，顺手拎回去。",
                    ["01_ice_sword_girl"] = "霜姑娘寒气入脉，切记练完剑喝半盏姜枣茶暖手。",
                    ["02_fire_talisman_boy"] = "小炽，上次被朱砂熏红眼的滴眼露还够用不？别硬扛着画符。",
                    ["03_lotus_healer_girl"] = "青莲回来啦！你系的双结安神散阿婆赞不绝口，药庐后院那尊试药寒傀正等你验方。",
                    ["05_celestial_musician_girl"] = "知音姑娘快来竹廊坐，雨刚停，药香正衬你的七弦泛音。",
                    ["09_bamboo_archer_girl"] = "听雨，这是防海风皲裂的润手膏，出海画鸟谱记得带在竹筒里。",
                    ["10_crimson_spear_girl"] = "停云练枪虎口易震，这瓶跌打清露拿着，输赢都别伤着手腕。",
                    ["15_water_dragon_scholar_boy"] = "沈书生熬夜校战报最伤目力，来抓把杭白菊泡茶喝。",
                    ["20_star_formation_master_girl"] = "星璇夜观星象露重风凉，这包安神莲心请一定收下。"
                }),
            new CityNpcDefinition(
                "guiyuan_ferryman", "周行舟", "驿渡管事", "南桥渡口", 240.0f, 38.5f,
                "19_spirit_beast_tamer_boy",
                "归元城通八景岛、月汐湾与碧渊水宫，灵潮每时辰轮换不催人。你若正好要出海寻宠打本，顺路替街坊捎个信物也挺有意思。",
                "三图灵潮轮转直传，不限时顺路捎物随时可接！",
                CityNpcFeatureKind.LightErrands,
                new[]
                {
                    new CityNpcShortcut("大世界灵潮地图", CityNpcShortcutKind.WorldMap),
                    new CityNpcShortcut("灵宠栖息图鉴", CityNpcShortcutKind.Pets)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾，真打算补好瓦就提着包出门当一回客人？渡口随时给你留头舱。",
                    ["01_ice_sword_girl"] = "霜姑娘要往碧渊水宫走一遭吗？水宫石阶湿滑，倒正合你的步法。",
                    ["02_fire_talisman_boy"] = "小炽上船可收好火符，船板刚刷的桐油，最怕火星子！",
                    ["03_lotus_healer_girl"] = "苏大夫寄往碧渊水宫茶亭的安神莲心包我已经装盒，顺路哪位道友都能捎去。",
                    ["05_celestial_musician_girl"] = "林姑娘乘舟听潮最惬意，八景岛风清，正宜抚琴。",
                    ["09_bamboo_archer_girl"] = "听雨！这班去八景岛的渡船靠岸了，靠舷这一侧风最小、最适合画岸鸟，真不上去看看？",
                    ["10_crimson_spear_girl"] = "停云，月汐湾潮汐快退了，你爹托我捎的旧渡船新麻缆就搁渡口，顺道带回去吧！",
                    ["15_water_dragon_scholar_boy"] = "沈书生往返八景岛与归元城比船夫还勤，今天顺风，半盏茶工夫就能靠岸。",
                    ["20_star_formation_master_girl"] = "星璇宗师出行无需看潮候，渡口舟楫随时听候调遣。"
                }),
            new CityNpcDefinition(
                "guiyuan_storyteller", "莫听风", "茶肆说书人", "桥畔茶亭", 186.25f, 117.25f,
                "15_water_dragon_scholar_boy",
                "留灯院旧友、水火冰锋、清音济世——归元城最动人的不是神兵数值，是人情奇谈。来求一支今日五行签，或把你的江湖名帖挂上茶壁吧！",
                "求支灵潮五行签，亮出你的江湖名帖与同行之约！",
                CityNpcFeatureKind.FortuneAndCallingCard,
                new[]
                {
                    new CityNpcShortcut("永久衣柜与人物志", CityNpcShortcutKind.Appearance),
                    new CityNpcShortcut("五行试炼志", CityNpcShortcutKind.TrialJournal)
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["00_reference_topright_boy"] = "阿禾来了！快坐，今儿说书正讲到《第四只碗》，听众都在问留灯院面馆放不放葱呢。",
                    ["01_ice_sword_girl"] = "霜姑娘，《霜阶扫雪》这一折可把大伙儿听暖了，来喝碗热茶歇歇脚。",
                    ["02_fire_talisman_boy"] = "小炽，茶棚炉子借你烤栗子，换你讲讲《焦边朱砂》里后来那张符成没成！",
                    ["03_lotus_healer_girl"] = "苏大夫一进茶亭，街坊们心就先安了一半，《药包双结》如今已是归元城佳话。",
                    ["05_celestial_musician_girl"] = "林姑娘若肯在茶亭拨两声散音，我这说书醒木怕是要失业咯。",
                    ["09_bamboo_archer_girl"] = "听雨姑娘，你画册里《没有画的那一页》，今天要不要添上第一笔八景岛的青羽？",
                    ["10_crimson_spear_girl"] = "停云姑娘！那句「今天纸赢了，你讲」如今全城武夫都爱挂在嘴边。",
                    ["15_water_dragon_scholar_boy"] = "沈兄又来茶亭对笔记了？趁这半盏清茶未凉，替我审审新编的归元名帖录。",
                    ["20_star_formation_master_girl"] = "十八星印集齐之日，便是归元城新折开书之时——星璇宗师请上座！"
                })
        };

        /// <summary>演武木桩（301–302）、回合残局（311–313）与归元人物奇谈（321–323）。</summary>
        public static readonly IReadOnlyList<CityNpcEncounterDefinition> Encounters = new[]
        {
            new CityNpcEncounterDefinition(
                301u, CityNpcEncounterCategory.PracticeDummy, "guiyuan_sparring",
                "演武单桩 · 铁甲破防", "本人真实养成 · 8 回合无损验招 · 零消耗零刷取",
                "单尊高护甲演武铁木桩（低攻不威胁生存），专用于校验人物单体爆发、穿防词条、连击与人宠五行相生收益。",
                "", "", "铁甲验招", false, 0u, 0u),
            new CityNpcEncounterDefinition(
                302u, CityNpcEncounterCategory.PracticeDummy, "guiyuan_sparring",
                "演武三才 · 群攻控盾", "本人真实养成 · 8 回合无损验招 · 零消耗零刷取",
                "三尊协同演武木桩阵，专用于实测群体伤害、出手速序、控制命中与治疗/太清灵盾的多目标覆盖效果。",
                "", "", "三才验招", false, 0u, 0u),
            new CityNpcEncounterDefinition(
                311u, CityNpcEncounterCategory.TacticalPuzzle, "guiyuan_sparring",
                "残局一 · 破甲试锋", "5 回合速决考题 · 可选本人养成或免费 3×3 预设",
                "前排高甲主傀护持脆弱策应卫。先清策应卫还是强破主甲？在 5 回合内完成击破即可点亮残局印记。",
                "演武复盘 · 破甲要点", "面对高护甲主目标与低血脆皮副目标，优先点杀副目标减少每回合承伤，再借人宠相生合击收尾最为稳妥。",
                "明策 · 破甲", true, 1u, 4u),
            new CityNpcEncounterDefinition(
                312u, CityNpcEncounterCategory.TacticalPuzzle, "guiyuan_sparring",
                "残局二 · 护主保宠", "5 回合抗压考题 · 可选本人养成或免费 3×3 预设",
                "敌将爆发凌厉，单靠硬拼易损灵宠。善用防御、护盾与控场保住人宠双线输出节奏，5 回合内稳中求胜。",
                "演武复盘 · 保核要点", "爆发型对手前两回合威胁最大，用太清灵盾或防御吃下首波锋芒后反打，往往比盲目抢攻胜率高出数倍。",
                "明策 · 护主", true, 3u, 6u),
            new CityNpcEncounterDefinition(
                313u, CityNpcEncounterCategory.TacticalPuzzle, "guiyuan_sparring",
                "残局三 · 先手断疗", "5 回合博弈考题 · 可选本人养成或免费 3×3 预设",
                "敌阵自带回春医傀与守阵傀儡。把握先手控制或集火断疗窗口，防止敌方拖满 5 回合。",
                "演武复盘 · 断疗要点", "遭遇回复型敌阵时，先手控住或集火压低医傀血线，切忌分散火力让敌方白白续航。",
                "明策 · 断疗", true, 2u, 5u),
            new CityNpcEncounterDefinition(
                321u, CityNpcEncounterCategory.CharacterTale, "guiyuan_constable",
                "奇谈一 · 留灯夜话（巷口纸人案）", "3 分钟原创短案 · 可跳过对白 · 支持免费预设试演",
                "留灯院巷口深夜屡见短腿纸人搬书。顾青崖邀你同陆照禾、沈澄循着墨香与水痕查探，原是旧书铺借还无名的灵纸作祟——先稳住受惊的纸人傀，再听它写下名字。",
                "隐藏续篇 ·《借还栏新页》",
                "纸人傀平息后，踮脚在沈澄递去的白纸夹页上歪歪扭扭按下一枚朱砂指印。阿禾提来两碗不放葱的热面，把门边木牌翻到正面：『夜客借书，天亮归还，碗留灯下。』自那以后，归元城印铺最底层总多了一盏长明小灯。",
                "见闻 · 留灯夜话", true, 2u, 5u, "00_reference_topright_boy"),
            new CityNpcEncounterDefinition(
                322u, CityNpcEncounterCategory.CharacterTale, "guiyuan_herbalist",
                "奇谈二 · 药庐双结（寒霜试药案）", "3 分钟原创短案 · 可跳过对白 · 支持免费预设试演",
                "城东药庐后院一尊借寒露炼药的木傀忽生异变，错把安神莲心当成烈寒毒霜。温半夏请来苏青莲与林知音以清音安神，须由你稳住试药傀并驱散寒毒。",
                "隐藏续篇 ·《竹廊余温》",
                "寒傀归静，药炉上的莲心茶恰好滚开。苏青莲在药包绳上添了第三道细结：『这一包留给练完剑手凉的霜姑娘。』廊下七弦轻拨，檐外灰雀竟也跟着落回青莲钵边啄水。",
                "见闻 · 药庐双结", true, 3u, 6u, "03_lotus_healer_girl"),
            new CityNpcEncounterDefinition(
                323u, CityNpcEncounterCategory.CharacterTale, "guiyuan_ferryman",
                "奇谈三 · 月汐渡口（潮汐归舟案）", "3 分钟原创短案 · 可跳过对白 · 支持免费预设试演",
                "退潮时分，月汐湾归舟被一只贪恋旧船铃的潮汐礁妖拦在浅滩。周行舟与祝停云、桑听雨急寻援手，只需一局巧战驱离礁妖，护送渡船安稳靠岸。",
                "隐藏续篇 ·《第一根缆绳》",
                "渡船稳稳泊进码头，停云把湿透的船绳重新系上桩头，回头看见听雨正坐在船舷边画下第一只青脚岸鸟。『明日去八景岛的票，我这回自己买了。』听雨把竹筒合紧，笑着朝归元城方向挥了挥手。",
                "见闻 · 月汐归舟", true, 1u, 4u, "10_crimson_spear_girl")
        };

        /// <summary>周行舟 3 条不限时顺路捎物轻委托。</summary>
        public static readonly IReadOnlyList<CityNpcErrandDefinition> Errands = new[]
        {
            new CityNpcErrandDefinition(
                "errand_bajing_ink", "八景岛 · 鸟谱青墨", 2u, "八景岛",
                "替桑听雨捎一盒调好的青松墨去八景岛，不限时，顺路登岛抓宠或打本即可送达。",
                "青松墨已稳稳留在八景岛亭台石案上，听雨的鸟谱终于添上了远岛第一笔青羽。"),
            new CityNpcErrandDefinition(
                "errand_yuexi_rope", "月汐湾 · 渡船麻缆", 3u, "月汐湾",
                "替祝停云家捎一捆新编麻缆去月汐湾码头，不限时，顺路赶海或抓宠即可送达。",
                "新麻缆系上了旧渡船，船家夫妇连声道谢，还往你袖里塞了一枚海风贝壳。"),
            new CityNpcErrandDefinition(
                "errand_biyuan_tea", "碧渊水宫 · 安神莲心", 5u, "碧渊水宫",
                "替苏青莲捎一包双结安神莲心去碧渊水宫茶亭，不限时，顺路游历即可送达。",
                "莲心清香漫过水宫茶亭，恰逢钟鼓微鸣，远近道友皆感神清气爽。")
        };

        /// <summary>收藏系列队友同框 2 秒互动气泡（零数值羁绊，纯社交与收藏表达）。</summary>
        public static readonly IReadOnlyList<SeriesCoopBubbleLine> SeriesCoopBubbles = new[]
        {
            new SeriesCoopBubbleLine("留灯院旧友", "00_reference_topright_boy", "10_crimson_spear_girl", "祝姑娘，今天后排交给我，回头一起去吃不放葱的面！"),
            new SeriesCoopBubbleLine("留灯院旧友", "10_crimson_spear_girl", "00_reference_topright_boy", "阿禾管好你的金葫芦，这局咱们堂堂正正赢下来！"),
            new SeriesCoopBubbleLine("留灯院旧友", "09_bamboo_archer_girl", "00_reference_topright_boy", "阿禾，鸟谱空着的那页我已经动笔啦！"),
            new SeriesCoopBubbleLine("留灯院旧友", "10_crimson_spear_girl", "09_bamboo_archer_girl", "听雨稳住节奏，前面有我替你开路！"),
            new SeriesCoopBubbleLine("水火冰锋", "01_ice_sword_girl", "02_fire_talisman_boy", "许炽，收着点火星，别又把石阶烤裂。"),
            new SeriesCoopBubbleLine("水火冰锋", "02_fire_talisman_boy", "01_ice_sword_girl", "放心霜姑娘！这回朱砂火候准得很，冰火配合看我的！"),
            new SeriesCoopBubbleLine("水火冰锋", "15_water_dragon_scholar_boy", "01_ice_sword_girl", "茶还温着，霜姑娘先手冰封，我来补水势收局。"),
            new SeriesCoopBubbleLine("水火冰锋", "15_water_dragon_scholar_boy", "02_fire_talisman_boy", "小炽沉住气留半寸回旋，这一局必胜！"),
            new SeriesCoopBubbleLine("清音济世", "03_lotus_healer_girl", "05_celestial_musician_girl", "知音只管抚琴控场，血线与驱散交给我！"),
            new SeriesCoopBubbleLine("清音济世", "05_celestial_musician_girl", "03_lotus_healer_girl", "药香衬琴音，青莲放心，今夜无人能近你身！")
        };

        public static CityNpcDefinition FindNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            for (int i = 0; i < Npcs.Count; ++i)
                if (string.Equals(Npcs[i].Id, npcId, StringComparison.Ordinal)) return Npcs[i];
            return null;
        }

        public static CityNpcEncounterDefinition FindEncounter(uint dungeonConfigId)
        {
            for (int i = 0; i < Encounters.Count; ++i)
                if (Encounters[i].DungeonConfigId == dungeonConfigId) return Encounters[i];
            return null;
        }

        public static List<CityNpcEncounterDefinition> FindEncountersForNpc(string npcId)
        {
            var result = new List<CityNpcEncounterDefinition>();
            if (string.IsNullOrEmpty(npcId)) return result;
            for (int i = 0; i < Encounters.Count; ++i)
                if (string.Equals(Encounters[i].HostNpcId, npcId, StringComparison.Ordinal))
                    result.Add(Encounters[i]);
            return result;
        }

        public static CityNpcErrandDefinition FindErrand(string errandId)
        {
            if (string.IsNullOrEmpty(errandId)) return null;
            for (int i = 0; i < Errands.Count; ++i)
                if (string.Equals(Errands[i].Id, errandId, StringComparison.Ordinal)) return Errands[i];
            return null;
        }

        public static string ResolveMapDisplayName(uint sceneConfigId) => sceneConfigId switch
        {
            1u => "归元城",
            2u => "八景岛",
            3u => "月汐湾",
            4u => "青石渡",
            5u => "碧渊水宫",
            _ => "归元界"
        };

        /// <summary>按 (playerId, UTC+8 日期, 当前灵潮图) 确定性求取五行签文（纯趣味、零属性）。</summary>
        public static WuxingFortuneResult DrawWuxingFortune(ulong playerId, ulong serverNowMs, uint tideSceneConfigId)
        {
            long utc8Ms = (long)serverNowMs + 8L * 3600_000L;
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0L, utc8Ms));
            string dateKey = $"{dt.Year:D4}-{dt.Month:D2}-{dt.Day:D2}";
            ulong dayOrdinal = (ulong)(dt.Year * 10000 + dt.Month * 100 + dt.Day);
            ulong mix = playerId ^ (dayOrdinal * 0x9E3779B97F4A7C15UL) ^ ((ulong)tideSceneConfigId * 0xBF58476D1CE4E5B9UL);
            mix ^= mix >> 30;
            mix *= 0xBF58476D1CE4E5B9UL;
            mix ^= mix >> 27;
            mix *= 0x94D049BB133111EBUL;
            mix ^= mix >> 31;

            int elementIdx = (int)(mix % 5UL);
            string[] elements = { "金象 · 锐锋签", "木象 · 长青签", "水象 · 润泽签", "火象 · 昭明签", "土象 · 厚载签" };
            string[] grades = { "上上吉", "上吉", "中平吉", "和顺吉", "清宁吉" };
            string[] verses =
            {
                "锋芒藏鞘待时机，演武木桩验破防，一击破阵不贪刀。",
                "青莲生息春意长，人宠相生协防稳，残局自有回天策。",
                "潮汐顺流赴远湾，水色空明好寻宠，切磋一局话平生。",
                "朱砂赤符映夜灯，群攻连携势如虹，借势破局最痛快。",
                "厚土载物定道心，慢劈临身巧施盾，守得云开见月明。"
            };
            string[] tags = { "【金签 · 破阵有锋】", "【木签 · 青莲顺遂】", "【水签 · 顺风寻宠】", "【火签 · 留灯吉运】", "【土签 · 磐石安泰】" };
            string tideHint = tideSceneConfigId > 1u
                ? $"此刻灵潮正涌「{ResolveMapDisplayName(tideSceneConfigId)}」，有空不妨往周行舟处直传一游。"
                : "此刻城外灵潮暂歇，正宜在归元城演武试招、翻读人物奇谈。";
            int gradeIdx = (int)((mix >> 8) % (ulong)grades.Length);
            return new WuxingFortuneResult(dateKey, elements[elementIdx], grades[gradeIdx], verses[elementIdx], tideHint, tags[elementIdx]);
        }

        /// <summary>根据权威属性面板与辅助疗盾加成，确定性推导人物「擅长流派标签」（零数值影响）。</summary>
        public static string DeriveBuildStyleTag(global::AttributePanelInfo panel)
        {
            if (panel == null) return "五行均衡";
            ulong body = 0, spirit = 0, power = 0, agility = 0;
            for (int i = 0; i < panel.Dimensions.Count; ++i)
            {
                var d = panel.Dimensions[i];
                if (d == null) continue;
                string name = d.Name ?? "";
                if (name.Contains("体")) body += d.Allocated;
                else if (name.Contains("灵")) spirit += d.Allocated;
                else if (name.Contains("力")) power += d.Allocated;
                else if (name.Contains("敏")) agility += d.Allocated;
            }
            double healBps = panel.SupportEffects?.Healing?.Complete == true ? panel.SupportEffects.Healing.TotalBonusBps : 0d;
            double shieldBps = panel.SupportEffects?.Shield?.Complete == true ? panel.SupportEffects.Shield.TotalBonusBps : 0d;
            if (healBps + shieldBps >= 2000d && body + spirit >= power) return "济世护主";
            ulong total = body + spirit + power + agility;
            if (total == 0) return "初入归元";
            ulong max = Math.Max(Math.Max(body, spirit), Math.Max(power, agility));
            if (max * 100UL < total * 40UL) return "五行均衡";
            if (max == power) return "锋芒力破";
            if (max == spirit) return "玄法通灵";
            if (max == agility) return "疾敏先手";
            return "固本玄守";
        }

        /// <summary>匹配同屏/同队收藏系列人物，生成 2 秒同游互动气泡（零数值羁绊）。</summary>
        public static bool TryMatchSeriesCoopBubble(string localAppearanceId, IReadOnlyList<string> companionAppearanceIds,
            int seed, out SeriesCoopBubbleLine matched)
        {
            matched = default;
            if (string.IsNullOrEmpty(localAppearanceId) || companionAppearanceIds == null || companionAppearanceIds.Count == 0)
                return false;
            var candidates = new List<SeriesCoopBubbleLine>();
            for (int i = 0; i < SeriesCoopBubbles.Count; ++i)
            {
                var line = SeriesCoopBubbles[i];
                if (!string.Equals(line.SpeakerAppearanceId, localAppearanceId, StringComparison.Ordinal))
                    continue;
                for (int j = 0; j < companionAppearanceIds.Count; ++j)
                {
                    if (string.Equals(companionAppearanceIds[j], line.PartnerAppearanceId, StringComparison.Ordinal))
                    {
                        candidates.Add(line);
                        break;
                    }
                }
            }
            if (candidates.Count == 0) return false;
            int idx = Math.Abs(seed) % candidates.Count;
            matched = candidates[idx];
            return true;
        }
    }
}
