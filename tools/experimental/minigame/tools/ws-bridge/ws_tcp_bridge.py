#!/usr/bin/env python3
"""WebSocket <-> TCP 本机联调桥(只给开发联调用,不是生产组件)。

为什么需要它:小游戏 / WebGL 客户端只能用 WebSocket,而 gate、battle 只收 muduo TCP。
这个桥把一条 WebSocket 连接接成一条 TCP 连接,只做分帧转换,不解析 protobuf:

  上行(客户端 -> 服务端):一条 WebSocket 二进制消息 = 一个完整 muduo 帧
      [len:int32 BE][nameLen][typeName\\0][body][adler32],原样写进 TCP。
  下行(服务端 -> 客户端):按 muduo 帧头 4 字节长度把 TCP 字节流切帧,
      每帧发成一条 WebSocket 二进制消息。

同一个端口上还提供:
  GET  /ws?target=host:port[&label=gate]  WebSocket 升级,target 必须在白名单里
  *    /api/...                            反向代理到网关(浏览器同源,免 CORS)
  POST /log[?label=x]                      把客户端日志追加到 --log-file(取证用)
  GET  /其它路径                           --static 目录下的静态文件(放 WebGL 包)

只用 Python 标准库。生产形态(负载均衡卸载 TLS、gate 内建 WS 或正式桥、心跳)另行设计。
"""

from __future__ import annotations

import argparse
import asyncio
import base64
import hashlib
import ipaddress
import itertools
import logging
import mimetypes
import os
import struct
import sys
import time
import urllib.parse
from dataclasses import dataclass, field
from pathlib import Path

WS_GUID = b"258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

MUDUO_HEADER_LEN = 4
# nameLen(4) + 至少 1 字节类型名 + '\0'(1) + adler32(4);与客户端 MuduoCodec.MinMessageLen 一致
MUDUO_MIN_LEN = 10
# gate / battle 的 codec 入站单帧上限是 64KB,所以上行整帧(含 4 字节长度头)不得超过它
UPSTREAM_MAX_FRAME = 64 * 1024
# 客户端 MuduoCodec 解码上限 64MB;下行只做合法性检查,不按 64KB 截
DOWNSTREAM_MAX_FRAME = 64 * 1024 * 1024
# 发往客户端的写缓冲超过这个量就停读 TCP(背压)
WS_WRITE_HIGH_WATER = 2 * 1024 * 1024
PING_INTERVAL_SECONDS = 20.0
MAX_HTTP_HEADER_BYTES = 16 * 1024
MAX_LOG_BODY_BYTES = 64 * 1024
MAX_PROXY_BODY_BYTES = 1024 * 1024

OP_CONT, OP_TEXT, OP_BIN, OP_CLOSE, OP_PING, OP_PONG = 0x0, 0x1, 0x2, 0x8, 0x9, 0xA

CLOSE_NORMAL = 1000
CLOSE_PROTOCOL_ERROR = 1002
CLOSE_UNSUPPORTED_DATA = 1003
CLOSE_POLICY = 1008
CLOSE_TOO_BIG = 1009
CLOSE_INTERNAL = 1011

log = logging.getLogger("ws_bridge")


class WsProtocolError(Exception):
    def __init__(self, code: int, reason: str):
        super().__init__(reason)
        self.code = code
        self.reason = reason


def websocket_accept(key: str) -> str:
    digest = hashlib.sha1(key.encode("ascii") + WS_GUID).digest()
    return base64.b64encode(digest).decode("ascii")


def apply_mask(data: bytes, mask: bytes) -> bytes:
    if not data:
        return data
    repeated = (mask * (len(data) // 4 + 1))[: len(data)]
    return (int.from_bytes(data, "big") ^ int.from_bytes(repeated, "big")).to_bytes(len(data), "big")


def encode_ws_frame(opcode: int, payload: bytes = b"") -> bytes:
    """服务端发出的帧不加掩码(RFC 6455 §5.1)。"""
    n = len(payload)
    head = bytes([0x80 | opcode])
    if n < 126:
        head += bytes([n])
    elif n < 1 << 16:
        head += bytes([126]) + struct.pack("!H", n)
    else:
        head += bytes([127]) + struct.pack("!Q", n)
    return head + payload


def encode_close(code: int, reason: str = "") -> bytes:
    return encode_ws_frame(OP_CLOSE, struct.pack("!H", code) + reason.encode("utf-8")[:120])


def validate_upstream_frame(message: bytes) -> None:
    """一条上行 WebSocket 消息必须恰好是一个 muduo 帧。"""
    if len(message) > UPSTREAM_MAX_FRAME:
        raise WsProtocolError(CLOSE_TOO_BIG, f"upstream frame {len(message)}B > {UPSTREAM_MAX_FRAME}B")
    if len(message) < MUDUO_HEADER_LEN + MUDUO_MIN_LEN:
        raise WsProtocolError(CLOSE_POLICY, f"upstream frame too short ({len(message)}B)")
    declared = int.from_bytes(message[:MUDUO_HEADER_LEN], "big", signed=True)
    if declared != len(message) - MUDUO_HEADER_LEN:
        raise WsProtocolError(CLOSE_POLICY, f"upstream len field {declared} != payload {len(message) - MUDUO_HEADER_LEN}")


class MuduoFrameSplitter:
    """把下行 TCP 字节流按 muduo 长度头切成完整帧。"""

    def __init__(self, max_frame: int = DOWNSTREAM_MAX_FRAME):
        self._buf = bytearray()
        self._max = max_frame

    def feed(self, data: bytes) -> list[bytes]:
        self._buf += data
        frames: list[bytes] = []
        while len(self._buf) >= MUDUO_HEADER_LEN:
            declared = int.from_bytes(self._buf[:MUDUO_HEADER_LEN], "big", signed=True)
            if declared < MUDUO_MIN_LEN or declared + MUDUO_HEADER_LEN > self._max:
                raise WsProtocolError(CLOSE_INTERNAL, f"bad downstream len field {declared}")
            total = declared + MUDUO_HEADER_LEN
            if len(self._buf) < total:
                break
            frames.append(bytes(self._buf[:total]))
            del self._buf[:total]
        return frames

    @property
    def pending(self) -> int:
        return len(self._buf)


@dataclass
class HttpRequest:
    method: str
    target: str
    version: str
    headers: dict[str, str]
    raw_head: bytes

    @property
    def path(self) -> str:
        return urllib.parse.urlsplit(self.target).path

    @property
    def query(self) -> dict[str, str]:
        return dict(urllib.parse.parse_qsl(urllib.parse.urlsplit(self.target).query))

    def header(self, name: str) -> str:
        return self.headers.get(name.lower(), "")


async def read_http_request(reader: asyncio.StreamReader) -> HttpRequest | None:
    try:
        head = await reader.readuntil(b"\r\n\r\n")
    except asyncio.IncompleteReadError:
        return None
    except asyncio.LimitOverrunError:
        raise WsProtocolError(CLOSE_POLICY, "http header too large")
    if len(head) > MAX_HTTP_HEADER_BYTES:
        raise WsProtocolError(CLOSE_POLICY, "http header too large")
    lines = head.decode("latin-1").split("\r\n")
    parts = lines[0].split(" ")
    if len(parts) != 3:
        raise WsProtocolError(CLOSE_POLICY, f"bad request line {lines[0]!r}")
    headers: dict[str, str] = {}
    for line in lines[1:]:
        if not line:
            continue
        name, _, value = line.partition(":")
        headers[name.strip().lower()] = value.strip()
    return HttpRequest(parts[0].upper(), parts[1], parts[2], headers, head)


def http_response(status: int, reason: str, body: bytes = b"", headers: dict[str, str] | None = None) -> bytes:
    all_headers = {"Content-Length": str(len(body)), "Connection": "close", "Access-Control-Allow-Origin": "*"}
    all_headers.update(headers or {})
    head = f"HTTP/1.1 {status} {reason}\r\n" + "".join(f"{k}: {v}\r\n" for k, v in all_headers.items()) + "\r\n"
    return head.encode("latin-1") + body


@dataclass
class BridgeConfig:
    host: str = "127.0.0.1"
    port: int = 9100
    allow: list[str] = field(default_factory=list)
    gateway: str = "http://127.0.0.1:8081"
    static_root: Path | None = None
    log_file: Path = Path("ws_bridge_client.log")
    ping_interval: float = PING_INTERVAL_SECONDS


class TargetPolicy:
    """target 白名单:默认只放本机;--allow 可加主机名、IP 或网段(真机联调时加本机局域网 IP)。"""

    def __init__(self, extra: list[str]):
        self._names = {"localhost"}
        self._nets = [ipaddress.ip_network("127.0.0.0/8"), ipaddress.ip_network("::1/128")]
        for item in extra:
            try:
                self._nets.append(ipaddress.ip_network(item, strict=False))
            except ValueError:
                self._names.add(item.lower())

    def parse(self, target: str) -> tuple[str, int]:
        if not target:
            raise WsProtocolError(CLOSE_POLICY, "missing target")
        if target.startswith("["):
            host, _, rest = target[1:].partition("]")
            port_text = rest.lstrip(":")
        else:
            host, _, port_text = target.rpartition(":")
        if not host or not port_text.isdigit() or not 0 < int(port_text) < 65536:
            raise WsProtocolError(CLOSE_POLICY, f"bad target {target!r}")
        if not self.allowed(host):
            raise WsProtocolError(CLOSE_POLICY, f"target host {host!r} not allowed")
        return host, int(port_text)

    def allowed(self, host: str) -> bool:
        if host.lower() in self._names:
            return True
        try:
            addr = ipaddress.ip_address(host)
        except ValueError:
            return False
        return any(addr in net for net in self._nets)


@dataclass
class SessionStats:
    sid: int
    peer: str
    target: str
    label: str
    started: float = field(default_factory=time.monotonic)
    up_frames: int = 0
    up_bytes: int = 0
    down_frames: int = 0
    down_bytes: int = 0
    max_down_frame: int = 0
    decode_errors: int = 0
    close: str = ""

    def summary(self) -> str:
        return (
            f"session={self.sid} label={self.label or '-'} target={self.target} peer={self.peer} "
            f"up_frames={self.up_frames} up_bytes={self.up_bytes} down_frames={self.down_frames} "
            f"down_bytes={self.down_bytes} max_down_frame={self.max_down_frame} "
            f"decode_errors={self.decode_errors} seconds={time.monotonic() - self.started:.1f} close={self.close}"
        )


class WsSession:
    def __init__(self, reader, writer, tcp_reader, tcp_writer, stats: SessionStats, ping_interval: float):
        self.reader = reader
        self.writer = writer
        self.tcp_reader = tcp_reader
        self.tcp_writer = tcp_writer
        self.stats = stats
        self.ping_interval = ping_interval
        self.closing = False

    async def send(self, frame: bytes) -> None:
        if self.writer.is_closing():
            return
        self.writer.write(frame)
        await self.writer.drain()

    async def close_with(self, code: int, reason: str) -> None:
        if not self.closing:
            self.closing = True
            self.stats.close = f"{code}:{reason}"
            try:
                await self.send(encode_close(code, reason))
            except (ConnectionError, OSError):
                pass

    async def read_frame(self) -> tuple[bool, int, bytes]:
        head = await self.reader.readexactly(2)
        fin = bool(head[0] & 0x80)
        if head[0] & 0x70:
            raise WsProtocolError(CLOSE_PROTOCOL_ERROR, "rsv bits set")
        opcode = head[0] & 0x0F
        masked = bool(head[1] & 0x80)
        length = head[1] & 0x7F
        if length == 126:
            length = struct.unpack("!H", await self.reader.readexactly(2))[0]
        elif length == 127:
            length = struct.unpack("!Q", await self.reader.readexactly(8))[0]
        if not masked:
            raise WsProtocolError(CLOSE_PROTOCOL_ERROR, "client frame not masked")
        if opcode >= 0x8 and (length > 125 or not fin):
            raise WsProtocolError(CLOSE_PROTOCOL_ERROR, "bad control frame")
        if length > UPSTREAM_MAX_FRAME:
            raise WsProtocolError(CLOSE_TOO_BIG, f"ws frame {length}B > {UPSTREAM_MAX_FRAME}B")
        mask = await self.reader.readexactly(4)
        payload = apply_mask(await self.reader.readexactly(length), mask)
        return fin, opcode, payload

    async def pump_upstream(self) -> None:
        """WebSocket -> TCP。"""
        message = bytearray()
        message_opcode = None
        while True:
            fin, opcode, payload = await self.read_frame()
            if opcode == OP_PING:
                await self.send(encode_ws_frame(OP_PONG, payload))
                continue
            if opcode == OP_PONG:
                continue
            if opcode == OP_CLOSE:
                code = struct.unpack("!H", payload[:2])[0] if len(payload) >= 2 else CLOSE_NORMAL
                await self.close_with(CLOSE_NORMAL, f"client closed {code}")
                return
            if opcode == OP_CONT:
                if message_opcode is None:
                    raise WsProtocolError(CLOSE_PROTOCOL_ERROR, "continuation without start")
            elif opcode in (OP_TEXT, OP_BIN):
                if message_opcode is not None:
                    raise WsProtocolError(CLOSE_PROTOCOL_ERROR, "new message before previous finished")
                message_opcode = opcode
            else:
                raise WsProtocolError(CLOSE_PROTOCOL_ERROR, f"unknown opcode {opcode}")
            message += payload
            if len(message) > UPSTREAM_MAX_FRAME:
                raise WsProtocolError(CLOSE_TOO_BIG, f"upstream message > {UPSTREAM_MAX_FRAME}B")
            if not fin:
                continue
            if message_opcode == OP_TEXT:
                raise WsProtocolError(CLOSE_UNSUPPORTED_DATA, "text messages not supported")
            frame = bytes(message)
            message.clear()
            message_opcode = None
            validate_upstream_frame(frame)
            self.tcp_writer.write(frame)
            await self.tcp_writer.drain()
            self.stats.up_frames += 1
            self.stats.up_bytes += len(frame)

    async def pump_downstream(self) -> None:
        """TCP -> WebSocket。TCP 读到 EOF 时先把已切好的帧发完,再发 Close(1000)。"""
        splitter = MuduoFrameSplitter()
        while True:
            data = await self.tcp_reader.read(64 * 1024)
            if not data:
                if splitter.pending:
                    self.stats.decode_errors += 1
                    log.warning("session=%s upstream EOF with %d partial bytes", self.stats.sid, splitter.pending)
                await self.close_with(CLOSE_NORMAL, "server closed")
                return
            for frame in splitter.feed(data):
                await self.send(encode_ws_frame(OP_BIN, frame))
                self.stats.down_frames += 1
                self.stats.down_bytes += len(frame)
                self.stats.max_down_frame = max(self.stats.max_down_frame, len(frame))

    async def pinger(self) -> None:
        while True:
            await asyncio.sleep(self.ping_interval)
            await self.send(encode_ws_frame(OP_PING, b"bridge"))

    async def run(self) -> None:
        tasks = [
            asyncio.create_task(self.pump_upstream(), name="up"),
            asyncio.create_task(self.pump_downstream(), name="down"),
            asyncio.create_task(self.pinger(), name="ping"),
        ]
        try:
            done, _ = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                exc = task.exception()
                if isinstance(exc, WsProtocolError):
                    self.stats.decode_errors += 1
                    await self.close_with(exc.code, exc.reason)
                elif isinstance(exc, asyncio.IncompleteReadError):
                    self.stats.close = self.stats.close or "client vanished"
                elif exc is not None and not isinstance(exc, (ConnectionError, OSError)):
                    log.exception("session=%s unexpected error", self.stats.sid, exc_info=exc)
                    await self.close_with(CLOSE_INTERNAL, "bridge error")
                elif exc is not None:
                    self.stats.close = self.stats.close or f"io error: {exc}"
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            for w in (self.tcp_writer, self.writer):
                w.close()
            for w in (self.tcp_writer, self.writer):
                try:
                    await w.wait_closed()
                except (ConnectionError, OSError):
                    pass


class Bridge:
    def __init__(self, config: BridgeConfig):
        self.config = config
        self.policy = TargetPolicy(config.allow)
        self._ids = itertools.count(1)
        self._server: asyncio.base_events.Server | None = None
        gw = urllib.parse.urlsplit(config.gateway)
        if gw.scheme != "http" or not gw.hostname:
            raise ValueError("--gateway must be http://host:port")
        self._gw_host = gw.hostname
        self._gw_port = gw.port or 80

    async def start(self) -> int:
        self._server = await asyncio.start_server(
            self.handle, self.config.host, self.config.port, limit=MAX_HTTP_HEADER_BYTES + 1024
        )
        return self._server.sockets[0].getsockname()[1]

    async def close(self) -> None:
        if self._server is not None:
            self._server.close()
            await self._server.wait_closed()

    async def handle(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        peer = "%s:%s" % writer.get_extra_info("peername")[:2]
        try:
            request = await read_http_request(reader)
            if request is None:
                return
            if request.path == "/ws":
                await self.handle_ws(request, reader, writer, peer)
                return
            if request.path == "/log":
                await self.handle_log(request, reader, writer, peer)
            elif request.path == "/api" or request.path.startswith("/api/"):
                await self.handle_proxy(request, reader, writer)
            elif request.method == "OPTIONS":
                writer.write(http_response(204, "No Content", headers={
                    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
                    "Access-Control-Allow-Headers": "*"}))
            else:
                await self.handle_static(request, writer)
            await writer.drain()
        except WsProtocolError as exc:
            writer.write(http_response(400, "Bad Request", exc.reason.encode()))
        except (ConnectionError, OSError, asyncio.IncompleteReadError):
            pass
        finally:
            if not writer.is_closing():
                writer.close()

    async def handle_ws(self, request: HttpRequest, reader, writer, peer: str) -> None:
        key = request.header("sec-websocket-key")
        if request.method != "GET" or "websocket" not in request.header("upgrade").lower() or not key:
            writer.write(http_response(400, "Bad Request", b"websocket upgrade required"))
            await writer.drain()
            return
        query = request.query
        try:
            host, port = self.policy.parse(query.get("target", ""))
        except WsProtocolError as exc:
            log.warning("reject ws from %s: %s", peer, exc.reason)
            writer.write(http_response(403, "Forbidden", exc.reason.encode()))
            await writer.drain()
            return
        try:
            tcp_reader, tcp_writer = await asyncio.wait_for(asyncio.open_connection(host, port), timeout=5)
        except (OSError, asyncio.TimeoutError) as exc:
            log.warning("connect %s:%s failed: %s", host, port, exc)
            writer.write(http_response(502, "Bad Gateway", f"connect {host}:{port} failed".encode()))
            await writer.drain()
            return
        writer.write(
            (
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                f"Sec-WebSocket-Accept: {websocket_accept(key)}\r\n\r\n"
            ).encode("latin-1")
        )
        writer.transport.set_write_buffer_limits(high=WS_WRITE_HIGH_WATER)
        await writer.drain()
        stats = SessionStats(next(self._ids), peer, f"{host}:{port}", query.get("label", ""))
        log.info("open %s", stats.summary())
        await WsSession(reader, writer, tcp_reader, tcp_writer, stats, self.config.ping_interval).run()
        log.info("done %s", stats.summary())

    async def handle_log(self, request: HttpRequest, reader, writer, peer: str) -> None:
        length = int(request.header("content-length") or 0)
        if request.method != "POST" or length > MAX_LOG_BODY_BYTES:
            writer.write(http_response(400, "Bad Request", b"POST body <= 64KB"))
            return
        body = await reader.readexactly(length) if length else b""
        label = request.query.get("label", "-")
        stamp = time.strftime("%Y-%m-%d %H:%M:%S")
        with self.config.log_file.open("a", encoding="utf-8") as f:
            for line in body.decode("utf-8", "replace").splitlines() or [""]:
                f.write(f"{stamp}\t{peer}\t{label}\t{line}\n")
        writer.write(http_response(204, "No Content"))

    async def handle_proxy(self, request: HttpRequest, reader, writer) -> None:
        if request.header("transfer-encoding"):
            writer.write(http_response(411, "Length Required"))
            return
        length = int(request.header("content-length") or 0)
        if length > MAX_PROXY_BODY_BYTES:
            writer.write(http_response(413, "Payload Too Large"))
            return
        body = await reader.readexactly(length) if length else b""
        hop = {"connection", "keep-alive", "proxy-connection", "upgrade", "te", "trailer", "host", "content-length"}
        lines = [f"{request.method} {request.target} HTTP/1.1", f"Host: {self._gw_host}:{self._gw_port}",
                 "Connection: close", f"Content-Length: {len(body)}"]
        lines += [f"{k}: {v}" for k, v in request.headers.items() if k not in hop]
        try:
            up_reader, up_writer = await asyncio.wait_for(asyncio.open_connection(self._gw_host, self._gw_port), 5)
        except (OSError, asyncio.TimeoutError):
            writer.write(http_response(502, "Bad Gateway", b"gateway unreachable"))
            return
        try:
            up_writer.write(("\r\n".join(lines) + "\r\n\r\n").encode("latin-1") + body)
            await up_writer.drain()
            response = await asyncio.wait_for(up_reader.read(), timeout=30)
        finally:
            up_writer.close()
        # 网关回包原样转发;加一条 CORS 头,方便直接用浏览器打开 WebGL 包
        head, sep, rest = response.partition(b"\r\n\r\n")
        if sep and b"access-control-allow-origin" not in head.lower():
            head += b"\r\nAccess-Control-Allow-Origin: *"
        writer.write(head + sep + rest)

    async def handle_static(self, request: HttpRequest, writer) -> None:
        root = self.config.static_root
        if root is None or request.method not in ("GET", "HEAD"):
            writer.write(http_response(404, "Not Found"))
            return
        rel = urllib.parse.unquote(request.path).lstrip("/") or "index.html"
        path = (root / rel).resolve()
        if root.resolve() not in path.parents and path != root.resolve() or not path.is_file():
            writer.write(http_response(404, "Not Found"))
            return
        headers = {}
        name = path.name
        if name.endswith(".br"):
            headers["Content-Encoding"] = "br"
            name = name[:-3]
        elif name.endswith(".gz"):
            headers["Content-Encoding"] = "gzip"
            name = name[:-3]
        if name.endswith(".wasm"):
            ctype = "application/wasm"
        elif name.endswith(".js"):
            ctype = "application/javascript"
        else:
            ctype = mimetypes.guess_type(name)[0] or "application/octet-stream"
        headers["Content-Type"] = ctype
        body = path.read_bytes()
        response = http_response(200, "OK", body, headers)
        if request.method == "HEAD":
            response = response[: len(response) - len(body)]
        writer.write(response)


def parse_args(argv: list[str]) -> BridgeConfig:
    parser = argparse.ArgumentParser(description="WebSocket <-> muduo TCP 本机联调桥")
    parser.add_argument("--host", default="127.0.0.1", help="监听地址;真机联调用 0.0.0.0")
    parser.add_argument("--port", type=int, default=9100)
    parser.add_argument("--allow", action="append", default=[],
                        help="额外允许的 target 主机名 / IP / 网段,可重复,例如 192.168.1.0/24")
    parser.add_argument("--gateway", default="http://127.0.0.1:8081", help="/api 反向代理的网关地址")
    parser.add_argument("--static", type=Path, default=None, help="静态文件目录(WebGL 包)")
    parser.add_argument("--log-file", type=Path, default=Path("ws_bridge_client.log"))
    parser.add_argument("--verbose", action="store_true")
    a = parser.parse_args(argv)
    return BridgeConfig(a.host, a.port, a.allow, a.gateway, a.static, a.log_file)


async def main_async(config: BridgeConfig) -> None:
    bridge = Bridge(config)
    port = await bridge.start()
    log.info("ws bridge listening on %s:%s (gateway %s, static %s)", config.host, port, config.gateway, config.static_root)
    try:
        await asyncio.Event().wait()
    finally:
        await bridge.close()


def main(argv: list[str] | None = None) -> int:
    config = parse_args(sys.argv[1:] if argv is None else argv)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        asyncio.run(main_async(config))
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
