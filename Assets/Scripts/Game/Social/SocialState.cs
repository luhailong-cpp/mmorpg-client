using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chatpb;

namespace MmorpgClient.Game.Social
{
    public enum SocialPage { Groups, World, Rumor }
    public enum SocialChannel { Current, World, Guild, Team, System }
    public enum RumorCategory { All, Beast, Treasure, Victory }
    [Serializable] public sealed class SocialPerson
    {
        public ulong Id; public string Name, Portrait, Title; public int Level; public bool Online = true;
    }
    [Serializable] public sealed class SocialMessage
    {
        public string Id, Text, Card, SenderName; public ulong Sender; public long TimeMs; public bool Local;
        public string TimeLabel { get { try { return DateTimeOffset.FromUnixTimeMilliseconds(TimeMs).ToLocalTime().ToString("HH:mm"); } catch { return "时间未知"; } } }
    }
    [Serializable] public sealed class SocialGroup
    {
        public long Id; public string Name, Announcement, Draft = ""; public ulong Owner;
        public bool Pinned, Alerts = true; public int Unread;
        public readonly List<ulong> Members = new(); public readonly List<SocialMessage> Messages = new();
    }
    [Serializable] public sealed class SocialRumor
    {
        public string Id, Title, Text, Place, Coordinates, Icon; public RumorCategory Category;
        public long TimeMs, AppearsAtMs; public bool Ended;
        public string StateLabel(long now)
        {
            if (Ended) return "✓ 已结束";
            if (AppearsAtMs <= 0) return "✓ 喜讯已记";
            long seconds = Math.Max(0, (AppearsAtMs - now + 999) / 1000);
            return seconds == 0 ? "✓ 现身时刻已到 · 示例" : "◷ 即将现身 · " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
    }
    /// <summary>Production starts empty. Only explicit preview state may create sample messages or groups.</summary>
    public sealed class SocialState
    {
        public const int MessageLimit = 120;
        public static readonly SocialChannel[] ChatChannels = { SocialChannel.Current, SocialChannel.World, SocialChannel.Guild, SocialChannel.Team };
        private readonly Func<long> _clock;
        private readonly Dictionary<SocialChannel,List<SocialMessage>> _messages = new();
        private readonly Dictionary<SocialChannel,string> _drafts = new();
        private readonly Dictionary<SocialChannel,int> _unread = new();
        private readonly List<SocialGroup> _groups = new();
        private readonly List<SocialRumor> _rumors = new();
        private readonly Dictionary<ulong,SocialPerson> _people = new();
        public bool IsPreview { get; }
        public ulong PlayerId { get; private set; }
        public bool IsReady { get; private set; }
        public bool Busy { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "请进入角色后刷新频道。";
        public SocialPage Page { get; private set; } = SocialPage.Groups;
        public SocialChannel Channel { get; private set; } = SocialChannel.World;
        public long SelectedGroupId { get; private set; }
        public RumorCategory RumorFilter { get; private set; }
        public string RumorSearch { get; private set; } = "";
        public IReadOnlyList<SocialGroup> Groups => _groups;
        public IReadOnlyList<SocialRumor> Rumors => _rumors;
        public IEnumerable<SocialPerson> People => _people.Values;
        public SocialGroup SelectedGroup => _groups.Find(g => g.Id == SelectedGroupId);
        public long NowMs => _clock();
        public event Action Changed;
        public SocialState(bool preview = false, Func<long> clock = null)
        {
            IsPreview = preview; _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            foreach (SocialChannel c in Enum.GetValues(typeof(SocialChannel))) { _messages[c] = new(); _drafts[c] = ""; _unread[c] = 0; } if (preview) LoadDemo();
        }
        public static string ChannelName(SocialChannel c) => c switch { SocialChannel.Current => "当前", SocialChannel.World => "世界", SocialChannel.Guild => "帮派", SocialChannel.Team => "队伍", _ => "系统" };
        public bool Supports(SocialChannel c) => IsPreview || c == SocialChannel.World;
        public bool CanSend(SocialChannel c) => c != SocialChannel.System && Supports(c) && IsReady && !Busy && !RequiresReconnect;
        public IReadOnlyList<SocialMessage> Messages(SocialChannel c) => _messages[c];
        public string Draft(SocialChannel c) => _drafts[c];
        public int Unread(SocialChannel c) => _unread[c];
        public void SetDraft(SocialChannel c, string value) => _drafts[c] = value ?? "";
        public SocialPerson Person(ulong id) => _people.TryGetValue(id, out var p) ? p : new SocialPerson { Id = id, Name = id == PlayerId ? "我" : "道友 " + id };
        public void SelectPage(SocialPage page) { Page = page; Changed?.Invoke(); }
        public void SelectChannel(SocialChannel c) { Channel = c; _unread[c] = 0; Changed?.Invoke(); }
        public void MarkRead(SocialChannel c) { _unread[c] = 0; }
        public void SelectGroup(long id) { if (_groups.Any(g => g.Id == id)) { SelectedGroupId = id; SelectedGroup.Unread = 0; Changed?.Invoke(); } }
        public void FilterRumors(RumorCategory category, string search) { RumorFilter = category; RumorSearch = (search ?? "").Trim(); Changed?.Invoke(); }
        public IEnumerable<SocialRumor> FilteredRumors() => _rumors.Where(r => (RumorFilter == RumorCategory.All || r.Category == RumorFilter) && (RumorSearch.Length == 0 || ((r.Text ?? "") + (r.Place ?? "") + (r.Title ?? "")).Contains(RumorSearch)));
        public void RuntimeStatus(bool ready, bool busy, bool reconnect, string status)
        { IsReady = ready; Busy = busy; RequiresReconnect = reconnect; Status = status ?? ""; Changed?.Invoke(); }
        public void Notify(string status) { Status = status ?? ""; Changed?.Invoke(); }
        public void Reset(ulong player = 0)
        {
            PlayerId = player; IsReady = IsPreview; Busy = false; RequiresReconnect = false;
            foreach (var c in _messages.Keys.ToArray()) { _messages[c].Clear(); _drafts[c] = ""; _unread[c] = 0; }
            _groups.Clear(); _rumors.Clear(); _people.Clear(); SelectedGroupId = 0; RumorFilter = RumorCategory.All; RumorSearch = "";
            Status = IsPreview ? "离线预览 · 所有操作仅在本地生效" : "频道信息尚未同步，请刷新。"; Changed?.Invoke();
        }
        public void ApplyHistory(SocialChannel channel, IEnumerable<ChatMessage> rows)
        {
            if (IsPreview) throw new InvalidOperationException("Preview state cannot consume live server data.");
            var result = new List<SocialMessage>();
            foreach (var m in rows ?? Array.Empty<ChatMessage>())
            {
                if (!Matches(channel, m.Channel)) continue;
                result.Add(new SocialMessage { Sender = m.SenderPlayerId, Text = m.Content, TimeMs = m.SendTimeMs,
                    Id = m.SenderPlayerId + ":" + m.SendTimeMs + ":" + m.Content, SenderName = m.SenderPlayerId == PlayerId ? "我" : "道友 " + m.SenderPlayerId });
            }
            result = result.OrderBy(m => m.TimeMs).TakeLast(100).ToList();
            int added = result.Count(m => !_messages[channel].Any(old => old.Id == m.Id));
            _messages[channel] = result;
            if (Page != SocialPage.World || Channel != channel) _unread[channel] += added;
            if (channel == SocialChannel.System)
            {
                _rumors.Clear();
                foreach (var m in result.AsEnumerable().Reverse()) _rumors.Add(new SocialRumor { Id = m.Id, Title = "系统传闻", Text = m.Text, TimeMs = m.TimeMs, Icon = "round_badge_taiji", Category = RumorCategory.All });
            }
            Changed?.Invoke();
        }
        public static bool Matches(SocialChannel c, ChatChannelType proto) => c switch
        { SocialChannel.World => proto == ChatChannelType.World, SocialChannel.Team => proto == ChatChannelType.Team, SocialChannel.System => proto == ChatChannelType.System, _ => false };
        public static int TextLength(string text)
        { int count = 0; for (int i = 0; i < (text ?? "").Length; i++,count++) if (char.IsHighSurrogate(text[i]) && i+1 < text.Length && char.IsLowSurrogate(text[i+1])) i++; return count; }
        public static bool ValidMessage(string text) => !string.IsNullOrWhiteSpace(text) && TextLength(text.Trim()) <= MessageLimit && Encoding.UTF8.GetByteCount(text.Trim()) <= 512;
        public bool PreviewSend(SocialChannel channel, string text, string card = null)
        {
            if (!IsPreview || channel == SocialChannel.System || (!ValidMessage(text) && string.IsNullOrEmpty(card))) return false;
            _messages[channel].Add(NewMessage(PlayerId,text?.Trim() ?? "",card)); if(string.IsNullOrEmpty(card)) _drafts[channel] = "";
            Status = "本地发送成功 · 尚未连接其他玩家"; Changed?.Invoke(); return true;
        }
        public void PreviewIncoming(SocialChannel channel = SocialChannel.World)
        {
            if (!IsPreview) return;
            _messages[channel].Add(NewMessage(2,"纸鹤捎来问候：道友，青岚竹海见！"));
            _unread[channel]++; Status = "收到一条本地示例消息"; Changed?.Invoke();
        }
        private SocialMessage NewMessage(ulong sender,string text,string card=null) => new SocialMessage { Id=Guid.NewGuid().ToString("N"),Sender=sender,Text=text,Card=card,TimeMs=NowMs,Local=true };
        public bool CreateGroup(string name)
        {
            name=(name??"").Trim(); if(!IsPreview || name.Length==0 || TextLength(name)>24 || _groups.Any(g=>g.Name==name)) { Notify("群名需为1–24字，且不能与已有群组重名。"); return false; }
            var g=new SocialGroup { Id=Math.Max(NowMs,_groups.Select(existing=>existing.Id).DefaultIfEmpty(0).Max()+1),Name=name,Owner=PlayerId,Announcement="同道相逢，一起云游。" };g.Members.Add(PlayerId);_groups.Add(g);SelectedGroupId=g.Id;Notify("本地群组已创建");return true;
        }
        public bool Invite(IEnumerable<ulong> ids)
        {
            var g=SelectedGroup;if(!IsPreview||g==null)return false;
            var additions=(ids??Array.Empty<ulong>()).Distinct().Where(id=>_people.ContainsKey(id)&&!g.Members.Contains(id)).ToArray();
            if(g.Members.Count+additions.Length>50){Notify("群组最多容纳50位道友");return false;}g.Members.AddRange(additions);Notify("已邀请所选道友 · 本地演示");return true;
        }
        public bool SaveGroup(string name,string announcement)
        {
            var g=SelectedGroup;name=(name??"").Trim();announcement=(announcement??"").Trim();
            if(!IsPreview||g==null||g.Owner!=PlayerId||name.Length==0||TextLength(name)>24||TextLength(announcement)>100||_groups.Any(other=>other.Id!=g.Id&&other.Name==name))return false;
            g.Name=name;g.Announcement=announcement;Notify("群组资料已保存 · 本地演示");return true;
        }
        public void SetGroupPinned(bool value){if(IsPreview&&SelectedGroup!=null){SelectedGroup.Pinned=value;Notify(value?"群组已置顶":"已取消置顶");}}
        public void SetGroupAlerts(bool value){if(IsPreview&&SelectedGroup!=null){SelectedGroup.Alerts=value;Notify(value?"消息提醒已开启":"消息提醒已关闭");}}
        public bool SendGroup(string text){if(!IsPreview||SelectedGroup==null||!ValidMessage(text))return false;SelectedGroup.Messages.Add(NewMessage(PlayerId,text.Trim()));SelectedGroup.Draft="";Notify("本地群组消息已发送");return true;}
        public bool LeaveGroup(){if(!IsPreview||SelectedGroup==null)return false;_groups.Remove(SelectedGroup);SelectedGroupId=_groups.FirstOrDefault()?.Id??0;Notify("已退出本地示例群组");return true;}
        public void LoadDemo()
        {
            if(!IsPreview)throw new InvalidOperationException("Sample data is restricted to explicit preview hosts.");
            Reset(1);
            var names=new[]{"青云小道","清铃","长风","桂枝","云游子","竹间客"};
            var portraits=new[]{"hero-headband","29_he_xiangu","24_lu_dongbin","lotus-healer","27_ink_kite_ranger","30_han_xiangzi"};
            for(int i=0;i<names.Length;i++)_people[(ulong)i+1]=new SocialPerson{Id=(ulong)i+1,Name=names[i],Level=28+i,Portrait=portraits[i],Title=new[]{"问道初成","清音入梦","仗剑云游","悬壶济世","符御山河","玉笛听风"}[i],Online=i!=4};
            var g=new SocialGroup{Id=101,Name="青云同游",Owner=1,Announcement="今夜戌时，青岚竹海集合。带上回春丹，一起赏月寻仙缘。",Pinned=true};g.Members.AddRange(new ulong[]{1,2,3,4});g.Messages.Add(NewMessage(3,"道友们，今晚一起去竹海寻灵鹤吗？"));g.Messages.Add(NewMessage(2,"我来！纸鹤已经准备好了。"));g.Messages.Add(NewMessage(1,"收到，待我收好葫芦就出发。"));_groups.Add(g);
            var h=new SocialGroup{Id=102,Name="月下茶话",Owner=2,Announcement="桂花煮茶，月下闲谈。",Unread=2};h.Members.AddRange(new ulong[]{1,2,5,6});h.Messages.Add(NewMessage(6,"桥边花灯亮了，大家过来喝茶呀。"));_groups.Add(h);SelectedGroupId=101;
            _messages[SocialChannel.World].AddRange(new[]{NewMessage(3,"青云试炼来两位道友，一起慢慢赏景。","trial"),NewMessage(2,"我来啦，今天的小纸鹤带路特别乖。"),NewMessage(5,"刚炼成一张太清符，给大家沾沾仙气。","talisman"),NewMessage(4,"竹海有灵鹤的消息，道友们记得带上回春丹。","bamboo"),NewMessage(1,"收到，收好葫芦就出发！"),NewMessage(6,"月下听笛，竹间煮茶。愿与诸位同行。")});
            _messages[SocialChannel.Current].Add(NewMessage(4,"丹霞坊桂花酿刚出炉，来尝一盏吧。"));_messages[SocialChannel.Guild].Add(NewMessage(3,"青云观的同门们，今晚先试炼，再去竹海采药。"));
            _rumors.Add(new SocialRumor{Id="r1",Category=RumorCategory.Beast,Title="妖兽现世",Text="赤霄灵鹤（80级）现身预告，竹影深处灵气汇聚。",Place="青岚竹海",Coordinates="126，84",TimeMs=NowMs,AppearsAtMs=NowMs+180000,Icon="round_badge_compass"});
            _rumors.Add(new SocialRumor{Id="r2",Category=RumorCategory.Treasure,Title="珍宝奇遇",Text="星河道人云游丹霞洞府，寻得「九转金丹」！",TimeMs=NowMs-90000,Icon="icon-pill"});
            _rumors.Add(new SocialRumor{Id="r3",Category=RumorCategory.Victory,Title="仙友捷报",Text="云游小队齐心协力，完成青云试炼，获赐一缕太清灵气。",TimeMs=NowMs-180000,Icon="round_badge_taiji"});
            _rumors.Add(new SocialRumor{Id="r4",Category=RumorCategory.Beast,Title="妖兽现世",Text="玄岩山魈（60级）曾现身松风古道。",Place="松风古道",Coordinates="72，119",TimeMs=NowMs-240000,Ended=true,Icon="round_badge_compass"});
            _rumors.Add(new SocialRumor{Id="r5",Category=RumorCategory.Treasure,Title="珍宝奇遇",Text="清铃解开月下灯谜，获赠「桂月宝匣」。",TimeMs=NowMs-300000,Icon="icon-chest"});
            Status="离线预览 · 所有消息、群组与传闻均为本地示例";Changed?.Invoke();
        }
    }
}
