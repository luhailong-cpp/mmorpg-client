// 微信 / 抖音小游戏的支付接口,给 MinigamePayBridges.cs 用。直接调宿主的全局对象 wx / tt;
// 回调一律把一个字符串交回 C#(空串 = 宿主报成功;支付结果仍以服务端为准)。
mergeInto(LibraryManager.library, {
  // 把请求号与字符串交给 C# 回调(PayCallback),调用后释放。
  $MinigamePayReply: function (callback, requestId, text) {
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    {{{ makeDynCall('vii', 'callback') }}}(requestId, buffer);
    _free(buffer);
  },

  // 1 = 微信小游戏,2 = 抖音小游戏,0 = 普通浏览器。抖音宿主也可能带 wx 兼容对象,所以先判 tt。
  MinigamePayHost: function () {
    if (typeof tt !== 'undefined' && tt && typeof tt.requestGamePayment === 'function') return 2;
    if (typeof wx !== 'undefined' && wx && typeof wx.requestMidasPaymentGameItem === 'function') return 1;
    return 0;
  },

  MinigamePayDeviceOs: function () {
    var os = '';
    try {
      var host = (typeof tt !== 'undefined' && tt) ? tt : ((typeof wx !== 'undefined' && wx) ? wx : null);
      if (host) {
        var info = typeof host.getDeviceInfo === 'function' ? host.getDeviceInfo() : null;
        if (info && info.platform) os = String(info.platform);
        else if (typeof host.getSystemInfoSync === 'function') {
          var sys = host.getSystemInfoSync();
          if (sys && sys.platform) os = String(sys.platform);
        }
      }
      os = os.toLowerCase();
    } catch (e) { os = ''; }
    var size = lengthBytesUTF8(os) + 1;
    var buffer = _malloc(size);
    stringToUTF8(os, buffer, size);
    return buffer; // Unity 取走后释放
  },

  MinigamePayWechatLogin__deps: ['$MinigamePayReply'],
  MinigamePayWechatLogin: function (requestId, callback) {
    var replied = false;
    var finish = function (value) {
      if (replied) return;
      replied = true;
      MinigamePayReply(callback, requestId, value);
    };
    try {
      wx.login({
        success: function (res) { finish((res && res.code) || ''); },
        fail: function () { finish(''); }
      });
    } catch (e) {
      finish('');
    }
  },

  MinigamePayWechat__deps: ['$MinigamePayReply'],
  MinigamePayWechat: function (requestId, signData, paySig, signature, callback) {
    var replied = false;
    var finish = function (value) {
      if (replied) return;
      replied = true;
      MinigamePayReply(callback, requestId, value);
    };
    try {
      wx.requestMidasPaymentGameItem({
        signData: UTF8ToString(signData),
        paySig: UTF8ToString(paySig),
        signature: UTF8ToString(signature),
        success: function () { finish(''); },
        fail: function (res) { finish((res && res.errMsg) || 'fail'); }
      });
    } catch (e) {
      finish(String((e && e.message) || e || 'fail'));
    }
  },

  MinigamePayDouyin__deps: ['$MinigamePayReply'],
  MinigamePayDouyin: function (requestId, optionsJson, callback) {
    var replied = false;
    var finish = function (value) {
      if (replied) return;
      replied = true;
      MinigamePayReply(callback, requestId, value);
    };
    try {
      var options = JSON.parse(UTF8ToString(optionsJson));
      options.success = function () { finish(''); };
      options.fail = function (res) { finish((res && res.errMsg) || 'fail'); };
      tt.requestGamePayment(options);
    } catch (e) {
      finish(String((e && e.message) || e || 'fail'));
    }
  }
});
