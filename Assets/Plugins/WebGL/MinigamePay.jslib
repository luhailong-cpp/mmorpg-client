// 微信 / 抖音小游戏的支付接口,给 MinigamePayBridges.cs 用。直接调宿主的全局对象 wx / tt;
// 回调一律把一个字符串交回 C#(空串 = 宿主报成功;支付结果仍以服务端为准)。
mergeInto(LibraryManager.library, {
  // 把字符串交给 C# 回调(Action<string>),调用后释放。
  $MinigamePayReply: function (callback, text) {
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    {{{ makeDynCall('vi', 'callback') }}}(buffer);
    _free(buffer);
  },

  // 1 = 微信小游戏,2 = 抖音小游戏,0 = 普通浏览器。抖音宿主也可能带 wx 兼容对象,所以先判 tt。
  MinigamePayHost: function () {
    if (typeof tt !== 'undefined' && typeof tt.requestGamePayment === 'function') return 2;
    if (typeof wx !== 'undefined' && typeof wx.requestMidasPaymentGameItem === 'function') return 1;
    return 0;
  },

  MinigamePayDeviceOs: function () {
    var os = '';
    try {
      var host = typeof tt !== 'undefined' ? tt : (typeof wx !== 'undefined' ? wx : null);
      if (host) os = host.getSystemInfoSync().platform || '';
    } catch (e) { os = ''; }
    var size = lengthBytesUTF8(os) + 1;
    var buffer = _malloc(size);
    stringToUTF8(os, buffer, size);
    return buffer; // Unity 取走后释放
  },

  MinigamePayWechatLogin__deps: ['$MinigamePayReply'],
  MinigamePayWechatLogin: function (callback) {
    wx.login({
      success: function (res) { MinigamePayReply(callback, (res && res.code) || ''); },
      fail: function () { MinigamePayReply(callback, ''); }
    });
  },

  MinigamePayWechat__deps: ['$MinigamePayReply'],
  MinigamePayWechat: function (signData, paySig, signature, callback) {
    wx.requestMidasPaymentGameItem({
      signData: UTF8ToString(signData),
      paySig: UTF8ToString(paySig),
      signature: UTF8ToString(signature),
      success: function () { MinigamePayReply(callback, ''); },
      fail: function (res) { MinigamePayReply(callback, (res && res.errMsg) || 'fail'); }
    });
  },

  MinigamePayDouyin__deps: ['$MinigamePayReply'],
  MinigamePayDouyin: function (optionsJson, callback) {
    var options = JSON.parse(UTF8ToString(optionsJson));
    options.success = function () { MinigamePayReply(callback, ''); };
    options.fail = function (res) { MinigamePayReply(callback, (res && res.errMsg) || 'fail'); };
    tt.requestGamePayment(options);
  }
});
