using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RegistrationTool
{
    /// <summary>
    /// 极简 WebSocket 客户端（仅用于报名 EXE 与主服务器 :3002 通讯）。
    /// 2026-05-21 修复 5 条与"主服务器收不到提交"相关的健壮性问题：
    ///   ① Connect 加 5s 超时（原来 TcpClient.Connect 阻塞 UI ~21s）
    ///   ② Send 加 try/catch，返回 bool；写失败立即 MarkDisconnected
    ///   ③ IsConnected 不再依赖陈旧的 _client.Connected，改用 _alive 标志
    ///   ④ ReceiveLoop 收到 PING 帧（opcode 9）自动回 PONG（opcode 10），防服务器空闲超时断开
    ///   ⑤ MarkDisconnected 统一释放资源 + 触发 OnDisconnected（任何路径走丢都能收到）
    /// </summary>
    internal class SimpleWebSocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private volatile bool _alive;
        private readonly object _sendLock = new object();
        private readonly Random _maskRand = new Random();

        public event Action<string> OnMessage;
        public event Action OnDisconnected;

        public bool IsConnected {
            get { return _alive && _client != null && _stream != null; }
        }

        // 兼容旧调用：保留无超时形参的 Connect，内部走 5s 超时
        public void Connect(string host, int port) {
            ConnectWithTimeout(host, port, 5000);
        }

        public void ConnectWithTimeout(string host, int port, int timeoutMs) {
            _client = new TcpClient();
            var ar = _client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, false)) {
                try { _client.Close(); } catch { }
                _client = null;
                throw new TimeoutException("连接超时 (" + timeoutMs + "ms)，请确认主服务器 IP / 端口");
            }
            try { _client.EndConnect(ar); }
            catch {
                try { _client.Close(); } catch { }
                _client = null;
                throw;
            }
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
                throw new Exception("WebSocket 握手失败：服务器未返回 101 升级响应");

            _alive = true;
            var t = new Thread(ReceiveLoop) { IsBackground = true, Name = "WS-Recv" };
            t.Start();
        }

        /// <summary>
        /// 发送文本帧（opcode 1）。返回 false 表示连接已断开 / 写入异常，调用方应当
        /// 立即停止"等待服务器回执"的等待逻辑并提示用户"发送失败"。
        /// </summary>
        public bool Send(string message) {
            if (!IsConnected) return false;
            try {
                WriteFrame(0x81, Encoding.UTF8.GetBytes(message));
                return true;
            } catch {
                MarkDisconnected();
                return false;
            }
        }

        // 内部：构造一个 masked client → server 帧，opcodeByte 已含 FIN(0x80)+opcode。
        // 共用 _sendLock 与 Send 互斥，保证多线程下帧不交错。
        private void WriteFrame(byte opcodeByte, byte[] payload) {
            int len = payload != null ? payload.Length : 0;
            byte[] mask = new byte[4];
            lock (_maskRand) { _maskRand.NextBytes(mask); }
            byte[] header;
            if (len < 126)
                header = new byte[] { opcodeByte, (byte)(0x80 | len), mask[0], mask[1], mask[2], mask[3] };
            else if (len < 65536)
                header = new byte[] { opcodeByte, 0xFE, (byte)(len >> 8), (byte)(len & 0xFF), mask[0], mask[1], mask[2], mask[3] };
            else
                header = new byte[] { opcodeByte, 0xFF, 0, 0, 0, 0, (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)(len & 0xFF), mask[0], mask[1], mask[2], mask[3] };

            byte[] body = new byte[len];
            for (int i = 0; i < len; i++) body[i] = (byte)(payload[i] ^ mask[i % 4]);

            lock (_sendLock) {
                _stream.Write(header, 0, header.Length);
                if (len > 0) _stream.Write(body, 0, body.Length);
            }
        }

        // PING → 立即回 PONG（带原 payload），符合 RFC 6455 §5.5.3。
        private void SendPong(byte[] payload) {
            try { WriteFrame(0x8A, payload ?? new byte[0]); }
            catch { MarkDisconnected(); }
        }

        public void Close() {
            try { if (_stream != null && _alive) WriteFrame(0x88, new byte[0]); } catch { }
            MarkDisconnected();
        }

        private void MarkDisconnected() {
            if (!_alive) return;
            _alive = false;
            try { if (_client != null) _client.Close(); } catch { }
            _client = null; _stream = null;
            var d = OnDisconnected;
            if (d != null) { try { d(); } catch { } }
        }

        private void ReceiveLoop() {
            try {
                while (_alive) {
                    int b0 = _stream.ReadByte(); if (b0 < 0) break;
                    int b1 = _stream.ReadByte(); if (b1 < 0) break;
                    bool masked = (b1 & 0x80) != 0;
                    long length = b1 & 0x7F;
                    if (length == 126) { byte[] ext = ReadExact(2); length = (ext[0] << 8) | ext[1]; }
                    else if (length == 127) { byte[] ext = ReadExact(8); length = 0; for (int i = 0; i < 8; i++) length = (length << 8) | ext[i]; }
                    byte[] maskKey = masked ? ReadExact(4) : null;
                    byte[] data = ReadExact((int)length);
                    if (masked && maskKey != null) for (int i = 0; i < data.Length; i++) data[i] ^= maskKey[i % 4];
                    int opcode = b0 & 0x0F;
                    if (opcode == 8) break;                              // close
                    if (opcode == 9) { SendPong(data); continue; }       // ping → pong
                    if (opcode == 10) continue;                          // pong → 忽略
                    if (opcode == 1 || opcode == 0) {                    // text / continuation
                        string msg = Encoding.UTF8.GetString(data);
                        var h = OnMessage;
                        if (h != null) { try { h(msg); } catch { } }
                    }
                    // 其它 opcode（2 binary 等）当前业务用不到，丢弃
                }
            } catch { }
            MarkDisconnected();
        }

        private byte[] ReadExact(int count) {
            byte[] buf = new byte[count]; int offset = 0;
            while (offset < count) {
                int read = _stream.Read(buf, offset, count - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return buf;
        }
    }
}
