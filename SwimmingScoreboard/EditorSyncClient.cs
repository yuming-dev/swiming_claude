using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SwimmingScoreboard
{
    // 编排 EXE → 主服务器 的轻量 WebSocket 客户端。
    // 仅用于"编排记录及成绩处理"模式下的 CompetitionPackage 双向同步：
    //   · 连上后：发 EDITOR_IDENTITY → 服务器推送当前 EDITOR_PACKAGE → 编排端覆盖本地并加载
    //   · 编排端 AutoSaveData 之后：发 EDITOR_PUSH_PACKAGE → 服务器覆盖本地数据 + 广播给其它客户端
    //   · 主服务器 AutoSaveData 之后：服务器推 EDITOR_PACKAGE → 编排端覆盖本地
    // 协议基于 Fleck（主服务器端口 3002），握手 + 帧格式与浏览器 WebSocket 一致。
    public class EditorSyncClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private volatile bool _running;
        private readonly object _writeLock = new object();
        private Thread _receiveThread;

        public event Action<string> OnMessage;            // 收到完整文本帧
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnLog;

        public bool IsConnected {
            get { return _client != null && _client.Connected && _stream != null; }
        }

        public string Host { get; private set; }
        public int Port { get; private set; }

        public void Connect(string host, int port) {
            Disconnect();
            Host = host;
            Port = port;
            try {
                _client = new TcpClient();
                _client.Connect(host, port);
                _stream = _client.GetStream();

                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                string handshake = string.Format(
                    "GET / HTTP/1.1\r\nHost: {0}:{1}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {2}\r\nSec-WebSocket-Version: 13\r\n\r\n",
                    host, port, key);
                byte[] req = Encoding.UTF8.GetBytes(handshake);
                _stream.Write(req, 0, req.Length);

                var sb = new StringBuilder();
                while (true) {
                    int b = _stream.ReadByte();
                    if (b < 0) break;
                    sb.Append((char)b);
                    if (sb.Length >= 4 && sb.ToString(sb.Length - 4, 4) == "\r\n\r\n") break;
                }
                if (!sb.ToString().Contains("101"))
                    throw new Exception("WebSocket 握手失败");

                _running = true;
                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "EditorSync-Recv" };
                _receiveThread.Start();

                RaiseLog("已连接主服务器 ws://" + host + ":" + port);
                Action h = OnConnected;
                if (h != null) h();
            } catch (Exception ex) {
                RaiseLog("连接主服务器失败: " + ex.Message);
                Disconnect();
                throw;
            }
        }

        public void Disconnect() {
            _running = false;
            try { if (_stream != null) _stream.Write(new byte[] { 0x88, 0x80, 0, 0, 0, 0 }, 0, 6); } catch { }
            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        public void Send(string message) {
            if (!IsConnected) return;
            byte[] payload = Encoding.UTF8.GetBytes(message);
            byte[] mask = new byte[4];
            new Random().NextBytes(mask);
            byte[] header;
            if (payload.Length < 126) {
                header = new byte[] { 0x81, (byte)(0x80 | payload.Length), mask[0], mask[1], mask[2], mask[3] };
            } else if (payload.Length < 65536) {
                header = new byte[] { 0x81, 0xFE, (byte)(payload.Length >> 8), (byte)(payload.Length & 0xFF),
                                      mask[0], mask[1], mask[2], mask[3] };
            } else {
                long len = payload.Length;
                header = new byte[] {
                    0x81, 0xFF,
                    0, 0, 0, 0,
                    (byte)((len >> 24) & 0xFF), (byte)((len >> 16) & 0xFF),
                    (byte)((len >> 8) & 0xFF),  (byte)(len & 0xFF),
                    mask[0], mask[1], mask[2], mask[3]
                };
            }

            byte[] masked = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);

            try {
                lock (_writeLock) {
                    _stream.Write(header, 0, header.Length);
                    _stream.Write(masked, 0, masked.Length);
                    _stream.Flush();
                }
            } catch (Exception ex) {
                RaiseLog("发送同步消息失败: " + ex.Message);
                Disconnect();
            }
        }

        private void ReceiveLoop() {
            try {
                var assembled = new List<byte>();   // 大包可能被拆成多帧（continuation）
                while (_running && _client != null && _client.Connected && _stream != null) {
                    int b0 = _stream.ReadByte(); if (b0 < 0) break;
                    int b1 = _stream.ReadByte(); if (b1 < 0) break;
                    bool fin = (b0 & 0x80) != 0;
                    int opcode = b0 & 0x0F;
                    bool masked = (b1 & 0x80) != 0;
                    long length = b1 & 0x7F;
                    if (length == 126) {
                        byte[] ext = ReadExact(2);
                        length = ((long)ext[0] << 8) | ext[1];
                    } else if (length == 127) {
                        byte[] ext = ReadExact(8);
                        length = 0;
                        for (int i = 0; i < 8; i++) length = (length << 8) | ext[i];
                    }
                    byte[] maskKey = masked ? ReadExact(4) : null;
                    byte[] data = ReadExact((int)length);
                    if (masked && maskKey != null) {
                        for (int i = 0; i < data.Length; i++) data[i] ^= maskKey[i % 4];
                    }
                    if (opcode == 0x8) break;                      // close
                    if (opcode == 0x9) { TrySendPong(data); continue; }   // ping → pong
                    if (opcode == 0xA) continue;                   // pong

                    // 文本/二进制 / 续帧：累积直到 fin
                    if (opcode == 0x1 || opcode == 0x2 || opcode == 0x0) {
                        for (int i = 0; i < data.Length; i++) assembled.Add(data[i]);
                        if (fin) {
                            string msg = Encoding.UTF8.GetString(assembled.ToArray());
                            assembled.Clear();
                            Action<string> h = OnMessage;
                            if (h != null) {
                                try { h(msg); } catch (Exception ex) { RaiseLog("处理同步消息异常: " + ex.Message); }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                if (_running) RaiseLog("同步接收异常: " + ex.Message);
            }
            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _stream = null;
            _client = null;
            Action d = OnDisconnected;
            if (d != null) d();
        }

        private void TrySendPong(byte[] data) {
            // 简化 pong：仅 header，不带 payload。Fleck 不会强校验。
            try {
                lock (_writeLock) {
                    _stream.Write(new byte[] { 0x8A, 0x80, 0, 0, 0, 0 }, 0, 6);
                    _stream.Flush();
                }
            } catch { }
        }

        private byte[] ReadExact(int count) {
            byte[] buf = new byte[count];
            int offset = 0;
            while (offset < count) {
                int read = _stream.Read(buf, offset, count - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return buf;
        }

        private void RaiseLog(string msg) {
            Action<string> h = OnLog;
            if (h != null) h(msg);
        }
    }
}
