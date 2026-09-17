using System;
using System.Collections.Generic;
using System.Linq;

namespace MmorpgClient.Game.Mail
{
    public enum MailCategory { All, System, Event, Reward }
    public enum MailOperation { Read, Claim, ClaimAll, Delete, ClearRead, Refresh }

    [Serializable]
    public sealed class MailReward
    {
        public string Name = "", IconKey = "";
        public int Count;
        public MailReward Copy() => new() { Name = Name, IconKey = IconKey, Count = Count };
    }

    [Serializable]
    public sealed class MailMessage
    {
        public string Id = "", Title = "", Sender = "", Body = "", BannerKey = "", ActivityId = "";
        public MailCategory Category;
        public long SentAt, ExpiresAt;
        public bool IsRead, Claimed;
        public List<MailReward> Rewards = new();
        public MailMessage Copy() => new() {
            Id = Id, Title = Title, Sender = Sender, Body = Body, BannerKey = BannerKey,
            ActivityId = ActivityId, Category = Category, SentAt = SentAt, ExpiresAt = ExpiresAt,
            IsRead = IsRead, Claimed = Claimed, Rewards = Rewards?.Where(x => x != null).Select(x => x.Copy()).ToList() ?? new()
        };
    }

    public sealed class MailRequest
    {
        public int Generation { get; }
        public MailOperation Operation { get; }
        public IReadOnlyList<string> MessageIds { get; }
        public MailRequest(int generation, MailOperation operation, IEnumerable<string> ids)
        { Generation = generation; Operation = operation; MessageIds = Array.AsReadOnly(ids.ToArray()); }
    }

    /// <summary>Explicit preview fixtures. The production MailUiRoot never calls this class.</summary>
    public static class MailDemoData
    {
        public static List<MailMessage> Create(long now)
        {
            MailReward R(string name, string icon, int count) => new() { Name = name, IconKey = icon, Count = count };
            return new List<MailMessage> {
                new() { Id="midautumn", Category=MailCategory.Event, Title="月满仙山 · 玉兔送福", Sender="仙盟司礼",
                    SentAt=now-3600, ExpiresAt=now+6*86400, BannerKey="event-midautumn", ActivityId="preview-midautumn",
                    Body="亲爱的道友：\n\n桂香入云，月满仙山。中秋雅集已备好花灯与团圆好礼，邀你与道友共赏明月。\n\n随信奉上玉兔团圆礼，愿道友修行顺遂，所行皆有良伴。前往雅集，还可体验月下祈福、花灯游园与玉兔寻宝。\n\n请在邮件到期前领取附件，莫让这份心意久候。\n\n仙盟司礼 敬上",
                    Rewards=new() { R("灵玉","round_badge_lotus",200),R("修行丹","icon-pill",10),R("祈福符","icon-talisman",5),R("团圆礼匣","icon-chest",1) } },
                new() { Id="maintenance", Category=MailCategory.System, Title="维护补偿，请道友查收", Sender="仙盟总管",
                    SentAt=now-7200, ExpiresAt=now+6*86400,
                    Body="亲爱的道友：\n\n仙境例行维护已经结束。感谢道友的耐心等候，随信奉上维护补偿，请在有效期内领取。\n\n本次维护优化了部分界面的显示与操作体验。愿道友重返仙境，一路顺遂。\n\n维护补偿：灵玉 × 100、修行丹 × 5。\n\n仙盟总管 敬上",
                    Rewards=new() { R("灵玉","round_badge_lotus",100),R("修行丹","icon-pill",5) } },
                new() { Id="notice", Category=MailCategory.System, Title="仙境来信 · 游历小记", Sender="云游道人",
                    SentAt=now-86400, ExpiresAt=now+5*86400,
                    Body="道友，见信如晤：\n\n秋风已过山门，桂花渐次开放。行经荷池、石桥与青玉道观时，不妨驻足片刻，看看仙山新景。\n\n这封信仅为游历提醒，不附带奖励。愿道友行有所获，心有所安。\n\n修行有时，闲游亦有所得。待下次云海相逢，再与你共饮一壶清茶。\n\n云游道人 手书" },
                new() { Id="adventure", Category=MailCategory.Reward, Title="云游试炼 · 通关奖励", Sender="试炼执事",
                    SentAt=now-88000, ExpiresAt=now+5*86400,
                    Body="道友，恭喜通关！\n\n你已完成本轮云游试炼。随信奉上历练奖励，感谢你守护仙山的安宁。\n\n请查收附件，休整片刻，再赴下一程山海。\n\n试炼执事 敬上",
                    Rewards=new() { R("修行丹","icon-pill",20),R("云游卷","icon-scroll",3) } },
                new() { Id="claimed", Category=MailCategory.Reward, Title="初入仙山 · 修行见礼", Sender="接引道童",
                    SentAt=now-2*86400, ExpiresAt=now+4*86400, IsRead=true, Claimed=true,
                    Body="道友，欢迎来到五行仙境：\n\n这份入门小礼愿能伴你踏上修行之路。随信奖励已经领取，愿道友心怀善念，自在云游。\n\n接引道童 敬上",
                    Rewards=new() { R("修行丹","icon-pill",10),R("祈福符","icon-talisman",2) } },
                new() { Id="expired", Category=MailCategory.Event, Title="花灯夜游 · 上期赠礼", Sender="仙盟司礼",
                    SentAt=now-8*86400, ExpiresAt=now-86400, IsRead=true,
                    Body="亲爱的道友：\n\n上期花灯夜游已经落幕。愿暖灯长明，映照道友的修行归途。\n\n本邮件已超过领取期限，随信附件已失效，无法继续领取。期待下一次雅集与你再会。\n\n仙盟司礼 敬上",
                    Rewards=new() { R("花灯礼匣","icon-chest",1),R("祈福符","icon-talisman",3) } }
            };
        }
    }
}
