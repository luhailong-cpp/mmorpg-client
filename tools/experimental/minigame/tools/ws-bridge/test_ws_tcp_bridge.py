"""ws_tcp_bridge 的单测:只用标准库,运行 `python -I -m unittest discover -s tools/ws-bridge -v`。"""

from __future__ import annotations

import asyncio
import os
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ws_tcp_bridge as wb  # noqa: E402


def muduo_frame(type_name: bytes = b"ClientTokenVerifyRequest", body: bytes = b"\x08\x01") -> bytes:
    name_len = struct.pack("!i", len(type_name) + 1)
    inner = name_len + type_name + b"\x00" + body
    payload = inner + struct.pack("!I", zlib.adler32(inner) & 0xFFFFFFFF)
    return struct.pack("!i", len(payload)) + payload


class FakeTcpServer:
    """假 gate:记下收到的字节,测试可以从它这边往下发数据。"""

    def __init__(self):
        self.received = bytearray()
        self.connected = asyncio.Event()
        self.eof = asyncio.Event()
        self.writer: asyncio.StreamWriter | None = None
        self.connections = 0

    async def start(self) -> int:
        self.server = await asyncio.start_server(self._on_conn, "127.0.0.1", 0)
        return self.server.sockets[0].getsockname()[1]

    async def _on_conn(self, reader, writer):
        self.connections += 1
        self.writer = writer
        self.connected.set()
        try:
            while True:
                data = await reader.read(65536)
                if not data:
                    break
                self.received += data
        except (ConnectionError, OSError):
            pass
        self.eof.set()

    async def wait_for_bytes(self, n: int, timeout: float = 3.0) -> bytes:
        async def poll():
            while len(self.received) < n:
                await asyncio.sleep(0.01)
        await asyncio.wait_for(poll(), timeout)
        return bytes(self.received)

    async def stop(self):
        if self.writer is not None:
            self.writer.close()
        self.server.close()
        await self.server.wait_closed()


class WsClient:
    def __init__(self, port: int):
        self.port = port

    async def connect(self, target: str, label: str = "") -> tuple[int, bytes]:
        self.reader, self.writer = await asyncio.open_connection("127.0.0.1", self.port)
        query = f"target={target}" + (f"&label={label}" if label else "")
        self.key = "dGhlIHNhbXBsZSBub25jZQ=="
        self.writer.write(
            (
                f"GET /ws?{query} HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                f"Sec-WebSocket-Key: {self.key}\r\nSec-WebSocket-Version: 13\r\n\r\n"
            ).encode()
        )
        await self.writer.drain()
        head = await asyncio.wait_for(self.reader.readuntil(b"\r\n\r\n"), 3)
        status = int(head.split(b" ")[1])
        return status, head

    def raw_frame(self, opcode: int, payload: bytes, fin: bool = True, masked: bool = True) -> bytes:
        first = (0x80 if fin else 0) | opcode
        n = len(payload)
        mask_bit = 0x80 if masked else 0
        if n < 126:
            head = bytes([first, mask_bit | n])
        elif n < 1 << 16:
            head = bytes([first, mask_bit | 126]) + struct.pack("!H", n)
        else:
            head = bytes([first, mask_bit | 127]) + struct.pack("!Q", n)
        if not masked:
            return head + payload
        mask = os.urandom(4)
        return head + mask + wb.apply_mask(payload, mask)

    async def send(self, opcode: int, payload: bytes, fin: bool = True, masked: bool = True):
        self.writer.write(self.raw_frame(opcode, payload, fin, masked))
        await self.writer.drain()

    async def recv(self, skip_ping: bool = True, timeout: float = 3.0) -> tuple[int, bytes]:
        while True:
            head = await asyncio.wait_for(self.reader.readexactly(2), timeout)
            self.assert_server_frame(head)
            opcode = head[0] & 0x0F
            n = head[1] & 0x7F
            if n == 126:
                n = struct.unpack("!H", await self.reader.readexactly(2))[0]
            elif n == 127:
                n = struct.unpack("!Q", await self.reader.readexactly(8))[0]
            payload = await asyncio.wait_for(self.reader.readexactly(n), timeout)
            if skip_ping and opcode == wb.OP_PING:
                continue
            return opcode, payload

    @staticmethod
    def assert_server_frame(head: bytes):
        assert head[0] & 0x80, "server frames must be FIN in this bridge"
        assert not head[1] & 0x80, "server frames must not be masked"

    async def expect_close(self) -> int:
        opcode, payload = await self.recv()
        assert opcode == wb.OP_CLOSE, f"expected close, got opcode {opcode}"
        return struct.unpack("!H", payload[:2])[0]

    def close(self):
        self.writer.close()


class BridgeTestBase(unittest.IsolatedAsyncioTestCase):
    ping_interval = 60.0

    async def asyncSetUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.tmpdir = Path(self.tmp.name)
        self.gate = FakeTcpServer()
        self.gate_port = await self.gate.start()
        self.gateway_requests: list[bytes] = []
        self.gateway = await asyncio.start_server(self._fake_gateway, "127.0.0.1", 0)
        gw_port = self.gateway.sockets[0].getsockname()[1]
        config = wb.BridgeConfig(
            host="127.0.0.1", port=0, gateway=f"http://127.0.0.1:{gw_port}", static_root=self.tmpdir,
            log_file=self.tmpdir / "client.log", ping_interval=self.ping_interval,
        )
        self.bridge = wb.Bridge(config)
        self.port = await self.bridge.start()
        self.client = WsClient(self.port)

    async def asyncTearDown(self):
        try:
            self.client.close()
        except AttributeError:
            pass
        await self.bridge.close()
        await self.gate.stop()
        self.gateway.close()
        await self.gateway.wait_closed()
        self.tmp.cleanup()

    async def _fake_gateway(self, reader, writer):
        head = await reader.readuntil(b"\r\n\r\n")
        length = 0
        for line in head.split(b"\r\n"):
            if line.lower().startswith(b"content-length:"):
                length = int(line.split(b":")[1])
        body = await reader.readexactly(length) if length else b""
        self.gateway_requests.append(head + body)
        payload = b'{"servers":[]}'
        writer.write(b"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: %d\r\n\r\n%s" % (len(payload), payload))
        await writer.drain()
        writer.close()

    async def open_ws(self, label: str = "gate"):
        status, _ = await self.client.connect(f"127.0.0.1:{self.gate_port}", label)
        self.assertEqual(101, status)
        await asyncio.wait_for(self.gate.connected.wait(), 3)


class HandshakeTests(BridgeTestBase):
    def test_accept_key_matches_rfc6455_example(self):
        self.assertEqual("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", wb.websocket_accept("dGhlIHNhbXBsZSBub25jZQ=="))

    async def test_upgrade_returns_101_with_accept(self):
        status, head = await self.client.connect(f"127.0.0.1:{self.gate_port}")
        self.assertEqual(101, status)
        self.assertIn(b"Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", head)

    async def test_target_outside_allow_list_is_forbidden_without_connecting(self):
        status, _ = await self.client.connect("8.8.8.8:53")
        self.assertEqual(403, status)
        self.assertEqual(0, self.gate.connections)

    async def test_unreachable_target_returns_502(self):
        probe = await asyncio.start_server(lambda r, w: None, "127.0.0.1", 0)
        dead_port = probe.sockets[0].getsockname()[1]
        probe.close()
        await probe.wait_closed()
        status, _ = await self.client.connect(f"127.0.0.1:{dead_port}")
        self.assertEqual(502, status)


class UpstreamTests(BridgeTestBase):
    async def test_one_message_is_written_verbatim(self):
        await self.open_ws()
        frame = muduo_frame()
        await self.client.send(wb.OP_BIN, frame)
        self.assertEqual(frame, await self.gate.wait_for_bytes(len(frame)))

    async def test_fragmented_message_is_reassembled(self):
        await self.open_ws()
        frame = muduo_frame(body=b"x" * 300)
        await self.client.send(wb.OP_BIN, frame[:100], fin=False)
        await self.client.send(wb.OP_CONT, frame[100:])
        self.assertEqual(frame, await self.gate.wait_for_bytes(len(frame)))

    async def test_oversize_frame_closes_1009_and_writes_nothing(self):
        await self.open_ws()
        frame = muduo_frame(body=b"x" * wb.UPSTREAM_MAX_FRAME)
        await self.client.send(wb.OP_BIN, frame)
        self.assertEqual(wb.CLOSE_TOO_BIG, await self.client.expect_close())
        self.assertEqual(b"", bytes(self.gate.received))

    async def test_length_field_mismatch_closes_1008(self):
        await self.open_ws()
        frame = bytearray(muduo_frame())
        frame[3] += 1
        await self.client.send(wb.OP_BIN, bytes(frame))
        self.assertEqual(wb.CLOSE_POLICY, await self.client.expect_close())
        self.assertEqual(b"", bytes(self.gate.received))

    async def test_text_message_closes_1003(self):
        await self.open_ws()
        await self.client.send(wb.OP_TEXT, b"hello")
        self.assertEqual(wb.CLOSE_UNSUPPORTED_DATA, await self.client.expect_close())

    async def test_unmasked_client_frame_closes_1002(self):
        await self.open_ws()
        await self.client.send(wb.OP_BIN, muduo_frame(), masked=False)
        self.assertEqual(wb.CLOSE_PROTOCOL_ERROR, await self.client.expect_close())

    async def test_ping_is_answered_with_pong(self):
        await self.open_ws()
        await self.client.send(wb.OP_PING, b"hb-1")
        opcode, payload = await self.client.recv()
        self.assertEqual((wb.OP_PONG, b"hb-1"), (opcode, payload))


class DownstreamTests(BridgeTestBase):
    async def test_coalesced_and_split_frames_become_one_message_each(self):
        await self.open_ws()
        a, b, c = muduo_frame(b"A", b"1"), muduo_frame(b"B", b"22"), muduo_frame(b"C", b"333" * 50)
        self.gate.writer.write(a + b + c[:7])
        await self.gate.writer.drain()
        await asyncio.sleep(0.05)
        self.gate.writer.write(c[7:])
        await self.gate.writer.drain()
        got = [await self.client.recv() for _ in range(3)]
        self.assertEqual([(wb.OP_BIN, a), (wb.OP_BIN, b), (wb.OP_BIN, c)], got)

    async def test_large_downstream_frame_is_not_capped_at_64kb(self):
        await self.open_ws()
        big = muduo_frame(b"RankPage", os.urandom(1024 * 1024))
        self.gate.writer.write(big)
        await self.gate.writer.drain()
        opcode, payload = await self.client.recv(timeout=10)
        self.assertEqual(wb.OP_BIN, opcode)
        self.assertEqual(big, payload)

    async def test_bad_downstream_length_closes_1011(self):
        await self.open_ws()
        self.gate.writer.write(struct.pack("!i", 3) + b"abc")
        await self.gate.writer.drain()
        self.assertEqual(wb.CLOSE_INTERNAL, await self.client.expect_close())

    async def test_server_eof_flushes_frames_then_closes_1000(self):
        await self.open_ws()
        frame = muduo_frame(b"KickNotify", b"\x01")
        self.gate.writer.write(frame)
        await self.gate.writer.drain()
        self.gate.writer.close()
        self.assertEqual((wb.OP_BIN, frame), await self.client.recv())
        self.assertEqual(wb.CLOSE_NORMAL, await self.client.expect_close())

    async def test_client_close_closes_tcp(self):
        await self.open_ws()
        await self.client.send(wb.OP_CLOSE, struct.pack("!H", 1000))
        self.assertEqual(wb.CLOSE_NORMAL, await self.client.expect_close())
        # 桥关掉 TCP 后,假 gate 的读循环会读到 EOF
        await asyncio.wait_for(self.gate.eof.wait(), 3)


class PingTests(BridgeTestBase):
    ping_interval = 0.1

    async def test_bridge_sends_periodic_ping(self):
        await self.open_ws()
        opcode, payload = await self.client.recv(skip_ping=False, timeout=2)
        self.assertEqual((wb.OP_PING, b"bridge"), (opcode, payload))


class HttpSideTests(BridgeTestBase):
    async def http(self, raw: bytes) -> bytes:
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(raw)
        await writer.drain()
        data = await asyncio.wait_for(reader.read(), 5)
        writer.close()
        return data

    async def test_api_requests_are_proxied_to_gateway(self):
        body = b'{"account":"robot_1"}'
        resp = await self.http(
            b"POST /api/login HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: %d\r\n\r\n%s"
            % (len(body), body)
        )
        self.assertTrue(resp.startswith(b"HTTP/1.1 200"))
        self.assertIn(b'{"servers":[]}', resp)
        self.assertIn(b"Access-Control-Allow-Origin: *", resp)
        self.assertEqual(1, len(self.gateway_requests))
        self.assertTrue(self.gateway_requests[0].startswith(b"POST /api/login HTTP/1.1"))
        self.assertTrue(self.gateway_requests[0].endswith(body))

    async def test_log_endpoint_appends_lines(self):
        body = "RESULT=PASS stage=in_game\n第二行".encode()
        resp = await self.http(b"POST /log?label=webgl HTTP/1.1\r\nHost: x\r\nContent-Length: %d\r\n\r\n%s" % (len(body), body))
        self.assertTrue(resp.startswith(b"HTTP/1.1 204"))
        lines = (self.tmpdir / "client.log").read_text(encoding="utf-8").splitlines()
        self.assertEqual(2, len(lines))
        self.assertTrue(lines[0].endswith("\twebgl\tRESULT=PASS stage=in_game"))
        self.assertTrue(lines[1].endswith("\twebgl\t第二行"))

    async def test_static_brotli_wasm_gets_encoding_and_type(self):
        (self.tmpdir / "Build").mkdir()
        (self.tmpdir / "Build" / "game.wasm.br").write_bytes(b"\x00br")
        resp = await self.http(b"GET /Build/game.wasm.br HTTP/1.1\r\nHost: x\r\n\r\n")
        head = resp.split(b"\r\n\r\n")[0]
        self.assertIn(b"Content-Encoding: br", head)
        self.assertIn(b"Content-Type: application/wasm", head)
        self.assertTrue(resp.endswith(b"\x00br"))

    async def test_static_path_traversal_is_blocked(self):
        resp = await self.http(b"GET /../../Windows/win.ini HTTP/1.1\r\nHost: x\r\n\r\n")
        self.assertTrue(resp.startswith(b"HTTP/1.1 404"))


class PolicyTests(unittest.TestCase):
    def test_default_allows_only_loopback(self):
        p = wb.TargetPolicy([])
        self.assertEqual(("127.0.0.1", 10000), p.parse("127.0.0.1:10000"))
        self.assertEqual(("::1", 10000), p.parse("[::1]:10000"))
        self.assertEqual(("localhost", 9), p.parse("localhost:9"))
        for bad in ("192.168.1.5:10000", "example.com:80", "127.0.0.1:0", "127.0.0.1:abc", "127.0.0.1"):
            with self.assertRaises(wb.WsProtocolError, msg=bad):
                p.parse(bad)

    def test_allow_adds_lan_network(self):
        p = wb.TargetPolicy(["192.168.1.0/24", "devbox"])
        self.assertEqual(("192.168.1.5", 20100), p.parse("192.168.1.5:20100"))
        self.assertEqual(("devbox", 1), p.parse("devbox:1"))
        with self.assertRaises(wb.WsProtocolError):
            p.parse("192.168.2.5:20100")

    def test_splitter_rejects_short_length(self):
        with self.assertRaises(wb.WsProtocolError):
            wb.MuduoFrameSplitter().feed(struct.pack("!i", 9) + b"x" * 9)


if __name__ == "__main__":
    unittest.main()
