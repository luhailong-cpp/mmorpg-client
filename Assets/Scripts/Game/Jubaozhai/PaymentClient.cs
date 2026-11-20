using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;
using Paymentpb;

namespace MmorpgClient.Game.Jubaozhai
{
    /// <summary>渠道 SDK 拉起之后交回的结果。SDK 说成功不算数,付没付只看服务端。</summary>
    public struct PayLaunch
    {
        /// <summary>非空 = 没拉起来或玩家放弃,给玩家看的话。</summary>
        public string Error;
        /// <summary>App Store:StoreKit 2 的 transaction id。</summary>
        public string AppleTransactionId;
        /// <summary>Google Play:purchase token 与商品号。</summary>
        public string GooglePurchaseToken;
        public string GoogleProductId;
    }

    /// <summary>
    /// 一个支付渠道的平台 SDK 接缝。服务端已把 SDK 要的参数全部算好(CreatePayIntentResponse),桥只负责原样交给 SDK,
    /// 不拼参数、不算签名、不判断付没付。新接一个平台 = 写一个子类并在 PaymentClient.RegisterPlatformBridges 登记。
    /// </summary>
    public abstract class PayBridge
    {
        /// <summary>下单前的准备。只有微信小游戏要用:wx.login 的 code(服务端换 session_key 算签名)。</summary>
        public virtual void Prepare(Action<string> loginCode) => loginCode("");
        /// <summary>拉起渠道 SDK;完成、失败或玩家放弃后回调恰好一次(主线程)。</summary>
        public abstract void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done);
        /// <summary>服务端确认记账之后结束商店交易(StoreKit finish)。别的渠道不用实现。</summary>
        public virtual void Finish(PayLaunch purchase) { }
        /// <summary>商店补交的未结束交易(上次没走完的购买):有就取走一笔。订单号可能为空,服务端按商店里的绑定认订单。别的渠道没有。</summary>
        public virtual bool TryTakeRedelivered(out string orderId, out PayLaunch purchase) { orderId = ""; purchase = default; return false; }
    }

    /// <summary>本地联调渠道:没有 SDK,到账通知由联调工具发给支付服务,这里直接进入等待。</summary>
    public sealed class DevPayBridge : PayBridge
    {
        public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done) => done(default);
    }

    /// <summary>
    /// 真钱支付的客户端流程(服务端 docs/design/payment-recharge.md §22):选渠道 → CreatePayIntent → 渠道 SDK → 以服务端为准确认。
    /// 二段式渠道(微信、支付宝、小游戏)在 SDK 返回后轮询 GetPayIntent;商店渠道(Apple / Google)把凭证交给 SubmitPurchase。
    /// 到账与发货都由服务端推进,这里不改任何资产,也不保存任何凭证:掉线或杀进程后,服务端靠渠道回调与补查照常记账。
    /// </summary>
    public sealed class PaymentClient : IDisposable
    {
        public const double PollIntervalSeconds = 2, PollWindowSeconds = 90;
        private readonly IBattleTransport _net;
        private readonly Dictionary<PayChannel, PayBridge> _bridges = new() { [PayChannel.Devpay] = new DevPayBridge() };
        private int _generation;
        private bool _disposed;
        private string _pollOrder = "", _launchFailure = "";
        private double _now, _nextPoll, _pollUntil;

        public bool Busy { get; private set; }
        public string Status { get; private set; } = "";
        /// <summary>当前平台开放的支付方式(ListChannels 的结果,按服务端给的展示顺序)。</summary>
        public IReadOnlyList<PayChannel> Channels { get; private set; } = Array.Empty<PayChannel>();
        /// <summary>正在等结果的订单号;空串表示没有在途支付。</summary>
        public string PendingOrder => _pollOrder;
        public event Action Changed;
        /// <summary>订单号:支付有了结局(已付或已关闭),界面据此重读订单。</summary>
        public event Action<string> Settled;

        public PaymentClient(IBattleTransport net)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _net.Disconnected += Disconnected;
            RegisterPlatformBridges();
        }

        /// <summary>登记一个渠道的平台 SDK 桥。没登记桥的渠道不能支付。</summary>
        public void Register(PayChannel channel, PayBridge bridge) => _bridges[channel] = bridge ?? throw new ArgumentNullException(nameof(bridge));

        private void RegisterPlatformBridges()
        {
#if MMORPG_UNITY_IAP && (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            // 商店桥(Unity IAP)全进程一个。登记时就连接商店:上次没结束的交易会被补交,由 Tick 空闲时提交。
            var storeChannel = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.IPhonePlayer ? PayChannel.Apple : PayChannel.Google;
            var store = StorePayBridge.Shared(storeChannel);
            Register(storeChannel, store);
            store.Prepare(_ => { });
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            Register(PayChannel.WechatPay, new WechatAppPayBridge());
            Register(PayChannel.Alipay, new AlipayAppPayBridge());
#elif UNITY_WEBGL && !UNITY_EDITOR
            Register(PayChannel.WechatMinigame, new WechatMinigamePayBridge());
            Register(PayChannel.DouyinMinigame, new DouyinMinigamePayBridge());
#endif
        }

        /// <summary>这个安装包的发行形态,决定服务端给哪些支付方式。</summary>
        public static PayClientPlatform Platform
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return PayClientPlatform.IosApp;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return PayClientPlatform.AndroidApp;
#elif UNITY_WEBGL && !UNITY_EDITOR
                return MinigamePay.Host() switch { 1 => PayClientPlatform.WechatMinigame, 2 => PayClientPlatform.DouyinMinigame, _ => PayClientPlatform.Desktop };
#else
                return PayClientPlatform.Desktop;
#endif
            }
        }

        private static string DeviceOs
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return MinigamePay.DeviceOs();
#else
                return UnityEngine.Application.platform == UnityEngine.RuntimePlatform.IPhonePlayer ? "ios"
                    : UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android ? "android" : "";
#endif
            }
        }

        public static string ChannelName(PayChannel channel) => channel switch
        {
            PayChannel.WechatPay or PayChannel.WechatMinigame => "微信支付",
            PayChannel.Alipay => "支付宝",
            PayChannel.DouyinMinigame => "抖音支付",
            PayChannel.Apple => "App Store",
            PayChannel.Google => "Google Play",
            PayChannel.Devpay => "联调支付",
            _ => "其他方式"
        };

        public static string DescribeTip(uint tip) => tip switch
        {
            (uint)payment_error.KPaymentChannelUnavailable => "该支付方式当前不可用。",
            (uint)payment_error.KPaymentOrderNotFound => "订单不存在。",
            (uint)payment_error.KPaymentOrderNotPayable => "订单当前不能付款，请刷新订单。",
            (uint)payment_error.KPaymentOrderExpired => "订单已超过支付时限，请重新下单。",
            (uint)payment_error.KPaymentChannelMismatch => "这笔订单已选择其他支付方式，如需更换请重新下单。",
            (uint)payment_error.KPaymentCurrencyUnsupported => "该支付方式不支持这笔订单的币种。",
            (uint)payment_error.KPaymentBusy => "支付服务繁忙，请稍后再试。",
            (uint)payment_error.KPaymentLoginRequired => "请重新登录后再支付。",
            // 未成年人充值限额(服务端 docs/design/real-name-anti-addiction.md §5.6),文案与 Tip.xlsx 一致。
            (uint)payment_error.KPaymentRealNameRequired => "请先完成实名认证再充值。",
            (uint)payment_error.KPaymentMinorNotAllowed => "根据国家规定，未满8周岁的用户不能充值。",
            (uint)payment_error.KPaymentMinorSingleLimit => "超过未成年人单次充值上限：未满16周岁50元，未满18周岁100元。",
            (uint)payment_error.KPaymentMinorDailyLimit => "今日充值已达未成年人单日上限，待付款的订单过期后才释放额度。",
            (uint)payment_error.KPaymentMinorMonthlyLimit => "本月充值已达未成年人上限：未满16周岁200元，未满18周岁400元；待付款的订单过期后才释放额度。",
            _ => JubaozhaiClient.DescribeTip(tip)
        };

        public void ListChannels() => Call(ClientPlayerPaymentRpc.ListPayChannelsMessageId, new ListPayChannelsRequest { Platform = Platform },
            ListPayChannelsResponse.Parser, r => r.ErrorMessage, "正在读取支付方式…", r =>
            {
                Channels = new List<PayChannel>(r.Channels);
                Status = Channels.Count == 0 ? "当前平台没有开放的支付方式。" : "";
            });

        /// <summary>为一张待付款订单发起支付。一张订单只能用一种支付方式,换方式须重新下单(服务端约束)。</summary>
        public void Pay(string orderId, PayChannel channel)
        {
            if (_disposed) return;
            if (Busy || _pollOrder.Length != 0) { SetStatus("上一笔支付还在确认，请稍候。"); return; }
            if (!_bridges.TryGetValue(channel, out var bridge)) { SetStatus("这个版本还没有接入该支付方式。"); return; }
            int preparing = ++_generation;
            Busy = true; SetStatus("正在准备支付…");
            bridge.Prepare(loginCode =>
            {
                if (preparing != _generation) return;
                Busy = false;
                Call(ClientPlayerPaymentRpc.CreatePayIntentMessageId, new CreatePayIntentRequest
                { OrderId = orderId, Channel = channel, Platform = Platform, LoginCode = loginCode ?? "", DeviceOs = DeviceOs },
                    CreatePayIntentResponse.Parser, r => r.ErrorMessage, "正在创建支付…", intent =>
                    {
                        if (Finished(orderId, intent.Intent)) return;
                        int launching = ++_generation;
                        Busy = true; Status = "请在支付界面完成付款…";
                        bridge.Launch(intent, result =>
                        {
                            if (launching != _generation) return;
                            Busy = false;
                            if (!string.IsNullOrEmpty(result.AppleTransactionId) || !string.IsNullOrEmpty(result.GooglePurchaseToken))
                                Submit(orderId, channel, result);
                            else Watch(orderId, result.Error ?? "");
                        });
                    });
            });
        }

        /// <summary>把商店凭证交给服务端核验。商店在启动时补发的未完成交易,桥也调这里(订单号取自交易里带的账号标记)。</summary>
        public void Submit(string orderId, PayChannel channel, PayLaunch purchase)
        {
            if (!_bridges.TryGetValue(channel, out var bridge)) return;
            Call(ClientPlayerPaymentRpc.SubmitPurchaseMessageId, new SubmitPurchaseRequest
            {
                Channel = channel, OrderId = orderId ?? "", AppleTransactionId = purchase.AppleTransactionId ?? "",
                GooglePurchaseToken = purchase.GooglePurchaseToken ?? "", GoogleProductId = purchase.GoogleProductId ?? ""
            }, SubmitPurchaseResponse.Parser, r => r.ErrorMessage, "正在核验购买凭证…", r =>
            {
                if (r.FinishTransaction) bridge.Finish(purchase);
                // 补交的交易可能不带订单号:没有可等的订单,核验完就结束。
                if (!Finished(orderId, r.Intent) && !string.IsNullOrEmpty(orderId)) Watch(orderId, "");
            });
        }

        /// <summary>界面每帧调用;now 是单调秒(Time.unscaledTimeAsDouble)。在途支付按间隔查一次服务端。</summary>
        public void Tick(double now)
        {
            _now = now;
            if (_disposed || Busy) return;
            if (_pollOrder.Length == 0)
            {
                if (!_net.IsReady || _net.PlayerId == 0) return;
                foreach (var pair in _bridges)
                    if (pair.Value.TryTakeRedelivered(out string redeliveredOrder, out var purchase))
                    { Submit(redeliveredOrder, pair.Key, purchase); return; }
                return;
            }
            if (now < _nextPoll) return;
            string orderId = _pollOrder;
            _nextPoll = now + PollIntervalSeconds;
            Call(ClientPlayerPaymentRpc.GetPayIntentMessageId, new GetPayIntentRequest { OrderId = orderId },
                GetPayIntentResponse.Parser, r => r.ErrorMessage, "正在确认支付结果…", r =>
                {
                    if (!Finished(orderId, r.Intent) && !StopWatchingIfOverdue()) Status = "等待支付结果…";
                }, () => StopWatchingIfOverdue());
        }

        // SDK 报失败(多半是玩家放弃)时只查一次;正常返回时查满一个窗口。
        private void Watch(string orderId, string launchFailure)
        {
            _pollOrder = orderId; _launchFailure = launchFailure;
            _nextPoll = _now;
            _pollUntil = launchFailure.Length == 0 ? _now + PollWindowSeconds : _now;
            SetStatus(launchFailure.Length == 0 ? "等待支付结果…" : launchFailure);
        }

        private bool StopWatchingIfOverdue()
        {
            if (_pollOrder.Length == 0 || _now < _pollUntil) return false;
            _pollOrder = "";
            Status = _launchFailure.Length != 0 ? _launchFailure : "还没有收到付款结果；如已付款，稍后在订单里刷新即可，请勿重复支付。";
            return true;
        }

        private bool Finished(string orderId, PayIntentView intent)
        {
            if (intent == null || (intent.State != PayIntentState.Paid && intent.State != PayIntentState.Closed)) return false;
            _pollOrder = "";
            Status = intent.State == PayIntentState.Paid ? "支付成功，正在发货。" : "这笔支付已关闭；如已扣款会原路退回。";
            Settled?.Invoke(orderId);
            return true;
        }

        private void Call<T>(uint method, IMessage request, MessageParser<T> parser, Func<T, TipInfoMessage> tipOf, string busyText,
            Action<T> ok, Action failed = null) where T : IMessage<T>
        {
            if (_disposed) return;
            if (!_net.IsReady || _net.PlayerId == 0) { SetStatus("请进入角色后操作。"); return; }
            int generation = ++_generation;
            Busy = true; SetStatus(busyText);
            _net.Call(method, request, parser, response =>
            {
                if (generation != _generation) return;
                Busy = false;
                uint tip = tipOf(response)?.Id ?? 0;
                if (tip != 0) { Status = DescribeTip(tip); failed?.Invoke(); }
                else ok(response);
                Changed?.Invoke();
            }, error =>
            {
                if (generation != _generation) return;
                Busy = false;
                Status = "支付服务暂时没有应答；如已付款，稍后在订单里刷新即可，请勿重复支付。";
                failed?.Invoke();
                Changed?.Invoke();
            });
        }

        private void SetStatus(string value) { Status = value; Changed?.Invoke(); }

        private void Disconnected()
        {
            ++_generation; Busy = false; _pollOrder = "";
            SetStatus("连接已断开；如已付款，重连后在订单里刷新即可，请勿重复支付。");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; ++_generation; _net.Disconnected -= Disconnected;
        }
    }
}
