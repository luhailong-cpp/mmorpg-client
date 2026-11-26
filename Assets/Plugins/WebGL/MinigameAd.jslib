// 微信 / 抖音小游戏的激励视频广告桥,给 MinigameAdBridge.cs 用。
// 直接调宿主的全局对象 tt / wx(必须先判 tt,因为抖音宿主可能带 wx 兼容对象)。
// 硬合规与稳定性约束:
// 1. 同 adUnitId 复用单例 RewardedVideoAd 实例,onLoad/onError/onClose 仅绑定一次,严禁重复绑定导致多次发奖回调;
// 2. 严格校验 !!(res && res.isEnded),中途关闭返回 "closed",严禁放行发奖;
// 3. load/show 请求之间强制 >= 1000ms 节流(符合抖音小游戏广告调用频率规范)。
mergeInto(LibraryManager.library, {
  $MinigameAdState: {
    cache: {},
    lastReqMs: 0,
    activeReqId: 0,
    activeCallback: 0,
    readyCallback: 0,
    currentUnitId: ''
  },

  $MinigameAdReply: function (callback, requestId, text) {
    if (!callback) return;
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    {{{ makeDynCall('vii', 'callback') }}}(requestId, buffer);
    _free(buffer);
  },

  $MinigameAdNotifyReady: function (ready) {
    var cb = MinigameAdState.readyCallback;
    if (!cb) return;
    {{{ makeDynCall('vi', 'cb') }}}(ready ? 1 : 0);
  },

  // 2 = 抖音小游戏, 1 = 微信小游戏, 0 = 普通浏览器或不支持。先判 tt 再判 wx。
  MinigameAdHost: function () {
    if (typeof tt !== 'undefined' && tt && typeof tt.createRewardedVideoAd === 'function') return 2;
    if (typeof wx !== 'undefined' && wx && typeof wx.createRewardedVideoAd === 'function') return 1;
    return 0;
  },

  MinigameAdSetReadyCallback__deps: ['$MinigameAdState'],
  MinigameAdSetReadyCallback: function (callback) {
    MinigameAdState.readyCallback = callback || 0;
  },

  $MinigameAdGetOrCreate__deps: ['$MinigameAdState', '$MinigameAdReply', '$MinigameAdNotifyReady'],
  $MinigameAdGetOrCreate: function (unitId) {
    if (!unitId) return null;
    if (MinigameAdState.cache[unitId]) return MinigameAdState.cache[unitId];
    var host = null;
    if (typeof tt !== 'undefined' && tt && typeof tt.createRewardedVideoAd === 'function') host = tt;
    else if (typeof wx !== 'undefined' && wx && typeof wx.createRewardedVideoAd === 'function') host = wx;
    if (!host) return null;

    var ad = host.createRewardedVideoAd({ adUnitId: unitId });
    if (!ad) return null;
    var entry = { ad: ad, ready: false, loading: false };
    MinigameAdState.cache[unitId] = entry;

    ad.onLoad(function () {
      entry.ready = true;
      entry.loading = false;
      if (MinigameAdState.currentUnitId === unitId) MinigameAdNotifyReady(true);
    });

    ad.onError(function (err) {
      entry.ready = false;
      entry.loading = false;
      if (MinigameAdState.currentUnitId === unitId) MinigameAdNotifyReady(false);
      var reqId = MinigameAdState.activeReqId;
      var cb = MinigameAdState.activeCallback;
      if (reqId && cb) {
        MinigameAdState.activeReqId = 0;
        MinigameAdState.activeCallback = 0;
        var msg = (err && (err.errMsg || err.errCode)) ? String(err.errMsg || err.errCode) : 'ad_error';
        MinigameAdReply(cb, reqId, 'error:' + msg);
      }
    });

    ad.onClose(function (res) {
      entry.ready = false;
      if (MinigameAdState.currentUnitId === unitId) MinigameAdNotifyReady(false);
      var reqId = MinigameAdState.activeReqId;
      var cb = MinigameAdState.activeCallback;
      MinigameAdState.activeReqId = 0;
      MinigameAdState.activeCallback = 0;
      if (reqId && cb) {
        var ended = !!(res && res.isEnded);
        MinigameAdReply(cb, reqId, ended ? 'ended' : 'closed');
      }
    });

    return entry;
  },

  MinigameAdPreload__deps: ['$MinigameAdState', '$MinigameAdGetOrCreate', '$MinigameAdNotifyReady'],
  MinigameAdPreload: function (unitIdPtr) {
    try {
      var unitId = UTF8ToString(unitIdPtr);
      if (!unitId) return;
      MinigameAdState.currentUnitId = unitId;
      var entry = MinigameAdGetOrCreate(unitId);
      if (!entry) { MinigameAdNotifyReady(false); return; }
      if (entry.ready) { MinigameAdNotifyReady(true); return; }
      var now = Date.now();
      if (entry.loading || (now - MinigameAdState.lastReqMs) < 1000) return;
      MinigameAdState.lastReqMs = now;
      entry.loading = true;
      var p = entry.ad.load();
      if (p && typeof p.catch === 'function') {
        p.catch(function () {
          entry.ready = false;
          entry.loading = false;
          if (MinigameAdState.currentUnitId === unitId) MinigameAdNotifyReady(false);
        });
      }
    } catch (e) {
      MinigameAdNotifyReady(false);
    }
  },

  MinigameAdIsReady__deps: ['$MinigameAdState'],
  MinigameAdIsReady: function (unitIdPtr) {
    try {
      var unitId = UTF8ToString(unitIdPtr);
      var entry = unitId ? MinigameAdState.cache[unitId] : null;
      return (entry && entry.ready) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  MinigameAdShow__deps: ['$MinigameAdState', '$MinigameAdGetOrCreate', '$MinigameAdReply', '$MinigameAdNotifyReady'],
  MinigameAdShow: function (requestId, unitIdPtr, callback) {
    try {
      var unitId = UTF8ToString(unitIdPtr);
      var entry = MinigameAdGetOrCreate(unitId);
      if (!entry) {
        MinigameAdReply(callback, requestId, 'unavailable');
        return;
      }
      var now = Date.now();
      if ((now - MinigameAdState.lastReqMs) < 1000 && !entry.ready) {
        MinigameAdReply(callback, requestId, 'unavailable');
        return;
      }
      MinigameAdState.lastReqMs = now;
      MinigameAdState.currentUnitId = unitId;
      MinigameAdState.activeReqId = requestId;
      MinigameAdState.activeCallback = callback;

      var failOut = function (err) {
        if (MinigameAdState.activeReqId !== requestId) return;
        MinigameAdState.activeReqId = 0;
        MinigameAdState.activeCallback = 0;
        entry.ready = false;
        MinigameAdNotifyReady(false);
        var msg = (err && (err.errMsg || err.errCode)) ? String(err.errMsg || err.errCode) : 'show_failed';
        MinigameAdReply(callback, requestId, 'error:' + msg);
      };

      var showPromise = entry.ad.show();
      if (showPromise && typeof showPromise.catch === 'function') {
        showPromise.catch(function () {
          var loadPromise = entry.ad.load();
          if (loadPromise && typeof loadPromise.then === 'function') {
            loadPromise.then(function () {
              var retry = entry.ad.show();
              if (retry && typeof retry.catch === 'function') retry.catch(failOut);
            }).catch(failOut);
          } else {
            failOut('load_failed');
          }
        });
      }
    } catch (e) {
      MinigameAdState.activeReqId = 0;
      MinigameAdState.activeCallback = 0;
      MinigameAdReply(callback, requestId, 'error:' + String((e && e.message) || e || 'exception'));
    }
  },

  MinigameAdCancel__deps: ['$MinigameAdState'],
  MinigameAdCancel: function () {
    MinigameAdState.activeReqId = 0;
    MinigameAdState.activeCallback = 0;
  }
});
