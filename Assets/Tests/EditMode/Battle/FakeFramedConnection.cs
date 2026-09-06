using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Net;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="IFramedConnection"/> 假实现,语义刻意贴着 <see cref="GateTcpClient"/>:
    ///  - Connect 同步完成(或按 <see cref="ConnectError"/> 抛错);
    ///  - Send 在未连接时抛 InvalidOperationException;
    ///  - 入站按投喂顺序在 Poll 内派发,断线标记排在它之前的消息之后到达
    ///    (真实 ReaderLoop 先把帧入队,再入队断线哨兵);
    ///  - Dispose 之后 Poll 不再派发任何东西。
    /// 替身比真实现宽松会藏 P0,所以这四条不许放宽。
    /// </summary>
    internal sealed class FakeFramedConnection : IFramedConnection
    {
        private sealed class DisconnectMarker { public static readonly DisconnectMarker Instance = new(); }

        private readonly Queue<object> _inbox = new();

        public string Host;
        public int Port;
        public int ConnectCalls;
        public Exception ConnectError;
        public bool Disposed;

        public readonly List<IMessage> Sent = new();

        public bool Connected { get; private set; }

        public event Action<IMessage> OnMessage;
        public event Action<string> OnError;
        public event Action OnDisconnected;

        public void Connect(string host, int port)
        {
            ConnectCalls++;
            if (ConnectError != null) throw ConnectError;
            Host = host;
            Port = port;
            Connected = true;
        }

        public void Send(IMessage message)
        {
            if (!Connected) throw new InvalidOperationException("not connected");
            Sent.Add(message);
        }

        public void Poll()
        {
            if (Disposed) return;
            while (_inbox.Count > 0)
            {
                var item = _inbox.Dequeue();
                if (item is DisconnectMarker)
                {
                    Connected = false;
                    OnDisconnected?.Invoke();
                    continue;
                }
                OnMessage?.Invoke((IMessage)item);
            }
        }

        public void Dispose()
        {
            Disposed = true;
            Connected = false;
        }

        /// <summary>模拟服务端发来一帧(下次 Poll 派发)。</summary>
        public void PushInbound(IMessage message) => _inbox.Enqueue(message);

        /// <summary>模拟对端 FIN / 读线程退出(下次 Poll 派发,排在之前投喂的消息之后)。</summary>
        public void PushDisconnect() => _inbox.Enqueue(DisconnectMarker.Instance);

        public void RaiseError(string error) => OnError?.Invoke(error);

        /// <summary>最近一条发出的指定类型消息;没有则 null。</summary>
        public T LastSent<T>() where T : class, IMessage
        {
            for (int i = Sent.Count - 1; i >= 0; i--)
                if (Sent[i] is T t) return t;
            return null;
        }

        public int CountSent<T>() where T : class, IMessage
        {
            int n = 0;
            foreach (var m in Sent) if (m is T) n++;
            return n;
        }
    }
}
