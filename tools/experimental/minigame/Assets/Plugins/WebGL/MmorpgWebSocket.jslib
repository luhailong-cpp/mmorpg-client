// WebGL / 微信、抖音小游戏的 WebSocket 通道(C# 侧见 Assets/Scripts/Net/BrowserWebSocketChannel.cs)。
// 事件按连接排队,C# 每帧主动来取:kind 0=已连接 1=二进制消息 2=错误 3=已关闭。
// 不用 TextEncoder / TextDecoder:部分小游戏运行环境没有这两个全局对象。
var MmorpgWebSocketLib = {
  $MmoWs: {
    next: 1,
    sockets: {},
    utf8: function (text) {
      var s = unescape(encodeURIComponent(String(text)));
      var out = new Uint8Array(s.length);
      for (var i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
      return out;
    },
    push: function (entry, kind, payload) {
      entry.events.push({ kind: kind, payload: payload });
    }
  },

  MmoWsOpen: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = MmoWs.next++;
    var entry = { events: [], socket: null, isTask: false, closed: false };
    MmoWs.sockets[id] = entry;
    var onOpen = function () { MmoWs.push(entry, 0, null); };
    var onMessage = function (data) {
      if (data instanceof ArrayBuffer) {
        MmoWs.push(entry, 1, new Uint8Array(data));
      } else if (data && ArrayBuffer.isView(data)) {
        MmoWs.push(entry, 1, new Uint8Array(data.buffer, data.byteOffset, data.byteLength).slice());
      } else {
        MmoWs.push(entry, 2, MmoWs.utf8('ignored text message'));
      }
    };
    var onError = function (message) { MmoWs.push(entry, 2, MmoWs.utf8(message || 'websocket error')); };
    var onClose = function (code, reason) {
      if (entry.closed) return;
      entry.closed = true;
      MmoWs.push(entry, 3, MmoWs.utf8((code || 0) + ' ' + (reason || '')));
    };
    try {
      if (typeof WebSocket !== 'undefined') {
        var ws = new WebSocket(url);
        ws.binaryType = 'arraybuffer';
        ws.onopen = onOpen;
        ws.onmessage = function (e) { onMessage(e.data); };
        ws.onerror = function () { onError('websocket error'); };
        ws.onclose = function (e) { onClose(e.code, e.reason); };
        entry.socket = ws;
      } else {
        var api = typeof wx !== 'undefined' ? wx : (typeof tt !== 'undefined' ? tt : null);
        if (!api || !api.connectSocket) throw new Error('no WebSocket implementation');
        var task = api.connectSocket({ url: url });
        task.onOpen(onOpen);
        task.onMessage(function (res) { onMessage(res && res.data); });
        task.onError(function (res) { onError(res && res.errMsg); });
        task.onClose(function (res) { onClose(res && res.code, res && res.reason); });
        entry.socket = task;
        entry.isTask = true;
      }
    } catch (err) {
      onError('open failed: ' + (err && err.message));
      onClose(1006, 'open failed');
    }
    return id;
  },

  MmoWsSend: function (id, ptr, length) {
    var entry = MmoWs.sockets[id];
    if (!entry || !entry.socket || entry.closed) return 0;
    // 拷出 wasm 堆再交给平台:堆可能扩容,不能把堆上的视图直接交出去
    var copy = HEAPU8.slice(ptr, ptr + length).buffer;
    try {
      if (entry.isTask) {
        entry.socket.send({ data: copy });
      } else {
        if (entry.socket.readyState !== 1) return 0;
        entry.socket.send(copy);
      }
      return 1;
    } catch (err) {
      MmoWs.push(entry, 2, MmoWs.utf8('send failed: ' + (err && err.message)));
      return 0;
    }
  },

  MmoWsPeekKind: function (id) {
    var entry = MmoWs.sockets[id];
    return entry && entry.events.length ? entry.events[0].kind : -1;
  },

  MmoWsPeekLength: function (id) {
    var entry = MmoWs.sockets[id];
    if (!entry || !entry.events.length) return 0;
    var payload = entry.events[0].payload;
    return payload ? payload.length : 0;
  },

  MmoWsPop: function (id, ptr, capacity) {
    var entry = MmoWs.sockets[id];
    if (!entry || !entry.events.length) return;
    var ev = entry.events.shift();
    if (ev.payload && capacity > 0) HEAPU8.set(ev.payload.subarray(0, capacity), ptr);
  },

  MmoWsClose: function (id) {
    var entry = MmoWs.sockets[id];
    if (!entry) return;
    delete MmoWs.sockets[id];
    try {
      if (entry.isTask) entry.socket.close({ code: 1000, reason: 'client dispose' });
      else if (entry.socket) entry.socket.close(1000, 'client dispose');
    } catch (err) { }
  }
};

autoAddDeps(MmorpgWebSocketLib, '$MmoWs');
mergeInto(LibraryManager.library, MmorpgWebSocketLib);
