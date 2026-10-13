#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Guild;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>帮会正式窗口的离线样例验收；不连接服务端，不保存或覆盖用户场景。</summary>
public static class GuildUiVerification
{
    public static string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-artifacts/guild-ui-v2"));
    private static GameObject _preview;
    private static GuildClient _previewClient;
    private static Func<string, bool> _captureFilter;
    // 可见却一个字形都没出的文字(截图名 + 文字名 + 源串)。框高不到一行时 TMP 的 Ellipsis 会把整行吞成空白,
    // 只读 text 源串的 EditMode 断言看不出来,所以截图时按真实网格再核一遍。
    private static readonly List<string> _missingGlyphs = new List<string>();

    [MenuItem("MMORPG/UI/Preview guild UI (offline)")]
    public static void PreviewOffline()
    {
        ClosePreview();
        _preview = new GameObject("[Offline Guild Preview]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _preview.hideFlags = HideFlags.DontSave;
        var canvas = _preview.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 360;
        var scaler = _preview.GetComponent<CanvasScaler>(); ConfigureScaler(scaler);
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("GuildPreviewEvents", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(_preview.transform, false);
        }
        var design = QdaoUguiFactory.CreateCenteredRect("Design", _preview.transform, 2560, 1080);
        var window = new GuildWindow(design);
        _previewClient = new GuildClient(new FixtureTransport());
        _previewClient.Changed += () => window.SetClient(_previewClient);
        window.RankRequested += page => _previewClient.Browse(page);
        window.RefreshRequested += () => _previewClient.Refresh();
        // 只接申请列表这一条写外的读请求：FixtureTransport 有对应分支，点开“入帮申请”能看到样例。
        // 任免 / 请离 / 审批等写操作故意不接——替身会以 error 回包，反而把预览锁进“需重新登录”。
        window.ApplicationsRequested += () => _previewClient.LoadApplications();
        // 捐献页 / 商店页的读取(B5):替身有样例,进页面即自动拉取;捐献、兑换、升级是写操作,同上不接。
        window.DonationsRequested += () => _previewClient.RefreshDonations();
        window.ShopRequested += () => _previewClient.RefreshShop();
        // 活动页的读取(B6a):正式环境里这个事件只排队、由 GuildUiRoot 每帧的 DrainQueued 发出;预览没有那个循环,
        // 而替身是同步回包(不会 Busy),所以直接拉。点灯 / 领团圆礼、历练的发出邀请 / 应答(B6b)是写操作,同上不接:
        // 历练卡片的样例是"本人发起、等待同道确认"(按钮"取消邀请"点了没有反应);选人框与邀请框见 CaptureAll 的 12 / 13 两屏。
        window.ActivitiesRequested += () => _previewClient.RefreshActivities();
        _previewClient.Refresh(); window.SetClient(_previewClient); window.Show();
        Badge(design);
        foreach (var child in _preview.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
        AssemblyReloadEvents.beforeAssemblyReload += ClosePreview;
    }

    [MenuItem("MMORPG/UI/Close guild preview")]
    public static void ClosePreview()
    {
        _previewClient?.Dispose(); _previewClient = null;
        if (_preview != null) UnityEngine.Object.DestroyImmediate(_preview);
        _preview = null; AssemblyReloadEvents.beforeAssemblyReload -= ClosePreview;
    }

    [MenuItem("MMORPG/UI/Capture guild screens (offline)")]
    public static void CaptureAll()
    {
        _missingGlyphs.Clear();
        Capture(2560, 1080); Capture(1920, 1080);
        // 13 屏 × 2 分辨率：六个页签 + 公告编辑 + 未入帮 + 长帮名确认 + 需重连 + 入帮申请审批 + 历练选人框 + 历练邀请框。
        // 六个页签都是带样例数据的真实页面(B6a 起活动页也是:05-activities 为灯会 / 团圆 / 历练三张卡片;
        // B6b 起历练卡片是"本人发起、等待同道确认"的邀请房间,另加 12-trial-picker 与 13-trial-invite 两屏弹窗)。
        // 有文字没出字形就记为 failed(不中断截图,好让所有问题一次看全)。
        bool passed = _missingGlyphs.Count == 0;
        File.WriteAllText(Path.Combine(OutputDirectory, "capture.json"),
            "{\"status\":\"" + (passed ? "passed" : "failed") + "\",\"source\":\"production GuildWindow with offline fixtures\",\"screenshots\":26,"
            + "\"missingGlyphLabels\":" + _missingGlyphs.Count + ",\"liveServerVerification\":false}");
        if (!passed) Debug.LogError("帮会界面有文字未渲染(框高不足一行?):\n" + string.Join("\n", _missingGlyphs));
        Debug.Log("Guild UI capture completed: " + OutputDirectory);
    }

    public static void CaptureEmptyStates()
    {
        _captureFilter = name => name == "08-empty" || name == "09-long-name-confirmation";
        try { Capture(2560, 1080); Capture(1920, 1080); }
        finally { _captureFilter = null; }
        Debug.Log("Guild UI empty-state recapture completed: " + OutputDirectory);
    }
    private static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式验收帮会界面。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previousScene.path))
        { EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single); previousScene = SceneManager.GetActiveScene(); }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null; Texture2D pixels = null; GuildWindow window = null; GuildClient client = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("GuildCaptureCamera").AddComponent<Camera>();
            camera.enabled = false; camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("GuildCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2f;
            ConfigureScaler(canvasObject.GetComponent<CanvasScaler>());
            var design = QdaoUguiFactory.CreateCenteredRect("Design", canvasObject.transform, 2560, 1080);
            window = new GuildWindow(design);
            var net = new FixtureTransport(); client = new GuildClient(net);
            client.Changed += () => window.SetClient(client);
            window.RankRequested += page => client.Browse(page);
            // 申请视图的数据要真走一次 ListGuildApplications，否则那一屏只会停在“正在读取入帮申请…”。
            window.ApplicationsRequested += () => client.LoadApplications();
            // 捐献页 / 商店页进页面自动拉取(同步回包),04-donate / 06-shop 截到的是样例数据而不是"点击刷新"。
            window.DonationsRequested += () => client.RefreshDonations();
            window.ShopRequested += () => client.RefreshShop();
            // 活动页同理(B6a):05-activities 截到的是三张带样例数据的卡片,不再是"暂未开放"占位。
            // 截图流程没有每帧的 DrainQueued,替身又是同步回包,所以直接拉而不排队。
            window.ActivitiesRequested += () => client.RefreshActivities();
            client.Refresh(); window.SetClient(client); Badge(design);
            void Shoot(string name)
            {
                if (_captureFilter != null && !_captureFilter(name)) return;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var label in canvasObject.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                foreach (var label in canvasObject.GetComponentsInChildren<TMP_Text>())
                {
                    // 空输入框的文字组件里留着一个零宽空格(U+200B,不算空白字符,先换成空格);禁用的占位文字不参与。
                    if (!label.enabled || string.IsNullOrWhiteSpace(label.text?.Replace((char)0x200B, ' '))) continue;
                    var info = label.textInfo;
                    bool visible = false;
                    for (int i = 0; info != null && i < info.characterCount; i++) visible |= info.characterInfo[i].isVisible;
                    if (!visible) _missingGlyphs.Add(name + "_" + width + "x" + height + "：" + label.name + " / " + label.text);
                }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }
            foreach (GuildPage page in Enum.GetValues(typeof(GuildPage)))
            { window.Show(page); Shoot("0" + ((int)page + 1) + "-" + page.ToString().ToLowerInvariant()); }
            // 同道历练的两个弹窗(B6b)。替身是同步回包:换一种样例后重拉一次活动视图,Changed → SetClient 会把页面重画。
            void Press(string name)
            {
                foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                    if (button.name == name) { button.onClick.Invoke(); break; }
            }
            // 12:没有房间、可发起 → 点"组队历练"打开选人框,选中两位在线同道(每点一下选人框整个重画,所以逐个找按钮)。
            net.Trial = FixtureTransport.TrialSample.Open; client.RefreshActivities(); window.Show(GuildPage.Activities);
            Press("GuildActivityAction_3"); Press("GuildTrialMember_10002"); Press("GuildTrialMember_10004");
            Shoot("12-trial-picker");
            // 13:被 10002 邀请、尚未应答 → 活动页重画时邀请框自动弹出(带倒计时)。
            window.Back(); net.Trial = FixtureTransport.TrialSample.Invited; client.RefreshActivities();
            Shoot("13-trial-invite");
            window.Back(); net.Trial = FixtureTransport.TrialSample.Hosting; client.RefreshActivities();
            window.Show(GuildPage.Overview);
            foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                if (button.name == "EditGuildAnnouncement") { button.onClick.Invoke(); break; }
            Shoot("07-announcement");
            // 入帮申请审批屏：先关掉公告弹窗，再进成员页切到申请视图。
            // 点击 GuildApplicationsToggle 会同步触发 ApplicationsRequested → LoadApplications（替身是
            // 同步回包），所以这里不需要等帧，Shoot 时列表已经在了。
            window.Back(); window.Show(GuildPage.Members);
            foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                if (button.name == "GuildApplicationsToggle") { button.onClick.Invoke(); break; }
            Shoot("11-applications");
            window.Back(); net.HasGuild = false; client.Reset(); client.Refresh(); window.Show();
            Shoot("08-empty");
            net.LongName = true; window.Show(GuildPage.Ranking);
            foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                if (button.name == "ApplyGuild_88001") { button.onClick.Invoke(); break; }
            Shoot("09-long-name-confirmation"); window.Back();
            net.HasGuild = true; client.Refresh(); net.FailNext = true; client.Refresh(); window.Show();
            Shoot("10-reconnect-required");
        }
        finally
        {
            window?.Hide(); client?.Dispose(); RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        }
    }
    private static void ConfigureScaler(CanvasScaler scaler)
    {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }
    private static void Badge(UnityEngine.Transform parent) =>
        QdaoUguiFactory.CreateText("OfflineGuildBadge", parent, 200, 12, 2160, 45,
            "离线界面验收 · 示例数据 · 正式窗口由帮会服务返回数据", 25, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);

    private sealed class FixtureTransport : IBattleTransport
    {
        public ulong PlayerId => 10001;
        public bool IsReady => true;
        public bool HasGuild = true;
        public bool LongName, FailNext;
        /// <summary>历练卡片的样例:本人发起、等待确认(默认,05-activities)/ 没有房间、可发起(选人框)/ 被邀请、待应答(邀请框)。</summary>
        public enum TrialSample { Hosting, Open, Invited }
        public TrialSample Trial = TrialSample.Hosting;
        public event Action Disconnected;
        public void RegisterNotify(uint id, Action<MessageContent> handler) { }
        public void SendOneWay(uint id, IMessage message) { }
        public void Call<T>(uint id, IMessage message, MessageParser<T> parser, Action<T> success, Action<string> error) where T : IMessage<T>
        {
            if (FailNext) { FailNext = false; error("rpc timeout"); return; }
            IMessage response;
            if (id == MessageIds.GetPlayerGuild)
                response = HasGuild ? new GetPlayerGuildResponse { Guild = Fixture() } :
                    new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } };
            else if (id == MessageIds.GetGuildRank)
            {
                var ranks = new GetGuildRankResponse { Page = 1, PageSize = 5, TotalCount = 5 };
                string[] names = { "清风明月", "云水同心", "青莲仙友", "桃源结义", "星河听雨" };
                for (int i = 0; i < 5; i++) ranks.Entries.Add(new GuildRankEntry { GuildId = (ulong)(88001 + i),
                    Name = i == 0 && LongName ? "青山常在明月长明同道相守万家灯火清风归处岁岁平安" : names[i], LeaderId = (ulong)(10001 + i), Level = (uint)(5 - i), MemberCount = (uint)(32 - i * 3),
                    Score = 88600 - i * 4200, Rank = (uint)(i + 1) });
                response = ranks;
            }
            else if (id == MessageIds.ListGuildApplications)
            {
                // 样例申请人：1 小时前提交、48 小时后过期，申请视图那一列固定显示“剩余 48 小时”；
                // 只有第 1 位在线，好让在线 / 离线两种样式同屏出现。
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var applications = new ListGuildApplicationsResponse();
                for (ulong i = 0; i < 3; i++)
                    applications.Applicants.Add(new GuildApplicantView
                    {
                        PlayerId = 20001 + i, Online = i == 0,
                        ApplyMs = (ulong)(now - 3600000L), ExpireMs = (ulong)(now + 48L * 3600000L)
                    });
                response = applications;
            }
            // 样例账号自己没有在申请别的帮会：排行页按钮保持“申请”，不会翻成“撤回申请”。
            else if (id == MessageIds.ListMyGuildApplications) response = new ListMyGuildApplicationsResponse();
            else if (id == MessageIds.GetGuildDonateOptions) response = DonateFixture();
            else if (id == MessageIds.GetGuildShop) response = ShopFixture();
            else if (id == MessageIds.GetGuildActivities) response = ActivitiesFixture();
            else { error("离线验收不执行服务端操作"); return; }
            success((T)response);
        }
        private static GuildInfo Fixture()
        {
            // 样例账号(10001)是帮主：成员页才会出现任免 / 转让 / 请离三槽。
            // 长老 1 人、上限 6 人 → “任长老”按钮可点(未到上限)；待审 3 份 → 角标显示“入帮申请 3”。
            var info = new GuildInfo { GuildId = 88001, Name = "清风明月", LeaderId = 10001, Level = 5, MaxMembers = 50,
                Announcement = "同道相逢，皆是有缘。\n\n愿每一盏灯，都照亮归家的路；愿每一次同行，都不负山海与明月。\n\n帮会事务请与帮主、长老联系。", ZoneId = 1,
                OfficerCount = 1, MaxOfficers = 6, PendingApplicationCount = 3, Funds = 186000, UpgradeCostFunds = 460000 };
            for (ulong i = 0; i < 12; i++) info.Members.Add(new GuildMember { PlayerId = 10001 + i,
                Role = i == 0 ? 3u : i == 1 ? 1u : 0u, ContributionTotal = 3560 - i * 130, ContributionBalance = 1240 - i * 60, Online = i < 7 });
            return info;
        }
        // 捐献样例:小捐用过 2 次、大捐用满(按钮置灰),一笔灵石捐献在战斗中结算中 —— 三种按钮态与页脚同屏。
        private static GetGuildDonateOptionsResponse DonateFixture()
        {
            var response = new GetGuildDonateOptionsResponse { ContributionTotal = 3560, ContributionBalance = 1240 };
            response.Options.Add(new GuildDonateOptionView { DonateId = 1, Name = "银两小捐", CurrencyType = 0, CostAmount = 10000,
                ContributionGain = 10, FundsGain = 1000, DailyLimit = 5, UsedToday = 2, MinGuildLevel = 1, Unlocked = true });
            response.Options.Add(new GuildDonateOptionView { DonateId = 2, Name = "银两大捐", CurrencyType = 0, CostAmount = 100000,
                ContributionGain = 120, FundsGain = 12000, DailyLimit = 2, UsedToday = 2, MinGuildLevel = 1, Unlocked = true });
            response.Options.Add(new GuildDonateOptionView { DonateId = 3, Name = "灵石捐献", CurrencyType = 1, CostAmount = 100,
                ContributionGain = 200, FundsGain = 20000, DailyLimit = 1, UsedToday = 1, MinGuildLevel = 1, Unlocked = true });
            response.PendingDonations.Add(new GuildDonationView { OpId = 7001, DonateId = 3, Status = GuildAssetOrderStatus.Pending,
                CurrencyType = 1, CostAmount = 100, ContributionGain = 200, FundsGain = 20000, ReasonTipId = GuildAssetReasons.InBattle });
            return response;
        }
        // 商店样例:与 GuildShop 默认 11 行同形,帮会 Lv.5 → 204(Lv.6)未解锁;花灯今日兑满;有一单背包满待发放。
        private static GetGuildShopResponse ShopFixture()
        {
            var response = new GetGuildShopResponse { ContributionBalance = 1240 };
            void Add(uint id, string name, uint category, uint count, ulong cost, uint level, uint period, uint limit, uint used = 0) =>
                response.Goods.Add(new GuildShopGoodsView { GoodsId = id, Name = name, Category = category, ItemId = id, ItemCount = count,
                    CostContribution = cost, RequiredGuildLevel = level, Unlocked = level <= 5, LimitPeriod = period, LimitCount = limit,
                    UsedCount = used, MaxBuyCount = 1 });
            Add(101, "培元丹", 1, 5, 30, 1, 1, 10, 3); Add(102, "回灵散", 1, 5, 30, 1, 1, 10);
            Add(103, "精炼石", 1, 1, 80, 2, 1, 5); Add(104, "修行秘录残页", 1, 1, 150, 3, 2, 5);
            Add(201, "帮会令牌", 2, 1, 300, 3, 2, 3); Add(202, "玄铁护符", 2, 1, 800, 4, 2, 1);
            Add(203, "灵兽口粮", 2, 10, 120, 2, 1, 3); Add(204, "藏经阁手札", 2, 1, 1500, 6, 2, 1);
            Add(301, "花灯", 3, 1, 50, 1, 1, 5, 5); Add(302, "月饼礼盒", 3, 1, 100, 1, 2, 7); Add(303, "同心结", 3, 1, 200, 5, 0, 0);
            response.PendingOrders.Add(new GuildShopOrderView { OpId = 7101, GoodsId = 203, Count = 1, Status = GuildAssetOrderStatus.Pending,
                CostContribution = 120, ReasonTipId = GuildAssetReasons.BagFull });
            return response;
        }
        // 活动样例:三种按钮态同屏 —— 灯会可点(本期 2 / 3,本人未点);团圆本期已锁存、本人已领,物品因背包满待发放;
        // 历练已开放(B6b),默认是"本人发起、已有一人同意、等待另一人确认"的邀请房间(按钮"取消邀请"),
        // 另两种样例见 TrialSample。带服务端时刻,标题行右侧显示"距下次重置 7 小时 25 分"。
        private GetGuildActivitiesResponse ActivitiesFixture()
        {
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ulong nextReset = now + (7 * 60 + 25) * 60000ul;
            var response = new GetGuildActivitiesResponse();
            response.Activities.Add(new GuildActivityView { ActivityId = 1, Type = GuildActivityType.Lantern, Name = "元宵灯会",
                State = GuildActivityState.Open, MinGuildLevel = 1, PersonalContribution = 20, GuildFunds = 500, GuildThreshold = 3,
                DailyLimit = 1, MyUsedCount = 0, Progress = 2, ServerTimeMs = now, NextResetMs = nextReset });
            // 团圆带档期(还有三天结束),顺带验"至 MM-dd HH:mm 结束"这一格的宽度。
            var reunion = new GuildActivityView { ActivityId = 2, Type = GuildActivityType.Reunion, Name = "中秋团圆",
                State = GuildActivityState.Open, StartAtMs = now - 86400000ul, EndAtMs = now + 3 * 86400000ul, MinGuildLevel = 1,
                PersonalContribution = 30, GuildThreshold = 3, DailyLimit = 1, MyUsedCount = 1, ThresholdReached = true,
                BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed,
                MyPendingRewardCount = 1, MyPendingReasonTipId = GuildAssetReasons.BagFull,
                ServerTimeMs = now, NextResetMs = nextReset };
            reunion.RewardItems.Add(new GuildRewardItem { ItemId = 1, Count = 4 });
            response.Activities.Add(reunion);
            var trial = new GuildActivityView { ActivityId = 3, Type = GuildActivityType.Trial, Name = "同道历练",
                State = GuildActivityState.Open, MinGuildLevel = 1, PersonalContribution = 50, GuildFunds = 300, GuildThreshold = 3,
                DailyLimit = 2, MyUsedCount = 1, Progress = 1, DungeonId = 1, TeamSizeMin = 2, TeamSizeMax = 5,
                ServerTimeMs = now, NextResetMs = nextReset };
            trial.RewardItems.Add(new GuildRewardItem { ItemId = 2, Count = 1 });
            if (Trial != TrialSample.Open)
            {
                // 邀请房间:三人队,24 秒后到期。Hosting = 样例账号(10001)发起,10002 已同意;Invited = 10002 发起,样例账号还没应答。
                ulong initiator = Trial == TrialSample.Hosting ? 10001ul : 10002ul;
                var lobby = new GuildTrialLobbyView { LobbyId = Trial == TrialSample.Hosting ? 9001ul : 9002ul,
                    InitiatorPlayerId = initiator, State = GuildTrialLobbyState.Pending, ExpireAtMs = now + 24000 };
                lobby.MemberPlayerIds.Add(initiator); lobby.AcceptedPlayerIds.Add(initiator);
                lobby.MemberPlayerIds.Add(Trial == TrialSample.Hosting ? 10002ul : 10001ul);
                lobby.MemberPlayerIds.Add(10003);
                if (Trial == TrialSample.Hosting) lobby.AcceptedPlayerIds.Add(10002);
                trial.TrialLobby = lobby;
            }
            response.Activities.Add(trial);
            return response;
        }
    }
}
#endif
