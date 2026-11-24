using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

// 仅替换网络传输；模型、Client、请求/响应类型和消息号全部链接正式源码。
// 本检查不启动 Unity，也不连接游戏服务器，不等同于 Unity 实点或联机验收。
internal static class Program
{
    private const string A = "00_reference_topright_boy", B = "10_crimson_spear_girl";
    private static int _checks;
    private sealed class Call
    {
        public uint Id;
        public IMessage Request;
        public Action<IMessage> Response;
        public Action<string> Error;
    }
    private sealed class RecordingTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = ulong.MaxValue - 5;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public readonly List<Call> Calls = new();
        public readonly Dictionary<uint, Action<MessageContent>> Notifies = new();
        public void RegisterNotify(uint messageId, Action<MessageContent> handler) => Notifies[messageId] = handler;
        public void Call<T>(uint messageId, IMessage request, MessageParser<T> parser, Action<T> onResponse,
            Action<string> onError) where T : IMessage<T>
            => Calls.Add(new Call { Id = messageId, Request = request, Response = response => onResponse((T)response), Error = onError });
        public void SendOneWay(uint messageId, IMessage request) => throw new InvalidOperationException("Unexpected one-way request");
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
    }
    private static void Check(bool condition, string description)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(description);
    }
    private static PlayerAppearanceInfo Info(string permanent = A, string active = B) => new PlayerAppearanceInfo
    {
        PermanentAppearanceId = permanent, ActiveAppearanceId = active,
        TrialAppearanceId = active == permanent ? "" : active,
        ServerNow = 1000, TrialExpiresAt = active == permanent ? 0ul : 4600ul,
        CardItemId = 3402, CardItemCount = 1, OwnedCardCount = 2,
        Appearances = { new CharacterAppearanceEntry { AppearanceId = A, Name = "金发带Q道童" },
            new CharacterAppearanceEntry { AppearanceId = B, Name = "赤枪少女" } }
    };

    private static void CatalogAndState()
    {
        Check(AppearanceBiographyCatalog.All.Count == 3, "目录必须恰有三人");
        var fixtures = new[] { (A, "陆照禾", "第四只碗", 4), (B, "祝停云", "纸也赢过一回", 6),
            ("09_bamboo_archer_girl", "桑听雨", "没有画的那一页", 3) };
        foreach (var fixture in fixtures)
        {
            Check(AppearanceBiographyCatalog.TryGetByRawAppearanceId(fixture.Item1, out var bio), "原始ID目录缺失");
            Check(bio.RawAppearanceId == fixture.Item1 && bio.ArchiveName == fixture.Item2 && bio.StoryTitle == fixture.Item3, "人物身份与篇名必须同ID");
            Check(bio.StoryBody.Split(new[] { "\n\n" }, StringSplitOptions.None).Length == fixture.Item4, "原始段落必须保留");
        }
        foreach (string id in new[] { "QdaoHeadbandBoy", "18_desert_sun_monk_girl", "26_osmanthus_healer",
            "00_REFERENCE_TOPRIGHT_BOY", A + " ", "01_ice_sword_girl", "17_ghost_script_calligrapher_boy", "", null })
            Check(!AppearanceBiographyCatalog.TryGetByRawAppearanceId(id, out var missing) && missing == null, "旧别名、已删角色和未设定人物不得借用文案");

        var state = new AppearancePanelState();
        state.SyncAvailableAppearances(new[] { A, B }, A, A);
        state.SelectAppearance(B, .37f);
        Check(state.RequestSwitch(B, A, 2, 1) == AppearanceSwitchIntentOutcome.OpenedSwitchConfirm, "切换只能先打开确认");
        state.ArmLegacySwitchConfirmation(B, 10);
        Check(state.OpenStory(.37f), "已公开故事必须能读");
        Check(!state.HasPendingSwitchConfirmation && !state.TryConsumePendingSwitchConfirm(out _), "读故事必须作废未提交扣卡确认");
        Check(state.RequestSwitch(B, A, 2, 1) == AppearanceSwitchIntentOutcome.Ignored, "阅读中不能重新武装扣卡确认");
        Check(state.TryGetReadingBiography(out var reading) && reading.RawAppearanceId == B, "阅读人物身份精确匹配");
        Check(state.CloseStory(AppearanceStoryCloseReason.Finished) && state.SelectedAppearanceId == B &&
            Math.Abs(state.SavedListScrollNormalized - .37f) < .0001f, "返回保持列表选择和滚动");
        Check(!state.TryConsumePendingSwitchConfirm(out _), "返回后旧确认保持失效");
        Check(state.RequestSwitch(B, A, 2, 1) == AppearanceSwitchIntentOutcome.OpenedSwitchConfirm, "返回后首次点击重新进入确认");
        Check(state.RequestSwitch(B, A, 0, 1, targetOwned: true, targetCardUnlockable: false) == AppearanceSwitchIntentOutcome.ReadyForFreeSwitch && !state.HasOverlay,
            "已拥有形象 0 卡直切免费分支且不弹确认");
        Check(state.RequestSwitch(B, A, 5, 1, targetOwned: false, targetCardUnlockable: false) == AppearanceSwitchIntentOutcome.LockedExclusive && !state.HasOverlay,
            "非形象卡兑换的玩法专属形象不弹扣卡确认");
        state.OpenStory(.37f);
        state.SyncAvailableAppearances(new[] { A }, A, A);
        Check(state.SelectedAppearanceId == A && !state.IsStoryOpen && !state.TryGetReadingBiography(out _), "服务器移除原条目后选中与阅读同步回退");
        state.SyncAvailableAppearances(Array.Empty<string>(), A, A);
        Check(state.SelectedAppearanceId.Length == 0 && !state.HasOverlay, "空目录同时清空选择与阅读");
        state.SyncAvailableAppearances(new[] { A }, A, A); state.OpenStory(.2f); state.ResetForCloseOrDisconnect();
        Check(!state.HasOverlay && !state.HasPendingSwitchConfirmation && !state.TryGetReadingBiography(out _), "断线清空阅读与确认");
    }

    private static void UnsupportedIsReadOnly()
    {
        var net = new RecordingTransport();
        using var client = new AppearanceClient(net, () => net, () => 10, new AppearanceMessageIds(0, 0, 0, 0, 0));
        client.RequestInfo(true); client.HandleSceneEntered(); client.Tick();
        Check(!client.SwitchAppearance(B) && !client.StartTrial(B) && !client.CancelTrial(), "无消息号时所有写操作拒绝");
        Check(!client.Ready && !client.IsSupported && net.Calls.Count == 0 && net.Notifies.Count == 0, "无消息号不得发包或注册错误消息号");
        var state = new AppearancePanelState(); state.SyncAvailableAppearances(new[] { A, B }, A, A);
        foreach (string id in new[] { A, B })
        {
            state.SelectAppearance(id, .4f); state.OpenStory(.4f); state.CloseStory(AppearanceStoryCloseReason.Skip);
        }
        Check(net.Calls.Count == 0, "只读阅读状态迁移无购买/扣卡/试用/切换请求");
    }

    private static void BiographyAndPreviewLayers()
    {
        var state = new AppearancePanelState();
        state.SyncAvailableAppearances(new[] { A, B, "01_ice_sword_girl" }, A, A);
        state.SelectAppearance(B, .42f);
        state.RequestSwitch(B, A, 2, 1);
        Check(state.OpenBiography(.42f) && state.IsBiographyOpen && !state.HasPendingSwitchConfirmation,
            "进入人物小传清除旧扣卡确认");
        Check(state.RequestSwitch(B, A, 2, 1) == AppearanceSwitchIntentOutcome.Ignored,
            "人物小传层拒绝资产确认");
        Check(state.OpenStory(.42f), "从小传进入一段往事");
        state.SaveStoryPosition(.23f);
        Check(state.OpenPortraitPreview(.42f) && state.IsPortraitPreviewOpen, "阅读中可安全预览形象");
        Check(!state.TryConsumePendingSwitchConfirm(out _) &&
            state.RequestSwitch(B, A, 2, 1) == AppearanceSwitchIntentOutcome.Ignored, "大图层无旧确认且拒绝资产确认");
        Check(state.ClosePortraitPreview() && state.IsStoryOpen && state.ReadingAppearanceId == B &&
            Math.Abs(state.SavedStoryScrollNormalized - .23f) < .0001f, "大图返回同人物同篇章及阅读位置");
        Check(state.CloseStory(AppearanceStoryCloseReason.Back) && state.IsBiographyOpen, "普通返回从故事到人物小传");
        Check(state.OpenStory(.42f) && Math.Abs(state.SavedStoryScrollNormalized - .23f) < .0001f,
            "再次阅读保留本人的阅读位置");
        Check(state.CloseStory(AppearanceStoryCloseReason.Skip) && !state.HasOverlay && state.SelectedAppearanceId == B,
            "跳过直接返回同人物详情");
        state.RequestSwitch(B, A, 2, 1);
        Check(state.OpenPortraitPreview(.42f) && !state.HasPendingSwitchConfirmation, "由扣卡确认进入大图时旧确认失效");
        Check(state.ClosePortraitPreview() && !state.HasOverlay && !state.TryConsumePendingSwitchConfirm(out _),
            "大图返回后旧确认不能提交");
        state.OpenBiography(.42f); state.OpenPortraitPreview(.42f);
        Check(state.ClosePortraitPreview() && state.IsBiographyOpen, "从小传预览返回小传");
        state.SelectAppearance(A, .42f);
        Check(!state.HasOverlay && state.ReadingAppearanceId.Length == 0 && state.SavedStoryScrollNormalized == 1,
            "换人物重置当前篇章与阅读位置");
        state.SelectAppearance("01_ice_sword_girl", .6f);
        Check(!state.OpenBiography(.6f) && !state.OpenStory(.6f) && state.OpenPortraitPreview(.6f),
            "暂无档案仍可预览且不借用小传故事");
        state.ResetForRoleChange();
        Check(state.SelectedAppearanceId.Length == 0 && state.ReadingAppearanceId.Length == 0 &&
            state.SavedListScrollNormalized == 1 && state.SavedStoryScrollNormalized == 1 && !state.HasOverlay,
            "换角色清空界面选择与全部阅读状态");
    }

    private static void ClientLifetime()
    {
        var ids = AppearanceMessageIds.Generated;
        Check(ids.IsSupported && ids.NotifyState != 0, "使用实际生成消息号");
        float now = 10; object connection = new object(); var net = new RecordingTransport();
        using var client = new AppearanceClient(net, () => connection, () => now);
        client.RequestInfo(true);
        Check(net.Calls.Count == 1 && net.Calls[0].Id == ids.Get && net.Calls[0].Request is GetAppearanceInfoRequest, "只读拉取只发正式Get请求");
        net.Calls[0].Response(new GetAppearanceInfoResponse { Info = Info() });
        Check(client.Info.PermanentAppearanceId == A && client.Info.ActiveAppearanceId == B && client.Info.TrialAppearanceId == B,
            "永久与试用不同人时不混淆快照");
        now += 120;
        Check(client.TrialRemainingSeconds == 3480 && client.HasActiveTrial, "倒计时沿服务器时间推进");
        client.RequestInfo(true); int old = net.Calls.Count - 1;
        connection = new object();
        net.Calls[old].Response(new GetAppearanceInfoResponse { Info = Info("old-connection", "old-connection") });
        Check(client.Info == null && !client.InfoPending, "连接更换后即使尚未Tick也拒收旧回包");
        client.RequestInfo(true); old = net.Calls.Count - 1;
        net.PlayerId--;
        net.Calls[old].Response(new GetAppearanceInfoResponse { Info = Info("old-role", "old-role") });
        Check(client.Info == null && !client.InfoPending, "同连接换角色后即使尚未Tick也拒收旧回包");
        client.RequestInfo(true); old = net.Calls.Count - 1;
        net.Disconnect(); net.Calls[old].Response(new GetAppearanceInfoResponse { Info = Info() });
        Check(client.Info == null && !client.InfoPending && !client.ActionPending, "断线后迟到回包无效");
        net.Notifies[ids.NotifyState](new MessageContent
        {
            MessageId = ids.NotifyState,
            SerializedMessage = new AppearanceStateNotify { Info = Info() }.ToByteString()
        });
        Check(client.Info == null, "断线后迟到推送不得恢复旧角色快照");
        net.IsReady = true; client.RequestInfo(true); old = net.Calls.Count - 1;
        client.Dispose(); net.Calls[old].Response(new GetAppearanceInfoResponse { Info = Info() });
        Check(client.Info == null && !client.InfoPending, "销毁后迟到回包无效");
    }

    private static void WriteRetryRemainsOriginalContract()
    {
        object connection = new object(); var net = new RecordingTransport();
        using var client = new AppearanceClient(net, () => connection, () => 100);
        Check(client.SwitchAppearance(B), "隔离传输中的正式切换请求可建立");
        var first = (SwitchAppearanceRequest)net.Calls[0].Request;
        Check(first.ClientRequestId != 0, "原写请求带非零幂等键");
        net.Calls[0].Error(OutstandingCalls.TimeoutDetail);
        Check(net.Calls.Count == 2 && ReferenceEquals(net.Calls[0].Request, net.Calls[1].Request), "超时仅原请求同键重试");
        net.Calls[1].Error(OutstandingCalls.TimeoutDetail);
        Check(net.Calls.Count == 2 && client.RequiresReconnect && !client.ActionPending, "第二次不明结果收口为需重连");
        Check(!client.SwitchAppearance(A) && net.Calls.Count == 2, "结果不明时不能改参数重投");

        var secondNet = new RecordingTransport();
        using var second = new AppearanceClient(secondNet, () => connection, () => 200);
        Check(second.StartTrial(B), "隔离传输中的试用请求可建立");
        secondNet.PlayerId--;
        secondNet.Calls[0].Response(new StartAppearanceTrialResponse { Info = Info() });
        Check(second.Info == null && !second.ActionPending, "同连接换角色后旧写回包也无效");
    }

    private static void SeriesCollectionModelsAndState()
    {
        var info = new PlayerAppearanceInfo
        {
            PermanentAppearanceId = A,
            ActiveAppearanceId = A,
            Appearances =
            {
                new CharacterAppearanceEntry { ConfigId = 2, AppearanceId = A, Name = "金发带Q道童", Owned = true, SeriesId = 1 },
                new CharacterAppearanceEntry { ConfigId = 8, AppearanceId = "09_bamboo_archer_girl", Name = "竹弓少女", Owned = true, SeriesId = 1 },
                new CharacterAppearanceEntry { ConfigId = 9, AppearanceId = B, Name = "赤枪少女", Owned = true, SeriesId = 1 },
                new CharacterAppearanceEntry { ConfigId = 3, AppearanceId = "01_ice_sword_girl", Name = "冰剑少女", Owned = false, SeriesId = 2 },
            },
            SeriesProgress =
            {
                new CharacterAppearanceSeriesProgress
                {
                    SeriesId = 1, SeriesKey = "lantern_courtyard", Name = "留灯院旧友", SortOrder = 1,
                    OwnedCount = 3, TotalCount = 3, Completed = true,
                    CompletionBadgeName = "留灯同袍", EpilogueTitle = "灯下三碗面",
                    MemberAppearanceIds = { A, "09_bamboo_archer_girl", B }
                },
                new CharacterAppearanceSeriesProgress
                {
                    SeriesId = 2, SeriesKey = "elemental_blades", Name = "水火冰锋", SortOrder = 2,
                    OwnedCount = 0, TotalCount = 3, Completed = false,
                    CompletionBadgeName = "三灵同契", EpilogueTitle = "霜火沧澜试",
                    MemberAppearanceIds = { "01_ice_sword_girl" }
                }
            }
        };

        Check(AppearanceSeriesModels.FilterBySeries(info, 0).Count == 4, "全部系列不过滤条目");
        Check(AppearanceSeriesModels.FilterBySeries(info, 1).Count == 3, "按系列 1 仅保留 3 位成员");
        Check(AppearanceSeriesModels.FilterBySeries(info, 2).Count == 1, "按系列 2 仅保留 1 位成员");
        Check(AppearanceSeriesModels.CountCompletedSeries(info) == 1, "已集齐系列计数精确");
        Check(!AppearanceSeriesModels.TryGetUnlockedEpilogue(info, 2, _ => "正文", out _), "未集齐系列严禁解锁合篇");
        Check(AppearanceSeriesModels.TryGetUnlockedEpilogue(info, 1, _ => "合篇正文内容", out var epilogue)
            && epilogue.BadgeName == "留灯同袍" && epilogue.EpilogueTitle == "灯下三碗面" && epilogue.EpilogueBody == "合篇正文内容",
            "集齐系列应解锁印章名与合篇正文");

        var state = new AppearancePanelState();
        state.SyncAvailableAppearances(new[] { A, B }, A, A);
        state.SelectAppearance(B, .5f);
        state.SelectSeries(1, .5f);
        Check(state.SelectedSeriesId == 1, "系列选中状态保存");
        state.RequestSwitch(B, A, 2, 1);
        Check(!state.OpenSeriesEpilogue(2, false, "正文", .5f) && !state.HasPendingSwitchConfirmation,
            "尝试打开未集齐合篇拒绝且清除旧扣卡确认");
        Check(state.OpenSeriesEpilogue(1, true, "合篇正文内容", .5f) && state.IsSeriesEpilogueOpen && state.ReadingSeriesEpilogueId == 1,
            "已集齐合篇可打开");
        Check(state.RequestSwitch(B, A, 2, 1) == AppearanceSwitchIntentOutcome.Ignored, "阅读系列合篇时隔离资产确认");
        Check(state.DismissActiveOverlay() && !state.IsSeriesEpilogueOpen && state.ReadingSeriesEpilogueId == 0,
            "DismissActiveOverlay 可收起合篇层");
        state.OpenSeriesEpilogue(1, true, "合篇正文内容", .5f);
        state.SyncSeriesSelection(new uint[] { 2 }, _ => false);
        Check(state.SelectedSeriesId == 0 && !state.IsSeriesEpilogueOpen, "系列下架或失效时自动回退全部并收起合篇");
        state.SelectSeries(2, .5f);
        state.ResetForRoleChange();
        Check(state.SelectedSeriesId == 0 && !state.HasOverlay, "换角色重置系列筛选");
    }

    private static int Main()
    {
        try
        {
            CatalogAndState(); BiographyAndPreviewLayers(); UnsupportedIsReadOnly(); ClientLifetime(); WriteRetryRemainsOriginalContract(); SeriesCollectionModelsAndState();
            Console.WriteLine($"PASS: {_checks} production biography/state/client/transport checks; Unity and network not started.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL after {_checks} checks: {error}"); return 1; }
    }
}
