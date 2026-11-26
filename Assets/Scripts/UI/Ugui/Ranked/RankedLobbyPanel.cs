using System;
using System.Collections.Generic;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Team;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Ranked.RankedArtCatalog;

namespace MmorpgClient.UI.Ugui.Ranked
{
    using Transform = UnityEngine.Transform;
    /// <summary>
    /// 论剑排位大厅主画面（1920x1080 设计坐标系，由外层 EquipmentUiFit 适配屏幕安全区）：
    /// - 严格落实 GEMINI_HANDOFF.md §3 与 state-layout.json 的全部 8 态与槽位；
    /// - 左栏：段位徽章与分值卡、赛季累计场次 4/8/12 三档展示权益预览卡；
    /// - 右栏：1V1–5V5 人数档页签、队伍与就地排队状态条、3 人物战法卡、3 宝宝战法卡（独立选择，零宠账号可用默认竞技宝宝）、
    ///   主操作按钮与仅在 120s 超时提示态出现的左侧次取消按钮。
    /// </summary>
    public sealed class RankedLobbyPanel
    {
        private sealed class PresetCardView
        {
            public uint PresetId;
            public Button Button;
            public Image Bg;
            public Image Portrait;
            public Image CheckMark;
            public TMP_Text Title;
            public TMP_Text Sub;
        }

        public event Action CloseRequested;
        public event Action AppearanceEntryRequested;
        public event Action<RankedDrawerTab> DrawerRequested;
        public event Action OpenTeamRequested;
        public event Action<uint> TeamSizeSelected;
        public event Action<uint, uint> PresetSelected; // (charPresetId, petPresetId)
        public event Action TeamConfirmPresetRequested;
        public event Action PrimaryActionClicked;
        public event Action TimeoutCancelClicked;

        private readonly RectTransform _root;
        private readonly TMP_Text _seasonSubtitle;
        private readonly Image _rankBadge;
        private readonly TMP_Text _rankTierText, _rankScoreText;
        private readonly TMP_Text _matchesNumText, _matchesNextBadge, _matchesNextFooter;
        private readonly TMP_Text[] _milestoneStatus = new TMP_Text[3];

        private readonly Button[] _sizeButtons = new Button[5];
        private readonly Image[] _sizeImages = new Image[5];
        private readonly TMP_Text[] _sizeLabels = new TMP_Text[5];

        private readonly TMP_Text _teamLeftText, _teamReadyPillText, _queueStatusText;
        private readonly Button _teamConfirmBtn, _teamOpenBtn;
        private readonly TMP_Text _teamConfirmLabel;

        private readonly PresetCardView[] _charCards = new PresetCardView[3];
        private readonly PresetCardView[] _petCards = new PresetCardView[3];

        private readonly Button _primaryBtn, _timeoutCancelBtn;
        private readonly TMP_Text _primaryLabel;

        private uint _selectedSize = RankedFormatting.DefaultTeamSize;
        private uint _selectedChar = RankedFormatting.DefaultCharacterPresetId;
        private uint _selectedPet = RankedFormatting.DefaultPetPresetId;
        private uint _waitPromptDismissedAtSec;
        private bool _wasQueued;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public string ActiveStateKey { get; private set; } = "idle";

        public RankedLobbyPanel(Transform designParent)
        {
            _root = QdaoUguiFactory.CreateRect("RankedLobbyPanel", designParent, 0f, 0f, DesignWidth, DesignHeight);

            // 1. 主窗口底板（严禁使用已退场的旧 ui-window / ui-window-clean；统一复用已验收帮会原图同源窗框）
            CreateGuildArt("WindowBase", _root, 0f, 128f, DesignWidth, 824f, "guild_empty_shell", preserveAspect: true, raycastTarget: true);

            // 2. 顶部标题与导航
            CreateLabel("LobbyTitle", _root, 760f, 74f, 400f, 70f, "论剑", 56f, GoldCream, TextAlignmentOptions.Center, bold: true);
            CreateLabel("BrandTag", _root, 140f, 146f, 240f, 38f, "五行奇谈", 26f, JadeHeading, bold: true);
            _seasonSubtitle = CreateLabel("SeasonSubtitle", _root, 140f, 198f, 640f, 38f, "首测赛季 · 八周", 23f, InkMuted);

            // 复用已有 10 元易容外观入口：pngDisplayRect [1208, 201, 194, 48]
            var appearanceBtn = CreateArtButton("AppearanceEntry", _root, 1208f, 201f, 194f, 48f,
                "appearance-entry", "易容形象", 21f, JadeDark, out _, out _);
            appearanceBtn.onClick.AddListener(() => AppearanceEntryRequested?.Invoke());

            var rankNavBtn = QdaoUguiFactory.CreateHitButton("NavLeaderboard", _root, 1432f, 202f, 130f, 44f);
            CreateLabel("NavLeaderboardText", rankNavBtn.transform, 0f, 0f, 130f, 44f, "排行榜", 24f, JadeDark, TextAlignmentOptions.Center, bold: true);
            rankNavBtn.onClick.AddListener(() => DrawerRequested?.Invoke(RankedDrawerTab.Leaderboard));

            var recordNavBtn = QdaoUguiFactory.CreateHitButton("NavSeasonRecord", _root, 1574f, 202f, 126f, 44f);
            CreateLabel("NavSeasonRecordText", recordNavBtn.transform, 0f, 0f, 126f, 44f, "赛季记录", 24f, JadeDark, TextAlignmentOptions.Center, bold: true);
            recordNavBtn.onClick.AddListener(() => DrawerRequested?.Invoke(RankedDrawerTab.SeasonRecord));

            // 关闭按钮：pngDisplayRect [1708, 201, 74, 48]
            var closeBtn = CreateArtButton("LobbyClose", _root, 1708f, 201f, 74f, 48f,
                "lobby-close", "×", 28f, JadeDark, out _, out _);
            closeBtn.onClick.AddListener(() => CloseRequested?.Invoke());

            // 3. 左上：当前段位卡（130, 270, 500, 357）
            var rankCardBtn = CreateArtButton("RankCard", _root, 130f, 270f, 500f, 357f,
                "panel-rank", "", 18f, Color.clear, out var rankCardImg, out _, preserveAspect: true);
            rankCardImg.preserveAspect = true;
            rankCardBtn.onClick.AddListener(() => DrawerRequested?.Invoke(RankedDrawerTab.Leaderboard));
            CreateLabel("RankCardHeader", rankCardBtn.transform, 100f, 16f, 300f, 34f, "当前段位", 22f, SageLight, TextAlignmentOptions.Center);
            _rankBadge = CreateArt("RankBadge", rankCardBtn.transform, 100f, 26f, 300f, 260f, "rank-1", preserveAspect: true);
            _rankTierText = CreateLabel("RankTier", rankCardBtn.transform, 40f, 244f, 420f, 56f, "未亮段", 46f, GoldCream, TextAlignmentOptions.Center, bold: true);
            _rankScoreText = CreateLabel("RankScore", rankCardBtn.transform, 40f, 298f, 420f, 44f, "1500 分", 34f, GoldSoft, TextAlignmentOptions.Center, bold: true);

            // 4. 左下：赛季进度与三档永久展示奖励卡（130, 650, 500, 260）
            var seasonCardBtn = CreateArtButton("SeasonProgressCard", _root, 130f, 650f, 500f, 260f,
                "panel-paper", "", 18f, Color.clear, out _, out _);
            seasonCardBtn.onClick.AddListener(() => DrawerRequested?.Invoke(RankedDrawerTab.Cosmetics));
            CreateLabel("SeasonProgressPrefix", seasonCardBtn.transform, 24f, 16f, 70f, 42f, "本季", 25f, JadeDark, bold: true);
            _matchesNumText = CreateLabel("SeasonMatchesNum", seasonCardBtn.transform, 94f, 12f, 190f, 48f, "0 / 12 场", 30f, JadeAccent, bold: true);
            _matchesNextBadge = CreateLabel("SeasonNextBadge", seasonCardBtn.transform, 286f, 20f, 192f, 32f, "点击查看外观", 18f, BronzeHint, TextAlignmentOptions.MidlineRight);

            string[] mIcons = { "frame-pattern", "namecard-pattern", "talisman" };
            string[] mNames = { "头像纹样", "名片底纹", "青云玉符" };
            float[] mCenterXs = { 85f, 250f, 415f };
            for (int i = 0; i < 3; i++)
            {
                CreateArt("MilestoneIcon_" + i, seasonCardBtn.transform, mCenterXs[i] - 36f, 88f, 72f, 63f, mIcons[i], preserveAspect: true);
                CreateLabel("MilestoneName_" + i, seasonCardBtn.transform, mCenterXs[i] - 72f, 158f, 144f, 28f,
                    mNames[i], 19f, InkSecondary, TextAlignmentOptions.Center);
                _milestoneStatus[i] = CreateLabel("MilestoneState_" + i, seasonCardBtn.transform, mCenterXs[i] - 78f, 186f, 156f, 26f,
                    $"{(i + 1) * 4} 场", 17f, BronzeHint, TextAlignmentOptions.Center);
            }
            _matchesNextFooter = CreateLabel("SeasonNextFooter", seasonCardBtn.transform, 20f, 220f, 460f, 30f,
                "累计 4 / 8 / 12 场永久取得展示外观", 18f, BronzeHint, TextAlignmentOptions.Center);

            // 5. 右上：五档人数选择（1V1..5V5，x=675+i*223, y=319, 204x77）
            CreateLabel("SizeHeading", _root, 675f, 266f, 320f, 38f, "选择对局人数", 28f, JadeHeading, bold: true);
            CreateLabel("SizeSub", _root, 1460f, 268f, 310f, 34f, "数字表示每边真人数", 19f, InkMuted, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < 5; i++)
            {
                uint size = (uint)(i + 1);
                var btn = CreateArtButton("SizeTab_" + size, _root, 675f + i * 223f, 319f, 204f, 77f,
                    size == _selectedSize ? "tab-selected" : "tab-idle",
                    $"{size}V{size}", 34f, size == _selectedSize ? ButtonGoldText : InkSecondary,
                    out _sizeImages[i], out _sizeLabels[i]);
                btn.onClick.AddListener(() =>
                {
                    _selectedSize = size;
                    TeamSizeSelected?.Invoke(size);
                });
                _sizeButtons[i] = btn;
            }

            // 6. 队伍复用条 + 就地队列状态槽（teamStrip: [675,415,1096,58], queueStatus: [1230,423,520,42]）
            var stripBg = QdaoUguiFactory.CreateRect("TeamStrip", _root, 675f, 415f, 1096f, 58f);
            var stripImg = stripBg.gameObject.AddComponent<Image>();
            stripImg.color = new Color(0.95f, 0.93f, 0.87f, 0.72f);
            stripImg.raycastTarget = false;

            _teamLeftText = CreateLabel("TeamStripPlayer", stripBg, 16f, 8f, 260f, 42f, "听风道友", 22f, JadeDark, bold: true);
            _teamReadyPillText = CreateLabel("TeamReadyPill", stripBg, 280f, 10f, 130f, 38f, "已就绪", 18f, BronzeHint, TextAlignmentOptions.Center, bold: true);

            _teamConfirmBtn = CreateArtButton("TeamConfirmPresetBtn", stripBg, 416f, 8f, 126f, 42f,
                "appearance-action-idle", "确认预设", 17f, JadeDark, out _, out _teamConfirmLabel);
            _teamConfirmBtn.onClick.AddListener(() => TeamConfirmPresetRequested?.Invoke());
            _teamConfirmBtn.gameObject.SetActive(false);

            _teamOpenBtn = CreateArtButton("TeamOpenBtn", stripBg, 548f, 8f, 90f, 42f,
                "drawer-collapse", "队伍", 17f, JadeDark, out _, out _);
            _teamOpenBtn.onClick.AddListener(() => OpenTeamRequested?.Invoke());

            _queueStatusText = CreateLabel("QueueStatusText", _root, 1230f, 423f, 520f, 42f,
                "单人报名 · 系统补齐真人队友", 20f, InkMuted, TextAlignmentOptions.MidlineRight, bold: true);

            // 7. 人物战法独立选区（3 选 1，x=675/1050/1425, y=537, 345x114）
            CreateLabel("CharPresetHeading", _root, 675f, 488f, 150f, 36f, "人物战法", 27f, JadeHeading, bold: true);
            CreateLabel("CharPresetSub", _root, 804f, 490f, 360f, 32f, "三选一 · 零装备与满养成同起跑线", 18f, InkMuted);
            uint[] charIds = { 101u, 102u, 103u };
            string[] charNames = { "破阵", "控场", "守心" };
            string[] charTags = { "物攻爆发", "限制与驱散", "治疗与保护" };
            float[] cardXs = { 675f, 1050f, 1425f };
            for (int i = 0; i < 3; i++)
            {
                uint pid = charIds[i];
                _charCards[i] = BuildPresetCard("CharPreset_" + pid, cardXs[i], 537f, pid,
                    CharacterAvatar(pid), charNames[i], charTags[i],
                    () => SelectPreset(pid, _selectedPet));
            }

            // 8. 宝宝战法独立选区（3 选 1，x=675/1050/1425, y=717, 345x114）
            CreateLabel("PetPresetHeading", _root, 675f, 668f, 150f, 36f, "宝宝战法", 27f, JadeHeading, bold: true);
            CreateLabel("PetPresetSub", _root, 804f, 670f, 520f, 32f, "与人物战法独立选择 · 零宠账号自动使用默认竞技宝宝", 18f, InkMuted);
            uint[] petIds = { 201u, 202u, 203u };
            string[] petNames = { "破甲 · 金猊", "灵咒 · 灵狐", "护主 · 石灵" };
            string[] petTags = { "先削防", "法攻与毒", "治疗与护罩" };
            for (int i = 0; i < 3; i++)
            {
                uint pid = petIds[i];
                _petCards[i] = BuildPresetCard("PetPreset_" + pid, cardXs[i], 717f, pid,
                    PetPortrait(pid), petNames[i], petTags[i],
                    () => SelectPreset(_selectedChar, pid));
            }

            // 9. 底部主操作与 120s 超时次取消按钮（state-layout.json 槽位）
            CreateLabel("RuleLine1", _root, 675f, 862f, 410f, 32f, "五档共用积分、段位与赛季进度", 20f, InkMuted);
            CreateLabel("RuleLine2", _root, 675f, 898f, 410f, 30f, "报名、六种预设与赛季奖励均免费", 18f, InkMuted);

            // 超时次按钮（仅 waiting_120_seconds 态显示）：[1100, 875, 200, 60]
            _timeoutCancelBtn = CreateArtButton("TimeoutCancelBtn", _root, 1100f, 875f, 200f, 60f,
                "timeout-cancel", "取消", 24f, JadeDark, out _, out _);
            _timeoutCancelBtn.onClick.AddListener(() => TimeoutCancelClicked?.Invoke());
            _timeoutCancelBtn.gameObject.SetActive(false);

            // 主按钮：[1337, 858, 430, 89]，preserveAspect = true，禁用 button-primary-cloud
            _primaryBtn = CreateArtButton("PrimaryQueueBtn", _root, 1337f, 858f, 430f, 89f,
                "button-primary", "开始匹配", 31f, ButtonGoldText, out _, out _primaryLabel, preserveAspect: true);
            _primaryBtn.onClick.AddListener(OnPrimaryClicked);

            CreateLabel("FooterRule", _root, 260f, 956f, 1400f, 34f,
                "单人也能参赛  ·  组队保留整队  ·  所有赛季外观仅用于展示", 20f, InkMuted, TextAlignmentOptions.Center);
        }

        public void SetVisible(bool visible)
        {
            _root.gameObject.SetActive(visible);
        }

        public void SetSelectedTeamSize(uint size)
        {
            if (size >= 1 && size <= 5) _selectedSize = size;
        }

        public uint SelectedTeamSize => _selectedSize;
        public uint SelectedCharacterPresetId => _selectedChar;
        public uint SelectedPetPresetId => _selectedPet;

        public void ContinueWaitingFromTimeout(uint currentQueuedSeconds)
        {
            _waitPromptDismissedAtSec = currentQueuedSeconds + 1u;
        }

        public void Render(
            RankedClient ranked,
            TeamClient team,
            BattleClient battle,
            BattleMatchEntryClient entry,
            PetClient pets,
            string localPlayerName,
            bool gateReady)
        {
            var profile = ranked?.Profile;
            if (profile?.SelectedPresets != null)
            {
                if (profile.SelectedPresets.CharacterPresetId != 0) _selectedChar = profile.SelectedPresets.CharacterPresetId;
                if (profile.SelectedPresets.PetPresetId != 0) _selectedPet = profile.SelectedPresets.PetPresetId;
            }

            // 1. 赛季副标题 & 左栏段位/进度
            if (profile != null && profile.HasSeason)
            {
                var s = profile.CurrentSeason;
                _seasonSubtitle.text = s.State == RankedSeasonState.Settling
                    ? $"{s.SeasonName} · 上季结算中"
                    : $"{s.SeasonName} · 八周";
                var p = profile.CurrentProgress;
                bool qualified = p?.Qualified == true;
                var tier = p?.CurrentTier ?? RankedTier.Unspecified;
                _rankBadge.sprite = RankBadge(tier);
                _rankTierText.text = RankedFormatting.TierDisplayName(tier, qualified);
                _rankScoreText.text = $"{RankedFormatting.FormatRating(p?.RatingCenti ?? 150000L)} 分";

                uint valid = p?.ValidMatches ?? 0u;
                _matchesNumText.text = $"{valid} / 12 场";
                _matchesNextBadge.text = valid >= 12 ? "已满 12 场" : $"还差 {(valid < 4 ? 4 - valid : valid < 8 ? 8 - valid : 12 - valid)} 场";
                _matchesNextFooter.text = RankedFormatting.NextMilestoneHint(valid);
                _milestoneStatus[0].text = profile.HasEntitlement(RankedRewardKind.RankedRewardAvatarPattern) ? "4 场 · 已获得" : "4 场";
                _milestoneStatus[1].text = profile.HasEntitlement(RankedRewardKind.RankedRewardCardBackground) ? "8 场 · 已获得" : "8 场";
                _milestoneStatus[2].text = profile.HasEntitlement(RankedRewardKind.RankedRewardTalisman) ? "12 场 · 已获得" : "12 场";
            }
            else
            {
                _seasonSubtitle.text = ranked?.ProfileLoading == true ? "正在同步论剑赛季档案…" : "首测赛季 · 八周";
            }

            // 2. 预设卡动态文案（含服务器下发预设表文案 & 玩家实有宝宝/零宠区分）
            string activePetName = ResolveActivePetDisplayName(pets);
            ApplyPresetCards(profile, activePetName, battle?.Phase == BattlePhase.None && ranked?.PresetSaving != true);

            // 3. 队伍与五档人数状态
            bool inTeam = team != null && team.HasTeam;
            uint partySize = inTeam ? (uint)Math.Max(1, team.Snapshot.Members.Count) : 1u;
            bool isMember = inTeam && !team.IsLeader;
            bool teamPresetsReady = !inTeam || team.Snapshot.RankedPresetsAllConfirmed;
            bool queued = battle != null && battle.Phase == BattlePhase.Queued;
            bool preparing = battle != null && battle.Phase == BattlePhase.Preparing;
            if (!queued && _wasQueued) _waitPromptDismissedAtSec = 0u;
            _wasQueued = queued;

            for (int i = 0; i < 5; i++)
            {
                uint size = (uint)(i + 1);
                bool selected = size == _selectedSize;
                _sizeImages[i].sprite = Load(selected ? "tab-selected" : "tab-idle");
                _sizeLabels[i].color = selected ? ButtonGoldText : InkSecondary;
                // 排队/开局期间锁定档位；组队时允许点选 5V5 以展示 4人排5V5 引导态，低于当前人数档置灰
                _sizeButtons[i].interactable = !queued && !preparing && size >= partySize;
            }

            if (inTeam)
            {
                int confirmed = 0;
                foreach (var m in team.Snapshot.Members) if (m != null && m.RankedPresetConfirmed) confirmed++;
                _teamLeftText.text = $"队伍 {partySize} 人 · 预设 {confirmed}/{partySize}";
                _teamReadyPillText.text = teamPresetsReady ? "全员已确认" : "待确认预设";
                _teamReadyPillText.color = teamPresetsReady ? JadeAccent : BronzeHint;
                _teamConfirmBtn.gameObject.SetActive(true);
                _teamConfirmBtn.interactable = !queued && !preparing && !team.Busy;
                _teamConfirmLabel.text = team.PendingAction == TeamAction.ConfirmRankedPreset ? "确认中…" : "确认预设";
            }
            else
            {
                _teamLeftText.text = string.IsNullOrEmpty(localPlayerName) ? "听风道友" : localPlayerName;
                _teamReadyPillText.text = "已就绪";
                _teamReadyPillText.color = BronzeHint;
                _teamConfirmBtn.gameObject.SetActive(false);
            }

            // 4. 严格映射 state-layout.json 的 8 态
            bool settling = profile != null && profile.IsSeasonSettling;
            bool fourChooseFive = inTeam && TeamClient.PvpPartySizeForbidden(_selectedSize, partySize);
            var qStatus = battle?.LastQueueStatus;
            uint elapsed = qStatus?.QueuedSeconds ?? 0u;
            uint waitPrompt = qStatus != null && qStatus.WaitPromptSeconds != 0
                ? qStatus.WaitPromptSeconds
                : (profile?.WaitPromptSeconds ?? RankedFormatting.DefaultWaitPromptSeconds);

            _timeoutCancelBtn.gameObject.SetActive(false);
            if (!gateReady || (ranked != null && ranked.ProfileLoading && profile == null))
            {
                ActiveStateKey = "recovering";
                _queueStatusText.text = "连接恢复中，正在恢复报名与结算状态";
                _queueStatusText.color = BronzeHint;
                _primaryLabel.text = "恢复中…";
                _primaryBtn.interactable = false;
            }
            else if (preparing)
            {
                ActiveStateKey = "matched";
                uint matchedSize = qStatus != null && qStatus.TeamSize != 0 ? qStatus.TeamSize : _selectedSize;
                _queueStatusText.text = $"成局 · {matchedSize}V{matchedSize}";
                _queueStatusText.color = JadeAccent;
                _primaryLabel.text = "进入对局";
                _primaryBtn.interactable = false;
            }
            else if (battle != null && battle.IsCancellingQueue)
            {
                ActiveStateKey = "cancelling";
                _queueStatusText.text = "取消中，正在等待确认";
                _queueStatusText.color = BronzeHint;
                _primaryLabel.text = "取消中…";
                _primaryBtn.interactable = false;
            }
            else if (queued && battle.IsJoinPending)
            {
                ActiveStateKey = "registering";
                _queueStatusText.text = "报名中";
                _queueStatusText.color = BronzeHint;
                _primaryLabel.text = "报名中…";
                _primaryBtn.interactable = false;
            }
            else if (queued && elapsed >= waitPrompt && elapsed >= _waitPromptDismissedAtSec)
            {
                ActiveStateKey = "waiting_120_seconds";
                _queueStatusText.text = $"已等待 {RankedFormatting.FormatElapsedClock(elapsed)}，是否继续寻找对手？";
                _queueStatusText.color = BronzeHint;
                _primaryLabel.text = "继续等待";
                _primaryBtn.interactable = true;
                _timeoutCancelBtn.gameObject.SetActive(true);
                _timeoutCancelBtn.interactable = entry?.CanCancelNow != false;
            }
            else if (queued)
            {
                ActiveStateKey = "waiting";
                string spread = qStatus != null && qStatus.CurrentRatingSpread > 0 ? $" · 分差≤{qStatus.CurrentRatingSpread}" : "";
                _queueStatusText.text = $"匹配中 · {RankedFormatting.FormatElapsedClock(elapsed)}{spread}";
                _queueStatusText.color = JadeAccent;
                _primaryLabel.text = "取消匹配";
                _primaryBtn.interactable = entry?.CanCancelNow != false;
            }
            else if (fourChooseFive)
            {
                ActiveStateKey = "four_members_choose_five";
                _queueStatusText.text = "再邀一位好友即可报名";
                _queueStatusText.color = WarnRed;
                _primaryLabel.text = "开始匹配";
                _primaryBtn.interactable = false;
            }
            else
            {
                ActiveStateKey = "idle";
                _primaryLabel.text = "开始匹配";
                if (settling)
                {
                    _queueStatusText.text = "上季结算中，暂时无法报名";
                    _queueStatusText.color = WarnRed;
                    _primaryBtn.interactable = false;
                }
                else if (inTeam && isMember)
                {
                    _queueStatusText.text = teamPresetsReady ? "已就绪 · 等待队长开始匹配" : "请先点击「确认预设」";
                    _queueStatusText.color = BronzeHint;
                    _primaryBtn.interactable = false;
                }
                else if (inTeam && !teamPresetsReady)
                {
                    _queueStatusText.text = "有队员尚未确认论剑战法预设";
                    _queueStatusText.color = WarnRed;
                    _primaryBtn.interactable = false;
                }
                else if (!string.IsNullOrEmpty(ranked?.LastError))
                {
                    _queueStatusText.text = ranked.LastError;
                    _queueStatusText.color = WarnRed;
                    _primaryBtn.interactable = entry?.CanStartNow != false;
                }
                else
                {
                    _queueStatusText.text = inTeam
                        ? $"整队报名 · 系统补齐真人队友（{_selectedSize}V{_selectedSize}）"
                        : $"单人报名 · 系统补齐真人队友（{_selectedSize}V{_selectedSize}）";
                    _queueStatusText.color = InkMuted;
                    _primaryBtn.interactable = entry?.CanStartNow != false;
                }
            }
        }

        private void ApplyPresetCards(RankedProfileSnapshot profile, string activePetName, bool interactable)
        {
            var serverChars = profile?.CharacterPresets;
            for (int i = 0; i < _charCards.Length; i++)
            {
                var card = _charCards[i];
                var pv = FindPreset(serverChars, card.PresetId);
                if (pv != null)
                {
                    card.Title.text = RankedFormatting.CharacterPresetTitle(pv.PresetId, pv.Name);
                    if (!string.IsNullOrEmpty(pv.RoleTag)) card.Sub.text = pv.RoleTag;
                }
                bool selected = card.PresetId == _selectedChar;
                card.Bg.sprite = Load(selected ? "selection-selected" : "selection-idle");
                card.CheckMark.gameObject.SetActive(selected);
                card.Title.color = selected ? JadeSelected : InkSecondary;
                card.Button.interactable = interactable;
            }

            var serverPets = profile?.PetPresets;
            for (int i = 0; i < _petCards.Length; i++)
            {
                var card = _petCards[i];
                var pv = FindPreset(serverPets, card.PresetId);
                string tacticName = RankedFormatting.PetPresetTitle(card.PresetId, pv?.Name);
                string species = !string.IsNullOrEmpty(activePetName)
                    ? activePetName
                    : RankedFormatting.PetDefaultSpeciesName(card.PresetId);
                card.Title.text = $"{tacticName} · {species}";
                if (pv != null && !string.IsNullOrEmpty(pv.RoleTag))
                    card.Sub.text = string.IsNullOrEmpty(activePetName) ? $"{pv.RoleTag}（默认宝宝）" : pv.RoleTag;
                bool selected = card.PresetId == _selectedPet;
                card.Bg.sprite = Load(selected ? "selection-selected" : "selection-idle");
                card.CheckMark.gameObject.SetActive(selected);
                card.Title.color = selected ? JadeSelected : InkSecondary;
                card.Button.interactable = interactable;
            }
        }

        private PresetCardView BuildPresetCard(string name, float x, float y, uint presetId,
            Sprite portraitSprite, string titleText, string subText, Action onClick)
        {
            var btn = CreateArtButton(name, _root, x, y, 345f, 114f,
                "selection-idle", "", 18f, Color.clear, out var bg, out _);
            btn.onClick.AddListener(() => onClick?.Invoke());
            var portrait = QdaoUguiFactory.CreateImage(name + "_Portrait", btn.transform, 22f, 19f, 76f, 76f, portraitSprite);
            portrait.preserveAspect = true;
            if (portrait.sprite == null)
            {
                portrait.color = SeasonEconomyUiArt.BattleArenaJade;
                CreateLabel(name + "_Glyph", portrait.transform, 0f, 0f, 82f, 82f,
                    titleText.Length > 0 ? titleText.Substring(0, 1) : "道", 32f, GoldCream, TextAlignmentOptions.Center, bold: true);
            }
            var title = CreateLabel(name + "_Title", btn.transform, 114f, 18f, 190f, 38f, titleText, 26f, InkSecondary, bold: true);
            var sub = CreateLabel(name + "_Sub", btn.transform, 114f, 58f, 214f, 32f, subText, 19f, InkMuted);
            var check = CreateArt(name + "_Check", btn.transform, 304f, 14f, 28f, 28f, "selected-check", preserveAspect: true);
            check.gameObject.SetActive(false);
            return new PresetCardView
            {
                PresetId = presetId, Button = btn, Bg = bg, Portrait = portrait,
                CheckMark = check, Title = title, Sub = sub,
            };
        }

        private void SelectPreset(uint charId, uint petId)
        {
            _selectedChar = charId;
            _selectedPet = petId;
            PresetSelected?.Invoke(charId, petId);
        }

        private void OnPrimaryClicked()
        {
            PrimaryActionClicked?.Invoke();
        }

        private static RankedPresetView FindPreset(IReadOnlyList<RankedPresetView> list, uint id)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].PresetId == id) return list[i];
            }
            return null;
        }

        private static string ResolveActivePetDisplayName(PetClient pets)
        {
            var list = pets?.Pets;
            if (list == null || list.ActivePetId == 0) return string.Empty;
            foreach (var p in list.Pets)
            {
                if (p != null && p.PetId == list.ActivePetId)
                    return !string.IsNullOrEmpty(p.Name) ? p.Name : "已出战宝宝";
            }
            return string.Empty;
        }
    }
}
