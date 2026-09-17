using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class GateTcpClientLifecycleTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void DisposeDuringIo_EndsWorkerWithoutEscapingException(bool reader)
        {
            using var pair = new LoopbackPair();
            using var stream = new PausedNetworkStream(pair.Local.Client);
            var client = CreateClient(pair, stream);
            var errors = new List<string>();
            int disconnected = 0;
            client.OnError += errors.Add;
            client.OnDisconnected += () => disconnected++;
            using var worker = new LoopWorker(client, reader);
            if (!reader) Outbox(client).Add(new byte[] { 0 });
            Assert.That(stream.Entered.Wait(3000), Is.True, "生产循环必须先进入实际 TCP I/O 边界");
            client.Dispose();
            stream.Proceed.Set();
            Assert.That(worker.Join(), Is.True, "主动关闭后后台循环未结束");
            Assert.That(worker.Escaped, Is.Null, "主动关闭造成的 I/O 异常不得逃出生产循环");
            client.Poll();
            Assert.That(errors, Is.Empty);
            Assert.That(disconnected, Is.Zero, "Dispose 后旧连接不能再派发事件");
            Assert.That(client.Connected, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void IoFailureWhileRunning_IsReported(bool reader)
        {
            using var pair = new LoopbackPair();
            using var stream = new PausedNetworkStream(pair.Local.Client);
            var client = CreateClient(pair, stream);
            var errors = new List<string>();
            int disconnected = 0;
            client.OnError += errors.Add;
            client.OnDisconnected += () => disconnected++;
            using var worker = new LoopWorker(client, reader);
            if (!reader) Outbox(client).Add(new byte[] { 0 });
            Assert.That(stream.Entered.Wait(3000), Is.True);
            // 运行标志仍为 true；关闭真实流制造 I/O 故障，不能被当作正常 Dispose 吞掉。
            stream.Close();
            stream.Proceed.Set();
            Assert.That(worker.Join(), Is.True);
            Assert.That(worker.Escaped, Is.Null);
            client.Poll();
            client.Poll();
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.StartWith(reader ? "reader: " : "writer: "));
            Assert.That(disconnected, Is.EqualTo(reader ? 1 : 0), "断线哨兵仍由 reader 产生且只派发一次");
            Assert.That(client.Connected, Is.False);
            client.Dispose();
        }

        [Test]
        public void MalformedFrame_ReportsDecodeErrorAndOneDisconnect()
        {
            using var pair = new LoopbackPair();
            var client = CreateClient(pair, pair.Local.GetStream());
            var errors = new List<string>();
            int disconnected = 0;
            client.OnError += errors.Add;
            client.OnDisconnected += () => disconnected++;
            using var worker = new LoopWorker(client, true);
            // 只测试线协议拒绝：通过本地 TCP 发送非法长度帧，不伪造业务响应。
            pair.Remote.GetStream().Write(new byte[14], 0, 14);
            Assert.That(worker.Join(), Is.True);
            Assert.That(worker.Escaped, Is.Null);
            client.Poll();
            client.Poll();
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("decode error: "));
            Assert.That(disconnected, Is.EqualTo(1));
            client.Dispose();
        }

        [Test]
        public void PeerEof_EmitsOneDisconnectWithoutError()
        {
            using var pair = new LoopbackPair();
            var client = CreateClient(pair, pair.Local.GetStream());
            var errors = new List<string>();
            int disconnected = 0;
            client.OnError += errors.Add;
            client.OnDisconnected += () => disconnected++;
            using var worker = new LoopWorker(client, true);
            pair.Remote.Client.Shutdown(SocketShutdown.Send);
            Assert.That(worker.Join(), Is.True);
            Assert.That(worker.Escaped, Is.Null);
            client.Poll();
            client.Poll();
            Assert.That(errors, Is.Empty);
            Assert.That(disconnected, Is.EqualTo(1));
            client.Dispose();
        }

        [Test]
        public void PeerEof_AlsoWakesIdleWriter()
        {
            using var pair = new LoopbackPair();
            var client = CreateClient(pair, pair.Local.GetStream());
            using var reader = new LoopWorker(client, true);
            using var writer = new LoopWorker(client, false);
            pair.Remote.Client.Shutdown(SocketShutdown.Send);
            Assert.That(reader.Join(), Is.True);
            Assert.That(writer.Join(), Is.True, "Reader 结束应唤醒仍在空 outbox 等待的 Writer");
            Assert.That(reader.Escaped, Is.Null);
            Assert.That(writer.Escaped, Is.Null);
        }

        [Test]
        public void WriteFailure_ClosesSocketToWakeReaderAndDisconnectsOnce()
        {
            using var pair = new LoopbackPair();
            using var stream = new PausedNetworkStream(pair.Local.Client) { PauseRead = false };
            var client = CreateClient(pair, stream);
            var errors = new List<string>();
            int disconnected = 0;
            client.OnError += errors.Add;
            client.OnDisconnected += () => disconnected++;
            using var reader = new LoopWorker(client, true);
            using var writer = new LoopWorker(client, false);
            Assert.That(stream.ReadEntered.Wait(3000), Is.True);
            Outbox(client).Add(new byte[] { 0 });
            Assert.That(stream.Entered.Wait(3000), Is.True);
            // 只关闭真实 Socket 的发送方向：写会失败，接收仍阻塞，必须由生产清理将其唤醒。
            pair.Local.Client.Shutdown(SocketShutdown.Send);
            stream.Proceed.Set();
            Assert.That(writer.Join(), Is.True);
            Assert.That(reader.Join(), Is.True, "Writer 故障应关闭连接以唤醒 Reader");
            Assert.That(writer.Escaped, Is.Null);
            Assert.That(reader.Escaped, Is.Null);
            client.Poll();
            client.Poll();
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("writer: "));
            Assert.That(disconnected, Is.EqualTo(1));
            Assert.That(client.Connected, Is.False);
        }

        // 仅用反射接入已有线程循环，捕获红版本逃逸异常以免杀死测试宿主；不增加生产测试接口。
        private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static void Set(GateTcpClient client, string field, object value) => typeof(GateTcpClient).GetField(field, PrivateInstance).SetValue(client, value);
        private static BlockingCollection<byte[]> Outbox(GateTcpClient client) => (BlockingCollection<byte[]>)typeof(GateTcpClient).GetField("_outbox", PrivateInstance).GetValue(client);
        private static GateTcpClient CreateClient(LoopbackPair pair, NetworkStream stream)
        {
            var client = new GateTcpClient(new MuduoCodec());
            Set(client, "_tcp", pair.Local);
            Set(client, "_stream", stream);
            Set(client, "_running", true);
            return client;
        }
        private sealed class LoopbackPair : IDisposable
        {
            private readonly TcpListener _listener;
            public readonly TcpClient Local, Remote;
            public LoopbackPair()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Local = new TcpClient();
                Local.Connect(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
                Remote = _listener.AcceptTcpClient();
            }
            public void Dispose() { Local.Dispose(); Remote.Dispose(); _listener.Stop(); }
        }
        private sealed class PausedNetworkStream : NetworkStream
        {
            public readonly ManualResetEventSlim Entered = new(false), Proceed = new(false), ReadEntered = new(false);
            public bool PauseRead = true;
            public PausedNetworkStream(Socket socket) : base(socket, false) { }
            private void BeforeIo()
            {
                Entered.Set();
                if (!Proceed.Wait(3000)) throw new TimeoutException("测试未释放 TCP I/O 边界");
            }
            public override int Read(byte[] buffer, int offset, int count) { ReadEntered.Set(); if (PauseRead) BeforeIo(); return base.Read(buffer, offset, count); }
            public override void Write(byte[] buffer, int offset, int count) { BeforeIo(); base.Write(buffer, offset, count); }
        }
        private sealed class LoopWorker : IDisposable
        {
            private readonly Thread _thread;
            private readonly GateTcpClient _client;
            public Exception Escaped;
            public LoopWorker(GateTcpClient client, bool reader)
            {
                _client = client;
                var method = typeof(GateTcpClient).GetMethod(reader ? "ReaderLoop" : "WriterLoop", PrivateInstance);
                _thread = new Thread(() =>
                {
                    try { method.Invoke(client, null); }
                    catch (TargetInvocationException ex) { Escaped = ex.InnerException; }
                    catch (Exception ex) { Escaped = ex; }
                }) { IsBackground = true };
                _thread.Start();
            }
            public bool Join() => _thread.Join(3000);
            public void Dispose() { _client.Dispose(); _thread.Join(3500); }
        }
    }
}
