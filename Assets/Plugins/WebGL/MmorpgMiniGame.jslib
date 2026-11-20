// 微信 / 抖音小游戏的长连接与登录,给 WebSocketDrivers.cs(JsWebSocketDriver)与 MiniGameLogin.cs 用。
// 直接调宿主的全局对象 wx / tt,不依赖两家的 Unity SDK(同 MinigamePay.jslib)。抖音宿主也可能带 wx 兼容对象,所以先判 tt。
// 服务端设计见 docs/design/minigame-websocket-gate.md W10。
mergeInto(LibraryManager.library, {
  // 连接号 → { closed, send(ArrayBuffer), close() }。C# 侧按连接号找回对象。
  $MmorpgWebSockets: { next: 1, live: {} },

  // 开一条 WebSocket,返回连接号。事件一律在本函数返回之后才回调 C#(C# 侧拿到连接号才登记)。
  //   onOpen(id);onBinary(id, ptr, len):一条二进制消息,指针只在回调期间有效;onClosed(id, reason):连不上、对端关闭、出错,至多一次。
  MmorpgWebSocketOpen__deps: ['$MmorpgWebSockets'],
  MmorpgWebSocketOpen: function (urlPtr, onOpen, onBinary, onClosed) {
    var url = UTF8ToString(urlPtr);
    var id = MmorpgWebSockets.next++;
    var entry = { closed: false, send: null, close: null };
    MmorpgWebSockets.live[id] = entry;

    function opened() {
      if (entry.closed) return;
      {{{ makeDynCall('vi', 'onOpen') }}}(id);
    }
    function binary(data) {
      // 文本消息不是本协议的内容,丢弃。按「不是字符串」认二进制,不用 instanceof ArrayBuffer:
      // 宿主从原生层交上来的 ArrayBuffer 不保证和这里的 ArrayBuffer 是同一个构造函数。
      if (entry.closed || !data || typeof data === 'string') return;
      var bytes = new Uint8Array(data);
      var ptr = _malloc(bytes.length || 1);
      HEAPU8.set(bytes, ptr);
      {{{ makeDynCall('viii', 'onBinary') }}}(id, ptr, bytes.length);
      _free(ptr);
    }
    function closed(reason) {
      if (entry.closed) return;
      entry.closed = true;
      delete MmorpgWebSockets.live[id];
      var text = reason || '';
      var size = lengthBytesUTF8(text) + 1;
      var buffer = _malloc(size);
      stringToUTF8(text, buffer, size);
      {{{ makeDynCall('vii', 'onClosed') }}}(id, buffer);
      _free(buffer);
    }
    // 宿主若同步报失败(地址不在合法域名里等),也推迟到本函数返回之后再回调。
    function closedLater(reason) {
      setTimeout(function () { closed(reason); }, 0);
    }

    var host = (typeof tt !== 'undefined' && typeof tt.connectSocket === 'function') ? tt
      : ((typeof wx !== 'undefined' && typeof wx.connectSocket === 'function') ? wx : null);
    try {
      if (host) {
        var task = host.connectSocket({
          url: url,
          fail: function (res) { closedLater((res && res.errMsg) || 'connect failed'); }
        });
        task.onOpen(opened);
        task.onMessage(function (res) { binary(res && res.data); });
        task.onClose(function () { closed(''); });
        task.onError(function (res) { closed((res && res.errMsg) || 'socket error'); });
        entry.send = function (buffer) { task.send({ data: buffer }); };
        entry.close = function () { task.close({}); };
      } else {
        var socket = new WebSocket(url);
        socket.binaryType = 'arraybuffer';
        socket.onopen = opened;
        socket.onmessage = function (event) { binary(event.data); };
        socket.onclose = function () { closed(''); };
        socket.onerror = function () { closed('socket error'); };
        entry.send = function (buffer) { socket.send(buffer); };
        entry.close = function () { socket.close(); };
      }
    } catch (e) {
      closedLater(String((e && e.message) || e));
    }
    return id;
  },

  MmorpgWebSocketSend__deps: ['$MmorpgWebSockets'],
  MmorpgWebSocketSend: function (id, dataPtr, length) {
    var entry = MmorpgWebSockets.live[id];
    if (!entry || entry.closed || !entry.send) return;
    // 拷一份再交给宿主:发送是异步的,WASM 堆上的这段内存在本函数返回后就会被复用。
    entry.send(HEAPU8.slice(dataPtr, dataPtr + length).buffer);
  },

  // 本端主动关:之后的事件不再回调 C#。
  MmorpgWebSocketClose__deps: ['$MmorpgWebSockets'],
  MmorpgWebSocketClose: function (id) {
    var entry = MmorpgWebSockets.live[id];
    if (!entry) return;
    entry.closed = true;
    delete MmorpgWebSockets.live[id];
    try { if (entry.close) entry.close(); } catch (e) { }
  },

  // 0 = 不在小游戏宿主里(普通浏览器),1 = 微信小游戏,2 = 抖音小游戏。
  MmorpgMiniGameHost: function () {
    if (typeof tt !== 'undefined' && typeof tt.login === 'function') return 2;
    if (typeof wx !== 'undefined' && typeof wx.login === 'function') return 1;
    return 0;
  },

  // 向宿主要一次性的登录 code(wx.login / tt.login),回调带 code;失败回空串。只在 MmorpgMiniGameHost() 非 0 时调用。
  MmorpgMiniGameLogin: function (callback) {
    function reply(text) {
      var size = lengthBytesUTF8(text) + 1;
      var buffer = _malloc(size);
      stringToUTF8(text, buffer, size);
      {{{ makeDynCall('vi', 'callback') }}}(buffer);
      _free(buffer);
    }
    var host = (typeof tt !== 'undefined' && typeof tt.login === 'function') ? tt : wx;
    host.login({
      success: function (res) { reply((res && res.code) || ''); },
      fail: function () { reply(''); }
    });
  }
});
