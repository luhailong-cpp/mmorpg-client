using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Google.Protobuf;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class GateTcpClientHeartbeatTests
    {
        [Test]
        public void VerificationStartsHeartbeatWithoutMainThreadPollAndRepliesStayOffBusinessQueue()
        {
            using var pair = new Peer();
            var seen = new List<IMessage>();
            pair.Client.OnMessage += seen.Add;
            pair.SendVerify(50, 1000);
            // 连续回复跨过判死期限：整个窗口不 Poll，模拟主线程长时间加载场景。
            for (ulong seq = 1; seq <= 24; seq++)
            {
                var heartbeat = pair.Read<ClientHeartbeatRequest>();
                Assert.That(heartbeat.Seq, Is.EqualTo(seq));
                pair.Send(new ClientHeartbeatResponse { Seq = seq });
            }
            Assert.That(pair.Client.Connected, Is.True);
            Assert.That(seen, Is.Empty);
            pair.Client.Poll();
            Assert.That(seen.Count, Is.EqualTo(1));
            Assert.That(seen[0], Is.TypeOf<ClientTokenVerifyResponse>());
            Assert.That(pair.Client.HeartbeatRoundTripMs, Is.Not.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnverifiedOrUnannouncedConnectionNeverSendsHeartbeat(bool verify)
        {
            using var pair = new Peer();
            if (verify)
            {
                bool seen = false;
                pair.Client.OnMessage += _ => seen = true;
                pair.SendVerify(0, 0);
                Assert.That(SpinWait.SpinUntil(() => { pair.Client.Poll(); return seen; }, 3000), Is.True);
            }
            pair.Client.Send(new ClientTokenVerifyRequest());
            Assert.That(pair.ReadMessage(), Is.TypeOf<ClientTokenVerifyRequest>(), "哨兵前不得插入心跳");
            Assert.That(GetThread(pair.Client, "_heartbeatThread"), Is.Null);
        }

        [Test]
        public void RejectedVerificationDoesNotStartHeartbeat()
        {
            using var pair = new Peer();
            pair.Send(new ClientTokenVerifyResponse { Success = false, HeartbeatIntervalMs = 50, HeartbeatTimeoutMs = 500 });
            bool seen = false;
            pair.Client.OnMessage += _ => seen = true;
            Assert.That(SpinWait.SpinUntil(() => { pair.Client.Poll(); return seen; }, 3000), Is.True);
            Assert.That(GetThread(pair.Client, "_heartbeatThread"), Is.Null);
        }

        [Test]
        public void SilentGateClosesTransportAndRaisesOneDisconnect()
        {
            using var pair = new Peer();
            var errors = new List<string>();
            int disconnected = 0;
            pair.Client.OnError += errors.Add;
            pair.Client.OnDisconnected += () => disconnected++;
            pair.SendVerify(50, 300);
            Assert.That(pair.Read<ClientHeartbeatRequest>().Seq, Is.EqualTo(1));
            Assert.That(SpinWait.SpinUntil(() => !pair.Client.Connected, 3000), Is.True);
            Assert.That(GetThread(pair.Client, "_readerThread").Join(3000), Is.True);
            pair.Client.Poll(); pair.Client.Poll();
            Assert.That(disconnected, Is.EqualTo(1));
            Assert.That(errors, Is.EqualTo(new[] { "gate heartbeat timed out" }));
        }

        [Test]
        public void IncompleteInboundFrameBytesKeepGateAlive()
        {
            using var pair = new Peer();
            pair.SendVerify(100, 1000);
            var frame = pair.Codec.Encode(new ClientHeartbeatResponse { Seq = 1 });
            // 只给半帧，但每 100ms 的真实字节都刷新接收时间；累计跨过 1s 超时。
            for (int i = 0; i < 15; i++)
            {
                pair.Read<ClientHeartbeatRequest>();
                pair.Stream.Write(frame, i, 1);
            }
            Assert.That(pair.Client.Connected, Is.True);
        }

        [Test]
        public void DisposeEndsAllWorkersAndNewConnectionWaitsForOwnVerification()
        {
            using var old = new Peer();
            old.SendVerify(10000, 30000);
            old.Read<ClientHeartbeatRequest>();
            old.Client.Dispose();
            old.AssertWorkersStopped();
            using var fresh = new Peer();
            fresh.Client.Send(new ClientTokenVerifyRequest());
            Assert.That(fresh.ReadMessage(), Is.TypeOf<ClientTokenVerifyRequest>());
            fresh.SendVerify(10000, 30000);
            Assert.That(fresh.Read<ClientHeartbeatRequest>().Seq, Is.EqualTo(1));
        }

        [Test]
        public void PeerEofWakesLongHeartbeatWait()
        {
            using var pair = new Peer();
            pair.SendVerify(10000, 30000);
            pair.Read<ClientHeartbeatRequest>();
            pair.Remote.Client.Shutdown(SocketShutdown.Send);
            pair.AssertWorkersStopped();
        }

        private static Thread GetThread(GateTcpClient client, string name) =>
            (Thread)typeof(GateTcpClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client);

        private sealed class Peer : IDisposable
        {
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            public readonly MuduoCodec Codec = new();
            public readonly GateTcpClient Client;
            public readonly TcpClient Remote;
            public NetworkStream Stream => Remote.GetStream();
            public Peer()
            {
                Codec.Register<ClientTokenVerifyRequest>(); Codec.Register<ClientTokenVerifyResponse>();
                Codec.Register<ClientHeartbeatRequest>(); Codec.Register<ClientHeartbeatResponse>();
                _listener.Start();
                Client = new GateTcpClient(Codec);
                Client.Connect("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);
                Remote = _listener.AcceptTcpClient();
                Stream.ReadTimeout = 3000;
            }
            public void SendVerify(uint interval, uint timeout) => Send(new ClientTokenVerifyResponse
            { Success = true, HeartbeatIntervalMs = interval, HeartbeatTimeoutMs = timeout });
            public void Send(IMessage message) { var bytes = Codec.Encode(message); Stream.Write(bytes, 0, bytes.Length); }
            public T Read<T>() where T : class, IMessage { var message = ReadMessage(); Assert.That(message, Is.TypeOf<T>()); return (T)message; }
            public IMessage ReadMessage()
            {
                var header = ReadExactly(4);
                int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                Assert.That(length, Is.InRange(1, 65536));
                var frame = new byte[length + 4];
                Buffer.BlockCopy(header, 0, frame, 0, 4);
                Buffer.BlockCopy(ReadExactly(length), 0, frame, 4, length);
                return Codec.TryDecode(frame, 0, frame.Length, out _);
            }
            private byte[] ReadExactly(int count)
            {
                byte[] bytes = new byte[count];
                for (int offset = 0; offset < count;)
                {
                    int received = Stream.Read(bytes, offset, count - offset);
                    if (received == 0) throw new EndOfStreamException();
                    offset += received;
                }
                return bytes;
            }
            public void AssertWorkersStopped()
            {
                foreach (string name in new[] { "_readerThread", "_writerThread", "_heartbeatThread" })
                    Assert.That(GetThread(Client, name)?.Join(3000) ?? true, Is.True, name);
            }
            public void Dispose() { Client.Dispose(); AssertWorkersStopped(); Remote.Dispose(); _listener.Stop(); }
        }
    }
}
